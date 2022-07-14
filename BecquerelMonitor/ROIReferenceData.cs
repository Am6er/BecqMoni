namespace BecquerelMonitor
{
    // Token: 0x02000119 RID: 281
    public class ROIReferenceData : ROIPrimitiveData
    {
        // Token: 0x170003CD RID: 973
        // (get) Token: 0x06000EF0 RID: 3824 RVA: 0x0005687C File Offset: 0x00054A7C
        // (set) Token: 0x06000EF1 RID: 3825 RVA: 0x00056884 File Offset: 0x00054A84
        public string Reference
        {
            get
            {
                return this.reference;
            }
            set
            {
                this.reference = value;
            }
        }

        // Token: 0x06000EF2 RID: 3826 RVA: 0x00056890 File Offset: 0x00054A90
        public ROIReferenceData()
        {
        }

        // Token: 0x06000EF3 RID: 3827 RVA: 0x00056898 File Offset: 0x00054A98
        public ROIReferenceData(ROIReferenceData prim) : base(prim)
        {
            this.reference = string.Copy(prim.reference);
        }

        // Token: 0x06000EF4 RID: 3828 RVA: 0x000568B4 File Offset: 0x00054AB4
        public override ROIPrimitiveData Clone()
        {
            return new ROIReferenceData(this);
        }

        // Token: 0x0400089E RID: 2206
        string reference;
    }
}
