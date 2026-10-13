using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml.Serialization;

namespace BqSummingProbeP256
{
    /// <summary>
    /// (П256, 14.10.2026) КОЭФФИЦИЕНТ БЕККЕРЕЛЕЙ ПАНЕЛИ ВЫДЕЛЕНИЯ ПОСЛЕ СНЯТИЯ ЗОН.
    ///
    /// Зоны ROI сняты 07.10.2026 (`AMBER208`), и вместе с ними — проба
    /// `RoiSummingProbeP167`, единственная, что сверяла множитель
    /// каскадного суммирования `BecquerelCoefficient.Summing` с независимым
    /// арбитром. Сам множитель жив: его по-прежнему берёт панель выделения
    /// (`EnergySpectrumView.EnsureSelectionAnalytics`). Строка `T268` (на
    /// rev45 «−13.87 % от арбитра у RC103_point0 384 кэВ») ушла в `DONE.md`
    /// переносом, без пересчёта арбитра. Здесь он пересчитан.
    ///
    /// Разделы:
    ///   А. Сцена × линия: множитель приложения против арбитра `roi_mc2.py`
    ///      (П167; перебор схемы DDEP на `.rmx` корпуса, окно ±0.75 ПШПВ по √E),
    ///      ПЕРЕСЧИТАННОГО 14.10.2026 на нынешних матрицах корпуса (phys=26) —
    ///      числа ниже (`Scenes`) и в журнале
    ///      `handover/handover-2026-10-14-p256-dose-coefficient-review.md`. Допуск: 5 и 25 см — 3 %,
    ///      вплотную — 6 % (у суммирователя FSA и перебора там своё
    ///      расхождение, `AMBER134`). Рядом — арбитр 28.09.2026 (прежняя
    ///      матрица), чтобы видно было, откуда `T268`.
    ///   К1. Суммирователь приложения (`CreateLikeFsa`) против суммирователя
    ///      самого FSA после `Analyze` — CF и площади сумм побитово.
    ///   П. Панель выделения на спектрах поверки ЛСРМ 2016 (Gamma-1S, точка
    ///      5 см) против паспорта, приведённого к началу набора: Бк панели
    ///      (брутто − фон) и Бк по площади над прямой подложкой — оба с K и
    ///      множителем панели; отношение «Бк панели / без поправки» обязано
    ///      равняться множителю `Summing` (контроль). Сцена подобрана по
    ///      паспорту Cs-137 (`AMBER153`) — согласие Cs-137 по построению,
    ///      Ba-133, Co-60 и Eu-152 — проверка формы кривой и суммирования.
    ///
    ///   BqSummingProbeP256.exe [--corpus=&lt;каталог corpus&gt;]
    ///
    /// Код: 0 — А в допуске, К1 побитово, контроль П сошёлся; 1 — нет; 2 — оснастка.
    /// Матрицы корпуса кладутся в склад ПРОБЫ (рядом с exe), склад корпуса
    /// только читается.
    /// </summary>
    static class Program
    {
        static int bad;
        static int outsideOld;

        sealed class Scene
        {
            public string Name, Carrier;
            public double Tolerance, FwhmPercent;
            public double[] Now, Old;
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

        // Порядок: Ba 81, 303, 356, 384; Eu 121.8, 244.7, 344.3, 964.1, 1408.0.
        // Now — `roi_mc2.py` 14.10.2026 на `.rmx` корпуса (phys=26); Old — 28.09.2026.
        static List<Scene> Scenes()
        {
            return new List<Scene>
            {
                new Scene { Name = "G1S_point5_p16", Carrier = "G1S16_Ba133_P5", Tolerance = 0.03, FwhmPercent = 6.44,
                    Now = new[] { 1.0627, 1.0514, 1.0348, 0.8078, 1.0667, 1.0993, 1.0227, 1.0650, 1.0301 },
                    Old = new[] { 1.0654, 1.0536, 1.0363, 0.8016, 1.0694, 1.1035, 1.0236, 1.0678, 1.0315 } },
                new Scene { Name = "G1S_point25", Carrier = "G1S16_Ba133_P25", Tolerance = 0.03, FwhmPercent = 6.44,
                    Now = new[] { 1.0047, 1.0034, 1.0024, 0.9839, 1.0050, 1.0070, 1.0020, 1.0043, 1.0020 },
                    Old = new[] { 1.0047, 1.0034, 1.0024, 0.9839, 1.0050, 1.0069, 1.0020, 1.0043, 1.0020 } },
                new Scene { Name = "RC103_point0", Carrier = "RC103_Cs137_0cm", Tolerance = 0.06, FwhmPercent = 8.49,
                    Now = new[] { 1.1372, 1.1970, 1.0550, 0.4345, 1.1573, 1.2687, 1.0188, 1.0739, 1.0926 },
                    Old = new[] { 1.1200, 1.1630, 1.0500, 0.4955, 1.1418, 1.2456, 1.0194, 1.0752, 1.0918 } },
                new Scene { Name = "AS80_point0", Carrier = "AS80_Cs137_0cm", Tolerance = 0.06, FwhmPercent = 7.65,
                    Now = new[] { 1.2703, 1.1899, 1.1067, 0.5878, 1.2943, 1.4703, 1.1054, 1.1533, 1.1357 },
                    Old = new[] { 1.2701, 1.1897, 1.1072, 0.5875, 1.2944, 1.4704, 1.1056, 1.1532, 1.1359 } },
            };
        }

        sealed class Passport
        {
            public string Spectrum, Nuclide;
            public double Becquerel, HalfLifeYears;
            public DateTime Date;
            public double[][] Lines;
        }

        // Паспорта — `manifest.csv` корпуса (колонка why): поверка ЛСРМ 2016,
        // «Точечная-5см»; все на 01.10.2008, dA = 2 %. Периоды — DDEP.
        static readonly Passport[] Passports =
        {
            new Passport { Spectrum = "G1S16_Cs137_P5", Nuclide = "Cs-137", Becquerel = 94200.0, HalfLifeYears = 30.08,
                Date = new DateTime(2008, 10, 1), Lines = new[] { new[] { 661.657, 85.10 } } },
            // Ba-133 — только 356: у 303 и 384 в окне ±1.5 ПШПВ NaI лежат 276 и
            // 356 (соседство окон, не коэффициент); их множитель судит раздел А.
            new Passport { Spectrum = "G1S16_Ba133_P5", Nuclide = "Ba-133", Becquerel = 44100.0, HalfLifeYears = 10.539,
                Date = new DateTime(2008, 10, 1), Lines = new[] { new[] { 356.0129, 62.05 } } },
            new Passport { Spectrum = "G1S16_Co60_P5", Nuclide = "Co-60", Becquerel = 107800.0, HalfLifeYears = 5.2711,
                Date = new DateTime(2008, 10, 1), Lines = new[] { new[] { 1173.228, 99.85 }, new[] { 1332.492, 99.9826 } } },
            new Passport { Spectrum = "G1S16_Eu152_P5", Nuclide = "Eu-152", Becquerel = 46700.0, HalfLifeYears = 13.522,
                Date = new DateTime(2008, 10, 1), Lines = new[] { new[] { 121.7817, 28.53 }, new[] { 344.2785, 26.59 }, new[] { 1408.013, 20.87 } } },
        };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();
            string corpus = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }

            if (corpus == null)
            {
                string root = FindRoot();
                if (root == null) { Console.Error.WriteLine("корень дерева не найден — дайте --corpus="); return 2; }
                corpus = Path.Combine(root, "tools", "CORPUS", "corpus");
            }

            GlobalConfigManager.GetInstance();
            Console.WriteLine("сборка приложения: {0}", typeof(BecquerelCoefficient).Assembly.Location);
            Console.WriteLine("склад матриц пробы: {0}", ResponseMatrixStore.Directory);
            Console.WriteLine("корпус: {0}", corpus);
            Console.WriteLine();

            foreach (Scene scene in Scenes())
            {
                ResultData rd = Load(Path.Combine(corpus, "spectra", scene.Carrier + ".xml"));
                if (rd == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry)
                {
                    Console.WriteLine("ОСНАСТКА: {0} — нет кривой с геометрией", scene.Carrier);
                    return 2;
                }

                if (!EnsureMatrix(corpus, rd.Efficiency.Guid)) return 2;
                rd.FwhmCalibration = null;   // окно и ПШПВ — по геометрии, как у арбитра
                ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency.Guid);
                Console.WriteLine("=== А. {0} (носитель {1}, кривая {2}; ПШПВ 662: окно {3} %, геометрия {4} %; матрица {5}) ===",
                    scene.Name, scene.Carrier, rd.Efficiency.Name, F(scene.FwhmPercent, 2),
                    F(rd.Efficiency.Geometry.FwhmAt662Percent, 2),
                    matrix == null ? "НЕТ" : (matrix.IsValidFor(rd.Efficiency.Geometry) ? "годна" : "ЧУЖАЯ"));
                Console.WriteLine("  {0,-8} {1,8} {2,8} {3,8} {4,8} {5,8} {6,9} {7,9}",
                    "линия", "арб.нов", "арб.стар", "Σ×прил.", "CF лин.", "чужие%", "к нов.%", "к стар.%");
                int i = 0;
                foreach (double[][] set in new[] { BaLines, EuLines })
                {
                    string nuc = set == BaLines ? "Ba-133" : "Eu-152";
                    foreach (double[] line in set)
                    {
                        double e = line[0], y = line[1];
                        double w = 0.75 * scene.FwhmPercent / 100.0 * Math.Sqrt(662.0 * e);
                        BecquerelCoefficient.SummingResult s = BecquerelCoefficient.Summing(nuc, e, y, e - w, e + w, rd);
                        double now = scene.Now[i], old = scene.Old[i];
                        i++;
                        double devNow = (s.Factor / now - 1.0) * 100.0;
                        double devOld = (s.Factor / old - 1.0) * 100.0;
                        Console.WriteLine("  {0,-8} {1,8} {2,8} {3,8} {4,8} {5,8} {6,9} {7,9}{8}",
                            F(e, 1), F(now, 4), F(old, 4), F(s.Factor, 4), F(s.LineCf, 4), F(s.WindowSumShare * 100.0, 2),
                            F(devNow, 2), F(devOld, 2), s.Applied ? "" : "  НЕ ПРИМЕНЕНО: " + s.Problem);
                        if (Math.Abs(devOld) > scene.Tolerance * 100.0)
                        {
                            outsideOld++;
                        }

                        if (!s.Applied || Math.Abs(devNow) > scene.Tolerance * 100.0)
                        {
                            Console.WriteLine("    !! {0} {1} кэВ: {2} % от арбитра (допуск {3} %)", scene.Name, F(e, 1), F(devNow, 2),
                                F(scene.Tolerance * 100.0, 1));
                            bad++;
                        }
                    }
                }

                Console.WriteLine();
            }

            // Положительный контроль судьи А: против арбитра 28.09.2026 (прежняя
            // матрица) та же мерка обязана поймать `T268` — RC103_point0 384 кэВ.
            Console.WriteLine("положительный контроль А: против арбитра 28.09.2026 вне допуска линий — {0} (ждём ≥ 1, это T268)",
                outsideOld);
            if (outsideOld < 1)
            {
                Console.WriteLine("  !! судья А не различает: прежний арбитр прошёл бы целиком");
                bad++;
            }

            Console.WriteLine();
            Control1(corpus);
            PassportSection(corpus);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: РАЗОШЛОСЬ, отказов " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>К1: суммирователь приложения против суммирователя самого FSA.</summary>
        static void Control1(string corpus)
        {
            Console.WriteLine("=== К1: CreateLikeFsa против суммирователя FSA после Analyze (G1S16_Ba133_P5) ===");
            ResultData rd = Load(Path.Combine(corpus, "spectra", "G1S16_Ba133_P5.xml"));
            EfficiencyConfigData eff = rd.Efficiency;
            EnsureMatrix(corpus, eff.Guid);
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
            FsaTuningReport.Print(analyzer, "К1, G1S16_Ba133_P5");
            FsaResult result = null;
            try { result = analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library, curve); }
            catch (Exception ex) { Console.WriteLine("  разбор бросил: {0}", ex.Message); }
            FieldInfo f = typeof(FsaAnalyzer).GetField("cascade", BindingFlags.NonPublic | BindingFlags.Instance);
            FsaCascadeSummer fsa = f != null ? (FsaCascadeSummer)f.GetValue(analyzer) : null;
            FsaCascadeSummer mine = BecquerelCoefficient.CreateLikeFsa(matrix, eff.Geometry, options, dead);
            Console.WriteLine("  разбор: {0}; мёртвое время прибора {1} с; окно суммирователя FSA {2} с, приложения {3} с",
                result == null ? "нет результата" : "есть", R(dead),
                fsa == null ? "-" : R(fsa.CoincidenceWindowSec), mine == null ? "-" : R(mine.CoincidenceWindowSec));
            if (fsa == null || mine == null) { Console.WriteLine("  !! сравнивать нечего (поле cascade: {0})", f == null ? "нет" : "есть"); bad++; return; }

            foreach (double[][] set in new[] { BaLines, EuLines })
            {
                string nuc = set == BaLines ? "Ba-133" : "Eu-152";
                foreach (double[] l in set)
                {
                    double a = CfOf(fsa, nuc, l[0], l[1]), b = CfOf(mine, nuc, l[0], l[1]);
                    double sa = SumsOf(fsa, nuc, l[0], l[1]), sb = SumsOf(mine, nuc, l[0], l[1]);
                    bool same = a.Equals(b) && sa.Equals(sb);
                    Console.WriteLine("  {0} {1,9}: CF FSA {2} / приложение {3}; Σ сумм {4} / {5} {6}",
                        nuc, F(l[0], 3), R(a), R(b), R(sa), R(sb), same ? "ok" : "!!");
                    if (!same) bad++;
                }
            }

            Console.WriteLine();
        }

        /// <summary>П: панель выделения против паспорта.</summary>
        static void PassportSection(string corpus)
        {
            Console.WriteLine("=== П. Панель выделения (EnsureSelectionAnalytics) против паспорта, G1S16 точка 5 см ===");
            Console.WriteLine("  окно — ±1.5 ПШПВ спектра у линии; «брутто» = (fg − фон)·K·Σ/t (число панели),");
            Console.WriteLine("  «над подложкой» = площадь над прямой между краями окна·K·Σ/t; паспорт — к началу набора");
            Console.WriteLine("  {0,-15} {1,9} {2,9} {3,7} {4,10} {5,10} {6,10} {7,8} {8,8}",
                "спектр", "линия", "K", "Σ×", "паспорт", "брутто", "над подл.", "брут/п", "подл/п");
            foreach (Passport p in Passports)
            {
                ResultData rd = Load(Path.Combine(corpus, "spectra", p.Spectrum + ".xml"));
                if (rd == null || rd.Efficiency == null || rd.BackgroundEnergySpectrum == null)
                {
                    Console.WriteLine("  ОСНАСТКА: {0} — нет кривой или фона", p.Spectrum);
                    bad++;
                    continue;
                }

                EnsureMatrix(corpus, rd.Efficiency.Guid);
                double years = (rd.StartTime - p.Date).TotalDays / 365.2422;
                double passportNow = p.Becquerel * Math.Pow(0.5, years / p.HalfLifeYears);
                foreach (double[] line in p.Lines)
                {
                    Panel(rd, p, line[0], line[1], passportNow);
                }
            }
        }

        static void Panel(ResultData rd, Passport p, double e, double y, double passport)
        {
            EnergySpectrum fg = rd.EnergySpectrum;
            EnergyCalibration cal = fg.EnergyCalibration;
            double peakChannel = PolynomialEnergyCalibration.ChannelOf(cal, e, fg.NumberOfChannels);
            double fwhmCh = rd.FwhmCalibration.ChannelToFwhm(peakChannel);
            int c0 = (int)Math.Round(peakChannel - 1.5 * fwhmCh);
            int c1 = (int)Math.Round(peakChannel + 1.5 * fwhmCh);
            rd.DetectedPeaks.Clear();
            rd.DetectedPeaks.Add(new Peak
            {
                Energy = e, Channel = (int)Math.Round(peakChannel), Count = 1000, FWHM = fwhmCh, SNR = 100.0,
                Nuclide = new NuclideDefinition { Name = p.Nuclide, Energy = e, Intencity = y, Visible = true, Sets = new HashSet<Guid>() },
            });
            rd.Visible = true;

            Type tv = typeof(EnergySpectrumView);
            object view = FormatterServices.GetUninitializedObject(tv);
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
            set("globalConfigManager", Config());
            set("nuclideManager", NuclideDefinitionManager.GetInstance());
            set("selectionAnalyticsDirty", true);
            set("selectionAnalytics", null);
            set("selectionFWHM", 0.0);
            tv.GetMethod("EnsureSelectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
            object an = tv.GetField("selectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            Type ta = an.GetType();
            Func<string, object> get = name => ta.GetProperty(name).GetValue(an, null);
            double activity = (double)get("Activity");
            string refusal = (string)get("ActivityRefusal");
            double fgCounts = (double)get("FgCounts"), bgCounts = (double)get("BgCounts");
            double peakCounts = (double)get("PeakCounts");
            double decay = (double)get("ActivityDecayFactor");
            double fgTime = fg.EffectiveLiveTime, bgTime = rd.BackgroundEnergySpectrum.EffectiveLiveTime;
            BecquerelCoefficient.LineResult k = BecquerelCoefficient.ForLine(e, y, rd.Efficiency);
            BecquerelCoefficient.SummingResult sr = BecquerelCoefficient.Summing(p.Nuclide, e, y,
                cal.ChannelToEnergy(c0 - 0.5), cal.ChannelToEnergy(c1 + 0.5), rd);
            double plain = ROIAriphmetics.CalculateActivity(k.Value, fgCounts, fgTime, bgCounts, bgTime);
            double overContinuum = peakCounts / fgTime * k.Value * sr.Factor;
            Console.WriteLine("  {0,-15} {1,9} {2,9} {3,7} {4,10} {5,10} {6,10} {7,8} {8,8}{9}",
                p.Spectrum, F(e, 1), k.Ok ? k.Value.ToString("G4", CultureInfo.InvariantCulture) : "-", F(sr.Factor, 4),
                F(passport, 0), F(activity, 0), F(overContinuum, 0), F(activity / passport, 3), F(overContinuum / passport, 3),
                refusal == null ? "" : "  отказ: " + refusal);
            if (!sr.Applied)
            {
                Console.WriteLine("    поправки нет: {0}", sr.Problem);
            }

            // Контроль: Бк панели / без поправки = множитель Summing (момент — среднее за набор).
            if (!(activity > 0.0) || decay != 1.0 || Math.Abs(activity / plain - sr.Factor) > 1e-9)
            {
                Console.WriteLine("    !! панель: Бк {0}, без поправки {1}, отношение {2}, Summing {3}, множитель распада {4}",
                    R(activity), R(plain), R(activity / plain), R(sr.Factor), R(decay));
                bad++;
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

        static bool EnsureMatrix(string corpus, string guid)
        {
            string dst = ResponseMatrixStore.PathOf(guid);
            string src = Path.Combine(corpus, "geometries", "response", guid + ".rmx");
            if (!File.Exists(src)) { Console.WriteLine("ОСНАСТКА: в корпусе нет {0}", src); return false; }
            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            Console.WriteLine("  (матрица корпуса {0}.rmx положена в склад пробы)", guid);
            return true;
        }

        static ResultData Load(string path)
        {
            if (!File.Exists(path)) return null;
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

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
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

        static string R(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
