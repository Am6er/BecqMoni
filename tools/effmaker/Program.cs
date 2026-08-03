using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace EffMaker
{
    /// <summary>
    /// Консольный харнесс конструктора кривой эффективности: тот же
    /// EfficiencyFitter, что и в форме приложения, но без GUI — чтобы гонять
    /// пачки корпуса и сравнивать кривые между прогонами.
    ///
    ///   effmaker --workdir=DIR --input=spectra [--ref=ROI.xml] [--out=prefix]
    ///            [--chains=Th-232,Ra-226] [--order=3] [--min-z=4] [--min-i=1]
    ///            [--no-bg] [--anchor=E:eps]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                string workdir = null, input = null, reference = null, outPrefix = "eff";
                string chains = null, anchor = null, deviceGuid = null;
                int order = 3;
                double minZ = 4.0, minI = 1.0;
                bool useBackground = true;

                foreach (string arg in args)
                {
                    int eq = arg.IndexOf('=');
                    string key = eq > 0 ? arg.Substring(0, eq) : arg;
                    string value = eq > 0 ? arg.Substring(eq + 1) : "";
                    switch (key)
                    {
                        case "--workdir": workdir = value; break;
                        case "--input": input = value; break;
                        case "--ref": reference = value; break;
                        case "--out": outPrefix = value; break;
                        case "--chains": chains = value; break;
                        case "--anchor": anchor = value; break;
                        case "--order": order = int.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--min-z": minZ = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--min-i": minI = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--no-bg": useBackground = false; break;
                        case "--device": deviceGuid = value; break;
                        default: throw new ArgumentException("Unknown option: " + arg);
                    }
                }

                if (string.IsNullOrEmpty(input))
                {
                    Console.Error.WriteLine("--input is required");
                    return 1;
                }

                if (!string.IsNullOrEmpty(workdir))
                {
                    Environment.CurrentDirectory = workdir;
                }

                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();

                EfficiencyFitInput fit = new EfficiencyFitInput
                {
                    PolynomialOrder = order,
                    MinSignificance = minZ,
                    MinIntensity = minI,
                    SubtractBackground = useBackground,
                    FallbackDeviceGuid = deviceGuid
                };
                fit.SpectrumFiles.AddRange(ResolveFiles(input));
                if (!string.IsNullOrEmpty(chains))
                {
                    fit.Chains.AddRange(chains.Split(',').Select(s => s.Trim()));
                }
                else
                {
                    fit.Chains.AddRange(EfficiencyLibrary.BuildChains()
                        .Where(kv => kv.Value.Count >= 3).Select(kv => kv.Key));
                }

                if (!string.IsNullOrEmpty(reference))
                {
                    fit.Reference = EfficiencyFitter.LoadReferenceCurve(reference);
                    fit.ReferencePath = reference;
                    Console.Error.WriteLine("reference: {0} points from {1}",
                        fit.Reference.Count, Path.GetFileName(reference));
                }

                if (!string.IsNullOrEmpty(anchor))
                {
                    string[] parts = anchor.Split(':');
                    fit.AnchorEnergy = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    fit.AnchorEfficiency = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }

                Console.Error.WriteLine("chains: {0}", string.Join(", ", fit.Chains));
                Console.Error.WriteLine("spectra: {0}", fit.SpectrumFiles.Count);

                EfficiencyFitResult result = EfficiencyFitter.Run(fit, Console.Error.WriteLine, () => false);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Console.Error.WriteLine("ERROR: " + result.Error);
                    return 2;
                }

                Console.WriteLine("lines={0} series={1} chi2ndf={2:F3} range={3:F0}-{4:F0} level={5}",
                    result.AcceptedCount, result.SeriesKeys.Count, result.Chi2Ndf,
                    result.MinEnergy, result.MaxEnergy, result.LevelSource);
                Console.WriteLine("shape: " + string.Join(", ",
                    result.Coefficients.Select(c => c.ToString("G6", CultureInfo.InvariantCulture))));
                Console.WriteLine("level: " + result.Level.ToString("G6", CultureInfo.InvariantCulture));
                foreach (double e in new[] { 60.0, 100.0, 186.0, 239.0, 352.0, 583.0, 662.0, 911.0, 1120.0, 1461.0, 1765.0, 2615.0 })
                {
                    Console.WriteLine("  eps({0,6:F0}) = {1:E4}", e, EfficiencyFitter.Evaluate(result, e));
                }

                EfficiencyFitter.ExportCsv(outPrefix + "_curve.csv", result);
                EfficiencyFitter.SaveCurve(outPrefix + "_roi.xml", reference,
                    Path.GetFileName(outPrefix), result.Curve,
                    "effmaker harness run");
                Console.Error.WriteLine("written: {0}_curve.csv, {0}_roi.xml", outPrefix);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        static IEnumerable<string> ResolveFiles(string path)
        {
            if (Directory.Exists(path))
            {
                return Directory.GetFiles(path, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            }

            return new[] { path };
        }
    }
}
