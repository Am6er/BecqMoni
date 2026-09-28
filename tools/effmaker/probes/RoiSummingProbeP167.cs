using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace RoiSummingProbeP167
{
    /// <summary>
    /// (`AMBER133`, П167 28.09.2026) КАСКАДНОЕ СУММИРОВАНИЕ У ЗОНЫ ROI: ЕСТЬ ЛИ
    /// ПОПРАВКА И СХОДИТСЯ ЛИ ОНА С НЕЗАВИСИМЫМ АРБИТРОМ.
    ///
    /// Арбитр — перебор `roi_mc.py` ревизии П163 (`D:\BqMoni_Claude\p163\s2_ba133_eu152`:
    /// все исходы распада по схеме DDEP, эффективности из тех же `.rmx`
    /// корпуса, окно зоны ±0.75 ПШПВ по √E от ПШПВ 662 геометрии) в
    /// определении зоны приложения — `roi_mc2.py` П167: прямая только своей
    /// линии, одиночные поглощения чужих линий в окне не считаются (у 384
    /// вплотную в окно входит 356, у 303 — 276: это соседство окон, не
    /// суммирование, и K о нём не знал и прежде). Его отношение и есть
    /// множитель, который зоне нужен. Числа вписаны из прогона 28.09.2026
    /// (журнал П167, `handover/p167/`).
    ///
    /// Разделы:
    ///   А. Каждая сцена × линия: K зоны против 100/(ε·I) (есть ли в K хоть
    ///      что-то от суммирования), CF, который FSA ставит той же линии
    ///      (`FsaCascadeSummer` напрямую), множитель приложения
    ///      `BecquerelCoefficient.Summing` (если он есть в сборке) и отклонение
    ///      зоны от истины ДО (множитель 1) и ПОСЛЕ.
    ///   К1. Суммирователь приложения (`CreateLikeFsa`) против суммирователя
    ///      самого FSA после `FsaAnalyzer.Analyze` (поле `cascade` отражением):
    ///      CF линий обязаны совпасть побитово — иначе ключи разошлись.
    ///   К2. Путь зон целиком (`MeasurementResultManager.Calculate` +
    ///      `Translate`): Бк после / Бк до = множитель; ручной K, счёт и имп/с —
    ///      побитово прежние.
    ///   К3. Кривая без геометрии: множитель 1, приписка «без поправки» у
    ///      Ba-133 356 (линия в каскаде), без приписки у Cs-137 662.
    ///   К4. Панель выделения (`EnsureSelectionAnalytics`, вид без окна):
    ///      Бк панели / Бк без поправки = множитель, строка приписки = фраза
    ///      `Summing`.
    ///
    /// Матрицы берутся приложением из склада по `Guid` кривой
    /// (`config\device\response\&lt;guid&gt;.rmx` рядом с пробой); проба кладёт
    /// туда копию `.rmx` корпуса (`tools\CORPUS\corpus\geometries\response`),
    /// если её там нет, и говорит об этом.
    ///
    ///   RoiSummingProbeP167.exe [--tol=&lt;доля&gt;] [--corpus=&lt;каталог corpus&gt;]
    ///
    /// `--corpus=` нужен из worktree: склад `.rmx` в git не лежит, и корпус
    /// берётся из основного дерева (только чтение).
    ///
    /// Код возврата: 0 — на ПОСЛЕ-сборке все проверки прошли (отклонение от
    /// арбитра в допуске: 5 и 25 см — 3 %, вплотную — 6 %; контроли К1–К4);
    /// 1 — нет; 2 — ключи/файлы; 3 — сборка без `Summing` (ДО): печатает
    /// замер и выходит, не судя.
    /// </summary>
    static class Program
    {
        static int bad;
        static double tol = 0.03;

        sealed class Scene
        {
            public string Name, Carrier;
            public double Tolerance;
            public Dictionary<double, double> Arbiter = new Dictionary<double, double>();
        }

        static readonly double[][] BaLines =
        {
            new[] { 80.9979, 32.9 }, new[] { 302.8508, 18.34 }, new[] { 356.0129, 62.05 }, new[] { 383.8485, 8.94 },
        };

        static readonly double[][] EuLines =
        {
            new[] { 121.7817, 28.53 }, new[] { 244.6974, 7.55 }, new[] { 344.2785, 26.59 }, new[] { 964.057, 14.51 },
            new[] { 1408.013, 20.87 },
        };

        static List<Scene> Scenes()
        {
            var list = new List<Scene>();
            // «прямая своей линии / наблюдённое в окне» арбитра `roi_mc2.py` (П167,
            // 28.09.2026; перебор П163 в определении зоны приложения — одиночные
            // поглощения ЧУЖИХ линий в окно не считаются, это не суммирование).
            // Порядок: Ba 81, 303, 356, 384; Eu 121.8, 244.7, 344.3, 964.1, 1408.0.
            list.Add(Make("G1S_point5", "G1S16_Ba133_P5", 0.03,
                1.0654, 1.0536, 1.0363, 0.8016, 1.0694, 1.1035, 1.0236, 1.0678, 1.0315));
            list.Add(Make("G1S_point25", "G1S16_Ba133_P25", 0.03,
                1.0047, 1.0034, 1.0024, 0.9839, 1.0050, 1.0069, 1.0020, 1.0043, 1.0020));
            // вплотную — допуск шире: у суммирователя FSA и точного перебора
            // там своё расхождение до −4 % (`AMBER134`, журнал П163)
            list.Add(Make("RC103_point0", "RC103_Cs137_0cm", 0.06,
                1.1200, 1.1630, 1.0500, 0.4955, 1.1418, 1.2456, 1.0194, 1.0752, 1.0918));
            list.Add(Make("AS80_point0", "AS80_Cs137_0cm", 0.06,
                1.2701, 1.1897, 1.1072, 0.5875, 1.2944, 1.4704, 1.1056, 1.1532, 1.1359));
            return list;
        }

        static Scene Make(string name, string carrier, double tolerance, params double[] arb)
        {
            var s = new Scene { Name = name, Carrier = carrier, Tolerance = tolerance };
            for (int i = 0; i < BaLines.Length; i++) s.Arbiter[BaLines[i][0]] = arb[i];
            for (int i = 0; i < EuLines.Length; i++) s.Arbiter[EuLines[i][0]] = arb[BaLines.Length + i];
            return s;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            // (`T243`) Снимок поставочной полосы — ДО разбора ключей: проба
            // считает FSA (контроль К1) и обязана отчитаться о своих настройках.
            FsaTuningReport.Snapshot();
            string corpus = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }

            if (corpus == null)
            {
                string root = FindRoot();
                if (root == null) { Console.Error.WriteLine("корень дерева не найден — дайте --corpus="); return 2; }
                corpus = Path.Combine(root, "tools", "CORPUS", "corpus");
            }
            Console.WriteLine("корпус: {0}", corpus);

            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();

            MethodInfo summing = typeof(BecquerelCoefficient).GetMethod("Summing", BindingFlags.Public | BindingFlags.Static);
            MethodInfo likeFsa = typeof(BecquerelCoefficient).GetMethod("CreateLikeFsa", BindingFlags.Public | BindingFlags.Static);
            bool after = summing != null && likeFsa != null;
            Console.WriteLine("=== чем мерено ===");
            Console.WriteLine("сборка приложения: {0}", typeof(BecquerelCoefficient).Assembly.Location);
            Console.WriteLine("BecquerelCoefficient.Summing: {0}", after ? "есть (сборка ПОСЛЕ)" : "НЕТ (сборка ДО: множитель зоны = 1)");
            Console.WriteLine("склад матриц приложения: {0}", ResponseMatrixStore.Directory);
            Console.WriteLine("допуск к арбитру: 5 и 25 см {0} %, вплотную {1} %", F(tol * 100.0, 1), F(tol * 200.0, 1));
            Console.WriteLine();

            foreach (Scene scene in Scenes())
            {
                string path = Path.Combine(corpus, "spectra", scene.Carrier + ".xml");
                ResultData rd = Load(path);
                EfficiencyConfigData eff = rd.Efficiency;
                if (eff == null || !eff.HasGeometry) { Console.WriteLine("!! {0}: у спектра нет кривой с геометрией", scene.Carrier); bad++; continue; }
                EnsureMatrix(corpus, eff.Guid);
                // ПШПВ окна — как у арбитра: по √E от ПШПВ 662 геометрии.
                rd.FwhmCalibration = null;
                double fw = eff.Geometry.FwhmAt662Percent;
                ResponseMatrix matrix = ResponseMatrixStore.Load(eff.Guid);
                Console.WriteLine("=== А. сцена {0} (носитель {1}, кривая {2}, ПШПВ 662 {3} %, матрица {4}) ===",
                    scene.Name, scene.Carrier, eff.Name, F(fw, 2), matrix == null ? "НЕТ" : (matrix.IsValidFor(eff.Geometry) ? "годна" : "ЧУЖАЯ"));
                FsaCascadeSummer direct = matrix != null
                    ? FsaCascadeSummer.Create(matrix, EfficiencySimulator.ScintillatorNameOf(eff.Geometry))
                    : null;
                Console.WriteLine("  {0,-8} {1,9} {2,8} {3,8} {4,8} {5,8} {6,8} {7,9} {8,9}  {9}",
                    "линия", "K·εI/100", "CF FSA", "арбитр", "Σ×прил.", "CF лин.", "чужие%", "зона ДО%", "ПОСЛЕ%", "приписка");
                RunLines(scene, rd, "Ba-133", BaLines, fw, direct, summing);
                RunLines(scene, rd, "Eu-152", EuLines, fw, direct, summing);
                Console.WriteLine();
            }

            if (!after)
            {
                Console.WriteLine("ИТОГ: сборка ДО — замер напечатан, контроли К1–К4 не судятся (код 3)");
                return 3;
            }

            Control1(corpus, likeFsa);
#if !BEFORE
            Control2(corpus);
            Control3(corpus);
            Control4(corpus);
#endif

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: ok — зона сходится с арбитром в допуске, контроли прошли"
                                       : "ИТОГ: !! отказов {0}", bad);
            return bad == 0 ? 0 : 1;
        }

        static void RunLines(Scene scene, ResultData rd, string nuclide, double[][] lines, double fw,
                             FsaCascadeSummer direct, MethodInfo summing)
        {
            foreach (double[] line in lines)
            {
                double e = line[0], y = line[1];
                double w = 0.75 * fw / 100.0 * Math.Sqrt(662.0 * e);
                var roi = new ROIDefinitionData
                {
                    Name = nuclide, Enabled = true, PeakEnergy = e, Intencity = y,
                    LowerLimit = e - w, UpperLimit = e + w, AutoBecquerelCoefficient = true,
                };
                BecquerelCoefficient.Result k = BecquerelCoefficient.Resolve(roi, rd.Efficiency);
                double eps, err;
                FsaEfficiency curve = FsaEfficiency.FromConfig(rd.Efficiency);
                curve.TryEval(e, out eps, out err);
                double kNorm = k.Value * eps * y / 100.0;

                double cfFsa = double.NaN;
                if (direct != null)
                {
                    var comp = new FsaComponent(nuclide, FsaComponentKind.Single);
                    comp.Lines.Add(new FsaLine(nuclide, e, y));
                    FsaCascadeSummer.Correction c = direct.For(comp);
                    if (c != null && c.Notes != null)
                        foreach (FsaCascadeSummer.LineNote n in c.Notes) if (Math.Abs(n.EnergyKev - e) < 0.5) cfFsa = n.Cf;
                }

                double factor = 1.0, lineCf = double.NaN, share = double.NaN;
                string note = "";
                if (summing != null)
                {
                    object r = summing.Invoke(null, new object[] { nuclide, e, y, e - w, e + w, rd });
                    Type t = r.GetType();
                    factor = (double)t.GetField("Factor").GetValue(r);
                    lineCf = (double)t.GetField("LineCf").GetValue(r);
                    share = (double)t.GetField("WindowSumShare").GetValue(r);
                    note = (string)t.GetField("Note").GetValue(r) ?? "";
                    if (!(bool)t.GetField("Applied").GetValue(r))
                    {
                        note += " / " + (string)t.GetField("Problem").GetValue(r);
                        bad++;
                    }
                }

                double arb = scene.Arbiter[e];
                double before = (1.0 / arb - 1.0) * 100.0;
                double afterDev = (factor / arb - 1.0) * 100.0;
                Console.WriteLine("  {0,-8} {1,9} {2,8} {3,8} {4,8} {5,8} {6,8} {7,9} {8,9}  {9}",
                    F(e, 1), F(kNorm, 6), F(cfFsa, 4), F(arb, 4), F(factor, 4), F(lineCf, 4), F(share * 100.0, 2),
                    F(before, 2), summing != null ? F(afterDev, 2) : "-", note);
                double allowed = scene.Tolerance * tol / 0.03;
                if (summing != null && Math.Abs(factor / arb - 1.0) > allowed)
                {
                    Console.WriteLine("    !! {0} {1} кэВ: зона ПОСЛЕ {2} % от истины (допуск {3} %)", scene.Name, F(e, 1), F(afterDev, 2), F(allowed * 100.0, 1));
                    bad++;
                }
            }
        }

        /// <summary>К1: суммирователь приложения против суммирователя самого FSA.</summary>
        static void Control1(string corpus, MethodInfo likeFsa)
        {
            Console.WriteLine("=== К1: CreateLikeFsa против суммирователя FSA после Analyze (G1S16_Ba133_P5) ===");
            ResultData rd = Load(Path.Combine(corpus, "spectra", "G1S16_Ba133_P5.xml"));
            EfficiencyConfigData eff = rd.Efficiency;
            ResponseMatrix matrix = ResponseMatrixStore.Load(eff.Guid);
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            double dead = 0.0;
            try { dead = rd.DeviceConfig.InputDeviceConfig.DeadTime(); } catch (Exception) { }

            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            FsaMatrixBinding.Bind(analyzer, eff.Geometry, matrix);
            analyzer.CoincidenceWindowSec = dead;
            var library = new List<FsaComponent>();
            var ba = new FsaComponent("Ba-133", FsaComponentKind.Chain);
            foreach (double[] l in BaLines) ba.Lines.Add(new FsaLine("Ba-133", l[0], l[1]));
            library.Add(ba);
            string refusal;
            FsaEfficiency curve = FsaEfficiency.FromConfig(eff, out refusal);
            FwhmCalibration fwhm = rd.FwhmCalibration;
            FsaTuningReport.Print(analyzer, "К1, G1S16_Ba133_P5");
            FsaResult result = null;
            try { result = analyzer.Analyze(rd.EnergySpectrum, null, fwhm, library, curve); }
            catch (Exception ex) { Console.WriteLine("  разбор бросил: {0}", ex.Message); }
            FieldInfo f = typeof(FsaAnalyzer).GetField("cascade", BindingFlags.NonPublic | BindingFlags.Instance);
            FsaCascadeSummer fsa = f != null ? (FsaCascadeSummer)f.GetValue(analyzer) : null;
            Console.WriteLine("  разбор: {0}; мёртвое время {1} с; суммирователь FSA: {2}",
                result == null ? "нет результата" : "есть", R(dead), fsa == null ? "НЕТ" : "есть");
            FsaCascadeSummer mine = (FsaCascadeSummer)likeFsa.Invoke(null, new object[] { matrix, eff.Geometry, options, dead });
            if (fsa == null || mine == null) { Console.WriteLine("  !! сравнивать нечего"); bad++; return; }

            foreach (double[][] set in new[] { BaLines, EuLines })
            {
                string nuc = set == BaLines ? "Ba-133" : "Eu-152";
                foreach (double[] l in set)
                {
                    double a = CfOf(fsa, nuc, l[0], l[1]);
                    double b = CfOf(mine, nuc, l[0], l[1]);
                    double sa = SumsOf(fsa, nuc, l[0], l[1]);
                    double sb = SumsOf(mine, nuc, l[0], l[1]);
                    bool same = a.Equals(b) && sa.Equals(sb);
                    Console.WriteLine("  {0} {1,9}: CF FSA {2} / приложение {3}; Σ площадей сумм {4} / {5} {6}",
                        nuc, F(l[0], 3), R(a), R(b), R(sa), R(sb), same ? "ok" : "!!");
                    if (!same) bad++;
                }
            }
        }

        static double CfOf(FsaCascadeSummer s, string nuc, double e, double y)
        {
            var comp = new FsaComponent(nuc, FsaComponentKind.Single);
            comp.Lines.Add(new FsaLine(nuc, e, y));
            FsaCascadeSummer.Correction c = s.For(comp);
            if (c == null || c.Notes == null) return double.NaN;
            foreach (FsaCascadeSummer.LineNote n in c.Notes) if (Math.Abs(n.EnergyKev - e) < 0.5) return n.Cf;
            return double.NaN;
        }

        static double SumsOf(FsaCascadeSummer s, string nuc, double e, double y)
        {
            var comp = new FsaComponent(nuc, FsaComponentKind.Single);
            comp.Lines.Add(new FsaLine(nuc, e, y));
            FsaCascadeSummer.Correction c = s.For(comp);
            double sum = 0.0;
            if (c != null && c.SumPeaks != null) foreach (FsaCascadeSummer.SumPeak p in c.SumPeaks) sum += p.Area;
            return sum;
        }

#if !BEFORE
        // Разделы К2 и К3 зовут новый API напрямую; сборка ДО собирает пробу с
        // /d:BEFORE (вручную, csc), `build_all.ps1` — без него.

        /// <summary>К2: путь зон целиком; ручной K, счёт, имп/с — побитово прежние.</summary>
        static void Control2(string corpus)
        {
            Console.WriteLine();
            Console.WriteLine("=== К2: путь зон целиком (Calculate + Translate), G1S16_Ba133_P5 ===");
            ResultData rd = Load(Path.Combine(corpus, "spectra", "G1S16_Ba133_P5.xml"));
            // ПШПВ окна — та же, что у зоны ниже (`Zone` снимает калибровку ПШПВ).
            rd.FwhmCalibration = null;
            foreach (double[] l in BaLines)
            {
                double e = l[0], y = l[1];
                double w = 0.75 * rd.Efficiency.Geometry.FwhmAt662Percent / 100.0 * Math.Sqrt(662.0 * e);
                double bqAuto = Zone(rd, e, y, w, true, ResultTranslation.Becquerels, out double fAuto, out string noteAuto);
                double bqManual = Zone(rd, e, y, w, false, ResultTranslation.Becquerels, out double fManual, out string noteManual);
                double cps = Zone(rd, e, y, w, true, ResultTranslation.CountsPerSecond, out double fCps, out string noteCps);
                double counts = Zone(rd, e, y, w, true, ResultTranslation.Nothing, out double fCounts, out string noteCounts);
                BecquerelCoefficient.SummingResult s = BecquerelCoefficient.Summing("Ba-133", e, y, e - w, e + w, rd);
                // Бк без поправки: cps · K_кривой (K тем же Resolve)
                var roi = new ROIDefinitionData { Name = "Ba-133", PeakEnergy = e, Intencity = y, LowerLimit = e - w, UpperLimit = e + w, AutoBecquerelCoefficient = true };
                double k = BecquerelCoefficient.Resolve(roi, rd.Efficiency).Value;
                double bqPlain = cps * k;
                Console.WriteLine("  {0,9} кэВ: Бк зоны {1} (без поправки {2}, отношение {3}; множитель строки {4}, Summing {5}); приписка «{6}»",
                    F(e, 3), R(bqAuto), R(bqPlain), R(bqAuto / bqPlain), R(fAuto), R(s.Factor), noteAuto);
                Console.WriteLine("             ручной K: множитель {0}, приписка «{1}»; имп/с множитель {2} приписка «{3}»; счёт множитель {4}",
                    R(fManual), noteManual ?? "", R(fCps), noteCps ?? "", R(fCounts));
                if (Math.Abs(bqAuto / bqPlain - s.Factor) > 1e-12 || fAuto != s.Factor) { Console.WriteLine("    !! Бк после / до ≠ множителю"); bad++; }
                if (fManual != 1.0 || noteManual != null) { Console.WriteLine("    !! ручной K тронут"); bad++; }
                if (fCps != 1.0 || noteCps != null || fCounts != 1.0) { Console.WriteLine("    !! счёт или имп/с тронуты"); bad++; }
            }
        }

        static double Zone(ResultData source, double e, double y, double w, bool auto, ResultTranslation tr,
                           out double factor, out string note)
        {
            var prim = new ROISimpleDifferenceData
            {
                LowerLimit = e - w, UpperLimit = e + w, Coefficient = 1.0, CoefficientError = 0.0,
                OperationType = "Addition", Operation = ROIPrimitiveOperation.OperationsMap["Addition"],
            };
            var zone = new ROIDefinitionData
            {
                Name = "Ba-133", Enabled = true, PeakEnergy = e, LowerLimit = e - w, UpperLimit = e + w,
                Intencity = y, BecquerelCoefficient = 1.0e-3, AutoBecquerelCoefficient = auto,
            };
            zone.ROIPrimitives.Add(prim);
            var roiCfg = new ROIConfigData();
            roiCfg.ROIDefinitions.Add(zone);
            var rd = new ResultData
            {
                EnergySpectrum = source.EnergySpectrum,
                BackgroundEnergySpectrum = source.BackgroundEnergySpectrum,
                Efficiency = source.Efficiency,
                DeviceConfig = source.DeviceConfig,
                PeakDetectionMethodConfig = source.PeakDetectionMethodConfig,
                ROIConfig = roiCfg,
            };
            rd.FwhmCalibration = null;
            var manager = new MeasurementResultManager();
            MeasurementResultCollection counts = manager.Calculate(rd);
            MeasurementResultCollection outc = manager.Translate(counts, tr);
            MeasurementResult m = outc.ResultList[0];
            factor = m.SummingFactor;
            note = m.SummingNote;
            return m.ResultValue;
        }

        /// <summary>К3: кривая без геометрии.</summary>
        static void Control3(string corpus)
        {
            Console.WriteLine();
            Console.WriteLine("=== К3: кривая без геометрии — множитель 1, приписка только у линии каскада ===");
            ResultData rd = Load(Path.Combine(corpus, "spectra", "G1S16_Ba133_P5.xml"));
            EfficiencyConfigData bare = rd.Efficiency.Copy();
            bare.Geometry = null;
            rd.Efficiency = bare;
            BecquerelCoefficient.SummingResult ba = BecquerelCoefficient.Summing("Ba-133", 356.0129, 62.05, 330, 382, rd);
            BecquerelCoefficient.SummingResult cs = BecquerelCoefficient.Summing("Cs-137", 661.657, 85.1, 620, 700, rd);
            Console.WriteLine("  HasGeometry {0}", bare.HasGeometry);
            Console.WriteLine("  Ba-133 356: множитель {0}, применено {1}, приписка «{2}», причина «{3}»", R(ba.Factor), ba.Applied, ba.Note, ba.Problem);
            Console.WriteLine("  Cs-137 662: множитель {0}, применено {1}, приписка «{2}», причина «{3}»", R(cs.Factor), cs.Applied, cs.Note ?? "", cs.Problem);
            if (ba.Factor != 1.0 || ba.Applied || string.IsNullOrEmpty(ba.Note)) { Console.WriteLine("    !! Ba-133 356 без геометрии: ждали множитель 1 и приписку"); bad++; }
            if (cs.Factor != 1.0 || cs.Applied || cs.Note != null) { Console.WriteLine("    !! Cs-137 662: приписки быть не должно"); bad++; }
        }

        /// <summary>К4: панель выделения — множитель в беккерелях и строка приписки.</summary>
        static void Control4(string corpus)
        {
            Console.WriteLine();
            Console.WriteLine("=== К4: панель выделения (EnsureSelectionAnalytics), G1S16_Ba133_P5, пик Ba-133 356 ===");
            ResultData rd = Load(Path.Combine(corpus, "spectra", "G1S16_Ba133_P5.xml"));
            EnergySpectrum fg = rd.EnergySpectrum;
            EnergyCalibration cal = fg.EnergyCalibration;
            const double e = 356.0129, y = 62.05;
            int c0 = (int)Math.Ceiling(PolynomialEnergyCalibration.ChannelOf(cal, 336.0, fg.NumberOfChannels));
            int c1 = (int)Math.Floor(PolynomialEnergyCalibration.ChannelOf(cal, 377.0, fg.NumberOfChannels));
            int peakChannel = (int)Math.Round(PolynomialEnergyCalibration.ChannelOf(cal, e, fg.NumberOfChannels));
            rd.DetectedPeaks.Clear();
            rd.DetectedPeaks.Add(new Peak
            {
                Energy = e, Channel = peakChannel, Count = 1000, FWHM = 10.0, SNR = 100.0,
                Nuclide = new NuclideDefinition { Name = "Ba-133", Energy = e, Intencity = y, Visible = true, Sets = new HashSet<Guid>() },
            });
            rd.Visible = true;

            Type tv = typeof(EnergySpectrumView);
            object view = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(tv);
            Action<string, object> set = (name, value) =>
            {
                FieldInfo fi = tv.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + name);
                fi.SetValue(view, value);
            };
            EnergySpectrum bg = SpectrumAriphmetics.BackgroundInScaleOf(rd.BackgroundEnergySpectrum, fg);
            set("energySpectrum", fg);
            set("backgroundEnergySpectrum", bg);
            set("substractedEnergySpectrum", null);
            set("normByEffEnergySpectrum", null);
            set("energyCalibration", cal);
            set("baseEnergyCalibration", cal);
            set("backgroundEnergyCalibration", bg.EnergyCalibration);
            set("backgroundNumberOfChannels", bg.NumberOfChannels);
            set("selectionStart", c0);
            set("selectionEnd", c1);
            set("peakMode", PeakMode.Visible);
            set("backgroundMode", BackgroundMode.Invisible);
            set("activeResultData", rd);
            var gcm = new GlobalConfigManager();
            var gci = new GlobalConfigInfo();
            if (gci.ColorConfig != null && (gci.ColorConfig.SpectrumColorList == null || gci.ColorConfig.SpectrumColorList.Count == 0))
            {
                gci.ColorConfig.InitializeSpectrumColor();
            }
            gcm.GlobalConfig = gci;
            set("globalConfigManager", gcm);
            set("nuclideManager", NuclideDefinitionManager.GetInstance());
            set("selectionAnalyticsDirty", true);
            set("selectionAnalytics", null);
            set("selectionFWHM", 0.0);
            tv.GetMethod("EnsureSelectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
            object an = tv.GetField("selectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            Type ta = an.GetType();
            Func<string, object> get = name => ta.GetProperty(name).GetValue(an, null);
            double activity = (double)get("Activity");
            string note = (string)get("ActivitySummingNote");
            string refusal = (string)get("ActivityRefusal");
            double fgCounts = (double)get("FgCounts"), bgCounts = (double)get("BgCounts");
            double fgTime = fg.EffectiveLiveTime, bgTime = rd.BackgroundEnergySpectrum.EffectiveLiveTime;
            BecquerelCoefficient.LineResult k = BecquerelCoefficient.ForLine(e, y, rd.Efficiency);
            BecquerelCoefficient.SummingResult sr = BecquerelCoefficient.Summing("Ba-133", e, y,
                cal.ChannelToEnergy(c0 - 0.5), cal.ChannelToEnergy(c1 + 0.5), rd);
            double plain = ROIAriphmetics.CalculateActivity(k.Value, fgCounts, fgTime, bgCounts, bgTime);
            Console.WriteLine("  выделение каналы {0}…{1}; отказ «{2}»; Бк панели {3}; Бк без поправки {4}; отношение {5}; Summing {6}",
                c0, c1, refusal ?? "", R(activity), R(plain), R(activity / plain), R(sr.Factor));
            Console.WriteLine("  строка панели: «{0}»", note ?? "");
            if (!(activity > 0.0) || Math.Abs(activity / plain - sr.Factor) > 1e-9) { Console.WriteLine("    !! Бк панели / без поправки ≠ множителю"); bad++; }
            if (note != sr.Problem || string.IsNullOrEmpty(note)) { Console.WriteLine("    !! строка панели не та"); bad++; }
        }

#endif

        static void EnsureMatrix(string corpus, string guid)
        {
            string dst = ResponseMatrixStore.PathOf(guid);
            if (File.Exists(dst)) return;
            string src = Path.Combine(corpus, "geometries", "response", guid + ".rmx");
            if (!File.Exists(src)) { Console.WriteLine("  ⚠ в корпусе нет {0}", src); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst);
            Console.WriteLine("  (матрица корпуса {0}.rmx положена в склад пробы)", guid);
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            var pcal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pcal != null) pcal.CheckCalibration(rd.EnergySpectrum.NumberOfChannels);
            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                rd.FwhmCalibration = cfg.FwhmCalibration ?? FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
            }
            return rd;
        }

        static string FindRoot()
        {
            string d = AppDomain.CurrentDomain.BaseDirectory;
            while (d != null)
            {
                if (File.Exists(Path.Combine(d, "TODO.md")) && Directory.Exists(Path.Combine(d, "tools"))) return d;
                d = Path.GetDirectoryName(d.TrimEnd('\\'));
            }
            return null;
        }

        static string F(double v, int d)
        {
            return double.IsNaN(v) ? "-" : v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
    }
}
