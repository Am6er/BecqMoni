using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x02000081 RID: 129
	public class ROISimpleDifferenceControl : ROIPrimitiveControl
	{
		// Token: 0x06000664 RID: 1636 RVA: 0x00027234 File Offset: 0x00025434
		public ROISimpleDifferenceControl()
		{
			this.InitializeComponent();
			this.comboBox1.Items.Clear();
			foreach (ROIPrimitiveOperation roiprimitiveOperation in ROIPrimitiveOperation.Operations)
			{
				this.comboBox1.Items.Add(roiprimitiveOperation.Translation);
			}
			this.comboBox1.SelectedIndex = 0;
		}

		// Token: 0x06000665 RID: 1637 RVA: 0x000272C4 File Offset: 0x000254C4
		public override void LoadFormContents(ROIPrimitiveData prim)
		{
			ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)prim;
			this.comboBox1.SelectedIndex = ROIPrimitiveOperation.GetOperationIndex(prim.OperationType);
			this.doubleTextBox3.Text = roisimpleDifferenceData.Coefficient.ToString();
			this.doubleTextBox4.Text = roisimpleDifferenceData.CoefficientError.ToString();
			this.doubleTextBox1.Text = roisimpleDifferenceData.LowerLimit.ToString();
			this.doubleTextBox2.Text = roisimpleDifferenceData.UpperLimit.ToString();
			this.textBox1.Text = roisimpleDifferenceData.Note;
		}

		// Token: 0x06000666 RID: 1638 RVA: 0x00027370 File Offset: 0x00025570
		public override bool SaveFormContents(ROIPrimitiveData prim)
		{
			ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)prim;
			try
			{
				ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
				roisimpleDifferenceData.Operation = roiprimitiveOperation;
				roisimpleDifferenceData.OperationType = roiprimitiveOperation.Name;
				roisimpleDifferenceData.Coefficient = double.Parse(this.doubleTextBox3.Text);
				roisimpleDifferenceData.CoefficientError = double.Parse(this.doubleTextBox4.Text);
				roisimpleDifferenceData.LowerLimit = double.Parse(this.doubleTextBox1.Text);
				roisimpleDifferenceData.UpperLimit = double.Parse(this.doubleTextBox2.Text);
				if (roisimpleDifferenceData.UpperLimit < roisimpleDifferenceData.LowerLimit)
				{
					roisimpleDifferenceData.UpperLimit = roisimpleDifferenceData.LowerLimit;
					this.doubleTextBox2.Text = roisimpleDifferenceData.LowerLimit.ToString();
				}
				this.doubleTextBox2.Text = roisimpleDifferenceData.UpperLimit.ToString();
				prim.Note = this.textBox1.Text;
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		// Token: 0x06000667 RID: 1639 RVA: 0x0002748C File Offset: 0x0002568C
		void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000668 RID: 1640 RVA: 0x00027494 File Offset: 0x00025694
		void doubleTextBox3_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x06000669 RID: 1641 RVA: 0x0002749C File Offset: 0x0002569C
		void doubleTextBox4_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600066A RID: 1642 RVA: 0x000274A4 File Offset: 0x000256A4
		void doubleTextBox1_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x0600066B RID: 1643 RVA: 0x000274A8 File Offset: 0x000256A8
		void doubleTextBox2_TextChanged(object sender, EventArgs e)
		{
		}

		// Token: 0x0600066C RID: 1644 RVA: 0x000274AC File Offset: 0x000256AC
		void textBox1_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600066D RID: 1645 RVA: 0x000274B4 File Offset: 0x000256B4
		void doubleTextBox1_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600066E RID: 1646 RVA: 0x000274BC File Offset: 0x000256BC
		void doubleTextBox2_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x0600066F RID: 1647 RVA: 0x000274C4 File Offset: 0x000256C4
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000670 RID: 1648 RVA: 0x000274EC File Offset: 0x000256EC
		void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(ROISimpleDifferenceControl));
			this.comboBox1 = new ComboBox();
			this.label1 = new Label();
			this.textBox1 = new TextBox();
			this.doubleTextBox1 = new DoubleTextBox();
			this.doubleTextBox2 = new DoubleTextBox();
			this.label2 = new Label();
			this.label3 = new Label();
			this.label4 = new Label();
			this.label5 = new Label();
			this.label7 = new Label();
			this.label8 = new Label();
			this.doubleTextBox3 = new DoubleTextBox();
			this.doubleTextBox4 = new DoubleTextBox();
			this.label6 = new Label();
			base.SuspendLayout();
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
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.textBox1, "textBox1");
			this.textBox1.Name = "textBox1";
			this.textBox1.TextChanged += this.textBox1_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox1, "doubleTextBox1");
			this.doubleTextBox1.Name = "doubleTextBox1";
			this.doubleTextBox1.TextChanged += this.doubleTextBox1_TextChanged;
			this.doubleTextBox1.Validated += this.doubleTextBox1_Validated;
			componentResourceManager.ApplyResources(this.doubleTextBox2, "doubleTextBox2");
			this.doubleTextBox2.Name = "doubleTextBox2";
			this.doubleTextBox2.TextChanged += this.doubleTextBox2_TextChanged;
			this.doubleTextBox2.Validated += this.doubleTextBox2_Validated;
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this.label5, "label5");
			this.label5.Name = "label5";
			componentResourceManager.ApplyResources(this.label7, "label7");
			this.label7.Name = "label7";
			componentResourceManager.ApplyResources(this.label8, "label8");
			this.label8.Name = "label8";
			componentResourceManager.ApplyResources(this.doubleTextBox3, "doubleTextBox3");
			this.doubleTextBox3.Name = "doubleTextBox3";
			this.doubleTextBox3.TextChanged += this.doubleTextBox3_TextChanged;
			componentResourceManager.ApplyResources(this.doubleTextBox4, "doubleTextBox4");
			this.doubleTextBox4.Name = "doubleTextBox4";
			this.doubleTextBox4.TextChanged += this.doubleTextBox4_TextChanged;
			componentResourceManager.ApplyResources(this.label6, "label6");
			this.label6.Name = "label6";
			componentResourceManager.ApplyResources(this, "$this");
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
			base.Name = "ROISimpleDifferenceControl";
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000352 RID: 850
		IContainer components;

		// Token: 0x04000353 RID: 851
		ComboBox comboBox1;

		// Token: 0x04000354 RID: 852
		Label label1;

		// Token: 0x04000355 RID: 853
		TextBox textBox1;

		// Token: 0x04000356 RID: 854
		DoubleTextBox doubleTextBox1;

		// Token: 0x04000357 RID: 855
		DoubleTextBox doubleTextBox2;

		// Token: 0x04000358 RID: 856
		Label label2;

		// Token: 0x04000359 RID: 857
		Label label3;

		// Token: 0x0400035A RID: 858
		Label label4;

		// Token: 0x0400035B RID: 859
		Label label5;

		// Token: 0x0400035C RID: 860
		Label label7;

		// Token: 0x0400035D RID: 861
		Label label8;

		// Token: 0x0400035E RID: 862
		DoubleTextBox doubleTextBox3;

		// Token: 0x0400035F RID: 863
		DoubleTextBox doubleTextBox4;

		// Token: 0x04000360 RID: 864
		Label label6;
	}
}
