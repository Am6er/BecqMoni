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
        /// ⛔ (`A46`) СЧЁТ В УЗЛАХ, А НЕ В ПРОГОНАХ — решение Amber 02.09.2026.
        ///
        /// Прогонов у узла бывает до трёх, и план по ним РОС на глазах: на
        /// четырёх снимках одного расчёта знаменатель прошёл 140 → 155 → 156 →
        /// 157, а полоса при этом пятилась назад. Узлов же ровно столько, сколько
        /// в сетке, и это число не меняется никогда.
        ///
        /// `StartedNodes` — узлов взято в работу (это число стоит в строке хода),
        /// `SettledNodes` — узлов досчитано окончательно, больше проходов им не
        /// нужно (по ним идёт полоса), `TotalNodes` — узлов в сетке.
        /// </summary>
        public int StartedNodes;

        /// <summary>Узлов досчитано окончательно; по ним идёт полоса (`A46`).</summary>
        public int SettledNodes;

        /// <summary>Узлов в сетке; не меняется за прогон (`A46`).</summary>
        public int TotalNodes;

        /// <summary>Энергия последнего посчитанного узла, кэВ.</summary>
        public double LastEnergyKev;

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

        /// <summary>
        /// Доля сделанного, % — ПО ДОСЧИТАННЫМ УЗЛАМ (`A46`).
        ///
        /// Прежде доля шла по цене узлов в потокосекундах, и полоса пятилась
        /// назад: узел, попросивший второго прохода, добавлял работы, и
        /// знаменатель рос быстрее числителя. Узлов же ровно столько, сколько в
        /// сетке, и досчитанный узел досчитан навсегда — полоса идёт только
        /// вперёд и заполняется ровно на последнем узле.
        /// </summary>
        public double Percent
        {
            get
            {
                return this.TotalNodes > 0
                    ? 100.0 * this.SettledNodes / this.TotalNodes
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

            // (`A41`) Цена узла, измеренная на его пробе: потокосекунд на одну
            // историю. По ней и считается остаток — в секундах, а не в историях.
            // Ноль — проба ещё не сделана, цена берётся средней по сделанным.
            // (`A46`) Узел взят в работу и узел досчитан — два разных числа: в
            // строке хода стоит первое, полосой идёт второе.
            bool[] nodeStarted = new bool[grid.Length];
            bool[] nodeSettled = new bool[grid.Length];
            object planLock = new object();

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

                // ⛔ (`A46`) ВРЕМЕНИ В ОТЧЁТЕ БОЛЬШЕ НЕТ — решение Amber
                // 02.09.2026: «уберём время совсем, ETA всегда врёт».
                //
                // Здесь стояла оценка остатка, и её чинили дважды: `A41` свела
                // её к секундам по замеренной цене каждого узла, `A44` — к
                // пересчёту по фактической скорости этого же счёта. Обе правки
                // делали её точнее (2.5 раза мимо → 1.3), но точной она не
                // стала и стать не могла: план дописывается по ходу, и пока
                // пробная фаза не кончилась, никто не знает, скольким узлам
                // понадобится второй проход и какой.
                //
                // Вместо прогноза считаются УЗЛЫ. Их число постоянно, и по ним
                // видно ровно то, что происходит: сколько взято в работу и
                // сколько досчитано окончательно.
                int started = 0, settled = 0;
                lock (planLock)
                {
                    for (int i = 0; i < grid.Length; i++)
                    {
                        if (nodeStarted[i])
                        {
                            started++;
                        }

                        if (nodeSettled[i])
                        {
                            settled++;
                        }
                    }
                }

                progress.Report(new ResponseMatrixProgress
                {
                    Done = completed,
                    Total = total,
                    DoneHistories = spent,
                    TotalHistories = plan,
                    LastEnergyKev = grid[index],
                    StartedNodes = started,
                    SettledNodes = settled,
                    TotalNodes = grid.Length
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
                        lock (planLock)
                        {
                            nodeStarted[index] = true;
                        }

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

                        // (`A46`) Узел досчитан, если следующего прохода ему не
                        // положено: по таким и идёт полоса хода.
                        lock (planLock)
                        {
                            nodeSettled[index] = next == 0;
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
                // ⛔ (`S130`) Три ключа физики 02.09.2026. Без них настройки
                // их не доносили, и штатный прогон всегда считал выключенную
                // физику — то есть замеры `S125`–`S127` были невыполнимы.
                XcomPairThreshold = options.XcomPairThreshold,
                PositronTransport = options.PositronTransport,
                PositronOffset = options.PositronOffset,
                RayleighToCrystal = options.RayleighToCrystal,
                // ⛔ (`A57`) Оценщик континуума — тем же путём, что физика:
                // не доехав до построителя, ключ мёртв.
                AnalogConeSampling = options.AnalogConeSampling,
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

        // ⛔ (`A46`) ПРЕДВАРИТЕЛЬНОЙ ОЦЕНКИ ВРЕМЕНИ БОЛЬШЕ НЕТ — решение Amber
        // 02.09.2026 «убирай ETA, оно всегда врёт».
        //
        // Здесь стоял `EstimateSeconds` со всей своей машинерией: пять проб по
        // сетке, разностный замер цены истории, замер фактической пропускной
        // способности потоков и поправка на хвост жадной раскладки (`W27`,
        // `A44`). Её довели с «2.5 раза мимо» до 10…15 %, но точной она стать
        // не может: план дописывается по ходу счёта, и до конца пробной фазы
        // неизвестно, скольким узлам понадобится второй проход и какой.
        //
        // Стоила она при этом полторы-две секунды при КАЖДОЙ правке поля в
        // форме. Возвращать — только вместе с ответом на вопрос, откуда взять
        // план заранее; код лежит в коммите 818732b2.
    }
}
