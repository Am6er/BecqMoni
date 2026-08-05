using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ResponseShapeProbe
{
    /// <summary>
    /// ГДЕ по шкале сидит расхождение отклика, взятого из матрицы, с прямым
    /// прогоном. `ResponseInterpProbe` отвечает «сколько», эта — «где».
    ///
    /// Заведена под конкретную находку: на 150 кэВ расхождение формы 7.5 % при
    /// шуме 0.5 %, и оно почти одинаково при шаге сетки 15 и 7 кэВ. Ошибка
    /// интерполяции обязана падать с шагом; не падающая — это не интерполяция,
    /// а что-то, что стоит на месте. Гадать бесполезно, надо смотреть профиль.
    ///
    /// Печатаются три величины по полосам шкалы: эталон, отклик из матрицы
    /// между узлами и отклик из матрицы НА УЗЛЕ. Последний — нулевая линия: там
    /// интерполяции нет вовсе, и всё, что в нём видно, это статистика плюс
    /// разница самих прогонов.
    ///
    /// Отдельно называются окна, в которых расхождение осмысленно ожидать:
    /// пик полного поглощения, вылет характеристического K-рентгена (для CsI —
    /// 28–33 кэВ ниже линии, см. tools/tccfcalc/README.md §5.1), комптоновский
    /// край и область обратного рассеяния.
    ///
    ///     responseshapeprobe --geometry=X.in [--nodes=100] [--energy=149]
    ///                        [--n=300000] [--ref=2000000] [--bin=2] [--fwhm=7]
    ///                        [--bands=40] [--raw]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            int nodeCount = 100;
            int histories = 300000;
            int refHistories = 2000000;
            double binKev = 2.0;
            double resolution = 0.07;
            int bands = 40;
            bool raw = false;
            bool single = true, xray = true, coherent = true, brems = true;
            var energies = new List<double> { 149.0 };

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodeCount = int.Parse(a.Substring(8));
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4));
                else if (a.StartsWith("--ref=", StringComparison.Ordinal)) refHistories = int.Parse(a.Substring(6));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--fwhm=", StringComparison.Ordinal)) resolution = 0.01 * double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bands=", StringComparison.Ordinal)) bands = int.Parse(a.Substring(8));
                else if (a == "--raw") raw = true;
                // Аблация физики. Ключи меняются СРАЗУ И В МАТРИЦЕ, И В ЭТАЛОНЕ:
                // расхождение меряется внутри одной модели, и разная физика по
                // сторонам мерила бы разницу моделей, а не переноса.
                else if (a == "--no-single") single = false;
                else if (a == "--no-xray") xray = false;
                else if (a == "--no-coherent-pass") coherent = false;
                else if (a == "--no-brems") brems = false;
                else if (a.StartsWith("--energy=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string part in a.Substring(9).Split(','))
                    {
                        energies.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    }
                }
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var options = new ResponseMatrixOptions
            {
                NodeCount = nodeCount,
                Histories = histories,
                BinKev = binKev,
                SingleScatter = single,
                XrayEscape = xray,
                CoherentPassesThrough = coherent,
                Bremsstrahlung = brems
            };

            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("сетка {0} узлов, {1} историй на узел, {2} на эталон, бин {3:F2} кэВ",
                              nodeCount, histories, refHistories, binKev);
            Console.WriteLine("физика: рассеяние до кристалла {0}, вылет рентгена {1}, когерентное сквозь {2}, тормозное {3}",
                              single ? "да" : "НЕТ", xray ? "да" : "НЕТ",
                              coherent ? "да" : "НЕТ", brems ? "да" : "НЕТ");

            double[] grid = options.BuildGrid();
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null, CancellationToken.None);
            Console.WriteLine("матрица построена за {0:F0} с", matrix.BuildSeconds);

            foreach (double anchor in energies)
            {
                int lo = -1;
                for (int i = 0; i + 1 < grid.Length; i++)
                {
                    if (grid[i] <= anchor && anchor < grid[i + 1])
                    {
                        lo = i;
                        break;
                    }
                }

                if (lo < 0)
                {
                    Console.WriteLine("{0:F0} кэВ вне сетки", anchor);
                    continue;
                }

                double mid = 0.5 * (grid[lo] + grid[lo + 1]);
                double node = grid[lo];

                // Оба эталона считаются разом: они независимы, а ядер много.
                double[][] exact = new double[2][];
                Parallel.For(0, 2, i =>
                {
                    exact[i] = Direct(geometry, i == 0 ? mid : node, binKev, refHistories, i, options);
                });

                double[] gotMid = matrix.Evaluate(mid, EfficiencySimulator.PeakBin(mid, binKev) + 1);
                double[] gotNode = matrix.Evaluate(node, EfficiencySimulator.PeakBin(node, binKev) + 1);

                Console.WriteLine();
                Console.WriteLine("=== {0:F1} кэВ (между узлами {1:F1} и {2:F1}, шаг {3:F1}) ===",
                                  mid, grid[lo], grid[lo + 1], grid[lo + 1] - grid[lo]);
                Console.WriteLine("длины: эталон {0}, матрица {1}; узел: эталон {2}, матрица {3}",
                                  exact[0].Length, gotMid.Length, exact[1].Length, gotNode.Length);

                double[] a = raw ? gotMid : Broaden(gotMid, binKev, resolution);
                double[] b = raw ? exact[0] : Broaden(exact[0], binKev, resolution);
                double[] c = raw ? gotNode : Broaden(gotNode, binKev, resolution);
                double[] d = raw ? exact[1] : Broaden(exact[1], binKev, resolution);

                Report(mid, a, b, c, d, binKev, resolution, bands);
            }

            return 0;
        }

        /// <summary>
        /// Профиль расхождения по полосам плюс именованные окна. Всё в долях от
        /// полной суммы эталона — чтобы числа складывались в ту же «форму»,
        /// которой меряет соседняя проба.
        /// </summary>
        static void Report(double lineEnergy, double[] mid, double[] exactMid, double[] node, double[] exactNode,
                           double binKev, double resolution, int bands)
        {
            double total = Sum(exactMid);
            if (!(total > 0.0))
            {
                Console.WriteLine("эталон пуст");
                return;
            }

            double nodeTotal = Sum(exactNode);
            int length = Math.Max(mid.Length, exactMid.Length);
            double width = lineEnergy / bands;

            Console.WriteLine();
            Console.WriteLine("{0,14} {1,12} {2,12} {3,10} {4,10}",
                              "полоса, кэВ", "эталон", "матрица", "Δ, % от Σ", "шум, %");

            for (int k = 0; k < bands + 2; k++)
            {
                int from = (int)Math.Floor(k * width / binKev);
                int to = (int)Math.Floor((k + 1) * width / binKev) - 1;
                if (from >= length)
                {
                    break;
                }

                double e = Window(exactMid, from, to);
                double g = Window(mid, from, to);
                double en = Window(exactNode, from, to);
                double gn = Window(node, from, to);

                double delta = 100.0 * (g - e) / total;
                double noise = nodeTotal > 0.0 ? 100.0 * (gn - en) / nodeTotal : 0.0;
                if (Math.Abs(delta) < 0.02 && Math.Abs(noise) < 0.02)
                {
                    continue;
                }

                Console.WriteLine("{0,14} {1,12:E3} {2,12:E3} {3,10:F2} {4,10:F2}",
                                  string.Format("{0:F0}–{1:F0}", from * binKev, (to + 1) * binKev),
                                  e, g, delta, noise);
            }

            // Именованные окна. Комптоновский край и обратное рассеяние — из
            // кинематики, вылет — из энергий K-линий иода и цезия (28.6 и 30.9),
            // окна расширены на полторы ПШПВ в обе стороны.
            double fwhm = resolution * Math.Sqrt(662.0 * lineEnergy);
            double edge = lineEnergy / (1.0 + 2.0 * lineEnergy / 511.0);
            Console.WriteLine();
            Console.WriteLine("{0,-22} {1,10} {2,10} {3,10}", "окно", "Δ, % от Σ", "|Δ|, %", "шум |Δ|, %");
            Named("пик полного погл.", lineEnergy - 1.5 * fwhm, lineEnergy + 1.5 * fwhm);
            Named("вылет K-рентгена", lineEnergy - 33.2 - 1.5 * fwhm, lineEnergy - 28.0 + 1.5 * fwhm);
            Named("комптоновский край", edge - 1.5 * fwhm, edge + 1.5 * fwhm);
            Named("обратное рассеяние", lineEnergy - edge - 1.5 * fwhm, lineEnergy - edge + 1.5 * fwhm);
            Named("ниже 30 кэВ", 0.0, 30.0);
            Named("всё", 0.0, lineEnergy * 2.0);

            void Named(string name, double from, double to)
            {
                int f = (int)Math.Floor(from / binKev);
                int t = (int)Math.Ceiling(to / binKev);
                double e = Window(exactMid, f, t), g = Window(mid, f, t);
                double en = Window(exactNode, f, t), gn = Window(node, f, t);
                double abs = AbsWindow(mid, exactMid, f, t);
                double absNoise = AbsWindow(node, exactNode, f, t);
                Console.WriteLine("{0,-22} {1,10:F2} {2,10:F2} {3,10:F2}",
                                  name,
                                  100.0 * (g - e) / total,
                                  100.0 * abs / total,
                                  nodeTotal > 0.0 ? 100.0 * absNoise / nodeTotal : 0.0);
            }
        }

        static double Sum(double[] values)
        {
            double total = 0.0;
            foreach (double v in values)
            {
                total += v;
            }

            return total;
        }

        static double Window(double[] values, int from, int to)
        {
            double total = 0.0;
            for (int i = Math.Max(0, from); i <= to && i < values.Length; i++)
            {
                total += values[i];
            }

            return total;
        }

        static double AbsWindow(double[] got, double[] exact, int from, int to)
        {
            double total = 0.0;
            int length = Math.Max(got.Length, exact.Length);
            for (int i = Math.Max(0, from); i <= to && i < length; i++)
            {
                double g = i < got.Length ? got[i] : 0.0;
                double e = i < exact.Length ? exact[i] : 0.0;
                total += Math.Abs(g - e);
            }

            return total;
        }

        static double[] Broaden(double[] histogram, double binKev, double resolution)
        {
            double[] result = new double[histogram.Length];
            for (int b = 0; b < histogram.Length; b++)
            {
                double value = histogram[b];
                if (!(value > 0.0))
                {
                    continue;
                }

                double energy = b * binKev;
                double fwhm = resolution * Math.Sqrt(662.0 * Math.Max(1.0, energy));
                double sigma = fwhm / 2.354820045 / binKev;
                if (!(sigma > 0.25))
                {
                    result[b] += value;
                    continue;
                }

                int span = (int)Math.Ceiling(4.0 * sigma);
                double norm = 0.0;
                for (int k = -span; k <= span; k++)
                {
                    double t = k / sigma;
                    norm += Math.Exp(-0.5 * t * t);
                }

                for (int k = -span; k <= span; k++)
                {
                    double t = k / sigma;
                    int at = b + k;
                    if (at < 0) at = 0;
                    if (at >= result.Length) at = result.Length - 1;
                    result[at] += value * Math.Exp(-0.5 * t * t) / norm;
                }
            }

            return result;
        }

        static double[] Direct(GeometryModel geometry, double energyKev, double binKev, int histories, int index,
                               ResponseMatrixOptions options)
        {
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = histories,
                XrayEscape = options.XrayEscape,
                CoherentPassesThrough = options.CoherentPassesThrough,
                Bremsstrahlung = options.Bremsstrahlung,
                SingleScatter = options.SingleScatter,
                PeakHalfWidthKev = 0.0
            };

            sim.ResetStream(0xC0FFEE0000000000UL + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
            double error;
            return sim.Response(energyKev, binKev, out error);
        }
    }
}
