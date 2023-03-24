using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
	// Token: 0x02000041 RID: 65
	public class ThermometerForm : UserControl
	{
		// Token: 0x1700015B RID: 347
		// (get) Token: 0x0600039C RID: 924 RVA: 0x00011560 File Offset: 0x0000F760
		// (set) Token: 0x0600039D RID: 925 RVA: 0x00011570 File Offset: 0x0000F770
		public string ThermometerTypeString
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

		// Token: 0x0600039E RID: 926 RVA: 0x00011580 File Offset: 0x0000F780
		public ThermometerForm()
		{
			this.InitializeComponent();
		}

		// Token: 0x0600039F RID: 927 RVA: 0x00011590 File Offset: 0x0000F790
		public ThermometerForm(DeviceConfigForm deviceConfigForm)
		{
			this.InitializeComponent();
			this.deviceConfigForm = deviceConfigForm;
			this.ThermometerTypeString = Resources.ThermometerTypeNone;
		}

		// Token: 0x060003A0 RID: 928 RVA: 0x000115B0 File Offset: 0x0000F7B0
		public virtual void Initialize()
		{
		}

		// Token: 0x060003A1 RID: 929 RVA: 0x000115B4 File Offset: 0x0000F7B4
		public virtual void FormClosing()
		{
		}

		// Token: 0x060003A2 RID: 930 RVA: 0x000115B8 File Offset: 0x0000F7B8
		public virtual void LoadFormContents(ThermometerConfig config)
		{
		}

		// Token: 0x060003A3 RID: 931 RVA: 0x000115BC File Offset: 0x0000F7BC
		public virtual bool SaveFormContents(ThermometerConfig config)
		{
			return true;
		}

		// Token: 0x060003A4 RID: 932 RVA: 0x000115C0 File Offset: 0x0000F7C0
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060003A5 RID: 933 RVA: 0x000115E8 File Offset: 0x0000F7E8
		void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(ThermometerForm));
			this.textBox1 = new TextBox();
			this.label4 = new Label();
			this.textBox2 = new TextBox();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.ReadOnly = true;
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			this.textBox2.BackColor = SystemColors.Control;
			componentResourceManager.ApplyResources(this.textBox2, "textBox2");
			this.textBox2.Name = "textBox2";
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = AutoScaleMode.Font;
			base.Controls.Add(this.textBox2);
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.label4);
			base.Name = "ThermometerForm";
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000172 RID: 370
		protected DeviceConfigForm deviceConfigForm;

		// Token: 0x04000173 RID: 371
		IContainer components;

		// Token: 0x04000174 RID: 372
		TextBox textBox1;

		// Token: 0x04000175 RID: 373
		Label label4;

		// Token: 0x04000176 RID: 374
		TextBox textBox2;
	}
}
