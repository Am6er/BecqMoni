namespace BecquerelMonitor
{
	// Token: 0x0200001A RID: 26
	public partial class DCSpectrumExplorerView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x060000E2 RID: 226 RVA: 0x00004FB8 File Offset: 0x000031B8
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060000E3 RID: 227 RVA: 0x00004FE0 File Offset: 0x000031E0
		void InitializeComponent()
		{
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);

            this.splitContainer1 = new global::System.Windows.Forms.SplitContainer();
			this.splitContainer2 = new global::System.Windows.Forms.SplitContainer();
			this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.label1 = new global::System.Windows.Forms.Label();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.splitContainer2.Panel1.SuspendLayout();
			this.splitContainer2.Panel2.SuspendLayout();
			this.splitContainer2.SuspendLayout();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			base.SuspendLayout();
			this.splitContainer1.Dock = global::System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.Location = new global::System.Drawing.Point(0, 0);
			this.splitContainer1.Name = "splitContainer1";
			this.splitContainer1.Orientation = global::System.Windows.Forms.Orientation.Horizontal;
			this.splitContainer1.Panel2.Controls.Add(this.splitContainer2);
			this.splitContainer1.Size = new global::System.Drawing.Size(356, 479);
			this.splitContainer1.SplitterDistance = 113;
			this.splitContainer1.TabIndex = 2;
			this.splitContainer2.Dock = global::System.Windows.Forms.DockStyle.Fill;
			this.splitContainer2.Location = new global::System.Drawing.Point(0, 0);
			this.splitContainer2.Name = "splitContainer2";
			this.splitContainer2.Orientation = global::System.Windows.Forms.Orientation.Horizontal;
			this.splitContainer2.Panel1.Controls.Add(this.table1);
			this.splitContainer2.Panel2.Controls.Add(this.label1);
			this.splitContainer2.Size = new global::System.Drawing.Size(356, 362);
			this.splitContainer2.SplitterDistance = 295;
			this.splitContainer2.TabIndex = 0;
			this.table1.BorderColor = global::System.Drawing.Color.Black;
			this.table1.ColumnModel = this.columnModel1;
			this.table1.DataMember = null;
			this.table1.DataSourceColumnBinder = dataSourceColumnBinder;
			this.table1.Dock = global::System.Windows.Forms.DockStyle.Fill;
			dragDropRenderer.ForeColor = global::System.Drawing.Color.Red;
			this.table1.DragDropRenderer = dragDropRenderer;
			this.table1.GridLinesContrainedToData = false;
            this.table1.HeaderFont = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
			this.table1.Location = new global::System.Drawing.Point(0, 0);
			this.table1.Name = "table1";
            this.table1.NoItemsText = "There is no spectrum file.";
			this.table1.Size = new global::System.Drawing.Size(356, 295);
			this.table1.TabIndex = 0;
			this.table1.TableModel = this.tableModel1;
			this.table1.Text = "table1";
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.label1.AutoSize = true;
			this.label1.Location = new global::System.Drawing.Point(12, 9);
			this.label1.Name = "label1";
			this.label1.Size = new global::System.Drawing.Size(138, 12);
			this.label1.TabIndex = 0;
            this.label1.Text = "Spectrum preview (not implemented)";
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(6f, 12f);
			base.ClientSize = new global::System.Drawing.Size(356, 479);
			base.Controls.Add(this.splitContainer1);
			base.HideOnClose = true;
			base.Name = "DCSpectrumExplorerView";
            base.TabText = "Spectrum explorer";
            this.Text = "Spectrum explorer";
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.ResumeLayout(false);
			this.splitContainer2.Panel1.ResumeLayout(false);
			this.splitContainer2.Panel2.ResumeLayout(false);
			this.splitContainer2.Panel2.PerformLayout();
			this.splitContainer2.ResumeLayout(false);
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			base.ResumeLayout(false);
		}

		// Token: 0x0400005C RID: 92
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400005D RID: 93
		global::System.Windows.Forms.SplitContainer splitContainer1;

		// Token: 0x0400005E RID: 94
		global::System.Windows.Forms.SplitContainer splitContainer2;

		// Token: 0x0400005F RID: 95
		global::XPTable.Models.Table table1;

		// Token: 0x04000060 RID: 96
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x04000061 RID: 97
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x04000062 RID: 98
		global::System.Windows.Forms.Label label1;
	}
}
