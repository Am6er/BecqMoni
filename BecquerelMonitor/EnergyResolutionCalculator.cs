using BecquerelMonitor.Utils;
using System;
using Windows.UI.Xaml.Documents;

namespace BecquerelMonitor
{
    public class EnergyResolutionCalculator
    {
        public static EnergyResolutionResult CalculateFWHM(EnergySpectrum spectrum, int startChannel, int endChannel)
        {
            if (spectrum == null || spectrum.Spectrum == null) return null;
            int[] source = spectrum.Spectrum;
            double[] counts = new double[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                counts[i] = source[i];
            }

            return CalculateFWHM(counts, spectrum.NumberOfChannels, spectrum.EnergyCalibration,
                                 startChannel, endChannel);
        }

        /// <summary>
        /// То же по ГОТОВЫМ отсчётам (`A45`). Нужен тем видам спектра, у которых
        /// нарисованное не лежит в `EnergySpectrum`: в режиме FSA на экране спектр
        /// за вычетом фона (`FsaNetSpectrum`), и линии полуширины обязаны идти по
        /// нему, а не по сырым отсчётам — иначе они висят над кривой.
        ///
        /// ⚠ Отсчёты здесь ДРОБНЫЕ и могут быть отрицательными: фон вычтен с
        /// поправкой на время. Алгоритм от этого не меняется — он и раньше считал
        /// в double, целыми были только входные ячейки.
        /// </summary>
        public static EnergyResolutionResult CalculateFWHM(double[] counts, int numberOfChannels,
                                                           EnergyCalibration energyCalibration,
                                                           int startChannel, int endChannel)
        {
            if (counts == null || energyCalibration == null) return null;
            if (startChannel >= endChannel || numberOfChannels < endChannel) return null;
            int centroid = startChannel;
            if (counts.Length <= endChannel) return null;
            double centroid_counts = counts[centroid];
            for (int i = startChannel; i <= endChannel; i++)
            {
                if (counts[i] - SpectrumAriphmetics.getY(i, startChannel, endChannel, counts[startChannel], counts[endChannel])
                    > centroid_counts - SpectrumAriphmetics.getY(centroid, startChannel, endChannel, counts[startChannel], counts[endChannel]))
                {
                    centroid_counts = counts[i];
                    centroid = i;
                }
            }

            double start_counts = counts[startChannel];
            double end_counts = counts[endChannel];
            double maxBaseValue = start_counts + (end_counts - start_counts) * (double)(centroid - startChannel) / (double)(endChannel - startChannel);
            double halfValue = (centroid_counts - maxBaseValue) / 2.0 + maxBaseValue;

            // ⛔ (`AMBER181`, 05.10.2026) ПОЛУВЫСОТА — НАД НАКЛОННОЙ БАЗОЙ, а не на
            // постоянном уровне. Прежде отсчёты каждого канала сравнивались с ОДНИМ
            // числом `halfValue` — полувысотой над базой ПОД ВЕРШИНОЙ, — и на склоне
            // подложки крыло со стороны высокой базы пересекало этот уровень раньше,
            // а со стороны низкой — позже. Счёт ревизии (гаусс σ = 10 каналов,
            // истинная ПШПВ 23.55, подложка 5000): наклон −20 отсч./канал, амплитуда
            // 1000 → 33.70 (+43 %); −60 → 14.01 (−40 %); амплитуда 300 → 4.83 (−80 %).
            // Число шло в панель выделения и ОПОРОЙ в кривую ПШПВ.
            // Теперь сравнивается ЧИСТЫЙ отсчёт `counts[j] − base(j)` (база — та же
            // прямая между краями выделения, что и у поиска вершины выше) с половиной
            // высоты над базой; пересечение — линейной интерполяцией чистого отсчёта
            // между соседними каналами. При ровной базе это прежний расчёт.
            // `HalfValue` результата — по-прежнему уровень полувысоты У ВЕРШИНЫ; линия
            // полувысоты на графике идёт параллельно базе (`EnergySpectrumView`).
            double halfNet = (centroid_counts - maxBaseValue) / 2.0;
            if (!(halfNet > 0.0)) return null;
            double baseSlope = (end_counts - start_counts) / (double)(endChannel - startChannel);
            Func<int, double> net = ch => counts[ch] - (start_counts + baseSlope * (double)(ch - startChannel));
            double leftChannel = -1.0;
            // До вершины ВКЛЮЧИТЕЛЬНО: пересечение между centroid − 1 и centroid
            // (узкий пик, полуширина меньше канала) прежде давало отказ.
            for(int j = startChannel + 1; j <= centroid; j++)
            {
                double nj = net(j);
                if (nj > halfNet)
                {
                    double nPrev = net(j - 1);
                    if (!(nj > nPrev)) return null;
                    leftChannel = (double)(j - 1) + (halfNet - nPrev) / (nj - nPrev);
                    break;
                }
            }
            if (leftChannel < 0.0) return null;
            double rightChannel = -1.0;
            for(int k = endChannel - 1; k >= centroid; k--)
            {
                double nk = net(k);
                if (nk > halfNet)
                {
                    double nNext = net(k + 1);
                    if (!(nk > nNext)) return null;
                    rightChannel = (double)(k + 1) - (halfNet - nNext) / (nk - nNext);
                    break;
                }
            }
            if (rightChannel < 0.0) return null;

            double leftEnergy = energyCalibration.ChannelToEnergy(leftChannel);
            double rightEnergy = energyCalibration.ChannelToEnergy(rightChannel);
            double resolution = (rightEnergy - leftEnergy) / energyCalibration.ChannelToEnergy((double)centroid);
            double resolutioninkev = rightEnergy - leftEnergy;



            EnergyResolutionResult result = new EnergyResolutionResult();
            result.StartChannel = (double)startChannel;
            result.EndChannel = (double)endChannel;
            result.StartValue = start_counts;
            result.EndValue = end_counts;
            result.MaxBaseValue = maxBaseValue;
            result.LeftChannel = leftChannel;
            result.RightChannel = rightChannel;
            result.HalfValue = halfValue;
            result.MaxChannel = (double)centroid;
            result.MaxValue = counts[(int)result.MaxChannel];
            result.Resolution = resolution;
            result.ResolutionInkeV = resolutioninkev;
            return result;
        }
    }
}
