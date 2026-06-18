using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    partial class ROISimpleDifferenceControl
    {
        IContainer components = null;

        ComboBox comboBox1;

        Label label1;

        TextBox textBox1;

        DoubleTextBox doubleTextBox1;

        DoubleTextBox doubleTextBox2;

        Label label2;

        Label label3;

        Label label4;

        Label label5;

        Label label7;

        Label label8;

        DoubleTextBox doubleTextBox3;

        DoubleTextBox doubleTextBox4;

        Label label6;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ROISimpleDifferenceControl));
            this.comboBox1 = new ComboBox();
            this.label1 = new Label();
            this.textBox1 = new TextBox();
            this.doubleTextBox1 = new DoubleTextBox();
            this.doubleTextBox2 = new DoubleTextBox();
            this.label2 = new Label();
            this.label3 = new Label();
            this.label4 = new Label();
            this.label5 = new Label();
            this.label7 = new Label();
            this.label8 = new Label();
            this.doubleTextBox3 = new DoubleTextBox();
            this.doubleTextBox4 = new DoubleTextBox();
            this.label6 = new Label();
            this.SuspendLayout();
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
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.TextChanged += this.textBox1_TextChanged;
            resources.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
            this.doubleTextBox1.Name = "doubleTextBox1";
            this.doubleTextBox1.TextChanged += this.doubleTextBox1_TextChanged;
            this.doubleTextBox1.Validated += this.doubleTextBox1_Validated;
            resources.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
            this.doubleTextBox2.Name = "doubleTextBox2";
            this.doubleTextBox2.TextChanged += this.doubleTextBox2_TextChanged;
            this.doubleTextBox2.Validated += this.doubleTextBox2_Validated;
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            resources.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
            this.doubleTextBox3.Name = "doubleTextBox3";
            this.doubleTextBox3.TextChanged += this.doubleTextBox3_TextChanged;
            resources.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
            this.doubleTextBox4.Name = "doubleTextBox4";
            this.doubleTextBox4.TextChanged += this.doubleTextBox4_TextChanged;
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            resources.ApplyResources(this, "$this");
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
            this.Name = "ROISimpleDifferenceControl";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
