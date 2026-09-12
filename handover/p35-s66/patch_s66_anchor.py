# П35 (S66), 12.09.2026: снять якорь приёма состава «Из NucBase» — решение Amber
# «Снять якорь, новизну оставить». Ветка якоря — прочь целиком (мёртвый код: 0/131,
# потребителя нет). CRLF сохраняется; каждая замена обязана встретиться ровно один раз.
import io, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

def patch(path, pairs):
    with io.open(path, encoding='utf-8', newline='') as f:
        text = f.read()
    for old, new in pairs:
        old = old.replace('\n', '\r\n'); new = new.replace('\n', '\r\n')
        n = text.count(old)
        if n != 1:
            raise SystemExit('%s: образец встречается %d раз, а не 1:\n%s' % (path, n, old[:200]))
        text = text.replace(old, new)
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(text)
    print('исправлен', path, '—', len(pairs), 'замен')

INF = ROOT + r'\BecquerelMonitor\FullSpectrumAnalysis\FsaCompositionInference.cs'
patch(INF, [
# E1: поле AnchorKev
("""        /// <summary>Энергия неспутываемой линии, подтвердившей родителя; NaN — такой нет.</summary>
        public double AnchorKev = double.NaN;

""", ""),
# E2: слово «якорь» в тексте отчёта
("""            if (!double.IsNaN(this.AnchorKev))
            {
                text.AppendFormat(CultureInfo.InvariantCulture, ", якорь {0:F1} кэВ", this.AnchorKev);
            }

""", ""),
# E3: документация порога
("""        /// Наименьшая доля ожидаемо-различимых групп, при которой родитель
        /// берётся в состав без якоря.
""", """        /// Наименьшая доля ожидаемо-различимых групп, при которой родитель
        /// берётся в состав. (Второго пути в состав — «якоря» — с 12.09.2026
        /// нет, `S66`; см. <see cref="Accept"/>.)
"""),
# E4: константа AnchorDominance с документацией
("""        /// <summary>
        /// Доля веса якоря, начиная с которой НЕПОДТВЕРЖДЁННАЯ группа отменяет
        /// якорь: «ничего сравнимо яркого не пропало».
        ///
        /// Без этой оговорки якорь пропускал фантомы с долей 10 % — Am-243 в
        /// граните 18.08.2026 прошёл единственной линией 74.7 кэВ при девяти
        /// ненайденных. Смысл оговорки прямой: довод «главная линия на месте»
        /// стоит чего-то, только если рядом с ней НЕ отсутствует другая, почти
        /// такая же яркая. Отсутствие такой — довод против родителя, и он
        /// сильнее.
        /// </summary>
        public const double AnchorDominance = 0.5;

""", ""),
# E5a: перегрузка без cut
("""                                          double coverage,
                                          bool anchors,
                                          bool novelty,
                                          out Report report)
        {
            return Infer(peaks, resultData, coverage, anchors, novelty, DefaultCut,
                         out report);
        }
""", """                                          double coverage,
                                          bool novelty,
                                          out Report report)
        {
            return Infer(peaks, resultData, coverage, novelty, DefaultCut,
                         out report);
        }
"""),
# E5b: полная перегрузка
("""                                          double coverage,
                                          bool anchors,
                                          bool novelty,
                                          FsaChainCut cut,
                                          out Report report)
""", """                                          double coverage,
                                          bool novelty,
                                          FsaChainCut cut,
                                          out Report report)
"""),
# E5c: вызов Score
("""            Score(models, minSnr, anchors);
""", """            Score(models, minSnr);
"""),
# E5d: короткие перегрузки
("""        /// <summary>То же с порогом прогона и якорями.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          double coverage, bool anchors, out Report report)
        {
            return Infer(peaks, resultData, coverage, anchors, true, out report);
        }

        /// <summary>То же с порогом прогона, якорями и проверкой новизны.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          double coverage, out Report report)
        {
            return Infer(peaks, resultData, coverage, true, true, out report);
        }

        /// <summary>То же с порогом по умолчанию.</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks, ResultData resultData,
                                          out Report report)
        {
            return Infer(peaks, resultData, DefaultCoverage, true, true, out report);
        }
""", """        /// <summary>То же с порогом прогона и проверкой новизны.</summary>
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
"""),
# E6: Score
("""        static void Score(List<Model> models, double minSnr, bool anchors)
""", """        static void Score(List<Model> models, double minSnr)
"""),
("""                evidence.Coverage = evidence.Expected > 0
                    ? (double)evidence.Matched / evidence.Expected : 0.0;
                evidence.AnchorKev = anchors ? Anchor(model, models) : double.NaN;
""", """                evidence.Coverage = evidence.Expected > 0
                    ? (double)evidence.Matched / evidence.Expected : 0.0;
"""),
# E7: Accept — документация и тело
("""        /// ⛔ ТРЕТЬЕ УСЛОВИЕ, И ОНО ПРО ГЛАВНЫЙ ОСТАВШИЙСЯ КЛАСС ФАНТОМОВ.
        /// Доля и якорь судят кандидата ПООДИНОЧКЕ, а фантом живёт не один: он
""", """        /// ⛔ (`S66`) ВТОРОГО ПУТИ В СОСТАВ — «неспутываемая главная линия на
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
"""),
("""        /// механизм, что у фантома Pu-238 из `N18`.
        ///
        /// Поэтому кандидат обязан принести хоть одну СВОЮ группу: такую, рядом
""", """        /// механизм, что у фантома Pu-238 из `N18`. Тем же замером П30 (`S66`,
        /// 12.09.2026) новизна держит 6 фантомов у 5 спектров (`--no-novel`:
        /// I-131, трижды Eu-152, U-235, Ag-108m) — потребитель у неё есть, она
        /// остаётся.
        ///
        /// Поэтому кандидат обязан принести хоть одну СВОЮ группу: такую, рядом
"""),
("""                bool anchored = !double.IsNaN(evidence.AnchorKev);
                if (!anchored && Judged(evidence) < coverage)
""", """                if (Judged(evidence) < coverage)
"""),
("""                evidence.Why = anchored
                    ? "неспутываемая линия на месте"
                    : (!double.IsNaN(evidence.HeadCoverage)
                       && evidence.HeadCoverage > evidence.Coverage
                       && evidence.Coverage < coverage)
                        ? "доля выше порога У ПОДЦЕПОЧКИ (ряд оборван)"
                        : "доля выше порога";
""", """                evidence.Why = !double.IsNaN(evidence.HeadCoverage)
                               && evidence.HeadCoverage > evidence.Coverage
                               && evidence.Coverage < coverage
                    ? "доля выше порога У ПОДЦЕПОЧКИ (ряд оборван)"
                    : "доля выше порога";
"""),
# E8: метод Anchor целиком
("""        /// <summary>
        /// Неспутываемая ГЛАВНАЯ линия родителя, стоящая на месте, — или NaN.
        ///
        /// Второй путь в состав, и нужен он ровно одному классу нуклидов —
        /// бедным линиями. У Cs-137 в рабочем окне четыре группы: 661.66 и три
        /// рентгеновские, которых сцинтиллятор внизу шкалы обычно не берёт.
        /// Доля выходит 1/4, порога не хватает, а нуклид в спектре стоит и
        /// виден за версту. Богатым родителям этот путь не нужен: у настоящего
        /// Th-232 доля и так за две трети.
        ///
        /// ⛔ ЯКОРЕМ СЛУЖИТ ТОЛЬКО САМАЯ СИЛЬНАЯ ОЖИДАЕМАЯ ГРУППА, и это не
        /// строгость ради строгости. Первый прогон брал якорем любую заметную
        /// линию — и якорь нашёлся у ВСЕХ кандидатов подряд, включая Ag-108m в
        /// чароите и Am-241 в урановом стекле; последний тянул за собой ряд
        /// нептуния и тринадцать выдуманных образов. Правило «главная линия»
        /// само по себе содержит нужную проверку: если родитель есть, ярче
        /// всего видно именно её, и её отсутствие есть довод против него, а не
        /// повод искать якорь послабее.
        ///
        /// ⚠ «Не с чем спутать» проверяется по ДРУГИМ кандидатам, и родня из
        /// проверки исключается: если поиск подписал пики и «Tl-208 (Th-232)»,
        /// и голым «Tl-208», кандидатов выйдет два, и они отняли бы якорь друг
        /// у друга — при том что физически это одно утверждение. Родня
        /// опознаётся по вхождению корня в состав соседа.
        /// </summary>
        static double Anchor(Model model, List<Model> models)
        {
            Group main = null;
            foreach (Group group in model.Groups)
            {
                if (group.Expected && (main == null || group.Weight > main.Weight))
                {
                    main = group;
                }
            }

            if (main == null || !main.Matched)
            {
                return double.NaN;
            }

            // Ничего сравнимо яркого не пропало — иначе якорь не довод.
            foreach (Group group in model.Groups)
            {
                if (group.Expected && !group.Matched
                    && group.Weight >= AnchorDominance * main.Weight)
                {
                    return double.NaN;
                }
            }

            foreach (Model other in models)
            {
                if (other == model || Kin(model, other))
                {
                    continue;
                }

                foreach (Group rival in other.Groups)
                {
                    // Соперник учитывается, только если он сам ожидаем: линия в
                    // тысячную процента не спутывает ничего, её просто не видно.
                    if (rival.Expected && Math.Abs(rival.Energy - main.Energy) <= main.Window)
                    {
                        return double.NaN;
                    }
                }
            }

            return main.Energy;
        }

""", ""),
])

F51 = ROOT + r'\tools\effmaker\probes\FsaInferProbeF51.cs'
patch(F51, [
("""    ///                    [--no-anchor] [--no-novel] [--modal-control]
""", """    ///                    [--no-novel] [--modal-control]
"""),
("""    /// а не догадка о нём. `--no-anchor` / `--no-novel` (`S66`, полоса П30
    /// 12.09.2026) выключают два дополнительных условия приёма `S57` — якорь и
    /// проверку новизны; это те же рычаги, что `--no-infer-anchor` /
    /// `--no-infer-novel` у `CorpusFsaProbe`, только тот на корпусе режим
    /// `--lib=infer` отвергает кодом 12, а экран «Из NucBase» меряется здесь.
    /// Любой из трёх ключей (`--cut=`, `--no-anchor`, `--no-novel`) переводит
    /// вызов на полную перегрузку `Infer`; остальные два довода при этом равны
    /// умолчанию приложения (`DefaultCut`, якоря вкл, новизна вкл).
""", """    /// а не догадка о нём. `--no-novel` (`S66`, полоса П30 12.09.2026)
    /// выключает проверку новизны — второе условие приёма `S57`; это тот же
    /// рычаг, что `--no-infer-novel` у `CorpusFsaProbe`, только тот на корпусе
    /// режим `--lib=infer` отвергает кодом 12, а экран «Из NucBase» меряется
    /// здесь. Ключа `--no-anchor` больше нет: якорь снят из приёма 12.09.2026
    /// решением Amber (`S66`, полоса П35) — этой же пробой он перемерен на 131
    /// спектре и не менял состав ни у одного. Любой из двух ключей (`--cut=`,
    /// `--no-novel`) переводит вызов на полную перегрузку `Infer`; второй
    /// довод при этом равен умолчанию приложения (`DefaultCut`, новизна вкл).
"""),
("""            bool anchors = true, novelty = true;
""", """            bool novelty = true;
"""),
("""                else if (a == "--no-anchor") anchors = false;
""", ""),
("""            if (cutName == null && anchors && novelty)
""", """            if (cutName == null && novelty)
"""),
("""                spec = FsaCompositionInference.Infer(peaks, rd, FsaCompositionInference.DefaultCoverage,
                                                    anchors, novelty, cut, out report);
""", """                spec = FsaCompositionInference.Infer(peaks, rd, FsaCompositionInference.DefaultCoverage,
                                                    novelty, cut, out report);
"""),
("""            Console.WriteLine("правило обрыва ряда: {0}{1}; якоря {2}, новизна {3}", report.Cut,
                              cutName == null ? " (умолчание приложения)" : " (--cut=" + cutName + ")",
                              anchors ? "вкл" : "ВЫКЛ (--no-anchor)",
                              novelty ? "вкл" : "ВЫКЛ (--no-novel)");
""", """            Console.WriteLine("правило обрыва ряда: {0}{1}; новизна {2}", report.Cut,
                              cutName == null ? " (умолчание приложения)" : " (--cut=" + cutName + ")",
                              novelty ? "вкл" : "ВЫКЛ (--no-novel)");
"""),
])
