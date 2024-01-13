namespace BecquerelMonitor
{
    partial class RadiaCodeDeviceForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RadiaCodeDeviceForm));
            this.label1 = new System.Windows.Forms.Label();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.button1 = new System.Windows.Forms.Button();
            this.troubleShootbtn = new System.Windows.Forms.Button();
            this.TroubleshootText = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // comboBox1
            // 
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.comboBox1, "comboBox1");
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // troubleShootbtn
            // 
            resources.ApplyResources(this.troubleShootbtn, "troubleShootbtn");
            this.troubleShootbtn.Name = "troubleShootbtn";
            this.troubleShootbtn.UseVisualStyleBackColor = true;
            this.troubleShootbtn.Click += new System.EventHandler(this.troubleShootbtn_Click);
            // 
            // TroubleshootText
            // 
            resources.ApplyResources(this.TroubleshootText, "TroubleshootText");
            this.TroubleshootText.Name = "TroubleshootText";
            this.TroubleshootText.ReadOnly = true;
            // 
            // RadiaCodeDeviceForm
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.TroubleshootText);
            this.Controls.Add(this.troubleShootbtn);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.comboBox1);
            this.Controls.Add(this.label1);
            this.Name = "RadiaCodeDeviceForm";
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.comboBox1, 0);
            this.Controls.SetChildIndex(this.button1, 0);
            this.Controls.SetChildIndex(this.troubleShootbtn, 0);
            this.Controls.SetChildIndex(this.TroubleshootText, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button troubleShootbtn;
        private System.Windows.Forms.TextBox TroubleshootText;
    }
}
