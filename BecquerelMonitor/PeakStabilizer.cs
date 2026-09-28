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
                if (leftChannel < 0) leftChannel = 0;
                if (rightChannel > energySpectrum.NumberOfChannels - 1) rightChannel = energySpectrum.NumberOfChannels - 1;
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
            // ⛔ `AMBER110` (П170, 28.09.2026): ОДНА ИЛИ ДВЕ ОПОРЫ — ПРЕЖНЯЯ
            //    ШКАЛА, ПРОЧИТАННАЯ В СДВИНУТОМ/РАСТЯНУТОМ НОМЕРЕ КАНАЛА, А НЕ
            //    ПРЯМАЯ. Здесь при одной опоре добавлялась точка (0 кан, 0 кэВ) и
            //    подгонялась прямая — нуль АЦП объявлялся нулём энергии (у 125 из
            //    136 спектров корпуса E(0) < 0), а при двух — прямая через две точки;
            //    оба раза кривизна шкалы (непропорциональность света NaI, Payne et al.,
            //    IEEE TNS 58, 2011) выбрасывалась. Замер `CalibPeaksProbeP170 --stab`, шкала
            //    RC103_Th232WT20, усиление не менялось: опора 661.66 — 1460.82
            //    −56.6 кэВ, 2614.51 −223 кэВ; опора 1460.82 — 661.66 +27.4 кэВ; опоры
            //    661.66 и 1460.82 — 59.54 −40 кэВ, 2614.51 −93 кэВ.
            //    Уход усиления ФЭУ растягивает амплитуды, то есть номер канала,
            //    уход нуля АЦП его сдвигает: линия, стоявшая на канале C(E) прежней
            //    шкалы, встаёт на ch, и C(E) = a + b·ch. Новая шкала — прежняя, прочитанная
            //    в канале a + b·ch: степень и форма прежние. Одна опора определяет
            //    только растяжение (a = 0, E(0) прежний), две — растяжение и сдвиг.
            //    Повторный вызов при тех же каналах даёт a = 0, b = 1 и шкалу не
            //    трогает; три опоры и больше — подгонка полинома ниже, как было.
            if (calibrationPoints.Count <= 2)
            {
                PolynomialEnergyCalibration remapped = RemapChannels(polynomialEnergyCalibration, calibrationPoints,
                                                                     energySpectrum.NumberOfChannels);
                if (remapped == null || !remapped.CheckCalibration(channels: energySpectrum.NumberOfChannels))
                {
                    return;
                }
                resultData.EnergySpectrum.EnergyCalibration = remapped;
                return;
            }
            // Order must not exceed the number of points minus one (else the fit is
            // under-determined) and must stay within the range ChannelToEnergy supports (<= 4).
            int polynomialOrder = Math.Min(calibrationPoints.Count - 1, 4);
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

        /// <summary>
        /// (`AMBER110`) Прежняя шкала, прочитанная в канале a + b·ch: одна точка —
        /// a = 0, b = C(E)/ch; две — прямая через (ch₁, C(E₁)) и (ch₂, C(E₂)).
        /// Коэффициенты — биномиальным разложением Σ c_i·(a + b·ch)^i, степень
        /// прежняя. null — опора вне шкалы, две опоры на одном канале или
        /// убывающее соответствие: шкала тогда не трогается вовсе.
        /// </summary>
        static PolynomialEnergyCalibration RemapChannels(PolynomialEnergyCalibration old, List<CalibrationPoint> points, int channels)
        {
            int m = points.Count;
            double[] found = new double[m];
            double[] expected = new double[m];
            for (int j = 0; j < m; j++)
            {
                found[j] = points[j].Channel;
                expected[j] = old.EnergyToChannel((double)points[j].Energy, maxCh: channels);
                if (!(expected[j] > 0.0) || !(expected[j] < channels) || !(found[j] > 0.0)
                    || double.IsInfinity(expected[j]))
                {
                    return null;
                }
            }
            double a, b;
            if (m == 1)
            {
                a = 0.0;
                b = expected[0] / found[0];
            }
            else
            {
                if (found[1] == found[0])
                {
                    return null;
                }
                b = (expected[1] - expected[0]) / (found[1] - found[0]);
                a = expected[0] - b * found[0];
            }
            if (!(b > 0.0) || double.IsInfinity(b) || double.IsNaN(a) || double.IsInfinity(a))
            {
                return null;
            }
            double[] c = old.Coefficients;
            int order = old.PolynomialOrder;
            if (c == null || c.Length != order + 1)
            {
                return null;
            }
            double[] d = new double[order + 1];
            for (int i = 0; i <= order; i++)
            {
                // c_i·(a + b·ch)^i = c_i·Σ_k C(i,k)·a^(i−k)·b^k·ch^k
                double binom = 1.0;
                for (int k = 0; k <= i; k++)
                {
                    d[k] += c[i] * binom * Math.Pow(a, i - k) * Math.Pow(b, k);
                    binom = binom * (i - k) / (k + 1);
                }
            }
            PolynomialEnergyCalibration result = (PolynomialEnergyCalibration)old.Clone();
            result.Coefficients = d;
            result.PolynomialOrder = order;
            return result;
        }
    }
}
