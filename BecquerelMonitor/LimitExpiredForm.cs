using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x0200002C RID: 44
	public partial class LimitExpiredForm : Form
	{
		// Token: 0x06000243 RID: 579 RVA: 0x0000913C File Offset: 0x0000733C
		public LimitExpiredForm()
		{
			this.InitializeComponent();
		}

		// Token: 0x06000244 RID: 580 RVA: 0x0000914C File Offset: 0x0000734C
		void button1_Click(object sender, EventArgs e)
		{
			base.Close();
		}
	}
}
