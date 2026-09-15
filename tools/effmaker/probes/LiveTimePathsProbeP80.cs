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

namespace LiveTimePathsProbeP80
{
    /// <summary>
    /// (`AMBER35`, П80 15.09.2026) ОДНА ЛИНИЯ НА ОДНОМ ЭКРАНЕ — ОДНА СКОРОСТЬ
    /// СЧЁТА. Решение Amber 15.09.2026, дословно: «Согласовать все связанные
    /// пути по живому времени (рекомендую); проверить также поиск пиков и
    /// экспорт с вычитанием фона.»
    ///
    /// Правило разбора FSA — живое время, если задано (&gt; 0), иначе полное;
    /// фон — отношением живых. Проба поднимает синтетическую сцену со спектром
    /// `LiveTime = 0.9·MeasurementTime` и фоном `LiveTime = 0.8·MeasurementTime`
    /// и по КАЖДОМУ пути приложения печатает скорость и ЗНАМЕНАТЕЛЬ, который
    /// из неё следует (число / скорость), — всё САМИМ приложением, не копией
    /// формулы:
    ///
    ///   1. панель выделения — `EnergySpectrumView.EnsureSelectionAnalytics`
    ///      (вид без окна, поля отражением, как `BqActivityProbe`): NetCps,
    ///      нормировка фона (AdjBgCounts/BgCounts), активность через K;
    ///   2. зоны — `MeasurementResultManager.Calculate` + `Translate`:
    ///      имп/с, Бк, нормировка фона;
    ///   3. мощность дозы — `DoseRateManager.Calculate`: Cps диапазона / Counts;
    ///   4. общее вычитание фона — `SpectrumAriphmetics.Substract`: нормировка
    ///      по плоскому каналу; отсюда же берёт вычтенный спектр поиск пиков
    ///      (`PeakDetector.DetectPeak`, режим «фон вычтен») и вывоз CSV;
    ///   5. разбор FSA — `FsaAnalyzer.Analyze`: `FsaResult.LiveTime` и
    ///      масштаб фона `Background[i]/bg[i]`;
    ///   6. график в имп/с — `EnergySpectrumView.ScaleFsaValue` (масштаб слоёв
    ///      FSA, приватный метод отражением) и ПО КОДУ: в исходниках
    ///      `EnergySpectrumView.cs` / `.Fsa.cs` не должно остаться ни одного
    ///      `.MeasurementTime` (все ~80 мест переведены на `.EffectiveLiveTime`);
    ///   7. поиск пиков — на корпусном спектре с фоном (`--spectra=`):
    ///      `DetectPeak(Substract)` обязан дать ТЕ ЖЕ пики, что `DetectPeak`
    ///      на спектре, вычтенном `Substract` вручную, — цепочка одна.
    ///
    /// Все знаменатели сравниваются с ожиданием правила с допуском 1e-9
    /// относительным. Вторая сцена — БЕЗ живого (`LiveTime = 0`): каждый
    /// знаменатель обязан РАВНЯТЬСЯ полному времени (`==`), и числа печатаются
    /// форматом «R» — их diff между старой и новой сборкой обязан быть пуст.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: на сборке ДО правки пути 1–4 и 6 краснеют
    /// (знаменатель 1000 вместо 900 — скорости занижены на 10 %, нормировка
    /// фона 0.5 вместо 0.5625), путь 5 (FSA) зелёный — он и был по живому.
    ///
    ///     LiveTimePathsProbeP80 [--spectra=&lt;…\CORPUS\corpus\spectra&gt;] [--source=&lt;корень дерева&gt;]
    ///
    /// Без `--spectra` раздел 7 пропускается и об этом говорится вслух.
    /// Корень дерева для проверки по коду ищется вверх от каталога пробы;
    /// `--source=` задаёт его явно.
    /// </summary>
    static class Program
    {
        const double Tol = 1e-9;
        static int bad;

        // Сцена: 4096 каналов по 1 кэВ, пик 662 кэВ σ = 12 кан. на ровном 200,
        // фон ровный 320. Времена — по строке реестра: 0.9 и 0.8 полного.
        const int Channels = 4096;
        const double FgFull = 1000.0, FgLive = 900.0;
        const double BgFull = 2000.0, BgLive = 1600.0;
        const int FgFlat = 200, BgFlat = 320;
        const double PeakKev = 662.0, PeakSigma = 12.0, PeakAmp = 40000.0;
        const int SelLo = 622, SelHi = 702;
        const double LineKev = 661.657, LineYield = 85.1;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            // (`T243`) Снимок поставочной полосы — ДО разбора ключей: проба
            // считает FSA и обязана отчитаться о своих настройках.
            FsaTuningReport.Snapshot();

            string spectraDir = null;
            string sourceRoot = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--source=", StringComparison.Ordinal)) sourceRoot = a.Substring(9);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            // ⛔ Обе карты примитивов ROI и ДО менеджеров (`T60`): у
            // `MeasurementResultManager` операции берутся из карты в
            // инициализаторе поля, а отказ менеджера без окон — исключение.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();

            Console.WriteLine("=== чем мерено ===");
            Console.WriteLine("сборка приложения: {0}", typeof(EnergySpectrumView).Assembly.Location);
            Console.WriteLine("сцена: спектр {0} с полного / {1} с живого, фон {2} с полного / {3} с живого; выделение [{4}..{5}] кэВ",
                              N(FgFull), N(FgLive), N(BgFull), N(BgLive), SelLo, SelHi);
            Console.WriteLine("ожидание по правилу разбора: знаменатель спектра {0} с, фона {1} с, нормировка фона {2}",
                              N(FgLive), N(BgLive), N(FgLive / BgLive));
            Console.WriteLine();

            Scene live = Scene.Build(FgLive, BgLive);
            Console.WriteLine("=== А. сцена С ЖИВЫМ временем: каждый путь — скорость и её знаменатель ===");
            RunPaths(live, FgLive, BgLive, "живое");
            Console.WriteLine();

            Scene none = Scene.Build(0.0, 0.0);
            Console.WriteLine("=== Б. сцена БЕЗ живого времени (LiveTime = 0): знаменатель == полное, числа «R» ===");
            RunPaths(none, FgFull, BgFull, "полное");
            Console.WriteLine();

            Section6_Source(sourceRoot);
            Console.WriteLine();

            Section7_PeakSearch(spectraDir);
            Console.WriteLine();

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format(CultureInfo.InvariantCulture, "НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  Пути 1–6 на одной сцене
        // ------------------------------------------------------------------

        static void RunPaths(Scene sc, double fgExpect, double bgExpect, string kind)
        {
            double ratioExpect = fgExpect / bgExpect;
            // Сцена Б: ПРЯМЫЕ значения (LiveTime результата FSA, времена коллекции)
            // сравниваются «==»; знаменатели, ВЫВЕДЕННЫЕ делением (n / (n/T)),
            // — допуском: побитовость сцены Б доказывает diff строк «R» между
            // старой и новой сборкой, а не равенство после двух делений.
            bool exact = sc.Fg.LiveTime == 0.0;

            // 1. Панель выделения.
            Selection sel = sc.Selection();
            double selFgT = sel.NetCounts / sel.NetCps;
            double selRatio = sel.AdjBgCounts / sel.BgCounts;
            Console.WriteLine("1. выделение: нетто {0} отсч., {1} имп/с → знаменатель {2} с; фон {3} → {4} скорр., нормировка {5}",
                              R(sel.NetCounts), R(sel.NetCps), R(selFgT), R(sel.BgCounts), R(sel.AdjBgCounts), R(selRatio));
            Check("выделение: знаменатель NetCps", fgExpect, selFgT, false);
            Check("выделение: нормировка фона", ratioExpect, selRatio, false);
            if (sel.Activity > 0.0)
            {
                double selFgTbyK = sel.NetCounts * sel.K / sel.Activity;
                Console.WriteLine("   активность {0} Бк при K = {1} → знаменатель {2} с ({3})",
                                  R(sel.Activity), R(sel.K), R(selFgTbyK), sel.Label);
                Check("выделение: знаменатель активности (Бк)", fgExpect, selFgTbyK, false);
            }
            else
            {
                Console.WriteLine("   ⛔ активность выделения не посчитана: {0}", sel.Refusal ?? "(без причины)");
                bad++;
            }

            // 2. Зоны.
            Zone z = sc.Zone();
            double zoneFgT = z.NetCounts / z.Cps;
            double zoneRatio = (z.FgRegion - z.NetCounts) / z.BgRegion;
            Console.WriteLine("2. зона: нетто {0} отсч., {1} имп/с → знаменатель {2} с; нормировка фона {3}; {4} Бк → знаменатель {5} с; МДА {6} отсч.",
                              R(z.NetCounts), R(z.Cps), R(zoneFgT), R(zoneRatio), R(z.Bq), R(z.NetCounts * z.K / z.Bq), R(z.Mda));
            Check("зона: знаменатель имп/с", fgExpect, zoneFgT, false);
            Check("зона: нормировка фона", ratioExpect, zoneRatio, false);
            Check("зона: знаменатель Бк", fgExpect, z.NetCounts * z.K / z.Bq, false);
            Console.WriteLine("   коллекция: MeasurementTime = {0} с (подпись), LiveTime = {1} с, CountingTime = {2} с",
                              R(z.CollectionMeasurementTime), R(z.CollectionLiveTime), R(z.CollectionCountingTime));
            Check("зона: подпись коллекции — ПОЛНОЕ время", FgFull, z.CollectionMeasurementTime, true);
            Check("зона: знаменатель коллекции (CountingTime)", fgExpect, z.CollectionCountingTime, exact);

            // 3. Доза.
            Dose d = sc.Dose();
            Console.WriteLine("3. доза: {0} мкЗв/ч; диапазон {1}–{2} кэВ: {3} отсч., {4} имп/с → знаменатель {5} с{6}",
                              R(d.Rate), N(d.LowKev), N(d.HighKev), R(d.Counts), R(d.Cps), R(d.Counts / d.Cps),
                              d.Refusal != null ? "; ОТКАЗ: " + d.Refusal : "");
            Check("доза: знаменатель Cps диапазона", fgExpect, d.Counts / d.Cps, false);

            // 4. Общее вычитание фона.
            double subNorm = sc.SubtractNorm(out int subAt, out int subValue);
            Console.WriteLine("4. вычитание фона (Substract): канал {0}: {1} − k·{2} = {3} → k = {4}",
                              subAt, FgFlat, BgFlat, subValue, R(subNorm));
            Check("Substract: нормировка фона", ratioExpect, subNorm, false);

            // 5. FSA.
            Fsa f = sc.Fsa();
            Console.WriteLine("5. FSA: FsaResult.LiveTime = {0} с; масштаб фона Background[i]/bg[i] = {1}{2}; компонентов {3}",
                              R(f.LiveTime), f.BackgroundScale.HasValue ? R(f.BackgroundScale.Value) : "(нет — фон в результате не в отсчётах)",
                              f.BackgroundUsed ? "" : " (фон НЕ ВЗЯТ: " + (f.BackgroundRejected ?? "?") + ")",
                              f.Components);
            Check("FSA: знаменатель (LiveTime результата)", fgExpect, f.LiveTime, exact);
            if (f.BackgroundScale.HasValue)
            {
                Check("FSA: масштаб фона", ratioExpect, f.BackgroundScale.Value, false);
            }

            // 6. График в имп/с: масштаб слоёв FSA (метод вида, отражением).
            double chartT = sc.ChartDenominator();
            Console.WriteLine("6. график (ScaleFsaValue, имп/с): знаменатель {0} с", R(chartT));
            Check("график: знаменатель имп/с", fgExpect, chartT, false);

            // Итог сцены: ОДИН знаменатель у всех путей.
            double[] all = { selFgT, zoneFgT, d.Counts / d.Cps, f.LiveTime, chartT };
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double v in all) { if (v < lo) lo = v; if (v > hi) hi = v; }
            Console.WriteLine("   → знаменатели путей 1,2,3,5,6: от {0} до {1} с ({2}); нормировки 1,2,4{3}: {4}, {5}, {6}{7}",
                              R(lo), R(hi), kind, f.BackgroundScale.HasValue ? ",5" : "",
                              R(selRatio), R(zoneRatio), R(subNorm),
                              f.BackgroundScale.HasValue ? ", " + R(f.BackgroundScale.Value) : "");
        }

        // ------------------------------------------------------------------
        //  6. По коду: в исходниках вида не осталось `.MeasurementTime`
        // ------------------------------------------------------------------

        static void Section6_Source(string sourceRoot)
        {
            Console.WriteLine("=== 6б. по коду: `.MeasurementTime` в исходниках вида ===");
            string root = sourceRoot ?? FindRepoRoot();
            if (root == null)
            {
                Console.WriteLine("  ⚠ корень дерева не найден вверх от {0} — проверка по коду пропущена (дай --source=)",
                                  AppDomain.CurrentDomain.BaseDirectory);
                bad++;
                return;
            }

            foreach (string rel in new[] { "BecquerelMonitor\\EnergySpectrumView.cs", "BecquerelMonitor\\EnergySpectrumView.Fsa.cs" })
            {
                string path = Path.Combine(root, rel);
                if (!File.Exists(path))
                {
                    Console.WriteLine("  ⚠ нет файла {0}", path);
                    bad++;
                    continue;
                }

                string text = File.ReadAllText(path);
                int left = Count(text, ".MeasurementTime");
                int moved = Count(text, ".EffectiveLiveTime");
                Console.WriteLine("  {0}: `.MeasurementTime` осталось {1}, `.EffectiveLiveTime` {2}", rel, left, moved);
                if (left != 0)
                {
                    Console.WriteLine("  !! в виде ещё делят на полное время: {0} мест", left);
                    bad++;
                }
                if (moved == 0)
                {
                    Console.WriteLine("  !! в виде нет ни одного `.EffectiveLiveTime` — правка не доехала");
                    bad++;
                }
            }
        }

        static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "BecquerelMonitor", "EnergySpectrumView.cs")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            return null;
        }

        static int Count(string text, string needle)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        // ------------------------------------------------------------------
        //  7. Поиск пиков: цепочка идёт через Substract
        // ------------------------------------------------------------------

        static void Section7_PeakSearch(string spectraDir)
        {
            Console.WriteLine("=== 7. поиск пиков: DetectPeak(фон вычтен) ≡ DetectPeak(Substract вручную) ===");
            if (spectraDir == null)
            {
                Console.WriteLine("  --spectra= не дан — раздел пропущен (цепочка PeakDetector.DetectPeak → SpectrumAriphmetics.Substract не измерена)");
                return;
            }

            string file = Path.Combine(spectraDir, "ASN16_Cs137_10cm.xml");
            if (!File.Exists(file))
            {
                Console.WriteLine("  нет {0} — раздел пропущен", file);
                bad++;
                return;
            }

            ResultData rd;
            try
            {
                rd = LoadResult(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ⛔ спектр не прочитан: {0}", ex.Message);
                bad++;
                return;
            }

            if (rd.BackgroundEnergySpectrum == null || rd.BackgroundEnergySpectrum.Spectrum == null)
            {
                Console.WriteLine("  ⛔ у {0} нет фона — раздел не измерен", Path.GetFileName(file));
                bad++;
                return;
            }

            EnergySpectrum fg = rd.EnergySpectrum;
            EnergySpectrum bg = rd.BackgroundEnergySpectrum;
            fg.LiveTime = 0.9 * fg.MeasurementTime;
            bg.LiveTime = 0.8 * bg.MeasurementTime;
            Console.WriteLine("  {0}: спектр {1} с полного / {2} с живого; фон {3} с / {4} с; каналов {5}",
                              Path.GetFileName(file), N(fg.MeasurementTime), N(fg.LiveTime),
                              N(bg.MeasurementTime), N(bg.LiveTime), fg.NumberOfChannels);

            var empty = new List<NuclideDefinition>();   // без библиотеки: подписи не нужны
            List<Peak> viaDetector = new PeakDetector().DetectPeak(rd, BackgroundMode.Substract, SmoothingMethod.None, null, empty);

            var saManual = new SpectrumAriphmetics(fg);
            EnergySpectrum subtracted = saManual.Substract(bg);
            saManual.Dispose();
            ResultData rd2 = rd.Clone();
            rd2.EnergySpectrum = subtracted;
            rd2.BackgroundEnergySpectrum = null;
            List<Peak> viaManual = new PeakDetector().DetectPeak(rd2, BackgroundMode.Invisible, SmoothingMethod.None, null, empty);

            Console.WriteLine("  пиков: через DetectPeak(Substract) {0}, через Substract вручную {1}", viaDetector.Count, viaManual.Count);
            bool same = viaDetector.Count == viaManual.Count;
            for (int i = 0; same && i < viaDetector.Count; i++)
            {
                Peak a = viaDetector[i], b = viaManual[i];
                same = a.Channel == b.Channel && a.Count == b.Count && a.Energy == b.Energy && a.FWHM == b.FWHM;
                if (!same)
                {
                    Console.WriteLine("  !! пик {0}: канал {1}/{2}, счёт {3}/{4}, энергия {5}/{6}",
                                      i, a.Channel, b.Channel, R(a.Count), R(b.Count), R(a.Energy), R(b.Energy));
                }
            }
            Console.WriteLine(same ? "  ok   цепочка одна: DetectPeak(фон вычтен) = DetectPeak(Substract) побитово по каналу, счёту, энергии, ПШПВ"
                                   : "  !! цепочка расходится — поиск пиков вычитает фон НЕ через Substract");
            if (!same) bad++;

            // Ради читателя: та же цепочка БЕЗ живого времени — сколько пиков и
            // отсчётов, чтобы видеть, что нормировка вообще что-то меняет.
            fg.LiveTime = 0.0;
            bg.LiveTime = 0.0;
            List<Peak> noLive = new PeakDetector().DetectPeak(rd, BackgroundMode.Substract, SmoothingMethod.None, null, empty);
            Console.WriteLine("  для сравнения: нормировка фона с живым {0}, без живого {1}",
                              R(0.9 * fg.MeasurementTime / (0.8 * bg.MeasurementTime)), R(fg.MeasurementTime / bg.MeasurementTime));
            Console.WriteLine("    с живым:    {0} пик(ов): {1}", viaDetector.Count, PeakList(viaDetector));
            Console.WriteLine("    без живого: {0} пик(ов): {1}", noLive.Count, PeakList(noLive));
        }

        static string PeakList(List<Peak> peaks)
        {
            var sb = new StringBuilder();
            foreach (Peak p in peaks)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(p.Energy.ToString("F1", CultureInfo.InvariantCulture)).Append(" кэВ, SNR ")
                  .Append(p.SNR.ToString("F2", CultureInfo.InvariantCulture));
            }
            return sb.Length > 0 ? sb.ToString() : "(нет)";
        }

        static ResultData LoadResult(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            var pcal = s != null ? s.EnergyCalibration as PolynomialEnergyCalibration : null;
            if (pcal != null) pcal.CheckCalibration(s.NumberOfChannels);

            // Настройки поиска — у прибора спектра (`S82`), как у всех проб; нет
            // прибора — умолчания библиотеки, и это названо: здесь меряется
            // тождество двух путей, а не корпусное число.
            string note = ProbeDeviceConfig.Attach(rd);
            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig))
            {
                Console.WriteLine("  ⚠ {0}; поиск идёт умолчаниями FWHMPeakDetectionMethodConfig", note);
                rd.PeakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig();
            }
            if (rd.FwhmCalibration == null)
            {
                var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                rd.FwhmCalibration = cfg.FwhmCalibration ?? FwhmCalibration.DefaultCalibration(cfg, s.EnergyCalibration);
            }
            return rd;
        }

        // ------------------------------------------------------------------
        //  Сцена
        // ------------------------------------------------------------------

        sealed class Selection
        {
            public double NetCounts, NetCps, BgCounts, AdjBgCounts, Activity, K;
            public string Label, Refusal;
        }

        sealed class Zone
        {
            public double NetCounts, Cps, Bq, Mda, K, FgRegion, BgRegion;
            public double CollectionMeasurementTime, CollectionLiveTime, CollectionCountingTime;
        }

        sealed class Dose
        {
            public double Rate, Counts, Cps, LowKev, HighKev;
            public string Refusal;
        }

        sealed class Fsa
        {
            public double LiveTime;
            public double? BackgroundScale;
            public bool BackgroundUsed;
            public string BackgroundRejected;
            public int Components;
        }

        sealed class Scene
        {
            static readonly Type TView = typeof(EnergySpectrumView);
            static readonly Type TAn = TView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            static readonly MethodInfo MEnsure = TView.GetMethod("EnsureSelectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance);
            static readonly MethodInfo MScale = TView.GetMethod("ScaleFsaValue", BindingFlags.NonPublic | BindingFlags.Instance);

            public EnergySpectrum Fg, Bg;
            public PolynomialEnergyCalibration Cal;
            public EfficiencyConfigData Curve;
            public Peak PeakOf;
            public ResultData Result;

            public static Scene Build(double fgLive, double bgLive)
            {
                var sc = new Scene();
                sc.Cal = new PolynomialEnergyCalibration();
                sc.Cal.PolynomialOrder = 1;
                sc.Cal.Coefficients = new double[] { 0.0, 1.0 };

                int[] fgArr = new int[Channels];
                int[] bgArr = new int[Channels];
                for (int i = 0; i < Channels; i++)
                {
                    double gauss = PeakAmp * Math.Exp(-0.5 * Math.Pow((i - PeakKev) / PeakSigma, 2.0));
                    fgArr[i] = FgFlat + (int)Math.Round(gauss);
                    bgArr[i] = BgFlat;
                }
                sc.Fg = Make(fgArr, sc.Cal, FgFull, fgLive);
                sc.Bg = Make(bgArr, sc.Cal, BgFull, bgLive);

                sc.Curve = new EfficiencyConfigData("проба П80");
                sc.Curve.Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 50.0,   Efficiency = 2.0e-2, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 662.0,  Efficiency = 1.0e-3, ErrorPercent = 5.0 },
                    new ROIEfficiencyData { Energy = 2000.0, Efficiency = 1.0e-4, ErrorPercent = 8.0 },
                };

                sc.PeakOf = new Peak
                {
                    Energy = PeakKev,
                    Channel = (int)PeakKev,
                    Count = (int)PeakAmp,
                    FWHM = 2.354820045 * PeakSigma,
                    SNR = 100.0,
                    Nuclide = new NuclideDefinition
                    {
                        Name = "Cs-137", Energy = LineKev, Intencity = LineYield, Visible = true, Sets = new HashSet<Guid>(),
                    },
                };

                sc.Result = new ResultData
                {
                    EnergySpectrum = sc.Fg,
                    BackgroundEnergySpectrum = sc.Bg,
                    Visible = true,
                    Efficiency = sc.Curve,
                };
                sc.Result.SampleInfo.Weight = 1.0;
                sc.Result.SampleInfo.Volume = 1.0;
                sc.Result.DetectedPeaks.Add(sc.PeakOf);
                return sc;
            }

            static EnergySpectrum Make(int[] data, EnergyCalibration cal, double full, double live)
            {
                var s = new EnergySpectrum();
                s.NumberOfChannels = data.Length;
                s.Spectrum = data;
                s.EnergyCalibration = cal;
                s.MeasurementTime = full;
                s.LiveTime = live;
                long total = 0;
                for (int i = 0; i < data.Length; i++) total += data[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
                return s;
            }

            /// <summary>1. Панель выделения — настоящая ветка вида, без окна.</summary>
            public Selection Selection()
            {
                object view = FormatterServices.GetUninitializedObject(TView);
                Set(view, "energySpectrum", this.Fg);
                Set(view, "backgroundEnergySpectrum", this.Bg);
                Set(view, "substractedEnergySpectrum", null);
                Set(view, "normByEffEnergySpectrum", null);
                Set(view, "energyCalibration", this.Cal);
                Set(view, "baseEnergyCalibration", this.Cal);
                Set(view, "backgroundEnergyCalibration", this.Cal);
                Set(view, "backgroundNumberOfChannels", this.Bg.NumberOfChannels);
                Set(view, "selectionStart", SelLo);
                Set(view, "selectionEnd", SelHi);
                Set(view, "peakMode", PeakMode.Visible);
                Set(view, "backgroundMode", BackgroundMode.Invisible);
                Set(view, "activeResultData", this.Result);
                Set(view, "globalConfigManager", Config());
                Set(view, "nuclideManager", NuclideDefinitionManager.GetInstance());
                Set(view, "selectionAnalyticsDirty", true);
                Set(view, "selectionAnalytics", null);
                Set(view, "selectionFWHM", 0.0);
                MEnsure.Invoke(view, null);

                object an = TView.GetField("selectionAnalytics", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                if (an == null) throw new InvalidOperationException("аналитика выделения не построена");
                var s = new Selection
                {
                    NetCounts = (double)Get(an, "NetCounts"),
                    NetCps = (double)Get(an, "NetCps"),
                    BgCounts = (double)Get(an, "BgCounts"),
                    AdjBgCounts = (double)Get(an, "AdjBgCounts"),
                    Activity = (double)Get(an, "Activity"),
                    Label = (string)Get(an, "ActivityLabel"),
                    Refusal = (string)Get(an, "ActivityRefusal"),
                };
                BecquerelCoefficient.LineResult k = BecquerelCoefficient.ForLine(PeakKev, LineYield, this.Curve);
                s.K = k.Ok ? k.Value : double.NaN;
                return s;
            }

            /// <summary>2. Зона [622..702] кэВ простой разностью, K запасённый.</summary>
            public Zone Zone()
            {
                BecquerelCoefficient.LineResult k = BecquerelCoefficient.ForLine(PeakKev, LineYield, this.Curve);
                var prim = new ROISimpleDifferenceData
                {
                    LowerLimit = SelLo,
                    UpperLimit = SelHi,
                    Coefficient = 1.0,
                    CoefficientError = 0.0,
                    OperationType = "Addition",
                    Operation = ROIPrimitiveOperation.OperationsMap["Addition"],
                };
                var zone = new ROIDefinitionData
                {
                    Name = "зона 662",
                    Enabled = true,
                    PeakEnergy = PeakKev,
                    LowerLimit = SelLo,
                    UpperLimit = SelHi,
                    Intencity = LineYield,
                    BecquerelCoefficient = k.Value,
                    BecquerelCoefficientError = k.Error,
                    AutoBecquerelCoefficient = false,
                };
                zone.ROIPrimitives.Add(prim);
                var roi = new ROIConfigData();
                roi.ROIDefinitions.Add(zone);

                var rd = new ResultData
                {
                    EnergySpectrum = this.Fg,
                    BackgroundEnergySpectrum = this.Bg,
                    Efficiency = this.Curve,
                    ROIConfig = roi,
                };
                rd.SampleInfo.Weight = 1.0;

                var manager = new MeasurementResultManager();
                MeasurementResultCollection counts = manager.Calculate(rd);
                MeasurementResultCollection cps = manager.Translate(counts, ResultTranslation.CountsPerSecond);
                MeasurementResultCollection bq = manager.Translate(counts, ResultTranslation.Becquerels);
                if (!counts.ResultList[0].IsValid || !bq.ResultList[0].IsValid)
                {
                    throw new InvalidOperationException("зона не посчитана: " + (bq.ResultList[0].StatusText ?? counts.ResultList[0].StatusText));
                }

                var z = new Zone
                {
                    NetCounts = counts.ResultList[0].ResultValue,
                    Mda = counts.ResultList[0].MDA,
                    Cps = cps.ResultList[0].ResultValue,
                    Bq = bq.ResultList[0].ResultValue,
                    K = k.Value,
                    CollectionMeasurementTime = counts.MeasurementTime,
                };
                // Коллекция: `LiveTime`/`CountingTime` — новые свойства (`AMBER35`);
                // на старой сборке их нет — отражением, чтобы проба собиралась и там.
                z.CollectionLiveTime = Prop(counts, "LiveTime", double.NaN);
                z.CollectionCountingTime = Prop(counts, "CountingTime", counts.MeasurementTime);
                for (int i = SelLo; i <= SelHi; i++)
                {
                    z.FgRegion += this.Fg.Spectrum[i];
                    z.BgRegion += this.Bg.Spectrum[i];
                }
                return z;
            }

            /// <summary>3. Доза по плоской кривой с точечной геометрией, без матрицы.</summary>
            public Dose Dose()
            {
                var curve = new EfficiencyConfigData("плоская П80") { Curve = FlatCurvePoints(), Geometry = PointGeometry(100.0) };
                DoseRateInput input = DoseRateInput.Of(curve, null);
                var rd = new ResultData { EnergySpectrum = this.Fg, BackgroundEnergySpectrum = this.Bg, Efficiency = curve };
                DoseRate dose = new DoseRateManager(Config()).Calculate(rd, input);
                var d = new Dose { Rate = dose.Rate, Refusal = string.IsNullOrEmpty(dose.Refusal) ? null : dose.Refusal };
                if (dose.Ranges != null)
                {
                    foreach (DoseRateRange r in dose.Ranges)
                    {
                        if (r.Counts > 0.0 && r.Cps > 0.0)
                        {
                            d.Counts = r.Counts; d.Cps = r.Cps; d.LowKev = r.LowKev; d.HighKev = r.HighKev;
                            break;
                        }
                    }
                }
                if (!(d.Cps > 0.0)) throw new InvalidOperationException("доза: ни одного диапазона со счётом — " + (d.Refusal ?? "?"));
                return d;
            }

            /// <summary>4. Нормировка фона в Substract — по плоскому каналу 100 (без пика).</summary>
            public double SubtractNorm(out int at, out int value)
            {
                at = 100;
                var sa = new SpectrumAriphmetics(this.Fg);
                EnergySpectrum sub = sa.Substract(this.Bg);
                sa.Dispose();
                value = sub.Spectrum[at];
                // fg − round(k·bg) = value; при k = 0.5625 и bg = 320 произведение
                // целое (180) — округление не мешает; при k = 0.5 — 160.
                return (double)(this.Fg.Spectrum[at] - value) / this.Bg.Spectrum[at];
            }

            /// <summary>5. Разбор FSA с одной линией Cs-137 и фоном.</summary>
            public Fsa Fsa()
            {
                var analyzer = new FsaAnalyzer
                {
                    MinEnergy = 40.0,
                    MaxEnergy = 1800.0,
                    CascadeSumming = false,
                    CascadeSumPeaks = false,
                    Backscatter = false,
                    PileUp = false,
                    AnchorScale = false,
                    RequireGeometry = false,
                };
                var member = new FsaComponent("Cs-137", FsaComponentKind.Single) { DecayChainRoot = "Cs-137", TotalYieldPercent = LineYield };
                member.Lines.Add(new FsaLine("Cs-137", LineKev, LineYield));
                var library = new List<FsaComponent> { member };
                // ПШПВ² = c·ch, c = FWHM²(662)/662 — как у пика сцены.
                double fwhmCh = 2.354820045 * PeakSigma;
                var fwhm = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 0.0, fwhmCh * fwhmCh / PeakKev } };

                FsaTuningReport.Print(analyzer, this.Fg.LiveTime > 0.0 ? "сцена А, с живым" : "сцена Б, без живого");
                FsaResult result = analyzer.Analyze(this.Fg, this.Bg, fwhm, library, null);
                if (result == null) throw new InvalidOperationException("FSA: Analyze вернул null");
                var f = new Fsa
                {
                    LiveTime = result.LiveTime,
                    BackgroundUsed = result.BackgroundUsed,
                    BackgroundRejected = result.BackgroundRejected,
                    Components = result.Components != null ? result.Components.Count : 0,
                };
                if (result.BackgroundUsed && result.Background != null && result.Background.Length > 100 && this.Bg.Spectrum[100] > 0)
                {
                    double scale = result.Background[100] / this.Bg.Spectrum[100];
                    // В режиме SNIP в Background лежит лишь пиковая часть — тогда
                    // отношение не масштаб, и его не показываем как масштаб.
                    if (scale > 0.0) f.BackgroundScale = scale;
                }
                return f;
            }

            /// <summary>6. Масштаб графика в имп/с: приватный ScaleFsaValue вида.</summary>
            public double ChartDenominator()
            {
                if (MScale == null) throw new InvalidOperationException("в сборке нет EnergySpectrumView.ScaleFsaValue");
                object view = FormatterServices.GetUninitializedObject(TView);
                Set(view, "energySpectrum", this.Fg);
                Set(view, "verticalUnit", VerticalUnit.CountsPerSecond);
                double scaled = (double)MScale.Invoke(view, new object[] { 12345.0 });
                return 12345.0 / scaled;
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

            static List<ROIEfficiencyData> FlatCurvePoints()
            {
                var points = new List<ROIEfficiencyData>();
                for (double e = 10.0; e <= 10000.0; e *= 1.2)
                {
                    points.Add(new ROIEfficiencyData { Energy = e, Efficiency = 0.05, ErrorPercent = 1.0 });
                }
                points.Add(new ROIEfficiencyData { Energy = 10000.0, Efficiency = 0.05, ErrorPercent = 1.0 });
                return points;
            }

            static GeometryModel PointGeometry(double distanceMm)
            {
                var g = new GeometryModel();
                g.IsScintillator = true;
                g.Shape = CrystalShape.Cylinder;
                g.CrystalDiameter = 50.0;
                g.CrystalHeight = 50.0;
                g.FrontReflectorThickness = 1.0;
                g.FrontCladdingThickness = 2.0;
                g.SideReflectorThickness = 1.0;
                g.SideCladdingThickness = 2.0;
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = distanceMm;
                return g;
            }

            static void Set(object target, string field, object value)
            {
                FieldInfo f = TView.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + field);
                f.SetValue(target, value);
            }

            static object Get(object an, string prop)
            {
                PropertyInfo p = TAn.GetProperty(prop);
                if (p == null) throw new InvalidOperationException("нет свойства SelectionAnalytics." + prop);
                return p.GetValue(an, null);
            }

            static double Prop(object target, string prop, double fallback)
            {
                PropertyInfo p = target.GetType().GetProperty(prop);
                return p == null ? fallback : (double)p.GetValue(target, null);
            }
        }

        // ------------------------------------------------------------------

        static void Check(string what, double expected, double got, bool exact)
        {
            bool ok = exact ? got == expected
                            : Math.Abs(got - expected) <= Tol * Math.Max(1.0, Math.Abs(expected));
            if (ok) return;
            double rel = expected != 0.0 ? (got - expected) / expected * 100.0 : double.NaN;
            Console.WriteLine("  !! {0}: ждали {1}, получили {2} (расхождение {3} %)", what, R(expected), R(got),
                              double.IsNaN(rel) ? "-" : rel.ToString("F3", CultureInfo.InvariantCulture));
            bad++;
        }

        static string N(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString("G6", CultureInfo.InvariantCulture);
        }

        static string R(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
