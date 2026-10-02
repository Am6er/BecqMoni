using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BecquerelMonitor.EfficiencyMaker;

// П199 (`AMBER147`): годится ли гладкая поверхность κ − 1 = c_i·c_j (ранг 1) для
// подстановки шумных узлов таблицы κ. Фит взвешенным МНК (веса 1/σ²) по узлам
// с шумом ниже порога, печать невязки в сигмах по надёжным узлам и сравнение
// модели с замером в ходовых парах.
//
//   KappaModelP199 <каталог .rmx | файл.rmx> [--max=5] [--emin=20]
public static class KappaModelP199
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length < 1)
        {
            Console.Error.WriteLine("KappaModelP199 <каталог|файл.rmx> [--max=5] [--emin=20]");
            return 2;
        }

        double maxErr = 5.0, emin = 20.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--max=")) maxErr = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--emin=")) emin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
        }

        var files = new List<string>();
        if (Directory.Exists(args[0])) files.AddRange(Directory.GetFiles(args[0], "*.rmx"));
        else files.Add(args[0]);
        files.Sort(StringComparer.Ordinal);
        Console.WriteLine("{0,-32} {1,6} {2,8} {3,8} {4,8}  {5}", "матрица", "надёжн", "χ²/ст", "|r|>3σ", "c(1332)", "Co60 замер/модель, 32+662, 81+356");
        foreach (string path in files)
        {
            ResponseMatrix m = ResponseMatrix.Load(path);
            if (m == null || m.JointKappa == null) continue;
            double[] g = m.JointEnergies;
            int n = g.Length;
            double[][] k = m.JointKappa, s = m.JointKappaError;
            // начальное c_i = sqrt(max(0, κ_ii − 1))
            double[] c = new double[n];
            for (int i = 0; i < n; i++) c[i] = Math.Sqrt(Math.Max(0.0, k[i][i] - 1.0));
            Func<int, int, bool> ok = (i, j) => g[i] >= emin && g[j] >= emin && s[i][j] > 0.0 && s[i][j] <= maxErr && k[i][j] > 0.0;
            for (int it = 0; it < 200; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    double num = 0.0, den = 0.0;
                    for (int j = 0; j < n; j++)
                    {
                        if (!ok(i, j)) continue;
                        double sig = s[i][j] / 100.0 * k[i][j];
                        double w = 1.0 / (sig * sig);
                        double cj = i == j ? c[i] : c[j];
                        num += w * (k[i][j] - 1.0) * cj;
                        den += w * cj * cj;
                    }
                    if (den > 0.0) c[i] = Math.Max(0.0, num / den);
                }
            }

            int used = 0, out3 = 0; double chi = 0.0;
            for (int i = 0; i < n; i++)
                for (int j = i; j < n; j++)
                {
                    if (!ok(i, j)) continue;
                    double sig = s[i][j] / 100.0 * k[i][j];
                    double r = (k[i][j] - 1.0 - c[i] * c[j]) / sig;
                    chi += r * r; used++;
                    if (Math.Abs(r) > 3.0) out3++;
                }
            Func<double, double, double> model = (e1, e2) => 1.0 + Interp(g, c, e1) * Interp(g, c, e2);
            Console.WriteLine("{0,-32} {1,6} {2,8:F2} {3,8} {4,8:F3}  {5:F3}/{6:F3}  {7:F3}/{8:F3}  {9:F3}/{10:F3}",
                Path.GetFileNameWithoutExtension(path), used, used > 0 ? chi / used : double.NaN, out3, Interp(g, c, 1332.5),
                m.JointFactor(1173.2, 1332.5), model(1173.2, 1332.5), m.JointFactor(32.0, 662.0), model(32.0, 662.0),
                m.JointFactor(81.0, 356.0), model(81.0, 356.0));
            if (args.Length > 1 && Array.IndexOf(args, "--dump") >= 0)
            {
                for (int i = 0; i < n; i++)
                {
                    Console.Write("   {0,8:F1} c={1:F3} |", g[i], c[i]);
                    for (int j = 0; j < n; j++) Console.Write(" {0,5:F2}/{1,3:F0}", k[i][j], s[i][j]);
                    Console.WriteLine();
                }
            }
        }

        return 0;
    }

    static double Interp(double[] g, double[] v, double e)
    {
        if (e <= g[0]) return v[0];
        if (e >= g[g.Length - 1]) return v[g.Length - 1];
        int hi = 1; while (g[hi] < e) hi++;
        double t = Math.Log(e / g[hi - 1]) / Math.Log(g[hi] / g[hi - 1]);
        return v[hi - 1] + t * (v[hi] - v[hi - 1]);
    }
}
