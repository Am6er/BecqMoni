namespace BecquerelMonitor
{
    partial class SpectrumCutOffDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SpectrumCutOffDialog));
            this.energyradioButton = new System.Windows.Forms.RadioButton();
            this.energytextBox = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.channelradioButton = new System.Windows.Forms.RadioButton();
            this.channeltextBox = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // energyradioButton
            // 
            resources.ApplyResources(this.energyradioButton, "energyradioButton");
            this.energyradioButton.Checked = true;
            this.energyradioButton.Name = "energyradioButton";
            this.energyradioButton.TabStop = true;
            this.energyradioButton.UseVisualStyleBackColor = true;
            this.energyradioButton.CheckedChanged += new System.EventHandler(this.energyradioButton_CheckedChanged);
            // 
            // energytextBox
            // 
            resources.ApplyResources(this.energytextBox, "energytextBox");
            this.energytextBox.Name = "energytextBox";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // channelradioButton
            // 
            resources.ApplyResources(this.channelradioButton, "channelradioButton");
            this.channelradioButton.Name = "channelradioButton";
            this.channelradioButton.UseVisualStyleBackColor = true;
            this.channelradioButton.CheckedChanged += new System.EventHandler(this.channelradioButton_CheckedChanged);
            // 
            // channeltextBox
            // 
            resources.ApplyResources(this.channeltextBox, "channeltextBox");
            this.channeltextBox.Name = "channeltextBox";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            resources.ApplyResources(this.button2, "button2");
            this.button2.Name = "button2";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // SpectrumCutOffDialog
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.channeltextBox);
            this.Controls.Add(this.channelradioButton);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.energytextBox);
            this.Controls.Add(this.energyradioButton);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SpectrumCutOffDialog";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton energyradioButton;
        private System.Windows.Forms.TextBox energytextBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.RadioButton channelradioButton;
        private System.Windows.Forms.TextBox channeltextBox;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
    }
}