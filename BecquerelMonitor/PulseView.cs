using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    [ToolboxItem(true)]
    public partial class PulseView : UserControl
    {
        public double[] PulseShape
        {
            get
            {
                return this.pulseShape;
            }
            set
            {
                this.pulseShape = value;
            }
        }

        public int PulseShapeSize
        {
            get
            {
                return this.pulseShapeSize;
            }
            set
            {
                this.pulseShapeSize = value;
            }
        }

        public int PeakIndex
        {
            get
            {
                return this.peakIndex;
            }
            set
            {
                this.peakIndex = value;
            }
        }

        public double PulseHeight
        {
            get
            {
                return this.pulseHeight;
            }
            set
            {
                if (this.maxpulseHeight < value)
                {
                    this.maxpulseHeight = value;
                }
                this.pulseHeight = value;
            }
        }

        public bool IsValidPulse
        {
            get
            {
                return this.isValidPulse;
            }
            set
            {
                this.isValidPulse = value;
            }
        }

        public bool AntiAliasing
        {
            get
            {
                return this.antiAliasing;
            }
            set
            {
                this.antiAliasing = value;
            }
        }

        public PulseView()
        {
            this.InitializeComponent();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            // Take local snapshots: the audio thread writes PulseShape/PulseShapeSize
            // without synchronization while this paints. Also clamp the size to the
            // array length - a torn update used to cause IndexOutOfRangeException here.
            double[] pulseShape = this.pulseShape;
            int pulseShapeSize = this.pulseShapeSize;
            if (pulseShape == null || pulseShape.Length == 0)
            {
                return;
            }
            if (pulseShapeSize > pulseShape.Length)
            {
                pulseShapeSize = pulseShape.Length;
            }
            if (pulseShapeSize < 2)
            {
                return;
            }

            Graphics graphics = pe.Graphics;
            Pen pen = new Pen(Color.FromArgb(60, 60, 60));
            Pen pen2 = new Pen(Color.FromArgb(30, 30, 30));
            int width = base.Width;
            int height = base.Height;
            int num = 0;
            float num2 = (float)height * 3f / 4f;
            float num3 = num2 / 120f;
            StringFormat stringFormat = new StringFormat();
            stringFormat.Alignment = StringAlignment.Near;

            for (float num4 = -100f; num4 <= 100f; num4 += 20f)
            {
                float num5 = -num4 * num3 + num2;
                if (num4 % 100f == 0f)
                {
                    Rectangle r = new Rectangle(4, (int)num5 - 14, 100, 22);
                    graphics.DrawString(num4.ToString("f0"), this.Font, Brushes.Gray, r, stringFormat);
                    graphics.DrawLine(pen, 0f, num5, (float)base.Width, num5);
                }
                else
                {
                    graphics.DrawLine(pen2, 0f, num5, (float)base.Width, num5);
                }
            }

            stringFormat.Alignment = StringAlignment.Far;
            float x = (float)num;
            float y = (float)(-(float)pulseShape[0]) * num3 + num2;
            float num6 = (float)num + (float)this.peakIndex * (float)width / (float)(pulseShapeSize - 1);
            float lineY = -100f * num3 + num2;
            graphics.DrawLine(pen, num6, lineY, num6, (float)base.Height);

            if (this.antiAliasing)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
            }

            for (int i = 0; i < pulseShapeSize; i++)
            {
                num6 = (float)num + (float)i * (float)width / (float)(pulseShapeSize - 1);
                lineY = (float)(-(float)pulseShape[i]) * num3 + num2;
                graphics.DrawLine(this.isValidPulse ? Pens.Yellow : Pens.Red, x, y, num6, lineY);
                x = num6;
                y = lineY;
            }

            if (this.antiAliasing)
            {
                graphics.SmoothingMode = SmoothingMode.Default;
                graphics.PixelOffsetMode = PixelOffsetMode.Default;
            }

            graphics.DrawString("Wave height: ", this.Font, Brushes.White, (float)(base.Width - 130), 2f);
            Rectangle r2 = new Rectangle(0, 2, base.Width - 4, 22);
            graphics.DrawString(this.pulseHeight.ToString("f2"), this.Font, Brushes.White, r2, stringFormat);

            graphics.DrawString("Max Wave height: ", this.Font, Brushes.White, (float)(base.Width - 130), 20f);
            Rectangle r3 = new Rectangle(0, 20, base.Width - 4, 22);
            graphics.DrawString(this.maxpulseHeight.ToString("f2"), this.Font, Brushes.White, r3, stringFormat);

            pen.Dispose();
            pen2.Dispose();
            stringFormat.Dispose();
            base.OnPaint(pe);
        }

        double[] pulseShape;

        int pulseShapeSize;

        bool isValidPulse = true;

        int peakIndex;

        double pulseHeight;

        double maxpulseHeight;

        bool antiAliasing;
    }
}
