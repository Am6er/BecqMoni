using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Сборка библиотеки образов для полноспектральной декомпозиции.
    ///
    /// Состав задаёт поиск пиков: какими нуклидами он подписал спектр, те и
    /// раскладываются. Фиксированный список брать неоткуда, а лишний компонент
    /// в нём немедленно становится фантомом — он ведь свободен и обязательно
    /// что-нибудь себе заберёт.
    ///
    /// Линии компонента берутся из базы нуклидов по имени, веса — из
    /// NuclideDefinition.Intencity. ⛔ Встроенной таблицы нуклидов здесь БОЛЬШЕ
    /// НЕТ (решение Amber 01.09.2026, `S110` пункт 1): у кого в базе нет
    /// интенсивностей, тот образа не получает — кроме характеристического
    /// рентгена, которому оставлена подстановка.
    ///
    /// Пики вылета и аннигиляция добавляются НЕ ВСЕГДА, а по физике: их считает
    /// <see cref="EscapeAndAnnihilation"/> от линий самого состава выше порога
    /// рождения пар. Они не принадлежат нуклиду, финдер их подписать не может, а
    /// без образа NNLS вешает их на ближайшую линию — но и выдумывать их там, где
    /// рождать пары нечему, нельзя.
    /// </summary>
    public static class FsaLibrary
    {
        /// <summary>
        /// Порог рождения пар: ниже 1022 кэВ пары не рождаются, а значит нет ни
        /// пиков вылета, ни аннигиляционного кванта. Физическая величина, не
        /// настройка.
        /// </summary>
        public const double PairThresholdKev = 1022.0;

        /// <summary>
        /// Сколько САМЫХ СИЛЬНЫХ линий выше порога пар получают образы вылета.
        ///
        /// ⚠ Ограничение не физическое, а счётное: каждая линия дала бы ДВЕ
        /// свободные колонки NNLS, и на спектре с двумя десятками жёстких линий
        /// разложение стало бы подгонкой по вылетам. Берём сильнейшие: у слабых
        /// вылет и так тонет в континууме.
        /// </summary>
        public const int EscapeParents = 3;

        /// <summary>Ниже этого выхода линия образа вылета не получает.</summary>
        public const double EscapeMinIntensity = 1.0;

        /// <summary>
        /// Образы вылета и аннигиляции СЧИТАЮТСЯ ОТ ЛИНИЙ СОСТАВА (решение
        /// Amber 01.09.2026 по описи `S110`, пункт 2).
        ///
        /// Прежде здесь стоял неподвижный список `AlwaysPresent` = `SE-2614`,
        /// `DE-2614`, `Ann-511`, и он дописывался к ЛЮБОМУ непустому составу:
        /// пики вылета от линии 2614.5 кэВ (Tl-208) появлялись у спектра, в
        /// котором тория нет вовсе. Это привязка к имени, а не к физике.
        ///
        /// Теперь правило одно и оно физическое: пары рождаются от
        /// <see cref="PairThresholdKev"/>, значит образы ставятся от КАЖДОЙ
        /// линии состава выше этого порога — одиночный вылет на E−511, двойной
        /// на E−1022, — а аннигиляционный образ появляется только тогда, когда
        /// такая линия в составе есть. Для ториевой пробы это ровно прежние
        /// `SE-2614` / `DE-2614`: имена сохраняются, потому что строятся из той
        /// же энергии.
        ///
        /// `Ann-511` образом ВЫЛЕТА не является и не станет: аннигиляционный
        /// квант рождается в защите и обвязке и ВЛЕТАЕТ в кристалл, а матрица
        /// отклика описывает судьбу кванта, уже попавшего внутрь. На этой
        /// разнице держится различение подмены (§13е), поэтому гейт `S47`
        /// (<see cref="FsaAnalyzer.EscapeGate"/>) её не касается.
        /// </summary>
        public static List<FsaComponent> EscapeAndAnnihilation(List<FsaComponent> composition)
        {
            var extra = new List<FsaComponent>();
            if (composition == null || composition.Count == 0)
            {
                return extra;
            }

            // Родители вылета: линии состава выше порога пар, сильнейшие первыми.
            var parents = new List<FsaLine>();
            foreach (FsaComponent component in composition)
            {
                if (component.Kind == FsaComponentKind.Nuisance)
                {
                    // Мешающие образы сами вылета не порождают: у них нет
                    // распада, а их линии — уже следствие чужого кванта.
                    continue;
                }

                foreach (FsaLine line in component.Lines)
                {
                    if (line.Energy > PairThresholdKev && line.Intensity >= EscapeMinIntensity)
                    {
                        parents.Add(line);
                    }
                }
            }

            if (parents.Count == 0)
            {
                return extra;
            }

            parents.Sort((a, b) => b.Intensity.CompareTo(a.Intensity));
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int used = 0;
            foreach (FsaLine parent in parents)
            {
                if (used >= EscapeParents)
                {
                    break;
                }

                // Имя строится из энергии родителя — по нему видно, ОТ ЧЕГО
                // вылет, и второго списка имён в проекте не заводится.
                string tag = Math.Round(parent.Energy).ToString(CultureInfo.InvariantCulture);
                if (!taken.Add(tag))
                {
                    continue;
                }

                used++;
                extra.Add(OneLine("SE-" + tag, parent.Energy - 511.0));
                extra.Add(OneLine("DE-" + tag, parent.Energy - 1022.0));
            }

            // Аннигиляция — только когда есть чему рождать пары.
            extra.Add(OneLine(FsaResult.AnnihilationComponentName, 511.0));
            return extra;
        }

        static FsaComponent OneLine(string name, double energy)
        {
            var component = new FsaComponent(name, FsaComponentKind.Nuisance);
            component.Lines.Add(new FsaLine(name, energy, 100.0));
            return component;
        }

        /// <summary>
        /// Образ ВЫЛЕТА из кристалла — тот, что матрица отклика содержит сама
        /// (S47, гейт живёт в <see cref="FsaAnalyzer.EscapeGate"/>).
        ///
        /// Опознаётся по приставке: имена строятся из энергии родителя
        /// (<see cref="EscapeAndAnnihilation"/>), и держать рядом второй список
        /// имён значило бы завести две копии одного правила.
        /// </summary>
        public static bool IsEscapeImage(string name)
        {
            return !string.IsNullOrEmpty(name)
                   && (name.StartsWith("SE-", StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith("DE-", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Нуклиды, у которых в базе есть линии, но нет интенсивностей
        /// (характеристический рентген): подписать пик база позволяет, а
        /// построить по ней образ — нет, поэтому берётся встроенный.
        ///
        /// Запасной путь, а не основной: линии рентгена элемента ввозятся в
        /// набор из NucBase вместе с долями внутри K-серии, и образ тогда
        /// строится по ним, как у любого другого компонента. Подстановка
        /// остаётся для наборов, заведённых до этого — там выходы пустые.
        ///
        /// Ключ — ТОКЕН имени, а не имя целиком: искать сюда приходит
        /// <see cref="NuclideToken"/>, то есть всё до первого пробела
        /// (<see cref="NuclideDefinition.NuclideNameOf"/>). Поэтому «W x-ray» из
        /// конфигурации ищется как «W», а «X-ray» — как «X-ray», пробела в нём
        /// нет. Строка S29 утверждала обратное («ни одно имя не совпадает, путь
        /// мёртв») — проверено 08.08.2026 разбором токенизации: совпадают оба.
        ///
        /// Дописан «Pb» — свинец был единственным несимметричным местом: у
        /// вольфрама запасной образ был, у свинца нет, хотя пишут их одинаково
        /// («Pb x-ray» -&gt; «Pb»). В поставочном конфиге у свинца интенсивности
        /// проставлены и до подстановки дело не доходит, но конфигурация без
        /// них — ровно тот случай, ради которого эта таблица и заведена.
        ///
        /// Родовые имена поставки — «x-rays», «Am-241/x-rays», «Low
        /// Bremsstrahlung x-rays» — сюда НЕ ЗАВОДЯТСЯ нарочно: они стоят на
        /// 15…55 кэВ и на K-серию свинца (75/72.8/84.9/87.3) или вольфрама
        /// (59.3/58.0/67.2/69.1) не похожи ничем. Подставить им свинцовый образ
        /// значило бы завести фантом там, где сегодня компонента просто нет.
        /// </summary>
        static readonly Dictionary<string, string> BuiltinSubstitutes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "W", "Xray-W" },
                { "Pb", "Xray-Pb" },
                { "X-ray", "Xray-Pb" }
            };

        /// <summary>
        /// Библиотека по найденным пикам: distinct по нуклидам, которыми поиск
        /// пиков подписал спектр. Это и есть состав, который надо разложить —
        /// брать фиксированный список неоткуда, а лишние компоненты в нём
        /// становятся фантомами. Побочно получается разрез цепочки: финдер
        /// подписывает пики дочерними (Ac-228, Pb-212, Tl-208), и каждый
        /// дочерний входит в модель своим образом со свободной амплитудой —
        /// жёсткая связка интенсивностей внутри цепочки не навязывается.
        /// </summary>
        public static List<FsaComponent> BuildFromPeaks(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions)
        {
            List<FsaComponent> result = new List<FsaComponent>();
            if (peaks == null || nuclideDefinitions == null)
            {
                return result;
            }

            // `Visible` НЕ проверяется: видимость — свойство ГРАФИКА, а не
            // модели. Линия, снятая с показа, из спектра не исчезла, и образ
            // без неё занижен. На этом стояла целая заготовка: поставочный
            // конфиг добирает недостающие линии нуклида скрытыми записями
            // (`tools/nucdb/fill_intensity.py`, второй проход) именно затем,
            // чтобы образ был полон, а график чист, — и весь добор сюда не
            // доезжал: у Bi-214 в файле 21 линия, в образе оказывалось 7
            // (найдено 08.08.2026 при сборке S28, решение Amber).
            //
            // Лишних компонентов от этого не появится: компонент попадает в
            // библиотеку, только если ПОИСК ПИКОВ уже подписал им пик, а поиск
            // (`PeakDetector`) скрытые линии как раз пропускает. Скрытая
            // запись способна дополнить образ уже опознанного нуклида и не
            // способна привести в разложение новый.
            List<NuclideDefinition> definitions = nuclideDefinitions
                .Where(n => n != null && n.Energy > 0.0)
                .ToList();

            // Порядок сохраняем по первому появлению: так состав читается в
            // том же порядке, в каком пики идут по спектру.
            List<string> order = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Peak peak in peaks)
            {
                if (peak == null || peak.Nuclide == null)
                {
                    // «(unknown)»: подписать нечем, образа тоже нет
                    continue;
                }

                string nuclide = NuclideToken(peak.Nuclide.Name);
                if (nuclide.Length > 0 && seen.Add(nuclide))
                {
                    order.Add(nuclide);
                }
            }

            Dictionary<string, FsaComponent> builtin = BuiltinSingles();
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string nuclide in order)
            {
                // Характеристический рентген элемента — мешающий образ, а не
                // нуклид: его линии в спектре есть, а активности за ними нет.
                // Амплитуда у него своя и свободная, в «пирог» долей он не
                // входит — как пики вылета. Признак — отсутствие массового
                // числа в подписи, см. NuclideDefinition.IsElementXrayName.
                FsaComponentKind kind = NuclideDefinition.IsElementXrayName(nuclide)
                    ? FsaComponentKind.Nuisance
                    : FsaComponentKind.Single;
                FsaComponent component = new FsaComponent(nuclide, kind);
                foreach (NuclideDefinition definition in definitions)
                {
                    if (definition.Intencity <= 0.0
                        || !string.Equals(NuclideToken(definition.Name), nuclide, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Одна линия может быть записана в базе дважды: своя запись
                    // и запись «в цепочке», либо ручная копия с округлённой
                    // энергией. Обе в образе удваивают вес линии — амплитуда
                    // компонента падает вдвое, доли между нуклидами едут.
                    // Порог 0.05 кэВ: реальных раздельных линий ближе не
                    // бывает, а дубль с иной точностью записи — та же линия.
                    bool duplicate = false;
                    foreach (FsaLine line in component.Lines)
                    {
                        if (Math.Abs(line.Energy - definition.Energy) < 0.05)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (duplicate)
                    {
                        continue;
                    }

                    component.Lines.Add(new FsaLine(nuclide, definition.Energy, definition.Intencity));
                }

                if (component.Lines.Count > 0)
                {
                    if (taken.Add(component.Name))
                    {
                        result.Add(component);
                    }

                    continue;
                }

                // Линий с интенсивностями в базе нет — берём встроенный образ:
                // сначала по имени самого нуклида (в поставочной базе выходы не
                // заполнены вовсе), затем по подстановке для рентгена.
                FsaComponent replacement;
                string substitute;
                if (!builtin.TryGetValue(nuclide, out replacement)
                    && BuiltinSubstitutes.TryGetValue(nuclide, out substitute))
                {
                    builtin.TryGetValue(substitute, out replacement);
                }

                if (replacement != null && taken.Add(replacement.Name))
                {
                    result.Add(replacement);
                }
            }

            // Мешающие образы добавляются только К ЧЕМУ-ТО: спектр без единого
            // подписанного пика должен получить «нет компонентов», а не тихое
            // разложение из одних пиков вылета и континуума. В харнессе так же:
            // служебные образы дописываются к непустому составу оператора.
            if (result.Count > 0)
            {
                // Вылет и аннигиляция — ОТ ЛИНИЙ СОСТАВА, а не из неподвижного
                // списка (решение Amber 01.09.2026, `S110` пункт 2).
                foreach (FsaComponent component in EscapeAndAnnihilation(result))
                {
                    if (taken.Add(component.Name))
                    {
                        result.Add(component);
                    }
                }
            }

            return result;
        }

        /// <summary>Имя нуклида без хвоста вида «(Th-232)».</summary>
        static string NuclideToken(string name)
        {
            return NuclideDefinition.NuclideNameOf(name);
        }

        static Dictionary<string, FsaComponent> BuiltinSingles()
        {
            Dictionary<string, FsaComponent> table = new Dictionary<string, FsaComponent>(StringComparer.OrdinalIgnoreCase);
            Action<string, FsaComponentKind, double[,]> add = (name, kind, lines) =>
            {
                FsaComponent component = new FsaComponent(name, kind);
                for (int i = 0; i < lines.GetLength(0); i++)
                {
                    component.Lines.Add(new FsaLine(name, lines[i, 0], lines[i, 1]));
                }

                table[name] = component;
            };

            // ⛔ ЗДЕСЬ СТОЯЛА ВСТРОЕННАЯ ТАБЛИЦА ВОСЬМИ НУКЛИДОВ — `K-40`, `Cs-137`,
            // `Am-241`, `Co-60`, `I-131`, `Eu-152`, `Ba-133`, `Lu-176` с энергиями и
            // выходами прямо в коде. СНЯТА 01.09.2026 решением Amber по описи `S110`
            // (пункт 1, дословно «снять совсем»).
            //
            // Почему она была: поставочный `NuclideDefinition.xml` держит имя и одну
            // энергию без интенсивности, и подписанному пику неоткуда было взять образ.
            // Почему снята: имя нуклида в коде — правило, выведенное из одного случая
            // и молча применённое ко всем; линии нуклида живут в `nucdb`, а не здесь.
            //
            // ⚠ Следствие, названное честно: нуклид, у которого в конфиге нет
            // интенсивностей, ОБРАЗА БОЛЬШЕ НЕ ПОЛУЧАЕТ и в состав не входит — на
            // экране такой компонент исчезнет. Корпуса это не касается: он идёт
            // `--lib=sample`, где линии берутся из `nucdb` по объявленной пробе.

            // Характеристический рентген — не нуклиды, а мешающие образы:
            // флуоресценция вольфрама (ториевые WT-электроды) и свинца (домик).
            // Без них NNLS вешает пик 58-59 кэВ на Am-241 (59.5 кэВ).
            add("Xray-W", FsaComponentKind.Nuisance, new double[,] {
                { 59.318, 100.0 }, { 57.981, 57.6 }, { 67.244, 22.0 }, { 69.067, 8.0 } });
            add("Xray-Pb", FsaComponentKind.Nuisance, new double[,] {
                { 74.969, 100.0 }, { 72.804, 59.5 }, { 84.936, 23.0 }, { 87.300, 8.0 } });

            // ⛔ Неподвижных образов вылета и аннигиляции здесь БОЛЬШЕ НЕТ:
            // `SE-2614`, `DE-2614` и `Ann-511` строились от зашитой линии 2614.5
            // (Tl-208) и дописывались любому составу. Снято 01.09.2026 решением
            // Amber (`S110`, пункт 2) — теперь их считает `EscapeAndAnnihilation`
            // от ЛИНИЙ САМОГО СОСТАВА, по порогу рождения пар.

            return table;
        }
    }
}
