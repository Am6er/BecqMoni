using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;

namespace BecquerelMonitor
{
    // Token: 0x02000060 RID: 96
    public class EnergySpectrumView : UserControl
    {
        // Token: 0x06000485 RID: 1157 RVA: 0x00015C24 File Offset: 0x00013E24
        protected override void Dispose(bool disposing)
        {
            if (disposing && this.components != null)
            {
                this.components.Dispose();
            }
            base.Dispose(disposing);
        }

        // Token: 0x06000486 RID: 1158 RVA: 0x00015C4C File Offset: 0x00013E4C
        void InitializeComponent()
        {
            this.components = new Container();
            ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(EnergySpectrumView));
            this.hScrollBar1 = new HScrollBar();
            this.vScrollBar1 = new VScrollBar();
            this.panel2 = new Panel();
            this.button1 = new RepeatButton();
            this.button2 = new RepeatButton();
            this.toolTip1 = new ToolTip(this.components);
            this.panel2.SuspendLayout();
            base.SuspendLayout();
            componentResourceManager.ApplyResources(this.hScrollBar1, "hScrollBar1");
            this.hScrollBar1.Name = "hScrollBar1";
            this.toolTip1.SetToolTip(this.hScrollBar1, componentResourceManager.GetString("hScrollBar1.ToolTip"));
            this.hScrollBar1.ValueChanged += this.hScrollBar1_ValueChanged;
            componentResourceManager.ApplyResources(this.vScrollBar1, "vScrollBar1");
            this.vScrollBar1.Name = "vScrollBar1";
            this.toolTip1.SetToolTip(this.vScrollBar1, componentResourceManager.GetString("vScrollBar1.ToolTip"));
            this.vScrollBar1.ValueChanged += this.vScrollBar1_ValueChanged;
            componentResourceManager.ApplyResources(this.panel2, "panel2");
            this.panel2.BackColor = SystemColors.Control;
            this.panel2.Controls.Add(this.button1);
            this.panel2.Controls.Add(this.button2);
            this.panel2.Name = "panel2";
            this.toolTip1.SetToolTip(this.panel2, componentResourceManager.GetString("panel2.ToolTip"));
            componentResourceManager.ApplyResources(this.button1, "button1");
            this.button1.Image = Resources.Zoomin;
            this.button1.Name = "button1";
            this.button1.TabStop = false;
            this.toolTip1.SetToolTip(this.button1, componentResourceManager.GetString("button1.ToolTip"));
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += this.button1_Click;
            componentResourceManager.ApplyResources(this.button2, "button2");
            this.button2.Image = Resources.Zoomout;
            this.button2.Name = "button2";
            this.button2.TabStop = false;
            this.toolTip1.SetToolTip(this.button2, componentResourceManager.GetString("button2.ToolTip"));
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += this.button2_Click;
            componentResourceManager.ApplyResources(this, "$this");
            base.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.White;
            base.Controls.Add(this.panel2);
            base.Controls.Add(this.vScrollBar1);
            base.Controls.Add(this.hScrollBar1);
            this.DoubleBuffered = true;
            base.Name = "EnergySpectrumView";
            this.toolTip1.SetToolTip(this, componentResourceManager.GetString("$this.ToolTip"));
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            base.SizeChanged += this.EnergySpectrumView_SizeChanged;
            base.MouseDown += this.EnergySpectrumView_MouseDown;
            base.MouseLeave += this.EnergySpectrumView_MouseLeave;
            base.MouseMove += this.EnergySpectrumView_MouseMove;
            base.MouseUp += this.EnergySpectrumView_MouseUp;
            this.panel2.ResumeLayout(false);
            base.ResumeLayout(false);
        }

        // Token: 0x1700017F RID: 383
        // (get) Token: 0x06000487 RID: 1159 RVA: 0x0001628C File Offset: 0x0001448C
        // (set) Token: 0x06000488 RID: 1160 RVA: 0x00016294 File Offset: 0x00014494
        public List<ResultData> ResultDataList
        {
            get
            {
                return this.resultDataList;
            }
            set
            {
                this.resultDataList = value;
            }
        }

        // Token: 0x17000180 RID: 384
        // (get) Token: 0x06000489 RID: 1161 RVA: 0x000162A0 File Offset: 0x000144A0
        // (set) Token: 0x0600048A RID: 1162 RVA: 0x000162A8 File Offset: 0x000144A8
        public int ActiveResultDataIndex
        {
            get
            {
                return this.activeResultDataIndex;
            }
            set
            {
                this.activeResultDataIndex = value;
                if (this.resultDataList != null)
                {
                    this.activeResultData = this.resultDataList[this.activeResultDataIndex];
                }
            }
        }

        // Token: 0x17000181 RID: 385
        // (get) Token: 0x0600048B RID: 1163 RVA: 0x000162D4 File Offset: 0x000144D4
        // (set) Token: 0x0600048C RID: 1164 RVA: 0x000162DC File Offset: 0x000144DC
        public int SelectionStart
        {
            get
            {
                return this.selectionStart;
            }
            set
            {
                this.selectionStart = value;
            }
        }

        // Token: 0x17000182 RID: 386
        // (get) Token: 0x0600048D RID: 1165 RVA: 0x000162E8 File Offset: 0x000144E8
        // (set) Token: 0x0600048E RID: 1166 RVA: 0x000162F0 File Offset: 0x000144F0
        public int SelectionEnd
        {
            get
            {
                return this.selectionEnd;
            }
            set
            {
                this.selectionEnd = value;
            }
        }

        // Token: 0x17000183 RID: 387
        // (get) Token: 0x0600048F RID: 1167 RVA: 0x000162FC File Offset: 0x000144FC
        // (set) Token: 0x06000490 RID: 1168 RVA: 0x00016304 File Offset: 0x00014504
        public int CursorX
        {
            get
            {
                return this.cursorX;
            }
            set
            {
                this.cursorX = value;
            }
        }

        // Token: 0x17000184 RID: 388
        // (get) Token: 0x06000491 RID: 1169 RVA: 0x00016310 File Offset: 0x00014510
        // (set) Token: 0x06000492 RID: 1170 RVA: 0x00016318 File Offset: 0x00014518
        public int CursorChannel
        {
            get
            {
                return this.cursorChannel;
            }
            set
            {
                this.cursorChannel = value;
            }
        }

        // Token: 0x14000012 RID: 18
        // (add) Token: 0x06000493 RID: 1171 RVA: 0x00016324 File Offset: 0x00014524
        // (remove) Token: 0x06000494 RID: 1172 RVA: 0x00016360 File Offset: 0x00014560
        public event EnergySpectrumView.ChannelPickupedEventHandler ChannelPickuped;

        // Token: 0x17000185 RID: 389
        // (get) Token: 0x06000495 RID: 1173 RVA: 0x0001639C File Offset: 0x0001459C
        // (set) Token: 0x06000496 RID: 1174 RVA: 0x000163A4 File Offset: 0x000145A4
        public BackgroundMode BackgroundMode
        {
            get
            {
                return this.backgroundMode;
            }
            set
            {
                this.backgroundMode = value;
            }
        }

        // Token: 0x17000186 RID: 390
        // (get) Token: 0x06000497 RID: 1175 RVA: 0x000163B0 File Offset: 0x000145B0
        // (set) Token: 0x06000498 RID: 1176 RVA: 0x000163B8 File Offset: 0x000145B8
        public DrawingMode DrawingMode
        {
            get
            {
                return this.drawingMode;
            }
            set
            {
                this.drawingMode = value;
            }
        }

        // Token: 0x17000187 RID: 391
        // (get) Token: 0x06000499 RID: 1177 RVA: 0x000163C4 File Offset: 0x000145C4
        // (set) Token: 0x0600049A RID: 1178 RVA: 0x000163CC File Offset: 0x000145CC
        public HorizontalUnit HorizontalUnit
        {
            get
            {
                return this.horizontalUnit;
            }
            set
            {
                this.horizontalUnit = value;
            }
        }

        // Token: 0x17000188 RID: 392
        // (get) Token: 0x0600049B RID: 1179 RVA: 0x000163D8 File Offset: 0x000145D8
        // (set) Token: 0x0600049C RID: 1180 RVA: 0x000163E0 File Offset: 0x000145E0
        public VerticalUnit VerticalUnit
        {
            get
            {
                return this.verticalUnit;
            }
            set
            {
                this.verticalUnit = value;
            }
        }

        // Token: 0x17000189 RID: 393
        // (get) Token: 0x0600049D RID: 1181 RVA: 0x000163EC File Offset: 0x000145EC
        // (set) Token: 0x0600049E RID: 1182 RVA: 0x000163F4 File Offset: 0x000145F4
        public ChartType ChartType
        {
            get
            {
                return this.chartType;
            }
            set
            {
                this.chartType = value;
            }
        }

        // Token: 0x1700018A RID: 394
        // (get) Token: 0x0600049F RID: 1183 RVA: 0x00016400 File Offset: 0x00014600
        // (set) Token: 0x060004A0 RID: 1184 RVA: 0x00016408 File Offset: 0x00014608
        public VerticalScaleType VerticalScaleType
        {
            get
            {
                return this.verticalScaleType;
            }
            set
            {
                this.verticalScaleType = value;
            }
        }

        // Token: 0x1700018B RID: 395
        // (get) Token: 0x060004A1 RID: 1185 RVA: 0x00016414 File Offset: 0x00014614
        // (set) Token: 0x060004A2 RID: 1186 RVA: 0x0001641C File Offset: 0x0001461C
        public VerticalFittingMode VerticalFittingMode
        {
            get
            {
                return this.fittingMode;
            }
            set
            {
                this.fittingMode = value;
                this.PrepareViewData();
                this.RecalcScrollBar();
            }
        }

        // Token: 0x1700018C RID: 396
        // (get) Token: 0x060004A3 RID: 1187 RVA: 0x00016434 File Offset: 0x00014634
        // (set) Token: 0x060004A4 RID: 1188 RVA: 0x0001643C File Offset: 0x0001463C
        public SmoothingMethod SmoothingMethod
        {
            get
            {
                return this.smoothingMethod;
            }
            set
            {
                this.smoothingMethod = value;
            }
        }

        // Token: 0x1700018D RID: 397
        // (get) Token: 0x060004A5 RID: 1189 RVA: 0x00016448 File Offset: 0x00014648
        // (set) Token: 0x060004A6 RID: 1190 RVA: 0x00016450 File Offset: 0x00014650
        public PeakMode PeakMode
        {
            get
            {
                return this.peakMode;
            }
            set
            {
                this.peakMode = value;
            }
        }

        // Token: 0x1700018E RID: 398
        // (get) Token: 0x060004A7 RID: 1191 RVA: 0x0001645C File Offset: 0x0001465C
        // (set) Token: 0x060004A8 RID: 1192 RVA: 0x00016464 File Offset: 0x00014664
        public double HorizontalScale
        {
            get
            {
                return this.horizontalScale;
            }
            set
            {
                this.horizontalScale = value;
                if (this.resultDataList != null)
                {
                    this.RecalcScrollBar();
                }
            }
        }

        public HorizontalMagnification HorizontalMagnification
        {
            get
            {
                return this.horizontalMagnification;
            }
            set
            {
                this.horizontalMagnification = value;
            }
        }

        // Token: 0x1700018F RID: 399
        // (get) Token: 0x060004A9 RID: 1193 RVA: 0x00016480 File Offset: 0x00014680
        // (set) Token: 0x060004AA RID: 1194 RVA: 0x00016488 File Offset: 0x00014688
        public double VerticalScale
        {
            get
            {
                return this.verticalScale;
            }
            set
            {
                this.verticalScale = value;
                if (this.resultDataList != null)
                {
                    this.RecalcScrollBar();
                }
            }
        }

        // Token: 0x060004AB RID: 1195 RVA: 0x000164A4 File Offset: 0x000146A4
        static EnergySpectrumView()
        {
            try
            {
                EnergySpectrumView.logoImage = Image.FromFile("config\\logo.png");
            }
            catch (Exception)
            {
                EnergySpectrumView.logoImage = null;
            }
        }

        // Token: 0x060004AC RID: 1196 RVA: 0x000164E4 File Offset: 0x000146E4
        public EnergySpectrumView()
        {
            this.InitializeComponent();
            this.farFormat = new StringFormat();
            this.farFormat.Alignment = StringAlignment.Far;
            this.centerFormat = new StringFormat();
            this.centerFormat.Alignment = StringAlignment.Center;
            ((Bitmap)this.button1.Image).MakeTransparent();
            ((Bitmap)this.button2.Image).MakeTransparent();
            this.hScrollBar1.Visible = true;
            this.vScrollBar1.Visible = true;
        }

        // Token: 0x060004AD RID: 1197 RVA: 0x00016630 File Offset: 0x00014830
        int CalcMaximumXValue()
        {
            return CalcXValue(this.numberOfChannels);
        }

        int CalcXValue(int channel)
        {
            if (this.dirty)
            {
                RecalcChartParameters();
                this.dirty = false;
            }
            return (int)((this.energyCalibration.ChannelToEnergy((double)channel) - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale);
        }

        int CalcChanValue(int scrollBarPos)
        {
            if (this.dirty)
            {
                RecalcChartParameters();
                this.dirty = false;
            }
            return (int)(this.energyCalibration.EnergyToChannel((double)(scrollBarPos) / this.pixelPerEnergy / this.horizontalScale + this.energyViewOffset, maxChannels: this.energySpectrum.NumberOfChannels));
        }

        // Token: 0x060004AE RID: 1198 RVA: 0x0001666C File Offset: 0x0001486C
        public void RecalcScrollBar()
        {
            if (this.resultDataList == null)
            {
                this.hScrollBar1.Enabled = false;
                this.hScrollBar1.Value = 0;
                this.vScrollBar1.Enabled = false;
                this.vScrollBar1.Value = 0;
                return;
            }
            this.totalMaxChannel = 0;
            foreach (ResultData resultData in this.resultDataList)
            {
                int num = resultData.EnergySpectrum.NumberOfChannels;
                if (num > this.totalMaxChannel)
                {
                    this.totalMaxChannel = num;
                }
            }
            int num2 = this.CalcMaximumXValue() + 5;
            int num3 = base.Height - this.bottom - this.hScrollBar1.Height;
            int num4 = base.Width - this.left - this.vScrollBar1.Width;
            if (num2 - num4 > 0)
            {
                this.hScrollBar1.Enabled = true;
                Size size = this.hScrollBar1.Size;
                this.hScrollBar1.Minimum = 0;
                this.hScrollBar1.Maximum = num2;
                if (num4 < 0)
                {
                    this.hScrollBar1.LargeChange = 0;
                }
                else
                {
                    this.hScrollBar1.LargeChange = num4;
                }
                this.hScrollBar1.SmallChange = 100;
                if (this.hScrollBar1.Value > num2 - num4)
                {
                    this.hScrollBar1.Value = num2 - num4;
                }
                if (this.hScrollBar1.Value < this.hScrollBar1.Minimum)
                {
                    this.hScrollBar1.Value = this.hScrollBar1.Minimum;
                }
            }
            else
            {
                this.hScrollBar1.Enabled = false;
                this.hScrollBar1.Maximum = 0;
                this.hScrollBar1.Minimum = 0;
                this.hScrollBar1.Value = 0;
            }
            int num5 = (int)((double)num3 * this.verticalScale);
            if (num5 - num3 > 0)
            {
                this.vScrollBar1.Enabled = true;
                this.vScrollBar1.Minimum = 0;
                this.vScrollBar1.Maximum = num5;
                if (num3 < 0)
                {
                    this.vScrollBar1.LargeChange = 0;
                }
                else
                {
                    this.vScrollBar1.LargeChange = num3;
                }
                this.vScrollBar1.SmallChange = 100;
                if (this.scrollY > num5 - num3)
                {
                    this.scrollY = num5 - num3;
                }
                if (this.scrollY < this.vScrollBar1.Minimum)
                {
                    this.scrollY = this.vScrollBar1.Minimum;
                }
                this.vScrollBar1.Value = this.scrollY;
                return;
            }
            this.vScrollBar1.Enabled = false;
            this.vScrollBar1.Maximum = 0;
            this.vScrollBar1.Minimum = 0;
            this.vScrollBar1.Value = 0;
        }

        // Token: 0x17000190 RID: 400
        // (get) Token: 0x060004AF RID: 1199 RVA: 0x0001694C File Offset: 0x00014B4C
        public ScrollBar ScrollBar
        {
            get
            {
                return this.vScrollBar1;
            }
        }

        // Token: 0x060004B0 RID: 1200 RVA: 0x00016954 File Offset: 0x00014B54
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!this.hScrollBar1.Visible)
            {
                return;
            }
            if (this.CtrlKeyPressed)
            {
                // Change Horizontal Scale
                double maxScale = (double)(base.Width - this.left - this.vScrollBar1.Width) / (this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels) * this.pixelPerEnergy + 8.0);
                double newHorizontalScale = this.horizontalScale;
                if (e.Delta < 0)
                {
                    newHorizontalScale = Math.Max(maxScale, 0.9 * newHorizontalScale);
                } else
                {
                    newHorizontalScale = Math.Min(10, 1.1 * newHorizontalScale);
                }
                int screenXOffset = base.Width - this.left - this.vScrollBar1.Width - 5;
                int medCh = CalcChanValue(this.hScrollBar1.Value + screenXOffset / 2);
                this.horizontalScale = newHorizontalScale;
                if (ActionEvent != null) ActionEvent(this, new EnergySpectrumActionEventArgs(true, (decimal)this.horizontalScale));
                this.hScrollBar1.Maximum = this.CalcMaximumXValue() + 5;
                int newValue = CalcXValue(medCh) - screenXOffset / 2;
                if (newValue < 0) { newValue = 0; }
                if (newValue > this.hScrollBar1.Maximum) {  newValue = this.hScrollBar1.Maximum; }
                this.hScrollBar1.Value = newValue;

                this.PrepareViewData();
                this.RecalcScrollBar();
                base.Invalidate();
            } else
            {
                // Horizontal scroll
                int num = e.Delta * SystemInformation.MouseWheelScrollLines / 120;
                int num2 = this.hScrollBar1.Value;
                num2 -= num * 15;
                if (num2 < this.hScrollBar1.Minimum)
                {
                    num2 = this.hScrollBar1.Minimum;
                }
                int maximum = this.hScrollBar1.Maximum;
                if (num2 > maximum)
                {
                    num2 = maximum;
                }
                this.hScrollBar1.Value = num2;
                base.Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A)
            {
                FitHorizontalScale();
                return;
            }
            if (e.KeyCode == Keys.M)
            {
                ZoominSelectedRegion();
                return;
            }
            if (e.KeyCode == Keys.ControlKey)
            {
                this.CtrlKeyPressed = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey)
            {
                this.CtrlKeyPressed = false;
                return;
            }
            base.OnKeyUp(e);
        }

        // Token: 0x060004B1 RID: 1201 RVA: 0x000169E4 File Offset: 0x00014BE4
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            if (this.resultDataList == null)
            {
                return;
            }
            using (Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color))
            {
                graphics.FillRectangle(brush, this.left, 0, this.width, this.height);
            }
        }

        // Token: 0x060004B2 RID: 1202 RVA: 0x00016A60 File Offset: 0x00014C60
        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics graphics = pe.Graphics;
            if (this.resultDataList == null)
            {
                return;
            }
            this.DrawChart(graphics);
        }

        // Token: 0x060004B3 RID: 1203 RVA: 0x00016A8C File Offset: 0x00014C8C
        public void PrepareViewData()
        {
            if (this.resultDataList == null)
            {
                return;
            }
            this.energySpectrum = this.activeResultData.EnergySpectrum;
            this.backgroundEnergySpectrum = this.activeResultData.BackgroundEnergySpectrum;
            this.roiConfig = this.activeResultData.ROIConfig;
            if (this.numberOfChannels > 0 && this.numberOfChannels != this.energySpectrum.NumberOfChannels)
            {
                this.dirty = true;
            }
            this.numberOfChannels = this.energySpectrum.NumberOfChannels;
            this.energyCalibration = this.energySpectrum.EnergyCalibration;
            this.baseEnergyCalibration = this.energySpectrum.EnergyCalibration;
            if (this.backgroundEnergySpectrum != null)
            {
                this.backgroundNumberOfChannels = this.backgroundEnergySpectrum.NumberOfChannels;
                this.backgroundEnergyCalibration = this.backgroundEnergySpectrum.EnergyCalibration;
            }

            this.CalculateDataForSpecificModes();
            this.SetupDrawingData();
            this.CalculateDrawingDataBoundaries();
            this.ApplySmoothingToDrawingData();
        }

        private void CalculateDataForSpecificModes()
        {
            if (this.backgroundMode == BackgroundMode.Substract)
            {
                if (this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    SpectrumAriphmetics sa = new SpectrumAriphmetics(this.energySpectrum);
                    this.substractedEnergySpectrum = sa.Substract(this.backgroundEnergySpectrum);
                    sa.Dispose();
                }
                else
                {
                    this.substractedEnergySpectrum = this.energySpectrum;
                }
            }

            if (this.backgroundMode == BackgroundMode.ShowContinuum)
            {
                SpectrumAriphmetics sa = new SpectrumAriphmetics((FWHMPeakDetectionMethodConfig)this.activeResultData.PeakDetectionMethodConfig, this.energySpectrum);
                this.continuumEnergySpectrum = sa.Continuum();

                this.peakEnergySpectrum.Clear();
                if (this.activeResultData.DetectedPeaks.Count > 0)
                {
                    FWHMPeakDetectionMethodConfig fwhmcfg = (FWHMPeakDetectionMethodConfig)this.activeResultData.DeviceConfig.PeakDetectionMethodConfig;
                    for (int i = 0; i < this.activeResultData.DetectedPeaks.Count; i++)
                    {
                        (int[] peakSpectrum, int min_ch, int max_ch, Color peakColor) = sa.GetPeak(this.activeResultData.DetectedPeaks[i], this.continuumEnergySpectrum, true, fwhmcfg);
                        this.peakEnergySpectrum.Add((peakSpectrum, min_ch, max_ch, peakColor));
                    }
                }

                sa.Dispose();
            }

            if (this.backgroundMode == BackgroundMode.NormalizeByEfficiency)
            {
                ROIConfigData roi = this.activeResultData.ROIConfig;
                if (this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    this.normByEffBgEnergySpectrum = SpectrumAriphmetics.NormalizeSpectrum(this.backgroundEnergySpectrum, roi);
                }
                else
                {
                    this.normByEffBgEnergySpectrum = this.backgroundEnergySpectrum;
                }

                this.normByEffEnergySpectrum = SpectrumAriphmetics.NormalizeSpectrum(this.energySpectrum, roi);
            }
        }

        private void ApplySmoothingToDrawingData()
        {
            if (this.smoothingMethod == SmoothingMethod.SimpleMovingAverage)
            {
                SpectrumAriphmetics sa = new SpectrumAriphmetics();
                int numberOfSMADataPoints = this.globalConfigManager.GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                int countlimit = this.globalConfigManager.GlobalConfig.ChartViewConfig.CountLimit;
                bool progressiveSmooth = this.globalConfigManager.GlobalConfig.ChartViewConfig.ProgresiveSmooth;
                foreach (ResultData resultData in this.resultDataList)
                {
                    resultData.EnergySpectrum.DrawingSpectrum = sa.SMA2(resultData.EnergySpectrum.DrawingSpectrum, numberOfSMADataPoints, countlimit: countlimit, progressive: progressiveSmooth);

                }
                if (this.IsBackgroundVisible())
                {
                    this.backgroundEnergySpectrum.DrawingSpectrum = sa.SMA2(this.backgroundEnergySpectrum.DrawingSpectrum, numberOfSMADataPoints, countlimit: countlimit, progressive: progressiveSmooth);
                }
                if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null)
                {
                    this.continuumEnergySpectrum.DrawingSpectrum = sa.SMA2(this.continuumEnergySpectrum.DrawingSpectrum, numberOfSMADataPoints, countlimit: countlimit, progressive: progressiveSmooth);
                }
                sa.Dispose();
            }
            else if (this.smoothingMethod == SmoothingMethod.WeightedMovingAverage)
            {
                SpectrumAriphmetics sa = new SpectrumAriphmetics();
                int numberOfWMADataPoints = this.globalConfigManager.GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                int countlimit = this.globalConfigManager.GlobalConfig.ChartViewConfig.CountLimit;
                bool progressiveSmooth = this.globalConfigManager.GlobalConfig.ChartViewConfig.ProgresiveSmooth;
                foreach (ResultData resultData in this.resultDataList)
                {
                    resultData.EnergySpectrum.DrawingSpectrum = sa.WMA2(resultData.EnergySpectrum.DrawingSpectrum, numberOfWMADataPoints, countlimit: countlimit, progressive: progressiveSmooth);
                }
                if (this.IsBackgroundVisible())
                {
                    this.backgroundEnergySpectrum.DrawingSpectrum = sa.WMA2(this.backgroundEnergySpectrum.DrawingSpectrum, numberOfWMADataPoints, countlimit: countlimit, progressive: progressiveSmooth);
                }
                if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null)
                {
                    this.continuumEnergySpectrum.DrawingSpectrum = sa.WMA2(this.continuumEnergySpectrum.DrawingSpectrum, numberOfWMADataPoints, countlimit: countlimit, progressive: progressiveSmooth);
                }
                sa.Dispose();
            }
        }

        private void CalculateDrawingDataBoundaries()
        {
            this.height = base.ClientSize.Height - this.bottom - this.hScrollBar1.Height;
            this.width = base.ClientSize.Width - this.left - this.vScrollBar1.Width;
            this.scrollX = -this.hScrollBar1.Value;
            this.scrollY = -this.vScrollBar1.Value;
            this.minChannel = 0;
            this.maxChannel = this.numberOfChannels - 1;
            if (this.fittingMode != VerticalFittingMode.None)
            {
                this.minChannel = Math.Max((int)CalcChanValue(-this.scrollX) - 5, 0);
                this.maxChannel = Math.Min((int)(CalcChanValue(-this.scrollX + this.width) - 5), this.numberOfChannels - 1);
            }

            // compute global min/max over all visible spectra (including background if visible)
            this.totalMaxValue = 0.0;
            this.totalMinValue = double.PositiveInfinity;

            if (this.IsBackgroundVisible())
            {
                for (int i = 0; i < this.numberOfChannels - 1; i++)
                {
                    double e = this.energyCalibration.ChannelToEnergy((double)i);
                    int bgChannel;
                    try
                    {
                        bgChannel = (int)this.backgroundEnergyCalibration.EnergyToChannel(e, maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                    }
                    catch (OutofChannelException)
                    {
                        continue;
                    }

                    if (bgChannel < 0 || bgChannel >= this.backgroundEnergySpectrum.NumberOfChannels)
                    {
                        continue;
                    }

                    double bgChannelValue;
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                    {
                        bgChannelValue = (double)this.backgroundEnergySpectrum.DrawingSpectrum[bgChannel] / this.backgroundEnergySpectrum.MeasurementTime;
                    }
                    else
                    {
                        bgChannelValue = (double)this.backgroundEnergySpectrum.DrawingSpectrum[bgChannel] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                    }

                    if (bgChannelValue > this.totalMaxValue)
                    {
                        this.totalMaxValue = bgChannelValue;
                    }
                    if (bgChannelValue < this.totalMinValue)
                    {
                        this.totalMinValue = bgChannelValue;
                    }
                }
            }

            foreach (ResultData resultData in this.resultDataList)
            {
                EnergySpectrum energySpectrum = resultData.EnergySpectrum;
                for (int j = 0; j < energySpectrum.NumberOfChannels - 1; j++)
                {
                    double channelValue = (double)energySpectrum.DrawingSpectrum[j];
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond && energySpectrum.MeasurementTime != 0.0)
                    {
                        channelValue /= energySpectrum.MeasurementTime;
                    }
                    if (channelValue > this.totalMaxValue)
                    {
                        this.totalMaxValue = channelValue;
                    }
                    if (channelValue < this.totalMinValue)
                    {
                        this.totalMinValue = channelValue;
                    }
                }
            }

            if (this.verticalUnit == VerticalUnit.Counts)
            {
                if (this.totalMaxValue < 1.0)
                {
                    this.totalMaxValue = 1.0;
                }
            }
            else if (this.totalMaxValue == 0.0)
            {
                this.totalMaxValue = 0.0001;
            }

            if (this.totalMinValue == double.PositiveInfinity)
            {
                this.totalMinValue = 0.0;
            }

            // compute min/max for the visible channel range (or background when fitting requires it)
            this.maxValue = 0.0;
            this.minValue = double.PositiveInfinity;
            bool nonZeroSeen = false;

            for (int k = this.minChannel; k < this.maxChannel; k++)
            {
                double channelValue;

                bool useBackground = (this.fittingMode == VerticalFittingMode.BackgroundMinMax || this.energySpectrum.MeasurementTime == 0.0)
                                     && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0;

                if (useBackground)
                {
                    double e2 = this.energyCalibration.ChannelToEnergy((double)k);
                    int bgChannel;
                    try
                    {
                        bgChannel = (int)this.backgroundEnergyCalibration.EnergyToChannel(e2, maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                    }
                    catch (OutofChannelException)
                    {
                        // skip this channel when background mapping is out of range
                        continue;
                    }

                    if (bgChannel < 0 || bgChannel >= this.backgroundEnergySpectrum.NumberOfChannels)
                    {
                        continue;
                    }

                    if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                    {
                        channelValue = (double)this.backgroundEnergySpectrum.DrawingSpectrum[bgChannel] / this.backgroundEnergySpectrum.MeasurementTime;
                    }
                    else
                    {
                        channelValue = (double)this.backgroundEnergySpectrum.DrawingSpectrum[bgChannel] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                    }
                }
                else
                {
                    channelValue = (double)this.energySpectrum.DrawingSpectrum[k];
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                    {
                        channelValue /= this.energySpectrum.MeasurementTime;
                    }
                }

                if (!nonZeroSeen && channelValue != 0.0)
                {
                    nonZeroSeen = true;
                }

                if (channelValue > this.maxValue)
                {
                    this.maxValue = channelValue;
                }

                if (channelValue < this.minValue && (nonZeroSeen || channelValue != 0.0))
                {
                    this.minValue = channelValue;
                }
            }

            if (this.minValue == double.PositiveInfinity)
            {
                this.minValue = 0.0;
            }

            if (this.verticalUnit == VerticalUnit.Counts)
            {
                if (this.maxValue < 1.0)
                {
                    this.maxValue = 1.0;
                }
            }
            else if (this.fittingMode == VerticalFittingMode.BackgroundMinMax && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
            {
                if (this.maxValue < 1.0 / this.backgroundEnergySpectrum.MeasurementTime)
                {
                    this.maxValue = 1.0 / this.backgroundEnergySpectrum.MeasurementTime;
                }
            }
            else if (this.energySpectrum.MeasurementTime != 0.0)
            {
                if (this.maxValue < 1.0 / this.energySpectrum.MeasurementTime)
                {
                    this.maxValue = 1.0 / this.energySpectrum.MeasurementTime;
                }
            }
            else if (this.maxValue < 0.0001)
            {
                this.maxValue = 0.0001;
            }

            double maxYscale = 1.05;
            if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
            {
                this.totalMaxValue *= Math.Pow(maxYscale, 10);
                this.totalMinValue = 0.0;
                this.maxValue *= Math.Pow(maxYscale, 10);
                this.minValue *= 0.7;
            }
            else if (this.verticalScaleType == VerticalScaleType.PowerScale)
            {
                this.totalMaxValue *= Math.Pow(maxYscale, this.PowNum);
                this.totalMinValue *= 0.98;
                this.maxValue *= Math.Pow(maxYscale, this.PowNum);
                this.minValue *= 0.98;
            }
            else
            {
                this.totalMaxValue *= maxYscale;
                this.totalMinValue *= 0.98;
                this.maxValue *= maxYscale;
                this.minValue *= 0.98;
            }

            // Normalize minimums for log/power scales
            if (this.verticalScaleType == VerticalScaleType.LogarithmicScale || this.verticalScaleType == VerticalScaleType.PowerScale)
            {
                if (this.totalMinValue == 0.0)
                {
                    if (this.verticalUnit == VerticalUnit.Counts)
                    {
                        this.totalMinValue = 0.7;
                    }
                    else if (this.fittingMode == VerticalFittingMode.BackgroundMinMax && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                    {
                        this.totalMinValue = 1.0 / this.backgroundEnergySpectrum.MeasurementTime * 0.7;
                    }
                    else
                    {
                        if (this.energySpectrum.MeasurementTime != 0.0)
                        {
                            this.totalMinValue = 1.0 / this.energySpectrum.MeasurementTime * 0.7;
                            foreach (ResultData resultData2 in this.resultDataList)
                            {
                                double candidate = 1.0 / resultData2.EnergySpectrum.MeasurementTime * 0.7;
                                if (candidate < this.totalMinValue)
                                {
                                    this.totalMinValue = candidate;
                                }
                            }
                        }
                        else
                        {
                            this.totalMinValue = 7E-05;
                        }
                    }
                }

                if (this.minValue == 0.0)
                {
                    if (this.verticalUnit == VerticalUnit.Counts)
                    {
                        this.minValue = 0.7;
                    }
                    else if (this.fittingMode == VerticalFittingMode.BackgroundMinMax && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                    {
                        this.minValue = 1.0 / this.backgroundEnergySpectrum.MeasurementTime * 0.7;
                    }
                    else if (this.energySpectrum.MeasurementTime != 0.0)
                    {
                        this.minValue = 1.0 / this.energySpectrum.MeasurementTime * 0.7;
                    }
                    else
                    {
                        this.minValue = 7E-05;
                    }
                }

                // compute logs/pows used later
                this.maxValueLog = Log10(this.maxValue);
                this.minValueLog = Log10(this.minValue);
                this.totalMaxValueLog = Log10(this.totalMaxValue);
                this.totalMinValueLog = Log10(this.totalMinValue);

                this.maxValuePow = Pow(this.maxValue);
                this.minValuePow = Pow(this.minValue);
                this.totalMaxValuePow = Pow(this.totalMaxValue);
                this.totalMinValuePow = Pow(this.totalMinValue);

                if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
                {
                    this.valueRangeLog = this.totalMaxValueLog - this.totalMinValueLog;
                }
                else
                {
                    this.valueRangePow = this.totalMaxValuePow - this.totalMinValuePow;
                }
            }
            else
            {
                this.valueRange = this.totalMaxValue - this.totalMinValue;
                this.maxValueLog = Log10(this.maxValue);
                this.minValueLog = Log10(this.minValue);
                this.totalMaxValueLog = Log10(this.totalMaxValue);
                this.totalMinValueLog = Log10(this.totalMinValue);
                this.maxValuePow = Pow(this.maxValue);
                this.minValuePow = Pow(this.minValue);
                this.totalMaxValuePow = Pow(this.totalMaxValue);
                this.totalMinValuePow = Pow(this.totalMinValue);
            }

            this.scrollY = this.vScrollBar1.Value;

            if (this.fittingMode != VerticalFittingMode.None)
            {
                if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
                {
                    if (this.maxValueLog - this.minValueLog != 0.0)
                    {
                        this.verticalScale = this.valueRangeLog / (this.maxValueLog - this.minValueLog);
                        if (this.verticalScale < 1.0) this.verticalScale = 1.0;
                    }
                    else
                    {
                        this.verticalScale = 1.0;
                    }
                    this.scrollY = (int)((double)this.height * (this.totalMaxValueLog - this.maxValueLog) / this.valueRangeLog * this.verticalScale);
                }
                else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    if (this.maxValuePow - this.minValuePow != 0.0)
                    {
                        this.verticalScale = this.valueRangePow / (this.maxValuePow - this.minValuePow);
                        if (this.verticalScale < 1.0) this.verticalScale = 1.0;
                    }
                    else
                    {
                        this.verticalScale = 1.0;
                    }
                    this.scrollY = (int)((double)this.height * (this.totalMaxValuePow - this.maxValuePow) / this.valueRangePow * this.verticalScale);
                }
                else
                {
                    if (this.maxValue - this.minValue != 0.0)
                    {
                        this.verticalScale = this.valueRange / (this.maxValue - this.minValue);
                        if (this.verticalScale < 1.0) this.verticalScale = 1.0;
                    }
                    else
                    {
                        this.verticalScale = 1.0;
                    }
                    this.scrollY = (int)((double)this.height * (this.totalMaxValue - this.maxValue) / this.valueRange * this.verticalScale);
                }
            }

            if (this.verticalScale < 1.0)
            {
                this.verticalScale = 1.0;
            }
        }

        private void SetupDrawingData()
        {
            // ensure drawing spectrum arrays
            if (this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.Spectrum != null && (this.backgroundEnergySpectrum.DrawingSpectrum == null || this.backgroundNumberOfChannels != this.backgroundEnergySpectrum.DrawingSpectrum.Length))
            {
                this.backgroundEnergySpectrum.DrawingSpectrum = new double[this.backgroundNumberOfChannels];
            }

            foreach (ResultData resultData in this.resultDataList)
            {
                if (resultData.EnergySpectrum.DrawingSpectrum == null || resultData.EnergySpectrum.NumberOfChannels != resultData.EnergySpectrum.DrawingSpectrum.Length)
                {
                    resultData.EnergySpectrum.DrawingSpectrum = new double[resultData.EnergySpectrum.NumberOfChannels];
                }
            }

            if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null && (this.continuumEnergySpectrum.DrawingSpectrum == null || this.continuumEnergySpectrum.NumberOfChannels != this.continuumEnergySpectrum.DrawingSpectrum.Length))
            {
                this.continuumEnergySpectrum.DrawingSpectrum = new double[this.continuumEnergySpectrum.NumberOfChannels];
            }

            // fill arrays with data
            if (this.IsBackgroundVisible())
            {
                EnergySpectrum source = this.backgroundMode == BackgroundMode.NormalizeByEfficiency
                    ? this.normByEffBgEnergySpectrum
                    : this.backgroundEnergySpectrum;
                Parallel.For(0, this.backgroundNumberOfChannels, l =>
                {
                    this.backgroundEnergySpectrum.DrawingSpectrum[l] = (double)source.Spectrum[l];
                });
            }

            if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null && this.continuumEnergySpectrum.Spectrum != null)
            {
                Parallel.For(0, this.continuumEnergySpectrum.NumberOfChannels, l =>
                {
                    this.continuumEnergySpectrum.DrawingSpectrum[l] = (double)this.continuumEnergySpectrum.Spectrum[l];
                });
            }

            foreach (ResultData resultData in this.resultDataList)
            {
                EnergySpectrum source;
                if (this.backgroundMode == BackgroundMode.Substract)
                {
                    if (resultData == activeResultData)
                    {
                        source = this.substractedEnergySpectrum;
                    }
                    else if (resultData.BackgroundEnergySpectrum != null && resultData.BackgroundEnergySpectrum.MeasurementTime > 0)
                    {
                        SpectrumAriphmetics sa = new SpectrumAriphmetics(resultData.EnergySpectrum);
                        source = sa.Substract(resultData.BackgroundEnergySpectrum);
                        sa.Dispose();
                    }
                    else
                    {
                        source = resultData.EnergySpectrum;
                    }
                }
                else if (this.backgroundMode == BackgroundMode.NormalizeByEfficiency)
                {
                    if (resultData == activeResultData)
                    {
                        source = this.normByEffEnergySpectrum;
                    }
                    else
                    {                        
                        source = SpectrumAriphmetics.NormalizeSpectrum(resultData.EnergySpectrum, resultData.ROIConfig);
                    }
                }
                else
                {
                    source = resultData.EnergySpectrum;
                }

                Parallel.For(0, resultData.EnergySpectrum.NumberOfChannels, l =>
                {
                    resultData.EnergySpectrum.DrawingSpectrum[l] = (double)source.Spectrum[l];
                });
            }
        }

        // Token: 0x060004B4 RID: 1204 RVA: 0x00017C20 File Offset: 0x00015E20
        void ShowStopwatch(Graphics g)
        {
            if (this.measureDrawingTime)
            {
                g.DrawString(this.stopwatch.ElapsedTicks.ToString(), this.Font, Brushes.Black, (float)(base.Width - 400), (float)this.stopwatchY);
                this.stopwatchY += 20;
                this.stopwatch.Reset();
                this.stopwatch.Start();
            }
        }

        // Token: 0x060004B5 RID: 1205 RVA: 0x00017C9C File Offset: 0x00015E9C
        void DrawChart(Graphics g)
        {
            if (this.measureDrawingTime)
            {
                this.stopwatchY = 10;
                this.stopwatch.Start();
            }
            this.scrollX = -this.hScrollBar1.Value;
            this.scrollY = this.vScrollBar1.Value;
            this.scrollBaseY = -((double)this.height * this.verticalScale - (double)this.height);
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            int num = this.CalcMaximumXValue() + this.scrollX + this.left;
            this.ShowROIBackground(g);
            this.ShowSelectionPart1(g);
            this.ShowStopwatch(g);
            this.ShowVerticalAxis(g);
            this.ShowHorizontalAxis(g);
            g.SetClip(new Rectangle(this.left + 1, 0, num - this.left, this.height));
            this.ShowStopwatch(g);
            this.ShowROIBorderLine(g);
            if (EnergySpectrumView.logoImage != null)
            {
                g.DrawImage(EnergySpectrumView.logoImage, base.Width - EnergySpectrumView.logoImage.Width - 40, 10, EnergySpectrumView.logoImage.Width, EnergySpectrumView.logoImage.Height);
            }
            if (this.activeResultData.Visible)
            {
                if (this.IsBackgroundVisible())
                {
                    this.ShowROINetRegion(g, true);
                }

                if (this.chartType == ChartType.BarChart)
                {
                    if (colorConfig.SpectrumDrawingOrder == 0)
                    {
                        // draw background/continuum first, then active spectrum
                        if (this.IsBackgroundVisible())
                        {
                            int alpha = (int)(colorConfig.BackgroundSpectrumColorTransparency * 255m / 100m);
                            using (Brush brush = new SolidBrush(Color.FromArgb(alpha, colorConfig.BackgroundSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha, colorConfig.BackgroundSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush, this.backgroundEnergySpectrum, this.backgroundEnergyCalibration, true);
                                }
                            }
                        }

                        if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null)
                        {
                            int alpha = (int)(colorConfig.BackgroundSpectrumColorTransparency * 255m / 100m);
                            using (Brush brush = new SolidBrush(Color.FromArgb(alpha, colorConfig.BackgroundSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha, colorConfig.BackgroundSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, true);
                                }
                            }

                            for (int i = 0; i < this.peakEnergySpectrum.Count; i++)
                            {
                                (int[] peakSpectrum, int min_ch, int max_ch, Color peakColor) = this.peakEnergySpectrum[i];
                                using (Brush brush = new SolidBrush(Color.FromArgb(alpha, peakColor)))
                                {
                                    using (new Pen(Color.FromArgb(alpha, peakColor)))
                                    {
                                        this.DrawPeakBarChart(g, brush, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, peakSpectrum, min_ch, max_ch);
                                    }
                                }
                            }
                        }

                        if (this.energySpectrum.MeasurementTime != 0.0)
                        {
                            int alpha2 = (int)(colorConfig.ActiveSpectrumColorTransparency * 255m / 100m);
                            Color color = this.backgroundMode == BackgroundMode.Substract
                                ? Color.FromArgb(alpha2, colorConfig.BgDiffColor.Color)
                                : Color.FromArgb(alpha2, colorConfig.ActiveSpectrumColor.Color);

                            using (Brush brush2 = new SolidBrush(color))
                            {
                                using (new Pen(Color.FromArgb(alpha2, colorConfig.ActiveSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush2, this.energySpectrum, this.energyCalibration, false);
                                }
                            }
                        }
                    }
                    else
                    {
                        // draw active spectrum first, then background/continuum
                        if (this.energySpectrum.MeasurementTime != 0.0)
                        {
                            int alpha3 = (int)(colorConfig.ActiveSpectrumColorTransparency * 255m / 100m);
                            Color color = this.backgroundMode == BackgroundMode.Substract
                                ? Color.FromArgb(alpha3, colorConfig.BgDiffColor.Color)
                                : Color.FromArgb(alpha3, colorConfig.ActiveSpectrumColor.Color);

                            using (Brush brush3 = new SolidBrush(color))
                            {
                                using (new Pen(Color.FromArgb(alpha3, colorConfig.ActiveSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush3, this.energySpectrum, this.energyCalibration, false);
                                }
                            }
                        }

                        if (this.IsBackgroundVisible())
                        {
                            int alpha4 = (int)(colorConfig.BackgroundSpectrumColorTransparency * 255m / 100m);
                            using (Brush brush4 = new SolidBrush(Color.FromArgb(alpha4, colorConfig.BackgroundSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha4, colorConfig.BackgroundSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush4, this.backgroundEnergySpectrum, this.backgroundEnergyCalibration, true);
                                }
                            }
                        }

                        if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null)
                        {
                            int alpha4 = (int)(colorConfig.BackgroundSpectrumColorTransparency * 255m / 100m);
                            using (Brush brush4 = new SolidBrush(Color.FromArgb(alpha4, colorConfig.BackgroundSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha4, colorConfig.BackgroundSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush4, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, true);
                                }
                            }
                            for (int i = 0; i < this.peakEnergySpectrum.Count; i++)
                            {
                                (int[] peakSpectrum, int min_ch, int max_ch, Color peakColor) = this.peakEnergySpectrum[i];
                                using (Brush brush = new SolidBrush(Color.FromArgb(alpha4, peakColor)))
                                {
                                    using (new Pen(Color.FromArgb(alpha4, peakColor)))
                                    {
                                        this.DrawPeakBarChart(g, brush, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, peakSpectrum, min_ch, max_ch);
                                    }
                                }
                            }
                        }
                    }

                    this.ShowSelectionPart2(g);
                }
                else
                {
                    this.ShowSelectionPart2(g);
                    if (this.drawingMode == DrawingMode.HighDefinition)
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                    }
                    if (this.IsBackgroundVisible())
                    {
                        using (Pen pen5 = new Pen(colorConfig.BackgroundSpectrumColor.Color))
                        {
                            this.DrawLineChart(g, pen5, this.backgroundEnergySpectrum, this.backgroundEnergyCalibration, true);
                        }
                    }
                    if (this.backgroundMode == BackgroundMode.ShowContinuum && this.continuumEnergySpectrum != null)
                    {
                        using (Pen pen5 = new Pen(colorConfig.BackgroundSpectrumColor.Color))
                        {
                            this.DrawLineChart(g, pen5, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, true);
                        }
                        for (int i = 0; i < this.peakEnergySpectrum.Count; i++)
                        {
                            (int[] peakSpectrum, int min_ch, int max_ch, Color peakColor) = this.peakEnergySpectrum[i];
                            using (Pen pen5 = new Pen(peakColor))
                            {
                                this.DrawPeakLineChart(g, pen5, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, peakSpectrum, min_ch, max_ch);
                            }
                        }
                    }
                    if (this.energySpectrum.MeasurementTime != 0.0)
                    {
                        Color color = this.backgroundMode == BackgroundMode.Substract
                            ? colorConfig.BgDiffColor.Color
                            : colorConfig.ActiveSpectrumColor.Color;

                        using (Pen pen6 = new Pen(color))
                        {
                            this.DrawLineChart(g, pen6, this.energySpectrum, this.energyCalibration, false);
                        }
                    }
                    if (this.drawingMode == DrawingMode.HighDefinition)
                    {
                        g.SmoothingMode = SmoothingMode.Default;
                        g.PixelOffsetMode = PixelOffsetMode.Default;
                    }
                }
                this.ShowROIReferencePeak(g);
            }
            this.ShowStopwatch(g);
            if (this.drawingMode == DrawingMode.HighDefinition)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
            }
            for (int i = 0; i < this.resultDataList.Count; i++)
            {
                ResultData resultData = this.resultDataList[i];
                if (resultData != this.activeResultData && resultData.Visible)
                {
                    using (Pen pen7 = new Pen(colorConfig.SpectrumColorList[i].Color))
                    {
                        EnergySpectrum energySpectrum = resultData.EnergySpectrum;
                        this.DrawLineChart(g, pen7, energySpectrum, energySpectrum.EnergyCalibration, false);
                    }
                }
            }
            if (this.drawingMode == DrawingMode.HighDefinition)
            {
                g.SmoothingMode = SmoothingMode.Default;
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            this.ShowStopwatch(g);
            if (this.activeResultData.Visible)
            {
                this.ShowCalibrationPeaks(g, this.energySpectrum, this.energyCalibration);
            }
            if (this.peakMode == PeakMode.Visible && this.activeResultData.Visible)
            {
                this.ShowDetectedPeaks(g, this.energySpectrum, this.energyCalibration);
            }
            this.DrawFWHM(g);
            g.ResetClip();
            this.validCursor = (this.cursorX != -1 && this.cursorX >= this.left && this.cursorX <= this.width + this.left);
            this.ShowCursorValues(g);
            this.ShowStopwatch(g);
            if (this.measureDrawingTime)
            {
                this.stopwatch.Stop();
            }
        }

        public double getNumberOfChannels()
        {
            return (double)this.numberOfChannels;
        }

        // Token: 0x060004B6 RID: 1206 RVA: 0x00018404 File Offset: 0x00016604
        void DrawFWHM(Graphics g)
        {
            if (this.selectionStart < 0)
            {
                return;
            }
            int selStart;
            int selEnd;
            if (this.selectionStart < this.selectionEnd)
            {
                selStart = this.selectionStart;
                selEnd = this.selectionEnd;
            }
            else
            {
                selStart = this.selectionEnd;
                selEnd = this.selectionStart;
            }
            this.selectionFWHM = -1.0;
            if (selStart == selEnd)
            {
                return;
            }

            EnergyResolutionResult energyResolutionResult;
            if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.substractedEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
            {
                energyResolutionResult = EnergyResolutionCalculator.CalculateFWHM(this.substractedEnergySpectrum, selStart, selEnd);
            }
            else if (this.backgroundMode == BackgroundMode.NormalizeByEfficiency)
            {
                energyResolutionResult = EnergyResolutionCalculator.CalculateFWHM(this.normByEffEnergySpectrum, selStart, selEnd);
            }
            else
            {
                energyResolutionResult = EnergyResolutionCalculator.CalculateFWHM(this.energySpectrum, selStart, selEnd);
            }
            if (energyResolutionResult == null)
            {
                return;
            }
            this.selectionFWHM = energyResolutionResult.Resolution;
            this.selectionFullWidth = (int)(energyResolutionResult.RightChannel - energyResolutionResult.LeftChannel);
            this.selectionFWHMinkev = energyResolutionResult.ResolutionInkeV;
            this.selectionCentroidCh = (int)energyResolutionResult.MaxChannel;
            this.selectionCentroidkeV = energyCalibration.ChannelToEnergy(this.selectionCentroidCh);
            int num3 = -1;
            int y = -1;
            int num8;
            for (int i = (int)energyResolutionResult.StartChannel; i <= (int)energyResolutionResult.EndChannel; i++)
            {
                double num4 = energyResolutionResult.StartValue + (energyResolutionResult.EndValue - energyResolutionResult.StartValue) * ((double)i - energyResolutionResult.StartChannel) / (energyResolutionResult.EndChannel - energyResolutionResult.StartChannel);
                if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                {
                    num4 /= this.energySpectrum.MeasurementTime;
                }
                int num5;
                if (this.verticalScaleType == VerticalScaleType.LinearScale)
                {
                    num5 = this.height - (int)((num4 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else if (num4 > 0.0 && this.verticalScaleType == VerticalScaleType.LogarithmicScale)
                {
                    double num6 = Log10(num4);
                    num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else if (num4 > 0.0 && this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    double num6 = Pow(num4);
                    num5 = this.height - (int)((num6 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    num5 = this.height;
                }
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    double num7 = this.energyCalibration.ChannelToEnergy((double)i + 0.5);
                    num8 = (int)((num7 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    num8 = (int)(((double)i + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                if (num3 > 0)
                {
                    g.DrawLine(Pens.Yellow, num3, y, num8, num5);
                }
                num3 = num8;
                y = num5;
            }
            if (this.horizontalUnit == HorizontalUnit.Energy)
            {
                double num9 = this.energyCalibration.ChannelToEnergy(energyResolutionResult.MaxChannel + 0.5);
                num8 = (int)((num9 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
            }
            else
            {
                num8 = (int)((energyResolutionResult.MaxChannel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
            }
            double num10 = energyResolutionResult.MaxValue;
            double num11 = energyResolutionResult.MaxBaseValue;
            if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
            {
                num10 /= this.energySpectrum.MeasurementTime;
                num11 /= this.energySpectrum.MeasurementTime;
            }
            if (num10 < this.totalMinValue || num11 < this.totalMinValue) return;
            int y2;
            int y3;
            if (this.verticalScaleType == VerticalScaleType.LinearScale)
            {
                y2 = this.height - (int)((num10 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                y3 = this.height - (int)((num11 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
            } else if (this.verticalScaleType == VerticalScaleType.PowerScale)
            {
                if (num10 > 0.0)
                {
                    y2 = this.height - (int)((Pow(num10) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    y2 = this.height;
                }
                if (num11 > 0.0)
                {
                    y3 = this.height - (int)((Pow(num11) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    y3 = this.height;
                }
            }
            else
            {
                if (num10 > 0.0)
                {
                    y2 = this.height - (int)((Log10(num10) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    y2 = this.height;
                }
                if (num11 > 0.0)
                {
                    y3 = this.height - (int)((Log10(num11) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    y3 = this.height;
                }
            }
            if (num8 > this.width || y2 > this.height || y3 > this.height || num8 < 0 || y2 < 0 || y3 < 0)
            {
                return;
            }
            g.DrawLine(Pens.Yellow, num8, y2, num8, y3);


            if (energyResolutionResult.LeftChannel < energyResolutionResult.MaxChannel && energyResolutionResult.RightChannel > energyResolutionResult.MaxChannel)
            {
                int x;
                int x2;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    double num12 = this.energyCalibration.ChannelToEnergy(energyResolutionResult.LeftChannel + 0.5);
                    double num13 = this.energyCalibration.ChannelToEnergy(energyResolutionResult.RightChannel + 0.5);
                    x = (int)((num12 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                    x2 = (int)((num13 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    x = (int)((energyResolutionResult.LeftChannel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                    x2 = (int)((energyResolutionResult.RightChannel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                double num14 = energyResolutionResult.HalfValue;
                if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                {
                    num14 /= this.energySpectrum.MeasurementTime;
                }
                int num5;
                if (this.verticalScaleType == VerticalScaleType.LinearScale)
                {
                    num5 = this.height - (int)((num14 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else if (num14 > 0.0 && this.verticalScaleType == VerticalScaleType.LogarithmicScale)
                {
                    num5 = this.height - (int)((Log10(num14) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else if (num14 > 0.0 && this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    num5 = this.height - (int)((Pow(num14) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    num5 = this.height;
                }
                if (num5 > 0 && num5 < this.height)
                {
                    try
                    {
                        g.DrawLine(Pens.Yellow, x, num5, x2, num5);
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("Found");
                    }
                }
            }
        }

        // Token: 0x060004B7 RID: 1207 RVA: 0x00018A9C File Offset: 0x00016C9C
        void DrawBarChart(Graphics g, Brush brush, EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground)
        {
            int num = this.CalcMaximumXValue() + this.scrollX + this.left;
            int i = this.left;
            while (i < num)
            {
                int num3;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    try
                    {
                        double num2 = (double)(i - this.scrollX - this.left) / this.horizontalScale;
                        num3 = (int)calibration.EnergyToChannel(num2 / this.pixelPerEnergy + this.energyViewOffset, maxChannels: spectrum.NumberOfChannels);
                    }
                    catch (OutofChannelException)
                    {
                        break;
                    }
                }
                else
                {
                    num3 = (int)((double)(i - this.scrollX - this.left) / this.horizontalScale);
                    if (this.backgroundEnergyCalibration != null && isBackground && !this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                    {
                        num3 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy((double)num3), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                    }
                }

                if (num3 >= 0 && num3 < spectrum.DrawingSpectrum.Length)
                {
                    double num4 = spectrum.DrawingSpectrum[num3];
                    if (this.backgroundEnergySpectrum != null && isBackground)
                    {
                        if (this.verticalUnit == VerticalUnit.Counts)
                        {
                            num4 = num4 * this.energySpectrum.MeasurementTime / spectrum.MeasurementTime;
                        }
                        else if (this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                        {
                            num4 /= spectrum.MeasurementTime;
                        }
                    }
                    else if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.MeasurementTime != 0.0)
                    {
                        num4 /= spectrum.MeasurementTime;
                    }
                    if (num4 > 0.0)
                    {
                        int num5;
                        if (this.verticalScaleType == VerticalScaleType.LinearScale)
                        {
                            num5 = this.height - (int)((num4 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                        {
                            double num6 = Pow(num4);
                            num5 = this.height - (int)((num6 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        else
                        {
                            double num6 = Log10(num4);
                            num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        if (i > this.left)
                        {
                            g.FillRectangle(brush, i, num5, 1, this.height - num5);
                        }
                    }
                }

                i++;
            }
        }

        void DrawPeakBarChart(Graphics g, Brush brush, EnergySpectrum spectrum, EnergyCalibration calibration, int[] peakSpectrum, int min_ch, int max_ch)
        {
            int num = this.CalcMaximumXValue() + this.scrollX + this.left;
            int i = this.left;
            while (i < num)
            {
                int num3;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    try
                    {
                        double num2 = (double)(i - this.scrollX - this.left) / this.horizontalScale;
                        num3 = (int)calibration.EnergyToChannel(num2 / this.pixelPerEnergy + this.energyViewOffset, maxChannels: spectrum.NumberOfChannels);
                    }
                    catch (OutofChannelException)
                    {
                        break;
                    }
                }
                else
                {
                    num3 = (int)((double)(i - this.scrollX - this.left) / this.horizontalScale);
                }

                if (num3 >= min_ch && num3 < max_ch)
                {
                    double peakv = spectrum.DrawingSpectrum[num3] + peakSpectrum[num3];
                    double num4 = spectrum.DrawingSpectrum[num3];
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.MeasurementTime != 0.0)
                    {
                        num4 /= spectrum.MeasurementTime;
                        peakv /= spectrum.MeasurementTime;
                    }
                    if (num4 > 0.0 && peakv > 0.0)
                    {
                        int num5;
                        int peak;
                        if (this.verticalScaleType == VerticalScaleType.LinearScale)
                        {
                            num5 = this.height - (int)((num4 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            peak = this.height - (int)((peakv - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                        {
                            double num6 = Pow(num4);
                            num5 = this.height - (int)((num6 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            double peaklog = Pow(peakv);
                            peak = this.height - (int)((peaklog - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        else
                        {
                            double num6 = Log10(num4);
                            num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            double peaklog = Log10(peakv);
                            peak = this.height - (int)((peaklog - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        if (i > this.left)
                        {
                            g.FillRectangle(brush, i, peak, 1, num5 - peak);
                        }
                    }
                }

                i++;
            }
        }

        // Token: 0x060004B8 RID: 1208 RVA: 0x00018CD8 File Offset: 0x00016ED8
        void DrawLineChart(Graphics g, Pen pen, EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground)
        {
            int y = this.height;
            int x = 0;
            if (spectrum.DrawingSpectrum == null) return;
            for (int i = 0; i < spectrum.DrawingSpectrum.Length; i++)
            {
                double num = spectrum.DrawingSpectrum[i];
                if (isBackground)
                {
                    if (this.verticalUnit == VerticalUnit.Counts)
                    {
                        if (spectrum.MeasurementTime != 0.0)
                        {
                            num = num * this.energySpectrum.MeasurementTime / spectrum.MeasurementTime;
                        }
                    }
                    else if (spectrum.MeasurementTime != 0.0)
                    {
                        num /= spectrum.MeasurementTime;
                    }
                }
                else if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.MeasurementTime != 0.0)
                {
                    num /= spectrum.MeasurementTime;
                }
                int num3;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    double num2 = calibration.ChannelToEnergy((double)i + 0.5);
                    num3 = (int)((num2 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                }
                else if (this.backgroundEnergyCalibration != null && isBackground)
                {
                    double num4 = this.baseEnergyCalibration.EnergyToChannel(this.backgroundEnergyCalibration.ChannelToEnergy((double)i), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                    num3 = (int)((num4 + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    num3 = (int)(((double)i + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                int num5;
                if (this.verticalScaleType == VerticalScaleType.LinearScale)
                {
                    if (num <= 0.0)
                    {
                        num5 = this.height;
                    }
                    else
                    {
                        num5 = this.height - (int)((num - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    double num6 = Pow(num);
                    if (num == 0.0)
                    {
                        num5 = this.height + 100;
                    }
                    else
                    {
                        num5 = this.height - (int)((num6 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else
                {
                    double num6 = Log10(num);
                    if (num == 0.0)
                    {
                        num5 = this.height + 100;
                    }
                    else
                    {
                        num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                if (num3 > this.left)
                {
                    try
                    {
                        g.DrawLine(pen, x, y, num3, num5);
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("123");
                    }
                }
                x = num3;
                y = num5;
            }
        }

        void DrawPeakLineChart(Graphics g, Pen pen, EnergySpectrum spectrum, EnergyCalibration calibration, int[] peakSpectrum, int min_ch, int max_ch)
        {
            int y = this.height;
            int x = 0;
            for (int i = min_ch; i < max_ch; i++)
            {
                double peakv = spectrum.DrawingSpectrum[i] + peakSpectrum[i];
                double num = spectrum.DrawingSpectrum[i];
                if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.MeasurementTime != 0.0)
                {
                    num /= spectrum.MeasurementTime;
                    peakv /= spectrum.MeasurementTime;
                }
                int num3;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    double num2 = calibration.ChannelToEnergy((double)i + 0.5);
                    num3 = (int)((num2 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    num3 = (int)(((double)i + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                int num5;
                int peak;
                if (this.verticalScaleType == VerticalScaleType.LinearScale)
                {
                    if (num <= 0.0 || peakv <= 0.0)
                    {
                        num5 = this.height;
                        peak = this.height;
                    }
                    else
                    {
                        num5 = this.height - (int)((num - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        peak = this.height - (int)((peakv - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    double num6 = Pow(num);
                    double peaklog = Pow(peakv);
                    if (num == 0.0 || peakv == 0.0)
                    {
                        num5 = this.height + 100;
                        peak = this.height + 100;
                    }
                    else
                    {
                        num5 = this.height - (int)((num6 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        peak = this.height - (int)((peaklog - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else
                {
                    double num6 = Log10(num);
                    double peaklog = Log10(peakv);
                    if (num == 0.0 || peakv == 0.0)
                    {
                        num5 = this.height + 100;
                        peak = this.height + 100;
                    }
                    else
                    {
                        num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        peak = this.height - (int)((peaklog - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                if (num3 > this.left)
                {
                    try
                    {
                        if (i == min_ch)
                        {
                            x = num3;
                            y = peak;
                        }
                        g.DrawLine(pen, x, y, num3, peak);
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("123");
                    }
                }
                x = num3;
                y = peak;
            }
        }

        // Token: 0x060004B9 RID: 1209 RVA: 0x00018F58 File Offset: 0x00017158
        void ShowROIBackground(Graphics g)
        {
            if (this.roiConfig == null)
            {
                return;
            }
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            using (Brush brush = new SolidBrush(colorConfig.ROIBackgroundColor.Color))
            {
                foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
                {
                    if (!roidefinitionData.Enabled)
                    {
                        continue;
                    }

                    double lowerLimit = roidefinitionData.LowerLimit;
                    double upperLimit = roidefinitionData.UpperLimit;
                    float leftX;
                    float rightX;

                    if (this.horizontalUnit == HorizontalUnit.Channel)
                    {
                        try
                        {
                            leftX = (float)(this.energyCalibration.EnergyToChannel(lowerLimit, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            // original behavior: stop processing further ROIs when lower limit is out of channel range
                            break;
                        }

                        try
                        {
                            rightX = (float)(this.energyCalibration.EnergyToChannel(upperLimit, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            // if upper limit is out of range, clamp to last channel
                            rightX = (float)((double)(this.numberOfChannels - 1) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                    }
                    else
                    {
                        leftX = (float)((lowerLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        rightX = (float)((upperLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                    }

                    g.FillRectangle(brush, leftX, 0f, rightX - leftX, (float)(this.height - 1));
                }
            }
        }

        void ShowROIReferencePeak(Graphics g)
        {
            if (this.roiConfig == null)
            {
                return;
            }

            List<double> intencityScale = new List<double>();

            foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
            {
                if (roidefinitionData.Enabled && (roidefinitionData.Intencity < 100.0 || roidefinitionData.Intencity > 0.0))
                {
                    intencityScale.Add(roidefinitionData.Intencity);
                }
            }

            if (intencityScale.Count == 0)
            {
                return;
            }

            intencityScale.Sort();
            double intencityMax = intencityScale[intencityScale.Count - 1];


            foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
            {
                if (roidefinitionData.Enabled)
                {
                    if (roidefinitionData.Intencity == 0.0)
                    {
                        continue;
                    }
                    double referencepeak = roidefinitionData.PeakEnergy;
                    double intencityscale;
                    if (this.verticalScaleType == VerticalScaleType.LinearScale)
                    {
                        intencityscale = 0.8 * roidefinitionData.Intencity / intencityMax;
                    }
                    else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                    {
                        intencityscale = 0.8 * Pow(roidefinitionData.Intencity) / Pow(intencityMax);
                    }
                    else
                    {
                        intencityscale = 0.8 * Log10(roidefinitionData.Intencity) / Log10(intencityMax);
                    }
                    
                    float num;
                    if (this.horizontalUnit == HorizontalUnit.Channel)
                    {
                        try
                        {
                            num = (float)(this.energyCalibration.EnergyToChannel(referencepeak, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            break;
                        }
                    }
                    num = (float)((referencepeak - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                    if (num > (float)this.left)
                    {
                        Pen pen = new Pen(roidefinitionData.Color.Color, 2);
                        g.DrawLine(pen, num, (float)((1.0 - intencityscale) * (this.height - 1)), num, (float)(this.height - 1));
                    }
            }
            }
        }

        // Token: 0x060004BA RID: 1210 RVA: 0x00019164 File Offset: 0x00017364
        void ShowROIBorderLine(Graphics g)
        {
            if (this.roiConfig == null)
            {
                return;
            }
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            using (Pen pen = new Pen(colorConfig.ROIBorderColor.Color))
            {
                foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
                {
                    if (!roidefinitionData.Enabled)
                    {
                        continue;
                    }

                    double lowerLimit = roidefinitionData.LowerLimit;
                    double upperLimit = roidefinitionData.UpperLimit;
                    float num;
                    float num2;

                    if (this.horizontalUnit == HorizontalUnit.Channel)
                    {
                        try
                        {
                            num = (float)(this.energyCalibration.EnergyToChannel(lowerLimit, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            // lower limit is out of channel range -> stop processing further ROIs (original behavior)
                            break;
                        }

                        try
                        {
                            num2 = (float)(this.energyCalibration.EnergyToChannel(upperLimit, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            // clamp upper limit to last channel when out of range
                            num2 = (float)((double)(this.numberOfChannels - 1) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                    }
                    else
                    {
                        num = (float)((lowerLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        num2 = (float)((upperLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                    }

                    if (num > (float)this.left)
                    {
                        g.DrawLine(pen, num, 0f, num, (float)(this.height - 1));
                    }
                    if (num2 > (float)this.left)
                    {
                        g.DrawLine(pen, num2, 0f, num2, (float)(this.height - 1));
                    }
                }
            }
        }

        // Token: 0x060004BB RID: 1211 RVA: 0x000193A4 File Offset: 0x000175A4
        void ShowROINetRegion(Graphics g, bool baseDrawing)
        {
            if (this.roiConfig == null)
            {
                return;
            }
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            using (Brush brush = new SolidBrush(colorConfig.ROINetColor.Color))
            {
                foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
                {
                    if (!roidefinitionData.Enabled)
                    {
                        continue;
                    }

                    double lowerLimit = roidefinitionData.LowerLimit;
                    double upperLimit = roidefinitionData.UpperLimit;
                    float leftX;
                    float rightX;

                    if (this.horizontalUnit == HorizontalUnit.Channel)
                    {
                        try
                        {
                            leftX = (float)(this.energyCalibration.EnergyToChannel(lowerLimit, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            // Original behavior returned from method when lower limit is out of range
                            return;
                        }

                        try
                        {
                            rightX = (float)(this.energyCalibration.EnergyToChannel(upperLimit, maxChannels: this.energySpectrum.NumberOfChannels) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            // clamp to last channel when upper limit is out of range
                            rightX = (float)((double)(this.numberOfChannels - 1) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                    }
                    else
                    {
                        leftX = (float)((lowerLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        rightX = (float)((upperLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                    }

                    int startPixel = (int)leftX;
                    int endPixel = (int)rightX;

                    for (int i = startPixel; i <= endPixel; i++)
                    {
                        double channelIndex;
                        if (this.horizontalUnit == HorizontalUnit.Energy)
                        {
                            double pixelEnergyPos = (double)(i - this.scrollX - this.left) / this.horizontalScale;
                            try
                            {
                                channelIndex = this.energyCalibration.EnergyToChannel(pixelEnergyPos / this.pixelPerEnergy + this.energyViewOffset, maxChannels: this.energySpectrum.NumberOfChannels);
                            }
                            catch (OutofChannelException)
                            {
                                // when mapping from pixel/energy to channel goes out of range, stop scanning this ROI
                                break;
                            }
                        }
                        else
                        {
                            channelIndex = (double)((int)((double)(i - this.scrollX - this.left) / this.horizontalScale));
                        }

                        int ch = (int)channelIndex;
                        if (ch >= 0 && ch < this.energySpectrum.Spectrum.Length)
                        {
                            double fgValue = this.energySpectrum.DrawingSpectrum[ch];
                            if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                            {
                                fgValue /= this.energySpectrum.MeasurementTime;
                            }

                            double bgValue = 0.0;
                            if (!(this.backgroundEnergySpectrum == null || this.backgroundEnergySpectrum.MeasurementTime == 0.0))
                            {
                                int bgCh = ch;
                                if (!this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                                {
                                    bgCh = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy(channelIndex), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                                }
                                if (bgCh < 0 || bgCh >= this.backgroundEnergySpectrum.Spectrum.Length)
                                {
                                    // skip drawing for this pixel if background mapping is invalid
                                    continue;
                                }
                                if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                                {
                                    bgValue = this.backgroundEnergySpectrum.DrawingSpectrum[bgCh] / this.backgroundEnergySpectrum.MeasurementTime;
                                }
                                else
                                {
                                    bgValue = this.backgroundEnergySpectrum.DrawingSpectrum[bgCh] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                                }
                            }

                            int y1;
                            int y2;
                            if (this.verticalScaleType == VerticalScaleType.LinearScale)
                            {
                                y1 = this.height - (int)((fgValue - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                y2 = this.height - (int)((bgValue - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                            {
                                if (fgValue == 0.0)
                                {
                                    y1 = this.height;
                                }
                                else
                                {
                                    y1 = this.height - (int)((Pow(fgValue) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                }
                                if (y1 > this.height) y1 = this.height;

                                if (bgValue == 0.0)
                                {
                                    y2 = this.height;
                                }
                                else
                                {
                                    y2 = this.height - (int)((Pow(bgValue) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                }
                                if (y2 > this.height) y2 = this.height;
                            }
                            else
                            {
                                if (fgValue == 0.0)
                                {
                                    y1 = this.height;
                                }
                                else
                                {
                                    y1 = this.height - (int)((Log10(fgValue) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                }
                                if (y1 > this.height) y1 = this.height;

                                if (bgValue == 0.0)
                                {
                                    y2 = this.height;
                                }
                                else
                                {
                                    y2 = this.height - (int)((Log10(bgValue) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                }
                                if (y2 > this.height) y2 = this.height;
                            }

                            if (y1 > y2)
                            {
                                int tmp = y1;
                                y1 = y2;
                                y2 = tmp;
                            }

                            if (i > this.left)
                            {
                                if (this.chartType == ChartType.BarChart)
                                {
                                    g.FillRectangle(brush, i, y1, 1, y2 - y1);
                                }
                                else
                                {
                                    g.FillRectangle(brush, i, y1, 1, y2 - y1 + 1);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Token: 0x060004BC RID: 1212 RVA: 0x00019900 File Offset: 0x00017B00
        void ShowSelectionPart1(Graphics g)
        {
            if (this.selectionStart == -1)
            {
                return;
            }
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            using (Brush brush = new SolidBrush(colorConfig.SelectionBackgroundColor.Color))
            {
                int num;
                int num2;
                if (this.selectionStart < this.selectionEnd)
                {
                    num = this.selectionStart;
                    num2 = this.selectionEnd;
                }
                else
                {
                    num = this.selectionEnd;
                    num2 = this.selectionStart;
                }
                num2++;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    double num3 = this.energyCalibration.ChannelToEnergy((double)num);
                    double num4 = this.energyCalibration.ChannelToEnergy((double)num2);
                    int num5 = (int)((num3 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                    int num6 = (int)((num4 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                    g.FillRectangle(brush, num5, 0, num6 - num5, this.height - 1);
                }
                else
                {
                    int num7 = (int)((double)num * this.horizontalScale) + this.scrollX + this.left;
                    int num8 = (int)((double)num2 * this.horizontalScale) + this.scrollX + this.left;
                    g.FillRectangle(brush, num7, 0, num8 - num7, this.height - 1);
                }
            }
        }

        // Token: 0x060004BD RID: 1213 RVA: 0x00019A84 File Offset: 0x00017C84
        void ShowSelectionPart2(Graphics g)
        {
            if (this.selectionStart == -1)
            {
                return;
            }
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            int num;
            int num2;
            if (this.selectionStart < this.selectionEnd)
            {
                num = this.selectionStart;
                num2 = this.selectionEnd;
            }
            else
            {
                num = this.selectionEnd;
                num2 = this.selectionStart;
            }
            num2++;
            int num5;
            int num6;
            if (this.horizontalUnit == HorizontalUnit.Energy)
            {
                double num3 = this.energyCalibration.ChannelToEnergy((double)num);
                double num4 = this.energyCalibration.ChannelToEnergy((double)num2);
                num5 = (int)((num3 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                num6 = (int)((num4 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
            }
            else
            {
                num5 = (int)((double)num * this.horizontalScale) + this.scrollX + this.left;
                num6 = (int)((double)num2 * this.horizontalScale) + this.scrollX + this.left;
            }
            using (Pen pen = new Pen(colorConfig.SelectionBorderColor.Color))
            {
                if (num5 > this.left)
                {
                    g.DrawLine(pen, num5, 0, num5, this.height - 1);
                }
                if (num6 > this.left)
                {
                    g.DrawLine(pen, num6, 0, num6, this.height - 1);
                }
            }
            using (Brush brush = new SolidBrush(colorConfig.SelectionNetColor.Color))
            {
                int i = num5;
                while (i <= num6)
                {
                    double channelIndex;
                    if (this.horizontalUnit == HorizontalUnit.Energy)
                    {
                        double pixelEnergyPos = (double)(i - this.scrollX - this.left) / this.horizontalScale;
                        try
                        {
                            channelIndex = this.energyCalibration.EnergyToChannel(pixelEnergyPos / this.pixelPerEnergy + this.energyViewOffset, maxChannels: this.energySpectrum.NumberOfChannels);
                        }
                        catch (OutofChannelException)
                        {
                            // mapping from pixel to channel went out of range -> stop scanning selection
                            break;
                        }
                    }
                    else
                    {
                        channelIndex = (double)((int)((double)(i - this.scrollX - this.left) / this.horizontalScale));
                    }

                    int ch = (int)channelIndex;
                    if (ch >= 0 && ch < this.energySpectrum.Spectrum.Length)
                    {
                        double fgValue = this.energySpectrum.DrawingSpectrum[ch];
                        double bgValue = 0.0;
                        if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                        {
                            fgValue /= this.energySpectrum.MeasurementTime;
                        }
                        if (this.IsBackgroundVisible())
                        {
                            int bgCh = ch;
                            if (!this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                            {
                                bgCh = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy(channelIndex), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                            }
                            if (bgCh < 0 || bgCh >= this.backgroundEnergySpectrum.Spectrum.Length)
                            {
                                // background mapping invalid for this pixel -> skip drawing this pixel
                                i++;
                                continue;
                            }
                            if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                            {
                                bgValue = this.backgroundEnergySpectrum.DrawingSpectrum[bgCh] / this.backgroundEnergySpectrum.MeasurementTime;
                            }
                            else
                            {
                                bgValue = this.backgroundEnergySpectrum.DrawingSpectrum[bgCh] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                            }
                        }

                        int y1;
                        int y2;
                        if (this.verticalScaleType == VerticalScaleType.LinearScale)
                        {
                            y1 = this.height - (int)((fgValue - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            y2 = this.height - (int)((bgValue - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                        {
                            if (fgValue == 0.0)
                            {
                                y1 = this.height;
                            }
                            else
                            {
                                y1 = this.height - (int)((Pow(fgValue) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            if (y1 > this.height)
                            {
                                y1 = this.height;
                            }
                            if (bgValue == 0.0)
                            {
                                y2 = this.height;
                            }
                            else
                            {
                                y2 = this.height - (int)((Pow(bgValue) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            if (y2 > this.height)
                            {
                                y2 = this.height;
                            }
                        }
                        else
                        {
                            if (fgValue == 0.0)
                            {
                                y1 = this.height;
                            }
                            else
                            {
                                y1 = this.height - (int)((Log10(fgValue) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            if (y1 > this.height)
                            {
                                y1 = this.height;
                            }
                            if (bgValue == 0.0)
                            {
                                y2 = this.height;
                            }
                            else
                            {
                                y2 = this.height - (int)((Log10(bgValue) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            if (y2 > this.height)
                            {
                                y2 = this.height;
                            }
                        }
                        if (y1 > y2)
                        {
                            int tmp = y1;
                            y1 = y2;
                            y2 = tmp;
                        }
                        if (i > this.left)
                        {
                            if (this.chartType == ChartType.BarChart)
                            {
                                g.FillRectangle(brush, i, y1, 1, y2 - y1);
                            }
                            else
                            {
                                g.FillRectangle(brush, i, y1, 1, y2 - y1 + 1);
                            }
                        }
                    }
                    i++;
                }
            }
        }

        private bool IsBackgroundVisible()
        {
            return (this.backgroundMode == BackgroundMode.Visible || this.backgroundMode == BackgroundMode.NormalizeByEfficiency)
                && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0;
        }

        String FormatAs10Power(decimal val)
        {
            if (val < 999) return val.ToString();
            string SuperscriptDigits = "\u2070\u00b9\u00b2\u00b3\u2074\u2075\u2076\u2077\u2078\u2079";
            string expstr = String.Format("{0:0.#E0}", val);

            string[] numparts = expstr.Split('E');
            char[] powerchars = numparts[1].ToArray();
            for (int i = 0; i < powerchars.Length; i++)
            {
                powerchars[i] = (powerchars[i] == '-') ? '\u207b' : SuperscriptDigits[powerchars[i] - '0'];
            }
            numparts[1] = new String(powerchars);
            return String.Join("\u00b710", numparts);
        }

        String FormatAs10Power(double val)
        {
            if (val > 0.01 && val < 999) return val.ToString();
            string SuperscriptDigits = "\u2070\u00b9\u00b2\u00b3\u2074\u2075\u2076\u2077\u2078\u2079";
            string expstr = String.Format("{0:0.#E0}", val);

            string[] numparts = expstr.Split('E');
            char[] powerchars = numparts[1].ToArray();
            for (int i = 0; i < powerchars.Length; i++)
            {
                powerchars[i] = (powerchars[i] == '-') ? '\u207b' : SuperscriptDigits[powerchars[i] - '0'];
            }
            numparts[1] = new String(powerchars);
            return String.Join("\u00b710", numparts);
        }

        // Token: 0x060004BE RID: 1214 RVA: 0x00019FEC File Offset: 0x000181EC
        void ShowVerticalAxis(Graphics g)
        {
            if (this.totalMinValue == double.PositiveInfinity)
            {
                this.totalMinValue = 0.0;
            }
            int num = this.CalcMaximumXValue() + this.scrollX + this.left;
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            using (Brush brush = new SolidBrush(colorConfig.AxisBackgroundColor.Color))
            {
                g.FillRectangle(brush, 0, 0, this.left, this.height);
            }
            if (this.width + this.left > num)
            {
                using (Brush brush2 = new SolidBrush(colorConfig.BlankAreaColor.Color))
                {
                    g.FillRectangle(brush2, num, 0, this.width + this.left - num, this.height);
                    g.DrawLine(Pens.Gray, num, 0, num, this.height);
                }
            }
            Rectangle clip = new Rectangle(0, 0, this.left + 1, this.height + 1);
            using (Pen pen = new Pen(colorConfig.AxisDivisionColor.Color))
            {
                using (Brush brush3 = new SolidBrush(colorConfig.AxisFigureColor.Color))
                {
                    using (Pen pen2 = new Pen(colorConfig.GridColor1.Color))
                    {
                        using (Pen pen3 = new Pen(colorConfig.GridColor2.Color))
                        {
                            if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
                            {
                                double num2 = Math.Pow(10.0, Math.Floor(this.totalMinValueLog));
                                double num3 = num2;
                                int num4 = this.height;
                                while (num2 < Math.Pow(10.0, this.totalMaxValueLog))
                                {
                                    for (int i = 2; i <= 10; i++)
                                    {
                                        num2 += num3;
                                        num4 = this.height - (int)((Log10(num2) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                        Rectangle r = new Rectangle(-20, num4 - 12, 20 + this.left - 3, 12);
                                        if (i != 10)
                                        {
                                            g.DrawLine(pen3, this.left + 1, num4, num - 1, num4);
                                            g.SetClip(clip);
                                            g.DrawLine(pen, this.left - 5, num4, this.left, num4);
                                            g.ResetClip();
                                        }
                                        else
                                        {
                                            g.DrawLine(pen2, this.left + 1, num4, num - 1, num4);
                                            g.SetClip(clip);
                                            g.DrawLine(pen, 0, num4, this.left, num4);
                                            g.DrawString(FormatAs10Power(num2), this.Font, brush3, r, this.farFormat);
                                            g.ResetClip();
                                        }
                                    }
                                    num3 *= 10.0;
                                }
                            }
                            else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                            {
                                double maxValue = this.totalMaxValue;
                                double iterator = Math.Pow(10.0, Math.Floor(Math.Log10(this.totalMinValue)));
                                if (iterator == 0)
                                {
                                    iterator = 1;
                                }
                                if (maxValue / iterator < 10.0)
                                {
                                    iterator = Math.Pow(10.0, Math.Floor(Math.Log10(maxValue / 10.0)));
                                }
                                double increment = iterator;
                                int resultAxis = this.height;
                                
                                while (iterator < maxValue)
                                {
                                    for (int i = 2; i <= 10; i++)
                                    {
                                        iterator += increment;
                                        resultAxis = this.height - (int)((Pow(iterator) - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                        if (resultAxis < 0.0) continue;
                                        Rectangle r = new Rectangle(-20, resultAxis - 12, 20 + this.left - 3, 12);
                                        if (i != 10)
                                        {
                                            //Младшие значения оси
                                            g.DrawLine(pen3, this.left + 1, resultAxis, num - 1, resultAxis);
                                            g.SetClip(clip);
                                            g.DrawLine(pen, this.left - 5, resultAxis, this.left, resultAxis);
                                            g.ResetClip();
                                        }
                                        else
                                        {
                                            //Старшие значения оси с цифрами
                                            g.DrawLine(pen2, this.left + 1, resultAxis, num - 1, resultAxis);
                                            g.SetClip(clip);
                                            g.DrawLine(pen, 0, resultAxis, this.left, resultAxis);
                                            g.DrawString(FormatAs10Power(iterator), this.Font, brush3, r, this.farFormat);
                                            g.ResetClip();
                                        }
                                    }
                                    increment *= 10.0;
                                }
                            }
                            else
                            {
                                double num5 = Math.Floor(Math.Log10(this.valueRange / this.verticalScale));
                                decimal d;
                                if (this.valueRange / Math.Pow(10.0, num5) > 5.0)
                                {
                                    d = (decimal)Math.Pow(10.0, num5);
                                }
                                else
                                {
                                    d = (decimal)(Math.Pow(10.0, num5) / 2.0);
                                }
                                decimal d2 = (decimal)Math.Pow(10.0, num5 - 1.0);
                                decimal num6 = (decimal)((-this.scrollBaseY - (double)this.scrollY) / this.verticalScale / (double)this.height * this.valueRange + this.totalMinValue);
                                if (d2 == 0)
                                {
                                    num6 = 0;
                                } else
                                {
                                    num6 = Math.Floor(num6 / d2) * d2;
                                }
                                double num7 = ((double)this.height - this.scrollBaseY - (double)this.scrollY) / this.verticalScale / (double)this.height * this.valueRange + this.totalMinValue;
                                while ((double)num6 <= num7)
                                {
                                    int num8 = this.height - (int)(((double)num6 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                    Rectangle r2 = new Rectangle(-20, num8 - 12, 20 + this.left - 3, 12);
                                    if (d != 0 && num6 % d != 0m)
                                    {
                                        g.DrawLine(pen3, this.left + 1, num8, num - 1, num8);
                                        g.SetClip(clip);
                                        g.DrawLine(pen, this.left - 5, num8, this.left, num8);
                                        g.ResetClip();
                                    }
                                    else
                                    {
                                        g.DrawLine(pen2, this.left + 1, num8, num - 1, num8);
                                        g.SetClip(clip);
                                        g.DrawLine(pen, 0, num8, this.left, num8);
                                        g.DrawString(FormatAs10Power(num6), this.Font, brush3, r2, this.farFormat);
                                        g.ResetClip();
                                    }
                                    num6 += d2;
                                }
                            }
                            g.DrawLine(pen, this.left, 0, this.left, this.height);
                        }
                    }
                }
            }
        }

        // Token: 0x060004BF RID: 1215 RVA: 0x0001A5F0 File Offset: 0x000187F0
        void ShowHorizontalAxis(Graphics g)
        {
            new Pen(Color.FromArgb(240, 240, 240));
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            using (Brush brush = new SolidBrush(colorConfig.AxisBackgroundColor.Color))
            {
                g.FillRectangle(brush, 0, this.height, this.width + this.left, this.bottom);
            }
            using (Pen pen = new Pen(colorConfig.AxisDivisionColor.Color))
            {
                using (Brush brush2 = new SolidBrush(colorConfig.AxisFigureColor.Color))
                {
                    using (Pen pen2 = new Pen(colorConfig.GridColor2.Color))
                    {
                        if (this.horizontalUnit == HorizontalUnit.Energy)
                        {
                            double num = this.energyCalibration.ChannelToEnergy(50.0 / this.horizontalScale) - this.energyCalibration.ChannelToEnergy(0.0);
                            double num2;
                            if (num > 100.0)
                            {
                                num2 = Math.Floor(num / 100.0) * 100.0;
                            }
                            else if (num > 20.0)
                            {
                                num2 = Math.Floor(num / 20.0) * 20.0;
                            }
                            else if (num > 5.0)
                            {
                                num2 = Math.Floor(num / 5.0) * 5.0;
                            }
                            else
                            {
                                num2 = 5.0;
                            }
                            double num3 = this.energyCalibration.ChannelToEnergy(0.0);
                            double num4 = this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels);
                            double num7;
                            for (double num5 = num3; num5 < num4; num5 = num7)
                            {
                                int num6 = (int)((num5 - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + this.scrollX + this.left;
                                g.DrawLine(pen, num6, this.height, num6, base.ClientSize.Height);
                                g.DrawString(num5.ToString("f0"), this.Font, brush2, (float)num6, (float)(this.height + 1));
                                if (num6 > this.left)
                                {
                                    g.DrawLine(pen2, num6, 0, num6, this.height - 1);
                                }
                                num7 = (Math.Floor(num5 / num2) + 1.0) * num2;
                                if (num7 - this.energyCalibration.ChannelToEnergy(0.0) < num2 / 2.0)
                                {
                                    num7 += num2;
                                }
                            }
                        }
                        else if (this.horizontalUnit == HorizontalUnit.Channel)
                        {
                            int num8 = (int)(50.0 / this.horizontalScale);
                            int num9;
                            if (num8 > 100)
                            {
                                num9 = num8 / 100 * 100;
                            }
                            else if (num8 > 20)
                            {
                                num9 = num8 / 20 * 20;
                            }
                            else if (num8 > 5)
                            {
                                num9 = num8 / 5 * 5;
                            }
                            else
                            {
                                num9 = 5;
                            }
                            for (int i = 0; i < this.totalMaxChannel; i += num9)
                            {
                                int num10 = (int)((double)i * this.horizontalScale) + this.scrollX + this.left;
                                g.DrawLine(pen, num10, this.height, num10, base.ClientSize.Height);
                                g.DrawString(i.ToString(), this.Font, brush2, (float)num10, (float)(this.height + 1));
                                if (num10 > this.left)
                                {
                                    g.DrawLine(pen2, num10, 0, num10, this.height - 1);
                                }
                            }
                        }
                        g.DrawLine(pen, 0, this.height, this.width + this.left, this.height);
                    }
                }
            }
        }

        // Token: 0x060004C0 RID: 1216 RVA: 0x0001AA58 File Offset: 0x00018C58
        void ShowDetectedPeaks(Graphics g, EnergySpectrum spectrum, EnergyCalibration calibration)
        {
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            Pen pen = new Pen(colorConfig.PeakLineColor.Color);
            Pen pen2 = new Pen(colorConfig.PeakFigureColor.Color);
            Brush brush = new SolidBrush(colorConfig.PeakFigureColor.Color);
            Brush brush2 = new SolidBrush(colorConfig.PeakBackgroundColor.Color);
            Peak peak = null;
            Peak peak2 = null;
            int num = 0;
            foreach (Peak peak3 in this.activeResultData.DetectedPeaks)
            {
                int channel = peak3.Channel;
                int num2;
                if (this.horizontalUnit == HorizontalUnit.Channel)
                {
                    num2 = (int)(((double)channel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    double num3 = this.energyCalibration.ChannelToEnergy((double)channel);
                    num2 = (int)(((num3 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale + (double)this.scrollX + (double)this.left);
                }
                if (this.cursorX >= num && this.cursorX < num2)
                {
                    peak = peak2;
                }
                num = num2;
                peak2 = peak3;
            }
            if (this.cursorX >= num)
            {
                peak = peak2;
            }
            foreach (Peak peak4 in this.activeResultData.DetectedPeaks)
            {
                int channel2 = peak4.Channel;
                int num4;
                if (this.horizontalUnit == HorizontalUnit.Channel)
                {
                    num4 = (int)(((double)channel2 + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    double num5 = this.energyCalibration.ChannelToEnergy((double)channel2);
                    num4 = (int)(((num5 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale + (double)this.scrollX + (double)this.left);
                }
                double num6 = 0.0;
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0
                    && this.substractedEnergySpectrum != null)
                {
                    num6 = this.substractedEnergySpectrum.DrawingSpectrum[channel2];
                } else
                {
                    num6 = spectrum.DrawingSpectrum[channel2];
                }
                if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.MeasurementTime != 0.0)
                {
                    num6 /= spectrum.MeasurementTime;
                }
                int y;
                if (this.verticalScaleType == VerticalScaleType.LinearScale)
                {
                    if (num6 <= 0.0)
                    {
                        y = this.height;
                    }
                    else
                    {
                        y = this.height - (int)((num6 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    double num7 = Pow(num6);
                    if (num6 == 0.0)
                    {
                        y = this.height + 100;
                    }
                    else
                    {
                        y = this.height - (int)((num7 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else
                {
                    double num7 = Log10(num6);
                    if (num6 == 0.0)
                    {
                        y = this.height + 100;
                    }
                    else
                    {
                        y = this.height - (int)((num7 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                if (num4 > this.left)
                {
                    g.DrawLine(pen, num4, 12, num4, y);
                }
                if (peak4 != peak)
                {
                    this.DrawPeakFlag(g, peak4, num4, 2, pen2, brush, brush2);
                }
            }
            if (peak != null)
            {
                int channel3 = peak.Channel;
                int px;
                if (this.horizontalUnit == HorizontalUnit.Channel)
                {
                    px = (int)(((double)channel3 + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    double num8 = this.energyCalibration.ChannelToEnergy((double)channel3);
                    px = (int)(((num8 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale + (double)this.scrollX + (double)this.left);
                }
                this.DrawPeakFlag(g, peak, px, 0, pen2, brush, brush2);
            }
            pen.Dispose();
            pen2.Dispose();
            brush.Dispose();
            brush2.Dispose();
        }

        // Token: 0x060004C1 RID: 1217 RVA: 0x0001AE94 File Offset: 0x00019094
        void DrawPeakFlag(Graphics g, Peak peak, int px, int py, Pen outlinePen, Brush figureBrush, Brush bgBrush)
        {
            string text = Resources.UnknownNuclide;
            if (peak.Nuclide != null)
            {
                text = peak.Nuclide.Name;
            }
            int num = (int)g.MeasureString(text, this.Font).Width;
            if (num < 50)
            {
                num = 50;
            }
            Point[] points = new Point[]
            {
                new Point(px, py + 10),
                new Point(px, py + 14),
                new Point(px + 1, py + 15),
                new Point(px + num + 10, py + 15),
                new Point(px + num + 11, py + 14),
                new Point(px + num + 11, py + 1),
                new Point(px + num + 10, py),
                new Point(px + 10, py)
            };
            g.FillPolygon(bgBrush, points);
            g.DrawPolygon(outlinePen, points);
            Rectangle r = new Rectangle(px + 10, py + 2, num + 2, 16);
            g.DrawString(text, this.Font, figureBrush, r);
        }

        // Token: 0x060004C2 RID: 1218 RVA: 0x0001AFF8 File Offset: 0x000191F8
        void ShowCalibrationPeaks(Graphics g, EnergySpectrum spectrum, EnergyCalibration calibration)
        {
            Pen pen = new Pen(Color.Red);
            foreach (Peak peak in this.activeResultData.CalibrationPeaks)
            {
                int channel = peak.Channel;
                int num;
                if (this.horizontalUnit == HorizontalUnit.Channel)
                {
                    num = (int)(((double)channel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    double num2 = this.energyCalibration.ChannelToEnergy((double)channel);
                    num = (int)(((num2 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale + (double)this.scrollX + (double)this.left);
                }
                double num3 = spectrum.DrawingSpectrum[channel];
                if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.MeasurementTime != 0.0)
                {
                    num3 /= spectrum.MeasurementTime;
                }
                int y;
                if (this.verticalScaleType == VerticalScaleType.LinearScale)
                {
                    if (num3 <= 0.0)
                    {
                        y = this.height;
                    }
                    else
                    {
                        y = this.height - (int)((num3 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else if (this.verticalScaleType == VerticalScaleType.PowerScale)
                {
                    double num4 = Pow(num3);
                    if (num3 == 0.0)
                    {
                        y = this.height + 100;
                    }
                    else
                    {
                        y = this.height - (int)((num4 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                else
                {
                    double num4 = Log10(num3);
                    if (num3 == 0.0)
                    {
                        y = this.height + 100;
                    }
                    else
                    {
                        y = this.height - (int)((num4 - this.totalMinValuePow) / this.valueRangePow * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                    }
                }
                if (num > this.left)
                {
                    g.DrawLine(Pens.Orange, num, 5, num, y);
                }
                int leftChannel = peak.LeftChannel;
                int rightChannel = peak.RightChannel;
                double num5 = this.energyCalibration.ChannelToEnergy((double)leftChannel);
                double num6 = this.energyCalibration.ChannelToEnergy((double)rightChannel);
                int num7;
                int num8;
                if (this.horizontalUnit == HorizontalUnit.Channel)
                {
                    num7 = (int)(((double)leftChannel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                    num8 = (int)(((double)rightChannel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                }
                else
                {
                    num7 = (int)(((num5 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale + (double)this.scrollX + (double)this.left);
                    num8 = (int)(((num6 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale + (double)this.scrollX + (double)this.left);
                }
                g.FillRectangle(Brushes.Orange, num7, 5, num8 - num7 + 1, 2);
                g.DrawLine(Pens.Orange, num7, 3, num7, 8);
                g.DrawLine(Pens.Orange, num8, 3, num8, 8);
            }
            pen.Dispose();
        }

        // Token: 0x060004C3 RID: 1219 RVA: 0x0001B334 File Offset: 0x00019534
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();

        void ShowCursorValues(Graphics g)
        {
            string intFormat = "n0";
            string floatFormat = "n2";
            string preciseFloatFormat = "n4";
            bool normalizeByEfficiency = this.backgroundMode == BackgroundMode.NormalizeByEfficiency;
            double fg_time = this.activeResultData.EnergySpectrum.MeasurementTime;
            double bg_time = this.activeResultData.BackgroundEnergySpectrum != null
                ? this.activeResultData.BackgroundEnergySpectrum.MeasurementTime
                : 0.0;

            int table_width_origin = 230;
            int channel_table_x_pos;
            int region_table_x_pos;
            bool isRegionSelected = this.selectionStart != -1 && this.selectionEnd != -1;
            if (this.cursorX < base.Width - (table_width_origin * 2 + 40))
            {
                region_table_x_pos = this.left + this.width - table_width_origin - 10;
                channel_table_x_pos = isRegionSelected 
                    ? region_table_x_pos - table_width_origin - 10
                    : region_table_x_pos;
            }
            else
            {
                region_table_x_pos = this.left + 10;
                channel_table_x_pos = isRegionSelected
                    ? region_table_x_pos + table_width_origin + 10
                    : region_table_x_pos;
            }
            int table_y_pos = 10;
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            if (this.validCursor && this.cursorChannel >= 0 &&
                this.cursorChannel < this.energySpectrum.NumberOfChannels &&
                this.energySpectrum.NumberOfChannels > Math.Max(this.selectionStart, this.selectionEnd))
            {
                using (Pen pen = new Pen(colorConfig.CursorColor.Color))
                {
                    pen.DashStyle = DashStyle.Dash;
                    int num4;
                    if (this.horizontalUnit == HorizontalUnit.Channel)
                    {
                        num4 = (int)(((double)this.cursorChannel + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                    }
                    else
                    {
                        double num5 = this.energyCalibration.ChannelToEnergy((double)this.cursorChannel);
                        num4 = (int)(((num5 - this.energyViewOffset) * this.pixelPerEnergy + 0.5) * this.horizontalScale) + this.scrollX + this.left;
                    }
                    g.DrawLine(pen, num4, 0, num4, this.height);
                    int percent = (int)this.globalConfigManager.GlobalConfig.ChartViewConfig.EnergyPercent;
                    int pitch = (int)this.globalConfigManager.GlobalConfig.ChartViewConfig.EnergyPitch;
                    double best_Energy = 0;
                    NuclideDefinition best_Nuclide = new NuclideDefinition();
                    foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
                    {
                        if (!nuclideDefinition.Visible) continue;
                        if (this.cursorEnergy >= nuclideDefinition.Energy - ((double)pitch + nuclideDefinition.Energy * (double)percent / 100.0) &&
                            this.cursorEnergy <= nuclideDefinition.Energy + ((double)pitch + nuclideDefinition.Energy * (double)percent / 100.0))
                        {
                            if (best_Energy == 0)
                            {
                                best_Energy = nuclideDefinition.Energy;
                                best_Nuclide = nuclideDefinition;
                            } else
                            {
                                if (Math.Abs(this.cursorEnergy - best_Energy) > Math.Abs(this.cursorEnergy - nuclideDefinition.Energy))
                                {
                                    best_Energy = nuclideDefinition.Energy;
                                    best_Nuclide = nuclideDefinition;
                                }
                            }
                        }
                    }
                    if (best_Energy != 0)
                    {
                        Peak peak = new Peak();
                        peak.Energy = best_Energy;
                        peak.Nuclide = best_Nuclide;
                        Pen pen2 = new Pen(colorConfig.PeakFigureColor.Color);
                        Brush brush = new SolidBrush(colorConfig.PeakFigureColor.Color);
                        Brush brush2 = new SolidBrush(colorConfig.PeakBackgroundColor.Color);
                        this.DrawPeakFlag(g, peak, this.cursorX, 2, pen2, brush, brush2);
                    }
                }
                int num6 = 62;
                if (this.backgroundEnergySpectrum != null && this.backgroundMode != BackgroundMode.Substract)
                {
                    num6 += 32;
                }
                g.FillRectangle(Brushes.DarkGray, channel_table_x_pos, table_y_pos, table_width_origin, num6);
                g.FillRectangle(Brushes.White, channel_table_x_pos - 3, table_y_pos - 3, table_width_origin, num6);
                g.DrawRectangle(Pens.Black, channel_table_x_pos - 3, table_y_pos - 3, table_width_origin, num6);
                Rectangle r = new Rectangle(channel_table_x_pos + 5, table_y_pos + 4, table_width_origin - 12, 32);
                g.DrawString(Resources.ChartHeaderChannel, this.Font, Brushes.Black, r);
                g.DrawString(this.cursorChannel.ToString(intFormat), this.Font, Brushes.Black, r, this.farFormat);
                r.Y += 16;
                double channelEnergy = this.energyCalibration.ChannelToEnergy((double)this.cursorChannel);
                g.DrawString(Resources.ChartHeaderEnergy, this.Font, Brushes.Black, r);
                g.DrawString(channelEnergy.ToString(floatFormat), this.Font, Brushes.Black, r, this.farFormat);
                r.Y += 22;
                g.DrawLine(Pens.LightGray, r.Left, r.Top - 6, r.Right, r.Top - 6);
                int channelGrossCounts = 0;
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    channelGrossCounts = this.substractedEnergySpectrum.Spectrum[this.cursorChannel];
                } 
                else
                {
                    channelGrossCounts = normalizeByEfficiency
                        ? this.normByEffEnergySpectrum.Spectrum[this.cursorChannel]
                        : this.energySpectrum.Spectrum[this.cursorChannel];
                }
                    
                g.DrawString(Resources.ChartHeaderGrossCounts, this.Font, Brushes.Black, r);
                g.DrawString(channelGrossCounts.ToString(floatFormat), this.Font, Brushes.Black, r, this.farFormat);
                if (this.backgroundEnergySpectrum != null && this.backgroundMode != BackgroundMode.Substract)
                {
                    double adjBgChannelCounts = 0.0;
                    if (bg_time > 0)
                    {
                        int bgChannelIndex;
                        if (this.backgroundEnergyCalibration.Equals(this.baseEnergyCalibration))
                        {
                            bgChannelIndex = this.cursorChannel;
                        }
                        else
                        {
                            bgChannelIndex = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy((double)this.cursorChannel), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                        }
                        if (bgChannelIndex >= 0 && bgChannelIndex < this.backgroundNumberOfChannels)
                        {
                            adjBgChannelCounts = normalizeByEfficiency
                                ? (double)this.normByEffBgEnergySpectrum.Spectrum[bgChannelIndex]
                                : (double)this.backgroundEnergySpectrum.Spectrum[bgChannelIndex];
                            adjBgChannelCounts *= fg_time / bg_time;
                        }
                    }
                    r.Y += 16;
                    g.DrawString(Resources.ChartHeaderBGCounts, this.Font, Brushes.Black, r);
                    g.DrawString(adjBgChannelCounts.ToString(floatFormat), this.Font, Brushes.Black, r, this.farFormat);
                    r.Y += 16;
                    if (adjBgChannelCounts != 0.0)
                    {
                        double num11 = (double)channelGrossCounts / adjBgChannelCounts * 100.0;
                        g.DrawString(Resources.ChartHeaderCountBGRatio, this.Font, Brushes.Black, r);
                        g.DrawString(num11.ToString(floatFormat) + Resources.PercentCharacter, this.Font, Brushes.Black, r, this.farFormat);
                    }
                }
            }
            if (this.selectionStart != -1 && this.energySpectrum.NumberOfChannels > Math.Max(this.selectionStart, this.selectionEnd))
            {
                // calculation data
                int start_channel;
                int end_channel;
                if (this.selectionStart < this.selectionEnd)
                {
                    start_channel = this.selectionStart;
                    end_channel = this.selectionEnd;
                }
                else
                {
                    start_channel = this.selectionEnd;
                    end_channel = this.selectionStart;
                }
                double start_energy = this.energyCalibration.ChannelToEnergy((double)start_channel);
                double end_energy = this.energyCalibration.ChannelToEnergy((double)end_channel);
                double fg_counts = 0.0;
                double fg_eff_norm_counts = 0.0;
                double bg_counts = 0.0;
                double bg_eff_norm_counts = 0.0;

                // display data
                double gross_counts = 0.0;
                double adj_bg_counts = 0.0;
                double net_counts = 0.0;
                double peakcounts = 0.0;
                double net_cps = 0.0;
                double net_cps_err = 0.0;
                double net_counts_err = 0.0;
                double Lc = 0.0;
                double Lu = 0.0;
                double Ld = 0.0;
                double mda = 0.0;
                double activity = 0.0;
                double activityError = 0.0;
                double activityUpperLimit = 0.0;
                double activityByMass = 0.0;
                double activityByMassError = 0.0;
                double activityByMassUpperLimit = 0.0;
                double activityByVolume = 0.0;
                double activityByVolumeError = 0.0;
                double activityByVolumeUpperLimit = 0.0;

                int[] sourceSpectrum = normalizeByEfficiency
                    ? this.normByEffEnergySpectrum.Spectrum
                    : (BackgroundMode == BackgroundMode.Substract)
                        ? this.substractedEnergySpectrum.Spectrum
                        : this.energySpectrum.Spectrum;


                for (int i = start_channel; i <= end_channel; i++)
                {
                    fg_counts += sourceSpectrum[i];
                    if (normalizeByEfficiency)
                    {
                        fg_eff_norm_counts += (double)this.normByEffEnergySpectrum.Spectrum[i];
                    }
                    int fg_counts_in_channel = sourceSpectrum[i];

                    int continuumFrom = sourceSpectrum[start_channel];
                    int continuumTo = sourceSpectrum[end_channel];
                    double continuum = SpectrumAriphmetics.getY(i, start_channel, end_channel, continuumFrom, continuumTo);

                    if (bg_time > 0 && BackgroundMode != BackgroundMode.Substract)
                    {
                        double adj_bg_counts_in_channel = 0.0;
                        int bg_channel = i;
                        if (!this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                        {
                            bg_channel = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy((double)i), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                        }
                        if (bg_channel >= 0 && bg_channel < this.backgroundNumberOfChannels)
                        {
                            bg_counts += (double)this.backgroundEnergySpectrum.Spectrum[bg_channel];
                            if (normalizeByEfficiency)
                            {
                                bg_eff_norm_counts += (double)this.normByEffBgEnergySpectrum.Spectrum[bg_channel];
                            }

                            adj_bg_counts_in_channel = normalizeByEfficiency
                                ? (double)this.normByEffBgEnergySpectrum.Spectrum[bg_channel]
                                : (double)this.backgroundEnergySpectrum.Spectrum[bg_channel];
                            adj_bg_counts_in_channel *= fg_time / bg_time;
                        }
                        continuum = Math.Max(adj_bg_counts_in_channel, continuum);
                    }
                    
                    // if fg counts below continuum, peak counts in this channel = 0
                    if (fg_counts_in_channel > continuum) peakcounts += (fg_counts_in_channel - continuum);
                }

                gross_counts = normalizeByEfficiency
                    ? fg_eff_norm_counts
                    : fg_counts;
                if (bg_time > 0)
                {
                    adj_bg_counts = normalizeByEfficiency
                        ? bg_eff_norm_counts
                        : bg_counts;
                    adj_bg_counts *= fg_time / bg_time;
                }

                if (fg_time > 0)
                {
                    double detectionLevel = (double)this.globalConfigManager.GlobalConfig.MeasurementConfig.DetectionLevel;
                    double errorLevel = (double)this.globalConfigManager.GlobalConfig.MeasurementConfig.ErrorLevel;
                    double limitsConfidenceLevel = (double)this.globalConfigManager.GlobalConfig.ChartViewConfig.ConfidenceLevel;

                    if (bg_time > 0)
                    {
                        net_counts = ROIAriphmetics.CalculateNetCount(fg_counts, fg_time, bg_counts, bg_time);
                        net_counts_err = ROIAriphmetics.CalculateNetCountError(fg_counts, fg_time, bg_counts, bg_time, errorLevel);

                        if (net_counts > 0)
                        {
                            Lc = ROIAriphmetics.CalculateLc(bg_counts, bg_time, fg_time, limitsConfidenceLevel);
                            Lu = ROIAriphmetics.CalculateLu(fg_counts, fg_time, bg_counts, bg_time, limitsConfidenceLevel);
                            Ld = ROIAriphmetics.CalculateLd(bg_counts, bg_time, fg_time, limitsConfidenceLevel);
                            mda = ROIAriphmetics.CalculateMDACounts(bg_counts, bg_time, fg_time, detectionLevel);

                            if (normalizeByEfficiency)
                            {
                                double eff_norm_net_counts = ROIAriphmetics.CalculateNetCount(fg_eff_norm_counts, fg_time, bg_eff_norm_counts, bg_time);
                                double adj = eff_norm_net_counts / net_counts;
                                Lc *= adj;
                                Lu *= adj;
                                Ld *= adj;
                                mda *= adj;
                                net_counts *= adj;
                                net_counts_err *= adj;
                            }

                            // calc activity
                            if (this.peakMode == PeakMode.Visible && this.selectionFWHM > 0.0 &&
                                this.activeResultData.Visible &&
                                this.roiConfig != null && 
                                this.roiConfig.HasEfficiency)
                            {
                                int number_of_peaks = 0;
                                Peak detected_peak = null;
                                foreach (Peak peak in this.activeResultData.DetectedPeaks)
                                {
                                    if (start_energy < peak.Energy && end_energy > peak.Energy)
                                    {
                                        number_of_peaks++;
                                        detected_peak = peak;
                                    }
                                }
                                if (number_of_peaks == 1 && detected_peak != null && detected_peak.Nuclide != null && detected_peak.Nuclide.Intencity > 0)
                                {
                                    ROIAriphmetics roiAriphmetics = new ROIAriphmetics(this.roiConfig);
                                    ROIEfficiencyData effData = roiAriphmetics.CalculateEfficiency(detected_peak.Energy);
                                    if (effData != null && effData.Efficiency > 0)
                                    {
                                        double bqCoeff = (1 / effData.Efficiency) / (detected_peak.Nuclide.Intencity / 100.0);
                                        double bqCoeffError = effData.ErrorPercent > 0
                                            ? bqCoeff * (effData.ErrorPercent / 100)
                                            : 0;

                                        activity = ROIAriphmetics.CalculateActivity(bqCoeff, fg_counts, fg_time, bg_counts, bg_time);
                                        activityError = ROIAriphmetics.CalculateActivityError(bqCoeff, bqCoeffError, fg_counts, fg_time, bg_counts, bg_time, errorLevel);
                                        activityUpperLimit = ROIAriphmetics.CalculateActivityUpperLimit(bqCoeff, bqCoeffError, fg_counts, fg_time, bg_counts, bg_time, limitsConfidenceLevel);
                                        if (this.activeResultData.SampleInfo.Weight > 0)
                                        {
                                            activityByMass = activity / this.activeResultData.SampleInfo.Weight;
                                            activityByMassError = activityError / this.activeResultData.SampleInfo.Weight;
                                            activityByMassUpperLimit = activityUpperLimit / this.activeResultData.SampleInfo.Weight;
                                        }

                                        if (this.activeResultData.SampleInfo.Volume > 0)
                                        {
                                            activityByVolume = activity / this.activeResultData.SampleInfo.Volume;
                                            activityByVolumeError = activityError / this.activeResultData.SampleInfo.Volume;
                                            activityByVolumeUpperLimit = activityUpperLimit / this.activeResultData.SampleInfo.Volume;
                                        }
                                    }
                                } 
                            }
                        }
                    }
                    else
                    {
                        net_counts = ROIAriphmetics.CalculateNetCount(fg_counts, fg_time, 0, 0);
                        net_counts_err = ROIAriphmetics.CalculateNetCountError(fg_counts, fg_time, 0, 0, errorLevel);
                        if (normalizeByEfficiency)
                        {
                            double eff_norm_net_counts = ROIAriphmetics.CalculateNetCount(fg_eff_norm_counts, fg_time, 0, 0);
                            double adj = eff_norm_net_counts / net_counts;
                            net_counts *= adj;
                            net_counts_err *= adj;
                        }
                    }

                    net_cps = net_counts / fg_time;
                    net_cps_err = net_counts_err / fg_time;
                }
                int infopanel_height = 104;
                if (this.selectionFWHM > 0.0)
                {
                    infopanel_height += 46;
                }
                if (Lc > 0 && net_counts < Lc) {
                    infopanel_height += 88;
                }
                if (Lc > 0 && net_counts > Lc)
                {
                    infopanel_height += 72;
                }
                if (adj_bg_counts > 0)
                {
                    infopanel_height += 48;
                }
                if (this.selectionFWHM > 0.0 && activity > 0.0)
                {
                    infopanel_height += 54;
                }
                g.FillRectangle(Brushes.DarkGray, region_table_x_pos, table_y_pos, table_width_origin, infopanel_height);
                g.FillRectangle(Brushes.White, region_table_x_pos - 3, table_y_pos - 3, table_width_origin, infopanel_height);
                g.DrawRectangle(Pens.Black, region_table_x_pos - 3, table_y_pos - 3, table_width_origin, infopanel_height);
                Rectangle r2 = new Rectangle(region_table_x_pos + 5, table_y_pos + 4, table_width_origin - 12, 32);
                g.DrawString(normalizeByEfficiency ? Resources.ChartHeaderSelectionEffNormalized : Resources.ChartHeaderSelection, this.Font, Brushes.Black, r2);
                r2.Y += 22;
                g.DrawLine(Pens.LightGray, r2.Left, r2.Top - 6, r2.Right, r2.Top - 6);
                g.DrawString(Resources.ChartHeaderChannel, this.Font, Brushes.Black, r2);
                g.DrawString(start_channel.ToString(intFormat) + " - " + end_channel.ToString(intFormat), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                g.DrawString(Resources.ChartHeaderEnergy, this.Font, Brushes.Black, r2);
                g.DrawString(start_energy.ToString(floatFormat) + " - " + end_energy.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 22;
                g.DrawLine(Pens.LightGray, r2.Left, r2.Top - 6, r2.Right, r2.Top - 6);
                g.DrawString(Resources.ChartHeaderGrossCounts, this.Font, Brushes.Black, r2);
                g.DrawString(gross_counts.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                if (adj_bg_counts != 0.0)
                {
                    r2.Y += 16;
                    g.DrawString(Resources.ChartHeaderBGCounts, this.Font, Brushes.Black, r2);
                    g.DrawString(adj_bg_counts.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    g.DrawString(Resources.ChartHeaderCountBGRatio, this.Font, Brushes.Black, r2);
                    double bg_ratio = gross_counts / adj_bg_counts * 100.0;
                    g.DrawString(bg_ratio.ToString(floatFormat) + Resources.PercentCharacter, this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    g.DrawString(Resources.ChartHeaderNetCps, this.Font, Brushes.Black, r2);
                    if (net_cps_err != 0.0)
                    {
                        g.DrawString(net_cps.ToString(preciseFloatFormat) + " " + Resources.PlusMinus + net_cps_err.ToString(preciseFloatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    }
                    else
                    {
                        g.DrawString(net_cps.ToString(preciseFloatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    }
                    r2.Y += 16;
                    g.DrawString(Resources.ChartHeaderNetCounts, this.Font, Brushes.Black, r2);
                    if (net_counts_err != 0.0)
                    {
                        g.DrawString(net_counts.ToString(floatFormat) + " " + Resources.PlusMinus + net_counts_err.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    }
                    else
                    {
                        g.DrawString(net_counts.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    }
                } else
                {
                    r2.Y += 16;
                    g.DrawString(Resources.ChartHeaderCPS, this.Font, Brushes.Black, r2);
                    g.DrawString(net_cps.ToString(preciseFloatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                }
                r2.Y += 22;
                if (Lc > 0)
                {
                    g.DrawLine(Pens.LightGray, r2.Left, r2.Top - 6, r2.Right, r2.Top - 6);
                    if (net_counts < Lc)
                    {
                        g.DrawString(Resources.NotDetected, this.Font, Brushes.DarkRed, r2, this.centerFormat);
                    } else if (net_counts > Lc && net_counts < Ld)
                    {
                        g.DrawString(Resources.DetectedWithUncertain, this.Font, Brushes.DarkOrange, r2, this.centerFormat);
                    } else
                    {
                        g.DrawString(Resources.Detected, this.Font, Brushes.DarkGreen, r2, this.centerFormat);
                    }
                    r2.Y += 16;
                    g.DrawString(Resources.Lc_counts, this.Font, Brushes.Black, r2);
                    g.DrawString(Lc.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    if (net_counts < Lc)
                    {
                        g.DrawString(Resources.Lu_counts, this.Font, Brushes.Black, r2);
                        g.DrawString(Lu.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                        r2.Y += 16;
                    }
                    string confidencelevel_str = ConfidenceLevel.GetSingleSideLevel(this.globalConfigManager.GlobalConfig.ChartViewConfig.ConfidenceLevel);
                    g.DrawString(Resources.Ld_counts + " (" + confidencelevel_str + ")", this.Font, Brushes.Black, r2);
                    g.DrawString(Ld.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    g.DrawString(Resources.MDA_cnts, this.Font, Brushes.Black, r2);
                    g.DrawString(mda.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    if (this.selectionFWHM > 0.0 && activity > 0.0)
                    {
                        if (net_counts < Lc)
                        {
                            Brush brush = Brushes.DarkRed;
                            g.DrawString(Resources.Activity + " " + Resources.Bq + ":", this.Font, brush, r2);
                            g.DrawString("< " + activityUpperLimit.ToString(floatFormat),
                                this.Font, brush, r2, this.farFormat);
                            r2.Y += 16;

                            g.DrawString(Resources.Activity + " " + Resources.Bqkg + ":", this.Font, brush, r2);
                            g.DrawString("< " + activityByMassUpperLimit.ToString(floatFormat),
                            this.Font, brush, r2, this.farFormat);
                            r2.Y += 16;

                            g.DrawString(Resources.Activity + " " + Resources.Bql + ":", this.Font, brush, r2);
                            g.DrawString("< " + activityByVolumeUpperLimit.ToString(floatFormat),
                                this.Font, brush, r2, this.farFormat);
                            r2.Y += 16;
                        } 
                        else
                        {
                            Brush brush = Brushes.Black;
                            g.DrawString(Resources.Activity + " " + Resources.Bq + ":", this.Font, brush, r2);
                            g.DrawString(activity.ToString(floatFormat) + " " + Resources.PlusMinus + activityError.ToString(floatFormat),
                                this.Font, brush, r2, this.farFormat);
                            r2.Y += 16;

                            g.DrawString(Resources.Activity + " " + Resources.Bqkg + ":", this.Font, brush, r2);
                            g.DrawString(activityByMass.ToString(floatFormat) + " " + Resources.PlusMinus + activityByMassError.ToString(floatFormat),
                            this.Font, brush, r2, this.farFormat);
                            r2.Y += 16;

                            g.DrawString(Resources.Activity + " " + Resources.Bql + ":", this.Font, brush, r2);
                            g.DrawString(activityByVolume.ToString(floatFormat) + " " + Resources.PlusMinus + activityByVolumeError.ToString(floatFormat),
                                this.Font, brush, r2, this.farFormat);
                            r2.Y += 16;
                        }
                    }
                    r2.Y += 6;
                }
                if (this.selectionFWHM > 0.0)
                {
                    g.DrawLine(Pens.LightGray, r2.Left, r2.Top - 6, r2.Right, r2.Top - 6);
                    g.DrawString(Resources.ChartHeaderPeakCounts, this.Font, Brushes.Black, r2);
                    g.DrawString(peakcounts.ToString(floatFormat), this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    g.DrawString(Resources.ChartHeaderFWHM, this.Font, Brushes.Black, r2);
                    g.DrawString((this.selectionFWHM * 100.0).ToString(floatFormat) + Resources.PercentCharacter +
                        " (" + (this.selectionFWHMinkev).ToString(floatFormat) + " " + Resources.kev + ", " + this.selectionFullWidth.ToString(intFormat) + " " + Resources.ChartChannelShort + ")",
                        this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    //g.DrawString(Resources._2Sigma, this.Font, Brushes.Black, r2);
                    //g.DrawString((this.selectionFullWidth).ToString() + " " + Resources.ChartChannelShort,
                    //    this.Font, Brushes.Black, r2, this.farFormat);
                    //r2.Y += 16;
                    g.DrawString(Resources.Centroid, this.Font, Brushes.Black, r2);
                    g.DrawString((this.selectionCentroidkeV).ToString(floatFormat) + " " + Resources.kev +
                        " (" + (this.selectionCentroidCh).ToString(intFormat) + " " + Resources.ChartChannelShort + ")",
                        this.Font, Brushes.Black, r2, this.farFormat);

                }
            }
        }

        double Log10(double x)
        {
            //if (x < 1) return 0.0;
            return Math.Log10(x);
        }

        double Pow(double x)
        {
            //if (x < 1) return 0.0;
            return Math.Pow(x, 1 / pownum);
        }

        // Token: 0x060004C4 RID: 1220 RVA: 0x0001BD50 File Offset: 0x00019F50
        void EnergySpectrumView_SizeChanged(object sender, EventArgs e)
        {
            this.RecalcChartParameters();
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }

        // Token: 0x060004C5 RID: 1221 RVA: 0x0001BD6C File Offset: 0x00019F6C
        public void RecalcChartParameters()
        {
            if (this.energyCalibration != null)
            {
                PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)this.energyCalibration;
                this.pixelPerEnergy = 1.0 / polynomialEnergyCalibration.Coefficients[1];
                this.energyViewOffset = polynomialEnergyCalibration.Coefficients[0];
            }
        }

        // Token: 0x060004C6 RID: 1222 RVA: 0x0001BDBC File Offset: 0x00019FBC
        void hScrollBar1_ValueChanged(object sender, EventArgs e)
        {
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }

        // Token: 0x060004C7 RID: 1223 RVA: 0x0001BDD0 File Offset: 0x00019FD0
        void vScrollBar1_ValueChanged(object sender, EventArgs e)
        {
            base.Invalidate();
        }

        // Token: 0x060004C8 RID: 1224 RVA: 0x0001BDD8 File Offset: 0x00019FD8
        void EnergySpectrumView_MouseMove(object sender, MouseEventArgs e)
        {
            int x = e.X;
            if (this.cursorX != x)
            {
                this.cursorX = x;
                this.lastCursorX = x;
                int num = this.cursorChannel;
                if (this.horizontalUnit == HorizontalUnit.Energy)
                {
                    double num2 = (double)(this.cursorX - this.left - this.scrollX) / this.horizontalScale;
                    this.cursorEnergy = num2 / this.pixelPerEnergy + this.energyViewOffset;
                    try
                    {
                        this.cursorChannel = (int)this.energyCalibration.EnergyToChannel(this.cursorEnergy, maxChannels: this.energySpectrum.NumberOfChannels);
                    }
                    catch (OutofChannelException)
                    {
                        this.cursorChannel = -1;
                    }
                }
                else
                {
                    this.cursorChannel = (int)((double)(this.cursorX - this.left - this.scrollX) / this.horizontalScale);
                    this.cursorEnergy = this.energyCalibration.ChannelToEnergy((double)this.CursorChannel);
                }

                if (this.selectionDragging && this.cursorChannel >= 0 && this.cursorChannel <= this.energySpectrum.NumberOfChannels - 1)
                {
                    this.selectionEnd = this.cursorChannel;
                    this.selectionEndEnergy = this.cursorEnergy;
                }
                if (num != this.cursorChannel)
                {
                    base.Invalidate();
                }
            }
        }

        // Token: 0x060004C9 RID: 1225 RVA: 0x0001BF1C File Offset: 0x0001A11C
        void EnergySpectrumView_MouseLeave(object sender, EventArgs e)
        {
            if (this.contextMenuOpening)
            {
                this.contextMenuOpening = false;
            }
            else
            {
                this.cursorX = -1;
            }
            base.Invalidate();
        }

        // Token: 0x060004CA RID: 1226 RVA: 0x0001BF44 File Offset: 0x0001A144
        public void ShowLastCursorPosition()
        {
            this.cursorX = this.lastCursorX;
            this.Refresh();
        }

        // Token: 0x060004CB RID: 1227 RVA: 0x0001BF58 File Offset: 0x0001A158
        void EnergySpectrumView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int x = e.X;
                double num = this.cursorEnergy;
                int num2 = this.cursorChannel;
                if (x >= this.left && num2 >= 0 && num2 <= this.energySpectrum.NumberOfChannels - 1)
                {
                    if (this.selectionStart != -1 && (Control.ModifierKeys & Keys.Shift) == Keys.Shift)
                    {
                        this.selectionEnd = num2;
                        this.selectionEndEnergy = num;
                    }
                    else
                    {
                        this.selectionStart = num2;
                        this.selectionEnd = num2;
                        this.selectionStartEnergy = num;
                        this.selectionEndEnergy = num;
                    }
                    this.selectionDragging = true;
                    base.Invalidate();
                }
                else
                {
                    this.selectionStart = -1;
                    base.Invalidate();
                }
                if (this.ChannelPickuped != null)
                {
                    this.ChannelPickuped(this, new ChannelPickupedEventArgs(num2, this.energySpectrum.Spectrum[num2]));
                    return;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                this.contextMenuOpening = true;
            }
        }

        // Token: 0x060004CC RID: 1228 RVA: 0x0001C058 File Offset: 0x0001A258
        void EnergySpectrumView_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.selectionDragging = false;
                return;
            }
            MouseButtons button = e.Button;
        }

        // Token: 0x060004CD RID: 1229 RVA: 0x0001C080 File Offset: 0x0001A280
        void button1_Click(object sender, EventArgs e)
        {
            double num = this.verticalScale;
            int value = this.vScrollBar1.Value;
            this.verticalScale += 0.1;
            this.RecalcScrollBar();
            int num2 = base.Height - this.bottom - this.hScrollBar1.Height;
            double num3 = (double)value + (double)num2 / 2.0;
            double num4 = num3 * this.verticalScale / num;
            int num5 = (int)(num4 - (double)num2 / 2.0);
            if (num5 < this.vScrollBar1.Minimum)
            {
                num5 = this.vScrollBar1.Minimum;
            }
            if (num5 > this.vScrollBar1.Maximum)
            {
                num5 = this.vScrollBar1.Maximum;
            }
            this.vScrollBar1.Value = num5;
            base.Invalidate();
        }

        // Token: 0x060004CE RID: 1230 RVA: 0x0001C15C File Offset: 0x0001A35C
        void button2_Click(object sender, EventArgs e)
        {
            double num = this.verticalScale;
            int value = this.vScrollBar1.Value;
            this.verticalScale -= 0.1;
            if (this.verticalScale < 1.0)
            {
                this.verticalScale = 1.0;
            }
            this.RecalcScrollBar();
            int num2 = base.Height - this.bottom - this.hScrollBar1.Height;
            double num3 = (double)value + (double)num2 / 2.0;
            double num4 = num3 * this.verticalScale / num;
            int num5 = (int)(num4 - (double)num2 / 2.0);
            if (num5 < this.vScrollBar1.Minimum)
            {
                num5 = this.vScrollBar1.Minimum;
            }
            if (num5 > this.vScrollBar1.Maximum)
            {
                num5 = this.vScrollBar1.Maximum;
            }
            this.vScrollBar1.Value = num5;
            base.Invalidate();
        }


        public void zoom(decimal zoomvalue)
        {
            double num = this.horizontalScale;
            int value = this.hScrollBar1.Value;
            this.horizontalScale = (double)zoomvalue;
            this.PrepareViewData();
            this.RecalcScrollBar();
            int num3 = value;
            int num4 = base.Width - this.left - this.vScrollBar1.Width;
            switch (this.globalConfigManager.GlobalConfig.ChartViewConfig.MagnificationReference)
            {
                case MagnificationReference.Left:
                    {
                        double num5 = (double)value;
                        double num6 = num5 * this.horizontalScale / num;
                        num3 = (int)num6;
                        break;
                    }
                case MagnificationReference.Center:
                    {
                        double num7 = (double)value + (double)num4 / 2.0;
                        double num8 = num7 * this.horizontalScale / num;
                        num3 = (int)(num8 - (double)num4 / 2.0);
                        break;
                    }
                case MagnificationReference.Right:
                    {
                        double num9 = (double)value + (double)num4;
                        double num10 = num9 * this.horizontalScale / num;
                        num3 = (int)(num10 - (double)num4);
                        break;
                    }
            }
            if (num3 < this.hScrollBar1.Minimum)
            {
                num3 = this.hScrollBar1.Minimum;
            }
            if (num3 > this.hScrollBar1.Maximum)
            {
                num3 = this.hScrollBar1.Maximum;
            }
            this.hScrollBar1.Value = num3;
            base.Invalidate();
        }

        public void takeScreenshot()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Screenshot";
                dialog.Filter = "png file (*.png)|*.png";
                dialog.FilterIndex = 1;
                dialog.RestoreDirectory = true;
                MainForm mf = (MainForm)MainForm.ActiveForm;
                dialog.FileName = mf.ActiveDocument.Text.Trim(new char[] { '*', ' ' });
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Bitmap bitmap = new Bitmap(base.Width - this.vScrollBar1.Width, base.Height - this.hScrollBar1.Height);
                        base.DrawToBitmap(bitmap, new Rectangle(0, 0, base.Width - this.vScrollBar1.Width, base.Height - this.hScrollBar1.Height));
                        bitmap.Save(dialog.FileName);
                    }
                    catch
                    {
                        MessageBox.Show("Error while saving file");
                    }
                }
            }
        }

        // Token: 0x060004D3 RID: 1235 RVA: 0x0001C674 File Offset: 0x0001A874
        public void ZoominSelectedRegion()
        {
            if (this.selectionStart == -1)
            {
                return;
            }
            int startRegion;
            int endRegion;
            if (this.selectionStart < this.selectionEnd)
            {
                startRegion = this.selectionStart;
                endRegion = this.selectionEnd;
            }
            else
            {
                startRegion = this.selectionEnd;
                endRegion = this.selectionStart;
            }
            this.horizontalScale = (double)this.width / ((double)(endRegion - startRegion) * 1.2);
            if (this.horizontalScale > 10.0)
            {
                this.horizontalScale = 10.0;
            }
            if (ActionEvent != null) ActionEvent(this, new EnergySpectrumActionEventArgs(true, (decimal)this.horizontalScale));
            int maxPos = this.CalcMaximumXValue() + 5;
            this.hScrollBar1.Maximum = maxPos;
            int currPos = (int)(CalcXValue((int)((endRegion + startRegion) / 2.0)) - this.width / 2.0); //5651
            if (currPos < 0)
            {
                currPos = 0;
            }
            if (currPos > maxPos)
            {
                currPos = maxPos;
            }
            this.hScrollBar1.Value = currPos;
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }

        // Token: 0x060004D4 RID: 1236 RVA: 0x0001C788 File Offset: 0x0001A988
        public void FitHorizontalScale()
        {
            int num = base.Width - this.left - this.vScrollBar1.Width - 4;
            this.horizontalScale = (double)num / (this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels) * this.pixelPerEnergy + 20.0);
            if (ActionEvent != null) ActionEvent(this, new EnergySpectrumActionEventArgs(true, (decimal)this.horizontalScale));
            int maximum = this.CalcMaximumXValue() + 5;
            this.hScrollBar1.Maximum = maximum;
            this.hScrollBar1.Value = 0;
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }

        public double PowNum
        {
            get { return this.pownum; }
            set { this.pownum = value; }
        }

        public void SetScale11()
        {
            int num = base.Width - this.left - this.vScrollBar1.Width;
            this.horizontalScale = 1.0;
            if (ActionEvent != null) ActionEvent(this, new EnergySpectrumActionEventArgs(true, (decimal)this.horizontalScale));
            int maximum = this.CalcMaximumXValue() + 5;
            this.hScrollBar1.Maximum = maximum;
            this.hScrollBar1.Value = 0;
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }

        // Token: 0x060004D5 RID: 1237 RVA: 0x0001C828 File Offset: 0x0001AA28
        public void SetDefaultHorizontalScale()
        {
            int num = base.Width - this.left - this.vScrollBar1.Width - 4;

            if (this.globalConfigManager.GlobalConfig.ChartViewConfig.DefaultHorizontalMagnification == HorizontalMagnification.Equal)
            {
                this.horizontalScale = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.HorizontalScale;
            }
            else
            {
                this.horizontalScale = (double)num / (this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels) * this.pixelPerEnergy + 20.0);
                int maximum = this.CalcMaximumXValue() + 5;
                this.hScrollBar1.Maximum = maximum;
                this.hScrollBar1.Value = 0;
            }
            if (ActionEvent != null) ActionEvent(this, new EnergySpectrumActionEventArgs(true, (decimal)this.horizontalScale));
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }


        // Token: 0x040001E9 RID: 489
        IContainer components;

        // Token: 0x040001EA RID: 490
        HScrollBar hScrollBar1;

        // Token: 0x040001EB RID: 491
        VScrollBar vScrollBar1;

        // Token: 0x040001EF RID: 495
        Panel panel2;

        // Token: 0x040001F0 RID: 496
        ToolTip toolTip1;

        // Token: 0x040001F1 RID: 497
        RepeatButton button1;

        // Token: 0x040001F2 RID: 498
        RepeatButton button2;

        // Token: 0x040001F5 RID: 501
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x040001F6 RID: 502
        List<ResultData> resultDataList;

        // Token: 0x040001F7 RID: 503
        int activeResultDataIndex;

        // Token: 0x040001F8 RID: 504
        ResultData activeResultData;

        // Token: 0x040001F9 RID: 505
        EnergySpectrum energySpectrum;

        // Token: 0x040001FA RID: 506
        EnergySpectrum backgroundEnergySpectrum;

        EnergySpectrum substractedEnergySpectrum;

        EnergySpectrum continuumEnergySpectrum;

        List<(int[], int, int, Color)> peakEnergySpectrum = new List<(int[], int, int, Color)>();

        EnergySpectrum normByEffBgEnergySpectrum;

        EnergySpectrum normByEffEnergySpectrum;


        // Token: 0x040001FB RID: 507
        ROIConfigData roiConfig;

        // Token: 0x040001FC RID: 508
        EnergyCalibration energyCalibration;

        // Token: 0x040001FD RID: 509
        EnergyCalibration backgroundEnergyCalibration;

        // Token: 0x040001FE RID: 510
        EnergyCalibration baseEnergyCalibration;

        // Token: 0x040001FF RID: 511
        double horizontalScale = 1.0;

        // Token: 0x04000200 RID: 512
        double verticalScale = 1.0;

        // Token: 0x04000201 RID: 513
        int left = 40;

        // Token: 0x04000202 RID: 514
        int bottom = 16;

        bool CtrlKeyPressed = false;

        // Token: 0x04000203 RID: 515
        BackgroundMode backgroundMode;

        // Token: 0x04000204 RID: 516
        DrawingMode drawingMode = DrawingMode.Normal;

        // Token: 0x04000205 RID: 517
        HorizontalUnit horizontalUnit = HorizontalUnit.Energy;

        // Token: 0x04000206 RID: 518
        VerticalUnit verticalUnit;

        // Token: 0x04000207 RID: 519
        ChartType chartType = ChartType.LineChart;

        // Token: 0x04000208 RID: 520
        VerticalScaleType verticalScaleType = VerticalScaleType.LogarithmicScale;

        // Token: 0x04000209 RID: 521
        VerticalFittingMode fittingMode = VerticalFittingMode.MinMax;

        HorizontalMagnification horizontalMagnification = HorizontalMagnification.Equal;

        // Token: 0x0400020A RID: 522
        SmoothingMethod smoothingMethod;

        // Token: 0x0400020B RID: 523
        PeakMode peakMode;

        // Token: 0x0400020C RID: 524
        StringFormat farFormat;

        StringFormat centerFormat;

        // Token: 0x0400020D RID: 525
        double pixelPerEnergy = 0.4;

        // Token: 0x0400020E RID: 526
        double energyViewOffset;

        // Token: 0x0400020F RID: 527
        int selectionStart = -1;

        // Token: 0x04000210 RID: 528
        int selectionEnd = -1;

        // Token: 0x04000211 RID: 529
        double selectionFWHM;

        int selectionFullWidth;

        int selectionCentroidCh;

        double selectionCentroidkeV;

        double selectionFWHMinkev;

        // Token: 0x04000212 RID: 530
        int cursorX = -1;

        // Token: 0x04000213 RID: 531
        int lastCursorX;

        // Token: 0x04000214 RID: 532
        int cursorChannel;

        // Token: 0x04000215 RID: 533
        double cursorEnergy;

        // Token: 0x04000216 RID: 534
        double selectionStartEnergy;

        // Token: 0x04000217 RID: 535
        double selectionEndEnergy;

        // Token: 0x04000219 RID: 537
        static Image logoImage;

        // Token: 0x0400021A RID: 538
        int numberOfChannels;

        public bool dirty = false;

        // Token: 0x0400021B RID: 539
        int backgroundNumberOfChannels;

        // Token: 0x0400021C RID: 540
        int height;

        // Token: 0x0400021D RID: 541
        int width;

        // Token: 0x0400021E RID: 542
        int scrollX;

        // Token: 0x0400021F RID: 543
        int scrollY;

        // Token: 0x04000220 RID: 544
        double scrollBaseY;

        // Token: 0x04000221 RID: 545
        bool validCursor;

        // Token: 0x04000222 RID: 546
        int totalMaxChannel;

        // Token: 0x04000223 RID: 547
        int minChannel;

        // Token: 0x04000224 RID: 548
        int maxChannel;

        // Token: 0x04000225 RID: 549
        double maxValue;

        // Token: 0x04000226 RID: 550
        double minValue;

        // Token: 0x04000227 RID: 551
        double maxValueLog;

        double maxValuePow;

        // Token: 0x04000228 RID: 552
        double minValueLog;

        double minValuePow;

        // Token: 0x04000229 RID: 553
        double totalMaxValue;

        // Token: 0x0400022A RID: 554
        double totalMinValue;

        // Token: 0x0400022B RID: 555
        double totalMaxValueLog;

        double totalMaxValuePow;

        // Token: 0x0400022C RID: 556
        double totalMinValueLog;

        double totalMinValuePow;

        // Token: 0x0400022D RID: 557
        double valueRange;

        // Token: 0x0400022E RID: 558
        double valueRangeLog;

        double valueRangePow;

        // Token: 0x0400022F RID: 559
        bool measureDrawingTime;

        // Token: 0x04000230 RID: 560
        Stopwatch stopwatch = new Stopwatch();

        // Token: 0x04000231 RID: 561
        int stopwatchY = 10;

        // Token: 0x04000232 RID: 562
        bool contextMenuOpening;

        // Token: 0x04000233 RID: 563
        bool selectionDragging;

        double pownum = 4;

        // Token: 0x0200021E RID: 542
        // (Invoke) Token: 0x06001927 RID: 6439
        public delegate void ChannelPickupedEventHandler(object sender, ChannelPickupedEventArgs e);

        public event EventHandler<EnergySpectrumActionEventArgs> ActionEvent;
    }

    public class EnergySpectrumActionEventArgs : EventArgs
    {
        private bool needUpdateScale;
        private decimal newScaleValue;

        public bool NeedUpdateScale
        {
            get { return this.needUpdateScale; }
        }

        public decimal NewScaleValue
        {
            get { return this.newScaleValue; }
        }

        public EnergySpectrumActionEventArgs(bool needUpdate, decimal newScalevalue)
        {
            this.needUpdateScale = needUpdate;
            this.newScaleValue = newScalevalue;
        }
    }
}