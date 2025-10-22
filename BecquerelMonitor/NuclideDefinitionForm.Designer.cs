namespace BecquerelMonitor
{
	// Token: 0x02000017 RID: 23
	public partial class NuclideDefinitionForm : System.Windows.Forms.Form
	{
		// Token: 0x060000D7 RID: 215 RVA: 0x000043A0 File Offset: 0x000025A0
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060000D8 RID: 216 RVA: 0x000043C8 File Offset: 0x000025C8
		void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BecquerelMonitor.NuclideDefinitionForm));
			XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new XPTable.Models.DataSourceColumnBinder();
			XPTable.Renderers.DragDropRenderer dragDropRenderer = new XPTable.Renderers.DragDropRenderer();
			this.button4 = new System.Windows.Forms.Button();
			this.button3 = new System.Windows.Forms.Button();
			this.label5 = new System.Windows.Forms.Label();
			this.table1 = new XPTable.Models.Table();
			this.columnModel1 = new XPTable.Models.ColumnModel();
			this.textColumn1 = new XPTable.Models.TextColumn();
			this.textColumn2 = new XPTable.Models.NumberColumn();
			this.textColumn3 = new XPTable.Models.NumberColumn();
			this.tableModel1 = new XPTable.Models.TableModel();
			this.textBox1 = new System.Windows.Forms.TextBox();
			this.button6 = new System.Windows.Forms.Button();
			this.button5 = new System.Windows.Forms.Button();
			this.tabControl1 = new System.Windows.Forms.TabControl();
			this.tabPage1 = new System.Windows.Forms.TabPage();
			this.textBox2 = new System.Windows.Forms.TextBox();
			this.label7 = new System.Windows.Forms.Label();
			this.doubleTextBox2 = new BecquerelMonitor.DoubleTextBox();
			this.doubleTextBox1 = new BecquerelMonitor.DoubleTextBox();
			this.intensityTextBox = new BecquerelMonitor.DoubleTextBox();
			this.intensityLabel = new System.Windows.Forms.Label();
			this.intensitypercentLabel = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			this.label8 = new System.Windows.Forms.Label();
			this.checkBox1 = new System.Windows.Forms.CheckBox();
			this.colorComboBox1 = new ColorComboBox.ColorComboBox();
			((System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			this.tabControl1.SuspendLayout();
			this.tabPage1.SuspendLayout();
			base.SuspendLayout();
			resources.ApplyResources(this.button4, "button4");
			this.button4.Name = "button4";
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new System.EventHandler(this.button4_Click);
			resources.ApplyResources(this.button3, "button3");
			this.button3.Name = "button3";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += new System.EventHandler(this.button3_Click);
			resources.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			resources.ApplyResources(this.table1, "table1");
			this.table1.BorderColor = System.Drawing.Color.Black;
			this.table1.ColumnModel = this.columnModel1;
			this.table1.DataMember = null;
			this.table1.DataSourceColumnBinder = dataSourceColumnBinder;
			dragDropRenderer.ForeColor = System.Drawing.Color.Red;
			this.table1.DragDropRenderer = dragDropRenderer;
			this.table1.FullRowSelect = true;
			this.table1.GridLines = XPTable.Models.GridLines.Both;
			this.table1.GridLinesContrainedToData = false;
			this.table1.Name = "table1";
			this.table1.SortedColumnBackColor = System.Drawing.Color.White;
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
			this.table1.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
			this.table1.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table1_EditingStopped);
			this.columnModel1.Columns.AddRange(new XPTable.Models.Column[]
			{
				this.textColumn1,
				this.textColumn2,
				this.textColumn3
			});
			resources.ApplyResources(this.textColumn1, "textColumn1");
			this.textColumn1.IsTextTrimmed = false;
			this.textColumn2.Alignment = XPTable.Models.ColumnAlignment.Right;
			resources.ApplyResources(this.textColumn2, "textColumn2");
			this.textColumn2.IsTextTrimmed = false;
			this.textColumn3.Alignment = XPTable.Models.ColumnAlignment.Right;
			resources.ApplyResources(this.textColumn3, "textColumn3");
			this.textColumn3.IsTextTrimmed = false;
			resources.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
			resources.ApplyResources(this.button6, "button6");
			this.button6.Name = "button6";
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new System.EventHandler(this.button6_Click);
			resources.ApplyResources(this.button5, "button5");
			this.button5.Name = "button5";
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new System.EventHandler(this.button5_Click);
			resources.ApplyResources(this.tabControl1, "tabControl1");
			this.tabControl1.Controls.Add(this.tabPage1);
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			resources.ApplyResources(this.tabPage1, "tabPage1");
			this.tabPage1.BackColor = System.Drawing.SystemColors.Control;
			this.tabPage1.Controls.Add(this.textBox2);
			this.tabPage1.Controls.Add(this.label7);
			this.tabPage1.Controls.Add(this.label8);
			this.tabPage1.Controls.Add(this.colorComboBox1);
			this.tabPage1.Controls.Add(this.doubleTextBox2);
			this.tabPage1.Controls.Add(this.doubleTextBox1);
            this.tabPage1.Controls.Add(this.intensityLabel);
            this.tabPage1.Controls.Add(this.intensitypercentLabel);
            this.tabPage1.Controls.Add(this.intensityTextBox);
            this.tabPage1.Controls.Add(this.label6);
			this.tabPage1.Controls.Add(this.label4);
			this.tabPage1.Controls.Add(this.label3);
			this.tabPage1.Controls.Add(this.label2);
			this.tabPage1.Controls.Add(this.label1);
			this.tabPage1.Controls.Add(this.textBox1);
            this.tabPage1.Controls.Add(this.checkBox1);
            this.tabPage1.Name = "tabPage1";
			resources.ApplyResources(this.textBox2, "textBox2");
			this.textBox2.Name = "textBox2";
			this.textBox2.TextChanged += new System.EventHandler(this.textBox2_TextChanged);
			resources.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			resources.ApplyResources(this.label8, "label8");
			this.label7.Name = "label8";
			resources.ApplyResources(this.colorComboBox1, "colorComboBox1");
			this.colorComboBox1.Extended = true;
			this.colorComboBox1.Name = "colorComboBox1";
			this.colorComboBox1.SelectedColor = System.Drawing.Color.Black;
			this.colorComboBox1.ColorChanged += new ColorComboBox.ColorChangedHandler(this.colorComboBox1_ColorChanged);
            resources.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
			this.doubleTextBox2.Name = "doubleTextBox2";
			this.doubleTextBox2.TextChanged += new System.EventHandler(this.doubleTextBox2_TextChanged);
			resources.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
			this.doubleTextBox1.Name = "doubleTextBox1";
			this.doubleTextBox1.TextChanged += new System.EventHandler(this.doubleTextBox1_TextChanged);
            resources.ApplyResources(this.intensityTextBox, "intensityTextBox");
            this.intensityTextBox.Name = "intensityTextBox";
            this.intensityTextBox.TextChanged += new System.EventHandler(this.intensityTextBox_TextChanged);
            resources.ApplyResources(this.intensityLabel, "intensityLabel");
            this.intensityLabel.Name = "intensityLabel";
            resources.ApplyResources(this.intensitypercentLabel, "intensitypercentLabel");
            this.intensitypercentLabel.Name = "intensitypercentLabel";
            resources.ApplyResources(this.checkBox1, "checkBox1");
            this.checkBox1.Name = "checkBox1";
			this.checkBox1.Checked = true;
			this.checkBox1.CheckStateChanged += new System.EventHandler(this.checkBox1_CheckStateChanged);
            resources.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			resources.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			resources.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			resources.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			resources.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			resources.ApplyResources(this, "$this");
			base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			base.Controls.Add(this.tabControl1);
			base.Controls.Add(this.button6);
			base.Controls.Add(this.button5);
			base.Controls.Add(this.button4);
			base.Controls.Add(this.button3);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.table1);
			base.Name = "NuclideDefinitionForm";
			base.Load += new System.EventHandler(this.NuclideDefinitionForm_Load);
            base.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormNuclideDefinitionForm_FormClosing);
            ((System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			this.tabControl1.ResumeLayout(false);
			this.tabPage1.ResumeLayout(false);
			this.tabPage1.PerformLayout();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x0400003C RID: 60
		System.ComponentModel.IContainer components;

		// Token: 0x0400003D RID: 61
		System.Windows.Forms.Button button4;

		// Token: 0x0400003E RID: 62
		System.Windows.Forms.Button button3;

		// Token: 0x0400003F RID: 63
		System.Windows.Forms.Label label5;

		// Token: 0x04000040 RID: 64
		XPTable.Models.Table table1;

		// Token: 0x04000041 RID: 65
		XPTable.Models.ColumnModel columnModel1;

		// Token: 0x04000042 RID: 66
		XPTable.Models.TextColumn textColumn1;

		// Token: 0x04000043 RID: 67
		XPTable.Models.NumberColumn textColumn2;

		// Token: 0x04000044 RID: 68
		XPTable.Models.NumberColumn textColumn3;

		// Token: 0x04000045 RID: 69
		XPTable.Models.TableModel tableModel1;

		// Token: 0x04000046 RID: 70
		System.Windows.Forms.TextBox textBox1;

		// Token: 0x04000047 RID: 71
		System.Windows.Forms.Button button6;

		// Token: 0x04000048 RID: 72
		System.Windows.Forms.Button button5;

		// Token: 0x04000049 RID: 73
		System.Windows.Forms.TabControl tabControl1;

		// Token: 0x0400004A RID: 74
		System.Windows.Forms.TabPage tabPage1;

		// Token: 0x0400004B RID: 75
		BecquerelMonitor.DoubleTextBox doubleTextBox2;

		// Token: 0x0400004C RID: 76
		BecquerelMonitor.DoubleTextBox doubleTextBox1;

		BecquerelMonitor.DoubleTextBox intensityTextBox;

		System.Windows.Forms.Label intensityLabel;

        System.Windows.Forms.Label intensitypercentLabel;

        // Token: 0x0400004D RID: 77
        System.Windows.Forms.Label label6;

		// Token: 0x0400004E RID: 78
		System.Windows.Forms.Label label4;

		System.Windows.Forms.CheckBox checkBox1;

		// Token: 0x0400004F RID: 79
		System.Windows.Forms.Label label3;

		// Token: 0x04000050 RID: 80
		System.Windows.Forms.Label label2;

		// Token: 0x04000051 RID: 81
		System.Windows.Forms.Label label1;

		// Token: 0x04000052 RID: 82
		System.Windows.Forms.TextBox textBox2;

		// Token: 0x04000053 RID: 83
		System.Windows.Forms.Label label7;

		System.Windows.Forms.Label label8;

		ColorComboBox.ColorComboBox colorComboBox1;
	}
}
