namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal sealed class RjmcmcPeakCandidate
    {
        public double Channel { get; set; }
        public double Fwhm { get; set; }
        public double Amplitude { get; set; }
        public double Snr { get; set; }
        public double DevianceImprovement { get; set; }
    }
}
