namespace BecquerelMonitor
{
	// Token: 0x020000C0 RID: 192
	public partial class DCSampleInfoView : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x0600093F RID: 2367 RVA: 0x00035AFC File Offset: 0x00033CFC
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000940 RID: 2368 RVA: 0x00035B24 File Offset: 0x00033D24
		void InitializeComponent()
		{
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCSampleInfoView));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.label9 = new global::System.Windows.Forms.Label();
			this.dateTimePicker2 = new global::System.Windows.Forms.DateTimePicker();
			this.numericUpDown2 = new global::System.Windows.Forms.NumericUpDown();
			this.numericUpDown1 = new global::System.Windows.Forms.NumericUpDown();
			this.label8 = new global::System.Windows.Forms.Label();
			this.label7 = new global::System.Windows.Forms.Label();
			this.textBox3 = new global::System.Windows.Forms.TextBox();
			this.textBox2 = new global::System.Windows.Forms.TextBox();
			this.label6 = new global::System.Windows.Forms.Label();
			this.label5 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label1 = new global::System.Windows.Forms.Label();
			this.textBox1 = new global::System.Windows.Forms.TextBox();
			this.dateTimePicker1 = new global::System.Windows.Forms.DateTimePicker();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown2).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown1).BeginInit();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			componentResourceManager.ApplyResources(this.dateTimePicker2, "dateTimePicker2");
			this.dateTimePicker2.Format = global::System.Windows.Forms.DateTimePickerFormat.Custom;
			this.dateTimePicker2.Name = "dateTimePicker2";
			this.dateTimePicker2.ValueChanged += new global::System.EventHandler(this.dateTimePicker2_ValueChanged);
			this.numericUpDown2.DecimalPlaces = 3;
			this.numericUpDown2.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				65536
			});
			componentResourceManager.ApplyResources(this.numericUpDown2, "numericUpDown2");
			this.numericUpDown2.Name = "numericUpDown2";
			this.numericUpDown2.ValueChanged += new global::System.EventHandler(this.numericUpDown2_ValueChanged);
			this.numericUpDown2.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown2_KeyDown);
			this.numericUpDown1.DecimalPlaces = 3;
			this.numericUpDown1.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				65536
			});
			componentResourceManager.ApplyResources(this.numericUpDown1, "numericUpDown1");
			this.numericUpDown1.Name = "numericUpDown1";
			this.numericUpDown1.ValueChanged += new global::System.EventHandler(this.numericUpDown1_ValueChanged);
			this.numericUpDown1.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.numericUpDown1_KeyDown);
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.textBox3, "textBox3");
			this.textBox3.Name = "textBox3";
			this.textBox3.TextChanged += new global::System.EventHandler(this.textBox3_TextChanged);
			this.textBox3.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.textBox3_KeyDown);
			this.textBox3.Validated += new global::System.EventHandler(this.textBox3_Validated);
			componentResourceManager.ApplyResources(this.textBox2, "textBox2");
			this.textBox2.Name = "textBox2";
			this.textBox2.TextChanged += new global::System.EventHandler(this.textBox2_TextChanged);
			this.textBox2.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.textBox2_KeyDown);
			this.textBox2.Validated += new global::System.EventHandler(this.textBox2_Validated);
			componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.TextChanged += new global::System.EventHandler(this.textBox1_TextChanged);
			this.textBox1.Validated += new global::System.EventHandler(this.textBox1_Validated);
			componentResourceManager.ApplyResources(this.dateTimePicker1, "dateTimePicker1");
			this.dateTimePicker1.Format = global::System.Windows.Forms.DateTimePickerFormat.Custom;
			this.dateTimePicker1.Name = "dateTimePicker1";
			this.dateTimePicker1.ValueChanged += new global::System.EventHandler(this.dateTimePicker1_ValueChanged);
			this.dateTimePicker1.Validated += new global::System.EventHandler(this.dateTimePicker1_Validated);
			componentResourceManager.ApplyResources(this, "$this");
			base.Controls.Add(this.label9);
			base.Controls.Add(this.dateTimePicker2);
			base.Controls.Add(this.numericUpDown2);
			base.Controls.Add(this.numericUpDown1);
			base.Controls.Add(this.label8);
			base.Controls.Add(this.label7);
			base.Controls.Add(this.textBox3);
			base.Controls.Add(this.textBox2);
			base.Controls.Add(this.label6);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.label4);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.dateTimePicker1);
			base.HideOnClose = true;
			base.Name = "DCSampleInfoView";
			base.FormClosed += new global::System.Windows.Forms.FormClosedEventHandler(this.DCSampleInfoView_FormClosed);
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown2).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDown1).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000519 RID: 1305
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400051A RID: 1306
		global::System.Windows.Forms.DateTimePicker dateTimePicker1;

		// Token: 0x0400051B RID: 1307
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x0400051C RID: 1308
		global::System.Windows.Forms.Label label1;

		// Token: 0x0400051D RID: 1309
		global::System.Windows.Forms.Label label2;

		// Token: 0x0400051E RID: 1310
		global::System.Windows.Forms.Label label3;

		// Token: 0x0400051F RID: 1311
		global::System.Windows.Forms.Label label4;

		// Token: 0x04000520 RID: 1312
		global::System.Windows.Forms.Label label5;

		// Token: 0x04000521 RID: 1313
		global::System.Windows.Forms.Label label6;

		// Token: 0x04000522 RID: 1314
		global::System.Windows.Forms.TextBox textBox2;

		// Token: 0x04000523 RID: 1315
		global::System.Windows.Forms.TextBox textBox3;

		// Token: 0x04000524 RID: 1316
		global::System.Windows.Forms.Label label7;

		// Token: 0x04000525 RID: 1317
		global::System.Windows.Forms.Label label8;

		// Token: 0x04000526 RID: 1318
		global::System.Windows.Forms.NumericUpDown numericUpDown1;

		// Token: 0x04000527 RID: 1319
		global::System.Windows.Forms.NumericUpDown numericUpDown2;

		// Token: 0x04000528 RID: 1320
		global::System.Windows.Forms.DateTimePicker dateTimePicker2;

		// Token: 0x04000529 RID: 1321
		global::System.Windows.Forms.Label label9;
	}
}
