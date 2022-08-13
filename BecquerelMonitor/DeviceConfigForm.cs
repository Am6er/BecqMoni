using BecquerelMonitor.Hash;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using XPTable.Editors;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x02000066 RID: 102
    public partial class DeviceConfigForm : Form
    {
        // Token: 0x1700019A RID: 410
        // (get) Token: 0x06000511 RID: 1297 RVA: 0x00020428 File Offset: 0x0001E628
        // (set) Token: 0x06000512 RID: 1298 RVA: 0x00020430 File Offset: 0x0001E630
        public DeviceConfigInfo ActiveDeviceConfig
        {
            get
            {
                return this.activeDeviceConfig;
            }
            set
            {
                this.activeDeviceConfig = value;
            }
        }

        // Token: 0x06000513 RID: 1299 RVA: 0x0002043C File Offset: 0x0001E63C
        public DeviceConfigForm()
        {
            this.InitializeComponent();
            base.Icon = Resources.becqmoni;
            this.button4.Enabled = false;
            this.DisableForm();
            this.button3.Enabled = true;
            this.button4.Enabled = false;
            this.button12.Enabled = false;
            this.button6.Enabled = false;
            this.button5.Enabled = true;
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            this.UpdateMultipointButtonState();
            this.comboBox4.Items.Clear();
            foreach (DeviceType item in DeviceType.DeviceTypeList)
            {
                this.comboBox4.Items.Add(item);
            }
            foreach (ThermometerType item2 in ThermometerType.ThermometerTypeList)
            {
                this.comboBox1.Items.Add(item2);
            }
            int[] deviceConfigListColumnSizes = this.globalConfigManager.GlobalConfig.DeviceConfigListColumnSizes;
            for (int i = 0; i < this.columnModel1.Columns.Count; i++)
            {
                this.columnModel1.Columns[i].Width = ((deviceConfigListColumnSizes[i] > 32) ? deviceConfigListColumnSizes[i] : 32);
            }
            this.groupBox3.Visible = false;
            this.groupBox2.Top = 24;
        }

        // Token: 0x06000514 RID: 1300 RVA: 0x0002067C File Offset: 0x0001E87C
        void DeviceConfigForm_Load(object sender, EventArgs e)
        {
            this.ListupConfigFiles();
        }

        // Token: 0x06000515 RID: 1301 RVA: 0x00020684 File Offset: 0x0001E884
        void DeviceConfigForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!this.ConfirmSaveDeviceConfig())
            {
                return;
            }
            if (this.inputDeviceForm != null)
            {
                this.inputDeviceForm.FormClosing();
            }
            this.ClearChannelPickupState();
            this.globalConfigManager.GlobalConfig.DeviceConfigFormWidth = base.Width;
            this.globalConfigManager.GlobalConfig.DeviceConfigFormHeight = base.Height;
            int[] array = new int[this.columnModel1.Columns.Count];
            this.globalConfigManager.GlobalConfig.DeviceConfigListColumnSizes = array;
            for (int i = 0; i < this.columnModel1.Columns.Count; i++)
            {
                array[i] = this.columnModel1.Columns[i].Width;
            }
        }

        // Token: 0x06000516 RID: 1302 RVA: 0x00020748 File Offset: 0x0001E948
        public void ListupConfigFiles()
        {
            this.table1.SuspendLayout();
            this.tableModel1.Rows.Clear();
            this.tableModel1.Selections.Clear();
            foreach (DeviceConfigInfo deviceConfigInfo in this.manager.DeviceConfigList)
            {
                DeviceConfigInfo deviceConfigInfo2 = deviceConfigInfo.Clone();
                Row row = new Row();
                row.Cells.Add(new Cell(deviceConfigInfo2.Name));
                row.Cells.Add(new Cell(deviceConfigInfo2.LastUpdated.ToShortDateString() + " " + deviceConfigInfo2.LastUpdated.ToLongTimeString()));
                row.Tag = deviceConfigInfo2;
                this.tableModel1.Rows.Add(row);
                if (this.activeDeviceConfig != null && this.activeDeviceConfig.Guid == deviceConfigInfo2.Guid)
                {
                    this.activeDeviceConfig = deviceConfigInfo2;
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                }
                if (this.table1.SortingColumn != -1)
                {
                    this.table1.Sort();
                }
            }
            this.table1.ResumeLayout();
        }

        // Token: 0x06000517 RID: 1303 RVA: 0x00020894 File Offset: 0x0001EA94
        void UpdateConfigFilesList()
        {
            foreach (object obj in this.tableModel1.Rows)
            {
                Row row = (Row)obj;
                DeviceConfigInfo deviceConfigInfo = (DeviceConfigInfo)row.Tag;
                row.Cells[0].Text = deviceConfigInfo.Name;
                row.Cells[1].Text = deviceConfigInfo.LastUpdated.ToShortDateString() + " " + deviceConfigInfo.LastUpdated.ToLongTimeString();
            }
        }

        // Token: 0x06000518 RID: 1304 RVA: 0x00020954 File Offset: 0x0001EB54
        public void UpdateModifiedConfigFile()
        {
            this.ListupConfigFiles();
            if (this.activeDeviceConfig != null)
            {
                this.LoadFormContents(this.activeDeviceConfig);
            }
        }

        // Token: 0x06000519 RID: 1305 RVA: 0x00020974 File Offset: 0x0001EB74
        void button3_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveDeviceConfig())
            {
                return;
            }
            string text = this.AssignNewFilename();
            if (text == null)
            {
                return;
            }
            DeviceConfigInfo config = this.manager.CreateConfig(text);
            this.activeDeviceConfig = config;
            this.LoadFormContents(config);
            this.ListupConfigFiles();
            this.textBox1.SelectAll();
            this.textBox1.Focus();
        }

        // Token: 0x0600051A RID: 1306 RVA: 0x000209D8 File Offset: 0x0001EBD8
        void button12_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveDeviceConfig())
            {
                return;
            }
            string filename = Path.GetFileNameWithoutExtension(this.activeDeviceConfig.Filename) + " (Copy).xml";
            DeviceConfigInfo deviceConfigInfo = this.manager.DuplicateConfig(this.activeDeviceConfig, filename);
            if (deviceConfigInfo == null)
            {
                return;
            }
            this.activeDeviceConfig = deviceConfigInfo;
            this.LoadFormContents(deviceConfigInfo);
            this.ListupConfigFiles();
            this.textBox1.SelectAll();
            this.textBox1.Focus();
        }

        // Token: 0x0600051B RID: 1307 RVA: 0x00020A58 File Offset: 0x0001EC58
        void button4_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig == null)
            {
                return;
            }
            DialogResult dialogResult = MessageBox.Show(string.Format(Resources.MessageRemoveDeviceConfig, this.activeDeviceConfig.Name), Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (dialogResult == DialogResult.OK)
            {
                this.manager.DeleteConfig(this.activeDeviceConfig);
                this.activeDeviceConfig = null;
                this.DisableForm();
                this.ListupConfigFiles();
            }
        }

        // Token: 0x0600051C RID: 1308 RVA: 0x00020AC4 File Offset: 0x0001ECC4
        //Save button from device configuration form
        void button6_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig == null)
            {
                return;
            }
            if (!this.SaveFormContents(this.activeDeviceConfig))
            {
                MessageBox.Show(Resources.ERRInvalidInputForm);
                return;
            }
            if (!this.manager.SaveConfig(this.activeDeviceConfig))
            {
                MessageBox.Show(Resources.ERRDuplicateConfigName);
                return;
            }
            this.ResetActiveDeviceConfigDirty();
            this.ListupConfigFiles();
        }

        // Token: 0x0600051D RID: 1309 RVA: 0x00020B2C File Offset: 0x0001ED2C
        string ConvertNameToFilename(string name)
        {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            foreach (char oldChar in invalidFileNameChars)
            {
                name = name.Replace(oldChar, '_');
            }
            return name + ".xml";
        }

        // Token: 0x0600051E RID: 1310 RVA: 0x00020B70 File Offset: 0x0001ED70
        string AssignNewFilename()
        {
            for (int i = 1; i < 999; i++)
            {
                string text = Resources.NewDeviceConfigPrefix + "(" + i.ToString() + ").xml";
                bool flag = false;
                foreach (DeviceConfigInfo deviceConfigInfo in this.manager.DeviceConfigList)
                {
                    if (text == deviceConfigInfo.Filename)
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    return text;
                }
            }
            return null;
        }

        // Token: 0x0600051F RID: 1311 RVA: 0x00020C20 File Offset: 0x0001EE20
        void button5_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveDeviceConfig())
            {
                return;
            }
            base.Close();
        }

        // Token: 0x06000520 RID: 1312 RVA: 0x00020C34 File Offset: 0x0001EE34
        void PrepareDeviceForm(DeviceType type)
        {
            if (type != null)
            {
                if (this.inputDeviceForm == null || this.inputDeviceForm.GetType() != type.DeviceConfigFormType)
                {
                    if (this.inputDeviceForm != null)
                    {
                        this.inputDeviceForm.FormClosing();
                    }
                    this.inputDeviceForm = (InputDeviceForm)Activator.CreateInstance(type.DeviceConfigFormType, new object[]
                    {
                        this
                    });
                    this.tabControl1.TabPages[1].Controls.Clear();
                    this.tabControl1.TabPages[1].Controls.Add(this.inputDeviceForm);
                    if (this.activeDeviceConfig.DeviceType == "AtomSpectraVCP")
                    {
                        this.button13.Enabled = true;
                        this.button13.Visible = true;
                        this.button14.Enabled = true;
                        this.button14.Visible = true;
                    }
                    else
                    {
                        this.button13.Enabled = false;
                        this.button13.Visible = false;
                        this.button14.Enabled = false;
                        this.button14.Visible = false;
                    }

                    this.inputDeviceForm.Initialize();
                    this.comboBox4.SelectedItem = type;
                    this.selectedDeviceIndex = this.comboBox4.SelectedIndex;
                    return;
                }
            }
            else
            {
                this.inputDeviceForm = new InputDeviceForm();
                this.comboBox4.SelectedItem = null;
                this.selectedDeviceIndex = -1;
                this.tabControl1.TabPages[1].Controls.Clear();
                this.tabControl1.TabPages[1].Controls.Add(this.inputDeviceForm);
            }
        }

        // Token: 0x06000521 RID: 1313 RVA: 0x00020D64 File Offset: 0x0001EF64
        void PrepareThermometerForm(ThermometerType type)
        {
            if (type != null)
            {
                if (this.thermometerForm == null || this.thermometerForm.GetType() != type.ThermometerFormType)
                {
                    if (this.thermometerForm != null)
                    {
                        this.thermometerForm.FormClosing();
                    }
                    this.thermometerForm = (ThermometerForm)Activator.CreateInstance(type.ThermometerFormType, new object[]
                    {
                        this
                    });
                    this.tabControl1.TabPages[2].Controls.Clear();
                    this.tabControl1.TabPages[2].Controls.Add(this.thermometerForm);
                    this.thermometerForm.Initialize();
                    this.comboBox1.SelectedItem = type;
                    this.selectedThermometerIndex = this.comboBox1.SelectedIndex;
                    return;
                }
            }
            else
            {
                this.thermometerForm = new ThermometerForm();
                this.comboBox1.SelectedItem = null;
                this.selectedThermometerIndex = -1;
                this.tabControl1.TabPages[2].Controls.Clear();
                this.tabControl1.TabPages[2].Controls.Add(this.thermometerForm);
            }
        }

        // Token: 0x06000522 RID: 1314 RVA: 0x00020E94 File Offset: 0x0001F094
        void LoadFormContents(DeviceConfigInfo config)
        {
            this.contentsLoading = true;
            this.textBox1.Text = config.Name;
            this.doubleTextBox5.Text = config.DefaultMeasurementTime.ToString();
            this.integerTextBox1.Text = config.NumberOfChannels.ToString();
            this.doubleTextBox6.Text = config.ChannelPitch.ToString();
            this.textBox19.Text = config.Note;
            this.deviceFormLoading = true;
            DeviceType type = null;
            DeviceType.DeviceTypeMap.TryGetValue(config.DeviceType, out type);
            this.PrepareDeviceForm(type);
            this.inputDeviceForm.LoadFormContents(config.InputDeviceConfig);
            this.deviceFormLoading = false;
            this.deviceFormLoading = true;
            ThermometerType type2 = null;
            ThermometerType.ThermometerTypeMap.TryGetValue(config.ThermometerType, out type2);
            this.PrepareThermometerForm(type2);
            this.thermometerForm.LoadFormContents(config.ThermometerConfig);
            this.deviceFormLoading = false;
            PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)config.EnergyCalibration;
            if (polynomialEnergyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown9.Text = polynomialEnergyCalibration.Coefficients[3].ToString();
            }
            else
            {
                this.numericUpDown9.Text = "0";
            }
            if (polynomialEnergyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown8.Text = polynomialEnergyCalibration.Coefficients[4].ToString();
            }
            else
            {
                this.numericUpDown8.Text = "0";

            }
            if (polynomialEnergyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = polynomialEnergyCalibration.Coefficients[2].ToString();
            }
            else
            {
                this.numericUpDown1.Text = "0";
            }
            this.numericUpDown2.Text = polynomialEnergyCalibration.Coefficients[1].ToString();
            this.numericUpDown7.Text = polynomialEnergyCalibration.Coefficients[0].ToString();
            this.ShowCalibrationPoints();
            this.UpdateMultipointButtonState();
            this.tableModel3.Rows.Clear();
            if (config.StabilizerConfig != null)
            {
                foreach (TargetPeak targetPeak in config.StabilizerConfig.TargetPeaks)
                {
                    Row row = new Row();
                    row.Cells.Add(new Cell(targetPeak.Nuclide));
                    row.Cells.Add(new Cell(targetPeak.Energy));
                    row.Cells.Add(new Cell(targetPeak.Error));
                    this.tableModel3.Rows.Add(row);
                }
            }
            DoseRateConfig doseRateConfig = config.DoseRateConfig;
            if (doseRateConfig != null)
            {
                this.doubleTextBox1.Text = doseRateConfig.Sensitivity.ToString();
                this.doubleTextBox2.Text = doseRateConfig.LowerBound.ToString();
                this.doubleTextBox3.Text = doseRateConfig.UpperBound.ToString();
            }
            FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)config.PeakDetectionMethodConfig;
            this.numberColumn4.Minimum = 1;
            this.numberColumn4.Maximum = 10000;
            this.numberColumn4.Increment = 1;
            this.numericUpDown4.Value = (decimal)FWHMPeakDetectionMethodConfig.Min_SNR;

            this.numberColumn3.Minimum = 1;
            this.numberColumn3.Maximum = 1000;
            this.numberColumn3.Increment = 1;
            this.numericUpDown3.Value = FWHMPeakDetectionMethodConfig.Max_Items;

            this.numericUpDown5.Minimum = 1;
            this.numericUpDown5.Maximum = 1000;
            this.numericUpDown5.Increment = 1;
            this.numericUpDown5.Value = (decimal)FWHMPeakDetectionMethodConfig.FWHM_AT_0;

            this.numericUpDown6.Minimum = 0;
            this.numericUpDown6.Maximum = 100;
            this.numericUpDown6.Increment = 1;
            this.numericUpDown6.Value = (decimal)FWHMPeakDetectionMethodConfig.Tolerance;

            this.numericUpDown10.Minimum = 1;
            this.numericUpDown10.Maximum = 100000;
            this.numericUpDown10.Increment = 1;
            this.numericUpDown10.Value = (decimal)FWHMPeakDetectionMethodConfig.Ch_Fwhm;

            this.numericUpDown11.Minimum = 1;
            this.numericUpDown11.Maximum = 1000;
            this.numericUpDown11.Increment = 1;
            this.numericUpDown11.Value = (decimal)FWHMPeakDetectionMethodConfig.Width_Fwhm;

            this.numericUpDown12.Minimum = 1;
            this.numericUpDown12.Maximum = 10000;
            this.numericUpDown12.Increment = 1;
            this.numericUpDown12.Value = (decimal)FWHMPeakDetectionMethodConfig.Min_Range;

            this.numericUpDown13.Minimum = 1;
            this.numericUpDown13.Maximum = 10000;
            this.numericUpDown13.Increment = 1;
            this.numericUpDown13.Value = (decimal)FWHMPeakDetectionMethodConfig.Max_Range;

            this.numericUpDown14.Minimum = 1;
            this.numericUpDown14.Maximum = 99;
            this.numericUpDown14.Increment = 1;
            this.numericUpDown14.Value = (decimal)FWHMPeakDetectionMethodConfig.Min_FWHM_Tol;

            this.numericUpDown15.Minimum = 101;
            this.numericUpDown15.Maximum = 199;
            this.numericUpDown15.Increment = 1;
            this.numericUpDown15.Value = (decimal)FWHMPeakDetectionMethodConfig.Max_FWHM_Tol;

            this.textBox17.Text = config.BackgroundSpectrumPathname;
            this.contentsLoading = false;
        }

        // Token: 0x06000523 RID: 1315 RVA: 0x000212F0 File Offset: 0x0001F4F0
        bool SaveFormContents(DeviceConfigInfo config)
        {
            try
            {
                if (config.Guid == null || config.Guid == "")
                {
                    config.Guid = Guid.NewGuid().ToString();
                }
                config.Name = this.textBox1.Text;
                config.Filename = this.textBox18.Text;
                DeviceType deviceType = (DeviceType)this.comboBox4.SelectedItem;
                ThermometerType thermometerType = (ThermometerType)this.comboBox1.SelectedItem;
                config.DeviceType = ((deviceType != null) ? deviceType.Id : "");
                config.ThermometerType = ((thermometerType != null) ? thermometerType.Id : "None");
                config.DefaultMeasurementTime = int.Parse(this.doubleTextBox5.Text);
                config.NumberOfChannels = int.Parse(this.integerTextBox1.Text);
                config.ChannelPitch = double.Parse(this.doubleTextBox6.Text);
                config.Note = this.textBox19.Text;
                this.inputDeviceForm.SaveFormContents(config.InputDeviceConfig);
                this.thermometerForm.SaveFormContents(config.ThermometerConfig);
                PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)config.EnergyCalibration;
                if (polynomialEnergyCalibration.PolynomialOrder >= 3)
                {
                    polynomialEnergyCalibration.Coefficients[3] = double.Parse(this.numericUpDown9.Text);
                }
                if (polynomialEnergyCalibration.PolynomialOrder == 4)
                {
                    polynomialEnergyCalibration.Coefficients[4] = double.Parse(this.numericUpDown8.Text);
                }

                polynomialEnergyCalibration.Coefficients[2] = double.Parse(this.numericUpDown1.Text);
                polynomialEnergyCalibration.Coefficients[1] = double.Parse(this.numericUpDown2.Text);
                polynomialEnergyCalibration.Coefficients[0] = double.Parse(this.numericUpDown7.Text);
                if (config.StabilizerConfig == null)
                {
                    config.StabilizerConfig = new StabilizerConfig();
                }
                config.StabilizerConfig.TargetPeaks = new List<TargetPeak>();
                foreach (object obj in this.tableModel3.Rows)
                {
                    Row row = (Row)obj;
                    TargetPeak targetPeak = new TargetPeak();
                    targetPeak.Nuclide = row.Cells[0].Text;
                    targetPeak.Energy = (decimal)row.Cells[1].Data;
                    targetPeak.Error = (decimal)row.Cells[2].Data;
                    config.StabilizerConfig.TargetPeaks.Add(targetPeak);
                }
                DoseRateConfig doseRateConfig = config.DoseRateConfig;
                doseRateConfig.Sensitivity = this.doubleTextBox1.GetValue();
                doseRateConfig.LowerBound = this.doubleTextBox2.GetValue();
                doseRateConfig.UpperBound = this.doubleTextBox3.GetValue();
                FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)config.PeakDetectionMethodConfig;
                FWHMPeakDetectionMethodConfig.Min_SNR = (double)this.numericUpDown4.Value;
                FWHMPeakDetectionMethodConfig.Max_Items = (int)this.numericUpDown3.Value;
                FWHMPeakDetectionMethodConfig.FWHM_AT_0 = (double)this.numericUpDown5.Value;
                FWHMPeakDetectionMethodConfig.Tolerance = (double)this.numericUpDown6.Value;
                FWHMPeakDetectionMethodConfig.Ch_Fwhm = (double)this.numericUpDown10.Value;
                FWHMPeakDetectionMethodConfig.Width_Fwhm = (double)this.numericUpDown11.Value;
                FWHMPeakDetectionMethodConfig.Min_Range = (double)this.numericUpDown12.Value;
                FWHMPeakDetectionMethodConfig.Max_Range = (double)this.numericUpDown13.Value;
                FWHMPeakDetectionMethodConfig.Min_FWHM_Tol = this.numericUpDown14.Value;
                FWHMPeakDetectionMethodConfig.Max_FWHM_Tol = this.numericUpDown15.Value;
                config.BackgroundSpectrumPathname = this.textBox17.Text;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        // Token: 0x06000524 RID: 1316 RVA: 0x00021668 File Offset: 0x0001F868
        void EnableForm()
        {
            this.tabControl1.Enabled = true;
        }

        // Token: 0x06000525 RID: 1317 RVA: 0x00021678 File Offset: 0x0001F878
        void DisableForm()
        {
            this.tabControl1.Enabled = false;
        }

        // Token: 0x06000526 RID: 1318 RVA: 0x00021688 File Offset: 0x0001F888
        void table1_SelectionChanged(object sender, SelectionEventArgs e)
        {
            if (this.reenter)
            {
                return;
            }
            this.reenter = true;
            DeviceConfigInfo deviceConfigInfo = null;
            Row row = null;
            if (this.table1.SelectedItems.Length > 0)
            {
                deviceConfigInfo = (DeviceConfigInfo)this.table1.SelectedItems[0].Tag;
                row = this.table1.SelectedItems[0];
            }
            if (deviceConfigInfo != this.activeDeviceConfig)
            {
                this.calibrationPoints.Clear();
            }
            if (!this.ConfirmSaveDeviceConfig())
            {
                this.ListupConfigFiles();
                this.reenter = false;
                return;
            }
            if (deviceConfigInfo != null)
            {
                this.activeDeviceConfig = deviceConfigInfo;
                this.tableModel1.Selections.Clear();
                this.tableModel1.Selections.AddCell(row.Index, 0);
                this.LoadFormContents(this.activeDeviceConfig);
                this.button4.Enabled = true;
                this.button12.Enabled = true;
                this.EnableForm();
            }
            else
            {
                this.button4.Enabled = false;
                this.button12.Enabled = false;
                this.DisableForm();
            }
            this.reenter = false;
        }

        // Token: 0x06000527 RID: 1319 RVA: 0x000217AC File Offset: 0x0001F9AC
        bool ConfirmSaveDeviceConfig()
        {
            if (this.activeDeviceConfig != null && this.activeDeviceConfig.Dirty)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGConfirmSaveConfig, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Yes)
                {
                    this.SaveFormContents(this.activeDeviceConfig);
                    if (!this.manager.SaveConfig(this.activeDeviceConfig))
                    {
                        MessageBox.Show(Resources.ERRDuplicateConfigName);
                        return false;
                    }
                }
                else
                {
                    this.activeDeviceConfig = this.manager.DeviceConfigMap[this.activeDeviceConfig.Guid].Clone();
                }
                this.ResetActiveDeviceConfigDirty();
                this.ListupConfigFiles();
            }
            return true;
        }

        // Token: 0x06000528 RID: 1320 RVA: 0x00021858 File Offset: 0x0001FA58
        void textBox1_TextChanged(object sender, EventArgs e)
        {
            this.textBox18.Text = this.ConvertNameToFilename(this.textBox1.Text);
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000529 RID: 1321 RVA: 0x0002187C File Offset: 0x0001FA7C
        void doubleTextBox5_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600052A RID: 1322 RVA: 0x00021884 File Offset: 0x0001FA84
        void integerTextBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600052B RID: 1323 RVA: 0x0002188C File Offset: 0x0001FA8C
        void doubleTextBox6_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600052C RID: 1324 RVA: 0x00021894 File Offset: 0x0001FA94
        void textBox19_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown8_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown8.Text);
                    this.numericUpDown8.ForeColor = Color.Black;
                    this.SetActiveDeviceConfigDirty();
                }
                catch
                {
                    PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    this.numericUpDown8.Text = pe.Coefficients[4].ToString();
                    this.numericUpDown8.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown9_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown9.Text);
                    this.numericUpDown9.ForeColor = Color.Black;
                    this.SetActiveDeviceConfigDirty();
                }
                catch
                {
                    PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    this.numericUpDown9.Text = pe.Coefficients[3].ToString();
                    this.numericUpDown9.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown1.Text);
                    this.numericUpDown1.ForeColor = Color.Black;
                    this.SetActiveDeviceConfigDirty();
                }
                catch
                {
                    PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    this.numericUpDown1.Text = pe.Coefficients[2].ToString();
                    this.numericUpDown1.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown2.Text);
                    this.numericUpDown2.ForeColor = Color.Black;
                    this.SetActiveDeviceConfigDirty();
                }
                catch
                {
                    PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    this.numericUpDown2.Text = pe.Coefficients[1].ToString();
                    this.numericUpDown2.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        void button13_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig.DeviceType == "AtomSpectraVCP")
            {
                AtomSpectraDeviceConfig deviceconfig = (AtomSpectraDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                AtomSpectraVCPIn device;
                List<AtomSpectraVCPIn> instances = AtomSpectraVCPIn.getAllInstances();
                bool runexist = false;
                if (instances.Count > 0)
                {
                    device = instances[0];
                    runexist = true;
                }
                else
                {
                    device = new AtomSpectraVCPIn(this.activeDeviceConfig.Guid);
                    device.setPort(deviceconfig.ComPortName);
                }

                try
                {
                    device.sendCommand("-cal");
                    String result = device.getCommandOutput(2000);
                    string[] separator = new string[] { "\r\n" };
                    string[] result_arr = result.Split(separator, StringSplitOptions.None);
                    string[] CalibrationCoefficients = new string[5];
                    if (result != null)
                    {
                        CalibrationCoefficients[0] = result_arr[0] + result_arr[1];
                        CalibrationCoefficients[1] = result_arr[2] + result_arr[3];
                        CalibrationCoefficients[2] = result_arr[4] + result_arr[5];
                        CalibrationCoefficients[3] = result_arr[6] + result_arr[7];
                        CalibrationCoefficients[4] = result_arr[8] + result_arr[9];

                        string result_str = "";
                        for (int i = 0; i < 10; i++)
                        {
                            result_str = result_str + result_arr[i];
                        }

                        byte[] bytes = Encoding.ASCII.GetBytes(result_str);
                        uint crc32 = Crc32.Compute(bytes);

                        if (uint.Parse(result_arr[10], System.Globalization.NumberStyles.AllowHexSpecifier) != crc32)
                        {
                            MessageBox.Show(Resources.ERRIncorrectCRC);
                            return;
                        }

                        PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                        List<double> coeff_list = new List<double>();
                        for (int i = 0; i < CalibrationCoefficients.Length; i++)
                        {
                            if (CalibrationCoefficients[i] != "FFFFFFFFFFFFFFFF")
                            {
                                byte[] floatVals = BitConverter.GetBytes(ulong.Parse(CalibrationCoefficients[i], System.Globalization.NumberStyles.AllowHexSpecifier));
                                coeff_list.Add(BitConverter.ToDouble(floatVals, 0));
                            }
                        }
                        if (coeff_list.Count == 0)
                        {
                            MessageBox.Show(Resources.ERREmptyCoefficients);
                            device.Dispose();
                            return;
                        }
                        this.numericUpDown1.Text = "0";
                        this.numericUpDown2.Text = "0";
                        this.numericUpDown7.Text = "0";
                        this.numericUpDown8.Text = "0";
                        this.numericUpDown9.Text = "0";
                        polynomialEnergyCalibration.PolynomialOrder = coeff_list.Count - 1;
                        polynomialEnergyCalibration.Coefficients = coeff_list.ToArray();
                        if (polynomialEnergyCalibration.PolynomialOrder >= 2)
                        {
                            this.numericUpDown7.Text = polynomialEnergyCalibration.Coefficients[0].ToString();
                            this.numericUpDown2.Text = polynomialEnergyCalibration.Coefficients[1].ToString();
                            this.numericUpDown1.Text = polynomialEnergyCalibration.Coefficients[2].ToString();
                        }
                        if (polynomialEnergyCalibration.PolynomialOrder >= 3)
                        {
                            this.numericUpDown9.Text = polynomialEnergyCalibration.Coefficients[3].ToString();
                        }
                        if (polynomialEnergyCalibration.PolynomialOrder == 4)
                        {
                            this.numericUpDown8.Text = polynomialEnergyCalibration.Coefficients[4].ToString();
                        }
                    }
                    else
                    {
                        MessageBox.Show(String.Format(Resources.ERRReadDataFromPort, deviceconfig.ComPortName));
                    }
                    if (!runexist)
                    {
                        device.Dispose();
                    }
                    SetActiveDeviceConfigDirty();
                }
                catch
                {
                    MessageBox.Show(Resources.ERRReadDataFromPort_Empty);
                }
            }
        }

        void button14_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig.DeviceType == "AtomSpectraVCP")
            {
                if (this.button6.Enabled)
                {
                    MessageBox.Show(Resources.MSGSaveBeforeWritingData);
                    return;
                }
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    List<string> result_list = new List<string>();
                    for (int i = 0; i < polynomialEnergyCalibration.Coefficients.Length; i++)
                    {
                        string result_str = BitConverter.DoubleToInt64Bits(polynomialEnergyCalibration.Coefficients[i]).ToString("X");
                        if (result_str == "0")
                        {
                            result_list.Add("00000000");
                            result_list.Add("00000000");
                        }
                        else
                        {
                            result_list.Add(result_str.Substring(0, result_str.Length / 2));
                            result_list.Add(result_str.Substring(result_str.Length / 2));
                        }
                    }

                    if (result_list.Count < 9)
                    {
                        for (int i = result_list.Count; i <= 9; i++)
                        {
                            result_list.Add("00000000");
                        }
                    }

                    string result_string = "";
                    for (int i = 0; i < 10; i++)
                    {
                        result_string = result_string + result_list[i];
                    }

                    byte[] bytes = Encoding.ASCII.GetBytes(result_string);
                    uint crc32 = Crc32.Compute(bytes);

                    result_list.Add(crc32.ToString("X"));

                    bool commands_accepted = true;
                    System.Diagnostics.Trace.WriteLine("commands_accepted = " + commands_accepted);
                    AtomSpectraDeviceConfig deviceconfig = (AtomSpectraDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                    AtomSpectraVCPIn device;
                    List<AtomSpectraVCPIn> instances = AtomSpectraVCPIn.getAllInstances();
                    bool runexist = false;
                    if (instances.Count > 0)
                    {
                        device = instances[0];
                        runexist = true;
                    }
                    else
                    {
                        device = new AtomSpectraVCPIn(this.activeDeviceConfig.Guid);
                        device.setPort(deviceconfig.ComPortName);
                        System.Threading.Thread.Sleep(2000);
                    }
                    string status_msg = "";
                    for (int i = 0; i < result_list.Count; i++)
                    {
                        device.sendCommand("-cal " + i + " " + result_list[i]);
                        bool result = device.waitForAnswer("ok", 2000);
                        commands_accepted &= result;
                        System.Diagnostics.Trace.WriteLine("result = " + result);
                        status_msg = status_msg + "-cal " + i + " " + result_list[i] + " -- result: " + result + Environment.NewLine;
                    }
                    if (!runexist)
                    {
                        device.Dispose();
                    }
                    Cursor.Current = Cursors.Default;
                    if (commands_accepted)
                    {
                        MessageBox.Show(Resources.MSGCoefficientsUploadedSuccesfull);
                    }
                    else
                    {
                        MessageBox.Show(Resources.ERRUploadCoefficeintsToDevice + Environment.NewLine + status_msg); ;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Resources.ERRUploadCoefficeintsToDevice + Environment.NewLine + ex.Message);
                }
            }
        }

        void numericUpDown7_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                try
                {
                    double result = fromStringtoDouble(this.numericUpDown7.Text);
                    this.numericUpDown7.ForeColor = Color.Black;
                    this.SetActiveDeviceConfigDirty();
                }
                catch
                {
                    PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    this.numericUpDown1.Text = pe.Coefficients[0].ToString();
                    this.numericUpDown1.ForeColor = Color.Red;
                }
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000530 RID: 1328 RVA: 0x000218B4 File Offset: 0x0001FAB4
        void textBox17_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000531 RID: 1329 RVA: 0x000218BC File Offset: 0x0001FABC
        void button7_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            string text = this.textBox17.Text;
            if (text != null && !(text == ""))
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(text);
                openFileDialog.FileName = Path.GetFileName(text);
            }
            openFileDialog.Title = Resources.BackgroundSelectionDialogTitle;
            openFileDialog.Filter = Resources.SpectrumFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            this.textBox17.Text = openFileDialog.FileName;
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000532 RID: 1330 RVA: 0x00021958 File Offset: 0x0001FB58
        public void SetActiveDeviceConfigDirty()
        {
            if (this.contentsLoading)
            {
                return;
            }
            if (this.activeDeviceConfig == null)
            {
                return;
            }
            this.activeDeviceConfig.Dirty = true;
            this.button6.Enabled = true;
        }

        // Token: 0x06000533 RID: 1331 RVA: 0x0002198C File Offset: 0x0001FB8C
        public void ResetActiveDeviceConfigDirty()
        {
            if (this.activeDeviceConfig == null)
            {
                return;
            }
            this.activeDeviceConfig.Dirty = false;
            this.button6.Enabled = false;
        }

        // Token: 0x06000534 RID: 1332 RVA: 0x000219B4 File Offset: 0x0001FBB4
        void button8_Click(object sender, EventArgs e)
        {
            MainForm mainForm = (MainForm)base.Owner;
            DocEnergySpectrum activeDocument = mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                this.channelPickupProcessing = true;
                activeDocument.EnergySpectrumView.ChannelPickuped += this.energySpectrumView_ChannelPickuped;
            }
            this.UpdateMultipointButtonState();
        }

        // Token: 0x06000535 RID: 1333 RVA: 0x00021A04 File Offset: 0x0001FC04
        void UpdateMultipointButtonState()
        {
            this.button1.Enabled = (this.calibrationPoints.Count > 0 && !this.channelPickupProcessing && this.multipointModified);
            this.button8.Enabled = (this.calibrationPoints.Count < 5 && !this.channelPickupProcessing);
            this.button9.Enabled = (this.calibrationPoints.Count > 0 && !this.channelPickupProcessing);
            this.button11.Enabled = this.channelPickupProcessing;
            if (this.calibrationDone)
            {
                this.label36.Text = Resources.MSGCalibrationDone;
                return;
            }
            if (this.channelPickupProcessing || this.calibrationPoints.Count < 5)
            {
                this.label36.Text = Resources.MSGPickUpCalibrationPoint;
                return;
            }
            this.label36.Text = Resources.MSGProceedCalibration;
        }

        // Token: 0x06000536 RID: 1334 RVA: 0x00021B08 File Offset: 0x0001FD08
        void energySpectrumView_ChannelPickuped(object sender, ChannelPickupedEventArgs e)
        {
            if (!this.channelPickupProcessing)
            {
                return;
            }
            decimal energy = Math.Round((decimal)this.activeDeviceConfig.EnergyCalibration.ChannelToEnergy((double)e.Channel), 2);
            CalibrationPoint item = new CalibrationPoint(e.Channel, energy);
            this.calibrationPoints.Add(item);
            this.multipointModified = true;
            this.calibrationDone = false;
            this.ShowCalibrationPoints();
            this.ClearChannelPickupState();
        }

        // Token: 0x06000537 RID: 1335 RVA: 0x00021B7C File Offset: 0x0001FD7C
        void button11_Click(object sender, EventArgs e)
        {
            this.ClearChannelPickupState();
        }

        // Token: 0x06000538 RID: 1336 RVA: 0x00021B84 File Offset: 0x0001FD84
        void ClearChannelPickupState()
        {
            MainForm mainForm = (MainForm)base.Owner;
            DocEnergySpectrum activeDocument = mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                activeDocument.EnergySpectrumView.ChannelPickuped -= this.energySpectrumView_ChannelPickuped;
            }
            this.channelPickupProcessing = false;
            this.UpdateMultipointButtonState();
        }

        // Token: 0x06000539 RID: 1337 RVA: 0x00021BD4 File Offset: 0x0001FDD4
        void button9_Click(object sender, EventArgs e)
        {
            int num;
            if (this.table2.SelectedItems.Length >= 1)
            {
                num = this.table2.SelectedItems[0].Index;
            }
            else
            {
                num = 0;
            }
            if (num < 0 && num >= this.calibrationPoints.Count)
            {
                return;
            }
            this.calibrationPoints.RemoveAt(num);
            this.tableModel2.Selections.Clear();
            this.multipointModified = true;
            this.calibrationDone = false;
            this.ShowCalibrationPoints();
            this.UpdateMultipointButtonState();
        }

        // Token: 0x0600053A RID: 1338 RVA: 0x00021C68 File Offset: 0x0001FE68
        void ShowCalibrationPoints()
        {
            int num = 1;
            this.table2.SuspendLayout();
            this.tableModel2.Rows.Clear();
            this.calibrationPoints.Sort();
            foreach (CalibrationPoint calibrationPoint in this.calibrationPoints)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(num.ToString()));
                row.Cells.Add(new Cell(calibrationPoint.Channel));
                row.Cells.Add(new Cell(calibrationPoint.Energy));
                this.tableModel2.Rows.Add(row);
                num++;
            }
            this.table2.ResumeLayout();
        }

        // Token: 0x0600053B RID: 1339 RVA: 0x00021D5C File Offset: 0x0001FF5C
        void table2_SelectionChanged(object sender, SelectionEventArgs e)
        {
            this.UpdateMultipointButtonState();
        }

        // Token: 0x0600053C RID: 1340 RVA: 0x00021D64 File Offset: 0x0001FF64
        void table2_EditingStopped(object sender, CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;
            try
            {
                if (e.Column == 1)
                {
                    string text = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Channel = (int)decimal.Parse(text);
                    this.multipointModified = true;
                    this.calibrationDone = false;
                    this.UpdateMultipointButtonState();
                }
                else if (e.Column == 2)
                {
                    string text2 = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Energy = decimal.Parse(text2);
                    this.multipointModified = true;
                    this.UpdateMultipointButtonState();
                }
                else if (e.Column == 4)
                {
                    string text2 = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Energy = decimal.Parse(text2);
                    this.multipointModified = true;
                    this.calibrationDone = false;
                    this.UpdateMultipointButtonState();
                }
            }
            catch (Exception)
            {
                e.Cancel = true;
            }
        }

        // Token: 0x0600053D RID: 1341 RVA: 0x00021E48 File Offset: 0x00020048
        void button1_Click(object sender, EventArgs e)
        {
            PolynomialEnergyCalibration energyCalibration = new PolynomialEnergyCalibration(this.activeDeviceConfig.NumberOfChannels);
            for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
            {
                energyCalibration.Coefficients[i] = 0.0;
            }
            double[] matrix;
            List<CalibrationPoint> points = this.calibrationPoints;
            if (points.Count == 1)
            {
                CalibrationPoint zero = new CalibrationPoint(0, 0);
                points.Add(zero);
            }
            try
            {
                if (this.calibrationPoints.Count >= 5)
                {
                    matrix = Utils.CalibrationSolver.Solve(points, 4);
                }
                else
                {
                    matrix = Utils.CalibrationSolver.Solve(points, points.Count - 1);
                }
                if (matrix == null) throw new Exception("Error");
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRInvalidChannelOrEnergyValues);
                return;
            }

            energyCalibration.Coefficients = new double[matrix.Length];
            energyCalibration.PolynomialOrder = matrix.Length - 1;
            energyCalibration.Coefficients = matrix;

            if (!energyCalibration.CheckCalibration())
            {
                MessageBox.Show(Resources.CalibrationFunctionError);
                return;
            }
            this.numericUpDown1.Text = "0";
            this.numericUpDown2.Text = "0";
            this.numericUpDown7.Text = "0";
            this.numericUpDown8.Text = "0";
            this.numericUpDown9.Text = "0";
            if (energyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = energyCalibration.Coefficients[2].ToString();
            }
            if (energyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown9.Text = energyCalibration.Coefficients[3].ToString();
            }
            if (energyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown8.Text = energyCalibration.Coefficients[4].ToString();
            }
            this.numericUpDown2.Text = energyCalibration.Coefficients[1].ToString();
            this.numericUpDown7.Text = energyCalibration.Coefficients[0].ToString();
            if (!energyCalibration.CheckCalibration())
            {
                MessageBox.Show(Resources.CalibrationFunctionError);
                return;
            }
            this.multipointModified = false;
            this.calibrationDone = true;
            this.UpdateMultipointButtonState();
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600053E RID: 1342 RVA: 0x00022254 File Offset: 0x00020454
        public void SetLowerThreshold(DeviceConfigInfo deviceConfig, double threshold)
        {
            bool flag = false;
            foreach (object obj in this.tableModel1.Rows)
            {
                Row row = (Row)obj;
                DeviceConfigInfo deviceConfigInfo = (DeviceConfigInfo)row.Tag;
                if (deviceConfig.Guid == deviceConfigInfo.Guid)
                {
                    flag = true;
                    this.tableModel1.Selections.Clear();
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                    break;
                }
            }
            if (!flag)
            {
                return;
            }
            this.tabControl1.SelectedIndex = 1;
            TextBox lowerThresholdTextBox = this.inputDeviceForm.LowerThresholdTextBox;
            if (lowerThresholdTextBox != null)
            {
                lowerThresholdTextBox.Text = threshold.ToString();
                lowerThresholdTextBox.SelectAll();
                lowerThresholdTextBox.Focus();
            }
        }

        // Token: 0x0600053F RID: 1343 RVA: 0x00022350 File Offset: 0x00020550
        public void SetUpperThreshold(DeviceConfigInfo deviceConfig, double threshold)
        {
            bool flag = false;
            foreach (object obj in this.tableModel1.Rows)
            {
                Row row = (Row)obj;
                DeviceConfigInfo deviceConfigInfo = (DeviceConfigInfo)row.Tag;
                if (deviceConfig.Guid == deviceConfigInfo.Guid)
                {
                    flag = true;
                    this.tableModel1.Selections.Clear();
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                    break;
                }
            }
            if (!flag)
            {
                return;
            }
            this.tabControl1.SelectedIndex = 1;
            TextBox upperThresholdTextBox = this.inputDeviceForm.UpperThresholdTextBox;
            if (upperThresholdTextBox != null)
            {
                upperThresholdTextBox.Text = threshold.ToString();
                upperThresholdTextBox.SelectAll();
                upperThresholdTextBox.Focus();
            }
        }

        // Token: 0x06000540 RID: 1344 RVA: 0x0002244C File Offset: 0x0002064C
        public void ShowStabilizerForm(DeviceConfigInfo deviceConfig)
        {
            bool flag = false;
            foreach (object obj in this.tableModel1.Rows)
            {
                Row row = (Row)obj;
                DeviceConfigInfo deviceConfigInfo = (DeviceConfigInfo)row.Tag;
                if (deviceConfig.Guid == deviceConfigInfo.Guid)
                {
                    flag = true;
                    this.tableModel1.Selections.Clear();
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                    break;
                }
            }
            if (!flag)
            {
                return;
            }
            this.tabControl1.SelectedIndex = 3;
        }

        // Token: 0x06000541 RID: 1345 RVA: 0x00022514 File Offset: 0x00020714
        void numericUpDown4_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000542 RID: 1346 RVA: 0x00022578 File Offset: 0x00020778
        void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000543 RID: 1347 RVA: 0x000225FC File Offset: 0x000207FC
        void numericUpDown5_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000544 RID: 1348 RVA: 0x00022604 File Offset: 0x00020804
        void numericUpDown6_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown10_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown11_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown12_ValueChanged(object sender, EventArgs e)
        {
            if (this.numericUpDown12.Value >= this.numericUpDown13.Value)
            {
                this.numericUpDown13.Value = this.numericUpDown12.Value + 1;
            }
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown13_ValueChanged(object sender, EventArgs e)
        {
            if (this.numericUpDown12.Value >= this.numericUpDown13.Value)
            {
                this.numericUpDown13.Value = this.numericUpDown12.Value + 1;
            }
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown14_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDown15_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000545 RID: 1349 RVA: 0x0002260C File Offset: 0x0002080C
        void comboBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.deviceFormLoading || this.selectedDeviceIndex == this.comboBox4.SelectedIndex)
            {
                return;
            }
            if (DialogResult.No == MessageBox.Show(Resources.MSGDeviceTypeChanging, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation))
            {
                this.deviceFormLoading = true;
                this.comboBox4.SelectedIndex = this.selectedDeviceIndex;
                this.deviceFormLoading = false;
                return;
            }
            this.selectedDeviceIndex = this.comboBox4.SelectedIndex;
            DeviceType deviceType = (DeviceType)this.comboBox4.SelectedItem;
            this.deviceFormLoading = true;
            this.PrepareDeviceForm(deviceType);
            InputDeviceConfig inputDeviceConfig = (InputDeviceConfig)Activator.CreateInstance(deviceType.DeviceConfigType);
            this.activeDeviceConfig.InputDeviceConfig = inputDeviceConfig;
            this.inputDeviceForm.LoadFormContents(inputDeviceConfig);
            this.deviceFormLoading = false;
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000546 RID: 1350 RVA: 0x000226E0 File Offset: 0x000208E0
        void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.deviceFormLoading || this.selectedThermometerIndex == this.comboBox1.SelectedIndex)
            {
                return;
            }
            if (DialogResult.No == MessageBox.Show(Resources.MSGThermometerTypeChanging, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation))
            {
                this.deviceFormLoading = true;
                this.comboBox1.SelectedIndex = this.selectedThermometerIndex;
                this.deviceFormLoading = false;
                return;
            }
            this.selectedThermometerIndex = this.comboBox1.SelectedIndex;
            ThermometerType thermometerType = (ThermometerType)this.comboBox1.SelectedItem;
            this.deviceFormLoading = true;
            this.PrepareThermometerForm(thermometerType);
            ThermometerConfig thermometerConfig = (ThermometerConfig)Activator.CreateInstance(thermometerType.ThermometerConfigType);
            if (thermometerType.Id == "None")
            {
                this.activeDeviceConfig.ThermometerConfig = null;
            }
            else
            {
                this.activeDeviceConfig.ThermometerConfig = thermometerConfig;
            }
            this.thermometerForm.LoadFormContents(thermometerConfig);
            this.deviceFormLoading = false;
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000547 RID: 1351 RVA: 0x000227D8 File Offset: 0x000209D8
        void doubleTextBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000548 RID: 1352 RVA: 0x000227E0 File Offset: 0x000209E0
        void doubleTextBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x06000549 RID: 1353 RVA: 0x000227E8 File Offset: 0x000209E8
        void doubleTextBox3_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
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

        // Token: 0x0600054A RID: 1354 RVA: 0x000227F0 File Offset: 0x000209F0
        void button10_Click(object sender, EventArgs e)
        {
            Row row = new Row();
            row.Cells.Add(new Cell(""));
            row.Cells.Add(new Cell(0m));
            row.Cells.Add(new Cell(10m));
            this.tableModel3.Rows.Add(row);
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600054B RID: 1355 RVA: 0x00022870 File Offset: 0x00020A70
        void button2_Click(object sender, EventArgs e)
        {
            if (this.table3.SelectedItems.Length <= 0)
            {
                return;
            }
            Row row = this.table3.SelectedItems[0];
            this.tableModel3.Rows.RemoveAt(row.Index);
            this.SetActiveDeviceConfigDirty();
        }

        // Token: 0x0600054C RID: 1356 RVA: 0x000228C4 File Offset: 0x00020AC4
        void table3_EditingStopped(object sender, CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;
            try
            {
                if (e.Column == 1)
                {
                    string text = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.SetActiveDeviceConfigDirty();
                }
                else if (e.Column == 2)
                {
                    string text2 = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.SetActiveDeviceConfigDirty();
                }
            }
            catch (Exception)
            {
                e.Cancel = true;
            }
        }

        // Token: 0x040002BB RID: 699
        DeviceConfigManager manager = DeviceConfigManager.GetInstance();

        // Token: 0x040002BC RID: 700
        DeviceConfigInfo activeDeviceConfig;

        // Token: 0x040002BD RID: 701
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x040002BE RID: 702
        bool contentsLoading;

        // Token: 0x040002BF RID: 703
        bool channelPickupProcessing;

        // Token: 0x040002C0 RID: 704
        bool calibrationDone;

        // Token: 0x040002C1 RID: 705
        bool multipointModified;

        // Token: 0x040002C2 RID: 706
        List<CalibrationPoint> calibrationPoints = new List<CalibrationPoint>();

        // Token: 0x040002C3 RID: 707
        InputDeviceForm inputDeviceForm;

        // Token: 0x040002C4 RID: 708
        ThermometerForm thermometerForm;

        // Token: 0x040002C5 RID: 709
        bool reenter;

        // Token: 0x040002C6 RID: 710
        int selectedDeviceIndex = -1;

        // Token: 0x040002C7 RID: 711
        bool deviceFormLoading;

        // Token: 0x040002C8 RID: 712
        int selectedThermometerIndex = -1;
    }
}
