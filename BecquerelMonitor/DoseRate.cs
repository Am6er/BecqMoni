using System;

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

        public double Epsilon
        {
            get
            {
                return 100.0 * this.error / this.rate;
            }
        }

        public override string ToString()
        {
            string uom = "μSv/h";
            double rate = this.Rate;
            double error = this.Error;
            double epsilon = this.Epsilon;
            if (rate > 1000000.0)
            {
                uom = "Sv/h";
                rate /= 1000000.0;
                error /= 1000000.0;
            }
            else if (rate > 1000.0)
            {
                uom = "mSv/h";
                rate /= 1000.0;
                error /= 1000.0;
            }
            string rate_str;
            if (rate < 10.0)
            {
                rate_str = rate.ToString("f3");
            }
            else if (rate < 100.0)
            {
                rate_str = rate.ToString("f2");
            }
            else
            {
                rate_str = rate.ToString("f1");
            }
            string error_str;
            string epsilon_str = epsilon.ToString("f1");
            if (error < 10.0)
            {
                error_str = error.ToString("f3");
            }
            else if (error < 100.0)
            {
                error_str = error.ToString("f2");
            }
            else
            {
                error_str = error.ToString("f1");
            }
            return String.Format("{0} ±{1} ({2}%) {3}", rate_str, error_str, epsilon_str, uom);
        }

        // Token: 0x040000F4 RID: 244
        double rate = 0.0;

        // Token: 0x040000F5 RID: 245
        double error = 0.0;
    }
}
