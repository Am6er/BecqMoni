using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200011B RID: 283
    public class ROICovellMethodData : ROIPrimitiveData
    {
        // Token: 0x170003D4 RID: 980
        // (get) Token: 0x06000F04 RID: 3844 RVA: 0x00056A5C File Offset: 0x00054C5C
        // (set) Token: 0x06000F05 RID: 3845 RVA: 0x00056A64 File Offset: 0x00054C64
        public double LowerLimit
        {
            get
            {
                return this.lowerLimit;
            }
            set
            {
                this.lowerLimit = value;
            }
        }

        // Token: 0x170003D5 RID: 981
        // (get) Token: 0x06000F06 RID: 3846 RVA: 0x00056A70 File Offset: 0x00054C70
        // (set) Token: 0x06000F07 RID: 3847 RVA: 0x00056A78 File Offset: 0x00054C78
        public double UpperLimit
        {
            get
            {
                return this.upperLimit;
            }
            set
            {
                this.upperLimit = value;
            }
        }

        // Token: 0x170003D6 RID: 982
        // (get) Token: 0x06000F08 RID: 3848 RVA: 0x00056A84 File Offset: 0x00054C84
        // (set) Token: 0x06000F09 RID: 3849 RVA: 0x00056A8C File Offset: 0x00054C8C
        [XmlIgnore]
        public int NumberOfSideChannels
        {
            get
            {
                return this.numberOfSideChannels;
            }
            set
            {
                this.numberOfSideChannels = value;
            }
        }

        // Token: 0x170003D7 RID: 983
        // (get) Token: 0x06000F0A RID: 3850 RVA: 0x00056A98 File Offset: 0x00054C98
        // (set) Token: 0x06000F0B RID: 3851 RVA: 0x00056AA0 File Offset: 0x00054CA0
        public double LeftRegionCenter
        {
            get
            {
                return this.leftRegionCenter;
            }
            set
            {
                this.leftRegionCenter = value;
            }
        }

        // Token: 0x170003D8 RID: 984
        // (get) Token: 0x06000F0C RID: 3852 RVA: 0x00056AAC File Offset: 0x00054CAC
        // (set) Token: 0x06000F0D RID: 3853 RVA: 0x00056AB4 File Offset: 0x00054CB4
        public double LeftRegionWidth
        {
            get
            {
                return this.leftRegionWidth;
            }
            set
            {
                this.leftRegionWidth = value;
            }
        }

        // Token: 0x170003D9 RID: 985
        // (get) Token: 0x06000F0E RID: 3854 RVA: 0x00056AC0 File Offset: 0x00054CC0
        // (set) Token: 0x06000F0F RID: 3855 RVA: 0x00056AC8 File Offset: 0x00054CC8
        public double RightRegionCenter
        {
            get
            {
                return this.rightRegionCenter;
            }
            set
            {
                this.rightRegionCenter = value;
            }
        }

        // Token: 0x170003DA RID: 986
        // (get) Token: 0x06000F10 RID: 3856 RVA: 0x00056AD4 File Offset: 0x00054CD4
        // (set) Token: 0x06000F11 RID: 3857 RVA: 0x00056ADC File Offset: 0x00054CDC
        public double RightRegionWidth
        {
            get
            {
                return this.rightRegionWidth;
            }
            set
            {
                this.rightRegionWidth = value;
            }
        }

        // Token: 0x06000F12 RID: 3858 RVA: 0x00056AE8 File Offset: 0x00054CE8
        public ROICovellMethodData()
        {
        }

        // Token: 0x06000F13 RID: 3859 RVA: 0x00056B18 File Offset: 0x00054D18
        public ROICovellMethodData(ROICovellMethodData prim) : base(prim)
        {
            this.lowerLimit = prim.lowerLimit;
            this.upperLimit = prim.upperLimit;
            this.numberOfSideChannels = prim.numberOfSideChannels;
            this.leftRegionCenter = prim.leftRegionCenter;
            this.leftRegionWidth = prim.leftRegionWidth;
            this.rightRegionCenter = prim.rightRegionCenter;
            this.rightRegionWidth = prim.rightRegionWidth;
        }

        // Token: 0x06000F14 RID: 3860 RVA: 0x00056BAC File Offset: 0x00054DAC
        public override ROIPrimitiveData Clone()
        {
            return new ROICovellMethodData(this);
        }

        public override void InitFromDefinition(ROIDefinitionData definition)
        {
            base.InitFromDefinition(definition);

            this.LowerLimit = definition.LowerLimit;
            this.UpperLimit = definition.UpperLimit;
            this.LeftRegionCenter = definition.LowerLimit;
            this.RightRegionCenter = definition.UpperLimit;
        }

        // Token: 0x040008A5 RID: 2213
        double lowerLimit;

        // Token: 0x040008A6 RID: 2214
        double upperLimit;

        // Token: 0x040008A7 RID: 2215
        int numberOfSideChannels = 5;

        // Token: 0x040008A8 RID: 2216
        double leftRegionCenter;

        // Token: 0x040008A9 RID: 2217
        double leftRegionWidth = 10.0;

        // Token: 0x040008AA RID: 2218
        double rightRegionCenter;

        // Token: 0x040008AB RID: 2219
        double rightRegionWidth = 10.0;
    }
}
