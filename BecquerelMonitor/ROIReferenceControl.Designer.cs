using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x0200006D RID: 109
	public class ROIReferenceControl : ROIPrimitiveControl
	{
		// Token: 0x060005A4 RID: 1444 RVA: 0x00023ACC File Offset: 0x00021CCC
		public ROIReferenceControl()
		{
			this.InitializeComponent();
			this.comboBox1.Items.Clear();
			foreach (ROIPrimitiveOperation roiprimitiveOperation in ROIPrimitiveOperation.Operations)
			{
				this.comboBox1.Items.Add(roiprimitiveOperation.Translation);
			}
			this.comboBox1.SelectedIndex = 0;
		}

		// Token: 0x060005A5 RID: 1445 RVA: 0x00023B5C File Offset: 0x00021D5C
		public override void PrepareForm(ROIConfigData config)
		{
			this.roiConfig = config;
			this.comboBox2.Items.Clear();
			foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
			{
				this.comboBox2.Items.Add(roidefinitionData.Name);
			}
		}

		// Token: 0x060005A6 RID: 1446 RVA: 0x00023BE4 File Offset: 0x00021DE4
		public override void LoadFormContents(ROIPrimitiveData prim)
		{
			ROIReferenceData roireferenceData = (ROIReferenceData)prim;
			this.comboBox1.SelectedIndex = ROIPrimitiveOperation.GetOperationIndex(prim.OperationType);
			this.doubleTextBox3.Text = roireferenceData.Coefficient.ToString();
			this.doubleTextBox4.Text = roireferenceData.CoefficientError.ToString();
			this.comboBox2.SelectedItem = roireferenceData.Reference;
			this.textBox1.Text = roireferenceData.Note;
		}

		// Token: 0x060005A7 RID: 1447 RVA: 0x00023C6C File Offset: 0x00021E6C
		public override bool SaveFormContents(ROIPrimitiveData prim)
		{
			ROIReferenceData roireferenceData = (ROIReferenceData)prim;
			try
			{
				ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[this.comboBox1.SelectedIndex];
				roireferenceData.Operation = roiprimitiveOperation;
				roireferenceData.OperationType = roiprimitiveOperation.Name;
				roireferenceData.Coefficient = double.Parse(this.doubleTextBox3.Text);
				roireferenceData.CoefficientError = double.Parse(this.doubleTextBox4.Text);
				roireferenceData.Reference = (string)this.comboBox2.SelectedItem;
				if (roireferenceData.Reference == null)
				{
					roireferenceData.Reference = "";
				}
				roireferenceData.Note = this.textBox1.Text;
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		// Token: 0x060005A8 RID: 1448 RVA: 0x00023D38 File Offset: 0x00021F38
		void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005A9 RID: 1449 RVA: 0x00023D40 File Offset: 0x00021F40
		void doubleTextBox3_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005AA RID: 1450 RVA: 0x00023D48 File Offset: 0x00021F48
		void doubleTextBox4_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005AB RID: 1451 RVA: 0x00023D50 File Offset: 0x00021F50
		void textBox1_TextChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005AC RID: 1452 RVA: 0x00023D58 File Offset: 0x00021F58
		void doubleTextBox1_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005AD RID: 1453 RVA: 0x00023D60 File Offset: 0x00021F60
		void doubleTextBox2_Validated(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005AE RID: 1454 RVA: 0x00023D68 File Offset: 0x00021F68
		void comboBox2_DropDown(object sender, EventArgs e)
		{
			string selectedItem = (string)this.comboBox2.SelectedItem;
			if (this.roiConfig != null)
			{
				this.comboBox2.Items.Clear();
				foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
				{
					this.comboBox2.Items.Add(roidefinitionData.Name);
				}
			}
			this.comboBox2.SelectedItem = selectedItem;
		}

		// Token: 0x060005AF RID: 1455 RVA: 0x00023E10 File Offset: 0x00022010
		void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
		{
			base.PrimitiveModified();
		}

		// Token: 0x060005B0 RID: 1456 RVA: 0x00023E18 File Offset: 0x00022018
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060005B1 RID: 1457 RVA: 0x00023E40 File Offset: 0x00022040
		void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(ROIReferenceControl));
			this.comboBox1 = new ComboBox();
			this.label1 = new Label();
			this.textBox1 = new TextBox();
			this.label7 = new Label();
			this.label8 = new Label();
			this.doubleTextBox3 = new DoubleTextBox();
			this.doubleTextBox4 = new DoubleTextBox();
			this.label6 = new Label();
			this.label9 = new Label();
			this.comboBox2 = new ComboBox();
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
			componentResourceManager.ApplyResources(this.label9, "label9");
			this.label9.Name = "label9";
			componentResourceManager.ApplyResources(this.comboBox2, "comboBox2");
			this.comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
			this.comboBox2.FormattingEnabled = true;
			this.comboBox2.Name = "comboBox2";
			this.comboBox2.DropDown += this.comboBox2_DropDown;
			this.comboBox2.SelectedIndexChanged += this.comboBox2_SelectedIndexChanged;
			componentResourceManager.ApplyResources(this, "$this");
			base.Controls.Add(this.comboBox2);
			base.Controls.Add(this.label9);
			base.Controls.Add(this.label6);
			base.Controls.Add(this.doubleTextBox4);
			base.Controls.Add(this.doubleTextBox3);
			base.Controls.Add(this.label8);
			base.Controls.Add(this.label7);
			base.Controls.Add(this.textBox1);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.comboBox1);
			base.Name = "ROIReferenceControl";
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x040002EA RID: 746
		ROIConfigData roiConfig;

		// Token: 0x040002EB RID: 747
		IContainer components;

		// Token: 0x040002EC RID: 748
		ComboBox comboBox1;

		// Token: 0x040002ED RID: 749
		Label label1;

		// Token: 0x040002EE RID: 750
		TextBox textBox1;

		// Token: 0x040002EF RID: 751
		Label label7;

		// Token: 0x040002F0 RID: 752
		Label label8;

		// Token: 0x040002F1 RID: 753
		DoubleTextBox doubleTextBox3;

		// Token: 0x040002F2 RID: 754
		DoubleTextBox doubleTextBox4;

		// Token: 0x040002F3 RID: 755
		Label label6;

		// Token: 0x040002F4 RID: 756
		Label label9;

		// Token: 0x040002F5 RID: 757
		ComboBox comboBox2;
	}
}
