namespace BecquerelMonitor
{
    partial class PeakDeconvolutionInfoForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PeakDeconvolutionInfoForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            this.dataGridViewDetails = new System.Windows.Forms.DataGridView();
            this.nuclideColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.energyColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.channelColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.snrColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.fwhmColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.devianceImprovementColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.occupancyColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.centerStdDevColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.residualSnrColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.residualCorrelationColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.anchorDistanceColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.supportingChainsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.buttonClose = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewDetails)).BeginInit();
            this.SuspendLayout();
            // 
            // dataGridViewDetails
            // 
            this.dataGridViewDetails.AllowUserToAddRows = false;
            this.dataGridViewDetails.AllowUserToDeleteRows = false;
            this.dataGridViewDetails.AllowUserToResizeRows = false;
            resources.ApplyResources(this.dataGridViewDetails, "dataGridViewDetails");
            this.dataGridViewDetails.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridViewDetails.BackgroundColor = System.Drawing.SystemColors.Window;
            this.dataGridViewDetails.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewDetails.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.nuclideColumn,
            this.energyColumn,
            this.channelColumn,
            this.snrColumn,
            this.fwhmColumn,
            this.devianceImprovementColumn,
            this.occupancyColumn,
            this.centerStdDevColumn,
            this.residualSnrColumn,
            this.residualCorrelationColumn,
            this.anchorDistanceColumn,
            this.supportingChainsColumn});
            this.dataGridViewDetails.MultiSelect = false;
            this.dataGridViewDetails.Name = "dataGridViewDetails";
            this.dataGridViewDetails.ReadOnly = true;
            this.dataGridViewDetails.RowHeadersVisible = false;
            this.dataGridViewDetails.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridViewDetails.ShowEditingIcon = false;
            // 
            // nuclideColumn
            // 
            this.nuclideColumn.FillWeight = 68F;
            resources.ApplyResources(this.nuclideColumn, "nuclideColumn");
            this.nuclideColumn.Name = "nuclideColumn";
            this.nuclideColumn.ReadOnly = true;
            // 
            // energyColumn
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.energyColumn.DefaultCellStyle = dataGridViewCellStyle1;
            this.energyColumn.FillWeight = 65F;
            resources.ApplyResources(this.energyColumn, "energyColumn");
            this.energyColumn.Name = "energyColumn";
            this.energyColumn.ReadOnly = true;
            // 
            // channelColumn
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.channelColumn.DefaultCellStyle = dataGridViewCellStyle2;
            this.channelColumn.FillWeight = 71F;
            resources.ApplyResources(this.channelColumn, "channelColumn");
            this.channelColumn.Name = "channelColumn";
            this.channelColumn.ReadOnly = true;
            // 
            // snrColumn
            // 
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            this.snrColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.snrColumn.FillWeight = 55F;
            resources.ApplyResources(this.snrColumn, "snrColumn");
            this.snrColumn.Name = "snrColumn";
            this.snrColumn.ReadOnly = true;
            // 
            // fwhmColumn
            // 
            this.fwhmColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.fwhmColumn.FillWeight = 66F;
            resources.ApplyResources(this.fwhmColumn, "fwhmColumn");
            this.fwhmColumn.Name = "fwhmColumn";
            this.fwhmColumn.ReadOnly = true;
            // 
            // devianceImprovementColumn
            // 
            this.devianceImprovementColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.devianceImprovementColumn.FillWeight = 58F;
            resources.ApplyResources(this.devianceImprovementColumn, "devianceImprovementColumn");
            this.devianceImprovementColumn.Name = "devianceImprovementColumn";
            this.devianceImprovementColumn.ReadOnly = true;
            // 
            // occupancyColumn
            // 
            this.occupancyColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.occupancyColumn.FillWeight = 52F;
            resources.ApplyResources(this.occupancyColumn, "occupancyColumn");
            this.occupancyColumn.Name = "occupancyColumn";
            this.occupancyColumn.ReadOnly = true;
            // 
            // centerStdDevColumn
            // 
            this.centerStdDevColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.centerStdDevColumn.FillWeight = 80F;
            resources.ApplyResources(this.centerStdDevColumn, "centerStdDevColumn");
            this.centerStdDevColumn.Name = "centerStdDevColumn";
            this.centerStdDevColumn.ReadOnly = true;
            // 
            // residualSnrColumn
            // 
            this.residualSnrColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.residualSnrColumn.FillWeight = 77F;
            resources.ApplyResources(this.residualSnrColumn, "residualSnrColumn");
            this.residualSnrColumn.Name = "residualSnrColumn";
            this.residualSnrColumn.ReadOnly = true;
            // 
            // residualCorrelationColumn
            // 
            this.residualCorrelationColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.residualCorrelationColumn.FillWeight = 72F;
            resources.ApplyResources(this.residualCorrelationColumn, "residualCorrelationColumn");
            this.residualCorrelationColumn.Name = "residualCorrelationColumn";
            this.residualCorrelationColumn.ReadOnly = true;
            // 
            // anchorDistanceColumn
            // 
            this.anchorDistanceColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.anchorDistanceColumn.FillWeight = 85F;
            resources.ApplyResources(this.anchorDistanceColumn, "anchorDistanceColumn");
            this.anchorDistanceColumn.Name = "anchorDistanceColumn";
            this.anchorDistanceColumn.ReadOnly = true;
            // 
            // supportingChainsColumn
            // 
            this.supportingChainsColumn.DefaultCellStyle = dataGridViewCellStyle3;
            this.supportingChainsColumn.FillWeight = 64F;
            resources.ApplyResources(this.supportingChainsColumn, "supportingChainsColumn");
            this.supportingChainsColumn.Name = "supportingChainsColumn";
            this.supportingChainsColumn.ReadOnly = true;
            // 
            // buttonClose
            // 
            resources.ApplyResources(this.buttonClose, "buttonClose");
            this.buttonClose.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.UseVisualStyleBackColor = true;
            // 
            // PeakDeconvolutionInfoForm
            // 
            this.AcceptButton = this.buttonClose;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonClose;
            this.Controls.Add(this.buttonClose);
            this.Controls.Add(this.dataGridViewDetails);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PeakDeconvolutionInfoForm";
            this.ShowInTaskbar = false;
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewDetails)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridViewDetails;
        private System.Windows.Forms.Button buttonClose;
        private System.Windows.Forms.DataGridViewTextBoxColumn nuclideColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn energyColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn channelColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn snrColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn fwhmColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn devianceImprovementColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn occupancyColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn centerStdDevColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn residualSnrColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn residualCorrelationColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn anchorDistanceColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn supportingChainsColumn;
    }
}
