using System;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    public partial class ChanNumberChangeDialog : Form
    {
        MainForm mainForm;

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
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SendData();
        }

        void SendData()
        {
            if (int.TryParse(textBox1.Text, out int newChan))
            {
                this.mainForm.newChan = newChan;
            } else
            {
                this.mainForm.newChan = 0;
            }
            Close();
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SendData();
            }
        }
    }
}
