using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.Media;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000D3 RID: 211
    public partial class GlobalConfigForm : Form
    {
        public GlobalConfigForm()
        {
            this.InitializeComponent();
            this.InitializeProgressiveSmoothTooltip();
        }

        // Token: 0x06000AC4 RID: 2756 RVA: 0x0003FE50 File Offset: 0x0003E050
        public GlobalConfigForm(MainForm mainForm)
        {
            this.InitializeComponent();
            this.InitializeProgressiveSmoothTooltip();
            this.mainForm = mainForm;
            base.Icon = Resources.becqmoni;
        }

        void InitializeProgressiveSmoothTooltip()
        {
            string progressiveSmoothTooltipText = this.progressiveSmoothCheckbox.Tag as string;
            if (!string.IsNullOrEmpty(progressiveSmoothTooltipText))
            {
                this.progressiveSmoothTooltip.SetToolTip(this.progressiveSmoothCheckbox, progressiveSmoothTooltipText);
            }
        }

        /// <summary>
        /// (`AMBER201`, мелочь 3.4, 05.10.2026) Цвета формы — из данного набора, не
        /// трогая ни настройку программы, ни прочие поля формы. Им «Сбросить цвета»
        /// показывает умолчания, а применяются они, как и всё остальное, только
        /// кнопками «ОК»/«Применить».
        /// </summary>
        void LoadColorContents(ColorConfig colorConfig)
        {
            this.colorComboBox1.SelectedColor = colorConfig.ActiveSpectrumColor.Color;
            this.colorComboBox2.SelectedColor = colorConfig.BackgroundSpectrumColor.Color;
            this.colorComboBox36.SelectedColor = colorConfig.BgDiffColor.Color;
            SetClamped(this.numericUpDown6, colorConfig.ActiveSpectrumColorTransparency);
            SetClamped(this.numericUpDown7, colorConfig.BackgroundSpectrumColorTransparency);
            this.comboBox15.SelectedIndex = colorConfig.SpectrumDrawingOrder;
            this.colorComboBox17.SelectedColor = colorConfig.SpectrumColorList[0].Color;
            this.colorComboBox18.SelectedColor = colorConfig.SpectrumColorList[1].Color;
            this.colorComboBox19.SelectedColor = colorConfig.SpectrumColorList[2].Color;
            this.colorComboBox20.SelectedColor = colorConfig.SpectrumColorList[3].Color;
            this.colorComboBox21.SelectedColor = colorConfig.SpectrumColorList[4].Color;
            this.colorComboBox22.SelectedColor = colorConfig.SpectrumColorList[5].Color;
            this.colorComboBox23.SelectedColor = colorConfig.SpectrumColorList[6].Color;
            this.colorComboBox24.SelectedColor = colorConfig.SpectrumColorList[7].Color;
            this.colorComboBox28.SelectedColor = colorConfig.SpectrumColorList[8].Color;
            this.colorComboBox29.SelectedColor = colorConfig.SpectrumColorList[9].Color;
            this.colorComboBox30.SelectedColor = colorConfig.SpectrumColorList[10].Color;
            this.colorComboBox31.SelectedColor = colorConfig.SpectrumColorList[11].Color;
            this.colorComboBox32.SelectedColor = colorConfig.SpectrumColorList[12].Color;
            this.colorComboBox33.SelectedColor = colorConfig.SpectrumColorList[13].Color;
            this.colorComboBox34.SelectedColor = colorConfig.SpectrumColorList[14].Color;
            this.colorComboBox35.SelectedColor = colorConfig.SpectrumColorList[15].Color;
            this.colorComboBox37.SelectedColor = colorConfig.UnknownPeakColor.Color;
            this.colorComboBox3.SelectedColor = colorConfig.BackgroundColor.Color;
            this.colorComboBox4.SelectedColor = colorConfig.GridColor1.Color;
            this.colorComboBox5.SelectedColor = colorConfig.GridColor2.Color;
            this.colorComboBox6.SelectedColor = colorConfig.ROIBorderColor.Color;
            this.colorComboBox8.SelectedColor = colorConfig.SelectionBorderColor.Color;
            this.colorComboBox9.SelectedColor = colorConfig.SelectionBackgroundColor.Color;
            this.colorComboBox14.SelectedColor = colorConfig.SelectionNetColor.Color;
            this.colorComboBox10.SelectedColor = colorConfig.AxisBackgroundColor.Color;
            this.colorComboBox11.SelectedColor = colorConfig.AxisFigureColor.Color;
            this.colorComboBox12.SelectedColor = colorConfig.AxisDivisionColor.Color;
            this.colorComboBox25.SelectedColor = colorConfig.PeakBackgroundColor.Color;
            this.colorComboBox26.SelectedColor = colorConfig.PeakFigureColor.Color;
            this.colorComboBox27.SelectedColor = colorConfig.PeakLineColor.Color;
            this.colorComboBox15.SelectedColor = colorConfig.CursorColor.Color;
            this.colorComboBox13.SelectedColor = colorConfig.BlankAreaColor.Color;
        }

        /// <summary>
        /// (`AMBER201`, подозрение полосы 3) Число из файла настроек — в пределы поля.
        /// Значение вне Minimum…Maximum (правленый руками или старый файл) бросало
        /// ArgumentOutOfRangeException из `Value`, и окно «Настройки» не открывалось.
        /// </summary>
        static void SetClamped(NumericUpDown box, decimal value)
        {
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        static void SetClampedDouble(NumericUpDown box, double value)
        {
            if (double.IsNaN(value))
            {
                box.Value = box.Minimum;
                return;
            }
            if (value >= (double)box.Maximum)
            {
                box.Value = box.Maximum;
                return;
            }
            if (value <= (double)box.Minimum)
            {
                box.Value = box.Minimum;
                return;
            }
            box.Value = (decimal)value;
        }

        // Token: 0x06000AC5 RID: 2757 RVA: 0x0003FEB4 File Offset: 0x0003E0B4
        public void LoadFormContents(GlobalConfigInfo globalConfig)
        {
            this.LoadColorContents(globalConfig.ColorConfig);
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
            this.doubleTextBox3.Text = globalConfig.ChartViewConfig.Energy2ndCoefficientStep.ToString(CultureInfo.InvariantCulture);
            this.doubleTextBox1.Text = globalConfig.ChartViewConfig.EnergyCoefficientStep.ToString(CultureInfo.InvariantCulture);
            this.doubleTextBox2.Text = globalConfig.ChartViewConfig.EnergyOffsetStep.ToString(CultureInfo.InvariantCulture);
            SetClamped(this.numericUpDown8, globalConfig.ChartViewConfig.EnergyPitch);
            SetClamped(this.numericUpDown9, globalConfig.ChartViewConfig.EnergyPercent);
            SetClamped(this.numericUpDown1, globalConfig.ChartViewConfig.NumberOfSMADataPoints);
            SetClamped(this.numericUpDown2, globalConfig.ChartViewConfig.NumberOfWMADataPoints);
            SetClamped(this.numericUpDown13, (int)globalConfig.ChartViewConfig.CountLimit);
            this.progressiveSmoothCheckbox.Checked = globalConfig.ChartViewConfig.ProgresiveSmooth;
            SetClamped(this.numericUpDown3, globalConfig.ChartViewConfig.ChartRefreshCycle);
            SetClamped(this.numericUpDown10, (int)globalConfig.AutosavePeriod);
            // (`AMBER211`) пауза счёта FSA при записи спектра, с
            SetClamped(this.numericUpDown11, globalConfig.FsaAcquisitionIntervalSeconds);
            SetClampedDouble(this.numericUpDown14, globalConfig.ChartViewConfig.HorizontalScale);
            this.comboBox11.SelectedIndex = (int)globalConfig.ChartViewConfig.MagnificationReference;
            this.autoSaveDefaultPolicyCheckBox.Checked = globalConfig.AutosaveDefaultPolicy;
            this.importSpectrumWithEmptyConfigCheckBox.Checked = globalConfig.ImportSpectrumWithEmptyConfig;
            // (`AMBER202`) матрица отклика — в файл спектра
            this.saveResponseMatrixInSpectrumCheckBox.Checked = globalConfig.SaveResponseMatrixInSpectrum;
            this.confidenceLevelcomboBox.SelectedIndex = ConfidenceLevel.GetSingleSideLevelIndex(globalConfig.ChartViewConfig.ConfidenceLevel);
            this.textBox6.Text = globalConfig.SoundConfig.MeasurementCompletion;
            this.comboBox12.SelectedIndex = (int)globalConfig.MeasurementConfig.VolumeUnit;
            this.comboBox13.SelectedIndex = (int)globalConfig.MeasurementConfig.WeightUnit;
            SetClamped(this.numericUpDown4, globalConfig.MeasurementConfig.ErrorLevel);
            SetClamped(this.numericUpDown5, globalConfig.MeasurementConfig.DetectionLevel);
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
            globalConfig.ChartViewConfig.CountLimit = (int)this.numericUpDown13.Value;
            globalConfig.ChartViewConfig.ProgresiveSmooth = this.progressiveSmoothCheckbox.Checked;
            globalConfig.ChartViewConfig.ChartRefreshCycle = (int)this.numericUpDown3.Value;
            globalConfig.ChartViewConfig.MagnificationReference = (MagnificationReference)this.comboBox11.SelectedIndex;
            globalConfig.ChartViewConfig.ConfidenceLevel = ConfidenceLevel.z_score_single_side[this.confidenceLevelcomboBox.SelectedIndex];
            globalConfig.SoundConfig.MeasurementCompletion = this.textBox6.Text;
            globalConfig.MeasurementConfig.VolumeUnit = (VolumeUnit)this.comboBox12.SelectedIndex;
            globalConfig.MeasurementConfig.WeightUnit = (WeightUnit)this.comboBox13.SelectedIndex;
            globalConfig.MeasurementConfig.ErrorLevel = this.numericUpDown4.Value;
            globalConfig.MeasurementConfig.DetectionLevel = this.numericUpDown5.Value;
            globalConfig.MeasurementConfig.DetectionCondition = this.comboBox14.SelectedIndex;
            globalConfig.MeasurementConfig.ShowValuesForNDResult = this.checkBox2.Checked;
            globalConfig.DoSaveRawPulseData = this.checkBox1.Checked;
            globalConfig.AutosavePeriod = (int)this.numericUpDown10.Value;
            globalConfig.FsaAcquisitionIntervalSeconds = (int)this.numericUpDown11.Value;
            globalConfig.AutosaveDefaultPolicy = this.autoSaveDefaultPolicyCheckBox.Checked;
            globalConfig.ImportSpectrumWithEmptyConfig = this.importSpectrumWithEmptyConfigCheckBox.Checked;
            globalConfig.SaveResponseMatrixInSpectrum = this.saveResponseMatrixInSpectrumCheckBox.Checked;
            globalConfig.ChartViewConfig.HorizontalScale = (double)this.numericUpDown14.Value;

        }

        // Token: 0x06000AC9 RID: 2761 RVA: 0x00041130 File Offset: 0x0003F330
        void button1_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.SaveFormContents(globalConfig);
            // (`AMBER201`, мелочь 3.5) Принятые настройки — на диск сразу, а не
            // только при закрытии программы: падение или выключение до закрытия
            // теряло их целиком. Отказ записи называет сам менеджер.
            GlobalConfigManager.GetInstance().SaveConfigFile();
            this.mainForm.RefreshAllView();
            base.Close();
        }

        void ComboBox10_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            if ((HorizontalMagnification)comboBox10.SelectedIndex == HorizontalMagnification.Equal)
            {
                numericUpDown14.Enabled = true;
            } else
            {
                numericUpDown14.Enabled = false;
            }
        }

        // Token: 0x06000ACA RID: 2762 RVA: 0x00041164 File Offset: 0x0003F364
        void button3_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.SaveFormContents(globalConfig);
            // (`AMBER201`, мелочь 3.5) Принятые настройки — на диск сразу, а не
            // только при закрытии программы: падение или выключение до закрытия
            // теряло их целиком. Отказ записи называет сам менеджер.
            GlobalConfigManager.GetInstance().SaveConfigFile();
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
            // (`AMBER201`, мелочь 3.4) Прежде здесь ВСЕ несохранённые правки формы
            // записывались в настройку программы (SaveFormContents), а цвета
            // заменялись в ней же — «Отмена» после сброса ничего не возвращала.
            // Теперь умолчания только показываются в полях формы.
            ColorConfig defaults = new ColorConfig();
            defaults.InitializeSpectrumColor();
            this.LoadColorContents(defaults);
            this.Refresh();
        }

        // Token: 0x06000ACE RID: 2766 RVA: 0x00041268 File Offset: 0x0003F468
        void button6_Click(object sender, EventArgs e)
        {
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            // ⚠ `A12`. Фильтр и заголовок были зашиты по-английски при
            // живой паре `GlobalConfigForm.ru.resx`.
            openFileDialog.Filter = Resources.WAVFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.Title = Resources.SelectSoundFileTitle;
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

    }
}
