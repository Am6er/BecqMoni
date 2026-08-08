using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Ход построения — для прогрессбара и оценки остатка.</summary>
    public sealed class ResponseMatrixProgress
    {
        public int Done;

        public int Total;

        public double ElapsedSeconds;

        /// <summary>Оценка остатка, с; отрицательная — пока не о чем судить.</summary>
        public double RemainingSeconds = -1.0;

        /// <summary>Энергия последнего посчитанного узла, кэВ.</summary>
        public double LastEnergyKev;

        public double Percent
        {
            get { return this.Total > 0 ? 100.0 * this.Done / this.Total : 0.0; }
        }
    }

    /// <summary>
    /// Считает матрицу отклика по геометрии: узел сетки — один прогон
    /// <see cref="EfficiencySimulator.Response"/>.
    ///
    /// ПАРАЛЛЕЛЬНО, и с двумя оговорками, которые важнее скорости.
    ///
    /// 1. **На поток — свой симулятор.** У него изменяемое состояние потока
    ///    случайных чисел, и один объект на всех означал бы гонку, а вместе с
    ///    ней невоспроизводимый результат.
    /// 2. **Зерно берётся от НОМЕРА УЗЛА, а не от порядка выполнения.** Иначе
    ///    матрица зависела бы от того, какой поток успел раньше, и повторный
    ///    счёт давал бы другие числа. При такой раздаче результат один и тот же
    ///    при любом числе потоков — это проверяется пробой.
    ///
    /// Оценка остатка считается по факту: узлы наверху шкалы дороже нижних
    /// (больше рассеяний до полного поглощения), поэтому пропорция «сделано к
    /// общему» врёт, и остаток берётся от среднего времени УЖЕ посчитанных.
    /// </summary>
    public static class ResponseMatrixBuilder
    {
        public static ResponseMatrix Build(GeometryModel geometry, ResponseMatrixOptions options,
                                           IProgress<ResponseMatrixProgress> progress,
                                           CancellationToken cancellation)
        {
            if (geometry == null)
            {
                throw new ArgumentNullException("geometry");
            }

            if (options == null)
            {
                options = new ResponseMatrixOptions();
            }

            double[] grid = options.BuildGrid();
            // Ошибка континуума по узлам — своей ячейкой на узел, без общей
            // переменной: Parallel.For, а максимум нужен один раз в конце.
            double[] continuumError = new double[grid.Length];
            float[][][] channelRows = new float[EfficiencySimulator.ResponseChannelCount][][];
            for (int c = 0; c < channelRows.Length; c++)
            {
                channelRows[c] = new float[grid.Length][];
            }
            var watch = Stopwatch.StartNew();
            int done = 0;

            int threads = options.Threads > 0
                ? options.Threads
                : Math.Max(1, Environment.ProcessorCount - 1);

            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = threads,
                CancellationToken = cancellation
            };

            Parallel.For(0, grid.Length, parallel, index =>
            {
                cancellation.ThrowIfCancellationRequested();

                EfficiencySimulator sim = MakeSimulator(geometry, options, index);
                double relativeError;
                double[][] histograms = sim.ResponseByChannel(grid[index], options.BinKev, out relativeError);
                continuumError[index] = sim.LastContinuumRelativeError;

                for (int c = 0; c < histograms.Length; c++)
                {
                    double[] histogram = histograms[c];
                    // Пустой канал (вылет 511 ниже порога пар) кладётся строкой
                    // нулевой длины: в файле он занимает четыре байта, а не
                    // полторы тысячи нулей на каждый узел.
                    bool any = false;
                    for (int b = 0; b < histogram.Length && !any; b++)
                    {
                        any = histogram[b] > 0.0;
                    }

                    float[] row = new float[any ? histogram.Length : 0];
                    for (int b = 0; b < row.Length; b++)
                    {
                        row[b] = (float)histogram[b];
                    }

                    channelRows[c][index] = row;
                }

                if (progress != null)
                {
                    int completed = Interlocked.Increment(ref done);
                    double elapsed = watch.Elapsed.TotalSeconds;
                    progress.Report(new ResponseMatrixProgress
                    {
                        Done = completed,
                        Total = grid.Length,
                        ElapsedSeconds = elapsed,
                        LastEnergyKev = grid[index],
                        RemainingSeconds = completed > 0
                            ? elapsed / completed * (grid.Length - completed)
                            : -1.0
                    });
                }
            });

            watch.Stop();
            double worstContinuum = 0.0;
            foreach (double e in continuumError)
            {
                if (e > worstContinuum)
                {
                    worstContinuum = e;
                }
            }

            ResponseMatrix matrix = new ResponseMatrix
            {
                ContinuumRelativeError = worstContinuum,
                Energies = grid,
                BinKev = options.BinKev,
                ChannelRows = channelRows,
                Histories = options.Histories,
                Options = options.Clone(),
                Stamp = ResponseMatrix.ComputeStamp(geometry, options),
                CreatedUtc = DateTime.UtcNow,
                BuildSeconds = watch.Elapsed.TotalSeconds
            };

            matrix.RebuildTotals();
            return matrix;
        }

        /// <summary>
        /// Симулятор одного узла. Геометрия копируется: сцена строится внутри
        /// симулятора по модели, и делить одну модель между потоками — значит
        /// однажды поймать её правку из другого места.
        /// </summary>
        static EfficiencySimulator MakeSimulator(GeometryModel geometry, ResponseMatrixOptions options, int index)
        {
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = options.Histories,
                XrayEscape = options.XrayEscape,
                CoherentPassesThrough = options.CoherentPassesThrough,
                Bremsstrahlung = options.Bremsstrahlung,
                SingleScatter = options.SingleScatter,
                LightNonproportionality = options.LightNonproportionality,
                AnalogContinuum = options.AnalogContinuum,
                BoundCompton = options.BoundScattering,
                DopplerBroadening = options.BoundScattering,
                RayleighScatter = options.BoundScattering,
                BremFromData = options.BremFromData,
                PeakHalfWidthKev = 0.0
            };

            // Зерно от номера узла: результат не должен зависеть от того, какой
            // поток дошёл до этого узла первым.
            sim.ResetStream((ulong)sim.Seed + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
            return sim;
        }

        /// <summary>
        /// Грубая оценка времени до счёта — чтобы форма могла сказать «около
        /// стольки-то», не запуская построение. Меряется одним узлом в середине
        /// шкалы и умножается на число узлов с поправкой на потоки.
        /// </summary>
        public static double EstimateSeconds(GeometryModel geometry, ResponseMatrixOptions options)
        {
            if (geometry == null)
            {
                return 0.0;
            }

            if (options == null)
            {
                options = new ResponseMatrixOptions();
            }

            double[] grid = options.BuildGrid();
            double probeEnergy = grid[grid.Length / 2];

            // Пробный узел считается уменьшенным числом историй: нам нужна
            // скорость, а не число.
            ResponseMatrixOptions probe = options.Clone();
            probe.Histories = Math.Max(2000, options.Histories / 50);

            EfficiencySimulator sim = MakeSimulator(geometry, probe, 0);
            var watch = Stopwatch.StartNew();
            double relativeError;
            sim.Response(probeEnergy, options.BinKev, out relativeError);
            watch.Stop();

            double perNode = watch.Elapsed.TotalSeconds * options.Histories / probe.Histories;
            int threads = options.Threads > 0
                ? options.Threads
                : Math.Max(1, Environment.ProcessorCount - 1);
            return perNode * grid.Length / threads;
        }
    }
}
