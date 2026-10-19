using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Один кандидат в состав: родитель, найденный поиском пиков, и всё, чем
    /// он себя подтвердил или не подтвердил.
    ///
    /// Держится и у отвергнутых тоже: «родителя не было» и «родитель был, но
    /// не набрал» с виду одно и то же, а разбираются по-разному — ровно та же
    /// причина, по которой заведён <see cref="FsaSampleLibrary.Report"/>.
    /// </summary>
    public sealed class FsaParentEvidence
    {
        /// <summary>`nucid` корня: «232TH», «137CS».</summary>
        public string Nucid = "";

        /// <summary>Подпись корня: «Th-232», «Cs-137».</summary>
        public string Name = "";

        /// <summary>У родителя есть дочерние — объявлять его надо рядом.</summary>
        public bool IsChain;

        /// <summary>Пиков, подписанных этим родителем при поиске.</summary>
        public int LabelledPeaks;

        /// <summary>Групп линий, которые модель ждала увидеть.</summary>
        public int Expected;

        /// <summary>Из них подтверждённых пиком.</summary>
        public int Matched;

        /// <summary>Доля подтверждённых: <see cref="Matched"/> / <see cref="Expected"/>.</summary>
        public double Coverage;

        /// <summary>
        /// (`S65`, `A205`) ПОДЦЕПОЧКА — тот кусок ряда, который в пробе есть на
        /// самом деле, и её собственная доля. Пусто и NaN — ряд не обрывался
        /// (или обрыв не искали вовсе).
        ///
        /// ⚠ Имя поля осталось прежним нарочно: у `S65` этот кусок всегда был
        /// ГОЛОВОЙ ряда, с 06.09.2026 он может начинаться и посреди ряда
        /// (`A205`, точечный Th-228 — от `Ra-224` вниз). Правило одно, и поле
        /// одно; развести их значило бы завести вторую копию правила.
        /// </summary>
        public readonly List<string> Head = new List<string>();

        /// <summary>Доля по подцепочке: <see cref="HeadMatched"/> / <see cref="HeadExpected"/>.</summary>
        public double HeadCoverage = double.NaN;

        /// <summary>Подтверждено и ожидалось В ПОДЦЕПОЧКЕ.</summary>
        public int HeadMatched, HeadExpected;

        /// <summary>
        /// Где подцепочка обрывается СВЕРХУ: головы отрезков ряда, которые её
        /// питают и в неё не взяты (`AMBER236`, через «+»). Пусто — подцепочка
        /// начинается с корня (урановое стекло).
        /// </summary>
        public string Cut = "";

        /// <summary>
        /// (`A205`) Где подцепочка обрывается СНИЗУ: головы отрезков, которые она
        /// питает и которые в неё не взяты. Пусто — подцепочка доходит до конца
        /// ряда (случай точечного Th-228).
        /// </summary>
        public string CutBelow = "";

        /// <summary>
        /// (`A205`) РАСКЛАД ПО ЧЛЕНАМ РЯДА: по строке на члена, «212PB@7 2/3» —
        /// `nucid`, глубина от корня, подтверждено / ожидалось.
        ///
        /// Заведено потому, что отказ «доля ниже порога» сам себя не объясняет:
        /// у точечного Th-228 (`A205`) Th-232 набирает 23 % и человек не видит,
        /// ЧТО именно не подтвердилось — верх ряда, низ или всё вперемешку.
        /// Разбор обрыва (<see cref="FsaChainCut"/>) считает эти же числа, и
        /// печатать их отдельной копией было бы вторым правилом о том, что
        /// такое «член подтверждён».
        /// </summary>
        public readonly List<string> ByMember = new List<string>();

        /// <summary>
        /// (`AMBER236`) РАСКЛАД ПО ОТРЕЗКАМ оборванного ряда: по строке на отрезок,
        /// «226RA[226RA+222RN+…] 1/26 нет» — голова, члены, подтверждено / ожидалось
        /// и приговор: «есть» (держит порог), «нет» (против), «молчит» (сказать
        /// нечего). Пусто — ряд по отрезкам не судили.
        /// </summary>
        public readonly List<string> BySegment = new List<string>();

        /// <summary>Родитель взят в состав.</summary>
        public bool Accepted;

        /// <summary>Почему принят или отвергнут — человеку и в журнал.</summary>
        public string Why = "";

        /// <summary>
        /// (`AMBER221`) То же решение, что <see cref="Why"/>, но признаком: строку
        /// отчёта FSA о нуклиде сета, не вошедшем в состав, окно собирает из своих
        /// ресурсов на двух языках, а не из русского текста журнала.
        /// </summary>
        public FsaParentVerdict Verdict;

        public override string ToString()
        {
            var text = new StringBuilder();
            // Сырой счёт подписанных пиков печатается рядом с долей нарочно:
            // именно он был первым кандидатом в критерий и именно он не годится
            // (`S57`). Видя оба числа вместе, читатель видит и почему.
            text.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:P0} ({2}/{3}, пиков {4})",
                              this.Name, this.Coverage, this.Matched, this.Expected,
                              this.LabelledPeaks);
            if (!double.IsNaN(this.HeadCoverage))
            {
                text.AppendFormat(CultureInfo.InvariantCulture,
                                  ", подцепочка {0:P0} ({1}/{2}, обрыв сверху {3}, снизу {4})",
                                  this.HeadCoverage, this.HeadMatched, this.HeadExpected,
                                  this.Cut.Length > 0 ? this.Cut : "—",
                                  this.CutBelow.Length > 0 ? this.CutBelow : "—");
            }

            if (this.Why.Length > 0)
            {
                text.Append(" — ").Append(this.Why);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// (`AMBER221`) Чем кончился разбор кандидата — признаком, парным к
    /// <see cref="FsaParentEvidence.Why"/>. <see cref="None"/> — кандидата не
    /// судили (разбор не дошёл до приёма).
    /// </summary>
    public enum FsaParentVerdict
    {
        None = 0,

        /// <summary>Взят в состав.</summary>
        Accepted,

        /// <summary>Ни одной линии в рабочем окне (полоса поиска, кривая эффективности).</summary>
        NoLines,

        /// <summary>Подписанные им пики не подтвердили ни одной его группы линий.</summary>
        NotConfirmed,

        /// <summary>Доля подтверждённых групп ниже порога.</summary>
        BelowCoverage,

        /// <summary>Своей структуры не приносит — её объясняет уже принятый.</summary>
        NotNovel,

        /// <summary>Принят, но входит в ряд принятого старшего и поглощён им.</summary>
        InChain
    }

    /// <summary>
    /// (`AMBER221`, П247 12.10.2026) Чем пик подтверждает группу линий родителя
    /// (<see cref="FsaCompositionInference.LabelMatch"/>).
    /// </summary>
    public enum FsaLabelMatch
    {
        /// <summary>Положение пика в ±0.75 ПШПВ от центра группы — до 12.10.2026.</summary>
        Position = 0,

        /// <summary>
        /// Линия подписи (энергия определения) против ЛЮБОЙ линии группы. Отвергнуто
        /// замером П247: у точечных Th-228 корпуса пик Tl-208 860.6 (линия скрыта в
        /// поставочной библиотеке) подписан слабой линией Ac-228 835 и подтвердил
        /// группу Ac-228 — подцепочка `A205` сменилась всем рядом.
        /// </summary>
        LabelLine = 1,

        /// <summary>
        /// Умолчание: положение пика — как прежде, ЛИБО линия подписи — если она
        /// ГЛАВНАЯ линия группы (ближайшая к подписи линия группы — её центр).
        /// Сдвиг шкалы (K-40 на 1397 вместо 1460.8) судит допуск подписи; подпись
        /// второстепенной линией группы ничего сверх прежнего не подтверждает.
        /// </summary>
        PositionOrMainLine = 2
    }

    /// <summary>
    /// (`AMBER221`) Родитель АКТИВНОГО СЕТА, не вошедший в состав разбора, и почему.
    /// Пусто в <see cref="FsaResult.SetOmissions"/> — все родители сета в составе.
    /// </summary>
    public sealed class FsaSetOmission
    {
        /// <summary>Подпись родителя: «K-40», «Th-232».</summary>
        public string Nuclide = "";

        /// <summary>
        /// Причина. <see cref="FsaParentVerdict.None"/> — ни один пик им не
        /// подписан, и кандидатом он не становился вовсе.
        /// </summary>
        public FsaParentVerdict Reason;

        /// <summary>Подтверждено и ожидалось групп линий (как в разборе кандидата).</summary>
        public int Matched, Expected;

        /// <summary>Доля, по которой судили (с подцепочкой, если она была).</summary>
        public double Coverage;

        /// <summary>Порог доли, с которым шёл вывод.</summary>
        public double Threshold;
    }

    /// <summary>
    /// Вывод состава пробы ИЗ ПОИСКА ПИКОВ — исполнение правила Amber
    /// 17.08.2026 (`S57`): «если поиск набрал весомое число пиков одного
    /// родителя — идём в базу и берём для FSA весь изотопный состав этого
    /// родителя».
    ///
    /// ПАРНАЯ СТРОКА К `S56`, И ГРАНИЦА МЕЖДУ НИМИ РОВНО ЗДЕСЬ. Постулат
    /// говорит: у каждого спектра своя база линий, привязанная к снятому
    /// нуклиду. В корпусе снятое известно из `manifest.csv`, и состав просто
    /// объявляется (<see cref="FsaSampleSpec"/>). В поле манифеста нет, и
    /// объявить состав некому — его надо ВЫВЕСТИ. Этот класс и есть вывод: он
    /// не собирает библиотеку сам, а заполняет тот же самый
    /// <see cref="FsaSampleSpec"/>, который дальше собирает
    /// <see cref="FsaSampleLibrary"/>. Второй сборки библиотеки в проекте нет и
    /// заводить её нельзя — сузив библиотеку двумя разными способами, мы
    /// получили бы два разных recall при одном имени.
    ///
    /// ОТКУДА БЕРУТСЯ КАНДИДАТЫ. Из подписей найденных пиков, то есть из общей
    /// библиотеки прибора (`NuclideDefinition`), — и это НЕ противоречит
    /// указанию Amber «источник — базы, а не конфиг». Конфиг здесь отвечает на
    /// вопрос «на что вообще посмотреть», и только на него: имя родителя из
    /// <see cref="NuclideDefinition.Chain"/> — это всё, что от него берётся.
    /// Состав ряда, линии, выходы, рентген — из `nucdb` и `matdb`, как и у
    /// `S56`. То есть общая библиотека перестаёт быть тем, что предъявляется
    /// спектру, и становится списком гипотез, каждая из которых обязана себя
    /// оплатить.
    ///
    /// ⛔ КРУГОВАЯ ЗАВИСИМОСТЬ ЗДЕСЬ НАСТОЯЩАЯ, И РАЗРЫВАЕТ ЕЁ ЗНАМЕНАТЕЛЬ, А НЕ ЧИСЛИТЕЛЬ.
    /// Поиск пиков задаёт библиотеку, библиотека задаёт разбор: один неверно
    /// подписанный пик мог бы утащить за собой весь состав. Развязка в том, ЧТО
    /// СРАВНИВАЕТСЯ. Числитель — пики, подписанные этим родителем, то есть
    /// буквально «весомое число пиков одного родителя» из правила Amber.
    /// Знаменатель — сколько таких пиков ДОЛЖНО было бы найтись, будь родитель
    /// в пробе, и считается он не по подписям, а по базе и по разрешению
    /// прибора. Ошибочная подпись поднимает числитель на единицу и не трогает
    /// знаменателя вовсе — фантом получает долю 1/16, а не состав.
    ///
    /// ⚠ Обратное решение — «подтверждать положением пика, не глядя на
    /// подпись» — выглядит строже и было отвергнуто ИЗМЕРЕНИЕМ 18.08.2026.
    /// На сцинтилляторе оно не работает: при ПШПВ 6.7 % у ASN16 окно
    /// соответствия на 344 кэВ выходит в 23 кэВ, и богатый линиями чужак
    /// попадает в чужую структуру всюду — Eu-152 набрал в ториевом спектре
    /// 15 групп из 16, Ag-108m в чароите 3 из 4, Am-241 в урановом стекле
    /// 8 из 15 и притащил за собой ряд нептуния тринадцатью образами.
    ///
    /// ⚠ ЧТО ЭТОТ КЛАСС НЕ ДЕЛАЕТ И ДЕЛАТЬ НЕ ДОЛЖЕН — приписывать линии.
    /// Своя база нуклида НИКОГДА не объяснит всего, что видно в спектре, и это
    /// не её дефект: часть линий рождается ПЕРЕНОСОМ, а не распадом
    /// (флуоресценция пробы, вылет K-рентгена кристалла), и в
    /// `decay_radiations` их нет и быть не может. Их территория —
    /// <see cref="FsaSampleSpec.AtomicXray"/> и матрица отклика (`F27`,
    /// физика 12). Увидев остаток на 20…65 кэВ, надо смотреть, есть ли матрица
    /// и свежа ли она, а не дописывать выдуманные линии.
    /// </summary>
    /// <summary>
    /// (`S65`, `A205`) Что делать с ОБОРВАННЫМ рядом — рядом, у которого в
    /// пробе есть не весь ряд, а его кусок.
    ///
    /// ✅ РЕШЕНИЕ AMBER ЕСТЬ, И УМОЛЧАНИЕ ТЕПЕРЬ <see cref="Only"/>. 25.08.2026
    /// по `S65`: «`Only`-ряд по голове цепочки РАЗРЕШЁН — считать долю по
    /// подцепочке и объявлять `Only`-ряд, если голова прошла, а хвост нет».
    /// 06.09.2026 по `A205`: то же правило распространено на ряд, оборванный
    /// СВЕРХУ (точечный Th-228), — «тем же правилом, что уже принято для
    /// `S65`, чтобы на два одинаковых случая было одно правило».
    ///
    /// <see cref="Whole"/> — то, что было до 06.09.2026: обрыв не ищется вовсе.
    /// <see cref="Criterion"/> не выходит за букву правила Amber «набрал
    /// родитель — берём ВЕСЬ его состав» (меняется только ЗНАМЕНАТЕЛЬ доли, а в
    /// библиотеку по-прежнему идёт весь ряд); <see cref="Only"/> выходит, потому
    /// что предъявляет фиту ОГРАНИЧЕННЫЙ ряд, — и именно это решено.
    ///
    /// ⚠ Оба прежних состояния оставлены КЛЮЧОМ, а не удалены: ими меряется
    /// цена правки, и без них плечо «как было» пришлось бы собирать отдельным
    /// двоичным файлом.
    /// </summary>
    public enum FsaChainCut
    {
        /// <summary>Ряд целиком: доля считается по всем его членам.</summary>
        Whole = 0,

        /// <summary>Доля считается ещё и по подцепочке; в состав идёт весь ряд.</summary>
        Criterion = 1,

        /// <summary>Доля по подцепочке, и в состав идёт ОНА (`FsaSampleChain.Only`).</summary>
        Only = 2
    }

    public static class FsaCompositionInference
    {
        /// <summary>
        /// Наименьшая доля ожидаемо-различимых групп, при которой родитель
        /// берётся в состав. (Второго пути в состав — «якоря» — с 12.09.2026
        /// нет, `S66`; см. <see cref="Accept"/>.)
        ///
        /// ⛔ ЭТО ЧИСЛО ВЫВЕДЕНО ЗАМЕРОМ ПО КОРПУСУ, а не назначено, — того
        /// требует сама строка `S57`. Прогонялка — `CorpusFsaProbe --lib=infer
        /// --infer-theta=`, мерка — `tools/pie/score.py --members --part=`;
        /// разбор и таблица развёртки в `tools/CORPUS/README.md`.
        ///
        /// Сырой счёт пиков на это место не годится, и в строке названы обе
        /// причины: он смещён в пользу богатых линиями родителей (у Th-232 их
        /// сорок пять выше 1 %, у Cs-137 четыре, и порог «три пика» отдаёт
        /// торий всегда) и несравним поперёк корпуса (ПШПВ от 0.20 % у
        /// германия до 13.3 % у обсидиана). Здесь считается ДОЛЯ, знаменатель
        /// у неё — то, что при этой статистике и этом разрешении ВООБЩЕ можно
        /// было увидеть (см. <see cref="Score"/>), и обе беды снимаются
        /// знаменателем, а не порогом.
        ///
        /// **0.30 — КОЛЕНО развёртки, снятой 18.08.2026 на всём корпусе-129.**
        /// Ниже него фантомы растут быстро и без прибавки recall (0.25 даёт те
        /// же 66 %/70 %, но 8 и 7 фантомов против 5 и 4); выше — recall падает,
        /// а фантомов почти не убывает (0.35: 64 %/68 % при 4 и 3). Таблица
        /// целиком — в `tools/CORPUS/README.md`.
        /// </summary>
        public const double DefaultCoverage = 0.30;

        /// <summary>
        /// (`A205`) Как вывод состава обходится с ОБОРВАННЫМ рядом, когда его
        /// об этом не спросили, — то есть в приложении.
        ///
        /// ⛔ Умолчание сменено 06.09.2026 решением Amber (`A205`), и цена
        /// названа в строке: с <see cref="FsaChainCut.Whole"/> человек,
        /// поставивший источник состава «Из NucBase» на точечный Th-228,
        /// получал ПУСТОЙ разбор — ни одного родителя, библиотека из одного
        /// рентгена свинца, невязка равна спектру.
        /// </summary>
        public const FsaChainCut DefaultCut = FsaChainCut.Only;

        /// <summary>
        /// Наименьшая полуширина окна соответствия в долях энергии — на случай,
        /// когда калибровки ПШПВ нет или она вырождена.
        ///
        /// ⛔ Допуск подписи (`Tolerance`) окном ПОЛОЖЕНИЯ сюда НЕ ГОДИТСЯ, и это
        /// выяснилось первым же прогоном. Он задан в ПРОЦЕНТАХ энергии и в
        /// поставочных конфигурациях равен 10…11 — то есть ±260 кэВ на линии 2614.
        /// С таким окном подтверждалось всё подряд: фантомный Eu-152 набирал в
        /// ториевом спектре 15 групп из 16. И снова 12.10.2026 (`AMBER221`, П247):
        /// окно-допуск вокруг ПОЛОЖЕНИЯ пика дало у двух точечных Th-228 корпуса
        /// пику ряда подтвердить чужую группу Ac-228 и сняло подцепочку `A205`.
        /// Допуск судит сдвиг пика от линии ОДИН раз — при подписи; здесь
        /// засчитывается сама линия подписи, если она главная в группе (<see cref="LabelMatch"/>).
        /// </summary>
        const double MinWindowFraction = 0.005;

        /// <summary>
        /// (`AMBER221`, решение Amber 12.10.2026 вопросником, дословно: «Ошибку
        /// следует брать из Tolerance Peak Detection») СДВИГ ПИКА ОТ ЛИНИИ СУДИТ
        /// ДОПУСК ПОИСКА ПИКОВ — один раз, при подписи. Пик подтверждает группу
        /// линий родителя положением, как прежде, ЛИБО своей подписью — когда пик
        /// подписан ГЛАВНОЙ линией группы (её истинная энергия из определения).
        ///
        /// ⛔ Прежнее окно (положение пика в ±0.75 ПШПВ от группы) было СТРОЖЕ
        /// ПОДПИСИ, и в этом весь дефект: подпись ставит пик в пределах допуска и
        /// 1.5 ПШПВ (<c>PeakDetector.MatchNuclides</c>), а вывод потом не признавал
        /// подписанный им же пик. Измерено на «Atom Spectra 80x80» 12.10.2026:
        /// калибровка энергии провисает на −5 % у 1460 кэВ, настоящий K-40 показан
        /// на 1397.1 и подписан K-40 (допуск 20 %), окно вывода — ±51.7 кэВ, и K-40
        /// молча выпадал из состава.
        ///
        /// ⚠ Две редакции отвергнуты замером на корпусе (П247, 136 спектров, одним
        /// двоичным файлом; корни состава не сменились ни у одного ни в одной):
        ///   * окно-допуск вокруг ПОЛОЖЕНИЯ пика — у `G1S16_Th228_P5` и
        ///     `G1S24_Th228_P25` пик ряда подтвердил ЧУЖУЮ группу Ac-228, доля
        ///     целого ряда перешла порог, и подцепочка точечного Th-228 (`A205`)
        ///     сменилась всем рядом;
        ///   * линия подписи против ЛЮБОЙ линии группы — тот же итог на тех же
        ///     двух: пик 864.0 (это Tl-208 860.6, а его линия в поставочной
        ///     библиотеке скрыта) подписан слабой линией Ac-228 835 и подтвердил
        ///     группу Ac-228 (Ac-228 2/10 → 3/10).
        /// Подпись ГЛАВНОЙ линией группы такой ошибки не даёт: подпись
        /// второстепенной ничего сверх прежнего не подтверждает.
        ///
        /// Сравниваются две ИСТИННЫЕ энергии, поэтому окно здесь — по разрешению
        /// (<see cref="Group.Window"/>): оно принимает округление энергии в
        /// библиотеке приложения («125» у линии Eu-152 121.78).
        ///
        /// Ключ — рычаг замера, не настройка (проба <c>FsaInferProbeF51 --match=</c>):
        /// <see cref="FsaLabelMatch.Position"/> — прежнее правило, положение пика в
        /// ±0.75 ПШПВ; <see cref="FsaLabelMatch.LabelLine"/> — линия подписи против
        /// любой линии группы; <see cref="FsaLabelMatch.PositionOrMainLine"/> —
        /// умолчание, см. у перечисления.
        /// </summary>
        public static FsaLabelMatch LabelMatch { get; set; } = FsaLabelMatch.PositionOrMainLine;

        /// <summary>
        /// Полуширина окна соответствия в долях ПШПВ.
        ///
        /// Три четверти, а не половина: сетка дрейфа самого разбора ходит на
        /// ±3 кэВ, и линия, стоящая ровно на краю полуширины, обязана
        /// подтвердиться.
        /// </summary>
        const double WindowFwhmFraction = 0.75;

        /// <summary>
        /// Что вышло — для проб, журнала и полки графика.
        /// </summary>
        public sealed class Report
        {
            /// <summary>Все кандидаты, принятые и отвергнутые, в порядке убывания доли.</summary>
            public readonly List<FsaParentEvidence> Candidates = new List<FsaParentEvidence>();

            /// <summary>Замечания сборки: чего не нашлось в базе и почему.</summary>
            public readonly List<string> Notes = new List<string>();

            /// <summary>Порог доли, с которым шёл этот вывод.</summary>
            public double Coverage;

            /// <summary>(`S65`) Как этот вывод обходился с оборванным рядом.</summary>
            public FsaChainCut Cut = FsaChainCut.Whole;

            /// <summary>Принято родителей.</summary>
            public int Accepted;

            /// <summary>Пиков всего и из них подписанных.</summary>
            public int Peaks, Labelled;

            /// <summary>
            /// (`AMBER157` (в), П195) Энергии линий кандидатов, не взятых в
            /// ожидаемое потому, что лежат вне диапазона кривой эффективности
            /// (прежде брали ε крайней точки). Множество — по всем кандидатам;
            /// итог — одним замечанием «кривая E1…E2 кэВ, линий вне: N».
            /// </summary>
            public readonly SortedSet<double> OutOfCurveLines = new SortedSet<double>();

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendFormat(CultureInfo.InvariantCulture,
                                  "выведено из пиков: {0} из {1} подписаны, родителей {2} из {3}, порог доли {4:P0}, оборванный ряд: {5}",
                                  this.Labelled, this.Peaks, this.Accepted,
                                  this.Candidates.Count, this.Coverage,
                                  this.Cut == FsaChainCut.Whole ? "не ищется"
                                  : this.Cut == FsaChainCut.Criterion ? "подцепочка судит, состав весь"
                                  : "подцепочка судит и идёт в состав");
                foreach (FsaParentEvidence candidate in this.Candidates)
                {
                    text.Append("; ").Append(candidate);
                }

                foreach (string note in this.Notes)
                {
                    text.Append("; ").Append(note);
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Состав по найденным пикам. Никогда не null: состав, в котором не
        /// прошёл ни один родитель, — это результат, а не отказ, и он не пуст —
        /// в нём остаются вездесущие ряды (<see cref="FsaSampleSpec.Room"/>).
        ///
        /// ⛔ Правило Amber 18.08.2026 «везде, где не знаешь, суй NORM — из
        /// базы, а не из `NuclideDefinition`» здесь работает в полную силу:
        /// именно этот случай — прибор в поле, состав неизвестен, — им и
        /// закрывается. Незнание закрывается природными рядами, а не пустотой.
        /// </summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks,
                                          ResultData resultData,
                                          double coverage,
                                          bool novelty,
                                          out Report report)
        {
            return Infer(peaks, resultData, coverage, novelty, DefaultCut,
                         out report);
        }

        /// <summary>То же с разбором ОБОРВАННОГО ряда (`S65`).</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks,
                                          ResultData resultData,
                                          double coverage,
                                          bool novelty,
                                          FsaChainCut cut,
                                          out Report report)
        {
            report = new Report { Coverage = coverage, Cut = cut };
            if (resultData == null)
            {
                return new FsaSampleSpec();
            }

            // ⛔ Обстановка спектра — кривая ради пола полосы (`S98`), порог
            // АЦП (`A73`), окно поиска пиков, вещество кристалла у прибора
            // (`A276`), кристалл и элементы пробы из геометрии (`S84`) —
            // берётся ОБЩИМ ВХОДОМ `FsaSampleSpec.OfSpectrum` (`T257`, 12.09.2026):
            // до того здесь лежала одна из пяти копий этой сборки, и второй
            // источник тех же порогов двигал бы энергию пика вылета.
            FsaSampleSpec spec = FsaSampleSpec.OfSpectrum(resultData);

            // Окно доли считается по тому же диапазону, что и спецификация:
            // у ASN16 низ стоит на 28.6 кэВ, и весь L-рентген ниже него в
            // «ожидаемое» попадать не вправе.
            var peakConfig = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;

            double minSnr = peakConfig != null && peakConfig.Min_SNR > 0.0 ? peakConfig.Min_SNR : 10.0;
            var found = new List<Peak>();
            if (peaks != null)
            {
                foreach (Peak peak in peaks)
                {
                    if (peak != null && peak.Energy > 0.0
                        && !double.IsNaN(peak.Energy) && !double.IsInfinity(peak.Energy))
                    {
                        found.Add(peak);
                    }
                }
            }

            found.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            report.Peaks = found.Count;

            // Вещества вокруг кванта (кристалл с долями, элементы пробы из
            // геометрии, вещество кристалла у прибора) уже лежат в `spec` —
            // положены `FsaSampleSpec.OfSpectrum` выше, тем же источником и
            // теми же порогами, что у объявленного состава корпуса.

            // (`AMBER119`) Кого из родителей назвал ЧЕЛОВЕК меткой ряда
            // (`NuclideDefinition.Chain`), а кто выведен из подписи без ряда:
            // множитель переходного равновесия — только у первых.
            var declaredRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = Candidates(found, spec, report, declaredRoots);
            var models = new List<Model>();
            foreach (string nucid in candidates)
            {
                Model model = Build(nucid, declaredRoots.Contains(nucid), spec, resultData, found, report);
                if (model != null)
                {
                    models.Add(model);
                }
            }

            // (`AMBER157` (в), П195) сверка кривой с линиями кандидатов — одним
            // замечанием на вывод, с диапазоном кривой и числом линий вне его
            if (report.OutOfCurveLines.Count > 0)
            {
                FsaEfficiency curve = FsaEfficiency.FromConfig(resultData.Efficiency);
                report.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "кривая {0}…{1} кэВ, линий вне: {2} ({3}) — в ожидаемое не взяты",
                    curve != null ? curve.MinEnergy.ToString("0.#", CultureInfo.InvariantCulture) : "?",
                    curve != null ? curve.MaxEnergy.ToString("0.#", CultureInfo.InvariantCulture) : "?",
                    report.OutOfCurveLines.Count,
                    string.Join(", ", report.OutOfCurveLines.Take(8)
                                          .Select(e => e.ToString("0.#", CultureInfo.InvariantCulture)))
                    + (report.OutOfCurveLines.Count > 8 ? ", …" : "")));
            }

            Score(models, minSnr);
            Breakdown(models);
            if (cut != FsaChainCut.Whole)
            {
                Head(models, coverage, report);
            }

            // Порядок РЕШАЕТ, а не украшает: приём идёт жадно, по убыванию
            // доли, и «новизна» кандидата проверяется против уже принятых
            // (см. Accept). Первым читается то, на чём состав держится.
            models.Sort((a, b) => Judged(b.Evidence).CompareTo(Judged(a.Evidence)));
            Accept(models, coverage, novelty, cut);
            Collapse(models, cut);

            foreach (Model model in models)
            {
                report.Candidates.Add(model.Evidence);
                if (!model.Evidence.Accepted)
                {
                    continue;
                }

                report.Accepted++;
                if (model.Evidence.IsChain)
                {
                    // ⛔ Ограничивать ряд головой — ВЫХОД за букву правила
                    // Amber, и делается это только по прямому ключу
                    // (`FsaChainCut.Only`). При `Criterion` голова решает,
                    // брать ли родителя, а в состав, как и велено, идёт весь
                    // его ряд.
                    FsaSampleChain chain = Restricted(model, cut)
                        ? new FsaSampleChain(model.Evidence.Nucid, model.Evidence.Head.ToArray())
                        : new FsaSampleChain(model.Evidence.Nucid);
                    chain.Transient = declaredRoots.Contains(model.Evidence.Nucid);
                    spec.Chains.Add(chain);
                }
                else
                {
                    spec.Nuclides.Add(model.Evidence.Nucid);
                }
            }

            return spec;
        }

        /// <summary>То же с порогом прогона и проверкой новизны.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          double coverage, out Report report)
        {
            return Infer(peaks, resultData, coverage, true, out report);
        }

        /// <summary>То же с порогом по умолчанию.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          out Report report)
        {
            return Infer(peaks, resultData, DefaultCoverage, true, out report);
        }

        // ------------------------------------------------------------------
        // Кандидаты
        // ------------------------------------------------------------------

        /// <summary>
        /// Родители, которых назвал поиск пиков, в порядке первого появления.
        ///
        /// Родитель берётся из <see cref="NuclideDefinition.Chain"/>: у линии
        /// Bi-214 из радиевого равновесия там стоит «Ra-226», и объявлять надо
        /// именно радий, а не висмут — иначе ряд разорвётся на середине и
        /// половина линий останется без образа. Пусто — линия сама по себе, и
        /// родителем является её собственный нуклид.
        /// </summary>
        static List<string> Candidates(List<Peak> peaks, FsaSampleSpec spec, Report report,
                                       HashSet<string> declaredRoots)
        {
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Peak peak in peaks)
            {
                if (peak.Nuclide == null || string.IsNullOrEmpty(peak.Nuclide.Name))
                {
                    continue;
                }

                report.Labelled++;

                // Характеристический рентген элемента родителем не бывает: за
                // его линиями нет распада вовсе. Но он сообщает нечто о
                // ГЕОМЕТРИИ — свинец домика, вольфрам электрода, — и это
                // единственный способ узнать про защиту, когда её никто не
                // объявлял. Элемент уходит в мешающие образы, где ему и место.
                if (NuclideDefinition.IsElementXrayName(peak.Nuclide.Name))
                {
                    int z = MaterialDatabase.ZOf(NuclideDefinition.NuclideNameOf(peak.Nuclide.Name));
                    if (z > 0 && !spec.ShieldElements.Contains(z))
                    {
                        spec.ShieldElements.Add(z);
                    }

                    continue;
                }

                string parent = peak.Nuclide.Chain;
                bool declared = !string.IsNullOrEmpty(parent);
                if (!declared)
                {
                    parent = peak.Nuclide.NuclideName;
                }

                string nucid = FsaSampleLibrary.NucidOf(parent);
                if (nucid.Length == 0)
                {
                    if (unknown.Add(parent))
                    {
                        report.Notes.Add("подпись «" + parent + "» не разобрана в nucid");
                    }

                    continue;
                }

                // (`AMBER119`) Родитель по метке ряда объявлен человеком; хоть
                // одна такая подпись — и корень объявлен.
                if (declared)
                {
                    declaredRoots.Add(nucid);
                }

                if (seen.Add(nucid))
                {
                    order.Add(nucid);
                }
            }

            return order;
        }

        // ------------------------------------------------------------------
        // Модель родителя: что он обещает и что из этого видно
        // ------------------------------------------------------------------

        /// <summary>
        /// Группа линий, неразличимая прибором: несколько линий, стоящих ближе
        /// одной ПШПВ, дают ОДИН наблюдаемый пик, и считать их порознь значит
        /// требовать от прибора того, чего он не умеет.
        ///
        /// Ровно это и снимает вторую беду сырого счёта, названную в `S57`:
        /// при ПШПВ от 0.20 % до 13.3 % одно и то же число линий означает
        /// разное число наблюдаемых структур, а число ГРУПП — уже одно и то же
        /// по смыслу.
        /// </summary>

        /// <summary>
        /// Стоимость пути в остаточном графе (`A297`): сумма нормированных
        /// отходов, а при ТОЧНОМ их равенстве — сумма номеров кандидатов в
        /// разобранном порядке (ближе, значимее, затем энергии).
        ///
        /// ⚠ Второй ключ нужен не для красоты. Отход `away/window` у разных
        /// пар совпадает точно чаще, чем кажется: пик ровно в центре группы
        /// даёт ноль, и таких нулей в спектре с хорошим разрешением много.
        /// Без второго ключа выбор между равными путями достался бы порядку
        /// обхода рёбер, то есть перестал бы быть свойством данных.
        /// </summary>
        struct Cost
        {
            public double Away;
            public long Rank;

            public Cost Plus(double away, long rank)
            {
                return new Cost { Away = this.Away + away, Rank = this.Rank + rank };
            }

            public int CompareTo(Cost other)
            {
                int by = this.Away.CompareTo(other.Away);
                return by != 0 ? by : this.Rank.CompareTo(other.Rank);
            }
        }

        /// <summary>
        /// Паросочетание максимальной мощности и минимальной стоимости
        /// (`A297`). Возвращает выбранные пары; группы и пики в них взаимно
        /// однозначны.
        ///
        /// Ход — последовательные кратчайшие увеличивающие пути: на каждом шаге
        /// ищется путь НАИМЕНЬШЕЙ стоимости от ЛЮБОЙ свободной группы до любого
        /// свободного пика, и по нему разворачивается паросочетание. Обратные
        /// рёбра (по занятым парам) имеют отрицательный вес, поэтому кратчайший
        /// путь ищет Беллман — Форд, а не Дейкстра.
        ///
        /// ⚠ Мощность при этом не страдает: шаг кончается, только когда пути
        /// нет НИ ОТ ОДНОЙ свободной группы, — а это и есть признак максимума.
        /// </summary>
        static List<Candidate> MinCostMatching(List<Group> order,
                                               Dictionary<Group, List<Candidate>> options,
                                               List<Candidate> ranked)
        {
            var rankOf = new Dictionary<Candidate, long>();
            for (int i = 0; i < ranked.Count; i++)
            {
                rankOf[ranked[i]] = i + 1;
            }

            var groupNo = new Dictionary<Group, int>();
            foreach (Group group in order)
            {
                groupNo[group] = groupNo.Count;
            }

            var peakNo = new Dictionary<Peak, int>();
            var edges = new List<Candidate>();
            foreach (Group group in order)
            {
                List<Candidate> bag;
                if (!options.TryGetValue(group, out bag))
                {
                    continue;
                }

                foreach (Candidate candidate in bag)
                {
                    if (!peakNo.ContainsKey(candidate.Peak))
                    {
                        peakNo[candidate.Peak] = peakNo.Count;
                    }

                    edges.Add(candidate);
                }
            }

            int groups = groupNo.Count;
            int peaks = peakNo.Count;
            var result = new List<Candidate>();
            if (groups == 0 || peaks == 0)
            {
                return result;
            }

            // Кто из кандидатов сейчас занимает пик; -1 — пик свободен.
            var matchOf = new int[peaks];
            for (int i = 0; i < peaks; i++)
            {
                matchOf[i] = -1;
            }

            int nodes = groups + peaks;
            var dist = new Cost[nodes];
            var reached = new bool[nodes];
            var viaEdge = new int[nodes];
            var viaNode = new int[nodes];
            var matchedGroup = new bool[groups];

            // ⛔ ПУТЬ ИЩЕТСЯ ОТ ВСЕХ СВОБОДНЫХ ГРУПП СРАЗУ, А НЕ ОТ ОЧЕРЕДНОЙ.
            // Это не ускорение, а условие правильности: перебор групп по одной
            // даёт минимум лишь среди раскладов, занимающих ТЕ ЖЕ группы. Один
            // пик на две группы — G1 ценой 0.4 и G2 ценой 0.2 — при обходе по
            // очереди достаётся G1 (она первая), и подвинуть её некуда: мощность
            // не растёт, а значит увеличивающего пути нет и цена остаётся 0.4.
            // Общий исток выбирает 0.2 сразу.
            for (int step = 0; step < peaks; step++)
            {
                for (int i = 0; i < nodes; i++)
                {
                    reached[i] = false;
                    viaEdge[i] = -1;
                    viaNode[i] = -1;
                }

                for (int g = 0; g < groups; g++)
                {
                    if (matchedGroup[g])
                    {
                        continue;
                    }

                    reached[g] = true;
                    dist[g] = new Cost();
                }

                // Беллман — Форд: проходов не больше числа узлов, выход раньше,
                // как только проход ничего не улучшил.
                for (int pass = 0; pass < nodes; pass++)
                {
                    bool moved = false;
                    for (int e = 0; e < edges.Count; e++)
                    {
                        Candidate edge = edges[e];
                        int left = groupNo[edge.Group];
                        int right = groups + peakNo[edge.Peak];
                        bool taken = matchOf[peakNo[edge.Peak]] == e;
                        int from = taken ? right : left;
                        int to = taken ? left : right;
                        if (!reached[from])
                        {
                            continue;
                        }

                        double away = taken ? -edge.Away : edge.Away;
                        long rank = taken ? -rankOf[edge] : rankOf[edge];
                        Cost candidate = dist[from].Plus(away, rank);
                        if (reached[to] && dist[to].CompareTo(candidate) <= 0)
                        {
                            continue;
                        }

                        reached[to] = true;
                        dist[to] = candidate;
                        viaEdge[to] = e;
                        viaNode[to] = from;
                        moved = true;
                    }

                    if (!moved)
                    {
                        break;
                    }
                }

                // ⚠ Обход пиков — ПО НОМЕРУ, а не по словарю: номера розданы в
                // порядке разобранных кандидатов, то есть свойство данных, а
                // перебор `Dictionary` порядка не обещает вовсе.
                int best = -1;
                for (int p = 0; p < peaks; p++)
                {
                    int node = groups + p;
                    if (matchOf[p] >= 0 || !reached[node])
                    {
                        continue;
                    }

                    if (best < 0 || dist[node].CompareTo(dist[best]) < 0)
                    {
                        best = node;
                    }
                }

                if (best < 0)
                {
                    // Свободного пика не достать ни одной перестановкой:
                    // мощность больше не растёт, и дальше расти не начнёт.
                    break;
                }

                // Разворот пути: каждое ребро «слева направо» становится
                // занятым, каждое «справа налево» освобождается — а оно тут же
                // перезанимается следующим шагом разворота.
                int at = best;
                while (at >= 0)
                {
                    if (at >= groups)
                    {
                        matchOf[at - groups] = viaEdge[at];
                    }

                    int previous = viaNode[at];
                    if (previous < 0)
                    {
                        // Начало пути — свободная группа, теперь занятая.
                        matchedGroup[at] = true;
                        break;
                    }

                    at = previous;
                }
            }

            for (int i = 0; i < peaks; i++)
            {
                if (matchOf[i] >= 0)
                {
                    result.Add(edges[matchOf[i]]);
                }
            }

            return result;
        }

        /// <summary>
        /// Пара «группа линий — найденный пик» для взаимно однозначного
        /// сопоставления (`A292`). Своего смысла не несёт, живёт один вызов.
        /// </summary>
        sealed class Candidate
        {
            public Group Group;
            public Peak Peak;

            /// <summary>Отход от центра группы В ДОЛЯХ ЕЁ ОКНА: окна у групп
            /// разной ширины, и сравнивать кэВ напрямую нельзя.</summary>
            public double Away;

            public double Snr;

            /// <summary>
            /// Порядок разбора: ближе к центру — раньше; при равной близости
            /// сильнее по значимости; дальше — по энергиям, чтобы у одинаковых
            /// пар был ОДИН исход, а не зависящий от порядка входных списков.
            /// </summary>
            public static int Order(Candidate a, Candidate b)
            {
                int by = a.Away.CompareTo(b.Away);
                if (by != 0)
                {
                    return by;
                }

                by = b.Snr.CompareTo(a.Snr);
                if (by != 0)
                {
                    return by;
                }

                by = a.Group.Energy.CompareTo(b.Group.Energy);
                return by != 0 ? by : a.Peak.Energy.CompareTo(b.Peak.Energy);
            }
        }

        sealed class Group
        {
            public double Energy;
            public double Weight;

            public double Window;

            /// <summary>
            /// (`AMBER221`) Истинные энергии линий группы — с ними сопоставляется
            /// линия подписи пика (<see cref="LabelMatch"/>).
            /// </summary>
            public readonly List<double> Lines = new List<double>();
            public double Snr = double.NaN;
            public bool Expected;

            /// <summary>
            /// (`S65`) Член ряда, чья линия в группе сильнейшая, — тот же, чья
            /// энергия стала <see cref="Energy"/>. Группу «чью» спрашивают
            /// только при разборе обрыва, и спрашивают именно про сильнейшую:
            /// слабый подмешавшийся сосед наблюдаемого пика не даёт.
            /// </summary>
            public string Owner = "";

            /// <summary>(`AMBER236`) Пик, подтвердивший группу; null — не подтверждена.</summary>
            public Peak Peak;

            /// <summary>
            /// (`AMBER236`) Голова отрезка ряда, которому группа засчитана
            /// (<see cref="Head"/>); пусто — по отрезкам не судили.
            /// </summary>
            public string Segment = "";

            public bool Matched
            {
                get { return !double.IsNaN(this.Snr); }
            }
        }

        sealed class Model
        {
            public readonly FsaParentEvidence Evidence = new FsaParentEvidence();
            public readonly List<Group> Groups = new List<Group>();
            public readonly HashSet<string> Members =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>(`S65`) Глубина члена ряда от корня; пусто — не ряд.</summary>
            public Dictionary<string, int> Depths;

            /// <summary>(`AMBER236`) Головы отрезков, взятых в подцепочку.</summary>
            public readonly HashSet<string> HeadSegments =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static Model Build(string nucid, bool declaredRoot, FsaSampleSpec spec, ResultData resultData,
                           List<Peak> peaks, Report report)
        {
            var model = new Model();
            model.Evidence.Nucid = nucid;
            model.Evidence.Name = FsaSampleLibrary.PrettyName(nucid);

            // Состав ряда собирается ТЕМ ЖЕ методом, которым потом соберётся
            // библиотека, — иначе критерий мерил бы один список, а фиту
            // предъявлялся другой.
            var branch = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var sample = new FsaSampleLibrary.Report();
            // (`AMBER119`) Веса критерия — с тем же множителем, что уйдёт в
            // библиотеку: у выведенного корня — вековое равновесие.
            FsaSampleLibrary.CollectChain(new FsaSampleChain(nucid) { Transient = declaredRoot },
                                          spec.MinChainBranch, spec.MinIsomerBranch, branch, sample);
            if (branch.Count == 0)
            {
                // Нуклида нет в `decay_chain` вовсе (стабильный, или подпись
                // указывает на то, чего база не знает). Ряда не будет, но сам
                // он линии иметь может — Cs-137 в `decay_chain` есть, а вот
                // одиночки без дочерних встречаются.
                branch[nucid] = 1.0;
            }

            foreach (string member in branch.Keys)
            {
                model.Members.Add(member);
            }

            // Ряд — если у родителя есть кто-то ещё, кроме него самого.
            model.Evidence.IsChain = branch.Count > 1;

            // (`S65`) Порядок членов ряда — отдельным обходом и только он.
            // Доли ветвления обходом в ширину считать нельзя (`S62`), а
            // порядок — можно и нужно: без него «голова ряда» не определена.
            if (model.Evidence.IsChain)
            {
                model.Depths = FsaSampleLibrary.ChainDepths(nucid, sample);
            }

            var lines = new List<Line>();
            foreach (KeyValuePair<string, double> member in branch)
            {
                foreach (double[] line in FsaSampleLibrary.DecayLines(member.Key, sample))
                {
                    if (line[0] < spec.MinEnergyKev || line[0] > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    lines.Add(new Line
                    {
                        Energy = line[0],
                        Weight = line[1] * member.Value,
                        Member = member.Key
                    });
                }
            }

            foreach (string note in sample.Notes)
            {
                report.Notes.Add(note);
            }

            if (lines.Count == 0)
            {
                model.Evidence.Why = "линий в рабочем окне нет";
                model.Evidence.Verdict = FsaParentVerdict.NoLines;
                return model;
            }

            foreach (Peak peak in peaks)
            {
                if (Belongs(peak, nucid))
                {
                    model.Evidence.LabelledPeaks++;
                }
            }

            lines.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            Collect(model, lines, resultData, peaks, report.OutOfCurveLines);
            return model;
        }

        /// <summary>Линия ряда с указанием, ЧЬЯ она (`S65`).</summary>
        sealed class Line
        {
            public double Energy;
            public double Weight;
            public string Member = "";
        }

        /// <summary>
        /// Линии — в группы по разрешению, группы — в соответствие с пиками.
        ///
        /// Вес группы есть сумма «выход × эффективность»: линия, которую прибор
        /// почти не регистрирует, ожидаемой не является, сколько бы ни был
        /// велик её выход. Кривой может не быть вовсе (в поле её обычно и нет)
        /// — тогда эффективность единица для всех, и вес вырождается в выход;
        /// это допущение, а не умолчание, и оно названо здесь.
        /// </summary>
        static void Collect(Model model, List<Line> lines, ResultData resultData,
                            List<Peak> peaks, SortedSet<double> outOfCurve)
        {
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(resultData.Efficiency);
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            EnergyCalibration energyCalibration = spectrum != null ? spectrum.EnergyCalibration : null;
            FwhmCalibration fwhmCalibration = resultData.FwhmCalibration;
            int channels = spectrum != null ? spectrum.NumberOfChannels : 0;

            Group current = null;
            double currentTop = 0.0;
            foreach (Line line in lines)
            {
                double weight = line.Weight;
                if (efficiency != null)
                {
                    // ⛔ (`AMBER157` (в), П195) тем же правилом, что образ
                    // разбора: вне кривой эффективность неизвестна, и линия в
                    // ожидаемое не идёт (прежде `Eval` давал ε крайней точки —
                    // линия за верхом кривой весила больше, чем весит). Энергия
                    // — в замечание вывода.
                    double eps, errorPercent;
                    if (!efficiency.TryEval(line.Energy, out eps, out errorPercent))
                    {
                        if (outOfCurve != null && line.Weight > 0.0)
                        {
                            outOfCurve.Add(line.Energy);
                        }

                        continue;
                    }

                    weight *= eps;
                }

                if (!(weight > 0.0))
                {
                    continue;
                }

                double width = Resolution(line.Energy, energyCalibration, fwhmCalibration, channels);
                if (current != null && line.Energy - current.Energy <= width)
                {
                    // Центр группы держится на сильнейшей линии: она и даёт
                    // наблюдаемый пик, а слабые соседи лишь подмешиваются.
                    current.Weight += weight;
                    current.Lines.Add(line.Energy);
                    if (weight > currentTop)
                    {
                        currentTop = weight;
                        current.Energy = line.Energy;
                        current.Owner = line.Member;
                    }

                    continue;
                }

                current = new Group { Energy = line.Energy, Weight = weight, Owner = line.Member };
                current.Lines.Add(line.Energy);
                currentTop = weight;
                model.Groups.Add(current);
            }

            // ⛔⛔ СОПОСТАВЛЕНИЕ ГРУПП И ПИКОВ — ВЗАИМНО ОДНОЗНАЧНОЕ (`A292`).
            //
            // Прежде каждая группа искала свой пик САМА, и найденный нигде не
            // помечался. Окна при этом пересекаются по построению: новая группа
            // начинается, когда линия отошла от центра больше чем на ПШПВ, а
            // окно у группы — ±0.75 ПШПВ. Две группы на расстоянии 1.1 ПШПВ
            // остаются разными, их окна общей шириной 1.5 ПШПВ накрывают друг
            // друга на 0.4 ПШПВ, и ОДИН пик в перекрытии подтверждал ОБЕ.
            //
            // Цена: `Matched` рос вдвое на одном наблюдении, а вместе с ним
            // `Coverage = Matched / Expected` — то есть родитель мог пройти
            // порог 0.30 без второго независимого свидетельства. Тем доступнее,
            // чем шире ПШПВ, то есть у сцинтилляторов в первую очередь.
            //
            // Здесь пары «группа — пик» строятся все разом и разбираются
            // жадно по БЛИЗОСТИ, нормированной окном группы. Порядок разбора
            // задан явно (близость, затем значимость, затем энергии), поэтому
            // итог не зависит от порядка групп и пиков во входных списках.
            List<Candidate> pairs = new List<Candidate>();
            foreach (Group group in model.Groups)
            {
                double window = Window(group.Energy,
                                       Resolution(group.Energy, energyCalibration,
                                                  fwhmCalibration, channels));
                group.Window = window;
                if (!(window > 0.0))
                {
                    continue;
                }

                foreach (Peak peak in peaks)
                {
                    // (`AMBER221`) сдвиг пика от линии судил допуск при подписи;
                    // здесь — линия подписи против линий группы
                    double away = Away(peak, group);
                    if (away > window)
                    {
                        continue;
                    }

                    // ⛔ ПОДПИСЬ ПИКА ПРОВЕРЯЕТСЯ, И ЭТО БУКВА ПРАВИЛА AMBER:
                    // «если поиск набрал весомое число пиков ОДНОГО РОДИТЕЛЯ».
                    // Считать подтверждением любой пик в окне пробовали — на
                    // сцинтилляторе это не работает вовсе: при ПШПВ 6.7 % у
                    // ASN16 окно на 344 кэВ выходит в 23 кэВ, и фантомный
                    // Eu-152 набрал в ториевом спектре 15 групп из 16, потому
                    // что богатый линиями чужак попадает в чужую структуру
                    // всюду. Разделяет их не положение, а ПРИНАДЛЕЖНОСТЬ.
                    //
                    // Круговой зависимости это не создаёт: подпись решает
                    // «какой пик чей», а прошёл родитель или нет — решает ДОЛЯ
                    // от ожидаемого, и одной подписи для неё мало. Один
                    // ошибочно подписанный пик даёт долю 1/16, а не состав.
                    if (!Belongs(peak, model.Evidence.Nucid))
                    {
                        continue;
                    }

                    pairs.Add(new Candidate
                    {
                        Group = group,
                        Peak = peak,
                        Away = away / window,
                        Snr = peak.SNR
                    });
                }
            }

            // ⛔ ЖАДНОГО ВЫБОРА МАЛО (`A293`, поправка к `A292`). Взаимную
            // однозначность он даёт, а МАКСИМАЛЬНОЕ число совпадений — нет:
            // группа `G1` видит пики `P1` и `P2` (P1 чуть ближе), соседняя
            // `G2` — только `P1`; жадность берёт `G1`–`P1`, и `G2` остаётся ни
            // с чем, хотя пара `G1`–`P2` и `G2`–`P1` даёт ДВА совпадения.
            // То есть двойной зачёт одного пика исчез, но `Matched` и
            // `Coverage` стали ЗАНИЖАТЬСЯ — там же, где проявлялась `A292`.
            //
            // Здесь ищется максимальное паросочетание двудольного графа
            // (увеличивающими путями, алгоритм Куна). Порядок обхода задан
            // явно — группы по энергии, кандидаты каждой по близости, — поэтому
            // итог не зависит от порядка входных списков.
            //
            // ⛔⛔ И МАКСИМАЛЬНОЙ МОЩНОСТИ ТОЖЕ МАЛО (`A297`, поправка к
            // `A293`). Максимумов бывает много, и прежний поиск брал ЛЮБОЙ из
            // них — тот, до которого довели увеличивающие пути. Контрпример:
            // `G1` видит `P1` ценой 0.1 и `P2` ценой 0.9, `G2` видит `P1`
            // ценой 0.2 и `P2` ценой 0.3. Заняв `G1`–`P1`, второй путь
            // выталкивает `G1` на `P2` — мощность 2, стоимость 1.1, тогда как
            // `G1`–`P1` + `G2`–`P2` даёт ту же мощность 2 при стоимости 0.4.
            //
            // Дешевле это не «красивее»: выбранный пик отдаёт группе свою
            // значимость (`Group.Snr`), медиана `SNR/Weight` решает, какие
            // группы считаются ожидаемыми, и `Expected`/`Coverage` меняются
            // вместе с выбором. То есть максимум без цены двигал приговор о
            // составе.
            //
            // Здесь ищется паросочетание МАКСИМАЛЬНОЙ МОЩНОСТИ И МИНИМАЛЬНОЙ
            // СТОИМОСТИ — последовательными кратчайшими увеличивающими путями
            // (Беллман — Форд по остаточному графу: у обратных рёбер вес
            // отрицателен, Дейкстра без потенциалов здесь неприменима).
            // Свойство SSP: augment по пути минимальной стоимости оставляет
            // паросочетание оптимальным ДЛЯ СВОЕЙ мощности на каждом шаге.
            //
            // ⚠ Что остаётся приближением: стоимость — сумма нормированных
            // отходов, значимость входит лишь вторым ключом при ТОЧНОМ
            // равенстве отходов. Взвешивать отход значимостью было бы уже
            // другой моделью, и такой договорённости нет.
            pairs.Sort(Candidate.Order);

            var options = new Dictionary<Group, List<Candidate>>();
            var order = new List<Group>();
            foreach (Candidate pair in pairs)
            {
                List<Candidate> bag;
                if (!options.TryGetValue(pair.Group, out bag))
                {
                    options[pair.Group] = bag = new List<Candidate>();
                    order.Add(pair.Group);
                }

                bag.Add(pair);
            }

            // Группы — по энергии: порядок обхода обязан быть свойством ДАННЫХ,
            // а не порядка построения списков.
            order.Sort((a, b) => a.Energy.CompareTo(b.Energy));

            foreach (Candidate taken in MinCostMatching(order, options, pairs))
            {
                taken.Group.Snr = taken.Snr;
                taken.Group.Peak = taken.Peak;
            }
        }

        /// <summary>
        /// Пик подписан этим родителем: либо его рядом
        /// (<see cref="NuclideDefinition.Chain"/>), либо им самим.
        /// </summary>
        static bool Belongs(Peak peak, string nucid)
        {
            if (peak.Nuclide == null || string.IsNullOrEmpty(peak.Nuclide.Name))
            {
                return false;
            }

            string parent = peak.Nuclide.Chain;
            if (string.IsNullOrEmpty(parent))
            {
                parent = peak.Nuclide.NuclideName;
            }

            return string.Equals(FsaSampleLibrary.NucidOf(parent), nucid,
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Полуширина окна соответствия: доля ПШПВ, а при отсутствующей
        /// калибровке — доля энергии. См. <see cref="MinWindowFraction"/> о том,
        /// почему сюда НЕ идёт допуск подписи.
        /// </summary>
        static double Window(double energy, double resolution)
        {
            return Math.Max(WindowFwhmFraction * resolution, MinWindowFraction * energy);
        }

        /// <summary>
        /// (`AMBER221`) Отход пика от группы, кэВ, по правилу <see cref="LabelMatch"/>.
        /// Положение пика — против центра группы, как до 12.10.2026. Линия подписи
        /// (энергия определения, которым пик подписан) — против линий группы: обе
        /// истинные, сдвиг шкалы уже судил допуск при подписи. Подпись без энергии —
        /// только положение.
        /// </summary>
        static double Away(Peak peak, Group group)
        {
            double position = Math.Abs(peak.Energy - group.Energy);
            double label = peak.Nuclide != null ? peak.Nuclide.Energy : 0.0;
            if (LabelMatch == FsaLabelMatch.Position || !(label > 0.0)
                || double.IsNaN(label) || double.IsInfinity(label))
            {
                return position;
            }

            double nearest = double.PositiveInfinity, nearestLine = double.NaN;
            foreach (double energy in group.Lines)
            {
                double away = Math.Abs(label - energy);
                if (away < nearest)
                {
                    nearest = away;
                    nearestLine = energy;
                }
            }

            if (LabelMatch == FsaLabelMatch.LabelLine)
            {
                return nearest;
            }

            // главная линия группы — её центр (`Collect`: центр держится на
            // сильнейшей); подпись второстепенной линией в счёт не идёт
            return nearestLine == group.Energy ? Math.Min(position, nearest) : position;
        }

        /// <summary>ПШПВ в кэВ на этой энергии; ноль — калибровки нет.</summary>
        static double Resolution(double energy, EnergyCalibration energyCalibration,
                                 FwhmCalibration fwhmCalibration, int channels)
        {
            if (energyCalibration == null || fwhmCalibration == null || channels <= 0)
            {
                return 0.0;
            }

            try
            {
                double channel = energyCalibration.EnergyToChannel(energy, maxChannels: channels);
                if (double.IsNaN(channel) || double.IsInfinity(channel))
                {
                    return 0.0;
                }

                channel = Math.Max(0.0, Math.Min(channels - 1, channel));
                double fwhm = fwhmCalibration.ChannelToFwhm(channel);
                if (!(fwhm > 0.0) || double.IsNaN(fwhm) || double.IsInfinity(fwhm))
                {
                    return 0.0;
                }

                double left = energyCalibration.ChannelToEnergy(Math.Max(0.0, channel - 0.5 * fwhm));
                double right = energyCalibration.ChannelToEnergy(
                    Math.Min(channels - 1, channel + 0.5 * fwhm));
                double width = right - left;
                return width > 0.0 && !double.IsNaN(width) && !double.IsInfinity(width)
                    ? width : 0.0;
            }
            catch (Exception)
            {
                // Калибровка вырождена — соответствие ищется по допуску
                // подписи. Молчать нельзя было бы, если бы отсюда шёл счёт;
                // отсюда идёт ШИРИНА ОКНА, и запасная у неё есть.
                return 0.0;
            }
        }

        // ------------------------------------------------------------------
        // Критерий значимости
        // ------------------------------------------------------------------

        /// <summary>
        /// Кто из кандидатов прошёл — и почему.
        ///
        /// ⛔ ЗНАМЕНАТЕЛЬ ЗДЕСЬ ВАЖНЕЕ ПОРОГА, и в нём весь смысл правила.
        /// Считать долю от ВСЕХ линий родителя нельзя: у Th-232 их сорок пять
        /// выше 1 % на распад, и ни один сцинтиллятор столько не разрешает —
        /// настоящий торий получил бы долю 0.2 и был бы отвергнут. Поэтому
        /// знаменатель — линии, которые при ЭТОЙ статистике и ЭТОМ разрешении
        /// вообще можно было увидеть, и определяется он самими данными:
        ///
        ///   * по подтверждённым группам берётся медиана «SNR на единицу
        ///     веса» — это и есть цена единицы выхода в отсчётах ЭТОГО
        ///     спектра;
        ///   * ожидаемой считается группа, которой при этой цене полагается
        ///     SNR не ниже порога поиска пиков.
        ///
        /// Отсюда сразу два нужных свойства. Слабый спектр не наказывается: у
        /// него мало что «ожидалось», доля считается от малого. И ложный
        /// родитель, зацепившийся одной слабой линией, наказывается жёстко:
        /// медиана цены выходит огромной, ожидаемым становится ВЕСЬ ряд, и
        /// доля падает до одной группы из сорока.
        ///
        /// ⚠ Медиана, а не среднее: одно случайное совпадение сильного пика со
        /// слабой линией сдвинуло бы среднее на порядок.
        /// </summary>
        static void Score(List<Model> models, double minSnr)
        {
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (model.Groups.Count == 0)
                {
                    // (`AMBER221`) линии были, но ни одна не дошла до группы
                    // (вне кривой эффективности, нулевой вес)
                    if (evidence.Why.Length == 0)
                    {
                        evidence.Why = "линий в рабочем окне нет";
                    }

                    evidence.Verdict = FsaParentVerdict.NoLines;
                    continue;
                }

                var prices = new List<double>();
                foreach (Group group in model.Groups)
                {
                    if (group.Matched && group.Weight > 0.0)
                    {
                        prices.Add(group.Snr / group.Weight);
                    }
                }

                if (prices.Count == 0)
                {
                    evidence.Why = "ни одна линия не подтверждена пиком";
                    evidence.Verdict = FsaParentVerdict.NotConfirmed;
                    continue;
                }

                prices.Sort();
                double price = prices.Count % 2 == 1
                    ? prices[prices.Count / 2]
                    : 0.5 * (prices[prices.Count / 2 - 1] + prices[prices.Count / 2]);

                foreach (Group group in model.Groups)
                {
                    // Подтверждённая группа ожидаемой является по построению:
                    // её ВИДНО. Оценка цены — медиана, и половина
                    // подтверждённых стоит по ней «ниже порога»; вычесть их из
                    // знаменателя, оставив в числителе, значило бы получить
                    // долю больше единицы.
                    group.Expected = group.Matched || price * group.Weight >= minSnr;
                    if (group.Expected)
                    {
                        evidence.Expected++;
                    }

                    if (group.Matched)
                    {
                        evidence.Matched++;
                    }
                }

                evidence.Coverage = evidence.Expected > 0
                    ? (double)evidence.Matched / evidence.Expected : 0.0;
            }
        }

        // ------------------------------------------------------------------
        // S65: ОБОРВАННЫЙ ряд
        // ------------------------------------------------------------------

        /// <summary>
        /// (`A205`) Что известно про каждого члена ряда: сколько его групп
        /// ожидалось и сколько подтвердилось пиком. ОДИН счёт на объяснение и
        /// на разбор обрыва — второе правило о том, что такое «член
        /// подтверждён», развело бы отказ с решением.
        /// </summary>
        static void MemberCounts(Model model, Dictionary<string, int> expected,
                                 Dictionary<string, int> matched)
        {
            foreach (Group group in model.Groups)
            {
                if (!group.Expected || group.Owner.Length == 0)
                {
                    continue;
                }

                int have;
                expected[group.Owner] = (expected.TryGetValue(group.Owner, out have) ? have : 0) + 1;
                if (group.Matched)
                {
                    matched[group.Owner] = (matched.TryGetValue(group.Owner, out have) ? have : 0) + 1;
                }
            }
        }

        /// <summary>
        /// (`A205`) Расклад по членам ряда — в
        /// <see cref="FsaParentEvidence.ByMember"/>, по возрастанию глубины.
        /// Считается ВСЕГДА, а не только при разборе обрыва: отказ «доля ниже
        /// порога» без него не объясняет себя ничем.
        /// </summary>
        static void Breakdown(List<Model> models)
        {
            foreach (Model model in models)
            {
                if (model.Depths == null || model.Depths.Count == 0)
                {
                    continue;
                }

                var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var matched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                MemberCounts(model, expected, matched);

                var order = new List<string>(model.Members);
                order.Sort(delegate (string a, string b)
                {
                    int da, db;
                    if (!model.Depths.TryGetValue(a, out da)) da = int.MaxValue;
                    if (!model.Depths.TryGetValue(b, out db)) db = int.MaxValue;
                    return da != db ? da.CompareTo(db)
                                    : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });

                foreach (string member in order)
                {
                    int have, hit, depth;
                    if (!expected.TryGetValue(member, out have))
                    {
                        have = 0;
                    }

                    if (!matched.TryGetValue(member, out hit))
                    {
                        hit = 0;
                    }

                    if (!model.Depths.TryGetValue(member, out depth))
                    {
                        depth = -1;
                    }

                    model.Evidence.ByMember.Add(string.Format(CultureInfo.InvariantCulture,
                                                              "{0}@{1} {2}/{3}", member, depth, hit, have));
                }
            }
        }

        /// <summary>
        /// ПОДЦЕПОЧКА — тот кусок ряда, который в пробе действительно есть, и
        /// его собственная доля.
        ///
        /// ⛔ ЗАЧЕМ ЭТО ВООБЩЕ НУЖНО. Доля <see cref="Score"/> спрашивает «виден
        /// ли ряд целиком, в равновесии». У пробы, где ряда целиком нет,
        /// правильный ответ на этот вопрос — «нет», и он же неверный ответ на
        /// вопрос, который на самом деле задан: «есть ли в пробе родитель».
        ///
        /// ⛔ ОДНО ПРАВИЛО НА ВСЕ СЛУЧАИ ОБРЫВА (решение Amber 06.09.2026 по
        /// `A205`: «тем же правилом, что уже принято для `S65`»; способ резать —
        /// решение Amber 19.10.2026 по `AMBER236`, дословно: «Резать по
        /// долгоживущим членам (рекомендую)»). Случаи:
        ///
        ///   `S65`/`AMBER236`, ряд оборван СНИЗУ — урановое стекло: уран попал в
        ///   стекло химически очищенным, Th-230 и всё ниже вымыто, и `U-238`
        ///   целиком набирал 9 % (3/33) — 26 групп из 33 у Pb-214 и Bi-214;
        ///   `A205`, ряд оборван СВЕРХУ — точечный Th-228: есть `Th-228` и всё
        ///   ниже, нет `Ra-228`/`Ac-228`;
        ///   обеднённый уран и U-235 в нём — Pa-231 и ниже не наросли.
        ///
        /// ЧТО ЭТО ЗА КУСКИ. Ряд режется на ОТРЕЗКИ по долгоживущим членам
        /// (<see cref="FsaSampleLibrary.ChainSegments"/>, период от
        /// <see cref="FsaSampleLibrary.ChainBreakSeconds"/>): внутри отрезка
        /// равновесие наступает само, между отрезками его может не быть. Группы и
        /// их «ожидаемость» — те же, что у доли целого ряда (<see cref="Score"/>), а
        /// засчитывается группа отрезку так:
        ///   * подтверждённая — отрезку нуклида, КОТОРЫМ ПОДПИСАН ЕЁ ПИК (подпись
        ///     решает, какой пик чей, — та же буква, что у сопоставления), а
        ///     подпись не членом ряда — отрезку хозяина группы;
        ///   * не подтверждённая — отрезку хозяина (<see cref="Group.Owner"/>).
        /// Каждый отрезок судится отдельно той же долей с тем же порогом:
        ///   * «есть» — попаданий не меньше двух и доля не ниже порога;
        ///   * «нет» — порога не держит, а ожидалось столько, что одно попадание
        ///     его бы не перешло (`have·порог &gt; 1`): отрезок ряду противоречит;
        ///   * «молчит» — ни то, ни другое: сказать нечего, и молчание уликой не
        ///     является.
        /// В подцепочку идут отрезки «есть» и «молчащие» МЕЖДУ ними (питаемые одним
        /// взятым и питающие другой): разрыв ряда посреди взятого ничем не
        /// подтверждён.
        ///
        /// ⚠ Почему подтверждение — по подписи, а не по хозяину. В широкой группе
        /// сцинтиллятора хозяин — сильнейшая линия всего ряда, а не та, что дала
        /// пик: на `UGlass nos 12.xml` (ASN16, 22 % ПШПВ на 95 кэВ) линии Th-234
        /// 63/92 кэВ стоят в группах с K-рентгеном висмута от Pb-214 и Ra-226, и
        /// подписанные Th-234 пики подтверждали радиевую часть ряда, которой в
        /// стекле нет. Отвергнутая редакция 19.10.2026 — группы ПО ОТРЕЗКАМ, своими
        /// группировками, — ломала точечный Th-228 (`G1S24_Th228_P5`: 4/9 → 4/14):
        /// где линии двух отрезков перекрываются, а пика нет, неподтверждённых
        /// групп становилось две вместо одной.
        ///
        /// ⚠ История. До 19.10.2026 обрыв ставился ОДИН — на уровне глубины, у
        /// которого больше всего групп ожидалось и не подтвердилось, — и из двух
        /// кусков брался больший, держащий порог. На урановом стекле худшим
        /// уровнем выходил Bi-214 (17 из 18), Pb-214 с восемью пустыми группами
        /// оставался в «голове», и она набирала 2/14; у U-235 тот же единственный
        /// обрыв вставал ниже Ac-227 и вносил в состав Pa-231. Ещё раньше (первая
        /// редакция 06.09.2026) лучший связный отрезок подбирался по доле и был
        /// отвергнут замером: наибольшую долю всегда даёт самый короткий кусок.
        /// Здесь кусков не подбирают — границы ставит период, а не удобное число.
        ///
        /// ⚠ Правка строго добавочная: ряд, набравший порог целиком, по отрезкам
        /// не судится вовсе, и равновесные ряды проходят прежним составом.
        /// </summary>
        static void Head(List<Model> models, double coverage, Report report)
        {
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (model.Depths == null || model.Depths.Count == 0
                    || evidence.Expected == 0)
                {
                    continue;
                }

                // ⛔ РЯД, НАБРАВШИЙ ПОРОГ ЦЕЛИКОМ, НЕ ОБОРВАН — и подцепочка у
                // него не ищется вовсе. Мера правки `S65` прямо требует «поднять
                // шесть названных спектров, НЕ ТРОНУВ равновесных рядов», и
                // оговорка делает правку строго добавочной.
                if (evidence.Coverage >= coverage)
                {
                    continue;
                }

                var sample = new FsaSampleLibrary.Report();
                FsaSampleLibrary.ChainSegmentation segments = FsaSampleLibrary.ChainSegments(evidence.Nucid, sample);
                foreach (string note in sample.Notes)
                {
                    report.Notes.Add(note);
                }

                if (segments.Order.Count < 2)
                {
                    // Долгоживущих членов под корнем нет — резать не по чему.
                    continue;
                }

                // Чьим отрезкам засчитаны группы.
                var have = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var hit = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (Group group in model.Groups)
                {
                    group.Segment = "";
                    if (!group.Expected)
                    {
                        continue;
                    }

                    string segment = null;
                    if (group.Matched && group.Peak != null && group.Peak.Nuclide != null)
                    {
                        string labelled = FsaSampleLibrary.NucidOf(group.Peak.Nuclide.NuclideName ?? "");
                        if (labelled.Length > 0)
                        {
                            segments.SegmentOf.TryGetValue(labelled, out segment);
                        }
                    }

                    if (segment == null && !segments.SegmentOf.TryGetValue(group.Owner, out segment))
                    {
                        continue;
                    }

                    group.Segment = segment;
                    int sum;
                    have[segment] = (have.TryGetValue(segment, out sum) ? sum : 0) + 1;
                    if (group.Matched)
                    {
                        hit[segment] = (hit.TryGetValue(segment, out sum) ? sum : 0) + 1;
                    }
                }

                // Приговор каждому отрезку.
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var silent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var verdicts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string head in segments.Order)
                {
                    int e, m;
                    have.TryGetValue(head, out e);
                    hit.TryGetValue(head, out m);
                    if (m >= 2 && e > 0 && (double)m / e >= coverage)
                    {
                        present.Add(head);
                        verdicts[head] = "есть";
                    }
                    else if (e * coverage > 1.0)
                    {
                        verdicts[head] = "нет";
                    }
                    else
                    {
                        silent.Add(head);
                        verdicts[head] = "молчит";
                    }
                }

                foreach (string head in segments.Order)
                {
                    var members = new List<string>();
                    foreach (KeyValuePair<string, string> of in segments.SegmentOf)
                    {
                        if (model.Members.Contains(of.Key)
                            && string.Equals(of.Value, head, StringComparison.OrdinalIgnoreCase))
                        {
                            members.Add(of.Key);
                        }
                    }

                    if (members.Count == 0)
                    {
                        continue;
                    }

                    int e, m;
                    have.TryGetValue(head, out e);
                    hit.TryGetValue(head, out m);
                    evidence.BySegment.Add(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] {2}/{3} {4}",
                                                         head, string.Join("+", members.ToArray()), m, e, verdicts[head]));
                }

                if (present.Count == 0)
                {
                    continue;
                }

                // Взятые отрезки: «есть» и «молчащие» между ними.
                var taken = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
                foreach (string head in silent)
                {
                    bool fedByTaken = false, feedsTaken = false;
                    foreach (string other in present)
                    {
                        fedByTaken |= Reaches(segments, other, head);
                        feedsTaken |= Reaches(segments, head, other);
                    }

                    if (fedByTaken && feedsTaken)
                    {
                        taken.Add(head);
                    }
                }

                foreach (string member in model.Members)
                {
                    string segment;
                    if (segments.SegmentOf.TryGetValue(member, out segment) && taken.Contains(segment))
                    {
                        evidence.Head.Add(member);
                    }
                }

                if (evidence.Head.Count == 0 || evidence.Head.Count == model.Members.Count)
                {
                    evidence.Head.Clear();
                    continue;
                }

                foreach (string head in taken)
                {
                    model.HeadSegments.Add(head);
                    int e, m;
                    have.TryGetValue(head, out e);
                    hit.TryGetValue(head, out m);
                    evidence.HeadExpected += e;
                    evidence.HeadMatched += m;
                }

                // Где оборван: отрезки, питающие взятый (сверху), и питаемые им (снизу).
                var above = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var below = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, HashSet<string>> edge in segments.Below)
                {
                    foreach (string child in edge.Value)
                    {
                        if (taken.Contains(child) && !taken.Contains(edge.Key))
                        {
                            above.Add(edge.Key);
                        }

                        if (taken.Contains(edge.Key) && !taken.Contains(child))
                        {
                            below.Add(child);
                        }
                    }
                }

                evidence.Cut = string.Join("+", above.ToArray());
                evidence.CutBelow = string.Join("+", below.ToArray());
                evidence.HeadCoverage = evidence.HeadExpected > 0
                    ? (double)evidence.HeadMatched / evidence.HeadExpected : 0.0;
            }

            report.Notes.Add("оборванный ряд: подцепочка разобрана у "
                             + CountHeads(models) + " кандидатов");
        }

        /// <summary>(`AMBER236`) Отрезок <paramref name="to"/> питается отрезком <paramref name="from"/> (через любые промежуточные).</summary>
        static bool Reaches(FsaSampleLibrary.ChainSegmentation segments, string from, string to)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                HashSet<string> children;
                if (!segments.Below.TryGetValue(current, out children))
                {
                    continue;
                }

                foreach (string child in children)
                {
                    if (string.Equals(child, to, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (seen.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return false;
        }

        static int CountHeads(List<Model> models)
        {
            int count = 0;
            foreach (Model model in models)
            {
                if (model.Evidence.Head.Count > 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Доля, по которой кандидата СУДЯТ: своя у ряда целого, головная у
        /// оборванного. Одна функция на приём и на порядок приёма — иначе
        /// жадность разбирала бы кандидатов в одном порядке, а принимала по
        /// другому числу.
        /// </summary>
        static double Judged(FsaParentEvidence evidence)
        {
            return double.IsNaN(evidence.HeadCoverage)
                ? evidence.Coverage
                : Math.Max(evidence.Coverage, evidence.HeadCoverage);
        }

        /// <summary>
        /// Жадный приём по убыванию доли — и проверка НОВИЗНЫ против уже
        /// принятых.
        ///
        /// ⛔ (`S66`) ВТОРОГО ПУТИ В СОСТАВ — «неспутываемая главная линия на
        /// месте», якоря — БОЛЬШЕ НЕТ: снят 12.09.2026 решением Amber, дословно
        /// «Снять якорь, новизну оставить». Он был заведён ради бедных линиями
        /// нуклидов (Cs-137: четыре группы, три из них рентген, доля 1/4) — и
        /// перемерен полосой П30 на 131 спектре корпуса ОДНИМ двоичным файлом
        /// (`FsaInferProbeF51 --no-anchor`): состав не изменился НИ У ОДНОГО
        /// (принятых родителей 180 = 180), менялся только текст отчёта у 69.
        /// При пороге 30 % нуклида с долей 1/4 на корпусе нет, у Cs-137 доля
        /// 1/3 проходит и без него. Признак без потребителя снят ЦЕЛИКОМ, а не
        /// оставлен ключом «на всякий случай», — так велела сама строка;
        /// прежняя ветка — в истории до 12.09.2026 (`Anchor`, `AnchorKev`,
        /// `AnchorDominance`).
        ///
        /// ⛔ ВТОРОЕ УСЛОВИЕ, И ОНО ПРО ГЛАВНЫЙ ОСТАВШИЙСЯ КЛАСС ФАНТОМОВ.
        /// Доля судит кандидата ПООДИНОЧКЕ, а фантом живёт не один: он
        /// садится на структуру, которую уже объяснил кто-то другой. Измерено
        /// 18.08.2026 при пороге 30 %: в ториевом спектре `ASN16_Th232` Eu-152
        /// набирает 3 группы из 8 — и все три стоят на линиях самого тория
        /// (121.78 против 129.06 у Ac-228, 344.3 против 338.32, 964.1 против
        /// 968.97), то есть НИ ОДНОЙ своей структуры не приносит. Ровно тот же
        /// механизм, что у фантома Pu-238 из `N18`. Тем же замером П30 (`S66`,
        /// 12.09.2026) новизна держит 6 фантомов у 5 спектров (`--no-novel`:
        /// I-131, трижды Eu-152, U-235, Ag-108m) — потребитель у неё есть, она
        /// остаётся.
        ///
        /// Поэтому кандидат обязан принести хоть одну СВОЮ группу: такую, рядом
        /// с которой ни у одного уже принятого неродственного родителя нет
        /// ожидаемой линии. Проверка идёт по ПОЛОЖЕНИЮ, а не по подписи —
        /// вопрос здесь другой, чем при подтверждении: не «чей это пик», а
        /// «есть ли уже в составе тот, кто способен светить в этом месте».
        ///
        /// ⚠ Жадность делает порядок значимым, и он выбран не произвольно: по
        /// убыванию доли. Сильнейший кандидат занимает свою структуру первым, и
        /// слабый обязан доказывать себя ОСТАТКОМ. Обратный порядок отдал бы
        /// структуру тому, кто просто оказался раньше в списке пиков.
        /// </summary>
        static void Accept(List<Model> models, double coverage, bool novelty, FsaChainCut cut)
        {
            var accepted = new List<Model>();
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (evidence.Expected == 0)
                {
                    continue;
                }

                if (Judged(evidence) < coverage)
                {
                    evidence.Why = "доля ниже порога";
                    evidence.Verdict = FsaParentVerdict.BelowCoverage;
                    continue;
                }

                if (novelty && !Novel(model, accepted, cut))
                {
                    evidence.Why = "своей структуры не приносит";
                    evidence.Verdict = FsaParentVerdict.NotNovel;
                    continue;
                }

                evidence.Accepted = true;
                evidence.Verdict = FsaParentVerdict.Accepted;
                evidence.Why = !double.IsNaN(evidence.HeadCoverage)
                               && evidence.HeadCoverage > evidence.Coverage
                               && evidence.Coverage < coverage
                    ? "доля выше порога У ПОДЦЕПОЧКИ (ряд оборван)"
                    : "доля выше порога";
                accepted.Add(model);
            }
        }

        /// <summary>
        /// У кандидата есть подтверждённая группа, на месте которой ни один уже
        /// принятый неродственный родитель светить не может.
        ///
        /// Пустой список принятых — новизна есть по определению: первому
        /// доказывать нечего, он и задаёт отсчёт.
        ///
        /// (`AMBER236`) Группы — те, что кандидат ОБЪЯВИТ (<see cref="DeclaredGroups"/>):
        /// у ряда, ограниченного подцепочкой, — группы взятых отрезков. Иначе
        /// голова урана со своим Ra-226 186 кэВ, которого в составе нет, отнимала
        /// бы у U-235 его 185.7 кэВ.
        /// </summary>
        static bool Novel(Model model, List<Model> accepted, FsaChainCut cut)
        {
            foreach (Group group in DeclaredGroups(model, cut))
            {
                if (!group.Matched)
                {
                    continue;
                }

                bool taken = false;
                foreach (Model other in accepted)
                {
                    if (Kin(model, other))
                    {
                        // Родня спорить не может: это одно утверждение, и
                        // старший поглотит младшего в Collapse.
                        continue;
                    }

                    foreach (Group rival in DeclaredGroups(other, cut))
                    {
                        if (rival.Expected
                            && Math.Abs(rival.Energy - group.Energy) <= group.Window)
                        {
                            taken = true;
                            break;
                        }
                    }

                    if (taken)
                    {
                        break;
                    }
                }

                if (!taken)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// (`AMBER236`) Группы линий того, что кандидат объявит библиотеке: у ряда,
        /// ограниченного подцепочкой (<see cref="Restricted"/>), — группы, засчитанные
        /// ВЗЯТЫМ отрезкам (<see cref="Group.Segment"/>); иначе — все группы ряда.
        /// </summary>
        static IEnumerable<Group> DeclaredGroups(Model model, FsaChainCut cut)
        {
            if (!Restricted(model, cut) || model.HeadSegments.Count == 0)
            {
                return model.Groups;
            }

            return model.Groups.Where(group => model.HeadSegments.Contains(group.Segment));
        }

        /// <summary>Родня: корень одного входит в состав другого.</summary>
        static bool Kin(Model a, Model b)
        {
            return a.Members.Contains(b.Evidence.Nucid) || b.Members.Contains(a.Evidence.Nucid);
        }

        /// <summary>
        /// Принятый родитель, целиком лежащий внутри другого принятого, из
        /// состава убирается: объявить и Th-232, и Tl-208 значит предъявить
        /// фиту один и тот же образ дважды.
        ///
        /// Побеждает СТАРШИЙ — тот, в чей ряд входит другой. Так велит само
        /// правило Amber: набрал родитель — берём ВЕСЬ его изотопный состав, а
        /// состав старшего дочернего уже содержит.
        ///
        /// ⛔ (`A205`) «Состав старшего» — это ТО, ЧТО УЙДЁТ В БИБЛИОТЕКУ, а не
        /// весь его ряд. У старшего с оборванным рядом
        /// (<see cref="FsaChainCut.Only"/>) в состав идёт подцепочка, и
        /// поглощать младшего, В НЕЁ НЕ ВОШЕДШЕГО, значит потерять его линии
        /// молча: младший объявлен непринятым «входит в ряд», а старший этих
        /// линий не несёт. Поэтому поглощение судит по подцепочке, когда она
        /// есть, и по всему ряду, когда её нет.
        /// </summary>
        /// <summary>
        /// (`A205`) Состав, который этот кандидат ОБЪЯВИТ библиотеке: подцепочка
        /// при <see cref="FsaChainCut.Only"/>, иначе весь ряд. Одно место на
        /// поглощение и на сборку спецификации — разойдясь, они дали бы
        /// «поглощён старшим, которого в составе нет».
        /// </summary>
        static ICollection<string> Declared(Model model, FsaChainCut cut)
        {
            return Restricted(model, cut)
                ? (ICollection<string>)new HashSet<string>(model.Evidence.Head,
                                                           StringComparer.OrdinalIgnoreCase)
                : model.Members;
        }

        /// <summary>Ряд этого кандидата уйдёт в состав ОГРАНИЧЕННЫМ подцепочкой.</summary>
        static bool Restricted(Model model, FsaChainCut cut)
        {
            return cut == FsaChainCut.Only && model.Evidence.Head.Count > 0
                   && model.Evidence.Head.Count < model.Members.Count;
        }

        static void Collapse(List<Model> models, FsaChainCut cut)
        {
            foreach (Model model in models)
            {
                if (!model.Evidence.Accepted)
                {
                    continue;
                }

                foreach (Model elder in models)
                {
                    if (elder == model || !elder.Evidence.Accepted)
                    {
                        continue;
                    }

                    if (Declared(elder, cut).Contains(model.Evidence.Nucid)
                        && !Declared(model, cut).Contains(elder.Evidence.Nucid))
                    {
                        model.Evidence.Accepted = false;
                        model.Evidence.Why = "входит в ряд " + elder.Evidence.Name;
                        model.Evidence.Verdict = FsaParentVerdict.InChain;
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // AMBER221: родители сета, не вошедшие в состав
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER221`, решение Amber 12.10.2026 вопросником, дословно: «Чинить
        /// сейчас») РОДИТЕЛИ АКТИВНОГО СЕТА, НЕ ВОШЕДШИЕ В СОСТАВ РАЗБОРА, — с
        /// причиной, для строки отчёта FSA.
        ///
        /// ⛔ До этого дня такой родитель выпадал МОЛЧА: человек добавлял K-40 в
        /// сет, видел подпись «K-40» над пиком — и ни компонента, ни слова в
        /// отчёте. Причина жила только в журнале трассировки (<see cref="Report"/>).
        ///
        /// Родитель — метка ряда (<see cref="NuclideDefinition.Chain"/>), а без неё
        /// сам нуклид: так же их берёт вывод состава (<c>Candidates</c>). Рентген
        /// элемента родителем не бывает и не перечисляется. Записи, которые поиск
        /// пиков не читает (скрытые, без энергии), не перечисляются тоже: такой
        /// член сета пика не подпишет никогда, и строка о нём была бы шумом.
        ///
        /// Родитель СЧИТАЕТСЯ ВОШЕДШИМ, если в библиотеке есть его образ — по имени
        /// колонки, по корню ряда (<see cref="FsaComponent.DecayChainRoot"/>) или
        /// по линии члена (колонка равновесного ряда несёт линии членов под их
        /// именами), — либо вывод принял его или поглотил рядом старшего.
        /// <paramref name="report"/> = null — состав собран по подписям пиков
        /// (источник «From labels of detected peaks»): там вошедшим считается
        /// всякий родитель, которым подписан хоть один пик.
        /// </summary>
        public static List<FsaSetOmission> SetOmissions(NuclideSet set,
                                                        IEnumerable<NuclideDefinition> definitions,
                                                        Report report,
                                                        IEnumerable<Peak> peaks,
                                                        IEnumerable<FsaComponent> library)
        {
            var result = new List<FsaSetOmission>();
            if (set == null || definitions == null)
            {
                return result;
            }

            var parents = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NuclideDefinition definition in definitions)
            {
                if (definition == null || definition.Sets == null || !definition.Sets.Contains(set.Id)
                    || !definition.Visible || definition.Energy == 0.0
                    || string.IsNullOrEmpty(definition.Name)
                    || NuclideDefinition.IsElementXrayName(definition.Name))
                {
                    continue;
                }

                string parent = string.IsNullOrEmpty(definition.Chain)
                    ? definition.NuclideName
                    : definition.Chain;
                string nucid = FsaSampleLibrary.NucidOf(parent);
                if (nucid.Length > 0 && seen.Add(nucid))
                {
                    parents.Add(nucid);
                }
            }

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (library != null)
            {
                foreach (FsaComponent component in library)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    present.Add(component.Name ?? "");
                    if (!string.IsNullOrEmpty(component.DecayChainRoot))
                    {
                        present.Add(component.DecayChainRoot);
                    }

                    foreach (FsaLine line in component.Lines)
                    {
                        if (line != null && !string.IsNullOrEmpty(line.Nuclide))
                        {
                            present.Add(line.Nuclide);
                        }
                    }
                }
            }

            var labelled = new List<Peak>();
            if (peaks != null)
            {
                foreach (Peak peak in peaks)
                {
                    if (peak != null)
                    {
                        labelled.Add(peak);
                    }
                }
            }

            foreach (string nucid in parents)
            {
                string pretty = FsaSampleLibrary.PrettyName(nucid);
                if (present.Contains(pretty))
                {
                    continue;
                }

                FsaParentEvidence evidence = null;
                if (report != null)
                {
                    foreach (FsaParentEvidence candidate in report.Candidates)
                    {
                        if (string.Equals(candidate.Nucid, nucid, StringComparison.OrdinalIgnoreCase))
                        {
                            evidence = candidate;
                            break;
                        }
                    }

                    if (evidence != null
                        && (evidence.Accepted || evidence.Verdict == FsaParentVerdict.InChain))
                    {
                        continue;
                    }
                }
                else if (labelled.Exists(peak => Belongs(peak, nucid)))
                {
                    continue;
                }

                result.Add(new FsaSetOmission
                {
                    Nuclide = pretty,
                    Reason = evidence != null ? evidence.Verdict : FsaParentVerdict.None,
                    Matched = evidence != null ? evidence.Matched : 0,
                    Expected = evidence != null ? evidence.Expected : 0,
                    Coverage = evidence != null ? Judged(evidence) : 0.0,
                    Threshold = report != null ? report.Coverage : 0.0
                });
            }

            return result;
        }
    }
}
