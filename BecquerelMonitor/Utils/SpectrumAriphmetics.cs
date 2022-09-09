using BecquerelMonitor.Properties;
using System;
using System.Security.Principal;
using System.Threading.Tasks;

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

        public EnergySpectrum NormalizeFWHM()
        {
            EnergySpectrum normaziledEnergySpectrum = this.EnergySpectrum.Clone();
            normaziledEnergySpectrum.Spectrum = new int[normaziledEnergySpectrum.NumberOfChannels];

            int ch_pos = 0;
            for (int i = 0; i < normaziledEnergySpectrum.NumberOfChannels; i++)
            {
                int ch_to_combine = Convert.ToInt32(Math.Ceiling(FWHM(i)));
                if (ch_to_combine > 1)
                {
                    for (int j = i; j < i + ch_to_combine; j++)
                    {
                        if (j < this.EnergySpectrum.NumberOfChannels)
                        {
                            normaziledEnergySpectrum.Spectrum[ch_pos] += this.EnergySpectrum.Spectrum[j];
                        }
                        normaziledEnergySpectrum.Spectrum[ch_pos] = Convert.ToInt32(normaziledEnergySpectrum.Spectrum[ch_pos] / ch_to_combine);
                    }
                    i += ch_to_combine;
                } else
                {
                    normaziledEnergySpectrum.Spectrum[ch_pos] = this.EnergySpectrum.Spectrum[i];
                }
                ch_pos++;
            }
            return normaziledEnergySpectrum;
        }

        public void Dispose()
        {
            this.MainSpectrum = null;
            this.EnergySpectrum = null;
            GC.Collect();
        }

        double FWHM(double x)
        {
            if (x < 0) return 0;
            return 0.03 *Math.Sqrt(x);
        }

        DocEnergySpectrum MainSpectrum;

        EnergySpectrum EnergySpectrum;
    }
}
