using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor
{
	// Token: 0x0200001A RID: 26
	public partial class DCSpectrumExplorerView : ToolWindow
	{
		// Token: 0x060000E1 RID: 225 RVA: 0x00004F98 File Offset: 0x00003198
		public DCSpectrumExplorerView(MainForm mainForm)
		{
			this.mainForm = mainForm;
			this.InitializeComponent();
			base.Icon = Resources.becqmoni;
		}

		// Token: 0x0400005B RID: 91
		MainForm mainForm;
	}
}
