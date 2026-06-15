using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    partial class ROICovellMethodControl
    {
        IContainer components;

        Label label6;

        DoubleTextBox doubleTextBox4;

        DoubleTextBox doubleTextBox3;

        Label label8;

        Label label7;

        Label label5;

        Label label4;

        Label label3;

        Label label2;

        DoubleTextBox doubleTextBox2;

        DoubleTextBox doubleTextBox1;

        TextBox textBox1;

        Label label1;

        ComboBox comboBox1;

        Label label9;

        Label label10;

        Label label11;

        Label label12;

        Label label13;

        Label label14;

        Label label15;

        Label label16;

        DoubleTextBox doubleTextBox5;

        DoubleTextBox doubleTextBox6;

        DoubleTextBox doubleTextBox7;

        DoubleTextBox doubleTextBox8;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ROICovellMethodControl));
            this.label6 = new Label();
            this.doubleTextBox4 = new DoubleTextBox();
            this.doubleTextBox3 = new DoubleTextBox();
            this.label8 = new Label();
            this.label7 = new Label();
            this.label5 = new Label();
            this.label4 = new Label();
            this.label3 = new Label();
            this.label2 = new Label();
            this.doubleTextBox2 = new DoubleTextBox();
            this.doubleTextBox1 = new DoubleTextBox();
            this.textBox1 = new TextBox();
            this.label1 = new Label();
            this.comboBox1 = new ComboBox();
            this.label9 = new Label();
            this.label10 = new Label();
            this.label11 = new Label();
            this.label12 = new Label();
            this.label13 = new Label();
            this.label14 = new Label();
            this.label15 = new Label();
            this.label16 = new Label();
            this.doubleTextBox5 = new DoubleTextBox();
            this.doubleTextBox6 = new DoubleTextBox();
            this.doubleTextBox7 = new DoubleTextBox();
            this.doubleTextBox8 = new DoubleTextBox();
            this.SuspendLayout();
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            resources.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
            this.doubleTextBox4.Name = "doubleTextBox4";
            this.doubleTextBox4.TextChanged += this.doubleTextBox4_TextChanged;
            resources.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
            this.doubleTextBox3.Name = "doubleTextBox3";
            this.doubleTextBox3.TextChanged += this.doubleTextBox3_TextChanged;
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            resources.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
            this.doubleTextBox2.Name = "doubleTextBox2";
            this.doubleTextBox2.TextChanged += this.doubleTextBox2_TextChanged;
            this.doubleTextBox2.Validated += this.doubleTextBox2_Validated;
            resources.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
            this.doubleTextBox1.Name = "doubleTextBox1";
            this.doubleTextBox1.TextChanged += this.doubleTextBox1_TextChanged;
            this.doubleTextBox1.Validated += this.doubleTextBox1_Validated;
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.TextChanged += this.textBox1_TextChanged;
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            resources.ApplyResources(this.comboBox1, "comboBox1");
            this.comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Items.AddRange(new object[]
            {
                resources.GetString("comboBox1.Items"),
                resources.GetString("comboBox1.Items1")
            });
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.SelectedIndexChanged += this.comboBox1_SelectedIndexChanged;
            resources.ApplyResources(this.label9, "label9");
            this.label9.Name = "label9";
            resources.ApplyResources(this.label10, "label10");
            this.label10.Name = "label10";
            resources.ApplyResources(this.label11, "label11");
            this.label11.Name = "label11";
            resources.ApplyResources(this.label12, "label12");
            this.label12.Name = "label12";
            resources.ApplyResources(this.label13, "label13");
            this.label13.Name = "label13";
            resources.ApplyResources(this.label14, "label14");
            this.label14.Name = "label14";
            resources.ApplyResources(this.label15, "label15");
            this.label15.Name = "label15";
            resources.ApplyResources(this.label16, "label16");
            this.label16.Name = "label16";
            resources.ApplyResources(this.doubleTextBox5, "doubleTextBox5");
            this.doubleTextBox5.Name = "doubleTextBox5";
            this.doubleTextBox5.TextChanged += this.doubleTextBox5_TextChanged;
            this.doubleTextBox5.Validated += this.doubleTextBox5_Validated;
            resources.ApplyResources(this.doubleTextBox6, "doubleTextBox6");
            this.doubleTextBox6.Name = "doubleTextBox6";
            this.doubleTextBox6.TextChanged += this.doubleTextBox6_TextChanged;
            this.doubleTextBox6.Validated += this.doubleTextBox6_Validated;
            resources.ApplyResources(this.doubleTextBox7, "doubleTextBox7");
            this.doubleTextBox7.Name = "doubleTextBox7";
            this.doubleTextBox7.TextChanged += this.doubleTextBox7_TextChanged;
            resources.ApplyResources(this.doubleTextBox8, "doubleTextBox8");
            this.doubleTextBox8.Name = "doubleTextBox8";
            this.doubleTextBox8.TextChanged += this.doubleTextBox8_TextChanged;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Controls.Add(this.doubleTextBox8);
            this.Controls.Add(this.doubleTextBox7);
            this.Controls.Add(this.doubleTextBox6);
            this.Controls.Add(this.doubleTextBox5);
            this.Controls.Add(this.label16);
            this.Controls.Add(this.label15);
            this.Controls.Add(this.label14);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.doubleTextBox4);
            this.Controls.Add(this.doubleTextBox3);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.doubleTextBox2);
            this.Controls.Add(this.doubleTextBox1);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBox1);
            this.Name = "ROICovellMethodControl";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
