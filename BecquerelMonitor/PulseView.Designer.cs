using System.ComponentModel;
using System.Drawing;

namespace BecquerelMonitor
{
    partial class PulseView
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
            this.BackColor = Color.Black;
            this.DoubleBuffered = true;
            this.Name = "PulseView";
            this.Size = new Size(298, 279);
            this.ResumeLayout(false);
        }
    }
}
