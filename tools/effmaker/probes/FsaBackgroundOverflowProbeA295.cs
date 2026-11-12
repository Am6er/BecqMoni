using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER295`, остаток, полоса `a295o`) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ РЫЧАГА `FsaAnalyzer.BackgroundOverflowOut`:
    /// канал переполнения фона в разбор не идёт. Рычаг умолчанием выключен.
    ///
    /// Сцена — спектр корпуса с фоном, у которого переполнения в файле нет (`--spectrum=`); переполнение
    /// насыпается в последний канал фона, шкала фона сжимается множителем (`×0.995` — канал ложится внутрь
    /// полосы фита по разности калибровок; `×1` — калибровки равны).
    ///
    ///   (1) фон как в файле, без переполнения: рычаг не меняет ни бита;
    ///   (2) крайний канал фона на один отсчёт НИЖЕ порога правила `OverflowChannel`: это обычный канал,
    ///       рычаг его не трогает — побитно как без рычага (мутант «последний канал занулять безусловно»);
    ///   (3) переполнение в полосе фита: без рычага горит красная строка «фон больше спектра» у верха шкалы;
    ///       с рычагом строки у верха нет и участка автомаски там нет, а разбор побитно равен разбору того же
    ///       спектра с последним каналом фона, занулённым рукой (мутант «рычаг ничего не делает»);
    ///   (4) крайний канал фона РОВНО на пороге правила: с рычагом — побитно как занулённый рукой, без рычага
    ///       канал вычитается (вычтенный фон у верха шкалы не нулевой);
    ///   (5) переполнение в НУЛЕВОМ канале фона (правило одно на оба конца; живого прибора в корпусе нет,
    ///       `A220`): с рычагом вычтенный фон побитно как у занулённого рукой, счётчик результата равен
    ///       насыпанному; оба конца сразу — счётчик равен сумме (мутант «зануляет только последний канал»).
    ///
    ///   (6) прямой вызов анализатора (сеанс окна снимает с фона копию, и по нему этого не увидеть): отсчёты
    ///       фона ВЫЗЫВАЮЩЕГО до и после разбора с рычагом одни и те же (мутант «зануляет спектр вызывающего»);
    ///   (7) фон БЕЗ калибровки, поканальное вычитание (`RebinBackgroundToSpectrum` выключен), прямой вызов:
    ///       рычаг действует так же — счётчик равен насыпанному, разбор побитно как у занулённого рукой, фон
    ///       вызывающего цел (мутант «без калибровки рычаг молча выключен»).
    ///
    /// Счётчик `FsaResult.BackgroundOverflowDropped` судится в сценах (1)–(7): нуль без рычага и без
    /// переполнения, насыпанное число — с рычагом.
    ///
    /// Коды: 0 — сошлось; 1 — нет; 2 — ключи или файл.
    /// </summary>
    static class FsaBackgroundOverflowProbeA295
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            int overflowCounts = 50000;
            double lineKev = 1460.82;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--counts=", StringComparison.Ordinal)) overflowCounts = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--line=", StringComparison.Ordinal)) lineKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (path == null || !File.Exists(path)) { Console.Error.WriteLine("нужен --spectrum=<файл спектра с фоном>"); return 2; }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData plain = Load(path, nuclides);
            if (plain.BackgroundEnergySpectrum == null) { Console.Error.WriteLine("у спектра нет фона"); return 2; }
            int last = plain.BackgroundEnergySpectrum.Spectrum.Length - 1;
            Same("(0) в фоне сцены переполнения нет", false, OverflowChannel.IsOverflow(plain.BackgroundEnergySpectrum.Spectrum, last));
            Same("(0) в спектре сцены переполнения нет", false, OverflowChannel.IsOverflow(plain.EnergySpectrum.Spectrum, plain.EnergySpectrum.Spectrum.Length - 1));

            // порог правила для крайнего канала этого фона
            int threshold = Threshold(plain.BackgroundEnergySpectrum.Spectrum);
            Console.WriteLine("(0) каналов {0}; порог правила для последнего канала фона — {1} отсч.", last + 1, threshold);

            FsaResult off = Run(plain, false);
            FsaResult on = Run(plain, true);
            Same("(1) разбор состоялся", true, off != null && on != null);
            if (off == null || on == null)
            {
                return Finish();
            }

            Same("(1) фон без переполнения: рычаг не меняет ни бита", Print(off), Print(on));
            Same("(1) счётчик выведенных отсчётов — нуль", 0.0, on.BackgroundOverflowDropped);
            ZeroChannel(path, nuclides, overflowCounts);
            Direct(path, nuclides, overflowCounts, lineKev);

            foreach (double squeeze in new[] { 0.995, 1.0 })
            {
                string tag = squeeze == 1.0 ? "калибровки равны" : "шкала фона ×" + squeeze.ToString("R", CultureInfo.InvariantCulture);

                // тот же спектр с последним каналом фона, занулённым рукой, без рычага — мера для сцен ниже
                FsaResult byHand = Run(Scene(path, nuclides, squeeze, 0), false);
                Same("(2–4) " + tag + ": разбор с занулённым каналом состоялся", true, byHand != null);
                if (byHand == null)
                {
                    continue;
                }

                // (2) на отсчёт ниже порога — обычный канал
                FsaResult belowOff = Run(Scene(path, nuclides, squeeze, threshold - 1), false);
                FsaResult belowOn = Run(Scene(path, nuclides, squeeze, threshold - 1), true);
                Same("(2) " + tag + ": разбор состоялся", true, belowOff != null && belowOn != null);
                if (belowOff != null && belowOn != null)
                {
                    Console.WriteLine("(2) {0}: в последнем канале фона {1} отсч.; вычтено у верха {2:R}, с занулённым каналом {3:R}", tag, threshold - 1, TopBackground(belowOn), TopBackground(byHand));
                    Same("(2) " + tag + ": канал ниже порога вычитается", true, TopBackground(belowOn) > TopBackground(byHand));
                    Same("(2) " + tag + ": канал ниже порога рычаг не трогает", Print(belowOff), Print(belowOn));
                    Same("(2) " + tag + ": счётчик — нуль", 0.0, belowOn.BackgroundOverflowDropped);
                }

                // (3) переполнение в полосе фита
                ResultData scene = Scene(path, nuclides, squeeze, overflowCounts);
                double topKev = scene.EnergySpectrum.EnergyCalibration.ChannelToEnergy(last);
                double edgeKev = 0.9 * topKev;
                FsaResult redOff = Run(scene, false);
                FsaResult redOn = Run(Scene(path, nuclides, squeeze, overflowCounts), true);
                Same("(3) " + tag + ": разбор состоялся", true, redOff != null && redOn != null);
                if (redOff != null && redOn != null)
                {
                    Console.WriteLine("(3) {0}: переполнение {1} отсч., верх шкалы {2:F1} кэВ", tag, overflowCounts, topKev);
                    Console.WriteLine("(3)   без рычага: строка «{0}»; маска: {1}; отказ сторожа: {2}",
                                      FSAReportView.BackgroundOversubtractedText(redOff) ?? "—", Bands(redOff), redOff.BackgroundMaskRefused ?? "—");
                    Console.WriteLine("(3)   с рычагом:  строка «{0}»; маска: {1}; отказ сторожа: {2}",
                                      FSAReportView.BackgroundOversubtractedText(redOn) ?? "—", Bands(redOn), redOn.BackgroundMaskRefused ?? "—");
                    Same("(3) " + tag + ": без рычага красная строка у верха шкалы", true, RedAbove(redOff, edgeKev));
                    Same("(3) " + tag + ": с рычагом красной строки у верха нет", false, RedAbove(redOn, edgeKev));
                    Same("(3) " + tag + ": с рычагом участка маски у верха нет", false, MaskAbove(redOn, edgeKev));
                    Same("(3) " + tag + ": без рычага переполнение вычитается", true, TopBackground(redOff) > TopBackground(byHand));
                    Same("(3) " + tag + ": с рычагом у верха вычтено столько же, сколько без канала", TopBackground(byHand), TopBackground(redOn));
                    Same("(3) " + tag + ": с рычагом побитно как занулённый рукой", Print(byHand), Print(redOn));
                    Same("(3) " + tag + ": без рычага разбор другой", false, Print(byHand) == Print(redOff));
                    Same("(3) " + tag + ": счётчик с рычагом равен насыпанному", (double)overflowCounts, redOn.BackgroundOverflowDropped);
                    Same("(3) " + tag + ": счётчик без рычага — нуль", 0.0, redOff.BackgroundOverflowDropped);
                }

                // (4) ровно на пороге — переполнение
                FsaResult atOff = Run(Scene(path, nuclides, squeeze, threshold), false);
                FsaResult atOn = Run(Scene(path, nuclides, squeeze, threshold), true);
                Same("(4) " + tag + ": разбор состоялся", true, atOff != null && atOn != null);
                if (atOff != null && atOn != null)
                {
                    Same("(4) " + tag + ": на пороге без рычага канал вычитается", true, TopBackground(atOff) > TopBackground(byHand));
                    Same("(4) " + tag + ": на пороге с рычагом побитно как занулённый рукой", Print(byHand), Print(atOn));
                }
            }

            return Finish();
        }

        /// <summary>(5) Переполнение в нулевом канале фона и на обоих концах сразу; шкалы равны.</summary>
        static void ZeroChannel(string path, NuclideDefinitionManager nuclides, int counts)
        {
            Func<int, int, ResultData> scene = (first, lastCounts) =>
            {
                ResultData rd = Scene(path, nuclides, 1.0, lastCounts);
                rd.BackgroundEnergySpectrum.Spectrum[0] = first;
                return rd;
            };
            Same("(5) насыпанный нулевой канал правило признаёт переполнением", true,
                 OverflowChannel.IsOverflow(scene(counts, 0).BackgroundEnergySpectrum.Spectrum, 0));

            FsaResult byHand = Run(scene(0, 0), false);
            FsaResult off = Run(scene(counts, 0), false);
            FsaResult on = Run(scene(counts, 0), true);
            FsaResult both = Run(scene(counts, counts), true);
            Same("(5) разбор состоялся", true, byHand != null && off != null && on != null && both != null);
            if (byHand == null || off == null || on == null || both == null)
            {
                return;
            }

            Console.WriteLine("(5) нулевой канал фона {0} отсч.: вычтено в канале 0 без рычага {1:R}, с рычагом {2:R}, занулён рукой {3:R}",
                              counts, off.Background[0], on.Background[0], byHand.Background[0]);
            Same("(5) без рычага нулевой канал вычитается", true, Hash(off.Background) != Hash(byHand.Background));
            Same("(5) с рычагом вычтенный фон побитно как у занулённого рукой", Hash(byHand.Background), Hash(on.Background));
            Same("(5) счётчик равен насыпанному", (double)counts, on.BackgroundOverflowDropped);
            Same("(5) оба конца: вычтенный фон побитно как у занулённого рукой", Hash(byHand.Background), Hash(both.Background));
            Same("(5) оба конца: счётчик равен сумме", 2.0 * counts, both.BackgroundOverflowDropped);
            Console.WriteLine("(5) разбор целиком: занулён рукой {0}; с рычагом {1}", Print(byHand), Print(on));
        }

        /// <summary>
        /// (6), (7) Прямой вызов анализатора на составе из одной линии (`--line=`, кэВ): фон вызывающего цел;
        /// фон без калибровки при поканальном вычитании.
        /// </summary>
        static void Direct(string path, NuclideDefinitionManager nuclides, int counts, double lineKev)
        {
            Func<bool, bool, int, int[], FsaResult> run = (lever, noCalibration, lastCounts, seen) =>
            {
                ResultData rd = Scene(path, nuclides, 1.0, lastCounts);
                EnergySpectrum background = rd.BackgroundEnergySpectrum;
                if (noCalibration)
                {
                    background.EnergyCalibration = null;
                }

                var component = new FsaComponent("образ", FsaComponentKind.Single);
                component.Lines.Add(new FsaLine("образ", lineKev, 1.0));
                var an = new FsaAnalyzer();
                an.SampleBeta = false;
                an.BackgroundOverflowOut = lever;
                an.RebinBackgroundToSpectrum = !noCalibration;
                FsaTuningReport.Print(an);
                int[] before = (int[])background.Spectrum.Clone();
                FsaResult result = an.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                              new List<FsaComponent> { component }, FsaEfficiency.FromConfig(rd.Efficiency));
                int changed = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] != background.Spectrum[i]) changed++;
                }

                seen[0] = changed;
                seen[1] = background.Spectrum[before.Length - 1];
                return result;
            };

            foreach (bool noCalibration in new[] { false, true })
            {
                string scene = noCalibration ? "(7) фон без калибровки" : "(6) прямой вызов";
                int[] seenHand = new int[2], seenOff = new int[2], seenOn = new int[2];
                FsaResult byHand = run(false, noCalibration, 0, seenHand);
                FsaResult off = run(false, noCalibration, counts, seenOff);
                FsaResult on = run(true, noCalibration, counts, seenOn);
                Same(scene + ": разбор состоялся", true, byHand != null && off != null && on != null
                     && byHand.Model != null && off.Model != null && on.Model != null);
                if (byHand == null || off == null || on == null || byHand.Model == null || off.Model == null || on.Model == null)
                {
                    continue;
                }

                Console.WriteLine("{0}: χ²П без рычага {1:R}, с рычагом {2:R}, занулён рукой {3:R}; вычтено у верха {4:R} / {5:R} / {6:R}",
                                  scene, off.Chi2NdfPoisson, on.Chi2NdfPoisson, byHand.Chi2NdfPoisson,
                                  TopBackground(off), TopBackground(on), TopBackground(byHand));
                Same(scene + ": фон вычитается (без рычага переполнение у верха)", true, TopBackground(off) > TopBackground(byHand));
                Same(scene + ": с рычагом побитно как занулённый рукой", Print(byHand), Print(on));
                Same(scene + ": без рычага разбор другой", false, Print(byHand) == Print(off));
                Same(scene + ": счётчик равен насыпанному", (double)counts, on.BackgroundOverflowDropped);
                Same(scene + ": счётчик без рычага — нуль", 0.0, off.BackgroundOverflowDropped);
                Same(scene + ": у фона вызывающего изменённых каналов", 0, seenOn[0]);
                Same(scene + ": последний канал фона вызывающего после разбора", counts, seenOn[1]);
            }
        }

        static int Finish()
        {
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Наименьшее содержимое последнего канала, которое правило признаёт переполнением.</summary>
        static int Threshold(int[] spectrum)
        {
            int[] copy = (int[])spectrum.Clone();
            int n = copy.Length;
            for (int v = 1; v < 10000000; v++)
            {
                copy[n - 1] = v;
                if (OverflowChannel.IsOverflow(copy, n - 1))
                {
                    return v;
                }
            }

            return int.MaxValue;
        }

        /// <summary>Спектр сцены: шкала фона сжата, в последнем канале фона — <paramref name="lastCounts"/>.</summary>
        static ResultData Scene(string path, NuclideDefinitionManager nuclides, double squeeze, int lastCounts)
        {
            ResultData rd = Load(path, nuclides);
            EnergySpectrum background = rd.BackgroundEnergySpectrum;
            background.Spectrum[background.Spectrum.Length - 1] = lastCounts;
            if (squeeze != 1.0)
            {
                var polynomial = background.EnergyCalibration as PolynomialEnergyCalibration;
                if (polynomial == null)
                {
                    throw new InvalidOperationException("сцене нужна полиномиальная калибровка фона");
                }

                var squeezed = (PolynomialEnergyCalibration)polynomial.Clone();
                for (int k = 0; k < squeezed.Coefficients.Length; k++)
                {
                    squeezed.Coefficients[k] *= squeeze;
                }

                squeezed.InvalidateCache();
                background.EnergyCalibration = squeezed;
            }

            return rd;
        }

        /// <summary>Вычтенный фон в верхних 2 % каналов (как в `FsaResult.Background`); судится разностью с разбором без канала.</summary>
        static double TopBackground(FsaResult result)
        {
            double sum = 0.0;
            double[] b = result.Background;
            if (b == null)
            {
                return 0.0;
            }

            for (int i = b.Length - Math.Max(2, b.Length / 50); i < b.Length; i++)
            {
                sum += Math.Abs(b[i]);
            }

            return sum;
        }

        static bool RedAbove(FsaResult result, double kev)
        {
            return result.BackgroundOversubtracted != null && result.BackgroundOversubtracted.HiKev >= kev;
        }

        static bool MaskAbove(FsaResult result, double kev)
        {
            if (result.BackgroundMaskBands == null)
            {
                return false;
            }

            foreach (FsaBandWarning band in result.BackgroundMaskBands)
            {
                if (band.HiKev >= kev)
                {
                    return true;
                }
            }

            return false;
        }

        static string Bands(FsaResult result)
        {
            if (result.BackgroundMaskBands == null || result.BackgroundMaskBands.Count == 0)
            {
                return "нет";
            }

            var sb = new StringBuilder();
            foreach (FsaBandWarning band in result.BackgroundMaskBands)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F0}–{1:F0} ", band.LoKev, band.HiKev);
            }

            return sb.ToString().Trim() + " кэВ";
        }

        /// <summary>Побитный отпечаток разбора: χ², усиление, вычтенный фон, модель.</summary>
        static string Print(FsaResult result)
        {
            var sb = new StringBuilder();
            sb.Append(result.Chi2NdfPoisson.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(result.Gain.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(result.BackgroundMaskChannels.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Hash(result.Background)).Append('|').Append(Hash(result.Model));
            return sb.ToString();
        }

        static string Hash(double[] values)
        {
            if (values == null)
            {
                return "null";
            }

            ulong h = 14695981039346656037UL;
            foreach (double v in values)
            {
                ulong bits = (ulong)BitConverter.DoubleToInt64Bits(v);
                for (int k = 0; k < 8; k++)
                {
                    h ^= (bits >> (8 * k)) & 0xFF;
                    h *= 1099511628211UL;
                }
            }

            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        static FsaResult Run(ResultData rd, bool lever)
        {
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            FsaAnalysisSession.ProbeAnalyzerHook = an =>
            {
                an.BackgroundOverflowOut = lever;
                FsaTuningReport.Print(an);
            };
            try
            {
                session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, FsaCalculationOptions.Of(rd));
                return done.WaitOne(TimeSpan.FromSeconds(600)) ? session.Result : null;
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
            }
        }

        static void Same<T>(string what, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "OK " : "⛔ ", what, expected, actual);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
