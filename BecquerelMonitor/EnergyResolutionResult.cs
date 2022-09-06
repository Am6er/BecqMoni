namespace BecquerelMonitor
{
    // Token: 0x02000085 RID: 133
    public class EnergyResolutionResult
    {
        // Token: 0x17000207 RID: 519
        // (get) Token: 0x060006C8 RID: 1736 RVA: 0x0002850C File Offset: 0x0002670C
        // (set) Token: 0x060006C9 RID: 1737 RVA: 0x00028514 File Offset: 0x00026714
        public double Resolution
        {
            get
            {
                return this.resolution;
            }
            set
            {
                this.resolution = value;
            }
        }

        public double ResolutionInkeV
        {
            get
            {
                return this.resolutioninkev;
            }
            set
            {
                this.resolutioninkev = value;
            }
        }

        // Token: 0x17000208 RID: 520
        // (get) Token: 0x060006CA RID: 1738 RVA: 0x00028520 File Offset: 0x00026720
        // (set) Token: 0x060006CB RID: 1739 RVA: 0x00028528 File Offset: 0x00026728
        public double StartChannel
        {
            get
            {
                return this.startChannel;
            }
            set
            {
                this.startChannel = value;
            }
        }

        // Token: 0x17000209 RID: 521
        // (get) Token: 0x060006CC RID: 1740 RVA: 0x00028534 File Offset: 0x00026734
        // (set) Token: 0x060006CD RID: 1741 RVA: 0x0002853C File Offset: 0x0002673C
        public double EndChannel
        {
            get
            {
                return this.endChannel;
            }
            set
            {
                this.endChannel = value;
            }
        }

        // Token: 0x1700020A RID: 522
        // (get) Token: 0x060006CE RID: 1742 RVA: 0x00028548 File Offset: 0x00026748
        // (set) Token: 0x060006CF RID: 1743 RVA: 0x00028550 File Offset: 0x00026750
        public double MaxChannel
        {
            get
            {
                return this.maxChannel;
            }
            set
            {
                this.maxChannel = value;
            }
        }

        // Token: 0x1700020B RID: 523
        // (get) Token: 0x060006D0 RID: 1744 RVA: 0x0002855C File Offset: 0x0002675C
        // (set) Token: 0x060006D1 RID: 1745 RVA: 0x00028564 File Offset: 0x00026764
        public double LeftChannel
        {
            get
            {
                return this.leftChannel;
            }
            set
            {
                this.leftChannel = value;
            }
        }

        // Token: 0x1700020C RID: 524
        // (get) Token: 0x060006D2 RID: 1746 RVA: 0x00028570 File Offset: 0x00026770
        // (set) Token: 0x060006D3 RID: 1747 RVA: 0x00028578 File Offset: 0x00026778
        public double RightChannel
        {
            get
            {
                return this.rightChannel;
            }
            set
            {
                this.rightChannel = value;
            }
        }

        // Token: 0x1700020D RID: 525
        // (get) Token: 0x060006D4 RID: 1748 RVA: 0x00028584 File Offset: 0x00026784
        // (set) Token: 0x060006D5 RID: 1749 RVA: 0x0002858C File Offset: 0x0002678C
        public double StartValue
        {
            get
            {
                return this.startValue;
            }
            set
            {
                this.startValue = value;
            }
        }

        // Token: 0x1700020E RID: 526
        // (get) Token: 0x060006D6 RID: 1750 RVA: 0x00028598 File Offset: 0x00026798
        // (set) Token: 0x060006D7 RID: 1751 RVA: 0x000285A0 File Offset: 0x000267A0
        public double EndValue
        {
            get
            {
                return this.endValue;
            }
            set
            {
                this.endValue = value;
            }
        }

        // Token: 0x1700020F RID: 527
        // (get) Token: 0x060006D8 RID: 1752 RVA: 0x000285AC File Offset: 0x000267AC
        // (set) Token: 0x060006D9 RID: 1753 RVA: 0x000285B4 File Offset: 0x000267B4
        public double HalfValue
        {
            get
            {
                return this.halfValue;
            }
            set
            {
                this.halfValue = value;
            }
        }

        // Token: 0x17000210 RID: 528
        // (get) Token: 0x060006DA RID: 1754 RVA: 0x000285C0 File Offset: 0x000267C0
        // (set) Token: 0x060006DB RID: 1755 RVA: 0x000285C8 File Offset: 0x000267C8
        public double MaxValue
        {
            get
            {
                return this.maxValue;
            }
            set
            {
                this.maxValue = value;
            }
        }

        // Token: 0x17000211 RID: 529
        // (get) Token: 0x060006DC RID: 1756 RVA: 0x000285D4 File Offset: 0x000267D4
        // (set) Token: 0x060006DD RID: 1757 RVA: 0x000285DC File Offset: 0x000267DC
        public double MaxBaseValue
        {
            get
            {
                return this.maxBaseValue;
            }
            set
            {
                this.maxBaseValue = value;
            }
        }

        // Token: 0x04000389 RID: 905
        double resolution;

        double resolutioninkev;

        // Token: 0x0400038A RID: 906
        double startChannel;

        // Token: 0x0400038B RID: 907
        double endChannel;

        // Token: 0x0400038C RID: 908
        double maxChannel;

        // Token: 0x0400038D RID: 909
        double leftChannel;

        // Token: 0x0400038E RID: 910
        double rightChannel;

        // Token: 0x0400038F RID: 911
        double startValue;

        // Token: 0x04000390 RID: 912
        double endValue;

        // Token: 0x04000391 RID: 913
        double halfValue;

        // Token: 0x04000392 RID: 914
        double maxValue;

        // Token: 0x04000393 RID: 915
        double maxBaseValue;
    }
}
