using System;
using System.Windows.Forms;
using XPTable.Models;
using BecquerelMonitor.Properties;

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

        public void UpdateFwhmCalibration()
        {
            // TODO проверить в mainForm на избыточность событий.
            // Нужны события, когда только калибровка меняется.
            // Нужно на финальном этапе смотреть
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

        void UpdateSelectedCurveInfo()
        {
            if (selectCurveComboBox.SelectedIndex == -1)
            {
                if (fwhmCalibration is SqrtFwhmCalibration)
                {
                    selectCurveComboBox.SelectedIndex = (int)FwhmCalibrationCurve.SquareRootPolynomial;
                }
                else
                {
                    selectCurveComboBox.SelectedIndex = (int)FwhmCalibrationCurve.SimpleSquareRoot;
                }
            }
            curveFormulaLabel.Text = fwhmCalibration.GetFormula();
            minPeaksRequirement = fwhmCalibration.MinPeaksRequirement();
            minPeaksRequirementLabel.Text = String.Format(Resources.MinPeaksRequirement, minPeaksRequirement);
        }

        void selectCurveComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (selectCurveComboBox.SelectedIndex == -1 || selectCurveComboBox.SelectedIndex == lastSelectedIndex) return;
            if (selectCurveComboBox.SelectedIndex == (int)FwhmCalibrationCurve.SquareRootPolynomial)
            {
                fwhmCalibration = new SqrtFwhmCalibration { CalibrationPeaks = fwhmCalibration.ClonePeaks() };
            } else
            {
                fwhmCalibration = new SimpleSqrtFwhmCalibration { CalibrationPeaks = fwhmCalibration.ClonePeaks() };
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
        }

        private void removePeakButton_Click(object sender, EventArgs e)
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
            if (selectedItemIndex < 0 && selectedItemIndex >= fwhmCalibration.CalibrationPeaks.Count)
            {
                UpdateCalibrateButtonState();
                return;
            }
            try
            {
                mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks.RemoveAt(selectedItemIndex);
                mainForm.ActiveDocument.Dirty = true;
            }
            catch { }
            tableModel1.Selections.Clear();
            UpdateFwhmCalibration();
        }

        private void saveToDeviceCfgButton_Click(object sender, EventArgs e)
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
            if (!fwhmCalibration.PerformCalibration())
            {
                // TODO нужно будет добавить обработку плохой калибровки
                throw new NotImplementedException();
            }
            FWHMPeakDetectionMethodConfig peakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig) deviceConfig.PeakDetectionMethodConfig;
            peakDetectionMethodConfig.FwhmCalibration = fwhmCalibration;
            DeviceConfigManager.GetInstance().SaveConfig(activeDocument.ActiveResultData.DeviceConfig);
            activeDocument.ActiveResultData.FwhmCalibration = fwhmCalibration;
            mainForm.UpdateDeviceConfigForm();
        }

        private void addPeakButton_Click(object sender, EventArgs e)
        {
            if (mainForm.ActiveDocument != null)
            {
                cancelAddPeakButton.Enabled = true;
                peakPickupProcessing = true;
                addPeakButton.Enabled = !peakPickupProcessing;
                mainForm.ActiveDocument.EnergySpectrumView.PeakPickuped += this.energySpectrumView_PeakPickuped;
            }
        }

        void energySpectrumView_PeakPickuped(object sender, PeakPickupedEventArgs e)
        {
            if (!peakPickupProcessing)
            {
                return;
            }

            CalibrationPeak newPeak = e.CalibrationPeak;
            foreach (CalibrationPeak peak in mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks)
            {
                if (peak.Channel == newPeak.Channel || peak.FWHM == newPeak.FWHM)
                {
                    string PeakExistText = String.Format(Resources.ERRPeakExist, peak.FWHM, peak.Channel);
                    MessageBox.Show(PeakExistText, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    return;
                }
            }
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
                mainForm.ActiveDocument.EnergySpectrumView.PeakPickuped -= this.energySpectrumView_PeakPickuped;
            }
            peakPickupProcessing = false;
            addPeakButton.Enabled = !peakPickupProcessing;
            cancelAddPeakButton.Enabled = peakPickupProcessing;
            UpdateCalibrateButtonState();
        }

        private void executeCalibrationButton_Click(object sender, EventArgs e)
        {
            if (!fwhmCalibration.PerformCalibration())
            {
                // TODO нужно будет добавить обработку плохой калибровки
                throw new NotImplementedException();
            }
            mainForm.ActiveDocument.ActiveResultData.FwhmCalibration = fwhmCalibration;
            calibrationDone = true;
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
            mainForm.UpdateDetectedPeakView();
        }

        private void cancelAddPeakButton_Click(object sender, EventArgs e)
        {
            ClearPeakPickupState();
        }
    }
}
