using BecquerelMonitor.Properties;
using System;

namespace BecquerelMonitor
{
	// Token: 0x02000066 RID: 102
	public partial class DeviceConfigForm : global::System.Windows.Forms.Form
	{
		// Token: 0x0600050F RID: 1295 RVA: 0x0001DFDC File Offset: 0x0001C1DC
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000510 RID: 1296 RVA: 0x0001E004 File Offset: 0x0001C204
		void InitializeComponent()
		{
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DeviceConfigForm));
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
            this.labelDREstimateTitle = new System.Windows.Forms.Label();
            this.textBoxEffFile = new System.Windows.Forms.TextBox();
            this.textBoxK40File = new System.Windows.Forms.TextBox();
            this.buttonEstimateDRConf = new System.Windows.Forms.Button();
            this.labelK40ResNote = new System.Windows.Forms.Label();
            this.upDownK40Res = new System.Windows.Forms.NumericUpDown();
            this.labelEffNote = new System.Windows.Forms.Label();
            this.buttonLoadEff = new System.Windows.Forms.Button();
            this.labelSpectrumNote = new System.Windows.Forms.Label();
            this.buttonLoadK40Spectrum = new System.Windows.Forms.Button();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.table4 = new XPTable.Models.Table();
            this.columnModel4 = new XPTable.Models.ColumnModel();
            this.numberColumn5 = new XPTable.Models.NumberColumn();
            this.numberColumn6 = new XPTable.Models.NumberColumn();
            this.numberColumn7 = new XPTable.Models.NumberColumn();
            this.numberColumn8 = new XPTable.Models.NumberColumn();
            this.tableModel4 = new XPTable.Models.TableModel();
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
            ((System.ComponentModel.ISupportInitialize)(this.upDownK40Res)).BeginInit();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.table4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).BeginInit();
            this.SuspendLayout();
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label4.Location = new System.Drawing.Point(6, 22);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(35, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "Name";
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(110, 18);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(297, 20);
            this.textBox1.TabIndex = 1;
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label5.Location = new System.Drawing.Point(12, 10);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(125, 13);
            this.label5.TabIndex = 9;
            this.label5.Text = "Device Configuration List";
            // 
            // button3
            // 
            this.button3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button3.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button3.Location = new System.Drawing.Point(165, 4);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(75, 25);
            this.button3.TabIndex = 1;
            this.button3.Text = "New";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // button4
            // 
            this.button4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button4.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button4.Location = new System.Drawing.Point(327, 4);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(75, 25);
            this.button4.TabIndex = 2;
            this.button4.Text = "Delete";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // button5
            // 
            this.button5.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button5.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button5.Location = new System.Drawing.Point(825, 636);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(75, 25);
            this.button5.TabIndex = 4;
            this.button5.Text = "Close";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // button6
            // 
            this.button6.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button6.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button6.Location = new System.Drawing.Point(744, 636);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(75, 25);
            this.button6.TabIndex = 3;
            this.button6.Text = "Save";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button13
            // 
            this.button13.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button13.Location = new System.Drawing.Point(20, 69);
            this.button13.Name = "button13";
            this.button13.Size = new System.Drawing.Size(76, 38);
            this.button13.TabIndex = 18;
            this.button13.Text = "Read from device";
            this.button13.UseVisualStyleBackColor = true;
            this.button13.Click += new System.EventHandler(this.button13_Click);
            // 
            // button14
            // 
            this.button14.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button14.Location = new System.Drawing.Point(130, 69);
            this.button14.Name = "button14";
            this.button14.Size = new System.Drawing.Size(76, 38);
            this.button14.TabIndex = 18;
            this.button14.Text = "Write to device";
            this.button14.UseVisualStyleBackColor = true;
            this.button14.Click += new System.EventHandler(this.button14_Click);
            // 
            // button15
            // 
            this.button15.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button15.Location = new System.Drawing.Point(6, 359);
            this.button15.Name = "button15";
            this.button15.Size = new System.Drawing.Size(66, 25);
            this.button15.TabIndex = 50;
            this.button15.Text = "New";
            this.button15.UseVisualStyleBackColor = true;
            this.button15.Click += new System.EventHandler(this.button15_Click);
            // 
            // button16
            // 
            this.button16.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button16.Location = new System.Drawing.Point(78, 359);
            this.button16.Name = "button16";
            this.button16.Size = new System.Drawing.Size(66, 25);
            this.button16.TabIndex = 50;
            this.button16.Text = "Delete";
            this.button16.UseVisualStyleBackColor = true;
            this.button16.Click += new System.EventHandler(this.button16_Click);
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label18.Location = new System.Drawing.Point(6, 228);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(73, 13);
            this.label18.TabIndex = 31;
            this.label18.Text = "# of Channels";
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Controls.Add(this.tabPage6);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage5);
            this.tabControl1.Controls.Add(this.tabPage4);
            this.tabControl1.Controls.Add(this.tabPage7);
            this.tabControl1.Location = new System.Drawing.Point(427, 4);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(479, 625);
            this.tabControl1.TabIndex = 37;
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
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(471, 599);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "General";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label2.Location = new System.Drawing.Point(191, 255);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(140, 13);
            this.label2.TabIndex = 93;
            this.label2.Text = "(1 for external MCA devices)";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label1.Location = new System.Drawing.Point(6, 104);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(96, 13);
            this.label1.TabIndex = 92;
            this.label1.Text = "Thermometer Type";
            // 
            // comboBox1
            // 
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Location = new System.Drawing.Point(111, 101);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(231, 21);
            this.comboBox1.TabIndex = 91;
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // label21
            // 
            this.label21.AutoSize = true;
            this.label21.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label21.Location = new System.Drawing.Point(6, 255);
            this.label21.Name = "label21";
            this.label21.Size = new System.Drawing.Size(73, 13);
            this.label21.TabIndex = 90;
            this.label21.Text = "Channel Pitch";
            // 
            // doubleTextBox6
            // 
            this.doubleTextBox6.Location = new System.Drawing.Point(110, 251);
            this.doubleTextBox6.Name = "doubleTextBox6";
            this.doubleTextBox6.Size = new System.Drawing.Size(75, 20);
            this.doubleTextBox6.TabIndex = 89;
            this.doubleTextBox6.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.doubleTextBox6.TextChanged += new System.EventHandler(this.doubleTextBox6_TextChanged);
            // 
            // integerTextBox1
            // 
            this.integerTextBox1.Location = new System.Drawing.Point(110, 224);
            this.integerTextBox1.Name = "integerTextBox1";
            this.integerTextBox1.Size = new System.Drawing.Size(75, 20);
            this.integerTextBox1.TabIndex = 51;
            this.integerTextBox1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.integerTextBox1.TextChanged += new System.EventHandler(this.integerTextBox1_TextChanged);
            // 
            // doubleTextBox5
            // 
            this.doubleTextBox5.Location = new System.Drawing.Point(109, 177);
            this.doubleTextBox5.Name = "doubleTextBox5";
            this.doubleTextBox5.Size = new System.Drawing.Size(75, 20);
            this.doubleTextBox5.TabIndex = 7;
            this.doubleTextBox5.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.doubleTextBox5.TextChanged += new System.EventHandler(this.doubleTextBox5_TextChanged);
            // 
            // label28
            // 
            this.label28.AutoSize = true;
            this.label28.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label28.Location = new System.Drawing.Point(190, 180);
            this.label28.Name = "label28";
            this.label28.Size = new System.Drawing.Size(27, 13);
            this.label28.TabIndex = 44;
            this.label28.Text = "sec.";
            // 
            // label27
            // 
            this.label27.AutoSize = true;
            this.label27.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label27.Location = new System.Drawing.Point(6, 180);
            this.label27.Name = "label27";
            this.label27.Size = new System.Drawing.Size(63, 13);
            this.label27.TabIndex = 42;
            this.label27.Text = "Preset Time";
            // 
            // label26
            // 
            this.label26.AutoSize = true;
            this.label26.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label26.Location = new System.Drawing.Point(2, 308);
            this.label26.Name = "label26";
            this.label26.Size = new System.Drawing.Size(35, 13);
            this.label26.TabIndex = 41;
            this.label26.Text = "Notes";
            // 
            // textBox19
            // 
            this.textBox19.Location = new System.Drawing.Point(110, 304);
            this.textBox19.Multiline = true;
            this.textBox19.Name = "textBox19";
            this.textBox19.Size = new System.Drawing.Size(343, 129);
            this.textBox19.TabIndex = 10;
            this.textBox19.TextChanged += new System.EventHandler(this.textBox19_TextChanged);
            // 
            // comboBox4
            // 
            this.comboBox4.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox4.FormattingEnabled = true;
            this.comboBox4.Location = new System.Drawing.Point(111, 73);
            this.comboBox4.Name = "comboBox4";
            this.comboBox4.Size = new System.Drawing.Size(231, 21);
            this.comboBox4.TabIndex = 9;
            this.comboBox4.SelectedIndexChanged += new System.EventHandler(this.comboBox4_SelectedIndexChanged);
            // 
            // label25
            // 
            this.label25.AutoSize = true;
            this.label25.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label25.Location = new System.Drawing.Point(6, 76);
            this.label25.Name = "label25";
            this.label25.Size = new System.Drawing.Size(95, 13);
            this.label25.TabIndex = 38;
            this.label25.Text = "Input Device Type";
            // 
            // textBox18
            // 
            this.textBox18.Location = new System.Drawing.Point(110, 46);
            this.textBox18.Name = "textBox18";
            this.textBox18.ReadOnly = true;
            this.textBox18.Size = new System.Drawing.Size(297, 20);
            this.textBox18.TabIndex = 37;
            this.textBox18.TabStop = false;
            // 
            // label24
            // 
            this.label24.AutoSize = true;
            this.label24.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label24.Location = new System.Drawing.Point(6, 49);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(49, 13);
            this.label24.TabIndex = 36;
            this.label24.Text = "Filename";
            // 
            // tabPage3
            // 
            this.tabPage3.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage3.Location = new System.Drawing.Point(4, 22);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage3.Size = new System.Drawing.Size(471, 599);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "Device Specific";
            // 
            // tabPage6
            // 
            this.tabPage6.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage6.Location = new System.Drawing.Point(4, 22);
            this.tabPage6.Name = "tabPage6";
            this.tabPage6.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage6.Size = new System.Drawing.Size(471, 599);
            this.tabPage6.TabIndex = 5;
            this.tabPage6.Text = "Tempco";
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
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(471, 599);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Energy Calibration";
            // 
            // panel2
            // 
            this.panel2.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panel2.Controls.Add(this.button10);
            this.panel2.Controls.Add(this.button2);
            this.panel2.Controls.Add(this.table3);
            this.panel2.Location = new System.Drawing.Point(8, 430);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(448, 139);
            this.panel2.TabIndex = 50;
            // 
            // button10
            // 
            this.button10.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button10.Location = new System.Drawing.Point(11, 3);
            this.button10.Name = "button10";
            this.button10.Size = new System.Drawing.Size(66, 25);
            this.button10.TabIndex = 50;
            this.button10.Text = "New";
            this.button10.UseVisualStyleBackColor = true;
            this.button10.Click += new System.EventHandler(this.button10_Click);
            // 
            // button2
            // 
            this.button2.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button2.Location = new System.Drawing.Point(251, 3);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(66, 25);
            this.button2.TabIndex = 18;
            this.button2.Text = "Remove";
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
            this.table3.Location = new System.Drawing.Point(11, 35);
            this.table3.Name = "table3";
            this.table3.NoItemsText = "There are no settings";
            this.table3.Size = new System.Drawing.Size(306, 83);
            this.table3.TabIndex = 49;
            this.table3.TableModel = this.tableModel3;
            this.table3.Text = "table3";
            this.table3.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table3.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table3_EditingStopped);
            // 
            // columnModel3
            // 
            this.columnModel3.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn4,
            this.numberColumn3,
            this.numberColumn4});
            // 
            // textColumn4
            // 
            this.textColumn4.IsTextTrimmed = false;
            this.textColumn4.Sortable = false;
            this.textColumn4.Text = "Nuclide";
            this.textColumn4.ToolTipText = "";
            // 
            // numberColumn3
            // 
            this.numberColumn3.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.numberColumn3.IsTextTrimmed = false;
            this.numberColumn3.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.numberColumn3.Sortable = false;
            this.numberColumn3.Text = "Energy";
            this.numberColumn3.ToolTipText = "";
            // 
            // numberColumn4
            // 
            this.numberColumn4.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.numberColumn4.IsTextTrimmed = false;
            this.numberColumn4.Sortable = false;
            this.numberColumn4.Text = "Error%";
            this.numberColumn4.ToolTipText = "";
            // 
            // label19
            // 
            this.label19.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label19.AutoSize = true;
            this.label19.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label19.Location = new System.Drawing.Point(6, 414);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(114, 13);
            this.label19.TabIndex = 48;
            this.label19.Text = "AutoCalibrator Settings";
            // 
            // label9
            // 
            this.label9.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label9.AutoSize = true;
            this.label9.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label9.Location = new System.Drawing.Point(6, 198);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(104, 13);
            this.label9.TabIndex = 46;
            this.label9.Text = "Multipoint Calibration";
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panel1.Controls.Add(this.table2);
            this.panel1.Controls.Add(this.button9);
            this.panel1.Controls.Add(this.button8);
            this.panel1.Controls.Add(this.button1);
            this.panel1.Controls.Add(this.button11);
            this.panel1.Controls.Add(this.label36);
            this.panel1.Location = new System.Drawing.Point(8, 221);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(448, 166);
            this.panel1.TabIndex = 45;
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
            this.table2.Location = new System.Drawing.Point(11, 37);
            this.table2.Name = "table2";
            this.table2.NoItemsText = "There are no calibration points";
            this.table2.Size = new System.Drawing.Size(306, 89);
            this.table2.TabIndex = 17;
            this.table2.TableModel = this.tableModel2;
            this.table2.Text = "table2";
            this.table2.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table2.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table2_EditingStopped);
            this.table2.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table2_SelectionChanged);
            // 
            // columnModel2
            // 
            this.columnModel2.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn3,
            this.numberColumn2,
            this.numberColumn1});
            // 
            // textColumn3
            // 
            this.textColumn3.Editable = false;
            this.textColumn3.IsTextTrimmed = false;
            this.textColumn3.Sortable = false;
            this.textColumn3.Text = "#";
            this.textColumn3.ToolTipText = "";
            this.textColumn3.Width = 32;
            // 
            // numberColumn2
            // 
            this.numberColumn2.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.numberColumn2.IsTextTrimmed = false;
            this.numberColumn2.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.numberColumn2.Sortable = false;
            this.numberColumn2.Text = "Channel";
            this.numberColumn2.ToolTipText = "";
            // 
            // numberColumn1
            // 
            this.numberColumn1.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.numberColumn1.IsTextTrimmed = false;
            this.numberColumn1.Minimum = new decimal(new int[] {
            10000,
            0,
            0,
            -2147483648});
            this.numberColumn1.Sortable = false;
            this.numberColumn1.Text = "Energy";
            this.numberColumn1.ToolTipText = "";
            // 
            // button9
            // 
            this.button9.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button9.Location = new System.Drawing.Point(251, 5);
            this.button9.Name = "button9";
            this.button9.Size = new System.Drawing.Size(66, 25);
            this.button9.TabIndex = 16;
            this.button9.Text = "Remove";
            this.button9.UseVisualStyleBackColor = true;
            this.button9.Click += new System.EventHandler(this.button9_Click);
            // 
            // button8
            // 
            this.button8.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button8.Location = new System.Drawing.Point(11, 5);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(100, 25);
            this.button8.TabIndex = 15;
            this.button8.Text = "Pick up channel";
            this.button8.UseVisualStyleBackColor = true;
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button1.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button1.Location = new System.Drawing.Point(323, 60);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(76, 38);
            this.button1.TabIndex = 13;
            this.button1.Text = "Execute Calibration";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button11
            // 
            this.button11.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button11.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button11.Location = new System.Drawing.Point(241, 130);
            this.button11.Name = "button11";
            this.button11.Size = new System.Drawing.Size(76, 25);
            this.button11.TabIndex = 11;
            this.button11.Text = "Cancel";
            this.button11.UseVisualStyleBackColor = true;
            this.button11.Click += new System.EventHandler(this.button11_Click);
            // 
            // label36
            // 
            this.label36.AutoSize = true;
            this.label36.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label36.Location = new System.Drawing.Point(9, 135);
            this.label36.Name = "label36";
            this.label36.Size = new System.Drawing.Size(192, 13);
            this.label36.TabIndex = 10;
            this.label36.Text = "Pick up a channel from spectrum chart.";
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
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
            this.groupBox1.Location = new System.Drawing.Point(6, 75);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(450, 108);
            this.groupBox1.TabIndex = 44;
            this.groupBox1.TabStop = false;
            // 
            // label43
            // 
            this.label43.AutoSize = true;
            this.label43.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label43.Location = new System.Drawing.Point(5, 16);
            this.label43.Name = "label43";
            this.label43.Size = new System.Drawing.Size(65, 13);
            this.label43.TabIndex = 20;
            this.label43.Text = "4th Coeff.(a)";
            // 
            // label44
            // 
            this.label44.AutoSize = true;
            this.label44.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label44.Location = new System.Drawing.Point(5, 48);
            this.label44.Name = "label44";
            this.label44.Size = new System.Drawing.Size(65, 13);
            this.label44.TabIndex = 20;
            this.label44.Text = "3rd Coeff.(b)";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label3.Location = new System.Drawing.Point(228, 16);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(68, 13);
            this.label3.TabIndex = 20;
            this.label3.Text = "2nd Coeff.(c)";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label6.Location = new System.Drawing.Point(228, 79);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(50, 13);
            this.label6.TabIndex = 15;
            this.label6.Text = "Offset (e)";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label7.Location = new System.Drawing.Point(228, 48);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(64, 13);
            this.label7.TabIndex = 14;
            this.label7.Text = "1st Coeff.(d)";
            // 
            // numericUpDown8
            // 
            this.numericUpDown8.Location = new System.Drawing.Point(75, 14);
            this.numericUpDown8.Name = "numericUpDown8";
            this.numericUpDown8.Size = new System.Drawing.Size(140, 20);
            this.numericUpDown8.TabIndex = 12;
            this.numericUpDown8.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown8_KeyDown);
            this.numericUpDown8.Leave += new System.EventHandler(this.numericUpDown8_Leave);
            // 
            // numericUpDown9
            // 
            this.numericUpDown9.Location = new System.Drawing.Point(75, 46);
            this.numericUpDown9.Name = "numericUpDown9";
            this.numericUpDown9.Size = new System.Drawing.Size(140, 20);
            this.numericUpDown9.TabIndex = 12;
            this.numericUpDown9.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown9_KeyDown);
            this.numericUpDown9.Leave += new System.EventHandler(this.numericUpDown9_Leave);
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(300, 14);
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(140, 20);
            this.numericUpDown1.TabIndex = 12;
            this.numericUpDown1.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown1_KeyDown);
            this.numericUpDown1.Leave += new System.EventHandler(this.numericUpDown1_Leave);
            // 
            // numericUpDown2
            // 
            this.numericUpDown2.Location = new System.Drawing.Point(300, 46);
            this.numericUpDown2.Name = "numericUpDown2";
            this.numericUpDown2.Size = new System.Drawing.Size(140, 20);
            this.numericUpDown2.TabIndex = 13;
            this.numericUpDown2.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown2_KeyDown);
            this.numericUpDown2.Leave += new System.EventHandler(this.numericUpDown2_Leave);
            // 
            // numericUpDown7
            // 
            this.numericUpDown7.Location = new System.Drawing.Point(300, 77);
            this.numericUpDown7.Name = "numericUpDown7";
            this.numericUpDown7.Size = new System.Drawing.Size(140, 20);
            this.numericUpDown7.TabIndex = 18;
            this.numericUpDown7.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown7_KeyDown);
            this.numericUpDown7.Leave += new System.EventHandler(this.numericUpDown7_Leave);
            // 
            // label8
            // 
            this.label8.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label8.AutoSize = true;
            this.label8.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label8.Location = new System.Drawing.Point(6, 17);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(386, 13);
            this.label8.TabIndex = 43;
            this.label8.Text = "Determine energy calibration parameters by fitting energy values of known peaks";
            // 
            // textBox15
            // 
            this.textBox15.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox15.BackColor = System.Drawing.SystemColors.ControlLight;
            this.textBox15.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox15.Location = new System.Drawing.Point(8, 39);
            this.textBox15.Multiline = true;
            this.textBox15.Name = "textBox15";
            this.textBox15.ReadOnly = true;
            this.textBox15.Size = new System.Drawing.Size(448, 17);
            this.textBox15.TabIndex = 40;
            this.textBox15.TabStop = false;
            this.textBox15.Text = "Energy[keV] = ax^4 + bx^3 + cx^2 + dx + e (x = Channel Number)";
            this.textBox15.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // textBox16
            // 
            this.textBox16.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox16.BackColor = System.Drawing.SystemColors.ControlLight;
            this.textBox16.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox16.Location = new System.Drawing.Point(8, 61);
            this.textBox16.Multiline = true;
            this.textBox16.Name = "textBox16";
            this.textBox16.ReadOnly = true;
            this.textBox16.Size = new System.Drawing.Size(448, 17);
            this.textBox16.TabIndex = 40;
            this.textBox16.TabStop = false;
            this.textBox16.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // tabPage5
            // 
            this.tabPage5.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage5.Controls.Add(this.groupBox2);
            this.tabPage5.Controls.Add(this.label49);
            this.tabPage5.Location = new System.Drawing.Point(4, 22);
            this.tabPage5.Name = "tabPage5";
            this.tabPage5.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage5.Size = new System.Drawing.Size(471, 599);
            this.tabPage5.TabIndex = 4;
            this.tabPage5.Text = "Analysis";
            // 
            // groupBox2
            // 
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
            this.groupBox2.Location = new System.Drawing.Point(14, 178);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(441, 206);
            this.groupBox2.TabIndex = 19;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Peak Detection";
            // 
            // label40
            // 
            this.label40.AutoSize = true;
            this.label40.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label40.Location = new System.Drawing.Point(18, 35);
            this.label40.Name = "label40";
            this.label40.Size = new System.Drawing.Size(50, 13);
            this.label40.TabIndex = 10;
            this.label40.Text = "Min SNR";
            // 
            // label42
            // 
            this.label42.AutoSize = true;
            this.label42.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label42.Location = new System.Drawing.Point(192, 116);
            this.label42.Name = "label42";
            this.label42.Size = new System.Drawing.Size(15, 13);
            this.label42.TabIndex = 18;
            this.label42.Text = "%";
            // 
            // numericUpDown4
            // 
            this.numericUpDown4.Location = new System.Drawing.Point(118, 32);
            this.numericUpDown4.Name = "numericUpDown4";
            this.numericUpDown4.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown4.TabIndex = 7;
            this.numericUpDown4.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown4.ValueChanged += new System.EventHandler(this.numericUpDown4_ValueChanged);
            // 
            // numericUpDown6
            // 
            this.numericUpDown6.DecimalPlaces = 1;
            this.numericUpDown6.Location = new System.Drawing.Point(118, 114);
            this.numericUpDown6.Name = "numericUpDown6";
            this.numericUpDown6.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown6.TabIndex = 17;
            this.numericUpDown6.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown6.ValueChanged += new System.EventHandler(this.numericUpDown6_ValueChanged);
            // 
            // numericUpDown3
            // 
            this.numericUpDown3.Location = new System.Drawing.Point(118, 60);
            this.numericUpDown3.Name = "numericUpDown3";
            this.numericUpDown3.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown3.TabIndex = 8;
            this.numericUpDown3.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown3.ValueChanged += new System.EventHandler(this.numericUpDown3_ValueChanged);
            // 
            // label41
            // 
            this.label41.AutoSize = true;
            this.label41.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label41.Location = new System.Drawing.Point(18, 116);
            this.label41.Name = "label41";
            this.label41.Size = new System.Drawing.Size(55, 13);
            this.label41.TabIndex = 16;
            this.label41.Text = "Tolerance";
            // 
            // label39
            // 
            this.label39.AutoSize = true;
            this.label39.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label39.Location = new System.Drawing.Point(18, 62);
            this.label39.Name = "label39";
            this.label39.Size = new System.Drawing.Size(55, 13);
            this.label39.TabIndex = 11;
            this.label39.Text = "Max Items";
            // 
            // numericUpDown5
            // 
            this.numericUpDown5.Location = new System.Drawing.Point(118, 87);
            this.numericUpDown5.Name = "numericUpDown5";
            this.numericUpDown5.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown5.TabIndex = 14;
            this.numericUpDown5.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown5.ValueChanged += new System.EventHandler(this.numericUpDown5_ValueChanged);
            // 
            // label38
            // 
            this.label38.AutoSize = true;
            this.label38.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label38.Location = new System.Drawing.Point(18, 89);
            this.label38.Name = "label38";
            this.label38.Size = new System.Drawing.Size(77, 13);
            this.label38.TabIndex = 12;
            this.label38.Text = "FWHM at 0 ch";
            // 
            // label45
            // 
            this.label45.AutoSize = true;
            this.label45.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label45.Location = new System.Drawing.Point(208, 35);
            this.label45.Name = "label45";
            this.label45.Size = new System.Drawing.Size(94, 13);
            this.label45.TabIndex = 10;
            this.label45.Text = "FWHM peak in ch";
            // 
            // label46
            // 
            this.label46.AutoSize = true;
            this.label46.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label46.Location = new System.Drawing.Point(208, 62);
            this.label46.Name = "label46";
            this.label46.Size = new System.Drawing.Size(94, 13);
            this.label46.TabIndex = 10;
            this.label46.Text = "FW peak width ch";
            // 
            // label47
            // 
            this.label47.AutoSize = true;
            this.label47.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label47.Location = new System.Drawing.Point(208, 89);
            this.label47.Name = "label47";
            this.label47.Size = new System.Drawing.Size(76, 13);
            this.label47.TabIndex = 10;
            this.label47.Text = "Min range keV";
            // 
            // label48
            // 
            this.label48.AutoSize = true;
            this.label48.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label48.Location = new System.Drawing.Point(208, 116);
            this.label48.Name = "label48";
            this.label48.Size = new System.Drawing.Size(79, 13);
            this.label48.TabIndex = 10;
            this.label48.Text = "Max range keV";
            // 
            // label50
            // 
            this.label50.AutoSize = true;
            this.label50.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label50.Location = new System.Drawing.Point(18, 142);
            this.label50.Name = "label50";
            this.label50.Size = new System.Drawing.Size(82, 13);
            this.label50.TabIndex = 16;
            this.label50.Text = "Min Δ FWHM %";
            // 
            // label51
            // 
            this.label51.AutoSize = true;
            this.label51.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label51.Location = new System.Drawing.Point(18, 168);
            this.label51.Name = "label51";
            this.label51.Size = new System.Drawing.Size(85, 13);
            this.label51.TabIndex = 16;
            this.label51.Text = "Max Δ FWHM %";
            // 
            // label52
            // 
            this.label52.AutoSize = true;
            this.label52.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label52.Location = new System.Drawing.Point(208, 143);
            this.label52.Name = "label52";
            this.label52.Size = new System.Drawing.Size(105, 13);
            this.label52.TabIndex = 10;
            this.label52.Text = "Concat channels, ch";
            // 
            // numericUpDown10
            // 
            this.numericUpDown10.Location = new System.Drawing.Point(330, 32);
            this.numericUpDown10.Name = "numericUpDown10";
            this.numericUpDown10.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown10.TabIndex = 7;
            this.numericUpDown10.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown10.ValueChanged += new System.EventHandler(this.numericUpDown10_ValueChanged);
            // 
            // numericUpDown11
            // 
            this.numericUpDown11.Location = new System.Drawing.Point(330, 60);
            this.numericUpDown11.Name = "numericUpDown11";
            this.numericUpDown11.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown11.TabIndex = 7;
            this.numericUpDown11.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown11.ValueChanged += new System.EventHandler(this.numericUpDown11_ValueChanged);
            // 
            // numericUpDown12
            // 
            this.numericUpDown12.Location = new System.Drawing.Point(330, 87);
            this.numericUpDown12.Name = "numericUpDown12";
            this.numericUpDown12.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown12.TabIndex = 7;
            this.numericUpDown12.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown12.ValueChanged += new System.EventHandler(this.numericUpDown12_ValueChanged);
            // 
            // numericUpDown13
            // 
            this.numericUpDown13.Location = new System.Drawing.Point(330, 114);
            this.numericUpDown13.Name = "numericUpDown13";
            this.numericUpDown13.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown13.TabIndex = 7;
            this.numericUpDown13.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown13.ValueChanged += new System.EventHandler(this.numericUpDown13_ValueChanged);
            // 
            // numericUpDown14
            // 
            this.numericUpDown14.Location = new System.Drawing.Point(118, 141);
            this.numericUpDown14.Name = "numericUpDown14";
            this.numericUpDown14.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown14.TabIndex = 7;
            this.numericUpDown14.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown14.ValueChanged += new System.EventHandler(this.numericUpDown14_ValueChanged);
            // 
            // numericUpDown15
            // 
            this.numericUpDown15.Location = new System.Drawing.Point(118, 168);
            this.numericUpDown15.Name = "numericUpDown15";
            this.numericUpDown15.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown15.TabIndex = 7;
            this.numericUpDown15.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown15.ValueChanged += new System.EventHandler(this.numericUpDown15_ValueChanged);
            // 
            // numericUpDown16
            // 
            this.numericUpDown16.Location = new System.Drawing.Point(330, 141);
            this.numericUpDown16.Name = "numericUpDown16";
            this.numericUpDown16.Size = new System.Drawing.Size(73, 20);
            this.numericUpDown16.TabIndex = 7;
            this.numericUpDown16.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDown16.ValueChanged += new System.EventHandler(this.numericUpDown16_ValueChanged);
            // 
            // label49
            // 
            this.label49.AutoSize = true;
            this.label49.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label49.Location = new System.Drawing.Point(18, 250);
            this.label49.Name = "label49";
            this.label49.Size = new System.Drawing.Size(390, 312);
            this.label49.TabIndex = 10;
            this.label49.Text = resources.GetString("label49.Text");
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
            this.tabPage4.Location = new System.Drawing.Point(4, 22);
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage4.Size = new System.Drawing.Size(471, 599);
            this.tabPage4.TabIndex = 3;
            this.tabPage4.Text = "References";
            // 
            // label31
            // 
            this.label31.AutoSize = true;
            this.label31.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label31.Location = new System.Drawing.Point(15, 42);
            this.label31.Name = "label31";
            this.label31.Size = new System.Drawing.Size(186, 13);
            this.label31.TabIndex = 26;
            this.label31.Text = "(must have same channel parameters)";
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label16.Location = new System.Drawing.Point(15, 20);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(188, 13);
            this.label16.TabIndex = 25;
            this.label16.Text = "Specify Default Background Spectrum";
            // 
            // button7
            // 
            this.button7.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button7.Location = new System.Drawing.Point(346, 74);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(60, 25);
            this.button7.TabIndex = 24;
            this.button7.Text = "Select...";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // label23
            // 
            this.label23.AutoSize = true;
            this.label23.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label23.Location = new System.Drawing.Point(15, 79);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(113, 13);
            this.label23.TabIndex = 1;
            this.label23.Text = "Background Spectrum";
            // 
            // textBox17
            // 
            this.textBox17.Location = new System.Drawing.Point(138, 76);
            this.textBox17.Name = "textBox17";
            this.textBox17.Size = new System.Drawing.Size(202, 20);
            this.textBox17.TabIndex = 23;
            this.textBox17.TextChanged += new System.EventHandler(this.textBox17_TextChanged);
            // 
            // effROIText
            // 
            this.effROIText.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.effROIText.Location = new System.Drawing.Point(15, 122);
            this.effROIText.Name = "effROIText";
            this.effROIText.Size = new System.Drawing.Size(448, 17);
            this.effROIText.TabIndex = 40;
            this.effROIText.Text = "Select ROI with efficiency curve data:";
            // 
            // selectEffROI
            // 
            this.selectEffROI.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.selectEffROI.FormattingEnabled = true;
            this.selectEffROI.Location = new System.Drawing.Point(30, 144);
            this.selectEffROI.Name = "selectEffROI";
            this.selectEffROI.Size = new System.Drawing.Size(288, 21);
            this.selectEffROI.TabIndex = 9;
            this.selectEffROI.SelectedIndexChanged += new System.EventHandler(this.selectEffROI_SelectedIndexChanged);
            // 
            // clearEffROI
            // 
            this.clearEffROI.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.clearEffROI.Location = new System.Drawing.Point(328, 142);
            this.clearEffROI.Name = "clearEffROI";
            this.clearEffROI.Size = new System.Drawing.Size(65, 25);
            this.clearEffROI.TabIndex = 50;
            this.clearEffROI.Text = "Clear";
            this.clearEffROI.UseVisualStyleBackColor = true;
            this.clearEffROI.Click += new System.EventHandler(this.clearEffROI_Click);
            // 
            // tabPage7
            // 
            this.tabPage7.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage7.Controls.Add(this.labelDREstimateTitle);
            this.tabPage7.Controls.Add(this.textBoxEffFile);
            this.tabPage7.Controls.Add(this.textBoxK40File);
            this.tabPage7.Controls.Add(this.buttonEstimateDRConf);
            this.tabPage7.Controls.Add(this.labelK40ResNote);
            this.tabPage7.Controls.Add(this.upDownK40Res);
            this.tabPage7.Controls.Add(this.labelEffNote);
            this.tabPage7.Controls.Add(this.buttonLoadEff);
            this.tabPage7.Controls.Add(this.labelSpectrumNote);
            this.tabPage7.Controls.Add(this.buttonLoadK40Spectrum);
            this.tabPage7.Controls.Add(this.groupBox3);
            this.tabPage7.Location = new System.Drawing.Point(4, 22);
            this.tabPage7.Name = "tabPage7";
            this.tabPage7.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage7.Size = new System.Drawing.Size(471, 599);
            this.tabPage7.TabIndex = 5;
            this.tabPage7.Text = "Dose Rate";
            // 
            // labelDREstimateTitle
            // 
            this.labelDREstimateTitle.AutoSize = true;
            this.labelDREstimateTitle.Location = new System.Drawing.Point(18, 419);
            this.labelDREstimateTitle.Name = "labelDREstimateTitle";
            this.labelDREstimateTitle.Size = new System.Drawing.Size(415, 13);
            this.labelDREstimateTitle.TabIndex = 30;
            this.labelDREstimateTitle.Text = "Experimental: predict doserate curve based on LSRM model and K40 sample spectrum";
            // 
            // textBoxEffFile
            // 
            this.textBoxEffFile.Location = new System.Drawing.Point(195, 481);
            this.textBoxEffFile.Name = "textBoxEffFile";
            this.textBoxEffFile.ReadOnly = true;
            this.textBoxEffFile.Size = new System.Drawing.Size(260, 20);
            this.textBoxEffFile.TabIndex = 29;
            this.textBoxEffFile.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // textBoxK40File
            // 
            this.textBoxK40File.Location = new System.Drawing.Point(196, 439);
            this.textBoxK40File.Name = "textBoxK40File";
            this.textBoxK40File.ReadOnly = true;
            this.textBoxK40File.Size = new System.Drawing.Size(259, 20);
            this.textBoxK40File.TabIndex = 28;
            this.textBoxK40File.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // buttonEstimateDRConf
            // 
            this.buttonEstimateDRConf.Enabled = false;
            this.buttonEstimateDRConf.Location = new System.Drawing.Point(20, 547);
            this.buttonEstimateDRConf.Name = "buttonEstimateDRConf";
            this.buttonEstimateDRConf.Size = new System.Drawing.Size(169, 23);
            this.buttonEstimateDRConf.TabIndex = 27;
            this.buttonEstimateDRConf.Text = "Estimate Dose Rate Config";
            this.buttonEstimateDRConf.UseVisualStyleBackColor = true;
            this.buttonEstimateDRConf.Click += new System.EventHandler(this.buttonEstimateDRConf_Click);
            // 
            // labelK40ResNote
            // 
            this.labelK40ResNote.AutoSize = true;
            this.labelK40ResNote.Location = new System.Drawing.Point(65, 523);
            this.labelK40ResNote.Name = "labelK40ResNote";
            this.labelK40ResNote.Size = new System.Drawing.Size(124, 13);
            this.labelK40ResNote.TabIndex = 26;
            this.labelK40ResNote.Text = "% resolution at 1460 keV";
            // 
            // upDownK40Res
            // 
            this.upDownK40Res.DecimalPlaces = 1;
            this.upDownK40Res.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.upDownK40Res.Location = new System.Drawing.Point(20, 521);
            this.upDownK40Res.Name = "upDownK40Res";
            this.upDownK40Res.Size = new System.Drawing.Size(39, 20);
            this.upDownK40Res.TabIndex = 25;
            this.upDownK40Res.Value = new decimal(new int[] {
            55,
            0,
            0,
            65536});
            // 
            // labelEffNote
            // 
            this.labelEffNote.AutoSize = true;
            this.labelEffNote.Location = new System.Drawing.Point(17, 505);
            this.labelEffNote.Name = "labelEffNote";
            this.labelEffNote.Size = new System.Drawing.Size(378, 13);
            this.labelEffNote.TabIndex = 24;
            this.labelEffNote.Text = "*point source 20 cm away from detector center (LSRM effcalc, 40 keV - 3MeV)";
            // 
            // buttonLoadEff
            // 
            this.buttonLoadEff.Location = new System.Drawing.Point(20, 479);
            this.buttonLoadEff.Name = "buttonLoadEff";
            this.buttonLoadEff.Size = new System.Drawing.Size(169, 23);
            this.buttonLoadEff.TabIndex = 23;
            this.buttonLoadEff.Text = "Load Efficiency Data*";
            this.buttonLoadEff.UseVisualStyleBackColor = true;
            this.buttonLoadEff.Click += new System.EventHandler(this.buttonLoadEff_Click);
            // 
            // labelSpectrumNote
            // 
            this.labelSpectrumNote.AutoSize = true;
            this.labelSpectrumNote.Location = new System.Drawing.Point(17, 463);
            this.labelSpectrumNote.Name = "labelSpectrumNote";
            this.labelSpectrumNote.Size = new System.Drawing.Size(131, 13);
            this.labelSpectrumNote.TabIndex = 22;
            this.labelSpectrumNote.Text = "*must contain background";
            // 
            // buttonLoadK40Spectrum
            // 
            this.buttonLoadK40Spectrum.Location = new System.Drawing.Point(20, 437);
            this.buttonLoadK40Spectrum.Name = "buttonLoadK40Spectrum";
            this.buttonLoadK40Spectrum.Size = new System.Drawing.Size(170, 23);
            this.buttonLoadK40Spectrum.TabIndex = 21;
            this.buttonLoadK40Spectrum.Text = "Load K40 Spectrum*";
            this.buttonLoadK40Spectrum.UseVisualStyleBackColor = true;
            this.buttonLoadK40Spectrum.Click += new System.EventHandler(this.buttonLoadK40Spectrum_Click);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.table4);
            this.groupBox3.Controls.Add(this.button15);
            this.groupBox3.Controls.Add(this.button16);
            this.groupBox3.Location = new System.Drawing.Point(14, 26);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(441, 390);
            this.groupBox3.TabIndex = 20;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Dose Rate";
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
            this.table4.Location = new System.Drawing.Point(5, 16);
            this.table4.Name = "table4";
            this.table4.NoItemsText = "No calibration points";
            this.table4.Size = new System.Drawing.Size(420, 337);
            this.table4.TabIndex = 0;
            this.table4.TableModel = this.tableModel4;
            this.table4.Text = "table4";
            this.table4.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table4.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table4_EditingStopped);
            // 
            // columnModel4
            // 
            this.columnModel4.Columns.AddRange(new XPTable.Models.Column[] {
            this.numberColumn5,
            this.numberColumn6,
            this.numberColumn7,
            this.numberColumn8});
            // 
            // numberColumn5
            // 
            this.numberColumn5.Alignment = XPTable.Models.ColumnAlignment.Center;
            this.numberColumn5.IsTextTrimmed = false;
            this.numberColumn5.Maximum = new decimal(new int[] {
            2999,
            0,
            0,
            0});
            this.numberColumn5.Sortable = false;
            this.numberColumn5.Text = "Lower bound, keV";
            this.numberColumn5.ToolTipText = "";
            this.numberColumn5.Width = 100;
            // 
            // numberColumn6
            // 
            this.numberColumn6.Alignment = XPTable.Models.ColumnAlignment.Center;
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
            this.numberColumn6.Text = "Upper bound, keV";
            this.numberColumn6.ToolTipText = "";
            this.numberColumn6.Width = 100;
            // 
            // numberColumn7
            // 
            this.numberColumn7.Alignment = XPTable.Models.ColumnAlignment.Center;
            this.numberColumn7.IsTextTrimmed = false;
            this.numberColumn7.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.numberColumn7.Sortable = false;
            this.numberColumn7.Text = "CPS";
            this.numberColumn7.ToolTipText = "";
            this.numberColumn7.Width = 60;
            // 
            // numberColumn8
            // 
            this.numberColumn8.Alignment = XPTable.Models.ColumnAlignment.Center;
            this.numberColumn8.IsTextTrimmed = false;
            this.numberColumn8.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.numberColumn8.Sortable = false;
            this.numberColumn8.Text = "Reference Dose Rate, μSv/h";
            this.numberColumn8.ToolTipText = "";
            this.numberColumn8.Width = 155;
            // 
            // button12
            // 
            this.button12.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button12.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.button12.Location = new System.Drawing.Point(246, 4);
            this.button12.Name = "button12";
            this.button12.Size = new System.Drawing.Size(75, 25);
            this.button12.TabIndex = 38;
            this.button12.Text = "Duplicate";
            this.button12.UseVisualStyleBackColor = true;
            this.button12.Click += new System.EventHandler(this.button12_Click);
            // 
            // table1
            // 
            this.table1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.table1.BorderColor = System.Drawing.Color.Black;
            this.table1.ColumnModel = this.columnModel1;
            this.table1.DataMember = null;
            this.table1.DataSourceColumnBinder = dataSourceColumnBinder2;
            dragDropRenderer2.ForeColor = System.Drawing.Color.Red;
            this.table1.DragDropRenderer = dragDropRenderer2;
            this.table1.FullRowSelect = true;
            this.table1.GridLines = XPTable.Models.GridLines.Both;
            this.table1.GridLinesContrainedToData = false;
            this.table1.Location = new System.Drawing.Point(2, 36);
            this.table1.Name = "table1";
            this.table1.NoItemsText = "There\'s no configuration.";
            this.table1.Size = new System.Drawing.Size(400, 594);
            this.table1.SortedColumnBackColor = System.Drawing.Color.White;
            this.table1.TabIndex = 0;
            this.table1.TableModel = this.tableModel1;
            this.table1.Text = "table1";
            this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table1.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn1,
            this.textColumn2});
            // 
            // textColumn1
            // 
            this.textColumn1.Editable = false;
            this.textColumn1.IsTextTrimmed = false;
            this.textColumn1.Text = "Configuragion Name";
            this.textColumn1.ToolTipText = "";
            this.textColumn1.Width = 240;
            // 
            // textColumn2
            // 
            this.textColumn2.Editable = false;
            this.textColumn2.IsTextTrimmed = false;
            this.textColumn2.Text = "Last Updated";
            this.textColumn2.ToolTipText = "";
            this.textColumn2.Width = 120;
            // 
            // DeviceConfigForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(912, 668);
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
            this.MinimumSize = new System.Drawing.Size(640, 690);
            this.Name = "DeviceConfigForm";
            this.Text = "Edit Device Configuration";
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
            ((System.ComponentModel.ISupportInitialize)(this.upDownK40Res)).EndInit();
            this.groupBox3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.table4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

        // Token: 0x0400025D RID: 605
        global::System.ComponentModel.IContainer components;

		// Token: 0x0400025E RID: 606
		global::System.Windows.Forms.Label label4;

		// Token: 0x0400025F RID: 607
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x04000260 RID: 608
		global::XPTable.Models.Table table1;

		// Token: 0x04000261 RID: 609
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x04000262 RID: 610
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x04000263 RID: 611
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x04000264 RID: 612
		global::System.Windows.Forms.Label label5;

		// Token: 0x04000265 RID: 613
		global::System.Windows.Forms.Button button3;

		// Token: 0x04000266 RID: 614
		global::System.Windows.Forms.Button button4;

		// Token: 0x04000267 RID: 615
		global::System.Windows.Forms.Button button5;

		// Token: 0x04000268 RID: 616
		global::System.Windows.Forms.Button button6;

		global::System.Windows.Forms.Button button13;

		global::System.Windows.Forms.Button button14;

        global::System.Windows.Forms.Button button15;

        global::System.Windows.Forms.Button button16;

        // Token: 0x04000269 RID: 617
        global::XPTable.Models.TextColumn textColumn2;

		// Token: 0x0400026A RID: 618
		global::System.Windows.Forms.Label label18;

		// Token: 0x0400026B RID: 619
		global::System.Windows.Forms.TabControl tabControl1;

		// Token: 0x0400026C RID: 620
		global::System.Windows.Forms.TabPage tabPage1;

		// Token: 0x0400026D RID: 621
		global::System.Windows.Forms.TabPage tabPage2;

		// Token: 0x0400026E RID: 622
		global::System.Windows.Forms.TabPage tabPage3;

		// Token: 0x0400026F RID: 623
		global::System.Windows.Forms.TabPage tabPage4;

		// Token: 0x04000270 RID: 624
		global::System.Windows.Forms.Button button7;

		// Token: 0x04000271 RID: 625
		global::System.Windows.Forms.Label label23;

		// Token: 0x04000272 RID: 626
		global::System.Windows.Forms.TextBox textBox17;

		// Token: 0x04000273 RID: 627
		global::System.Windows.Forms.TextBox textBox18;

		// Token: 0x04000274 RID: 628
		global::System.Windows.Forms.Label label24;

		// Token: 0x04000275 RID: 629
		global::System.Windows.Forms.Label label26;

		// Token: 0x04000276 RID: 630
		global::System.Windows.Forms.TextBox textBox19;

		// Token: 0x04000277 RID: 631
		global::System.Windows.Forms.ComboBox comboBox4;

		// Token: 0x04000278 RID: 632
		global::System.Windows.Forms.Label label25;

		// Token: 0x04000279 RID: 633
		global::System.Windows.Forms.Label label27;

		// Token: 0x0400027A RID: 634
		global::System.Windows.Forms.Label label28;

		// Token: 0x0400027B RID: 635
		global::BecquerelMonitor.DoubleTextBox doubleTextBox5;

		// Token: 0x0400027C RID: 636
		global::System.Windows.Forms.Label label16;

		// Token: 0x0400027D RID: 637
		global::BecquerelMonitor.IntegerTextBox integerTextBox1;

		// Token: 0x0400027E RID: 638
		global::System.Windows.Forms.Label label31;

		// Token: 0x0400027F RID: 639
		global::System.Windows.Forms.TabPage tabPage5;

		// Token: 0x04000280 RID: 640
		global::System.Windows.Forms.Label label38;

		// Token: 0x04000281 RID: 641
		global::System.Windows.Forms.Label label39;

		// Token: 0x04000282 RID: 642
		global::System.Windows.Forms.Label label40;

		// Token: 0x04000283 RID: 643
		global::System.Windows.Forms.NumericUpDown numericUpDown3;

		// Token: 0x04000284 RID: 644
		global::System.Windows.Forms.NumericUpDown numericUpDown4;

		global::System.Windows.Forms.NumericUpDown numericUpDown10;

		global::System.Windows.Forms.NumericUpDown numericUpDown11;

		global::System.Windows.Forms.NumericUpDown numericUpDown12;

		global::System.Windows.Forms.NumericUpDown numericUpDown13;

		global::System.Windows.Forms.NumericUpDown numericUpDown14;

		global::System.Windows.Forms.NumericUpDown numericUpDown15;

        global::System.Windows.Forms.NumericUpDown numericUpDown16;

        // Token: 0x04000285 RID: 645
        global::System.Windows.Forms.NumericUpDown numericUpDown5;

		// Token: 0x04000286 RID: 646
		global::System.Windows.Forms.Label label42;

		// Token: 0x04000287 RID: 647
		global::System.Windows.Forms.NumericUpDown numericUpDown6;

		// Token: 0x04000288 RID: 648
		global::System.Windows.Forms.Label label41;

		// Token: 0x04000289 RID: 649
		global::System.Windows.Forms.Button button12;

		// Token: 0x0400028A RID: 650
		global::System.Windows.Forms.Label label21;

		// Token: 0x0400028B RID: 651
		global::BecquerelMonitor.DoubleTextBox doubleTextBox6;

		// Token: 0x0400028C RID: 652
		global::System.Windows.Forms.Label label1;

		// Token: 0x0400028D RID: 653
		global::System.Windows.Forms.ComboBox comboBox1;

		// Token: 0x0400028E RID: 654
		global::System.Windows.Forms.TabPage tabPage6;

        global::System.Windows.Forms.TabPage tabPage7;

        // Token: 0x0400028F RID: 655
        global::System.Windows.Forms.Label label2;

		// Token: 0x04000290 RID: 656
		global::System.Windows.Forms.Panel panel1;

		// Token: 0x04000291 RID: 657
		global::System.Windows.Forms.Button button9;

		// Token: 0x04000292 RID: 658
		global::System.Windows.Forms.Button button8;

		// Token: 0x04000293 RID: 659
		global::System.Windows.Forms.Button button1;

		// Token: 0x04000294 RID: 660
		global::System.Windows.Forms.Button button11;

		// Token: 0x04000295 RID: 661
		global::System.Windows.Forms.Label label36;

		// Token: 0x04000296 RID: 662
		global::System.Windows.Forms.GroupBox groupBox1;

		// Token: 0x04000297 RID: 663
		global::System.Windows.Forms.Label label3;

		// Token: 0x04000298 RID: 664
		global::System.Windows.Forms.Label label6;

		// Token: 0x04000299 RID: 665
		global::System.Windows.Forms.Label label7;

		// Token: 0x0400029A RID: 666
		global::System.Windows.Forms.TextBox numericUpDown1;

		// Token: 0x0400029B RID: 667
		global::System.Windows.Forms.TextBox numericUpDown2;

		// Token: 0x0400029C RID: 668
		global::System.Windows.Forms.TextBox numericUpDown7;

		global::System.Windows.Forms.TextBox numericUpDown8;

		global::System.Windows.Forms.TextBox numericUpDown9;

		// Token: 0x0400029D RID: 669
		global::System.Windows.Forms.Label label8;

		global::System.Windows.Forms.Label label43;

		global::System.Windows.Forms.Label label44;

		// Token: 0x0400029E RID: 670
		global::System.Windows.Forms.TextBox textBox15;

        global::System.Windows.Forms.TextBox textBox16;

        // Token: 0x0400029F RID: 671
        global::XPTable.Models.Table table2;

		// Token: 0x040002A0 RID: 672
		global::XPTable.Models.ColumnModel columnModel2;

		// Token: 0x040002A1 RID: 673
		global::XPTable.Models.TableModel tableModel2;

		// Token: 0x040002A2 RID: 674
		global::XPTable.Models.TextColumn textColumn3;

		// Token: 0x040002A3 RID: 675
		global::XPTable.Models.NumberColumn numberColumn1;

		// Token: 0x040002A4 RID: 676
		global::System.Windows.Forms.Label label9;

		// Token: 0x040002A5 RID: 677
		global::System.Windows.Forms.GroupBox groupBox3;

		// Token: 0x040002A6 RID: 678
		//global::System.Windows.Forms.Label label15;

		global::System.Windows.Forms.Label label49;

		global::System.Windows.Forms.Label label50;

		global::System.Windows.Forms.Label label51;

		//global::System.Windows.Forms.Label label14;
		//global::System.Windows.Forms.Label label13;
		//global::BecquerelMonitor.DoubleTextBox doubleTextBox3;
		//global::BecquerelMonitor.DoubleTextBox doubleTextBox2;
		//global::System.Windows.Forms.Label label12;
		//global::System.Windows.Forms.Label label11;
		//global::BecquerelMonitor.DoubleTextBox doubleTextBox1;
		//global::System.Windows.Forms.Label label10;

		// Token: 0x040002AF RID: 687
		global::System.Windows.Forms.GroupBox groupBox2;

		// Token: 0x040002B0 RID: 688
		global::XPTable.Models.NumberColumn numberColumn2;

		// Token: 0x040002B1 RID: 689
		global::XPTable.Models.Table table3;
        global::XPTable.Models.Table table4;

        // Token: 0x040002B2 RID: 690
        global::XPTable.Models.ColumnModel columnModel3;

        global::XPTable.Models.ColumnModel columnModel4;

        // Token: 0x040002B3 RID: 691
        global::XPTable.Models.TextColumn textColumn4;

		// Token: 0x040002B4 RID: 692
		global::XPTable.Models.TableModel tableModel3;

        global::XPTable.Models.TableModel tableModel4;

        // Token: 0x040002B5 RID: 693
        global::System.Windows.Forms.Label label19;

		global::System.Windows.Forms.Label label45;

		global::System.Windows.Forms.Label label46;

		global::System.Windows.Forms.Label label47;

		global::System.Windows.Forms.Label label48;

        global::System.Windows.Forms.Label label52;

        // Token: 0x040002B6 RID: 694
        global::System.Windows.Forms.Panel panel2;

		// Token: 0x040002B7 RID: 695
		global::System.Windows.Forms.Button button10;

		// Token: 0x040002B8 RID: 696
		global::System.Windows.Forms.Button button2;

		// Token: 0x040002B9 RID: 697
		global::XPTable.Models.NumberColumn numberColumn3;

		// Token: 0x040002BA RID: 698
		global::XPTable.Models.NumberColumn numberColumn4;

        global::XPTable.Models.NumberColumn numberColumn5;
        global::XPTable.Models.NumberColumn numberColumn6;
        global::XPTable.Models.NumberColumn numberColumn7;
        global::XPTable.Models.NumberColumn numberColumn8;
		global::System.Windows.Forms.Label effROIText;
		global::System.Windows.Forms.Button clearEffROI;
		global::System.Windows.Forms.ComboBox selectEffROI;
        private System.Windows.Forms.Button buttonLoadK40Spectrum;
        private System.Windows.Forms.Label labelSpectrumNote;
        private System.Windows.Forms.Label labelEffNote;
        private System.Windows.Forms.Button buttonLoadEff;
        private System.Windows.Forms.Button buttonEstimateDRConf;
        private System.Windows.Forms.Label labelK40ResNote;
        private System.Windows.Forms.NumericUpDown upDownK40Res;
        private System.Windows.Forms.TextBox textBoxEffFile;
        private System.Windows.Forms.TextBox textBoxK40File;
        private System.Windows.Forms.Label labelDREstimateTitle;
    }
}
