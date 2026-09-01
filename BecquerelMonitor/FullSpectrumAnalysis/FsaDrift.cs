using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// ⛔ ДРЕЙФ ШКАЛЫ ДЕРЖИТСЯ НА НАЙДЕННЫХ ПИКАХ (`A36`, задание Amber
    /// 01.09.2026), а не на общем χ².
    ///
    /// Зачем. Прежде поправка шкалы была ОДНОЙ парой «усиление + сдвиг» на весь
    /// спектр, и выбирал её перебор сетки по суммарному χ². Внизу шкалы отсчётов
    /// на порядки больше, поэтому выигрыш покупался там, а на дальнем конце
    /// модель уезжала: на `Th232_29.07.2022` модельный 2614 вставал на 2598.1
    /// при измеренном 2612.5 — 14.5 кэВ мимо — при том, что ошибка
    /// энергетической калибровки у этого пика −0.02 кэВ. Хуже того, итог был не
    /// лучше, а хуже: χ²/ndf 6.994 с подгонкой против 6.794 без неё.
    ///
    /// Теперь ту же работу делают ПИКИ. Финдер уже нашёл центроид и подписал его
    /// линией — значит для каждой подписи известно, где линия ДОЛЖНА стоять
    /// (канал энергии подписи по калибровке) и где она НАЙДЕНА. Разность — это
    /// и есть поправка шкалы в этой точке.
    ///
    /// Форма поправки — КУСОЧНО-ЛИНЕЙНАЯ по опорным точкам (правило Amber
    /// дословно: «между опорными точками создаются узлы подгонки, учитывающие
    /// дрейф двух ближних опорных точек; на краях линейно экстраполируем
    /// ближайшие узлы до краёв»):
    ///
    ///   * между двумя соседними опорными — прямая через них;
    ///   * ниже первой и выше последней — продолжение крайней пары;
    ///   * одна опорная — постоянный сдвиг, наклон определять нечем;
    ///   * ни одной — поправки нет вовсе (усиление 1, сдвиг 0).
    ///
    /// ⚠ Поверх этой поправки остаётся УЗКАЯ сетка «усиление + сдвиг»: пики
    /// задают центр, χ² уточняет рядом (решение Amber). Диапазон сужается до
    /// одного шага прежней сетки — см. <see cref="FsaAnalyzer"/>.
    /// </summary>
    public sealed class FsaDrift
    {
        /// <summary>Канал линии по калибровке, отсортировано по возрастанию.</summary>
        readonly double[] at;

        /// <summary>Сдвиг в этой точке: «где нашли» минус «где ждали», каналы.</summary>
        readonly double[] shift;

        FsaDrift(double[] at, double[] shift)
        {
            this.at = at;
            this.shift = shift;
        }

        /// <summary>Сколько опорных точек держит поправку.</summary>
        public int AnchorCount
        {
            get { return this.at.Length; }
        }

        /// <summary>Опорные точки: канал по калибровке и сдвиг в нём.</summary>
        public void Describe(int index, out double channel, out double shiftChannels)
        {
            channel = this.at[index];
            shiftChannels = this.shift[index];
        }

        /// <summary>
        /// Поправка в канале <paramref name="position"/>, каналы. Вне отрезка
        /// опорных точек — линейное продолжение крайней пары.
        /// </summary>
        public double ShiftAt(double position)
        {
            if (this.at.Length == 0 || Double.IsNaN(position))
            {
                return 0.0;
            }

            if (this.at.Length == 1)
            {
                return this.shift[0];
            }

            if (position <= this.at[0])
            {
                return Interpolate(0, 1, position);
            }

            if (position >= this.at[this.at.Length - 1])
            {
                return Interpolate(this.at.Length - 2, this.at.Length - 1, position);
            }

            // Опорных точек единицы, поэтому линейный поиск дешевле двоичного и
            // читается прямо.
            for (int i = 1; i < this.at.Length; i++)
            {
                if (position <= this.at[i])
                {
                    return Interpolate(i - 1, i, position);
                }
            }

            return this.shift[this.shift.Length - 1];
        }

        double Interpolate(int a, int b, double position)
        {
            double span = this.at[b] - this.at[a];
            if (!(Math.Abs(span) > 1e-9))
            {
                return 0.5 * (this.shift[a] + this.shift[b]);
            }

            double t = (position - this.at[a]) / span;
            return this.shift[a] + t * (this.shift[b] - this.shift[a]);
        }

        /// <summary>
        /// Собрать поправку по найденным пикам. Берутся ПОДПИСАННЫЕ пики: у
        /// безымянного нет линии, с которой его сравнивать.
        ///
        /// ⚠ Обе стороны считаются в каналах ОДНОЙ И ТОЙ ЖЕ калибровкой, а не
        /// «канал финдера против энергии из базы»: у пика поле `Channel` целое,
        /// и на 8192 каналах это до полуканала произвола, тогда как `Energy`
        /// хранит центроид как он посчитан.
        ///
        /// ⚠ Две подписи в одной точке (дубль линии, две записи базы) дают одну
        /// опорную точку со средним сдвигом: иначе кусочная поправка получила бы
        /// вертикальный участок и зигзаг между ними.
        /// </summary>
        /// <returns>Поправка либо null, если опереться не на что.</returns>
        public static FsaDrift FromPeaks(IEnumerable<Peak> peaks, EnergyCalibration calibration,
                                         int channels)
        {
            if (peaks == null || calibration == null || channels <= 0)
            {
                return null;
            }

            var expected = new List<double>();
            var delta = new List<double>();
            foreach (Peak peak in peaks)
            {
                if (peak == null || peak.Nuclide == null
                    || !(peak.Nuclide.Energy > 0.0) || !(peak.Energy > 0.0))
                {
                    continue;
                }

                double want = calibration.EnergyToChannel(peak.Nuclide.Energy, maxChannels: channels);
                double found = calibration.EnergyToChannel(peak.Energy, maxChannels: channels);
                if (Double.IsNaN(want) || Double.IsNaN(found)
                    || want < 0.0 || want > channels || found < 0.0 || found > channels)
                {
                    continue;
                }

                expected.Add(want);
                delta.Add(found - want);
            }

            if (expected.Count == 0)
            {
                return null;
            }

            int[] order = new int[expected.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (x, y) => expected[x].CompareTo(expected[y]));

            var at = new List<double>();
            var shift = new List<double>();
            int k = 0;
            while (k < order.Length)
            {
                double position = expected[order[k]];
                double sum = 0.0;
                int count = 0;
                while (k < order.Length && Math.Abs(expected[order[k]] - position) < 0.5)
                {
                    sum += delta[order[k]];
                    count++;
                    k++;
                }

                at.Add(position);
                shift.Add(sum / count);
            }

            return new FsaDrift(at.ToArray(), shift.ToArray());
        }
    }
}
