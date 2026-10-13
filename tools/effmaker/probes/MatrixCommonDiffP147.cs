using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// СДВИГ МАТРИЦЫ МЕЖДУ ПОКОЛЕНИЯМИ С РАЗНОЙ СЕТКОЙ (полоса П147, 24.09.2026,
/// физика 24). `MatrixDiffProbe` сравнивает только матрицы с одинаковым числом
/// узлов, а физика 24 добавляет узлы у K-краёв кристалла (`AMBER95`): у NaI 142
/// узла вместо 140, у CsI 144. Проба сравнивает ОБЩИЕ узлы (энергия совпадает
/// до бита) — пик (сумма канала `Peak`), Σ строки, форма (L1 к сумме) и
/// положение K-вылета; κ — по общим энергиям сетки κ. Печать — медиана и
/// худший, в процентах.
///
///     matrixcommondiffp147 --a=было.rmx --b=стало.rmx
///
/// ⚠ Как и у `MatrixDiffProbe`, число само по себе ничего не значит: зерно
/// узла — от его НОМЕРА, и у узлов выше добавленных поток другой.
/// </summary>
static class MatrixCommonDiffP147
{
    static string F(double v, int d)
    {
        return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static double Sum(float[] row)
    {
        double s = 0.0;
        foreach (float v in row) s += v;
        return s;
    }

    static void Stat(string what, List<double> v)
    {
        if (v.Count == 0)
        {
            Console.WriteLine("   {0}: нет общих точек", what);
            return;
        }

        var abs = v.ConvertAll(Math.Abs);
        abs.Sort();
        double mean = 0.0;
        foreach (double x in v) mean += x;
        mean /= v.Count;
        Console.WriteLine("   {0}: точек {1}, |Δ| медиана {2} %, худший {3} %, среднее Δ {4} %", what, v.Count,
                          F(abs[abs.Count / 2], 3), F(abs[abs.Count - 1], 3), F(mean, 3));
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string pa = null, pb = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--a=", StringComparison.Ordinal)) pa = a.Substring(4);
            else if (a.StartsWith("--b=", StringComparison.Ordinal)) pb = a.Substring(4);
        }

        if (pa == null || pb == null || !File.Exists(pa) || !File.Exists(pb))
        {
            Console.Error.WriteLine("нужны --a= и --b=");
            return 2;
        }

        ResponseMatrix A = ResponseMatrix.Load(pa), B = ResponseMatrix.Load(pb);
        Console.WriteLine("A {0}: узлов {1}, {2}", Path.GetFileName(pa), A.Energies.Length, A.Stamp);
        Console.WriteLine("B {0}: узлов {1}, {2}", Path.GetFileName(pb), B.Energies.Length, B.Stamp);
        var extra = new List<string>();
        foreach (double e in B.Energies)
        {
            if (Array.IndexOf(A.Energies, e) < 0) extra.Add(F(e, 3));
        }

        Console.WriteLine("   узлы B, которых нет у A: {0}", extra.Count == 0 ? "нет" : string.Join(" ", extra));
        var peak = new List<double>();
        var sum = new List<double>();
        var shape = new List<double>();
        var low = new List<double>();
        for (int i = 0; i < A.Energies.Length; i++)
        {
            int j = Array.IndexOf(B.Energies, A.Energies[i]);
            if (j < 0) continue;
            float[] ra = A.Rows[i], rb = B.Rows[j];
            double sa = Sum(ra), sb = Sum(rb);
            if (sa > 0.0) sum.Add(100.0 * (sb / sa - 1.0));
            if (A.HasChannels && B.HasChannels)
            {
                double pka = Sum(A.ChannelRows[0][i]), pkb = Sum(B.ChannelRows[0][j]);
                if (pka > 0.0) peak.Add(100.0 * (pkb / pka - 1.0));
            }

            int n = Math.Min(ra.Length, rb.Length);
            double l1 = 0.0, la = 0.0, lb = 0.0;
            int q = n / 4;
            for (int b = 0; b < n; b++)
            {
                l1 += Math.Abs(ra[b] / sa - rb[b] / sb);
                if (b < q) { la += ra[b]; lb += rb[b]; }
            }

            if (sa > 0.0 && sb > 0.0) shape.Add(100.0 * l1);
            if (la > 0.0 && A.Energies[i] > 200.0) low.Add(100.0 * ((lb / sb) / (la / sa) - 1.0));
        }

        Stat("пик (канал Peak)", peak);
        Stat("Σ строки", sum);
        Stat("форма, L1 к сумме", shape);
        Stat("нижняя четверть строки (доля), узлы > 200 кэВ", low);

        if (A.JointKappa != null && B.JointKappa != null && A.JointEnergies != null && B.JointEnergies != null)
        {
            var kap = new List<double>();
            var pull = new List<double>();
            for (int i = 0; i < A.JointEnergies.Length; i++)
            {
                int ib = Array.IndexOf(B.JointEnergies, A.JointEnergies[i]);
                if (ib < 0) continue;
                for (int k = i; k < A.JointEnergies.Length; k++)
                {
                    int kb = Array.IndexOf(B.JointEnergies, A.JointEnergies[k]);
                    if (kb < 0) continue;
                    double ka = A.JointKappa[i][k], kbv = B.JointKappa[ib][kb];
                    double ea0 = A.JointKappaError != null ? A.JointKappaError[i][k] : 0.0;
                    double eb0 = B.JointKappaError != null ? B.JointKappaError[ib][kb] : 0.0;
                    // Пары, где κ не измерен (шум ≥ 10 % у любой из матриц: низ
                    // шкалы, эффективность ~0), в сводку не идут — отношение там
                    // шум деления на ноль.
                    if (ka > 0.0 && kbv > 0.0 && ea0 > 0.0 && ea0 < 10.0 && eb0 > 0.0 && eb0 < 10.0)
                    {
                        kap.Add(100.0 * (kbv / ka - 1.0));
                        double ea = A.JointKappaError != null ? A.JointKappaError[i][k] : 0.0;
                        double eb = B.JointKappaError != null ? B.JointKappaError[ib][kb] : 0.0;
                        double s = Math.Sqrt(ea * ea + eb * eb);
                        if (s > 0.0) pull.Add((100.0 * (kbv / ka - 1.0)) / s);
                    }
                }
            }

            Stat("κ по общим парам (шум < 10 % у обеих)", kap);
            if (pull.Count > 0)
            {
                double m = 0.0;
                foreach (double p in pull) m += p;
                Console.WriteLine("   κ: средний сдвиг в σ {0} (по {1} парам)", F(m / pull.Count, 2), pull.Count);
            }
        }
        else
        {
            Console.WriteLine("   κ: у одной из матриц блока нет");
        }

        return 0;
    }
}
