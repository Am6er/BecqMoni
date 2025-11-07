using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class DCFwhmCalibrationView : ToolWindow
    {
        public DCFwhmCalibrationView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
        }

        private void DCFwhmCalibrationView_FormLoad(object sender, EventArgs e)
        {

        }

        private void DCFwhmCalibrationView_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        MainForm mainForm;

        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();
    }
}
