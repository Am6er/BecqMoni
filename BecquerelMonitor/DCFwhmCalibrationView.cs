using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using XPTable.Editors;
using XPTable.Models;

namespace BecquerelMonitor
{
    public partial class DCFwhmCalibrationView : ToolWindow
    {
        MainForm mainForm;

        FwhmCalibration fwhmCalibration;

        int lastSelectedIndex = -1;

        int minPeaksRequirement = 2;

        bool peakPickupProcessing = false;

        bool calibrationDone = true;
        bool updatingCurveSelection;

        string expGaussExpLeftParameterLabelText;
        string expGaussExpRightParameterLabelText;

        public DCFwhmCalibrationView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
            channelColumn.Alignment = ColumnAlignment.Right;
            energyColumn.Alignment = ColumnAlignment.Right;
            fwhmColumn.Alignment = ColumnAlignment.Right;
            executeCalibrationButton.Enabled = false;
            expGaussExpLeftParameterLabelText = peakShapeFirstParameterLabel.Text;
            expGaussExpRightParameterLabelText = peakShapeSecondParameterLabel.Text;
        }

        public void UpdateFwhmCalibration(bool reset_state = false)
        {
            // TODO проверить в mainForm на избыточность событий.
            // Нужны события, когда только калибровка меняется.
            // Нужно на финальном этапе смотреть
            if (reset_state)
            {
                calibrationDone = true;
                ClearPeakPickupState();
            }
            if (mainForm.ActiveDocument != null)
            {
                ResultData activeResultData = mainForm.ActiveDocument.ActiveResultData;
                EnsureFwhmCalibration(activeResultData);
                fwhmCalibration = activeResultData != null ? activeResultData.FwhmCalibration : null;
                if (fwhmCalibration == null)
                {
                    tableModel1.Rows.Clear();
                    calibrationProcessingPanel.Hide();
                    UpdateCalibrateButtonState();
                    return;
                }
                UpdateData();
            }
        }

        void EnsureFwhmCalibration(ResultData resultData)
        {
            if (resultData == null || resultData.FwhmCalibration != null)
            {
                return;
            }

            FWHMPeakDetectionMethodConfig cfg = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            EnergyCalibration energyCalibration = resultData.EnergySpectrum != null ? resultData.EnergySpectrum.EnergyCalibration : null;
            if (cfg == null || energyCalibration == null)
            {
                return;
            }

            if (cfg.FwhmCalibration == null)
            {
                cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, energyCalibration);
            }

            if (cfg.FwhmCalibration != null)
            {
                resultData.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
        }

        private void DCFwhmCalibrationView_FormLoad(object sender, EventArgs e)
        {

        }

        private void DCFwhmCalibrationView_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClearPeakPickupState();
        }

        void UpdateData()
        {
            UpdateTable();
            if (fwhmCalibration.CalibrationPeaks.Count > 0)
            {
                calibrationProcessingPanel.Show();
                UpdateSelectedCurveInfo();
            } else
            {
                calibrationProcessingPanel.Hide();
            }
            UpdateCalibrateButtonState();
        }

        void UpdateTable()
        {
            CollectedPeaksTable.SuspendLayout();
            tableModel1.Rows.Clear();
            fwhmCalibration.CalibrationPeaks.Sort();
            int position = 1;
            foreach (CalibrationPeak calibrationPeak in fwhmCalibration.CalibrationPeaks)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(position.ToString()));
                row.Cells.Add(new Cell(calibrationPeak.Channel));
                row.Cells.Add(new Cell(calibrationPeak.Energy));
                row.Cells.Add(new Cell(calibrationPeak.FWHM));
                tableModel1.Rows.Add(row);
                position++;
            }
            CollectedPeaksTable.ResumeLayout();
        }

        void UpdateSelectedCurveInfo()
        {
            int targetSelectedIndex = fwhmCalibration is SimpleSqrtFwhmCalibration
                ? (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration
                : (int)FwhmCalibration.FwhmCalibrationCurve.SqrtFwhmCalibration;

            if (selectCurveComboBox.SelectedIndex != targetSelectedIndex)
            {
                updatingCurveSelection = true;
                try
                {
                    selectCurveComboBox.SelectedIndex = targetSelectedIndex;
                }
                finally
                {
                    updatingCurveSelection = false;
                }
            }

            curveFormulaLabel.Text = fwhmCalibration.GetFormula();
            minPeaksRequirement = fwhmCalibration.MinPeaksRequirement();
            minPeaksRequirementLabel.Text = String.Format(Resources.MinPeaksRequirement, minPeaksRequirement);
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
            UpdatePeakShapeInfo();
        }

        void UpdatePeakShapeInfo()
        {
            int peakType = FwhmCalibration.IsSupportedPeakType(fwhmCalibration.PeakType)
                ? fwhmCalibration.PeakType
                : FwhmCalibration.GaussianPeakType;
            bool showParameters = peakType != FwhmCalibration.GaussianPeakType;

            peakShapeTypeValueLabel.Text = GetPeakShapeName(peakType);
            peakShapeFirstParameterLabel.Visible = showParameters;
            peakShapeFirstParameterValueLabel.Visible = showParameters;
            peakShapeSecondParameterLabel.Visible = showParameters;
            peakShapeSecondParameterValueLabel.Visible = showParameters;

            if (!showParameters)
            {
                return;
            }

            if (peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                peakShapeFirstParameterLabel.Text = expGaussExpLeftParameterLabelText;
                peakShapeSecondParameterLabel.Text = expGaussExpRightParameterLabelText;
                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.ExpGaussExpLeftTail.ToString("0.0");
                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.ExpGaussExpRightTail.ToString("0.0");
            }
            else
            {
                peakShapeFirstParameterLabel.Text = Resources.ResourceManager.GetString("VoigtRelativeSigmaLabel");
                peakShapeSecondParameterLabel.Text = Resources.ResourceManager.GetString("VoigtRelativeGammaLabel");
                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.VoigtSigma.ToString("0.0");
                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.VoigtGamma.ToString("0.0");
            }
        }

        internal static string GetPeakShapeName(int peakType)
        {
            switch (peakType)
            {
                case FwhmCalibration.ExpGaussExpPeakType:
                    return Resources.ResourceManager.GetString("PeakShapeExpGaussExp");
                case FwhmCalibration.VoigtPeakType:
                    return Resources.ResourceManager.GetString("PeakShapeVoigt");
                default:
                    return Resources.ResourceManager.GetString("PeakShapeGaussian");
            }
        }

        void SelectCurveComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updatingCurveSelection || selectCurveComboBox.SelectedIndex == -1 || selectCurveComboBox.SelectedIndex == lastSelectedIndex || lastSelectedIndex == -1)
            {
                lastSelectedIndex = selectCurveComboBox.SelectedIndex;
                return;
            }

            FwhmCalibration previousCalibration = fwhmCalibration;
            if (selectCurveComboBox.SelectedIndex == (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration)
            {
                fwhmCalibration = new SimpleSqrtFwhmCalibration { CalibrationPeaks = fwhmCalibration.ClonePeaks() };
            } else
            {
                fwhmCalibration = new SqrtFwhmCalibration { CalibrationPeaks = fwhmCalibration.ClonePeaks() };
            }

            CopyPeakShapeSettings(previousCalibration, fwhmCalibration);
            UpdateSelectedCurveInfo();
            UpdateCalibrateButtonState();
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
        }

        static void CopyPeakShapeSettings(FwhmCalibration source, FwhmCalibration target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.PeakType = source.PeakType;
            target.ExpGaussExpLeftTail = source.ExpGaussExpLeftTail;
            target.ExpGaussExpRightTail = source.ExpGaussExpRightTail;
            target.VoigtSigma = source.VoigtSigma;
            target.VoigtGamma = source.VoigtGamma;
        }

        void UpdateCalibrateButtonState()
        {
            bool hasCalibration = fwhmCalibration != null;

            if (!hasCalibration ||
                selectCurveComboBox.SelectedIndex == -1 ||
                (CollectedPeaksTable.SelectedItems.Length > 0 && CollectedPeaksTable.SelectedItems[0].Index < 0) ||
                (lastSelectedIndex == selectCurveComboBox.SelectedIndex && calibrationDone) ||
                minPeaksRequirement > tableModel1.Rows.Count)
            {
                executeCalibrationButton.Enabled = false;
            } else
            {
                executeCalibrationButton.Enabled = true;
            }
            if (!hasCalibration || minPeaksRequirement > tableModel1.Rows.Count)
            {
                saveToDeviceCfgButton.Enabled = false;
            }
            else
            {
                saveToDeviceCfgButton.Enabled = true;
            }
            if (hasCalibration && isCalibrationPeaksExist() > 1)
            {
                getAllPeaksButton.Enabled = true;
            }
            else
            {
                getAllPeaksButton.Enabled = false;
            }
        }

        private void RemovePeakButton_Click(object sender, EventArgs e)
        {
            int selectedItemIndex;
            if (CollectedPeaksTable.SelectedItems.Length >= 1)
            {
                selectedItemIndex = CollectedPeaksTable.SelectedItems[0].Index;
                UpdateCalibrateButtonState();
            }
            else
            {
                selectedItemIndex = 0;
            }
            if (selectedItemIndex < 0 || selectedItemIndex >= fwhmCalibration.CalibrationPeaks.Count)
            {
                UpdateCalibrateButtonState();
                return;
            }
            try
            {
                mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks.RemoveAt(selectedItemIndex);
                mainForm.ActiveDocument.Dirty = true;
                calibrationDone = false;
            }
            catch { }
            tableModel1.Selections.Clear();
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
        }

        private void SaveToDeviceCfgButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Resources.MSGSaveFWHMCalibration, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            DeviceConfigInfo deviceConfig = activeDocument.ActiveResultData.DeviceConfig;
            if (deviceConfig == null || deviceConfig.Guid == null || deviceConfig.Guid == "")
            {
                MessageBox.Show(Resources.ERRDeviceConfigNotSelected, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            if (!fwhmCalibration.PerformCalibration(activeDocument.ActiveResultData.EnergySpectrum.Spectrum.Length))
            {
                // TODO нужно будет добавить обработку плохой калибровки
                MessageBox.Show(Resources.CalibrationFunctionError);
                return;
            }
            FWHMPeakDetectionMethodConfig peakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig) deviceConfig.PeakDetectionMethodConfig;
            peakDetectionMethodConfig.FwhmCalibration = fwhmCalibration.Clone();
            DeviceConfigManager.GetInstance().SaveConfig(activeDocument.ActiveResultData.DeviceConfig);
            activeDocument.ActiveResultData.FwhmCalibration = fwhmCalibration.Clone();
            mainForm.UpdateDeviceConfigForm();
        }

        private void AddPeakButton_Click(object sender, EventArgs e)
        {
            if (mainForm.ActiveDocument != null)
            {
                cancelAddPeakButton.Enabled = true;
                peakPickupProcessing = true;
                addPeakButton.Enabled = !peakPickupProcessing;
                mainForm.ActiveDocument.EnergySpectrumView.PeakPickuped += this.EnergySpectrumView_PeakPickuped;
            }
        }

        void EnergySpectrumView_PeakPickuped(object sender, PeakPickupedEventArgs e)
        {
            if (!peakPickupProcessing)
            {
                return;
            }

            CalibrationPeak newPeak = e.CalibrationPeak;
            foreach (CalibrationPeak peak in mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks)
            {
                if (peak.Equals(newPeak))
                {
                    string PeakExistText = String.Format(Resources.ERRPeakExist, peak.FWHM, peak.Channel);
                    MessageBox.Show(PeakExistText, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    return;
                }
            }
            // PeakFit
            SpectrumAriphmetics sa = new SpectrumAriphmetics(mainForm.ActiveDocument.ActiveResultData.EnergySpectrum);
            newPeak = sa.CalcPeakFitValues(newPeak,
                e.PeakStartSelection,
                e.PeakEndSelection);
            sa.Dispose();


            mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks.Add(newPeak);
            mainForm.ActiveDocument.Dirty = true;
            calibrationDone = false;
            ClearPeakPickupState();
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
        }

        void ClearPeakPickupState()
        {
            if (mainForm.ActiveDocument != null)
            {
                mainForm.ActiveDocument.EnergySpectrumView.PeakPickuped -= this.EnergySpectrumView_PeakPickuped;
            }
            peakPickupProcessing = false;
            addPeakButton.Enabled = !peakPickupProcessing;
            cancelAddPeakButton.Enabled = peakPickupProcessing;
            UpdateCalibrateButtonState();
        }

        private void ExecuteCalibrationButton_Click(object sender, EventArgs e)
        {
            if (!fwhmCalibration.PerformCalibration(mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum.Length))
            {
                // TODO нужно будет добавить обработку плохой калибровки
                MessageBox.Show(Resources.CalibrationFunctionError);
                return;
            }
            if (HasPeakShapeComparisonData(fwhmCalibration.CalibrationPeaks))
            {
                SelectGlobalPeakShape(fwhmCalibration.CalibrationPeaks);
            }

            mainForm.ActiveDocument.ActiveResultData.FwhmCalibration = fwhmCalibration.Clone();
            mainForm.ActiveDocument.Dirty = true;
            calibrationDone = true;
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
            mainForm.UpdateDetectedPeakView();
            mainForm.ActiveDocument.UpdateEnergySpectrum();
        }

        void SelectGlobalPeakShape(List<CalibrationPeak> peaks)
        {
            const int TailSteps = 50;
            int peakCount = peaks.Count;
            double gaussianChi2 = 0.0;
            int gaussianNdp = 0;
            bool hasGaussian = peakCount > 0;

            foreach (CalibrationPeak peak in peaks)
            {
                if (!IsValidFitStatistic(peak.GaussianChi2, peak.GaussianNdp))
                {
                    hasGaussian = false;
                    break;
                }

                gaussianChi2 += peak.GaussianChi2;
                gaussianNdp += peak.GaussianNdp;
            }

            double expGaussExpChi2 = Double.PositiveInfinity;
            int expGaussExpNdp = 0;
            int bestExpGaussExpCandidate = -1;
            double voigtChi2 = Double.PositiveInfinity;
            int voigtNdp = 0;
            int bestVoigtCandidate = -1;

            for (int candidateIndex = 0; candidateIndex < TailSteps * TailSteps; candidateIndex++)
            {
                double candidateChi2 = 0.0;
                int candidateNdp = 0;
                bool hasCandidateForEveryPeak = peakCount > 0;
                foreach (CalibrationPeak peak in peaks)
                {
                    if (peak.ExpGaussExpCandidateChi2 == null || peak.ExpGaussExpCandidateNdp == null ||
                        candidateIndex >= peak.ExpGaussExpCandidateChi2.Length ||
                        candidateIndex >= peak.ExpGaussExpCandidateNdp.Length ||
                        !IsValidFitStatistic(peak.ExpGaussExpCandidateChi2[candidateIndex], peak.ExpGaussExpCandidateNdp[candidateIndex]))
                    {
                        hasCandidateForEveryPeak = false;
                        break;
                    }

                    candidateChi2 += peak.ExpGaussExpCandidateChi2[candidateIndex];
                    candidateNdp += peak.ExpGaussExpCandidateNdp[candidateIndex];
                }

                if (hasCandidateForEveryPeak && candidateChi2 < expGaussExpChi2)
                {
                    expGaussExpChi2 = candidateChi2;
                    // The two tail parameters are shared by all peaks and are
                    // therefore subtracted once, not once per calibration peak.
                    expGaussExpNdp = candidateNdp + 2 * (peakCount - 1);
                    bestExpGaussExpCandidate = candidateIndex;
                }

                candidateChi2 = 0.0;
                candidateNdp = 0;
                hasCandidateForEveryPeak = peakCount > 0;
                foreach (CalibrationPeak peak in peaks)
                {
                    if (peak.VoigtCandidateChi2 == null || peak.VoigtCandidateNdp == null ||
                        candidateIndex >= peak.VoigtCandidateChi2.Length ||
                        candidateIndex >= peak.VoigtCandidateNdp.Length ||
                        !IsValidFitStatistic(peak.VoigtCandidateChi2[candidateIndex], peak.VoigtCandidateNdp[candidateIndex]))
                    {
                        hasCandidateForEveryPeak = false;
                        break;
                    }

                    candidateChi2 += peak.VoigtCandidateChi2[candidateIndex];
                    candidateNdp += peak.VoigtCandidateNdp[candidateIndex];
                }

                if (hasCandidateForEveryPeak && candidateChi2 < voigtChi2)
                {
                    voigtChi2 = candidateChi2;
                    // Sigma and gamma are also global shape parameters.
                    voigtNdp = candidateNdp + 2 * (peakCount - 1);
                    bestVoigtCandidate = candidateIndex;
                }
            }

            int peakType = FwhmCalibration.GaussianPeakType;
            double selectedChi2 = hasGaussian ? gaussianChi2 : Double.PositiveInfinity;
            int selectedNdp = hasGaussian ? gaussianNdp : 0;
            double selectedScore = hasGaussian
                ? GetPeakShapeAicScore(FwhmCalibration.GaussianPeakType, gaussianChi2)
                : Double.PositiveInfinity;
            if (bestExpGaussExpCandidate >= 0)
            {
                double expGaussExpScore = GetPeakShapeAicScore(FwhmCalibration.ExpGaussExpPeakType, expGaussExpChi2);
                if (expGaussExpScore < selectedScore)
                {
                    peakType = FwhmCalibration.ExpGaussExpPeakType;
                    selectedChi2 = expGaussExpChi2;
                    selectedNdp = expGaussExpNdp;
                    selectedScore = expGaussExpScore;
                }
            }
            if (bestVoigtCandidate >= 0)
            {
                double voigtScore = GetPeakShapeAicScore(FwhmCalibration.VoigtPeakType, voigtChi2);
                if (voigtScore < selectedScore)
                {
                    peakType = FwhmCalibration.VoigtPeakType;
                    selectedChi2 = voigtChi2;
                    selectedNdp = voigtNdp;
                    selectedScore = voigtScore;
                }
            }

            fwhmCalibration.PeakType = peakType;
            fwhmCalibration.ExpGaussExpLeftTail = 1.0;
            fwhmCalibration.ExpGaussExpRightTail = 1.0;
            fwhmCalibration.VoigtSigma = 1.0;
            fwhmCalibration.VoigtGamma = 1.0;
            fwhmCalibration.GaussianChi2Total = hasGaussian ? gaussianChi2 : -1.0;
            fwhmCalibration.ExpGaussExpChi2Total = bestExpGaussExpCandidate >= 0 ? expGaussExpChi2 : -1.0;
            fwhmCalibration.VoigtChi2Total = bestVoigtCandidate >= 0 ? voigtChi2 : -1.0;
            fwhmCalibration.Chi2pNdp = selectedNdp > 0 && !Double.IsInfinity(selectedChi2)
                ? selectedChi2 / selectedNdp
                : 0.0;

            if (peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                fwhmCalibration.ExpGaussExpLeftTail = (bestExpGaussExpCandidate / TailSteps + 1) * 0.1;
                fwhmCalibration.ExpGaussExpRightTail = (bestExpGaussExpCandidate % TailSteps + 1) * 0.1;
            }
            else if (peakType == FwhmCalibration.VoigtPeakType)
            {
                fwhmCalibration.VoigtSigma = (bestVoigtCandidate / TailSteps + 1) * 0.1;
                fwhmCalibration.VoigtGamma = (bestVoigtCandidate % TailSteps + 1) * 0.1;
            }

            ShowGlobalPeakFitComparisonTable(
                peakType,
                hasGaussian ? gaussianChi2 : -1.0,
                hasGaussian ? gaussianNdp : -1,
                bestExpGaussExpCandidate >= 0 ? expGaussExpChi2 : -1.0,
                bestExpGaussExpCandidate >= 0 ? expGaussExpNdp : -1,
                bestExpGaussExpCandidate >= 0 ? GetExpGaussExpParametersText(bestExpGaussExpCandidate, TailSteps) : GetResourceText("PeakFitChiTableUnavailable", "n/a"),
                bestVoigtCandidate >= 0 ? voigtChi2 : -1.0,
                bestVoigtCandidate >= 0 ? voigtNdp : -1,
                bestVoigtCandidate >= 0 ? GetVoigtParametersText(bestVoigtCandidate, TailSteps) : GetResourceText("PeakFitChiTableUnavailable", "n/a"));
        }

        static bool IsValidFitStatistic(double chi2, int ndp)
        {
            return chi2 >= 0.0 && ndp > 0 && !Double.IsNaN(chi2) && !Double.IsInfinity(chi2);
        }

        static double GetPeakShapeAicScore(int peakType, double chi2)
        {
            if (Double.IsNaN(chi2) || Double.IsInfinity(chi2) || chi2 < 0.0)
            {
                return Double.PositiveInfinity;
            }

            // Relative AIC comparison: Gaussian is the baseline, while the other
            // families add two global shape parameters and therefore pay +4.
            switch (peakType)
            {
                case FwhmCalibration.ExpGaussExpPeakType:
                case FwhmCalibration.VoigtPeakType:
                    return chi2 + 4.0;
                default:
                    return chi2;
            }
        }

        static bool HasPeakShapeComparisonData(List<CalibrationPeak> peaks)
        {
            if (peaks == null || peaks.Count == 0)
            {
                return false;
            }

            foreach (CalibrationPeak peak in peaks)
            {
                if (!IsValidFitStatistic(peak.GaussianChi2, peak.GaussianNdp) ||
                    peak.ExpGaussExpCandidateChi2 == null || peak.ExpGaussExpCandidateNdp == null ||
                    peak.VoigtCandidateChi2 == null || peak.VoigtCandidateNdp == null)
                {
                    return false;
                }
            }

            return true;
        }

        void ShowGlobalPeakFitComparisonTable(
            int selectedPeakType,
            double gaussianChi2,
            int gaussianNdp,
            double expGaussExpChi2,
            int expGaussExpNdp,
            string expGaussExpParameters,
            double voigtChi2,
            int voigtNdp,
            string voigtParameters)
        {
            List<PeakFitComparisonItem> items = new List<PeakFitComparisonItem>
            {
                new PeakFitComparisonItem(FwhmCalibration.GaussianPeakType, GetPeakShapeName(FwhmCalibration.GaussianPeakType), gaussianChi2, gaussianNdp, "-"),
                new PeakFitComparisonItem(FwhmCalibration.ExpGaussExpPeakType, GetPeakShapeName(FwhmCalibration.ExpGaussExpPeakType), expGaussExpChi2, expGaussExpNdp, expGaussExpParameters),
                new PeakFitComparisonItem(FwhmCalibration.VoigtPeakType, GetPeakShapeName(FwhmCalibration.VoigtPeakType), voigtChi2, voigtNdp, voigtParameters)
            };

            items.Sort(delegate (PeakFitComparisonItem left, PeakFitComparisonItem right)
            {
                bool leftValid = !Double.IsNaN(left.Score) && !Double.IsInfinity(left.Score);
                bool rightValid = !Double.IsNaN(right.Score) && !Double.IsInfinity(right.Score);
                if (leftValid && rightValid)
                {
                    int scoreComparison = left.Score.CompareTo(right.Score);
                    if (scoreComparison != 0)
                    {
                        return scoreComparison;
                    }

                    return (left.Chi2 / left.Ndp).CompareTo(right.Chi2 / right.Ndp);
                }
                if (leftValid)
                {
                    return -1;
                }
                if (rightValid)
                {
                    return 1;
                }
                return String.Compare(left.CurveName, right.CurveName, StringComparison.CurrentCulture);
            });

            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.AppendFormat(
                GetResourceText("PeakFitChiTablePeakSummary", "Selected shape: {0}."),
                GetPeakShapeName(selectedPeakType));
            messageBuilder.AppendLine();
            messageBuilder.AppendLine();

            foreach (PeakFitComparisonItem item in items)
            {
                messageBuilder.AppendFormat(
                    "{0}: {1} = {2}; {3} = {4}; {5}: {6}",
                    item.CurveName,
                    GetResourceText("PeakFitChiTableScoreColumn", "Score"),
                    FormatPeakShapeScore(item.Score),
                    GetResourceText("PeakFitChiTableChi2PerNdpColumn", "chi2/ndp"),
                    FormatPeakFitRatio(item.Chi2, item.Ndp),
                    GetResourceText("PeakFitChiTableParametersColumn", "Parameters"),
                    item.Parameters);
                messageBuilder.AppendLine();
            }

            ShowWideMessage(
                GetResourceText("PeakFitChiTableTitle", "Peak fit comparison"),
                messageBuilder.ToString().TrimEnd());
        }

        string GetExpGaussExpParametersText(int candidateIndex, int tailSteps)
        {
            return String.Format(
                "{0}={1:0.0}; {2}={3:0.0}",
                expGaussExpLeftParameterLabelText,
                (candidateIndex / tailSteps + 1) * 0.1,
                expGaussExpRightParameterLabelText,
                (candidateIndex % tailSteps + 1) * 0.1);
        }

        string GetVoigtParametersText(int candidateIndex, int tailSteps)
        {
            return String.Format(
                "{0}={1:0.0}; {2}={3:0.0}",
                Resources.ResourceManager.GetString("VoigtRelativeSigmaLabel"),
                (candidateIndex / tailSteps + 1) * 0.1,
                Resources.ResourceManager.GetString("VoigtRelativeGammaLabel"),
                (candidateIndex % tailSteps + 1) * 0.1);
        }

        string FormatPeakFitRatio(double chi2, int ndp)
        {
            return IsValidFitStatistic(chi2, ndp)
                ? (chi2 / ndp).ToString("0.#####")
                : GetResourceText("PeakFitChiTableUnavailable", "n/a");
        }

        string FormatPeakShapeScore(double score)
        {
            return !Double.IsNaN(score) && !Double.IsInfinity(score)
                ? score.ToString("0.#####")
                : GetResourceText("PeakFitChiTableUnavailable", "n/a");
        }

        static string GetResourceText(string resourceName, string fallback)
        {
            string value = Resources.ResourceManager.GetString(resourceName);
            return String.IsNullOrEmpty(value) ? fallback : value;
        }

        void ShowWideMessage(string title, string message)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = title;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(490, 156);
                dialog.Font = this.Font;

                TextBox messageTextBox = new TextBox();
                messageTextBox.Dock = DockStyle.Fill;
                messageTextBox.Multiline = true;
                messageTextBox.ReadOnly = true;
                messageTextBox.BorderStyle = BorderStyle.None;
                messageTextBox.BackColor = SystemColors.Window;
                messageTextBox.WordWrap = true;
                messageTextBox.ScrollBars = ScrollBars.None;
                messageTextBox.TabStop = false;
                messageTextBox.Text = message;

                Panel contentPanel = new Panel();
                contentPanel.Dock = DockStyle.Top;
                contentPanel.Height = 112;
                contentPanel.Padding = new Padding(12, 10, 12, 0);
                contentPanel.Controls.Add(messageTextBox);

                FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
                buttonPanel.Dock = DockStyle.Bottom;
                buttonPanel.Height = 36;
                buttonPanel.FlowDirection = FlowDirection.RightToLeft;
                buttonPanel.Padding = new Padding(0, 6, 12, 6);
                buttonPanel.WrapContents = false;

                Button okButton = new Button();
                okButton.Text = GetResourceText("PeakFitChiTableCloseButton", "Close");
                okButton.DialogResult = DialogResult.OK;
                okButton.Size = new Size(90, 24);
                buttonPanel.Controls.Add(okButton);

                dialog.AcceptButton = okButton;
                dialog.CancelButton = okButton;
                dialog.Controls.Add(buttonPanel);
                dialog.Controls.Add(contentPanel);
                dialog.ShowDialog(this);
            }
        }

        sealed class PeakFitComparisonItem
        {
            public PeakFitComparisonItem(int peakType, string curveName, double chi2, int ndp, string parameters)
            {
                PeakType = peakType;
                CurveName = curveName;
                Chi2 = chi2;
                Ndp = ndp;
                Parameters = parameters;
                Score = GetPeakShapeAicScore(peakType, chi2);
            }

            public int PeakType { get; private set; }
            public string CurveName { get; private set; }
            public double Chi2 { get; private set; }
            public int Ndp { get; private set; }
            public string Parameters { get; private set; }
            public double Score { get; private set; }
        }

        private void CancelAddPeakButton_Click(object sender, EventArgs e)
        {
            ClearPeakPickupState();
        }

        private void CollectedPeaksTable_EditingStopped(object sender, XPTable.Events.CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;
            List<CalibrationPeak> calibrationPeaks = mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks;
            NumberCellEditor editor = (NumberCellEditor)e.Editor;
            string textvalue = editor.TextBox.Text;

            if (e.Column == 1)
            {
                calibrationPeaks[row.Index].Channel = int.Parse(textvalue);
            }
            else if (e.Column == 2)
            {
                calibrationPeaks[row.Index].Energy = double.Parse(textvalue);
            }
            else if (e.Column == 3)
            {
                calibrationPeaks[row.Index].FWHM = double.Parse(textvalue);
            } else
            {
                return;
            }
            mainForm.ActiveDocument.Dirty = true;
            calibrationDone = false;
            UpdateCalibrateButtonState();
        }

        int isCalibrationPeaksExist()
        {
            int count = 0;
            if (mainForm.DocumentList != null)
            {
                foreach (DocEnergySpectrum doc in mainForm.DocumentList)
                {
                    if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
                    {
                        continue;
                    }

                    foreach (ResultData data in doc.ResultDataFile.ResultDataList)
                    {
                        if (data == null || data.FwhmCalibration == null || data.FwhmCalibration.CalibrationPeaks == null)
                        {
                            continue;
                        }

                        count += data.FwhmCalibration.CalibrationPeaks.Count;
                    }
                }
            }
            return count;
        }

        private void GetAllPeaksButton_Click(object sender, EventArgs e)
        {
            List<CalibrationPeak> peaks = new List<CalibrationPeak>();
            Dictionary<int, double> peaksDict = new Dictionary<int, double>();
            ResultData activeResultData = mainForm.ActiveDocument != null ? mainForm.ActiveDocument.ActiveResultData : null;

            EnsureFwhmCalibration(activeResultData);
            if (activeResultData == null || activeResultData.FwhmCalibration == null)
            {
                UpdateCalibrateButtonState();
                return;
            }

            if (mainForm.DocumentList != null)
            {
                foreach (DocEnergySpectrum doc in mainForm.DocumentList)
                {
                    if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
                    {
                        continue;
                    }

                    foreach (ResultData data in doc.ResultDataFile.ResultDataList)
                    {
                        if (data != null &&
                            data.FwhmCalibration != null &&
                            data.FwhmCalibration.CalibrationPeaks != null &&
                            data.FwhmCalibration.CalibrationPeaks.Count > 0)
                        {
                            peaks.AddRange(data.FwhmCalibration.CalibrationPeaks);
                        }
                    }
                }
                if (peaks.Count > 0)
                {
                    activeResultData.FwhmCalibration.CalibrationPeaks.Clear();
                }
                foreach (CalibrationPeak peak in peaks)
                {
                    if (peaksDict.ContainsKey(peak.Channel))
                    {
                        continue;
                    }
                    else
                    {
                        peaksDict.Add(peak.Channel, peak.FWHM);
                        activeResultData.FwhmCalibration.CalibrationPeaks.Add(peak);
                        mainForm.ActiveDocument.Dirty = true;
                        calibrationDone = false;
                    }
                }
                UpdateFwhmCalibration();
                UpdateCalibrateButtonState();
            }
        }

        private void ViewCalibrationButton_Click(object sender, EventArgs e)
        {
            FWHMCalibrationGraph graph = new FWHMCalibrationGraph(this.mainForm);
            graph.Init(fwhmCalibration, mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.NumberOfChannels);
            DialogResult result = graph.ShowDialog();
            if (result == DialogResult.Yes)
            {
                calibrationDone = false;
                fwhmCalibration.CalibrationPeaks = CalibrationPeak.ClonePeaks(mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks);
                UpdateTable();
                UpdateCalibrateButtonState();
            }
        }

        private void ViewPeakShapeButton_Click(object sender, EventArgs e)
        {
            if (fwhmCalibration == null)
            {
                return;
            }

            PeakShapePreviewGraph graph = new PeakShapePreviewGraph(this.mainForm);
            graph.Init(fwhmCalibration, this.peakShapeInfoGroupBox.Text);
            graph.ShowDialog(this);
        }
    }
}
