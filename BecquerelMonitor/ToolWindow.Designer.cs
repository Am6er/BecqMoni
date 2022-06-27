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
			this.components = new global::System.ComponentModel.Container();
			this.contextMenuStrip1 = new global::System.Windows.Forms.ContextMenuStrip(this.components);
			this.option1ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.option2ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.option3ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.contextMenuStrip1.SuspendLayout();
			base.SuspendLayout();
			this.contextMenuStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.option1ToolStripMenuItem,
				this.option2ToolStripMenuItem,
				this.option3ToolStripMenuItem
			});
			this.contextMenuStrip1.Name = "contextMenuStrip1";
			this.contextMenuStrip1.Size = new global::System.Drawing.Size(122, 70);
			this.option1ToolStripMenuItem.Name = "option1ToolStripMenuItem";
			this.option1ToolStripMenuItem.Size = new global::System.Drawing.Size(121, 22);
			this.option1ToolStripMenuItem.Text = "Option&1";
			this.option2ToolStripMenuItem.Name = "option2ToolStripMenuItem";
			this.option2ToolStripMenuItem.Size = new global::System.Drawing.Size(121, 22);
			this.option2ToolStripMenuItem.Text = "Option&2";
			this.option3ToolStripMenuItem.Name = "option3ToolStripMenuItem";
			this.option3ToolStripMenuItem.Size = new global::System.Drawing.Size(121, 22);
			this.option3ToolStripMenuItem.Text = "Option&3";
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(6f, 12f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.ClientSize = new global::System.Drawing.Size(292, 246);
			base.DockAreas = (global::WeifenLuo.WinFormsUI.Docking.DockAreas.Float | global::WeifenLuo.WinFormsUI.Docking.DockAreas.DockLeft | global::WeifenLuo.WinFormsUI.Docking.DockAreas.DockRight | global::WeifenLuo.WinFormsUI.Docking.DockAreas.DockTop | global::WeifenLuo.WinFormsUI.Docking.DockAreas.DockBottom);
			this.Font = new global::System.Drawing.Font("MS UI Gothic", 9f, global::System.Drawing.FontStyle.Regular, global::System.Drawing.GraphicsUnit.Point, 128);
			base.Name = "ToolWindow";
			base.TabText = "ToolWindow";
			this.Text = "ToolWindow";
			this.contextMenuStrip1.ResumeLayout(false);
			base.ResumeLayout(false);
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
