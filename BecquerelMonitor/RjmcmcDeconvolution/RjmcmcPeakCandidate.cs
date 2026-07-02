namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal sealed class RjmcmcPeakCandidate
    {
        public double Channel { get; set; }
        public double Fwhm { get; set; }
        public double Amplitude { get; set; }
        public double Snr { get; set; }
        public double DevianceImprovement { get; set; }
        public double PosteriorOccupancy { get; set; }
        public double CenterStdDev { get; set; }
        public double ResidualSnr { get; set; }
        public double ResidualCorrelation { get; set; }
        public double AnchorDistanceFwhm { get; set; }
        public int SupportingChainCount { get; set; }
    }
}
