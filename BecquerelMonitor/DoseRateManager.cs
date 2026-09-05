using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

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
    public class DoseRateManager
    {
        private GlobalConfigManager globalConfigManager;
        public DoseRateManager(GlobalConfigManager globalConfigManager) 
        {
            this.globalConfigManager = globalConfigManager;
        }

        // Token: 0x060002B0 RID: 688 RVA: 0x0000D214 File Offset: 0x0000B414
        public DoseRate Calculate(ResultData resultData, DoseRateConfig config)
        {
            // Доза — свойство ИЗМЕРЕННОГО спектра. Раньше сюда передавался
            // режим отображения графика, и при «фон вычтен» доза считалась по
            // разности — показание дозиметра менялось от галки отрисовки
            // (TODO G6). Дозиметр так себя не ведёт: фон — тоже доза.
            EnergySpectrum energySpectrum = resultData.EnergySpectrum;
            DoseRate doseRate = new DoseRate();

            // ⛔ `C4(в)`. Негодный вход отказывается ВИДИМО. Прежде расчёт на
            // спектре без калибровки падал `NullReferenceException` где-то
            // внутри, а на спектре с нулевым временем возвращал ровный ноль —
            // и ноль уходил в строку состояния неотличимо от измеренного.
            if (energySpectrum == null || energySpectrum.Spectrum == null
                || energySpectrum.NumberOfChannels < 1)
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

            if (!(energySpectrum.MeasurementTime > 0.0))
            {
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateNoTime", "Dose rate: the reference spectrum has zero measurement time.");
                return doseRate;
            }

            // Сколько отсчётов спектра вообще попало в откалиброванные
            // диапазоны. Считается по флажкам каналов, а не сложением длин:
            // диапазоны в конфигурации могут перекрываться, и сумма их
            // содержимого была бы больше спектра.
            bool[] covered = new bool[energySpectrum.Spectrum.Length];

            // ⛔ `A203`. Каналы, которым нечего давать в дозу, названы вслух —
            // см. <see cref="OverflowChannel"/>. Прежде их роль исполнял зажим
            // `endch = Length − 1` при полуоткрытом цикле: последний канал не
            // считался НИКОГДА, и на ASN16 это работало защитой (в последнем
            // канале лежит переполнение), а на приборе с обычным последним
            // каналом молча теряло его.
            bool[] overflow = OverflowChannel.Mask(energySpectrum.Spectrum);

            List<double> errors = new List<double>();
            List<double> doseRates = new List<double>();
            foreach (DoseRateCalibrationPoint point in config.DoseRateCalibrationPoints)
            {
                int startch = (int)calibration.EnergyToChannel(point.LowerBound, energySpectrum.NumberOfChannels);
                int endch = (int)calibration.EnergyToChannel(point.UpperBound, energySpectrum.NumberOfChannels);
                if (startch < 0) startch = 0;
                // ⛔ Зажим В ДЛИНУ, а не в «длина − 1» (`A203`). Цикл ниже
                // полуоткрыт, поэтому `Length − 1` делал последний канал
                // недостижимым при ЛЮБОЙ верхней границе — правило «последний
                // канал не считать» жило здесь умолчанием, безымянно и
                // независимо от того, переполнение в нём или измерение.
                // ⚠ Генератору точек (`DoseRateEstimator.Estimate`) эта правка
                // видна НЕ БЫВАЕТ: его сетка строится по шкале прибора, верх
                // сетки равен `ChannelToEnergy(N − 1)`, и его собственный
                // `toChannel` до `N` не доходит. Обратный ход «построить точки
                // по эталону — померить тот же эталон» остаётся 1.000000, что и
                // проверяется пробой. Правка меняет счёт только у конфигураций,
                // чьи диапазоны выходят ЗА шкалу, — у поставочного `RC-103.xml`
                // (36 точек до 4997 кэВ) и у сеток, построенных до 05.09.2026.
                if (endch > energySpectrum.Spectrum.Length) endch = energySpectrum.Spectrum.Length;
                double counts = 0.0;
                // Полуоткрыто, [startch, endch): диапазоны в конфигурациях идут
                // ВСТЫК (у поставочной RC-103 их 36, верх одного равен низу
                // следующего), и замкнутая сумма считала граничный канал каждого
                // диапазона дважды — 35 лишних каналов из ~900 на 1024-канальном
                // спектре, доза завышалась. Генератор точек в DeviceConfigForm
                // всегда суммировал полуоткрыто; расходились именно эти два
                // места (W19, решение Amber 08.08.2026 — полуоткрыто везде).
                for (int i = startch; i < endch; i++)
                {
                    // Канал переполнения пропускается ИМЕНЕМ, а не границей
                    // цикла: он бывает и нулевым, и последним (`A203`).
                    if (overflow[i]) continue;
                    counts += energySpectrum.Spectrum[i];
                    covered[i] = true;
                }
                if (counts == 0) continue;
                double error = Math.Sqrt(counts) / counts;
                double dr = counts * point.Sensitivity;
                doseRates.Add(dr);
                errors.Add(dr * error);
            }

            // Доля отсчётов, попавшая в откалиброванные диапазоны (`C4(в)`).
            //
            // ⛔ Канал переполнения не считается НИ В ЗНАМЕНАТЕЛЕ, ни в числителе
            // (`A203`). Он не измерение, и держать его в знаменателе значит
            // тянуть покрытие вниз тем, что покрыть НЕЛЬЗЯ: у ASN16 в последний
            // канал уходит 0.142 отсчёта в секунду, и приписка о неполном
            // покрытии (`A199`) появлялась бы от прибора, а не от сетки.
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

            doseRate.Rate = doseRates.Sum();
            if (double.IsNaN(doseRate.Rate) || double.IsInfinity(doseRate.Rate) || energySpectrum.MeasurementTime == 0.0)
            {
                doseRate.Rate = 0.0;
                doseRate.Refusal = DoseRateCoefficients.Text(
                    "DoseRateNotFinite",
                    "Dose rate: the sum over the ranges is not a finite number — the calibration points are unusable.");
                return doseRate;
            }

            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            double errorLevel = (double)globalConfig.MeasurementConfig.ErrorLevel;
            doseRate.Error = errorLevel * Math.Sqrt(errors.Sum(e => e * e)) / energySpectrum.MeasurementTime;
            doseRate.Rate /= energySpectrum.MeasurementTime;
            return doseRate;
        }
    }
}
