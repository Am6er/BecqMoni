using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PeakBinLossProbe
{
    /// <summary>
    /// «ПИЛА» Σ СТРОКИ ОТКЛИКА ПО УЗЛАМ СЕТКИ — мерка `AMBER50` (П122, 22.09.2026).
    ///
    /// Посылка строки: аналоговый обход континуума
    /// (<see cref="EfficiencySimulator"/>, `AnalogContinuumRun`) отбрасывал историю,
    /// округлившуюся в бин пика, но не попавшую в допуск пика, а затем перезаписывал
    /// бин `peak − 1`, куда ту же историю кладёт взвешенная ветвь (`BinOf`). Класс
    /// непуст на узлах, лёгших ВЫШЕ центра своего бина: `δ = E_узла − peak·бин > 0`,
    /// потерянная полоса недобора — `(допуск, допуск + δ]`. На узлах с `δ ≤ 0` класс
    /// пуст, и правка обязана оставить их ПОБИТОВО прежними — это контроль
    /// неизменности.
    ///
    ///     peakbinlossprobe --geometry=&lt;файл.in&gt; [--emin=30] [--emax=60] [--nodes=16]
    ///                      [--n=3000000] [--threads=0] --out=&lt;префикс&gt;
    ///
    /// Матрица строится ШТАТНЫМ строителем (`ResponseMatrixBuilder.Build`) на
    /// умолчаниях `ResponseMatrixOptions` (склад), сетка `nodes` узлов на
    /// `[emin, emax]`. На каждый узел печатается: E, бин пика, δ, Σ строки по всем
    /// каналам, содержимое бинов `peak − 1` и `peak`, достигнутое число историй.
    /// Файлы:
    ///
    ///   * `&lt;out&gt;_nodes.csv` — сводка по узлам (то, что читается глазами);
    ///   * `&lt;out&gt;_rows.csv` — ПОЛНАЯ выписка `узел;канал;бин;значение` с
    ///     `float.ToString("R")` — для побитового сравнения ДО/ПОСЛЕ.
    ///
    /// ⚠ Своего вызова симулятора здесь нет нарочно (`S37`): узлы считаются тем
    /// же строителем, что и склад, зерно — от номера узла (воспроизводимо при любом
    /// числе потоков). ⛔ В склад проба НЕ пишет: матрица живёт в памяти.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            string outPrefix = null;
            double emin = 30.0, emax = 60.0;
            int nodes = 16;
            int histories = 0;
            int threads = 0;

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPrefix = a.Substring(6);
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) emin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) emax = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            if (geometryPath == null || outPrefix == null)
            {
                Console.Error.WriteLine("нужны --geometry=<файл .in> и --out=<префикс>");
                return 2;
            }

            if (!File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нет файла геометрии: {0}", geometryPath);
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var options = new ResponseMatrixOptions();
            options.NodeCount = nodes;
            options.MinEnergyKev = emin;
            options.MaxEnergyKev = emax;
            options.ResolveEdges = false;
            if (histories > 0)
            {
                options.Histories = histories;
            }

            if (threads > 0)
            {
                options.Threads = threads;
            }

            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("физика: PhysicsVersion = {0}; настройки — умолчания ResponseMatrixOptions, "
                              + "PeakToleranceHalfBin={1}, бин {2} кэВ, историй на узел {3}, узлов {4} на [{5}, {6}]",
                              ResponseMatrix.PhysicsVersion, options.PeakToleranceHalfBin ? "ВКЛ" : "ВЫКЛ",
                              F(options.BinKev, 2), options.Histories, nodes, F(emin, 3), F(emax, 3));

            DateTime started = DateTime.Now;
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null, CancellationToken.None);
            if (matrix == null || !matrix.HasChannels)
            {
                Console.Error.WriteLine("⛔ матрица не построилась");
                return 1;
            }

            Console.WriteLine("посчитано за {0} с; клеймо {1}", F((DateTime.Now - started).TotalSeconds, 1), matrix.Stamp);
            Console.WriteLine();
            Console.WriteLine("{0,4} {1,10} {2,5} {3,8} {4,14} {5,14} {6,14} {7,10}",
                              "узел", "E_кэВ", "peak", "delta", "sum_row", "bin_peak-1", "bin_peak", "историй");

            int channels = matrix.ChannelRows.Length;
            var nodeRows = new List<string>();
            nodeRows.Add("node;e_kev;peak;delta_kev;sum_row;bin_peak_m1;bin_peak;histories;noise_pct");
            var fullRows = new List<string>();
            fullRows.Add("node;e_kev;channel;bin;value");
            for (int i = 0; i < matrix.Energies.Length; i++)
            {
                double e = matrix.Energies[i];
                int peak = EfficiencySimulator.PeakBin(e, matrix.BinKev);
                double delta = e - peak * matrix.BinKev;
                double sum = 0.0, below = 0.0, top = 0.0;
                for (int c = 0; c < channels; c++)
                {
                    float[] row = matrix.ChannelRows[c][i];
                    if (row == null)
                    {
                        continue;
                    }

                    for (int b = 0; b < row.Length; b++)
                    {
                        sum += row[b];
                        if (b == peak - 1) below += row[b];
                        if (b == peak) top += row[b];
                        if (row[b] != 0.0f)
                        {
                            fullRows.Add(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4}",
                                                       i, e.ToString("R", CultureInfo.InvariantCulture), c, b,
                                                       row[b].ToString("R", CultureInfo.InvariantCulture)));
                        }
                    }
                }

                long n = matrix.NodeHistories != null && i < matrix.NodeHistories.Length ? matrix.NodeHistories[i] : -1;
                double noise = matrix.NodeErrors != null && i < matrix.NodeErrors.Length ? matrix.NodeErrors[i] : double.NaN;
                Console.WriteLine("{0,4} {1,10} {2,5} {3,8} {4,14} {5,14} {6,14} {7,10}",
                                  i, F(e, 4), peak, F(delta, 4), E(sum), E(below), E(top), n);
                nodeRows.Add(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4};{5};{6};{7};{8}",
                                           i, e.ToString("R", CultureInfo.InvariantCulture), peak,
                                           delta.ToString("R", CultureInfo.InvariantCulture),
                                           sum.ToString("R", CultureInfo.InvariantCulture),
                                           below.ToString("R", CultureInfo.InvariantCulture),
                                           top.ToString("R", CultureInfo.InvariantCulture), n,
                                           noise.ToString("R", CultureInfo.InvariantCulture)));
            }

            File.WriteAllLines(outPrefix + "_nodes.csv", nodeRows, new UTF8Encoding(false));
            File.WriteAllLines(outPrefix + "_rows.csv", fullRows, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("записано: {0}_nodes.csv ({1} узлов), {0}_rows.csv ({2} ненулевых значений)",
                              outPrefix, nodeRows.Count - 1, fullRows.Count - 1);
            return 0;
        }

        // ⛔ Разделитель дробной части — ТОЧКА, культурой инвариантной и явной
        // (правило Amber 05.09.2026).
        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static string E(double value)
        {
            return value.ToString("E6", CultureInfo.InvariantCulture);
        }
    }
}
