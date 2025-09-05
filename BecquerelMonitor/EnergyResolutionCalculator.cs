using BecquerelMonitor.Utils;
using MathNet.Numerics;
using System;

namespace BecquerelMonitor
{
    // Token: 0x02000086 RID: 134
    public class EnergyResolutionCalculator
    {
        // Token: 0x060006DF RID: 1759 RVA: 0x000285F0 File Offset: 0x000267F0
        public static EnergyResolutionResult CalculateFWHM(EnergySpectrum spectrum, double startEnergy, double endEnergy)
        {
            EnergyCalibration energyCalibration = spectrum.EnergyCalibration;
            int startChannel = (int)Math.Ceiling(energyCalibration.EnergyToChannel(startEnergy, maxChannels: spectrum.NumberOfChannels));
            int endChannel = (int)Math.Floor(energyCalibration.EnergyToChannel(endEnergy, maxChannels: spectrum.NumberOfChannels));
            return EnergyResolutionCalculator.CalculateFWHM(spectrum, startChannel, endChannel);
        }

        // Token: 0x060006E0 RID: 1760 RVA: 0x0002862C File Offset: 0x0002682C
        public static EnergyResolutionResult CalculateFWHM(EnergySpectrum spectrum, int startChannel, int endChannel)
        {
            EnergyCalibration energyCalibration = spectrum.EnergyCalibration;
            if (startChannel >= endChannel || spectrum.NumberOfChannels < endChannel)
            {
                return null;
            }
            EnergyResolutionCalculator.result.StartChannel = (double)startChannel;
            EnergyResolutionCalculator.result.EndChannel = (double)endChannel;
            int centroid = startChannel;
            if (spectrum.Spectrum.Length <= startChannel) return null;
            double centroid_counts = (double)spectrum.Spectrum[centroid];
            for (int i = startChannel; i <= endChannel; i++)
            {
                if ((double)spectrum.Spectrum[i] > centroid_counts)
                {
                    centroid_counts = (double)spectrum.Spectrum[i];
                    centroid = i;
                }
            }

            SpectrumAriphmetics sa = new SpectrumAriphmetics();
            int centroid_new = sa.FindCentroid(spectrum, centroid, startChannel, endChannel);
            sa.Dispose();


            EnergyResolutionCalculator.result.MaxChannel = (double)centroid_new;
            EnergyResolutionCalculator.result.MaxValue = (double)spectrum.Spectrum[(int)EnergyResolutionCalculator.result.MaxChannel];

            double start_counts = (double)spectrum.Spectrum[startChannel];
            double end_counts = (double)spectrum.Spectrum[endChannel];
            double maxBaseValue = start_counts + (end_counts - start_counts) * (double)(centroid - startChannel) / (double)(endChannel - startChannel);
            EnergyResolutionCalculator.result.StartValue = start_counts;
            EnergyResolutionCalculator.result.EndValue = end_counts;
            EnergyResolutionCalculator.result.MaxBaseValue = maxBaseValue;
            double halfValue = (centroid_counts - maxBaseValue) / 2.0 + maxBaseValue;
            EnergyResolutionCalculator.result.HalfValue = halfValue;
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
            EnergyResolutionCalculator.result.LeftChannel = leftChannel;
            EnergyResolutionCalculator.result.RightChannel = rightChannel;
            double leftEnergy = energyCalibration.ChannelToEnergy(leftChannel);
            double rightEnergy = energyCalibration.ChannelToEnergy(rightChannel);
            double resolution = (rightEnergy - leftEnergy) / energyCalibration.ChannelToEnergy((double)centroid);
            double resolutioninkev = rightEnergy - leftEnergy;
            EnergyResolutionCalculator.result.Resolution = resolution;
            EnergyResolutionCalculator.result.ResolutionInkeV = resolutioninkev;
            return EnergyResolutionCalculator.result;
        }

        // Token: 0x04000394 RID: 916
        static EnergyResolutionResult result = new EnergyResolutionResult();
    }
}
