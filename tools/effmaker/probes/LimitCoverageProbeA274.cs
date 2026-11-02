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

/// <summary>
/// `AMBER274` (02.11.2026, полоса `fsa274b`): ПОКРЫТИЕ характеристических пределов FSA (`S9`)
/// Монте-Карло — приёмка решений Amber 02.11.2026 вопросником, дословно: «Шапки в оценщик
/// (Рекомендую)» и «MDA под порог z ≥ 3 (Рекомендую)». Приём — критика `crit_beta_lim`
/// (02.11.2026), без его рычагов: правила пределов включаются и выключаются настройками
/// анализатора (`FsaAnalyzer.LimitFreeContinuum`, `FsaAnalyzer.LimitAtWindowThreshold`).
/// Журнал — `handover/handover-2026-11-02-amber274-fsa274b.md`.
///
///     LimitCoverageProbeA274 --spectrum=<файл.xml> --matrix=<файл.rmx> --sample=… --chain=…
///         --target=<имя строки> [--levels=0,thr,lim,2lim,<x>lim,<x>bq,mthr,mlim,<x>mlim] [--seeds=40] [--seed0=1]
///         [--base=model|data|resample|synthetic] [--syn-counts=<отсч.>] [--syn-only-target]
///         [--refit-z=<z>] [--no-anchor] [--limit-free-hats=0|1] [--limit-window=0|1]
///         [--beta-two-pass=0|1] [--csv=<файл>]
///
/// Нулевой разбор даёт a* и a# строки-цели; затем спектр разыгрывается заново (`--base=`): `model` —
/// Пуассон модели нулевого фита плюс вычтенный фон (без цели), `data` — спектр как есть,
/// `resample` — Пуассон спектра, `synthetic` — линейная подложка без фона, которую шапки сплайна
/// описывают точно; поверх — Пуассон образа цели (тот же построитель, что у фита, вместе с
/// отвязанным хвостом) на уровнях `--levels=`. Печатается доля зёрен, где цель НАЙДЕНА ОКНОМ —
/// строка состава с амплитудой больше нуля после отсева значимостью, то есть ровно то, что
/// приложение показывает найденным; рядом — решение ISO 11929 (скорость строки предела выше a*
/// того же разбора). Уровни `thr`/`lim` — a*/a# нулевого разбора (по самому спектру); `mthr`/`mlim`
/// — МЕДИАНЫ a*/a# зёрен уровня `0` (по разыгранной подложке: у подложки «модель» невязки формы
/// нет, и её пределы уже, чем у спектра) — уровень `0` обязан стоять в списке раньше. Приёмка
/// «MDA под порог окна»: на уровне `mlim` доля найденных окном — 95 ± 4 % (40 зёрен).
/// Код 2 — ключи или данные.
/// </summary>
public static class LimitCoverageProbeA274
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        FsaTuningReport.Snapshot();
        string spectrumPath = null, matrixPath = null, csv = null, targetName = null, baseMode = "model";
        string levelsArg = "0,thr,lim";
        var chains = new List<string>();
        var nuclides = new List<string>();
        int seeds = 40, seed0 = 1;
        double synCounts = 2.0e6, refitZ = double.NaN;
        bool synOnlyTarget = false, noAnchor = false;
        int freeHats = -1, window = -1, twoPass = -1;
        foreach (string a in args)
        {
            if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
            else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
            else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            else if (a.StartsWith("--target=", StringComparison.Ordinal)) targetName = a.Substring(9);
            else if (a.StartsWith("--levels=", StringComparison.Ordinal)) levelsArg = a.Substring(9);
            else if (a.StartsWith("--seeds=", StringComparison.Ordinal)) seeds = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed0=", StringComparison.Ordinal)) seed0 = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--base=", StringComparison.Ordinal)) baseMode = a.Substring(7);
            else if (a.StartsWith("--syn-counts=", StringComparison.Ordinal)) synCounts = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
            else if (a == "--syn-only-target") synOnlyTarget = true;
            else if (a.StartsWith("--refit-z=", StringComparison.Ordinal)) refitZ = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a == "--no-anchor") noAnchor = true;
            else if (a.StartsWith("--limit-free-hats=", StringComparison.Ordinal)) freeHats = int.Parse(a.Substring(18), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--limit-window=", StringComparison.Ordinal)) window = int.Parse(a.Substring(15), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--beta-two-pass=", StringComparison.Ordinal)) twoPass = int.Parse(a.Substring(16), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (spectrumPath == null || matrixPath == null || targetName == null || (chains.Count == 0 && nuclides.Count == 0)
            || (baseMode != "model" && baseMode != "data" && baseMode != "resample" && baseMode != "synthetic"))
        {
            Console.Error.WriteLine("нужны --spectrum=, --matrix=, --target=, состав и --base=model|data|resample|synthetic");
            return 2;
        }

        Func<ResultData, GeometryModel, ResponseMatrix, FsaAnalyzer> make = (rd0, geometry0, matrix0) =>
        {
            var analyzer = new FsaAnalyzer();
            FsaMatrixBinding.Bind(analyzer, geometry0, matrix0);
            analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(geometry0);
            if (rd0.DeviceConfig != null && rd0.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd0.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd0.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            if (!double.IsNaN(refitZ)) analyzer.RefitZ = refitZ;
            if (noAnchor) analyzer.AnchorScale = false;
            if (freeHats >= 0) analyzer.LimitFreeContinuum = freeHats == 1;
            if (window >= 0) analyzer.LimitAtWindowThreshold = window == 1;
            if (twoPass >= 0) analyzer.BetaTwoPass = twoPass == 1;
            return analyzer;
        };

        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        ResultData rd = Load(spectrumPath);
        Console.WriteLine("спектр {0}; прибор {1}", Path.GetFileName(spectrumPath), ProbeDeviceConfig.Attach(rd));
        GeometryModel geometry = rd.Efficiency != null ? rd.Efficiency.Geometry : null;
        MatrixRefusal refusal;
        int fileFormat;
        ResponseMatrix matrix = ResponseMatrix.Load(matrixPath, out refusal, out fileFormat);
        if (matrix == null || geometry == null || !matrix.IsValidFor(geometry))
        {
            Console.Error.WriteLine("⛔ матрица не прочлась или клеймо не сошлось: {0}", refusal);
            return 2;
        }

        Console.WriteLine("матрица {0}: раздел электрона {1}", Path.GetFileName(matrixPath),
                          matrix.ElectronSection != null ? matrix.ElectronSection.NodeCount + " узлов" : "нет");

        FsaSampleLibrary.Report report;
        FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
        List<FsaComponent> library = FsaSampleLibrary.Build(spec, out report);
        FsaComponent target = null;
        foreach (FsaComponent c in library)
        {
            if (c.Kind != FsaComponentKind.Nuisance && string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                target = c;
            }
        }

        if (target == null)
        {
            Console.Error.WriteLine("⛔ в составе нет строки «{0}»", targetName);
            return 2;
        }

        if (synOnlyTarget)
        {
            var only = new List<FsaComponent>();
            foreach (FsaComponent c in library)
            {
                if (ReferenceEquals(c, target) || c.Kind == FsaComponentKind.Nuisance) only.Add(c);
            }

            library = only;
        }

        Console.Write("состав:");
        foreach (FsaComponent c in library) Console.Write(" {0}[{1}{2}]", c.Name, c.Kind, c.BetaOnly ? ",β" : "");
        Console.WriteLine();
        Console.WriteLine("подложка {0}; зёрен {1}", baseMode, seeds);

        int channels = rd.EnergySpectrum.NumberOfChannels;
        int[] original = (int[])rd.EnergySpectrum.Spectrum.Clone();
        double[] baseMean = new double[channels];
        if (baseMode == "synthetic")
        {
            rd.BackgroundEnergySpectrum = null;
            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            double sum = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double e = cal.ChannelToEnergy(i);
                baseMean[i] = e < 15.0 ? 0.0 : 1.5 - (double)i / channels;
                sum += baseMean[i];
            }

            for (int i = 0; i < channels; i++) baseMean[i] *= synCounts / sum;
            for (int i = 0; i < channels; i++) rd.EnergySpectrum.Spectrum[i] = (int)Math.Round(baseMean[i]);
            SetTotals(rd);
        }

        // --- нулевой разбор ---
        FsaAnalyzer first = make(rd, geometry, matrix);
        FsaTuningReport.Print(first, "нулевой разбор");
        FsaResult r0 = first.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                     library, FsaEfficiency.FromConfig(rd.Efficiency));
        if (r0 == null)
        {
            Console.Error.WriteLine("⛔ нулевой разбор: отказ {0} {1}", first.Refusal, first.RefusalNote);
            return 2;
        }

        double live = r0.LiveTime;
        FsaCharacteristicLimit l0 = LimitOf(r0, target.Name);
        FsaComponentResult c0 = ComponentOf(r0, target.Name);
        Console.WriteLine("нулевой разбор: χ²/ndf Пуассона {0}; σ× {1}; порог отсева {2}; k_α {3}, k_β {4}; шапок в оценщике {5}",
                          F(r0.Chi2NdfPoisson, 3), F(r0.SigmaInflation, 3), F(first.RefitZUsed, 2),
                          F(r0.LimitDecisionQuantile, 3), F(r0.LimitDetectionQuantile, 4), first.LimitHatsAdded);
        foreach (FsaCharacteristicLimit l in r0.CharacteristicLimits)
        {
            Console.WriteLine("  предел {0,-10} обн. {1,-3} {2,12} /с a* {3,10} a# {4,10} колл. {5}", l.Name,
                              l.Detected ? "да" : "нет", F(l.CountRate, 3), F(l.DecisionThresholdRate, 3),
                              F(l.DetectionLimitRate, 3), F(l.Collinearity, 5));
        }

        if (l0 == null || double.IsNaN(l0.DetectionLimitRate))
        {
            Console.WriteLine("⚠ у цели нет предела в нулевом разборе — уровни thr/lim не определены");
        }

        double thr0 = l0 != null ? l0.DecisionThresholdRate : double.NaN;
        double lim0 = l0 != null ? l0.DetectionLimitRate : double.NaN;

        // --- образ цели: тот же построитель, что у фита ---
        MethodInfo build = typeof(FsaAnalyzer).GetMethod("BuildTemplateFromResponse", BindingFlags.Instance | BindingFlags.NonPublic);
        if (build == null)
        {
            Console.Error.WriteLine("⛔ отражение не нашло BuildTemplateFromResponse — проба устарела");
            return 2;
        }

        object[] call = { target, rd.EnergySpectrum.EnergyCalibration, rd.FwhmCalibration, r0.Gain, r0.OffsetChannels,
                          0, channels - 1, channels, null };
        var column = (double[])build.Invoke(first, call);
        var tail = (double[])call[8];
        if (column == null)
        {
            Console.Error.WriteLine("⛔ образ цели не построился");
            return 2;
        }

        double[] image = new double[channels];
        double perDecay = 0.0;
        for (int i = 0; i < channels; i++)
        {
            image[i] = column[i] + (tail != null && i < tail.Length ? tail[i] : 0.0);
            perDecay += image[i];
        }

        Console.WriteLine("образ «{0}»: регистраций на распад {1}", target.Name, perDecay.ToString("G5", CultureInfo.InvariantCulture));
        if (baseMode == "model")
        {
            double[] fm = r0.FitModel();
            double[] bg = r0.Background;
            double[] own = new double[channels];
            if (c0 != null && c0.CountRate > 0.0)
            {
                for (int i = 0; i < channels; i++)
                {
                    own[i] = (c0.Curve != null && i < c0.Curve.Length ? c0.Curve[i] : 0.0)
                             + (c0.TailCurve != null && i < c0.TailCurve.Length ? c0.TailCurve[i] : 0.0);
                }

                Console.WriteLine("⚠ цель в нулевом фите найдена ({0} /с) — её кривая вынута из модели", F(c0.CountRate, 3));
            }

            for (int i = 0; i < channels; i++)
            {
                baseMean[i] = i < r0.FirstChannel || i > r0.LastChannel
                    ? original[i]
                    : Math.Max(0.0, fm[i] - own[i]) + (bg != null && i < bg.Length ? bg[i] : 0.0);
            }
        }
        else if (baseMode == "resample" || baseMode == "data")
        {
            for (int i = 0; i < channels; i++) baseMean[i] = original[i];
        }

        int[] reference = (int[])rd.EnergySpectrum.Spectrum.Clone();
        var levels = new List<KeyValuePair<string, double>>();
        foreach (string s in levelsArg.Split(','))
        {
            double v;
            if (s == "mthr" || s == "mlim" || s.EndsWith("mlim", StringComparison.Ordinal)) v = double.NaN;   // после уровня 0
            else if (s == "0") v = 0.0;
            else if (s == "thr") v = thr0;
            else if (s == "lim") v = lim0;
            else if (s.EndsWith("lim", StringComparison.Ordinal)) v = lim0 * double.Parse(s.Substring(0, s.Length - 3), CultureInfo.InvariantCulture);
            else if (s.EndsWith("thr", StringComparison.Ordinal)) v = thr0 * double.Parse(s.Substring(0, s.Length - 3), CultureInfo.InvariantCulture);
            else if (s.EndsWith("bq", StringComparison.Ordinal)) v = double.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("уровень? " + s); return 2; }
            levels.Add(new KeyValuePair<string, double>(s, v));
        }

        var rows = new List<string> { "base;level;activity;seed;app_rate;app_z;app_det;lim_rate;thr;lim;iso_det;chi2p" };
        Console.WriteLine();
        Console.WriteLine("{0,-7} {1,10} | {2,-13} {3,-13} | {4,10} {5,9} | {6,10} {7,10}",
                          "уровень", "Бк", "окно нашло", "ISO нашло", "найдено", "σ зерна", "a* мед.", "a# мед.");
        double thrMedian = double.NaN, limMedian = double.NaN;
        foreach (KeyValuePair<string, double> level in levels)
        {
            double activity = level.Value;
            if (level.Key == "mthr") activity = thrMedian;
            else if (level.Key == "mlim") activity = limMedian;
            else if (level.Key.EndsWith("mlim", StringComparison.Ordinal))
                activity = limMedian * double.Parse(level.Key.Substring(0, level.Key.Length - 4), CultureInfo.InvariantCulture);
            if (double.IsNaN(activity))
            {
                Console.WriteLine("{0,-7} — уровень не определён", level.Key);
                continue;
            }

            int appDet = 0, isoDet = 0, n = 0;
            var found = new List<double>();
            var thrs = new List<double>();
            var lims = new List<double>();
            for (int s = 0; s < seeds; s++)
            {
                var random = new Random(seed0 + s);
                int[] spectrum = rd.EnergySpectrum.Spectrum;
                for (int i = 0; i < channels; i++)
                {
                    int b = baseMode == "data" ? original[i] : Poisson(random, baseMean[i]);
                    spectrum[i] = b + Poisson(random, activity * live * image[i]);
                }

                SetTotals(rd);
                FsaAnalyzer analyzer = make(rd, geometry, matrix);
                FsaResult r = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                               library, FsaEfficiency.FromConfig(rd.Efficiency));
                double appRate = 0.0, appZ = 0.0, limRate = double.NaN, thr = double.NaN, lim = double.NaN, chi = double.NaN;
                bool app = false, iso = false;
                if (r != null)
                {
                    n++;
                    chi = r.Chi2NdfPoisson;
                    FsaComponentResult c = ComponentOf(r, target.Name);
                    if (c != null)
                    {
                        appRate = c.CountRate;
                        appZ = c.Z;
                        app = appRate > 0.0;
                    }

                    FsaCharacteristicLimit l = LimitOf(r, target.Name);
                    if (l != null)
                    {
                        limRate = l.CountRate;
                        thr = l.DecisionThresholdRate;
                        lim = l.DetectionLimitRate;
                        iso = !double.IsNaN(thr) && limRate > thr;
                    }

                    if (app) appDet++;
                    if (iso) isoDet++;
                    found.Add(appRate);
                    if (!double.IsNaN(thr)) thrs.Add(thr);
                    if (!double.IsNaN(lim)) lims.Add(lim);
                }

                rows.Add(string.Join(";", baseMode, level.Key, F(activity, 4), (seed0 + s).ToString(CultureInfo.InvariantCulture),
                                     F(appRate, 4), F(appZ, 3), app ? "1" : "0", F(limRate, 4), F(thr, 4), F(lim, 4),
                                     iso ? "1" : "0", F(chi, 4)));
            }

            double mean = 0.0;
            foreach (double v in found) mean += v;
            mean /= Math.Max(1, found.Count);
            double var = 0.0;
            foreach (double v in found) var += (v - mean) * (v - mean);
            double sd = found.Count > 1 ? Math.Sqrt(var / (found.Count - 1)) : 0.0;
            Console.WriteLine("{0,-7} {1,10} | {2,-13} {3,-13} | {4,10} {5,9} | {6,10} {7,10}",
                              level.Key, F(activity, 3), Frac(appDet, n), Frac(isoDet, n), F(mean, 3), F(sd, 3),
                              F(Median(thrs), 3), F(Median(lims), 3));
            if (level.Key == "0")
            {
                thrMedian = Median(thrs);
                limMedian = Median(lims);
            }
        }

        Array.Copy(reference, rd.EnergySpectrum.Spectrum, reference.Length);
        if (csv != null)
        {
            File.WriteAllLines(csv, rows.ToArray(), new UTF8Encoding(false));
            Console.WriteLine("csv: {0}", csv);
        }

        return 0;
    }

    static string Frac(int k, int n)
    {
        if (n == 0) return "—";
        return string.Format(CultureInfo.InvariantCulture, "{0}/{1}={2:F3}", k, n, (double)k / n);
    }

    static double Median(List<double> v)
    {
        if (v.Count == 0) return double.NaN;
        var c = new List<double>(v);
        c.Sort();
        return c.Count % 2 == 1 ? c[c.Count / 2] : 0.5 * (c[c.Count / 2 - 1] + c[c.Count / 2]);
    }

    static void SetTotals(ResultData rd)
    {
        long total = 0;
        foreach (int v in rd.EnergySpectrum.Spectrum) total += v;
        rd.EnergySpectrum.TotalPulseCount = total;
        rd.EnergySpectrum.ValidPulseCount = total;
    }

    static FsaCharacteristicLimit LimitOf(FsaResult r, string name)
    {
        foreach (FsaCharacteristicLimit l in r.CharacteristicLimits)
            if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(l.TiedTo)) return l;
        return null;
    }

    static FsaComponentResult ComponentOf(FsaResult r, string name)
    {
        foreach (FsaComponentResult c in r.Components)
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    /// <summary>Пуассон: малые — произведением равномерных, большие — нормальным приближением с округлением.</summary>
    static int Poisson(Random random, double mean)
    {
        if (!(mean > 0.0)) return 0;
        if (mean < 30.0)
        {
            double limit = Math.Exp(-mean), p = 1.0;
            int k = 0;
            do { k++; p *= random.NextDouble(); } while (p > limit);
            return k - 1;
        }

        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * normal));
    }

    static List<string> NucidsOf(List<string> labels)
    {
        var nucids = new List<string>();
        foreach (string label in labels)
        {
            string nucid = FsaSampleLibrary.NucidOf(label);
            nucids.Add(nucid.Length > 0 ? nucid : label.ToUpperInvariant());
        }

        return nucids;
    }

    static string F(double value, int digits)
    {
        return double.IsNaN(value) ? "NaN"
            : value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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

        if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            rd.FwhmCalibration = cfg.FwhmCalibration;
        }

        return rd;
    }
}
