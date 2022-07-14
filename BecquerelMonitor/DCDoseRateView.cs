namespace BecquerelMonitor
{
    // Token: 0x020000A6 RID: 166
    public partial class DCDoseRateView : ToolWindow
    {
        // Token: 0x0600083E RID: 2110 RVA: 0x0002FF08 File Offset: 0x0002E108
        public DCDoseRateView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
        }

        // Token: 0x0600083F RID: 2111 RVA: 0x0002FF28 File Offset: 0x0002E128
        public void ShowDoseRate(DoseRate doseRate)
        {
            string text = "μSv/h";
            if (doseRate == null)
            {
                this.label3.Text = "0.000 ±0.000";
                this.label4.Text = text;
                return;
            }
            double num = doseRate.Rate;
            double num2 = doseRate.Error;
            if (num > 1000000.0)
            {
                text = "Sv/h";
                num /= 1000000.0;
                num2 /= 1000000.0;
            }
            else if (num > 1000.0)
            {
                text = "mSv/h";
                num /= 1000.0;
                num2 /= 1000.0;
            }
            string str;
            if (num < 10.0)
            {
                str = num.ToString("f3");
            }
            else if (num < 100.0)
            {
                str = num.ToString("f2");
            }
            else
            {
                str = num.ToString("f1");
            }
            string str2;
            if (num2 < 10.0)
            {
                str2 = num2.ToString("f3");
            }
            else if (num2 < 100.0)
            {
                str2 = num2.ToString("f2");
            }
            else
            {
                str2 = num2.ToString("f1");
            }
            this.label3.Text = str + " ±" + str2;
            this.label4.Text = text;
        }

        // Token: 0x04000449 RID: 1097
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x0400044A RID: 1098
        MainForm mainForm;
    }
}
