using BecquerelMonitor.Hash;
using BecquerelMonitor.Properties;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
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
            this.expGaussExpLeftLabelText = this.leftSkewlabel.Text;
            this.expGaussExpRightLabelText = this.rightSkewlabel.Text;
            this.BuildEfficiencyTab();
            this.BuildDoseRateTab();
            this.HideTempcoTabPage();
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
            this.groupBox2.Top = 24;
            this.peakSpecgroupBox.Top = this.groupBox2.Bottom + 6;
        }

        void HideTempcoTabPage()
        {
            if (this.tabControl1.TabPages.Contains(this.tabPage6))
            {
                this.tabControl1.TabPages.Remove(this.tabPage6);
            }
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
                // Actually cancel the close. A bare return let the form close anyway
                // while skipping the cleanup below (the 50 ms AudioInputDeviceForm timer
                // then kept poking disposed controls, and ChannelPickuped stayed
                // subscribed).
                e.Cancel = true;
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
        /// <summary>
        /// Смена вкладки: сперва предложить сохранить набранное.
        /// </summary>
        /// <remarks>
        /// ⛔ `A18`. Прежде обработчик был объявлен с `EventArgs`, а не с
        /// `TabControlCancelEventArgs`. Это КОМПИЛИРУЕТСЯ (у делегата
        /// параметр можно принимать базовым типом) и обработчик
        /// вызывался — но `e.Cancel` он не видел, поэтому его `return`
        /// при отказе не отменял НИЧЕГО и вкладка переключалась всё
        /// равно: человек уходил со страницы, чьи правки сохранить не
        /// удалось.
        ///
        /// Запереть человека на вкладке это не может: отказ наступает
        /// только после его же ответа «да, сохранить», а ответ «нет»
        /// возвращает конфигурацию с диска и переход пропускает — тот же
        /// порядок, что у `*_FormClosing` в этой форме.
        /// </remarks>
        private void tabControl1_Selecting(object sender, TabControlCancelEventArgs e)
        {
            if (!this.ConfirmSaveDeviceConfig())
            {
                e.Cancel = true;
            }
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
                // ⛔ `A19`. Форма гасится ТОЛЬКО при удавшемся удалении.
                // Прежде это делалось безусловно, и после отказа (файл
                // занят, нет прав) строка возвращалась в список — но
                // форма пустела и выбор слетал: человеку надо было
                // заново ткнуть в строку, которую он и не терял.
                if (!this.manager.DeleteConfig(this.activeDeviceConfig))
                {
                    return;
                }
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
                string text = Resources.NewDeviceConfigPrefix + "(" + i.ToString(CultureInfo.InvariantCulture) + ").xml";
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
                    this.tabPage3.Controls.Clear();
                    this.tabPage3.Controls.Add(this.inputDeviceForm);
                    switch (type.Name)
                    {
                        case "AtomSpectraVCP":
                            {
                                this.button13.Enabled = true;
                                this.button13.Visible = true;
                                this.button14.Enabled = true;
                                this.button14.Visible = true;

                                this.integerTextBox1.Enabled = true;
                                this.doubleTextBox6.Enabled = false;
                                this.doubleTextBox6.Text = "1";
                                this.ActiveDeviceConfig.ChannelPitch = 1;
                                break;
                            }
                        case "RadiaCode":
                            {
                                this.button13.Enabled = true;
                                this.button13.Visible = true;
                                this.button14.Enabled = false;
                                this.button14.Visible = false;

                                // set channels 1024, pitch = 1
                                this.integerTextBox1.Enabled = false;
                                this.doubleTextBox6.Enabled = false;
                                this.integerTextBox1.Text = "1024";
                                this.doubleTextBox6.Text = "1";
                                this.ActiveDeviceConfig.NumberOfChannels = 1024;
                                this.ActiveDeviceConfig.ChannelPitch = 1;
                                break;
                            }
                        case "Obsidian":
                            {
                                this.button13.Enabled = true;
                                this.button13.Visible = true;
                                this.button14.Enabled = false;
                                this.button14.Visible = false;

                                this.integerTextBox1.Enabled = false;
                                this.doubleTextBox6.Enabled = false;
                                this.integerTextBox1.Text = "1024";
                                this.doubleTextBox6.Text = "1";
                                this.ActiveDeviceConfig.NumberOfChannels = 1024;
                                this.ActiveDeviceConfig.ChannelPitch = 1;
                                break;
                            }
                        default:
                            {
                                this.button13.Enabled = false;
                                this.button13.Visible = false;
                                this.button14.Enabled = false;
                                this.button14.Visible = false;

                                this.integerTextBox1.Enabled = true;
                                this.doubleTextBox6.Enabled = true;
                                break;
                            }
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
                this.tabPage3.Controls.Clear();
                this.tabPage3.Controls.Add(this.inputDeviceForm);
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
                    this.tabPage6.Controls.Clear();
                    this.tabPage6.Controls.Add(this.thermometerForm);
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
                this.tabPage6.Controls.Clear();
                this.tabPage6.Controls.Add(this.thermometerForm);
            }
        }

        // Token: 0x06000522 RID: 1314 RVA: 0x00020E94 File Offset: 0x0001F094
        void LoadFormContents(DeviceConfigInfo config)
        {
            this.contentsLoading = true;
            this.LoadEfficiencyTab(config);
            this.LoadDoseRateTab(config);
            this.textBox1.Text = config.Name;
            this.doubleTextBox5.Text = config.DefaultMeasurementTime.ToString(CultureInfo.InvariantCulture);
            this.integerTextBox1.Text = config.NumberOfChannels.ToString(CultureInfo.InvariantCulture);
            this.doubleTextBox6.Text = config.ChannelPitch.ToString(CultureInfo.InvariantCulture);
            this.textBox19.Text = config.Note;
            this.deviceFormLoading = true;
            DeviceType type = null;
            DeviceType.DeviceTypeMap.TryGetValue(config.DeviceType, out type);
            try
            {
                this.PrepareDeviceForm(type);
            }
            catch (Exception)
            {
                // ⛔ `T106`. Метод зовут отражением из пробы, то есть это
                // безоконный путь (`S100`): модальное окно здесь вешало бы
                // прогон насмерть. Сторож этого не видел, пока не научился
                // разбирать вызовы через отражение.
                AppUi.Report(Resources.ERRBTNotSupportedByOS,
                             Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                this.PrepareDeviceForm(null);
                this.DisableForm();
            }

            if (this.inputDeviceForm != null)
            {
                this.inputDeviceForm.LoadFormContents(config.InputDeviceConfig);
            }
            ThermometerType type2 = null;
            ThermometerType.ThermometerTypeMap.TryGetValue(config.ThermometerType, out type2);
            this.PrepareThermometerForm(type2);
            this.thermometerForm.LoadFormContents(config.ThermometerConfig);
            this.deviceFormLoading = false;
            PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)config.EnergyCalibration;
            if (polynomialEnergyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown9.Text = polynomialEnergyCalibration.Coefficients[3].ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                this.numericUpDown9.Text = "0";
            }
            if (polynomialEnergyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown8.Text = polynomialEnergyCalibration.Coefficients[4].ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                this.numericUpDown8.Text = "0";

            }
            if (polynomialEnergyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = polynomialEnergyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                this.numericUpDown1.Text = "0";
            }
            this.numericUpDown2.Text = polynomialEnergyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);
            this.numericUpDown7.Text = polynomialEnergyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);
            this.ShowCalibrationPoints();
            this.UpdateMultipointButtonState();
            this.tableModel3.Rows.Clear();
            this.textBox16.Text = string.Empty;
            if (type != null && type.Name == "RadiaCode")
            {
                RadiaCodeDeviceConfig rc_config = (RadiaCodeDeviceConfig)config.InputDeviceConfig;
                if (rc_config.RC_EnergyCalibration != null)
                {
                    this.textBox16.Text = rc_config.RC_EnergyCalibration.ToString();
                    this.button14.Enabled = true;
                    this.button14.Visible = true;
                }
                else
                {
                    this.button14.Enabled = false;
                    this.button14.Visible = true;
                }
            }
            else if (type != null && type.Name == "Obsidian")
            {
                ObsidianDeviceConfig obs_config = (ObsidianDeviceConfig)config.InputDeviceConfig;
                this.button13.Enabled = true;
                this.button13.Visible = true;
                if (obs_config.OBS_EnergyCalibration != null)
                {
                    this.textBox16.Text = obs_config.OBS_EnergyCalibration.ToString();
                    this.button14.Enabled = true;
                    this.button14.Visible = true;
                }
                else
                {
                    this.button14.Enabled = false;
                    this.button14.Visible = true;
                }
            }

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
            this.tableModel4.Rows.Clear();
            if (doseRateConfig != null && doseRateConfig.DoseRateCalibrationPoints != null)
            {
                foreach(DoseRateCalibrationPoint point in doseRateConfig.DoseRateCalibrationPoints)
                {
                    Row row = new Row();
                    row.Cells.Add(new Cell(point.LowerBound));
                    row.Cells.Add(new Cell(point.UpperBound));
                    row.Cells.Add(new Cell(point.CPS));
                    row.Cells.Add(new Cell(point.EtalonDoseRateValue));
                    this.tableModel4.Rows.Add(row);
                }
            }
            FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)config.PeakDetectionMethodConfig;
            this.numericUpDown4.Minimum = 1;
            this.numericUpDown4.Maximum = 10000;
            this.numericUpDown4.Increment = 1;
            this.numericUpDown4.Value = (decimal)FWHMPeakDetectionMethodConfig.Min_SNR;

            this.numericUpDown3.Minimum = 1;
            this.numericUpDown3.Maximum = 1000;
            this.numericUpDown3.Increment = 1;
            this.numericUpDown3.Value = FWHMPeakDetectionMethodConfig.Max_Items;

            this.numericUpDown6.Minimum = 0;
            this.numericUpDown6.Maximum = 100;
            this.numericUpDown6.Increment = 1;
            this.numericUpDown6.Value = (decimal)FWHMPeakDetectionMethodConfig.Tolerance;

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

            this.numericUpDown16.Minimum = 256;
            this.numericUpDown16.Maximum = config.NumberOfChannels;
            this.numericUpDown16.Increment = 1;
            if (FWHMPeakDetectionMethodConfig.Ch_Concat < config.NumberOfChannels && FWHMPeakDetectionMethodConfig.Ch_Concat > 256)
            {
                this.numericUpDown16.Value = (decimal)FWHMPeakDetectionMethodConfig.Ch_Concat;
            } else
            {
                this.numericUpDown16.Value = (decimal)config.NumberOfChannels;
            }

            this.numericUpDownWidenFactor.Minimum = (decimal)1.0;
            this.numericUpDownWidenFactor.Maximum = (decimal)3.0;
            this.numericUpDownWidenFactor.Increment = (decimal)0.05;
            this.numericUpDownWidenFactor.Value = ClampNumericValue(this.numericUpDownWidenFactor, (decimal)FWHMPeakDetectionMethodConfig.PeakWidthWidenFactor);
            this.centroidComCheckBox.Checked = FWHMPeakDetectionMethodConfig.UseCenterOfMassCentroid;

            LoadPeakShapeControls(FWHMPeakDetectionMethodConfig.FwhmCalibration);


            this.textBox17.Text = config.BackgroundSpectrumPathname;

            List<ROIConfigData> rOIConfigDatas = ROIConfigManager.GetInstance().ROIConfigList;
            if (rOIConfigDatas != null || rOIConfigDatas.Count > 0) 
            {
            }

            this.contentsLoading = false;
        }

        public void LoadPeakFinderPresetContents(DeviceConfigInfo config)
        {
            this.contentsLoading = true;
            FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)config.PeakDetectionMethodConfig;
            this.numericUpDown14.Value = FWHMPeakDetectionMethodConfig.Min_FWHM_Tol;
            this.numericUpDown15.Value = FWHMPeakDetectionMethodConfig.Max_FWHM_Tol;
            this.numericUpDown16.Value = FWHMPeakDetectionMethodConfig.Ch_Concat;
            this.numericUpDownWidenFactor.Value = ClampNumericValue(this.numericUpDownWidenFactor, (decimal)FWHMPeakDetectionMethodConfig.PeakWidthWidenFactor);
            this.centroidComCheckBox.Checked = FWHMPeakDetectionMethodConfig.UseCenterOfMassCentroid;
            this.numericUpDown12.Value = (decimal)FWHMPeakDetectionMethodConfig.Min_Range;
            LoadPeakShapeControls(FWHMPeakDetectionMethodConfig.FwhmCalibration);
            this.contentsLoading = false;
        }

        static decimal ClampNumericValue(NumericUpDown numericUpDown, decimal value)
        {
            return Math.Min(numericUpDown.Maximum, Math.Max(numericUpDown.Minimum, value));
        }

        private static decimal ClampPeakShapeParameter(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value))
            {
                return 1.0m;
            }

            if (value <= 0.1)
            {
                return 0.1m;
            }
            if (value >= 5.0)
            {
                return 5.0m;
            }

            return (decimal)value;
        }

        void LoadPeakShapeControls(FwhmCalibration fwhmCalibration)
        {
            expGaussExpLeftValue = ClampPeakShapeParameter(fwhmCalibration.ExpGaussExpLeftTail);
            expGaussExpRightValue = ClampPeakShapeParameter(fwhmCalibration.ExpGaussExpRightTail);
            voigtSigmaValue = ClampPeakShapeParameter(fwhmCalibration.VoigtSigma);
            voigtGammaValue = ClampPeakShapeParameter(fwhmCalibration.VoigtGamma);

            int peakType = fwhmCalibration.PeakType;
            if (!FwhmCalibration.IsSupportedPeakType(peakType))
            {
                peakType = FwhmCalibration.GaussianPeakType;
                fwhmCalibration.PeakType = peakType;
            }

            this.peakTypecomboBox.SelectedIndex = peakType;
            UpdatePeakShapeControlState();
        }

        void StoreCurrentPeakShapeParameters()
        {
            if (this.contentsLoading)
            {
                return;
            }

            if (this.peakTypecomboBox.SelectedIndex == FwhmCalibration.ExpGaussExpPeakType)
            {
                expGaussExpLeftValue = leftSkewnumericUpDown.Value;
                expGaussExpRightValue = rightSkewnumericUpDown.Value;
            }
            else if (this.peakTypecomboBox.SelectedIndex == FwhmCalibration.VoigtPeakType)
            {
                voigtSigmaValue = leftSkewnumericUpDown.Value;
                voigtGammaValue = rightSkewnumericUpDown.Value;
            }
        }

        void UpdatePeakShapeControlState()
        {
            bool showParameters = this.peakTypecomboBox.SelectedIndex != FwhmCalibration.GaussianPeakType;
            this.leftSkewlabel.Visible = showParameters;
            this.leftSkewnumericUpDown.Visible = showParameters;
            this.rightSkewlabel.Visible = showParameters;
            this.rightSkewnumericUpDown.Visible = showParameters;

            if (!showParameters)
            {
                return;
            }

            if (this.peakTypecomboBox.SelectedIndex == FwhmCalibration.ExpGaussExpPeakType)
            {
                this.leftSkewlabel.Text = expGaussExpLeftLabelText;
                this.rightSkewlabel.Text = expGaussExpRightLabelText;
                this.leftSkewnumericUpDown.Value = expGaussExpLeftValue;
                this.rightSkewnumericUpDown.Value = expGaussExpRightValue;
            }
            else
            {
                this.leftSkewlabel.Text = Resources.ResourceManager.GetString("VoigtRelativeSigmaLabel");
                this.rightSkewlabel.Text = Resources.ResourceManager.GetString("VoigtRelativeGammaLabel");
                this.leftSkewnumericUpDown.Value = voigtSigmaValue;
                this.rightSkewnumericUpDown.Value = voigtGammaValue;
            }
        }

        // Token: 0x06000523 RID: 1315 RVA: 0x000212F0 File Offset: 0x0001F4F0
        bool SaveFormContents(DeviceConfigInfo config)
        {
            try
            {
                // ⛔ Три числа с шапки окна разбираются ДО первой записи (`A6`):
                //    прежде имя, файл и тип прибора были уже переписаны, когда
                //    разбор спотыкался на времени измерения или числе каналов, —
                //    и конфигурация, одна на всех, оставалась смесью нового со
                //    старым. Остаток известен и НЕ закрыт здесь: ниже по методу
                //    разбор коэффициентов калибровки стоит уже после записей и
                //    после двух вложенных форм.
                int defaultMeasurementTime = UserNumber.ParseInt(this.doubleTextBox5.Text);
                int numberOfChannels = UserNumber.ParseInt(this.integerTextBox1.Text);
                double channelPitch = UserNumber.ParseDouble(this.doubleTextBox6.Text);
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
                config.DefaultMeasurementTime = defaultMeasurementTime;
                config.NumberOfChannels = numberOfChannels;
                config.ChannelPitch = channelPitch;
                config.Note = this.textBox19.Text;
                this.SaveEfficiencyTab(config);
                if (config.InputDeviceConfig is RadiaCodeDeviceConfig)
                {
                    PolynomialEnergyCalibration cal = (PolynomialEnergyCalibration)config.EnergyCalibration;
                    if (cal.PolynomialOrder == 2)
                    {
                        RadiaCodeDeviceConfig rc_config = (RadiaCodeDeviceConfig)config.InputDeviceConfig;
                        rc_config.RC_EnergyCalibration = cal;
                    } else if (this.rc_EnergyCalibration != null)
                    {
                        RadiaCodeDeviceConfig rc_config = (RadiaCodeDeviceConfig)config.InputDeviceConfig;
                        rc_config.RC_EnergyCalibration = this.rc_EnergyCalibration;
                    }
                }
                else if (config.InputDeviceConfig is ObsidianDeviceConfig)
                {
                    PolynomialEnergyCalibration cal = (PolynomialEnergyCalibration)config.EnergyCalibration;
                    if (cal.PolynomialOrder == 2)
                    {
                        ObsidianDeviceConfig obs_config = (ObsidianDeviceConfig)config.InputDeviceConfig;
                        obs_config.OBS_EnergyCalibration = cal;
                    }
                    else if (this.rc_EnergyCalibration != null)
                    {
                        ObsidianDeviceConfig obs_config = (ObsidianDeviceConfig)config.InputDeviceConfig;
                        obs_config.OBS_EnergyCalibration = this.rc_EnergyCalibration;
                    }
                }
                this.inputDeviceForm.SaveFormContents(config.InputDeviceConfig);
                this.thermometerForm.SaveFormContents(config.ThermometerConfig);
                PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)config.EnergyCalibration;
                if (polynomialEnergyCalibration.PolynomialOrder >= 2)
                {
                    polynomialEnergyCalibration.Coefficients[2] = UserNumber.ParseDouble(this.numericUpDown1.Text);
                }
                if (polynomialEnergyCalibration.PolynomialOrder >= 3)
                {
                    polynomialEnergyCalibration.Coefficients[3] = UserNumber.ParseDouble(this.numericUpDown9.Text);
                }
                if (polynomialEnergyCalibration.PolynomialOrder == 4)
                {
                    polynomialEnergyCalibration.Coefficients[4] = UserNumber.ParseDouble(this.numericUpDown8.Text);
                }
                polynomialEnergyCalibration.Coefficients[1] = UserNumber.ParseDouble(this.numericUpDown2.Text);
                polynomialEnergyCalibration.Coefficients[0] = UserNumber.ParseDouble(this.numericUpDown7.Text);
                // Element writes bypass the property setter - drop the stale
                // EnergyToChannel cache explicitly.
                polynomialEnergyCalibration.InvalidateCache();
                if (config.StabilizerConfig == null)
                {
                    config.StabilizerConfig = new StabilizerConfig();
                }
                config.StabilizerConfig.TargetPeaks = new List<TargetPeak>();
                foreach (Row row in this.tableModel3.Rows)
                {
                    TargetPeak targetPeak = new TargetPeak();
                    targetPeak.Nuclide = row.Cells[0].Text;
                    targetPeak.Energy = (decimal)row.Cells[1].Data;
                    targetPeak.Error = (decimal)row.Cells[2].Data;
                    config.StabilizerConfig.TargetPeaks.Add(targetPeak);
                }
                DoseRateConfig doseRateConfig = config.DoseRateConfig;
                config.DoseRateConfig.DoseRateCalibrationPoints = new List<DoseRateCalibrationPoint>();
                foreach(Row row in this.tableModel4.Rows)
                {
                    DoseRateCalibrationPoint point = new DoseRateCalibrationPoint();
                    point.LowerBound = getDouble(row.Cells[0].Data);
                    point.UpperBound = getDouble(row.Cells[1].Data);
                    point.CPS = getDouble(row.Cells[2].Data);
                    point.EtalonDoseRateValue = getDouble(row.Cells[3].Data);
                    config.DoseRateConfig.DoseRateCalibrationPoints.Add(point);
                }
                FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)config.PeakDetectionMethodConfig;
                FWHMPeakDetectionMethodConfig.Min_SNR = (double)this.numericUpDown4.Value;
                FWHMPeakDetectionMethodConfig.Max_Items = (int)this.numericUpDown3.Value;
                FWHMPeakDetectionMethodConfig.Tolerance = (double)this.numericUpDown6.Value;
                FWHMPeakDetectionMethodConfig.Min_Range = (double)this.numericUpDown12.Value;
                FWHMPeakDetectionMethodConfig.Max_Range = (double)this.numericUpDown13.Value;
                FWHMPeakDetectionMethodConfig.Min_FWHM_Tol = this.numericUpDown14.Value;
                FWHMPeakDetectionMethodConfig.Max_FWHM_Tol = this.numericUpDown15.Value;
                FWHMPeakDetectionMethodConfig.Ch_Concat = (int)this.numericUpDown16.Value;
                FWHMPeakDetectionMethodConfig.PeakWidthWidenFactor = (double)this.numericUpDownWidenFactor.Value;
                FWHMPeakDetectionMethodConfig.UseCenterOfMassCentroid = this.centroidComCheckBox.Checked;
                StoreCurrentPeakShapeParameters();
                FWHMPeakDetectionMethodConfig.FwhmCalibration.PeakType = peakTypecomboBox.SelectedIndex;
                FWHMPeakDetectionMethodConfig.FwhmCalibration.ExpGaussExpLeftTail = (double)expGaussExpLeftValue;
                FWHMPeakDetectionMethodConfig.FwhmCalibration.ExpGaussExpRightTail = (double)expGaussExpRightValue;
                FWHMPeakDetectionMethodConfig.FwhmCalibration.VoigtSigma = (double)voigtSigmaValue;
                FWHMPeakDetectionMethodConfig.FwhmCalibration.VoigtGamma = (double)voigtGammaValue;
                config.BackgroundSpectrumPathname = this.textBox17.Text;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        double getDouble(object Data)
        {
            if (Data.GetType() == typeof(int))
            {
                return (double)(int)Data;
            }
            if (Data.GetType() == typeof(double))
            {
                return (double)Data;
            }
            if(Data.GetType() == typeof(decimal))
            {
                return (double)(decimal)Data;
            }
            return (double)Data;
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
                // Конструкторы кривой привязаны к прежнему клону конфигурации:
                // вместе с ним они и уходят, иначе их «Сохранить» писало бы в
                // объект, который больше ниоткуда не достижим.
                this.CloseEfficiencyMakers();
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
                try
                {
                    this.LoadFormContents(this.activeDeviceConfig);
                } catch (Exception ex)
                {
                    MessageBox.Show($"{Resources.ERRBTNotSupportedByOS} Message: {ex.Message}");
                    this.button4.Enabled = false;
                    this.button12.Enabled = false;
                    this.DisableForm();
                    this.reenter = false;
                    return;
                }
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
        /// <summary>
        /// Вторая дверь к сохранению конфигурации прибора — вопрос «сохранить
        /// изменения?» при закрытии окна, при заведении и копировании
        /// конфигурации и при переходе на другую строку списка.
        ///
        /// ⛔ Прежде она звала <see cref="SaveFormContents"/> и ВЫБРАСЫВАЛА его
        /// ответ (`A6`), тогда как кнопка «Сохранить» тот же ответ читала и
        /// ругалась <c>ERRInvalidInputForm</c>. Одна и та же введённая ерунда по
        /// кнопке отвергалась, а по вопросу проглатывалась — и уезжала на диск.
        ///
        /// ⚠ Почему при отказе окно ОСТАЁТСЯ ОТКРЫТЫМ: человек ответил «да,
        /// сохранить», сохранить нельзя, и превращать его «да» в «нет» молча
        /// нельзя — отказ от правок у него уже есть отдельной кнопкой «Нет».
        /// Обе соседние беды этого же метода (<c>CalibrationFunctionError</c>,
        /// <c>ERRDuplicateConfigName</c>) поступают так же.
        /// </summary>
        bool ConfirmSaveDeviceConfig()
        {
            if (this.activeDeviceConfig != null && this.activeDeviceConfig.Dirty)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGConfirmSaveConfig, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Yes)
                {
                    PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                    if (!pe.CheckCalibration(channels: this.activeDeviceConfig.NumberOfChannels))
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                        return false;
                    }
                    if (!this.SaveFormContents(this.activeDeviceConfig))
                    {
                        MessageBox.Show(Resources.ERRInvalidInputForm);
                        return false;
                    }
                    if (!this.manager.SaveConfig(this.activeDeviceConfig))
                    {
                        MessageBox.Show(Resources.ERRDuplicateConfigName);
                        return false;
                    }
                }
                else
                {
                    // Правки отвергнуты, клон заменяется свежим — открытые на
                    // прежнем клоне конструкторы кривой закрываются вместе с ним.
                    this.CloseEfficiencyMakers();
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

        void setNewCalibration(TextBox t, int order)
        {
            PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
            try
            {
                double result = fromStringtoDouble(t.Text);

                // Nothing changes, leave
                if ((result == 0 && order > pe.PolynomialOrder) ||
                    (pe.PolynomialOrder == order && pe.Coefficients[order] == result))
                {
                    t.ForeColor = Color.Black;
                    return;
                }

                if (pe.Coefficients.Length <= order)
                {
                    PolynomialEnergyCalibration newPe = (PolynomialEnergyCalibration)pe.Clone();
                    newPe.PolynomialOrder = order;
                    double[] coeff = new double[pe.Coefficients.Length + 1];
                    Array.Copy(pe.Coefficients, coeff, pe.Coefficients.Length);
                    newPe.Coefficients = coeff;
                    newPe.Coefficients[order] = result;
                    this.activeDeviceConfig.EnergyCalibration = (PolynomialEnergyCalibration)newPe;
                } else if (t.Text == "0" && order == pe.Coefficients.Length - 1)
                {
                    if (pe.PolynomialOrder > 1)
                    {
                        PolynomialEnergyCalibration newPe = (PolynomialEnergyCalibration)pe.Clone().Downgrade(order - 1);
                        this.activeDeviceConfig.EnergyCalibration = (PolynomialEnergyCalibration)newPe;
                    }
                    else
                    {
                        throw new Exception();
                    }
                } else
                {
                    if (pe.Coefficients[order] == result) return;
                    pe.Coefficients[order] = result;
                    pe.InvalidateCache();
                    this.activeDeviceConfig.EnergyCalibration = (PolynomialEnergyCalibration)pe;
                }
                t.ForeColor = Color.Black;
                this.SetActiveDeviceConfigDirty();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
                t.ForeColor = Color.Red;
            }
        }

        void numericUpDown8_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                setNewCalibration(this.numericUpDown8, 4);
                e.SuppressKeyPress = true;
            }
            else
            {
                this.numericUpDown8.ForeColor = Color.Blue;
            }
        }

        void numericUpDown8_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown8, 4);
        }

        void numericUpDown9_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                setNewCalibration(this.numericUpDown9, 3);
                e.SuppressKeyPress = true;
            }
            else
            {
                this.numericUpDown9.ForeColor = Color.Blue;
            }
        }

        void numericUpDown9_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown9, 3);
        }

        void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                setNewCalibration(this.numericUpDown1, 2);
                e.SuppressKeyPress = true;
            }
            else
            {
                this.numericUpDown1.ForeColor = Color.Blue;
            }
        }

        void numericUpDown1_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown1, 2);
        }

        void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                setNewCalibration(this.numericUpDown2, 1);
                e.SuppressKeyPress = true;
            }
            else
            {
                this.numericUpDown2.ForeColor = Color.Blue;
            }
        }

        void numericUpDown2_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown2, 1);
        }

        void numericUpDown7_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                setNewCalibration(this.numericUpDown7, 0);
                e.SuppressKeyPress = true;
            }
            else
            {
                this.numericUpDown7.ForeColor = Color.Blue;
            }
        }

        void numericUpDown7_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown7, 0);
        }


        void button13_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig.DeviceType == "AtomSpectraVCP")
            {
                AtomSpectraDeviceConfig deviceconfig = (AtomSpectraDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                string guid = this.activeDeviceConfig.Guid;
                AtomSpectraVCPIn device = AtomSpectraVCPIn.tryGetInstance(guid);
                bool createdInstance = device == null;
                if (createdInstance)
                {
                    device = AtomSpectraVCPIn.getInstance(guid);
                    device.setPort(deviceconfig.ComPortName, deviceconfig.BaudRate);
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

                        if (uint.Parse(result_arr[10], System.Globalization.NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) != crc32)
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
                                byte[] floatVals = BitConverter.GetBytes(ulong.Parse(CalibrationCoefficients[i], System.Globalization.NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                                coeff_list.Add(BitConverter.ToDouble(floatVals, 0));
                            }
                        }
                        for (int i = 4; i >= 0; i--)
                        {
                            if (coeff_list[i] == 0)
                            {
                                coeff_list.RemoveAt(i);
                            } else
                            {
                                break;
                            }
                        }
                        if (coeff_list.Count < 2)
                        {
                            MessageBox.Show(Resources.ERREmptyCoefficients);
                            if (createdInstance)
                            {
                                AtomSpectraVCPIn.cleanUp(guid);
                            }
                            return;
                        }
                        this.numericUpDown1.Text = "0";
                        this.numericUpDown2.Text = "0";
                        this.numericUpDown7.Text = "0";
                        this.numericUpDown8.Text = "0";
                        this.numericUpDown9.Text = "0";
                        polynomialEnergyCalibration.PolynomialOrder = coeff_list.Count - 1;
                        polynomialEnergyCalibration.Coefficients = coeff_list.ToArray();
                        if (polynomialEnergyCalibration.PolynomialOrder >= 1)
                        {
                            this.numericUpDown7.Text = polynomialEnergyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);
                            this.numericUpDown2.Text = polynomialEnergyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);
                        }
                        if (polynomialEnergyCalibration.PolynomialOrder >= 2)
                        {
                            this.numericUpDown1.Text = polynomialEnergyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);
                        }
                        if (polynomialEnergyCalibration.PolynomialOrder >= 3)
                        {
                            this.numericUpDown9.Text = polynomialEnergyCalibration.Coefficients[3].ToString(CultureInfo.InvariantCulture);
                        }
                        if (polynomialEnergyCalibration.PolynomialOrder == 4)
                        {
                            this.numericUpDown8.Text = polynomialEnergyCalibration.Coefficients[4].ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    else
                    {
                        MessageBox.Show(String.Format(Resources.ERRReadDataFromPort, deviceconfig.ComPortName));
                    }
                    if (createdInstance)
                    {
                        AtomSpectraVCPIn.cleanUp(guid);
                    }
                    SetActiveDeviceConfigDirty();
                }
                catch
                {
                    MessageBox.Show(Resources.ERRReadDataFromPort_Empty);
                }
            } else if (this.activeDeviceConfig.DeviceType == "RadiaCode")
            {
                RadiaCodeDeviceConfig deviceconfig = (RadiaCodeDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                RadiaCodeIn device = null;
                List<RadiaCodeIn> instances = RadiaCodeIn.getAllInstances();
                bool runexist = false;
                if (instances.Count > 0)
                {
                    foreach (RadiaCodeIn instance in instances)
                    {
                        if (instance.GUID == this.activeDeviceConfig.Guid)
                        {
                            device = instance;
                            runexist = true;
                            break;
                        }
                    }
                }
                if (!runexist)
                {
                    device = new RadiaCodeIn(this.activeDeviceConfig.Guid);
                    device.setDeviceSerial(deviceconfig.DeviceSerial, deviceconfig.AddressBLE);
                    device.sendCommand("Start");
                }

                try
                {
                    PolynomialEnergyCalibration polynomialEnergyCalibration;
                    for (int i = 0; i < 50; i++)
                    {
                        Thread.Sleep(200);
                        polynomialEnergyCalibration = device.GetCalibration();
                        if (polynomialEnergyCalibration != null)
                        {
                            this.activeDeviceConfig.EnergyCalibration = polynomialEnergyCalibration;

                            this.numericUpDown1.Text = "0";
                            this.numericUpDown2.Text = "0";
                            this.numericUpDown7.Text = "0";
                            this.numericUpDown8.Text = "0";
                            this.numericUpDown9.Text = "0";

                            this.numericUpDown7.Text = polynomialEnergyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);
                            this.numericUpDown2.Text = polynomialEnergyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);
                            this.numericUpDown1.Text = polynomialEnergyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);
                            SetActiveDeviceConfigDirty();
                            return;
                        }
                    }
                    MessageBox.Show(String.Format(Resources.ERRReadDataFromPort, deviceconfig.DeviceSerial));
                }
                catch
                {
                    MessageBox.Show(Resources.ERRReadDataFromPort_Empty);
                }
                finally
                {
                    // The locally created RadiaCodeIn bypasses the static instances
                    // registry (finishAll cannot see it), so it MUST be disposed here -
                    // including on exceptions, which used to leak its BLE threads.
                    if (!runexist)
                    {
                        device.Dispose();
                    }
                }
            }
            else if (this.activeDeviceConfig.DeviceType == "Obsidian")
            {
                ObsidianDeviceConfig deviceconfig = (ObsidianDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                try
                {
                    using (ObsidianCalibrationIO device = new ObsidianCalibrationIO())
                    {
                        if (!device.Connect(deviceconfig.AddressBLE))
                        {
                            MessageBox.Show(String.Format(Resources.ERRReadDataFromPort, deviceconfig.DeviceSerial));
                            return;
                        }

                        PolynomialEnergyCalibration polynomialEnergyCalibration = device.ReadCalibration();
                        if (polynomialEnergyCalibration != null)
                        {
                            this.activeDeviceConfig.EnergyCalibration = polynomialEnergyCalibration;
                            deviceconfig.OBS_EnergyCalibration = (PolynomialEnergyCalibration)polynomialEnergyCalibration.Clone();

                            this.numericUpDown1.Text = "0";
                            this.numericUpDown2.Text = "0";
                            this.numericUpDown7.Text = "0";
                            this.numericUpDown8.Text = "0";
                            this.numericUpDown9.Text = "0";

                            this.numericUpDown7.Text = polynomialEnergyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);
                            this.numericUpDown2.Text = polynomialEnergyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);
                            this.numericUpDown1.Text = polynomialEnergyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);
                            this.textBox16.Text = deviceconfig.OBS_EnergyCalibration.ToString();
                            this.button14.Enabled = true;
                            SetActiveDeviceConfigDirty();
                            return;
                        }
                    }

                    MessageBox.Show(String.Format(Resources.ERRReadDataFromPort, deviceconfig.DeviceSerial));
                }
                catch
                {
                    MessageBox.Show(Resources.ERRReadDataFromPort_Empty);
                }
            }
        }

        void button14_Click(object sender, EventArgs e)
        {
            this.button14.Enabled = false;

            // Capture UI state on the UI thread: DoWork used to read this.button6.Enabled
            // from the worker thread (illegal cross-thread control access).
            bool configNotSaved = this.button6.Enabled;

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += new DoWorkEventHandler(delegate (object o, DoWorkEventArgs args)
            {
                BackgroundWorker b = o as BackgroundWorker;


                if (this.activeDeviceConfig.DeviceType == "AtomSpectraVCP")
                {
                    if (configNotSaved)
                    {
                        ShowOwnedMessageBox(Resources.MSGSaveBeforeWritingData);
                        return;
                    }
                    try
                    {
                        Cursor.Current = Cursors.WaitCursor;
                        b.ReportProgress(0);
                        PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)this.activeDeviceConfig.EnergyCalibration;
                        List<string> result_list = new List<string>();
                        for (int i = 0; i < polynomialEnergyCalibration.Coefficients.Length; i++)
                        {
                            string result_str = BitConverter.DoubleToInt64Bits(polynomialEnergyCalibration.Coefficients[i]).ToString("X", CultureInfo.InvariantCulture);
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

                        result_list.Add(crc32.ToString("X", CultureInfo.InvariantCulture));

                        bool commands_accepted = true;
                        System.Diagnostics.Trace.WriteLine("commands_accepted = " + commands_accepted);
                        AtomSpectraDeviceConfig deviceconfig = (AtomSpectraDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                        string guid = this.activeDeviceConfig.Guid;
                        AtomSpectraVCPIn device = AtomSpectraVCPIn.tryGetInstance(guid);
                        bool createdInstance = device == null;
                        if (createdInstance)
                        {
                            device = AtomSpectraVCPIn.getInstance(guid);
                            device.setPort(deviceconfig.ComPortName, deviceconfig.BaudRate);
                        }
                        string status_msg = "";
                        for (int i = 0; i < result_list.Count; i++)
                        {
                            int percent = (int)(100*i)/(result_list.Count - 1);
                            b.ReportProgress(percent);
                            device.sendCommand("-cal " + i.ToString(CultureInfo.InvariantCulture) + " " + result_list[i]);
                            bool result = device.waitForAnswer("ok", 2000);
                            commands_accepted &= result;
                            System.Diagnostics.Trace.WriteLine("result = " + result);
                            status_msg = status_msg + "-cal " + i.ToString(CultureInfo.InvariantCulture) + " " + result_list[i] + " -- result: " + result + Environment.NewLine;
                        }
                        Cursor.Current = Cursors.Default;
                        if (commands_accepted)
                        {
                            ShowOwnedMessageBox(Resources.MSGCoefficientsUploadedSuccessful);
                        }
                        else
                        {
                            //Workaround with some command -cal not responded
                            device.sendCommand("-cal");
                            String result = device.getCommandOutput(2000);
                            string[] separator = new string[] { "\r\n" };
                            string[] result_arr = result.Split(separator, StringSplitOptions.None);
                            for (int i = 0; i < result_list.Count; i++)
                            {
                                if (result_list[i] != result_arr[i])
                                {
                                    ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + status_msg);
                                    if (createdInstance)
                                    {
                                        AtomSpectraVCPIn.cleanUp(guid);
                                    }
                                    return;
                                }
                            }
                            ShowOwnedMessageBox(Resources.MSGCoefficientsUploadedSuccessful);
                        }
                        if (createdInstance)
                        {
                            AtomSpectraVCPIn.cleanUp(guid);
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + ex.Message);
                    }
                } else if (this.activeDeviceConfig.DeviceType == "RadiaCode")
                {
                    if (configNotSaved)
                    {
                        ShowOwnedMessageBox(Resources.MSGSaveBeforeWritingData);
                        return;
                    }
                    try
                    {
                        Cursor.Current = Cursors.WaitCursor;
                        b.ReportProgress(0);
                        RadiaCodeDeviceConfig rc_config = (RadiaCodeDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                        PolynomialEnergyCalibration polynomialEnergyCalibration = rc_config.RC_EnergyCalibration;
                        if (polynomialEnergyCalibration == null)
                        {
                            ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + "Empty calibration");
                            return;
                        }

                        bool commands_accepted = false;
                        RadiaCodeIn device = null;
                        List<RadiaCodeIn> instances = RadiaCodeIn.getAllInstances();
                        bool runexist = false;
                        if (instances.Count > 0)
                        {
                            foreach (RadiaCodeIn instance in instances)
                            {
                                if (instance.GUID == this.activeDeviceConfig.Guid)
                                {
                                    device = instance;
                                    runexist = true;
                                    break;
                                }
                            }
                        }
                        if (!runexist)
                        {
                            device = new RadiaCodeIn(this.activeDeviceConfig.Guid);
                            device.setDeviceSerial(rc_config.DeviceSerial, rc_config.AddressBLE);
                        }
                        try
                        {
                            device.setCalibration(polynomialEnergyCalibration);

                            device.sendCommand("Calibration");

                            for (int i = 0; i < 100; i++)
                            {
                                Thread.Sleep(100);
                                if (device.getStateString() == "Calibration done")
                                {
                                    commands_accepted = true;
                                    b.ReportProgress(100);
                                    break;
                                } else if (device.getStateString() == "Calibration fail")
                                {
                                    commands_accepted = false;
                                    b.ReportProgress(100);
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            // The locally created RadiaCodeIn bypasses the instances
                            // registry, so dispose it even on exceptions (it used to
                            // leak its BLE threads).
                            if (!runexist)
                            {
                                device.Dispose();
                            } else
                            {
                                device.sendCommand("Continue");
                            }
                        }
                        Cursor.Current = Cursors.Default;
                        if (commands_accepted)
                        {
                            ShowOwnedMessageBox(Resources.MSGCoefficientsUploadedSuccessful);
                        }
                        else
                        {
                            // ⛔ `A15`. Здесь стояла склейка с `status_msg`,
                            //    которая в ЭТОЙ ветви заводилась пустой и не
                            //    заполнялась ничем: человек получал «Ошибка
                            //    записи коэффициентов» и пустую строку под ней.
                            //    Причину знает сам прибор — разбор у
                            //    `RadiaCodeIn.CalibrationFailureText`.
                            ShowOwnedMessageBox(RadiaCodeIn.CalibrationFailureText(device));
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + ex.Message);
                    }
                }
                else if (this.activeDeviceConfig.DeviceType == "Obsidian")
                {
                    if (configNotSaved)
                    {
                        ShowOwnedMessageBox(Resources.MSGSaveBeforeWritingData);
                        return;
                    }
                    try
                    {
                        Cursor.Current = Cursors.WaitCursor;
                        b.ReportProgress(0);
                        ObsidianDeviceConfig obs_config = (ObsidianDeviceConfig)this.activeDeviceConfig.InputDeviceConfig;
                        PolynomialEnergyCalibration polynomialEnergyCalibration = obs_config.OBS_EnergyCalibration;
                        if (polynomialEnergyCalibration == null)
                        {
                            ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + "Empty calibration");
                            return;
                        }

                        bool commands_accepted;
                        // ⛔ `A20`. Причина отказа теперь СОХРАНЯЕТСЯ прибором
                        // и читается здесь: прежде окно показывало одно
                        // «не удалось записать» без единого слова о том,
                        // что случилось.
                        string obsFailure = "";
                        using (ObsidianCalibrationIO device = new ObsidianCalibrationIO())
                        {
                            commands_accepted = device.Connect(obs_config.AddressBLE) && device.WriteCalibration(polynomialEnergyCalibration);
                            obsFailure = device.LastFailure;
                        }

                        b.ReportProgress(100);
                        Cursor.Current = Cursors.Default;
                        if (commands_accepted)
                        {
                            ShowOwnedMessageBox(Resources.MSGCoefficientsUploadedSuccessful);
                        }
                        else
                        {
                            ShowOwnedMessageBox(string.IsNullOrEmpty(obsFailure)
                                ? Resources.ERRUploadCoefficientsToDevice
                                : Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + obsFailure);
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + ex.Message);
                    }
                }


            });

            MainForm mainForm = (MainForm)base.Owner;

            worker.ProgressChanged += new ProgressChangedEventHandler(delegate (object o, ProgressChangedEventArgs args)
            {
                if (mainForm != null)
                {
                    mainForm.SetStatusTextLeft(string.Format(CultureInfo.InvariantCulture, Resources.WriteCalibrationToAtomProProgress, args.ProgressPercentage));
                }
            });

            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(delegate (object o, RunWorkerCompletedEventArgs args)
            {
                mainForm.ClearStatusTextLeft();
                this.button14.Enabled = true;
                // Errors from DoWork used to be silently swallowed.
                if (args.Error != null)
                {
                    ShowOwnedMessageBox(Resources.ERRUploadCoefficientsToDevice + Environment.NewLine + args.Error.Message);
                }
            });

            worker.RunWorkerAsync();
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
                // Remember the subscribed view - see ClearChannelPickupState.
                this.pickupSubscribedView = activeDocument.EnergySpectrumView;
                this.pickupSubscribedView.ChannelPickuped += this.energySpectrumView_ChannelPickuped;
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
            CalibrationPoint item = new CalibrationPoint(e.Channel, energy, e.Count);
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
            // Unsubscribe from the view we actually subscribed to, not from whatever
            // document happens to be active now.
            if (this.pickupSubscribedView != null)
            {
                this.pickupSubscribedView.ChannelPickuped -= this.energySpectrumView_ChannelPickuped;
                this.pickupSubscribedView = null;
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
                row.Cells.Add(new Cell(num.ToString(CultureInfo.InvariantCulture)));
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
                    this.calibrationPoints[row.Index].Channel = (int)UserNumber.ParseDecimal(text);
                    this.multipointModified = true;
                    this.calibrationDone = false;
                    this.UpdateMultipointButtonState();
                }
                else if (e.Column == 2)
                {
                    string text2 = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Energy = UserNumber.ParseDecimal(text2);
                    this.multipointModified = true;
                    this.UpdateMultipointButtonState();
                }
                else if (e.Column == 4)
                {
                    string text2 = ((NumberCellEditor)e.Editor).TextBox.Text;
                    this.calibrationPoints[row.Index].Energy = UserNumber.ParseDecimal(text2);
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
            PolynomialEnergyCalibration energyCalibration = new PolynomialEnergyCalibration();
            for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
            {
                energyCalibration.Coefficients[i] = 0.0;
            }
            double[] matrix;
            List<CalibrationPoint> points = this.calibrationPoints;
            if (points.Count == 1)
            {
                CalibrationPoint zero = new CalibrationPoint(0, 0, 0);
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
                // ⛔ `A245`, полоса F44 05.09.2026. Голое модальное окно на
                //    БЕЗОКОННОМ пути. Окно тут было всегда, а достижимым из
                //    безоконного прогона место стало 05.09.2026, когда проба
                //    `RestCultureProbeF28` завела литерал `"button1_Click"` и
                //    сторож `check_headless.py` признал метод достижимым по
                //    правилу `REFLECT_OVERRIDE`. Нажать «ОК» здесь некому:
                //    проба, дойдя сюда, виснет насмерть до убийства процесса.
                //    Дверь маршалит показ на поток окон сама (~~`A241`~~), а
                //    на потоке окон вызов остаётся синхронным — в приложении
                //    вид сообщения ПРЕЖНИЙ: `MessageBox.Show(text)` это тот же
                //    пустой заголовок, та же единственная «ОК» и тот же
                //    отсутствующий знак (`MessageBoxIcon.None`).
                AppUi.Report(Resources.ERRInvalidChannelOrEnergyValues, "", MessageBoxIcon.None);
                return;
            }

            energyCalibration.Coefficients = new double[matrix.Length];
            energyCalibration.PolynomialOrder = matrix.Length - 1;
            energyCalibration.Coefficients = matrix;

            if (!energyCalibration.CheckCalibration())
            {
                // ⛔ `A245`, полоса F44 05.09.2026 — то же, что соседом выше:
                //    голое модальное окно на безоконном пути. Тот же ресурс
                //    уже ходит через дверь с пустым заголовком в
                //    `DocumentManager.cs:1171` и `N42/Util.cs:510` — вид
                //    сообщения в приложении не меняется.
                AppUi.Report(Resources.CalibrationFunctionError, "", MessageBoxIcon.None);
                return;
            }
            this.numericUpDown1.Text = "0";
            this.numericUpDown2.Text = "0";
            this.numericUpDown7.Text = "0";
            this.numericUpDown8.Text = "0";
            this.numericUpDown9.Text = "0";
            if (energyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = energyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);
            }
            if (energyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown9.Text = energyCalibration.Coefficients[3].ToString(CultureInfo.InvariantCulture);
            }
            if (energyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown8.Text = energyCalibration.Coefficients[4].ToString(CultureInfo.InvariantCulture);
            }
            this.numericUpDown2.Text = energyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);
            this.numericUpDown7.Text = energyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);
            if (!energyCalibration.CheckCalibration())
            {
                // ⛔ `A245`, полоса F44 05.09.2026. Третье голое окно того же
                //    обработчика: проверка повторяется после того, как
                //    коэффициенты прошли через поля ввода, и отказать может
                //    именно она. Чинится вместе с двумя соседями — оставить
                //    одно из трёх значило бы оставить безоконный путь висящим.
                AppUi.Report(Resources.CalibrationFunctionError, "", MessageBoxIcon.None);
                return;
            }
            if (activeDeviceConfig.InputDeviceConfig is RadiaCodeDeviceConfig && energyCalibration.PolynomialOrder >= 2)
            {
                matrix = Utils.CalibrationSolver.Solve(points, 2);
                if (matrix == null) throw new Exception("Error");
                rc_EnergyCalibration = new PolynomialEnergyCalibration();
                rc_EnergyCalibration.Coefficients = matrix;
                rc_EnergyCalibration.PolynomialOrder = 2;
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
            this.tabControl1.SelectedTab = this.tabPage3;
            TextBox lowerThresholdTextBox = this.inputDeviceForm.LowerThresholdTextBox;
            if (lowerThresholdTextBox != null)
            {
                lowerThresholdTextBox.Text = threshold.ToString(CultureInfo.InvariantCulture);
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
            this.tabControl1.SelectedTab = this.tabPage3;
            TextBox upperThresholdTextBox = this.inputDeviceForm.UpperThresholdTextBox;
            if (upperThresholdTextBox != null)
            {
                upperThresholdTextBox.Text = threshold.ToString(CultureInfo.InvariantCulture);
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
            this.tabControl1.SelectedTab = this.tabPage2;
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

        // Token: 0x06000544 RID: 1348 RVA: 0x00022604 File Offset: 0x00020804
        void numericUpDown6_ValueChanged(object sender, EventArgs e)
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

        void numericUpDown16_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void numericUpDownWidenFactor_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDeviceConfigDirty();
        }

        void centroidComCheckBox_CheckedChanged(object sender, EventArgs e)
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
            try
            {
                this.PrepareDeviceForm(deviceType);
            } catch (Exception)
            {
                MessageBox.Show(Resources.ERRBTNotSupportedByOS);
                this.DisableForm();
            }
            
            InputDeviceConfig inputDeviceConfig = (InputDeviceConfig)Activator.CreateInstance(deviceType.DeviceConfigType);
            this.activeDeviceConfig.InputDeviceConfig = inputDeviceConfig;
            try
            {
                this.inputDeviceForm.LoadFormContents(inputDeviceConfig);
            }
            catch (Exception)
            {
                MessageBox.Show(Resources.ERRBTNotSupportedByOS);
                this.DisableForm();
            }

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

        double fromStringtoDouble(string str)
        {
            double result;
            if (double.TryParse(str.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out result))
            {
                if (result > -200.0 && result < 200.0)
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

        void ShowOwnedMessageBox(string message)
        {
            if (this.IsDisposed)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    ShowOwnedMessageBox(message);
                });
                return;
            }
            MessageBox.Show(this, message);
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

        void table4_EditingStopped(object sender, CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;
            try
            {
                string text = ((NumberCellEditor)e.Editor).TextBox.Text;
                this.SetActiveDeviceConfigDirty();
            }
            catch (Exception)
            {
                e.Cancel = true;
            }
        }

        void button15_Click(object sender, EventArgs e)
        {
            Row row1 = new Row();
            if (this.table4.RowCount == 0)
            {
                row1.Cells.Add(new Cell(0));
                row1.Cells.Add(new Cell(3000));
                row1.Cells.Add(new Cell(1));
                row1.Cells.Add(new Cell(0.001));
            } else
            {
                row1.Cells.Add(this.tableModel4[this.tableModel4.Rows.Count - 1,1]);
                row1.Cells.Add(new Cell(3000));
                row1.Cells.Add(new Cell(1));
                row1.Cells.Add(new Cell(0.001));
            }
            this.tableModel4.Rows.Add(row1);
            this.SetActiveDeviceConfigDirty();
            this.EvaluateButtonEstimateDRState();
        }

        void button16_Click(object sender, EventArgs e)
        {
            if (this.table4.SelectedItems.Length <= 0)
            {
                return;
            }
            Row row = this.table4.SelectedItems[0];
            this.tableModel4.Rows.RemoveAt(row.Index);
            this.SetActiveDeviceConfigDirty();
            this.EvaluateButtonEstimateDRState();
        }

        // Token: 0x040002BB RID: 699
        DeviceConfigManager manager = DeviceConfigManager.GetInstance();

        // Token: 0x040002BC RID: 700
        DeviceConfigInfo activeDeviceConfig;

        // Token: 0x040002BD RID: 701
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x040002BE RID: 702
        bool contentsLoading;

        decimal expGaussExpLeftValue = 1.0m;

        decimal expGaussExpRightValue = 1.0m;

        decimal voigtSigmaValue = 1.0m;

        decimal voigtGammaValue = 1.0m;

        string expGaussExpLeftLabelText;

        string expGaussExpRightLabelText;

        // Token: 0x040002BF RID: 703
        bool channelPickupProcessing;

        EnergySpectrumView pickupSubscribedView;

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

        PolynomialEnergyCalibration rc_EnergyCalibration;


        private EnergySpectrum doseRateSpectrum;
        private DoseRateCurve efficiencyCurve;

        // --- `C4(а)` и `C4(б)`: то, что уже есть у приложения, вместо диалога ---
        //
        // ⚠ Оба списка — `comboDoseRateSpectrum` и `comboDoseRateEfficiency` —
        // живут в `DeviceConfigForm.Designer.cs` и получают место, размер и
        // порядок обхода из `DeviceConfigForm.resx` (`A202`, 05.09.2026). До
        // того они строились здесь, кодом, и брали раскладку у двух ПОЛЕЙ
        // «путь к файлу», которые остались в конструкторе форм и прятались
        // `Visible = false`: раскладка жила в двух местах разом, а человек за
        // конструктором видел поля, которых на вкладке нет.
        ToolTip doseRateToolTip;

        /// <summary>Спектр, поднятый из файла старым путём; null, если его не было.</summary>
        DoseRateSpectrumChoice doseRateFileChoice;

        /// <summary>Кривая, поднятая из файла ЛСРМ старым путём.</summary>
        List<ROIEfficiencyData> doseRateFileCurve;
        string doseRateFileCurveName;


        /// <summary>
        /// Достроить вкладку «Dose Rate» тем, чего конструктор форм не хранит.
        ///
        /// Сами списки — спектра и кривой — стоят в конструкторе форм рядом со
        /// своими кнопками «… из файла»: выбранный пункт и есть имя источника,
        /// а полный путь к файлу, поднятому кнопкой, висит подсказкой. Подсказка
        /// раскладкой не является и в `*.resx` не хранится — компонент
        /// заводится здесь (`A202`, 05.09.2026).
        /// </summary>
        void BuildDoseRateTab()
        {
            this.doseRateToolTip = new ToolTip();
        }

        /// <summary>
        /// Наполнить оба списка. Зовётся при загрузке конфигурации: набор
        /// кривых у каждой конфигурации свой.
        /// </summary>
        void LoadDoseRateTab(DeviceConfigInfo config)
        {
            if (this.comboDoseRateEfficiency == null)
            {
                return;
            }

            this.FillDoseRateEfficiencyCombo(config);
            this.FillDoseRateSpectrumCombo();
        }

        void FillDoseRateEfficiencyCombo(DeviceConfigInfo config)
        {
            this.comboDoseRateEfficiency.Items.Clear();

            // Кривые САМОЙ конфигурации прибора — то, чего вкладка не видела
            // вовсе (`C4(а)`).
            foreach (EfficiencyConfigData item in DoseRateEstimator.OfferedEfficiencies(config))
            {
                this.comboDoseRateEfficiency.Items.Add(item);
            }

            // Файл ЛСРМ остаётся: старый путь цел, он просто перестал быть
            // единственным.
            if (this.doseRateFileCurve != null)
            {
                this.comboDoseRateEfficiency.Items.Add(this.doseRateFileCurveName);
            }

            if (this.comboDoseRateEfficiency.Items.Count > 0)
            {
                this.comboDoseRateEfficiency.SelectedIndex = this.comboDoseRateEfficiency.Items.Count - 1;
            }
            else
            {
                this.efficiencyCurve = null;
                this.EvaluateButtonEstimateDRState();
            }
        }

        void FillDoseRateSpectrumCombo()
        {
            this.comboDoseRateSpectrum.Items.Clear();

            // Уже открытые спектры (`C4(б)`).
            foreach (DoseRateSpectrumChoice choice in this.OpenSpectrumChoices())
            {
                this.comboDoseRateSpectrum.Items.Add(choice);
            }

            if (this.doseRateFileChoice != null)
            {
                this.comboDoseRateSpectrum.Items.Add(this.doseRateFileChoice);
            }

            if (this.comboDoseRateSpectrum.Items.Count > 0)
            {
                this.comboDoseRateSpectrum.SelectedIndex = this.comboDoseRateSpectrum.Items.Count - 1;
            }
            else
            {
                this.doseRateSpectrum = null;
                this.EvaluateButtonEstimateDRState();
            }
        }

        /// <summary>Открытые документы, приведённые к выбору вкладки.</summary>
        List<DoseRateSpectrumChoice> OpenSpectrumChoices()
        {
            var titles = new List<string>();
            var results = new List<ResultData>();
            try
            {
                foreach (DocEnergySpectrum document in DocumentManager.GetInstance().DocumentList)
                {
                    if (document == null || document.ActiveResultData == null)
                    {
                        continue;
                    }

                    titles.Add(string.IsNullOrEmpty(document.Filename)
                        ? document.Text
                        : Path.GetFileNameWithoutExtension(document.Filename));
                    results.Add(document.ActiveResultData);
                }
            }
            catch (Exception ex)
            {
                // Список документов — удобство, а не условие работы: без него
                // остаётся старый путь через файл.
                Trace.WriteLine("Dose rate: список открытых документов недоступен: " + ex.Message);
            }

            return DoseRateEstimator.OfferedSpectra(titles, results);
        }

        void comboDoseRateSpectrum_SelectedIndexChanged(object sender, EventArgs e)
        {
            DoseRateSpectrumChoice choice = this.comboDoseRateSpectrum.SelectedItem as DoseRateSpectrumChoice;
            this.doseRateSpectrum = choice == null ? null : choice.Spectrum;
            this.EvaluateButtonEstimateDRState();
        }

        void comboDoseRateEfficiency_SelectedIndexChanged(object sender, EventArgs e)
        {
            object selected = this.comboDoseRateEfficiency.SelectedItem;
            try
            {
                EfficiencyConfigData data = selected as EfficiencyConfigData;
                List<ROIEfficiencyData> points = null;
                if (data != null)
                {
                    points = data.Curve;
                }
                else if (this.doseRateFileCurve != null)
                {
                    points = this.doseRateFileCurve;
                }

                this.efficiencyCurve = points == null ? null : DoseRateEstimator.CurveOf(points);
            }
            catch (DoseRateRefusalException ex)
            {
                this.efficiencyCurve = null;

                // ⚠ Во время загрузки конфигурации окно НЕ показывается: список
                // наполняется сам, человек ничего не выбирал, и негодная кривая
                // из хранилища встретила бы его модальным окном на открытии
                // формы. Причина при этом не теряется — она уходит в журнал, а
                // кнопка «Оценить» остаётся выключенной.
                if (this.contentsLoading)
                {
                    Trace.WriteLine("Dose rate: " + ex.Message);
                }
                else
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            this.EvaluateButtonEstimateDRState();
        }

        private void buttonLoadDoseRateSpectrum_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.OpenFileDialogTitle;
            openFileDialog.Filter = Resources.SpectrumFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            try
            {
                using (FileStream fileStream = new FileStream(openFileDialog.FileName, FileMode.Open))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));
                    ResultDataFile result = (ResultDataFile)xmlSerializer.Deserialize(fileStream);

                    // Проверка входа, которой на этом месте не было (собственное
                    // `TODO: add input data validation`): пустой список или
                    // спектр без калибровки прежде уезжали дальше молча и
                    // всплывали `NullReferenceException` в расчёте.
                    var titles = new List<string> { Path.GetFileNameWithoutExtension(openFileDialog.FileName) };
                    var results = new List<ResultData>();
                    if (result != null && result.ResultDataList != null && result.ResultDataList.Count > 0)
                    {
                        results.Add(result.ResultDataList[0]);
                    }

                    List<DoseRateSpectrumChoice> choices = DoseRateEstimator.OfferedSpectra(titles, results);
                    if (choices.Count == 0)
                    {
                        MessageBox.Show(this, string.Format(
                            CultureInfo.CurrentCulture,
                            DoseRateCoefficients.Text("DoseRateFileUnusable",
                                "Dose rate: {0} has no spectrum with an energy calibration, channels and a non-zero measurement time."),
                            openFileDialog.FileName), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    this.doseRateFileChoice = choices[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Resources.ERRFileOpenFailure, openFileDialog.FileName, ex.Message),
                                this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.doseRateToolTip.SetToolTip(this.comboDoseRateSpectrum, openFileDialog.FileName);
            this.FillDoseRateSpectrumCombo();
            EvaluateButtonEstimateDRState();
        }

        /// <summary>
        /// Общий разбор текстового экспорта ЛСРМ (собственное `TODO: create
        /// shared method for LSRM file read`). Статический и без формы нарочно:
        /// так его можно проверить пробой, не поднимая окна.
        ///
        /// Что проверяется, чего раньше не проверялось вовсе: файл читается, в
        /// нём есть строки, числа разбираются, и точек набралось хотя бы две.
        /// Прежде разбор молча глотал исключение, отдавал пустой список и
        /// строил по нему сплайн.
        /// </summary>
        /// <summary>
        /// Число из файла ЛСРМ. Пробуются ОБА разделителя дробной части.
        ///
        /// ⛔ Найдено 05.09.2026 (`C4(а)`). Прежде здесь стоял
        /// `Convert.ToDouble(string)` — он разбирает по ТЕКУЩЕЙ культуре, а
        /// файл приходит с той, в которой его записали. На русской системе
        /// «0.0386379» ловит `FormatException`, старый разбор его глотал,
        /// отдавал пустой список и строил по нему сплайн; на английской то же
        /// самое случалось с «0,0386379». Читатель файла обязан быть безразличен
        /// к культуре машины, на которой файл открывают.
        /// </summary>
        static double ParseLsrmDouble(string text)
        {
            text = text == null ? "" : text.Trim();
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            // Последняя попытка: запятая как дробная часть на любой системе.
            if (text.IndexOf(',') >= 0
                && double.TryParse(text.Replace(',', '.'), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            throw new FormatException(string.Format(CultureInfo.CurrentCulture,
                "«{0}» — не число ни с точкой, ни с запятой", text));
        }

        /// <summary>
        /// ⛔ ПРАВИЛО «ТОЧКЕ С ПОГРЕШНОСТЬЮ ВЫШЕ 100 % НЕ ВЕРИТЬ» — решение
        /// Amber 05.09.2026 (`T174`). Экспорт ЛСРМ несёт третьей колонкой
        /// ЗАЯВЛЕННУЮ САМИМ ЛСРМ погрешность точки в процентах, и первая точка
        /// каждого из восьми настоящих экспортов в дереве заявлена с
        /// погрешностью от 554 до 3830 % — то есть значение известно хуже, чем
        /// «неизвестно вовсе». Такая точка не мнение о кривой, а шум, и в
        /// кривую она не берётся. Правило одинаково для всех файлов, порога «по
        /// вкусу» здесь нет.
        ///
        /// ⚠ Что этим действительно выбрасывается (замер 05.09.2026 по восьми
        /// файлам): РОВНО ПО ОДНОЙ точке из каждого — первая, 20.0 кэВ (у
        /// `Nano 16 - marinelli` — 10.0 кэВ). Ни в одном файле второй такой
        /// точки нет: следующая по счёту (40 кэВ) заявлена с 30.5…65.9 %. Заодно
        /// это снимает единственное физически невозможное значение во всём
        /// наборе — `Obsidian - marinelli 0.5`, 20 кэВ, эффективность
        /// 1.47185E+03, то есть 147 тысяч процентов.
        /// </summary>
        internal const double LsrmMaxErrorPercent = 100.0;

        /// <summary>
        /// ⛔ Сколько точек обязано ОСТАТЬСЯ после отсечения (моё решение,
        /// названо вслух — решением Amber этот случай не покрыт). Двух хватает
        /// ровно потому, что двух требует потребитель: <c>DoseRateCurve</c>
        /// строится сплайном по строго растущим узлам, и на одном узле сплайна
        /// нет. Меньше двух — ОТКАЗ, и отказ называет ОБА числа: сколько точек
        /// файл нёс и сколько отсечено, — иначе «в файле мало точек» неотличимо
        /// от «правило съело файл».
        ///
        /// ⚠ На восьми настоящих экспортах этот случай не наступает ни разу:
        /// после отсечения остаётся 149…150 точек (у `Nano 16 - marinelli` —
        /// 59). Ветка проверена подставным файлом в `DoseRateProbe`.
        /// </summary>
        internal const int LsrmMinPoints = 2;

        internal static List<ROIEfficiencyData> ReadLsrmEfficiencyExport(string path, out string problem)
        {
            problem = null;
            var points = new List<ROIEfficiencyData>();
            int dropped = 0;
            int lineNumber = 0;
            try
            {
                using (StreamReader streamReader = new StreamReader(path, Encoding.GetEncoding(65001)))
                {
                    // ⛔ Шапка ПРОВЕРЯЕТСЯ, а не проглатывается (`T174`). Прежде
                    // первая строка отбрасывалась безусловно: файл без шапки
                    // молча терял первую точку, а файл с переставленными
                    // колонками читался как ЛСРМ-овский и давал числа не о том.
                    // Шапка — единственное, что закрепляет порядок колонок
                    // (энергия, эффективность, погрешность в процентах), и без
                    // неё разбор был бы догадкой.
                    string header = streamReader.ReadLine();
                    lineNumber = 1;
                    string headerText = (header ?? "").ToLowerInvariant();
                    if (headerText.IndexOf("energy", StringComparison.Ordinal) < 0
                        || headerText.IndexOf("efficiency", StringComparison.Ordinal) < 0
                        || headerText.IndexOf("uncertainty", StringComparison.Ordinal) < 0)
                    {
                        problem = string.Format(CultureInfo.CurrentCulture,
                            DoseRateCoefficients.Text("DoseRateLsrmNoHeader",
                                "Dose rate: {0} does not start with the LSRM header"
                                + " \"Energy, keV / Efficiency / Uncertainty, %\" — the first line reads \"{1}\"."),
                            path, header ?? "");
                        return new List<ROIEfficiencyData>();
                    }

                    while (streamReader.Peek() != -1)
                    {
                        lineNumber++;
                        string line = streamReader.ReadLine();
                        if (string.IsNullOrEmpty(line) || line.Trim().Length == 0)
                        {
                            continue;
                        }

                        // ⛔ Пустые поля выбрасываются, КРАТНОСТЬ табуляций
                        // ничего не значит. Настоящий экспорт разделяет колонки
                        // двумя-тремя табуляциями подряд (шесть полей на строку),
                        // и прежний разбор именно на этом и держался: строка
                        // короче шести полей ПРОПУСКАЛАСЬ МОЛЧА. Тот же файл с
                        // одиночными табуляциями — а это ровно то, что делает с
                        // ним любой текстовый редактор, — читался как пустой.
                        List<string> cells = line.Split('\t')
                                                 .Select(c => c.Trim())
                                                 .Where(c => c.Length > 0)
                                                 .ToList();
                        if (cells.Count < 3)
                        {
                            // ⛔ ОТКАЗ, а не `continue`. Строка, не давшая точки,
                            // — это потерянная точка; молча прочитанная половина
                            // файла хуже непрочитанного файла, потому что по ней
                            // строится кривая.
                            problem = string.Format(CultureInfo.InvariantCulture,
                                DoseRateCoefficients.Text("DoseRateLsrmShortLine",
                                    "Dose rate: {0} (line {1}) has {2} column(s) instead of three"
                                    + " (energy, efficiency, uncertainty): \"{3}\"."),
                                path, lineNumber, cells.Count, line);
                            return new List<ROIEfficiencyData>();
                        }

                        double error = ParseLsrmDouble(cells[2]);
                        if (!(error <= LsrmMaxErrorPercent))
                        {
                            // Точка, которой сам ЛСРМ не верит. Отбрасывается
                            // ЧИСЛОМ, а не молча: счётчик уходит в отказ ниже.
                            dropped++;
                            continue;
                        }

                        points.Add(new ROIEfficiencyData()
                        {
                            Energy = ParseLsrmDouble(cells[0]),
                            Efficiency = ParseLsrmDouble(cells[1]),
                            ErrorPercent = error
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                problem = string.Format(CultureInfo.InvariantCulture, "{0} (line {1}): {2}",
                                        path, lineNumber, ex.Message);
                return new List<ROIEfficiencyData>();
            }

            if (points.Count < LsrmMinPoints)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    DoseRateCoefficients.Text("DoseRateLsrmNoPoints",
                        "Dose rate: {0} yielded {1} curve point(s) — at least {2} are needed"
                        + " ({3} more were dropped as declared to more than {4:f0} % uncertainty)."),
                    path, points.Count, LsrmMinPoints, dropped, LsrmMaxErrorPercent);
                return new List<ROIEfficiencyData>();
            }

            return points;
        }

        private void buttonLoadEff_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.EffCalcMCImportDialogTitle;
            openFileDialog.Filter = Resources.EffCalcMCFileFilter;
            openFileDialog.FilterIndex = 2;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            string problem;
            List<ROIEfficiencyData> points = ReadLsrmEfficiencyExport(openFileDialog.FileName, out problem);
            if (problem != null)
            {
                MessageBox.Show(this, string.Format(Resources.ERRFileOpenFailure, openFileDialog.FileName, problem),
                                this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.doseRateFileCurve = points;
            this.doseRateFileCurveName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
            this.doseRateToolTip.SetToolTip(this.comboDoseRateEfficiency, openFileDialog.FileName);
            this.FillDoseRateEfficiencyCombo(this.activeDeviceConfig);

            EvaluateButtonEstimateDRState();
        }

        private void buttonEstimateDRConf_Click(object sender, EventArgs e)
        {
            List<DoseRateCalibrationPoint> doseConfig;
            try
            {
                double expectedDoseRate = (double)this.upDownDoseRateValue.Value;

                // Сетка от ШКАЛЫ ПРИБОРА, а не от двух вшитых чисел (`C4(в)`).
                double minKev, maxKev;
                DoseRateEstimator.DeviceRange(this.activeDeviceConfig, this.doseRateSpectrum,
                                              out minKev, out maxKev);

                // ...и по протяжённости кривой: за её крайними точками сплайн
                // продолжает форму, а не эффективность.
                if (this.efficiencyCurve != null)
                {
                    minKev = Math.Max(minKev, this.efficiencyCurve.MinKev);
                    maxKev = Math.Min(maxKev, this.efficiencyCurve.MaxKev);
                }

                double[] energies = DoseRateEstimator.BuildGrid(minKev, maxKev);

                var log = new List<string>();
                doseConfig = CalculateDoseRateConfig(this.doseRateSpectrum, this.efficiencyCurve,
                                                     expectedDoseRate, energies, log);
                foreach (string line in log)
                {
                    Trace.WriteLine(line);
                }
            }
            catch (DoseRateRefusalException ex)
            {
                // ⛔ Отказ ВИДИМЫЙ. Прежде обработчик молча выходил по `return`,
                // и человек нажимал кнопку, не получая ни таблицы, ни причины.
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Оценка ЗАМЕЩАЕТ таблицу, а не дописывается к ней. Прежде кнопка
            // включалась только при пустой таблице (см. EvaluateButtonEstimateDRState),
            // и чтобы пересчитать, надо было сначала нажать «Очистить» —
            // C4(г).
            tableModel4.Rows.Clear();
            tableModel4.Rows.AddRange(doseConfig.Select(dc =>
            {
                Row row = new Row();
                row.Cells.Add(new Cell(dc.LowerBound));
                row.Cells.Add(new Cell(dc.UpperBound));
                row.Cells.Add(new Cell(dc.CPS));
                row.Cells.Add(new Cell(dc.EtalonDoseRateValue));

                return row;
            }).ToArray());

            this.SetActiveDeviceConfigDirty();
            this.EvaluateButtonEstimateDRState();
        }

        private void EvaluateButtonEstimateDRState()
        {
            // Без «и таблица пуста»: оценка теперь заменяет содержимое таблицы
            // целиком, поэтому пересчёт не требует предварительной очистки.
            buttonEstimateDRConf.Enabled = doseRateSpectrum != null && efficiencyCurve != null;
        }

        /// <summary>
        /// Точки калибровки мощности дозы.
        ///
        /// ⛔ `C4(в)`. Здесь больше нет ни сетки, ни коэффициентов: пятнадцать
        /// вшитых диапазонов 40–3000 кэВ, шестнадцать значений μ_en/ρ и
        /// шестнадцать значений перевода Р→Зв уехали в
        /// <see cref="DoseRateCoefficients"/>, где у них есть имя, единица и
        /// источник, а μ_en/ρ вообще перестал быть таблицей — считается из XCOM
        /// (`matdb.sqlite`). Сетка приходит снаружи, от шкалы прибора.
        /// </summary>
        private List<DoseRateCalibrationPoint> CalculateDoseRateConfig(
            EnergySpectrum spectrum, DoseRateCurve efficiency, double expectedDoseRate,
            double[] energies, IList<string> log)
        {
            return DoseRateEstimator.Estimate(spectrum, efficiency, expectedDoseRate, energies, log);
        }

        private void buttonClearDoseRate_Click(object sender, EventArgs e)
        {
            tableModel4.Rows.Clear();
            this.SetActiveDeviceConfigDirty();
            this.EvaluateButtonEstimateDRState();
        }

        private void peakTypecomboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdatePeakShapeControlState();
            this.SetActiveDeviceConfigDirty();
        }

        private void leftSkewnumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            StoreCurrentPeakShapeParameters();
            this.SetActiveDeviceConfigDirty();
        }

        private void rightSkewnumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            StoreCurrentPeakShapeParameters();
            this.SetActiveDeviceConfigDirty();
        }
    }
}
