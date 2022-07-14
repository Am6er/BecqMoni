namespace BecquerelMonitor
{
    // Token: 0x020000F2 RID: 242
    public class ROISimpleDifferenceData : ROIPrimitiveData
    {
        // Token: 0x1700031E RID: 798
        // (get) Token: 0x06000BCB RID: 3019 RVA: 0x00047A14 File Offset: 0x00045C14
        // (set) Token: 0x06000BCC RID: 3020 RVA: 0x00047A1C File Offset: 0x00045C1C
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

        // Token: 0x1700031F RID: 799
        // (get) Token: 0x06000BCD RID: 3021 RVA: 0x00047A28 File Offset: 0x00045C28
        // (set) Token: 0x06000BCE RID: 3022 RVA: 0x00047A30 File Offset: 0x00045C30
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

        // Token: 0x06000BCF RID: 3023 RVA: 0x00047A3C File Offset: 0x00045C3C
        public ROISimpleDifferenceData()
        {
        }

        // Token: 0x06000BD0 RID: 3024 RVA: 0x00047A44 File Offset: 0x00045C44
        public ROISimpleDifferenceData(ROISimpleDifferenceData prim) : base(prim)
        {
            this.lowerLimit = prim.lowerLimit;
            this.upperLimit = prim.upperLimit;
        }

        // Token: 0x06000BD1 RID: 3025 RVA: 0x00047A68 File Offset: 0x00045C68
        public override ROIPrimitiveData Clone()
        {
            return new ROISimpleDifferenceData(this);
        }

        // Token: 0x04000781 RID: 1921
        double lowerLimit;

        // Token: 0x04000782 RID: 1922
        double upperLimit;
    }
}
