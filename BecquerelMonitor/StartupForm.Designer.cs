namespace BecquerelMonitor
{
	// Token: 0x0200006C RID: 108
	public partial class StartupForm : global::System.Windows.Forms.Form
	{
		// Token: 0x0600059E RID: 1438 RVA: 0x00023704 File Offset: 0x00021904
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600059F RID: 1439 RVA: 0x0002372C File Offset: 0x0002192C
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.StartupForm));
			this.textBox1 = new global::System.Windows.Forms.TextBox();
			this.label1 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.panel1 = new global::System.Windows.Forms.Panel();
			this.panel2 = new global::System.Windows.Forms.Panel();
			this.iconPanel1 = new global::BecquerelMonitor.IconPanel();
			this.label5 = new global::System.Windows.Forms.Label();
			this.panel1.SuspendLayout();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.ReadOnly = true;
			this.textBox1.TabStop = false;
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			this.panel1.BackColor = global::System.Drawing.SystemColors.Control;
			this.panel1.BorderStyle = global::System.Windows.Forms.BorderStyle.FixedSingle;
			this.panel1.Controls.Add(this.panel2);
			this.panel1.Controls.Add(this.iconPanel1);
			this.panel1.Controls.Add(this.label5);
			this.panel1.Controls.Add(this.label4);
			this.panel1.Controls.Add(this.textBox1);
			this.panel1.Controls.Add(this.label3);
			this.panel1.Controls.Add(this.label1);
			componentResourceManager.ApplyResources(this.panel1, "panel1");
			this.panel1.Name = "panel1";
			componentResourceManager.ApplyResources(this.panel2, "panel2");
			this.panel2.BackColor = global::System.Drawing.SystemColors.ControlLight;
			this.panel2.Name = "panel2";
			componentResourceManager.ApplyResources(this.iconPanel1, "iconPanel1");
			this.iconPanel1.Name = "iconPanel1";
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.Controls.Add(this.panel1);
			base.FormBorderStyle = global::System.Windows.Forms.FormBorderStyle.None;
			base.Name = "StartupForm";
			base.ShowInTaskbar = false;
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			base.ResumeLayout(false);
		}

		// Token: 0x040002E1 RID: 737
		global::System.ComponentModel.IContainer components;

		// Token: 0x040002E2 RID: 738
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x040002E3 RID: 739
		global::System.Windows.Forms.Label label1;

		// Token: 0x040002E4 RID: 740
		global::System.Windows.Forms.Label label3;

		// Token: 0x040002E5 RID: 741
		global::System.Windows.Forms.Label label4;

		// Token: 0x040002E6 RID: 742
		global::System.Windows.Forms.Panel panel1;

		// Token: 0x040002E7 RID: 743
		global::BecquerelMonitor.IconPanel iconPanel1;

		// Token: 0x040002E8 RID: 744
		global::System.Windows.Forms.Panel panel2;

		// Token: 0x040002E9 RID: 745
		global::System.Windows.Forms.Label label5;
	}
}
