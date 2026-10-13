using System;
using System.Globalization;
using System.IO;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
// П191: κ (совместная эффективность пары) по всем матрицам склада для ходовых пар + шум узлов, между которыми пара лежит.
public static class KappaScanP191
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length < 1) { Console.Error.WriteLine("KappaScanP191 <каталог с .rmx>"); return 2; }
        double[][] pairs = { new[] { 1173.2, 1332.5 }, new[] { 81.0, 356.0 }, new[] { 122.0, 1408.0 }, new[] { 32.0, 662.0 }, new[] { 898.0, 1836.1 }, new[] { 569.7, 1063.7 } };
        if (args.Length > 1 && args[1] == "--xray") pairs = new[] { new[] { 32.0, 356.0 }, new[] { 40.0, 122.0 }, new[] { 40.0, 344.0 }, new[] { 32.0, 81.0 }, new[] { 75.0, 570.0 }, new[] { 14.0, 122.0 } };
        Console.WriteLine("{0,-34} {1,10} {2,10} {3,10} {4,10} {5,10} {6,10}   узлов κ / точек / шум узлов ≥ 100 кэВ: медиана, max", "матрица", "Co60", "Ba133", "Eu152", "X+662", "Y88", "Bi207");
        foreach (string path in Directory.GetFiles(args[0], "*.rmx"))
        {
            ResponseMatrix m;
            try { m = ResponseMatrix.Load(path); } catch (Exception ex) { Console.WriteLine("{0}: {1}", Path.GetFileName(path), ex.Message); continue; }
            if (m == null || m.JointKappa == null || m.JointEnergies == null) { Console.WriteLine("{0,-34} κ нет", Path.GetFileName(path)); continue; }
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("{0,-34}", Path.GetFileNameWithoutExtension(path));
            foreach (double[] p in pairs) sb.AppendFormat(" {0,10:F3}", m.JointFactor(p[0], p[1]));
            var errs = new System.Collections.Generic.List<double>();
            double[] g = m.JointEnergies;
            for (int i = 0; i < g.Length; i++) for (int j = i; j < g.Length; j++)
                if (g[i] >= 100.0 && g[j] >= 100.0 && m.JointKappaError != null) errs.Add(m.JointKappaError[i][j]);
            errs.Sort();
            sb.AppendFormat("   {0} / {1} / {2:F1} %, {3:F0} %", g.Length, m.JointPoints, errs.Count > 0 ? errs[errs.Count / 2] : double.NaN, errs.Count > 0 ? errs[errs.Count - 1] : double.NaN);
            Console.WriteLine(sb.ToString());
        }

        return 0;
    }
}
