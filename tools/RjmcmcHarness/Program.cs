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
                Environment.CurrentDirectory = options.WorkingDirectory;

                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                ROIConfigManager.GetInstance();
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
                    foreach (double minSnr in options.MinSnrValues)
                    {
                        RunScenario(spectrumFile, minSnr, options);
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

        static void RunScenario(string spectrumFile, double minSnr, Options options)
        {
            ResultData resultData = LoadResultData(spectrumFile);
            FWHMPeakDetectionMethodConfig peakConfig = PreparePeakConfig(resultData, minSnr, options);

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
                options.UseBackgroundSubtraction ? resultData.BackgroundEnergySpectrum : null,
                finder,
                peakConfig,
                resultData.FwhmCalibration);

            List<Peak> peaks = detector.DetectPeak(
                resultData,
                options.UseBackgroundSubtraction ? BackgroundMode.Substract : BackgroundMode.Visible,
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
            resultData.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)((FWHMPeakDetectionMethodConfig)deviceConfig.PeakDetectionMethodConfig).Clone();
            resultData.ROIConfig = ROIConfigManager.GetInstance().ROIConfigList
                .FirstOrDefault(candidate => candidate.Guid == resultData.ROIConfigReference.Guid);

            if (resultData.FwhmCalibration == null)
            {
                FWHMPeakDetectionMethodConfig peakConfig = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
                resultData.FwhmCalibration = peakConfig.FwhmCalibration?.Clone() ??
                    FwhmCalibration.DefaultCalibration(peakConfig, resultData.EnergySpectrum.EnergyCalibration);
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

        static FWHMPeakDetectionMethodConfig PreparePeakConfig(ResultData resultData, double minSnr, Options options)
        {
            FWHMPeakDetectionMethodConfig config =
                (FWHMPeakDetectionMethodConfig)((FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig).Clone();
            config.UseDeconvolution = true;
            config.Min_SNR = minSnr;
            config.BurnIn = options.BurnIn;
            config.Samples = options.Samples;
            config.MaxRois = options.MaxRois;
            config.MaxExtraPeaksPerRoi = options.MaxExtraPeaksPerRoi;
            config.RoiRadiusFwhm = options.RoiRadiusFwhm;
            config.Max_Items = options.MaxItems;
            resultData.PeakDetectionMethodConfig = config;
            return config;
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
            public List<double> MinSnrValues { get; private set; }
            public int BurnIn { get; private set; }
            public int Samples { get; private set; }
            public int MaxRois { get; private set; }
            public int MaxExtraPeaksPerRoi { get; private set; }
            public double RoiRadiusFwhm { get; private set; }
            public int MaxItems { get; private set; }
            public bool UseBackgroundSubtraction { get; private set; }

            public static Options Parse(string[] args)
            {
                Options options = new Options
                {
                    InputPath = @"C:\Users\moroz\OneDrive\Desktop\Deconvolve\examples",
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    MinSnrValues = new List<double> { 4.0, 6.0, 8.0, 10.0 },
                    BurnIn = 500,
                    Samples = 1500,
                    MaxRois = 20,
                    MaxExtraPeaksPerRoi = 3,
                    RoiRadiusFwhm = 3.0,
                    MaxItems = 40,
                    UseBackgroundSubtraction = true
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
                    else if (arg.StartsWith("--snr=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MinSnrValues = arg.Substring("--snr=".Length)
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(value => Double.Parse(value, CultureInfo.InvariantCulture))
                            .ToList();
                    }
                    else if (arg.StartsWith("--burnin=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.BurnIn = Int32.Parse(arg.Substring("--burnin=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--samples=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Samples = Int32.Parse(arg.Substring("--samples=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-rois=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxRois = Int32.Parse(arg.Substring("--max-rois=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-extra=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxExtraPeaksPerRoi = Int32.Parse(arg.Substring("--max-extra=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--roi-radius=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.RoiRadiusFwhm = Double.Parse(arg.Substring("--roi-radius=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (arg.StartsWith("--max-items=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.MaxItems = Int32.Parse(arg.Substring("--max-items=".Length), CultureInfo.InvariantCulture);
                    }
                    else if (String.Equals(arg, "--bg=visible", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseBackgroundSubtraction = false;
                    }
                    else if (String.Equals(arg, "--bg=substract", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseBackgroundSubtraction = true;
                    }
                }

                return options;
            }
        }
    }
}
