using BecquerelMonitor.RjmcmcDeconvolution;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BecquerelMonitor
{
    public class PeakDetector
    {
        public List<Peak> DetectPeak(ResultData resultData, BackgroundMode bgMode, SmoothingMethod smoothMethod, NuclideSet nuclideSet)
        {
            FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
            EnergySpectrum inferenceSpectrum;
            SpectrumAriphmetics sa = new SpectrumAriphmetics();
            if (bgMode == BackgroundMode.Substract && resultData.BackgroundEnergySpectrum != null)
            {
                sa = new SpectrumAriphmetics(resultData.EnergySpectrum);
                inferenceSpectrum = sa.Substract(resultData.BackgroundEnergySpectrum);
            }
            else
            {
                inferenceSpectrum = resultData.EnergySpectrum.Clone();
            }

            EnergySpectrum searchSpectrum = inferenceSpectrum.Clone();
            int countlimit = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.CountLimit;
            bool progressiveSmooth = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.ProgresiveSmooth;
            switch (smoothMethod)
            {
                case SmoothingMethod.SimpleMovingAverage:
                    int points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                    searchSpectrum.Spectrum = sa.SMA(searchSpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                    searchSpectrum.Spectrum = sa.WMA(searchSpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
            }

            List<Peak> peaks = new List<Peak>();
            if (searchSpectrum.TotalPulseCount == 0)
            {
                return peaks;
            }

            FWHMPeakDetector.PeakFinder finder = PeakFinder(searchSpectrum, fwhmPeakDetectionMethodConfig, resultData.FwhmCalibration);
            bool useDeconvolution = fwhmPeakDetectionMethodConfig.UseDeconvolution;
            RjmcmcResult deconvolution = null;
            if (useDeconvolution)
            {
                deconvolution = RjmcmcPeakDeconvolver.Run(
                    resultData.EnergySpectrum,
                    bgMode == BackgroundMode.Substract ? resultData.BackgroundEnergySpectrum : null,
                    finder,
                    fwhmPeakDetectionMethodConfig,
                    resultData.FwhmCalibration);
            }

            peaks = CollectPeaks(finder, searchSpectrum, fwhmPeakDetectionMethodConfig.Tolerance, sa, nuclideSet, fwhmPeakDetectionMethodConfig);
            if (useDeconvolution)
            {
                AppendRjmcmcPeaks(
                    peaks,
                    deconvolution,
                    inferenceSpectrum,
                    resultData.FwhmCalibration,
                    fwhmPeakDetectionMethodConfig.Min_SNR,
                    fwhmPeakDetectionMethodConfig.Tolerance,
                    nuclideSet);
                peaks.Sort((left, right) =>
                {
                    int energyComparison = left.Energy.CompareTo(right.Energy);
                    return energyComparison != 0
                        ? energyComparison
                        : left.Channel.CompareTo(right.Channel);
                });
            }
            GC.Collect();
            return peaks;
        }

        bool isNewPeak(Peak newpeak, bool hidepeaks, List<Peak> peaks)
        {
            bool isUnresol = false;
            foreach (Peak peak in peaks)
            {
                // Sparrow limit
                // Критерий неразрешимости двух пиков delta < 2 * sigma
                // fwhm = 2 * sqrt(2 * ln(2)) * sigma
                // delta < 0.85 * fwhm
                if (!isUnresol && Math.Abs(newpeak.Channel - peak.Channel) <= 0.85 * peak.FWHM)
                {
                    isUnresol = true;
                }
                if (newpeak.Nuclide != null && peak.Nuclide != null)
                {
                    if (newpeak.Nuclide.Energy == peak.Nuclide.Energy)
                    {
                        double newpeak_delta = Math.Abs(newpeak.Energy - newpeak.Nuclide.Energy);
                        double oldpeak_delta = Math.Abs(peak.Energy - peak.Nuclide.Energy);
                        if (newpeak_delta < oldpeak_delta)
                        {
                            if (hidepeaks || isUnresol)
                            {
                                peaks.Remove(peak);
                            }
                            else
                            {
                                peak.Nuclide = null;
                            }
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            return !isUnresol;
        }

        List<Peak> CollectPeaks(FWHMPeakDetector.PeakFinder finder, EnergySpectrum energySpectrum, double tol, SpectrumAriphmetics sa, NuclideSet nuclideSet, FWHMPeakDetectionMethodConfig peakConfig)
        {
            List<Peak> peaks = new List<Peak>();
            if (finder.centroids == null)
            {
                return peaks;
            }

            for (int i = 0; i < finder.centroids.Length; i++)
            {
                Peak peak = CreatePeak(
                    energySpectrum,
                    finder.centroids[i],
                    finder.snrs[i],
                    finder.fwhms[i],
                    finder.fwhm_delta[i],
                    sa,
                    peakConfig,
                    refineCentroid: true);
                peak.PeakSearchOrigin = PeakSearchOrigin.FWHMPeakFinder;
                peak.Nuclide = MatchNuclide(peak, tol, nuclideSet);
                if (peak.Nuclide == null && nuclideSet?.HideUnknownPeaks == true)
                {
                    continue;
                }

                bool hidepeaks = nuclideSet != null && nuclideSet.HideUnknownPeaks;
                if (isNewPeak(peak, hidepeaks, peaks))
                {
                    peaks.Add(peak);
                }
            }

            return peaks;
        }

        void AppendRjmcmcPeaks(List<Peak> peaks, RjmcmcResult deconvolution, EnergySpectrum energySpectrum, FwhmCalibration fwhmCalibration, double minSnr, double tol, NuclideSet nuclideSet)
        {
            if (deconvolution == null || deconvolution.ExtraCandidates == null || deconvolution.ExtraCandidates.Count == 0)
            {
                return;
            }

            foreach (RjmcmcPeakCandidate candidate in deconvolution.ExtraCandidates.OrderBy(c => c.Channel))
            {
                if (!IsFinite(candidate.Snr) || candidate.Snr < minSnr)
                {
                    continue;
                }

                Peak peak = CreatePeak(
                    energySpectrum,
                    candidate.Channel,
                    candidate.Snr,
                    candidate.Fwhm,
                    FwhmDelta(candidate, fwhmCalibration),
                    null,
                    null,
                    refineCentroid: false);
                peak.PeakSearchOrigin = PeakSearchOrigin.RJMCMC;
                peak.DeconvolutionInfo = new PeakDeconvolutionInfo
                {
                    Amplitude = candidate.Amplitude,
                    DevianceImprovement = candidate.DevianceImprovement,
                    PosteriorOccupancy = candidate.PosteriorOccupancy,
                    CenterStdDev = candidate.CenterStdDev,
                    ResidualSnr = candidate.ResidualSnr,
                    ResidualCorrelation = candidate.ResidualCorrelation,
                    AnchorDistanceFwhm = candidate.AnchorDistanceFwhm,
                    SupportingChainCount = candidate.SupportingChainCount,
                    MinimumSnrThreshold = minSnr,
                    MatchTolerancePercent = tol
                };
                peak.Nuclide = MatchNuclide(peak, tol, nuclideSet);
                if (peak.Nuclide == null && nuclideSet?.HideUnknownPeaks == true)
                {
                    continue;
                }

                if (CanAppendDeconvolvedPeak(peak, peaks))
                {
                    peaks.Add(peak);
                }
            }
        }

        double FwhmDelta(RjmcmcPeakCandidate candidate, FwhmCalibration fwhmCalibration)
        {
            if (candidate == null || fwhmCalibration == null)
            {
                return 0.0;
            }

            return Math.Abs(candidate.Fwhm - fwhmCalibration.ChannelToFwhm(candidate.Channel));
        }

        bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        Peak CreatePeak(
            EnergySpectrum energySpectrum,
            double centroid,
            double snr,
            double fwhm,
            double fwhmDelta,
            SpectrumAriphmetics sa,
            FWHMPeakDetectionMethodConfig config,
            bool refineCentroid)
        {
            if (refineCentroid && sa != null && config != null)
            {
                int concat = Math.Max(1, config.Ch_Concat);
                int mul = energySpectrum.Spectrum.Length / concat;
                centroid = sa.FindCentroid2(
                    energySpectrum,
                    Convert.ToInt32(centroid),
                    Convert.ToInt32(centroid - mul - 1),
                    Convert.ToInt32(centroid + mul + 1));
            }

            Peak peak = new Peak();
            peak.Channel = Math.Max(0, Math.Min(energySpectrum.NumberOfChannels - 1, Convert.ToInt32(Math.Round(centroid))));
            peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy(peak.Channel);
            peak.SNR = snr;
            peak.FWHM = fwhm;
            peak.FWHM_DELTA = fwhmDelta;
            return peak;
        }

        NuclideDefinition MatchNuclide(Peak peak, double tol, NuclideSet nuclideSet)
        {
            NuclideDefinition bestNuclide = null;
            double minDelta = Double.MaxValue;
            foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
            {
                if (!nuclideDefinition.Visible || nuclideDefinition.Energy == 0.0) continue;
                if (nuclideSet != null && !nuclideDefinition.Sets.Contains(nuclideSet.Id)) continue;

                double delta = Math.Abs((peak.Energy - nuclideDefinition.Energy) / nuclideDefinition.Energy);
                if (delta < tol / 100.0 && delta < minDelta)
                {
                    bestNuclide = nuclideDefinition;
                    minDelta = delta;
                }
            }

            return bestNuclide;
        }

        bool CanAppendDeconvolvedPeak(Peak peak, List<Peak> peaks)
        {
            foreach (Peak existingPeak in peaks)
            {
                double duplicateDistance = Math.Max(1.0, 0.04 * Math.Max(existingPeak.FWHM, peak.FWHM));
                if (Math.Abs(existingPeak.Channel - peak.Channel) <= duplicateDistance)
                {
                    return false;
                }
            }

            return true;
        }

        FWHMPeakDetector.PeakFinder PeakFinder(EnergySpectrum energySpectrum, FWHMPeakDetectionMethodConfig peakConfig, FwhmCalibration fwhmCalibration)
        {
            int min_range_ch = Convert.ToInt32(energySpectrum.EnergyCalibration.EnergyToChannel(peakConfig.Min_Range, maxChannels: energySpectrum.NumberOfChannels));
            int max_range_ch = Convert.ToInt32(energySpectrum.EnergyCalibration.EnergyToChannel(peakConfig.Max_Range, maxChannels: energySpectrum.NumberOfChannels));
            min_range_ch = Math.Max(0, Math.Min(energySpectrum.NumberOfChannels - 1, min_range_ch));
            max_range_ch = Math.Max(0, Math.Min(energySpectrum.NumberOfChannels - 1, max_range_ch));
            if (max_range_ch < min_range_ch)
            {
                int swap = min_range_ch;
                min_range_ch = max_range_ch;
                max_range_ch = swap;
            }

            double fwhm_tol_min = ((double)peakConfig.Min_FWHM_Tol) / 100;
            double fwhm_tol_max = ((double)peakConfig.Max_FWHM_Tol) / 100;

            FWHMPeakDetector.Spectrum spec = new FWHMPeakDetector.Spectrum(energySpectrum);
            int concat = Math.Max(1, peakConfig.Ch_Concat);
            int mul = energySpectrum.NumberOfChannels / concat;
            if (mul > 1)
            {
                spec.combine_bins(mul);
            }
            FWHMPeakDetector.PeakFilter kernel = new FWHMPeakDetector.PeakFilter(fwhmCalibration);
            FWHMPeakDetector.PeakFinder finder = new FWHMPeakDetector.PeakFinder(
                spec,
                kernel,
                fwhm_tol_min: fwhm_tol_min,
                fwhm_tol_max: fwhm_tol_max);
            finder.find_peaks(
                min_range_ch,
                max_range_ch,
                peakConfig.Min_SNR,
                peakConfig.Max_Items);
            return finder;
        }

        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
    }
}
