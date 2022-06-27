namespace BecquerelMonitor
{
	// Token: 0x0200003D RID: 61
	public partial class DCDebugPanel : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x06000376 RID: 886 RVA: 0x00010F10 File Offset: 0x0000F110
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000377 RID: 887 RVA: 0x00010F38 File Offset: 0x0000F138
		void InitializeComponent()
		{
            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);

			this.textBox9 = new global::System.Windows.Forms.TextBox();
			this.button5 = new global::System.Windows.Forms.Button();
			this.button4 = new global::System.Windows.Forms.Button();
			this.groupBox1 = new global::System.Windows.Forms.GroupBox();
			this.button6 = new global::System.Windows.Forms.Button();
			this.groupBox1.SuspendLayout();
			base.SuspendLayout();
			this.textBox9.Location = new global::System.Drawing.Point(15, 23);
			this.textBox9.Name = "textBox9";
			this.textBox9.Size = new global::System.Drawing.Size(180, 19);
			this.textBox9.TabIndex = 54;
			this.textBox9.TabStop = false;
			this.textBox9.Text = "sample2H-v65-1.wav";
			this.button5.Enabled = false;
			this.button5.Location = new global::System.Drawing.Point(109, 48);
			this.button5.Name = "button5";
			this.button5.Size = new global::System.Drawing.Size(86, 23);
			this.button5.TabIndex = 53;
			this.button5.Text = "デバッグ中止";
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new global::System.EventHandler(this.button5_Click);
			this.button4.Location = new global::System.Drawing.Point(15, 48);
			this.button4.Name = "button4";
			this.button4.Size = new global::System.Drawing.Size(88, 23);
			this.button4.TabIndex = 52;
			this.button4.Text = "2時間デバッグ";
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new global::System.EventHandler(this.button4_Click);
			this.groupBox1.Controls.Add(this.button6);
			this.groupBox1.Controls.Add(this.textBox9);
			this.groupBox1.Controls.Add(this.button5);
			this.groupBox1.Controls.Add(this.button4);
			this.groupBox1.Location = new global::System.Drawing.Point(12, 12);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new global::System.Drawing.Size(210, 115);
			this.groupBox1.TabIndex = 62;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "デバッグ用";
			this.button6.Location = new global::System.Drawing.Point(15, 77);
			this.button6.Name = "button6";
			this.button6.Size = new global::System.Drawing.Size(88, 23);
			this.button6.TabIndex = 55;
			this.button6.Text = "8時間デバッグ";
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new global::System.EventHandler(this.button6_Click);
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(6f, 12f);
			base.ClientSize = new global::System.Drawing.Size(355, 199);
			base.Controls.Add(this.groupBox1);
			base.HideOnClose = true;
			base.Name = "DCDebugPanel";
			base.TabText = "デバッグ用";
			this.Text = "デバッグ用";
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			base.ResumeLayout(false);
		}

		// Token: 0x0400015D RID: 349
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400015E RID: 350
		global::System.Windows.Forms.TextBox textBox9;

		// Token: 0x0400015F RID: 351
		global::System.Windows.Forms.Button button5;

		// Token: 0x04000160 RID: 352
		global::System.Windows.Forms.Button button4;

		// Token: 0x04000161 RID: 353
		global::System.Windows.Forms.GroupBox groupBox1;

		// Token: 0x04000162 RID: 354
		global::System.Windows.Forms.Button button6;
	}
}
