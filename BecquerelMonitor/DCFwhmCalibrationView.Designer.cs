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
            this.CollectedPeaksTable = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.positionColumn = new XPTable.Models.TextColumn();
            this.channelColumn = new XPTable.Models.NumberColumn();
            this.energyColumn = new XPTable.Models.NumberColumn();
            this.fwhmColumn = new XPTable.Models.NumberColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.saveToDeviceCfgButton = new System.Windows.Forms.Button();
            this.selectCurveComboBox = new System.Windows.Forms.ComboBox();
            this.minPeaksRequirementLabel = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.addPeakButton = new System.Windows.Forms.Button();
            this.removePeakButton = new System.Windows.Forms.Button();
            this.calibrationProcessingPanel = new System.Windows.Forms.Panel();
            this.label3 = new System.Windows.Forms.Label();
            this.curveFormulaLabel = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.executeCalibrationButton = new System.Windows.Forms.Button();
            this.cancelAddPeakButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.CollectedPeaksTable)).BeginInit();
            this.calibrationProcessingPanel.SuspendLayout();
            this.SuspendLayout();
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
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.positionColumn,
            this.channelColumn,
            this.energyColumn,
            this.fwhmColumn});
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
            this.channelColumn.Editable = false;
            this.channelColumn.IsTextTrimmed = false;
            this.channelColumn.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
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
            this.fwhmColumn.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.fwhmColumn.Sortable = false;
            resources.ApplyResources(this.fwhmColumn, "fwhmColumn");
            // 
            // saveToDeviceCfgButton
            // 
            resources.ApplyResources(this.saveToDeviceCfgButton, "saveToDeviceCfgButton");
            this.saveToDeviceCfgButton.Name = "saveToDeviceCfgButton";
            this.toolTip1.SetToolTip(this.saveToDeviceCfgButton, resources.GetString("saveToDeviceCfgButton.ToolTip"));
            this.saveToDeviceCfgButton.UseVisualStyleBackColor = true;
            this.saveToDeviceCfgButton.Click += new System.EventHandler(this.saveToDeviceCfgButton_Click);
            // 
            // selectCurveComboBox
            // 
            this.selectCurveComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.selectCurveComboBox.FormattingEnabled = true;
            this.selectCurveComboBox.Items.AddRange(new object[] {
            resources.GetString("selectCurveComboBox.Items"),
            resources.GetString("selectCurveComboBox.Items1")});
            resources.ApplyResources(this.selectCurveComboBox, "selectCurveComboBox");
            this.selectCurveComboBox.Name = "selectCurveComboBox";
            this.toolTip1.SetToolTip(this.selectCurveComboBox, resources.GetString("selectCurveComboBox.ToolTip"));
            this.selectCurveComboBox.SelectedIndexChanged += new System.EventHandler(this.selectCurveComboBox_SelectedIndexChanged);
            // 
            // minPeaksRequirementLabel
            // 
            resources.ApplyResources(this.minPeaksRequirementLabel, "minPeaksRequirementLabel");
            this.minPeaksRequirementLabel.Name = "minPeaksRequirementLabel";
            this.toolTip1.SetToolTip(this.minPeaksRequirementLabel, resources.GetString("minPeaksRequirementLabel.ToolTip"));
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // addPeakButton
            // 
            resources.ApplyResources(this.addPeakButton, "addPeakButton");
            this.addPeakButton.Name = "addPeakButton";
            this.addPeakButton.UseVisualStyleBackColor = true;
            this.addPeakButton.Click += new System.EventHandler(this.addPeakButton_Click);
            // 
            // removePeakButton
            // 
            resources.ApplyResources(this.removePeakButton, "removePeakButton");
            this.removePeakButton.Name = "removePeakButton";
            this.removePeakButton.UseVisualStyleBackColor = true;
            this.removePeakButton.Click += new System.EventHandler(this.removePeakButton_Click);
            // 
            // calibrationProcessingPanel
            // 
            resources.ApplyResources(this.calibrationProcessingPanel, "calibrationProcessingPanel");
            this.calibrationProcessingPanel.Controls.Add(this.minPeaksRequirementLabel);
            this.calibrationProcessingPanel.Controls.Add(this.label3);
            this.calibrationProcessingPanel.Controls.Add(this.curveFormulaLabel);
            this.calibrationProcessingPanel.Controls.Add(this.label2);
            this.calibrationProcessingPanel.Controls.Add(this.selectCurveComboBox);
            this.calibrationProcessingPanel.Controls.Add(this.saveToDeviceCfgButton);
            this.calibrationProcessingPanel.Controls.Add(this.executeCalibrationButton);
            this.calibrationProcessingPanel.Name = "calibrationProcessingPanel";
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
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // executeCalibrationButton
            // 
            resources.ApplyResources(this.executeCalibrationButton, "executeCalibrationButton");
            this.executeCalibrationButton.Name = "executeCalibrationButton";
            this.executeCalibrationButton.UseVisualStyleBackColor = true;
            this.executeCalibrationButton.Click += new System.EventHandler(this.executeCalibrationButton_Click);
            // 
            // cancelAddPeakButton
            // 
            resources.ApplyResources(this.cancelAddPeakButton, "cancelAddPeakButton");
            this.cancelAddPeakButton.Name = "cancelAddPeakButton";
            this.cancelAddPeakButton.UseVisualStyleBackColor = true;
            this.cancelAddPeakButton.Click += new System.EventHandler(this.cancelAddPeakButton_Click);
            // 
            // DCFwhmCalibrationView
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.cancelAddPeakButton);
            this.Controls.Add(this.calibrationProcessingPanel);
            this.Controls.Add(this.removePeakButton);
            this.Controls.Add(this.addPeakButton);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.CollectedPeaksTable);
            this.HideOnClose = true;
            this.Name = "DCFwhmCalibrationView";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.DCFwhmCalibrationView_FormClosing);
            this.Load += new System.EventHandler(this.DCFwhmCalibrationView_FormLoad);
            ((System.ComponentModel.ISupportInitialize)(this.CollectedPeaksTable)).EndInit();
            this.calibrationProcessingPanel.ResumeLayout(false);
            this.calibrationProcessingPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private XPTable.Models.Table CollectedPeaksTable;
        private XPTable.Models.TableModel tableModel1;
        private XPTable.Models.ColumnModel columnModel1;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.Label label1;
        private XPTable.Models.NumberColumn channelColumn;
        private XPTable.Models.TextColumn positionColumn;
        private XPTable.Models.NumberColumn energyColumn;
        private XPTable.Models.NumberColumn fwhmColumn;
        private System.Windows.Forms.Button addPeakButton;
        private System.Windows.Forms.Button removePeakButton;
        private System.Windows.Forms.Panel calibrationProcessingPanel;
        private System.Windows.Forms.Button saveToDeviceCfgButton;
        private System.Windows.Forms.Button executeCalibrationButton;
        private System.Windows.Forms.ComboBox selectCurveComboBox;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label curveFormulaLabel;
        private System.Windows.Forms.Label minPeaksRequirementLabel;
        private System.Windows.Forms.Button cancelAddPeakButton;
    }
}
