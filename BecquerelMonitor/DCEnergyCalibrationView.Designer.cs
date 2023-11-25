namespace BecquerelMonitor
{
	// Token: 0x020000A4 RID: 164
	public partial class DCEnergyCalibrationView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x06000834 RID: 2100 RVA: 0x0002ED0C File Offset: 0x0002CF0C
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000835 RID: 2101 RVA: 0x0002ED34 File Offset: 0x0002CF34
		void InitializeComponent()
		{
			this.components = new global::System.ComponentModel.Container();
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCEnergyCalibrationView));
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();
			
			this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
			
			this.label4 = new global::System.Windows.Forms.Label();
			this.label6 = new global::System.Windows.Forms.Label();
			this.label7 = new global::System.Windows.Forms.Label();
			this.label37 = new global::System.Windows.Forms.Label();
			this.button1 = new global::System.Windows.Forms.Button();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label1 = new global::System.Windows.Forms.Label();
			this.numericUpDown2 = new global::System.Windows.Forms.TextBox();
			this.numericUpDown1 = new global::System.Windows.Forms.TextBox();
			this.numericUpDown4 = new global::System.Windows.Forms.TextBox();
			this.numericUpDown5 = new global::System.Windows.Forms.TextBox();
			this.numericUpDown6 = new global::System.Windows.Forms.NumericUpDown();
			this.numericUpDown3 = new global::System.Windows.Forms.TextBox();
			this.button4 = new global::System.Windows.Forms.Button();
			this.button5 = new global::System.Windows.Forms.Button();
			this.textBox15 = new global::System.Windows.Forms.TextBox();
			this.button6 = new global::System.Windows.Forms.Button();
			this.checkBox1 = new global::System.Windows.Forms.CheckBox();
            this.checkBox2 = new global::System.Windows.Forms.CheckBox();
            this.printDialog1 = new global::System.Windows.Forms.PrintDialog();
			this.label5 = new global::System.Windows.Forms.Label();
			this.groupBox1 = new global::System.Windows.Forms.GroupBox();
			this.toolTip1 = new global::System.Windows.Forms.ToolTip(this.components);
            this.button7 = new global::System.Windows.Forms.Button();
			this.button11 = new global::System.Windows.Forms.Button();
            this.button14 = new global::System.Windows.Forms.Button();
            this.button15 = new global::System.Windows.Forms.Button();
            this.label36 = new global::System.Windows.Forms.Label();
			this.panel1 = new global::System.Windows.Forms.Panel();
			this.button9 = new global::System.Windows.Forms.Button();
			this.button8 = new global::System.Windows.Forms.Button();
			this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.textColumn1 = new global::XPTable.Models.TextColumn();
			this.numberColumn2 = new global::XPTable.Models.NumberColumn();
            this.numberColumn3 = new global::XPTable.Models.NumberColumn();
            this.numberColumn1 = new global::XPTable.Models.NumberColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.groupBox1.SuspendLayout();
			this.panel1.SuspendLayout();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			base.SuspendLayout();
			resources.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			resources.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			resources.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";

			resources.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.toolTip1.SetToolTip(this.button1, resources.GetString("button1.ToolTip"));
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);

			resources.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			resources.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			resources.ApplyResources(this.numericUpDown3, "numericUpDown3");
			global::System.Windows.Forms.TextBox numericUpDown3 = this.numericUpDown3;
			int[] array = new int[4];
			array[0] = 10000;
			this.numericUpDown3.Name = "numericUpDown3";
			this.numericUpDown3.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown3_KeyDown);
            this.numericUpDown3.Leave += new global::System.EventHandler(this.numericUpDown3_Leave);
            resources.ApplyResources(this.numericUpDown2, "numericUpDown2");
			global::System.Windows.Forms.TextBox numericUpDown2 = this.numericUpDown2;
			this.numericUpDown2.Name = "numericUpDown2";
			this.numericUpDown2.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown2_KeyDown);
            this.numericUpDown2.Leave += new global::System.EventHandler(this.numericUpDown2_Leave);
            resources.ApplyResources(this.numericUpDown1, "numericUpDown1");
			global::System.Windows.Forms.TextBox numericUpDown1 = this.numericUpDown1;
			this.numericUpDown1.Name = "numericUpDown1";
			this.numericUpDown1.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown1_KeyDown);
			this.numericUpDown1.Leave += new global::System.EventHandler(this.numericUpDown1_Leave);
			resources.ApplyResources(this.numericUpDown4, "numericUpDown4");
			global::System.Windows.Forms.TextBox numericUpDown4 = this.numericUpDown4;
			this.numericUpDown4.Name = "numericUpDown4";
			this.numericUpDown4.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown4_KeyDown);
            this.numericUpDown4.Leave += new global::System.EventHandler(this.numericUpDown4_Leave);
            resources.ApplyResources(this.numericUpDown5, "numericUpDown5");
			global::System.Windows.Forms.TextBox numericUpDown5 = this.numericUpDown5;
			this.numericUpDown5.Name = "numericUpDown5";
			this.numericUpDown5.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown5_KeyDown);
            this.numericUpDown5.Leave += new global::System.EventHandler(this.numericUpDown5_Leave);


            resources.ApplyResources(this.button4, "button4");
			this.button4.Name = "button4";
			this.toolTip1.SetToolTip(this.button4, resources.GetString("button4.ToolTip"));
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new global::System.EventHandler(this.button4_Click);
			resources.ApplyResources(this.button5, "button5");
			this.button5.Name = "button5";
			this.toolTip1.SetToolTip(this.button5, resources.GetString("button5.ToolTip"));
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new global::System.EventHandler(this.button5_Click);
			resources.ApplyResources(this.textBox15, "textBox15");
			this.textBox15.BackColor = global::System.Drawing.SystemColors.ControlLight;
			this.textBox15.BorderStyle = global::System.Windows.Forms.BorderStyle.None;
			this.textBox15.Name = "textBox15";
			this.textBox15.ReadOnly = true;
			this.textBox15.TabStop = false;
			resources.ApplyResources(this.button6, "button6");
			this.button6.Name = "button6";
			this.toolTip1.SetToolTip(this.button6, resources.GetString("button6.ToolTip"));
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new global::System.EventHandler(this.button6_Click);
			resources.ApplyResources(this.checkBox1, "checkBox1");
			this.checkBox1.Name = "checkBox1";
			this.toolTip1.SetToolTip(this.checkBox1, resources.GetString("checkBox1.ToolTip"));
			this.checkBox1.UseVisualStyleBackColor = true;
			this.checkBox1.CheckedChanged += new global::System.EventHandler(this.checkBox1_CheckedChanged);
			this.printDialog1.UseEXDialog = true;
			resources.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			resources.ApplyResources(this.groupBox1, "groupBox1");
			this.groupBox1.Controls.Add(this.label6);
			this.groupBox1.Controls.Add(this.label7);
			this.groupBox1.Controls.Add(this.label4);
			this.groupBox1.Controls.Add(this.label2);
			this.groupBox1.Controls.Add(this.label1);
			this.groupBox1.Controls.Add(this.button1);
			this.groupBox1.Controls.Add(this.numericUpDown1);
			this.groupBox1.Controls.Add(this.numericUpDown2);
			this.groupBox1.Controls.Add(this.numericUpDown3);
			this.groupBox1.Controls.Add(this.numericUpDown4);
			this.groupBox1.Controls.Add(this.numericUpDown5);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.TabStop = false;
			resources.ApplyResources(this.button7, "button7");
			this.button7.Name = "button7";
			this.button7.UseVisualStyleBackColor = true;
			this.button7.Click += new global::System.EventHandler(this.button7_Click);
			resources.ApplyResources(this.button11, "button11");
			this.button11.Name = "button11";
			this.button11.UseVisualStyleBackColor = true;
			this.button11.Click += new global::System.EventHandler(this.button11_Click);
			resources.ApplyResources(this.label36, "label36");
			this.label36.Name = "label36";
			resources.ApplyResources(this.panel1, "panel1");
			this.panel1.BackColor = global::System.Drawing.SystemColors.ControlLight;
			this.panel1.Controls.Add(this.button9);
			this.panel1.Controls.Add(this.button8);
			this.panel1.Controls.Add(this.table1);
			this.panel1.Controls.Add(this.button7);
			this.panel1.Controls.Add(this.button11);
			this.panel1.Controls.Add(this.label36);
			this.panel1.Controls.Add(this.numericUpDown6);
			this.panel1.Controls.Add(this.label37);
            this.panel1.Controls.Add(this.checkBox2);
            this.panel1.Controls.Add(this.button14);
            this.panel1.Controls.Add(this.button15);
            this.panel1.Name = "panel1";
            resources.ApplyResources(this.checkBox2, "checkBox2");
            this.checkBox2.Name = "checkBox2";
            this.toolTip1.SetToolTip(this.checkBox2, resources.GetString("checkBox2.ToolTip"));
            this.checkBox2.UseVisualStyleBackColor = true;
            this.checkBox2.Checked = false;
            this.checkBox2.Click += new global::System.EventHandler(this.checkBox2_Click);
            resources.ApplyResources(this.button14, "button14");
            this.button14.Name = "button14";
            this.toolTip1.SetToolTip(this.button14, resources.GetString("button14.ToolTip"));
            this.button14.UseVisualStyleBackColor = true;
            this.button14.Click += new global::System.EventHandler(this.button14_Click);
            resources.ApplyResources(this.button15, "button15");
            this.button15.Name = "button15";
            this.toolTip1.SetToolTip(this.button15, resources.GetString("button15.ToolTip"));
            this.button15.UseVisualStyleBackColor = true;
            this.button15.Click += new global::System.EventHandler(this.button15_Click);
            resources.ApplyResources(this.numericUpDown6, "numericUpDown6");
			this.numericUpDown6.Name = "numericUpDown6";
			this.numericUpDown6.Increment = 1;
			this.numericUpDown6.Minimum = 0;
			this.numericUpDown6.Maximum = 4;
			this.numericUpDown6.Value = 0;
			resources.ApplyResources(this.label37, "label37");
			this.label37.Name = "label37";
			resources.ApplyResources(this.button9, "button9");
			this.button9.Name = "button9";
			this.button9.UseVisualStyleBackColor = true;
			this.button9.Click += new global::System.EventHandler(this.button9_Click);
			resources.ApplyResources(this.button8, "button8");
			this.button8.Name = "button8";
			this.button8.UseVisualStyleBackColor = true;
			this.button8.Click += new global::System.EventHandler(this.button8_Click);
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
			this.table1.HeaderFont = new global::System.Drawing.Font("Microsoft Sans Serif", 9f);
			this.table1.Name = "table1";
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table1.EditingStopped += new global::XPTable.Events.CellEditEventHandler(this.table1_EditingStopped);
			this.table1.SelectionChanged += new global::XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn1,
				this.numberColumn2,
				this.numberColumn1,
				this.numberColumn3

			});
			this.textColumn1.Editable = false;
			this.textColumn1.IsTextTrimmed = false;
			this.textColumn1.Sortable = false;
			resources.ApplyResources(this.textColumn1, "textColumn1");
			this.numberColumn2.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.numberColumn2.IsTextTrimmed = false;
			int[] array4 = new int[4];
			array4[0] = 65535;
            this.numberColumn2.Maximum = new decimal(array4);
			this.numberColumn2.Sortable = false;
			resources.ApplyResources(this.numberColumn2, "numberColumn2");
			this.numberColumn1.Alignment = global::XPTable.Models.ColumnAlignment.Right;
			this.numberColumn1.IsTextTrimmed = false;
			int[] array5 = new int[4];
			array5[0] = 10000;
            this.numberColumn1.Maximum = new decimal(array5);
			this.numberColumn1.Minimum = new decimal(new int[]
			{
				10000,
				0,
				0,
				int.MinValue
			});
			this.numberColumn1.Sortable = false;
			resources.ApplyResources(this.numberColumn1, "numberColumn1");
            resources.ApplyResources(this.numberColumn3, "numberColumn3");
            this.numberColumn3.Alignment = global::XPTable.Models.ColumnAlignment.Right;
            this.numberColumn3.IsTextTrimmed = false;
            this.numberColumn3.Maximum = 10000000;
			this.numberColumn3.Minimum = 0;
            this.numberColumn3.Sortable = false;
            resources.ApplyResources(this, "$this");
			base.Controls.Add(this.panel1);
			base.Controls.Add(this.button6);
			base.Controls.Add(this.groupBox1);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.checkBox1);
			base.Controls.Add(this.textBox15);
			base.Controls.Add(this.button5);
			base.Controls.Add(this.button4);
			base.HideOnClose = true;
			base.Name = "DCEnergyCalibrationView";
			base.FormClosing += new global::System.Windows.Forms.FormClosingEventHandler(this.DCEnergyCalibrationView_FormClosing);
			base.Load += new global::System.EventHandler(this.DCEnergyCalibrationView_Load);
			base.SizeChanged += new global::System.EventHandler(this.DCEnergyCalibrationView_SizeChanged);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x0400041F RID: 1055
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000420 RID: 1056
		global::System.Windows.Forms.Label label4;

		// Token: 0x04000422 RID: 1058
		global::System.Windows.Forms.TextBox numericUpDown3;

		// Token: 0x04000424 RID: 1060
		global::System.Windows.Forms.Button button1;

		// Token: 0x04000425 RID: 1061
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000426 RID: 1062
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000427 RID: 1063
		global::System.Windows.Forms.TextBox numericUpDown2;

		// Token: 0x04000428 RID: 1064
		global::System.Windows.Forms.TextBox numericUpDown1;

		global::System.Windows.Forms.TextBox numericUpDown4;

		global::System.Windows.Forms.TextBox numericUpDown5;

		global::System.Windows.Forms.NumericUpDown numericUpDown6;

		// Token: 0x04000429 RID: 1065
		global::System.Windows.Forms.Button button4;

		// Token: 0x0400042A RID: 1066
		global::System.Windows.Forms.Button button5;

		// Token: 0x0400042B RID: 1067
		global::System.Windows.Forms.TextBox textBox15;

		// Token: 0x0400042C RID: 1068
		global::System.Windows.Forms.Button button6;

		// Token: 0x0400042D RID: 1069
		global::System.Windows.Forms.CheckBox checkBox1;

        global::System.Windows.Forms.CheckBox checkBox2;

        // Token: 0x0400042E RID: 1070
        global::System.Windows.Forms.PrintDialog printDialog1;

		// Token: 0x0400042F RID: 1071
		global::System.Windows.Forms.Label label5;

		// Token: 0x04000430 RID: 1072
		global::System.Windows.Forms.GroupBox groupBox1;

		// Token: 0x04000431 RID: 1073
		global::System.Windows.Forms.ToolTip toolTip1;

		// Token: 0x04000432 RID: 1074
		global::System.Windows.Forms.Button button7;

		// Token: 0x04000433 RID: 1075
		global::System.Windows.Forms.Button button11;

        global::System.Windows.Forms.Button button14;

        global::System.Windows.Forms.Button button15;

        // Token: 0x04000434 RID: 1076
        global::System.Windows.Forms.Label label36;

		global::System.Windows.Forms.Label label6;

		global::System.Windows.Forms.Label label7;

		global::System.Windows.Forms.Label label37;

		// Token: 0x04000435 RID: 1077
		global::System.Windows.Forms.Panel panel1;

		// Token: 0x04000436 RID: 1078
		global::System.Windows.Forms.Button button9;

		// Token: 0x04000437 RID: 1079
		global::System.Windows.Forms.Button button8;

		// Token: 0x04000438 RID: 1080
		global::XPTable.Models.Table table1;

		// Token: 0x04000439 RID: 1081
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x0400043A RID: 1082
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x0400043B RID: 1083
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x0400043C RID: 1084
		global::XPTable.Models.NumberColumn numberColumn1;

		// Token: 0x0400043D RID: 1085
		global::XPTable.Models.NumberColumn numberColumn2;

        global::XPTable.Models.NumberColumn numberColumn3;
    }
}
