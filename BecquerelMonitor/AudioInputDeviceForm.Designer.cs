using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using WinMM;

namespace BecquerelMonitor
{
	// Token: 0x02000142 RID: 322
	public partial class AudioInputDeviceForm : InputDeviceForm
	{
		// Token: 0x0600103B RID: 4155 RVA: 0x00058AF8 File Offset: 0x00056CF8
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}
		
		#region Windows Form Designer generated code

		// Token: 0x0600103C RID: 4156 RVA: 0x00058B20 File Offset: 0x00056D20
		void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(AudioInputDeviceForm));
			this.label37 = new Label();
			this.checkBox2 = new CheckBox();
			this.checkBox1 = new CheckBox();
			this.label15 = new Label();
			this.maskedTextBox1 = new MaskedTextBox();
			this.label29 = new Label();
			this.comboBox1 = new ComboBox();
			this.comboBox2 = new ComboBox();
			this.comboBox3 = new ComboBox();
			this.label1 = new Label();
			this.label2 = new Label();
			this.label3 = new Label();
			this.trackBar1 = new TrackBar();
			this.label11 = new Label();
			this.label30 = new Label();
			this.maskedTextBox2 = new MaskedTextBox();
			this.doubleTextBox4 = new DoubleTextBox();
			this.doubleTextBox3 = new DoubleTextBox();
			this.textBox7 = new TextBox();
			this.label6 = new Label();
			this.label9 = new Label();
			this.label8 = new Label();
			this.numericUpDown2 = new NumericUpDown();
			this.label7 = new Label();
			this.textBox11 = new TextBox();
			this.numericUpDown1 = new NumericUpDown();
			this.label17 = new Label();
			this.standardPulseView1 = new StandardPulseView();
			this.button1 = new Button();
			this.textBox4 = new TextBox();
			this.button2 = new Button();
			this.label10 = new Label();
			this.trackBar2 = new TrackBar();
			this.label14 = new Label();
			this.label12 = new Label();
			this.label13 = new Label();
			this.doubleTextBox2 = new DoubleTextBox();
			this.doubleTextBox1 = new DoubleTextBox();
			this.panel1 = new Panel();
			this.label5 = new Label();
			((ISupportInitialize)this.trackBar1).BeginInit();
			((ISupportInitialize)this.numericUpDown2).BeginInit();
			((ISupportInitialize)this.numericUpDown1).BeginInit();
			((ISupportInitialize)this.trackBar2).BeginInit();
			this.panel1.SuspendLayout();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.label37, "label37");
			this.label37.Name = "label37";
			componentResourceManager.ApplyResources(this.checkBox2, "checkBox2");
			this.checkBox2.Name = "checkBox2";
			this.checkBox2.UseVisualStyleBackColor = true;
			this.checkBox2.CheckedChanged += this.checkBox2_CheckedChanged;
			componentResourceManager.ApplyResources(this.checkBox1, "checkBox1");
			this.checkBox1.Name = "checkBox1";
			this.checkBox1.UseVisualStyleBackColor = true;
			this.checkBox1.CheckedChanged += this.checkBox1_CheckedChanged;
			componentResourceManager.ApplyResources(this.label15, "label15");
			this.label15.Name = "label15";
			componentResourceManager.ApplyResources(this.maskedTextBox1, "maskedTextBox1");
			this.maskedTextBox1.Name = "maskedTextBox1";
			this.maskedTextBox1.TextChanged += this.maskedTextBox1_TextChanged;
			componentResourceManager.ApplyResources(this.label29, "label29");
			this.label29.Name = "label29";
			componentResourceManager.ApplyResources(this.comboBox1, "comboBox1");
			this.comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
			this.comboBox1.FormattingEnabled = true;
			this.comboBox1.Name = "comboBox1";
			this.comboBox1.SelectedIndexChanged += this.comboBox1_SelectedIndexChanged;
			componentResourceManager.ApplyResources(this.comboBox2, "comboBox2");
			this.comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
			this.comboBox2.FormattingEnabled = true;
			this.comboBox2.Items.AddRange(new object[]
			{
				componentResourceManager.GetString("comboBox2.Items"),
				componentResourceManager.GetString("comboBox2.Items1"),
				componentResourceManager.GetString("comboBox2.Items2"),
				componentResourceManager.GetString("comboBox2.Items3"),
                componentResourceManager.GetString("comboBox2.Items4")
            });
			this.comboBox2.Name = "comboBox2";
			this.comboBox2.SelectedIndexChanged += this.comboBox2_SelectedIndexChanged;
			componentResourceManager.ApplyResources(this.comboBox3, "comboBox3");
			this.comboBox3.DropDownStyle = ComboBoxStyle.DropDownList;
			this.comboBox3.FormattingEnabled = true;
			this.comboBox3.Items.AddRange(new object[]
			{
				componentResourceManager.GetString("comboBox3.Items"),
				componentResourceManager.GetString("comboBox3.Items1"),
                componentResourceManager.GetString("comboBox3.Items2")
            });
			this.comboBox3.Name = "comboBox3";
			this.comboBox3.SelectedIndexChanged += this.comboBox3_SelectedIndexChanged;
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.trackBar1, "trackBar1");
			this.trackBar1.LargeChange = 10;
			this.trackBar1.Maximum = 100;
			this.trackBar1.Name = "trackBar1";
			this.trackBar1.TickFrequency = 10;
			this.trackBar1.Scroll += this.trackBar1_Scroll;
			componentResourceManager.ApplyResources(this.label11, "label11");
			this.label11.Name = "label11";
			componentResourceManager.ApplyResources(this.label30, "label30");
			this.label30.Name = "label30";
			componentResourceManager.ApplyResources(this.maskedTextBox2, "maskedTextBox2");
			this.maskedTextBox2.Name = "maskedTextBox2";
			this.maskedTextBox2.TextChanged += this.maskedTextBox2_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
			this.doubleTextBox4.Name = "doubleTextBox4";
			this.doubleTextBox4.TextChanged += this.doubleTextBox4_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
			this.doubleTextBox3.Name = "doubleTextBox3";
			this.doubleTextBox3.TextChanged += this.doubleTextBox3_TextChanged;
			componentResourceManager.ApplyResources(this.textBox7, "textBox7");
			this.textBox7.BackColor = Color.LightGray;
			this.textBox7.BorderStyle = BorderStyle.None;
			this.textBox7.Name = "textBox7";
			this.textBox7.ReadOnly = true;
			this.textBox7.TabStop = false;
			componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			componentResourceManager.ApplyResources(this.numericUpDown2, "numericUpDown2");
			NumericUpDown numericUpDown = this.numericUpDown2;
			int[] array = new int[4];
			array[0] = 63;
			numericUpDown.Maximum = new decimal(array);
			this.numericUpDown2.Name = "numericUpDown2";
			this.numericUpDown2.ValueChanged += this.numericUpDown2_ValueChanged;
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.textBox11, "textBox11");
			this.textBox11.BackColor = Color.LightGray;
			this.textBox11.BorderStyle = BorderStyle.None;
			this.textBox11.Name = "textBox11";
			this.textBox11.ReadOnly = true;
			this.textBox11.TabStop = false;
			componentResourceManager.ApplyResources(this.numericUpDown1, "numericUpDown1");
			NumericUpDown numericUpDown2 = this.numericUpDown1;
			int[] array2 = new int[4];
			array2[0] = 256;
			numericUpDown2.Maximum = new decimal(array2);
			NumericUpDown numericUpDown3 = this.numericUpDown1;
			int[] array3 = new int[4];
			array3[0] = 8;
			numericUpDown3.Minimum = new decimal(array3);
			this.numericUpDown1.Name = "numericUpDown1";
			NumericUpDown numericUpDown4 = this.numericUpDown1;
			int[] array4 = new int[4];
			array4[0] = 8;
			numericUpDown4.Value = new decimal(array4);
			this.numericUpDown1.ValueChanged += this.numericUpDown1_ValueChanged;
			componentResourceManager.ApplyResources(this.label17, "label17");
			this.label17.Name = "label17";
			componentResourceManager.ApplyResources(this.standardPulseView1, "standardPulseView1");
			this.standardPulseView1.BackColor = Color.Black;
			this.standardPulseView1.Name = "standardPulseView1";
			this.standardPulseView1.PeakIndex = 0;
			this.standardPulseView1.PulseShape = null;
			this.standardPulseView1.PulseShapeSize = 0;
			componentResourceManager.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += this.button1_Click;
			componentResourceManager.ApplyResources(this.textBox4, "textBox4");
			this.textBox4.Name = "textBox4";
			this.textBox4.ReadOnly = true;
			this.textBox4.TabStop = false;
			componentResourceManager.ApplyResources(this.button2, "button2");
			this.button2.Name = "button2";
			this.button2.UseVisualStyleBackColor = true;
			this.button2.Click += this.button2_Click;
			componentResourceManager.ApplyResources(this.label10, "label10");
			this.label10.Name = "label10";
			componentResourceManager.ApplyResources(this.trackBar2, "trackBar2");
			this.trackBar2.LargeChange = 10;
			this.trackBar2.Maximum = 100;
			this.trackBar2.Name = "trackBar2";
			this.trackBar2.TickFrequency = 10;
			this.trackBar2.Scroll += this.trackBar2_Scroll;
			componentResourceManager.ApplyResources(this.label14, "label14");
			this.label14.Name = "label14";
			componentResourceManager.ApplyResources(this.label12, "label12");
			this.label12.Name = "label12";
			componentResourceManager.ApplyResources(this.label13, "label13");
			this.label13.Name = "label13";
			componentResourceManager.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
			this.doubleTextBox2.Name = "doubleTextBox2";
			this.doubleTextBox2.TextChanged += this.doubleTextBox2_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
			this.doubleTextBox1.Name = "doubleTextBox1";
			this.doubleTextBox1.TextChanged += this.doubleTextBox1_TextChanged;
			componentResourceManager.ApplyResources(this.panel1, "panel1");
			this.panel1.BackColor = Color.LightGray;
			this.panel1.BorderStyle = BorderStyle.FixedSingle;
			this.panel1.Controls.Add(this.doubleTextBox4);
			this.panel1.Controls.Add(this.label6);
			this.panel1.Controls.Add(this.doubleTextBox3);
			this.panel1.Controls.Add(this.label10);
			this.panel1.Controls.Add(this.textBox7);
			this.panel1.Controls.Add(this.button2);
			this.panel1.Controls.Add(this.textBox4);
			this.panel1.Controls.Add(this.label9);
			this.panel1.Controls.Add(this.button1);
			this.panel1.Controls.Add(this.label8);
			this.panel1.Controls.Add(this.standardPulseView1);
			this.panel1.Controls.Add(this.numericUpDown2);
			this.panel1.Controls.Add(this.label17);
			this.panel1.Controls.Add(this.label7);
			this.panel1.Controls.Add(this.numericUpDown1);
			this.panel1.Controls.Add(this.textBox11);
			this.panel1.Name = "panel1";
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = AutoScaleMode.Font;
			base.Controls.Add(this.label5);
			base.Controls.Add(this.panel1);
			base.Controls.Add(this.label37);
			base.Controls.Add(this.checkBox2);
			base.Controls.Add(this.checkBox1);
			base.Controls.Add(this.label15);
			base.Controls.Add(this.maskedTextBox1);
			base.Controls.Add(this.label29);
			base.Controls.Add(this.comboBox1);
			base.Controls.Add(this.comboBox2);
			base.Controls.Add(this.comboBox3);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.trackBar1);
			base.Controls.Add(this.label11);
			base.Controls.Add(this.label30);
			base.Controls.Add(this.maskedTextBox2);
			base.Controls.Add(this.trackBar2);
			base.Controls.Add(this.label14);
			base.Controls.Add(this.label12);
			base.Controls.Add(this.label13);
			base.Controls.Add(this.doubleTextBox2);
			base.Controls.Add(this.doubleTextBox1);
			base.Name = "AudioInputDeviceForm";
			base.Controls.SetChildIndex(this.doubleTextBox1, 0);
			base.Controls.SetChildIndex(this.doubleTextBox2, 0);
			base.Controls.SetChildIndex(this.label13, 0);
			base.Controls.SetChildIndex(this.label12, 0);
			base.Controls.SetChildIndex(this.label14, 0);
			base.Controls.SetChildIndex(this.trackBar2, 0);
			base.Controls.SetChildIndex(this.maskedTextBox2, 0);
			base.Controls.SetChildIndex(this.label30, 0);
			base.Controls.SetChildIndex(this.label11, 0);
			base.Controls.SetChildIndex(this.trackBar1, 0);
			base.Controls.SetChildIndex(this.label3, 0);
			base.Controls.SetChildIndex(this.label2, 0);
			base.Controls.SetChildIndex(this.label1, 0);
			base.Controls.SetChildIndex(this.comboBox3, 0);
			base.Controls.SetChildIndex(this.comboBox2, 0);
			base.Controls.SetChildIndex(this.comboBox1, 0);
			base.Controls.SetChildIndex(this.label29, 0);
			base.Controls.SetChildIndex(this.maskedTextBox1, 0);
			base.Controls.SetChildIndex(this.label15, 0);
			base.Controls.SetChildIndex(this.checkBox1, 0);
			base.Controls.SetChildIndex(this.checkBox2, 0);
			base.Controls.SetChildIndex(this.label37, 0);
			base.Controls.SetChildIndex(this.panel1, 0);
			base.Controls.SetChildIndex(this.label5, 0);
			((ISupportInitialize)this.trackBar1).EndInit();
			((ISupportInitialize)this.numericUpDown2).EndInit();
			((ISupportInitialize)this.numericUpDown1).EndInit();
			((ISupportInitialize)this.trackBar2).EndInit();
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			base.ResumeLayout(false);
			base.PerformLayout();
		}
		
		#endregion

		// Token: 0x0400096A RID: 2410
		IContainer components;

		// Token: 0x0400096B RID: 2411
		Label label37;

		// Token: 0x0400096C RID: 2412
		CheckBox checkBox2;

		// Token: 0x0400096D RID: 2413
		CheckBox checkBox1;

		// Token: 0x0400096E RID: 2414
		Label label15;

		// Token: 0x0400096F RID: 2415
		MaskedTextBox maskedTextBox1;

		// Token: 0x04000970 RID: 2416
		Label label29;

		// Token: 0x04000971 RID: 2417
		ComboBox comboBox1;

		// Token: 0x04000972 RID: 2418
		ComboBox comboBox2;

		// Token: 0x04000973 RID: 2419
		ComboBox comboBox3;

		// Token: 0x04000974 RID: 2420
		Label label1;

		// Token: 0x04000975 RID: 2421
		Label label2;

		// Token: 0x04000976 RID: 2422
		Label label3;

		// Token: 0x04000977 RID: 2423
		TrackBar trackBar1;

		// Token: 0x04000978 RID: 2424
		Label label11;

		// Token: 0x04000979 RID: 2425
		Label label30;

		// Token: 0x0400097A RID: 2426
		MaskedTextBox maskedTextBox2;

		// Token: 0x0400097B RID: 2427
		DoubleTextBox doubleTextBox4;

		// Token: 0x0400097C RID: 2428
		DoubleTextBox doubleTextBox3;

		// Token: 0x0400097D RID: 2429
		TextBox textBox7;

		// Token: 0x0400097E RID: 2430
		Label label6;

		// Token: 0x0400097F RID: 2431
		Label label9;

		// Token: 0x04000980 RID: 2432
		Label label8;

		// Token: 0x04000981 RID: 2433
		NumericUpDown numericUpDown2;

		// Token: 0x04000982 RID: 2434
		Label label7;

		// Token: 0x04000983 RID: 2435
		TextBox textBox11;

		// Token: 0x04000984 RID: 2436
		NumericUpDown numericUpDown1;

		// Token: 0x04000985 RID: 2437
		Label label17;

		// Token: 0x04000986 RID: 2438
		StandardPulseView standardPulseView1;

		// Token: 0x04000987 RID: 2439
		Button button1;

		// Token: 0x04000988 RID: 2440
		TextBox textBox4;

		// Token: 0x04000989 RID: 2441
		Button button2;

		// Token: 0x0400098A RID: 2442
		Label label10;

		// Token: 0x0400098B RID: 2443
		TrackBar trackBar2;

		// Token: 0x0400098C RID: 2444
		Label label14;

		// Token: 0x0400098D RID: 2445
		Label label12;

		// Token: 0x0400098E RID: 2446
		Label label13;

		// Token: 0x0400098F RID: 2447
		DoubleTextBox doubleTextBox2;

		// Token: 0x04000990 RID: 2448
		DoubleTextBox doubleTextBox1;

		// Token: 0x04000991 RID: 2449
		Panel panel1;

		// Token: 0x04000992 RID: 2450
		Label label5;

		// Token: 0x04000993 RID: 2451
		ReadOnlyCollection<WaveInDeviceCaps> waveDeviceList;

		// Token: 0x04000994 RID: 2452
		StandardPulseRecorder pulseRecorder = new StandardPulseRecorder();

		// Token: 0x04000995 RID: 2453
		AudioVolumeController audioVolumeController;

		// Token: 0x04000996 RID: 2454
		float previousVolume;

		// Token: 0x04000997 RID: 2455
		Timer timer;

		// Token: 0x04000998 RID: 2456
		AudioInputDeviceConfig tempConfig;
	}
}
