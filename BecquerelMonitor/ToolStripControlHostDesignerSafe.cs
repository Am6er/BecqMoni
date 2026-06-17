using System.Windows.Forms;

namespace System.Windows.Forms
{
    // WinForms designer can fail to deserialize custom ToolStripControlHost
    // items because ToolStripControlHost itself has no parameterless ctor.
    public class ToolStripControlHostDesignerSafe : ToolStripControlHost
    {
        public ToolStripControlHostDesignerSafe()
            : base(new Control())
        {
        }

        public ToolStripControlHostDesignerSafe(Control control)
            : base(control)
        {
        }
    }
}
