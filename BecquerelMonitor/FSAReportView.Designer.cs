namespace BecquerelMonitor
{
    public partial class FSAReportView : BecquerelMonitor.ToolWindow
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FSAReportView));
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.sourceGroupBox = new System.Windows.Forms.GroupBox();
            this.sourceFlow = new System.Windows.Forms.FlowLayoutPanel();
            this.sourcePeaksRadio = new System.Windows.Forms.RadioButton();
            this.sourceNucBaseRadio = new System.Windows.Forms.RadioButton();
            this.displayGroupBox = new System.Windows.Forms.GroupBox();
            this.displayFlow = new System.Windows.Forms.FlowLayoutPanel();
            this.groupingLabel = new System.Windows.Forms.Label();
            this.parentsRadio = new System.Windows.Forms.RadioButton();
            this.daughtersRadio = new System.Windows.Forms.RadioButton();
            this.chainGroupBox = new System.Windows.Forms.GroupBox();
            this.chainFlow = new System.Windows.Forms.FlowLayoutPanel();
            this.equilibriumCheckBox = new System.Windows.Forms.CheckBox();
            this.extrasGroupBox = new System.Windows.Forms.GroupBox();
            this.extrasFlow = new System.Windows.Forms.FlowLayoutPanel();
            this.atomicXrayCheckBox = new System.Windows.Forms.CheckBox();
            this.cascadeSummingCheckBox = new System.Windows.Forms.CheckBox();
            this.backscatterCheckBox = new System.Windows.Forms.CheckBox();
            this.escapeCheckBox = new System.Windows.Forms.CheckBox();
            this.pileUpCheckBox = new System.Windows.Forms.CheckBox();
            this.residualBandCheckBox = new System.Windows.Forms.CheckBox();
            this.reportTable = new XPTable.Models.Table();
            this.columnModel = new XPTable.Models.ColumnModel();
            this.swatchColumn = new XPTable.Models.ImageColumn();
            this.componentColumn = new XPTable.Models.TextColumn();
            this.valueColumn = new XPTable.Models.TextColumn();
            this.tableModel = new XPTable.Models.TableModel();
            this.sourceGroupBox.SuspendLayout();
            this.sourceFlow.SuspendLayout();
            this.displayGroupBox.SuspendLayout();
            this.displayFlow.SuspendLayout();
            this.chainGroupBox.SuspendLayout();
            this.chainFlow.SuspendLayout();
            this.extrasGroupBox.SuspendLayout();
            this.extrasFlow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.reportTable)).BeginInit();
            this.SuspendLayout();
            //
            // sourceGroupBox
            //
            resources.ApplyResources(this.sourceGroupBox, "sourceGroupBox");
            this.sourceGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.sourceGroupBox.Controls.Add(this.sourceFlow);
            this.sourceGroupBox.Name = "sourceGroupBox";
            this.sourceGroupBox.TabStop = false;
            //
            // sourceFlow
            //
            resources.ApplyResources(this.sourceFlow, "sourceFlow");
            this.sourceFlow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.sourceFlow.Controls.Add(this.sourcePeaksRadio);
            this.sourceFlow.Controls.Add(this.sourceNucBaseRadio);
            this.sourceFlow.Name = "sourceFlow";
            //
            // sourcePeaksRadio
            //
            resources.ApplyResources(this.sourcePeaksRadio, "sourcePeaksRadio");
            this.sourcePeaksRadio.Checked = true;
            this.sourcePeaksRadio.Name = "sourcePeaksRadio";
            this.sourcePeaksRadio.TabStop = true;
            this.sourcePeaksRadio.UseVisualStyleBackColor = true;
            this.sourcePeaksRadio.CheckedChanged += new System.EventHandler(this.sourceRadio_CheckedChanged);
            //
            // sourceNucBaseRadio
            //
            resources.ApplyResources(this.sourceNucBaseRadio, "sourceNucBaseRadio");
            this.sourceNucBaseRadio.Name = "sourceNucBaseRadio";
            this.sourceNucBaseRadio.UseVisualStyleBackColor = true;
            this.sourceNucBaseRadio.CheckedChanged += new System.EventHandler(this.sourceRadio_CheckedChanged);
            //
            // displayGroupBox
            //
            // (`A265`) ГРУППА ОТРИСОВКИ. Здесь лежит всё, что правит только
            // ПОКАЗ и до счёта не доходит: группировка родители/дочерние и
            // лента невязки. Расчётное — в трёх группах выше, и ни одна
            // галка не стоит в чужом роде. Граница между двумя родами
            // переключателей обязана быть видна БЕЗ НАВЕДЕНИЯ (решение
            // Amber 06.09.2026) — потому она проведена рамкой группы, а не
            // одной лишь подсказкой, как было до 06.09.2026.
            //
            resources.ApplyResources(this.displayGroupBox, "displayGroupBox");
            this.displayGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.displayGroupBox.Controls.Add(this.displayFlow);
            this.displayGroupBox.Name = "displayGroupBox";
            this.displayGroupBox.TabStop = false;
            //
            // displayFlow
            //
            resources.ApplyResources(this.displayFlow, "displayFlow");
            this.displayFlow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.displayFlow.Controls.Add(this.groupingLabel);
            this.displayFlow.Controls.Add(this.parentsRadio);
            this.displayFlow.Controls.Add(this.daughtersRadio);
            this.displayFlow.Controls.Add(this.residualBandCheckBox);
            this.displayFlow.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.displayFlow.Name = "displayFlow";
            this.displayFlow.WrapContents = false;
            //
            // groupingLabel
            //
            resources.ApplyResources(this.groupingLabel, "groupingLabel");
            this.groupingLabel.Name = "groupingLabel";
            //
            // parentsRadio
            //
            resources.ApplyResources(this.parentsRadio, "parentsRadio");
            this.parentsRadio.Name = "parentsRadio";
            this.parentsRadio.UseVisualStyleBackColor = true;
            this.parentsRadio.CheckedChanged += new System.EventHandler(this.groupingRadio_CheckedChanged);
            //
            // daughtersRadio
            //
            resources.ApplyResources(this.daughtersRadio, "daughtersRadio");
            this.daughtersRadio.Checked = true;
            this.daughtersRadio.Name = "daughtersRadio";
            this.daughtersRadio.TabStop = true;
            this.daughtersRadio.UseVisualStyleBackColor = true;
            this.daughtersRadio.CheckedChanged += new System.EventHandler(this.groupingRadio_CheckedChanged);
            //
            // chainGroupBox
            //
            resources.ApplyResources(this.chainGroupBox, "chainGroupBox");
            this.chainGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.chainGroupBox.Controls.Add(this.chainFlow);
            this.chainGroupBox.Name = "chainGroupBox";
            this.chainGroupBox.TabStop = false;
            //
            // chainFlow
            //
            resources.ApplyResources(this.chainFlow, "chainFlow");
            this.chainFlow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.chainFlow.Controls.Add(this.equilibriumCheckBox);
            this.chainFlow.Name = "chainFlow";
            //
            // equilibriumCheckBox
            //
            resources.ApplyResources(this.equilibriumCheckBox, "equilibriumCheckBox");
            this.equilibriumCheckBox.Checked = true;
            this.equilibriumCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.equilibriumCheckBox.Name = "equilibriumCheckBox";
            this.equilibriumCheckBox.UseVisualStyleBackColor = true;
            this.equilibriumCheckBox.CheckedChanged += new System.EventHandler(this.equilibriumCheckBox_CheckedChanged);
            //
            // extrasGroupBox
            //
            resources.ApplyResources(this.extrasGroupBox, "extrasGroupBox");
            this.extrasGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.extrasGroupBox.Controls.Add(this.extrasFlow);
            this.extrasGroupBox.Name = "extrasGroupBox";
            this.extrasGroupBox.TabStop = false;
            //
            // extrasFlow
            //
            resources.ApplyResources(this.extrasFlow, "extrasFlow");
            this.extrasFlow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.extrasFlow.Controls.Add(this.atomicXrayCheckBox);
            this.extrasFlow.Controls.Add(this.cascadeSummingCheckBox);
            this.extrasFlow.Controls.Add(this.backscatterCheckBox);
            this.extrasFlow.Controls.Add(this.escapeCheckBox);
            this.extrasFlow.Controls.Add(this.pileUpCheckBox);
            this.extrasFlow.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.extrasFlow.Name = "extrasFlow";
            this.extrasFlow.WrapContents = false;
            //
            // atomicXrayCheckBox
            //
            resources.ApplyResources(this.atomicXrayCheckBox, "atomicXrayCheckBox");
            this.atomicXrayCheckBox.Checked = true;
            this.atomicXrayCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.atomicXrayCheckBox.Name = "atomicXrayCheckBox";
            this.atomicXrayCheckBox.UseVisualStyleBackColor = true;
            this.atomicXrayCheckBox.CheckedChanged += new System.EventHandler(this.atomicXrayCheckBox_CheckedChanged);
            //
            // cascadeSummingCheckBox
            //
            resources.ApplyResources(this.cascadeSummingCheckBox, "cascadeSummingCheckBox");
            this.cascadeSummingCheckBox.Checked = true;
            this.cascadeSummingCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cascadeSummingCheckBox.Name = "cascadeSummingCheckBox";
            this.cascadeSummingCheckBox.UseVisualStyleBackColor = true;
            this.cascadeSummingCheckBox.CheckedChanged += new System.EventHandler(this.cascadeSummingCheckBox_CheckedChanged);
            //
            // backscatterCheckBox
            //
            resources.ApplyResources(this.backscatterCheckBox, "backscatterCheckBox");
            this.backscatterCheckBox.Checked = true;
            this.backscatterCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.backscatterCheckBox.Name = "backscatterCheckBox";
            this.backscatterCheckBox.UseVisualStyleBackColor = true;
            this.backscatterCheckBox.CheckedChanged += new System.EventHandler(this.backscatterCheckBox_CheckedChanged);
            //
            // escapeCheckBox
            //
            resources.ApplyResources(this.escapeCheckBox, "escapeCheckBox");
            this.escapeCheckBox.Checked = true;
            this.escapeCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.escapeCheckBox.Name = "escapeCheckBox";
            this.escapeCheckBox.UseVisualStyleBackColor = true;
            this.escapeCheckBox.CheckedChanged += new System.EventHandler(this.escapeCheckBox_CheckedChanged);
            //
            // pileUpCheckBox
            //
            resources.ApplyResources(this.pileUpCheckBox, "pileUpCheckBox");
            this.pileUpCheckBox.Checked = true;
            this.pileUpCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.pileUpCheckBox.Name = "pileUpCheckBox";
            this.pileUpCheckBox.UseVisualStyleBackColor = true;
            this.pileUpCheckBox.CheckedChanged += new System.EventHandler(this.pileUpCheckBox_CheckedChanged);
            //
            // residualBandCheckBox
            //
            resources.ApplyResources(this.residualBandCheckBox, "residualBandCheckBox");
            this.residualBandCheckBox.Checked = true;
            this.residualBandCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.residualBandCheckBox.Name = "residualBandCheckBox";
            this.residualBandCheckBox.UseVisualStyleBackColor = true;
            this.residualBandCheckBox.CheckedChanged += new System.EventHandler(this.residualBandCheckBox_CheckedChanged);
            //
            // reportTable
            //
            resources.ApplyResources(this.reportTable, "reportTable");
            this.reportTable.BorderColor = System.Drawing.Color.Black;
            this.reportTable.ColumnModel = this.columnModel;
            this.reportTable.DataMember = null;
            this.reportTable.FullRowSelect = true;
            this.reportTable.GridLinesContrainedToData = false;
            this.reportTable.Name = "reportTable";
            this.reportTable.TableModel = this.tableModel;
            this.reportTable.UnfocusedBorderColor = System.Drawing.Color.Black;
            //
            // columnModel
            //
            this.columnModel.Columns.AddRange(new XPTable.Models.Column[] {
            this.swatchColumn,
            this.componentColumn,
            this.valueColumn});
            //
            // swatchColumn
            //
            this.swatchColumn.DrawText = false;
            this.swatchColumn.Editable = false;
            this.swatchColumn.Resizable = false;
            this.swatchColumn.Sortable = false;
            resources.ApplyResources(this.swatchColumn, "swatchColumn");
            //
            // componentColumn
            //
            this.componentColumn.Editable = false;
            this.componentColumn.IsTextTrimmed = false;
            this.componentColumn.Sortable = false;
            resources.ApplyResources(this.componentColumn, "componentColumn");
            //
            // valueColumn
            //
            this.valueColumn.Editable = false;
            this.valueColumn.IsTextTrimmed = false;
            this.valueColumn.Resizable = false;
            this.valueColumn.Sortable = false;
            resources.ApplyResources(this.valueColumn, "valueColumn");
            //
            // FSAReportView
            //
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.reportTable);
            this.Controls.Add(this.displayGroupBox);
            this.Controls.Add(this.extrasGroupBox);
            this.Controls.Add(this.chainGroupBox);
            this.Controls.Add(this.sourceGroupBox);
            this.HideOnClose = true;
            this.Name = "FSAReportView";
            this.sourceGroupBox.ResumeLayout(false);
            this.sourceGroupBox.PerformLayout();
            this.sourceFlow.ResumeLayout(false);
            this.sourceFlow.PerformLayout();
            this.displayGroupBox.ResumeLayout(false);
            this.displayGroupBox.PerformLayout();
            this.displayFlow.ResumeLayout(false);
            this.displayFlow.PerformLayout();
            this.chainGroupBox.ResumeLayout(false);
            this.chainGroupBox.PerformLayout();
            this.chainFlow.ResumeLayout(false);
            this.chainFlow.PerformLayout();
            this.extrasGroupBox.ResumeLayout(false);
            this.extrasGroupBox.PerformLayout();
            this.extrasFlow.ResumeLayout(false);
            this.extrasFlow.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.reportTable)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.GroupBox sourceGroupBox;
        private System.Windows.Forms.FlowLayoutPanel sourceFlow;
        private System.Windows.Forms.RadioButton sourcePeaksRadio;
        private System.Windows.Forms.RadioButton sourceNucBaseRadio;
        private System.Windows.Forms.GroupBox displayGroupBox;
        private System.Windows.Forms.FlowLayoutPanel displayFlow;
        private System.Windows.Forms.Label groupingLabel;
        private System.Windows.Forms.RadioButton parentsRadio;
        private System.Windows.Forms.RadioButton daughtersRadio;
        private System.Windows.Forms.GroupBox chainGroupBox;
        private System.Windows.Forms.FlowLayoutPanel chainFlow;
        private System.Windows.Forms.CheckBox equilibriumCheckBox;
        private System.Windows.Forms.GroupBox extrasGroupBox;
        private System.Windows.Forms.FlowLayoutPanel extrasFlow;
        private System.Windows.Forms.CheckBox atomicXrayCheckBox;
        private System.Windows.Forms.CheckBox cascadeSummingCheckBox;
        private System.Windows.Forms.CheckBox backscatterCheckBox;
        private System.Windows.Forms.CheckBox escapeCheckBox;
        private System.Windows.Forms.CheckBox pileUpCheckBox;
        private System.Windows.Forms.CheckBox residualBandCheckBox;
        private XPTable.Models.Table reportTable;
        private XPTable.Models.ColumnModel columnModel;
        private XPTable.Models.ImageColumn swatchColumn;
        private XPTable.Models.TextColumn componentColumn;
        private XPTable.Models.TextColumn valueColumn;
        private XPTable.Models.TableModel tableModel;
    }
}
