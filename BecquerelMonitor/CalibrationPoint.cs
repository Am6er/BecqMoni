using System;

namespace BecquerelMonitor
{
    // Token: 0x020000A5 RID: 165
    public class CalibrationPoint : IComparable
    {
        // Token: 0x1700024E RID: 590
        // (get) Token: 0x06000836 RID: 2102 RVA: 0x0002FB90 File Offset: 0x0002DD90
        // (set) Token: 0x06000837 RID: 2103 RVA: 0x0002FB98 File Offset: 0x0002DD98
        public int Channel
        {
            get
            {
                return this.channel;
            }
            set
            {
                this.channel = value;
            }
        }

        // Token: 0x1700024F RID: 591
        // (get) Token: 0x06000838 RID: 2104 RVA: 0x0002FBA4 File Offset: 0x0002DDA4
        // (set) Token: 0x06000839 RID: 2105 RVA: 0x0002FBAC File Offset: 0x0002DDAC
        public decimal Energy
        {
            get
            {
                return this.energy;
            }
            set
            {
                this.energy = value;
            }
        }

        // Token: 0x0600083A RID: 2106 RVA: 0x0002FBB8 File Offset: 0x0002DDB8
        public CalibrationPoint(int channel, decimal energy)
        {
            this.channel = channel;
            this.energy = energy;
        }

        // Token: 0x0600083B RID: 2107 RVA: 0x0002FBD0 File Offset: 0x0002DDD0
        public int CompareTo(object obj)
        {
            return this.channel.CompareTo(((CalibrationPoint)obj).Channel);
        }

        // Token: 0x0400043E RID: 1086
        int channel;

        // Token: 0x0400043F RID: 1087
        decimal energy;
    }
}
