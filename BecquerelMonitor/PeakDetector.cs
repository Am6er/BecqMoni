using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000065 RID: 101
    public class PeakDetector
    {
        public List<Peak> DetectPeak(ResultData resultData)
        {
            EnergySpectrum energySpectrum = resultData.EnergySpectrum;
            FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
            List<Peak> peaks = new List<Peak>();
            int sum = 0;
            for (int i = 0; i < energySpectrum.Spectrum.Length; i++)
            {
                sum += energySpectrum.Spectrum[i];
            }
            if (sum == 0)
            {
                return peaks;
            }

            int min_range_ch = (int)energySpectrum.EnergyCalibration.EnergyToChannel(FWHMPeakDetectionMethodConfig.Min_Range);
            int max_range_ch = (int)energySpectrum.EnergyCalibration.EnergyToChannel(FWHMPeakDetectionMethodConfig.Max_Range);

            FWHMPeakDetector.Spectrum spec = new FWHMPeakDetector.Spectrum(energySpectrum);
            int mul = energySpectrum.NumberOfChannels / 1024;
            if (mul > 1)
            {
                spec.combine_bins(mul);
            }
            FWHMPeakDetector.PeakFilter kernel = new FWHMPeakDetector.PeakFilter(
                FWHMPeakDetectionMethodConfig.Ch_Fwhm, 
                FWHMPeakDetectionMethodConfig.Width_Fwhm,
                FWHMPeakDetectionMethodConfig.FWHM_AT_0);
            FWHMPeakDetector.PeakFinder finder = new FWHMPeakDetector.PeakFinder(spec, kernel);
            finder.find_peaks(min_range_ch, max_range_ch, FWHMPeakDetectionMethodConfig.Min_SNR, FWHMPeakDetectionMethodConfig.Max_Items);

            resultData.DetectedPeaks.Clear();
            
            if (finder.centroids != null)
            {
                for (int i = 0; i < finder.centroids.Length; i++)
                {
                    double centroid = finder.centroids[i];
                    double snr = finder.snrs[i];
                    double fwhm = finder.fwhms[i];
                    NuclideDefinition bestNuclide = null;
                    double minDelta = -1;
                    Peak peak = new Peak();
                    peak.Channel = (int)Math.Round(centroid + 1);
                    peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy(peak.Channel);
                    peak.SNR = (int)snr;
                    peak.FWHM = (int)fwhm;
                    foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
                    {
                        double delta = Math.Abs((peak.Energy - nuclideDefinition.Energy) / nuclideDefinition.Energy);
                        double tol = FWHMPeakDetectionMethodConfig.Tolerance;
                        if (delta < tol / 100.0 && delta < tol)
                        {
                            if (minDelta == -1)
                            {
                                bestNuclide = nuclideDefinition;
                                minDelta = delta;
                            } else
                            {
                                if (delta < minDelta)
                                {
                                    bestNuclide = nuclideDefinition;
                                    minDelta = delta;
                                }
                            }
                        }
                        peak.Nuclide = bestNuclide;
                    }

                    peaks.Add(peak);
                    resultData.DetectedPeaks.Add(peak);
                }
            }
            finder = null;
            kernel = null;
            spec = null;
            GC.Collect();
            return peaks;
        }

        // Token: 0x0400025C RID: 604
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
    }
}
