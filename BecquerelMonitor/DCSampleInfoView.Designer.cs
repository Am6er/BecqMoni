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
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCSampleInfoView));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.label9 = new global::System.Windows.Forms.Label();
			this.dateTimePicker2 = new global::System.Windows.Forms.DateTimePicker();
			this.numericUpDownVolume = new global::System.Windows.Forms.NumericUpDown();
			this.numericUpDownWeight = new global::System.Windows.Forms.NumericUpDown();
			this.label8 = new global::System.Windows.Forms.Label();
			this.label7 = new global::System.Windows.Forms.Label();
			this.textBoxLocation = new global::System.Windows.Forms.TextBox();
			this.textBoxName = new global::System.Windows.Forms.TextBox();
			this.label6 = new global::System.Windows.Forms.Label();
			this.label5 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.label1 = new global::System.Windows.Forms.Label();
			this.textBoxNote = new global::System.Windows.Forms.TextBox();
			this.dateTimePickerSampleTime = new global::System.Windows.Forms.DateTimePicker();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDownVolume).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDownWeight).BeginInit();
			base.SuspendLayout();
			resources.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			resources.ApplyResources(this.dateTimePicker2, "dateTimePicker2");
			this.dateTimePicker2.Format = global::System.Windows.Forms.DateTimePickerFormat.Custom;
			this.dateTimePicker2.Name = "dateTimePicker2";
			this.dateTimePicker2.ValueChanged += new global::System.EventHandler(this.dateTimePickerSampleTime_ValueChanged);
			this.numericUpDownVolume.DecimalPlaces = 3;
			this.numericUpDownVolume.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				65536
			});
			resources.ApplyResources(this.numericUpDownVolume, "numericUpDown2");
			this.numericUpDownVolume.Name = "numericUpDown2";
			this.numericUpDownVolume.ValueChanged += new global::System.EventHandler(this.numericUpDownVolume_ValueChanged);
			this.numericUpDownWeight.DecimalPlaces = 3;
			this.numericUpDownWeight.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				65536
			});
			resources.ApplyResources(this.numericUpDownWeight, "numericUpDown1");
			this.numericUpDownWeight.Name = "numericUpDown1";
			this.numericUpDownWeight.ValueChanged += new global::System.EventHandler(this.numericUpDownWeight_ValueChanged);
			resources.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			resources.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			resources.ApplyResources(this.textBoxLocation, "textBox3");
			this.textBoxLocation.Name = "textBox3";
			this.textBoxLocation.TextChanged += new global::System.EventHandler(this.textBoxLocation_Changed);
			resources.ApplyResources(this.textBoxName, "textBox2");
			this.textBoxName.Name = "textBox2";
			this.textBoxName.TextChanged += new global::System.EventHandler(this.textBoxName_Changed);
			resources.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			resources.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			resources.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			resources.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			resources.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			resources.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			resources.ApplyResources(this.textBoxNote, "textBox1");
			this.textBoxNote.Name = "textBox1";
			this.textBoxNote.TextChanged += new global::System.EventHandler(this.textBoxNote_TextChanged);
			resources.ApplyResources(this.dateTimePickerSampleTime, "dateTimePicker1");
			this.dateTimePickerSampleTime.Format = global::System.Windows.Forms.DateTimePickerFormat.Custom;
			this.dateTimePickerSampleTime.Name = "dateTimePicker1";
			this.dateTimePickerSampleTime.ValueChanged += new global::System.EventHandler(this.dateTimePickerSampleTime_ValueChanged);
			resources.ApplyResources(this, "$this");
			base.Controls.Add(this.label9);
			base.Controls.Add(this.dateTimePicker2);
			base.Controls.Add(this.numericUpDownVolume);
			base.Controls.Add(this.numericUpDownWeight);
			base.Controls.Add(this.label8);
			base.Controls.Add(this.label7);
			base.Controls.Add(this.textBoxLocation);
			base.Controls.Add(this.textBoxName);
			base.Controls.Add(this.label6);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.label4);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.textBoxNote);
			base.Controls.Add(this.dateTimePickerSampleTime);
			base.HideOnClose = true;
			base.Name = "DCSampleInfoView";
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDownVolume).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.numericUpDownWeight).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000519 RID: 1305
		global::System.ComponentModel.IContainer components;

		// Token: 0x0400051A RID: 1306
		global::System.Windows.Forms.DateTimePicker dateTimePickerSampleTime;

		// Token: 0x0400051B RID: 1307
		global::System.Windows.Forms.TextBox textBoxNote;

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
		global::System.Windows.Forms.TextBox textBoxName;

		// Token: 0x04000523 RID: 1315
		global::System.Windows.Forms.TextBox textBoxLocation;

		// Token: 0x04000524 RID: 1316
		global::System.Windows.Forms.Label label7;

		// Token: 0x04000525 RID: 1317
		global::System.Windows.Forms.Label label8;

		// Token: 0x04000526 RID: 1318
		global::System.Windows.Forms.NumericUpDown numericUpDownWeight;

		// Token: 0x04000527 RID: 1319
		global::System.Windows.Forms.NumericUpDown numericUpDownVolume;

		// Token: 0x04000528 RID: 1320
		global::System.Windows.Forms.DateTimePicker dateTimePicker2;

		// Token: 0x04000529 RID: 1321
		global::System.Windows.Forms.Label label9;
	}
}
