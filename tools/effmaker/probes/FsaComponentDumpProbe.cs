using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaComponentDumpProbe
{
    /// <summary>
    /// СОСТАВ РАЗБОРА ПОИМЁННО: какие линии у каждого компонента и где лежит
    /// его лента.
    ///
    /// Заведена 11.09.2026 по разбору внешнего рецензента. Он спросил
    /// «фактические списки `FsaLine` для Cs-137 и Ba-131», и вопрос оказался
    /// не праздным: по картинке видно только СУММУ состава, а спор «это линия
    /// нуклида или артефакт подгонки» решается лишь списком линий и лентой
    /// того компонента, которому её приписали.
    ///
    /// ⛔ Своего разбора здесь НЕТ: библиотека собирается тем же
    /// `FsaLibrary.BuildFromPeaks`, которым её собирает окно, а считает тот же
    /// `FsaAnalyzer`. Проба только печатает то, что у них получилось.
    ///
    ///     fsacomponentdumpprobe --spectrum=&lt;файл.xml&gt; [--out=&lt;префикс&gt;]
    ///                           [--set=Имя] [--matrix-any]
    ///
    /// Печатает состав библиотеки и состав разбора; пишет `&lt;префикс&gt;.csv`
    /// (канал, энергия, измерение, модель, лента каждого компонента) и
    /// `&lt;префикс&gt;-lines.csv` (компонент, энергия линии, выход).
    /// </summary>
    static class Program
    {
        static readonly string[] ChannelNames =
        {
            "peak", "compton", "esc_se", "esc_xray", "esc_de"
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            string outPrefix = null;
            string setName = null;
            bool matrixAny = false;

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else if (a.StartsWith("--out=", StringComparison.Ordinal))
                {
                    outPrefix = a.Substring(6);
                }
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    setName = a.Substring(6);
                }
                else if (a == "--matrix-any")
                {
                    matrixAny = true;
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (outPrefix == null)
            {
                outPrefix = "components";
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));
            if (setName != null)
            {
                foreach (NuclideSet set in nuclides.NuclideSets)
                {
                    if (set != null && string.Equals(set.Name, setName, StringComparison.Ordinal))
                    {
                        nuclides.ActiveSet = set;
                    }
                }
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            bool stampOk = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат {1})", refusal, fileFormat);
                return 1;
            }

            if (!stampOk && !matrixAny)
            {
                Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ; осознанно — ключ --matrix-any");
                return 1;
            }

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);

            Console.WriteLine("библиотека прибора: {0} определений",
                              CountOf(nuclides.NuclideDefinitions));
            Console.WriteLine("найдено пиков: {0}; образов собрано: {1}", peaks.Count, library.Count);
            Console.WriteLine();
            Console.WriteLine("=== СОСТАВ БИБЛИОТЕКИ: линии каждого образа ===");

            var lineRows = new List<string>();
            lineRows.Add("component;kind;energy_kev;intensity_pct");
            foreach (FsaComponent component in library)
            {
                Console.WriteLine("  {0,-14} {1,-10} линий {2}",
                                  component.Name, component.Kind, component.Lines.Count);
                foreach (FsaLine line in component.Lines)
                {
                    Console.WriteLine("      {0,10} кэВ   выход {1,8} %",
                                      F(line.Energy, 3), F(line.Intensity, 4));
                    lineRows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0};{1};{2};{3}", component.Name, component.Kind,
                        F(line.Energy, 4), F(line.Intensity, 5)));
                }
            }

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

            FsaTuningReport.Print(analyzer, "состав разбора");

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("разбор : chi2/ndf {0}, состав {1}",
                              F(result.Chi2Ndf, 3), result.Components.Count);

            // ⛔ ВТОРОЙ ПРОХОД ПО ТОЙ ЖЕ БИБЛИОТЕКЕ — не украшение. Анализатор
            // достраивает состав сам (образы вылета, обратное рассеяние,
            // наложения), и вопрос «откуда у образа одной линии пик за 500 кэВ»
            // решается только сравнением списка ДО и ПОСЛЕ разбора.
            Console.WriteLine();
            Console.WriteLine("=== ЛИНИИ ОБРАЗОВ ПОСЛЕ РАЗБОРА (та же библиотека) ===");
            foreach (FsaComponent component in library)
            {
                Console.WriteLine("  {0,-14} {1,-10} линий {2}",
                                  component.Name, component.Kind, component.Lines.Count);
                if (component.Lines.Count <= 12)
                {
                    foreach (FsaLine line in component.Lines)
                    {
                        Console.WriteLine("      {0,10} кэВ   выход {1,8} %   нуклид {2}",
                                          F(line.Energy, 3), F(line.Intensity, 4), line.Nuclide);
                    }
                }
            }

            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int channels = rd.EnergySpectrum.NumberOfChannels;

            // Измерение — то же, что видит человек: за вычетом фона, который
            // подобрал сам разбор.
            double[] measured = new double[channels];
            int[] raw = rd.EnergySpectrum.Spectrum;
            for (int i = 0; i < channels; i++)
            {
                double back = result.Background != null && i < result.Background.Length
                    ? result.Background[i] : 0.0;
                measured[i] = (i < raw.Length ? raw[i] : 0.0) - back;
            }

            // Где лента компонента вообще не ноль — тот самый вопрос «может ли
            // образ одной линии 31 кэВ давать отсчёты при 160».
            Console.WriteLine();
            Console.WriteLine("=== ГДЕ ЛЕЖИТ ЛЕНТА КАЖДОГО КОМПОНЕНТА ===");
            Console.WriteLine("{0,-14} {1,10} {2,10} {3,14} {4,12}",
                              "компонент", "от, кэВ", "до, кэВ", "площадь", "максимум, кэВ");
            foreach (FsaComponentResult component in result.Components)
            {
                double[] curve = component.Curve;
                if (curve == null)
                {
                    continue;
                }

                int lo = -1, hi = -1, top = -1;
                double sum = 0.0, best = 0.0;
                for (int i = 0; i < curve.Length && i < channels; i++)
                {
                    if (!(curve[i] > 0.0))
                    {
                        continue;
                    }

                    if (lo < 0)
                    {
                        lo = i;
                    }

                    hi = i;
                    sum += curve[i];
                    if (curve[i] > best)
                    {
                        best = curve[i];
                        top = i;
                    }
                }

                if (lo < 0)
                {
                    continue;
                }

                Console.WriteLine("{0,-14} {1,10} {2,10} {3,14} {4,12}",
                                  component.Name,
                                  F(calibration.ChannelToEnergy(lo), 1),
                                  F(calibration.ChannelToEnergy(hi), 1),
                                  F(sum, 1),
                                  F(calibration.ChannelToEnergy(top), 1));
            }

            var rows = new List<string>();
            var header = new StringBuilder("channel;energy_kev;measured;model");
            foreach (FsaComponentResult component in result.Components)
            {
                header.Append(';').Append(component.Name);
            }

            rows.Add(header.ToString());
            for (int i = 0; i < channels; i++)
            {
                var sb = new StringBuilder();
                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(';');
                sb.Append(F(calibration.ChannelToEnergy(i), 3)).Append(';');
                sb.Append(F(i < measured.Length ? measured[i] : 0.0, 3)).Append(';');
                sb.Append(F(result.Model != null && i < result.Model.Length ? result.Model[i] : 0.0, 3));
                foreach (FsaComponentResult component in result.Components)
                {
                    double[] curve = component.Curve;
                    sb.Append(';').Append(F(curve != null && i < curve.Length ? curve[i] : 0.0, 3));
                }

                rows.Add(sb.ToString());
            }

            File.WriteAllLines(outPrefix + ".csv", rows, new UTF8Encoding(false));
            File.WriteAllLines(outPrefix + "-lines.csv", lineRows, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("записано: {0}.csv и {0}-lines.csv", outPrefix);
            return 0;
        }

        static int CountOf(IEnumerable<NuclideDefinition> definitions)
        {
            int n = 0;
            if (definitions != null)
            {
                foreach (NuclideDefinition d in definitions)
                {
                    n++;
                }
            }

            return n;
        }

        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
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
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

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
