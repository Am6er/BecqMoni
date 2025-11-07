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

        public DCFwhmCalibrationView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
            channelColumn.Alignment = ColumnAlignment.Right;
            energyColumn.Alignment = ColumnAlignment.Right;
            fwhmColumn.Alignment = ColumnAlignment.Right;
            executeCalibrationButton.Enabled = false;
        }

        public void SetFwhmCalibration(FwhmCalibration fwhmCalibration)
        {
            // проверить в mainForm на избыточность событий.
            // Нужны события, когда только калибровка меняется.
            if (fwhmCalibration == null) { return; }
            this.fwhmCalibration = fwhmCalibration;
            UpdateData();
        }

        private void DCFwhmCalibrationView_FormLoad(object sender, EventArgs e)
        {

        }

        private void DCFwhmCalibrationView_FormClosing(object sender, FormClosingEventArgs e)
        {

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
            UpdateCommitButtonsState();
        }

        void UpdateSelectedCurveInfo()
        {
            if (selectCurveComboBox.SelectedIndex == -1)
            {
                if (fwhmCalibration is SqrtFwhmCalibration)
                {
                    selectCurveComboBox.SelectedIndex = (int)FwhmCalibrationCurve.SquareRootPolynomial;
                    // унести в свой класс
                    curveFormulaLabel.Text = "FWHM = √(a * ch² + b * ch + c)";
                }
                else
                {
                    selectCurveComboBox.SelectedIndex = (int)FwhmCalibrationCurve.SimpleSquareRoot;
                    curveFormulaLabel.Text = "FWHM = √(b + k * ch)";
                }
                minPeaksRequirement = fwhmCalibration.MaxCoefficients - 1;
            }
            else
            {
                if ((FwhmCalibrationCurve)selectCurveComboBox.SelectedIndex == FwhmCalibrationCurve.SquareRootPolynomial)
                {
                    curveFormulaLabel.Text = "FWHM = √(a * ch² + b * ch + c)";
                    // вытащить из класса, а не хардкодить
                    minPeaksRequirement = 3;
                }
                else
                {
                    curveFormulaLabel.Text = "FWHM = √(b + k * ch)";
                    minPeaksRequirement = 2;
                }
            }
            minPeaksRequirementLabel.Text = String.Format(Resources.MinPeaksRequirement, minPeaksRequirement);
        }

        void selectCurveComboBox_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            UpdateSelectedCurveInfo();
            UpdateCommitButtonsState();
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
        }

        void UpdateCommitButtonsState()
        {
            if (selectCurveComboBox.SelectedIndex == -1 ||
                (CollectedPeaksTable.SelectedItems.Length > 0 && CollectedPeaksTable.SelectedItems[0].Index < 0) ||
                lastSelectedIndex == selectCurveComboBox.SelectedIndex ||
                minPeaksRequirement < tableModel1.Rows.Count)
            {
                executeCalibrationButton.Enabled = false;
                saveToDeviceCfgButton.Enabled = false;
            } else
            {
                executeCalibrationButton.Enabled = true;
                saveToDeviceCfgButton.Enabled = true;
            }
        }

        private void removePeakButton_Click(object sender, EventArgs e)
        {
            int selectedItemIndex;
            if (CollectedPeaksTable.SelectedItems.Length >= 1)
            {
                selectedItemIndex = CollectedPeaksTable.SelectedItems[0].Index;
                UpdateCommitButtonsState();
            }
            else
            {
                selectedItemIndex = 0;
            }
            if (selectedItemIndex < 0 && selectedItemIndex >= fwhmCalibration.CalibrationPeaks.Count)
            {
                UpdateCommitButtonsState();
                return;
            }
            try
            {
                mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks.RemoveAt(selectedItemIndex);
                mainForm.ActiveDocument.Dirty = true;
            }
            catch { }
            tableModel1.Selections.Clear();
            UpdateData();
        }

        private void saveToDeviceCfgButton_Click(object sender, EventArgs e)
        {

        }

        private void addPeakButton_Click(object sender, EventArgs e)
        {

        }
    }
}
