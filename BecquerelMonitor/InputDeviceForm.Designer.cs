using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x020000D6 RID: 214
	public partial class InputDeviceForm : UserControl
	{

		// Token: 0x06000AE9 RID: 2793 RVA: 0x000454FC File Offset: 0x000436FC
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Windows Form Designer generated code

        // Token: 0x06000AEA RID: 2794 RVA: 0x00045524 File Offset: 0x00043724
        void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(InputDeviceForm));
			this.textBox1 = new TextBox();
			this.label4 = new Label();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.ReadOnly = true;
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = AutoScaleMode.Font;
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.label4);
			base.Name = "InputDeviceForm";
			base.ResumeLayout(false);
			base.PerformLayout();
		}

        #endregion

        // Token: 0x040006B8 RID: 1720
        protected DeviceConfigForm deviceConfigForm;

		// Token: 0x040006B9 RID: 1721
		IContainer components;

		// Token: 0x040006BA RID: 1722
		TextBox textBox1;

		// Token: 0x040006BB RID: 1723
		Label label4;
	}
}
