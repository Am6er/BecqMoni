using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

// (`AMBER311`) Какой признак несёт именно горб обратного рассеяния: распределение заноса канала
// `Compton` по историям, чей занос в большинстве дан квантами, рассеянными ВНЕ кристалла на угол
// не меньше порога. Пороги — `EfficiencySimulator.BackscatterDiagCos`: любой, 60°, 90°, 120°, 150°.
//
//   BackscatterAngleProbeA311 --scene=<.in> [--scene=…] [--energies=661.657,2614.5] [--n=3000000] [--lead=<мм>] [--bare] [--csv=<каталог>]
//
// --lead — защита толщиной N мм свинца вокруг прибора (кольцо слоёв, сцена `ShieldInScene`), --bare —
// без обвязки кристалла (положительный контроль: рассеиваться негде). Код 0 — замер снят.
static class BackscatterAngleProbeA311
{
    static bool noLight;

    static readonly string[] Names = { "любой", "≥60°", "≥90°", "≥120°", "≥150°" };

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var scenes = new List<string>();
        double[] energies = { 661.657, 2614.5 };
        int n = 3000000;
        double lead = 0.0;
        bool bare = false;
        string csv = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenes.Add(a.Substring(8));
            else if (a.StartsWith("--energies=", StringComparison.Ordinal)) energies = Array.ConvertAll(a.Substring(11).Split(','), s => double.Parse(s, CultureInfo.InvariantCulture));
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--lead=", StringComparison.Ordinal)) lead = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--bare") bare = true;
            else if (a == "--nolight") noLight = true;
            else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
            else { Console.WriteLine("не знаю ключа: " + a); return 2; }
        }

        GlobalConfigManager.GetInstance();
        foreach (string path in scenes)
        {
            GeometryModel g = GeometryModel.Load(path);
            string tag = Path.GetFileNameWithoutExtension(path);
            if (bare)
            {
                g.FrontReflectorThickness = 0.0; g.SideReflectorThickness = 0.0;
                g.FrontGapThickness = 0.0; g.SideGapThickness = 0.0;
                g.FrontCladdingThickness = 0.0; g.SideCladdingThickness = 0.0;
                g.MountingThickness = 0.0;
                tag += "+bare";
            }

            if (lead > 0.0)
            {
                g.InShield = true;
                g.ShieldInnerDiameter = 200.0;
                g.ShieldInnerHeight = 300.0;
                g.ShieldFloorGap = 50.0;
                g.ShieldLayers = new List<GeometryShieldLayer>
                {
                    new GeometryShieldLayer { Material = GeometryMaterialLead(), SideThickness = lead, FloorThickness = lead, LidThickness = lead }
                };
                tag += "+Pb" + lead.ToString("F0", CultureInfo.InvariantCulture);
            }

            foreach (double e in energies)
            {
                Measure(g, tag, e, n, csv);
            }
        }

        return 0;
    }

    static GeometryMaterial GeometryMaterialLead()
    {
        var m = new GeometryMaterial { Name = "Lead", Density = 11.35 };
        m.Fractions[82] = 1.0;
        return m;
    }

    static void Measure(GeometryModel g, string tag, double energy, int n, string csv)
    {
        var options = new ResponseMatrixOptions { BinKev = 2.0, Histories = n, ContinuumErrorTarget = 0.0 };
        if (noLight) options.LightNonproportionality = false;
        int bins = EfficiencySimulator.PeakBin(energy, options.BinKev) + 1;
        int threads = Math.Max(1, Environment.ProcessorCount - 2);
        var parts = new double[threads][][];
        var totals = new double[threads][];
        var channelSum = new double[bins];
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            var sim = (EfficiencySimulator)typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { g, options, 100 + t, energy });
            sim.Histories = n / threads;
            var diag = new double[6][];
            for (int k = 0; k < diag.Length; k++) diag[k] = new double[bins];
            typeof(EfficiencySimulator).GetField("BackscatterDiag", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sim, diag);
            double err;
            sim.ResponseByChannel(energy, options.BinKev, out err);
            parts[t] = diag;
        });

        var sum = new double[6][];
        for (int k = 0; k < 6; k++)
        {
            sum[k] = new double[bins];
            for (int t = 0; t < threads; t++)
            {
                for (int b = 0; b < bins; b++) sum[k][b] += parts[t][k][b];
            }
        }

        double hump = energy / (1.0 + 2.0 * energy / 511.0);
        double at90 = energy / (1.0 + energy / 511.0);
        double compton = Sum(sum[0], 0, bins);
        Console.WriteLine("== {0} E={1:F1} кэВ: Compton {2:E3} на испущенный; горб 180° у {3:F1}, 90° у {4:F1}, край Комптона {5:F1}", tag, energy, compton / n, hump, at90, energy - hump);
        for (int k = 0; k < 5; k++)
        {
            double[] h = sum[k + 1];
            double area = Sum(h, 0, bins);
            int lo = (int)((hump - 30.0) / options.BinKev), hi = (int)((at90 + 30.0) / options.BinKev);
            int top = TopSmoothed(h, 5);
            int half = HalfWidthBins(h, top, 5);
            Console.WriteLine("   порог {0,-6} доля Compton {1,7:F3} %; в окне горба {2:F0}…{3:F0} кэВ {4,6:F1} % своей площади; выше 90°-края {5,6:F1} %; вершина {6,7:F1} кэВ, полуширина {7,6:F1} кэВ; средняя энергия {8,7:F1} кэВ",
                              Names[k], compton > 0 ? 100.0 * area / compton : 0.0, hump - 30.0, at90 + 30.0,
                              area > 0 ? 100.0 * Sum(h, lo, hi) / area : 0.0, area > 0 ? 100.0 * Sum(h, hi, bins) / area : 0.0,
                              (top + 0.5) * options.BinKev, half * options.BinKev, area > 0 ? Mean(h, options.BinKev) : 0.0);
        }

        if (csv != null)
        {
            Directory.CreateDirectory(csv);
            var sb = new StringBuilder("kev,compton,any,a60,a90,a120,a150\n");
            for (int b = 0; b < bins; b++)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1},{1:E4},{2:E4},{3:E4},{4:E4},{5:E4},{6:E4}\n", (b + 0.5) * options.BinKev,
                                sum[0][b] / n, sum[1][b] / n, sum[2][b] / n, sum[3][b] / n, sum[4][b] / n, sum[5][b] / n);
            }

            File.WriteAllText(Path.Combine(csv, tag + "_" + energy.ToString("F0", CultureInfo.InvariantCulture) + ".csv"), sb.ToString(), new UTF8Encoding(false));
        }
    }

    static double Sum(double[] h, int lo, int hi)
    {
        double s = 0.0;
        for (int b = Math.Max(0, lo); b < Math.Min(h.Length, hi); b++) s += h[b];
        return s;
    }

    static double Mean(double[] h, double bin)
    {
        double s = 0.0, m = 0.0;
        for (int b = 0; b < h.Length; b++) { s += h[b]; m += h[b] * (b + 0.5) * bin; }
        return s > 0 ? m / s : 0.0;
    }

    static int TopSmoothed(double[] h, int half)
    {
        int top = 0;
        double best = -1.0;
        // Низ шкалы (рентген защиты) вершиной не считается: ищем выше 50 кэВ.
        for (int b = 25 + half; b < h.Length - half; b++)
        {
            double s = 0.0;
            for (int d = -half; d <= half; d++) s += h[b + d];
            if (s > best) { best = s; top = b; }
        }

        return top;
    }

    static int HalfWidthBins(double[] h, int top, int half)
    {
        Func<int, double> smooth = b =>
        {
            double s = 0.0;
            for (int d = -half; d <= half; d++) { int i = b + d; if (i >= 0 && i < h.Length) s += h[i]; }
            return s;
        };
        double level = smooth(top) / 2.0;
        int lo = top, hi = top;
        while (lo > 0 && smooth(lo) > level) lo--;
        while (hi < h.Length - 1 && smooth(hi) > level) hi++;
        return (hi - lo) / 2;
    }
}
