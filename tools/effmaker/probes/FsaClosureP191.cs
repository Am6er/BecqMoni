using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
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
        static readonly List<KeyValuePair<System.Reflection.FieldInfo, object>> fieldSets =
            new List<KeyValuePair<System.Reflection.FieldInfo, object>>();
        static readonly List<KeyValuePair<System.Reflection.PropertyInfo, object>> copySets =
            new List<KeyValuePair<System.Reflection.PropertyInfo, object>>();
        static readonly List<KeyValuePair<System.Reflection.FieldInfo, object>> copyFieldSets =
            new List<KeyValuePair<System.Reflection.FieldInfo, object>>();
        static bool runningCopy;

        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            var scales = new List<double> { 1.0 };
            int seed = 20260930, repeats = 40;
            double sumWinLo = 0.0, sumWinHi = 0.0;
            bool matrixAny = false;
            foreach (string a in args) { if (a == "--nobg") noBg = true; if (a == "--exact") exact = true; if (a == "--notes") notes = true; if (a == "--nomatrix") noMatrix = true; if (a == "--truth-nobg") truthNoBg = true;
                if (a.StartsWith("--dgain=")) driftGain = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                if (a.StartsWith("--doff=")) driftOff = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                if (a == "--lie") lie = true;
                if (a.StartsWith("--rebin=")) rebin = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                if (a == "--decimate") decimate = true;
                if (a == "--fwhm-binning") fwhmBinning = true;
                if (a.StartsWith("--copy-decimate=")) copyDecimate = int.Parse(a.Substring(16), CultureInfo.InvariantCulture);
                if (a == "--ramp=off") FsaBand.DefaultRampModel = FsaRampModel.Off;
                if (a.StartsWith("--band=")) FsaBand.DefaultMode = (FsaBandMode)Enum.Parse(typeof(FsaBandMode), a.Substring(7), true);
                if (a.StartsWith("--curve-max=")) curveMax = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                if (a == "--fwhm-follow") fwhmFollow = true;
                if (a.StartsWith("--fwhm-scale=")) fwhmScale = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                if (a.StartsWith("--truth-fwhm=")) truthFwhm = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                if (a.StartsWith("--bgdrift=")) bgDrift = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                if (a == "--nnls") nnlsTrace = true; if (a == "--bgtrace") FsaAnalyzer.BackgroundGainTraceSink = line => Console.WriteLine("      [фон] " + line);
                if (a.StartsWith("--floor=")) floorSpec = a.Substring(8);
                if (a.StartsWith("--dfrom=")) driftFrom = int.Parse(a.Substring(8), CultureInfo.InvariantCulture); }
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
                else if (a == "--nobg" || a == "--exact" || a == "--notes" || a == "--nomatrix" || a == "--truth-nobg" || a.StartsWith("--dgain=") || a.StartsWith("--doff=") || a == "--lie" || a.StartsWith("--dfrom=") || a == "--fwhm-follow" || a.StartsWith("--fwhm-scale=") || a.StartsWith("--truth-fwhm=") || a.StartsWith("--bgdrift=") || a == "--nnls" || a == "--bgtrace" || a.StartsWith("--floor=") || a.StartsWith("--curve-max=") || a.StartsWith("--rebin=") || a == "--decimate" || a == "--fwhm-binning" || a.StartsWith("--copy-decimate=") || a == "--ramp=off" || a.StartsWith("--band=")) { }
                else if (a.StartsWith("--set=", StringComparison.Ordinal) || a.StartsWith("--copy-set=", StringComparison.Ordinal))
                {
                    // П198 --copy-set=: та же настройка, но ТОЛЬКО разбору копий (истина — умолчаниями)
                    bool copyOnly = a.StartsWith("--copy-set=", StringComparison.Ordinal);
                    string kv = a.Substring(copyOnly ? 11 : 6);
                    int eq = kv.IndexOf('=');
                    System.Reflection.PropertyInfo pi = eq > 0 ? typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq)) : null;
                    System.Reflection.FieldInfo fi = eq > 0 && pi == null ? typeof(FsaAnalyzer).GetField(kv.Substring(0, eq)) : null;
                    if ((pi == null || !pi.CanWrite) && fi == null) { Console.Error.WriteLine("нет свойства анализатора: {0}", kv); return 2; }
                    if (pi != null)
                        (copyOnly ? copySets : sets).Add(new KeyValuePair<System.Reflection.PropertyInfo, object>(pi,
                            pi.PropertyType.IsEnum ? Enum.Parse(pi.PropertyType, kv.Substring(eq + 1), true)
                                                   : Convert.ChangeType(kv.Substring(eq + 1), pi.PropertyType, CultureInfo.InvariantCulture)));
                    else
                        (copyOnly ? copyFieldSets : fieldSets).Add(new KeyValuePair<System.Reflection.FieldInfo, object>(fi,
                            fi.FieldType.IsEnum ? Enum.Parse(fi.FieldType, kv.Substring(eq + 1), true)
                                                : Convert.ChangeType(kv.Substring(eq + 1), fi.FieldType, CultureInfo.InvariantCulture)));
                }
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }

            if (spectrumPath == null) { Console.Error.WriteLine("нужен --spectrum=<файл.xml>"); return 2; }
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            if (floorSpec != null)
            {
                string mode = floorSpec; double kev = double.NaN;
                int colon = floorSpec.IndexOf(':');
                if (colon > 0) { mode = floorSpec.Substring(0, colon); kev = double.Parse(floorSpec.Substring(colon + 1), CultureInfo.InvariantCulture); }
                FsaFitFloor f = (FsaFitFloor)Enum.Parse(typeof(FsaFitFloor), mode, true);
                FsaBand.DefaultFitFloor = f;
                if (!double.IsNaN(kev)) FsaBand.DefaultFitFloorKev = kev;
                Console.WriteLine("пол полосы фита: {0}{1}", f, double.IsNaN(kev) ? "" : " " + kev.ToString("F2", CultureInfo.InvariantCulture) + " кэВ");
            }

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

            if (rebin > 1) RebinAll(rd, rebin);
            if (bgDrift != 1.0 && rd.BackgroundEnergySpectrum != null && rd.BackgroundEnergySpectrum.Spectrum != null)
            {
                EnergySpectrum bg = rd.BackgroundEnergySpectrum;
                var target = new DriftedCalibration(bg.EnergyCalibration, bgDrift, 0.0);
                double[] moved = SpectrumAriphmetics.RebinByEnergy(bg.Spectrum, bg.EnergyCalibration, target, bg.NumberOfChannels);
                int[] counts = new int[bg.NumberOfChannels]; long tot = 0;
                for (int i = 0; i < counts.Length; i++) { counts[i] = (int)Math.Round(moved[i]); tot += counts[i]; }
                bg.Spectrum = counts; bg.TotalPulseCount = tot; bg.ValidPulseCount = tot;
                Console.WriteLine("хранимый фон перебинирован на усиление {0} (сумма {1})", bgDrift.ToString("F5", CultureInfo.InvariantCulture), tot);
            }
            if (truthFwhm != 1.0)
            {
                var pf0 = rd.FwhmCalibration as PowerFwhmCalibration;
                if (pf0 == null) throw new InvalidOperationException("--truth-fwhm: нужна PowerFwhmCalibration");
                var scaled = (PowerFwhmCalibration)pf0.Clone();
                double[] c0 = (double[])scaled.Coefficients.Clone(); c0[0] *= truthFwhm; scaled.Coefficients = c0;
                rd.FwhmCalibration = scaled;
                Console.WriteLine("калибровка ПШПВ спектра умножена на {0}", truthFwhm.ToString("F4", CultureInfo.InvariantCulture));
            }
            if (curveMax > 0.0 && rd.Efficiency != null && rd.Efficiency.Curve != null)
            {
                // П191: кривая эффективности обрезана сверху — линии выше её верха получают Eval = ε(верх) (зажим)
                int before = rd.Efficiency.Curve.Count;
                rd.Efficiency.Curve.RemoveAll(pt => pt.Energy > curveMax);
                var effCut = FsaEfficiency.FromConfig(rd.Efficiency);
                Console.WriteLine("кривая эффективности обрезана выше {0} кэВ: точек {1} → {2}; MaxEnergy {3}; Eval(2614.5) = {4:E4}, TryEval(2614.5) = {5}",
                                  curveMax.ToString("F1", CultureInfo.InvariantCulture), before, rd.Efficiency.Curve.Count,
                                  effCut != null ? effCut.MaxEnergy.ToString("F1", CultureInfo.InvariantCulture) : "—",
                                  effCut != null ? effCut.Eval(2614.5) : double.NaN,
                                  effCut != null && effCut.TryEval(2614.5, out double eTry, out double errTry) ? eTry.ToString("E4", CultureInfo.InvariantCulture) : "отказ");
            }
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
            if (notes)
            {
                // χ² истины по блокам 100 кэВ: (данные − фон − модель)²/max(модель + фон, 1) — где сидит невязка настоящего спектра
                EnergyCalibration calT = rd.EnergySpectrum.EnergyCalibration;
                var blocksT = new SortedDictionary<int, double[]>();
                for (int i = 0; i < channels; i++)
                {
                    if (i < truthResult.FirstChannel || i > truthResult.LastChannel) continue;
                    double e = calT.ChannelToEnergy(i);
                    double bg = truthResult.Background != null && i < truthResult.Background.Length ? truthResult.Background[i] : 0.0;
                    double m = i < truthResult.Model.Length ? truthResult.Model[i] : 0.0;
                    double d = rd.EnergySpectrum.Spectrum[i] - bg;
                    int blk = (int)Math.Floor(e / 100.0);
                    double[] acc; if (!blocksT.TryGetValue(blk, out acc)) { acc = new double[2]; blocksT[blk] = acc; }
                    acc[0] += (d - m) * (d - m) / Math.Max(m + bg, 1.0); acc[1] += 1.0;
                }
                var sbT = new StringBuilder("  [истина] χ² по блокам 100 кэВ (блок: χ²/каналов):");
                foreach (var kv in blocksT) sbT.AppendFormat(" {0}: {1:F0}/{2:F0};", kv.Key * 100, kv.Value[0], kv.Value[1]);
                Console.WriteLine(sbT.ToString());
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
                        // П208: и ленты образов истины (как у копии) — сверка образа укрупнённой копии с истиной
                        var partsT = new StringBuilder();
                        foreach (FsaComponentResult c in truthResult.Components)
                        {
                            double cv = c.Curve != null && i < c.Curve.Length ? c.Curve[i] : 0.0;
                            double tv = c.TailCurve != null && i < c.TailCurve.Length ? c.TailCurve[i] : 0.0;
                            if (Math.Abs(cv) + Math.Abs(tv) > 0.05) partsT.AppendFormat(CultureInfo.InvariantCulture, "  {0} {1:F1}{2}", c.Name, cv, tv != 0.0 ? "+хвост " + tv.ToString("F1", CultureInfo.InvariantCulture) : "");
                        }
                        Console.WriteLine("     кан {0,5} {1,8:F1} кэВ  данные {2,7}  фон {3,7:F1}  модель {4,8:F1}  сумм {5,7:F1}  сплайн {6,7:F1}  налож {7,8:F1}{8}", i, e,
                                          rd.EnergySpectrum.Spectrum[i], bg, i < truthResult.Model.Length ? truthResult.Model[i] : 0.0, sp,
                                          truthResult.Continuum != null && i < truthResult.Continuum.Length ? truthResult.Continuum[i] : 0.0, pu, partsT.ToString());
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
                var gains = new List<double>(); var offs = new List<double>();
                var absent = new Dictionary<string, int[]>();
                int done = 0;
                for (int r = 0; r < repeats; r++)
                {
                    FsaAnalyzer.ZeroTraceSink = notes && r == 0 ? (Action<string>)(line => Console.WriteLine("      [нуль копии] " + line)) : null;
                    double[] truthUsed = lie ? truth : Drift(truth, rd.EnergySpectrum.EnergyCalibration, channels);
                    EnergySpectrum copy = exact ? ExactCopy(rd.EnergySpectrum, truthUsed, scale) : PoissonCopy(rd.EnergySpectrum, truthUsed, scale, rng);
                    if (lie && (driftGain != 1.0 || driftOff != 0.0)) copy.EnergyCalibration = LieCalibration(rd.EnergySpectrum.EnergyCalibration);
                    ResultData copyData = Rewrap(rd, copy, scale);
                    if (fwhmScale != 1.0)
                    {
                        var pf = rd.FwhmCalibration as PowerFwhmCalibration;
                        if (pf == null) throw new InvalidOperationException("--fwhm-scale: нужна PowerFwhmCalibration");
                        var wide = (PowerFwhmCalibration)pf.Clone();
                        double[] c = (double[])wide.Coefficients.Clone(); c[0] *= fwhmScale; wide.Coefficients = c;
                        copyData.FwhmCalibration = wide;
                    }
                    if (fwhmFollow && driftOff != 0.0)
                    {
                        var pf = rd.FwhmCalibration as PowerFwhmCalibration;
                        if (pf == null) throw new InvalidOperationException("--fwhm-follow: нужна PowerFwhmCalibration");
                        copyData.FwhmCalibration = new ShiftedPowerFwhm(pf, driftOff);
                    }
                    int nnlsCall = 0;
                    if (nnlsTrace && r == 0)
                        FsaAnalyzer.NnlsTraceSink = t =>
                        {
                            nnlsCall++;
                            int m = t.X.Length, nAct = 0, nBan = 0, nPos = 0;
                            for (int k = 0; k < m; k++) { if (t.Active[k]) nAct++; if (t.Banned[k]) nBan++; if (t.X[k] > 0) nPos++; }
                            var top = Enumerable.Range(0, m).OrderByDescending(k => Math.Abs(t.C[k])).Take(4).Select(k => string.Format(CultureInfo.InvariantCulture, "k{0}:c={1:E2},x={2:E2},{3}{4}", k, t.C[k], t.X[k], t.Active[k] ? "A" : "-", t.Banned[k] ? "B" : ""));
                            Console.WriteLine("      [nnls {0}] m={1} iter={2}/{3} drops={4} active={5} banned={6} x>0={7} tol={8:E2}; крупнейшие |c|: {9}", nnlsCall, m, t.Iterations, t.Budget, t.Drops, nAct, nBan, nPos, t.Tol, string.Join(" ", top));
                        };
                    EnergySpectrum bgUse = withBg ? rd.BackgroundEnergySpectrum : null;
                    if (copyDecimate > 1)
                    {
                        copy = DecimateSpectrum(copy, copyDecimate, "копия");
                        copyData.EnergySpectrum = copy;
                        copy.LiveTime = rd.EnergySpectrum.EffectiveLiveTime * scale;
                        if (bgUse != null) bgUse = DecimateSpectrum(bgUse, copyDecimate, "фон копии");
                        var pfc = copyData.FwhmCalibration as PowerFwhmCalibration;
                        if (pfc == null) throw new InvalidOperationException("--copy-decimate: нужна PowerFwhmCalibration");
                        copyData.FwhmCalibration = new DecimatedPowerFwhm(pfc, copyDecimate);
                    }
                    runningCopy = true;
                    FsaResult res = Run(copyData, copy, bgUse, spec, matrix, material);
                    runningCopy = false;
                    FsaAnalyzer.NnlsTraceSink = null;
                    if (res == null) { if (notes) Console.WriteLine("  [копия {0}] отказ {1}", r, lastRefusal); continue; }
                    if (notes && r < 2) PrintNotes("копия " + r, res);
                    if (notes && r == 0)
                    {
                        // χ² копии по блокам 100 кэВ: (данные − модель)²/max(модель + фон, 1)
                        EnergyCalibration calB = copy.EnergyCalibration;
                        var blocks = new SortedDictionary<int, double[]>();
                        for (int i = 0; i < copy.NumberOfChannels; i++)
                        {
                            double e = calB.ChannelToEnergy(i);
                            if (i < res.FirstChannel || i > res.LastChannel) continue;
                            double bg = res.Background != null && i < res.Background.Length ? res.Background[i] : 0.0;
                            double m = i < res.Model.Length ? res.Model[i] : 0.0;
                            double d = copy.Spectrum[i] - bg;
                            int blk = (int)Math.Floor(e / 100.0);
                            double[] acc; if (!blocks.TryGetValue(blk, out acc)) { acc = new double[2]; blocks[blk] = acc; }
                            acc[0] += (d - m) * (d - m) / Math.Max(m + bg, 1.0); acc[1] += 1.0;
                        }
                        var sb = new StringBuilder("  [копия 0] χ² по блокам 100 кэВ (блок: χ²/каналов):");
                        foreach (var kv in blocks) sb.AppendFormat(" {0}: {1:F0}/{2:F0};", kv.Key * 100, kv.Value[0], kv.Value[1]);
                        Console.WriteLine(sb.ToString());
                    }
                    if (notes && r == 0 && sumWinLo > 0.0 && sumWinHi > sumWinLo)
                    {
                        // окно копии: её данные против её модели, тяга канала по σ = sqrt(модель + фон)
                        EnergyCalibration cal = copy.EnergyCalibration;
                        double chiWin = 0.0; int nWin = 0;
                        for (int i = 0; i < copy.NumberOfChannels; i++)
                        {
                            double e = cal.ChannelToEnergy(i);
                            if (e < sumWinLo - 60.0 || e > sumWinHi + 60.0) continue;
                            double bg = res.Background != null && i < res.Background.Length ? res.Background[i] : 0.0;
                            double m = i < res.Model.Length ? res.Model[i] : 0.0;
                            double d = copy.Spectrum[i] - bg;
                            double pull = (d - m) / Math.Sqrt(Math.Max(m + bg, 1.0));
                            chiWin += pull * pull; nWin++;
                            var parts = new StringBuilder();
                            foreach (FsaComponentResult c in res.Components)
                            {
                                double cv = c.Curve != null && i < c.Curve.Length ? c.Curve[i] : 0.0;
                                double tv = c.TailCurve != null && i < c.TailCurve.Length ? c.TailCurve[i] : 0.0;
                                if (Math.Abs(cv) + Math.Abs(tv) > 0.05) parts.AppendFormat("  {0} {1:F1}{2}", c.Name, cv, tv != 0.0 ? "+хвост " + tv.ToString("F1", CultureInfo.InvariantCulture) : "");
                            }
                            Console.WriteLine("     копия кан {0,5} {1,8:F1} кэВ  данные−фон {2,10:F1}  модель {3,10:F1}  разн {4,9:F1}  тяга {5,7:F2}  сплайн {6,8:F1}{7}", i, e, d, m, d - m, pull,
                                              res.Continuum != null && i < res.Continuum.Length ? res.Continuum[i] : 0.0, parts.ToString());
                        }

                        Console.WriteLine("     копия окно ±60: chi2 {0:F1} на {1} кан", chiWin, nWin);
                    }
                    done++;
                    infl.Add(res.SigmaInflation);
                    chi.Add(res.Chi2Ndf);
                    gains.Add(res.Gain);
                    offs.Add(res.OffsetChannels);
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
                Console.WriteLine("=== scale {0}{7} : копий {1} из {2}; chi2ndf решателя {3:F3}, sigmaInflation {4:F3}, gain {5:F5} ± {6:F5}, сдвиг {8:F3} ± {9:F3} кан; задано дрейфом g {10:F5} off {11:F3} ===",
                                  scale, done, repeats, Mean(chi), Mean(infl), Mean(gains), Std(gains), (exact ? " (точная копия)" : "") + (withBg ? " с фоном" : " без фона"), Mean(offs), Std(offs), driftGain, driftOff);
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

        static bool noBg, exact, notes, noMatrix, effDumped, truthNoBg;
        static bool tuningPrinted;
        static double curveMax = 0.0;   // --curve-max=E: снять точки кривой эффективности выше E кэВ (зажим Eval за верхом)
        static double driftGain = 1.0, driftOff = 0.0;
        static bool lie, fwhmFollow;
        static double fwhmScale = 1.0;   // калибровка ПШПВ у копии шире (>1) или уже (<1) той, которой построена истина
        static double truthFwhm = 1.0;   // множитель к калибровке ПШПВ САМОГО спектра перед разбором истины (развёртка ширины по настоящим данным)
        static double bgDrift = 1.0;
        static int rebin = 1;   // --rebin=k: спектр, фон, калибровки — ×k грубее (инвариантность к дискретизации)
        // П198 --decimate (вместе с --rebin=k): ТОЧНОЕ укрупнение — канал m = сумма каналов k·m … k·m+k−1 без деления
        // отсчётов, E'(m) = E(k·m + (k−1)/2), ПШПВ'(m) = ПШПВ(k·m + (k−1)/2)/k. Перекладка RebinByEnergy (центры каналов)
        // делит крайние старые каналы пополам и добавляет данным дисперсию 1/12 старого канала² — у пика в 1.3 нового
        // канала это +3 % ширины, то есть сама проверка «×2» была не нейтральна к дискретизации (`AMBER158`, П198).
        static bool decimate;
        // П208 --fwhm-binning: у --decimate / --copy-decimate калибровка ПШПВ укрупнённого спектра — наблюдаемая ширина с ящиком
        // нового канала (см. DecimatedPowerFwhm), а не прежняя ширина / k (`S207`)
        static bool fwhmBinning;
        // П198 --copy-decimate=k: истина — в родных каналах, а КОПИЯ (с фоном и калибровками) укрупняется точно ×k перед
        // разбором. Так мерится инвариантность самого разбора к числу каналов на модели, которая описывает данные точно
        // (у настоящего спектра χ²/ndf ≫ 1, и любая перестановка весов двигает амплитуды — это уже не дискретизация).
        static int copyDecimate = 1;
        static bool nnlsTrace;
        static string floorSpec;         // пол полосы фита: off | adc | threshold | fixed:<кэВ> — статики FsaBand.DefaultFitFloor/DefaultFitFloorKev           // печать трассы решателя NNLS (A308-прибор): вызов, итерации, активные/забаненные колонки, крупнейшие x     // перебинировать ХРАНИМЫЙ ФОН на усиление g перед разбором истины — имитация «фон вычитается в истинной шкале» (AMBER154)

        // Ширина, следующая за сдвигом: у копии ПШПВ(канал) = ПШПВ_исходной(канал − off) — то, что физически верно
        // при дрейфе нуля (ширина линии в каналах не меняется, меняется только её место).
        sealed class ShiftedPowerFwhm : PowerFwhmCalibration
        {
            readonly PowerFwhmCalibration inner; readonly double off;
            public ShiftedPowerFwhm(PowerFwhmCalibration inner, double off) { this.inner = inner; this.off = off; this.Coefficients = (double[])inner.Coefficients.Clone(); }
            public override double ChannelToFwhm(double channel) { return this.inner.ChannelToFwhm(channel - this.off); }
        }
        static int driftFrom = 0;   // сдвигать только каналы истины ≥ этого (край/рампа остаются на месте)

        // «Ложь о калибровке» — дрейф без перекладки отсчётов: данные копии те же, а объявленная калибровка копии
        // E_lie(n) = P(g·n + off) — то есть по ней линия E стоит в канале (ch_d(E) − off)/g, тогда как в данных она
        // на ch_d(E): разбор обязан найти усиление g и сдвиг off (сверх собственного дрейфа истины). Полином ≤ 3 — точно.
        static EnergyCalibration LieCalibration(EnergyCalibration declared)
        {
            var poly = declared as PolynomialEnergyCalibration;
            if (poly == null || poly.Coefficients.Length > 4) throw new InvalidOperationException("--lie: нужна полиномиальная калибровка порядка ≤ 3");
            double[] c = new double[4];
            for (int i = 0; i < poly.Coefficients.Length; i++) c[i] = poly.Coefficients[i];
            double g = driftGain, o = driftOff;
            var target = new PolynomialEnergyCalibration(poly);
            target.Coefficients = new[]
            {
                c[0] + c[1] * o + c[2] * o * o + c[3] * o * o * o,
                g * (c[1] + 2.0 * c[2] * o + 3.0 * c[3] * o * o),
                g * g * (c[2] + 3.0 * c[3] * o),
                g * g * g * c[3]
            };
            target.PolynomialOrder = 3;
            return target;
        }

        // Дрейф прибора: квант, который по объявленной шкале сел бы в канал c, у сдрейфовавшего прибора садится в
        // c' = c·g + off. Содержимое канала j дрейфнувшего спектра — истина в диапазоне объявленных каналов
        // ((j − 0.5 − off)/g … (j + 0.5 − off)/g), то есть перекладка истины по энергии в шкалу E_t(j) = E_d((j − off)/g).
        static double[] Drift(double[] truth, EnergyCalibration declared, int channels)
        {
            if (driftGain == 1.0 && driftOff == 0.0) return truth;
            var target = new DriftedCalibration(declared, driftGain, driftOff);
            // ⛔ Множитель — по месту, не 1000: у G1S24_Eu152_P5 (44 М отсчётов) рентген 2.5 М/канал × 1000
            // переполнял int (01.10.2026) — три канала «−0», всплеск в соседнем, и разбор копии «терял» Eu-152 целиком.
            double peak = 0.0; for (int i = 0; i < channels; i++) peak = Math.Max(peak, truth[i]);
            double factor = Math.Min(1000.0, Math.Floor((int.MaxValue / 4.0) / Math.Max(peak, 1.0)));
            if (factor < 1.0) factor = 1.0;
            int[] scaled = new int[channels];
            for (int i = 0; i < channels; i++) scaled[i] = i >= driftFrom ? (int)Math.Round(truth[i] * factor) : 0;
            double[] moved = SpectrumAriphmetics.RebinByEnergy(scaled, declared, target, channels);
            for (int i = 0; i < channels; i++) moved[i] = moved[i] / factor + (i < driftFrom ? truth[i] : 0.0);
            return moved;
        }

        // Шкала сдрейфовавшего прибора: центр канала j отвечает объявленной энергии канала (j − off)/g.
        sealed class DriftedCalibration : EnergyCalibration
        {
            readonly EnergyCalibration declared; readonly double g, off;
            public DriftedCalibration(EnergyCalibration declared, double g, double off) { this.declared = declared; this.g = g; this.off = off; }
            public override double ChannelToEnergy(double n) { return this.declared.ChannelToEnergy((n - this.off) / this.g); }
            public override double EnergyToChannel(double e, int maxChannels = 10000) { return this.declared.EnergyToChannel(e, maxChannels) * this.g + this.off; }
            public override EnergyCalibration Clone() { return new DriftedCalibration(this.declared, this.g, this.off); }
            public override EnergyCalibration Downgrade(int p) { return this.Clone(); }
            public override bool Equals(EnergyCalibration calib) { return ReferenceEquals(this, calib); }
            public override int MaxChannels() { return this.declared.MaxChannels(); }
        }
        static string lastBand, lastRefusal;

        static void PrintNotes(string who, FsaResult r)
        {
            Console.WriteLine("  [{0}] gain {1:F5} сдвиг {2:F3} кан; chi2ndf {3:F3}; {4}", who, r.Gain, r.OffsetChannels, r.Chi2Ndf, r.AnchorNote ?? "—");
            Console.WriteLine("  [{0}] множитель ширины {1:F5} по {2} опорам", who, r.WidthScale, r.WidthAnchorsUsed);
            Console.WriteLine("  [{0}] {1}", who, lastBand ?? "—");
            if (r.SuppressedImages != null && r.SuppressedImages.Count > 0)
                foreach (FsaSuppressedImage si in r.SuppressedImages)
                    Console.WriteLine("  [{0}]   снят образ {1} ({2}) z={3:F2} ряд={4}", who, si.Name, si.Kind, si.Z, si.DecayChainRoot ?? "—");
            if (r.Components != null)
                foreach (FsaComponentResult c in r.Components)
                    Console.WriteLine("  [{0}]   компонент {1,-12} {2,-8} A={3:F1} z={4:F2} пик.отсч={5:F0}", who, c.Name, c.Kind, c.CountRate * r.LiveTime, c.Z, c.PeakCounts);
            if (r.ScaleAnchors != null)
            {
                foreach (FsaScaleAnchor a in r.ScaleAnchors)
                {
                    Console.WriteLine("  [{0}]   опора {1,-8} {2,8:F2} кэВ  модель {3,8:F3}  данные {4,8:F3}  сдвиг {5,7:F3} кэВ  σ {6,6:F3}  доля {7:F3}  z {8,7:F1}  окно {9}…{10}  {11}  ширина данные/образ {12:F4}",
                                      who, a.Component, a.LineKev, a.ModelKev, a.MeasuredKev, a.ShiftKev, a.SigmaKev, a.PeakShare, a.Z,
                                      a.FirstChannel, a.LastChannel, a.Used ? "ВЗЯТА" : (a.Refusal ?? "—"), a.WidthRatio);
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

            // --set/--field — ПОСЛЕ полосы прибора, чтобы MinEnergy/MaxEnergy ключом тоже доезжали
            foreach (var kv in sets) kv.Key.SetValue(analyzer, kv.Value, null);
            foreach (var kv in fieldSets) kv.Key.SetValue(analyzer, kv.Value);
            if (runningCopy)
            {
                foreach (var kv in copySets) kv.Key.SetValue(analyzer, kv.Value, null);
                foreach (var kv in copyFieldSets) kv.Key.SetValue(analyzer, kv.Value);
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

            // (T243) настройки разбора против умолчаний — один раз, на истине
            if (!tuningPrinted)
            {
                FsaTuningReport.Print(analyzer, "замкнутая проверка");
                tuningPrinted = true;
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


        // П191 --rebin=k: та же физика в k раз более грубых каналах — RebinByEnergy приложения, калибровка энергии
        // по каналу x = k·x' (c_i → c_i·k^i), ПШПВ в каналах — RescaleCoefficients(k) (a → a·k^(p−1)).
        static void RebinAll(ResultData rd, int k)
        {
            if (decimate) { DecimateAll(rd, k); return; }
            rd.EnergySpectrum = RebinSpectrum(rd.EnergySpectrum, k, "спектр");
            if (rd.BackgroundEnergySpectrum != null && rd.BackgroundEnergySpectrum.Spectrum != null)
                rd.BackgroundEnergySpectrum = RebinSpectrum(rd.BackgroundEnergySpectrum, k, "фон");
            var pf = rd.FwhmCalibration as PowerFwhmCalibration;
            if (pf == null) throw new InvalidOperationException("--rebin: нужна PowerFwhmCalibration");
            var f = (PowerFwhmCalibration)pf.Clone();
            double a0 = f.Coefficients[0];
            f.RescaleCoefficients(k);
            rd.FwhmCalibration = f;
            Console.WriteLine("ПШПВ ×{0}: a {1} → {2} (p {3}); FWHM(кан 100 старых = {4} новых) {5:F3} → {6:F3} кан", k,
                              a0.ToString("G6", CultureInfo.InvariantCulture), f.Coefficients[0].ToString("G6", CultureInfo.InvariantCulture),
                              f.Coefficients[1].ToString("G6", CultureInfo.InvariantCulture), 100.0 / k, pf.ChannelToFwhm(100.0), f.ChannelToFwhm(100.0 / k) * k);
        }

        static void DecimateAll(ResultData rd, int k)
        {
            rd.EnergySpectrum = DecimateSpectrum(rd.EnergySpectrum, k, "спектр");
            if (rd.BackgroundEnergySpectrum != null && rd.BackgroundEnergySpectrum.Spectrum != null)
                rd.BackgroundEnergySpectrum = DecimateSpectrum(rd.BackgroundEnergySpectrum, k, "фон");
            var pf = rd.FwhmCalibration as PowerFwhmCalibration;
            if (pf == null) throw new InvalidOperationException("--rebin: нужна PowerFwhmCalibration");
            var f = new DecimatedPowerFwhm(pf, k);
            rd.FwhmCalibration = f;
            Console.WriteLine("ПШПВ укрупнена точно ×{0}: ПШПВ'(m) = ПШПВ({0}·m + {1})/{0}; кан 50 новых: {2:F4} кан (старых {3:F4})", k,
                              (0.5 * (k - 1)).ToString("F1", CultureInfo.InvariantCulture), f.ChannelToFwhm(50.0), pf.ChannelToFwhm(50.0 * k + 0.5 * (k - 1)));
        }

        sealed class DecimatedPowerFwhm : PowerFwhmCalibration
        {
            readonly PowerFwhmCalibration inner; readonly int k;
            public DecimatedPowerFwhm(PowerFwhmCalibration inner, int k)
            {
                this.inner = inner; this.k = k;
                var c = (PowerFwhmCalibration)inner.Clone(); c.RescaleCoefficients(k);
                this.Coefficients = (double[])c.Coefficients.Clone();
                this.PeakType = inner.PeakType; this.ExpGaussExpLeftTail = inner.ExpGaussExpLeftTail; this.ExpGaussExpRightTail = inner.ExpGaussExpRightTail;
                this.VoigtSigma = inner.VoigtSigma; this.VoigtGamma = inner.VoigtGamma;
            }
            // П208 --fwhm-binning: калибровка ПШПВ — НАБЛЮДАЕМАЯ ширина (в ней ящик канала 1/12 кан²); у канала вдвое шире ящик
            // k²/12 старых кан², и наблюдаемая ширина укрупнённого спектра — sqrt(ПШПВ² + 8·ln2·(k² − 1)/12)/k, а не ПШПВ/k
            public override double ChannelToFwhm(double channel)
            {
                double w = this.inner.ChannelToFwhm(this.k * channel + 0.5 * (this.k - 1));
                if (fwhmBinning) w = Math.Sqrt(w * w + 8.0 * Math.Log(2.0) * (this.k * this.k - 1) / 12.0);
                return w / this.k;
            }
            public override FwhmCalibration Clone() { return new DecimatedPowerFwhm(this.inner, this.k); }
        }

        static EnergySpectrum DecimateSpectrum(EnergySpectrum s, int k, string what)
        {
            var cal = s.EnergyCalibration as PolynomialEnergyCalibration;
            if (cal == null) throw new InvalidOperationException("--decimate: нужна PolynomialEnergyCalibration у " + what);
            int n = s.NumberOfChannels, m = n / k;
            // E'(x) = E(k·x + h), h = (k−1)/2: коэффициенты композиции полинома (бином Ньютона) — точно для любого порядка
            double h = 0.5 * (k - 1);
            double[] c = cal.Coefficients;
            double[] d = new double[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                // c_i·(k·x + h)^i = c_i·Σ_j C(i,j)·k^j·x^j·h^(i−j)
                for (int j = 0; j <= i; j++)
                {
                    double binom = 1.0; for (int t = 0; t < j; t++) binom = binom * (i - t) / (t + 1);
                    d[j] += c[i] * binom * Math.Pow(k, j) * Math.Pow(h, i - j);
                }
            }
            var target = new PolynomialEnergyCalibration(cal);
            target.Coefficients = d;
            target.CheckCalibration(m);
            int[] counts = new int[m];
            long tot = 0, sumOld = 0;
            for (int i = 0; i < m; i++) { int v = 0; for (int t = 0; t < k; t++) v += s.Spectrum[k * i + t]; counts[i] = v; tot += v; }
            for (int i = 0; i < n; i++) sumOld += s.Spectrum[i];
            Console.WriteLine("{0} укрупнён точно ×{1}: каналов {2} → {3}, сумма {4} → {5}; E'(0) {6} = E({7}) {8}; E'(10) {9} = E({10}) {11} кэВ", what, k, n, m, sumOld, tot,
                              target.ChannelToEnergy(0).ToString("F3", CultureInfo.InvariantCulture), h.ToString("F1", CultureInfo.InvariantCulture), cal.ChannelToEnergy(h).ToString("F3", CultureInfo.InvariantCulture),
                              target.ChannelToEnergy(10).ToString("F3", CultureInfo.InvariantCulture), (10.0 * k + h).ToString("F1", CultureInfo.InvariantCulture), cal.ChannelToEnergy(10.0 * k + h).ToString("F3", CultureInfo.InvariantCulture));
            return new EnergySpectrum
            {
                NumberOfChannels = m, Spectrum = counts, EnergyCalibration = target, TotalPulseCount = tot, ValidPulseCount = tot,
                MeasurementTime = s.MeasurementTime, LiveTime = s.LiveTime, ChannelPitch = s.ChannelPitch * k
            };
        }

        static EnergySpectrum RebinSpectrum(EnergySpectrum s, int k, string what)
        {
            var cal = s.EnergyCalibration as PolynomialEnergyCalibration;
            if (cal == null) throw new InvalidOperationException("--rebin: нужна PolynomialEnergyCalibration у " + what);
            int n = s.NumberOfChannels, m = n / k;
            var target = new PolynomialEnergyCalibration(cal);
            double[] c = (double[])target.Coefficients.Clone();
            double f = 1.0;
            for (int i = 0; i < c.Length; i++) { c[i] *= f; f *= k; }
            target.Coefficients = c;
            target.CheckCalibration(m);
            double[] moved = SpectrumAriphmetics.RebinByEnergy(s.Spectrum, cal, target, m);
            int[] counts = new int[m];
            long tot = 0, sumOld = 0;
            double sumMoved = 0.0;
            for (int i = 0; i < m; i++) { counts[i] = (int)Math.Round(moved[i]); tot += counts[i]; sumMoved += moved[i]; }
            for (int i = 0; i < n; i++) sumOld += s.Spectrum[i];
            Console.WriteLine("{0} перебинирован ×{1}: каналов {2} → {3}, сумма {4} → {5} → {6}; E(0) {7} → {8}, E(посл) {9} → {10} кэВ", what, k, n, m, sumOld,
                              sumMoved.ToString("F1", CultureInfo.InvariantCulture), tot,
                              cal.ChannelToEnergy(0).ToString("F3", CultureInfo.InvariantCulture), target.ChannelToEnergy(0).ToString("F3", CultureInfo.InvariantCulture),
                              cal.ChannelToEnergy(n - 1).ToString("F2", CultureInfo.InvariantCulture), target.ChannelToEnergy(m - 1).ToString("F2", CultureInfo.InvariantCulture));
            return new EnergySpectrum
            {
                NumberOfChannels = m, Spectrum = counts, EnergyCalibration = target, TotalPulseCount = tot, ValidPulseCount = tot,
                MeasurementTime = s.MeasurementTime, LiveTime = s.LiveTime, ChannelPitch = s.ChannelPitch * k
            };
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
