using BecquerelMonitor.Utils;
using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            if (energySpectrum.Spectrum.Sum() == 0)
            {
                return peaks;
            }

            int min_range_ch = (int)energySpectrum.EnergyCalibration.EnergyToChannel(FWHMPeakDetectionMethodConfig.Min_Range);
            int max_range_ch = (int)energySpectrum.EnergyCalibration.EnergyToChannel(FWHMPeakDetectionMethodConfig.Max_Range);

            double fwhm_tol_min = ((double)FWHMPeakDetectionMethodConfig.Min_FWHM_Tol) / 100;
            double fwhm_tol_max = ((double)FWHMPeakDetectionMethodConfig.Max_FWHM_Tol) / 100;

            FWHMPeakDetector.Spectrum spec = new FWHMPeakDetector.Spectrum(energySpectrum);
            int mul = energySpectrum.NumberOfChannels / 512;
            if (mul > 1)
            {
                spec.combine_bins(mul);
            }
            FWHMPeakDetector.PeakFilter kernel = new FWHMPeakDetector.PeakFilter(
                FWHMPeakDetectionMethodConfig.Ch_Fwhm, 
                FWHMPeakDetectionMethodConfig.Width_Fwhm,
                FWHMPeakDetectionMethodConfig.FWHM_AT_0);
            FWHMPeakDetector.PeakFinder finder = new FWHMPeakDetector.PeakFinder(
                spec, 
                kernel,
                fwhm_tol_min: fwhm_tol_min,
                fwhm_tol_max: fwhm_tol_max);
            finder.find_peaks(min_range_ch,
                max_range_ch, 
                FWHMPeakDetectionMethodConfig.Min_SNR, 
                FWHMPeakDetectionMethodConfig.Max_Items);

            resultData.DetectedPeaks.Clear();
            
            if (finder.centroids != null)
            {
                SpectrumAriphmetics sa = new SpectrumAriphmetics(energySpectrum);
                for (int i = 0; i < finder.centroids.Length; i++)
                {
                    int centroid = (int)Math.Round(finder.centroids[i]);
                    double snr = finder.snrs[i];
                    double fwhm = finder.fwhms[i];

                    //Fit optimization
                    if (mul == 0)
                    {
                        mul = 3;
                    }
                    int low_boundary = centroid - (int)mul;
                    int high_boundary = centroid + (int)mul;
                    if (low_boundary < 0) low_boundary = 0;
                    if (high_boundary > energySpectrum.NumberOfChannels) high_boundary = energySpectrum.NumberOfChannels - 1;
                    double[] x = new double[high_boundary - low_boundary];
                    double[] y = new double[high_boundary - low_boundary];
                    for (int j = 0; j < high_boundary - low_boundary; j++)
                    {
                        x[j] = low_boundary + j;
                        y[j] = energySpectrum.Spectrum[low_boundary + j];
                    }
                    Func<double, double> func = Fit.PolynomialFunc(x, y, 3);
                    double new_centroid = centroid;
                    double max = func.Invoke(new_centroid);
                    for (int j = low_boundary; j < high_boundary; j++)
                    {
                        double new_max = func.Invoke(j);
                        if (new_max > max)
                        {
                            new_centroid = j;
                            max = new_max;
                        }
                    }
                    if (new_centroid > low_boundary && new_centroid < high_boundary)
                    {
                        Trace.WriteLine("New centroid: " + centroid + " -> " + new_centroid);
                        centroid = (int)new_centroid;
                    }

                    NuclideDefinition bestNuclide = null;
                    double minDelta = -1;
                    Peak peak = new Peak();
                    peak.Channel = centroid;
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
                sa.Dispose();
            }
            finder = null;
            kernel = null;
            spec = null;
            GC.Collect();
            return peaks;
        }

        FWHMPeakDetector.PeakFinder doCorrection(FWHMPeakDetector.PeakFinder Finder)
        {
            FWHMPeakDetector.PeakFinder peakFinder = Finder;
            if (peakFinder != null)
            {

            }
            return peakFinder;
        }

        // Token: 0x0400025C RID: 604
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
    }
}
