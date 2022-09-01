using System;

namespace BecquerelMonitor.Utils
{
    public class SpectrumAriphmetics
    {
        public SpectrumAriphmetics(DocEnergySpectrum energySpectrum)
        {
            this.MainSpectrum = energySpectrum;
        }

        public DocEnergySpectrum CombineWith(DocEnergySpectrum energySpectrum)
        {
            if (this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels == energySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
            {
                if (energySpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration.Equals(this.MainSpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration))
                {
                    for (int i = 0; i < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels; i++)
                    {
                        this.MainSpectrum.ActiveResultData.EnergySpectrum.Spectrum[i] += energySpectrum.ActiveResultData.EnergySpectrum.Spectrum[i];
                    }
                }
                else
                {
                    for (int i = 0; i < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels; i++)
                    {
                        PolynomialEnergyCalibration MainSpectrumEnergyCalibration = (PolynomialEnergyCalibration)this.MainSpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration;
                        PolynomialEnergyCalibration CombinedSpectrumEnergyCalibration = (PolynomialEnergyCalibration)energySpectrum.ActiveResultData.EnergySpectrum.EnergyCalibration;
                        double getChannel = Math.Round(CombinedSpectrumEnergyCalibration.EnergyToChannel(MainSpectrumEnergyCalibration.ChannelToEnergy(i)));
                        if (getChannel >= 0 && getChannel < this.MainSpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
                        {
                            this.MainSpectrum.ActiveResultData.EnergySpectrum.Spectrum[i] += energySpectrum.ActiveResultData.EnergySpectrum.Spectrum[Convert.ToInt32(getChannel)];
                        }
                    }
                }

                this.MainSpectrum.ActiveResultData.EnergySpectrum.MeasurementTime += energySpectrum.ActiveResultData.EnergySpectrum.MeasurementTime;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.TotalPulseCount += energySpectrum.ActiveResultData.EnergySpectrum.TotalPulseCount;
                this.MainSpectrum.ActiveResultData.EnergySpectrum.ValidPulseCount += energySpectrum.ActiveResultData.EnergySpectrum.ValidPulseCount;

                this.MainSpectrum.ActiveResultData.PresetTime += energySpectrum.ActiveResultData.PresetTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.PresetTime += energySpectrum.ActiveResultData.ResultDataStatus.PresetTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.ElapsedTime += energySpectrum.ActiveResultData.ResultDataStatus.ElapsedTime;
                this.MainSpectrum.ActiveResultData.ResultDataStatus.TotalTime += energySpectrum.ActiveResultData.ResultDataStatus.TotalTime;
            }

            return this.MainSpectrum;
        }

        private DocEnergySpectrum MainSpectrum;
    }
}
