using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace RjmcmcHarness
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Options options = Options.Parse(args);
                DiagnosticSnapshot diagnostics = DiagnosticSnapshot.Load(options.DiagnosticsPath);
                Environment.CurrentDirectory = options.WorkingDirectory;

                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();

                List<string> spectrumFiles = ResolveSpectrumFiles(options.InputPath);
                if (spectrumFiles.Count == 0)
                {
                    Console.Error.WriteLine("No spectrum XML files found.");
                    return 1;
                }

                foreach (string spectrumFile in spectrumFiles)
                {
                    Console.WriteLine("=== {0} ===", spectrumFile);
                    foreach (double minSnr in ResolveMinSnrValues(options, diagnostics))
                    {
                        RunScenario(spectrumFile, minSnr, options, diagnostics);
                    }

                    Console.WriteLine();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        static void RunScenario(string spectrumFile, double minSnr, Options options, DiagnosticSnapshot diagnostics)
        {
            ResultData resultData = LoadResultData(spectrumFile);
            FWHMPeakDetectionMethodConfig peakConfig = PreparePeakConfig(resultData, minSnr, options, diagnostics);
            bool useBackgroundSubtraction = ResolveBackgroundSubtraction(options, diagnostics);

            PeakDetector detector = new PeakDetector();
            object finder = InvokePrivateMethod(
                detector,
                "PeakFinder",
                resultData.EnergySpectrum,
                peakConfig,
                resultData.FwhmCalibration);

            object deconvolution = InvokeInternalStaticMethod(
                "BecquerelMonitor.RjmcmcDeconvolution.RjmcmcPeakDeconvolver",
                "Run",
                resultData.EnergySpectrum,
                useBackgroundSubtraction ? resultData.BackgroundEnergySpectrum : null,
                finder,
                peakConfig,
                resultData.FwhmCalibration);

            List<Peak> peaks = detector.DetectPeak(
                resultData,
                useBackgroundSubtraction ? BackgroundMode.Substract : BackgroundMode.Visible,
                SmoothingMethod.None,
                null);

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "MinSNR={0:F1}; Finder={1}; DeconvExtras={2}; FinalPeaks={3}",
                minSnr,
                CountArrayProperty(finder, "centroids"),
                CountListProperty(deconvolution, "ExtraCandidates"),
                peaks.Count));

            PrintDetectedPeaks(peaks);
            PrintExtraCandidates(deconvolution);
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
            FWHMPeakDetectionMethodConfig peakConfig = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            resultData.PeakDetectionMethodConfig = peakConfig != null
                ? (FWHMPeakDetectionMethodConfig)peakConfig.Clone()
                : (FWHMPeakDetectionMethodConfig)((FWHMPeakDetectionMethodConfig)deviceConfig.PeakDetectionMethodConfig).Clone();
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
            if (spectrum == null)
            {
                return;
            }

            if (spectrum.TotalPulseCount == 0 && spectrum.Spectrum != null)
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

        static FWHMPeakDetectionMethodConfig PreparePeakConfig(ResultData resultData, double minSnr, Options options, DiagnosticSnapshot diagnostics)
        {
            FWHMPeakDetectionMethodConfig config =
                (FWHMPeakDetectionMethodConfig)((FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig).Clone();
            config.Min_SNR = minSnr;

            ApplyDiagnostics(config, diagnostics);

            config.UseDeconvolution = options.UseDeconvolutionOverride ?? config.UseDeconvolution;
            config.BurnIn = options.BurnInOverride ?? config.BurnIn;
            config.Samples = options.SamplesOverride ?? config.Samples;
            config.MaxRois = options.MaxRoisOverride ?? config.MaxRois;
            config.MaxExtraPeaksPerRoi = options.MaxExtraPeaksPerRoiOverride ?? config.MaxExtraPeaksPerRoi;
            config.RoiRadiusFwhm = options.RoiRadiusFwhmOverride ?? config.RoiRadiusFwhm;
            config.Max_Items = options.MaxItemsOverride ?? config.Max_Items;
            config.Tolerance = options.ToleranceOverride ?? config.Tolerance;
            config.Min_Range = options.MinRangeOverride ?? config.Min_Range;
            config.Max_Range = options.MaxRangeOverride ?? config.Max_Range;
            config.Min_FWHM_Tol = options.MinFwhmToleranceOverride ?? config.Min_FWHM_Tol;
            config.Max_FWHM_Tol = options.MaxFwhmToleranceOverride ?? config.Max_FWHM_Tol;
            config.Ch_Concat = options.ChannelConcatOverride ?? config.Ch_Concat;
            config.MinDevianceImprovement = options.MinDevianceImprovementOverride ?? config.MinDevianceImprovement;
            config.MinimumCandidateAmplitude = options.MinimumCandidateAmplitudeOverride ?? config.MinimumCandidateAmplitude;

            resultData.PeakDetectionMethodConfig = config;
            return config;
        }

        static void ApplyDiagnostics(FWHMPeakDetectionMethodConfig config, DiagnosticSnapshot diagnostics)
        {
            if (config == null || diagnostics == null)
            {
                return;
            }

            config.Enabled = diagnostics.GetBool("peak_detection_config.enabled") ?? config.Enabled;
            config.UseDeconvolution = diagnostics.GetBool("peak_detection_config.use_deconvolution") ?? config.UseDeconvolution;
            config.Max_Items = diagnostics.GetInt("peak_detection_config.max_items") ?? config.Max_Items;
            config.Tolerance = diagnostics.GetDouble("peak_detection_config.tolerance_percent") ?? config.Tolerance;
            config.Min_Range = diagnostics.GetDouble("peak_detection_config.min_range_keV") ?? config.Min_Range;
            config.Max_Range = diagnostics.GetDouble("peak_detection_config.max_range_keV") ?? config.Max_Range;
            config.Min_FWHM_Tol = diagnostics.GetDecimal("peak_detection_config.min_fwhm_tol_percent") ?? config.Min_FWHM_Tol;
            config.Max_FWHM_Tol = diagnostics.GetDecimal("peak_detection_config.max_fwhm_tol_percent") ?? config.Max_FWHM_Tol;
            config.Ch_Concat = diagnostics.GetInt("peak_detection_config.channel_concat") ?? config.Ch_Concat;
            config.BurnIn = diagnostics.GetInt("peak_detection_config.burn_in") ?? config.BurnIn;
            config.Samples = diagnostics.GetInt("peak_detection_config.samples") ?? config.Samples;
            config.MaxRois = diagnostics.GetInt("peak_detection_config.max_rois") ?? config.MaxRois;
            config.MaxExtraPeaksPerRoi = diagnostics.GetInt("peak_detection_config.max_extra_peaks_per_roi") ?? config.MaxExtraPeaksPerRoi;
            config.RoiRadiusFwhm = diagnostics.GetDouble("peak_detection_config.roi_radius_fwhm") ?? config.RoiRadiusFwhm;
            config.MinDevianceImprovement = diagnostics.GetDouble("peak_detection_config.min_deviance_improvement") ?? config.MinDevianceImprovement;
            config.MinimumCandidateAmplitude = diagnostics.GetDouble("peak_detection_config.minimum_candidate_amplitude") ?? config.MinimumCandidateAmplitude;
        }

        static IEnumerable<double> ResolveMinSnrValues(Options options, DiagnosticSnapshot diagnostics)
        {
            if (options.MinSnrValuesOverride != null && options.MinSnrValuesOverride.Count > 0)
            {
                return options.MinSnrValuesOverride;
            }

            double? minSnr = diagnostics?.GetDouble("peak_detection_config.min_snr");
            if (minSnr.HasValue)
            {
                return new[] { minSnr.Value };
            }

            return new[] { 4.0, 6.0, 8.0, 10.0 };
        }

        static bool ResolveBackgroundSubtraction(Options options, DiagnosticSnapshot diagnostics)
        {
            if (options.UseBackgroundSubtractionOverride.HasValue)
            {
                return options.UseBackgroundSubtractionOverride.Value;
            }

            string backgroundMode = diagnostics?.GetString("view.background_mode");
            if (String.Equals(backgroundMode, BackgroundMode.Visible.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (String.Equals(backgroundMode, BackgroundMode.Substract.ToString(), StringComparison.OrdinalIgnoreCase) ||
                String.Equals(backgroundMode, "Subtract", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return true;
        }

        static void PrintDetectedPeaks(List<Peak> peaks)
        {
            Console.WriteLine("Final peaks:");
            foreach (Peak peak in peaks.OrderBy(candidate => candidate.Channel))
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  ch={0,5}  E={1,8:F2} keV  SNR={2,6:F2}  FWHM={3,6:F2}",
                    peak.Channel,
                    peak.Energy,
                    peak.SNR,
                    peak.FWHM));
            }
        }

        static void PrintExtraCandidates(object deconvolution)
        {
            Console.WriteLine("RJMCMC extras:");
            IEnumerable<object> candidates = GetEnumerableProperty(deconvolution, "ExtraCandidates");
            foreach (object candidate in candidates)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  ch={0,7:F2}  SNR={1,6:F2}  amp={2,10:F2}  deltaD={3,8:F2}  occ={4,6:F3}  std={5,6:F2}  residSNR={6,6:F2}  corr={7,6:F3}  dist={8,6:F3}  support={9}",
                    GetDoubleProperty(candidate, "Channel"),
                    GetDoubleProperty(candidate, "Snr"),
                    GetDoubleProperty(candidate, "Amplitude"),
                    GetDoubleProperty(candidate, "DevianceImprovement"),
                    GetDoubleProperty(candidate, "PosteriorOccupancy"),
                    GetDoubleProperty(candidate, "CenterStdDev"),
                    GetDoubleProperty(candidate, "ResidualSnr"),
                    GetDoubleProperty(candidate, "ResidualCorrelation"),
                    GetDoubleProperty(candidate, "AnchorDistanceFwhm"),
                    GetIntProperty(candidate, "SupportingChainCount")));
            }
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

        static object InvokePrivateMethod(object target, string methodName, params object[] parameters)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            return method.Invoke(target, parameters);
        }

        static object InvokeInternalStaticMethod(string typeName, string methodName, params object[] parameters)
        {
            Type type = typeof(PeakDetector).Assembly.GetType(typeName, throwOnError: true);
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method.Invoke(null, parameters);
        }

        static int CountArrayProperty(object instance, string propertyOrFieldName)
        {
            object value = GetPropertyOrField(instance, propertyOrFieldName);
            Array array = value as Array;
            return array?.Length ?? 0;
        }

        static int CountListProperty(object instance, string propertyName)
        {
            return GetEnumerableProperty(instance, propertyName).Count();
        }

        static IEnumerable<object> GetEnumerableProperty(object instance, string propertyName)
        {
            object value = GetPropertyOrField(instance, propertyName);
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (object item in enumerable)
            {
                yield return item;
            }
        }

        static double GetDoubleProperty(object instance, string propertyName)
        {
            object value = GetPropertyOrField(instance, propertyName);
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        static int GetIntProperty(object instance, string propertyName)
        {
            object value = GetPropertyOrField(instance, propertyName);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        static object GetPropertyOrField(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            throw new MissingMemberException(type.FullName, name);
        }

        sealed class Options
        {
            public string InputPath { get; private set; }
            public string WorkingDirectory { get; private set; }
            public string DiagnosticsPath { get; private set; }
            public List<double> MinSnrValuesOverride { get; private set; }
            public int? BurnInOverride { get; private set; }
            public int? SamplesOverride { get; private set; }
            public int? MaxRoisOverride { get; private set; }
            public int? MaxExtraPeaksPerRoiOverride { get; private set; }
            public double? RoiRadiusFwhmOverride { get; private set; }
            public int? MaxItemsOverride { get; private set; }
            public double? ToleranceOverride { get; private set; }
            public double? MinRangeOverride { get; private set; }
            public double? MaxRangeOverride { get; private set; }
            public decimal? MinFwhmToleranceOverride { get; private set; }
            public decimal? MaxFwhmToleranceOverride { get; private set; }
            public int? ChannelConcatOverride { get; private set; }
            public bool? UseDeconvolutionOverride { get; private set; }
            public double? MinDevianceImprovementOverride { get; private set; }
            public double? MinimumCandidateAmplitudeOverride { get; private set; }
            public bool? UseBackgroundSubtractionOverride { get; private set; }

            public static Options Parse(string[] args)
            {
                Options options = new Options
                {
                    InputPath = @"C:\Users\moroz\OneDrive\Desktop\Deconvolve\examples",
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    MinSnrValuesOverride = null
                };

                foreach (string arg in args ?? Array.Empty<string>())
                {
                    if (arg.StartsWith("--input=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.InputPath = arg.Substring("--input=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--workdir=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.WorkingDirectory = arg.Substring("--workdir=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--diagnostics=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.DiagnosticsPath = arg.Substring("--diagnostics=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--snr=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MinSnrValuesOverride = arg.Substring("--snr=".Length)
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(value => Double.Parse(value, CultureInfo.InvariantCulture))
                            .ToList();
                    }
                    else if (arg.StartsWith("--burnin=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.BurnInOverride = Int32.Parse(arg.Substring("--burnin=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--samples=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.SamplesOverride = Int32.Parse(arg.Substring("--samples=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-rois=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxRoisOverride = Int32.Parse(arg.Substring("--max-rois=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-extra=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxExtraPeaksPerRoiOverride = Int32.Parse(arg.Substring("--max-extra=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--roi-radius=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.RoiRadiusFwhmOverride = Double.Parse(arg.Substring("--roi-radius=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-items=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxItemsOverride = Int32.Parse(arg.Substring("--max-items=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--tolerance=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ToleranceOverride = Double.Parse(arg.Substring("--tolerance=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--min-range=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MinRangeOverride = Double.Parse(arg.Substring("--min-range=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-range=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxRangeOverride = Double.Parse(arg.Substring("--max-range=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--min-fwhm-tol=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MinFwhmToleranceOverride = Decimal.Parse(arg.Substring("--min-fwhm-tol=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-fwhm-tol=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxFwhmToleranceOverride = Decimal.Parse(arg.Substring("--max-fwhm-tol=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--channel-concat=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ChannelConcatOverride = Int32.Parse(arg.Substring("--channel-concat=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--use-deconvolution=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseDeconvolutionOverride = Boolean.Parse(arg.Substring("--use-deconvolution=".Length));
                    }
                    else if (arg.StartsWith("--min-dev=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MinDevianceImprovementOverride = Double.Parse(arg.Substring("--min-dev=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--min-amp=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MinimumCandidateAmplitudeOverride = Double.Parse(arg.Substring("--min-amp=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (String.Equals(arg, "--bg=visible", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseBackgroundSubtractionOverride = false;
                    }
                    else if (String.Equals(arg, "--bg=substract", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseBackgroundSubtractionOverride = true;
                    }
                }

                return options;
            }
        }

        sealed class DiagnosticSnapshot
        {
            readonly Dictionary<string, string> values;

            DiagnosticSnapshot(Dictionary<string, string> values)
            {
                this.values = values;
            }

            public static DiagnosticSnapshot Load(string path)
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine?.Trim();
                    if (String.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal) || (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    string value = line.Substring(separatorIndex + 1).Trim();
                    values[key] = value;
                }

                return new DiagnosticSnapshot(values);
            }

            public string GetString(string key)
            {
                string value;
                return this.values != null && this.values.TryGetValue(key, out value)
                    ? value
                    : null;
            }

            public bool? GetBool(string key)
            {
                string value = GetString(key);
                if (String.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                bool parsed;
                return Boolean.TryParse(value, out parsed)
                    ? parsed
                    : (bool?)null;
            }

            public int? GetInt(string key)
            {
                string value = GetString(key);
                if (String.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                int parsed;
                return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                    ? parsed
                    : (int?)null;
            }

            public double? GetDouble(string key)
            {
                string value = GetString(key);
                if (String.IsNullOrWhiteSpace(value) || String.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                double parsed;
                return Double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed)
                    ? parsed
                    : (double?)null;
            }

            public decimal? GetDecimal(string key)
            {
                string value = GetString(key);
                if (String.IsNullOrWhiteSpace(value) || String.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                decimal parsed;
                return Decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed)
                    ? parsed
                    : (decimal?)null;
            }
        }
    }
}
