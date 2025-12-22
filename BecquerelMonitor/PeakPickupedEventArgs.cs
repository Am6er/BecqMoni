using System;

namespace BecquerelMonitor
{
    public class PeakPickupedEventArgs : EventArgs
    {
        public CalibrationPeak CalibrationPeak { get => peak; set => peak = value; }

        public int PeakStartSelection { get => peakStartSelection; set => peakStartSelection = value; }

        public int PeakEndSelection { get => peakEndSelection; set => peakEndSelection = value; }

        public PeakPickupedEventArgs(int channel, double energy, double fwhm, int peakStartSelection, int peakEndSelection)
        {
            this.peak = new CalibrationPeak {
                Channel = channel,
                Energy = energy,
                FWHM = fwhm
            }; 
            this.peakStartSelection = peakStartSelection;
            this.peakEndSelection = peakEndSelection;
        }

        CalibrationPeak peak;
        int peakStartSelection, peakEndSelection;
    }
}
