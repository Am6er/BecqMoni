using System;
using System.Drawing;
using BecquerelMonitor.Properties;
using XPTable.Events;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor
{
	// Token: 0x02000016 RID: 22
	public class ResultValueCellRenderer : TextCellRenderer
	{
		// Token: 0x060000C0 RID: 192 RVA: 0x00003CCC File Offset: 0x00001ECC
		public ResultValueCellRenderer()
		{
			this.ndBitmap = new Bitmap(Resources.NDBitmap);
			this.ndBitmap.MakeTransparent(Color.White);
		}

		// Token: 0x060000C1 RID: 193 RVA: 0x00003CF4 File Offset: 0x00001EF4
		protected override void OnPaint(PaintCellEventArgs e)
		{
			base.OnPaint(e);
			Cell cell = e.Cell;
			if (cell == null)
			{
				return;
			}
			Graphics graphics = e.Graphics;
			if (this.font == null)
			{
				this.font = new Font(base.Font.FontFamily, 8f);
			}
			if (!(bool)cell.Tag)
			{
				Rectangle clientRectangle = this.ClientRectangle;
				clientRectangle.X += 2;
				clientRectangle.Width = 18;
				clientRectangle.Height -= 2;
				graphics.DrawImage(this.ndBitmap, clientRectangle.X, clientRectangle.Y);
			}
		}

		// Token: 0x04000036 RID: 54
		Font font;

		// Token: 0x04000037 RID: 55
		Bitmap ndBitmap;
	}
}
