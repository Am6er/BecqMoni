using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace BecquerelMonitor
{
	// Token: 0x02000039 RID: 57
	[ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip | ToolStripItemDesignerAvailability.StatusStrip)]
	public class ToolStripCheckBox : ToolStripControlHost
	{
		// Token: 0x060002CB RID: 715 RVA: 0x0000D560 File Offset: 0x0000B760
		public ToolStripCheckBox() : base(new CheckBox())
		{
			this.checkBox = (CheckBox)base.Control;
		}

		// Token: 0x1700012C RID: 300
		// (get) Token: 0x060002CC RID: 716 RVA: 0x0000D580 File Offset: 0x0000B780
		// (set) Token: 0x060002CD RID: 717 RVA: 0x0000D590 File Offset: 0x0000B790
		public Appearance Appearance
		{
			get
			{
				return this.checkBox.Appearance;
			}
			set
			{
				this.checkBox.Appearance = value;
			}
		}

		// Token: 0x1700012D RID: 301
		// (get) Token: 0x060002CE RID: 718 RVA: 0x0000D5A0 File Offset: 0x0000B7A0
		// (set) Token: 0x060002CF RID: 719 RVA: 0x0000D5B0 File Offset: 0x0000B7B0
		public Image ButtonImage
		{
			get
			{
				return this.checkBox.Image;
			}
			set
			{
				this.checkBox.Image = value;
				if (this.checkBox.Image is Bitmap)
				{
					((Bitmap)this.checkBox.Image).MakeTransparent();
				}
			}
		}

		// Token: 0x1700012E RID: 302
		// (get) Token: 0x060002D0 RID: 720 RVA: 0x0000D5E8 File Offset: 0x0000B7E8
		// (set) Token: 0x060002D1 RID: 721 RVA: 0x0000D5F8 File Offset: 0x0000B7F8
		public FlatStyle FlatStyle
		{
			get
			{
				return this.checkBox.FlatStyle;
			}
			set
			{
				this.checkBox.FlatStyle = value;
			}
		}

		// Token: 0x04000100 RID: 256
		CheckBox checkBox;
	}
}
