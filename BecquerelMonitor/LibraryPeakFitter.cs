using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BecquerelMonitor
{
    /// <summary>
    /// Библиотечный фит по нуклидному сету: если FWHM-finder нашёл якорную
    /// линию сета (NuclideDefinition.IsAnchor), компоненты сажаются на линии
    /// сета в фиксированных (табличных) позициях, амплитуды фитятся
    /// Пуассон-правдоподобием тем же модельным стеком, что и деконволюция
    /// (профили PeakShapeModel, SASNIP-континуум + фон прибора). Значимые
    /// компоненты (Fisher z >= порога) добавляются как пики origin Library.
    ///
    /// Суб-Sparrow бленды линий одной цепочки решаются BR-связкой: линии
    /// ближе 0.85·FWHM объединяются в группу с одной свободной амплитудой и
    /// весами ∝ NuclideDefinition.Intencity (вековое равновесие ряда, кривая
    /// эффективности на близких энергиях сокращается). Пик-центроид бленда
    /// заменяется на линии группы, если это улучшает AIC (D + 2k).
    /// Дополнительно фитятся escape-компоненты SE/DE от сильных пиков.
    ///
    /// Портировано и развито из oracle-режима tools/RjmcmcHarness (итерации
    /// 6 и 8 в tools/RjmcmcTuning/README.md). Формализм: Nagata et al.,
    /// arXiv:1812.05501 (Пуассон, значимость через информацию Фишера);
    /// Okubo et al., arXiv:2605.17518 (внешние ограничения разрывают
    /// спектральное вырождение); Ukita, JPSJ 91 (2022) 064002 (фиксированные
    /// позиции разрушают вырождение вложенных моделей).
    /// </summary>
    public class LibraryPeakFitter
    {
        // Порог значимости Fisher z фитованной амплитуды (критерий
        // RECOVERABLE oracle-режима). Линии слабее порога считаются
        // отсутствующими и пиков не порождают.
        public const double SignificanceZ = 4.0;

        // Допуск совпадения якорной линии с найденным пиком, доли FWHM пика.
        const double AnchorMatchToleranceFwhm = 0.5;

        // Пик «принадлежит» линии (линия уже обнаружена), если центр пика в
        // этой доле FWHM от линии. Меньше исторических 0.5: посторонний пик в
        // 0.3-0.5 FWHM не должен блокировать посадку компонента линии.
        const double ClaimToleranceFwhm = 0.25;

        // Пик считается центроидом бленда группы (и подлежит замене на её
        // линии), если он в этой доле FWHM от какой-либо линии группы.
        const double BlendCoverToleranceFwhm = 0.5;

        // Порог кластеризации линий в bound-группу: предел Sparrow
        // (delta < 2·sigma = 0.85·FWHM — неразрешимо слепым поиском).
        const double SparrowFwhm = 0.85;

        // Минимальная энергия источника для посадки escape-компонент
        // (SE = E-511, DE = E-1022); ниже SE слаб и тонет в континууме.
        const double EscapeSourceMinEnergy = 1200.0;

        // Максимум итераций координатного спуска (как в oracle-режиме).
        const int FitIterations = 300;

        // --- Гейт значимости: сравнение моделей вместо теста амплитуды ---
        //
        // Fisher z проверяет «амплитуда значимо отлична от нуля», а не «здесь есть
        // пик»: компонента на пустом месте забирает систематический положительный
        // остаток, и при миллионах отсчётов z уходит в сотни. На сетах-обманках
        // (якорь настоящий, остальные линии сдвинуты на пустые энергии) фит принимал
        // 63-79 % несуществующих линий, одинаково на девяти спектрах, трёх детекторах
        // и статистике от 0.43 М до 61 М; распределения z у фантомов и настоящих линий
        // совпадают, подъём порога режет тех и других поровну, а улучшение модели фона
        // проверено прямым экспериментом и не помогает. Подробности —
        // tools/LibraryFitLab/README.md, раздел «Результат 2».
        //
        // Правильный вопрос — не «велика ли амплитуда», а «становится ли модель лучше,
        // если эту компоненту оставить». Отсюда тест отношения правдоподобий: ΔD между
        // моделью с компонентой и без неё. Ключевая часть — ПЕРЕФИТ СОСЕДЕЙ при
        // выключении: без него отсчётам компоненты некуда деться, ΔD раздувается и тест
        // вырождается обратно в тест амплитуды. Именно поэтому фантом и проходил:
        // рядом всегда есть чему его подобрать, но амплитудный критерий этого не видит.
        //
        // Порог. При H0 (амплитуда = 0) ΔD асимптотически хи-квадрат с одной степенью
        // свободы, но параметр лежит на границе области (амплитуда неотрицательна),
        // поэтому нулевое распределение — смесь 50:50 из chi2(0) и chi2(1), и
        // односторонний уровень значимости z соответствует порогу z^2. Это и есть
        // «поправка Уилкса» из плана в README; при z = 4 получается 16.
        public const double SignificanceDeltaDeviance = SignificanceZ * SignificanceZ;

        // Сколько проходов координатного спуска отводится на локальный перефит соседей
        // при выключенной компоненте. Окно узкое (носитель одной компоненты), соседей
        // единицы, и спуск сходится за десяток проходов; 300 как в основном фите здесь
        // не нужны и стоили бы дорого - перефит делается на каждого кандидата.
        const int ProfileFitIterations = 40;

        // Переключатель на время сравнения критериев: false - прежний гейт по Fisher z.
        // Держится явным, чтобы прогон tools/LibraryFitLab мог померить оба на одних и
        // тех же спектрах и сетах-обманках.
        // Умолчание — ВЫКЛЮЧЕН. Измерено на корпусе: ΔD и тест устойчивости
        // отбирают линии по одной, и чем строже они отсеивают, тем меньше точек
        // остаётся вету по согласованности набора, которому нужно минимум
        // четыре. В связке «ΔD + shape + вето» фантомов оказывается 9.6 % против
        // 5.1 % у «z + вето» при том же recall: пофайловые критерии голодом
        // выключают тот, что работает лучше их обоих.
        public static bool UseDevianceGate = false;

        // --- Гейт устойчивости к модели фона ---
        //
        // ΔD снял 17-28 п.п. фантомов ценой 9-15 п.п. recall, но половина
        // несуществующих линий всё ещё проходит. Причина у обоих гейтов общая: и
        // амплитуда, и прирост правдоподобия считаются ОТНОСИТЕЛЬНО ОДНОЙ И ТОЙ ЖЕ
        // подложки - огибающей SNIP и фона прибора. Если структура появилась из-за
        // того, КАК проведён континуум, оба теста её подтвердят: модель без
        // компоненты действительно хуже описывает данные, потому что подложка в этом
        // месте занижена.
        //
        // Здесь задаётся другой вопрос: переживёт ли линия смену модели фона. Чистая
        // площадь гауссианы известной ширины меряется дважды - над ЛИНЕЙНОЙ и над
        // КВАДРАТИЧНОЙ подложкой, подогнанной только по крыльям окна и никак не
        // связанной ни со SNIP, ни с фоном прибора. Амплитуда в обеих линейна, так
        // что оценка всегда возвращает число с честной пуассоновской ошибкой, а не
        // упирается в границу. Настоящий фотопик положителен и значим при обеих
        // подложках: это избыток, а не то, как нарисован континуум. Искривление
        // континуума при смене порядка меняет знак.
        //
        // Рабочая точка взята из измерения на тех же девяти спектрах
        // (tools/LibraryFitLab/scripts/verify_phantom.py): тест отсеивает 84-96 %
        // фантомов, сохраняя 53-92 % настоящих линий - заметно лучше, чем у ΔD.
        // ΔD остаётся дешёвым предварительным отсевом: он считается на носителе
        // компоненты и не требует отдельного прохода по спектру.
        // Умолчание — ВЫКЛЮЧЕН, по той же причине, что и ΔD (см. выше).
        public static bool UseBackgroundShapeGate = false;

        // Порог значимости чистой площади при КАЖДОЙ из двух подложек.
        // Не const: свип tools/LibraryFitLab гоняет по нему рабочую точку.
        public static double BackgroundShapeZ = 3.0;

        // Полуокно измерения и граница крыльев, в сигмах. Не const: геометрия окна
        // и есть главный параметр теста, свип гоняет по ней рабочую точку. Чем
        // шире окно, тем меньше у квадратичной подложки возможности повторить
        // сам пик, но тем больше шансов зацепить соседнюю линию.
        //
        // 4.5/2.5 — лучшая точка измерения на корпусе из 46 спектров. Исходные
        // 2.6/1.5 (геометрия офлайновой проверки) дают на 4 п.п. меньше recall
        // при том же соотношении ложных к настоящим: на узком окне квадратика
        // успевает частично повторить сам пик и съедает настоящие линии.
        public static double ShapeWindowSigma = 4.5;
        public static double ShapeFlankSigma = 2.5;

        // Максимальный порядок подложки. 2 — исходная пара «линейная и
        // квадратичная»; 1 оставляет только линейную и показывает, сколько стоит
        // именно квадратичная.
        public static int ShapeMaxOrder = 2;

        // --- Вето по согласованности набора (кривая относительной эффективности) ---
        //
        // Всё, что стояло до сих пор, судит компоненту ПООДИНОЧКЕ: велика ли
        // амплитуда, лучше ли с ней модель, переживёт ли линия смену подложки. Ни
        // один из этих вопросов не спрашивает главного - согласуются ли принятые
        // линии МЕЖДУ СОБОЙ. А у настоящей цепочки в вековом равновесии все линии
        // делят одну активность, и потому площадь каждой обязана равняться
        // A * I(E) * eps(E): точки S/I ложатся на одну гладкую кривую
        // эффективности. Форма eps(E) заранее не нужна - она фитится вместе с
        // активностью, важна только гладкость (тот же приём, что в изотопном
        // анализе: Рейлли, гл. 8, RE(E) ~ C(E)/BR).
        //
        // У сета-обманки интенсивности табличные, а площади набраны из того, что
        // случайно оказалось на сдвинутой энергии. Лечь на общую кривую они не
        // обязаны, и не ложатся.
        //
        // Измерено на корпусе до реализации (scripts/re_curve_check.py): дробный
        // разброс вокруг кривой у настоящих цепочек - медиана 74 %, у обманок -
        // 156 %. При пороге 100 % проходят 76 % настоящих наборов и 16 % обманок.
        //
        // Почему порог так велик. Даже сильные одиночные линии настоящей цепочки
        // расходятся с гладкой кривой на 24 % (медиана; худший случай 58 %):
        // каскадное суммирование, интерференция, погрешности табличных
        // интенсивностей. Это систематический пол, ниже которого порог опускать
        // нельзя - иначе критерий начнёт отвергать настоящие цепочки.
        //
        // Решение принимается на НАБОР целиком: не согласуется - снимаются все
        // библиотечные линии. Якорный пик найден финдером независимо и остаётся,
        // то есть при срабатывании вето recall откатывается к базе финдера.
        public static bool UseChainConsistencyVeto = true;

        // Предельный дробный разброс вокруг кривой. 1.25 — рабочая точка,
        // измеренная на корпусе: тот же recall, что у прежнего «ΔD + shape»
        // (59.4 %), при 5.7 % фантомов против 24.7 %.
        public static double ChainScatterLimit = 1.25;

        // Меньше этого числа линий - кривую не построить (порядок 1 требует двух
        // параметров, нужна хотя бы пара степеней свободы). Судить не о чем, и
        // отсутствие суждения не улика: набор пропускается.
        // Сколько линий нужно вету, чтобы его вердикту можно было верить.
        // Было const 4 — минимум, при котором вообще строится кривая. Замеры
        // показали, что «строится» и «надёжен» это разные вещи: на германии с
        // оборванным рядом U-238 кривая строится ровно по четырём точкам, вето
        // объявляет НАСТОЯЩИЙ набор несогласованным и откатывает результат к
        // базе финдера. Ниже этого числа вето воздерживается, и решение
        // принимает запасной критерий по линии.
        //
        // Умолчание 6, а не 4: замеры по корпусу (69 спектров) дали при нём
        // 63.5 % recall и 8.6 % фантомов против 64.0 / 9.7 при четырёх, то есть
        // на четырёх-пяти точках вердикт вета уже шум, и передать решение
        // запасному критерию выгоднее.
        public static int ChainConsistencyMinLines = 6;

        // Запасной критерий на случай, когда вето по набору судить не может.
        // Замеры на корпусе (69 спектров) показали, что вето и тест
        // устойчивости к фону дополняют друг друга ВДОЛЬ ОСИ РАЗРЕШЕНИЯ, а не
        // конкурируют: на сцинтилляторах вето сильнее (G1S 94.7 % при 6.5 %
        // фантомов против 84.0 / 13.1 у shape), а на германии, где цепочка даёт
        // мало разрешённых одиночных линий, вето снимает НАСТОЯЩИЙ набор и не
        // добавляет ничего сверх финдера (HPGE 28.2 % при нуле фантомов), тогда
        // как shape поднимает recall до 38.5 % ценой 3.7 %.
        //
        // Поэтому shape включается не ВМЕСТО вето и не ВМЕСТЕ с ним (глобально
        // связка хуже: 10.4 % фантомов против 6.4 % при том же recall - shape
        // отбирает у вето точки для кривой), а ТОЛЬКО там, где вето
        // воздержалось или сняло набор целиком.
        public static bool UseChainVetoFallback = true;

        // Второе вето по набору: доля линий, которые кривая эффективности
        // предсказывает УВЕРЕННО ВИДИМЫМИ, а фит их не принял.
        //
        // Прежнее вето спрашивает только про принятые линии — легли ли они на
        // общую кривую. Про отсутствующие не спрашивает никто, а это половина
        // доступной информации. У настоящей цепочки в равновесии отсутствие
        // информативнее присутствия: если 2614 кэВ есть с площадью A, то 583
        // обязана быть с предсказуемой площадью. У набора-обманки линии смещены
        // на пустые энергии, фит принимает те, что случайно сели на структуру, а
        // остальные проваливаются — и сейчас эти провалы игнорируются.
        //
        // Замерено офлайн по полному корпусу (scripts/absence_check.py), 61
        // настоящий набор против 51 обманки. При одинаковой доле пропущенных
        // настоящих наборов связка двух вето бьёт каждое поодиночке на всех
        // рабочих точках: при 70 % настоящих — 13.7 % обманок против 23.5 % у
        // одного разброса, при 90 % — 49.0 % против 72.5 %.
        public static bool UseAbsenceVeto = true;

        // Во сколько сигм предсказание должно превышать ноль, чтобы отсутствие
        // линии считалось уликой. Это критический уровень по Currie, только
        // поставленный обратной стороной: не «видна ли линия», а «должна ли она
        // была быть видна». Пять меряется заметно лучше двух и трёх.
        public static double AbsenceVisibleSigma = 5.0;

        // Порог доли необъяснённых пропусков. Офлайновый расчёт давал оптимум
        // около 0.6, но в фиттере эффект слабее: модель видит не все линии сета,
        // а континуум у неё свой (SNIP плюс фон прибора), не локальный полином.
        // Скан по корпусу: 0.60 не даёт ничего, 0.45 -> 7.4 %, 0.35 -> 7.1 %,
        // 0.25 -> те же 7.1 % ценой recall. Плато на 0.35, там и стоим.
        public static double AbsenceMissLimit = 0.35;

        // Поимённое исключение выбросов вместо решения «всё или ничего».
        //
        // Оба вета решают на НАБОР: набор из девяти согласованных линий и одного
        // фантома проходит целиком, и остаток фантомов живёт именно там. Достать
        // одну линию ни одно из них не может по построению.
        //
        // Здесь набор, не уложившийся в порог разброса, не снимается сразу:
        // выбрасывается линия с наибольшей невязкой относительно кривой, кривая
        // строится заново, и так пока разброс не уложится. Промышленный образец —
        // PACE у Canberra (ANIMMA 2021): переопределённая система по активностям,
        // восстановленные площади против измеренных, выбросы поимённо.
        //
        // ВЫКЛЮЧЕНО ПО ИТОГАМ ЗАМЕРА. Приём не переносится, и причина общая:
        // разброс вокруг кривой — единственное, что отличает набор от обманки, а
        // процедура, РЕДАКТИРУЮЩАЯ набор ради улучшения этой самой статистики,
        // уничтожает улику, по которой судит. Наивный выброс худшей невязки поднял
        // фантомы с 7.1 до 49.9 %. Условие Граббса (выбрасывать только настоящий
        // выброс, а не просто худший) чинит поведение и интерполирует между 49.9 и
        // 7.1 %, но выигрышной точки нет: при K = 3.5 выходит 65.1 % recall при
        // 9.6 % фантомов, то есть 0.74 ложной на настоящую против 0.59 без выброса.
        //
        // У PACE ожидаемые площади предсказываются по НЕЗАВИСИМО откалиброванной
        // кривой эффективности, а у нас кривая подгоняется по самому набору —
        // отсюда круг. Приём станет применим, если появится независимая кривая
        // (в поставке есть `LSRM Geometries/`); до тех пор переключатель выключен.
        public static bool UseOutlierTrim = false;

        // Сколько линий позволено выбросить, в долях набора. Без ограничения
        // подгонка выродится: из любого набора можно оставить ChainConsistencyMinLines
        // точек, легших на кривую, и объявить его согласованным.
        public static double OutlierTrimMaxFraction = 0.34;

        // Во сколько стандартных отклонений невязка должна превосходить
        // остальные, чтобы линию можно было выбросить.
        //
        // Без этого условия приём не работает вовсе — измерено: наивное
        // «выбросить худшую невязку» подняло долю фантомов с 7.1 до 49.9 %,
        // потому что выброс худшего оптимизирует РОВНО ТУ статистику, по которой
        // судит вето, и согласованным становится любой набор. Ужесточение доли
        // не помогает (0.20 дало 38.4 %).
        //
        // Условие Граббса разводит два случая, которые наивный приём смешивает:
        // у набора-обманки разбросаны ВСЕ точки, ни одна не выделяется, выбросить
        // некого — вето срабатывает; у настоящего набора с одним фантомом тот
        // выделяется и уходит.
        public static double OutlierTrimGrubbsK = 2.5;

        // Вычитать ли из наблюдённого спектра вклад ОСТАЛЬНЫХ компонент модели перед
        // измерением. Офлайновая проверка этого не делала и теряла настоящие линии в
        // блендах: крылья окна ложатся на соседнюю линию, подложка задирается, и
        // чистая площадь уходит в минус. Вычитаются только линии - фиксированный фон
        // (SNIP + фон прибора) НЕ трогается, иначе тест снова окажется относительно
        // той самой подложки, от которой он и должен быть независим.
        public static bool ShapeGateSubtractNeighbours = true;

        public sealed class LibraryCandidate
        {
            // Всегда задан: escape-компоненты (SE/DE) участвуют в модели фита,
            // но кандидатами не становятся — библиотечную пометку получают
            // только линии сета.
            public NuclideDefinition Nuclide;
            public int Channel;
            public double Fwhm;
            public double Amplitude;
            // Площадь = амплитуда * сумма профиля. Профиль нормирован на высоту
            // (PeakShapeModel.RelativeValue), поэтому амплитуда сама по себе -
            // высота, а сравнивать линии разных энергий надо по площади: ширина
            // растёт как sqrt(E), и на 2615 кэВ она вдвое больше, чем на 600.
            public double Area;
            public double Z;
        }

        public sealed class LibraryFitResult
        {
            public List<LibraryCandidate> AddedPeaks = new List<LibraryCandidate>();
            // Пики-центроиды блендов, заменённые линиями bound-группы.
            public List<Peak> ReplacedPeaks = new List<Peak>();
            // Пики, совпавшие с якорными линиями сета (включившие фит).
            public List<Peak> AnchorPeaks = new List<Peak>();
        }

        sealed class FitComponent
        {
            public NuclideDefinition Nuclide;
            public string Label;
            public int Channel;
            public double Fwhm;
            public int Start;
            public double[] Profile;
            public double Amplitude;
            // Члены bound-группы (для развёртки амплитуды по линиям).
            public List<LineSite> GroupMembers;
            public double[] GroupWeights;
            // Профили членов группы, построенные один раз вместе с групповым
            // компонентом: их дважды используют BestMemberZ и финальная
            // развёртка, а BuildFitComponent — не самая дешёвая операция.
            public List<FitComponent> GroupComponents;
        }

        sealed class LineSite
        {
            public NuclideDefinition Nuclide;
            public int Channel;
            public double Fwhm;
            public double Intensity;
            public string Chain;
        }

        public static LibraryFitResult Fit(
            EnergySpectrum spectrum,
            EnergySpectrum backgroundSpectrum,
            Func<int[]> snipContinuumProvider,
            FwhmCalibration fwhmCalibration,
            List<Peak> existingPeaks,
            NuclideSet nuclideSet,
            List<NuclideDefinition> nuclideDefinitions,
            FWHMPeakDetectionMethodConfig peakConfig)
        {
            LibraryFitResult result = new LibraryFitResult();
            if (spectrum?.Spectrum == null || fwhmCalibration == null || nuclideSet == null || peakConfig == null)
            {
                return result;
            }

            // Список приходит снимком от вызывающего (PeakDetector.DetectPeak):
            // живой NuclideDefinitions правится из UI-потока, а фит крутится
            // в Task.Run.
            List<NuclideDefinition> setLines = (nuclideDefinitions ?? NuclideDefinitionManager.GetInstance().NuclideDefinitions)
                .Where(n => n != null && n.Visible && n.Energy > 0.0 && n.Sets != null && n.Sets.Contains(nuclideSet.Id))
                .OrderBy(n => n.Energy)
                .ToList();
            if (setLines.Count == 0 || !setLines.Any(n => n.IsAnchor))
            {
                return result;
            }

            int channels = spectrum.NumberOfChannels;
            int chMin = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Min_Range, maxChannels: channels));
            int chMax = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(peakConfig.Max_Range, maxChannels: channels));
            if (chMax < chMin)
            {
                int swap = chMin;
                chMin = chMax;
                chMax = swap;
            }

            // Гейт: хотя бы одна якорная линия должна совпасть с найденным
            // пиком. Сдвиг калибровки берём с сильнейшего (по SNR) якоря:
            // matched-filter центроид точнее табличной позиции при дрейфе.
            int calibrationShift = 0;
            double bestAnchorSnr = Double.NegativeInfinity;
            Dictionary<Peak, NuclideDefinition> anchorMatches = new Dictionary<Peak, NuclideDefinition>();
            foreach (NuclideDefinition anchorLine in setLines.Where(n => n.IsAnchor))
            {
                int anchorChannel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(anchorLine.Energy, maxChannels: channels));
                foreach (Peak peak in existingPeaks)
                {
                    double tolerance = AnchorMatchToleranceFwhm * Math.Max(1.0, peak.FWHM);
                    if (Math.Abs(peak.Channel - anchorChannel) <= tolerance)
                    {
                        if (!anchorMatches.ContainsKey(peak))
                        {
                            anchorMatches.Add(peak, anchorLine);
                            result.AnchorPeaks.Add(peak);
                        }
                        if (peak.SNR > bestAnchorSnr)
                        {
                            bestAnchorSnr = peak.SNR;
                            calibrationShift = peak.Channel - anchorChannel;
                        }
                    }
                }
            }

            if (anchorMatches.Count == 0)
            {
                return result;
            }

            // SNIP-континуум запрашивается лениво и только здесь, ПОСЛЕ
            // якорных гейтов: у большинства пользователей сет без якорей, и
            // считать полный SNIP на каждый прогон детекции было бы впустую.
            int[] snipContinuum = snipContinuumProvider != null ? snipContinuumProvider() : null;

            int[] observed = spectrum.Spectrum;
            double[] fixedBackground = BuildFixedBackground(spectrum, backgroundSpectrum, snipContinuum);

            // --- Сайты линий сета ---
            List<LineSite> sites = new List<LineSite>();
            foreach (NuclideDefinition line in setLines)
            {
                int channel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(line.Energy, maxChannels: channels)) + calibrationShift;
                if (channel <= chMin || channel >= chMax)
                {
                    continue;
                }

                double fwhm = fwhmCalibration.ChannelToFwhm(channel);
                if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
                {
                    continue;
                }

                sites.Add(new LineSite
                {
                    Nuclide = line,
                    Channel = channel,
                    Fwhm = fwhm,
                    Intensity = line.Intencity,
                    Chain = ChainOf(line)
                });
            }

            if (sites.Count == 0)
            {
                return result;
            }

            // «Заявленные» линии: линия уже обнаружена, если существующий пик
            // стоит в ClaimToleranceFwhm от неё. Такие пики защищены от
            // замены другими группами.
            Dictionary<Peak, LineSite> claims = BuildClaims(existingPeaks, sites);

            // Якорный пик — тоже заявивший: гейт якоря (AnchorMatchToleranceFwhm)
            // ШИРЕ ClaimToleranceFwhm, поэтому пик, включивший весь фит, в
            // claims попадал не всегда, и дедуп дрейфа мог его удалить —
            // вместе с якорной пометкой, выставляемой в AppendLibraryPeaks.
            // Совпадает с claims только у сильнейшего якоря: от него берётся
            // calibrationShift, поэтому его сайт садится ровно на пик, а
            // остальные якоря уезжают на разницу локальных дрейфов.
            foreach (KeyValuePair<Peak, NuclideDefinition> anchorMatch in anchorMatches)
            {
                if (claims.ContainsKey(anchorMatch.Key))
                {
                    continue;
                }

                LineSite anchorSite = sites.FirstOrDefault(s => ReferenceEquals(s.Nuclide, anchorMatch.Value));
                if (anchorSite != null)
                {
                    claims[anchorMatch.Key] = anchorSite;
                }
            }

            HashSet<LineSite> claimedSites = new HashSet<LineSite>(claims.Values);

            // --- Кластеризация в bound-группы (Sparrow + одна цепочка + BR) ---
            List<List<LineSite>> clusters = ClusterSites(sites);
            List<LineSite> singles = new List<LineSite>();
            List<List<LineSite>> groups = new List<List<LineSite>>();
            foreach (List<LineSite> cluster in clusters)
            {
                foreach (IGrouping<string, LineSite> chainGroup in cluster.GroupBy(s => s.Chain))
                {
                    List<LineSite> members = chainGroup.ToList();
                    if (members.Count >= 2 && members.All(m => m.Intensity > 0.0))
                    {
                        groups.Add(members);
                    }
                    else
                    {
                        singles.AddRange(members);
                    }
                }
            }

            // --- Свободные компоненты: существующие пики ---
            Dictionary<Peak, FitComponent> peakComponents = new Dictionary<Peak, FitComponent>();
            foreach (Peak peak in existingPeaks.OrderBy(p => p.Channel))
            {
                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, peak.Channel, null, null);
                if (component != null)
                {
                    peakComponents[peak] = component;
                }
            }

            // --- Одиночные линии: пропустить заявленные, посадить свободные ---
            List<FitComponent> singleComponents = new List<FitComponent>();
            foreach (LineSite site in singles)
            {
                if (claimedSites.Contains(site))
                {
                    continue;
                }

                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, site.Channel, site.Nuclide, null);
                if (component != null)
                {
                    singleComponents.Add(component);
                }
            }

            // --- Escape-сайты SE/DE от сильных найденных пиков ---
            // Источник обязан быть заявлен линией сета: escape от ложного
            // пика — это ложный escape (например, SE от мусорного 2333).
            foreach (Peak sourcePeak in existingPeaks.Where(p => p.Energy >= EscapeSourceMinEnergy && claims.ContainsKey(p)))
            {
                foreach (double escapeOffset in new[] { 511.0, 1022.0 })
                {
                    double escapeEnergy = sourcePeak.Energy - escapeOffset;
                    int escapeChannel = ClampChannel(channels, spectrum.EnergyCalibration.EnergyToChannel(escapeEnergy, maxChannels: channels));
                    if (escapeChannel <= chMin || escapeChannel >= chMax)
                    {
                        continue;
                    }

                    double escapeFwhm = fwhmCalibration.ChannelToFwhm(escapeChannel);
                    if (!PeakShapeModel.IsFinite(escapeFwhm) || escapeFwhm <= 0.0)
                    {
                        continue;
                    }

                    double claimTolerance = ClaimToleranceFwhm * escapeFwhm;
                    bool occupied =
                        existingPeaks.Any(p => Math.Abs(p.Channel - escapeChannel) <= claimTolerance) ||
                        sites.Any(s => Math.Abs(s.Channel - escapeChannel) <= claimTolerance) ||
                        singleComponents.Any(c => c.Label != null && Math.Abs(c.Channel - escapeChannel) <= claimTolerance);
                    if (occupied)
                    {
                        continue;
                    }

                    string label = String.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} {1:F0}",
                        escapeOffset > 600.0 ? "DE" : "SE",
                        sourcePeak.Energy);
                    FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, escapeChannel, null, label);
                    if (component != null)
                    {
                        singleComponents.Add(component);
                    }
                }
            }

            // --- Модель A: пики + одиночные компоненты, без групп ---
            List<FitComponent> model = new List<FitComponent>(peakComponents.Values);
            model.AddRange(singleComponents);
            HashSet<FitComponent> modelSet = new HashSet<FitComponent>(model);
            double[] lambdaCurrent;
            double devianceCurrent = FitModel(observed, fixedBackground, model, chMin, chMax, out lambdaCurrent);

            // --- Последовательное принятие bound-групп по AIC ---
            List<Peak> replacedPeaks = new List<Peak>();
            HashSet<Peak> replacedSet = new HashSet<Peak>();
            // Компоненты фита, идущие в ногу с result.AddedPeaks. Нужны запасному
            // критерию: он проверяет линии тестом устойчивости к модели фона, а
            // тот работает по компоненте. Список ЛОКАЛЬНЫЙ — FitComponent тип
            // приватный, и в публичный LibraryCandidate его не положить.
            List<FitComponent> addedSources = new List<FitComponent>();
            foreach (List<LineSite> group in groups.OrderBy(g => g[0].Channel))
            {
                FitComponent groupComponent = BuildGroupComponent(spectrum, fwhmCalibration, group);
                if (groupComponent == null)
                {
                    continue;
                }

                // Пики-центроиды бленда: близко к линиям группы и не заявлены
                // линией вне группы.
                List<Peak> covering = new List<Peak>();
                HashSet<FitComponent> coveringComponents = new HashSet<FitComponent>();
                foreach (KeyValuePair<Peak, FitComponent> entry in peakComponents)
                {
                    if (!modelSet.Contains(entry.Value))
                    {
                        continue; // уже заменён предыдущей группой
                    }

                    Peak peak = entry.Key;
                    bool nearGroup = group.Any(s => Math.Abs(peak.Channel - s.Channel) <= BlendCoverToleranceFwhm * Math.Max(1.0, Math.Max(peak.FWHM, s.Fwhm)));
                    if (!nearGroup)
                    {
                        continue;
                    }

                    if (claims.TryGetValue(peak, out LineSite claimedBy) && !group.Contains(claimedBy))
                    {
                        continue; // пик принадлежит линии вне группы — защищён
                    }

                    covering.Add(peak);
                    coveringComponents.Add(entry.Value);
                }

                List<FitComponent> trial = model.Where(c => !coveringComponents.Contains(c)).ToList();
                trial.Add(groupComponent);

                double[] lambdaTrial;
                double devianceTrial = FitModel(observed, fixedBackground, trial, chMin, chMax, out lambdaTrial);

                // AIC: D + 2k; в trial добавлен 1 параметр, убрано covering.Count.
                double aicCurrent = devianceCurrent + 2.0 * model.Count;
                double aicTrial = devianceTrial + 2.0 * trial.Count;
                if (aicTrial >= aicCurrent)
                {
                    continue;
                }

                double bestMemberZ = BestMemberZ(groupComponent, lambdaTrial);
                if (bestMemberZ < SignificanceZ)
                {
                    continue;
                }

                model = trial;
                modelSet = new HashSet<FitComponent>(trial);
                devianceCurrent = devianceTrial;
                foreach (Peak covered in covering)
                {
                    if (replacedSet.Add(covered))
                    {
                        replacedPeaks.Add(covered);
                    }
                }
            }

            // --- Финальный отбор ---
            // Рефит принятой модели: FitModel(trial) мутирует амплитуды
            // компонентов, разделяемых trial и model, поэтому после
            // ОТКЛОНЁННОЙ пробы амплитуды в model остаются от фита с чужим
            // групповым компонентом (занижены там, где он перекрывал линии).
            // Скалярные AIC-сравнения это не ломало, а финальные z — ломало.
            FitModel(observed, fixedBackground, model, chMin, chMax, out lambdaCurrent);
            foreach (FitComponent component in model)
            {
                if (component.GroupMembers != null)
                {
                    // Развёртка bound-группы по линиям: амплитуда доли w_i,
                    // z по собственному профилю линии.
                    for (int i = 0; i < component.GroupMembers.Count; i++)
                    {
                        LineSite member = component.GroupMembers[i];
                        double memberAmplitude = component.Amplitude * component.GroupWeights[i];
                        FitComponent memberComponent = component.GroupComponents[i];
                        memberComponent.Amplitude = memberAmplitude;
                        double z = FisherZ(memberComponent, lambdaCurrent);
                        if (!Significant(observed, model, memberComponent, lambdaCurrent,
                                         chMin, chMax, z))
                        {
                            continue;
                        }

                        result.AddedPeaks.Add(new LibraryCandidate
                        {
                            Nuclide = member.Nuclide,
                            Channel = member.Channel,
                            Fwhm = member.Fwhm,
                            Amplitude = memberAmplitude,
                            Area = memberAmplitude * ProfileSum(memberComponent),
                            Z = z
                        });
                        addedSources.Add(memberComponent);
                    }
                }
                else if (component.Nuclide != null)
                {
                    // Одиночная линия сета. Escape-компоненты (Nuclide == null)
                    // НЕ выводятся как пики: они остаются только в модели
                    // фита, чтобы амплитуда SE/DE не перетекала в соседние
                    // линии. Библиотечную пометку получают ТОЛЬКО линии сета.
                    double z = FisherZ(component, lambdaCurrent);
                    if (!Significant(observed, model, component, lambdaCurrent,
                                     chMin, chMax, z))
                    {
                        continue;
                    }

                    result.AddedPeaks.Add(new LibraryCandidate
                    {
                        Nuclide = component.Nuclide,
                        Channel = component.Channel,
                        Fwhm = component.Fwhm,
                        Amplitude = component.Amplitude,
                        Area = component.Amplitude * ProfileSum(component),
                        Z = z
                    });
                    addedSources.Add(component);
                }
            }

            // --- Вето по согласованности набора ---
            ChainVerdict verdict = UseChainConsistencyVeto
                ? ChainConsistent(result.AddedPeaks)
                : ChainVerdict.Consistent;

            // Вето воздержалось: точек на кривую не хватило. Прежде в этом
            // случае не судил никто - пофайловые критерии в production
            // выключены, и на коротких наборах защиты не было вовсе. Здесь
            // включается тест устойчивости к модели фона, поимённо.
            if (UseChainConsistencyVeto && UseChainVetoFallback &&
                verdict == ChainVerdict.Abstained)
            {
                result.AddedPeaks = ShapeFilter(result.AddedPeaks, addedSources,
                                                observed, model, chMin, chMax);
            }

            // Ветки Inconsistent запасной критерий НЕ трогает, и это измерено.
            // Когда он подменял собой снятие несогласованного набора, доля
            // фантомов на сцинтилляторах росла впятеро при неизменном recall
            // (G1S 33.9 % против 6.5 %): вето убивает набор-обманку ЦЕЛИКОМ, а
            // тест устойчивости пропускает треть его линий — сам по себе он
            // опускает фантомы только до 28.9 %. Сила вето именно в решении на
            // набор, и подменять его решением по линии нельзя.

            // Второе вето: набор молчит там, где по собственной же кривой
            // обязан был говорить. Считается ДО снятия набора первым вето и
            // только когда то высказалось — иначе кривой нет.
            if (UseAbsenceVeto && verdict == ChainVerdict.Consistent)
            {
                double missed = UnexplainedAbsence(result.AddedPeaks, model, lambdaCurrent);
                if (missed >= 0.0 && missed > AbsenceMissLimit)
                {
                    verdict = ChainVerdict.Inconsistent;
                }
            }

            // Набор не уложился в порог — прежде чем снимать его целиком,
            // попробовать выбросить виновные линии поимённо.
            if (UseChainConsistencyVeto && UseOutlierTrim &&
                verdict == ChainVerdict.Inconsistent)
            {
                List<LibraryCandidate> trimmed = TrimToConsistent(result.AddedPeaks);
                if (trimmed != null && trimmed.Count > 0)
                {
                    result.AddedPeaks = trimmed;
                    verdict = ChainVerdict.Consistent;
                }
            }

            if (UseChainConsistencyVeto && verdict == ChainVerdict.Inconsistent)
            {
                // Набор не согласуется сам с собой: линии, которые он предъявил,
                // не ложатся на общую кривую эффективности. Снимаем весь
                // библиотечный вклад - решение принимается на набор, а не на
                // линию, потому что несогласованность и есть свойство набора.
                //
                // Вместе с добавленными линиями отменяется и ЗАМЕНА пиков
                // финдера линиями bound-групп. Иначе фит уносит с собой центроид
                // бленда, ничего не давая взамен, и recall проваливается НИЖЕ
                // базы финдера - ровно это и наблюдалось на германии
                // (23.1 % против 28.2 % у финдера), пока замена не отменялась.
                result.AddedPeaks.Clear();
                // Чистится ЛОКАЛЬНЫЙ список: result.ReplacedPeaks присваивается
                // из него ниже, и очистка самого поля здесь была бы затёрта.
                replacedPeaks.Clear();
                replacedSet.Clear();
            }

            // --- Дедуп дрейфа: незаявленный существующий пик в 0.5·FWHM от
            // принятой библиотечной компоненты — это либо та же линия со
            // сдвинутым центроидом (дрейф калибровки, K-40 1482→1461), либо
            // артефакт на её склоне. Табличная позиция точнее — пик уходит.
            // Заявленные и якорные пики защищены: якорный пик мог не попасть в
            // claims, если его линия вышла за [chMin, chMax] и сайта не
            // получила, а удалять пик, включивший весь фит, нельзя.
            foreach (LibraryCandidate candidate in result.AddedPeaks)
            {
                foreach (Peak peak in existingPeaks)
                {
                    if (replacedSet.Contains(peak) || claims.ContainsKey(peak) || anchorMatches.ContainsKey(peak))
                    {
                        continue;
                    }

                    double tolerance = BlendCoverToleranceFwhm * Math.Max(1.0, Math.Max(peak.FWHM, candidate.Fwhm));
                    if (Math.Abs(peak.Channel - candidate.Channel) <= tolerance && replacedSet.Add(peak))
                    {
                        replacedPeaks.Add(peak);
                    }
                }
            }

            result.ReplacedPeaks = replacedPeaks;
            return result;
        }

        // Идентификатор цепочки для векового равновесия: текст в последних
        // скобках имени («Bi-214 (Ra-226)» → «Ra-226»), иначе имя целиком.
        // BR-связка допустима только внутри одной цепочки.
        static string ChainOf(NuclideDefinition nuclide)
        {
            string name = nuclide.Name ?? "";
            int open = name.LastIndexOf('(');
            int close = name.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                return name.Substring(open + 1, close - open - 1).Trim();
            }
            return name.Trim();
        }

        static Dictionary<Peak, LineSite> BuildClaims(List<Peak> peaks, List<LineSite> sites)
        {
            Dictionary<Peak, LineSite> claims = new Dictionary<Peak, LineSite>();
            foreach (Peak peak in peaks)
            {
                LineSite best = null;
                double bestDistance = Double.MaxValue;
                foreach (LineSite site in sites)
                {
                    double distance = Math.Abs(peak.Channel - site.Channel);
                    double tolerance = ClaimToleranceFwhm * Math.Max(1.0, Math.Max(peak.FWHM, site.Fwhm));
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        best = site;
                        bestDistance = distance;
                    }
                }

                if (best != null)
                {
                    claims[peak] = best;
                }
            }

            return claims;
        }

        static List<List<LineSite>> ClusterSites(List<LineSite> sites)
        {
            List<List<LineSite>> clusters = new List<List<LineSite>>();
            List<LineSite> current = null;
            LineSite previous = null;
            foreach (LineSite site in sites.OrderBy(s => s.Channel))
            {
                if (previous != null &&
                    Math.Abs(site.Channel - previous.Channel) <= SparrowFwhm * Math.Max(site.Fwhm, previous.Fwhm))
                {
                    current.Add(site);
                }
                else
                {
                    current = new List<LineSite> { site };
                    clusters.Add(current);
                }

                previous = site;
            }

            return clusters;
        }

        static int ClampChannel(int channels, double value)
        {
            return Math.Max(0, Math.Min(channels - 1, Convert.ToInt32(Math.Round(value))));
        }

        // Фиксированный фон = огибающая max(SASNIP-континуум, масштабированный
        // по времени фон прибора) — тот же рецепт, что в RJMCMC-деконволюции
        // (ExtractFixedBackground) и oracle-режиме харнесса.
        static double[] BuildFixedBackground(EnergySpectrum foreground, EnergySpectrum background, int[] snip)
        {
            int channels = foreground.NumberOfChannels;
            double[] fixedBackground = new double[channels];
            double scale = background != null && background.MeasurementTime > 0.0 && foreground.MeasurementTime > 0.0
                ? foreground.MeasurementTime / background.MeasurementTime
                : 0.0;
            if (!PeakShapeModel.IsFinite(scale) || scale < 0.0)
            {
                scale = 0.0;
            }

            // Без обеих калибровок канал фона не сопоставить — фон прибора
            // просто не подмешивается (как в RJMCMC-аналоге
            // ApplyScaledInstrumentBackground, который в этом случае выходит).
            if (scale > 0.0 && (foreground.EnergyCalibration == null || background.EnergyCalibration == null))
            {
                scale = 0.0;
            }

            bool sameCalibration = scale > 0.0 && SameCalibration(foreground.EnergyCalibration, background.EnergyCalibration);
            for (int i = 0; i < channels; i++)
            {
                double value = snip != null && i < snip.Length ? Math.Max(0.0, snip[i]) : 0.0;
                if (scale > 0.0)
                {
                    int backgroundChannel = i;
                    if (!sameCalibration)
                    {
                        double energy = foreground.EnergyCalibration.ChannelToEnergy(i);
                        backgroundChannel = Convert.ToInt32(background.EnergyCalibration.EnergyToChannel(energy, maxChannels: background.NumberOfChannels));
                    }

                    if (backgroundChannel >= 0 && backgroundChannel < background.NumberOfChannels)
                    {
                        value = Math.Max(value, scale * background.Spectrum[backgroundChannel]);
                    }
                }

                fixedBackground[i] = value;
            }

            return fixedBackground;
        }

        // Безопасное сравнение калибровок. EnergyCalibration.Equals у
        // наследников кастует аргумент БЕЗ проверки типа и сразу его
        // разыменовывает (NRE на null, InvalidCastException на чужом типе), а
        // NonlinearEnergyCalibration бросает NotImplementedException. Отсюда
        // исключение убило бы всю детекцию: DetectPeak вызывается под
        // catch-all в DCPeakDetectionView, который лишь пишет в Trace.
        static bool SameCalibration(EnergyCalibration left, EnergyCalibration right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.GetType() != right.GetType())
            {
                return false;
            }

            return left.Equals(right);
        }

        static FitComponent BuildFitComponent(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration, int channel, NuclideDefinition nuclide, string label)
        {
            double fwhm = fwhmCalibration.ChannelToFwhm(channel);
            if (!PeakShapeModel.IsFinite(fwhm) || fwhm <= 0.0)
            {
                return null;
            }

            double left = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
            double right = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
            if (!PeakShapeModel.IsFinite(left) || !PeakShapeModel.IsFinite(right))
            {
                return null;
            }

            int start = Math.Max(0, channel - Convert.ToInt32(Math.Ceiling(left)));
            int end = Math.Min(spectrum.NumberOfChannels - 1, channel + Convert.ToInt32(Math.Ceiling(right)));
            if (start > end)
            {
                return null;
            }

            double[] profile = new double[end - start + 1];
            for (int ch = start; ch <= end; ch++)
            {
                profile[ch - start] = PeakShapeModel.RelativeValue(ch - channel, fwhm, fwhmCalibration);
            }

            return new FitComponent
            {
                Nuclide = nuclide,
                Label = label,
                Channel = channel,
                Fwhm = fwhm,
                Start = start,
                Profile = profile,
                Amplitude = 0.0
            };
        }

        // Композитный компонент bound-группы: profile = Σ w_i·profile_i,
        // w_i = I_i/ΣI (амплитуда группы = суммарная площадь линий).
        static FitComponent BuildGroupComponent(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration, List<LineSite> members)
        {
            List<FitComponent> memberComponents = new List<FitComponent>();
            foreach (LineSite member in members)
            {
                FitComponent component = BuildFitComponent(spectrum, fwhmCalibration, member.Channel, member.Nuclide, null);
                if (component == null)
                {
                    return null;
                }

                memberComponents.Add(component);
            }

            double intensitySum = members.Sum(m => m.Intensity);
            if (intensitySum <= 0.0)
            {
                return null;
            }

            double[] weights = members.Select(m => m.Intensity / intensitySum).ToArray();
            int start = memberComponents.Min(c => c.Start);
            int end = memberComponents.Max(c => c.Start + c.Profile.Length - 1);
            double[] profile = new double[end - start + 1];
            for (int i = 0; i < memberComponents.Count; i++)
            {
                FitComponent component = memberComponents[i];
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    profile[component.Start + j - start] += weights[i] * component.Profile[j];
                }
            }

            // Центр группы — линия с максимальным весом (для диагностики).
            int strongestIndex = Array.IndexOf(weights, weights.Max());
            return new FitComponent
            {
                Nuclide = members[strongestIndex].Nuclide,
                Channel = members[strongestIndex].Channel,
                Fwhm = members[strongestIndex].Fwhm,
                Start = start,
                Profile = profile,
                Amplitude = 0.0,
                GroupMembers = members,
                GroupWeights = weights,
                GroupComponents = memberComponents
            };
        }

        // Максимальный z по членам группы (для критерия принятия). Профили
        // членов уже построены в BuildGroupComponent — переиспользуем их, а не
        // строим заново на каждую пробу.
        static double BestMemberZ(FitComponent groupComponent, double[] lambda)
        {
            double best = 0.0;
            for (int i = 0; i < groupComponent.GroupComponents.Count; i++)
            {
                FitComponent memberComponent = groupComponent.GroupComponents[i];
                memberComponent.Amplitude = groupComponent.Amplitude * groupComponent.GroupWeights[i];
                double z = FisherZ(memberComponent, lambda);
                if (z > best)
                {
                    best = z;
                }
            }

            return best;
        }

        static double[] BuildLambda(double[] fixedBackground, List<FitComponent> components, int channels)
        {
            double[] lambda = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                lambda[i] = Math.Max(1E-6, fixedBackground[i]);
            }

            foreach (FitComponent component in components)
            {
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    lambda[component.Start + j] += component.Amplitude * component.Profile[j];
                }
            }

            return lambda;
        }

        static double PoissonDeviance(int[] observed, double[] lambda, int chMin, int chMax)
        {
            double deviance = 0.0;
            for (int i = chMin; i <= chMax; i++)
            {
                double mu = Math.Max(1E-9, lambda[i]);
                int k = observed[i];
                deviance += k > 0
                    ? 2.0 * (k * Math.Log(k / mu) - (k - mu))
                    : 2.0 * mu;
            }

            return deviance;
        }

        static double LocalLogLikelihoodDelta(int[] observed, double[] lambda, FitComponent component, double amplitudeDelta, int chMin, int chMax)
        {
            double delta = 0.0;
            for (int j = 0; j < component.Profile.Length; j++)
            {
                int ch = component.Start + j;
                if (ch < chMin || ch > chMax)
                {
                    continue;
                }

                double p = component.Profile[j];
                if (p <= 0.0)
                {
                    continue;
                }

                double mu = lambda[ch];
                double muNew = mu + amplitudeDelta * p;
                if (muNew <= 1E-9)
                {
                    return Double.NegativeInfinity;
                }

                delta += observed[ch] > 0
                    ? observed[ch] * Math.Log(muNew / mu) - (muNew - mu)
                    : -(muNew - mu);
            }

            return delta;
        }

        static void ApplyAmplitudeDelta(double[] lambda, FitComponent component, double amplitudeDelta)
        {
            for (int j = 0; j < component.Profile.Length; j++)
            {
                lambda[component.Start + j] += amplitudeDelta * component.Profile[j];
            }

            component.Amplitude += amplitudeDelta;
        }

        // Полный фит модели «с нуля» (амплитуды сбрасываются) — используется
        // и для базовой модели, и для AIC-проб bound-групп. lambda отдаётся
        // наружу: спуск и так держит её актуальной, а вызывающему она нужна
        // для FisherZ — собирать её повторно через BuildLambda было лишним
        // полным проходом по спектру на каждую пробу.
        static double FitModel(int[] observed, double[] fixedBackground, List<FitComponent> components, int chMin, int chMax, out double[] lambda)
        {
            foreach (FitComponent component in components)
            {
                component.Amplitude = 0.0;
            }

            return FitAmplitudes(observed, fixedBackground, components, chMin, chMax, FitIterations, out lambda);
        }

        // Координатный Пуассон-спуск с matched-инициализацией (oracle-режим).
        static double FitAmplitudes(int[] observed, double[] fixedBackground, List<FitComponent> components, int chMin, int chMax, int iterations, out double[] lambdaResult)
        {
            double[] lambda = BuildLambda(fixedBackground, components, fixedBackground.Length);
            lambdaResult = lambda;

            foreach (FitComponent component in components)
            {
                double numerator = 0.0;
                double denominator = 0.0;
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    int ch = component.Start + j;
                    double p = component.Profile[j];
                    numerator += (observed[ch] - lambda[ch]) * p;
                    denominator += p * p;
                }

                double initial = denominator > 0.0 ? Math.Max(0.0, numerator / denominator) : 0.0;
                if (initial > 0.0)
                {
                    ApplyAmplitudeDelta(lambda, component, initial);
                }
            }

            double stepScale = 1.0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool improved = false;
                foreach (FitComponent component in components)
                {
                    double step = Math.Max(0.5, component.Amplitude * 0.10) * stepScale;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        double delta = direction * step;
                        if (component.Amplitude + delta < 0.0)
                        {
                            delta = -component.Amplitude;
                            if (delta == 0.0)
                            {
                                continue;
                            }
                        }

                        double gain = LocalLogLikelihoodDelta(observed, lambda, component, delta, chMin, chMax);
                        if (gain > 1E-9)
                        {
                            ApplyAmplitudeDelta(lambda, component, delta);
                            improved = true;
                            break;
                        }
                    }
                }

                if (improved)
                {
                    stepScale = Math.Min(1.0, stepScale * 1.25);
                }
                else
                {
                    stepScale *= 0.5;
                    if (stepScale < 1E-5)
                    {
                        break;
                    }
                }
            }

            return PoissonDeviance(observed, lambda, chMin, chMax);
        }

        // Проходит ли компонента гейт значимости.
        static bool Significant(int[] observed, List<FitComponent> model, FitComponent component,
                                double[] lambda, int chMin, int chMax, double z)
        {
            if (UseDevianceGate &&
                DevianceGain(observed, model, component, lambda, chMin, chMax)
                    < SignificanceDeltaDeviance)
            {
                return false;
            }
            if (!UseDevianceGate && !UseBackgroundShapeGate && z < SignificanceZ)
            {
                return false;
            }
            return !UseBackgroundShapeGate ||
                   SurvivesBackgroundChange(observed, model, component, chMin, chMax);
        }

        // Положительна и значима ли чистая площадь компоненты при ДВУХ разных
        // подложках, проведённых по крыльям окна.
        static bool SurvivesBackgroundChange(int[] observed, List<FitComponent> model,
                                             FitComponent component, int chMin, int chMax)
        {
            double sigma = component.Fwhm / PseudoVoigtProfile.FwhmToSigma;
            if (!PeakShapeModel.IsFinite(sigma) || sigma <= 0.0)
            {
                return true;                 // ширины нет - проверять нечем
            }

            int half = (int)Math.Round(ShapeWindowSigma * sigma);
            // На 1024 каналах ниже ~120 кэВ сигма — пара каналов, и крыльев не
            // хватает даже на квадратичную подложку. Раньше тест в этом месте
            // просто не мог быть посчитан; окно расширяется до минимума, при
            // котором в крыльях остаётся хотя бы по три канала с каждой стороны.
            half = Math.Max(half, (int)Math.Ceiling(ShapeFlankSigma * sigma) + 3);
            int lo = Math.Max(chMin, component.Channel - half);
            int hi = Math.Min(chMax, component.Channel + half);
            if (hi - lo < 8)
            {
                return true;                 // окна не хватает даже на подложку
            }

            double[] y = new double[hi - lo + 1];
            for (int i = 0; i < y.Length; i++)
            {
                y[i] = observed[lo + i];
            }

            if (ShapeGateSubtractNeighbours)
            {
                foreach (FitComponent other in model)
                {
                    if (ReferenceEquals(other, component) || other.Amplitude <= 0.0)
                    {
                        continue;
                    }

                    // Компонента bound-группы, к которой принадлежит проверяемая
                    // линия, в model лежит как ОДИН объект с суммарным профилем -
                    // включая долю самой линии. Вычесть его целиком значит стереть
                    // проверяемый пик и гарантированно завалить тест: у всех линий
                    // в блендах чистая площадь уходила в ноль. Вычитаются соседи
                    // по группе поимённо, сама линия - нет.
                    if (other.GroupComponents != null && other.GroupComponents.Contains(component))
                    {
                        for (int i = 0; i < other.GroupComponents.Count; i++)
                        {
                            FitComponent sibling = other.GroupComponents[i];
                            if (ReferenceEquals(sibling, component))
                            {
                                continue;
                            }
                            SubtractInto(y, lo, hi, sibling, other.Amplitude * other.GroupWeights[i]);
                        }
                        continue;
                    }

                    SubtractInto(y, lo, hi, other, other.Amplitude);
                }
            }

            for (int order = 1; order <= ShapeMaxOrder; order++)
            {
                double z = LocalShapeZ(y, lo, component.Channel, sigma, order);
                if (double.IsNaN(z))
                {
                    continue;                // посчитать не удалось - это не улика
                }
                if (z < BackgroundShapeZ)
                {
                    return false;
                }
            }
            return true;
        }

        static void SubtractInto(double[] y, int lo, int hi, FitComponent source, double amplitude)
        {
            if (amplitude <= 0.0)
            {
                return;
            }
            for (int j = 0; j < source.Profile.Length; j++)
            {
                int channel = source.Start + j;
                if (channel >= lo && channel <= hi)
                {
                    y[channel - lo] -= amplitude * source.Profile[j];
                }
            }
        }

        // z чистой площади гауссианы фиксированной ширины над подложкой порядка
        // `order`, подогнанной по крыльям окна методом наименьших квадратов.
        // Амплитуда линейна, поэтому оценка всегда определена и может быть
        // отрицательной - в отличие от координатного спуска, зажатого нулём снизу.
        static double LocalShapeZ(double[] y, int offset, double center, double sigma, int order)
        {
            int n = y.Length;
            double[] g = new double[n];
            bool[] flank = new bool[n];
            int flankCount = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (offset + i - center) / sigma;
                g[i] = Math.Exp(-0.5 * t * t);
                flank[i] = Math.Abs(t) > ShapeFlankSigma;
                if (flank[i])
                {
                    flankCount++;
                }
            }
            if (flankCount < order + 3)
            {
                return double.NaN;
            }

            // Подложка фитится по t = канал − центр, а не по номеру канала: на
            // 16384 каналах t^4 в нормальных уравнениях иначе теряет точность.
            double[] coefficients = PolyFitFlanks(y, offset, center, flank, order);
            if (coefficients == null)
            {
                return double.NaN;
            }

            double gg = 0.0, numerator = 0.0, variance = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = offset + i - center;
                double baseline = 0.0;
                double power = 1.0;
                for (int k = 0; k <= order; k++)
                {
                    baseline += coefficients[k] * power;
                    power *= t;
                }
                gg += g[i] * g[i];
                numerator += (y[i] - baseline) * g[i];
                variance += (Math.Max(y[i], 0.0) + Math.Max(baseline, 0.0)) * g[i] * g[i];
            }
            if (gg <= 1E-9 || variance <= 0.0)
            {
                return double.NaN;
            }

            double amplitude = numerator / gg;
            double error = Math.Sqrt(variance) / gg;
            return error > 0.0 ? amplitude / error : double.NaN;
        }

        static double[] PolyFitFlanks(double[] y, int offset, double center, bool[] flank, int order)
        {
            int size = order + 1;
            double[,] normal = new double[size, size + 1];
            for (int i = 0; i < y.Length; i++)
            {
                if (!flank[i])
                {
                    continue;
                }
                double t = offset + i - center;
                double[] powers = new double[size];
                double power = 1.0;
                for (int k = 0; k < size; k++)
                {
                    powers[k] = power;
                    power *= t;
                }
                for (int r = 0; r < size; r++)
                {
                    for (int c = 0; c < size; c++)
                    {
                        normal[r, c] += powers[r] * powers[c];
                    }
                    normal[r, size] += powers[r] * y[i];
                }
            }

            for (int col = 0; col < size; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < size; r++)
                {
                    if (Math.Abs(normal[r, col]) > Math.Abs(normal[pivot, col]))
                    {
                        pivot = r;
                    }
                }
                if (Math.Abs(normal[pivot, col]) < 1E-12)
                {
                    return null;             // вырожденная система - крылья пусты
                }
                if (pivot != col)
                {
                    for (int c = col; c <= size; c++)
                    {
                        double swap = normal[col, c];
                        normal[col, c] = normal[pivot, c];
                        normal[pivot, c] = swap;
                    }
                }
                for (int r = 0; r < size; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double factor = normal[r, col] / normal[col, col];
                    for (int c = col; c <= size; c++)
                    {
                        normal[r, c] -= factor * normal[col, c];
                    }
                }
            }

            double[] result = new double[size];
            for (int r = 0; r < size; r++)
            {
                result[r] = normal[r, size] / normal[r, r];
            }
            return result;
        }

        // Насколько хуже становится модель, если компоненту убрать: ΔD = D(без) − D(с),
        // посчитанные на носителе самой компоненты.
        //
        // Соседи при выключении ПЕРЕФИЧИВАЮТСЯ. Это и есть содержательная часть теста:
        // фантом стоит там, где его вклад может подобрать континуум или соседняя линия,
        // и после перефита модель без него почти так же хороша — ΔD мал. У настоящей
        // линии подобрать её форму нечем, и ΔD велик. Без перефита отсчётам выключенной
        // компоненты некуда деться, ΔD раздувается на её же площадь, и тест вырождается
        // обратно в тест амплитуды, то есть в тот самый гейт, который ничего не ловит.
        //
        // Модель возвращается в исходное состояние: тот же lambda и те же амплитуды —
        // каждый кандидат проверяется относительно одной и той же принятой модели.
        static double DevianceGain(int[] observed, List<FitComponent> model, FitComponent target,
                                   double[] lambda, int chMin, int chMax)
        {
            if (target.Amplitude <= 0.0)
            {
                return 0.0;
            }
            int start = Math.Max(chMin, target.Start);
            int end = Math.Min(chMax, target.Start + target.Profile.Length - 1);
            if (start > end)
            {
                return 0.0;
            }

            double devianceWith = PoissonDeviance(observed, lambda, start, end);

            double targetAmplitude = target.Amplitude;
            ApplyAmplitudeDelta(lambda, target, -targetAmplitude);

            List<FitComponent> neighbours = new List<FitComponent>();
            List<double> saved = new List<double>();
            foreach (FitComponent other in model)
            {
                if (ReferenceEquals(other, target))
                {
                    continue;
                }
                int otherEnd = other.Start + other.Profile.Length - 1;
                if (otherEnd < start || other.Start > end)
                {
                    continue;                       // носители не пересекаются
                }
                neighbours.Add(other);
                saved.Add(other.Amplitude);
            }

            for (int iteration = 0; iteration < ProfileFitIterations; iteration++)
            {
                bool improved = false;
                foreach (FitComponent other in neighbours)
                {
                    double step = Math.Max(0.5, other.Amplitude * 0.10);
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        double delta = direction * step;
                        if (other.Amplitude + delta < 0.0)
                        {
                            delta = -other.Amplitude;
                            if (delta == 0.0)
                            {
                                continue;
                            }
                        }
                        if (LocalLogLikelihoodDelta(observed, lambda, other, delta, start, end) > 1E-9)
                        {
                            ApplyAmplitudeDelta(lambda, other, delta);
                            improved = true;
                            break;
                        }
                    }
                }
                if (!improved)
                {
                    break;
                }
            }

            double devianceWithout = PoissonDeviance(observed, lambda, start, end);

            for (int i = 0; i < neighbours.Count; i++)
            {
                ApplyAmplitudeDelta(lambda, neighbours[i], saved[i] - neighbours[i].Amplitude);
            }
            ApplyAmplitudeDelta(lambda, target, targetAmplitude);

            return devianceWithout - devianceWith;
        }

        // Сумма профиля компоненты: множитель между амплитудой (высотой) и площадью.
        static double ProfileSum(FitComponent component)
        {
            double sum = 0.0;
            for (int i = 0; i < component.Profile.Length; i++)
            {
                sum += component.Profile[i];
            }
            return sum;
        }

        // Ложатся ли принятые линии набора на одну гладкую кривую S/I = A·eps(E).
        //
        // Кривая берётся в логарифмах: ln(S/I) = polynom(ln E). Логарифм здесь не
        // косметика - эффективность падает на порядки по диапазону, и в линейной
        // шкале подгонка отдала бы весь вес самым сильным линиям. Порядок 2 при
        // пяти и более линиях, иначе 1: на четырёх точках квадратика не оставит
        // ни одной степени свободы и разброс окажется нулевым у любого набора.
        // Оставить только те линии, что переживают смену модели фона. Тест тот
        // же самый, что и в гейте по линии (SurvivesBackgroundChange), но
        // применяется ПОСЛЕ фита, а не во время него: пока идёт фит, отсев по
        // одной линии лишает вето точек для кривой, и оно перестаёт судить -
        // измерено, глобально это даёт 10.4 % фантомов против 6.4 %.
        static List<LibraryCandidate> ShapeFilter(List<LibraryCandidate> candidates,
                                                  List<FitComponent> sources,
                                                  int[] observed, List<FitComponent> model,
                                                  int chMin, int chMax)
        {
            List<LibraryCandidate> kept = new List<LibraryCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                FitComponent source = i < sources.Count ? sources[i] : null;
                if (source == null ||
                    SurvivesBackgroundChange(observed, model, source, chMin, chMax))
                {
                    kept.Add(candidates[i]);
                }
            }
            return kept;
        }

        // Доля линий набора, которые кривая эффективности объявляет уверенно
        // видимыми, а фит их не принял. Возвращает -1, если предсказывать не по
        // чему (ни одна линия не проходит порог видимости).
        static double UnexplainedAbsence(List<LibraryCandidate> accepted,
                                         List<FitComponent> model, double[] lambda)
        {
            double[] curve = EfficiencyCurve(accepted);
            if (curve == null)
            {
                return -1.0;
            }

            HashSet<NuclideDefinition> seen = new HashSet<NuclideDefinition>();
            foreach (LibraryCandidate candidate in accepted)
            {
                if (candidate.Nuclide != null)
                {
                    seen.Add(candidate.Nuclide);
                }
            }

            int expected = 0;
            int missing = 0;
            foreach (FitComponent component in model)
            {
                if (component.Nuclide == null || component.Nuclide.Intencity <= 0.0 ||
                    component.Nuclide.Energy <= 0.0)
                {
                    continue;                       // escape-компоненты сюда не входят
                }

                // Сигма чистой площади при НУЛЕВОМ сигнале: та же информация
                // Фишера, что в FisherZ, только без множителя-амплитуды —
                // у отвергнутой линии амплитуда нулевая, и через неё не выразить.
                double information = 0.0;
                for (int j = 0; j < component.Profile.Length; j++)
                {
                    double pr = component.Profile[j];
                    information += pr * pr / Math.Max(1.0, lambda[component.Start + j]);
                }
                if (information <= 0.0)
                {
                    continue;
                }
                double sigmaAmplitude = 1.0 / Math.Sqrt(information);
                double profileSum = ProfileSum(component);
                if (profileSum <= 0.0)
                {
                    continue;
                }
                double sigmaArea = sigmaAmplitude * profileSum;

                double lnE = Math.Log(component.Nuclide.Energy);
                double model_ = curve[0] + curve[1] * lnE +
                                (curve.Length > 2 ? curve[2] * lnE * lnE : 0.0);
                double predicted = component.Nuclide.Intencity * Math.Exp(model_);
                if (!PeakShapeModel.IsFinite(predicted) ||
                    predicted < AbsenceVisibleSigma * sigmaArea)
                {
                    continue;                       // линия и не должна была быть видна
                }

                expected++;
                if (!seen.Contains(component.Nuclide))
                {
                    missing++;
                }
            }

            return expected > 0 ? (double)missing / expected : -1.0;
        }

        // Коэффициенты ln(S/I) = polynom(ln E) по принятым линиям. Та же кривая,
        // на которой стоит вето по разбросу; вынесена, чтобы её считали оба.
        static double[] EfficiencyCurve(List<LibraryCandidate> candidates)
        {
            List<double> x = new List<double>();
            List<double> y = new List<double>();
            foreach (LibraryCandidate candidate in candidates)
            {
                if (candidate.Nuclide == null || candidate.Area <= 0.0 ||
                    candidate.Nuclide.Intencity <= 0.0 || candidate.Nuclide.Energy <= 0.0)
                {
                    continue;
                }
                double ratio = candidate.Area / candidate.Nuclide.Intencity;
                if (!PeakShapeModel.IsFinite(ratio) || ratio <= 0.0)
                {
                    continue;
                }
                x.Add(Math.Log(candidate.Nuclide.Energy));
                y.Add(Math.Log(ratio));
            }
            if (x.Count < ChainConsistencyMinLines)
            {
                return null;
            }
            return PolyFit(x, y, x.Count >= 5 ? 2 : 1);
        }

        // Убрать выбросы поимённо, пока набор не уложится в порог разброса.
        // Возвращает укороченный список или null, если уложить не удалось в
        // пределах дозволенного числа исключений.
        static List<LibraryCandidate> TrimToConsistent(List<LibraryCandidate> candidates)
        {
            // Кандидаты, по которым кривая не строится (нет площади, интенсивности
            // или энергии), в подгонке не участвуют и выбросами быть не могут, но
            // из набора не выпадают: решение принимается по тем, кто на кривой.
            List<LibraryCandidate> usable = new List<LibraryCandidate>();
            List<LibraryCandidate> passive = new List<LibraryCandidate>();
            foreach (LibraryCandidate candidate in candidates)
            {
                double ratio = candidate.Nuclide != null && candidate.Nuclide.Intencity > 0.0 &&
                               candidate.Nuclide.Energy > 0.0 && candidate.Area > 0.0
                    ? candidate.Area / candidate.Nuclide.Intencity
                    : double.NaN;
                if (PeakShapeModel.IsFinite(ratio) && ratio > 0.0)
                {
                    usable.Add(candidate);
                }
                else
                {
                    passive.Add(candidate);
                }
            }

            int allowed = (int)Math.Floor(usable.Count * OutlierTrimMaxFraction);
            for (int dropped = 0; ; dropped++)
            {
                if (usable.Count < ChainConsistencyMinLines)
                {
                    return null;
                }

                List<double> x = new List<double>();
                List<double> y = new List<double>();
                foreach (LibraryCandidate candidate in usable)
                {
                    x.Add(Math.Log(candidate.Nuclide.Energy));
                    y.Add(Math.Log(candidate.Area / candidate.Nuclide.Intencity));
                }
                int order = x.Count >= 5 ? 2 : 1;
                double[] coefficients = PolyFit(x, y, order);
                if (coefficients == null)
                {
                    return null;
                }

                double sum = 0.0;
                int worst = -1;
                double worstResidual = -1.0;
                for (int i = 0; i < x.Count; i++)
                {
                    double model = 0.0;
                    double power = 1.0;
                    for (int k = 0; k <= order; k++)
                    {
                        model += coefficients[k] * power;
                        power *= x[i];
                    }
                    double residual = Math.Abs(y[i] - model);
                    sum += residual * residual;
                    if (residual > worstResidual)
                    {
                        worstResidual = residual;
                        worst = i;
                    }
                }

                double scatter = Math.Exp(Math.Sqrt(sum / x.Count)) - 1.0;
                if (!PeakShapeModel.IsFinite(scatter) || scatter <= ChainScatterLimit)
                {
                    List<LibraryCandidate> kept = new List<LibraryCandidate>(usable);
                    kept.AddRange(passive);
                    return kept;
                }

                if (dropped >= allowed || worst < 0)
                {
                    return null;               // не уложить - набор снимается целиком
                }

                // Выбрасывать можно только НАСТОЯЩИЙ выброс. Среднеквадратичная
                // невязка считается БЕЗ кандидата на исключение: иначе он сам
                // раздувает знаменатель и никогда не проходит порог.
                double restSum = sum - worstResidual * worstResidual;
                int restCount = x.Count - 1;
                if (restCount < 2)
                {
                    return null;
                }
                double restSigma = Math.Sqrt(restSum / restCount);
                if (!(restSigma > 0.0) || worstResidual < OutlierTrimGrubbsK * restSigma)
                {
                    return null;               // разбросаны все - это не выброс, а набор
                }
                usable.RemoveAt(worst);
            }
        }

        internal enum ChainVerdict
        {
            Consistent,      // набор лёг на общую кривую
            Inconsistent,    // не лёг - это улика
            Abstained        // точек меньше ChainConsistencyMinLines: судить не о чем
        }

        static ChainVerdict ChainConsistent(List<LibraryCandidate> candidates)
        {
            List<double> x = new List<double>();
            List<double> y = new List<double>();
            foreach (LibraryCandidate candidate in candidates)
            {
                if (candidate.Nuclide == null || candidate.Area <= 0.0 ||
                    candidate.Nuclide.Intencity <= 0.0 || candidate.Nuclide.Energy <= 0.0)
                {
                    continue;
                }
                double ratio = candidate.Area / candidate.Nuclide.Intencity;
                if (!PeakShapeModel.IsFinite(ratio) || ratio <= 0.0)
                {
                    continue;
                }
                x.Add(Math.Log(candidate.Nuclide.Energy));
                y.Add(Math.Log(ratio));
            }

            if (x.Count < ChainConsistencyMinLines)
            {
                return ChainVerdict.Abstained;   // судить не о чем - это не улика
            }

            int order = x.Count >= 5 ? 2 : 1;
            double[] coefficients = PolyFit(x, y, order);
            if (coefficients == null)
            {
                return ChainVerdict.Abstained;
            }

            double sum = 0.0;
            for (int i = 0; i < x.Count; i++)
            {
                double model = 0.0;
                double power = 1.0;
                for (int k = 0; k <= order; k++)
                {
                    model += coefficients[k] * power;
                    power *= x[i];
                }
                double residual = y[i] - model;
                sum += residual * residual;
            }

            // Разброс дробный: exp(rms) - 1. Для настоящей цепочки это 50-100 %,
            // для набора, собранного из случайных структур, - вдвое больше.
            double scatter = Math.Exp(Math.Sqrt(sum / x.Count)) - 1.0;
            return (!PeakShapeModel.IsFinite(scatter) || scatter <= ChainScatterLimit)
                ? ChainVerdict.Consistent
                : ChainVerdict.Inconsistent;
        }

        // Взвешенных весов тут нет намеренно: погрешность площади у сильной линии
        // - доли процента, и взвешивание по ней отдало бы весь фит двум-трём
        // линиям, а разброс определяется систематикой, а не статистикой.
        static double[] PolyFit(List<double> x, List<double> y, int order)
        {
            int size = order + 1;
            double[,] normal = new double[size, size + 1];
            for (int i = 0; i < x.Count; i++)
            {
                double[] powers = new double[size];
                double power = 1.0;
                for (int k = 0; k < size; k++)
                {
                    powers[k] = power;
                    power *= x[i];
                }
                for (int r = 0; r < size; r++)
                {
                    for (int c = 0; c < size; c++)
                    {
                        normal[r, c] += powers[r] * powers[c];
                    }
                    normal[r, size] += powers[r] * y[i];
                }
            }

            for (int col = 0; col < size; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < size; r++)
                {
                    if (Math.Abs(normal[r, col]) > Math.Abs(normal[pivot, col]))
                    {
                        pivot = r;
                    }
                }
                if (Math.Abs(normal[pivot, col]) < 1E-12)
                {
                    return null;
                }
                if (pivot != col)
                {
                    for (int c = col; c <= size; c++)
                    {
                        double swap = normal[col, c];
                        normal[col, c] = normal[pivot, c];
                        normal[pivot, c] = swap;
                    }
                }
                for (int r = 0; r < size; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double factor = normal[r, col] / normal[col, col];
                    for (int c = col; c <= size; c++)
                    {
                        normal[r, c] -= factor * normal[col, c];
                    }
                }
            }

            double[] result = new double[size];
            for (int r = 0; r < size; r++)
            {
                result[r] = normal[r, size] / normal[r, r];
            }
            return result;
        }

        // z = A·sqrt(I), I = Σ p²/max(1, λ) — информация Фишера амплитуды при
        // Пуассон-шуме; z сопоставим по смыслу с SNR finder'а.
        static double FisherZ(FitComponent component, double[] lambda)
        {
            if (component.Amplitude <= 0.0)
            {
                return 0.0;
            }

            double information = 0.0;
            for (int j = 0; j < component.Profile.Length; j++)
            {
                double p = component.Profile[j];
                information += p * p / Math.Max(1.0, lambda[component.Start + j]);
            }

            return component.Amplitude * Math.Sqrt(information);
        }
    }
}
