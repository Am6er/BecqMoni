namespace BecquerelMonitor
{
    public class CalibrationPeak
    {
        int channel;
        double energy;
        double fwhm;

        public int Channel { get => channel; set => channel = value; }
        public double Energy { get => energy; set => energy = value; }
        public double FWHM { get => fwhm; set => fwhm = value; }
    }
}
