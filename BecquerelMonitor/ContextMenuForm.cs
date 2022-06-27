using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x02000050 RID: 80
	public partial class ContextMenuForm : Form
	{
		// Token: 0x17000179 RID: 377
		// (get) Token: 0x06000446 RID: 1094 RVA: 0x00014D04 File Offset: 0x00012F04
		// (set) Token: 0x06000447 RID: 1095 RVA: 0x00014D0C File Offset: 0x00012F0C
		public bool Locked
		{
			get
			{
				return this._locked;
			}
			set
			{
				this._locked = value;
			}
		}

		// Token: 0x06000448 RID: 1096 RVA: 0x00014D18 File Offset: 0x00012F18
		public ContextMenuForm()
		{
			this.InitializeComponent();
		}

		// Token: 0x06000449 RID: 1097 RVA: 0x00014D28 File Offset: 0x00012F28
		public void Show(Control parent, Point startLocation, int width)
		{
			this._parentControl = parent;
			Point location = parent.PointToScreen(startLocation);
			base.Location = location;
			base.Width = width;
			base.Show();
		}

		// Token: 0x0600044A RID: 1098 RVA: 0x00014D5C File Offset: 0x00012F5C
		public void SetContainingControl(Control control)
		{
			this.panelMain.Controls.Clear();
			control.Dock = DockStyle.Fill;
			this.panelMain.Controls.Add(control);
		}

		// Token: 0x0600044B RID: 1099 RVA: 0x00014D98 File Offset: 0x00012F98
		void ContextMenuPanel_Deactivate(object sender, EventArgs e)
		{
			if (!this.Locked)
			{
				this.Hide();
			}
		}

		// Token: 0x0600044C RID: 1100 RVA: 0x00014DAC File Offset: 0x00012FAC
		void ContextMenuPanel_Leave(object sender, EventArgs e)
		{
			if (!this.Locked)
			{
				this.Hide();
			}
		}

		// Token: 0x0600044D RID: 1101 RVA: 0x00014DC0 File Offset: 0x00012FC0
		public new void Hide()
		{
			base.Hide();
			if (this._parentControl != null)
			{
				Form form = this._parentControl.FindForm();
				if (form != null)
				{
					form.BringToFront();
				}
			}
		}

		// Token: 0x040001C6 RID: 454
		bool _locked;

		// Token: 0x040001C7 RID: 455
		Control _parentControl;
	}
}
