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
        public double MinDevianceImprovement { get; private set; }
        public double ExtraPeakPenalty { get; private set; }
        public double MinimumCandidateAmplitude { get; private set; }
        public int ChainCount { get; private set; }

        /// <summary>
        /// Creates the baseline local-RJMCMC settings used for difficult peak-overlap ROIs.
        /// References: Green (1995), "Reversible jump Markov chain Monte Carlo computation and Bayesian model determination",
        /// https://doi.org/10.1093/biomet/82.4.711; Deep research report, section "Implementation priorities".
        /// </summary>
        public static RjmcmcConfig CreateDefault()
        {
            return new RjmcmcConfig
            {
                Enabled = true,
                BurnIn = 500,
                Samples = 1500,
                Seed = 1337,
                MaxRois = 20,
                MaxChannelsPerRoi = 512,
                MaxExtraPeaksPerRoi = 3,
                MaxAnchorsPerRoi = 6,
                RoiRadiusFwhm = 6.0,
                CenterUpdateSigmaFwhm = 0.2,
                BackgroundUpdateFraction = 0.08,
                MinDevianceImprovement = 2.0,
                ExtraPeakPenalty = 0.35,
                MinimumCandidateAmplitude = 2.0,
                ChainCount = Math.Max(1, Environment.ProcessorCount - 1)
            };
        }

        /// <summary>
        /// Creates the ROI-scoped RJMCMC profile recommended for production use after deterministic candidate search.
        /// References: Gulam Razul et al. (2003), NIM A 497, 492-510; Deep research report, sections
        /// "Bayesian variable-dimensional models" and "Implementation priorities".
        /// </summary>
        public static RjmcmcConfig CreateForRoiSearch()
        {
            RjmcmcConfig config = CreateDefault();
            return config;
        }
    }
}
