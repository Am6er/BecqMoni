using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Каскадное суммирование поверх фотонной матрицы отклика (TODO F1, п. «г»).
    ///
    /// ЗАЧЕМ. Матрица отвечает на вопрос «что оставит в кристалле ОДИН квант
    /// энергии E», и этого достаточно, пока кванты независимы. В каскаде они не
    /// независимы: вместе с квантом линии k из того же распада вылетает партнёр,
    /// и если партнёр тоже что-то оставил в кристалле, событие уезжает из пика k
    /// вверх по шкале. Линейной комбинацией столбцов матрицы это невыразимо —
    /// эффект принадлежит нуклиду и схеме распада, а не фотону. Поэтому он
    /// вносится множителем на площадь пика (CF) и отдельными сумм-пиками, а
    /// матрица остаётся тем, чем была.
    ///
    /// ФОРМУЛЫ — детерминированный путь EFFTRAN, тот же, что в пробе
    /// `CoincCfProbe` (сверен с ион-режимом Geant4: все линии в ≤2.2σ,
    /// Cs-134 1365.2 — 0.8515 против 0.8516):
    ///
    ///     CF(k) = 1 / [ (1 − L_out) + Σ_in / (p_k · ε_p(k)) ]
    ///     L_out = Σ_j P(j|k) · ε_T(E_j)
    ///     Σ_in  = Σ_{(i,j): E_i+E_j ≈ E_k} p_ij · ε_p(i) · ε_p(j) · S_ij
    ///     S_ij  = 1 − Σ_{m∉{i,j}} max(P(m|i), P(m|j)) · ε_T(m)
    ///
    /// ОТКУДА ЭФФЕКТИВНОСТИ. Из САМОЙ матрицы, а не вторым розыгрышем:
    /// ε_p(E) — сумма строки канала <see cref="EfficiencySimulator.ResponseChannel.Peak"/>
    /// (полное поглощение), ε_T(E) — сумма всей строки узла (вероятность
    /// оставить в кристалле хоть что-нибудь). Обе нормированы на квант,
    /// испущенный источником в 4π, — ровно то, что нужно формулам. Второй
    /// розыгрыш дал бы те же числа с другим шумом ГСЧ и стоил бы минуты счёта.
    ///
    /// ЧТО ПОПРАВКА ТРОГАЕТ. Только канал ПИКА. Вынос из пика — чистая потеря,
    /// а континуум одновременно теряет свои события и получает чужие суммы, и
    /// в первом порядке остаётся при своём; красить его тем же множителем
    /// значило бы выдумать потерю, которой нет. Пики вылета (511, K-рентген)
    /// теряют наравне с пиком, но их поправка здесь НЕ применяется — они малы,
    /// а разделять правило на три случая ради этого рано.
    ///
    /// РЕНТГЕН И АННИГИЛЯЦИЯ В ПАРАХ (S27, 18.08.2026). Таблицы SandiaDecay
    /// держат только пары γ-γ, а из распада вылетает и не-гамма: K-рентген
    /// дочернего атома (до 115 % на распад у захватных) и два кванта по
    /// 511 кэВ у β⁺. Они заводятся в те же `Pairs` и `Partners`, что и ядерные
    /// пары, — поэтому весь счёт ниже (CF, сумм-пики, тройные суммы,
    /// сумм-континуум) работает над ними БЕЗ ЕДИНОЙ ПРАВКИ. Откуда берутся
    /// вероятности — см. <see cref="CascadeAtomicData"/>.
    ///
    /// ГЕЙТ ПО ВРЕМЕНИ. Совпадение — это два кванта в ОДНОМ импульсе, а
    /// длительность импульса и есть мёртвое время прибора
    /// (`InputDeviceConfig.DeadTime()`; у AtomSpectra оно прямо считается как
    /// `(rise + fall + 1)/f` и на AS80x80 измерено 1.357 мкс). Кванты,
    /// разделённые долгоживущим уровнем, в один импульс не попадают, и пара
    /// между ними — выдумка. Разводится это начисто: у Hf-176 уровень 88 кэВ
    /// живёт 1.43 нс (совпадение есть), у Ag-109 тот же по энергии уровень —
    /// 39.79 с, а у Ba-137 уровень 661.7 кэВ — 153 с (совпадений нет). Без
    /// гейта модель поставила бы Cd-109 сумму 22 + 88 = 110 кэВ, которой не
    /// бывает. ⚠ Порог НЕ безразличен: 1.04 % уровней `g4_level` лежат в полосе
    /// 1…10 мкс, и среди них Sc-44 146 кэВ (51 мкс) — из-за него рентген Ti-44
    /// не совпадает с его же гаммами 67.9 и 78.3.
    ///
    /// ЧЕГО НЕТ, сознательно (наследуется от пробы):
    ///   * тройных влётов (E_i+E_j+E_m = E_k) — на два порядка мельче;
    ///   * пары 511 + 511 — кванты летят спина к спине, изотропная формула
    ///     завышает их совместное попадание в разы (решение Amber 18.08.2026,
    ///     разобрано в <see cref="CascadeAtomicData.AnnihilationQuanta"/>);
    ///   * L-серии рентгена — у неё своя бухгалтерия вакансий, TODO S58;
    ///   * угловых корреляций (`database/scheme.md`, D-1) — совпадения
    ///     изотропны; геометрическая половина живёт своей строкой, TODO N14;
    ///   * сумм-континуума (полное + частичное поглощение пары): сумм-пик
    ///     ставится только на полное поглощение обоих квантов.
    /// </summary>
    public sealed class FsaCascadeSummer
    {
        /// <summary>Сумм-пик: энергия и площадь в долях на распад родителя.</summary>
        public sealed class SumPeak
        {
            public SumPeak(double energy, double area, string nuclide,
                           double fromKev, double withKev)
                : this(energy, area)
            {
                this.Nuclide = nuclide ?? "";
                this.FromKev = fromKev;
                this.WithKev = withKev;
            }

            /// <summary>Нуклид, чей это каскад; пусто — конструктор без разбора.</summary>
            public string Nuclide { get; private set; }

            /// <summary>Первая линия пары, кэВ.</summary>
            public double FromKev { get; private set; }

            /// <summary>Вторая линия пары, кэВ.</summary>
            public double WithKev { get; private set; }

            /// <summary>
            /// Третий квант каскада, кэВ; 0 — сумма двойная. Тройные суммы
            /// заведены по S19: множитель выживания `S_ij` вычитал события,
            /// у которых третий квант тоже попал в кристалл, и никуда их не
            /// перекладывал — а полное поглощение третьего даёт СВОЙ пик.
            /// </summary>
            public double ThirdKev { get; private set; }

            /// <summary>Сумма трёх, а не двух.</summary>
            public bool IsTriple
            {
                get { return this.ThirdKev > 0.0; }
            }

            public SumPeak(double energy, double area, string nuclide,
                           double fromKev, double withKev, double thirdKev)
                : this(energy, area, nuclide, fromKev, withKev)
            {
                this.ThirdKev = thirdKev;
            }

            public SumPeak(double energy, double area)
            {
                this.Energy = energy;
                this.Area = area;
            }

            public double Energy { get; private set; }

            /// <summary>
            /// Площадь пика полного поглощения СУММЫ, уже с обеими пиковыми
            /// эффективностями внутри: `p_ij · ε_p(i) · ε_p(j) · S_ij`. Второй
            /// раз эффективность к ней применяться не должна.
            /// </summary>
            public double Area { get; private set; }
        }

        /// <summary>
        /// Сумм-КОНТИНУУМ: пара поглощена целиком, а третий квант каскада
        /// оставил ЧАСТЬ своей энергии. Не пик, а сплошной подъём от видимой
        /// суммы пары до неё же плюс энергия третьего — то есть отклик третьего
        /// кванта БЕЗ пикового канала, сдвинутый на сумму пары (S19).
        /// </summary>
        public sealed class SumContinuum
        {
            public SumContinuum(double shiftKev, double thirdKev, double weight, string nuclide)
            {
                this.ShiftKev = shiftKev;
                this.ThirdKev = thirdKev;
                this.Weight = weight;
                this.Nuclide = nuclide ?? "";
            }

            /// <summary>Видимая сумма пары, на которую сдвинут отклик, кэВ.</summary>
            public double ShiftKev { get; private set; }

            /// <summary>Энергия третьего кванта — чей отклик берётся, кэВ.</summary>
            public double ThirdKev { get; private set; }

            /// <summary>
            /// Вес отклика: `p_ij · ε_p(i) · ε_p(j) · P(m)`. Эффективности
            /// третьего кванта внутри НЕТ — она придёт из самой строки матрицы,
            /// поэтому второй раз применять её нельзя.
            /// </summary>
            public double Weight { get; private set; }

            public string Nuclide { get; private set; }
        }

        /// <summary>Поправки одного компонента: множители линий и его сумм-пики.</summary>
        public sealed class Correction
        {
            /// <summary>
            /// Множитель на площадь пика В ОБРАЗЕ, параллельно `component.Lines`.
            /// Это НАБЛЮДАЕМАЯ площадь, делённая на идеальную, то есть 1/CF, а
            /// не сам CF: образ моделирует то, что детектор видит, а CF по
            /// принятому смыслу восстанавливает истинную площадь из
            /// наблюдённой (A_ист = A_набл · CF). Знак путается на раз — при
            /// первом же прогоне множитель стоял вверх ногами и приподнимал
            /// пики вместо того, чтобы их срезать.
            /// </summary>
            public double[] LineFactors { get; set; }

            public List<SumPeak> SumPeaks { get; set; }

            /// <summary>
            /// Сумм-континуум (S19): частичное поглощение третьего кванта.
            /// Отдельным списком, а не внутри <see cref="SumPeaks"/>, потому что
            /// кладётся в образ иначе — сдвинутым откликом, а не дельтой в бин
            /// пика, — и срез <see cref="MaxSumPeaks"/> к нему не применяется:
            /// у него нет «высоты», по которой отбирать.
            /// </summary>
            public List<SumContinuum> SumContinua { get; set; }

            /// <summary>
            /// Разбор поправки по линиям — только для отчёта
            /// (<see cref="FsaCascadeSummer.Describe"/>). В счёте не участвует:
            /// счёт идёт по <see cref="LineFactors"/>.
            /// </summary>
            public List<LineNote> Notes { get; set; }

            /// <summary>Есть ли вообще что применять — иначе быстрый путь.</summary>
            public bool Any { get; set; }
        }

        /// <summary>
        /// Что именно сделано с одной линией: сам CF и обе его половины —
        /// вынос (партнёр задел кристалл) и влёт (сумма пары попала в окно
        /// линии). Без этой раскладки CF есть одно число, и увидеть, почему
        /// оно такое, нельзя — ровно то, чего не хватало при сверке с ЛСРМ.
        /// </summary>
        public sealed class LineNote
        {
            public string Nuclide { get; set; }

            public double EnergyKev { get; set; }

            /// <summary>CF в принятом смысле: A_ист = A_набл · CF.</summary>
            public double Cf { get; set; }

            /// <summary>Доля событий, вынесенных из пика партнёром.</summary>
            public double Loss { get; set; }

            /// <summary>Влёт: площадь сумм-событий в окне линии, к прямой площади.</summary>
            public double InShare { get; set; }

            /// <summary>Прямая площадь линии на распад родителя.</summary>
            public double DirectArea { get; set; }
        }

        /// <summary>Пары и выходы одного нуклида, как они лежат в базе.</summary>
        sealed class NuclideData
        {
            public Dictionary<double, double> Intensity;                        // E → I, %
            public List<double[]> Pairs;                                        // {E, Ecoinc, P(Ecoinc|E)}
            public Dictionary<double, Dictionary<double, double>> Partners;     // P(m|a), обе стороны
        }

        /// <summary>
        /// Сумма пары попадает в окно линии — тогда влёт учитывается её CF, а
        /// отдельного сумм-пика ставить нельзя (двойной счёт). Полуширина как
        /// у `g4cf` и пробы: ±0.5 кэВ.
        /// </summary>
        const double SumWindowKev = 0.5;

        /// <summary>
        /// Энергии одной линии внутри таблиц совпадений совпадают до 0.001 кэВ —
        /// это одна поставка данных.
        /// </summary>
        const double SamePairLineKev = 0.05;

        /// <summary>
        /// А вот линия КОМПОНЕНТА приходит из справочника нуклидов пользователя,
        /// и там та же линия записана со своим округлением: у Lu-176 сильнейшая
        /// 306.78 против 306.880 в таблицах совпадений — 0.10 кэВ. С допуском
        /// 0.05 она не сходилась, и САМАЯ СИЛЬНАЯ линия нуклида (I = 93.6 %)
        /// молча оставалась без поправки. 0.3 кэВ покрывает такие разночтения и
        /// остаётся много меньше ПШПВ любого сцинтиллятора; ближайшая из
        /// подошедших всё равно выбирается по минимуму расхождения.
        /// </summary>
        const double SameLineKev = 0.3;

        /// <summary>
        /// Сумм-пик ниже этой доли от самого сильного пика компонента не
        /// ставится: он не виден, а массив поглощения ради него тянулся бы до
        /// удвоенной энергии.
        /// </summary>
        const double SumPeakFloor = 1.0E-4;

        /// <summary>Больше этого числа сумм-пиков на компонент не берём.</summary>
        const int MaxSumPeaks = 24;

        /// <summary>
        /// ЖУРНАЛ ТРОЙНЫХ СУММ (`S19`, диагностика). Каждая рассмотренная тройка
        /// с её площадью, порогом и решением — иначе не узнать, почему тройной
        /// суммы нет в перечне: отсев идёт в трёх местах, а наружу видно только
        /// выжившее.
        ///
        /// ⚠ Копится только когда включён <see cref="LogTriples"/>: на корпусном
        /// прогоне это сотни строк на спектр, и держать их незачем.
        /// </summary>
        public static bool LogTriples;

        /// <summary>Строки журнала троек; чистится вызывающим.</summary>
        public static readonly List<string> TripleLog = new List<string>();

        /// <summary>
        /// ⚡ ЗАМЕРНЫЙ РЫЧАГ (`S19`, `S50`): один множитель κ на все сумм-события.
        ///
        /// Площадь сумм-пика считается как `p_ij · ε_p(i) · ε_p(j)` — произведение
        /// СРЕДНИХ по объёму эффективностей. Но точка распада у двух квантов
        /// каскада ОДНА, и на протяжённом источнике их шансы связаны: верная
        /// величина — среднее ПРОИЗВЕДЕНИЯ ⟨ε₁ε₂⟩, которое больше. Отношение
        /// κ = ⟨ε₁ε₂⟩/(⟨ε₁⟩⟨ε₂⟩) меряет `CascadeJointProbe`: на банке Ø40×h15
        /// вплотную к ASN16 оно 1.34 для пары 202+307 и 2.26 для 88+202, а на
        /// точечном источнике 1.00 — то есть мерится именно протяжённость.
        ///
        /// ⚠ Ноль (умолчание) — рычаг выключен, счёт прежний. Настоящая поправка
        /// обязана зависеть от энергий пары и приходить из матрицы; этот ключ
        /// нужен, чтобы измерить цену до того, как считать её для всего склада.
        /// </summary>
        public static double JointFactorOverride;

        static readonly object Gate = new object();

        static readonly Dictionary<string, NuclideData> Cache =
            new Dictionary<string, NuclideData>(StringComparer.OrdinalIgnoreCase);

        static bool databaseChecked;
        static bool databasePresent;

        /// <summary>
        /// Почему база не отдала данные, если не отдала. Пусто — отказов не
        /// было. Читатель — пробы и журнал: без него отказ выглядит как
        /// «у нуклида нет каскадов», и поломка живёт незамеченной.
        /// </summary>
        public static string Failure { get; private set; }

        /// <summary>
        /// Окно совпадения по умолчанию, секунды, — когда прибор своего
        /// мёртвого времени не назвал (`DeadTime()` вернул 0 или конфигурация
        /// его не держит вовсе, как все заглушки корпуса).
        ///
        /// Величина выбрана НЕ из середины, а с узкого края семейства: у
        /// RadiaCode и Obsidian в коде стоит 5 мкс, у AS80x80 измерено 1.357,
        /// у AudioInput это длина формы импульса. Узкое окно даёт МЕНЬШЕ
        /// совпадений, то есть меньшую поправку, — и ошибается в сторону «не
        /// выдумать суммирования», а не наоборот.
        /// </summary>
        public const double DefaultCoincidenceWindowSec = 1.0E-6;

        readonly ResponseMatrix matrix;
        readonly double[] peakAtNode;
        readonly double[] totalAtNode;
        readonly MaterialDatabase.LightYieldCurve light;
        readonly double windowSec;
        readonly bool withXrays;
        readonly bool withAnnihilation;
        readonly bool withIsomers;
        readonly Dictionary<FsaComponent, Correction> corrections =
            new Dictionary<FsaComponent, Correction>();

        /// <summary>
        /// Данные нуклида, уже дополненные рентгеном и аннигиляцией. Кэш
        /// ЭКЗЕМПЛЯРА, а не общий: дополнение зависит от окна совпадения и от
        /// абляционных ключей, а они у каждого разбора свои. Общий кэш
        /// (<see cref="Cache"/>) держит то, что от них не зависит, — поставку
        /// SandiaDecay.
        /// </summary>
        readonly Dictionary<string, NuclideData> augmented =
            new Dictionary<string, NuclideData>(StringComparer.OrdinalIgnoreCase);

        FsaCascadeSummer(ResponseMatrix matrix, double[] peakAtNode, double[] totalAtNode,
                         MaterialDatabase.LightYieldCurve light, double windowSec,
                         bool withXrays, bool withAnnihilation, bool withIsomers)
        {
            this.matrix = matrix;
            this.peakAtNode = peakAtNode;
            this.totalAtNode = totalAtNode;
            this.light = light;
            this.windowSec = windowSec > 0.0 ? windowSec : DefaultCoincidenceWindowSec;
            this.withXrays = withXrays;
            this.withAnnihilation = withAnnihilation;
            this.withIsomers = withIsomers;
        }

        /// <summary>Окно совпадения этого разбора, секунды — для отчёта проб.</summary>
        public double CoincidenceWindowSec
        {
            get { return this.windowSec; }
        }

        /// <summary>Имя кривой света, по которой ставятся суммы; пусто — по энергии.</summary>
        public string LightYieldName
        {
            get { return this.light == null ? "" : this.light.Material; }
        }

        /// <summary>
        /// Суммирователь для этой матрицы; null — считать нечем: матрицы нет,
        /// у неё нет раскладки по каналам (формат старше 3) или рядом с
        /// программой нет `nucdb.sqlite`.
        /// </summary>
        public static FsaCascadeSummer Create(ResponseMatrix matrix)
        {
            return Create(matrix, null);
        }

        /// <summary>
        /// То же, но с веществом кристалла: по нему берётся кривая светового
        /// выхода, и суммы ставятся по СВЕТУ, а не по энергии (S20). Имя —
        /// как в `scint_electron_light_yield` («CsI:Tl», «NaI:Tl»); пустое или
        /// незнакомое даёт прежнее поведение, а не отказ: без кривой сумма по
        /// энергии — приближение, а не ошибка.
        ///
        /// Вещество приходит СНАРУЖИ, потому что у матрицы его нет: она хранит
        /// от геометрии только необратимый отпечаток (`ResponseMatrix.Stamp`).
        /// </summary>
        public static FsaCascadeSummer Create(ResponseMatrix matrix, string scintillator)
        {
            return Create(matrix, scintillator, 0.0, true, true, true);
        }

        /// <summary>
        /// То же, но с ОКНОМ СОВПАДЕНИЯ и абляционными ключами (S27).
        ///
        /// `windowSec` — мёртвое время прибора, то есть длительность импульса:
        /// два кванта попадают в один импульс и складываются, только если
        /// разошлись во времени меньше, чем на неё. Ноль означает «прибор не
        /// сказал» и заменяется <see cref="DefaultCoincidenceWindowSec"/>.
        ///
        /// `withXrays` / `withAnnihilation` — выключатели для РАЗДЕЛЯЮЩЕГО
        /// замера: цена правки меряется при одной версии физики, «было/стало»
        /// на одном бинаре. По правилу T42 в клеймо матрицы они не идут — и не
        /// должны: матрицу они не трогают вовсе, это слой поверх неё.
        /// </summary>
        public static FsaCascadeSummer Create(ResponseMatrix matrix, string scintillator,
                                              double windowSec, bool withXrays,
                                              bool withAnnihilation, bool withIsomers)
        {
            if (matrix == null || !matrix.HasChannels || matrix.Energies == null
                || matrix.Energies.Length == 0 || matrix.Rows == null)
            {
                return null;
            }

            if (!DatabasePresent())
            {
                return null;
            }

            float[][] peakRows = matrix.ChannelRows[(int)EfficiencySimulator.ResponseChannel.Peak];
            int nodes = matrix.Energies.Length;
            double[] peak = new double[nodes];
            double[] total = new double[nodes];
            for (int i = 0; i < nodes; i++)
            {
                peak[i] = Sum(peakRows != null && i < peakRows.Length ? peakRows[i] : null);
                total[i] = Sum(i < matrix.Rows.Length ? matrix.Rows[i] : null);
            }

            MaterialDatabase.LightYieldCurve curve = null;
            if (!string.IsNullOrEmpty(scintillator))
            {
                try
                {
                    curve = MaterialDatabase.LightYieldOf(scintillator);
                }
                catch (Exception ex)
                {
                    // Отказ базы не должен ронять разбор, но и молчать о нём
                    // нельзя: без кривой суммы поедут на единицы кэВ, а
                    // выглядеть это будет как «модель промахнулась».
                    Failure = "кривая света для «" + scintillator + "»: " + ex.Message;
                }
            }

            return new FsaCascadeSummer(matrix, peak, total, curve, windowSec,
                                        withXrays, withAnnihilation, withIsomers);
        }

        /// <summary>
        /// Где на ШКАЛЕ окажется сумма нескольких полностью поглощённых квантов.
        ///
        /// В сцинтилляторе шкалу задаёт свет, а он непропорционален энергии
        /// (F11): энергетическая калибровка снята по ОДИНОЧНЫМ линиям, то есть
        /// связывает канал с Λ(E) = L(E)·E одного кванта. У пары свет
        /// складывается, и видимая энергия суммы решает уравнение
        ///
        ///     L(E_вид)·E_вид = Σ_k L(E_k)·E_k,
        ///
        /// а не равна Σ E_k. По кривой CsI:Tl это +2.96 кэВ на 508.61
        /// (201.83+306.78), +3.64 на 290.17 и +6.30 на тройной 596.95 — величины
        /// порядка десятой доли полуширины, но систематические и в одну сторону.
        ///
        /// ⚠ Приближение названо: L(E) — выход для ЭЛЕКТРОНА энергии E, а квант
        /// отдаёт энергию каскадом электронов разной энергии. Точная Λ(E) есть
        /// только у симулятора (`EfficiencySimulator.lightDeposit`), суммирователю
        /// она недоступна. Это ровно та формула, которой мерена цена в S20.
        /// </summary>
        public double ApparentSum(double first, double second, double third = 0.0)
        {
            double plain = first + second + third;
            if (this.light == null || !(plain > 0.0))
            {
                return plain;
            }

            double target = Light(first) + Light(second) + (third > 0.0 ? Light(third) : 0.0);

            // Обращение Λ(E) деление пополам: кривая монотонна по построению
            // (свет растёт с энергией), а аналитического обратного у неё нет.
            double lo = plain * 0.5, hi = plain * 1.5;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Light(mid) < target)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return 0.5 * (lo + hi);
        }

        double Light(double energyKev)
        {
            return this.light.Of(energyKev) * energyKev;
        }

        /// <summary>
        /// Сколько пар совпадений известно про этот нуклид. Ноль значит либо
        /// «имя не разбирается», либо «каскадов у нуклида нет» — но эти два
        /// случая для отчёта различает <see cref="Nucid"/>. Заведено ради проб:
        /// «поправка ничего не сделала» без такого различения — сигнал, по
        /// которому нельзя понять, сломано что-то или так и должно быть.
        /// </summary>
        public int PairCount(string nuclide)
        {
            NuclideData data = Data(nuclide);
            return data != null ? data.Pairs.Count : 0;
        }

        /// <summary>
        /// Есть ли у нуклида линия с такой энергией в таблицах совпадений —
        /// то есть сойдётся ли линия компонента с линией базы. Разъезд имён и
        /// округлений здесь тише всего: поправка просто не применяется.
        /// </summary>
        public bool HasLine(string nuclide, double energyKev)
        {
            NuclideData data = Data(nuclide);
            double found;
            return data != null && Match(data.Intensity, energyKev, out found);
        }

        /// <summary>
        /// Поправки компонента; null — этому компоненту поправлять нечего
        /// (нуклид не разбирается, пар нет, всё вышло единицей).
        /// </summary>
        public Correction For(FsaComponent component)
        {
            if (component == null || component.Lines == null || component.Lines.Count == 0)
            {
                return null;
            }

            Correction correction;
            if (this.corrections.TryGetValue(component, out correction))
            {
                return correction;
            }

            correction = this.Compute(component);
            this.corrections[component] = correction;
            return correction;
        }

        /// <summary>
        /// Перечень того, что каскадное суммирование сделало с компонентом:
        /// сумм-пики с породившими их парами и раскладка CF по линиям.
        ///
        /// ЗАЧЕМ ОТДЕЛЬНЫМ ВЫХОДОМ. Сумм-пики у нас считаются формулой, и
        /// наружу выходит только их действие — подправленный образ. При сверке
        /// с ЛСРМ этого мало: у них в отчёте есть разделы «Coincidence sum
        /// peaks» и «xray_peaks», то есть видно, ЧТО именно они посчитали
        /// суммой, а у нас видно было только «на сколько всё съехало» (наш
        /// F25). Сравнивать два числа, пришедших разными путями, без такого
        /// перечня нельзя.
        ///
        /// Печатается всё, что посчитано, включая отброшенное порогом
        /// (`SumPeakFloor`) и срезанное `MaxSumPeaks`: в `Correction` попадает
        /// только выжившее, а знать надо и то, что не выжило.
        ///
        /// Рентгеновских линий здесь нет и быть пока не может: библиотека FSA
        /// не различает γ и рентген (у `FsaLine` нет вида линии, см. TODO R2),
        /// так что раздела «xray_peaks» у нас нет не потому, что его не
        /// напечатали, а потому, что его нечем наполнить.
        /// </summary>
        public string Describe(FsaComponent component)
        {
            Correction correction = this.For(component);
            var sb = new StringBuilder();
            sb.Append("компонент: ").Append(component == null ? "(нет)" : component.Name)
              .AppendLine();
            if (correction == null)
            {
                sb.AppendLine("поправлять нечего: нуклид не разбирается или пар нет");
                return sb.ToString();
            }

            sb.AppendLine();
            sb.AppendLine("Coincidence sum peaks — площади на распад родителя цепочки");
            sb.AppendLine("(площадь уже с обеими пиковыми эффективностями и с множителем");
            sb.AppendLine(" выживания третьего кванта S_ij; второй раз эффективность не применять)");
            sb.AppendLine();
            if (correction.SumPeaks == null || correction.SumPeaks.Count == 0)
            {
                sb.AppendLine("  нет: либо пары не нашлись, либо все суммы попали в окна линий");
                sb.AppendLine("  (попавшая сумма учтена влётом в CF своей линии — см. ниже)");
            }
            else
            {
                sb.AppendLine("   E сумм, кэВ        слагаемые, кэВ         нуклид        площадь");
                foreach (SumPeak peak in correction.SumPeaks)
                {
                    // Энергия печатается ВИДИМАЯ (по свету), поэтому рядом с
                    // ней стоят слагаемые: без них разница «сумма не равна
                    // сумме» читается как опечатка, а это сдвиг S20.
                    string parts = peak.IsTriple
                        ? string.Format(CultureInfo.InvariantCulture, "{0:F2}+{1:F2}+{2:F2}",
                                        peak.FromKev, peak.WithKev, peak.ThirdKev)
                        : string.Format(CultureInfo.InvariantCulture, "{0:F2}+{1:F2}",
                                        peak.FromKev, peak.WithKev);
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "  {0,11:F2}   {1,-22}  {2,-10}  {3,12:E4}",
                        peak.Energy, parts, peak.Nuclide, peak.Area);
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
            sb.AppendLine("Раскладка CF по линиям (A_ист = A_набл · CF)");
            sb.AppendLine("вынос — партнёр задел кристалл; влёт — сумма пары попала в окно линии");
            sb.AppendLine();
            if (correction.Notes == null || correction.Notes.Count == 0)
            {
                sb.AppendLine("  нет линий, сошедшихся с базой совпадений");
            }
            else
            {
                sb.AppendLine("     E, кэВ   нуклид          CF     вынос     влёт   прямая площадь");
                foreach (LineNote note in correction.Notes)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "  {0,9:F2}   {1,-10}  {2,8:F4}  {3,8:F4} {4,8:F4}   {5,12:E4}",
                        note.EnergyKev, note.Nuclide, note.Cf, note.Loss,
                        note.InShare, note.DirectArea);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Счёт
        // ------------------------------------------------------------------

        Correction Compute(FsaComponent component)
        {
            int count = component.Lines.Count;
            double[] factors = new double[count];
            for (int i = 0; i < count; i++)
            {
                factors[i] = 1.0;
            }

            List<SumPeak> sumPeaks = new List<SumPeak>();
            List<SumContinuum> continua = new List<SumContinuum>();
            List<LineNote> notes = new List<LineNote>();
            bool any = false;

            // Пары совпадений живут ВНУТРИ одного нуклида: у компонента-цепочки
            // линии разных дочерних, и мешать их каскады нельзя.
            foreach (string nuclide in DistinctNuclides(component))
            {
                NuclideData data = Data(nuclide);
                if (data == null)
                {
                    continue;
                }

                // Масштаб нормировки: интенсивности компонента даны на распад
                // РОДИТЕЛЯ цепочки, база — на распад самого нуклида. Отношение
                // берётся по самой сильной сошедшейся линии; в CF оно
                // сокращается, а площади сумм-пиков без него уехали бы.
                double scale = Scale(component, nuclide, data);
                double strongest = 0.0;
                for (int i = 0; i < count; i++)
                {
                    FsaLine line = component.Lines[i];
                    if (!Belongs(line, nuclide))
                    {
                        continue;
                    }

                    double area = line.Intensity / 100.0 * this.PeakEfficiency(line.Energy);
                    if (area > strongest)
                    {
                        strongest = area;
                    }

                    double baseEnergy;
                    if (!Match(data.Intensity, line.Energy, out baseEnergy))
                    {
                        continue;
                    }

                    // В образ идёт ОБРАТНАЯ величина: см. Correction.LineFactors.
                    double loss, inShare, direct;
                    double cf = this.CoincidenceFactor(data, baseEnergy,
                                                       out loss, out inShare, out direct);
                    notes.Add(new LineNote
                    {
                        Nuclide = nuclide,
                        EnergyKev = line.Energy,
                        Cf = cf,
                        Loss = loss,
                        InShare = inShare,
                        DirectArea = direct
                    });

                    if (cf > 0.0 && Math.Abs(cf - 1.0) > 1.0E-6)
                    {
                        factors[i] = 1.0 / cf;
                        any = true;
                    }
                }

                this.CollectSumPeaks(component, nuclide, data, scale, strongest, sumPeaks, continua);
            }

            if (sumPeaks.Count > 0)
            {
                any = true;
                sumPeaks.Sort((a, b) => b.Area.CompareTo(a.Area));
                if (sumPeaks.Count > MaxSumPeaks)
                {
                    sumPeaks.RemoveRange(MaxSumPeaks, sumPeaks.Count - MaxSumPeaks);
                }
            }

            if (continua.Count > 0)
            {
                any = true;
            }

            return new Correction
            {
                LineFactors = factors,
                SumPeaks = sumPeaks,
                SumContinua = continua,
                Notes = notes,
                Any = any
            };
        }

        /// <summary>
        /// CF одной линии по формуле EFFTRAN — в принятом смысле: во столько
        /// раз наблюдённая площадь МЕНЬШЕ истинной (A_ист = A_набл · CF).
        /// Больше единицы — суммирование выносит из пика больше, чем вносит.
        /// </summary>
        double CoincidenceFactor(NuclideData data, double energy)
        {
            double loss, inShare, direct;
            return this.CoincidenceFactor(data, energy, out loss, out inShare, out direct);
        }

        /// <summary>
        /// То же с раскладкой на составляющие — для отчёта
        /// (<see cref="Describe"/>). Один и тот же счёт, две подписи: копия
        /// формулы ради печати однажды разошлась бы с той, по которой считают.
        /// </summary>
        double CoincidenceFactor(NuclideData data, double energy,
                                 out double loss, out double inShare, out double direct)
        {
            loss = 0.0;
            inShare = 0.0;
            direct = 0.0;

            double intensity;
            if (!data.Intensity.TryGetValue(energy, out intensity) || !(intensity > 0.0))
            {
                return 1.0;
            }

            // Вынос: любой партнёр, оставивший в кристалле хоть что-нибудь,
            // уносит событие из пика.
            foreach (KeyValuePair<double, double> partner in Partners(data, energy))
            {
                loss += partner.Value * this.TotalEfficiency(partner.Key);
            }

            // Влёт: пары, сумма которых попадает в окно этой линии. Сравнивается
            // ВИДИМАЯ сумма (по свету, S20) — окно задано на шкале прибора, а
            // сумма встаёт на неё сдвинутой на единицы кэВ.
            double sumIn = 0.0;
            foreach (double[] pair in data.Pairs)
            {
                if (Math.Abs(this.ApparentSum(pair[0], pair[1]) - energy) >= SumWindowKev)
                {
                    continue;
                }

                sumIn += this.PairArea(data, pair);
            }

            direct = intensity / 100.0 * this.PeakEfficiency(energy);
            inShare = direct > 0.0 ? sumIn / direct : 0.0;
            double denominator = (1.0 - loss) + inShare;
            return denominator > 0.0 ? 1.0 / denominator : 1.0;
        }

        /// <summary>
        /// Сумм-пики нуклида: пары, чья сумма НЕ попала ни в одну линию этого
        /// компонента. Попавшие уже учтены влётом в CF той линии, и ставить их
        /// вторично значило бы посчитать одно и то же дважды.
        /// </summary>
        void CollectSumPeaks(FsaComponent component, string nuclide, NuclideData data,
                             double scale, double strongest, List<SumPeak> sumPeaks,
                             List<SumContinuum> continua)
        {
            if (!(scale > 0.0))
            {
                return;
            }

            double floor = strongest * SumPeakFloor;
            foreach (double[] pair in data.Pairs)
            {
                // Энергия сумм-пика — ВИДИМАЯ (по свету, S20): именно на это
                // место шкалы событие ложится, и именно с этим местом надо
                // сверять окна линий компонента.
                double energy = this.ApparentSum(pair[0], pair[1]);
                if (this.PeakEfficiency(energy) <= 0.0)
                {
                    continue;
                }

                bool absorbed = false;
                foreach (FsaLine line in component.Lines)
                {
                    if (Belongs(line, nuclide) && Math.Abs(line.Energy - energy) < SumWindowKev)
                    {
                        absorbed = true;
                        break;
                    }
                }

                if (absorbed)
                {
                    continue;
                }

                double area = scale * this.PairArea(data, pair) * JointFactor();
                if (area > floor)
                {
                    sumPeaks.Add(new SumPeak(energy, area, nuclide, pair[0], pair[1]));
                }

                this.CollectTripleSums(component, nuclide, data, pair, scale, floor, sumPeaks,
                                       continua);
            }
        }

        /// <summary>
        /// Тройные суммы пары (S19). Множитель выживания `S_ij` вычитает из пары
        /// те случаи, когда третий квант каскада тоже попал в кристалл, и до
        /// 13.08.2026 вычтенное просто пропадало. Между тем часть его —
        /// ε_p(m) из ε_T(m) — это ПОЛНОЕ поглощение третьего, то есть свой пик
        /// на E_i+E_j+E_m. Мерено на Lu-176: сумма всех трёх (88.34+201.83+306.78)
        /// стоит отдельным пиком на пустом месте, и модель его не ставила вовсе.
        ///
        /// Остаток `ε_T(m) − ε_p(m)` — частичное поглощение третьего — по-прежнему
        /// пропадает: он даёт не пик, а сплошной подъём между E_i+E_j и
        /// E_i+E_j+E_m, и для него нужен отдельный образ (вторая половина S19).
        /// </summary>
        void CollectTripleSums(FsaComponent component, string nuclide, NuclideData data,
                               double[] pair, double scale, double floor, List<SumPeak> sumPeaks,
                               List<SumContinuum> continua)
        {
            double baseArea = this.PairBase(data, pair);
            if (!(baseArea > 0.0))
            {
                return;
            }

            double pairEnergy = this.ApparentSum(pair[0], pair[1]);
            foreach (KeyValuePair<double, double> third in MergedThird(data, pair[0], pair[1]))
            {
                double peakThird = this.PeakEfficiency(third.Key);
                double totalThird = this.TotalEfficiency(third.Key);

                // Частичное поглощение третьего кванта — сумм-континуум (S19,
                // вторая половина). Именно эту долю `S_ij` вычитал и терял:
                // пика она не даёт, но и в нуль не обращается. Вес идёт БЕЗ
                // эффективности третьего — она придёт из строки матрицы.
                if (totalThird > peakThird && third.Value > 0.0 && baseArea > 0.0)
                {
                    continua.Add(new SumContinuum(pairEnergy, third.Key,
                                                  scale * baseArea * third.Value, nuclide));
                }

                if (!(peakThird > 0.0))
                {
                    continue;
                }

                double area = scale * baseArea * third.Value * peakThird * JointFactor();
                double energy = this.ApparentSum(pair[0], pair[1], third.Key);
                if (LogTriples)
                {
                    TripleLog.Add(string.Format(CultureInfo.InvariantCulture,
                        "  {0,9:F2} = {1:F2}+{2:F2}+{3:F2}  {4,-8}  площадь {5:E3}  порог {6:E3}  {7}",
                        energy, pair[0], pair[1], third.Key, nuclide, area, floor,
                        area > floor ? "проходит" : "НИЖЕ ПОРОГА"));
                }

                if (!(area > floor))
                {
                    continue;
                }

                if (this.PeakEfficiency(energy) <= 0.0)
                {
                    continue;
                }

                // Та же защита от двойного счёта, что у пар: сумма, попавшая в
                // окно линии компонента, уже учтена влётом в CF этой линии.
                bool absorbed = false;
                foreach (FsaLine line in component.Lines)
                {
                    if (Belongs(line, nuclide) && Math.Abs(line.Energy - energy) < SumWindowKev)
                    {
                        absorbed = true;
                        break;
                    }
                }

                if (absorbed)
                {
                    continue;
                }

                // И защита от двойного счёта между самими тройками: пара (i,j) с
                // третьим m и пара (i,m) с третьим j дают ОДНО И ТО ЖЕ событие.
                // Держим первую встреченную — суммы уже стоят на одном месте
                // шкалы, и вторая была бы чистым удвоением.
                bool already = false;
                foreach (SumPeak have in sumPeaks)
                {
                    if (have.IsTriple && Math.Abs(have.Energy - energy) < SamePairLineKev)
                    {
                        already = true;
                        break;
                    }
                }

                if (!already)
                {
                    sumPeaks.Add(new SumPeak(energy, area, nuclide, pair[0], pair[1], third.Key));
                }
            }
        }

        /// <summary>Множитель совместной эффективности; 1.0 — рычаг выключен.</summary>
        static double JointFactor()
        {
            return JointFactorOverride > 0.0 ? JointFactorOverride : 1.0;
        }

        /// <summary>
        /// Площадь сумм-события пары на распад: оба кванта поглощены целиком и
        /// ТРЕТИЙ квант каскада не помешал.
        /// </summary>
        double PairArea(NuclideData data, double[] pair)
        {
            double survive = this.Survive(data, pair);
            return survive > 0.0 ? this.PairBase(data, pair) * survive : 0.0;
        }

        /// <summary>
        /// Площадь пары БЕЗ множителя выживания: оба кванта поглощены целиком, а
        /// что делает третий — ещё не решено. Отделено от <see cref="PairArea"/>
        /// ради S19: та часть, которую `S_ij` вычитает, не исчезает — при полном
        /// поглощении третьего она даёт тройной сумм-пик.
        /// </summary>
        double PairBase(NuclideData data, double[] pair)
        {
            double intensity;
            if (!data.Intensity.TryGetValue(pair[0], out intensity) || !(intensity > 0.0))
            {
                return 0.0;
            }

            return intensity / 100.0 * pair[2]
                   * this.PeakEfficiency(pair[0]) * this.PeakEfficiency(pair[1]);
        }

        /// <summary>
        /// Доля пар, которым третий квант каскада не помешал:
        /// `S_ij = 1 − Σ_m P(m) · ε_T(m)`.
        /// </summary>
        double Survive(NuclideData data, double[] pair)
        {
            double survive = 1.0;
            foreach (KeyValuePair<double, double> third in MergedThird(data, pair[0], pair[1]))
            {
                survive -= third.Value * this.TotalEfficiency(third.Key);
            }

            return survive;
        }

        /// <summary>
        /// Третий квант каскада для пары (i, j): тройная условная из парных
        /// данных невосстановима, берётся P(m|i∧j) ≈ max(P(m|i), P(m|j)) — для
        /// каскада i→j квант ниже j воспроизводится точно, выше i консервативно.
        /// </summary>
        static Dictionary<double, double> MergedThird(NuclideData data, double i, double j)
        {
            Dictionary<double, double> merged = new Dictionary<double, double>();
            foreach (double side in new[] { i, j })
            {
                foreach (KeyValuePair<double, double> entry in Partners(data, side))
                {
                    if (Math.Abs(entry.Key - i) < SamePairLineKev || Math.Abs(entry.Key - j) < SamePairLineKev)
                    {
                        continue;
                    }

                    double have;
                    if (!merged.TryGetValue(entry.Key, out have) || entry.Value > have)
                    {
                        merged[entry.Key] = entry.Value;
                    }
                }
            }

            return merged;
        }

        static Dictionary<double, double> Partners(NuclideData data, double energy)
        {
            Dictionary<double, double> bag;
            return data.Partners.TryGetValue(energy, out bag)
                ? bag
                : new Dictionary<double, double>();
        }

        /// <summary>
        /// Отношение «выход в компоненте / выход в базе» по самой сильной
        /// сошедшейся линии нуклида.
        /// </summary>
        static double Scale(FsaComponent component, string nuclide, NuclideData data)
        {
            double best = 0.0;
            double scale = 1.0;
            foreach (FsaLine line in component.Lines)
            {
                double baseEnergy;
                if (!Belongs(line, nuclide) || !(line.Intensity > 0.0)
                    || !Match(data.Intensity, line.Energy, out baseEnergy))
                {
                    continue;
                }

                double baseIntensity = data.Intensity[baseEnergy];
                if (baseIntensity > best && baseIntensity > 0.0)
                {
                    best = baseIntensity;
                    scale = line.Intensity / baseIntensity;
                }
            }

            return scale;
        }

        static IEnumerable<string> DistinctNuclides(FsaComponent component)
        {
            List<string> names = new List<string>();
            foreach (FsaLine line in component.Lines)
            {
                string name = line.Nuclide ?? "";
                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        static bool Belongs(FsaLine line, string nuclide)
        {
            return string.Equals(line.Nuclide ?? "", nuclide, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Линия компонента и линия базы — одна и та же?</summary>
        static bool Match(Dictionary<double, double> table, double energy, out double found)
        {
            found = 0.0;
            double best = SameLineKev;
            bool ok = false;
            foreach (double key in table.Keys)
            {
                double delta = Math.Abs(key - energy);
                if (delta < best)
                {
                    best = delta;
                    found = key;
                    ok = true;
                }
            }

            return ok;
        }

        // ------------------------------------------------------------------
        // Эффективности из матрицы
        // ------------------------------------------------------------------

        /// <summary>Пиковая эффективность: сумма строки канала полного поглощения.</summary>
        public double PeakEfficiency(double energyKev)
        {
            return this.Interpolate(this.peakAtNode, energyKev);
        }

        /// <summary>Полная эффективность: сумма всей строки узла.</summary>
        public double TotalEfficiency(double energyKev)
        {
            return this.Interpolate(this.totalAtNode, energyKev);
        }

        /// <summary>
        /// Между узлами — логарифмическая интерполяция: сетка узлов
        /// логарифмическая, и эффективность на ней ложится почти прямой, а
        /// линейная по энергии заметно врала бы внизу шкалы. За краями
        /// ЗАЖИМАЕТСЯ: экстраполировать степенным ходом на энергии, где физика
        /// другая (ниже порога, выше сетки), — верный способ получить ерунду.
        /// </summary>
        double Interpolate(double[] values, double energyKev)
        {
            double[] grid = this.matrix.Energies;
            if (!(energyKev > 0.0) || grid.Length == 0)
            {
                return 0.0;
            }

            if (energyKev <= grid[0])
            {
                return values[0];
            }

            int last = grid.Length - 1;
            if (energyKev >= grid[last])
            {
                return values[last];
            }

            int hi = Array.BinarySearch(grid, energyKev);
            if (hi >= 0)
            {
                return values[hi];
            }

            hi = ~hi;
            int lo = hi - 1;
            double a = values[lo], b = values[hi];
            double t = (Math.Log(energyKev) - Math.Log(grid[lo]))
                       / (Math.Log(grid[hi]) - Math.Log(grid[lo]));
            if (a > 0.0 && b > 0.0)
            {
                return Math.Exp(Math.Log(a) + t * (Math.Log(b) - Math.Log(a)));
            }

            return a + t * (b - a);
        }

        static double Sum(float[] row)
        {
            if (row == null)
            {
                return 0.0;
            }

            double total = 0.0;
            foreach (float value in row)
            {
                total += value;
            }

            return total;
        }

        // ------------------------------------------------------------------
        // База
        // ------------------------------------------------------------------

        /// <summary>
        /// Пары и выходы нуклида, УЖЕ дополненные рентгеном и аннигиляцией;
        /// null — имя не разбирается (пики вылета) либо у нуклида нет ни
        /// совпадений, ни атомных партнёров (K-40).
        ///
        /// ⚠ Стала методом ЭКЗЕМПЛЯРА при S27: дополнение зависит от окна
        /// совпадения прибора и от абляционных ключей, а они принадлежат
        /// разбору, не процессу. Поставка SandiaDecay по-прежнему лежит в общем
        /// статическом кэше — она от разбора не зависит.
        /// </summary>
        NuclideData Data(string nuclide)
        {
            string key = ParentKey(nuclide);
            if (key == null)
            {
                return null;
            }

            // Выключатель прежнего поведения (S27, пункт «изомеры»): до правки
            // `Nucid` возвращал на именах вида «Ba-137m» null, и такой
            // компонент оставался без поправки молча. Ключ нужен, чтобы цену
            // именно этой половины можно было снять отдельно.
            if (!this.withIsomers && key.StartsWith(IsomerPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            NuclideData ready;
            if (this.augmented.TryGetValue(key, out ready))
            {
                return ready;
            }

            NuclideData raw = BaseData(key);
            ready = this.Augment(key, raw);
            this.augmented[key] = ready;
            return ready;
        }

        /// <summary>Поставка SandiaDecay как есть; кэш общий на процесс.</summary>
        static NuclideData BaseData(string key)
        {
            lock (Gate)
            {
                NuclideData data;
                if (Cache.TryGetValue(key, out data))
                {
                    return data;
                }

                data = Load(key);
                Cache[key] = data;
                return data;
            }
        }

        /// <summary>
        /// Ключ родителя совпадений по имени нуклида: либо наш `nucid`
        /// («Pb-214» → «214PB»), либо — для ИЗОМЕРОВ — символ Sandia
        /// («Ba-137m» → «Ba137m»), с приставкой, отличающей одно от другого.
        ///
        /// ⚠ Почему у изомеров отдельный путь (S27, пункт «изомеры
        /// пропускаются»). Наш `l_seqno` — это НОМЕР УРОВНЯ в схеме, а не
        /// порядковый номер изомера: у Sandia он лежит отдельным полем
        /// `isomer`, и 418 изомеров поставки нашей нумерации не приписаны
        /// вовсе (`database/scheme.md`, §8). Искать их поэтому надо по
        /// `sandia_symbol`. До S27 <see cref="Nucid"/> возвращал на таких
        /// именах null, и «Ba-137m» молча оставался без поправки.
        /// </summary>
        public static string ParentKey(string name)
        {
            string nucid = Nucid(name);
            if (nucid != null)
            {
                return nucid;
            }

            string symbol = SandiaSymbol(name);
            return symbol != null ? IsomerPrefix + symbol : null;
        }

        /// <summary>
        /// Приставка ключа изомера. Нужна, чтобы «Ba137m» нельзя было спутать с
        /// нашим `nucid`: пространство ключей одно, а таблицы разные.
        /// </summary>
        const string IsomerPrefix = "sandia:";

        /// <summary>
        /// Имя изомера в символ Sandia: «Ba-137m» → «Ba137m», «Tb-154m2» →
        /// «Tb154m2». Не изомер (нет буквенного хвоста после массы) — null:
        /// такие имена идут обычным путём, через `nucid`.
        /// </summary>
        public static string SandiaSymbol(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            int dash = name.IndexOf('-');
            if (dash <= 0 || dash + 1 >= name.Length)
            {
                return null;
            }

            string element = name.Substring(0, dash);
            string tail = name.Substring(dash + 1);
            foreach (char c in element)
            {
                if (!char.IsLetter(c))
                {
                    return null;
                }
            }

            // Масса, затем хвост изомера: «137m», «154m2». Без хвоста это не
            // изомер, и сюда попадать не должно.
            int digits = 0;
            while (digits < tail.Length && char.IsDigit(tail[digits]))
            {
                digits++;
            }

            if (digits == 0 || digits == tail.Length)
            {
                return null;
            }

            string suffix = tail.Substring(digits);
            foreach (char c in suffix)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    return null;
                }
            }

            return char.ToUpperInvariant(element[0])
                   + element.Substring(1).ToLowerInvariant()
                   + tail.Substring(0, digits)
                   + suffix.ToLowerInvariant();
        }

        static NuclideData Load(string key)
        {
            NuclideData data = new NuclideData
            {
                Intensity = new Dictionary<double, double>(),
                Pairs = new List<double[]>(),
                Partners = new Dictionary<double, Dictionary<double, double>>()
            };

            try
            {
                using (SqliteConnection connection = new SqliteConnection(
                    "Data Source=" + DatabasePath() + ";Mode=ReadOnly;Cache=Shared;"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        // Изомер ищется по символу Sandia, обычный нуклид — по
                        // нашему `nucid` при `isomer = 0`. Разные столбцы, одни
                        // и те же представления.
                        bool isomer = key.StartsWith(IsomerPrefix, StringComparison.Ordinal);
                        string parameter = isomer ? key.Substring(IsomerPrefix.Length) : key;
                        string filter = isomer
                            ? " where sandia_symbol = $n"
                            : " where nucid = $n and isomer = 0";

                        command.CommandText =
                            "select energy_kev, intensity_pct from v_gamma_coincidence_line"
                            + filter;
                        command.Parameters.AddWithValue("$n", parameter);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                data.Intensity[reader.GetDouble(0)] = reader.GetDouble(1);
                            }
                        }

                        command.CommandText =
                            "select energy_kev, coinc_energy_kev, fraction from v_gamma_coincidence"
                            + filter;
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                data.Pairs.Add(new[]
                                {
                                    reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                // База читается только ради поправки: не прочлась — работаем
                // без неё, как работали до неё. Но МОЛЧА этого делать нельзя:
                // «поправка ничего не сделала» и «поправка не смогла» с виду
                // одно и то же, и без записанной причины разница теряется.
                Failure = key + ": " + error.Message;
                return null;
            }

            // ⚠ Пустой набор пар БОЛЬШЕ НЕ ЗНАЧИТ «поправлять нечего» (S27): у
            // нуклида с одной гаммой (Ce-139, Cd-109, Na-22) пар γ-γ нет и в
            // поставке его нет вовсе, а совпадение с рентгеном или с
            // аннигиляционным квантом у него есть. Решение «ничего нет»
            // принимается теперь ПОСЛЕ дополнения, в Augment.

            // Пара лежит в базе ОДИН раз и направленно; обратная условная
            // считается через отношение выходов: P(A|B) = P(B|A)·I(A)/I(B)
            // (database/scheme.md, §8).
            foreach (double[] pair in data.Pairs)
            {
                Put(data, pair[0], pair[1], pair[2]);
                double ia, ib;
                if (data.Intensity.TryGetValue(pair[0], out ia)
                    && data.Intensity.TryGetValue(pair[1], out ib) && ib > 0.0)
                {
                    Put(data, pair[1], pair[0], pair[2] * ia / ib);
                }
            }

            return data;
        }

        /// <summary>
        /// Дополнить ядерные пары АТОМНЫМИ участниками распада: K-рентгеном
        /// дочернего атома и аннигиляционными квантами (S27).
        ///
        /// ГЛАВНЫЙ ХОД. Новые партнёры кладутся в те же `Pairs`, `Partners` и
        /// `Intensity`, что и ядерные, — и после этого весь счёт выше
        /// (CF, сумм-пики, тройные суммы, сумм-континуум, защита от двойного
        /// счёта) работает над ними без единой правки. Отдельной ветки «а тут
        /// у нас рентген» в формулах нет нигде, и это сознательно: две ветки
        /// разошлись бы при первой же правке одной из них.
        ///
        /// ВЕРОЯТНОСТЬ РЕНТГЕНА ПРИ ГАММЕ k. Складывается из двух источников
        /// вакансии, и они совпадают по-разному (см.
        /// <see cref="CascadeAtomicData"/>):
        ///
        ///     P(K-вакансия | γ_k) = V_захв·[γ_k пришла вовремя]
        ///                         + Σ_{T ≠ k} P(γ_T | γ_k)·α_K(T)·[T и k рядом]
        ///
        /// Второе слагаемое выводится так: доля событий, где ПЕРЕХОД T вообще
        /// случился, при известной γ_k равна P(γ_T|γ_k)·(1 + α_tot(T)), а
        /// вакансию он даёт с вероятностью α_K(T)/(1 + α_tot(T)) — полные
        /// коэффициенты сокращаются, и остаётся ровно P(γ_T|γ_k)·α_K(T).
        /// Слагаемого T = k нет НАРОЧНО: если k вылетела гаммой, значит она не
        /// конвертировала, и вакансии от неё в этом событии не было.
        /// </summary>
        NuclideData Augment(string key, NuclideData raw)
        {
            bool hasPairs = raw != null && raw.Pairs.Count > 0;
            if (!this.withXrays && !this.withAnnihilation)
            {
                return hasPairs ? raw : null;
            }

            // Изомеру своей строки в `decay_radiations` нет: выходы у нас
            // сложены на родителя цепочки (Cs-137 держит и линию Ba-137m).
            // Значит атомных данных для него взять неоткуда, и это не отказ.
            if (key.StartsWith(IsomerPrefix, StringComparison.Ordinal))
            {
                return hasPairs ? raw : null;
            }

            CascadeAtomicData atomic = CascadeAtomicData.Of(key);
            if (atomic == null)
            {
                return hasPairs ? raw : null;
            }

            if (!string.IsNullOrEmpty(atomic.Note))
            {
                Failure = key + ": " + atomic.Note;
            }

            // Копия, а не правка на месте: `raw` лежит в ОБЩЕМ кэше, и дописать
            // в него окно этого разбора значило бы отдать его следующему.
            NuclideData data = Copy(raw);

            // Выходы гамма-линий: у кого нет строки в поставке SandiaDecay,
            // берутся из `decay_radiations`. Уже имеющиеся НЕ трогаем — иначе
            // прежние замеры сдвинулись бы без всякой связи с S27 (у Lu-176
            // поставки расходятся: 91.0 % против 77.97 на линии 201.83).
            foreach (double[] line in atomic.GammaIntensity)
            {
                double had;
                if (!Match(data.Intensity, line[0], out had))
                {
                    data.Intensity[line[0]] = line[1];
                }
            }

            // Носитель: энергия, доля внутри своей серии и признак «это
            // вакансия» (рентген) против «это аннигиляция».
            var carriers = new List<Carrier>();
            if (this.withXrays && atomic.KIntensityPct > 0.0 && atomic.OmegaK > 0.0)
            {
                foreach (double[] line in atomic.KLines)
                {
                    // Вакансия одна, а ответить она может любой линией серии —
                    // отсюда доля.
                    carriers.Add(new Carrier
                    {
                        EnergyKev = line[0],
                        IntensityPct = line[1],
                        Share = line[1] / atomic.KIntensityPct,
                        FromVacancy = true
                    });
                }
            }

            if (this.withAnnihilation && atomic.AnnihilationQuanta > 0.0)
            {
                carriers.Add(new Carrier
                {
                    EnergyKev = AnnihilationKev,
                    IntensityPct = atomic.AnnihilationQuanta * 100.0,
                    Share = 1.0,
                    FromVacancy = false
                });
            }

            if (carriers.Count == 0)
            {
                return data.Pairs.Count > 0 ? data : null;
            }

            // ⛔ НОСИТЕЛЬ НИЖЕ СЕТКИ МАТРИЦЫ НЕ БЕРЁТСЯ ВОВСЕ, и это не мелочь.
            // `Interpolate` за нижним краем ЗАЖИМАЕТ значение первым узлом —
            // для линии самого нуклида это осторожно, а для партнёра совпадения
            // это выдумка: квант в 2.96 кэВ (Ar K у K-40) из пробы и корпуса не
            // выйдет никогда, а зажим выдаёт ему полную эффективность НИЖНЕГО
            // УЗЛА, то есть десятки процентов. Померено: K-40, у которого
            // никакого совпадения быть не может, ехал на 0.65 % χ²/ndf — ровно
            // отсюда. Матрица про такие энергии не знает ничего, и честный
            // ответ «не знаю» здесь — не заводить пару.
            //
            // Заодно это объясняет, у кого правка обязана быть невидимой:
            // Mn-54 (5.4 кэВ), Ti-44 (4.1), Co-57 (6.4), Zn-65 (8.0), Y-88
            // (14.1) — их K-рентген слишком мягок, чтобы дойти до кристалла, и
            // ноль у них ФИЗИЧЕСКИЙ, а не признак поломки.
            double lowestNode = this.matrix.Energies.Length > 0
                ? this.matrix.Energies[0]
                : 0.0;
            carriers.RemoveAll(c => c.EnergyKev < lowestNode);
            if (carriers.Count == 0)
            {
                return data.Pairs.Count > 0 ? data : null;
            }

            // Выходы носителей — в таблицу выходов ДО построения пар: обратная
            // условная считается через них, и на полпути их там быть уже
            // должно.
            foreach (Carrier carrier in carriers)
            {
                double had;
                if (!Match(data.Intensity, carrier.EnergyKev, out had))
                {
                    data.Intensity[carrier.EnergyKev] = carrier.IntensityPct;
                }
            }

            foreach (double[] gamma in atomic.GammaIntensity)
            {
                double decayEnergy = gamma[0];

                // ⛔ КЛЮЧ ПАРЫ — ТОТ ЖЕ, ЧТО У ЯДЕРНЫХ ПАР, а он приходит из
                // ДРУГОЙ поставки и округлён иначе: у Lu-176 линия 306.780 в
                // `decay_radiations` против 306.880 в таблицах совпадений.
                // `Partners` и `PairBase` ищут по ТОЧНОМУ ключу, поэтому пара,
                // положенная под энергией распада, для них не существует —
                // проверено измерением: первый прогон дал побитово те же
                // невязки, что и с выключенным ключом. Величина, не
                // шелохнувшаяся там, где обязана была двинуться, — это про
                // инструмент, а не про правку.
                double pairKey;
                if (!Match(data.Intensity, decayEnergy, out pairKey))
                {
                    pairKey = decayEnergy;
                }

                double delay = DelayOf(atomic, decayEnergy);
                if (delay < 0.0)
                {
                    // Перехода в схеме не нашлось — времени вылета не знаем.
                    // Считаем квант мгновенным: это сторона, где совпадение
                    // остаётся, а сам факт виден в отчёте пробы.
                    delay = 0.0;
                }

                foreach (Carrier carrier in carriers)
                {
                    double probability = carrier.FromVacancy
                        ? carrier.Share * this.VacancyGiven(atomic, raw, decayEnergy, delay)
                        : (delay < this.windowSec ? atomic.AnnihilationQuanta : 0.0);
                    if (!(probability > 0.0))
                    {
                        continue;
                    }

                    double carrierKey;
                    if (!Match(data.Intensity, carrier.EnergyKev, out carrierKey))
                    {
                        carrierKey = carrier.EnergyKev;
                    }

                    data.Pairs.Add(new[] { pairKey, carrierKey, probability });
                    Put(data, pairKey, carrierKey, probability);

                    // Обратная условная — тем же правилом, что у ядерных пар:
                    // P(A|B) = P(B|A)·I(A)/I(B).
                    double ia, ib;
                    if (data.Intensity.TryGetValue(pairKey, out ia)
                        && data.Intensity.TryGetValue(carrierKey, out ib) && ib > 0.0)
                    {
                        Put(data, carrierKey, pairKey, probability * ia / ib);
                    }
                }
            }

            return data.Pairs.Count > 0 ? data : null;
        }

        /// <summary>Неядерный участник каскада: линия рентгена или 511.</summary>
        sealed class Carrier
        {
            public double EnergyKev;

            /// <summary>Выход на распад, %.</summary>
            public double IntensityPct;

            /// <summary>Доля внутри своей серии; у аннигиляции единица.</summary>
            public double Share;

            /// <summary>
            /// true — квант родился из K-вакансии (тогда вероятность считает
            /// <see cref="VacancyGiven"/>), false — из аннигиляции позитрона.
            /// </summary>
            public bool FromVacancy;
        }

        /// <summary>Аннигиляционная линия, кэВ.</summary>
        const double AnnihilationKev = 511.0;

        /// <summary>
        /// Число K-вакансий, приходящееся на событие с гаммой `energyKev`, —
        /// формула из шапки <see cref="Augment"/>, уже с гейтом по времени.
        /// </summary>
        double VacancyGiven(CascadeAtomicData atomic, NuclideData raw,
                            double energyKev, double delaySec)
        {
            double vacancy = 0.0;
            if (delaySec < this.windowSec)
            {
                // Захватная вакансия рождается в момент распада, значит от неё
                // до гаммы прошло ровно `delaySec`.
                vacancy += atomic.PromptVacancy;
            }

            foreach (double[] other in atomic.GammaIntensity)
            {
                if (Math.Abs(other[0] - energyKev) < SamePairLineKev)
                {
                    continue;
                }

                CascadeAtomicData.Transition transition;
                if (!atomic.Gammas.TryGetValue(other[0], out transition)
                    || !(transition.AlphaK > 0.0))
                {
                    continue;
                }

                if (Math.Abs(transition.EmitDelaySec - delaySec) >= this.windowSec)
                {
                    continue;
                }

                vacancy += Conditional(atomic, raw, energyKev, other[0]) * transition.AlphaK;
            }

            return vacancy * atomic.OmegaK;
        }

        /// <summary>
        /// P(γ_other | γ_energy) — из поставки совпадений, если пара там есть.
        ///
        /// Пары нет — берём безусловный выход другой линии. Это НЕ уклонение:
        /// у нуклида с одной гаммой других слагаемых не бывает вовсе, а у
        /// многогаммового отсечка поставки (обе линии ≥0.1 %, доля ≥0.1 %)
        /// отбрасывает как раз слабые пары, где приближение независимости
        /// стоит меньше самого слагаемого.
        /// </summary>
        static double Conditional(CascadeAtomicData atomic, NuclideData raw,
                                  double energyKev, double otherKev)
        {
            if (raw != null)
            {
                double have;
                if (Match(raw.Intensity, energyKev, out have))
                {
                    Dictionary<double, double> bag;
                    if (raw.Partners.TryGetValue(have, out bag))
                    {
                        foreach (KeyValuePair<double, double> entry in bag)
                        {
                            if (Math.Abs(entry.Key - otherKev) < SameLineKev)
                            {
                                return entry.Value;
                            }
                        }
                    }
                }
            }

            foreach (double[] line in atomic.GammaIntensity)
            {
                if (Math.Abs(line[0] - otherKev) < SamePairLineKev)
                {
                    return line[1] / 100.0;
                }
            }

            return 0.0;
        }

        /// <summary>Через сколько секунд после распада вылетает эта гамма; −1 — не знаем.</summary>
        static double DelayOf(CascadeAtomicData atomic, double energyKev)
        {
            CascadeAtomicData.Transition transition;
            return atomic.Gammas.TryGetValue(energyKev, out transition)
                ? transition.EmitDelaySec
                : -1.0;
        }

        static NuclideData Copy(NuclideData source)
        {
            NuclideData data = new NuclideData
            {
                Intensity = new Dictionary<double, double>(),
                Pairs = new List<double[]>(),
                Partners = new Dictionary<double, Dictionary<double, double>>()
            };

            if (source == null)
            {
                return data;
            }

            foreach (KeyValuePair<double, double> entry in source.Intensity)
            {
                data.Intensity[entry.Key] = entry.Value;
            }

            foreach (double[] pair in source.Pairs)
            {
                data.Pairs.Add(new[] { pair[0], pair[1], pair[2] });
            }

            foreach (KeyValuePair<double, Dictionary<double, double>> entry in source.Partners)
            {
                var bag = new Dictionary<double, double>();
                foreach (KeyValuePair<double, double> inner in entry.Value)
                {
                    bag[inner.Key] = inner.Value;
                }

                data.Partners[entry.Key] = bag;
            }

            return data;
        }

        static void Put(NuclideData data, double from, double to, double probability)
        {
            Dictionary<double, double> bag;
            if (!data.Partners.TryGetValue(from, out bag))
            {
                data.Partners[from] = bag = new Dictionary<double, double>();
            }

            bag[to] = probability;
        }

        /// <summary>
        /// Имя нуклида в наш `nucid`: «Pb-214» → «214PB». Изомеры («Ba-137m»)
        /// возвращают null: у совпадений своя нумерация Sandia, искать их надо
        /// по `sandia_symbol`, а не по нашему номеру уровня.
        /// </summary>
        public static string Nucid(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            int dash = name.IndexOf('-');
            if (dash <= 0 || dash + 1 >= name.Length)
            {
                return null;
            }

            string element = name.Substring(0, dash);
            string mass = name.Substring(dash + 1);
            foreach (char c in element)
            {
                if (!char.IsLetter(c))
                {
                    return null;
                }
            }

            foreach (char c in mass)
            {
                if (!char.IsDigit(c))
                {
                    return null;
                }
            }

            int number;
            if (!int.TryParse(mass, NumberStyles.None, CultureInfo.InvariantCulture, out number)
                || number <= 0)
            {
                return null;
            }

            return number.ToString(CultureInfo.InvariantCulture)
                   + element.ToUpperInvariant();
        }

        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static bool DatabasePresent()
        {
            lock (Gate)
            {
                if (!databaseChecked)
                {
                    databasePresent = File.Exists(DatabasePath());
                    databaseChecked = true;
                }

                return databasePresent;
            }
        }
    }
}
