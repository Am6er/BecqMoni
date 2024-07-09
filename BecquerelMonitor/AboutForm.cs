using BecquerelMonitor.Properties;
using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            this.InitializeComponent();
            base.Icon = Resources.becqmoni;
            this.label2.Text = "Version " + GlobalConfigManager.GetInstance().VersionString;
            this.textBox1.Text = Resources.LibraryLicensesMessage;
        }

        void button1_Click(object sender, EventArgs e)
        {
            base.Close();
        }
    }
}
