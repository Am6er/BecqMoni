using System;
using System.Collections.Generic;
using BecquerelMonitor.Utils;

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
            int smaWindowsSize = 11;
            double[] smoothedArray = new double[energySpectrum.NumberOfChannels];
            for (int i = 0; i < energySpectrum.NumberOfChannels; i++)
            {
                double newCount = 0.0;
                for (int j = i - smaWindowsSize / 2; j < i - smaWindowsSize / 2 + smaWindowsSize; j++)
                {
                    int chan = j;
                    if (chan < 0)
                    {
                        chan = 0;
                    }
                    else if (j >= energySpectrum.NumberOfChannels)
                    {
                        chan = energySpectrum.NumberOfChannels - 1;
                    }
                    newCount += (double)energySpectrum.Spectrum[chan];
                }
                smoothedArray[i] = newCount / (double)smaWindowsSize;
            }
            resultData.CalibrationPeaks.Clear();
            foreach (TargetPeak targetPeak in deviceConfig.StabilizerConfig.TargetPeaks)
            {
                double targetPeakEnergy = (double)targetPeak.Energy;
                double targetPeakEnergyErrMin = targetPeakEnergy * (1.0 - (double)targetPeak.Error / 100.0);
                double tragetPeakEnergyErrMax = targetPeakEnergy * (1.0 + (double)targetPeak.Error / 100.0);
                int leftChannel = (int)Math.Floor(deviceConfig.EnergyCalibration.EnergyToChannel(targetPeakEnergyErrMin, maxChannels: energySpectrum.NumberOfChannels));
                int rightChannel = (int)Math.Ceiling(deviceConfig.EnergyCalibration.EnergyToChannel(tragetPeakEnergyErrMax, maxChannels: energySpectrum.NumberOfChannels));
                double currentPeakMaximum = 0.0;
                int channel = -1;
                for (int k = leftChannel; k <= rightChannel; k++)
                {
                    if (smoothedArray[k] > currentPeakMaximum)
                    {
                        currentPeakMaximum = smoothedArray[k];
                        channel = k;
                    }
                }
                if (channel > 0)
                {
                    Peak peak = new Peak();
                    peak.Channel = channel;
                    peak.Energy = (double)targetPeak.Energy;
                    peak.LeftChannel = leftChannel;
                    peak.RightChannel = rightChannel;
                    resultData.CalibrationPeaks.Add(peak);
                }
            }
            List<Peak> calibrationPeaks = resultData.CalibrationPeaks;
            PolynomialEnergyCalibration polynomialEnergyCalibration = resultData.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (polynomialEnergyCalibration == null || calibrationPeaks.Count < 1)
            {
                return;
            }
            PolynomialEnergyCalibration newPolynomialEnergyCalibration = (PolynomialEnergyCalibration)polynomialEnergyCalibration.Clone();
            List<CalibrationPoint> calibrationPoints = new List<CalibrationPoint>();
            foreach (Peak calibrationPeak in calibrationPeaks)
            {
                int count = 0;
                if (calibrationPeak.Channel >= 0 && calibrationPeak.Channel < energySpectrum.Spectrum.Length)
                {
                    count = energySpectrum.Spectrum[calibrationPeak.Channel];
                }
                calibrationPoints.Add(new CalibrationPoint(calibrationPeak.Channel, (decimal)calibrationPeak.Energy, count));
            }
            if (calibrationPoints.Count == 1)
            {
                calibrationPoints.Add(new CalibrationPoint(0, 0m, 0));
            }
            int polynomialOrder = Math.Max(2, calibrationPoints.Count - 1);
            if (polynomialOrder < 1)
            {
                polynomialOrder = 1;
            }
            double[] matrix;
            try
            {
                matrix = CalibrationSolver.Solve(calibrationPoints, polynomialOrder);
                if (matrix == null)
                {
                    return;
                }
            }
            catch (Exception)
            {
                return;
            }
            newPolynomialEnergyCalibration.Coefficients = new double[matrix.Length];
            newPolynomialEnergyCalibration.PolynomialOrder = matrix.Length - 1;
            newPolynomialEnergyCalibration.Coefficients = matrix;
            if (!newPolynomialEnergyCalibration.CheckCalibration(channels: energySpectrum.NumberOfChannels))
            {
                return;
            }
            resultData.EnergySpectrum.EnergyCalibration = newPolynomialEnergyCalibration;
        }
    }
}
