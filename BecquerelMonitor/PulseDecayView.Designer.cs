using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x020000E3 RID: 227
	public partial class PulseDecayView : UserControl
	{
		// Token: 0x06000B29 RID: 2857 RVA: 0x00045F08 File Offset: 0x00044108
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000B2A RID: 2858 RVA: 0x00045F30 File Offset: 0x00044130
		void InitializeComponent()
		{
			this.SuspendLayout();
			this.AutoScaleDimensions = new SizeF(6f, 12f);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.BackColor = Color.Black;
			this.DoubleBuffered = true;
			this.Name = "PulseDecayView";
			this.Size = new Size(947, 265);
			this.ResumeLayout(false);
		}

		// Token: 0x04000752 RID: 1874
		IContainer components = null;
	}
}
