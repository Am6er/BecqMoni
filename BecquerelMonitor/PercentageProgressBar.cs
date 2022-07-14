using BecquerelMonitor.Properties;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000F3 RID: 243
    [DefaultEvent("PercentageVisibleChanged")]
    [DefaultProperty("PercentageVisible")]
    [Description("Control that extends the System.Windows.Forms.ProgressBar with the ability to overlay the percentage.")]
    public class PercentageProgressBar : ProgressBar
    {
        // Token: 0x14000025 RID: 37
        // (add) Token: 0x06000BD2 RID: 3026 RVA: 0x00047A70 File Offset: 0x00045C70
        // (remove) Token: 0x06000BD3 RID: 3027 RVA: 0x00047AAC File Offset: 0x00045CAC
        [Description("Raised when the visibility of the percentage text is changed.")]
        [Category("Property Changed")]
        public event EventHandler PercentageVisibleChanged;

        // Token: 0x06000BD4 RID: 3028 RVA: 0x00047AE8 File Offset: 0x00045CE8
        public PercentageProgressBar()
        {
            this.overlayColor = Color.White;
            this.stringFormat = new StringFormat();
            this.percentageVisible = true;
            this.stringFormat.Alignment = StringAlignment.Center;
            this.stringFormat.LineAlignment = StringAlignment.Center;
            base.SetStyle(ControlStyles.UserPaint, true);
            base.SetStyle(ControlStyles.DoubleBuffer, true);
            base.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        }

        // Token: 0x17000320 RID: 800
        // (get) Token: 0x06000BD5 RID: 3029 RVA: 0x00047B60 File Offset: 0x00045D60
        // (set) Token: 0x06000BD6 RID: 3030 RVA: 0x00047B68 File Offset: 0x00045D68
        [DefaultValue(typeof(Color), "White")]
        [Description("The Color that is used to draw the text over a filled section of the progress bar.")]
        [Category("Appearance")]
        public Color OverlayColor
        {
            get
            {
                return this.overlayColor;
            }
            set
            {
                if (this.overlayColor != value)
                {
                    this.overlayColor = value;
                }
            }
        }

        // Token: 0x17000321 RID: 801
        // (get) Token: 0x06000BD7 RID: 3031 RVA: 0x00047B84 File Offset: 0x00045D84
        // (set) Token: 0x06000BD8 RID: 3032 RVA: 0x00047B8C File Offset: 0x00045D8C
        [Description("Indicates whether the percentage will be displayed on the progress bar.")]
        [DefaultValue(true)]
        [Category("Appearance")]
        public bool PercentageVisible
        {
            get
            {
                return this.percentageVisible;
            }
            set
            {
                if (this.percentageVisible != value)
                {
                    this.percentageVisible = value;
                    this.OnPercentageVisibleChanged(EventArgs.Empty);
                }
            }
        }

        // Token: 0x17000322 RID: 802
        // (get) Token: 0x06000BD9 RID: 3033 RVA: 0x00047BAC File Offset: 0x00045DAC
        // (set) Token: 0x06000BDA RID: 3034 RVA: 0x00047BB4 File Offset: 0x00045DB4
        public double DoubleValue
        {
            get
            {
                return this.doubleValue;
            }
            set
            {
                if (this.doubleValue != value)
                {
                    this.doubleValue = value;
                    base.Value = (int)value;
                    if (this.percentageVisible)
                    {
                        base.Invalidate();
                    }
                }
            }
        }

        // Token: 0x17000323 RID: 803
        // (get) Token: 0x06000BDB RID: 3035 RVA: 0x00047BE4 File Offset: 0x00045DE4
        // (set) Token: 0x06000BDC RID: 3036 RVA: 0x00047BEC File Offset: 0x00045DEC
        public string PriorText
        {
            get
            {
                return this.priorText;
            }
            set
            {
                this.priorText = value;
            }
        }

        // Token: 0x17000324 RID: 804
        // (get) Token: 0x06000BDD RID: 3037 RVA: 0x00047BF8 File Offset: 0x00045DF8
        public override string Text
        {
            get
            {
                return this.priorText + string.Format(Resources.ProgressString, this.doubleValue.ToString("f1"));
            }
        }

        // Token: 0x06000BDE RID: 3038 RVA: 0x00047C20 File Offset: 0x00045E20
        protected virtual void OnPercentageVisibleChanged(EventArgs e)
        {
            EventHandler percentageVisibleChanged = this.PercentageVisibleChanged;
            if (percentageVisibleChanged != null)
            {
                percentageVisibleChanged(this, e);
            }
        }

        // Token: 0x06000BDF RID: 3039 RVA: 0x00047C48 File Offset: 0x00045E48
        void ShowPercentage(Graphics g)
        {
            Region region = new Region(new RectangleF((float)base.ClientRectangle.X, (float)base.ClientRectangle.Y, (float)(base.ClientRectangle.Width * base.Value / 100), (float)base.ClientRectangle.Height));
            using (Brush brush = new SolidBrush(this.overlayColor))
            {
                g.Clip = region;
                g.DrawString(this.Text, this.Font, brush, base.ClientRectangle, this.stringFormat);
            }
            Region region2 = new Region(base.ClientRectangle);
            region2.Exclude(region);
            using (Brush brush2 = new SolidBrush(this.ForeColor))
            {
                g.Clip = region2;
                g.DrawString(this.Text, this.Font, brush2, base.ClientRectangle, this.stringFormat);
            }
        }

        // Token: 0x06000BE0 RID: 3040 RVA: 0x00047D78 File Offset: 0x00045F78
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (this.percentageVisible && m.Msg == 15 && base.Enabled)
            {
                using (Graphics graphics = base.CreateGraphics())
                {
                    this.ShowPercentage(graphics);
                }
            }
        }

        // Token: 0x06000BE1 RID: 3041 RVA: 0x00047DE0 File Offset: 0x00045FE0
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            Rectangle rectangle = new Rectangle(0, 0, base.Width, base.Height);
            if (ProgressBarRenderer.IsSupported)
            {
                ProgressBarRenderer.DrawHorizontalBar(graphics, rectangle);
            }
            rectangle.Width = (int)((double)(rectangle.Width - 2) * (this.doubleValue / (double)base.Maximum));
            rectangle.Height -= 2;
            rectangle.Offset(1, 1);
            if (base.Enabled)
            {
                if (ProgressBarRenderer.IsSupported)
                {
                    ProgressBarRenderer.DrawHorizontalChunks(graphics, rectangle);
                }
                else
                {
                    using (Brush brush = new SolidBrush(Color.FromArgb(0, 170, 0)))
                    {
                        graphics.FillRectangle(brush, rectangle);
                    }
                }
                this.ShowPercentage(graphics);
            }
        }

        // Token: 0x04000783 RID: 1923
        const int WM_PAINT = 15;

        // Token: 0x04000784 RID: 1924
        double doubleValue;

        // Token: 0x04000786 RID: 1926
        Color overlayColor;

        // Token: 0x04000787 RID: 1927
        bool percentageVisible;

        // Token: 0x04000788 RID: 1928
        StringFormat stringFormat;

        // Token: 0x04000789 RID: 1929
        string priorText = "";
    }
}
