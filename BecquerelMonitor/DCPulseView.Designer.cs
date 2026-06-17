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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCPulseView));
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.pulseView2 = new System.Windows.Forms.Panel();
            this.pulseView1 = new System.Windows.Forms.Panel();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // checkBox1
            // 
            resources.ApplyResources(this.checkBox1, "checkBox1");
            this.checkBox1.Checked = true;
            this.checkBox1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // pulseView2
            // 
            this.pulseView2.BackColor = System.Drawing.Color.Black;
            resources.ApplyResources(this.pulseView2, "pulseView2");
            this.pulseView2.Name = "pulseView2";
            // 
            // pulseView1
            // 
            this.pulseView1.BackColor = System.Drawing.Color.Black;
            resources.ApplyResources(this.pulseView1, "pulseView1");
            this.pulseView1.Name = "pulseView1";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // checkBox2
            // 
            resources.ApplyResources(this.checkBox2, "checkBox2");
            this.checkBox2.Checked = true;
            this.checkBox2.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.UseVisualStyleBackColor = true;
            this.checkBox2.CheckedChanged += new System.EventHandler(this.checkBox2_CheckedChanged);
            // 
            // DCPulseView
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.checkBox2);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.pulseView2);
            this.Controls.Add(this.pulseView1);
            this.HideOnClose = true;
            this.Name = "DCPulseView";
            this.SizeChanged += new System.EventHandler(this.DCPulseView_SizeChanged);
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		// Token: 0x0400052E RID: 1326
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400052F RID: 1327
		global::System.Windows.Forms.CheckBox checkBox1;

		// Token: 0x04000530 RID: 1328
		global::System.Windows.Forms.Panel pulseView2;

		// Token: 0x04000531 RID: 1329
		global::System.Windows.Forms.Panel pulseView1;

		// Token: 0x04000532 RID: 1330
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000533 RID: 1331
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000534 RID: 1332
		global::System.Windows.Forms.CheckBox checkBox2;
	}
}
