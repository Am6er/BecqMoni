using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class StandardPulseView : UserControl
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

        public StandardPulseView()
        {
            this.InitializeComponent();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics graphics = pe.Graphics;
            Pen pen = new Pen(Color.FromArgb(60, 60, 60));
            int num = 0;
            int width = base.Width;
            int height = base.Height;
            float num2 = (float)height / 2f;
            graphics.DrawLine(pen, 0f, num2, (float)width, num2);
            if (this.pulseShape != null)
            {
                float num3 = (float)num + (float)this.peakIndex * (float)width / (float)this.pulseShape.Length;
                graphics.DrawLine(pen, num3, 0f, num3, (float)height);
                double num4 = 100.0;
                for (int i = 0; i < this.pulseShapeSize; i++)
                {
                    if (i >= this.pulseShape.Length)
                    {
                        break;
                    }
                    if (this.pulseShape[i] < num4)
                    {
                        num4 = this.pulseShape[i];
                    }
                }
                double num5 = 0.0;
                if (this.pulseShape[this.peakIndex] != 0.0)
                {
                    num5 = (double)height / 2.0 / (double)((float)this.pulseShape[this.peakIndex]) * 0.9;
                }
                if (num4 != 0.0)
                {
                    double num6 = (double)height / 2.0 / Math.Abs(num4) * 0.9;
                    if (num6 < num5)
                    {
                        num5 = num6;
                    }
                }
                float x = (float)num;
                float y = (float)(-(float)this.pulseShape[0] * num5 + (double)height / 2.0);
                for (int j = 1; j < this.pulseShapeSize; j++)
                {
                    if (j >= this.pulseShape.Length)
                    {
                        break;
                    }
                    num3 = (float)num + (float)j * (float)width / (float)this.pulseShapeSize;
                    num2 = (float)(-(float)this.pulseShape[j] * num5 + (double)height / 2.0);
                    graphics.DrawLine(Pens.Yellow, x, y, num3, num2);
                    x = num3;
                    y = num2;
                }
            }
            pen.Dispose();
            base.OnPaint(pe);
        }

        double[] pulseShape;

        int pulseShapeSize;

        int peakIndex;
    }
}
