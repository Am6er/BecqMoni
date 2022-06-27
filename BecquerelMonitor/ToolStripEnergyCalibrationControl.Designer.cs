using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x020000A2 RID: 162
	public class ToolStripEnergyCalibrationControl : UserControl
	{
		// Token: 0x060007F8 RID: 2040 RVA: 0x0002C414 File Offset: 0x0002A614
		public ToolStripEnergyCalibrationControl(ToolStripEnergyCalibrationButton button)
		{
			this.InitializeComponent();
			this.button = button;
			GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
			this.numericUpDown1.Increment = (decimal)globalConfig.ChartViewConfig.Energy2ndCoefficientStep;
			this.numericUpDown2.Increment = (decimal)globalConfig.ChartViewConfig.EnergyCoefficientStep;
			this.numericUpDown3.Increment = (decimal)globalConfig.ChartViewConfig.EnergyOffsetStep;
		}

		// Token: 0x060007F9 RID: 2041 RVA: 0x0002C498 File Offset: 0x0002A698
		public void SetEnergyCalibration(EnergyCalibration energyCalibration, EnergyCalibration defaultEnergyCalibration)
		{
			this.energyCalibration = (PolynomialEnergyCalibration)energyCalibration;
			this.defaultEnergyCalibration = (PolynomialEnergyCalibration)defaultEnergyCalibration;
			decimal num = (decimal)this.energyCalibration.Coefficients[2];
			if (num > this.numericUpDown1.Maximum)
			{
				this.numericUpDown1.Value = this.numericUpDown1.Maximum;
			}
			else if (num < this.numericUpDown1.Minimum)
			{
				this.numericUpDown1.Value = this.numericUpDown1.Minimum;
			}
			else
			{
				this.numericUpDown1.Value = num;
			}
			num = (decimal)this.energyCalibration.Coefficients[1];
			if (num > this.numericUpDown2.Maximum)
			{
				this.numericUpDown2.Value = this.numericUpDown2.Maximum;
			}
			else if (num < this.numericUpDown2.Minimum)
			{
				this.numericUpDown2.Value = this.numericUpDown2.Minimum;
			}
			else
			{
				this.numericUpDown2.Value = num;
			}
			num = (decimal)this.energyCalibration.Coefficients[0];
			if (num > this.numericUpDown3.Maximum)
			{
				this.numericUpDown3.Value = this.numericUpDown3.Maximum;
				return;
			}
			if (num < this.numericUpDown3.Minimum)
			{
				this.numericUpDown3.Value = this.numericUpDown3.Minimum;
				return;
			}
			this.numericUpDown3.Value = num;
		}

		// Token: 0x060007FA RID: 2042 RVA: 0x0002C640 File Offset: 0x0002A840
		void numericUpDown1_ValueChanged(object sender, EventArgs e)
		{
			this.energyCalibration.Coefficients[2] = (double)this.numericUpDown1.Value;
			this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
		}

		// Token: 0x060007FB RID: 2043 RVA: 0x0002C678 File Offset: 0x0002A878
		void numericUpDown2_ValueChanged(object sender, EventArgs e)
		{
			this.energyCalibration.Coefficients[1] = (double)this.numericUpDown2.Value;
			this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
		}

		// Token: 0x060007FC RID: 2044 RVA: 0x0002C6B0 File Offset: 0x0002A8B0
		void numericUpDown3_ValueChanged(object sender, EventArgs e)
		{
			this.energyCalibration.Coefficients[0] = (double)this.numericUpDown3.Value;
			this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
		}

		// Token: 0x060007FD RID: 2045 RVA: 0x0002C6E8 File Offset: 0x0002A8E8
		void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
			}
		}

		// Token: 0x060007FE RID: 2046 RVA: 0x0002C700 File Offset: 0x0002A900
		void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
			}
		}

		// Token: 0x060007FF RID: 2047 RVA: 0x0002C718 File Offset: 0x0002A918
		void numericUpDown3_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
			}
		}

		// Token: 0x06000800 RID: 2048 RVA: 0x0002C730 File Offset: 0x0002A930
		void button1_Click(object sender, EventArgs e)
		{
			this.energyCalibration.Coefficients[2] = this.defaultEnergyCalibration.Coefficients[2];
			this.numericUpDown1.Value = (decimal)this.energyCalibration.Coefficients[2];
			this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
		}

		// Token: 0x06000801 RID: 2049 RVA: 0x0002C790 File Offset: 0x0002A990
		void button2_Click(object sender, EventArgs e)
		{
			this.energyCalibration.Coefficients[1] = this.defaultEnergyCalibration.Coefficients[1];
			this.numericUpDown2.Value = (decimal)this.energyCalibration.Coefficients[1];
			this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
		}

		// Token: 0x06000802 RID: 2050 RVA: 0x0002C7F0 File Offset: 0x0002A9F0
		void button3_Click(object sender, EventArgs e)
		{
			this.energyCalibration.Coefficients[0] = this.defaultEnergyCalibration.Coefficients[0];
			this.numericUpDown3.Value = (decimal)this.energyCalibration.Coefficients[0];
			this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
		}

		// Token: 0x06000803 RID: 2051 RVA: 0x0002C850 File Offset: 0x0002AA50
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000804 RID: 2052 RVA: 0x0002C878 File Offset: 0x0002AA78
		void InitializeComponent()
		{
			ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(ToolStripEnergyCalibrationControl));
			this.numericUpDown1 = new NumericUpDown();
			this.numericUpDown2 = new NumericUpDown();
			this.label1 = new Label();
			this.label2 = new Label();
			this.label3 = new Label();
			this.button1 = new Button();
			this.button2 = new Button();
			this.numericUpDown3 = new NumericUpDown();
			this.button3 = new Button();
			this.label4 = new Label();
			((ISupportInitialize)this.numericUpDown1).BeginInit();
			((ISupportInitialize)this.numericUpDown2).BeginInit();
			((ISupportInitialize)this.numericUpDown3).BeginInit();
			base.SuspendLayout();
			componentResourceManager.ApplyResources(this.numericUpDown1, "numericUpDown1");
			this.numericUpDown1.DecimalPlaces = 7;
			this.numericUpDown1.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				262144
			});
			NumericUpDown numericUpDown = this.numericUpDown1;
			int[] array = new int[4];
			array[0] = 10;
			numericUpDown.Maximum = new decimal(array);
			this.numericUpDown1.Minimum = new decimal(new int[]
			{
				10,
				0,
				0,
				int.MinValue
			});
			this.numericUpDown1.Name = "numericUpDown1";
			this.numericUpDown1.ValueChanged += this.numericUpDown1_ValueChanged;
			this.numericUpDown1.KeyDown += this.numericUpDown1_KeyDown;
			componentResourceManager.ApplyResources(this.numericUpDown2, "numericUpDown2");
			this.numericUpDown2.DecimalPlaces = 4;
			this.numericUpDown2.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				131072
			});
			NumericUpDown numericUpDown2 = this.numericUpDown2;
			int[] array2 = new int[4];
			array2[0] = 1000;
			numericUpDown2.Maximum = new decimal(array2);
			this.numericUpDown2.Minimum = new decimal(new int[]
			{
				1,
				0,
				0,
				131072
			});
			this.numericUpDown2.Name = "numericUpDown2";
			this.numericUpDown2.Value = new decimal(new int[]
			{
				1,
				0,
				0,
				131072
			});
			this.numericUpDown2.ValueChanged += this.numericUpDown2_ValueChanged;
			this.numericUpDown2.KeyDown += this.numericUpDown2_KeyDown;
			componentResourceManager.ApplyResources(this.label1, "label1");
			this.label1.Name = "label1";
			componentResourceManager.ApplyResources(this.label2, "label2");
			this.label2.Name = "label2";
			componentResourceManager.ApplyResources(this.label3, "label3");
			this.label3.Name = "label3";
			componentResourceManager.ApplyResources(this.button1, "button1");
			this.button1.Name = "button1";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += this.button1_Click;
			componentResourceManager.ApplyResources(this.button2, "button2");
			this.button2.Name = "button2";
			this.button2.UseVisualStyleBackColor = true;
			this.button2.Click += this.button2_Click;
			componentResourceManager.ApplyResources(this.numericUpDown3, "numericUpDown3");
			this.numericUpDown3.DecimalPlaces = 4;
			this.numericUpDown3.Increment = new decimal(new int[]
			{
				1,
				0,
				0,
				131072
			});
			NumericUpDown numericUpDown3 = this.numericUpDown3;
			int[] array3 = new int[4];
			array3[0] = 10000;
			numericUpDown3.Maximum = new decimal(array3);
			this.numericUpDown3.Minimum = new decimal(new int[]
			{
				10000,
				0,
				0,
				int.MinValue
			});
			this.numericUpDown3.Name = "numericUpDown3";
			this.numericUpDown3.ValueChanged += this.numericUpDown3_ValueChanged;
			this.numericUpDown3.KeyDown += this.numericUpDown3_KeyDown;
			componentResourceManager.ApplyResources(this.button3, "button3");
			this.button3.Name = "button3";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += this.button3_Click;
			componentResourceManager.ApplyResources(this.label4, "label4");
			this.label4.Name = "label4";
			componentResourceManager.ApplyResources(this, "$this");
			base.AutoScaleMode = AutoScaleMode.Font;
			base.BorderStyle = BorderStyle.FixedSingle;
			base.Controls.Add(this.label4);
			base.Controls.Add(this.button3);
			base.Controls.Add(this.numericUpDown3);
			base.Controls.Add(this.button2);
			base.Controls.Add(this.button1);
			base.Controls.Add(this.label3);
			base.Controls.Add(this.label2);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.numericUpDown2);
			base.Controls.Add(this.numericUpDown1);
			base.Name = "ToolStripEnergyCalibrationControl";
			((ISupportInitialize)this.numericUpDown1).EndInit();
			((ISupportInitialize)this.numericUpDown2).EndInit();
			((ISupportInitialize)this.numericUpDown3).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000405 RID: 1029
		ToolStripEnergyCalibrationButton button;

		// Token: 0x04000406 RID: 1030
		PolynomialEnergyCalibration energyCalibration;

		// Token: 0x04000407 RID: 1031
		PolynomialEnergyCalibration defaultEnergyCalibration;

		// Token: 0x04000408 RID: 1032
		IContainer components;

		// Token: 0x04000409 RID: 1033
		NumericUpDown numericUpDown1;

		// Token: 0x0400040A RID: 1034
		NumericUpDown numericUpDown2;

		// Token: 0x0400040B RID: 1035
		Label label1;

		// Token: 0x0400040C RID: 1036
		Label label2;

		// Token: 0x0400040D RID: 1037
		Label label3;

		// Token: 0x0400040E RID: 1038
		Button button1;

		// Token: 0x0400040F RID: 1039
		Button button2;

		// Token: 0x04000410 RID: 1040
		NumericUpDown numericUpDown3;

		// Token: 0x04000411 RID: 1041
		Button button3;

		// Token: 0x04000412 RID: 1042
		Label label4;
	}
}
