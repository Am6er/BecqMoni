namespace BecquerelMonitor
{
    public partial class DCFwhmCalibrationView : BecquerelMonitor.ToolWindow
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCFwhmCalibrationView));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox15 = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.offsetLabel = new System.Windows.Forms.Label();
            this.coeff1Label = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.numericUpDown1 = new System.Windows.Forms.TextBox();
            this.numericUpDown2 = new System.Windows.Forms.TextBox();
            this.numericUpDown3 = new System.Windows.Forms.TextBox();
            this.numericUpDown4 = new System.Windows.Forms.TextBox();
            this.numericUpDown5 = new System.Windows.Forms.TextBox();
            this.tractCurvatureLabel = new System.Windows.Forms.Label();
            this.tractCurvatureTextBox = new System.Windows.Forms.TextBox();
            this.tractCurvatureSourceLabel = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.viewEnergyGraphButton = new System.Windows.Forms.Button();
            this.getAllPeaksButton = new System.Windows.Forms.Button();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.label37 = new System.Windows.Forms.Label();
            this.numericUpDown6 = new InvariantNumericUpDown();
            this.label36 = new System.Windows.Forms.Label();
            this.cancelAddPeakButton = new System.Windows.Forms.Button();
            this.executeCalibrationButton = new System.Windows.Forms.Button();
            this.CollectedPeaksTable = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.positionColumn = new XPTable.Models.TextColumn();
            this.channelColumn = new XPTable.Models.NumberColumn();
            this.energyColumn = new XPTable.Models.NumberColumn();
            this.fwhmColumn = new XPTable.Models.TextColumn();
            this.weightColumn = new XPTable.Models.NumberColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            this.addPeakButton = new System.Windows.Forms.Button();
            this.removePeakButton = new System.Windows.Forms.Button();
            this.calibrationProcessingPanel = new System.Windows.Forms.Panel();
            this.label2 = new System.Windows.Forms.Label();
            this.selectCurveComboBox = new System.Windows.Forms.ComboBox();
            this.viewCalibrationButton = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.curveFormulaLabel = new System.Windows.Forms.Label();
            this.minPeaksRequirementLabel = new System.Windows.Forms.Label();
            this.peakShapeInfoGroupBox = new System.Windows.Forms.GroupBox();
            this.viewPeakShapeButton = new System.Windows.Forms.Button();
            this.peakShapeSecondParameterValueLabel = new System.Windows.Forms.Label();
            this.peakShapeFirstParameterValueLabel = new System.Windows.Forms.Label();
            this.peakShapeSecondParameterLabel = new System.Windows.Forms.Label();
            this.peakShapeFirstParameterLabel = new System.Windows.Forms.Label();
            this.peakShapeTypeValueLabel = new System.Windows.Forms.Label();
            this.peakShapeTypeLabel = new System.Windows.Forms.Label();
            this.saveToDeviceCfgButton = new System.Windows.Forms.Button();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.groupBox1.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown6)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.CollectedPeaksTable)).BeginInit();
            this.calibrationProcessingPanel.SuspendLayout();
            this.peakShapeInfoGroupBox.SuspendLayout();
            this.SuspendLayout();
            //
            // label1
            //
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
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
            // groupBox1
            //
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.label7);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.offsetLabel);
            this.groupBox1.Controls.Add(this.coeff1Label);
            this.groupBox1.Controls.Add(this.button1);
            this.groupBox1.Controls.Add(this.numericUpDown1);
            this.groupBox1.Controls.Add(this.numericUpDown2);
            this.groupBox1.Controls.Add(this.numericUpDown3);
            this.groupBox1.Controls.Add(this.numericUpDown4);
            this.groupBox1.Controls.Add(this.numericUpDown5);
            this.groupBox1.Controls.Add(this.tractCurvatureLabel);
            this.groupBox1.Controls.Add(this.tractCurvatureTextBox);
            this.groupBox1.Controls.Add(this.tractCurvatureSourceLabel);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
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
            // label4
            //
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            //
            // offsetLabel
            //
            resources.ApplyResources(this.offsetLabel, "offsetLabel");
            this.offsetLabel.Name = "offsetLabel";
            //
            // coeff1Label
            //
            resources.ApplyResources(this.coeff1Label, "coeff1Label");
            this.coeff1Label.Name = "coeff1Label";
            //
            // button1
            //
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.toolTip1.SetToolTip(this.button1, resources.GetString("button1.ToolTip"));
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
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
            // numericUpDown3
            //
            resources.ApplyResources(this.numericUpDown3, "numericUpDown3");
            this.numericUpDown3.Name = "numericUpDown3";
            this.numericUpDown3.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown3_KeyDown);
            this.numericUpDown3.Leave += new System.EventHandler(this.numericUpDown3_Leave);
            //
            // numericUpDown4
            //
            resources.ApplyResources(this.numericUpDown4, "numericUpDown4");
            this.numericUpDown4.Name = "numericUpDown4";
            this.numericUpDown4.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown4_KeyDown);
            this.numericUpDown4.Leave += new System.EventHandler(this.numericUpDown4_Leave);
            //
            // numericUpDown5
            //
            resources.ApplyResources(this.numericUpDown5, "numericUpDown5");
            this.numericUpDown5.Name = "numericUpDown5";
            this.numericUpDown5.KeyDown += new System.Windows.Forms.KeyEventHandler(this.numericUpDown5_KeyDown);
            this.numericUpDown5.Leave += new System.EventHandler(this.numericUpDown5_Leave);
            //
            // tractCurvatureLabel
            //
            resources.ApplyResources(this.tractCurvatureLabel, "tractCurvatureLabel");
            this.tractCurvatureLabel.Name = "tractCurvatureLabel";
            this.toolTip1.SetToolTip(this.tractCurvatureLabel, resources.GetString("tractCurvatureLabel.ToolTip"));
            //
            // tractCurvatureTextBox
            //
            resources.ApplyResources(this.tractCurvatureTextBox, "tractCurvatureTextBox");
            this.tractCurvatureTextBox.Name = "tractCurvatureTextBox";
            this.toolTip1.SetToolTip(this.tractCurvatureTextBox, resources.GetString("tractCurvatureTextBox.ToolTip"));
            this.tractCurvatureTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.tractCurvatureTextBox_KeyDown);
            this.tractCurvatureTextBox.Leave += new System.EventHandler(this.tractCurvatureTextBox_Leave);
            //
            // tractCurvatureSourceLabel
            //
            resources.ApplyResources(this.tractCurvatureSourceLabel, "tractCurvatureSourceLabel");
            this.tractCurvatureSourceLabel.Name = "tractCurvatureSourceLabel";
            //
            // panel1
            //
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panel1.Controls.Add(this.viewEnergyGraphButton);
            this.panel1.Controls.Add(this.getAllPeaksButton);
            this.panel1.Controls.Add(this.checkBox2);
            this.panel1.Controls.Add(this.label37);
            this.panel1.Controls.Add(this.numericUpDown6);
            this.panel1.Controls.Add(this.label36);
            this.panel1.Controls.Add(this.cancelAddPeakButton);
            this.panel1.Controls.Add(this.executeCalibrationButton);
            this.panel1.Controls.Add(this.CollectedPeaksTable);
            this.panel1.Controls.Add(this.addPeakButton);
            this.panel1.Controls.Add(this.removePeakButton);
            this.panel1.Name = "panel1";
            //
            // viewEnergyGraphButton
            //
            resources.ApplyResources(this.viewEnergyGraphButton, "viewEnergyGraphButton");
            this.viewEnergyGraphButton.Name = "viewEnergyGraphButton";
            this.toolTip1.SetToolTip(this.viewEnergyGraphButton, resources.GetString("viewEnergyGraphButton.ToolTip"));
            this.viewEnergyGraphButton.UseVisualStyleBackColor = true;
            this.viewEnergyGraphButton.Click += new System.EventHandler(this.ViewEnergyGraphButton_Click);
            //
            // getAllPeaksButton
            //
            resources.ApplyResources(this.getAllPeaksButton, "getAllPeaksButton");
            this.getAllPeaksButton.Name = "getAllPeaksButton";
            this.toolTip1.SetToolTip(this.getAllPeaksButton, resources.GetString("getAllPeaksButton.ToolTip"));
            this.getAllPeaksButton.UseVisualStyleBackColor = true;
            this.getAllPeaksButton.Click += new System.EventHandler(this.GetAllPeaksButton_Click);
            //
            // checkBox2
            //
            resources.ApplyResources(this.checkBox2, "checkBox2");
            this.checkBox2.Name = "checkBox2";
            this.toolTip1.SetToolTip(this.checkBox2, resources.GetString("checkBox2.ToolTip"));
            this.checkBox2.UseVisualStyleBackColor = true;
            this.checkBox2.Click += new System.EventHandler(this.checkBox2_Click);
            //
            // label37
            //
            resources.ApplyResources(this.label37, "label37");
            this.label37.Name = "label37";
            //
            // numericUpDown6
            //
            resources.ApplyResources(this.numericUpDown6, "numericUpDown6");
            this.numericUpDown6.Maximum = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numericUpDown6.Name = "numericUpDown6";
            this.toolTip1.SetToolTip(this.numericUpDown6, resources.GetString("numericUpDown6.ToolTip"));
            this.numericUpDown6.ValueChanged += new System.EventHandler(this.numericUpDown6_ValueChanged);
            //
            // label36
            //
            resources.ApplyResources(this.label36, "label36");
            this.label36.Name = "label36";
            //
            // cancelAddPeakButton
            //
            resources.ApplyResources(this.cancelAddPeakButton, "cancelAddPeakButton");
            this.cancelAddPeakButton.Name = "cancelAddPeakButton";
            this.cancelAddPeakButton.UseVisualStyleBackColor = true;
            this.cancelAddPeakButton.Click += new System.EventHandler(this.CancelAddPeakButton_Click);
            //
            // executeCalibrationButton
            //
            resources.ApplyResources(this.executeCalibrationButton, "executeCalibrationButton");
            this.executeCalibrationButton.Name = "executeCalibrationButton";
            this.toolTip1.SetToolTip(this.executeCalibrationButton, resources.GetString("executeCalibrationButton.ToolTip"));
            this.executeCalibrationButton.UseVisualStyleBackColor = true;
            this.executeCalibrationButton.Click += new System.EventHandler(this.ExecuteCalibrationButton_Click);
            //
            // CollectedPeaksTable
            //
            resources.ApplyResources(this.CollectedPeaksTable, "CollectedPeaksTable");
            this.CollectedPeaksTable.BorderColor = System.Drawing.Color.Black;
            this.CollectedPeaksTable.ColumnModel = this.columnModel1;
            this.CollectedPeaksTable.DataMember = null;
            this.CollectedPeaksTable.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.CollectedPeaksTable.DragDropRenderer = dragDropRenderer1;
            this.CollectedPeaksTable.FullRowSelect = true;
            this.CollectedPeaksTable.GridLines = XPTable.Models.GridLines.Both;
            this.CollectedPeaksTable.GridLinesContrainedToData = false;
            this.CollectedPeaksTable.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 9F);
            this.CollectedPeaksTable.Name = "CollectedPeaksTable";
            this.CollectedPeaksTable.TableModel = this.tableModel1;
            this.CollectedPeaksTable.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.CollectedPeaksTable.EditingStopped += new XPTable.Events.CellEditEventHandler(this.CollectedPeaksTable_EditingStopped);
            //
            // columnModel1
            //
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.positionColumn,
            this.channelColumn,
            this.energyColumn,
            this.fwhmColumn,
            this.weightColumn});
            //
            // positionColumn
            //
            this.positionColumn.Editable = false;
            this.positionColumn.IsTextTrimmed = false;
            this.positionColumn.Sortable = false;
            resources.ApplyResources(this.positionColumn, "positionColumn");
            //
            // channelColumn
            //
            this.channelColumn.IsTextTrimmed = false;
            this.channelColumn.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.channelColumn.Sortable = false;
            resources.ApplyResources(this.channelColumn, "channelColumn");
            //
            // energyColumn
            //
            this.energyColumn.IsTextTrimmed = false;
            this.energyColumn.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.energyColumn.Minimum = new decimal(new int[] {
            10000,
            0,
            0,
            -2147483648});
            this.energyColumn.Sortable = false;
            resources.ApplyResources(this.energyColumn, "energyColumn");
            //
            // fwhmColumn
            //
            this.fwhmColumn.IsTextTrimmed = false;
            this.fwhmColumn.Sortable = false;
            resources.ApplyResources(this.fwhmColumn, "fwhmColumn");
            //
            // weightColumn
            //
            this.weightColumn.IsTextTrimmed = false;
            this.weightColumn.Maximum = new decimal(new int[] {
            10000000,
            0,
            0,
            0});
            this.weightColumn.Sortable = false;
            resources.ApplyResources(this.weightColumn, "weightColumn");
            //
            // addPeakButton
            //
            resources.ApplyResources(this.addPeakButton, "addPeakButton");
            this.addPeakButton.Name = "addPeakButton";
            this.toolTip1.SetToolTip(this.addPeakButton, resources.GetString("addPeakButton.ToolTip"));
            this.addPeakButton.UseVisualStyleBackColor = true;
            this.addPeakButton.Click += new System.EventHandler(this.AddPeakButton_Click);
            //
            // removePeakButton
            //
            resources.ApplyResources(this.removePeakButton, "removePeakButton");
            this.removePeakButton.Name = "removePeakButton";
            this.removePeakButton.UseVisualStyleBackColor = true;
            this.removePeakButton.Click += new System.EventHandler(this.RemovePeakButton_Click);
            //
            // calibrationProcessingPanel
            //
            resources.ApplyResources(this.calibrationProcessingPanel, "calibrationProcessingPanel");
            this.calibrationProcessingPanel.Controls.Add(this.label2);
            this.calibrationProcessingPanel.Controls.Add(this.selectCurveComboBox);
            this.calibrationProcessingPanel.Controls.Add(this.viewCalibrationButton);
            this.calibrationProcessingPanel.Controls.Add(this.label3);
            this.calibrationProcessingPanel.Controls.Add(this.curveFormulaLabel);
            this.calibrationProcessingPanel.Controls.Add(this.minPeaksRequirementLabel);
            this.calibrationProcessingPanel.Controls.Add(this.peakShapeInfoGroupBox);
            this.calibrationProcessingPanel.Name = "calibrationProcessingPanel";
            //
            // label2
            //
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            //
            // selectCurveComboBox
            //
            this.selectCurveComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.selectCurveComboBox.FormattingEnabled = true;
            this.selectCurveComboBox.Items.AddRange(new object[] {
            resources.GetString("selectCurveComboBox.Items"),
            resources.GetString("selectCurveComboBox.Items1"),
            resources.GetString("selectCurveComboBox.Items2")});
            resources.ApplyResources(this.selectCurveComboBox, "selectCurveComboBox");
            this.selectCurveComboBox.Name = "selectCurveComboBox";
            this.toolTip1.SetToolTip(this.selectCurveComboBox, resources.GetString("selectCurveComboBox.ToolTip"));
            this.selectCurveComboBox.SelectedIndexChanged += new System.EventHandler(this.SelectCurveComboBox_SelectedIndexChanged);
            //
            // viewCalibrationButton
            //
            resources.ApplyResources(this.viewCalibrationButton, "viewCalibrationButton");
            this.viewCalibrationButton.Name = "viewCalibrationButton";
            this.toolTip1.SetToolTip(this.viewCalibrationButton, resources.GetString("viewCalibrationButton.ToolTip"));
            this.viewCalibrationButton.UseVisualStyleBackColor = true;
            this.viewCalibrationButton.Click += new System.EventHandler(this.ViewCalibrationButton_Click);
            //
            // label3
            //
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            //
            // curveFormulaLabel
            //
            resources.ApplyResources(this.curveFormulaLabel, "curveFormulaLabel");
            this.curveFormulaLabel.Name = "curveFormulaLabel";
            //
            // minPeaksRequirementLabel
            //
            resources.ApplyResources(this.minPeaksRequirementLabel, "minPeaksRequirementLabel");
            this.minPeaksRequirementLabel.Name = "minPeaksRequirementLabel";
            this.toolTip1.SetToolTip(this.minPeaksRequirementLabel, resources.GetString("minPeaksRequirementLabel.ToolTip"));
            //
            // peakShapeInfoGroupBox
            //
            resources.ApplyResources(this.peakShapeInfoGroupBox, "peakShapeInfoGroupBox");
            this.peakShapeInfoGroupBox.Controls.Add(this.viewPeakShapeButton);
            this.peakShapeInfoGroupBox.Controls.Add(this.peakShapeSecondParameterValueLabel);
            this.peakShapeInfoGroupBox.Controls.Add(this.peakShapeFirstParameterValueLabel);
            this.peakShapeInfoGroupBox.Controls.Add(this.peakShapeSecondParameterLabel);
            this.peakShapeInfoGroupBox.Controls.Add(this.peakShapeFirstParameterLabel);
            this.peakShapeInfoGroupBox.Controls.Add(this.peakShapeTypeValueLabel);
            this.peakShapeInfoGroupBox.Controls.Add(this.peakShapeTypeLabel);
            this.peakShapeInfoGroupBox.Name = "peakShapeInfoGroupBox";
            this.peakShapeInfoGroupBox.TabStop = false;
            //
            // viewPeakShapeButton
            //
            resources.ApplyResources(this.viewPeakShapeButton, "viewPeakShapeButton");
            this.viewPeakShapeButton.Name = "viewPeakShapeButton";
            this.viewPeakShapeButton.UseVisualStyleBackColor = true;
            this.viewPeakShapeButton.Click += new System.EventHandler(this.ViewPeakShapeButton_Click);
            //
            // peakShapeSecondParameterValueLabel
            //
            resources.ApplyResources(this.peakShapeSecondParameterValueLabel, "peakShapeSecondParameterValueLabel");
            this.peakShapeSecondParameterValueLabel.Name = "peakShapeSecondParameterValueLabel";
            //
            // peakShapeFirstParameterValueLabel
            //
            resources.ApplyResources(this.peakShapeFirstParameterValueLabel, "peakShapeFirstParameterValueLabel");
            this.peakShapeFirstParameterValueLabel.Name = "peakShapeFirstParameterValueLabel";
            //
            // peakShapeSecondParameterLabel
            //
            resources.ApplyResources(this.peakShapeSecondParameterLabel, "peakShapeSecondParameterLabel");
            this.peakShapeSecondParameterLabel.Name = "peakShapeSecondParameterLabel";
            //
            // peakShapeFirstParameterLabel
            //
            resources.ApplyResources(this.peakShapeFirstParameterLabel, "peakShapeFirstParameterLabel");
            this.peakShapeFirstParameterLabel.Name = "peakShapeFirstParameterLabel";
            //
            // peakShapeTypeValueLabel
            //
            resources.ApplyResources(this.peakShapeTypeValueLabel, "peakShapeTypeValueLabel");
            this.peakShapeTypeValueLabel.Name = "peakShapeTypeValueLabel";
            //
            // peakShapeTypeLabel
            //
            resources.ApplyResources(this.peakShapeTypeLabel, "peakShapeTypeLabel");
            this.peakShapeTypeLabel.Name = "peakShapeTypeLabel";
            //
            // saveToDeviceCfgButton
            //
            resources.ApplyResources(this.saveToDeviceCfgButton, "saveToDeviceCfgButton");
            this.saveToDeviceCfgButton.Name = "saveToDeviceCfgButton";
            this.toolTip1.SetToolTip(this.saveToDeviceCfgButton, resources.GetString("saveToDeviceCfgButton.ToolTip"));
            this.saveToDeviceCfgButton.UseVisualStyleBackColor = true;
            this.saveToDeviceCfgButton.Click += new System.EventHandler(this.SaveToDeviceCfgButton_Click);
            //
            // DCFwhmCalibrationView
            //
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBox15);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.calibrationProcessingPanel);
            this.Controls.Add(this.saveToDeviceCfgButton);
            this.HideOnClose = true;
            this.Name = "DCFwhmCalibrationView";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.DCFwhmCalibrationView_FormClosing);
            this.Load += new System.EventHandler(this.DCFwhmCalibrationView_FormLoad);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown6)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.CollectedPeaksTable)).EndInit();
            this.calibrationProcessingPanel.ResumeLayout(false);
            this.calibrationProcessingPanel.PerformLayout();
            this.peakShapeInfoGroupBox.ResumeLayout(false);
            this.peakShapeInfoGroupBox.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textBox15;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label offsetLabel;
        private System.Windows.Forms.Label coeff1Label;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.TextBox numericUpDown1;
        private System.Windows.Forms.TextBox numericUpDown2;
        private System.Windows.Forms.TextBox numericUpDown3;
        private System.Windows.Forms.TextBox numericUpDown4;
        private System.Windows.Forms.TextBox numericUpDown5;
        private System.Windows.Forms.Label tractCurvatureLabel;
        private System.Windows.Forms.TextBox tractCurvatureTextBox;
        private System.Windows.Forms.Label tractCurvatureSourceLabel;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button viewEnergyGraphButton;
        private System.Windows.Forms.Button getAllPeaksButton;
        private System.Windows.Forms.CheckBox checkBox2;
        private System.Windows.Forms.Label label37;
        private InvariantNumericUpDown numericUpDown6;
        private System.Windows.Forms.Label label36;
        private System.Windows.Forms.Button cancelAddPeakButton;
        private System.Windows.Forms.Button executeCalibrationButton;
        private XPTable.Models.Table CollectedPeaksTable;
        private XPTable.Models.ColumnModel columnModel1;
        private XPTable.Models.TextColumn positionColumn;
        private XPTable.Models.NumberColumn channelColumn;
        private XPTable.Models.NumberColumn energyColumn;
        private XPTable.Models.TextColumn fwhmColumn;
        private XPTable.Models.NumberColumn weightColumn;
        private XPTable.Models.TableModel tableModel1;
        private System.Windows.Forms.Button addPeakButton;
        private System.Windows.Forms.Button removePeakButton;
        private System.Windows.Forms.Panel calibrationProcessingPanel;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox selectCurveComboBox;
        private System.Windows.Forms.Button viewCalibrationButton;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label curveFormulaLabel;
        private System.Windows.Forms.Label minPeaksRequirementLabel;
        private System.Windows.Forms.GroupBox peakShapeInfoGroupBox;
        private System.Windows.Forms.Button viewPeakShapeButton;
        private System.Windows.Forms.Label peakShapeSecondParameterValueLabel;
        private System.Windows.Forms.Label peakShapeFirstParameterValueLabel;
        private System.Windows.Forms.Label peakShapeSecondParameterLabel;
        private System.Windows.Forms.Label peakShapeFirstParameterLabel;
        private System.Windows.Forms.Label peakShapeTypeValueLabel;
        private System.Windows.Forms.Label peakShapeTypeLabel;
        private System.Windows.Forms.Button saveToDeviceCfgButton;
        private System.Windows.Forms.ToolTip toolTip1;
    }
}
