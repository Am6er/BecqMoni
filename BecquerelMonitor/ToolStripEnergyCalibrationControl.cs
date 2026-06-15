using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class ToolStripEnergyCalibrationControl : UserControl
    {
        ToolStripEnergyCalibrationButton button;

        PolynomialEnergyCalibration energyCalibration;

        PolynomialEnergyCalibration defaultEnergyCalibration;

        public ToolStripEnergyCalibrationControl()
        {
            this.InitializeComponent();
        }

        public ToolStripEnergyCalibrationControl(ToolStripEnergyCalibrationButton button)
            : this()
        {
            this.button = button;
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            if (globalConfig != null)
            {
                this.numericUpDown1.Increment = (decimal)globalConfig.ChartViewConfig.Energy2ndCoefficientStep;
                this.numericUpDown2.Increment = (decimal)globalConfig.ChartViewConfig.EnergyCoefficientStep;
                this.numericUpDown3.Increment = (decimal)globalConfig.ChartViewConfig.EnergyOffsetStep;
            }
        }

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

        void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (this.energyCalibration == null || this.button == null)
            {
                return;
            }
            this.energyCalibration.Coefficients[2] = (double)this.numericUpDown1.Value;
            this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
        }

        void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            if (this.energyCalibration == null || this.button == null)
            {
                return;
            }
            this.energyCalibration.Coefficients[1] = (double)this.numericUpDown2.Value;
            this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
        }

        void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            if (this.energyCalibration == null || this.button == null)
            {
                return;
            }
            this.energyCalibration.Coefficients[0] = (double)this.numericUpDown3.Value;
            this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
        }

        void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
            }
        }

        void numericUpDown3_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
            }
        }

        void button1_Click(object sender, EventArgs e)
        {
            if (this.energyCalibration == null || this.defaultEnergyCalibration == null || this.button == null)
            {
                return;
            }
            this.energyCalibration.Coefficients[2] = this.defaultEnergyCalibration.Coefficients[2];
            this.numericUpDown1.Value = (decimal)this.energyCalibration.Coefficients[2];
            this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
        }

        void button2_Click(object sender, EventArgs e)
        {
            if (this.energyCalibration == null || this.defaultEnergyCalibration == null || this.button == null)
            {
                return;
            }
            this.energyCalibration.Coefficients[1] = this.defaultEnergyCalibration.Coefficients[1];
            this.numericUpDown2.Value = (decimal)this.energyCalibration.Coefficients[1];
            this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
        }

        void button3_Click(object sender, EventArgs e)
        {
            if (this.energyCalibration == null || this.defaultEnergyCalibration == null || this.button == null)
            {
                return;
            }
            this.energyCalibration.Coefficients[0] = this.defaultEnergyCalibration.Coefficients[0];
            this.numericUpDown3.Value = (decimal)this.energyCalibration.Coefficients[0];
            this.button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(this.energyCalibration));
        }
    }
}
