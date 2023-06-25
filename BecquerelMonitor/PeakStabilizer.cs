using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;

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
                int num5 = (int)Math.Floor(deviceConfig.EnergyCalibration.EnergyToChannel(e, maxChannels: energySpectrum.NumberOfChannels));
                int num6 = (int)Math.Ceiling(deviceConfig.EnergyCalibration.EnergyToChannel(e2, maxChannels: energySpectrum.NumberOfChannels));
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
            if (polynomialEnergyCalibration.Coefficients.Length < 2)
            {
                polynomialEnergyCalibration.PolynomialOrder++;
                double[] tmp = new double[3];
                Array.Copy(polynomialEnergyCalibration.Coefficients, tmp, 2);
                polynomialEnergyCalibration.Coefficients = tmp;
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
                    int ch1 = calibrationPeaks[0].Channel;
                    int ch2 = calibrationPeaks[1].Channel;
                    int ch3 = calibrationPeaks[2].Channel;
                    Matrix<double> matrix = Matrix<double>.Build.DenseOfArray(new double[,] {
                        { (double)(ch1 * ch1), (double)ch1, 1.0 },
                        { (double)(ch2 * ch2), (double)ch2, 1.0 },
                        { (double)(ch3 * ch3), (double)ch3, 1.0 },
                    });
                    Vector<double> matrix2 = Vector<double>.Build.Dense(new double[] {
                        (double)calibrationPeaks[0].Energy,
                        (double)calibrationPeaks[1].Energy,
                        (double)calibrationPeaks[2].Energy
                    });
                    double[] matrix3;
                    try
                    {
                        matrix3 = matrix.Solve(matrix2).ToArray();
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    polynomialEnergyCalibration2.Coefficients[2] = matrix3[0];
                    polynomialEnergyCalibration2.Coefficients[1] = matrix3[1];
                    polynomialEnergyCalibration2.Coefficients[0] = matrix3[2];
                    goto IL_52F;
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
