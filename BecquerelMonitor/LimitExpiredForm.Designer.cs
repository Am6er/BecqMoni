namespace BecquerelMonitor
{
	// Token: 0x0200002C RID: 44
	public partial class LimitExpiredForm : global::System.Windows.Forms.Form
	{
		// Token: 0x06000245 RID: 581 RVA: 0x00009154 File Offset: 0x00007354
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000246 RID: 582 RVA: 0x0000917C File Offset: 0x0000737C
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.LimitExpiredForm));
			this.label1 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.button1 = new global::System.Windows.Forms.Button();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.Controls.Add(this.button1);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.FormBorderStyle = global::System.Windows.Forms.FormBorderStyle.FixedDialog;
			base.MaximizeBox = false;
			base.MinimizeBox = false;
			base.Name = "LimitExpiredForm";
			base.TopMost = true;
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x040000A7 RID: 167
		global::System.ComponentModel.IContainer components;

		// Token: 0x040000A8 RID: 168
		global::System.Windows.Forms.Label label1;

		// Token: 0x040000A9 RID: 169
		global::System.Windows.Forms.Label label2;

		// Token: 0x040000AA RID: 170
		global::System.Windows.Forms.Button button1;
	}
}
