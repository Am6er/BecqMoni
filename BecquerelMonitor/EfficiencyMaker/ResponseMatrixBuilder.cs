using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BecquerelMonitor.FullSpectrumAnalysis;

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
        /// (`AMBER220`, П246 07.10.2026) ТАБЛИЦА κ ПАР — ВТОРАЯ ЧАСТЬ РАБОТЫ.
        /// Блоков κ досчитано (<see cref="ResponseMatrixBuilder.JointBlocks"/> на
        /// проход). На CPU-пути κ идёт ПОСЛЕ узлов, на GPU-пути — на ЦП
        /// одновременно с узлами на устройстве; и там и там матрица не собрана,
        /// пока κ не досчитана, — значит и полоса не полна.
        /// </summary>
        public int JointDone;

        /// <summary>
        /// Блоков κ в ПРЕДЕЛЬНОМ плане — проба плюс все доборы
        /// (<see cref="ResponseMatrixBuilder.JointMaxBlocks"/>); 0 — у этой сцены
        /// κ не считается (точечная: κ ≡ 1). Предел, а не текущий план, нарочно:
        /// добор добавляет блоки по ходу, и знаменатель, растущий на глазах,
        /// пятил бы полосу назад (`A46`). Закончили раньше предела — последний
        /// отчёт ставит <see cref="JointDone"/> = <see cref="JointPlanned"/>.
        /// </summary>
        public int JointPlanned;

        /// <summary>Блоков κ в плане НА ЭТОТ МОМЕНТ (проба, затем доборы) — для подписи, не для полосы.</summary>
        public int JointPlannedNow;

        /// <summary>
        /// (`AMBER305`, 05.11.2026) РАЗДЕЛ ЭЛЕКТРОНА ПРОБЫ — ТРЕТЬЯ ЧАСТЬ РАБОТЫ ОКНА: идёт
        /// после того, как фотонная матрица собрана. Строк раздела досчитано; единица — строка
        /// (узел, линия конверсии, ветвь), их число известно заранее и за счёт не меняется.
        /// </summary>
        public int SectionDone;

        /// <summary>Строк раздела всего; 0 — это отчёт фотонной части, а не раздела.</summary>
        public int SectionTotal;

        /// <summary>
        /// (`AMBER318`, пятый заход) Строк раздела, прошедших пробный проход останова по шуму; 0 — останова нет.
        /// </summary>
        public int SectionPilotDone;

        /// <summary>
        /// (`AMBER318`, пятый заход) Доля полосы раздела, отданная пробному проходу (его цена против номинала,
        /// N/10 — 0.1); 0 — останова нет, полоса идёт по досчитанным строкам, как прежде.
        /// </summary>
        public double SectionPilotShare;

        /// <summary>
        /// Доля сделанного, % — ПО ДОСЧИТАННЫМ УЗЛАМ (`A46`) И БЛОКАМ κ (`AMBER220`).
        ///
        /// Прежде доля шла по цене узлов в потокосекундах, и полоса пятилась
        /// назад: узел, попросивший второго прохода, добавлял работы, и
        /// знаменатель рос быстрее числителя. Узлов же ровно столько, сколько в
        /// сетке, и досчитанный узел досчитан навсегда — полоса идёт только
        /// вперёд. Единица полосы — узел или блок κ; 100 % — ровно тогда, когда
        /// досчитаны и узлы, и κ, то есть когда матрица собрана.
        /// </summary>
        public double Percent
        {
            get
            {
                // (`AMBER305`) Отчёт раздела — своя шкала; как сложить её с фотонной, решает окно.
                if (this.SectionTotal > 0)
                {
                    // (`AMBER318`, пятый заход) С остановом строка даёт полосе долю пробного прохода, как его
                    // прошла, и остальное — когда досчитана: полоса растёт и на пробном проходе, не пятится.
                    double share = Math.Max(0.0, Math.Min(1.0, this.SectionPilotShare));
                    return 100.0 * (share * this.SectionPilotDone + (1.0 - share) * this.SectionDone) / this.SectionTotal;
                }

                int units = this.TotalNodes + Math.Max(0, this.JointPlanned);
                return units > 0
                    ? 100.0 * (this.SettledNodes + Math.Max(0, this.JointDone)) / units
                    : 0.0;
            }
        }

        /// <summary>Узлы досчитаны, а κ ещё нет — полоса стоит на таблице пар.</summary>
        public bool JointPending
        {
            get
            {
                return this.TotalNodes > 0 && this.SettledNodes >= this.TotalNodes
                       && this.JointPlanned > 0 && this.JointDone < this.JointPlanned;
            }
        }
    }

    /// <summary>(`AMBER305`) Чем кончилось <see cref="ResponseMatrixBuilder.AttachElectronSection"/>.</summary>
    public enum ElectronSectionOutcome
    {
        /// <summary>Раздел посчитан заново.</summary>
        Built,

        /// <summary>Прежний раздел того же клейма взят без счёта.</summary>
        Carried,

        /// <summary>Сцене раздел не положен: изотропное поле (`Ф5`).</summary>
        NotApplicable
    }

    /// <summary>
    /// ⛔ (`AMBER305`, решение Amber 05.11.2026 вопросником, дословно: «Строить в окне
    /// (Рекомендую)») РЕЦЕПТ РАЗДЕЛА ЭЛЕКТРОНА ПРОБЫ — ОДИН НА ОКНО И СКЛАД. Прежде он жил
    /// умолчаниями `BetaSectionProbeA274` и строкой в `handover/store30/sections.ps1`, а окно
    /// матрицы раздел только переносило: матрицы пользователя и корпуса различались молча.
    /// Рецепт сверяет с клеймами склада сторож `tools/check_electron_recipe.py`.
    ///
    /// Рецепт: сетка <see cref="ResponseElectronSection.DefaultGrid"/>, <see cref="Histories"/>
    /// историй на строку, зерно 0 (зерно матрицы), позитрон ВКЛ, строки линий конверсии и
    /// строки ветвей — рядов библиотеки нуклидов с выходом не ниже
    /// <see cref="LibraryMinPercent"/> %. Список линий и ветвей зависит от библиотеки НА ЭТОЙ
    /// машине и входит в клеймо раздела (`lines=`, `br=`), так что другая библиотека — другое
    /// клеймо. Годность раздела для разбора от этого не зависит
    /// (<see cref="ResponseMatrix.ElectronSectionFor"/>): линия вне списка интерполируется по
    /// узлам, ветвь без строки ложится весами узлов.
    /// </summary>
    public sealed class ElectronSectionRecipe
    {
        /// <summary>
        /// Историй на строку (решение Amber 05.11.2026 вопросником, дословно: «Поднять до 800000»).
        /// Замер на приборе вне корпуса, один нуклид, единственная опора шкалы: СКО активности от
        /// зерна раздела 1.5 % при 200 тыс. и 0.36 % при 800 тыс.; число входит в клеймо раздела.
        /// </summary>
        public const int StoreHistories = 800000;

        /// <summary>
        /// РЫЧАГ ПРОБ, не настройка: столько историй на строку берёт прежняя перегрузка
        /// <see cref="ResponseMatrixBuilder.AttachElectronSection(GeometryModel, ResponseMatrix, ResponseElectronSection, RmGpu, int, IProgress{ResponseMatrixProgress}, CancellationToken, Action{string})"/>
        /// (`MatrixRecomputeProbe`) и <see cref="StampFor(GeometryModel, ResponseMatrix)"/>. Ни один
        /// штатный путь его не ставит. ⛔ (`AMBER318`) Окно матрицы его НЕ читает: у окна своё поле
        /// «Истории бета» (умолчание — <see cref="StoreHistories"/>), и номинал из поля приходит в
        /// построитель параметром. Пробы окна (`MatrixWindowProbeP291`, `…DetailsProbeP292`,
        /// `ElectronSectionFormProbeA305`) снижают рычаг и сами переносят его в поле окна, потому
        /// что судят не раздел, а поведение окна. Клеймо раздела число историй несёт.
        /// </summary>
        public static int Histories = StoreHistories;

        /// <summary>Порог выхода строк линий и ветвей, % на распад члена ряда.</summary>
        public const double LibraryMinPercent = 1.0;

        /// <summary>Зерно раздела: 0 — зерно матрицы.</summary>
        public const int Seed = 0;

        /// <summary>Строки позитрона считаются.</summary>
        public const bool Positron = true;

        /// <summary>Сетка энергий узлов.</summary>
        public double[] Grid { get; private set; }

        /// <summary>Энергии строк линий конверсии; null — нет.</summary>
        public double[] Lines { get; private set; }

        /// <summary>Ветви строк «событие целиком».</summary>
        public List<ElectronBranchSpec> Branches { get; private set; }

        /// <summary>Рецепт по библиотеке нуклидов этой машины.</summary>
        public static ElectronSectionRecipe FromLibrary(Action<string> log)
        {
            double[] grid = ResponseElectronSection.DefaultGrid();
            return new ElectronSectionRecipe
            {
                Grid = grid,
                Lines = LibraryLines(LibraryMinPercent, grid[0], grid[grid.Length - 1], log).ToArray(),
                Branches = BranchesOf(LibraryMembers(), LibraryMinPercent, "lib:1", log),
            };
        }

        /// <summary>Клеймо раздела, который дал бы этот рецепт для матрицы и сцены (истории — рычаг <see cref="Histories"/>).</summary>
        public string StampFor(GeometryModel geometry, ResponseMatrix matrix)
        {
            return this.StampFor(geometry, matrix, Histories);
        }

        /// <summary>
        /// (`AMBER318`) То же с НОМИНАЛОМ <paramref name="histories"/> историй на строку — тем, что
        /// стоит в поле окна. Цель останова по шуму в клеймо не входит: номинал — содержание
        /// клейма, набранное сверх него — усилие (как у фотонной части, `ComputeStamp`).
        /// </summary>
        public string StampFor(GeometryModel geometry, ResponseMatrix matrix, int histories)
        {
            double bin = matrix.BinKev > 0.0 ? matrix.BinKev : matrix.Options.BinKev;
            return ResponseElectronSection.ComputeStamp(geometry, matrix.Stamp, this.Grid, histories, Seed, Positron, bin,
                                                        ResponseElectronSection.CleanLines(this.Lines, this.Grid),
                                                        ResponseMatrixBuilder.CleanBranches(this.Branches, null));
        }

        /// <summary>
        /// Нуклиды библиотеки и члены их рядов (`FsaSampleLibrary.ChainBranches`): каждый нуклид
        /// библиотеки — корень. Порядок — по имени: от него зависит номер строки ветви, а значит
        /// и её зерно.
        /// </summary>
        public static SortedSet<string> LibraryMembers()
        {
            var members = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var report = new FsaSampleLibrary.Report();
            foreach (NuclideDefinition d in NuclideDefinitionManager.GetInstance().NuclideDefinitions)
            {
                string nucid = FsaSampleLibrary.NucidOf(d.Name ?? "");
                if (nucid.Length == 0)
                {
                    continue;
                }

                members.Add(nucid);
                Dictionary<string, double> chain = FsaSampleLibrary.ChainBranches(nucid, report);
                if (chain != null)
                {
                    foreach (string m in chain.Keys)
                    {
                        members.Add(m);
                    }
                }
            }

            return members;
        }

        /// <summary>
        /// (`AMBER274`, физика 28) Линии конверсии рядов библиотеки: линии членов с выходом (на
        /// распад члена) не ниже <paramref name="minPercent"/> в пределах сетки раздела. Различные
        /// энергии (слияние ближе 0.05 кэВ) — по возрастанию.
        /// </summary>
        public static List<double> LibraryLines(double minPercent, double minKev, double maxKev, Action<string> log)
        {
            SortedSet<string> members = LibraryMembers();
            var energies = new List<double>();
            foreach (string nucid in members)
            {
                foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                {
                    if (b.IsLine && b.Intensity >= minPercent && b.EndpointKev >= minKev && b.EndpointKev <= maxKev)
                    {
                        energies.Add(b.EndpointKev);
                    }
                }
            }

            energies.Sort();
            var distinct = new List<double>();
            foreach (double e in energies)
            {
                if (distinct.Count == 0 || e - distinct[distinct.Count - 1] > 0.05)
                {
                    distinct.Add(e);
                }
            }

            if (log != null)
            {
                log(string.Format(CultureInfo.InvariantCulture,
                    "== линии конверсии рядов библиотеки: нуклидов и членов {0}, линий с выходом ≥ {1} % в {2}…{3} кэВ — {4}, различных энергий {5}",
                    members.Count, minPercent, minKev, maxKev, energies.Count, distinct.Count));
            }

            return distinct;
        }

        /// <summary>
        /// (`Ф8`) β⁻-ветви нуклидов <paramref name="members"/> с выходом не ниже порога, у которых
        /// есть каскад. В журнал — перепись: сколько взято и почему пропущены прочие.
        /// </summary>
        public static List<ElectronBranchSpec> BranchesOf(IEnumerable<string> members, double minPercent, string label,
                                                          Action<string> log)
        {
            var list = new List<ElectronBranchSpec>();
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int considered = 0, nuclides = 0;
            foreach (string nucid in members)
            {
                nuclides++;
                foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                {
                    if (b.IsLine || b.Intensity < minPercent)
                    {
                        continue;
                    }

                    considered++;
                    string why;
                    ElectronBranchSpec s = FsaBetaLibrary.EventSpec(b, out why);
                    if (s != null)
                    {
                        list.Add(s);
                        continue;
                    }

                    int c;
                    reasons.TryGetValue(why ?? "?", out c);
                    reasons[why ?? "?"] = c + 1;
                    if (log != null && b.Intensity >= 1.0 && why != null
                        && !why.StartsWith("ветвь на основное", StringComparison.Ordinal))
                    {
                        log(string.Format(CultureInfo.InvariantCulture, "   ветвь {0} {1:F1} кэВ {2:G4} %: {3}",
                                          nucid, b.EndpointKev, b.Intensity, why));
                    }
                }
            }

            if (log != null)
            {
                log(string.Format(CultureInfo.InvariantCulture,
                    "== строки ветвей (--branches={0}): нуклидов и членов {1}, ветвей с выходом ≥ {2} % — {3}, с каскадом — {4}",
                    label, nuclides, minPercent, considered, list.Count));
                foreach (KeyValuePair<string, int> r in reasons)
                {
                    log(string.Format(CultureInfo.InvariantCulture, "   пропущено {0}: {1}", r.Value, r.Key));
                }
            }

            return list;
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

            // (`AMBER307`) Слой без вещества — отказ словами, а не падение в потоке счёта.
            string missingMaterial = geometry.MissingMaterialProblem();
            if (missingMaterial != null)
            {
                throw new InvalidOperationException(missingMaterial);
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

            // (`AMBER311`) Строки «рассеянные вне кристалла» — часть канала `Compton`, в тело не входят.
            float[][] backscatterRows = new float[grid.Length][];
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

            // (`AMBER220`, П246; решение Amber 07.10.2026, дословно: «Узлы + блоки
            // κ, план κ — предельный 96») Блоки κ — вторая часть работы, у полосы
            // хода они в знаменателе с самого начала.
            int jointUnits = JointPlannedBlocks(geometry, options, grid.Length);

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
                    TotalNodes = grid.Length,
                    // (`AMBER220`) κ пар на CPU-пути идёт ПОСЛЕ узлов: её блоки уже
                    // в знаменателе, чтобы полоса не доходила до 100 % раньше κ.
                    JointPlanned = jointUnits,
                    JointPlannedNow = jointUnits > 0 ? JointBlocks : 0
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
                        double[] backscatter;
                        double[][] histograms = RunNode(geometry, options, grid[index], index,
                                                        histories, out achieved, out angular,
                                                        out resolutionExtra, out backscatter, cancellation);
                        backscatterRows[index] = BackscatterRowOf(backscatter);
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
                BackscatterRows = backscatterRows,
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

            // (`AMBER220`) Ход κ — теми же отчётами: узлы досчитаны все, блоки κ
            // прибывают; последний отчёт ставит κ в предел плана — матрица собрана.
            Action<int, int> jointReport = null;
            if (progress != null && jointUnits > 0)
            {
                jointReport = (jointDone, jointNow) => progress.Report(new ResponseMatrixProgress
                {
                    Done = Volatile.Read(ref done),
                    Total = Volatile.Read(ref planned),
                    DoneHistories = Interlocked.Read(ref doneHistories),
                    TotalHistories = Interlocked.Read(ref plannedHistories),
                    LastEnergyKev = grid[0],
                    StartedNodes = grid.Length,
                    SettledNodes = grid.Length,
                    TotalNodes = grid.Length,
                    JointPlanned = jointUnits,
                    JointPlannedNow = jointNow,
                    JointDone = Math.Min(jointDone, jointUnits)
                });
            }

            BuildJoint(geometry, options, matrix, grid, parallel, jointReport);
            if (jointReport != null)
            {
                jointReport(jointUnits, jointUnits);
            }

            watch.Stop();
            matrix.BuildSeconds = watch.Elapsed.TotalSeconds;

            return matrix;
        }

        /// <summary>
        /// (`AMBER274`, Э2, 04.11.2026) СОБСТВЕННАЯ АКТИВНОСТЬ КРИСТАЛЛА — раздел `CWNA` к
        /// готовой матрице, один вход для матриц путей CPU и GPU. ⛔ СЛОЙ ВЫКЛЮЧЕН (решение Amber
        /// 05.11.2026 вопросником, дословно: «Разобрать χ², слой выкл (Рекомендую)»): построитель,
        /// окно и склад этот вход НЕ зовут, раздел не считается и файл прежний до бита; зовёт только
        /// замер (`CrystalOwnActivityProbeA274`). Парный ключ разбора —
        /// <see cref="FullSpectrumAnalysis.FsaAnalyzer.CrystalOwnLayer"/>. Считается
        /// от фотонного тела независимо (событие распада в объёме кристалла, перенос ЦП
        /// `InCrystal`/`ElectronLoss`, в ядре GPU его нет), поэтому фотонные строки остаются
        /// побитово прежними, а сцены без La/Lu/Ce раздела не получают вовсе. Сбой слоя матрицу не
        /// портит: он необязательный, причина уходит в примечание раздела.
        /// </summary>
        public static void AttachCrystalSection(GeometryModel geometry, ResponseMatrix matrix)
        {
            if (matrix == null || !FullSpectrumAnalysis.CrystalOwnActivityBuilder.HasOwnActivity(geometry))
            {
                return;
            }

            try
            {
                string ownNote;
                matrix.CrystalSection = FullSpectrumAnalysis.CrystalOwnActivityBuilder.Build(
                    geometry, matrix.BinKev, OwnActivityHistories, 0x274, out ownNote);
                matrix.CrystalSectionDamage = ownNote;
            }
            catch (Exception error)
            {
                matrix.CrystalSection = null;
                matrix.CrystalSectionDamage = "слой не собран: " + error.Message;
            }
        }

        /// <summary>
        /// (`AMBER274`, Э2) Сколько историй на нуклид разыгрывает слой собственной
        /// активности. 100 тыс. на нуклид — секунды на сцену на ЦП; шум гистограммы
        /// при этом много ниже статистики самого слоя в измеренном спектре.
        /// </summary>
        const int OwnActivityHistories = 100000;

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
            BuildJoint(geometry, options, matrix, grid, parallel, null);
        }

        /// <summary>(`AMBER220`) То же с отчётом о ходе блоков κ — см. <see cref="MeasureJointTable(GeometryModel, ResponseMatrixOptions, double[], ParallelOptions, Action{int, int})"/>.</summary>
        internal static void BuildJoint(GeometryModel geometry, ResponseMatrixOptions options,
                               ResponseMatrix matrix, double[] grid, ParallelOptions parallel,
                               Action<int, int> jointProgress)
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

            JointTable table = MeasureJointTable(geometry, options, grid, parallel, jointProgress);
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
            return MeasureJointTable(geometry, options, grid, parallel, null);
        }

        /// <summary>
        /// (`AMBER220`) Блоков κ в ПРЕДЕЛЬНОМ плане у этой сцены: проба плюс все
        /// доборы, либо 0, когда κ не считается (сетка короче двух узлов,
        /// <see cref="ResponseMatrixOptions.JointNodes"/> ≤ 1, точечная сцена при
        /// цели по шуму — κ ≡ 1). Те же условия, что у <see cref="BuildJoint"/>.
        /// </summary>
        public static int JointPlannedBlocks(GeometryModel geometry, ResponseMatrixOptions options, int gridLength)
        {
            if (options == null || options.JointNodes <= 1 || gridLength < 2)
            {
                return 0;
            }

            if (options.JointNoiseTarget > 0.0 && IsPointScene(geometry))
            {
                return 0;
            }

            return JointMaxBlocks;
        }

        /// <summary>Блоков κ в предельном плане: проба и до <see cref="JointTopUpRounds"/> доборов по <see cref="JointBlocks"/>.</summary>
        public const int JointMaxBlocks = JointBlocks * (1 + JointTopUpRounds);

        /// <summary>
        /// (`AMBER220`) Замер таблицы κ с отчётом о ходе: <paramref name="jointProgress"/>
        /// зовётся после каждого досчитанного блока и при каждом доборе
        /// (`done`, `plannedNow`) — из потоков замера.
        /// </summary>
        public static JointTable MeasureJointTable(GeometryModel geometry, ResponseMatrixOptions options,
                                                   double[] grid, ParallelOptions parallel,
                                                   Action<int, int> jointProgress)
        {
            int nodes = options.JointNodes;
            if (nodes <= 1 || grid == null || grid.Length < 2)
            {
                return null;
            }

            int blocksDone = 0, blocksNow = JointBlocks;
            Action blockDone = null;
            if (jointProgress != null)
            {
                blockDone = () =>
                {
                    int d = Interlocked.Increment(ref blocksDone);
                    jointProgress(d, Volatile.Read(ref blocksNow));
                };
                jointProgress(0, blocksNow);
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
                                             points, perPoint, 0, parallel, blockDone);
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

                    if (jointProgress != null)
                    {
                        // Добор — ещё `JointBlocks` блоков в плане; подпись видит это сразу.
                        int now = Interlocked.Add(ref blocksNow, JointBlocks);
                        jointProgress(Volatile.Read(ref blocksDone), now);
                    }

                    JointSums more = RunJointBlocks(geometry, options, grid, energies, halfWidths,
                                                    (int)Math.Min(int.MaxValue, extra), perPoint,
                                                    JointBlocks * (round + 1), parallel, blockDone);
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
                                        ParallelOptions parallel, Action blockDone = null)
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
                    // ⛔ (П253, 14.10.2026) Отмена — и ВНУТРИ блока κ, как у узлов
                    // (`AMBER201`, `RunNode`). Блок κ на маринелли 1 л — минута счёта на
                    // поток (замер П246: 14 блоков за 64 с), и `Parallel.ForEach` снимает
                    // только ещё не начатые: «Отмена» в окне ждала до минуты, а на
                    // GPU-пути (κ идёт с первой секунды) — почти при любом нажатии.
                    // Опрос раз в точку, случайных чисел не тянет — таблица побитово прежняя.
                    if (parallel != null && parallel.CancellationToken.CanBeCanceled)
                    {
                        sim.PollCancellation = parallel.CancellationToken.ThrowIfCancellationRequested;
                    }

                    partials[block] = sim.JointPeakSums(energies, perBlock, halfWidths, perPoint);
                    // (`AMBER220`) Досчитанный блок — единица хода полосы; зовётся из
                    // потоков, получатель считает `Interlocked`.
                    if (blockDone != null)
                    {
                        blockDone();
                    }
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
        public const int JointBlocks = 32;

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
        /// (`AMBER274`, Э1 29.10.2026) РАЗДЕЛ СТРОК ЭЛЕКТРОНА ПРОБЫ к ГОТОВОЙ матрице
        /// (<see cref="ResponseElectronSection"/>): по узлу сетки
        /// <paramref name="grid"/> — строка электрона и (при <paramref name="positron"/>)
        /// позитрона из точки пробы (<see cref="EfficiencySimulator.ElectronSourceResponse"/>),
        /// симулятором ТЕХ ЖЕ настроек, что у матрицы (<see cref="MakeSimulator"/> с
        /// <see cref="ResponseMatrix.Options"/>). Фотонная часть матрицы не трогается.
        ///
        /// Шкала: свет пика полного поглощения на кэВ при 661.657 кэВ берётся у
        /// симулятора тех же настроек (20 000 историй узла 661.657); у кристалла без
        /// кривой света — 0, строки в оставленной энергии.
        ///
        /// Отказ словами: матрица не годна для сцены (клеймо фотонной части не
        /// сошлось — раздел посчитался бы для другой сцены).
        /// </summary>
        public static ResponseElectronSection BuildElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   double[] grid, int histories, int seed,
                                                                   bool positron, int threads,
                                                                   CancellationToken cancellation,
                                                                   Action<string> log)
        {
            return BuildElectronSection(geometry, matrix, grid, histories, seed, positron, threads, cancellation, log, null);
        }

        /// <summary>
        /// (`AMBER274`, физика 28) То же со СТРОКАМИ ЛИНИЙ КОНВЕРСИИ на точной энергии
        /// <paramref name="lines"/> (кэВ; электрон, тот же перенос, что у узла; зерно строки
        /// линии j — от номера 8001 + j, у узлов 7001 + i). null или пусто — строк линий нет.
        /// Энергии вне сетки узлов отбрасываются, совпадающие ближе
        /// <see cref="ResponseElectronSection.LineToleranceKev"/> сливаются.
        /// </summary>
        public static ResponseElectronSection BuildElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   double[] grid, int histories, int seed,
                                                                   bool positron, int threads,
                                                                   CancellationToken cancellation,
                                                                   Action<string> log, double[] lines)
        {
            return BuildElectronSection(geometry, matrix, grid, histories, seed, positron, threads, cancellation, log,
                                        lines, null);
        }

        /// <summary>
        /// (`AMBER274` Ф8, полоса `f8cyl` 02.11.2026) То же со СТРОКАМИ ВЕТВЕЙ — шов события
        /// целиком (решение Amber 30.10.2026 вопросником, дословно: «В Э1 событием целиком
        /// (Рекомендую)»): у каждой ветви <paramref name="branches"/> — бета и кванты её каскада
        /// одной историей (<see cref="EfficiencySimulator.BranchEventResponse"/>), зерно строки
        /// ветви j — от номера 9001 + j. null или пусто — ключ шва ВЫКЛ: строк ветвей нет, строки
        /// узлов и линий — побайтно те же, что без шва (их зёрна свои). Ветви без каскада и
        /// с негодными таблицами пропускаются с записью в журнал.
        /// </summary>
        public static ResponseElectronSection BuildElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   double[] grid, int histories, int seed,
                                                                   bool positron, int threads,
                                                                   CancellationToken cancellation,
                                                                   Action<string> log, double[] lines,
                                                                   IList<ElectronBranchSpec> branches)
        {
            return BuildElectronSection(geometry, matrix, grid, histories, seed, positron, threads, cancellation, log,
                                        lines, branches, null);
        }

        /// <summary>
        /// (`AMBER305`) Раздел рецептом склада к только что посчитанной матрице
        /// (<paramref name="gpu"/> — на устройстве, null — на ЦП в <paramref name="threads"/>
        /// потоков). <paramref name="previous"/> берётся без счёта, только если его клеймо —
        /// то самое, что дал бы этот счёт (та же физика раздела, матрица, сетка, истории,
        /// зерно, бин, капсула, линии и ветви библиотеки), и разбор его принимает: раздел от
        /// фотонных строк не зависит, а считать его заново значит только ждать. Всякий иной
        /// прежний раздел негоден, и прежний перенос по одному `ForMatrixStamp` пропускал его
        /// молча: разбор потом отказывал (другая капсула, прежняя физика раздела).
        ///
        /// Сцена изотропного поля раздела не имеет (`Ф5`). Отмена и отказ счёта — исключением.
        /// </summary>
        public static ElectronSectionOutcome AttachElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   ResponseElectronSection previous, RmGpu gpu, int threads,
                                                                   IProgress<ResponseMatrixProgress> progress,
                                                                   CancellationToken cancellation, Action<string> log)
        {
            // Прежний путь (склад, `MatrixRecomputeProbe`): номинал — рычаг проб, счёт плоский.
            return AttachElectronSection(geometry, matrix, previous, gpu, threads, progress, cancellation, log,
                                         ElectronSectionRecipe.Histories, 0.0);
        }

        /// <summary>
        /// ⛔ (`AMBER318`, решение Amber 07.11.2026 вопросником, дословно: «Свое поле, дефолт 800000 с
        /// остановкой по шуму»; где действует — «Как фотоны (Рекомендую)») То же с НОМИНАЛОМ из поля
        /// окна <paramref name="histories"/> и целью останова по шуму <paramref name="errorTarget"/>, %
        /// (0 — плоско). Номинал идёт в клеймо (`n=`) и в счёт; цель — только в счёт на ЦП
        /// (<see cref="BuildElectronSection(GeometryModel, ResponseMatrix, double[], int, int, bool, int, CancellationToken, Action{string}, double[], IList{ElectronBranchSpec}, IProgress{ResponseMatrixProgress}, double)"/>):
        /// на GPU раздел плоский, как и фотонная часть (`AMBER219`). Прежний раздел того же клейма
        /// переносится без счёта, как бы он ни был посчитан — плоско или с остановом.
        /// </summary>
        public static ElectronSectionOutcome AttachElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   ResponseElectronSection previous, RmGpu gpu, int threads,
                                                                   IProgress<ResponseMatrixProgress> progress,
                                                                   CancellationToken cancellation, Action<string> log,
                                                                   int histories, double errorTarget)
        {
            if (geometry == null || matrix == null || matrix.Options == null)
            {
                throw new ArgumentNullException("matrix");
            }

            if (geometry.Scene == GeometrySceneKind.Iso)
            {
                return ElectronSectionOutcome.NotApplicable;
            }

            ElectronSectionRecipe recipe = ElectronSectionRecipe.FromLibrary(log);
            cancellation.ThrowIfCancellationRequested();
            if (previous != null && string.Equals(previous.Stamp, recipe.StampFor(geometry, matrix, histories), StringComparison.Ordinal))
            {
                matrix.ElectronSection = previous;
                ElectronSectionRefusal refusal;
                if (matrix.ElectronSectionFor(geometry, out refusal) != null)
                {
                    return ElectronSectionOutcome.Carried;
                }

                matrix.ElectronSection = null;
            }

            matrix.ElectronSection = gpu != null
                ? GpuBuild.BuildElectronSection(gpu, geometry, matrix, recipe.Grid, histories,
                                                ElectronSectionRecipe.Seed, ElectronSectionRecipe.Positron, recipe.Lines,
                                                recipe.Branches, cancellation, log, progress)
                : BuildElectronSection(geometry, matrix, recipe.Grid, histories,
                                       ElectronSectionRecipe.Seed, ElectronSectionRecipe.Positron, threads, cancellation,
                                       log, recipe.Lines, recipe.Branches, progress, errorTarget);
            return ElectronSectionOutcome.Built;
        }

        /// <summary>(`AMBER305`) То же с ходом по строкам раздела; <paramref name="progress"/> на числа не влияет. Плоско.</summary>
        public static ResponseElectronSection BuildElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   double[] grid, int histories, int seed,
                                                                   bool positron, int threads,
                                                                   CancellationToken cancellation,
                                                                   Action<string> log, double[] lines,
                                                                   IList<ElectronBranchSpec> branches,
                                                                   IProgress<ResponseMatrixProgress> progress)
        {
            return BuildElectronSection(geometry, matrix, grid, histories, seed, positron, threads, cancellation, log,
                                        lines, branches, progress, 0.0);
        }

        /// <summary>
        /// ⛔ (`AMBER318`) То же с ОСТАНОВОМ ПО ШУМУ: <paramref name="errorTarget"/> &gt; 0 — каждая строка
        /// (узел, линия, ветвь) идёт пробным проходом N/<see cref="ResponseMatrixOptions.PilotDivisor"/>
        /// (не меньше <see cref="MinPilotHistories"/>, не больше N), по его шуму
        /// (<see cref="EfficiencySimulator.LastContinuumRelativeError"/>, у строки электрона —
        /// <c>SetRowNoise</c>, та же мера, что у фотонного узла) считается нужное число историй
        /// (<see cref="NeededHistories"/>, потолок N×<see cref="ResponseMatrixOptions.MaxHistoriesFactor"/>),
        /// и строка пересчитывается ЗАНОВО тем же зерном — не больше <see cref="MaxNodePasses"/> проходов,
        /// ровно как узел фотонной части. Тихая строка останавливается на пробном проходе, шумная
        /// досчитывается. У узла с позитроном шум — худший из двух строк (e⁻, e⁺) одних историй.
        /// Делители и потолок — из <see cref="ResponseMatrix.Options"/>, как у её фотонных узлов.
        /// ⛔ Второй заход (решение Amber 08.11.2026: «Малые строки не досчитывать (Рекомендую)»):
        /// строка, чья площадь меньше <see cref="SectionSmallRowShare"/> от наибольшей площади строк
        /// всего раздела по пробному проходу (пятый заход; прежде — своего вида), сверх номинала не досчитывается — в образ нуклида она
        /// входит своей площадью, и её шум в спектре не виден. Решение «нужен ли проход» — по
        /// наибольшим площадям пробного прохода, поэтому проходы идут двумя фазами, как у фотонных узлов.
        /// ⛔ Третий заход (решение Amber 08.11.2026: «Бюджет раздела (Рекомендую)»): всего историй — не
        /// больше плоского счёта (<see cref="SectionBudget"/>), остаток бюджета делится в долях нужды.
        /// Четвёртый и пятый заходы: бюджет — 0.97 объёма работы плоского счёта по счётчику шагов
        /// (<see cref="SectionBudgetMode"/>); номинал меньше <see cref="SectionStopMinHistories"/> — плоско.
        /// 0 — плоско: каждая строка ровно N историй, побитово прежний счёт.
        /// </summary>
        public static ResponseElectronSection BuildElectronSection(GeometryModel geometry, ResponseMatrix matrix,
                                                                   double[] grid, int histories, int seed,
                                                                   bool positron, int threads,
                                                                   CancellationToken cancellation,
                                                                   Action<string> log, double[] lines,
                                                                   IList<ElectronBranchSpec> branches,
                                                                   IProgress<ResponseMatrixProgress> progress,
                                                                   double errorTarget)
        {
            if (geometry == null || matrix == null || matrix.Options == null)
            {
                throw new ArgumentNullException("matrix");
            }

            if (!matrix.IsValidFor(geometry))
            {
                throw new InvalidOperationException("матрица не годна для сцены " + geometry.Name
                                                    + ": клеймо фотонной части не сошлось");
            }

            // (`Ф5` критика физики 30.10.2026) У сцены изотропного поля источника в сцене
            // нет — её «точка» есть ламбертова сфера в пустоте, и электрон с неё не бета
            // пробы. Раздела у неё не бывает, разбор его не берёт (`ElectronSectionFor`).
            if (geometry.Scene == GeometrySceneKind.Iso)
            {
                throw new InvalidOperationException("сцена изотропного поля " + geometry.Name
                                                    + ": раздела электрона пробы у неё нет (AMBER274 Ф5)");
            }

            if (grid == null || grid.Length < 2)
            {
                grid = ResponseElectronSection.DefaultGrid();
            }

            ResponseMatrixOptions options = matrix.Options.Clone();
            double bin = matrix.BinKev > 0.0 ? matrix.BinKev : options.BinKev;
            var watch = Stopwatch.StartNew();

            // Якорь шкалы — свет пика 661.657 кэВ тем же симулятором.
            double lightPerKev = 0.0;
            if (options.LightNonproportionality)
            {
                EfficiencySimulator anchor = MakeSimulator(geometry, options, 7000, 661.657);
                anchor.Histories = 20000;
                double anchorError;
                anchor.Response(661.657, bin, out anchorError);
                lightPerKev = anchor.LastPhotonLightScale;
            }

            int n = grid.Length;
            double[] lineEnergies = ResponseElectronSection.CleanLines(lines, grid);
            int nl = lineEnergies != null ? lineEnergies.Length : 0;
            List<ElectronBranchSpec> branchList = CleanBranches(branches, log);
            int nb = branchList.Count;
            // (`AMBER318`) Останов по шуму — та же раскладка чисел, что у фотонных узлов (`Build`):
            // пробный проход, потолок и правило нужного числа историй (`NeededHistories`).
            // ⛔ Второй заход (решение Amber 08.11.2026 вопросником, дословно: «Малые строки не
            // досчитывать (Рекомендую)»): строка, чья ПЛОЩАДЬ (доля испущенных частиц, оставивших
            // энергию в кристалле) меньше доли <see cref="SectionSmallRowShare"/> от наибольшей площади
            // строк того же вида, сверх номинала не досчитывается; заметная — досчитывается до цели.
            // ⛔ (`AMBER318`, пятый заход, находка `crit318b`) Номинал меньше
            // <see cref="SectionStopMinHistories"/> — плоско: пробный проход (не меньше
            // <see cref="MinPilotHistories"/>) съел бы весь бюджет, и клеймо `n=` разошлось бы с набранным.
            bool adaptive = errorTarget > 0.0 && histories >= SectionStopMinHistories;
            if (errorTarget > 0.0 && !adaptive && log != null)
            {
                log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  останов по шуму не включён: номинал {0} меньше {1} — счёт плоский", histories, SectionStopMinHistories));
            }
            int pilot = adaptive
                ? Math.Min(histories, Math.Max(MinPilotHistories, histories / Math.Max(1, options.PilotDivisor)))
                : histories;
            int cap = (int)Math.Min(int.MaxValue, (long)Math.Max(1, histories) * Math.Max(1, options.MaxHistoriesFactor));
            double smallShare = SectionSmallRowShare;
            int rows = n + nl + nb;

            var section = new ResponseElectronSection
            {
                Stamp = ResponseElectronSection.ComputeStamp(geometry, matrix.Stamp, grid, histories, seed, positron, bin,
                                                             lineEnergies, branchList),
                ForMatrixStamp = matrix.Stamp,
                BinKev = bin,
                Energies = (double[])grid.Clone(),
                Electron = new float[n][],
                Positron = positron ? new float[n][] : null,
                Histories = new long[n],
                EnteredElectron = new double[n],
                EnteredPositron = new double[n],
                NodeSeconds = new double[n],
                LightPerKev = lightPerKev,
                Capsule = ResponseElectronSection.CapsuleText(geometry),
                Engine = "cpu",
                CreatedUtc = DateTime.UtcNow,
                LineEnergies = lineEnergies,
                LineElectron = nl > 0 ? new float[nl][] : null,
                LineHistories = nl > 0 ? new long[nl] : null,
                LineEntered = nl > 0 ? new double[nl] : null,
            };

            if (nb > 0)
            {
                AllocateBranches(section, branchList);
            }

            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, threads),
                CancellationToken = cancellation,
            };
            object gate = new object();

            // Состояние строк: r < n — узел (A — e⁻, B — e⁺), r < n + nl — линия (A), иначе ветвь (A —
            // строка события целиком). Шум — мера фотонного узла (`SetRowNoise`), площадь — сумма строки
            // на испущенную частицу. Пишет ячейку строки только поток, что её считает.
            int[] last = new int[rows];
            double[] noiseA = new double[rows], noiseB = new double[rows];
            double[] areaA = new double[rows], areaB = new double[rows];
            double[] perHistory = new double[rows];
            // (`AMBER318`, четвёртый заход) Счётчики работы последнего прохода строки — детерминированы
            // (поток случайных чисел строки свой), в отличие от часов: из них цена истории для бюджета.
            long[][] work = new long[rows][];
            Action<int, EfficiencySimulator> recordWork = (r, s) =>
            {
                work[r] = new[]
                {
                    s.CountAt, s.CountStep, s.CountMu, s.CountWalk, s.CountLayerSteps, s.CountCrystalSteps,
                    s.CountLayerHardCollisions, s.CountCrystalHardCollisions
                };
            };
            var nodeE = new EfficiencySimulator.ElectronSourceStats[n];
            var nodeP = new EfficiencySimulator.ElectronSourceStats[n];
            var branchStats = new EfficiencySimulator.BranchEventStats[nb];
            Func<int, double> energyOf = r => r < n ? grid[r] : r < n + nl ? lineEnergies[r - n] : branchList[r - n - nl].EndpointKev;

            Action<int, int> runRow = (r, h) =>
            {
                long ticks0 = Stopwatch.GetTimestamp();
                if (r < n)
                {
                    int i = r;
                    double te = grid[i];
                    EfficiencySimulator sim = MakeSimulator(geometry, options, 7001 + i, te);
                    sim.Histories = Math.Max(1000, h);
                    int baseSeed = seed != 0 ? seed : sim.Seed;
                    sim.ResetStream((ulong)baseSeed + (ulong)(7001 + i) * 0x9E3779B97F4A7C15UL);
                    if (cancellation.CanBeCanceled)
                    {
                        sim.PollCancellation = cancellation.ThrowIfCancellationRequested;
                    }

                    EfficiencySimulator.ElectronSourceStats es, ps = null;
                    double[] e = sim.ElectronSourceResponse(te, false, bin, lightPerKev, out es);
                    noiseA[r] = sim.LastContinuumRelativeError;
                    double[] p = positron ? sim.ElectronSourceResponse(te, true, bin, lightPerKev, out ps) : null;
                    noiseB[r] = positron ? sim.LastContinuumRelativeError : 0.0;
                    section.Electron[i] = ResponseElectronSection.Trim(e);
                    areaA[r] = SumRow(section.Electron[i]);
                    if (positron)
                    {
                        section.Positron[i] = ResponseElectronSection.Trim(p);
                        areaB[r] = SumRow(section.Positron[i]);
                    }

                    section.Histories[i] = es.Histories;
                    section.EnteredElectron[i] = (double)es.Entered / es.Histories;
                    section.EnteredPositron[i] = ps != null ? (double)ps.Entered / ps.Histories : 0.0;
                    section.NodeSeconds[i] += es.Seconds + (ps != null ? ps.Seconds : 0.0);
                    nodeE[i] = es;
                    nodeP[i] = ps;
                    recordWork(r, sim);
                }
                else if (r < n + nl)
                {
                    // (`AMBER274`, физика 28) Строка линии конверсии на точной энергии — электрон, тот
                    // же перенос; зерно — от номера 8001 + j.
                    int j = r - n;
                    double te = lineEnergies[j];
                    EfficiencySimulator sim = MakeSimulator(geometry, options, 8001 + j, te);
                    sim.Histories = Math.Max(1000, h);
                    int baseSeed = seed != 0 ? seed : sim.Seed;
                    sim.ResetStream((ulong)baseSeed + (ulong)(8001 + j) * 0x9E3779B97F4A7C15UL);
                    if (cancellation.CanBeCanceled)
                    {
                        sim.PollCancellation = cancellation.ThrowIfCancellationRequested;
                    }

                    EfficiencySimulator.ElectronSourceStats es;
                    double[] e = sim.ElectronSourceResponse(te, false, bin, lightPerKev, out es);
                    noiseA[r] = sim.LastContinuumRelativeError;
                    section.LineElectron[j] = ResponseElectronSection.Trim(e);
                    areaA[r] = SumRow(section.LineElectron[j]);
                    section.LineHistories[j] = es.Histories;
                    section.LineEntered[j] = (double)es.Entered / es.Histories;
                    recordWork(r, sim);
                }
                else
                {
                    // (`AMBER274` Ф8, `f8cyl`) Строка ветви — событие целиком; зерно — от номера 9001 + j.
                    int j = r - n - nl;
                    ElectronBranchSpec spec = branchList[j];
                    EfficiencySimulator sim = MakeSimulator(geometry, options, 9001 + j, spec.EndpointKev);
                    sim.Histories = Math.Max(1000, h);
                    int baseSeed = seed != 0 ? seed : sim.Seed;
                    sim.ResetStream((ulong)baseSeed + (ulong)(9001 + j) * 0x9E3779B97F4A7C15UL);
                    if (cancellation.CanBeCanceled)
                    {
                        sim.PollCancellation = cancellation.ThrowIfCancellationRequested;
                    }

                    EfficiencySimulator.BranchEventStats bs;
                    double[][] pair = sim.BranchEventResponse(spec, bin, lightPerKev, out bs);
                    noiseA[r] = sim.LastContinuumRelativeError;
                    section.BranchEvent[j] = ResponseElectronSection.Trim(pair[0]);
                    section.BranchGamma[j] = ResponseElectronSection.Trim(pair[1]);
                    areaA[r] = SumRow(section.BranchEvent[j]);
                    section.BranchHistories[j] = bs.Histories;
                    section.BranchEntered[j] = (double)bs.Entered / bs.Histories;
                    branchStats[j] = bs;
                    recordWork(r, sim);
                }

                last[r] = h;
                perHistory[r] = (double)(Stopwatch.GetTimestamp() - ticks0) / Stopwatch.Frequency / Math.Max(1000, h);
            };

            // Вид строки для «наибольшей площади»: 0 — узлы e⁻, 1 — узлы e⁺, 2 — линии, 3 — ветви.
            double[] maxArea = new double[4];
            Func<int, int> kindA = r => r < n ? 0 : r < n + nl ? 2 : 3;
            Func<int, int, double, double, double, double, int> subWant = (h, kind, noise, area, share, dummy) =>
            {
                int need = NeededHistories(h, noise, errorTarget, cap);
                if (share > 0.0 && area < share * maxArea[kind])
                {
                    need = Math.Min(need, Math.Max(h, histories));
                }

                return need;
            };
            Func<int, int, double, int> wantOf = (r, h, share) =>
            {
                int want = Math.Max(h, subWant(h, kindA(r), noiseA[r], areaA[r], share, 0.0));
                if (r < n && positron)
                {
                    want = Math.Max(want, subWant(h, 1, noiseB[r], areaB[r], share, 0.0));
                }

                return want;
            };

            // (`AMBER318`, пятый заход) Доля пробного прохода в полосе хода — его цена против номинала.
            var sink = new SectionPassReport(progress, rows, (long)rows * Math.Max(1000, pilot),
                                             adaptive ? (double)pilot / Math.Max(1, histories) : 0.0);
            long spentRows = 0L;
            int stoppedEarly = 0, extended = 0, cappedSmall = 0, budgetCut = 0;
            Action<int> settle = r =>
            {
                double kev = energyOf(r);
                sink.Settle(kev);
                if (adaptive)
                {
                    if (last[r] < histories) Interlocked.Increment(ref stoppedEarly);
                    else if (last[r] > histories) Interlocked.Increment(ref extended);
                }

                if (log == null)
                {
                    return;
                }

                lock (gate)
                {
                    if (r < n)
                    {
                        EfficiencySimulator.ElectronSourceStats es = nodeE[r], ps = nodeP[r];
                        log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "  узел {0,2} T {1,7:F1} кэВ: e- вошло {2:F5}, занос {3:E3} на частицу, квантов очереди {4:F3}; " +
                            "e+ вошло {5:F5}, аннигиляций в сцене {6:F3}; {7:F1} с; историй {8}, шум e- {9:F2} %, e+ {10:F2} %; площадь e- {11:E3}, e+ {12:E3}",
                            r, kev, section.EnteredElectron[r], es.MeanDepositKev / Math.Max(kev, 1e-9),
                            (double)es.QueuedPhotons / es.Histories, section.EnteredPositron[r],
                            ps != null ? (double)ps.Annihilations / ps.Histories : 0.0, section.NodeSeconds[r],
                            section.Histories[r], noiseA[r], noiseB[r], areaA[r], areaB[r]));
                    }
                    else if (r < n + nl)
                    {
                        if (adaptive)
                        {
                            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "  линия {0,3} T {1,7:F1} кэВ: историй {2}, шум {3:F2} %, площадь {4:E3}",
                                r - n, kev, section.LineHistories[r - n], noiseA[r], areaA[r]));
                        }
                    }
                    else
                    {
                        int j = r - n - nl;
                        ElectronBranchSpec spec = branchList[j];
                        EfficiencySimulator.BranchEventStats bs = branchStats[j];
                        log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "  ветвь {0,3} {1} {2:F1} кэВ → уровень {3:F1}: бета вошла {4:F5}, квантов каскада {5:F3} на распад, " +
                            "событие {6:E3}, каскад {7:E3} на распад; упоров каскада {8}; {9:F1} с; историй {10}, шум {11:F2} %",
                            j, spec.Nuclide, spec.EndpointKev, spec.LevelKev, section.BranchEntered[j],
                            (double)bs.CascadeQuanta / bs.Histories, SumRow(section.BranchEvent[j]),
                            SumRow(section.BranchGamma[j]), bs.CascadeCut, bs.Seconds, section.BranchHistories[j], noiseA[r]));
                    }
                }
            };

            // Проход 1: пробный (плоско — единственный) по всем строкам. Тихая строка (и плоская)
            // досчитана сразу; решение о прочих ждёт наибольших площадей.
            bool[] settled = new bool[rows];
            Parallel.ForEach(Partitioner.Create(0, rows, 1), parallel, chunk =>
            {
                for (int r = chunk.Item1; r < chunk.Item2; r++)
                {
                    runRow(r, pilot);
                    Interlocked.Add(ref spentRows, Math.Max(1000, pilot));
                    sink.PilotPass(energyOf(r), Math.Max(1000, pilot));
                    if (!adaptive || wantOf(r, pilot, 0.0) <= pilot)
                    {
                        settled[r] = true;
                        settle(r);
                    }
                }
            });

            if (adaptive)
            {
                for (int r = 0; r < rows; r++)
                {
                    int k = kindA(r);
                    maxArea[k] = Math.Max(maxArea[k], areaA[r]);
                    if (r < n && positron)
                    {
                        maxArea[1] = Math.Max(maxArea[1], areaB[r]);
                    }
                }

                // ⛔ (`AMBER318`, пятый заход, находка `crit318b`) «Малая» — от наибольшей площади во ВСЁМ
                // разделе (узлы e⁻, e⁺, линии, ветви — одна мера: доля испущенных, оставивших энергию в
                // кристалле), а не своего вида: у Петри все линии ничтожны, но по своему виду восемь из них
                // проходили порог и брали потолок. Рычаг проб `SectionSmallByKind` — прежнее правило.
                if (!SectionSmallByKind)
                {
                    double whole = Math.Max(Math.Max(maxArea[0], maxArea[1]), Math.Max(maxArea[2], maxArea[3]));
                    for (int k = 0; k < maxArea.Length; k++)
                    {
                        maxArea[k] = whole;
                    }
                }

                if (log != null)
                {
                    // (`AMBER318`, пятый заход) Цена — по счётчику работы (шаги обхода + истории), как бюджет.
                    double[] workPerHistory = new double[rows];
                    for (int r = 0; r < rows; r++)
                    {
                        workPerHistory[r] = ((double)work[r][1] + Math.Max(1000, last[r])) / Math.Max(1000, last[r]);
                    }

                    LogShareTable(log, rows, pilot, histories, workPerHistory, settled, r => wantOf(r, pilot, 0.0),
                                  share => (r => wantOf(r, pilot, share)));
                    if (SectionWorkDump)
                    {
                        // (`AMBER318`, четвёртый заход) Строки пробного прохода для подбора меры цены:
                        // часы на историю против счётчиков работы на историю.
                        log("  РАБОТА;строка;вид;кэВ;историй;с_на_историю;At;Step;Mu;Walk;LayerSteps;CrystalSteps;LayerHard;CrystalHard");
                        for (int r = 0; r < rows; r++)
                        {
                            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "  РАБОТА;{0};{1};{2:R};{3};{4:R};{5}", r, kindA(r), energyOf(r), last[r], perHistory[r],
                                string.Join(";", Array.ConvertAll(work[r], v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
                        }
                    }
                }

                for (int r = 0; r < rows; r++)
                {
                    if (!settled[r] && wantOf(r, pilot, smallShare) < wantOf(r, pilot, 0.0))
                    {
                        cappedSmall++;
                    }
                }

                if (SectionStopPilotOnly)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        if (!settled[r]) settle(r);
                    }
                }
                else
                {
                    // ⛔ (`AMBER318`, третий заход; решение Amber 08.11.2026 вопросником, дословно: «Бюджет
                    // раздела (Рекомендую)») БЮДЖЕТ: всего историй на раздел — не больше плоского счёта
                    // (номинал × строк, пробные проходы внутри). Остаток бюджета раздаётся в начале каждого
                    // уточняющего прохода строкам, которым нужен проход (малая — не выше номинала, заметная —
                    // по нужде до потолка): хватает — каждой её нужда, не хватает — всем поровну в долях нужды
                    // (`floor(нужда · остаток / Σ нужд)`). Тихие строки (остановленные на пробном проходе) и
                    // малые этим отдают свои истории заметным. Строка, чья доля не больше уже набранного,
                    // проходом не пересчитывается. Решение — между проходами, по готовым числам и в порядке
                    // номеров строк: при любом числе потоков одно и то же. Строка пересчитывается тем же зерном.
                    // ⛔ (`AMBER318`, четвёртый заход; решение Amber 08.11.2026 вопросником, дословно: «Бюджет
                    // во времени (Рекомендую)») Бюджет и раздача — в ЦЕНЕ СЧЁТА, а не в историях. Цена истории
                    // строки — по счётчику работы её пробного прохода (`SectionCostPerHistory`: шагов обхода
                    // сцены `CountStep` плюс одна на историю), а не по часам: счётчик детерминирован (поток
                    // случайных чисел строки свой), часы — нет, и раздача по часам давала бы разный раздел при
                    // разной загрузке и числе потоков. Бюджет = Σ цена истории × номинал (цена плоского счёта);
                    // истрачено — по счётчикам каждого прохода. `SectionBudgetMode` 1 — в историях (третий
                    // заход), 3 — по часам (только положительный контроль недетерминированности), 0 — без бюджета.
                    int mode = SectionBudgetMode;
                    double[] unitCost = new double[rows];
                    double budget = double.PositiveInfinity, spentCost = 0.0;
                    Func<int, int, double> passCost = (r, h) =>
                        mode == 2 ? (double)work[r][1] + Math.Max(1000, h)
                        : mode == 3 ? perHistory[r] * Math.Max(1000, h)
                        : Math.Max(1000, h);
                    if (mode != 0)
                    {
                        budget = 0.0;
                        for (int r = 0; r < rows; r++)
                        {
                            unitCost[r] = passCost(r, last[r]) / Math.Max(1000, last[r]);
                            budget += unitCost[r] * Math.Max(1000, histories);
                            spentCost += passCost(r, last[r]);
                        }

                        // Запас на неточность меры: счётчик — оценка цены, а не часы (у маринелли KCl RC-103
                        // бюджет 1.0000 по счётчику дал 1.009 по времени ЦП, журнал «Четвёртый заход», Ч3).
                        budget *= SectionBudgetShare;
                    }

                    double budgetPilot = spentCost;
                    int[] want = new int[rows];
                    var heavy = new List<int>();
                    for (int r = 0; r < rows; r++)
                    {
                        if (!settled[r])
                        {
                            heavy.Add(r);
                        }
                    }

                    for (int round = 1; round < MaxNodePasses && heavy.Count > 0; round++)
                    {
                        double need = 0.0;
                        var next = new List<int>();
                        foreach (int r in heavy)
                        {
                            want[r] = wantOf(r, last[r], smallShare);
                            if (want[r] > last[r])
                            {
                                next.Add(r);
                                need += want[r] * (mode != 0 ? unitCost[r] : 1.0);
                            }
                            else
                            {
                                settled[r] = true;
                                settle(r);
                            }
                        }

                        double left = Math.Max(0.0, budget - spentCost);
                        if (need > left)
                        {
                            double scale = left / need;
                            budgetCut += next.Count;
                            foreach (int r in next)
                            {
                                want[r] = (int)Math.Floor(want[r] * scale);
                            }
                        }

                        heavy.Clear();
                        foreach (int r in next)
                        {
                            if (want[r] > last[r] && want[r] >= MinPilotHistories)
                            {
                                heavy.Add(r);
                                sink.Plan(want[r]);
                            }
                            else
                            {
                                settled[r] = true;
                                settle(r);
                            }
                        }

                        // Дорогие вперёд (LPT), как у фотонных узлов.
                        heavy.Sort((a, b) => want[b] != want[a] ? want[b].CompareTo(want[a]) : a.CompareTo(b));
                        int[] order = heavy.ToArray();
                        bool lastRound = round + 1 >= MaxNodePasses;
                        if (order.Length == 0)
                        {
                            // Бюджет кончился или всем хватило пробного: проходов больше нет
                            // (`Partitioner.Create(0, 0, 1)` бросает).
                            break;
                        }

                        Parallel.ForEach(Partitioner.Create(0, order.Length, 1), parallel, chunk =>
                        {
                            for (int slot = chunk.Item1; slot < chunk.Item2; slot++)
                            {
                                int r = order[slot];
                                runRow(r, want[r]);
                                Interlocked.Add(ref spentRows, Math.Max(1000, want[r]));
                                sink.Pass(energyOf(r), Math.Max(1000, want[r]));
                                // ⛔ (`AMBER318`, пятый заход, находка `crit318b`) Строка досчитана СРАЗУ, как её
                                // проход кончился и больше ей не нужно, — как узел у фотонов; прежде она объявлялась
                                // досчитанной лишь в начале следующего раунда, и ход стоял весь счёт. С бюджетом
                                // уточняющий проход один (остаток бюджета после него не раздаётся: раздача — по
                                // нуждам всех строк сразу); без бюджета решение о следующем — тем же правилом.
                                bool more = !lastRound && mode == 0 && wantOf(r, last[r], smallShare) > last[r];
                                if (!more)
                                {
                                    settled[r] = true;
                                    settle(r);
                                }
                            }
                        });

                        // Истрачено — по счётчикам прохода, в порядке номеров строк (сумма одна при любом числе потоков).
                        Array.Sort(order);
                        foreach (int r in order)
                        {
                            spentCost += passCost(r, last[r]);
                        }

                        heavy.RemoveAll(r => settled[r]);
                        heavy.Sort();
                    }

                    if (log != null && mode != 0)
                    {
                        log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "  бюджет раздела ({0}): {5:F3} цены плоского счёта = {1:G6}, пробные проходы {2:G6}, истрачено {3:G6} = {4:F4} бюджета = {6:F4} цены плоского",
                            mode == 2 ? "шаги обхода + истории" : mode == 1 ? "истории" : "часы, с", budget, budgetPilot, spentCost,
                            budget > 0.0 ? spentCost / budget : 0.0, SectionBudgetShare,
                            budget > 0.0 ? spentCost / budget * SectionBudgetShare : 0.0));
                    }
                }
            }

            if (log != null)
            {
                log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  раздел: {0} узлов, {5} строк линий и {6} строк ветвей, {1} историй на строку, свет 662 на кэВ {2:F6}, капсула {3}, {4:F1} с",
                    n, histories, lightPerKev, section.Capsule, watch.Elapsed.TotalSeconds, nl, nb));
                if (adaptive)
                {
                    log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "  останов по шуму {0:F1} %: пробный проход {1}, потолок {2}, малая строка — площадь < {9:G4} наибольшей в разделе; " +
                        "истрачено {3} историй ({4:F3} от плоского {5}×{6}); раньше номинала {7} строк, сверх номинала {8}, малых не досчитано сверх номинала {10}; " +
                        "бюджет {12}, урезано бюджетом строк {13}{11}",
                        errorTarget, pilot, cap, spentRows, (double)spentRows / ((double)rows * Math.Max(1000, histories)),
                        rows, Math.Max(1000, histories), stoppedEarly, extended, smallShare, cappedSmall,
                        SectionStopPilotOnly ? "; ⚠ ТОЛЬКО ПРОБНЫЙ ПРОХОД (рычаг SectionStopPilotOnly)" : "",
                        SectionBudgetMode == 2 ? "по счётчику работы" : SectionBudgetMode == 1 ? "в историях" : SectionBudgetMode == 3 ? "ПО ЧАСАМ (контроль)" : "ВЫКЛ", budgetCut));
                }
            }

            return section;
        }

        /// <summary>
        /// ⛔ (`AMBER318`, второй заход 08.11.2026) ПОРОГ «МАЛОЙ СТРОКИ» раздела при останове по шуму:
        /// строка, чья площадь (доля испущенных частиц, оставивших энергию в кристалле, — то, чем
        /// строка входит в образ нуклида) меньше этой доли от наибольшей площади строк во всём разделе
        /// (узлы e⁻, узлы e⁺, линии, ветви вместе; пятый заход), сверх номинала не досчитывается. 0 — правило выключено
        /// (первый заход: шумная строка досчитывается до потолка, какой бы малой она ни была).
        /// Величина — замером (журнал `handover/handover-2026-11-07-a318.md`, «Пятый заход», П3): доля от
        /// наибольшей площади во ВСЁМ разделе; 0.05 — наименьшая, при которой работа без бюджета не больше
        /// 0.97 плоского на Петри K-40 (0.937), точке RC-103 (0.420) и точке AS80 (0.718); маринелли KCl
        /// не укладывается ни при какой доле — там работу держит бюджет. Рычаг проб
        /// (`BetaSectionProbeA274 --small=`); окно берёт умолчание.
        /// </summary>
        public static double SectionSmallRowShare = 0.05;

        /// <summary>
        /// (`AMBER318`) РЫЧАГ ПРОБ: счёт раздела с остановом кончается пробным проходом, а таблица
        /// «порог малой строки → предсказанные истории и время» печатается в журнал построителя —
        /// дешёвый подбор порога (`BetaSectionProbeA274 --stop-dry`). Раздел при этом пробный, в
        /// дело не годен. Штатный путь рычаг не ставит.
        /// </summary>
        public static bool SectionStopPilotOnly;

        /// <summary>(`AMBER318`, четвёртый заход) РЫЧАГ ПРОБ: печать часов и счётчиков работы пробного прохода по строкам (`--work-dump`).</summary>
        public static bool SectionWorkDump;

        /// <summary>
        /// ⛔ (`AMBER318`, третий заход; решение Amber 08.11.2026 вопросником, дословно: «Бюджет раздела
        /// (Рекомендую)») Раздел с остановом по шуму тратит не больше историй, чем плоский счёт того же
        /// номинала: остаток бюджета после пробного прохода делится между строками, которым нужен проход,
        /// в долях их нужды. Выключенный — рычаг проб для сравнения (`BetaSectionProbeA274 --no-budget`).
        /// </summary>
        public static bool SectionBudget
        {
            get { return SectionBudgetMode != 0; }
            set { SectionBudgetMode = value ? 2 : 0; }
        }

        /// <summary>
        /// ⛔ (`AMBER318`, четвёртый заход; решение Amber 08.11.2026: «Бюджет во времени (Рекомендую)»)
        /// В чём считается бюджет раздела: 2 — в цене счёта по счётчику работы пробного прохода
        /// (умолчание; шагов обхода сцены `CountStep` плюс одна на историю — корреляция с часами на
        /// историю 0.996…0.999 на четырёх сценах, журнал «Четвёртый заход»); 1 — в историях (третий
        /// заход); 3 — по часам пробного прохода (НЕ детерминирован: только положительный контроль);
        /// 0 — без бюджета. Рычаг проб (`BetaSectionProbeA274 --budget=`).
        /// ⚠ (пятый заход, `crit318b`) Бюджет держит ОБЪЁМ РАБОТЫ по счётчику, а не время: время ЦП
        /// процесса на машине с SMT мерит плотность загрузки (одна работа — 338.7 с ЦП на 1 потоке и
        /// 677.6 с на 15), стена — ещё и хвост последнего прохода; по часам счёт с остановом может выйти
        /// на несколько процентов дольше или короче плоского.
        /// </summary>
        public static int SectionBudgetMode = 2;

        /// <summary>
        /// (`AMBER318`, четвёртый заход) Доля цены плоского счёта, которую раздел с остановом может
        /// истратить: запас на неточность счётчика как меры времени. Замер — журнал, «Четвёртый заход», Ч3.
        /// </summary>
        public static double SectionBudgetShare = 0.97;

        /// <summary>
        /// (`AMBER318`, пятый заход) Номинал, ниже которого останов по шуму не включается (счёт плоский):
        /// десять минимальных пробных проходов.
        /// </summary>
        public const int SectionStopMinHistories = 10 * MinPilotHistories;

        /// <summary>
        /// (`AMBER318`, пятый заход) РЫЧАГ ПРОБ: «малая строка» — от наибольшей площади своего вида (правило
        /// второго…четвёртого заходов); умолчание — от наибольшей во всём разделе (`--small-by-kind`).
        /// </summary>
        public static bool SectionSmallByKind;

        /// <summary>
        /// (`AMBER318`) Таблица подбора порога малой строки по пробному проходу: для каждой доли
        /// <c>P</c> — сколько историй и потокосекунд стоил бы счёт (пробный проход плюс один
        /// уточняющий до нужного числа; третий проход не предсказывается), сколько строк досчитано
        /// сверх номинала и сколько малых удержано номиналом; рядом — плоский счёт номиналом по цене
        /// истории, измеренной на пробном проходе каждой строки.
        /// </summary>
        static void LogShareTable(Action<string> log, int rows, int pilot, int histories, double[] perHistory,
                                  bool[] settled, Func<int, int> wantFree, Func<double, Func<int, int>> wantAt)
        {
            double flatSeconds = 0.0;
            for (int r = 0; r < rows; r++)
            {
                flatSeconds += perHistory[r] * Math.Max(1000, histories);
            }

            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  подбор порога малой строки (по пробному проходу): плоско {0} историй, работы (шаги обхода + истории) {1:G6}",
                (long)rows * Math.Max(1000, histories), flatSeconds));
            foreach (double share in new[] { 0.0, 0.001, 0.003, 0.01, 0.02, 0.03, 0.05, 0.1, 0.2, 0.3, 0.5, 1.0 })
            {
                Func<int, int> want = wantAt(share);
                long spent = 0;
                double seconds = 0.0;
                int above = 0, held = 0;
                for (int r = 0; r < rows; r++)
                {
                    long h = Math.Max(1000, pilot);
                    if (!settled[r])
                    {
                        int w = want(r);
                        if (w > pilot)
                        {
                            h += w;
                        }

                        if (w > histories) above++;
                        if (w < wantFree(r)) held++;
                    }

                    spent += h;
                    seconds += perHistory[r] * h;
                }

                log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  P {0,6:G3}: историй {1} ({2:F3} плоского), работы {3:G6} ({4:F3} плоского), сверх номинала строк {5}, малых удержано {6}",
                    share, spent, (double)spent / ((double)rows * Math.Max(1000, histories)), seconds,
                    flatSeconds > 0.0 ? seconds / flatSeconds : 0.0, above, held));
            }
        }

        /// <summary>(`AMBER311`) Строка «рассеянные вне кристалла» узла: пустая — нулевой длины, как пустой канал.</summary>
        internal static float[] BackscatterRowOf(double[] histogram)
        {
            bool any = false;
            for (int b = 0; histogram != null && b < histogram.Length && !any; b++)
            {
                any = histogram[b] > 0.0;
            }

            float[] row = new float[any ? histogram.Length : 0];
            for (int b = 0; b < row.Length; b++)
            {
                row[b] = (float)histogram[b];
            }

            return row;
        }

        /// <summary>
        /// (`AMBER305`) Отчёт о досчитанной строке раздела — общий у построителей ЦП и GPU; сразу
        /// отдаёт отчёт «0 из N»: окно называет фазу раньше первой строки. Без
        /// <paramref name="progress"/> — ничего. Счётчик и отчёт под одной защёлкой: отчёты
        /// потоков уходят по возрастанию, и полоса не пятится (`A46`).
        /// </summary>
        internal static Action<double> SectionRows(IProgress<ResponseMatrixProgress> progress, int total)
        {
            if (progress == null)
            {
                return kev => { };
            }

            progress.Report(new ResponseMatrixProgress { SectionTotal = total });
            object gate = new object();
            int done = 0;
            return kev =>
            {
                lock (gate)
                {
                    done++;
                    progress.Report(new ResponseMatrixProgress { SectionDone = done, SectionTotal = total, LastEnergyKev = kev });
                }
            };
        }

        /// <summary>
        /// (`AMBER318`) Ход раздела на ЦП. С остановом по шуму у строки бывает до
        /// <see cref="MaxNodePasses"/> проходов. Полоса и подпись идут по ДОСЧИТАННЫМ строкам
        /// (<see cref="ResponseMatrixProgress.SectionDone"/>: строка, которой следующий проход не
        /// положен); их число за счёт не меняется — полоса не пятится (`A46`). Прогоны и истории
        /// (<see cref="ResponseMatrixProgress.Done"/>/<see cref="ResponseMatrixProgress.Total"/>,
        /// <see cref="ResponseMatrixProgress.DoneHistories"/>/<see cref="ResponseMatrixProgress.TotalHistories"/>)
        /// растут, как у фотонных узлов (`W27`): строка, которой нужен ещё проход, дописывает его в
        /// план. Плоско — один проход и досчитана. Без <c>progress</c> — ничего; при создании
        /// отдаёт отчёт «0 из N». Отчёты — под одной защёлкой, по возрастанию.
        /// </summary>
        internal sealed class SectionPassReport
        {
            readonly IProgress<ResponseMatrixProgress> progress;
            readonly object gate = new object();
            readonly int rows;
            int settled, passes, planned, pilots;
            long spent, plan;
            double lastKev;
            readonly double pilotShare;

            /// <summary>Пробный проход строки окончен (при плоском счёте — её единственный проход).</summary>
            public void PilotPass(double kev, int histories)
            {
                lock (this.gate)
                {
                    this.pilots++;
                    this.passes++;
                    this.spent += histories;
                    this.lastKev = kev;
                    this.Report();
                }
            }

            public SectionPassReport(IProgress<ResponseMatrixProgress> progress, int rows, long firstPlanHistories,
                                     double pilotShare)
            {
                this.progress = progress;
                this.rows = rows;
                this.pilotShare = pilotShare;
                this.planned = rows;
                this.plan = firstPlanHistories;
                this.Report();
            }

            /// <summary>Проход строки окончен.</summary>
            public void Pass(double kev, int histories)
            {
                lock (this.gate)
                {
                    this.passes++;
                    this.spent += histories;
                    this.lastKev = kev;
                    this.Report();
                }
            }

            /// <summary>Строке нужен ещё проход в <paramref name="histories"/> историй.</summary>
            public void Plan(int histories)
            {
                lock (this.gate)
                {
                    this.planned++;
                    this.plan += histories;
                    this.Report();
                }
            }

            /// <summary>Строка досчитана окончательно.</summary>
            public void Settle(double kev)
            {
                lock (this.gate)
                {
                    this.settled++;
                    this.lastKev = kev;
                    this.Report();
                }
            }

            void Report()
            {
                if (this.progress == null)
                {
                    return;
                }

                this.progress.Report(new ResponseMatrixProgress
                {
                    SectionDone = this.settled,
                    SectionTotal = this.rows,
                    SectionPilotDone = this.pilotShare > 0.0 ? this.pilots : 0,
                    SectionPilotShare = this.pilotShare,
                    Done = this.passes,
                    Total = this.planned,
                    DoneHistories = this.spent,
                    TotalHistories = this.plan,
                    LastEnergyKev = this.lastKev
                });
            }
        }

        /// <summary>
        /// (`Ф8`) Ветви, годные для строки: с каскадом и согласованными таблицами, без повторов
        /// (тот же нуклид и граница); прочие — в журнал словами. Порядок — как дан.
        /// </summary>
        internal static List<ElectronBranchSpec> CleanBranches(IList<ElectronBranchSpec> branches, Action<string> log)
        {
            var result = new List<ElectronBranchSpec>();
            if (branches == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ElectronBranchSpec b in branches)
            {
                if (b == null)
                {
                    continue;
                }

                string problem = b.Problem();
                if (problem != null || !b.HasCascade)
                {
                    if (log != null)
                    {
                        log("  ветвь " + b.Key + " пропущена: " + (problem ?? "каскада нет"));
                    }

                    continue;
                }

                if (seen.Add(b.Key))
                {
                    result.Add(b);
                }
            }

            return result;
        }

        /// <summary>(`Ф8`) Места под строки ветвей раздела — общие у построителей ЦП и GPU.</summary>
        internal static void AllocateBranches(ResponseElectronSection section, List<ElectronBranchSpec> list)
        {
            int nb = list.Count;
            section.BranchNuclides = new string[nb];
            section.BranchEndpoints = new double[nb];
            section.BranchLevels = new double[nb];
            section.BranchHistories = new long[nb];
            section.BranchEntered = new double[nb];
            section.BranchHashes = new string[nb];
            section.BranchEvent = new float[nb][];
            section.BranchGamma = new float[nb][];
            for (int j = 0; j < nb; j++)
            {
                section.BranchNuclides[j] = list[j].Nuclide;
                section.BranchEndpoints[j] = list[j].EndpointKev;
                section.BranchLevels[j] = list[j].LevelKev;
                section.BranchHashes[j] = list[j].Hash;
            }
        }

        /// <summary>(`Ф8`) Площадь строки — для журнала построителя.</summary>
        internal static double SumRow(float[] row)
        {
            double s = 0.0;
            if (row != null)
            {
                foreach (float v in row)
                {
                    s += v;
                }
            }

            return s;
        }

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
                                  out double resolutionExtra, out double[] backscatter, CancellationToken cancellation)
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
            backscatter = sim.LastBackscatterRow;
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
                // ⛔ (`AMBER274`, физика 28) Страгглинг потерь электрона — тем же путём:
                // не доехав до построителя, ключ мёртв (`S130`), а клеймо `estr=1` лгало
                // бы о происхождении матрицы.
                ElectronStraggling = options.ElectronStraggling,
                // ⛔ (`AMBER274`, физика 29) Перенос электрона, сходящийся по шагу — тем же
                // путём: не доехав до построителя, ключ мёртв (`S130`), а клеймо `emix=1`
                // лгало бы о происхождении матрицы.
                ElectronMixedTransport = options.ElectronMixedTransport,
                // ⛔ (`AMBER274`, физика 29, `phys29b`) Поправка Мотта при Z > 30 — тем же
                // путём: не доехав до построителя, ключ мёртв (`S130`), а клеймо `mott=1`
                // лгало бы о происхождении матрицы.
                ElectronMottHeavy = options.ElectronMottHeavy,
                // ⛔ (`AMBER274`, физика 30) Правки критиков физики 29 и ядерная часть по DPWA — тем же
                // путём: не доехав до построителя, ключи мертвы (`S130`), а клеймо `eref=1`/`dpwa=1`
                // лгало бы о происхождении матрицы.
                ElectronElasticRefine = options.ElectronElasticRefine,
                ElectronScreeningDpwa = options.ElectronScreeningDpwa,
                // (`AMBER274`, физика 30) Вакансия L-кванта после Костера—Кронига (`ckv=1`) — тем же путём.
                CascadeCkVacancy = options.CascadeCkVacancy,
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
            // (`AMBER274`, физика 30) РЫЧАГ ЗАМЕРА ПРОБ, не настройка: рычаги симулятора, которых нет в
            // `ResponseMatrixOptions` (доля шага кристалла, части `ElectronRefineParts`), пробе иначе не достать —
            // а цену и смещение на GPU (упаковщик берёт этот же симулятор) иначе не измерить
            // (`CorpusMatrixProbe --sim-cstep= --sim-rparts=`). Ни один штатный путь его не ставит; матрица с ним —
            // замер, не склад.
            ProbeTweak?.Invoke(sim);
            return sim;
        }

        /// <summary>(`AMBER274`, физика 30) Рычаг замера проб — см. конец <see cref="MakeSimulator"/>; штатно null.</summary>
        internal static Action<EfficiencySimulator> ProbeTweak = null;

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
