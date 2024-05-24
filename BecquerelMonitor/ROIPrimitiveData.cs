using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x020000F1 RID: 241
    public class ROIPrimitiveData
    {
        // Token: 0x17000316 RID: 790
        // (get) Token: 0x06000BB8 RID: 3000 RVA: 0x00047898 File Offset: 0x00045A98
        // (set) Token: 0x06000BB9 RID: 3001 RVA: 0x000478A0 File Offset: 0x00045AA0
        public string PrimitiveType
        {
            get
            {
                return this.primitiveType;
            }
            set
            {
                this.primitiveType = value;
            }
        }

        // Token: 0x17000317 RID: 791
        // (get) Token: 0x06000BBA RID: 3002 RVA: 0x000478AC File Offset: 0x00045AAC
        // (set) Token: 0x06000BBB RID: 3003 RVA: 0x000478B4 File Offset: 0x00045AB4
        public string OperationType
        {
            get
            {
                return this.operationType;
            }
            set
            {
                this.operationType = value;
            }
        }

        // Token: 0x17000318 RID: 792
        // (get) Token: 0x06000BBC RID: 3004 RVA: 0x000478C0 File Offset: 0x00045AC0
        // (set) Token: 0x06000BBD RID: 3005 RVA: 0x000478C8 File Offset: 0x00045AC8
        [XmlIgnore]
        public ROIPrimitiveDefinition Primitive
        {
            get
            {
                return this.primitive;
            }
            set
            {
                this.primitive = value;
            }
        }

        // Token: 0x17000319 RID: 793
        // (get) Token: 0x06000BBE RID: 3006 RVA: 0x000478D4 File Offset: 0x00045AD4
        // (set) Token: 0x06000BBF RID: 3007 RVA: 0x000478DC File Offset: 0x00045ADC
        [XmlIgnore]
        public ROIPrimitiveOperation Operation
        {
            get
            {
                return this.operation;
            }
            set
            {
                this.operation = value;
            }
        }

        // Token: 0x1700031A RID: 794
        // (get) Token: 0x06000BC0 RID: 3008 RVA: 0x000478E8 File Offset: 0x00045AE8
        // (set) Token: 0x06000BC1 RID: 3009 RVA: 0x000478F0 File Offset: 0x00045AF0
        public double Coefficient
        {
            get
            {
                return this.coefficient;
            }
            set
            {
                this.coefficient = value;
            }
        }

        // Token: 0x1700031B RID: 795
        // (get) Token: 0x06000BC2 RID: 3010 RVA: 0x000478FC File Offset: 0x00045AFC
        // (set) Token: 0x06000BC3 RID: 3011 RVA: 0x00047904 File Offset: 0x00045B04
        public double CoefficientError
        {
            get
            {
                return this.coefficientError;
            }
            set
            {
                this.coefficientError = value;
            }
        }

        // Token: 0x1700031C RID: 796
        // (get) Token: 0x06000BC4 RID: 3012 RVA: 0x00047910 File Offset: 0x00045B10
        // (set) Token: 0x06000BC5 RID: 3013 RVA: 0x00047918 File Offset: 0x00045B18
        public CDATA Note
        {
            get
            {
                return this.note;
            }
            set
            {
                this.note = value;
            }
        }

        // Token: 0x1700031D RID: 797
        // (get) Token: 0x06000BC6 RID: 3014 RVA: 0x00047924 File Offset: 0x00045B24
        // (set) Token: 0x06000BC7 RID: 3015 RVA: 0x0004792C File Offset: 0x00045B2C
        [XmlIgnore]
        public ROIPrimitiveControl Control
        {
            get
            {
                return this.control;
            }
            set
            {
                this.control = value;
            }
        }

        // Token: 0x06000BC8 RID: 3016 RVA: 0x00047938 File Offset: 0x00045B38
        public ROIPrimitiveData()
        {
        }

        // Token: 0x06000BC9 RID: 3017 RVA: 0x00047960 File Offset: 0x00045B60
        public ROIPrimitiveData(ROIPrimitiveData prim)
        {
            this.primitiveType = string.Copy(prim.primitiveType);
            this.operationType = string.Copy(prim.operationType);
            this.primitive = prim.primitive;
            this.operation = prim.operation;
            this.coefficient = prim.coefficient;
            this.coefficientError = prim.coefficientError;
            this.note = new CDATA(prim.note);
            this.control = prim.control;
        }

        // Token: 0x06000BCA RID: 3018 RVA: 0x00047A0C File Offset: 0x00045C0C
        public virtual ROIPrimitiveData Clone()
        {
            return new ROIPrimitiveData(this);
        }

        public virtual void InitFromDefinition(ROIDefinitionData definition)
        {}

        // Token: 0x04000779 RID: 1913
        string primitiveType;

        // Token: 0x0400077A RID: 1914
        string operationType;

        // Token: 0x0400077B RID: 1915
        ROIPrimitiveDefinition primitive;

        // Token: 0x0400077C RID: 1916
        ROIPrimitiveOperation operation;

        // Token: 0x0400077D RID: 1917
        double coefficient = 1.0;

        // Token: 0x0400077E RID: 1918
        double coefficientError;

        // Token: 0x0400077F RID: 1919
        CDATA note = "";

        // Token: 0x04000780 RID: 1920
        ROIPrimitiveControl control;
    }
}
