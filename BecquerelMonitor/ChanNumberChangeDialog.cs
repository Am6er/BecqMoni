using System;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    public partial class ChanNumberChangeDialog : Form
    {
        MainForm mainForm;
        bool resultStatus = false;

        public ChanNumberChangeDialog()
        {
            InitializeComponent();
        }

        public ChanNumberChangeDialog(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.resultStatus = false;
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.resultStatus = true;
            Close();
        }

        public int SendData()
        {
            if (int.TryParse(textBox1.Text, out int newChan))
            {
                if (resultStatus)
                {
                    return newChan;
                }
            }
            return 0;
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.resultStatus = true;
                Close();
            }
        }
    }
}
