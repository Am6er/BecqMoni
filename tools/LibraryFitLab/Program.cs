using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace LibraryFitLab
{
    /// <summary>
    /// Headless driver for the nuclide-set / deconvolution parameter study.
    ///
    /// Unlike tools/RjmcmcHarness (one scenario per process, human-readable
    /// output) this one loads a spectrum once and then runs PeakDetector.DetectPeak
    /// over a whole list of nuclide sets and deconvolution configurations in the
    /// same process, writing one CSV row per detected peak. A sweep over
    /// (set composition x deconvolution parameters) is thousands of runs, and
    /// paying the config + spectrum load on each of them is what made the
    /// obvious "one process per point" approach unusable.
    ///
    /// Build (after the main project):
    ///   csc /target:exe /platform:anycpu /langversion:7.3 /out:&lt;out&gt;\LibraryFitLab.exe
    ///       /r:&lt;out&gt;\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll
    ///       /r:System.Xml.dll /r:System.Xml.Serialization.dll /r:System.Drawing.dll
    ///       /r:System.Windows.Forms.dll tools\LibraryFitLab\Program.cs
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Options options = Options.Parse(args);
                Environment.CurrentDirectory = options.WorkingDirectory;

                ApplyGate(options.Gate, options.ShapeZ, options.ShapeWindow, options.ShapeFlank,
                          options.ShapeOrder, options.ChainVeto, options.ChainScatter,
                          options.ChainMinLines, options.AbsenceMiss, options.AbsenceSigma,
                          options.TrimFraction, options.TrimGrubbs);

                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();

                List<NuclideSet> sets = ResolveSets(nuclideManager, options.SetNames);
                if (options.SetNames.Count > 0 && sets.Count == 0)
                {
                    Console.Error.WriteLine("None of the requested nuclide sets exist.");
                    return 1;
                }

                using (StreamWriter peaksWriter = OpenWriter(options.PeaksPath))
                using (StreamWriter runsWriter = OpenWriter(options.RunsPath))
                {
                    WriteLine(peaksWriter, "run,spectrum,set,gate,snr,deconv,roi,extra,channel,energy,peak_snr,fwhm,origin,anchor,nuclide,nuclide_energy");
                    WriteLine(runsWriter, "run,spectrum,set,gate,snr,deconv,roi,extra,burnin,samples,maxrois,mindev,minamp,ms,n_total,n_finder,n_rjmcmc,n_library,n_anchor,set_lines");

                    int run = 0;
                    foreach (string spectrumFile in ResolveSpectrumFiles(options.InputPath))
                    {
                        string spectrumName = Path.GetFileNameWithoutExtension(spectrumFile);
                        foreach (Scenario scenario in options.Scenarios)
                        {
                            // A null set entry means "run without any nuclide set"
                            // - the finder/deconvolution-only baseline.
                            foreach (NuclideSet set in EnumerateSets(sets, options.IncludeNoSet))
                            {
                                run++;
                                RunOne(run, spectrumFile, spectrumName, set, scenario, options,
                                       nuclideManager, peaksWriter, runsWriter);
                            }
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        // Какой гейт значимости проверяет библиотечный фит. Держится ключом, а не
        // пересборкой, чтобы все критерии мерились на одних и тех же спектрах и
        // одних и тех же сетах-обманках в одном свипе.
        //   z        - Fisher z фитованной амплитуды (исходный критерий)
        //   dd       - только тест отношения правдоподобий ΔD
        //   shape    - только устойчивость к смене модели фона
        //   dd+shape - ΔD как дешёвый предварительный отсев, затем устойчивость
        //   chain    - z по линии плюс вето по согласованности набора (умолчание)
        //   dd+shape+chain - всё сразу
        static void ApplyGate(string gate, double? shapeZ, double? window, double? flank, int? order,
                              bool? chainVeto, double? chainScatter, int? chainMinLines,
                              double? absenceMiss, double? absenceSigma,
                              double? trimFraction, double? trimGrubbs)
        {
            if (trimFraction.HasValue) LibraryPeakFitter.OutlierTrimMaxFraction = trimFraction.Value;
            if (trimGrubbs.HasValue) LibraryPeakFitter.OutlierTrimGrubbsK = trimGrubbs.Value;
            if (absenceMiss.HasValue) LibraryPeakFitter.AbsenceMissLimit = absenceMiss.Value;
            if (absenceSigma.HasValue) LibraryPeakFitter.AbsenceVisibleSigma = absenceSigma.Value;
            if (chainMinLines.HasValue)
            {
                LibraryPeakFitter.ChainConsistencyMinLines = chainMinLines.Value;
            }
            if (shapeZ.HasValue)
            {
                LibraryPeakFitter.BackgroundShapeZ = shapeZ.Value;
            }
            if (window.HasValue) LibraryPeakFitter.ShapeWindowSigma = window.Value;
            if (flank.HasValue) LibraryPeakFitter.ShapeFlankSigma = flank.Value;
            if (order.HasValue) LibraryPeakFitter.ShapeMaxOrder = order.Value;
            if (chainVeto.HasValue) LibraryPeakFitter.UseChainConsistencyVeto = chainVeto.Value;
            if (chainScatter.HasValue) LibraryPeakFitter.ChainScatterLimit = chainScatter.Value;

            LibraryPeakFitter.UseChainVetoFallback = false;
            LibraryPeakFitter.UseAbsenceVeto = false;
            LibraryPeakFitter.UseOutlierTrim = false;
            switch (gate)
            {
                case "z":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = false;
                    break;
                case "dd":
                    LibraryPeakFitter.UseDevianceGate = true;
                    LibraryPeakFitter.UseBackgroundShapeGate = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = false;
                    break;
                case "shape":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = true;
                    LibraryPeakFitter.UseChainConsistencyVeto = false;
                    break;
                case "shape-raw":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = true;
                    LibraryPeakFitter.ShapeGateSubtractNeighbours = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = false;
                    break;
                case "dd+shape":
                    LibraryPeakFitter.UseDevianceGate = true;
                    LibraryPeakFitter.UseBackgroundShapeGate = true;
                    LibraryPeakFitter.UseChainConsistencyVeto = false;
                    break;
                // Связка «устойчивость к фону + вето по набору». В журнале её не
                // было: там мерилось только «всё» (dd+shape+chain), и вывод
                // «строгие критерии морят вето голодом» сделан по нему. Но голод
                // создаёт прежде всего dd — он несёт тот же дефект
                // фиксированного континуума, что и z, и режет линии, ничего не
                // добавляя. Отчёт Verter73 к PR #32 показал, что на германии
                // shape даёт вдвенадцатеро меньше фантомов, чем вето, при том же
                // recall, — значит комбинацию без dd надо померить отдельно.
                // Вето с запасным критерием: shape включается ТОЛЬКО там, где
                // вето воздержалось или сняло набор. Это и есть конструкция,
                // которую поддерживают замеры по детекторам.
                // Вето по разбросу + вето по отсутствиям + запасной критерий.
                // Полная связка: вето по разбросу с поимённым исключением
                // выбросов + вето по отсутствиям + запасной критерий.
                case "chain+trim":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = true;
                    LibraryPeakFitter.UseChainVetoFallback = true;
                    LibraryPeakFitter.UseAbsenceVeto = true;
                    LibraryPeakFitter.UseOutlierTrim = true;
                    break;
                case "chain+absence":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = true;
                    LibraryPeakFitter.UseChainVetoFallback = true;
                    LibraryPeakFitter.UseAbsenceVeto = true;
                    break;
                case "chain+fallback":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = true;
                    LibraryPeakFitter.UseChainVetoFallback = true;
                    break;
                case "shape+chain":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = true;
                    LibraryPeakFitter.UseChainConsistencyVeto = true;
                    break;
                case "dd+shape+chain":
                    LibraryPeakFitter.UseDevianceGate = true;
                    LibraryPeakFitter.UseBackgroundShapeGate = true;
                    LibraryPeakFitter.UseChainConsistencyVeto = true;
                    break;
                case "chain":
                    LibraryPeakFitter.UseDevianceGate = false;
                    LibraryPeakFitter.UseBackgroundShapeGate = false;
                    LibraryPeakFitter.UseChainConsistencyVeto = true;
                    break;
                default:
                    throw new ArgumentException("unknown --gate: " + gate);
            }
        }

        static IEnumerable<NuclideSet> EnumerateSets(List<NuclideSet> sets, bool includeNoSet)
        {
            if (includeNoSet)
            {
                yield return null;
            }

            foreach (NuclideSet set in sets)
            {
                yield return set;
            }
        }

        static void RunOne(
            int run,
            string spectrumFile,
            string spectrumName,
            NuclideSet set,
            Scenario scenario,
            Options options,
            NuclideDefinitionManager nuclideManager,
            StreamWriter peaksWriter,
            StreamWriter runsWriter)
        {
            ResultData resultData = LoadResultData(spectrumFile);
            FWHMPeakDetectionMethodConfig config = PreparePeakConfig(resultData, scenario, options);

            Stopwatch watch = Stopwatch.StartNew();
            List<Peak> peaks;
            try
            {
                peaks = new PeakDetector().DetectPeak(
                    resultData,
                    options.SubtractBackground ? BackgroundMode.Substract : BackgroundMode.Visible,
                    SmoothingMethod.None,
                    set);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("run {0} {1} / {2} failed: {3}", run, spectrumName, set?.Name ?? "-", ex.Message);
                return;
            }
            watch.Stop();

            int setLines = set == null
                ? 0
                : nuclideManager.NuclideDefinitions.Count(n => n != null && n.Sets != null && n.Sets.Contains(set.Id));

            string prefix = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7}",
                run, Csv(spectrumName), Csv(set?.Name ?? "-"), Csv(options.Gate),
                scenario.MinSnr.ToString("F2", CultureInfo.InvariantCulture),
                scenario.UseDeconvolution ? 1 : 0,
                scenario.RoiRadiusFwhm.ToString("F2", CultureInfo.InvariantCulture),
                scenario.MaxExtraPeaksPerRoi);

            foreach (Peak peak in peaks.OrderBy(p => p.Channel))
            {
                WriteLine(peaksWriter, string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2:F3},{3:F3},{4:F3},{5},{6},{7},{8}",
                    prefix,
                    peak.Channel,
                    peak.Energy,
                    peak.SNR,
                    peak.FWHM,
                    peak.PeakSearchOrigin,
                    peak.IsLibraryAnchor ? 1 : 0,
                    Csv(peak.Nuclide?.Name ?? ""),
                    peak.Nuclide != null
                        ? peak.Nuclide.Energy.ToString("F3", CultureInfo.InvariantCulture)
                        : ""));
            }

            WriteLine(runsWriter, string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
                prefix,
                config.BurnIn, config.Samples, config.MaxRois,
                config.MinDevianceImprovement.ToString("F3", CultureInfo.InvariantCulture),
                config.MinimumCandidateAmplitude.ToString("F3", CultureInfo.InvariantCulture),
                watch.ElapsedMilliseconds,
                peaks.Count,
                peaks.Count(p => p.PeakSearchOrigin == PeakSearchOrigin.FWHMPeakFinder),
                peaks.Count(p => p.PeakSearchOrigin == PeakSearchOrigin.RJMCMC),
                peaks.Count(p => p.PeakSearchOrigin == PeakSearchOrigin.Library),
                peaks.Count(p => p.IsLibraryAnchor),
                setLines));

            peaksWriter?.Flush();
            runsWriter?.Flush();
        }

        static List<NuclideSet> ResolveSets(NuclideDefinitionManager manager, List<string> names)
        {
            if (names.Count == 0)
            {
                return new List<NuclideSet>();
            }

            List<NuclideSet> sets = new List<NuclideSet>();
            foreach (string name in names)
            {
                NuclideSet set = manager.NuclideSets.FirstOrDefault(
                    candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (set == null)
                {
                    Console.Error.WriteLine("Nuclide set not found: " + name);
                    continue;
                }

                sets.Add(set);
            }

            return sets;
        }

        static ResultData LoadResultData(string spectrumFile)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (FileStream stream = new FileStream(spectrumFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData resultData = file.ResultDataList.First();
            EnsureSpectrumIntegrity(resultData.EnergySpectrum);
            EnsureSpectrumIntegrity(resultData.BackgroundEnergySpectrum);

            DeviceConfigInfo deviceConfig = DeviceConfigManager.GetInstance().DeviceConfigList
                .FirstOrDefault(candidate => candidate.Guid == resultData.DeviceConfigReference.Guid);
            if (deviceConfig == null)
            {
                throw new InvalidOperationException("Device config not found for " + resultData.DeviceConfigReference.Guid);
            }

            resultData.DeviceConfig = deviceConfig;
            // Настройки поиска берутся из конфигурации УСТРОЙСТВА, а не из
            // ResultData. Раньше здесь стояло "из файла, если есть, иначе из
            // устройства", но ветка с устройством была недостижима:
            // ResultData.PeakDetectionMethodConfig помечен [XmlIgnore] и
            // инициализирован новым FWHMPeakDetectionMethodConfig, то есть из
            // файла не читается никогда, а null не бывает. Все прогоны молча
            // шли на умолчаниях класса (Min_Range = 30, Max_Range = 2800,
            // Ch_Concat = 1024). Для 8192-канальных сцинтилляторов это близко к
            // правде — 14 каналов на полуширину, — поэтому расхождения не было
            // видно; на германии 16384 канала пересыпались в 1024, полуширина
            // становилась меньше канала, и финдер не находил НИ ОДНОГО пика.
            resultData.PeakDetectionMethodConfig =
                (FWHMPeakDetectionMethodConfig)((FWHMPeakDetectionMethodConfig)deviceConfig.PeakDetectionMethodConfig).Clone();
            resultData.ROIConfig = null;

            if (resultData.FwhmCalibration == null)
            {
                FWHMPeakDetectionMethodConfig fwhmPeakConfig = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
                resultData.FwhmCalibration = fwhmPeakConfig.FwhmCalibration?.Clone() ??
                    FwhmCalibration.DefaultCalibration(fwhmPeakConfig, resultData.EnergySpectrum.EnergyCalibration);
            }

            return resultData;
        }

        static void EnsureSpectrumIntegrity(EnergySpectrum spectrum)
        {
            if (spectrum?.Spectrum == null)
            {
                return;
            }

            if (spectrum.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < spectrum.Spectrum.Length; i++)
                {
                    total += spectrum.Spectrum[i];
                }

                spectrum.TotalPulseCount = total;
                spectrum.ValidPulseCount = total;
            }
        }

        static FWHMPeakDetectionMethodConfig PreparePeakConfig(ResultData resultData, Scenario scenario, Options options)
        {
            FWHMPeakDetectionMethodConfig config =
                (FWHMPeakDetectionMethodConfig)((FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig).Clone();

            config.Min_SNR = scenario.MinSnr;
            config.UseDeconvolution = scenario.UseDeconvolution;
            config.RoiRadiusFwhm = scenario.RoiRadiusFwhm;
            config.MaxExtraPeaksPerRoi = scenario.MaxExtraPeaksPerRoi;
            if (scenario.BurnIn.HasValue) config.BurnIn = scenario.BurnIn.Value;
            if (scenario.Samples.HasValue) config.Samples = scenario.Samples.Value;
            if (scenario.MaxRois.HasValue) config.MaxRois = scenario.MaxRois.Value;
            if (scenario.MinDeviance.HasValue) config.MinDevianceImprovement = scenario.MinDeviance.Value;
            if (scenario.MinAmplitude.HasValue) config.MinimumCandidateAmplitude = scenario.MinAmplitude.Value;
            if (options.MinRange.HasValue) config.Min_Range = options.MinRange.Value;
            if (options.MaxRange.HasValue) config.Max_Range = options.MaxRange.Value;
            if (options.Tolerance.HasValue) config.Tolerance = options.Tolerance.Value;
            if (options.MaxItems.HasValue) config.Max_Items = options.MaxItems.Value;

            resultData.PeakDetectionMethodConfig = config;
            return config;
        }

        static List<string> ResolveSpectrumFiles(string inputPath)
        {
            if (File.Exists(inputPath))
            {
                return new List<string> { Path.GetFullPath(inputPath) };
            }

            if (!Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException(inputPath);
            }

            return Directory.GetFiles(inputPath, "*.xml")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static StreamWriter OpenWriter(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new StreamWriter(path, false, new UTF8Encoding(false));
        }

        static void WriteLine(StreamWriter writer, string line)
        {
            if (writer != null)
            {
                writer.WriteLine(line);
            }
            else
            {
                Console.WriteLine(line);
            }
        }

        static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        sealed class Scenario
        {
            public double MinSnr = 4.0;
            public bool UseDeconvolution;
            public double RoiRadiusFwhm = 3.0;
            public int MaxExtraPeaksPerRoi = 3;
            public int? BurnIn;
            public int? Samples;
            public int? MaxRois;
            public double? MinDeviance;
            public double? MinAmplitude;
        }

        sealed class Options
        {
            public string InputPath;
            public string WorkingDirectory = Directory.GetCurrentDirectory();
            public string PeaksPath;
            public string RunsPath;
            public List<string> SetNames = new List<string>();
            public List<Scenario> Scenarios = new List<Scenario>();
            public bool IncludeNoSet;
            public bool SubtractBackground = true;
            public double? MinRange;
            public double? MaxRange;
            public double? Tolerance;
            public int? MaxItems;
            public string Gate = "chain";
            public double? ShapeZ;
            public double? ShapeWindow;
            public double? ShapeFlank;
            public int? ShapeOrder;
            public bool? ChainVeto;
            public double? ChainScatter;
            public int? ChainMinLines;
            public double? AbsenceMiss;
            public double? AbsenceSigma;
            public double? TrimFraction;
            public double? TrimGrubbs;

            public static Options Parse(string[] args)
            {
                Options options = new Options();
                List<double> snrs = new List<double> { 4.0 };
                List<double> rois = new List<double> { 3.0 };
                List<int> extras = new List<int> { 3 };
                List<bool> deconv = new List<bool> { false };
                int? burnIn = null, samples = null, maxRois = null;
                double? minDev = null, minAmp = null;

                foreach (string arg in args ?? Array.Empty<string>())
                {
                    if (TryValue(arg, "--input=", out string value)) options.InputPath = value;
                    else if (TryValue(arg, "--workdir=", out value)) options.WorkingDirectory = value;
                    else if (TryValue(arg, "--peaks=", out value)) options.PeaksPath = value;
                    else if (TryValue(arg, "--runs=", out value)) options.RunsPath = value;
                    else if (TryValue(arg, "--sets=", out value)) options.SetNames = Split(value).ToList();
                    else if (TryValue(arg, "--snr=", out value)) snrs = Split(value).Select(ParseDouble).ToList();
                    else if (TryValue(arg, "--roi-radius=", out value)) rois = Split(value).Select(ParseDouble).ToList();
                    else if (TryValue(arg, "--max-extra=", out value)) extras = Split(value).Select(ParseInt).ToList();
                    else if (TryValue(arg, "--deconv=", out value)) deconv = Split(value).Select(v => bool.Parse(v)).ToList();
                    else if (TryValue(arg, "--burnin=", out value)) burnIn = ParseInt(value);
                    else if (TryValue(arg, "--samples=", out value)) samples = ParseInt(value);
                    else if (TryValue(arg, "--max-rois=", out value)) maxRois = ParseInt(value);
                    else if (TryValue(arg, "--min-dev=", out value)) minDev = ParseDouble(value);
                    else if (TryValue(arg, "--min-amp=", out value)) minAmp = ParseDouble(value);
                    else if (TryValue(arg, "--min-range=", out value)) options.MinRange = ParseDouble(value);
                    else if (TryValue(arg, "--max-range=", out value)) options.MaxRange = ParseDouble(value);
                    else if (TryValue(arg, "--tolerance=", out value)) options.Tolerance = ParseDouble(value);
                    else if (TryValue(arg, "--max-items=", out value)) options.MaxItems = ParseInt(value);
                    else if (TryValue(arg, "--gate=", out value)) options.Gate = value.Trim().ToLowerInvariant();
                    else if (TryValue(arg, "--shape-z=", out value)) options.ShapeZ = ParseDouble(value);
                    else if (TryValue(arg, "--shape-window=", out value)) options.ShapeWindow = ParseDouble(value);
                    else if (TryValue(arg, "--shape-flank=", out value)) options.ShapeFlank = ParseDouble(value);
                    else if (TryValue(arg, "--shape-order=", out value)) options.ShapeOrder = ParseInt(value);
                    else if (TryValue(arg, "--chain-veto=", out value)) options.ChainVeto = bool.Parse(value);
                    else if (TryValue(arg, "--chain-scatter=", out value)) options.ChainScatter = ParseDouble(value);
                    else if (TryValue(arg, "--chain-min-lines=", out value)) options.ChainMinLines = int.Parse(value);
                    else if (TryValue(arg, "--absence-miss=", out value)) options.AbsenceMiss = ParseDouble(value);
                    else if (TryValue(arg, "--absence-sigma=", out value)) options.AbsenceSigma = ParseDouble(value);
                    else if (TryValue(arg, "--trim-fraction=", out value)) options.TrimFraction = ParseDouble(value);
                    else if (TryValue(arg, "--trim-grubbs=", out value)) options.TrimGrubbs = ParseDouble(value);
                    else if (string.Equals(arg, "--no-set", StringComparison.OrdinalIgnoreCase)) options.IncludeNoSet = true;
                    else if (string.Equals(arg, "--bg=visible", StringComparison.OrdinalIgnoreCase)) options.SubtractBackground = false;
                    else if (string.Equals(arg, "--bg=substract", StringComparison.OrdinalIgnoreCase)) options.SubtractBackground = true;
                }

                foreach (bool useDeconvolution in deconv)
                {
                    foreach (double snr in snrs)
                    {
                        // The ROI radius and extra-peak budget only exist for the
                        // deconvolution, so the finder-only rows are not multiplied
                        // by them.
                        foreach (double roi in useDeconvolution ? rois : rois.Take(1))
                        {
                            foreach (int extra in useDeconvolution ? extras : extras.Take(1))
                            {
                                options.Scenarios.Add(new Scenario
                                {
                                    MinSnr = snr,
                                    UseDeconvolution = useDeconvolution,
                                    RoiRadiusFwhm = roi,
                                    MaxExtraPeaksPerRoi = extra,
                                    BurnIn = burnIn,
                                    Samples = samples,
                                    MaxRois = maxRois,
                                    MinDeviance = minDev,
                                    MinAmplitude = minAmp,
                                });
                            }
                        }
                    }
                }

                return options;
            }

            static bool TryValue(string arg, string prefix, out string value)
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = arg.Substring(prefix.Length).Trim('"');
                    return true;
                }

                value = null;
                return false;
            }

            static IEnumerable<string> Split(string value)
            {
                return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length > 0);
            }

            static double ParseDouble(string value)
            {
                return double.Parse(value, CultureInfo.InvariantCulture);
            }

            static int ParseInt(string value)
            {
                return int.Parse(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
