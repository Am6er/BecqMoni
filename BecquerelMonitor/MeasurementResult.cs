namespace BecquerelMonitor
{
    // Token: 0x020000E4 RID: 228
    public class MeasurementResult
    {
        // Token: 0x170002F5 RID: 757
        // (get) Token: 0x06000B2F RID: 2863 RVA: 0x000460B0 File Offset: 0x000442B0
        // (set) Token: 0x06000B30 RID: 2864 RVA: 0x000460B8 File Offset: 0x000442B8
        public ROIDefinitionData ROIDefinition
        {
            get
            {
                return this.roiDefinition;
            }
            set
            {
                this.roiDefinition = value;
            }
        }

        // Token: 0x170002F6 RID: 758
        // (get) Token: 0x06000B31 RID: 2865 RVA: 0x000460C4 File Offset: 0x000442C4
        // (set) Token: 0x06000B32 RID: 2866 RVA: 0x000460CC File Offset: 0x000442CC
        public double ResultValue
        {
            get
            {
                return this.resultValue;
            }
            set
            {
                this.resultValue = value;
            }
        }

        // Token: 0x170002F7 RID: 759
        // (get) Token: 0x06000B33 RID: 2867 RVA: 0x000460D8 File Offset: 0x000442D8
        // (set) Token: 0x06000B34 RID: 2868 RVA: 0x000460E0 File Offset: 0x000442E0
        public double ResultError
        {
            get
            {
                return this.resultError;
            }
            set
            {
                this.resultError = value;
            }
        }

        // Token: 0x170002F8 RID: 760
        // (get) Token: 0x06000B35 RID: 2869 RVA: 0x000460EC File Offset: 0x000442EC
        // (set) Token: 0x06000B36 RID: 2870 RVA: 0x000460F4 File Offset: 0x000442F4
        public double MDA
        {
            get
            {
                return this.mda;
            }
            set
            {
                this.mda = value;
            }
        }

        // Token: 0x170002F9 RID: 761
        // (get) Token: 0x06000B37 RID: 2871 RVA: 0x00046100 File Offset: 0x00044300
        // (set) Token: 0x06000B38 RID: 2872 RVA: 0x00046108 File Offset: 0x00044308
        public bool IsValid
        {
            get
            {
                return this.isValid;
            }
            set
            {
                this.isValid = value;
            }
        }

        // Token: 0x06000B39 RID: 2873 RVA: 0x00046114 File Offset: 0x00044314
        public MeasurementResult(ROIDefinitionData roiDefinition, double resultValue, double resultError)
        {
            this.roiDefinition = roiDefinition;
            this.resultValue = resultValue;
            this.resultError = resultError;
        }

        // Token: 0x06000B3A RID: 2874 RVA: 0x00046138 File Offset: 0x00044338
        public MeasurementResult(ROIDefinitionData roiDefinition, double resultValue, double resultError, double mda)
        {
            this.roiDefinition = roiDefinition;
            this.resultValue = resultValue;
            this.resultError = resultError;
            this.mda = mda;
        }

        // Token: 0x04000754 RID: 1876
        ROIDefinitionData roiDefinition;

        // Token: 0x04000755 RID: 1877
        double resultValue;

        // Token: 0x04000756 RID: 1878
        double resultError;

        // Token: 0x04000757 RID: 1879
        double mda;

        // Token: 0x04000758 RID: 1880
        bool isValid = true;
    }
}
