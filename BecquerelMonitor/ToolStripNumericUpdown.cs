using System;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
    // Token: 0x02000104 RID: 260
    [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
    public class ToolStripNumericUpdown : ToolStripControlHost
    {
        // Token: 0x06000E43 RID: 3651 RVA: 0x00054214 File Offset: 0x00052414
        public ToolStripNumericUpdown() : base(new NumericUpDown())
        {
        }

        public NumericUpDown NumericUpDownControl
        {
            get
            {
                return Control as NumericUpDown;
            }
        }

        public decimal Value
        {
            get
            {
                return NumericUpDownControl.Value;
            }
            set
            {
                value = NumericUpDownControl.Value;
            }
        }

        protected override void OnSubscribeControlEvents(Control c)
        {
            base.OnSubscribeControlEvents(c);
            NumericUpDown mumControl = (NumericUpDown)c;
            mumControl.ValueChanged += new EventHandler(OnValueChanged);
        }

        protected override void OnUnsubscribeControlEvents(Control c)
        {
            base.OnUnsubscribeControlEvents(c);
            NumericUpDown mumControl = (NumericUpDown)c;
            mumControl.ValueChanged -= new EventHandler(OnValueChanged);
        }

        public event EventHandler ValueChanged;

        private void OnValueChanged(object sender, EventArgs e)
        {
            if (ValueChanged != null) ValueChanged(this, e);
        }
    }
}
