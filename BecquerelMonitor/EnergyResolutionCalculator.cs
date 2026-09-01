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
            double leftChannel = -1.0;
            for(int j = startChannel + 1; j < centroid; j++)
            {
                if (counts[j] > halfValue)
                {
                    if (counts[j] == counts[j - 1]) return null;
                    leftChannel = (double)(j - 1) + (halfValue - counts[j - 1]) / (counts[j] - counts[j - 1]);
                    break;
                }
            }
            if (leftChannel < 0.0) return null;
            double rightChannel = -1.0;
            for(int k = endChannel - 1; k > centroid; k--)
            {
                if (counts[k] > halfValue)
                {
                    if (counts[k] == counts[k + 1]) return null;
                    rightChannel = (double)(k + 1) - (halfValue - counts[k + 1]) / (counts[k] - counts[k + 1]);
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
