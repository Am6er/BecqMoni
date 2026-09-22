using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaScaleTopProbe
{
    /// <summary>
    /// ВЕРХ ШКАЛЫ: КУДА ДЕВАЕТСЯ ОТКЛИК ВЫШЕ `E(N)` (`AMBER72`, П134
    /// 22.09.2026).
    ///
    /// ⛔ ЧТО МЕРИТСЯ И ПОЧЕМУ ИМЕННО ТАК. `EnergyToChannel` зажимает всё, что
    /// выше верха шкалы, в канал `N` (`PolynomialEnergyCalibration.cs:253`), а
    /// укладчик источников образа канал `N` принимает. Значит надшкальная часть
    /// отклика — сумм-пики каскада, наложения, хвост комптона — садилась НА
    /// верх шкалы, и левая половина ядра уширения расползалась по последним
    /// каналам образа ложным пиком у края.
    ///
    /// Судить об этом по одному спектру нельзя: у ложного пика нет подписи, а
    /// настоящий левый хвост надшкального пика в окно фита ТОЖЕ попадает и
    /// тоже поднимает край. Поэтому проба режет шкалу НАРОЧНО:
    ///
    ///   * `--cut-kev=<E>` — шкала обрезается так, чтобы её верх встал чуть
    ///     выше `E`; отсчёты выше просто отброшены, коэффициенты калибровки не
    ///     тронуты, то есть `E(ch)` у оставшихся каналов ТОТ ЖЕ.
    ///   * если резать НИЖЕ сильной линии состава (Tl-208 2614 кэВ), то у
    ///     данных в последних каналах остаётся один континуум, а у модели с
    ///     зажимом — вся площадь этой линии, свёрнутая в край.
    ///
    /// Мерка — превышение модели над данными в последних `--tail` каналах в
    /// единицах пуассоновского шума данных: `z = (модель − данные)/√max(данные,1)`.
    /// Средний `z` по хвосту выше `--limit` — ОТКАЗ кодом 1.
    ///
    /// ⚠ Это положительный контроль самой правки: на коде ДО неё проба обязана
    /// быть КРАСНОЙ, на коде после — зелёной. Без разреза (`--cut-kev` не
    /// задан) проба меряет спектр как есть — ею же снимается контроль
    /// неизменности на шкале, целиком накрывающей отклик.
    ///
    ///     fsascaletopprobe --spectrum=&lt;файл.xml&gt; [--chain=Th-232] [--sample=137CS]
    ///                      [--cut-kev=2000] [--tail=20] [--limit=3] [--out=&lt;csv&gt;]
    ///                      [--matrix-any]
    ///
    /// Состав — из базы по объявленному составу (`AMBER19`, общий вход
    /// `FsaSampleSpec.FromManifest`), поставочный список не поднимается.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            string outPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            double cutKev = Double.NaN;
            int tail = 20;
            double limit = 3.0;
            bool matrixAny = false;

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--cut-kev=", StringComparison.Ordinal))
                    cutKev = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--tail=", StringComparison.Ordinal))
                    tail = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                    limit = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a == "--matrix-any") matrixAny = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл.xml>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));

            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;

            if (Finite(cutKev))
            {
                int keep = spectrum.NumberOfChannels;
                while (keep > 8 && calibration.ChannelToEnergy(keep - 1) > cutKev)
                {
                    keep--;
                }

                int[] cut = new int[keep];
                Array.Copy(spectrum.Spectrum, cut, keep);
                long total = 0;
                for (int i = 0; i < keep; i++) total += cut[i];
                spectrum.Spectrum = cut;
                spectrum.NumberOfChannels = keep;
                spectrum.TotalPulseCount = total;
                spectrum.ValidPulseCount = total;
                Console.WriteLine("⚠ ШКАЛА ОБРЕЗАНА по --cut-kev={0}: каналов {1}, верх E(N) = {2} кэВ",
                                  F(cutKev, 1), keep, F(calibration.ChannelToEnergy(keep), 2));
            }

            int channels = spectrum.NumberOfChannels;
            double topKev = calibration.ChannelToEnergy(channels);
            Console.WriteLine("шкала  : каналов {0}, E(N-1) = {1} кэВ, E(N) = {2} кэВ",
                              channels, F(calibration.ChannelToEnergy(channels - 1), 2), F(topKev, 2));

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат {1})", refusal, fileFormat);
                return 1;
            }

            bool stampOk = rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (!stampOk && !matrixAny)
            {
                Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ; осознанно — ключ --matrix-any");
                return 1;
            }

            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);

            string material = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);
            var analyzer = new FsaAnalyzer { ResponseMatrix = matrix, ScintillatorMaterial = material };
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            FsaTuningReport.Print(analyzer, "верх шкалы");

            FsaResult result = analyzer.Analyze(spectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся");
                return 1;
            }

            Console.WriteLine("разбор : chi2/ndf {0}, состав {1}", F(result.Chi2Ndf, 3), result.Components.Count);

            int lo = Math.Max(0, channels - tail);
            var rows = new List<string> { "channel;energy_kev;measured;model;z" };
            double sumZ = 0.0, worstZ = Double.NegativeInfinity;
            int worstCh = -1, counted = 0;
            double modelTail = 0.0, dataTail = 0.0;
            for (int i = lo; i < channels; i++)
            {
                double back = result.Background != null && i < result.Background.Length ? result.Background[i] : 0.0;
                double measured = (i < spectrum.Spectrum.Length ? spectrum.Spectrum[i] : 0.0) - back;
                double model = result.Model != null && i < result.Model.Length ? result.Model[i] : 0.0;
                double noise = Math.Sqrt(Math.Max(measured + back, 1.0));
                double z = (model - measured) / noise;
                modelTail += model;
                dataTail += measured;
                sumZ += z;
                counted++;
                if (z > worstZ) { worstZ = z; worstCh = i; }
                rows.Add(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4}",
                    i, F(calibration.ChannelToEnergy(i), 3), F(measured, 3), F(model, 3), F(z, 3)));
            }

            double meanZ = counted > 0 ? sumZ / counted : 0.0;
            Console.WriteLine();
            Console.WriteLine("=== ХВОСТ ШКАЛЫ: последние {0} каналов ({1}…{2} кэВ) ===",
                              counted, F(calibration.ChannelToEnergy(lo), 1),
                              F(calibration.ChannelToEnergy(channels - 1), 1));
            Console.WriteLine("данные за вычетом фона : {0}", F(dataTail, 2));
            Console.WriteLine("модель                 : {0}", F(modelTail, 2));
            Console.WriteLine("превышение модели      : {0} отсчётов ({1} %)",
                              F(modelTail - dataTail, 2),
                              F(dataTail > 0.0 ? 100.0 * (modelTail - dataTail) / dataTail : Double.NaN, 1));
            Console.WriteLine("средний z по хвосту    : {0} (предел {1})", F(meanZ, 3), F(limit, 2));
            Console.WriteLine("наибольший z           : {0} в канале {1} ({2} кэВ)",
                              F(worstZ, 3), worstCh, F(calibration.ChannelToEnergy(Math.Max(worstCh, 0)), 1));

            if (outPath != null)
            {
                File.WriteAllLines(outPath, rows, new UTF8Encoding(false));
                Console.WriteLine("записано: {0}", outPath);
            }

            int raised = NuclideDefinitionManager.RaiseCount;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0}", raised);
            if (raised > 0)
            {
                Console.Error.WriteLine("⛔ AMBER19: поставочную библиотеку поднимали {0} раз(а) — числа негодны", raised);
                return 12;
            }

            if (meanZ > limit)
            {
                Console.Error.WriteLine(
                    "⛔ AMBER72: модель в последних {0} каналах выше данных на {1} σ в среднем "
                    + "(предел {2}) — надшкальный отклик сидит НА верхе шкалы",
                    counted, F(meanZ, 2), F(limit, 2));
                return 1;
            }

            Console.WriteLine("СОШЛОСЬ: край шкалы чист (средний z {0} ≤ {1})", F(meanZ, 2), F(limit, 2));
            return 0;
        }

        static bool Finite(double v)
        {
            return !Double.IsNaN(v) && !Double.IsInfinity(v);
        }

        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
        }

        /// <summary>«Cs-137» → «137CS» (nucid, как его зовёт nucdb); nucid — как есть.</summary>
        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                if (label.Length == 0) continue;
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0
                    ? label.ToUpperInvariant()
                    : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
            }

            return nucids;
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
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                rd.FwhmCalibration = cfg.FwhmCalibration;
            }

            return rd;
        }
    }
}
