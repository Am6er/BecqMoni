using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    public class PeakDetector
    {
        public List<Peak> DetectPeak(ResultData resultData, BackgroundMode bgMode, SmoothingMethod smoothMethod, NuclideSet nuclideSet, List<NuclideDefinition> nuclideDefinitions = null)
        {
            // Снимок списка нуклидов. DetectPeak крутится в Task.Run, а
            // NuclideSetForm правит и СОРТИРУЕТ тот же список из UI-потока:
            // перечисление живого списка ловит "Collection was modified", а
            // catch-all в DCPeakDetectionView гасит этим всю детекцию в одну
            // строку Trace. Копию снимает вызывающий — на UI-потоке; null для
            // однопоточных вызовов (харнесс).
            this.nuclideDefinitions = nuclideDefinitions ?? this.nuclideManager.NuclideDefinitions;

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

            peaks = CollectPeaks(finder, searchSpectrum, fwhmPeakDetectionMethodConfig.Tolerance, sa, nuclideSet, fwhmPeakDetectionMethodConfig);
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
                // Площадь берётся у того же финдера и по тому же номеру: все
                // его массивы параллельны и фильтруются вместе (`PeakFinder`
                // обрезает их одним проходом). Отсутствие массива — не повод
                // молча подставить ноль, поэтому длина проверяется.
                double netCounts = finder.integrals != null && i < finder.integrals.Length
                    ? finder.integrals[i]
                    : 0.0;

                Peak peak = CreatePeak(
                    energySpectrum,
                    finder.centroids[i],
                    finder.snrs[i],
                    finder.fwhms[i],
                    finder.fwhm_delta[i],
                    netCounts,
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

        /// <param name="netCounts">
        /// Чистая площадь пика — отклик согласованного фильтра за вычетом
        /// подложки (`PeakFinder.integrals`, то есть `signal[xbin]`).
        ///
        /// До 13.08.2026 сюда не приходило НИЧЕГО, и `Peak.Count` у каждого
        /// найденного пика оставался нулём. Поле при этом читалось — в
        /// `PeakOriginProbe` на нём стоят два отбора «пик заметный»
        /// (родитель обратного рассеяния и слагаемые случайной суммы), и оба
        /// сравнивали ноль с нулём: `q.Count &lt; 0.05·maxCounts` при нулевом
        /// максимуме ложно ВСЕГДА. Отсюда и «случайных сумм ноль» в журнале
        /// InterSpec (§6), списанное тогда на лабораторные условия, и то, что
        /// обратное рассеяние объясняло 59 % всех пиков: родителем годился
        /// любой пик выше по шкале (TODO P4).
        /// </param>
        Peak CreatePeak(
            EnergySpectrum energySpectrum,
            double centroid,
            double snr,
            double fwhm,
            double fwhmDelta,
            double netCounts,
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
            peak.Count = netCounts > 0.0 && !Double.IsNaN(netCounts)
                ? (int)Math.Round(Math.Min(netCounts, Int32.MaxValue))
                : 0;
            return peak;
        }

        NuclideDefinition MatchNuclide(Peak peak, double tol, NuclideSet nuclideSet)
        {
            NuclideDefinition bestNuclide = null;
            double minDelta = Double.MaxValue;
            foreach (NuclideDefinition nuclideDefinition in this.nuclideDefinitions)
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

        // Снимок NuclideDefinitions на время одного прогона DetectPeak.
        List<NuclideDefinition> nuclideDefinitions;
    }
}
