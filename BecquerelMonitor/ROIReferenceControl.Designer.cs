using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    partial class ROIReferenceControl
    {
        IContainer components = null;

        ComboBox comboBox1;

        Label label1;

        TextBox textBox1;

        Label label7;

        Label label8;

        DoubleTextBox doubleTextBox3;

        DoubleTextBox doubleTextBox4;

        Label label6;

        Label label9;

        ComboBox comboBox2;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ROIReferenceControl));
            this.comboBox1 = new ComboBox();
            this.label1 = new Label();
            this.textBox1 = new TextBox();
            this.label7 = new Label();
            this.label8 = new Label();
            this.doubleTextBox3 = new DoubleTextBox();
            this.doubleTextBox4 = new DoubleTextBox();
            this.label6 = new Label();
            this.label9 = new Label();
            this.comboBox2 = new ComboBox();
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
            resources.ApplyResources(this.label9, "label9");
            this.label9.Name = "label9";
            resources.ApplyResources(this.comboBox2, "comboBox2");
            this.comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox2.FormattingEnabled = true;
            this.comboBox2.Name = "comboBox2";
            this.comboBox2.DropDown += this.comboBox2_DropDown;
            this.comboBox2.SelectedIndexChanged += this.comboBox2_SelectedIndexChanged;
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.comboBox2);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.doubleTextBox4);
            this.Controls.Add(this.doubleTextBox3);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBox1);
            this.Name = "ROIReferenceControl";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
