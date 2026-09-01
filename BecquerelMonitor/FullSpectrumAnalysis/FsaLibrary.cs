using BecquerelMonitor.EfficiencyMaker;
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
    /// НЕТ (решение Amber 01.09.2026, `S110` пункт 1): у кого в конфиге нет
    /// интенсивностей, тот образа не получает. Единственное исключение —
    /// характеристический рентген ЭЛЕМЕНТА: имя ищется в базе веществ, а образ
    /// строит `FsaSampleLibrary.FluorescenceComponent`, то же место, что у
    /// корпусного пути. Безымянный «X-ray» образа не получает: элемент называет
    /// человек, а не разбор.
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

        // ⛔ СЛОВАРЬ ПОДСТАНОВОК СНЯТ 01.09.2026 (решение Amber, `S110`).
        // Он отображал имена конфига `W` и `Pb` на зашитые образы, а безымянный
        // `X-ray` — на СВИНЕЦ, то есть угадывал элемент за человека. Теперь имя
        // элемента ищется в базе (`MaterialDatabase.ZOf`), а образ строится
        // оттуда же, откуда его берёт корпусный путь; безымянный `X-ray` образа
        // не получает вовсе.

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

                // Линий с интенсивностями в конфиге нет. Единственное, что здесь
                // ещё можно построить, — характеристический рентген ЭЛЕМЕНТА, и
                // строится он ИЗ БАЗЫ, тем же местом, что и у корпусного пути
                // (решение Amber 01.09.2026, `S110` пункты 2–3).
                //
                // ⛔ Догадок больше нет. Прежде безымянный «X-ray» подставлялся
                // СВИНЦОМ — то есть разбор угадывал элемент за человека. Снято:
                // «не может быть безымянных X-ray; если нуклид заносится из базы
                // изотопов, он уже имеет имя» (Amber). Цепочка должна быть такой:
                // в `NucBase` набирается элемент → оттуда приходят его линии
                // X-ray → уезжают в `NuclideDefinition.xml` → финдер их находит
                // → FSA узнаёт о них по ИМЕНИ, а не по подстановке.
                int z = MaterialDatabase.ZOf(nuclide);
                if (z <= 0)
                {
                    continue;
                }

                FsaComponent xray = FsaSampleLibrary.FluorescenceComponent(
                    z, 0.0, double.MaxValue);
                if (xray != null && xray.Lines.Count > 0 && taken.Add(xray.Name))
                {
                    result.Add(xray);
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

        // ⛔ МЕТОД `BuiltinSingles` СНЯТ 01.09.2026 (решения Amber по описи `S110`).
        // Он держал встроенную таблицу восьми нуклидов, зашитые образы `Xray-W` и
        // `Xray-Pb` и неподвижные `SE-2614`/`DE-2614`/`Ann-511`. Все три разряда
        // сняты по отдельности: нуклиды — совсем (пункт 1); рентген элемента
        // строится из `matdb` одним местом на проект (`FsaSampleLibrary
        // .FluorescenceComponent`, пункты 2–3); вылет и аннигиляция считаются от
        // линий состава (`EscapeAndAnnihilation`, пункт 2). Пустой словарь после
        // этого — мёртвый код, и держать его значило бы обещать читателю
        // встроенную библиотеку, которой нет.
    }
}
