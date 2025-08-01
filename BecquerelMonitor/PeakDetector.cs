﻿using BecquerelMonitor.Utils;
using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
            resultData.DetectedPeaks.Clear();

            FWHMPeakDetector.PeakFinder finder = PeakFinder(energySpectrum, FWHMPeakDetectionMethodConfig);

            peaks = CollectPeaks(finder, energySpectrum, FWHMPeakDetectionMethodConfig.Tolerance, resultData.DetectedPeaks, sa, nuclideSet);
            
            resultData.DetectedPeaks = peaks;

            //sa.Dispose();
            GC.Collect();
            return peaks;
        }

        bool isNewPeak(List<Peak> existpeaks, Peak newpeak, bool hidepeaks, List<Peak> peaks)
        {
            foreach (Peak peak in existpeaks)
            {
                if (Math.Abs(newpeak.Channel - peak.Channel) <= 4)
                {
                    return false;
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
                            if (!hidepeaks)
                            {
                                peak.Nuclide = null;
                            } else
                            {
                                existpeaks.Remove(peak);
                                peaks.Remove(peak);
                            }

                            //Trace.WriteLine("New peak added, ch=" + newpeak.Channel + " En=" + newpeak.Energy);
                            return true;
                        } else
                        {
                            return false;
                        }  
                    }
                }
            }
            //Trace.WriteLine("New peak added, ch=" + newpeak.Channel + " En=" + newpeak.Energy);
            return true;
        }

        List<Peak> CollectPeaks(FWHMPeakDetector.PeakFinder finder, EnergySpectrum energySpectrum, double tol, List<Peak> existPeaks, SpectrumAriphmetics sa, NuclideSet nuclideSet)
        {
            List<Peak> peaks = new List<Peak>();
            if (finder.centroids != null)
            {
                for (int i = 0; i < finder.centroids.Length; i++)
                {
                    double centroid = finder.centroids[i];
                    int snr = (int)finder.snrs[i];
                    double fwhm = finder.fwhms[i];
                    centroid = sa.FindCentroid(energySpectrum, Convert.ToInt32(centroid), Convert.ToInt32(centroid - fwhm), Convert.ToInt32(centroid + fwhm));

                    NuclideDefinition bestNuclide = null;
                    double minDelta = Double.MaxValue;
                    Peak peak = new Peak();
                    peak.Channel = Convert.ToInt32(centroid);
                    peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy(peak.Channel);
                    peak.SNR = snr;
                    peak.FWHM = fwhm;
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
                        peak.Nuclide = bestNuclide;
                    }
                    if (peak.Nuclide == null && nuclideSet?.HideUnknownPeaks == true) continue;
                    bool hidepeaks = false;
                    if (nuclideSet != null) hidepeaks = nuclideSet.HideUnknownPeaks;
                    if (isNewPeak(existPeaks, peak, hidepeaks, peaks))
                    {
                        peaks.Add(peak);
                        existPeaks.Add(peak);
                    }
                }
            }
            return peaks;
        }

        FWHMPeakDetector.PeakFinder PeakFinder(EnergySpectrum energySpectrum, FWHMPeakDetectionMethodConfig fWHMPeakDetectionMethodConfig)
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
            FWHMPeakDetector.PeakFilter kernel = new FWHMPeakDetector.PeakFilter(
                fWHMPeakDetectionMethodConfig.Ch_Fwhm,
                fWHMPeakDetectionMethodConfig.Width_Fwhm,
                fWHMPeakDetectionMethodConfig.FWHM_AT_0);
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
