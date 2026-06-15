using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    partial class ToolStripEnergyCalibrationControl
    {
        IContainer components;

        NumericUpDown numericUpDown1;

        NumericUpDown numericUpDown2;

        Label label1;

        Label label2;

        Label label3;

        Button button1;

        Button button2;

        NumericUpDown numericUpDown3;

        Button button3;

        Label label4;

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.components != null)
            {
                this.components.Dispose();
            }
            base.Dispose(disposing);
        }

        void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ToolStripEnergyCalibrationControl));
            this.numericUpDown1 = new NumericUpDown();
            this.numericUpDown2 = new NumericUpDown();
            this.label1 = new Label();
            this.label2 = new Label();
            this.label3 = new Label();
            this.button1 = new Button();
            this.button2 = new Button();
            this.numericUpDown3 = new NumericUpDown();
            this.button3 = new Button();
            this.label4 = new Label();
            ((ISupportInitialize)this.numericUpDown1).BeginInit();
            ((ISupportInitialize)this.numericUpDown2).BeginInit();
            ((ISupportInitialize)this.numericUpDown3).BeginInit();
            this.SuspendLayout();
            resources.ApplyResources(this.numericUpDown1, "numericUpDown1");
            this.numericUpDown1.DecimalPlaces = 7;
            this.numericUpDown1.Increment = new decimal(new int[]
            {
                1,
                0,
                0,
                262144
            });
            NumericUpDown numericUpDown = this.numericUpDown1;
            int[] array = new int[4];
            array[0] = 10;
            numericUpDown.Maximum = new decimal(array);
            this.numericUpDown1.Minimum = new decimal(new int[]
            {
                10,
                0,
                0,
                int.MinValue
            });
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.ValueChanged += this.numericUpDown1_ValueChanged;
            this.numericUpDown1.KeyDown += this.numericUpDown1_KeyDown;
            resources.ApplyResources(this.numericUpDown2, "numericUpDown2");
            this.numericUpDown2.DecimalPlaces = 4;
            this.numericUpDown2.Increment = new decimal(new int[]
            {
                1,
                0,
                0,
                131072
            });
            NumericUpDown numericUpDown2 = this.numericUpDown2;
            int[] array2 = new int[4];
            array2[0] = 1000;
            numericUpDown2.Maximum = new decimal(array2);
            this.numericUpDown2.Minimum = new decimal(new int[]
            {
                1,
                0,
                0,
                131072
            });
            this.numericUpDown2.Name = "numericUpDown2";
            this.numericUpDown2.Value = new decimal(new int[]
            {
                1,
                0,
                0,
                131072
            });
            this.numericUpDown2.ValueChanged += this.numericUpDown2_ValueChanged;
            this.numericUpDown2.KeyDown += this.numericUpDown2_KeyDown;
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += this.button1_Click;
            resources.ApplyResources(this.button2, "button2");
            this.button2.Name = "button2";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += this.button2_Click;
            resources.ApplyResources(this.numericUpDown3, "numericUpDown3");
            this.numericUpDown3.DecimalPlaces = 4;
            this.numericUpDown3.Increment = new decimal(new int[]
            {
                1,
                0,
                0,
                131072
            });
            NumericUpDown numericUpDown3 = this.numericUpDown3;
            int[] array3 = new int[4];
            array3[0] = 10000;
            numericUpDown3.Maximum = new decimal(array3);
            this.numericUpDown3.Minimum = new decimal(new int[]
            {
                10000,
                0,
                0,
                int.MinValue
            });
            this.numericUpDown3.Name = "numericUpDown3";
            this.numericUpDown3.ValueChanged += this.numericUpDown3_ValueChanged;
            this.numericUpDown3.KeyDown += this.numericUpDown3_KeyDown;
            resources.ApplyResources(this.button3, "button3");
            this.button3.Name = "button3";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += this.button3_Click;
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(this.label4);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.numericUpDown3);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.numericUpDown2);
            this.Controls.Add(this.numericUpDown1);
            this.Name = "ToolStripEnergyCalibrationControl";
            ((ISupportInitialize)this.numericUpDown1).EndInit();
            ((ISupportInitialize)this.numericUpDown2).EndInit();
            ((ISupportInitialize)this.numericUpDown3).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
