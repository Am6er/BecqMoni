namespace BecquerelMonitor
{
    partial class ROIEditEfficiencyDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ROIEditEfficiencyDialog));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            this.buttonRemoveRow = new System.Windows.Forms.Button();
            this.buttonAddRow = new System.Windows.Forms.Button();
            this.tableEfficiency = new XPTable.Models.Table();
            this.columnModelEfficiency = new XPTable.Models.ColumnModel();
            this.energyColumn = new XPTable.Models.NumberColumn();
            this.efficiencyColumn = new XPTable.Models.NumberColumn();
            this.errorPercentColumn = new XPTable.Models.NumberColumn();
            this.tableModelEfficiency = new XPTable.Models.TableModel();
            ((System.ComponentModel.ISupportInitialize)(this.tableEfficiency)).BeginInit();
            this.SuspendLayout();
            // 
            // buttonRemoveRow
            // 
            resources.ApplyResources(this.buttonRemoveRow, "buttonRemoveRow");
            this.buttonRemoveRow.Name = "buttonRemoveRow";
            this.buttonRemoveRow.UseVisualStyleBackColor = true;
            this.buttonRemoveRow.Click += new System.EventHandler(this.buttonRemoveRow_Click);
            // 
            // buttonAddRow
            // 
            resources.ApplyResources(this.buttonAddRow, "buttonAddRow");
            this.buttonAddRow.Name = "buttonAddRow";
            this.buttonAddRow.UseVisualStyleBackColor = true;
            this.buttonAddRow.Click += new System.EventHandler(this.buttonAddRow_Click);
            // 
            // tableEfficiency
            // 
            resources.ApplyResources(this.tableEfficiency, "tableEfficiency");
            this.tableEfficiency.BorderColor = System.Drawing.Color.Black;
            this.tableEfficiency.ColumnModel = this.columnModelEfficiency;
            this.tableEfficiency.DataMember = null;
            this.tableEfficiency.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.tableEfficiency.DragDropRenderer = dragDropRenderer1;
            this.tableEfficiency.FullRowSelect = true;
            this.tableEfficiency.GridLines = XPTable.Models.GridLines.Both;
            this.tableEfficiency.GridLinesContrainedToData = false;
            this.tableEfficiency.Name = "tableEfficiency";
            this.tableEfficiency.TableModel = this.tableModelEfficiency;
            this.tableEfficiency.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.tableEfficiency.EditingStopped += new XPTable.Events.CellEditEventHandler(this.tableEfficiency_EditingStopped);
            this.tableEfficiency.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.tableEfficiency_SelectionChanged);
            // 
            // columnModelEfficiency
            // 
            this.columnModelEfficiency.Columns.AddRange(new XPTable.Models.Column[] {
            this.energyColumn,
            this.efficiencyColumn,
            this.errorPercentColumn});
            // 
            // energyColumn
            // 
            this.energyColumn.Increment = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.energyColumn.IsTextTrimmed = false;
            this.energyColumn.Maximum = new decimal(new int[] {
            2147483647,
            0,
            0,
            0});
            resources.ApplyResources(this.energyColumn, "energyColumn");
            // 
            // efficiencyColumn
            // 
            this.efficiencyColumn.IsTextTrimmed = false;
            this.efficiencyColumn.Maximum = new decimal(new int[] {
            2147483647,
            0,
            0,
            0});
            resources.ApplyResources(this.efficiencyColumn, "efficiencyColumn");
            // 
            // errorPercentColumn
            // 
            this.errorPercentColumn.IsTextTrimmed = false;
            resources.ApplyResources(this.errorPercentColumn, "errorPercentColumn");
            // 
            // ROIEditEfficiencyDialog
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.buttonAddRow);
            this.Controls.Add(this.buttonRemoveRow);
            this.Controls.Add(this.tableEfficiency);
            this.MinimizeBox = false;
            this.Name = "ROIEditEfficiencyDialog";
            this.ShowInTaskbar = false;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ROIEditEfficiencyDialog_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.tableEfficiency)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private XPTable.Models.Table tableEfficiency;
        private XPTable.Models.ColumnModel columnModelEfficiency;
        private XPTable.Models.TableModel tableModelEfficiency;
        private XPTable.Models.NumberColumn energyColumn;
        private XPTable.Models.NumberColumn efficiencyColumn;
        private XPTable.Models.NumberColumn errorPercentColumn;
        private System.Windows.Forms.Button buttonRemoveRow;
        private System.Windows.Forms.Button buttonAddRow;
    }
}