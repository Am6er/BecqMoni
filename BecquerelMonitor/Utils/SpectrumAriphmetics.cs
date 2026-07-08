using BecquerelMonitor.Properties;
using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BecquerelMonitor.Utils
{
    public class SpectrumAriphmetics
    {
        public SpectrumAriphmetics()
        {

        }

        public SpectrumAriphmetics(DocEnergySpectrum docenergySpectrum)
        {
            this.MainSpectrum = docenergySpectrum;
        }

        public SpectrumAriphmetics(EnergySpectrum energySpectrum)
        {
            this.EnergySpectrum = energySpectrum.Clone();
        }

        public SpectrumAriphmetics(FwhmCalibration fwhmCalibration, EnergySpectrum energySpectrum, SmoothingMethod smoothingMethod)
        {
            this.FwhmCalibration = fwhmCalibration;
            this.EnergySpectrum = energySpectrum.Clone();
            int countlimit = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.CountLimit;
            bool progressiveSmooth = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.ProgresiveSmooth;
            switch (smoothingMethod)
            {
                case SmoothingMethod.SimpleMovingAverage:
                    int points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                    this.EnergySpectrum.Spectrum = SMA(this.EnergySpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                    this.EnergySpectrum.Spectrum = WMA(this.EnergySpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
            }
        }

        // Уточнение центроида пика по ядру: центр масс (интенсивно-взвешенное
        // среднее) на фоново-вычтенных отсчётах, по смежным бинам выше полумаксимума
        // вокруг вершины. Даёт субканальную позицию и устойчив к шуму и приплюснутым
        // вершинам (в отличие от сырого argmax, который скачет на один бин). Ограничение
        // ядра полумаксимумом отсекает хвосты и удалённых соседей.
        // Проверено на синтетике (плоские/шумные/наклонный фон) и на реальных спектрах.
        // useCenterOfMass=false возвращает сырой argmax (максимальный бин) — для
        // сравнения методов (переключается флагом в конфиге детекции).
        public double FindCentroid(EnergySpectrum energySpectrum, int centroid, int low_boundary, int high_boundary, bool useCenterOfMass = true)
        {
            int[] spectrum = energySpectrum.Spectrum;
            if (low_boundary < 0) low_boundary = 0;
            if (high_boundary >= energySpectrum.NumberOfChannels) high_boundary = energySpectrum.NumberOfChannels - 1;
            if (high_boundary <= low_boundary)
            {
                return low_boundary;
            }
            if (high_boundary - low_boundary < 3)
            {
                return spectrum[low_boundary] >= spectrum[high_boundary] ? low_boundary : high_boundary;
            }

            // Вершина и фон (минимум) в окне.
            int apex = low_boundary;
            int apexCounts = spectrum[low_boundary];
            int bg = spectrum[low_boundary];
            for (int i = low_boundary; i <= high_boundary; i++)
            {
                int v = spectrum[i];
                if (v > apexCounts) { apexCounts = v; apex = i; }
                if (v < bg) { bg = v; }
            }

            if (!useCenterOfMass)
            {
                // Прежний метод: максимальный бин.
                return apex;
            }

            double height = apexCounts - bg;
            if (height <= 0.0)
            {
                return apex;
            }

            // Смежные бины выше полумаксимума вокруг вершины (ядро пика).
            double threshold = bg + 0.5 * height;
            int left = apex;
            while (left > low_boundary && spectrum[left - 1] > threshold)
            {
                left--;
            }
            int right = apex;
            while (right < high_boundary && spectrum[right + 1] > threshold)
            {
                right++;
            }

            // Центр масс по ядру на фоново-вычтенных весах.
            double weightSum = 0.0;
            double weightedPos = 0.0;
            for (int i = left; i <= right; i++)
            {
                double w = spectrum[i] - bg;
                if (w <= 0.0) continue;
                weightSum += w;
                weightedPos += i * w;
            }

            if (weightSum <= 0.0)
            {
                return apex;
            }

            return weightedPos / weightSum;
        }

        double Ln(double x)
        {
            if (x < 1) return 0.0;
            return Math.Log(x);
        }

        /// <summary>
        /// Y = k*x + b
        /// using known points (x1,y1), (x2, y2)
        /// </summary>
        /// <param name="X"></param>
        /// <param name="x1"></param>
        /// <param name="x2"></param>
        /// <param name="y1"></param>
        /// <param name="y2"></param>
        /// <returns></returns>
        public static double getY(int X, int x1, int x2, int y1, int y2)
        {
            if (x1 - x2 != 0)
            {
                double k = (double)(y1 - y2) / (double)(x1 - x2);
                double b = (double)y1 - k * (double)x1;
                return k * (double)X + b;
            }
            else
            {
                return 0;
            }
        }

        public DocEnergySpectrum CombineWith(DocEnergySpectrum docenergySpectrum)
        {
            if (this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels == docenergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
            {
                PolynomialEnergyCalibration MainSpectrumEnergyCalibration = (PolynomialEnergyCalibration)this.MainSpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration;
                PolynomialEnergyCalibration CombinedSpectrumEnergyCalibration = (PolynomialEnergyCalibration)docenergySpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration;
                bool checkCalibration = CombinedSpectrumEnergyCalibration.CheckCalibration(docenergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels);
                if (!checkCalibration)
                {
                    MessageBox.Show(String.Format(Resources.ERRCombineBadCalibratedSpectra, docenergySpectrum.Filename));
                }
                if (CombinedSpectrumEnergyCalibration.Equals(MainSpectrumEnergyCalibration) || !checkCalibration)
                {
                    docenergySpectrum.ActiveResultData.EnergySpectrum.Spectrum[docenergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels - 1] = 0;
                    for (int i = 0; i < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels; i++)
                    {
                        this.MainSpectrum.ActiveResultData.EnergySpectrum.Spectrum[i] += docenergySpectrum.ActiveResultData.EnergySpectrum.Spectrum[i];
                    }
                }
                else
                {
                    for (int i = 0; i < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels; i++)
                    {
                        double getChannel = Math.Round(CombinedSpectrumEnergyCalibration.EnergyToChannel(MainSpectrumEnergyCalibration.ChannelToEnergy(i), maxCh: docenergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels));
                        if (getChannel >= 0 && getChannel < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
                        {
                            this.MainSpectrum.ActiveResultData.EnergySpectrum.Spectrum[i] += docenergySpectrum.ActiveResultData.EnergySpectrum.Spectrum[Convert.ToInt32(getChannel)];
                        }
                    }
                }

                this.MainSpectrum.ActiveResultData.EnergySpectrum.MeasurementTime += docenergySpectrum.ActiveResultData.EnergySpectrum.MeasurementTime;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.LiveTime += docenergySpectrum.ActiveResultData.EnergySpectrum.LiveTime;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.TotalPulseCount += docenergySpectrum.ActiveResultData.EnergySpectrum.TotalPulseCount;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.ValidPulseCount += docenergySpectrum.ActiveResultData.EnergySpectrum.ValidPulseCount;

                this.MainSpectrum.ActiveResultData.PresetTime += docenergySpectrum.ActiveResultData.PresetTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.PresetTime += docenergySpectrum.ActiveResultData.ResultDataStatus.PresetTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.ElapsedTime += docenergySpectrum.ActiveResultData.ResultDataStatus.ElapsedTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.TotalTime += docenergySpectrum.ActiveResultData.ResultDataStatus.TotalTime;
            } else
            {
               MessageBox.Show(Resources.CombineIncorrectChannels);
            }

            return this.MainSpectrum;
        }

        public EnergySpectrum Substract(EnergySpectrum bgenergySpectrum)
        {
            EnergySpectrum substractedEnergySpectrum = this.EnergySpectrum.Clone();
            if (this.EnergySpectrum.MeasurementTime == 0 || bgenergySpectrum.MeasurementTime == 0)
            {
                return substractedEnergySpectrum;
            }
            double norm_coeff = this.EnergySpectrum.MeasurementTime / bgenergySpectrum.MeasurementTime;
            substractedEnergySpectrum.TotalPulseCount = 0;
            if (this.EnergySpectrum.EnergyCalibration.Equals(bgenergySpectrum.EnergyCalibration))
            {
                Parallel.For(0, substractedEnergySpectrum.NumberOfChannels, i =>
                {
                    substractedEnergySpectrum.Spectrum[i] = Convert.ToInt32(this.EnergySpectrum.Spectrum[i] - norm_coeff * bgenergySpectrum.Spectrum[i]);
                    if (substractedEnergySpectrum.Spectrum[i] < 0)
                    {
                        substractedEnergySpectrum.Spectrum[i] = 0;
                    }
                });
                substractedEnergySpectrum.TotalPulseCount = substractedEnergySpectrum.Spectrum.Sum(x => (long)x);
                substractedEnergySpectrum.ValidPulseCount = substractedEnergySpectrum.TotalPulseCount;
            } else
            {
                Parallel.For(0, substractedEnergySpectrum.NumberOfChannels, i =>
                {
                    double enrg = this.EnergySpectrum.EnergyCalibration.ChannelToEnergy(i);
                    int bgchan = Convert.ToInt32(bgenergySpectrum.EnergyCalibration.EnergyToChannel(enrg, maxChannels: substractedEnergySpectrum.NumberOfChannels));
                    if (bgchan >= 0 && bgchan < bgenergySpectrum.NumberOfChannels)
                    {
                        substractedEnergySpectrum.Spectrum[i] = Convert.ToInt32(this.EnergySpectrum.Spectrum[i] - norm_coeff * bgenergySpectrum.Spectrum[bgchan]);
                        if (substractedEnergySpectrum.Spectrum[i] < 0)
                        {
                            substractedEnergySpectrum.Spectrum[i] = 0;
                        }
                    }
                });
                substractedEnergySpectrum.TotalPulseCount = substractedEnergySpectrum.Spectrum.Sum(x => (long)x);
                substractedEnergySpectrum.ValidPulseCount = substractedEnergySpectrum.TotalPulseCount;
            }
            return substractedEnergySpectrum;
        }

        public static EnergySpectrum NormalizeSpectrum(EnergySpectrum spectrum, ROIConfigData roi)
        {
            EnergySpectrum normalizedSpectrum = spectrum.Clone();
            if (roi == null)
            {
                return normalizedSpectrum;
            }

            ROIAriphmetics roiAriphmetics = new ROIAriphmetics(roi);
            if (!roiAriphmetics.HasValidCurve)
            {
                return normalizedSpectrum;
            }

            int minChannel = Convert.ToInt32(spectrum.EnergyCalibration.EnergyToChannel(roiAriphmetics.MinEnergy, maxChannels: normalizedSpectrum.NumberOfChannels));
            int maxChannel = Convert.ToInt32(spectrum.EnergyCalibration.EnergyToChannel(roiAriphmetics.MaxEnergy, maxChannels: normalizedSpectrum.NumberOfChannels));
            normalizedSpectrum.TotalPulseCount = 0;
            Parallel.For(0, normalizedSpectrum.NumberOfChannels, i =>
            {
                if (i > maxChannel || i < minChannel)
                {
                    normalizedSpectrum.Spectrum[i] = 0;
                } 
                else
                {
                    double enrg = normalizedSpectrum.EnergyCalibration.ChannelToEnergy(i);
                    ROIEfficiencyData effData = roiAriphmetics.CalculateEfficiency(enrg);
                    if (effData != null && effData.Efficiency > 0)
                    {
                        double normChannelValue = normalizedSpectrum.Spectrum[i] / effData.Efficiency;
                        if (normChannelValue < 0 || normChannelValue >= int.MaxValue) 
                        { 
                            normalizedSpectrum.Spectrum[i] = 0; 
                        }
                        else 
                        {
                            normalizedSpectrum.Spectrum[i] = Convert.ToInt32(normChannelValue);
                        }
                    }
                    else
                    {
                        normalizedSpectrum.Spectrum[i] = 0;
                    }
                }
            });

            try 
            {
                normalizedSpectrum.TotalPulseCount = normalizedSpectrum.Spectrum.Sum(x => (long)x);
            }
            catch (OverflowException)
            {
                normalizedSpectrum.TotalPulseCount = long.MaxValue;
            }
            
            normalizedSpectrum.ValidPulseCount = normalizedSpectrum.TotalPulseCount;

            return normalizedSpectrum;
        }

        public EnergySpectrum Continuum(List<Peak> peaks = null, double coeff = 1.0, double peakWidthWidenFactor = DefaultPeakWidthWidenFactor)
        {
            this.peakWidthWidenFactor = IsFinite(peakWidthWidenFactor) && peakWidthWidenFactor >= 1.0
                ? peakWidthWidenFactor
                : DefaultPeakWidthWidenFactor;
            EnergySpectrum continuum = this.EnergySpectrum.Clone();
            continuum.Spectrum = SASNIP(this.EnergySpectrum.Spectrum, peaks, coeff: coeff, useLLS: true, decreasing: true);
            Parallel.For(0, continuum.NumberOfChannels, i =>
            {
                if (continuum.Spectrum[i] > this.EnergySpectrum.Spectrum[i])
                {
                    continuum.Spectrum[i] = this.EnergySpectrum.Spectrum[i];
                }
            });
            
            return continuum;
        }

        public EnergySpectrum SubtractPeak(Peak peak, EnergySpectrum energySpectrum)
        {
            EnergySpectrum result = energySpectrum.Clone();
            (int[] peakspectrum, int min_val, int max_val, Color peakColor) = GetPeak(peak, result);
            for (int i = min_val; i <= max_val; i++)
            {
                result.Spectrum[i] -= peakspectrum[i];
                if (result.Spectrum[i] < 0)
                {
                    result.Spectrum[i] = 0;
                }
            }
            return result;
        }

        public EnergySpectrum SubtractPeaks(List<Peak> peaks, EnergySpectrum energySpectrum)
        {
            EnergySpectrum result = energySpectrum.Clone();
            foreach(Peak peak in peaks)
            {
                result = SubtractPeak(peak, result);
            }
            return result;
        }

        public CalibrationPeak CalcPeakFitValues(CalibrationPeak Peak, int startCh, int endCh)
        {
            Peak.GaussianChi2 = -1.0;
            Peak.GaussianNdp = -1;
            Peak.ExpGaussExpChi2 = -1.0;
            Peak.ExpGaussExpNdp = -1;
            Peak.ExpGaussExpBestLeftTail = 1.0;
            Peak.ExpGaussExpBestRightTail = 1.0;
            Peak.ExpGaussExpCandidateChi2 = null;
            Peak.ExpGaussExpCandidateNdp = null;
            Peak.VoigtChi2 = -1.0;
            Peak.VoigtNdp = -1;
            Peak.VoigtBestSigma = 1.0;
            Peak.VoigtBestGamma = 1.0;
            Peak.VoigtCandidateChi2 = null;
            Peak.VoigtCandidateNdp = null;

            if (this.EnergySpectrum == null || this.EnergySpectrum.Spectrum == null || this.EnergySpectrum.NumberOfChannels <= 0)
            {
                Peak.Chi2pNdp = 0.0;
                Peak.PeakType = global::BecquerelMonitor.FwhmCalibration.GaussianPeakType;
                Peak.ExpGaussExpLeftTail = 1.0;
                Peak.ExpGaussExpRightTail = 1.0;
                Peak.VoigtSigma = 1.0;
                Peak.VoigtGamma = 1.0;
                return Peak;
            }

            if (startCh > endCh)
            {
                int temp = startCh;
                startCh = endCh;
                endCh = temp;
            }

            startCh = Math.Max(0, startCh);
            endCh = Math.Min(this.EnergySpectrum.NumberOfChannels - 1, endCh);
            if (startCh > endCh)
            {
                Peak.Chi2pNdp = 0.0;
                Peak.PeakType = global::BecquerelMonitor.FwhmCalibration.GaussianPeakType;
                Peak.ExpGaussExpLeftTail = 1.0;
                Peak.ExpGaussExpRightTail = 1.0;
                Peak.VoigtSigma = 1.0;
                Peak.VoigtGamma = 1.0;
                return Peak;
            }

            double chi2pndp = 0.0;
            int peak_type;
            double left_tail;
            double right_tail;
            double voigt_sigma;
            double voigt_gamma;
            const double tailStep = 0.1;
            const int tailSteps = 50;
            int candidateCount = tailSteps * tailSteps;
            double[] expGaussExpCandidateChi2 = new double[candidateCount];
            int[] expGaussExpCandidateNdp = new int[candidateCount];
            double[] voigtCandidateChi2 = new double[candidateCount];
            int[] voigtCandidateNdp = new int[candidateCount];
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                expGaussExpCandidateChi2[candidateIndex] = -1.0;
                voigtCandidateChi2[candidateIndex] = -1.0;
            }
            double fwhm = Peak.FWHM;
            int median = Math.Max(0, Math.Min(Peak.Channel, this.EnergySpectrum.NumberOfChannels - 1));
            double amplitude = this.EnergySpectrum.Spectrum[median] - 
                getY(median, startCh, endCh, this.EnergySpectrum.Spectrum[startCh], this.EnergySpectrum.Spectrum[endCh]);
            int validChannels = 0;
            int nParameters = 3; //(double)amplitude, (double)fwhm, (double)median
            double currentChi2pndp = 0.0;
            double observedValue;
            double expectedValue;
            bool hasGoodCandidate = false;
            double gaussianChi2 = -1.0;
            int gaussianNdp = -1;
            double expGaussExpChi2 = -1.0;
            int expGaussExpNdp = -1;
            double expGaussExpChi2pNdp = Double.PositiveInfinity;
            double expGaussExpLeftTail = 1.0;
            double expGaussExpRightTail = 1.0;
            double voigtChi2 = -1.0;
            int voigtNdp = -1;
            double voigtChi2pNdp = Double.PositiveInfinity;
            double bestVoigtSigma = 1.0;
            double bestVoigtGamma = 1.0;
            int channelsCount = endCh - startCh + 1;
            double sigma = fwhm / 2.35482;
            double[] observedValues = new double[channelsCount];
            double[] measurementVariances = new double[channelsCount];
            double[] leftBaselineWeights = new double[channelsCount];
            double[] rightBaselineWeights = new double[channelsCount];
            double[] tValues = new double[channelsCount];
            double[] gaussChiPrefix = new double[channelsCount + 1];
            double[] gaussLeftProjectionPrefix = new double[channelsCount + 1];
            double[] gaussRightProjectionPrefix = new double[channelsCount + 1];
            int[] gaussValidPrefix = new int[channelsCount + 1];
            int[] leftTailEnds = new int[tailSteps + 1];
            double[] leftTailChi = new double[tailSteps + 1];
            double[] leftTailLeftProjection = new double[tailSteps + 1];
            double[] leftTailRightProjection = new double[tailSteps + 1];
            int[] leftTailValid = new int[tailSteps + 1];
            int[] rightTailStarts = new int[tailSteps + 1];
            double[] rightTailChi = new double[tailSteps + 1];
            double[] rightTailLeftProjection = new double[tailSteps + 1];
            double[] rightTailRightProjection = new double[tailSteps + 1];
            int[] rightTailValid = new int[tailSteps + 1];
            bool[] validSamples = new bool[channelsCount];
            bool canFit = IsFinite(fwhm) && fwhm > 0.0 && IsFinite(amplitude) && amplitude > 0.0;
            int endPointLeftCounts = this.EnergySpectrum.Spectrum[startCh];
            int endPointRightCounts = this.EnergySpectrum.Spectrum[endCh];
            double covariance00 = 1.0 / Math.Max(1.0, endPointLeftCounts);
            double covariance01 = 0.0;
            double covariance11 = 1.0 / Math.Max(1.0, endPointRightCounts);

            for (int index = 0; index < channelsCount; index++)
            {
                int channel = startCh + index;
                observedValue = this.EnergySpectrum.Spectrum[channel] -
                    getY(channel, startCh, endCh, this.EnergySpectrum.Spectrum[startCh], this.EnergySpectrum.Spectrum[endCh]);
                observedValues[index] = observedValue;
                gaussChiPrefix[index + 1] = gaussChiPrefix[index];
                gaussLeftProjectionPrefix[index + 1] = gaussLeftProjectionPrefix[index];
                gaussRightProjectionPrefix[index + 1] = gaussRightProjectionPrefix[index];
                gaussValidPrefix[index + 1] = gaussValidPrefix[index];

                if (!canFit)
                {
                    continue;
                }

                // Tail boundary searches include the endpoints, even though those
                // measurements do not contribute independent chi-square terms.
                double t = (channel - median) / sigma;
                tValues[index] = t;

                // Baseline is defined by the two endpoint measurements, so those
                // points contain no independent residual and must not enter chi-square.
                if (channel == startCh || channel == endCh)
                {
                    continue;
                }

                double baselineFraction = (double)(channel - startCh) / (endCh - startCh);
                double measurementVariance = Math.Max(1.0, this.EnergySpectrum.Spectrum[channel]);
                if (!IsFinite(measurementVariance) || measurementVariance <= 0.0)
                {
                    continue;
                }

                validSamples[index] = true;
                measurementVariances[index] = measurementVariance;
                leftBaselineWeights[index] = 1.0 - baselineFraction;
                rightBaselineWeights[index] = baselineFraction;
                covariance00 += leftBaselineWeights[index] * leftBaselineWeights[index] / measurementVariance;
                covariance01 += leftBaselineWeights[index] * rightBaselineWeights[index] / measurementVariance;
                covariance11 += rightBaselineWeights[index] * rightBaselineWeights[index] / measurementVariance;
                expectedValue = amplitude * Math.Exp(-0.5 * t * t);
                if (IsFinite(expectedValue) && expectedValue >= 0.0)
                {
                    double difference = expectedValue - observedValue;
                    gaussChiPrefix[index + 1] += ChiSquareTerm(difference, measurementVariance);
                    gaussLeftProjectionPrefix[index + 1] += leftBaselineWeights[index] * difference / measurementVariance;
                    gaussRightProjectionPrefix[index + 1] += rightBaselineWeights[index] * difference / measurementVariance;
                    gaussValidPrefix[index + 1]++;
                }
            }

            for (int leftStepIndex = 1; leftStepIndex <= tailSteps; leftStepIndex++)
            {
                double left = leftStepIndex * tailStep;
                int leftTailEnd = -1;
                double chiSum = 0.0;
                double leftProjection = 0.0;
                double rightProjection = 0.0;
                int validCount = 0;

                for (int index = 0; index < channelsCount; index++)
                {
                    if (tValues[index] > -left)
                    {
                        break;
                    }

                    leftTailEnd = index;
                    expectedValue = amplitude * Math.Exp(0.5 * left * left + left * tValues[index]);
                    if (validSamples[index] && IsFinite(expectedValue) && expectedValue >= 0.0)
                    {
                        double difference = expectedValue - observedValues[index];
                        chiSum += ChiSquareTerm(difference, measurementVariances[index]);
                        leftProjection += leftBaselineWeights[index] * difference / measurementVariances[index];
                        rightProjection += rightBaselineWeights[index] * difference / measurementVariances[index];
                        validCount++;
                    }
                }

                leftTailEnds[leftStepIndex] = leftTailEnd;
                leftTailChi[leftStepIndex] = chiSum;
                leftTailLeftProjection[leftStepIndex] = leftProjection;
                leftTailRightProjection[leftStepIndex] = rightProjection;
                leftTailValid[leftStepIndex] = validCount;
            }

            for (int rightStepIndex = 1; rightStepIndex <= tailSteps; rightStepIndex++)
            {
                double right = rightStepIndex * tailStep;
                int rightTailStart = channelsCount;
                double chiSum = 0.0;
                double leftProjection = 0.0;
                double rightProjection = 0.0;
                int validCount = 0;

                for (int index = channelsCount - 1; index >= 0; index--)
                {
                    if (tValues[index] <= right)
                    {
                        break;
                    }

                    rightTailStart = index;
                    expectedValue = amplitude * Math.Exp(0.5 * right * right - right * tValues[index]);
                    if (validSamples[index] && IsFinite(expectedValue) && expectedValue >= 0.0)
                    {
                        double difference = expectedValue - observedValues[index];
                        chiSum += ChiSquareTerm(difference, measurementVariances[index]);
                        leftProjection += leftBaselineWeights[index] * difference / measurementVariances[index];
                        rightProjection += rightBaselineWeights[index] * difference / measurementVariances[index];
                        validCount++;
                    }
                }

                rightTailStarts[rightStepIndex] = rightTailStart;
                rightTailChi[rightStepIndex] = chiSum;
                rightTailLeftProjection[rightStepIndex] = leftProjection;
                rightTailRightProjection[rightStepIndex] = rightProjection;
                rightTailValid[rightStepIndex] = validCount;
            }

            // For Gaussian
            currentChi2pndp = gaussChiPrefix[channelsCount];
            validChannels = gaussValidPrefix[channelsCount];
            int degreesOfFreedom = validChannels - nParameters;
            if (degreesOfFreedom > 0)
            {
                currentChi2pndp = CorrelatedChiSquare(
                    currentChi2pndp,
                    gaussLeftProjectionPrefix[channelsCount],
                    gaussRightProjectionPrefix[channelsCount],
                    covariance00,
                    covariance01,
                    covariance11);
                gaussianChi2 = currentChi2pndp;
                gaussianNdp = degreesOfFreedom;
                currentChi2pndp /= degreesOfFreedom;
                chi2pndp = currentChi2pndp;
                hasGoodCandidate = true;
            }

            // default fitting into gauss
            peak_type = FwhmCalibration.GaussianPeakType;
            left_tail = 1;
            right_tail = 1;
            voigt_sigma = 1.0;
            voigt_gamma = 1.0;

            // For ExpGaussExp
            nParameters = 5; //(double)amplitude, (double)median, (double)fwhm, left, right
            for (int leftStepIndex = 1; leftStepIndex <= tailSteps; leftStepIndex++)
            {
                double left = leftStepIndex * tailStep;
                int gaussStart = leftTailEnds[leftStepIndex] + 1;
                for (int rightStepIndex = 1; rightStepIndex <= tailSteps; rightStepIndex++)
                {
                    double right = rightStepIndex * tailStep;
                    int gaussEndExclusive = rightTailStarts[rightStepIndex];
                    currentChi2pndp = leftTailChi[leftStepIndex]
                        + (gaussChiPrefix[gaussEndExclusive] - gaussChiPrefix[gaussStart])
                        + rightTailChi[rightStepIndex];
                    double leftProjection = leftTailLeftProjection[leftStepIndex]
                        + (gaussLeftProjectionPrefix[gaussEndExclusive] - gaussLeftProjectionPrefix[gaussStart])
                        + rightTailLeftProjection[rightStepIndex];
                    double rightProjection = leftTailRightProjection[leftStepIndex]
                        + (gaussRightProjectionPrefix[gaussEndExclusive] - gaussRightProjectionPrefix[gaussStart])
                        + rightTailRightProjection[rightStepIndex];
                    validChannels = leftTailValid[leftStepIndex]
                        + (gaussValidPrefix[gaussEndExclusive] - gaussValidPrefix[gaussStart])
                        + rightTailValid[rightStepIndex];
                    degreesOfFreedom = validChannels - nParameters;
                    if (degreesOfFreedom <= 0)
                    {
                        continue;
                    }
                    currentChi2pndp = CorrelatedChiSquare(
                        currentChi2pndp,
                        leftProjection,
                        rightProjection,
                        covariance00,
                        covariance01,
                        covariance11);
                    double candidateChi2 = currentChi2pndp;
                    int candidateIndex = (leftStepIndex - 1) * tailSteps + rightStepIndex - 1;
                    expGaussExpCandidateChi2[candidateIndex] = candidateChi2;
                    expGaussExpCandidateNdp[candidateIndex] = degreesOfFreedom;
                    currentChi2pndp /= degreesOfFreedom;
                    if (currentChi2pndp < expGaussExpChi2pNdp)
                    {
                        expGaussExpChi2 = candidateChi2;
                        expGaussExpNdp = degreesOfFreedom;
                        expGaussExpChi2pNdp = currentChi2pndp;
                        expGaussExpLeftTail = left;
                        expGaussExpRightTail = right;
                    }
                    if (!hasGoodCandidate || currentChi2pndp < chi2pndp)
                    {
                        chi2pndp = currentChi2pndp;
                        peak_type = FwhmCalibration.ExpGaussExpPeakType;
                        left_tail = left;
                        right_tail = right;
                        voigt_sigma = 1.0;
                        voigt_gamma = 1.0;
                        hasGoodCandidate = true;
                    }
                }
            }

            // For Voigt
            nParameters = 5; //(double)amplitude, (double)median, (double)fwhm, sigma, gamma
            for (int sigmaStepIndex = 1; sigmaStepIndex <= tailSteps; sigmaStepIndex++)
            {
                double currentVoigtSigma = sigmaStepIndex * tailStep;
                for (int gammaStepIndex = 1; gammaStepIndex <= tailSteps; gammaStepIndex++)
                {
                    double currentVoigtGamma = gammaStepIndex * tailStep;
                    PseudoVoigtParameters voigtParameters;
                    if (!PseudoVoigtProfile.TryCreate(fwhm, currentVoigtSigma, currentVoigtGamma, out voigtParameters))
                    {
                        continue;
                    }
                    currentChi2pndp = 0.0;
                    double leftProjection = 0.0;
                    double rightProjection = 0.0;
                    validChannels = 0;
                    for (int index = 0; index < channelsCount; index++)
                    {
                        if (!validSamples[index])
                        {
                            continue;
                        }

                        expectedValue = amplitude * PseudoVoigtProfile.RelativeValue(startCh + index - median, voigtParameters);
                        if (!IsFinite(expectedValue) || expectedValue < 0.0)
                        {
                            validChannels = 0;
                            currentChi2pndp = Double.PositiveInfinity;
                            break;
                        }

                        double difference = expectedValue - observedValues[index];
                        currentChi2pndp += ChiSquareTerm(difference, measurementVariances[index]);
                        leftProjection += leftBaselineWeights[index] * difference / measurementVariances[index];
                        rightProjection += rightBaselineWeights[index] * difference / measurementVariances[index];
                        validChannels++;
                    }

                    degreesOfFreedom = validChannels - nParameters;
                    if (degreesOfFreedom <= 0)
                    {
                        continue;
                    }

                    currentChi2pndp = CorrelatedChiSquare(
                        currentChi2pndp,
                        leftProjection,
                        rightProjection,
                        covariance00,
                        covariance01,
                        covariance11);
                    double candidateChi2 = currentChi2pndp;
                    int candidateIndex = (sigmaStepIndex - 1) * tailSteps + gammaStepIndex - 1;
                    voigtCandidateChi2[candidateIndex] = candidateChi2;
                    voigtCandidateNdp[candidateIndex] = degreesOfFreedom;
                    currentChi2pndp /= degreesOfFreedom;
                    if (currentChi2pndp < voigtChi2pNdp)
                    {
                        voigtChi2 = candidateChi2;
                        voigtNdp = degreesOfFreedom;
                        voigtChi2pNdp = currentChi2pndp;
                        bestVoigtSigma = currentVoigtSigma;
                        bestVoigtGamma = currentVoigtGamma;
                    }
                    if (!hasGoodCandidate || currentChi2pndp < chi2pndp)
                    {
                        chi2pndp = currentChi2pndp;
                        peak_type = FwhmCalibration.VoigtPeakType;
                        left_tail = 1.0;
                        right_tail = 1.0;
                        voigt_sigma = currentVoigtSigma;
                        voigt_gamma = currentVoigtGamma;
                        hasGoodCandidate = true;
                    }
                }
            }

            if (!hasGoodCandidate)
            {
                chi2pndp = 0.0;
                peak_type = FwhmCalibration.GaussianPeakType;
                left_tail = 1.0;
                right_tail = 1.0;
                voigt_sigma = 1.0;
                voigt_gamma = 1.0;
            }

            Peak.GaussianChi2 = gaussianChi2;
            Peak.GaussianNdp = gaussianNdp;
            Peak.ExpGaussExpChi2 = expGaussExpChi2;
            Peak.ExpGaussExpNdp = expGaussExpNdp;
            Peak.ExpGaussExpBestLeftTail = expGaussExpLeftTail;
            Peak.ExpGaussExpBestRightTail = expGaussExpRightTail;
            Peak.ExpGaussExpCandidateChi2 = expGaussExpCandidateChi2;
            Peak.ExpGaussExpCandidateNdp = expGaussExpCandidateNdp;
            Peak.VoigtChi2 = voigtChi2;
            Peak.VoigtNdp = voigtNdp;
            Peak.VoigtBestSigma = bestVoigtSigma;
            Peak.VoigtBestGamma = bestVoigtGamma;
            Peak.VoigtCandidateChi2 = voigtCandidateChi2;
            Peak.VoigtCandidateNdp = voigtCandidateNdp;
            Peak.Chi2pNdp = chi2pndp;
            Peak.PeakType = peak_type;
            if (Peak.PeakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                Peak.ExpGaussExpLeftTail = left_tail;
                Peak.ExpGaussExpRightTail = right_tail;
                Peak.VoigtSigma = 1.0;
                Peak.VoigtGamma = 1.0;
            }
            else if (Peak.PeakType == FwhmCalibration.VoigtPeakType)
            {
                Peak.ExpGaussExpLeftTail = 1.0;
                Peak.ExpGaussExpRightTail = 1.0;
                Peak.VoigtSigma = voigt_sigma;
                Peak.VoigtGamma = voigt_gamma;
            }
            else
            {
                Peak.ExpGaussExpLeftTail = 1.0;
                Peak.ExpGaussExpRightTail = 1.0;
                Peak.VoigtSigma = 1.0;
                Peak.VoigtGamma = 1.0;
            }

            return Peak;
        }

        static double ChiSquareTerm(double difference, double variance)
        {
            return difference * difference / variance;
        }

        static double CorrelatedChiSquare(
            double diagonalChiSquare,
            double leftProjection,
            double rightProjection,
            double covariance00,
            double covariance01,
            double covariance11)
        {
            double determinant = covariance00 * covariance11 - covariance01 * covariance01;
            if (!IsFinite(determinant) || determinant <= 0.0)
            {
                return Double.PositiveInfinity;
            }

            double correction = (covariance11 * leftProjection * leftProjection -
                2.0 * covariance01 * leftProjection * rightProjection +
                covariance00 * rightProjection * rightProjection) / determinant;
            return Math.Max(0.0, diagonalChiSquare - correction);
        }

        static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        static double tail_support(double skew)
        {
            if (!IsFinite(skew) || skew <= 0.0)
            {
                return 8.0;
            }

            return Math.Max(8.0, (10.0 + 0.5 * skew * skew) / skew);
        }

        double gauss_value(double x, double amplitude, double fwhm, double median)
        {
            return PeakShapeModel.Evaluate(x, amplitude, median, fwhm, FwhmCalibration);
        }

        int gauss(double x, double amplitude, double fwhm, double median)
        {
            return Convert.ToInt32(gauss_value(x, amplitude, fwhm, median));
        }

        double exp_gauss_exp_value(double x, double amplitude, double median, double fwhm, double left, double right)
        {
            if (fwhm <= 0)
            {
                return 0.0;
            }
            double sigma = fwhm / 2.35482;
            double t = (x - median) / sigma;
            if (t > right) return amplitude * Math.Exp(0.5 * right * right - right * t);
            if (t > -left) return amplitude * Math.Exp(-0.5 * t * t);
            return amplitude * Math.Exp(0.5 * left * left + left * t);
        }

        int exp_gauss_exp(double x, double amplitude, double median, double fwhm, double left, double right)
        {
            return Convert.ToInt32(exp_gauss_exp_value(x, amplitude, median, fwhm, left, right));
        }

        public (int[], int, int, Color) GetPeak(Peak peak, EnergySpectrum continuum)
        {
            double amplitude = this.EnergySpectrum.Spectrum[peak.Channel] - continuum.Spectrum[peak.Channel];
            if (peak != null &&
                peak.PeakSearchOrigin == PeakSearchOrigin.RJMCMC &&
                peak.DeconvolutionInfo != null &&
                IsFinite(peak.DeconvolutionInfo.Amplitude) &&
                peak.DeconvolutionInfo.Amplitude > 0.0)
            {
                amplitude = peak.DeconvolutionInfo.Amplitude;
            }
            double fwhm = peak.FWHM;
            int median = peak.Channel;
            if (!IsFinite(fwhm) || fwhm <= 0.0)
            {
                fwhm = 0.0;
            }
            int peakType = FwhmCalibration.PeakType;
            if (!global::BecquerelMonitor.FwhmCalibration.IsSupportedPeakType(peakType))
            {
                peakType = global::BecquerelMonitor.FwhmCalibration.GaussianPeakType;
            }

            PseudoVoigtParameters voigtParameters = new PseudoVoigtParameters();
            if (peakType == global::BecquerelMonitor.FwhmCalibration.VoigtPeakType &&
                !PseudoVoigtProfile.TryCreate(fwhm, FwhmCalibration.VoigtSigma, FwhmCalibration.VoigtGamma, out voigtParameters))
            {
                peakType = global::BecquerelMonitor.FwhmCalibration.GaussianPeakType;
            }

            double leftHalfWidth = PeakShapeModel.GetLeftSupport(FwhmCalibration, fwhm);
            double rightHalfWidth = PeakShapeModel.GetRightSupport(FwhmCalibration, fwhm);

            int min_ch = median - (int)Math.Ceiling(leftHalfWidth);
            int max_ch = median + (int)Math.Ceiling(rightHalfWidth);
            if (min_ch < 0 )
            {
                min_ch = 0;
            }
            if (max_ch > this.EnergySpectrum.NumberOfChannels - 1)
            {
                max_ch = this.EnergySpectrum.NumberOfChannels - 1;
            }
            int[] retvalue = new int[this.EnergySpectrum.NumberOfChannels];
            int min_value = min_ch;
            int max_value = max_ch;
            bool left_side = true;
            for (int i = min_ch; i <= max_ch; i++)
            {
                if (peakType == global::BecquerelMonitor.FwhmCalibration.GaussianPeakType)
                {
                    retvalue[i] = gauss((double)i, (double)amplitude, fwhm, (double)median);
                }
                else if (peakType == global::BecquerelMonitor.FwhmCalibration.ExpGaussExpPeakType)
                {
                    retvalue[i] = exp_gauss_exp((double)i, (double)amplitude, (double)median, fwhm, FwhmCalibration.ExpGaussExpLeftTail, FwhmCalibration.ExpGaussExpRightTail);
                }
                else
                {
                    retvalue[i] = Convert.ToInt32(amplitude * PseudoVoigtProfile.RelativeValue(i - median, voigtParameters));
                }
                if (retvalue[i] == 0.0)
                {
                    if (left_side)
                    {
                        min_value = i;
                    } else
                    {
                        max_value = i;
                        break;
                    }
                } else
                {
                    left_side = false;
                }
            }
            if (peak.Nuclide != null)
            {
                return (retvalue, min_value, max_value, peak.Nuclide.NuclideColor.Color);
            }
            Color color = GlobalConfigManager.GetInstance().GlobalConfig.ColorConfig.UnknownPeakColor.Color;
            return (retvalue, min_value, max_value, color);
        } 

        public double FWHM(double x, FwhmCalibration calibration)
        {
            return calibration.ChannelToFwhm(x);
        }

        // https://doi.org/10.1016/j.nima.2017.12.064
        int[] SASNIP(int[] x, List<Peak> peaks = null, double coeff = 1.0, bool useLLS = false, bool decreasing = false)
        {
            double[] baseline = new double[x.Length];

            if (useLLS)
            {
                baseline = x.Select(i => LLS(i)).ToArray();
            }
            else
            {
                baseline = x.Select(i => Convert.ToDouble(i)).ToArray();
            }

            double[] r = BuildSnipRadius(baseline, peaks, coeff);

            int n = Convert.ToInt32(r.Max());

            if (n <= 0)
            {
                if (useLLS)
                {
                    baseline = baseline.Select(i => iLLS(i)).ToArray();
                }
            }
            else
            {
                int start = decreasing ? n : 1;
                int end = decreasing ? 1 : n;
                int step = decreasing ? -1 : 1;

                int len = x.Length;
                for (int p = start; decreasing ? p >= end : p <= end; p += step)
                {
                    double[] next = (double[])baseline.Clone();
                    // Итерируем все каналы и зеркально отражаем индексы соседей
                    // от границ, а не пропускаем первые/последние p каналов. Иначе
                    // крайний низкоэнергетический пик не клиппируется (симметричное
                    // окно уходит за канал 0) и остаётся в континууме. Отражение
                    // (в отличие от зажима к baseline[0]) не проваливает континуум
                    // у самого левого края, а ведёт его по подъёму спектра.
                    Parallel.For(0, len, i =>
                    {
                        if (p <= r[i])
                        {
                            int il = ReflectIndex(i - p, len);
                            int ir = ReflectIndex(i + p, len);
                            double b = (baseline[il] + baseline[ir]) / 2;
                            next[i] = Math.Min(baseline[i], b);
                        }
                    });
                    baseline = next;
                }

                if (useLLS)
                {
                    baseline = baseline.Select(i => iLLS(i)).ToArray();
                }
            }

            int[] baseline_arr = baseline.Select(i => Convert.ToInt32(i)).ToArray();
            return baseline_arr;
        }

        // Зеркальное отражение индекса от границ [0, len-1].
        // Для маленьких выходов за край даёт корректный симметричный сосед;
        // при экстремальных выходах схлопывается к ближайшей границе.
        static int ReflectIndex(int index, int len)
        {
            if (len <= 1)
            {
                return 0;
            }

            int last = len - 1;
            while (index < 0 || index > last)
            {
                if (index < 0)
                {
                    index = -index;
                }
                if (index > last)
                {
                    index = 2 * last - index;
                }
            }

            return index;
        }

        double[] BuildSnipRadius(double[] baseline, List<Peak> peaks, double coeff)
        {
            double[] radius = new double[baseline.Length];

            // FWHM-модель (sqrt от квадратичной по каналу) на низких каналах может
            // уходить под ноль и клампиться в 0. Тогда радиус SNIP становится нулевым,
            // условие (p <= r[i]) никогда не выполняется, и низкоэнергетические каналы
            // вообще не клиппируются — континуум "засасывается" в первый пик.
            // Ставим небольшой ненулевой пол. Он должен быть МАЛЕНЬКИМ: на низких
            // энергиях реальный FWHM в каналах невелик, а широкое окно на крутом
            // низкоэнергетическом склоне дотягивается до near-zero области и
            // "выгрызает" континуум почти в ноль вместо того, чтобы вести его
            // прямо под спектром.
            double fwhmFloor = LowEnergyFwhmFloor(baseline.Length);

            // В зоне, где модель FWHM невалидна, ширину окна берём из РЕАЛЬНО
            // обнаруженного пика, накрывающего этот канал (его измеренный FWHM),
            // а не из константного пола. Иначе широкий низкоэнергетический пик,
            // сидящий ниже валидной зоны модели (как главный пик у самого края
            // на грубо забиненных спектрах), недоклиппируется полом-5 и континуум
            // горбом лезет в него. Где модель валидна — поведение не меняется.
            double[] peakFwhmCover = BuildPeakFwhmCoverage(baseline.Length, peaks);
            bool[] fromPeakWidth = new bool[baseline.Length];

            for (int i = 0; i < baseline.Length; i++)
            {
                if (baseline[i] == 0)
                {
                    continue;
                }

                double fwhm = FWHM(i, this.FwhmCalibration);
                if (!IsFinite(fwhm) || fwhm < fwhmFloor)
                {
                    // Модель невалидна/вырождена. Берём ширину накрывающего пика,
                    // если он есть, иначе маленький пол.
                    fwhm = fwhmFloor;
                    if (peakFwhmCover != null && peakFwhmCover[i] > fwhm)
                    {
                        fwhm = peakFwhmCover[i];
                        fromPeakWidth[i] = true;
                    }
                }
                else if (peakFwhmCover != null && peakFwhmCover[i] > fwhm)
                {
                    // Модель валидна, но реально обнаруженный пик ШИРЕ модели.
                    // Немного расширяем окно в сторону измеренной ширины (с запасом),
                    // но не более чем в PeakWidthWidenFactor раз — чтобы, если пик
                    // окажется шире модели, континуум не "засасывался" в него, и при
                    // этом окно не раздувалось и не выгрызало континуум.
                    fwhm = Math.Min(peakFwhmCover[i], fwhm * this.peakWidthWidenFactor);
                }

                radius[i] = coeff * fwhm;
            }

            if (peaks == null || peaks.Count == 0)
            {
                return radius;
            }

            int[] multiplicity = BuildPeakMultiplicityMap(baseline.Length, peaks);
            for (int i = 0; i < radius.Length; i++)
            {
                // Множитель мультиплета НЕ применяем там, где радиус уже взят из
                // измеренной ширины пика: она сама учитывает реальную ширину, а
                // домножение на кратность раздувает окно и "выгрызает" континуум.
                if (multiplicity[i] > 1 && radius[i] > 0.0 && !fromPeakWidth[i])
                {
                    radius[i] *= multiplicity[i];
                }
            }

            return radius;
        }

        // Карта покрытия каналов обнаруженными пиками: для каждого канала —
        // максимальный FWHM пика, поддержка которого (±FWHM*SupportFactor)
        // накрывает канал. 0, если ни один пик не накрывает.
        double[] BuildPeakFwhmCoverage(int channelsCount, List<Peak> peaks)
        {
            double[] cover = new double[channelsCount];
            if (peaks == null || peaks.Count == 0)
            {
                return cover;
            }

            const double SupportFactor = 1.5;
            foreach (Peak peak in peaks)
            {
                if (peak == null || !IsFinite(peak.FWHM) || peak.FWHM <= 0.0)
                {
                    continue;
                }

                int halfWidth = Math.Max(1, Convert.ToInt32(Math.Ceiling(peak.FWHM * SupportFactor)));
                int left = Math.Max(0, peak.Channel - halfWidth);
                int right = Math.Min(channelsCount - 1, peak.Channel + halfWidth);
                for (int i = left; i <= right; i++)
                {
                    if (peak.FWHM > cover[i])
                    {
                        cover[i] = peak.FWHM;
                    }
                }
            }

            return cover;
        }

        // Минимальный радиус SNIP (в каналах) для низких энергий, где FWHM-модель
        // вырождается в 0. Держим маленьким: на низких энергиях реальное разрешение
        // в каналах мало, а слишком широкое окно "выгрызает" континуум на крутом
        // склоне. Значение подобрано так, чтобы континуум шёл прямо под спектром на
        // низкоэнергетическом подъёме; при этом резкие пики клиппируются реальной
        // (большей) моделью FWHM выше этой зоны.
        const double MinLowEnergyFwhmChannels = 5.0;

        // Насколько (максимум) можно расширить окно SNIP относительно модельного
        // FWHM в сторону измеренной ширины пика, когда пик реально шире модели.
        // Запас на недооценку ширины моделью; 1.2 = до +20%. Значение берётся из
        // FWHMPeakDetectionMethodConfig.PeakWidthWidenFactor (см. Continuum);
        // константа — только фолбэк по умолчанию.
        const double DefaultPeakWidthWidenFactor = 1.2;

        double peakWidthWidenFactor = DefaultPeakWidthWidenFactor;

        double LowEnergyFwhmFloor(int channelsCount)
        {
            if (channelsCount <= 0)
            {
                return 0.0;
            }

            double floor = MinLowEnergyFwhmChannels;

            // Не превышаем реальное разрешение прибора в нижней калибровочной точке:
            // если детектор действительно очень узкий, берём меньшее значение.
            if (this.FwhmCalibration != null)
            {
                List<CalibrationPeak> calPeaks = this.FwhmCalibration.CalibrationPeaks;
                if (calPeaks != null && calPeaks.Count > 0)
                {
                    int anchorChannel = int.MaxValue;
                    foreach (CalibrationPeak cp in calPeaks)
                    {
                        if (cp != null && cp.Channel >= 0 && cp.Channel < anchorChannel)
                        {
                            anchorChannel = cp.Channel;
                        }
                    }
                    if (anchorChannel != int.MaxValue)
                    {
                        double anchorFwhm = FWHM(anchorChannel, this.FwhmCalibration);
                        if (IsFinite(anchorFwhm) && anchorFwhm > 0.0 && anchorFwhm < floor)
                        {
                            floor = anchorFwhm;
                        }
                    }
                }
            }

            return floor;
        }

        int[] BuildPeakMultiplicityMap(int channelsCount, List<Peak> peaks)
        {
            const double SupportFactor = 1.5;

            int[] delta = new int[channelsCount + 1];
            foreach (Peak peak in peaks)
            {
                if (peak == null || !IsFinite(peak.FWHM) || peak.FWHM <= 0.0)
                {
                    continue;
                }

                int halfWidth = Math.Max(1, Convert.ToInt32(Math.Ceiling(peak.FWHM * SupportFactor)));
                int left = Math.Max(0, peak.Channel - halfWidth);
                int right = Math.Min(channelsCount - 1, peak.Channel + halfWidth);
                delta[left]++;
                if (right + 1 < delta.Length)
                {
                    delta[right + 1]--;
                }
            }

            int[] multiplicity = new int[channelsCount];
            int current = 0;
            for (int i = 0; i < channelsCount; i++)
            {
                current += delta[i];
                multiplicity[i] = current;
            }

            return multiplicity;
        }

        double LLS(double x)
        {
            return Math.Log(Math.Log(Math.Sqrt(x + 1) + 1) + 1);

        }

        double iLLS(double x)
        {
            return Math.Pow(Math.Exp(Math.Exp(x) - 1) - 1, 2) - 1;
        }

        static int[] ConcatArray(int[] spectrum, int newChanNumber)
        {
            int[] result = new int[newChanNumber];
            int multiplier = spectrum.Length / newChanNumber;
            for (int i = 0; i < result.Length; i++)
            {
                for (int j = 0; j < multiplier; j++)
                {
                    result[i] += spectrum[multiplier * i + j];
                }
            }
            return result;
        }

        public static EnergySpectrum CutoffSpectrumChannels(EnergySpectrum energySpectrum, int newChan)
        {
            EnergySpectrum newSpectrum = new EnergySpectrum(energySpectrum.ChannelPitch, newChan);
            PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration((PolynomialEnergyCalibration)energySpectrum.EnergyCalibration);
            newSpectrum.EnergyCalibration = calibration;
            newSpectrum.NumberOfChannels = newChan;
            Array.Copy(energySpectrum.Spectrum, newSpectrum.Spectrum, newChan);
            newSpectrum.MeasurementTime = energySpectrum.MeasurementTime;
            newSpectrum.LiveTime = energySpectrum.LiveTime;
            newSpectrum.TotalPulseCount = newSpectrum.Spectrum.Sum(x => (long)x);
            newSpectrum.ValidPulseCount = newSpectrum.TotalPulseCount;
            newSpectrum.NumberOfSamples = energySpectrum.NumberOfSamples;
            return newSpectrum;
        }

        public static EnergySpectrum CutoffSpectrumEnergy(EnergySpectrum energySpectrum, double energyVal)
        {
            PolynomialEnergyCalibration calibration = (PolynomialEnergyCalibration)energySpectrum.EnergyCalibration;
            int newChan = Convert.ToInt32(calibration.EnergyToChannel(energyVal, maxCh: energySpectrum.NumberOfChannels));
            return CutoffSpectrumChannels(energySpectrum, newChan);
        }

        public static EnergySpectrum Cutoff(EnergySpectrum energySpectrum, bool isEnergy, double energyVal = 0.0, int channel = 0)
        {
            if (isEnergy && energyVal > 0.0)
            {
                return CutoffSpectrumEnergy(energySpectrum, energyVal);
            }
            if (!isEnergy && channel > 0)
            {
                return CutoffSpectrumChannels(energySpectrum, channel);
            }
            return null;
        }

        public static EnergySpectrum ConcatSpectrum(EnergySpectrum energySpectrum, int newChan)
        {
            EnergySpectrum newSpectrum = new EnergySpectrum(energySpectrum.ChannelPitch, newChan);
            PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration((PolynomialEnergyCalibration)energySpectrum.EnergyCalibration);
            double mul = (double)energySpectrum.NumberOfChannels / (double)newChan;
            for (int i = 0; i < calibration.Coefficients.Length; i++)
            {
                calibration.Coefficients[i] = Math.Pow(mul, i) * calibration.Coefficients[i];
            }
            newSpectrum.EnergyCalibration = calibration;
            newSpectrum.NumberOfChannels = newChan;
            newSpectrum.Spectrum = ConcatArray(energySpectrum.Spectrum, newChan);
            newSpectrum.MeasurementTime = energySpectrum.MeasurementTime;
            newSpectrum.LiveTime = energySpectrum.LiveTime;
            newSpectrum.TotalPulseCount = newSpectrum.Spectrum.Sum(x => (long)x);
            double newValidPulseCount = (double)newSpectrum.TotalPulseCount * (double)energySpectrum.ValidPulseCount / (double)energySpectrum.TotalPulseCount;
            newSpectrum.ValidPulseCount = (long)newValidPulseCount;
            newSpectrum.NumberOfSamples = energySpectrum.NumberOfSamples;
            return newSpectrum;
        }

        public static EnergySpectrum RestoreSpectrum(EnergySpectrum energySpectrum, int newChan)
        {
            EnergySpectrum newSpectrum = new EnergySpectrum(energySpectrum.ChannelPitch, newChan);
            PolynomialEnergyCalibration calibration = new PolynomialEnergyCalibration((PolynomialEnergyCalibration)energySpectrum.EnergyCalibration);
            double mul = (double)energySpectrum.NumberOfChannels / (double)newChan;
            for (int i = 0; i < calibration.Coefficients.Length; i++)
            {
                calibration.Coefficients[i] = Math.Pow(mul, i) * calibration.Coefficients[i];
            }
            newSpectrum.EnergyCalibration = calibration;
            newSpectrum.NumberOfChannels = newChan;
            newSpectrum.Spectrum = RestoreArray(energySpectrum.Spectrum, newChan);
            newSpectrum.MeasurementTime = energySpectrum.MeasurementTime;
            newSpectrum.LiveTime = energySpectrum.LiveTime;
            newSpectrum.TotalPulseCount = newSpectrum.Spectrum.Sum(x => (long)x);
            double newValidPulseCount = (double)newSpectrum.TotalPulseCount * (double)energySpectrum.ValidPulseCount / (double)energySpectrum.TotalPulseCount;
            newSpectrum.ValidPulseCount = (long)newValidPulseCount;
            newSpectrum.NumberOfSamples = energySpectrum.NumberOfSamples;
            return newSpectrum;
        }

        static int[] RestoreArray(int[] spectrum, int newChanNumber)
        {
            int[] result = new int[newChanNumber];
            int multiplier = newChanNumber / spectrum.Length;
            for (int i = 0; i < spectrum.Length - 4; i++)
            {
                double[] x_v = { i * multiplier, (i + 1) * multiplier, (i + 2) * multiplier, (i + 3) * multiplier, (i + 4) * multiplier };
                double[] y_v = { spectrum[i] / multiplier, spectrum[i + 1] / multiplier, spectrum[i + 2] / multiplier, spectrum[i + 3] / multiplier, spectrum[i + 4] / multiplier };

                double[] poly = Fit.Polynomial(x_v, y_v, 3);

                for (int j = 0; j < 4*multiplier; j++)
                {
                    result[multiplier * i + j] = Convert.ToInt32(poly[0] + poly[1] * (i * multiplier + j) + poly[2] * Math.Pow(i * multiplier + j, 2) + poly[3] * Math.Pow(i * multiplier + j, 3));
                    if (result[multiplier * i + j] < 0)
                    {
                        result[multiplier * i + j] = 0;
                    }
                }
            }
            return result;
        }

        public double[] WMA2(double[] spectrum, int numberOfWMADataPoints, int countlimit = 100, bool progressive = false)
        {
            double[] result = new double[spectrum.Length];
            if (progressive)
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    double pointsnum = GetProgressivePointsNum(i, spectrum.Length, numberOfWMADataPoints);
                    if (pointsnum < 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    int window_size = GetWindowSize(spectrum[i], countlimit, pointsnum);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    result[i] = GetWMAPointValue(spectrum, i, window_size);
                });
            }
            else
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    int window_size = GetWindowSize(spectrum[i], countlimit, numberOfWMADataPoints);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                    }
                    else
                    {
                        result[i] = GetWMAPointValue(spectrum, i, window_size);
                    }
                });
            }
            
            return result;
        }

        public int[] WMA(int[] spectrum, int numberOfWMADataPoints, int countlimit = 100, bool progressive = false)
        {
            int[] result = new int[spectrum.Length];
            if (progressive)
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    double pointsnum = GetProgressivePointsNum(i, spectrum.Length, numberOfWMADataPoints);
                    if (pointsnum < 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    int window_size = GetWindowSize(spectrum[i], countlimit, pointsnum);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    result[i] = Convert.ToInt32(GetWMAPointValue(spectrum, i, window_size));
                });
            }
            else
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    int window_size = GetWindowSize(spectrum[i], countlimit, numberOfWMADataPoints);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                    }
                    else
                    {
                        result[i] = Convert.ToInt32(GetWMAPointValue(spectrum, i, window_size));
                    }
                });
            }

            return result;
        }

        public double[] SMA2(double[] spectrum, int numberOfSMADataPoints, int countlimit = 100, bool progressive = true)
        {
            double[] result = new double[spectrum.Length];
            if (progressive)
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    double pointsnum = GetProgressivePointsNum(i, spectrum.Length, numberOfSMADataPoints);
                    if (pointsnum < 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    int window_size = GetWindowSize(spectrum[i], countlimit, pointsnum);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    double new_count = GetSMAPointValue(spectrum, i, window_size);
                    result[i] = new_count / window_size;
                });
            }
            else
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    int window_size = GetWindowSize(spectrum[i], countlimit, numberOfSMADataPoints);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                    }
                    else
                    {
                        double new_count = GetSMAPointValue(spectrum, i, window_size);
                        result[i] = new_count / window_size;
                    }
                });
            }

            return result;
        }

        public int[] SMA(int[] spectrum, int numberOfSMADataPoints, int countlimit = 100, bool progressive = false)
        {
            int[] result = new int[spectrum.Length];
            if (progressive)
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    double pointsnum = GetProgressivePointsNum(i, spectrum.Length, numberOfSMADataPoints);
                    if (pointsnum < 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    int window_size = GetWindowSize(spectrum[i], countlimit, pointsnum);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                        return;
                    }

                    double new_count = GetSMAPointValue(spectrum, i, window_size);
                    result[i] = Convert.ToInt32(new_count / window_size);
                });
            }
            else
            {
                Parallel.For(0, spectrum.Length, i =>
                {
                    int window_size = GetWindowSize(spectrum[i], countlimit, numberOfSMADataPoints);
                    if (window_size == 1)
                    {
                        result[i] = spectrum[i];
                    }
                    else
                    {
                        double new_count = GetSMAPointValue(spectrum, i, window_size);
                        result[i] = Convert.ToInt32(new_count / window_size);
                    }
                });
            }
            
            return result;
        }

        private static double GetSMAPointValue<T>(T[] spectrum, int i, int window_size)
        {
            double new_count = 0.0;
            for (int j = i - window_size / 2; j < i - window_size / 2 + window_size; j++)
            {
                int ch = j;
                if (ch < 0)
                {
                    ch = 0;
                }
                else if (ch >= spectrum.Length)
                {
                    ch = spectrum.Length - 1;
                }
                new_count += Convert.ToDouble(spectrum[ch]);
            }

            return new_count;
        }

        private static double GetWMAPointValue<T>(T[] spectrum, int i, int window_size)
        {
            double part = 0.0;
            double total = 0.0;
            for (int j = i - window_size / 2; j < i - window_size / 2 + window_size; j++)
            {
                int ch = j;
                if (ch < 0)
                {
                    ch = 0;
                }
                else if (ch >= spectrum.Length)
                {
                    ch = spectrum.Length - 1;
                }
                double weight = (double)(window_size / 2 + 1 - Math.Abs(i - ch));
                part += Convert.ToDouble(spectrum[ch]) * weight;
                total += weight;
            }
            double value = part / total;
            return value;
        }

        private static double GetProgressivePointsNum(int channelIndex, int channelCount, int numberOfDataPoints)
        {
            return numberOfDataPoints * Math.Sqrt(channelIndex / (channelCount / 2.0));
        }

        private static int GetWindowSize(double channelValue, int countlimit, double pointsNum)
        {
            int window_size = 1;
            if (channelValue <= countlimit)
            {
                window_size = Convert.ToInt32(channelValue * (1 - pointsNum) / countlimit + pointsNum);
            }

            return window_size;
        }

        public void Dispose()
        {
            this.MainSpectrum = null;
            this.EnergySpectrum = null;
            GC.Collect();
        }

        DocEnergySpectrum MainSpectrum;

        EnergySpectrum EnergySpectrum;

        FwhmCalibration FwhmCalibration;
    }
}
