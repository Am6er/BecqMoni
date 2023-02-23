using BecquerelMonitor.Properties;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Complex;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Runtime.ExceptionServices;
using System.Security.Permissions;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

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

        public SpectrumAriphmetics(FWHMPeakDetectionMethodConfig fWHMPeakDetectionMethodConfig, EnergySpectrum energySpectrum)
        {
            this.FWHMPeakDetectionMethodConfig = fWHMPeakDetectionMethodConfig;
            this.EnergySpectrum = energySpectrum.Clone();
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
                        double getChannel = Math.Round(CombinedSpectrumEnergyCalibration.EnergyToChannel(MainSpectrumEnergyCalibration.ChannelToEnergy(i)));
                        if (getChannel >= 0 && getChannel < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
                        {
                            this.MainSpectrum.ActiveResultData.EnergySpectrum.Spectrum[i] += docenergySpectrum.ActiveResultData.EnergySpectrum.Spectrum[Convert.ToInt32(getChannel)];
                        }
                    }
                }

                this.MainSpectrum.ActiveResultData.EnergySpectrum.MeasurementTime += docenergySpectrum.ActiveResultData.EnergySpectrum.MeasurementTime;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.TotalPulseCount += docenergySpectrum.ActiveResultData.EnergySpectrum.TotalPulseCount;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.ValidPulseCount += docenergySpectrum.ActiveResultData.EnergySpectrum.ValidPulseCount;

                this.MainSpectrum.ActiveResultData.PresetTime += docenergySpectrum.ActiveResultData.PresetTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.PresetTime += docenergySpectrum.ActiveResultData.ResultDataStatus.PresetTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.ElapsedTime += docenergySpectrum.ActiveResultData.ResultDataStatus.ElapsedTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.TotalTime += docenergySpectrum.ActiveResultData.ResultDataStatus.TotalTime;
            } else
            {
                System.Windows.Forms.MessageBox.Show(Resources.CombineIncorrectChannels);
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
                    substractedEnergySpectrum.TotalPulseCount += substractedEnergySpectrum.Spectrum[i];
                });
                substractedEnergySpectrum.ValidPulseCount = substractedEnergySpectrum.TotalPulseCount;
            } else
            {
                Parallel.For(0, substractedEnergySpectrum.NumberOfChannels, i =>
                {
                    double enrg = this.EnergySpectrum.EnergyCalibration.ChannelToEnergy(i);
                    int bgchan = Convert.ToInt32(bgenergySpectrum.EnergyCalibration.EnergyToChannel(enrg));
                    if (bgchan > 0 && bgchan < bgenergySpectrum.NumberOfChannels)
                    {
                        substractedEnergySpectrum.Spectrum[i] = Convert.ToInt32(this.EnergySpectrum.Spectrum[i] - norm_coeff * bgenergySpectrum.Spectrum[bgchan]);
                        if (substractedEnergySpectrum.Spectrum[i] < 0)
                        {
                            substractedEnergySpectrum.Spectrum[i] = 0;
                        }
                    }
                    substractedEnergySpectrum.TotalPulseCount += substractedEnergySpectrum.Spectrum[i];
                });
                substractedEnergySpectrum.ValidPulseCount = substractedEnergySpectrum.TotalPulseCount;
            }
            return substractedEnergySpectrum;
        }

        public EnergySpectrum Continuum()
        {
            EnergySpectrum continuum = this.EnergySpectrum.Clone();
            continuum.Spectrum = SASNIP(this.EnergySpectrum.Spectrum, coeff: 1.5);
            for (int i = 0; i<continuum.NumberOfChannels; i++)
            {
                if (continuum.Spectrum[i] > this.EnergySpectrum.Spectrum[i])
                {
                    continuum.Spectrum[i] = this.EnergySpectrum.Spectrum[i];
                }
            }
            return continuum;
        }

        public EnergySpectrum SubtractPeak(Peak peak, EnergySpectrum energySpectrum)
        {
            EnergySpectrum result = energySpectrum.Clone();
            (int[] peakspectrum, int min_val, int max_val, Color peakColor) = GetPeak(peak, result, true);
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

        int gauss(int x, int amplitude, int fwhm, int median)
        {
            double sigma = fwhm / 2.35482;
            return (int)(amplitude * Math.Exp(-Math.Pow(x - median,2)/(2*Math.Pow(sigma, 2))));
        }

        public (int[], int, int, Color) GetPeak(Peak peak, EnergySpectrum continuum, bool smooth)
        {
            int amplitude;
            int[] SMASpectrum = SMA(this.EnergySpectrum.Spectrum, 3, countlimit: 100);
            if (smooth && SMASpectrum[peak.Channel] < this.EnergySpectrum.Spectrum[peak.Channel])
            {
                amplitude = SMASpectrum[peak.Channel] - continuum.Spectrum[peak.Channel];
            } else
            {
                amplitude = this.EnergySpectrum.Spectrum[peak.Channel] - continuum.Spectrum[peak.Channel];
            }
            int fwhm = (int)peak.FWHM;
            int median = peak.Channel;
            int interval = 3 * fwhm;
            int min_ch = median - interval;
            int max_ch = median + interval;
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
                retvalue[i] = gauss(i, amplitude, fwhm, median);
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

        public double FWHM(double x, FWHMPeakDetectionMethodConfig cfg)
        {
            double f0 = cfg.FWHM_AT_0;
            double f1 = cfg.Width_Fwhm;
            double x1 = cfg.Ch_Fwhm;
            double fwhm_sqr = Math.Pow(f0, 2) + (Math.Pow(f1, 2) - Math.Pow(f0, 2)) * (x / x1);
            return Math.Sqrt(fwhm_sqr);
        }

        // https://doi.org/10.1016/j.nima.2017.12.064
        int[] SASNIP(int[] x, double coeff = 1.0, bool useLLS = false, bool decreasing = true)
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

            //FWHM from config
            double[] r = new double[x.Length];
            r = r.Select((i, iter) => coeff * (FWHM(iter, this.FWHMPeakDetectionMethodConfig))).ToArray();

            int n = (int)r.Max();

            int[] seq = new int[n];
            if (decreasing)
            {
                seq = seq.Select((i, iter) => n - iter).ToArray();
            }
            else
            {
                seq = seq.Select((i, iter) => iter).ToArray();
            }

            double[] tmp = baseline;

            foreach (int p in seq)
            {
                Parallel.For(p, x.Length - p, i =>
                {
                    double b = 0;
                    if (p <= r[i])
                    {
                        b = (baseline[i - p] + baseline[i + p]) / 2;
                        tmp[i] = Math.Min(baseline[i], b);
                    }
                    else
                    {
                        tmp[i] = baseline[i];
                    }
                });
                baseline = tmp;
            }

            if (useLLS)
            {
                baseline = baseline.Select(i => iLLS(i)).ToArray();
            }

            int[] baseline_arr = baseline.Select(i => (int)i).ToArray();

            int baseline_max = 0;
            int baseline_max_i = 0;
            for(int i = 1; i < baseline_arr.Length/2; i++)
            {
                if (baseline_arr[i] > baseline_max)
                {
                    baseline_max = baseline_arr[i];
                    baseline_max_i = i;
                }
            }

            for (int i = 0; i < baseline_max_i; i++)
            {
                baseline_arr[i] = baseline_max;
            }

            return baseline_arr;
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
            newSpectrum.TotalPulseCount = newSpectrum.Spectrum.Sum();
            newSpectrum.ValidPulseCount = newSpectrum.TotalPulseCount;
            newSpectrum.NumberOfSamples = energySpectrum.NumberOfSamples;
            return newSpectrum;
        }

        public static EnergySpectrum CutoffSpectrumEnergy(EnergySpectrum energySpectrum, double energyVal)
        {
            PolynomialEnergyCalibration calibration = (PolynomialEnergyCalibration)energySpectrum.EnergyCalibration;
            int newChan = (int)calibration.EnergyToChannel(energyVal);
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
            newSpectrum.TotalPulseCount = newSpectrum.Spectrum.Sum();
            newSpectrum.ValidPulseCount = newSpectrum.TotalPulseCount;
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
            newSpectrum.TotalPulseCount = newSpectrum.Spectrum.Sum();
            newSpectrum.ValidPulseCount = newSpectrum.TotalPulseCount;
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

        public double[] WMA2(int[] spectrum, int numberOfWMADataPoints, int countlimit = 100)
        {
            double[] result = new double[spectrum.Length];
            //for (int i = 0; i < spectrum.Length; i++)
            Parallel.For(0, spectrum.Length, i =>
            {
                int window_size = 1;
                if (spectrum[i] <= countlimit)
                {
                    window_size = (int)Math.Round((double)spectrum[i] * (1 - (double)numberOfWMADataPoints) / (double)countlimit + (double)numberOfWMADataPoints);
                }
                if (window_size == 1)
                {
                    result[i] = spectrum[i];
                }
                else
                {
                    double part = 0.0;
                    double total = 0.0;
                    for (int j = i - numberOfWMADataPoints / 2; j < i - numberOfWMADataPoints / 2 + numberOfWMADataPoints; j++)
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
                        double weight = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(i - ch));
                        part += (double)spectrum[ch] * weight;
                        total += weight;
                    }
                    result[i] = part / total;
                }
            });
            return result;
        }

        public int[] WMA(int[] spectrum, int numberOfWMADataPoints, int countlimit = 100)
        {
            int[] result = new int[spectrum.Length];
            //for (int num14 = 0; num14 < spectrum.Length; num14++)
            Parallel.For(0, spectrum.Length, i =>
            {
                int window_size = 1;
                if (spectrum[i] <= countlimit)
                {
                    window_size = (int)Math.Round((double)spectrum[i] * (1 - (double)numberOfWMADataPoints) / (double)countlimit + (double)numberOfWMADataPoints);
                }
                if (window_size == 1)
                {
                    result[i] = spectrum[i];
                }
                else
                {
                    double part = 0.0;
                    double total = 0.0;
                    for (int j = i - numberOfWMADataPoints / 2; j < i - numberOfWMADataPoints / 2 + numberOfWMADataPoints; j++)
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
                        double weight = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(i - ch));
                        part += (double)spectrum[ch] * weight;
                        total += weight;
                    }
                    result[i] = (int)(part / total);
                }
            });
            return result;
        }

        public double[] SMA2(int[] spectrum, int numberOfSMADataPoints, int countlimit = 100)
        {
            double[] result = new double[spectrum.Length];
            //for (int i = 0; i < spectrum.Length; i++)
            Parallel.For(0, spectrum.Length, i =>
            {
                double new_count = 0.0;
                int window_size = 1;
                if (spectrum[i] <= countlimit)
                {
                    window_size = (int)Math.Round((double)spectrum[i] * (1 - (double)numberOfSMADataPoints) / (double)countlimit + (double)numberOfSMADataPoints);
                }
                if (window_size == 1)
                {
                    result[i] = spectrum[i];
                }
                else
                {
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
                        new_count += (double)spectrum[ch];
                    }
                    result[i] = new_count / (double)window_size;
                }
            });
            return result;
        }

        public int[] SMA(int[] spectrum, int numberOfSMADataPoints, int countlimit = 100)
        {
            int[] result = new int[spectrum.Length];
            //for (int num10 = 0; num10 < spectrum.Length; num10++)
            Parallel.For(0, spectrum.Length, i =>
            {
                double new_count = 0.0;
                int window_size = 1;
                if (spectrum[i] <= countlimit)
                {
                    window_size = (int)Math.Round((double)spectrum[i] * (1 - (double)numberOfSMADataPoints) / (double)countlimit + (double)numberOfSMADataPoints);
                }
                if (window_size == 1)
                {
                    result[i] = spectrum[i];
                }
                else
                {
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
                        new_count += (double)spectrum[ch];
                    }
                    result[i] = (int)(new_count / (double)window_size);
                }
            });
            return result;
        }

        public void Dispose()
        {
            this.MainSpectrum = null;
            this.EnergySpectrum = null;
            GC.Collect();
        }

        DocEnergySpectrum MainSpectrum;

        EnergySpectrum EnergySpectrum;

        FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig;
    }
}
