using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x02000063 RID: 99
	public class ROIPrimitiveControl : UserControl
	{
		// Token: 0x14000013 RID: 19
		// (add) Token: 0x060004F0 RID: 1264 RVA: 0x0001CF78 File Offset: 0x0001B178
		// (remove) Token: 0x060004F1 RID: 1265 RVA: 0x0001CFB4 File Offset: 0x0001B1B4
		public event EventHandler ROIPrimitiveModified;

		// Token: 0x060004F2 RID: 1266 RVA: 0x0001CFF0 File Offset: 0x0001B1F0
		public ROIPrimitiveControl()
		{
			this.InitializeComponent();
		}

		// Token: 0x060004F3 RID: 1267 RVA: 0x0001D000 File Offset: 0x0001B200
		public virtual void LoadFormContents(ROIPrimitiveData prim)
		{
		}

		// Token: 0x060004F4 RID: 1268 RVA: 0x0001D004 File Offset: 0x0001B204
		public virtual bool SaveFormContents(ROIPrimitiveData prim)
		{
			prim = new ROIPrimitiveData();
			return true;
		}

		// Token: 0x060004F5 RID: 1269 RVA: 0x0001D010 File Offset: 0x0001B210
		public virtual void PrepareForm(ROIConfigData config)
		{
		}

		// Token: 0x060004F6 RID: 1270 RVA: 0x0001D014 File Offset: 0x0001B214
		protected void PrimitiveModified()
		{
			if (this.ROIPrimitiveModified != null)
			{
				this.ROIPrimitiveModified(this, new EventArgs());
			}
		}

		// Token: 0x060004F7 RID: 1271 RVA: 0x0001D034 File Offset: 0x0001B234
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060004F8 RID: 1272 RVA: 0x0001D05C File Offset: 0x0001B25C
		void InitializeComponent()
		{
			base.SuspendLayout();
			base.AutoScaleDimensions = new SizeF(6f, 12f);
			base.AutoScaleMode = AutoScaleMode.Font;
			base.Name = "ROIPrimitiveControl";
			base.Size = new Size(510, 169);
			base.ResumeLayout(false);
		}

		// Token: 0x04000240 RID: 576
		IContainer components;
	}
}
