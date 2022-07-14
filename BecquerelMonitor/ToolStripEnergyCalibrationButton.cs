using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x0200005D RID: 93
    [ToolboxItem(false)]
    public class ToolStripEnergyCalibrationButton : ToolStripDropDownButton
    {
        // Token: 0x14000011 RID: 17
        // (add) Token: 0x06000477 RID: 1143 RVA: 0x0001577C File Offset: 0x0001397C
        // (remove) Token: 0x06000478 RID: 1144 RVA: 0x000157B8 File Offset: 0x000139B8
        public event ToolStripEnergyCalibrationButton.EnergyCalibrationChangedEventHandler EnergyCalibrationChanged;

        // Token: 0x06000479 RID: 1145 RVA: 0x000157F4 File Offset: 0x000139F4
        public ToolStripEnergyCalibrationButton()
        {
            this.InitControl();
        }

        // Token: 0x0600047A RID: 1146 RVA: 0x00015804 File Offset: 0x00013A04
        public void SetEnergyCalibration(EnergyCalibration energyCalibration, EnergyCalibration defaultEnergyCalibration)
        {
            this.energyCalibration = energyCalibration;
            this.defaultEnergyCalibration = defaultEnergyCalibration;
            if (this.control != null && !this.control.IsDisposed)
            {
                this.control.SetEnergyCalibration(energyCalibration, defaultEnergyCalibration);
            }
        }

        // Token: 0x0600047B RID: 1147 RVA: 0x0001583C File Offset: 0x00013A3C
        protected override void Dispose(bool disposing)
        {
            if (disposing && this.components != null)
            {
                this.components.Dispose();
                if (this.dropDown != null && !this.dropDown.IsDisposed)
                {
                    this.dropDown.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // Token: 0x0600047C RID: 1148 RVA: 0x00015898 File Offset: 0x00013A98
        void InitControl()
        {
            base.AutoSize = false;
            base.Width = 30;
        }

        // Token: 0x0600047D RID: 1149 RVA: 0x000158AC File Offset: 0x00013AAC
        protected override void OnClick(EventArgs e)
        {
            this.control = new ToolStripEnergyCalibrationControl(this);
            this.dropDown = new ToolStripEnergyCalibration(this.control);
            this.control.SetEnergyCalibration(this.energyCalibration, this.defaultEnergyCalibration);
            Rectangle r = this.Bounds;
            ToolStrip parent = base.Parent;
            r = parent.RectangleToScreen(r);
            Point screenLocation;
            switch (parent.DefaultDropDownDirection)
            {
                case ToolStripDropDownDirection.AboveLeft:
                    screenLocation = new Point(r.Right - this.dropDown.Width, r.Top - this.dropDown.Height);
                    goto IL_185;
                case ToolStripDropDownDirection.AboveRight:
                    screenLocation = new Point(r.Left, r.Top - this.dropDown.Height);
                    goto IL_185;
                case ToolStripDropDownDirection.BelowLeft:
                    screenLocation = new Point(r.Right - this.dropDown.Width, r.Bottom);
                    goto IL_185;
                case ToolStripDropDownDirection.BelowRight:
                    screenLocation = new Point(r.Left, r.Bottom);
                    goto IL_185;
                case ToolStripDropDownDirection.Left:
                    screenLocation = new Point(r.Left - this.dropDown.Width, r.Top);
                    goto IL_185;
                case ToolStripDropDownDirection.Right:
                    screenLocation = new Point(r.Right, r.Top);
                    goto IL_185;
                case ToolStripDropDownDirection.Default:
                    screenLocation = new Point(r.Left, r.Bottom);
                    goto IL_185;
            }
            screenLocation = new Point(r.Left, r.Bottom);
        IL_185:
            this.dropDown.Show(screenLocation);
        }

        // Token: 0x0600047E RID: 1150 RVA: 0x00015A50 File Offset: 0x00013C50
        public void FireEnergyCalibrationChanged(EnergyCalibrationChangedEventArgs eventArgs)
        {
            this.energyCalibration = eventArgs.EnergyCalibration;
            if (this.EnergyCalibrationChanged != null)
            {
                this.EnergyCalibrationChanged(this, eventArgs);
            }
        }

        // Token: 0x0600047F RID: 1151 RVA: 0x00015A78 File Offset: 0x00013C78
        void Refresh()
        {
            if (base.Owner != null)
            {
                base.Owner.Refresh();
            }
        }

        // Token: 0x040001E0 RID: 480
        ToolStripEnergyCalibration dropDown;

        // Token: 0x040001E1 RID: 481
        ToolStripEnergyCalibrationControl control;

        // Token: 0x040001E2 RID: 482
        IContainer components;

        // Token: 0x040001E3 RID: 483
        EnergyCalibration energyCalibration;

        // Token: 0x040001E4 RID: 484
        EnergyCalibration defaultEnergyCalibration;

        // Token: 0x0200021D RID: 541
        // (Invoke) Token: 0x06001923 RID: 6435
        public delegate void EnergyCalibrationChangedEventHandler(object sender, EnergyCalibrationChangedEventArgs e);
    }
}
