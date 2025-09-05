using BecquerelMonitor.Utils;
using System;
using Windows.UI.Xaml.Documents;

namespace BecquerelMonitor
{
    public class EnergyResolutionCalculator
    {
        public static EnergyResolutionResult CalculateFWHM(EnergySpectrum spectrum, int startChannel, int endChannel)
        {
            EnergyCalibration energyCalibration = spectrum.EnergyCalibration;
            if (startChannel >= endChannel || spectrum.NumberOfChannels < endChannel) return null;
            int centroid = startChannel;
            if (spectrum.Spectrum.Length <= startChannel) return null;
            double centroid_counts = (double)spectrum.Spectrum[centroid] - SpectrumAriphmetics.getY(centroid, startChannel, endChannel, spectrum.Spectrum[startChannel], spectrum.Spectrum[endChannel]);
            for (int i = startChannel; i <= endChannel; i++)
            {
                if ((double)spectrum.Spectrum[i] - SpectrumAriphmetics.getY(i, startChannel, endChannel, spectrum.Spectrum[startChannel], spectrum.Spectrum[endChannel]) 
                    > centroid_counts - SpectrumAriphmetics.getY(centroid, startChannel, endChannel, spectrum.Spectrum[startChannel], spectrum.Spectrum[endChannel]))
                {
                    centroid_counts = (double)spectrum.Spectrum[i];
                    centroid = i;
                }
            }

            double start_counts = (double)spectrum.Spectrum[startChannel];
            double end_counts = (double)spectrum.Spectrum[endChannel];
            double maxBaseValue = start_counts + (end_counts - start_counts) * (double)(centroid - startChannel) / (double)(endChannel - startChannel);
            double halfValue = (centroid_counts - maxBaseValue) / 2.0 + maxBaseValue;
            double leftChannel = -1.0;
            for(int j = startChannel + 1; j < centroid; j++)
            {
                if ((double)spectrum.Spectrum[j] > halfValue)
                {
                    if (spectrum.Spectrum[j] == spectrum.Spectrum[j - 1]) return null;
                    leftChannel = (double)(j - 1) + (halfValue - (double)spectrum.Spectrum[j - 1]) / (double)(spectrum.Spectrum[j] - spectrum.Spectrum[j - 1]);
                    break;
                }
            }
            if (leftChannel < 0.0) return null;
            double rightChannel = -1.0;
            for(int k = endChannel - 1; k > centroid; k--)
            {
                if ((double)spectrum.Spectrum[k] > halfValue)
                {
                    if (spectrum.Spectrum[k] == spectrum.Spectrum[k + 1]) return null;
                    rightChannel = (double)(k + 1) - (halfValue - (double)spectrum.Spectrum[k + 1]) / (double)(spectrum.Spectrum[k] - spectrum.Spectrum[k + 1]);
                    break;
                }
            }
            if (rightChannel < 0.0) return null;

            double leftEnergy = energyCalibration.ChannelToEnergy(leftChannel);
            double rightEnergy = energyCalibration.ChannelToEnergy(rightChannel);
            double resolution = (rightEnergy - leftEnergy) / energyCalibration.ChannelToEnergy((double)centroid);
            double resolutioninkev = rightEnergy - leftEnergy;



            result.StartChannel = (double)startChannel;
            result.EndChannel = (double)endChannel;
            result.StartValue = start_counts;
            result.EndValue = end_counts;
            result.MaxBaseValue = maxBaseValue;
            result.LeftChannel = leftChannel;
            result.RightChannel = rightChannel;
            result.HalfValue = halfValue;
            result.MaxChannel = (double)centroid;
            result.MaxValue = (double)spectrum.Spectrum[(int)result.MaxChannel];
            result.Resolution = resolution;
            result.ResolutionInkeV = resolutioninkev;
            return result;
        }

        static EnergyResolutionResult result = new EnergyResolutionResult();
    }
}
