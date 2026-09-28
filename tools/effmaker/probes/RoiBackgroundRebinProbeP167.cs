using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml.Serialization;

namespace RoiBackgroundRebinProbeP167
{
    /// <summary>
    /// (`AMBER109`, П167 28.09.2026) ФОН В ЧУЖОЙ КАЛИБРОВКЕ: СОХРАНЯЕТСЯ ЛИ
    /// ЧИСЛО ОТСЧЁТОВ ПРИ ПЕРЕНОСЕ В ШКАЛУ СПЕКТРА.
    ///
    /// Спектр и его фон с РАЗНЫМИ калибровками (по умолчанию пара корпуса
    /// `RC103_Th232WT20`: спектр poly2, фон poly4). Эталон считает сама проба,
    /// независимо от приложения: каналы обеих шкал — отрезки по энергии с
    /// границами посередине между центрами (как `FsaAnalyzer.Rebin`), отсчёты
    /// канала фона раскладываются по перекрытию. Против эталона печатается,
    /// что ДАЁТ ПРИЛОЖЕНИЕ по каждому своему пути, в отсчётах ФОНА в окне:
    ///
    ///   1. зона ROI — `MeasurementResultManager.Calculate` (фон = (брутто −
    ///      нетто) / k, k = живое спектра / живое фона);
    ///   2. общее вычитание — `SpectrumAriphmetics.Substract` (Σ(спектр −
    ///      вычтенный) / k; каналы, обрезанные нулём, считаются и названы);
    ///   3. панель выделения — `EnergySpectrumView.EnsureSelectionAnalytics`
    ///      (вид без окна, поля отражением, как `LiveTimePathsProbeP80`) с тем
    ///      фоном, который виду даёт `PrepareViewData`: если в приложении есть
    ///      `SpectrumAriphmetics.BackgroundInScaleOf` — его выход, иначе фон
    ///      как есть.
    ///
    /// Контроли: (К1) фон с калибровкой, РАВНОЙ калибровке спектра, — все пути
    /// обязаны дать прямую сумму каналов побитово (ветка совпадающих шкал не
    /// должна шевельнуться); (К2) эталон при равных шкалах — тождество; (К3)
    /// эталон сохраняет интеграл фона на перекрытии шкал.
    ///
    ///   RoiBackgroundRebinProbeP167.exe [--spectrum=&lt;xml&gt;] [--tol=&lt;доля&gt;]
    ///
    /// Код возврата: 0 — все пути в пределах допуска от эталона (по умолчанию
    /// 0.5 %) и контроли прошли; 1 — нет; 2 — ключи/файл.
    /// </summary>
    static class Program
    {
        static int bad;
        static double tol = 0.005;

        static readonly double[][] Windows =
        {
            new[] { 40.0, 80.0 },
            new[] { 230.0, 250.0 },
            new[] { 600.0, 720.0 },
            new[] { 1380.0, 1540.0 },
            new[] { 2500.0, 2700.0 },
        };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string path = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }
            if (path == null)
            {
                string root = FindRoot();
                if (root == null) { Console.Error.WriteLine("корень дерева не найден — дайте --spectrum="); return 2; }
                path = Path.Combine(root, "tools", "CORPUS", "corpus", "spectra", "RC103_Th232WT20.xml");
            }
            if (!File.Exists(path)) { Console.Error.WriteLine("нет файла {0}", path); return 2; }

            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();

            ResultData rd = Load(path);
            EnergySpectrum fg = rd.EnergySpectrum;
            EnergySpectrum bg = rd.BackgroundEnergySpectrum;
            if (bg == null) { Console.Error.WriteLine("в файле нет фона"); return 2; }

            MethodInfo helper = typeof(SpectrumAriphmetics).GetMethod("BackgroundInScaleOf", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("=== чем мерено ===");
            Console.WriteLine("сборка приложения: {0}", typeof(SpectrumAriphmetics).Assembly.Location);
            Console.WriteLine("SpectrumAriphmetics.BackgroundInScaleOf: {0}", helper != null ? "есть" : "НЕТ (вид получает фон как есть)");
            Console.WriteLine("спектр: {0}", path);
            Console.WriteLine("каналов: спектр {0}, фон {1}; живое: спектр {2} с, фон {3} с; k = {4}",
                fg.NumberOfChannels, bg.NumberOfChannels, N(fg.EffectiveLiveTime), N(bg.EffectiveLiveTime),
                N(fg.EffectiveLiveTime / bg.EffectiveLiveTime));
            Console.WriteLine("калибровки равны: {0}", fg.EnergyCalibration.Equals(bg.EnergyCalibration));
            Console.WriteLine();

            Console.WriteLine("=== ширина канала h_спектр / h_фон ===");
            foreach (double e in new[] { 60.0, 238.6, 662.0, 1460.8, 2614.5 })
            {
                double hf = Width(fg.EnergyCalibration, fg.NumberOfChannels, e);
                double hb = Width(bg.EnergyCalibration, bg.NumberOfChannels, e);
                Console.WriteLine("  {0,7} кэВ: h_спектр {1} кэВ, h_фон {2} кэВ, отношение {3}", N(e), F(hf, 4), F(hb, 4), F(hf / hb, 4));
            }
            Console.WriteLine();

            // (К2) эталон при равных шкалах — тождество.
            double[] same = Rebin(bg.Spectrum, fg.EnergyCalibration, fg.EnergyCalibration, fg.NumberOfChannels);
            double maxSame = 0.0;
            for (int i = 0; i < same.Length; i++) maxSame = Math.Max(maxSame, Math.Abs(same[i] - bg.Spectrum[i]));
            Console.WriteLine("К2 эталон при равных шкалах: max |перекладка − исходник| = {0} отсч. {1}", R(maxSame), maxSame < 1e-6 ? "ok" : "!!");
            if (!(maxSame < 1e-6)) bad++;

            double[] truth = Rebin(bg.Spectrum, bg.EnergyCalibration, fg.EnergyCalibration, fg.NumberOfChannels);
            // (К3) интеграл на перекрытии шкал.
            double[] fgEdges = Edges(fg.EnergyCalibration, fg.NumberOfChannels);
            double[] bgEdges = Edges(bg.EnergyCalibration, bg.NumberOfChannels);
            double lo = Math.Max(fgEdges[0], bgEdges[0]), hi = Math.Min(fgEdges[fg.NumberOfChannels], bgEdges[bg.NumberOfChannels]);
            double srcIn = 0.0;
            for (int j = 0; j < bg.NumberOfChannels; j++)
            {
                double a = bgEdges[j], b = bgEdges[j + 1];
                double ov = Math.Min(b, hi) - Math.Max(a, lo);
                if (ov > 0) srcIn += bg.Spectrum[j] * ov / (b - a);
            }
            double sumTruth = 0.0;
            foreach (double v in truth) sumTruth += v;
            Console.WriteLine("К3 эталон: интеграл фона на перекрытии [{0}; {1}] кэВ {2}, после перекладки {3} ({4})",
                F(lo, 1), F(hi, 1), F(srcIn, 1), F(sumTruth, 1), Math.Abs(sumTruth - srcIn) < 1e-6 * srcIn ? "ok" : "!!");
            if (!(Math.Abs(sumTruth - srcIn) < 1e-6 * srcIn)) bad++;
            Console.WriteLine();

            Console.WriteLine("=== фон в окне, отсчёты фона (до нормировки): эталон и пути приложения ===");
            RunPaths(rd, truth, helper, "разные калибровки", false);
            Console.WriteLine();

            // (К1) фон с калибровкой спектра: пути обязаны дать прямую сумму побитово.
            ResultData rdSame = rd.Clone();
            rdSame.BackgroundEnergySpectrum = bg.Clone();
            rdSame.BackgroundEnergySpectrum.EnergyCalibration = fg.EnergyCalibration.Clone();
            double[] direct = new double[bg.NumberOfChannels];
            for (int i = 0; i < direct.Length; i++) direct[i] = bg.Spectrum[i];
            Console.WriteLine("=== К1: фон в калибровке спектра — ветка совпадающих шкал, ждём прямую сумму побитово ===");
            RunPaths(rdSame, direct, helper, "равные калибровки", true);
            Console.WriteLine();

            Console.WriteLine(bad == 0 ? "ИТОГ: ok — все пути в допуске {0} % от эталона, контроли прошли"
                                       : "ИТОГ: !! {1} отказ(ов) (допуск {0} %)", F(tol * 100.0, 2), bad);
            return bad == 0 ? 0 : 1;
        }

        static void RunPaths(ResultData rd, double[] truth, MethodInfo helper, string label, bool exact)
        {
            EnergySpectrum fg = rd.EnergySpectrum;
            EnergySpectrum bg = rd.BackgroundEnergySpectrum;
            double k = fg.EffectiveLiveTime / bg.EffectiveLiveTime;

            var sa = new SpectrumAriphmetics(fg);
            EnergySpectrum sub = sa.Substract(bg);

            EnergySpectrum viewBg = bg;
            if (helper != null)
            {
                viewBg = (EnergySpectrum)helper.Invoke(null, new object[] { bg, fg });
            }

            Console.WriteLine("  {0,-12} {1,12} {2,12} {3,9} {4,12} {5,9} {6,12} {7,9}",
                "окно, кэВ", "эталон", "зона ROI", "откл.%", "Substract", "откл.%", "выделение", "откл.%");
            foreach (double[] w in Windows)
            {
                int c0 = (int)Math.Ceiling(PolynomialEnergyCalibration.ChannelOf(fg.EnergyCalibration, w[0], fg.NumberOfChannels));
                int c1 = (int)Math.Floor(PolynomialEnergyCalibration.ChannelOf(fg.EnergyCalibration, w[1], fg.NumberOfChannels));
                double t = 0.0, fgSum = 0.0, subSum = 0.0;
                int clipped = 0;
                for (int i = Math.Max(0, c0); i <= c1 && i < fg.NumberOfChannels; i++)
                {
                    t += truth[i];
                    fgSum += fg.Spectrum[i];
                    subSum += sub.Spectrum[i];
                    if (sub.Spectrum[i] == 0) clipped++;
                }

                double roiBg = (fgSum - Zone(rd, w[0], w[1])) / k;
                double subBg = (fgSum - subSum) / k;
                double selBg = Selection(rd, viewBg, c0, c1);

                string tag = string.Format(CultureInfo.InvariantCulture, "{0}–{1}", N(w[0]), N(w[1]));
                Console.WriteLine("  {0,-12} {1,12} {2,12} {3,9} {4,12} {5,9} {6,12} {7,9}{8}",
                    tag, F(t, 1), F(roiBg, 1), Dev(roiBg, t), F(subBg, 1), Dev(subBg, t), F(selBg, 1), Dev(selBg, t),
                    clipped > 0 ? string.Format(CultureInfo.InvariantCulture, "   (Substract: {0} кан. обрезано нулём)", clipped) : "");

                // Зона и выделение: допуск от эталона; Substract округляет каждый канал
                // до целого (±0.5 отсч. × k⁻¹ на канал) — допуск тот же, плюс это.
                double subTol = tol * Math.Abs(t) + 0.5 * (c1 - c0 + 1) / k;
                if (exact)
                {
                    Expect(label + " зона " + tag, t, roiBg, 1e-9 * Math.Max(1.0, t));
                    Expect(label + " выделение " + tag, t, selBg, 0.0);
                    if (clipped == 0) Expect(label + " Substract " + tag, t, subBg, subTol);
                }
                else
                {
                    Expect(label + " зона " + tag, t, roiBg, tol * Math.Abs(t));
                    // выделение идёт по целочисленному фону вида (перекладка с
                    // накопленным округлением): ±1 отсчёт на окно сверх допуска.
                    Expect(label + " выделение " + tag, t, selBg, tol * Math.Abs(t) + 1.0);
                    if (clipped == 0) Expect(label + " Substract " + tag, t, subBg, subTol);
                }
            }
        }

        static void Expect(string what, double expected, double got, double allowed)
        {
            if (Math.Abs(got - expected) <= allowed) return;
            Console.WriteLine("    !! {0}: эталон {1}, приложение {2} (допуск ±{3})", what, F(expected, 2), F(got, 2), F(allowed, 2));
            bad++;
        }

        /// <summary>Нетто зоны простой разности — самим приложением.</summary>
        static double Zone(ResultData source, double lo, double hi)
        {
            var prim = new ROISimpleDifferenceData
            {
                LowerLimit = lo,
                UpperLimit = hi,
                Coefficient = 1.0,
                CoefficientError = 0.0,
                OperationType = "Addition",
                Operation = ROIPrimitiveOperation.OperationsMap["Addition"],
            };
            var zone = new ROIDefinitionData
            {
                Name = "окно",
                Enabled = true,
                PeakEnergy = 0.5 * (lo + hi),
                LowerLimit = lo,
                UpperLimit = hi,
                Intencity = 100.0,
                BecquerelCoefficient = 1.0,
                AutoBecquerelCoefficient = false,
            };
            zone.ROIPrimitives.Add(prim);
            var roi = new ROIConfigData();
            roi.ROIDefinitions.Add(zone);
            ResultData rd = new ResultData
            {
                EnergySpectrum = source.EnergySpectrum,
                BackgroundEnergySpectrum = source.BackgroundEnergySpectrum,
                ROIConfig = roi,
            };
            var manager = new MeasurementResultManager();
            MeasurementResultCollection counts = manager.Calculate(rd);
            return counts.ResultList[0].ResultValue;
        }

        static readonly Type TView = typeof(EnergySpectrumView);
        static readonly Type TAn = TView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
        static readonly MethodInfo MEnsure = TView.GetMethod("EnsureSelectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Отсчёты фона выделения (`BgCounts`) — настоящей веткой вида.</summary>
        static double Selection(ResultData rd, EnergySpectrum viewBg, int c0, int c1)
        {
            object view = FormatterServices.GetUninitializedObject(TView);
            EnergySpectrum fg = rd.EnergySpectrum;
            Set(view, "energySpectrum", fg);
            Set(view, "backgroundEnergySpectrum", viewBg);
            Set(view, "substractedEnergySpectrum", null);
            Set(view, "normByEffEnergySpectrum", null);
            Set(view, "energyCalibration", fg.EnergyCalibration);
            Set(view, "baseEnergyCalibration", fg.EnergyCalibration);
            Set(view, "backgroundEnergyCalibration", viewBg.EnergyCalibration);
            Set(view, "backgroundNumberOfChannels", viewBg.NumberOfChannels);
            Set(view, "selectionStart", c0);
            Set(view, "selectionEnd", c1);
            Set(view, "peakMode", PeakMode.Visible);
            Set(view, "backgroundMode", BackgroundMode.Invisible);
            var result = new ResultData
            {
                EnergySpectrum = fg,
                BackgroundEnergySpectrum = rd.BackgroundEnergySpectrum,
                Visible = true,
            };
            Set(view, "activeResultData", result);
            Set(view, "globalConfigManager", Config());
            Set(view, "nuclideManager", NuclideDefinitionManager.GetInstance());
            Set(view, "selectionAnalyticsDirty", true);
            Set(view, "selectionAnalytics", null);
            Set(view, "selectionFWHM", 0.0);
            MEnsure.Invoke(view, null);
            object an = TView.GetField("selectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            if (an == null) throw new InvalidOperationException("аналитика выделения не построена");
            return (double)TAn.GetProperty("BgCounts").GetValue(an, null);
        }

        static GlobalConfigManager Config()
        {
            var m = new GlobalConfigManager();
            var c = new GlobalConfigInfo();
            if (c.ColorConfig != null && (c.ColorConfig.SpectrumColorList == null || c.ColorConfig.SpectrumColorList.Count == 0))
            {
                c.ColorConfig.InitializeSpectrumColor();
            }
            m.GlobalConfig = c;
            return m;
        }

        static void Set(object target, string field, object value)
        {
            FieldInfo f = TView.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + field);
            f.SetValue(target, value);
        }

        // ---------------- эталон ----------------

        static double[] Edges(EnergyCalibration cal, int n)
        {
            double[] c = new double[n];
            for (int i = 0; i < n; i++) c[i] = cal.ChannelToEnergy(i);
            double[] e = new double[n + 1];
            for (int i = 1; i < n; i++) e[i] = 0.5 * (c[i - 1] + c[i]);
            e[0] = c[0] - 0.5 * (c[1] - c[0]);
            e[n] = c[n - 1] + 0.5 * (c[n - 1] - c[n - 2]);
            return e;
        }

        static double[] Rebin(int[] src, EnergyCalibration from, EnergyCalibration to, int n)
        {
            double[] dst = new double[n];
            double[] se = Edges(from, src.Length);
            double[] de = Edges(to, n);
            for (int j = 0; j < src.Length; j++)
            {
                if (src[j] == 0) continue;
                double a = se[j], b = se[j + 1];
                if (!(b > a)) continue;
                for (int i = 0; i < n; i++)
                {
                    double ov = Math.Min(b, de[i + 1]) - Math.Max(a, de[i]);
                    if (ov > 0) dst[i] += src[j] * ov / (b - a);
                }
            }
            return dst;
        }

        static double Width(EnergyCalibration cal, int n, double e)
        {
            double ch = PolynomialEnergyCalibration.ChannelOf(cal, e, n);
            return cal.ChannelToEnergy(ch + 0.5) - cal.ChannelToEnergy(ch - 0.5);
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
            foreach (EnergySpectrum s in new[] { rd.EnergySpectrum, rd.BackgroundEnergySpectrum })
            {
                if (s == null) continue;
                var pcal = s.EnergyCalibration as PolynomialEnergyCalibration;
                if (pcal != null) pcal.CheckCalibration(s.NumberOfChannels);
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

        static string Dev(double got, double truth)
        {
            if (truth == 0.0) return "-";
            return ((got - truth) / truth * 100.0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
        }

        static string F(double v, int d) { return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); }
        static string N(double v) { return v.ToString("G6", CultureInfo.InvariantCulture); }
        static string R(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
    }
}
