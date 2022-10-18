using BecquerelMonitor.Properties;
using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class SpectrumCutOffDialog : Form
    {
        MainForm mainForm;

        bool resultStatus = false;

        public SpectrumCutOffDialog()
        {
            InitializeComponent();
        }

        public SpectrumCutOffDialog(MainForm mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void energyradioButton_CheckedChanged(object sender, EventArgs e)
        {
            RadioButtonCheck();
        }

        private void channelradioButton_CheckedChanged(object sender, EventArgs e)
        {
            RadioButtonCheck();
        }

        void RadioButtonCheck()
        {
            if (energyradioButton.Checked)
            {
                channelradioButton.Checked = false;
                channeltextBox.Enabled = false;
                energytextBox.Enabled = true;
            }
            else
            {
                channelradioButton.Checked = true;
                channeltextBox.Enabled = true;
                energytextBox.Enabled = false;
            }
        }

        public (bool ResultStatus, bool isEnergy, double energyVal, int channel) SendData()
        {
            if (energyradioButton.Checked)
            {
                if (double.TryParse(energytextBox.Text, out double energyVal))
                {
                    if (energyVal > 0 && energyVal < double.MaxValue)
                    {
                        return (this.resultStatus, true, energyVal, 0);
                    }
                }
                return (this.resultStatus, false, 0, 0);
            } else
            {
                if (int.TryParse(channeltextBox.Text, out int channel))
                {
                    if (channel > 0 && channel < int.MaxValue)
                    {
                        return (this.resultStatus, false, 0, channel);
                    }
                }
                return (this.resultStatus, false, 0, 0);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.resultStatus = true;
            Close();
        }
    }
}
