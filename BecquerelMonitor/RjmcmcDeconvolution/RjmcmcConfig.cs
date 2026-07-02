using System;

namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal sealed class RjmcmcConfig
    {
        public bool Enabled { get; private set; }
        public int BurnIn { get; private set; }
        public int Samples { get; private set; }
        public int Seed { get; private set; }
        public int MaxRois { get; private set; }
        public int MaxChannelsPerRoi { get; private set; }
        public int MaxExtraPeaksPerRoi { get; private set; }
        public int MaxAnchorsPerRoi { get; private set; }
        public double RoiRadiusFwhm { get; private set; }
        public double CenterUpdateSigmaFwhm { get; private set; }
        public double BackgroundUpdateFraction { get; private set; }
        public double TargetSnr { get; private set; }
        public double ExtraSnrMultiplier { get; private set; }
        public double MinDevianceImprovement { get; private set; }
        public double ExtraPeakPenalty { get; private set; }
        public double MinimumCandidateAmplitude { get; private set; }
        public int MinimumSupportingChains { get; private set; }
        public double SupportingDevianceImprovement { get; private set; }
        public double SupportingSnrFraction { get; private set; }
        public double SupportCenterToleranceFwhm { get; private set; }
        public int SupportCenterToleranceMaxChannels { get; private set; }
        public int ChainCount { get; private set; }

        /// <summary>
        /// Creates the baseline local-RJMCMC settings used for difficult peak-overlap ROIs.
        /// References: Green (1995), "Reversible jump Markov chain Monte Carlo computation and Bayesian model determination",
        /// https://doi.org/10.1093/biomet/82.4.711; Deep research report, section "Implementation priorities".
        /// </summary>
        public static RjmcmcConfig CreateDefault()
        {
            const double defaultPeakFinderSnr = 10.0;
            const double defaultExtraSnrMultiplier = 1.5;
            double defaultTargetSnr = CalculateExtraTargetSnr(defaultPeakFinderSnr, defaultExtraSnrMultiplier);

            return new RjmcmcConfig
            {
                Enabled = true,
                BurnIn = 500,
                Samples = 1500,
                Seed = 1337,
                MaxRois = 20,
                MaxChannelsPerRoi = 512,
                MaxExtraPeaksPerRoi = 1,
                MaxAnchorsPerRoi = 6,
                RoiRadiusFwhm = 3.0,
                CenterUpdateSigmaFwhm = 0.2,
                BackgroundUpdateFraction = 0.08,
                TargetSnr = defaultTargetSnr,
                ExtraSnrMultiplier = defaultExtraSnrMultiplier,
                MinDevianceImprovement = CalculateMinimumDevianceImprovement(defaultTargetSnr),
                ExtraPeakPenalty = 0.35,
                MinimumCandidateAmplitude = 0.0,
                MinimumSupportingChains = 3,
                SupportingDevianceImprovement = CalculateSupportingDevianceImprovement(defaultTargetSnr, 0.75),
                SupportingSnrFraction = 0.75,
                SupportCenterToleranceFwhm = 0.10,
                SupportCenterToleranceMaxChannels = 10,
                ChainCount = 4
            };
        }

        /// <summary>
        /// Creates the ROI-scoped RJMCMC profile recommended for production use after deterministic candidate search.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510; Deep research report, sections
        /// "Bayesian variable-dimensional models" and "Implementation priorities".
        /// </summary>
        public static RjmcmcConfig CreateForRoiSearch(FWHMPeakDetectionMethodConfig peakConfig)
        {
            RjmcmcConfig config = CreateDefault();
            if (peakConfig == null)
            {
                return config;
            }

            config.Enabled = peakConfig.UseDeconvolution;
            config.BurnIn = Math.Max(0, peakConfig.BurnIn);
            config.Samples = Math.Max(1, peakConfig.Samples);
            config.MaxRois = Math.Max(1, peakConfig.MaxRois);
            config.MaxExtraPeaksPerRoi = Math.Max(0, peakConfig.MaxExtraPeaksPerRoi);
            config.RoiRadiusFwhm = Math.Max(1.0, peakConfig.RoiRadiusFwhm);
            double peakFinderSnr = NormalizeTargetSnr(peakConfig.Min_SNR);
            config.TargetSnr = CalculateExtraTargetSnr(peakFinderSnr, config.ExtraSnrMultiplier);
            config.MinDevianceImprovement = CalculateMinimumDevianceImprovement(config.TargetSnr);
            config.MinimumCandidateAmplitude = 0.0;
            config.SupportingDevianceImprovement = CalculateSupportingDevianceImprovement(config.TargetSnr, config.SupportingSnrFraction);
            return config;
        }

        static double NormalizeTargetSnr(double minSnr)
        {
            if (Double.IsNaN(minSnr) || Double.IsInfinity(minSnr))
            {
                return 1.0;
            }

            return Math.Max(1.0, minSnr);
        }

        static double CalculateExtraTargetSnr(double peakFinderSnr, double extraSnrMultiplier)
        {
            double multiplier = Double.IsNaN(extraSnrMultiplier) || Double.IsInfinity(extraSnrMultiplier)
                ? 1.0
                : Math.Max(1.0, extraSnrMultiplier);
            return Math.Max(1.0, peakFinderSnr * multiplier);
        }

        static double CalculateMinimumDevianceImprovement(double targetSnr)
        {
            return Math.Max(1.0, targetSnr * targetSnr);
        }

        static double CalculateSupportingDevianceImprovement(double targetSnr, double supportingSnrFraction)
        {
            double supportSnr = Math.Max(1.0, targetSnr * Math.Max(0.0, supportingSnrFraction));
            return supportSnr * supportSnr;
        }
    }
}
