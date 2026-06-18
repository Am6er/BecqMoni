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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCSampleInfoView));
            this.label9 = new System.Windows.Forms.Label();
            this.dateTimePicker2 = new System.Windows.Forms.DateTimePicker();
            this.numericUpDownVolume = new System.Windows.Forms.NumericUpDown();
            this.numericUpDownWeight = new System.Windows.Forms.NumericUpDown();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.textBoxLocation = new System.Windows.Forms.TextBox();
            this.textBoxName = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.textBoxNote = new System.Windows.Forms.TextBox();
            this.dateTimePickerSampleTime = new System.Windows.Forms.DateTimePicker();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownVolume)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownWeight)).BeginInit();
            this.SuspendLayout();
            // 
            // label9
            // 
            resources.ApplyResources(this.label9, "label9");
            this.label9.Name = "label9";
            // 
            // dateTimePicker2
            // 
            resources.ApplyResources(this.dateTimePicker2, "dateTimePicker2");
            this.dateTimePicker2.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateTimePicker2.Name = "dateTimePicker2";
            this.dateTimePicker2.ValueChanged += new System.EventHandler(this.dateTimePickerSampleTime_ValueChanged);
            // 
            // numericUpDownVolume
            // 
            this.numericUpDownVolume.DecimalPlaces = 3;
            this.numericUpDownVolume.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            resources.ApplyResources(this.numericUpDownVolume, "numericUpDownVolume");
            this.numericUpDownVolume.Name = "numericUpDownVolume";
            this.numericUpDownVolume.ValueChanged += new System.EventHandler(this.numericUpDownVolume_ValueChanged);
            // 
            // numericUpDownWeight
            // 
            this.numericUpDownWeight.DecimalPlaces = 3;
            this.numericUpDownWeight.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            resources.ApplyResources(this.numericUpDownWeight, "numericUpDownWeight");
            this.numericUpDownWeight.Name = "numericUpDownWeight";
            this.numericUpDownWeight.ValueChanged += new System.EventHandler(this.numericUpDownWeight_ValueChanged);
            // 
            // label8
            // 
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            // 
            // textBoxLocation
            // 
            resources.ApplyResources(this.textBoxLocation, "textBoxLocation");
            this.textBoxLocation.Name = "textBoxLocation";
            this.textBoxLocation.TextChanged += new System.EventHandler(this.textBoxLocation_Changed);
            // 
            // textBoxName
            // 
            resources.ApplyResources(this.textBoxName, "textBoxName");
            this.textBoxName.Name = "textBoxName";
            this.textBoxName.TextChanged += new System.EventHandler(this.textBoxName_Changed);
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // textBoxNote
            // 
            resources.ApplyResources(this.textBoxNote, "textBoxNote");
            this.textBoxNote.Name = "textBoxNote";
            this.textBoxNote.TextChanged += new System.EventHandler(this.textBoxNote_TextChanged);
            // 
            // dateTimePickerSampleTime
            // 
            resources.ApplyResources(this.dateTimePickerSampleTime, "dateTimePickerSampleTime");
            this.dateTimePickerSampleTime.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateTimePickerSampleTime.Name = "dateTimePickerSampleTime";
            this.dateTimePickerSampleTime.ValueChanged += new System.EventHandler(this.dateTimePickerSampleTime_ValueChanged);
            // 
            // DCSampleInfoView
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.label9);
            this.Controls.Add(this.dateTimePicker2);
            this.Controls.Add(this.numericUpDownVolume);
            this.Controls.Add(this.numericUpDownWeight);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.textBoxLocation);
            this.Controls.Add(this.textBoxName);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBoxNote);
            this.Controls.Add(this.dateTimePickerSampleTime);
            this.HideOnClose = true;
            this.Name = "DCSampleInfoView";
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownVolume)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownWeight)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		// Token: 0x04000519 RID: 1305
		global::System.ComponentModel.IContainer components = null;

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
