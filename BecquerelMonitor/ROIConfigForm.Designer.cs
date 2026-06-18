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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ROIConfigForm));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder2 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer2 = new XPTable.Renderers.DragDropRenderer();
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder3 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer3 = new XPTable.Renderers.DragDropRenderer();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.buttonSave = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.buttonEfficiency = new System.Windows.Forms.Button();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.colorComboBox1 = new ColorComboBox.ColorComboBox();
            this.label25 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.doubleTextBox5 = new BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox2 = new BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox1 = new BecquerelMonitor.DoubleTextBox();
            this.label20 = new System.Windows.Forms.Label();
            this.label19 = new System.Windows.Forms.Label();
            this.label26 = new System.Windows.Forms.Label();
            this.doubleTextBox6 = new BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox7 = new BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox4 = new BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox3 = new BecquerelMonitor.DoubleTextBox();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.label12 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.button7 = new System.Windows.Forms.Button();
            this.button8 = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label18 = new System.Windows.Forms.Label();
            this.textBox5 = new System.Windows.Forms.TextBox();
            this.textBox4 = new System.Windows.Forms.TextBox();
            this.label17 = new System.Windows.Forms.Label();
            this.textBox3 = new System.Windows.Forms.TextBox();
            this.label16 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.label24 = new System.Windows.Forms.Label();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.label23 = new System.Windows.Forms.Label();
            this.label22 = new System.Windows.Forms.Label();
            this.button9 = new System.Windows.Forms.Button();
            this.comboBox2 = new System.Windows.Forms.ComboBox();
            this.label21 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.table2 = new XPTable.Models.Table();
            this.columnModel2 = new XPTable.Models.ColumnModel();
            this.textColumn13 = new XPTable.Models.TextColumn();
            this.textColumn8 = new XPTable.Models.TextColumn();
            this.imageColumn1 = new XPTable.Models.ImageColumn();
            this.textColumn10 = new XPTable.Models.TextColumn();
            this.textColumn11 = new XPTable.Models.TextColumn();
            this.tableModel2 = new XPTable.Models.TableModel();
            this.table1 = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.checkBoxColumn2 = new XPTable.Models.CheckBoxColumn();
            this.textColumn2 = new XPTable.Models.TextColumn();
            this.textColumn3 = new XPTable.Models.TextColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            this.button10 = new System.Windows.Forms.Button();
            this.textColumn4 = new XPTable.Models.TextColumn();
            this.textColumn5 = new XPTable.Models.TextColumn();
            this.textColumn6 = new XPTable.Models.TextColumn();
            this.textColumn7 = new XPTable.Models.TextColumn();
            this.table3 = new XPTable.Models.Table();
            this.columnModel3 = new XPTable.Models.ColumnModel();
            this.textColumn1 = new XPTable.Models.TextColumn();
            this.textColumn12 = new XPTable.Models.TextColumn();
            this.checkBoxColumnEff = new XPTable.Models.CheckBoxColumn();
            this.tableModel3 = new XPTable.Models.TableModel();
            this.groupBox1.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.table2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.table3)).BeginInit();
            this.SuspendLayout();
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            resources.ApplyResources(this.button2, "button2");
            this.button2.Name = "button2";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // button3
            // 
            resources.ApplyResources(this.button3, "button3");
            this.button3.Name = "button3";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // button4
            // 
            resources.ApplyResources(this.button4, "button4");
            this.button4.Name = "button4";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // buttonSave
            // 
            resources.ApplyResources(this.buttonSave, "buttonSave");
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.UseVisualStyleBackColor = true;
            this.buttonSave.Click += new System.EventHandler(this.button5_Click);
            // 
            // button6
            // 
            resources.ApplyResources(this.button6, "button6");
            this.button6.Name = "button6";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // buttonEfficiency
            // 
            resources.ApplyResources(this.buttonEfficiency, "buttonEfficiency");
            this.buttonEfficiency.Name = "buttonEfficiency";
            this.buttonEfficiency.UseVisualStyleBackColor = true;
            this.buttonEfficiency.Click += new System.EventHandler(this.buttonEfficiency_Click);
            // 
            // tabControl1
            // 
            resources.ApplyResources(this.tabControl1, "tabControl1");
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // textBox1
            // 
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // comboBox1
            // 
            resources.ApplyResources(this.comboBox1, "comboBox1");
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Items.AddRange(new object[] {
            resources.GetString("comboBox1.Items")});
            this.comboBox1.Name = "comboBox1";
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
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
            // 
            // groupBox3
            // 
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
            resources.ApplyResources(this.groupBox3, "groupBox3");
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.TabStop = false;
            // 
            // colorComboBox1
            // 
            this.colorComboBox1.Extended = true;
            resources.ApplyResources(this.colorComboBox1, "colorComboBox1");
            this.colorComboBox1.Name = "colorComboBox1";
            this.colorComboBox1.SelectedColor = System.Drawing.Color.Black;
            this.colorComboBox1.ColorChanged += new ColorComboBox.ColorChangedHandler(this.colorComboBox1_ColorChanged);
            // 
            // label25
            // 
            resources.ApplyResources(this.label25, "label25");
            this.label25.Name = "label25";
            // 
            // label13
            // 
            resources.ApplyResources(this.label13, "label13");
            this.label13.Name = "label13";
            // 
            // label14
            // 
            resources.ApplyResources(this.label14, "label14");
            this.label14.Name = "label14";
            // 
            // doubleTextBox5
            // 
            resources.ApplyResources(this.doubleTextBox5, "doubleTextBox5");
            this.doubleTextBox5.Name = "doubleTextBox5";
            this.doubleTextBox5.TextChanged += new System.EventHandler(this.doubleTextBox5_TextChanged);
            // 
            // doubleTextBox2
            // 
            resources.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
            this.doubleTextBox2.Name = "doubleTextBox2";
            this.doubleTextBox2.TextChanged += new System.EventHandler(this.doubleTextBox2_TextChanged);
            // 
            // doubleTextBox1
            // 
            resources.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
            this.doubleTextBox1.Name = "doubleTextBox1";
            this.doubleTextBox1.TextChanged += new System.EventHandler(this.doubleTextBox1_TextChanged);
            // 
            // label20
            // 
            resources.ApplyResources(this.label20, "label20");
            this.label20.Name = "label20";
            // 
            // label19
            // 
            resources.ApplyResources(this.label19, "label19");
            this.label19.Name = "label19";
            // 
            // label26
            // 
            resources.ApplyResources(this.label26, "label26");
            this.label26.Name = "label26";
            // 
            // doubleTextBox6
            // 
            resources.ApplyResources(this.doubleTextBox6, "doubleTextBox6");
            this.doubleTextBox6.Name = "doubleTextBox6";
            this.doubleTextBox6.TextChanged += new System.EventHandler(this.doubleTextBox6_TextChanged);
            // 
            // doubleTextBox7
            // 
            resources.ApplyResources(this.doubleTextBox7, "doubleTextBox7");
            this.doubleTextBox7.Name = "doubleTextBox7";
            this.doubleTextBox7.TextChanged += new System.EventHandler(this.doubleTextBox7_TextChanged);
            // 
            // doubleTextBox4
            // 
            resources.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
            this.doubleTextBox4.Name = "doubleTextBox4";
            this.doubleTextBox4.TextChanged += new System.EventHandler(this.doubleTextBox4_TextChanged);
            // 
            // doubleTextBox3
            // 
            resources.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
            this.doubleTextBox3.Name = "doubleTextBox3";
            this.doubleTextBox3.TextChanged += new System.EventHandler(this.doubleTextBox3_TextChanged);
            // 
            // checkBox1
            // 
            resources.ApplyResources(this.checkBox1, "checkBox1");
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // label12
            // 
            resources.ApplyResources(this.label12, "label12");
            this.label12.Name = "label12";
            // 
            // label10
            // 
            resources.ApplyResources(this.label10, "label10");
            this.label10.Name = "label10";
            // 
            // label9
            // 
            resources.ApplyResources(this.label9, "label9");
            this.label9.Name = "label9";
            // 
            // textBox2
            // 
            resources.ApplyResources(this.textBox2, "textBox2");
            this.textBox2.Name = "textBox2";
            this.textBox2.TextChanged += new System.EventHandler(this.textBox2_TextChanged);
            // 
            // label8
            // 
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            // 
            // label15
            // 
            resources.ApplyResources(this.label15, "label15");
            this.label15.Name = "label15";
            // 
            // button7
            // 
            resources.ApplyResources(this.button7, "button7");
            this.button7.Name = "button7";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // button8
            // 
            resources.ApplyResources(this.button8, "button8");
            this.button8.Name = "button8";
            this.button8.UseVisualStyleBackColor = true;
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // groupBox2
            // 
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Controls.Add(this.label18);
            this.groupBox2.Controls.Add(this.textBox5);
            this.groupBox2.Controls.Add(this.textBox4);
            this.groupBox2.Controls.Add(this.label17);
            this.groupBox2.Controls.Add(this.textBox3);
            this.groupBox2.Controls.Add(this.label16);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            // 
            // label18
            // 
            resources.ApplyResources(this.label18, "label18");
            this.label18.Name = "label18";
            // 
            // textBox5
            // 
            resources.ApplyResources(this.textBox5, "textBox5");
            this.textBox5.Name = "textBox5";
            this.textBox5.TextChanged += new System.EventHandler(this.textBox5_TextChanged);
            // 
            // textBox4
            // 
            resources.ApplyResources(this.textBox4, "textBox4");
            this.textBox4.Name = "textBox4";
            this.textBox4.ReadOnly = true;
            // 
            // label17
            // 
            resources.ApplyResources(this.label17, "label17");
            this.label17.Name = "label17";
            // 
            // textBox3
            // 
            resources.ApplyResources(this.textBox3, "textBox3");
            this.textBox3.Name = "textBox3";
            this.textBox3.TextChanged += new System.EventHandler(this.textBox3_TextChanged);
            // 
            // label16
            // 
            resources.ApplyResources(this.label16, "label16");
            this.label16.Name = "label16";
            // 
            // panel1
            // 
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.BackColor = System.Drawing.Color.LightGray;
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
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
            this.panel1.Controls.Add(this.buttonEfficiency);
            this.panel1.Name = "panel1";
            // 
            // label24
            // 
            resources.ApplyResources(this.label24, "label24");
            this.label24.Name = "label24";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.DecimalPlaces = 1;
            this.numericUpDown1.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            resources.ApplyResources(this.numericUpDown1, "numericUpDown1");
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            // 
            // label23
            // 
            resources.ApplyResources(this.label23, "label23");
            this.label23.Name = "label23";
            // 
            // label22
            // 
            resources.ApplyResources(this.label22, "label22");
            this.label22.Name = "label22";
            // 
            // button9
            // 
            resources.ApplyResources(this.button9, "button9");
            this.button9.Name = "button9";
            this.button9.UseVisualStyleBackColor = true;
            this.button9.Click += new System.EventHandler(this.button9_Click);
            // 
            // comboBox2
            // 
            this.comboBox2.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox2.DropDownWidth = 200;
            this.comboBox2.FormattingEnabled = true;
            resources.ApplyResources(this.comboBox2, "comboBox2");
            this.comboBox2.Name = "comboBox2";
            this.comboBox2.SelectedIndexChanged += new System.EventHandler(this.comboBox2_SelectedIndexChanged);
            // 
            // label21
            // 
            resources.ApplyResources(this.label21, "label21");
            this.label21.Name = "label21";
            // 
            // label11
            // 
            resources.ApplyResources(this.label11, "label11");
            this.label11.Name = "label11";
            // 
            // table2
            // 
            resources.ApplyResources(this.table2, "table2");
            this.table2.BorderColor = System.Drawing.Color.Black;
            this.table2.ColumnModel = this.columnModel2;
            this.table2.DataMember = null;
            this.table2.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.table2.DragDropRenderer = dragDropRenderer1;
            this.table2.FullRowSelect = true;
            this.table2.GridLines = XPTable.Models.GridLines.Both;
            this.table2.GridLinesContrainedToData = false;
            this.table2.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.table2.Name = "table2";
            this.table2.SortedColumnBackColor = System.Drawing.Color.White;
            this.table2.TableModel = this.tableModel2;
            this.table2.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table2.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table2_SelectionChanged);
            // 
            // columnModel2
            // 
            this.columnModel2.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn13,
            this.textColumn8,
            this.imageColumn1,
            this.textColumn10,
            this.textColumn11});
            // 
            // textColumn13
            // 
            this.textColumn13.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn13, "textColumn13");
            // 
            // textColumn8
            // 
            this.textColumn8.Editable = false;
            this.textColumn8.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn8, "textColumn8");
            // 
            // imageColumn1
            // 
            this.imageColumn1.IsTextTrimmed = false;
            resources.ApplyResources(this.imageColumn1, "imageColumn1");
            // 
            // textColumn10
            // 
            this.textColumn10.Editable = false;
            this.textColumn10.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn10, "textColumn10");
            // 
            // textColumn11
            // 
            this.textColumn11.Editable = false;
            this.textColumn11.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn11, "textColumn11");
            // 
            // table1
            // 
            resources.ApplyResources(this.table1, "table1");
            this.table1.BorderColor = System.Drawing.Color.Black;
            this.table1.ColumnModel = this.columnModel1;
            this.table1.DataMember = null;
            this.table1.DataSourceColumnBinder = dataSourceColumnBinder2;
            dragDropRenderer2.ForeColor = System.Drawing.Color.Red;
            this.table1.DragDropRenderer = dragDropRenderer2;
            this.table1.FullRowSelect = true;
            this.table1.GridLines = XPTable.Models.GridLines.Both;
            this.table1.GridLinesContrainedToData = false;
            this.table1.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.table1.Name = "table1";
            this.table1.SortedColumnBackColor = System.Drawing.Color.White;
            this.table1.TableModel = this.tableModel1;
            this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table1.CellCheckChanged += new XPTable.Events.CellCheckBoxEventHandler(this.table1_CellCheckChanged);
            this.table1.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.checkBoxColumn2,
            this.textColumn2,
            this.textColumn3});
            // 
            // checkBoxColumn2
            // 
            this.checkBoxColumn2.IsTextTrimmed = false;
            resources.ApplyResources(this.checkBoxColumn2, "checkBoxColumn2");
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
            // button10
            // 
            resources.ApplyResources(this.button10, "button10");
            this.button10.Name = "button10";
            this.button10.UseVisualStyleBackColor = true;
            this.button10.Click += new System.EventHandler(this.button10_Click);
            // 
            // textColumn4
            // 
            this.textColumn4.Enabled = false;
            this.textColumn4.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn4, "textColumn4");
            // 
            // textColumn5
            // 
            this.textColumn5.Enabled = false;
            this.textColumn5.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn5, "textColumn5");
            // 
            // textColumn6
            // 
            this.textColumn6.Enabled = false;
            this.textColumn6.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn6, "textColumn6");
            // 
            // textColumn7
            // 
            this.textColumn7.Enabled = false;
            this.textColumn7.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn7, "textColumn7");
            // 
            // table3
            // 
            resources.ApplyResources(this.table3, "table3");
            this.table3.BorderColor = System.Drawing.Color.Black;
            this.table3.ColumnModel = this.columnModel3;
            this.table3.DataMember = null;
            this.table3.DataSourceColumnBinder = dataSourceColumnBinder3;
            dragDropRenderer3.ForeColor = System.Drawing.Color.Red;
            this.table3.DragDropRenderer = dragDropRenderer3;
            this.table3.FullRowSelect = true;
            this.table3.GridLines = XPTable.Models.GridLines.Both;
            this.table3.GridLinesContrainedToData = false;
            this.table3.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.table3.Name = "table3";
            this.table3.SortedColumnBackColor = System.Drawing.Color.White;
            this.table3.TableModel = this.tableModel3;
            this.table3.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table3.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table3_SelectionChanged);
            // 
            // columnModel3
            // 
            this.columnModel3.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn1,
            this.textColumn12,
            this.checkBoxColumnEff});
            // 
            // textColumn1
            // 
            this.textColumn1.Editable = false;
            this.textColumn1.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn1, "textColumn1");
            // 
            // textColumn12
            // 
            this.textColumn12.Editable = false;
            this.textColumn12.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn12, "textColumn12");
            // 
            // checkBoxColumnEff
            // 
            this.checkBoxColumnEff.Alignment = XPTable.Models.ColumnAlignment.Center;
            this.checkBoxColumnEff.DrawText = false;
            this.checkBoxColumnEff.Editable = false;
            this.checkBoxColumnEff.IsTextTrimmed = false;
            resources.ApplyResources(this.checkBoxColumnEff, "checkBoxColumnEff");
            // 
            // ROIConfigForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.button10);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.button8);
            this.Controls.Add(this.button7);
            this.Controls.Add(this.label15);
            this.Controls.Add(this.table3);
            this.Controls.Add(this.button6);
            this.Controls.Add(this.buttonSave);
            this.Name = "ROIConfigForm";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ROIConfigForm_FormClosing);
            this.Load += new System.EventHandler(this.ROIConfigForm_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.table2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.table3)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		// Token: 0x040004B7 RID: 1207
		global::System.ComponentModel.IContainer components = null;

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
		global::System.Windows.Forms.Button buttonSave;

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

		global::XPTable.Models.CheckBoxColumn checkBoxColumnEff;

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

		private System.Windows.Forms.Button buttonEfficiency;
    }
}
