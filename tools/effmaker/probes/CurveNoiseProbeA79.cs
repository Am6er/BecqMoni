using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// ⛔ `AMBER79`, остаток (полоса П147, 24.09.2026, решение Amber «Важностный
/// розыгрыш (Рекомендую)»): ШУМ, ВРЕМЯ И СМЕЩЕНИЕ КРИВОЙ ТРЕМЯ ПЛЕЧАМИ.
///
/// На каждой энергии `--e=` при допуске пика кривой (ПШПВ/2) — эффективность,
/// её относительная погрешность и время `Efficiency(E)`:
///   * `off` — класса вне конуса нет (`OutOfConePeakEverywhere` ВЫКЛ): кривая
///     как в физике 23, опора для шума и времени;
///   * `plain` — класс простым аналоговым счётом (`OutOfConeImportance` ВЫКЛ,
///     как в коммите `09bb4c29`) — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: шум вдвое;
///   * `imp` — класс своим розыгрышем (умолчание).
/// И пик пути МАТРИЦЫ (`Run` с гистограммой при шаге E, `--nref=` историй) —
/// мерка смещения `plain` и `imp`.
///
///     curvenoiseprobea79 --in=&lt;.in&gt; [--e=20,30,60,100,662] [--n=200000]
///                        [--nref=2000000] [--seed=147] [--fwhm662=8]
///
/// Рычаги ставятся отражением: на коде без них плечо пропускается.
/// </summary>
static class CurveNoiseProbeA79
{
    static string F(double v, int d)
    {
        return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    static void Set(EfficiencySimulator sim, string field, object value)
    {
        FieldInfo f = typeof(EfficiencySimulator).GetField(field);
        if (f != null)
        {
            f.SetValue(sim, value);
        }
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null;
        double[] energies = { 20, 30, 60, 100, 662 };
        int n = 200000, nref = 2000000, seed = 147;
        double fwhm = 8.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
            else if (a.StartsWith("--e=", StringComparison.Ordinal))
                energies = Array.ConvertAll(a.Substring(4).Split(','), x => double.Parse(x, CultureInfo.InvariantCulture));
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--nref=", StringComparison.Ordinal)) nref = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
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

        MethodInfo run = typeof(EfficiencySimulator).GetMethod(
            "Run", BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(double), typeof(double[]), typeof(double), typeof(double).MakeByRefType() }, null);
        Console.WriteLine("{0}: историй {1} (опора матрицы {2}), зерно {3}", Path.GetFileNameWithoutExtension(inPath),
                          n, nref, seed);
        Console.WriteLine("   E, кэВ | пик матрицы | off: ε ± %, с | plain: отн., ± %, с | imp: отн., ± %, с");
        foreach (double e in energies)
        {
            var refSim = new EfficiencySimulator(g) { Histories = nref, PeakHalfWidthKev = g.PeakHalfWidthKev(e) };
            refSim.ResetStream((ulong)seed + 7);
            double[] row = new double[EfficiencySimulator.PeakBin(e, e) + 1];
            object[] callArgs = { e, row, e, 0.0 };
            double matrix = (double)run.Invoke(refSim, callArgs);
            double errM = (double)callArgs[3];

            string[] arms = { "off", "plain", "imp" };
            var text = new StringBuilder();
            text.Append("   ").Append(F(e, 1).PadLeft(6)).Append(" | ")
                .Append(matrix.ToString("E4", CultureInfo.InvariantCulture)).Append(" ± ").Append(F(errM, 2)).Append(" %");
            foreach (string arm in arms)
            {
                var sim = new EfficiencySimulator(g) { Histories = n, PeakHalfWidthKev = g.PeakHalfWidthKev(e) };
                Set(sim, "OutOfConePeakEverywhere", arm != "off");
                Set(sim, "OutOfConeImportance", arm == "imp");
                sim.ResetStream((ulong)seed);
                Stopwatch clock = Stopwatch.StartNew();
                double err;
                double eff = sim.Efficiency(e, out err);
                clock.Stop();
                double sig = Math.Sqrt(err * err + errM * errM) / 100.0;
                text.Append(" | ").Append(arm).Append(": ");
                if (arm == "off")
                {
                    text.Append(eff.ToString("E4", CultureInfo.InvariantCulture));
                }
                else
                {
                    text.Append(F(eff / matrix, 4)).Append(" (").Append(F((eff / matrix - 1.0) / sig, 1)).Append("σ)");
                }

                text.Append(" ± ").Append(F(err, 2)).Append(" %, ").Append(F(clock.Elapsed.TotalSeconds, 2)).Append(" с");
            }

            Console.WriteLine(text.ToString());
        }

        return 0;
    }
}
