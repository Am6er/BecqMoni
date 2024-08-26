using System;

namespace BecquerelMonitor
{
    
    public partial class DCCountRateView : ToolWindow
    {
        
        public DCCountRateView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
        }

        public decimal getWindow()
        {
            return this.windowControl.Value;
        } 

        
        public void UpdateInfo(decimal Cps, int sEffRatio)
        {
            this.LossCountsRatioValLbl.Text = sEffRatio.ToString();
            this.cpslabel.Text = String.Format("{0:F2}", Cps);
        }

        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        MainForm mainForm;
    }
}
