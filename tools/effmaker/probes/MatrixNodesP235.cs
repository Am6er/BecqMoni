using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// (`AMBER205`, П235 05.10.2026) Узлы двух матриц рядом: ε пика (последний
/// бин строки), сумма строки и континуум по полосам энергии — < 100 кэВ,
/// 100…300 кэВ, 300 кэВ…0.9·E (без пика) — и сдвиг B против A в процентах.
/// Только чтение. Нужна приёмке «сцена сосуда полной высоты»: что меняет
/// дальнее донышко и стенка стакана (обратное рассеяние — полоса 100…300 кэВ).
///
///     matrixnodesp235 --a=было.rmx --b=стало.rmx [--e=40,60,100,662,1461]
///
/// Без `--e` — все узлы. Разделитель дробной части — точка.
/// </summary>
static class MatrixNodesP235
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string aPath = null, bPath = null;
        List<double> want = null;
        foreach (string s in args)
        {
            if (s.StartsWith("--a=", StringComparison.Ordinal)) aPath = s.Substring(4);
            else if (s.StartsWith("--b=", StringComparison.Ordinal)) bPath = s.Substring(4);
            else if (s.StartsWith("--e=", StringComparison.Ordinal))
            {
                want = new List<double>();
                foreach (string p in s.Substring(4).Split(','))
                {
                    want.Add(double.Parse(p, CultureInfo.InvariantCulture));
                }
            }
            else { Console.Error.WriteLine("неизвестный ключ: " + s); return 2; }
        }

        ResponseMatrix a = ResponseMatrix.Load(aPath), b = ResponseMatrix.Load(bPath);
        if (a == null || b == null || a.Energies.Length != b.Energies.Length)
        {
            Console.Error.WriteLine("матрицы не читаются или сетки разные");
            return 2;
        }

        Console.WriteLine("узел E,кэВ | пик A → B (%) | сумма A → B (%) | <100 (%) | 100–300 (%) | 300–0.9E (%)");
        var nodes = new List<int>();
        if (want == null)
        {
            for (int i = 0; i < a.Energies.Length; i++) nodes.Add(i);
        }
        else
        {
            foreach (double e in want)
            {
                int best = 0;
                for (int i = 0; i < a.Energies.Length; i++)
                {
                    if (Math.Abs(a.Energies[i] - e) < Math.Abs(a.Energies[best] - e)) best = i;
                }

                nodes.Add(best);
            }
        }

        foreach (int i in nodes)
        {
            double[] qa = Bands(a, i), qb = Bands(b, i);
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0,4} {1,8:F1}", i, a.Energies[i]);
            for (int k = 0; k < qa.Length; k++)
            {
                double d = qa[k] > 0.0 ? 100.0 * (qb[k] / qa[k] - 1.0) : double.NaN;
                sb.AppendFormat(CultureInfo.InvariantCulture, " | {0:G6} → {1:G6} ({2:+0.00;-0.00})", qa[k], qb[k], d);
            }

            Console.WriteLine(sb.ToString());
        }

        return 0;
    }

    /// <summary>пик, сумма, &lt;100, 100–300, 300–0.9E (континуум без последнего бина).</summary>
    static double[] Bands(ResponseMatrix m, int i)
    {
        float[] row = m.Rows[i];
        double bin = m.BinKev, e = m.Energies[i];
        double sum = 0, lo = 0, mid = 0, hi = 0;
        int last = row.Length - 1;
        for (int k = 0; k < row.Length; k++)
        {
            sum += row[k];
            if (k == last) continue;
            double x = (k + 0.5) * bin;
            if (x < 100.0) lo += row[k];
            else if (x < 300.0) mid += row[k];
            else if (x < 0.9 * e) hi += row[k];
        }

        return new[] { row.Length > 0 ? row[last] : 0.0, sum, lo, mid, hi };
    }
}
