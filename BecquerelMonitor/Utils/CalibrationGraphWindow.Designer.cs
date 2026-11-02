namespace BecquerelMonitor.Utils
{
    partial class CalibrationGraphWindow
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CalibrationGraphWindow));
            this.graphPanel = new System.Windows.Forms.Panel();
            this.unavailableLabel = new System.Windows.Forms.Label();
            this.topPanel = new System.Windows.Forms.Panel();
            this.viewLabel = new System.Windows.Forms.Label();
            this.viewComboBox = new System.Windows.Forms.ComboBox();
            this.graphPanel.SuspendLayout();
            this.topPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // graphPanel
            //
            this.graphPanel.Controls.Add(this.unavailableLabel);
            resources.ApplyResources(this.graphPanel, "graphPanel");
            this.graphPanel.Name = "graphPanel";
            //
            // unavailableLabel
            //
            resources.ApplyResources(this.unavailableLabel, "unavailableLabel");
            this.unavailableLabel.Name = "unavailableLabel";
            //
            // topPanel
            //
            this.topPanel.Controls.Add(this.viewLabel);
            this.topPanel.Controls.Add(this.viewComboBox);
            resources.ApplyResources(this.topPanel, "topPanel");
            this.topPanel.Name = "topPanel";
            //
            // viewLabel
            //
            resources.ApplyResources(this.viewLabel, "viewLabel");
            this.viewLabel.Name = "viewLabel";
            //
            // viewComboBox
            //
            this.viewComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.viewComboBox.FormattingEnabled = true;
            this.viewComboBox.Items.AddRange(new object[] {
            resources.GetString("viewComboBox.Items"),
            resources.GetString("viewComboBox.Items1"),
            resources.GetString("viewComboBox.Items2")});
            resources.ApplyResources(this.viewComboBox, "viewComboBox");
            this.viewComboBox.Name = "viewComboBox";
            this.viewComboBox.SelectedIndexChanged += new System.EventHandler(this.ViewComboBox_SelectedIndexChanged);
            //
            // CalibrationGraphWindow
            //
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.graphPanel);
            this.Controls.Add(this.topPanel);
            this.MinimizeBox = false;
            this.Name = "CalibrationGraphWindow";
            this.graphPanel.ResumeLayout(false);
            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel graphPanel;
        private System.Windows.Forms.Label unavailableLabel;
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Label viewLabel;
        private System.Windows.Forms.ComboBox viewComboBox;
    }
}
