namespace BecquerelMonitor
{
    // Token: 0x0200001C RID: 28
    public class DoseRateConfig
    {
        // Token: 0x1700003F RID: 63
        // (get) Token: 0x060000E8 RID: 232 RVA: 0x00005414 File Offset: 0x00003614
        // (set) Token: 0x060000E9 RID: 233 RVA: 0x0000541C File Offset: 0x0000361C
        public double Sensitivity
        {
            get
            {
                return this.sensitivity;
            }
            set
            {
                this.sensitivity = value;
            }
        }

        // Token: 0x17000040 RID: 64
        // (get) Token: 0x060000EA RID: 234 RVA: 0x00005428 File Offset: 0x00003628
        // (set) Token: 0x060000EB RID: 235 RVA: 0x00005430 File Offset: 0x00003630
        public double LowerBound
        {
            get
            {
                return this.lowerBound;
            }
            set
            {
                this.lowerBound = value;
            }
        }

        // Token: 0x17000041 RID: 65
        // (get) Token: 0x060000EC RID: 236 RVA: 0x0000543C File Offset: 0x0000363C
        // (set) Token: 0x060000ED RID: 237 RVA: 0x00005444 File Offset: 0x00003644
        public double UpperBound
        {
            get
            {
                return this.upperBound;
            }
            set
            {
                this.upperBound = value;
            }
        }

        // Token: 0x060000EE RID: 238 RVA: 0x00005450 File Offset: 0x00003650
        public DoseRateConfig()
        {
        }

        // Token: 0x060000EF RID: 239 RVA: 0x00005468 File Offset: 0x00003668
        public DoseRateConfig(DoseRateConfig config)
        {
            this.sensitivity = config.sensitivity;
            this.lowerBound = config.lowerBound;
            this.upperBound = config.upperBound;
        }

        // Token: 0x060000F0 RID: 240 RVA: 0x000054A4 File Offset: 0x000036A4
        public DoseRateConfig Clone()
        {
            return new DoseRateConfig(this);
        }

        // Token: 0x04000063 RID: 99
        double sensitivity;

        // Token: 0x04000064 RID: 100
        double lowerBound;

        // Token: 0x04000065 RID: 101
        double upperBound = 10000.0;
    }
}
