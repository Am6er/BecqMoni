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
    ///    ⚠ (`A104`) СВОЕГО ЗЕРНА И СВОЕЙ ГЕОМЕТРИИ УЗЛУ БЫЛО МАЛО, и обещание
    ///    выше СЕМЬ МЕСЯЦЕВ было неверным не по своей вине. С 04.09.2026 по
    ///    06.09.2026 три разные пробы порознь показали, что два прогона одного
    ///    двоичного файла на многих потоках расходятся; замер полосы П15
    ///    06.09.2026 (`AS80_point0`, 20 узлов по 20 тыс. историй, 15 потоков)
    ///    дал СЕМЬ разных матриц из восьми прогонов, а на одном потоке — одну.
    ///    Виноват был не построитель: доли L-подоболочек помнились ДВУМЯ
    ///    полями на объекте из статического кэша `MaterialDatabase.photoShells`
    ///    — одном на весь процесс, — и поток читал свою энергию вместе с чужим
    ///    массивом. Разбор — `MaterialDatabase.PhotoShellModel.Memo`. После
    ///    правки восемь прогонов на 15 потоках дают ОДНО тело, и оно побитово
    ///    совпадает с однопоточным, посчитанным ДО правки.
    ///
    ///    ⛔ Урок на будущее, он же условие годности этого обещания: своего
    ///    зерна и своей копии геометрии НЕ ДОСТАТОЧНО. Любое общее на процесс
    ///    состояние, которое симулятор ПИШЕТ по ходу счёта — кэш, памятка,
    ///    буфер, — ломает воспроизводимость молча, потому что числа остаются
    ///    правдоподобными. Проверять надо не чтением, а счётом: два прогона
    ///    одного файла на многих потоках обязаны дать один отпечаток тела
    ///    (`MatrixDiffProbe`).
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

            // (`AMBER201`, Р4, мелочь 9.2) Боковая постановка у цилиндра —
            // отказ словами до счёта, а не часы счёта сцены с переставленной
            // обвязкой (`E21` обещал отказ, `FacingError` прежде не читал никто).
            string facingError = geometry.FacingError;
            if (!string.IsNullOrEmpty(facingError))
            {
                throw new InvalidOperationException(facingError);
            }

            // (`AMBER201`, Р4, подозрение G) Элемент вне таблиц ослабления в
            // любом слое сцены — отказ словами, а не слой без него.
            string unknownElement = geometry.UnknownElementProblem();
            if (unknownElement != null)
            {
                throw new InvalidOperationException(unknownElement);
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

            // (`AMBER46`, П87) Моменты угловой эффективности узла — из ТЕХ ЖЕ
            // историй, что дали его строку: у узла своя ячейка, пишет её тот
            // поток, что считал узел, и ПОСЛЕДНИЙ проход перекрывает пробный —
            // ровно как `store` перекрывает строки. Таблица Q_k собирается из
            // них в конце (`AngularAttenuation.FromMoments`).
            AngularMomentSums[] nodeAngular = new AngularMomentSums[grid.Length];

            // (`AMBER145`, П199) Второй счёт пика узла — допуском по разрешению
            // (`EfficiencySimulator.LastResolutionPeakExtra`): у узла своя
            // ячейка, последний проход перекрывает пробный — как `store`.
            double[] nodeResolutionExtra = new double[grid.Length];

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
                    // Пустой канал (вылет аннигиляции ниже порога пар) кладётся строкой
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
                        AngularMomentSums angular;
                        double resolutionExtra;
                        double[][] histograms = RunNode(geometry, options, grid[index], index,
                                                        histories, out achieved, out angular,
                                                        out resolutionExtra, cancellation);
                        nodeResolutionExtra[index] = resolutionExtra;
                        nodeSeconds[index] += (double)(Stopwatch.GetTimestamp() - ticks0)
                                              / Stopwatch.Frequency;
                        continuumError[index] = achieved;
                        nodeAngular[index] = angular;
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
                // (`AMBER13` (б)) Нормировка — по сцене, тем же правилом, что
                // клеймо: у поля строки в см², у всех прочих — доли.
                Normalization = ResponseMatrix.NormalizationOf(geometry),
                CreatedUtc = DateTime.UtcNow,
                // (`AMBER46`, П87) Q_k(E) сцены — из тех же историй, что
                // строки; формат 9 несёт их обязательным блоком.
                AngularQk = AngularAttenuation.FromMoments(grid, nodeAngular)
            };

            matrix.RebuildTotals();
            FillResolutionPeak(geometry, matrix, nodeResolutionExtra);
            BuildJoint(geometry, options, matrix, grid, parallel);
            watch.Stop();
            matrix.BuildSeconds = watch.Elapsed.TotalSeconds;
            return matrix;
        }

        /// <summary>
        /// ⚡ ТАБЛИЦА СОВМЕСТНОЙ ЭФФЕКТИВНОСТИ ПАР (`S112`, задача Amber 09.09.2026).
        ///
        /// Формула сумм-пика перемножает СРЕДНИЕ по объёму эффективности
        /// `ε_p(i)·ε_p(j)`, а точка распада у двух квантов каскада ОДНА: верная
        /// величина — среднее ПРОИЗВЕДЕНИЯ ⟨ε₁ε₂⟩. Здесь считается их отношение
        /// κ(E₁,E₂) — тем же переносом, что и сама матрица, из одной разыгранной
        /// точки по два кванта.
        ///
        /// ⛔ БЛОКИ С НЕЗАВИСИМЫМИ ЗЁРНАМИ, А НЕ РАЗДАЧА ТОЧЕК ПОТОКАМ. Число
        /// блоков постоянно, зерно блока — от его номера, накопители
        /// складываются; поэтому результат не зависит от того, сколько потоков
        /// дали машине. Раздача по потокам сделала бы матрицу невоспроизводимой
        /// на другом железе, а этим свойством здесь уже пользуется приёмка.
        ///
        /// ⚠ Сетка κ СВОЯ и грубее узловой: замер стоит `2·N` историй на точку.
        /// Её края совпадают с краями сетки узлов — за ними
        /// <see cref="ResponseMatrix.JointFactor"/> зажимает, а не продолжает.
        /// </summary>
        internal static void BuildJoint(GeometryModel geometry, ResponseMatrixOptions options,
                               ResponseMatrix matrix, double[] grid, ParallelOptions parallel)
        {
            int nodes = options.JointNodes;
            if (nodes <= 1 || grid == null || grid.Length < 2)
            {
                matrix.JointMode = JointKappaMode.None;
                return;
            }

            // ⛔ (`AMBER147`) ТОЧЕЧНАЯ СЦЕНА — κ ≡ 1 БЕЗ РОЗЫГРЫША. Таблицы нет,
            // и `ResponseMatrix.JointFactor` отдаёт ровно 1.0 у любой пары.
            if (options.JointNoiseTarget > 0.0 && IsPointScene(geometry))
            {
                matrix.JointMode = JointKappaMode.Point;
                matrix.JointPoints = 0;
                return;
            }

            JointTable table = MeasureJointTable(geometry, options, grid, parallel);
            if (table == null)
            {
                matrix.JointMode = JointKappaMode.None;
                return;
            }

            matrix.JointEnergies = table.Energies;
            matrix.JointKappa = table.Kappa;
            matrix.JointKappaError = table.Error;
            matrix.JointPoints = table.Points;
            matrix.JointMode = table.Mode;
            matrix.JointRawMedianNoise = table.RawMedianNoise;
            matrix.JointRawMaxNoise = table.RawMaxNoise;
            matrix.JointCellsCounted = table.CellsCounted;
            matrix.JointSubstituted = table.Substituted;
        }

        /// <summary>
        /// (`AMBER147`) Итог замера таблицы κ — для построителя и для проб,
        /// которым нужен замер без построения строк.
        /// </summary>
        public sealed class JointTable
        {
            public double[] Energies;
            public double[][] Kappa;
            public double[][] Error;
            public long Points;
            public JointKappaMode Mode;
            public double RawMedianNoise;
            public double RawMaxNoise;
            public int CellsCounted;
            public int Substituted;
            public int Clamped;
            public long PilotPoints;
            public double PilotMedianNoise;
        }

        /// <summary>
        /// (`AMBER147`) Точечная ли сцена: источник — точка и сцена не поле
        /// (у поля ISO кванты летят со всей сферы, это протяжённый источник).
        /// </summary>
        public static bool IsPointScene(GeometryModel geometry)
        {
            return geometry != null
                   && geometry.SourceType == GeometrySourceType.Point
                   && geometry.Scene != GeometrySceneKind.Iso;
        }

        /// <summary>
        /// ⚡ ЗАМЕР ТАБЛИЦЫ κ (`S112`; `AMBER147`, П199 01.10.2026).
        ///
        /// При <see cref="ResponseMatrixOptions.JointNoiseTarget"/> = 0 —
        /// прежний счёт побитово: <see cref="ResponseMatrixOptions.JointHistories"/>
        /// точек по одной истории на энергию, таблица как вышла.
        ///
        /// При цели больше нуля:
        ///
        ///   1. ПРОБА — <see cref="ResponseMatrixOptions.JointHistories"/> точек по
        ///      <see cref="ResponseMatrixOptions.JointHistoriesPerPoint"/> историй
        ///      на энергию;
        ///   2. ДОБОР до цели по МЕДИАНЕ шума ячеек с обеими энергиями от
        ///      <see cref="JointNoiseMinKev"/> (накопители складываются, проба не
        ///      выбрасывается; блоки добора — своими зёрнами после блоков пробы),
        ///      не больше <see cref="JointMaxPointsFactor"/> проб;
        ///   3. ячейки с шумом выше цели (и ячейки без единого совместного
        ///      события) получают ГЛАДКУЮ оценку от надёжных:
        ///      <see cref="SmoothKappa"/>; затем зажим κ ≥ 1.
        ///
        /// ⛔ Блоков постоянное число, зерно блока — от его номера: результат
        /// не зависит от числа потоков (условие воспроизводимости склада).
        /// </summary>
        public static JointTable MeasureJointTable(GeometryModel geometry, ResponseMatrixOptions options,
                                                   double[] grid, ParallelOptions parallel)
        {
            int nodes = options.JointNodes;
            if (nodes <= 1 || grid == null || grid.Length < 2)
            {
                return null;
            }

            double lo = grid[0], hi = grid[grid.Length - 1];
            double[] energies = new double[nodes];
            double logLo = Math.Log(lo), logHi = Math.Log(hi);
            for (int i = 0; i < nodes; i++)
            {
                energies[i] = Math.Exp(logLo + (logHi - logLo) * i / (nodes - 1));
            }

            // Допуск пика — СВОЙ на каждую энергию сетки κ (`E34`): поле
            // симулятора одно, а шкала здесь пройдена вся.
            double[] halfWidths = null;
            if (options.PeakToleranceFromGeometry || options.PeakToleranceHalfBin)
            {
                halfWidths = new double[nodes];
                for (int i = 0; i < nodes; i++)
                {
                    halfWidths[i] = PeakTolerance(options, geometry, energies[i]);
                }
            }

            bool adaptive = options.JointNoiseTarget > 0.0;
            int perPoint = adaptive ? Math.Max(1, options.JointHistoriesPerPoint) : 1;
            int points = Math.Max(1000, options.JointHistories);
            JointSums total = RunJointBlocks(geometry, options, grid, energies, halfWidths,
                                             points, perPoint, 0, parallel);
            double[][] kappa, error;
            JointSums.Resolve(total, out kappa, out error);
            if (kappa == null)
            {
                return null;
            }

            var table = new JointTable
            {
                Energies = energies,
                Mode = adaptive ? JointKappaMode.Adaptive : JointKappaMode.Legacy,
                PilotPoints = total.Points
            };

            if (adaptive)
            {
                double median = MedianNoise(energies, total, error);
                table.PilotMedianNoise = median;
                double target = options.JointNoiseTarget;
                long cap = (long)points * JointMaxPointsFactor;
                // Добор — не больше двух раундов, как у узлов (`MaxNodePasses`):
                // оценка шума по редким событиям занижена при малой статистике
                // (замер П199, `RC103_marinelli05_kcl`, 4 истории на точку:
                // 200 тыс. точек — медиана 11.65 %, 1.25 млн — 6.43 % вместо
                // ожидаемых по 1/√N 4.7 %), и один раунд недобирает.
                for (int round = 0; round < JointTopUpRounds && median > target; round++)
                {
                    // Шум ячейки — как 1/√(точек): нужное число в лоб, с запасом,
                    // как у узлов (`NeededHistories`). Бесконечная медиана (больше
                    // половины ячеек без совместных событий) — сразу потолок.
                    long want = cap;
                    if (!double.IsInfinity(median))
                    {
                        double ratio = median / target;
                        want = (long)Math.Ceiling(total.Points * ratio * ratio * HistoriesMargin);
                    }

                    long extra = Math.Min(cap, want) - total.Points;
                    if (extra < JointBlocks)
                    {
                        break;
                    }

                    JointSums more = RunJointBlocks(geometry, options, grid, energies, halfWidths,
                                                    (int)Math.Min(int.MaxValue, extra), perPoint,
                                                    JointBlocks * (round + 1), parallel);
                    total.Add(more);
                    JointSums.Resolve(total, out kappa, out error);
                    median = MedianNoise(energies, total, error);
                }
            }

            table.Points = total.Points;
            NoiseSummary(energies, total, error, out table.RawMedianNoise, out table.RawMaxNoise,
                         out table.CellsCounted);
            if (adaptive)
            {
                SmoothKappa(energies, total, kappa, error, options.JointNoiseTarget,
                            out table.Substituted, out table.Clamped);
            }

            table.Kappa = kappa;
            table.Error = error;
            return table;
        }

        /// <summary>
        /// (`AMBER147`) Блоки замера κ: <see cref="JointBlocks"/> блоков по
        /// `points / JointBlocks` точек, зерно блока — `grid.Length + firstBlock
        /// + номер` (κ разыгрывается НЕ теми же точками, что последний узел;
        /// блоки добора — своими зёрнами после блоков пробы).
        /// </summary>
        static JointSums RunJointBlocks(GeometryModel geometry, ResponseMatrixOptions options,
                                        double[] grid, double[] energies, double[] halfWidths,
                                        int points, int perPoint, int firstBlock,
                                        ParallelOptions parallel)
        {
            int nodes = energies.Length;
            int blocks = Math.Max(1, Math.Min(JointBlocks, points));
            int perBlock = Math.Max(1, points / blocks);
            var partials = new JointSums[blocks];
            Parallel.ForEach(Partitioner.Create(0, blocks, 1), parallel, chunk =>
            {
                for (int block = chunk.Item1; block < chunk.Item2; block++)
                {
                    // Зерно блока продолжает нумерацию узлов сетки, чтобы κ
                    // разыгрывалась НЕ теми же точками, что последний узел.
                    EfficiencySimulator sim = MakeSimulator(geometry, options,
                                                            grid.Length + firstBlock + block, energies[0]);
                    partials[block] = sim.JointPeakSums(energies, perBlock, halfWidths, perPoint);
                }
            });

            var total = new JointSums(nodes);
            foreach (JointSums part in partials)
            {
                total.Add(part);
            }

            return total;
        }

        /// <summary>
        /// (`AMBER147`) Ячейка входит в мерку шума, если обе энергии не ниже
        /// <see cref="JointNoiseMinKev"/>.
        /// </summary>
        static bool Counted(double[] energies, int i, int j)
        {
            return energies[i] >= JointNoiseMinKev && energies[j] >= JointNoiseMinKev;
        }

        /// <summary>
        /// (`AMBER147`) Шум ячейки, %. Ячейка без совместных событий (`Resolve`
        /// пишет ей κ = 1 и шум 0) — шум БЕСКОНЕЧЕН, а не ноль: замер её не видел.
        /// </summary>
        static double CellNoise(JointSums sums, double[][] error, int i, int j)
        {
            int a = Math.Min(i, j), b = Math.Max(i, j);
            return sums.Joint[a][b] > 0.0 ? error[i][j] : double.PositiveInfinity;
        }

        static double MedianNoise(double[] energies, JointSums sums, double[][] error)
        {
            double median, max;
            int counted;
            NoiseSummary(energies, sums, error, out median, out max, out counted);
            return median;
        }

        static void NoiseSummary(double[] energies, JointSums sums, double[][] error,
                                 out double median, out double max, out int counted)
        {
            var noise = new List<double>();
            int n = energies.Length;
            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++)
                {
                    if (Counted(energies, i, j))
                    {
                        noise.Add(CellNoise(sums, error, i, j));
                    }
                }
            }

            counted = noise.Count;
            if (noise.Count == 0)
            {
                median = 0.0;
                max = 0.0;
                return;
            }

            noise.Sort();
            median = noise[noise.Count / 2];
            max = noise[noise.Count - 1];
        }

        /// <summary>
        /// ⛔ (`AMBER147`) ШУМНЫЕ ЯЧЕЙКИ НЕ ЧИТАЮТСЯ. Ячейка с шумом выше
        /// <paramref name="threshold"/> (или без совместных событий) получает
        /// средневзвешенное надёжных ячеек: вес 1/σ² и гауссова близость по
        /// логарифму обеих энергий; радиус начинается с шага сетки и растёт в
        /// полтора раза, пока надёжных в нём не наберётся <see cref="SmoothMinCells"/>.
        /// Шум подставленной — шум этого среднего. Затем — ЗАЖИМ κ ≥ 1 у всей
        /// таблицы (у протяжённого источника эффективности двух квантов
        /// сомонотонны: обе падают с удалением точки от кристалла, и
        /// ковариация неотрицательна). Надёжных на всю таблицу меньше
        /// <see cref="SmoothMinCells"/> — подставлять не от чего: шумные ячейки
        /// получают κ = 1 (поправки нет), об этом говорит `substituted`.
        /// </summary>
        static void SmoothKappa(double[] energies, JointSums sums, double[][] kappa, double[][] error,
                                double threshold, out int substituted, out int clamped)
        {
            int n = energies.Length;
            substituted = 0;
            clamped = 0;
            bool[,] good = new bool[n, n];
            int goodCount = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double s = CellNoise(sums, error, i, j);
                    good[i, j] = s <= threshold && kappa[i][j] > 0.0;
                    if (good[i, j] && j >= i)
                    {
                        goodCount++;
                    }
                }
            }

            double step = Math.Log(energies[1] / energies[0]);
            double[] logE = new double[n];
            for (int i = 0; i < n; i++)
            {
                logE[i] = Math.Log(energies[i]);
            }

            double[][] outK = new double[n][];
            double[][] outE = new double[n][];
            for (int i = 0; i < n; i++)
            {
                outK[i] = (double[])kappa[i].Clone();
                outE[i] = (double[])error[i].Clone();
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++)
                {
                    if (good[i, j])
                    {
                        continue;
                    }

                    double value = 1.0, noise = 0.0;
                    if (goodCount >= SmoothMinCells)
                    {
                        for (double h = step; h <= 100.0 * step; h *= 1.5)
                        {
                            double sw = 0.0, swk = 0.0, sw2s2 = 0.0;
                            int cells = 0;
                            for (int k = 0; k < n; k++)
                            {
                                for (int l = 0; l < n; l++)
                                {
                                    if (!good[k, l])
                                    {
                                        continue;
                                    }

                                    double d1 = logE[i] - logE[k], d2 = logE[j] - logE[l];
                                    double d = (d1 * d1 + d2 * d2) / (h * h);
                                    if (d > 9.0)
                                    {
                                        continue;
                                    }

                                    double sigma = Math.Max(SmoothNoiseFloor, error[k][l]);
                                    double w = Math.Exp(-0.5 * d) / (sigma * sigma);
                                    sw += w;
                                    swk += w * kappa[k][l];
                                    sw2s2 += w * w * sigma * sigma;
                                    cells++;
                                }
                            }

                            if (cells >= SmoothMinCells && sw > 0.0)
                            {
                                value = swk / sw;
                                noise = Math.Sqrt(sw2s2) / sw;
                                break;
                            }
                        }
                    }

                    outK[i][j] = value;
                    outK[j][i] = value;
                    outE[i][j] = noise;
                    outE[j][i] = noise;
                    substituted++;
                }
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (outK[i][j] < 1.0)
                    {
                        outK[i][j] = 1.0;
                        if (j >= i)
                        {
                            clamped++;
                        }
                    }

                    kappa[i][j] = outK[i][j];
                    error[i][j] = outE[i][j];
                }
            }
        }

        /// <summary>(`AMBER147`) Нижняя энергия ячеек мерки шума κ, кэВ — граница приёмки строки.</summary>
        public const double JointNoiseMinKev = 100.0;

        /// <summary>(`AMBER147`) Потолок добора точек κ, в разах от пробы.</summary>
        public const int JointMaxPointsFactor = 16;

        /// <summary>(`AMBER147`) Раундов добора точек κ сверх пробы.</summary>
        const int JointTopUpRounds = 2;

        /// <summary>(`AMBER147`) Сколько надёжных ячеек нужно гладкой подстановке.</summary>
        const int SmoothMinCells = 4;

        /// <summary>
        /// (`AMBER147`) Пол шума в весе подстановки, %: без него ячейка с шумом
        /// 0.3 % забирала бы весь вес у соседок с 3 %, и подстановка стала бы
        /// значением одной ячейки.
        /// </summary>
        const double SmoothNoiseFloor = 1.0;

        /// <summary>
        /// (`AMBER145`, П199 01.10.2026) Допуск второго счёта пика узла, кэВ:
        /// ПШПВ(E)/2 геометрии — ТО ЖЕ выражение, что у пути кривой
        /// (`EfficiencyCalculation.Run`), — если он шире допуска строки
        /// (<see cref="PeakTolerance"/>); иначе ноль (второй счёт не нужен: пик
        /// по разрешению и есть канал `Peak`).
        /// </summary>
        internal static double ResolutionHalfWidth(ResponseMatrixOptions options, GeometryModel geometry,
                                          double energyKev)
        {
            double resolution = geometry.PeakHalfWidthKev(energyKev);
            return resolution > PeakTolerance(options, geometry, energyKev) ? resolution : 0.0;
        }

        /// <summary>
        /// ⚡ (`AMBER145`, П199 01.10.2026) ПИКОВАЯ ЭФФЕКТИВНОСТЬ УЗЛА ПО
        /// РАЗРЕШЕНИЮ: Σ строки канала `Peak` (то, что прежде брал
        /// суммирователь каскада) плюс второй счёт узла — истории вне бина пика
        /// с недобором не больше ПШПВ/2. Определение — кривой эффективности;
        /// у геометрии без разрешения (`FwhmAt662Percent` не задан) — Σ канала
        /// `Peak`, и <see cref="ResponseMatrix.PeakResolutionFromGeometry"/> = false.
        /// </summary>
        internal static void FillResolutionPeak(GeometryModel geometry, ResponseMatrix matrix, double[] extra)
        {
            int nodes = matrix.Energies.Length;
            float[][] peakRows = matrix.ChannelRows != null
                                 && matrix.ChannelRows.Length > (int)EfficiencySimulator.ResponseChannel.Peak
                ? matrix.ChannelRows[(int)EfficiencySimulator.ResponseChannel.Peak]
                : null;
            double[] values = new double[nodes];
            for (int i = 0; i < nodes; i++)
            {
                double sum = 0.0;
                float[] row = peakRows != null && i < peakRows.Length ? peakRows[i] : null;
                if (row != null)
                {
                    foreach (float v in row)
                    {
                        sum += v;
                    }
                }

                values[i] = sum + (extra != null && i < extra.Length ? extra[i] : 0.0);
            }

            matrix.PeakEfficiencyResolution = values;
            matrix.PeakResolutionFromGeometry = geometry.FwhmAt662Percent > 0.0;
        }

        /// <summary>
        /// Блоков замера κ. Постоянное число, а не «по потокам»: от него зависит
        /// разбиение розыгрыша, то есть сами числа.
        /// </summary>
        const int JointBlocks = 32;

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
                                  out double achieved, out AngularMomentSums angular,
                                  out double resolutionExtra, CancellationToken cancellation)
        {
            EfficiencySimulator sim = MakeSimulator(geometry, options, index, energyKev);
            sim.Histories = Math.Max(1, histories);
            // (`AMBER201`, Р4, мелочь G.6) Отмена — и ВНУТРИ узла, а не только
            // между узлами: начатый узел прежде досчитывался до конца (до
            // 24 млн историй, минуты на поток). Опрос раз в 4096 историй;
            // случайных чисел не тянет — тело узла побитово прежнее.
            if (cancellation.CanBeCanceled)
            {
                sim.PollCancellation = cancellation.ThrowIfCancellationRequested;
            }

            // (`AMBER145`) Второй счёт пика — допуском КРИВОЙ (ПШПВ/2 геометрии),
            // если он шире допуска строки; иначе пик по разрешению и есть канал
            // `Peak`. Случайных чисел не тянет — тело узла прежнее побитово.
            double resolutionHalfWidth = ResolutionHalfWidth(options, geometry, energyKev);
            sim.ResolutionPeakHalfWidthKev = resolutionHalfWidth;
            double relativeError;
            double[][] histograms = sim.ResponseByChannel(energyKev, options.BinKev,
                                                          out relativeError);
            achieved = sim.LastContinuumRelativeError;
            resolutionExtra = resolutionHalfWidth > 0.0 ? sim.LastResolutionPeakExtra : 0.0;
            // (`AMBER46`) Моменты Q_k узла — из тех же историй взвешенной ветки,
            // что дали строку; отдельного розыгрыша нет.
            angular = sim.LastAngularMoments;

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
        /// В `achieved` приходит <see cref="EfficiencySimulator.LastContinuumRelativeError"/>
        /// — шум ПОСЛЕ СВЁРТКИ профилем прибора. Абсолютной формулы у него нет:
        /// от историй он зависит как 1/√N (это и есть всё, что нужно расчёту),
        /// но численно 100/√N по набранным событиям континуума НЕ равен —
        /// то описание принадлежит другой величине, `LastContinuumIntegralError`.
        /// Поэтому нужное число историй считается В ЛОБ по ОТНОШЕНИЮ, а не
        /// подбирается блоками: N = N₀·(шум₀/цель)². Запас нужен потому, что
        /// сама оценка шумная (её точность — порядка 1/√(2N₀)).
        ///
        /// ⚠ Шапка правлена по `A76`: до 03.09.2026 здесь стояло равенство
        /// «шум узла = 100/√N», описывавшее интегральную мерку, — а мерку
        /// перевели на свёрточную решением Amber 01.09.2026 (`A39`) именно
        /// потому, что интегральная мерила не то, что видит человек. Замер на
        /// `AS80_point0`, 3 млн историй: событий континуума около 444 тыс., то
        /// есть 100/√N = 0.15 %, а записанный шум узла 0.41 % — втрое больше.
        /// ⛔ КОД ВЕРЕН И ПРАВКИ НЕ ТРЕБУЕТ: закон 1/√N у обеих величин один и
        /// тот же, а отношение шум₀/цель от постоянного множителя не зависит.
        /// Неверным было только равенство АБСОЛЮТНОГО числа в описании, и цена
        /// у него своя: читающий считал по шапке ожидаемый шум, получал втрое
        /// меньше настоящего и шёл искать несуществующую потерю статистики.
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
        /// ДОПУСК ПИКА на данной энергии — ОДНО правило на обе точки, где он
        /// нужен (симулятор узла и сетка κ). Второе выражение для одной
        /// величины однажды разъехалось бы молча (`S37`), и ровно так уже было
        /// с самим этим допуском (`E34`: путь матрицы ставил ноль, путь кривой
        /// эффективности брал из геометрии).
        ///
        /// Порядок разбора, и он же порядок старшинства:
        ///
        ///   * `PeakToleranceFromGeometry` — ПШПВ(E)/2 из геометрии. Плечо для
        ///     сравнения, умолчанием выключено: замер 11.09.2026 показал, что
        ///     на 32.194 кэВ такой допуск равен 17.3 % энергии линии и вдвое
        ///     раздувает комптоновский канал (см. поле опции);
        ///   * `PeakToleranceHalfBin` — ПОЛУБИН сетки. Решение Amber
        ///     11.09.2026: «Допуск по БИНУ, а не по ПШПВ». Умолчание;
        ///   * иначе ноль — поведение до 11.09.2026, при котором ветвь
        ///     однократного рассеяния теряла вклад целиком.
        /// </summary>
        static double PeakTolerance(ResponseMatrixOptions options, GeometryModel geometry,
                                    double energyKev)
        {
            if (options.PeakToleranceFromGeometry)
            {
                return geometry.PeakHalfWidthKev(energyKev);
            }

            if (options.PeakToleranceHalfBin)
            {
                return 0.5 * options.BinKev;
            }

            return 0.0;
        }

        /// <summary>
        /// Симулятор одного узла. Геометрия копируется: сцена строится внутри
        /// симулятора по модели, и делить одну модель между потоками — значит
        /// однажды поймать её правку из другого места.
        /// </summary>
        internal static EfficiencySimulator MakeSimulator(GeometryModel geometry, ResponseMatrixOptions options,
                                                 int index, double energyKev)
        {
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = options.Histories,
                XrayEscape = options.XrayEscape,
                LXrayEscape = options.LXrayEscape,
                KLCascade = options.KLCascade,
                // ⛔ (`AMBER16` п. 1, решение Amber 11.09.2026 «Развести K и L
                // отдельными каналами») Без этой строки ключ был бы МЁРТВ
                // (`S130`): поле в настройках, клеймо и хвост файла есть, а
                // симулятор кладёт L-вылет туда же, куда и K. Побитовый замер
                // такой дыры не ловит — числа верные, испорчено ПРОИСХОЖДЕНИЕ.
                SplitXrayShells = options.SplitXrayShells,
                // ⛔ (`F11` (а), решения Amber 11.09.2026) Без этих трёх строк
                // ключи K-провала и η были бы МЕРТВЫ (`S130`): поле в
                // настройках, клеймо и хвост файла есть, а симулятор считает
                // свет по таблице. Уровень 1 — обе половины, 2 — только
                // кривая, 3 — только каскад. Выражения — у самих настроек
                // (`KDipCurveHalf`/`KDipCascadeHalf`): путь КРИВОЙ
                // (`EfficiencyCalculation.Run`) берёт те же по решению Amber
                // 12.09.2026 «Да — одна физика для кривой и матрицы», и
                // второе правило для одной величины разъехалось бы молча (`S37`).
                LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(options.KDipLight),
                LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(options.KDipLight),
                LightEtaEh = options.LightEtaEh,
                // ⛔ (П23 12.09.2026: `A267`, `A306`, `M9` — три решения Amber
                // «в СЛЕДУЮЩИЙ единый счёт склада») Без этих трёх строк ключи
                // были бы МЕРТВЫ (`S130`): поле в настройках, клеймо и хвост
                // файла есть, а симулятор считает по-старому. Умолчания
                // настроек ВКЛ с 13.09.2026 (физика 17, П37).
                LightBinUnified = options.LightBinUnified,
                PeakChannelByTolerance = options.PeakChannelByTolerance,
                LYieldSupply = options.LYieldSupply,
                // ⛔ (`A72`, П27 12.09.2026, решение Amber «Вести электрон
                // переносом») Без этой строки ключ был бы МЁРТВ (`S130`).
                // Умолчание настроек ВКЛ с 13.09.2026 (физика 17, П37).
                ElectronTransport = options.ElectronTransport,
                CoherentPassesThrough = options.CoherentPassesThrough,
                Bremsstrahlung = options.Bremsstrahlung,
                // ⛔ (`E34`, решение Amber 06.09.2026, ветка «а») ВЕТКА
                // ОДНОКРАТНОГО РАССЕЯНИЯ НЕ СЧИТАЕТСЯ, КОГДА ЕЁ ВЫХОД ЗАВЕДОМО
                // СТИРАЕТСЯ — то есть при нулевом допуске пика и включённом
                // аналоговом континууме. Разбор условия и почему оно стоит
                // здесь, а не внутри симулятора (кривая эффективности сюда не
                // заходит, и там ветка обязана работать) — в шапке
                // <see cref="ResponseMatrix.SingleScatterErased"/>.
                SingleScatter = options.SingleScatter
                                && !ResponseMatrix.SingleScatterErased(geometry, options),
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
                // ⛔ (`E29`, П41) Важностный розыгрыш точки вылета — тем же
                // путём: не доехав до построителя, ключ мёртв (`S130`), а
                // клеймо `imp=1` при этом лгало бы о происхождении матрицы.
                ImportanceSampling = options.ImportanceSampling,
                // ⛔ (`N4`/`F11` (г) и `M3`, П44 13.09.2026) Электрон в
                // произвольном веществе и тормозное вдоль пути — тем же
                // путём: не доехав до построителя, ключ мёртв (`S130`), а
                // клеймо `ecomp=1`/`bpath=N` лгало бы о происхождении матрицы.
                ElectronAnyMaterial = options.ElectronAnyMaterial,
                BremAlongPath = options.BremAlongPath,
                // ⛔ (`AMBER44`/`M12`, П94 17.09.2026) Перенос электрона в слоях
                // обвязки (занос и возврат) — тем же путём: не доехав до
                // построителя, ключ мёртв (`S130`), а клеймо `eltr=1` лгало бы
                // о происхождении матрицы.
                ElectronLayerTransport = options.ElectronLayerTransport,
                // ⛔ (`M13`, П100 18.09.2026) Смешанная схема упругого рассеяния
                // в слоях обвязки — тем же путём: не доехав до построителя, ключ
                // мёртв (`S130`), а клеймо `elmix=1` лгало бы о происхождении.
                ElectronLayerMixedScattering = options.ElectronLayerMixedScattering,
                // ⛔ (`M13`, П106 19.09.2026) Тормозное электрона в слоях обвязки
                // по ходу переноса — тем же путём: не доехав до построителя, ключ
                // мёртв (`S130`), а клеймо `lbrem=1` лгало бы о происхождении.
                ElectronLayerBremAlongPath = options.ElectronLayerBremAlongPath,
                // ⛔ (`M13`, П111 19.09.2026) Направление кванта тормозного в слоях
                // (2BS) — тем же путём: не доехав до построителя, ключ мёртв
                // (`S130`), а клеймо `lbang=1` лгало бы о происхождении.
                ElectronLayerBremAngular2BS = options.ElectronLayerBremAngular2BS,
                // ⛔ (`E34`) ДОПУСК ПИКА. Ноль здесь стоял безусловно, и это
                // запирало поправку на однократное рассеяние: `InPeak` требует
                // `E − deposited ≤ допуск`, а у рассеявшегося кванта недобор
                // положителен всегда. Решение Amber 02.09.2026 — ветка «б»:
                // поправка обязана входить в пик, дефект — нулевой допуск.
                // Ключ выключен умолчанием, чтобы ни одна посчитанная матрица
                // не устарела; см.
                // <see cref="ResponseMatrixOptions.PeakToleranceFromGeometry"/>.
                //
                // ⚠ Допуск берётся ТЕМ ЖЕ выражением, что на пути кривой
                // эффективности (`EfficiencyCalculation.cs`): второе правило
                // для одной величины однажды разъехалось бы молча (`S37`).
                PeakHalfWidthKev = PeakTolerance(options, geometry, energyKev)
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
