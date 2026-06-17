using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
    [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
    public class ToolStripNumericUpdown : ToolStripControlHostDesignerSafe
    {
        ToolTip hostedControlToolTip;

        public ToolStripNumericUpdown()
            : base(CreateControlInstance())
        {
            hostedControlToolTip = new ToolTip();
        }

        static Control CreateControlInstance()
        {
            return new NumericUpDown();
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public NumericUpDown NumericUpDownControl
        {
            get
            {
                return Control as NumericUpDown;
            }
        }

        [Category("Behavior")]
        public int DecimalPlaces
        {
            get
            {
                return NumericUpDownControl.DecimalPlaces;
            }
            set
            {
                NumericUpDownControl.DecimalPlaces = value;
            }
        }

        [Category("Behavior")]
        public decimal Increment
        {
            get
            {
                return NumericUpDownControl.Increment;
            }
            set
            {
                NumericUpDownControl.Increment = value;
            }
        }

        [Category("Behavior")]
        public decimal Maximum
        {
            get
            {
                return NumericUpDownControl.Maximum;
            }
            set
            {
                NumericUpDownControl.Maximum = value;
            }
        }

        [Category("Behavior")]
        public decimal Minimum
        {
            get
            {
                return NumericUpDownControl.Minimum;
            }
            set
            {
                NumericUpDownControl.Minimum = value;
            }
        }

        [Category("Behavior")]
        public decimal Value
        {
            get
            {
                return NumericUpDownControl.Value;
            }
            set
            {
                NumericUpDownControl.Value = value;
            }
        }

        [Browsable(true)]
        [Category("Appearance")]
        [Localizable(true)]
        [DefaultValue("")]
        public new string ToolTipText
        {
            get
            {
                return base.ToolTipText;
            }
            set
            {
                base.ToolTipText = value;
                UpdateHostedControlToolTip();
            }
        }

        protected override void OnSubscribeControlEvents(Control c)
        {
            base.OnSubscribeControlEvents(c);
            NumericUpDown numControl = (NumericUpDown)c;
            numControl.ValueChanged += new EventHandler(OnValueChanged);
            UpdateHostedControlToolTip();
        }

        protected override void OnUnsubscribeControlEvents(Control c)
        {
            base.OnUnsubscribeControlEvents(c);
            NumericUpDown numControl = (NumericUpDown)c;
            numControl.ValueChanged -= new EventHandler(OnValueChanged);
        }

        public event EventHandler ValueChanged;

        void OnValueChanged(object sender, EventArgs e)
        {
            if (ValueChanged != null)
            {
                ValueChanged(this, e);
            }
        }

        void UpdateHostedControlToolTip()
        {
            NumericUpDown control = NumericUpDownControl;
            if (control == null || control.IsDisposed)
            {
                return;
            }

            if (hostedControlToolTip == null)
            {
                hostedControlToolTip = new ToolTip();
            }

            hostedControlToolTip.SetToolTip(control, base.ToolTipText ?? string.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hostedControlToolTip.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
