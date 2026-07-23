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
            }

            AppendLibraryPeaks(peaks, resultData, bgMode, finder, nuclideSet, fwhmPeakDetectionMethodConfig);

            if (useDeconvolution || nuclideSet != null)
            {
                peaks.Sort((left, right) =>
                {
                    int energyComparison = left.Energy.CompareTo(right.Energy);
                    return energyComparison != 0
                        ? energyComparison
                        : left.Channel.CompareTo(right.Channel);
                });
            }
            return peaks;
        }

        // Библиотечный фит по активному нуклидному сету (LibraryPeakFitter):
        // якорная линия сета найдена finder'ом → компоненты на всех линиях
        // сета, Пуассон-фит амплитуд, значимые (z >= SignificanceZ) — в пики
        // с origin Library. Работает и без деконволюции.
        void AppendLibraryPeaks(
            List<Peak> peaks,
            ResultData resultData,
            BackgroundMode bgMode,
            FWHMPeakDetector.PeakFinder finder,
            NuclideSet nuclideSet,
            FWHMPeakDetectionMethodConfig peakConfig)
        {
            if (nuclideSet == null || peaks.Count == 0)
            {
                return;
            }

            int[] snipContinuum = RjmcmcPeakDeconvolver.BuildSnipContinuum(
                resultData.EnergySpectrum,
                finder,
                resultData.FwhmCalibration);

            LibraryPeakFitter.LibraryFitResult fitResult = LibraryPeakFitter.Fit(
                resultData.EnergySpectrum,
                bgMode == BackgroundMode.Substract ? resultData.BackgroundEnergySpectrum : null,
                snipContinuum,
                resultData.FwhmCalibration,
                peaks,
                nuclideSet,
                peakConfig);

            // Пики, сработавшие якорями (включившие библиотечный фит) —
            // флаг для отрисовки, независимый от того, какой экземпляр
            // нуклида достался пику в MatchNuclide (дубликаты линий).
            foreach (Peak anchorPeak in fitResult.AnchorPeaks)
            {
                anchorPeak.IsLibraryAnchor = true;
            }

            // Пики-центроиды блендов, заменённые линиями bound-группы
            // (BR-связка: их место занимают линии с табличными позициями).
            foreach (Peak replaced in fitResult.ReplacedPeaks)
            {
                peaks.Remove(replaced);
            }

            foreach (LibraryPeakFitter.LibraryCandidate candidate in fitResult.AddedPeaks)
            {
                // Безымянные компоненты отклика (escape SE/DE) скрываются
                // вместе с прочими неопознанными пиками.
                if (candidate.Nuclide == null && nuclideSet.HideUnknownPeaks)
                {
                    continue;
                }

                Peak peak = CreatePeak(
                    resultData.EnergySpectrum,
                    candidate.Channel,
                    candidate.Z,
                    candidate.Fwhm,
                    0.0,
                    null,
                    null,
                    refineCentroid: false);
                peak.PeakSearchOrigin = PeakSearchOrigin.Library;
                peak.Nuclide = candidate.Nuclide;
                if (CanAppendDeconvolvedPeak(peak, peaks))
                {
                    peaks.Add(peak);
                }
            }
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
                            if (hidepeaks || isUnresol)
                            {
                                return false;
                            }
                            // Mirror of the branch above: when the peaks are resolvable,
                            // the farther peak only loses the nuclide label. It used to be
                            // dropped entirely, losing a real peak.
                            newpeak.Nuclide = null;
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
                    MatchTolerancePercent = tol,
                    RoiStartChannel = candidate.RoiStartChannel,
                    RoiEndChannel = candidate.RoiEndChannel,
                    LocalAnchorChannels = candidate.LocalAnchorChannels,
                    HaloAnchorChannels = candidate.HaloAnchorChannels,
                    ReferenceAnchorChannels = candidate.ReferenceAnchorChannels
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
                // Keep the window at least [c-2, c+2]: for spectra shorter than Ch_Concat
                // the integer division gave mul = 0, the window collapsed to [c-1, c+1]
                // and FindCentroid returned only a BOUNDARY - every peak systematically
                // shifted by +-1 channel on 256/512/1000-channel spectra.
                int mul = Math.Max(1, energySpectrum.Spectrum.Length / concat);
                centroid = sa.FindCentroid(
                    energySpectrum,
                    Convert.ToInt32(centroid),
                    Convert.ToInt32(centroid - mul - 1),
                    Convert.ToInt32(centroid + mul + 1),
                    config.UseCenterOfMassCentroid);
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
