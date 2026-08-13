using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>Компонент в готовом разложении.</summary>
    public sealed class FsaComponentResult
    {
        public string Name { get; set; }

        public FsaComponentKind Kind { get; set; }

        /// <summary>Вклад компонента по каналам, отсчёты (амплитуда x образ).</summary>
        public double[] Curve { get; set; }

        /// <summary>
        /// Та ЧАСТЬ <see cref="Curve"/>, что пришла от сумм-пиков каскада;
        /// null — их нет. Не отдельный компонент: в фите это одна колонка с
        /// нуклидом, и свободной амплитуды у сумм-пиков нет нарочно. Здесь
        /// лежит только ради отрисовки подслоем.
        /// </summary>
        public double[] SumPeakCurve { get; set; }

        /// <summary>
        /// Отсчёты компонента в его ПИКОВЫХ ОКНАХ (±2 ПШПВ у каждой линии и
        /// каждого сумм-пика), а не по всему образу — см.
        /// <c>FsaAnalyzer.PeakWindowCounts</c> и решение S24в.
        /// </summary>
        public double PeakCounts { get; set; }

        /// <summary>Скорость счёта компонента, имп/с.</summary>
        public double CountRate { get; set; }

        /// <summary>Значимость амплитуды (амплитуда / её погрешность).</summary>
        public double Z { get; set; }

        /// <summary>Доля в «пироге» — по объяснённым пиковым отсчётам, %.</summary>
        public double SharePercent { get; set; }
    }

    /// <summary>Слой стека для отрисовки: кривая и подпись с долей.</summary>
    public sealed class FsaStackLayer
    {
        public string Name { get; set; }

        public FsaComponentKind Kind { get; set; }

        /// <summary>Вклад слоя по каналам с разнесённой на него подложкой.</summary>
        public double[] Curve { get; set; }

        /// <summary>
        /// Часть <see cref="Curve"/> от сумм-пиков каскада; null — их нет.
        /// Рисуется ВНУТРИ ленты слоя штриховкой того же цвета: сумм-пик
        /// принадлежит своему нуклиду, отдельной строки в легенде и отдельной
        /// доли в «пироге» у него быть не должно. Подложка сюда не
        /// разносится — это чистый пиковый вклад.
        /// </summary>
        public double[] SumPeakCurve { get; set; }

        /// <summary>Доля слоя в полном счёте модели, %.</summary>
        public double SharePercent { get; set; }
    }

    /// <summary>Результат полноспектральной декомпозиции одного спектра.</summary>
    public sealed class FsaResult
    {
        public List<FsaComponentResult> Components { get; set; }

        /// <summary>Континуум модели (шапки сплайна), отсчёты по каналам.</summary>
        public double[] Continuum { get; set; }

        /// <summary>Вычтенный измеренный фон, отсчёты по каналам.</summary>
        public double[] Background { get; set; }

        /// <summary>Сумма модели, отсчёты по каналам.</summary>
        public double[] Model { get; set; }

        public int FirstChannel { get; set; }

        public int LastChannel { get; set; }

        public double Chi2Ndf { get; set; }

        public double Gain { get; set; }

        public double OffsetChannels { get; set; }

        /// <summary>
        /// Оптимум дрейфа упёрся в границу сетки — шкале верить нельзя. Это ИЛИ
        /// двух признаков ниже; порознь они появились 13.08.2026, когда
        /// корпусный прогон (S1, S6) показал, что одним словом «дрейф» названы
        /// два разных отказа, и читатель предупреждения расширял не ту сетку.
        /// </summary>
        public bool DriftOnGridEdge
        {
            get { return this.GainOnGridEdge || this.OffsetOnGridEdge; }
        }

        /// <summary>Упёрлось УСИЛЕНИЕ: оптимум на краю сетки <c>GainRange</c>.</summary>
        public bool GainOnGridEdge { get; set; }

        /// <summary>Упёрся НОЛЬ шкалы: оптимум на краю сетки <c>OffsetRangeKev</c>.</summary>
        public bool OffsetOnGridEdge { get; set; }

        public double LiveTime { get; set; }

        /// <summary>Кривая эффективности была учтена.</summary>
        public bool EfficiencyUsed { get; set; }

        /// <summary>
        /// Образы построены по матрице отклика (S2). Без пометки «с матрицей»
        /// и «без матрицы» неотличимы на глаз, а браковка матрицы по
        /// отпечатку или формату файла молчалива — разложение просто тихо
        /// становится хуже.
        /// </summary>
        public bool ResponseMatrixUsed { get; set; }

        /// <summary>
        /// Каскадное суммирование СРАБОТАЛО: хоть одной линии состава поправлен
        /// пик или добавлен сумм-пик. Пометка ставится по факту, а не по
        /// включённому ключу: у состава из Cs-137 и K-40 каскадов нет вовсе, и
        /// «с суммированием» там значило бы, что поправка что-то сделала.
        /// </summary>
        public bool CascadeSummingUsed { get; set; }

        public FsaResult()
        {
            this.Components = new List<FsaComponentResult>();
        }

        /// <summary>
        /// Слои для послойной отрисовки. Континуум разносится по компонентам
        /// так, как это неявно делают полные измеренные образы: подложка на
        /// энергии E приписывается компонентам пропорционально их пиковому
        /// счёту ВЫШЕ E — комптоновское рассеяние сбрасывает энергию только
        /// вниз. Это правило отображения, а не фита.
        ///
        /// Исключение — хвост: выше самой верхней линии пикового счёта нет ни у
        /// кого, и разносить подложку не по чему. Там она остаётся отдельным
        /// серым слоем, иначе на пустом месте рисовался бы состав, которого
        /// модель не знает.
        /// </summary>
        public List<FsaStackLayer> BuildStackedLayers(int maxNamedLayers)
        {
            List<FsaStackLayer> layers = new List<FsaStackLayer>();
            if (this.Components == null || this.Components.Count == 0)
            {
                return layers;
            }

            int channels = this.Model != null ? this.Model.Length : 0;
            if (channels == 0)
            {
                return layers;
            }

            List<FsaComponentResult> ordered = new List<FsaComponentResult>(this.Components);
            ordered.RemoveAll(c => c.Curve == null || Max(c.Curve) <= 0.0);
            ordered.Sort((a, b) => Max(b.Curve).CompareTo(Max(a.Curve)));

            // Мешающие образы (рентген, пики вылета) показываются всегда и в
            // лимит НЕ входят: лимит отмеряет, сколько нуклидов названо
            // поимённо. Общий счётчик их смешивал — при обычном составе четыре
            // мешающих (рентген W и Pb, SE- и DE-2614, последние два в
            // библиотеке всегда) съедали четыре слота из шести, и нуклиды
            // сверх двух схлопывались в «other».
            List<FsaComponentResult> named = new List<FsaComponentResult>();
            List<FsaComponentResult> rest = new List<FsaComponentResult>();
            int namedNuclides = 0;
            foreach (FsaComponentResult component in ordered)
            {
                if (component.Kind == FsaComponentKind.Nuisance)
                {
                    named.Add(component);
                }
                else if (namedNuclides < maxNamedLayers)
                {
                    named.Add(component);
                    namedNuclides++;
                }
                else
                {
                    rest.Add(component);
                }
            }

            foreach (FsaComponentResult component in named)
            {
                layers.Add(new FsaStackLayer
                {
                    Name = component.Name,
                    Kind = component.Kind,
                    Curve = PositivePart(component.Curve),
                    SumPeakCurve = component.SumPeakCurve != null
                        ? (double[])component.SumPeakCurve.Clone()
                        : null
                });
            }

            if (rest.Count > 0)
            {
                double[] other = new double[channels];
                double[] otherSums = null;
                foreach (FsaComponentResult component in rest)
                {
                    for (int i = 0; i < channels && i < component.Curve.Length; i++)
                    {
                        other[i] += component.Curve[i];
                    }

                    if (component.SumPeakCurve == null)
                    {
                        continue;
                    }

                    if (otherSums == null)
                    {
                        otherSums = new double[channels];
                    }

                    for (int i = 0; i < channels && i < component.SumPeakCurve.Length; i++)
                    {
                        otherSums[i] += component.SumPeakCurve[i];
                    }
                }

                layers.Add(new FsaStackLayer
                {
                    Name = OtherLayerName,
                    Kind = FsaComponentKind.Single,
                    Curve = other,
                    SumPeakCurve = otherSums
                });
            }

            double[] leftover = DistributeContinuum(layers, channels);
            if (leftover != null)
            {
                // Неразнесённый остаток — только хвост выше последней линии.
                layers.Insert(0, new FsaStackLayer
                {
                    Name = ContinuumLayerName,
                    Kind = FsaComponentKind.Nuisance,
                    Curve = leftover
                });
            }

            double total = 0.0;
            foreach (FsaStackLayer layer in layers)
            {
                total += Sum(layer.Curve);
            }

            foreach (FsaStackLayer layer in layers)
            {
                layer.SharePercent = total > 0.0 ? 100.0 * Sum(layer.Curve) / total : 0.0;
            }

            // Порядок: сначала нуклиды, потом всё остальное — приборные образы
            // (рассеяние, вылеты, рентген), «прочее» и подложка. Список задаёт и
            // стопку, и легенду разом, поэтому раскладывать их порознь нельзя:
            // легенда, идущая не в том порядке, в каком нарисованы ленты, —
            // отдельный способ ошибиться. Внутри каждой группы порядок прежний,
            // по убыванию высоты.
            // Порядок внутри группы держится на исходном номере: List.Sort
            // неустойчива, и без него слои равного ранга перемешивались бы от
            // запуска к запуску.
            int[] order = new int[layers.Count];
            for (int k = 0; k < order.Length; k++)
            {
                order[k] = k;
            }

            List<FsaStackLayer> source = new List<FsaStackLayer>(layers);
            Array.Sort(order, (x, y) =>
            {
                int rx = LayerRank(source[x]), ry = LayerRank(source[y]);
                return rx != ry ? rx.CompareTo(ry) : x.CompareTo(y);
            });

            layers.Clear();
            for (int k = 0; k < order.Length; k++)
            {
                layers.Add(source[order[k]]);
            }

            return layers;
        }

        /// <summary>0 — нуклид, 1 — приборный образ и «прочее», 2 — подложка.</summary>
        static int LayerRank(FsaStackLayer layer)
        {
            if (string.Equals(layer.Name, ContinuumLayerName, StringComparison.Ordinal))
            {
                return 2;
            }

            return layer.Kind == FsaComponentKind.Nuisance
                   || string.Equals(layer.Name, OtherLayerName, StringComparison.Ordinal)
                ? 1 : 0;
        }

        public const string OtherLayerName = "other";

        /// <summary>Имя слоя подложки в стеке отрисовки.</summary>
        public const string ContinuumLayerName = "continuum";

        /// <summary>
        /// Имя образа случайных наложений (pile-up). Служебное и НЕ переводится:
        /// по нему раздаётся цвет и его же ищет проба. Человеку показывается
        /// перевод — см. <see cref="FsaPalette.DisplayName"/>.
        /// </summary>
        public const string PileUpLayerName = "pile-up";

        /// <summary>
        /// Копия кривой без отрицательной части. У ленты стека не бывает
        /// отрицательной высоты, а знакопеременные образы есть: наложения
        /// ПЕРЕНОСЯТ счёт — снизу убыль, сверху приход. Рисуется приход,
        /// убыль (доли процента) остаётся в модели, но не в стеке; из-за этого
        /// верх стека расходится с суммой модели на ту же долю процента.
        /// </summary>
        static double[] PositivePart(double[] curve)
        {
            double[] copy = new double[curve.Length];
            for (int i = 0; i < curve.Length; i++)
            {
                copy[i] = curve[i] > 0.0 ? curve[i] : 0.0;
            }

            return copy;
        }

        /// <summary>
        /// Разнести континуум по компонентам. Возвращает остаток — ту часть
        /// подложки, разносить которую не по чему (выше самой верхней линии
        /// пикового счёта ни у одного компонента нет), или null, если остатка
        /// нет.
        /// </summary>
        double[] DistributeContinuum(List<FsaStackLayer> layers, int channels)
        {
            if (this.Continuum == null || layers.Count == 0)
            {
                return null;
            }

            int count = layers.Count;
            double[][] above = new double[count][];
            for (int k = 0; k < count; k++)
            {
                double[] curve = layers[k].Curve;
                double[] cumulative = new double[channels];
                double running = 0.0;
                for (int i = channels - 1; i >= 0; i--)
                {
                    running += i < curve.Length ? curve[i] : 0.0;
                    cumulative[i] = running;
                }

                above[k] = cumulative;
            }

            double[] leftover = null;
            for (int i = 0; i < channels; i++)
            {
                double continuum = i < this.Continuum.Length ? this.Continuum[i] : 0.0;
                if (continuum <= 0.0)
                {
                    continue;
                }

                double totalAbove = 0.0;
                for (int k = 0; k < count; k++)
                {
                    totalAbove += above[k][i];
                }

                if (totalAbove <= 0.0)
                {
                    // Хвост: линий выше нет, приписать подложку некому.
                    if (leftover == null)
                    {
                        leftover = new double[channels];
                    }

                    leftover[i] = continuum;
                    continue;
                }

                for (int k = 0; k < count; k++)
                {
                    // Длина проверяется, как и везде рядом: кривые слоёв
                    // приходят от анализатора и в принципе могут быть короче
                    // модели, по которой считается channels.
                    double[] curve = layers[k].Curve;
                    if (i < curve.Length)
                    {
                        curve[i] += above[k][i] / totalAbove * continuum;
                    }
                }
            }

            return leftover;
        }

        static double Max(double[] values)
        {
            double max = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        static double Sum(double[] values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return sum;
        }
    }
}
