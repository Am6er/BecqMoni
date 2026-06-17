using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
    [ToolboxItem(false)]
    [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
    public class ToolStripEnergyCalibrationButton : ToolStripDropDownButton
    {
        public event EnergyCalibrationChangedEventHandler EnergyCalibrationChanged;

        ToolStripEnergyCalibration dropDown;
        ToolStripEnergyCalibrationControl control;
        IContainer components;
        EnergyCalibration energyCalibration;
        EnergyCalibration defaultEnergyCalibration;

        public ToolStripEnergyCalibrationButton()
        {
            InitControl();
        }

        public void SetEnergyCalibration(EnergyCalibration energyCalibration, EnergyCalibration defaultEnergyCalibration)
        {
            this.energyCalibration = energyCalibration;
            this.defaultEnergyCalibration = defaultEnergyCalibration;
            if (control != null && !control.IsDisposed)
            {
                control.SetEnergyCalibration(energyCalibration, defaultEnergyCalibration);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
                if (dropDown != null && !dropDown.IsDisposed)
                {
                    dropDown.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        void InitControl()
        {
            AutoSize = false;
            Width = 30;
        }

        protected override void OnClick(EventArgs e)
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime || Parent == null)
            {
                return;
            }

            if (dropDown != null && !dropDown.IsDisposed)
            {
                dropDown.Dispose();
            }

            control = new ToolStripEnergyCalibrationControl(this);
            dropDown = new ToolStripEnergyCalibration(control);
            control.SetEnergyCalibration(energyCalibration, defaultEnergyCalibration);

            Rectangle r = Bounds;
            ToolStrip parent = Parent;
            r = parent.RectangleToScreen(r);

            Point screenLocation;
            switch (parent.DefaultDropDownDirection)
            {
                case ToolStripDropDownDirection.AboveLeft:
                    screenLocation = new Point(r.Right - dropDown.Width, r.Top - dropDown.Height);
                    break;
                case ToolStripDropDownDirection.AboveRight:
                    screenLocation = new Point(r.Left, r.Top - dropDown.Height);
                    break;
                case ToolStripDropDownDirection.BelowLeft:
                    screenLocation = new Point(r.Right - dropDown.Width, r.Bottom);
                    break;
                case ToolStripDropDownDirection.BelowRight:
                    screenLocation = new Point(r.Left, r.Bottom);
                    break;
                case ToolStripDropDownDirection.Left:
                    screenLocation = new Point(r.Left - dropDown.Width, r.Top);
                    break;
                case ToolStripDropDownDirection.Right:
                    screenLocation = new Point(r.Right, r.Top);
                    break;
                default:
                    screenLocation = new Point(r.Left, r.Bottom);
                    break;
            }

            dropDown.Show(screenLocation);
        }

        public void FireEnergyCalibrationChanged(EnergyCalibrationChangedEventArgs eventArgs)
        {
            energyCalibration = eventArgs.EnergyCalibration;
            if (EnergyCalibrationChanged != null)
            {
                EnergyCalibrationChanged(this, eventArgs);
            }
        }

        void Refresh()
        {
            if (Owner != null)
            {
                Owner.Refresh();
            }
        }

        public delegate void EnergyCalibrationChangedEventHandler(object sender, EnergyCalibrationChangedEventArgs e);
    }
}
