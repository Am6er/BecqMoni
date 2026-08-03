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

        /// <summary>Отсчёты компонента в диапазоне фита.</summary>
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

        /// <summary>Оптимум дрейфа упёрся в границу сетки — шкале верить нельзя.</summary>
        public bool DriftOnGridEdge { get; set; }

        public double LiveTime { get; set; }

        /// <summary>Кривая эффективности была учтена.</summary>
        public bool EfficiencyUsed { get; set; }

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
                    Curve = (double[])component.Curve.Clone()
                });
            }

            if (rest.Count > 0)
            {
                double[] other = new double[channels];
                foreach (FsaComponentResult component in rest)
                {
                    for (int i = 0; i < channels && i < component.Curve.Length; i++)
                    {
                        other[i] += component.Curve[i];
                    }
                }

                layers.Add(new FsaStackLayer
                {
                    Name = OtherLayerName,
                    Kind = FsaComponentKind.Single,
                    Curve = other
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

            return layers;
        }

        public const string OtherLayerName = "other";

        /// <summary>Имя слоя подложки в стеке отрисовки.</summary>
        public const string ContinuumLayerName = "continuum";

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
