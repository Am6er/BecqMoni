using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor.Controls
{
	partial class StatusMessage
	{
		IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		void InitializeComponent()
		{
			this.SuspendLayout();
			this.AutoScaleDimensions = new SizeF(6f, 12f);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.BackColor = Color.Black;
			this.DoubleBuffered = true;
			this.ForeColor = Color.White;
			this.Name = "StatusMessage";
			this.Size = new Size(348, 77);
			this.Load += this.StatusMessage_Load;
			this.SizeChanged += this.StatusMessage_SizeChanged;
			this.ResumeLayout(false);
		}
	}
}
