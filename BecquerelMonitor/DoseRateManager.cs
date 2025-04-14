using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000032 RID: 50
    public class DoseRateManager
    {
        private GlobalConfigManager globalConfigManager;
        public DoseRateManager(GlobalConfigManager globalConfigManager) 
        {
            this.globalConfigManager = globalConfigManager;
        }

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

            List<double> errors = new List<double>();
            List<double> doseRates = new List<double>();
            foreach (DoseRateCalibrationPoint point in config.DoseRateCalibrationPoints)
            {
                int startch = (int)calibration.EnergyToChannel(point.LowerBound, maxCh: energySpectrum.NumberOfChannels);
                int endch = (int)calibration.EnergyToChannel(point.UpperBound, maxCh: energySpectrum.NumberOfChannels);
                if (startch < 0) startch = 0;
                if (endch >= energySpectrum.Spectrum.Length) endch = energySpectrum.Spectrum.Length - 1;
                double counts = 0.0;
                for (int i = startch; i <= endch; i++)
                {
                    counts += energySpectrum.Spectrum[i];
                }
                double error = Math.Sqrt(counts) / counts;
                double dr = counts * point.Sensitivity;
                doseRates.Add(dr);
                errors.Add(dr * error);
            }

            doseRate.Rate = doseRates.Sum();
            if (doseRate.Rate >= double.MaxValue || energySpectrum.MeasurementTime == 0.0)
            {
                doseRate.Rate = 0.0;
                return doseRate;
            }

            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            double errorLevel = (double)globalConfig.MeasurementConfig.ErrorLevel;
            doseRate.Error = errorLevel * Math.Sqrt(errors.Sum(e => e * e)) / energySpectrum.MeasurementTime;
            doseRate.Rate /= energySpectrum.MeasurementTime;
            return doseRate;
        }
    }
}
