using System;
using System.ComponentModel;
using System.Drawing;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
	// Token: 0x02000019 RID: 25
	public partial class DCSpectrumView : ToolWindow
	{
		// Token: 0x1700003E RID: 62
		// (get) Token: 0x060000DC RID: 220 RVA: 0x00004DD0 File Offset: 0x00002FD0
		// (set) Token: 0x060000DD RID: 221 RVA: 0x00004DD8 File Offset: 0x00002FD8
		public EnergySpectrumView EnergySpectrumView
		{
			get
			{
				return this.energySpectrumView1;
			}
			set
			{
				this.energySpectrumView1 = value;
			}
		}

		// Token: 0x060000DE RID: 222 RVA: 0x00004DE4 File Offset: 0x00002FE4
		public DCSpectrumView()
		{
			this.InitializeComponent();
			base.Icon = Resources.becqmoni;
		}
	}
}
