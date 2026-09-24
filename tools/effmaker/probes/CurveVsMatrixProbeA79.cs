using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// ⛔ `AMBER79` (полоса П147, 24.09.2026, физика 24): КЛАСС «ВНЕ КОНУСА» В
/// КРИВОЙ И В κ.
///
/// П132 (`AMBER66`) добавил пику строки матрицы истории, чьё первичное
/// направление лежит вне конуса взвешенной ветви (аналоговая ветвь их рождает,
/// взвешенная — нет). Кривая «из геометрии» (`Efficiency` → `Run` без
/// гистограммы) и совместная эффективность каскада (`JointPeakSums`, κ) этого
/// класса не получили: кривая лежит ниже пика матрицы той же сцены.
///
/// Проба на каждой энергии `--e=` при допуске пика кривой (ПШПВ/2 из
/// геометрии, `--fwhm662=` если в геометрии разрешения нет) считает:
///   * ПИК МАТРИЦЫ — значение `Run` с гистограммой при шаге E (отражением —
///     метод закрытый): пик со всеми вкладами, у матрицы и с классом вне
///     конуса, взятым по тому же допуску;
///   * КРИВУЮ — `Efficiency(E)` с рычагом `OutOfConePeakEverywhere` ВЫКЛ (как
///     физика 23) и ВКЛ (физика 24); рычаг ставится отражением, чтобы проба
///     собиралась и на коде до правки (там рычага нет — плечо ВКЛ пропускается);
///   * κ пары `--pair=E1,E2` (`JointPeakFactor`) рычагом ВЫКЛ и ВКЛ.
///
///     curvevsmatrixprobea79 --in=&lt;.in&gt; [--e=32,60,122,662] [--n=2000000]
///                           [--fwhm662=8] [--pair=32,60] [--seed=147]
///
/// Код 0 — кривая ВКЛ сходится с пиком матрицы в 3σ на всех энергиях; 1 — нет
/// (или рычага нет — код до правки: ОЖИДАЕМО).
/// </summary>
static class CurveVsMatrixProbeA79
{
    static string F(double v, int d)
    {
        return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static double[] List(string s)
    {
        return Array.ConvertAll(s.Split(','), x => double.Parse(x, CultureInfo.InvariantCulture));
    }

    static readonly FieldInfo Lever = typeof(EfficiencySimulator).GetField("OutOfConePeakEverywhere");

    static EfficiencySimulator Make(GeometryModel g, int n, int seed, double energy, bool? lever)
    {
        var sim = new EfficiencySimulator(g)
        {
            Histories = n,
            PeakHalfWidthKev = g.PeakHalfWidthKev(energy),
        };
        if (lever.HasValue && Lever != null)
        {
            Lever.SetValue(sim, lever.Value);
        }

        if (seed != 0)
        {
            sim.ResetStream((ulong)seed);
        }

        return sim;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null;
        double[] energies = { 32, 60, 122, 662 };
        double[] pair = { 32, 60 };
        int n = 2000000, seed = 147;
        double fwhm = 8.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
            else if (a.StartsWith("--e=", StringComparison.Ordinal)) energies = List(a.Substring(4));
            else if (a.StartsWith("--pair=", StringComparison.Ordinal)) pair = List(a.Substring(7));
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--fwhm662=", StringComparison.Ordinal)) fwhm = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else
            {
                Console.Error.WriteLine("неизвестный ключ " + a);
                return 2;
            }
        }

        if (inPath == null || !File.Exists(inPath))
        {
            Console.Error.WriteLine("нет геометрии: " + inPath);
            return 2;
        }

        GlobalConfigManager.GetInstance();
        GeometryModel g = GeometryModel.Load(inPath);
        if (!(g.FwhmAt662Percent > 0.0))
        {
            g.FwhmAt662Percent = fwhm;
        }

        Console.WriteLine("{0}: историй {1}, ПШПВ на 662 {2} %, рычаг {3}", Path.GetFileNameWithoutExtension(inPath), n,
                          F(g.FwhmAt662Percent, 2), Lever != null ? "есть" : "НЕТ (код до правки)");
        Console.WriteLine("   E, кэВ | допуск | пик матрицы | кривая ВЫКЛ (отн.) | кривая ВКЛ (отн.)");
        int failed = Lever == null ? 1 : 0;
        foreach (double e in energies)
        {
            double errM, errOff, errOn = 0.0;
            // Пик пути МАТРИЦЫ — значение, которое `Run` с гистограммой
            // возвращает (F28: пик со всеми вкладами, у матрицы — и с классом
            // вне конуса `AMBER66`), при ШАГЕ = E: тогда бин пика — [E/2, ∞),
            // и класс вне конуса матрица берёт ровно по допуску `InPeak`, как
            // кривая. При мелком шаге матрица берёт из класса только историю
            // с бином пика (`BinOf`), а занос в допуске, но ниже полубина,
            // уходит в континуум — это правило СТРОКИ (допуск склада —
            // полубин), а не пика в допуске ПШПВ/2. Последний бин строки не
            // годится вовсе: при шаге E в него сдвигает часть бина 0 якорь света.
            var run = typeof(EfficiencySimulator).GetMethod(
                "Run", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(double), typeof(double[]), typeof(double), typeof(double).MakeByRefType() }, null);
            double[] row = new double[EfficiencySimulator.PeakBin(e, e) + 1];
            object[] callArgs = { e, row, e, 0.0 };
            double matrix = (double)run.Invoke(Make(g, n, seed, e, null), callArgs);
            errM = (double)callArgs[3];
            double off = Make(g, n, seed + 1, e, false).Efficiency(e, out errOff);
            double on = double.NaN;
            if (Lever != null)
            {
                on = Make(g, n, seed + 2, e, true).Efficiency(e, out errOn);
            }

            double sOff = Math.Sqrt(errM * errM + errOff * errOff) / 100.0;
            double sOn = Math.Sqrt(errM * errM + errOn * errOn) / 100.0;
            double rOff = off / matrix, rOn = on / matrix;
            bool ok = Lever != null && Math.Abs(rOn - 1.0) <= 3.0 * sOn;
            Console.WriteLine("   {0,6} | {1} | {2:E4} ± {3} % | {4} ({5}σ) | {6} ({7}σ) {8}",
                              F(e, 1), F(g.PeakHalfWidthKev(e), 2), matrix, F(errM, 2),
                              F(rOff, 4), F((rOff - 1.0) / sOff, 1),
                              double.IsNaN(rOn) ? "—" : F(rOn, 4), double.IsNaN(rOn) ? "—" : F((rOn - 1.0) / sOn, 1),
                              ok ? "✅" : "⛔");
            if (!ok)
            {
                failed++;
            }
        }

        if (pair != null && pair.Length == 2)
        {
            double eK;
            double kOff = Make(g, n, seed + 3, pair[0], false).JointPeakFactor(pair[0], pair[1], out eK);
            string on = "—";
            if (Lever != null)
            {
                double eK2;
                double kOn = Make(g, n, seed + 3, pair[0], true).JointPeakFactor(pair[0], pair[1], out eK2);
                on = F(kOn, 4) + " ± " + F(eK2, 2) + " %; ВКЛ/ВЫКЛ " + F(kOn / kOff, 4);
            }

            Console.WriteLine("   κ({0}, {1}): ВЫКЛ {2} ± {3} %; ВКЛ {4}", F(pair[0], 1), F(pair[1], 1),
                              F(kOff, 4), F(eK, 2), on);
        }

        Console.WriteLine(failed == 0 ? "✅ кривая сходится с пиком матрицы" : "⛔ отказов: " + failed);
        return failed == 0 ? 0 : 1;
    }
}
