namespace BecquerelMonitor
{
	// Token: 0x020000C1 RID: 193
	public partial class DCPulseView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x06000963 RID: 2403 RVA: 0x00036B44 File Offset: 0x00034D44
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000964 RID: 2404 RVA: 0x00036B6C File Offset: 0x00034D6C
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCPulseView));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);

            this.checkBox1 = new global::System.Windows.Forms.CheckBox();
			this.pulseView2 = new global::BecquerelMonitor.PulseView();
			this.pulseView1 = new global::BecquerelMonitor.PulseView();
			this.label1 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.checkBox2 = new global::System.Windows.Forms.CheckBox();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.checkBox1, "checkBox1");
			this.checkBox1.Checked = true;
			this.checkBox1.CheckState = global::System.Windows.Forms.CheckState.Checked;
			this.checkBox1.Name = "checkBox1";
			this.checkBox1.UseVisualStyleBackColor = true;
			this.checkBox1.CheckedChanged += new global::System.EventHandler(this.checkBox1_CheckedChanged);
			componentResourceManager.ApplyResources(this.pulseView2, "pulseView2");
			this.pulseView2.AntiAliasing = false;
			this.pulseView2.BackColor = global::System.Drawing.Color.Black;
			this.pulseView2.IsValidPulse = true;
			this.pulseView2.Name = "pulseView2";
			this.pulseView2.PeakIndex = 0;
			this.pulseView2.PulseHeight = 0.0;
			this.pulseView2.PulseShape = null;
			this.pulseView2.PulseShapeSize = 0;
			componentResourceManager.ApplyResources(this.pulseView1, "pulseView1");
			this.pulseView1.AntiAliasing = false;
			this.pulseView1.BackColor = global::System.Drawing.Color.Black;
			this.pulseView1.IsValidPulse = true;
			this.pulseView1.Name = "pulseView1";
			this.pulseView1.PeakIndex = 0;
			this.pulseView1.PulseHeight = 0.0;
			this.pulseView1.PulseShape = null;
			this.pulseView1.PulseShapeSize = 0;
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.checkBox2, "checkBox2");
			this.checkBox2.Checked = true;
			this.checkBox2.CheckState = global::System.Windows.Forms.CheckState.Checked;
			this.checkBox2.Name = "checkBox2";
			this.checkBox2.UseVisualStyleBackColor = true;
			this.checkBox2.CheckedChanged += new global::System.EventHandler(this.checkBox2_CheckedChanged);
			componentResourceManager.ApplyResources(this, "$this");
			base.Controls.Add(this.checkBox2);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.checkBox1);
			base.Controls.Add(this.pulseView2);
			base.Controls.Add(this.pulseView1);
			base.HideOnClose = true;
			base.Name = "DCPulseView";
			base.SizeChanged += new global::System.EventHandler(this.DCPulseView_SizeChanged);
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x0400052E RID: 1326
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400052F RID: 1327
		global::System.Windows.Forms.CheckBox checkBox1;

		// Token: 0x04000530 RID: 1328
		global::BecquerelMonitor.PulseView pulseView2;

		// Token: 0x04000531 RID: 1329
		global::BecquerelMonitor.PulseView pulseView1;

		// Token: 0x04000532 RID: 1330
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000533 RID: 1331
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000534 RID: 1332
		global::System.Windows.Forms.CheckBox checkBox2;
	}
}
