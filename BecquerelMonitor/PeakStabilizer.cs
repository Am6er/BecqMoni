using System;
using System.Collections.Generic;
using MaNet;

namespace BecquerelMonitor
{
	// Token: 0x02000077 RID: 119
	public class PeakStabilizer
	{
		// Token: 0x0600060A RID: 1546 RVA: 0x00025F10 File Offset: 0x00024110
		public void Stabilize(ResultData resultData)
		{
			DeviceConfigInfo deviceConfig = resultData.DeviceConfig;
			EnergySpectrum energySpectrum = resultData.EnergySpectrum;
			int num = 11;
			double[] array = new double[energySpectrum.NumberOfChannels];
			for (int i = 0; i < energySpectrum.NumberOfChannels; i++)
			{
				double num2 = 0.0;
				for (int j = i - num / 2; j < i - num / 2 + num; j++)
				{
					int num3 = j;
					if (num3 < 0)
					{
						num3 = 0;
					}
					else if (j >= energySpectrum.NumberOfChannels)
					{
						num3 = energySpectrum.NumberOfChannels - 1;
					}
					num2 += (double)energySpectrum.Spectrum[num3];
				}
				array[i] = num2 / (double)num;
			}
			resultData.CalibrationPeaks.Clear();
			foreach (TargetPeak targetPeak in deviceConfig.StabilizerConfig.TargetPeaks)
			{
				double num4 = (double)targetPeak.Energy;
				double e = num4 * (1.0 - (double)targetPeak.Error / 100.0);
				double e2 = num4 * (1.0 + (double)targetPeak.Error / 100.0);
				int num5 = (int)Math.Floor(deviceConfig.EnergyCalibration.EnergyToChannel(e));
				int num6 = (int)Math.Ceiling(deviceConfig.EnergyCalibration.EnergyToChannel(e2));
				double num7 = 0.0;
				int num8 = -1;
				for (int k = num5; k <= num6; k++)
				{
					if (array[k] > num7)
					{
						num7 = array[k];
						num8 = k;
					}
				}
				if (num8 > 0)
				{
					Peak peak = new Peak();
					peak.Channel = num8;
					peak.Energy = (double)targetPeak.Energy;
					peak.LeftChannel = num5;
					peak.RightChannel = num6;
					resultData.CalibrationPeaks.Add(peak);
				}
			}
			List<Peak> calibrationPeaks = resultData.CalibrationPeaks;
			if (!(deviceConfig.EnergyCalibration is PolynomialEnergyCalibration))
			{
				return;
			}
			PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)deviceConfig.EnergyCalibration;
			PolynomialEnergyCalibration polynomialEnergyCalibration2 = (PolynomialEnergyCalibration)polynomialEnergyCalibration.Clone();
			if (calibrationPeaks.Count < 1)
			{
				return;
			}
			if (calibrationPeaks.Count == 1)
			{
				int channel = calibrationPeaks[0].Channel;
				double energy = calibrationPeaks[0].Energy;
				double num9 = (energy - polynomialEnergyCalibration.Coefficients[2] * (double)channel * (double)channel - polynomialEnergyCalibration.Coefficients[0]) / (double)channel;
				if (num9 < 0.01)
				{
					return;
				}
				polynomialEnergyCalibration2.Coefficients[1] = num9;
			}
			else
			{
				if (calibrationPeaks.Count != 2)
				{
					int channel2 = calibrationPeaks[0].Channel;
					int channel3 = calibrationPeaks[1].Channel;
					int channel4 = calibrationPeaks[2].Channel;
					Matrix matrix = new Matrix(3, 3);
					matrix.Array[0][0] = (double)(channel2 * channel2);
					matrix.Array[0][1] = (double)channel2;
					matrix.Array[0][2] = 1.0;
					matrix.Array[1][0] = (double)(channel3 * channel3);
					matrix.Array[1][1] = (double)channel3;
					matrix.Array[1][2] = 1.0;
					matrix.Array[2][0] = (double)(channel4 * channel4);
					matrix.Array[2][1] = (double)channel4;
					matrix.Array[2][2] = 1.0;
					Matrix matrix2 = new Matrix(3, 1);
					matrix2.Array[0][0] = calibrationPeaks[0].Energy;
					matrix2.Array[1][0] = calibrationPeaks[1].Energy;
					matrix2.Array[2][0] = calibrationPeaks[2].Energy;
					Matrix matrix3 = new Matrix(3, 1);
					try
					{
						matrix3 = matrix.Solve(matrix2);
					}
					catch (Exception)
					{
						return;
					}
					if (matrix3.Array[1][0] >= 0.01)
					{
						polynomialEnergyCalibration2.Coefficients[2] = matrix3.Array[0][0];
						polynomialEnergyCalibration2.Coefficients[1] = matrix3.Array[1][0];
						polynomialEnergyCalibration2.Coefficients[0] = matrix3.Array[2][0];
						goto IL_52F;
					}
					return;
				}
				double num10 = (double)calibrationPeaks[0].Channel;
				double num11 = (double)calibrationPeaks[1].Channel;
				double energy2 = calibrationPeaks[0].Energy;
				double energy3 = calibrationPeaks[1].Energy;
				double num12 = (energy3 - energy2 - polynomialEnergyCalibration.Coefficients[2] * (num11 * num11 - num10 * num10)) / (num11 - num10);
				double num13 = energy2 - polynomialEnergyCalibration.Coefficients[2] * num10 * num10 - num12 * num10;
				if (num12 < 0.01)
				{
					return;
				}
				polynomialEnergyCalibration2.Coefficients[1] = num12;
				polynomialEnergyCalibration2.Coefficients[0] = num13;
			}
			IL_52F:
			resultData.EnergySpectrum.EnergyCalibration = polynomialEnergyCalibration2;
		}
	}
}
