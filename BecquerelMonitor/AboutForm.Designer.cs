namespace BecquerelMonitor
{
	// Token: 0x020000C5 RID: 197
	public partial class AboutForm : global::System.Windows.Forms.Form
	{
		// Token: 0x06000976 RID: 2422 RVA: 0x00037750 File Offset: 0x00035950
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000977 RID: 2423 RVA: 0x00037778 File Offset: 0x00035978
		void InitializeComponent()
		{
			
			this.label1 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label5 = new global::System.Windows.Forms.Label();
			this.button1 = new global::System.Windows.Forms.Button();
			this.textBox1 = new global::System.Windows.Forms.TextBox();
			this.iconPanel1 = new global::BecquerelMonitor.IconPanel();
			this.label6 = new global::System.Windows.Forms.Label();
			base.SuspendLayout();
			
			this.label1.Name = "label1";
			
			this.label2.Name = "label2";
			
			this.label3.Name = "label3";
			
			this.label4.Name = "label4";
			
			this.label5.Name = "label5";
			
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			
			this.textBox1.Name = "textBox1";
			this.textBox1.ReadOnly = true;
			this.textBox1.TextChanged += new global::System.EventHandler(this.textBox1_TextChanged);
			
			this.iconPanel1.Name = "iconPanel1";
			
			this.label6.Name = "label6";
			
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = global::System.Drawing.Color.AliceBlue;
			base.Controls.Add(this.label6);
			base.Controls.Add(this.iconPanel1);
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.button1);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.label4);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Name = "AboutForm";
			base.SizeChanged += new global::System.EventHandler(this.AboutForm_SizeChanged);
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000545 RID: 1349
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000546 RID: 1350
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000547 RID: 1351
		global::System.Windows.Forms.Label label2;

		// Token: 0x04000548 RID: 1352
		global::System.Windows.Forms.Label label3;

		// Token: 0x04000549 RID: 1353
		global::System.Windows.Forms.Label label4;

		// Token: 0x0400054A RID: 1354
		global::System.Windows.Forms.Label label5;

		// Token: 0x0400054B RID: 1355
		global::System.Windows.Forms.Button button1;

		// Token: 0x0400054C RID: 1356
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x0400054D RID: 1357
		global::BecquerelMonitor.IconPanel iconPanel1;

		// Token: 0x0400054E RID: 1358
		global::System.Windows.Forms.Label label6;
	}
}
