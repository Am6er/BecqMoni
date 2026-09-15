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
    /// G — <see cref="DoseRateInput.FluencePerPhoton"/>, E_i — середина
    /// диапазона. Диапазон представлен одной энергией: это приближение того
    /// же рода, что и прежнее (все отсчёты диапазона приписаны квантам его
    /// середины), и цена его та же — снимается не здесь, а разложением спектра.
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
        /// Доли отклика: `fractions[j][i]` — какая часть квантов энергии
        /// E_j (середина диапазона j) даёт отсчёт В ДИАПАЗОНЕ i. Строка матрицы
        /// растягивается на энергию линии тем же кодом, что у разбора
        /// (<see cref="ResponseMatrix.Evaluate"/>), и раскладывается по
        /// границам сетки; бин отклика — [b·шаг, (b+1)·шаг) поглощённой
        /// энергии, судится по середине.
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
        static double[][] ResponseFractions(DoseRateInput input, DoseRateRange[] ranges)
        {
            ResponseMatrix matrix = input.Matrix;
            int bins = ranges.Length;
            double step = matrix.BinKev;
            if (!(step > 0.0))
            {
                throw new DoseRateRefusalException(DoseRateCoefficients.Text(
                    "DoseRateEmptyMatrix", "Dose rate: the response matrix of the curve has no rows."));
            }

            int cells = (int)Math.Ceiling(ranges[bins - 1].HighKev / step) + 2;
            var fractions = new double[bins][];
            for (int j = 0; j < bins; j++)
            {
                double[] row = matrix.Evaluate(ranges[j].CenterKev, cells);
                var share = new double[bins];
                int i = 0;
                for (int b = 0; b < row.Length; b++)
                {
                    double e = (b + 0.5) * step;
                    while (i < bins && e >= ranges[i].HighKev)
                    {
                        i++;
                    }

                    if (i >= bins)
                    {
                        break;
                    }

                    if (e >= ranges[i].LowKev)
                    {
                        share[i] += row[b];
                    }
                }

                fractions[j] = share;
            }

            return fractions;
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
            for (int k = 0; k < bins; k++)
            {
                double fromE = grid[k];
                double toE = grid[k + 1];

                // ⛔ ОТБРАСЫВАНИЕ дробной части, одно правило на обоих концах
                // (`C4`, `W19`): генератор и потребитель теперь одно место,
                // и разойтись им негде, но правило остаётся названным.
                int startch = (int)calibration.EnergyToChannel(fromE, energySpectrum.NumberOfChannels);
                int endch = (int)calibration.EnergyToChannel(toE, energySpectrum.NumberOfChannels);
                if (startch < 0) startch = 0;
                // ⛔ Зажим В ДЛИНУ, а не в «длина − 1» (`A203`): цикл ниже
                // полуоткрыт, и `Length − 1` делал последний канал недостижимым
                // при ЛЮБОЙ верхней границе.
                if (endch > energySpectrum.Spectrum.Length) endch = energySpectrum.Spectrum.Length;

                double counts = 0.0;
                // Полуоткрыто, [startch, endch): диапазоны идут ВСТЫК, и
                // замкнутая сумма считала бы граничный канал дважды (W19,
                // решение Amber 08.08.2026 — полуоткрыто везде).
                for (int i = startch; i < endch; i++)
                {
                    // Канал переполнения пропускается ИМЕНЕМ, а не границей
                    // цикла: он бывает и нулевым, и последним (`A203`).
                    if (overflow[i]) continue;
                    counts += energySpectrum.Spectrum[i];
                    covered[i] = true;
                }

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
                if (input.Matrix != null)
                {
                    fractions = ResponseFractions(input, ranges);
                }

                // Пол вырожденной эффективности — от наибольшей по сетке:
                // делить на ноль нельзя и на почти ноль тоже, см.
                // <see cref="MinOwnEfficiencyFraction"/>.
                double maxOwn = 0.0;
                for (int k = 0; k < bins; k++)
                {
                    DoseRateRange r = ranges[k];
                    r.Efficiency = input.EfficiencyAt(r.CenterKev);
                    r.OwnEfficiency = fractions == null ? r.Efficiency : fractions[k][k];
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

                    maxOwn = Math.Max(maxOwn, r.OwnEfficiency);
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
                var emitted = new double[bins];    // N_k, квантов/с
                for (int k = bins - 1; k >= 0; k--)
                {
                    DoseRateRange r = ranges[k];
                    double explained = 0.0;
                    if (fractions != null)
                    {
                        for (int j = k + 1; j < bins; j++)
                        {
                            explained += emitted[j] * seconds * fractions[j][k];
                        }
                    }

                    r.Explained = explained;
                    r.Attributed = Math.Max(0.0, r.Counts - explained);
                    r.DoseRatePerFluenceRate = DoseRateCoefficients.DoseRatePerFluenceRate(r.CenterKev);

                    if (r.OwnEfficiency < MinOwnEfficiencyFraction * maxOwn)
                    {
                        // Диапазон не приписывается никому: его отсчёты
                        // выходят из покрытия, о чём скажет приписка.
                        r.Skipped = true;
                        r.Cps = r.Attributed / seconds;
                        continue;
                    }

                    r.Cps = r.Attributed / seconds;
                    emitted[k] = r.Cps / r.OwnEfficiency;
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

                    int startch = (int)calibration.EnergyToChannel(ranges[k].LowKev, energySpectrum.NumberOfChannels);
                    int endch = (int)calibration.EnergyToChannel(ranges[k].HighKev, energySpectrum.NumberOfChannels);
                    if (startch < 0) startch = 0;
                    if (endch > covered.Length) endch = covered.Length;
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
