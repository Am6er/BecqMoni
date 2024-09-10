namespace BecquerelMonitor
{
    partial class SelectDeviceDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectDeviceDialog));
            this.SelectDeviceLbl = new System.Windows.Forms.Label();
            this.DeviceListcomboBox = new System.Windows.Forms.ComboBox();
            this.ApplyBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // SelectDeviceLbl
            // 
            resources.ApplyResources(this.SelectDeviceLbl, "SelectDeviceLbl");
            this.SelectDeviceLbl.Name = "SelectDeviceLbl";
            // 
            // DeviceListcomboBox
            // 
            this.DeviceListcomboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DeviceListcomboBox.FormattingEnabled = true;
            resources.ApplyResources(this.DeviceListcomboBox, "DeviceListcomboBox");
            this.DeviceListcomboBox.Name = "DeviceListcomboBox";
            // 
            // ApplyBtn
            // 
            resources.ApplyResources(this.ApplyBtn, "ApplyBtn");
            this.ApplyBtn.Name = "ApplyBtn";
            this.ApplyBtn.UseVisualStyleBackColor = true;
            this.ApplyBtn.Click += new System.EventHandler(this.ApplyBtn_Click);
            // 
            // CancelBtn
            // 
            resources.ApplyResources(this.CancelBtn, "CancelBtn");
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.UseVisualStyleBackColor = true;
            this.CancelBtn.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // SelectDeviceDialog
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.ApplyBtn);
            this.Controls.Add(this.DeviceListcomboBox);
            this.Controls.Add(this.SelectDeviceLbl);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SelectDeviceDialog";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label SelectDeviceLbl;
        private System.Windows.Forms.ComboBox DeviceListcomboBox;
        private System.Windows.Forms.Button ApplyBtn;
        private System.Windows.Forms.Button CancelBtn;
    }
}