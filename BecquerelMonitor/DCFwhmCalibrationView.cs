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

        public DCFwhmCalibrationView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
            channelColumn.Alignment = ColumnAlignment.Right;
            energyColumn.Alignment = ColumnAlignment.Right;
            fwhmColumn.Alignment = ColumnAlignment.Right;
            executeCalibrationButton.Enabled = false;
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
            fwhmCalibration = mainForm.ActiveDocument.ActiveResultData.FwhmCalibration;
            UpdateData();
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
            // Perform calibration for Peak_type, left, right skew
            CalibrationPeak optimalPeak = null;
            foreach (CalibrationPeak peak in fwhmCalibration.CalibrationPeaks)
            {
                if (peak.Chi2pNdp != -1.0)
                {
                    if (optimalPeak == null || optimalPeak.Chi2pNdp > peak.Chi2pNdp)
                    {
                        optimalPeak = peak;
                    }
                }
            }
            if (optimalPeak != null)
            {
                fwhmCalibration.PeakType = optimalPeak.PeakType;
                fwhmCalibration.ExpGaussExpLeftTail = optimalPeak.ExpGaussExpLeftTail;
                fwhmCalibration.ExpGaussExpRightTail = optimalPeak.ExpGaussExpRightTail;
                fwhmCalibration.Chi2pNdp = optimalPeak.Chi2pNdp;
            }

            mainForm.ActiveDocument.ActiveResultData.FwhmCalibration = fwhmCalibration.Clone();
            mainForm.ActiveDocument.Dirty = true;
            calibrationDone = true;
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
            mainForm.UpdateDetectedPeakView();
            mainForm.ActiveDocument.UpdateEnergySpectrum();
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
