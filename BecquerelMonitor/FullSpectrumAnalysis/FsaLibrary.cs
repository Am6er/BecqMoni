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
                total += f.Value * MaterialDatabase.Interpolate(
                    element.EnergyKev, element.LogEnergyKev,
                    element.Total, element.LogTotal, lo, hi, energyKev, logEnergyKev);
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
            return BuildFromPeaks(peaks, nuclideDefinitions, null);
        }

        /// <summary>
        /// То же, но с массовыми долями вещества КРИСТАЛЛА (`S122`). Они
        /// нужны только образам вылета: по ним считается доля рождения пар,
        /// и по ней отбираются родители
        /// (<see cref="EscapeAndAnnihilation(List{FsaComponent}, IDictionary{int, double})"/>).
        /// Долей нет — отбор идёт прежним правилом, по одному выходу линии.
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
        /// <see cref="FsaCalculationOptions.AtomicXray"/>): выключенный не
        /// строит образ характеристического рентгена элемента, названного
        /// подписью пика. На пути из баз ту же роль играет
        /// <see cref="FsaSampleSpec.AtomicXray"/>. Образы вылета и аннигиляции
        /// сюда НЕ относятся — их судьбу решает анализатор, знающий про матрицу
        /// (<see cref="FsaAnalyzer.EscapeAndAnnihilation"/>).
        /// </summary>
        public static List<FsaComponent> BuildFromPeaks(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions,
            IDictionary<int, double> crystalFractions,
            bool atomicXray)
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

                // ⛔ ПОДПИСЬ «ВЫЛЕТ» НУКЛИДОМ НЕ ЯВЛЯЕТСЯ (`S141`, 06.09.2026).
                // Признак нуклидного имени — массовое число
                // (<see cref="NuclideDefinition.IsElementXrayName"/>), и
                // «SE-2615» ему отвечает: цифры в имени есть. Оттого подпись
                // вылета становилась ОБЫЧНЫМ компонентом вида `Single` — с
                // долей в «пироге» активностей и с линией 2103.5 кэВ, которая
                // сама выше порога пар и родила бы вылет вылета.
                //
                // Раньше этого случиться не могло: имена `SE-`/`DE-` в подписи
                // брались только из общей библиотеки приложения, где их нет.
                // Сегодня их даёт `FsaSampleLibrary.AsDefinitions` — корпусный
                // путь подписывает пики СВОЕЙ библиотекой, а в ней вылет уже
                // есть. Измерено в тот же день: библиотека по пикам выросла с
                // 9 образов до 11, SE/DE с 4 до 6.
                //
                // Образы вылета от этого не теряются: ниже их СТРОИТ
                // <see cref="EscapeAndAnnihilation"/> — от линий состава и по
                // физике, а не по тому, что финдер подписал.
                if (IsEscapeImage(nuclide))
                {
                    continue;
                }

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
                    if (!PositiveFinite(definition.Intencity)
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
                // (`A145`) Выключенный флажок «атомный рентген» снимает этот
                // образ — и только его: нуклидные образы выше от него не зависят.
                int z = atomicXray ? MaterialDatabase.ZOf(nuclide) : 0;
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
                foreach (FsaComponent component in
                         EscapeAndAnnihilation(result, crystalFractions))
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
