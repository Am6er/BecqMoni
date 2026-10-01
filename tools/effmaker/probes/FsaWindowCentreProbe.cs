using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaWindowCentreProbe
{
    /// <summary>
    /// СТОРОЖ: ПИКОВОЕ ОКНО СТОИТ ТАМ, ГДЕ ПИК ОБРАЗА (`AMBER89`, П144
    /// 24.09.2026).
    ///
    ///     fsawindowcentreprobe --spectrum=&lt;файл.xml&gt; [--sample=241AM] [--chain=Th-228]
    ///                          [--lines=59.54,2614.5] [--min-share=10] [--matrix-any]
    ///
    /// Запускать ИЗ оснастки (`tools\CORPUS\scripts\wd_*`): оттуда берутся
    /// приборы спектра и склад матриц.
    ///
    /// ⛔ ЧТО МЕРИТСЯ. Форма света "line" (умолчание с 12.09.2026) кладёт пик
    /// линии в образ на E + s(E), а пиковое окно ±2 ПШПВ, по которому
    /// считаются «пиковые отсчёты», предел и зонная мерка, до П144 ставилось
    /// на E. Проба берёт у разбора три вещи и сверяет их между собой:
    ///
    ///   * ПИК ОБРАЗА — вершина пикового канала (`ChannelCurves[Peak]`) строки
    ///     компонента, парабола по трём точкам; своё построение, не API;
    ///   * ДВЕ КАНДИДАТНЫЕ ПОЗИЦИИ — p(E) и p(E + s), обе через карту разбора
    ///     (`LightToChannel`, отражением) и дрейф результата;
    ///   * НАСТОЯЩЕЕ ОКНО — маска `PeakWindowMask` (отражением), та самая, по
    ///     которой разбор суммирует пиковые отсчёты.
    ///
    /// Окно узнаётся ТОЧНО, а не по середине отрезка: для каждой кандидатной
    /// позиции проба строит отрезок правилом разбора (`floor(p − 2w)` …
    /// `ceil(p + 2w)`) и смотрит, какой из них совпал с отрезком маски. Середина
    /// отрезка врала бы на полканала — у 59.5 кэВ на NaI это треть ПШПВ.
    ///
    /// Приговор по линии: окно ОБЯЗАНО совпасть с отрезком позиции E + s. Если
    /// оба кандидата дают один отрезок (s меньше канала), линия НЕ СУДИТСЯ и
    /// называется «неразличимо». Совпало с E и не с E + s — ОТКАЗ.
    ///
    /// Код: 0 — все судимые линии на месте; 1 — окно не на пике (или судить
    /// нечего); 2 — ключи; 12 — поднимали поставочную библиотеку.
    /// </summary>
    static class Program
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            var wanted = new List<double>();
            double minShare = 10.0;
            bool matrixAny = false;

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                    chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--lines=", StringComparison.Ordinal))
                {
                    foreach (string raw in a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        double v;
                        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        {
                            Console.Error.WriteLine("не разобрано --lines=: {0}", raw);
                            return 2;
                        }

                        wanted.Add(v);
                    }
                }
                else if (a.StartsWith("--min-share=", StringComparison.Ordinal))
                {
                    if (!double.TryParse(a.Substring(12), NumberStyles.Float, CultureInfo.InvariantCulture, out minShare))
                    {
                        Console.Error.WriteLine("не разобрано --min-share=");
                        return 2;
                    }
                }
                else if (a == "--matrix-any") matrixAny = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (spectrumPath == null || (chains.Count == 0 && nuclides.Count == 0))
            {
                Console.Error.WriteLine("нужны --spectrum=<файл.xml> и состав --sample=/--chain=");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (rd == null) return 1;
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат {1}) — пик образа по пиковому каналу судится только при матрице",
                                        refusal, fileFormat);
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
            // (`T263`, П193) матрица — через `FsaMatrixBinding.Bind`, как у приложения: с нею едут Q_k угловых корреляций и обстановка (домик)
            var analyzer = new FsaAnalyzer();
            FsaMatrixBinding.Bind(analyzer, rd.Efficiency != null ? rd.Efficiency.Geometry : null, matrix);
            analyzer.ScintillatorMaterial = material;
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

            FsaTuningReport.Print(analyzer, "окно пика");
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library, efficiency);
            if (result == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся: {0}", analyzer.Refusal);
                return 1;
            }

            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            FwhmCalibration fwhm = rd.FwhmCalibration;
            int channels = rd.EnergySpectrum.NumberOfChannels;
            Console.WriteLine("разбор : χ²/ndf {0}; усиление {1}, сдвиг {2} кан.; полоса {3}…{4}; свет {5} β {6} форма {7}",
                              F(result.Chi2Ndf, 3), F(result.Gain, 6), F(result.OffsetChannels, 3),
                              result.FirstChannel, result.LastChannel,
                              result.AnchorLightCurve, F(result.AnchorLightBeta, 3), result.AnchorLightForm);

            MethodInfo maskOf = typeof(FsaAnalyzer).GetMethod("PeakWindowMask", Private);
            MethodInfo positionKev = typeof(FsaAnalyzer).GetMethod("LinePositionKev", Private);
            MethodInfo lightToChannel = typeof(FsaAnalyzer).GetMethod("LightToChannel", Private);
            if (maskOf == null || positionKev == null || lightToChannel == null)
            {
                Console.Error.WriteLine("⛔ отражение не нашло PeakWindowMask / LinePositionKev / LightToChannel — проба устарела");
                return 1;
            }

            int peakChannel = (int)EfficiencySimulator.ResponseChannel.Peak;
            int judged = 0, bad = 0, same = 0;
            Console.WriteLine();
            Console.WriteLine("компонент\tE, кэВ\ts, кэВ\ts/ПШПВ\tпик образа\tp(E)\tp(E+s)\tокно\tокно на\tобраз−p(E+s), ПШПВ\tокно−пик, ПШПВ\tдоля пика в окне");
            foreach (FsaComponentResult row in result.Components)
            {
                FsaComponent component = ByName(library, row.Name);
                if (component == null || row.ChannelCurves == null || row.ChannelCurves.Length <= peakChannel
                    || row.ChannelCurves[peakChannel] == null)
                {
                    continue;
                }

                double[] peakCurve = row.ChannelCurves[peakChannel];
                var mask = (bool[])maskOf.Invoke(analyzer, new object[]
                {
                    component, efficiency, calibration, fwhm, result.Gain, result.OffsetChannels,
                    result.FirstChannel, result.LastChannel, channels
                });
                if (mask == null)
                {
                    continue;
                }

                double[] weights = analyzer.PeakWindowLinePeakCounts(component, efficiency);
                double top = 0.0;
                for (int i = 0; weights != null && i < weights.Length; i++) top = Math.Max(top, weights[i]);

                for (int j = 0; j < component.Lines.Count; j++)
                {
                    FsaLine line = component.Lines[j];
                    double e = line.Energy;
                    if (wanted.Count > 0 && !Named(wanted, e)) continue;
                    if (wanted.Count == 0 && !(weights != null && top > 0.0 && 100.0 * weights[j] >= minShare * top)) continue;

                    double sKev = (double)positionKev.Invoke(analyzer, new object[] { e }) - e;
                    double pE = Drift(result, (double)lightToChannel.Invoke(analyzer, new object[] { calibration, e, channels }));
                    double pS = Drift(result, (double)lightToChannel.Invoke(analyzer, new object[] { calibration, e + sKev, channels }));
                    double w = fwhm.ChannelToFwhm(pS);
                    if (!(w > 0.0) || !Finite(pE) || !Finite(pS))
                    {
                        Console.WriteLine("{0}	{1}	пропуск: позиция или ПШПВ не определены", row.Name, F(e, 3));
                        continue;
                    }

                    // Вершина пикового канала образа: ЛОКАЛЬНЫЙ максимум, ближайший
                    // к середине между кандидатами, на отрезке, накрывающем оба с
                    // запасом полуширины, — поиск не подсказывает ответ ни одному
                    // из них, а соседняя линия ряда не перебивает своей высотой.
                    int a0 = Math.Max(result.FirstChannel + 1, (int)Math.Floor(Math.Min(pE, pS) - 0.5 * w));
                    int a1 = Math.Min(result.LastChannel - 1, (int)Math.Ceiling(Math.Max(pE, pS) + 0.5 * w));
                    double mid = 0.5 * (pE + pS);
                    int best = -1;
                    for (int i = a0; i <= a1; i++)
                    {
                        if (peakCurve[i] >= peakCurve[i - 1] && peakCurve[i] >= peakCurve[i + 1] && peakCurve[i] > 0.0
                            && (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid)))
                        {
                            best = i;
                        }
                    }

                    if (best <= result.FirstChannel || best >= result.LastChannel || !(peakCurve[best] > 0.0))
                    {
                        Console.WriteLine("{0}	{1}	пропуск: вершины пикового канала у p(E)={2} / p(E+s)={3} нет",
                                          row.Name, F(e, 3), F(pE, 2), F(pS, 2));
                        continue;
                    }
                    double y0 = peakCurve[best - 1], y1 = peakCurve[best], y2 = peakCurve[best + 1];
                    double den = y0 - 2.0 * y1 + y2;
                    double image = best + (den < 0.0 ? 0.5 * (y0 - y2) / den : 0.0);

                    // Отрезок маски, накрывающий вершину образа.
                    int c = (int)Math.Round(image);
                    if (c < 0 || c >= channels || !mask[c])
                    {
                        Console.WriteLine("{0}\t{1}\t{2}\t—\t{3}\t{4}\t{5}\tНЕТ ОКНА НА ПИКЕ",
                                          row.Name, F(e, 3), F(sKev, 2), F(image, 2), F(pE, 2), F(pS, 2));
                        judged++;
                        bad++;
                        continue;
                    }

                    int from = c, to = c;
                    while (from - 1 >= result.FirstChannel && mask[from - 1]) from--;
                    while (to + 1 <= result.LastChannel && mask[to + 1]) to++;

                    // Окно, слитое с соседним, узнаётся по СВОБОДНОМУ краю: у
                    // слитого отрезка один край чужой, другой — этой линии.
                    string edge = "";
                    bool onE = Matches(pE, fwhm, from, to, result.FirstChannel, result.LastChannel, 0);
                    bool onS = Matches(pS, fwhm, from, to, result.FirstChannel, result.LastChannel, 0);
                    if (!onE && !onS)
                    {
                        for (int side = 1; side <= 2 && !onE && !onS; side++)
                        {
                            bool e1 = Matches(pE, fwhm, from, to, result.FirstChannel, result.LastChannel, side);
                            bool s1 = Matches(pS, fwhm, from, to, result.FirstChannel, result.LastChannel, side);
                            if (e1 != s1)
                            {
                                onE = e1;
                                onS = s1;
                                edge = side == 1 ? " (верх. край)" : " (ниж. край)";
                            }
                        }
                    }

                    string verdict;
                    if (onE && onS) { verdict = "неразличимо"; same++; }
                    else if (onS) { verdict = "E+s ✓" + edge; judged++; }
                    else if (onE) { verdict = "E ⛔" + edge; judged++; bad++; }
                    else { verdict = "слито/край"; }

                    double inWindow = 0.0, around = 0.0;
                    int r0 = Math.Max(result.FirstChannel, (int)Math.Floor(image - 3.0 * w));
                    int r1 = Math.Min(result.LastChannel, (int)Math.Ceiling(image + 3.0 * w));
                    for (int i = r0; i <= r1; i++)
                    {
                        around += peakCurve[i];
                        if (i >= from && i <= to) inWindow += peakCurve[i];
                    }

                    double windowCentre = onS ? pS : (onE ? pE : 0.5 * (from + to));
                    Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}…{8}\t{9}\t{10}\t{11}\t{12}",
                                      row.Name, F(e, 3), F(sKev, 2), F(sKev / Math.Max(KevWidth(calibration, pS, w), 1e-9), 3),
                                      F(image, 2), F(pE, 2), F(pS, 2), from, to, verdict,
                                      F((image - pS) / w, 3), F((windowCentre - image) / w, 3),
                                      F(around > 0.0 ? inWindow / around : double.NaN, 4));
                }

                Console.WriteLine("ROW\t{0}\tпиковых отсчётов {1}", row.Name, F(row.PeakCounts, 3));
            }

            Console.WriteLine();
            Console.WriteLine("судимых линий {0}, окно не на пике {1}, неразличимо (|s| < канала) {2}", judged, bad, same);

            int raised = NuclideDefinitionManager.RaiseCount;
            if (raised > 0)
            {
                Console.Error.WriteLine("⛔ AMBER19: поставочную библиотеку поднимали {0} раз(а)", raised);
                return 12;
            }

            if (judged == 0)
            {
                Console.Error.WriteLine("⛔ судить нечего: ни одна линия не различила E и E+s");
                return 1;
            }

            if (bad > 0)
            {
                Console.Error.WriteLine("⛔ AMBER89: окно стоит на E, а пик образа — на E+s, у {0} линий", bad);
                return 1;
            }

            Console.WriteLine("СОШЛОСЬ: окно на пике образа у всех судимых линий");
            return 0;
        }

        /// <summary>ПШПВ в кэВ у позиции p (ширина w в каналах).</summary>
        static double KevWidth(EnergyCalibration calibration, double p, double w)
        {
            return calibration.ChannelToEnergy(p + 0.5 * w) - calibration.ChannelToEnergy(p - 0.5 * w);
        }

        /// <summary>
        /// Отрезок окна правилом разбора (`MarkPeakWindow`) для позиции p:
        /// side 0 — оба края, 1 — только верхний, 2 — только нижний.
        /// </summary>
        static bool Matches(double p, FwhmCalibration fwhm, int from, int to, int chLo, int chHi, int side)
        {
            double w = fwhm.ChannelToFwhm(p);
            if (!(w > 0.0)) return false;
            int f = Math.Max(chLo, (int)Math.Floor(p - 2.0 * w));
            int t = Math.Min(chHi, (int)Math.Ceiling(p + 2.0 * w));
            if (side == 1) return t == to;
            if (side == 2) return f == from;
            return f == from && t == to;
        }

        static double Drift(FsaResult result, double position)
        {
            return result.Gain * position + result.OffsetChannels;
        }

        static bool Named(List<double> list, double energy)
        {
            foreach (double v in list)
            {
                if (Math.Abs(v - energy) <= 0.5) return true;
            }

            return false;
        }

        static FsaComponent ByName(List<FsaComponent> library, string name)
        {
            foreach (FsaComponent component in library)
            {
                if (string.Equals(component.Name, name, StringComparison.Ordinal)) return component;
            }

            return null;
        }

        static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("⛔ нет файла: " + path);
                return null;
            }

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
