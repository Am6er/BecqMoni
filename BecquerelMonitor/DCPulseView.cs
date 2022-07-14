using System;

namespace BecquerelMonitor
{
    // Token: 0x020000C1 RID: 193
    public partial class DCPulseView : ToolWindow
    {
        // Token: 0x17000290 RID: 656
        // (get) Token: 0x06000956 RID: 2390 RVA: 0x00036938 File Offset: 0x00034B38
        // (set) Token: 0x06000957 RID: 2391 RVA: 0x00036940 File Offset: 0x00034B40
        public PulseView PulseView
        {
            get
            {
                return this.pulseView1;
            }
            set
            {
                this.pulseView1 = value;
            }
        }

        // Token: 0x17000291 RID: 657
        // (get) Token: 0x06000958 RID: 2392 RVA: 0x0003694C File Offset: 0x00034B4C
        // (set) Token: 0x06000959 RID: 2393 RVA: 0x00036954 File Offset: 0x00034B54
        public PulseView NGPulseView
        {
            get
            {
                return this.pulseView2;
            }
            set
            {
                this.pulseView2 = value;
            }
        }

        // Token: 0x17000292 RID: 658
        // (get) Token: 0x0600095A RID: 2394 RVA: 0x00036960 File Offset: 0x00034B60
        // (set) Token: 0x0600095B RID: 2395 RVA: 0x00036968 File Offset: 0x00034B68
        public MainForm MainForm
        {
            get
            {
                return this.mainForm;
            }
            set
            {
                this.mainForm = value;
            }
        }

        // Token: 0x17000293 RID: 659
        // (get) Token: 0x0600095C RID: 2396 RVA: 0x00036974 File Offset: 0x00034B74
        // (set) Token: 0x0600095D RID: 2397 RVA: 0x00036984 File Offset: 0x00034B84
        public bool DoUpdatePulseView
        {
            get
            {
                return this.checkBox1.Checked;
            }
            set
            {
                this.checkBox1.Checked = value;
            }
        }

        // Token: 0x0600095E RID: 2398 RVA: 0x00036994 File Offset: 0x00034B94
        public DCPulseView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
            this.checkBox1.Checked = mainForm.DoUpdatePulseView;
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            bool flag = false;
            if (globalConfig != null)
            {
                flag = globalConfig.AntiAliasingPulseView;
            }
            this.checkBox2.Checked = flag;
            this.pulseView1.AntiAliasing = flag;
            this.pulseView2.AntiAliasing = flag;
            this.RecalcPosition();
        }

        // Token: 0x0600095F RID: 2399 RVA: 0x00036A10 File Offset: 0x00034C10
        void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            this.mainForm.DoUpdatePulseView = this.checkBox1.Checked;
        }

        // Token: 0x06000960 RID: 2400 RVA: 0x00036A28 File Offset: 0x00034C28
        void DCPulseView_SizeChanged(object sender, EventArgs e)
        {
            this.RecalcPosition();
        }

        // Token: 0x06000961 RID: 2401 RVA: 0x00036A30 File Offset: 0x00034C30
        void RecalcPosition()
        {
            this.pulseView1.Left = 6;
            this.pulseView1.Width = (base.Width - 16) / 2;
            this.pulseView2.Left = this.pulseView1.Left + this.pulseView1.Width + 4;
            this.pulseView2.Width = (base.Width - 16) / 2;
            this.pulseView1.Height = base.Height - 54;
            this.pulseView2.Height = base.Height - 54;
            this.label1.Left = this.pulseView1.Left;
            this.label2.Left = this.pulseView2.Left;
        }

        // Token: 0x06000962 RID: 2402 RVA: 0x00036AF4 File Offset: 0x00034CF4
        void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            bool @checked = this.checkBox2.Checked;
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            if (globalConfig != null)
            {
                globalConfig.AntiAliasingPulseView = @checked;
            }
            this.pulseView1.AntiAliasing = @checked;
            this.pulseView2.AntiAliasing = @checked;
        }

        // Token: 0x0400052D RID: 1325
        MainForm mainForm;
    }
}
