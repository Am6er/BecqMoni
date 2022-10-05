using BecquerelMonitor.Properties;
using System;
using System.Media;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000D3 RID: 211
    public partial class GlobalConfigForm : Form
    {
        // Token: 0x06000AC4 RID: 2756 RVA: 0x0003FE50 File Offset: 0x0003E050
        public GlobalConfigForm(MainForm mainForm)
        {
            this.InitializeComponent();
            this.mainForm = mainForm;
            base.Icon = Resources.becqmoni;
            this.UpdateDeviceConfigList();
            this.UpdateROIConfigList();
            this.tabControl1.TabPages.RemoveAt(2);
        }

        // Token: 0x06000AC5 RID: 2757 RVA: 0x0003FEB4 File Offset: 0x0003E0B4
        public void LoadFormContents(GlobalConfigInfo globalConfig)
        {
            this.colorComboBox1.SelectedColor = globalConfig.ColorConfig.ActiveSpectrumColor.Color;
            this.colorComboBox2.SelectedColor = globalConfig.ColorConfig.BackgroundSpectrumColor.Color;
            this.colorComboBox36.SelectedColor = globalConfig.ColorConfig.BgDiffColor.Color;
            this.numericUpDown6.Value = globalConfig.ColorConfig.ActiveSpectrumColorTransparency;
            this.numericUpDown7.Value = globalConfig.ColorConfig.BackgroundSpectrumColorTransparency;
            this.comboBox15.SelectedIndex = globalConfig.ColorConfig.SpectrumDrawingOrder;
            this.colorComboBox17.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[0].Color;
            this.colorComboBox18.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[1].Color;
            this.colorComboBox19.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[2].Color;
            this.colorComboBox20.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[3].Color;
            this.colorComboBox21.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[4].Color;
            this.colorComboBox22.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[5].Color;
            this.colorComboBox23.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[6].Color;
            this.colorComboBox24.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[7].Color;
            this.colorComboBox28.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[8].Color;
            this.colorComboBox29.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[9].Color;
            this.colorComboBox30.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[10].Color;
            this.colorComboBox31.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[11].Color;
            this.colorComboBox32.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[12].Color;
            this.colorComboBox33.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[13].Color;
            this.colorComboBox34.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[14].Color;
            this.colorComboBox35.SelectedColor = globalConfig.ColorConfig.SpectrumColorList[15].Color;
            this.colorComboBox37.SelectedColor = globalConfig.ColorConfig.UnknownPeakColor.Color;
            this.colorComboBox3.SelectedColor = globalConfig.ColorConfig.BackgroundColor.Color;
            this.colorComboBox4.SelectedColor = globalConfig.ColorConfig.GridColor1.Color;
            this.colorComboBox5.SelectedColor = globalConfig.ColorConfig.GridColor2.Color;
            this.colorComboBox6.SelectedColor = globalConfig.ColorConfig.ROIBorderColor.Color;
            this.colorComboBox7.SelectedColor = globalConfig.ColorConfig.ROIBackgroundColor.Color;
            this.colorComboBox16.SelectedColor = globalConfig.ColorConfig.ROINetColor.Color;
            this.colorComboBox8.SelectedColor = globalConfig.ColorConfig.SelectionBorderColor.Color;
            this.colorComboBox9.SelectedColor = globalConfig.ColorConfig.SelectionBackgroundColor.Color;
            this.colorComboBox14.SelectedColor = globalConfig.ColorConfig.SelectionNetColor.Color;
            this.colorComboBox10.SelectedColor = globalConfig.ColorConfig.AxisBackgroundColor.Color;
            this.colorComboBox11.SelectedColor = globalConfig.ColorConfig.AxisFigureColor.Color;
            this.colorComboBox12.SelectedColor = globalConfig.ColorConfig.AxisDivisionColor.Color;
            this.colorComboBox25.SelectedColor = globalConfig.ColorConfig.PeakBackgroundColor.Color;
            this.colorComboBox26.SelectedColor = globalConfig.ColorConfig.PeakFigureColor.Color;
            this.colorComboBox27.SelectedColor = globalConfig.ColorConfig.PeakLineColor.Color;
            this.colorComboBox15.SelectedColor = globalConfig.ColorConfig.CursorColor.Color;
            this.colorComboBox13.SelectedColor = globalConfig.ColorConfig.BlankAreaColor.Color;
            this.comboBox1.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultVerticalUnit;
            this.comboBox2.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultVerticalScaleType;
            this.comboBox3.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultChartType;
            this.comboBox4.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultVerticalFittingMode;
            this.comboBox5.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultHorizontalUnit;
            this.comboBox6.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultSmoothingMothod;
            this.comboBox7.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultBackgroundMode;
            this.comboBox8.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultDrawingMode;
            this.comboBox9.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultPeakMode;
            this.comboBox10.SelectedIndex = (int)globalConfig.ChartViewConfig.DefaultHorizontalMagnification;
            this.doubleTextBox3.Text = globalConfig.ChartViewConfig.Energy2ndCoefficientStep.ToString();
            this.doubleTextBox1.Text = globalConfig.ChartViewConfig.EnergyCoefficientStep.ToString();
            this.doubleTextBox2.Text = globalConfig.ChartViewConfig.EnergyOffsetStep.ToString();
            this.numericUpDown8.Value = globalConfig.ChartViewConfig.EnergyPitch;
            this.numericUpDown9.Value = globalConfig.ChartViewConfig.EnergyPercent;
            this.numericUpDown1.Value = globalConfig.ChartViewConfig.NumberOfSMADataPoints;
            this.numericUpDown2.Value = globalConfig.ChartViewConfig.NumberOfWMADataPoints;
            this.numericUpDown3.Value = globalConfig.ChartViewConfig.ChartRefreshCycle;
            this.comboBox11.SelectedIndex = (int)globalConfig.ChartViewConfig.MagnificationReference;
            EasyControlConfig easyControlConfig = globalConfig.EasyControlConfig;
            bool flag = false;
            for (int i = 0; i < this.deviceConfigManager.DeviceConfigList.Count; i++)
            {
                DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigList[i];
                if (easyControlConfig.DeviceConfig != null && easyControlConfig.DeviceConfig.Guid == deviceConfigInfo.Guid)
                {
                    this.comboBox16.SelectedIndex = i;
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                this.comboBox16.SelectedIndex = -1;
            }
            flag = false;
            for (int j = 0; j < this.roiConfigManager.ROIConfigList.Count; j++)
            {
                ROIConfigData roiconfigData = this.roiConfigManager.ROIConfigList[j];
                if (easyControlConfig.ROIConfig != null && easyControlConfig.ROIConfig.Guid == roiconfigData.Guid)
                {
                    this.comboBox17.SelectedIndex = j;
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                this.comboBox17.SelectedIndex = -1;
            }
            this.textBox2.Text = easyControlConfig.SpectraFolder;
            this.textBox3.Text = easyControlConfig.EnergyCalibrationFilePrefix;
            this.textBox4.Text = easyControlConfig.BackgroundFilePrefix;
            this.textBox5.Text = easyControlConfig.SampleFilePrefix;
            this.integerTextBox11.Text = easyControlConfig.WarmupTime.ToString();
            this.integerTextBox12.Text = easyControlConfig.CalibrationTime.ToString();
            this.integerTextBox13.Text = easyControlConfig.ShutdownTime.ToString();
            this.integerTextBox1.Text = easyControlConfig.DefaultBGPresetTime.ToString();
            this.integerTextBox2.Text = easyControlConfig.BGPresetTime1.ToString();
            this.integerTextBox3.Text = easyControlConfig.BGPresetTime2.ToString();
            this.integerTextBox4.Text = easyControlConfig.BGPresetTime3.ToString();
            this.integerTextBox5.Text = easyControlConfig.BGPresetTime4.ToString();
            this.integerTextBox6.Text = easyControlConfig.DefaultPresetTime.ToString();
            this.integerTextBox7.Text = easyControlConfig.PresetTime1.ToString();
            this.integerTextBox8.Text = easyControlConfig.PresetTime2.ToString();
            this.integerTextBox9.Text = easyControlConfig.PresetTime3.ToString();
            this.integerTextBox10.Text = easyControlConfig.PresetTime4.ToString();
            this.textBox6.Text = globalConfig.SoundConfig.MeasurementCompletion;
            this.comboBox12.SelectedIndex = (int)globalConfig.MeasurementConfig.VolumeUnit;
            this.comboBox13.SelectedIndex = (int)globalConfig.MeasurementConfig.WeightUnit;
            this.numericUpDown4.Value = globalConfig.MeasurementConfig.ErrorLevel;
            this.numericUpDown5.Value = globalConfig.MeasurementConfig.DetectionLevel;
            this.comboBox14.SelectedIndex = globalConfig.MeasurementConfig.DetectionCondition;
            this.checkBox2.Checked = globalConfig.MeasurementConfig.ShowValuesForNDResult;
            this.checkBox1.Checked = globalConfig.DoSaveRawPulseData;
        }

        // Token: 0x06000AC6 RID: 2758 RVA: 0x00040814 File Offset: 0x0003EA14
        public void SaveFormContents(GlobalConfigInfo globalConfig)
        {
            globalConfig.ColorConfig.ActiveSpectrumColor.Color = this.colorComboBox1.SelectedColor;
            globalConfig.ColorConfig.BackgroundSpectrumColor.Color = this.colorComboBox2.SelectedColor;
            globalConfig.ColorConfig.BgDiffColor.Color = this.colorComboBox36.SelectedColor;
            globalConfig.ColorConfig.ActiveSpectrumColorTransparency = this.numericUpDown6.Value;
            globalConfig.ColorConfig.BackgroundSpectrumColorTransparency = this.numericUpDown7.Value;
            globalConfig.ColorConfig.SpectrumDrawingOrder = this.comboBox15.SelectedIndex;
            globalConfig.ColorConfig.SpectrumColorList[0].Color = this.colorComboBox17.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[1].Color = this.colorComboBox18.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[2].Color = this.colorComboBox19.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[3].Color = this.colorComboBox20.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[4].Color = this.colorComboBox21.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[5].Color = this.colorComboBox22.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[6].Color = this.colorComboBox23.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[7].Color = this.colorComboBox24.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[8].Color = this.colorComboBox28.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[9].Color = this.colorComboBox29.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[10].Color = this.colorComboBox30.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[11].Color = this.colorComboBox31.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[12].Color = this.colorComboBox32.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[13].Color = this.colorComboBox33.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[14].Color = this.colorComboBox34.SelectedColor;
            globalConfig.ColorConfig.SpectrumColorList[15].Color = this.colorComboBox35.SelectedColor;
            globalConfig.ColorConfig.UnknownPeakColor.Color = this.colorComboBox37.SelectedColor;
            globalConfig.ColorConfig.BackgroundColor.Color = this.colorComboBox3.SelectedColor;
            globalConfig.ColorConfig.GridColor1.Color = this.colorComboBox4.SelectedColor;
            globalConfig.ColorConfig.GridColor2.Color = this.colorComboBox5.SelectedColor;
            globalConfig.ColorConfig.ROIBorderColor.Color = this.colorComboBox6.SelectedColor;
            globalConfig.ColorConfig.ROIBackgroundColor.Color = this.colorComboBox7.SelectedColor;
            globalConfig.ColorConfig.ROINetColor.Color = this.colorComboBox16.SelectedColor;
            globalConfig.ColorConfig.SelectionBorderColor.Color = this.colorComboBox8.SelectedColor;
            globalConfig.ColorConfig.SelectionBackgroundColor.Color = this.colorComboBox9.SelectedColor;
            globalConfig.ColorConfig.SelectionNetColor.Color = this.colorComboBox14.SelectedColor;
            globalConfig.ColorConfig.AxisBackgroundColor.Color = this.colorComboBox10.SelectedColor;
            globalConfig.ColorConfig.AxisFigureColor.Color = this.colorComboBox11.SelectedColor;
            globalConfig.ColorConfig.AxisDivisionColor.Color = this.colorComboBox12.SelectedColor;
            globalConfig.ColorConfig.PeakBackgroundColor.Color = this.colorComboBox25.SelectedColor;
            globalConfig.ColorConfig.PeakFigureColor.Color = this.colorComboBox26.SelectedColor;
            globalConfig.ColorConfig.PeakLineColor.Color = this.colorComboBox27.SelectedColor;
            globalConfig.ColorConfig.CursorColor.Color = this.colorComboBox15.SelectedColor;
            globalConfig.ColorConfig.BlankAreaColor.Color = this.colorComboBox13.SelectedColor;
            globalConfig.ChartViewConfig.EnergyPitch = this.numericUpDown8.Value;
            globalConfig.ChartViewConfig.EnergyPercent = this.numericUpDown9.Value;
            globalConfig.ChartViewConfig.DefaultVerticalUnit = (VerticalUnit)this.comboBox1.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultVerticalScaleType = (VerticalScaleType)this.comboBox2.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultChartType = (ChartType)this.comboBox3.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultVerticalFittingMode = (VerticalFittingMode)this.comboBox4.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultHorizontalUnit = (HorizontalUnit)this.comboBox5.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultSmoothingMothod = (SmoothingMethod)this.comboBox6.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultBackgroundMode = (BackgroundMode)this.comboBox7.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultDrawingMode = (DrawingMode)this.comboBox8.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultPeakMode = (PeakMode)this.comboBox9.SelectedIndex;
            globalConfig.ChartViewConfig.DefaultHorizontalMagnification = (HorizontalMagnification)this.comboBox10.SelectedIndex;
            globalConfig.ChartViewConfig.Energy2ndCoefficientStep = this.doubleTextBox3.GetValue();
            globalConfig.ChartViewConfig.EnergyCoefficientStep = this.doubleTextBox1.GetValue();
            globalConfig.ChartViewConfig.EnergyOffsetStep = this.doubleTextBox2.GetValue();
            globalConfig.ChartViewConfig.NumberOfSMADataPoints = (int)this.numericUpDown1.Value;
            globalConfig.ChartViewConfig.NumberOfWMADataPoints = (int)this.numericUpDown2.Value;
            globalConfig.ChartViewConfig.ChartRefreshCycle = (int)this.numericUpDown3.Value;
            globalConfig.ChartViewConfig.MagnificationReference = (MagnificationReference)this.comboBox11.SelectedIndex;
            EasyControlConfig easyControlConfig = globalConfig.EasyControlConfig;
            if (this.comboBox16.SelectedIndex >= 0)
            {
                easyControlConfig.DeviceConfig = this.deviceConfigManager.DeviceConfigList[this.comboBox16.SelectedIndex];
                easyControlConfig.DeviceConfigReference = easyControlConfig.DeviceConfig.CreateReference();
            }
            if (this.comboBox17.SelectedIndex >= 0)
            {
                easyControlConfig.ROIConfig = this.roiConfigManager.ROIConfigList[this.comboBox17.SelectedIndex];
                easyControlConfig.ROIConfigReference = easyControlConfig.ROIConfig.CreateReference();
            }
            easyControlConfig.SpectraFolder = this.textBox2.Text;
            easyControlConfig.EnergyCalibrationFilePrefix = this.textBox3.Text;
            easyControlConfig.BackgroundFilePrefix = this.textBox4.Text;
            easyControlConfig.SampleFilePrefix = this.textBox5.Text;
            easyControlConfig.WarmupTime = this.integerTextBox11.GetValue();
            easyControlConfig.CalibrationTime = this.integerTextBox12.GetValue();
            easyControlConfig.ShutdownTime = this.integerTextBox13.GetValue();
            easyControlConfig.DefaultBGPresetTime = this.integerTextBox1.GetValue();
            easyControlConfig.BGPresetTime1 = this.integerTextBox2.GetValue();
            easyControlConfig.BGPresetTime2 = this.integerTextBox3.GetValue();
            easyControlConfig.BGPresetTime3 = this.integerTextBox4.GetValue();
            easyControlConfig.BGPresetTime4 = this.integerTextBox5.GetValue();
            easyControlConfig.DefaultPresetTime = this.integerTextBox6.GetValue();
            easyControlConfig.PresetTime1 = this.integerTextBox7.GetValue();
            easyControlConfig.PresetTime2 = this.integerTextBox8.GetValue();
            easyControlConfig.PresetTime3 = this.integerTextBox9.GetValue();
            easyControlConfig.PresetTime4 = this.integerTextBox10.GetValue();
            globalConfig.SoundConfig.MeasurementCompletion = this.textBox6.Text;
            globalConfig.MeasurementConfig.VolumeUnit = (VolumeUnit)this.comboBox12.SelectedIndex;
            globalConfig.MeasurementConfig.WeightUnit = (WeightUnit)this.comboBox13.SelectedIndex;
            globalConfig.MeasurementConfig.ErrorLevel = this.numericUpDown4.Value;
            globalConfig.MeasurementConfig.DetectionLevel = this.numericUpDown5.Value;
            globalConfig.MeasurementConfig.DetectionCondition = this.comboBox14.SelectedIndex;
            globalConfig.MeasurementConfig.ShowValuesForNDResult = this.checkBox2.Checked;
            globalConfig.DoSaveRawPulseData = this.checkBox1.Checked;
        }

        // Token: 0x06000AC7 RID: 2759 RVA: 0x00041060 File Offset: 0x0003F260
        void UpdateDeviceConfigList()
        {
            this.comboBox16.Items.Clear();
            for (int i = 0; i < this.deviceConfigManager.DeviceConfigList.Count; i++)
            {
                DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigList[i];
                this.comboBox16.Items.Add(deviceConfigInfo.Name);
            }
        }

        // Token: 0x06000AC8 RID: 2760 RVA: 0x000410C8 File Offset: 0x0003F2C8
        void UpdateROIConfigList()
        {
            this.comboBox17.Items.Clear();
            for (int i = 0; i < this.roiConfigManager.ROIConfigList.Count; i++)
            {
                ROIConfigData roiconfigData = this.roiConfigManager.ROIConfigList[i];
                this.comboBox17.Items.Add(roiconfigData.Name);
            }
        }

        // Token: 0x06000AC9 RID: 2761 RVA: 0x00041130 File Offset: 0x0003F330
        void button1_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.SaveFormContents(globalConfig);
            this.mainForm.RefreshAllView();
            base.Close();
        }

        // Token: 0x06000ACA RID: 2762 RVA: 0x00041164 File Offset: 0x0003F364
        void button3_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.SaveFormContents(globalConfig);
            this.mainForm.RefreshAllView();
        }

        // Token: 0x06000ACB RID: 2763 RVA: 0x00041194 File Offset: 0x0003F394
        void button2_Click(object sender, EventArgs e)
        {
            base.Close();
        }

        // Token: 0x06000ACC RID: 2764 RVA: 0x0004119C File Offset: 0x0003F39C
        void button4_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Resources.MSGInitializeAllColorSetting, Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
            {
                return;
            }
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.SaveFormContents(globalConfig);
            globalConfig.ColorConfig = new ColorConfig();
            globalConfig.ColorConfig.InitializeSpectrumColor();
            this.LoadFormContents(globalConfig);
            this.Refresh();
        }

        // Token: 0x06000ACD RID: 2765 RVA: 0x000411FC File Offset: 0x0003F3FC
        void button5_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.Description = "スペクトル保存フォルダを指定してください";
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.Desktop;
            folderBrowserDialog.SelectedPath = globalConfig.EasyControlConfig.SpectraFolder;
            folderBrowserDialog.ShowNewFolderButton = true;
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                this.textBox2.Text = folderBrowserDialog.SelectedPath;
            }
        }

        // Token: 0x06000ACE RID: 2766 RVA: 0x00041268 File Offset: 0x0003F468
        void button6_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "WAVファイル(*.wav)|*.wav|すべてのファイル(*.*)|*.*";
            openFileDialog.FilterIndex = 1;
            openFileDialog.Title = "サウンドファイルを選択してください";
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                this.textBox6.Text = openFileDialog.FileName;
            }
        }

        // Token: 0x06000ACF RID: 2767 RVA: 0x000412CC File Offset: 0x0003F4CC
        void button7_Click(object sender, EventArgs e)
        {
            string text = this.textBox6.Text;
            if (text != null && text != "")
            {
                SoundPlayer soundPlayer = new SoundPlayer(text);
                soundPlayer.Play();
            }
        }

        // Token: 0x040005F2 RID: 1522
        public MainForm mainForm;

        // Token: 0x040005F3 RID: 1523
        DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();

        // Token: 0x040005F4 RID: 1524
        ROIConfigManager roiConfigManager = ROIConfigManager.GetInstance();
    }
}
