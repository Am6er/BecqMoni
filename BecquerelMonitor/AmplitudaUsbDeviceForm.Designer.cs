namespace BecquerelMonitor
{
    partial class AmplitudaUsbDeviceForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopTest();
                testTimer.Dispose();
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.labelDevice = new System.Windows.Forms.Label();
            this.comboDevice = new System.Windows.Forms.ComboBox();
            this.buttonRefresh = new System.Windows.Forms.Button();
            this.labelLower = new System.Windows.Forms.Label();
            this.numericLower = new InvariantNumericUpDown();
            this.labelUpper = new System.Windows.Forms.Label();
            this.numericUpper = new InvariantNumericUpDown();
            this.labelDeadTime = new System.Windows.Forms.Label();
            this.numericDeadTime = new InvariantNumericUpDown();
            this.labelHint = new System.Windows.Forms.Label();
            this.buttonTest = new System.Windows.Forms.Button();
            this.labelTestResult = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericLower)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpper)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDeadTime)).BeginInit();
            this.SuspendLayout();
            //
            // labelDevice
            //
            this.labelDevice.AutoSize = true;
            this.labelDevice.Location = new System.Drawing.Point(17, 76);
            this.labelDevice.Name = "labelDevice";
            this.labelDevice.Size = new System.Drawing.Size(47, 13);
            this.labelDevice.TabIndex = 10;
            this.labelDevice.Text = "Device :";
            //
            // comboDevice
            //
            this.comboDevice.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboDevice.FormattingEnabled = true;
            this.comboDevice.Location = new System.Drawing.Point(215, 73);
            this.comboDevice.Name = "comboDevice";
            this.comboDevice.Size = new System.Drawing.Size(150, 21);
            this.comboDevice.TabIndex = 11;
            this.comboDevice.SelectedIndexChanged += new System.EventHandler(this.Setting_Changed);
            //
            // buttonRefresh
            //
            this.buttonRefresh.Location = new System.Drawing.Point(375, 72);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(80, 23);
            this.buttonRefresh.TabIndex = 12;
            this.buttonRefresh.Text = "Refresh";
            this.buttonRefresh.UseVisualStyleBackColor = true;
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // labelLower
            //
            this.labelLower.AutoSize = true;
            this.labelLower.Location = new System.Drawing.Point(17, 109);
            this.labelLower.Name = "labelLower";
            this.labelLower.Size = new System.Drawing.Size(150, 13);
            this.labelLower.TabIndex = 13;
            this.labelLower.Text = "Lower threshold, ADC code :";
            //
            // numericLower
            //
            this.numericLower.Location = new System.Drawing.Point(215, 107);
            this.numericLower.Maximum = new decimal(new int[] { 4095, 0, 0, 0 });
            this.numericLower.Name = "numericLower";
            this.numericLower.Size = new System.Drawing.Size(80, 20);
            this.numericLower.TabIndex = 14;
            this.numericLower.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelUpper
            //
            this.labelUpper.AutoSize = true;
            this.labelUpper.Location = new System.Drawing.Point(17, 139);
            this.labelUpper.Name = "labelUpper";
            this.labelUpper.Size = new System.Drawing.Size(150, 13);
            this.labelUpper.TabIndex = 15;
            this.labelUpper.Text = "Upper threshold, ADC code :";
            //
            // numericUpper
            //
            this.numericUpper.Location = new System.Drawing.Point(215, 137);
            this.numericUpper.Maximum = new decimal(new int[] { 4095, 0, 0, 0 });
            this.numericUpper.Name = "numericUpper";
            this.numericUpper.Size = new System.Drawing.Size(80, 20);
            this.numericUpper.TabIndex = 16;
            this.numericUpper.Value = new decimal(new int[] { 4095, 0, 0, 0 });
            this.numericUpper.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelDeadTime
            //
            this.labelDeadTime.AutoSize = true;
            this.labelDeadTime.Location = new System.Drawing.Point(17, 169);
            this.labelDeadTime.Name = "labelDeadTime";
            this.labelDeadTime.Size = new System.Drawing.Size(130, 13);
            this.labelDeadTime.TabIndex = 17;
            this.labelDeadTime.Text = "Dead time per pulse, us :";
            //
            // numericDeadTime
            //
            this.numericDeadTime.DecimalPlaces = 1;
            this.numericDeadTime.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericDeadTime.Location = new System.Drawing.Point(215, 167);
            this.numericDeadTime.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericDeadTime.Name = "numericDeadTime";
            this.numericDeadTime.Size = new System.Drawing.Size(80, 20);
            this.numericDeadTime.TabIndex = 18;
            this.numericDeadTime.Value = new decimal(new int[] { 14, 0, 0, 0 });
            this.numericDeadTime.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelHint
            //
            this.labelHint.Location = new System.Drawing.Point(17, 203);
            this.labelHint.Name = "labelHint";
            this.labelHint.Size = new System.Drawing.Size(438, 45);
            this.labelHint.TabIndex = 19;
            this.labelHint.Text = "Channels: 4096, 2048, 1024, 512 or 256 - set on the common tab.";
            //
            // buttonTest
            //
            this.buttonTest.Location = new System.Drawing.Point(17, 257);
            this.buttonTest.Name = "buttonTest";
            this.buttonTest.Size = new System.Drawing.Size(100, 23);
            this.buttonTest.TabIndex = 20;
            this.buttonTest.Text = "Test";
            this.buttonTest.UseVisualStyleBackColor = true;
            this.buttonTest.Click += new System.EventHandler(this.buttonTest_Click);
            //
            // labelTestResult
            //
            this.labelTestResult.Location = new System.Drawing.Point(17, 290);
            this.labelTestResult.Name = "labelTestResult";
            this.labelTestResult.Size = new System.Drawing.Size(438, 60);
            this.labelTestResult.TabIndex = 21;
            //
            // AmplitudaUsbDeviceForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.labelTestResult);
            this.Controls.Add(this.buttonTest);
            this.Controls.Add(this.labelHint);
            this.Controls.Add(this.numericDeadTime);
            this.Controls.Add(this.labelDeadTime);
            this.Controls.Add(this.numericUpper);
            this.Controls.Add(this.labelUpper);
            this.Controls.Add(this.numericLower);
            this.Controls.Add(this.labelLower);
            this.Controls.Add(this.buttonRefresh);
            this.Controls.Add(this.comboDevice);
            this.Controls.Add(this.labelDevice);
            this.Name = "AmplitudaUsbDeviceForm";
            ((System.ComponentModel.ISupportInitialize)(this.numericLower)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpper)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDeadTime)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label labelDevice;
        private System.Windows.Forms.ComboBox comboDevice;
        private System.Windows.Forms.Button buttonRefresh;
        private System.Windows.Forms.Label labelLower;
        private InvariantNumericUpDown numericLower;
        private System.Windows.Forms.Label labelUpper;
        private InvariantNumericUpDown numericUpper;
        private System.Windows.Forms.Label labelDeadTime;
        private InvariantNumericUpDown numericDeadTime;
        private System.Windows.Forms.Label labelHint;
        private System.Windows.Forms.Button buttonTest;
        private System.Windows.Forms.Label labelTestResult;
    }
}
