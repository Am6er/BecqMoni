using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
    // Token: 0x020000BB RID: 187
    [ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
    public class ToolStripRadioButton : ToolStripControlHost
    {
        // Token: 0x060008EC RID: 2284 RVA: 0x00031D64 File Offset: 0x0002FF64
        public ToolStripRadioButton() : base(new RadioButton())
        {
        }
    }
}
