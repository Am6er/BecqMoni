using BecquerelMonitor.Utils;
using System;
using System.Threading.Tasks;

namespace BecquerelMonitor
{
    // Token: 0x02000032 RID: 50
    public class DoseRateManager
    {
        // Token: 0x060002B0 RID: 688 RVA: 0x0000D214 File Offset: 0x0000B414
        public DoseRate Calculate(ResultData resultData, DoseRateConfig config, BackgroundMode mode)
        {
            EnergySpectrum energySpectrum;
            if (mode == BackgroundMode.Substract && resultData.BackgroundEnergySpectrum != null)
            {
                SpectrumAriphmetics sa = new SpectrumAriphmetics(resultData.EnergySpectrum);
                energySpectrum = sa.Substract(resultData.BackgroundEnergySpectrum);
                sa.Dispose();
            } else
            {
                energySpectrum = resultData.EnergySpectrum;
            }
            PolynomialEnergyCalibration calibration = (PolynomialEnergyCalibration)energySpectrum.EnergyCalibration;
            DoseRate doseRate = new DoseRate();

            foreach (DoseRateCalibrationPoint point in config.DoseRateCalibrationPoints)
            {
                int startch = (int)calibration.EnergyToChannel(point.LowerBound);
                int endch = (int)calibration.EnergyToChannel(point.UpperBound);
                double rate = 0.0;
                for (int i = startch; i <= endch; i++)
                {
                    rate += energySpectrum.Spectrum[i];
                }
                rate *= point.Sensitivity;
                doseRate.Rate += rate;
            }

            if (doseRate.Rate >= double.MaxValue || energySpectrum.MeasurementTime == 0.0)
            {
                doseRate.Rate = 0.0;
                return doseRate;
            }

            doseRate.Error = Math.Sqrt(doseRate.Rate) / energySpectrum.MeasurementTime;
            doseRate.Rate /= energySpectrum.MeasurementTime;
            return doseRate;
        }
    }
}
