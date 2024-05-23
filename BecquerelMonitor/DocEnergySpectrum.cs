using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System.Diagnostics;
using System.Threading;

namespace BecquerelMonitor
{
    // Token: 0x0200003C RID: 60
    public partial class DocEnergySpectrum : DockContent
    {
        // Token: 0x14000006 RID: 6
        // (add) Token: 0x060002FA RID: 762 RVA: 0x0000F3BC File Offset: 0x0000D5BC
        // (remove) Token: 0x060002FB RID: 763 RVA: 0x0000F3F8 File Offset: 0x0000D5F8
        public event EventHandler CloseDocument;

        // Token: 0x14000007 RID: 7
        // (add) Token: 0x060002FC RID: 764 RVA: 0x0000F434 File Offset: 0x0000D634
        // (remove) Token: 0x060002FD RID: 765 RVA: 0x0000F470 File Offset: 0x0000D670
        public event EventHandler SaveDocument;

        // Token: 0x14000008 RID: 8
        // (add) Token: 0x060002FE RID: 766 RVA: 0x0000F4AC File Offset: 0x0000D6AC
        // (remove) Token: 0x060002FF RID: 767 RVA: 0x0000F4E8 File Offset: 0x0000D6E8
        public event EventHandler CreateNewROI;

        // Token: 0x14000009 RID: 9
        // (add) Token: 0x06000300 RID: 768 RVA: 0x0000F524 File Offset: 0x0000D724
        // (remove) Token: 0x06000301 RID: 769 RVA: 0x0000F560 File Offset: 0x0000D760
        public event EventHandler SetLowerThreshold;

        // Token: 0x1400000A RID: 10
        // (add) Token: 0x06000302 RID: 770 RVA: 0x0000F59C File Offset: 0x0000D79C
        // (remove) Token: 0x06000303 RID: 771 RVA: 0x0000F5D8 File Offset: 0x0000D7D8
        public event EventHandler SetUpperThreshold;

        // Token: 0x1400000B RID: 11
        // (add) Token: 0x06000304 RID: 772 RVA: 0x0000F614 File Offset: 0x0000D814
        // (remove) Token: 0x06000305 RID: 773 RVA: 0x0000F650 File Offset: 0x0000D850
        public event ShowEnergyCalibrationViewEventHandler ShowEnergyCalibrationView;

        // Token: 0x1400000C RID: 12
        // (add) Token: 0x06000306 RID: 774 RVA: 0x0000F68C File Offset: 0x0000D88C
        // (remove) Token: 0x06000307 RID: 775 RVA: 0x0000F6C8 File Offset: 0x0000D8C8
        public event AddSpectrumToDocumentEventHandler AddSpectrumToDocument;

        // Token: 0x1700013F RID: 319
        // (get) Token: 0x06000308 RID: 776 RVA: 0x0000F704 File Offset: 0x0000D904
        // (set) Token: 0x06000309 RID: 777 RVA: 0x0000F70C File Offset: 0x0000D90C
        public ResultDataFile ResultDataFile
        {
            get
            {
                return this.resultDataFile;
            }
            set
            {
                this.resultDataFile = value;
            }
        }

        // Token: 0x17000140 RID: 320
        // (get) Token: 0x0600030A RID: 778 RVA: 0x0000F718 File Offset: 0x0000D918
        // (set) Token: 0x0600030B RID: 779 RVA: 0x0000F720 File Offset: 0x0000D920
        public int ActiveResultDataIndex
        {
            get
            {
                return this.activeResultDataIndex;
            }
            set
            {
                this.activeResultDataIndex = value;
            }
        }

        public bool AutoSave
        {
            get
            {
                return this.autosave;
            }
            set
            {
                this.autosave = value;
            }
        }

        // Token: 0x17000141 RID: 321
        // (get) Token: 0x0600030C RID: 780 RVA: 0x0000F72C File Offset: 0x0000D92C
        // (set) Token: 0x0600030D RID: 781 RVA: 0x0000F744 File Offset: 0x0000D944
        public ResultData ActiveResultData
        {
            get
            {
                return this.resultDataFile.ResultDataList[this.activeResultDataIndex];
            }
            set
            {
                this.resultDataFile.ResultDataList[this.activeResultDataIndex] = value;
            }
        }

        // Token: 0x17000142 RID: 322
        // (get) Token: 0x0600030E RID: 782 RVA: 0x0000F760 File Offset: 0x0000D960
        // (set) Token: 0x0600030F RID: 783 RVA: 0x0000F768 File Offset: 0x0000D968
        public bool UpdateMeasurementResult
        {
            get
            {
                return this.updateMeasurementResult;
            }
            set
            {
                this.updateMeasurementResult = value;
            }
        }

        // Token: 0x17000143 RID: 323
        // (get) Token: 0x06000310 RID: 784 RVA: 0x0000F774 File Offset: 0x0000D974
        // (set) Token: 0x06000311 RID: 785 RVA: 0x0000F77C File Offset: 0x0000D97C
        public bool UpdateSpectrum
        {
            get
            {
                return this.updateSpectrum;
            }
            set
            {
                this.updateSpectrum = value;
            }
        }

        public bool UpdateDetectedPeaks
        {
            get
            {
                return this.updateDetectedPeaks;
            }
            set
            {
                this.updateDetectedPeaks = value;
            }
        }

        public bool UpdateDoseRate
        {
            get
            {
                return this.updateDoseRate;
            }
            set
            {
                this.updateDoseRate = value;
            }
        }

        // Token: 0x17000144 RID: 324
        // (get) Token: 0x06000312 RID: 786 RVA: 0x0000F788 File Offset: 0x0000D988
        // (set) Token: 0x06000313 RID: 787 RVA: 0x0000F790 File Offset: 0x0000D990
        public bool ActiveEnergyCalibration
        {
            get
            {
                return this.activeEnergyCalibration;
            }
            set
            {
                this.activeEnergyCalibration = value;
                if (this.activeEnergyCalibration)
                {
                    this.toolStripSplitButton10.Image = BecquerelMonitor.Properties.Resources.EnergyCalibration;
                    return;
                }
                this.toolStripSplitButton10.Image = BecquerelMonitor.Properties.Resources.EnergyCalibration2;
            }
        }

        // Token: 0x17000145 RID: 325
        // (get) Token: 0x06000314 RID: 788 RVA: 0x0000F7C8 File Offset: 0x0000D9C8
        // (set) Token: 0x06000315 RID: 789 RVA: 0x0000F7D0 File Offset: 0x0000D9D0
        public bool IsActivating
        {
            get
            {
                return this.isActivating;
            }
            set
            {
                this.isActivating = value;
            }
        }

        // Token: 0x17000146 RID: 326
        // (get) Token: 0x06000316 RID: 790 RVA: 0x0000F7DC File Offset: 0x0000D9DC
        // (set) Token: 0x06000317 RID: 791 RVA: 0x0000F818 File Offset: 0x0000DA18
        public PulseDetector PulseDetector
        {
            get
            {
                DeviceController deviceController = this.ActiveResultData.MeasurementController.DeviceController;
                if (deviceController is AudioInputDeviceController)
                {
                    return ((AudioInputDeviceController)deviceController).PulseDetector;
                }
                else if (deviceController is AtomSpectraDeviceController)
                {
                    return ((AtomSpectraDeviceController)deviceController).PulseDetector;
                }
                else if (deviceController is RadiaCodeDeviceController)
                {
                    return ((RadiaCodeDeviceController)deviceController).PulseDetector;
                }
                return null;
            }
            set
            {
                DeviceController deviceController = this.ActiveResultData.MeasurementController.DeviceController;
                if (deviceController is AudioInputDeviceController)
                {
                    ((AudioInputDeviceController)deviceController).PulseDetector = value;
                }
                else if (deviceController is AtomSpectraDeviceController)
                {
                    ((AtomSpectraDeviceController)deviceController).PulseDetector = value;
                }
                else if (deviceController is RadiaCodeDeviceController)
                {
                    ((RadiaCodeDeviceController)deviceController).PulseDetector = value;
                }
            }
        }

        // Token: 0x17000147 RID: 327
        // (get) Token: 0x06000318 RID: 792 RVA: 0x0000F854 File Offset: 0x0000DA54
        // (set) Token: 0x06000319 RID: 793 RVA: 0x0000F85C File Offset: 0x0000DA5C
        public string Filename
        {
            get
            {
                return this.filename;
            }
            set
            {
                this.filename = value;
                this.Text = Path.GetFileNameWithoutExtension(this.filename);
            }
        }

        // Token: 0x17000148 RID: 328
        // (get) Token: 0x0600031A RID: 794 RVA: 0x0000F878 File Offset: 0x0000DA78
        // (set) Token: 0x0600031B RID: 795 RVA: 0x0000F880 File Offset: 0x0000DA80
        public bool IsNamed
        {
            get
            {
                return this.isNamed;
            }
            set
            {
                this.isNamed = value;
            }
        }

        // Token: 0x17000149 RID: 329
        // (get) Token: 0x0600031C RID: 796 RVA: 0x0000F88C File Offset: 0x0000DA8C
        public EnergySpectrumView EnergySpectrumView
        {
            get
            {
                return this.energySpectrumView1;
            }
        }

        // Token: 0x1700014A RID: 330
        // (get) Token: 0x0600031D RID: 797 RVA: 0x0000F894 File Offset: 0x0000DA94
        // (set) Token: 0x0600031E RID: 798 RVA: 0x0000F89C File Offset: 0x0000DA9C
        public bool Dirty
        {
            get
            {
                return this.dirty;
            }
            set
            {
                this.dirty = value;
            }
        }

        // Token: 0x0600031F RID: 799 RVA: 0x0000F8A8 File Offset: 0x0000DAA8
        public DocEnergySpectrum()
        {
            this.InitializeComponent();
            this.resultDataFile = new ResultDataFile();
            this.resultDataFile.InitFormatVersion();
            this.CreateResultData();
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.view = this.energySpectrumView1;
            this.view.VerticalUnit = globalConfig.ChartViewConfig.DefaultVerticalUnit;
            this.view.VerticalScaleType = globalConfig.ChartViewConfig.DefaultVerticalScaleType;
            this.view.ChartType = globalConfig.ChartViewConfig.DefaultChartType;
            this.view.VerticalFittingMode = globalConfig.ChartViewConfig.DefaultVerticalFittingMode;
            this.view.SmoothingMethod = globalConfig.ChartViewConfig.DefaultSmoothingMothod;
            this.view.HorizontalUnit = globalConfig.ChartViewConfig.DefaultHorizontalUnit;
            this.view.BackgroundMode = globalConfig.ChartViewConfig.DefaultBackgroundMode;
            this.view.DrawingMode = globalConfig.ChartViewConfig.DefaultDrawingMode;
            this.view.PeakMode = globalConfig.ChartViewConfig.DefaultPeakMode;
            this.view.HorizontalMagnification = globalConfig.ChartViewConfig.DefaultHorizontalMagnification;
            this.view.ActionEvent += View_ActionEvent;
            this.SetToolStripIcons();
            this.toolStripNumericUpdown.NumericUpDownControl.Value = (decimal)globalConfig.ChartViewConfig.PowNum;
            toolStripContainer1.BottomToolStripPanel.SuspendLayout();
            toolStrip1.Dock = DockStyle.None;
            toolStrip2.Dock = DockStyle.None;
            toolStrip1.Location = new Point(0, 0);
            toolStrip2.Location = new Point(300, 0);
            toolStripContainer1.BottomToolStripPanel.ResumeLayout();
        }

        private void View_ActionEvent(object sender, EnergySpectrumActionEventArgs e)
        {
            decimal value = e.NewScaleValue;
            if (value > 10.0M) 
            {
                value = 10.0M;
            } else if (value < 0.1M)
            {
                value = 0.1M;
            }
            if (e.NeedUpdateScale) this.toolStripNumUpDownScale.NumericUpDownControl.Value = value;
        }

        // Token: 0x06000320 RID: 800 RVA: 0x0000F9E4 File Offset: 0x0000DBE4
        public DocEnergySpectrum(string filename) : this()
        {
            this.filename = filename;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            this.Text = fileNameWithoutExtension;
        }

        // Token: 0x06000321 RID: 801 RVA: 0x0000FA10 File Offset: 0x0000DC10
        public ResultData CreateResultData()
        {
            ResultData resultData = new ResultData();
            this.resultDataFile.ResultDataList.Add(resultData);
            resultData.MeasurementController = new MeasurementController(this, resultData);
            this.activeResultDataIndex = this.resultDataFile.ResultDataList.Count - 1;
            if (this.deviceConfigManager.DeviceConfigList.Count > 0)
            {
                resultData.DeviceConfig = this.deviceConfigManager.DeviceConfigList[0];
                resultData.DeviceConfigReference = resultData.DeviceConfig.CreateReference();
                resultData.EnergySpectrum = new EnergySpectrum(resultData.DeviceConfig.ChannelPitch, resultData.DeviceConfig.NumberOfChannels);
                resultData.EnergySpectrum.EnergyCalibration = resultData.DeviceConfig.EnergyCalibration.Clone();
                resultData.PresetTime = resultData.DeviceConfig.DefaultMeasurementTime;
                resultData.ResultDataStatus.PresetTime = resultData.PresetTime;
                resultData.PeakDetectionMethodConfig = resultData.DeviceConfig.PeakDetectionMethodConfig.Clone();
                FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
                FWHMPeakDetectionMethodConfig devcfg = (FWHMPeakDetectionMethodConfig)resultData.DeviceConfig.PeakDetectionMethodConfig;
                if (GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.DefaultPeakMode == PeakMode.Visible)
                {
                    cfg.Enabled = true;
                    devcfg.Enabled = true;
                } else
                {
                    cfg.Enabled = false;
                    devcfg.Enabled = false;
                }
            }
            else
            {
                resultData.EnergySpectrum = new EnergySpectrum(0.04, 2500);
                resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
            }
            if (this.roiConfigManager.ROIConfigList.Count > 0)
            {
                if (resultData.DeviceConfig.EfficencyROIGuid != null &&
                    this.roiConfigManager.ROIConfigMap.ContainsKey(resultData.DeviceConfig.EfficencyROIGuid))
                {
                    resultData.ROIConfig = this.roiConfigManager.ROIConfigMap[resultData.DeviceConfig.EfficencyROIGuid];
                } else
                {
                    resultData.ROIConfig = this.roiConfigManager.ROIConfigList[0];
                }
                resultData.ROIConfigReference = resultData.ROIConfig.CreateReference();
            }
            return resultData;
        }

        // Token: 0x06000322 RID: 802 RVA: 0x0000FB68 File Offset: 0x0000DD68
        public void DeleteActiveResultData()
        {
            if (this.resultDataFile.ResultDataList.Count > 1)
            {
                this.resultDataFile.ResultDataList.RemoveAt(this.activeResultDataIndex);
                if (this.activeResultDataIndex > this.resultDataFile.ResultDataList.Count - 1)
                {
                    this.activeResultDataIndex = this.resultDataFile.ResultDataList.Count - 1;
                }
            }
        }

        // Token: 0x06000323 RID: 803 RVA: 0x0000FBDC File Offset: 0x0000DDDC
        public void UpdateEnergySpectrum()
        {
            this.view.ResultDataList = this.resultDataFile.ResultDataList;
            this.view.ActiveResultDataIndex = this.activeResultDataIndex;
            this.RefreshView();
        }

        // Token: 0x06000324 RID: 804 RVA: 0x0000FC24 File Offset: 0x0000DE24
        public void SetDefaultHorizontalScale()
        {
            this.view.SetDefaultHorizontalScale();
        }

        // Token: 0x06000325 RID: 805 RVA: 0x0000FC34 File Offset: 0x0000DE34
        protected override string GetPersistString()
        {
            return string.Concat(new string[]
            {
                base.GetType().ToString(),
                ",",
                this.Filename,
                ",",
                this.Text
            });
        }

        // Token: 0x06000326 RID: 806 RVA: 0x0000FC98 File Offset: 0x0000DE98
        public void ClearSpectrum()
        {
            ResultData activeResultData = this.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            activeResultData.EnergySpectrum.Initialize();
            activeResultData.PulseCollection.Pulses.Clear();
            activeResultData.CountRates.Clear();
            this.RefreshView();
            resultDataStatus.TotalTime = TimeSpan.FromSeconds(0.0);
        }

        // Token: 0x06000327 RID: 807 RVA: 0x0000FCEC File Offset: 0x0000DEEC
        void 保存SToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.SaveDocument(this, new EventArgs());
        }

        // Token: 0x06000328 RID: 808 RVA: 0x0000FD00 File Offset: 0x0000DF00
        void 閉じるCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.CloseDocument(this, new EventArgs());
        }

        // Token: 0x06000329 RID: 809 RVA: 0x0000FD14 File Offset: 0x0000DF14
        void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            this.保存SToolStripMenuItem.Enabled = this.Dirty;
            this.選択領域からROI定義作成SToolStripMenuItem.Enabled = (this.view.SelectionStart != -1);
            bool enabled = this.view.CursorX != -1 && this.view.CursorChannel > 0 && this.view.CursorChannel < this.ActiveResultData.EnergySpectrum.NumberOfChannels;
            this.下限閾値に設定LToolStripMenuItem.Enabled = enabled;
            this.上限閾値に設定HToolStripMenuItem.Enabled = enabled;
        }

        // Token: 0x0600032A RID: 810 RVA: 0x0000FDB0 File Offset: 0x0000DFB0
        void contextMenuStrip1_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            this.view.CursorX = -1;
            this.view.Invalidate();
        }

        // Token: 0x0600032B RID: 811 RVA: 0x0000FDCC File Offset: 0x0000DFCC
        void DocEnergySpectrum_MouseDown(object sender, MouseEventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x0600032C RID: 812 RVA: 0x0000FDD4 File Offset: 0x0000DFD4
        void ClearSelection()
        {
            this.view.SelectionStart = -1;
            this.view.Invalidate();
        }

        // Token: 0x0600032D RID: 813 RVA: 0x0000FDF0 File Offset: 0x0000DFF0
        void DocEnergySpectrum_SizeChanged(object sender, EventArgs e)
        {
        }

        // Token: 0x0600032E RID: 814 RVA: 0x0000FDF4 File Offset: 0x0000DFF4
        void DocEnergySpectrum_Load(object sender, EventArgs e)
        {
        }

        // Token: 0x0600032F RID: 815 RVA: 0x0000FDF8 File Offset: 0x0000DFF8
        public void RefreshView()
        {
            this.view.PrepareViewData();
            this.view.RecalcScrollBar();
            this.view.Invalidate();
        }

        // Token: 0x06000330 RID: 816 RVA: 0x0000FE1C File Offset: 0x0000E01C
        void SetToolStripIcons()
        {
            Image image = BecquerelMonitor.Properties.Resources.BG;
            switch (this.view.BackgroundMode)
            {
                case BackgroundMode.Visible:
                    image = BecquerelMonitor.Properties.Resources.BG;
                    break;
                case BackgroundMode.Invisible:
                    image = BecquerelMonitor.Properties.Resources.CONT;
                    break;
                case BackgroundMode.Substract:
                    image = BecquerelMonitor.Properties.Resources.SUB;
                    break;
                case BackgroundMode.ShowContinuum:
                    image = BecquerelMonitor.Properties.Resources.CONT;
                    break;
            }
            this.toolStripSplitButton7.Image = image;
            image = BecquerelMonitor.Properties.Resources.HD1;
            switch (this.view.DrawingMode)
            {
                case DrawingMode.HighDefinition:
                    image = BecquerelMonitor.Properties.Resources.HD;
                    break;
                case DrawingMode.Normal:
                    image = BecquerelMonitor.Properties.Resources.HD1;
                    break;
            }
            this.toolStripSplitButton8.Image = image;
            image = BecquerelMonitor.Properties.Resources.cnt;
            switch (this.view.VerticalUnit)
            {
                case VerticalUnit.Counts:
                    image = BecquerelMonitor.Properties.Resources.cnt;
                    break;
                case VerticalUnit.CountsPerSecond:
                    image = BecquerelMonitor.Properties.Resources.cps;
                    break;
            }
            this.toolStripSplitButton1.Image = image;
            image = BecquerelMonitor.Properties.Resources.log;
            switch (this.view.VerticalScaleType)
            {
                case VerticalScaleType.LinearScale:
                    image = BecquerelMonitor.Properties.Resources.linear;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
                case VerticalScaleType.LogarithmicScale:
                    image = BecquerelMonitor.Properties.Resources.log;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
                case VerticalScaleType.PowerScale:
                    image = BecquerelMonitor.Properties.Resources.pow;
                    this.toolStripNumericUpdown.Enabled = true;
                    break;
            }
            this.toolStripSplitButton2.Image = image;
            image = BecquerelMonitor.Properties.Resources.line;
            switch (this.view.ChartType)
            {
                case ChartType.BarChart:
                    image = BecquerelMonitor.Properties.Resources.bar;
                    break;
                case ChartType.LineChart:
                    image = BecquerelMonitor.Properties.Resources.line;
                    break;
            }
            this.toolStripSplitButton3.Image = image;
            image = BecquerelMonitor.Properties.Resources.fit1;
            switch (this.view.VerticalFittingMode)
            {
                case VerticalFittingMode.None:
                    image = BecquerelMonitor.Properties.Resources.None;
                    break;
                case VerticalFittingMode.MinMax:
                    image = BecquerelMonitor.Properties.Resources.fit1;
                    break;
                case VerticalFittingMode.BackgroundMinMax:
                    image = BecquerelMonitor.Properties.Resources.bgfit;
                    break;
            }
            this.toolStripSplitButton5.Image = image;
            image = BecquerelMonitor.Properties.Resources.NoSmooth;
            switch (this.view.SmoothingMethod)
            {
                case SmoothingMethod.None:
                    image = BecquerelMonitor.Properties.Resources.NoSmooth;
                    break;
                case SmoothingMethod.SimpleMovingAverage:
                    image = BecquerelMonitor.Properties.Resources.SMA;
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    image = BecquerelMonitor.Properties.Resources.WMA;
                    break;
            }
            this.toolStripSplitButton6.Image = image;
            image = BecquerelMonitor.Properties.Resources.ene;
            switch (this.view.HorizontalUnit)
            {
                case HorizontalUnit.Channel:
                    image = BecquerelMonitor.Properties.Resources.channel;
                    break;
                case HorizontalUnit.Energy:
                    image = BecquerelMonitor.Properties.Resources.ene;
                    break;
            }
            this.toolStripSplitButton4.Image = image;
            image = BecquerelMonitor.Properties.Resources.peak;
            switch (this.view.PeakMode)
            {
                case PeakMode.Visible:
                    image = BecquerelMonitor.Properties.Resources.peak;
                    break;
                case PeakMode.Invisible:
                    image = BecquerelMonitor.Properties.Resources.nopeak;
                    break;
            }
            this.toolStripSplitButton9.Image = image;
        }

        // Token: 0x06000331 RID: 817 RVA: 0x000100AC File Offset: 0x0000E2AC
        void toolStripSplitButton7_ButtonClick(object sender, EventArgs e)
        {
            BackgroundMode backgroundMode = BackgroundMode.Invisible;
            Image image = BecquerelMonitor.Properties.Resources.NOBG;
            switch (this.view.BackgroundMode)
            {
                case BackgroundMode.Visible:
                    backgroundMode = BackgroundMode.Invisible;
                    image = BecquerelMonitor.Properties.Resources.NOBG;
                    break;
                case BackgroundMode.Invisible:
                    backgroundMode = BackgroundMode.Substract;
                    image = BecquerelMonitor.Properties.Resources.SUB;
                    break;
                case BackgroundMode.Substract:
                    backgroundMode = BackgroundMode.ShowContinuum;
                    image = BecquerelMonitor.Properties.Resources.CONT;
                    break;
                case BackgroundMode.ShowContinuum:
                    backgroundMode = BackgroundMode.Visible;
                    image = BecquerelMonitor.Properties.Resources.BG;
                    break;
            }
            this.view.BackgroundMode = backgroundMode;
            this.toolStripSplitButton7.Image = image;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        // Token: 0x06000332 RID: 818 RVA: 0x00010118 File Offset: 0x0000E318
        void toolStripSplitButton7_DropDownOpening(object sender, EventArgs e)
        {
            this.バックグラウンド表示ありToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.Visible);
            this.バックグラウンド表示なしToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.Invisible);
            this.SubstractBgToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.Substract);
            this.ShowConToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.ShowContinuum);
        }

        // Token: 0x06000333 RID: 819 RVA: 0x0001015C File Offset: 0x0000E35C
        void バックグラウンド表示ありToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.Visible;
            this.toolStripSplitButton7.Image = BecquerelMonitor.Properties.Resources.BG;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        // Token: 0x06000334 RID: 820 RVA: 0x00010180 File Offset: 0x0000E380
        void バックグラウンド表示なしToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.Invisible;
            this.toolStripSplitButton7.Image = BecquerelMonitor.Properties.Resources.NOBG;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        void SubstractBgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.Substract;
            this.toolStripSplitButton7.Image = BecquerelMonitor.Properties.Resources.SUB;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        void ShowConToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.ShowContinuum;
            this.toolStripSplitButton7.Image = BecquerelMonitor.Properties.Resources.CONT;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        // Token: 0x06000335 RID: 821 RVA: 0x000101A4 File Offset: 0x0000E3A4
        void toolStripSplitButton8_ButtonClick(object sender, EventArgs e)
        {
            DrawingMode drawingMode = DrawingMode.Normal;
            Image image = BecquerelMonitor.Properties.Resources.HD1;
            switch (this.view.DrawingMode)
            {
                case DrawingMode.HighDefinition:
                    drawingMode = DrawingMode.Normal;
                    image = BecquerelMonitor.Properties.Resources.HD1;
                    break;
                case DrawingMode.Normal:
                    drawingMode = DrawingMode.HighDefinition;
                    image = BecquerelMonitor.Properties.Resources.HD;
                    break;
            }
            this.view.DrawingMode = drawingMode;
            this.toolStripSplitButton8.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000336 RID: 822 RVA: 0x00010210 File Offset: 0x0000E410
        void toolStripSplitButton8_DropDownOpening(object sender, EventArgs e)
        {
            this.高精細表示ToolStripMenuItem.Checked = (this.view.DrawingMode == DrawingMode.HighDefinition);
            this.通常表示ToolStripMenuItem.Checked = (this.view.DrawingMode == DrawingMode.Normal);
        }

        // Token: 0x06000337 RID: 823 RVA: 0x00010254 File Offset: 0x0000E454
        void 高精細表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.DrawingMode = DrawingMode.HighDefinition;
            this.toolStripSplitButton8.Image = BecquerelMonitor.Properties.Resources.HD;
            this.RefreshView();
        }

        // Token: 0x06000338 RID: 824 RVA: 0x00010278 File Offset: 0x0000E478
        void 通常表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.DrawingMode = DrawingMode.Normal;
            this.toolStripSplitButton8.Image = BecquerelMonitor.Properties.Resources.HD1;
            this.RefreshView();
        }

        // Token: 0x06000339 RID: 825 RVA: 0x0001029C File Offset: 0x0000E49C
        void toolStripSplitButton1_ButtonClick(object sender, EventArgs e)
        {
            VerticalUnit verticalUnit = VerticalUnit.Counts;
            Image image = BecquerelMonitor.Properties.Resources.cnt;
            switch (this.view.VerticalUnit)
            {
                case VerticalUnit.Counts:
                    verticalUnit = VerticalUnit.CountsPerSecond;
                    image = BecquerelMonitor.Properties.Resources.cps;
                    break;
                case VerticalUnit.CountsPerSecond:
                    verticalUnit = VerticalUnit.Counts;
                    image = BecquerelMonitor.Properties.Resources.cnt;
                    break;
            }
            this.view.VerticalUnit = verticalUnit;
            this.toolStripSplitButton1.Image = image;
            this.RefreshView();
        }

        // Token: 0x0600033A RID: 826 RVA: 0x00010308 File Offset: 0x0000E508
        void cps表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalUnit = VerticalUnit.CountsPerSecond;
            this.toolStripSplitButton1.Image = BecquerelMonitor.Properties.Resources.cps;
            this.RefreshView();
        }

        // Token: 0x0600033B RID: 827 RVA: 0x0001032C File Offset: 0x0000E52C
        void カウント表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalUnit = VerticalUnit.Counts;
            this.toolStripSplitButton1.Image = BecquerelMonitor.Properties.Resources.cnt;
            this.RefreshView();
        }

        // Token: 0x0600033C RID: 828 RVA: 0x00010350 File Offset: 0x0000E550
        void toolStripSplitButton1_DropDownOpening(object sender, EventArgs e)
        {
            this.cps表示ToolStripMenuItem.Checked = (this.view.VerticalUnit == VerticalUnit.CountsPerSecond);
            this.カウント表示ToolStripMenuItem.Checked = (this.view.VerticalUnit == VerticalUnit.Counts);
        }

        void toolStripNumericUpdown_ValueChanged(object sender, EventArgs e)
        {
            this.view.PowNum = (double)this.toolStripNumericUpdown.NumericUpDownControl.Value;
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            globalConfig.ChartViewConfig.PowNum = this.view.PowNum;
            GlobalConfigManager.GetInstance().SaveConfigFile();
            this.RefreshView();
        }

        void ToolStripNumericUpdown_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.E)
            {
                this.toolStripNumericUpdown.NumericUpDownControl.Value = (decimal)Math.E;
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x0600033D RID: 829 RVA: 0x00010394 File Offset: 0x0000E594
        void toolStripSplitButton2_ButtonClick(object sender, EventArgs e)
        {
            VerticalScaleType verticalScaleType = VerticalScaleType.LogarithmicScale;
            Image image = BecquerelMonitor.Properties.Resources.log;
            switch (this.view.VerticalScaleType)
            {
                case VerticalScaleType.LinearScale:
                    verticalScaleType = VerticalScaleType.PowerScale;
                    image = BecquerelMonitor.Properties.Resources.pow;
                    this.toolStripNumericUpdown.Enabled = true;
                    break;
                case VerticalScaleType.PowerScale:
                    verticalScaleType = VerticalScaleType.LogarithmicScale;
                    image = BecquerelMonitor.Properties.Resources.log;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
                case VerticalScaleType.LogarithmicScale:
                    verticalScaleType = VerticalScaleType.LinearScale;
                    image = BecquerelMonitor.Properties.Resources.linear;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
            }
            this.view.VerticalScaleType = verticalScaleType;
            this.toolStripSplitButton2.Image = image;
            this.RefreshView();
        }

        // Token: 0x0600033E RID: 830 RVA: 0x00010400 File Offset: 0x0000E600
        void 対数表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalScaleType = VerticalScaleType.LogarithmicScale;
            this.toolStripSplitButton2.Image = BecquerelMonitor.Properties.Resources.log;
            this.toolStripNumericUpdown.Enabled = false;
            this.RefreshView();
        }

        void powToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalScaleType = VerticalScaleType.PowerScale;
            this.toolStripSplitButton2.Image = BecquerelMonitor.Properties.Resources.pow;
            this.toolStripNumericUpdown.Enabled = true;
            this.RefreshView();
        }

        // Token: 0x0600033F RID: 831 RVA: 0x00010424 File Offset: 0x0000E624
        void リニア表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalScaleType = VerticalScaleType.LinearScale;
            this.toolStripSplitButton2.Image = BecquerelMonitor.Properties.Resources.linear;
            this.toolStripNumericUpdown.Enabled = false;
            this.RefreshView();
        }

        // Token: 0x06000340 RID: 832 RVA: 0x00010448 File Offset: 0x0000E648
        void toolStripSplitButton2_DropDownOpening(object sender, EventArgs e)
        {
            this.対数表示ToolStripMenuItem.Checked = (this.view.VerticalScaleType == VerticalScaleType.LogarithmicScale);
            this.リニア表示ToolStripMenuItem.Checked = (this.view.VerticalScaleType == VerticalScaleType.LinearScale);
            this.powToolStripMenuItem.Checked = (this.view.VerticalScaleType == VerticalScaleType.PowerScale);
        }

        // Token: 0x06000341 RID: 833 RVA: 0x0001048C File Offset: 0x0000E68C
        void toolStripSplitButton3_ButtonClick(object sender, EventArgs e)
        {
            ChartType chartType = ChartType.LineChart;
            Image image = BecquerelMonitor.Properties.Resources.line;
            switch (this.view.ChartType)
            {
                case ChartType.BarChart:
                    chartType = ChartType.LineChart;
                    image = BecquerelMonitor.Properties.Resources.line;
                    break;
                case ChartType.LineChart:
                    chartType = ChartType.BarChart;
                    image = BecquerelMonitor.Properties.Resources.bar;
                    break;
            }
            this.view.ChartType = chartType;
            this.toolStripSplitButton3.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000342 RID: 834 RVA: 0x000104F8 File Offset: 0x0000E6F8
        void 折れ線グラフToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.ChartType = ChartType.LineChart;
            this.toolStripSplitButton3.Image = BecquerelMonitor.Properties.Resources.line;
            this.RefreshView();
        }

        // Token: 0x06000343 RID: 835 RVA: 0x0001051C File Offset: 0x0000E71C
        void バ\u30FCグラフToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.ChartType = ChartType.BarChart;
            this.toolStripSplitButton3.Image = BecquerelMonitor.Properties.Resources.bar;
            this.RefreshView();
        }

        // Token: 0x06000344 RID: 836 RVA: 0x00010540 File Offset: 0x0000E740
        void toolStripSplitButton3_DropDownOpening(object sender, EventArgs e)
        {
            this.折れ線グラフToolStripMenuItem.Checked = (this.view.ChartType == ChartType.LineChart);
            this.バ\u30FCグラフToolStripMenuItem.Checked = (this.view.ChartType == ChartType.BarChart);
        }

        // Token: 0x06000345 RID: 837 RVA: 0x00010584 File Offset: 0x0000E784
        void toolStripSplitButton5_ButtonClick(object sender, EventArgs e)
        {
            VerticalFittingMode verticalFittingMode = VerticalFittingMode.MinMax;
            Image image = BecquerelMonitor.Properties.Resources.fit1;
            switch (this.view.VerticalFittingMode)
            {
                case VerticalFittingMode.None:
                    verticalFittingMode = VerticalFittingMode.MinMax;
                    image = BecquerelMonitor.Properties.Resources.fit1;
                    break;
                case VerticalFittingMode.MinMax:
                    verticalFittingMode = VerticalFittingMode.BackgroundMinMax;
                    image = BecquerelMonitor.Properties.Resources.bgfit;
                    break;
                case VerticalFittingMode.BackgroundMinMax:
                    verticalFittingMode = VerticalFittingMode.None;
                    image = BecquerelMonitor.Properties.Resources.None;
                    break;
            }
            this.view.VerticalFittingMode = verticalFittingMode;
            this.toolStripSplitButton5.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000346 RID: 838 RVA: 0x00010600 File Offset: 0x0000E800
        void 自動フィットToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalFittingMode = VerticalFittingMode.MinMax;
            this.toolStripSplitButton5.Image = BecquerelMonitor.Properties.Resources.fit1;
            this.RefreshView();
        }

        // Token: 0x06000347 RID: 839 RVA: 0x00010624 File Offset: 0x0000E824
        void 自動フィットBGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalFittingMode = VerticalFittingMode.BackgroundMinMax;
            this.toolStripSplitButton5.Image = BecquerelMonitor.Properties.Resources.bgfit;
            this.RefreshView();
        }

        // Token: 0x06000348 RID: 840 RVA: 0x00010648 File Offset: 0x0000E848
        void なしToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalFittingMode = VerticalFittingMode.None;
            this.toolStripSplitButton5.Image = BecquerelMonitor.Properties.Resources.None;
            this.view.Invalidate();
        }

        // Token: 0x06000349 RID: 841 RVA: 0x00010674 File Offset: 0x0000E874
        void toolStripSplitButton5_DropDownOpening(object sender, EventArgs e)
        {
            this.自動フィットToolStripMenuItem.Checked = (this.view.VerticalFittingMode == VerticalFittingMode.MinMax);
            this.自動フィットBGToolStripMenuItem.Checked = (this.view.VerticalFittingMode == VerticalFittingMode.BackgroundMinMax);
            this.なしToolStripMenuItem.Checked = (this.view.VerticalFittingMode == VerticalFittingMode.None);
        }

        // Token: 0x0600034A RID: 842 RVA: 0x000106D0 File Offset: 0x0000E8D0
        void toolStripSplitButton6_ButtonClick(object sender, EventArgs e)
        {
            SmoothingMethod smoothingMethod = SmoothingMethod.None;
            Image image = BecquerelMonitor.Properties.Resources.NoSmooth;
            switch (this.view.SmoothingMethod)
            {
                case SmoothingMethod.None:
                    smoothingMethod = SmoothingMethod.SimpleMovingAverage;
                    image = BecquerelMonitor.Properties.Resources.SMA;
                    break;
                case SmoothingMethod.SimpleMovingAverage:
                    smoothingMethod = SmoothingMethod.WeightedMovingAverage;
                    image = BecquerelMonitor.Properties.Resources.WMA;
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    smoothingMethod = SmoothingMethod.None;
                    image = BecquerelMonitor.Properties.Resources.NoSmooth;
                    break;
            }
            this.view.SmoothingMethod = smoothingMethod;
            this.toolStripSplitButton6.Image = image;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034B RID: 843 RVA: 0x0001074C File Offset: 0x0000E94C
        void スム\u30FCジングなしToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.SmoothingMethod = SmoothingMethod.None;
            this.toolStripSplitButton6.Image = BecquerelMonitor.Properties.Resources.NoSmooth;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034C RID: 844 RVA: 0x00010770 File Offset: 0x0000E970
        void 単純移動平均ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.SmoothingMethod = SmoothingMethod.SimpleMovingAverage;
            this.toolStripSplitButton6.Image = BecquerelMonitor.Properties.Resources.SMA;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034D RID: 845 RVA: 0x00010794 File Offset: 0x0000E994
        void 加重移動平均ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.SmoothingMethod = SmoothingMethod.WeightedMovingAverage;
            this.toolStripSplitButton6.Image = BecquerelMonitor.Properties.Resources.WMA;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034E RID: 846 RVA: 0x000107B8 File Offset: 0x0000E9B8
        void toolStripSplitButton6_DropDownOpening(object sender, EventArgs e)
        {
            this.スム\u30FCジングなしToolStripMenuItem.Checked = (this.view.SmoothingMethod == SmoothingMethod.None);
            this.単純移動平均ToolStripMenuItem.Checked = (this.view.SmoothingMethod == SmoothingMethod.SimpleMovingAverage);
            this.加重移動平均ToolStripMenuItem.Checked = (this.view.SmoothingMethod == SmoothingMethod.WeightedMovingAverage);
        }

        // Token: 0x0600034F RID: 847 RVA: 0x00010814 File Offset: 0x0000EA14
        void toolStripSplitButton4_ButtonClick(object sender, EventArgs e)
        {
            HorizontalUnit horizontalUnit = HorizontalUnit.Energy;
            Image image = BecquerelMonitor.Properties.Resources.ene;
            switch (this.view.HorizontalUnit)
            {
                case HorizontalUnit.Channel:
                    horizontalUnit = HorizontalUnit.Energy;
                    image = BecquerelMonitor.Properties.Resources.ene;
                    break;
                case HorizontalUnit.Energy:
                    horizontalUnit = HorizontalUnit.Channel;
                    image = BecquerelMonitor.Properties.Resources.channel;
                    break;
            }
            this.view.HorizontalUnit = horizontalUnit;
            this.toolStripSplitButton4.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000350 RID: 848 RVA: 0x00010880 File Offset: 0x0000EA80
        void エネルギ\u30FC表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.HorizontalUnit = HorizontalUnit.Energy;
            this.toolStripSplitButton4.Image = BecquerelMonitor.Properties.Resources.ene;
            this.RefreshView();
        }

        // Token: 0x06000351 RID: 849 RVA: 0x000108A4 File Offset: 0x0000EAA4
        void チャネル表示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.HorizontalUnit = HorizontalUnit.Channel;
            this.toolStripSplitButton4.Image = BecquerelMonitor.Properties.Resources.channel;
            this.RefreshView();
        }

        // Token: 0x06000352 RID: 850 RVA: 0x000108C8 File Offset: 0x0000EAC8
        void toolStripSplitButton4_DropDownOpening(object sender, EventArgs e)
        {
            this.エネルギ\u30FC表示ToolStripMenuItem.Checked = (this.view.HorizontalUnit == HorizontalUnit.Energy);
            this.チャネル表示ToolStripMenuItem.Checked = (this.view.HorizontalUnit == HorizontalUnit.Channel);
        }

        // Token: 0x06000353 RID: 851 RVA: 0x0001090C File Offset: 0x0000EB0C
        void toolStripSplitButton9_ButtonClick(object sender, EventArgs e)
        {
            PeakMode peakMode = PeakMode.Invisible;
            Image image = BecquerelMonitor.Properties.Resources.nopeak;
            switch (this.view.PeakMode)
            {
                case PeakMode.Visible:
                    peakMode = PeakMode.Invisible;
                    image = BecquerelMonitor.Properties.Resources.nopeak;
                    FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)this.ActiveResultData.PeakDetectionMethodConfig;
                    FWHMPeakDetectionMethodConfig devcfg = (FWHMPeakDetectionMethodConfig)this.ActiveResultData.DeviceConfig.PeakDetectionMethodConfig;
                    cfg.Enabled = false;
                    devcfg.Enabled = false;
                    this.UpdateDetectedPeaks = false;
                    break;
                case PeakMode.Invisible:
                    peakMode = PeakMode.Visible;
                    image = BecquerelMonitor.Properties.Resources.peak;
                    FWHMPeakDetectionMethodConfig peakConfig = (FWHMPeakDetectionMethodConfig)this.ActiveResultData.PeakDetectionMethodConfig;
                    FWHMPeakDetectionMethodConfig devConfig = (FWHMPeakDetectionMethodConfig)this.ActiveResultData.DeviceConfig.PeakDetectionMethodConfig;
                    peakConfig.Enabled = true;
                    devConfig.Enabled = true;
                    this.UpdateDetectedPeaks = true;
                    break;
            }
            this.view.PeakMode = peakMode;
            this.toolStripSplitButton9.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000354 RID: 852 RVA: 0x00010978 File Offset: 0x0000EB78
        void toolStripSplitButton9_DropDownOpening(object sender, EventArgs e)
        {
            this.ピ\u30FCク表示ありToolStripMenuItem.Checked = (this.view.PeakMode == PeakMode.Visible);
            this.ピ\u30FCク表示なしToolStripMenuItem.Checked = (this.view.PeakMode == PeakMode.Invisible);
        }

        // Token: 0x06000355 RID: 853 RVA: 0x000109BC File Offset: 0x0000EBBC
        void ピ\u30FCク表示ありToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.PeakMode = PeakMode.Visible;
            this.toolStripSplitButton9.Image = BecquerelMonitor.Properties.Resources.peak;
            FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)this.ActiveResultData.PeakDetectionMethodConfig;
            cfg.Enabled = true;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x06000356 RID: 854 RVA: 0x000109E0 File Offset: 0x0000EBE0
        void ピ\u30FCク表示なしToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.PeakMode = PeakMode.Invisible;
            this.toolStripSplitButton9.Image = BecquerelMonitor.Properties.Resources.nopeak;
            FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)this.ActiveResultData.PeakDetectionMethodConfig;
            cfg.Enabled = false;
            this.UpdateDetectedPeaks = false;
            this.RefreshView();
        }

        // Token: 0x06000357 RID: 855 RVA: 0x00010A04 File Offset: 0x0000EC04
        void toolStripSplitButton10_ButtonClick(object sender, EventArgs e)
        {
            if (this.activeEnergyCalibration)
            {
                if (!this.isActivating)
                {
                    this.ActiveEnergyCalibration = false;
                    this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
                }
            }
            else
            {
                this.ActiveEnergyCalibration = true;
                this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
            }
            this.isActivating = false;
        }

        // Token: 0x06000358 RID: 856 RVA: 0x00010A74 File Offset: 0x0000EC74
        void toolStripSplitButton10_DropDownOpening(object sender, EventArgs e)
        {
            this.showEnergyCalibrationToolToolStripMenuItem.Checked = this.activeEnergyCalibration;
            this.hideEnergyCalibrationToolToolStripMenuItem.Checked = !this.activeEnergyCalibration;
        }

        // Token: 0x06000359 RID: 857 RVA: 0x00010A9C File Offset: 0x0000EC9C
        void showEnergyCalibrationToolToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ActiveEnergyCalibration = true;
            this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
        }

        // Token: 0x0600035A RID: 858 RVA: 0x00010ABC File Offset: 0x0000ECBC
        void hideEnergyCalibrationToolToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ActiveEnergyCalibration = false;
            this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
        }

        // Token: 0x0600035F RID: 863 RVA: 0x00010BAC File Offset: 0x0000EDAC
        void toolStripContainer1_BottomToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000360 RID: 864 RVA: 0x00010BB4 File Offset: 0x0000EDB4
        void toolStripContainer1_LeftToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000361 RID: 865 RVA: 0x00010BBC File Offset: 0x0000EDBC
        void toolStripContainer1_TopToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000362 RID: 866 RVA: 0x00010BC4 File Offset: 0x0000EDC4
        void toolStripContainer1_RightToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000363 RID: 867 RVA: 0x00010BCC File Offset: 0x0000EDCC
        void toolStripEnergyCalibrationButton1_EnergyCalibrationChanged(object sender, EnergyCalibrationChangedEventArgs e)
        {
            ResultData activeResultData = this.ActiveResultData;
            activeResultData.EnergySpectrum.EnergyCalibration = e.EnergyCalibration;
            this.RefreshView();
            this.updateMeasurementResult = true;
            this.dirty = true;
        }

        // Token: 0x06000364 RID: 868 RVA: 0x00010C0C File Offset: 0x0000EE0C
        void toolStripButton1_Click(object sender, EventArgs e)
        {
            this.energySpectrumView1.FitHorizontalScale();
        }

        void toolStripButton3_Click(object sender, EventArgs e)
        {
            this.energySpectrumView1.SetScale11();
        }

        // Token: 0x06000365 RID: 869 RVA: 0x00010C1C File Offset: 0x0000EE1C
        void 全チャネルを表示AToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.energySpectrumView1.FitHorizontalScale();
        }

        // Token: 0x06000366 RID: 870 RVA: 0x00010C2C File Offset: 0x0000EE2C
        void toolStripButton2_Click(object sender, EventArgs e)
        {
            this.energySpectrumView1.ZoominSelectedRegion();
        }

        // Token: 0x06000367 RID: 871 RVA: 0x00010C3C File Offset: 0x0000EE3C
        void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.energySpectrumView1.ZoominSelectedRegion();
        }

        // Token: 0x06000368 RID: 872 RVA: 0x00010C4C File Offset: 0x0000EE4C
        void 選択領域からROI定義作成SToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.CreateNewROI != null)
            {
                this.CreateNewROI(this, new EventArgs());
            }
        }

        // Token: 0x06000369 RID: 873 RVA: 0x00010C6C File Offset: 0x0000EE6C
        void 下限閾値に設定LToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.SetLowerThreshold != null)
            {
                this.SetLowerThreshold(this, new EventArgs());
            }
        }

        void toolStripScreenShotButton_Click(object sender, EventArgs e)
        {
            this.view.takeScreenshot();
        }

        void toolStripNumUpDownScale_ValueChanged(object sender, EventArgs e)
        {
            this.view.zoom(this.toolStripNumUpDownScale.NumericUpDownControl.Value);
        }

        void toolStripNumUpDownScale_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.view.zoom(this.toolStripNumUpDownScale.NumericUpDownControl.Value);
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x0600036A RID: 874 RVA: 0x00010C8C File Offset: 0x0000EE8C
        void 上限閾値に設定HToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.SetUpperThreshold != null)
            {
                this.SetUpperThreshold(this, new EventArgs());
            }
        }

        // Token: 0x0600036B RID: 875 RVA: 0x00010CAC File Offset: 0x0000EEAC
        void DocEnergySpectrum_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] array = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string path in array)
                {
                    if (Path.GetExtension(path) != ".xml")
                    {
                        return;
                    }
                    if (!File.Exists(path))
                    {
                        return;
                    }
                }
                e.Effect = DragDropEffects.Copy;
            }
        }

        // Token: 0x0600036C RID: 876 RVA: 0x00010D34 File Offset: 0x0000EF34
        void DocEnergySpectrum_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] pathnames = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (this.AddSpectrumToDocument != null)
                {
                    this.AddSpectrumToDocument(this, new AddSpectrumToDocumentEventArgs(pathnames));
                    this.dirty = true;
                }
            }
        }

        // Token: 0x0600036D RID: 877 RVA: 0x00010D94 File Offset: 0x0000EF94
        void DocEnergySpectrum_Activated(object sender, EventArgs e)
        {
        }

        // Token: 0x0600036E RID: 878 RVA: 0x00010D98 File Offset: 0x0000EF98
        void DocEnergySpectrum_Click(object sender, EventArgs e)
        {
        }

        // Token: 0x0600036F RID: 879 RVA: 0x00010D9C File Offset: 0x0000EF9C
        void DocEnergySpectrum_MouseUp(object sender, MouseEventArgs e)
        {
        }

        // Token: 0x04000149 RID: 329
        DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();

        // Token: 0x0400014A RID: 330
        ROIConfigManager roiConfigManager = ROIConfigManager.GetInstance();

        // Token: 0x0400014B RID: 331
        ResultDataFile resultDataFile;

        // Token: 0x0400014C RID: 332
        int activeResultDataIndex;

        // Token: 0x0400014D RID: 333
        string filename;

        // Token: 0x0400014E RID: 334
        bool isNamed = true;

        // Token: 0x0400014F RID: 335
        bool dirty;

        // Token: 0x04000150 RID: 336
        EnergySpectrumView view;

        // Token: 0x04000151 RID: 337
        bool updateMeasurementResult;

        // Token: 0x04000152 RID: 338
        bool updateSpectrum;

        bool updateDetectedPeaks = false;

        bool updateDoseRate = false;

        // Token: 0x04000153 RID: 339
        bool activeEnergyCalibration;

        // Token: 0x04000154 RID: 340
        bool isActivating;

        bool autosave = false;
    }
}
