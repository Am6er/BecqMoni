namespace BecquerelMonitor
{
    partial class AmplitudaSerialDeviceForm
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
            this.labelPort = new System.Windows.Forms.Label();
            this.comboPort = new System.Windows.Forms.ComboBox();
            this.buttonRefresh = new System.Windows.Forms.Button();
            this.labelAddress = new System.Windows.Forms.Label();
            this.numericAddress = new InvariantNumericUpDown();
            this.labelPoll = new System.Windows.Forms.Label();
            this.numericPoll = new InvariantNumericUpDown();
            this.labelGap = new System.Windows.Forms.Label();
            this.numericGap = new InvariantNumericUpDown();
            this.labelDeadTime = new System.Windows.Forms.Label();
            this.numericDeadTime = new InvariantNumericUpDown();
            this.labelSettle = new System.Windows.Forms.Label();
            this.numericSettle = new InvariantNumericUpDown();
            this.labelHint = new System.Windows.Forms.Label();
            this.buttonTest = new System.Windows.Forms.Button();
            this.buttonScan = new System.Windows.Forms.Button();
            this.buttonInstruction = new System.Windows.Forms.Button();
            this.labelTestResult = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericAddress)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericPoll)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericGap)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDeadTime)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSettle)).BeginInit();
            this.SuspendLayout();
            //
            // labelPort
            //
            this.labelPort.AutoSize = true;
            this.labelPort.Location = new System.Drawing.Point(17, 76);
            this.labelPort.Name = "labelPort";
            this.labelPort.Size = new System.Drawing.Size(35, 13);
            this.labelPort.TabIndex = 10;
            this.labelPort.Text = "Port :";
            //
            // comboPort
            //
            this.comboPort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboPort.FormattingEnabled = true;
            this.comboPort.Location = new System.Drawing.Point(215, 73);
            this.comboPort.Name = "comboPort";
            this.comboPort.Size = new System.Drawing.Size(150, 21);
            this.comboPort.TabIndex = 11;
            this.comboPort.SelectedIndexChanged += new System.EventHandler(this.Setting_Changed);
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
            // labelAddress
            //
            this.labelAddress.AutoSize = true;
            this.labelAddress.Location = new System.Drawing.Point(17, 109);
            this.labelAddress.Name = "labelAddress";
            this.labelAddress.Size = new System.Drawing.Size(80, 13);
            this.labelAddress.TabIndex = 13;
            this.labelAddress.Text = "Block address :";
            //
            // numericAddress
            //
            this.numericAddress.Location = new System.Drawing.Point(215, 107);
            this.numericAddress.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numericAddress.Name = "numericAddress";
            this.numericAddress.Size = new System.Drawing.Size(80, 20);
            this.numericAddress.TabIndex = 14;
            this.numericAddress.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelPoll
            //
            this.labelPoll.AutoSize = true;
            this.labelPoll.Location = new System.Drawing.Point(17, 139);
            this.labelPoll.Name = "labelPoll";
            this.labelPoll.Size = new System.Drawing.Size(95, 13);
            this.labelPoll.TabIndex = 15;
            this.labelPoll.Text = "Polling period, s :";
            //
            // numericPoll
            //
            this.numericPoll.Location = new System.Drawing.Point(215, 137);
            this.numericPoll.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.numericPoll.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            this.numericPoll.Name = "numericPoll";
            this.numericPoll.Size = new System.Drawing.Size(80, 20);
            this.numericPoll.TabIndex = 16;
            this.numericPoll.Value = new decimal(new int[] { 3, 0, 0, 0 });
            this.numericPoll.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelGap
            //
            this.labelGap.AutoSize = true;
            this.labelGap.Location = new System.Drawing.Point(17, 169);
            this.labelGap.Name = "labelGap";
            this.labelGap.Size = new System.Drawing.Size(170, 13);
            this.labelGap.TabIndex = 17;
            this.labelGap.Text = "Pause before the address byte, ms :";
            //
            // numericGap
            //
            this.numericGap.Location = new System.Drawing.Point(215, 167);
            this.numericGap.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
            this.numericGap.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericGap.Name = "numericGap";
            this.numericGap.Size = new System.Drawing.Size(80, 20);
            this.numericGap.TabIndex = 18;
            this.numericGap.Value = new decimal(new int[] { 20, 0, 0, 0 });
            this.numericGap.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelDeadTime
            //
            this.labelDeadTime.AutoSize = true;
            this.labelDeadTime.Location = new System.Drawing.Point(17, 199);
            this.labelDeadTime.Name = "labelDeadTime";
            this.labelDeadTime.Size = new System.Drawing.Size(130, 13);
            this.labelDeadTime.TabIndex = 19;
            this.labelDeadTime.Text = "Dead time per pulse, us :";
            //
            // numericDeadTime
            //
            this.numericDeadTime.DecimalPlaces = 1;
            this.numericDeadTime.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericDeadTime.Location = new System.Drawing.Point(215, 197);
            this.numericDeadTime.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericDeadTime.Name = "numericDeadTime";
            this.numericDeadTime.Size = new System.Drawing.Size(80, 20);
            this.numericDeadTime.TabIndex = 20;
            this.numericDeadTime.Value = new decimal(new int[] { 95, 0, 0, 0 });
            this.numericDeadTime.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelSettle
            //
            this.labelSettle.AutoSize = true;
            this.labelSettle.Location = new System.Drawing.Point(17, 229);
            this.labelSettle.Name = "labelSettle";
            this.labelSettle.Size = new System.Drawing.Size(170, 13);
            this.labelSettle.TabIndex = 21;
            this.labelSettle.Text = "Settling time after a block restart, s :";
            //
            // numericSettle
            //
            this.numericSettle.Location = new System.Drawing.Point(215, 227);
            this.numericSettle.Maximum = new decimal(new int[] { 600, 0, 0, 0 });
            this.numericSettle.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numericSettle.Name = "numericSettle";
            this.numericSettle.Size = new System.Drawing.Size(80, 20);
            this.numericSettle.TabIndex = 22;
            this.numericSettle.Value = new decimal(new int[] { 60, 0, 0, 0 });
            this.numericSettle.ValueChanged += new System.EventHandler(this.Setting_Changed);
            //
            // labelHint
            //
            this.labelHint.Location = new System.Drawing.Point(17, 261);
            this.labelHint.Name = "labelHint";
            this.labelHint.Size = new System.Drawing.Size(438, 58);
            this.labelHint.TabIndex = 23;
            this.labelHint.Text = "Channels: a multiple of 512 - set on the common tab.";
            //
            // buttonTest
            //
            this.buttonTest.Location = new System.Drawing.Point(17, 327);
            this.buttonTest.Name = "buttonTest";
            this.buttonTest.Size = new System.Drawing.Size(100, 23);
            this.buttonTest.TabIndex = 24;
            this.buttonTest.Text = "Test";
            this.buttonTest.UseVisualStyleBackColor = true;
            this.buttonTest.Click += new System.EventHandler(this.buttonTest_Click);
            //
            // buttonScan
            //
            this.buttonScan.Location = new System.Drawing.Point(127, 327);
            this.buttonScan.Name = "buttonScan";
            this.buttonScan.Size = new System.Drawing.Size(100, 23);
            this.buttonScan.TabIndex = 25;
            this.buttonScan.Text = "Find blocks";
            this.buttonScan.UseVisualStyleBackColor = true;
            this.buttonScan.Click += new System.EventHandler(this.buttonScan_Click);
            //
            // buttonInstruction
            //
            this.buttonInstruction.Location = new System.Drawing.Point(237, 327);
            this.buttonInstruction.Name = "buttonInstruction";
            this.buttonInstruction.Size = new System.Drawing.Size(100, 23);
            this.buttonInstruction.TabIndex = 26;
            this.buttonInstruction.Text = "Instruction";
            this.buttonInstruction.UseVisualStyleBackColor = true;
            this.buttonInstruction.Click += new System.EventHandler(this.buttonInstruction_Click);
            //
            // labelTestResult
            //
            this.labelTestResult.Location = new System.Drawing.Point(17, 360);
            this.labelTestResult.Name = "labelTestResult";
            this.labelTestResult.Size = new System.Drawing.Size(438, 60);
            this.labelTestResult.TabIndex = 27;
            //
            // AmplitudaSerialDeviceForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.labelTestResult);
            this.Controls.Add(this.buttonInstruction);
            this.Controls.Add(this.buttonScan);
            this.Controls.Add(this.buttonTest);
            this.Controls.Add(this.labelHint);
            this.Controls.Add(this.numericSettle);
            this.Controls.Add(this.labelSettle);
            this.Controls.Add(this.numericDeadTime);
            this.Controls.Add(this.labelDeadTime);
            this.Controls.Add(this.numericGap);
            this.Controls.Add(this.labelGap);
            this.Controls.Add(this.numericPoll);
            this.Controls.Add(this.labelPoll);
            this.Controls.Add(this.numericAddress);
            this.Controls.Add(this.labelAddress);
            this.Controls.Add(this.buttonRefresh);
            this.Controls.Add(this.comboPort);
            this.Controls.Add(this.labelPort);
            this.Name = "AmplitudaSerialDeviceForm";
            ((System.ComponentModel.ISupportInitialize)(this.numericAddress)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericPoll)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericGap)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDeadTime)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSettle)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label labelPort;
        private System.Windows.Forms.ComboBox comboPort;
        private System.Windows.Forms.Button buttonRefresh;
        private System.Windows.Forms.Label labelAddress;
        private InvariantNumericUpDown numericAddress;
        private System.Windows.Forms.Label labelPoll;
        private InvariantNumericUpDown numericPoll;
        private System.Windows.Forms.Label labelGap;
        private InvariantNumericUpDown numericGap;
        private System.Windows.Forms.Label labelDeadTime;
        private InvariantNumericUpDown numericDeadTime;
        private System.Windows.Forms.Label labelSettle;
        private InvariantNumericUpDown numericSettle;
        private System.Windows.Forms.Label labelHint;
        private System.Windows.Forms.Button buttonTest;
        private System.Windows.Forms.Button buttonScan;
        private System.Windows.Forms.Button buttonInstruction;
        private System.Windows.Forms.Label labelTestResult;
    }
}
