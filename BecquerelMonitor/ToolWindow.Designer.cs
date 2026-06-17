namespace BecquerelMonitor
{
	// Token: 0x02000018 RID: 24
	public partial class ToolWindow : global::WeifenLuo.WinFormsUI.Docking.DockContent
	{
		// Token: 0x060000DA RID: 218 RVA: 0x00004BC0 File Offset: 0x00002DC0
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060000DB RID: 219 RVA: 0x00004BE8 File Offset: 0x00002DE8
		void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ToolWindow));
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.option1ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.option2ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.option3ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.option1ToolStripMenuItem,
            this.option2ToolStripMenuItem,
            this.option3ToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            resources.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
            // 
            // option1ToolStripMenuItem
            // 
            this.option1ToolStripMenuItem.Name = "option1ToolStripMenuItem";
            resources.ApplyResources(this.option1ToolStripMenuItem, "option1ToolStripMenuItem");
            // 
            // option2ToolStripMenuItem
            // 
            this.option2ToolStripMenuItem.Name = "option2ToolStripMenuItem";
            resources.ApplyResources(this.option2ToolStripMenuItem, "option2ToolStripMenuItem");
            // 
            // option3ToolStripMenuItem
            // 
            this.option3ToolStripMenuItem.Name = "option3ToolStripMenuItem";
            resources.ApplyResources(this.option3ToolStripMenuItem, "option3ToolStripMenuItem");
            // 
            // ToolWindow
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.DockAreas = ((WeifenLuo.WinFormsUI.Docking.DockAreas)(((((WeifenLuo.WinFormsUI.Docking.DockAreas.Float | WeifenLuo.WinFormsUI.Docking.DockAreas.DockLeft) 
            | WeifenLuo.WinFormsUI.Docking.DockAreas.DockRight) 
            | WeifenLuo.WinFormsUI.Docking.DockAreas.DockTop) 
            | WeifenLuo.WinFormsUI.Docking.DockAreas.DockBottom)));
            this.Name = "ToolWindow";
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

		}

		// Token: 0x04000054 RID: 84
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000055 RID: 85
		global::System.Windows.Forms.ContextMenuStrip contextMenuStrip1;

		// Token: 0x04000056 RID: 86
		global::System.Windows.Forms.ToolStripMenuItem option1ToolStripMenuItem;

		// Token: 0x04000057 RID: 87
		global::System.Windows.Forms.ToolStripMenuItem option2ToolStripMenuItem;

		// Token: 0x04000058 RID: 88
		global::System.Windows.Forms.ToolStripMenuItem option3ToolStripMenuItem;
	}
}
