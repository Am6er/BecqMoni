namespace BecquerelMonitor
{
    // Token: 0x020000D5 RID: 213
    public class SimplePeakDetectionMethodConfig : PeakDetectionMethodConfig
    {
        // Token: 0x170002DE RID: 734
        // (get) Token: 0x06000AD4 RID: 2772 RVA: 0x00045390 File Offset: 0x00043590
        // (set) Token: 0x06000AD5 RID: 2773 RVA: 0x00045398 File Offset: 0x00043598
        public int PolynomialOrder
        {
            get
            {
                return this.polynomialOrder;
            }
            set
            {
                this.polynomialOrder = value;
            }
        }

        // Token: 0x170002DF RID: 735
        // (get) Token: 0x06000AD6 RID: 2774 RVA: 0x000453A4 File Offset: 0x000435A4
        // (set) Token: 0x06000AD7 RID: 2775 RVA: 0x000453AC File Offset: 0x000435AC
        public int WindowSize
        {
            get
            {
                return this.windowSize;
            }
            set
            {
                this.windowSize = value;
            }
        }

        // Token: 0x170002E0 RID: 736
        // (get) Token: 0x06000AD8 RID: 2776 RVA: 0x000453B8 File Offset: 0x000435B8
        // (set) Token: 0x06000AD9 RID: 2777 RVA: 0x000453C0 File Offset: 0x000435C0
        public double Threshold
        {
            get
            {
                return this.threshold;
            }
            set
            {
                this.threshold = value;
            }
        }

        // Token: 0x170002E1 RID: 737
        // (get) Token: 0x06000ADA RID: 2778 RVA: 0x000453CC File Offset: 0x000435CC
        // (set) Token: 0x06000ADB RID: 2779 RVA: 0x000453D4 File Offset: 0x000435D4
        public double Tolerance
        {
            get
            {
                return this.tolerance;
            }
            set
            {
                this.tolerance = value;
            }
        }

        // Token: 0x06000ADC RID: 2780 RVA: 0x000453E0 File Offset: 0x000435E0
        public SimplePeakDetectionMethodConfig()
        {
        }

        // Token: 0x06000ADD RID: 2781 RVA: 0x00045418 File Offset: 0x00043618
        public SimplePeakDetectionMethodConfig(SimplePeakDetectionMethodConfig config)
        {
            this.polynomialOrder = config.polynomialOrder;
            this.windowSize = config.windowSize;
            this.threshold = config.threshold;
            this.tolerance = config.tolerance;
        }

        // Token: 0x06000ADE RID: 2782 RVA: 0x0004548C File Offset: 0x0004368C
        public override PeakDetectionMethodConfig Clone()
        {
            return new SimplePeakDetectionMethodConfig(this);
        }

        // Token: 0x040006B4 RID: 1716
        int polynomialOrder = 1;

        // Token: 0x040006B5 RID: 1717
        int windowSize = 35;

        // Token: 0x040006B6 RID: 1718
        double threshold = 1.0;

        // Token: 0x040006B7 RID: 1719
        double tolerance = 10.0;
    }
}
