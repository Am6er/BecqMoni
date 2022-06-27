using System;

namespace BecquerelMonitor
{
	// Token: 0x02000032 RID: 50
	public class DoseRateManager
	{
		// Token: 0x060002B0 RID: 688 RVA: 0x0000D214 File Offset: 0x0000B414
		public DoseRate Calculate(ResultData resultData)
		{
			DoseRate doseRate = new DoseRate();
			DeviceConfigInfo deviceConfig = resultData.DeviceConfig;
			if (deviceConfig == null)
			{
				return null;
			}
			DoseRateConfig doseRateConfig = deviceConfig.DoseRateConfig;
			double num = 1.0 / (doseRateConfig.Sensitivity / 60.0);
			EnergySpectrum energySpectrum = resultData.EnergySpectrum;
			double measurementTime = energySpectrum.MeasurementTime;
			if (doseRateConfig.Sensitivity <= 0.0 || measurementTime <= 0.0)
			{
				return null;
			}
			try
			{
				int num2 = 0;
				int num3 = (int)Math.Ceiling(energySpectrum.EnergyCalibration.EnergyToChannel(doseRateConfig.LowerBound));
				int num4 = (int)Math.Floor(energySpectrum.EnergyCalibration.EnergyToChannel(doseRateConfig.UpperBound));
				int num5 = num3;
				while (num5 < energySpectrum.NumberOfChannels && num5 <= num4)
				{
					if (num5 > 0)
					{
						num2 += energySpectrum.Spectrum[num5];
					}
					num5++;
				}
				doseRate.Rate = (double)num2 / measurementTime * num;
				doseRate.Error = Math.Sqrt((double)num2) / measurementTime * num;
			}
			catch (Exception)
			{
				return null;
			}
			return doseRate;
		}
	}
}
