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
		private void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCResultView));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            this.comboBox2 = new System.Windows.Forms.ComboBox();
            this.button1 = new System.Windows.Forms.Button();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.table1 = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.textColumn1 = new XPTable.Models.TextColumn();
            this.textColumn2 = new XPTable.Models.NumberColumn();
            this.textColumn3 = new XPTable.Models.TextColumn();
            this.textColumn4 = new XPTable.Models.NumberColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).BeginInit();
            this.SuspendLayout();
            // 
            // comboBox2
            // 
            this.comboBox2.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox2.FormattingEnabled = true;
            this.comboBox2.Items.AddRange(new object[] {
            resources.GetString("comboBox2.Items"),
            resources.GetString("comboBox2.Items1")});
            resources.ApplyResources(this.comboBox2, "comboBox2");
            this.comboBox2.Name = "comboBox2";
            this.comboBox2.SelectedIndexChanged += new System.EventHandler(this.comboBox2_SelectedIndexChanged);
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // comboBox1
            // 
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Items.AddRange(new object[] {
            resources.GetString("comboBox1.Items"),
            resources.GetString("comboBox1.Items1"),
            resources.GetString("comboBox1.Items2"),
            resources.GetString("comboBox1.Items3"),
            resources.GetString("comboBox1.Items4")});
            resources.ApplyResources(this.comboBox1, "comboBox1");
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // table1
            // 
            resources.ApplyResources(this.table1, "table1");
            this.table1.BorderColor = System.Drawing.Color.Black;
            this.table1.ColumnModel = this.columnModel1;
            this.table1.DataMember = null;
            this.table1.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.table1.DragDropRenderer = dragDropRenderer1;
            this.table1.FullRowSelect = true;
            this.table1.GridLines = XPTable.Models.GridLines.Both;
            this.table1.GridLinesContrainedToData = false;
            this.table1.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 8F);
            this.table1.Name = "table1";
            this.table1.SortedColumnBackColor = System.Drawing.Color.White;
            this.table1.TableModel = this.tableModel1;
            this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn1,
            this.textColumn2,
            this.textColumn3,
            this.textColumn4});
            // 
            // textColumn1
            // 
            this.textColumn1.Editable = false;
            this.textColumn1.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn1, "textColumn1");
            // 
            // textColumn2
            // 
            this.textColumn2.Editable = false;
            this.textColumn2.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn2, "textColumn2");
            // 
            // textColumn3
            // 
            this.textColumn3.Editable = false;
            this.textColumn3.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn3, "textColumn3");
            // 
            // textColumn4
            // 
            this.textColumn4.Editable = false;
            this.textColumn4.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn4, "textColumn4");
            // 
            // DCResultView
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.comboBox2);
            this.Controls.Add(this.comboBox1);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.table1);
            this.HideOnClose = true;
            this.Name = "DCResultView";
            ((System.ComponentModel.ISupportInitialize)(this.table1)).EndInit();
            this.ResumeLayout(false);

		}

		// Token: 0x040001A8 RID: 424
		global::System.ComponentModel.IContainer components = null;

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
