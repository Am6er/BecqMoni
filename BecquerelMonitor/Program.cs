using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x020000A1 RID: 161
	static class Program
	{
		// Token: 0x060007F7 RID: 2039 RVA: 0x0002C3FC File Offset: 0x0002A5FC
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
		}
	}
}
