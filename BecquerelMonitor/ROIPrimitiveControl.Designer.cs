using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    partial class ROIPrimitiveControl
    {
        IContainer components;

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
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(6f, 12f);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Name = "ROIPrimitiveControl";
            this.Size = new Size(510, 169);
            this.ResumeLayout(false);
        }
    }
}
