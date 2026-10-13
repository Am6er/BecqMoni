using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// ⛔ `AMBER95` (полоса П147, 24.09.2026, физика 24): ПИК МАТРИЦЫ У K-КРАЯ
/// КРИСТАЛЛА.
///
/// Матрица читает пик линии `E` линейной смесью строк двух соседних узлов
/// (`ResponseMatrix.Accumulate`, веса `1 − t` и `t`). Если K-край кристалла
/// лежит между узлами, смесь ведёт прямую через ступеньку эффективности —
/// у NaI край иода 33.17 кэВ лежал между узлами 32.99 и 34.55, и La Kα1 33.44
/// (Ce-139) читалась с пиком «как ниже края».
///
/// Проба берёт пик (сумма канала `Peak` узла) у матрицы `--matrix=` на
/// энергии `--e=` той же линейной смесью и сравнивает с АРБИТРОМ — матрицей
/// `--arb=`, у которой есть узел ровно на `E` (`CorpusMatrixProbe --emin=E
/// --emax=… --nodes=2`), тем же кодом.
///
///     matrixedgeprobea95 --matrix=&lt;rmx&gt; --arb=&lt;rmx&gt; [--e=33.44] [--tol=3]
///
/// Код 0 — |смесь/арбитр − 1| ≤ `--tol` %; 1 — больше.
/// </summary>
static class MatrixEdgeProbeA95
{
    static double PeakSum(ResponseMatrix m, int node)
    {
        float[] row = m.HasChannels ? m.ChannelRows[(int)EfficiencySimulator.ResponseChannel.Peak][node] : m.Rows[node];
        double s = 0.0;
        if (!m.HasChannels)
        {
            return row[row.Length - 1];
        }

        foreach (float v in row)
        {
            s += v;
        }

        return s;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string matrixPath = null, arbPath = null;
        double e = 33.44, tol = 3.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
            else if (a.StartsWith("--arb=", StringComparison.Ordinal)) arbPath = a.Substring(6);
            else if (a.StartsWith("--e=", StringComparison.Ordinal)) e = double.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else
            {
                Console.Error.WriteLine("неизвестный ключ " + a);
                return 2;
            }
        }

        if (matrixPath == null || arbPath == null || !File.Exists(matrixPath) || !File.Exists(arbPath))
        {
            Console.Error.WriteLine("нужны --matrix= и --arb=");
            return 2;
        }

        ResponseMatrix m = ResponseMatrix.Load(matrixPath);
        ResponseMatrix arb = ResponseMatrix.Load(arbPath);
        int hi = Array.BinarySearch(m.Energies, e);
        double mixed;
        string nodes;
        if (hi >= 0)
        {
            mixed = PeakSum(m, hi);
            nodes = "узел ровно на энергии";
        }
        else
        {
            hi = ~hi;
            int lo = hi - 1;
            double t = (e - m.Energies[lo]) / (m.Energies[hi] - m.Energies[lo]);
            mixed = (1.0 - t) * PeakSum(m, lo) + t * PeakSum(m, hi);
            nodes = string.Format(CultureInfo.InvariantCulture, "узлы {0:F3} и {1:F3} кэВ, t = {2:F3}",
                                  m.Energies[lo], m.Energies[hi], t);
        }

        int k = Array.BinarySearch(arb.Energies, e);
        if (k < 0)
        {
            Console.Error.WriteLine("у арбитра нет узла ровно на " + e.ToString(CultureInfo.InvariantCulture));
            return 2;
        }

        double truth = PeakSum(arb, k);
        double ratio = mixed / truth;
        bool ok = Math.Abs(ratio - 1.0) * 100.0 <= tol;
        Console.WriteLine("{0} на {1} кэВ ({2}): пик смесью {3:E4}, арбитр {4:E4}, отношение {5} {6}",
                          Path.GetFileNameWithoutExtension(matrixPath), e.ToString("F2", CultureInfo.InvariantCulture),
                          nodes, mixed, truth, ratio.ToString("F4", CultureInfo.InvariantCulture), ok ? "✅" : "⛔");
        return ok ? 0 : 1;
    }
}
