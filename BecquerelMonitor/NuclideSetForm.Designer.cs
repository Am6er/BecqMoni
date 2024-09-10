using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    partial class NuclideSetForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NuclideSetForm));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder2 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer2 = new XPTable.Renderers.DragDropRenderer();
            this.buttonClose = new System.Windows.Forms.Button();
            this.buttonSave = new System.Windows.Forms.Button();
            this.groupBoxEdit = new System.Windows.Forms.GroupBox();
            this.tableNuclides = new XPTable.Models.Table();
            this.columnModelNuclides = new XPTable.Models.ColumnModel();
            this.columnNuclideIncluded = new XPTable.Models.CheckBoxColumn();
            this.columnNuclideName = new XPTable.Models.TextColumn();
            this.columnNuclideEnergy = new XPTable.Models.TextColumn();
            this.tableModelNuclides = new XPTable.Models.TableModel();
            this.buttonDeleteSet = new System.Windows.Forms.Button();
            this.labelNuclides = new System.Windows.Forms.Label();
            this.buttonAddSet = new System.Windows.Forms.Button();
            this.labelSets = new System.Windows.Forms.Label();
            this.tableSets = new XPTable.Models.Table();
            this.columnModelSets = new XPTable.Models.ColumnModel();
            this.columnSetName = new XPTable.Models.TextColumn();
            this.columnSetHideUnknown = new XPTable.Models.CheckBoxColumn();
            this.tableModelSets = new XPTable.Models.TableModel();
            this.groupBoxEdit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tableNuclides)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tableSets)).BeginInit();
            this.SuspendLayout();
            // 
            // buttonClose
            // 
            resources.ApplyResources(this.buttonClose, "buttonClose");
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.UseVisualStyleBackColor = true;
            this.buttonClose.Click += new System.EventHandler(this.buttonClose_Click);
            // 
            // buttonSave
            // 
            resources.ApplyResources(this.buttonSave, "buttonSave");
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.UseVisualStyleBackColor = true;
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            // 
            // groupBoxEdit
            // 
            resources.ApplyResources(this.groupBoxEdit, "groupBoxEdit");
            this.groupBoxEdit.Controls.Add(this.tableNuclides);
            this.groupBoxEdit.Controls.Add(this.buttonDeleteSet);
            this.groupBoxEdit.Controls.Add(this.labelNuclides);
            this.groupBoxEdit.Controls.Add(this.buttonAddSet);
            this.groupBoxEdit.Controls.Add(this.labelSets);
            this.groupBoxEdit.Controls.Add(this.tableSets);
            this.groupBoxEdit.Name = "groupBoxEdit";
            this.groupBoxEdit.TabStop = false;
            // 
            // tableNuclides
            // 
            resources.ApplyResources(this.tableNuclides, "tableNuclides");
            this.tableNuclides.BorderColor = System.Drawing.Color.Black;
            this.tableNuclides.ColumnModel = this.columnModelNuclides;
            this.tableNuclides.DataMember = null;
            this.tableNuclides.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.tableNuclides.DragDropRenderer = dragDropRenderer1;
            this.tableNuclides.GridLinesContrainedToData = false;
            this.tableNuclides.Name = "tableNuclides";
            this.tableNuclides.TableModel = this.tableModelNuclides;
            this.tableNuclides.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.tableNuclides.CellClick += new XPTable.Events.CellMouseEventHandler(this.tableNuclides_CellClick);
            this.tableNuclides.HeaderClick += new XPTable.Events.HeaderMouseEventHandler(this.tableNuclides_HeaderClick);
            // 
            // columnModelNuclides
            // 
            this.columnModelNuclides.Columns.AddRange(new XPTable.Models.Column[] {
            this.columnNuclideIncluded,
            this.columnNuclideName,
            this.columnNuclideEnergy});
            // 
            // columnNuclideIncluded
            // 
            resources.ApplyResources(this.columnNuclideIncluded, "columnNuclideIncluded");
            this.columnNuclideIncluded.IsTextTrimmed = false;
            this.columnNuclideIncluded.Resizable = false;
            this.columnNuclideIncluded.Selectable = false;
            this.columnNuclideIncluded.Sortable = false;
            // 
            // columnNuclideName
            // 
            resources.ApplyResources(this.columnNuclideName, "columnNuclideName");
            this.columnNuclideName.Editable = false;
            this.columnNuclideName.IsTextTrimmed = false;
            this.columnNuclideName.Selectable = false;
            this.columnNuclideName.Sortable = false;
            // 
            // columnNuclideEnergy
            // 
            resources.ApplyResources(this.columnNuclideEnergy, "columnNuclideEnergy");
            this.columnNuclideEnergy.Editable = false;
            this.columnNuclideEnergy.IsTextTrimmed = false;
            this.columnNuclideEnergy.Selectable = false;
            this.columnNuclideEnergy.Sortable = false;
            // 
            // buttonDeleteSet
            // 
            resources.ApplyResources(this.buttonDeleteSet, "buttonDeleteSet");
            this.buttonDeleteSet.Name = "buttonDeleteSet";
            this.buttonDeleteSet.UseVisualStyleBackColor = true;
            this.buttonDeleteSet.Click += new System.EventHandler(this.buttonDeleteSet_Click);
            // 
            // labelNuclides
            // 
            resources.ApplyResources(this.labelNuclides, "labelNuclides");
            this.labelNuclides.Name = "labelNuclides";
            // 
            // buttonAddSet
            // 
            resources.ApplyResources(this.buttonAddSet, "buttonAddSet");
            this.buttonAddSet.Name = "buttonAddSet";
            this.buttonAddSet.UseVisualStyleBackColor = true;
            this.buttonAddSet.Click += new System.EventHandler(this.buttonAddSet_Click);
            // 
            // labelSets
            // 
            resources.ApplyResources(this.labelSets, "labelSets");
            this.labelSets.Name = "labelSets";
            // 
            // tableSets
            // 
            resources.ApplyResources(this.tableSets, "tableSets");
            this.tableSets.BorderColor = System.Drawing.Color.Black;
            this.tableSets.ColumnModel = this.columnModelSets;
            this.tableSets.DataMember = null;
            this.tableSets.DataSourceColumnBinder = dataSourceColumnBinder2;
            dragDropRenderer2.ForeColor = System.Drawing.Color.Red;
            this.tableSets.DragDropRenderer = dragDropRenderer2;
            this.tableSets.GridLinesContrainedToData = false;
            this.tableSets.Name = "tableSets";
            this.tableSets.TableModel = this.tableModelSets;
            this.tableSets.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.tableSets.CellCheckChanged += new XPTable.Events.CellCheckBoxEventHandler(this.tableSets_CellCheckChanged);
            this.tableSets.EditingStopped += new XPTable.Events.CellEditEventHandler(this.tableSets_EditingStopped);
            this.tableSets.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.tableSets_SelectionChanged);
            // 
            // columnModelSets
            // 
            this.columnModelSets.Columns.AddRange(new XPTable.Models.Column[] {
            this.columnSetName,
            this.columnSetHideUnknown});
            // 
            // columnSetName
            // 
            resources.ApplyResources(this.columnSetName, "columnSetName");
            this.columnSetName.IsTextTrimmed = false;
            this.columnSetName.Sortable = false;
            // 
            // columnSetHideUnknown
            // 
            resources.ApplyResources(this.columnSetHideUnknown, "columnSetHideUnknown");
            this.columnSetHideUnknown.IsTextTrimmed = false;
            // 
            // NuclideSetForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBoxEdit);
            this.Controls.Add(this.buttonSave);
            this.Controls.Add(this.buttonClose);
            this.Name = "NuclideSetForm";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.NuclideSetForm_FormClosing);
            this.groupBoxEdit.ResumeLayout(false);
            this.groupBoxEdit.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tableNuclides)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tableSets)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button buttonClose;
        private System.Windows.Forms.Button buttonSave;
        private System.Windows.Forms.GroupBox groupBoxEdit;
        private XPTable.Models.Table tableNuclides;
        private System.Windows.Forms.Button buttonDeleteSet;
        private System.Windows.Forms.Label labelNuclides;
        private System.Windows.Forms.Button buttonAddSet;
        private System.Windows.Forms.Label labelSets;
        private XPTable.Models.Table tableSets;
        private XPTable.Models.ColumnModel columnModelNuclides;
        private XPTable.Models.CheckBoxColumn columnNuclideIncluded;
        private XPTable.Models.TextColumn columnNuclideName;
        private XPTable.Models.TextColumn columnNuclideEnergy;
        private XPTable.Models.ColumnModel columnModelSets;
        private XPTable.Models.TextColumn columnSetName;
        private XPTable.Models.TableModel tableModelSets;
        private XPTable.Models.TableModel tableModelNuclides;
        private XPTable.Models.CheckBoxColumn columnSetHideUnknown;
    }
}