using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x02000061 RID: 97
	[ToolboxItem(true)]
	public class PulseView : UserControl
	{
		// Token: 0x060004D6 RID: 1238 RVA: 0x0001C8F8 File Offset: 0x0001AAF8
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060004D7 RID: 1239 RVA: 0x0001C920 File Offset: 0x0001AB20
		void InitializeComponent()
		{
			base.SuspendLayout();
			this.BackColor = Color.Black;
			this.DoubleBuffered = true;
			base.Name = "PulseView";
			base.Size = new Size(298, 279);
			base.ResumeLayout(false);
		}

		// Token: 0x17000191 RID: 401
		// (get) Token: 0x060004D8 RID: 1240 RVA: 0x0001C970 File Offset: 0x0001AB70
		// (set) Token: 0x060004D9 RID: 1241 RVA: 0x0001C978 File Offset: 0x0001AB78
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

		// Token: 0x17000192 RID: 402
		// (get) Token: 0x060004DA RID: 1242 RVA: 0x0001C984 File Offset: 0x0001AB84
		// (set) Token: 0x060004DB RID: 1243 RVA: 0x0001C98C File Offset: 0x0001AB8C
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

		// Token: 0x17000193 RID: 403
		// (get) Token: 0x060004DC RID: 1244 RVA: 0x0001C998 File Offset: 0x0001AB98
		// (set) Token: 0x060004DD RID: 1245 RVA: 0x0001C9A0 File Offset: 0x0001ABA0
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

		// Token: 0x17000194 RID: 404
		// (get) Token: 0x060004DE RID: 1246 RVA: 0x0001C9AC File Offset: 0x0001ABAC
		// (set) Token: 0x060004DF RID: 1247 RVA: 0x0001C9B4 File Offset: 0x0001ABB4
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

		// Token: 0x17000195 RID: 405
		// (get) Token: 0x060004E0 RID: 1248 RVA: 0x0001C9C0 File Offset: 0x0001ABC0
		// (set) Token: 0x060004E1 RID: 1249 RVA: 0x0001C9C8 File Offset: 0x0001ABC8
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

		// Token: 0x17000196 RID: 406
		// (get) Token: 0x060004E2 RID: 1250 RVA: 0x0001C9D4 File Offset: 0x0001ABD4
		// (set) Token: 0x060004E3 RID: 1251 RVA: 0x0001C9DC File Offset: 0x0001ABDC
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

		// Token: 0x060004E4 RID: 1252 RVA: 0x0001C9E8 File Offset: 0x0001ABE8
		public PulseView()
		{
			this.InitializeComponent();
		}

		// Token: 0x060004E5 RID: 1253 RVA: 0x0001CA00 File Offset: 0x0001AC00
		protected override void OnPaint(PaintEventArgs pe)
		{
			if (this.pulseShape == null)
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
			if (this.pulseShape != null)
			{
				float x = (float)num;
				float y = (float)(-(float)this.pulseShape[0]) * num3 + num2;
				float num6 = (float)num + (float)this.peakIndex * (float)width / (float)(this.pulseShapeSize - 1);
				float num5 = -100f * num3 + num2;
				graphics.DrawLine(pen, num6, num5, num6, (float)base.Height);
				if (this.antiAliasing)
				{
					graphics.SmoothingMode = SmoothingMode.AntiAlias;
					graphics.PixelOffsetMode = PixelOffsetMode.Half;
				}
				for (int i = 0; i < this.pulseShapeSize; i++)
				{
					num6 = (float)num + (float)i * (float)width / (float)(this.pulseShapeSize - 1);
					num5 = (float)(-(float)this.pulseShape[i]) * num3 + num2;
					graphics.DrawLine(this.isValidPulse ? Pens.Yellow : Pens.Red, x, y, num6, num5);
					x = num6;
					y = num5;
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
            }
			pen.Dispose();
			pen2.Dispose();
			base.OnPaint(pe);
		}

		// Token: 0x04000234 RID: 564
		IContainer components;

		// Token: 0x04000235 RID: 565
		double[] pulseShape;

		// Token: 0x04000236 RID: 566
		int pulseShapeSize;

		// Token: 0x04000237 RID: 567
		bool isValidPulse = true;

		// Token: 0x04000238 RID: 568
		int peakIndex;

		// Token: 0x04000239 RID: 569
		double pulseHeight;

		double maxpulseHeight;

		// Token: 0x0400023A RID: 570
		bool antiAliasing;
	}
}
