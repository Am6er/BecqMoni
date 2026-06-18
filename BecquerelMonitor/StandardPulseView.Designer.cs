using System.ComponentModel;
using System.Drawing;

namespace BecquerelMonitor
{
    partial class StandardPulseView
    {
        IContainer components = null;

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
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = Color.Black;
            this.DoubleBuffered = true;
            this.Name = "StandardPulseView";
            this.Size = new Size(195, 187);
            this.ResumeLayout(false);
        }
    }
}
