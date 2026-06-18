namespace BecquerelMonitor
{
    partial class AtomSpectraVCPDeviceForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AtomSpectraVCPDeviceForm));
            this.comPortsBox = new System.Windows.Forms.ComboBox();
            this.baudratesBox = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.CommandLineIn = new System.Windows.Forms.TextBox();
            this.CommandLineOut = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.deadTimeLbl = new System.Windows.Forms.Label();
            this.deadTimeBtn = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // comPortsBox
            // 
            resources.ApplyResources(this.comPortsBox, "comPortsBox");
            this.comPortsBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comPortsBox.Name = "comPortsBox";
            this.comPortsBox.SelectedIndexChanged += new System.EventHandler(this.ComPortsBox_SelectedIndexChanged);
            // 
            // baudratesBox
            // 
            resources.ApplyResources(this.baudratesBox, "baudratesBox");
            this.baudratesBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.baudratesBox.Items.AddRange(new object[] {
            resources.GetString("baudratesBox.Items"),
            resources.GetString("baudratesBox.Items1"),
            resources.GetString("baudratesBox.Items2"),
            resources.GetString("baudratesBox.Items3"),
            resources.GetString("baudratesBox.Items4")});
            this.baudratesBox.Name = "baudratesBox";
            this.baudratesBox.SelectedIndexChanged += new System.EventHandler(this.BaudratesBox_SelectedIndexChanged);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // CommandLineIn
            // 
            resources.ApplyResources(this.CommandLineIn, "CommandLineIn");
            this.CommandLineIn.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.CommandLineIn.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.CustomSource;
            this.CommandLineIn.Name = "CommandLineIn";
            this.CommandLineIn.KeyDown += new System.Windows.Forms.KeyEventHandler(this.CommandLineIn_KeyDown);
            // 
            // CommandLineOut
            // 
            resources.ApplyResources(this.CommandLineOut, "CommandLineOut");
            this.CommandLineOut.Name = "CommandLineOut";
            this.CommandLineOut.ReadOnly = true;
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
            // deadTimeLbl
            // 
            resources.ApplyResources(this.deadTimeLbl, "deadTimeLbl");
            this.deadTimeLbl.Name = "deadTimeLbl";
            // 
            // deadTimeBtn
            // 
            resources.ApplyResources(this.deadTimeBtn, "deadTimeBtn");
            this.deadTimeBtn.Name = "deadTimeBtn";
            this.deadTimeBtn.Click += new System.EventHandler(this.deadTimeBtn_Click);
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.Click += new System.EventHandler(this.Button1_Click);
            // 
            // AtomSpectraVCPDeviceForm
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.deadTimeBtn);
            this.Controls.Add(this.deadTimeLbl);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.CommandLineOut);
            this.Controls.Add(this.CommandLineIn);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.baudratesBox);
            this.Controls.Add(this.comPortsBox);
            this.Name = "AtomSpectraVCPDeviceForm";
            this.Controls.SetChildIndex(this.comPortsBox, 0);
            this.Controls.SetChildIndex(this.baudratesBox, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.CommandLineIn, 0);
            this.Controls.SetChildIndex(this.CommandLineOut, 0);
            this.Controls.SetChildIndex(this.label2, 0);
            this.Controls.SetChildIndex(this.label3, 0);
            this.Controls.SetChildIndex(this.button1, 0);
            this.Controls.SetChildIndex(this.deadTimeLbl, 0);
            this.Controls.SetChildIndex(this.deadTimeBtn, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox comPortsBox;
        private System.Windows.Forms.ComboBox baudratesBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox CommandLineIn;
        private System.Windows.Forms.TextBox CommandLineOut;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label deadTimeLbl;
        private System.Windows.Forms.Button deadTimeBtn;
        private System.Windows.Forms.Button button1;
    }
}
