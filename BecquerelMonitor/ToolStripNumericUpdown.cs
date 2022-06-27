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
	}
}
