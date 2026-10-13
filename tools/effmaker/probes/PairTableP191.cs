using System;
using System.Collections.Generic;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
// П191: таблица пар совпадений нуклида из поставки (nucdb) — энергии и совместная доля на распад.
public static class PairTableP191
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();
        foreach (string nuc in args)
        {
            List<double[]> pairs = FsaCascadeSummer.PairTable(nuc);
            Console.WriteLine("== {0}: пар {1}", nuc, pairs == null ? -1 : pairs.Count);
            if (pairs == null) continue;
            pairs.Sort((a, b) => b[2].CompareTo(a[2]));
            int shown = 0;
            foreach (double[] p in pairs)
            {
                if (shown++ > 14) break;
                Console.WriteLine("   {0,9:F2} + {1,9:F2} = {2,9:F2}  P={3:F5}{4}", p[0], p[1], p[0] + p[1], p[2],
                                  p.Length > 3 ? "  [" + string.Join(", ", Array.ConvertAll(p, v => v.ToString("G6", CultureInfo.InvariantCulture))) + "]" : "");
            }
        }

        return 0;
    }
}
