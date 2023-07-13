namespace BecquerelMonitor
{
	// Token: 0x02000050 RID: 80
	public partial class ContextMenuForm : global::System.Windows.Forms.Form
	{
		// Token: 0x0600044E RID: 1102 RVA: 0x00014DFC File Offset: 0x00012FFC
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600044F RID: 1103 RVA: 0x00014E24 File Offset: 0x00013024
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.ContextMenuForm));
			this.panelMain = new global::System.Windows.Forms.Panel();
			base.SuspendLayout();
			this.panelMain.BackColor = global::System.Drawing.Color.White;
			this.panelMain.BorderStyle = global::System.Windows.Forms.BorderStyle.FixedSingle;
			this.panelMain.Dock = global::System.Windows.Forms.DockStyle.Fill;
			this.panelMain.Location = new global::System.Drawing.Point(0, 0);
			this.panelMain.Name = "panelMain";
			this.panelMain.Size = new global::System.Drawing.Size(292, 266);
			this.panelMain.TabIndex = 0;
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.None;
			this.BackColor = global::System.Drawing.Color.White;
			base.ClientSize = new global::System.Drawing.Size(292, 266);
			base.ControlBox = false;
			base.Controls.Add(this.panelMain);
			base.FormBorderStyle = global::System.Windows.Forms.FormBorderStyle.None;
			base.Icon = (global::System.Drawing.Icon)resources.GetObject("$this.Icon");
			base.Name = "ContextMenuForm";
			base.ShowIcon = false;
			base.ShowInTaskbar = false;
			base.SizeGripStyle = global::System.Windows.Forms.SizeGripStyle.Hide;
			base.StartPosition = global::System.Windows.Forms.FormStartPosition.Manual;
			this.Text = "ContextMenuPanel";
			base.Deactivate += new global::System.EventHandler(this.ContextMenuPanel_Deactivate);
			base.Leave += new global::System.EventHandler(this.ContextMenuPanel_Leave);
			base.ResumeLayout(false);
		}

		// Token: 0x040001C8 RID: 456
		global::System.ComponentModel.IContainer components;

		// Token: 0x040001C9 RID: 457
		global::System.Windows.Forms.Panel panelMain;
	}
}
