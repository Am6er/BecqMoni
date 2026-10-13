using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// ⛔ `AMBER135` (полоса П164, 28.09.2026): ЦЕНА ЗАКОНА ДОПУСКА ПИКА КРИВОЙ.
///
/// Допуск пика кривой «из геометрии» — половина ПШПВ(E), и ход ПШПВ с энергией
/// до П164 брался корневым от 662 кэВ (`GeometryModel.PeakHalfWidthKev`). У
/// сцинтиллятора внизу шкалы настоящая ПШПВ уже корневой (степень `V2`
/// 0.585…0.775): допуск шире пика, и в «пик» кривой попадает часть
/// однократного рассеяния с потерей, которой в пике спектра нет.
///
/// Проба на каждой энергии `--e=` считает узел кривой (`Efficiency(E)`, тот же
/// путь, что у `EfficiencyCalculation.Run`) при двух допусках — корневом
/// (`--pow=0.5`, как до правки) и степенном с показателем `--pow2=` (или
/// нынешнем законе геометрии, если `--pow2` не дан) — одним зерном, и печатает
/// отношение «корень / степень» с погрешностью. Больше единицы — корневой
/// допуск завышает кривую.
///
///     peaktolprobep164 --in=&lt;.in&gt; [--e=46.5,59.5,88,122,662] [--n=2000000]
///                      [--pow2=0.65] [--seed=164]
///
/// Код 0 — посчиталось; 2 — ключи.
/// </summary>
static class PeakTolProbeP164
{
    static string F(double v, int d)
    {
        return v.ToString("F" + d, CultureInfo.InvariantCulture);
    }

    static double[] List(string s)
    {
        return Array.ConvertAll(s.Split(','), x => double.Parse(x, CultureInfo.InvariantCulture));
    }

    // полуширина ПШПВ(E) по степенному закону от 662 кэВ: ½·F662%·662·(E/662)^p
    static double Half(GeometryModel g, double e, double p)
    {
        return 0.5 * g.FwhmAt662Percent / 100.0 * 662.0 * Math.Pow(e / 662.0, p);
    }

    static double Node(GeometryModel g, int n, int seed, double e, double half, out double err)
    {
        var sim = new EfficiencySimulator(g) { Histories = n, PeakHalfWidthKev = half };
        if (seed != 0)
        {
            sim.ResetStream((ulong)seed);
        }

        return sim.Efficiency(e, out err);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null;
        double[] energies = { 46.5, 59.5, 88, 122, 662 };
        int n = 2000000, seed = 164;
        double pow2 = double.NaN;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
            else if (a.StartsWith("--e=", StringComparison.Ordinal)) energies = List(a.Substring(4));
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--pow2=", StringComparison.Ordinal)) pow2 = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
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
            Console.Error.WriteLine("у геометрии нет DS_Fwhm662 — допуск нулевой, сравнивать нечего");
            return 2;
        }

        Console.WriteLine("{0}: историй {1}, ПШПВ на 662 {2} %, зерно {3}, второй допуск: {4}",
                          Path.GetFileNameWithoutExtension(inPath), n, F(g.FwhmAt662Percent, 2), seed,
                          double.IsNaN(pow2) ? "закон геометрии (GeometryModel.PeakHalfWidthKev)" : "степень " + F(pow2, 3));
        Console.WriteLine("   E, кэВ | ½ПШПВ √E | ½ПШПВ второй | ε(√E) | ε(второй) | √E / второй");
        foreach (double e in energies)
        {
            double wSqrt = Half(g, e, 0.5);
            double w2 = double.IsNaN(pow2) ? g.PeakHalfWidthKev(e) : Half(g, e, pow2);
            double err1, err2;
            double a1 = Node(g, n, seed, e, wSqrt, out err1);
            double a2 = Node(g, n, seed, e, w2, out err2);
            // одно зерно: истории те же, узлы коррелированы — σ отношения
            // берётся с запасом как у независимых (верхняя оценка)
            double s = Math.Sqrt(err1 * err1 + err2 * err2) / 100.0;
            double r = a1 / a2;
            Console.WriteLine("   {0,6} | {1,7} | {2,7} | {3:E4} ± {4} % | {5:E4} ± {6} % | {7} (≤ ±{8} %)",
                              F(e, 1), F(wSqrt, 2), F(w2, 2), a1, F(err1, 2), a2, F(err2, 2), F(r, 4), F(100.0 * s, 2));
        }

        return 0;
    }
}
