using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Сравнение ДВУХ матриц отклика — приёмка правок, которые обязаны считать то
/// же самое (`T43`, ускорение обхода сцены).
///
/// Зачем отдельная проба. Ускоряя счёт, легко получить «быстро и не то», и
/// взвешенного шума на это НЕ ХВАТАЕТ: он говорит, насколько зашумлена матрица,
/// а не совпала ли она с прежней. Совпадение мерится строка к строке.
///
/// ⚠ Побитового совпадения требовать НЕЛЬЗЯ, и это не оговорка на всякий
/// случай. Аналоговая ветка континуума тянет пробег на КАЖДЫЙ шаг обхода
/// (`AnalogContinuumRun`), поэтому любая правка, меняющая ЧИСЛО шагов, меняет и
/// число розыгрышей — поток случайных чисел уходит целиком, и две матрицы
/// становятся двумя независимыми выборками одного и того же. Само по себе это
/// не порок: пробег экспоненциален, а экспонента без памяти — перезапуск её на
/// границе отрезка распределения не меняет.
///
/// Отсюда правило пользования: ЧИСЛО ЭТОЙ ПРОБЫ САМО ПО СЕБЕ НИЧЕГО НЕ ЗНАЧИТ.
/// Оно значит что-то только рядом со вторым числом — расхождением двух прогонов
/// ОДНОГО И ТОГО ЖЕ кода на разных зёрнах (`CorpusMatrixProbe --seed=`). Если
/// «было против стало» не больше, чем «зерно против зерна», — правка считает то
/// же самое. Если заметно больше — не то же.
///
///   matrixdiffprobe --a=было.rmx --b=стало.rmx
///
/// Печатается расхождение в ПРОЦЕНТАХ: пик (последний бин), сумма строки
/// (эффективность узла) и форма (L1 к сумме) — медиана и худший узел.
/// </summary>
static class MatrixDiffProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string aPath = null, bPath = null;
        foreach (string s in args)
        {
            if (s.StartsWith("--a=", StringComparison.Ordinal)) aPath = s.Substring(4);
            else if (s.StartsWith("--b=", StringComparison.Ordinal)) bPath = s.Substring(4);
            else { Console.Error.WriteLine("неизвестный ключ: " + s); return 2; }
        }

        if (aPath == null || bPath == null)
        {
            Console.Error.WriteLine("нужны --a= и --b=");
            return 2;
        }

        if (!File.Exists(aPath) || !File.Exists(bPath))
        {
            Console.Error.WriteLine("нет файла: " + (File.Exists(aPath) ? bPath : aPath));
            return 2;
        }

        ResponseMatrix a = ResponseMatrix.Load(aPath);
        ResponseMatrix b = ResponseMatrix.Load(bPath);
        if (a == null || b == null)
        {
            Console.Error.WriteLine("матрица не прочиталась");
            return 2;
        }

        if (a.Energies.Length != b.Energies.Length)
        {
            Console.Error.WriteLine(string.Format("узлов разное число: {0} против {1} — сравнивать нечего",
                                                  a.Energies.Length, b.Energies.Length));
            return 2;
        }

        Console.WriteLine("Расхождение двух матриц (T43)");
        Console.WriteLine("  A: {0}", Path.GetFileName(aPath));
        Console.WriteLine("  B: {0}", Path.GetFileName(bPath));
        // Взвешенный шум и потраченные истории в файле НЕ ХРАНЯТСЯ (см.
        // `ResponseMatrix.Save`), поэтому здесь их нет и выдумывать нечего:
        // шум печатает та проба, которая матрицу построила.
        Console.WriteLine("  клейма {0}", a.Stamp == b.Stamp ? "одинаковые" : "РАЗНЫЕ — считались не из одного");
        Console.WriteLine();

        int n = a.Energies.Length;
        var peak = new double[n];
        var sum = new double[n];
        var shape = new double[n];
        var peakSigned = new double[n];
        var sumSigned = new double[n];
        int wp = 0, ws = 0, wf = 0;

        for (int i = 0; i < n; i++)
        {
            float[] ra = a.Rows[i], rb = b.Rows[i];
            int m = Math.Min(ra.Length, rb.Length);
            double sa = 0.0, sb = 0.0, l1 = 0.0;
            for (int k = 0; k < m; k++)
            {
                sa += ra[k];
                sb += rb[k];
                l1 += Math.Abs(ra[k] - rb[k]);
            }

            double pa = ra[ra.Length - 1], pb = rb[rb.Length - 1];
            peakSigned[i] = pa > 0.0 ? 100.0 * (pb - pa) / pa : 0.0;
            sumSigned[i] = sa > 0.0 ? 100.0 * (sb - sa) / sa : 0.0;
            peak[i] = Math.Abs(peakSigned[i]);
            sum[i] = Math.Abs(sumSigned[i]);
            shape[i] = sa > 0.0 ? 100.0 * l1 / sa : 0.0;
            if (peak[i] > peak[wp]) wp = i;
            if (sum[i] > sum[ws]) ws = i;
            if (shape[i] > shape[wf]) wf = i;
        }

        Console.WriteLine("расхождение по {0} узлам, % (медиана / худший):", n);
        Console.WriteLine("   пик       : {0:F3} / {1:F3}   (узел {2}, {3:F0} кэВ)",
                          Median(peak), peak[wp], wp, a.Energies[wp]);
        Console.WriteLine("   сумма     : {0:F3} / {1:F3}   (узел {2}, {3:F0} кэВ)",
                          Median(sum), sum[ws], ws, a.Energies[ws]);
        Console.WriteLine("   форма, L1 : {0:F2}  / {1:F2}    (узел {2}, {3:F0} кэВ)",
                          Median(shape), shape[wf], wf, a.Energies[wf]);

        // СМЕЩЕНИЕ — то, ради чего эта проба и написана. Величина расхождения
        // говорит только «пересдали карты»; вопрос же в другом — не сдвинулось
        // ли СРЕДНЕЕ. У каждого узла свой поток случайных чисел, поэтому знаки
        // независимы, и среднее по узлам чувствительнее одиночного узла в
        // корень из их числа. Смещение видно, когда среднее вылезает за свою
        // же ошибку; «0.3 ± 0.4» — это ноль, «2.1 ± 0.4» — это не ноль.
        double mp, ep, ms, es;
        MeanAndError(peakSigned, out mp, out ep);
        MeanAndError(sumSigned, out ms, out es);
        Console.WriteLine();
        Console.WriteLine("СМЕЩЕНИЕ B относительно A, % (среднее по узлам ± ошибка среднего):");
        Console.WriteLine("   пик       : {0,7:F3} ± {1:F3}{2}", mp, ep, Verdict(mp, ep));
        Console.WriteLine("   сумма     : {0,7:F3} ± {1:F3}{2}", ms, es, Verdict(ms, es));
        Console.WriteLine();
        Console.WriteLine("⚠ сравнивать ЭТИ числа надо с такими же для двух зёрен одного кода");
        return 0;
    }

    static string Verdict(double mean, double error)
    {
        double t = error > 0.0 ? Math.Abs(mean) / error : 0.0;
        if (t <= 2.0) return "   — ноль в пределах ошибки";
        return t <= 3.0 ? "   — на грани, мерить гуще" : "   — СМЕЩЕНИЕ, это не пересдача карт";
    }

    static void MeanAndError(double[] v, out double mean, out double error)
    {
        mean = 0.0;
        error = 0.0;
        if (v.Length == 0)
        {
            return;
        }

        foreach (double x in v) mean += x;
        mean /= v.Length;
        if (v.Length < 2)
        {
            return;
        }

        double s2 = 0.0;
        foreach (double x in v) s2 += (x - mean) * (x - mean);
        error = Math.Sqrt(s2 / (v.Length - 1) / v.Length);
    }

    static double Median(double[] v)
    {
        var c = (double[])v.Clone();
        Array.Sort(c);
        return c.Length == 0 ? 0.0
             : (c.Length % 2 == 1 ? c[c.Length / 2] : 0.5 * (c[c.Length / 2 - 1] + c[c.Length / 2]));
    }
}
