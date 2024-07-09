namespace BecquerelMonitor
{
	public partial class DCCountRateView : global::BecquerelMonitor.ToolWindow
	{
        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCCountRateView));
            this.timeConstantLbl = new System.Windows.Forms.Label();
            this.countsRateLbl = new System.Windows.Forms.Label();
            this.cpslabel = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.secLbl = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.windowControl = new System.Windows.Forms.NumericUpDown();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.windowControl)).BeginInit();
            this.SuspendLayout();
            // 
            // timeConstantLbl
            // 
            resources.ApplyResources(this.timeConstantLbl, "timeConstantLbl");
            this.timeConstantLbl.Name = "timeConstantLbl";
            // 
            // countsRateLbl
            // 
            resources.ApplyResources(this.countsRateLbl, "countsRateLbl");
            this.countsRateLbl.Name = "countsRateLbl";
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
            // secLbl
            // 
            resources.ApplyResources(this.secLbl, "secLbl");
            this.secLbl.Name = "secLbl";
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
            this.windowControl.InterceptArrowKeys = false;
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
            this.Controls.Add(this.secLbl);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.countsRateLbl);
            this.Controls.Add(this.timeConstantLbl);
            this.HideOnClose = true;
            this.Name = "DCCountRateView";
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.windowControl)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

        #endregion

        System.ComponentModel.IContainer components;
		System.Windows.Forms.Label timeConstantLbl;
        System.Windows.Forms.Label countsRateLbl;
		System.Windows.Forms.Label cpslabel;
		System.Windows.Forms.Label label4;
		System.Windows.Forms.Label secLbl;
		System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.NumericUpDown windowControl;
    }
}
