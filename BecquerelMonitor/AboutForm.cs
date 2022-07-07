using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using System.Deployment.Application;

namespace BecquerelMonitor
{
	// Token: 0x020000C5 RID: 197
	public partial class AboutForm : Form
	{
		// Token: 0x06000978 RID: 2424 RVA: 0x00037A54 File Offset: 0x00035C54
		public AboutForm()
		{
			this.InitializeComponent();

            global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.AboutForm));
            componentResourceManager.ApplyResources(this.label1, "label1");
            componentResourceManager.ApplyResources(this.label2, "label2");
            componentResourceManager.ApplyResources(this.label3, "label3");
            componentResourceManager.ApplyResources(this.label4, "label4");
            componentResourceManager.ApplyResources(this.label5, "label5");
            componentResourceManager.ApplyResources(this.button1, "button1");
            componentResourceManager.ApplyResources(this.textBox1, "textBox1");
            componentResourceManager.ApplyResources(this.iconPanel1, "iconPanel1");
            componentResourceManager.ApplyResources(this.label6, "label6");
            componentResourceManager.ApplyResources(this, "$this");


			base.Icon = Resources.becqmoni;

			try
			{
				ApplicationDeployment currentDeployment = ApplicationDeployment.CurrentDeployment;
				this.label2.Text = "Version + " + currentDeployment.CurrentVersion.ToString();
			}
			catch
			{
				this.label2.Text = string.Format(Resources.VersionString, GlobalConfigManager.GetInstance().VersionString);
			}
			
			this.textBox1.Text = Resources.LibraryLicensesMessage;
			this.RecalcPosition();
		}

		// Token: 0x06000979 RID: 2425 RVA: 0x00037AB4 File Offset: 0x00035CB4
		void button1_Click(object sender, EventArgs e)
		{
			base.Close();
		}

		// Token: 0x0600097A RID: 2426 RVA: 0x00037ABC File Offset: 0x00035CBC
		void textBox1_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x0600097B RID: 2427 RVA: 0x00037AC0 File Offset: 0x00035CC0
		void AboutForm_SizeChanged(object sender, EventArgs e)
		{
			this.RecalcPosition();
		}

		// Token: 0x0600097C RID: 2428 RVA: 0x00037AC8 File Offset: 0x00035CC8
		void RecalcPosition()
		{
			this.iconPanel1.Width = this.iconPanel1.Height;
			this.label1.Left = this.iconPanel1.Width + 20;
			this.label2.Left = this.iconPanel1.Width + 20;
			this.label3.Left = this.iconPanel1.Width + 20;
		}
	}
}
