namespace BecquerelMonitor
{
	// Token: 0x0200004E RID: 78
	public partial class DCResultView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x0600043B RID: 1083 RVA: 0x00013D50 File Offset: 0x00011F50
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600043C RID: 1084 RVA: 0x00013D78 File Offset: 0x00011F78
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCResultView));
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.comboBox2 = new global::System.Windows.Forms.ComboBox();
			this.button1 = new global::System.Windows.Forms.Button();
			this.comboBox1 = new global::System.Windows.Forms.ComboBox();
			this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.textColumn1 = new global::XPTable.Models.TextColumn();
			this.textColumn2 = new global::XPTable.Models.NumberColumn();
			this.textColumn3 = new global::XPTable.Models.TextColumn();
			this.textColumn4 = new global::XPTable.Models.NumberColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			base.SuspendLayout();
			resources.ApplyResources(this.comboBox2, "comboBox2");
			this.comboBox2.DropDownStyle = global::System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBox2.FormattingEnabled = true;
			this.comboBox2.Items.AddRange(new object[]
			{
				resources.GetString("comboBox2.Items"),
				resources.GetString("comboBox2.Items1")
			});
			this.comboBox2.Name = "comboBox2";
			this.comboBox2.SelectedIndexChanged += new global::System.EventHandler(this.comboBox2_SelectedIndexChanged);
			resources.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			resources.ApplyResources(this.comboBox1, "comboBox1");
			this.comboBox1.DropDownStyle = global::System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBox1.FormattingEnabled = true;
			this.comboBox1.Items.AddRange(new object[]
			{
				resources.GetString("comboBox1.Items"),
				resources.GetString("comboBox1.Items1"),
				resources.GetString("comboBox1.Items2"),
				resources.GetString("comboBox1.Items3"),
				resources.GetString("comboBox1.Items4")
			});
			this.comboBox1.Name = "comboBox1";
			this.comboBox1.SelectedIndexChanged += new global::System.EventHandler(this.comboBox1_SelectedIndexChanged);
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
			this.table1.Name = "table1";
			this.table1.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn1,
				this.textColumn2,
				this.textColumn3,
				this.textColumn4
			});
			resources.ApplyResources(this.textColumn1, "textColumn1");
			this.textColumn1.Editable = false;
			this.textColumn1.IsTextTrimmed = false;
			this.textColumn2.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			resources.ApplyResources(this.textColumn2, "textColumn2");
			this.textColumn2.Editable = false;
			this.textColumn2.IsTextTrimmed = false;
			this.textColumn3.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			resources.ApplyResources(this.textColumn3, "textColumn3");
			this.textColumn3.Editable = false;
			this.textColumn3.IsTextTrimmed = false;
			this.textColumn3.Width= 115;
			this.textColumn4.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			resources.ApplyResources(this.textColumn4, "textColumn4");
			this.textColumn4.Editable = false;
			this.textColumn4.IsTextTrimmed = false;
			resources.ApplyResources(this, "$this");
			base.Controls.Add(this.comboBox2);
			base.Controls.Add(this.comboBox1);
			base.Controls.Add(this.button1);
			base.Controls.Add(this.table1);
			base.Name = "DCResultView";
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			base.ResumeLayout(false);
		}

		// Token: 0x040001A8 RID: 424
		global::System.ComponentModel.IContainer components;

		// Token: 0x040001A9 RID: 425
		global::XPTable.Models.Table table1;

		// Token: 0x040001AA RID: 426
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x040001AB RID: 427
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x040001AC RID: 428
		global::XPTable.Models.NumberColumn textColumn2;

		// Token: 0x040001AD RID: 429
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x040001AE RID: 430
		global::XPTable.Models.TextColumn textColumn3;

		// Token: 0x040001AF RID: 431
		global::System.Windows.Forms.Button button1;

		// Token: 0x040001B0 RID: 432
		global::System.Windows.Forms.ComboBox comboBox2;

		// Token: 0x040001B1 RID: 433
		global::System.Windows.Forms.ComboBox comboBox1;

		// Token: 0x040001B2 RID: 434
		global::XPTable.Models.NumberColumn textColumn4;
	}
}
