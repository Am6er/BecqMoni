using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    public class PeakDetector
    {
        public List<Peak> DetectPeak(ResultData resultData, BackgroundMode bgMode, SmoothingMethod smoothMethod, NuclideSet nuclideSet)
        {
            FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
            EnergySpectrum energySpectrum;
            SpectrumAriphmetics sa = new SpectrumAriphmetics();
            if (bgMode == BackgroundMode.Substract && resultData.BackgroundEnergySpectrum != null)
            {
                sa = new SpectrumAriphmetics(FWHMPeakDetectionMethodConfig, resultData.EnergySpectrum);
                energySpectrum = sa.Substract(resultData.BackgroundEnergySpectrum);
            } else
            {
                energySpectrum = resultData.EnergySpectrum.Clone();
            }
            int countlimit = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.CountLimit;
            bool progressiveSmooth = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.ProgresiveSmooth;
            switch (smoothMethod)
            {
                case SmoothingMethod.SimpleMovingAverage:
                    int points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                    energySpectrum.Spectrum = sa.SMA(energySpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                    energySpectrum.Spectrum = sa.WMA(energySpectrum.Spectrum, points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
            }


            List<Peak> peaks = new List<Peak>();
            if (energySpectrum.TotalPulseCount == 0)
            {
                return peaks;
            }

            FWHMPeakDetector.PeakFinder finder = PeakFinder(energySpectrum, FWHMPeakDetectionMethodConfig, resultData.FwhmCalibration);
            peaks = CollectPeaks(finder, energySpectrum, FWHMPeakDetectionMethodConfig.Tolerance, sa, nuclideSet, FWHMPeakDetectionMethodConfig);
            //sa.Dispose();
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
                        //Fix if new peak suits better by energy, than older one.
                        double newpeak_delta = Math.Abs(newpeak.Energy - newpeak.Nuclide.Energy);
                        double oldpeak_delta = Math.Abs(peak.Energy -  peak.Nuclide.Energy);
                        if (newpeak_delta < oldpeak_delta)
                        {
                            if (hidepeaks || isUnresol)
                            {
                                peaks.Remove(peak);
                            } else
                            {
                                peak.Nuclide = null;
                            }
                            return true;
                        } else
                        {
                            return false;
                        }  
                    }
                }
            }
            return !isUnresol;
        }

        List<Peak> CollectPeaks(FWHMPeakDetector.PeakFinder finder, EnergySpectrum energySpectrum, double tol, SpectrumAriphmetics sa, NuclideSet nuclideSet, FWHMPeakDetectionMethodConfig fWHMPeakDetectionMethodConfig)
        {
            List<Peak> peaks = new List<Peak>();
            if (finder.centroids != null)
            {
                int mul = energySpectrum.Spectrum.Length / fWHMPeakDetectionMethodConfig.Ch_Concat;
                for (int i = 0; i < finder.centroids.Length; i++)
                {
                    double centroid = finder.centroids[i];
                    int snr = (int)finder.snrs[i];
                    double fwhm = finder.fwhms[i];
                    double fwhm_delta = finder.fwhm_delta[i];
                    centroid = sa.FindCentroid2(energySpectrum,
                        Convert.ToInt32(centroid),
                        Convert.ToInt32(centroid - mul - 1),
                        Convert.ToInt32(centroid + mul + 1));

                    NuclideDefinition bestNuclide = null;
                    double minDelta = Double.MaxValue;
                    Peak peak = new Peak();
                    peak.Channel = Convert.ToInt32(centroid);
                    peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy(peak.Channel);
                    peak.SNR = snr;
                    peak.FWHM = fwhm;
                    peak.FWHM_DELTA = fwhm_delta;
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
                    peak.Nuclide = bestNuclide;
                    if (peak.Nuclide == null && nuclideSet?.HideUnknownPeaks == true) continue;
                    bool hidepeaks = false;
                    if (nuclideSet != null) hidepeaks = nuclideSet.HideUnknownPeaks;
                    if (isNewPeak(peak, hidepeaks, peaks))
                    {
                        peaks.Add(peak);
                    }
                }
            }
            return peaks;
        }

        FWHMPeakDetector.PeakFinder PeakFinder(EnergySpectrum energySpectrum, FWHMPeakDetectionMethodConfig fWHMPeakDetectionMethodConfig, FwhmCalibration fwhmCalibration)
        {
            int min_range_ch = Convert.ToInt32(energySpectrum.EnergyCalibration.EnergyToChannel(fWHMPeakDetectionMethodConfig.Min_Range, maxChannels: energySpectrum.NumberOfChannels));
            int max_range_ch = Convert.ToInt32(energySpectrum.EnergyCalibration.EnergyToChannel(fWHMPeakDetectionMethodConfig.Max_Range, maxChannels: energySpectrum.NumberOfChannels));

            double fwhm_tol_min = ((double)fWHMPeakDetectionMethodConfig.Min_FWHM_Tol) / 100;
            double fwhm_tol_max = ((double)fWHMPeakDetectionMethodConfig.Max_FWHM_Tol) / 100;

            FWHMPeakDetector.Spectrum spec = new FWHMPeakDetector.Spectrum(energySpectrum);
            int mul = energySpectrum.NumberOfChannels / fWHMPeakDetectionMethodConfig.Ch_Concat;
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
            finder.find_peaks(min_range_ch,
                max_range_ch,
                fWHMPeakDetectionMethodConfig.Min_SNR,
                fWHMPeakDetectionMethodConfig.Max_Items);
            return finder;
        }

        // Token: 0x0400025C RID: 604
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
    }
}
