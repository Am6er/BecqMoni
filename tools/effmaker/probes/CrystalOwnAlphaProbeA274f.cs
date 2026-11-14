using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace CrystalOwnAlphaProbeA274f
{
    /// <summary>
    /// (`AMBER274` Э3, полоса `a274f` 14.11.2026) АЛЬФЫ СЛОЯ КРИСТАЛЛА ПО ПОЛЯМ ПРИБОРА: источник QF
    /// (`EfficiencyConfigData.AlphaQuenchingSource`), форма групп альф (`AlphaGroupShape`), активность ряда с датой
    /// (`CrystalSeriesActivity`), признак годности счёта по альфам (узкая линия в окне альф). Прибор — XML из КОПИИ
    /// конфигурации пользователя (`--device=`); поля формы и ряда можно задать ключами поверх файла (файл не пишется).
    ///
    ///   `--mode=layer` — разделы слоя прибора: источник пуст (обязан побитно равняться разделу умолчания), альфы без
    ///       формы, альфы с формой; сверки (бета-часть та же, сумма альф на распад та же, число альф на распад
    ///       головы), `layer.csv`, выборка событий альф `samples.csv` и фотонная шкала `scale.csv` — замеру формы
    ///       вне пробы; контроль ядра формы на дельте.
    ///   `--mode=count --spectra=list.csv` — счёт ряда по альфам на спектрах списка
    ///       (`метка;файл отсчётов;живое время;c0 c1 c2 c3 c4;FWHM % на 662;верх;[дата yyyy-MM-dd]`) разделом с формой
    ///       и без формы: активность, подложка, χ², узкая линия, годность, хвост данных выше окна; с полем ряда
    ///       (`--series=`) и датой — вход разбора `FixSeriesByAlphas` с запасным путём.
    ///   `--mode=wire` — положительные контроли проводки: поля в XML, рецепт прибора, клеймо, отказ чужого рецепта,
    ///       привязка, распад поля ряда.
    ///   `--mode=fsa --spectra=list.csv --tags=S11,… --chains=…` — разбор спектров списка анализатором по малой
    ///       матрице сцены прибора (ЦП): слой ВЫКЛ, слой без альф, альфы без формы, альфы с формой; окно линии
    ///       `--line=2614.511`.
    ///
    ///   `--mode=count --xml=спектр.xml[,…]` — то же на спектрах-файлах приложения (их шкала и FWHM) входом разбора
    ///       `FixSeriesByAlphas`; `--scene38` — сцена LaBr3 ⌀38×38 проб слоя (`a274g`…`a274m`) вместо сцены прибора,
    ///       источник QF, форма и ряд — прибора.
    ///
    ///     CrystalOwnAlphaProbeA274f --mode=… --device=прибор.xml --out=каталог [--histories=200000] [--bin=2]
    ///         [--shape=доб.FWHM,доля полки,длина полки] [--series=Бк,yyyy-MM-dd] [--spectra=list.csv] [--tags=…]
    ///         [--chains=Th-232] [--nuclides=…] [--line=2614.511] [--samples=200000]
    /// </summary>
    internal static class Program
    {
        static int bad;

        static void Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "СОШЛОСЬ     " : "НЕ СОШЛОСЬ  ") + what);
            if (!ok)
            {
                bad++;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string mode = "layer", outDir = null, spectra = null, devicePath = null, shape = null, series = null, tags = null, xml = null;
            bool scene38 = false;
            var chains = new List<string>();
            var nuclides = new List<string>();
            int histories = 200000, samples = 200000;
            double bin = 2.0, line = 2614.511;
            foreach (string a in args)
            {
                string v = a.Contains("=") ? a.Substring(a.IndexOf('=') + 1) : "";
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = v;
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = v;
                else if (a.StartsWith("--device=", StringComparison.Ordinal)) devicePath = v;
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--samples=", StringComparison.Ordinal)) samples = int.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) bin = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--line=", StringComparison.Ordinal)) line = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--shape=", StringComparison.Ordinal)) shape = v;
                else if (a.StartsWith("--series=", StringComparison.Ordinal)) series = v;
                else if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectra = v;
                else if (a.StartsWith("--tags=", StringComparison.Ordinal)) tags = v;
                else if (a.StartsWith("--xml=", StringComparison.Ordinal)) xml = v;
                else if (a == "--scene38") scene38 = true;
                else if (a.StartsWith("--chains=", StringComparison.Ordinal)) chains.AddRange(v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal)) nuclides.AddRange(v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else { Console.Error.WriteLine("ключ не понят: " + a); return 2; }
            }

            if (outDir == null || devicePath == null || !File.Exists(devicePath))
            {
                Console.Error.WriteLine("нужны --out= и --device=<XML прибора в копии конфигурации>");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            DeviceConfigInfo device;
            using (var stream = File.OpenRead(devicePath))
            {
                device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(stream);
            }

            EfficiencyConfigData eff = device.ActiveEfficiency
                                       ?? (device.EfficiencyConfigs != null && device.EfficiencyConfigs.Count > 0 ? device.EfficiencyConfigs[0] : null);
            if (eff == null || eff.Geometry == null)
            {
                Console.Error.WriteLine("у прибора нет конфигурации эффективности с геометрией");
                return 3;
            }

            eff = eff.Copy();
            Console.WriteLine("прибор: «" + device.Name + "», конфигурация «" + eff.Name + "»; источник QF в файле: «" + eff.AlphaQuenchingSource
                              + "»; форма в файле: " + ShapeText(eff.AlphaGroupShape) + "; ряд в файле: " + SeriesText(eff.CrystalSeriesActivity));
            if (shape != null)
            {
                double[] p = shape.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                eff.AlphaGroupShape = new AlphaGroupShape { ExtraFwhmShare = p[0], ShelfShare = p[1], ShelfLength = p[2], Origin = "ключ пробы" };
            }

            if (series != null)
            {
                string[] p = series.Split(',');
                eff.CrystalSeriesActivity = new CrystalSeriesActivity
                {
                    ActivityBq = double.Parse(p[0], CultureInfo.InvariantCulture),
                    Date = DateTime.ParseExact(p[1], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Origin = "ключ пробы",
                };
            }

            if (scene38)
            {
                eff.Geometry = new GeometryModel
                {
                    Name = "LaBr3 38x38 (AMBER274)",
                    IsScintillator = true,
                    Shape = CrystalShape.Cylinder,
                    CrystalDiameter = 38.0,
                    CrystalHeight = 38.0,
                    Crystal = Material("Lanthanum bromide"),
                    Reflector = Material("Polytetrafluoroethylene"),
                    Cladding = Material("Aluminum"),
                    Gap = Material("Air, dry"),
                    FrontReflectorThickness = 1.0,
                    SideReflectorThickness = 1.5,
                    FrontCladdingThickness = 1.0,
                    SideCladdingThickness = 1.0,
                    FrontGapThickness = 0.0,
                    SideGapThickness = 0.0,
                    MountingThickness = 0.0,
                };
            }

            GeometryModel g = eff.Geometry;
            Console.WriteLine("сцена: кристалл " + g.Crystal.Name + " ⌀" + F(g.CrystalDiameter) + "×" + F(g.CrystalHeight) + " мм, вещество шкалы «"
                              + EfficiencySimulator.ScintillatorNameOf(g) + "»; форма: " + ShapeText(eff.AlphaGroupShape) + "; ряд: "
                              + SeriesText(eff.CrystalSeriesActivity));
            int code = mode == "layer" ? Layer(eff, histories, samples, bin, outDir)
                     : mode == "count" && xml != null ? CountXml(eff, histories, bin, xml, outDir)
                     : mode == "count" ? Count(eff, histories, bin, spectra, outDir)
                     : mode == "wire" ? Wire(eff, device, outDir)
                     : mode == "fsa" ? Fsa(eff, spectra, tags, chains, nuclides, line, outDir)
                     : Fail("режим не понят: " + mode);
            if (code != 0)
            {
                return code;
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static string ShapeText(AlphaGroupShape s)
        {
            return s == null ? "нет" : "доб. FWHM " + R(s.ExtraFwhmShare) + ", полка " + R(s.ShelfShare) + " × " + R(s.ShelfLength);
        }

        static string SeriesText(CrystalSeriesActivity s)
        {
            return s == null ? "нет" : R(s.ActivityBq) + " Бк на " + s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        static EfficiencyConfigData Without(EfficiencyConfigData eff, bool source, bool shape)
        {
            EfficiencyConfigData copy = eff.Copy();
            if (source)
            {
                copy.AlphaQuenchingSource = "";
            }

            if (shape)
            {
                copy.AlphaGroupShape = null;
            }

            return copy;
        }

        // ------------------------------------------------------------------------------------------------------
        static int Layer(EfficiencyConfigData eff, int histories, int samples, double bin, string outDir)
        {
            GeometryModel g = eff.Geometry;
            string n0, n1, n2, note;
            CrystalOwnParts r0 = ResponseCrystalSection.RecipeFor(g, Without(eff, true, false), out n0);
            CrystalOwnParts r1 = ResponseCrystalSection.RecipeFor(g, Without(eff, false, true), out n1);
            CrystalOwnParts r2 = ResponseCrystalSection.RecipeFor(g, eff, out n2);
            Console.WriteLine("рецепт, источник пуст : " + CrystalOwnActivityBuilder.RecipeStamp(r0) + (n0 != null ? " — " + n0 : ""));
            Console.WriteLine("рецепт, альфы без формы: " + CrystalOwnActivityBuilder.RecipeStamp(r1) + (n1 != null ? " — " + n1 : ""));
            Console.WriteLine("рецепт прибора         : " + CrystalOwnActivityBuilder.RecipeStamp(r2) + (n2 != null ? " — " + n2 : ""));
            Check("источник пуст: рецепт умолчания (клеймо рецепта то же, альф нет)",
                  !r0.HasAlpha && CrystalOwnActivityBuilder.RecipeStamp(r0) == CrystalOwnActivityBuilder.RecipeStamp(null));
            Check("источник назван: альфы есть, QF рецепта = закону таблицы", r1.HasAlpha && r2.HasAlpha);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            ResponseCrystalSection sDefault = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274, out note);
            ResponseCrystalSection s0 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274, r0, out note);
            Check("источник пуст: раздел побитно равен разделу умолчания (клеймо, записи, гистограммы)", SameSection(sDefault, s0));
            ResponseCrystalSection s1 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274, r1, out note);
            ResponseCrystalSection s2 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274, r2, out note);
            Console.WriteLine("разделы собраны за " + watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " с; примечание: " + note);
            Console.WriteLine("клеймо раздела прибора: " + s2.Stamp);

            CrystalOwnEntry la = sDefault.Find("138LA", "beta");
            CrystalOwnEntry ac0 = sDefault.Find("227AC", "beta"), ac1 = s1.Find("227AC", "beta"), ac2 = s2.Find("227AC", "beta");
            if (la == null || ac0 == null || ac1 == null || ac2 == null || ac1.AlphaPerDecay == null || ac2.AlphaPerDecay == null)
            {
                return Fail("в разделе нет La-138 или ряда с альфами");
            }

            Console.WriteLine("La-138 по массе: " + la.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) + " Бк");
            double a1 = ac1.AlphaPerDecay.Sum(), a2 = ac2.AlphaPerDecay.Sum();
            Console.WriteLine("альф на распад головы: без формы " + a1.ToString("F6", CultureInfo.InvariantCulture) + ", с формой " + a2.ToString("F6", CultureInfo.InvariantCulture));
            Check("форма сохраняет число альф на распад (|Δ| < 1e-9)", Math.Abs(a1 - a2) < 1e-9);
            double worst01 = 0.0, worst12 = 0.0;
            for (int i = 0; i < ac1.PerDecay.Length; i++)
            {
                double beta1 = ac1.PerDecay[i] - ac1.AlphaPerDecay[i], beta2 = ac2.PerDecay[i] - ac2.AlphaPerDecay[i];
                worst12 = Math.Max(worst12, Math.Abs(beta1 - beta2));
                worst01 = Math.Max(worst01, Math.Abs(beta1 - (i < ac0.PerDecay.Length ? ac0.PerDecay[i] : 0.0)));
            }

            Check("бета-часть ряда: с формой = без формы (макс |Δ| " + worst12.ToString("E1", CultureInfo.InvariantCulture) + ")", worst12 < 1e-12);
            Check("бета-часть ряда с альфами = ряду без альф (макс |Δ| " + worst01.ToString("E1", CultureInfo.InvariantCulture) + ")", worst01 < 1e-12);

            // число альф на распад головы — по выходам строк базы (нормировка на долю моды, `a274m`)
            var notes = new List<string>();
            List<KeyValuePair<double, CrystalOwnActivityBuilder.AlphaSample>> list =
                CrystalOwnActivityBuilder.SampleSeriesAlphas(g, samples, 0x2741, notes);
            double exact = 0.0;
            foreach (string nucid in new[] { "227AC", "227TH", "223RA", "219RN", "215PO", "211BI" })
            {
                double sum = CrystalOwnActivityBuilder.AlphaLines(nucid).Sum(l => l.Intensity);
                Console.WriteLine("  " + nucid + ": Σ выходов α после нормировки на долю моды " + sum.ToString("F5", CultureInfo.InvariantCulture) + " % ("
                                  + CrystalOwnActivityBuilder.AlphaLines(nucid).Count + " строк)");
                exact += sum / 100.0 * (nucid == "227TH" ? ThoriumWeight() : 1.0);
            }

            Console.WriteLine("альф на распад головы по выходам базы: " + exact.ToString("F6", CultureInfo.InvariantCulture) + " (вес Th-227 — доля β⁻ Ac-227 "
                              + ThoriumWeight().ToString("F6", CultureInfo.InvariantCulture) + "); выборка: Σ весов " + list.Sum(p => p.Key).ToString("F6", CultureInfo.InvariantCulture));
            Check("альф на распад головы 4.997 (|Δ| < 0.001; база LNHB с Σ > 100 % нормирована на долю моды)", Math.Abs(exact - 4.997237) < 0.001);
            Check("раздел: альф на распад = по выходам базы (|Δ| < 0.002: округление историй и строки без историй)", Math.Abs(a1 - exact) < 0.002);

            var csv = new StringBuilder();
            csv.Append("E_keV,la138,ac227_e2,ac227_bare,alpha_bare,ac227_shape,alpha_shape\n");
            for (int i = 0; i < ac2.PerDecay.Length; i++)
            {
                csv.Append(F(i * bin)).Append(',').Append(R(Get(la.PerDecay, i))).Append(',').Append(R(Get(ac0.PerDecay, i))).Append(',')
                   .Append(R(ac1.PerDecay[i])).Append(',').Append(R(ac1.AlphaPerDecay[i])).Append(',')
                   .Append(R(ac2.PerDecay[i])).Append(',').Append(R(ac2.AlphaPerDecay[i])).Append('\n');
            }

            File.WriteAllText(Path.Combine(outDir, "layer.csv"), csv.ToString(), new UTF8Encoding(false));

            CrystalOwnActivityBuilder.PhotonLightScale light = CrystalOwnActivityBuilder.MeasurePhotonScale(g, bin, 0x274);
            var s = new StringBuilder();
            s.Append("w,E_alpha,cascade_keV,cascade_light\n");
            foreach (var pair in list)
            {
                s.Append(R(pair.Key)).Append(',').Append(R(pair.Value.AlphaKev)).Append(',').Append(R(pair.Value.CascadeKev)).Append(',')
                 .Append(R(pair.Value.CascadeLight)).Append('\n');
            }

            File.WriteAllText(Path.Combine(outDir, "samples.csv"), s.ToString(), new UTF8Encoding(false));
            var sc = new StringBuilder();
            sc.Append("E_keV,per_keV,light\n");
            for (int i = 0; i < light.EnergyKev.Length; i++)
            {
                sc.Append(R(light.EnergyKev[i])).Append(',').Append(R(light.PerKev[i])).Append(',').Append(R(light.Light[i])).Append('\n');
            }

            File.WriteAllText(Path.Combine(outDir, "scale.csv"), sc.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "qf.txt"), R(r2.AlphaQuenching) + " " + R(r2.AlphaQuenchingSlopePerMev) + "\n", new UTF8Encoding(false));

            // контроль ядра формы на дельте: сумма, ширина ядра, доля полки
            var delta = new double[(int)(4500.0 / bin) + 1];
            int at = (int)(3000.0 / bin);
            delta[at] = 1.0;
            var test = new CrystalOwnParts { AlphaQuenching = 0.4, AlphaExtraFwhmShare = 0.03, AlphaShelfShare = 0.5, AlphaShelfLength = 0.08 };
            double[] shaped = CrystalOwnActivityBuilder.ShapeAlphaGroups(delta, bin, test);
            double total = shaped.Sum(), below = 0.0, above = 0.0;
            for (int i = 0; i < shaped.Length; i++)
            {
                double e = i * bin;
                if (e < 3000.0 * (1.0 - 0.08) - 150.0) below += shaped[i];
                if (e > 3000.0 + 150.0) above += shaped[i];
            }

            double core = 0.0, shelf = 0.0;
            for (int i = 0; i < shaped.Length; i++)
            {
                double e = i * bin;
                if (e > 3000.0) core += shaped[i];                                  // верхняя половина ядра + хвост полки под гауссом
                if (e >= 2800.0 && e < 2900.0) shelf += shaped[i];                  // середина полки: 100 кэВ из 240
            }

            Console.WriteLine("ядро формы на дельте 3000 кэВ-экв (доб. FWHM 0.03, полка 0.5 × 0.08): сумма " + total.ToString("R", CultureInfo.InvariantCulture)
                              + ", выше места " + core.ToString("F4", CultureInfo.InvariantCulture) + ", в 2800–2900 " + shelf.ToString("F4", CultureInfo.InvariantCulture)
                              + ", вне [2610, 3150] " + (below + above).ToString("E1", CultureInfo.InvariantCulture));
            Check("форма сохраняет сумму (|Δ| < 1e-12)", Math.Abs(total - 1.0) < 1e-12);
            Check("полка: в 100 кэВ середины полки доля 0.5·100/240 = 0.2083 (|Δ| < 0.003)", Math.Abs(shelf - 0.5 * 100.0 / 240.0) < 0.003);
            Check("ядро: выше места половина ядра и край полки под гауссом — 0.25…0.33", core > 0.25 && core < 0.33);
            Check("вне отрезка «полка ± 150 кэВ» пусто", below + above < 1e-4);
            double[] none = CrystalOwnActivityBuilder.ShapeAlphaGroups(delta, bin, new CrystalOwnParts { AlphaQuenching = 0.4 });
            Check("формы нет — гистограмма та же", Same(none, delta));
            Console.WriteLine("готово: " + outDir);
            return 0;
        }

        /// <summary>Вес Th-227 в ряду — доля β⁻ Ac-227 (как у слоя): 1 − доля α-моды головы.</summary>
        static double ThoriumWeight()
        {
            double alpha = CrystalOwnActivityBuilder.AlphaLines("227AC").Sum(l => l.Intensity);
            return 1.0 - alpha / 100.0;
        }

        // ------------------------------------------------------------------------------------------------------
        sealed class Row
        {
            public string Tag;
            public double[] Counts;
            public double Live;
            public double[] C;
            public double Fwhm662;
            public DateTime Date = ResultData.UnknownStartTime;

            public double EnergyAt(double ch)
            {
                double e = 0.0, p = 1.0;
                foreach (double k in this.C)
                {
                    e += k * p;
                    p *= ch;
                }

                return e;
            }

            public double FwhmAt(double e)
            {
                return this.Fwhm662 * Math.Sqrt(Math.Max(e, 1.0) / 661.657);
            }

            public PolynomialEnergyCalibration Calibration()
            {
                var cal = new PolynomialEnergyCalibration { PolynomialOrder = this.C.Length - 1, Coefficients = (double[])this.C.Clone() };
                cal.CheckCalibration(this.Counts.Length);
                return cal;
            }

            /// <summary>FWHM² в каналах ≈ (k²·E)/s², E ≈ c0 + s·ch: `SqrtFwhm` — a + b·ch.</summary>
            public SqrtFwhmCalibration FwhmCalibration()
            {
                double s = this.C[1], k2 = this.Fwhm662 * this.Fwhm662 / 661.657;
                return new SqrtFwhmCalibration { Coefficients = new[] { k2 * this.C[0] / (s * s), k2 / s, 0.0 } };
            }

            public int[] Raw()
            {
                return this.Counts.Select(x => (int)Math.Round(x)).ToArray();
            }
        }

        static List<Row> ReadList(string spectra, string tags)
        {
            var want = tags != null ? new HashSet<string>(tags.Split(',')) : null;
            var rows = new List<Row>();
            foreach (string raw in File.ReadAllLines(spectra))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] f = raw.Split(';');
                if (want != null && !want.Contains(f[0]))
                {
                    continue;
                }

                var row = new Row
                {
                    Tag = f[0],
                    Counts = File.ReadAllLines(f[1]).Where(l => l.Length > 0).Select(l => double.Parse(l, CultureInfo.InvariantCulture)).ToArray(),
                    Live = double.Parse(f[2], CultureInfo.InvariantCulture),
                    C = f[3].Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray(),
                    Fwhm662 = double.Parse(f[4], CultureInfo.InvariantCulture) / 100.0 * 661.657,
                };
                if (f.Length > 6 && f[6].Length > 0)
                {
                    row.Date = DateTime.ParseExact(f[6], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                rows.Add(row);
            }

            return rows;
        }

        static int Count(EfficiencyConfigData eff, int histories, double bin, string spectra, string outDir)
        {
            if (spectra == null || !File.Exists(spectra))
            {
                return Fail("нужен --spectra=список");
            }

            GeometryModel g = eff.Geometry;
            string n1, n2, note;
            ResponseCrystalSection s1 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274,
                                                                        ResponseCrystalSection.RecipeFor(g, Without(eff, false, true), out n1), out note);
            ResponseCrystalSection s2 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274, ResponseCrystalSection.RecipeFor(g, eff, out n2), out note);
            CrystalOwnEntry ac1 = s1.Find("227AC", "beta"), ac2 = s2.Find("227AC", "beta");
            double[] known1 = CrystalOwnActivityBuilder.KnownPerSecond(s1), known2 = CrystalOwnActivityBuilder.KnownPerSecond(s2);
            CrystalOwnSeriesFallback fallback = eff.CrystalSeriesActivity != null
                ? new CrystalOwnSeriesFallback { ActivityBq = eff.CrystalSeriesActivity.ActivityBq, Date = eff.CrystalSeriesActivity.Date }
                : null;
            var text = new StringBuilder();
            text.Append("tag;date;live_s;A_Bq;sigma_Bq;base_pct;chi2ndf;win_lo;win_hi;line_keV;line_share_pct;line_z;line_found;reliable;tail_above_pct;"
                        + "A_bare_Bq;base_bare_pct;chi2ndf_bare;win_hi_bare;line_share_bare_pct;reliable_bare;tail_above_bare_pct;entry;entry_Bq;note\n");
            Console.WriteLine("метка | с формой: A, Бк  подл.%  χ²/ndf  окно  линия(кэВ-экв, % счёта окна, z)  годен  хвост выше окна % | без формы: A  подл.%  χ²/ndf  годен  хвост % | вход разбора");
            foreach (Row row in ReadList(spectra, null))
            {
                CrystalOwnActivityBuilder.AlphaCount c2 = CrystalOwnActivityBuilder.CountByAlphas(ac2, bin, row.Counts, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known2);
                CrystalOwnActivityBuilder.AlphaCount c1 = CrystalOwnActivityBuilder.CountByAlphas(ac1, bin, row.Counts, row.Live, row.EnergyAt, row.FwhmAt, 0.0, known1);
                double tail2 = TailAbove(row, c2), tail1 = TailAbove(row, c1);

                string state = "", entryBq = "";
                string hookNote = null;
                if (fallback != null)
                {
                    fallback.SpectrumStart = row.Date;
                }

                ResponseCrystalSection hooked = CrystalOwnActivityBuilder.FixSeriesByAlphas(s2, row.Raw(), row.Live, row.Calibration(), row.FwhmCalibration(),
                                                                                           null, fallback, out hookNote);
                CrystalOwnEntry h = hooked.Find("227AC", "beta");
                state = h == null ? "снята" : h.Free ? "свободная" : (hookNote ?? "").Contains("из поля прибора") ? "поле" : "счёт";
                entryBq = h != null && !h.Free ? h.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) : "";

                text.Append(row.Tag).Append(';').Append(row.Date == ResultData.UnknownStartTime ? "" : row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
                    .Append(F(row.Live)).Append(';').Append(R5(c2.ActivityBq)).Append(';').Append(R5(c2.SigmaBq)).Append(';')
                    .Append(Pct(c2.Baseline, c2.Counts)).Append(';').Append(R5(c2.ChiPerNdf)).Append(';').Append(F(c2.WindowLoKev)).Append(';')
                    .Append(F(c2.WindowHiKev)).Append(';').Append(F(c2.LineKev)).Append(';').Append(R5(100.0 * c2.LineShare)).Append(';')
                    .Append(R5(c2.LineZ)).Append(';').Append(c2.LineFound ? 1 : 0).Append(';').Append(c2.Reliable ? 1 : 0).Append(';').Append(R5(tail2)).Append(';')
                    .Append(R5(c1.ActivityBq)).Append(';').Append(Pct(c1.Baseline, c1.Counts)).Append(';').Append(R5(c1.ChiPerNdf)).Append(';')
                    .Append(F(c1.WindowHiKev)).Append(';').Append(R5(100.0 * c1.LineShare)).Append(';').Append(c1.Reliable ? 1 : 0).Append(';')
                    .Append(R5(tail1)).Append(';').Append(state).Append(';').Append(entryBq).Append(';').Append((hookNote ?? "").Replace(';', ',')).Append('\n');
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-4} | {1,7:F4} ± {2:F4}  {3,5}  {4,8:F2}  {5:F0}–{6:F0}  {7,5:F0} {8,5:F1} {9,6:F1}  {10}  {11,6:F2} | {12,7:F4}  {13,5}  {14,8:F2}  {15}  {16,6:F2} | {17} {18}",
                    row.Tag, c2.ActivityBq, c2.SigmaBq, Pct(c2.Baseline, c2.Counts), c2.ChiPerNdf, c2.WindowLoKev, c2.WindowHiKev, c2.LineKev,
                    100.0 * c2.LineShare, c2.LineZ, c2.Reliable ? "да " : "НЕТ", tail2, c1.ActivityBq, Pct(c1.Baseline, c1.Counts), c1.ChiPerNdf,
                    c1.Reliable ? "да " : "НЕТ", tail1, state, entryBq));
            }

            File.WriteAllText(Path.Combine(outDir, "count.csv"), text.ToString(), new UTF8Encoding(false));
            return 0;
        }

        /// <summary>Счёт по альфам на спектрах-файлах приложения: вход разбора с их шкалой и FWHM, раздел с формой и без.</summary>
        static int CountXml(EfficiencyConfigData eff, int histories, double bin, string xml, string outDir)
        {
            GeometryModel g = eff.Geometry;
            string n1, n2, note;
            ResponseCrystalSection s1 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274,
                                                                        ResponseCrystalSection.RecipeFor(g, Without(eff, false, true), out n1), out note);
            ResponseCrystalSection s2 = CrystalOwnActivityBuilder.Build(g, bin, histories, 0x274, ResponseCrystalSection.RecipeFor(g, eff, out n2), out note);
            foreach (string path in xml.Split(','))
            {
                ResultDataFile file;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(stream);
                }

                ResultData rd = file.ResultDataList[0];
                EnergySpectrum es = rd.EnergySpectrum;
                Console.WriteLine();
                Console.WriteLine("== " + Path.GetFileNameWithoutExtension(path) + ": живое " + F(es.EffectiveLiveTime) + " с, начало "
                                  + (rd.StartTime == ResultData.UnknownStartTime ? "неизвестно" : rd.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + " ==");
                foreach (var arm in new[] { Tuple.Create("без формы", s1), Tuple.Create("с формой ", s2) })
                {
                    foreach (bool field in new[] { false, true })
                    {
                        if (field && eff.CrystalSeriesActivity == null)
                        {
                            continue;
                        }

                        CrystalOwnSeriesFallback fallback = field
                            ? new CrystalOwnSeriesFallback { ActivityBq = eff.CrystalSeriesActivity.ActivityBq, Date = eff.CrystalSeriesActivity.Date, SpectrumStart = rd.StartTime }
                            : null;
                        var list = new List<CrystalOwnActivityBuilder.AlphaCount>();
                        string hook;
                        ResponseCrystalSection hooked = CrystalOwnActivityBuilder.FixSeriesByAlphas(arm.Item2, es.Spectrum, es.EffectiveLiveTime, es.EnergyCalibration,
                                                                                                   rd.FwhmCalibration, list, fallback, out hook);
                        CrystalOwnEntry h = hooked.Find("227AC", "beta");
                        CrystalOwnActivityBuilder.AlphaCount c = list.Count > 0 ? list[0] : new CrystalOwnActivityBuilder.AlphaCount();
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}{1}: счёт {2:F3} ± {3:F3} Бк, подложка {4} %, χ²/ndf {5:F2}, линия у {6:F0} кэВ-экв {7:F2} % счёта (z {8:F1}), найден {9}, годен {10}; запись ряда: {11}",
                                                        arm.Item1, field ? ", поле ряда" : "          ", c.ActivityBq, c.SigmaBq, Pct(c.Baseline, c.Counts), c.ChiPerNdf, c.LineKev,
                                                        100.0 * c.LineShare, c.LineZ, c.Found ? "да" : "нет", c.Reliable ? "да" : "НЕТ",
                                                        h == null ? "снята" : h.Free ? "свободная" : h.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) + " Бк"));
                        Console.WriteLine("      " + hook);
                    }
                }
            }

            return 0;
        }

        static string Pct(double part, double whole)
        {
            return (whole > 0.0 ? 100.0 * part / whole : 0.0).ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Данные выше верха окна счёта (до верха шкалы) за вычетом подложки, % чистого счёта окна: то, что окно режет.</summary>
        static double TailAbove(Row row, CrystalOwnActivityBuilder.AlphaCount c)
        {
            double net = c.Counts - c.Baseline;
            if (!(net > 0.0))
            {
                return 0.0;
            }

            int nWin = 0;
            double above = 0.0;
            int nAbove = 0;
            for (int i = 0; i < row.Counts.Length; i++)
            {
                double centre = 0.5 * (row.EnergyAt(i - 0.5) + row.EnergyAt(i + 0.5));
                if (centre >= c.WindowLoKev && centre < c.WindowHiKev)
                {
                    nWin++;
                }
                else if (centre >= c.WindowHiKev)
                {
                    above += row.Counts[i];
                    nAbove++;
                }
            }

            double perChannel = nWin > 0 ? c.Baseline / nWin : 0.0;
            return 100.0 * (above - perChannel * nAbove) / net;
        }

        // ------------------------------------------------------------------------------------------------------
        static int Wire(EfficiencyConfigData eff, DeviceConfigInfo device, string outDir)
        {
            GeometryModel g = eff.Geometry;
            var serializer = new XmlSerializer(typeof(EfficiencyConfigData));

            Console.WriteLine("== 1. поля прибора в XML ==");
            var plain = new EfficiencyConfigData("проба") { Geometry = g };
            string plainXml = Xml(serializer, plain);
            Check("пустые поля в XML не пишутся (нет AlphaQuenchingSource, AlphaGroupShape, CrystalSeriesActivity)",
                  plainXml.IndexOf("AlphaQuenchingSource", StringComparison.Ordinal) < 0 && plainXml.IndexOf("AlphaGroupShape", StringComparison.Ordinal) < 0
                  && plainXml.IndexOf("CrystalSeriesActivity", StringComparison.Ordinal) < 0);
            var filled = plain.Copy();
            filled.AlphaQuenchingSource = "a274a_bg_vertices";
            filled.AlphaGroupShape = new AlphaGroupShape { ExtraFwhmShare = 0.0292, ShelfShare = 0.483, ShelfLength = 0.0739, Origin = "проба" };
            filled.CrystalSeriesActivity = new CrystalSeriesActivity { ActivityBq = 1.773, Date = new DateTime(2022, 10, 15), Origin = "проба" };
            string filledXml = Xml(serializer, filled);
            EfficiencyConfigData back;
            using (var reader = new StringReader(filledXml))
            {
                back = (EfficiencyConfigData)serializer.Deserialize(reader);
            }

            Check("заполненные поля пишутся и читаются обратно (числа формы и ряда — до бита, дата — та же)",
                  back.AlphaGroupShape != null && back.CrystalSeriesActivity != null
                  && back.AlphaGroupShape.ExtraFwhmShare == 0.0292 && back.AlphaGroupShape.ShelfShare == 0.483 && back.AlphaGroupShape.ShelfLength == 0.0739
                  && back.CrystalSeriesActivity.ActivityBq == 1.773 && back.CrystalSeriesActivity.Date == new DateTime(2022, 10, 15));
            Check("числа в XML — с точкой", filledXml.Contains("<ExtraFwhmShare>0.0292</ExtraFwhmShare>") && filledXml.Contains("<ActivityBq>1.773</ActivityBq>"));
            EfficiencyConfigData copy = filled.Copy(), dup = filled.Duplicate("копия");
            copy.AlphaGroupShape.ShelfShare = 0.1;
            copy.CrystalSeriesActivity.ActivityBq = 9.0;
            Check("Copy() и Duplicate() несут поля, копия глубокая (правка копии оригинал не трогает)",
                  dup.AlphaGroupShape != null && dup.CrystalSeriesActivity != null && dup.AlphaGroupShape.ShelfShare == 0.483
                  && filled.AlphaGroupShape.ShelfShare == 0.483 && filled.CrystalSeriesActivity.ActivityBq == 1.773);
            using (var reader = new StringReader(plainXml))
            {
                EfficiencyConfigData old = (EfficiencyConfigData)serializer.Deserialize(reader);
                Check("прежний XML читается: полей нет (null), источник пуст",
                      old.AlphaGroupShape == null && old.CrystalSeriesActivity == null && old.AlphaQuenchingSource == "");
            }

            Console.WriteLine();
            Console.WriteLine("== 2. рецепт прибора ==");
            string note;
            ScintAlphaQuenching.Law law = ScintAlphaQuenching.Find(EfficiencySimulator.ScintillatorNameOf(g), eff.AlphaQuenchingSource);
            CrystalOwnParts recipe = ResponseCrystalSection.RecipeFor(g, eff, out note);
            Console.WriteLine("источник «" + eff.AlphaQuenchingSource + "»: " + (law == null ? "строки нет" : "QF(6 МэВ) " + R(law.QfAtBuilderReference) + ", наклон " + R(law.SlopePerMev))
                              + "; рецепт " + CrystalOwnActivityBuilder.RecipeStamp(recipe));
            Check("источник прибора найден в matdb.scint_alpha_quenching, рецепт несёт его QF и наклон",
                  law != null && recipe.AlphaQuenching == law.QfAtBuilderReference && recipe.AlphaQuenchingSlopePerMev == law.SlopePerMev && note == null);
            Check("форма прибора доехала до рецепта", eff.AlphaGroupShape == null
                  ? !recipe.HasAlphaShape
                  : recipe.AlphaExtraFwhmShare == eff.AlphaGroupShape.ExtraFwhmShare && recipe.AlphaShelfShare == eff.AlphaGroupShape.ShelfShare
                    && recipe.AlphaShelfLength == eff.AlphaGroupShape.ShelfLength);
            CrystalOwnParts empty = ResponseCrystalSection.RecipeFor(g, Without(eff, true, false), out note);
            Check("источник пуст (строки «по веществу» нет): альф нет, формы нет, рецепт умолчания — хотя форма у прибора задана",
                  !empty.HasAlpha && !empty.HasAlphaShape && note == null
                  && CrystalOwnActivityBuilder.RecipeStamp(empty) == CrystalOwnActivityBuilder.RecipeStamp(null));
            EfficiencyConfigData wrong = eff.Copy();
            wrong.AlphaQuenchingSource = "нет_такого";
            CrystalOwnParts unknown = ResponseCrystalSection.RecipeFor(g, wrong, out note);
            Console.WriteLine("  неизвестный источник: " + note);
            Check("источник назван, а строки нет: альф нет и причина названа", !unknown.HasAlpha && note != null && note.Contains("нет_такого"));
            CrystalOwnParts nullEff = ResponseCrystalSection.RecipeFor(g, null, out note);
            Check("прибора нет (null): рецепт умолчания", !nullEff.HasAlpha && note == null);
            GeometryModel nai = g.Clone();
            nai.Crystal = Material("Sodium iodide");
            CrystalOwnParts naiRecipe = ResponseCrystalSection.RecipeFor(nai, eff, out note);
            Check("NaI с тем же источником: альф нет (ряда у кристалла нет)", !naiRecipe.HasAlpha && note == null);

            Console.WriteLine();
            Console.WriteLine("== 3. раздел в матрице: сборка рецептом прибора и сверка клейма ==");
            ResponseMatrix m = BuildMatrix(g);
            ResponseMatrixBuilder.AttachCrystalSection(g, m);
            ResponseCrystalSection sDefault = m.CrystalSection;
            ResponseMatrixBuilder.AttachCrystalSection(g, m, Without(eff, true, false));
            ResponseCrystalSection sEmpty = m.CrystalSection;
            Check("источник пуст: раздел штатного входа побитно равен разделу прежнего входа", sDefault != null && sEmpty != null && SameSection(sDefault, sEmpty));
            CrystalSectionRefusal why;
            string detail;
            Check("раздел без альф: прежняя сверка — годен; прибор без источника — годен",
                  m.CrystalSectionFor(g, out why, out detail) != null && m.CrystalSectionFor(g, Without(eff, true, false), out why, out detail) != null);
            Check("раздел без альф прибор С источником не берёт: OtherRecipe",
                  m.CrystalSectionFor(g, eff, out why, out detail) == null && why == CrystalSectionRefusal.OtherRecipe);
            Console.WriteLine("  причина: " + ResponseCrystalSection.RefusalText(why, detail));

            ResponseMatrixBuilder.AttachCrystalSection(g, m, eff);
            ResponseCrystalSection sAlpha = m.CrystalSection;
            Console.WriteLine("раздел прибора: " + (sAlpha == null ? "НЕТ — " + m.CrystalSectionDamage : sAlpha.Stamp));
            Check("источник назван: раздел с альфами, в клейме рецепта :aq, :as, :av" + (recipe.HasAlphaShape ? ", :aw, :af, :al" : ""),
                  sAlpha != null && sAlpha.HasAlpha && sAlpha.Stamp.Contains(":aq" + R(recipe.AlphaQuenching)) && sAlpha.Stamp.Contains(":as")
                  && sAlpha.Stamp.Contains(":av") && (!recipe.HasAlphaShape || (sAlpha.Stamp.Contains(":aw" + R(recipe.AlphaExtraFwhmShare))
                                                                               && sAlpha.Stamp.Contains(":af" + R(recipe.AlphaShelfShare))
                                                                               && sAlpha.Stamp.Contains(":al" + R(recipe.AlphaShelfLength)))));
            Check("раздел с альфами прибор берёт: причина None",
                  m.CrystalSectionFor(g, eff, out why, out detail) != null && why == CrystalSectionRefusal.None);
            Check("раздел с альфами прежняя сверка (без прибора) не берёт: OtherRecipe",
                  m.CrystalSectionFor(g, out why, out detail) == null && why == CrystalSectionRefusal.OtherRecipe);
            Check("раздел с альфами прибор без источника не берёт: OtherRecipe",
                  m.CrystalSectionFor(g, Without(eff, true, false), out why, out detail) == null && why == CrystalSectionRefusal.OtherRecipe);
            EfficiencyConfigData otherShape = eff.Copy();
            otherShape.AlphaGroupShape = new AlphaGroupShape { ExtraFwhmShare = 0.05, ShelfShare = 0.2, ShelfLength = 0.05 };
            Check("раздел с альфами прибор с ДРУГОЙ формой не берёт: OtherRecipe",
                  m.CrystalSectionFor(g, otherShape, out why, out detail) == null && why == CrystalSectionRefusal.OtherRecipe);
            Console.WriteLine("  причина: " + ResponseCrystalSection.RefusalText(why, detail));
            if (eff.AlphaGroupShape != null)
            {
                Check("раздел с формой прибор без формы не берёт: OtherRecipe",
                      m.CrystalSectionFor(g, Without(eff, false, true), out why, out detail) == null && why == CrystalSectionRefusal.OtherRecipe);
            }

            string path = Path.Combine(outDir, "wire.rmx");
            m.Save(path);
            ResponseMatrix read = ResponseMatrix.Load(path);
            Check("матрица с разделом прибора: запись и чтение — раздел побитно тот же и годен прибору",
                  read != null && read.CrystalSection != null && SameSection(read.CrystalSection, sAlpha)
                  && read.CrystalSectionFor(g, eff, out why, out detail) != null);

            Console.WriteLine();
            Console.WriteLine("== 4. привязка ==");
            var analyzer = new FsaAnalyzer();
            FsaMatrixBinding.BindDevice(analyzer, eff, m);
            Check("привязка с прибором: раздел с альфами отдан анализатору", analyzer.CrystalOwnSection != null && analyzer.CrystalOwnSection.HasAlpha
                  && analyzer.CrystalOwnRefusal == CrystalSectionRefusal.None);
            Check("привязка с прибором: поле ряда доехало (или его нет)", eff.CrystalSeriesActivity == null
                  ? analyzer.CrystalOwnSeriesFallback == null
                  : analyzer.CrystalOwnSeriesFallback != null && analyzer.CrystalOwnSeriesFallback.ActivityBq == eff.CrystalSeriesActivity.ActivityBq
                    && analyzer.CrystalOwnSeriesFallback.Date == eff.CrystalSeriesActivity.Date);
            var analyzer2 = new FsaAnalyzer();
            FsaMatrixBinding.Bind(analyzer2, g, m);
            Check("прежняя привязка (по геометрии): раздел с альфами не отдан, причина OtherRecipe, поля ряда нет",
                  analyzer2.CrystalOwnSection == null && analyzer2.CrystalOwnRefusal == CrystalSectionRefusal.OtherRecipe
                  && analyzer2.CrystalOwnSeriesFallback == null);
            var rd = new ResultData { StartTime = new DateTime(2025, 7, 1) };
            analyzer.AdoptSpectrum(rd);
            Check("дата спектра доехала до анализатора (AdoptSpectrum)", analyzer.CrystalOwnSpectrumStart == new DateTime(2025, 7, 1));
            analyzer.AdoptSpectrum(null);
            Check("спектра нет — дата неизвестна", analyzer.CrystalOwnSpectrumStart == ResultData.UnknownStartTime);

            Console.WriteLine();
            Console.WriteLine("== 5. поле ряда: распад головы ==");
            double halfLife = CrystalOwnActivityBuilder.HalfLifeSeconds("227AC");
            Console.WriteLine("T½ Ac-227 по nucdb: " + (halfLife / 31557600.0).ToString("F4", CultureInfo.InvariantCulture) + " лет (" + R(halfLife) + " с)");
            var fb = new CrystalOwnSeriesFallback { ActivityBq = 1.773, Date = new DateTime(2022, 10, 15), SpectrumStart = new DateTime(2025, 7, 15) };
            string no;
            double at = fb.ActivityAtSpectrum("227AC", out no);
            double expect = 1.773 * Math.Pow(2.0, -(new DateTime(2025, 7, 15) - new DateTime(2022, 10, 15)).TotalSeconds / halfLife);
            Console.WriteLine("1.773 Бк на 2022-10-15 → на 2025-07-15: " + at.ToString("F5", CultureInfo.InvariantCulture) + " Бк (ждём " + expect.ToString("F5", CultureInfo.InvariantCulture) + ")");
            Check("распад поля ряда: A₀·2^(−Δt/T½), T½ в 21.6…21.9 лет", halfLife > 21.6 * 31557600.0 && halfLife < 21.9 * 31557600.0
                  && Math.Abs(at - expect) < 1e-12 && at < 1.773 && at > 1.6);
            fb.SpectrumStart = ResultData.UnknownStartTime;
            Check("дата спектра неизвестна — поле не берётся, причина названа", fb.ActivityAtSpectrum("227AC", out no) == 0.0 && no != null);
            Console.WriteLine("  причина: " + no);
            fb.SpectrumStart = new DateTime(2025, 7, 15);
            Check("нуклида нет в базе — поле не берётся, причина названа", fb.ActivityAtSpectrum("999XX", out no) == 0.0 && no != null);
            return 0;
        }

        static string Xml(XmlSerializer serializer, EfficiencyConfigData data)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, data);
                return writer.ToString();
            }
        }

        static GeometryMaterial Material(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null ? GeometryMaterialLibrary.Make(entry, entry.Density) : new GeometryMaterial();
        }

        /// <summary>Малая матрица сцены на ЦП — рецепт пробы `CrystalOwnStoreProbeA274m` (16 узлов 30–3000 кэВ, 20 тыс. историй).</summary>
        static ResponseMatrix BuildMatrix(GeometryModel geometry)
        {
            var options = new ResponseMatrixOptions { NodeCount = 16, Histories = 20000, BinKev = 2.0, MinEnergyKev = 30.0, MaxEnergyKev = 3000.0 };
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null, System.Threading.CancellationToken.None);
            Console.WriteLine("матрица стенда собрана: " + (matrix.Energies != null ? matrix.Energies.Length : 0) + " узлов");
            return matrix;
        }

        // ------------------------------------------------------------------------------------------------------
        static int Fsa(EfficiencyConfigData eff, string spectra, string tags, List<string> chains, List<string> nuclides, double line, string outDir)
        {
            if (spectra == null || !File.Exists(spectra) || (chains.Count == 0 && nuclides.Count == 0))
            {
                return Fail("нужны --spectra=список и проба (--chains= или --nuclides=)");
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            GeometryModel g = eff.Geometry;
            ResponseMatrix m = BuildMatrix(g);
            EfficiencyConfigData effEmpty = Without(eff, true, false), effBare = Without(eff, false, true);
            ResponseMatrixBuilder.AttachCrystalSection(g, m, effEmpty);
            ResponseCrystalSection sEmpty = m.CrystalSection;
            ResponseMatrixBuilder.AttachCrystalSection(g, m, effBare);
            ResponseCrystalSection sBare = m.CrystalSection;
            ResponseMatrixBuilder.AttachCrystalSection(g, m, eff);
            ResponseCrystalSection sShape = m.CrystalSection;
            EfficiencyConfigData effNoSeries = eff.Copy();
            effNoSeries.CrystalSeriesActivity = null;
            var text = new StringBuilder();
            text.Append("tag;arm;chi2ndf_poisson;gain;window_data;window_model;window_sample;rates;layer;note\n");
            foreach (Row row in ReadList(spectra, tags))
            {
                Console.WriteLine();
                Console.WriteLine("== " + row.Tag + " (живое " + F(row.Live) + " с" + (row.Date == ResultData.UnknownStartTime ? "" : ", " + row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + ") ==");
                var arms = new List<Tuple<string, bool, EfficiencyConfigData, ResponseCrystalSection>>
                {
                    Tuple.Create("слой ВЫКЛ", false, effEmpty, sEmpty),
                    Tuple.Create("слой ВКЛ, альф нет (источник пуст)", true, effEmpty, sEmpty),
                    Tuple.Create("слой ВКЛ, альфы без формы", true, Without(effNoSeries, false, true), sBare),
                    Tuple.Create("БЫЛО: альфы без формы, ряд по их счёту", true, WasArm(effNoSeries, sBare, row), sBare),
                    Tuple.Create("слой ВКЛ, альфы с формой", true, effNoSeries, sShape),
                };
                if (eff.CrystalSeriesActivity != null)
                {
                    arms.Add(Tuple.Create("слой ВКЛ, альфы с формой, поле ряда", true, eff, sShape));
                }

                foreach (var arm in arms)
                {
                    m.CrystalSection = arm.Item4;
                    var es = new EnergySpectrum(1.0, row.Counts.Length);
                    es.Spectrum = row.Raw();
                    es.MeasurementTime = row.Live;
                    es.LiveTime = row.Live;
                    long totalCounts = es.Spectrum.Sum(x => (long)x);
                    es.TotalPulseCount = totalCounts;
                    es.ValidPulseCount = totalCounts;
                    es.EnergyCalibration = row.Calibration();
                    var rd = new ResultData { EnergySpectrum = es, FwhmCalibration = row.FwhmCalibration(), StartTime = row.Date };
                    rd.Efficiency = arm.Item3.Copy();

                    var analyzer = new FsaAnalyzer();
                    analyzer.PileUp = false;
                    analyzer.RequireGeometry = false;
                    analyzer.CrystalOwnLayer = arm.Item2;
                    FsaTuningReport.Print(analyzer, row.Tag + ", " + arm.Item1);
                    analyzer.AdoptSpectrum(rd);
                    FsaMatrixBinding.BindDevice(analyzer, rd.Efficiency, m);
                    FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
                    FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
                    FsaCalculationOptions.Of(rd).ApplyTo(spec);
                    List<FsaComponent> library = FsaSampleLibrary.Build(spec);
                    FsaResult result = library.Count > 0 ? analyzer.Analyze(es, null, rd.FwhmCalibration, library, efficiency) : null;
                    if (result == null)
                    {
                        Console.WriteLine(arm.Item1 + ": отказ разбора — " + analyzer.Refusal + " " + analyzer.RefusalNote);
                        Check("разбор прошёл: " + row.Tag + ", " + arm.Item1, false);
                        continue;
                    }

                    // окно линии ±1 FWHM: данные, модель, образы пробы (не приборные)
                    double width = row.FwhmAt(line);
                    double data = 0.0, model = 0.0, sample = 0.0;
                    for (int i = 0; i < row.Counts.Length; i++)
                    {
                        double e = row.EnergyAt(i);
                        if (e < line - width || e >= line + width)
                        {
                            continue;
                        }

                        data += row.Counts[i];
                        model += result.Model != null && i < result.Model.Length ? result.Model[i] : 0.0;
                        foreach (FsaComponentResult c in result.Components)
                        {
                            if (c.Kind != FsaComponentKind.Nuisance && c.Curve != null && i < c.Curve.Length)
                            {
                                sample += c.Curve[i];
                            }
                        }
                    }

                    var rates = new List<string>();
                    foreach (FsaComponentResult c in result.Components)
                    {
                        if (c.Kind != FsaComponentKind.Nuisance && c.CountRate > 0.0)
                        {
                            rates.Add(c.Name + " " + c.CountRate.ToString("F3", CultureInfo.InvariantCulture));
                        }
                    }

                    var layer = new List<string>();
                    foreach (FsaCrystalOwnResult own in result.CrystalOwn ?? new List<FsaCrystalOwnResult>())
                    {
                        layer.Add(own.Nuclide + " " + (own.Free ? "свободная " : "известная ") + own.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) + " Бк");
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-40} χ²/ndf П {1,9:F3}  усиление {2:F5}  окно {3:F0}±FWHM: данные {4:F0}, модель подгонки (без вычтенного слоя) {5:F0}, образы пробы {6:F0}",
                                                    arm.Item1, result.Chi2NdfPoisson, result.Gain, line, data, model, sample));
                    Console.WriteLine("      скорости, 1/с: " + string.Join(", ", rates.ToArray()));
                    Console.WriteLine("      слой: " + (layer.Count > 0 ? string.Join(", ", layer.ToArray()) : "нет") + "; примечание: " + (result.CrystalOwnNote ?? "нет"));
                    text.Append(row.Tag).Append(';').Append(arm.Item1).Append(';').Append(R5(result.Chi2NdfPoisson)).Append(';').Append(R(result.Gain)).Append(';')
                        .Append(F(data)).Append(';').Append(F(model)).Append(';').Append(F(sample)).Append(';').Append(string.Join(", ", rates.ToArray()))
                        .Append(';').Append(string.Join(", ", layer.ToArray())).Append(';').Append((result.CrystalOwnNote ?? "").Replace(';', ',')).Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(outDir, "fsa.csv"), text.ToString(), new UTF8Encoding(false));
            return 0;
        }

        /// <summary>
        /// Плечо «было» (до формы групп, полоса `a274a`): раздел с альфами без формы, а ряд — известной частью с
        /// активностью, которую давал счёт по альфам этим разделом (тогда он считался годным). Теперь такой счёт
        /// признак линии отвергает, поэтому активность кладётся полем ряда на дату самого спектра.
        /// </summary>
        static EfficiencyConfigData WasArm(EfficiencyConfigData effNoSeries, ResponseCrystalSection sBare, Row row)
        {
            EfficiencyConfigData was = Without(effNoSeries, false, true);
            CrystalOwnActivityBuilder.AlphaCount c = CrystalOwnActivityBuilder.CountByAlphas(
                sBare.Find("227AC", "beta"), sBare.BinKev, row.Counts, row.Live, row.EnergyAt, row.FwhmAt, 0.0, CrystalOwnActivityBuilder.KnownPerSecond(sBare));
            was.CrystalSeriesActivity = new CrystalSeriesActivity { ActivityBq = c.ActivityBq, Date = row.Date, Origin = "счёт без формы" };
            return was;
        }

        // ------------------------------------------------------------------------------------------------------
        static bool SameSection(ResponseCrystalSection a, ResponseCrystalSection b)
        {
            if (!string.Equals(a.Stamp, b.Stamp, StringComparison.Ordinal) || a.BinKev != b.BinKev || a.Count != b.Count || a.Histories != b.Histories)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                CrystalOwnEntry x = a.Entries[i], y = b.Entries[i];
                if (x.Nuclide != y.Nuclide || x.Layer != y.Layer || x.Free != y.Free || x.Histories != y.Histories
                    || BitConverter.DoubleToInt64Bits(x.ActivityBq) != BitConverter.DoubleToInt64Bits(y.ActivityBq)
                    || !Same(x.PerDecay, y.PerDecay) || !Same(x.AlphaPerDecay, y.AlphaPerDecay))
                {
                    return false;
                }
            }

            return true;
        }

        static bool Same(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        static int Fail(string text)
        {
            Console.Error.WriteLine(text);
            return 2;
        }

        static double Get(double[] v, int i) { return i < v.Length ? v[i] : 0.0; }

        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        static string R5(double v) { return v.ToString("0.#####", CultureInfo.InvariantCulture); }
    }
}
