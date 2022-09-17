using BecquerelMonitor.Properties;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Complex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Runtime.ExceptionServices;
using System.Security.Permissions;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms.VisualStyles;

namespace BecquerelMonitor.Utils
{
    public class SpectrumAriphmetics
    {
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
            this.EnergySpectrum = energySpectrum;
        }

        public DocEnergySpectrum CombineWith(DocEnergySpectrum docenergySpectrum)
        {
            if (this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels == docenergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
            {
                if (docenergySpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration.Equals(this.MainSpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration))
                {
                    docenergySpectrum.ActiveResultData.EnergySpectrum.Spectrum[docenergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels - 1] = 0;
                    for (int i = 0; i < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels; i++)
                    {
                        this.MainSpectrum.ActiveResultData.EnergySpectrum.Spectrum[i] += docenergySpectrum.ActiveResultData.EnergySpectrum.Spectrum[i];
                    }
                }
                else
                {
                    PolynomialEnergyCalibration MainSpectrumEnergyCalibration = (PolynomialEnergyCalibration)this.MainSpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration;
                    PolynomialEnergyCalibration CombinedSpectrumEnergyCalibration = (PolynomialEnergyCalibration)docenergySpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration;
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
            if (this.EnergySpectrum.NumberOfChannels < 1000)
            {
                continuum.Spectrum = SASNIP(this.EnergySpectrum.Spectrum, 2);
            } else
            {
                continuum.Spectrum = SASNIP(SMA(this.EnergySpectrum.Spectrum, 20), 2);
            }
            return continuum;
        }

        public EnergySpectrum SubtractPeak(Peak peak)
        {
            EnergySpectrum result = this.EnergySpectrum.Clone();
            int amplitude = result.Spectrum[peak.Channel];
            int median = peak.Channel;
            int fwhm = (int)(peak.FWHM);
            for (int i = median - fwhm; i < median + fwhm; i++)
            {
                result.Spectrum[i] -= gauss(i, amplitude, 2*fwhm, median);
            }
            return result;
        }

        int gauss(int x, int amplitude, int fwhm, int median)
        {

            return (int)(amplitude * Math.Exp(-Math.Pow(x - median,2)/(2*Math.Pow(fwhm/2, 2))));
        }

        public double fwhm(double x, FWHMPeakDetectionMethodConfig cfg)
        {
            double f0 = cfg.FWHM_AT_0;
            double f1 = cfg.Width_Fwhm;
            double x1 = cfg.Ch_Fwhm;
            double fwhm_sqr = Math.Pow(f0, 2) + (Math.Pow(f1, 2) - Math.Pow(f0, 2)) * (x / x1);
            return Math.Sqrt(fwhm_sqr);
        }

        // https://doi.org/10.1016/j.nima.2017.12.064
        int[] SASNIP(int[] x, double coeff = 1.3, bool useLLS = false, bool decreasing = true)
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
            r = r.Select((i, iter) => coeff * (fwhm(iter, this.FWHMPeakDetectionMethodConfig))).ToArray();

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
                    double a = baseline[i];
                    double b = 0;
                    if (p <= r[i])
                    {
                        b = (baseline[i - p] + baseline[i + p]) / 2;
                    }
                    else
                    {
                        b = baseline[i];
                    }

                    tmp[i] = Math.Min(a, b);
                });
                baseline = tmp;
            }

            if (useLLS)
            {
                baseline = baseline.Select(i => iLLS(i)).ToArray();
            }

            return baseline.Select(i => (int)i).ToArray();
        }

        //https://github.com/crp2a/gamma/blob/master/R/baseline_snip.R
        int[] SNIP(int[] x, bool useLLS = false, bool decreasing = true, int n = 100, int correction = 0)
        {
            double[] baseline = new double[x.Length];

            if (useLLS)
            {
                baseline = x.Select(i => LLS(i)).ToArray();
            } else
            {
                baseline = x.Select(i => Convert.ToDouble(i)).ToArray();
            }

            int[] seq = new int[n];
            if (decreasing)
            {
                seq = seq.Select((i, iter) => n - iter).ToArray();
            } else
            {
                seq = seq.Select((i, iter) => iter).ToArray();
            }

            double[] tmp = baseline;

            foreach (int p in seq)
            {
                for (int i = p; i < x.Length - p; i++)
                {
                    double a = baseline[i];
                    double b = 0;
                    switch (correction)
                    {
                        case 0:
                            b = (baseline[i - p] + baseline[i + p]) / 2;
                            break;
                        case 1:
                            b = (-(baseline[i - p] + baseline[i + p]) 
                                + 4 * (baseline[i - p / 2] + baseline[i + p / 2])) / 6;
                            break;
                        case 2:
                            b = (baseline[i - p] + baseline[i + p] 
                                - 6 * (baseline[i - 2*p/3] + baseline[i + 2*p/3]) 
                                + 15 * (baseline[i - p/3] + baseline[i + p/3])) / 20;
                            break;
                        case 3:
                            b = (-(baseline[i - p] + baseline[i + p]) 
                                + 8 * (baseline[i - 3 * p / 4] + baseline[i + 3 * p / 4]) 
                                - 28 * (baseline[i - p / 2] + baseline[i + p / 2]) 
                                + 56 * (baseline[i - p / 4] + baseline[i + p / 4])) / 70;
                            break;
                    }
                    tmp[i] = Math.Min(a, b);
                }
                baseline = tmp;
            }

            if (useLLS)
            {
                baseline = baseline.Select(i => iLLS(i)).ToArray();
            }

            return baseline.Select(i => (int)i).ToArray();
        }

        double LLS(double x)
        {
            return Math.Log(Math.Log(Math.Sqrt(x + 1) + 1) + 1);

        }

        double iLLS(double x)
        {
            return Math.Pow(Math.Exp(Math.Exp(x) - 1) - 1, 2) - 1;
        }

        int[] ConcatSpectrum(int[] spectrum, int newChanNumber)
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

        int[] RestoreSpectrum(int[] spectrum, int newChanNumber)
        {
            int[] result = new int[newChanNumber];
            int multiplier = newChanNumber / spectrum.Length;
            for (int i = 0; i < spectrum.Length - 1; i++)
            {
                double[] x_v = { i * multiplier, (i + 1) * multiplier };
                double[] y_v = { spectrum[i] / multiplier, spectrum[i + 1] / multiplier };

                double[] poly = Fit.Polynomial(x_v, y_v, 1);

                for (int j = 0; j < multiplier; j++)
                {
                    result[multiplier * i + j] = Convert.ToInt32(poly[0] + poly[1] * (i * multiplier + j));
                }
            }
            return result;
        }

        public double[] WMA(double[] spectrum, int numberOfWMADataPoints)
        {
            double[] result = new double[spectrum.Length];
            for (int num14 = 0; num14 < spectrum.Length; num14++)
            {
                double num15 = 0.0;
                double num16 = 0.0;
                for (int num17 = num14 - numberOfWMADataPoints / 2; num17 < num14 - numberOfWMADataPoints / 2 + numberOfWMADataPoints; num17++)
                {
                    int num18 = num17;
                    if (num18 < 0)
                    {
                        num18 = 0;
                    }
                    else if (num17 >= spectrum.Length)
                    {
                        num18 = spectrum.Length - 1;
                    }
                    double num19 = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(num14 - num17));
                    num15 += (double)spectrum[num18] * num19;
                    num16 += num19;
                }
                result[num14] = num15 / num16;
            }
            return result;
        }

        public int[] WMA(int[] spectrum, int numberOfWMADataPoints)
        {
            int[] result = new int[spectrum.Length];
            for (int num14 = 0; num14 < spectrum.Length; num14++)
            {
                double num15 = 0.0;
                double num16 = 0.0;
                for (int num17 = num14 - numberOfWMADataPoints / 2; num17 < num14 - numberOfWMADataPoints / 2 + numberOfWMADataPoints; num17++)
                {
                    int num18 = num17;
                    if (num18 < 0)
                    {
                        num18 = 0;
                    }
                    else if (num17 >= spectrum.Length)
                    {
                        num18 = spectrum.Length - 1;
                    }
                    double num19 = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(num14 - num17));
                    num15 += (double)spectrum[num18] * num19;
                    num16 += num19;
                }
                result[num14] = (int)(num15 / num16);
            }
            return result;
        }

        public double[] SMA(double[] spectrum, int numberOfSMADataPoints)
        {
            double[] result = new double[spectrum.Length];
            for (int num10 = 0; num10 < spectrum.Length; num10++)
            {
                double num11 = 0.0;
                for (int num12 = num10 - numberOfSMADataPoints / 2; num12 < num10 - numberOfSMADataPoints / 2 + numberOfSMADataPoints; num12++)
                {
                    int num13 = num12;
                    if (num13 < 0)
                    {
                        num13 = 0;
                    }
                    else if (num12 >= spectrum.Length)
                    {
                        num13 = spectrum.Length - 1;
                    }
                    num11 += (double)spectrum[num13];
                }
                result[num10] = num11 / (double)numberOfSMADataPoints;
            }
            return result;
        }

        public int[] SMA(int[] spectrum, int numberOfSMADataPoints)
        {
            int[] result = new int[spectrum.Length];
            for (int num10 = 0; num10 < spectrum.Length; num10++)
            {
                double num11 = 0.0;
                for (int num12 = num10 - numberOfSMADataPoints / 2; num12 < num10 - numberOfSMADataPoints / 2 + numberOfSMADataPoints; num12++)
                {
                    int num13 = num12;
                    if (num13 < 0)
                    {
                        num13 = 0;
                    }
                    else if (num12 >= spectrum.Length)
                    {
                        num13 = spectrum.Length - 1;
                    }
                    num11 += (double)spectrum[num13];
                }
                result[num10] = (int)(num11 / (double)numberOfSMADataPoints);
            }
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
