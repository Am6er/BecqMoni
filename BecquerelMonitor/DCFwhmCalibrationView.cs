using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
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
                fwhmCalibration = mainForm.ActiveDocument.ActiveResultData.FwhmCalibration;
                UpdateData();
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
            if (selectCurveComboBox.SelectedIndex == -1)
            {
                if (fwhmCalibration is SimpleSqrtFwhmCalibration)
                {
                    selectCurveComboBox.SelectedIndex = (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration;
                }
                else
                {
                    selectCurveComboBox.SelectedIndex = (int)FwhmCalibration.FwhmCalibrationCurve.SqrtFwhmCalibration;
                }
            }

            if (fwhmCalibration is SimpleSqrtFwhmCalibration)
            {
                if (selectCurveComboBox.SelectedIndex == -1 ||
                    selectCurveComboBox.SelectedIndex != (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration)
                        selectCurveComboBox.SelectedIndex = (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration;
            }
            else
            {
                if (selectCurveComboBox.SelectedIndex == -1 ||
                    selectCurveComboBox.SelectedIndex != (int)FwhmCalibration.FwhmCalibrationCurve.SqrtFwhmCalibration)
                        selectCurveComboBox.SelectedIndex = (int)FwhmCalibration.FwhmCalibrationCurve.SqrtFwhmCalibration;
            }


            curveFormulaLabel.Text = fwhmCalibration.GetFormula();
            minPeaksRequirement = fwhmCalibration.MinPeaksRequirement();
            minPeaksRequirementLabel.Text = String.Format(Resources.MinPeaksRequirement, minPeaksRequirement);
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

        static string GetPeakShapeName(int peakType)
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
            if (selectCurveComboBox.SelectedIndex == -1 || selectCurveComboBox.SelectedIndex == lastSelectedIndex || lastSelectedIndex == -1)
            {
                lastSelectedIndex = selectCurveComboBox.SelectedIndex;
                return;
            }
            if (selectCurveComboBox.SelectedIndex == (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration)
            {
                fwhmCalibration = new SimpleSqrtFwhmCalibration { CalibrationPeaks = fwhmCalibration.ClonePeaks() };
            } else
            {
                fwhmCalibration = new SqrtFwhmCalibration { CalibrationPeaks = fwhmCalibration.ClonePeaks() };
            }
            UpdateSelectedCurveInfo();
            UpdateCalibrateButtonState();
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
        }

        void UpdateCalibrateButtonState()
        {
            if (selectCurveComboBox.SelectedIndex == -1 ||
                (CollectedPeaksTable.SelectedItems.Length > 0 && CollectedPeaksTable.SelectedItems[0].Index < 0) ||
                (lastSelectedIndex == selectCurveComboBox.SelectedIndex && calibrationDone) ||
                minPeaksRequirement > tableModel1.Rows.Count)
            {
                executeCalibrationButton.Enabled = false;
            } else
            {
                executeCalibrationButton.Enabled = true;
            }
            if (minPeaksRequirement > tableModel1.Rows.Count)
            {
                saveToDeviceCfgButton.Enabled = false;
            }
            else
            {
                saveToDeviceCfgButton.Enabled = true;
            }
            if (isCalibrationPeaksExist() > 1)
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
            SelectGlobalPeakShape(fwhmCalibration.CalibrationPeaks);

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
                        !IsValidFitStatistic(peak.GaussianChi2, peak.GaussianNdp) ||
                        !IsValidFitStatistic(peak.ExpGaussExpCandidateChi2[candidateIndex], peak.ExpGaussExpCandidateNdp[candidateIndex]) ||
                        !DoesNotWorsenGaussianFit(peak.ExpGaussExpCandidateChi2[candidateIndex], peak.GaussianChi2))
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
                        !IsValidFitStatistic(peak.GaussianChi2, peak.GaussianNdp) ||
                        !IsValidFitStatistic(peak.VoigtCandidateChi2[candidateIndex], peak.VoigtCandidateNdp[candidateIndex]) ||
                        !DoesNotWorsenGaussianFit(peak.VoigtCandidateChi2[candidateIndex], peak.GaussianChi2))
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
            if (bestExpGaussExpCandidate >= 0 && expGaussExpChi2 < selectedChi2)
            {
                peakType = FwhmCalibration.ExpGaussExpPeakType;
                selectedChi2 = expGaussExpChi2;
                selectedNdp = expGaussExpNdp;
            }
            if (bestVoigtCandidate >= 0 && voigtChi2 < selectedChi2)
            {
                peakType = FwhmCalibration.VoigtPeakType;
                selectedChi2 = voigtChi2;
                selectedNdp = voigtNdp;
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

        }

        static bool IsValidFitStatistic(double chi2, int ndp)
        {
            return chi2 >= 0.0 && ndp > 0 && !Double.IsNaN(chi2) && !Double.IsInfinity(chi2);
        }

        static bool DoesNotWorsenGaussianFit(double candidateChi2, double gaussianChi2)
        {
            // A global shape is accepted only when it is no worse for every selected peak.
            double tolerance = Math.Max(1e-9, Math.Abs(gaussianChi2) * 1e-12);
            return candidateChi2 <= gaussianChi2 + tolerance;
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
                    foreach (ResultData data in doc.ResultDataFile.ResultDataList)
                    {
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

            if (mainForm.DocumentList != null)
            {
                foreach (DocEnergySpectrum doc in mainForm.DocumentList)
                {
                    foreach (ResultData data in doc.ResultDataFile.ResultDataList)
                    {
                        if (data.FwhmCalibration.CalibrationPeaks.Count > 0)
                        {
                            peaks.AddRange(data.FwhmCalibration.CalibrationPeaks);
                        }
                    }
                }
                if (peaks.Count > 0)
                {
                    mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks.Clear();
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
                        mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks.Add(peak);
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
    }
}
