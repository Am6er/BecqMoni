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

        /// <summary>
        /// Потрачено потокосекунд на сосчитанные узлы. Ими же меряется доля и
        /// остаток (`A41`): история истории не ровня — при 2614 кэВ она втрое
        /// дороже, чем при 30, потому что тянет за собой вторичные.
        /// </summary>
        public double DoneNodeSeconds;

        /// <summary>Осталось потокосекунд по замеренной цене узлов (`A41`).</summary>
        public double RemainingNodeSeconds;

        /// <summary>Доля сделанного, % — ПО ЦЕНЕ УЗЛОВ, а не по числу историй.</summary>
        public double Percent
        {
            get
            {
                double total = this.DoneNodeSeconds + this.RemainingNodeSeconds;
                if (total > 0.0)
                {
                    return 100.0 * this.DoneNodeSeconds / total;
                }

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

            // (`A41`) Цена узла, измеренная на его пробе: потокосекунд на одну
            // историю. По ней и считается остаток — в секундах, а не в историях.
            // Ноль — проба ещё не сделана, цена берётся средней по сделанным.
            double[] nodeCost = new double[grid.Length];
            int[] nodeNeed = new int[grid.Length];
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

                // ⛔ (`A41`) ОСТАТОК СЧИТАЕТСЯ В СЕКУНДАХ ПО ЦЕНЕ УЗЛОВ, а не в
                // историях от плана (решение Amber 01.09.2026 «считать точно»).
                //
                // Прежняя оценка прыгала по двум причинам сразу: план в историях
                // РОС по ходу (узел узнаёт о своей добавке только после пробы), и
                // все истории считались равными — а история при 2614 кэВ тянет за
                // собой вторичные и стоит втрое дороже, чем при 30. Теперь у
                // каждого узла есть ЗАМЕРЕННАЯ цена истории (его проба), и остаток
                // складывается из того, что каждому узлу ещё предстоит.
                //
                // ⛔ (`A44`) ОСТАТОК ПЕРЕСЧИТЫВАЕТСЯ ПО ФАКТИЧЕСКОЙ СКОРОСТИ ЭТОГО
                // ЖЕ СЧЁТА, а не делением на число потоков.
                //
                // Работа узла меряется ПО ЧАСАМ ЕГО ПОТОКА, и это не настенное
                // время: при пятнадцати потоках на восьми ядрах поток идёт втрое
                // медленнее, чем в одиночку. Делить эту работу на число потоков
                // верно ровно пока все они заняты — а в конце счёта это не так, и
                // остаток занижался втрое (снимок Amber: 0:31 при факте 1:34).
                // Делить на число ЗАНЯТЫХ тоже неверно: последний узел идёт один и
                // потому втрое быстрее своей замеренной цены — так остаток
                // завышался в шестнадцать раз (96 с при факте 6 с).
                //
                // Коэффициент пересчёта не надо знать: его уже измерил сам счёт.
                // Сделано `doneSeconds` часов потока за `elapsed` настенных секунд,
                // значит впереди `elapsed × remaining / done`. В этом отношении
                // сами собой сидят и просадка от гипертрединга, и падение
                // занятости к концу.
                double doneSeconds = 0.0;
                double remainingSeconds = 0.0;
                int aheadNodes = 0;
                lock (planLock)
                {
                    double costSum = 0.0;
                    double aheadSum = 0.0;
                    int measured = 0;
                    for (int i = 0; i < grid.Length; i++)
                    {
                        doneSeconds += nodeSeconds[i];
                        if (nodeCost[i] > 0.0)
                        {
                            costSum += nodeCost[i];
                            aheadSum += nodeNeed[i] * nodeCost[i];
                            measured++;
                        }
                    }

                    double meanCost = measured > 0 ? costSum / measured : 0.0;
                    double meanAhead = measured > 0 ? aheadSum / measured : 0.0;
                    for (int i = 0; i < grid.Length; i++)
                    {
                        if (nodeSettled[i])
                        {
                            continue;
                        }

                        aheadNodes++;

                        if (nodeCost[i] > 0.0)
                        {
                            // Проба сделана: остался ровно уточняющий проход.
                            remainingSeconds += nodeNeed[i] * nodeCost[i];
                        }
                        else
                        {
                            // Пробы ещё не было: и она сама, и то, что за ней
                            // последует, оцениваются средним по уже измеренным.
                            remainingSeconds += pilot * meanCost + meanAhead;
                        }
                    }
                }

                // ⚠ Пока сделанного нет (самый первый отчёт), пересчитывать не по
                // чему: тогда идёт запасной путь — деление на число потоков,
                // ограниченное числом узлов, которым ещё есть что считать.
                double wallRemaining;
                if (doneSeconds > 0.0 && elapsed > 0.0)
                {
                    wallRemaining = elapsed * remainingSeconds / doneSeconds;
                }
                else
                {
                    double perThread = Math.Max(1, Math.Min(threads, aheadNodes));
                    wallRemaining = remainingSeconds / perThread;
                }

                progress.Report(new ResponseMatrixProgress
                {
                    Done = completed,
                    Total = total,
                    DoneHistories = spent,
                    TotalHistories = plan,
                    ElapsedSeconds = elapsed,
                    LastEnergyKev = grid[index],
                    DoneNodeSeconds = doneSeconds,
                    RemainingNodeSeconds = remainingSeconds,
                    RemainingSeconds = remainingSeconds > 0.0
                        ? wallRemaining
                        : (spent > 0L ? 0.0 : -1.0)
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

                        // (`A41`) Цена истории этого узла — по ЕГО собственному
                        // проходу, а не по средней: узлы отличаются втрое, и общая
                        // средняя врала бы в обе стороны сразу.
                        lock (planLock)
                        {
                            if (histories > 0)
                            {
                                double seconds = (double)(Stopwatch.GetTimestamp() - ticks0)
                                                 / Stopwatch.Frequency;
                                nodeCost[index] = seconds / histories;
                            }

                            nodeNeed[index] = next;
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
            // скорость, а не число. ⚠ (`A44`) Само число здесь только стартовое:
            // длина каждой пробы подбирается ниже ПО ВРЕМЕНИ (`ProbeSeconds`), потому
            // что доля от заказанных историй на малых наборах падала до единиц
            // миллисекунд, а на таком масштабе замер уже ничего не значит.
            ResponseMatrixOptions probe = options.Clone();
            probe.Histories = MinPilotHistories;

            // ⛔ Фазы считаются ПОРОЗНЬ, и не ради точности ради точности.
            // Пробу видят ВСЕ узлы, а уточнение — только недобравшие, и делить
            // обе на одно число потоков нельзя: на сетке из двенадцати узлов
            // при пятнадцати потоках такая оценка занижала втрое. Занятость
            // фазы ограничена числом узлов В НЕЙ, а не в сетке.
            double[] energies = new double[samples.Length];
            int[] probeLength = new int[samples.Length];
            double[] pilotCost = new double[samples.Length];
            double[] refineCost = new double[samples.Length];
            double[] refineShare = new double[samples.Length];
            for (int p = 0; p < samples.Length; p++)
            {
                int index = samples[p];
                EfficiencySimulator sim = MakeSimulator(geometry, probe, index);

                // ⛔ (`A44`) ПРОГРЕВ ОТДЕЛЬНЫМ ПРОГОНОМ, И ТОЛЬКО ПОТОМ ПОДБОР.
                //
                // Первый прогон нового симулятора всегда долгий: в нём строится
                // сцена и компилируется горячий код. Пока подбор шёл по нему, он
                // перешагивал порог на первой же итерации и оставлял длину
                // минимальной — то есть ровно тот шум, ради которого заводился.
                // Оценка от этого гуляла вдвое между запусками на одном наборе.
                double warmError;
                sim.Histories = MinPilotHistories;
                sim.Response(grid[index], options.BinKev, out warmError);

                // Длина пробы — ПО ВРЕМЕНИ: удваиваем, пока прогон не займёт
                // `ProbeSeconds`.
                int probeHistories = MinPilotHistories;
                while (true)
                {
                    sim.Histories = probeHistories;
                    var trial = Stopwatch.StartNew();
                    double trialError;
                    sim.Response(grid[index], options.BinKev, out trialError);
                    trial.Stop();
                    if (trial.Elapsed.TotalSeconds >= ProbeSeconds
                        || probeHistories >= MaxProbeHistories
                        || probeHistories >= pilot)
                    {
                        break;
                    }

                    probeHistories *= 2;
                }

                sim.Histories = probeHistories;

                // ⛔ (`A44`) ЦЕНА ИСТОРИИ — РАЗНОСТЬЮ ДВУХ ПРОГОНОВ, `N` и `2N`.
                //
                // Прямое деление времени на число историй завышало её в разы:
                // проба идёт по 12 тысячам историй, а прогон узла в счёте — по
                // сотням тысяч и миллионам, и постоянная часть (сцена нового
                // симулятора, таблицы, первый проход JIT) в пробе весит несравнимо
                // больше. Завышение гасило занижение от деления на число потоков,
                // и обе ошибки жили незамеченными, пока считали ими обеими.
                //
                // В разности всё, что не зависит от числа историй, сокращается.
                double relativeError;
                var first = Stopwatch.StartNew();
                sim.Response(grid[index], options.BinKev, out relativeError);
                first.Stop();

                sim.Histories = 2 * probeHistories;
                var second = Stopwatch.StartNew();
                sim.Response(grid[index], options.BinKev, out relativeError);
                second.Stop();
                sim.Histories = probeHistories;

                double growth = second.Elapsed.TotalSeconds - first.Elapsed.TotalSeconds;
                double perHistory = growth > 0.0
                    ? growth / probeHistories
                    : second.Elapsed.TotalSeconds / (2 * probeHistories);
                probeLength[p] = probeHistories;
                energies[p] = grid[index];
                pilotCost[p] = perHistory * pilot;
                if (!adaptive)
                {
                    continue;
                }

                // ⚠ Шум — со второго прогона, а там историй ВДВОЕ больше пробы.
                double achieved = sim.LastContinuumRelativeError > 0.0
                    ? sim.LastContinuumRelativeError
                      * Math.Sqrt(2.0 * probeHistories / Math.Max(1, pilot))
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

            // ⛔ (`A44`) ДЕЛИТСЯ НА ЗАМЕРЕННУЮ ПРОПУСКНУЮ СПОСОБНОСТЬ, А НЕ НА
            // ЧИСЛО ПОТОКОВ. Пробы выше идут по одной на свободной машине, и
            // делить их сумму на 15 значит верить, будто пятнадцать потоков
            // считают в пятнадцать раз быстрее. Замер 01.09.2026: 1 поток 65.8 с,
            // 8 потоков 14.7 с, 15 потоков 13.1 с — то есть 5.0×, а после восьми
            // прирост 12 %. Отсюда и «около 2:00» при факте 5:06 на экране Amber.
            int middle = samples.Length / 2;
            double scale = MeasureThroughput(geometry, probe, grid, samples[middle],
                                             probeLength[middle], threads);
            double seconds = pilotTotal / Math.Min(scale, Math.Max(1, grid.Length));
            if (refineTotal > 0.0)
            {
                int heavy = (int)Math.Ceiling(heavyNodes);
                double machines = Math.Min(scale, Math.Max(1, heavy));

                // ⛔ (`A44`) ХВОСТ ЖАДНОЙ РАСКЛАДКИ. Деление суммы на число машин —
                // это ИДЕАЛ, в котором работу можно нарезать как угодно. Узел
                // нарезать нельзя, а в фазе уточнения их длины отличаются в разы:
                // последний длинный считается, когда остальные потоки уже стоят. На
                // замере 01.09.2026 последние пять прогонов из 185 заняли 40 секунд
                // из 241 — шестую часть счёта.
                //
                // Фаза 2 раскладывается жадно, убывающими длинами (LPT, `T35`), и
                // для неё известна граница Грэхэма: makespan не хуже 4/3 − 1/(3m)
                // от идеала. Её и берём — при четырёх машинах это 1.25, при
                // пятнадцати 1.31. Замер до поправки: оценка занижала в 1.31 и 1.29
                // раза на двух разных наборах, то есть ровно на этот хвост.
                //
                // ⚠ К ПРОБНОЙ фазе это не относится: там у всех узлов одинаковое
                // число историй, и разброс длин втрое, а не в разы.
                double greedy = 4.0 / 3.0 - 1.0 / (3.0 * machines);
                seconds += greedy * refineTotal / machines;
            }

            return seconds;
        }

        /// <summary>
        /// ⚡ (`A44`) Во сколько раз машина считает быстрее с этим числом потоков.
        ///
        /// Замеряется, а не выводится из числа ядер: гипертрединг, общий кэш и
        /// частота под нагрузкой дают на этой машине 5.0× при пятнадцати потоках,
        /// и предсказать это по `Environment.ProcessorCount` нельзя. Одна и та же
        /// проба гоняется сначала одна, потом в `threads` копий одновременно;
        /// отношение времён и есть ответ.
        ///
        /// ⚠ Ответ зажат в [1, threads]: меньше одного он значить не может, а
        /// больше числа потоков — тем более. Занятая посторонним счётом машина
        /// даст меньше, и это ПРАВДА для текущих условий, а не ошибка замера.
        /// </summary>
        static double MeasureThroughput(GeometryModel geometry, ResponseMatrixOptions probe,
                                        double[] grid, int index, int histories, int threads)
        {
            if (threads <= 1)
            {
                return 1.0;
            }

            // Длина та же, что подобрана под цену истории (`A44`): слишком короткий
            // прогон меряет не пропускную способность, а накладные расходы.
            if (histories < MinPilotHistories)
            {
                histories = MinPilotHistories;
            }

            // ⚠ (`A44`) Симуляторы строятся и ПРОГРЕВАЮТСЯ до секундомера: под ним
            // должен остаться только счёт. Иначе в обе половины замера войдёт
            // постройка сцены, и отношение времён к пропускной способности уже не
            // относится.
            EfficiencySimulator[] pack = new EfficiencySimulator[threads];
            for (int k = 0; k < threads; k++)
            {
                pack[k] = MakeSimulator(geometry, probe, index + k + 1);
            }

            EfficiencySimulator single = pack[0];
            double error;
            foreach (EfficiencySimulator warm in pack)
            {
                warm.Histories = Math.Max(MinPilotHistories / 4, histories / 8);
                warm.Response(grid[index], probe.BinKev, out error);
                warm.Histories = histories;
            }

            var alone = Stopwatch.StartNew();
            single.Response(grid[index], probe.BinKev, out error);
            alone.Stop();
            double one = alone.Elapsed.TotalSeconds;
            if (!(one > 0.0))
            {
                return threads;
            }

            var together = Stopwatch.StartNew();
            Parallel.For(0, threads,
                         new ParallelOptions { MaxDegreeOfParallelism = threads },
                         k =>
                         {
                             double e;
                             pack[k].Response(grid[index], probe.BinKev, out e);
                         });
            together.Stop();

            double many = together.Elapsed.TotalSeconds;
            if (!(many > 0.0))
            {
                return threads;
            }

            double scale = threads * one / many;
            return Math.Max(1.0, Math.Min(threads, scale));
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
        /// Сколько секунд должен занять пробный прогон под оценку (`A44`).
        ///
        /// Длина пробы задаётся ВРЕМЕНЕМ, а не долей от заказанных историй.
        /// Прежде она была `Histories / 250`, то есть на наборе в миллион историй
        /// падала до четырёх тысяч — единицы миллисекунд, — а цена истории
        /// берётся разностью двух прогонов, и на таком масштабе разность это шум
        /// планировщика. Замер: оценка гуляла от ×0.76 до ×1.80 на двух наборах.
        /// </summary>
        const double ProbeSeconds = 0.04;

        /// <summary>Потолок историй в пробе: длинная проба не окупает точности.</summary>
        const int MaxProbeHistories = 400000;

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
