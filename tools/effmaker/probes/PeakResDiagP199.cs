using System;
using System.Globalization;
using System.Reflection;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// П199 (`AMBER145`): разбор расхождения пиковой по разрешению (Σ канала Peak +
// второй счёт аналоговой ветви) с кривой. Симулятор узла — ТОТ ЖЕ, что у
// построителя (`ResponseMatrixBuilder.MakeSimulator` отражением), три оценки:
//   A — взвешенная ветвь с допуском ПШПВ/2 (путь без гистограммы, `Efficiency`),
//       физика МАТРИЦЫ;
//   B — Σ канала Peak + `LastResolutionPeakExtra` (то, что пишет построитель);
//   C — аналоговая ветвь целиком: её пик (`WeightPeakBinDropped`/n) + второй счёт.
// И D — та же A с физикой КРИВОЙ (`EfficiencyCalculation.Run`, узел на энергии).
//
//   PeakResDiagP199 --in=<сцена.in> --kev=45 [--n=1000000]
public static class PeakResDiagP199
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null; double kev = 45.0; int n = 1000000;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=")) inPath = a.Substring(5);
            else if (a.StartsWith("--kev=")) kev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--n=")) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
        }

        GlobalConfigManager.GetInstance();
        GeometryModel g = GeometryModel.Load(inPath);
        var options = new ResponseMatrixOptions();
        MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
        double hw = g.PeakHalfWidthKev(kev);

        var sim = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7, kev });
        sim.Histories = n;
        sim.ResolutionPeakHalfWidthKev = hw;
        double err;
        double[][] rows = sim.ResponseByChannel(kev, options.BinKev, out err);
        double peak = 0.0;
        foreach (double v in rows[(int)EfficiencySimulator.ResponseChannel.Peak]) peak += v;
        double total = 0.0;
        foreach (double[] r in rows) foreach (double v in r) total += v;
        double extra = sim.LastResolutionPeakExtra;
        double analogPeak = sim.WeightPeakBinDropped / Math.Max(1000, n);

        var simA = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7, kev });
        simA.Histories = n;
        simA.PeakHalfWidthKev = hw;
        double errA;
        double a1 = simA.Efficiency(kev, out errA);

        var opt = new EfficiencyCalculationOptions { Histories = n, MinEnergyKev = kev, MaxEnergyKev = kev + 1.0, GridMode = EfficiencyGridMode.Logarithmic, NodeCount = 2 };
        EfficiencyFitResult fresh = EfficiencyCalculation.Run(g, opt, s => { }, () => false);
        double d = double.NaN, best = double.MaxValue;
        if (fresh != null && fresh.Curve != null)
            foreach (ROIEfficiencyData p in fresh.Curve) if (Math.Abs(p.Energy - kev) < best) { best = Math.Abs(p.Energy - kev); d = p.Efficiency; }

        Console.WriteLine("{0} {1:F2} кэВ, ПШПВ/2 {2:F2}: Σ Peak {3:E4}, второй счёт {4:E4}, B = {5:E4}; аналог.пик {6:E4} → C = {7:E4}; A (взвеш., физика матрицы) {8:E4} ± {9:F2} %; D (кривая) {10:E4}; Σ строки {11:E4}",
            System.IO.Path.GetFileNameWithoutExtension(inPath), kev, hw, peak, extra, peak + extra, analogPeak, analogPeak + extra, a1, errA, d, total);
        Console.WriteLine("   B/A {0:F4}, C/A {1:F4}, A/D {2:F4}, B/D {3:F4}, аналог.пик/Σ Peak {4:F4}",
            (peak + extra) / a1, (analogPeak + extra) / a1, a1 / d, (peak + extra) / d, analogPeak / peak);
        return 0;
    }
}
