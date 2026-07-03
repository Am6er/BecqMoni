using BecquerelMonitor.Properties;
using BecquerelMonitor.RjmcmcDeconvolution;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class PeakDeconvolutionInfoForm : Form
    {
        readonly DocEnergySpectrum document;
        readonly List<Peak> displayedPeaks;
        readonly NuclideSet selectedNuclideSet;

        public PeakDeconvolutionInfoForm(DocEnergySpectrum document, IEnumerable<Peak> peaks, NuclideSet selectedNuclideSet)
        {
            this.document = document;
            this.displayedPeaks = (peaks ?? Enumerable.Empty<Peak>())
                .Where(peak => peak != null)
                .OrderBy(item => item.Energy)
                .ToList();
            this.selectedNuclideSet = selectedNuclideSet;

            this.InitializeComponent();
            this.LoadPeaks(this.displayedPeaks);
        }

        void LoadPeaks(IEnumerable<Peak> peaks)
        {
            this.dataGridViewDetails.Rows.Clear();

            foreach (Peak peak in peaks.OrderBy(item => item != null ? item.Energy : Double.MaxValue))
            {
                if (peak == null)
                {
                    continue;
                }

                PeakDeconvolutionInfo info = peak.DeconvolutionInfo;
                if (info == null)
                {
                    continue;
                }

                this.dataGridViewDetails.Rows.Add(
                    peak.Nuclide != null ? peak.Nuclide.Name : Resources.UnknownNuclide,
                    FormatMetric(peak.Energy, "0.00"),
                    peak.Channel.ToString(CultureInfo.InvariantCulture),
                    FormatMetric(peak.SNR, "0.0"),
                    FormatMetric(peak.FWHM, "0.0"),
                    FormatMetric(info.DevianceImprovement, "0.###"),
                    FormatMetric(info.PosteriorOccupancy, "0.###"),
                    FormatMetric(info.CenterStdDev, "0.###"),
                    FormatMetric(info.ResidualSnr, "0.###"),
                    FormatMetric(info.ResidualCorrelation, "0.###"),
                    FormatMetric(info.AnchorDistanceFwhm, "0.###"),
                    info.SupportingChainCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        void buttonCopyDiagnostics_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(this.BuildDiagnosticsText());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        string BuildDiagnosticsText()
        {
            StringBuilder builder = new StringBuilder(16384);
            CultureInfo culture = CultureInfo.InvariantCulture;
            ResultData activeResultData = this.document != null ? this.document.ActiveResultData : null;
            FWHMPeakDetectionMethodConfig peakConfig = activeResultData != null
                ? activeResultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig
                : null;
            RjmcmcConfig rjmcmcConfig = peakConfig != null
                ? RjmcmcConfig.CreateForRoiSearch(peakConfig)
                : null;

            builder.AppendLine("# Peak Deconvolution Diagnostics");
            AppendKeyValue(builder, "generated_at_utc", DateTime.UtcNow.ToString("O", culture));
            AppendKeyValue(builder, "assembly_version", Assembly.GetExecutingAssembly().GetName().Version?.ToString());
            AppendKeyValue(builder, "ui_culture", CultureInfo.CurrentUICulture.Name);
            builder.AppendLine();

            builder.AppendLine("[document]");
            AppendKeyValue(builder, "document.filename", this.document != null ? this.document.Filename : null);
            AppendKeyValue(builder, "document.is_named", this.document != null ? this.document.IsNamed.ToString(culture) : null);
            AppendKeyValue(builder, "document.result_data_format_version", this.document?.ResultDataFile?.FormatVersion);
            AppendKeyValue(builder, "document.result_count", this.document?.ResultDataFile?.ResultDataList?.Count);
            AppendKeyValue(builder, "document.active_result_index", this.document != null ? this.document.ActiveResultDataIndex : 0);
            builder.AppendLine();

            builder.AppendLine("[view]");
            AppendKeyValue(builder, "view.background_mode", this.document?.EnergySpectrumView?.BackgroundMode.ToString());
            AppendKeyValue(builder, "view.smoothing_method", this.document?.EnergySpectrumView?.SmoothingMethod.ToString());
            AppendKeyValue(builder, "view.peak_mode", this.document?.EnergySpectrumView?.PeakMode.ToString());
            AppendKeyValue(builder, "view.nuclide_set_name", this.selectedNuclideSet != null ? this.selectedNuclideSet.Name : "ALL");
            AppendKeyValue(builder, "view.nuclide_set_id", this.selectedNuclideSet != null ? this.selectedNuclideSet.Id.ToString() : null);
            AppendKeyValue(builder, "view.hide_unknown_peaks", this.selectedNuclideSet != null ? this.selectedNuclideSet.HideUnknownPeaks.ToString(culture) : "false");
            builder.AppendLine();

            builder.AppendLine("[result_data]");
            AppendKeyValue(builder, "result_data.sample_name", activeResultData?.SampleInfo?.Name);
            AppendKeyValue(builder, "result_data.visible", activeResultData != null ? activeResultData.Visible.ToString(culture) : null);
            AppendKeyValue(builder, "result_data.start_time", activeResultData != null ? activeResultData.StartTime.ToString("O", culture) : null);
            AppendKeyValue(builder, "result_data.end_time", activeResultData != null ? activeResultData.EndTime.ToString("O", culture) : null);
            AppendKeyValue(builder, "result_data.preset_time_s", activeResultData != null ? activeResultData.PresetTime : 0);
            AppendKeyValue(builder, "result_data.device_config_name", activeResultData?.DeviceConfig?.Name);
            AppendKeyValue(builder, "result_data.device_config_guid", activeResultData?.DeviceConfig?.Guid);
            AppendKeyValue(builder, "result_data.device_type", activeResultData?.DeviceConfig?.DeviceType);
            AppendKeyValue(builder, "result_data.device_config_reference_guid", activeResultData?.DeviceConfigReference?.Guid);
            AppendKeyValue(builder, "result_data.roi_config_reference_guid", activeResultData?.ROIConfigReference?.Guid);
            AppendKeyValue(builder, "result_data.background_spectrum_file", activeResultData?.BackgroundSpectrumFile);
            AppendKeyValue(builder, "result_data.background_spectrum_pathname", activeResultData?.BackgroundSpectrumPathname);
            builder.AppendLine();

            AppendSpectrumSection(builder, "foreground_spectrum", activeResultData?.EnergySpectrum);
            AppendSpectrumSection(builder, "background_spectrum", activeResultData?.BackgroundEnergySpectrum);
            AppendPeakDetectionConfig(builder, peakConfig);
            AppendRjmcmcConfig(builder, rjmcmcConfig);
            AppendEnergyCalibration(builder, activeResultData?.EnergySpectrum?.EnergyCalibration);
            AppendFwhmCalibration(builder, activeResultData?.FwhmCalibration);
            AppendDetectedPeaks(builder, activeResultData?.DetectedPeaks);
            AppendDeconvolutionPeaks(builder, this.displayedPeaks, rjmcmcConfig);

            return builder.ToString();
        }

        void AppendSpectrumSection(StringBuilder builder, string sectionName, EnergySpectrum spectrum)
        {
            builder.AppendLine("[" + sectionName + "]");
            AppendKeyValue(builder, sectionName + ".present", (spectrum != null).ToString(CultureInfo.InvariantCulture));
            if (spectrum != null)
            {
                AppendKeyValue(builder, sectionName + ".number_of_channels", spectrum.NumberOfChannels);
                AppendKeyValue(builder, sectionName + ".channel_pitch", spectrum.ChannelPitch);
                AppendKeyValue(builder, sectionName + ".measurement_time_s", spectrum.MeasurementTime);
                AppendKeyValue(builder, sectionName + ".live_time_s", spectrum.LiveTime);
                AppendKeyValue(builder, sectionName + ".total_pulse_count", spectrum.TotalPulseCount);
                AppendKeyValue(builder, sectionName + ".valid_pulse_count", spectrum.ValidPulseCount);
            }
            builder.AppendLine();
        }

        void AppendPeakDetectionConfig(StringBuilder builder, FWHMPeakDetectionMethodConfig peakConfig)
        {
            builder.AppendLine("[peak_detection_config]");
            if (peakConfig == null)
            {
                AppendKeyValue(builder, "peak_detection_config.present", "false");
                builder.AppendLine();
                return;
            }

            AppendKeyValue(builder, "peak_detection_config.present", "true");
            AppendKeyValue(builder, "peak_detection_config.type", peakConfig.GetType().FullName);
            AppendKeyValue(builder, "peak_detection_config.enabled", peakConfig.Enabled.ToString(CultureInfo.InvariantCulture));
            AppendKeyValue(builder, "peak_detection_config.use_deconvolution", peakConfig.UseDeconvolution.ToString(CultureInfo.InvariantCulture));
            AppendKeyValue(builder, "peak_detection_config.min_snr", peakConfig.Min_SNR);
            AppendKeyValue(builder, "peak_detection_config.max_items", peakConfig.Max_Items);
            AppendKeyValue(builder, "peak_detection_config.tolerance_percent", peakConfig.Tolerance);
            AppendKeyValue(builder, "peak_detection_config.min_range_keV", peakConfig.Min_Range);
            AppendKeyValue(builder, "peak_detection_config.max_range_keV", peakConfig.Max_Range);
            AppendKeyValue(builder, "peak_detection_config.min_fwhm_tol_percent", peakConfig.Min_FWHM_Tol);
            AppendKeyValue(builder, "peak_detection_config.max_fwhm_tol_percent", peakConfig.Max_FWHM_Tol);
            AppendKeyValue(builder, "peak_detection_config.channel_concat", peakConfig.Ch_Concat);
            AppendKeyValue(builder, "peak_detection_config.burn_in", peakConfig.BurnIn);
            AppendKeyValue(builder, "peak_detection_config.samples", peakConfig.Samples);
            AppendKeyValue(builder, "peak_detection_config.max_rois", peakConfig.MaxRois);
            AppendKeyValue(builder, "peak_detection_config.max_extra_peaks_per_roi", peakConfig.MaxExtraPeaksPerRoi);
            AppendKeyValue(builder, "peak_detection_config.roi_radius_fwhm", peakConfig.RoiRadiusFwhm);
            AppendKeyValue(builder, "peak_detection_config.min_deviance_improvement", peakConfig.MinDevianceImprovement);
            AppendKeyValue(builder, "peak_detection_config.minimum_candidate_amplitude", peakConfig.MinimumCandidateAmplitude);
            builder.AppendLine();
        }

        void AppendRjmcmcConfig(StringBuilder builder, RjmcmcConfig config)
        {
            builder.AppendLine("[rjmcmc_config_resolved]");
            if (config == null)
            {
                AppendKeyValue(builder, "rjmcmc_config_resolved.present", "false");
                builder.AppendLine();
                return;
            }

            AppendKeyValue(builder, "rjmcmc_config_resolved.present", "true");
            AppendKeyValue(builder, "rjmcmc_config_resolved.enabled", config.Enabled.ToString(CultureInfo.InvariantCulture));
            AppendKeyValue(builder, "rjmcmc_config_resolved.burn_in", config.BurnIn);
            AppendKeyValue(builder, "rjmcmc_config_resolved.samples", config.Samples);
            AppendKeyValue(builder, "rjmcmc_config_resolved.seed", config.Seed);
            AppendKeyValue(builder, "rjmcmc_config_resolved.max_rois", config.MaxRois);
            AppendKeyValue(builder, "rjmcmc_config_resolved.max_channels_per_roi", config.MaxChannelsPerRoi);
            AppendKeyValue(builder, "rjmcmc_config_resolved.max_extra_peaks_per_roi", config.MaxExtraPeaksPerRoi);
            AppendKeyValue(builder, "rjmcmc_config_resolved.max_anchors_per_roi", config.MaxAnchorsPerRoi);
            AppendKeyValue(builder, "rjmcmc_config_resolved.roi_radius_fwhm", config.RoiRadiusFwhm);
            AppendKeyValue(builder, "rjmcmc_config_resolved.center_update_sigma_fwhm", config.CenterUpdateSigmaFwhm);
            AppendKeyValue(builder, "rjmcmc_config_resolved.background_update_fraction", config.BackgroundUpdateFraction);
            AppendKeyValue(builder, "rjmcmc_config_resolved.target_snr", config.TargetSnr);
            AppendKeyValue(builder, "rjmcmc_config_resolved.extra_snr_multiplier", config.ExtraSnrMultiplier);
            AppendKeyValue(builder, "rjmcmc_config_resolved.min_deviance_improvement", config.MinDevianceImprovement);
            AppendKeyValue(builder, "rjmcmc_config_resolved.extra_peak_penalty", config.ExtraPeakPenalty);
            AppendKeyValue(builder, "rjmcmc_config_resolved.minimum_candidate_amplitude", config.MinimumCandidateAmplitude);
            AppendKeyValue(builder, "rjmcmc_config_resolved.minimum_supporting_chains", config.MinimumSupportingChains);
            AppendKeyValue(builder, "rjmcmc_config_resolved.supporting_deviance_improvement", config.SupportingDevianceImprovement);
            AppendKeyValue(builder, "rjmcmc_config_resolved.supporting_snr_fraction", config.SupportingSnrFraction);
            AppendKeyValue(builder, "rjmcmc_config_resolved.support_center_tolerance_fwhm", config.SupportCenterToleranceFwhm);
            AppendKeyValue(builder, "rjmcmc_config_resolved.support_center_tolerance_max_channels", config.SupportCenterToleranceMaxChannels);
            AppendKeyValue(builder, "rjmcmc_config_resolved.profile_optimization_iterations", config.ProfileOptimizationIterations);
            AppendKeyValue(builder, "rjmcmc_config_resolved.preselection_snr_fraction", config.PreselectionSnrFraction);
            AppendKeyValue(builder, "rjmcmc_config_resolved.residual_matched_snr_fraction", config.ResidualMatchedSnrFraction);
            AppendKeyValue(builder, "rjmcmc_config_resolved.minimum_residual_profile_correlation", config.MinimumResidualProfileCorrelation);
            AppendKeyValue(builder, "rjmcmc_config_resolved.minimum_posterior_occupancy", config.MinimumPosteriorOccupancy);
            AppendKeyValue(builder, "rjmcmc_config_resolved.maximum_posterior_center_std_dev_fwhm", config.MaximumPosteriorCenterStdDevFwhm);
            AppendKeyValue(builder, "rjmcmc_config_resolved.close_anchor_distance_fwhm", config.CloseAnchorDistanceFwhm);
            AppendKeyValue(builder, "rjmcmc_config_resolved.close_anchor_minimum_supporting_chains", config.CloseAnchorMinimumSupportingChains);
            AppendKeyValue(builder, "rjmcmc_config_resolved.close_anchor_minimum_posterior_occupancy", config.CloseAnchorMinimumPosteriorOccupancy);
            AppendKeyValue(builder, "rjmcmc_config_resolved.close_anchor_minimum_residual_profile_correlation", config.CloseAnchorMinimumResidualProfileCorrelation);
            AppendKeyValue(builder, "rjmcmc_config_resolved.chain_count", config.ChainCount);
            AppendKeyValue(builder, "rjmcmc_config_resolved.max_degree_of_parallelism", config.MaxDegreeOfParallelism);
            builder.AppendLine();
        }

        void AppendEnergyCalibration(StringBuilder builder, EnergyCalibration calibration)
        {
            builder.AppendLine("[energy_calibration]");
            AppendKeyValue(builder, "energy_calibration.present", (calibration != null).ToString(CultureInfo.InvariantCulture));
            if (calibration != null)
            {
                AppendKeyValue(builder, "energy_calibration.type", calibration.GetType().FullName);
                AppendKeyValue(builder, "energy_calibration.formula", calibration.ToString());
                if (calibration is PolynomialEnergyCalibration polynomial)
                {
                    AppendKeyValue(builder, "energy_calibration.polynomial_order", polynomial.PolynomialOrder);
                    AppendKeyValue(builder, "energy_calibration.coefficients", JoinDoubles(polynomial.Coefficients));
                    AppendKeyValue(builder, "energy_calibration.max_channels", polynomial.MaxChannels());
                }
            }
            builder.AppendLine();
        }

        void AppendFwhmCalibration(StringBuilder builder, FwhmCalibration calibration)
        {
            builder.AppendLine("[fwhm_calibration]");
            AppendKeyValue(builder, "fwhm_calibration.present", (calibration != null).ToString(CultureInfo.InvariantCulture));
            if (calibration != null)
            {
                AppendKeyValue(builder, "fwhm_calibration.type", calibration.GetType().FullName);
                AppendKeyValue(builder, "fwhm_calibration.formula", calibration.ToString());
                AppendKeyValue(builder, "fwhm_calibration.peak_type", calibration.PeakType);
                AppendKeyValue(builder, "fwhm_calibration.peak_type_name", DescribePeakType(calibration.PeakType));
                AppendKeyValue(builder, "fwhm_calibration.exp_gauss_exp_left_tail", calibration.ExpGaussExpLeftTail);
                AppendKeyValue(builder, "fwhm_calibration.exp_gauss_exp_right_tail", calibration.ExpGaussExpRightTail);
                AppendKeyValue(builder, "fwhm_calibration.voigt_sigma", calibration.VoigtSigma);
                AppendKeyValue(builder, "fwhm_calibration.voigt_gamma", calibration.VoigtGamma);
                AppendKeyValue(builder, "fwhm_calibration.gaussian_chi2_total", calibration.GaussianChi2Total);
                AppendKeyValue(builder, "fwhm_calibration.exp_gauss_exp_chi2_total", calibration.ExpGaussExpChi2Total);
                AppendKeyValue(builder, "fwhm_calibration.voigt_chi2_total", calibration.VoigtChi2Total);
                AppendKeyValue(builder, "fwhm_calibration.chi2_per_ndp", calibration.Chi2pNdp);
                AppendKeyValue(builder, "fwhm_calibration.coefficients", JoinDoubles(calibration.Coefficients));
                AppendKeyValue(builder, "fwhm_calibration.calibration_peak_count", calibration.CalibrationPeaks != null ? calibration.CalibrationPeaks.Count : 0);

                IList<CalibrationPeak> calibrationPeaks = calibration.CalibrationPeaks ?? new List<CalibrationPeak>();
                for (int i = 0; i < calibrationPeaks.Count; i++)
                {
                    CalibrationPeak peak = calibrationPeaks[i];
                    string prefix = "fwhm_calibration.calibration_peaks[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    AppendKeyValue(builder, prefix + ".channel", peak.Channel);
                    AppendKeyValue(builder, prefix + ".energy_keV", peak.Energy);
                    AppendKeyValue(builder, prefix + ".fwhm_ch", peak.FWHM);
                }
            }
            builder.AppendLine();
        }

        void AppendDetectedPeaks(StringBuilder builder, IEnumerable<Peak> peaks)
        {
            List<Peak> peakList = (peaks ?? Enumerable.Empty<Peak>())
                .Where(peak => peak != null)
                .OrderBy(peak => peak.Channel)
                .ToList();

            builder.AppendLine("[detected_peaks]");
            AppendKeyValue(builder, "detected_peaks.count", peakList.Count);
            for (int i = 0; i < peakList.Count; i++)
            {
                Peak peak = peakList[i];
                string prefix = "detected_peaks[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                AppendPeak(builder, prefix, peak, null);
            }
            builder.AppendLine();
        }

        void AppendDeconvolutionPeaks(StringBuilder builder, IEnumerable<Peak> peaks, RjmcmcConfig config)
        {
            List<Peak> peakList = (peaks ?? Enumerable.Empty<Peak>())
                .Where(peak => peak != null && peak.DeconvolutionInfo != null)
                .OrderBy(peak => peak.Channel)
                .ToList();

            builder.AppendLine("[deconvolution_peaks]");
            AppendKeyValue(builder, "deconvolution_peaks.count", peakList.Count);
            for (int i = 0; i < peakList.Count; i++)
            {
                Peak peak = peakList[i];
                string prefix = "deconvolution_peaks[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                AppendPeak(builder, prefix, peak, config);
            }
            builder.AppendLine();
        }

        void AppendPeak(StringBuilder builder, string prefix, Peak peak, RjmcmcConfig config)
        {
            PeakDeconvolutionInfo info = peak.DeconvolutionInfo;
            AppendKeyValue(builder, prefix + ".origin", peak.PeakSearchOrigin.ToString());
            AppendKeyValue(builder, prefix + ".nuclide_name", peak.Nuclide != null ? peak.Nuclide.Name : Resources.UnknownNuclide);
            AppendKeyValue(builder, prefix + ".nuclide_energy_keV", peak.Nuclide != null ? peak.Nuclide.Energy : Double.NaN);
            AppendKeyValue(builder, prefix + ".energy_keV", peak.Energy);
            AppendKeyValue(builder, prefix + ".channel", peak.Channel);
            AppendKeyValue(builder, prefix + ".snr", peak.SNR);
            AppendKeyValue(builder, prefix + ".fwhm_ch", peak.FWHM);
            AppendKeyValue(builder, prefix + ".fwhm_delta_ch", peak.FWHM_DELTA);
            if (info == null)
            {
                return;
            }

            AppendKeyValue(builder, prefix + ".deconvolution.amplitude", info.Amplitude);
            AppendKeyValue(builder, prefix + ".deconvolution.deviance_improvement", info.DevianceImprovement);
            AppendKeyValue(builder, prefix + ".deconvolution.posterior_occupancy", info.PosteriorOccupancy);
            AppendKeyValue(builder, prefix + ".deconvolution.center_std_dev_ch", info.CenterStdDev);
            AppendKeyValue(builder, prefix + ".deconvolution.residual_snr", info.ResidualSnr);
            AppendKeyValue(builder, prefix + ".deconvolution.residual_correlation", info.ResidualCorrelation);
            AppendKeyValue(builder, prefix + ".deconvolution.anchor_distance_fwhm", info.AnchorDistanceFwhm);
            AppendKeyValue(builder, prefix + ".deconvolution.supporting_chain_count", info.SupportingChainCount);
            AppendKeyValue(builder, prefix + ".deconvolution.minimum_snr_threshold", info.MinimumSnrThreshold);
            AppendKeyValue(builder, prefix + ".deconvolution.match_tolerance_percent", info.MatchTolerancePercent);
            AppendKeyValue(builder, prefix + ".deconvolution.roi_start_channel", info.RoiStartChannel);
            AppendKeyValue(builder, prefix + ".deconvolution.roi_end_channel", info.RoiEndChannel);
            AppendKeyValue(builder, prefix + ".deconvolution.local_anchor_channels", JoinInts(info.LocalAnchorChannels));
            AppendKeyValue(builder, prefix + ".deconvolution.reference_anchor_channels", JoinInts(info.ReferenceAnchorChannels));
            AppendKeyValue(builder, prefix + ".deconvolution.support_tolerance_channels", CalculateSupportToleranceChannels(peak.FWHM, config));
        }

        static int CalculateSupportToleranceChannels(double fwhm, RjmcmcConfig config)
        {
            if (config == null)
            {
                return 0;
            }

            if (Double.IsNaN(fwhm) || Double.IsInfinity(fwhm) || fwhm <= 0.0)
            {
                fwhm = 1.0;
            }

            int tolerance = Convert.ToInt32(Math.Ceiling(config.SupportCenterToleranceFwhm * fwhm));
            tolerance = Math.Max(2, tolerance);
            return Math.Min(config.SupportCenterToleranceMaxChannels, tolerance);
        }

        static string DescribePeakType(int peakType)
        {
            switch (peakType)
            {
                case FwhmCalibration.GaussianPeakType:
                    return "Gaussian";
                case FwhmCalibration.ExpGaussExpPeakType:
                    return "ExpGaussianExp";
                case FwhmCalibration.VoigtPeakType:
                    return "Voigt";
                default:
                    return "Unknown";
            }
        }

        static string JoinInts(IEnumerable<int> values)
        {
            return values == null
                ? "[]"
                : "[" + String.Join(", ", values.Select(value => value.ToString(CultureInfo.InvariantCulture))) + "]";
        }

        static string JoinDoubles(IEnumerable<double> values)
        {
            return values == null
                ? "[]"
                : "[" + String.Join(", ", values.Select(FormatRawMetric)) + "]";
        }

        static void AppendKeyValue(StringBuilder builder, string key, object value)
        {
            builder.Append(key);
            builder.Append('=');

            if (value == null)
            {
                builder.AppendLine();
                return;
            }

            switch (value)
            {
                case string text:
                    builder.AppendLine(text);
                    return;
                case bool booleanValue:
                    builder.AppendLine(booleanValue ? "true" : "false");
                    return;
                case DateTime dateTimeValue:
                    builder.AppendLine(dateTimeValue.ToString("O", CultureInfo.InvariantCulture));
                    return;
                case int intValue:
                    builder.AppendLine(intValue.ToString(CultureInfo.InvariantCulture));
                    return;
                case long longValue:
                    builder.AppendLine(longValue.ToString(CultureInfo.InvariantCulture));
                    return;
                case decimal decimalValue:
                    builder.AppendLine(decimalValue.ToString(CultureInfo.InvariantCulture));
                    return;
                case double doubleValue:
                    builder.AppendLine(FormatRawMetric(doubleValue));
                    return;
                case float floatValue:
                    builder.AppendLine(floatValue.ToString("R", CultureInfo.InvariantCulture));
                    return;
            }

            builder.AppendLine(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        static string FormatRawMetric(double value)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value)
                ? "n/a"
                : value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string FormatMetric(double value, string format)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value)
                ? "n/a"
                : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
