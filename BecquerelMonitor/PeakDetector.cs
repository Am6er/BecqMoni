using BecquerelMonitor.Utils;
using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BecquerelMonitor
{
    public class PeakDetector
    {
        public List<Peak> DetectPeak(ResultData resultData, BackgroundMode bgMode, SmoothingMethod smoothMethod)
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
            switch (smoothMethod)
            {
                case SmoothingMethod.SimpleMovingAverage:
                    int points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                    energySpectrum.Spectrum = sa.SMA(energySpectrum.Spectrum, points, countlimit: countlimit);
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                    energySpectrum.Spectrum = sa.WMA(energySpectrum.Spectrum, points, countlimit: countlimit);
                    break;
            }


            List<Peak> peaks = new List<Peak>();
            if (energySpectrum.Spectrum.Sum() == 0)
            {
                return peaks;
            }
            resultData.DetectedPeaks.Clear();

            FWHMPeakDetector.PeakFinder finder = PeakFinder(energySpectrum, FWHMPeakDetectionMethodConfig);

            peaks = CollectPeaks(finder, energySpectrum, FWHMPeakDetectionMethodConfig.Tolerance, resultData.DetectedPeaks, sa);

            /*
            energySpectrum = sa.SubtractPeaks(peaks, energySpectrum);
            finder = PeakFinder(energySpectrum, FWHMPeakDetectionMethodConfig);
            List<Peak> peaks2 = CollectPeaks(finder, energySpectrum, FWHMPeakDetectionMethodConfig.Tolerance, peaks, sa);
            peaks.AddRange(peaks2);

            energySpectrum = sa.SubtractPeaks(peaks2, energySpectrum);
            finder = PeakFinder(energySpectrum, FWHMPeakDetectionMethodConfig);
            List<Peak> peaks3 = CollectPeaks(finder, energySpectrum, FWHMPeakDetectionMethodConfig.Tolerance, peaks, sa);
            peaks.AddRange(peaks3);
            */
            
            resultData.DetectedPeaks = peaks;

            //sa.Dispose();
            GC.Collect();
            return peaks;
        }

        bool isNewPeak(List<Peak> peaks, Peak newpeak)
        {
            foreach (Peak peak in peaks)
            {
                if (newpeak.Channel == peak.Channel || newpeak.Energy == peak.Energy || Math.Abs(newpeak.Channel - peak.Channel) <= 6)
                {
                    return false;
                }
                if (newpeak.Nuclide != null && peak.Nuclide != null)
                {
                    if (newpeak.Nuclide.Energy == peak.Nuclide.Energy)
                    {
                        return false;
                    }
                }
            }
            //Trace.WriteLine("New peak added, ch=" + newpeak.Channel + " En=" + newpeak.Energy);
            return true;
        }

        List<Peak> CollectPeaks(FWHMPeakDetector.PeakFinder finder, EnergySpectrum energySpectrum, double tol, List<Peak> existPeaks, SpectrumAriphmetics sa)
        {
            List<Peak> peaks = new List<Peak>();
            if (finder.centroids != null)
            {
                for (int i = 0; i < finder.centroids.Length; i++)
                {
                    int centroid = (int)Math.Round(finder.centroids[i]);
                    int snr = (int)finder.snrs[i];
                    int fwhm = (int)Math.Round(finder.fwhms[i]);
                    centroid = doCorrection(energySpectrum, centroid, fwhm);

                    NuclideDefinition bestNuclide = null;
                    double minDelta = -1;
                    Peak peak = new Peak();
                    peak.Channel = centroid;
                    peak.Energy = energySpectrum.EnergyCalibration.ChannelToEnergy(peak.Channel);
                    peak.SNR = snr;
                    peak.FWHM = fwhm;
                    foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
                    {
                        if (!nuclideDefinition.Visible) continue;
                        double delta = Math.Abs((peak.Energy - nuclideDefinition.Energy) / nuclideDefinition.Energy);
                        //double tol = FWHMPeakDetectionMethodConfig.Tolerance;
                        if (delta < tol / 100.0 && delta < tol)
                        {
                            if (minDelta == -1)
                            {
                                bestNuclide = nuclideDefinition;
                                minDelta = delta;
                            }
                            else
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
                    if (isNewPeak(existPeaks, peak))
                    {
                        peaks.Add(peak);
                    }
                    //energySpectrum = sa.SubtractPeak(peak, energySpectrum);
                }
            }
            return peaks;
        }

        FWHMPeakDetector.PeakFinder PeakFinder(EnergySpectrum energySpectrum, FWHMPeakDetectionMethodConfig fWHMPeakDetectionMethodConfig)
        {
            int min_range_ch = (int)energySpectrum.EnergyCalibration.EnergyToChannel(fWHMPeakDetectionMethodConfig.Min_Range, maxChannels: energySpectrum.NumberOfChannels);
            int max_range_ch = (int)energySpectrum.EnergyCalibration.EnergyToChannel(fWHMPeakDetectionMethodConfig.Max_Range, maxChannels: energySpectrum.NumberOfChannels);

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

        int doCorrection(EnergySpectrum energySpectrum, int centroid, int fwhm)
        {
            if (fwhm == 0)
            {
                fwhm = 1;
            }
            int low_boundary = centroid - fwhm;
            int high_boundary = centroid + fwhm;
            if (low_boundary < 0) low_boundary = 0;
            if (high_boundary > energySpectrum.NumberOfChannels) high_boundary = energySpectrum.NumberOfChannels - 1;
            int poly_order = 8;
            if (high_boundary - low_boundary < 8)
            {
                poly_order = high_boundary - low_boundary;
            }
            if (poly_order < 3)
            {
                return (int) Math.Max(low_boundary, high_boundary);
            }
            double[] x = new double[high_boundary - low_boundary + 1];
            double[] y = new double[high_boundary - low_boundary + 1];
            for (int j = 0; j < high_boundary - low_boundary + 1; j++)
            {
                x[j] = low_boundary + j;
                y[j] = energySpectrum.Spectrum[low_boundary + j];
            }
            Func<double, double> func = Fit.PolynomialFunc(x, y, poly_order);
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
            return (int)Math.Round(new_centroid);
        }

        // Token: 0x0400025C RID: 604
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
    }
}
