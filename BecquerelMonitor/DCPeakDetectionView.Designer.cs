using XPTable.Models;

namespace BecquerelMonitor
{
	// Token: 0x0200004F RID: 79
	public partial class DCPeakDetectionView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x06000444 RID: 1092 RVA: 0x00014624 File Offset: 0x00012824
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000445 RID: 1093 RVA: 0x0001464C File Offset: 0x0001284C
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCPeakDetectionView));
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);

            this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.textColumn3 = new global::XPTable.Models.TextColumn();
			this.textColumn1 = new global::XPTable.Models.TextColumn();
			this.textColumn4 = new global::XPTable.Models.TextColumn();
			this.textColumn5 = new global::XPTable.Models.TextColumn();
			this.textColumn6 = new global::XPTable.Models.TextColumn();
			this.textColumn2 = new global::XPTable.Models.TextColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.numericUpDown1 = new global::System.Windows.Forms.NumericUpDown();
			this.numericUpDown2 = new global::System.Windows.Forms.NumericUpDown();
			this.label1 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.contextMenuStrip1 = new global::System.Windows.Forms.ContextMenuStrip();
			this.toolStripMenuItem1 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.numericUpDown3 = new global::System.Windows.Forms.NumericUpDown();
			this.button1 = new global::System.Windows.Forms.Button();
			this.label4 = new global::System.Windows.Forms.Label();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown1).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown2).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown3).BeginInit();
			this.contextMenuStrip1.SuspendLayout();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.table1, "table1");
			this.table1.BorderColor = global::System.Drawing.Color.Black;
			this.table1.ColumnModel = this.columnModel1;
			this.table1.DataMember = null;
			this.table1.DataSourceColumnBinder = dataSourceColumnBinder;
			dragDropRenderer.ForeColor = global::System.Drawing.Color.Red;
			this.table1.DragDropRenderer = dragDropRenderer;
			this.table1.FullRowSelect = true;
			this.table1.GridLines = global::XPTable.Models.GridLines.Both;
			this.table1.GridLinesContrainedToData = false;
            this.table1.HeaderFont = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
			this.table1.Name = "table1";
			this.table1.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table1.MultiSelect = true;
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn3,
				this.textColumn1,
				this.textColumn4,
				this.textColumn2,
				this.textColumn5,
				this.textColumn6
			});
			this.textColumn3.Editable = false;
			this.textColumn3.IsTextTrimmed = false;
			this.textColumn3.Sortable = false;
			componentResourceManager.ApplyResources(this.textColumn3, "textColumn3");
			this.textColumn1.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.textColumn1.Editable = false;
			this.textColumn1.IsTextTrimmed = false;
			this.textColumn1.Sortable = false;
			componentResourceManager.ApplyResources(this.textColumn1, "textColumn1");
			this.textColumn4.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.textColumn4.Editable = false;
			this.textColumn4.IsTextTrimmed = false;
			this.textColumn4.Sortable = false;
			componentResourceManager.ApplyResources(this.textColumn4, "textColumn4");
			this.textColumn2.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.textColumn2.Editable = false;
			this.textColumn2.IsTextTrimmed = false;
			this.textColumn2.Sortable = false;
			componentResourceManager.ApplyResources(this.textColumn5, "textColumn5");
			this.textColumn5.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.textColumn5.Editable = false;
			this.textColumn5.IsTextTrimmed = false;
			this.textColumn5.Sortable = false;
			componentResourceManager.ApplyResources(this.textColumn6, "textColumn6");
			this.textColumn6.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.textColumn6.Editable = false;
			this.textColumn6.IsTextTrimmed = false;
			this.textColumn6.Sortable = false;
			componentResourceManager.ApplyResources(this.textColumn2, "textColumn2");
			this.textColumn1.AutoResizeMode = ColumnAutoResizeMode.Grow;
			this.textColumn3.AutoResizeMode = ColumnAutoResizeMode.Grow;
			this.textColumn4.AutoResizeMode = ColumnAutoResizeMode.Grow;

			componentResourceManager.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
			this.contextMenuStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.toolStripMenuItem1
			});
			this.contextMenuStrip1.Name = "contextMenuStrip1";
			this.table1.ContextMenuStrip = this.contextMenuStrip1;
			this.contextMenuStrip1.Opening += this.ToolStripMenuItem1_Opening;
			componentResourceManager.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
			this.toolStripMenuItem1.Name = "toolStripMenuItem1";
			this.toolStripMenuItem1.Click += new global::System.EventHandler(this.ToolStripMenuItem1_Click);
			this.ContextMenuStrip = this.contextMenuStrip1;

			componentResourceManager.ApplyResources(this.numericUpDown1, "numericUpDown1");
			global::System.Windows.Forms.NumericUpDown numericUpDown = this.numericUpDown1;
			global::System.Windows.Forms.NumericUpDown numericUpDown2 = this.numericUpDown1;
			this.numericUpDown1.Name = "numericUpDown1";
			global::System.Windows.Forms.NumericUpDown numericUpDown3 = this.numericUpDown1;
			this.numericUpDown1.ValueChanged += new global::System.EventHandler(this.numericUpDown1_ValueChanged);
			global::System.Windows.Forms.NumericUpDown numericUpDown4 = this.numericUpDown2;
			componentResourceManager.ApplyResources(this.numericUpDown2, "numericUpDown2");
			global::System.Windows.Forms.NumericUpDown numericUpDown5 = this.numericUpDown2;
			global::System.Windows.Forms.NumericUpDown numericUpDown6 = this.numericUpDown2;
			this.numericUpDown2.Name = "numericUpDown2";
			global::System.Windows.Forms.NumericUpDown numericUpDown7 = this.numericUpDown2;
			this.numericUpDown2.ValueChanged += new global::System.EventHandler(this.numericUpDown2_ValueChanged);
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			this.numericUpDown3.DecimalPlaces = 2;
			componentResourceManager.ApplyResources(this.numericUpDown3, "numericUpDown3");
			global::System.Windows.Forms.NumericUpDown numericUpDown8 = this.numericUpDown3;
			this.numericUpDown3.Name = "numericUpDown3";
			global::System.Windows.Forms.NumericUpDown numericUpDown9 = this.numericUpDown3;
			int[] array9 = new int[4];
			array9[0] = 1;
			numericUpDown9.Value = new decimal(array9);
			this.numericUpDown3.ValueChanged += new global::System.EventHandler(this.numericUpDown3_ValueChanged);
			componentResourceManager.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this, "$this");
			base.Controls.Add(this.label4);
			base.Controls.Add(this.button1);
			base.Controls.Add(this.numericUpDown3);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.numericUpDown2);
			base.Controls.Add(this.numericUpDown1);
			base.Controls.Add(this.table1);
			base.HideOnClose = true;
			base.Name = "DCPeakDetectionView";
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown1).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown2).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown3).EndInit();
			this.contextMenuStrip1.ResumeLayout(false);
			base.ResumeLayout(false);
			base.PerformLayout();
		}

        // Token: 0x040001B6 RID: 438
        global::System.ComponentModel.IContainer components;

		// Token: 0x040001B7 RID: 439
		global::XPTable.Models.Table table1;

		// Token: 0x040001B8 RID: 440
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x040001B9 RID: 441
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x040001BA RID: 442
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x040001BB RID: 443
		global::XPTable.Models.TextColumn textColumn3;

		// Token: 0x040001BC RID: 444
		global::XPTable.Models.TextColumn textColumn2;

		global::XPTable.Models.TextColumn textColumn5;

		global::XPTable.Models.TextColumn textColumn6;

		// Token: 0x040001BD RID: 445
		global::System.Windows.Forms.NumericUpDown numericUpDown1;

		// Token: 0x040001BE RID: 446
		global::System.Windows.Forms.NumericUpDown numericUpDown2;

		// Token: 0x040001BF RID: 447
		global::System.Windows.Forms.Label label1;

		// Token: 0x040001C0 RID: 448
		global::System.Windows.Forms.Label label2;

		// Token: 0x040001C1 RID: 449
		global::System.Windows.Forms.Label label3;

		// Token: 0x040001C2 RID: 450
		global::System.Windows.Forms.NumericUpDown numericUpDown3;

		// Token: 0x040001C3 RID: 451
		global::XPTable.Models.TextColumn textColumn4;

		// Token: 0x040001C4 RID: 452
		global::System.Windows.Forms.Button button1;

		// Token: 0x040001C5 RID: 453
		global::System.Windows.Forms.Label label4;

		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;

		global::System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
	}
}
