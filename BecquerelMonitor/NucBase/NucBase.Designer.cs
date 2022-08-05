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
            this.label7 = new System.Windows.Forms.Label();
            this.DaughtersDataGridView = new System.Windows.Forms.DataGridView();
            this.label6 = new System.Windows.Forms.Label();
            this.ParentsDataGridView = new System.Windows.Forms.DataGridView();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.IsotopeHLLabel = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.IsotopeNLabel = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.IsotopeZLabel = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.IsotopeNameLabel = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.ResultDataGridView = new System.Windows.Forms.DataGridView();
            this.NameColumn2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EnTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EnergyColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IntensityColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.XRayTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DecaModeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.NameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PercentColum = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.NameColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TypeColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PercentColumn1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
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
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(974, 54);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Search Isotope";
            // 
            // HalfLifeUOMComboBox
            // 
            this.HalfLifeUOMComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.HalfLifeUOMComboBox.FormattingEnabled = true;
            this.HalfLifeUOMComboBox.Items.AddRange(new object[] {
            "ns",
            "us",
            "ms",
            "s",
            "m",
            "h",
            "d",
            "Y"});
            this.HalfLifeUOMComboBox.Location = new System.Drawing.Point(509, 19);
            this.HalfLifeUOMComboBox.Name = "HalfLifeUOMComboBox";
            this.HalfLifeUOMComboBox.Size = new System.Drawing.Size(68, 21);
            this.HalfLifeUOMComboBox.TabIndex = 13;
            // 
            // HalfLifeTextBox
            // 
            this.HalfLifeTextBox.Location = new System.Drawing.Point(464, 20);
            this.HalfLifeTextBox.Name = "HalfLifeTextBox";
            this.HalfLifeTextBox.Size = new System.Drawing.Size(41, 20);
            this.HalfLifeTextBox.TabIndex = 12;
            this.HalfLifeTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.HalfLifeTextBox_KeyDown);
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(412, 23);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(51, 13);
            this.label12.TabIndex = 11;
            this.label12.Text = "Half-life >";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(841, 23);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(26, 13);
            this.label11.TabIndex = 10;
            this.label11.Text = "keV";
            // 
            // HighEnrgTextBox
            // 
            this.HighEnrgTextBox.Location = new System.Drawing.Point(790, 20);
            this.HighEnrgTextBox.Name = "HighEnrgTextBox";
            this.HighEnrgTextBox.Size = new System.Drawing.Size(45, 20);
            this.HighEnrgTextBox.TabIndex = 9;
            this.HighEnrgTextBox.Text = "3000";
            this.HighEnrgTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.HighEnrgTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.HighEnrgTextBox_KeyDown);
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(738, 23);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(47, 13);
            this.label10.TabIndex = 8;
            this.label10.Text = "keV and";
            // 
            // LowEnrgTextBox
            // 
            this.LowEnrgTextBox.Location = new System.Drawing.Point(687, 20);
            this.LowEnrgTextBox.Name = "LowEnrgTextBox";
            this.LowEnrgTextBox.Size = new System.Drawing.Size(45, 20);
            this.LowEnrgTextBox.TabIndex = 7;
            this.LowEnrgTextBox.Text = "0";
            this.LowEnrgTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.LowEnrgTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.LowEnrgTextBox_KeyDown);
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(596, 23);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(87, 13);
            this.label9.TabIndex = 6;
            this.label9.Text = "Energy between:";
            // 
            // IntencityTextBox
            // 
            this.IntencityTextBox.Location = new System.Drawing.Point(354, 20);
            this.IntencityTextBox.Name = "IntencityTextBox";
            this.IntencityTextBox.Size = new System.Drawing.Size(35, 20);
            this.IntencityTextBox.TabIndex = 5;
            this.IntencityTextBox.Text = "6";
            this.IntencityTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.IntencityTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.IntencityTextBox_KeyDown);
            // 
            // IncludeDecayChainCheckBox
            // 
            this.IncludeDecayChainCheckBox.AutoSize = true;
            this.IncludeDecayChainCheckBox.Location = new System.Drawing.Point(158, 23);
            this.IncludeDecayChainCheckBox.Name = "IncludeDecayChainCheckBox";
            this.IncludeDecayChainCheckBox.Size = new System.Drawing.Size(122, 17);
            this.IncludeDecayChainCheckBox.TabIndex = 4;
            this.IncludeDecayChainCheckBox.Text = "Include decay chain";
            this.IncludeDecayChainCheckBox.UseVisualStyleBackColor = true;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(285, 23);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(67, 13);
            this.label8.TabIndex = 3;
            this.label8.Text = "% Intencity >";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 23);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(73, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Isotope Name";
            // 
            // IsotopeTextBox
            // 
            this.IsotopeTextBox.Location = new System.Drawing.Point(91, 20);
            this.IsotopeTextBox.Name = "IsotopeTextBox";
            this.IsotopeTextBox.Size = new System.Drawing.Size(61, 20);
            this.IsotopeTextBox.TabIndex = 1;
            this.IsotopeTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.IsotopeTextBox.TextChanged += new System.EventHandler(this.IsotopeTextBox_TextChanged);
            this.IsotopeTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.IsotopeTextBox_KeyDown);
            // 
            // SearchButton
            // 
            this.SearchButton.Location = new System.Drawing.Point(879, 18);
            this.SearchButton.Name = "SearchButton";
            this.SearchButton.Size = new System.Drawing.Size(89, 23);
            this.SearchButton.TabIndex = 0;
            this.SearchButton.Text = "Search";
            this.SearchButton.UseVisualStyleBackColor = true;
            this.SearchButton.Click += new System.EventHandler(this.SearchButton_Click);
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.Controls.Add(this.label7);
            this.panel1.Controls.Add(this.DaughtersDataGridView);
            this.panel1.Controls.Add(this.label6);
            this.panel1.Controls.Add(this.ParentsDataGridView);
            this.panel1.Controls.Add(this.groupBox2);
            this.panel1.Controls.Add(this.ResultDataGridView);
            this.panel1.Location = new System.Drawing.Point(0, 60);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(976, 387);
            this.panel1.TabIndex = 1;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(724, 91);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(93, 13);
            this.label7.TabIndex = 5;
            this.label7.Text = "Decay Daughters:";
            // 
            // DaughtersDataGridView
            // 
            this.DaughtersDataGridView.AllowUserToAddRows = false;
            this.DaughtersDataGridView.AllowUserToDeleteRows = false;
            this.DaughtersDataGridView.AllowUserToResizeRows = false;
            this.DaughtersDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.DaughtersDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DaughtersDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.NameColumn1,
            this.TypeColumn1,
            this.PercentColumn1});
            this.DaughtersDataGridView.Location = new System.Drawing.Point(724, 111);
            this.DaughtersDataGridView.Name = "DaughtersDataGridView";
            this.DaughtersDataGridView.ReadOnly = true;
            this.DaughtersDataGridView.RowHeadersVisible = false;
            this.DaughtersDataGridView.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.DaughtersDataGridView.ShowEditingIcon = false;
            this.DaughtersDataGridView.Size = new System.Drawing.Size(240, 267);
            this.DaughtersDataGridView.TabIndex = 4;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(449, 92);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(80, 13);
            this.label6.TabIndex = 3;
            this.label6.Text = "Decay Parents:";
            // 
            // ParentsDataGridView
            // 
            this.ParentsDataGridView.AllowUserToAddRows = false;
            this.ParentsDataGridView.AllowUserToDeleteRows = false;
            this.ParentsDataGridView.AllowUserToResizeRows = false;
            this.ParentsDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ParentsDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ParentsDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.NameColumn,
            this.TypeColumn,
            this.PercentColum});
            this.ParentsDataGridView.Location = new System.Drawing.Point(448, 111);
            this.ParentsDataGridView.Name = "ParentsDataGridView";
            this.ParentsDataGridView.ReadOnly = true;
            this.ParentsDataGridView.RowHeadersVisible = false;
            this.ParentsDataGridView.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.ParentsDataGridView.ShowEditingIcon = false;
            this.ParentsDataGridView.Size = new System.Drawing.Size(240, 267);
            this.ParentsDataGridView.TabIndex = 2;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.IsotopeHLLabel);
            this.groupBox2.Controls.Add(this.label5);
            this.groupBox2.Controls.Add(this.IsotopeNLabel);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.IsotopeZLabel);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Controls.Add(this.IsotopeNameLabel);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Location = new System.Drawing.Point(448, 3);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(516, 76);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Isotope Info";
            // 
            // IsotopeHLLabel
            // 
            this.IsotopeHLLabel.AutoSize = true;
            this.IsotopeHLLabel.Location = new System.Drawing.Point(66, 46);
            this.IsotopeHLLabel.Name = "IsotopeHLLabel";
            this.IsotopeHLLabel.Size = new System.Drawing.Size(0, 13);
            this.IsotopeHLLabel.TabIndex = 7;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(10, 46);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(49, 13);
            this.label5.TabIndex = 6;
            this.label5.Text = "Half-Life:";
            // 
            // IsotopeNLabel
            // 
            this.IsotopeNLabel.AutoSize = true;
            this.IsotopeNLabel.Location = new System.Drawing.Point(260, 20);
            this.IsotopeNLabel.Name = "IsotopeNLabel";
            this.IsotopeNLabel.Size = new System.Drawing.Size(0, 13);
            this.IsotopeNLabel.TabIndex = 5;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(235, 20);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(18, 13);
            this.label4.TabIndex = 4;
            this.label4.Text = "N:";
            // 
            // IsotopeZLabel
            // 
            this.IsotopeZLabel.AutoSize = true;
            this.IsotopeZLabel.Location = new System.Drawing.Point(160, 20);
            this.IsotopeZLabel.Name = "IsotopeZLabel";
            this.IsotopeZLabel.Size = new System.Drawing.Size(0, 13);
            this.IsotopeZLabel.TabIndex = 3;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(136, 20);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(17, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Z:";
            // 
            // IsotopeNameLabel
            // 
            this.IsotopeNameLabel.AutoSize = true;
            this.IsotopeNameLabel.Location = new System.Drawing.Point(52, 19);
            this.IsotopeNameLabel.Name = "IsotopeNameLabel";
            this.IsotopeNameLabel.Size = new System.Drawing.Size(0, 13);
            this.IsotopeNameLabel.TabIndex = 1;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 20);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(38, 13);
            this.label2.TabIndex = 0;
            this.label2.Text = "Name:";
            // 
            // ResultDataGridView
            // 
            this.ResultDataGridView.AllowUserToAddRows = false;
            this.ResultDataGridView.AllowUserToDeleteRows = false;
            this.ResultDataGridView.AllowUserToResizeRows = false;
            this.ResultDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ResultDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ResultDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.NameColumn2,
            this.EnTypeColumn,
            this.EnergyColumn,
            this.IntensityColumn,
            this.XRayTypeColumn,
            this.DecaModeColumn});
            this.ResultDataGridView.Location = new System.Drawing.Point(8, 0);
            this.ResultDataGridView.Name = "ResultDataGridView";
            this.ResultDataGridView.ReadOnly = true;
            this.ResultDataGridView.RowHeadersVisible = false;
            this.ResultDataGridView.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.ResultDataGridView.ShowEditingIcon = false;
            this.ResultDataGridView.Size = new System.Drawing.Size(434, 378);
            this.ResultDataGridView.TabIndex = 0;
            this.ResultDataGridView.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.ResultDataGridView_CellClick);
            this.ResultDataGridView.CellEnter += new System.Windows.Forms.DataGridViewCellEventHandler(this.ResultDataGridView_CellEnter);
            // 
            // NameColumn2
            // 
            this.NameColumn2.HeaderText = "Name";
            this.NameColumn2.Name = "NameColumn2";
            this.NameColumn2.ReadOnly = true;
            // 
            // EnTypeColumn
            // 
            this.EnTypeColumn.FillWeight = 50F;
            this.EnTypeColumn.HeaderText = "Type";
            this.EnTypeColumn.Name = "EnTypeColumn";
            this.EnTypeColumn.ReadOnly = true;
            this.EnTypeColumn.Width = 50;
            // 
            // EnergyColumn
            // 
            this.EnergyColumn.HeaderText = "Energy, keV";
            this.EnergyColumn.Name = "EnergyColumn";
            this.EnergyColumn.ReadOnly = true;
            // 
            // IntensityColumn
            // 
            this.IntensityColumn.FillWeight = 70F;
            this.IntensityColumn.HeaderText = "Intensity, %";
            this.IntensityColumn.Name = "IntensityColumn";
            this.IntensityColumn.ReadOnly = true;
            this.IntensityColumn.Width = 70;
            // 
            // XRayTypeColumn
            // 
            this.XRayTypeColumn.FillWeight = 50F;
            this.XRayTypeColumn.HeaderText = "X-Ray";
            this.XRayTypeColumn.Name = "XRayTypeColumn";
            this.XRayTypeColumn.ReadOnly = true;
            this.XRayTypeColumn.Width = 50;
            // 
            // DecaModeColumn
            // 
            this.DecaModeColumn.FillWeight = 50F;
            this.DecaModeColumn.HeaderText = "Decay";
            this.DecaModeColumn.Name = "DecaModeColumn";
            this.DecaModeColumn.ReadOnly = true;
            this.DecaModeColumn.Width = 50;
            // 
            // NameColumn
            // 
            this.NameColumn.HeaderText = "Name";
            this.NameColumn.Name = "NameColumn";
            this.NameColumn.ReadOnly = true;
            // 
            // TypeColumn
            // 
            this.TypeColumn.FillWeight = 60F;
            this.TypeColumn.HeaderText = "Type";
            this.TypeColumn.Name = "TypeColumn";
            this.TypeColumn.ReadOnly = true;
            this.TypeColumn.Width = 60;
            // 
            // PercentColum
            // 
            this.PercentColum.FillWeight = 140F;
            this.PercentColum.HeaderText = "%";
            this.PercentColum.Name = "PercentColum";
            this.PercentColum.ReadOnly = true;
            this.PercentColum.Width = 140;
            // 
            // NameColumn1
            // 
            this.NameColumn1.HeaderText = "Name";
            this.NameColumn1.Name = "NameColumn1";
            this.NameColumn1.ReadOnly = true;
            // 
            // TypeColumn1
            // 
            this.TypeColumn1.FillWeight = 60F;
            this.TypeColumn1.HeaderText = "Type";
            this.TypeColumn1.Name = "TypeColumn1";
            this.TypeColumn1.ReadOnly = true;
            this.TypeColumn1.Width = 60;
            // 
            // PercentColumn1
            // 
            this.PercentColumn1.FillWeight = 140F;
            this.PercentColumn1.HeaderText = "%";
            this.PercentColumn1.Name = "PercentColumn1";
            this.PercentColumn1.ReadOnly = true;
            this.PercentColumn1.Width = 140;
            // 
            // NucBase
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(988, 450);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.groupBox1);
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(1004, 489);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(1004, 489);
            this.Name = "NucBase";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Isotope Database";
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
        private System.Windows.Forms.DataGridViewTextBoxColumn NameColumn2;
        private System.Windows.Forms.DataGridViewTextBoxColumn EnTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn EnergyColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn IntensityColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn XRayTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn DecaModeColumn;
        private System.Windows.Forms.ComboBox HalfLifeUOMComboBox;
        private System.Windows.Forms.TextBox HalfLifeTextBox;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.DataGridViewTextBoxColumn NameColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn TypeColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn PercentColumn1;
        private System.Windows.Forms.DataGridViewTextBoxColumn NameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn TypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn PercentColum;
    }
}