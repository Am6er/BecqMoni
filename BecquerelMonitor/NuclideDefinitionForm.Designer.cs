namespace BecquerelMonitor
{
    partial class NuclideDefinitionForm
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
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NuclideDefinitionForm));
            XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder1 = new XPTable.Models.DataSourceColumnBinder();
            XPTable.Renderers.DragDropRenderer dragDropRenderer1 = new XPTable.Renderers.DragDropRenderer();
            this.button4 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.label5 = new System.Windows.Forms.Label();
            this.table1 = new XPTable.Models.Table();
            this.columnModel1 = new XPTable.Models.ColumnModel();
            this.textColumn1 = new XPTable.Models.TextColumn();
            this.textColumn2 = new XPTable.Models.NumberColumn();
            this.textColumn3 = new XPTable.Models.NumberColumn();
            this.tableModel1 = new XPTable.Models.TableModel();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.button6 = new System.Windows.Forms.Button();
            this.button5 = new System.Windows.Forms.Button();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.colorComboBox1 = new ColorComboBox.ColorComboBox();
            this.doubleTextBox2 = new BecquerelMonitor.DoubleTextBox();
            this.doubleTextBox1 = new BecquerelMonitor.DoubleTextBox();
            this.intensityLabel = new System.Windows.Forms.Label();
            this.intensitypercentLabel = new System.Windows.Forms.Label();
            this.intensityTextBox = new BecquerelMonitor.DoubleTextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.table1)).BeginInit();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.SuspendLayout();
            // 
            // button4
            // 
            resources.ApplyResources(this.button4, "button4");
            this.button4.Name = "button4";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // button3
            // 
            resources.ApplyResources(this.button3, "button3");
            this.button3.Name = "button3";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
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
            this.table1.HeaderFont = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.table1.Name = "table1";
            this.table1.SortedColumnBackColor = System.Drawing.Color.White;
            this.table1.TableModel = this.tableModel1;
            this.table1.UnfocusedBorderColor = System.Drawing.Color.Black;
            this.table1.EditingStopped += new XPTable.Events.CellEditEventHandler(this.table1_EditingStopped);
            this.table1.SelectionChanged += new XPTable.Events.SelectionEventHandler(this.table1_SelectionChanged);
            // 
            // columnModel1
            // 
            this.columnModel1.Columns.AddRange(new XPTable.Models.Column[] {
            this.textColumn1,
            this.textColumn2,
            this.textColumn3});
            // 
            // textColumn1
            // 
            this.textColumn1.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn1, "textColumn1");
            // 
            // textColumn2
            // 
            this.textColumn2.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn2, "textColumn2");
            // 
            // textColumn3
            // 
            this.textColumn3.IsTextTrimmed = false;
            resources.ApplyResources(this.textColumn3, "textColumn3");
            // 
            // textBox1
            // 
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // button6
            // 
            resources.ApplyResources(this.button6, "button6");
            this.button6.Name = "button6";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button5
            // 
            resources.ApplyResources(this.button5, "button5");
            this.button5.Name = "button5";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // tabControl1
            // 
            resources.ApplyResources(this.tabControl1, "tabControl1");
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            // 
            // tabPage1
            // 
            this.tabPage1.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage1.Controls.Add(this.checkBox1);
            this.tabPage1.Controls.Add(this.textBox2);
            this.tabPage1.Controls.Add(this.label7);
            this.tabPage1.Controls.Add(this.label8);
            this.tabPage1.Controls.Add(this.colorComboBox1);
            this.tabPage1.Controls.Add(this.doubleTextBox2);
            this.tabPage1.Controls.Add(this.doubleTextBox1);
            this.tabPage1.Controls.Add(this.intensityLabel);
            this.tabPage1.Controls.Add(this.intensitypercentLabel);
            this.tabPage1.Controls.Add(this.intensityTextBox);
            this.tabPage1.Controls.Add(this.label6);
            this.tabPage1.Controls.Add(this.label4);
            this.tabPage1.Controls.Add(this.label3);
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.textBox1);
            resources.ApplyResources(this.tabPage1, "tabPage1");
            this.tabPage1.Name = "tabPage1";
            // 
            // checkBox1
            // 
            resources.ApplyResources(this.checkBox1, "checkBox1");
            this.checkBox1.Checked = true;
            this.checkBox1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckStateChanged += new System.EventHandler(this.checkBox1_CheckStateChanged);
            // 
            // textBox2
            // 
            resources.ApplyResources(this.textBox2, "textBox2");
            this.textBox2.Name = "textBox2";
            this.textBox2.TextChanged += new System.EventHandler(this.textBox2_TextChanged);
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            // 
            // label8
            // 
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            // 
            // colorComboBox1
            // 
            this.colorComboBox1.Extended = true;
            resources.ApplyResources(this.colorComboBox1, "colorComboBox1");
            this.colorComboBox1.Name = "colorComboBox1";
            this.colorComboBox1.SelectedColor = System.Drawing.Color.Black;
            this.colorComboBox1.ColorChanged += new ColorComboBox.ColorChangedHandler(this.colorComboBox1_ColorChanged);
            // 
            // doubleTextBox2
            // 
            resources.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
            this.doubleTextBox2.Name = "doubleTextBox2";
            this.doubleTextBox2.TextChanged += new System.EventHandler(this.doubleTextBox2_TextChanged);
            // 
            // doubleTextBox1
            // 
            resources.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
            this.doubleTextBox1.Name = "doubleTextBox1";
            this.doubleTextBox1.TextChanged += new System.EventHandler(this.doubleTextBox1_TextChanged);
            // 
            // intensityLabel
            // 
            resources.ApplyResources(this.intensityLabel, "intensityLabel");
            this.intensityLabel.Name = "intensityLabel";
            // 
            // intensitypercentLabel
            // 
            resources.ApplyResources(this.intensitypercentLabel, "intensitypercentLabel");
            this.intensitypercentLabel.Name = "intensitypercentLabel";
            // 
            // intensityTextBox
            // 
            resources.ApplyResources(this.intensityTextBox, "intensityTextBox");
            this.intensityTextBox.Name = "intensityTextBox";
            this.intensityTextBox.TextChanged += new System.EventHandler(this.intensityTextBox_TextChanged);
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // NuclideDefinitionForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.button6);
            this.Controls.Add(this.button5);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.table1);
            this.Name = "NuclideDefinitionForm";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.NuclideDefinitionForm_FormClosing);
            this.Load += new System.EventHandler(this.NuclideDefinitionForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.table1)).EndInit();
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Label label5;
        private XPTable.Models.Table table1;
        private XPTable.Models.ColumnModel columnModel1;
        private XPTable.Models.TextColumn textColumn1;
        private XPTable.Models.NumberColumn textColumn2;
        private XPTable.Models.NumberColumn textColumn3;
        private XPTable.Models.TableModel tableModel1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label8;
        private ColorComboBox.ColorComboBox colorComboBox1;
        private BecquerelMonitor.DoubleTextBox doubleTextBox2;
        private BecquerelMonitor.DoubleTextBox doubleTextBox1;
        private System.Windows.Forms.Label intensityLabel;
        private System.Windows.Forms.Label intensitypercentLabel;
        private BecquerelMonitor.DoubleTextBox intensityTextBox;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
    }
}
