using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            this.panel1 = new Panel();
            this.label1 = new Label();
            this.textBox1 = new TextBox();
            this.button4 = new RepeatButton();
            this.button5 = new Button();
            this.button3 = new RepeatButton();
            this.panel2 = new Panel();
            this.button1 = new RepeatButton();
            this.button2 = new RepeatButton();
            this.toolTip1 = new ToolTip(this.components);
            this.panel1.SuspendLayout();
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
            componentResourceManager.ApplyResources(this.panel1, "panel1");
            this.panel1.BackColor = SystemColors.Control;
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.textBox1);
            this.panel1.Controls.Add(this.button5);
            this.panel1.Controls.Add(this.button4);
            this.panel1.Controls.Add(this.button3);
            this.panel1.Name = "panel1";
            this.toolTip1.SetToolTip(this.panel1, componentResourceManager.GetString("panel1.ToolTip"));
            componentResourceManager.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            this.toolTip1.SetToolTip(this.label1, componentResourceManager.GetString("label1.ToolTip"));
            componentResourceManager.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.TabStop = false;
            this.toolTip1.SetToolTip(this.textBox1, componentResourceManager.GetString("textBox1.ToolTip"));
            this.textBox1.KeyDown += this.textBox1_KeyDown;
            this.textBox1.Validated += this.textBox1_Validated;
            componentResourceManager.ApplyResources(this.button5, "button5");
            this.button5.Text = "S";
            this.button5.Name = "button5";
            this.button5.TabStop = false;
            this.toolTip1.SetToolTip(this.button5, componentResourceManager.GetString("button5.ToolTip"));
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += this.button5_Click;
            componentResourceManager.ApplyResources(this.button4, "button4");
            this.button4.Image = Resources.Zoomout;
            this.button4.Name = "button4";
            this.button4.TabStop = false;
            this.toolTip1.SetToolTip(this.button4, componentResourceManager.GetString("button4.ToolTip"));
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += this.button4_Click;
            componentResourceManager.ApplyResources(this.button3, "button3");
            this.button3.Image = Resources.Zoomin;
            this.button3.Name = "button3";
            this.button3.TabStop = false;
            this.toolTip1.SetToolTip(this.button3, componentResourceManager.GetString("button3.ToolTip"));
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += this.button3_Click;
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
            base.Controls.Add(this.panel1);
            base.Controls.Add(this.vScrollBar1);
            base.Controls.Add(this.hScrollBar1);
            this.DoubleBuffered = true;
            base.Name = "EnergySpectrumView";
            this.toolTip1.SetToolTip(this, componentResourceManager.GetString("$this.ToolTip"));
            base.SizeChanged += this.EnergySpectrumView_SizeChanged;
            base.MouseDown += this.EnergySpectrumView_MouseDown;
            base.MouseLeave += this.EnergySpectrumView_MouseLeave;
            base.MouseMove += this.EnergySpectrumView_MouseMove;
            base.MouseUp += this.EnergySpectrumView_MouseUp;
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
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
            ((Bitmap)this.button1.Image).MakeTransparent();
            ((Bitmap)this.button2.Image).MakeTransparent();
            ((Bitmap)this.button3.Image).MakeTransparent();
            ((Bitmap)this.button4.Image).MakeTransparent();
            this.hScrollBar1.Visible = true;
            this.vScrollBar1.Visible = true;
            this.textBox1.Text = this.horizontalScale.ToString();
        }

        // Token: 0x060004AD RID: 1197 RVA: 0x00016630 File Offset: 0x00014830
        int CalcMaximumXValue()
        {
            return (int)((this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels) - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale);
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
            this.continuumEnergySpectrum = this.activeResultData.ContinuumEnergySpectrum;
            this.substractedEnergySpectrum = this.activeResultData.SubtractEnergySpectrum;
            this.roiConfig = this.activeResultData.ROIConfig;
            this.numberOfChannels = this.energySpectrum.NumberOfChannels;
            this.energyCalibration = this.energySpectrum.EnergyCalibration;
            this.baseEnergyCalibration = this.energySpectrum.EnergyCalibration;
            if (this.backgroundEnergySpectrum != null)
            {
                this.backgroundNumberOfChannels = this.backgroundEnergySpectrum.NumberOfChannels;
                this.backgroundEnergyCalibration = this.backgroundEnergySpectrum.EnergyCalibration;
            }
            this.height = base.ClientSize.Height - this.bottom - this.hScrollBar1.Height;
            this.width = base.ClientSize.Width - this.left - this.vScrollBar1.Width;
            this.scrollX = -this.hScrollBar1.Value;
            this.scrollY = -this.vScrollBar1.Value;
            this.minChannel = 0;
            this.maxChannel = this.numberOfChannels - 1;
            if (this.fittingMode != VerticalFittingMode.None)
            {
                this.minChannel = (int)((double)(-(double)this.scrollX) / this.horizontalScale);
                this.maxChannel = (int)((double)(-(double)this.scrollX + this.width) / this.horizontalScale);
                if (this.maxChannel >= this.numberOfChannels)
                {
                    this.maxChannel = this.numberOfChannels - 1;
                }
            }
            this.totalMaxValue = 0.0;
            this.totalMinValue = double.PositiveInfinity;
            if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
            {
                int i = 0;
                while (i < this.numberOfChannels - 1)
                {
                    double e = this.energyCalibration.ChannelToEnergy((double)i);
                    int num;
                    try
                    {
                        num = (int)this.backgroundEnergyCalibration.EnergyToChannel(e);
                    }
                    catch (OutofChannelException)
                    {
                        goto IL_28F;
                    }
                    goto IL_1F4;
                    IL_28F:
                    i++;
                    continue;
                    IL_1F4:
                    if (num < 0 || num >= this.backgroundEnergySpectrum.NumberOfChannels)
                    {
                        goto IL_28F;
                    }
                    double num2;
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                    {
                        num2 = (double)this.backgroundEnergySpectrum.Spectrum[num] / this.backgroundEnergySpectrum.MeasurementTime;
                    }
                    else
                    {
                        num2 = (double)this.backgroundEnergySpectrum.Spectrum[num] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                    }
                    if (num2 > this.totalMaxValue)
                    {
                        this.totalMaxValue = num2;
                    }
                    if (num2 < this.totalMinValue)
                    {
                        this.totalMinValue = num2;
                        goto IL_28F;
                    }
                    goto IL_28F;
                }
            }
            foreach (ResultData resultData in this.resultDataList)
            {
                EnergySpectrum energySpectrum = resultData.EnergySpectrum;
                for (int j = 0; j < energySpectrum.NumberOfChannels - 1; j++)
                {
                    double num3 = (double)energySpectrum.Spectrum[j];
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond && energySpectrum.MeasurementTime != 0.0)
                    {
                        num3 /= energySpectrum.MeasurementTime;
                    }
                    if (num3 > this.totalMaxValue)
                    {
                        this.totalMaxValue = num3;
                    }
                    if (num3 < this.totalMinValue)
                    {
                        this.totalMinValue = num3;
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
            if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
            {
                for (int j = 0; j < substractedEnergySpectrum.NumberOfChannels - 1; j++)
                {
                    double num3 = (double)substractedEnergySpectrum.Spectrum[j];
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond && substractedEnergySpectrum.MeasurementTime != 0.0)
                    {
                        num3 /= substractedEnergySpectrum.MeasurementTime;
                    }
                    if (num3 > this.totalMaxValue)
                    {
                        this.totalMaxValue = num3;
                    }
                    if (num3 < this.totalMinValue)
                    {
                        this.totalMinValue = num3;
                    }
                }
            }
            this.maxValue = 0.0;
            this.minValue = double.PositiveInfinity;
            bool flag = false;
            int k = this.minChannel;
            while (k < this.maxChannel)
            {
                double num5;
                if ((this.fittingMode == VerticalFittingMode.BackgroundMinMax || this.energySpectrum.MeasurementTime == 0.0) && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    double e2 = this.energyCalibration.ChannelToEnergy((double)k);
                    int num4;
                    try
                    {
                        num4 = (int)this.backgroundEnergyCalibration.EnergyToChannel(e2);
                    }
                    catch (OutofChannelException)
                    {
                        goto IL_595;
                    }
                    if (num4 >= 0 && num4 < this.backgroundEnergySpectrum.NumberOfChannels)
                    {
                        if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                        {
                            num5 = (double)this.backgroundEnergySpectrum.Spectrum[num4] / this.backgroundEnergySpectrum.MeasurementTime;
                            goto IL_53A;
                        }
                        num5 = (double)this.backgroundEnergySpectrum.Spectrum[num4] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                        goto IL_53A;
                    }
                }
                else
                {
                    if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                    {
                        num5 = (double)this.substractedEnergySpectrum.Spectrum[k];
                    } else
                    {
                        num5 = (double)this.energySpectrum.Spectrum[k];
                    }
                        
                    if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                    {
                        if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                        {
                            num5 /= this.substractedEnergySpectrum.MeasurementTime;
                        } else
                        {
                            num5 /= this.energySpectrum.MeasurementTime;
                        }
                        goto IL_53A;
                    }
                    goto IL_53A;
                }
                IL_595:
                k++;
                continue;
                IL_53A:
                if (!flag && num5 != 0.0)
                {
                    flag = true;
                }
                if (num5 > this.maxValue)
                {
                    this.maxValue = num5;
                }
                if (num5 < this.minValue && (flag || num5 != 0.0))
                {
                    this.minValue = num5;
                    goto IL_595;
                }
                goto IL_595;
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
            if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
            {
                this.totalMaxValue *= 2.0;
                this.totalMinValue = 0.0;
                this.maxValue *= 2.0;
                this.minValue *= 0.7;
            }
            else
            {
                this.totalMaxValue *= 1.05;
                this.totalMinValue *= 0.98;
                this.maxValue *= 1.05;
                this.minValue *= 0.98;
            }
            if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
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
                            using (List<ResultData>.Enumerator enumerator2 = this.resultDataList.GetEnumerator())
                            {
                                while (enumerator2.MoveNext())
                                {
                                    ResultData resultData2 = enumerator2.Current;
                                    double num6 = 1.0 / resultData2.EnergySpectrum.MeasurementTime * 0.7;
                                    if (num6 < this.totalMinValue)
                                    {
                                        this.totalMinValue = num6;
                                    }
                                }
                                goto IL_8F8;
                            }
                        }
                        this.totalMinValue = 7E-05;
                    }
                }
                IL_8F8:
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
                this.maxValueLog = Math.Log10(this.maxValue);
                this.minValueLog = Math.Log10(this.minValue);
                this.totalMaxValueLog = Math.Log10(this.totalMaxValue);
                this.totalMinValueLog = Math.Log10(this.totalMinValue);
                this.valueRangeLog = this.totalMaxValueLog - this.totalMinValueLog;
            }
            else
            {
                this.valueRange = this.totalMaxValue - this.totalMinValue;
            }
            this.scrollY = this.vScrollBar1.Value;
            if (this.fittingMode != VerticalFittingMode.None)
            {
                if (this.verticalScaleType == VerticalScaleType.LogarithmicScale)
                {
                    if (this.maxValueLog - this.minValueLog != 0.0)
                    {
                        this.verticalScale = this.valueRangeLog / (this.maxValueLog - this.minValueLog);
                        if (this.verticalScale < 1.0)
                        {
                            this.verticalScale = 1.0;
                        }
                    }
                    else
                    {
                        this.verticalScale = 1.0;
                    }
                    this.scrollY = (int)((double)this.height * (this.totalMaxValueLog - this.maxValueLog) / this.valueRangeLog * this.verticalScale);
                }
                else
                {
                    if (this.maxValue - this.minValue != 0.0)
                    {
                        this.verticalScale = this.valueRange / (this.maxValue - this.minValue);
                        if (this.verticalScale < 1.0)
                        {
                            this.verticalScale = 1.0;
                        }
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
            if (this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.Spectrum != null && (this.backgroundEnergySpectrum.DrawingSpectrum == null || this.backgroundNumberOfChannels != this.backgroundEnergySpectrum.DrawingSpectrum.Length))
            {
                this.backgroundEnergySpectrum.DrawingSpectrum = new double[this.backgroundNumberOfChannels];
            }
            foreach (ResultData resultData3 in this.resultDataList)
            {
                if (resultData3.EnergySpectrum.DrawingSpectrum == null || resultData3.EnergySpectrum.NumberOfChannels != resultData3.EnergySpectrum.DrawingSpectrum.Length)
                {
                    resultData3.EnergySpectrum.DrawingSpectrum = new double[resultData3.EnergySpectrum.NumberOfChannels];
                }
            }
            if (this.smoothingMethod == SmoothingMethod.None)
            {
                if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.Spectrum != null)
                {
                    for (int l = 0; l < this.backgroundNumberOfChannels; l++)
                    {
                        this.backgroundEnergySpectrum.DrawingSpectrum[l] = (double)this.backgroundEnergySpectrum.Spectrum[l];
                    }
                }
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    for (int l = 0; l < this.substractedEnergySpectrum.NumberOfChannels; l++)
                    {
                        this.substractedEnergySpectrum.DrawingSpectrum[l] = (double)this.substractedEnergySpectrum.Spectrum[l];
                    }
                }
                if (this.backgroundMode == BackgroundMode.ShowContinuum)
                {
                    for (int l = 0; l < this.continuumEnergySpectrum.NumberOfChannels; l++)
                    {
                        this.continuumEnergySpectrum.DrawingSpectrum[l] = (double)this.continuumEnergySpectrum.Spectrum[l];
                    }
                }
                    using (List<ResultData>.Enumerator enumerator4 = this.resultDataList.GetEnumerator())
                {
                    while (enumerator4.MoveNext())
                    {
                        ResultData resultData4 = enumerator4.Current;
                        for (int m = 0; m < resultData4.EnergySpectrum.NumberOfChannels; m++)
                        {
                            resultData4.EnergySpectrum.DrawingSpectrum[m] = (double)resultData4.EnergySpectrum.Spectrum[m];
                        }
                    }
                    return;
                }
            }
            if (this.smoothingMethod == SmoothingMethod.SimpleMovingAverage)
            {
                int numberOfSMADataPoints = this.globalConfigManager.GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                foreach (ResultData resultData5 in this.resultDataList)
                {
                    EnergySpectrum energySpectrum2 = resultData5.EnergySpectrum;
                    for (int n = 0; n < energySpectrum2.NumberOfChannels; n++)
                    {
                        double num7 = 0.0;
                        for (int num8 = n - numberOfSMADataPoints / 2; num8 < n - numberOfSMADataPoints / 2 + numberOfSMADataPoints; num8++)
                        {
                            int num9 = num8;
                            if (num9 < 0)
                            {
                                num9 = 0;
                            }
                            else if (num8 >= energySpectrum2.NumberOfChannels)
                            {
                                num9 = energySpectrum2.NumberOfChannels - 1;
                            }
                            num7 += (double)energySpectrum2.Spectrum[num9];
                        }
                        energySpectrum2.DrawingSpectrum[n] = num7 / (double)numberOfSMADataPoints;
                    }
                }
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    for (int num10 = 0; num10 < this.substractedEnergySpectrum.NumberOfChannels; num10++)
                    {
                        double num11 = 0.0;
                        for (int num12 = num10 - numberOfSMADataPoints / 2; num12 < num10 - numberOfSMADataPoints / 2 + numberOfSMADataPoints; num12++)
                        {
                            int num13 = num12;
                            if (num13 < 0)
                            {
                                num13 = 0;
                            }
                            else if (num12 >= this.substractedEnergySpectrum.NumberOfChannels)
                            {
                                num13 = this.substractedEnergySpectrum.NumberOfChannels - 1;
                            }
                            num11 += (double)this.substractedEnergySpectrum.Spectrum[num13];
                        }
                        this.substractedEnergySpectrum.DrawingSpectrum[num10] = num11 / (double)numberOfSMADataPoints;
                    }
                }
                if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.Spectrum != null)
                {
                    for (int num10 = 0; num10 < this.backgroundNumberOfChannels; num10++)
                    {
                        double num11 = 0.0;
                        for (int num12 = num10 - numberOfSMADataPoints / 2; num12 < num10 - numberOfSMADataPoints / 2 + numberOfSMADataPoints; num12++)
                        {
                            int num13 = num12;
                            if (num13 < 0)
                            {
                                num13 = 0;
                            }
                            else if (num12 >= this.backgroundNumberOfChannels)
                            {
                                num13 = this.backgroundNumberOfChannels - 1;
                            }
                            num11 += (double)this.backgroundEnergySpectrum.Spectrum[num13];
                        }
                        this.backgroundEnergySpectrum.DrawingSpectrum[num10] = num11 / (double)numberOfSMADataPoints;
                    }
                }
                if (this.backgroundMode == BackgroundMode.ShowContinuum)
                {
                    for (int num10 = 0; num10 < this.continuumEnergySpectrum.NumberOfChannels; num10++)
                    {
                        double num11 = 0.0;
                        for (int num12 = num10 - numberOfSMADataPoints / 2; num12 < num10 - numberOfSMADataPoints / 2 + numberOfSMADataPoints; num12++)
                        {
                            int num13 = num12;
                            if (num13 < 0)
                            {
                                num13 = 0;
                            }
                            else if (num12 >= this.continuumEnergySpectrum.NumberOfChannels)
                            {
                                num13 = this.continuumEnergySpectrum.NumberOfChannels - 1;
                            }
                            num11 += (double)this.continuumEnergySpectrum.Spectrum[num13];
                        }
                        this.continuumEnergySpectrum.DrawingSpectrum[num10] = num11 / (double)numberOfSMADataPoints;
                    }
                }
            }
            else if (this.smoothingMethod == SmoothingMethod.WeightedMovingAverage)
            {
                int numberOfWMADataPoints = this.globalConfigManager.GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                foreach (ResultData resultData6 in this.resultDataList)
                {
                    EnergySpectrum energySpectrum3 = resultData6.EnergySpectrum;
                    for (int num14 = 0; num14 < energySpectrum3.NumberOfChannels; num14++)
                    {
                        double num15 = 0.0;
                        double num16 = 0.0;
                        for (int num17 = num14 - numberOfWMADataPoints / 2; num17 < num14 - numberOfWMADataPoints / 2 + numberOfWMADataPoints; num17++)
                        {
                            int num18 = num17;
                            if (num18 < 0)
                            {
                                num18 = 0;
                            }
                            else if (num17 >= energySpectrum3.NumberOfChannels)
                            {
                                num18 = energySpectrum3.NumberOfChannels - 1;
                            }
                            double num19 = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(num14 - num17));
                            num15 += (double)energySpectrum3.Spectrum[num18] * num19;
                            num16 += num19;
                        }
                        energySpectrum3.DrawingSpectrum[num14] = num15 / num16;
                    }
                }
                if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.Spectrum != null)
                {
                    for (int num20 = 0; num20 < this.backgroundNumberOfChannels; num20++)
                    {
                        double num21 = 0.0;
                        double num22 = 0.0;
                        for (int num23 = num20 - numberOfWMADataPoints / 2; num23 < num20 - numberOfWMADataPoints / 2 + numberOfWMADataPoints; num23++)
                        {
                            int num24 = num23;
                            if (num24 < 0)
                            {
                                num24 = 0;
                            }
                            else if (num23 >= this.backgroundNumberOfChannels)
                            {
                                num24 = this.backgroundNumberOfChannels - 1;
                            }
                            double num25 = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(num20 - num23));
                            num21 += (double)this.backgroundEnergySpectrum.Spectrum[num24] * num25;
                            num22 += num25;
                        }
                        this.backgroundEnergySpectrum.DrawingSpectrum[num20] = num21 / num22;
                    }
                }
                if (this.backgroundMode == BackgroundMode.ShowContinuum)
                {
                    for (int num20 = 0; num20 < this.continuumEnergySpectrum.NumberOfChannels; num20++)
                    {
                        double num21 = 0.0;
                        double num22 = 0.0;
                        for (int num23 = num20 - numberOfWMADataPoints / 2; num23 < num20 - numberOfWMADataPoints / 2 + numberOfWMADataPoints; num23++)
                        {
                            int num24 = num23;
                            if (num24 < 0)
                            {
                                num24 = 0;
                            }
                            else if (num23 >= this.continuumEnergySpectrum.NumberOfChannels)
                            {
                                num24 = this.continuumEnergySpectrum.NumberOfChannels - 1;
                            }
                            double num25 = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(num20 - num23));
                            num21 += (double)this.continuumEnergySpectrum.Spectrum[num24] * num25;
                            num22 += num25;
                        }
                        this.continuumEnergySpectrum.DrawingSpectrum[num20] = num21 / num22;
                    }
                }
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    for (int num20 = 0; num20 < this.substractedEnergySpectrum.NumberOfChannels; num20++)
                    {
                        double num21 = 0.0;
                        double num22 = 0.0;
                        for (int num23 = num20 - numberOfWMADataPoints / 2; num23 < num20 - numberOfWMADataPoints / 2 + numberOfWMADataPoints; num23++)
                        {
                            int num24 = num23;
                            if (num24 < 0)
                            {
                                num24 = 0;
                            }
                            else if (num23 >= this.substractedEnergySpectrum.NumberOfChannels)
                            {
                                num24 = this.substractedEnergySpectrum.NumberOfChannels - 1;
                            }
                            double num25 = (double)(numberOfWMADataPoints / 2 + 1 - Math.Abs(num20 - num23));
                            num21 += (double)this.substractedEnergySpectrum.Spectrum[num24] * num25;
                            num22 += num25;
                        }
                        this.substractedEnergySpectrum.DrawingSpectrum[num20] = num21 / num22;
                    }
                }
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
                if (this.backgroundMode == BackgroundMode.Visible)
                {
                    this.ShowROINetRegion(g, true);
                }
                if (this.chartType == ChartType.BarChart)
                {
                    if (colorConfig.SpectrumDrawingOrder == 0)
                    {
                        if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
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
                        if (this.backgroundMode == BackgroundMode.ShowContinuum)
                        {
                            int alpha = (int)(colorConfig.BackgroundSpectrumColorTransparency * 255m / 100m);
                            using (Brush brush = new SolidBrush(Color.FromArgb(alpha, colorConfig.BackgroundSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha, colorConfig.BackgroundSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, true);
                                }
                            }
                        }
                        if (this.energySpectrum.MeasurementTime == 0.0)
                        {
                            goto IL_438;
                        }
                        int alpha2 = (int)(colorConfig.ActiveSpectrumColorTransparency * 255m / 100m);
                        if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                        {
                            using (Brush brush2 = new SolidBrush(Color.FromArgb(alpha2, colorConfig.BgDiffColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(255, colorConfig.BgDiffColor.Color)))
                                {
                                    this.DrawBarChart(g, brush2, this.substractedEnergySpectrum, this.substractedEnergySpectrum.EnergyCalibration, false);
                                }
                                goto IL_438;
                            }
                        } else
                        {
                            using (Brush brush2 = new SolidBrush(Color.FromArgb(alpha2, colorConfig.ActiveSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha2, colorConfig.ActiveSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush2, this.energySpectrum, this.energyCalibration, false);
                                }
                                goto IL_438;
                            }
                        }
                        
                    }
                    if (this.energySpectrum.MeasurementTime != 0.0)
                    {
                        int alpha3 = (int)(colorConfig.ActiveSpectrumColorTransparency * 255m / 100m);
                        if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                        {
                            using (Brush brush3 = new SolidBrush(Color.FromArgb(alpha3, colorConfig.BgDiffColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(255, colorConfig.BgDiffColor.Color)))
                                {
                                    this.DrawBarChart(g, brush3, this.substractedEnergySpectrum, this.substractedEnergySpectrum.EnergyCalibration, false);
                                }
                            }
                        }
                        else
                        {
                            using (Brush brush3 = new SolidBrush(Color.FromArgb(alpha3, colorConfig.ActiveSpectrumColor.Color)))
                            {
                                using (new Pen(Color.FromArgb(alpha3, colorConfig.ActiveSpectrumColor.Color)))
                                {
                                    this.DrawBarChart(g, brush3, this.energySpectrum, this.energyCalibration, false);
                                }
                            }
                        }
                    }
                    if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
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
                    if (this.backgroundMode == BackgroundMode.ShowContinuum)
                    {
                        int alpha4 = (int)(colorConfig.BackgroundSpectrumColorTransparency * 255m / 100m);
                        using (Brush brush4 = new SolidBrush(Color.FromArgb(alpha4, colorConfig.BackgroundSpectrumColor.Color)))
                        {
                            using (new Pen(Color.FromArgb(alpha4, colorConfig.BackgroundSpectrumColor.Color)))
                            {
                                this.DrawBarChart(g, brush4, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, true);
                            }
                        }
                    }
                IL_438:
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
                    if (this.backgroundMode == BackgroundMode.Visible && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                    {
                        using (Pen pen5 = new Pen(colorConfig.BackgroundSpectrumColor.Color))
                        {
                            this.DrawLineChart(g, pen5, this.backgroundEnergySpectrum, this.backgroundEnergyCalibration, true);
                        }
                    }
                    if (this.backgroundMode == BackgroundMode.ShowContinuum)
                    {
                        using (Pen pen5 = new Pen(colorConfig.BackgroundSpectrumColor.Color))
                        {
                            this.DrawLineChart(g, pen5, this.continuumEnergySpectrum, this.continuumEnergySpectrum.EnergyCalibration, true);
                        }
                    }
                    if (this.energySpectrum.MeasurementTime != 0.0)
                    {
                        if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                        {
                            using (Pen pen6 = new Pen(colorConfig.BgDiffColor.Color))
                            {
                                this.DrawLineChart(g, pen6, this.substractedEnergySpectrum, this.substractedEnergySpectrum.EnergyCalibration, false);
                            }
                        }
                        else
                        {
                            using (Pen pen6 = new Pen(colorConfig.ActiveSpectrumColor.Color))
                            {
                                this.DrawLineChart(g, pen6, this.energySpectrum, this.energyCalibration, false);
                            }
                        }
                    }
                    if (this.drawingMode == DrawingMode.HighDefinition)
                    {
                        g.SmoothingMode = SmoothingMode.Default;
                        g.PixelOffsetMode = PixelOffsetMode.Default;
                    }
                }
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
            this.selectionFWHM = -1.0;
            if (num == num2)
            {
                return;
            }

            EnergyResolutionResult energyResolutionResult;
            if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
            {
                energyResolutionResult = EnergyResolutionCalculator.CalculateFWHM(this.substractedEnergySpectrum, num, num2);
            } else
            {
                energyResolutionResult = EnergyResolutionCalculator.CalculateFWHM(this.energySpectrum, num, num2);
            }
            if (energyResolutionResult == null)
            {
                return;
            }
            this.selectionFWHM = energyResolutionResult.Resolution;
            this.selectionFullWidth = (int)(energyResolutionResult.RightChannel - energyResolutionResult.LeftChannel);
            this.selectionFWHMinkev = energyResolutionResult.ResolutionInkeV;
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
                else if (num4 > 0.0)
                {
                    double num6 = Math.Log10(num4);
                    num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
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
            int y2;
            int y3;
            if (this.verticalScaleType == VerticalScaleType.LinearScale)
            {
                y2 = this.height - (int)((num10 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                y3 = this.height - (int)((num11 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
            }
            else
            {
                if (num10 > 0.0)
                {
                    y2 = this.height - (int)((Math.Log10(num10) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    y2 = this.height;
                }
                if (num11 > 0.0)
                {
                    y3 = this.height - (int)((Math.Log10(num11) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                }
                else
                {
                    y3 = this.height;
                }
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
                else if (num14 > 0.0)
                {
                    num5 = this.height - (int)((Math.Log10(num14) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
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
                        num3 = (int)calibration.EnergyToChannel(num2 / this.pixelPerEnergy + this.energyViewOffset);
                        goto IL_B8;
                    }
                    catch (OutofChannelException)
                    {
                        break;
                    }
                    goto IL_68;
                }
                goto IL_68;
                IL_B8:
                if (num3 >= 0 && num3 < spectrum.DrawingSpectrum.Length)
                {
                    double num4 = spectrum.DrawingSpectrum[num3];
                    if (isBackground)
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
                        else
                        {
                            double num6 = Math.Log10(num4);
                            num5 = this.height - (int)((num6 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        if (i > this.left)
                        {
                            g.FillRectangle(brush, i, num5, 1, this.height - num5);
                        }
                    }
                }
                i++;
                continue;
                IL_68:
                num3 = (int)((double)(i - this.scrollX - this.left) / this.horizontalScale);
                if (isBackground && !this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                {
                    num3 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy((double)num3));
                    goto IL_B8;
                }
                goto IL_B8;
            }
        }

        // Token: 0x060004B8 RID: 1208 RVA: 0x00018CD8 File Offset: 0x00016ED8
        void DrawLineChart(Graphics g, Pen pen, EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground)
        {
            int y = this.height;
            int x = 0;
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
                else if (isBackground)
                {
                    double num4 = this.baseEnergyCalibration.EnergyToChannel(this.backgroundEnergyCalibration.ChannelToEnergy((double)i));
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
                else
                {
                    double num6 = Math.Log10(num);
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
                    }
                }
                x = num3;
                y = num5;
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
                    if (roidefinitionData.Enabled)
                    {
                        double lowerLimit = roidefinitionData.LowerLimit;
                        double upperLimit = roidefinitionData.UpperLimit;
                        float num;
                        float num2;
                        if (this.horizontalUnit == HorizontalUnit.Channel)
                        {
                            try
                            {
                                num = (float)(this.energyCalibration.EnergyToChannel(lowerLimit) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                            }
                            catch (OutofChannelException)
                            {
                                break;
                            }
                            try
                            {
                                num2 = (float)(this.energyCalibration.EnergyToChannel(upperLimit) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                                goto IL_14D;
                            }
                            catch (OutofChannelException)
                            {
                                num2 = (float)((double)(this.numberOfChannels - 1) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                                goto IL_14D;
                            }
                            goto IL_F9;
                        }
                        goto IL_F9;
                        IL_14D:
                        g.FillRectangle(brush, num, 0f, num2 - num, (float)(this.height - 1));
                        continue;
                        IL_F9:
                        num = (float)((lowerLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        num2 = (float)((upperLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        goto IL_14D;
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
                    if (roidefinitionData.Enabled)
                    {
                        double lowerLimit = roidefinitionData.LowerLimit;
                        double upperLimit = roidefinitionData.UpperLimit;
                        float num;
                        float num2;
                        if (this.horizontalUnit == HorizontalUnit.Channel)
                        {
                            try
                            {
                                num = (float)(this.energyCalibration.EnergyToChannel(lowerLimit) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                            }
                            catch (OutofChannelException)
                            {
                                break;
                            }
                            try
                            {
                                num2 = (float)(this.energyCalibration.EnergyToChannel(upperLimit) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                                goto IL_14D;
                            }
                            catch (OutofChannelException)
                            {
                                num2 = (float)((double)(this.numberOfChannels - 1) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                                goto IL_14D;
                            }
                            goto IL_F9;
                        }
                        goto IL_F9;
                        IL_14D:
                        if (num > (float)this.left)
                        {
                            g.DrawLine(pen, num, 0f, num, (float)(this.height - 1));
                        }
                        if (num2 > (float)this.left)
                        {
                            g.DrawLine(pen, num2, 0f, num2, (float)(this.height - 1));
                            continue;
                        }
                        continue;
                        IL_F9:
                        num = (float)((lowerLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        num2 = (float)((upperLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        goto IL_14D;
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
            Brush brush = new SolidBrush(colorConfig.ROINetColor.Color);
            foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
            {
                if (roidefinitionData.Enabled)
                {
                    double lowerLimit = roidefinitionData.LowerLimit;
                    double upperLimit = roidefinitionData.UpperLimit;
                    float num;
                    float num2;
                    if (this.horizontalUnit == HorizontalUnit.Channel)
                    {
                        try
                        {
                            num = (float)(this.energyCalibration.EnergyToChannel(lowerLimit) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                        }
                        catch (OutofChannelException)
                        {
                            return;
                        }
                        try
                        {
                            num2 = (float)(this.energyCalibration.EnergyToChannel(upperLimit) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                            goto IL_14D;
                        }
                        catch (OutofChannelException)
                        {
                            num2 = (float)((double)(this.numberOfChannels - 1) * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                            goto IL_14D;
                        }
                        goto IL_F9;
                    }
                    goto IL_F9;
                    IL_14D:
                    int i = (int)num;
                    while (i <= (int)num2)
                    {
                        double num4;
                        if (this.horizontalUnit == HorizontalUnit.Energy)
                        {
                            double num3 = (double)(i - this.scrollX - this.left) / this.horizontalScale;
                            try
                            {
                                num4 = this.energyCalibration.EnergyToChannel(num3 / this.pixelPerEnergy + this.energyViewOffset);
                                goto IL_1C1;
                            }
                            catch (OutofChannelException)
                            {
                                break;
                            }
                            goto IL_1A5;
                        }
                        goto IL_1A5;
                        IL_1C1:
                        if ((int)num4 >= 0 && (int)num4 < this.energySpectrum.Spectrum.Length)
                        {
                            double num5 = this.energySpectrum.DrawingSpectrum[(int)num4];
                            if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                            {
                                num5 /= this.energySpectrum.MeasurementTime;
                            }
                            double num6;
                            if (this.backgroundEnergySpectrum == null || this.backgroundEnergySpectrum.MeasurementTime == 0.0)
                            {
                                num6 = 0.0;
                            }
                            else
                            {
                                int num7 = (int)num4;
                                if (!this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                                {
                                    num7 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy(num4));
                                }
                                if (num7 < 0 || num7 >= this.backgroundEnergySpectrum.Spectrum.Length)
                                {
                                    goto IL_4B4;
                                }
                                if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                                {
                                    num6 = this.backgroundEnergySpectrum.DrawingSpectrum[num7] / this.backgroundEnergySpectrum.MeasurementTime;
                                }
                                else
                                {
                                    num6 = this.backgroundEnergySpectrum.DrawingSpectrum[num7] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                                }
                            }
                            int num8;
                            int num9;
                            if (this.verticalScaleType == VerticalScaleType.LinearScale)
                            {
                                num8 = this.height - (int)((num5 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                num9 = this.height - (int)((num6 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            else
                            {
                                if (num5 == 0.0)
                                {
                                    num8 = this.height;
                                }
                                else
                                {
                                    num8 = this.height - (int)((Math.Log10(num5) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                }
                                if (num8 > this.height)
                                {
                                    num8 = this.height;
                                }
                                if (num6 == 0.0)
                                {
                                    num9 = this.height;
                                }
                                else
                                {
                                    num9 = this.height - (int)((Math.Log10(num6) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                }
                                if (num9 > this.height)
                                {
                                    num9 = this.height;
                                }
                            }
                            if (num8 > num9)
                            {
                                int num10 = num8;
                                num8 = num9;
                                num9 = num10;
                            }
                            if (i > this.left)
                            {
                                if (this.chartType == ChartType.BarChart)
                                {
                                    g.FillRectangle(brush, i, num8, 1, num9 - num8);
                                }
                                else
                                {
                                    g.FillRectangle(brush, i, num8, 1, num9 - num8 + 1);
                                }
                            }
                        }
                        IL_4B4:
                        i++;
                        continue;
                        IL_1A5:
                        num4 = (double)((int)((double)(i - this.scrollX - this.left) / this.horizontalScale));
                        goto IL_1C1;
                    }
                    continue;
                    IL_F9:
                    num = (float)((lowerLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                    num2 = (float)((upperLimit - this.energyViewOffset) * this.pixelPerEnergy * this.horizontalScale) + (float)this.scrollX + (float)this.left;
                    goto IL_14D;
                }
            }
            brush.Dispose();
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
                    double num8;
                    if (this.horizontalUnit == HorizontalUnit.Energy)
                    {
                        double num7 = (double)(i - this.scrollX - this.left) / this.horizontalScale;
                        try
                        {
                            num8 = this.energyCalibration.EnergyToChannel(num7 / this.pixelPerEnergy + this.energyViewOffset);
                            goto IL_1F4;
                        }
                        catch (OutofChannelException)
                        {
                            break;
                        }
                        goto IL_1D8;
                    }
                    goto IL_1D8;
                    IL_1F4:
                    if ((int)num8 >= 0 && (int)num8 < this.energySpectrum.Spectrum.Length)
                    {
                        double num9 = 0.0;
                        double num10 = 0.0;
                        if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                        {
                            num9 = this.substractedEnergySpectrum.DrawingSpectrum[(int)num8];
                            if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.substractedEnergySpectrum.MeasurementTime != 0.0)
                            {
                                num9 /= this.substractedEnergySpectrum.MeasurementTime;
                            }
                        } else
                        {
                            num9 = this.energySpectrum.DrawingSpectrum[(int)num8];
                            if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
                            {
                                num9 /= this.energySpectrum.MeasurementTime;
                            }
                            if (this.backgroundEnergySpectrum == null || this.backgroundEnergySpectrum.MeasurementTime == 0.0 || this.backgroundMode != BackgroundMode.Visible)
                            {
                                num10 = 0.0;
                            }
                            else
                            {
                                int num11 = (int)num8;
                                if (!this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                                {
                                    num11 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy(num8));
                                }
                                if (num11 < 0 || num11 >= this.backgroundEnergySpectrum.Spectrum.Length)
                                {
                                    goto IL_4E9;
                                }
                                if (this.verticalUnit == VerticalUnit.CountsPerSecond)
                                {
                                    num10 = this.backgroundEnergySpectrum.DrawingSpectrum[num11] / this.backgroundEnergySpectrum.MeasurementTime;
                                }
                                else
                                {
                                    num10 = this.backgroundEnergySpectrum.DrawingSpectrum[num11] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                                }
                            }
                        }

                        int num12;
                        int num13;
                        if (this.verticalScaleType == VerticalScaleType.LinearScale)
                        {
                            num12 = this.height - (int)((num9 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            num13 = this.height - (int)((num10 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                        }
                        else
                        {
                            if (num9 == 0.0)
                            {
                                num12 = this.height;
                            }
                            else
                            {
                                num12 = this.height - (int)((Math.Log10(num9) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            if (num12 > this.height)
                            {
                                num12 = this.height;
                            }
                            if (num10 == 0.0)
                            {
                                num13 = this.height;
                            }
                            else
                            {
                                num13 = this.height - (int)((Math.Log10(num10) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                            }
                            if (num13 > this.height)
                            {
                                num13 = this.height;
                            }
                        }
                        if (num12 > num13)
                        {
                            int num14 = num12;
                            num12 = num13;
                            num13 = num14;
                        }
                        if (i > this.left)
                        {
                            if (this.chartType == ChartType.BarChart)
                            {
                                g.FillRectangle(brush, i, num12, 1, num13 - num12);
                            }
                            else
                            {
                                g.FillRectangle(brush, i, num12, 1, num13 - num12 + 1);
                            }
                        }
                    }
                    IL_4E9:
                    i++;
                    continue;
                    IL_1D8:
                    num8 = (double)((int)((double)(i - this.scrollX - this.left) / this.horizontalScale));
                    goto IL_1F4;
                }
            }
        }

        // Token: 0x060004BE RID: 1214 RVA: 0x00019FEC File Offset: 0x000181EC
        void ShowVerticalAxis(Graphics g)
        {
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
                                        num4 = this.height - (int)((Math.Log10(num2) - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
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
                                            g.DrawString(num2.ToString(), this.Font, brush3, r, this.farFormat);
                                            g.ResetClip();
                                        }
                                    }
                                    num3 *= 10.0;
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
                                num6 = Math.Floor(num6 / d2) * d2;
                                double num7 = ((double)this.height - this.scrollBaseY - (double)this.scrollY) / this.verticalScale / (double)this.height * this.valueRange + this.totalMinValue;
                                while ((double)num6 <= num7)
                                {
                                    int num8 = this.height - (int)(((double)num6 - this.totalMinValue) / this.valueRange * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
                                    Rectangle r2 = new Rectangle(-20, num8 - 12, 20 + this.left - 3, 12);
                                    if (num6 % d != 0m)
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
                                        g.DrawString(num6.ToString(), this.Font, brush3, r2, this.farFormat);
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
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    num6 = substractedEnergySpectrum.DrawingSpectrum[channel2];
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
                else
                {
                    double num7 = Math.Log10(num6);
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
                else
                {
                    double num4 = Math.Log10(num3);
                    if (num3 == 0.0)
                    {
                        y = this.height + 100;
                    }
                    else
                    {
                        y = this.height - (int)((num4 - this.totalMinValueLog) / this.valueRangeLog * (double)this.height * this.verticalScale + this.scrollBaseY + (double)this.scrollY);
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
            int num = 190;
            int num2;
            if (this.cursorX < base.Width - (num + 20))
            {
                num2 = this.left + this.width - num - 10;
            }
            else
            {
                num2 = this.left + 10;
            }
            int num3 = 10;
            ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
            if (this.validCursor && this.cursorChannel >= 0 && this.cursorChannel < this.energySpectrum.NumberOfChannels)
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
                        if (this.cursorEnergy >= nuclideDefinition.Energy-(pitch + (int)Math.Round(nuclideDefinition.Energy * percent/100, 0)) &&
                            this.cursorEnergy <= nuclideDefinition.Energy + (pitch + (int)Math.Round(nuclideDefinition.Energy * percent / 100, 0)))
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
                int num6 = 94;
                g.FillRectangle(Brushes.DarkGray, num2, num3, num, num6);
                g.FillRectangle(Brushes.White, num2 - 3, num3 - 3, num, num6);
                g.DrawRectangle(Pens.Black, num2 - 3, num3 - 3, num, num6);
                Rectangle r = new Rectangle(num2 + 5, num3 + 4, num - 12, 32);
                g.DrawString(Resources.ChartHeaderChannel, this.Font, Brushes.Black, r);
                g.DrawString(this.cursorChannel.ToString(), this.Font, Brushes.Black, r, this.farFormat);
                r.Y += 16;
                double num7 = this.energyCalibration.ChannelToEnergy((double)this.cursorChannel);
                g.DrawString(Resources.ChartHeaderEnergy, this.Font, Brushes.Black, r);
                g.DrawString(num7.ToString("f2"), this.Font, Brushes.Black, r, this.farFormat);
                r.Y += 22;
                g.DrawLine(Pens.LightGray, r.Left, r.Top - 6, r.Right, r.Top - 6);
                int num8 = 0;
                if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                {
                    num8 = this.substractedEnergySpectrum.Spectrum[this.cursorChannel];
                } else
                {
                    num8 = this.energySpectrum.Spectrum[this.cursorChannel];
                }
                    
                g.DrawString(Resources.ChartHeaderGrossCounts, this.Font, Brushes.Black, r);
                g.DrawString(num8.ToString("f2"), this.Font, Brushes.Black, r, this.farFormat);
                if (this.backgroundEnergySpectrum != null && this.backgroundMode != BackgroundMode.Substract)
                {
                    double num9 = 0.0;
                    if (this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                    {
                        int num10;
                        if (this.backgroundEnergyCalibration.Equals(this.baseEnergyCalibration))
                        {
                            num10 = this.cursorChannel;
                        }
                        else
                        {
                            num10 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy((double)this.cursorChannel));
                        }
                        if (num10 >= 0 && num10 < this.backgroundNumberOfChannels)
                        {
                            num9 = (double)this.backgroundEnergySpectrum.Spectrum[num10] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                        }
                    }
                    r.Y += 16;
                    g.DrawString(Resources.ChartHeaderBGCounts, this.Font, Brushes.Black, r);
                    g.DrawString(num9.ToString("f2"), this.Font, Brushes.Black, r, this.farFormat);
                    r.Y += 16;
                    if (num9 != 0.0)
                    {
                        double num11 = (double)num8 / num9 * 100.0;
                        g.DrawString(Resources.ChartHeaderCountBGRatio, this.Font, Brushes.Black, r);
                        g.DrawString(num11.ToString("f2") + Resources.PercentCharacter, this.Font, Brushes.Black, r, this.farFormat);
                    }
                }
                num3 = 110;
            }
            if (this.selectionStart != -1)
            {
                int num12 = 194;
                g.FillRectangle(Brushes.DarkGray, num2, num3, num, num12);
                g.FillRectangle(Brushes.White, num2 - 3, num3 - 3, num, num12);
                g.DrawRectangle(Pens.Black, num2 - 3, num3 - 3, num, num12);
                int num13;
                int num14;
                if (this.selectionStart < this.selectionEnd)
                {
                    num13 = this.selectionStart;
                    num14 = this.selectionEnd;
                }
                else
                {
                    num13 = this.selectionEnd;
                    num14 = this.selectionStart;
                }
                double num15 = this.energyCalibration.ChannelToEnergy((double)num13);
                double num16 = this.energyCalibration.ChannelToEnergy((double)num14);
                double num17 = 0.0;
                double num18 = 0.0;
                double num19 = 0.0;
                double peakcounts = 0.0;
                for (int i = num13; i <= num14; i++)
                {
                    int num20 = 0;
                    double continuum = 0.0;
                    if (this.backgroundMode == BackgroundMode.Substract && this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0)
                    {
                        num20 = this.substractedEnergySpectrum.Spectrum[i];
                        continuum = getY(i, num13, num14, this.substractedEnergySpectrum.Spectrum[num13], this.substractedEnergySpectrum.Spectrum[num14]);
                    } else
                    {
                        num20 = this.energySpectrum.Spectrum[i];
                        continuum = getY(i, num13, num14, this.energySpectrum.Spectrum[num13], this.energySpectrum.Spectrum[num14]);
                    }
                    if (continuum > num20)
                    {
                        continuum = num20;
                    }
                    num17 += (double)num20;
                    num18 += (double)(num20);
                    peakcounts += (double)num20;
                    double num21 = 0.0;
                    if (this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.MeasurementTime != 0.0 && this.backgroundMode != BackgroundMode.Substract)
                    {
                        int num22 = i;
                        if (!this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
                        {
                            num22 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.baseEnergyCalibration.ChannelToEnergy((double)i));
                        }
                        if (num22 >= 0 && num22 < this.backgroundNumberOfChannels)
                        {
                            num21 = (double)this.backgroundEnergySpectrum.Spectrum[num22] * this.energySpectrum.MeasurementTime / this.backgroundEnergySpectrum.MeasurementTime;
                        }
                    }
                    num19 += num21;
                    num18 -= num21;
                    if (continuum < num21)
                    {
                        continuum = num21;
                    }
                    peakcounts -= continuum;
                }
                double num23 = 0.0;
                if (this.energySpectrum.MeasurementTime != 0.0)
                {
                    num23 = num18 / this.energySpectrum.MeasurementTime;
                }
                Rectangle r2 = new Rectangle(num2 + 5, num3 + 4, num - 12, 32);
                g.DrawString(Resources.ChartHeaderSelection, this.Font, Brushes.Black, r2);
                r2.Y += 22;
                g.DrawLine(Pens.LightGray, r2.Left, r2.Top - 6, r2.Right, r2.Top - 6);
                g.DrawString(Resources.ChartHeaderChannel, this.Font, Brushes.Black, r2);
                g.DrawString(num13.ToString() + " - " + num14.ToString(), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                g.DrawString(Resources.ChartHeaderEnergy, this.Font, Brushes.Black, r2);
                g.DrawString(num15.ToString("f2") + " - " + num16.ToString("f2"), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 22;
                g.DrawLine(Pens.LightGray, r2.Left, r2.Top - 6, r2.Right, r2.Top - 6);
                g.DrawString(Resources.ChartHeaderGrossCounts, this.Font, Brushes.Black, r2);
                g.DrawString(num17.ToString("f2"), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                g.DrawString(Resources.ChartHeaderBGCounts, this.Font, Brushes.Black, r2);
                g.DrawString(num19.ToString("f2"), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                if (num19 != 0.0)
                {
                    double num24 = num17 / num19 * 100.0;
                    g.DrawString(Resources.ChartHeaderCountBGRatio, this.Font, Brushes.Black, r2);
                    g.DrawString(num24.ToString("f2") + Resources.PercentCharacter, this.Font, Brushes.Black, r2, this.farFormat);
                }
                r2.Y += 16;
                g.DrawString(Resources.ChartHeaderNetCounts, this.Font, Brushes.Black, r2);
                g.DrawString(num18.ToString("f2"), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                g.DrawString(Resources.ChartHeaderNetCps, this.Font, Brushes.Black, r2);
                g.DrawString(num23.ToString("f4"), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                g.DrawString(Resources.ChartHeaderPeakCounts, this.Font, Brushes.Black, r2);
                g.DrawString(peakcounts.ToString("f2"), this.Font, Brushes.Black, r2, this.farFormat);
                r2.Y += 16;
                if (this.selectionFWHM > 0.0)
                {
                    g.DrawString(Resources.ChartHeaderFWHM, this.Font, Brushes.Black, r2);
                    g.DrawString((this.selectionFWHM * 100.0).ToString("f2") + Resources.PercentCharacter +
                        " (" + (this.selectionFWHMinkev).ToString("f2") + " " + Resources.kev + ")",
                        this.Font, Brushes.Black, r2, this.farFormat);
                    r2.Y += 16;
                    g.DrawString(Resources.Sigma, this.Font, Brushes.Black, r2);
                    g.DrawString((this.selectionFullWidth).ToString() + " " + Resources.ChartChannelShort,
                        this.Font, Brushes.Black, r2, this.farFormat);
                }
            }
        }

        /// <summary>
        /// Y = k*x + b
        /// using known points (x1,y1), (x2, y2)
        /// </summary>
        /// <param name="X"></param>
        /// <param name="x1"></param>
        /// <param name="x2"></param>
        /// <param name="y1"></param>
        /// <param name="y2"></param>
        /// <returns></returns>
        private double getY(int X, int x1, int x2, int y1, int y2)
        {
            if (x1 - x2 != 0)
            {
                double k = k = (y1 - y2) / (x1 - x2);
                double b = y1 - x1 * (y1 - y2) / (x1 - x2);
                return k * X + b;
            } else
            {
                return 0;
            }
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
                        this.cursorChannel = (int)this.energyCalibration.EnergyToChannel(this.cursorEnergy);
                        goto IL_CB;
                    }
                    catch (OutofChannelException)
                    {
                        this.cursorChannel = -1;
                        goto IL_CB;
                    }
                }
                this.cursorChannel = (int)((double)(this.cursorX - this.left - this.scrollX) / this.horizontalScale);
                this.cursorEnergy = this.energyCalibration.ChannelToEnergy((double)this.CursorChannel);
                IL_CB:
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
                    this.ChannelPickuped(this, new ChannelPickupedEventArgs(num2));
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

        // Token: 0x060004CF RID: 1231 RVA: 0x0001C258 File Offset: 0x0001A458
        void button3_Click(object sender, EventArgs e)
        {
            double num = this.horizontalScale;
            int value = this.hScrollBar1.Value;
            decimal num2 = (decimal)this.horizontalScale;
            num2 += 0.1m;
            num2 = Math.Round(num2, 1);
            if (num2 > 10.0m)
            {
                num2 = 10.0m;
            }
            this.horizontalScale = (double)num2;
            this.textBox1.Text = this.horizontalScale.ToString();
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

        // Token: 0x060004D0 RID: 1232 RVA: 0x0001C3F8 File Offset: 0x0001A5F8
        void button4_Click(object sender, EventArgs e)
        {
            double num = this.horizontalScale;
            int value = this.hScrollBar1.Value;
            decimal num2 = (decimal)this.horizontalScale;
            num2 -= 0.1m;
            num2 = Math.Round(num2, 1);
            if (num2 < 0.1m)
            {
                num2 = 0.1m;
            }
            this.horizontalScale = (double)num2;
            this.textBox1.Text = this.horizontalScale.ToString();
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

        void button5_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Screenshot";
                dialog.Filter = "png file (*.png)|*.png";
                dialog.FilterIndex = 1;
                dialog.RestoreDirectory = true;
                MainForm mf = (MainForm)MainForm.ActiveForm;
                dialog.FileName = mf.ActiveDocument.Text;
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

        // Token: 0x060004D1 RID: 1233 RVA: 0x0001C598 File Offset: 0x0001A798
        void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            double num;
            if (e.KeyCode == Keys.Return && double.TryParse(this.textBox1.Text, out num))
            {
                this.horizontalScale = num;
                if (num > 10.0)
                {
                    this.horizontalScale = 10.0;
                }
                else if (num < 0.1)
                {
                    this.horizontalScale = 0.1;
                }
                this.PrepareViewData();
                this.RecalcScrollBar();
                base.Invalidate();
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x060004D2 RID: 1234 RVA: 0x0001C630 File Offset: 0x0001A830
        void textBox1_Validated(object sender, EventArgs e)
        {
            double num;
            if (double.TryParse(this.textBox1.Text, out num))
            {
                this.horizontalScale = num;
                this.PrepareViewData();
                this.RecalcScrollBar();
                base.Invalidate();
            }
        }

        // Token: 0x060004D3 RID: 1235 RVA: 0x0001C674 File Offset: 0x0001A874
        public void ZoominSelectedRegion()
        {
            if (this.selectionStart == -1)
            {
                return;
            }
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
            this.horizontalScale = (double)this.width / ((double)(num2 - num) * 1.2);
            if (this.horizontalScale > 10.0)
            {
                this.horizontalScale = 10.0;
            }
            this.textBox1.Text = this.horizontalScale.ToString();
            int num3 = this.CalcMaximumXValue() + 5;
            this.hScrollBar1.Maximum = num3;
            int num4 = (int)((double)(num2 + num) / 2.0 * this.horizontalScale - (double)this.width / 2.0);
            if (num4 < 0)
            {
                num4 = 0;
            }
            if (num4 > num3)
            {
                num4 = num3;
            }
            this.hScrollBar1.Value = num4;
            this.PrepareViewData();
            this.RecalcScrollBar();
            base.Invalidate();
        }

        // Token: 0x060004D4 RID: 1236 RVA: 0x0001C788 File Offset: 0x0001A988
        public void FitHorizontalScale()
        {
            int num = base.Width - this.left - this.vScrollBar1.Width;
            this.horizontalScale = (double)num / (this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels) * this.pixelPerEnergy + 8.0);
            this.textBox1.Text = this.horizontalScale.ToString();
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
            int num = base.Width - this.left - this.vScrollBar1.Width;
            if (this.globalConfigManager.GlobalConfig.ChartViewConfig.DefaultHorizontalMagnification == HorizontalMagnification.Equal)
            {
                this.horizontalScale = 1.0;
            }
            else
            {
                this.horizontalScale = (double)num / (this.energyCalibration.ChannelToEnergy((double)this.numberOfChannels) * this.pixelPerEnergy + 8.0);
                this.textBox1.Text = this.horizontalScale.ToString();
                int maximum = this.CalcMaximumXValue() + 5;
                this.hScrollBar1.Maximum = maximum;
                this.hScrollBar1.Value = 0;
            }
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

        // Token: 0x040001EC RID: 492
        Panel panel1;

        // Token: 0x040001ED RID: 493
        Label label1;

        // Token: 0x040001EE RID: 494
        TextBox textBox1;

        // Token: 0x040001EF RID: 495
        Panel panel2;

        // Token: 0x040001F0 RID: 496
        ToolTip toolTip1;

        // Token: 0x040001F1 RID: 497
        RepeatButton button1;

        // Token: 0x040001F2 RID: 498
        RepeatButton button2;

        // Token: 0x040001F3 RID: 499
        RepeatButton button3;

        // Token: 0x040001F4 RID: 500
        RepeatButton button4;

        Button button5;

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

        // Token: 0x0400020A RID: 522
        SmoothingMethod smoothingMethod;

        // Token: 0x0400020B RID: 523
        PeakMode peakMode;

        // Token: 0x0400020C RID: 524
        StringFormat farFormat;

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

        // Token: 0x04000228 RID: 552
        double minValueLog;

        // Token: 0x04000229 RID: 553
        double totalMaxValue;

        // Token: 0x0400022A RID: 554
        double totalMinValue;

        // Token: 0x0400022B RID: 555
        double totalMaxValueLog;

        // Token: 0x0400022C RID: 556
        double totalMinValueLog;

        // Token: 0x0400022D RID: 557
        double valueRange;

        // Token: 0x0400022E RID: 558
        double valueRangeLog;

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

        // Token: 0x0200021E RID: 542
        // (Invoke) Token: 0x06001927 RID: 6439
        public delegate void ChannelPickupedEventHandler(object sender, ChannelPickupedEventArgs e);
    }
}