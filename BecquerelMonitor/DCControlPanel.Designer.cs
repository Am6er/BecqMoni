namespace BecquerelMonitor
{
	// Token: 0x02000030 RID: 48
	public partial class DCControlPanel : global::BecquerelMonitor.ToolWindow
	{
		// Token: 0x060002A9 RID: 681 RVA: 0x0000C470 File Offset: 0x0000A670
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060002AA RID: 682 RVA: 0x0000C498 File Offset: 0x0000A698
		void InitializeComponent()
		{
			this.components = new global::System.ComponentModel.Container();
			global::System.ComponentModel.ComponentResourceManager componentResourceManager = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DCControlPanel));

            this.Font = new global::System.Drawing.Font("Microsoft Sans Serif", 8);
            
            this.imageList1 = new global::System.Windows.Forms.ImageList(this.components);
			this.toolTip1 = new global::System.Windows.Forms.ToolTip(this.components);
			this.button9 = new global::System.Windows.Forms.Button();
			this.button8 = new global::System.Windows.Forms.Button();
			this.button6 = new global::System.Windows.Forms.Button();
			this.button5 = new global::System.Windows.Forms.Button();
			this.button1 = new global::System.Windows.Forms.Button();
			this.button2 = new global::System.Windows.Forms.Button();
			this.button7 = new global::System.Windows.Forms.Button();
			this.comboBox2 = new global::System.Windows.Forms.ComboBox();
			this.comboBox1 = new global::System.Windows.Forms.ComboBox();
			this.integerTextBox1 = new global::BecquerelMonitor.IntegerTextBox();
			this.checkBox1 = new global::System.Windows.Forms.CheckBox();
			this.groupBox1 = new global::System.Windows.Forms.GroupBox();
			this.percentageProgressBar1 = new global::BecquerelMonitor.PercentageProgressBar();
			this.label7 = new global::System.Windows.Forms.Label();
			this.label6 = new global::System.Windows.Forms.Label();
			this.textBox3 = new global::System.Windows.Forms.TextBox();
			this.textBox6 = new global::System.Windows.Forms.TextBox();
			this.textBox5 = new global::System.Windows.Forms.TextBox();
			this.label5 = new global::System.Windows.Forms.Label();
			this.label4 = new global::System.Windows.Forms.Label();
			this.label2 = new global::System.Windows.Forms.Label();
			this.textBox4 = new global::System.Windows.Forms.TextBox();
			this.button4 = new global::System.Windows.Forms.Button();
			this.label10 = new global::System.Windows.Forms.Label();
			this.button3 = new global::System.Windows.Forms.Button();
			this.label8 = new global::System.Windows.Forms.Label();
			this.textBox1 = new global::System.Windows.Forms.TextBox();
			this.button10 = new global::System.Windows.Forms.Button();
			this.textBox10 = new global::System.Windows.Forms.TextBox();
			this.label9 = new global::System.Windows.Forms.Label();
			this.label3 = new global::System.Windows.Forms.Label();
			this.groupBox1.SuspendLayout();
			base.SuspendLayout();
			this.imageList1.ImageStream = (global::System.Windows.Forms.ImageListStreamer)componentResourceManager.GetObject("imageList1.ImageStream");
			this.imageList1.TransparentColor = global::System.Drawing.Color.Transparent;
			this.imageList1.Images.SetKeyName(0, "start.ico");
			this.imageList1.Images.SetKeyName(1, "stop.ico");
			this.imageList1.Images.SetKeyName(2, "clear.ico");
			this.imageList1.Images.SetKeyName(3, "reload.ico");
			componentResourceManager.ApplyResources(this.button9, "button9");
			this.button9.ImageList = this.imageList1;
			this.button9.Name = "button9";
			this.toolTip1.SetToolTip(this.button9, componentResourceManager.GetString("button9.ToolTip"));
			this.button9.UseVisualStyleBackColor = true;
			this.button9.Click += new global::System.EventHandler(this.button9_Click);
			componentResourceManager.ApplyResources(this.button8, "button8");
			this.button8.ImageList = this.imageList1;
			this.button8.Name = "button8";
			this.toolTip1.SetToolTip(this.button8, componentResourceManager.GetString("button8.ToolTip"));
			this.button8.UseVisualStyleBackColor = true;
			this.button8.Click += new global::System.EventHandler(this.button8_Click);
			componentResourceManager.ApplyResources(this.button6, "button6");
			this.button6.Name = "button6";
			this.toolTip1.SetToolTip(this.button6, componentResourceManager.GetString("button6.ToolTip"));
			this.button6.UseVisualStyleBackColor = true;
			this.button6.Click += new global::System.EventHandler(this.button6_Click);
			componentResourceManager.ApplyResources(this.button5, "button5");
			this.button5.ImageList = this.imageList1;
			this.button5.Name = "button5";
			this.toolTip1.SetToolTip(this.button5, componentResourceManager.GetString("button5.ToolTip"));
			this.button5.UseVisualStyleBackColor = true;
			this.button5.Click += new global::System.EventHandler(this.button5_Click);
			componentResourceManager.ApplyResources(this.button1, "button1");
			this.button1.ImageList = this.imageList1;
			this.button1.Name = "button1";
			this.toolTip1.SetToolTip(this.button1, componentResourceManager.GetString("button1.ToolTip"));
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new global::System.EventHandler(this.button1_Click);
			componentResourceManager.ApplyResources(this.button2, "button2");
			this.button2.ImageList = this.imageList1;
			this.button2.Name = "button2";
			this.toolTip1.SetToolTip(this.button2, componentResourceManager.GetString("button2.ToolTip"));
			this.button2.UseVisualStyleBackColor = true;
			this.button2.Click += new global::System.EventHandler(this.button2_Click);
			componentResourceManager.ApplyResources(this.button7, "button7");
			this.button7.ImageList = this.imageList1;
			this.button7.Name = "button7";
			this.toolTip1.SetToolTip(this.button7, componentResourceManager.GetString("button7.ToolTip"));
			this.button7.UseVisualStyleBackColor = true;
			this.button7.Click += new global::System.EventHandler(this.button7_Click);
			componentResourceManager.ApplyResources(this.comboBox2, "comboBox2");
			this.comboBox2.DropDownStyle = global::System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBox2.FormattingEnabled = true;
			this.comboBox2.Name = "comboBox2";
			this.toolTip1.SetToolTip(this.comboBox2, componentResourceManager.GetString("comboBox2.ToolTip"));
			this.comboBox2.SelectedIndexChanged += new global::System.EventHandler(this.comboBox2_SelectedIndexChanged);
			componentResourceManager.ApplyResources(this.comboBox1, "comboBox1");
			this.comboBox1.DropDownStyle = global::System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.comboBox1.FormattingEnabled = true;
			this.comboBox1.Name = "comboBox1";
			this.toolTip1.SetToolTip(this.comboBox1, componentResourceManager.GetString("comboBox1.ToolTip"));
			this.comboBox1.SelectedIndexChanged += new global::System.EventHandler(this.comboBox1_SelectedIndexChanged);
			componentResourceManager.ApplyResources(this.integerTextBox1, "integerTextBox1");
			this.integerTextBox1.Name = "integerTextBox1";
			this.toolTip1.SetToolTip(this.integerTextBox1, componentResourceManager.GetString("integerTextBox1.ToolTip"));
			this.integerTextBox1.KeyDown += new global::System.Windows.Forms.KeyEventHandler(this.textBox7_KeyDown);
			this.integerTextBox1.Validated += new global::System.EventHandler(this.textBox7_Validated);
			componentResourceManager.ApplyResources(this.checkBox1, "checkBox1");
			this.checkBox1.Name = "checkBox1";
			this.checkBox1.UseVisualStyleBackColor = true;
			this.checkBox1.CheckedChanged += new global::System.EventHandler(this.checkBox1_CheckedChanged);
			componentResourceManager.ApplyResources(this.groupBox1, "groupBox1");
			this.groupBox1.Controls.Add(this.integerTextBox1);
			this.groupBox1.Controls.Add(this.percentageProgressBar1);
			this.groupBox1.Controls.Add(this.button1);
			this.groupBox1.Controls.Add(this.button2);
			this.groupBox1.Controls.Add(this.label7);
			this.groupBox1.Controls.Add(this.label6);
			this.groupBox1.Controls.Add(this.button7);
			this.groupBox1.Controls.Add(this.textBox3);
			this.groupBox1.Controls.Add(this.textBox6);
			this.groupBox1.Controls.Add(this.textBox5);
			this.groupBox1.Controls.Add(this.label5);
			this.groupBox1.Controls.Add(this.label4);
			this.groupBox1.Controls.Add(this.label2);
			this.groupBox1.Controls.Add(this.textBox4);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.TabStop = false;
			componentResourceManager.ApplyResources(this.percentageProgressBar1, "percentageProgressBar1");
			this.percentageProgressBar1.DoubleValue = 0.0;
			this.percentageProgressBar1.ForeColor = global::System.Drawing.Color.Black;
			this.percentageProgressBar1.Name = "percentageProgressBar1";
			this.percentageProgressBar1.OverlayColor = global::System.Drawing.Color.Black;
			this.percentageProgressBar1.PriorText = "28800";
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this.textBox3, "textBox3");
			this.textBox3.Name = "textBox3";
			this.textBox3.ReadOnly = true;
			this.textBox3.TabStop = false;
			componentResourceManager.ApplyResources(this.textBox6, "textBox6");
			this.textBox6.Name = "textBox6";
			this.textBox6.ReadOnly = true;
			this.textBox6.TabStop = false;
			componentResourceManager.ApplyResources(this.textBox5, "textBox5");
			this.textBox5.Name = "textBox5";
			this.textBox5.ReadOnly = true;
			this.textBox5.TabStop = false;
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.textBox4, "textBox4");
			this.textBox4.Name = "textBox4";
			this.textBox4.ReadOnly = true;
			this.textBox4.TabStop = false;
			componentResourceManager.ApplyResources(this.button4, "button4");
			this.button4.Name = "button4";
			this.button4.UseVisualStyleBackColor = true;
			this.button4.Click += new global::System.EventHandler(this.button4_Click);
			componentResourceManager.ApplyResources(this.label10, "label10");
			this.label10.Name = "label10";
			componentResourceManager.ApplyResources(this.button3, "button3");
			this.button3.Name = "button3";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += new global::System.EventHandler(this.button3_Click);
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.ReadOnly = true;
			this.textBox1.TabStop = false;
			componentResourceManager.ApplyResources(this.button10, "button10");
			this.button10.Name = "button10";
			this.button10.UseVisualStyleBackColor = true;
			this.button10.Click += new global::System.EventHandler(this.button10_Click);
			componentResourceManager.ApplyResources(this.textBox10, "textBox10");
			this.textBox10.Name = "textBox10";
			this.textBox10.ReadOnly = true;
			this.textBox10.TabStop = false;
			componentResourceManager.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this, "$this");
			base.Controls.Add(this.button9);
			base.Controls.Add(this.button8);
			base.Controls.Add(this.button6);
			base.Controls.Add(this.checkBox1);
			base.Controls.Add(this.button5);
			base.Controls.Add(this.groupBox1);
			base.Controls.Add(this.button4);
			base.Controls.Add(this.comboBox2);
			base.Controls.Add(this.label10);
			base.Controls.Add(this.button3);
			base.Controls.Add(this.label8);
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.comboBox1);
			base.Controls.Add(this.button10);
			base.Controls.Add(this.textBox10);
			base.Controls.Add(this.label9);
			base.Controls.Add(this.label3);
			base.HideOnClose = true;
			base.Name = "DCControlPanel";
			base.Load += new global::System.EventHandler(this.DCControlPanel_Load);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x040000D2 RID: 210
		global::System.ComponentModel.IContainer components;

		// Token: 0x040000D3 RID: 211
		global::System.Windows.Forms.Button button7;

		// Token: 0x040000D4 RID: 212
		global::System.Windows.Forms.Label label7;

		// Token: 0x040000D5 RID: 213
		global::System.Windows.Forms.Label label6;

		// Token: 0x040000D6 RID: 214
		global::System.Windows.Forms.TextBox textBox6;

		// Token: 0x040000D7 RID: 215
		global::System.Windows.Forms.TextBox textBox5;

		// Token: 0x040000D8 RID: 216
		global::System.Windows.Forms.Label label5;

		// Token: 0x040000D9 RID: 217
		global::System.Windows.Forms.Label label4;

		// Token: 0x040000DA RID: 218
		global::System.Windows.Forms.TextBox textBox4;

		// Token: 0x040000DB RID: 219
		global::System.Windows.Forms.Label label3;

		// Token: 0x040000DC RID: 220
		global::System.Windows.Forms.Label label2;

		// Token: 0x040000DD RID: 221
		global::System.Windows.Forms.TextBox textBox3;

		// Token: 0x040000DE RID: 222
		global::System.Windows.Forms.Button button2;

		// Token: 0x040000DF RID: 223
		global::System.Windows.Forms.Button button1;

		// Token: 0x040000E0 RID: 224
		global::System.Windows.Forms.Label label9;

		// Token: 0x040000E1 RID: 225
		global::System.Windows.Forms.TextBox textBox10;

		// Token: 0x040000E2 RID: 226
		global::System.Windows.Forms.Button button10;

		// Token: 0x040000E3 RID: 227
		global::System.Windows.Forms.ComboBox comboBox1;

		// Token: 0x040000E4 RID: 228
		global::System.Windows.Forms.TextBox textBox1;

		// Token: 0x040000E5 RID: 229
		global::System.Windows.Forms.Label label8;

		// Token: 0x040000E6 RID: 230
		global::System.Windows.Forms.Button button3;

		// Token: 0x040000E7 RID: 231
		global::System.Windows.Forms.Label label10;

		// Token: 0x040000E8 RID: 232
		global::System.Windows.Forms.ComboBox comboBox2;

		// Token: 0x040000E9 RID: 233
		global::System.Windows.Forms.Button button4;

		// Token: 0x040000EA RID: 234
		global::System.Windows.Forms.GroupBox groupBox1;

		// Token: 0x040000EB RID: 235
		global::System.Windows.Forms.ImageList imageList1;

		// Token: 0x040000EC RID: 236
		global::System.Windows.Forms.Button button5;

		// Token: 0x040000ED RID: 237
		global::System.Windows.Forms.ToolTip toolTip1;

		// Token: 0x040000EE RID: 238
		global::BecquerelMonitor.PercentageProgressBar percentageProgressBar1;

		// Token: 0x040000EF RID: 239
		global::BecquerelMonitor.IntegerTextBox integerTextBox1;

		// Token: 0x040000F0 RID: 240
		global::System.Windows.Forms.CheckBox checkBox1;

		// Token: 0x040000F1 RID: 241
		global::System.Windows.Forms.Button button6;

		// Token: 0x040000F2 RID: 242
		global::System.Windows.Forms.Button button8;

		// Token: 0x040000F3 RID: 243
		global::System.Windows.Forms.Button button9;
	}
}
