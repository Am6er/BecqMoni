namespace BecquerelMonitor
{
    // Token: 0x02000031 RID: 49
    public class DoseRate
    {
        // Token: 0x17000121 RID: 289
        // (get) Token: 0x060002AB RID: 683 RVA: 0x0000D1E4 File Offset: 0x0000B3E4
        // (set) Token: 0x060002AC RID: 684 RVA: 0x0000D1EC File Offset: 0x0000B3EC
        public double Rate
        {
            get
            {
                return this.rate;
            }
            set
            {
                this.rate = value;
            }
        }

        // Token: 0x17000122 RID: 290
        // (get) Token: 0x060002AD RID: 685 RVA: 0x0000D1F8 File Offset: 0x0000B3F8
        // (set) Token: 0x060002AE RID: 686 RVA: 0x0000D200 File Offset: 0x0000B400
        public double Error
        {
            get
            {
                return this.error;
            }
            set
            {
                this.error = value;
            }
        }

        public string ToString()
        {
            string uom = "μSv/h";
            double num = this.Rate;
            double num2 = this.Error;
            if (num > 1000000.0)
            {
                uom = "Sv/h";
                num /= 1000000.0;
                num2 /= 1000000.0;
            }
            else if (num > 1000.0)
            {
                uom = "mSv/h";
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
            return str + " ±" + str2 + " " + uom;
        }

        // Token: 0x040000F4 RID: 244
        double rate = 0.0;

        // Token: 0x040000F5 RID: 245
        double error = 0.0;
    }
}
