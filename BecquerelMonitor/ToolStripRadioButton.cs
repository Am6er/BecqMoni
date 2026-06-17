using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
    [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
    public class ToolStripRadioButton : System.Windows.Forms.ToolStripControlHostDesignerSafe
    {
        public ToolStripRadioButton()
            : base(CreateControlInstance())
        {
        }

        static Control CreateControlInstance()
        {
            return new RadioButton();
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public RadioButton RadioButtonControl
        {
            get
            {
                return Control as RadioButton;
            }
        }
    }
}
