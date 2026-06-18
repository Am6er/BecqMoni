using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    public partial class EnergySpectrumView : UserControl
    {
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EnergySpectrumView));
            this.components = new System.ComponentModel.Container();
            this.hScrollBar1 = new System.Windows.Forms.HScrollBar();
            this.vScrollBar1 = new System.Windows.Forms.VScrollBar();
            this.panel2 = new System.Windows.Forms.Panel();
            this.button1 = new BecquerelMonitor.RepeatButton();
            this.button2 = new BecquerelMonitor.RepeatButton();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            resources.ApplyResources(this.hScrollBar1, "hScrollBar1");
            this.hScrollBar1.Name = "hScrollBar1";
            this.toolTip1.SetToolTip(this.hScrollBar1, resources.GetString("hScrollBar1.ToolTip"));
            this.hScrollBar1.ValueChanged += this.hScrollBar1_ValueChanged;
            resources.ApplyResources(this.vScrollBar1, "vScrollBar1");
            this.vScrollBar1.Name = "vScrollBar1";
            this.toolTip1.SetToolTip(this.vScrollBar1, resources.GetString("vScrollBar1.ToolTip"));
            this.vScrollBar1.ValueChanged += this.vScrollBar1_ValueChanged;
            resources.ApplyResources(this.panel2, "panel2");
            this.panel2.BackColor = SystemColors.Control;
            this.panel2.Controls.Add(this.button1);
            this.panel2.Controls.Add(this.button2);
            this.panel2.Name = "panel2";
            this.toolTip1.SetToolTip(this.panel2, resources.GetString("panel2.ToolTip"));
            resources.ApplyResources(this.button1, "button1");
            this.button1.Image = Resources.Zoomin;
            this.button1.Name = "button1";
            this.button1.TabStop = false;
            this.toolTip1.SetToolTip(this.button1, resources.GetString("button1.ToolTip"));
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += this.button1_Click;
            resources.ApplyResources(this.button2, "button2");
            this.button2.Image = Resources.Zoomout;
            this.button2.Name = "button2";
            this.button2.TabStop = false;
            this.toolTip1.SetToolTip(this.button2, resources.GetString("button2.ToolTip"));
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += this.button2_Click;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.White;
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.vScrollBar1);
            this.Controls.Add(this.hScrollBar1);
            this.DoubleBuffered = true;
            this.Name = "EnergySpectrumView";
            this.toolTip1.SetToolTip(this, resources.GetString("$this.ToolTip"));
            this.SizeChanged += this.EnergySpectrumView_SizeChanged;
            this.MouseDown += this.EnergySpectrumView_MouseDown;
            this.MouseLeave += this.EnergySpectrumView_MouseLeave;
            this.MouseMove += this.EnergySpectrumView_MouseMove;
            this.MouseUp += this.EnergySpectrumView_MouseUp;
            this.panel2.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        IContainer components = null;
        HScrollBar hScrollBar1;
        VScrollBar vScrollBar1;
        Panel panel2;
        ToolTip toolTip1;
        RepeatButton button1;
        RepeatButton button2;
    }
}
