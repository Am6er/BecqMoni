using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Одна цепочка в составе пробы: корень ряда и, при надобности, ограничение
    /// на его членов.
    ///
    /// <see cref="Only"/> нужен ровно для одного случая, и случай этот в корпусе
    /// есть: урановое стекло (`U-238u`). Ряд в нём ОБОРВАН на радии — уран в
    /// стекло попал химически очищенным, и равновесия ниже Ra-226 нет. Взять
    /// такому образцу весь ряд значит предъявить ему Bi-214 и Pb-214, которых в
    /// нём нет, и отдать им структуру. Список членов повторяет
    /// `build_corpus.sample_lines`, где то же самое сделано для калибровки.
    /// Пусто — брать весь ряд.
    /// </summary>
    public sealed class FsaSampleChain
    {
        public FsaSampleChain(string root)
        {
            this.Root = root;
        }

        public FsaSampleChain(string root, params string[] only)
        {
            this.Root = root;
            if (only != null)
            {
                foreach (string member in only)
                {
                    this.Only.Add(member);
                }
            }
        }

        /// <summary>`nucid` корня ряда: «232TH», «238U».</summary>
        public string Root;

        /// <summary>`nucid` членов, которыми ряд ограничен; пусто — весь ряд.</summary>
        public readonly HashSet<string> Only =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Что снято этим спектром и чем оно снято — вход
    /// <see cref="FsaSampleLibrary"/>.
    ///
    /// Заполняется тем, кто про спектр это знает: для корпуса — `manifest.csv`
    /// и `materials.csv` (проба `CorpusFsaProbe`), в приложении — объявленный
    /// состав документа. Сам сборщик ничего не угадывает.
    /// </summary>
    public sealed class FsaSampleSpec
    {
        /// <summary>Ряды в пробе.</summary>
        public readonly List<FsaSampleChain> Chains = new List<FsaSampleChain>();

        /// <summary>Одиночные нуклиды поверх рядов: `nucid`, «40K», «176LU».</summary>
        public readonly List<string> Nuclides = new List<string>();

        /// <summary>Z элементов ПРОБЫ — вольфрам электрода WT-20, лютеций Lu₂O₃.</summary>
        public readonly List<int> SampleElements = new List<int>();

        /// <summary>Z элементов КРИСТАЛЛА — иод и цезий у CsI, иод у NaI.</summary>
        public readonly List<int> CrystalElements = new List<int>();

        /// <summary>
        /// МАССОВЫЕ доли элементов кристалла, Z → доля (`S84`, 19.08.2026).
        /// Пусто — доли неизвестны, и тогда образ вылета считает их равными.
        ///
        /// Заведены потому, что вылет у кристалла ОДИН на все его элементы, а
        /// соотношение членов внутри него задаётся не данными, а веществом:
        /// вероятность того, что первое взаимодействие оказалось K-поглощением
        /// именно в элементе i, равна w_i·τ_i,K / Σ_j w_j·μ_j. Доли лежат прямо
        /// в геометрии (<c>GeometryMaterial.Fractions</c>), и до 19.08.2026
        /// <see cref="HeavyElementsOf"/> их просто выбрасывала, оставляя одни
        /// номера.
        /// </summary>
        public readonly Dictionary<int, double> CrystalFractions =
            new Dictionary<int, double>();

        /// <summary>
        /// Короткое имя вещества кристалла — «CsI», «NaI», «LaBr3». Им зовётся
        /// образ вылета (`Esc-CsI`), чтобы читатель видел, из чего прибор
        /// считает вылет, а не только то, что вылет есть. Пусто — имя
        /// собирается из символов элементов по убыванию доли.
        /// </summary>
        public string CrystalName = "";

        /// <summary>Z элементов ЗАЩИТЫ И ОБВЯЗКИ — свинец домика, железо корпуса.</summary>
        public readonly List<int> ShieldElements = new List<int>();

        /// <summary>
        /// Нижняя граница рабочего диапазона, кэВ. Ставится вызывающим из
        /// `Min_Range` прибора — то есть из настройки ПОИСКА ПИКОВ.
        ///
        /// ⚠ С 25.08.2026 линии режет НЕ она, а <see cref="LineFloorKev"/>:
        /// диапазон поиска пиков и полоса, в которой модель имеет право
        /// говорить, — разные вещи (`S98`).
        /// </summary>
        public double MinEnergyKev = 10.0;

        /// <summary>Верхняя граница рабочего диапазона, кэВ.</summary>
        public double MaxEnergyKev = 3200.0;

        /// <summary>
        /// Как сводятся полоса фита и полоса библиотеки (`S98`).
        ///
        /// ⛔ ЧИТАЕТСЯ, А НЕ ХРАНИТСЯ (`S101`, измерено 26.08.2026). Здесь
        /// стояло ПОЛЕ с копией <see cref="FsaBand.DefaultMode"/>, снятой в
        /// момент создания спецификации, и такая же копия — у
        /// <see cref="FsaAnalyzer"/>. Двигали обычно вторую (полоса объявлена
        /// свойством анализатора), первая оставалась поставочной, и корпусный
        /// A/B по полосе молча мерил ОДНО И ТО ЖЕ: плечо `--band=whole` дало
        /// состав и пределы побитово те же, что поставочный прогон, — 32 файла
        /// из 32. Теперь оба конца читают статику при обращении, присваивания
        /// нет ни у одного, и развести их нечем.
        /// Разбор веток и цена каждой — в шапке <see cref="FsaBand"/>.
        /// </summary>
        public FsaBandMode Band
        {
            get { return FsaBand.DefaultMode; }
        }

        /// <summary>
        /// Пол полосы библиотеки, кэВ, при <see cref="FsaBandMode.LibraryToFit"/>.
        /// Ноль или отрицательное — пола нет, режет <see cref="MinEnergyKev"/>.
        /// ⛔ Как и <see cref="Band"/>, читается у <see cref="FsaBand.DefaultFloor"/>
        /// при обращении и своей копии не имеет (`S101`).
        /// </summary>
        public double LibraryFloorKev
        {
            get { return FsaBand.DefaultFloor; }
        }

        /// <summary>
        /// ⛔ КРИВАЯ ЭФФЕКТИВНОСТИ СПЕКТРА — нужна ТОЛЬКО чтобы назначить пол
        /// полосы при <see cref="FsaBandMode.LibraryToFitByCurve"/>. Ставится
        /// ОДНИМ присваиванием у каждого, кто строит спецификацию; сам расчёт
        /// живёт в <see cref="FsaEfficiency.FloorAtFraction"/> и больше нигде —
        /// иначе у решения «где пол» завелась бы третья копия, а две у полосы
        /// уже стоили дня разбора (`S101`).
        ///
        /// `null` — законное значение: у 38 спектров корпуса из 121 кривой нет,
        /// и пол по кривой им назначить нечем.
        /// </summary>
        public FsaEfficiency Efficiency;

        // ⛔ СОБСТВЕННОЙ ДОЛИ У СПЕЦИФИКАЦИИ НЕТ (`S101`). Здесь стояло поле
        // `FloorFraction` с признаком «ноль или отрицательное — брать
        // умолчание», и не ставил его никто. Убрано не за неиспользуемость, а
        // за то, что это ТРЕТИЙ рычаг у одного решения: заверение анализатора
        // (`FsaAnalyzer.BandNote`) называет пол по `FsaBand.DefaultFloorFraction`,
        // и спецификация со своей долей заставила бы два конца печатать разные
        // числа об одном и том же поле. Долю двигает `FsaBand.DefaultFloorFraction`,
        // её видят оба; ключ пробы — `--floor-frac=`.

        /// <summary>
        /// ⛔ ГРАНИЦА, КОТОРАЯ РЕАЛЬНО РЕЖЕТ ЛИНИИ. Одна на все образы —
        /// распадные, рентген пробы и кристалла, пики вылета: разойдясь в ней,
        /// они разошлись бы в том, что вообще существует ниже `Min_Range`.
        ///
        /// При <see cref="FsaBandMode.LibraryToFit"/> — пол
        /// <see cref="LibraryFloorKev"/>, но НЕ выше `Min_Range` (поднимать
        /// границу этот режим не должен); иначе — `Min_Range`, как было.
        ///
        /// Измерено 25.08.2026 (понятная часть корпуса, 81 спектр): при поле
        /// 10 кэВ линии распада возвращаются у 15 спектров, а иодная Kα
        /// кристалла (28.32/28.61 кэВ, 81.3 % веса K-серии) — у ВСЕХ 81.
        /// </summary>
        public double LineFloorKev
        {
            get
            {
                // ⛔ Пол ПО КРИВОЙ (решение Amber 27.08.2026, `S98`): его знает
                // сама кривая спектра, а не число в коде. Кривой нет — падаем
                // на `Min_Range`, и это запасная ветвь, а не отказ.
                // ⛔ ОПОРА ПО СТОЛБЦУ (`S103`) на этом конце НИЧЕГО не режет
                // сверх пола по кривой, и это не упущение: доля континуума
                // существует только внутри фита (нужны активный набор колонок,
                // Gram на подобранном узле дрейфа и веса решателя), а здесь
                // библиотека собирается ДО всякого фита. Первый проход поэтому
                // впускает ровно то же, что поставка, а выброс делает
                // `FsaAnalyzer` вторым проходом.
                if (this.Band == FsaBandMode.LibraryToFitByCurve
                    || this.Band == FsaBandMode.LibraryToFitByShare)
                {
                    double byCurve = this.CurveFloorKev;
                    return byCurve > 0.0 ? Math.Min(this.MinEnergyKev, byCurve) : this.MinEnergyKev;
                }

                if (this.Band != FsaBandMode.LibraryToFit || !(this.LibraryFloorKev > 0.0))
                {
                    return this.MinEnergyKev;
                }

                return Math.Min(this.MinEnergyKev, this.LibraryFloorKev);
            }
        }

        /// <summary>
        /// Пол, который назначает САМА кривая, кэВ; 0 — назначить нечем (кривой
        /// нет либо доля недостижима). Отдельным свойством, чтобы его можно было
        /// НАПЕЧАТАТЬ: заверение обязано называть то, что случилось, а не то,
        /// что заказывали.
        /// </summary>
        public double CurveFloorKev
        {
            get
            {
                if (this.Efficiency == null)
                {
                    return 0.0;
                }

                // Доля — ОДНА на разбор и берётся у статики при обращении
                // (`S101`): её же читает заверение анализатора, и второй копии
                // здесь быть не должно.
                return this.Efficiency.FloorAtFraction(FsaBand.DefaultFloorFraction);
            }
        }

        /// <summary>
        /// Добавлять вездесущие K-40 / Th-232 / Ra-226 — NORM.
        ///
        /// ⛔ **Правило Amber 18.08.2026: «везде, где не знаешь, суй NORM — не из
        /// `NuclideDefinition`, а из базы».** То есть это не оговорка и не
        /// умолчание на всякий случай: незнание состава закрывается природными
        /// рядами, и берутся они оттуда же, откуда всё остальное, — из
        /// `nucdb.decay_chain` обходом от корней 232TH и 226RA плюс 40K. Ровно
        /// поэтому <see cref="RoomChains"/> — список `nucid`, а не имён
        /// компонентов конфига.
        ///
        /// «Снятым нуклидом» NORM не является, но физически в спектре есть, и
        /// без него этой структуре не найдётся имени. Второе следствие того же
        /// правила измерено: найденный ПОСЛЕ вычитания фона природный ряд — не
        /// выдумка разбора («по сути да, там NORM», Amber), и мерка корпуса
        /// считает такие компоненты отдельной колонкой «комнатных», а не
        /// фантомами (`S59` «б»).
        ///
        /// Ключ оставлен A/B-стороной замера (`--no-room`), а не выключателем на
        /// каждый день: без NORM Σχ² по корпусу хуже на 8…9 %.
        /// </summary>
        public bool Room = true;

        /// <summary>
        /// Добавлять атомный рентген — пробы, кристалла, защиты — и пики вылета
        /// кристалла (решение Amber 18.08.2026, `S56`). A/B-сторона замера.
        /// </summary>
        public bool AtomicXray = true;

        /// <summary>
        /// Наименьшая накопленная доля ветвления, при которой член ряда идёт в
        /// состав отдельным образом.
        ///
        /// ⛔ Порог здесь ОБЯЗАТЕЛЕН, и вот почему. Члены ряда входят в разбор
        /// РАЗНЫМИ образами со свободными амплитудами (так принято: «разрез
        /// цепочки получается сам», <see cref="FsaLibrary"/>). Свободная
        /// амплитуда снимает множитель ветвления начисто: у Tl-210 в радиевом
        /// ряду доля 2·10⁻⁴, а линий двадцать четыре, и предъявленный фиту
        /// такой образ ведёт себя ровно как фантом Pu-238 из `N18` — двадцать
        /// четыре свободные линии там, где физически нет ничего. Порог 10⁻³
        /// оставляет ряд целым (у Th-232 и Ra-226 все настоящие члены стоят на
        /// 1.0000 либо 0.3594) и снимает хвосты редких ветвей.
        /// </summary>
        public double MinChainBranch = 1.0e-3;

        /// <summary>
        /// РАВНОВЕСИЕ: ряд идёт в разбор ОДНОЙ колонкой с одной свободной
        /// амплитудой, а относительные веса его членов закреплены накопленной
        /// долей ветвления (решение Amber 18.08.2026, `S70`; умолчание —
        /// ВКЛЮЧЕНО).
        ///
        /// ⛔ Выключено — прежнее поведение: у каждого члена ряда СВОЯ свободная
        /// амплитуда, «разрез цепочки получается сам», и именно так видно
        /// НЕРАВНОВЕСИЕ — оборванный ряд уранового стекла (`S65`), ушедшая
        /// эманация радона. Ради этого случая свободные амплитуды и оставлены,
        /// и убирать их насовсем нельзя.
        ///
        /// Цена свободных амплитуд измерена и она не мала. На
        /// `Th232_29.07.2022.xml` — чистый ториевый источник, ряд заведомо
        /// равновесный — Ra-224 получил 8.22 % против положенных ему по
        /// равновесию ≈0.9 %: у него единственная гамма 240.986 кэВ с выходом
        /// 4.1 %, в 2.4 кэВ от 238.632 кэВ Pb-212 с выходом 43.6 %, и при ПШПВ
        /// прибора в 52 канала обе линии — один бугор. Свободная амплитуда
        /// раздаёт этот бугор как угодно, связка одной амплитудой снимает
        /// вопрос по построению.
        ///
        /// ⚠ Отбором списка это НЕ является: у Th-232 равновесны ВСЕ члены, и
        /// никакой отсев на том спектре не изменил бы ничего.
        /// </summary>
        public bool Equilibrium = true;

        /// <summary>
        /// Верхняя энергия родительской линии, которой ещё строится пик вылета,
        /// кэВ. Выше неё фотоэффект в кристалле пренебрежимо мал против
        /// комптона, и вылет тонет; счёт от этого не портится (вес выйдет почти
        /// нулевым), но образ разбухает линиями ни о чём.
        /// </summary>
        public double EscapeParentMaxKev = 600.0;

        /// <summary>
        /// Наименьший вес линии вылета в долях сильнейшей линии ТОГО ЖЕ образа.
        ///
        /// ⛔ Порог здесь не украшение, и цена его отсутствия измерена
        /// 18.08.2026 на `ASN16_Lu176`: без порога образ вылета иода вышел
        /// гребёнкой в 66 линий по всей шкале — по линии на каждую линию
        /// состава, включая тысячные доли процента вездесущих рядов. Гребёнка
        /// делает сразу две скверные вещи: в NNLS она свободный образ, готовый
        /// забрать любую необъяснённую структуру (механизм фантома `N18`), а в
        /// поиске пиков она забирает ПОДПИСИ — на том прогоне «Esc-I» и «Esc-Cs»
        /// подписали четыре пика из девяти, включая 304.32, который есть линия
        /// 306.78 самого лютеция.
        /// </summary>
        public double EscapeMinRelativeWeight = 0.02;
    }

    /// <summary>
    /// Сборка библиотеки образов ПО ОБЪЯВЛЕННОМУ СОСТАВУ ПРОБЫ — исполнение
    /// первого постулата (`S56`, формулировка Amber 17.08.2026): «у каждого
    /// спектра — СВОЯ база пиков, привязанная к снятому нуклиду».
    ///
    /// ЧЕМ ЭТО ОТЛИЧАЕТСЯ ОТ <see cref="FsaLibrary.BuildFromPeaks"/>. Там состав
    /// задаёт поиск пиков: какими нуклидами он подписал спектр, те и
    /// раскладываются, — а подписывает он из ОБЩЕГО списка, предъявленного
    /// прибору. Здесь наоборот: состав известен заранее (корпус знает, что
    /// снято), и общая библиотека приложения перестаёт быть тем, что
    /// предъявляется спектру. Цена прежнего порядка измерена в тот же день
    /// (`N18`): на `ASN16_Lu176` завёлся Pu-238 долей 1.7 % при z = 31.77 —
    /// единственной линией 152 кэВ в интенсивности 0.0009 %, попавшей в полосу
    /// обратного рассеяния линии 306.78 самого лютеция. Своим списком такой
    /// кандидат не появился бы вовсе.
    ///
    /// ⛔ ИСТОЧНИК — БАЗЫ, А НЕ КОНФИГ (указание Amber 18.08.2026). Ни
    /// `NuclideDefinition.xml`, ни `NuclideSet` здесь не читаются и читаться не
    /// должны: линии берутся из `nucdb.decay_radiations`, ряды — из
    /// `nucdb.decay_chain`, атомный рентген — из `matdb.xray_fluorescence`.
    /// Конфиг — дело человека и графика; состав образа — дело базы.
    ///
    /// ЛИНИИ БЕРУТСЯ ИЗ ВСЕХ ИЗЛУЧЕНИЙ РАСПАДА, А НЕ ИЗ ГАММ. Измерено
    /// 17.08.2026: у Lu-176 единственная гамма ниже 200 кэВ — 88.34, а реальный
    /// спектр внизу держится на K-рентгене гафния 54…65 кэВ (тип `X`); база из
    /// одних гамм дала невязку 405σ на 60.8 кэВ, с рентгеном — 251σ,
    /// χ²/ndf 5.342 → 4.696. Поэтому сюда идут типы `G` и `X`, K- и L-серия.
    ///
    /// ЧТО ЗДЕСЬ ЕСТЬ СВЕРХ РАСПАДА — и почему это не противоречит правилу
    /// «перенос живёт в матрице». Правило остаётся верным для СЧЁТА: матрица
    /// считает флуоресценцию пробы и вылет рентгена кристалла честно, из
    /// геометрии (`F27`, физика 12). Но матрицы нет у сорока спектров корпуса
    /// вовсе, а там, где она есть, её никто не проверяет — `E31` и `B14`
    /// прожили именно на этом. Решение Amber 18.08.2026: атомные образы
    /// кладутся ВСЕМ спектрам, свободной амплитудой, как опора будущей
    /// кросс-проверки (`S60`). ⚠ Отсюда следствие, которое надо помнить при
    /// чтении чисел: на понятной части эти образы конкурируют с матрицей за
    /// одни и те же отсчёты, и разница прогонов «было/стало» принадлежит не
    /// только сужению библиотеки. Разводится ключом `AtomicXray`.
    /// </summary>
    public static class FsaSampleLibrary
    {
        /// <summary>
        /// Что собралось и что не собралось — для проб и журнала. Пусто, если
        /// сказать нечего. Заводится на КАЖДЫЙ вызов <see cref="Build"/>:
        /// «нуклида нет в базе» и «нуклид есть, но линий в окне нет» с виду
        /// одно и то же, а чинятся по-разному.
        /// </summary>
        public sealed class Report
        {
            public readonly List<string> Notes = new List<string>();

            /// <summary>Членов рядов, отброшенных порогом ветвления.</summary>
            public int ChainMembersDropped;

            /// <summary>Образов распада (ряды и одиночные нуклиды).</summary>
            public int DecayComponents;

            /// <summary>Образов атомного рентгена и вылета.</summary>
            public int AtomicComponents;

            /// <summary>Линий во всех образах вместе.</summary>
            public int Lines;

            /// <summary>
            /// Полоса, в которой собиралась библиотека, готовой строкой
            /// (`S98`). Печатается всегда: умолчание, которого не видно в
            /// выводе прогона, ничем не отличается от случайного, а это
            /// умолчание меняет базу корпуса.
            /// </summary>
            public string Band = "";

            /// <summary>
            /// Сколько линий легло в образы НИЖЕ `Min_Range` — то есть ровно
            /// то, что добавил пол полосы (`S98`). Ноль при
            /// <see cref="FsaBandMode.Whole"/> и <see cref="FsaBandMode.FitToLibrary"/>
            /// по построению: там пол и есть `Min_Range`.
            /// </summary>
            public int LinesBelowMinRange;

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendFormat(CultureInfo.InvariantCulture,
                                  "распад {0}, атомных {1}, линий {2}",
                                  this.DecayComponents, this.AtomicComponents, this.Lines);
                if (!string.IsNullOrEmpty(this.Band))
                {
                    text.Append("; ").Append(this.Band);
                }

                if (this.LinesBelowMinRange > 0)
                {
                    text.AppendFormat(CultureInfo.InvariantCulture,
                                      ", из них ниже Min_Range {0}", this.LinesBelowMinRange);
                }
                if (this.ChainMembersDropped > 0)
                {
                    text.AppendFormat(CultureInfo.InvariantCulture,
                                      "; членов ряда ниже порога ветвления {0}",
                                      this.ChainMembersDropped);
                }

                foreach (string note in this.Notes)
                {
                    text.Append("; ").Append(note);
                }

                return text.ToString();
            }
        }

        static readonly object Gate = new object();

        /// <summary>Кэш линий распада по `nucid`: база одна, спектров сто.</summary>
        static readonly Dictionary<string, List<double[]>> LineCache =
            new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Кэш обходов ряда по корню.</summary>
        static readonly Dictionary<string, Dictionary<string, double>> ChainCache =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Кэш глубин членов ряда по корню (`S65`).</summary>
        static readonly Dictionary<string, Dictionary<string, int>> DepthCache =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Библиотека образов по объявленному составу. Никогда не null; пустой
        /// список означает «состав не дал ни одной линии в рабочем диапазоне» —
        /// это результат, а не отказ.
        /// </summary>
        public static List<FsaComponent> Build(FsaSampleSpec spec)
        {
            Report report;
            return Build(spec, out report);
        }

        /// <summary>То же, с отчётом о сборке.</summary>
        public static List<FsaComponent> Build(FsaSampleSpec spec, out Report report)
        {
            report = new Report();
            var result = new List<FsaComponent>();
            if (spec == null)
            {
                return result;
            }

            // Порядок сохраняется по первому появлению: сначала объявленный
            // состав, потом вездесущие, потом атомные образы. Так состав
            // читается в том же порядке, в каком о нём думают.
            var byName = new Dictionary<string, FsaComponent>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            // Накопленная доля ветвления на нуклид. Один и тот же нуклид может
            // прийти из двух рядов сразу (Ra-226 объявлен сам по себе и лежит
            // внутри U-238; Th-228 — сам по себе и внутри Th-232), и доли у него
            // тогда разные. Берём БОЛЬШУЮ: она отвечает тому ряду, в котором
            // нуклида больше, а свободная амплитуда всё равно перекроет разницу.
            var branch = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            // Хозяин члена — корень ряда, от которого посчитана его доля. При
            // равновесии (`S70`) по нему члены и связываются в одну колонку;
            // без равновесия он не используется вовсе.
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (FsaSampleChain chain in spec.Chains)
            {
                if (chain == null || string.IsNullOrEmpty(chain.Root))
                {
                    continue;
                }

                CollectChain(chain, spec.MinChainBranch, branch, owner, report);
            }

            foreach (string nucid in spec.Nuclides)
            {
                if (string.IsNullOrEmpty(nucid))
                {
                    continue;
                }

                if (IsIsomer(nucid))
                {
                    // Объявить изомер напрямую можно — это осознанный выбор
                    // того, кто заполняет состав, — но сказать об этом надо:
                    // общее правило (Amber 18.08.2026) изомеров не берёт.
                    report.Notes.Add("объявлен изомер " + nucid + " — взят по объявлению");
                }

                // Одиночный нуклид объявлен САМ, значит его выход дан на его
                // собственный распад — доля ветвления единица, и хозяин он сам.
                Remember(branch, owner, nucid, 1.0, nucid);
            }

            // Кто пришёл из ОБЪЯВЛЕННОГО состава, а кто добавлен комнатой.
            // Разница нужна пикам вылета: их образ строится по линиям пробы, и
            // подмешивать туда вездесущие ряды нельзя — в спектре лютеция
            // комната стоит на три порядка ниже, а в образе её линии оказались
            // бы вровень с лютециевыми. Измерено 18.08.2026: без этого деления
            // гребёнка вылета иода выходила в 66 линий на весь спектр.
            var declared = new HashSet<string>(branch.Keys, StringComparer.OrdinalIgnoreCase);

            // Сколько членов у каждого хозяина: колонкой ряда компонент
            // становится, только если членов больше одного. Одинокий корень
            // остаётся обычным одиночным нуклидом, и звать его «рядом» было бы
            // неправдой.
            var groupSize = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (spec.Room)
            {
                // Вездесущие. Ряды берутся целиком, тем же порогом: комната даёт
                // не «немножко тория», а торий в равновесии, просто слабый.
                foreach (string root in RoomChains)
                {
                    foreach (KeyValuePair<string, double> member in ChainBranches(root, report))
                    {
                        // Ряды комнаты — торий и радий, урана среди них нет,
                        // значит изомеров здесь не держим вовсе.
                        if (member.Value >= spec.MinChainBranch && !IsIsomer(member.Key))
                        {
                            Remember(branch, owner, member.Key, member.Value, root);
                        }
                    }
                }

                Remember(branch, owner, "40K", 1.0, "40K");
            }

            // ⚠ Считаются ТОЛЬКО излучающие члены. Обход `decay_chain` доводит
            // ряд до стабильного конца, и у калия в «ряду» лежат ещё аргон с
            // кальцием — они не излучают, образа не дают и членами ряда для
            // связки не являются. Без этой оговорки K-40 становился колонкой
            // РЯДА из одного нуклида: имя то же, а вид `Chain`, и подпись пиков
            // уходила по ветке ряда. `DecayLines` кэширован, второй проход
            // ничего не стоит.
            if (spec.Equilibrium)
            {
                foreach (string nucid in branch.Keys)
                {
                    if (DecayLines(nucid, report).Count == 0)
                    {
                        continue;
                    }

                    string root = OwnerOf(owner, nucid);
                    int have;
                    groupSize[root] = groupSize.TryGetValue(root, out have) ? have + 1 : 1;
                }
            }

            // Имя компонента, доставшееся нуклиду: по нему потом собираются
            // родительские линии пиков вылета. При равновесии член ряда лежит в
            // колонке корня, и искать его под собственным именем уже нельзя.
            var componentOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, double> member in branch)
            {
                List<double[]> lines = DecayLines(member.Key, report);
                if (lines.Count == 0)
                {
                    continue;
                }

                // ⛔ Суммарный выход считается по ВСЕМ линиям распада, до
                // отсева по рабочему окну прибора: это априорное свойство
                // нуклида (`S69`), а не образа. У Ra-228 обе линии — 13.52 и
                // 16.2 кэВ — ниже порога сцинтиллятора, и «выход мал» с «линии
                // вне шкалы» смешивать нельзя.
                double yield = 0.0;
                foreach (double[] line in lines)
                {
                    yield += line[1];
                }

                // Своя линия каждого члена подписана ЕГО именем и при
                // равновесии тоже: каскадное суммирование и совпадения работают
                // по нуклиду линии, а не по имени колонки.
                string self = PrettyName(member.Key);
                string root = spec.Equilibrium ? OwnerOf(owner, member.Key) : member.Key;
                bool grouped = spec.Equilibrium && GroupCount(groupSize, root) > 1;
                string name = grouped ? PrettyName(root) : self;
                componentOf[member.Key] = name;

                FsaComponent component = Take(byName, order, name,
                                              grouped ? FsaComponentKind.Chain
                                                      : FsaComponentKind.Single);

                // В колонку ряда попадают несколько нуклидов; априорным выходом
                // колонки берётся НАИБОЛЬШИЙ из них — вопрос, на который этот
                // выход отвечает, звучит «может ли эта строка вообще что-то
                // показать», и одного видимого члена для «да» довольно.
                if (double.IsNaN(component.TotalYieldPercent)
                    || yield > component.TotalYieldPercent)
                {
                    component.TotalYieldPercent = yield;
                }

                // S98: снизу режет ПОЛ БИБЛИОТЕКИ, а не диапазон поиска пиков.
                // У Cd-109 разница видна целиком: ниже `Min_Range` = 30 кэВ
                // лежит K-серия серебра 21.99/22.163/25.03/25.454 с суммарным
                // выходом 102.3 % на распад, а в полосе остаётся одна гамма
                // 88.03 с выходом 3.64 % — то есть 97 % излучения нуклида
                // отбрасывалось до того, как фит его увидит.
                double floor = spec.LineFloorKev;
                foreach (double[] line in lines)
                {
                    if (line[0] < floor || line[0] > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    AddLine(component, self, line[0], line[1] * member.Value);
                }
            }

            // Образы без единой линии в окне выбрасываются здесь, а не при
            // сборке: у члена ряда линии могут быть все выше потолка прибора, и
            // пустой образ в NNLS — вырожденный столбец.
            foreach (string name in order)
            {
                FsaComponent component = byName[name];
                if (component.Lines.Count > 0)
                {
                    result.Add(component);
                    report.DecayComponents++;
                    report.Lines += component.Lines.Count;
                }
            }

            if (spec.AtomicXray)
            {
                int before = result.Count;

                // ⚠ Имя берётся то, под которым нуклид РЕАЛЬНО лёг в состав, а
                // не `PrettyName` его самого: при равновесии член ряда лежит в
                // колонке корня, и поиск по собственному имени не нашёл бы его
                // вовсе — гребёнка вылета осталась бы без родительских линий.
                var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string nucid in declared)
                {
                    string name;
                    declaredNames.Add(componentOf.TryGetValue(nucid, out name)
                                      ? name : PrettyName(nucid));
                }

                AddAtomic(spec, result, declaredNames, report);
                report.AtomicComponents = result.Count - before;
            }

            AddAnnihilation(result, report);

            // S98: полоса библиотеки — в отчёт, и он её печатает. Считается
            // ЗДЕСЬ, по готовым образам, а не по трём местам отсева: так число
            // «линий ниже Min_Range» не зависит от того, сколько мест эту
            // границу читают, и не разъедется, если появится четвёртое.
            // ⛔ Печатается ФАКТИЧЕСКИЙ пол, а не заказанный: при поле по кривой
            // у спектра без кривой его назначить нечем, и молчать об этом нельзя.
            report.Band = FsaBand.Describe(spec.Band,
                                           spec.Band == FsaBandMode.LibraryToFitByCurve
                                           || spec.Band == FsaBandMode.LibraryToFitByShare
                                               ? spec.CurveFloorKev
                                               : spec.LibraryFloorKev,
                                           spec.MinEnergyKev, spec.MaxEnergyKev);
            foreach (FsaComponent component in result)
            {
                foreach (FsaLine line in component.Lines)
                {
                    if (line.Energy < spec.MinEnergyKev)
                    {
                        report.LinesBelowMinRange++;
                    }
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Распад
        // ------------------------------------------------------------------

        /// <summary>
        /// Ряды, которые кладутся ЛЮБОМУ спектру: торий и радий комнаты. Калий
        /// стоит отдельной строкой — он не ряд.
        /// </summary>
        static readonly string[] RoomChains = { "232TH", "226RA" };

        /// <summary>
        /// Члены ряда, прошедшие оба правила отбора, — в накопитель долей.
        ///
        /// Вынесено отдельным методом, потому что читателей у правила стало
        /// ДВА: сборка библиотеки по объявленному составу
        /// (<see cref="Build"/>) и вывод состава из поиска пиков
        /// (<see cref="FsaCompositionInference"/>, `S57`). Второй обязан
        /// считать «ожидаемые линии родителя» ровно по тому составу, который
        /// потом и будет собран, — иначе критерий значимости мерит один список,
        /// а фиту предъявляется другой.
        ///
        /// ⛔ Изомеры в состав НЕ идут — кроме ряда урана-238 (указание
        /// Amber 18.08.2026). Исключение здесь не поблажка, а физика:
        /// Pa-234m1 несёт линию 1001.03 кэВ, классический «урановый монопик»,
        /// по которому уран и опознают; выбросив изомер, ряд U-238 остался бы
        /// вовсе без сильной линии. В остальных рядах изомер — отдельное
        /// состояние с собственным временем жизни, и отдельным свободным
        /// образом он делает то же, что делают хвосты редких ветвей: даёт фиту
        /// свободные линии там, где равновесия нет.
        ///
        /// Основание у правила не только формальное. Слова Amber в тот же день:
        /// на приборах AtomSpectra (ASN16, AS80x80 и прочие «на A») изомеры в
        /// спектрах встречаются ТОЛЬКО в урановом стекле; у америция там линии
        /// стабильные, при активности порядка 65 кБк и ниже (оценка для
        /// понимания порядка). То есть исключение ровно одно и оно названо, а
        /// не выведено.
        /// </summary>
        internal static void CollectChain(FsaSampleChain chain, double minBranch,
                                          Dictionary<string, double> branch, Report report)
        {
            CollectChain(chain, minBranch, branch, null, report);
        }

        /// <summary>
        /// То же, с записью хозяина каждого члена (корня ряда) — нужна связке
        /// равновесия (`S70`). Читателю, которому хозяин не нужен, годится
        /// перегрузка выше.
        /// </summary>
        internal static void CollectChain(FsaSampleChain chain, double minBranch,
                                          Dictionary<string, double> branch,
                                          Dictionary<string, string> owner, Report report)
        {
            if (chain == null || string.IsNullOrEmpty(chain.Root))
            {
                return;
            }

            bool keepIsomers = string.Equals(chain.Root, "238U",
                                             StringComparison.OrdinalIgnoreCase);
            Dictionary<string, double> members = ChainBranches(chain.Root, report);
            foreach (KeyValuePair<string, double> member in members)
            {
                if (chain.Only.Count > 0 && !chain.Only.Contains(member.Key))
                {
                    continue;
                }

                if (!keepIsomers && IsIsomer(member.Key))
                {
                    continue;
                }

                if (member.Value < minBranch)
                {
                    report.ChainMembersDropped++;
                    continue;
                }

                Remember(branch, owner, member.Key, member.Value, chain.Root);
            }
        }

        /// <summary>
        /// {nucid → накопленная доля ветвления от корня}, только основные
        /// состояния родителя.
        ///
        /// Правило `l_seqno` — то же, что в `tools/CORPUS/scripts/chains.py`, и
        /// оно не косметическое: строки с `l_seqno` больше минимального
        /// описывают распад ВОЗБУЖДЁННОГО уровня и дублируют переход с другим
        /// ветвлением (у 212BI это 35.94 % при нуле и 67 % при пяти). Изомер
        /// при этом имеет собственный `nucid` (234PAm1), поэтому наименьший
        /// присутствующий уровень и есть физический распад.
        ///
        /// ⚠ Метод ОТКРЫТ ради проб, а не ради приложения: обходов ряда в
        /// дереве три (здесь, `NucBase.NucBaseFramework.GetChainBranches` и
        /// `tools/CORPUS/scripts/chains.py`), и разойтись они уже успели —
        /// `S62`. Сверять их между собой можно только имея доступ ко всем трём;
        /// читатель этой сверки — `ChainProbe`, раздел «два обхода ряда».
        /// </summary>
        public static Dictionary<string, double> ChainBranches(string root, Report report)
        {
            lock (Gate)
            {
                Dictionary<string, double> cached;
                if (ChainCache.TryGetValue(root, out cached))
                {
                    return cached;
                }
            }

            var branch = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Шаг 1: РЁБРА. Обход в ширину здесь только открывает узлы, а
                // доли не считает вовсе — см. шаг 2, почему это принципиально.
                var edges = new Dictionary<string, List<KeyValuePair<string, double>>>(
                    StringComparer.OrdinalIgnoreCase);
                var order = new List<string> { root };
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
                using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select daughter_nucid, perc from decay_chain d"
                        + " where nucid = $n and perc not null"
                        + " and l_seqno = (select min(l_seqno) from decay_chain x"
                        + "                where x.nucid = d.nucid"
                        + "                  and x.daughter_nucid = d.daughter_nucid"
                        + "                  and x.dec_type = d.dec_type)";
                    command.Parameters.AddWithValue("$n", root);
                    for (int i = 0; i < order.Count && order.Count <= MaxChainNodes; i++)
                    {
                        string current = order[i];
                        command.Parameters["$n"].Value = current;
                        var step = new List<KeyValuePair<string, double>>();
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string daughter = reader.IsDBNull(0) ? null : reader.GetString(0);
                                if (string.IsNullOrEmpty(daughter)
                                    || string.Equals(daughter, current, StringComparison.OrdinalIgnoreCase))
                                {
                                    // 238U несёт самопетлю на l_seqno-119.
                                    continue;
                                }

                                double percent;
                                if (!TryNumber(reader, 1, out percent) || !(percent > 0.0))
                                {
                                    continue;
                                }

                                step.Add(new KeyValuePair<string, double>(daughter, percent));
                                if (known.Add(daughter))
                                {
                                    order.Add(daughter);
                                }
                            }
                        }

                        edges[current] = step;
                    }
                }

                // Шаг 2: ДОЛИ РЕЛАКСАЦИЕЙ ДО НЕПОДВИЖНОЙ ТОЧКИ (`S62`).
                //
                // ⛔ Обход в ширину здесь давал ответ НА ПОРЯДКИ НЕВЕРНЫЙ, и
                // причина в том, что он раскрывает узел один раз — значением,
                // какое у того было В МОМЕНТ раскрытия. Вклад, пришедший к тому
                // же узлу позже, детям уже не передавался. В радиевом ряду
                // Pb-210 попадает в очередь раньше Po-214, который и даёт ему
                // почти всю долю: сам Pb-210 выходил верным (1.0), а его дети —
                // 3e-5 вместо 1.0, то есть ниже `MinChainBranch` = 1e-3, и из
                // библиотеки выпадали вовсе.
                //
                // Доля — это СУММА ПО ВСЕМ ПУТЯМ от корня, x = e_root + x*P,
                // и считается она повторением подстановки: за проход вклад
                // продвигается на одно ребро, значит сходимость наступает за
                // число проходов, равное длине самого длинного пути. Ряды
                // короткие (два десятка членов), но зажим по числу проходов
                // стоит на случай кольца в поставке.
                double drift = 0.0;
                for (int pass = 0; pass < MaxChainPasses; pass++)
                {
                    var next = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    next[root] = 1.0;
                    foreach (string parent in order)
                    {
                        double have;
                        List<KeyValuePair<string, double>> outgoing;
                        if (!branch.TryGetValue(parent, out have) || !(have > 0.0)
                            || !edges.TryGetValue(parent, out outgoing))
                        {
                            continue;
                        }

                        foreach (KeyValuePair<string, double> edge in outgoing)
                        {
                            double add = have * edge.Value / 100.0;
                            double already;
                            next[edge.Key] = (next.TryGetValue(edge.Key, out already) ? already : 0.0) + add;
                        }
                    }

                    drift = 0.0;
                    foreach (KeyValuePair<string, double> row in next)
                    {
                        double was;
                        double gap = Math.Abs(row.Value - (branch.TryGetValue(row.Key, out was) ? was : 0.0));
                        if (gap > drift)
                        {
                            drift = gap;
                        }
                    }

                    branch = next;
                    if (drift <= ChainConverged)
                    {
                        break;
                    }
                }

                if (drift > ChainConverged)
                {
                    // Признак отказа без читателя — не признак. Ряд, не
                    // сошедшийся за отведённые проходы, означает кольцо в
                    // поставке, и молчать об этом нельзя.
                    report.Notes.Add("ряд " + root + ": доли не сошлись за "
                                     + MaxChainPasses + " проходов, остаток "
                                     + drift.ToString("E2", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception error)
            {
                // Отказ базы не должен ронять разбор — но и молчать нельзя:
                // «ряд пуст» и «читатель сломан» с виду одно и то же.
                report.Notes.Add("ряд " + root + ": отказ базы — " + error.Message);
            }

            lock (Gate)
            {
                ChainCache[root] = branch;
            }

            return branch;
        }

        /// <summary>
        /// {nucid → ГЛУБИНА от корня по `decay_chain`}: сам корень 0, его
        /// дочерние 1, их дочерние 2 и так далее. Кратчайший путь, потому что
        /// обход в ширину, — а у ветвящегося ряда (212BI даёт и 208TL, и
        /// 212PO) путей до одного члена несколько.
        ///
        /// ⚠ Заведено ради `S65` и ТОЛЬКО ради порядка: доли ветвления обходом
        /// в ширину считать нельзя (`S62`, ответ выходил на порядки неверным),
        /// и здесь они не считаются вовсе. Здесь нужен ПОРЯДОК членов —
        /// «кто выше по ряду», — а он у обхода в ширину как раз верен.
        ///
        /// Рёбра берутся тем же запросом и тем же правилом `l_seqno`, что у
        /// <see cref="ChainBranches"/>: второе соглашение о том, что считать
        /// ребром ряда, развело бы порядок с составом.
        /// </summary>
        internal static Dictionary<string, int> ChainDepths(string root, Report report)
        {
            lock (Gate)
            {
                Dictionary<string, int> cached;
                if (DepthCache.TryGetValue(root, out cached))
                {
                    return cached;
                }
            }

            var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var order = new List<string> { root };
                depth[root] = 0;
                using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select daughter_nucid, perc from decay_chain d"
                        + " where nucid = $n and perc not null"
                        + " and l_seqno = (select min(l_seqno) from decay_chain x"
                        + "                where x.nucid = d.nucid"
                        + "                  and x.daughter_nucid = d.daughter_nucid"
                        + "                  and x.dec_type = d.dec_type)";
                    command.Parameters.AddWithValue("$n", root);
                    for (int i = 0; i < order.Count && order.Count <= MaxChainNodes; i++)
                    {
                        string current = order[i];
                        command.Parameters["$n"].Value = current;
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string daughter = reader.IsDBNull(0) ? null : reader.GetString(0);
                                if (string.IsNullOrEmpty(daughter)
                                    || string.Equals(daughter, current, StringComparison.OrdinalIgnoreCase))
                                {
                                    // 238U несёт самопетлю на l_seqno-119.
                                    continue;
                                }

                                double percent;
                                if (!TryNumber(reader, 1, out percent) || !(percent > 0.0))
                                {
                                    continue;
                                }

                                if (!depth.ContainsKey(daughter))
                                {
                                    depth[daughter] = depth[current] + 1;
                                    order.Add(daughter);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                report.Notes.Add("ряд " + root + ": отказ базы при обходе глубин — " + error.Message);
            }

            lock (Gate)
            {
                DepthCache[root] = depth;
            }

            return depth;
        }

        /// <summary>Сколько членов ряда открывать, зажим против поставки-кольца.</summary>
        const int MaxChainNodes = 128;

        /// <summary>
        /// Сколько проходов релаксации отводится ряду. За проход вклад
        /// продвигается на одно ребро; самый длинный из наших рядов (238U) —
        /// два десятка звеньев, так что запас здесь десятикратный.
        /// </summary>
        const int MaxChainPasses = 256;

        /// <summary>
        /// Порог сходимости долей. Он на три порядка ниже `MinChainBranch`
        /// (1e-3), то есть заведомо не влияет на то, кто в ряд попадёт.
        /// </summary>
        const double ChainConverged = 1.0e-12;

        /// <summary>
        /// Линии распада нуклида: {энергия, выход % на распад ЭТОГО нуклида}.
        /// Типы `G` и `X`; K-серия по правилу <see cref="KSeriesRule"/> — ОДНО
        /// правило на весь проект, общее с <see cref="CascadeAtomicData"/>
        /// (двух соглашений о Kβ здесь быть не должно: разойдясь в них,
        /// библиотека и суммирователь совпадений разойдутся в составе пробы
        /// при одинаковых с виду числах, `T50`). L-серия — подробными
        /// строками, если они есть, иначе обобщённой.
        /// </summary>
        /// ⚠ Открыт наружу РАДИ ЧИТАТЕЛЯ (`S89`): два места в проекте читают
        /// `decay_radiations` с разными зажимами, и «оба обязаны давать один
        /// I_K» — утверждение, которое надо проверять прогоном, а не глазами.
        /// Проверяет `DecayReadersProbe`; внутри приложения зовут его только
        /// отсюда.
        public static List<double[]> DecayLines(string nucid, Report report)
        {
            lock (Gate)
            {
                List<double[]> cached;
                if (LineCache.TryGetValue(nucid, out cached))
                {
                    return cached;
                }
            }

            var gamma = new List<double[]>();
            var kAlpha = new List<double[]>();
            var kBetaSplit = new List<double[]>();
            var kBetaSplitSeries = new HashSet<string>(StringComparer.Ordinal);
            var kBetaTotal = new List<double[]>();
            var lLumped = new List<double[]>();
            var lDetailed = new List<double[]>();

            try
            {
                using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
                using (SqliteCommand command = connection.CreateCommand())
                {
                    // ⚠ Тот же зажим по `parent_l_seqno`, что и у ряда, и по той
                    // же причине: Pa-234m1 несёт линию 1001.03 кэВ на уровне 2, а
                    // не на нуле, поэтому «= 0» здесь потеряло бы её целиком.
                    // (`S89`) Само правило вынесено в `DecayParentRule` — двух
                    // соглашений о том, что такое «родитель», в проекте быть не
                    // должно, ровно как и о K-серии.
                    command.CommandText =
                        "select type_a, type_c, energy_num, intensity_num from decay_radiations"
                        + " where parent_nucid = $n and type_a in ('G', 'X')"
                        + " and energy_num not null and intensity_num > 0"
                        + DecayParentRule.LevelClause;
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string kind = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            string series = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                            double energy, intensity;
                            if (!TryNumber(reader, 2, out energy) || !TryNumber(reader, 3, out intensity)
                                || !(energy > 0.0) || !(intensity > 0.0))
                            {
                                continue;
                            }

                            var line = new[] { energy, intensity };
                            if (kind == "G")
                            {
                                gamma.Add(line);
                            }
                            else if (series.Length == 0 || series[0] != 'K')
                            {
                                if (series.Length == 1 && series[0] == 'L')
                                {
                                    lLumped.Add(line);
                                }
                                else if (series.Length > 1 && series[0] == 'L')
                                {
                                    lDetailed.Add(line);
                                }
                            }
                            else if (KSeriesRule.IsBetaTotal(series))
                            {
                                kBetaTotal.Add(line);
                            }
                            else if (KSeriesRule.IsBetaSplit(series))
                            {
                                kBetaSplit.Add(line);
                                kBetaSplitSeries.Add(series);
                            }
                            else
                            {
                                kAlpha.Add(line);
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                report.Notes.Add("линии " + nucid + ": отказ базы — " + error.Message);
            }

            var lines = new List<double[]>();
            lines.AddRange(gamma);
            lines.AddRange(kAlpha);

            // Kβ: итог `KB`, если он есть, иначе разложение — но там, где
            // разложение ПОЛНОЕ, берётся именно оно: то же число двумя
            // энергиями вместо одной усреднённой. Правило и его измерение —
            // `KSeriesRule` (`T50`); прежнее «разложение, если оно есть»
            // занижало Kβ у 350 наборов.
            lines.AddRange(KSeriesRule.Beta(kBetaSplit, kBetaTotal, kBetaSplitSeries.Count));

            // L-серия: та же развилка. Подробных строк в базе всего у трёх
            // нуклидов (225RA, 225RN, 229TH), и ни один в корпусе не снят, —
            // но правило дешевле проверки «а вдруг».
            lines.AddRange(lDetailed.Count > 0 ? lDetailed : lLumped);

            lines.Sort((a, b) => a[0].CompareTo(b[0]));
            lock (Gate)
            {
                LineCache[nucid] = lines;
            }

            return lines;
        }

        // ------------------------------------------------------------------
        // Атомные образы: рентген пробы, кристалла, защиты и пики вылета
        // ------------------------------------------------------------------

        /// <summary>
        /// Флуоресценция пробы, кристалла и защиты плюс вылет K-рентгена
        /// кристалла. Всё — мешающими образами со свободной амплитудой: за
        /// этими линиями нет активности, и в «пирог» долей они не входят.
        /// </summary>
        static void AddAtomic(FsaSampleSpec spec, List<FsaComponent> result,
                              HashSet<string> declared, Report report)
        {
            var seen = new HashSet<int>();
            AddFluorescence(spec, spec.SampleElements, "проба", result, report, seen);
            AddFluorescence(spec, spec.ShieldElements, "защита", result, report, seen);
            AddFluorescence(spec, spec.CrystalElements, "кристалл", result, report, seen);
            AddEscape(spec, result, declared, report);
        }

        static void AddFluorescence(FsaSampleSpec spec, List<int> elements, string what,
                                    List<FsaComponent> result, Report report, HashSet<int> seen)
        {
            foreach (int z in elements)
            {
                if (z <= 0 || !seen.Add(z))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(z);
                if (fluorescence == null || fluorescence.LineKev == null)
                {
                    report.Notes.Add("нет K-серии для Z=" + z.ToString(CultureInfo.InvariantCulture)
                                     + " (" + what + ")");
                    continue;
                }

                // ⛔ L-серия сюда НЕ идёт, и это не забывчивость: в
                // `matdb.xray_fluorescence` её нет вовсе — таблица держит только
                // K-край, Kα1, Kα2 и Kβ. Собрать L пришлось бы из
                // `eadl_radiative`, считая веса внутри серии самому; решением
                // Amber 18.08.2026 в `S56` это не входит.
                var component = new FsaComponent("Xray-" + MaterialDatabase.SymbolOf(z),
                                                 FsaComponentKind.Nuisance);
                for (int i = 0; i < fluorescence.LineKev.Length; i++)
                {
                    // S98: и здесь снизу режет пол библиотеки. Это НЕ мелочь:
                    // при `Min_Range` = 30 кэВ у иода кристалла выбрасывались
                    // ОБЕ линии Kα (28.317 и 28.612 кэВ, вместе 81.3 % веса
                    // K-серии), и образ `Xray-I` состоял из одной Kβ 32.68.
                    // Мерено 25.08.2026: так было во ВСЕХ 81 спектре понятной
                    // части корпуса. Образ, у которого выброшена главная линия,
                    // не «неполный» — он стоит не там, и амплитуду забирает
                    // чужую.
                    double energy = fluorescence.LineKev[i];
                    double weight = fluorescence.LineWeight[i];
                    if (!(energy > 0.0) || !(weight > 0.0)
                        || energy < spec.LineFloorKev || energy > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    // Вес — доля ВНУТРИ K-серии, а не выход на распад: у
                    // характеристического рентгена выхода на распад не
                    // существует (атом светит, когда в K появилась дырка, а
                    // сколько их — дело геометрии и спектра возбуждения). Ровно
                    // поэтому образ и мешающий.
                    AddLine(component, component.Name, energy, 100.0 * weight);
                }

                if (component.Lines.Count > 0)
                {
                    result.Add(component);
                    report.Lines += component.Lines.Count;
                }
            }
        }

        /// <summary>
        /// Пики вылета K-рентгена кристалла: линия E порождает вылет на
        /// E − E(Kα) того элемента кристалла, чей K-край она перешла.
        ///
        /// ⚠ ЭТО ПЕРВОЕ ПРИБЛИЖЕНИЕ, и называть его надо приближением. Честно
        /// вылет считает матрица отклика — розыгрышем переноса, с геометрией
        /// кристалла и глубиной поглощения. Здесь же вес линии берётся как
        ///
        ///     I(родителя) · f_фото(E) · доля K · ω_K,
        ///
        /// то есть без геометрической части («какая доля рождённых квантов
        /// вышла наружу»), которая от энергии почти не зависит и потому уходит
        /// в общую свободную амплитуду образа. Все три множителя — из `matdb`
        /// (`xcom_cross_sections`, `epics_photo_subshell`, `xray_fluorescence`),
        /// выдуманного в них нет.
        ///
        /// ⛔ На спектрах С МАТРИЦЕЙ этот образ спорит с ней за одни и те же
        /// отсчёты — то самое «второе счётоведение», ради прекращения которого
        /// заведён гейт `S47` (<see cref="FsaAnalyzer.EscapeGate"/>). Гейт судит
        /// по имени и снимает только `SE-2614`/`DE-2614`, поэтому здешние
        /// `Esc-*` он не тронет. Так решено 18.08.2026 (Amber, «клади везде»):
        /// образ нужен как опора кросс-проверки матрицы (`S60`). Цена снимается
        /// ключом `AtomicXray`, а не догадкой.
        /// </summary>
        static void AddEscape(FsaSampleSpec spec, List<FsaComponent> result,
                              HashSet<string> declared, Report report)
        {
            if (spec.CrystalElements.Count == 0)
            {
                return;
            }

            // Родительские линии берутся из уже собранного состава распада: это
            // ровно те кванты, которые в кристалл прилетят.
            var parents = new List<double[]>();
            foreach (FsaComponent source in result)
            {
                // Мешающие образы отсекаются по виду, нуклидные — по имени.
                // Ряд, связанный равновесием, имеет вид `Chain`, и проверять
                // «ровно `Single`» нельзя: объявленный ториевый источник ушёл
                // бы из родителей целиком.
                if (source.Kind == FsaComponentKind.Nuisance
                    || !declared.Contains(source.Name))
                {
                    continue;
                }

                foreach (FsaLine line in source.Lines)
                {
                    if (line.Energy <= spec.EscapeParentMaxKev && line.Intensity > 0.0)
                    {
                        parents.Add(new[] { line.Energy, line.Intensity });
                    }
                }
            }

            if (parents.Count == 0)
            {
                return;
            }

            // ⛔ ОБРАЗ ВЫЛЕТА У КРИСТАЛЛА ОДИН (`S84`, решение Amber 19.08.2026),
            // а не по одному на элемент. Прежде их было столько, сколько
            // элементов, и каждому доставалась СВОЯ свободная амплитуда — а
            // различить их данные не могут: Kα цезия 30.97 и иода 28.612 кэВ
            // разводят гребёнки на 2.4 кэВ при ПШПВ прибора в десятки, и после
            // правки `S80` обе сжались в одну и ту же полосу 44…61 кэВ. Измерено
            // 19.08.2026: на одном и том же чароите выживал то `Esc-Cs`, то
            // `Esc-I`, смотря по мелочам обстановки, — то есть выбор между ними
            // не нёс физического смысла вовсе.
            //
            // Довод тот же, что у связки ряда: данные различают образ целиком, а
            // не его половину. Соотношение членов теперь задаёт ВЕЩЕСТВО —
            // массовые доли элементов кристалла, — и амплитуда у образа одна.
            CrystalMix mix = CrystalMix.Of(spec, report);
            if (mix == null)
            {
                return;
            }

            var component = new FsaComponent("Esc-" + mix.Name, FsaComponentKind.Nuisance);
            foreach (CrystalMix.Part part in mix.Parts)
            {
                MaterialDatabase.Fluorescence fluorescence = part.Fluorescence;
                MaterialDatabase.PhotoShellModel shells = part.Shells;
                MaterialDatabase.Element element = part.Element;
                double omega = fluorescence.Omega(true);
                double kAlpha = fluorescence.LineKev[0];

                // Ослабление СВОЕГО рентгена в веществе кристалла — одно на все
                // линии этого члена, поэтому считается здесь, а не в цикле.
                double muXray = mix.Attenuation(kAlpha);
                foreach (double[] parent in parents)
                {
                    // Ниже K-края дырки в K-оболочке не бывает, и вылета нет.
                    if (parent[0] <= fluorescence.KEdgeKev)
                    {
                        continue;
                    }

                    // S98: пол тот же, что у остальных образов. Одна граница на
                    // все — иначе вылет существовал бы там, где родительская
                    // линия уже нет, и наоборот.
                    double energy = parent[0] - kAlpha;
                    if (energy < spec.LineFloorKev || energy > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    // Числитель — K-поглощение ИМЕННО В ЭТОМ элементе, со своей
                    // массовой долей; знаменатель — полное ослабление ВЕЩЕСТВА
                    // кристалла. Отсюда и берётся закреплённое соотношение
                    // членов образа: у элемента, которого в кристалле вдвое
                    // меньше, вклад вдвое меньше, и фиту тут решать нечего.
                    double photo = part.Fraction
                                   * MaterialDatabase.Interpolate(element.EnergyKev,
                                                                  element.Channels[2], parent[0]);
                    double total = mix.Attenuation(parent[0]);
                    if (!(photo > 0.0) || !(total > 0.0))
                    {
                        continue;
                    }

                    double kShare = shells != null ? shells.KFraction(parent[0])
                                                   : fluorescence.KFraction;

                    // ⛔ ЧЕТВЁРТЫЙ множитель — вероятность рентгену ВЫЙТИ из
                    // кристалла (`S80`). Без него в весе стояла вероятность
                    // родить K-дырку и только она, а вылет тем самым считался
                    // одинаково возможным с любой глубины. Отсюда и брался
                    // перекос, найденный по печати линий 19.08.2026: на
                    // `Th232_29.07.2022.xml` сильнейшей линией образа выходила
                    // 210.020 кэВ (вылет из 238.632 кэВ Pb-212) с весом 18.05
                    // против 11.73 у 48.496 кэВ — то есть гребёнка была тяжелее
                    // ВВЕРХУ шкалы, где вылета быть не может вовсе.
                    double weight = parent[1] * (photo / total) * kShare * omega
                                    * EscapeFraction(muXray, total);
                    if (weight > 0.0)
                    {
                        // ⚠ Линия помечается СВОИМ членом, а не именем образа, и
                        // это не украшение. Отсев дубля в `AddLine` сверяет пару
                        // «метка + энергия», а вылеты разных элементов
                        // расходятся ровно на разницу их Kα (у CsI 2.36 кэВ) —
                        // значит две родительские линии, отстоящие на те же
                        // 2.36 кэВ, дают ОДНУ энергию вылета. У ториевого ряда
                        // такая пара есть прямо в середине: 238.632 Pb-212 и
                        // 240.986 Ra-224 дают через иод и цезий 210.020 и
                        // 210.013 кэВ. С общей меткой вторая пропала бы молча.
                        AddLine(component, part.Tag, energy, weight);
                    }
                }
            }

            Prune(component, spec.EscapeMinRelativeWeight);
            if (component.Lines.Count > 0)
            {
                result.Add(component);
                report.Lines += component.Lines.Count;
            }
        }

        /// <summary>
        /// Вещество кристалла для образа вылета: члены с их МАССОВЫМИ долями,
        /// полное ослабление смеси и короткое имя (`S84`, 19.08.2026).
        ///
        /// Заведено потому, что образ вылета у кристалла ОДИН, а соотношение его
        /// членов задаётся веществом, а не фитом. Всё, что для этого нужно, уже
        /// лежит в геометрии — <c>GeometryMaterial.Fractions</c>; раньше
        /// <see cref="HeavyElementsOf"/> оставляла от неё одни номера элементов.
        /// </summary>
        sealed class CrystalMix
        {
            internal sealed class Part
            {
                public string Tag;
                public double Fraction;
                public MaterialDatabase.Element Element;
                public MaterialDatabase.Fluorescence Fluorescence;
                public MaterialDatabase.PhotoShellModel Shells;
            }

            public readonly List<Part> Parts = new List<Part>();

            public string Name = "";

            /// <summary>
            /// Полное массовое ослабление СМЕСИ, см²/г: Σ w_i·(μ/ρ)_i. Плотность
            /// не нужна — в вес линии оно входит только отношением.
            ///
            /// ⚠ Считается по тем же членам, что и вылет, то есть по элементам,
            /// прошедшим отбор <see cref="HeavyElementsOf"/>. Лёгкая примесь
            /// (активатор, натрий ниже порога доли) в сумму не попадает, и на
            /// её долю ослабление занижено. Для CsI отбор берёт оба элемента и
            /// сумма полна.
            /// </summary>
            public double Attenuation(double kev)
            {
                double mu = 0.0;
                for (int k = 0; k < this.Parts.Count; k++)
                {
                    Part part = this.Parts[k];
                    mu += part.Fraction * MaterialDatabase.Interpolate(
                        part.Element.EnergyKev, part.Element.Total, kev);
                }

                return mu;
            }

            /// <summary>
            /// Собрать смесь по объявленным элементам кристалла. Доли берутся из
            /// <see cref="FsaSampleSpec.CrystalFractions"/> и нормируются на
            /// сумму ВОШЕДШИХ; доли нет — считаются равными, и об этом говорится
            /// в отчёте, а не молчится. null — строить нечего.
            /// </summary>
            public static CrystalMix Of(FsaSampleSpec spec, Report report)
            {
                var mix = new CrystalMix();
                var done = new HashSet<int>();
                double sum = 0.0;
                bool declared = spec.CrystalFractions.Count > 0;
                foreach (int z in spec.CrystalElements)
                {
                    if (z <= 0 || !done.Add(z))
                    {
                        continue;
                    }

                    MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(z);
                    MaterialDatabase.Element element;
                    if (fluorescence == null || !MaterialDatabase.TryGet(z, out element))
                    {
                        report.Notes.Add("вылет: нет данных для Z="
                                         + z.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }

                    if (!(fluorescence.Omega(true) > 0.0)
                        || fluorescence.LineKev == null || fluorescence.LineKev.Length == 0
                        || !(fluorescence.LineKev[0] > 0.0))
                    {
                        continue;
                    }

                    // ⚠ Доли объявлены — значит объявлен и СОСТАВ: элемента, в
                    // них не названного, в кристалле нет, и брать его с долей
                    // «по умолчанию» нельзя. Так бывает, когда список элементов
                    // пополняется из второго источника (у пробы — `materials.csv`
                    // поверх геометрии), и незваный элемент с долей 1.0 забрал бы
                    // образ себе.
                    double fraction;
                    bool known = spec.CrystalFractions.TryGetValue(z, out fraction)
                                 && fraction > 0.0;
                    if (!known)
                    {
                        if (declared)
                        {
                            report.Notes.Add("вылет: Z="
                                             + z.ToString(CultureInfo.InvariantCulture)
                                             + " не назван в составе кристалла, пропущен");
                            continue;
                        }

                        fraction = 1.0;
                    }

                    sum += fraction;
                    mix.Parts.Add(new Part
                    {
                        Tag = "Esc-" + MaterialDatabase.SymbolOf(z),
                        Fraction = fraction,
                        Element = element,
                        Fluorescence = fluorescence,
                        Shells = MaterialDatabase.PhotoShellOf(z)
                    });
                }

                if (mix.Parts.Count == 0 || !(sum > 0.0))
                {
                    return null;
                }

                if (!declared)
                {
                    report.Notes.Add("вылет: долей кристалла нет, считаны равными");
                }

                var order = new List<Part>(mix.Parts);
                order.Sort((x, y) => y.Fraction != x.Fraction
                    ? y.Fraction.CompareTo(x.Fraction)
                    : string.CompareOrdinal(x.Tag, y.Tag));

                var name = new StringBuilder();
                foreach (Part part in order)
                {
                    part.Fraction /= sum;
                    name.Append(part.Tag.Substring(4));
                }

                mix.Name = string.IsNullOrEmpty(spec.CrystalName)
                    ? name.ToString() : spec.CrystalName;
                return mix;
            }
        }

        /// <summary>
        /// Доля K-рентгена, которая УСПЕВАЕТ ВЫЙТИ из кристалла, — четвёртый
        /// множитель веса линии вылета (`S80`, 19.08.2026).
        ///
        /// Считается точно, а не поправкой. Квант первый раз взаимодействует на
        /// глубине z с плотностью μ_E·exp(−μ_E·z); рождённый там рентген летит
        /// изотропно и уходит назад через переднюю грань, если пройдёт z/cosθ
        /// без поглощения. Интеграл по глубине и по задней полусфере берётся в
        /// замкнутом виде и зависит ТОЛЬКО от отношения ослаблений
        /// a = μ(рентгена) / μ(кванта):
        ///
        ///     f = ½ · [ 1 − a · ln(1 + 1/a) ]
        ///
        /// Отношение берётся от МАССОВЫХ коэффициентов, поэтому плотность в него
        /// не входит и знать её не нужно.
        ///
        /// Пределы читаются физикой. Сразу над K-краем μ_E огромно, квант
        /// садится в первые доли миллиметра, a → 0 и f → ½: наружу уходит
        /// ровно та половина рентгена, что полетела назад. Вверху шкалы μ_E
        /// мало, a велико, f → 1/(4a) → 0: квант поглощается в сантиметрах от
        /// поверхности, а пробег 28.6-кэВ рентгена иода в CsI ≈ 0.25 мм, и
        /// выйти оттуда он не может. На CsI это даёт f ≈ 0.10 при 78 кэВ и
        /// ≈ 0.011 при 238.6 кэВ — то самое подавление в десять раз, которого
        /// в весе не было.
        ///
        /// ⚠ **Кристалл считается полубесконечным, и вылет назад — весь вылет.**
        /// Там, где вылет вообще заметен, это верно с запасом: при 78 кэВ квант
        /// садится в первый миллиметр, до задней грани ему далеко. Наверху
        /// шкалы тонкий кристалл дал бы ещё и вылет вперёд, но там f и без того
        /// пренебрежимо мала. Толщины у <see cref="FsaSampleSpec"/> нет вовсе,
        /// и заводить её ради поправки к пренебрежимому не стоит.
        ///
        /// ⚠ Оба ослабления берутся у ВЕЩЕСТВА кристалла, а не у элемента
        /// (`S83`, снято 19.08.2026 вместе с `S84`): массовые доли лежат в
        /// геометрии, и смесь считается прямо по ним. Для CsI разница была
        /// невелика (Z 55 и 53 рядом), для NaI лёгкий натрий ослабление
        /// разбавляет, и элементное отношение было смещено.
        /// </summary>
        static double EscapeFraction(double totalAtXray, double totalAtParent)
        {
            if (!(totalAtXray > 0.0) || !(totalAtParent > 0.0))
            {
                return 0.0;
            }

            double a = totalAtXray / totalAtParent;

            // ⚠ При большом a разность 1 − a·ln(1+1/a) — вычитание близких
            // чисел, и на краю двойной точности от неё ничего не осталось бы.
            // Ряд там сходится быстро: a·ln(1+1/a) = 1 − 1/(2a) + 1/(3a²) − …
            double f = a > 1.0e4
                ? 0.5 * (1.0 / (2.0 * a) - 1.0 / (3.0 * a * a))
                : 0.5 * (1.0 - a * Math.Log(1.0 + 1.0 / a));
            return f > 0.0 ? f : 0.0;
        }

        /// <summary>
        /// Выбросить из образа линии слабее доли <paramref name="relative"/> от
        /// его же сильнейшей. Отношение берётся ВНУТРИ образа, а не по всей
        /// библиотеке: амплитуда у образа своя и свободная, так что абсолютный
        /// порог для него не определён вовсе.
        /// </summary>
        static void Prune(FsaComponent component, double relative)
        {
            if (!(relative > 0.0) || component.Lines.Count == 0)
            {
                return;
            }

            double top = 0.0;
            foreach (FsaLine line in component.Lines)
            {
                if (line.Intensity > top)
                {
                    top = line.Intensity;
                }
            }

            double floor = top * relative;
            var kept = new List<FsaLine>(component.Lines.Count);
            foreach (FsaLine line in component.Lines)
            {
                if (line.Intensity >= floor)
                {
                    kept.Add(line);
                }
            }

            component.Lines.Clear();
            component.Lines.AddRange(kept);
        }

        /// <summary>
        /// Аннигиляционная линия 511 кэВ: рождение пар жёсткими квантами в
        /// защите и обвязке плюс β⁺-примеси. Нуклиду она не принадлежит, доля
        /// зависит от домика и геометрии — поэтому свободный мешающий образ.
        /// Правило то же, что у <see cref="FsaLibrary"/>: без образа NNLS вешает
        /// 511 на ближайшую линию соседа.
        /// </summary>
        static void AddAnnihilation(List<FsaComponent> result, Report report)
        {
            if (result.Count == 0)
            {
                return;
            }

            var component = new FsaComponent("Ann-511", FsaComponentKind.Nuisance);
            component.Lines.Add(new FsaLine("Ann-511", 511.0, 100.0));
            result.Add(component);
            report.Lines++;
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        /// <summary>
        /// Та же база, но в виде, который понимает ПОИСК ПИКОВ
        /// (<see cref="PeakDetector.DetectPeak"/>).
        ///
        /// Постулат `S56` говорит «своя база ПИКОВ», и подпись — вторая его
        /// половина. Пока поиск подписывает из общего поставочного списка, на
        /// спектре лютеция получаются подписи «Pu-238», «Eu-152», «I-131» —
        /// измерено 18.08.2026 на `ASN16_Lu176`, где так подписаны четыре пика
        /// из девяти. В разложение они теперь не попадают (состав задаёт проба),
        /// но человек читает именно их, и кривая эффективности строится по
        /// площади ПОДПИСАННОГО пика.
        ///
        /// ⚠ Подавать вместе с <c>nuclideSet = null</c>: набор здесь ни при чём,
        /// список уже свой, а <see cref="NuclideSet.HideUnknownPeaks"/> у чужого
        /// набора вычеркнул бы неподписанные пики и увёл счёт.
        /// </summary>
        public static List<NuclideDefinition> AsDefinitions(List<FsaComponent> library)
        {
            var definitions = new List<NuclideDefinition>();
            if (library == null)
            {
                return definitions;
            }

            foreach (FsaComponent component in library)
            {
                foreach (FsaLine line in component.Lines)
                {
                    var definition = new NuclideDefinition();

                    // ⚠ У колонки ряда (равновесие, `S70`) имя компонента —
                    // корень, а линии принадлежат РАЗНЫМ его членам. Пик
                    // подписывается тем, кто эту линию излучил: подписать
                    // 583.19 кэВ «Th-232» вместо «Tl-208» значило бы отнять у
                    // таблицы пиков то единственное, чего связка не отменяет, —
                    // знание, кто в ряду светит. У всех прочих образов нуклид
                    // линии и имя компонента совпадают, и правило для них
                    // ничего не меняет.
                    definition.Name = component.Kind == FsaComponentKind.Chain
                                      && !string.IsNullOrEmpty(line.Nuclide)
                        ? line.Nuclide : component.Name;
                    definition.Energy = line.Energy;
                    definition.Intencity = line.Intensity;
                    definition.Visible = true;
                    definitions.Add(definition);
                }
            }

            return definitions;
        }

        /// <summary>
        /// Элементы вещества, чей K-рентген вообще способен дать линию в этом
        /// спектре: массовая доля не ниже <paramref name="minFraction"/>, а
        /// Kα1 — внутри рабочего диапазона.
        ///
        /// Оба отбора нужны, и оба дешёвые. Без порога по доле в состав пробы
        /// попадает всё, включая примеси; без порога по энергии — кальций
        /// оникса (Kα 3.69 кэВ) и калий KCl (3.31), которых ни один прибор
        /// корпуса не видит.
        ///
        /// ⛔ **Прежнее обоснование нижней границы было неверным и снято
        /// 25.08.2026 (`S98`):** «образ из линий вне окна ФИТА — вырожденный
        /// столбец в NNLS». Окна фита нет — фит идёт с нулевого канала
        /// (<see cref="FsaBandMode"/>), и линия ниже `Min_Range` вырожденной не
        /// была бы. Отбор остаётся нужным по другой причине — прибор таких
        /// энергий не регистрирует, — и границей ему служит
        /// <see cref="FsaSampleSpec.LineFloorKev"/>, а не диапазон поиска пиков.
        ///
        /// ⚠ Вещество из файла спектра приходит ОДНИМ ИМЕНЕМ, без состава:
        /// `GeometryMaterial.Fractions` в XML пуст, а «Cesium iodide» лежит
        /// строкой. Поэтому состав добирается из библиотеки веществ по имени, и
        /// молчаливого отказа здесь быть не должно — вещество, которого в
        /// библиотеке нет, возвращает пустой список, а не «ничего страшного».
        ///
        /// ⛔ **Звать надо ЭТУ перегрузку, со спеком.** Окно она берёт у полосы
        /// БИБЛИОТЕКИ, и разница считается: у кадмиевой пробы Kα 23.17 кэВ
        /// ниже `Min_Range` = 30, и вызов с диапазоном поиска пиков выбрасывал
        /// элемент ЦЕЛИКОМ — вместе с образом, который новый пол разрешает.
        /// </summary>
        public static List<int> HeavyElementsOf(GeometryMaterial material,
                                                double minFraction,
                                                FsaSampleSpec spec)
        {
            return spec == null
                ? new List<int>()
                : HeavyElementsOf(material, minFraction, spec.LineFloorKev, spec.MaxEnergyKev);
        }

        /// <summary>
        /// То же, границами явно. Оставлено ради вызывающих, у которых спека
        /// нет; у кого он есть — перегрузка выше.
        /// </summary>
        public static List<int> HeavyElementsOf(GeometryMaterial material,
                                                double minFraction,
                                                double minKAlphaKev,
                                                double maxKAlphaKev)
        {
            var elements = new List<int>();
            if (material == null)
            {
                return elements;
            }

            Dictionary<int, double> fractions = material.Fractions;
            if (fractions.Count == 0 && !string.IsNullOrEmpty(material.Name))
            {
                GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(material.Name);
                if (entry != null)
                {
                    fractions = GeometryMaterialLibrary.Make(entry, material.Density).Fractions;
                }
            }

            foreach (KeyValuePair<int, double> pair in fractions)
            {
                if (pair.Value < minFraction)
                {
                    continue;
                }

                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(pair.Key);
                if (fluorescence == null || fluorescence.LineKev == null
                    || fluorescence.LineKev.Length == 0)
                {
                    continue;
                }

                double kAlpha = fluorescence.LineKev[0];
                if (kAlpha >= minKAlphaKev && kAlpha <= maxKAlphaKev)
                {
                    elements.Add(pair.Key);
                }
            }

            elements.Sort();
            return elements;
        }

        /// <summary>
        /// Кристалл — в спецификацию целиком: элементы, их МАССОВЫЕ доли и
        /// короткое имя вещества (`S84`, 19.08.2026).
        ///
        /// Заведено взамен голого <see cref="HeavyElementsOf"/> у кристалла:
        /// образ вылета один на вещество, и соотношение его членов задают доли,
        /// которые лежали тут же и выбрасывались. Прочие вещества (проба,
        /// защита) долей не требуют — у них каждый элемент светит сам за себя и
        /// получает свой образ со своей амплитудой.
        ///
        /// ⚠ У КРИСТАЛЛА окна по Kα нет, и это не оплошность: элемент кристалла
        /// не только светит сам, но и уносит энергию вылетом, а пик вылета стоит
        /// на E − Kα, то есть внутри окна даже когда сама Kα ниже его низа
        /// (измерено 18.08.2026 на ASN16).
        /// </summary>
        public static void DescribeCrystal(FsaSampleSpec spec, GeometryMaterial crystal,
                                           double minFraction, string shortName)
        {
            spec.CrystalElements.AddRange(HeavyElementsOf(crystal, minFraction, 0.0, double.MaxValue));
            if (!string.IsNullOrEmpty(shortName))
            {
                // Активатор в имени вещества не нужен: «CsI:Tl» → «CsI».
                int mark = shortName.IndexOf(':');
                spec.CrystalName = mark > 0 ? shortName.Substring(0, mark) : shortName;
            }

            if (crystal == null)
            {
                return;
            }

            Dictionary<int, double> fractions = crystal.Fractions;
            if (fractions.Count == 0 && !string.IsNullOrEmpty(crystal.Name))
            {
                GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(crystal.Name);
                if (entry != null)
                {
                    fractions = GeometryMaterialLibrary.Make(entry, crystal.Density).Fractions;
                }
            }

            foreach (KeyValuePair<int, double> pair in fractions)
            {
                spec.CrystalFractions[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// Возбуждённое состояние: у `nucid` есть метка состояния строчными
        /// буквами («234PAm1», «108AGm», «105PDe»).
        ///
        /// ⚠ Признак — РЕГИСТР, а не буква. Символ элемента записан заглавными
        /// целиком, поэтому «241AM» (америций) изомером не является, а «108AGm»
        /// является. На этом уже поскользнулись 18.08.2026 — см.
        /// <see cref="PrettyName"/>.
        /// </summary>
        public static bool IsIsomer(string nucid)
        {
            int mass;
            string symbol, state;
            return CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state)
                   && state.Length > 0;
        }

        /// <summary>
        /// Долю ветвления нуклида — в накопитель, БОЛЬШУЮ из встреченных.
        /// Вместе с долей запоминается и ХОЗЯИН — тот корень, от которого эта
        /// доля посчитана: по нему связываются члены при
        /// <see cref="FsaSampleSpec.Equilibrium"/>.
        ///
        /// ⚠ Тот же нуклид приходит из двух рядов сразу (Ra-226 объявлен сам по
        /// себе и лежит внутри U-238; Th-228 — сам по себе и внутри Th-232), и
        /// доли у него разные. Побеждает бо́льшая, и хозяином становится её
        /// корень: объявленный САМ нуклид держит долю 1.0, то есть отбирает
        /// себя и своих потомков у объемлющего ряда — ровно то, чего человек и
        /// хотел, объявив его отдельно. При равенстве побеждает ПЕРВЫЙ, поэтому
        /// объявленный ряд не отбирается вездесущим (<see cref="RoomChains"/>).
        /// </summary>
        static void Remember(Dictionary<string, double> branch, Dictionary<string, string> owner,
                             string nucid, double value, string root)
        {
            double have;
            if (branch.TryGetValue(nucid, out have) && have >= value)
            {
                return;
            }

            branch[nucid] = value;
            if (owner != null)
            {
                owner[nucid] = root;
            }
        }

        /// <summary>
        /// Хозяин нуклида: корень ряда, от которого посчитана его доля. Не
        /// записан — сам себе хозяин (так выходит у одиночных нуклидов и у
        /// читателей, которым хозяин не нужен).
        /// </summary>
        static string OwnerOf(Dictionary<string, string> owner, string nucid)
        {
            string root;
            return owner != null && owner.TryGetValue(nucid, out root) ? root : nucid;
        }

        static int GroupCount(Dictionary<string, int> groupSize, string root)
        {
            int count;
            return groupSize.TryGetValue(root, out count) ? count : 1;
        }

        static FsaComponent Take(Dictionary<string, FsaComponent> byName, List<string> order,
                                 string name, FsaComponentKind kind)
        {
            FsaComponent component;
            if (!byName.TryGetValue(name, out component))
            {
                component = new FsaComponent(name, kind);
                byName[name] = component;
                order.Add(name);
            }

            return component;
        }

        /// <summary>
        /// Линия в образ с защитой от дубля. Порог 0.05 кэВ — тот же, что в
        /// <see cref="FsaLibrary"/>: реальных раздельных линий ближе не бывает,
        /// а запись той же линии с иной точностью удвоила бы её вес и уронила
        /// амплитуду образа вдвое.
        /// </summary>
        /// <summary>
        /// Линию — в образ, если такой у ЭТОГО ЖЕ нуклида ещё нет.
        ///
        /// ⚠ Совпадение проверяется по паре «нуклид + энергия», а не по одной
        /// энергии, и это существенно с приходом связки равновесия (`S70`): в
        /// колонке ряда лежат линии НЕСКОЛЬКИХ нуклидов, и две близкие линии
        /// разных членов — это две настоящие линии, обе в спектре есть. Отсев
        /// по одной энергии выбросил бы вторую молча. У всех прочих читателей
        /// (<see cref="AddFluorescence"/>, <see cref="AddEscape"/>, состав без
        /// равновесия) нуклид у линий образа один и тот же, поэтому для них
        /// правило не изменилось ни на волос.
        ///
        /// Отсев нужен затем, зачем и заводился: одна линия бывает записана в
        /// базе дважды — своей строкой и строкой «в цепочке», либо копией с
        /// округлённой энергией, — и в образе она удваивает вес. Порог 0.05 кэВ:
        /// раздельных линий ближе не бывает.
        /// </summary>
        static void AddLine(FsaComponent component, string nuclide, double energy, double intensity)
        {
            foreach (FsaLine line in component.Lines)
            {
                if (Math.Abs(line.Energy - energy) < 0.05
                    && string.Equals(line.Nuclide, nuclide, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            component.Lines.Add(new FsaLine(nuclide, energy, intensity));
        }

        /// <summary>
        /// `nucid` → подпись: «212PB» → «Pb-212», «241AM» → «Am-241»,
        /// «234PAm1» → «Pa-234m», «108AGm» → «Ag-108m».
        ///
        /// ⚠ Хвост изомера пишется БЕЗ номера состояния, и это не небрежность:
        /// именно так изомер назван и в мерке корпуса (`score.py`,
        /// `CHAIN_MEMBERS`: «Pa-234m»), а имя компонента — ключ, по которому
        /// мерка сводит найденное с истиной. `chains.pretty` в питоне на этом
        /// имени спотыкается вовсе и отдаёт «234PAm1» как есть (регистр `m` не
        /// подходит под её шаблон) — здесь это учтено.
        /// </summary>
        public static string PrettyName(string nucid)
        {
            int mass;
            string symbol, state;
            if (!CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state))
            {
                return nucid ?? "";
            }

            // Номер состояния в подпись НЕ идёт: «234PAm1» -> «Pa-234m». Так
            // изомер назван и в мерке корпуса (`score.py`, `CHAIN_MEMBERS`), а
            // имя компонента — ключ, по которому мерка сводит найденное с
            // истиной.
            string letters = "";
            foreach (char c in state)
            {
                if (!char.IsDigit(c))
                {
                    letters += char.ToLowerInvariant(c);
                }
            }

            return char.ToUpperInvariant(symbol[0]) + symbol.Substring(1).ToLowerInvariant()
                   + "-" + mass.ToString(CultureInfo.InvariantCulture) + letters;
        }
        /// <summary>Обратно: «Cs-137» → «137CS»; пусто — разобрать не вышло.</summary>
        public static string NucidOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            string text = name.Trim();
            int dash = text.IndexOf('-');
            if (dash <= 0 || dash + 1 >= text.Length)
            {
                return "";
            }

            string symbol = text.Substring(0, dash);
            string mass = text.Substring(dash + 1);
            string isomer = "";
            while (mass.Length > 0 && char.IsLetter(mass[mass.Length - 1]))
            {
                isomer = char.ToLowerInvariant(mass[mass.Length - 1]) + isomer;
                mass = mass.Substring(0, mass.Length - 1);
            }

            foreach (char c in mass)
            {
                if (c < '0' || c > '9')
                {
                    return "";
                }
            }

            return mass.Length == 0 ? "" : mass + symbol.ToUpperInvariant() + isomer;
        }

        static bool TryNumber(SqliteDataReader reader, int column, out double value)
        {
            value = 0.0;
            if (reader.IsDBNull(column))
            {
                return false;
            }

            // Колонки базы разнородны по типу: `perc` лежит текстом, `energy_num`
            // — числом. Читать текстовую как double бросает, числовую как
            // строку — тоже, поэтому пробуются оба.
            try
            {
                value = reader.GetDouble(column);
                return true;
            }
            catch (InvalidCastException)
            {
            }

            string text = reader.GetValue(column) as string;
            return text != null
                   && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static SqliteConnection OpenRead(string path)
        {
            SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;");
            connection.Open();
            return connection;
        }

        static string NuclideDatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }
    }
}
