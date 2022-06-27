using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
	// Token: 0x02000065 RID: 101
	public class PeakDetector
	{
		// Token: 0x0600050D RID: 1293 RVA: 0x0001DC68 File Offset: 0x0001BE68
		public List<Peak> DetectPeak(ResultData resultData)
		{
			EnergySpectrum energySpectrum = resultData.EnergySpectrum;
			SimplePeakDetectionMethodConfig simplePeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
			int numberOfChannels = energySpectrum.NumberOfChannels;
			int polynomialOrder = simplePeakDetectionMethodConfig.PolynomialOrder;
			int num = simplePeakDetectionMethodConfig.WindowSize / 2;
			double threshold = simplePeakDetectionMethodConfig.Threshold;
			double num2 = simplePeakDetectionMethodConfig.Tolerance / 100.0;
			double num3 = 0.0;
			for (int i = 0; i < numberOfChannels; i++)
			{
				if (num3 < (double)energySpectrum.Spectrum[i])
				{
					num3 = (double)energySpectrum.Spectrum[i];
				}
			}
			double[] array = SavitzkyGolayMethod.CalcSavitzkyGolayWeight(1, polynomialOrder, num);
			double[] array2 = new double[numberOfChannels];
			for (int j = num; j < numberOfChannels - num; j++)
			{
				double num4 = 0.0;
				for (int k = j - num; k <= j + num; k++)
				{
					num4 += (double)energySpectrum.Spectrum[k] * array[k - (j - num)];
				}
				array2[j] = num4;
			}
			List<Peak> list = new List<Peak>();
			resultData.DetectedPeaks.Clear();
			double num5 = 0.0;
			double num6 = 0.0;
			double num7 = double.PositiveInfinity;
			for (int l = num; l < numberOfChannels - num; l++)
			{
				double num8 = array2[l];
				if (num6 < num8)
				{
					num6 = num8;
				}
				if ((double)energySpectrum.Spectrum[l] < num7)
				{
					num7 = (double)energySpectrum.Spectrum[l];
				}
				if (num5 > 0.0 && num8 < 0.0 && num6 > threshold && energySpectrum.Spectrum[l] > 0)
				{
					Peak peak = new Peak();
					peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy((double)l);
					if (energySpectrum.Spectrum[l] > energySpectrum.Spectrum[l - 1])
					{
						peak.Channel = l;
					}
					else
					{
						peak.Channel = l - 1;
					}
					list.Add(peak);
					resultData.DetectedPeaks.Add(peak);
					num6 = 0.0;
					num7 = (double)energySpectrum.Spectrum[l];
				}
				num5 = num8;
			}
			foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
			{
				Peak peak2 = null;
				double num9 = num2;
				foreach (Peak peak3 in list)
				{
					double num10 = Math.Abs((peak3.Energy - nuclideDefinition.Energy) / nuclideDefinition.Energy);
					if (num10 < num2 && num10 < num9)
					{
						num9 = num10;
						peak2 = peak3;
					}
				}
				if (peak2 != null)
				{
					if (peak2.Nuclide == null)
					{
						peak2.Nuclide = nuclideDefinition;
					}
					else if (Math.Abs(peak2.Energy - nuclideDefinition.Energy) < Math.Abs(peak2.Energy - peak2.Nuclide.Energy))
					{
						peak2.Nuclide = nuclideDefinition;
					}
				}
			}
			return list;
		}

		// Token: 0x0400025C RID: 604
		NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
	}
}
