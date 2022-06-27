using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BecquerelMonitor.Controls
{
	// Token: 0x020000AB RID: 171
	public class StatusMessage : UserControl
	{
		// Token: 0x06000870 RID: 2160 RVA: 0x00030558 File Offset: 0x0002E758
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000871 RID: 2161 RVA: 0x00030580 File Offset: 0x0002E780
		void InitializeComponent()
		{
			base.SuspendLayout();
			base.AutoScaleDimensions = new SizeF(6f, 12f);
			base.AutoScaleMode = AutoScaleMode.Font;
			this.BackColor = Color.Black;
			this.DoubleBuffered = true;
			this.ForeColor = Color.White;
			base.Name = "StatusMessage";
			base.Size = new Size(348, 77);
			base.Load += this.StatusMessage_Load;
			base.SizeChanged += this.StatusMessage_SizeChanged;
			base.ResumeLayout(false);
		}

		// Token: 0x17000262 RID: 610
		// (get) Token: 0x06000872 RID: 2162 RVA: 0x00030618 File Offset: 0x0002E818
		// (set) Token: 0x06000873 RID: 2163 RVA: 0x00030620 File Offset: 0x0002E820
		public string Message
		{
			get
			{
				return this.message;
			}
			set
			{
				this.message = value;
				this.x = 0;
				this.prevX = 1;
				this.pauseCount = this.initPauseCount;
				this.flashCount = 10;
				base.Invalidate();
			}
		}

		// Token: 0x17000263 RID: 611
		// (get) Token: 0x06000874 RID: 2164 RVA: 0x00030654 File Offset: 0x0002E854
		// (set) Token: 0x06000875 RID: 2165 RVA: 0x0003065C File Offset: 0x0002E85C
		public Color MessageColor
		{
			get
			{
				return this.messageColor;
			}
			set
			{
				this.messageColor = value;
			}
		}

		// Token: 0x17000264 RID: 612
		// (get) Token: 0x06000876 RID: 2166 RVA: 0x00030668 File Offset: 0x0002E868
		// (set) Token: 0x06000877 RID: 2167 RVA: 0x00030670 File Offset: 0x0002E870
		public bool DoScroll
		{
			get
			{
				return this.doScroll;
			}
			set
			{
				this.doScroll = value;
			}
		}

		// Token: 0x06000878 RID: 2168 RVA: 0x0003067C File Offset: 0x0002E87C
		public StatusMessage()
		{
			this.InitializeComponent();
			this.RecalcDimensions();
		}

		// Token: 0x06000879 RID: 2169 RVA: 0x000306EC File Offset: 0x0002E8EC
		void StatusMessage_Load(object sender, EventArgs e)
		{
			this.timer = new Timer();
			this.timer.Interval = 33;
			this.timer.Tick += this.OnTimer;
			this.timer.Start();
		}

		// Token: 0x0600087A RID: 2170 RVA: 0x00030728 File Offset: 0x0002E928
		void OnTimer(object sender, EventArgs e)
		{
			this.flashCount--;
			if (this.flashCount < 0)
			{
				this.flashCount = this.initFlashCount;
			}
			base.Invalidate();
			if (this.prevX > 0 && this.x <= 0)
			{
				this.pauseCount--;
				if (this.pauseCount >= 0)
				{
					return;
				}
			}
			else
			{
				this.pauseCount = this.initPauseCount;
			}
			this.prevX = this.x;
			this.x -= this.speed;
			if (this.x < this.minimumX)
			{
				this.x = this.maximumX;
			}
		}

		// Token: 0x0600087B RID: 2171 RVA: 0x000307E0 File Offset: 0x0002E9E0
		protected override void OnPaintBackground(PaintEventArgs e)
		{
			base.OnPaintBackground(e);
		}

		// Token: 0x0600087C RID: 2172 RVA: 0x000307EC File Offset: 0x0002E9EC
		protected override void OnPaint(PaintEventArgs pe)
		{
			Graphics graphics = pe.Graphics;
			this.DrawMessage(graphics);
		}

		// Token: 0x0600087D RID: 2173 RVA: 0x0003080C File Offset: 0x0002EA0C
		public void DrawMessage(Graphics g)
		{
			Brush brush;
			if (this.flashCount <= 10)
			{
				Color white = Color.White;
				Color color = this.messageColor;
				int red = (int)color.R + (int)(white.R - color.R) * this.flashCount / 10;
				int green = (int)color.G + (int)(white.G - color.G) * this.flashCount / 10;
				int blue = (int)color.B + (int)(white.B - color.B) * this.flashCount / 10;
				Color color2 = Color.FromArgb(red, green, blue);
				brush = new SolidBrush(color2);
			}
			else
			{
				brush = new SolidBrush(this.messageColor);
			}
			g.TextRenderingHint = TextRenderingHint.AntiAlias;
			Size size = TextRenderer.MeasureText(g, this.message, this.font);
			this.minimumX = -size.Width;
			g.DrawString(this.message, this.font, brush, (float)(this.doScroll ? this.x : 0), (float)(base.Height / 2 - size.Height / 2));
			brush.Dispose();
		}

		// Token: 0x0600087E RID: 2174 RVA: 0x00030934 File Offset: 0x0002EB34
		void StatusMessage_SizeChanged(object sender, EventArgs e)
		{
			this.RecalcDimensions();
			base.Invalidate();
		}

		// Token: 0x0600087F RID: 2175 RVA: 0x00030944 File Offset: 0x0002EB44
		void RecalcDimensions()
		{
			this.maximumX = base.Width;
			if (this.font != null)
			{
				this.font.Dispose();
			}
			this.fontSize = base.Height * 2 / 3;
			if (this.fontSize < 16)
			{
				this.fontSize = 16;
			}
			this.font = new Font("メイリオ", (float)this.fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
			this.speed = base.Height / 32;
		}

		// Token: 0x0400045E RID: 1118
		IContainer components;

		// Token: 0x0400045F RID: 1119
		Timer timer;

		// Token: 0x04000460 RID: 1120
		string message = "";

		// Token: 0x04000461 RID: 1121
		Color messageColor = Color.Red;

		// Token: 0x04000462 RID: 1122
		int fontSize = 16;

		// Token: 0x04000463 RID: 1123
		int prevX = 1;

		// Token: 0x04000464 RID: 1124
		int x;

		// Token: 0x04000465 RID: 1125
		int minimumX = -100;

		// Token: 0x04000466 RID: 1126
		int maximumX = 100;

		// Token: 0x04000467 RID: 1127
		Font font;

		// Token: 0x04000468 RID: 1128
		int speed = 2;

		// Token: 0x04000469 RID: 1129
		bool doScroll;

		// Token: 0x0400046A RID: 1130
		int pauseCount;

		// Token: 0x0400046B RID: 1131
		int initPauseCount = 25;

		// Token: 0x0400046C RID: 1132
		int flashCount;

		// Token: 0x0400046D RID: 1133
		int initFlashCount = 50;
	}
}
