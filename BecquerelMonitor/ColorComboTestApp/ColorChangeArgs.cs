using System;
using System.Drawing;

namespace ColorComboTestApp
{
	// Token: 0x020000D1 RID: 209
	public class ColorChangeArgs : EventArgs
	{
		// Token: 0x06000ABF RID: 2751 RVA: 0x0003FE40 File Offset: 0x0003E040
		public ColorChangeArgs(Color color)
		{
			this.color = color;
		}

		// Token: 0x040005F1 RID: 1521
		public Color color;
	}
}
