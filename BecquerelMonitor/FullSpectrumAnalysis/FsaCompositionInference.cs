using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Энергия неспутываемой линии, подтвердившей родителя; NaN — такой нет.</summary>
        public double AnchorKev = double.NaN;

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
        /// Члены ряда, стоящие СРАЗУ НАД подцепочкой, — там она и обрывается
        /// сверху. Пусто — подцепочка начинается с корня (случай `S65`).
        /// </summary>
        public string Cut = "";

        /// <summary>
        /// (`A205`) Члены ряда, стоящие СРАЗУ ПОД подцепочкой. Пусто —
        /// подцепочка доходит до конца ряда (случай точечного Th-228).
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

        /// <summary>Родитель взят в состав.</summary>
        public bool Accepted;

        /// <summary>Почему принят или отвергнут — человеку и в журнал.</summary>
        public string Why = "";

        public override string ToString()
        {
            var text = new StringBuilder();
            // Сырой счёт подписанных пиков печатается рядом с долей нарочно:
            // именно он был первым кандидатом в критерий и именно он не годится
            // (`S57`). Видя оба числа вместе, читатель видит и почему.
            text.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:P0} ({2}/{3}, пиков {4})",
                              this.Name, this.Coverage, this.Matched, this.Expected,
                              this.LabelledPeaks);
            if (!double.IsNaN(this.AnchorKev))
            {
                text.AppendFormat(CultureInfo.InvariantCulture, ", якорь {0:F1} кэВ", this.AnchorKev);
            }

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
        /// берётся в состав без якоря.
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
        /// ⛔ Допуск подписи (`Tolerance`) сюда НЕ ГОДИТСЯ, и это выяснилось
        /// первым же прогоном. Он задан в ПРОЦЕНТАХ энергии и в поставочных
        /// конфигурациях равен 10…11 — то есть ±260 кэВ на линии 2614. С таким
        /// окном подтверждалось всё подряд: фантомный Eu-152 набирал в ториевом
        /// спектре 15 групп из 16. Допуск отвечает за СНОС ШКАЛЫ при подписи, а
        /// здесь нужна РАЗРЕШИМОСТЬ, и это разные величины.
        /// </summary>
        const double MinWindowFraction = 0.005;

        /// <summary>
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
                                          bool anchors,
                                          bool novelty,
                                          out Report report)
        {
            return Infer(peaks, resultData, coverage, anchors, novelty, DefaultCut,
                         out report);
        }

        /// <summary>То же с разбором ОБОРВАННОГО ряда (`S65`).</summary>
        public static FsaSampleSpec Infer(IEnumerable<Peak> peaks,
                                          ResultData resultData,
                                          double coverage,
                                          bool anchors,
                                          bool novelty,
                                          FsaChainCut cut,
                                          out Report report)
        {
            report = new Report { Coverage = coverage, Cut = cut };
            var spec = new FsaSampleSpec();
            if (resultData == null)
            {
                return spec;
            }

            // ⛔ Кривая спектра нужна библиотеке ровно за одним — назначить пол
            // полосы (`FsaBandMode.LibraryToFitByCurve`, решение Amber
            // 27.08.2026). Сам расчёт живёт в `FsaEfficiency.FloorAtFraction` и
            // больше нигде; здесь только присваивание, чтобы у решения «где пол»
            // не завелась вторая копия.
            spec.Efficiency = FsaEfficiency.FromConfig(resultData.Efficiency);

            // ⛔ `A73`: порог АЦП САМОГО спектра — второй источник пола полосы,
            // работающий там, где кривой нет. Здесь, как и с кривой, ОДНО
            // присваивание; расчёт живёт в `FsaBand.AdcFloorOf`.
            spec.AdcFloorKev = FsaBand.AdcFloorOf(resultData.EnergySpectrum);

            // Окно — рабочий диапазон САМОГО поиска пиков. Иначе знаменатель
            // доли считался бы по линиям, которых прибор не искал: у ASN16 низ
            // стоит на 28.6 кэВ, и весь L-рентген ниже него в «ожидаемое»
            // попадать не вправе.
            var peakConfig = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (peakConfig != null && peakConfig.Max_Range > peakConfig.Min_Range)
            {
                spec.MinEnergyKev = peakConfig.Min_Range;
                spec.MaxEnergyKev = peakConfig.Max_Range;
            }

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

            // Вещества вокруг кванта — из геометрии, если она есть. Тот же
            // источник и те же пороги, что у объявленного состава
            // (`CorpusFsaProbe.SpecOf`): второй источник правды двигал бы
            // энергию пика вылета, а она есть разность с Kα кристалла.
            EfficiencyConfigData efficiencyConfig = resultData.Efficiency;

            // ⛔ `A276`: вещество кристалла, названное человеком у ПРИБОРА, —
            // запасной источник массовых долей там, где геометрии нет. Здесь
            // ТОЛЬКО присваивание ссылки: старшинство источников держит
            // `FsaSampleLibrary.CrystalFractionsOf` и больше никто, поэтому
            // условия «а есть ли геометрия» тут нет и быть не должно — второе
            // место, решающее тот же вопрос, разошлось бы с первым.
            if (resultData.DeviceConfig != null)
            {
                spec.CrystalMaterialName = resultData.DeviceConfig.CrystalMaterialName;
            }

            if (efficiencyConfig != null && efficiencyConfig.HasGeometry)
            {
                GeometryModel geometry = efficiencyConfig.Geometry;
                // ⚠ У КРИСТАЛЛА окна по Kα нет, и это не оплошность: элемент
                // кристалла не только светит сам, но и уносит энергию вылетом,
                // а пик вылета стоит на E − Kα, то есть внутри окна даже когда
                // сама Kα ниже его низа (измерено 18.08.2026 на ASN16).
                // Кристалл идёт целиком — с долями и именем вещества (`S84`):
                // образ вылета у него ОДИН, и соотношение его членов задаёт
                // вещество, а не фит.
                FsaSampleLibrary.DescribeCrystal(spec, geometry.Crystal, 0.01,
                    EfficiencySimulator.ScintillatorNameOf(geometry));
                spec.SampleElements.AddRange(FsaSampleLibrary.HeavyElementsOf(
                    geometry.Source, 0.01, spec.MinEnergyKev, spec.MaxEnergyKev));
            }

            var candidates = Candidates(found, spec, report);
            var models = new List<Model>();
            foreach (string nucid in candidates)
            {
                Model model = Build(nucid, spec, resultData, found, report);
                if (model != null)
                {
                    models.Add(model);
                }
            }

            Score(models, minSnr, anchors);
            Breakdown(models);
            if (cut != FsaChainCut.Whole)
            {
                Head(models, coverage, report);
            }

            // Порядок РЕШАЕТ, а не украшает: приём идёт жадно, по убыванию
            // доли, и «новизна» кандидата проверяется против уже принятых
            // (см. Accept). Первым читается то, на чём состав держится.
            models.Sort((a, b) => Judged(b.Evidence).CompareTo(Judged(a.Evidence)));
            Accept(models, coverage, novelty);
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
                    spec.Chains.Add(Restricted(model, cut)
                                    ? new FsaSampleChain(model.Evidence.Nucid,
                                                         model.Evidence.Head.ToArray())
                                    : new FsaSampleChain(model.Evidence.Nucid));
                }
                else
                {
                    spec.Nuclides.Add(model.Evidence.Nucid);
                }
            }

            return spec;
        }

        /// <summary>То же с порогом прогона и якорями.</summary>
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
        static List<string> Candidates(List<Peak> peaks, FsaSampleSpec spec, Report report)
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
                if (string.IsNullOrEmpty(parent))
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
            public double Snr = double.NaN;
            public bool Expected;

            /// <summary>
            /// (`S65`) Член ряда, чья линия в группе сильнейшая, — тот же, чья
            /// энергия стала <see cref="Energy"/>. Группу «чью» спрашивают
            /// только при разборе обрыва, и спрашивают именно про сильнейшую:
            /// слабый подмешавшийся сосед наблюдаемого пика не даёт.
            /// </summary>
            public string Owner = "";

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
        }

        static Model Build(string nucid, FsaSampleSpec spec, ResultData resultData,
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
            FsaSampleLibrary.CollectChain(new FsaSampleChain(nucid), spec.MinChainBranch,
                                          spec.MinIsomerBranch, branch, sample);
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
            Collect(model, lines, resultData, peaks);
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
                            List<Peak> peaks)
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
                    weight *= efficiency.Eval(line.Energy);
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
                    if (weight > currentTop)
                    {
                        currentTop = weight;
                        current.Energy = line.Energy;
                        current.Owner = line.Member;
                    }

                    continue;
                }

                current = new Group { Energy = line.Energy, Weight = weight, Owner = line.Member };
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
                    double away = Math.Abs(peak.Energy - group.Energy);
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
        static void Score(List<Model> models, double minSnr, bool anchors)
        {
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (model.Groups.Count == 0)
                {
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
                evidence.AnchorKev = anchors ? Anchor(model, models) : double.NaN;
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
        /// ⛔ ОДНО ПРАВИЛО НА ДВА СЛУЧАЯ, и второй копии рядом быть не должно
        /// (решение Amber 06.09.2026 по `A205`: «тем же правилом, что уже
        /// принято для `S65`»). Случая ровно два, и они зеркальны:
        ///
        ///   `S65`, ряд оборван СНИЗУ — урановое стекло. Уран попал в стекло
        ///   химически очищенным, равновесия ниже радия нет: `Th-234` 93 кэВ и
        ///   `Pa-234m` 1001 кэВ видны прекрасно, а `Bi-214` и `Pb-214` не видны
        ///   вовсе, и `U-238` набирает единицы процентов при пороге 30.
        ///
        ///   `A205`, ряд оборван СВЕРХУ — точечный источник Th-228. В нём есть
        ///   `Ra-224` и всё, что ниже, и НЕТ `Ac-228`. Измерено 06.09.2026 на
        ///   `G1S16_Th228_P5`: у `Th-232` 22 ожидаемые группы, и ЧЕТЫРНАДЦАТЬ
        ///   из них — Ac-228, подтверждённых у него 2. Целый ряд набирает 23 %,
        ///   подцепочка от `Th-228` вниз — 3 из 8, то есть 37.5 %.
        ///
        /// ЧТО ЭТО ЗА ОТРЕЗОК. Ряд разложен по УРОВНЯМ ГЛУБИНЫ от корня
        /// (<see cref="FsaSampleLibrary.ChainDepths"/>): глубина, а не порядок в
        /// словаре, потому что ряд ветвится (212BI даёт и 208TL, и 212PO) и
        /// «выше» у него определено только расстоянием от корня. Обрыв — ОДИН, и
        /// стоит он на уровне, который ряду противоречит сильнее всех: у кого
        /// больше всего групп ожидалось и не подтвердилось. Подцепочек от этого
        /// ровно две — над обрывом и под ним; берётся БОЛЬШАЯ из держащих порог.
        ///
        /// ⛔ ОТРЕЗОК НЕ ПОДБИРАЕТСЯ, И ЭТО ГЛАВНАЯ ОГОВОРКА. Первая редакция
        /// правила искала лучший связный отрезок ростом от лучшего уровня — и
        /// была ОТВЕРГНУТА замером 06.09.2026 по всему корпусу: наибольшую долю
        /// всегда даёт самый короткий кусок ряда, состав менялся на 72 спектрах
        /// из 129, в чистый Co-60 приходил торий тремя членами хвоста, а из
        /// `GS4000_Lu176` пропадал сам лютеций — принятый первым фантом отбирал
        /// у него структуру проверкой новизны. При одном обрыве, поставленном
        /// не поиском удобного числа, таких кусков не бывает.
        ///
        /// ⚠ Прежнее правило (`S65` до 06.09.2026) резало ряд на ПЕРВОМ члене,
        /// у которого нет ни одной подтверждённой группы, и оставляло голову.
        /// На точечном Th-228 оно не работает, и измерено, почему: разбитым там
        /// оказывается `212BI` (2 ожидаемые группы, 0 подтверждённых — его
        /// сильную 727 кэВ забрала в свою группу соседняя линия), а `228AC`
        /// разбитым НЕ считается, потому что 2 группы из 14 у него всё же
        /// подтвердились. Голова выходила 3/17 = 18 %, то есть хуже целого ряда.
        /// Счёт по уровням с ростом от зерна разводит эти два случая тем, что
        /// смотрит на ДОЛЮ уровня, а не на наличие у него хоть одного попадания.
        ///
        /// ⚠ Члены, у которых ожидаемых групп нет вовсе (слишком слабы для
        /// этого спектра), отрезку не мешают и не помогают — сказать про них
        /// нечего, и молчание уликой против родителя не является.
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
                // него не ищется вовсе. Без этой оговорки правило трогало бы
                // РАВНОВЕСНЫЕ ряды: у любого ряда найдётся отрезок с долей
                // выше, чем у целого, и при `Only` состав равновесного тория
                // молча сузился бы до этого отрезка. Мера правки `S65` прямо
                // требует обратного — «поднять шесть названных спектров, НЕ
                // ТРОНУВ равновесных рядов», — и оговорка делает правку
                // строго добавочной: что проходило прежде, проходит тем же
                // составом.
                if (evidence.Coverage >= coverage)
                {
                    continue;
                }

                // Что известно про каждого члена: ожидалось / подтвердилось.
                var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var matched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                MemberCounts(model, expected, matched);

                // Те же числа, сложенные ПО УРОВНЯМ ГЛУБИНЫ: ряд ветвится
                // (212BI даёт и 208TL, и 212PO), и подцепочка режется по
                // уровню, а не по отдельному члену.
                var levelExpected = new Dictionary<int, int>();
                var levelMatched = new Dictionary<int, int>();
                int lowest = int.MaxValue, deepest = int.MinValue;
                foreach (string member in model.Members)
                {
                    int depth;
                    if (!model.Depths.TryGetValue(member, out depth))
                    {
                        continue;
                    }

                    lowest = Math.Min(lowest, depth);
                    deepest = Math.Max(deepest, depth);
                    int have, hit, sum;
                    expected.TryGetValue(member, out have);
                    matched.TryGetValue(member, out hit);
                    levelExpected[depth] = (levelExpected.TryGetValue(depth, out sum) ? sum : 0) + have;
                    levelMatched[depth] = (levelMatched.TryGetValue(depth, out sum) ? sum : 0) + hit;
                }

                if (lowest > deepest)
                {
                    continue;
                }

                // МЕСТО ОБРЫВА — уровень, ПРОТИВОРЕЧАЩИЙ ряду сильнее всех:
                // тот, у кого больше всего групп ожидалось и не подтвердилось.
                // При равенстве берётся верхний — выбор нужен лишь затем, чтобы
                // ход был определён.
                int cut = int.MinValue, worst = 0;
                for (int depth = lowest; depth <= deepest; depth++)
                {
                    int have, hit;
                    if (!levelExpected.TryGetValue(depth, out have) || have == 0)
                    {
                        continue;
                    }

                    levelMatched.TryGetValue(depth, out hit);
                    if (have - hit > worst)
                    {
                        worst = have - hit;
                        cut = depth;
                    }
                }

                if (cut == int.MinValue)
                {
                    continue;
                }

                // Подцепочек ровно ДВЕ — над обрывом и под ним, — и обе целые.
                //
                // ⛔ ОТРЕЗОК НЕ ПОДБИРАЕТСЯ, И ЭТО ГЛАВНАЯ ОГОВОРКА ПРАВИЛА.
                // Перебор всех связных отрезков с выбором лучшего измерен
                // 06.09.2026 на всём корпусе и ОТВЕРГНУТ: наибольшую долю
                // всегда даёт самый короткий кусок ряда, и на 72 спектрах из
                // 129 состав менялся — в чистый Co-60 приходил торий тремя
                // членами хвоста, из `GS4000_Lu176` пропадал сам лютеций.
                // Порог при таком отборе перестаёт что-либо значить. Здесь
                // граница ОДНА, и её ставит не поиск удобного числа, а
                // единственный член, который ряду противоречит.
                int lo, hi;
                if (!BestSide(levelExpected, levelMatched, lowest, deepest, cut, coverage,
                              out lo, out hi))
                {
                    continue;
                }

                foreach (string member in model.Members)
                {
                    int depth;
                    if (model.Depths.TryGetValue(member, out depth) && depth >= lo && depth <= hi)
                    {
                        evidence.Head.Add(member);
                    }
                }

                if (evidence.Head.Count == 0 || evidence.Head.Count == model.Members.Count)
                {
                    evidence.Head.Clear();
                    continue;
                }

                var head = new HashSet<string>(evidence.Head, StringComparer.OrdinalIgnoreCase);
                foreach (Group group in model.Groups)
                {
                    if (!group.Expected || !head.Contains(group.Owner))
                    {
                        continue;
                    }

                    evidence.HeadExpected++;
                    if (group.Matched)
                    {
                        evidence.HeadMatched++;
                    }
                }

                evidence.Cut = CutAt(model, lo > lowest ? lo - 1 : int.MinValue);
                evidence.CutBelow = CutAt(model, hi < deepest ? hi + 1 : int.MinValue);
                evidence.HeadCoverage = evidence.HeadExpected > 0
                    ? (double)evidence.HeadMatched / evidence.HeadExpected : 0.0;
            }

            report.Notes.Add("оборванный ряд: подцепочка разобрана у "
                             + CountHeads(models) + " кандидатов");
        }

        /// <summary>
        /// Из двух подцепочек — над обрывом и под ним — та, что берётся в
        /// расчёт: держащая порог и БОЛЬШАЯ из держащих.
        ///
        /// ⚠ Большая, а не с лучшей долей. Доля тем выше, чем короче кусок, и
        /// отбор по доле выбирал бы огрызок ряда; отбор по величине оставляет
        /// то, о чём в пробе есть что сказать. Ни одна не держит порога — ряд
        /// оборванным не объявляется вовсе.
        /// </summary>
        static bool BestSide(Dictionary<int, int> levelExpected, Dictionary<int, int> levelMatched,
                             int lowest, int deepest, int cut, double coverage,
                             out int lo, out int hi)
        {
            lo = 0;
            hi = -1;
            int bestExpected = 0;
            double bestCoverage = 0.0;
            for (int side = 0; side < 2; side++)
            {
                int from = side == 0 ? lowest : cut + 1;
                int to = side == 0 ? cut - 1 : deepest;
                if (from > to)
                {
                    continue;
                }

                int have = 0, hit = 0;
                for (int depth = from; depth <= to; depth++)
                {
                    int e, m;
                    if (levelExpected.TryGetValue(depth, out e)) have += e;
                    if (levelMatched.TryGetValue(depth, out m)) hit += m;
                }

                // ⛔ ОДНОЙ СЛУЧАЙНОЙ ЛИНИИ НЕ ДОЛЖНО ХВАТАТЬ. У подцепочки с
                // тремя ожидаемыми группами одно попадание даёт 33 % — выше
                // порога 30 %, — то есть порог перестаёт быть порогом ровно
                // там, где знаменатель мал. Требование `have > 1/coverage`
                // никакого нового числа не вводит: оно ВЫВЕДЕНО из самого
                // порога и означает «чтобы пройти, попаданий нужно не меньше
                // двух». Измерено 06.09.2026: без него в корпус приходили
                // `Am-243` одним членом ряда и хвост ряда `U-235` в спектрах
                // Y-88.
                if (have == 0 || have * coverage <= 1.0)
                {
                    continue;
                }

                double own = (double)hit / have;
                if (own < coverage)
                {
                    continue;
                }

                if (have > bestExpected || (have == bestExpected && own > bestCoverage))
                {
                    bestExpected = have;
                    bestCoverage = own;
                    lo = from;
                    hi = to;
                }
            }

            return hi >= lo;
        }

        /// <summary>Члены ряда на этом уровне, через «+»; пусто — уровня нет.</summary>
        static string CutAt(Model model, int depth)
        {
            if (depth == int.MinValue)
            {
                return "";
            }

            var names = new List<string>();
            foreach (string member in model.Members)
            {
                int own;
                if (model.Depths.TryGetValue(member, out own) && own == depth)
                {
                    names.Add(member);
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("+", names.ToArray());
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
        /// ⛔ ТРЕТЬЕ УСЛОВИЕ, И ОНО ПРО ГЛАВНЫЙ ОСТАВШИЙСЯ КЛАСС ФАНТОМОВ.
        /// Доля и якорь судят кандидата ПООДИНОЧКЕ, а фантом живёт не один: он
        /// садится на структуру, которую уже объяснил кто-то другой. Измерено
        /// 18.08.2026 при пороге 30 %: в ториевом спектре `ASN16_Th232` Eu-152
        /// набирает 3 группы из 8 — и все три стоят на линиях самого тория
        /// (121.78 против 129.06 у Ac-228, 344.3 против 338.32, 964.1 против
        /// 968.97), то есть НИ ОДНОЙ своей структуры не приносит. Ровно тот же
        /// механизм, что у фантома Pu-238 из `N18`.
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
        static void Accept(List<Model> models, double coverage, bool novelty)
        {
            var accepted = new List<Model>();
            foreach (Model model in models)
            {
                FsaParentEvidence evidence = model.Evidence;
                if (evidence.Expected == 0)
                {
                    continue;
                }

                bool anchored = !double.IsNaN(evidence.AnchorKev);
                if (!anchored && Judged(evidence) < coverage)
                {
                    evidence.Why = "доля ниже порога";
                    continue;
                }

                if (novelty && !Novel(model, accepted))
                {
                    evidence.Why = "своей структуры не приносит";
                    continue;
                }

                evidence.Accepted = true;
                evidence.Why = anchored
                    ? "неспутываемая линия на месте"
                    : (!double.IsNaN(evidence.HeadCoverage)
                       && evidence.HeadCoverage > evidence.Coverage
                       && evidence.Coverage < coverage)
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
        /// </summary>
        static bool Novel(Model model, List<Model> accepted)
        {
            foreach (Group group in model.Groups)
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

                    foreach (Group rival in other.Groups)
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
                        break;
                    }
                }
            }
        }
    }
}
