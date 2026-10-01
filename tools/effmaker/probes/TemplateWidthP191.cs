using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
// П191: ширина образа ПОСЛЕ уширения (BroadenResponseDeposit) против калибровки ПШПВ — по дробному положению
// центра относительно сетки каналов и по ширине в каналах. Деление массы между соседними каналами (Splat)
// добавляет дисперсию f(1−f) кан², и у узких (в каналах) пиков образ шире калибровки (`AMBER158`).
//
// П198 (01.10.2026): проба перенесена в дерево и получила два режима.
//   TemplateWidthP191 <спектр.xml> [rebin] [phase|drift] [gain]
//   phase (умолчание) — дробное положение центра задаётся ЭНЕРГИЕЙ заноса (центр на кан+φ, φ = 0…7/8),
//         сдвига шкалы нет; эталон — ПШПВ калибровки в центре образа. Это приёмка `AMBER158`.
//   drift — занос на номинальной энергии, сдвиг нуля 0/0.25/0.5/0.75/1/2 кан (и усиление gain);
//         эталон — g·ПШПВ(канал ДО дрейфа) (`AMBER149`); колонка «отн(сдв)» — против ПШПВ в сдвинутом
//         канале (так образ строился до П198).
// Итог — строка «ИТОГ: max|отн−1| …» по всем строкам режима.
public static class TemplateWidthP191
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();
        string path = args[0];
        int rebin = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1;
        string mode = args.Length > 2 ? args[2] : "phase";
        double gain = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 1.0;
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        ResultDataFile file;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) file = (ResultDataFile)serializer.Deserialize(stream);
        ResultData rd = file.ResultDataList[0];
        var cal0 = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
        var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
        var pf0 = (PowerFwhmCalibration)(rd.FwhmCalibration ?? (cfg != null ? cfg.FwhmCalibration : null));
        int channels = rd.EnergySpectrum.NumberOfChannels / rebin;
        var cal = new PolynomialEnergyCalibration(cal0);
        double[] c = (double[])cal.Coefficients.Clone(); double f = 1.0;
        for (int i = 0; i < c.Length; i++) { c[i] *= f; f *= rebin; }
        cal.Coefficients = c; cal.CheckCalibration(channels);
        var pf = (PowerFwhmCalibration)pf0.Clone(); pf.RescaleCoefficients(rebin);
        Console.WriteLine("спектр {0}: каналов {1} (×{2}), режим {3}, усиление {4}, ПШПВ a={5} p={6}", Path.GetFileName(path), channels, rebin, mode,
                          gain.ToString("F4", CultureInfo.InvariantCulture),
                          pf.Coefficients[0].ToString("G5", CultureInfo.InvariantCulture), pf.Coefficients[1].ToString("G5", CultureInfo.InvariantCulture));

        var analyzer = new FsaAnalyzer();
        MethodInfo m = typeof(FsaAnalyzer).GetMethod("BroadenResponseDeposit", BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(double[]), typeof(EnergyCalibration), typeof(FwhmCalibration), typeof(double), typeof(double), typeof(double), typeof(int), typeof(int), typeof(int) }, null);
        if (m == null) { Console.Error.WriteLine("BroadenResponseDeposit(9 арг.) не найден"); return 1; }
        const double bin = 0.05; // кэВ на бин заноса: мелко, чтобы фаза задавалась точно
        bool drift = string.Equals(mode, "drift", StringComparison.OrdinalIgnoreCase);
        double[] steps = drift ? new[] { 0.0, 0.25, 0.5, 0.75, 1.0, 2.0 } : new[] { 0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875 };
        Console.WriteLine("{0,8} {1,7} {2,9} {3,6} {4,9} {5,9} {6,8} {7,9} {8,9}", "E кэВ", drift ? "сдвиг" : "фаза", "центр", "дробь", "FWHM эт", "FWHM обр", "отнош", "отн(сдв)", "площадь");
        double worst = 0.0, worstE = 0.0, worstS = 0.0; int rows = 0;
        foreach (double e0 in new[] { 22.1, 30.0, 45.0, 59.5, 88.0, 122.1, 165.9, 356.0, 661.7, 1332.5 })
        {
            foreach (double s in steps)
            {
                double e = e0, off = 0.0;
                if (drift) { off = s; }
                else
                {
                    double ch = cal.EnergyToChannel(e0);
                    e = cal.ChannelToEnergy(Math.Floor(ch) + s);
                }
                int n = (int)Math.Ceiling((e + 80.0) / bin) + 2;
                double[] deposit = new double[n];
                deposit[(int)Math.Round(e / bin)] = 1.0;
                double[] t = (double[])m.Invoke(analyzer, new object[] { deposit, cal, pf, bin, drift ? gain : 1.0, off, 0, channels - 1, channels });
                if (t == null) { Console.WriteLine("{0,8:F1} {1,7:F3} образа нет", e, s); continue; }
                double s0 = 0, s1 = 0, s2 = 0;
                for (int i = 0; i < t.Length; i++) { s0 += t[i]; s1 += t[i] * i; s2 += t[i] * (double)i * i; }
                double mean = s1 / s0, var = s2 / s0 - mean * mean;
                double fwhmObs = 2.0 * Math.Sqrt(2.0 * Math.Log(2.0) * Math.Max(var, 0.0));
                double fwhmShifted = pf.ChannelToFwhm(mean);
                // эталон: в фазовом режиме — ПШПВ в центре; в режиме дрейфа — g·ПШПВ(канал до дрейфа)
                double g = drift ? gain : 1.0;
                double fwhmRef = drift ? g * pf.ChannelToFwhm((mean - off) / g) : fwhmShifted;
                double ratio = fwhmObs / fwhmRef;
                rows++;
                if (Math.Abs(ratio - 1.0) > worst) { worst = Math.Abs(ratio - 1.0); worstE = e0; worstS = s; }
                Console.WriteLine("{0,8:F2} {1,7:F3} {2,9:F3} {3,6:F3} {4,9:F4} {5,9:F4} {6,8:F4} {7,9:F4} {8,9:F5}", e, s, mean, mean - Math.Floor(mean), fwhmRef, fwhmObs, ratio, fwhmObs / fwhmShifted, s0);
            }
        }
        Console.WriteLine("ИТОГ: строк {0}, max|отн−1| = {1} (E {2} кэВ, {3} {4})", rows, worst.ToString("F4", CultureInfo.InvariantCulture),
                          worstE.ToString("F1", CultureInfo.InvariantCulture), drift ? "сдвиг" : "фаза", worstS.ToString("F3", CultureInfo.InvariantCulture));
        return 0;
    }
}
