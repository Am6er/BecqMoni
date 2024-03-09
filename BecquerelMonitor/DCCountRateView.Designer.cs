namespace BecquerelMonitor
{
	// Token: 0x020000A6 RID: 166
	public partial class DCCountRateView : global::BecquerelMonitor.ToolWindow
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCCountRateView));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.cpslabel = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.windowControl = new System.Windows.Forms.NumericUpDown();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.windowControl)).BeginInit();
            this.SuspendLayout();
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
            // cpslabel
            // 
            resources.ApplyResources(this.cpslabel, "cpslabel");
            this.cpslabel.Name = "cpslabel";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panel1.Controls.Add(this.cpslabel);
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // windowControl
            // 
            this.windowControl.DecimalPlaces = 2;
            this.windowControl.Increment = new decimal(new int[] {
            2,
            0,
            0,
            0});
            resources.ApplyResources(this.windowControl, "windowControl");
            this.windowControl.Maximum = new decimal(new int[] {
            120,
            0,
            0,
            0});
            this.windowControl.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.windowControl.Name = "windowControl";
            this.windowControl.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            // 
            // DCCountRateView
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.windowControl);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.HideOnClose = true;
            this.Name = "DCCountRateView";
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.windowControl)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		// Token: 0x04000440 RID: 1088
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000443 RID: 1091
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000444 RID: 1092
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000445 RID: 1093
		global::System.Windows.Forms.Label cpslabel;

		// Token: 0x04000446 RID: 1094
		global::System.Windows.Forms.Label label4;

		// Token: 0x04000447 RID: 1095
		global::System.Windows.Forms.Label label6;

		// Token: 0x04000448 RID: 1096
		global::System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.NumericUpDown windowControl;
    }
}
