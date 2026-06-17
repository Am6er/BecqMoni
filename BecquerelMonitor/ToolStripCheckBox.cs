using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
    [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
    public class ToolStripCheckBox : System.Windows.Forms.ToolStripControlHostDesignerSafe
    {
        public ToolStripCheckBox()
            : base(CreateControlInstance())
        {
        }

        static Control CreateControlInstance()
        {
            return new CheckBox();
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public CheckBox CheckBoxControl
        {
            get
            {
                return Control as CheckBox;
            }
        }

        [Category("Appearance")]
        public Appearance Appearance
        {
            get
            {
                return CheckBoxControl.Appearance;
            }
            set
            {
                CheckBoxControl.Appearance = value;
            }
        }

        [Category("Appearance")]
        public Image ButtonImage
        {
            get
            {
                return CheckBoxControl.Image;
            }
            set
            {
                CheckBoxControl.Image = value;
                if (CheckBoxControl.Image is Bitmap bitmap)
                {
                    bitmap.MakeTransparent();
                }
            }
        }

        [Category("Appearance")]
        public FlatStyle FlatStyle
        {
            get
            {
                return CheckBoxControl.FlatStyle;
            }
            set
            {
                CheckBoxControl.FlatStyle = value;
            }
        }
    }
}
