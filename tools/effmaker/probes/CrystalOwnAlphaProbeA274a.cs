using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CrystalOwnAlphaProbeA274a
{
    /// <summary>
    /// (`AMBER274` Э3 + остаток `AMBER323`, полоса `a274a` 14.11.2026) АЛЬФЫ РЯДА Ac-227 В СЛОЕ КРИСТАЛЛА
    /// И СЧЁТ РЯДА ПО АЛЬФАМ. Сцена — шаблон «AtomSpectra Pro LaBr3(Ce)» (⌀25×25, корпус Ø55, экран).
    ///
    ///   `--mode=layer` — раздел без альф (умолчание, Э2) и с альфами (`--qf=`, `--qs=`): гистограммы на распад в
    ///       `layer.csv` (E, La-138, ряд Э2, ряд Э3, часть альф), альфа-линии членов с каскадом или причиной его
    ///       отсутствия, суммы на распад; запись раздела туда и обратно (версия 1 без альф побайтно, версия 2 с
    ///       альфами); выборка событий альф (`samples.csv`: вес, E_α, осевшее и свет каскада) и фотонная шкала
    ///       (`scale.csv`) — для уточнения QF вне пробы; гистограмма той же выборки кодом приложения при
    ///       `--qf/--qs` (`samples_hist.csv`) — сверка формулы места события у внешнего счёта.
    ///   `--mode=count --spectra=list.csv` — счёт ряда по альфам (`CrystalOwnActivityBuilder.CountByAlphas`)
    ///       на спектрах из списка: строка `метка;файл отсчётов;живое время;c0 c1 c2 c3 c4;FWHM % на 662;верх кэВ`
    ///       (файл — отсчёты по каналам, по одному в строке; шкала — многочлен по каналу, своя у каждого
    ///       спектра); второй счёт — с верхом окна `верх` (группа Po-215 вне окна); у первой строки — ещё и вход
    ///       разбора `FixSeriesByAlphas` с калибровками приложения (сверка с прямым счётом).
    ///
    ///     CrystalOwnAlphaProbeA274a --mode=layer|count --out=каталог [--histories=200000] [--bin=1]
    ///                               [--qf=0.39] [--qs=0.01] [--samples=200000] [--spectra=list.csv]
    /// </summary>
    internal static class Program
    {
        const string PresetName = "AtomSpectra Pro LaBr3(Ce)";

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string mode = "layer", outDir = null, spectra = null;
            int histories = 200000, samples = 200000;
            double bin = 1.0, qf = 0.39, qs = 0.0;
            foreach (string a in args)
            {
                string v = a.Contains("=") ? a.Substring(a.IndexOf('=') + 1) : "";
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = v;
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = v;
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--samples=", StringComparison.Ordinal)) samples = int.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) bin = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--qf=", StringComparison.Ordinal)) qf = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--qs=", StringComparison.Ordinal)) qs = double.Parse(v, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectra = v;
                else { Console.Error.WriteLine("ключ не понят: " + a); return 2; }
            }

            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            GeometryPresets.Preset preset = GeometryPresets.Items.FirstOrDefault(p => p.Name == PresetName);
            if (preset == null)
            {
                Console.Error.WriteLine("нет шаблона " + PresetName);
                return 3;
            }

            var geometry = new GeometryModel();
            preset.Apply(geometry);
            var parts = new CrystalOwnParts { AlphaQuenching = qf, AlphaQuenchingSlopePerMev = qs };
            Console.WriteLine("сцена: " + PresetName + ", кристалл " + geometry.CrystalDiameter.ToString("F1", CultureInfo.InvariantCulture)
                              + "×" + geometry.CrystalHeight.ToString("F1", CultureInfo.InvariantCulture) + " мм, объём "
                              + GeometryScenes.CrystalVolumeCm3(geometry).ToString("F3", CultureInfo.InvariantCulture) + " см³");
            Console.WriteLine("QF(6 МэВ) = " + qf.ToString("R", CultureInfo.InvariantCulture) + ", наклон " + qs.ToString("R", CultureInfo.InvariantCulture) + " /МэВ");
            return mode == "layer" ? Layer(geometry, parts, histories, samples, bin, outDir)
                 : mode == "count" ? Count(geometry, parts, histories, bin, spectra, outDir)
                 : mode == "device" ? Device(spectra, outDir)
                 : Fail("режим не понят: " + mode);
        }

        /// <summary>
        /// (`AMBER323`) КОНФИГУРАЦИЯ ПРИБОРА на КОПИИ конфигурации пользователя: образец — прибор с основой
        /// AS 80x80 (`--spectra=` — путь к его XML в копии), новый Guid, имя «AtomSpectra Pro LaBr3(Ce)», 8192
        /// канала, одна конфигурация эффективности — геометрия шаблона (точечный источник, кривой нет: её
        /// посчитает конструктор, когда будут геометрии проб); шкала энергии — линейная по опорам K-пик 37.01 и
        /// пик суммы 1469.35 кэВ (каналы 107.27 и 3833.7 — фон B13 описи `a274x`; у прибора плывут усиление и
        /// ноль, шкала спектров — своя у каждого). Пишет `<out>\AtomSpectra Pro LaBr3(Ce).xml` — тем же
        /// сериализатором, что и приложение, и читает обратно.
        /// </summary>
        static int Device(string source, string outDir)
        {
            if (source == null || !File.Exists(source))
            {
                return Fail("нужен --spectra=<XML прибора-образца в копии конфигурации>");
            }

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(DeviceConfigInfo));
            DeviceConfigInfo sample;
            using (var stream = File.OpenRead(source))
            {
                sample = (DeviceConfigInfo)serializer.Deserialize(stream);
            }

            DeviceConfigInfo device = sample.Clone();
            device.InitFormatVersion();
            device.Guid = Guid.NewGuid().ToString();
            device.Name = PresetName;
            device.NumberOfChannels = 8192;
            device.BackgroundSpectrumPathname = "";
            double slope = (1469.35 - 37.01) / (3833.7 - 107.27);
            var energy = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new[] { 37.01 - slope * 107.27, slope } };
            energy.CheckCalibration(8192);
            device.EnergyCalibration = energy;

            GeometryPresets.Preset preset = GeometryPresets.Items.First(p => p.Name == PresetName);
            var geometry = new GeometryModel();
            preset.Apply(geometry);
            var efficiency = new EfficiencyConfigData
            {
                Guid = Guid.NewGuid().ToString(),
                Name = PresetName + " — точечный (шаблон)",
                LastUpdated = DateTime.Now,
                Curve = new List<ROIEfficiencyData>(),
                Geometry = geometry,
            };
            device.EfficiencyConfigs = new List<EfficiencyConfigData> { efficiency };
            device.ActiveEfficiencyGuid = efficiency.Guid;

            string path = Path.Combine(outDir, PresetName + ".xml");
            using (var stream = File.Create(path))
            {
                serializer.Serialize(stream, device);
            }

            DeviceConfigInfo back;
            using (var stream = File.OpenRead(path))
            {
                back = (DeviceConfigInfo)serializer.Deserialize(stream);
            }

            GeometryModel g = back.ActiveEfficiency != null ? back.ActiveEfficiency.Geometry : null;
            Console.WriteLine("конфигурация прибора: " + path);
            Console.WriteLine("  прочитана обратно: имя «" + back.Name + "», каналов " + back.NumberOfChannels + ", шкала E(0) = "
                              + back.EnergyCalibration.ChannelToEnergy(0).ToString("F2", CultureInfo.InvariantCulture) + ", E(8191) = "
                              + back.EnergyCalibration.ChannelToEnergy(8191).ToString("F1", CultureInfo.InvariantCulture) + " кэВ");
            if (g != null)
            {
                Console.WriteLine("  геометрия: кристалл " + g.Crystal.Name + " ⌀" + F(g.CrystalDiameter) + "×" + F(g.CrystalHeight) + ", FWHM 662 "
                                  + F(g.FwhmAt662Percent) + " %, зазор сбоку " + F(g.SideGapThickness) + ", экран " + F(g.MagneticShieldThickness)
                                  + " мм × " + F(g.MagneticShieldLength) + " мм (" + g.MagneticShield.Name + "), объём кристалла "
                                  + GeometryScenes.CrystalVolumeCm3(g).ToString("F3", CultureInfo.InvariantCulture) + " см³");
            }

            return 0;
        }

        static int Fail(string text)
        {
            Console.Error.WriteLine(text);
            return 2;
        }

        static int Layer(GeometryModel geometry, CrystalOwnParts parts, int histories, int samples, double bin, string outDir)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string noteOff, noteOn;
            ResponseCrystalSection off = CrystalOwnActivityBuilder.Build(geometry, bin, histories, 0x274, out noteOff);
            double tOff = watch.Elapsed.TotalSeconds;
            ResponseCrystalSection on = CrystalOwnActivityBuilder.Build(geometry, bin, histories, 0x274, parts, out noteOn);
            double tOn = watch.Elapsed.TotalSeconds - tOff;
            Console.WriteLine("раздел без альф: " + off.Entries.Count + " записей, " + tOff.ToString("F1", CultureInfo.InvariantCulture) + " с; клеймо " + off.Stamp);
            Console.WriteLine("  примечание: " + noteOff);
            Console.WriteLine("раздел с альфами: " + on.Entries.Count + " записей, " + tOn.ToString("F1", CultureInfo.InvariantCulture) + " с; клеймо " + on.Stamp);
            Console.WriteLine("  примечание: " + noteOn);

            CrystalOwnEntry la = off.Find("138LA", "beta");
            CrystalOwnEntry acOff = off.Find("227AC", "beta");
            CrystalOwnEntry laOn = on.Find("138LA", "beta");
            CrystalOwnEntry acOn = on.Find("227AC", "beta");
            Console.WriteLine("La-138: A = " + la.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) + " Бк; La-138 записи двух разделов побитно: "
                              + Same(la.PerDecay, Trim(laOn.PerDecay, la.PerDecay.Length)) + " (хвост раздела с альфами 3000–4500 пуст: "
                              + (Sum(laOn.PerDecay, la.PerDecay.Length, laOn.PerDecay.Length) == 0.0) + ")");
            Console.WriteLine("ряд: на распад — Э2 " + Sum(acOff.PerDecay).ToString("F5", CultureInfo.InvariantCulture)
                              + ", Э3 " + Sum(acOn.PerDecay).ToString("F5", CultureInfo.InvariantCulture)
                              + ", из них альф " + Sum(acOn.AlphaPerDecay).ToString("F5", CultureInfo.InvariantCulture)
                              + "; Э3 − альфы − Э2 (бета-члены при альфах прежние?) "
                              + (Sum(acOn.PerDecay) - Sum(acOn.AlphaPerDecay) - Sum(acOff.PerDecay)).ToString("E3", CultureInfo.InvariantCulture));
            double[] betaPartOn = new double[acOn.PerDecay.Length];
            for (int i = 0; i < betaPartOn.Length; i++) betaPartOn[i] = acOn.PerDecay[i] - acOn.AlphaPerDecay[i];
            Console.WriteLine("  бета-часть Э3 против Э2 побитно по бинам < 3000: " + SameTol(Trim(betaPartOn, acOff.PerDecay.Length), acOff.PerDecay, 1e-15));

            // альфа-линии членов
            var lines = new StringBuilder();
            lines.Append("nuclide;E_alpha;I_pct;level_keV;daughter;cascade;why\n");
            foreach (string nucid in new[] { "227AC", "227TH", "223RA", "219RN", "215PO", "211BI" })
            {
                foreach (CrystalOwnActivityBuilder.AlphaLine line in CrystalOwnActivityBuilder.AlphaLines(nucid))
                {
                    lines.Append(nucid).Append(';').Append(F(line.EnergyKev)).Append(';').Append(F(line.Intensity)).Append(';')
                         .Append(F(line.LevelKev)).Append(';').Append(line.DaughterZ).Append('/').Append(line.DaughterA).Append(';')
                         .Append(line.Cascade != null ? "да, переходов " + line.Cascade.TransitionCount + ", уровень " + F(line.Cascade.LevelKev) : "нет")
                         .Append(';').Append(line.NoCascade ?? "").Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(outDir, "alpha_lines.csv"), lines.ToString(), new UTF8Encoding(false));

            // гистограммы
            var csv = new StringBuilder();
            csv.Append("E_keV,la138,ac227_e2,ac227_e3,ac227_alpha\n");
            for (int i = 0; i < acOn.PerDecay.Length; i++)
            {
                csv.Append(F(i * bin)).Append(',').Append(R(Get(la.PerDecay, i))).Append(',').Append(R(Get(acOff.PerDecay, i))).Append(',')
                   .Append(R(acOn.PerDecay[i])).Append(',').Append(R(acOn.AlphaPerDecay[i])).Append('\n');
            }

            File.WriteAllText(Path.Combine(outDir, "layer.csv"), csv.ToString(), new UTF8Encoding(false));

            // туда и обратно
            RoundTrip(off, "без альф", 1);
            RoundTrip(on, "с альфами", 2);

            // выборка событий альф и шкала
            var notes = new List<string>();
            List<KeyValuePair<double, CrystalOwnActivityBuilder.AlphaSample>> list = CrystalOwnActivityBuilder.SampleSeriesAlphas(geometry, samples, 0x2741, notes);
            CrystalOwnActivityBuilder.PhotonLightScale light = CrystalOwnActivityBuilder.MeasurePhotonScale(geometry, bin, 0x274);
            Console.WriteLine("выборка альф: " + list.Count + " событий, Σ веса " + list.Sum(p => p.Key).ToString("F5", CultureInfo.InvariantCulture)
                              + " на распад головы; " + string.Join("; ", notes.ToArray()));
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
            var hist = new double[(int)(CrystalOwnActivityBuilder.AlphaHistogramTopKev / bin) + 2];
            foreach (var pair in list)
            {
                double pos = CrystalOwnActivityBuilder.AlphaPosition(pair.Value, parts, light);
                int k = (int)(pos / bin + 0.5);
                if (k >= 0 && k < hist.Length) hist[k] += pair.Key;
            }

            var h = new StringBuilder();
            h.Append("E_keV,w\n");
            for (int i = 0; i < hist.Length; i++) h.Append(F(i * bin)).Append(',').Append(R(hist[i])).Append('\n');
            File.WriteAllText(Path.Combine(outDir, "samples_hist.csv"), h.ToString(), new UTF8Encoding(false));

            // положения групп альф в разделе
            Console.WriteLine("часть альф, центры групп (по разделу, кэВ-экв):");
            foreach (var w in new[] { new[] { 1700.0, 2450.0 }, new[] { 2450.0, 2850.0 }, new[] { 2850.0, 3600.0 } })
            {
                double sw = 0.0, se = 0.0;
                for (int i = 0; i < acOn.AlphaPerDecay.Length; i++)
                {
                    double e = i * bin;
                    if (e >= w[0] && e < w[1]) { sw += acOn.AlphaPerDecay[i]; se += e * acOn.AlphaPerDecay[i]; }
                }

                Console.WriteLine("  " + F(w[0]) + "–" + F(w[1]) + ": на распад " + sw.ToString("F4", CultureInfo.InvariantCulture)
                                  + ", центр " + (sw > 0 ? (se / sw).ToString("F1", CultureInfo.InvariantCulture) : "—"));
            }

            Console.WriteLine("готово: " + outDir);
            return 0;
        }

        static int Count(GeometryModel geometry, CrystalOwnParts parts, int histories, double bin, string spectra, string outDir)
        {
            if (spectra == null || !File.Exists(spectra))
            {
                return Fail("нужен --spectra=список");
            }

            string note;
            ResponseCrystalSection on = CrystalOwnActivityBuilder.Build(geometry, bin, histories, 0x274, parts, out note);
            CrystalOwnEntry ac = on.Find("227AC", "beta");
            double[] known = CrystalOwnActivityBuilder.KnownPerSecond(on);
            var outText = new StringBuilder();
            outText.Append("tag;live_s;A_Bq;sigma_Bq;Z;chi2ndf;counts;baseline;tmpl_per_Bqs;win_lo;win_hi;cut_above;A_top_Bq;sigma_top_Bq;chi2_top;win_hi_top;A_noknown_Bq;side_lo;note\n");
            bool first = true;
            foreach (string raw in File.ReadAllLines(spectra))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] f = raw.Split(';');
                string tag = f[0];
                double[] counts = File.ReadAllLines(f[1]).Where(l => l.Length > 0).Select(l => double.Parse(l, CultureInfo.InvariantCulture)).ToArray();
                double live = double.Parse(f[2], CultureInfo.InvariantCulture);
                double[] c = f[3].Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                double fwhm662 = double.Parse(f[4], CultureInfo.InvariantCulture) / 100.0 * 661.657;
                double top = double.Parse(f[5], CultureInfo.InvariantCulture);
                Func<double, double> energyAt = ch => { double e = 0.0, p = 1.0; foreach (double k in c) { e += k * p; p *= ch; } return e; };
                Func<double, double> fwhmAt = e => fwhm662 * Math.Sqrt(Math.Max(e, 1.0) / 661.657);
                CrystalOwnActivityBuilder.AlphaCount full = CrystalOwnActivityBuilder.CountByAlphas(ac, bin, counts, live, energyAt, fwhmAt, 0.0, known);
                CrystalOwnActivityBuilder.AlphaCount cut = CrystalOwnActivityBuilder.CountByAlphas(ac, bin, counts, live, energyAt, fwhmAt, top, known);
                CrystalOwnActivityBuilder.AlphaCount naive = CrystalOwnActivityBuilder.CountByAlphas(ac, bin, counts, live, energyAt, fwhmAt, 0.0);
                outText.Append(tag).Append(';').Append(F(live)).Append(';').Append(R4(full.ActivityBq)).Append(';').Append(R4(full.SigmaBq)).Append(';')
                       .Append(R4(full.Z)).Append(';').Append(R4(full.ChiPerNdf)).Append(';').Append(F(full.Counts)).Append(';').Append(F(full.Baseline)).Append(';')
                       .Append(R4(full.TemplatePerBqS)).Append(';').Append(F(full.WindowLoKev)).Append(';').Append(F(full.WindowHiKev)).Append(';')
                       .Append(R4(full.CutAboveTop)).Append(';').Append(R4(cut.ActivityBq)).Append(';').Append(R4(cut.SigmaBq)).Append(';')
                       .Append(R4(cut.ChiPerNdf)).Append(';').Append(F(cut.WindowHiKev)).Append(';').Append(R4(naive.ActivityBq)).Append(';')
                       .Append(F(full.SideLoKev)).Append(';').Append(full.Note).Append('\n');
                Console.WriteLine(tag + ": A = " + full.ActivityBq.ToString("F3", CultureInfo.InvariantCulture) + " ± " + full.SigmaBq.ToString("F3", CultureInfo.InvariantCulture)
                                  + " Бк (" + full.Note + "); верх " + F(top) + ": " + cut.ActivityBq.ToString("F3", CultureInfo.InvariantCulture)
                                  + " ± " + cut.SigmaBq.ToString("F3", CultureInfo.InvariantCulture));
                if (first)
                {
                    first = false;
                    Hook(on, counts, live, c, fwhm662, full);
                }
            }

            File.WriteAllText(Path.Combine(outDir, "count.csv"), outText.ToString(), new UTF8Encoding(false));
            return 0;
        }

        /// <summary>Вход разбора: раздел → запись ряда с амплитудой по альфам; калибровки приложения.</summary>
        static void Hook(ResponseCrystalSection section, double[] counts, double live, double[] c, double fwhm662, CrystalOwnActivityBuilder.AlphaCount direct)
        {
            var cal = new PolynomialEnergyCalibration { PolynomialOrder = c.Length - 1, Coefficients = (double[])c.Clone() };
            Console.WriteLine("  шкала приложения: степень " + cal.PolynomialOrder + ", годна " + cal.CheckCalibration(counts.Length)
                              + ", E(8191.5) = " + cal.ChannelToEnergy(counts.Length - 0.5).ToString("F1", CultureInfo.InvariantCulture));
            // FWHM² в каналах ≈ (k²·E)/s², E ≈ c0 + s·ch: SqrtFwhm — a + b·ch (c = 0)
            double s = c[1], k2 = fwhm662 * fwhm662 / 661.657;
            var fwhm = new SqrtFwhmCalibration { Coefficients = new[] { k2 * c[0] / (s * s), k2 / s, 0.0 } };
            int[] raw = counts.Select(x => (int)Math.Round(x)).ToArray();
            string note;
            var list = new List<CrystalOwnActivityBuilder.AlphaCount>();
            ResponseCrystalSection fixedSection = CrystalOwnActivityBuilder.FixSeriesByAlphas(section, raw, live, cal, fwhm, list, out note);
            CrystalOwnEntry ac = fixedSection.Find("227AC", "beta");
            Console.WriteLine("вход разбора FixSeriesByAlphas: " + note);
            Console.WriteLine("  запись ряда " + (ac == null ? "снята" : (ac.Free ? "свободная" : "с амплитудой " + ac.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) + " Бк"))
                              + "; прямой счёт " + direct.ActivityBq.ToString("F4", CultureInfo.InvariantCulture) + " Бк; исходный раздел не тронут: "
                              + section.Find("227AC", "beta").Free);

            // отрицательный контроль: спектр без альф (окно альф обнулено) — строка снимается
            int[] noAlpha = (int[])raw.Clone();
            for (int i = 0; i < noAlpha.Length; i++)
            {
                if (cal.ChannelToEnergy(i) > direct.WindowLoKev - 400.0) noAlpha[i] = (int)Math.Round(counts[i] * 0.0) + (i % 7 == 0 ? 1 : 0);
            }

            ResponseCrystalSection none = CrystalOwnActivityBuilder.FixSeriesByAlphas(section, noAlpha, live, cal, fwhm, null, out note);
            Console.WriteLine("  отрицательный контроль (альфы вырезаны): запись ряда " + (none.Find("227AC", "beta") == null ? "снята" : "ОСТАЛАСЬ") + " — " + note);
        }

        static void RoundTrip(ResponseCrystalSection section, string what, int expectVersion)
        {
            Type t = typeof(ResponseCrystalSection);
            MethodInfo write = t.GetMethod("WriteTrailer", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo read = t.GetMethod("ReadTrailer", BindingFlags.NonPublic | BindingFlags.Static);
            using (var memory = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
                {
                    writer.Write(new byte[16]);                      // «тело матрицы»
                    write.Invoke(null, new object[] { writer, section });
                }

                memory.Position = 20;
                int version = new BinaryReader(memory).ReadInt32();
                memory.Position = 0;
                var argsRead = new object[] { memory, 16L, null };
                var back = (ResponseCrystalSection)read.Invoke(null, argsRead);
                bool same = back != null && back.Entries.Count == section.Entries.Count && back.Stamp == section.Stamp;
                for (int i = 0; same && i < back.Entries.Count; i++)
                {
                    same = Same(back.Entries[i].PerDecay, section.Entries[i].PerDecay)
                           && (back.Entries[i].AlphaPerDecay == null) == (section.Entries[i].AlphaPerDecay == null)
                           && (back.Entries[i].AlphaPerDecay == null || Same(back.Entries[i].AlphaPerDecay, section.Entries[i].AlphaPerDecay));
                }

                Console.WriteLine("запись раздела " + what + ": версия " + version + " (ждём " + expectVersion + "), туда и обратно — "
                                  + (same ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ") + (argsRead[2] != null ? ", порча: " + argsRead[2] : ""));
            }
        }

        static double Sum(double[] v) { return v == null ? 0.0 : v.Sum(); }

        static double Sum(double[] v, int from, int to)
        {
            double s = 0.0;
            for (int i = from; i < to && i < v.Length; i++) s += v[i];
            return s;
        }

        static double[] Trim(double[] v, int n) { var r = new double[n]; Array.Copy(v, r, Math.Min(n, v.Length)); return r; }

        static bool Same(double[] a, double[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i])) return false;
            return true;
        }

        static bool SameTol(double[] a, double[] b, double tol)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > tol) return false;
            return true;
        }

        static double Get(double[] v, int i) { return i < v.Length ? v[i] : 0.0; }

        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        static string R4(double v) { return v.ToString("0.#####", CultureInfo.InvariantCulture); }
    }
}
