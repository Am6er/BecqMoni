using BecquerelMonitor.Properties;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Remoting.Channels;
using System.Windows.Forms;
using XPTable.Editors;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x020000A4 RID: 164
    public partial class DCEnergyCalibrationView : ToolWindow
    {
        // Token: 0x06000813 RID: 2067 RVA: 0x0002DC70 File Offset: 0x0002BE70
        public DCEnergyCalibrationView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            this.UpdateMultipointButtonState();
        }

        // Token: 0x06000814 RID: 2068 RVA: 0x0002DD10 File Offset: 0x0002BF10
        void DCEnergyCalibrationView_Load(object sender, EventArgs e)
        {
            this.HideMultipointForm();
        }

        // Token: 0x06000815 RID: 2069 RVA: 0x0002DD18 File Offset: 0x0002BF18
        public void UpdateEnergyCalibrationConfig()
        {
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
        }

        // Token: 0x06000816 RID: 2070 RVA: 0x0002DD8C File Offset: 0x0002BF8C
        public void SetEnergyCalibration(EnergyCalibration energyCalibration, EnergyCalibration defaultEnergyCalibration)
        {
            this.numericUpDown1.Text = "0";
            this.numericUpDown2.Text = "0";
            this.numericUpDown3.Text = "0";
            this.numericUpDown4.Text = "0";
            this.numericUpDown5.Text = "0";


            this.energyCalibration = (PolynomialEnergyCalibration)energyCalibration.Clone();
            this.defaultEnergyCalibration = (PolynomialEnergyCalibration)defaultEnergyCalibration.Clone();
            this.formLoading = true;
            this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString();
            this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString();


            if (this.energyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString();
            }
            if (this.energyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString();
            }
            if (this.energyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString();
            }
            this.formLoading = false;
            this.calibrationPoints.Clear();
            this.ShowCalibrationPoints();
            this.UpdateMultipointButtonState();
        }

        void button12_Click(object sender, EventArgs e)
        {
            if (this.defaultEnergyCalibration.PolynomialOrder == 4)
            {
                this.energyCalibration.Coefficients[4] = this.defaultEnergyCalibration.Coefficients[4];
                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString();
            } else
            {
                this.energyCalibration.Coefficients[4] = 0;
                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString();
            }
            
            this.UpdateEnergyCalibration();
        }

        void button13_Click(object sender, EventArgs e)
        {
            if (this.defaultEnergyCalibration.PolynomialOrder >= 3)
            {
                this.energyCalibration.Coefficients[3] = this.defaultEnergyCalibration.Coefficients[3];
                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString();
            } else
            {
                this.energyCalibration.Coefficients[3] = 0;
                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString();
            }
            
            this.UpdateEnergyCalibration();
        }

        // Token: 0x0600081A RID: 2074 RVA: 0x0002E008 File Offset: 0x0002C208
        void button1_Click(object sender, EventArgs e)
        {
            if (this.defaultEnergyCalibration.PolynomialOrder >= 2)
            {
                this.energyCalibration.Coefficients[2] = this.defaultEnergyCalibration.Coefficients[2];
                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString();
            } else
            {
                this.energyCalibration.Coefficients[2] = 0;
                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString();
            }
            
            this.UpdateEnergyCalibration();
        }

        // Token: 0x0600081B RID: 2075 RVA: 0x0002E058 File Offset: 0x0002C258
        void button2_Click(object sender, EventArgs e)
        {
            this.energyCalibration.Coefficients[1] = this.defaultEnergyCalibration.Coefficients[1];
            this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString();
            this.UpdateEnergyCalibration();
        }

        // Token: 0x0600081C RID: 2076 RVA: 0x0002E0A8 File Offset: 0x0002C2A8
        void button3_Click(object sender, EventArgs e)
        {
            this.energyCalibration.Coefficients[0] = this.defaultEnergyCalibration.Coefficients[0];
            this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString();
            this.UpdateEnergyCalibration();
        }

        // Token: 0x0600081D RID: 2077 RVA: 0x0002E0F8 File Offset: 0x0002C2F8
        void UpdateEnergyCalibration()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration = this.energyCalibration.Clone();
            activeDocument.Dirty = true;
            activeDocument.UpdateEnergySpectrum();
            this.mainForm.ShowMeasurementResult(true);
            this.mainForm.UpdateDetectedPeakView();
        }

        // Token: 0x0600081E RID: 2078 RVA: 0x0002E154 File Offset: 0x0002C354
        void button6_Click(object sender, EventArgs e)
        {
            this.enableMultipointCalibration = !this.enableMultipointCalibration;
            if (this.enableMultipointCalibration)
            {
                this.ShowMultipointForm();
            }
            else
            {
                this.HideMultipointForm();
            }
            base.Invalidate();
        }

        // Token: 0x0600081F RID: 2079 RVA: 0x0002E188 File Offset: 0x0002C388
        void ShowMultipointForm()
        {
            Size size = base.Size;
            Size autoScrollMinSize = base.AutoScrollMinSize;
            this.panel1.Visible = true;
            base.SuspendLayout();
            size.Height += 160;
            base.Size = size;
            autoScrollMinSize.Height = 380;
            base.AutoScrollMinSize = autoScrollMinSize;
            this.button6.Text = Resources.MultipointCalibrationString + " <<";
            base.VerticalScroll.Value = base.VerticalScroll.Maximum;
            base.ResumeLayout();
        }

        // Token: 0x06000820 RID: 2080 RVA: 0x0002E21C File Offset: 0x0002C41C
        void HideMultipointForm()
        {
            Size size = base.Size;
            Size autoScrollMinSize = base.AutoScrollMinSize;
            this.panel1.Visible = false;
            size.Height -= 160;
            base.Size = size;
            autoScrollMinSize.Height = 222;
            base.AutoScrollMinSize = autoScrollMinSize;
            this.button6.Text = Resources.MultipointCalibrationString + " >>";
        }

        // Token: 0x06000821 RID: 2081 RVA: 0x0002E290 File Offset: 0x0002C490
        public void SetStabilizerState(ResultData resultData)
        {
            this.checkBox1.Checked = resultData.ResultDataStatus.Stabilization;
        }

        // Token: 0x06000822 RID: 2082 RVA: 0x0002E2A8 File Offset: 0x0002C4A8
        void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            bool @checked = this.checkBox1.Checked;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.ResultDataStatus.Stabilization = @checked;
            this.mainForm.UpdateCalibrationPeak();
        }

        // Token: 0x06000823 RID: 2083 RVA: 0x0002E2F0 File Offset: 0x0002C4F0
        void button5_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            DeviceConfigInfo deviceConfig = activeDocument.ActiveResultData.DeviceConfig;
            this.mainForm.ShowDeviceConfigStabilizerForm(deviceConfig);
        }

        // Token: 0x06000824 RID: 2084 RVA: 0x0002E328 File Offset: 0x0002C528
        void button4_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Resources.MSGSaveEnergyCalibration, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
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
            deviceConfig.EnergyCalibration = this.energyCalibration;
            DeviceConfigManager.GetInstance().SaveConfig(activeDocument.ActiveResultData.DeviceConfig);
            activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration = this.energyCalibration;
            this.mainForm.UpdateDeviceConfigForm();
        }

        // Token: 0x06000825 RID: 2085 RVA: 0x0002E3E8 File Offset: 0x0002C5E8
        void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown1.Text);
                    this.numericUpDown1.ForeColor = Color.Black;
                    this.energyCalibration.Coefficients[2] = result;
                    if (!energyCalibration.CheckCalibration())
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                        throw new Exception();
                    }
                    this.UpdateEnergyCalibration();
                }
                catch
                {
                    this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString();
                    this.numericUpDown1.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000826 RID: 2086 RVA: 0x0002E400 File Offset: 0x0002C600
        void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown2.Text);
                    this.numericUpDown2.ForeColor = Color.Black;
                    this.energyCalibration.Coefficients[1] = result;
                    if (!energyCalibration.CheckCalibration())
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                        throw new Exception();
                    }
                    this.UpdateEnergyCalibration();
                }
                catch
                {
                    this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString();
                    this.numericUpDown2.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000827 RID: 2087 RVA: 0x0002E418 File Offset: 0x0002C618
        void numericUpDown3_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown3.Text);
                    this.numericUpDown3.ForeColor = Color.Black;
                    this.energyCalibration.Coefficients[0] = result;
                    if (!energyCalibration.CheckCalibration())
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                        throw new Exception();
                    }
                    this.UpdateEnergyCalibration();
                }
                catch
                {
                    this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString();
                    this.numericUpDown3.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown4_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown4.Text);
                    this.numericUpDown4.ForeColor = Color.Black;
                    this.energyCalibration.Coefficients[4] = result;
                    if (!energyCalibration.CheckCalibration())
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                        throw new Exception();
                    }
                    this.UpdateEnergyCalibration();
                }
                catch
                {
                    this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString();
                    this.numericUpDown4.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown5_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown5.Text);
                    this.numericUpDown5.ForeColor = Color.Black;
                    this.energyCalibration.Coefficients[3] = result;
                    if (!energyCalibration.CheckCalibration())
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                        throw new Exception();
                    }
                    this.UpdateEnergyCalibration();
                }
                catch
                {
                    this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString();
                    this.numericUpDown5.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000828 RID: 2088 RVA: 0x0002E430 File Offset: 0x0002C630
        void button8_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                this.channelPickupProcessing = true;
                activeDocument.EnergySpectrumView.ChannelPickuped += this.energySpectrumView_ChannelPickuped;
            }
            this.UpdateMultipointButtonState();
        }

        // Token: 0x06000829 RID: 2089 RVA: 0x0002E478 File Offset: 0x0002C678
        void UpdateMultipointButtonState()
        {
            this.button7.Enabled = (this.calibrationPoints.Count > 0 && !this.channelPickupProcessing && this.multipointModified);
            this.button8.Enabled = (!this.channelPickupProcessing);
            this.button9.Enabled = (this.calibrationPoints.Count > 0 && !this.channelPickupProcessing);
            this.button11.Enabled = this.channelPickupProcessing;
            if (this.calibrationDone)
            {
                this.label36.Text = Resources.MSGCalibrationDone;
                return;
            }
            if (this.channelPickupProcessing)
            {
                this.label36.Text = Resources.MSGPickUpCalibrationPoint;
                return;
            }
            this.label36.Text = Resources.MSGProceedCalibration;
        }

        // Token: 0x0600082A RID: 2090 RVA: 0x0002E57C File Offset: 0x0002C77C
        void energySpectrumView_ChannelPickuped(object sender, ChannelPickupedEventArgs e)
        {
            if (!this.channelPickupProcessing)
            {
                return;
            }
            decimal energy = Math.Round((decimal)this.energyCalibration.ChannelToEnergy((double)e.Channel), 2);
            CalibrationPoint item = new CalibrationPoint(e.Channel, energy, e.Count);
            this.calibrationPoints.Add(item);
            this.multipointModified = true;
            this.calibrationDone = false;
            this.ShowCalibrationPoints();
            this.ClearChannelPickupState();
        }

        public void AddCalibration(int channel, decimal energy, int count)
        {
            CalibrationPoint item = new CalibrationPoint(channel, energy, count);
            this.calibrationPoints.Add(item);
            this.multipointModified = true;
            this.calibrationDone = false;
            this.ShowCalibrationPoints();
            this.UpdateMultipointButtonState();
        }

        // Token: 0x0600082B RID: 2091 RVA: 0x0002E5F4 File Offset: 0x0002C7F4
        void button11_Click(object sender, EventArgs e)
        {
            this.ClearChannelPickupState();
        }

        // Token: 0x0600082C RID: 2092 RVA: 0x0002E5FC File Offset: 0x0002C7FC
        void ClearChannelPickupState()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                activeDocument.EnergySpectrumView.ChannelPickuped -= this.energySpectrumView_ChannelPickuped;
            }
            this.channelPickupProcessing = false;
            this.UpdateMultipointButtonState();
        }

        // Token: 0x0600082D RID: 2093 RVA: 0x0002E644 File Offset: 0x0002C844
        void button9_Click(object sender, EventArgs e)
        {
            int num;
            if (this.table1.SelectedItems.Length >= 1)
            {
                num = this.table1.SelectedItems[0].Index;
            }
            else
            {
                num = 0;
            }
            if (num < 0 && num >= this.calibrationPoints.Count)
            {
                return;
            }
            try
            {
                this.calibrationPoints.RemoveAt(num);
            } catch
            {

            }
            this.tableModel1.Selections.Clear();
            this.multipointModified = true;
            this.calibrationDone = false;
            this.ShowCalibrationPoints();
            this.UpdateMultipointButtonState();
        }

        // Token: 0x0600082E RID: 2094 RVA: 0x0002E6D8 File Offset: 0x0002C8D8
        void ShowCalibrationPoints()
        {
            int num = 1;
            this.table1.SuspendLayout();
            this.tableModel1.Rows.Clear();
            this.calibrationPoints.Sort();
            foreach (CalibrationPoint calibrationPoint in this.calibrationPoints)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(num.ToString()));
                row.Cells.Add(new Cell(calibrationPoint.Channel));
                row.Cells.Add(new Cell(calibrationPoint.Energy));
                this.tableModel1.Rows.Add(row);
                num++;
            }
            if (num <= 5 && num >= 2)
            {
                this.numericUpDown6.Value = num-2;
            } else
            {
                if (num == 1) this.numericUpDown6.Value = 0;
                this.numericUpDown6.Value = 4;
            }
            this.table1.ResumeLayout();
        }

        // Token: 0x0600082F RID: 2095 RVA: 0x0002E7CC File Offset: 0x0002C9CC
        void table1_SelectionChanged(object sender, SelectionEventArgs e)
        {
            this.UpdateMultipointButtonState();
        }

        // Token: 0x06000830 RID: 2096 RVA: 0x0002E7D4 File Offset: 0x0002C9D4
        void table1_EditingStopped(object sender, CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;
            try
            {
                if (e.Column == 1)
                {
                    string text = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Channel = (int)decimal.Parse(text);
                    if (this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum.Length > this.calibrationPoints[row.Index].Channel)
                    {
                        this.calibrationPoints[row.Index].Count = this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum[this.calibrationPoints[row.Index].Channel];
                    }
                    else
                    {
                        throw new Exception(Resources.ERRCalibrationChannelExceed);
                    }
                    
                    this.multipointModified = true;
                    this.calibrationDone = false;
                    this.UpdateMultipointButtonState();
                }
                else if (e.Column == 2)
                {
                    string text2 = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Energy = decimal.Parse(text2);
                    this.multipointModified = true;
                    this.calibrationDone = false;
                    this.UpdateMultipointButtonState();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Resources.ERRAddCalibrationPoints, ((NumberCellEditor)e.Editor).TextBox.Text, ex.Message), Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
            }
        }

        void checkBox2_Click(object sender, EventArgs e)
        {
            this.button7.Enabled = true;
        }

        // Token: 0x06000831 RID: 2097 RVA: 0x0002E8C0 File Offset: 0x0002CAC0
        void button7_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < this.energyCalibration.Coefficients.Length; i++)
            {
                this.energyCalibration.Coefficients[i] = 0.0;
            }

            int PolynomOrder = (int)this.numericUpDown6.Value;
            double[] matrix;
            List<CalibrationPoint> points = this.calibrationPoints;
            if (points.Count == 1)
            {
                CalibrationPoint zero = new CalibrationPoint(0, 0, 0);
                points.Add(zero);
                PolynomOrder += 1;
            }
            try
            {
                if (this.checkBox2.Checked)
                {
                    matrix = Utils.CalibrationSolver.SolveWeighted(points, PolynomOrder);
                } else
                {
                    matrix = Utils.CalibrationSolver.Solve(points, PolynomOrder);
                }
                if (matrix == null) throw new Exception("Error");
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRInvalidChannelOrEnergyValues);
                return;
            }

            this.energyCalibration.Coefficients = new double[matrix.Length];
            this.energyCalibration.PolynomialOrder = matrix.Length - 1;
            this.energyCalibration.Coefficients = matrix;

            if (!this.energyCalibration.CheckCalibration())
            {
                MessageBox.Show(Resources.CalibrationFunctionError);
                return;
            }
            this.numericUpDown1.Text = "0";
            this.numericUpDown2.Text = "0";
            this.numericUpDown3.Text = "0";
            this.numericUpDown4.Text = "0";
            this.numericUpDown5.Text = "0";
            this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString();
            this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString();
            if (this.energyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString();
            }
            if (this.energyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString();
            }
            if (this.energyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString();
            }
            if (!this.energyCalibration.CheckCalibration())
            {
                MessageBox.Show(Resources.CalibrationFunctionError);
                return;
            }
            this.multipointModified = false;
            this.calibrationDone = true;
            this.UpdateMultipointButtonState();
            this.UpdateEnergyCalibration();
        }

        double fromStringtoDouble(string str)
        {
            double result;
            if (double.TryParse(str.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out result))
            {
                if (result > -100.0 && result < 100.0)
                {
                    return result;
                }
                else
                {
                    throw new Exception();
                }
            }
            //System.Windows.Forms.MessageBox.Show("Error while converting text to double: " + str);
            throw new Exception();
        }

        // Token: 0x06000832 RID: 2098 RVA: 0x0002ED00 File Offset: 0x0002CF00
        void DCEnergyCalibrationView_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.ClearChannelPickupState();
        }

        // Token: 0x06000833 RID: 2099 RVA: 0x0002ED08 File Offset: 0x0002CF08
        void DCEnergyCalibrationView_SizeChanged(object sender, EventArgs e)
        {
        }

        // Token: 0x04000415 RID: 1045
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x04000416 RID: 1046
        MainForm mainForm;

        // Token: 0x04000417 RID: 1047
        PolynomialEnergyCalibration energyCalibration;

        // Token: 0x04000418 RID: 1048
        PolynomialEnergyCalibration defaultEnergyCalibration;

        // Token: 0x04000419 RID: 1049
        bool enableMultipointCalibration;

        // Token: 0x0400041A RID: 1050
        bool formLoading;

        // Token: 0x0400041B RID: 1051
        bool channelPickupProcessing;

        // Token: 0x0400041C RID: 1052
        bool calibrationDone;

        // Token: 0x0400041D RID: 1053
        bool multipointModified;

        // Token: 0x0400041E RID: 1054
        List<CalibrationPoint> calibrationPoints = new List<CalibrationPoint>();
    }
}
