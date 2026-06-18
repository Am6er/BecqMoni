using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    partial class ThermometerForm
    {
        IContainer components = null;

        TextBox textBox1;

        Label label4;

        TextBox textBox2;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ThermometerForm));
            this.textBox1 = new TextBox();
            this.label4 = new Label();
            this.textBox2 = new TextBox();
            this.SuspendLayout();
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.ReadOnly = true;
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            this.textBox2.BackColor = SystemColors.Control;
            resources.ApplyResources(this.textBox2, "textBox2");
            this.textBox2.Name = "textBox2";
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.label4);
            this.Name = "ThermometerForm";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
