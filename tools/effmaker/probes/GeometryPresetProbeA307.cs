using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace GeometryPresetProbeA307
{
    /// <summary>
    /// Приёмка `AMBER307`: шаблон «RadiaCode-110» и слова отказа FSA о приборе.
    ///
    ///   GeometryPresetProbeA307.exe --gdml=&lt;RC110_detector.gdml&gt; --spectrum=&lt;файл спектра с Cs-137.xml&gt;
    ///       [--n=300000] [--threads=10] [--no-eff] [--ui]
    ///
    /// Разделы:
    ///   1. шаблон есть, стоит последним, поля равны числам, выведенным ИЗ САМОГО файла
    ///      GDML (сборка тел разбирается здесь, а не набрана второй копией чисел);
    ///   2. круг «модель → .in → модель» держит клеймо матрицы; слои без отказов;
    ///   3. ε на точке 50 мм против кривой `C` журнала `handover-2026-11-05-rc110-check.md`
    ///      (`Точка C 5 см`: те же слои, FWHM 10.85, зазор бока 3.0, 300 тыс. историй).
    ///      Положительный контроль здесь же: глубина 5.5 мм вместо 3.85 обязана
    ///      разойтись с кривой `C` за допуском;
    ///   4. слова отказа — через НАСТОЯЩИЙ сеанс `FsaAnalysisSession`, обе культуры: прибор
    ///      не найден, прибор другого номера той же модели, подходят несколько, найден без
    ///      геометрии, назначен руками без геометрии; кривая старше прибора;
    ///   5. (`--ui`) редактор геометрии: шаблон выбирается, списки веществ названы, поля дают в
    ///      расчёт то же клеймо. Форма показывается — запускать ТОЛЬКО на скрытом столе.
    ///
    /// Коды: 0 — все сошлись; 1 — есть расхождение; 2 — мерить нечем.
    /// Числа журнала C ниже — чтение кривой копии конфигурации полосы `rc110check`.
    /// </summary>
    static class Program
    {
        // Кривая «Точка C 5 см» полосы `rc110check`, чтение логарифмической интерполяцией.
        static readonly double[] RefEnergy = { 32.0, 88.0, 200.0, 662.0, 1461.0 };
        static readonly double[] RefC50 = { 5.24766e-03, 4.68214e-03, 3.48103e-03, 4.84174e-04, 1.37244e-04 };

        /// <summary>Допуск кривой при 300 тыс. историй: шум узла 0.5–1 %, состав ПЭТ чуть другой.</summary>
        const double RefTolerance = 0.02;

        static int bad;
        static int checks;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            string gdml = null, spectrum = null;
            int n = 300000, threads = 10;
            bool noEff = false, ui = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--gdml=", StringComparison.Ordinal)) gdml = a.Substring(7);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--no-eff") noEff = true;
                else if (a == "--ui") ui = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (gdml == null || !File.Exists(gdml)) { Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: нужен --gdml=<RC110_detector.gdml>"); return 2; }
            if (spectrum == null || !File.Exists(spectrum)) { Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: нужен --spectrum=<файл.xml>"); return 2; }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            GeometryModel g = TemplateSection(gdml);
            if (g == null) return 1;
            RoundTripSection(g);
            if (!noEff) EfficiencySection(g, n, threads);
            int measured = RefusalSection(spectrum, nuclides);
            if (measured != 0) return measured;
            if (ui) UiSection();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ: проверок {0}" : "РАСХОЖДЕНИЙ {1} из {0}", checks, bad);
            return bad == 0 ? 0 : 1;
        }

        static void Same(string what, bool ok, string detail = null)
        {
            checks++;
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}{2}", ok ? "ok" : "!!", what, detail == null ? "" : " — " + detail);
        }

        static string F(double v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        // ---------------- 1. поля шаблона против файла GDML ----------------

        sealed class V3
        {
            public double X, Y, Z;
        }

        static GeometryModel TemplateSection(string path)
        {
            Console.WriteLine("=== 1. шаблон против файла GDML ===");
            GeometryPresets.Preset preset = GeometryPresets.Items.FirstOrDefault(p => p.Name == "RadiaCode-110");
            Same("шаблон «RadiaCode-110» есть", preset != null);
            if (preset == null) return null;
            Same("стоит последним: пробы берут шаблоны по номеру", GeometryPresets.Items.Last() == preset,
                 "всего " + GeometryPresets.Items.Count.ToString(CultureInfo.InvariantCulture));
            Same("[1] RC-101, [2] RC-103, [4] 80x80 на прежних местах",
                 GeometryPresets.Items[1].Name == "RadiaCode-101" && GeometryPresets.Items[2].Name == "RadiaCode-103"
                 && GeometryPresets.Items[4].Name == "Atom Spectra Pro 80x80");

            GeometryModel g = GeometryEditorPanel.Blank();
            preset.Apply(g);

            XDocument x = XDocument.Load(path);
            Dictionary<string, V3> pos = x.Descendants("position").Where(e => e.Attribute("name") != null)
                .ToDictionary(e => (string)e.Attribute("name"), e => new V3 { X = A(e, "x"), Y = A(e, "y"), Z = A(e, "z") });
            Dictionary<string, V3> box = x.Descendants("box").Where(e => e.Attribute("name") != null)
                .ToDictionary(e => (string)e.Attribute("name"), e => new V3 { X = A(e, "x"), Y = A(e, "y"), Z = A(e, "z") });

            V3 crystal = box["CrystalSolid"], outer = box["Case_outer"], inner = box["Case_inner"];
            V3 capOuter = box["Capsule_outer"], capInner = box["Capsule_inner"];
            double esr = box["ESR_slab_z"].Z;
            double cx = pos["p_detector"].X + pos["p_node"].X;
            double cz = pos["p_detector"].Z + pos["p_node"].Z;

            // Окно кристалла — к тылу, ось +Z сборки. Ближняя боковая сторона — −X.
            double depthBack = outer.Z / 2.0 - (cz + crystal.Z / 2.0);
            double depthNearSide = (outer.X / 2.0 + cx) - crystal.X / 2.0;
            double absWall = (outer.Z - inner.Z) / 2.0 + (capOuter.Z - capInner.Z) / 2.0;
            double mounting = (capOuter.Z - capInner.Z) / 2.0;

            XElement abs = x.Descendants("material").First(e => (string)e.Attribute("name") == "Case_ABS");
            XElement foil = x.Descendants("material").First(e => (string)e.Attribute("name") == "ESR_foil");
            double absDensity = A(abs.Element("D"), "value"), foilDensity = A(foil.Element("D"), "value");
            Func<string, double> absPart = el => abs.Elements("fraction")
                .Where(f => (string)f.Attribute("ref") == el).Sum(f => A(f, "n"));

            Console.WriteLine("  {0,-34} {1,10} {2,10}", "поле", "шаблон", "GDML");
            Action<string, double, double> row = (name, have, want) =>
            {
                bool ok = Math.Abs(have - want) < 1e-9;
                Console.WriteLine("  {0,-34} {1,10} {2,10} {3}", name, F(have), F(want), ok ? "" : "!!");
                checks++;
                if (!ok) bad++;
            };

            row("куб X, мм", g.CrystalBoxX, crystal.X);
            row("куб Y, мм", g.CrystalBoxY, crystal.Y);
            row("куб Z, мм", g.CrystalBoxZ, crystal.Z);
            row("плотность кристалла, г/см3", g.Crystal.Density, A(x.Descendants("material").First(e => (string)e.Attribute("name") == "G4_CESIUM_IODIDE").Element("D"), "value"));
            row("глубина к тылу (окно), мм", g.FrontReflectorThickness + g.FrontGapThickness + g.FrontCladdingThickness, depthBack);
            row("ABS торца, мм", g.FrontCladdingThickness, absWall);
            row("ESR торца, мм", g.FrontReflectorThickness, esr);
            row("воздух торца, мм", g.FrontGapThickness, depthBack - absWall - esr);
            row("ABS бока, мм", g.SideCladdingThickness, absWall);
            row("ESR бока, мм", g.SideReflectorThickness, box["ESR_slab_x"].X);
            row("воздух бока (сторона −X), мм", g.SideGapThickness, depthNearSide - absWall - box["ESR_slab_x"].X);
            row("опора (капсула), мм", g.MountingThickness, mounting);
            row("плотность ABS, г/см3", g.Cladding.Density, absDensity);
            row("доля C ABS", Fraction(g.Cladding, 6), absPart("G4_C"));
            row("доля H ABS", Fraction(g.Cladding, 1), absPart("G4_H"));
            row("доля N ABS", Fraction(g.Cladding, 7), absPart("G4_N"));
            row("плотность ESR, г/см3", g.Reflector.Density, foilDensity);

            Same("вещество кристалла — CsI", g.Crystal.Name == "Cesium iodide", g.Crystal.Name);
            Same("вещество отражателя — ПЭТ библиотеки", g.Reflector.Name == "Polyethylene terephthalate" && g.Reflector.Fractions.Count == 3, g.Reflector.Name);
            Same("вещество корпуса — ABS библиотеки", g.Cladding.Name == "ABS plastic" && g.Cladding.Fractions.Count == 3, g.Cladding.Name);
            Same("зазор — воздух", g.Gap != null && g.Gap.Name == "Air, dry", g.Gap != null ? g.Gap.Name : "нет");
            Same("FWHM — мера RC-110, не число RC-101", Math.Abs(g.FwhmAt662Percent - 8.57) < 1e-9, F(g.FwhmAt662Percent));
            return g;
        }

        static double Fraction(GeometryMaterial m, int z)
        {
            double v;
            return m.Fractions.TryGetValue(z, out v) ? v : 0.0;
        }

        static double A(XElement e, string name)
        {
            XAttribute a = e.Attribute(name);
            return a == null ? 0.0 : double.Parse((string)a, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---------------- 2. круг через файл ----------------

        static void RoundTripSection(GeometryModel g)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. круг модель → .in → модель ===");
            List<string> layers = g.CheckLayers();
            Same("слои без отказов", layers == null || layers.Count == 0, layers == null ? null : string.Join("; ", layers));
            Same("ориентация Front без отказа", g.Facing == GeometryDetectorFacing.Front && string.IsNullOrEmpty(g.FacingError));

            GeometryModel point = g.Clone();
            point.SourceType = GeometrySourceType.Point;
            point.PointDistance = 50.0;
            string file = Path.Combine(Path.GetTempPath(), "a307_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                GeometryWriter.Save(point, file);
                GeometryModel back = GeometryModel.Load(file);
                string a = ResponseMatrix.ComputeStamp(point, new ResponseMatrixOptions());
                string b = ResponseMatrix.ComputeStamp(back, new ResponseMatrixOptions());
                Same("клеймо матрицы то же после круга", a == b, a);
                Same("глубина после круга", Math.Abs(back.FrontGapThickness - 0.785) < 1e-9, F(back.FrontGapThickness));
                Same("зазор бока после круга", Math.Abs(back.SideGapThickness - 2.985) < 1e-9, F(back.SideGapThickness));
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }

            // Вещество удалили из библиотеки: шаблон кладёт пустое, счёт обязан отказать словами.
            GeometryModel empty = point.Clone();
            empty.Cladding = new GeometryMaterial();
            string missing = empty.MissingMaterialProblem();
            Same("пустой корпус: причина названа", !string.IsNullOrEmpty(missing), missing);
            Same("шаблон как есть: причины нет", g.MissingMaterialProblem() == null);
            string refusedText;
            try
            {
                refusedText = EfficiencyCalculation.Run(empty, new EfficiencyCalculationOptions { Histories = 1000, Threads = 1, NodeCount = 5 }, null, null).Error;
            }
            catch (Exception ex)
            {
                refusedText = "ПАДЕНИЕ " + ex.GetType().Name;
            }

            Same("пустой корпус: кривая отказывает словами", refusedText == missing, refusedText);
            string thrown = null;
            try
            {
                ResponseMatrixBuilder.Build(empty, new ResponseMatrixOptions { Histories = 1000, NodeCount = 3 }, null, default(CancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                thrown = ex.Message;
            }
            catch (Exception ex)
            {
                thrown = "ПАДЕНИЕ " + ex.GetType().Name;
            }

            Same("пустой корпус: матрица отказывает словами", thrown == missing, thrown);

            GeometryTemplate own = GeometryTemplate.FromModel("a307", g);
            GeometryModel viaOwn = GeometryEditorPanel.Blank();
            own.Apply(viaOwn);
            Same("свой шаблон из этой модели повторяет клеймо",
                 ResponseMatrix.ComputeStamp(viaOwn, new ResponseMatrixOptions()) == ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions()));
        }

        // ---------------- 3. эффективность на точке 50 мм ----------------

        static double[] Curve(GeometryModel g, int n, int threads, out string error)
        {
            var options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = 5.0,
                MaxEnergyKev = 3000.0,
                GridMode = EfficiencyGridMode.Logarithmic,
                NodeCount = 100,
                Histories = n,
                Threads = threads
            };
            EfficiencyFitResult r = EfficiencyCalculation.Run(g, options, null, null);
            error = r.Error;
            if (!string.IsNullOrEmpty(r.Error)) return null;

            var config = new EfficiencyConfigData();
            config.Curve = new List<ROIEfficiencyData>(r.Curve);
            config.ComputeStamp = r.ComputeStamp;
            string refusal;
            FsaEfficiency eff = FsaEfficiency.FromConfig(config, out refusal);
            if (eff == null) { error = refusal ?? "кривая не читается"; return null; }
            return RefEnergy.Select(e => eff.Eval(e)).ToArray();
        }

        static void EfficiencySection(GeometryModel template, int n, int threads)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. ε точки 50 мм против кривой C журнала rc110-check, {0} историй ===", n);
            GeometryModel asTemplate = template.Clone();
            asTemplate.SourceType = GeometrySourceType.Point;
            asTemplate.PointDistance = 50.0;

            // Плечо «как C»: та же сцена журнала, отличия шаблона убраны.
            GeometryModel asC = asTemplate.Clone();
            asC.FwhmAt662Percent = 10.85;
            asC.SideGapThickness = 3.0;

            // Положительный контроль: глубина 5.5 мм (как у обвязки RC-103) обязана разойтись.
            GeometryModel deep = asC.Clone();
            deep.FrontGapThickness = 5.5 - deep.FrontReflectorThickness - deep.FrontCladdingThickness;

            string error;
            double[] c = Curve(asC, n, threads, out error);
            if (c == null) { Same("кривая «как C»", false, error); return; }
            double[] t = Curve(asTemplate, n, threads, out error);
            if (t == null) { Same("кривая шаблона", false, error); return; }
            double[] d = Curve(deep, n, threads, out error);
            if (d == null) { Same("кривая контроля", false, error); return; }

            Console.WriteLine("  {0,8} {1,12} {2,10} {3,10} {4,10}", "E, кэВ", "ε журнала C", "как C", "шаблон", "глубина 5.5");
            for (int i = 0; i < RefEnergy.Length; i++)
            {
                Console.WriteLine("  {0,8} {1,12} {2,10} {3,10} {4,10}", F(RefEnergy[i]),
                                  RefC50[i].ToString("0.00000E+0", CultureInfo.InvariantCulture),
                                  (c[i] / RefC50[i]).ToString("0.0000", CultureInfo.InvariantCulture),
                                  (t[i] / RefC50[i]).ToString("0.0000", CultureInfo.InvariantCulture),
                                  (d[i] / RefC50[i]).ToString("0.0000", CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < RefEnergy.Length; i++)
            {
                Same("как C, " + F(RefEnergy[i]) + " кэВ в пределах 2 %", Math.Abs(c[i] / RefC50[i] - 1.0) <= RefTolerance);
                Same("шаблон, " + F(RefEnergy[i]) + " кэВ в пределах 2 %", Math.Abs(t[i] / RefC50[i] - 1.0) <= RefTolerance);
            }

            // Контроль: геометрический фактор на 662 кэВ при глубине 5.5 мм ниже на 3–5 %
            // (1.65 мм ближе к источнику — +5.3 % на 5 см по журналу, минус другие слои).
            Same("контроль: глубина 5.5 мм расходится с C на 662 кэВ", (1.0 - d[3] / c[3]) > RefTolerance,
                 (d[3] / c[3]).ToString("0.0000", CultureInfo.InvariantCulture));
        }

        // ---------------- 5. редактор геометрии ----------------

        static void UiSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== 5. редактор геометрии (скрытый стол) ===");
            Application.EnableVisualStyles();
            string own = Path.Combine(Path.GetTempPath(), "a307_templates_" + Guid.NewGuid().ToString("N") + ".xml");
            GeometryTemplateStore.PathOverride = own;
            GeometryTemplateStore.Reload();
            foreach (string culture in new[] { "en", "ru" })
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                using (Form form = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-4000, -4000),
                    ShowInTaskbar = false,
                    ClientSize = new System.Drawing.Size(950, 720),
                })
                {
                    GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
                    form.Controls.Add(panel);
                    form.Show();
                    Application.DoEvents();
                    Same(culture + ": шаблон выбирается в списке", panel.SelectPresetByName("RadiaCode-110"));
                    Application.DoEvents();
                    var materials = (Dictionary<string, ComboBox>)typeof(GeometryEditorPanel)
                        .GetField("materials", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                    foreach (string slot in new[] { "Crystal", "Reflector", "Gap", "Cladding" })
                    {
                        ComboBox combo = materials[slot];
                        Same(culture + ": строка «" + slot + "» названа", combo.SelectedIndex >= 0 && combo.Text.Length > 0, combo.Text);
                    }

                    GeometryModel built = (GeometryModel)typeof(GeometryEditorPanel)
                        .GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                        .Invoke(panel, null);
                    GeometryModel want = panel.Model;
                    Same(culture + ": поля дают модель шаблона, клеймо то же",
                         ResponseMatrix.ComputeStamp(built, new ResponseMatrixOptions()) == ResponseMatrix.ComputeStamp(want, new ResponseMatrixOptions()),
                         "ABS " + F(built.FrontCladdingThickness) + ", ESR " + F(built.FrontReflectorThickness)
                         + ", зазор " + F(built.FrontGapThickness) + ", бок " + F(built.SideGapThickness)
                         + ", корпус «" + built.Cladding.Name + "», ρ " + F(built.Cladding.Density));
                    Same(culture + ": глубина в полях 3.85 мм",
                         Math.Abs(built.FrontCladdingThickness + built.FrontReflectorThickness + built.FrontGapThickness - 3.85) < 1e-9);
                    form.Close();
                }
            }

            if (File.Exists(own)) File.Delete(own);
        }

        // ---------------- 4. слова отказа ----------------

        sealed class FakeInput : AudioInputDeviceConfig
        {
            readonly string serial;

            public FakeInput(string serial)
            {
                this.serial = serial;
            }

            public override string InstrumentSerialNumber
            {
                get { return this.serial; }
            }
        }

        static DeviceConfigInfo Device(string name, string serial)
        {
            return new DeviceConfigInfo
            {
                Name = name,
                Guid = Guid.NewGuid().ToString(),
                InputDeviceConfig = new FakeInput(serial),
                PeakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig()
            };
        }

        static string SessionStatus(ResultData rd, FsaCalculationOptions options)
        {
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            session.EnsureUpToDate(rd, false, options);
            if (!done.WaitOne(TimeSpan.FromSeconds(120))) return "НЕ ЗАВЕРШЁН";
            if (session.Result != null) return "РЕЗУЛЬТАТ ЕСТЬ, отказа нет";
            return session.Status ?? "";
        }

        static int RefusalSection(string path, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. слова отказа, настоящий сеанс ===");
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            if (rd.SampleInfo == null) rd.SampleInfo = new SampleInfoData();
            rd.Efficiency = null;
            rd.DeviceConfigReference = new DeviceConfigReference { Name = "RadiaCode-110" };
            rd.EnergySpectrum.SerialNumber = "A307-TEST";
            if (rd.PeakDetectionMethodConfig == null) rd.PeakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig();
            if (rd.FwhmCalibration == null)
            {
                var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                if (cfg.FwhmCalibration == null) cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            options.DbLookups = true;

            DeviceConfigManager manager = DeviceConfigManager.GetInstance();
            List<DeviceConfigInfo> keep = manager.DeviceConfigList;

            CultureInfo cultureWas = BecquerelMonitor.Properties.Resources.Culture;
            try
            {
                foreach (string lang in new[] { "en", "ru" })
                {
                    BecquerelMonitor.Properties.Resources.Culture = new CultureInfo(lang);
                    string notFoundMark = lang == "en" ? "was not found" : "не найден";
                    string ambiguousMark = lang == "en" ? "several configurations" : "несколько конфигураций";
                    string geometryMark = lang == "en" ? "needs a detector geometry" : "нужна геометрия прибора";
                    string askDevice = lang == "en" ? "Measurement Control" : "Управление измерением";
                    string assignedMark = lang == "en" ? "not assigned to this spectrum" : "спектру не назначен";
                    string curveMark = lang == "en" ? "has not been calculated" : "не посчитана";
                    string geometryText = BecquerelMonitor.Properties.Resources.ResourceManager.GetString("FSANoGeometry", new CultureInfo(lang));
                    Console.WriteLine("  --- культура {0}", lang);

                    // (а) прибора в списке нет вообще
                    rd.DeviceConfig = new DeviceConfigInfo();
                    rd.DeviceConfigReference = new DeviceConfigReference { Name = "RadiaCode-110" };
                    manager.DeviceConfigList = new List<DeviceConfigInfo>();
                    string a = SessionStatus(rd, options);
                    Console.WriteLine("    (а) список пуст: {0}", a);
                    Same(lang + " (а) не найден: называет случай", a.Contains(notFoundMark));
                    Same(lang + " (а) называет прибор и номер из файла", a.Contains("RadiaCode-110") && a.Contains("A307-TEST"));
                    Same(lang + " (а) говорит, что сделать", a.Contains(askDevice));
                    Same(lang + " (а) не прежний текст о геометрии", a != geometryText && !a.Contains(geometryMark));

                    // (б) той же модели, но другой экземпляр — по правилу открытия чужой прибор
                    manager.DeviceConfigList = new List<DeviceConfigInfo> { Device("RadiaCode-110", "OTHER-1") };
                    string b = SessionStatus(rd, options);
                    Same(lang + " (б) другой номер той же модели: не найден", b.Contains(notFoundMark), b);

                    // (в) подходят несколько
                    manager.DeviceConfigList = new List<DeviceConfigInfo> { Device("X1", "A307-TEST"), Device("X2", "A307-TEST") };
                    string c = SessionStatus(rd, options);
                    Same(lang + " (в) несколько: называет случай", c.Contains(ambiguousMark) && !c.Contains(notFoundMark), c);
                    Same(lang + " (в) говорит, что сделать", c.Contains(askDevice));

                    // (г) прибор есть в списке, спектр открыт раньше: дверь открытия не проходилась
                    DeviceConfigInfo listed = Device("RC-110 тест", "A307-TEST");
                    manager.DeviceConfigList = new List<DeviceConfigInfo> { listed };
                    string d = SessionStatus(rd, options);
                    Console.WriteLine("    (г) заведён, не назначен: {0}", d);
                    Same(lang + " (г) заведён, не назначен: называет случай и прибор",
                         d.Contains(assignedMark) && d.Contains("RC-110 тест") && d != geometryText, d);
                    Same(lang + " (г) говорит, что сделать", d.Contains(askDevice));

                    // (г2) та же дверь, что при открытии файла: теперь прибор назначен, геометрии нет
                    DocumentManager.DeviceMatch how;
                    List<DeviceConfigInfo> candidates;
                    DocumentManager.AttachDeviceConfig(rd, manager.DeviceConfigList, out how, out candidates);
                    string d2 = SessionStatus(rd, options);
                    Same(lang + " (г2) назначен, геометрии нет: прежний текст", d2 == geometryText, d2);

                    // (г3) назначен, у прибора есть кривая с геометрией, спектру не выбрана
                    string chooseMark = lang == "en" ? "Efficiency list" : "«Эффективн.»";
                    GeometryModel curveGeometry = GeometryEditorPanel.Blank();
                    GeometryPresets.Items.First(x => x.Name == "RadiaCode-110").Apply(curveGeometry);
                    var curve = new EfficiencyConfigData("A307 кривая") { Geometry = curveGeometry };
                    listed.EfficiencyConfigs.Add(curve);
                    string d3 = SessionStatus(rd, options);
                    Console.WriteLine("    (г3) кривая не выбрана: {0}", d3);
                    Same(lang + " (г3) кривая есть, не выбрана: называет список", d3.Contains(chooseMark) && d3.Contains(askDevice) && d3 != geometryText, d3);
                    Same(lang + " (г3) называет прибор", d3.Contains("RC-110 тест"));
                    // своя кривая спектра без геометрии — другой случай, прежний текст
                    rd.Efficiency = new EfficiencyConfigData("A307 без геометрии");
                    string d4 = SessionStatus(rd, options);
                    Same(lang + " (г3) своя кривая без геометрии: не этот случай", !d4.Contains(chooseMark), d4);
                    rd.Efficiency = null;
                    listed.EfficiencyConfigs.Remove(curve);
                    string d5 = SessionStatus(rd, options);
                    Same(lang + " (г3) кривую сняли: снова прежний текст", d5 == geometryText, d5);
                    rd.DeviceConfig = new DeviceConfigInfo();
                    rd.DeviceConfigReference = new DeviceConfigReference { Name = "RadiaCode-110" };

                    // (д) прибор назначен: ссылка переписана, как после выбора в панели
                    DeviceConfigInfo pick = Device("RC-110 руками", "NO-MATCH");
                    manager.DeviceConfigList = new List<DeviceConfigInfo> { pick };
                    rd.DeviceConfig = pick;
                    rd.DeviceConfigReference = pick.CreateReference();
                    string e = SessionStatus(rd, options);
                    Same(lang + " (д) назначен по Guid без геометрии: прежний текст", e == geometryText, e);

                    // (е) ссылка на Guid, которого здесь нет, — та же цепочка: не найден
                    manager.DeviceConfigList = new List<DeviceConfigInfo>();
                    string f = SessionStatus(rd, options);
                    Same(lang + " (е) Guid из файла здесь неизвестен: не найден", f.Contains(notFoundMark), f);

                    // (з) один сеанс: прибор завели — на экране не остаётся «не найден»; назначили —
                    // не остаётся «не назначен»
                    rd.DeviceConfig = new DeviceConfigInfo();
                    rd.DeviceConfigReference = new DeviceConfigReference { Name = "RadiaCode-110" };
                    manager.DeviceConfigList = new List<DeviceConfigInfo>();
                    var one = new FsaAnalysisSession();
                    var signal = new AutoResetEvent(false);
                    one.Completed += (s, ev) => signal.Set();
                    one.EnsureUpToDate(rd, false, options);
                    signal.WaitOne(TimeSpan.FromSeconds(120));
                    string before = one.Status ?? "";
                    manager.DeviceConfigList = new List<DeviceConfigInfo> { Device("RC-110 тест", "A307-TEST") };
                    one.EnsureUpToDate(rd, false, options);
                    bool rerun = signal.WaitOne(TimeSpan.FromSeconds(120));
                    Same(lang + " (з) завели прибор: разбор перезапущен, слова сменились",
                         before.Contains(notFoundMark) && rerun && (one.Status ?? "").Contains(assignedMark), one.Status);
                    DocumentManager.AttachDeviceConfig(rd, manager.DeviceConfigList, out how, out candidates);
                    one.EnsureUpToDate(rd, false, options);
                    rerun = signal.WaitOne(TimeSpan.FromSeconds(120));
                    Same(lang + " (з) назначили: разбор перезапущен, текст о геометрии", rerun && one.Status == geometryText, one.Status);

                    // (ж) старшинство: не посчитанная и отвергнутая кривая старше прибора
                    string g = FsaAnalysisSession.RefusalText(FsaRefusal.Geometry, null, "кривая", DocumentManager.DeviceMatch.None, "R", "S", null, null);
                    Same(lang + " (ж) не посчитанная кривая старше прибора", g.Contains(curveMark) && !g.Contains(notFoundMark), g);
                    string h = FsaAnalysisSession.RefusalText(FsaRefusal.Geometry, "причина", null, DocumentManager.DeviceMatch.None, "R", "S", null, null);
                    Same(lang + " (ж) отвергнутая кривая старше прибора", h.Contains("причина") && !h.Contains(notFoundMark), h);
                    string i = FsaAnalysisSession.RefusalText(FsaRefusal.NoFit, null, null, DocumentManager.DeviceMatch.None, "R", "S", null, null);
                    Same(lang + " (ж) другая причина прибором не подменяется", !i.Contains(notFoundMark), i);
                    string j = FsaAnalysisSession.RefusalText(FsaRefusal.Geometry, null, null, DocumentManager.DeviceMatch.None, null, null, null, null);
                    Same(lang + " (ж) документ без прибора: без пустых кавычек и прочерков", !j.Contains("—") && !j.Contains("{0}") && j.Contains(askDevice), j);
                    string j2 = FsaAnalysisSession.RefusalText(FsaRefusal.Geometry, null, null, DocumentManager.DeviceMatch.None, "R", null, null, null);
                    Same(lang + " (ж) имя есть, номера нет: прочерк у номера", j2.Contains("—"), j2);
                    string w = lang == "en" ? "Edit Device Configuration" : "Редактировать конфигурацию устройства";
                    Same(lang + " (ж) название окна — как его заголовок", a.Contains(w) && j.Contains(w));
                }
            }
            finally
            {
                BecquerelMonitor.Properties.Resources.Culture = cultureWas;
                manager.DeviceConfigList = keep;
            }

            return 0;
        }
    }
}
