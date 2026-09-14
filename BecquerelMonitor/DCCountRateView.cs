using System;
using System.Drawing;
using System.Globalization;

namespace BecquerelMonitor
{
    
    public partial class DCCountRateView : ToolWindow
    {
        
        public DCCountRateView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
        }

        public double getWindow()
        {
            return (double)this.windowControl.Value;
        } 

        
        public void UpdateInfo(double Cps, int sEffRatio, double deadTime)
        {
            this.LossCountsRatioValLbl.Text = sEffRatio.ToString(CultureInfo.InvariantCulture);
            this.cpslabel.Text = Cps.ToString("f2", CultureInfo.InvariantCulture);
            if (deadTime <= 20.0)
            {
                this.DeadTimeValLbl.ForeColor = Color.Black;
            }
            else if (deadTime > 20.0 && deadTime <= 50.0)
            {
                this.DeadTimeValLbl.ForeColor = Color.DarkOrange;
            }
            else if (deadTime > 50.0)
            {
                this.DeadTimeValLbl.ForeColor = Color.DarkRed;
            }
            this.DeadTimeValLbl.BackColor = this.DeadTimeValLbl.BackColor;
            this.DeadTimeValLbl.Text = deadTime.ToString("f4", CultureInfo.InvariantCulture);
        }

        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        MainForm mainForm;
    }
}
