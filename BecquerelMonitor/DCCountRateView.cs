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

        
        public void ShowCountsRate(decimal value)
        {
            if (value == 0)
            {
                this.cpslabel.Text = "0.00";
                return;
            }

            this.cpslabel.Text = String.Format("{0:F2}", value);
        }

        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        MainForm mainForm;
    }
}
