using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace FsaClosureP191
{
    /// <summary>
    /// П191 (проверка 30.09.2026): ЗАМКНУТАЯ ПРОВЕРКА АМПЛИТУД FSA. Разбор спектра как есть даёт
    /// «истину» (модель и амплитуды образов); по истине × scale разыгрываются пуассоновские копии,
    /// каждая разбирается тем же составом и теми же умолчаниями. Печатается у каждого образа:
    /// среднее отношение амплитуды копии к истинной (смещение), разброс амплитуд копий против
    /// средней заявленной σ (A/z) и доля копий, где образ пропал (амплитуда 0 / отсев).
    /// Каркас (загрузка, копия, перевязка) — как у FsaReportWeightsProbe (S180/S182).
    ///
    ///   FsaClosureP191 --spectrum=файл.xml [--chain=Th-232] [--sample=137CS] [--scale=1,0.1]
    ///                  [--repeats=40] [--seed=20260930] [--matrix-any] [--set=Имя=значение]
    /// </summary>
    static class Program
    {
        static readonly List<KeyValuePair<System.Reflection.PropertyInfo, object>> sets =
            new List<KeyValuePair<System.Reflection.PropertyInfo, object>>();

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            // (`T243`) Эталон настроек — ДО разбора ключей (П196: проба П191 перенесена в дерево).
            FsaTuningReport.Snapshot();
            string spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            var scales = new List<double> { 1.0 };
            int seed = 20260930, repeats = 40;
            double sumWinLo = 0.0, sumWinHi = 0.0;
            // (П202, `AMBER151`) окно сличения данных с моделью: χ² и невязка по каналам окна
            double chiWinLo = 0.0, chiWinHi = 0.0;
            bool matrixAny = false;
            foreach (string a in args) { if (a == "--nobg") noBg = true; if (a == "--exact") exact = true; if (a == "--notes") notes = true; if (a == "--nomatrix") noMatrix = true; if (a == "--truth-nobg") truthNoBg = true; }
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--scale=", StringComparison.Ordinal))
                {
                    scales.Clear();
                    foreach (string t in a.Substring(8).Split(',')) scales.Add(double.Parse(t, CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--repeats=", StringComparison.Ordinal)) repeats = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--matrix-any") matrixAny = true;
                else if (a.StartsWith("--sumwin=", StringComparison.Ordinal))
                {
                    string[] t = a.Substring(9).Split(',');
                    sumWinLo = double.Parse(t[0], CultureInfo.InvariantCulture);
                    sumWinHi = double.Parse(t[1], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--chiwin=", StringComparison.Ordinal))
                {
                    string[] t = a.Substring(9).Split(',');
                    chiWinLo = double.Parse(t[0], CultureInfo.InvariantCulture);
                    chiWinHi = double.Parse(t[1], CultureInfo.InvariantCulture);
                }
                else if (a == "--nobg" || a == "--exact" || a == "--notes" || a == "--nomatrix" || a == "--truth-nobg") { }
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    string kv = a.Substring(6);
                    int eq = kv.IndexOf('=');
                    System.Reflection.PropertyInfo pi = eq > 0 ? typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq)) : null;
                    if (pi == null || !pi.CanWrite) { Console.Error.WriteLine("нет свойства анализатора: {0}", kv); return 2; }
                    sets.Add(new KeyValuePair<System.Reflection.PropertyInfo, object>(pi,
                        Convert.ChangeType(kv.Substring(eq + 1), pi.PropertyType, CultureInfo.InvariantCulture)));
                }
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }

            if (spectrumPath == null) { Console.Error.WriteLine("нужен --spectrum=<файл.xml>"); return 2; }
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null) { Console.Error.WriteLine("матрицы НЕТ ({0}, формат {1})", refusal, fileFormat); return 1; }
            bool stampOk = rd.Efficiency != null && rd.Efficiency.HasGeometry && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (!stampOk && !matrixAny) { Console.Error.WriteLine("ОТПЕЧАТОК НЕ СОШЁЛСЯ; осознанно — ключ --matrix-any"); return 1; }

            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
            string material = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency != null ? rd.Efficiency.Geometry : null);

            if (notes) FsaAnalyzer.ZeroTraceSink = line => Console.WriteLine("      [нуль] " + line);
            FsaResult truthResult = Run(rd, rd.EnergySpectrum, truthNoBg ? null : rd.BackgroundEnergySpectrum, spec, matrix, material);
            if (truthResult == null || truthResult.Model == null) { Console.Error.WriteLine("разбор не состоялся"); return 1; }
            int channels = rd.EnergySpectrum.NumberOfChannels;
            // Сырое ожидание канала: модель разбора + вычтенный фон (в шкале пробы). Копия разбирается
            // С ТЕМ ЖЕ фоном — тогда пол полосы, рампа порога (S204) и вычитание у копии те же, что у
            // истины, и разбор копии — та же задача. --nobg: старая схема (истина без фона, копия без фона).
            double[] truth = new double[channels];
            double band = 0.0;
            bool withBg = !noBg && truthResult.BackgroundUsed && truthResult.Background != null;
            for (int i = 0; i < channels; i++)
            {
                double m = i < truthResult.Model.Length ? truthResult.Model[i] : 0.0;
                band += Math.Max(m, 0.0);
                if (withBg && i < truthResult.Background.Length) m += truthResult.Background[i];
                truth[i] = Math.Max(m, 0.0);
            }

            Console.WriteLine("истина : LiveTime {0:F1} с, полоса {1}…{2}, модель {3:F0} отсч., chi2ndf {4:F3}, sigmaInflation {5:F3}, фон {6}",
                              truthResult.LiveTime, truthResult.FirstChannel, truthResult.LastChannel, band,
                              truthResult.Chi2Ndf, truthResult.SigmaInflation, truthResult.BackgroundUsed ? "вычтен" : "нет");
            PrintNotes("истина", truthResult);
            if (chiWinLo > 0.0 && chiWinHi > chiWinLo)
            {
                // (П202) Окно сличения: данные − вычтенный фон против модели разбора. Дисперсия канала —
                // сырой счёт плюс вычтенный фон (пуассон обоих; фон в шкале пробы — приближение), то есть
                // число годно для сравнения ПЛЕЧ одного спектра, а не как абсолютный χ² разбора.
                EnergyCalibration cw = rd.EnergySpectrum.EnergyCalibration;
                double d = 0.0, mo = 0.0, chi = 0.0, pos = 0.0, neg = 0.0;
                int n = 0, inBand = 0;
                for (int i = 0; i < channels; i++)
                {
                    double e = cw.ChannelToEnergy(i);
                    if (e < chiWinLo || e > chiWinHi) continue;
                    double raw = rd.EnergySpectrum.Spectrum[i];
                    double bg = truthResult.Background != null && i < truthResult.Background.Length ? truthResult.Background[i] : 0.0;
                    double m = i < truthResult.Model.Length ? truthResult.Model[i] : 0.0;
                    double r = raw - bg - m;
                    d += raw - bg;
                    mo += m;
                    chi += r * r / Math.Max(raw + Math.Max(bg, 0.0), 1.0);
                    if (r > 0) pos += r; else neg -= r;
                    n++;
                    if (i >= truthResult.FirstChannel && i <= truthResult.LastChannel) inBand++;
                }

                Console.WriteLine("окно сличения {0}…{1} кэВ: каналов {2} (в полосе разбора {3}), данные−фон {4:F1}, модель {5:F1}, (данные−модель)/модель {6:F4}, χ²/канал {7:F3}, невязка + {8:F1} / − {9:F1} отсч.",
                                  chiWinLo, chiWinHi, n, inBand, d, mo, mo > 0 ? (d - mo) / mo : double.NaN, n > 0 ? chi / n : double.NaN, pos, neg);
            }
            if (sumWinLo > 0.0 && sumWinHi > sumWinLo)
            {
                // окно сумм-пика: данные (сырое − вычтенный фон), модель, доля сумм-пиков образов
                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                double data = 0.0, model = 0.0, sumPeak = 0.0;
                for (int i = 0; i < channels; i++)
                {
                    double e = cal.ChannelToEnergy(i);
                    if (e < sumWinLo || e > sumWinHi) continue;
                    data += rd.EnergySpectrum.Spectrum[i] - (truthResult.Background != null && i < truthResult.Background.Length ? truthResult.Background[i] : 0.0);
                    model += i < truthResult.Model.Length ? truthResult.Model[i] : 0.0;
                    foreach (FsaComponentResult c in truthResult.Components)
                        if (c.SumPeakCurve != null && i < c.SumPeakCurve.Length) sumPeak += c.SumPeakCurve[i];
                }

                Console.WriteLine("окно {0}…{1} кэВ: данные−фон {2:F1}, модель {3:F1}, сумм-пики модели {4:F1}, (данные − (модель − сумм)) / сумм = {5:F3}; наложения: пар {6:G5}, граница {7:G5}, упёрлось {8}",
                                  sumWinLo, sumWinHi, data, model, sumPeak, sumPeak > 0.0 ? (data - (model - sumPeak)) / sumPeak : double.NaN,
                                  truthResult.PileUpUncappedPairs, truthResult.PileUpCapPairs, truthResult.PileUpNotPairs);
                foreach (FsaComponentResult c in truthResult.Components)
                {
                    double own = 0.0, ownSum = 0.0;
                    for (int i = 0; i < channels; i++)
                    {
                        double e = cal.ChannelToEnergy(i);
                        if (e < sumWinLo || e > sumWinHi) continue;
                        if (c.Curve != null && i < c.Curve.Length) own += c.Curve[i];
                        if (c.SumPeakCurve != null && i < c.SumPeakCurve.Length) ownSum += c.SumPeakCurve[i];
                    }

                    Console.WriteLine("   в окне {0,-12} лента {1,10:F1}  из них сумм-пики {2,10:F1}", c.Name, own, ownSum);
                }

                double cont = 0.0;
                for (int i = 0; i < channels; i++)
                {
                    double e = cal.ChannelToEnergy(i);
                    if (e < sumWinLo || e > sumWinHi) continue;
                    if (truthResult.Continuum != null && i < truthResult.Continuum.Length) cont += truthResult.Continuum[i];
                }

                Console.WriteLine("   в окне подложка (сплайн) {0:F1}", cont);
                if (notes)
                {
                    for (int i = 0; i < channels; i++)
                    {
                        double e = cal.ChannelToEnergy(i);
                        if (e < sumWinLo - 60.0 || e > sumWinHi + 60.0) continue;
                        double bg = truthResult.Background != null && i < truthResult.Background.Length ? truthResult.Background[i] : 0.0;
                        double sp = 0.0;
                        foreach (FsaComponentResult c in truthResult.Components) if (c.SumPeakCurve != null && i < c.SumPeakCurve.Length) sp += c.SumPeakCurve[i];
                        double pu = 0.0;
                        foreach (FsaComponentResult c in truthResult.Components) if (c.Name == "pile-up" && c.Curve != null && i < c.Curve.Length) pu = c.Curve[i];
                        Console.WriteLine("     кан {0,5} {1,8:F1} кэВ  данные {2,7}  фон {3,7:F1}  модель {4,8:F1}  сумм {5,7:F1}  сплайн {6,7:F1}  налож {7,8:F1}", i, e,
                                          rd.EnergySpectrum.Spectrum[i], bg, i < truthResult.Model.Length ? truthResult.Model[i] : 0.0, sp,
                                          truthResult.Continuum != null && i < truthResult.Continuum.Length ? truthResult.Continuum[i] : 0.0, pu);
                    }
                }
            }
            var truthAmp = new Dictionary<string, double>();
            foreach (FsaComponentResult c in truthResult.Components)
            {
                truthAmp[c.Name] = c.CountRate * truthResult.LiveTime;
                Console.WriteLine("  истина {0,-14} {1,-9} A={2,14:F2} z={3,8:F2} пик.отсч={4,12:F0} связка={5}",
                                  c.Name, c.Kind, c.CountRate * truthResult.LiveTime, c.Z, c.PeakCounts, c.ChainRoot ?? c.TiedTo ?? "—");
            }

            var rng = new Random(seed);
            foreach (double scale in scales)
            {
                var amps = new Dictionary<string, List<double>>();
                var sigs = new Dictionary<string, List<double>>();
                var infl = new List<double>();
                var chi = new List<double>();
                var gains = new List<double>();
                var absent = new Dictionary<string, int[]>();
                int done = 0;
                for (int r = 0; r < repeats; r++)
                {
                    FsaAnalyzer.ZeroTraceSink = notes && r == 0 ? (Action<string>)(line => Console.WriteLine("      [нуль копии] " + line)) : null;
                    EnergySpectrum copy = exact ? ExactCopy(rd.EnergySpectrum, truth, scale) : PoissonCopy(rd.EnergySpectrum, truth, scale, rng);
                    ResultData copyData = Rewrap(rd, copy, scale);
                    FsaResult res = Run(copyData, copy, withBg ? rd.BackgroundEnergySpectrum : null, spec, matrix, material);
                    if (res == null) { if (notes) Console.WriteLine("  [копия {0}] отказ {1}", r, lastRefusal); continue; }
                    if (notes && r < 2) PrintNotes("копия " + r, res);
                    if (notes && r == 0 && sumWinLo > 0.0 && sumWinHi > sumWinLo)
                    {
                        // окно копии: её данные против её модели, тяга канала по σ = sqrt(модель + фон)
                        EnergyCalibration cal = copy.EnergyCalibration;
                        double chiWin = 0.0; int nWin = 0;
                        for (int i = 0; i < channels; i++)
                        {
                            double e = cal.ChannelToEnergy(i);
                            if (e < sumWinLo - 60.0 || e > sumWinHi + 60.0) continue;
                            double bg = res.Background != null && i < res.Background.Length ? res.Background[i] : 0.0;
                            double m = i < res.Model.Length ? res.Model[i] : 0.0;
                            double d = copy.Spectrum[i] - bg;
                            double pull = (d - m) / Math.Sqrt(Math.Max(m + bg, 1.0));
                            chiWin += pull * pull; nWin++;
                            Console.WriteLine("     копия кан {0,5} {1,8:F1} кэВ  данные−фон {2,10:F1}  модель {3,10:F1}  разн {4,9:F1}  тяга {5,7:F2}", i, e, d, m, d - m, pull);
                        }

                        Console.WriteLine("     копия окно ±60: chi2 {0:F1} на {1} кан", chiWin, nWin);
                    }
                    done++;
                    infl.Add(res.SigmaInflation);
                    chi.Add(res.Chi2Ndf);
                    gains.Add(res.Gain);
                    var seen = new HashSet<string>();
                    // пределы S9: у кандидатов, которых в истине нет, — доля копий «обнаружен» и «выше a*»
                    if (res.CharacteristicLimits != null)
                    {
                        foreach (FsaCharacteristicLimit lim in res.CharacteristicLimits)
                        {
                            if (truthAmp.ContainsKey(lim.Name)) continue;
                            int[] tally;
                            if (!absent.TryGetValue(lim.Name, out tally)) { tally = new int[4]; absent[lim.Name] = tally; }
                            tally[0]++;
                            if (lim.Detected) tally[1]++;
                            if (lim.CountRate > lim.DecisionThresholdRate) tally[2]++;
                            if (lim.CountRate > lim.DetectionLimitRate) tally[3]++;
                        }
                    }

                    foreach (FsaComponentResult c in res.Components)
                    {
                        if (!truthAmp.ContainsKey(c.Name)) continue;
                        seen.Add(c.Name);
                        double amp = c.CountRate * res.LiveTime;
                        List<double> la, ls;
                        if (!amps.TryGetValue(c.Name, out la)) { la = new List<double>(); amps[c.Name] = la; sigs[c.Name] = new List<double>(); }
                        ls = sigs[c.Name];
                        la.Add(amp);
                        ls.Add(c.Z > 0.0 ? amp / c.Z : double.NaN);
                    }
                }

                Console.WriteLine();
                Console.WriteLine("=== scale {0}{7} : копий {1} из {2}; chi2ndf решателя {3:F3}, sigmaInflation {4:F3}, gain {5:F5} ± {6:F5} ===",
                                  scale, done, repeats, Mean(chi), Mean(infl), Mean(gains), Std(gains), (exact ? " (точная копия)" : "") + (withBg ? " с фоном" : " без фона"));
                Console.WriteLine("{0,-14} {1,6} {2,10} {3,10} {4,10} {5,10} {6,10} {7,8}",
                                  "образ", "копий", "смещ., %", "±ош.ср,%", "разброс,%", "σ заявл,%", "разбр/σ", "ср.тяга");
                foreach (KeyValuePair<string, double> t in truthAmp)
                {
                    List<double> la;
                    if (!amps.TryGetValue(t.Key, out la) || la.Count == 0 || !(t.Value > 0.0))
                    {
                        Console.WriteLine("{0,-14} {1,6}  — образ пропал во всех копиях", t.Key, 0);
                        continue;
                    }

                    double expect = t.Value * scale;
                    double mean = Mean(la), sd = Std(la);
                    var ls = sigs[t.Key].Where(v => !double.IsNaN(v)).ToList();
                    double sig = ls.Count > 0 ? Mean(ls) : double.NaN;
                    var pulls = new List<double>();
                    for (int i = 0; i < la.Count; i++)
                    {
                        double s = sigs[t.Key][i];
                        if (s > 0.0) pulls.Add((la[i] - expect) / s);
                    }

                    Console.WriteLine("{0,-14} {1,6} {2,10:F3} {3,10:F3} {4,10:F3} {5,10:F3} {6,10:F3} {7,8:F2}",
                                      t.Key, la.Count, 100.0 * (mean / expect - 1.0), 100.0 * sd / expect / Math.Sqrt(la.Count),
                                      100.0 * sd / expect, 100.0 * sig / expect, sd / sig, pulls.Count > 0 ? Mean(pulls) : double.NaN);
                }

                foreach (KeyValuePair<string, int[]> a in absent)
                {
                    Console.WriteLine("  отсутствующий {0,-12}: копий {1}, «обнаружен» {2} ({3:F1} %), выше a* {4} ({5:F1} %), выше a# {6} ({7:F1} %)",
                                      a.Key, a.Value[0], a.Value[1], 100.0 * a.Value[1] / Math.Max(1, a.Value[0]),
                                      a.Value[2], 100.0 * a.Value[2] / Math.Max(1, a.Value[0]), a.Value[3], 100.0 * a.Value[3] / Math.Max(1, a.Value[0]));
                }
            }

            return 0;
        }

        static bool noBg, exact, notes, noMatrix, effDumped, truthNoBg, tuningPrinted;
        static string lastBand, lastRefusal;

        static void PrintNotes(string who, FsaResult r)
        {
            Console.WriteLine("  [{0}] gain {1:F5} сдвиг {2:F3} кан; chi2ndf {3:F3}; {4}", who, r.Gain, r.OffsetChannels, r.Chi2Ndf, r.AnchorNote ?? "—");
            Console.WriteLine("  [{0}] {1}", who, lastBand ?? "—");
            if (r.ScaleAnchors != null)
            {
                foreach (FsaScaleAnchor a in r.ScaleAnchors)
                {
                    Console.WriteLine("  [{0}]   опора {1,-8} {2,8:F2} кэВ  модель {3,8:F3}  данные {4,8:F3}  сдвиг {5,7:F3} кэВ  σ {6,6:F3}  доля {7:F3}  z {8,7:F1}  окно {9}…{10}  {11}",
                                      who, a.Component, a.LineKev, a.ModelKev, a.MeasuredKev, a.ShiftKev, a.SigmaKev, a.PeakShare, a.Z,
                                      a.FirstChannel, a.LastChannel, a.Used ? "ВЗЯТА" : (a.Refusal ?? "—"));
                }
            }
        }

        static EnergySpectrum ExactCopy(EnergySpectrum source, double[] truth, double scale)
        {
            int channels = source.NumberOfChannels;
            int[] counts = new int[channels];
            long total = 0;
            for (int i = 0; i < channels; i++)
            {
                counts[i] = (int)Math.Round(Math.Max(truth[i] * scale, 0.0));
                total += counts[i];
            }

            return new EnergySpectrum
            {
                NumberOfChannels = channels, Spectrum = counts, EnergyCalibration = source.EnergyCalibration,
                TotalPulseCount = total, ValidPulseCount = total, MeasurementTime = source.MeasurementTime,
                ChannelPitch = source.ChannelPitch
            };
        }

        static double Mean(List<double> v) { return v.Count == 0 ? double.NaN : v.Average(); }

        static double Std(List<double> v)
        {
            if (v.Count < 2) return double.NaN;
            double m = v.Average();
            return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
        }

        static FsaResult Run(ResultData rd, EnergySpectrum spectrum, EnergySpectrum background, FsaSampleSpec spec,
                             ResponseMatrix matrix, string material)
        {
            // Матрица — ТЕМ ЖЕ движением, что приложение и корпус (FsaMatrixBinding.Bind): с нею едут вещество
            // кристалла, таблица Q_k угловых корреляций и обстановка (домик). Прямое `ResponseMatrix = matrix`
            // (как у FsaReportWeightsProbe) оставляло Q_k = null — корреляции в сумм-пиках молча выключены.
            var analyzer = new FsaAnalyzer();
            if (!noMatrix) FsaMatrixBinding.Bind(analyzer, rd.Efficiency != null ? rd.Efficiency.Geometry : null, matrix);
            foreach (var kv in sets) kv.Key.SetValue(analyzer, kv.Value, null);
            if (!tuningPrinted)
            {
                FsaTuningReport.Print(analyzer);
                tuningPrinted = true;
            }
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            FWHMPeakDetectionMethodConfig peakConfig = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (peakConfig != null)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            FsaEfficiency eff = FsaEfficiency.FromConfig(rd.Efficiency);
            if (notes && !effDumped && eff != null)
            {
                // кривая эффективности пробы против пиковой эффективности матрицы по линиям библиотеки
                effDumped = true;
                FsaCascadeSummer summer = matrix != null ? FsaCascadeSummer.Create(matrix, material) : null;
                Console.WriteLine("  [кривая] {0}; на единичный флюенс: {1}", rd.Efficiency != null ? rd.Efficiency.Name : "—", eff.IsPerUnitFluence);
                foreach (FsaComponent c in library)
                {
                    if (c.Lines == null) continue;
                    foreach (FsaLine ln in c.Lines)
                    {
                        if (ln.Intensity < 1.0) continue;
                        double ec = eff.Eval(ln.Energy);
                        double ep = summer != null ? summer.PeakEfficiency(ln.Energy) : double.NaN;
                        Console.WriteLine("  [кривая] {0,-10} {1,9:F2} кэВ I {2,7:F3} %  кривая {3:E4}  матрица ε_p {4:E4}  кривая/матрица {5:F3}", c.Name, ln.Energy, ln.Intensity, ec, ep, ep > 0 ? ec / ep : double.NaN);
                    }
                }
            }

            FsaResult result = analyzer.Analyze(spectrum, background, rd.FwhmCalibration, library, eff);
            lastBand = analyzer.BandNote;
            lastRefusal = result == null ? analyzer.Refusal + ": " + analyzer.RefusalNote : null;
            return result;
        }

        static EnergySpectrum PoissonCopy(EnergySpectrum source, double[] truth, double scale, Random rng)
        {
            int channels = source.NumberOfChannels;
            int[] counts = new int[channels];
            long total = 0;
            for (int i = 0; i < channels; i++)
            {
                int k = Poisson(truth[i] * scale, rng);
                counts[i] = k;
                total += k;
            }

            return new EnergySpectrum
            {
                NumberOfChannels = channels,
                Spectrum = counts,
                EnergyCalibration = source.EnergyCalibration,
                TotalPulseCount = total,
                ValidPulseCount = total,
                MeasurementTime = source.MeasurementTime,
                ChannelPitch = source.ChannelPitch
            };
        }

        // Пуассон: до 50 — Кнут в логарифмах, выше — нормальное приближение с поправкой на целое
        // (копии богатых спектров: миллионы отсчётов в канале Кнутом считались бы минуты).
        static int Poisson(double mu, Random rng)
        {
            if (!(mu > 0.0)) return 0;
            if (mu > 50.0)
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                double v = mu + Math.Sqrt(mu) * g;
                return v <= 0.0 ? 0 : (int)Math.Floor(v + 0.5);
            }

            double target = -mu, sum = 0.0;
            int k = 0;
            while (true)
            {
                double u = rng.NextDouble();
                if (u <= 0.0) u = double.Epsilon;
                sum += Math.Log(u);
                if (sum <= target) return k;
                k++;
            }
        }

        static ResultData Rewrap(ResultData source, EnergySpectrum copy, double scale)
        {
            var rd = new ResultData
            {
                EnergySpectrum = copy,
                BackgroundEnergySpectrum = null,
                FwhmCalibration = source.FwhmCalibration,
                Efficiency = source.Efficiency,
                DeviceConfig = source.DeviceConfig,
                DeviceConfigReference = source.DeviceConfigReference,
                PeakDetectionMethodConfig = source.PeakDetectionMethodConfig
            };
            // Живое время — дробным числом: округление MeasurementTime до целых секунд на малом масштабе
            // сбивало бы нормировку фона (3434.8 с × 0.01 = 34.348 → 34 с, −1 %).
            copy.MeasurementTime = (int)Math.Max(1.0, Math.Round(source.EnergySpectrum.MeasurementTime * scale));
            copy.LiveTime = source.EnergySpectrum.EffectiveLiveTime * scale;
            return rd;
        }

        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                if (label.Length == 0) continue;
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0 ? label.ToUpperInvariant() : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
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

            FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (rd.FwhmCalibration == null && cfg != null)
            {
                rd.FwhmCalibration = cfg.FwhmCalibration;
            }

            return rd;
        }
    }
}
