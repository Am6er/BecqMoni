using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;

namespace BecquerelMonitor
{
    /// <summary>
    /// ⛔ ПРАВИЛО «КАНАЛ ПЕРЕПОЛНЕНИЯ», названо вслух 05.09.2026 (`A203`).
    ///
    /// **Что это.** Канал переполнения — КРАЙНИЙ канал шкалы АЦП (нулевой либо
    /// последний), в который прибор сбрасывает всё, что вне шкалы: события выше
    /// верхнего предела — в последний, отсечённые порогом — в нулевой. Энергии
    /// у такой структуры нет: она собрана из событий разных энергий и не
    /// принадлежит ни одному диапазону. Ни в дозу, ни в разложение ей давать
    /// нечего.
    ///
    /// **Откуда это известно — и почему НЕ «последний».** Свойство это
    /// ПРИБОРНОЕ (устройство АЦП и прошивка), а не свойство места в массиве. Ни
    /// один из читаемых форматов и ни одна конфигурация прибора признака не
    /// несут: у <see cref="EnergySpectrum"/> такого поля нет вовсе, у
    /// <c>DeviceConfigInfo</c> — тоже. Поэтому канал опознаётся ПО СОДЕРЖИМОМУ,
    /// и правило одно на оба конца шкалы.
    ///
    /// ⛔ Прежде правило звучало «последний канал не считать НИКОГДА» и жило
    /// умолчанием: у <see cref="DoseRateManager"/> верхняя граница зажималась
    /// в <c>Length − 1</c> при полуоткрытом цикле, то есть последний канал был
    /// недостижим независимо от того, переполнение в нём или обычные отсчёты.
    /// В `FullSpectrumAnalysis` и `tools/pie` то же допущение сделано БЕЗУСЛОВНО
    /// («последний канал АЦП — канал переполнения»). Замер по корпусу
    /// 05.09.2026 показывает, что безусловным оно быть не может: переполнение в
    /// последнем канале есть у 28 спектров из 129, а у остальных 101 последний
    /// канал обычный (у 4000-канального GS4000, у германия, у G1S16/G1S24, у
    /// ASN8 — ноль или единицы отсчётов на уровне соседей). Разброс приборный:
    /// AS80 / ASN16 / AS1Pro / RC-103 переполнение пишут, ASN8 / GS4000 / HPGe /
    /// LaBr / OBS / RC-101 — нет.
    ///
    /// **Критерий.** Пусть v — содержимое крайнего канала, m — медиана
    /// <see cref="NeighbourWindow"/> ближайших каналов ВНУТРИ шкалы. Канал
    /// объявляется переполнением, когда выполнены ОБА условия:
    ///
    ///   * кратность: v ≥ <see cref="MinRatio"/> · max(m, 1) — переполнение
    ///     собирает весь хвост распределения, оно не «немного больше соседей»;
    ///   * значимость: v − m ≥ <see cref="MinSigma"/>·√(m + 1) — чтобы редкий
    ///     одиночный отсчёт на пустом хвосте не объявлялся переполнением.
    ///
    /// ⚠ Отдельного абсолютного пола НЕ заводится: на пустом хвосте (m = 0)
    /// кратность требует ровно <see cref="MinRatio"/> отсчётов, и это он и есть.
    ///
    /// **Как разделился корпус (129 спектров, замер 05.09.2026).** Принято 28,
    /// отвергнут 101. Наименьшее принятое — 32 отсчёта при медиане соседей 0
    /// (`RC103_Lu176`); наибольшее отвергнутое — 127 при медиане 196.5
    /// (`G1S24_Th228_P5`, хвост живой, кратность требует 3930); ближайший промах
    /// — 11 при медиане 0 (`RC103_Cs137_0cm`, порог 20). Между принятыми и
    /// отвергнутыми лежит полтора десятичных порядка, так что числа
    /// <see cref="MinRatio"/> и <see cref="MinSigma"/> корпусом не подгоняются:
    /// сдвиг любого из них вдвое разбиения не меняет.
    ///
    /// **Нулевой канал.** Правило одно на оба конца, и у нулевого канала оно
    /// проверяется тем же кодом. На корпусе оно не срабатывает НИ РАЗУ: у 129
    /// спектров нулевой канал либо пуст, либо на порядки НИЖЕ соседей (порог
    /// АЦП режет низ — `ASN8_Th232_1024`: 0 при медиане 91127). То есть в
    /// корпусе такого прибора нет, и живого подтверждения у этой половины
    /// правила НЕТ — она проверена положительным контролем в `DoseRateProbe`
    /// (спектр с насыпанным нулевым каналом), а не измерением.
    /// </summary>
    public static class OverflowChannel
    {
        /// <summary>Сколько соседей ВНУТРИ шкалы задают местный уровень.</summary>
        public const int NeighbourWindow = 32;

        /// <summary>Во сколько раз крайний канал обязан превзойти местный уровень.</summary>
        public const double MinRatio = 20.0;

        /// <summary>На сколько пуассоновских сигм — чтобы не судить по одиночному отсчёту.</summary>
        public const double MinSigma = 8.0;

        /// <summary>
        /// Судить можно только о КРАЙНЕМ канале, и только когда соседей
        /// набирается хотя бы столько. На спектре короче судить не по чему, и
        /// правило честно отвечает «не переполнение».
        /// </summary>
        public const int MinNeighbours = 4;

        /// <summary>
        /// Переполнение ли этот канал. Для не-крайнего канала всегда false:
        /// вопрос о переполнении в середине шкалы не имеет смысла.
        /// </summary>
        public static bool IsOverflow(int[] spectrum, int channel)
        {
            if (spectrum == null)
            {
                return false;
            }

            int n = spectrum.Length;
            if (n < MinNeighbours + 2 || (channel != 0 && channel != n - 1))
            {
                return false;
            }

            int window = Math.Min(NeighbourWindow, n - 2);
            if (window < MinNeighbours)
            {
                return false;
            }

            var neighbours = new double[window];
            for (int k = 0; k < window; k++)
            {
                neighbours[k] = channel == 0 ? spectrum[1 + k] : spectrum[n - 2 - k];
            }

            Array.Sort(neighbours);
            double m = window % 2 == 1
                ? neighbours[window / 2]
                : 0.5 * (neighbours[window / 2 - 1] + neighbours[window / 2]);

            double v = spectrum[channel];
            return v >= MinRatio * Math.Max(m, 1.0)
                   && v - m >= MinSigma * Math.Sqrt(m + 1.0);
        }

        /// <summary>
        /// Карта каналов, которым нечего давать в счёт. Длина — как у спектра;
        /// true стоит не более чем у двух крайних каналов.
        /// </summary>
        public static bool[] Mask(int[] spectrum)
        {
            var mask = new bool[spectrum == null ? 0 : spectrum.Length];
            if (mask.Length == 0)
            {
                return mask;
            }

            mask[0] = IsOverflow(spectrum, 0);
            mask[mask.Length - 1] = IsOverflow(spectrum, mask.Length - 1);
            return mask;
        }
    }

    // Token: 0x02000032 RID: 50
    /// <summary>
    /// Мощность амбиентного эквивалента дозы по спектру — от кривой
    /// эффективности, ВЫБРАННОЙ НА ПАНЕЛИ (`AMBER18`, задача Amber 11.09.2026).
    ///
    /// ⛔ ДО 12.09.2026 здесь считали по ручным точкам калибровки
    /// (`DoseRateConfig.DoseRateCalibrationPoints`, чувствительность
    /// «доза на отсчёт» на диапазон, нормированная на эталон). Точки, эталон,
    /// вкладка `Dose Rate` и сам `DoseRateConfig` сняты решениями Amber
    /// 10–11.09.2026. Теперь вход один — <see cref="DoseRateInput"/>: полная
    /// либо пиковая эффективность и геометрический множитель сцены, — а
    /// единицы явные (<see cref="DoseRateCoefficients.DoseRatePerFluenceRate"/>).
    ///
    /// Расчёт по диапазонам геометрической сетки (<see cref="DoseRateEstimator.BuildGrid"/>)
    /// на пересечении шкалы спектра, области входа и области коэффициентов:
    ///
    ///     N_i  = cps_i / ε(E_i)                 квантов/с, испущенных источником в 4π
    ///     φ̇_i  = N_i · G                        квант/(см²·с) в центре кристалла
    ///     Ḣ_i  = φ̇_i · Ḣ*(10)/φ̇ (E_i)           мкЗв/ч
    ///
    /// где ε — полная (сумма строки матрицы) или пиковая (кривая, «≈»),
    /// G — <see cref="DoseRateInput.FluencePerPhoton"/>, E_i — ПРЕДСТАВИТЕЛЬНАЯ
    /// энергия диапазона: по центру тяжести приписанных ему отсчётов
    /// (`AMBER77`, П145 24.09.2026; до того — середина, и показание уходило
    /// на −1.5…+5.3 % на Cs-137 и до +34 % на линиях у края диапазона).
    /// Остаётся приближением: две линии в одном диапазоне делятся на ε одной
    /// общей энергии, между ними.
    ///
    /// С матрицей строка берётся В РАЗРЕШЕНИИ прибора (калибровка ширины
    /// спектра), и отсчёты делятся между линиями всех диапазонов совместным
    /// решением — хвост пика у границы уходит и вниз, и вверх (`S185`, П153
    /// 24.09.2026; прежде — строка без разрешения и вычитание сверху вниз,
    /// линия у границы диапазона ошибалась до +9.7 %).
    ///
    /// Сцена поля `ISO` (`AMBER13` (б), 12.09.2026) идёт ТЕМ ЖЕ ходом: там ε —
    /// эффективная площадь A_эфф(E) в см² (на квант/см² поля), G ≡ 1, и первая
    /// строка сразу даёт φ̇_i = cps_i / A_эфф(E_i). Развёртка по матрице та же
    /// (строка матрицы поля — тоже отклик на линию, только в см²), пометка
    /// «≈» — та же (только кривая, A_пик). Признак нормировки сверяется при
    /// сборке входа (<see cref="DoseRateInput.Of"/>), здесь он уже сошёлся.
    /// </summary>
    public class DoseRateManager
    {
        private GlobalConfigManager globalConfigManager;
        public DoseRateManager(GlobalConfigManager globalConfigManager)
        {
            this.globalConfigManager = globalConfigManager;
        }

        /// <summary>
        /// Доза по спектру и кривой, выбранной у него на панели
        /// (<c>ResultData.Efficiency</c>). null — показывать НЕЧЕГО: кривая не
        /// выбрана или у неё нет точек (решение «кривая не выбрана — пусто»);
        /// всё остальное — число либо отказ с причиной в <see cref="DoseRate.Refusal"/>.
        /// </summary>
        public DoseRate Calculate(ResultData resultData)
        {
            if (resultData == null)
            {
                return null;
            }

            EfficiencyConfigData efficiency = resultData.Efficiency;
            if (efficiency == null || !efficiency.HasCurve)
            {
                return null;
            }

            DoseRateInput input;
            try
            {
                input = this.InputOf(efficiency);
            }
            catch (DoseRateRefusalException ex)
            {
                DoseRate refused = new DoseRate();
                refused.Refusal = ex.Message;
                return refused;
            }

            return this.Calculate(resultData, input);
        }

        /// <summary>
        /// Вход по кривой из склада матриц приложения — ТЕМ ЖЕ путём, что и
        /// разбор FSA (<c>FsaAnalysisSession</c>): матрица читается, только
        /// если у кривой есть геометрия и не снята галка «пускать матрицу»
        /// (`W11`), и берётся, только если её клеймо сходится с геометрией
        /// кривой. Не сошлось, не прочиталась, нет файла — считаем по пиковой
        /// с пометкой; причина остаётся в <see cref="LastMatrixNote"/>.
        ///
        /// Кэш — по ссылке на кривую и по отметке файла матрицы: строка
        /// состояния перерисовывается каждые 200 мс при наборе, а матрица —
        /// полмегабайта разбора. Пересчитанная в форме матрица меняет отметку
        /// файла, и кэш это видит.
        /// </summary>
        public DoseRateInput InputOf(EfficiencyConfigData efficiency)
        {
            if (efficiency == null)
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateNoEfficiency", "Dose rate: no efficiency curve is selected."));
            }

            string stamp = MatrixStamp(efficiency);
            lock (this.cacheSync)
            {
                if (object.ReferenceEquals(this.cachedEfficiency, efficiency)
                    && this.cachedInput != null && this.cachedStamp == stamp)
                {
                    return this.cachedInput;
                }
            }

            ResponseMatrix matrix = null;
            string note = "";
            if (efficiency.HasGeometry && efficiency.UseResponseMatrix)
            {
                MatrixRefusal refusal;
                int fileFormat;
                matrix = ResponseMatrixStore.Load(efficiency.Guid, out refusal, out fileFormat);
                if (matrix == null)
                {
                    note = refusal == MatrixRefusal.OldFormat
                        ? string.Format(CultureInfo.InvariantCulture, "matrix file format {0}, need {1}",
                                        fileFormat, ResponseMatrix.FormatVersion)
                        : refusal.ToString();
                }
                else if (!matrix.IsValidFor(efficiency.Geometry))
                {
                    note = "matrix stamp does not match the geometry";
                    matrix = null;
                }
            }
            else if (!efficiency.HasGeometry)
            {
                note = "no geometry";
            }
            else
            {
                note = "UseResponseMatrix = false";
            }

            DoseRateInput input = DoseRateInput.Of(efficiency, matrix);
            lock (this.cacheSync)
            {
                this.cachedEfficiency = efficiency;
                this.cachedInput = input;
                this.cachedStamp = stamp;
                this.lastMatrixNote = note;
            }

            return input;
        }

        /// <summary>Почему у последнего входа нет матрицы; пусто — матрица есть.</summary>
        public string LastMatrixNote
        {
            get
            {
                lock (this.cacheSync)
                {
                    return this.lastMatrixNote ?? "";
                }
            }
        }

        /// <summary>
        /// Отметка всего, от чего зависит вход, кроме самой ссылки на кривую:
        /// файл матрицы (длина и время), галка «пускать матрицу», наличие
        /// геометрии и Guid. Галка правится НА ТОМ ЖЕ объекте (форма «Матрица
        /// отклика»), и без неё в отметке кэш отдавал бы вход с матрицей после
        /// того, как её выключили, — поймано пробой `DoseRateFromCurveProbe` §7.
        /// </summary>
        static string MatrixStamp(EfficiencyConfigData efficiency)
        {
            string file;
            try
            {
                string path = ResponseMatrixStore.PathOf(efficiency.Guid);
                if (!File.Exists(path))
                {
                    file = "-";
                }
                else
                {
                    FileInfo info = new FileInfo(path);
                    file = string.Format(CultureInfo.InvariantCulture, "{0}:{1}",
                                         info.Length, info.LastWriteTimeUtc.Ticks);
                }
            }
            catch (Exception)
            {
                file = "?";
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}",
                                 file, efficiency.UseResponseMatrix, efficiency.HasGeometry, efficiency.Guid);
        }

        /// <summary>
        /// Каналы диапазона [<paramref name="fromE"/>, <paramref name="toE"/>):
        /// полуоткрыто, `[start, end)`, зажато в длину спектра.
        ///
        /// ⛔ (`AMBER101`, П145 24.09.2026) КАНАЛ ПРИНАДЛЕЖИТ ДИАПАЗОНУ ПО СВОЕМУ
        /// ЦЕНТРУ: номер канала — его центр (`AMBER73`; у разбора FSA
        /// `centres[i] = ChannelToEnergy(i)`), значит канал `i` в диапазоне,
        /// если `E(i) ∈ [fromE, toE)`, то есть `i ∈ [ceil(x_низ), ceil(x_верх))`.
        /// Прежнее отбрасывание дробной части `(int)x` судило канал по точке
        /// «центр + 1 канал»: на каждой границе канал с центром НИЖЕ неё уходил
        /// в верхний диапазон, и у сетки, чьи бины матрицы судятся по центру
        /// (`AMBER71`), расхождение сторон стало целым каналом (RC-103 на 1024
        /// канала — 2.93 кэВ). Одно правило на обоих концах (`C4`, `W19`)
        /// остаётся: и низ, и верх — `ceil`.
        ///
        /// ⛔ Зажим В ДЛИНУ, а не в «длина − 1» (`A203`): цикл потребителя
        /// полуоткрыт, и `Length − 1` делал последний канал недостижимым при
        /// ЛЮБОЙ верхней границе.
        /// </summary>
        static void ChannelSpan(EnergyCalibration calibration, int channels, int length,
                                double fromE, double toE, out int start, out int end)
        {
            double x0 = Math.Ceiling(calibration.EnergyToChannel(fromE, channels));
            double x1 = Math.Ceiling(calibration.EnergyToChannel(toE, channels));
            start = x0 > 0.0 ? (x0 < length ? (int)x0 : length) : 0;
            end = x1 > 0.0 ? (x1 < length ? (int)x1 : length) : 0;
        }

        /// <summary>
        /// Доли отклика: `fractions[j][i]` — какая часть квантов энергии
        /// E_j (середина диапазона j) даёт отсчёт В ДИАПАЗОНЕ i. Строка матрицы
        /// растягивается на энергию линии тем же кодом, что у разбора
        /// (<see cref="ResponseMatrix.Evaluate"/>), и раскладывается по
        /// сетке ПО ПЕРЕКРЫТИЮ бина с каналами спектра, а каналы — в диапазон по
        /// центру, как отсчёты (<see cref="RowProjection"/>, <see cref="ChannelLayout"/>;
        /// `AMBER82`, `AMBER101`); без раскладки каналов — по перекрытию бина с
        /// границами диапазона. Эта перегрузка — БЕЗ разрешения прибора; её
        /// зовёт отражением `DoseBinCentreProbe`.
        ///
        /// ⚠ Здесь строка берётся на СЕРЕДИНЕ диапазона — это доли для пола
        /// эффективности (<see cref="MinOwnEfficiencyFraction"/>), свойство
        /// сетки, а не спектра. Делит расчёт по строке на ПРЕДСТАВИТЕЛЬНОЙ
        /// энергии диапазона (`AMBER77`, см. <see cref="Calculate(ResultData, DoseRateInput)"/>).
        ///
        /// ⛔ БИН `b` — ЭТО [(b − 0.5)·шаг, (b + 0.5)·шаг), А ЦЕНТР ЕГО `b·шаг`
        /// (`AMBER71`). Сетка поглощённой энергии в складе ОКРУГЛЯЮЩАЯ —
        /// `EfficiencySimulator.PeakBin` кладёт энергию в `round(E/шаг)`, — и
        /// судить бин по `(b + 0.5)·шаг` значит сдвинуть ВСЮ строку на полбина
        /// вверх. При шаге склада 2 кэВ это целый кэВ: на каждой границе
        /// диапазона один бин континуума менял владельца, а у нижнего
        /// диапазона сетки (низ шкалы 11.0 кэВ, `AS80_point0`) дома оставалось
        /// 0.581 отклика вместо 1.000 — 42 % квантов уходило соседу (замер
        /// П131 22.09.2026, `DoseBinCentreProbe`). Тот же дефект у разбора
        /// закрыт ~~`S15`~~ 07.08.2026.
        ///
        /// ⛔ ЗАЧЕМ ЭТО, А НЕ ПРОСТО СУММА СТРОКИ (`AMBER18`, замер 12.09.2026).
        /// Наивное «отсчёты диапазона ÷ сумма строки» приписывает ВСЕ отсчёты
        /// диапазона квантам его середины — а внизу шкалы отсчёты почти целиком
        /// континуум линий, стоящих выше. У живой ASN16 (Cs-137 на торце)
        /// диапазон 10…13 кэВ при ε = 2.5e-5 давал 71 % показания. С матрицей
        /// континуум ИЗВЕСТЕН: отсчёты делятся между линиями совместным
        /// решением (<see cref="SolveJoint"/>), и знаменателем идёт доля
        /// отклика линии В СВОЁМ диапазоне. Для отклика без континуума (одна
        /// дельта в пике) обе записи тождественны.
        /// </summary>
        static double[][] ResponseFractions(DoseRateInput input, DoseRateRange[] ranges, ChannelLayout layout)
        {
            double step = MatrixStep(input.Matrix);
            int cells = Cells(ranges, step);
            RowProjection projection = RowProjection.Of(layout, ranges, step, cells, null, 0, null);
            double[][] fractions, moments, unused;
            CentreRows(input, ranges, projection, null, cells, out fractions, out moments, out unused);
            return fractions;
        }

        /// <summary>
        /// Доли и первые моменты строк на СЕРЕДИНАХ диапазонов через готовую
        /// проекцию (<see cref="RowProjection"/>; с разрешением прибора, если
        /// проекция его несёт, `S185`); <paramref name="plain"/> (если не null)
        /// — та же строка через проекцию без разрешения, доли для пола.
        /// </summary>
        static void CentreRows(DoseRateInput input, DoseRateRange[] ranges, RowProjection projection,
                               RowProjection plain, int cells, out double[][] fractions,
                               out double[][] moments, out double[][] plainFractions)
        {
            ResponseMatrix matrix = input.Matrix;
            int bins = ranges.Length;
            fractions = new double[bins][];
            moments = new double[bins][];
            plainFractions = plain == null ? null : new double[bins][];
            for (int j = 0; j < bins; j++)
            {
                double[] row = matrix.Evaluate(ranges[j].CenterKev, cells);
                fractions[j] = new double[projection.Targets];
                moments[j] = new double[projection.Targets];
                projection.Apply(row, fractions[j], moments[j]);
                if (plain != null)
                {
                    plainFractions[j] = new double[plain.Targets];
                    plain.Apply(row, plainFractions[j], null);
                }
            }
        }

        /// <summary>Шаг склада матрицы, кэВ; нет шага — отказ словами.</summary>
        static double MatrixStep(ResponseMatrix matrix)
        {
            double step = matrix.BinKev;
            if (!(step > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateEmptyMatrix", "Dose rate: the response matrix of the curve has no rows."));
            }

            return step;
        }

        /// <summary>
        /// (`AMBER82`, `AMBER101`, П145) Раскладка каналов спектра: границы
        /// каналов `E(j − ½)`, их центры `E(j)`, пропускаемые каналы
        /// (переполнение) и каналы каждого диапазона `[Start[k], End[k])` —
        /// ТЕ ЖЕ, что считает сторона спектра (<see cref="ChannelSpan"/>).
        /// По ней строка матрицы ложится на каналы так, как лёг бы спектр
        /// без разрешения, и диапазону идёт то, что идёт спектру: сторона
        /// матрицы и сторона спектра судят одним правилом.
        /// </summary>
        sealed class ChannelLayout
        {
            public double[] Edges;
            public double[] Centres;
            public bool[] Skip;
            public int[] Start;
            public int[] End;

            /// <summary>
            /// null — калибровка на шкале не монотонна (или не число), и
            /// раскладки по каналам нет: тогда строка кладётся на границы
            /// сетки по перекрытию (<see cref="RowProjection"/>).
            /// </summary>
            public static ChannelLayout Of(EnergyCalibration calibration, int channels, int length, bool[] skip,
                                           DoseRateRange[] ranges)
            {
                if (length < 2)
                {
                    return null;
                }

                // Крайние границы — продолжением соседней полуширины, а не
                // `E(−½)` и `E(N − ½)`: калибровка зажимает номер канала в
                // [0, maxChannels], и `E(−½)` возвращала `E(0)` — нулевой канал
                // выходил вдвое уже.
                var edges = new double[length + 1];
                var centres = new double[length];
                for (int j = 0; j < length; j++)
                {
                    centres[j] = calibration.ChannelToEnergy(j);
                    if (j > 0)
                    {
                        edges[j] = calibration.ChannelToEnergy(j - 0.5);
                    }
                }

                edges[0] = 2.0 * centres[0] - edges[1];
                edges[length] = 2.0 * centres[length - 1] - edges[length - 1];
                for (int j = 0; j <= length; j++)
                {
                    if (double.IsNaN(edges[j]) || double.IsInfinity(edges[j]) || (j > 0 && !(edges[j] > edges[j - 1])))
                    {
                        return null;
                    }
                }

                var layout = new ChannelLayout
                {
                    Edges = edges,
                    Centres = centres,
                    Skip = skip ?? new bool[length],
                    Start = new int[ranges.Length],
                    End = new int[ranges.Length],
                };

                layout.RangeOf = new int[length];
                for (int j = 0; j < length; j++)
                {
                    layout.RangeOf[j] = -1;
                }

                for (int k = 0; k < ranges.Length; k++)
                {
                    ChannelSpan(calibration, channels, length, ranges[k].LowKev, ranges[k].HighKev,
                                out layout.Start[k], out layout.End[k]);
                    for (int j = layout.Start[k]; j < layout.End[k]; j++)
                    {
                        layout.RangeOf[j] = k;
                    }
                }

                return layout;
            }

            /// <summary>Диапазон канала по центру (−1 — ни в одном), как у спектра.</summary>
            public int[] RangeOf;
        }

        /// <summary>
        /// ⛔ (`S185`, П153 24.09.2026) ПРОЕКЦИЯ СТРОКИ МАТРИЦЫ НА ДИАПАЗОНЫ —
        /// один раз на расчёт, а не на каждую строку. Всё, что делается со
        /// строкой склада до сравнения с отсчётами, линейно по ней и зависит
        /// только от шкалы, ширины, сетки и шага склада, поэтому
        /// сворачивается в разреженную таблицу «бин → (диапазон, доля,
        /// доля·кэВ)», и строка проецируется за один проход по её бинам.
        ///
        /// Без разрешения — прежнее правило: бин `b` (отрезок
        /// `[(b − ½)·шаг, (b + ½)·шаг)`, `AMBER71`) ложится по перекрытию на
        /// каналы спектра, канал — в диапазон по центру, как отсчёты
        /// (`AMBER101`); без раскладки каналов (калибровка не монотонна) — по
        /// перекрытию с границами диапазонов.
        ///
        /// ⛔ (`AMBER82`, П145) БИН — ОТРЕЗОК, А НЕ ТОЧКА: доля по
        /// ПЕРЕКРЫТИЮ, как у перегруппировки гистограммы. Прежнее «весь бин
        /// тому, в чей диапазон попал его центр» после `AMBER70` теряло нижний
        /// бин первого диапазона сетки целиком (своя доля скачком до 0.78,
        /// поток ×1.28).
        ///
        /// С разрешением прибора (калибровка ширины САМОГО спектра задана) —
        /// бин размывается гауссом: равномерный отрезок шириной в шаг,
        /// свёрнутый с гауссом σ(E) = ПШПВ/2.3548 в кэВ, заменён гауссом
        /// σ_эфф = √(σ² + шаг²/12) с центром в бине, и диапазону k достаётся
        /// его доля между ЭНЕРГИЯМИ краёв своих каналов `E(Start − ½)` и
        /// `E(End − ½)` — ровно то, что соберёт спектр, судя каналы по
        /// центру. Момент — по среднему усечённого гаусса. Кусок, упавший на
        /// пропускаемый канал (переполнение), снимается.
        /// </summary>
        sealed class RowProjection
        {
            /// <summary>Записи бина b — `[first[b], first[b + 1])`.</summary>
            int[] first;
            int[] target;
            double[] weight;
            double[] momentWeight;

            /// <summary>Число целей проекции — диапазонов.</summary>
            public int Targets;

            /// <summary>Свёрнута ли проекция с разрешением прибора.</summary>
            public bool Smeared;

            /// <summary>Сколько σ гаусса учитывается по обе стороны бина.</summary>
            const double LeakSigmas = 6.0;

            public void Apply(double[] row, double[] share, double[] moment)
            {
                int bins = Math.Min(row.Length, this.first.Length - 1);
                for (int b = 0; b < bins; b++)
                {
                    double value = row[b];
                    if (value == 0.0)
                    {
                        continue;
                    }

                    for (int e = this.first[b]; e < this.first[b + 1]; e++)
                    {
                        share[this.target[e]] += value * this.weight[e];
                        if (moment != null)
                        {
                            moment[this.target[e]] += value * this.momentWeight[e];
                        }
                    }
                }
            }

            public static RowProjection Of(ChannelLayout layout, DoseRateRange[] ranges, double step, int cells,
                                           EnergyCalibration calibration, int channels, FwhmCalibration fwhm)
            {
                int bins = ranges.Length;
                var p = new RowProjection { first = new int[cells + 1], Targets = bins };
                var targets = new System.Collections.Generic.List<int>(cells * 3);
                var weights = new System.Collections.Generic.List<double>(cells * 3);
                var moments = new System.Collections.Generic.List<double>(cells * 3);
                var w = new double[bins];
                var wm = new double[bins];
                var touched = new bool[bins];
                var touchedList = new System.Collections.Generic.List<int>(8);

                bool smear = layout != null && calibration != null && fwhm != null && !fwhm.NotCalibrated();
                int n = layout != null ? layout.Edges.Length - 1 : 0;

                // Энергии краёв диапазонов по каналам и пропускаемые каналы.
                double[] lowEdge = null, highEdge = null;
                var skipped = new System.Collections.Generic.List<int>(2);
                if (smear)
                {
                    lowEdge = new double[bins];
                    highEdge = new double[bins];
                    for (int k = 0; k < bins; k++)
                    {
                        lowEdge[k] = layout.Edges[layout.Start[k]];
                        highEdge[k] = layout.Edges[layout.End[k]];
                    }

                    for (int i = 0; i < n; i++)
                    {
                        if (layout.Skip[i] && layout.RangeOf[i] >= 0)
                        {
                            skipped.Add(i);
                        }
                    }
                }

                int firstChannel = 0;
                int firstRange = 0;
                for (int b = 0; b < cells; b++)
                {
                    p.first[b] = targets.Count;
                    double lo = (b - 0.5) * step;
                    double hi = (b + 0.5) * step;
                    touchedList.Clear();
                    double sigma = smear ? SigmaKev(calibration, channels, fwhm, b * step) : 0.0;
                    if (sigma > 0.0)
                    {
                        p.Smeared = true;
                        double centre = b * step;
                        double se = Math.Sqrt(sigma * sigma + step * step / 12.0);
                        double reachLo = centre - LeakSigmas * se, reachHi = centre + LeakSigmas * se;
                        // Диапазоны идут встык: Φ и плотность на общей
                        // границе считаются один раз.
                        double lastEdge = double.NaN, lastPhi = 0.0, lastPdf = 0.0;
                        for (int k = 0; k < bins; k++)
                        {
                            double a = lowEdge[k], c = highEdge[k];
                            if (!(c > a) || c <= reachLo || a >= reachHi)
                            {
                                continue;
                            }

                            double za = (a - centre) / se, zb = (c - centre) / se;
                            double phiA = a == lastEdge ? lastPhi : Phi(za);
                            double pdfA = a == lastEdge ? lastPdf : Pdf(za);
                            double phiB = Phi(zb), pdfB = Pdf(zb);
                            lastEdge = c;
                            lastPhi = phiB;
                            lastPdf = pdfB;
                            double f = phiB - phiA;
                            double fm = f * centre + se * (pdfA - pdfB);
                            foreach (int i in skipped)
                            {
                                if (layout.RangeOf[i] == k)
                                {
                                    double slice = Phi((layout.Edges[i + 1] - centre) / se)
                                                   - Phi((layout.Edges[i] - centre) / se);
                                    f -= slice;
                                    fm -= slice * layout.Centres[i];
                                }
                            }

                            if (f > 0.0)
                            {
                                Touch(k, f, fm, w, wm, touched, touchedList);
                            }
                        }
                    }
                    else if (layout != null)
                    {
                        double[] edges = layout.Edges;
                        while (firstChannel < n && edges[firstChannel + 1] <= lo)
                        {
                            firstChannel++;
                        }

                        for (int j = firstChannel; j < n && edges[j] < hi; j++)
                        {
                            double a = Math.Max(lo, edges[j]);
                            double c = Math.Min(hi, edges[j + 1]);
                            int k = layout.RangeOf[j];
                            if (!(c > a) || k < 0 || layout.Skip[j])
                            {
                                continue;
                            }

                            double part = c - a >= step ? 1.0 : (c - a) / step;
                            Touch(k, part, part * layout.Centres[j], w, wm, touched, touchedList);
                        }
                    }
                    else
                    {
                        while (firstRange < bins && ranges[firstRange].HighKev <= lo)
                        {
                            firstRange++;
                        }

                        for (int k = firstRange; k < bins && ranges[k].LowKev < hi; k++)
                        {
                            double a = Math.Max(lo, ranges[k].LowKev);
                            double c = Math.Min(hi, ranges[k].HighKev);
                            if (!(c > a))
                            {
                                continue;
                            }

                            double part = c - a >= step ? 1.0 : (c - a) / step;
                            Touch(k, part, part * 0.5 * (a + c), w, wm, touched, touchedList);
                        }
                    }

                    touchedList.Sort();
                    foreach (int k in touchedList)
                    {
                        targets.Add(k);
                        weights.Add(w[k]);
                        moments.Add(wm[k]);
                        w[k] = 0.0;
                        wm[k] = 0.0;
                        touched[k] = false;
                    }
                }

                p.first[cells] = targets.Count;
                p.target = targets.ToArray();
                p.weight = weights.ToArray();
                p.momentWeight = moments.ToArray();
                return p;
            }

            /// <summary>
            /// σ гаусса разрешения в кэВ на энергии <paramref name="energy"/>:
            /// ПШПВ калибровки ширины (в каналах) на канале этой энергии,
            /// умноженная на ширину канала там же. Нет ширины (у степенной
            /// модели — канал 0) или энергия вне шкалы — 0: бин без свёртки.
            /// </summary>
            static double SigmaKev(EnergyCalibration calibration, int channels, FwhmCalibration fwhm, double energy)
            {
                if (!(energy > 0.0))
                {
                    return 0.0;
                }

                double channel = calibration.EnergyToChannel(energy, channels);
                if (!(channel > 0.0) || !(channel < channels))
                {
                    return 0.0;
                }

                double width = calibration.ChannelToEnergy(channel + 0.5) - calibration.ChannelToEnergy(channel - 0.5);
                double sigma = fwhm.ChannelToFwhm(channel) * width / 2.3548200450309493;
                return sigma > 0.0 && !double.IsInfinity(sigma) ? sigma : 0.0;
            }

            static void Touch(int k, double weight, double moment, double[] w, double[] wm, bool[] touched,
                              System.Collections.Generic.List<int> touchedList)
            {
                if (!touched[k])
                {
                    touched[k] = true;
                    touchedList.Add(k);
                }

                w[k] += weight;
                wm[k] += moment;
            }

            static double Pdf(double z)
            {
                return Math.Exp(-0.5 * z * z) / 2.5066282746310002;
            }

            /// <summary>
            /// Φ(z) через erfc по Numerical Recipes (erfcc, относительная
            /// ошибка ниже 1.2e-7): одна экспонента вместо ряда.
            /// </summary>
            static double Phi(double z)
            {
                double x = -z / 1.4142135623730951;
                double a = Math.Abs(x);
                double u = 1.0 / (1.0 + 0.5 * a);
                double erfc = u * Math.Exp(-a * a - 1.26551223 + u * (1.00002368 + u * (0.37409196 + u * (0.09678418
                              + u * (-0.18628806 + u * (0.27886807 + u * (-1.13520398 + u * (1.48851587
                              + u * (-0.82215223 + u * 0.17087277)))))))));
                return 0.5 * (x >= 0.0 ? erfc : 2.0 - erfc);
            }
        }

        /// <summary>
        /// (`AMBER77`) Представительная энергия берётся по центру тяжести,
        /// только если приписанное — не меньше этой доли отсчётов диапазона.
        /// Ниже — это разность двух больших чисел (отсчёты минус континуум
        /// сверху), и её центр тяжести — шум вычитания: при рассогласовании
        /// континуума на δ он сдвигается на ~δ·(объяснено/приписано) ширины
        /// диапазона. Пятая часть держит сдвиг в пределах трети ширины при
        /// δ ≈ 5 %. Ниже порога диапазон остаётся на середине, как было (с
        /// `S194` — ниже порога/√2, см. ниже).
        ///
        /// ⛔ (`S194`, П159 24.09.2026) ПОРОГ ПЛАВНЫЙ, А НЕ ОБРЕЗ — как пол
        /// эффективности (`S192`). Доверие подобранной энергии
        /// <see cref="ShareTrust"/> растёт по доле приписанного от 0 до 1 в
        /// полосе <see cref="RepresentativeBand"/> ВОКРУГ порога (от
        /// порога/√2 до порога·√2, половина — на самом пороге), и энергия
        /// диапазона — между серединой и подобранной в меру доверия. Прежний
        /// обрез переставлял энергию скачком: `ASN16_Cs137_10cm`, 17.22…22.59
        /// кэВ — 19.90 → 18.64 кэВ, показание −0.19 % на сдвиге порога пола
        /// 0.23 % (`DoseFloorProbe --steps=1200`) и −0.32 % на шаге набора
        /// счёта 1/3200 (`DoseShareProbe`).
        ///
        /// Полоса ВОКРУГ порога, а не над ним (как у пола) — замером
        /// (`DoseGridProbe`, ширина синтетики `manifest.csv`): полоса над
        /// порогом (0.2…0.4) уводит к середине диапазоны с приписанным
        /// 20…40 %, и синтетика там хуже — AS80 356 кэВ +0.06 → −0.11 %,
        /// G1S 356 +0.03 → −0.15 %, RC-103 59.5 +1.81 → +1.93 %; полоса
        /// вокруг порога не ухудшает ни одну линию больше чем на 0.002 п.п.
        /// и лечит низ шкалы (ASN16 59.5 +2.21 → +1.21 %, фантом 1.71 → 1.03 %
        /// показания; RC-103 356 −0.30 → −0.15 %), и в среднем по доле
        /// приписанного доверие то же, что у обреза: вне полосы расчёт
        /// прежний, внутри — обрез размазан.
        ///
        /// ⚠ ЦЕНА. Диапазон с приписанным 14…20 % получает часть подобранной
        /// энергии (прежде — середина), с 20…28 % — не всю. Сдвиг от шума
        /// вычитания (δ·(1 − доля)/доля ширины, выше) в полосе не больше
        /// 0.13 ширины при δ = 5 % (у обреза на самом пороге было 0.2).
        /// Показания настоящих спектров (П159): `AS80_Cs137_0cm` +0.015 %,
        /// `G1S16_Cs137_P5` −0.008 %, `RC103_Cs137_0cm` +0.015 %,
        /// `ASN16_Cs137_10cm` −0.018 % по матрице; путь «≈» — побитово
        /// прежний (там континуума нет и доля — 1). Журнал
        /// `handover-2026-09-24-p159-dose-representative.md`.
        /// </summary>
        public const double RepresentativeMinShare = 0.2;

        /// <summary>
        /// (`S194`, П159) Ширина полосы доверия представительной энергии, во
        /// сколько раз её верх выше низа; полоса стоит вокруг
        /// <see cref="RepresentativeMinShare"/> (геометрически). Вдвое — как у
        /// пола (`S192`); ширины 1.5…4 разнятся в синтетике на сотые доли
        /// процента (журнал П159).
        /// </summary>
        public const double RepresentativeBand = 2.0;

        /// <summary>
        /// Мерные рычаги проб (`DoseShareProbe`, `DoseGridProbe --set=`, П159):
        /// ширина полосы и её середина в порогах (1 — на пороге; √2 — полоса
        /// над порогом, отвергнутый вариант), которыми считает расчёт.
        /// Приложение в них не пишет; проба двигает их отражением.
        /// </summary>
        static double representativeBand = RepresentativeBand;

        static double representativeCentre = 1.0;

        /// <summary>
        /// (`S194`, П159) Доверие подобранной энергии по доле приписанного
        /// <paramref name="attributed"/>/<paramref name="counts"/>: 0 при доле не
        /// выше низа полосы, 1 от её верха, между — линейно по логарифму доли
        /// (тем же законом, что у пола, <see cref="FloorWeight"/>), ½ на пороге.
        /// </summary>
        static double ShareTrust(double attributed, double counts)
        {
            if (!(attributed > 0.0) || !(counts > 0.0))
            {
                return 0.0;
            }

            double x = attributed / (representativeCentre * RepresentativeMinShare * counts);
            double top = Math.Sqrt(representativeBand);
            if (double.IsNaN(x) || !(x > 1.0 / top))
            {
                return 0.0;
            }

            return x >= top ? 1.0 : 0.5 + Math.Log(x) / Math.Log(representativeBand);
        }

        /// <summary>Сколько раз уточнять представительную энергию (`AMBER77`).</summary>
        const int RepresentativeIterations = 8;

        /// <summary>Когда уточнение останавливается, кэВ.</summary>
        const double RepresentativeToleranceKev = 1e-3;

        /// <summary>
        /// (`S192`, П157) Строка матрицы на энергии <paramref name="energy"/>,
        /// спроецированная на диапазоны: доли, моменты и — если задана проекция
        /// без разрешения — её доли. Те же действия, что у подбора
        /// <see cref="Representative"/>.
        /// </summary>
        static void RowAt(DoseRateInput input, double energy, int cells, RowProjection projection, RowProjection plain,
                          out double[] share, out double[] moment, out double[] plainShare)
        {
            double[] row = input.Matrix.Evaluate(energy, cells);
            share = new double[projection.Targets];
            moment = new double[projection.Targets];
            projection.Apply(row, share, moment);
            plainShare = null;
            if (plain != null)
            {
                plainShare = new double[plain.Targets];
                plain.Apply(row, plainShare, null);
            }
        }

        /// <summary>
        /// (`AMBER77`) Энергия линии диапазона <paramref name="k"/>, чья строка
        /// матрицы даёт в окне <paramref name="window"/> (диапазон k и соседи,
        /// слитые с ним или принявшие его пик, `S185`, <see cref="Spills"/>)
        /// центр тяжести <paramref name="target"/>, —
        /// подбором `E ← E + (цель − центр строки(E))/наклон` (секущая) от
        /// <paramref name="start"/>, не больше <see cref="RepresentativeIterations"/>
        /// шагов; энергия зажата в диапазон k и в область матрицы.
        /// <paramref name="slope"/> — наклон центра по энергии: на входе
        /// известный с прошлого прохода (NaN — нет), на выходе последний
        /// измеренный. Возвращает энергию и строку на ней, спроецированную на
        /// диапазоны (<paramref name="share"/>, <paramref name="moment"/>) и —
        /// если задана проекция без разрешения — её доли (<paramref name="plainShare"/>).
        ///
        /// (`S195`, П160) Остановка секущей принимается, только если это корень
        /// на восходящей ветви центра строки или край, к которому центр растёт
        /// (<see cref="EdgeHolds"/>; ответ — в <paramref name="edges"/> по
        /// диапазону, группе соседей <paramref name="group"/> и краю, один раз
        /// на расчёт); иначе энергия — по правилу <see cref="RisingBranch"/>.
        /// </summary>
        static double Representative(DoseRateInput input, DoseRateRange[] ranges, int k, double[] window, double target,
                                     double start, ref double slope, int cells, RowProjection projection,
                                     RowProjection plain, double[][] known, sbyte[] edges, int group,
                                     out double[] share, out double[] moment, out double[] plainShare)
        {
            ResponseMatrix matrix = input.Matrix;
            double lo = Math.Max(ranges[k].LowKev, input.MinKev);
            double hi = Math.Min(ranges[k].HighKev, input.MaxKev);
            double energy = Clamp(start, lo, hi);
            double previousEnergy = double.NaN, previousCentre = double.NaN;
            for (int iteration = 0; ; iteration++)
            {
                if (iteration == 0 && known != null)
                {
                    // Строка на начальной энергии уже есть — с прошлого подбора.
                    share = known[0];
                    moment = known[1];
                    plainShare = known[2];
                }
                else
                {
                    double[] row = matrix.Evaluate(energy, cells);
                    share = new double[projection.Targets];
                    moment = new double[projection.Targets];
                    projection.Apply(row, share, moment);
                    plainShare = null;
                    if (plain != null)
                    {
                        plainShare = new double[plain.Targets];
                        plain.Apply(row, plainShare, null);
                    }
                }

                double mass = 0.0, first = 0.0;
                for (int i = 0; i < window.Length; i++)
                {
                    if (window[i] == 1.0)
                    {
                        mass += share[i];
                        first += moment[i];
                    }
                    else if (window[i] > 0.0)
                    {
                        // (`S192`) Сосед в полосе плавного пола — с его весом.
                        mass += window[i] * share[i];
                        first += window[i] * moment[i];
                    }
                }

                if (!(share[k] > 0.0) || !(mass > 0.0))
                {
                    return energy;
                }

                if (iteration >= RepresentativeIterations)
                {
                    // (`S195`) Предел шагов. Секущая на восходящей ветви и
                    // последний шаг меньше допуска совместного решения — энергия
                    // прежняя (как было); иначе — корень на восходящей ветви.
                    if (slope > 0.0 && Math.Abs(energy - previousEnergy) < Tolerance(energy))
                    {
                        return energy;
                    }

                    return RisingBranch(input, k, window, target, lo, hi, ref slope, cells, projection, plain,
                                        out share, out moment, out plainShare);
                }

                // Секущая, а не шаг «один к одному»: у линии, чей собственный
                // комптон заходит в диапазон (2614.5 в [2286, 3000)), центр
                // строки растёт МЕДЛЕННЕЕ её энергии, и единичный шаг за
                // четыре попытки не доходил (2531 вместо 2614, показание +38 %).
                double centre = first / mass;
                if (!double.IsNaN(previousEnergy))
                {
                    slope = (centre - previousCentre) / (energy - previousEnergy);
                }

                double gain = slope > 0.05 && slope < 20.0 ? 1.0 / slope : 1.0;
                double step = energy + gain * (target - centre);
                double next = Clamp(step, lo, hi);
                if (Math.Abs(next - energy) < RepresentativeToleranceKev)
                {
                    // (`S195`, П160) Корень — только на ВОСХОДЯЩЕЙ ветви центра,
                    // и край диапазона — только там, где центр к нему растёт.
                    // Иначе (край, за который цель тянет дальше, а центр к
                    // краю падает; корень, где центр падает) — не решение, а
                    // ложная остановка, см. <see cref="RisingBranch"/>.
                    bool root = Math.Abs(step - energy) < RepresentativeToleranceKev;
                    bool holds;
                    if (root)
                    {
                        holds = !(slope < 0.0);
                    }
                    else
                    {
                        // Держит ли край — от окна (диапазон и группа соседей)
                        // и края, но не от отсчётов: один раз на расчёт.
                        int memo = ((k << 2) + group) * 2 + (step > hi ? 1 : 0);
                        if (edges[memo] == 0)
                        {
                            edges[memo] = EdgeHolds(input, k, window, energy, step > hi, lo, hi, cells, projection,
                                                    centre) ? (sbyte)1 : (sbyte)-1;
                        }

                        holds = edges[memo] > 0;
                    }

                    if (holds)
                    {
                        return energy;
                    }

                    return RisingBranch(input, k, window, target, lo, hi, ref slope, cells, projection, plain,
                                        out share, out moment, out plainShare);
                }

                previousEnergy = energy;
                previousCentre = centre;
                energy = next;
            }
        }

        static double Clamp(double value, double lo, double hi)
        {
            return value < lo ? lo : (value > hi ? hi : value);
        }

        /// <summary>
        /// (`S195`) Центр тяжести строки энергии <paramref name="energy"/> в окне
        /// <paramref name="window"/> — теми же действиями, что в подборе
        /// <see cref="Representative"/>. NaN — строка в окне пуста.
        /// </summary>
        static double WindowCentre(DoseRateInput input, int k, double[] window, double energy, int cells,
                                   RowProjection projection)
        {
            double[] row = input.Matrix.Evaluate(energy, cells);
            var share = new double[projection.Targets];
            var moment = new double[projection.Targets];
            projection.Apply(row, share, moment);
            double mass = 0.0, first = 0.0;
            for (int i = 0; i < window.Length; i++)
            {
                if (window[i] == 1.0)
                {
                    mass += share[i];
                    first += moment[i];
                }
                else if (window[i] > 0.0)
                {
                    mass += window[i] * share[i];
                    first += window[i] * moment[i];
                }
            }

            return share[k] > 0.0 && mass > 0.0 ? first / mass : double.NaN;
        }

        /// <summary>
        /// (`S195`) Держит ли край диапазона: подбор упёрся в край
        /// <paramref name="energy"/> (нижний или верхний), цель тянет за него, —
        /// и центр строки к верхнему краю РАСТЁТ (к нижнему — убывает). Тогда
        /// край — ближайшее к цели, что даёт восходящая ветвь, и он
        /// принимается. Центр, падающий к верхнему краю (пик линии
        /// разрешением уходит в соседа, и центр оставшегося в диапазоне
        /// падает), — край не решение.
        /// </summary>
        static bool EdgeHolds(DoseRateInput input, int k, double[] window, double energy, bool upper, double lo,
                              double hi, int cells, RowProjection projection, double centre)
        {
            double delta = (hi - lo) / RisingGrid;
            double inner = WindowCentre(input, k, window, upper ? energy - delta : energy + delta, cells, projection);
            if (double.IsNaN(inner))
            {
                return true;
            }

            return upper ? inner < centre : inner > centre;
        }

        /// <summary>Шагов сетки по диапазону, по которой ищется восходящая ветвь центра строки (`S195`).</summary>
        const int RisingGrid = 24;

        /// <summary>
        /// ⛔ (`S195`, П160 24.09.2026) ЭНЕРГИЯ ДИАПАЗОНА — КОРЕНЬ НА ВОСХОДЯЩЕЙ
        /// ВЕТВИ ЦЕНТРА СТРОКИ, а не там, где секущая остановилась.
        ///
        /// Центр тяжести строки линии в своём диапазоне c(E) растёт с её
        /// энергией не до верхнего края: у края пик разрешением уходит в
        /// соседа, и центр оставшегося в диапазоне (комптон и часть пика)
        /// падает. `ASN16_Cs137_10cm`, 1742.6…2286.5 кэВ: c растёт от 1766 до
        /// 2012.8 (E ≈ 2232) и падает до 1981.8 на краю; цель 1991.5 — два
        /// корня (≈ 2167 и ≈ 2275), и секущая то сходилась к 2167, то
        /// останавливалась на КРАЮ 2286.5, где c = 1981.8, — не корень вовсе:
        /// цель тянула за край, шаг зажимался в него. От этого на пути набора
        /// счёта (`DoseShareProbe`) энергия прыгала 2286.5 ↔ 2167, за ней —
        /// энергия соседа 3000 ↔ 2780 (его цель, 2664, выше всего, что даёт
        /// его строка), и показание — −0.088/+0.087 % за шаг 1/3200 набора.
        /// Слияние, окно `SpillShare`, выход при Q ≤ 0 и предел проходов тут
        /// ни при чём: трасса П160 — оба решения сходятся за пять проходов,
        /// без слияний, все Q > 0.
        ///
        /// Правило. Сетка в <see cref="RisingGrid"/> шагов по диапазону
        /// проходится СВЕРХУ ВНИЗ: вершина ветви — первый сверху узел, ниже
        /// которого центр уже не растёт (падающая ветвь у края пройдена),
        /// уточнённая параболой по трём узлам. Цель не ниже вершины — энергия
        /// в вершине; ниже — корень под вершиной на первом сверху отрезке
        /// сетки, где центр переходит через цель, — ложным положением; центр
        /// выше цели всюду под вершиной — нижний край. Энергия так —
        /// непрерывная неубывающая функция цели, и выбор между двумя корнями
        /// не зависит от того, откуда шла секущая.
        ///
        /// Зовётся только там, где секущая не дала корня на восходящей ветви
        /// (край, к которому центр падает; корень с убывающим центром; предел
        /// шагов), — всюду прочее подбор прежний. Цена — около дюжины строк
        /// матрицы на вызов. Журнал `handover-2026-09-24-p160-dose-branch.md`.
        /// </summary>
        static double RisingBranch(DoseRateInput input, int k, double[] window, double target, double lo, double hi,
                                   ref double slope, int cells, RowProjection projection, RowProjection plain,
                                   out double[] share, out double[] moment, out double[] plainShare)
        {
            double step = (hi - lo) / RisingGrid;
            Func<int, double> node = i => i == RisingGrid ? hi : lo + step * i;
            // Спуск по сетке сверху, пока центр растёт вниз (падающая ветвь).
            int top = RisingGrid;
            double topCentre = WindowCentre(input, k, window, hi, cells, projection);
            double upE = double.NaN, upC = double.NaN;         // узел над вершиной
            double downE = double.NaN, downC = double.NaN;     // узел под вершиной
            for (int i = RisingGrid - 1; i >= 0; i--)
            {
                double ci = WindowCentre(input, k, window, node(i), cells, projection);
                if (double.IsNaN(topCentre) || ci > topCentre)
                {
                    upE = node(top);
                    upC = topCentre;
                    top = i;
                    topCentre = ci;
                    continue;
                }

                downE = node(i);
                downC = ci;
                break;
            }

            double energy;
            if (double.IsNaN(topCentre))
            {
                energy = lo;
            }
            else
            {
                double apex = node(top), apexCentre = topCentre;
                if (!double.IsNaN(upE) && !double.IsNaN(downE) && !double.IsNaN(upC) && !double.IsNaN(downC))
                {
                    // Вершина параболы по трём узлам (шаг сетки равный).
                    double curvature = upC - 2.0 * topCentre + downC;
                    if (curvature < 0.0)
                    {
                        double x = apex + 0.5 * step * (downC - upC) / curvature;
                        if (x > downE && x < upE)
                        {
                            double cx = WindowCentre(input, k, window, x, cells, projection);
                            if (cx > apexCentre)
                            {
                                apex = x;
                                apexCentre = cx;
                            }
                        }
                    }
                }

                if (!(target < apexCentre))
                {
                    energy = apex;
                    slope = double.NaN;
                }
                else
                {
                    // Корень под вершиной: первый сверху отрезок, где центр
                    // опускается до цели.
                    double hiE = apex, hiC = apexCentre;
                    double loE = double.NaN, loC = double.NaN;
                    if (!double.IsNaN(downE))
                    {
                        int i = (int)Math.Round((downE - lo) / step);
                        double ci = downC;
                        for (;;)
                        {
                            if (double.IsNaN(ci))
                            {
                                break;
                            }

                            if (ci <= target)
                            {
                                loE = node(i);
                                loC = ci;
                                break;
                            }

                            hiE = node(i);
                            hiC = ci;
                            if (--i < 0)
                            {
                                break;
                            }

                            ci = WindowCentre(input, k, window, node(i), cells, projection);
                        }
                    }

                    if (double.IsNaN(loE))
                    {
                        energy = double.IsNaN(downE) || hiE <= lo ? lo : hiE;
                    }
                    else
                    {
                        // Ложное положение с поправкой «Иллинойс».
                        int side = 0;
                        for (int it = 0; it < 60 && hiE - loE > RepresentativeToleranceKev; it++)
                        {
                            double x = loE - (loC - target) * (hiE - loE) / (hiC - loC);
                            if (!(x > loE && x < hiE))
                            {
                                x = 0.5 * (loE + hiE);
                            }

                            double cx = WindowCentre(input, k, window, x, cells, projection);
                            if (double.IsNaN(cx))
                            {
                                break;
                            }

                            if (Math.Abs(cx - target) < 1e-9 * Math.Abs(target))
                            {
                                loE = hiE = x;
                                break;
                            }

                            if (cx < target)
                            {
                                loE = x;
                                loC = cx;
                                if (side == 1)
                                {
                                    hiC = target + 0.5 * (hiC - target);
                                }

                                side = 1;
                            }
                            else
                            {
                                hiE = x;
                                hiC = cx;
                                if (side == -1)
                                {
                                    loC = target + 0.5 * (loC - target);
                                }

                                side = -1;
                            }
                        }

                        energy = 0.5 * (loE + hiE);
                        slope = double.NaN;
                    }
                }
            }

            RowAt(input, energy, cells, projection, plain, out share, out moment, out plainShare);
            return energy;
        }

        /// <summary>Длина строки отклика, накрывающая сетку, с запасом на верхний бин.</summary>
        static int Cells(DoseRateRange[] ranges, double step)
        {
            return (int)Math.Ceiling(ranges[ranges.Length - 1].HighKev / step) + 2;
        }

        /// <summary>
        /// Пол эффективности: диапазон, у которого доля отклика в своём же
        /// диапазоне меньше этой части от наибольшей по сетке, не
        /// приписывается никому. Деление на почти ноль превращает горстку
        /// отсчётов в тысячи квантов на см²: у ASN16 в 10…13 кэВ (ε = 2.5e-5,
        /// 4 отсч/с) выходило 1200 квант/(см²·с) и 2.4 мкЗв/ч из 3.3. Отсчёты
        /// такого диапазона уходят из покрытия, и приписка это показывает.
        /// Одна сотая — от максимума ~0.2…0.26 это ε ≈ 2e-3, то есть ниже
        /// ~15…17 кэВ у сцинтиллятора в обвязке. Пол ОТНОСИТЕЛЬНЫЙ нарочно: у
        /// сцены поля `ISO` те же величины идут в см² (максимум ~46 у Ø63×63),
        /// и правило действует без пересчёта.
        ///
        /// ⛔ (`S192`, П157 24.09.2026) ПОЛ ПЛАВНЫЙ, А НЕ ОБРЕЗ. Решение Amber
        /// 24.09.2026 вопросником, дословно: «Плавный пол (Рекомендую)». Вес
        /// диапазона <see cref="FloorWeight"/> растёт от 0 на поле до 1 на
        /// <see cref="FloorBand"/> полах — линейно по логарифму доли; тем же
        /// весом доли на подобранной энергии диапазона решается, насколько
        /// верить этой энергии (между серединой и ею). Прежний обрез давал
        /// полную дозу диапазону чуть выше пола и ноль чуть ниже, и одна сотая
        /// была не порогом, а переключателем: замер П157 (`DoseFloorProbe`,
        /// сдвиг порога на 0.9 %) — `ASN16_Cs137_10cm` −9.4 % по матрице
        /// (диапазон 13.1…17.2 кэВ, своя доля 1.63 пола, 11.2 % показания) и
        /// −7.2 % по пиковой (10…13.1 кэВ, 1.03 пола), AS80 по пиковой −2.4 %
        /// (17…22 кэВ, 2.9 пола), G1S −0.8/+0.3 %. Прежнее «сдвиг порога вдвое
        /// меняет показание меньше чем на 1 %» для ASN16 было неверно.
        ///
        /// ⚠ ЦЕНА. (1) Показание зависит от выбора порога и теперь — в меру
        /// доли диапазонов в полосе: |d ln Ḣ / d ln порога| ≈ (их доза)/ln 2;
        /// на корпусе по порогам 0.25…4 от штатного — не больше 0.29
        /// (`ASN16_Cs137_10cm` по матрице), прежде — скачок до 9.4 %. (2)
        /// Диапазон в полосе даёт дозу не целиком: его отсчёты — кванты
        /// низкой энергии с ε у самого нуля, и какая их часть настоящая, по
        /// спектру не сказать; пол этого не решает, а только перестаёт дёргать
        /// показание. На штатном пороге (П157): `ASN16_Cs137_10cm` −3.52 % по
        /// матрице (вес 13…17 кэВ 0.70) и −6.94 % по пиковой (вес 10…13 кэВ
        /// 0.04), `RC103_Cs137_0cm` по пиковой −0.006 %, прочее побитово
        /// прежнее (журнал `handover-2026-09-24-p157-dose-soft-floor.md`).
        ///
        /// ⛔ (`S186`, П151 24.09.2026) ПОЛ — ТОЛЬКО НИЖЕ МАКСИМУМА КРИВОЙ.
        /// Решение Amber 24.09.2026 вопросником, дословно: «Пол только ниже
        /// максимума (Рекомендую)». Пол задуман против низа шкалы (ε → 0
        /// ниже 15 кэВ), но у малого кристалла пиковая кривая падает к верху
        /// шкалы ниже сотой доли максимума: у RC-103 диапазоны 1657…2837 кэВ
        /// (своя 5.2e-4 и 8.1e-4 против 0.118) снимались, и линия 2614.5 дозы
        /// не давала вовсе (`DoseGridProbe`, путь «≈» −100 %). Выше диапазона
        /// с наибольшей долей диапазон снимается только пустой строкой (доля
        /// не больше нуля) — делить на ноль нельзя, на малое, но честно
        /// посчитанное, — можно: там отсчёты — кванты своей энергии. Плавности
        /// там нет: вес 1 при доле больше нуля.
        /// </summary>
        public const double MinOwnEfficiencyFraction = 0.01;

        /// <summary>
        /// (`S192`, П157) Ширина полосы плавного пола, в полах: вес диапазона
        /// растёт от 0 на <see cref="MinOwnEfficiencyFraction"/> до 1 на
        /// удвоенном поле. Вдвое — по прежнему описанию порога («сдвиг вдвое»):
        /// то, что обрез переключал на одном значении, теперь размазано на
        /// удвоение. Уже — круче производная (1/ln полосы), шире — больше
        /// диапазонов теряют дозу на штатном пороге; замер ширин 1.25…4 —
        /// журнал П157 §3.
        /// </summary>
        public const double FloorBand = 2.0;

        /// <summary>
        /// Мерные рычаги проб (`DoseFloorProbe`, П157): порог и ширина полосы,
        /// которыми считает расчёт. Приложение в них не пишет; проба двигает их
        /// отражением, чтобы мерить показание по порогу.
        /// </summary>
        static double floorFraction = MinOwnEfficiencyFraction;

        static double floorBand = FloorBand;

        /// <summary>
        /// (`S192`, П157) Вес диапазона по полу эффективности: доля
        /// <paramref name="own"/> в полах (<see cref="MinOwnEfficiencyFraction"/>
        /// от <paramref name="maxOwn"/>) — 0 на поле и ниже, 1 от
        /// <see cref="FloorBand"/> полов, между ними ln(доля)/ln(полоса).
        /// Линейно по логарифму нарочно: из всех переходов 0 → 1 на отрезке
        /// [1, полоса] у него наименьшая наибольшая производная по ln порога
        /// (1/ln полосы; у кубической ступени — 1.5/ln полосы). Выше максимума
        /// кривой (`S186`) пола нет: вес 1 при доле больше нуля, иначе 0.
        /// </summary>
        static double FloorWeight(double own, double maxOwn, bool floorApplies)
        {
            if (!floorApplies)
            {
                return own > 0.0 ? 1.0 : 0.0;
            }

            double x = own / (floorFraction * maxOwn);
            if (double.IsNaN(x) || x >= floorBand)
            {
                return 1.0;
            }

            return x > 1.0 ? Math.Log(x) / Math.Log(floorBand) : 0.0;
        }

        // Token: 0x060002B0 RID: 688 RVA: 0x0000D214 File Offset: 0x0000B414
        /// <summary>
        /// Доза по спектру и готовому входу. Единственный расчёт; всё, что
        /// выше, только собирает вход.
        /// </summary>
        public DoseRate Calculate(ResultData resultData, DoseRateInput input)
        {
            // Доза — свойство ИЗМЕРЕННОГО спектра. Раньше сюда передавался
            // режим отображения графика, и при «фон вычтен» доза считалась по
            // разности — показание дозиметра менялось от галки отрисовки
            // (TODO G6). Дозиметр так себя не ведёт: фон — тоже доза.
            DoseRate doseRate = new DoseRate();
            if (input == null)
            {
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateNoEfficiency", "Dose rate: no efficiency curve is selected.");
                return doseRate;
            }

            doseRate.Approximate = input.Approximate;
            EnergySpectrum energySpectrum = resultData == null ? null : resultData.EnergySpectrum;

            // ⛔ `C4(в)`. Негодный вход отказывается ВИДИМО. Прежде расчёт на
            // спектре без калибровки падал `NullReferenceException` где-то
            // внутри, а на спектре с нулевым временем возвращал ровный ноль —
            // и ноль уходил в строку состояния неотличимо от измеренного.
            if (energySpectrum == null || energySpectrum.Spectrum == null
                || energySpectrum.NumberOfChannels < 2)
            {
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateEmptySpectrum", "Dose rate: the reference spectrum has no channels.");
                return doseRate;
            }

            // Базовый тип, не каст к PolynomialEnergyCalibration: у спектра
            // может стоять NonlinearEnergyCalibration — она сестра, а не
            // наследник, и каст валил расчёт InvalidCastException (TODO G5).
            EnergyCalibration calibration = energySpectrum.EnergyCalibration;
            if (calibration == null)
            {
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateNoCalibration",
                    "Dose rate: the reference spectrum has no energy calibration — the channels cannot be turned into keV.");
                return doseRate;
            }

            // (`AMBER35`, решение Amber 15.09.2026) Знаменатель дозы — по
            // правилу разбора FSA: живое время, если задано (> 0), иначе
            // полное (`EnergySpectrum.EffectiveLiveTime`). Прежде делили на
            // полное, и мкЗв/ч занижались на мёртвое время прибора — а оно
            // поля не уменьшает. Спектр без живого — побитово прежние числа.
            if (!(energySpectrum.EffectiveLiveTime > 0.0))
            {
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateNoTime", "Dose rate: the reference spectrum has zero measurement time.");
                return doseRate;
            }

            double[] grid;
            try
            {
                // Сетка — на пересечении шкалы САМОГО СПЕКТРА, области входа
                // (кривая либо матрица) и области коэффициентов (10 кэВ…10 МэВ).
                // Прежде сетка строилась по шкале ПРИБОРА, потому что точки
                // хранились в его конфигурации; теперь ничего не хранится, и
                // шкала спектра — та, по которой его каналы и переводятся в кэВ.
                double scaleMin, scaleMax;
                DoseRateEstimator.DeviceRange(null, energySpectrum, out scaleMin, out scaleMax);
                grid = DoseRateEstimator.BuildGrid(Math.Max(scaleMin, input.MinKev),
                                                   Math.Min(scaleMax, input.MaxKev));
            }
            catch (DoseRateRefusalException ex)
            {
                doseRate.Refusal = ex.Message;
                return doseRate;
            }

            // Сколько отсчётов спектра вообще попало в диапазоны сетки.
            // Считается по весам каналов, а не сложением длин: 1 — в дозе
            // целиком, 0 — вне покрытия, между — диапазон в полосе плавного
            // пола (`S192`), его отсчёты покрыты в меру его веса.
            double[] covered = new double[energySpectrum.Spectrum.Length];

            // ⛔ `A203`. Каналы, которым нечего давать в дозу, названы вслух —
            // см. <see cref="OverflowChannel"/>. Прежде их роль исполнял зажим
            // `endch = Length − 1` при полуоткрытом цикле: последний канал не
            // считался НИКОГДА, и на ASN16 это работало защитой (в последнем
            // канале лежит переполнение), а на приборе с обычным последним
            // каналом молча теряло его.
            bool[] overflow = OverflowChannel.Mask(energySpectrum.Spectrum);

            double seconds = energySpectrum.EffectiveLiveTime;

            // Шаг 1. Отсчёты по диапазонам — с картой покрытых каналов.
            int bins = grid.Length - 1;
            var ranges = new DoseRateRange[bins];
            // Σ отсчёт·E(центра канала) по диапазону — первый момент для
            // представительной энергии (`AMBER77`).
            var countsMoment = new double[bins];
            for (int k = 0; k < bins; k++)
            {
                double fromE = grid[k];
                double toE = grid[k + 1];

                int startch, endch;
                ChannelSpan(calibration, energySpectrum.NumberOfChannels, energySpectrum.Spectrum.Length,
                            fromE, toE, out startch, out endch);

                double counts = 0.0;
                double moment = 0.0;
                // Полуоткрыто, [startch, endch): диапазоны идут ВСТЫК, и
                // замкнутая сумма считала бы граничный канал дважды (W19,
                // решение Amber 08.08.2026 — полуоткрыто везде).
                for (int i = startch; i < endch; i++)
                {
                    // Канал переполнения пропускается ИМЕНЕМ, а не границей
                    // цикла: он бывает и нулевым, и последним (`A203`).
                    if (overflow[i]) continue;
                    counts += energySpectrum.Spectrum[i];
                    moment += energySpectrum.Spectrum[i]
                              * calibration.ChannelToEnergy(i);
                    covered[i] = 1.0;
                }

                countsMoment[k] = moment;

                ranges[k] = new DoseRateRange
                {
                    LowKev = fromE,
                    HighKev = toE,
                    CenterKev = 0.5 * (fromE + toE),
                    Counts = counts,
                    Attributed = counts,
                };
            }

            // Шаг 2. Кому принадлежат отсчёты каждого диапазона — и какой
            // эффективностью их делить.
            double rate = 0.0;
            double errorSquares = 0.0;
            try
            {
                ResponseMatrix matrix = input.Matrix;
                RowProjection projection = null, plain = null;
                double[][] fractions = null, fractionMoments = null, floorFractions = null;
                int cells = 0;
                if (matrix != null)
                {
                    double step = MatrixStep(matrix);
                    cells = Cells(ranges, step);
                    ChannelLayout layout = ChannelLayout.Of(calibration, energySpectrum.NumberOfChannels,
                                                            energySpectrum.Spectrum.Length, overflow, ranges);
                    // (`S185`) Строка — с разрешением прибора ЭТОГО спектра.
                    projection = RowProjection.Of(layout, ranges, step, cells, calibration,
                                                  energySpectrum.NumberOfChannels, resultData.FwhmCalibration);
                    // Пол судится по строке БЕЗ разрешения: пропускать ли
                    // диапазон — свойство сетки и входа, а не ширины спектра
                    // (иначе свёртка сама переставляет пол у низа шкалы:
                    // ASN16_Cs137_10cm, 13…17 кэВ — 8.6 % показания, П153).
                    plain = projection.Smeared
                        ? RowProjection.Of(layout, ranges, step, cells, null, 0, null) : null;
                    CentreRows(input, ranges, projection, plain, cells, out fractions, out fractionMoments,
                               out floorFractions);
                    if (floorFractions == null)
                    {
                        floorFractions = fractions;
                    }
                }

                // Пол вырожденной эффективности — от наибольшей по сетке:
                // делить на ноль нельзя и на почти ноль тоже, см.
                // <see cref="MinOwnEfficiencyFraction"/>.
                //
                // ⚠ Пол судится по доле НА СЕРЕДИНЕ диапазона: пропускать ли
                // диапазон — свойство сетки и входа, а не спектра, и от
                // представительной энергии (`AMBER77`) оно зависеть не должно.
                double maxOwn = 0.0;
                int maxOwnRange = 0;
                var ownAtCentre = new double[bins];
                for (int k = 0; k < bins; k++)
                {
                    DoseRateRange r = ranges[k];
                    r.RepresentativeKev = r.CenterKev;
                    r.Efficiency = input.EfficiencyAt(r.CenterKev);
                    r.OwnEfficiency = fractions == null ? r.Efficiency : floorFractions[k][k];
                    ownAtCentre[k] = r.OwnEfficiency;
                    // Ровно ноль — не «маленькая эффективность», а пустая
                    // строка матрицы или порча: отказ, а не пропуск. Малая,
                    // но положительная — дело пола ниже.
                    if (double.IsNaN(r.Efficiency) || double.IsInfinity(r.Efficiency) || !(r.Efficiency > 0.0))
                    {
                        throw new DoseRateRefusalException(string.Format(
                            CultureInfo.InvariantCulture,
                            DoseRateCoefficients.Text("DoseRateBadEfficiency",
                                "Dose rate: the efficiency curve gives {0} at {1:f0} keV — division by it is meaningless."),
                            r.Efficiency, r.CenterKev));
                    }

                    // Не число — как у прежнего Math.Max: максимум портится, и
                    // ниже следует отказ.
                    if (double.IsNaN(r.OwnEfficiency))
                    {
                        maxOwn = double.NaN;
                    }
                    else if (r.OwnEfficiency > maxOwn)
                    {
                        maxOwn = r.OwnEfficiency;
                        maxOwnRange = k;
                    }
                }

                if (!(maxOwn > 0.0))
                {
                    throw new DoseRateRefusalException(string.Format(
                        CultureInfo.InvariantCulture,
                        DoseRateCoefficients.Text("DoseRateBadEfficiency",
                            "Dose rate: the efficiency curve gives {0} at {1:f0} keV — division by it is meaningless."),
                        0.0, ranges[bins - 1].CenterKev));
                }

                // ⛔ (`AMBER77`, П145 24.09.2026) ЭНЕРГИЯ ДИАПАЗОНА — НЕ ЕГО
                // СЕРЕДИНА. Диапазон ×1.325 по энергии, и линия в нём стоит где
                // угодно: Cs-137 661.657 в [588.0, 771.5) делилась на ε и
                // множилась на Ḣ/φ̇ середины 679.7, а 356.01 или 1332.49 у
                // верхнего края своих диапазонов — на энергии на 10…14 % ниже.
                // Замер П145 (`DoseGridProbe`, спектр линии из той же матрицы,
                // ответ руками): показание −1.5…+5.3 % на 662 и до +34 % на
                // других линиях, знак и доля — от места линии в диапазоне.
                //
                // Представительная энергия — та, у которой строка матрицы даёт
                // в диапазоне ТОТ ЖЕ центр тяжести, что приписанные отсчёты:
                // центр тяжести отсчётов диапазона за вычетом отклика прочих
                // линий (его первый момент известен из тех же строк), затем
                // энергия линии подбирается так, чтобы центр тяжести её
                // собственной строки в диапазоне с ним совпал, — тогда
                // собственный хвост линии под пиком (комптон, вылеты внутри
                // диапазона) не тянет энергию вниз. Эта энергия — у ε, у Ḣ/φ̇
                // и у строки линии. У пиковой кривой строки нет — энергия
                // диапазона есть центр тяжести его отсчётов как есть. Пустой
                // диапазон остаётся на середине: его вклад ноль при любой
                // энергии; остаётся на середине и диапазон, где приписанное
                // меньше пятой части отсчётов (<see cref="RepresentativeMinShare"/>):
                // там центр тяжести — шум вычитания. (`S194`, П159) Выше порога
                // доверие растёт плавно (<see cref="ShareTrust"/>), без скачка.
                //
                // ⚠ Почему ПЕРВЫЙ момент, а не пик или квантиль: из всех мер
                // положения только среднее не меняется от симметричного
                // размытия. Цена: у линии, чей собственный комптон заходит в
                // её диапазон (2614.5 в верхнем диапазоне у малых кристаллов),
                // центр тяжести строки почти не растёт с её энергией, и
                // энергия по нему определена плохо (замер П145: ASN16 +8.7 %
                // без разрешения).
                if (matrix != null)
                {
                    SolveJoint(input, ranges, countsMoment, projection, plain, cells, fractions, fractionMoments,
                               plain == null ? null : floorFractions, ownAtCentre, maxOwn, maxOwnRange, seconds);
                }
                else
                {
                    PeakPath(input, ranges, countsMoment, ownAtCentre, maxOwn, maxOwnRange, seconds);
                }

                // Сумма и ошибка — сверху вниз, как прежде: порядок сложения
                // держит путь «≈» побитово прежним.
                for (int k = bins - 1; k >= 0; k--)
                {
                    DoseRateRange r = ranges[k];
                    if (r.Skipped || !(r.Attributed > 0.0))
                    {
                        continue;
                    }

                    rate += r.DoseRate;
                    // Пуассон по СЫРЫМ отсчётам диапазона: вычитание
                    // континуума шум не убирает, а долю его увеличивает.
                    double relative = Math.Sqrt(Math.Max(r.Counts, 1.0)) / r.Attributed;
                    errorSquares += r.DoseRate * relative * (r.DoseRate * relative);
                }

                // Пропущенные диапазоны — вон из покрытия; диапазоны в полосе
                // плавного пола (`S192`) — в меру своего веса.
                for (int k = 0; k < bins; k++)
                {
                    double weight = ranges[k].Skipped ? 0.0 : ranges[k].Weight;
                    if (weight == 1.0)
                    {
                        continue;
                    }

                    int startch, endch;
                    ChannelSpan(calibration, energySpectrum.NumberOfChannels, covered.Length,
                                ranges[k].LowKev, ranges[k].HighKev, out startch, out endch);
                    for (int i = startch; i < endch; i++)
                    {
                        covered[i] = weight;
                    }
                }

                doseRate.Ranges.AddRange(ranges);
            }
            catch (DoseRateRefusalException ex)
            {
                doseRate.Ranges.Clear();
                doseRate.Refusal = ex.Message;
                return doseRate;
            }

            // Доля отсчётов, попавшая в диапазоны сетки (`C4(в)`).
            //
            // ⛔ Канал переполнения не считается НИ В ЗНАМЕНАТЕЛЕ, ни в числителе
            // (`A203`). Он не измерение, и держать его в знаменателе значит
            // тянуть покрытие вниз тем, что покрыть НЕЛЬЗЯ.
            double total = 0.0;
            double inside = 0.0;
            for (int i = 0; i < energySpectrum.Spectrum.Length; i++)
            {
                if (overflow[i]) continue;
                total += energySpectrum.Spectrum[i];
                if (covered[i] == 1.0)
                {
                    inside += energySpectrum.Spectrum[i];
                }
                else if (covered[i] > 0.0)
                {
                    inside += covered[i] * energySpectrum.Spectrum[i];
                }
            }

            doseRate.Coverage = total > 0.0 ? inside / total : -1.0;

            if (double.IsNaN(rate) || double.IsInfinity(rate))
            {
                doseRate.Ranges.Clear();
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateNotFinite",
                    "Dose rate: the sum over the ranges is not a finite number — the efficiency input is unusable.");
                return doseRate;
            }

            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            double errorLevel = (double)globalConfig.MeasurementConfig.ErrorLevel;
            doseRate.Rate = rate;
            doseRate.Error = errorLevel * Math.Sqrt(errorSquares);
            return doseRate;
        }

        /// <summary>
        /// Путь пиковой кривой («≈»): строки нет, континуума нет, каждый
        /// диапазон — сам по себе. Числа — побитово прежние (`S185` его не
        /// касается: здесь нечего сворачивать и нечего развязывать).
        /// </summary>
        static void PeakPath(DoseRateInput input, DoseRateRange[] ranges, double[] countsMoment, double[] ownAtCentre,
                             double maxOwn, int maxOwnRange, double seconds)
        {
            for (int k = ranges.Length - 1; k >= 0; k--)
            {
                DoseRateRange r = ranges[k];
                double explained = 0.0;

                r.Explained = explained;
                r.Attributed = Math.Max(0.0, r.Counts - explained);
                r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(r.CenterKev);

                // (`S186`) Пол — только НИЖЕ максимума кривой; выше него
                // диапазон снимается лишь пустой строкой (доля не > 0).
                // (`S192`) Пол плавный: в полосе над ним доза диапазона — с
                // весом <see cref="FloorWeight"/>.
                bool floorApplies = k < maxOwnRange;
                r.Weight = FloorWeight(ownAtCentre[k], maxOwn, floorApplies);
                if (!(r.Weight > 0.0))
                {
                    // Диапазон не приписывается никому: его отсчёты
                    // выходят из покрытия, о чём скажет приписка.
                    r.Skipped = true;
                    r.Weight = 0.0;
                    r.Cps = r.Attributed / seconds;
                    continue;
                }

                // (`S194`, П159) Доверие энергии по доле приписанного — плавное.
                // Здесь континуума нет (объяснено 0) и доля — 1: побитово прежнее.
                double shareTrust = ShareTrust(r.Attributed, r.Counts);
                if (shareTrust > 0.0)
                {
                    double target = countsMoment[k] / (r.Counts - explained);
                    if (double.IsNaN(target) || double.IsInfinity(target))
                    {
                        target = r.CenterKev;
                    }

                    double energy = Clamp(target, r.LowKev, r.HighKev);
                    double own = input.EfficiencyAt(energy);

                    // Доля на представительной энергии ниже пола — это не
                    // линия, а остаток у края, где эффективность круто
                    // падает (замер П145: остаток 3 % отсчётов в 13…17 кэВ
                    // уехал к низу 13.12 с долей в 18 раз меньше, чем на
                    // середине, и дал +2.6 % показания): остаёмся на середине.
                    // (`S186`) Выше максимума кривой пола нет и здесь — там
                    // нет и «края, где эффективность круто падает» к нулю.
                    // (`S192`, П157) В полосе над полом — энергия между
                    // серединой и подобранной, в меру веса доли на подобранной.
                    double trust = double.IsNaN(own) || double.IsInfinity(own) ? 0.0 : FloorWeight(own, maxOwn, floorApplies);
                    if (shareTrust != 1.0)
                    {
                        trust *= shareTrust;
                    }

                    if (trust > 0.0 && trust < 1.0)
                    {
                        energy = r.CenterKev + trust * (energy - r.CenterKev);
                        own = input.EfficiencyAt(energy);
                    }

                    if (trust > 0.0)
                    {
                        r.RepresentativeKev = energy;
                        r.OwnEfficiency = own;
                        r.Efficiency = input.EfficiencyAt(energy);
                        r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(energy);
                    }
                }

                r.Cps = r.Attributed / seconds;
                double emitted = r.Cps / r.OwnEfficiency;    // N_k, квантов/с
                r.FluenceRate = emitted * input.FluencePerPhoton;
                if (r.Weight != 1.0)
                {
                    r.FluenceRate *= r.Weight;
                }

                r.DoseRate = r.FluenceRate * r.DoseRatePerFluenceRate;
            }
        }

        /// <summary>Сколько раз уточнять энергии всех диапазонов в совместном решении (`S185`).</summary>
        const int JointIterations = 12;

        /// <summary>
        /// Допуск схождения энергии диапазона в совместном решении, кэВ:
        /// 0.02 кэВ или 1e-4 энергии — что больше. Показание от энергии зависит
        /// через ε и Ḣ/φ̇ примерно линейно, и 1e-4 энергии — это ~1e-4 его доли.
        /// </summary>
        static double Tolerance(double energy)
        {
            return Math.Max(0.02, 1e-4 * energy);
        }

        /// <summary>
        /// ⛔ (`S185`, П153 24.09.2026) ПУТЬ МАТРИЦЫ — СОВМЕСТНОЕ РЕШЕНИЕ ПО
        /// ВСЕМ ДИАПАЗОНАМ, а не вычитание сверху вниз.
        ///
        /// Неизвестные — число квантов Q_k линии каждого диапазона и её
        /// энергия E_k; уравнения — отсчёты каждого диапазона
        ///
        ///     C_i = Σ_k Q_k · s_i(E_k)
        ///
        /// (s_i(E) — доля строки энергии E в диапазоне i, с разрешением
        /// прибора, <see cref="RowProjection"/>) и первый момент в нём, по
        /// которому подбирается E_k (`AMBER77`). Прежний проход «сверху вниз»
        /// решал ту же систему, пока она была ТРЕУГОЛЬНОЙ — линия давала
        /// отсчёты только в свой диапазон и НИЖЕ. Строка с разрешением даёт
        /// их и ВЫШЕ (хвост пика за верхней границей): проход сверху вниз
        /// принимал этот хвост за линию верхнего диапазона, и та «объясняла»
        /// своим комптоном пик настоящей линии ниже — замер П151 на свёртке
        /// без развязки: RC-103 59.5 +2.6 → +10.3 %, G1S 1332.5 до +11.7 %.
        ///
        /// Решение. Проход 0 — прежний, сверху вниз (без утечки вверх он и
        /// есть решение). Затем по очереди, до схождения энергий
        /// (<see cref="JointIterations"/>, <see cref="Tolerance"/>):
        /// при известных E — квадратная линейная система на Q целиком
        /// (<see cref="SolveCounts"/>); при известных Q — энергия каждого
        /// диапазона по центру тяжести его отсчётов за вычетом отклика ВСЕХ
        /// прочих линий, и выше, и ниже.
        ///
        /// Отрицательный Q не бывает: диапазон, которому решение даёт меньше
        /// нуля (его отсчёты с избытком объяснены чужими линиями), выходит из
        /// системы вместе со СВОИМ уравнением — прежнее «приписано
        /// max(0, отсчёты − объяснено)»: избыток объяснения в нём не тянет
        /// вниз линии, которые его объяснили. Наименьшие квадраты (NNLS)
        /// нарочно НЕ взяты: у них пересчитанный комптон низа шкалы, где
        /// модель континуума грубее пика, правил бы число квантов верхних
        /// линий. Исключение одно — сосед, в который РАЗРЕШЕНИЕМ протёк пик
        /// линии (<see cref="Spills"/>, <see cref="MergeLeaks"/>): там окно
        /// центра тяжести и уравнение отсчётов — общие на два диапазона, иначе
        /// ошибка ширины модели садится прямо в долю линии.
        ///
        /// ⚠ Ширина берётся из калибровки ширины САМОГО спектра; расходится
        /// она с настоящей — остаётся ошибка. Замер П153 (`DoseGridProbe`,
        /// синтетика шириной `manifest.csv`): AS80 59.5 кэВ, где ширины
        /// разнятся вдвое, +7.4 % (прежде +9.7 %); при своей ширине все линии
        /// всех четырёх приборов — в пределах 0.2 %.
        /// </summary>
        static void SolveJoint(DoseRateInput input, DoseRateRange[] ranges, double[] countsMoment,
                               RowProjection projection, RowProjection plain, int cells, double[][] centreShare,
                               double[][] centreMoment, double[][] centrePlain, double[] floorOwn, double maxOwn,
                               int maxOwnRange, double seconds)
        {
            int bins = ranges.Length;
            var active = new bool[bins];
            var share = new double[bins][];
            var moment = new double[bins][];
            var plainShare = new double[bins][];
            var energy = new double[bins];
            var fitted = new bool[bins];
            var counts = new double[bins];
            var lastTarget = new double[bins];
            var lastGroup = new int[bins];
            var slope = new double[bins];
            // Последний подбор энергии диапазона — принятый или отвергнутый
            // полом: энергия и строка на ней (доли, моменты, доли без
            // разрешения). Подбор повторяется, только если сдвинулась цель.
            var tried = new double[bins][][];
            var triedEnergy = new double[bins];
            // (`S192`) Строка на энергии между серединой и подобранной — у
            // диапазона, чья доля на подобранной энергии в полосе пола.
            var blended = new double[bins][][];
            var blendedEnergy = new double[bins];
            var merge = new int[bins];
            // (`S195`) Держит ли край диапазона подбор энергии: по диапазону,
            // группе соседей в окне и краю — 0 не знаем, 1 держит, −1 нет.
            var edges = new sbyte[bins * 8];
            // (`S192`) Вес диапазона по плавному полу: доза линии диапазона —
            // с этим весом, и её отклик в ЧУЖИХ диапазонах (комптон, хвост
            // пика) объясняет их отсчёты с тем же весом — иначе вход диапазона
            // в систему на самом поле переставлял бы соседей скачком.
            var weight = new double[bins];
            for (int k = 0; k < bins; k++)
            {
                counts[k] = ranges[k].Counts;
                share[k] = centreShare[k];
                moment[k] = centreMoment[k];
                plainShare[k] = centrePlain == null ? null : centrePlain[k];
                energy[k] = ranges[k].CenterKev;
                lastTarget[k] = double.NaN;
                slope[k] = double.NaN;
                merge[k] = -1;
                // (`S186`) Пол — только НИЖЕ максимума кривой; выше него
                // диапазон снимается лишь пустой строкой (доля не > 0).
                bool floorApplies = k < maxOwnRange;
                weight[k] = FloorWeight(floorOwn[k], maxOwn, floorApplies);
                active[k] = weight[k] > 0.0;
            }

            // Подобрать энергию линии диапазона k при известных прочих
            // линиях; в ответ — на сколько она сдвинулась, кэВ. Окно центра
            // тяжести — диапазон k и слитые с ним соседи.
            Func<int, double[], double> refit = (k, q) =>
            {
                double explained = 0.0;
                for (int i = 0; i < bins; i++)
                {
                    if (i != k && q[i] > 0.0)
                    {
                        explained += weight[i] * q[i] * share[i][k];
                    }
                }

                // Окно — с весами: сосед в полосе плавного пола (`S192`) входит
                // в него в меру своего веса.
                var window = new double[bins];
                window[k] = 1.0;
                int group = 0;
                for (int side = 0; side < 2; side++)
                {
                    int i = side == 0 ? k - 1 : k + 1;
                    if (i >= 0 && i < bins && (merge[i] == k || Spills(k, i, q, share, plainShare, active, merge)))
                    {
                        window[i] = weight[i];
                        group |= 1 << side;
                    }
                }

                double newEnergy = ranges[k].CenterKev;
                double[] newShare = centreShare[k], newMoment = centreMoment[k];
                double[] newPlain = centrePlain == null ? null : centrePlain[k];
                bool fit = false;
                double attributed = counts[k] - explained;
                // (`S194`, П159) Доверие подобранной энергии по доле
                // приписанного — плавное, <see cref="ShareTrust"/>.
                double shareTrust = ShareTrust(attributed, counts[k]);
                if (shareTrust > 0.0)
                {
                    double target = WindowTarget(k, window, counts, countsMoment, q, share, moment, ranges, weight);
                    double known = slope[k] > 0.05 && slope[k] < 20.0 ? slope[k] : 1.0;
                    if (!(tried[k] != null && group == lastGroup[k]
                          && Math.Abs(target - lastTarget[k]) < 0.5 * Tolerance(ranges[k].CenterKev) * known))
                    {
                        // Цель сдвинулась на половину допуска схождения в
                        // энергии или больше — подбор заново, от прошлой
                        // энергии и с её уже известной строкой.
                        double sl = group == lastGroup[k] ? slope[k] : double.NaN;
                        double[] s, m, ps;
                        double start = tried[k] != null ? triedEnergy[k] : target;
                        double e = Representative(input, ranges, k, window, target, start, ref sl, cells, projection,
                                                  plain, tried[k], edges, group, out s, out m, out ps);
                        slope[k] = sl;
                        lastTarget[k] = target;
                        lastGroup[k] = group;
                        triedEnergy[k] = e;
                        tried[k] = new[] { s, m, ps };
                    }

                    double[] ts = tried[k][0];
                    double own = ts[k];

                    // Доля на представительной энергии ниже пола — это не
                    // линия, а остаток у края, где эффективность круто
                    // падает (замер П145: остаток 3 % отсчётов в 13…17 кэВ
                    // уехал к низу 13.12 с долей в 18 раз меньше, чем на
                    // середине, и дал +2.6 % показания): остаёмся на середине.
                    // (`S186`) Выше максимума кривой пола нет и здесь.
                    //
                    // (`S192`, П157) И здесь пол плавный: в полосе над ним
                    // энергия — между серединой и подобранной, в меру веса
                    // доли на подобранной. Прежний обрез переставлял энергию
                    // скачком: `ASN16_Cs137_10cm`, 17.2…22.6 кэВ — 18.05 → 19.90
                    // кэВ на сдвиге порога 0.9 %, показание −0.76 %.
                    bool floorApplies = k < maxOwnRange;
                    double trust = double.IsNaN(own) || double.IsInfinity(own) ? 0.0 : FloorWeight(own, maxOwn, floorApplies);
                    // (`S194`, П159) И доверие по доле приписанного: энергия —
                    // между серединой и подобранной в меру ОБОИХ доверий.
                    if (shareTrust != 1.0)
                    {
                        trust *= shareTrust;
                    }

                    if (trust == 1.0)
                    {
                        newEnergy = triedEnergy[k];
                        newShare = ts;
                        newMoment = tried[k][1];
                        newPlain = tried[k][2];
                        fit = true;
                    }
                    else if (trust > 0.0)
                    {
                        double blend = ranges[k].CenterKev + trust * (triedEnergy[k] - ranges[k].CenterKev);
                        if (!(blended[k] != null && blendedEnergy[k] == blend))
                        {
                            double[] bs, bm, bp;
                            RowAt(input, blend, cells, projection, plain, out bs, out bm, out bp);
                            blended[k] = new[] { bs, bm, bp };
                            blendedEnergy[k] = blend;
                        }

                        newEnergy = blend;
                        newShare = blended[k][0];
                        newMoment = blended[k][1];
                        newPlain = blended[k][2];
                        fit = true;
                    }
                }

                double moved = Math.Abs(newEnergy - energy[k]);
                energy[k] = newEnergy;
                share[k] = newShare;
                moment[k] = newMoment;
                plainShare[k] = newPlain;
                fitted[k] = fit;
                return moved;
            };

            // Проход 0 — прежний, сверху вниз: линия диапазона k видит линии
            // выше уже найденными. Без утечки вверх это и есть решение; с ней —
            // хорошее начало для совместных проходов.
            var quanta = new double[bins];
            for (int k = bins - 1; k >= 0; k--)
            {
                if (!active[k])
                {
                    continue;
                }

                refit(k, quanta);
                double explained = 0.0;
                for (int i = k + 1; i < bins; i++)
                {
                    explained += weight[i] * quanta[i] * share[i][k];
                }

                double attributed = counts[k] - explained;
                quanta[k] = attributed > 0.0 && share[k][k] > 0.0 ? attributed / share[k][k] : 0.0;
            }

            // Совместные проходы: система целиком при известных энергиях
            // (со слиянием диапазонов, `MergeLeaks`), затем энергии при
            // известных числах квантов — до схождения.
            var shifts = new System.Collections.Generic.List<double>();
            var banned = new bool[bins];
            // (`S195`) Последний сдвиг энергии каждого диапазона, со знаком.
            var lastMove = new double[bins];
            for (int pass = 1; ; pass++)
            {
                quanta = SolveCounts(share, counts, active, merge, weight);
                if (pass > JointIterations)
                {
                    break;
                }

                bool changed = MergeLeaks(share, plainShare, counts, active, quanta, merge, banned, weight);
                if (changed)
                {
                    quanta = SolveCounts(share, counts, active, merge, weight);
                }

                double shift = 0.0;
                bool converged = true;
                bool reversed = true;
                for (int k = 0; k < bins; k++)
                {
                    if (active[k] && merge[k] < 0)
                    {
                        double before = energy[k];
                        double moved = refit(k, quanta);
                        double move = energy[k] - before;
                        if (moved >= Tolerance(ranges[k].CenterKev) && !(move * lastMove[k] < 0.0))
                        {
                            reversed = false;
                        }

                        lastMove[k] = move;
                        shift = Math.Max(shift, moved);
                        converged &= moved < Tolerance(ranges[k].CenterKev);
                    }
                }

                // Качается (соседние диапазоны перетягивают энергию туда-сюда
                // на сотые кэВ: сдвиг тот же, что два прохода назад, и мал —
                // замер П153 на RC103_Cs137_0cm, 33.57 ↔ 33.59 кэВ) — тоже
                // конец: дальше точность не растёт.
                //
                // ⛔ (`S195`, П160) «Качается» — только если КАЖДЫЙ не сошедшийся
                // диапазон сменил направление сдвига. Прежде признаком было
                // одно «сдвиг тот же, что два прохода назад», и его давал и
                // медленный ДРЕЙФ в одну сторону (`ASN16_Cs137_10cm`, 17…30 кэВ:
                // +0.05…0.1 кэВ за проход): выход то по «качается» на 11-м
                // проходе, то по пределу на 12-м — ступенька показания 0.011 %
                // на шаге набора 1/3200 (`DoseShareProbe`).
                shifts.Add(shift);
                int n = shifts.Count;
                bool swinging = !changed && reversed && n >= 3 && shift < 0.1
                                && Math.Abs(shift - shifts[n - 3]) <= 0.2 * shifts[n - 3];
                if (converged && !changed || swinging)
                {
                    quanta = SolveCounts(share, counts, active, merge, weight);
                    break;
                }
            }

            for (int k = 0; k < bins; k++)
            {
                DoseRateRange r = ranges[k];
                double explained = 0.0;
                for (int i = 0; i < bins; i++)
                {
                    if (i != k && quanta[i] > 0.0)
                    {
                        explained += weight[i] * quanta[i] * share[i][k];
                    }
                }

                r.Explained = explained;
                r.Weight = weight[k];
                if (!active[k])
                {
                    // Диапазон не приписывается никому: его отсчёты
                    // выходят из покрытия, о чём скажет приписка.
                    r.Skipped = true;
                    r.Weight = 0.0;
                    r.Attributed = Math.Max(0.0, r.Counts - explained);
                    r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(r.CenterKev);
                    r.Cps = r.Attributed / seconds;
                    continue;
                }

                r.OwnEfficiency = share[k][k];
                if (fitted[k] && merge[k] < 0)
                {
                    r.RepresentativeKev = energy[k];
                    r.Efficiency = input.EfficiencyAt(energy[k]);
                }

                r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(r.RepresentativeKev);
                r.Attributed = quanta[k] * share[k][k];
                r.Cps = r.Attributed / seconds;
                r.FluenceRate = quanta[k] / seconds * input.FluencePerPhoton;
                if (weight[k] != 1.0)
                {
                    // (`S192`) Диапазон в полосе плавного пола.
                    r.FluenceRate *= weight[k];
                }

                r.DoseRate = r.FluenceRate * r.DoseRatePerFluenceRate;
            }
        }

        /// <summary>
        /// ⛔ (`S185`, П153) СЛИЯНИЕ ДИАПАЗОНА С СОСЕДОМ, ЧЕЙ ПИК В НЕГО
        /// ПРОТЁК. Диапазон без своей линии (решение дало ноль), чьи отсчёты
        /// с избытком объяснены чужими линиями, сливается с соседом, если пик
        /// соседа протекает в него РАЗРЕШЕНИЕМ (доля строки соседа в нём со
        /// свёрткой минус без неё) не меньше <see cref="SpillShare"/> отсчётов
        /// соседа в своём диапазоне. Слитый диапазон своей линии не имеет,
        /// его уравнение складывается с уравнением соседа, и число квантов
        /// соседа решается по сумме отсчётов обоих — пик у их общей границы
        /// там лежит целиком, и его доля от ширины модели почти не зависит.
        ///
        /// Замер П153 (`DoseGridProbe`, ширина синтетики — `manifest.csv`, у.же
        /// калибровки ширины спектра на 6…19 %): без слияния избыток
        /// отбрасывался (прежнее «приписано max(0, …)»), и линия у границы
        /// делилась на долю более широкого пика модели — G1S 1332.5 (4.3 кэВ
        /// над границей) +11.7 %, AS80 1332.5 (2.4σ над ней) +2.6 %; со
        /// слиянием +0.04 и +0.09 %. Отвергнутое правило «сливать, только если
        /// избыток не больше утечки» AS80 не лечило: завышенное число квантов
        /// завышает и свой континуум линии в соседе, и избыток всегда выходил
        /// чуть больше утечки (2.26 М против 2.19 М).
        ///
        /// ⚠ Цена: избыток континуума модели в таком соседе теперь входит в
        /// число квантов линии (по доле соседа в сумме их отсчётов), а не
        /// отбрасывается. Слияние снимается, когда у соседа линии не стало.
        /// Возвращает, было ли что-то изменено.
        /// </summary>
        static bool MergeLeaks(double[][] share, double[][] plainShare, double[] counts, bool[] active,
                               double[] quanta, int[] merge, bool[] banned, double[] weight)
        {
            int bins = counts.Length;
            bool changed = false;
            for (int i = 0; i < bins; i++)
            {
                if (!active[i])
                {
                    continue;
                }

                if (merge[i] >= 0)
                {
                    if (!(quanta[merge[i]] > 0.0))
                    {
                        merge[i] = -1;
                        // Второй раз не сливается: иначе слияние и его снятие
                        // чередуются через проход (RC103_Cs137_0cm, 13…17 кэВ).
                        banned[i] = true;
                        changed = true;
                    }

                    continue;
                }

                if (quanta[i] > 0.0 || banned[i])
                {
                    continue;
                }

                double over = -counts[i];
                for (int j = 0; j < bins; j++)
                {
                    if (j != i && quanta[j] > 0.0)
                    {
                        over += weight[j] * quanta[j] * share[j][i];
                    }
                }

                if (!(over > 0.0))
                {
                    continue;
                }

                int best = -1;
                double bestLeak = 0.0;
                for (int j = i - 1; j <= i + 1; j += 2)
                {
                    if (j < 0 || j >= bins || !active[j] || merge[j] >= 0 || !(quanta[j] > 0.0)
                        || plainShare[j] == null)
                    {
                        continue;
                    }

                    double leak = weight[j] * quanta[j] * (share[j][i] - plainShare[j][i]);
                    if (leak > bestLeak)
                    {
                        best = j;
                        bestLeak = leak;
                    }
                }

                if (best >= 0 && bestLeak >= SpillShare * quanta[best] * share[best][best])
                {
                    // У диапазона, с которым сливаются, своих слитых не бывает
                    // цепочкой: он сам не слит (`merge[best] < 0`).
                    merge[i] = best;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>Какая часть своих отсчётов линии, протёкшая разрешением к соседу, уже делает окно общим (`S185`).</summary>
        const double SpillShare = 0.01;

        /// <summary>
        /// (`S185`) Берётся ли сосед <paramref name="i"/> в окно центра
        /// тяжести линии <paramref name="k"/>: своей линии у соседа нет, а пик
        /// линии k протекает в него РАЗРЕШЕНИЕМ (доля строки со свёрткой минус
        /// без неё) не меньше <see cref="SpillShare"/> её отсчётов в своём
        /// диапазоне. Центр тяжести пика, усечённого границей, почти не растёт
        /// с энергией линии и уходит от неё вместе с ошибкой ширины модели
        /// (замер П153: AS80 1332.5 при ширине модели на 19 % шире настоящей —
        /// энергия −2.4 кэВ, своя доля −3.4 %); у пика, лежащего в окне целиком,
        /// первый момент от ширины не зависит.
        /// </summary>
        static bool Spills(int k, int i, double[] quanta, double[][] share, double[][] plainShare, bool[] active,
                           int[] merge)
        {
            if (!active[i] || merge[i] >= 0 || quanta[i] > 0.0 || !(quanta[k] > 0.0) || plainShare[k] == null)
            {
                return false;
            }

            return share[k][i] - plainShare[k][i] >= SpillShare * share[k][k];
        }

        /// <summary>
        /// (`S185`) Центр тяжести отсчётов окна <paramref name="window"/> за
        /// вычетом отклика всех линий, кроме линии диапазона <paramref name="k"/>.
        /// Нечего приписать — середина диапазона k.
        /// </summary>
        static double WindowTarget(int k, double[] window, double[] counts, double[] countsMoment, double[] quanta,
                                   double[][] share, double[][] moment, DoseRateRange[] ranges, double[] weight)
        {
            double mass = 0.0, first = 0.0;
            for (int i = 0; i < window.Length; i++)
            {
                if (!(window[i] > 0.0))
                {
                    continue;
                }

                // (`S192`) Диапазон окна — в меру своего веса, отклик чужих
                // линий — в меру их весов; при весах 1 — прежние числа побитово.
                double win = window[i];
                if (win == 1.0)
                {
                    mass += counts[i];
                    first += countsMoment[i];
                    for (int j = 0; j < quanta.Length; j++)
                    {
                        if (j != k && quanta[j] > 0.0)
                        {
                            mass -= weight[j] * quanta[j] * share[j][i];
                            first -= weight[j] * quanta[j] * moment[j][i];
                        }
                    }

                    continue;
                }

                double part = counts[i], partFirst = countsMoment[i];
                for (int j = 0; j < quanta.Length; j++)
                {
                    if (j != k && quanta[j] > 0.0)
                    {
                        part -= weight[j] * quanta[j] * share[j][i];
                        partFirst -= weight[j] * quanta[j] * moment[j][i];
                    }
                }

                mass += win * part;
                first += win * partFirst;
            }

            double target = first / mass;
            return mass > 0.0 && !double.IsNaN(target) && !double.IsInfinity(target) ? target : ranges[k].CenterKev;
        }

        /// <summary>
        /// (`S185`) Квадратная система `C_i = Σ_k Q_k·share[k][i]` по
        /// диапазонам <paramref name="active"/>: исключение Гаусса с выбором
        /// ведущего. Слитый диапазон (<paramref name="merge"/>) своей
        /// неизвестной не имеет, а его уравнение складывается с уравнением
        /// того, с кем он слит. Неизвестная с отрицательным решением (или
        /// вырожденным столбцом) выходит из системы вместе со своим уравнением
        /// (и слитыми с ним), и система решается заново — не больше числа
        /// диапазонов раз. Вышедшим — ноль.
        ///
        /// (`S192`, П157) Веса плавного пола <paramref name="weight"/>: линия
        /// диапазона в полосе объясняет отсчёты ЧУЖИХ диапазонов с его весом
        /// (свой диапазон и слитые с ним — целиком), и уравнение слитого
        /// диапазона в полосе складывается с уравнением соседа с его весом.
        /// При весах 1 — прежняя система побитово.
        /// </summary>
        static double[] SolveCounts(double[][] share, double[] counts, bool[] active, int[] merge, double[] weight)
        {
            int bins = counts.Length;
            var quanta = new double[bins];
            var use = new bool[bins];
            for (int k = 0; k < bins; k++)
            {
                use[k] = active[k] && merge[k] < 0;
            }

            var index = new int[bins];
            for (int round = 0; round <= bins; round++)
            {
                int m = 0;
                for (int k = 0; k < bins; k++)
                {
                    if (use[k])
                    {
                        index[m++] = k;
                    }
                }

                if (m == 0)
                {
                    break;
                }

                // a[r][c] — доля строки линии index[c] в диапазоне index[r]
                // вместе со слитыми с ним.
                var a = new double[m][];
                var x = new double[m];
                for (int r = 0; r < m; r++)
                {
                    a[r] = new double[m];
                    int row = index[r];
                    for (int i = Math.Max(0, row - 1); i <= Math.Min(bins - 1, row + 1); i++)
                    {
                        if (i != row && !(active[i] && merge[i] == row))
                        {
                            continue;
                        }

                        // Уравнение слитого диапазона — с его весом.
                        double rowWeight = i == row ? 1.0 : weight[i];
                        for (int c = 0; c < m; c++)
                        {
                            int line = index[c];
                            double lineWeight = i == line || merge[i] == line ? 1.0 : weight[line];
                            double coefficient = rowWeight * lineWeight;
                            a[r][c] += coefficient == 1.0 ? share[line][i] : coefficient * share[line][i];
                        }

                        x[r] += rowWeight == 1.0 ? counts[i] : rowWeight * counts[i];
                    }
                }

                int degenerate = -1;
                for (int c = 0; c < m && degenerate < 0; c++)
                {
                    int pivot = c;
                    for (int r = c + 1; r < m; r++)
                    {
                        if (Math.Abs(a[r][c]) > Math.Abs(a[pivot][c]))
                        {
                            pivot = r;
                        }
                    }

                    if (!(Math.Abs(a[pivot][c]) > 1e-300) || double.IsInfinity(a[pivot][c]))
                    {
                        degenerate = c;
                        break;
                    }

                    if (pivot != c)
                    {
                        double[] t = a[pivot]; a[pivot] = a[c]; a[c] = t;
                        double tx = x[pivot]; x[pivot] = x[c]; x[c] = tx;
                    }

                    for (int r = c + 1; r < m; r++)
                    {
                        double f = a[r][c] / a[c][c];
                        if (f == 0.0)
                        {
                            continue;
                        }

                        for (int j = c; j < m; j++)
                        {
                            a[r][j] -= f * a[c][j];
                        }

                        x[r] -= f * x[c];
                    }
                }

                if (degenerate >= 0)
                {
                    use[index[degenerate]] = false;
                    continue;
                }

                for (int c = m - 1; c >= 0; c--)
                {
                    double s = x[c];
                    for (int j = c + 1; j < m; j++)
                    {
                        s -= a[c][j] * x[j];
                    }

                    x[c] = s / a[c][c];
                }

                bool negative = false;
                for (int c = 0; c < m; c++)
                {
                    if (!(x[c] >= 0.0))
                    {
                        use[index[c]] = false;
                        negative = true;
                    }
                }

                if (!negative)
                {
                    for (int c = 0; c < m; c++)
                    {
                        quanta[index[c]] = x[c];
                    }

                    break;
                }
            }

            return quanta;
        }

        readonly object cacheSync = new object();

        EfficiencyConfigData cachedEfficiency;

        DoseRateInput cachedInput;

        string cachedStamp;

        string lastMatrixNote;
    }
}
