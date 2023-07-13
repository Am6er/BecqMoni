namespace BecquerelMonitor
{
	// Token: 0x02000029 RID: 41
	public partial class DCStatusMessageView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x06000234 RID: 564 RVA: 0x00008DD4 File Offset: 0x00006FD4
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000235 RID: 565 RVA: 0x00008DFC File Offset: 0x00006FFC
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCStatusMessageView));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.statusMessage1 = new global::BecquerelMonitor.Controls.StatusMessage();
			base.SuspendLayout();
			this.statusMessage1.BackColor = global::System.Drawing.Color.Black;
			resources.ApplyResources(this.statusMessage1, "statusMessage1");
			this.statusMessage1.ForeColor = global::System.Drawing.Color.White;
            this.statusMessage1.Message = "The sample is being measured.";
			this.statusMessage1.MessageColor = global::System.Drawing.Color.Red;
			this.statusMessage1.Name = "statusMessage1";
			resources.ApplyResources(this, "$this");
			base.Controls.Add(this.statusMessage1);
			base.HideOnClose = true;
			base.Name = "DCStatusMessageView";
			base.Load += new global::System.EventHandler(this.DCStatusMessageView_Load);
			base.ResumeLayout(false);
		}

		// Token: 0x040000A3 RID: 163
		global::System.ComponentModel.IContainer components;

		// Token: 0x040000A4 RID: 164
		global::BecquerelMonitor.Controls.StatusMessage statusMessage1;
	}
}
