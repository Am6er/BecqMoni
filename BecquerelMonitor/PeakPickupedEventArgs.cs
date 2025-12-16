using System;

namespace BecquerelMonitor
{
    public class PeakPickupedEventArgs : EventArgs
    {
        public CalibrationPeak CalibrationPeak { 
            get => peak; 
            set => peak = value; 
        }

        public PeakPickupedEventArgs(int channel, double energy, double fwhm)
        {
            this.peak = new CalibrationPeak {
                Channel = channel,
                Energy = energy,
                FWHM = fwhm
            }; 
        }

        CalibrationPeak peak;
    }
}
