using BecquerelMonitor.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Второй путь к кривой эффективности: не восстановить её из измерений, а
    /// посчитать из геометрии монте-карловским переносом
    /// (<see cref="EfficiencySimulator"/>).
    ///
    /// Отличие от восстановления по вековому равновесию принципиальное:
    /// **уровень здесь абсолютный**. Восстановление даёт только форму, уровень
    /// приходится брать с прежней кривой или с опорной точки; расчёт даёт
    /// эффективность как она есть, из телесного угла, ослабления и сечений.
    ///
    /// Чего от него ждать. Поверка по источникам с известной активностью
    /// (журнал `tools/effmaker/README.md`): на чистой одиночной линии —
    /// точечный источник Cs-137 на 10 см, паспортная активность — расчёт
    /// сходится с измерением до 1.3 %, у RC103 в маринелли против заводского
    /// коэффициента 0.994. На линиях в наложениях расхождение доходит до
    /// десятка процентов, но это разброс ИЗМЕРЕНИЯ: два независимых способа
    /// вынуть площадь расходятся там на 6–22 %.
    ///
    /// Низ шкалы слабее: когерентное рассеяние в кристалле не выделено (вылет
    /// характеристического K-рентгена кристалла моделируется —
    /// `EfficiencySimulator.XrayEscape`, включён умолчанием). Поэтому штатная
    /// сетка начинается с 40 кэВ; считать ниже не запрещено — нижняя граница
    /// задаётся полем формы, и сетка до неё дотягивается, — но доверия к первым
    /// точкам меньше, чем к середине шкалы.
    /// </summary>
    /// <summary>Как разложены узлы сетки энергий.</summary>
    public enum EfficiencyGridMode
    {
        /// <summary>
        /// Штатная сетка <see cref="EfficiencyCalculation.DefaultEnergies"/>,
        /// обрезанная выбранным диапазоном.
        /// </summary>
        Standard,

        /// <summary>Равномерная по логарифму энергии, заданное число узлов.</summary>
        Logarithmic
    }

    /// <summary>
    /// Параметры расчёта кривой из геометрии — цена счёта и сетка, на которой
    /// он ведётся. Физики здесь нет намеренно: ключи переноса
    /// (<see cref="EfficiencySimulator"/>) калиброваны сверкой с Geant4 и новой
    /// TCCFCALC, и выведенные наружу свободными числами они превратили бы
    /// абсолютный уровень кривой в подгоночный.
    /// </summary>
    public sealed class EfficiencyCalculationOptions
    {
        /// <summary>
        /// Историй на узел кривой. Погрешность идёт как 1/√N: на 200 тысячах
        /// это около процента в середине шкалы и несколько процентов на её
        /// верху, где эффективность мала. Больше смысла имеет мало —
        /// систематика модели крупнее.
        /// </summary>
        public int Histories = 200000;

        /// <summary>
        /// Нижний край расчёта, кэВ. **Сорок → ПЯТЬ, решение Amber 23.08.2026
        /// (`E36`), тем же доводом, что и у сетки матрицы (`T49`).**
        ///
        /// ⛔ ЗАЧЕМ. `FsaEfficiency.Eval` ниже первой точки кривой ЗАЖИМАЕТ —
        /// отдаёт её значение, — и бьёт это сильнее, чем зажим матрицы: матрица
        /// задаёт ФОРМУ отклика, а вес линии в образе есть `I/100 × ε(E)`, и ε
        /// берётся здесь. Пока край стоял на 40 кэВ, каждой линии ниже
        /// выдавалась эффективность сорока килоэлектронвольт, тогда как
        /// настоящая на 9…30 кэВ близка к нулю.
        ///
        /// ⚠ Измерено 23.08.2026 по объявленным составам понятной части
        /// корпуса: ниже 40 кэВ лежат **419 линий с выходом ≥ 1 % у 62 спектров
        /// из 81** — три четверти части. Для сравнения, край матрицы (30 кэВ)
        /// давал 318 линий у 48.
        ///
        /// ⚠ Узлы ниже сорока НЕ вписаны в <see cref="EfficiencyCalculation.DefaultEnergies"/>
        /// нарочно: список стоит там, где стоит, по причине, и правка его дала
        /// бы другую сетку под тем же именем. Их достраивает уже существующий
        /// <see cref="Reach"/> — шагом первой пары (10 кэВ), — а самое резкое
        /// внизу, K-края веществ самой сцены, разрешает `BuildGrid(geometry)`
        /// (`E24`).
        /// </summary>
        public double MinEnergyKev = 5.0;

        public double MaxEnergyKev = 3000.0;

        public EfficiencyGridMode GridMode = EfficiencyGridMode.Standard;

        /// <summary>Узлов при логарифмической сетке; штатная считает их сама.</summary>
        public int NodeCount = 34;

        /// <summary>Потоков; 0 — по числу ядер минус один.</summary>
        public int Threads;

        /// <summary>Потоков на самом деле.</summary>
        public int EffectiveThreads
        {
            get
            {
                return this.Threads > 0
                    ? this.Threads
                    : Math.Max(1, Environment.ProcessorCount - 1);
            }
        }

        /// <summary>
        /// Узлы сетки, кэВ. Диапазон в полях — это диапазон СЧЁТА, и штатная
        /// сетка обязана его покрыть: узлы <see cref="EfficiencyCalculation.DefaultEnergies"/>
        /// внутри диапазона берутся как есть — они стоят там, где стоят, по
        /// причине (изгиб кривой внизу шкалы, рабочие линии), и раздвигать их
        /// значило бы получить другую сетку под тем же именем, — а за их краями
        /// сетка ПРОДОЛЖАЕТСЯ (см. <see cref="Reach"/>).
        ///
        /// Прежде диапазон штатную сетку только ОБРЕЗАЛ: выставленные 20 кэВ
        /// считались от 40, выставленные 5000 — до 3000, и увидеть это можно
        /// было только в журнале прогона, задним числом (E16). Число в поле,
        /// которым не считают, читается как обещание.
        ///
        /// Если внутри диапазона штатных узлов не осталось двух, сетка
        /// становится логарифмической: пустой ответ здесь хуже. Молча это не
        /// делается (`E17` «б») — строка журнала называет и подмену, и число
        /// узлов, которым посчитано, а клеймо кривой пишет `log`, а не `std`.
        /// </summary>
        public double[] BuildGrid()
        {
            return this.BuildGrid(null, null);
        }

        /// <summary>
        /// То же, но с ГЕОМЕТРИЕЙ: в сетку добавляются узлы вокруг K-краёв её
        /// собственных веществ (E24). Второй список, если он не пуст,
        /// заполняется строками для журнала — какой край чем разрешён.
        /// </summary>
        public double[] BuildGrid(GeometryModel geometry, List<string> notes)
        {
            EfficiencyGridMode used;
            return this.BuildGrid(geometry, notes, out used);
        }

        /// <summary>
        /// Наименьшая ширина диапазона счёта, кэВ (`E17` «а», решение Amber
        /// 02.09.2026). Вырожденный диапазон — верх не выше низа — разводить
        /// надо: логарифм от нулевой ширины даёт сетку из одной точки, а из неё
        /// кривой нет. Но разводить его надо ДОБАВКОЙ, а не долей от низа.
        ///
        /// ⛔ Прежде здесь стояло `Math.Max(lo * 1.01, MaxEnergyKev)`, то есть
        /// «не уже одного процента от нижней границы», и внизу шкалы это
        /// незаметно (5 кэВ → 0.05), а наверху раздвигает ЗАКОННЫЙ диапазон:
        /// выставленные 3000…3010 считались до 3030 — на два узла шире, чем
        /// стоит в поле, и увидеть это можно было только в журнале, задним
        /// числом. Килоэлектронвольт добавки не зависит от места на шкале: он
        /// меньше любой ширины пика и заведомо больше нуля.
        /// </summary>
        public const double MinSpanKev = 1.0;

        /// <summary>
        /// То же и с ответом, КАКОЙ сеткой посчитано (`E17` «б»). Заказанная
        /// сетка и посчитанная — разные вещи: штатная, не нашедшая внутри
        /// диапазона двух своих узлов, молча становится логарифмической. Так и
        /// задумано (пустой ответ хуже), но журнал и клеймо обязаны назвать ТУ,
        /// которой считали, — иначе испорчено ПРОИСХОЖДЕНИЕ кривой.
        /// </summary>
        public double[] BuildGrid(GeometryModel geometry, List<string> notes,
                                  out EfficiencyGridMode used)
        {
            double lo = Math.Max(1.0, this.MinEnergyKev);
            double hi = this.MaxEnergyKev;
            if (hi < lo + MinSpanKev)
            {
                // Раздвижка называет себя в журнале: число в поле, которым не
                // считают, читается как обещание (тот же довод, что в E16).
                if (notes != null)
                {
                    notes.Add(string.Format(CultureInfo.InvariantCulture,
                        Resources.EfficiencyMakerGridWidened, hi, lo, lo + MinSpanKev));
                }

                hi = lo + MinSpanKev;
            }

            used = this.GridMode;
            if (this.GridMode == EfficiencyGridMode.Standard)
            {
                List<double> picked = new List<double>();
                foreach (double energy in EfficiencyCalculation.DefaultEnergies)
                {
                    if (energy >= lo && energy <= hi)
                    {
                        picked.Add(energy);
                    }
                }

                if (picked.Count >= 2)
                {
                    // Дотянуть ДО вставки краёв: продолжение идёт шагом
                    // крайнего участка сетки, а узлы у края стоят вплотную —
                    // попав в этот шаг, они растянули бы сетку до границы
                    // сотнями узлов.
                    Reach(picked, lo, hi);
                    DensifyLow(picked);
                    AddEdges(picked, geometry, lo, hi, notes);
                    return picked.ToArray();
                }

                // Штатных узлов внутри меньше двух. Считается логарифмической —
                // и об этом ГОВОРИТСЯ: число узлов берётся при этом из поля
                // «Точек», запертого, пока выбрана штатная сетка, и человеку
                // неоткуда узнать, что считано именно им.
                used = EfficiencyGridMode.Logarithmic;
                if (notes != null)
                {
                    notes.Add(string.Format(CultureInfo.InvariantCulture,
                        Resources.EfficiencyMakerGridFallback, picked.Count, lo, hi,
                        Math.Max(2, this.NodeCount)));
                }
            }

            int n = Math.Max(2, this.NodeCount);
            List<double> grid = new List<double>(n);
            double logLo = Math.Log(lo), logHi = Math.Log(hi);
            for (int i = 0; i < n; i++)
            {
                grid.Add(Math.Exp(logLo + (logHi - logLo) * i / (n - 1)));
            }

            // Края нужны и логарифмической сетке: она реже штатной внизу шкалы,
            // и провал в разы попадает между её узлами тем более.
            DensifyLow(grid);
            AddEdges(grid, geometry, lo, hi, notes);
            return grid.ToArray();
        }

        /// <summary>
        /// ⛔ (`AMBER95`, П147 24.09.2026, физика 24) Верх участка, где сетка
        /// кривой гуще штатной: ниже 40 кэВ — первого узла
        /// <see cref="EfficiencyCalculation.DefaultEnergies"/>.
        /// </summary>
        public const double LowGridTopKev = 40.0;

        /// <summary>
        /// (`AMBER95`) Наибольшее отношение соседних узлов кривой ниже
        /// <see cref="LowGridTopKev"/> — ШАГ ШТАТНОЙ СЕТКИ МАТРИЦЫ
        /// (<see cref="ResponseMatrixOptions"/>: 140 узлов на 5…3000 кэВ,
        /// 1.0471), одно правило густоты для кривой и матрицы (решение Amber
        /// 12.09.2026 «одна физика для кривой и матрицы»). Считается из умолчаний
        /// матрицы, а не переписано числом: вторая копия разъехалась бы молча
        /// (`S37`).
        /// </summary>
        public static readonly double LowGridRatio = MatrixGridRatio();

        static double MatrixGridRatio()
        {
            ResponseMatrixOptions matrix = new ResponseMatrixOptions();
            double lo = Math.Max(1.0, matrix.MinEnergyKev);
            double hi = Math.Max(lo * 1.01, matrix.MaxEnergyKev);
            int n = Math.Max(2, matrix.NodeCount);
            return Math.Exp((Math.Log(hi) - Math.Log(lo)) / (n - 1));
        }

        /// <summary>
        /// ⛔ (`AMBER95`, П147 24.09.2026, физика 24) СГУСТИТЬ СЕТКУ НИЖЕ 40 кэВ:
        /// в каждый промежуток ниже <see cref="LowGridTopKev"/>, где соседние
        /// узлы отстоят больше чем в <see cref="LowGridRatio"/> раз, вставить
        /// узлы геометрически поровну.
        ///
        /// ЧТО БЫЛО НЕ ТАК. Штатная сетка начинается с 40 кэВ, ниже её
        /// достраивал <see cref="Reach"/> шагом первой пары, 10 кэВ: узлы 5, 10,
        /// 20, 30, 40. Эффективность там — пропускание корпуса и пробы
        /// `exp(−μt)` при μ ∝ E⁻³, её логарифм по ln E вогнут, и лог-лог хорда
        /// между узлами лежит НИЖЕ кривой. Замер П147 (`CurveGridProbeA95`,
        /// 400 тыс. историй, арбитр — узел ровно на энергии): лог-лог
        /// штатной против арбитра на 26.34 кэВ — 0.87 (RC-103 маринелли), 0.85
        /// (NaI 63×63, 5 см), 0.75 (NaI 80×80 впритык); на 17.14 — 0.28 / 0.15
        /// / 1.21; на 13.9 — 0.11 / 0.04 / 4.19. Беккерели по кривой на
        /// линиях Am-241 26.34 и L-рентгене Np — в разы мимо.
        /// </summary>
        static void DensifyLow(List<double> grid)
        {
            if (grid == null || grid.Count < 2)
            {
                return;
            }

            grid.Sort();
            double logRatio = Math.Log(LowGridRatio);
            List<double> added = new List<double>();
            for (int i = 1; i < grid.Count; i++)
            {
                double a = grid[i - 1], b = grid[i];
                if (!(a > 0.0) || a >= LowGridTopKev)
                {
                    continue;
                }

                // Промежуток, выходящий за 40 кэВ (у логарифмической сетки),
                // гущается по всей длине: его нижний конец уже в зоне.
                double span = Math.Log(b / a);
                int parts = (int)Math.Ceiling(span / logRatio - 1e-9);
                for (int k = 1; k < parts; k++)
                {
                    added.Add(Math.Exp(Math.Log(a) + span * k / parts));
                }
            }

            grid.AddRange(added);
            grid.Sort();
        }

        /// <summary>
        /// Насколько узел отступает от K-края, долей энергии (E24).
        ///
        /// Узел ставится ПАРОЙ по обе стороны края и НИКОГДА на сам край: на
        /// краю коэффициент ослабления разрывен, таблица XCOM держит там две
        /// точки, и число «на краю» — это выбор одной из них, то есть ответ на
        /// вопрос, которого никто не задавал. Пара по бокам разрывна честно:
        /// между этими двумя узлами кривая и падает ступенькой.
        ///
        /// Две десятых процента — это 0.13 кэВ на K-крае лютеция (63.3) и
        /// 0.02 кэВ на крае железа (7.1): меньше любой ширины пика и заведомо
        /// больше шага интерполяции таблиц.
        /// </summary>
        public const double EdgeOffset = 0.002;

        /// <summary>
        /// Во сколько раз ослабление должно скакнуть на крае, чтобы край
        /// считался заметным (E24). Десять процентов: ниже этого ступенька
        /// теряется в шуме розыгрыша, а лишний узел стоит полного прогона
        /// историй.
        ///
        /// Порог по СКАЧКУ, а не по массовой доле, нарочно: доля не говорит
        /// ничего, пока не известна энергия — у железа при 3 % массы край
        /// (7.1 кэВ) вне любой рабочей сетки, а у лютеция при той же доле
        /// край на 63.3 виден в разы.
        /// </summary>
        public const double EdgeStep = 1.10;

        /// <summary>
        /// Добавить узлы вокруг K-краёв веществ геометрии. Берутся ВСЕ вещества
        /// на пути кванта — проба, стенка сосуда, отражатель и корпус: край
        /// принадлежит веществу, а не пробе, и стенка из свинца ступит кривую
        /// ровно так же, как проба из лютеция.
        ///
        /// ⛔ (`AMBER95`, П147 24.09.2026, физика 24) И КРИСТАЛЛ ТОЖЕ. Прежде
        /// здесь стояло «его собственный край — это край СЧЁТА, а не
        /// пропускания, и он уже сидит в штатных узлах у своих иода и цезия» —
        /// неверно дважды: штатных узлов 33 и 36 кэВ нет (ниже 40 сетка шла 30,
        /// 40), а край счёта ступит кривую так же — над K-краем иода уходит
        /// флуоресценция (пик вылета), и эффективность в пике падает скачком.
        /// Замер П147 (`CurveGridProbeA95`, NaI 63×63 на 5 см): лог-лог
        /// штатной против арбитра на 33.3 кэВ — 1.14, на 34.0 — 1.12, на 35.0
        /// (Cs Kβ) — 1.10, на 32.2 (Ba Kα) — 0.93; у CsI маринелли 1.07 / 1.04 /
        /// 1.00 / 0.93. Правило то же, что у прочих веществ: пара ±0.2 %,
        /// скачок ослабления ≥ 10 %; сетка матрицы (`AddSampleEdges`) получает
        /// те же пары.
        /// </summary>
        /// <summary>
        /// То же правило снаружи — им пользуется сетка МАТРИЦЫ отклика (`E31`),
        /// где дыра ровно та же. Открыто, а не скопировано: две копии одного
        /// счёта разъезжаются молча (`S37`).
        /// </summary>
        public static void AddSampleEdges(List<double> grid, GeometryModel geometry,
                                          double lo, double hi, List<string> notes)
        {
            AddEdges(grid, geometry, lo, hi, notes);
        }

        static void AddEdges(List<double> grid, GeometryModel geometry,
                             double lo, double hi, List<string> notes)
        {
            if (geometry == null)
            {
                return;
            }

            // (`AMBER95`) Кристалл — ПЕРВЫМ: его края видны у всякой сцены.
            GeometryMaterial[] onPath = { geometry.Crystal, geometry.Source, geometry.BeakerWall,
                                          geometry.Reflector, geometry.Cladding };
            List<double> added = new List<double>();
            HashSet<int> seen = new HashSet<int>();

            foreach (GeometryMaterial material in onPath)
            {
                if (material == null || !(material.Density > 0.0))
                {
                    continue;
                }

                foreach (KeyValuePair<int, double> pair in material.Fractions)
                {
                    if (!(pair.Value > 0.0) || !seen.Add(pair.Key))
                    {
                        continue;
                    }

                    MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(pair.Key);
                    if (f == null || !(f.KEdgeKev > 0.0))
                    {
                        continue;
                    }

                    double below = f.KEdgeKev * (1.0 - EdgeOffset);
                    double above = f.KEdgeKev * (1.0 + EdgeOffset);
                    if (below <= lo || above >= hi)
                    {
                        continue;
                    }

                    double muBelow = material.LinearAttenuation(below);
                    double muAbove = material.LinearAttenuation(above);
                    if (!(muBelow > 0.0) || muAbove / muBelow < EdgeStep)
                    {
                        continue;
                    }

                    added.Add(below);
                    added.Add(above);
                    if (notes != null)
                    {
                        notes.Add(string.Format(CultureInfo.InvariantCulture,
                            Resources.EfficiencyMakerGridEdge, pair.Key, f.KEdgeKev,
                            muAbove / muBelow, material.Name));
                    }
                }
            }

            if (added.Count == 0)
            {
                return;
            }

            foreach (double energy in added)
            {
                // Узел вплотную к уже стоящему не заводится — тем же доводом,
                // что и в Reach: это та же точка, а стоит она полного прогона.
                bool near = false;
                foreach (double have in grid)
                {
                    if (Math.Abs(have - energy) < energy * EdgeOffset * 0.5)
                    {
                        near = true;
                        break;
                    }
                }

                if (!near)
                {
                    grid.Add(energy);
                }
            }

            grid.Sort();
        }

        /// <summary>
        /// Дотянуть штатную сетку до границ диапазона, не трогая её собственных
        /// узлов. Продолжение идёт ШАГОМ КРАЙНЕГО УЧАСТКА самой сетки (внизу
        /// это 10 кэВ, вверху 200), поэтому густота узлов на стыке не прыгает, а
        /// последней ставится сама граница — если шаг в неё не попал ровно.
        ///
        /// Узел вплотную к уже имеющемуся не заводится: граница ближе десятой
        /// доли шага — это та же точка, а лишний узел стоил бы полного прогона
        /// историй ради повторения соседа.
        /// </summary>
        static void Reach(List<double> picked, double lo, double hi)
        {
            // Оба шага сняты ДО вставок: вставка снизу сдвигает индексы, и
            // верхний шаг после неё пришлось бы искать заново.
            double lowStep = picked[1] - picked[0];
            double highStep = picked[picked.Count - 1] - picked[picked.Count - 2];

            for (double energy = picked[0] - lowStep; energy > lo; energy -= lowStep)
            {
                picked.Insert(0, energy);
            }

            if (picked[0] - lo > lowStep * 0.1)
            {
                picked.Insert(0, lo);
            }

            for (double energy = picked[picked.Count - 1] + highStep; energy < hi; energy += highStep)
            {
                picked.Add(energy);
            }

            if (hi - picked[picked.Count - 1] > highStep * 0.1)
            {
                picked.Add(hi);
            }
        }

        public EfficiencyCalculationOptions Clone()
        {
            return (EfficiencyCalculationOptions)this.MemberwiseClone();
        }
    }

    public static class EfficiencyCalculation
    {
        /// <summary>
        /// Сетка энергий кривой. Сгущена там, где кривая гнётся сильнее всего
        /// (низ шкалы), и вокруг рабочих линий 662, 1461, 2615 кэВ.
        /// </summary>
        public static readonly double[] DefaultEnergies =
        {
            40, 50, 60, 70, 80, 90, 100, 120, 150, 186, 240, 300, 352, 400, 460,
            510, 583, 609, 662, 720, 800, 900, 1000, 1120, 1250, 1461, 1600,
            1765, 2000, 2200, 2450, 2615, 2800, 3000
        };

        /// <summary>
        /// Прежний вход с одним числом историй — остальное по умолчанию.
        /// Держится ради проб (`tools/effmaker/probes`), у которых своего UI
        /// нет и настраивать им нечего.
        /// </summary>
        public static EfficiencyFitResult Run(GeometryModel geometry, int histories,
                                              Action<string> log, Func<bool> cancelled)
        {
            return Run(geometry, new EfficiencyCalculationOptions { Histories = histories },
                       log, cancelled);
        }

        /// <summary>
        /// Считает кривую по геометрии. <paramref name="log"/> получает строку
        /// на каждую точку, <paramref name="cancelled"/> опрашивается перед
        /// каждой точкой: одна точка — это десятки тысяч историй, и прервать
        /// счёт внутри неё нельзя. Точки считаются одновременно, в журнал они
        /// всё равно идут по возрастанию энергии.
        ///
        /// <paramref name="physics"/> (`E29`, П41) — настройки склада, от
        /// которых кривая берёт свои ключи физики; null — умолчания класса,
        /// то есть ровно то, чем считает приложение. Нужно ТОЛЬКО пробам:
        /// у кривой в UI рычагов физики нет намеренно, и этот вход их не
        /// заводит — он даёт пробе передать физику склада на путь кривой, не
        /// заводя второй копии правил (`S37`).
        ///
        /// <paramref name="importanceSampling"/> (`E29`, П43 13.09.2026) —
        /// ЯВНОЕ значение ключа розыгрыша точки вылета (проба `--imp=0|1`);
        /// null — автоматика <see cref="ImportanceSamplingFor"/>: полевая
        /// сцена — ВКЛ, прочие — умолчание склада. Явное сильнее автоматики в
        /// обе стороны, иначе абляцию «поле без розыгрыша» было бы не снять.
        /// </summary>
        public static EfficiencyFitResult Run(GeometryModel geometry,
                                              EfficiencyCalculationOptions options,
                                              Action<string> log, Func<bool> cancelled,
                                              ResponseMatrixOptions physics = null,
                                              bool? importanceSampling = null)
        {
            if (options == null)
            {
                options = new EfficiencyCalculationOptions();
            }

            if (log == null)
            {
                log = delegate { };
            }

            if (cancelled == null)
            {
                cancelled = () => false;
            }

            EfficiencyFitResult result = new EfficiencyFitResult();
            if (geometry == null)
            {
                result.Error = Resources.EfficiencyMakerNoGeometry;
                return result;
            }

            // (`AMBER185`) Отпечаток геометрии — В НАЧАЛЕ счёта, с той модели,
            // что считается: кривая несёт его в конфигурацию, и правка
            // геометрии после счёта видна как расхождение, а не молчит.
            result.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(geometry);

            if (!geometry.IsScintillator)
            {
                result.Error = Resources.EfficiencyMakerGeometryNotScintillator;
                return result;
            }

            // (`AMBER201`, Р4, мелочь 9.2) Боковая постановка у цилиндра —
            // отказ словами, как обещал `E21`. Прежде `FacingError` не читал
            // никто, и сцена строилась с переставленной обвязкой молча.
            string facingError = geometry.FacingError;
            if (!string.IsNullOrEmpty(facingError))
            {
                result.Error = facingError;
                return result;
            }

            int missingZ;
            if (!geometry.Crystal.IsKnown(out missingZ))
            {
                result.Error = string.Format(CultureInfo.InvariantCulture,
                                             Resources.EfficiencyMakerGeometryUnknownElement, missingZ);
                return result;
            }

            // (`AMBER201`, Р4, подозрение G) ...и у всех слоёв сцены, а не только
            // у кристалла: элемент вне таблиц выпадал из слоя молча.
            string unknownElement = geometry.UnknownElementProblem();
            if (unknownElement != null)
            {
                result.Error = unknownElement;
                return result;
            }

            // Сетка строится ПО ГЕОМЕТРИИ (E24): K-край вещества пробы или
            // обвязки роняет кривую в разы, а штатные узлы идут «…60, 70, 80…»
            // и край лежит между ними — нарисованная кривая ведёт прямую там,
            // где на деле ступенька.
            List<string> gridNotes = new List<string>();
            // Сетка отвечает и тем, КАКОЙ она вышла (`E17` «б»): заказанная
            // штатная становится логарифмической, если своих узлов внутри
            // диапазона у неё меньше двух, и журнал с клеймом обязаны назвать
            // посчитанную, а не заказанную.
            EfficiencyGridMode gridUsed;
            double[] energies = options.BuildGrid(geometry, gridNotes, out gridUsed);

            // ⛔ ОДНА ФИЗИКА ДЛЯ КРИВОЙ И МАТРИЦЫ — решение Amber 12.09.2026,
            // дословно: «Да — одна физика для кривой и матрицы». K-провал
            // кривой света (`F11` (а), П17: кривая электронов в коде +
            // раздельный оже-каскад EADL) кривая берёт ОТ УМОЛЧАНИЯ НАСТРОЕК
            // МАТРИЦЫ (`ResponseMatrixOptions.KDipLight`), тем же выражением,
            // что `ResponseMatrixBuilder.MakeSimulator`; до 12.09.2026 она
            // брала умолчание симулятора (ВЫКЛ) и ниже ~120 кэВ расходилась со
            // складом на глубину провала (1…3 %). Прочие ключи склада до
            // кривой не доезжают по делу: полубин без сетки не определён
            // (допуск — ПШПВ/2 из геометрии, `E34`), раскладка по каналам у
            // кривой отсутствует (реестр `tools/check_matrix_keys.py`, `SIM`).
            // Число и уровень — в клеймо кривой (`kdip=N` ниже), иначе кривая
            // с провалом была бы неотличима от кривой без него (`T42`).
            ResponseMatrixOptions storePhysics = physics ?? new ResponseMatrixOptions();
            // (`E29`, П43 13.09.2026) Розыгрыш точки вылета у кривой — ОДНИМ
            // правилом `ImportanceSamplingFor` (его же читает `CorpusEffProbe`
            // для гварда пересчёта, `S37`): явное значение пробы сильнее
            // всего; без него полевая сцена (земля, лунка) — ВКЛ по решению
            // Amber 13.09.2026 «ВКЛ автоматически для полевых сцен», прочие
            // сцены — умолчание склада (ВЫКЛ по решению 10.09.2026), то есть
            // сосуды считаются посимвольно и побитово как прежде.
            bool importance = ImportanceSamplingFor(geometry, storePhysics, importanceSampling);
            bool importanceAuto = !importanceSampling.HasValue && importance
                                  && !storePhysics.ImportanceSampling;
            EfficiencySimulator simulator = new EfficiencySimulator(geometry)
            {
                Histories = Math.Max(1000, options.Histories),
                // (`E29`, П41 13.09.2026) Важностный розыгрыш точки вылета —
                // у полевой сцены ВКЛ автоматикой (П43), у прочих от умолчания
                // настроек склада: у кривой полевой сцены ровно тот же разброс,
                // что у матрицы (26…56 % на узел), и без розыгрыша признак
                // разброса ниже (`NodeSpread`) кричит о шумной кривой.
                ImportanceSampling = importance,
                LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(storePhysics.KDipLight),
                LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(storePhysics.KDipLight),
                // (`M9`, П23 12.09.2026) Источник ω_L и переходы Костера—Кронига
                // — ТЕМ ЖЕ путём, что K-провал выше: от умолчания настроек
                // матрицы, чтобы кривая и склад считали одну физику. С
                // 13.09.2026 (физика 17, П37) умолчание 2 — кривая пошла
                // следом сама, как и было задумано.
                LYieldSupply = storePhysics.LYieldSupply,
                // (`A72`, П27 12.09.2026) Перенос электрона — тем же путём:
                // вылет электрона двигает ПИК, то есть саму кривую, и кривая
                // обязана считать его той же физикой, что склад. С 13.09.2026
                // (физика 17, П37) умолчание ВКЛ.
                ElectronTransport = storePhysics.ElectronTransport,
                // (`S126`/`S127`, П37 13.09.2026, физика 17) Пара и когерентное
                // в проводке — тем же путём. До 13.09.2026 кривая брала их
                // умолчанием СИМУЛЯТОРА (ВЫКЛ), и с включённым умолчанием
                // склада разошлась бы с ним на вылете 511 (пик и полная выше
                // порога пар) и на 1.5…1.8 % внизу шкалы у тонких кристаллов
                // (П30 §9.2). Умолчание живёт у настроек матрицы, кривая его
                // читает, а не держит копию (`S37`).
                PositronTransport = storePhysics.PositronTransport,
                PositronOffset = storePhysics.PositronOffset,
                RayleighToCrystal = storePhysics.RayleighToCrystal,
                // (`N4`/`F11` (г) и `M3`, П44 13.09.2026) Электрон в
                // произвольном веществе и тормозное вдоль пути — тем же
                // путём: от умолчания настроек склада (оба ВЫКЛ до единого
                // счёта физики 18), чтобы кривая и склад считали одну физику.
                ElectronAnyMaterial = storePhysics.ElectronAnyMaterial,
                BremAlongPath = storePhysics.BremAlongPath,
                // (`AMBER44`/`M12`, П94 17.09.2026) Перенос электрона в слоях
                // обвязки (занос и возврат) — тем же путём: от умолчания
                // настроек склада (ВЫКЛ до единого счёта физики 19), чтобы
                // кривая и склад считали одну физику.
                ElectronLayerTransport = storePhysics.ElectronLayerTransport,
                // (`M13`, П100 18.09.2026) Смешанная схема упругого рассеяния в
                // слоях обвязки — тем же путём: от умолчания настроек склада
                // (ВЫКЛ до решения Amber о едином счёте), чтобы кривая и склад
                // считали одну физику.
                ElectronLayerMixedScattering = storePhysics.ElectronLayerMixedScattering,
                // (`M13`, П106 19.09.2026) Тормозное электрона в слоях обвязки по
                // ходу переноса — тем же путём: от умолчания настроек склада
                // (ВЫКЛ до решения Amber о едином счёте), чтобы кривая и склад
                // считали одну физику.
                ElectronLayerBremAlongPath = storePhysics.ElectronLayerBremAlongPath,
                // (`M13`, П111 19.09.2026) Направление кванта тормозного в слоях
                // (2BS) — тем же путём, от умолчания настроек склада (ВЫКЛ до
                // решения Amber о едином счёте).
                ElectronLayerBremAngular2BS = storePhysics.ElectronLayerBremAngular2BS,
                // (`AMBER80`, П147 24.09.2026, физика 24) Пороговая форма пар —
                // тем же путём, от умолчания настроек склада (ВКЛ с физики 24,
                // решение Amber 24.09.2026): кривая и склад считают одно сечение.
                XcomPairThreshold = storePhysics.XcomPairThreshold,
            };

            log(geometry.Describe());

            // Предупреждения разбора идут в журнал ПЕРЕД сценой и числами:
            // всё, что ниже, посчитано с учётом того, о чём здесь сказано, и
            // прочитать это задним числом уже бесполезно.
            foreach (string warning in geometry.Warnings)
            {
                log(warning);
            }

            // (`E19`, решение Amber 01.09.2026: предупреждать, счёт разрешать.)
            // Проба, оставшаяся ВОЗДУХОМ при непустом сосуде, — это не «нет
            // данных», а систематическая ошибка в разы, и молчала она до сих
            // пор целиком: ни строки в журнале, ни следа в клейме, так что две
            // кривые, расходящиеся втрое, были неотличимы.
            bool sampleIsAir = geometry.SampleIsAir;
            if (sampleIsAir)
            {
                log(string.Format(CultureInfo.InvariantCulture,
                    Resources.EfficiencyMakerSampleIsAir,
                    geometry.Source == null || string.IsNullOrEmpty(geometry.Source.Name)
                        ? "-" : geometry.Source.Name,
                    geometry.Source == null ? 0.0 : geometry.Source.Density,
                    geometry.SampleHeightMm));
            }

            log(simulator.DescribeScene());

            // (`E29`, П43) Автоматика — вслух: человек за экраном «Посчитать из
            // геометрии» иначе не узнал бы, почему у полевой сцены кривая
            // несёт `imp=1`, а у сосуда — нет. Печатается ТОЛЬКО когда ключ
            // включила сцена, а не явное значение и не умолчание склада.
            if (importanceAuto)
            {
                log(Resources.EfficiencyMakerImportanceAuto);
            }

            log(string.Format(CultureInfo.InvariantCulture,
                "{0}: {1}; {2}: {3}; {4}",
                Resources.EfficiencyMakerCrossSections,
                simulator.UsesPartialCrossSections
                    ? Resources.EfficiencyMakerCrossSectionsPartial
                    : Resources.EfficiencyMakerCrossSectionsApprox,
                Resources.EfficiencyMakerBremsstrahlung,
                simulator.ElectronMaterialName.Length > 0
                    ? simulator.ElectronMaterialName
                    : Resources.EfficiencyMakerBremsstrahlungNoData,
                string.Format(CultureInfo.InvariantCulture,
                    Resources.EfficiencyMakerHistories, simulator.Histories)));

            // Сетка и потоки — в журнал вместе со всем прочим, чем посчитано:
            // кривая уходит в конфигурацию прибора одними числами, и по ней
            // самой уже не сказать, на скольких узлах она получена.
            log(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerGridSummary,
                              energies.Length, energies[0], energies[energies.Length - 1],
                              gridUsed == EfficiencyGridMode.Standard
                                  ? Resources.EfficiencyMakerGridStandard
                                  : Resources.EfficiencyMakerGridLogarithmic,
                              options.EffectiveThreads));
            foreach (string note in gridNotes)
            {
                log(note);
            }

            log("");

            // Точки кривой считаются ОДНОВРЕМЕННО. Они независимы полностью:
            // сцена и таблицы сечений у каждого счётчика свои и после сборки
            // только читаются, а поток случайных чисел задаётся номером точки
            // (см. ResetStream).
            //
            // Замер (ASN16 в маринелли, 34 точки по 200 000 историй, i7-11800H,
            // 8 ядер / 16 потоков): 171 с в один поток, 24.1 с в несколько —
            // ускорение 7.1x. Два прогона подряд совпадают до последнего знака.
            //
            // ⚠ (`A104`) «СОВПАДАЮТ ДО ПОСЛЕДНЕГО ЗНАКА» БЫЛО НЕВЕРНО ДО
            // 06.09.2026, и обещание это стоило одной приёмки (`E34`):
            // расхождение «база против правки» оказалось МЕНЬШЕ собственного
            // разброса пробы. Замер полосы П15 06.09.2026 (`EffDipProbe`,
            // `RC-103`, кривая «Кубик», 34 узла по 100 тыс. историй): до правки
            // четыре прогона одного двоичного файла разошлись по одному узлу из
            // 34, до 0.016 % (80 кэВ: 1.2915E-2 против 1.2917E-2); после правки
            // ВСЕ ПЯТНАДЦАТЬ пар из шести прогонов сошлись строка в строку.
            //
            // ⛔ Причина была НЕ здесь, и это важно тому, кто придёт сюда
            // чинить: не в общей на работников `GeometryModel` (её симулятор
            // только читает) и не в переиспользовании работника между точками,
            // а в статическом кэше `MaterialDatabase.photoShells` — доли
            // L-подоболочек помнились двумя полями и писались всеми потоками
            // вразнобой. Разбор — `MaterialDatabase.PhotoShellModel.Memo`.
            // Числа кривой от правки НЕ ИЗМЕНИЛИСЬ: после неё выходит ровно то
            // же, что давали неиспорченные прогоны до неё.
            //
            // Одно ядро по умолчанию оставлено интерфейсу: точек вчетверо
            // больше, чем ядер, на общем времени это не сказывается, а окно не
            // застывает. Число потоков можно задать в форме — результат от него
            // не зависит: зерно берётся от НОМЕРА точки, а не от порядка
            // выполнения (см. ResetStream ниже).
            double[] values = new double[energies.Length];
            double[] errors = new double[energies.Length];
            bool[] ready = new bool[energies.Length];
            object gate = new object();
            int printed = 0;

            // Культура выставлена на потоке счёта, а точки пойдут на потоках
            // пула — без переноса строки прогона взялись бы из нейтрального
            // ресурса вместо выбранного языка.
            CultureInfo ui = CultureInfo.CurrentUICulture;
            CultureInfo formatting = CultureInfo.CurrentCulture;
            ParallelOptions parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.EffectiveThreads,
            };

            // Точки раздаются ПО ОДНОЙ. Parallel.For сам режет диапазон кусками,
            // а точек всего 34 при цене в секунды: кусок из трёх точек означает,
            // что один поток работает, пока остальные ждут. С раздачей по одной
            // 28.0 с против 24.1 (замер на i7-11800H, 8 ядер).
            Parallel.ForEach(Partitioner.Create(0, energies.Length, 1), parallel,
                () =>
                {
                    Thread.CurrentThread.CurrentUICulture = ui;
                    Thread.CurrentThread.CurrentCulture = formatting;
                    return new EfficiencySimulator(geometry)
                    {
                        Histories = simulator.Histories,
                        Seed = simulator.Seed,
                        // Рабочий потока — копия головного симулятора и по
                        // физике света тоже: без этих двух строк K-провал
                        // стоял бы у головного, а считали бы рабочие без него.
                        LightSubKevCurve = simulator.LightSubKevCurve,
                        LightCascadeSplit = simulator.LightCascadeSplit,
                        LYieldSupply = simulator.LYieldSupply,
                        ElectronTransport = simulator.ElectronTransport,
                        PositronTransport = simulator.PositronTransport,
                        PositronOffset = simulator.PositronOffset,
                        RayleighToCrystal = simulator.RayleighToCrystal,
                        ImportanceSampling = simulator.ImportanceSampling,
                        ElectronAnyMaterial = simulator.ElectronAnyMaterial,
                        BremAlongPath = simulator.BremAlongPath,
                        ElectronLayerTransport = simulator.ElectronLayerTransport,
                        ElectronLayerMixedScattering = simulator.ElectronLayerMixedScattering,
                        ElectronLayerBremAlongPath = simulator.ElectronLayerBremAlongPath,
                        ElectronLayerBremAngular2BS = simulator.ElectronLayerBremAngular2BS,
                        XcomPairThreshold = simulator.XcomPairThreshold,
                    };
                },
                (range, loop, worker) =>
                {
                    if (cancelled())
                    {
                        loop.Stop();
                        return worker;
                    }

                    int index = range.Item1;
                    worker.ResetStream((ulong)worker.Seed
                                       ^ ((ulong)(index + 1) * 0x9E3779B97F4A7C15UL));

                    // Допуск пика — от разрешения прибора, если оно задано в
                    // геометрии: без него поправка на однократное рассеяние
                    // (SingleScatter) не даёт ничего, см. GeometryModel.FwhmAt662Percent.
                    worker.PeakHalfWidthKev = geometry.PeakHalfWidthKev(energies[index]);
                    double error;
                    double efficiency = worker.Efficiency(energies[index], out error);

                    lock (gate)
                    {
                        values[index] = efficiency;
                        errors[index] = error;
                        ready[index] = true;

                        // В журнал точки выливаются ПО ПОРЯДКУ, по мере того как
                        // готов очередной непрерывный кусок: считаются они
                        // вразнобой, а читать кривую вперемешку невозможно.
                        while (printed < ready.Length && ready[printed])
                        {
                            if (values[printed] > 0.0 && !double.IsNaN(values[printed]))
                            {
                                // Энергия с десятой долей: у логарифмической
                                // сетки узлы не круглые, и округление до целых
                                // печатало бы не ту энергию, на которой считано.
                                log(string.Format(CultureInfo.InvariantCulture,
                                    "    {0,9:F1} keV   eps = {1:E4}   +/- {2:F2} %",
                                    energies[printed], values[printed], errors[printed]));
                            }

                            printed++;
                        }
                    }

                    return worker;
                },
                worker => { });

            if (cancelled())
            {
                result.Error = Resources.EfficiencyMakerCancelled;
                return result;
            }

            for (int i = 0; i < energies.Length; i++)
            {
                if (!ready[i] || !(values[i] > 0.0) || double.IsNaN(values[i]))
                {
                    continue;
                }

                result.Curve.Add(new ROIEfficiencyData
                {
                    Energy = energies[i],
                    Efficiency = values[i],
                    ErrorPercent = errors[i],
                });
            }

            if (result.Curve.Count < 2)
            {
                result.Error = Resources.EfficiencyMakerGeometryNoCurve;
                return result;
            }

            result.MinEnergy = result.Curve[0].Energy;
            result.MaxEnergy = result.Curve[result.Curve.Count - 1].Energy;
            result.LevelSource = EfficiencyLevelSource.Simulation;

            // ⛔ (`E29`, П41 13.09.2026) ПРИЗНАК РАЗБРОСА НА УЗЕЛ — вслух, а не
            // строкой среди тридцати четырёх. До того полевая кривая
            // («детектор на земле») отдавалась с 26…56 % разброса на узел при
            // штатных 200 000 историй МОЛЧА, хотя описание умолчания обещает
            // «около процента» (П18 §3.3, ESS = 14 историй из 200 тысяч). Ниже
            // — сводка по всем узлам и, если типичный узел шумит выше порога,
            // предупреждение: счёт разрешается (решение Amber 01.09.2026 по
            // `E19` — «предупреждать, счёт разрешать»), но кривая названа
            // шумной здесь же, где напечатаны её узлы.
            EfficiencyNodeSpread spread = NodeSpread(result.Curve, simulator.Histories);
            // (`AMBER128`, П164) медиана и приговор — по рабочему диапазону,
            // низкая полоса — отдельной строкой.
            log(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerNodeSpread,
                              spread.MedianPercent, spread.WorstPercent, spread.WorstEnergy,
                              spread.WorstEss, simulator.Histories, spread.WorkFromKev, spread.Nodes));
            if (spread.LowNodes > 0)
            {
                log(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerNodeSpreadLow,
                                  spread.WorkFromKev, spread.LowNodes, spread.LowMedianPercent));
            }

            if (spread.Noisy)
            {
                log(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerNodeSpreadWarning,
                                  spread.MedianPercent, NodeSpreadWarnPercent, simulator.Histories,
                                  spread.NoisyNodes, spread.Nodes));
            }

            // Клеймо «чем посчитана» (E12): без него кривая в конфигурации
            // прибора неотличима от посчитанной другой физикой. Версия физики
            // переноса — та же константа, что у матрицы отклика: перенос один.
            // Формат инвариантный: клеймо хранится и сравнивается, а
            // локализованная строка расползалась бы по языкам.
            //
            // Имя сетки — ПОСЧИТАННОЙ (`E17` «б»), а не заказанной. Хвост
            // `; sample=air` пишется ТОЛЬКО у кривой с воздухом вместо пробы
            // (`E19`): на всех прочих сценах клеймо посимвольно прежнее, иначе
            // все посчитанные кривые разом объявились бы чужими.
            //
            // `; kdip=N` (12.09.2026, «одна физика для кривой и матрицы») —
            // тем же правилом `T42`, что у клейма матрицы: пишется ТОЛЬКО при
            // включённом K-провале, и с ним кривая с провалом отличима от
            // посчитанной до 12.09.2026 без него; у той клеймо посимвольно
            // прежнее. Разборщики клейма (`TryParseComputeStamp`,
            // `ResponseMatrix.PhysicsFromStamp`, `tools/check_curve_generation.py`)
            // читают свои куски по ключу и хвоста не замечают.
            //
            // `; norm=fluence` (`AMBER13` (б), 12.09.2026) — ТОЛЬКО у кривой
            // сцены изотропного поля: её значения — эффективная площадь в см²,
            // а не доля, и без этой строки такая кривая была бы неотличима от
            // обычной. Правило то же, что у матрицы (`ResponseMatrix.NormalizationOf`).
            // `; lys=N` (`M9`, П23 12.09.2026) — по тому же правилу, что
            // `kdip=`: только при ненулевом уровне, иначе кривая с поставкой ω_L
            // была бы неотличима от кривой без неё (`T42`).
            // `; etr=1` (`A72`, П27 12.09.2026) — по тому же правилу: только
            // при включённом переносе электрона.
            // `; e+tr=1; e+off=N` и `; rayl2=1` (`S126`/`S127`, П37 13.09.2026,
            // физика 17) — теми же именами, что у клейма матрицы
            // (`ResponseMatrix.ComputeStamp`), и по тому же правилу: пара —
            // только при включённом переносе позитрона, и тогда с обеими
            // половинами; когерентное в проводке — только включённое.
            // `; imp=1` (`E29`, П41 13.09.2026) — тем же именем, что у клейма
            // матрицы, и по тому же правилу: только при включённом важностном
            // розыгрыше точки вылета — ДЕЙСТВУЮЩЕМ (`importance`), а не по
            // умолчанию склада: у полевой сцены его включает автоматика (П43),
            // и клеймо обязано это нести; у сосуда — клеймо посимвольно прежнее.
            // `; ecomp=1` и `; bpath=N` (`N4`/`F11` (г), `M3`, П44 13.09.2026) —
            // теми же именами, что у клейма матрицы, только включёнными.
            // `; eltr=1` (`AMBER44`/`M12`, П94 17.09.2026) — тем же именем, что у
            // клейма матрицы, только включённым; с физики 19 (П97, 18.09.2026)
            // ключ ВКЛ умолчанием склада, и у кривой он в клейме всегда.
            // `; elmix=1` (`M13`, П100 18.09.2026) — тем же именем, что у клейма
            // матрицы, только включённым; с физики 20 (П103, 19.09.2026) ключ
            // ВКЛ умолчанием склада, и у кривой он в клейме всегда.
            // `; lbrem=1` (`M13`, П106 19.09.2026) — тем же именем, что у клейма
            // матрицы, только включённым; с физики 21 (П107, 19.09.2026) ключ
            // ВКЛ умолчанием склада, и у кривой он в клейме всегда.
            // `; lbang=1` (`M13`, П111 19.09.2026) — направление тормозного в слоях
            // 2BS, тем же именем, что у клейма матрицы, только включённым; с
            // физики 22 (П114, 19–21.09.2026) ключ ВКЛ умолчанием склада, и
            // у кривой он в клейме всегда.
            // `; pairth=1` (`AMBER80`, П147 24.09.2026) — пороговая форма пар,
            // тем же именем, что у клейма матрицы, только включённым; с физики
            // 24 ключ ВКЛ умолчанием склада, и у кривой он в клейме всегда.
            // `; mdb=<16 знаков>` (`S202`, П180, физика 25) — отпечаток содержимого
            // таблиц `matdb`, которые читает перенос
            // (`MaterialDatabase.SimulatorDataFingerprint`), тем же именем, что у
            // клейма матрицы; пишется всегда, последним куском.
            // `; peps=fwhm` (`S208`, П216 02.10.2026) — пик кривой окном полной
            // строки ±ПШПВ/2 (взвешенная ветвь тесным допуском + аналоговая
            // полоса, `EfficiencySimulator.CurvePeakResolutionWindow`), тем же
            // именем и значением, что определение ε_p в клейме матрицы
            // (`AMBER145`). Пишется ТОЛЬКО у геометрии с разрешением: без него
            // допуск ноль, окна нет и кривая посимвольно прежняя (`T42`). Без
            // куска кривая с окном была бы неотличима от прежней при той же
            // физике 26 — поколение `phys=` у кривой и матрицы общее, и поднять
            // его значило бы объявить чужим весь склад, чьё содержимое правка не
            // трогает.
            // `; bmscene=1` (`AMBER205`, П235 05.10.2026) — сцена сосуда полной
            // высоты (стенка цилиндра над пробой, стакан маринелли с дальним
            // донышком), тем же именем и по тому же правилу, что у клейма матрицы
            // (`EfficiencySimulator.VesselBeyondSample`): только там, где сцена
            // правда другая, иначе кривая посимвольно прежняя (`T42`).
            bool resolutionWindow = PeakWindowExpected(geometry);
            result.ComputeStamp = string.Format(CultureInfo.InvariantCulture,
                "phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}{6}{7}{8}{9}{10}{11}{12}{13}{14}{15}{16}{17}{18}{19}{20}{21}{22}{23}",
                ResponseMatrix.PhysicsVersion, simulator.Histories,
                result.MinEnergy, result.MaxEnergy, result.Curve.Count,
                gridUsed == EfficiencyGridMode.Standard ? "std" : "log",
                sampleIsAir ? "; sample=air" : "",
                storePhysics.KDipLight != 0
                    ? "; kdip=" + storePhysics.KDipLight.ToString(CultureInfo.InvariantCulture)
                    : "",
                storePhysics.LYieldSupply != 0
                    ? "; lys=" + storePhysics.LYieldSupply.ToString(CultureInfo.InvariantCulture)
                    : "",
                storePhysics.ElectronTransport ? "; etr=1" : "",
                storePhysics.PositronTransport
                    ? "; e+tr=1; e+off=" + (storePhysics.PositronOffset ? "1" : "0")
                    : "",
                storePhysics.RayleighToCrystal ? "; rayl2=1" : "",
                ResponseMatrix.NormalizationOf(geometry) == ResponseMatrixNormalization.PerUnitFluence
                    ? "; norm=fluence" : "",
                importance ? "; imp=1" : "",
                storePhysics.ElectronAnyMaterial ? "; ecomp=1" : "",
                storePhysics.BremAlongPath != 0
                    ? "; bpath=" + storePhysics.BremAlongPath.ToString(CultureInfo.InvariantCulture)
                    : "",
                storePhysics.ElectronLayerTransport ? "; eltr=1" : "",
                storePhysics.ElectronLayerMixedScattering ? "; elmix=1" : "",
                storePhysics.ElectronLayerBremAlongPath ? "; lbrem=1" : "",
                storePhysics.ElectronLayerBremAngular2BS ? "; lbang=1" : "",
                storePhysics.XcomPairThreshold ? "; pairth=1" : "",
                resolutionWindow ? "; " + PeakWindowStamp : "",
                VesselStampExpected(geometry) ? "; " + VesselStamp : "",
                "; mdb=" + MaterialDatabase.SimulatorDataFingerprint());
            return result;
        }

        /// <summary>
        /// Розыгрыш точки вылета, которым СЧИТАЕТСЯ кривая (`E29`, П43
        /// 13.09.2026) — одно правило на путь кривой и на гвард пересчёта
        /// `CorpusEffProbe` (`S37`: второй копии правила быть не должно).
        /// Порядок, сверху вниз, берётся первое:
        /// 1. <paramref name="explicitValue"/> задано (проба `--imp=0|1`) —
        ///    оно, в обе стороны: «ВЫКЛ на поле» нужен абляции, «ВКЛ на сосуде»
        ///    — замеру безвредности (П41 §3.5);
        /// 2. сцена полевая (<see cref="GeometryScenes.IsField"/>) — ВКЛ:
        ///    решение Amber 13.09.2026, дословно «ВКЛ автоматически для
        ///    полевых сцен (Ground/Borehole)»;
        /// 3. иначе — умолчание склада <see cref="ResponseMatrixOptions.ImportanceSampling"/>
        ///    (ВЫКЛ, решение 10.09.2026): сосуды и точечные сцены — как прежде.
        ///
        /// ⛔ Путь МАТРИЦЫ (`ResponseMatrixBuilder.MakeSimulator`) этого правила
        /// не читает и автоматики не получает — решение Amber покрывало кривую;
        /// склад считается ключом и только им.
        /// </summary>
        public static bool ImportanceSamplingFor(GeometryModel geometry,
                                                 ResponseMatrixOptions physics,
                                                 bool? explicitValue)
        {
            if (explicitValue.HasValue)
            {
                return explicitValue.Value;
            }

            if (GeometryScenes.IsField(geometry))
            {
                return true;
            }

            return (physics ?? new ResponseMatrixOptions()).ImportanceSampling;
        }

        /// <summary>
        /// Порог разброса ТИПИЧНОГО узла (медианы по кривой), выше которого
        /// кривая называется шумной (`E29`, П41). Пять процентов — тот же
        /// порог, что у предупреждения формы матрицы о шуме континуума
        /// (`T15`): описание умолчания <see cref="EfficiencyCalculationOptions.Histories"/>
        /// обещает «около процента в середине шкалы», сосудная сцена корпуса
        /// даёт на 200 000 историй 1.5 % (П18), полевая — 26…56 %. Медиана,
        /// а не худший узел, нарочно: верх шкалы у мелкого кристалла шумит
        /// на десятки процентов по одной статистике (ε ~ 1e-4 на 3 МэВ), и
        /// это описание обещает («несколько процентов на её верху»);
        /// шумная КРИВАЯ — та, у которой шумит середина.
        /// </summary>
        public const double NodeSpreadWarnPercent = 5.0;

        /// <summary>
        /// ⛔ (`S208`, П216 02.10.2026; решение Amber 02.10.2026 вопросником,
        /// дословно: «Научить вкладку и сторожа peps=fwhm (Рекомендую)») КУСОК
        /// КЛЕЙМА «пик кривой окном полной строки ±ПШПВ/2»
        /// (<see cref="EfficiencySimulator.CurvePeakResolutionWindow"/>). Одно
        /// место истины: его печатает клеймо кривой (<see cref="Run"/>), его ищут
        /// подпись вкладки «Эффективность» (`DeviceConfigForm.GenerationNotes`),
        /// гвард пересчёта корпуса (`CorpusEffProbe`) и сторож
        /// `tools/check_curve_generation.py`.
        ///
        /// Зачем отдельный признак, а не поколение. `phys=` кривой — это
        /// <see cref="ResponseMatrix.PhysicsVersion"/>, общий с матрицей; правка
        /// пика кривой матрицу не трогает, и поднять номер значило бы объявить
        /// чужим весь склад. Кривая физики 26 без куска у геометрии с
        /// разрешением посчитана прежним определением пика — на 32…80 кэВ ниже
        /// на 1…3 %, выше K-края вещества пробы — в разы.
        /// </summary>
        public const string PeakWindowStamp = "peps=fwhm";

        /// <summary>
        /// (`S208`) Обязана ли кривая этой геометрии нести
        /// <see cref="PeakWindowStamp"/>: да — у геометрии с разрешением (допуск
        /// пика ПШПВ/2 больше нуля). Без разрешения допуск ноль, окна нет, и
        /// кривая посимвольно прежняя.
        /// </summary>
        public static bool PeakWindowExpected(GeometryModel geometry)
        {
            return geometry != null && geometry.FwhmAt662Percent > 0.0;
        }

        /// <summary>
        /// (`AMBER205`, П235) Кусок клейма «сцена сосуда полной высоты» — тот же,
        /// что у клейма матрицы (`ResponseMatrix.ComputeStamp`).
        /// </summary>
        public const string VesselStamp = "bmscene=1";

        /// <summary>
        /// (`AMBER205`) Обязана ли кривая этой геометрии нести
        /// <see cref="VesselStamp"/>: да — где сцена сосуда отличается от прежней
        /// (<see cref="EfficiencySimulator.VesselBeyondSample"/>). У прочих кривая
        /// посимвольно прежняя.
        /// </summary>
        public static bool VesselStampExpected(GeometryModel geometry)
        {
            return EfficiencySimulator.VesselBeyondSample(geometry);
        }

        /// <summary>
        /// Разброс кривой по узлам (`E29`, П41): медиана и худший узел по
        /// `ErrorPercent`, действующая выборка худшего
        /// (`ESS = n / (1 + n·δ²)` — та же формула, что у `SceneCostProbe`),
        /// число узлов выше порога и приговор «шумная» по медиане. Правило
        /// ОДНО — им пользуются и журнал расчёта, и `CorpusEffProbe`: второе
        /// правило для одной величины разъехалось бы молча (`S37`).
        ///
        /// ⛔ (`AMBER128`, П164 28.09.2026) Медиана и счёт шумных узлов — по
        /// РАБОЧЕМУ ДИАПАЗОНУ, узлам от <see cref="EfficiencyCalculationOptions.LowGridTopKev"/> и выше.
        /// После сгущения сетки ниже 40 кэВ (`AMBER95`) там 41–51 узел из
        /// 77–87, и медиана всех узлов стала медианой низкой полосы, где узел
        /// шумит по природе (ε падает на порядки к 5 кэВ): маринелли RC-103
        /// на 200 000 историй — 3.98 % по всем узлам при 2.2 % на узлах от
        /// 40 кэВ, `RC103_lu_front` — 5.47 % («шумная», кривая корпуса не
        /// записана) при ≈ 2.7 %. Низкая полоса печатается отдельно
        /// (<see cref="EfficiencyNodeSpread.LowMedianPercent"/>); худший узел —
        /// по всей кривой, как и был. Кривая без узлов от 40 кэВ судится по
        /// всем своим узлам — иначе судить нечем.
        /// </summary>
        public static EfficiencyNodeSpread NodeSpread(IList<ROIEfficiencyData> curve, int histories)
        {
            var spread = new EfficiencyNodeSpread();
            if (curve == null || curve.Count == 0)
            {
                return spread;
            }

            var work = new List<double>();
            var low = new List<double>();
            var all = new List<double>();
            foreach (ROIEfficiencyData point in curve)
            {
                double e = point.ErrorPercent;
                if (double.IsNaN(e) || double.IsInfinity(e))
                {
                    continue;
                }

                all.Add(e);
                if (point.Energy >= EfficiencyCalculationOptions.LowGridTopKev)
                {
                    work.Add(e);
                }
                else
                {
                    low.Add(e);
                }

                if (e > spread.WorstPercent)
                {
                    spread.WorstPercent = e;
                    spread.WorstEnergy = point.Energy;
                }
            }

            if (all.Count == 0)
            {
                return spread;
            }

            // рабочий диапазон пуст — кривая целиком ниже 40 кэВ: судится вся
            List<double> judged = work.Count > 0 ? work : all;
            spread.WorkFromKev = work.Count > 0 ? EfficiencyCalculationOptions.LowGridTopKev : 0.0;
            spread.Nodes = judged.Count;
            foreach (double e in judged)
            {
                if (e > NodeSpreadWarnPercent)
                {
                    spread.NoisyNodes++;
                }
            }

            spread.MedianPercent = Median(judged);
            spread.LowNodes = work.Count > 0 ? low.Count : 0;
            spread.LowMedianPercent = spread.LowNodes > 0 ? Median(low) : double.NaN;
            double n = Math.Max(1, histories);
            double delta = spread.WorstPercent / 100.0;
            spread.WorstEss = n / (1.0 + n * delta * delta);
            spread.Noisy = spread.MedianPercent > NodeSpreadWarnPercent;
            return spread;
        }

        static double Median(List<double> values)
        {
            var sorted = new List<double>(values);
            sorted.Sort();
            return sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : 0.5 * (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]);
        }
    }

    /// <summary>Разброс кривой по узлам — выход <see cref="EfficiencyCalculation.NodeSpread"/>.</summary>
    public sealed class EfficiencyNodeSpread
    {
        /// <summary>
        /// Медиана `ErrorPercent` по узлам РАБОЧЕГО ДИАПАЗОНА (от
        /// <see cref="WorkFromKev"/>), % (`AMBER128`).
        /// </summary>
        public double MedianPercent;

        /// <summary>
        /// Нижняя граница рабочего диапазона, кэВ: <see cref="EfficiencyCalculationOptions.LowGridTopKev"/>;
        /// ноль — узлов от неё нет, судились все узлы (`AMBER128`).
        /// </summary>
        public double WorkFromKev;

        /// <summary>
        /// Низкая полоса (ниже <see cref="WorkFromKev"/>) — отдельно: медиана, %,
        /// и число узлов; NaN и ноль, если полосы нет (`AMBER128`).
        /// </summary>
        public double LowMedianPercent = double.NaN;

        /// <summary>Узлов в низкой полосе (`AMBER128`).</summary>
        public int LowNodes;

        /// <summary>Худший узел по ВСЕЙ кривой: его разброс, %, энергия, кэВ, и действующая выборка, историй.</summary>
        public double WorstPercent, WorstEnergy, WorstEss;

        /// <summary>
        /// Узлов рабочего диапазона выше порога <see cref="EfficiencyCalculation.NodeSpreadWarnPercent"/>
        /// и всего узлов рабочего диапазона.
        /// </summary>
        public int NoisyNodes, Nodes;

        /// <summary>Кривая шумная: медиана выше порога.</summary>
        public bool Noisy;
    }
}
