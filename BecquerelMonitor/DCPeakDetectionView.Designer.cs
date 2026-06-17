namespace BecquerelMonitor
{
    partial class DCPeakDetectionView
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCPeakDetectionView));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            this.table1 = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.textColumn3 = new XPTable.Models.TextColumn();
            this.textColumn1 = new XPTable.Models.NumberColumn();
            this.textColumn4 = new XPTable.Models.TextColumn();
            this.textColumn2 = new XPTable.Models.NumberColumn();
            this.textColumn5 = new XPTable.Models.NumberColumn();
            this.textColumn6 = new XPTable.Models.TextColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown2 = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripMenuItem();
            this.numericUpDown3 = new System.Windows.Forms.NumericUpDown();
            this.button1 = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.comboBoxNuclSet = new System.Windows.Forms.ComboBox();
            this.labelSetName = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown2)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown3)).BeginInit();
            this.SuspendLayout();
            // 
            // table1
            // 
            resources.ApplyResources(this.table1, "table1");
            this.table1.BorderColor = System.Drawing.Color.Black;
            this.table1.ColumnModel = this.columnModel1;
            this.table1.DataMember = null;
            this.table1.DataSourceColumnBinder = dataSourceColumnBinder1;
            dragDropRenderer1.ForeColor = System.Drawing.Color.Red;
            this.table1.DragDropRenderer = dragDropRenderer1;
            this.table1.FullRowSelect = true;
            this.table1.GridLines = XPTable.Models.GridLines.Both;
            this.table1.GridLinesContrainedToData = false;
            this.table1.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 8F);
            this.table1.MultiSelect = true;
            this.table1.Name = "table1";
            this.table1.SortedColumnBackColor = System.Drawing.Color.White;
            this.table1.TableModel = this.tableModel1;
            this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn3,
            this.textColumn1,
            this.textColumn4,
            this.textColumn2,
            this.textColumn5,
            this.textColumn6});
            // 
            // textColumn3
            // 
            this.textColumn3.Editable = false;
            this.textColumn3.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn3, "textColumn3");
            // 
            // textColumn1
            // 
            this.textColumn1.Editable = false;
            this.textColumn1.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn1, "textColumn1");
            // 
            // textColumn4
            // 
            this.textColumn4.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.textColumn4.Editable = false;
            this.textColumn4.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn4, "textColumn4");
            // 
            // textColumn2
            // 
            this.textColumn2.Editable = false;
            this.textColumn2.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn2, "textColumn2");
            // 
            // textColumn5
            // 
            this.textColumn5.Editable = false;
            this.textColumn5.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn5, "textColumn5");
            // 
            // textColumn6
            // 
            this.textColumn6.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.textColumn6.Editable = false;
            this.textColumn6.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn6, "textColumn6");
            // 
            // numericUpDown1
            // 
            resources.ApplyResources(this.numericUpDown1, "numericUpDown1");
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.ValueChanged += new System.EventHandler(this.numericUpDown1_ValueChanged);
            // 
            // numericUpDown2
            // 
            resources.ApplyResources(this.numericUpDown2, "numericUpDown2");
            this.numericUpDown2.Name = "numericUpDown2";
            this.numericUpDown2.ValueChanged += new System.EventHandler(this.numericUpDown2_ValueChanged);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuItem1,
            this.toolStripMenuItem2});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            resources.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
            this.contextMenuStrip1.Opening += new System.ComponentModel.CancelEventHandler(this.ToolStripMenuItem1_Opening);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
            this.toolStripMenuItem1.Click += new System.EventHandler(this.ToolStripMenuItem1_Click);
            // 
            // toolStripMenuItem2
            // 
            this.toolStripMenuItem2.Name = "toolStripMenuItem2";
            resources.ApplyResources(this.toolStripMenuItem2, "toolStripMenuItem2");
            this.toolStripMenuItem2.Click += new System.EventHandler(this.ToolStripMenuItem2_Click);
            // 
            // numericUpDown3
            // 
            this.numericUpDown3.DecimalPlaces = 1;
            resources.ApplyResources(this.numericUpDown3, "numericUpDown3");
            this.numericUpDown3.Name = "numericUpDown3";
            this.numericUpDown3.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDown3.ValueChanged += new System.EventHandler(this.numericUpDown3_ValueChanged);
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // comboBoxNuclSet
            // 
            this.comboBoxNuclSet.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxNuclSet.FormattingEnabled = true;
            resources.ApplyResources(this.comboBoxNuclSet, "comboBoxNuclSet");
            this.comboBoxNuclSet.Name = "comboBoxNuclSet";
            this.comboBoxNuclSet.SelectedIndexChanged += new System.EventHandler(this.comboBoxNuclSet_SelectedIndexChanged);
            // 
            // labelSetName
            // 
            resources.ApplyResources(this.labelSetName, "labelSetName");
            this.labelSetName.Name = "labelSetName";
            // 
            // DCPeakDetectionView
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.comboBoxNuclSet);
            this.Controls.Add(this.labelSetName);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.numericUpDown3);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.numericUpDown2);
            this.Controls.Add(this.numericUpDown1);
            this.Controls.Add(this.table1);
            this.HideOnClose = true;
            this.Name = "DCPeakDetectionView";
            ((System.ComponentModel.ISupportInitialize)(this.table1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown2)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown3)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private XPTable.Models.Table table1;
        private XPTable.Models.ColumnModel columnModel1;
        private XPTable.Models.TableModel tableModel1;
        private XPTable.Models.NumberColumn textColumn1;
        private XPTable.Models.TextColumn textColumn3;
        private XPTable.Models.NumberColumn textColumn2;
        private XPTable.Models.NumberColumn textColumn5;
        private XPTable.Models.TextColumn textColumn6;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        private System.Windows.Forms.NumericUpDown numericUpDown2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.NumericUpDown numericUpDown3;
        private XPTable.Models.TextColumn textColumn4;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem2;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ComboBox comboBoxNuclSet;
        private System.Windows.Forms.Label labelSetName;
    }
}
