using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x02000062 RID: 98
	public class StandardPulseView : UserControl
	{
		// Token: 0x17000197 RID: 407
		// (get) Token: 0x060004E6 RID: 1254 RVA: 0x0001CCAC File Offset: 0x0001AEAC
		// (set) Token: 0x060004E7 RID: 1255 RVA: 0x0001CCB4 File Offset: 0x0001AEB4
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

		// Token: 0x17000198 RID: 408
		// (get) Token: 0x060004E8 RID: 1256 RVA: 0x0001CCC0 File Offset: 0x0001AEC0
		// (set) Token: 0x060004E9 RID: 1257 RVA: 0x0001CCC8 File Offset: 0x0001AEC8
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

		// Token: 0x17000199 RID: 409
		// (get) Token: 0x060004EA RID: 1258 RVA: 0x0001CCD4 File Offset: 0x0001AED4
		// (set) Token: 0x060004EB RID: 1259 RVA: 0x0001CCDC File Offset: 0x0001AEDC
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

		// Token: 0x060004EC RID: 1260 RVA: 0x0001CCE8 File Offset: 0x0001AEE8
		public StandardPulseView()
		{
			this.InitializeComponent();
		}

		// Token: 0x060004ED RID: 1261 RVA: 0x0001CCF8 File Offset: 0x0001AEF8
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

		// Token: 0x060004EE RID: 1262 RVA: 0x0001CEE4 File Offset: 0x0001B0E4
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060004EF RID: 1263 RVA: 0x0001CF0C File Offset: 0x0001B10C
		void InitializeComponent()
		{
			base.SuspendLayout();
			base.AutoScaleDimensions = new SizeF(6f, 12f);
			base.AutoScaleMode = AutoScaleMode.Font;
			this.BackColor = Color.Black;
			this.DoubleBuffered = true;
			base.Name = "StandardPulseView";
			base.Size = new Size(195, 187);
			base.ResumeLayout(false);
		}

		// Token: 0x0400023B RID: 571
		double[] pulseShape;

		// Token: 0x0400023C RID: 572
		int pulseShapeSize;

		// Token: 0x0400023D RID: 573
		int peakIndex;

		// Token: 0x0400023E RID: 574
		IContainer components;
	}
}
