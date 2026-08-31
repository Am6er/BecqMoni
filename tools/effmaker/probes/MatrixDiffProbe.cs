using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
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

        // ⛔ ПРОВЕРКА СЕТКИ И СТРОК, а не только ЧИСЛА узлов (`T111`, 31.08.2026).
        // Сверка выше спрашивала лишь `Energies.Length`, и на двух настоящих
        // матрицах корпуса (`G1S_point5` против `G1S_point25`) проба падала
        // НЕОБРАБОТАННЫМ `IndexOutOfRangeException` в `Main`: узлов поровну, а
        // строк у одной меньше. Падение — негодный способ сказать «сравнивать
        // нечего»: из него не видно ни причины, ни того, чем матрицы разошлись.
        //
        // ⚠ И совпадения ЧИСЛА узлов мало по существу. Строки сличаются по
        // НОМЕРУ узла, поэтому при разных ЭНЕРГИЯХ узлов сравнение вышло бы
        // бессмысленным молча: строка i у A и строка i у B — про разные энергии.
        if (a.Rows == null || b.Rows == null)
        {
            Console.Error.WriteLine("у одной из матриц нет строк вовсе — сравнивать нечего");
            return 2;
        }

        if (a.Rows.Length < a.Energies.Length || b.Rows.Length < b.Energies.Length)
        {
            Console.Error.WriteLine(string.Format(
                "строк меньше, чем узлов: A {0} строк при {1} узлах, B {2} при {3} — сравнивать нечего",
                a.Rows.Length, a.Energies.Length, b.Rows.Length, b.Energies.Length));
            return 2;
        }

        for (int i = 0; i < a.Energies.Length; i++)
        {
            if (Math.Abs(a.Energies[i] - b.Energies[i]) > 1e-6)
            {
                Console.Error.WriteLine(string.Format(
                    "сетки узлов РАЗНЫЕ: узел {0} — {1:F3} кэВ против {2:F3} кэВ. Строки сличаются по номеру "
                    + "узла, значит сравнение сравнивало бы разные энергии.",
                    i, a.Energies[i], b.Energies[i]));
                return 2;
            }

        }

        // ⛔ ПУСТОЙ УЗЕЛ — НОРМА, А НЕ ПОВОД ОТКАЗАТЬ. У всех матриц корпуса
        // нулевой узел (5 кэВ) пуст: отклика там нет. Именно на нём проба и
        // падала — `ra[ra.Length - 1]` на строке нулевой длины. Отказывать на
        // таком узле значило бы сделать пробу бесполезной на настоящих
        // матрицах, то есть сторожем, который отказывает всегда. Пустые узлы
        // ПРОПУСКАЮТСЯ и ПЕРЕСЧИТЫВАЮТСЯ, а число их печатается: молча
        // выбрасывать часть сетки нельзя, читатель обязан знать, по скольким
        // узлам считалась медиана.
        int skipped = 0;
        var live = new List<int>();
        for (int i = 0; i < a.Energies.Length; i++)
        {
            bool empty = a.Rows[i] == null || b.Rows[i] == null
                         || a.Rows[i].Length == 0 || b.Rows[i].Length == 0;
            if (empty)
            {
                skipped++;
            }
            else
            {
                live.Add(i);
            }
        }

        if (live.Count == 0)
        {
            Console.Error.WriteLine("у обеих матриц пусты ВСЕ узлы — сравнивать нечего");
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

        int n = live.Count;
        var peak = new double[n];
        var sum = new double[n];
        var shape = new double[n];
        var peakSigned = new double[n];
        var sumSigned = new double[n];
        var shapeNorm = new double[n];
        var peakFracA = new double[n];
        var peakFracB = new double[n];
        int wp = 0, ws = 0, wf = 0, wn = 0;

        for (int i = 0; i < n; i++)
        {
            int node = live[i];
            float[] ra = a.Rows[node], rb = b.Rows[node];
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

            // ⛔ ФОРМА ОТДЕЛЬНО ОТ МАСШТАБА (`T111`, 31.08.2026). Строки выше
            // считаны БЕЗ нормировки, и для двух прогонов ОДНОЙ геометрии это
            // верно: масштаб у них общий. Но матрицы РАЗНЫХ постановок
            // различаются прежде всего телесным углом — у пары `G1S_point5` и
            // `G1S_point25` сумма расходится на 92 %, — и все три числа выше
            // меряют тогда одно только расстояние до источника. Фиту же
            // масштаб безразличен: амплитуда образа свободна. Поэтому строки
            // сличаются ЕЩЁ РАЗ, приведёнными к единичной сумме, и рядом
            // печатается доля пика — она от нормировки не зависит вовсе.
            shapeNorm[i] = (sa > 0.0 && sb > 0.0) ? 100.0 * NormalizedL1(ra, rb, m, sa, sb) : 0.0;
            peakFracA[i] = sa > 0.0 ? pa / sa : 0.0;
            peakFracB[i] = sb > 0.0 ? pb / sb : 0.0;
            if (shapeNorm[i] > shapeNorm[wn]) wn = i;
        }

        if (skipped > 0)
        {
            Console.WriteLine("узлов пустых и пропущенных: {0} из {1} (у обеих матриц строка нулевой длины)",
                              skipped, a.Energies.Length);
        }

        Console.WriteLine("расхождение по {0} узлам, % (медиана / худший):", n);
        Console.WriteLine("   пик       : {0:F3} / {1:F3}   (узел {2}, {3:F0} кэВ)",
                          Median(peak), peak[wp], live[wp], a.Energies[live[wp]]);
        Console.WriteLine("   сумма     : {0:F3} / {1:F3}   (узел {2}, {3:F0} кэВ)",
                          Median(sum), sum[ws], live[ws], a.Energies[live[ws]]);
        Console.WriteLine("   форма, L1 : {0:F2}  / {1:F2}    (узел {2}, {3:F0} кэВ)",
                          Median(shape), shape[wf], live[wf], a.Energies[live[wf]]);

        // ФОРМА БЕЗ МАСШТАБА — единственное, что видит фит со свободной
        // амплитудой. Для двух прогонов одной геометрии эта строка почти
        // повторяет предыдущую; для двух РАЗНЫХ постановок только она и
        // осмысленна.
        Console.WriteLine();
        Console.WriteLine("ФОРМА БЕЗ МАСШТАБА (строки приведены к единичной сумме):");
        Console.WriteLine("   L1        : {0:F2}  / {1:F2}    (узел {2}, {3:F0} кэВ)",
                          Median(shapeNorm), shapeNorm[wn], live[wn], a.Energies[live[wn]]);
        double fa = Median(peakFracA), fb = Median(peakFracB);
        Console.WriteLine("   доля пика : A {0:F4}, B {1:F4}  (медиана по узлам; отношение B/A {2:F4})",
                          fa, fb, fa > 0.0 ? fb / fa : 0.0);

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

    /// <summary>
    /// Расстояние L1 между двумя строками, приведёнными к единичной сумме, —
    /// то есть расхождение ФОРМЫ без масштаба. Величина лежит в 0…2 (полное
    /// несовпадение двух распределений даёт 2), поэтому наверху она умножается
    /// на 100 и читается как проценты «до двухсот».
    /// </summary>
    static double NormalizedL1(float[] ra, float[] rb, int m, double sa, double sb)
    {
        double d = 0.0;
        for (int k = 0; k < m; k++)
        {
            d += Math.Abs(ra[k] / sa - rb[k] / sb);
        }

        return d;
    }

    static double Median(double[] v)
    {
        var c = (double[])v.Clone();
        Array.Sort(c);
        return c.Length == 0 ? 0.0
             : (c.Length % 2 == 1 ? c[c.Length / 2] : 0.5 * (c[c.Length / 2 - 1] + c[c.Length / 2]));
    }
}
