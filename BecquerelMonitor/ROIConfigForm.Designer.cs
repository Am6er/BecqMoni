namespace BecquerelMonitor
{
	// Token: 0x020000BC RID: 188
	public partial class ROIConfigForm : global::System.Windows.Forms.Form
	{
		// Token: 0x060008ED RID: 2285 RVA: 0x00031D74 File Offset: 0x0002FF74
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060008EE RID: 2286 RVA: 0x00031D9C File Offset: 0x0002FF9C
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.ROIConfigForm));
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder2 = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer2 = new global::XPTable.Renderers.DragDropRenderer();
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder3 = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer3 = new global::XPTable.Renderers.DragDropRenderer();
			this.button1 = new global::System.Windows.Forms.Button();
			this.button2 = new global::System.Windows.Forms.Button();
			this.button3 = new global::System.Windows.Forms.Button();
			this.button4 = new global::System.Windows.Forms.Button();
			this.button5 = new global::System.Windows.Forms.Button();
			this.button6 = new global::System.Windows.Forms.Button();
			this.tabControl1 = new global::System.Windows.Forms.TabControl();
			this.label1 = new global::System.Windows.Forms.Label();
			this.textBox1 = new global::System.Windows.Forms.TextBox();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label5 = new global::System.Windows.Forms.Label();
			this.label6 = new global::System.Windows.Forms.Label();
			this.comboBox1 = new global::System.Windows.Forms.ComboBox();
			this.groupBox1 = new global::System.Windows.Forms.GroupBox();
			this.groupBox3 = new global::System.Windows.Forms.GroupBox();
			this.label25 = new global::System.Windows.Forms.Label();
			this.label13 = new global::System.Windows.Forms.Label();
			this.label14 = new global::System.Windows.Forms.Label();
			this.label20 = new global::System.Windows.Forms.Label();
			this.label19 = new global::System.Windows.Forms.Label();
            this.label26 = new global::System.Windows.Forms.Label();
            this.checkBox1 = new global::System.Windows.Forms.CheckBox();
			this.label12 = new global::System.Windows.Forms.Label();
			this.label10 = new global::System.Windows.Forms.Label();
			this.label9 = new global::System.Windows.Forms.Label();
			this.textBox2 = new global::System.Windows.Forms.TextBox();
			this.label8 = new global::System.Windows.Forms.Label();
			this.label7 = new global::System.Windows.Forms.Label();
			this.label15 = new global::System.Windows.Forms.Label();
			this.button7 = new global::System.Windows.Forms.Button();
			this.button8 = new global::System.Windows.Forms.Button();
			this.groupBox2 = new global::System.Windows.Forms.GroupBox();
			this.label18 = new global::System.Windows.Forms.Label();
			this.textBox5 = new global::System.Windows.Forms.TextBox();
			this.textBox4 = new global::System.Windows.Forms.TextBox();
			this.label17 = new global::System.Windows.Forms.Label();
			this.textBox3 = new global::System.Windows.Forms.TextBox();
			this.label16 = new global::System.Windows.Forms.Label();
			this.panel1 = new global::System.Windows.Forms.Panel();
			this.label24 = new global::System.Windows.Forms.Label();
			this.numericUpDown1 = new global::System.Windows.Forms.NumericUpDown();
			this.label23 = new global::System.Windows.Forms.Label();
			this.label22 = new global::System.Windows.Forms.Label();
			this.button9 = new global::System.Windows.Forms.Button();
			this.comboBox2 = new global::System.Windows.Forms.ComboBox();
			this.label21 = new global::System.Windows.Forms.Label();
			this.label11 = new global::System.Windows.Forms.Label();
			this.button10 = new global::System.Windows.Forms.Button();
			this.table2 = new global::XPTable.Models.Table();
			this.columnModel2 = new global::XPTable.Models.ColumnModel();
			this.textColumn13 = new global::XPTable.Models.TextColumn();
			this.textColumn8 = new global::XPTable.Models.TextColumn();
			this.imageColumn1 = new global::XPTable.Models.ImageColumn();
			this.textColumn10 = new global::XPTable.Models.TextColumn();
			this.textColumn11 = new global::XPTable.Models.TextColumn();
			this.tableModel2 = new global::XPTable.Models.TableModel();
			this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.checkBoxColumn2 = new global::XPTable.Models.CheckBoxColumn();
			this.textColumn2 = new global::XPTable.Models.TextColumn();
			this.textColumn3 = new global::XPTable.Models.TextColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.colorComboBox1 = new global::ColorComboBox.ColorComboBox();
			this.doubleTextBox5 = new global::BecquerelMonitor.DoubleTextBox();
			this.doubleTextBox2 = new global::BecquerelMonitor.DoubleTextBox();
			this.doubleTextBox1 = new global::BecquerelMonitor.DoubleTextBox();
			this.doubleTextBox6 = new global::BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox7 = new global::BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox4 = new global::BecquerelMonitor.DoubleTextBox();
			this.doubleTextBox3 = new global::BecquerelMonitor.DoubleTextBox();
			this.textColumn4 = new global::XPTable.Models.TextColumn();
			this.textColumn5 = new global::XPTable.Models.TextColumn();
			this.textColumn6 = new global::XPTable.Models.TextColumn();
			this.textColumn7 = new global::XPTable.Models.TextColumn();
			this.table3 = new global::XPTable.Models.Table();
			this.columnModel3 = new global::XPTable.Models.ColumnModel();
			this.textColumn1 = new global::XPTable.Models.TextColumn();
			this.textColumn12 = new global::XPTable.Models.TextColumn();
			this.tableModel3 = new global::XPTable.Models.TableModel();
			this.groupBox1.SuspendLayout();
			this.groupBox3.SuspendLayout();
			this.groupBox2.SuspendLayout();
			this.panel1.SuspendLayout();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown1).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.table2).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.table3).BeginInit();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			componentResourceManager.ApplyResources(this.button2, "button2");
			this.button2.Name = "button2";
			this.button2.UseVisualStyleBackColor = true;
			this.button2.Click += new global::System.EventHandler(this.button2_Click);
			componentResourceManager.ApplyResources(this.button3, "button3");
			this.button3.Name = "button3";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += new global::System.EventHandler(this.button3_Click);
			componentResourceManager.ApplyResources(this.button4, "button4");
			this.button4.Name = "button4";
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new global::System.EventHandler(this.button4_Click);
			componentResourceManager.ApplyResources(this.button5, "button5");
			this.button5.Name = "button5";
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new global::System.EventHandler(this.button5_Click);
			componentResourceManager.ApplyResources(this.button6, "button6");
			this.button6.Name = "button6";
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new global::System.EventHandler(this.button6_Click);
			componentResourceManager.ApplyResources(this.tabControl1, "tabControl1");
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.TextChanged += new global::System.EventHandler(this.textBox1_TextChanged);
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this.comboBox1, "comboBox1");
			this.comboBox1.DropDownStyle = global::System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBox1.FormattingEnabled = true;
			this.comboBox1.Items.AddRange(new object[]
			{
				componentResourceManager.GetString("comboBox1.Items")
			});
			this.comboBox1.Name = "comboBox1";
			componentResourceManager.ApplyResources(this.groupBox1, "groupBox1");
			this.groupBox1.Controls.Add(this.groupBox3);
			this.groupBox1.Controls.Add(this.label20);
			this.groupBox1.Controls.Add(this.label19);
            this.groupBox1.Controls.Add(this.label26);
            this.groupBox1.Controls.Add(this.doubleTextBox6);
            this.groupBox1.Controls.Add(this.doubleTextBox7);
            this.groupBox1.Controls.Add(this.doubleTextBox4);
			this.groupBox1.Controls.Add(this.doubleTextBox3);
			this.groupBox1.Controls.Add(this.checkBox1);
			this.groupBox1.Controls.Add(this.label12);
			this.groupBox1.Controls.Add(this.label10);
			this.groupBox1.Controls.Add(this.label9);
			this.groupBox1.Controls.Add(this.textBox2);
			this.groupBox1.Controls.Add(this.label8);
			this.groupBox1.Controls.Add(this.textBox1);
			this.groupBox1.Controls.Add(this.label2);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.TabStop = false;
			componentResourceManager.ApplyResources(this.groupBox3, "groupBox3");
			this.groupBox3.Controls.Add(this.colorComboBox1);
			this.groupBox3.Controls.Add(this.label25);
			this.groupBox3.Controls.Add(this.label13);
			this.groupBox3.Controls.Add(this.label5);
			this.groupBox3.Controls.Add(this.label4);
			this.groupBox3.Controls.Add(this.label3);
			this.groupBox3.Controls.Add(this.label14);
			this.groupBox3.Controls.Add(this.label6);
			this.groupBox3.Controls.Add(this.doubleTextBox5);
			this.groupBox3.Controls.Add(this.doubleTextBox2);
			this.groupBox3.Controls.Add(this.doubleTextBox1);
			this.groupBox3.Name = "groupBox3";
			this.groupBox3.TabStop = false;
			componentResourceManager.ApplyResources(this.label25, "label25");
			this.label25.Name = "label25";
			componentResourceManager.ApplyResources(this.label13, "label13");
			this.label13.Name = "label13";
			componentResourceManager.ApplyResources(this.label14, "label14");
			this.label14.Name = "label14";
			componentResourceManager.ApplyResources(this.label20, "label20");
			this.label20.Name = "label20";
			componentResourceManager.ApplyResources(this.label19, "label19");
			this.label19.Name = "label19";
            componentResourceManager.ApplyResources(this.label26, "label26");
            this.label26.Name = "label26";
            componentResourceManager.ApplyResources(this.checkBox1, "checkBox1");
			this.checkBox1.Name = "checkBox1";
			this.checkBox1.UseVisualStyleBackColor = true;
			this.checkBox1.CheckedChanged += new global::System.EventHandler(this.checkBox1_CheckedChanged);
			componentResourceManager.ApplyResources(this.label12, "label12");
			this.label12.Name = "label12";
			componentResourceManager.ApplyResources(this.label10, "label10");
			this.label10.Name = "label10";
			componentResourceManager.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			componentResourceManager.ApplyResources(this.textBox2, "textBox2");
			this.textBox2.Name = "textBox2";
			this.textBox2.TextChanged += new global::System.EventHandler(this.textBox2_TextChanged);
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.label15, "label15");
			this.label15.Name = "label15";
			componentResourceManager.ApplyResources(this.button7, "button7");
			this.button7.Name = "button7";
			this.button7.UseVisualStyleBackColor = true;
			this.button7.Click += new global::System.EventHandler(this.button7_Click);
			componentResourceManager.ApplyResources(this.button8, "button8");
			this.button8.Name = "button8";
			this.button8.UseVisualStyleBackColor = true;
			this.button8.Click += new global::System.EventHandler(this.button8_Click);
			componentResourceManager.ApplyResources(this.groupBox2, "groupBox2");
			this.groupBox2.Controls.Add(this.label18);
			this.groupBox2.Controls.Add(this.textBox5);
			this.groupBox2.Controls.Add(this.textBox4);
			this.groupBox2.Controls.Add(this.label17);
			this.groupBox2.Controls.Add(this.textBox3);
			this.groupBox2.Controls.Add(this.label16);
			this.groupBox2.Name = "groupBox2";
			this.groupBox2.TabStop = false;
			componentResourceManager.ApplyResources(this.label18, "label18");
			this.label18.Name = "label18";
			componentResourceManager.ApplyResources(this.textBox5, "textBox5");
			this.textBox5.Name = "textBox5";
			this.textBox5.TextChanged += new global::System.EventHandler(this.textBox5_TextChanged);
			componentResourceManager.ApplyResources(this.textBox4, "textBox4");
			this.textBox4.Name = "textBox4";
			this.textBox4.ReadOnly = true;
			componentResourceManager.ApplyResources(this.label17, "label17");
			this.label17.Name = "label17";
			componentResourceManager.ApplyResources(this.textBox3, "textBox3");
			this.textBox3.Name = "textBox3";
			this.textBox3.TextChanged += new global::System.EventHandler(this.textBox3_TextChanged);
			componentResourceManager.ApplyResources(this.label16, "label16");
			this.label16.Name = "label16";
			componentResourceManager.ApplyResources(this.panel1, "panel1");
			this.panel1.BackColor = global::System.Drawing.Color.LightGray;
			this.panel1.BorderStyle = global::System.Windows.Forms.BorderStyle.FixedSingle;
			this.panel1.Controls.Add(this.label24);
			this.panel1.Controls.Add(this.numericUpDown1);
			this.panel1.Controls.Add(this.label23);
			this.panel1.Controls.Add(this.label22);
			this.panel1.Controls.Add(this.button9);
			this.panel1.Controls.Add(this.comboBox2);
			this.panel1.Controls.Add(this.label21);
			this.panel1.Controls.Add(this.label11);
			this.panel1.Controls.Add(this.label1);
			this.panel1.Controls.Add(this.table2);
			this.panel1.Controls.Add(this.table1);
			this.panel1.Controls.Add(this.button1);
			this.panel1.Controls.Add(this.button2);
			this.panel1.Controls.Add(this.groupBox1);
			this.panel1.Controls.Add(this.tabControl1);
			this.panel1.Controls.Add(this.button3);
			this.panel1.Controls.Add(this.label7);
			this.panel1.Controls.Add(this.button4);
			this.panel1.Controls.Add(this.comboBox1);
			this.panel1.Name = "panel1";
			componentResourceManager.ApplyResources(this.label24, "label24");
			this.label24.Name = "label24";
			componentResourceManager.ApplyResources(this.numericUpDown1, "numericUpDown1");
			this.numericUpDown1.DecimalPlaces = 1;
			this.numericUpDown1.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				65536
			});
			this.numericUpDown1.Name = "numericUpDown1";
			global::System.Windows.Forms.NumericUpDown numericUpDown = this.numericUpDown1;
			int[] array = new int[4];
			array[0] = 5;
			numericUpDown.Value = new decimal(array);
			componentResourceManager.ApplyResources(this.label23, "label23");
			this.label23.Name = "label23";
			componentResourceManager.ApplyResources(this.label22, "label22");
			this.label22.Name = "label22";
			componentResourceManager.ApplyResources(this.button9, "button9");
			this.button9.Name = "button9";
			this.button9.UseVisualStyleBackColor = true;
			this.button9.Click += new global::System.EventHandler(this.button9_Click);
			componentResourceManager.ApplyResources(this.comboBox2, "comboBox2");
			this.comboBox2.DropDownStyle = global::System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBox2.FormattingEnabled = true;
			this.comboBox2.Name = "comboBox2";
			this.comboBox2.SelectedIndexChanged += new global::System.EventHandler(this.comboBox2_SelectedIndexChanged);
			componentResourceManager.ApplyResources(this.label21, "label21");
			this.label21.Name = "label21";
			componentResourceManager.ApplyResources(this.label11, "label11");
			this.label11.Name = "label11";
			componentResourceManager.ApplyResources(this.button10, "button10");
			this.button10.Name = "button10";
			this.button10.UseVisualStyleBackColor = true;
			this.button10.Click += new global::System.EventHandler(this.button10_Click);
			componentResourceManager.ApplyResources(this.table2, "table2");
			this.table2.BorderColor = global::System.Drawing.Color.Black;
			this.table2.ColumnModel = this.columnModel2;
			this.table2.DataMember = null;
			this.table2.DataSourceColumnBinder = dataSourceColumnBinder;
			dragDropRenderer.ForeColor = global::System.Drawing.Color.Red;
			this.table2.DragDropRenderer = dragDropRenderer;
			this.table2.FullRowSelect = true;
			this.table2.GridLines = global::XPTable.Models.GridLines.Both;
			this.table2.GridLinesContrainedToData = false;
			this.table2.MinimumSize = new global::System.Drawing.Size(0, 50);
			this.table2.Name = "table2";
			this.table2.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table2.TableModel = this.tableModel2;
			this.table2.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table2.SelectionChanged += new global::XPTable.Events.SelectionEventHandler(this.table2_SelectionChanged);
			this.columnModel2.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn13,
				this.textColumn8,
				this.imageColumn1,
				this.textColumn10,
				this.textColumn11
			});
			componentResourceManager.ApplyResources(this.textColumn13, "textColumn13");
			this.textColumn13.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn8, "textColumn8");
			this.textColumn8.Editable = false;
			this.textColumn8.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.imageColumn1, "imageColumn1");
			this.imageColumn1.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn10, "textColumn10");
			this.textColumn10.Editable = false;
			this.textColumn10.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn11, "textColumn11");
			this.textColumn11.Editable = false;
			this.textColumn11.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.table1, "table1");
			this.table1.BorderColor = global::System.Drawing.Color.Black;
			this.table1.ColumnModel = this.columnModel1;
			this.table1.DataMember = null;
			this.table1.DataSourceColumnBinder = dataSourceColumnBinder2;
			dragDropRenderer2.ForeColor = global::System.Drawing.Color.Red;
			this.table1.DragDropRenderer = dragDropRenderer2;
			this.table1.FullRowSelect = true;
			this.table1.GridLines = global::XPTable.Models.GridLines.Both;
			this.table1.GridLinesContrainedToData = false;
			this.table1.Name = "table1";
			this.table1.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table1.TableModel = this.tableModel1;
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table1.CellCheckChanged += new global::XPTable.Events.CellCheckBoxEventHandler(this.table1_CellCheckChanged);
			this.table1.SelectionChanged += new global::XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.checkBoxColumn2,
				this.textColumn2,
				this.textColumn3
			});
			componentResourceManager.ApplyResources(this.checkBoxColumn2, "checkBoxColumn2");
			this.checkBoxColumn2.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn2, "textColumn2");
			this.textColumn2.Editable = false;
			this.textColumn2.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn3, "textColumn3");
			this.textColumn3.Editable = false;
			this.textColumn3.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.colorComboBox1, "colorComboBox1");
			this.colorComboBox1.Extended = true;
			this.colorComboBox1.Name = "colorComboBox1";
			this.colorComboBox1.SelectedColor = global::System.Drawing.Color.Black;
			this.colorComboBox1.ColorChanged += new global::ColorComboBox.ColorChangedHandler(this.colorComboBox1_ColorChanged);
			componentResourceManager.ApplyResources(this.doubleTextBox5, "doubleTextBox5");
			this.doubleTextBox5.Name = "doubleTextBox5";
			this.doubleTextBox5.TextChanged += new global::System.EventHandler(this.doubleTextBox5_TextChanged);
			componentResourceManager.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
			this.doubleTextBox2.Name = "doubleTextBox2";
			this.doubleTextBox2.TextChanged += new global::System.EventHandler(this.doubleTextBox2_TextChanged);
			componentResourceManager.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
			this.doubleTextBox1.Name = "doubleTextBox1";
			this.doubleTextBox1.TextChanged += new global::System.EventHandler(this.doubleTextBox1_TextChanged);
			componentResourceManager.ApplyResources(this.doubleTextBox6, "doubleTextBox6");
			this.doubleTextBox6.Name = "doubleTextBox6";
			this.doubleTextBox6.TextChanged += new global::System.EventHandler(this.doubleTextBox6_TextChanged);
            componentResourceManager.ApplyResources(this.doubleTextBox7, "doubleTextBox7");
            this.doubleTextBox7.Name = "doubleTextBox7";
            this.doubleTextBox7.TextChanged += new global::System.EventHandler(this.doubleTextBox7_TextChanged);
            componentResourceManager.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
			this.doubleTextBox4.Name = "doubleTextBox4";
			this.doubleTextBox4.TextChanged += new global::System.EventHandler(this.doubleTextBox4_TextChanged);
			componentResourceManager.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
			this.doubleTextBox3.Name = "doubleTextBox3";
			this.doubleTextBox3.TextChanged += new global::System.EventHandler(this.doubleTextBox3_TextChanged);
			componentResourceManager.ApplyResources(this.textColumn4, "textColumn4");
			this.textColumn4.Enabled = false;
			this.textColumn4.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn5, "textColumn5");
			this.textColumn5.Enabled = false;
			this.textColumn5.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn6, "textColumn6");
			this.textColumn6.Enabled = false;
			this.textColumn6.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn7, "textColumn7");
			this.textColumn7.Enabled = false;
			this.textColumn7.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.table3, "table3");
			this.table3.BorderColor = global::System.Drawing.Color.Black;
			this.table3.ColumnModel = this.columnModel3;
			this.table3.DataMember = null;
			this.table3.DataSourceColumnBinder = dataSourceColumnBinder3;
			dragDropRenderer3.ForeColor = global::System.Drawing.Color.Red;
			this.table3.DragDropRenderer = dragDropRenderer3;
			this.table3.FullRowSelect = true;
			this.table3.GridLines = global::XPTable.Models.GridLines.Both;
			this.table3.GridLinesContrainedToData = false;
			this.table3.Name = "table3";
			this.table3.SortedColumnBackColor = global::System.Drawing.Color.White;
			this.table3.TableModel = this.tableModel3;
			this.table3.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.table3.SelectionChanged += new global::XPTable.Events.SelectionEventHandler(this.table3_SelectionChanged);
			this.columnModel3.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn1,
				this.textColumn12
			});
			componentResourceManager.ApplyResources(this.textColumn1, "textColumn1");
			this.textColumn1.Editable = false;
			this.textColumn1.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this.textColumn12, "textColumn12");
			this.textColumn12.Editable = false;
			this.textColumn12.IsTextTrimmed = false;
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.Controls.Add(this.button10);
			base.Controls.Add(this.panel1);
			base.Controls.Add(this.groupBox2);
			base.Controls.Add(this.button8);
			base.Controls.Add(this.button7);
			base.Controls.Add(this.label15);
			base.Controls.Add(this.table3);
			base.Controls.Add(this.button6);
			base.Controls.Add(this.button5);
			base.Name = "ROIConfigForm";
			base.FormClosing += new global::System.Windows.Forms.FormClosingEventHandler(this.ROIConfigForm_FormClosing);
			base.Load += new global::System.EventHandler(this.ROIConfigForm_Load);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.groupBox3.ResumeLayout(false);
			this.groupBox3.PerformLayout();
			this.groupBox2.ResumeLayout(false);
			this.groupBox2.PerformLayout();
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown1).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.table2).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.table3).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x040004B7 RID: 1207
		global::System.ComponentModel.IContainer components;

		// Token: 0x040004B8 RID: 1208
		global::XPTable.Models.Table table1;

		// Token: 0x040004B9 RID: 1209
		global::System.Windows.Forms.Button button1;

		// Token: 0x040004BA RID: 1210
		global::System.Windows.Forms.Button button2;

		// Token: 0x040004BB RID: 1211
		global::System.Windows.Forms.Button button3;

		// Token: 0x040004BC RID: 1212
		global::System.Windows.Forms.Button button4;

		// Token: 0x040004BD RID: 1213
		global::System.Windows.Forms.Button button5;

		// Token: 0x040004BE RID: 1214
		global::System.Windows.Forms.Button button6;

		// Token: 0x040004BF RID: 1215
		global::System.Windows.Forms.TabControl tabControl1;

		// Token: 0x040004C0 RID: 1216
		global::System.Windows.Forms.Label label1;

		// Token: 0x040004C1 RID: 1217
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x040004C2 RID: 1218
		global::XPTable.Models.TextColumn textColumn2;

		// Token: 0x040004C3 RID: 1219
		global::XPTable.Models.TextColumn textColumn3;

		// Token: 0x040004C4 RID: 1220
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x040004C5 RID: 1221
		global::XPTable.Models.Table table2;

		// Token: 0x040004C6 RID: 1222
		global::XPTable.Models.TableModel tableModel2;

		// Token: 0x040004C7 RID: 1223
		global::XPTable.Models.TextColumn textColumn4;

		// Token: 0x040004C8 RID: 1224
		global::XPTable.Models.TextColumn textColumn5;

		// Token: 0x040004C9 RID: 1225
		global::XPTable.Models.TextColumn textColumn6;

		// Token: 0x040004CA RID: 1226
		global::XPTable.Models.TextColumn textColumn7;

		// Token: 0x040004CB RID: 1227
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x040004CC RID: 1228
		global::System.Windows.Forms.Label label2;

		// Token: 0x040004CD RID: 1229
		global::BecquerelMonitor.DoubleTextBox doubleTextBox1;

		// Token: 0x040004CE RID: 1230
		global::BecquerelMonitor.DoubleTextBox doubleTextBox2;

		// Token: 0x040004CF RID: 1231
		global::System.Windows.Forms.Label label3;

		// Token: 0x040004D0 RID: 1232
		global::System.Windows.Forms.Label label4;

		// Token: 0x040004D1 RID: 1233
		global::System.Windows.Forms.Label label5;

		// Token: 0x040004D2 RID: 1234
		global::System.Windows.Forms.Label label6;

		// Token: 0x040004D3 RID: 1235
		global::System.Windows.Forms.ComboBox comboBox1;

		// Token: 0x040004D4 RID: 1236
		global::System.Windows.Forms.GroupBox groupBox1;

		// Token: 0x040004D5 RID: 1237
		global::System.Windows.Forms.Label label7;

		// Token: 0x040004D6 RID: 1238
		global::System.Windows.Forms.TextBox textBox2;

		// Token: 0x040004D7 RID: 1239
		global::System.Windows.Forms.Label label8;

		// Token: 0x040004D8 RID: 1240
		global::System.Windows.Forms.Label label9;

		// Token: 0x040004D9 RID: 1241
		global::System.Windows.Forms.Label label10;

		// Token: 0x040004DA RID: 1242
		global::System.Windows.Forms.Label label12;

		// Token: 0x040004DB RID: 1243
		global::XPTable.Models.ColumnModel columnModel2;

		// Token: 0x040004DC RID: 1244
		global::XPTable.Models.TextColumn textColumn8;

		// Token: 0x040004DD RID: 1245
		global::XPTable.Models.TextColumn textColumn10;

		// Token: 0x040004DE RID: 1246
		global::XPTable.Models.TextColumn textColumn11;

		// Token: 0x040004DF RID: 1247
		global::XPTable.Models.CheckBoxColumn checkBoxColumn2;

		// Token: 0x040004E0 RID: 1248
		global::System.Windows.Forms.CheckBox checkBox1;

		// Token: 0x040004E1 RID: 1249
		global::BecquerelMonitor.DoubleTextBox doubleTextBox4;

		// Token: 0x040004E2 RID: 1250
		global::BecquerelMonitor.DoubleTextBox doubleTextBox3;

		// Token: 0x040004E3 RID: 1251
		global::System.Windows.Forms.Label label14;

		// Token: 0x040004E4 RID: 1252
		global::BecquerelMonitor.DoubleTextBox doubleTextBox5;

		// Token: 0x040004E5 RID: 1253
		global::System.Windows.Forms.Label label13;

		// Token: 0x040004E6 RID: 1254
		global::XPTable.Models.Table table3;

		// Token: 0x040004E7 RID: 1255
		global::XPTable.Models.ColumnModel columnModel3;

		// Token: 0x040004E8 RID: 1256
		global::XPTable.Models.TableModel tableModel3;

		// Token: 0x040004E9 RID: 1257
		global::System.Windows.Forms.Label label15;

		// Token: 0x040004EA RID: 1258
		global::System.Windows.Forms.Button button7;

		// Token: 0x040004EB RID: 1259
		global::System.Windows.Forms.Button button8;

		// Token: 0x040004EC RID: 1260
		global::System.Windows.Forms.GroupBox groupBox2;

		// Token: 0x040004ED RID: 1261
		global::System.Windows.Forms.TextBox textBox4;

		// Token: 0x040004EE RID: 1262
		global::System.Windows.Forms.Label label17;

		// Token: 0x040004EF RID: 1263
		global::System.Windows.Forms.TextBox textBox3;

		// Token: 0x040004F0 RID: 1264
		global::System.Windows.Forms.Label label16;

		// Token: 0x040004F1 RID: 1265
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x040004F2 RID: 1266
		global::XPTable.Models.TextColumn textColumn12;

		// Token: 0x040004F3 RID: 1267
		global::XPTable.Models.TextColumn textColumn13;

		// Token: 0x040004F4 RID: 1268
		global::System.Windows.Forms.Panel panel1;

		// Token: 0x040004F5 RID: 1269
		global::System.Windows.Forms.Label label18;

		// Token: 0x040004F6 RID: 1270
		global::System.Windows.Forms.TextBox textBox5;

		// Token: 0x040004F7 RID: 1271
		global::System.Windows.Forms.Label label20;

		// Token: 0x040004F8 RID: 1272
		global::System.Windows.Forms.Label label19;

        global::System.Windows.Forms.Label label26;

        // Token: 0x040004F9 RID: 1273
        global::BecquerelMonitor.DoubleTextBox doubleTextBox6;

        global::BecquerelMonitor.DoubleTextBox doubleTextBox7;

        // Token: 0x040004FA RID: 1274
        global::System.Windows.Forms.GroupBox groupBox3;

		// Token: 0x040004FB RID: 1275
		global::XPTable.Models.ImageColumn imageColumn1;

		// Token: 0x040004FC RID: 1276
		global::System.Windows.Forms.Label label11;

		// Token: 0x040004FD RID: 1277
		global::System.Windows.Forms.Label label22;

		// Token: 0x040004FE RID: 1278
		global::System.Windows.Forms.Button button9;

		// Token: 0x040004FF RID: 1279
		global::System.Windows.Forms.ComboBox comboBox2;

		// Token: 0x04000500 RID: 1280
		global::System.Windows.Forms.Label label21;

		// Token: 0x04000501 RID: 1281
		global::System.Windows.Forms.Label label24;

		// Token: 0x04000502 RID: 1282
		global::System.Windows.Forms.NumericUpDown numericUpDown1;

		// Token: 0x04000503 RID: 1283
		global::System.Windows.Forms.Label label23;

		// Token: 0x04000504 RID: 1284
		global::System.Windows.Forms.Button button10;

		// Token: 0x04000505 RID: 1285
		global::ColorComboBox.ColorComboBox colorComboBox1;

		// Token: 0x04000506 RID: 1286
		global::System.Windows.Forms.Label label25;
	}
}
