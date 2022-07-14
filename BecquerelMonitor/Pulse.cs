namespace BecquerelMonitor
{
    // Token: 0x020000F0 RID: 240
    public class Pulse
    {
        // Token: 0x17000313 RID: 787
        // (get) Token: 0x06000BB0 RID: 2992 RVA: 0x00047834 File Offset: 0x00045A34
        // (set) Token: 0x06000BB1 RID: 2993 RVA: 0x0004783C File Offset: 0x00045A3C
        public long Time
        {
            get
            {
                return this.time;
            }
            set
            {
                this.time = value;
            }
        }

        // Token: 0x17000314 RID: 788
        // (get) Token: 0x06000BB2 RID: 2994 RVA: 0x00047848 File Offset: 0x00045A48
        // (set) Token: 0x06000BB3 RID: 2995 RVA: 0x00047850 File Offset: 0x00045A50
        public double Height
        {
            get
            {
                return this.height;
            }
            set
            {
                this.height = value;
            }
        }

        // Token: 0x17000315 RID: 789
        // (get) Token: 0x06000BB4 RID: 2996 RVA: 0x0004785C File Offset: 0x00045A5C
        // (set) Token: 0x06000BB5 RID: 2997 RVA: 0x00047864 File Offset: 0x00045A64
        public int Width
        {
            get
            {
                return this.width;
            }
            set
            {
                this.width = value;
            }
        }

        // Token: 0x06000BB6 RID: 2998 RVA: 0x00047870 File Offset: 0x00045A70
        public Pulse()
        {
        }

        // Token: 0x06000BB7 RID: 2999 RVA: 0x00047878 File Offset: 0x00045A78
        public Pulse(long time, double height, int width)
        {
            this.time = time;
            this.height = height;
            this.width = width;
        }

        // Token: 0x04000776 RID: 1910
        long time;

        // Token: 0x04000777 RID: 1911
        double height;

        // Token: 0x04000778 RID: 1912
        int width;
    }
}
