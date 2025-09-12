using BecquerelMonitor.Properties;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	public partial class DeviceConfigForm : Form
	{
        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
		void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DeviceConfigForm));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder4 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer4 = new XPTable.Renderers.DragDropRenderer();
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder2 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer2 = new XPTable.Renderers.DragDropRenderer();
            this.label4 = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.button3 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.button5 = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.button13 = new System.Windows.Forms.Button();
            this.button14 = new System.Windows.Forms.Button();
            this.button15 = new System.Windows.Forms.Button();
            this.button16 = new System.Windows.Forms.Button();
            this.buttonClearDoseRate = new System.Windows.Forms.Button();
            this.label18 = new System.Windows.Forms.Label();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.label21 = new System.Windows.Forms.Label();
            this.doubleTextBox6 = new BecquerelMonitor.DoubleTextBox();
            this.integerTextBox1 = new BecquerelMonitor.IntegerTextBox();
            this.doubleTextBox5 = new BecquerelMonitor.DoubleTextBox();
            this.label28 = new System.Windows.Forms.Label();
            this.label27 = new System.Windows.Forms.Label();
            this.label26 = new System.Windows.Forms.Label();
            this.textBox19 = new System.Windows.Forms.TextBox();
            this.comboBox4 = new System.Windows.Forms.ComboBox();
            this.label25 = new System.Windows.Forms.Label();
            this.textBox18 = new System.Windows.Forms.TextBox();
            this.label24 = new System.Windows.Forms.Label();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.tabPage6 = new System.Windows.Forms.TabPage();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.panel2 = new System.Windows.Forms.Panel();
            this.button10 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.table3 = new XPTable.Models.Table();
            this.columnModel3 = new XPTable.Models.ColumnModel();
            this.textColumn4 = new XPTable.Models.TextColumn();
            this.numberColumn3 = new XPTable.Models.NumberColumn();
            this.numberColumn4 = new XPTable.Models.NumberColumn();
            this.tableModel3 = new XPTable.Models.TableModel();
            this.label19 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.table2 = new XPTable.Models.Table();
            this.columnModel2 = new XPTable.Models.ColumnModel();
            this.textColumn3 = new XPTable.Models.TextColumn();
            this.numberColumn2 = new XPTable.Models.NumberColumn();
            this.numberColumn1 = new XPTable.Models.NumberColumn();
            this.tableModel2 = new XPTable.Models.TableModel();
            this.button9 = new System.Windows.Forms.Button();
            this.button8 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.button11 = new System.Windows.Forms.Button();
            this.label36 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label43 = new System.Windows.Forms.Label();
            this.label44 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.numericUpDown8 = new System.Windows.Forms.TextBox();
            this.numericUpDown9 = new System.Windows.Forms.TextBox();
            this.numericUpDown1 = new System.Windows.Forms.TextBox();
            this.numericUpDown2 = new System.Windows.Forms.TextBox();
            this.numericUpDown7 = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.textBox15 = new System.Windows.Forms.TextBox();
            this.textBox16 = new System.Windows.Forms.TextBox();
            this.tabPage5 = new System.Windows.Forms.TabPage();
            this.peakSpecgroupBox = new System.Windows.Forms.GroupBox();
            this.rightSkewnumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.rightSkewlabel = new System.Windows.Forms.Label();
            this.leftSkewnumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.leftSkewlabel = new System.Windows.Forms.Label();
            this.peakTypecomboBox = new System.Windows.Forms.ComboBox();
            this.peakTypelabel = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label40 = new System.Windows.Forms.Label();
            this.label42 = new System.Windows.Forms.Label();
            this.numericUpDown4 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown6 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown3 = new System.Windows.Forms.NumericUpDown();
            this.label41 = new System.Windows.Forms.Label();
            this.label39 = new System.Windows.Forms.Label();
            this.numericUpDown5 = new System.Windows.Forms.NumericUpDown();
            this.label38 = new System.Windows.Forms.Label();
            this.label45 = new System.Windows.Forms.Label();
            this.label46 = new System.Windows.Forms.Label();
            this.label47 = new System.Windows.Forms.Label();
            this.label48 = new System.Windows.Forms.Label();
            this.label50 = new System.Windows.Forms.Label();
            this.label51 = new System.Windows.Forms.Label();
            this.label52 = new System.Windows.Forms.Label();
            this.numericUpDown10 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown11 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown12 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown13 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown14 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown15 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown16 = new System.Windows.Forms.NumericUpDown();
            this.label49 = new System.Windows.Forms.Label();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.label31 = new System.Windows.Forms.Label();
            this.label16 = new System.Windows.Forms.Label();
            this.button7 = new System.Windows.Forms.Button();
            this.label23 = new System.Windows.Forms.Label();
            this.textBox17 = new System.Windows.Forms.TextBox();
            this.effROIText = new System.Windows.Forms.Label();
            this.selectEffROI = new System.Windows.Forms.ComboBox();
            this.clearEffROI = new System.Windows.Forms.Button();
            this.tabPage7 = new System.Windows.Forms.TabPage();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.table4 = new XPTable.Models.Table();
            this.columnModel4 = new XPTable.Models.ColumnModel();
            this.numberColumn5 = new XPTable.Models.NumberColumn();
            this.numberColumn6 = new XPTable.Models.NumberColumn();
            this.numberColumn7 = new XPTable.Models.NumberColumn();
            this.numberColumn8 = new XPTable.Models.NumberColumn();
            this.tableModel4 = new XPTable.Models.TableModel();
            this.labelDREstimateTitle = new System.Windows.Forms.Label();
            this.textBoxEffFile = new System.Windows.Forms.TextBox();
            this.textBoxDoseRateSpectrumFile = new System.Windows.Forms.TextBox();
            this.buttonEstimateDRConf = new System.Windows.Forms.Button();
            this.labelDoseRateValue = new System.Windows.Forms.Label();
            this.upDownDoseRateValue = new System.Windows.Forms.NumericUpDown();
            this.labelEffNote = new System.Windows.Forms.Label();
            this.buttonLoadEff = new System.Windows.Forms.Button();
            this.labelSpectrumNote = new System.Windows.Forms.Label();
            this.buttonLoadDoseRateSpectrum = new System.Windows.Forms.Button();
            this.button12 = new System.Windows.Forms.Button();
            this.table1 = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.textColumn1 = new XPTable.Models.TextColumn();
            this.textColumn2 = new XPTable.Models.TextColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.table3)).BeginInit();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.table2)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.tabPage5.SuspendLayout();
            this.peakSpecgroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rightSkewnumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.leftSkewnumericUpDown)).BeginInit();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown6)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown3)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown5)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown10)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown11)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown12)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown13)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown14)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown15)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown16)).BeginInit();
            this.tabPage4.SuspendLayout();
            this.tabPage7.SuspendLayout();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.table4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.upDownDoseRateValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).BeginInit();
            this.SuspendLayout();
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // textBox1
            // 
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
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
            // button5
            // 
            resources.ApplyResources(this.button5, "button5");
            this.button5.Name = "button5";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // button6
            // 
            resources.ApplyResources(this.button6, "button6");
            this.button6.Name = "button6";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button13
            // 
            resources.ApplyResources(this.button13, "button13");
            this.button13.Name = "button13";
            this.button13.UseVisualStyleBackColor = true;
            this.button13.Click += new System.EventHandler(this.button13_Click);
            // 
            // button14
            // 
            resources.ApplyResources(this.button14, "button14");
            this.button14.Name = "button14";
            this.button14.UseVisualStyleBackColor = true;
            this.button14.Click += new System.EventHandler(this.button14_Click);
            // 
            // button15
            // 
            resources.ApplyResources(this.button15, "button15");
            this.button15.Name = "button15";
            this.button15.UseVisualStyleBackColor = true;
            this.button15.Click += new System.EventHandler(this.button15_Click);
            // 
            // button16
            // 
            resources.ApplyResources(this.button16, "button16");
            this.button16.Name = "button16";
            this.button16.UseVisualStyleBackColor = true;
            this.button16.Click += new System.EventHandler(this.button16_Click);
            // 
            // buttonClearDoseRate
            // 
            resources.ApplyResources(this.buttonClearDoseRate, "buttonClearDoseRate");
            this.buttonClearDoseRate.Name = "buttonClearDoseRate";
            this.buttonClearDoseRate.Click += new System.EventHandler(this.buttonClearDoseRate_Click);
            // 
            // label18
            // 
            resources.ApplyResources(this.label18, "label18");
            this.label18.Name = "label18";
            // 
            // tabControl1
            // 
            resources.ApplyResources(this.tabControl1, "tabControl1");
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Controls.Add(this.tabPage6);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage5);
            this.tabControl1.Controls.Add(this.tabPage4);
            this.tabControl1.Controls.Add(this.tabPage7);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Selecting += new System.Windows.Forms.TabControlCancelEventHandler(this.tabControl1_Selecting);
            // 
            // tabPage1
            // 
            this.tabPage1.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.comboBox1);
            this.tabPage1.Controls.Add(this.label21);
            this.tabPage1.Controls.Add(this.doubleTextBox6);
            this.tabPage1.Controls.Add(this.integerTextBox1);
            this.tabPage1.Controls.Add(this.doubleTextBox5);
            this.tabPage1.Controls.Add(this.label28);
            this.tabPage1.Controls.Add(this.label27);
            this.tabPage1.Controls.Add(this.label26);
            this.tabPage1.Controls.Add(this.textBox19);
            this.tabPage1.Controls.Add(this.comboBox4);
            this.tabPage1.Controls.Add(this.label25);
            this.tabPage1.Controls.Add(this.textBox18);
            this.tabPage1.Controls.Add(this.label24);
            this.tabPage1.Controls.Add(this.textBox1);
            this.tabPage1.Controls.Add(this.label18);
            this.tabPage1.Controls.Add(this.label4);
            resources.ApplyResources(this.tabPage1, "tabPage1");
            this.tabPage1.Name = "tabPage1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // comboBox1
            // 
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox1.FormattingEnabled = true;
            resources.ApplyResources(this.comboBox1, "comboBox1");
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // label21
            // 
            resources.ApplyResources(this.label21, "label21");
            this.label21.Name = "label21";
            // 
            // doubleTextBox6
            // 
            resources.ApplyResources(this.doubleTextBox6, "doubleTextBox6");
            this.doubleTextBox6.Name = "doubleTextBox6";
            this.doubleTextBox6.TextChanged += new System.EventHandler(this.doubleTextBox6_TextChanged);
            // 
            // integerTextBox1
            // 
            resources.ApplyResources(this.integerTextBox1, "integerTextBox1");
            this.integerTextBox1.Name = "integerTextBox1";
            this.integerTextBox1.TextChanged += new System.EventHandler(this.integerTextBox1_TextChanged);
            // 
            // doubleTextBox5
            // 
            resources.ApplyResources(this.doubleTextBox5, "doubleTextBox5");
            this.doubleTextBox5.Name = "doubleTextBox5";
            this.doubleTextBox5.TextChanged += new System.EventHandler(this.doubleTextBox5_TextChanged);
            // 
            // label28
            // 
            resources.ApplyResources(this.label28, "label28");
            this.label28.Name = "label28";
            // 
            // label27
            // 
            resources.ApplyResources(this.label27, "label27");
            this.label27.Name = "label27";
            // 
            // label26
            // 
            resources.ApplyResources(this.label26, "label26");
            this.label26.Name = "label26";
            // 
            // textBox19
            // 
            resources.ApplyResources(this.textBox19, "textBox19");
            this.textBox19.Name = "textBox19";
            this.textBox19.TextChanged += new System.EventHandler(this.textBox19_TextChanged);
            // 
            // comboBox4
            // 
            this.comboBox4.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox4.FormattingEnabled = true;
            resources.ApplyResources(this.comboBox4, "comboBox4");
            this.comboBox4.Name = "comboBox4";
            this.comboBox4.SelectedIndexChanged += new System.EventHandler(this.comboBox4_SelectedIndexChanged);
            // 
            // label25
            // 
            resources.ApplyResources(this.label25, "label25");
            this.label25.Name = "label25";
            // 
            // textBox18
            // 
            resources.ApplyResources(this.textBox18, "textBox18");
            this.textBox18.Name = "textBox18";
            this.textBox18.ReadOnly = true;
            this.textBox18.TabStop = false;
            // 
            // label24
            // 
            resources.ApplyResources(this.label24, "label24");
            this.label24.Name = "label24";
            // 
            // tabPage3
            // 
            this.tabPage3.BackColor = System.Drawing.SystemColors.Control;
            resources.ApplyResources(this.tabPage3, "tabPage3");
            this.tabPage3.Name = "tabPage3";
            // 
            // tabPage6
            // 
            this.tabPage6.BackColor = System.Drawing.SystemColors.Control;
            resources.ApplyResources(this.tabPage6, "tabPage6");
            this.tabPage6.Name = "tabPage6";
            // 
            // tabPage2
            // 
            this.tabPage2.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage2.Controls.Add(this.panel2);
            this.tabPage2.Controls.Add(this.label19);
            this.tabPage2.Controls.Add(this.label9);
            this.tabPage2.Controls.Add(this.panel1);
            this.tabPage2.Controls.Add(this.groupBox1);
            this.tabPage2.Controls.Add(this.label8);
            this.tabPage2.Controls.Add(this.textBox15);
            this.tabPage2.Controls.Add(this.textBox16);
            resources.ApplyResources(this.tabPage2, "tabPage2");
            this.tabPage2.Name = "tabPage2";
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panel2.Controls.Add(this.button10);
            this.panel2.Controls.Add(this.button2);
            this.panel2.Controls.Add(this.table3);
            resources.ApplyResources(this.panel2, "panel2");
            this.panel2.Name = "panel2";
            // 
            // button10
            // 
            resources.ApplyResources(this.button10, "button10");
            this.button10.Name = "button10";
            this.button10.UseVisualStyleBackColor = true;
            this.button10.Click += new System.EventHandler(this.button10_Click);
            // 
            // button2
            // 
            resources.ApplyResources(this.button2, "button2");
            this.button2.Name = "button2";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // table3
            // 
            this.table3.BorderColor = System.Drawing.Color.Black;
            this.table3.ColumnModel = this.columnModel3;
            this.table3.DataMember = null;
            this.table3.DataSourceColumnBinder = dataSourceColumnBinder4;
            this.table3.DragDropRenderer = dragDropRenderer4;
            this.table3.FullRowSelect = true;
            this.table3.GridLines = XPTable.Models.GridLines.Both;
            this.table3.GridLinesContrainedToData = false;
            resources.ApplyResources(this.table3, "table3");
            this.table3.Name = "table3";
            this.table3.TableModel = this.tableModel3;
            this.table3.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table3.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table3_EditingStopped);
            // 
            // columnModel3
            // 
            this.columnModel3.Columns.AddRange(new XPTable.Models.Column[] {
            ((XPTable.Models.Column)(this.textColumn4)),
            ((XPTable.Models.Column)(this.numberColumn3)),
            ((XPTable.Models.Column)(this.numberColumn4))});
            // 
            // textColumn4
            // 
            this.textColumn4.IsTextTrimmed = false;
            this.textColumn4.Sortable = false;
            resources.ApplyResources(this.textColumn4, "textColumn4");
            // 
            // numberColumn3
            // 
            this.numberColumn3.IsTextTrimmed = false;
            this.numberColumn3.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numberColumn3.Sortable = false;
            resources.ApplyResources(this.numberColumn3, "numberColumn3");
            // 
            // numberColumn4
            // 
            this.numberColumn4.IsTextTrimmed = false;
            this.numberColumn4.Sortable = false;
            resources.ApplyResources(this.numberColumn4, "numberColumn4");
            // 
            // label19
            // 
            resources.ApplyResources(this.label19, "label19");
            this.label19.Name = "label19";
            // 
            // label9
            // 
            resources.ApplyResources(this.label9, "label9");
            this.label9.Name = "label9";
            // 
            // panel1
            // 
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panel1.Controls.Add(this.table2);
            this.panel1.Controls.Add(this.button9);
            this.panel1.Controls.Add(this.button8);
            this.panel1.Controls.Add(this.button1);
            this.panel1.Controls.Add(this.button11);
            this.panel1.Controls.Add(this.label36);
            this.panel1.Name = "panel1";
            // 
            // table2
            // 
            this.table2.BorderColor = System.Drawing.Color.Black;
            this.table2.ColumnModel = this.columnModel2;
            this.table2.DataMember = null;
            this.table2.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.table2.DragDropRenderer = dragDropRenderer1;
            this.table2.FullRowSelect = true;
            this.table2.GridLines = XPTable.Models.GridLines.Both;
            this.table2.GridLinesContrainedToData = false;
            resources.ApplyResources(this.table2, "table2");
            this.table2.Name = "table2";
            this.table2.TableModel = this.tableModel2;
            this.table2.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table2.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table2_EditingStopped);
            this.table2.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table2_SelectionChanged);
            // 
            // columnModel2
            // 
            this.columnModel2.Columns.AddRange(new XPTable.Models.Column[] {
            ((XPTable.Models.Column)(this.textColumn3)),
            ((XPTable.Models.Column)(this.numberColumn2)),
            ((XPTable.Models.Column)(this.numberColumn1))});
            // 
            // textColumn3
            // 
            this.textColumn3.Editable = false;
            this.textColumn3.IsTextTrimmed = false;
            this.textColumn3.Sortable = false;
            resources.ApplyResources(this.textColumn3, "textColumn3");
            // 
            // numberColumn2
            // 
            this.numberColumn2.IsTextTrimmed = false;
            this.numberColumn2.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.numberColumn2.Sortable = false;
            resources.ApplyResources(this.numberColumn2, "numberColumn2");
            // 
            // numberColumn1
            // 
            this.numberColumn1.IsTextTrimmed = false;
            this.numberColumn1.Minimum = new decimal(new int[] {
            10000,
            0,
            0,
            -2147483648});
            this.numberColumn1.Sortable = false;
            resources.ApplyResources(this.numberColumn1, "numberColumn1");
            // 
            // button9
            // 
            resources.ApplyResources(this.button9, "button9");
            this.button9.Name = "button9";
            this.button9.UseVisualStyleBackColor = true;
            this.button9.Click += new System.EventHandler(this.button9_Click);
            // 
            // button8
            // 
            resources.ApplyResources(this.button8, "button8");
            this.button8.Name = "button8";
            this.button8.UseVisualStyleBackColor = true;
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button11
            // 
            resources.ApplyResources(this.button11, "button11");
            this.button11.Name = "button11";
            this.button11.UseVisualStyleBackColor = true;
            this.button11.Click += new System.EventHandler(this.button11_Click);
            // 
            // label36
            // 
            resources.ApplyResources(this.label36, "label36");
            this.label36.Name = "label36";
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.label43);
            this.groupBox1.Controls.Add(this.label44);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.label7);
            this.groupBox1.Controls.Add(this.numericUpDown8);
            this.groupBox1.Controls.Add(this.numericUpDown9);
            this.groupBox1.Controls.Add(this.numericUpDown1);
            this.groupBox1.Controls.Add(this.numericUpDown2);
            this.groupBox1.Controls.Add(this.numericUpDown7);
            this.groupBox1.Controls.Add(this.button13);
            this.groupBox1.Controls.Add(this.button14);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // label43
            // 
            resources.ApplyResources(this.label43, "label43");
            this.label43.Name = "label43";
            // 
            // label44
            // 
            resources.ApplyResources(this.label44, "label44");
            this.label44.Name = "label44";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            // 
            // numericUpDown8
            // 
            resources.ApplyResources(this.numericUpDown8, "numericUpDown8");
            this.numericUpDown8.Name = "numericUpDown8";
            this.numericUpDown8.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown8_KeyDown);
            this.numericUpDown8.Leave += new System.EventHandler(this.numericUpDown8_Leave);
            // 
            // numericUpDown9
            // 
            resources.ApplyResources(this.numericUpDown9, "numericUpDown9");
            this.numericUpDown9.Name = "numericUpDown9";
            this.numericUpDown9.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown9_KeyDown);
            this.numericUpDown9.Leave += new System.EventHandler(this.numericUpDown9_Leave);
            // 
            // numericUpDown1
            // 
            resources.ApplyResources(this.numericUpDown1, "numericUpDown1");
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown1_KeyDown);
            this.numericUpDown1.Leave += new System.EventHandler(this.numericUpDown1_Leave);
            // 
            // numericUpDown2
            // 
            resources.ApplyResources(this.numericUpDown2, "numericUpDown2");
            this.numericUpDown2.Name = "numericUpDown2";
            this.numericUpDown2.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown2_KeyDown);
            this.numericUpDown2.Leave += new System.EventHandler(this.numericUpDown2_Leave);
            // 
            // numericUpDown7
            // 
            resources.ApplyResources(this.numericUpDown7, "numericUpDown7");
            this.numericUpDown7.Name = "numericUpDown7";
            this.numericUpDown7.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown7_KeyDown);
            this.numericUpDown7.Leave += new System.EventHandler(this.numericUpDown7_Leave);
            // 
            // label8
            // 
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            // 
            // textBox15
            // 
            resources.ApplyResources(this.textBox15, "textBox15");
            this.textBox15.BackColor = System.Drawing.SystemColors.ControlLight;
            this.textBox15.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox15.Name = "textBox15";
            this.textBox15.ReadOnly = true;
            this.textBox15.TabStop = false;
            // 
            // textBox16
            // 
            resources.ApplyResources(this.textBox16, "textBox16");
            this.textBox16.BackColor = System.Drawing.SystemColors.ControlLight;
            this.textBox16.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox16.Name = "textBox16";
            this.textBox16.ReadOnly = true;
            this.textBox16.TabStop = false;
            // 
            // tabPage5
            // 
            this.tabPage5.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage5.Controls.Add(this.peakSpecgroupBox);
            this.tabPage5.Controls.Add(this.groupBox2);
            this.tabPage5.Controls.Add(this.label49);
            resources.ApplyResources(this.tabPage5, "tabPage5");
            this.tabPage5.Name = "tabPage5";
            // 
            // peakSpecgroupBox
            // 
            resources.ApplyResources(this.peakSpecgroupBox, "peakSpecgroupBox");
            this.peakSpecgroupBox.Controls.Add(this.rightSkewnumericUpDown);
            this.peakSpecgroupBox.Controls.Add(this.rightSkewlabel);
            this.peakSpecgroupBox.Controls.Add(this.leftSkewnumericUpDown);
            this.peakSpecgroupBox.Controls.Add(this.leftSkewlabel);
            this.peakSpecgroupBox.Controls.Add(this.peakTypecomboBox);
            this.peakSpecgroupBox.Controls.Add(this.peakTypelabel);
            this.peakSpecgroupBox.Name = "peakSpecgroupBox";
            this.peakSpecgroupBox.TabStop = false;
            // 
            // rightSkewnumericUpDown
            // 
            this.rightSkewnumericUpDown.DecimalPlaces = 1;
            this.rightSkewnumericUpDown.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            resources.ApplyResources(this.rightSkewnumericUpDown, "rightSkewnumericUpDown");
            this.rightSkewnumericUpDown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.rightSkewnumericUpDown.Name = "rightSkewnumericUpDown";
            this.rightSkewnumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.rightSkewnumericUpDown.ValueChanged += new System.EventHandler(this.rightSkewnumericUpDown_ValueChanged);
            // 
            // rightSkewlabel
            // 
            resources.ApplyResources(this.rightSkewlabel, "rightSkewlabel");
            this.rightSkewlabel.Name = "rightSkewlabel";
            // 
            // leftSkewnumericUpDown
            // 
            this.leftSkewnumericUpDown.DecimalPlaces = 1;
            this.leftSkewnumericUpDown.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            resources.ApplyResources(this.leftSkewnumericUpDown, "leftSkewnumericUpDown");
            this.leftSkewnumericUpDown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.leftSkewnumericUpDown.Name = "leftSkewnumericUpDown";
            this.leftSkewnumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.leftSkewnumericUpDown.ValueChanged += new System.EventHandler(this.leftSkewnumericUpDown_ValueChanged);
            // 
            // leftSkewlabel
            // 
            resources.ApplyResources(this.leftSkewlabel, "leftSkewlabel");
            this.leftSkewlabel.Name = "leftSkewlabel";
            // 
            // peakTypecomboBox
            // 
            this.peakTypecomboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.peakTypecomboBox.FormattingEnabled = true;
            this.peakTypecomboBox.Items.AddRange(new object[] {
            resources.GetString("peakTypecomboBox.Items"),
            resources.GetString("peakTypecomboBox.Items1")});
            resources.ApplyResources(this.peakTypecomboBox, "peakTypecomboBox");
            this.peakTypecomboBox.Name = "peakTypecomboBox";
            this.peakTypecomboBox.SelectedIndexChanged += new System.EventHandler(this.peakTypecomboBox_SelectedIndexChanged);
            // 
            // peakTypelabel
            // 
            resources.ApplyResources(this.peakTypelabel, "peakTypelabel");
            this.peakTypelabel.Name = "peakTypelabel";
            // 
            // groupBox2
            // 
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Controls.Add(this.label40);
            this.groupBox2.Controls.Add(this.label42);
            this.groupBox2.Controls.Add(this.numericUpDown4);
            this.groupBox2.Controls.Add(this.numericUpDown6);
            this.groupBox2.Controls.Add(this.numericUpDown3);
            this.groupBox2.Controls.Add(this.label41);
            this.groupBox2.Controls.Add(this.label39);
            this.groupBox2.Controls.Add(this.numericUpDown5);
            this.groupBox2.Controls.Add(this.label38);
            this.groupBox2.Controls.Add(this.label45);
            this.groupBox2.Controls.Add(this.label46);
            this.groupBox2.Controls.Add(this.label47);
            this.groupBox2.Controls.Add(this.label48);
            this.groupBox2.Controls.Add(this.label50);
            this.groupBox2.Controls.Add(this.label51);
            this.groupBox2.Controls.Add(this.label52);
            this.groupBox2.Controls.Add(this.numericUpDown10);
            this.groupBox2.Controls.Add(this.numericUpDown11);
            this.groupBox2.Controls.Add(this.numericUpDown12);
            this.groupBox2.Controls.Add(this.numericUpDown13);
            this.groupBox2.Controls.Add(this.numericUpDown14);
            this.groupBox2.Controls.Add(this.numericUpDown15);
            this.groupBox2.Controls.Add(this.numericUpDown16);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            // 
            // label40
            // 
            resources.ApplyResources(this.label40, "label40");
            this.label40.Name = "label40";
            // 
            // label42
            // 
            resources.ApplyResources(this.label42, "label42");
            this.label42.Name = "label42";
            // 
            // numericUpDown4
            // 
            resources.ApplyResources(this.numericUpDown4, "numericUpDown4");
            this.numericUpDown4.Name = "numericUpDown4";
            this.numericUpDown4.ValueChanged += new System.EventHandler(this.numericUpDown4_ValueChanged);
            // 
            // numericUpDown6
            // 
            this.numericUpDown6.DecimalPlaces = 1;
            resources.ApplyResources(this.numericUpDown6, "numericUpDown6");
            this.numericUpDown6.Name = "numericUpDown6";
            this.numericUpDown6.ValueChanged += new System.EventHandler(this.numericUpDown6_ValueChanged);
            // 
            // numericUpDown3
            // 
            resources.ApplyResources(this.numericUpDown3, "numericUpDown3");
            this.numericUpDown3.Name = "numericUpDown3";
            this.numericUpDown3.ValueChanged += new System.EventHandler(this.numericUpDown3_ValueChanged);
            // 
            // label41
            // 
            resources.ApplyResources(this.label41, "label41");
            this.label41.Name = "label41";
            // 
            // label39
            // 
            resources.ApplyResources(this.label39, "label39");
            this.label39.Name = "label39";
            // 
            // numericUpDown5
            // 
            resources.ApplyResources(this.numericUpDown5, "numericUpDown5");
            this.numericUpDown5.Name = "numericUpDown5";
            this.numericUpDown5.ValueChanged += new System.EventHandler(this.numericUpDown5_ValueChanged);
            // 
            // label38
            // 
            resources.ApplyResources(this.label38, "label38");
            this.label38.Name = "label38";
            // 
            // label45
            // 
            resources.ApplyResources(this.label45, "label45");
            this.label45.Name = "label45";
            // 
            // label46
            // 
            resources.ApplyResources(this.label46, "label46");
            this.label46.Name = "label46";
            // 
            // label47
            // 
            resources.ApplyResources(this.label47, "label47");
            this.label47.Name = "label47";
            // 
            // label48
            // 
            resources.ApplyResources(this.label48, "label48");
            this.label48.Name = "label48";
            // 
            // label50
            // 
            resources.ApplyResources(this.label50, "label50");
            this.label50.Name = "label50";
            // 
            // label51
            // 
            resources.ApplyResources(this.label51, "label51");
            this.label51.Name = "label51";
            // 
            // label52
            // 
            resources.ApplyResources(this.label52, "label52");
            this.label52.Name = "label52";
            // 
            // numericUpDown10
            // 
            resources.ApplyResources(this.numericUpDown10, "numericUpDown10");
            this.numericUpDown10.Name = "numericUpDown10";
            this.numericUpDown10.ValueChanged += new System.EventHandler(this.numericUpDown10_ValueChanged);
            // 
            // numericUpDown11
            // 
            resources.ApplyResources(this.numericUpDown11, "numericUpDown11");
            this.numericUpDown11.Name = "numericUpDown11";
            this.numericUpDown11.ValueChanged += new System.EventHandler(this.numericUpDown11_ValueChanged);
            // 
            // numericUpDown12
            // 
            resources.ApplyResources(this.numericUpDown12, "numericUpDown12");
            this.numericUpDown12.Name = "numericUpDown12";
            this.numericUpDown12.ValueChanged += new System.EventHandler(this.numericUpDown12_ValueChanged);
            // 
            // numericUpDown13
            // 
            resources.ApplyResources(this.numericUpDown13, "numericUpDown13");
            this.numericUpDown13.Name = "numericUpDown13";
            this.numericUpDown13.ValueChanged += new System.EventHandler(this.numericUpDown13_ValueChanged);
            // 
            // numericUpDown14
            // 
            resources.ApplyResources(this.numericUpDown14, "numericUpDown14");
            this.numericUpDown14.Name = "numericUpDown14";
            this.numericUpDown14.ValueChanged += new System.EventHandler(this.numericUpDown14_ValueChanged);
            // 
            // numericUpDown15
            // 
            resources.ApplyResources(this.numericUpDown15, "numericUpDown15");
            this.numericUpDown15.Name = "numericUpDown15";
            this.numericUpDown15.ValueChanged += new System.EventHandler(this.numericUpDown15_ValueChanged);
            // 
            // numericUpDown16
            // 
            resources.ApplyResources(this.numericUpDown16, "numericUpDown16");
            this.numericUpDown16.Name = "numericUpDown16";
            this.numericUpDown16.ValueChanged += new System.EventHandler(this.numericUpDown16_ValueChanged);
            // 
            // label49
            // 
            resources.ApplyResources(this.label49, "label49");
            this.label49.Name = "label49";
            // 
            // tabPage4
            // 
            this.tabPage4.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage4.Controls.Add(this.label31);
            this.tabPage4.Controls.Add(this.label16);
            this.tabPage4.Controls.Add(this.button7);
            this.tabPage4.Controls.Add(this.label23);
            this.tabPage4.Controls.Add(this.textBox17);
            this.tabPage4.Controls.Add(this.effROIText);
            this.tabPage4.Controls.Add(this.selectEffROI);
            this.tabPage4.Controls.Add(this.clearEffROI);
            resources.ApplyResources(this.tabPage4, "tabPage4");
            this.tabPage4.Name = "tabPage4";
            // 
            // label31
            // 
            resources.ApplyResources(this.label31, "label31");
            this.label31.Name = "label31";
            // 
            // label16
            // 
            resources.ApplyResources(this.label16, "label16");
            this.label16.Name = "label16";
            // 
            // button7
            // 
            resources.ApplyResources(this.button7, "button7");
            this.button7.Name = "button7";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // label23
            // 
            resources.ApplyResources(this.label23, "label23");
            this.label23.Name = "label23";
            // 
            // textBox17
            // 
            resources.ApplyResources(this.textBox17, "textBox17");
            this.textBox17.Name = "textBox17";
            this.textBox17.TextChanged += new System.EventHandler(this.textBox17_TextChanged);
            // 
            // effROIText
            // 
            resources.ApplyResources(this.effROIText, "effROIText");
            this.effROIText.Name = "effROIText";
            // 
            // selectEffROI
            // 
            this.selectEffROI.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.selectEffROI.FormattingEnabled = true;
            resources.ApplyResources(this.selectEffROI, "selectEffROI");
            this.selectEffROI.Name = "selectEffROI";
            this.selectEffROI.SelectedIndexChanged += new System.EventHandler(this.selectEffROI_SelectedIndexChanged);
            // 
            // clearEffROI
            // 
            resources.ApplyResources(this.clearEffROI, "clearEffROI");
            this.clearEffROI.Name = "clearEffROI";
            this.clearEffROI.UseVisualStyleBackColor = true;
            this.clearEffROI.Click += new System.EventHandler(this.clearEffROI_Click);
            // 
            // tabPage7
            // 
            this.tabPage7.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage7.Controls.Add(this.groupBox3);
            this.tabPage7.Controls.Add(this.labelDREstimateTitle);
            this.tabPage7.Controls.Add(this.textBoxEffFile);
            this.tabPage7.Controls.Add(this.textBoxDoseRateSpectrumFile);
            this.tabPage7.Controls.Add(this.buttonEstimateDRConf);
            this.tabPage7.Controls.Add(this.labelDoseRateValue);
            this.tabPage7.Controls.Add(this.upDownDoseRateValue);
            this.tabPage7.Controls.Add(this.labelEffNote);
            this.tabPage7.Controls.Add(this.buttonLoadEff);
            this.tabPage7.Controls.Add(this.labelSpectrumNote);
            this.tabPage7.Controls.Add(this.buttonLoadDoseRateSpectrum);
            resources.ApplyResources(this.tabPage7, "tabPage7");
            this.tabPage7.Name = "tabPage7";
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.table4);
            this.groupBox3.Controls.Add(this.button15);
            this.groupBox3.Controls.Add(this.button16);
            this.groupBox3.Controls.Add(this.buttonClearDoseRate);
            resources.ApplyResources(this.groupBox3, "groupBox3");
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.TabStop = false;
            // 
            // table4
            // 
            this.table4.BorderColor = System.Drawing.Color.Black;
            this.table4.ColumnModel = this.columnModel4;
            this.table4.DataMember = null;
            this.table4.DataSourceColumnBinder = dataSourceColumnBinder4;
            dragDropRenderer4.ForeColor = System.Drawing.Color.Red;
            this.table4.DragDropRenderer = dragDropRenderer4;
            this.table4.FullRowSelect = true;
            this.table4.GridLines = XPTable.Models.GridLines.Both;
            this.table4.GridLinesContrainedToData = false;
            resources.ApplyResources(this.table4, "table4");
            this.table4.Name = "table4";
            this.table4.TableModel = this.tableModel4;
            this.table4.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table4.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table4_EditingStopped);
            // 
            // columnModel4
            // 
            this.columnModel4.Columns.AddRange(new XPTable.Models.Column[] {
            ((XPTable.Models.Column)(this.numberColumn5)),
            ((XPTable.Models.Column)(this.numberColumn6)),
            ((XPTable.Models.Column)(this.numberColumn7)),
            ((XPTable.Models.Column)(this.numberColumn8))});
            // 
            // numberColumn5
            // 
            this.numberColumn5.IsTextTrimmed = false;
            this.numberColumn5.Maximum = new decimal(new int[] {
            2999,
            0,
            0,
            0});
            this.numberColumn5.Sortable = false;
            resources.ApplyResources(this.numberColumn5, "numberColumn5");
            // 
            // numberColumn6
            // 
            this.numberColumn6.IsTextTrimmed = false;
            this.numberColumn6.Maximum = new decimal(new int[] {
            3000,
            0,
            0,
            0});
            this.numberColumn6.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numberColumn6.Sortable = false;
            resources.ApplyResources(this.numberColumn6, "numberColumn6");
            // 
            // numberColumn7
            // 
            this.numberColumn7.IsTextTrimmed = false;
            this.numberColumn7.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.numberColumn7.Sortable = false;
            resources.ApplyResources(this.numberColumn7, "numberColumn7");
            // 
            // numberColumn8
            // 
            this.numberColumn8.IsTextTrimmed = false;
            this.numberColumn8.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.numberColumn8.Sortable = false;
            resources.ApplyResources(this.numberColumn8, "numberColumn8");
            // 
            // labelDREstimateTitle
            // 
            resources.ApplyResources(this.labelDREstimateTitle, "labelDREstimateTitle");
            this.labelDREstimateTitle.Name = "labelDREstimateTitle";
            // 
            // textBoxEffFile
            // 
            resources.ApplyResources(this.textBoxEffFile, "textBoxEffFile");
            this.textBoxEffFile.Name = "textBoxEffFile";
            this.textBoxEffFile.ReadOnly = true;
            // 
            // textBoxDoseRateSpectrumFile
            // 
            resources.ApplyResources(this.textBoxDoseRateSpectrumFile, "textBoxDoseRateSpectrumFile");
            this.textBoxDoseRateSpectrumFile.Name = "textBoxDoseRateSpectrumFile";
            this.textBoxDoseRateSpectrumFile.ReadOnly = true;
            // 
            // buttonEstimateDRConf
            // 
            resources.ApplyResources(this.buttonEstimateDRConf, "buttonEstimateDRConf");
            this.buttonEstimateDRConf.Name = "buttonEstimateDRConf";
            this.buttonEstimateDRConf.UseVisualStyleBackColor = true;
            this.buttonEstimateDRConf.Click += new System.EventHandler(this.buttonEstimateDRConf_Click);
            // 
            // labelDoseRateValue
            // 
            resources.ApplyResources(this.labelDoseRateValue, "labelDoseRateValue");
            this.labelDoseRateValue.Name = "labelDoseRateValue";
            // 
            // upDownDoseRateValue
            // 
            this.upDownDoseRateValue.DecimalPlaces = 2;
            this.upDownDoseRateValue.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            resources.ApplyResources(this.upDownDoseRateValue, "upDownDoseRateValue");
            this.upDownDoseRateValue.Name = "upDownDoseRateValue";
            this.upDownDoseRateValue.Value = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            // 
            // labelEffNote
            // 
            resources.ApplyResources(this.labelEffNote, "labelEffNote");
            this.labelEffNote.Name = "labelEffNote";
            // 
            // buttonLoadEff
            // 
            resources.ApplyResources(this.buttonLoadEff, "buttonLoadEff");
            this.buttonLoadEff.Name = "buttonLoadEff";
            this.buttonLoadEff.UseVisualStyleBackColor = true;
            this.buttonLoadEff.Click += new System.EventHandler(this.buttonLoadEff_Click);
            // 
            // labelSpectrumNote
            // 
            resources.ApplyResources(this.labelSpectrumNote, "labelSpectrumNote");
            this.labelSpectrumNote.Name = "labelSpectrumNote";
            // 
            // buttonLoadDoseRateSpectrum
            // 
            resources.ApplyResources(this.buttonLoadDoseRateSpectrum, "buttonLoadDoseRateSpectrum");
            this.buttonLoadDoseRateSpectrum.Name = "buttonLoadDoseRateSpectrum";
            this.buttonLoadDoseRateSpectrum.UseVisualStyleBackColor = true;
            this.buttonLoadDoseRateSpectrum.Click += new System.EventHandler(this.buttonLoadDoseRateSpectrum_Click);
            // 
            // button12
            // 
            resources.ApplyResources(this.button12, "button12");
            this.button12.Name = "button12";
            this.button12.UseVisualStyleBackColor = true;
            this.button12.Click += new System.EventHandler(this.button12_Click);
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
            this.table1.Name = "table1";
            this.table1.SortedColumnBackColor = System.Drawing.Color.White;
            this.table1.TableModel = this.tableModel1;
            this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table1.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            ((XPTable.Models.Column)(this.textColumn1)),
            ((XPTable.Models.Column)(this.textColumn2))});
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
            // DeviceConfigForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.button12);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.button6);
            this.Controls.Add(this.button5);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.table1);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DeviceConfigForm";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.DeviceConfigForm_FormClosing);
            this.Load += new System.EventHandler(this.DeviceConfigForm_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.table3)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.table2)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.tabPage5.ResumeLayout(false);
            this.tabPage5.PerformLayout();
            this.peakSpecgroupBox.ResumeLayout(false);
            this.peakSpecgroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rightSkewnumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.leftSkewnumericUpDown)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown6)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown3)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown5)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown10)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown11)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown12)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown13)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown14)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown15)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown16)).EndInit();
            this.tabPage4.ResumeLayout(false);
            this.tabPage4.PerformLayout();
            this.tabPage7.ResumeLayout(false);
            this.tabPage7.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.table4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.upDownDoseRateValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

        #endregion

        // Token: 0x0400025D RID: 605
        System.ComponentModel.IContainer components;

		// Token: 0x0400025E RID: 606
		System.Windows.Forms.Label label4;

		// Token: 0x0400025F RID: 607
		System.Windows.Forms.TextBox textBox1;

		// Token: 0x04000260 RID: 608
		XPTable.Models.Table table1;

		// Token: 0x04000261 RID: 609
		XPTable.Models.ColumnModel columnModel1;

		// Token: 0x04000262 RID: 610
		XPTable.Models.TextColumn textColumn1;

		// Token: 0x04000263 RID: 611
		XPTable.Models.TableModel tableModel1;

		// Token: 0x04000264 RID: 612
		System.Windows.Forms.Label label5;

		// Token: 0x04000265 RID: 613
		System.Windows.Forms.Button button3;

		// Token: 0x04000266 RID: 614
		System.Windows.Forms.Button button4;

		// Token: 0x04000267 RID: 615
		System.Windows.Forms.Button button5;

		// Token: 0x04000268 RID: 616
		System.Windows.Forms.Button button6;

		System.Windows.Forms.Button button13;

		System.Windows.Forms.Button button14;

        System.Windows.Forms.Button button15;

        System.Windows.Forms.Button button16;

        // Token: 0x04000269 RID: 617
        XPTable.Models.TextColumn textColumn2;

		// Token: 0x0400026A RID: 618
		System.Windows.Forms.Label label18;

		// Token: 0x0400026B RID: 619
		System.Windows.Forms.TabControl tabControl1;

		// Token: 0x0400026C RID: 620
		System.Windows.Forms.TabPage tabPage1;

		// Token: 0x0400026D RID: 621
		System.Windows.Forms.TabPage tabPage2;

		// Token: 0x0400026E RID: 622
		System.Windows.Forms.TabPage tabPage3;

		// Token: 0x0400026F RID: 623
		System.Windows.Forms.TabPage tabPage4;

		// Token: 0x04000270 RID: 624
		System.Windows.Forms.Button button7;

		// Token: 0x04000271 RID: 625
		System.Windows.Forms.Label label23;

		// Token: 0x04000272 RID: 626
		System.Windows.Forms.TextBox textBox17;

		// Token: 0x04000273 RID: 627
		System.Windows.Forms.TextBox textBox18;

		// Token: 0x04000274 RID: 628
		System.Windows.Forms.Label label24;

		// Token: 0x04000275 RID: 629
		System.Windows.Forms.Label label26;

		// Token: 0x04000276 RID: 630
		System.Windows.Forms.TextBox textBox19;

		// Token: 0x04000277 RID: 631
		System.Windows.Forms.ComboBox comboBox4;

		// Token: 0x04000278 RID: 632
		System.Windows.Forms.Label label25;

		// Token: 0x04000279 RID: 633
		System.Windows.Forms.Label label27;

		// Token: 0x0400027A RID: 634
		System.Windows.Forms.Label label28;

		// Token: 0x0400027B RID: 635
		BecquerelMonitor.DoubleTextBox doubleTextBox5;

		// Token: 0x0400027C RID: 636
		System.Windows.Forms.Label label16;

		// Token: 0x0400027D RID: 637
		BecquerelMonitor.IntegerTextBox integerTextBox1;

		// Token: 0x0400027E RID: 638
		System.Windows.Forms.Label label31;

		// Token: 0x0400027F RID: 639
		System.Windows.Forms.TabPage tabPage5;

		// Token: 0x04000280 RID: 640
		System.Windows.Forms.Label label38;

		// Token: 0x04000281 RID: 641
		System.Windows.Forms.Label label39;

		// Token: 0x04000282 RID: 642
		System.Windows.Forms.Label label40;

		// Token: 0x04000283 RID: 643
		System.Windows.Forms.NumericUpDown numericUpDown3;

		// Token: 0x04000284 RID: 644
		System.Windows.Forms.NumericUpDown numericUpDown4;

		System.Windows.Forms.NumericUpDown numericUpDown10;

		System.Windows.Forms.NumericUpDown numericUpDown11;

		System.Windows.Forms.NumericUpDown numericUpDown12;

		System.Windows.Forms.NumericUpDown numericUpDown13;

		System.Windows.Forms.NumericUpDown numericUpDown14;

		System.Windows.Forms.NumericUpDown numericUpDown15;

        System.Windows.Forms.NumericUpDown numericUpDown16;

        // Token: 0x04000285 RID: 645
        System.Windows.Forms.NumericUpDown numericUpDown5;

		// Token: 0x04000286 RID: 646
		System.Windows.Forms.Label label42;

		// Token: 0x04000287 RID: 647
		System.Windows.Forms.NumericUpDown numericUpDown6;

		// Token: 0x04000288 RID: 648
		System.Windows.Forms.Label label41;

		// Token: 0x04000289 RID: 649
		System.Windows.Forms.Button button12;

		// Token: 0x0400028A RID: 650
		System.Windows.Forms.Label label21;

		// Token: 0x0400028B RID: 651
		BecquerelMonitor.DoubleTextBox doubleTextBox6;

		// Token: 0x0400028C RID: 652
		System.Windows.Forms.Label label1;

		// Token: 0x0400028D RID: 653
		System.Windows.Forms.ComboBox comboBox1;

		// Token: 0x0400028E RID: 654
		System.Windows.Forms.TabPage tabPage6;

        System.Windows.Forms.TabPage tabPage7;

        // Token: 0x0400028F RID: 655
        System.Windows.Forms.Label label2;

		// Token: 0x04000290 RID: 656
		System.Windows.Forms.Panel panel1;

		// Token: 0x04000291 RID: 657
		System.Windows.Forms.Button button9;

		// Token: 0x04000292 RID: 658
		System.Windows.Forms.Button button8;

		// Token: 0x04000293 RID: 659
		System.Windows.Forms.Button button1;

		// Token: 0x04000294 RID: 660
		System.Windows.Forms.Button button11;

		// Token: 0x04000295 RID: 661
		System.Windows.Forms.Label label36;

		// Token: 0x04000296 RID: 662
		System.Windows.Forms.GroupBox groupBox1;

		// Token: 0x04000297 RID: 663
		System.Windows.Forms.Label label3;

		// Token: 0x04000298 RID: 664
		System.Windows.Forms.Label label6;

		// Token: 0x04000299 RID: 665
		System.Windows.Forms.Label label7;

		// Token: 0x0400029A RID: 666
		System.Windows.Forms.TextBox numericUpDown1;

		// Token: 0x0400029B RID: 667
		System.Windows.Forms.TextBox numericUpDown2;

		// Token: 0x0400029C RID: 668
		System.Windows.Forms.TextBox numericUpDown7;

		System.Windows.Forms.TextBox numericUpDown8;

		System.Windows.Forms.TextBox numericUpDown9;

		// Token: 0x0400029D RID: 669
		System.Windows.Forms.Label label8;

		System.Windows.Forms.Label label43;

		System.Windows.Forms.Label label44;

		// Token: 0x0400029E RID: 670
		System.Windows.Forms.TextBox textBox15;

        System.Windows.Forms.TextBox textBox16;

        // Token: 0x0400029F RID: 671
        XPTable.Models.Table table2;

		// Token: 0x040002A0 RID: 672
		XPTable.Models.ColumnModel columnModel2;

		// Token: 0x040002A1 RID: 673
		XPTable.Models.TableModel tableModel2;

		// Token: 0x040002A2 RID: 674
		XPTable.Models.TextColumn textColumn3;

		// Token: 0x040002A3 RID: 675
		XPTable.Models.NumberColumn numberColumn1;

		// Token: 0x040002A4 RID: 676
		System.Windows.Forms.Label label9;

		// Token: 0x040002A5 RID: 677
		System.Windows.Forms.GroupBox groupBox3;

		// Token: 0x040002A6 RID: 678
		//System.Windows.Forms.Label label15;

		System.Windows.Forms.Label label49;

		System.Windows.Forms.Label label50;

		System.Windows.Forms.Label label51;

		//System.Windows.Forms.Label label14;
		//System.Windows.Forms.Label label13;
		//BecquerelMonitor.DoubleTextBox doubleTextBox3;
		//BecquerelMonitor.DoubleTextBox doubleTextBox2;
		//System.Windows.Forms.Label label12;
		//System.Windows.Forms.Label label11;
		//BecquerelMonitor.DoubleTextBox doubleTextBox1;
		//System.Windows.Forms.Label label10;

		// Token: 0x040002AF RID: 687
		System.Windows.Forms.GroupBox groupBox2;

		// Token: 0x040002B0 RID: 688
		XPTable.Models.NumberColumn numberColumn2;

		// Token: 0x040002B1 RID: 689
		XPTable.Models.Table table3;
        XPTable.Models.Table table4;

        // Token: 0x040002B2 RID: 690
        XPTable.Models.ColumnModel columnModel3;

        XPTable.Models.ColumnModel columnModel4;

        // Token: 0x040002B3 RID: 691
        XPTable.Models.TextColumn textColumn4;

		// Token: 0x040002B4 RID: 692
		XPTable.Models.TableModel tableModel3;

        XPTable.Models.TableModel tableModel4;

        // Token: 0x040002B5 RID: 693
        System.Windows.Forms.Label label19;

		System.Windows.Forms.Label label45;

		System.Windows.Forms.Label label46;

		System.Windows.Forms.Label label47;

		System.Windows.Forms.Label label48;

        System.Windows.Forms.Label label52;

        // Token: 0x040002B6 RID: 694
        System.Windows.Forms.Panel panel2;

		// Token: 0x040002B7 RID: 695
		System.Windows.Forms.Button button10;

		// Token: 0x040002B8 RID: 696
		System.Windows.Forms.Button button2;

		// Token: 0x040002B9 RID: 697
		XPTable.Models.NumberColumn numberColumn3;

		// Token: 0x040002BA RID: 698
		XPTable.Models.NumberColumn numberColumn4;

        XPTable.Models.NumberColumn numberColumn5;
        XPTable.Models.NumberColumn numberColumn6;
        XPTable.Models.NumberColumn numberColumn7;
        XPTable.Models.NumberColumn numberColumn8;
		System.Windows.Forms.Label effROIText;
		System.Windows.Forms.Button clearEffROI;
		System.Windows.Forms.ComboBox selectEffROI;

		// dose rate calculation
        private System.Windows.Forms.Button buttonLoadDoseRateSpectrum;
        private System.Windows.Forms.Label labelSpectrumNote;
        private System.Windows.Forms.Label labelEffNote;
        private System.Windows.Forms.Button buttonLoadEff;
        private System.Windows.Forms.Button buttonEstimateDRConf;
        private System.Windows.Forms.Label labelDoseRateValue;
        private System.Windows.Forms.NumericUpDown upDownDoseRateValue;
        private System.Windows.Forms.TextBox textBoxEffFile;
        private System.Windows.Forms.TextBox textBoxDoseRateSpectrumFile;
        private System.Windows.Forms.Label labelDREstimateTitle;
        private System.Windows.Forms.Button buttonClearDoseRate;
        private GroupBox peakSpecgroupBox;
        private ComboBox peakTypecomboBox;
        private Label peakTypelabel;
        private NumericUpDown rightSkewnumericUpDown;
        private Label rightSkewlabel;
        private NumericUpDown leftSkewnumericUpDown;
        private Label leftSkewlabel;
    }
}
