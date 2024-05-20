using BecquerelMonitor.Properties;
using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class EffCalcMCDialog : Form
    {
        MainForm mainForm;
        string returnName = null;

        public EffCalcMCDialog()
        {
            InitializeComponent();
        }

        public EffCalcMCDialog(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
        }

        public string SendData()
        {
            return this.returnName;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (textBox1.Text.Length == 0)
            {
                MessageBox.Show(Resources.ERREmptyROIConfigName);
                return;
            }
            this.returnName = textBox1.Text;
            Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
