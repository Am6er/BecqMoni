using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Ход построения — для прогрессбара и оценки остатка.
    ///
    /// ⛔ <see cref="Done"/> и <see cref="Total"/> считают ПРОГОНЫ УЗЛОВ, а не
    /// узлы, и это не придирка к слову (`W27`). При останове по шуму — а это
    /// УМОЛЧАНИЕ (<c>ContinuumErrorTarget = 3.0</c>) — сетка проходится не один
    /// раз: проба по всем узлам плюс до двух уточняющих раундов по недобравшим
    /// (<c>MaxNodePasses</c>). Прежде <see cref="Done"/> считал прогоны, а
    /// <see cref="Total"/> стоял на числе узлов, отчего на экране появлялось
    /// «Node 121 of 100», полоса замирала полной с сотого прогона, а остаток
    /// уходил в минус и рисовался знаком вопроса — ровно в той фазе, где он и
    /// нужен.
    ///
    /// План работ заранее НЕ ИЗВЕСТЕН и известен быть не может: сколько узлов
    /// попросит второго прохода, видно только по достигнутому ими шуму. Поэтому
    /// <see cref="Total"/> и <see cref="TotalHistories"/> РАСТУТ по ходу счёта:
    /// закончив прогон узла, строитель тут же считает, нужен ли узлу следующий,
    /// и добавляет его в план. Так план не прыгает ступенькой на границе фаз, а
    /// расширяется по одному узлу — и знаменатель всегда честен на один раунд
    /// вперёд.
    /// </summary>
    public sealed class ResponseMatrixProgress
    {
        /// <summary>Прогонов узлов закончено.</summary>
        public int Done;

        /// <summary>Прогонов узлов в плане на этот момент; растёт по ходу счёта.</summary>
        public int Total;

        /// <summary>
        /// Историй сосчитано. Долю ведём по ним, а не по прогонам: пробный
        /// проход идёт по <c>Histories/PilotDivisor</c> (вдесятеро меньше), а
        /// уточняющий — до <c>Histories × MaxHistoriesFactor</c> (в восемь раз
        /// больше), то есть прогон прогону дороже в восемьдесят раз и мерить
        /// время их числом нельзя.
        /// </summary>
        public long DoneHistories;

        /// <summary>Историй в плане на этот момент; растёт вместе с <see cref="Total"/>.</summary>
        public long TotalHistories;

        public double ElapsedSeconds;

        /// <summary>Оценка остатка, с; отрицательная — пока не о чем судить.</summary>
        public double RemainingSeconds = -1.0;

        /// <summary>Энергия последнего посчитанного узла, кэВ.</summary>
        public double LastEnergyKev;

        /// <summary>Доля сделанного, % — ПО ИСТОРИЯМ (см. <see cref="DoneHistories"/>).</summary>
        public double Percent
        {
            get
            {
                return this.TotalHistories > 0L
                    ? 100.0 * this.DoneHistories / this.TotalHistories
                    : 0.0;
            }
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
    ///
    /// 3. **Узлы раздаются ПО ОДНОМУ и дорогими вперёд** (`T35`, 17.08.2026).
    ///    Это не украшение: `Parallel.For` по диапазону нарезает его статически,
    ///    кусками подряд идущих номеров, — а стоимость узла растёт с энергией.
    ///    Работник, которому достался нижний кусок, отрабатывал его быстро и
    ///    ПАРКОВАЛСЯ до конца прогона. Измерено на живом счёте: стабильно 10–11
    ///    потоков Running и 9–10 в ожидании `UserRequest` (то есть без работы), и
    ///    доля занятых ядер держалась 11.2 из 15 на всех девяноста прогонах — от
    ///    12-секундных сцен до 133-секундных. Постоянство и обмануло сначала:
    ///    перекос от статической нарезки пропорционален, поэтому от масштаба
    ///    сцены не зависит и на «хвост» не похож.
    ///
    ///    Лечится раздачей по одному узлу (`Partitioner.Create(..., 1)`) плюс
    ///    порядком «дорогие первыми»: это классическая LPT-раскладка, при ней
    ///    хвост не длиннее одного самого дорогого узла. Порядок на РЕЗУЛЬТАТ не
    ///    влияет по пункту 2 — зерно у узла от его номера, а не от очереди.
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

            double[] grid = options.BuildGrid(geometry);
            // Ошибка континуума по узлам — своей ячейкой на узел, без общей
            // переменной: Parallel.For, а максимум нужен один раз в конце.
            double[] continuumError = new double[grid.Length];
            float[][][] channelRows = new float[EfficiencySimulator.ResponseChannelCount][][];
            for (int c = 0; c < channelRows.Length; c++)
            {
                channelRows[c] = new float[grid.Length][];
            }
            var watch = Stopwatch.StartNew();

            // Счётчики хода (`W27`). Прогоны и истории ведутся ПОРОЗНЬ: подпись
            // «узел такой-то из стольких-то» читается прогонами, а полоса и
            // остаток — историями, потому что прогон прогону не ровня.
            int done = 0;
            int planned = 0;
            long doneHistories = 0L;
            long plannedHistories = 0L;

            int threads = options.Threads > 0
                ? options.Threads
                : Math.Max(1, Environment.ProcessorCount - 1);

            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = threads,
                CancellationToken = cancellation
            };

            // Поузловые счётчики замера `S55` — заводятся под размер сетки.
            NodeDropped = new long[grid.Length];
            NodeScored = new long[grid.Length];
            NodeDroppedScattered = new long[grid.Length];

            long[] nodeHistories = new long[grid.Length];
            double[] nodeSeconds = new double[grid.Length];
            int[] nodeLast = new int[grid.Length];

            // Уложить гистограммы узла в строки матрицы.
            Action<int, double[][]> store = (index, histograms) =>
            {
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
            };

            int nominal = Math.Max(1, options.Histories);
            bool adaptive = options.ContinuumErrorTarget > 0.0;
            int pilot = adaptive
                ? Math.Min(nominal, Math.Max(MinPilotHistories,
                                             nominal / Math.Max(1, options.PilotDivisor)))
                : nominal;
            int cap = (int)Math.Min(int.MaxValue,
                                    (long)nominal * Math.Max(1, options.MaxHistoriesFactor));

            // Прогон узла закончен. `nextHistories` — сколько историй узлу
            // понадобится СЛЕДУЮЩИМ проходом (0 — следующего не будет): узел
            // сам себя и досчитывает до плана, поэтому знаменатель растёт ровно
            // тогда, когда становится известен, а не ступенькой на границе фаз.
            Action<int, int, int> report = (index, histories, nextHistories) =>
            {
                if (progress == null)
                {
                    return;
                }

                if (nextHistories > 0)
                {
                    Interlocked.Increment(ref planned);
                    Interlocked.Add(ref plannedHistories, nextHistories);
                }

                int completed = Interlocked.Increment(ref done);
                long spent = Interlocked.Add(ref doneHistories, histories);
                int total = Volatile.Read(ref planned);
                long plan = Interlocked.Read(ref plannedHistories);
                double elapsed = watch.Elapsed.TotalSeconds;

                // ⚠ Остаток берётся от историй, а не от прогонов, и уйти в
                // минус больше не может: план по построению не меньше
                // сделанного. Отрицательным он остаётся только пока судить не
                // по чему (ни одной истории), и такой остаток форма не
                // показывает вовсе.
                progress.Report(new ResponseMatrixProgress
                {
                    Done = completed,
                    Total = total,
                    DoneHistories = spent,
                    TotalHistories = plan,
                    ElapsedSeconds = elapsed,
                    LastEnergyKev = grid[index],
                    RemainingSeconds = spent > 0L && plan >= spent
                        ? elapsed / spent * (plan - spent)
                        : -1.0
                });
            };

            // Раздача по одному узлу — см. пункт 3 в шапке класса. `order`
            // задаёт, в каком порядке узлы уходят в работу: дорогими вперёд.
            // `pass` — номер прохода, от нуля: по нему узел решает, положен ли
            // ему следующий (<see cref="MaxNodePasses"/>).
            Action<int[], Func<int, int>, int> run = (order, historiesOf, pass) =>
                Parallel.ForEach(Partitioner.Create(0, order.Length, 1), parallel, chunk =>
                {
                    for (int slot = chunk.Item1; slot < chunk.Item2; slot++)
                    {
                        int index = order[slot];
                        cancellation.ThrowIfCancellationRequested();

                        int histories = historiesOf(index);
                        double achieved;
                        // ⚠ Время узла — ПО ЧАСАМ его собственного прохода. При
                        // 15 потоках на 8 физических ядрах оно завышено (поток
                        // снимают с ядра), зато узлы между собой сравнимы — а для
                        // приоритета оптимизации нужно именно это. Числом ЦП его
                        // называть нельзя, и в раскладке оно так и подписано.
                        long ticks0 = Stopwatch.GetTimestamp();
                        double[][] histograms = RunNode(geometry, options, grid[index], index,
                                                        histories, out achieved);
                        nodeSeconds[index] += (double)(Stopwatch.GetTimestamp() - ticks0)
                                              / Stopwatch.Frequency;
                        continuumError[index] = achieved;
                        // Всего потрачено — с учётом выброшенных проходов: только
                        // так видно настоящую цену останова. Последний проход
                        // держится отдельно: от него считается следующий.
                        nodeHistories[index] += histories;
                        nodeLast[index] = histories;
                        store(index, histograms);

                        // Нужен ли узлу ещё проход — считается ЗДЕСЬ, тем же
                        // правилом, по которому вторая фаза его и отберёт
                        // (<see cref="NeededHistories"/>). Второго правила быть
                        // не должно: разойдясь, они дали бы план, по которому
                        // никто не работает.
                        int next = 0;
                        if (adaptive && pass + 1 < MaxNodePasses)
                        {
                            int need = NeededHistories(histories, achieved,
                                                       options.ContinuumErrorTarget, cap);
                            if (need > histories)
                            {
                                next = need;
                            }
                        }

                        report(index, histories, next);
                    }
                });

            // План на первый проход известен целиком: он идёт по всем узлам.
            // Дальше план дописывают сами узлы, из `report`.
            if (progress != null)
            {
                planned = grid.Length;
                plannedHistories = (long)grid.Length * pilot;
            }

            if (!adaptive)
            {
                // Плоский счёт: дорогие узлы наверху шкалы, поэтому вперёд идут
                // они — порядок обратный номерам.
                int[] order = new int[grid.Length];
                for (int i = 0; i < order.Length; i++)
                {
                    order[i] = grid.Length - 1 - i;
                }

                run(order, index => nominal, 0);
            }
            else
            {
                // ⛔ ДВЕ ФАЗЫ, и порядок берётся из ИЗМЕРЕНИЯ, а не из догадки
                // о том, какие узлы дороже (`T35`, решение Amber 17.08.2026).
                //
                // Догадка уже подвела: при плоском счёте дороже узлы НАВЕРХУ
                // шкалы, а при останове по шуму — ВНИЗУ (внизу мало континуума,
                // шум высокий, узел упирается в потолок), то есть профиль
                // стоимости переворачивается вместе с режимом. Раздача «сверху
                // вниз» в режиме останова отдавала самые тяжёлые узлы последними
                // и роняла занятость ядер.
                //
                // Поэтому: фаза 1 — проба ПО ВСЕМ узлам, она и так считалась,
                // только выбрасывалась; из неё известны и достигнутый шум, и
                // нужное число историй. Узлы, которым пробы хватило, готовы — им
                // второй проход не нужен вовсе. Фаза 2 — остальные, В ПОРЯДКЕ
                // УБЫВАНИЯ нужного N, то есть настоящая LPT-раскладка.
                //
                // `pilot` и `cap` подняты в тело метода (`W27`): по ним же
                // считает план счётчик хода, и два вычисления одного числа
                // однажды разошлись бы.
                int[] all = new int[grid.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = grid.Length - 1 - i;
                }

                run(all, index => pilot, 0);

                // Уточняющих раундов не больше двух: оценка нужного N сама
                // шумная, и один промах мимо цели она обычно исправляет, а
                // бесконечно догонять — значит потерять предсказуемость времени.
                int[] want = new int[grid.Length];
                for (int round = 1; round < MaxNodePasses; round++)
                {
                    var heavy = new List<int>();
                    for (int i = 0; i < grid.Length; i++)
                    {
                        want[i] = NeededHistories(nodeLast[i], continuumError[i],
                                                  options.ContinuumErrorTarget, cap);
                        if (want[i] > nodeLast[i])
                        {
                            heavy.Add(i);
                        }
                    }

                    if (heavy.Count == 0)
                    {
                        break;
                    }

                    heavy.Sort((a, b) => want[b].CompareTo(want[a]));
                    run(heavy.ToArray(), index => want[index], round);
                }
            }

            watch.Stop();
            double worstContinuum = 0.0;
            // Взвешенная по вкладу узла ошибка (T15): вес — число набранных
            // узлом событий континуума, а оно из определения ошибки узла
            // (err = 100/√N) выходит как 1/err². См.
            // ResponseMatrix.ContinuumWeightedError.
            double sumInverse = 0.0;
            double sumWeight = 0.0;
            foreach (double e in continuumError)
            {
                if (e > worstContinuum)
                {
                    worstContinuum = e;
                }

                if (e > 0.0)
                {
                    sumInverse += 1.0 / e;
                    sumWeight += 1.0 / (e * e);
                }
            }

            long spentTotal = 0;
            long capNode = 0;
            foreach (long h in nodeHistories)
            {
                spentTotal += h;
                if (h > capNode)
                {
                    capNode = h;
                }
            }

            ResponseMatrix matrix = new ResponseMatrix
            {
                ContinuumRelativeError = worstContinuum,
                ContinuumWeightedError = sumWeight > 0.0 ? sumInverse / sumWeight : 0.0,
                HistoriesSpent = spentTotal,
                HistoriesWorstNode = capNode,
                NodeHistories = nodeHistories,
                NodeErrors = continuumError,
                NodeSeconds = nodeSeconds,
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

        /// <summary>Минимум историй на пробный проход: по десятку событий шум не измерить.</summary>
        const int MinPilotHistories = 2000;

        /// <summary>Проходов на узел: пробный плюс два уточняющих.</summary>
        const int MaxNodePasses = 3;

        /// <summary>
        /// Запас к расчётному числу историй. Сама оценка шумная (её точность —
        /// √2/√N событий пробного прохода), и без запаса половина узлов
        /// промахивалась бы мимо цели на волосок и уходила в лишний проход.
        /// </summary>
        const double HistoriesMargin = 1.15;

        /// <summary>
        /// Один узел до заданного шума (`T35`). Возвращает гистограммы, а через
        /// параметры — достигнутый шум и потраченные истории.
        ///
        /// Почему счёт, а не подбор блоками. Шум узла по построению равен
        /// 100/√N по НАБРАННЫМ событиям континуума
        /// (<see cref="EfficiencySimulator.LastContinuumRelativeError"/>), то есть
        /// зависит от числа историй ровно как 1/√N. Значит по одному дешёвому
        /// проходу нужное число историй вычисляется в лоб:
        /// N = N_пробы · (шум_пробы / цель)². Блочное наращивание давало бы то
        /// же самое за много проходов и с непредсказуемым временем.
        ///
        /// ⚠ Повторный проход считает узел ЗАНОВО, а не досчитывает: зерно у
        /// узла одно и то же (<see cref="MakeSimulator"/> берёт его от номера),
        /// поэтому длинный проход повторяет пробный первыми историями и
        /// результат остаётся воспроизводимым побитово при любом числе потоков.
        /// Цена — выброшенный пробный проход, то есть не больше десятой доли.
        ///
        /// ⚠ Достигнутый шум возвращается ФАКТИЧЕСКИЙ. Узел, которому и потолка
        /// не хватило, остаётся шумным и говорит об этом числом, а не молчит:
        /// на этом стоит вся приёмка матриц (`ContinuumWeightedError`).
        /// </summary>
        static double[][] RunNode(GeometryModel geometry, ResponseMatrixOptions options,
                                  double energyKev, int index, int histories,
                                  out double achieved)
        {
            EfficiencySimulator sim = MakeSimulator(geometry, options, index);
            sim.Histories = Math.Max(1, histories);
            double relativeError;
            double[][] histograms = sim.ResponseByChannel(energyKev, options.BinKev,
                                                          out relativeError);
            achieved = sim.LastContinuumRelativeError;

            // Счётчики работы геометрии — в общую сумму РАЗ на узел, а не на
            // вызов: внутри узла они считаются без блокировок (`T43`).
            Interlocked.Add(ref WalkAt, sim.CountAt);
            Interlocked.Add(ref WalkStep, sim.CountStep);
            Interlocked.Add(ref WalkMu, sim.CountMu);
            Interlocked.Add(ref WalkCollect, sim.CountWalk);
            Interlocked.Add(ref WalkHistories, sim.Histories);
            Interlocked.Add(ref AnalogDropped, sim.CountPeakBinDropped);
            Interlocked.Add(ref AnalogScored, sim.CountAnalogScored);
            if (NodeDropped != null && index < NodeDropped.Length)
            {
                NodeDropped[index] = sim.CountPeakBinDropped;
                NodeScored[index] = sim.CountAnalogScored;
                NodeDroppedScattered[index] = sim.CountPeakBinDroppedScattered;
            }
            return histograms;
        }

        /// <summary>
        /// Сколько раз обход сцены спросил область, границу и ослабление —
        /// суммарно по последнему построению (`T43`). Публичные и обнуляемые:
        /// это мерка для оптимизации, а не свойство матрицы.
        /// </summary>
        public static long WalkAt, WalkStep, WalkMu, WalkHistories, WalkCollect;

        /// <summary>Замер к `S55`: выброшено в бин пика / зачтено, по всем узлам.</summary>
        public static long AnalogDropped, AnalogScored;

        /// <summary>То же поузлово — трендом по энергии, а не суммой.</summary>
        public static long[] NodeDropped, NodeScored, NodeDroppedScattered;

        /// <summary>Обнулить счётчики обхода перед построением.</summary>
        public static void ResetWalkCounters()
        {
            WalkAt = 0;
            WalkStep = 0;
            WalkMu = 0;
            WalkHistories = 0;
            WalkCollect = 0;
            AnalogDropped = 0;
            AnalogScored = 0;
        }

        /// <summary>
        /// Сколько историй нужно узлу, чтобы дойти до цели (`T35`). Возвращает
        /// прежнее число, если цель уже взята или расти некуда.
        ///
        /// Шум узла по построению равен 100/√N по набранным событиям континуума,
        /// то есть зависит от историй как 1/√N, — значит нужное число считается
        /// В ЛОБ, а не подбирается блоками: N = N₀·(шум₀/цель)². Запас нужен
        /// потому, что сама оценка шумная (её точность — порядка 1/√(2N₀)).
        /// </summary>
        static int NeededHistories(int histories, double achieved, double target, int cap)
        {
            if (histories <= 0 || !(target > 0.0) || !(achieved > target) || histories >= cap)
            {
                return Math.Max(histories, 0);
            }

            double ratio = achieved / target;
            long want = (long)Math.Ceiling(histories * ratio * ratio * HistoriesMargin);
            return (int)Math.Min(cap, Math.Max((long)histories + 1, want));
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
                ScatterRouletteWeight = options.ScatterRoulette,
                SampleFluorescenceOutside = options.SampleFluorescence,
                PeakHalfWidthKev = 0.0
            };

            // Зерно от номера узла: результат не должен зависеть от того, какой
            // поток дошёл до этого узла первым. Ноль в настройках — штатное
            // зерно симулятора; иным задаётся НЕЗАВИСИМАЯ выборка тем же кодом,
            // и она — единственная мерка для приёмки «в пределах шума ГСЧ»
            // (`T43`).
            int seed = options.Seed != 0 ? options.Seed : sim.Seed;
            sim.ResetStream((ulong)seed + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
            return sim;
        }

        /// <summary>
        /// Грубая оценка времени до счёта — чтобы форма могла сказать «около
        /// стольки-то», не запуская построение.
        ///
        /// ⛔ Считает ПО ФАКТИЧЕСКОМУ ПЛАНУ, а не по «узел = <c>Histories</c>
        /// историй» (`W27`). Прежняя оценка брала ровно `grid.Length` прогонов
        /// по номиналу каждый и промахивалась вдвойне: при останове по шуму —
        /// а это умолчание — пробный проход идёт по <c>Histories/PilotDivisor</c>
        /// (вдесятеро меньше), а уточняющий доходит до
        /// <c>Histories × MaxHistoriesFactor</c> (в восемь раз больше). На
        /// экране Amber 18.08.2026 это дало «около 0:32» против записанных у
        /// матрицы 40 с.
        ///
        /// ⛔ И узел брался ОДИН, из середины сетки, а в режиме останова он не
        /// представителен: стоимость узла там U-образна — внизу шкалы мало
        /// континуума и узел упирается в потолок историй (`T35`), наверху дорога
        /// сама история (больше рассеяний до поглощения). Поэтому проб ТРИ — низ,
        /// середина, верх, — а по сетке стоимость разносится линейно в
        /// логарифме энергии.
        ///
        /// ⚠ Достигнутый пробой шум пересчитывается на пробный проход законом
        /// 1/√N — тем же, на котором стоит <see cref="NeededHistories"/>. Сама
        /// проба короткая, и точность этой оценки шума порядка 1/√(2N); для
        /// «около стольки-то» этого довольно, для приёмки матрицы — нет, и
        /// приёмка на ней не стоит.
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

            double[] grid = options.BuildGrid(geometry);
            if (grid == null || grid.Length == 0)
            {
                return 0.0;
            }

            int nominal = Math.Max(1, options.Histories);
            bool adaptive = options.ContinuumErrorTarget > 0.0;
            int pilot = adaptive
                ? Math.Min(nominal, Math.Max(MinPilotHistories,
                                             nominal / Math.Max(1, options.PilotDivisor)))
                : nominal;
            int cap = (int)Math.Min(int.MaxValue,
                                    (long)nominal * Math.Max(1, options.MaxHistoriesFactor));

            int[] samples = SampleNodes(grid.Length);

            // Пробный узел считается уменьшенным числом историй: нам нужна
            // скорость, а не число. Делитель учитывает, что проб теперь
            // несколько, — общая работа оценки осталась прежней.
            ResponseMatrixOptions probe = options.Clone();
            probe.Histories = Math.Max(MinPilotHistories, nominal / (50 * samples.Length));

            // ⛔ Фазы считаются ПОРОЗНЬ, и не ради точности ради точности.
            // Пробу видят ВСЕ узлы, а уточнение — только недобравшие, и делить
            // обе на одно число потоков нельзя: на сетке из двенадцати узлов
            // при пятнадцати потоках такая оценка занижала втрое. Занятость
            // фазы ограничена числом узлов В НЕЙ, а не в сетке.
            double[] energies = new double[samples.Length];
            double[] pilotCost = new double[samples.Length];
            double[] refineCost = new double[samples.Length];
            double[] refineShare = new double[samples.Length];
            for (int p = 0; p < samples.Length; p++)
            {
                int index = samples[p];
                EfficiencySimulator sim = MakeSimulator(geometry, probe, index);
                var watch = Stopwatch.StartNew();
                double relativeError;
                sim.Response(grid[index], options.BinKev, out relativeError);
                watch.Stop();

                double perHistory = watch.Elapsed.TotalSeconds / probe.Histories;
                energies[p] = grid[index];
                pilotCost[p] = perHistory * pilot;
                if (!adaptive)
                {
                    continue;
                }

                double achieved = sim.LastContinuumRelativeError > 0.0
                    ? sim.LastContinuumRelativeError
                      * Math.Sqrt((double)probe.Histories / Math.Max(1, pilot))
                    : 0.0;
                int need = NeededHistories(pilot, achieved, options.ContinuumErrorTarget, cap);
                if (need > pilot)
                {
                    // Уточняющий проход считает узел ЗАНОВО, а не досчитывает, —
                    // значит его истории идут отдельной ценой целиком, а не
                    // разностью.
                    refineCost[p] = perHistory * need;
                    refineShare[p] = 1.0;
                }
            }

            double pilotTotal = 0.0, refineTotal = 0.0, heavyNodes = 0.0;
            for (int i = 0; i < grid.Length; i++)
            {
                pilotTotal += InterpolateCost(energies, pilotCost, grid[i]);
                refineTotal += InterpolateCost(energies, refineCost, grid[i]);
                heavyNodes += InterpolateCost(energies, refineShare, grid[i]);
            }

            int threads = options.Threads > 0
                ? options.Threads
                : Math.Max(1, Environment.ProcessorCount - 1);
            double seconds = pilotTotal / Math.Min(threads, Math.Max(1, grid.Length));
            if (refineTotal > 0.0)
            {
                int heavy = (int)Math.Ceiling(heavyNodes);
                seconds += refineTotal / Math.Min(threads, Math.Max(1, heavy));
            }

            return seconds;
        }

        /// <summary>
        /// Узлы под пробу — поровну по шкале, от нижнего до верхнего.
        ///
        /// Их пять, а не один и не три. Один (как было до `W27`) не годится
        /// потому, что стоимость узла в режиме останова U-образна: внизу шкалы
        /// узел упирается в потолок историй, наверху дорога сама история.
        /// Трёх мало по другой причине: нужное число историй меняется с энергией
        /// на порядки и выпукло, а линейная прокладка между редкими точками
        /// выпуклую кривую систематически занижает.
        /// </summary>
        static int[] SampleNodes(int count)
        {
            if (count <= EstimateSamples)
            {
                int[] all = new int[Math.Max(1, count)];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = i;
                }

                return all;
            }

            int[] samples = new int[EstimateSamples];
            for (int i = 0; i < EstimateSamples; i++)
            {
                samples[i] = (int)Math.Round((double)i * (count - 1) / (EstimateSamples - 1));
            }

            return samples;
        }

        /// <summary>Проб под оценку времени; см. <see cref="SampleNodes"/>.</summary>
        const int EstimateSamples = 5;

        /// <summary>
        /// Стоимость узла на энергии <paramref name="energyKev"/> — линейно по
        /// логарифму энергии между пробами; за краями — крайняя проба.
        /// </summary>
        static double InterpolateCost(double[] energies, double[] costs, double energyKev)
        {
            int last = energies.Length - 1;
            if (last <= 0 || energyKev <= energies[0])
            {
                return costs[0];
            }

            if (energyKev >= energies[last])
            {
                return costs[last];
            }

            for (int i = 1; i <= last; i++)
            {
                if (energyKev <= energies[i])
                {
                    double lo = Math.Log(energies[i - 1]);
                    double hi = Math.Log(energies[i]);
                    double t = hi > lo ? (Math.Log(energyKev) - lo) / (hi - lo) : 0.0;
                    return costs[i - 1] + t * (costs[i] - costs[i - 1]);
                }
            }

            return costs[last];
        }
    }
}
