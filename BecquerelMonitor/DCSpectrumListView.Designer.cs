namespace BecquerelMonitor
{
	// Token: 0x0200002F RID: 47
	public partial class DCSpectrumListView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x0600027B RID: 635 RVA: 0x0000A7B8 File Offset: 0x000089B8
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600027C RID: 636 RVA: 0x0000A7E0 File Offset: 0x000089E0
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCSpectrumListView));
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.checkBoxColumn1 = new global::XPTable.Models.CheckBoxColumn();
			this.textColumn2 = new global::XPTable.Models.TextColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.button2 = new global::System.Windows.Forms.Button();
			this.button1 = new global::System.Windows.Forms.Button();
			this.button3 = new global::System.Windows.Forms.Button();
			this.button4 = new global::System.Windows.Forms.Button();
			this.button5 = new global::System.Windows.Forms.Button();
			this.button6 = new global::System.Windows.Forms.Button();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			base.SuspendLayout();
			resources.ApplyResources(this.table1, "table1");
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
			this.table1.MultiSelect = true;
			this.table1.Name = "table1";
			this.table1.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table1.SuppressEditorTerminatorBeep = true;
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table1.CellCheckChanged += new global::XPTable.Events.CellCheckBoxEventHandler(this.table1_CellCheckChanged);
			this.table1.EditingStopped += new global::XPTable.Events.CellEditEventHandler(this.table1_EditingStopped);
			this.table1.SelectionChanged += new global::XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.checkBoxColumn1,
				this.textColumn2
			});
			resources.ApplyResources(this.checkBoxColumn1, "checkBoxColumn1");
			this.checkBoxColumn1.DrawText = false;
			this.checkBoxColumn1.IsTextTrimmed = false;
			this.checkBoxColumn1.Sortable = false;
			resources.ApplyResources(this.textColumn2, "textColumn2");
			this.textColumn2.IsTextTrimmed = false;
			this.textColumn2.Sortable = false;
			resources.ApplyResources(this.button2, "button2");
			this.button2.Name = "button2";
			this.button2.UseVisualStyleBackColor = true;
			this.button2.Click += new global::System.EventHandler(this.button2_Click);
			resources.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			resources.ApplyResources(this.button3, "button3");
			this.button3.Name = "button3";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += new global::System.EventHandler(this.button3_Click);
			resources.ApplyResources(this.button4, "button4");
			this.button4.Name = "button4";
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new global::System.EventHandler(this.button4_Click);
			resources.ApplyResources(this.button5, "button5");
			this.button5.Image = global::BecquerelMonitor.Properties.Resources.Up;
			this.button5.Name = "button5";
			this.button5.TabStop = false;
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new global::System.EventHandler(this.button5_Click);
			resources.ApplyResources(this.button6, "button6");
			this.button6.Image = global::BecquerelMonitor.Properties.Resources.Down;
			this.button6.Name = "button6";
			this.button6.TabStop = false;
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new global::System.EventHandler(this.button6_Click);
			resources.ApplyResources(this, "$this");
			base.Controls.Add(this.button6);
			base.Controls.Add(this.button5);
			base.Controls.Add(this.button4);
			base.Controls.Add(this.button3);
			base.Controls.Add(this.button1);
			base.Controls.Add(this.button2);
			base.Controls.Add(this.table1);
			base.HideOnClose = true;
			base.Name = "DCSpectrumListView";
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			base.ResumeLayout(false);
		}

		// Token: 0x040000BE RID: 190
		global::System.ComponentModel.IContainer components;

		// Token: 0x040000BF RID: 191
		global::XPTable.Models.Table table1;

		// Token: 0x040000C0 RID: 192
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x040000C1 RID: 193
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x040000C2 RID: 194
		global::XPTable.Models.CheckBoxColumn checkBoxColumn1;

		// Token: 0x040000C3 RID: 195
		global::XPTable.Models.TextColumn textColumn2;

		// Token: 0x040000C4 RID: 196
		global::System.Windows.Forms.Button button2;

		// Token: 0x040000C5 RID: 197
		global::System.Windows.Forms.Button button1;

		// Token: 0x040000C6 RID: 198
		global::System.Windows.Forms.Button button3;

		// Token: 0x040000C7 RID: 199
		global::System.Windows.Forms.Button button4;

		// Token: 0x040000C8 RID: 200
		global::System.Windows.Forms.Button button5;

		// Token: 0x040000C9 RID: 201
		global::System.Windows.Forms.Button button6;
	}
}
