using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

/// <summary>
/// `AMBER274`, этап Э1, Ф14 (01.11.2026, полоса `purebeta`): СТРОКА ПО СПЛОШНОМУ СПЕКТРУ —
/// чистый бета-излучатель в составе FSA. Решение Amber 30.10.2026 вопросником, дословно:
/// «Строка без линий по набору (Рекомендую)». Журнал —
/// `handover/handover-2026-11-01-amber274-purebeta.md`.
///
///     PureBetaProbeA274 --spectrum=<файл.xml> --matrix=<файл.rmx> [--sample=137CS] [--chain=Sr-90]
///                       [--inject=<метка>:<Бк>[,<Бк>…]] [--seeds=10] [--seed0=1] [--synthetic]
///                       [--mode=real|synth|synthcs|mirror] [--two-pass=0|1] [--beta-ladder=K,K,…]
///                       [--base-knot-fwhm=K] [--bits]
///                       [--no-section] [--no-matrix] [--matrix-any] [--trust-floor=<кэВ>] [--knot-fwhm=<K>] [--csv=<файл>]
///
/// (`AMBER274`, 02.11.2026, полоса `fsa274b`: «Редкие узлы + два захода») ПОДЛОЖКА ВПРЫСКА —
/// `--mode=`: `real` — спектр как есть (умолчание); `synth` — без спектра, фон снят (то же, что
/// `--synthetic`); `synthcs` — Пуассон ОЖИДАНИЯ первого разбора (модель + вычтенное, своим потоком
/// зёрен) — спектр без невязки формы, контроль самого оценщика (приём критика `crit_beta_fit`);
/// `mirror` — отражённая невязка `2·ожидание − спектр`: знак разлада модели с данными обращён, и
/// смещение от невязки меняет знак, а от оценщика — нет. `--two-pass=0` — плечо прежнего разбора
/// одним заходом (`FsaAnalyzer.BetaTwoPass`); `--beta-ladder=` — свой ряд шагов узлов под бетой
/// (`FsaAnalyzer.BetaKnotLadderFwhm`, плечо замера); `--base-knot-fwhm=K` — ожидание подложки
/// `synthcs`/`mirror` (и шкала образа) берётся у ПЕРВОГО разбора одним заходом с шагом узлов K:
/// подложка, которую заход беты с тем же шагом описывает точно, — контроль самого оценщика
/// (так мерил критик `crit_beta_fit`); без ключа — ожидание обычного разбора (два захода),
/// и в смещение входит ещё пол невязки формы подложки. `--bits` — скорости и χ² без впрыска ещё и битами
/// (формат R): приёмка «состав без беты в спектре — как сегодня до бита». Строка `ИТОГ` несёт и
/// средние прочих строк состава (загрязнение гамма-строк впрыском беты).
///
/// Без `--inject` — один разбор спектра объявленным составом: строки состава и пределов
/// (по сплошному ли спектру, скорость, z, предел), строки отчёта FSA словами окна (ru и en),
/// строки окна результата с пометкой, снятые строки и причина.
///
/// `--inject=<метка>:<Бк>` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Образ состава метки (ряд меткой ряда,
/// одиночка — nucid) строится ТЕМ ЖЕ анализатором (отражением `BuildTemplateFromResponse`
/// вместе с отвязанным подпороговым хвостом) на шкале первого разбора, умножается на
/// активность × живое время и разыгрывается Пуассоном поверх спектра (`--synthetic` — ВМЕСТО
/// спектра, фон снимается); затем разбор объявленным составом, `--seeds` зёрен. Печатается
/// найденная активность строки метки: среднее, разброс, смещение к заложенной, доля зёрен,
/// где строка обнаружена. Состав обязан содержать метку (`--chain=`/`--sample=`).
///
/// `--no-section` — раздел электрона у матрицы снят (строка обязана сняться с причиной);
/// `--no-matrix` — разбор без матрицы (то же, причина «нет матрицы»).
///
/// Код 1 — нарушено (`⛔ НАРУШЕНО`), код 2 — ключи или данные.
/// </summary>
public static class PureBetaProbeA274
{
    static int failures;

    /// <summary>`--trust-floor=<кэВ>` — порог доверия континуума матрицы (рычаг плеча, у ВСЕХ образов); NaN — умолчание.</summary>
    static double trustFloorKev = double.NaN;

    /// <summary>`--knot-fwhm=<K>` — шаг узлов сплайна подложки в ПШПВ (рычаг плеча: вырожденность беты со сплайном); NaN — умолчание.</summary>
    static double knotFwhmArm = double.NaN;

    /// <summary>`--two-pass=0|1` — два захода строки беты (`FsaAnalyzer.BetaTwoPass`); −1 — умолчание.</summary>
    static int twoPassArm = -1;

    /// <summary>`--bits` — печатать скорости и χ² разбора без впрыска ещё и битами.</summary>
    static bool bits;

    /// <summary>`--beta-ladder=K,K,…` — ряд шагов узлов под строкой беты (плечо); null — умолчание.</summary>
    static double[] ladderArm;

    /// <summary>`--base-knot-fwhm=K` — первый разбор (ожидание подложки впрыска) одним заходом с шагом K; NaN — умолчание.</summary>
    static double baseKnotFwhm = double.NaN;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        FsaTuningReport.Snapshot();
        string spectrumPath = null, matrixPath = null, csv = null, inject = null;
        var chains = new List<string>();
        var nuclides = new List<string>();
        int seeds = 10, seed0 = 1;
        bool synthetic = false, noSection = false, noMatrix = false, matrixAny = false;
        string mode = "real";
        double trustFloor = double.NaN, knotFwhm = double.NaN;
        foreach (string a in args)
        {
            if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
            else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
            else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            else if (a.StartsWith("--inject=", StringComparison.Ordinal)) inject = a.Substring(9);
            else if (a.StartsWith("--seeds=", StringComparison.Ordinal)) seeds = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed0=", StringComparison.Ordinal)) seed0 = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
            else if (a == "--synthetic") synthetic = true;
            else if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
            else if (a.StartsWith("--two-pass=", StringComparison.Ordinal))
                twoPassArm = int.Parse(a.Substring(11), CultureInfo.InvariantCulture);
            else if (a == "--bits") bits = true;
            else if (a.StartsWith("--base-knot-fwhm=", StringComparison.Ordinal))
                baseKnotFwhm = double.Parse(a.Substring(17), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--beta-ladder=", StringComparison.Ordinal))
            {
                var steps = new List<double>();
                foreach (string v in a.Substring(14).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    steps.Add(double.Parse(v, CultureInfo.InvariantCulture));
                }

                ladderArm = steps.ToArray();
            }
            else if (a.StartsWith("--knot-fwhm=", StringComparison.Ordinal))
                knotFwhm = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--trust-floor=", StringComparison.Ordinal))
                trustFloor = double.Parse(a.Substring(14), CultureInfo.InvariantCulture);
            else if (a == "--no-section") noSection = true;
            else if (a == "--no-matrix") noMatrix = true;
            else if (a == "--matrix-any") matrixAny = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        trustFloorKev = trustFloor;
        knotFwhmArm = knotFwhm;
        if (synthetic)
        {
            mode = "synth";
        }

        if (mode != "real" && mode != "synth" && mode != "synthcs" && mode != "mirror")
        {
            Console.Error.WriteLine("--mode=real|synth|synthcs|mirror");
            return 2;
        }

        synthetic = mode == "synth";
        if (spectrumPath == null || (matrixPath == null && !noMatrix) || (chains.Count == 0 && nuclides.Count == 0))
        {
            Console.Error.WriteLine("нужны --spectrum=, --matrix= (или --no-matrix) и состав --chain=/--sample=");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        ResultData rd = Load(spectrumPath);
        Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
        Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));
        GeometryModel geometry = rd.Efficiency != null ? rd.Efficiency.Geometry : null;

        ResponseMatrix matrix = null;
        if (!noMatrix)
        {
            MatrixRefusal refusal;
            int fileFormat;
            matrix = ResponseMatrix.Load(matrixPath, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрица не прочлась: {0} (формат {1})", refusal, fileFormat);
                return 2;
            }

            bool stamp = geometry != null && matrix.IsValidFor(geometry);
            Console.WriteLine("матрица: {0}; формат файла {1}; клеймо для сцены спектра {2}; раздел электрона {3}",
                              Path.GetFileName(matrixPath), fileFormat, stamp ? "сошлось" : "НЕ сошлось",
                              matrix.ElectronSection != null ? matrix.ElectronSection.NodeCount + " узлов" : "нет");
            if (!stamp && !matrixAny)
            {
                Console.Error.WriteLine("⛔ клеймо матрицы не сошлось со сценой спектра; осознанно — --matrix-any");
                return 2;
            }

            if (noSection)
            {
                matrix.ElectronSection = null;
                Console.WriteLine("раздел электрона СНЯТ (--no-section)");
            }
        }

        FsaSampleLibrary.Report report;
        FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
        List<FsaComponent> library = FsaSampleLibrary.Build(spec, out report);
        Console.WriteLine("состав: {0}", report);
        foreach (FsaComponent c in library)
        {
            if (c.Kind == FsaComponentKind.Nuisance)
            {
                continue;
            }

            Console.WriteLine("  {0,-10} {1,-6} линий {2,3}  бета-ветвей {3,3}  по сплошному {4}  выход {5}",
                              c.Name, c.Kind, c.Lines.Count, c.Betas.Count, c.BetaOnly ? "ДА" : "нет",
                              double.IsNaN(c.TotalYieldPercent) ? "NaN" : F(c.TotalYieldPercent, 4));
        }

        int[] original = (int[])rd.EnergySpectrum.Spectrum.Clone();
        if (inject == null)
        {
            FsaResult result = Analyze(rd, library, geometry, matrix, true);
            if (result == null)
            {
                return Done();
            }

            PrintResult(rd, result);
            return Done();
        }

        // --- положительный контроль: впрыск образа метки ---
        int colon = inject.LastIndexOf(':');
        string label = inject.Substring(0, colon);
        var activities = new List<double>();
        foreach (string s in inject.Substring(colon + 1).Split(','))
        {
            activities.Add(double.Parse(s, CultureInfo.InvariantCulture));
        }

        // Образ метки — анализатором первого разбора на реальном спектре.
        FsaAnalyzer first = MakeAnalyzer(rd, geometry, matrix);
        if (!double.IsNaN(baseKnotFwhm))
        {
            first.ContinuumKnotFwhm = baseKnotFwhm;
            first.BetaTwoPass = false;
        }

        FsaTuningReport.Print(first, "первый разбор (шкала образа)");
        FsaResult firstResult = first.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                              library, FsaEfficiency.FromConfig(rd.Efficiency));
        if (firstResult == null)
        {
            Console.Error.WriteLine("⛔ первый разбор не состоялся: {0} {1}", first.Refusal, first.RefusalNote);
            return 1;
        }

        FsaComponent target = null;
        foreach (FsaComponent c in library)
        {
            if (string.Equals(c.Name, Pretty(label), StringComparison.OrdinalIgnoreCase))
            {
                target = c;
            }
        }

        if (target == null)
        {
            Console.Error.WriteLine("⛔ в составе нет строки «{0}»", Pretty(label));
            return 2;
        }

        int channels = rd.EnergySpectrum.NumberOfChannels;
        EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
        MethodInfo build = typeof(FsaAnalyzer).GetMethod("BuildTemplateFromResponse", BindingFlags.Instance | BindingFlags.NonPublic);
        if (build == null)
        {
            Console.Error.WriteLine("⛔ отражение не нашло BuildTemplateFromResponse — проба устарела");
            return 2;
        }

        object[] call = { target, calibration, rd.FwhmCalibration, firstResult.Gain, firstResult.OffsetChannels,
                          0, channels - 1, channels, null };
        var column = (double[])build.Invoke(first, call);
        var tail = (double[])call[8];
        if (column == null)
        {
            Console.Error.WriteLine("⛔ образ «{0}» не построился (раздела нет?)", target.Name);
            return 1;
        }

        double perDecay = 0.0;
        var image = new double[channels];
        for (int i = 0; i < channels; i++)
        {
            image[i] = column[i] + (tail != null && i < tail.Length ? tail[i] : 0.0);
            perDecay += image[i];
        }

        double live = firstResult.LiveTime;
        // ожидание сырого спектра первого разбора: модель фита + вычтенное (фон, закреплённая бета)
        // в полосе фита, вне её — сам спектр
        var expectRaw = new double[channels];
        for (int i = 0; i < channels; i++)
        {
            expectRaw[i] = original[i];
        }

        if (first.LastFitModel != null)
        {
            for (int i = first.LastFitFirstChannel; i <= first.LastFitLastChannel && i < channels; i++)
            {
                double m = first.LastFitModel[i] + (first.LastFitSubtracted != null ? first.LastFitSubtracted[i] : 0.0);
                expectRaw[i] = Math.Max(0.0, m);
            }
        }

        Console.WriteLine("подложка впрыска: {0}", mode);
        Console.WriteLine();
        Console.WriteLine("образ «{0}»: регистраций на распад корня {1}; живое время {2} с; усиление {3}, сдвиг {4}",
                          target.Name, perDecay.ToString("G5", CultureInfo.InvariantCulture), F(live, 1),
                          F(firstResult.Gain, 6), F(firstResult.OffsetChannels, 3));
        if (synthetic)
        {
            rd.BackgroundEnergySpectrum = null;
            Console.WriteLine("⚠ --synthetic: спектр = только Пуассон образа, фон снят");
        }

        var lines = new List<string> { "activity_bq;seed;found_bq;z;detected;limit_bq;chi2ndf" };
        foreach (double activity in activities)
        {
            var found = new List<double>();
            var others = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            int detected = 0;
            for (int s = 0; s < seeds; s++)
            {
                var random = new Random(seed0 + s);
                // подложка `synthcs` — своим потоком: впрыск того же зерна от неё не зависит
                var randomBase = new Random(100000 + seed0 + s);
                int[] spectrum = rd.EnergySpectrum.Spectrum;
                long total = 0;
                for (int i = 0; i < channels; i++)
                {
                    int baseCount = mode == "synth" ? 0
                        : mode == "synthcs" ? Poisson(randomBase, expectRaw[i])
                        : mode == "mirror" ? Math.Max(0, (int)Math.Round(2.0 * expectRaw[i] - original[i]))
                        : original[i];
                    spectrum[i] = baseCount + Poisson(random, activity * live * image[i]);
                    total += spectrum[i];
                }

                rd.EnergySpectrum.TotalPulseCount = total;
                rd.EnergySpectrum.ValidPulseCount = total;
                FsaResult r = Analyze(rd, library, geometry, matrix, false);
                double rate = 0.0, z = 0.0, limit = double.NaN;
                bool isDetected = false;
                if (r != null)
                {
                    foreach (FsaComponentResult c in r.Components)
                    {
                        if (string.Equals(c.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            rate = c.CountRate;
                            z = c.Z;
                            isDetected = rate > 0.0;
                        }
                        else if (c.Kind != FsaComponentKind.Nuisance)
                        {
                            List<double> list;
                            if (!others.TryGetValue(c.Name, out list))
                            {
                                list = new List<double>();
                                others.Add(c.Name, list);
                            }

                            list.Add(c.CountRate);
                        }
                    }

                    foreach (FsaCharacteristicLimit l in r.CharacteristicLimits)
                    {
                        if (string.Equals(l.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            limit = l.DetectionLimitRate;
                        }
                    }
                }

                if (isDetected)
                {
                    detected++;
                }

                found.Add(rate);
                lines.Add(string.Join(";", F(activity, 3), (seed0 + s).ToString(CultureInfo.InvariantCulture), F(rate, 4),
                                      F(z, 3), isDetected ? "1" : "0", F(limit, 4), r != null ? F(r.Chi2NdfPoisson, 4) : "NaN"));
                if (s == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("--- заложено {0} Бк, зерно {1} ---", F(activity, 1), seed0 + s);
                    if (r != null)
                    {
                        PrintResult(rd, r);
                    }
                }
            }

            double mean = 0.0;
            foreach (double v in found)
            {
                mean += v;
            }

            mean /= found.Count;
            double var = 0.0;
            foreach (double v in found)
            {
                var += (v - mean) * (v - mean);
            }

            double sd = found.Count > 1 ? Math.Sqrt(var / (found.Count - 1)) : 0.0;
            Console.WriteLine();
            Console.WriteLine("ИТОГ «{0}»: заложено {1} Бк → найдено {2} ± {3} Бк (σ зерна), смещение {4} % ± {5} %; обнаружена в {6} из {7}",
                              target.Name, F(activity, 2), F(mean, 2), F(sd, 2),
                              activity > 0.0 ? F(100.0 * (mean - activity) / activity, 2) : "—",
                              activity > 0.0 ? F(100.0 * sd / activity / Math.Sqrt(found.Count), 2) : "—",
                              detected, found.Count);
            foreach (KeyValuePair<string, List<double>> pair in others)
            {
                double m = 0.0;
                foreach (double v in pair.Value)
                {
                    m += v;
                }

                m /= Math.Max(1, pair.Value.Count);
                Console.WriteLine("ИТОГ прочие «{0}»: заложено {1} Бк беты → {2} Бк (среднее {3} зёрен из {4})",
                                  pair.Key, F(activity, 2), F(m, 3), pair.Value.Count, found.Count);
            }
        }

        Array.Copy(original, rd.EnergySpectrum.Spectrum, original.Length);
        if (csv != null)
        {
            File.WriteAllLines(csv, lines.ToArray(), new UTF8Encoding(false));
            Console.WriteLine("csv: {0}", csv);
        }

        return Done();
    }

    static FsaAnalyzer MakeAnalyzer(ResultData rd, GeometryModel geometry, ResponseMatrix matrix)
    {
        var analyzer = new FsaAnalyzer();
        if (matrix != null)
        {
            FsaMatrixBinding.Bind(analyzer, geometry, matrix);
        }

        analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(geometry);
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

        if (!double.IsNaN(trustFloorKev))
        {
            analyzer.ResponseContinuumTrustFloorKev = trustFloorKev;
        }

        if (!double.IsNaN(knotFwhmArm))
        {
            analyzer.ContinuumKnotFwhm = knotFwhmArm;
        }

        if (twoPassArm >= 0)
        {
            analyzer.BetaTwoPass = twoPassArm == 1;
        }

        if (ladderArm != null)
        {
            analyzer.BetaKnotLadderFwhm = ladderArm;
        }

        return analyzer;
    }

    static FsaResult Analyze(ResultData rd, List<FsaComponent> library, GeometryModel geometry, ResponseMatrix matrix,
                             bool verbose)
    {
        FsaAnalyzer analyzer = MakeAnalyzer(rd, geometry, matrix);
        if (verbose)
        {
            FsaTuningReport.Print(analyzer, "разбор");
        }

        FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                            library, FsaEfficiency.FromConfig(rd.Efficiency));
        if (verbose || result == null)
        {
            Console.WriteLine("раздел для сцены: {0}; снято строк по сплошному спектру: {1} ({2}, раздел {3})",
                              analyzer.SampleBetaRefusal, analyzer.BetaOnlyRemoved.Count, analyzer.BetaOnlyRemovedReason,
                              analyzer.BetaOnlySectionRefusal);
        }

        if (result == null)
        {
            Console.WriteLine("разбор: ОТКАЗ {0} — {1}", analyzer.Refusal, analyzer.RefusalNote);
            foreach (string culture in new[] { "en", "ru-RU" })
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Console.WriteLine("  [{0}] {1}", culture,
                                  FSAReportView.BetaOnlyRefusalText(analyzer.BetaOnlyRemoved, analyzer.BetaOnlyRemovedReason,
                                                                    analyzer.BetaOnlySectionRefusal) ?? "(приписки нет)");
            }
        }

        return result;
    }

    static void PrintResult(ResultData rd, FsaResult result)
    {
        Console.WriteLine("разбор: χ²/ndf Пуассона {0}; бета в образах {1} отсч.", F(result.Chi2NdfPoisson, 4),
                          F(result.SampleBetaCounts, 0));
        Console.WriteLine("шаг узлов под бетой: {0}; VIF {1}; два захода {2}; ход: {3}",
                          F(result.BetaKnotFwhm, 1), F(result.BetaKnotVif, 2), result.BetaTwoPass ? "да" : "нет",
                          result.BetaKnotTrace ?? "—");
        if (bits)
        {
            Console.WriteLine("биты: χ²/ndf Пуассона {0}; χ²/ndf {1}",
                              result.Chi2NdfPoisson.ToString("R", CultureInfo.InvariantCulture),
                              result.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture));
            foreach (FsaComponentResult c in result.Components)
            {
                Console.WriteLine("биты: {0} скорость {1} z {2}", c.Name, c.CountRate.ToString("R", CultureInfo.InvariantCulture),
                                  c.Z.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        foreach (FsaComponentResult c in result.Components)
        {
            if (c.Kind == FsaComponentKind.Nuisance)
            {
                continue;
            }

            Console.WriteLine("  состав  {0,-10} {1,-6} по сплошному {2,-3} скорость {3,12} /с  z {4,8}  предел {5,12} /с  бета {6} отсч.",
                              c.Name, c.Kind, c.BetaOnly ? "ДА" : "нет", F(c.CountRate, 4), F(c.Z, 3),
                              F(c.DetectionLimitRate, 4), F(c.BetaCounts, 0));
        }

        foreach (FsaCharacteristicLimit l in result.CharacteristicLimits)
        {
            Console.WriteLine("  предел  {0,-10} {1,-6} по сплошному {2,-3} обнаружен {3,-3} порог {4,12} /с  предел {5,12} /с  выход {6}",
                              l.Name, l.Kind, l.BetaOnly ? "ДА" : "нет", l.Detected ? "да" : "нет",
                              F(l.DecisionThresholdRate, 4), F(l.DetectionLimitRate, 4),
                              double.IsNaN(l.TotalYieldPercent) ? "NaN" : F(l.TotalYieldPercent, 3));
        }

        foreach (string culture in new[] { "en", "ru-RU" })
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            FsaPresentation presentation = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
            Console.WriteLine("  [{0}] строки отчёта FSA:", culture);
            foreach (FsaReportRow row in presentation.Rows)
            {
                if (row.Kind == FsaReportRowKind.Layer || row.Kind == FsaReportRowKind.Undetected
                    || row.Kind == FsaReportRowKind.UndetectedFolded)
                {
                    Console.WriteLine("      {0,-12} {1} | {2}", row.Kind, FSAReportView.RowCaption(row), row.Value);
                }
            }

            string removed = FSAReportView.BetaOnlyRemovedCaption(result);
            if (removed != null)
            {
                Console.WriteLine("      блок качества: {0}", removed);
            }

            string excess = FSAReportView.BetaBranchExcessCaption(result);
            if (excess != null)
            {
                Console.WriteLine("      блок качества: {0}", excess);
            }

            // (`AMBER284`) шаг узлов — подсказка строк «только по бета» (своей строки блока нет)
            string knots = FSAReportView.BetaKnotCaption(result);
            if (knots != null)
            {
                Console.WriteLine("      подсказка строк «только по бета»: {0}", knots);
            }

            string sampleBeta = FSAReportView.SampleBetaText(result);
            if (sampleBeta != null)
            {
                Console.WriteLine("      блок качества: бета-излучение пробы — {0}", sampleBeta);
            }

            string sampleBetaTip = FSAReportView.SampleBetaTip(result);
            if (sampleBetaTip != null)
            {
                Console.WriteLine("      подсказка беты пробы: {0}", sampleBetaTip);
            }

            var texts = new FsaMeasurementResult.Texts { ContinuumOnly = culture == "en" ? "beta only" : "только по бета" };
            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection rows = FsaMeasurementResult.Build(rd, result, null, texts, out summary);
            if (rows != null)
            {
                foreach (MeasurementResult m in rows.ResultList)
                {
                    Console.WriteLine("      окно результата: {0}{1} — {2} (MDA {3})", m.Line.Name,
                                      string.IsNullOrEmpty(m.Line.Note) ? "" : " — " + m.Line.Note,
                                      F(m.ResultValue / Math.Max(1e-30, rows.CountingTime), 4),
                                      F(m.MDA / Math.Max(1e-30, rows.CountingTime), 4));
                }
            }
        }

        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    /// <summary>Пуассон: малые — произведением равномерных, большие — нормальным приближением с округлением.</summary>
    static int Poisson(Random random, double mean)
    {
        if (!(mean > 0.0))
        {
            return 0;
        }

        if (mean < 30.0)
        {
            double limit = Math.Exp(-mean), p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= random.NextDouble();
            }
            while (p > limit);
            return k - 1;
        }

        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * normal));
    }

    static string Pretty(string label)
    {
        string nucid = FsaSampleLibrary.NucidOf(label);
        return nucid.Length > 0 ? FsaSampleLibrary.PrettyName(nucid) : label;
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

    static int Done()
    {
        Console.WriteLine(failures == 0 ? "ПРОБА: нарушений нет" : "⛔ НАРУШЕНО: " + failures);
        return failures == 0 ? 0 : 1;
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

        if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            rd.FwhmCalibration = cfg.FwhmCalibration;
        }

        return rd;
    }
}
