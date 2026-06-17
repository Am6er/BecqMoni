using System.Windows.Forms.VisualStyles;

namespace BecquerelMonitor.NucBase
{
    partial class NucBase
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NucBase));
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.HalfLifeUOMComboBox = new System.Windows.Forms.ComboBox();
            this.HalfLifeTextBox = new System.Windows.Forms.TextBox();
            this.label12 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.HighEnrgTextBox = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.LowEnrgTextBox = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.IntencityTextBox = new System.Windows.Forms.TextBox();
            this.IncludeDecayChainCheckBox = new System.Windows.Forms.CheckBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.IsotopeTextBox = new System.Windows.Forms.TextBox();
            this.SearchButton = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.comboBoxNameFormat = new System.Windows.Forms.ComboBox();
            this.labelNameFormat = new System.Windows.Forms.Label();
            this.checkBoxAppendRootName = new System.Windows.Forms.CheckBox();
            this.checkBoxOverwriteDef = new System.Windows.Forms.CheckBox();
            this.buttonImportDef = new System.Windows.Forms.Button();
            this.label7 = new System.Windows.Forms.Label();
            this.DaughtersDataGridView = new System.Windows.Forms.DataGridView();
            this.NameColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TypeColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PercentColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.label6 = new System.Windows.Forms.Label();
            this.ParentsDataGridView = new System.Windows.Forms.DataGridView();
            this.NameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PercentColum = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.IsotopeAbundance = new System.Windows.Forms.Label();
            this.abundanceLbl = new System.Windows.Forms.Label();
            this.IsotopeSpecActivity = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.IsotopeHLLabel = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.IsotopeNLabel = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.IsotopeZLabel = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.IsotopeNameLabel = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.ResultDataGridView = new System.Windows.Forms.DataGridView();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.CheckBoxColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.NameColumn2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EnTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EnergyColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IntensityColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.XRayTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DecaModeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.HalfLifeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.groupBox1.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DaughtersDataGridView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ParentsDataGridView)).BeginInit();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResultDataGridView)).BeginInit();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.HalfLifeUOMComboBox);
            this.groupBox1.Controls.Add(this.HalfLifeTextBox);
            this.groupBox1.Controls.Add(this.label12);
            this.groupBox1.Controls.Add(this.label11);
            this.groupBox1.Controls.Add(this.HighEnrgTextBox);
            this.groupBox1.Controls.Add(this.label10);
            this.groupBox1.Controls.Add(this.LowEnrgTextBox);
            this.groupBox1.Controls.Add(this.label9);
            this.groupBox1.Controls.Add(this.IntencityTextBox);
            this.groupBox1.Controls.Add(this.IncludeDecayChainCheckBox);
            this.groupBox1.Controls.Add(this.label8);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.IsotopeTextBox);
            this.groupBox1.Controls.Add(this.SearchButton);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            this.toolTip1.SetToolTip(this.groupBox1, resources.GetString("groupBox1.ToolTip"));
            // 
            // HalfLifeUOMComboBox
            // 
            resources.ApplyResources(this.HalfLifeUOMComboBox, "HalfLifeUOMComboBox");
            this.HalfLifeUOMComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.HalfLifeUOMComboBox.FormattingEnabled = true;
            this.HalfLifeUOMComboBox.Items.AddRange(new object[] {
            resources.GetString("HalfLifeUOMComboBox.Items"),
            resources.GetString("HalfLifeUOMComboBox.Items1"),
            resources.GetString("HalfLifeUOMComboBox.Items2"),
            resources.GetString("HalfLifeUOMComboBox.Items3"),
            resources.GetString("HalfLifeUOMComboBox.Items4"),
            resources.GetString("HalfLifeUOMComboBox.Items5"),
            resources.GetString("HalfLifeUOMComboBox.Items6"),
            resources.GetString("HalfLifeUOMComboBox.Items7")});
            this.HalfLifeUOMComboBox.Name = "HalfLifeUOMComboBox";
            this.toolTip1.SetToolTip(this.HalfLifeUOMComboBox, resources.GetString("HalfLifeUOMComboBox.ToolTip"));
            // 
            // HalfLifeTextBox
            // 
            resources.ApplyResources(this.HalfLifeTextBox, "HalfLifeTextBox");
            this.HalfLifeTextBox.Name = "HalfLifeTextBox";
            this.toolTip1.SetToolTip(this.HalfLifeTextBox, resources.GetString("HalfLifeTextBox.ToolTip"));
            this.HalfLifeTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.HalfLifeTextBox_KeyDown);
            // 
            // label12
            // 
            resources.ApplyResources(this.label12, "label12");
            this.label12.Name = "label12";
            this.toolTip1.SetToolTip(this.label12, resources.GetString("label12.ToolTip"));
            // 
            // label11
            // 
            resources.ApplyResources(this.label11, "label11");
            this.label11.Name = "label11";
            this.toolTip1.SetToolTip(this.label11, resources.GetString("label11.ToolTip"));
            // 
            // HighEnrgTextBox
            // 
            resources.ApplyResources(this.HighEnrgTextBox, "HighEnrgTextBox");
            this.HighEnrgTextBox.Name = "HighEnrgTextBox";
            this.toolTip1.SetToolTip(this.HighEnrgTextBox, resources.GetString("HighEnrgTextBox.ToolTip"));
            this.HighEnrgTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.HighEnrgTextBox_KeyDown);
            // 
            // label10
            // 
            resources.ApplyResources(this.label10, "label10");
            this.label10.Name = "label10";
            this.toolTip1.SetToolTip(this.label10, resources.GetString("label10.ToolTip"));
            // 
            // LowEnrgTextBox
            // 
            resources.ApplyResources(this.LowEnrgTextBox, "LowEnrgTextBox");
            this.LowEnrgTextBox.Name = "LowEnrgTextBox";
            this.toolTip1.SetToolTip(this.LowEnrgTextBox, resources.GetString("LowEnrgTextBox.ToolTip"));
            this.LowEnrgTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.LowEnrgTextBox_KeyDown);
            // 
            // label9
            // 
            resources.ApplyResources(this.label9, "label9");
            this.label9.Name = "label9";
            this.toolTip1.SetToolTip(this.label9, resources.GetString("label9.ToolTip"));
            // 
            // IntencityTextBox
            // 
            resources.ApplyResources(this.IntencityTextBox, "IntencityTextBox");
            this.IntencityTextBox.Name = "IntencityTextBox";
            this.toolTip1.SetToolTip(this.IntencityTextBox, resources.GetString("IntencityTextBox.ToolTip"));
            this.IntencityTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.IntencityTextBox_KeyDown);
            // 
            // IncludeDecayChainCheckBox
            // 
            resources.ApplyResources(this.IncludeDecayChainCheckBox, "IncludeDecayChainCheckBox");
            this.IncludeDecayChainCheckBox.Name = "IncludeDecayChainCheckBox";
            this.toolTip1.SetToolTip(this.IncludeDecayChainCheckBox, resources.GetString("IncludeDecayChainCheckBox.ToolTip"));
            this.IncludeDecayChainCheckBox.UseVisualStyleBackColor = true;
            this.IncludeDecayChainCheckBox.CheckedChanged += new System.EventHandler(this.IncludeDecayChainCheckBox_CheckedChanged);
            // 
            // label8
            // 
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            this.toolTip1.SetToolTip(this.label8, resources.GetString("label8.ToolTip"));
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            this.toolTip1.SetToolTip(this.label1, resources.GetString("label1.ToolTip"));
            // 
            // IsotopeTextBox
            // 
            resources.ApplyResources(this.IsotopeTextBox, "IsotopeTextBox");
            this.IsotopeTextBox.Name = "IsotopeTextBox";
            this.toolTip1.SetToolTip(this.IsotopeTextBox, resources.GetString("IsotopeTextBox.ToolTip"));
            this.IsotopeTextBox.TextChanged += new System.EventHandler(this.IsotopeTextBox_TextChanged);
            this.IsotopeTextBox.Enter += new System.EventHandler(this.IsotopeTextBox_Enter);
            this.IsotopeTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.IsotopeTextBox_KeyDown);
            // 
            // SearchButton
            // 
            resources.ApplyResources(this.SearchButton, "SearchButton");
            this.SearchButton.Name = "SearchButton";
            this.toolTip1.SetToolTip(this.SearchButton, resources.GetString("SearchButton.ToolTip"));
            this.SearchButton.UseVisualStyleBackColor = true;
            this.SearchButton.Click += new System.EventHandler(this.SearchButton_Click);
            // 
            // panel1
            // 
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Controls.Add(this.comboBoxNameFormat);
            this.panel1.Controls.Add(this.labelNameFormat);
            this.panel1.Controls.Add(this.checkBoxAppendRootName);
            this.panel1.Controls.Add(this.checkBoxOverwriteDef);
            this.panel1.Controls.Add(this.buttonImportDef);
            this.panel1.Controls.Add(this.label7);
            this.panel1.Controls.Add(this.DaughtersDataGridView);
            this.panel1.Controls.Add(this.label6);
            this.panel1.Controls.Add(this.ParentsDataGridView);
            this.panel1.Controls.Add(this.groupBox2);
            this.panel1.Controls.Add(this.ResultDataGridView);
            this.panel1.Name = "panel1";
            this.toolTip1.SetToolTip(this.panel1, resources.GetString("panel1.ToolTip"));
            // 
            // comboBoxNameFormat
            // 
            resources.ApplyResources(this.comboBoxNameFormat, "comboBoxNameFormat");
            this.comboBoxNameFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxNameFormat.FormattingEnabled = true;
            this.comboBoxNameFormat.Items.AddRange(new object[] {
            resources.GetString("comboBoxNameFormat.Items"),
            resources.GetString("comboBoxNameFormat.Items1"),
            resources.GetString("comboBoxNameFormat.Items2")});
            this.comboBoxNameFormat.Name = "comboBoxNameFormat";
            this.toolTip1.SetToolTip(this.comboBoxNameFormat, resources.GetString("comboBoxNameFormat.ToolTip"));
            // 
            // labelNameFormat
            // 
            resources.ApplyResources(this.labelNameFormat, "labelNameFormat");
            this.labelNameFormat.Name = "labelNameFormat";
            this.toolTip1.SetToolTip(this.labelNameFormat, resources.GetString("labelNameFormat.ToolTip"));
            // 
            // checkBoxAppendRootName
            // 
            resources.ApplyResources(this.checkBoxAppendRootName, "checkBoxAppendRootName");
            this.checkBoxAppendRootName.Checked = true;
            this.checkBoxAppendRootName.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxAppendRootName.Name = "checkBoxAppendRootName";
            this.toolTip1.SetToolTip(this.checkBoxAppendRootName, resources.GetString("checkBoxAppendRootName.ToolTip"));
            this.checkBoxAppendRootName.UseVisualStyleBackColor = true;
            // 
            // checkBoxOverwriteDef
            // 
            resources.ApplyResources(this.checkBoxOverwriteDef, "checkBoxOverwriteDef");
            this.checkBoxOverwriteDef.Name = "checkBoxOverwriteDef";
            this.toolTip1.SetToolTip(this.checkBoxOverwriteDef, resources.GetString("checkBoxOverwriteDef.ToolTip"));
            this.checkBoxOverwriteDef.UseVisualStyleBackColor = true;
            // 
            // buttonImportDef
            // 
            resources.ApplyResources(this.buttonImportDef, "buttonImportDef");
            this.buttonImportDef.Name = "buttonImportDef";
            this.toolTip1.SetToolTip(this.buttonImportDef, resources.GetString("buttonImportDef.ToolTip"));
            this.buttonImportDef.UseVisualStyleBackColor = true;
            this.buttonImportDef.Click += new System.EventHandler(this.buttonImportDef_Click);
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            this.toolTip1.SetToolTip(this.label7, resources.GetString("label7.ToolTip"));
            // 
            // DaughtersDataGridView
            // 
            resources.ApplyResources(this.DaughtersDataGridView, "DaughtersDataGridView");
            this.DaughtersDataGridView.AllowUserToAddRows = false;
            this.DaughtersDataGridView.AllowUserToDeleteRows = false;
            this.DaughtersDataGridView.AllowUserToResizeRows = false;
            this.DaughtersDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.DaughtersDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DaughtersDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.NameColumn1,
            this.TypeColumn1,
            this.PercentColumn1});
            this.DaughtersDataGridView.Name = "DaughtersDataGridView";
            this.DaughtersDataGridView.ReadOnly = true;
            this.DaughtersDataGridView.RowHeadersVisible = false;
            this.DaughtersDataGridView.ShowEditingIcon = false;
            this.toolTip1.SetToolTip(this.DaughtersDataGridView, resources.GetString("DaughtersDataGridView.ToolTip"));
            // 
            // NameColumn1
            // 
            resources.ApplyResources(this.NameColumn1, "NameColumn1");
            this.NameColumn1.Name = "NameColumn1";
            this.NameColumn1.ReadOnly = true;
            // 
            // TypeColumn1
            // 
            this.TypeColumn1.FillWeight = 60F;
            resources.ApplyResources(this.TypeColumn1, "TypeColumn1");
            this.TypeColumn1.Name = "TypeColumn1";
            this.TypeColumn1.ReadOnly = true;
            // 
            // PercentColumn1
            // 
            this.PercentColumn1.FillWeight = 140F;
            resources.ApplyResources(this.PercentColumn1, "PercentColumn1");
            this.PercentColumn1.Name = "PercentColumn1";
            this.PercentColumn1.ReadOnly = true;
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            this.toolTip1.SetToolTip(this.label6, resources.GetString("label6.ToolTip"));
            // 
            // ParentsDataGridView
            // 
            resources.ApplyResources(this.ParentsDataGridView, "ParentsDataGridView");
            this.ParentsDataGridView.AllowUserToAddRows = false;
            this.ParentsDataGridView.AllowUserToDeleteRows = false;
            this.ParentsDataGridView.AllowUserToResizeRows = false;
            this.ParentsDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ParentsDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ParentsDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.NameColumn,
            this.TypeColumn,
            this.PercentColum});
            this.ParentsDataGridView.Name = "ParentsDataGridView";
            this.ParentsDataGridView.ReadOnly = true;
            this.ParentsDataGridView.RowHeadersVisible = false;
            this.ParentsDataGridView.ShowEditingIcon = false;
            this.toolTip1.SetToolTip(this.ParentsDataGridView, resources.GetString("ParentsDataGridView.ToolTip"));
            // 
            // NameColumn
            // 
            resources.ApplyResources(this.NameColumn, "NameColumn");
            this.NameColumn.Name = "NameColumn";
            this.NameColumn.ReadOnly = true;
            // 
            // TypeColumn
            // 
            this.TypeColumn.FillWeight = 60F;
            resources.ApplyResources(this.TypeColumn, "TypeColumn");
            this.TypeColumn.Name = "TypeColumn";
            this.TypeColumn.ReadOnly = true;
            // 
            // PercentColum
            // 
            this.PercentColum.FillWeight = 140F;
            resources.ApplyResources(this.PercentColum, "PercentColum");
            this.PercentColum.Name = "PercentColum";
            this.PercentColum.ReadOnly = true;
            // 
            // groupBox2
            // 
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Controls.Add(this.IsotopeAbundance);
            this.groupBox2.Controls.Add(this.abundanceLbl);
            this.groupBox2.Controls.Add(this.IsotopeSpecActivity);
            this.groupBox2.Controls.Add(this.label13);
            this.groupBox2.Controls.Add(this.IsotopeHLLabel);
            this.groupBox2.Controls.Add(this.label5);
            this.groupBox2.Controls.Add(this.IsotopeNLabel);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.IsotopeZLabel);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Controls.Add(this.IsotopeNameLabel);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            this.toolTip1.SetToolTip(this.groupBox2, resources.GetString("groupBox2.ToolTip"));
            // 
            // IsotopeAbundance
            // 
            resources.ApplyResources(this.IsotopeAbundance, "IsotopeAbundance");
            this.IsotopeAbundance.Name = "IsotopeAbundance";
            this.toolTip1.SetToolTip(this.IsotopeAbundance, resources.GetString("IsotopeAbundance.ToolTip"));
            // 
            // abundanceLbl
            // 
            resources.ApplyResources(this.abundanceLbl, "abundanceLbl");
            this.abundanceLbl.Name = "abundanceLbl";
            this.toolTip1.SetToolTip(this.abundanceLbl, resources.GetString("abundanceLbl.ToolTip"));
            // 
            // IsotopeSpecActivity
            // 
            resources.ApplyResources(this.IsotopeSpecActivity, "IsotopeSpecActivity");
            this.IsotopeSpecActivity.Name = "IsotopeSpecActivity";
            this.toolTip1.SetToolTip(this.IsotopeSpecActivity, resources.GetString("IsotopeSpecActivity.ToolTip"));
            // 
            // label13
            // 
            resources.ApplyResources(this.label13, "label13");
            this.label13.Name = "label13";
            this.toolTip1.SetToolTip(this.label13, resources.GetString("label13.ToolTip"));
            // 
            // IsotopeHLLabel
            // 
            resources.ApplyResources(this.IsotopeHLLabel, "IsotopeHLLabel");
            this.IsotopeHLLabel.Name = "IsotopeHLLabel";
            this.toolTip1.SetToolTip(this.IsotopeHLLabel, resources.GetString("IsotopeHLLabel.ToolTip"));
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            this.toolTip1.SetToolTip(this.label5, resources.GetString("label5.ToolTip"));
            // 
            // IsotopeNLabel
            // 
            resources.ApplyResources(this.IsotopeNLabel, "IsotopeNLabel");
            this.IsotopeNLabel.Name = "IsotopeNLabel";
            this.toolTip1.SetToolTip(this.IsotopeNLabel, resources.GetString("IsotopeNLabel.ToolTip"));
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            this.toolTip1.SetToolTip(this.label4, resources.GetString("label4.ToolTip"));
            // 
            // IsotopeZLabel
            // 
            resources.ApplyResources(this.IsotopeZLabel, "IsotopeZLabel");
            this.IsotopeZLabel.Name = "IsotopeZLabel";
            this.toolTip1.SetToolTip(this.IsotopeZLabel, resources.GetString("IsotopeZLabel.ToolTip"));
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            this.toolTip1.SetToolTip(this.label3, resources.GetString("label3.ToolTip"));
            // 
            // IsotopeNameLabel
            // 
            resources.ApplyResources(this.IsotopeNameLabel, "IsotopeNameLabel");
            this.IsotopeNameLabel.Name = "IsotopeNameLabel";
            this.toolTip1.SetToolTip(this.IsotopeNameLabel, resources.GetString("IsotopeNameLabel.ToolTip"));
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            this.toolTip1.SetToolTip(this.label2, resources.GetString("label2.ToolTip"));
            // 
            // ResultDataGridView
            // 
            resources.ApplyResources(this.ResultDataGridView, "ResultDataGridView");
            this.ResultDataGridView.AllowUserToAddRows = false;
            this.ResultDataGridView.AllowUserToDeleteRows = false;
            this.ResultDataGridView.AllowUserToResizeRows = false;
            this.ResultDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ResultDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ResultDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.CheckBoxColumn,
            this.NameColumn2,
            this.EnTypeColumn,
            this.EnergyColumn,
            this.IntensityColumn,
            this.XRayTypeColumn,
            this.DecaModeColumn,
            this.HalfLifeColumn});
            this.ResultDataGridView.Name = "ResultDataGridView";
            this.ResultDataGridView.RowHeadersVisible = false;
            this.ResultDataGridView.ShowEditingIcon = false;
            this.toolTip1.SetToolTip(this.ResultDataGridView, resources.GetString("ResultDataGridView.ToolTip"));
            this.ResultDataGridView.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.ResultDataGridView_CellClick);
            this.ResultDataGridView.CellEnter += new System.Windows.Forms.DataGridViewCellEventHandler(this.ResultDataGridView_CellEnter);
            // 
            // CheckBoxColumn
            // 
            this.CheckBoxColumn.FalseValue = false;
            resources.ApplyResources(this.CheckBoxColumn, "CheckBoxColumn");
            this.CheckBoxColumn.Name = "CheckBoxColumn";
            this.CheckBoxColumn.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.CheckBoxColumn.TrueValue = true;
            // 
            // NameColumn2
            // 
            resources.ApplyResources(this.NameColumn2, "NameColumn2");
            this.NameColumn2.Name = "NameColumn2";
            this.NameColumn2.ReadOnly = true;
            // 
            // EnTypeColumn
            // 
            this.EnTypeColumn.FillWeight = 50F;
            resources.ApplyResources(this.EnTypeColumn, "EnTypeColumn");
            this.EnTypeColumn.Name = "EnTypeColumn";
            this.EnTypeColumn.ReadOnly = true;
            // 
            // EnergyColumn
            // 
            resources.ApplyResources(this.EnergyColumn, "EnergyColumn");
            this.EnergyColumn.Name = "EnergyColumn";
            this.EnergyColumn.ReadOnly = true;
            // 
            // IntensityColumn
            // 
            this.IntensityColumn.FillWeight = 70F;
            resources.ApplyResources(this.IntensityColumn, "IntensityColumn");
            this.IntensityColumn.Name = "IntensityColumn";
            this.IntensityColumn.ReadOnly = true;
            // 
            // XRayTypeColumn
            // 
            this.XRayTypeColumn.FillWeight = 50F;
            resources.ApplyResources(this.XRayTypeColumn, "XRayTypeColumn");
            this.XRayTypeColumn.Name = "XRayTypeColumn";
            this.XRayTypeColumn.ReadOnly = true;
            // 
            // DecaModeColumn
            // 
            this.DecaModeColumn.FillWeight = 50F;
            resources.ApplyResources(this.DecaModeColumn, "DecaModeColumn");
            this.DecaModeColumn.Name = "DecaModeColumn";
            this.DecaModeColumn.ReadOnly = true;
            // 
            // HalfLifeColumn
            // 
            resources.ApplyResources(this.HalfLifeColumn, "HalfLifeColumn");
            this.HalfLifeColumn.Name = "HalfLifeColumn";
            this.HalfLifeColumn.ReadOnly = true;
            // 
            // NucBase
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.groupBox1);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "NucBase";
            this.toolTip1.SetToolTip(this, resources.GetString("$this.ToolTip"));
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DaughtersDataGridView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ParentsDataGridView)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResultDataGridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox IsotopeTextBox;
        private System.Windows.Forms.Button SearchButton;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.DataGridView ResultDataGridView;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Label IsotopeNameLabel;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label IsotopeNLabel;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label IsotopeZLabel;
        private System.Windows.Forms.Label IsotopeHLLabel;
        private System.Windows.Forms.DataGridView ParentsDataGridView;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.DataGridView DaughtersDataGridView;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.TextBox HighEnrgTextBox;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox LowEnrgTextBox;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox IntencityTextBox;
        private System.Windows.Forms.CheckBox IncludeDecayChainCheckBox;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.ComboBox HalfLifeUOMComboBox;
        private System.Windows.Forms.TextBox HalfLifeTextBox;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.DataGridViewTextBoxColumn NameColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn TypeColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn PercentColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn NameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn TypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn PercentColum;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.Label IsotopeSpecActivity;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label abundanceLbl;
        private System.Windows.Forms.Label IsotopeAbundance;
        private System.Windows.Forms.Button buttonImportDef;
        private System.Windows.Forms.Label labelNameFormat;
        private System.Windows.Forms.CheckBox checkBoxAppendRootName;
        private System.Windows.Forms.CheckBox checkBoxOverwriteDef;
        private System.Windows.Forms.ComboBox comboBoxNameFormat;
        private System.Windows.Forms.DataGridViewCheckBoxColumn CheckBoxColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn NameColumn2;
        private System.Windows.Forms.DataGridViewTextBoxColumn EnTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn EnergyColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn IntensityColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn XRayTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn DecaModeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn HalfLifeColumn;
    }
}