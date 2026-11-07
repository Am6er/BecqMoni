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
    /// ⛔ (`AMBER306`, решение Amber 05.11.2026 вопросником, дословно: «Линии и
    /// выходы из nucdb (Рекомендую)») МЕТКИ ВЫБИРАЮТ НУКЛИДЫ, А ИЗЛУЧЕНИЯ И ВЫХОДЫ —
    /// ИЗ БАЗЫ, тем же сборщиком <see cref="FsaSampleLibrary.Build"/>, что у «Из
    /// NucBase»: <see cref="BuildFromLabels"/>. Библиотека конфигурации образа не
    /// наполняет. ⛔ Встроенной таблицы нуклидов здесь БОЛЬШЕ НЕТ (решение Amber
    /// 01.09.2026, `S110` пункт 1). Характеристический рентген ЭЛЕМЕНТА — из
    /// `matdb`, одним местом (`FsaSampleLibrary.FluorescenceComponent`). Безымянный
    /// «X-ray» элемента не получает: элемент называет человек, а не разбор.
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
        /// ⛔ (`A271`) Насколько выше порога пар обязан стоять родитель образа
        /// вылета — ВСЕГДА, знаем мы вещество кристалла или нет.
        ///
        /// Заведён потому, что отсев по доле рождения пар
        /// (<see cref="EscapeMinPairShare"/>) вещества требует, а у спектра без
        /// геометрии его нет, и при неизвестном веществе отсев отключался
        /// ЦЕЛИКОМ: родителем становилась любая линия выше 1022 кэВ по сырому
        /// выходу. Наверх выходила `Bi-214` 1120 — на 98 кэВ выше порога, где
        /// пар практически не рождается, — а её образ `SE-1120` стоит на 609.0,
        /// то есть ПОВЕРХ линии `Bi-214` 609.3, и забирал до 76 % отсчётов
        /// спектра (`AS80_Charoite`, измерено 06.09.2026).
        ///
        /// ⚠ Величина ЗАМЕРЕНА, а не выбрана круглой: это наименьшая по всем
        /// кристаллам библиотеки энергия, на которой доля рождения пар
        /// пересекает <see cref="EscapeMinPairShare"/>, минус порог пар. Ниже
        /// неё родителя отвергает ЛЮБОЕ известное вещество, поэтому запас не
        /// спорит с отсевом по доле, а продолжает его туда, где вещества нет.
        ///
        /// Замер `EscapeMarginProbe` 06.09.2026, десять кристаллов библиотеки —
        /// пересечение от 1146.3 кэВ (BGO, самый тяжёлый) до 1204.8 (германий);
        /// у CsI 1156.7, у NaI 1166.3. Наименьшее и взято: 1146.3 − 1022 =
        /// 124.3, округлено ВНИЗ до 124.0, чтобы запас гарантированно не был
        /// строже отсева по доле ни у одного вещества. Проба печатает замер
        /// заново и ОТКАЖЕТ, если правка библиотеки веществ сдвинет наименьшее
        /// пересечение ниже запаса.
        ///
        /// ⚠ Что запас НЕ трогает: `Ann-511` (<see cref="PairsPossible"/>).
        /// Аннигиляционный квант рождается в защите и обвязке, а не в
        /// кристалле, и вещество кристалла о нём ничего не говорит — там
        /// действует голый порог 1022 кэВ, как и прежде.
        /// </summary>
        public const double EscapeParentMarginKev = 124.0;

        /// <summary>
        /// Сколько САМЫХ СИЛЬНЫХ линий выше порога пар получают образы вылета.
        ///
        /// ⚠ Ограничение не физическое, а счётное: каждая линия дала бы ДВЕ
        /// свободные колонки NNLS, и на спектре с двумя десятками жёстких линий
        /// разложение стало бы подгонкой по вылетам.
        ///
        /// ⛔ (`S122`) СИЛЬНЕЙШИЕ — не по выходу линии, а по ОЖИДАЕМОЙ ПЛОЩАДИ
        /// вылета: вес родителя равен I·p_пар(E), где p_пар — доля рождения
        /// пар в полном ослаблении кристалла
        /// (<see cref="EscapeMinPairShare"/>). Прежний отбор по одному выходу
        /// ставил порядок неверно: линия 1461 кэВ K-40 с выходом 10.7 % даёт
        /// вылета больше, чем 1173 кэВ Co-60 со 100 %, потому что пар на ней
        /// рождается в двенадцать раз больше.
        /// </summary>
        public const int EscapeParents = 3;

        /// <summary>Ниже этого выхода линия образа вылета не получает.</summary>
        public const double EscapeMinIntensity = 1.0;

        /// <summary>
        /// ⛔ (`S122`) Ниже этой доли рождения пар в полном ослаблении
        /// кристалла линия образов вылета не получает — сколько бы её ни
        /// испускали.
        ///
        /// Порога 1022 кэВ для этого мало: сразу над ним пары рождаться
        /// МОГУТ, но практически не рождаются, и свободный столбец там ловит
        /// один шум. Измерено по `matdb` для CsI (доля пар в полном
        /// ослаблении): 1050 кэВ — 0.0012 %, 1100 — 0.023 %, 1173 — 0.135 %,
        /// 1332 — 0.78 %, 1461 — 1.63 %, 2614 — 15.3 %. У NaI ход тот же и
        /// числа на пятую часть ниже. Порог 0.1 % проводит границу между
        /// 1.10 МэВ (образа нет) и линиями Co-60 (есть).
        ///
        /// ⚠ Величина проверяемая, а не подогнанная: она СЧИТАЕТСЯ по
        /// сечениям вещества кристалла.
        ///
        /// ⛔ Вещества нет — правило НЕ ОТКЛЮЧАЕТСЯ (`A271`, 06.09.2026). Оно
        /// заменяется своим следствием, посчитанным заранее по всем известным
        /// кристаллам: запасом по энергии
        /// (<see cref="EscapeParentMarginKev"/>). До этой правки при `NaN`
        /// отсева не оставалось вовсе, и это стоило 7 пунктов recall на
        /// непонятной части корпуса. Само вещество при этом ищется дальше, чем
        /// геометрия: у спектра без неё элементы кристалла названы, и
        /// <see cref="FsaSampleLibrary.CrystalFractionsOf"/> восстанавливает по
        /// ним вещество библиотеки — но ОДНОЗНАЧНЫМ совпадением состава, а не
        /// догадкой.
        /// </summary>
        public const double EscapeMinPairShare = 0.001;

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
            return EscapeAndAnnihilation(composition, null);
        }

        /// <summary>
        /// То же, но с ВЕЩЕСТВОМ КРИСТАЛЛА (`S122`): массовые доли элементов,
        /// снятые с геометрии прибора. С ними родители отбираются и
        /// упорядочиваются по ожидаемой площади вылета I·p_пар(E), без них —
        /// прежним правилом по одному выходу линии.
        ///
        /// ⚠ Доли передаются СНИМКОМ, а не ссылкой в живую конфигурацию:
        /// сборка библиотеки идёт в фоновой задаче (`S119`).
        /// </summary>
        public static List<FsaComponent> EscapeAndAnnihilation(
            List<FsaComponent> composition, IDictionary<int, double> crystalFractions)
        {
            List<FsaComponent> extra = EscapeImages(composition, crystalFractions);

            // Аннигиляция — только когда есть чему рождать пары.
            if (PairsPossible(composition))
            {
                extra.Add(OneLine(FsaResult.AnnihilationComponentName, 511.0));
            }

            return extra;
        }

        /// <summary>
        /// ТОЛЬКО образы вылета — одиночного и двойного, без `Ann-511`.
        ///
        /// ⛔ Вынесено 06.09.2026 (`S141`), и вынесено потому, что правило
        /// понадобилось ВТОРОМУ собирателю: библиотека из баз
        /// (<see cref="FsaSampleLibrary.Build"/>, ею идёт весь корпус)
        /// строила `Ann-511` своим местом, а SE/DE не строила вовсе — то есть
        /// на спектре без матрицы образов вылета в модели не было. Копия
        /// правила там завела бы вторую редакцию отбора родителей; поэтому
        /// половины разведены, а не размножены.
        ///
        /// `Ann-511` сюда не входит по существу, а не для удобства: он не
        /// образ ВЫЛЕТА (квант 511 влетает в кристалл снаружи), у него своё
        /// условие и свой ключ в анализаторе, и собиратель из баз ставит его
        /// сам.
        /// </summary>
        public static List<FsaComponent> EscapeImages(
            List<FsaComponent> composition, IDictionary<int, double> crystalFractions)
        {
            var extra = new List<FsaComponent>();
            if (composition == null || composition.Count == 0)
            {
                return extra;
            }

            // Родители вылета: линии состава выше порога пар, впереди те, у
            // кого ожидаемая площадь вылета больше.
            var parents = new List<EscapeParent>();
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
                    // ⛔ (`A271`) Запас над порогом пар спрашивается ПЕРВЫМ и
                    // спрашивается всегда: он и есть тот отсев, который
                    // остаётся, когда вещество кристалла неизвестно. Прежде
                    // здесь стоял голый порог 1022 кэВ, а единственный
                    // физический ограничитель — доля рождения пар — при
                    // неизвестном веществе молчал целиком.
                    if (!(line.Energy >= PairThresholdKev + EscapeParentMarginKev)
                        || !(line.Intensity >= EscapeMinIntensity))
                    {
                        continue;
                    }

                    double share = PairShare(crystalFractions, line.Energy);
                    if (!double.IsNaN(share) && share < EscapeMinPairShare)
                    {
                        continue;         // пары рождаться могут, но не рождаются
                    }

                    parents.Add(new EscapeParent(line,
                        double.IsNaN(share) ? line.Intensity : line.Intensity * share,
                        component.Name));
                }
            }

            // Порядок ДЕТЕРМИНИРОВАН: при равных весах решает энергия. У
            // List.Sort порядок равных элементов не определён, а состав
            // библиотеки обязан быть один и тот же от прогона к прогону.
            parents.Sort((a, b) =>
            {
                int byWeight = b.Weight.CompareTo(a.Weight);
                return byWeight != 0 ? byWeight : b.Line.Energy.CompareTo(a.Line.Energy);
            });

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int used = 0;
            foreach (EscapeParent parent in parents)
            {
                if (used >= EscapeParents)
                {
                    break;
                }

                // Имя строится из энергии родителя — по нему видно, ОТ ЧЕГО
                // вылет, и второго списка имён в проекте не заводится.
                string tag = Math.Round(parent.Line.Energy)
                    .ToString(CultureInfo.InvariantCulture);
                if (!taken.Add(tag))
                {
                    continue;
                }

                used++;
                extra.Add(OneLine("SE-" + tag, parent.Line.Energy - 511.0, parent.Owner));
                extra.Add(OneLine("DE-" + tag, parent.Line.Energy - 1022.0, parent.Owner));
            }

            return extra;
        }

        /// <summary>
        /// Есть ли в составе чему рождать пары: линия выше
        /// <see cref="PairThresholdKev"/> с выходом не ниже
        /// <see cref="EscapeMinIntensity"/>.
        ///
        /// ⚠ Отсев по ДОЛЕ рождения пар в веществе кристалла
        /// (<see cref="EscapeMinPairShare"/>) сюда не входит намеренно:
        /// аннигиляционный квант рождается в защите и обвязке, а не в
        /// кристалле, и вещество кристалла о нём ничего не говорит.
        ///
        /// ⛔ Открыто ради ВТОРОГО собирателя (`S141`): у
        /// <see cref="FsaSampleLibrary"/> стояла своя копия этого же условия,
        /// и двум копиям одного физического порога расходиться незачем.
        /// </summary>
        public static bool PairsPossible(List<FsaComponent> composition)
        {
            if (composition == null)
            {
                return false;
            }

            foreach (FsaComponent component in composition)
            {
                if (component.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                foreach (FsaLine line in component.Lines)
                {
                    if (line.Energy > PairThresholdKev
                        && line.Intensity >= EscapeMinIntensity)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Линия-родитель вылета и её ожидаемый вес.</summary>
        sealed class EscapeParent
        {
            public readonly FsaLine Line;
            public readonly double Weight;

            /// <summary>
            /// (`AMBER8`) Имя КОЛОНКИ, чья это линия. Нужно, чтобы образ вылета
            /// помнил родителя: вылета без родителя не бывает, и когда колонку
            /// снимает отсев, её вылет обязан уйти вместе с ней.
            /// </summary>
            public readonly string Owner;

            public EscapeParent(FsaLine line, double weight, string owner)
            {
                this.Line = line;
                this.Weight = weight;
                this.Owner = owner;
            }
        }

        /// <summary>
        /// Доля рождения пар в полном ослаблении вещества кристалла на энергии
        /// <paramref name="energyKev"/> (`S122`). NaN — вещество неизвестно
        /// или у элемента нет сечений: тогда правило не применяется, а не
        /// подменяется догадкой.
        ///
        /// ⛔ Сечение пар берётся ПОРОГОВОЙ интерполяцией (`S121`) без
        /// оглядки на ключ матрицы: у матрицы контракт «выключенный ключ —
        /// побитово прежний файл», а здесь величина считается заново на
        /// каждом разборе, и считать её заведомо неверной схемой незачем.
        /// Плотность в отношение не входит и не спрашивается.
        /// </summary>
        static double PairShare(IDictionary<int, double> crystalFractions, double energyKev)
        {
            if (crystalFractions == null || crystalFractions.Count == 0
                || !(energyKev > 0.0))
            {
                return double.NaN;
            }

            double logEnergyKev = Math.Log(energyKev);
            double pair = 0.0;
            double total = 0.0;
            foreach (KeyValuePair<int, double> f in crystalFractions)
            {
                if (!(f.Value > 0.0))
                {
                    continue;
                }

                MaterialDatabase.Element element;
                int lo, hi;
                if (!MaterialDatabase.TryGet(f.Key, out element)
                    || !MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
                {
                    return double.NaN;    // элемент без сечений — доли не знаем
                }

                pair += f.Value * PartialCrossSections.MassCrossSection(
                    element, lo, hi, energyKev, logEnergyKev,
                    PhotonProcess.PairProduction, true);
                // ⛔ (`AMBER80`, П149 24.09.2026) Полное ослабление — СУММОЙ
                // КАНАЛОВ (<see cref="PartialCrossSections.MassTotal"/>, ~~`AMBER74`~~),
                // тем же правилом, что у всех слоёв сцены. Здесь стояла прямая по
                // `Element.Total` — сумме каналов в узлах, — и доля пар выходила
                // занижена до 1.7 % на 2614.5 кэВ (CsI; NaI 1.5 %, LaBr₃ 1.4 %;
                // замер `FsaTotalReadersProbe`). Строка `AMBER74` этого читателя
                // называла, а до него правка не доехала.
                total += f.Value * PartialCrossSections.MassTotal(
                    element, lo, hi, energyKev, logEnergyKev);
            }

            return total > 0.0 ? pair / total : double.NaN;
        }

        static FsaComponent OneLine(string name, double energy, string escapeParent = null)
        {
            var component = new FsaComponent(name, FsaComponentKind.Nuisance);
            component.Lines.Add(new FsaLine(name, energy, 100.0));
            component.EscapeParent = escapeParent;
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
        // оттуда же, откуда его берёт корпусный путь; безымянный `X-ray` элемента
        // не получает вовсе.

        /// <summary>
        /// Библиотека по найденным пикам без обстановки спектра: для проб, которым
        /// спектр не нужен. Приложение идёт <see cref="BuildFromLabels"/>.
        /// </summary>
        public static List<FsaComponent> BuildFromPeaks(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions)
        {
            return BuildFromPeaks(peaks, nuclideDefinitions, null);
        }

        /// <summary>
        /// То же, с массовыми долями вещества КРИСТАЛЛА (`S122`): по ним образы
        /// вылета считают долю рождения пар и отбирают родителей.
        /// </summary>
        public static List<FsaComponent> BuildFromPeaks(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions,
            IDictionary<int, double> crystalFractions)
        {
            return BuildFromPeaks(peaks, nuclideDefinitions, crystalFractions, true);
        }

        /// <summary>
        /// То же, с пользовательским флажком «атомный рентген» (`A145`,
        /// <see cref="FsaCalculationOptions.AtomicXray"/>).
        /// </summary>
        public static List<FsaComponent> BuildFromPeaks(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions,
            IDictionary<int, double> crystalFractions,
            bool atomicXray)
        {
            var spec = new FsaSampleSpec { AtomicXray = atomicXray };
            if (crystalFractions != null)
            {
                foreach (KeyValuePair<int, double> fraction in crystalFractions)
                {
                    spec.CrystalFractions[fraction.Key] = fraction.Value;
                }
            }

            LabelReport report;
            return BuildFromLabels(peaks, nuclideDefinitions, spec, out report);
        }

        /// <summary>
        /// (`AMBER306`) Что сборка по меткам взяла откуда. Читают трассировка и проба.
        /// </summary>
        public sealed class LabelReport
        {
            /// <summary>Нуклиды из базы, `nucid`: образ из `nucdb`.</summary>
            public readonly List<string> FromDatabase = new List<string>();

            /// <summary>Подписи элементов, Z: образ из `matdb`.</summary>
            public readonly List<int> Elements = new List<int>();

            /// <summary>Подписи, которых в базах нет: образ из линий библиотеки как вбиты.</summary>
            public readonly List<string> Typed = new List<string>();

            /// <summary>
            /// Линии библиотеки у нуклида из базы, которых в базе нет: в образ не идут
            /// («Имя энергия»). В окне библиотеки у них красная метка (`AMBER281`).
            /// </summary>
            public readonly List<string> IgnoredTypedLines = new List<string>();

            /// <summary>Отчёт сборки образов; null — состав пуст, сборка не звалась.</summary>
            public FsaSampleLibrary.Report Library;

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "по меткам: из базы {0} [{1}], элементов {2}, вбитых руками {3} [{4}], линий руками мимо образа {5} [{6}]; {7}",
                    this.FromDatabase.Count, string.Join(", ", this.FromDatabase), this.Elements.Count,
                    this.Typed.Count, string.Join(", ", this.Typed), this.IgnoredTypedLines.Count,
                    string.Join(", ", this.IgnoredTypedLines), this.Library);
            }
        }

        /// <summary>
        /// ⛔ (`AMBER306`, 06.11.2026) СОСТАВ ПО МЕТКАМ ПИКОВ — ТЕМ ЖЕ СБОРЩИКОМ, ЧТО «ИЗ NUCBASE».
        ///
        /// Метка выбирает нуклид: distinct по нуклидам, которыми поиск пиков подписал
        /// спектр. Брать фиксированный список неоткуда, а лишние компоненты в нём
        /// становятся фантомами. Излучения и выходы — первый постулат: все излучения
        /// распада из `nucdb` (<see cref="FsaSampleLibrary.Build"/>). Отличие от «Из
        /// NucBase» одно: родителя здесь не выводят, каждый подписанный нуклид — своя
        /// колонка со свободной амплитудой (разрез цепочки: финдер подписывает
        /// дочерние, и жёсткая связка интенсивностей не навязывается), и выход у него
        /// на СОБСТВЕННЫЙ распад, а не на распад корня: метка ряда в подписи только
        /// называет происхождение для окна результата.
        ///
        /// Измеренная цена прежнего пути (линии и выходы библиотеки конфигурации,
        /// проверка `AMBER296`, вне корпуса): у Cs-137 нет K-рентгена бария, χ²/ndf
        /// Пуассона на точке 69 против 7; у K-40 выход библиотеки вместо базы, один
        /// спектр 14767 или 15224 Бк только от положения переключателя.
        ///
        /// ⛔ ЛИНИЯ, ВБИТАЯ РУКАМИ (логика `AMBER281`, <see cref="NuclideDbLink"/>).
        /// <list type="bullet">
        /// <item>Нуклид есть в базе, линии нет — в образ НЕ идёт: излучения этого
        /// нуклида знает база, а метка в окне библиотеки у линии уже стоит. Пик она
        /// подписать может, активность по ней не считается; названа в
        /// <see cref="LabelReport.IgnoredTypedLines"/>.</item>
        /// <item>Нуклида нет в базе вовсе (или у него нет линий) — единственный путь в
        /// модель: образ из его линий как вбиты, вид Single, своя строка под его именем.
        /// Человек завёл такую линию и подписал ею пик; молча снять её значило бы
        /// потерять его нуклид.</item>
        /// <item>Подпись элемента, известного `matdb`, — рентген из базы защитным
        /// элементом, как у «Из NucBase» (<c>FsaCompositionInference.Candidates</c>);
        /// неизвестного — мешающий образ из строки библиотеки.</item>
        /// </list>
        ///
        /// `Visible` не проверяется, как и прежде: видимость — свойство графика.
        /// Мешающие образы добавляются только К ЧЕМУ-ТО: спектр без единой подписи
        /// получает «нет компонентов», а не разложение из одних служебных образов.
        ///
        /// <paramref name="spec"/> — обстановка спектра (`FsaSampleSpec.OfSpectrum`,
        /// `Declared`): полоса, кристалл, флажок рентгена. Состав дополняется в ней.
        /// </summary>
        public static List<FsaComponent> BuildFromLabels(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions,
            FsaSampleSpec spec,
            out LabelReport report)
        {
            report = new LabelReport();
            if (peaks == null || nuclideDefinitions == null || spec == null)
            {
                return new List<FsaComponent>();
            }

            List<NuclideDefinition> definitions = nuclideDefinitions
                .Where(n => n != null && PositiveFinite(n.Energy))
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

                // ⛔ ПОДПИСЬ «ВЫЛЕТ» НУКЛИДОМ НЕ ЯВЛЯЕТСЯ (`S141`, 06.09.2026): цифры в
                // имени есть, и без этой строки «SE-2615» стала бы обычной колонкой.
                // Образы вылета строятся ниже от линий состава, а не по подписи.
                if (IsEscapeImage(nuclide))
                {
                    continue;
                }

                if (nuclide.Length > 0 && seen.Add(nuclide))
                {
                    order.Add(nuclide);
                }
            }

            foreach (string nuclide in order)
            {
                if (NuclideDefinition.IsElementXrayName(nuclide))
                {
                    // Рентген элемента — мешающий образ, а не нуклид: тот же вход, что
                    // у вывода состава (`FsaCompositionInference.Candidates`).
                    int z = MaterialDatabase.ZOf(nuclide);
                    if (z > 0)
                    {
                        if (!spec.ShieldElements.Contains(z))
                        {
                            spec.ShieldElements.Add(z);
                        }

                        report.Elements.Add(z);
                        continue;
                    }
                }
                else
                {
                    string nucid = FsaSampleLibrary.NucidOf(nuclide);
                    List<double[]> lines = nucid.Length > 0
                        ? FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report())
                        : null;
                    if (lines != null && lines.Count > 0)
                    {
                        if (!spec.Nuclides.Contains(nucid))
                        {
                            spec.Nuclides.Add(nucid);
                            report.FromDatabase.Add(nucid);
                        }

                        NoteTypedLines(nuclide, lines, definitions, report);
                        continue;
                    }
                }

                FsaComponent typed = TypedComponent(nuclide, definitions, spec);
                if (typed != null)
                {
                    spec.TypedComponents.Add(typed);
                    report.Typed.Add(typed.Name);
                }
            }

            if (spec.Nuclides.Count == 0 && spec.TypedComponents.Count == 0 && report.Elements.Count == 0)
            {
                return new List<FsaComponent>();
            }

            FsaSampleLibrary.Report built;
            List<FsaComponent> library = FsaSampleLibrary.Build(spec, out built);
            report.Library = built;
            return library;
        }

        /// <summary>(`AMBER306`) Линии библиотеки у нуклида из базы, которых в базе нет: назвать.</summary>
        static void NoteTypedLines(string nuclide, List<double[]> lines, List<NuclideDefinition> definitions,
                                   LabelReport report)
        {
            foreach (NuclideDefinition definition in definitions)
            {
                if (!string.Equals(NuclideToken(definition.Name), nuclide, StringComparison.OrdinalIgnoreCase)
                    || !PositiveFinite(definition.Intencity))
                {
                    continue;
                }

                // Окно — то же, которым сверка библиотеки ищет строку базы.
                if (!lines.Any(line => Math.Abs(line[0] - definition.Energy) <= NuclideDbLink.WindowKev))
                {
                    report.IgnoredTypedLines.Add(nuclide + " "
                        + definition.Energy.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>
        /// (`AMBER306`) Образ из линий библиотеки как вбиты: подписи нет в базах. Null —
        /// годных линий нет. Полоса та же, что у всех образов (`S98`).
        /// </summary>
        static FsaComponent TypedComponent(string nuclide, List<NuclideDefinition> definitions, FsaSampleSpec spec)
        {
            // Рентген — мешающий образ: активности за ним нет. Признак — отсутствие
            // массового числа в подписи, см. NuclideDefinition.IsElementXrayName.
            FsaComponentKind kind = NuclideDefinition.IsElementXrayName(nuclide)
                ? FsaComponentKind.Nuisance
                : FsaComponentKind.Single;
            FsaComponent component = new FsaComponent(nuclide, kind);
            double floor = spec.LineFloorKev;
            foreach (NuclideDefinition definition in definitions)
            {
                if (!PositiveFinite(definition.Intencity)
                    || definition.Energy < floor || definition.Energy > spec.MaxEnergyKev
                    || !string.Equals(NuclideToken(definition.Name), nuclide, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Одна линия может быть записана дважды: своя запись и запись «в
                // цепочке», либо ручная копия с округлённой энергией. Обе в образе
                // удваивают вес линии. Порог 0.05 кэВ: реальных раздельных линий
                // ближе не бывает, а дубль с иной точностью записи — та же линия.
                if (component.Lines.Any(line => Math.Abs(line.Energy - definition.Energy) < 0.05))
                {
                    continue;
                }

                component.Lines.Add(new FsaLine(nuclide, definition.Energy, definition.Intencity));
            }

            return component.Lines.Count > 0 ? component : null;
        }

        /// <summary>Имя нуклида без хвоста вида «(Th-232)».</summary>
        static string NuclideToken(string name)
        {
            return NuclideDefinition.NuclideNameOf(name);
        }

        /// <summary>
        /// Число линии, с которым можно безопасно строить физический образ.
        /// `NaN` не сравним с нулём, а положительная бесконечность больше него,
        /// поэтому одного условия <c>&gt; 0</c> здесь недостаточно.
        /// </summary>
        static bool PositiveFinite(double value)
        {
            return value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
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
