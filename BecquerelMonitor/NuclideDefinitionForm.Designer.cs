namespace BecquerelMonitor
{
	// Token: 0x02000017 RID: 23
	public partial class NuclideDefinitionForm : global::System.Windows.Forms.Form
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
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.NuclideDefinitionForm));
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();
			this.button4 = new global::System.Windows.Forms.Button();
			this.button3 = new global::System.Windows.Forms.Button();
			this.label5 = new global::System.Windows.Forms.Label();
			this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.textColumn1 = new global::XPTable.Models.TextColumn();
			this.textColumn2 = new global::XPTable.Models.NumberColumn();
			this.textColumn3 = new global::XPTable.Models.NumberColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.textBox1 = new global::System.Windows.Forms.TextBox();
			this.button6 = new global::System.Windows.Forms.Button();
			this.button5 = new global::System.Windows.Forms.Button();
			this.tabControl1 = new global::System.Windows.Forms.TabControl();
			this.tabPage1 = new global::System.Windows.Forms.TabPage();
			this.textBox2 = new global::System.Windows.Forms.TextBox();
			this.label7 = new global::System.Windows.Forms.Label();
			this.doubleTextBox2 = new global::BecquerelMonitor.DoubleTextBox();
			this.doubleTextBox1 = new global::BecquerelMonitor.DoubleTextBox();
			this.label6 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label1 = new global::System.Windows.Forms.Label();
			this.label8 = new global::System.Windows.Forms.Label();
			this.checkBox1 = new global::System.Windows.Forms.CheckBox();
			this.colorComboBox1 = new global::ColorComboBox.ColorComboBox();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			this.tabControl1.SuspendLayout();
			this.tabPage1.SuspendLayout();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.button4, "button4");
			this.button4.Name = "button4";
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new global::System.EventHandler(this.button4_Click);
			componentResourceManager.ApplyResources(this.button3, "button3");
			this.button3.Name = "button3";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += new global::System.EventHandler(this.button3_Click);
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
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
			this.table1.Name = "table1";
			this.table1.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table1.SelectionChanged += new global::XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn1,
				this.textColumn2,
				this.textColumn3
			});
			componentResourceManager.ApplyResources(this.textColumn1, "textColumn1");
			this.textColumn1.IsTextTrimmed = false;
			this.textColumn2.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			componentResourceManager.ApplyResources(this.textColumn2, "textColumn2");
			this.textColumn2.IsTextTrimmed = false;
			this.textColumn3.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			componentResourceManager.ApplyResources(this.textColumn3, "textColumn3");
			this.textColumn3.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.TextChanged += new global::System.EventHandler(this.textBox1_TextChanged);
			componentResourceManager.ApplyResources(this.button6, "button6");
			this.button6.Name = "button6";
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new global::System.EventHandler(this.button6_Click);
			componentResourceManager.ApplyResources(this.button5, "button5");
			this.button5.Name = "button5";
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new global::System.EventHandler(this.button5_Click);
			componentResourceManager.ApplyResources(this.tabControl1, "tabControl1");
			this.tabControl1.Controls.Add(this.tabPage1);
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			componentResourceManager.ApplyResources(this.tabPage1, "tabPage1");
			this.tabPage1.BackColor = global::System.Drawing.SystemColors.Control;
			this.tabPage1.Controls.Add(this.textBox2);
			this.tabPage1.Controls.Add(this.label7);
			this.tabPage1.Controls.Add(this.label8);
			this.tabPage1.Controls.Add(this.colorComboBox1);
			this.tabPage1.Controls.Add(this.doubleTextBox2);
			this.tabPage1.Controls.Add(this.doubleTextBox1);
			this.tabPage1.Controls.Add(this.label6);
			this.tabPage1.Controls.Add(this.label4);
			this.tabPage1.Controls.Add(this.label3);
			this.tabPage1.Controls.Add(this.label2);
			this.tabPage1.Controls.Add(this.label1);
			this.tabPage1.Controls.Add(this.textBox1);
            this.tabPage1.Controls.Add(this.checkBox1);
            this.tabPage1.Name = "tabPage1";
			componentResourceManager.ApplyResources(this.textBox2, "textBox2");
			this.textBox2.Name = "textBox2";
			this.textBox2.TextChanged += new global::System.EventHandler(this.textBox2_TextChanged);
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label7.Name = "label8";
			componentResourceManager.ApplyResources(this.colorComboBox1, "colorComboBox1");
			this.colorComboBox1.Extended = true;
			this.colorComboBox1.Name = "colorComboBox1";
			this.colorComboBox1.SelectedColor = global::System.Drawing.Color.Black;
			this.colorComboBox1.ColorChanged += new global::ColorComboBox.ColorChangedHandler(this.colorComboBox1_ColorChanged);
            componentResourceManager.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
			this.doubleTextBox2.Name = "doubleTextBox2";
			this.doubleTextBox2.TextChanged += new global::System.EventHandler(this.doubleTextBox2_TextChanged);
			componentResourceManager.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
			this.doubleTextBox1.Name = "doubleTextBox1";
			this.doubleTextBox1.TextChanged += new global::System.EventHandler(this.doubleTextBox1_TextChanged);
            componentResourceManager.ApplyResources(this.checkBox1, "checkBox1");
            this.checkBox1.Name = "checkBox1";
			this.checkBox1.Checked = true;
			this.checkBox1.CheckStateChanged += new global::System.EventHandler(this.checkBox1_CheckStateChanged);
            componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.Controls.Add(this.tabControl1);
			base.Controls.Add(this.button6);
			base.Controls.Add(this.button5);
			base.Controls.Add(this.button4);
			base.Controls.Add(this.button3);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.table1);
			base.Name = "NuclideDefinitionForm";
			base.Load += new global::System.EventHandler(this.NuclideDefinitionForm_Load);
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			this.tabControl1.ResumeLayout(false);
			this.tabPage1.ResumeLayout(false);
			this.tabPage1.PerformLayout();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x0400003C RID: 60
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400003D RID: 61
		global::System.Windows.Forms.Button button4;

		// Token: 0x0400003E RID: 62
		global::System.Windows.Forms.Button button3;

		// Token: 0x0400003F RID: 63
		global::System.Windows.Forms.Label label5;

		// Token: 0x04000040 RID: 64
		global::XPTable.Models.Table table1;

		// Token: 0x04000041 RID: 65
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x04000042 RID: 66
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x04000043 RID: 67
		global::XPTable.Models.NumberColumn textColumn2;

		// Token: 0x04000044 RID: 68
		global::XPTable.Models.NumberColumn textColumn3;

		// Token: 0x04000045 RID: 69
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x04000046 RID: 70
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x04000047 RID: 71
		global::System.Windows.Forms.Button button6;

		// Token: 0x04000048 RID: 72
		global::System.Windows.Forms.Button button5;

		// Token: 0x04000049 RID: 73
		global::System.Windows.Forms.TabControl tabControl1;

		// Token: 0x0400004A RID: 74
		global::System.Windows.Forms.TabPage tabPage1;

		// Token: 0x0400004B RID: 75
		global::BecquerelMonitor.DoubleTextBox doubleTextBox2;

		// Token: 0x0400004C RID: 76
		global::BecquerelMonitor.DoubleTextBox doubleTextBox1;

		// Token: 0x0400004D RID: 77
		global::System.Windows.Forms.Label label6;

		// Token: 0x0400004E RID: 78
		global::System.Windows.Forms.Label label4;

		global::System.Windows.Forms.CheckBox checkBox1;

		// Token: 0x0400004F RID: 79
		global::System.Windows.Forms.Label label3;

		// Token: 0x04000050 RID: 80
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000051 RID: 81
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000052 RID: 82
		global::System.Windows.Forms.TextBox textBox2;

		// Token: 0x04000053 RID: 83
		global::System.Windows.Forms.Label label7;

		global::System.Windows.Forms.Label label8;

		global::ColorComboBox.ColorComboBox colorComboBox1;
	}
}
