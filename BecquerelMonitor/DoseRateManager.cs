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
        /// центру, как отсчёты (<see cref="Spread"/>, <see cref="ChannelLayout"/>;
        /// `AMBER82`, `AMBER101`); без раскладки каналов — по перекрытию бина с
        /// границами диапазона.
        ///
        /// ⚠ Здесь строка берётся на СЕРЕДИНЕ диапазона — это доли для пола
        /// эффективности (<see cref="MinOwnEfficiencyFraction"/>), свойство
        /// сетки, а не спектра. Делит и вычитает расчёт по строке на
        /// ПРЕДСТАВИТЕЛЬНОЙ энергии диапазона (`AMBER77`, см. <see cref="Calculate(ResultData, DoseRateInput)"/>).
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
        /// континуум ИЗВЕСТЕН: сверху вниз он вычитается, и знаменателем идёт
        /// доля отклика линии В СВОЁМ диапазоне. Для отклика без континуума
        /// (одна дельта в пике) обе записи тождественны.
        /// </summary>
        static double[][] ResponseFractions(DoseRateInput input, DoseRateRange[] ranges, ChannelLayout layout)
        {
            ResponseMatrix matrix = input.Matrix;
            int bins = ranges.Length;
            double step = matrix.BinKev;
            if (!(step > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateEmptyMatrix", "Dose rate: the response matrix of the curve has no rows."));
            }

            int cells = Cells(ranges, step);
            var fractions = new double[bins][];
            for (int j = 0; j < bins; j++)
            {
                double[] row = matrix.Evaluate(ranges[j].CenterKev, cells);
                var share = new double[bins];
                Spread(row, step, ranges, share, null, layout);
                fractions[j] = share;
            }

            return fractions;
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
            /// сетки по перекрытию (<see cref="Spread"/>).
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

                for (int k = 0; k < ranges.Length; k++)
                {
                    ChannelSpan(calibration, channels, length, ranges[k].LowKev, ranges[k].HighKev,
                                out layout.Start[k], out layout.End[k]);
                }

                return layout;
            }
        }

        /// <summary>
        /// (`AMBER77`) Представительная энергия берётся по центру тяжести,
        /// только если приписанное — не меньше этой доли отсчётов диапазона.
        /// Ниже — это разность двух больших чисел (отсчёты минус континуум
        /// сверху), и её центр тяжести — шум вычитания: при рассогласовании
        /// континуума на δ он сдвигается на ~δ·(объяснено/приписано) ширины
        /// диапазона. Пятая часть держит сдвиг в пределах трети ширины при
        /// δ ≈ 5 %. Ниже порога диапазон остаётся на середине, как было.
        /// </summary>
        public const double RepresentativeMinShare = 0.2;

        /// <summary>Сколько раз уточнять представительную энергию (`AMBER77`).</summary>
        const int RepresentativeIterations = 8;

        /// <summary>Когда уточнение останавливается, кэВ.</summary>
        const double RepresentativeToleranceKev = 1e-3;

        /// <summary>
        /// (`AMBER77`) Энергия линии, чья строка матрицы даёт в диапазоне
        /// <paramref name="k"/> центр тяжести <paramref name="target"/>, —
        /// подбором `E ← E + (цель − центр строки(E))/наклон` (секущая) от
        /// `E = цель`, не больше <see cref="RepresentativeIterations"/> шагов; энергия
        /// зажата в диапазон и в область матрицы. Возвращает её и строку на
        /// ней, разложенную по сетке (<see cref="Spread"/>).
        /// </summary>
        static double Representative(DoseRateInput input, DoseRateRange[] ranges, int k, double target, int cells,
                                     ChannelLayout layout, out double[] share, out double[] moment)
        {
            ResponseMatrix matrix = input.Matrix;
            double lo = Math.Max(ranges[k].LowKev, input.MinKev);
            double hi = Math.Min(ranges[k].HighKev, input.MaxKev);
            double energy = Clamp(target, lo, hi);
            double previousEnergy = double.NaN, previousCentre = double.NaN;
            for (int iteration = 0; ; iteration++)
            {
                double[] row = matrix.Evaluate(energy, cells);
                share = new double[ranges.Length];
                moment = new double[ranges.Length];
                Spread(row, matrix.BinKev, ranges, share, moment, layout);
                if (iteration >= RepresentativeIterations || !(share[k] > 0.0))
                {
                    return energy;
                }

                // Секущая, а не шаг «один к одному»: у линии, чей собственный
                // комптон заходит в диапазон (2614.5 в [2286, 3000)), центр
                // строки растёт МЕДЛЕННЕЕ её энергии, и единичный шаг за
                // четыре попытки не доходил (2531 вместо 2614, показание +38 %).
                double centre = moment[k] / share[k];
                double gain = 1.0;
                if (!double.IsNaN(previousEnergy))
                {
                    double slope = (centre - previousCentre) / (energy - previousEnergy);
                    if (slope > 0.05 && slope < 20.0)
                    {
                        gain = 1.0 / slope;
                    }
                }

                double next = Clamp(energy + gain * (target - centre), lo, hi);
                if (Math.Abs(next - energy) < RepresentativeToleranceKev)
                {
                    return energy;
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

        /// <summary>Длина строки отклика, накрывающая сетку, с запасом на верхний бин.</summary>
        static int Cells(DoseRateRange[] ranges, double step)
        {
            return (int)Math.Ceiling(ranges[ranges.Length - 1].HighKev / step) + 2;
        }

        /// <summary>
        /// Разложить строку отклика по диапазонам: `share[i]` — доля в
        /// диапазоне i, `moment[i]` (если не null) — та же доля, умноженная на
        /// энергию, кэВ (первый момент — для представительной энергии, `AMBER77`).
        ///
        /// ⛔ (`AMBER82`, П145 24.09.2026) БИН — ОТРЕЗОК, А НЕ ТОЧКА. Бин `b`
        /// склада — это `[(b − ½)·шаг, (b + ½)·шаг)` с центром `b·шаг`
        /// (`AMBER71`), и диапазону достаётся та его часть, что лежит внутри:
        /// доля по ПЕРЕКРЫТИЮ, как у перегруппировки гистограммы. Прежнее
        /// правило «весь бин тому, в чей диапазон попал его центр» было верно
        /// лишь пока пик линии занимал ОДИН бин; после `AMBER70` (П135) пик
        /// делится между `floor(E/шаг)` и `floor(E/шаг) + 1`, и у первого
        /// диапазона сетки, чья полуширина (1.6…2.0 кэВ) меньше шага склада
        /// (2 кэВ), нижний бин ложился ЦЕНТРОМ ниже `LowKev` и терялся
        /// целиком: своя доля падала скачком до 0.78 при низе шкалы 10.0…10.7
        /// кэВ, и поток диапазона завышался в 1.28 раза. По перекрытию доля
        /// от положения границы зависит НЕПРЕРЫВНО, и сумма по сетке, которую
        /// строка целиком накрывает, та же.
        ///
        /// С раскладкой каналов (<paramref name="layout"/>) перекрытие берётся
        /// не с границами диапазона, а с границами КАНАЛОВ, и дальше каналы
        /// идут в диапазон по центру — как у спектра (`AMBER101`); момент — по
        /// центрам каналов, как у первого момента отсчётов. Без раскладки —
        /// перекрытие с границами диапазона.
        /// </summary>
        static void Spread(double[] row, double step, DoseRateRange[] ranges, double[] share, double[] moment,
                           ChannelLayout layout)
        {
            if (layout != null)
            {
                SpreadByChannels(row, step, share, moment, layout);
                return;
            }

            int bins = ranges.Length;
            int first = 0;
            for (int b = 0; b < row.Length; b++)
            {
                double value = row[b];
                if (value == 0.0)
                {
                    continue;
                }

                double lo = (b - 0.5) * step;
                double hi = (b + 0.5) * step;
                while (first < bins && ranges[first].HighKev <= lo)
                {
                    first++;
                }

                if (first >= bins)
                {
                    break;
                }

                for (int i = first; i < bins && ranges[i].LowKev < hi; i++)
                {
                    double a = Math.Max(lo, ranges[i].LowKev);
                    double c = Math.Min(hi, ranges[i].HighKev);
                    if (!(c > a))
                    {
                        continue;
                    }

                    double part = c - a >= step ? value : value * ((c - a) / step);
                    share[i] += part;
                    if (moment != null)
                    {
                        moment[i] += part * 0.5 * (a + c);
                    }
                }
            }
        }

        static void SpreadByChannels(double[] row, double step, double[] share, double[] moment, ChannelLayout layout)
        {
            double[] edges = layout.Edges;
            int n = edges.Length - 1;
            var mass = new double[n];
            int first = 0;
            for (int b = 0; b < row.Length; b++)
            {
                double value = row[b];
                if (value == 0.0)
                {
                    continue;
                }

                double lo = (b - 0.5) * step;
                double hi = (b + 0.5) * step;
                while (first < n && edges[first + 1] <= lo)
                {
                    first++;
                }

                if (first >= n)
                {
                    break;
                }

                for (int j = first; j < n && edges[j] < hi; j++)
                {
                    double a = Math.Max(lo, edges[j]);
                    double c = Math.Min(hi, edges[j + 1]);
                    if (c > a)
                    {
                        mass[j] += c - a >= step ? value : value * ((c - a) / step);
                    }
                }
            }

            for (int k = 0; k < share.Length; k++)
            {
                double s = 0.0, m = 0.0;
                for (int j = layout.Start[k]; j < layout.End[k]; j++)
                {
                    if (layout.Skip[j])
                    {
                        continue;
                    }

                    s += mass[j];
                    m += mass[j] * layout.Centres[j];
                }

                share[k] = s;
                if (moment != null)
                {
                    moment[k] = m;
                }
            }
        }

        /// <summary>
        /// Пол эффективности: диапазон, у которого доля отклика в своём же
        /// диапазоне меньше этой части от наибольшей по сетке, не
        /// приписывается никому. Деление на почти ноль превращает горстку
        /// отсчётов в тысячи квантов на см²: у ASN16 в 10…13 кэВ (ε = 2.5e-5,
        /// 4 отсч/с) выходило 1200 квант/(см²·с) и 2.4 мкЗв/ч из 3.3. Отсчёты
        /// такого диапазона уходят из покрытия, и приписка это показывает.
        /// Одна сотая — от максимума ~0.2…0.26 это ε ≈ 2e-3, то есть ниже
        /// ~15…17 кэВ у сцинтиллятора в обвязке; на числах корпуса порог
        /// сдвигом вдвое в любую сторону меняет показание меньше чем на 1 %.
        /// Пол ОТНОСИТЕЛЬНЫЙ нарочно: у сцены поля `ISO` те же величины идут
        /// в см² (максимум ~46 у Ø63×63), и правило действует без пересчёта.
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
        /// посчитанное, — можно: там отсчёты — кванты своей энергии.
        /// </summary>
        public const double MinOwnEfficiencyFraction = 0.01;

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
            // Считается по флажкам каналов, а не сложением длин.
            bool[] covered = new bool[energySpectrum.Spectrum.Length];

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
                    covered[i] = true;
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
                double[][] fractions = null;
                ChannelLayout layout = null;
                if (input.Matrix != null)
                {
                    layout = ChannelLayout.Of(calibration, energySpectrum.NumberOfChannels,
                                              energySpectrum.Spectrum.Length, overflow, ranges);
                    fractions = ResponseFractions(input, ranges, layout);
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
                    r.OwnEfficiency = fractions == null ? r.Efficiency : fractions[k][k];
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

                // Сверху вниз: континуум линий, приписанных ВЫШЕ, вычитается
                // из диапазонов НИЖЕ (только с матрицей — у пиковой кривой
                // континуума нет, и это ровно то, за что она «≈»).
                //
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
                // центр тяжести отсчётов диапазона за вычетом континуума линий
                // выше (его первый момент известен из тех же строк), затем
                // энергия линии подбирается так, чтобы центр тяжести её
                // собственной строки в диапазоне с ним совпал, — тогда
                // собственный хвост линии под пиком (комптон, вылеты внутри
                // диапазона) не тянет энергию вниз. Эта энергия — у ε, у Ḣ/φ̇
                // и у строки, которой вычитается континуум ниже. У пиковой
                // кривой строки нет — энергия диапазона есть центр тяжести его
                // отсчётов как есть. Пустой диапазон остаётся на середине: его
                // вклад ноль при любой энергии; остаётся на середине и
                // диапазон, где приписанное меньше пятой части отсчётов
                // (<see cref="RepresentativeMinShare"/>): там центр тяжести —
                // шум вычитания.
                //
                // ⚠ Почему ПЕРВЫЙ момент, а не пик или квантиль: строка матрицы
                // — без разрешения прибора, спектр — с ним, и из всех мер
                // положения только среднее не меняется от симметричного
                // размытия. Цена: у линии, чей собственный комптон заходит в
                // её диапазон (2614.5 в верхнем диапазоне у малых кристаллов),
                // центр тяжести строки почти не растёт с её энергией, и
                // энергия по нему определена плохо (замер П145: ASN16 +8.7 %
                // без разрешения, +4.4 % с ним — не хуже прежних +2.9/+4.6).
                var emitted = new double[bins];    // N_k, квантов/с
                var explainedCounts = new double[bins];
                var explainedMoment = new double[bins];
                ResponseMatrix matrix = input.Matrix;
                int cells = matrix != null ? Cells(ranges, matrix.BinKev) : 0;
                for (int k = bins - 1; k >= 0; k--)
                {
                    DoseRateRange r = ranges[k];
                    double explained = explainedCounts[k];

                    r.Explained = explained;
                    r.Attributed = Math.Max(0.0, r.Counts - explained);
                    r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(r.CenterKev);

                    // (`S186`) Пол — только НИЖЕ максимума кривой; выше него
                    // диапазон снимается лишь пустой строкой (доля не > 0).
                    bool floorApplies = k < maxOwnRange;
                    if (floorApplies ? ownAtCentre[k] < MinOwnEfficiencyFraction * maxOwn : !(ownAtCentre[k] > 0.0))
                    {
                        // Диапазон не приписывается никому: его отсчёты
                        // выходят из покрытия, о чём скажет приписка.
                        r.Skipped = true;
                        r.Cps = r.Attributed / seconds;
                        continue;
                    }

                    double[] share = null, moment = null;
                    if (r.Attributed > 0.0 && r.Attributed >= RepresentativeMinShare * r.Counts)
                    {
                        double mass = r.Counts - explained;
                        double target = (countsMoment[k] - explainedMoment[k]) / mass;
                        if (double.IsNaN(target) || double.IsInfinity(target))
                        {
                            target = r.CenterKev;
                        }

                        double energy;
                        double own;
                        if (matrix != null)
                        {
                            energy = Representative(input, ranges, k, target, cells, layout, out share, out moment);
                            own = share[k];
                        }
                        else
                        {
                            energy = Clamp(target, r.LowKev, r.HighKev);
                            own = input.EfficiencyAt(energy);
                        }

                        // Доля на представительной энергии ниже пола — это не
                        // линия, а остаток у края, где эффективность круто
                        // падает (замер П145: остаток 3 % отсчётов в 13…17 кэВ
                        // уехал к низу 13.12 с долей в 18 раз меньше, чем на
                        // середине, и дал +2.6 % показания): остаёмся на середине.
                        // (`S186`) Выше максимума кривой пола нет и здесь — там
                        // нет и «края, где эффективность круто падает» к нулю.
                        if ((floorApplies ? own >= MinOwnEfficiencyFraction * maxOwn : own > 0.0)
                            && !double.IsInfinity(own))
                        {
                            r.RepresentativeKev = energy;
                            r.OwnEfficiency = own;
                            r.Efficiency = input.EfficiencyAt(energy);
                            r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(energy);
                        }
                        else
                        {
                            share = null;
                            moment = null;
                        }
                    }

                    r.Cps = r.Attributed / seconds;
                    emitted[k] = r.Cps / r.OwnEfficiency;

                    // Континуум этой линии — в диапазоны НИЖЕ, по строке на её
                    // энергии (на середине — если строки на своей энергии нет).
                    if (matrix != null && emitted[k] > 0.0)
                    {
                        if (share == null)
                        {
                            share = fractions[k];
                        }

                        double quanta = emitted[k] * seconds;
                        for (int i = 0; i < k; i++)
                        {
                            explainedCounts[i] += quanta * share[i];
                            if (moment != null)
                            {
                                explainedMoment[i] += quanta * moment[i];
                            }
                            else
                            {
                                // Строка на середине без момента: момент её
                                // доли в диапазоне i — по его середине.
                                explainedMoment[i] += quanta * share[i] * ranges[i].CenterKev;
                            }
                        }
                    }

                    r.FluenceRate = emitted[k] * input.FluencePerPhoton;
                    r.DoseRate = r.FluenceRate * r.DoseRatePerFluenceRate;

                    if (r.Attributed > 0.0)
                    {
                        rate += r.DoseRate;
                        // Пуассон по СЫРЫМ отсчётам диапазона: вычитание
                        // континуума шум не убирает, а долю его увеличивает.
                        double relative = Math.Sqrt(Math.Max(r.Counts, 1.0)) / r.Attributed;
                        errorSquares += r.DoseRate * relative * (r.DoseRate * relative);
                    }
                }

                // Пропущенные диапазоны — вон из покрытия.
                for (int k = 0; k < bins; k++)
                {
                    if (!ranges[k].Skipped)
                    {
                        continue;
                    }

                    int startch, endch;
                    ChannelSpan(calibration, energySpectrum.NumberOfChannels, covered.Length,
                                ranges[k].LowKev, ranges[k].HighKev, out startch, out endch);
                    for (int i = startch; i < endch; i++)
                    {
                        covered[i] = false;
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
                if (covered[i])
                {
                    inside += energySpectrum.Spectrum[i];
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

        readonly object cacheSync = new object();

        EfficiencyConfigData cachedEfficiency;

        DoseRateInput cachedInput;

        string cachedStamp;

        string lastMatrixNote;
    }
}
