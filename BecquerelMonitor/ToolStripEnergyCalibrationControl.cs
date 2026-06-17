using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    [ToolboxItem(false)]
    public partial class ToolStripEnergyCalibrationControl : UserControl
    {
        ToolStripEnergyCalibrationButton button;
        PolynomialEnergyCalibration energyCalibration;
        PolynomialEnergyCalibration defaultEnergyCalibration;

        public ToolStripEnergyCalibrationControl()
        {
            InitializeComponent();
        }

        public ToolStripEnergyCalibrationControl(ToolStripEnergyCalibrationButton button)
            : this()
        {
            this.button = button;

            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            {
                return;
            }

            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            if (globalConfig != null)
            {
                numericUpDown1.Increment = (decimal)globalConfig.ChartViewConfig.Energy2ndCoefficientStep;
                numericUpDown2.Increment = (decimal)globalConfig.ChartViewConfig.EnergyCoefficientStep;
                numericUpDown3.Increment = (decimal)globalConfig.ChartViewConfig.EnergyOffsetStep;
            }
        }

        public void SetEnergyCalibration(EnergyCalibration energyCalibration, EnergyCalibration defaultEnergyCalibration)
        {
            this.energyCalibration = (PolynomialEnergyCalibration)energyCalibration;
            this.defaultEnergyCalibration = (PolynomialEnergyCalibration)defaultEnergyCalibration;

            decimal num = (decimal)this.energyCalibration.Coefficients[2];
            if (num > numericUpDown1.Maximum)
            {
                numericUpDown1.Value = numericUpDown1.Maximum;
            }
            else if (num < numericUpDown1.Minimum)
            {
                numericUpDown1.Value = numericUpDown1.Minimum;
            }
            else
            {
                numericUpDown1.Value = num;
            }

            num = (decimal)this.energyCalibration.Coefficients[1];
            if (num > numericUpDown2.Maximum)
            {
                numericUpDown2.Value = numericUpDown2.Maximum;
            }
            else if (num < numericUpDown2.Minimum)
            {
                numericUpDown2.Value = numericUpDown2.Minimum;
            }
            else
            {
                numericUpDown2.Value = num;
            }

            num = (decimal)this.energyCalibration.Coefficients[0];
            if (num > numericUpDown3.Maximum)
            {
                numericUpDown3.Value = numericUpDown3.Maximum;
                return;
            }

            if (num < numericUpDown3.Minimum)
            {
                numericUpDown3.Value = numericUpDown3.Minimum;
                return;
            }

            numericUpDown3.Value = num;
        }

        void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (energyCalibration == null || button == null)
            {
                return;
            }

            energyCalibration.Coefficients[2] = (double)numericUpDown1.Value;
            button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(energyCalibration));
        }

        void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            if (energyCalibration == null || button == null)
            {
                return;
            }

            energyCalibration.Coefficients[1] = (double)numericUpDown2.Value;
            button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(energyCalibration));
        }

        void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            if (energyCalibration == null || button == null)
            {
                return;
            }

            energyCalibration.Coefficients[0] = (double)numericUpDown3.Value;
            button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(energyCalibration));
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
            if (energyCalibration == null || defaultEnergyCalibration == null || button == null)
            {
                return;
            }

            energyCalibration.Coefficients[2] = defaultEnergyCalibration.Coefficients[2];
            numericUpDown1.Value = (decimal)energyCalibration.Coefficients[2];
            button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(energyCalibration));
        }

        void button2_Click(object sender, EventArgs e)
        {
            if (energyCalibration == null || defaultEnergyCalibration == null || button == null)
            {
                return;
            }

            energyCalibration.Coefficients[1] = defaultEnergyCalibration.Coefficients[1];
            numericUpDown2.Value = (decimal)energyCalibration.Coefficients[1];
            button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(energyCalibration));
        }

        void button3_Click(object sender, EventArgs e)
        {
            if (energyCalibration == null || defaultEnergyCalibration == null || button == null)
            {
                return;
            }

            energyCalibration.Coefficients[0] = defaultEnergyCalibration.Coefficients[0];
            numericUpDown3.Value = (decimal)energyCalibration.Coefficients[0];
            button.FireEnergyCalibrationChanged(new EnergyCalibrationChangedEventArgs(energyCalibration));
        }
    }
}
