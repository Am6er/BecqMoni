using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// П199 (`AMBER147`): приёмка таблицы κ по складу — продолжение `KappaScanP191`.
// На матрицу: режим (точка / сосуд / прежний), κ ходовых пар ТЕМ ЖЕ
// `JointFactor`, что у суммирователя, шум ячеек с обеими энергиями от 100 кэВ
// В ТАБЛИЦЕ (медиана, max — у подставленных ячеек это шум подстановки), шум
// замера ДО подстановки (блок JNTQ), число подставленных и минимум κ по
// таблице. Приёмка строки: у точечных κ = 1 ровно (таблицы нет), у сосудов
// шум ячеек ≥ 100 кэВ ≤ 5 % и κ ≥ 1.
//
//   KappaScanP199 <каталог с .rmx | файл.rmx> [--xray] [--legacy]
public static class KappaScanP199
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length < 1) { Console.Error.WriteLine("KappaScanP199 <каталог|файл.rmx> [--xray]"); return 2; }
        double[][] pairs = { new[] { 1173.2, 1332.5 }, new[] { 81.0, 356.0 }, new[] { 122.0, 1408.0 }, new[] { 32.0, 662.0 }, new[] { 898.0, 1836.1 }, new[] { 569.7, 1063.7 } };
        string[] names = { "Co60", "Ba133", "Eu152", "X+662", "Y88", "Bi207" };
        if (Array.IndexOf(args, "--xray") >= 0)
        {
            pairs = new[] { new[] { 32.0, 356.0 }, new[] { 40.0, 122.0 }, new[] { 40.0, 344.0 }, new[] { 32.0, 81.0 }, new[] { 75.0, 570.0 }, new[] { 14.0, 122.0 } };
            names = new[] { "32+356", "40+122", "40+344", "32+81", "75+570", "14+122" };
        }

        bool legacy = Array.IndexOf(args, "--legacy") >= 0;
        var files = new List<string>();
        if (Directory.Exists(args[0])) files.AddRange(Directory.GetFiles(args[0], "*.rmx"));
        else files.Add(args[0]);
        files.Sort(StringComparer.Ordinal);
        var head = new System.Text.StringBuilder();
        head.AppendFormat("{0,-30} {1,-9}", "матрица", "режим");
        foreach (string n in names) head.AppendFormat(" {0,8}", n);
        head.Append("   шум ≥100 кэВ в таблице мед/max | до подстановки мед/max | подст. | min κ | точек");
        Console.WriteLine(head.ToString());
        int bad = 0;
        foreach (string path in files)
        {
            MatrixRefusal refusal; int format;
            // `--legacy`: прежний формат читается ради сравнения (положительный
            // контроль: таблицы склада rev38 той же мерой обязаны НЕ пройти).
            ResponseMatrix m = ResponseMatrix.Load(path, out refusal, out format,
                                                   legacy ? ResponseMatrix.PreviousFormatVersion : 0);
            string key = Path.GetFileNameWithoutExtension(path);
            if (m == null)
            {
                Console.WriteLine("{0,-30} НЕ ЧИТАЕТСЯ: {1}{2}", key, refusal,
                                  refusal == MatrixRefusal.OldFormat ? " (формат " + format.ToString(CultureInfo.InvariantCulture) + ")" : "");
                bad++;
                continue;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("{0,-30} {1,-9}", key, m.JointMode);
            foreach (double[] p in pairs) sb.AppendFormat(" {0,8:F3}", m.JointFactor(p[0], p[1]));
            double med = 0.0, max = 0.0, minK = 1.0;
            if (m.JointKappa != null)
            {
                var errs = new List<double>();
                double[] g = m.JointEnergies;
                minK = double.MaxValue;
                for (int i = 0; i < g.Length; i++)
                    for (int j = i; j < g.Length; j++)
                    {
                        minK = Math.Min(minK, m.JointKappa[i][j]);
                        if (g[i] >= 100.0 && g[j] >= 100.0 && m.JointKappaError != null) errs.Add(m.JointKappaError[i][j]);
                    }
                errs.Sort();
                if (errs.Count > 0) { med = errs[errs.Count / 2]; max = errs[errs.Count - 1]; }
            }

            sb.AppendFormat("   {0,6:F2} / {1,6:F1} % | {2,6:F2} / {3,6:F1} % | {4,4} | {5,5:F3} | {6}",
                            med, max, m.JointRawMedianNoise, m.JointRawMaxNoise, m.JointSubstituted, minK, m.JointPoints);
            bool point = m.JointMode == JointKappaMode.Point;
            bool ok = point
                ? m.JointKappa == null && Math.Abs(m.JointFactor(1173.2, 1332.5) - 1.0) == 0.0
                : m.JointMode == JointKappaMode.Adaptive && max <= 5.0 && minK >= 1.0;
            sb.Append(ok ? "   ПРИНЯТА" : "   НЕ ПРИНЯТА");
            if (!ok) bad++;
            Console.WriteLine(sb.ToString());
        }

        Console.WriteLine(bad == 0 ? "ВСЕ ПРИНЯТЫ" : "НЕ ПРИНЯТО: " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }
}
