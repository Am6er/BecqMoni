using System;
using System.ComponentModel;

namespace BecquerelMonitor
{
    partial class AtomSpectraTcpDeviceForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                testGeneration++;
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
            this.labelHost = new System.Windows.Forms.Label();
            this.textHost = new System.Windows.Forms.TextBox();
            this.labelPort = new System.Windows.Forms.Label();
            this.numericPort = new BecquerelMonitor.InvariantNumericUpDown();
            this.labelDeadTime = new System.Windows.Forms.Label();
            this.labelHint = new System.Windows.Forms.Label();
            this.buttonTest = new System.Windows.Forms.Button();
            this.labelTestResult = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericPort)).BeginInit();
            this.SuspendLayout();
            // 
            // labelHost
            // 
            this.labelHost.AutoSize = true;
            this.labelHost.Location = new System.Drawing.Point(17, 76);
            this.labelHost.Name = "labelHost";
            this.labelHost.TabIndex = 10;
            this.labelHost.Text = "Host (IP or name) :";
            // 
            // textHost
            // 
            this.textHost.Location = new System.Drawing.Point(215, 73);
            this.textHost.Name = "textHost";
            this.textHost.Size = new System.Drawing.Size(150, 20);
            this.textHost.TabIndex = 11;
            this.textHost.TextChanged += new System.EventHandler(this.Setting_Changed);
            // 
            // labelPort
            // 
            this.labelPort.AutoSize = true;
            this.labelPort.Location = new System.Drawing.Point(17, 106);
            this.labelPort.Name = "labelPort";
            this.labelPort.TabIndex = 12;
            this.labelPort.Text = "TCP port :";
            // 
            // numericPort
            // 
            this.numericPort.Location = new System.Drawing.Point(215, 104);
            this.numericPort.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericPort.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.numericPort.Name = "numericPort";
            this.numericPort.Size = new System.Drawing.Size(80, 20);
            this.numericPort.TabIndex = 13;
            this.numericPort.Value = new decimal(new int[] {
            8234,
            0,
            0,
            0});
            this.numericPort.ValueChanged += new System.EventHandler(this.Setting_Changed);
            // 
            // labelDeadTime
            // 
            this.labelDeadTime.AutoSize = true;
            this.labelDeadTime.Location = new System.Drawing.Point(17, 136);
            this.labelDeadTime.Name = "labelDeadTime";
            this.labelDeadTime.TabIndex = 14;
            // 
            // labelHint
            // 
            this.labelHint.Location = new System.Drawing.Point(17, 162);
            this.labelHint.Name = "labelHint";
            this.labelHint.Size = new System.Drawing.Size(438, 70);
            this.labelHint.TabIndex = 15;
            // 
            // buttonTest
            // 
            this.buttonTest.Location = new System.Drawing.Point(17, 240);
            this.buttonTest.Name = "buttonTest";
            this.buttonTest.Size = new System.Drawing.Size(130, 23);
            this.buttonTest.TabIndex = 16;
            this.buttonTest.Text = "Test connection";
            this.buttonTest.UseVisualStyleBackColor = true;
            this.buttonTest.Click += new System.EventHandler(this.buttonTest_Click);
            // 
            // labelTestResult
            // 
            this.labelTestResult.Location = new System.Drawing.Point(17, 272);
            this.labelTestResult.Name = "labelTestResult";
            this.labelTestResult.Size = new System.Drawing.Size(438, 60);
            this.labelTestResult.TabIndex = 17;
            // 
            // AtomSpectraTcpDeviceForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.labelTestResult);
            this.Controls.Add(this.buttonTest);
            this.Controls.Add(this.labelHint);
            this.Controls.Add(this.labelDeadTime);
            this.Controls.Add(this.numericPort);
            this.Controls.Add(this.labelPort);
            this.Controls.Add(this.textHost);
            this.Controls.Add(this.labelHost);
            this.Name = "AtomSpectraTcpDeviceForm";
            ((System.ComponentModel.ISupportInitialize)(this.numericPort)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label labelHost;
        private System.Windows.Forms.TextBox textHost;
        private System.Windows.Forms.Label labelPort;
        private InvariantNumericUpDown numericPort;
        private System.Windows.Forms.Label labelDeadTime;
        private System.Windows.Forms.Label labelHint;
        private System.Windows.Forms.Button buttonTest;
        private System.Windows.Forms.Label labelTestResult;
    }
}
