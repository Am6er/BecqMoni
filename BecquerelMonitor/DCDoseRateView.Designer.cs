namespace BecquerelMonitor
{
	// Token: 0x020000A6 RID: 166
	public partial class DCDoseRateView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x0600083C RID: 2108 RVA: 0x0002FBE8 File Offset: 0x0002DDE8
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600083D RID: 2109 RVA: 0x0002FC10 File Offset: 0x0002DE10
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCDoseRateView));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);

            this.button1 = new global::System.Windows.Forms.Button();
			this.comboBox1 = new global::System.Windows.Forms.ComboBox();
			this.label1 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label6 = new global::System.Windows.Forms.Label();
			this.panel1 = new global::System.Windows.Forms.Panel();
			this.panel1.SuspendLayout();
			base.SuspendLayout();
			resources.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			resources.ApplyResources(this.comboBox1, "comboBox1");
			this.comboBox1.FormattingEnabled = true;
			this.comboBox1.Items.AddRange(new object[]
			{
				resources.GetString("comboBox1.Items"),
				resources.GetString("comboBox1.Items1"),
				resources.GetString("comboBox1.Items2"),
				resources.GetString("comboBox1.Items3"),
				resources.GetString("comboBox1.Items4"),
				resources.GetString("comboBox1.Items5")
			});
			this.comboBox1.Name = "comboBox1";
			resources.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			resources.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			resources.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			resources.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			resources.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			resources.ApplyResources(this.panel1, "panel1");
			this.panel1.BackColor = global::System.Drawing.SystemColors.ControlLight;
			this.panel1.Controls.Add(this.label3);
			this.panel1.Name = "panel1";
			resources.ApplyResources(this, "$this");
			base.Controls.Add(this.panel1);
			base.Controls.Add(this.label6);
			base.Controls.Add(this.label4);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.comboBox1);
			base.Controls.Add(this.button1);
			base.HideOnClose = true;
			base.Name = "DCDoseRateView";
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000440 RID: 1088
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000441 RID: 1089
		global::System.Windows.Forms.Button button1;

		// Token: 0x04000442 RID: 1090
		global::System.Windows.Forms.ComboBox comboBox1;

		// Token: 0x04000443 RID: 1091
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000444 RID: 1092
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000445 RID: 1093
		global::System.Windows.Forms.Label label3;

		// Token: 0x04000446 RID: 1094
		global::System.Windows.Forms.Label label4;

		// Token: 0x04000447 RID: 1095
		global::System.Windows.Forms.Label label6;

		// Token: 0x04000448 RID: 1096
		global::System.Windows.Forms.Panel panel1;
	}
}
