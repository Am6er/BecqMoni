using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
	// Token: 0x0200006C RID: 108
	public partial class StartupForm : Form
	{
		// Token: 0x060005A0 RID: 1440 RVA: 0x000239F8 File Offset: 0x00021BF8
		public StartupForm()
		{
			this.InitializeComponent();
			base.Icon = Resources.becqmoni;
			CultureInfo currentUICulture = Thread.CurrentThread.CurrentUICulture;
			if (this.label5.Text.StartsWith("起動"))
			{
				this.panel2.BackgroundImage = Resources.ベクモニロゴ;
			}
			this.textBox1.HideSelection = false;
			GlobalConfigManager instance = GlobalConfigManager.GetInstance();
			this.label3.Text = string.Format(Resources.VersionString, instance.VersionString) + " ";
		}

		// Token: 0x170001B5 RID: 437
		// (get) Token: 0x060005A1 RID: 1441 RVA: 0x00023A8C File Offset: 0x00021C8C
		// (set) Token: 0x060005A2 RID: 1442 RVA: 0x00023A9C File Offset: 0x00021C9C
		public string MessageText
		{
			get
			{
				return this.textBox1.Text;
			}
			set
			{
				this.textBox1.Text = value;
			}
		}

		// Token: 0x060005A3 RID: 1443 RVA: 0x00023AAC File Offset: 0x00021CAC
		public void AppendMessage(string message)
		{
			this.textBox1.AppendText(message + "\r\n");
			this.Refresh();
		}
	}
}
