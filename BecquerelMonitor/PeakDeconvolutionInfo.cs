namespace BecquerelMonitor
{
    public sealed class PeakDeconvolutionInfo
    {
        public double Amplitude { get; set; }
        public double DevianceImprovement { get; set; }
        public double PosteriorOccupancy { get; set; }
        public double CenterStdDev { get; set; }
        public double ResidualSnr { get; set; }
        public double ResidualCorrelation { get; set; }
        public double AnchorDistanceFwhm { get; set; }
        public int SupportingChainCount { get; set; }
        public double MinimumSnrThreshold { get; set; }
        public double MatchTolerancePercent { get; set; }
    }
}
