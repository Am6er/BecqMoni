using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Расчёт эффективности регистрации в пике полного поглощения по геометрии,
    /// методом Монте-Карло. Ось z — ось детектора, начало — передний торец
    /// кристалла, кристалл лежит в z от 0 до высоты, проба — в отрицательных z.
    ///
    /// Две части, разные по способу счёта:
    ///
    /// 1. От точки вылета до кристалла фотон ведётся ДЕТЕРМИНИРОВАННО: считается
    ///    оптическая толщина по всем слоям (проба, стенка стакана, зазор,
    ///    отражатель, корпус) и вес умножается на exp(-tau). Для пика полного
    ///    поглощения это точно, а не приближение: рассеявшийся по дороге фотон
    ///    теряет энергию и в пик уже не попадёт ни при каких обстоятельствах.
    ///
    /// 2. В кристалле — обычный розыгрыш: длина пробега по mu_total, выбор типа
    ///    взаимодействия, комптоновское рассеяние по Клейну — Нишине с
    ///    продолжением истории, рождение пар с двумя квантами 511 кэВ. В пик
    ///    попадает история, из которой НИЧЕГО не вылетело.
    ///
    /// Что уже снято из прежних приближений: когерентное рассеяние выделено
    /// своим каналом и не убивает квант по дороге к кристаллу
    /// (<see cref="CoherentPassesThrough"/>); вылет характеристического
    /// K-рентгена моделируется (<see cref="XrayEscape"/>), и доля K-оболочки
    /// берётся по энергии из EPICS2017, а не константой со скачка на крае;
    /// однократный комптон в ближних слоях разыгрывается
    /// (<see cref="SingleScatter"/>).
    ///
    /// Что осталось приближением и где это заметно:
    /// * связь электронов в комптоновском сечении не учтена (чистая
    ///   Клейн — Нишина). Ниже 100 кэВ это завышает комптон, но там правит
    ///   фотопоглощение;
    /// * L-флуоресценция не моделируется: L-рентген тяжёлых кристаллов — это
    ///   4-6 кэВ, наружу он не выходит ниоткуда, кроме самой поверхности.
    ///
    /// Один экземпляр — один поток: и генератор (state), и ленивая сборка
    /// сцены (EnsureBuilt) без замков. Параллельный счёт заводит по
    /// экземпляру на поток (см. EfficiencyCalculation.Run).
    /// </summary>
    public sealed class EfficiencySimulator
    {
        // Судьба электрона (ElectronEscape, Bremsstrahlung) считается отдельно:
        // пока она не учтена, расчёт молча кладёт всю энергию электрона на
        // месте, а на деле электрон вблизи границы уходит наружу, и тормозной
        // квант тоже может уйти. Обе потери растут с энергией — там же, где
        // расчёт расходится с измерением сильнее всего.

        const double ElectronMassKev = 510.99895;
        const double ClassicalRadiusCm = 2.8179403262e-13;
        const double Avogadro = 6.02214076e23;
        const double Eps = 1e-9;

        /// <summary>Историй на точку кривой.</summary>
        public int Histories = 200000;

        public int Seed = 20260803;

        /// <summary>
        /// Доля когерентного (рэлеевского) рассеяния в полном ослаблении
        /// кристалла — для оценки того, во что обходится его отсутствие в
        /// таблице. Ноль (умолчание) — прежнее поведение: когерентное молча
        /// сидит внутри фотопоглощения. Величина здесь ставится руками; это
        /// инструмент измерения чувствительности, а не физическая модель.
        /// </summary>
        public double CoherentFractionOfTotal;

        /// <summary>
        /// Ставить оправу детектора перед торцом кристалла (иначе — за ним).
        /// В файле геометрии LSRM указана только толщина, без указания места.
        ///
        /// ВЫКЛЮЧЕНО, и это больше не догадка: на чертеже конструктора геометрий
        /// LSRM (GMaster, вкладка Detector) источник стоит сверху, передние
        /// толщины отражателя и корпуса подписаны у верхнего торца, а толщина
        /// оправы — у нижнего. Оправа за кристаллом.
        ///
        /// Цена ошибки, если бы выбрали иначе (ASN16 в маринелли, оправа 0.2 см):
        /// -6.2 % на 50 кэВ, -3.9 % на 662, -3.3 % на 2614. Для источника
        /// спереди оправа сзади не поглощает ничего вовсе, и её толщина на
        /// расчёт не влияет — у маринелли влияет, потому что проба охватывает
        /// детектор и часть её оказывается позади кристалла.
        /// </summary>
        public bool MountingInFront;

        /// <summary>
        /// Считать не пик полного поглощения, а долю квантов, ДОШЕДШИХ до
        /// кристалла без ослабления. Нужно, чтобы отделить геометрию от физики:
        /// для точечного источника на оси эта доля равна телесному углу и
        /// проверяется формулой, безо всякого переноса.
        /// </summary>
        public bool ScoreEntranceOnly;

        /// <summary>
        /// Учитывать вылет самого электрона через близкую границу кристалла.
        /// Разыгрывается изотропное направление, пробег берётся из ESTAR
        /// (<see cref="ElectronData"/>), путь до границы считается по прямой,
        /// вылет — с эффективной глубины t_eff (порог и наклон по T, см.
        /// <see cref="ElectronEscapeSlope"/>).
        ///
        /// ВКЛЮЧЕНО с 07.08.2026 (физика 5). История: старая модель с полным
        /// CSDA была верхней оценкой, на 662 кэВ снимала 6–14 % и сверку с
        /// измерениями не прошла — выключалась. Развёртка против Geant4 по
        /// шкале показала, что неверна была МОДЕЛЬ, не физика: настоящий вылет
        /// на 662 — доли процента (потому и невидим заводским сверкам), а на
        /// 2614 наша ε_пик без него завышена на 15 % (tube) и 18 % (ASN16).
        /// Порогово-линейная t_eff калибрована по tube-развёртке и вслепую
        /// проверена на ASN16: остатки ≤1.1 % и +2.4/−3.6 % на 1332/2614
        /// (журнал tccfcalc2, §9). Ниже порога 350 кэВ вылета нет — низ шкалы
        /// не тронут.
        /// </summary>
        public bool ElectronEscape = true;

        /// <summary>
        /// Учитывать вылет тормозного кванта, рождённого электроном. Форму
        /// спектра задаёт <see cref="BremFromData"/>.
        /// </summary>
        public bool Bremsstrahlung = true;

        /// <summary>
        /// Спектр тормозного — из СЕЧЕНИЙ Зельцера — Бергера, а не приближением
        /// Крамерса (TODO M3).
        ///
        /// Прежде спектр брался как dN/dk = C/k на [5 кэВ, T] с нормировкой на
        /// радиационный выход ESTAR: средняя излучённая энергия выходила
        /// табличной, а форма — угаданной. Теперь она считается —
        /// <see cref="ThickTargetBrem"/> интегрирует dσ/dk по пути торможения
        /// электрона, беря сечения из `seltzer_berger` и пробег из ESTAR.
        ///
        /// У ЛСРМ на этом месте готовые таблицы толстой мишени на девять
        /// веществ, и иодистого цезия среди них нет (наш F10); здесь спектр
        /// считается для любого состава, лишь бы элементы были в поставке
        /// Зельцера — Бергера (Z = 1…92).
        ///
        /// Выключенный ключ возвращает приближение Крамерса — для абляции.
        /// </summary>
        public bool BremFromData = true;

        /// <summary>
        /// Наклон эффективной глубины вылета электрона:
        /// t_eff = наклон·max(0, T − <see cref="ElectronEscapeT0Kev"/>)·R(T),
        /// T в МэВ. Мягкий электрон гибнет по пути и возвращается обратным
        /// рассеянием — вылет включается порогом и растёт с T. Калибровка по
        /// Geant4-развёртке ε_пик 662–2614 кэВ на CsI 18.5×59
        /// (tools/tccfcalc2/README.md, §9).
        /// </summary>
        public double ElectronEscapeSlope = 0.4;

        /// <summary>
        /// Порог включения вылета, кэВ кинетической энергии.
        ///
        /// Здесь стояло 300, а док, сообщение коммита и §10 журнала говорили
        /// 350; артефактов, различающих их, не сохранилось — оба числа родились
        /// в одном коммите, а развёртка снималась ключами, которые тогда до
        /// симулятора не доезжали (F18). Разрешено ПОВТОРОМ точки развёртки
        /// 08.08.2026 (`esc_scan.py --t0s=300,350`, Nano16Pro_tube, физика 8,
        /// 8 млн историй, одно зерно — прогоны парные): наш/Geant4 при 300 —
        /// 1.017/1.025/0.952 на 662/1461/2614 кэВ, при 350 — 1.018/1.027/0.958
        /// при своём разбросе 0.002/0.003/0.004. Порог не решает НИЧЕГО на
        /// 662 и 1461 и стоит 1.5 σ на 2614, где 350 ближе к единице.
        /// Оставлено 350 — то, что написано в трёх документах из четырёх.
        /// </summary>
        public double ElectronEscapeT0Kev = 350.0;

        /// <summary>
        /// Сколько энергии событие может потерять и всё-таки остаться в пике,
        /// кэВ. Пик имеет ширину, и утечка в единицы кэВ из него не выводит.
        /// Ноль (умолчание) — прежний строгий счёт: в пик идёт только история,
        /// из которой не вылетело ничего.
        /// </summary>
        public double PeakHalfWidthKev;

        /// <summary>
        /// Учитывать вылет характеристического рентгена. Выше K-края квант
        /// выбивает электрон именно с K-оболочки, атом отвечает квантом Kα или
        /// Kβ, и тот может уйти наружу — событие покидает пик полного
        /// поглощения и садится в escape-пик.
        ///
        /// Эффект узкий по шкале, но крупный: у иодида цезия он включается
        /// скачком на 33.2 кэВ (край иода) и на 36.0 (край цезия), на 40 кэВ
        /// снимает четверть событий, к 200 кэВ сходит на нет. Ровно там стоят
        /// опорные линии калибровки — 59.5 америция, 81 бария, 122 кобальта.
        ///
        /// Считается только K: L-рентген тяжёлых сцинтилляторов — это 4-5 кэВ,
        /// его пробег десятки микрон, и наружу он не выходит ниоткуда, кроме
        /// самой поверхности.
        /// </summary>
        public bool XrayEscape = true;

        /// <summary>
        /// Не считать когерентное рассеяние потерей на пути к кристаллу.
        ///
        /// Рэлеевское рассеяние энергию не меняет: квант после него даёт тот же
        /// отсчёт в пике полного поглощения, если попал в кристалл. А попадает
        /// он почти наверняка, когда рассеялся в окне или оболочке — они в
        /// миллиметрах от кристалла и видны из точки рассеяния под большим
        /// углом. Убивать такой квант, как делает формула узкого пучка, —
        /// ошибка известного знака: она занижает эффективность, и тем сильнее,
        /// чем ниже энергия и толще окно.
        ///
        /// Что осталось за скобками: малоугловой комптон (тоже не сразу выводит
        /// из пика) и то, что для ДАЛЬНЕГО рассеивателя поправка завышает — там
        /// рассеянное в кристалл уже не возвращается. Второе мало: доля
        /// когерентного в воде падает с 13 % на 28 кэВ до 1 % на 200.
        /// </summary>
        public bool CoherentPassesThrough = true;

        /// <summary>
        /// Брать долю K-оболочки в фотопоглощении ПО ЭНЕРГИИ из пооболочечных
        /// сечений EPICS2017 (<see cref="MaterialDatabase.PhotoShellOf"/>), а
        /// не константой со скачка сечения на K-крае.
        ///
        /// Константа — это значение ровно НА крае, а доля с энергией растёт:
        /// у иода 0.834 на 33.2 кэВ, 0.842 на 40, 0.858 на 90. Константа
        /// занижала вылет рентгена на 1–3 % вероятности всюду выше края —
        /// ровно тот остаток «+7 % на 40 кэВ», что записан в
        /// database/scheme.md §9а A-2. Ключ измерительный: выключенный, он
        /// возвращает прежнее поведение до последнего бита.
        /// </summary>
        public bool KFractionByEnergy = true;

        /// <summary>
        /// Считать ОТКЛИК в шкале света, а не поглощённой энергии (TODO F11).
        ///
        /// Прибор меряет свет, и у CsI(Tl)/NaI(Tl) свет на единицу энергии
        /// зависит от энергии КАЖДОГО электрона (кривая L(E) из nucdb,
        /// таблица scint_electron_light_yield — модель Пейна, источники в
        /// tools/nucdb/import_light_yield.py). События с разным составом
        /// электронов — один фотоэлектрон против цепочки комптонов — дают
        /// разный свет при одной поглощённой энергии, и раскладывать их в один
        /// бин значит рисовать не тот спектр, что видит прибор.
        ///
        /// Шкала света привязывается к шкале прибора ПИКОМ: реальный прибор
        /// откалиброван по пикам полного поглощения, поэтому бины отклика
        /// пересчитываются так, чтобы средний свет пика лёг ровно на энергию
        /// линии, а континуум, вылеты и структура у K-края сместились
        /// ОТНОСИТЕЛЬНО пика — как в измеренном спектре. Остаток эффекта —
        /// ход ошибки калибровки МЕЖДУ опорными линиями — требует знания
        /// опорных линий прибора и в это приближение не входит.
        ///
        /// Пересчёт детерминированный и выполняется ПОСЛЕ прогона: розыгрыш
        /// не тянет ни одного случайного числа, кривая эффективности и пиковые
        /// величины не меняются вовсе, а с выключенным ключом (или без кривой
        /// в базе — германий, CZT) поведение побитово прежнее.
        /// </summary>
        public bool LightNonproportionality = true;

        /// <summary>
        /// Разыгрывать ОДНО комптоновское рассеяние на пути к кристаллу.
        ///
        /// Формула узкого пучка `exp(-tau)` считает потерянным всё, что
        /// провзаимодействовало. Для когерентного это чинится вычетом канала
        /// (<see cref="CoherentPassesThrough"/>), но комптон на малый угол тоже
        /// из пика не выводит: при 60 кэВ рассеяние на 10° отнимает 0.2 %
        /// энергии. А главное — рассеиватель в миллиметрах от кристалла (окно,
        /// оболочка, стенка стакана) виден из точки рассеяния под большим углом,
        /// и рассеянное вперёд туда и приходит.
        ///
        /// С 06.08.2026 рассеяние разыгрывается и у квантов, чей луч прошёл
        /// МИМО кристалла: на упоре таких большинство, и без них полная
        /// эффективность (сумма отклика) занижалась на ~15 % — это вскрыла
        /// сверка каскадного суммирования с новой TCCFCALC и Geant4-арбитром
        /// (tools/tccfcalc2/README.md, §8). В пик такие истории попадают
        /// только при ненулевом допуске, как и прежде; выигрывает канал
        /// континуума и всё, что на нём стоит (ε_полная, CF).
        ///
        /// Считается ОДНО рассеяние: после него квант ведётся до кристалла уже
        /// поглощающей проводкой. Второе и дальше отброшены сознательно — их
        /// вклад меньше и знак у него тот же, так что поправка остаётся НИЖНЕЙ
        /// оценкой, а не подгонкой.
        /// </summary>
        public bool SingleScatter = true;

        /// <summary>
        /// Континуум отклика — АНАЛОГОВОЙ веткой (физика 6, F14).
        ///
        /// Взвешенная проводка (конус + exp(−τ) + одно рассеяние) хороша для
        /// пика, но континуум систематически недобирает: кросс-проверка строк
        /// отклика против Geant4 на девяти геометриях дала 0.57–0.92 по полной
        /// сумме строки, полосы малых сумм — 0.2–0.8, хуже всего маринелли
        /// (tools/tccfcalc2/README.md, §11). Не хватает ровно того, что уже
        /// чинилось в ε_полной: полной сферы направлений, многократного
        /// рассеяния по всем областям, пролёта сквозь кристалл с возвратом
        /// из-за него и заноса электронов.
        ///
        /// Поэтому бины НИЖЕ пика считаются отдельным аналоговым прогоном тем
        /// же переносом, что <see cref="TotalEfficiency"/>, а бин пика остаётся
        /// за взвешенной оценкой — у неё дисперсия пика на порядок лучше.
        /// Классы историй не пересекаются по построению: аналоговый вклад,
        /// округлившийся в бин пика, отбрасывается.
        ///
        /// На кривую эффективности ключ не влияет вовсе: аналоговая ветка
        /// запускается только при счёте гистограммы отклика.
        /// </summary>
        public bool AnalogContinuum = true;

        /// <summary>
        /// Комптоновский угол — на СВЯЗАННОМ электроне: к Клейну — Нишине
        /// добавляется множитель отбора S(x,Z) из EPDL97 (таблица
        /// `epdl_scattering_function`, см. <see cref="ScatteringData"/>).
        ///
        /// Голая формула Клейна — Нишины описывает рассеяние на СВОБОДНОМ
        /// покоящемся электроне. У связанного электрона рассеяние на малый
        /// угол подавлено: переданный импульс меньше импульса связи, атом
        /// такую передачу «не принимает», и S(x,Z) → 0 при x → 0. Завышенное
        /// рассеяние вперёд — это завышенный континуум сразу под пиком и
        /// заниженный обратный ход.
        ///
        /// ПОЛНОЕ сечение при этом не меняется: оно остаётся из XCOM, где
        /// связанность уже учтена. S(x,Z) входит только множителем отбора,
        /// то есть меняет форму распределения, а не его нормировку — иначе
        /// связанность учлась бы дважды.
        /// </summary>
        public bool BoundCompton = true;

        /// <summary>
        /// Доплеровское размытие рассеянной энергии по профилям Комптона
        /// (таблицы `compton_profile*`, профили Биггса по оболочкам).
        ///
        /// Электрон, на котором рассеялись, не покоится: проекция его импульса
        /// на направление передачи сдвигает энергию рассеянного кванта. Без
        /// этого комптоновский край и пик обратного рассеяния выходят
        /// бесконечно резкими, а в измеренном спектре они размыты, и размыты
        /// НЕ только разрешением прибора.
        ///
        /// Оболочка выбирается по заселённости, импульс — по её профилю,
        /// энергия — из точного кинематического уравнения (импульсное
        /// приближение). Оболочка, энергия связи которой больше энергии кванта,
        /// недоступна и переразыгрывается.
        /// </summary>
        public bool DopplerBroadening = true;

        /// <summary>
        /// Когерентное (рэлеевское) рассеяние — настоящим каналом с углом по
        /// форм-фактору F²(x,Z) (таблица `epdl_form_factor`).
        ///
        /// До сих пор когерентное либо молча числилось поглощением, либо
        /// проходило насквозь БЕЗ отклонения
        /// (<see cref="CoherentPassesThrough"/>). Оба — крайние случаи: оно
        /// меняет направление, не трогая энергию, а значит и уводит квант мимо
        /// кристалла, и заводит его туда, куда он сам не летел.
        ///
        /// Ключ работает в АНАЛОГОВЫХ ветках переноса (розыгрыш в кристалле,
        /// ε_полная, континуум отклика). Взвешенная проводка к кристаллу
        /// (пиковая ветвь) устроена на exp(−τ) и угла не разыгрывает вовсе —
        /// там по-прежнему решает <see cref="CoherentPassesThrough"/>.
        /// </summary>
        public bool RayleighScatter = true;

        /// <summary>
        /// Относительная ошибка КОНТИНУУМА последнего прогона отклика, % полной
        /// суммы континуума строки; −1 — аналоговой ветки не было (F23).
        ///
        /// Считается отдельно от `relativeError`, который возвращает
        /// <see cref="Response"/>: тот описывает ВЗВЕШЕННУЮ ветку, тратящую на
        /// детектор все истории, и о статистике континуума не знает ничего.
        /// Аналоговая ветка идёт полной сферой (конус смещал ε_T на −4.7 %,
        /// см. <see cref="TotalFullSphere"/>), поэтому до кристалла доходит
        /// доля телесного угла: на контактной геометрии это почти все истории,
        /// а на точечном источнике в 20–25 см — единицы процентов от процента.
        /// Пик при этом остаётся точным, и разницу без этого числа не увидеть.
        ///
        /// Число оптимистичное: это ошибка ИНТЕГРАЛА континуума, шум в
        /// отдельном бине во столько раз больше, во сколько бинов размазаны
        /// события.
        /// </summary>
        public double LastContinuumRelativeError = -1.0;

        readonly GeometryModel geometry;
        readonly List<Region> regions = new List<Region>();
        Region crystal;
        double sphereZ, sphereR;         // объемлющая сфера детектора — для сужения конуса
        Sampler source;
        ulong state;
        bool crystalHasPartials;
        ElectronData.Material electron;

        /// <summary>Кривая светового выхода кристалла; null — шкала пропорциональна.</summary>
        MaterialDatabase.LightYieldCurve lightYield;

        /// <summary>
        /// Спектр тормозного толстой мишени для вещества кристалла; null —
        /// ключ выключен или сечений для состава нет, тогда работает
        /// приближение Крамерса.
        /// </summary>
        ThickTargetBrem bremTable;

        /// <summary>Элементы кристалла, у которых есть данные о K-флуоресценции.</summary>
        int[] fluoZ;
        double[] fluoFraction;
        MaterialDatabase.Fluorescence[] fluoData;

        /// <summary>
        /// Пооболочечный фотоэффект тех же элементов; null в ячейке — данных
        /// EPICS для элемента нет, доля K берётся константой, как раньше.
        /// </summary>
        MaterialDatabase.PhotoShellModel[] fluoShells;

        /// <summary>
        /// Угловые данные рассеяния по веществам сцены. Кэш на ЭКЗЕМПЛЯР, без
        /// замка: один экземпляр — один поток (см. шапку класса), а сама
        /// <see cref="ScatteringData"/> общий кэш стережёт своим.
        /// </summary>
        readonly Dictionary<GeometryMaterial, Scatterers> scatterers =
            new Dictionary<GeometryMaterial, Scatterers>();

        /// <summary>Элементы вещества с их угловыми данными рассеяния.</summary>
        sealed class Scatterers
        {
            public int[] Z;
            public double[] MassFraction;
            public ScatteringData.Atom[] Atom;
        }

        public EfficiencySimulator(GeometryModel model)
        {
            // Сцена строится в САНТИМЕТРАХ, а модель хранит миллиметры. Пересчёт
            // здесь, один раз и на входе: весь расчёт стоит на массовых
            // коэффициентах ослабления в см²/г и плотностях в г/см³, и путать
            // единицы длины внутри нельзя — пробег в миллиметрах при сечении на
            // сантиметр даёт кристалл, прозрачный вдесятеро.
            this.geometry = model == null ? null : model.InCentimeters();
        }

        /// <summary>
        /// Сцена собирается лениво: настройки (например, где стоит оправа)
        /// выставляются после конструктора, а на них она и опирается.
        /// </summary>
        void EnsureBuilt()
        {
            if (this.regions.Count > 0)
            {
                return;
            }

            this.Build();
            this.crystalHasPartials = this.CrystalHasPartials();
            this.electron = ElectronData.Match(this.geometry.Crystal);
            this.BuildFluorescence();
            this.lightYield = this.LightNonproportionality && this.electron != null
                ? MaterialDatabase.LightYieldOf(ScintillatorNameOf(this.electron))
                : null;
            this.bremTable = this.Bremsstrahlung && this.BremFromData && this.electron != null
                ? ThickTargetBrem.For(this.geometry.Crystal, this.electron, 5.0)
                : null;
        }

        /// <summary>
        /// Имя сцинтиллятора в таблице кривых света по веществу кристалла.
        /// Активатор в геометрии не записан (его доли процента), поэтому
        /// берётся штатный: у CsI это Tl — приборы корпуса с CsI(Na) не
        /// встречались, а появится такой — активатор придётся вынести в
        /// конфигурацию устройства.
        /// </summary>
        static string ScintillatorNameOf(ElectronData.Material material)
        {
            if (material == null)
            {
                return "";
            }

            switch (material.Name)
            {
                case "CsI": return "CsI:Tl";
                case "NaI": return "NaI:Tl";
                case "LaBr3": return "LaBr3:Ce";
                default: return material.Name;
            }
        }

        /// <summary>Имя кривой света, которой считается отклик; пусто — шкала пропорциональна.</summary>
        public string LightYieldName
        {
            get
            {
                this.EnsureBuilt();
                return this.lightYield == null ? "" : this.lightYield.Material;
            }
        }

        /// <summary>
        /// Собрать список элементов кристалла, у которых есть K-флуоресценция.
        /// Лёгких (кислород, натрий, кремний) в нём нет и быть не должно:
        /// K-край у них ниже сетки сечений, а рентген в килоэлектронвольт
        /// поглощается в микронах от места рождения.
        /// </summary>
        void BuildFluorescence()
        {
            List<int> zs = new List<int>();
            List<double> fractions = new List<double>();
            List<MaterialDatabase.Fluorescence> data = new List<MaterialDatabase.Fluorescence>();
            List<MaterialDatabase.PhotoShellModel> shells = new List<MaterialDatabase.PhotoShellModel>();
            foreach (KeyValuePair<int, double> pair in this.geometry.Crystal.Fractions)
            {
                if (!(pair.Value > 0.0))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(pair.Key);
                if (f == null)
                {
                    continue;
                }

                zs.Add(pair.Key);
                fractions.Add(pair.Value);
                data.Add(f);
                shells.Add(this.KFractionByEnergy
                    ? MaterialDatabase.PhotoShellOf(pair.Key)
                    : null);
            }

            this.fluoZ = zs.ToArray();
            this.fluoFraction = fractions.ToArray();
            this.fluoData = data.ToArray();
            this.fluoShells = shells.ToArray();
        }

        /// <summary>
        /// Название материала кристалла в таблице ESTAR, или пустая строка, если
        /// состава там нет: тогда судьба электрона не считается вовсе.
        /// </summary>
        public string ElectronMaterialName
        {
            get
            {
                this.EnsureBuilt();
                return this.electron == null ? "" : this.electron.Name;
            }
        }

        /// <summary>Считается ли кристалл по парциальным сечениям, а не приближением.</summary>
        public bool UsesPartialCrossSections
        {
            get
            {
                this.EnsureBuilt();
                return this.crystalHasPartials;
            }
        }

        // ------------------------------------------------------------------
        // Сцена
        // ------------------------------------------------------------------

        /// <summary>
        /// Область сцены: либо коаксиальное кольцо (RIn..ROut), либо
        /// прямоугольный брус (|x| &lt;= AX, |y| &lt;= AY). Области вкладываются
        /// друг в друга, и поиск идёт по порядку: побеждает первая, в которую
        /// точка попала, поэтому кристалл кладётся раньше своей обвязки.
        /// </summary>
        sealed class Region
        {
            public bool IsBox;
            public double RIn, ROut;      // кольцо
            public double AX, AY;         // полуразмеры бруса
            public double ZMin, ZMax;
            public GeometryMaterial Material;
            public bool IsCrystal;

            public bool Contains(double x, double y, double z)
            {
                if (z < this.ZMin - Eps || z >= this.ZMax - Eps)
                {
                    return false;
                }

                if (this.IsBox)
                {
                    return Math.Abs(x) < this.AX - Eps && Math.Abs(y) < this.AY - Eps;
                }

                double r = Math.Sqrt(x * x + y * y);
                return r >= this.RIn - Eps && r < this.ROut - Eps;
            }
        }

        void Add(double rIn, double rOut, double zMin, double zMax,
                 GeometryMaterial material, bool isCrystal)
        {
            if (!(rOut > rIn + Eps) || !(zMax > zMin + Eps) || material == null
                || !(material.Density > 0.0))
            {
                return;
            }

            this.Register(new Region
            {
                RIn = rIn,
                ROut = rOut,
                ZMin = zMin,
                ZMax = zMax,
                Material = material,
                IsCrystal = isCrystal,
            }, isCrystal);
        }

        /// <summary>Брус с полуразмерами ax, ay. Вкладывается: порядок значим.</summary>
        void AddBox(double ax, double ay, double zMin, double zMax,
                    GeometryMaterial material, bool isCrystal)
        {
            if (!(ax > Eps) || !(ay > Eps) || !(zMax > zMin + Eps) || material == null
                || !(material.Density > 0.0))
            {
                return;
            }

            this.Register(new Region
            {
                IsBox = true,
                AX = ax,
                AY = ay,
                ZMin = zMin,
                ZMax = zMax,
                Material = material,
                IsCrystal = isCrystal,
            }, isCrystal);
        }

        void Register(Region region, bool isCrystal)
        {
            this.regions.Add(region);
            if (isCrystal)
            {
                this.crystal = region;
            }
        }

        /// <summary>
        /// Вещество слоя или ПУСТОТА, если его в файле нет.
        ///
        /// Пустота, а не «ничего»: области сцены вложены и ищутся по порядку, и
        /// пропущенный слой не исчезает, а замещается слоем СНАРУЖИ. Забыли
        /// плотность отражателя — и на его месте оказывается алюминий корпуса,
        /// который тяжелее; расчёт доводится до конца и выдаёт чужую кривую.
        /// Поэтому слой всё равно занимает своё место, но не поглощает.
        /// О самой пропаже говорит GeometryModel.Warnings.
        /// </summary>
        static GeometryMaterial OrVacuum(GeometryMaterial material)
        {
            if (material != null && material.Density > 0.0 && material.Fractions.Count > 0)
            {
                return material;
            }

            GeometryMaterial vacuum = new GeometryMaterial { Name = "vacuum", Density = 1e-10 };
            vacuum.Fractions[1] = 1.0;
            return vacuum;
        }

        void Build()
        {
            GeometryModel g = this.geometry;
            GeometryMaterial reflector = OrVacuum(g.Reflector);
            GeometryMaterial cladding = OrVacuum(g.Cladding);
            GeometryMaterial beakerWall = OrVacuum(g.BeakerWall);
            GeometryMaterial sample = OrVacuum(g.Source);
            double rc = 0.5 * g.CrystalDiameter;
            double hc = g.CrystalHeight;
            double tfr = g.FrontReflectorThickness, tsr = g.SideReflectorThickness;
            double tfc = g.FrontCladdingThickness, tsc = g.SideCladdingThickness;
            // Оправа детектора. В файле геометрии это одна толщина без указания,
            // где она стоит; MountingInFront решает, ставить её перед торцом
            // (тогда квант её проходит) или за кристаллом. Ключ введён как
            // измерительный: у прогона без неё остаётся ровный сдвиг вверх.
            double tm = Math.Max(0.0, g.MountingThickness);
            double rDet = rc + tsr + tsc;
            double zFace = -(tfr + tfc) - (this.MountingInFront ? tm : 0.0);

            // Кристалл и его обвязка. Области вкладываются, порядок значим:
            // кристалл кладётся первым, чтобы точка внутри него доставалась ему,
            // а не объемлющему слою.
            double transverse;
            if (g.Shape == CrystalShape.Box)
            {
                double ax = 0.5 * g.CrystalBoxX, ay = 0.5 * g.CrystalBoxY;
                hc = g.CrystalBoxZ;
                this.AddBox(ax, ay, 0.0, hc, g.Crystal, true);
                this.AddBox(ax, ay, -tfr, 0.0, reflector, false);
                this.AddBox(ax + tsr, ay + tsr, -tfr, hc, reflector, false);
                this.AddBox(ax + tsr + tsc, ay + tsr + tsc, -(tfr + tfc), -tfr, cladding, false);
                this.AddBox(ax + tsr + tsc, ay + tsr + tsc, -tfr, hc, cladding, false);
                if (this.MountingInFront && tm > 0.0)
                {
                    this.AddBox(ax + tsr + tsc, ay + tsr + tsc, zFace, -(tfr + tfc), cladding, false);
                }
                else if (tm > 0.0)
                {
                    this.AddBox(ax + tsr + tsc, ay + tsr + tsc, hc, hc + tm, cladding, false);
                }

                double bx = ax + tsr + tsc, by = ay + tsr + tsc;
                transverse = Math.Sqrt(bx * bx + by * by);
            }
            else
            {
                this.Add(0.0, rc, 0.0, hc, g.Crystal, true);
                this.Add(0.0, rc, -tfr, 0.0, reflector, false);
                this.Add(rc, rc + tsr, -tfr, hc, reflector, false);
                this.Add(0.0, rDet, -(tfr + tfc), -tfr, cladding, false);
                this.Add(rc + tsr, rDet, -tfr, hc, cladding, false);
                if (this.MountingInFront && tm > 0.0)
                {
                    this.Add(0.0, rDet, zFace, -(tfr + tfc), cladding, false);
                }
                else if (tm > 0.0)
                {
                    this.Add(0.0, rDet, hc, hc + tm, cladding, false);
                }

                transverse = rDet;
            }

            this.sphereZ = 0.5 * hc;
            this.sphereR = Math.Sqrt(transverse * transverse
                                     + Math.Pow(0.5 * hc + tfr + tfc, 2.0)) + 1e-3;

            switch (g.SourceType)
            {
                case GeometrySourceType.Point:
                    this.source = new PointSampler(zFace - g.PointDistance);
                    break;

                case GeometrySourceType.Box:
                {
                    // Прямоугольная кювета: та же раскладка, что у цилиндра, но
                    // дно прямоугольное. Стороны в модели ПОЛНЫЕ, области
                    // строятся по половинам.
                    double axOut = 0.5 * g.BoxSourceX, ayOut = 0.5 * g.BoxSourceY;
                    double axIn = Math.Max(0.0, axOut - g.BoxSideWallThickness);
                    double ayIn = Math.Max(0.0, ayOut - g.BoxSideWallThickness);
                    double zWallTop = zFace - g.BoxToDetectorDistance;
                    double zWallBottom = zWallTop - g.BoxEndWallThickness;
                    double zSrcTop = zWallBottom;
                    double zSrcBottom = zSrcTop - g.BoxSourceHeight;
                    // Проба РАНЬШЕ боковой стенки: стенка — полный брус, области
                    // ищутся «первая победившая», и в обратном порядке стенка
                    // затеняет пробу целиком (F13: вода считалась полиэтиленом).
                    // Стенка нулевой толщины совпала бы с пробой — не кладётся.
                    this.AddBox(axOut, ayOut, zWallBottom, zWallTop, beakerWall, false);
                    this.AddBox(axIn, ayIn, zSrcBottom, zSrcTop, sample, false);
                    if (g.BoxSideWallThickness > Eps)
                    {
                        this.AddBox(axOut, ayOut, zSrcBottom, zSrcTop, beakerWall, false);
                    }
                    this.source = new BoxSampler(axIn, ayIn, zSrcBottom, zSrcTop);
                    break;
                }

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * g.BeakerDiameter;
                    double rIn = Math.Max(0.0, rOut - g.BeakerSideWallThickness);
                    double zWallTop = zFace - g.BeakerToDetectorDistance;
                    double zWallBottom = zWallTop - g.BeakerEndWallThickness;
                    double zSrcTop = zWallBottom;
                    double zSrcBottom = zSrcTop - g.SourceHeight;
                    this.Add(0.0, rOut, zWallBottom, zWallTop, beakerWall, false);
                    this.Add(rIn, rOut, zSrcBottom, zSrcTop, beakerWall, false);
                    this.Add(0.0, rIn, zSrcBottom, zSrcTop, sample, false);
                    this.source = new CylinderSampler(rIn, zSrcBottom, zSrcTop);
                    break;
                }

                default:
                {
                    // Стакан Маринелли: проба охватывает детектор. Колодец —
                    // глухое отверстие, детектор входит в него; расстояние до
                    // детектора отмеряется от внутреннего потолка колодца.
                    double rh = 0.5 * g.MarinelliHoleDiameter;
                    double ths = g.MarinelliHoleSideThickness;
                    double the = g.MarinelliHoleEndWallThickness;
                    double rOut = Math.Max(0.5 * g.MarinelliBeakerDiameter, rh + ths + 0.1);
                    double rSrcOut = Math.Max(rh + ths, rOut - g.MarinelliSideThickness);
                    double hs = g.MarinelliSourceHeight;
                    double hh = g.MarinelliHoleHeight;

                    double zCeiling = zFace - g.MarinelliToDetectorDistance;
                    double cap = Math.Max(0.0, hs - hh);        // проба над потолком колодца
                    double zSrc0 = zCeiling - the - cap;

                    this.Add(0.0, rh + ths, zCeiling - the, zCeiling, beakerWall, false);
                    this.Add(rh, rh + ths, zCeiling, zCeiling + hh, beakerWall, false);
                    this.Add(rSrcOut, rOut, zSrc0, zSrc0 + hs, beakerWall, false);
                    this.Add(0.0, rh + ths, zSrc0, zCeiling - the, sample, false);
                    this.Add(rh + ths, rSrcOut, zSrc0, zSrc0 + hs, sample, false);
                    this.source = new MarinelliSampler(rh + ths, rSrcOut, zSrc0, zSrc0 + hs,
                                                       zCeiling - the);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Розыгрыш точки вылета
        // ------------------------------------------------------------------

        abstract class Sampler
        {
            public abstract void Next(EfficiencySimulator s, out double x, out double y, out double z);

            /// <summary>Машинная строка для дампа сцены (см, ось сцены).</summary>
            public abstract string Describe();
        }

        sealed class PointSampler : Sampler
        {
            readonly double z;

            public PointSampler(double z)
            {
                this.z = z;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                x = 0.0;
                y = 0.0;
                z = this.z;
            }

            public override string Describe()
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     "source point {0:R}", this.z);
            }
        }

        /// <summary>
        /// Точка внутри прямоугольной кюветы. Равномерно по объёму — здесь это
        /// просто три независимых равномерных числа, в отличие от цилиндра, где
        /// радиус приходится брать корнем.
        /// </summary>
        sealed class BoxSampler : Sampler
        {
            readonly double ax, ay, z0, z1;

            public BoxSampler(double ax, double ay, double z0, double z1)
            {
                this.ax = ax;
                this.ay = ay;
                this.z0 = z0;
                this.z1 = z1;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                x = this.ax * (2.0 * s.Uniform() - 1.0);
                y = this.ay * (2.0 * s.Uniform() - 1.0);
                z = this.z0 + (this.z1 - this.z0) * s.Uniform();
            }

            public override string Describe()
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     "source box {0:R} {1:R} {2:R} {3:R}",
                                     this.ax, this.ay, this.z0, this.z1);
            }
        }

        sealed class CylinderSampler : Sampler
        {
            readonly double r, z0, z1;

            public CylinderSampler(double r, double z0, double z1)
            {
                this.r = r;
                this.z0 = z0;
                this.z1 = z1;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                // равномерно по объёму: радиус по корню, иначе центр перевешен
                double rr = this.r * Math.Sqrt(s.Uniform());
                double phi = 2.0 * Math.PI * s.Uniform();
                x = rr * Math.Cos(phi);
                y = rr * Math.Sin(phi);
                z = this.z0 + (this.z1 - this.z0) * s.Uniform();
            }

            public override string Describe()
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     "source cyl {0:R} {1:R} {2:R}",
                                     this.r, this.z0, this.z1);
            }
        }

        sealed class MarinelliSampler : Sampler
        {
            readonly double rIn, rOut, z0, z1, zCap;
            readonly double capFraction;

            public MarinelliSampler(double rIn, double rOut, double z0, double z1, double zCap)
            {
                this.rIn = rIn;
                this.rOut = rOut;
                this.z0 = z0;
                this.z1 = z1;
                this.zCap = zCap;
                double annulus = (rOut * rOut - rIn * rIn) * (z1 - z0);
                double cap = rIn * rIn * Math.Max(0.0, zCap - z0);
                this.capFraction = (annulus + cap) > 0.0 ? cap / (annulus + cap) : 0.0;
            }

            public override void Next(EfficiencySimulator s, out double x, out double y, out double z)
            {
                double rr, zz;
                if (s.Uniform() < this.capFraction)
                {
                    rr = this.rIn * Math.Sqrt(s.Uniform());
                    zz = this.z0 + (this.zCap - this.z0) * s.Uniform();
                }
                else
                {
                    double a = this.rIn * this.rIn;
                    double b = this.rOut * this.rOut;
                    rr = Math.Sqrt(a + (b - a) * s.Uniform());
                    zz = this.z0 + (this.z1 - this.z0) * s.Uniform();
                }

                double phi = 2.0 * Math.PI * s.Uniform();
                x = rr * Math.Cos(phi);
                y = rr * Math.Sin(phi);
                z = zz;
            }

            public override string Describe()
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     "source marinelli {0:R} {1:R} {2:R} {3:R} {4:R}",
                                     this.rIn, this.rOut, this.z0, this.z1, this.zCap);
            }
        }

        // ------------------------------------------------------------------
        // Сечения
        // ------------------------------------------------------------------

        /// <summary>Полное сечение Клейна — Нишины на электрон, см².</summary>
        public static double KleinNishinaTotal(double energyKev)
        {
            double a = energyKev / ElectronMassKev;
            if (!(a > 0.0))
            {
                return 0.0;
            }

            double t1 = (1.0 + a) / (a * a) * (2.0 * (1.0 + a) / (1.0 + 2.0 * a)
                                               - Math.Log(1.0 + 2.0 * a) / a);
            double t2 = Math.Log(1.0 + 2.0 * a) / (2.0 * a);
            double t3 = (1.0 + 3.0 * a) / ((1.0 + 2.0 * a) * (1.0 + 2.0 * a));
            return 2.0 * Math.PI * ClassicalRadiusCm * ClassicalRadiusCm * (t1 + t2 - t3);
        }

        /// <summary>
        /// Каналы взаимодействия в кристалле, 1/см.
        ///
        /// Если для всех элементов кристалла есть парциальные сечения
        /// (<see cref="PartialCrossSections"/>) — берутся они. Это единственный
        /// правильный путь: канал поглощения в сцинтилляторе есть малая разность
        /// больших чисел, и получать его вычитанием комптона из полного нельзя.
        /// В CsI на 1332 кэВ настоящий фотоэффект — 5.2 % полного ослабления, а
        /// вычитание даёт 7.7 %, в полтора раза больше.
        ///
        /// Когерентное рассеяние во взаимодействия НЕ входит: энергии оно не
        /// оставляет, а направление меняет на малый угол. Считать его
        /// поглощением — ровно та ошибка, ради снятия которой таблица заведена.
        ///
        /// Запасной путь (элемента нет в таблице) — прежнее приближение: комптон
        /// по Клейну — Нишине, остаток делится между фотоэффектом и парами
        /// размазанным порогом. Оно завышает поглощение и оставлено только
        /// чтобы расчёт не падал на неизвестном кристалле.
        /// </summary>
        void CrystalChannels(double energyKev, out double photo, out double compton, out double pair)
        {
            GeometryMaterial m = this.geometry.Crystal;
            if (this.crystalHasPartials)
            {
                photo = 0.0;
                compton = 0.0;
                pair = 0.0;
                foreach (KeyValuePair<int, double> f in m.Fractions)
                {
                    photo += f.Value * PartialCrossSections.MassCrossSection(
                        f.Key, energyKev, PhotonProcess.Photoelectric);
                    compton += f.Value * PartialCrossSections.MassCrossSection(
                        f.Key, energyKev, PhotonProcess.Incoherent);
                    pair += f.Value * PartialCrossSections.MassCrossSection(
                        f.Key, energyKev, PhotonProcess.PairProduction);
                }

                photo *= m.Density;
                compton *= m.Density;
                pair *= m.Density;
                return;
            }

            double total = m.LinearAttenuation(energyKev);
            compton = KleinNishinaTotal(energyKev) * m.ElectronDensity();
            if (compton > total)
            {
                compton = total;
            }

            // Когерентное рассеяние энергии не оставляет: если его выделить,
            // оно уходит из канала поглощения совсем. Сейчас оно неотделимо и
            // молча числится фотопоглощением — а фотопоглощение в середине
            // шкалы само мало, и потому такая добавка искажает ветвление
            // сильнее всего именно там.
            double rest = Math.Max(0.0, total - compton - this.CoherentFractionOfTotal * total);
            double ramp = 0.0;
            if (energyKev > 2.0 * ElectronMassKev)
            {
                ramp = Math.Min(1.0, (energyKev - 2.0 * ElectronMassKev) / (1500.0 - 2.0 * ElectronMassKev));
            }

            pair = ramp * rest;
            photo = rest - pair;
        }

        /// <summary>Есть ли парциальные сечения для всех элементов кристалла.</summary>
        bool CrystalHasPartials()
        {
            if (this.geometry.Crystal.Fractions.Count == 0)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> f in this.geometry.Crystal.Fractions)
            {
                if (f.Value > 0.0 && !PartialCrossSections.HasElement(f.Key))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Трассировка
        // ------------------------------------------------------------------

        Region At(double x, double y, double z)
        {
            for (int i = 0; i < this.regions.Count; i++)
            {
                if (this.regions[i].Contains(x, y, z))
                {
                    return this.regions[i];
                }
            }

            return null;
        }

        /// <summary>Расстояние до ближайшей границы любой области, вдоль луча.</summary>
        double StepToBoundary(double x, double y, double z, double ux, double uy, double uz)
        {
            double best = double.MaxValue;
            for (int i = 0; i < this.regions.Count; i++)
            {
                Region g = this.regions[i];
                Plane(z, uz, g.ZMin, ref best);
                Plane(z, uz, g.ZMax, ref best);
                if (g.IsBox)
                {
                    Plane(x, ux, g.AX, ref best);
                    Plane(x, ux, -g.AX, ref best);
                    Plane(y, uy, g.AY, ref best);
                    Plane(y, uy, -g.AY, ref best);
                }
                else
                {
                    Cylinder(x, y, ux, uy, g.RIn, ref best);
                    Cylinder(x, y, ux, uy, g.ROut, ref best);
                }
            }

            return best;
        }

        static void Plane(double z, double uz, double plane, ref double best)
        {
            if (Math.Abs(uz) < Eps)
            {
                return;
            }

            double t = (plane - z) / uz;
            if (t > 1e-7 && t < best)
            {
                best = t;
            }
        }

        static void Cylinder(double x, double y, double ux, double uy, double radius, ref double best)
        {
            if (!(radius > 0.0))
            {
                return;
            }

            double a = ux * ux + uy * uy;
            if (a < Eps)
            {
                return;
            }

            double b = 2.0 * (x * ux + y * uy);
            double c = x * x + y * y - radius * radius;
            double disc = b * b - 4.0 * a * c;
            if (disc < 0.0)
            {
                return;
            }

            double sq = Math.Sqrt(disc);
            double t1 = (-b - sq) / (2.0 * a);
            double t2 = (-b + sq) / (2.0 * a);
            if (t1 > 1e-7 && t1 < best)
            {
                best = t1;
            }

            if (t2 > 1e-7 && t2 < best)
            {
                best = t2;
            }
        }

        /// <summary>
        /// Ведёт фотон до кристалла, копя оптическую толщину. Возвращает false,
        /// если кристалл не встретился.
        /// </summary>
        bool ToCrystal(ref double x, ref double y, ref double z,
                       double ux, double uy, double uz, double energyKev, out double tau)
        {
            tau = 0.0;
            double travelled = 0.0;
            double limit = 40.0 * this.sphereR + 200.0;
            for (int guard = 0; guard < 200; guard++)
            {
                Region here = this.At(x, y, z);
                if (here != null && here.IsCrystal)
                {
                    return true;
                }

                double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (step >= double.MaxValue || travelled + step > limit)
                {
                    return false;
                }

                if (here != null)
                {
                    tau += (this.CoherentPassesThrough
                            ? here.Material.LinearAttenuationWithoutCoherent(energyKev)
                            : here.Material.LinearAttenuation(energyKev)) * step;
                    if (tau > 60.0)
                    {
                        return false;      // exp(-60) — заведомо ноль
                    }
                }

                double advance = step + 1e-7;
                x += ux * advance;
                y += uy * advance;
                z += uz * advance;
                travelled += advance;
            }

            return false;
        }

        /// <summary>
        /// Вклад историй, рассеявшихся ОДИН раз по дороге к кристаллу.
        ///
        /// Прямой вклад считается весом `exp(-tau)`, где tau — оптическая
        /// толщина по каналам, которые квант убивают. Значит доля
        /// `1 - exp(-tau)` — это те истории, что провзаимодействовали, и они
        /// сейчас просто теряются. Часть из них — комптон, и такой квант никуда
        /// не делся: он летит дальше с другой энергией.
        ///
        /// Точка взаимодействия разыгрывается по той же экспоненте, но
        /// нормированной на условие «взаимодействие произошло»; в этой точке
        /// доля комптона равна mu_неког/mu_убив. Дальше — угол по Клейну —
        /// Нишине, новая энергия и обычная проводка до кристалла.
        ///
        /// Один розыгрыш даёт ОБА ответа сразу — сколько история приносит в пик
        /// и в какой бин отклика ложится, — потому что вопросы разные, а
        /// случайные числа у них обязаны быть одни и те же.
        ///
        /// Событие попадает в пик РАССЕЯННОЙ энергии, а не исходной: в пик
        /// полного поглощения линии оно годится только тогда, когда потеря
        /// укладывается в ЕГО ШИРИНУ. Отсюда важное: при нулевом допуске
        /// (<see cref="PeakHalfWidthKev"/> = 0) эта поправка не даёт ничего
        /// вовсе, и так и должно быть — у детектора с бесконечным
        /// разрешением рассеянный квант в пик линии не попадает. Величина
        /// поправки зависит от разрешения прибора, а его в модели геометрии
        /// нет. Потери у истории две — недобор при рассеянии и вылет из
        /// кристалла — и в пик она годится, только когда В СУММЕ они
        /// укладываются в допуск: порознь каждая может быть меньше w, а
        /// событие отстоит от пика на величину до 2w. Это ровно то же
        /// `E − deposited`, которым пользуется <see cref="Deposit"/>, и
        /// записано оно один раз — в <see cref="InPeak"/>.
        ///
        /// Возвращает добавку к счёту (0, если рассеяние не состоялось,
        /// рассеянный квант до кристалла не дошёл или в пик не годится).
        ///
        /// До 08.08.2026 счёт и гистограмма стояли в РАЗНЫХ ветках `Run`: при
        /// `histogram == null` добавка в счёт была, при `histogram != null` —
        /// нет, и возвращаемая `Run` эффективность значила разное в
        /// зависимости от аргумента (F28). Розыгрыш от объединения не
        /// изменился: обе ветки и раньше звали
        /// <see cref="ScatteredContribution"/> ровно один раз за историю.
        /// </summary>
        double ScatteredRun(double[] histogram, double binKev,
                            double x, double y, double z,
                            double ux, double uy, double uz,
                            double energyKev, double tauKill, double weight)
        {
            this.lossAnnihilation = 0.0;
            this.lossXray = 0.0;
            this.lightDeposit = 0.0;

            double sw, scattered, sEscaped;
            if (!this.ScatteredContribution(x, y, z, ux, uy, uz, energyKev, tauKill,
                                            out sw, out scattered, out sEscaped))
            {
                return 0.0;
            }

            // Рассеявшийся квант приносит СВОЮ энергию, а не энергию линии: в
            // отклике он и должен лечь ниже по шкале, а не в пик.
            double deposited = scattered - sEscaped;
            double share = weight * sw;
            if (histogram != null)
            {
                this.Deposit(histogram, binKev, energyKev, deposited, share);
                this.ScoreLight(binKev, deposited, share);
                if (this.channelHistograms != null)
                {
                    // Квант рассеялся ДО кристалла и принёс меньше энергии
                    // линии — в пик он не попадёт при любом исходе внутри.
                    // Канал берётся по тем же меткам: если внутри ушёл рентген
                    // или аннигиляционный квант, история принадлежит им, иначе
                    // это недобор.
                    ResponseChannel channel = this.ChannelOf(sEscaped);
                    if (channel == ResponseChannel.Peak)
                    {
                        channel = ResponseChannel.Compton;
                    }

                    this.Deposit(this.channelHistograms[(int)channel],
                            binKev, energyKev, deposited, share);
                }
            }

            return this.InPeak(energyKev, deposited) ? share : 0.0;
        }

        /// <summary>
        /// Тот же однократно рассеявшийся квант, но без отсечек по допуску:
        /// возвращает вес, энергию ПОСЛЕ рассеяния и то, сколько из неё
        /// вылетело. Отсечку «годится в пик» и раскладку по бинам навешивает
        /// поверх <see cref="ScatteredRun"/>. Разделение нужно потому, что
        /// «попало в пик» и «сколько поглотилось» — разные вопросы к одной
        /// истории, а розыгрыш у них обязан быть один.
        /// </summary>
        bool ScatteredContribution(double x, double y, double z,
                                   double ux, double uy, double uz,
                                   double energyKev, double tauKill,
                                   out double weight, out double scatteredEnergy,
                                   out double escapedEnergy)
        {
            weight = 0.0;
            scatteredEnergy = 0.0;
            escapedEnergy = 0.0;
            if (!this.SingleScatter || !(tauKill > 1e-6))
            {
                return false;
            }

            double interacted = 1.0 - Math.Exp(-tauKill);
            // точка первого взаимодействия: tau_целевое из усечённой экспоненты
            double tauTarget = -Math.Log(1.0 - this.Uniform() * interacted);

            double px = x, py = y, pz = z;
            double accumulated = 0.0;
            Region here = null;
            double travelled = 0.0;
            double limit = 40.0 * this.sphereR + 200.0;
            for (int guard = 0; guard < 200; guard++)
            {
                here = this.At(px, py, pz);
                if (here != null && here.IsCrystal)
                {
                    return false;             // до кристалла не рассеялся
                }

                double step = this.StepToBoundary(px, py, pz, ux, uy, uz);
                if (step >= double.MaxValue || travelled + step > limit)
                {
                    return false;
                }

                if (here != null)
                {
                    double mu = this.CoherentPassesThrough
                        ? here.Material.LinearAttenuationWithoutCoherent(energyKev)
                        : here.Material.LinearAttenuation(energyKev);
                    if (mu > 0.0 && accumulated + mu * step >= tauTarget)
                    {
                        double advance = (tauTarget - accumulated) / mu;
                        px += ux * advance;
                        py += uy * advance;
                        pz += uz * advance;
                        double incoherent = here.Material.LinearIncoherent(energyKev);
                        double share = incoherent / mu;
                        if (!(share > 0.0))
                        {
                            return false;     // взаимодействие было, но не комптон
                        }

                        double cos;
                        double scattered = this.ComptonScatter(here.Material, energyKev, out cos);
                        double sx = ux, sy = uy, sz = uz;
                        this.Rotate(ref sx, ref sy, ref sz, cos);

                        double tau2;
                        if (!this.ToCrystal(ref px, ref py, ref pz, sx, sy, sz, scattered, out tau2))
                        {
                            return false;
                        }

                        weight = interacted * share * Math.Exp(-tau2);
                        scatteredEnergy = scattered;
                        escapedEnergy = this.InCrystal(px, py, pz, sx, sy, sz, scattered, 0);
                        return true;
                    }

                    accumulated += mu * step;
                }

                double next = step + 1e-7;
                px += ux * next;
                py += uy * next;
                pz += uz * next;
                travelled += next;
            }

            return false;
        }

        /// <summary>
        /// Оптическая толщина убивающих каналов вдоль луча ДО ВЫХОДА из сцены.
        /// Нужна лучам, прошедшим мимо кристалла: у них нет tau «до кристалла»,
        /// а рассеяться в кристалл они могут из любого слоя по дороге.
        /// </summary>
        double KillDepthToExit(double x, double y, double z,
                               double ux, double uy, double uz, double energyKev)
        {
            double tau = 0.0;
            double travelled = 0.0;
            double limit = 40.0 * this.sphereR + 200.0;
            for (int guard = 0; guard < 200; guard++)
            {
                Region here = this.At(x, y, z);
                double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (step >= double.MaxValue || travelled + step > limit)
                {
                    return tau;
                }

                if (here != null)
                {
                    tau += (this.CoherentPassesThrough
                            ? here.Material.LinearAttenuationWithoutCoherent(energyKev)
                            : here.Material.LinearAttenuation(energyKev)) * step;
                    if (tau > 60.0)
                    {
                        return 60.0;
                    }
                }

                double advance = step + 1e-7;
                x += ux * advance;
                y += uy * advance;
                z += uz * advance;
                travelled += advance;
            }

            return tau;
        }

        /// <summary>Длина пути внутри кристалла от точки в направлении.</summary>
        double CrystalPath(double x, double y, double z, double ux, double uy, double uz)
        {
            Region c = this.crystal;
            double best = double.MaxValue;
            Plane(z, uz, c.ZMin, ref best);
            Plane(z, uz, c.ZMax, ref best);
            if (c.IsBox)
            {
                Plane(x, ux, c.AX, ref best);
                Plane(x, ux, -c.AX, ref best);
                Plane(y, uy, c.AY, ref best);
                Plane(y, uy, -c.AY, ref best);
            }
            else
            {
                Cylinder(x, y, ux, uy, c.ROut, ref best);
            }

            return best >= double.MaxValue ? 0.0 : best;
        }

        // ------------------------------------------------------------------
        // Розыгрыш в кристалле
        // ------------------------------------------------------------------

        /// <summary>
        /// Ведёт фотон внутри кристалла. Возвращает энергию, ВЫЛЕТЕВШУЮ наружу
        /// (0 — всё поглотилось, история попадает в пик полного поглощения).
        /// </summary>
        double InCrystal(double x, double y, double z, double ux, double uy, double uz,
                         double energyKev, int depth)
        {
            if (depth > 12)
            {
                return energyKev;
            }

            // lost копит энергию, ушедшую из кристалла на предыдущих шагах: это
            // тормозные кванты и сами электроны от уже отработанных
            // взаимодействий. Всё, что возвращается, — сумма таких потерь.
            double lost = 0.0;
            double e = energyKev;
            for (int step = 0; step < 200; step++)
            {
                double photo, compton, pair;
                this.CrystalChannels(e, out photo, out compton, out pair);
                // Когерентное — отдельным каналом: энергии не оставляет, но
                // поворачивает квант, а значит меняет и путь до выхода.
                double coherent = this.RayleighScatter
                    ? this.geometry.Crystal.LinearCoherent(e) : 0.0;
                double total = photo + compton + pair + coherent;
                if (!(total > 0.0))
                {
                    return lost + e;
                }

                double path = this.CrystalPath(x, y, z, ux, uy, uz);
                double free = -Math.Log(1.0 - this.Uniform()) / total;
                if (free >= path)
                {
                    return lost + e;                // вылетел
                }

                x += ux * free;
                y += uy * free;
                z += uz * free;

                double pick = this.Uniform() * total;
                if (pick < photo)
                {
                    // Энергия делится надвое: характеристический квант, если
                    // атом ответил им, и всё остальное — на электроны (сам
                    // фотоэлектрон, оже-каскад, мягкие линии). Сумма всегда
                    // равна энергии поглощённого кванта, ничего не теряется и
                    // не появляется.
                    double xray = this.SampleFluorescence(e);
                    if (xray > 0.0)
                    {
                        double kx, ky, kz;
                        this.Isotropic(out kx, out ky, out kz);
                        // Порядок вызовов ТОТ ЖЕ, что был до раскладки по
                        // каналам: сначала электрон, потом рентгеновский квант.
                        // Оба тянут случайные числа, и перестановка уводит
                        // поток — матрица выходит другой, а кривая
                        // эффективности перестаёт быть побитово прежней.
                        double electron = this.ElectronLoss(x, y, z, e - xray, depth);
                        // Метка канала — только НЕПОМЕЧЕННЫЙ остаток вылета:
                        // вложенная рекурсия свои вылеты уже пометила (её метка
                        // точнее — она знает, ЧЕМ квант вылетел), и прибавка
                        // полного gone считала бы их дважды, а rest в ChannelOf
                        // уходил бы в минус.
                        double markedBefore = this.lossAnnihilation + this.lossXray;
                        double gone = this.InCrystal(x, y, z, kx, ky, kz, xray, depth + 1);
                        double markedInside = this.lossAnnihilation + this.lossXray - markedBefore;
                        this.lossXray += Math.Max(0.0, gone - markedInside);
                        return lost + electron + gone;
                    }

                    // фотоэлектрон уносит почти всю энергию кванта
                    return lost + this.ElectronLoss(x, y, z, e, depth);
                }

                if (pick < photo + compton)
                {
                    double cos;
                    double scattered = this.ComptonScatter(this.geometry.Crystal, e, out cos);
                    this.Rotate(ref ux, ref uy, ref uz, cos);
                    lost += this.ElectronLoss(x, y, z, e - scattered, depth);
                    e = scattered;
                    if (e < 1.0)
                    {
                        // остаток осел на месте — по свету это электрон
                        // той же субкэвной энергии
                        this.AddLight(e, e);
                        return lost;
                    }

                    continue;
                }

                if (pick < photo + compton + coherent)
                {
                    // Когерентное рассеяние: энергия та же, направление другое.
                    // Ни отсчёта, ни потери — только новый путь до выхода.
                    this.Rotate(ref ux, ref uy, ref uz,
                                this.RayleighCosine(this.geometry.Crystal, e));
                    continue;
                }

                // рождение пары: 1022 кэВ уходит в два кванта аннигиляции,
                // остальное достаётся паре электрон-позитрон. Кванты летят
                // СТРОГО в противоположные стороны (импульс покоящейся пары
                // нулевой): разыгранные независимо, они завышали бы совпадение
                // «оба поглотились»/«оба вылетели» и портили соотношение
                // одиночного и двойного вылета.
                double escaped = lost + this.ElectronLoss(x, y, z, e - 2.0 * ElectronMassKev, depth);
                double ax, ay, az;
                this.Isotropic(out ax, out ay, out az);
                // Как и у рентгена выше: метится только непомеченный рекурсией
                // остаток, иначе вложенные вылеты считаются дважды.
                double pairMarkedBefore = this.lossAnnihilation + this.lossXray;
                double first = this.InCrystal(x, y, z, ax, ay, az, ElectronMassKev, depth + 1);
                double second = this.InCrystal(x, y, z, -ax, -ay, -az, ElectronMassKev, depth + 1);
                double pairMarkedInside = this.lossAnnihilation + this.lossXray - pairMarkedBefore;
                this.lossAnnihilation += Math.Max(0.0, first + second - pairMarkedInside);
                return escaped + first + second;
            }

            return lost + e;
        }

        /// <summary>
        /// Сколько энергии уходит из кристалла вместе с электроном кинетической
        /// энергии <paramref name="te"/>, рождённым в точке (x, y, z).
        ///
        /// Две независимые статьи расхода:
        ///
        /// 1. Тормозное излучение. Спектр — из сечений Зельцера — Бергера,
        ///    проинтегрированных по пути торможения
        ///    (<see cref="ThickTargetBrem"/>, ключ <see cref="BremFromData"/>);
        ///    выключенный ключ возвращает прежнее приближение Крамерса
        ///    dN/dk = C/k на [k_min, Te] с нормировкой на радиационный выход
        ///    ESTAR (C = Y·Te/(Te − k_min), число квантов = C·ln(Te/k_min)).
        ///    Каждый разыгранный квант ведётся дальше обычной трассировкой и
        ///    может вылететь, а может поглотиться.
        ///
        /// 2. Вылет самого электрона. Направление изотропно (длинный трек
        ///    изотропизуется многократным рассеянием), но вылет считается не с
        ///    полного пробега CSDA, а с ЭФФЕКТИВНОЙ ГЛУБИНЫ
        ///    t_eff = <see cref="ElectronEscapeSlope"/>·max(0, T − <see
        ///    cref="ElectronEscapeT0Kev"/>)·R(T), T в МэВ: мягкий электрон
        ///    гибнет по пути и возвращается обратным рассеянием, и доля
        ///    пробега, с которой вылет реально уносит энергию, растёт с T.
        ///    Форма, наклон и порог откалиброваны по Geant4-развёртке
        ///    662–2614 кэВ (журнал tccfcalc2, §9, §15).
        ///
        ///    Здесь стояла формула БЕЗ порога, и её числа (t_eff/R = 0.06 на
        ///    662 → 0.33 на 2614) отвечали наклону ~0.15, а не зашитому 0.4 —
        ///    описание расходилось с кодом на всю величину порога (F18). При
        ///    сегодняшних 0.4 и 350 кэВ выходит t_eff/R = 0.12 (662) → 0.91
        ///    (2614).
        ///
        ///    Прежние модели не прошли сверку ОБЕ: изотропная с полным CSDA —
        ///    верхняя оценка, снимала 6–14 % на 662 (см. memory
        ///    electron-escape-rejected); направленная по родившему кванту с
        ///    detour — не та форма по энергии (на 662 вшестеро больше Geant4,
        ///    на 2000 — мало: короткие треки она переоценивает, длинные,
        ///    уходящие вбок от переднего направления, — теряет).
        ///
        /// Что здесь приближение: путь не прямая; тормозной квант испускается
        /// в точке рождения электрона, а не вдоль его пути; вылетевший
        /// позитрон всё равно аннигилирует в точке рождения пары.
        /// </summary>
        double ElectronLoss(double x, double y, double z, double te, int depth)
        {
            if (this.electron == null || !(te > 1.0) || depth > 12)
            {
                // электрон осел целиком там, где родился
                this.AddLight(te, te);
                return 0.0;
            }

            double lost = 0.0;

            // Свет электрона — по СОБСТВЕННОМУ треку: излучённое тормозным
            // покидает трек всё, а не только вылетевшая из кристалла часть.
            // Перепоглощённые кванты рождают свои электроны в рекурсии, и
            // считать их энергию ещё и здесь значило бы засчитать свет дважды.
            double radiated = 0.0;

            if (this.Bremsstrahlung)
            {
                const double MinKev = 5.0;      // ниже кванту не выйти ниоткуда
                if (te > MinKev)
                {
                    // Форма спектра — либо по сечениям Зельцера — Бергера,
                    // проинтегрированным по пути торможения (`BremFromData`),
                    // либо прежним приближением Крамерса dN/dk = C/k с
                    // нормировкой на радиационный выход ESTAR. Оба дают ЧИСЛО
                    // квантов и их энергии; всё остальное ниже общее.
                    ThickTargetBrem table = this.BremFromData ? this.bremTable : null;
                    double mean = table != null
                        ? table.Photons(te)
                        : ElectronData.YieldOf(this.electron, te) * te / (te - MinKev)
                          * Math.Log(te / MinKev);
                    int n = this.Poisson(mean);
                    for (int i = 0; i < n; i++)
                    {
                        double k = table != null
                            ? table.SampleKev(te, this.Uniform())
                            : MinKev * Math.Pow(te / MinKev, this.Uniform());
                        double ax, ay, az;
                        this.Isotropic(out ax, out ay, out az);

                        // Сумма квантов не может превысить энергию электрона:
                        // кванты разыгрываются независимо, и редкая история
                        // излучала больше, чем имела. Розыгрыши (k, направление)
                        // делаются ВСЕГДА — поток случайных чисел в нормальных
                        // историях не сдвигается, зажимается только расход.
                        double kUse = Math.Min(k, te - radiated);
                        if (!(kUse > 0.0))
                        {
                            continue;
                        }

                        radiated += kUse;
                        lost += this.InCrystal(x, y, z, ax, ay, az, kUse, depth + 1);
                    }
                }
            }

            double escapedSelf = 0.0;
            if (this.ElectronEscape)
            {
                double density = this.geometry.Crystal.Density;
                double range = ElectronData.RangeOf(this.electron, te) / density;   // см
                // эффективная глубина вылета: порог и линейный рост по T.
                // Пробег и глубина сознательно считаются от ПОЛНОЙ te — так
                // калибровался наклон (журнал tccfcalc2, §10); излучённое
                // учитывается ниже зажимом уносимой энергии.
                double reach = range * Math.Min(1.0, this.ElectronEscapeSlope
                    * Math.Max(0.0, te - this.ElectronEscapeT0Kev) / 1000.0);
                double ax, ay, az;
                this.Isotropic(out ax, out ay, out az);
                double toEdge = this.CrystalPath(x, y, z, ax, ay, az);
                if (toEdge < reach)
                {
                    // расход пробега на пути до границы — в масштабе reach:
                    // на границе (toEdge→reach) остаток нулевой, вплотную к
                    // стенке (toEdge→0) уносится почти вся энергия
                    double used = range * toEdge / reach;
                    // Унести больше, чем осталось после тормозного, нельзя:
                    // без зажима lost превышал te, а свет уходил в минус и
                    // молча отбрасывался — история теряла энергию из ниоткуда.
                    escapedSelf = Math.Min(
                        ElectronData.EnergyOfRange(this.electron, (range - used) * density),
                        te - radiated);
                    lost += escapedSelf;
                }
            }

            this.AddLight(te - radiated - escapedSelf, te);
            return lost;
        }

        /// <summary>
        /// Разыграть характеристический квант при фотопоглощении кванта энергии
        /// <paramref name="energyKev"/>. Ноль — атом ответил оже-электроном,
        /// поглощение на другой оболочке или элементе без данных.
        ///
        /// Розыгрыш в три шага, и первый — самый важный: сначала выбирается,
        /// НА КАКОМ элементе произошло поглощение, с весом w·σ_фото(E). Брать
        /// просто массовую долю нельзя — у иодида цезия доли почти равны, а
        /// края разнесены на 2.8 кэВ, и между ними поглощает только цезий.
        /// Дальше — попала ли дырка в K-оболочку (доля из скачка сечения на
        /// крае) и ответил ли атом квантом (выход флуоресценции).
        /// </summary>
        double SampleFluorescence(double energyKev)
        {
            if (!this.XrayEscape || this.fluoZ == null || this.fluoZ.Length == 0)
            {
                return 0.0;
            }

            // вес элемента — его вклад в фотопоглощение на этой энергии
            double sum = 0.0;
            double[] weight = new double[this.fluoZ.Length];
            for (int i = 0; i < this.fluoZ.Length; i++)
            {
                if (energyKev <= this.fluoData[i].KEdgeKev)
                {
                    continue;               // K-оболочка ещё недоступна
                }

                weight[i] = this.fluoFraction[i] * PartialCrossSections.MassCrossSection(
                    this.fluoZ[i], energyKev, PhotonProcess.Photoelectric);
                sum += weight[i];
            }

            if (!(sum > 0.0))
            {
                return 0.0;
            }

            // Знаменатель — полное фотопоглощение вещества, включая элементы без
            // K-края на этой энергии: они тоже поглощают, и их доля обязана
            // уменьшать вероятность рентгена, а не выпадать из счёта.
            double all = 0.0;
            foreach (KeyValuePair<int, double> pair in this.geometry.Crystal.Fractions)
            {
                all += pair.Value * PartialCrossSections.MassCrossSection(
                    pair.Key, energyKev, PhotonProcess.Photoelectric);
            }

            if (!(all > 0.0) || this.Uniform() * all >= sum)
            {
                return 0.0;
            }

            double pick = this.Uniform() * sum;
            int k = 0;
            while (k < weight.Length - 1 && pick >= weight[k])
            {
                pick -= weight[k];
                k++;
            }

            MaterialDatabase.Fluorescence f = this.fluoData[k];

            // Доля K-оболочки: по энергии из EPICS2017, если данные есть;
            // иначе — константа со скачка на крае, как раньше. Число случайных
            // чисел от выбора не меняется — меняется только порог сравнения.
            double kFraction = this.fluoShells[k] != null
                ? this.fluoShells[k].KFraction(energyKev)
                : f.KFraction;
            if (this.Uniform() >= kFraction * f.OmegaK)
            {
                return 0.0;                 // не K-оболочка или оже-электрон
            }

            double line = this.Uniform();
            double acc = 0.0;
            for (int i = 0; i < f.LineWeight.Length; i++)
            {
                acc += f.LineWeight[i];
                if (line < acc)
                {
                    return f.LineKev[i];
                }
            }

            return f.LineKev[f.LineKev.Length - 1];
        }

        /// <summary>Пуассон по Кнуту: среднее у нас всегда меньше единицы.</summary>
        int Poisson(double mean)
        {
            if (!(mean > 0.0))
            {
                return 0;
            }

            if (mean > 20.0)
            {
                mean = 20.0;
            }

            double limit = Math.Exp(-mean), p = 1.0;
            int k = 0;
            while (k < 64)
            {
                p *= this.Uniform();
                if (p <= limit)
                {
                    break;
                }

                k++;
            }

            return k;
        }

        // ------------------------------------------------------------------
        // Углы рассеяния
        // ------------------------------------------------------------------

        /// <summary>
        /// Ослабление, по которому АНАЛОГОВАЯ ветка разыгрывает длину свободного
        /// пробега вне кристалла.
        ///
        /// С <see cref="RayleighScatter"/> когерентное входит в него наравне с
        /// прочим и разыгрывается своим каналом — поворотом без потери энергии.
        /// Без ключа оно вычтено: квант проходит слой, будто его не было, и это
        /// прежнее приближение <see cref="CoherentPassesThrough"/>. Обе крайности
        /// — «убить» и «не заметить» — заменяются розыгрышем только здесь;
        /// взвешенная проводка к кристаллу углов не разыгрывает вовсе.
        /// </summary>
        double AnalogMu(GeometryMaterial material, double energyKev)
        {
            return this.RayleighScatter
                ? material.LinearAttenuation(energyKev)
                : material.LinearAttenuationWithoutCoherent(energyKev);
        }

        /// <summary>Угловые данные элементов вещества; строится один раз на вещество.</summary>
        Scatterers ScatterersOf(GeometryMaterial material)
        {
            Scatterers found;
            if (this.scatterers.TryGetValue(material, out found))
            {
                return found;
            }

            List<int> zs = new List<int>();
            List<double> mass = new List<double>();
            List<ScatteringData.Atom> atoms = new List<ScatteringData.Atom>();
            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                if (!(pair.Value > 0.0))
                {
                    continue;
                }

                ScatteringData.Atom atom = ScatteringData.Of(pair.Key);
                if (atom == null)
                {
                    continue;
                }

                zs.Add(pair.Key);
                mass.Add(pair.Value);
                atoms.Add(atom);
            }

            Scatterers built = new Scatterers
            {
                Z = zs.ToArray(),
                MassFraction = mass.ToArray(),
                Atom = atoms.ToArray()
            };
            this.scatterers[material] = built;
            return built;
        }

        /// <summary>
        /// На КАКОМ элементе вещества произошло рассеяние: розыгрыш по вкладам
        /// элементов в сечение канала. Угловые данные принадлежат атому, и для
        /// соединения выбрать атом надо раньше, чем угол; null — данных нет
        /// ни у одного элемента (тогда остаётся голый Клейн — Нишина).
        /// </summary>
        ScatteringData.Atom PickAtom(GeometryMaterial material, double energyKev,
                                     PhotonProcess process)
        {
            Scatterers s = this.ScatterersOf(material);
            int n = s.Atom.Length;
            if (n == 0)
            {
                return null;
            }

            if (n == 1)
            {
                return s.Atom[0];
            }

            double total = 0.0;
            for (int i = 0; i < n; i++)
            {
                total += s.MassFraction[i]
                         * PartialCrossSections.MassCrossSection(s.Z[i], energyKev, process);
            }

            if (!(total > 0.0))
            {
                return s.Atom[0];
            }

            double pick = this.Uniform() * total;
            double running = 0.0;
            for (int i = 0; i < n; i++)
            {
                running += s.MassFraction[i]
                           * PartialCrossSections.MassCrossSection(s.Z[i], energyKev, process);
                if (pick <= running)
                {
                    return s.Atom[i];
                }
            }

            return s.Atom[n - 1];
        }

        /// <summary>
        /// Комптоновское рассеяние в веществе <paramref name="material"/>:
        /// возвращает энергию рассеянного кванта, косинус угла — наружу.
        ///
        /// Три слоя, каждый отпирается своим ключом: угол по Клейну — Нишине
        /// всегда, множитель отбора S(x,Z) при <see cref="BoundCompton"/>,
        /// доплеровский сдвиг энергии при <see cref="DopplerBroadening"/>.
        /// С выключенными обоими результат побитово прежний.
        ///
        /// Открыт наружу ради пробы `BoundScatterProbe`: розыгрыш надо мерить
        /// тем же кодом, каким он идёт в расчёте, а не его копией.
        /// </summary>
        public double ComptonScatter(GeometryMaterial material, double energyKev, out double cos)
        {
            ScatteringData.Atom atom = null;
            if ((this.BoundCompton || this.DopplerBroadening) && material != null)
            {
                atom = this.PickAtom(material, energyKev, PhotonProcess.Incoherent);
            }

            cos = this.ComptonCosine(energyKev, this.BoundCompton ? atom : null);
            double free = energyKev / (1.0 + energyKev / ElectronMassKev * (1.0 - cos));
            if (!this.DopplerBroadening || atom == null || atom.ShellCount == 0)
            {
                return free;
            }

            return this.DopplerEnergy(atom, energyKev, cos, free);
        }

        /// <summary>
        /// Энергия рассеянного кванта с учётом импульса связанного электрона
        /// (импульсное приближение). Оболочка — по заселённости, проекция
        /// импульса p_z — по профилю Комптона этой оболочки.
        ///
        /// Кинематика: при p_z = q·m_e·c и ε = E'/E выполняется
        /// ε²·(v₂² − q²) − 2ε·(v₂ − q²·cosθ) + (1 − q²) = 0, где
        /// v₂ = 1 + (E/m_e c²)(1 − cosθ). Из двух корней годится тот, у
        /// которого знак (1 − ε·v₂) совпадает со знаком q: второй появился при
        /// возведении в квадрат и отвечает противоположной проекции.
        ///
        /// Рассеянная энергия ограничена сверху E − E_св: связь оболочки
        /// оплачивается из энергии кванта, и без этой границы синий сдвиг
        /// давал бы электрону отрицательную энергию.
        /// </summary>
        double DopplerEnergy(ScatteringData.Atom atom, double energyKev,
                             double cos, double free)
        {
            double a = energyKev / ElectronMassKev;
            double var2 = 1.0 + a * (1.0 - cos);
            for (int guard = 0; guard < 64; guard++)
            {
                int shell = atom.SelectShell(this.Uniform());
                double binding = atom.ShellBindingKev(shell);
                if (binding >= energyKev)
                {
                    continue;                  // оболочка кванту не по зубам
                }

                double q = ScatteringData.FineStructure
                           * atom.SampleMomentumAu(shell, this.Uniform());
                if (this.Uniform() < 0.5)
                {
                    q = -q;
                }

                double q2 = q * q;
                double var3 = var2 * var2 - q2;
                double var4 = var2 - q2 * cos;
                double disc = var4 * var4 - var3 + q2 * var3;
                if (!(var3 > 0.0) || !(disc >= 0.0))
                {
                    continue;
                }

                double root = Math.Sqrt(disc);
                double eps = Consistent((var4 - root) / var3, var2, q);
                if (!(eps > 0.0))
                {
                    eps = Consistent((var4 + root) / var3, var2, q);
                }

                if (!(eps > 0.0) || eps > 1.0)
                {
                    continue;
                }

                double scattered = eps * energyKev;
                if (scattered > energyKev - binding)
                {
                    continue;
                }

                return scattered;
            }

            return free;
        }

        /// <summary>
        /// Доплеровская энергия при ЗАДАННОМ угле — только для пробы
        /// `BoundScatterProbe`: она меряет размытие отдельно от розыгрыша
        /// угла, иначе ширина края мешалась бы с шириной углового
        /// распределения. В самом расчёте не участвует.
        /// </summary>
        public double DopplerAt(GeometryMaterial material, double energyKev, double cos)
        {
            ScatteringData.Atom atom =
                this.PickAtom(material, energyKev, PhotonProcess.Incoherent);
            double free = energyKev / (1.0 + energyKev / ElectronMassKev * (1.0 - cos));
            if (atom == null || atom.ShellCount == 0)
            {
                return free;
            }

            return this.DopplerEnergy(atom, energyKev, cos, free);
        }

        /// <summary>
        /// Корень, отвечающий разыгранному знаку проекции импульса: −1, если
        /// корень посторонний (появился при возведении уравнения в квадрат).
        /// </summary>
        static double Consistent(double eps, double var2, double q)
        {
            if (!(eps > 0.0))
            {
                return -1.0;
            }

            double residual = 1.0 - eps * var2;
            if (Math.Abs(residual) < 1e-12)
            {
                return eps;                    // q ≈ 0, оба корня совпали
            }

            return (residual > 0.0) == (q > 0.0) ? eps : -1.0;
        }

        /// <summary>
        /// Косинус угла КОГЕРЕНТНОГО рассеяния по форм-фактору: сначала
        /// разыгрывается квадрат переданного импульса по F²(x,Z), затем
        /// доигрывается томсоновский множитель (1 + cos²θ)/2 отбором.
        /// Энергия при этом не меняется вовсе. Открыт наружу по той же
        /// причине, что <see cref="ComptonScatter"/>.
        /// </summary>
        public double RayleighCosine(GeometryMaterial material, double energyKev)
        {
            ScatteringData.Atom atom = this.PickAtom(material, energyKev, PhotonProcess.Coherent);
            double xMax = ScatteringData.InverseCmPerKev * energyKev;
            double tMax = xMax * xMax;
            if (atom == null || !(tMax > 0.0))
            {
                return 2.0 * this.Uniform() - 1.0;
            }

            for (int guard = 0; guard < 1000; guard++)
            {
                double t = atom.SampleMomentumTransferSq(this.Uniform(), tMax);
                double cos = 1.0 - 2.0 * t / tMax;
                if (cos < -1.0) cos = -1.0;
                if (cos > 1.0) cos = 1.0;
                if (this.Uniform() <= 0.5 * (1.0 + cos * cos))
                {
                    return cos;
                }
            }

            return 1.0;
        }

        /// <summary>
        /// Косинус угла комптоновского рассеяния, метод Кана. С непустым
        /// <paramref name="atom"/> к нему добавляется отбор по функции
        /// некогерентного рассеяния: принимается доля S(x,Z)/Z, где
        /// x = (E/hc)·sin(θ/2). Полное сечение от этого не меняется — оно
        /// берётся из XCOM, а отбор перераспределяет углы внутри канала.
        /// </summary>
        double ComptonCosine(double energyKev, ScatteringData.Atom atom)
        {
            if (atom == null)
            {
                return this.ComptonCosine(energyKev);
            }

            double k = ScatteringData.InverseCmPerKev * energyKev;
            for (int guard = 0; guard < 1000; guard++)
            {
                double cos = this.ComptonCosine(energyKev);
                double x = k * Math.Sqrt(Math.Max(0.0, 0.5 * (1.0 - cos)));
                if (this.Uniform() * atom.Z <= atom.ScatteringFunction(x))
                {
                    return cos;
                }
            }

            return -1.0;
        }

        /// <summary>Косинус угла комптоновского рассеяния, метод Кана.</summary>
        double ComptonCosine(double energyKev)
        {
            double a = energyKev / ElectronMassKev;
            double a1 = 1.0 + 2.0 * a;
            for (int guard = 0; guard < 1000; guard++)
            {
                double r1 = this.Uniform(), r2 = this.Uniform(), r3 = this.Uniform();
                double ratio;
                if (r1 <= (1.0 + 2.0 * a) / (9.0 + 2.0 * a))
                {
                    ratio = 1.0 + 2.0 * a * r2;
                    if (r3 <= 4.0 * (1.0 / ratio - 1.0 / (ratio * ratio)))
                    {
                        return 1.0 - (ratio - 1.0) / a;
                    }
                }
                else
                {
                    ratio = a1 / (1.0 + 2.0 * a * r2);
                    double cos = 1.0 - (ratio - 1.0) / a;
                    if (r3 <= 0.5 * (cos * cos + 1.0 / ratio))
                    {
                        return cos;
                    }
                }
            }

            return 1.0;
        }

        void Rotate(ref double ux, ref double uy, ref double uz, double cos)
        {
            if (cos > 1.0) cos = 1.0;
            if (cos < -1.0) cos = -1.0;
            double sin = Math.Sqrt(Math.Max(0.0, 1.0 - cos * cos));
            double phi = 2.0 * Math.PI * this.Uniform();
            double cp = Math.Cos(phi), sp = Math.Sin(phi);

            double perp = Math.Sqrt(ux * ux + uy * uy);
            double nx, ny, nz;
            if (perp < 1e-8)
            {
                nx = sin * cp;
                ny = sin * sp;
                nz = cos * (uz >= 0.0 ? 1.0 : -1.0);
            }
            else
            {
                nx = ux * cos + sin * (ux * uz * cp - uy * sp) / perp;
                ny = uy * cos + sin * (uy * uz * cp + ux * sp) / perp;
                nz = uz * cos - sin * perp * cp;
            }

            double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            ux = nx / norm;
            uy = ny / norm;
            uz = nz / norm;
        }

        void Isotropic(out double ux, out double uy, out double uz)
        {
            double cos = 2.0 * this.Uniform() - 1.0;
            double sin = Math.Sqrt(Math.Max(0.0, 1.0 - cos * cos));
            double phi = 2.0 * Math.PI * this.Uniform();
            ux = sin * Math.Cos(phi);
            uy = sin * Math.Sin(phi);
            uz = cos;
        }

        // ------------------------------------------------------------------
        // Кривая
        // ------------------------------------------------------------------

        /// <summary>Эффективность в пике полного поглощения и её погрешность, доля.</summary>
        public double Efficiency(double energyKev, out double relativeError)
        {
            return this.Run(energyKev, null, 0.0, out relativeError);
        }

        /// <summary>
        /// ПОЛНАЯ эффективность: вероятность кванту оставить в кристалле хоть
        /// что-нибудь. Нужна каскадному суммированию (F1): вынос из пика
        /// определяется тем, что квант-партнёр ЗАДЕЛ кристалл, а не тем, что
        /// он поглотился целиком.
        ///
        /// Счёт аналоговый и отдельный от пиковой ветки: квант ведётся по всем
        /// областям с настоящими взаимодействиями — сколько угодно комптонов
        /// подряд, в пробе, стенках и оправе, пока не поглотится, не заденет
        /// кристалл или не уйдёт из сцены. Взвешенная проводка пиковой ветки
        /// (exp(−τ) плюс ОДНО рассеяние) полную эффективность занижает: на
        /// упоре сверка с Geant4 давала −12…−15 % — многократное рассеяние и
        /// возврат из-за кристалла там не мелочь (tools/tccfcalc2/README.md §8).
        ///
        /// Когерентное рассеяние считается прозрачным (пролёт без отклонения):
        /// пробег берётся по ослаблению БЕЗ когерентного, как и в проводке.
        /// </summary>
        public double TotalEfficiency(double energyKev, out double relativeError)
        {
            this.EnsureBuilt();
            double sum = 0.0, sum2 = 0.0;
            int n = Math.Max(1000, this.Histories);
            double limit = 40.0 * this.sphereR + 200.0;
            for (int i = 0; i < n; i++)
            {
                double x, y, z;
                this.source.Next(this, out x, out y, out z);
                double dz = this.sphereZ - z;
                double dist = Math.Sqrt(x * x + y * y + dz * dz);
                double weight = 1.0;
                double ux, uy, uz;
                if (!this.TotalFullSphere && dist > this.sphereR)
                {
                    double cosMax = Math.Sqrt(Math.Max(0.0, 1.0 - this.sphereR * this.sphereR / (dist * dist)));
                    weight = 0.5 * (1.0 - cosMax);
                    this.InCone(-x / dist, -y / dist, dz / dist, cosMax, out ux, out uy, out uz);
                }
                else
                {
                    this.Isotropic(out ux, out uy, out uz);
                }

                double e = energyKev;
                double travelled = 0.0;
                double score = 0.0;
                for (int guard = 0; guard < 400 && e > 1.0; guard++)
                {
                    Region here = this.At(x, y, z);
                    if (here != null && here.IsCrystal)
                    {
                        // Внутри кристалла: любое взаимодействие из каналов —
                        // отсчёт (когерентное в каналы не входит).
                        double photo, compton, pair;
                        this.CrystalChannels(e, out photo, out compton, out pair);
                        double mu = photo + compton + pair;
                        double path = this.CrystalPath(x, y, z, ux, uy, uz);
                        double free = mu > 0.0 ? -Math.Log(1.0 - this.Uniform()) / mu : double.MaxValue;
                        if (free < path)
                        {
                            score = weight;
                            break;
                        }

                        double advance = path + 1e-7;
                        x += ux * advance;
                        y += uy * advance;
                        z += uz * advance;
                        travelled += advance;
                        continue;
                    }

                    double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                    if (step >= double.MaxValue || travelled + step > limit)
                    {
                        break;              // ушёл из сцены
                    }

                    double muKill = here == null ? 0.0 : this.AnalogMu(here.Material, e);
                    if (muKill > 0.0)
                    {
                        double free = -Math.Log(1.0 - this.Uniform()) / muKill;
                        if (free < step)
                        {
                            x += ux * free;
                            y += uy * free;
                            z += uz * free;
                            travelled += free;
                            double incoherent = here.Material.LinearIncoherent(e);
                            double coherent = this.RayleighScatter
                                ? here.Material.LinearCoherent(e) : 0.0;
                            double channel = this.Uniform() * muKill;
                            if (channel < coherent)
                            {
                                // Когерентное: энергия та же, направление другое.
                                // Ни отсчёта, ни потери — квант летит дальше.
                                this.Rotate(ref ux, ref uy, ref uz,
                                            this.RayleighCosine(here.Material, e));
                                continue;
                            }

                            if (channel >= coherent + incoherent)
                            {
                                // Фотопоглощение или пары вне кристалла: сам
                                // фотон погиб, но электрон может занести (F1).
                                if (this.ElectronReachesCrystal(x, y, z, ux, uy, uz, e))
                                {
                                    score = weight;
                                }

                                break;
                            }

                            double cos;
                            double after = this.ComptonScatter(here.Material, e, out cos);
                            // Комптон-электрон: занос считается ДО поворота
                            // фотона — направлением электрона берётся направление
                            // налетающего кванта (см. шапку ElectronReachesCrystal).
                            if (this.ElectronReachesCrystal(x, y, z, ux, uy, uz, e - after))
                            {
                                score = weight;
                                break;
                            }

                            e = after;
                            this.Rotate(ref ux, ref uy, ref uz, cos);
                            continue;
                        }
                    }

                    double next = step + 1e-7;
                    x += ux * next;
                    y += uy * next;
                    z += uz * next;
                    travelled += next;
                }

                sum += score;
                sum2 += score * score;
            }

            double mean = sum / n;
            double variance = Math.Max(0.0, sum2 / n - mean * mean);
            relativeError = mean > 0.0 ? Math.Sqrt(variance / n) / mean * 100.0 : 0.0;
            return mean;
        }

        /// <summary>
        /// Доля пробега CSDA, которую заносимому электрону разрешено пройти по
        /// прямой: многократное рассеяние укорачивает проникновение (detour
        /// factor; практический пробег в Al на 1 МэВ ~0.7 CSDA). Вылет ИЗ
        /// кристалла считается иначе — эффективной глубиной t_eff (см.
        /// <see cref="ElectronEscapeSlope"/>); этот параметр калибруется по
        /// ε_полной Geant4 на шести энергиях (tools/tccfcalc2/README.md, §9).
        /// </summary>
        public double ElectronCarryDetour = 0.7;

        /// <summary>
        /// ε_полная: разыгрывать НАПРАВЛЕНИЯ по всей сфере (умолчание), а не
        /// конусом на объемлющую сферу детектора. Конус — сужение ради
        /// дисперсии, но он молча отсекает истории «мимо узла, рассеялся в
        /// пробе или воздухе, вернулся в кристалл» — на упоре это −4 % ε_T
        /// (измерено против Geant4, §9 журнала tccfcalc2), и именно этот
        /// хвост выглядел «остатком после заноса электронов». Ложь была в
        /// оценщике, не в физике. false — старый конус: быстрее в ~1/вес по
        /// историям, годится там, где ε_T не нужна точнее нескольких %.
        /// </summary>
        public bool TotalFullSphere = true;

        /// <summary>
        /// Занос электрона (F1): фотон провзаимодействовал ВНЕ кристалла, но
        /// выбитый электрон мог долететь до кристалла и оставить там энергию —
        /// для ПОЛНОЙ эффективности это отсчёт. Geant4 и новая ЛСРМ электроны
        /// переносят, и без заноса наша ε_T была ниже обоих на −5…−8 % с
        /// ростом по энергии — профиль пробегов электронов.
        ///
        /// Модель нарочно грубая, по одному лучу: электрон летит ПРЯМОЙ по
        /// направлению налетающего фотона (для фотоэффекта и больших передач
        /// комптона — верно в главном; электроны с малыми передачами до
        /// кристалла всё равно не долетают), в каждом слое расходуя долю
        /// СВОЕГО пробега path·ρ / R_CSDA(вещество, T); дошёл с остатком —
        /// отсчёт. Кривизна траектории спрятана в <see cref="ElectronCarryDetour"/>.
        /// Вещества слоёв — по составу (ElectronData.Match: Al, PTFE, вода +
        /// кристаллы); не опознанное вещество считается водой (в г/см² пробеги
        /// лёгких веществ близки), пустота — воздухом на таблице воды.
        /// </summary>
        bool ElectronReachesCrystal(double x, double y, double z,
                                    double ux, double uy, double uz, double energyKev)
        {
            double used;
            return this.ElectronWalkToCrystal(x, y, z, ux, uy, uz, energyKev, out used);
        }

        /// <summary>
        /// Занос электрона с ЭНЕРГИЕЙ (аналоговый континуум, F14): тот же обход,
        /// но долетевший электрон приносит остаток своей энергии, а не бит
        /// «долетел». Остаток берётся по энергетическому эквиваленту
        /// неизрасходованной доли пробега в таблице вещества кристалла — та же
        /// связка энергия-пробег, что у вылета из кристалла
        /// (<see cref="ElectronLoss"/>), только в обратную сторону. Кристалла
        /// нет в таблицах — остаток линеен по доле, грубее, но того же порядка.
        /// Свет заносного вклада взвешивается его же энергией входа.
        /// </summary>
        bool ElectronCarryDeposit(double x, double y, double z,
                                  double ux, double uy, double uz, double energyKev,
                                  out double depositKev)
        {
            depositKev = 0.0;
            double used;
            if (!this.ElectronWalkToCrystal(x, y, z, ux, uy, uz, energyKev, out used))
            {
                return false;
            }

            double left = Math.Max(0.0, 1.0 - used);
            depositKev = this.electron != null
                ? ElectronData.EnergyOfRange(this.electron,
                      left * ElectronData.RangeOf(this.electron, energyKev))
                : energyKev * left;
            if (!(depositKev > 0.0))
            {
                return false;
            }

            this.AddLight(depositKev, depositKev);
            return true;
        }

        bool ElectronWalkToCrystal(double x, double y, double z,
                                   double ux, double uy, double uz, double energyKev,
                                   out double usedFraction)
        {
            usedFraction = 0.0;
            if (energyKev < 20.0)
            {
                return false;               // пробег меньше ~10 мкм — не долетит
            }

            double used = 0.0;              // израсходованная доля пробега
            for (int guard = 0; guard < 60; guard++)
            {
                Region here = this.At(x, y, z);
                if (here != null && here.IsCrystal)
                {
                    usedFraction = used;
                    return true;
                }

                double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                if (step >= double.MaxValue)
                {
                    return false;           // ушёл из сцены
                }

                double density = here != null ? here.Material.Density : AirDensity;
                ElectronData.Material medium = here != null
                    ? this.CarryMedium(here.Material) : ElectronData.ByName("Water");
                double range = ElectronData.RangeOf(medium, energyKev)
                               * this.ElectronCarryDetour;
                used += step * density / Math.Max(range, 1e-12);
                if (used >= 1.0)
                {
                    return false;           // пробег кончился в слое
                }

                double advance = step + 1e-7;
                x += ux * advance;
                y += uy * advance;
                z += uz * advance;
            }

            return false;
        }

        const double AirDensity = 1.205e-3;             // г/см³, сухой воздух

        readonly Dictionary<GeometryMaterial, ElectronData.Material> carryCache =
            new Dictionary<GeometryMaterial, ElectronData.Material>();

        ElectronData.Material CarryMedium(GeometryMaterial material)
        {
            ElectronData.Material found;
            if (!this.carryCache.TryGetValue(material, out found))
            {
                found = ElectronData.Match(material) ?? ElectronData.ByName("Water");
                this.carryCache[material] = found;
            }

            return found;
        }

        /// <summary>
        /// Отклик детектора: распределение ПОГЛОЩЁННОЙ энергии, доля на бин, за
        /// ОДИН прогон историй.
        ///
        /// Длина массива считается ПО ТОМУ ЖЕ ПРАВИЛУ, по которому раскладка
        /// выбирает бин, поэтому последний бин — всегда пик полного поглощения.
        /// Раньше длина бралась как `ceil(E/шаг)+1`, а бин пика — как
        /// `(int)(E/шаг + 0.5)`; у энергии, не кратной шагу, это разные индексы,
        /// и последний бин оставался пустым. Ошибка проявлялась не всегда: при
        /// удачно легших энергиях узлов оба правила давали одно и то же.
        ///
        /// Зачем отдельный метод, а не сканирование порога `PeakHalfWidthKev`:
        /// сканирование даёт то же самое (условие `вылетело ≤ w` — это функция
        /// распределения), но повторяет перенос на каждый бин. При полутора
        /// тысячах бинов это полторы тысячи прогонов вместо одного.
        /// </summary>
        public double[] Response(double energyKev, double binKev, out double relativeError)
        {
            if (!(energyKev > 0.0) || !(binKev > 0.0))
            {
                throw new ArgumentOutOfRangeException("binKev");
            }

            double[] histogram = new double[PeakBin(energyKev, binKev) + 1];
            this.Run(energyKev, histogram, binKev, out relativeError);
            return histogram;
        }

        /// <summary>
        /// Каналы отклика: по какой ПРИЧИНЕ история не попала в пик полного
        /// поглощения. Порядок — номер строки в <see cref="ResponseByChannel"/>.
        /// </summary>
        public enum ResponseChannel
        {
            /// <summary>Полное поглощение: не вылетело ничего.</summary>
            Peak = 0,
            /// <summary>Утечка рассеянного кванта, электрона или тормозного.</summary>
            Compton = 1,
            /// <summary>Ушёл хотя бы один аннигиляционный квант 511 кэВ.</summary>
            Escape511 = 2,
            /// <summary>Ушёл характеристический K-рентген кристалла.</summary>
            EscapeXray = 3
        }

        /// <summary>Сколько каналов у отклика.</summary>
        public const int ResponseChannelCount = 4;

        /// <summary>
        /// Тот же отклик, разложенный по каналам исхода: `[канал][бин]`. Сумма
        /// каналов побитово равна обычному <see cref="Response"/> — история
        /// кладётся ровно в один канал, а розыгрыш от разложения не меняется.
        ///
        /// Канал выбирается НЕ по величине вылетевшей энергии, а по метке,
        /// поставленной в точке события: комптон способен унести ровно 511 кэВ
        /// случайно, и такая история села бы в чужой канал. Метки копятся по
        /// статьям расхода, и берётся та, что унесла больше, — история, где
        /// ушли и рентген, и рассеянный квант, принадлежит тому, чей вклад в
        /// недобор больше.
        /// </summary>
        public double[][] ResponseByChannel(double energyKev, double binKev, out double relativeError)
        {
            if (!(energyKev > 0.0) || !(binKev > 0.0))
            {
                throw new ArgumentOutOfRangeException("binKev");
            }

            int bins = PeakBin(energyKev, binKev) + 1;
            double[][] channels = new double[ResponseChannelCount][];
            for (int c = 0; c < ResponseChannelCount; c++)
            {
                channels[c] = new double[bins];
            }

            this.channelHistograms = channels;
            try
            {
                double[] total = new double[bins];
                this.Run(energyKev, total, binKev, out relativeError);
            }
            finally
            {
                this.channelHistograms = null;
            }

            return channels;
        }

        // Раскладка по каналам включается на время прогона. Поле, а не
        // параметр: раскладка нужна только матрице отклика, а `Run` зовут ещё
        // кривая и сканирование порога, и тащить сквозь них лишний аргумент
        // значило бы менять три подписи ради одного потребителя.
        double[][] channelHistograms;

        // Метки исхода текущей истории, кэВ. Обнуляются перед каждой.
        double lossAnnihilation;
        double lossXray;

        // Свет текущей истории в кэВ-эквивалентах кривой L(E): каждый
        // электронный вклад входит с весом L(его начальной энергии).
        // Обнуляется вместе с метками исхода.
        double lightDeposit;

        // Σ(вес·свет) по бинам поглощённой энергии — копится рядом с
        // гистограммой отклика и после прогона даёт средний свет каждого
        // бина для пересчёта в шкалу прибора. null — пересчёт выключен.
        double[] lightSum;

        /// <summary>
        /// Средний свет пика полного поглощения на кэВ энергии линии из
        /// ПОСЛЕДНЕГО прогона отклика — это и есть фотонная
        /// непропорциональность модели (1.0 — пропорционально; сверять с
        /// таблицей I Khodyuk 2012). Ноль, если пересчёт не выполнялся.
        /// </summary>
        public double LastPhotonLightScale { get; private set; }

        /// <summary>Вклад электрона начальной энергии te, осевший в кристалле.</summary>
        void AddLight(double deposited, double te)
        {
            if (this.lightYield != null && deposited > 0.0)
            {
                this.lightDeposit += deposited * this.lightYield.Of(te);
            }
        }

        /// <summary>
        /// Канал текущей истории по меткам, набранным в точках событий.
        /// Ничего не вылетело — пик; иначе побеждает статья, унёсшая больше.
        /// </summary>
        ResponseChannel ChannelOf(double escaped)
        {
            if (!(escaped > this.PeakHalfWidthKev))
            {
                return ResponseChannel.Peak;
            }

            double rest = escaped - this.lossAnnihilation - this.lossXray;
            if (this.lossAnnihilation >= this.lossXray && this.lossAnnihilation >= rest)
            {
                return ResponseChannel.Escape511;
            }

            return this.lossXray >= rest ? ResponseChannel.EscapeXray : ResponseChannel.Compton;
        }

        /// <summary>
        /// Номер бина, в который попадает полное поглощение. Тем же правилом
        /// пользуется <see cref="Deposit"/> — иначе пик оказывается не в
        /// последнем бине.
        /// </summary>
        public static int PeakBin(double energyKev, double binKev)
        {
            return (int)(energyKev / binKev + 0.5);
        }

        /// <summary>
        /// Вклад в бин поглощённой энергии. Ноль отбрасывается: история, из
        /// которой вылетело всё, отсчёта не даёт вовсе, и класть её в нулевой
        /// бин значило бы считать несобытие событием.
        /// </summary>
        /// <summary>
        /// Положить вклад в бин поглощённой энергии.
        ///
        /// В БИН ПИКА попадает только то, что пиковой ветвью СЧИТАЕТСЯ пиком —
        /// то есть недобравшее не больше <see cref="PeakHalfWidthKev"/>. Правило
        /// у обеих ветвей одно и то же и здесь записано один раз: у прямого
        /// попадания недобор равен вылетевшему (`escaped`), у рассеянного —
        /// `(E − scattered) + escaped`, и оба выражаются как `E − deposited`.
        ///
        /// Раньше бин выбирался только округлением, и вклад с потерей меньше
        /// ПОЛУБИНА попадал в пик гистограммы, хотя пиковая ветвь при допуске 0
        /// такую историю пиком не считала: пик матрицы и пик кривой расходились
        /// на величину, зависящую от шага бина (F21). Собственный комментарий
        /// рассеянной ветки при этом утверждал обратное — «в пик он не попадёт
        /// при любом исходе внутри».
        ///
        /// Недобравшее больше допуска кладётся в СОСЕДНИЙ бин, а не
        /// отбрасывается: энергия в кристалле осталась, и терять её нельзя.
        /// </summary>
        /// <summary>
        /// Годится ли история в пик полного поглощения линии. Недобор — это
        /// всё, что до энергии линии не дошло, чем бы оно ни было: у прямого
        /// попадания это вылетевшее из кристалла, у рассеянного — недобор при
        /// рассеянии ПЛЮС вылетевшее, и оба выражаются как `E − deposited`.
        ///
        /// Правило одно на все ветки и записано ЗДЕСЬ один раз: пока оно
        /// стояло в трёх местах, пик кривой и пик отклика расходились (F21), а
        /// возвращаемая эффективность значила разное на двух путях (F28).
        /// </summary>
        bool InPeak(double energyKev, double deposited)
        {
            return energyKev - deposited <= this.PeakHalfWidthKev + 1e-9;
        }

        void Deposit(double[] histogram, double binKev, double energyKev,
                     double deposited, double weight)
        {
            if (!(deposited > 0.0) || !(weight > 0.0))
            {
                return;
            }

            int bin = (int)(deposited / binKev + 0.5);
            if (bin < 0)
            {
                bin = 0;
            }

            int peak = histogram.Length - 1;
            if (bin >= peak)
            {
                bin = this.InPeak(energyKev, deposited)
                    ? peak
                    : Math.Max(0, peak - 1);
            }

            histogram[bin] += weight;
        }

        /// <summary>
        /// Общий цикл историй. `histogram == null` — считается только пик, и это
        /// в точности прежнее поведение; иначе та же история дополнительно
        /// раскладывается по бинам поглощённой энергии.
        ///
        /// ВОЗВРАЩАЕМОЕ значение значит одно и то же при любом аргументе —
        /// пиковую эффективность со ВСЕМИ вкладами, прямыми и рассеянными
        /// (F28, 08.08.2026). Раньше рассеянный вклад попадал в счёт только на
        /// пути без гистограммы, а на упоре он — заметная часть (без него
        /// полная эффективность занижалась на ~15 %), так что прогон с
        /// гистограммой молча возвращал другую величину. Сегодняшние читатели
        /// (<see cref="Response"/>, <see cref="ResponseByChannel"/>) её
        /// выбрасывают, но `relativeError` считается по тому же счёту и им
        /// нужен.
        /// </summary>
        double Run(double energyKev, double[] histogram, double binKev, out double relativeError)
        {
            this.EnsureBuilt();
            this.lightSum = histogram != null && this.lightYield != null
                ? new double[histogram.Length]
                : null;
            if (this.lightSum != null)
            {
                this.LastPhotonLightScale = 0.0;
            }

            double sum = 0.0, sum2 = 0.0;
            int n = Math.Max(1000, this.Histories);
            for (int i = 0; i < n; i++)
            {
                double x, y, z;
                this.source.Next(this, out x, out y, out z);

                // Направление разыгрывается не по всей сфере, а в конусе,
                // накрывающем детектор: иначе на дальней геометрии почти все
                // истории уходят мимо и статистика набирается впустую.
                double dz = this.sphereZ - z;
                double dist = Math.Sqrt(x * x + y * y + dz * dz);
                double weight = 1.0;
                double ux, uy, uz;
                if (dist > this.sphereR)
                {
                    double cosMax = Math.Sqrt(Math.Max(0.0, 1.0 - this.sphereR * this.sphereR / (dist * dist)));
                    weight = 0.5 * (1.0 - cosMax);
                    this.InCone(-x / dist, -y / dist, dz / dist, cosMax, out ux, out uy, out uz);
                }
                else
                {
                    this.Isotropic(out ux, out uy, out uz);
                }

                double px = x, py = y, pz = z, tau;
                double score = 0.0;
                bool reached = this.ToCrystal(ref px, ref py, ref pz, ux, uy, uz, energyKev, out tau);
                if (!reached && !this.ScoreEntranceOnly && this.SingleScatter)
                {
                    // Луч прошёл мимо кристалла. Прямого вклада нет, но квант
                    // мог рассеяться в пробе или обвязке и завернуть в
                    // кристалл — на упоре таких лучей большинство, и без этой
                    // ветки полная эффективность занижалась на ~15 %
                    // (сверка CF, tools/tccfcalc2/README.md §8). «Убивающая»
                    // толщина здесь — весь путь луча до выхода из сцены.
                    double tauMiss = this.KillDepthToExit(x, y, z, ux, uy, uz, energyKev);
                    score += this.ScatteredRun(histogram, binKev, x, y, z, ux, uy, uz,
                                               energyKev, tauMiss, weight);
                }

                if (reached)
                {
                    if (this.ScoreEntranceOnly)
                    {
                        score = weight * Math.Exp(-tau);
                    }
                    else
                    {
                        this.lossAnnihilation = 0.0;
                        this.lossXray = 0.0;
                        this.lightDeposit = 0.0;
                        double escaped = this.InCrystal(px, py, pz, ux, uy, uz, energyKev, 0);
                        if (this.InPeak(energyKev, energyKev - escaped))
                        {
                            score = weight * Math.Exp(-tau);
                        }

                        // Отклик берёт ту же историю целиком, а не один бит
                        // «попало в пик»: сколько энергии осталось в кристалле,
                        // уже посчитано, и раскладывание по бинам стоит одного
                        // сложения. Розыгрыш от этого не меняется — гистограмма
                        // не тянет ни одного случайного числа, поэтому кривая
                        // остаётся побитово прежней.
                        if (histogram != null)
                        {
                            double share = weight * Math.Exp(-tau);
                            this.Deposit(histogram, binKev, energyKev, energyKev - escaped, share);
                            this.ScoreLight(binKev, energyKev - escaped, share);
                            if (this.channelHistograms != null)
                            {
                                this.Deposit(this.channelHistograms[(int)this.ChannelOf(escaped)],
                                        binKev, energyKev, energyKev - escaped, share);
                            }
                        }
                    }

                    // Прямой вклад — это доля exp(-tau), не провзаимодействовавшая
                    // по дороге. Остаток 1 - exp(-tau) сейчас теряется целиком, а
                    // часть его — комптон на малый угол, и такой квант доходит.
                    if (!this.ScoreEntranceOnly)
                    {
                        score += this.ScatteredRun(histogram, binKev, x, y, z, ux, uy, uz,
                                                   energyKev, tau, weight);
                    }
                }

                sum += score;
                sum2 += score * score;
            }

            double mean = sum / n;
            double variance = Math.Max(0.0, sum2 / n - mean * mean);
            relativeError = mean > 0.0 ? Math.Sqrt(variance / n) / mean * 100.0 : 0.0;

            // Континуум — аналоговой веткой (физика 6): бины ниже пика
            // перезаписываются до нормировки, оба прогона на одних n.
            if (histogram != null && histogram.Length > 1 && this.AnalogContinuum)
            {
                this.AnalogContinuumRun(energyKev, histogram, binKev, n);
            }

            // Бины копят сумму весов, а величина отклика — среднее по историям,
            // ровно как возвращаемая эффективность. Без этого деления отклик
            // выходит больше единицы и вообще не вероятность.
            if (histogram != null)
            {
                for (int b = 0; b < histogram.Length; b++)
                {
                    histogram[b] /= n;
                }
            }

            if (this.channelHistograms != null)
            {
                foreach (double[] channel in this.channelHistograms)
                {
                    for (int b = 0; b < channel.Length; b++)
                    {
                        channel[b] /= n;
                    }
                }
            }

            this.RemapLightScale(energyKev, binKev, histogram, n);
            return mean;
        }

        /// <summary>
        /// Аналоговый прогон континуума (<see cref="AnalogContinuum"/>): свои
        /// n историй полной сферой направлений и настоящими взаимодействиями во
        /// всех областях — как в <see cref="TotalEfficiency"/>, но со счётом
        /// ПОГЛОЩЁННОЙ энергии вместо бита «задел». Вклады одной истории
        /// суммируются: занос комптон-электрона и его же рассеянный квант —
        /// одно событие в детекторе, Geant4 складывает их так же.
        ///
        /// Судьбу кванта в кристалле решает <see cref="InCrystal"/> от точки
        /// входа: пролёт без взаимодействия он воспроизводит сам (его первый же
        /// розыгрыш пробега — та самая exp(−μ·путь)), и пролетевший квант
        /// продолжает путь с дальней грани — возврат рассеянием из-за кристалла
        /// был заметной частью недобора полос малых сумм. Квант, вылетевший из
        /// кристалла ПОСЛЕ вклада, дальше не ведётся (повторный залёт после
        /// вылета не смоделирован — как и во взвешенной ветке); аннигиляционные
        /// кванты от пар ВНЕ кристалла не разыгрываются (фотон гибнет, занос
        /// достаётся электронам) — обе оговорки записаны в журнале.
        ///
        /// Бины [0, пик) гистограммы, каналов и света перезаписываются суммами
        /// весов этого прогона; вклад, округлившийся в бин пика, отбрасывается —
        /// пик остаётся за взвешенной оценкой, и классы не пересекаются.
        /// </summary>
        void AnalogContinuumRun(double energyKev, double[] histogram, double binKev, int n)
        {
            int peak = histogram.Length - 1;
            double[] hist = new double[histogram.Length];
            double[][] channels = null;
            if (this.channelHistograms != null)
            {
                channels = new double[ResponseChannelCount][];
                for (int c = 0; c < ResponseChannelCount; c++)
                {
                    channels[c] = new double[histogram.Length];
                }
            }

            double[] light = this.lightSum != null ? new double[histogram.Length] : null;
            double limit = 40.0 * this.sphereR + 200.0;
            int scored = 0;
            for (int i = 0; i < n; i++)
            {
                double x, y, z;
                this.source.Next(this, out x, out y, out z);
                double ux, uy, uz;
                this.Isotropic(out ux, out uy, out uz);

                this.lossAnnihilation = 0.0;
                this.lossXray = 0.0;
                this.lightDeposit = 0.0;

                double e = energyKev;
                double deposited = 0.0;
                double travelled = 0.0;
                for (int guard = 0; guard < 400 && e > 1.0; guard++)
                {
                    Region here = this.At(x, y, z);
                    if (here != null && here.IsCrystal)
                    {
                        double escaped = this.InCrystal(x, y, z, ux, uy, uz, e, 0);
                        if (e - escaped > 1e-9)
                        {
                            deposited += e - escaped;
                            break;
                        }

                        // пролетел насквозь без вклада — с дальней грани дальше
                        double through = this.CrystalPath(x, y, z, ux, uy, uz) + 1e-7;
                        x += ux * through;
                        y += uy * through;
                        z += uz * through;
                        travelled += through;
                        continue;
                    }

                    double step = this.StepToBoundary(x, y, z, ux, uy, uz);
                    if (step >= double.MaxValue || travelled + step > limit)
                    {
                        break;              // ушёл из сцены
                    }

                    double muKill = here == null ? 0.0 : this.AnalogMu(here.Material, e);
                    if (muKill > 0.0)
                    {
                        double free = -Math.Log(1.0 - this.Uniform()) / muKill;
                        if (free < step)
                        {
                            x += ux * free;
                            y += uy * free;
                            z += uz * free;
                            travelled += free;
                            double incoherent = here.Material.LinearIncoherent(e);
                            double coherent = this.RayleighScatter
                                ? here.Material.LinearCoherent(e) : 0.0;
                            double carried;
                            double channel = this.Uniform() * muKill;
                            if (channel < coherent)
                            {
                                // Когерентное: только поворот, энергия та же.
                                this.Rotate(ref ux, ref uy, ref uz,
                                            this.RayleighCosine(here.Material, e));
                                continue;
                            }

                            if (channel >= coherent + incoherent)
                            {
                                // Фотопоглощение или пары вне кристалла: фотон
                                // погиб, электрон может донести остаток (F1).
                                if (this.ElectronCarryDeposit(x, y, z, ux, uy, uz, e, out carried))
                                {
                                    deposited += carried;
                                }

                                break;
                            }

                            double cos;
                            double after = this.ComptonScatter(here.Material, e, out cos);
                            // Занос комптон-электрона — ДО поворота фотона (см.
                            // шапку ElectronReachesCrystal); фотон летит дальше.
                            if (this.ElectronCarryDeposit(x, y, z, ux, uy, uz, e - after, out carried))
                            {
                                deposited += carried;
                            }

                            e = after;
                            this.Rotate(ref ux, ref uy, ref uz, cos);
                            continue;
                        }
                    }

                    double next = step + 1e-7;
                    x += ux * next;
                    y += uy * next;
                    z += uz * next;
                    travelled += next;
                }

                if (!(deposited > 0.0))
                {
                    continue;
                }

                int bin = (int)(deposited / binKev + 0.5);
                if (bin >= peak)
                {
                    continue;               // бин пика — за взвешенной оценкой
                }

                hist[bin] += 1.0;
                scored++;
                if (light != null)
                {
                    light[bin] += this.lightDeposit;
                }

                if (channels != null)
                {
                    ResponseChannel channel = this.ChannelOf(energyKev - deposited);
                    if (channel == ResponseChannel.Peak)
                    {
                        channel = ResponseChannel.Compton;
                    }

                    channels[(int)channel][bin] += 1.0;
                }
            }

            // Событий в континууме строки — столько же, сколько независимых
            // историй его набрало: ошибка интеграла 1/√N (F23).
            this.LastContinuumRelativeError = scored > 0 ? 100.0 / Math.Sqrt(scored) : 100.0;

            for (int b = 0; b < peak; b++)
            {
                histogram[b] = hist[b];
                if (light != null)
                {
                    this.lightSum[b] = light[b];
                }

                if (channels != null)
                {
                    for (int c = 0; c < ResponseChannelCount; c++)
                    {
                        this.channelHistograms[c][b] = channels[c][b];
                    }
                }
            }
        }

        /// <summary>
        /// Свет текущей истории — в копилку бина её ПОГЛОЩЁННОЙ энергии. Бин
        /// считается тем же правилом, что в <see cref="Deposit"/>, иначе
        /// средний свет достанется чужому бину.
        /// </summary>
        void ScoreLight(double binKev, double deposited, double weight)
        {
            if (this.lightSum == null || !(deposited > 0.0) || !(weight > 0.0))
            {
                return;
            }

            int bin = (int)(deposited / binKev + 0.5);
            if (bin < 0)
            {
                bin = 0;
            }

            if (bin >= this.lightSum.Length)
            {
                bin = this.lightSum.Length - 1;
            }

            this.lightSum[bin] += weight * this.lightDeposit;
        }

        /// <summary>
        /// Пересчёт отклика из шкалы поглощённой энергии в шкалу прибора по
        /// среднему свету каждого бина (<see cref="LightNonproportionality"/>).
        ///
        /// Якорь — пик полного поглощения: его средний свет объявляется
        /// равным его же бину, как у прибора, откалиброванного по пикам.
        /// Остальные бины встают по отношению своего среднего света к якорю,
        /// вес делится между двумя соседними бинами линейно. Каналы отклика
        /// переносятся ТОЙ ЖЕ картой, что и сумма, — их сумма остаётся равной
        /// полному отклику побитово.
        ///
        /// Пересчёт детерминирован (ни одного случайного числа) и работает по
        /// уже посчитанной гистограмме, поэтому с выключенным ключом или без
        /// кривой света поведение прежнее до бита. Побочная точность: средний
        /// свет хранит положение событий ВНУТРИ бина, так что даже
        /// тождественная кривая слегка уточняет позиции — это не ошибка.
        /// </summary>
        void RemapLightScale(double energyKev, double binKev, double[] histogram, int n)
        {
            double[] light = this.lightSum;
            this.lightSum = null;
            if (light == null || histogram == null)
            {
                return;
            }

            int peak = histogram.Length - 1;
            if (peak < 1)
            {
                return;         // однобинный отклик двигать некуда
            }

            double peakWeight = histogram[peak] * n;
            if (!(peakWeight > 0.0) || !(light[peak] > 0.0))
            {
                return;         // пика нет — якоря нет, шкала остаётся энергетической
            }

            // Свет полного поглощения на кэВ линии — фотонная
            // непропорциональность модели, наружу для сверки с измерениями.
            double anchorPerKev = light[peak] / peakWeight / energyKev;
            this.LastPhotonLightScale = anchorPerKev;

            // Внутренний якорь берётся к ЦЕНТРУ пикового бина, а не к энергии
            // линии: бин пика обязан остаться последним, а энергия линии не
            // кратна шагу, и якорь по ней увёл бы половину пика в соседний бин.
            double anchorPerBin = light[peak] / peakWeight / (peak * binKev);
            int[] lowBin = new int[histogram.Length];
            double[] lowShare = new double[histogram.Length];
            for (int b = 0; b <= peak; b++)
            {
                double w = histogram[b] * n;
                double index = b;
                if (w > 0.0 && light[b] > 0.0)
                {
                    index = light[b] / w / anchorPerBin / binKev;
                }

                if (index <= 0.0)
                {
                    lowBin[b] = 0;
                    lowShare[b] = 1.0;
                    continue;
                }

                if (index >= peak)
                {
                    lowBin[b] = peak;
                    lowShare[b] = 1.0;
                    continue;
                }

                int lo = (int)index;
                lowBin[b] = lo;
                lowShare[b] = 1.0 - (index - lo);
            }

            ApplyLightMap(histogram, lowBin, lowShare);
            if (this.channelHistograms != null)
            {
                foreach (double[] channel in this.channelHistograms)
                {
                    ApplyLightMap(channel, lowBin, lowShare);
                }
            }
        }

        static void ApplyLightMap(double[] histogram, int[] lowBin, double[] lowShare)
        {
            double[] moved = new double[histogram.Length];
            for (int b = 0; b < histogram.Length; b++)
            {
                double w = histogram[b];
                if (!(w > 0.0))
                {
                    continue;
                }

                int lo = lowBin[b];
                double share = lowShare[b];
                moved[lo] += w * share;
                if (share < 1.0)
                {
                    moved[lo + 1] += w * (1.0 - share);
                }
            }

            Array.Copy(moved, histogram, histogram.Length);
        }

        void InCone(double ax, double ay, double az, double cosMax,
                    out double ux, out double uy, out double uz)
        {
            double cos = cosMax + (1.0 - cosMax) * this.Uniform();
            ux = ax;
            uy = ay;
            uz = az;
            this.Rotate(ref ux, ref uy, ref uz, cos);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Начать новый поток случайных чисел с заданного состояния.
        ///
        /// Нужно для счёта точек кривой в несколько потоков: точки считаются
        /// одновременно, и если бы они брали числа из ОДНОЙ последовательности,
        /// результат зависел бы от того, кто успел раньше. Своё состояние на
        /// точку делает её ответом на вопрос «зерно и номер точки», а не «зерно,
        /// номер и порядок выполнения».
        ///
        /// Первые выдачи отбрасываются: xorshift с бедным по битам состоянием
        /// первые несколько шагов выдаёт заметно связанные числа.
        /// </summary>
        public void ResetStream(ulong seed)
        {
            this.state = seed | 1UL;
            for (int i = 0; i < 16; i++)
            {
                this.Uniform();
            }
        }

        double Uniform()
        {
            // xorshift64*: воспроизводимо и без зависимостей
            if (this.state == 0UL)
            {
                this.state = (ulong)this.Seed | 1UL;
            }

            this.state ^= this.state >> 12;
            this.state ^= this.state << 25;
            this.state ^= this.state >> 27;
            ulong r = this.state * 2685821657736338717UL;
            return ((r >> 11) + 0.5) * (1.0 / 9007199254740992.0);
        }

        public string DescribeScene()
        {
            this.EnsureBuilt();
            List<string> parts = new List<string>();
            foreach (Region r in this.regions)
            {
                // У бруса радиусов нет вовсе, и печатать их нулями — врать в
                // журнале прогона, который теперь видит пользователь.
                string shape = r.IsBox
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:F2}x{1:F2}", 2.0 * r.AX, 2.0 * r.AY)
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "r[{0:F2}..{1:F2}]", r.RIn, r.ROut);
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} {1} z[{2:F2}..{3:F2}]{4}",
                    r.Material.Name, shape, r.ZMin, r.ZMax, r.IsCrystal ? " *" : ""));
            }

            // Единица названа явно: сцена строится в сантиметрах, а геометрию
            // выше в том же журнале печатают в миллиметрах, и два ряда чисел,
            // отличающихся вдесятеро, без подписи читаются как ошибка.
            return "cm: " + string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Машинный дамп сцены для внешнего арбитра (`tools/g4cf --scene`):
        /// материалы составом (Z:массовая доля) и плотностью, области в
        /// порядке поиска (первая победившая — как в <see cref="At"/>) и
        /// источник. Всё в сантиметрах, ось — ось сцены. Формат построчный:
        ///
        ///     SCENE
        ///     mat m0 плотность Z:доля Z:доля ...
        ///     region tub|box m0 (rIn rOut | ax ay) z0 z1 crystal|-
        ///     source point z | cyl r z0 z1 | box ax ay z0 z1
        ///            | marinelli rIn rOut z0 z1 zCap
        ///     END
        ///
        /// Перекрытия областей здесь НЕ разрешаются: наша сцена ищет «первую
        /// победившую», у Geant4 сёстры обязаны не пересекаться — проверка и
        /// отказ на его стороне.
        /// </summary>
        public string DumpScene()
        {
            this.EnsureBuilt();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string> { "SCENE" };
            var materialId = new Dictionary<GeometryMaterial, string>();
            foreach (Region r in this.regions)
            {
                if (!materialId.ContainsKey(r.Material))
                {
                    string id = "m" + materialId.Count.ToString(ci);
                    materialId[r.Material] = id;
                    var comp = new List<string>();
                    foreach (KeyValuePair<int, double> f in r.Material.Fractions)
                    {
                        if (f.Value > 0.0)
                        {
                            comp.Add(string.Format(ci, "{0}:{1:R}", f.Key, f.Value));
                        }
                    }

                    lines.Add(string.Format(ci, "mat {0} {1:R} {2}", id,
                                            r.Material.Density,
                                            string.Join(" ", comp.ToArray())));
                }
            }

            foreach (Region r in this.regions)
            {
                lines.Add(r.IsBox
                    ? string.Format(ci, "region box {0} {1:R} {2:R} {3:R} {4:R} {5}",
                                    materialId[r.Material], r.AX, r.AY, r.ZMin, r.ZMax,
                                    r.IsCrystal ? "crystal" : "-")
                    : string.Format(ci, "region tub {0} {1:R} {2:R} {3:R} {4:R} {5}",
                                    materialId[r.Material], r.RIn, r.ROut, r.ZMin, r.ZMax,
                                    r.IsCrystal ? "crystal" : "-"));
            }

            lines.Add(this.source.Describe());
            lines.Add("END");
            return string.Join("\n", lines.ToArray());
        }
    }
}
