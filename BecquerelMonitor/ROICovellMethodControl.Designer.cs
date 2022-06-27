using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x02000064 RID: 100
	public class ROICovellMethodControl : ROIPrimitiveControl
	{
		// Token: 0x060004F9 RID: 1273 RVA: 0x0001D0B8 File Offset: 0x0001B2B8
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060004FA RID: 1274 RVA: 0x0001D0E0 File Offset: 0x0001B2E0
		void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(ROICovellMethodControl));
			this.label6 = new Label();
			this.doubleTextBox4 = new DoubleTextBox();
			this.doubleTextBox3 = new DoubleTextBox();
			this.label8 = new Label();
			this.label7 = new Label();
			this.label5 = new Label();
			this.label4 = new Label();
			this.label3 = new Label();
			this.label2 = new Label();
			this.doubleTextBox2 = new DoubleTextBox();
			this.doubleTextBox1 = new DoubleTextBox();
			this.textBox1 = new TextBox();
			this.label1 = new Label();
			this.comboBox1 = new ComboBox();
			this.label9 = new Label();
			this.label10 = new Label();
			this.label11 = new Label();
			this.label12 = new Label();
			this.label13 = new Label();
			this.label14 = new Label();
			this.label15 = new Label();
			this.label16 = new Label();
			this.doubleTextBox5 = new DoubleTextBox();
			this.doubleTextBox6 = new DoubleTextBox();
			this.doubleTextBox7 = new DoubleTextBox();
			this.doubleTextBox8 = new DoubleTextBox();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
			this.doubleTextBox4.Name = "doubleTextBox4";
			this.doubleTextBox4.TextChanged += this.doubleTextBox4_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
			this.doubleTextBox3.Name = "doubleTextBox3";
			this.doubleTextBox3.TextChanged += this.doubleTextBox3_TextChanged;
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
			this.doubleTextBox2.Name = "doubleTextBox2";
			this.doubleTextBox2.TextChanged += this.doubleTextBox2_TextChanged;
			this.doubleTextBox2.Validated += this.doubleTextBox2_Validated;
			componentResourceManager.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
			this.doubleTextBox1.Name = "doubleTextBox1";
			this.doubleTextBox1.TextChanged += this.doubleTextBox1_TextChanged;
			this.doubleTextBox1.Validated += this.doubleTextBox1_Validated;
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.TextChanged += this.textBox1_TextChanged;
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.comboBox1, "comboBox1");
			this.comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
			this.comboBox1.FormattingEnabled = true;
			this.comboBox1.Items.AddRange(new object[]
			{
				componentResourceManager.GetString("comboBox1.Items"),
				componentResourceManager.GetString("comboBox1.Items1")
			});
			this.comboBox1.Name = "comboBox1";
			this.comboBox1.SelectedIndexChanged += this.comboBox1_SelectedIndexChanged;
			componentResourceManager.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			componentResourceManager.ApplyResources(this.label10, "label10");
			this.label10.Name = "label10";
			componentResourceManager.ApplyResources(this.label11, "label11");
			this.label11.Name = "label11";
			componentResourceManager.ApplyResources(this.label12, "label12");
			this.label12.Name = "label12";
			componentResourceManager.ApplyResources(this.label13, "label13");
			this.label13.Name = "label13";
			componentResourceManager.ApplyResources(this.label14, "label14");
			this.label14.Name = "label14";
			componentResourceManager.ApplyResources(this.label15, "label15");
			this.label15.Name = "label15";
			componentResourceManager.ApplyResources(this.label16, "label16");
			this.label16.Name = "label16";
			componentResourceManager.ApplyResources(this.doubleTextBox5, "doubleTextBox5");
			this.doubleTextBox5.Name = "doubleTextBox5";
			this.doubleTextBox5.TextChanged += this.doubleTextBox5_TextChanged;
			this.doubleTextBox5.Validated += this.doubleTextBox5_Validated;
			componentResourceManager.ApplyResources(this.doubleTextBox6, "doubleTextBox6");
			this.doubleTextBox6.Name = "doubleTextBox6";
			this.doubleTextBox6.TextChanged += this.doubleTextBox6_TextChanged;
			this.doubleTextBox6.Validated += this.doubleTextBox6_Validated;
			componentResourceManager.ApplyResources(this.doubleTextBox7, "doubleTextBox7");
			this.doubleTextBox7.Name = "doubleTextBox7";
			this.doubleTextBox7.TextChanged += this.doubleTextBox7_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox8, "doubleTextBox8");
			this.doubleTextBox8.Name = "doubleTextBox8";
			this.doubleTextBox8.TextChanged += this.doubleTextBox8_TextChanged;
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = AutoScaleMode.Font;
			base.Controls.Add(this.doubleTextBox8);
			base.Controls.Add(this.doubleTextBox7);
			base.Controls.Add(this.doubleTextBox6);
			base.Controls.Add(this.doubleTextBox5);
			base.Controls.Add(this.label16);
			base.Controls.Add(this.label15);
			base.Controls.Add(this.label14);
			base.Controls.Add(this.label13);
			base.Controls.Add(this.label12);
			base.Controls.Add(this.label11);
			base.Controls.Add(this.label10);
			base.Controls.Add(this.label9);
			base.Controls.Add(this.label6);
			base.Controls.Add(this.doubleTextBox4);
			base.Controls.Add(this.doubleTextBox3);
			base.Controls.Add(this.label8);
			base.Controls.Add(this.label7);
			base.Controls.Add(this.label5);
			base.Controls.Add(this.label4);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.doubleTextBox2);
			base.Controls.Add(this.doubleTextBox1);
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.comboBox1);
			base.Name = "ROICovellMethodControl";
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x060004FB RID: 1275 RVA: 0x0001D8F4 File Offset: 0x0001BAF4
		public ROICovellMethodControl()
		{
			this.InitializeComponent();
			this.comboBox1.Items.Clear();
			foreach (ROIPrimitiveOperation roiprimitiveOperation in ROIPrimitiveOperation.Operations)
			{
				this.comboBox1.Items.Add(roiprimitiveOperation.Translation);
			}
			this.comboBox1.SelectedIndex = 0;
		}

		// Token: 0x060004FC RID: 1276 RVA: 0x0001D984 File Offset: 0x0001BB84
		public override void LoadFormContents(ROIPrimitiveData prim)
		{
			ROICovellMethodData roicovellMethodData = (ROICovellMethodData)prim;
			this.comboBox1.SelectedIndex = ROIPrimitiveOperation.GetOperationIndex(roicovellMethodData.OperationType);
			this.doubleTextBox3.Text = roicovellMethodData.Coefficient.ToString();
			this.doubleTextBox4.Text = roicovellMethodData.CoefficientError.ToString();
			this.doubleTextBox1.Text = roicovellMethodData.LowerLimit.ToString();
			this.doubleTextBox2.Text = roicovellMethodData.UpperLimit.ToString();
			this.doubleTextBox5.Text = roicovellMethodData.LeftRegionCenter.ToString();
			this.doubleTextBox6.Text = roicovellMethodData.RightRegionCenter.ToString();
			this.doubleTextBox7.Text = roicovellMethodData.LeftRegionWidth.ToString();
			this.doubleTextBox8.Text = roicovellMethodData.RightRegionWidth.ToString();
			this.textBox1.Text = roicovellMethodData.Note;
		}

		// Token: 0x060004FD RID: 1277 RVA: 0x0001DA98 File Offset: 0x0001BC98
		public override bool SaveFormContents(ROIPrimitiveData prim)
		{
			ROICovellMethodData roicovellMethodData = (ROICovellMethodData)prim;
			try
			{
				ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
				roicovellMethodData.Operation = roiprimitiveOperation;
				roicovellMethodData.OperationType = roiprimitiveOperation.Name;
				roicovellMethodData.Coefficient = double.Parse(this.doubleTextBox3.Text);
				roicovellMethodData.CoefficientError = double.Parse(this.doubleTextBox4.Text);
				roicovellMethodData.LowerLimit = double.Parse(this.doubleTextBox1.Text);
				roicovellMethodData.UpperLimit = double.Parse(this.doubleTextBox2.Text);
				if (roicovellMethodData.UpperLimit < roicovellMethodData.LowerLimit)
				{
					roicovellMethodData.UpperLimit = roicovellMethodData.LowerLimit;
					this.doubleTextBox2.Text = roicovellMethodData.LowerLimit.ToString();
				}
				roicovellMethodData.LeftRegionCenter = double.Parse(this.doubleTextBox5.Text);
				roicovellMethodData.RightRegionCenter = double.Parse(this.doubleTextBox6.Text);
				roicovellMethodData.LeftRegionWidth = double.Parse(this.doubleTextBox7.Text);
				roicovellMethodData.RightRegionWidth = double.Parse(this.doubleTextBox8.Text);
				roicovellMethodData.Note = this.textBox1.Text;
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		// Token: 0x060004FE RID: 1278 RVA: 0x0001DC00 File Offset: 0x0001BE00
		void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060004FF RID: 1279 RVA: 0x0001DC08 File Offset: 0x0001BE08
		void doubleTextBox3_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000500 RID: 1280 RVA: 0x0001DC10 File Offset: 0x0001BE10
		void doubleTextBox4_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000501 RID: 1281 RVA: 0x0001DC18 File Offset: 0x0001BE18
		void doubleTextBox1_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x06000502 RID: 1282 RVA: 0x0001DC1C File Offset: 0x0001BE1C
		void doubleTextBox2_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x06000503 RID: 1283 RVA: 0x0001DC20 File Offset: 0x0001BE20
		void integerTextBox1_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000504 RID: 1284 RVA: 0x0001DC28 File Offset: 0x0001BE28
		void textBox1_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000505 RID: 1285 RVA: 0x0001DC30 File Offset: 0x0001BE30
		void doubleTextBox1_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000506 RID: 1286 RVA: 0x0001DC38 File Offset: 0x0001BE38
		void doubleTextBox2_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000507 RID: 1287 RVA: 0x0001DC40 File Offset: 0x0001BE40
		void doubleTextBox5_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x06000508 RID: 1288 RVA: 0x0001DC44 File Offset: 0x0001BE44
		void doubleTextBox6_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x06000509 RID: 1289 RVA: 0x0001DC48 File Offset: 0x0001BE48
		void doubleTextBox7_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600050A RID: 1290 RVA: 0x0001DC50 File Offset: 0x0001BE50
		void doubleTextBox8_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600050B RID: 1291 RVA: 0x0001DC58 File Offset: 0x0001BE58
		void doubleTextBox5_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600050C RID: 1292 RVA: 0x0001DC60 File Offset: 0x0001BE60
		void doubleTextBox6_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x04000241 RID: 577
		IContainer components;

		// Token: 0x04000242 RID: 578
		Label label6;

		// Token: 0x04000243 RID: 579
		DoubleTextBox doubleTextBox4;

		// Token: 0x04000244 RID: 580
		DoubleTextBox doubleTextBox3;

		// Token: 0x04000245 RID: 581
		Label label8;

		// Token: 0x04000246 RID: 582
		Label label7;

		// Token: 0x04000247 RID: 583
		Label label5;

		// Token: 0x04000248 RID: 584
		Label label4;

		// Token: 0x04000249 RID: 585
		Label label3;

		// Token: 0x0400024A RID: 586
		Label label2;

		// Token: 0x0400024B RID: 587
		DoubleTextBox doubleTextBox2;

		// Token: 0x0400024C RID: 588
		DoubleTextBox doubleTextBox1;

		// Token: 0x0400024D RID: 589
		TextBox textBox1;

		// Token: 0x0400024E RID: 590
		Label label1;

		// Token: 0x0400024F RID: 591
		ComboBox comboBox1;

		// Token: 0x04000250 RID: 592
		Label label9;

		// Token: 0x04000251 RID: 593
		Label label10;

		// Token: 0x04000252 RID: 594
		Label label11;

		// Token: 0x04000253 RID: 595
		Label label12;

		// Token: 0x04000254 RID: 596
		Label label13;

		// Token: 0x04000255 RID: 597
		Label label14;

		// Token: 0x04000256 RID: 598
		Label label15;

		// Token: 0x04000257 RID: 599
		Label label16;

		// Token: 0x04000258 RID: 600
		DoubleTextBox doubleTextBox5;

		// Token: 0x04000259 RID: 601
		DoubleTextBox doubleTextBox6;

		// Token: 0x0400025A RID: 602
		DoubleTextBox doubleTextBox7;

		// Token: 0x0400025B RID: 603
		DoubleTextBox doubleTextBox8;
	}
}
