namespace BecquerelMonitor
{
	// Token: 0x02000019 RID: 25
	public partial class DCSpectrumView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x060000DF RID: 223 RVA: 0x00004E00 File Offset: 0x00003000
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060000E0 RID: 224 RVA: 0x00004E28 File Offset: 0x00003028
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCSpectrumView));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.energySpectrumView1 = new global::BecquerelMonitor.EnergySpectrumView();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.energySpectrumView1, "energySpectrumView1");
			this.energySpectrumView1.ActiveResultDataIndex = 0;
			this.energySpectrumView1.BackColor = global::System.Drawing.Color.White;
			this.energySpectrumView1.BackgroundMode = global::BecquerelMonitor.BackgroundMode.Visible;
			this.energySpectrumView1.ChartType = global::BecquerelMonitor.ChartType.LineChart;
			this.energySpectrumView1.CursorChannel = 0;
			this.energySpectrumView1.CursorX = -1;
			this.energySpectrumView1.DrawingMode = global::BecquerelMonitor.DrawingMode.Normal;
			this.energySpectrumView1.HorizontalScale = 1.0;
			this.energySpectrumView1.HorizontalUnit = global::BecquerelMonitor.HorizontalUnit.Energy;
			this.energySpectrumView1.Name = "energySpectrumView1";
			this.energySpectrumView1.PeakMode = global::BecquerelMonitor.PeakMode.Visible;
			this.energySpectrumView1.ResultDataList = null;
			this.energySpectrumView1.SelectionEnd = -1;
			this.energySpectrumView1.SelectionStart = -1;
			this.energySpectrumView1.SmoothingMethod = global::BecquerelMonitor.SmoothingMethod.None;
			this.energySpectrumView1.VerticalFittingMode = global::BecquerelMonitor.VerticalFittingMode.MinMax;
			this.energySpectrumView1.VerticalScale = 1.0;
			this.energySpectrumView1.VerticalScaleType = global::BecquerelMonitor.VerticalScaleType.LogarithmicScale;
			this.energySpectrumView1.VerticalUnit = global::BecquerelMonitor.VerticalUnit.Counts;
			componentResourceManager.ApplyResources(this, "$this");
			base.Controls.Add(this.energySpectrumView1);
			base.Name = "DCSpectrumView";
			base.ResumeLayout(false);
		}

		// Token: 0x04000059 RID: 89
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400005A RID: 90
		global::BecquerelMonitor.EnergySpectrumView energySpectrumView1;
	}
}
