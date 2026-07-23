using BecquerelMonitor;
using BecquerelMonitor.Utils;
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
                    if (!String.IsNullOrEmpty(options.OracleSeries))
                    {
                        RunOracleScenario(spectrumFile, options.OracleSeries, ResolveMinSnrValues(options, diagnostics).First(), options, diagnostics);
                        Console.WriteLine();
                        continue;
                    }

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

            NuclideSet nuclideSet = ResolveNuclideSet(options);
            List<Peak> peaks = detector.DetectPeak(
                resultData,
                useBackgroundSubtraction ? BackgroundMode.Substract : BackgroundMode.Visible,
                SmoothingMethod.None,
                nuclideSet);

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "MinSNR={0:F1}; Finder={1}; DeconvExtras={2}; LibraryPeaks={3}; FinalPeaks={4}",
                minSnr,
                CountArrayProperty(finder, "centroids"),
                CountListProperty(deconvolution, "ExtraCandidates"),
                peaks.Count(p => p.PeakSearchOrigin == PeakSearchOrigin.Library),
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
                    "  ch={0,5}  E={1,8:F2} keV  SNR={2,6:F2}  FWHM={3,6:F2}{4}",
                    peak.Channel,
                    peak.Energy,
                    peak.SNR,
                    peak.FWHM,
                    peak.PeakSearchOrigin == PeakSearchOrigin.Library
                        ? string.Format(CultureInfo.InvariantCulture, "  [LIB {0}]", peak.Nuclide?.Name ?? "?")
                        : ""));
            }
        }

        // Resolves the nuclide set named by --nuclide-set= and flags --anchor= line
        // energies as IsAnchor in memory (the user's NuclideDefinition.xml is not saved).
        static NuclideSet ResolveNuclideSet(Options options)
        {
            if (String.IsNullOrEmpty(options.NuclideSetName))
            {
                return null;
            }

            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            NuclideSet nuclideSet = manager.NuclideSets
                .FirstOrDefault(set => String.Equals(set.Name, options.NuclideSetName, StringComparison.OrdinalIgnoreCase));
            if (nuclideSet == null)
            {
                Console.Error.WriteLine("Nuclide set not found: " + options.NuclideSetName);
                return null;
            }

            if (options.AnchorEnergies != null && options.AnchorEnergies.Count > 0)
            {
                foreach (NuclideDefinition nuclide in manager.NuclideDefinitions)
                {
                    if (nuclide.Sets == null || !nuclide.Sets.Contains(nuclideSet.Id))
                    {
                        continue;
                    }

                    if (options.AnchorEnergies.Any(energy => Math.Abs(nuclide.Energy - energy) <= 2.0))
                    {
                        nuclide.IsAnchor = true;
                        Console.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "Anchor: {0} {1:F2} keV",
                            nuclide.Name,
                            nuclide.Energy));
                    }
                }
            }

            return nuclideSet;
        }

        static void PrintExtraCandidates(object deconvolution)
        {
            Console.WriteLine("RJMCMC extras:");
            IEnumerable<object> candidates = GetEnumerableProperty(deconvolution, "ExtraCandidates");
            foreach (object candidate in candidates)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  ch={0,7:F2}  SNR={1,6:F2}  amp={2,10:F2}  deltaD={3,8:F2}  occ={4,6:F3}  std={5,6:F2}  residSNR={6,6:F2}  corr={7,6:F3}  dist={8,6:F3}  support={9}  roi=[{10}..{11}]  local={12}  halo={13}",
                    GetDoubleProperty(candidate, "Channel"),
                    GetDoubleProperty(candidate, "Snr"),
                    GetDoubleProperty(candidate, "Amplitude"),
                    GetDoubleProperty(candidate, "DevianceImprovement"),
                    GetDoubleProperty(candidate, "PosteriorOccupancy"),
                    GetDoubleProperty(candidate, "CenterStdDev"),
                    GetDoubleProperty(candidate, "ResidualSnr"),
                    GetDoubleProperty(candidate, "ResidualCorrelation"),
                    GetDoubleProperty(candidate, "AnchorDistanceFwhm"),
                    GetIntProperty(candidate, "SupportingChainCount"),
                    GetIntProperty(candidate, "RoiStartChannel"),
                    GetIntProperty(candidate, "RoiEndChannel"),
                    JoinInts(GetIntArrayProperty(candidate, "LocalAnchorChannels")),
                    JoinInts(GetIntArrayProperty(candidate, "HaloAnchorChannels"))));
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

        static int[] GetIntArrayProperty(object instance, string propertyName)
        {
            return GetPropertyOrField(instance, propertyName) as int[] ?? Array.Empty<int>();
        }

        static string JoinInts(IEnumerable<int> values)
        {
            return "[" + String.Join(",", values ?? Enumerable.Empty<int>()) + "]";
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

        // ===================== Oracle mode =====================
        // Fixed peak positions from evaluated decay-series tables (DDEP/ENSDF), free amplitudes,
        // fitted by Poisson likelihood on the same model stack the production deconvolution uses
        // (PeakShapeModel profiles, SASNIP continuum coeff=2, scaled instrument background envelope).
        // Compares the "oracle" model against the production final-peak model on the same data.

        sealed class OracleLine
        {
            public double Energy;
            public string Label;
            public OracleLine(double energy, string label) { Energy = energy; Label = label; }
        }

        sealed class FitComponent
        {
            public string Label;
            public double Energy;
            public int Channel;
            public double Fwhm;
            public int Start;
            public double[] Profile;
            public double Amplitude;
            public bool Frozen;
        }

        static readonly OracleLine[] OracleTh232 = new[]
        {
            new OracleLine(129.1,"Ac228"), new OracleLine(209.3,"Ac228"), new OracleLine(238.6,"Pb212"),
            new OracleLine(241.0,"Ra224"), new OracleLine(270.2,"Ac228"), new OracleLine(277.4,"Tl208"),
            new OracleLine(300.1,"Pb212"), new OracleLine(328.0,"Ac228"), new OracleLine(338.3,"Ac228"),
            new OracleLine(409.5,"Ac228"), new OracleLine(463.0,"Ac228"), new OracleLine(583.2,"Tl208"),
            new OracleLine(727.3,"Bi212"), new OracleLine(755.3,"Ac228"), new OracleLine(772.3,"Ac228"),
            new OracleLine(785.4,"Bi212"), new OracleLine(794.9,"Ac228"), new OracleLine(830.5,"Ac228"),
            new OracleLine(835.7,"Ac228"), new OracleLine(860.6,"Tl208"), new OracleLine(911.2,"Ac228"),
            new OracleLine(964.8,"Ac228"), new OracleLine(969.0,"Ac228"), new OracleLine(1588.2,"Ac228"),
            new OracleLine(1620.5,"Bi212"), new OracleLine(1630.6,"Ac228"), new OracleLine(2614.5,"Tl208"),
            new OracleLine(511.0,"annih"), new OracleLine(1460.8,"K40"),
        };

        static readonly OracleLine[] OracleRa226 = new[]
        {
            new OracleLine(186.2,"Ra226"), new OracleLine(242.0,"Pb214"), new OracleLine(295.2,"Pb214"),
            new OracleLine(351.9,"Pb214"), new OracleLine(609.3,"Bi214"), new OracleLine(665.4,"Bi214"),
            new OracleLine(768.4,"Bi214"), new OracleLine(786.0,"Pb214"), new OracleLine(806.2,"Bi214"),
            new OracleLine(934.1,"Bi214"), new OracleLine(1120.3,"Bi214"), new OracleLine(1155.2,"Bi214"),
            new OracleLine(1238.1,"Bi214"), new OracleLine(1280.9,"Bi214"), new OracleLine(1377.7,"Bi214"),
            new OracleLine(1401.5,"Bi214"), new OracleLine(1408.0,"Bi214"), new OracleLine(1509.2,"Bi214"),
            new OracleLine(1661.3,"Bi214"), new OracleLine(1729.6,"Bi214"), new OracleLine(1764.5,"Bi214"),
            new OracleLine(1847.4,"Bi214"), new OracleLine(2118.5,"Bi214"), new OracleLine(2204.2,"Bi214"),
            new OracleLine(2447.9,"Bi214"), new OracleLine(511.0,"annih"), new OracleLine(1460.8,"K40"),
        };

        static void RunOracleScenario(string spectrumFile, string series, double minSnr, Options options, DiagnosticSnapshot diagnostics)
        {
            ResultData resultData = LoadResultData(spectrumFile);
            FWHMPeakDetectionMethodConfig peakConfig = PreparePeakConfig(resultData, minSnr, options, diagnostics);
            bool useBackgroundSubtraction = ResolveBackgroundSubtraction(options, diagnostics);
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            FwhmCalibration fwhmCalibration = resultData.FwhmCalibration;
            int channels = spectrum.NumberOfChannels;

            // Production final peaks (base pipeline as committed).
            PeakDetector detector = new PeakDetector();
            List<Peak> basePeaks = detector.DetectPeak(
                resultData,
                useBackgroundSubtraction ? BackgroundMode.Substract : BackgroundMode.Visible,
                SmoothingMethod.None,
                null);

            // SNIP continuum exactly as production BuildSnipContinuum (finder anchors, coeff 2.0).
            object finder = InvokePrivateMethod(detector, "PeakFinder", spectrum, peakConfig, fwhmCalibration);
            double[] centroids = GetPropertyOrField(finder, "centroids") as double[] ?? new double[0];
            double[] finderFwhms = GetPropertyOrField(finder, "fwhms") as double[] ?? new double[0];
            List<Peak> anchorPeaks = new List<Peak>();
            for (int i = 0; i < centroids.Length; i++)
            {
                anchorPeaks.Add(new Peak
                {
                    Channel = Convert.ToInt32(Math.Round(centroids[i])),
                    FWHM = i < finderFwhms.Length ? finderFwhms[i] : 0.0
                });
            }

            SpectrumAriphmetics ariphmetics = new SpectrumAriphmetics(fwhmCalibration, spectrum, SmoothingMethod.None);
            int[] snip;
            try
            {
                snip = ariphmetics.Continuum(anchorPeaks, 2.0).Spectrum;
            }
            finally
            {
                ariphmetics.Dispose();
            }

            double[] fixedBackground = BuildOracleFixedBackground(
                spectrum,
                useBackgroundSubtraction ? resultData.BackgroundEnergySpectrum : null,
                snip);

            int chMin = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Min_Range, maxChannels: channels));
            int chMax = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Max_Range, maxChannels: channels));
            if (chMax < chMin) { int t = chMin; chMin = chMax; chMax = t; }

            int[] observed = spectrum.Spectrum;

            // Null model: fixed background only.
            double devianceNull = PoissonDeviance(observed, BuildLambda(fixedBackground, new List<FitComponent>(), channels), chMin, chMax);

            // Base model: components at production final-peak channels, amplitudes refit.
            List<FitComponent> baseComponents = new List<FitComponent>();
            foreach (Peak peak in basePeaks.OrderBy(p => p.Channel))
            {
                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, peak.Channel,
                    String.Format(CultureInfo.InvariantCulture, "det@{0:F0}", peak.Energy), peak.Energy);
                if (component != null) baseComponents.Add(component);
            }

            double devianceBase = FitAmplitudes(observed, fixedBackground, baseComponents, chMin, chMax, 300);

            // Oracle model: components at tabulated line energies, amplitudes free.
            OracleLine[] lines = String.Equals(series, "th", StringComparison.OrdinalIgnoreCase) ? OracleTh232 : OracleRa226;
            List<FitComponent> oracleComponents = new List<FitComponent>();
            foreach (OracleLine line in lines.OrderBy(l => l.Energy))
            {
                int channel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(line.Energy, maxChannels: channels));
                if (channel <= chMin || channel >= chMax) continue;
                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, channel, line.Label, line.Energy);
                if (component != null) oracleComponents.Add(component);
            }

            double devianceOracle = FitAmplitudes(observed, fixedBackground, oracleComponents, chMin, chMax, 300);

            Console.WriteLine(String.Format(CultureInfo.InvariantCulture,
                "OracleFit series={0} range=[{1}..{2}]ch  D(null)={3:F0}  D(base)={4:F0} k={5}  D(oracle)={6:F0} k={7}",
                series, chMin, chMax, devianceNull, devianceBase, baseComponents.Count, devianceOracle, oracleComponents.Count));
            Console.WriteLine(String.Format(CultureInfo.InvariantCulture,
                "AIC-like: base={0:F0}  oracle={1:F0}  (D + 2k)", devianceBase + 2.0 * baseComponents.Count, devianceOracle + 2.0 * oracleComponents.Count));

            double[] lambdaOracle = BuildLambda(fixedBackground, oracleComponents, channels);
            Console.WriteLine("Oracle lines:");
            foreach (FitComponent component in oracleComponents)
            {
                double z = FisherZ(component, lambdaOracle);
                double deltaD = DevianceDrop(observed, fixedBackground, oracleComponents, component, chMin, chMax, devianceOracle);
                int bestShift = BestShiftChannels(observed, lambdaOracle, component, channels);
                double shiftKeV = spectrum.EnergyCalibration.ChannelToEnergy(component.Channel + bestShift) -
                                  spectrum.EnergyCalibration.ChannelToEnergy(component.Channel);
                double fwhmKeV = spectrum.EnergyCalibration.ChannelToEnergy(Convert.ToInt32(component.Channel + component.Fwhm / 2.0)) -
                                 spectrum.EnergyCalibration.ChannelToEnergy(Convert.ToInt32(component.Channel - component.Fwhm / 2.0));
                bool covered = basePeaks.Any(p => Math.Abs(p.Channel - component.Channel) <= 0.5 * component.Fwhm);
                string verdict = z >= 4.0 ? (covered ? "OK-detected" : "RECOVERABLE") : (z >= 2.0 ? "marginal" : "absent");
                Console.WriteLine(String.Format(CultureInfo.InvariantCulture,
                    "  {0,-6} E={1,7:F1} ch={2,5} FWHMkeV={3,5:F1}  A={4,10:F1}  z={5,7:F1}  dD={6,9:F1}  shift={7,5:F1}keV  {8}{9}",
                    component.Label, component.Energy, component.Channel, fwhmKeV, component.Amplitude, z, deltaD, shiftKeV,
                    verdict, covered ? "" : " [no-peak-in-pipeline]"));
            }
        }

        static int ClampChannel(int channels, double value)
        {
            return Math.Max(0, Math.Min(channels - 1, Convert.ToInt32(Math.Round(value))));
        }

        static double[] BuildOracleFixedBackground(EnergySpectrum foreground, EnergySpectrum background, int[] snip)
        {
            int channels = foreground.NumberOfChannels;
            double[] result = new double[channels];
            double scale = background != null && background.MeasurementTime > 0.0 && foreground.MeasurementTime > 0.0
                ? foreground.MeasurementTime / background.MeasurementTime
                : 0.0;
            bool sameCalibration = background != null &&
                foreground.EnergyCalibration != null &&
                foreground.EnergyCalibration.Equals(background.EnergyCalibration);
            for (int i = 0; i < channels; i++)
            {
                double value = snip != null && i < snip.Length ? Math.Max(0.0, snip[i]) : 0.0;
                if (scale > 0.0)
                {
                    int backgroundChannel = i;
                    if (!sameCalibration)
                    {
                        double energy = foreground.EnergyCalibration.ChannelToEnergy(i);
                        backgroundChannel = Convert.ToInt32(background.EnergyCalibration.EnergyToChannel(energy, maxChannels: background.NumberOfChannels));
                    }

                    if (backgroundChannel >= 0 && backgroundChannel < background.NumberOfChannels)
                    {
                        value = Math.Max(value, scale * background.Spectrum[backgroundChannel]);
                    }
                }

                result[i] = value;
            }

            return result;
        }

        static FitComponent BuildFitComponent(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration, int channel, string label, double energy)
        {
            double fwhm = fwhmCalibration.ChannelToFwhm(channel);
            if (Double.IsNaN(fwhm) || Double.IsInfinity(fwhm) || fwhm <= 0.0)
            {
                return null;
            }

            double left = (double)InvokeInternalStaticMethod("BecquerelMonitor.Utils.PeakShapeModel", "GetLeftSupport", fwhmCalibration, fwhm);
            double right = (double)InvokeInternalStaticMethod("BecquerelMonitor.Utils.PeakShapeModel", "GetRightSupport", fwhmCalibration, fwhm);
            if (Double.IsNaN(left) || Double.IsInfinity(left) || Double.IsNaN(right) || Double.IsInfinity(right))
            {
                return null;
            }

            int start = Math.Max(0, channel - Convert.ToInt32(Math.Ceiling(left)));
            int end = Math.Min(spectrum.NumberOfChannels - 1, channel + Convert.ToInt32(Math.Ceiling(right)));
            if (start > end)
            {
                return null;
            }

            double[] profile = new double[end - start + 1];
            for (int ch = start; ch <= end; ch++)
            {
                profile[ch - start] = (double)InvokeInternalStaticMethod(
                    "BecquerelMonitor.Utils.PeakShapeModel", "RelativeValue", (double)(ch - channel), fwhm, fwhmCalibration);
            }

            return new FitComponent
            {
                Label = label,
                Energy = energy,
                Channel = channel,
                Fwhm = fwhm,
                Start = start,
                Profile = profile,
                Amplitude = 0.0
            };
        }

        static double[] BuildLambda(double[] fixedBackground, List<FitComponent> components, int channels)
        {
            double[] lambda = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                lambda[i] = Math.Max(1E-6, fixedBackground[i]);
            }

            foreach (FitComponent component in components)
            {
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    lambda[component.Start + j] += component.Amplitude * component.Profile[j];
                }
            }

            return lambda;
        }

        static double PoissonDeviance(int[] observed, double[] lambda, int chMin, int chMax)
        {
            double deviance = 0.0;
            for (int i = chMin; i <= chMax; i++)
            {
                double mu = Math.Max(1E-9, lambda[i]);
                int k = observed[i];
                deviance += k > 0
                    ? 2.0 * (k * Math.Log(k / mu) - (k - mu))
                    : 2.0 * mu;
            }

            return deviance;
        }

        static double LocalLogLikelihoodDelta(int[] observed, double[] lambda, FitComponent component, double amplitudeDelta, int chMin, int chMax)
        {
            double delta = 0.0;
            for (int j = 0; j < component.Profile.Length; j++)
            {
                int ch = component.Start + j;
                if (ch < chMin || ch > chMax)
                {
                    continue;
                }

                double p = component.Profile[j];
                if (p <= 0.0)
                {
                    continue;
                }

                double mu = lambda[ch];
                double muNew = mu + amplitudeDelta * p;
                if (muNew <= 1E-9)
                {
                    return Double.NegativeInfinity;
                }

                delta += observed[ch] > 0
                    ? observed[ch] * Math.Log(muNew / mu) - (muNew - mu)
                    : -(muNew - mu);
            }

            return delta;
        }

        static void ApplyAmplitudeDelta(double[] lambda, FitComponent component, double amplitudeDelta)
        {
            for (int j = 0; j < component.Profile.Length; j++)
            {
                lambda[component.Start + j] += amplitudeDelta * component.Profile[j];
            }

            component.Amplitude += amplitudeDelta;
        }

        static double FitAmplitudes(int[] observed, double[] fixedBackground, List<FitComponent> components, int chMin, int chMax, int iterations)
        {
            double[] lambda = BuildLambda(fixedBackground, components, observed.Length);

            // Matched least-squares initialisation on the running residual.
            foreach (FitComponent component in components)
            {
                if (component.Frozen)
                {
                    continue;
                }

                double numerator = 0.0;
                double denominator = 0.0;
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    int ch = component.Start + j;
                    double p = component.Profile[j];
                    numerator += (observed[ch] - lambda[ch]) * p;
                    denominator += p * p;
                }

                double initial = denominator > 0.0 ? Math.Max(0.0, numerator / denominator) : 0.0;
                if (initial > 0.0)
                {
                    ApplyAmplitudeDelta(lambda, component, initial);
                }
            }

            double stepScale = 1.0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool improved = false;
                foreach (FitComponent component in components)
                {
                    if (component.Frozen)
                    {
                        continue;
                    }

                    double step = Math.Max(0.5, component.Amplitude * 0.10) * stepScale;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        double delta = direction * step;
                        if (component.Amplitude + delta < 0.0)
                        {
                            delta = -component.Amplitude;
                            if (delta == 0.0)
                            {
                                continue;
                            }
                        }

                        double gain = LocalLogLikelihoodDelta(observed, lambda, component, delta, chMin, chMax);
                        if (gain > 1E-9)
                        {
                            ApplyAmplitudeDelta(lambda, component, delta);
                            improved = true;
                            break;
                        }
                    }
                }

                if (improved)
                {
                    stepScale = Math.Min(1.0, stepScale * 1.25);
                }
                else
                {
                    stepScale *= 0.5;
                    if (stepScale < 1E-5)
                    {
                        break;
                    }
                }
            }

            return PoissonDeviance(observed, lambda, chMin, chMax);
        }

        static double FisherZ(FitComponent component, double[] lambda)
        {
            if (component.Amplitude <= 0.0)
            {
                return 0.0;
            }

            double information = 0.0;
            for (int j = 0; j < component.Profile.Length; j++)
            {
                double p = component.Profile[j];
                information += p * p / Math.Max(1.0, lambda[component.Start + j]);
            }

            return component.Amplitude * Math.Sqrt(information);
        }

        static double DevianceDrop(int[] observed, double[] fixedBackground, List<FitComponent> components, FitComponent target, int chMin, int chMax, double fittedDeviance)
        {
            double savedAmplitude = target.Amplitude;
            double[] savedAmplitudes = components.Select(c => c.Amplitude).ToArray();
            target.Amplitude = 0.0;
            target.Frozen = true;
            double devianceWithout = FitAmplitudes(observed, fixedBackground, components, chMin, chMax, 60);
            target.Frozen = false;
            for (int i = 0; i < components.Count; i++)
            {
                components[i].Amplitude = savedAmplitudes[i];
            }

            target.Amplitude = savedAmplitude;
            return devianceWithout - fittedDeviance;
        }

        static int BestShiftChannels(int[] observed, double[] lambda, FitComponent component, int channels)
        {
            int radius = Math.Max(2, Convert.ToInt32(Math.Round(0.6 * component.Fwhm)));
            double bestScore = Double.NegativeInfinity;
            int bestShift = 0;
            for (int shift = -radius; shift <= radius; shift++)
            {
                double score = 0.0;
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    int ch = component.Start + j + shift;
                    if (ch < 0 || ch >= channels)
                    {
                        continue;
                    }

                    // residual with this component's own contribution restored at its fitted place
                    double residual = observed[ch] - lambda[ch];
                    if (j + shift >= 0 && j + shift < component.Profile.Length)
                    {
                        residual += component.Amplitude * component.Profile[j + shift];
                    }

                    score += residual * component.Profile[j];
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestShift = shift;
                }
            }

            return bestShift;
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
            public string OracleSeries { get; private set; }
            public string NuclideSetName { get; private set; }
            public List<double> AnchorEnergies { get; private set; }

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
                    else if (arg.StartsWith("--oracle=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.OracleSeries = arg.Substring("--oracle=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--nuclide-set=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.NuclideSetName = arg.Substring("--nuclide-set=".Length).Trim('"');
                    }
                    else if (arg.StartsWith("--anchor=", StringComparison.OrdinalIgnoreCase))
                    {
                        // In-memory anchor override: nuclide-set line energies (keV) to flag
                        // IsAnchor for this run without touching the user's NuclideDefinition.xml.
                        options.AnchorEnergies = arg.Substring("--anchor=".Length)
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(value => Double.Parse(value, CultureInfo.InvariantCulture))
                            .ToList();
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
