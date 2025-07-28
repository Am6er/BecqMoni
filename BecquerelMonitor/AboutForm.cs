using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
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

        private void label7_Click(object sender, EventArgs e)
        {
            try
            {
                string url = "https://github.com/Am6er/BecqMoni";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception) { }
        }

        private void label8_Click(object sender, EventArgs e)
        {
            try
            {
                string url = "https://www.youtube.com/@Am6er";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception) { }
        }

        private void label9_Click(object sender, EventArgs e)
        {
            try
            {
                string url = "https://rutube.ru/channel/30585350/";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception) { }
        }
    }
}
