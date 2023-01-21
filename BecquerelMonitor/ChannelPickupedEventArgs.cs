using System;

namespace BecquerelMonitor
{
    // Token: 0x020000C3 RID: 195
    public class ChannelPickupedEventArgs : EventArgs
    {
        // Token: 0x17000294 RID: 660
        // (get) Token: 0x0600096B RID: 2411 RVA: 0x000373B4 File Offset: 0x000355B4
        // (set) Token: 0x0600096C RID: 2412 RVA: 0x000373BC File Offset: 0x000355BC
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

        public int Count
        {
            get
            {
                return this.count;
            }
            set
            {
                this.count = value;
            }
        }

        // Token: 0x0600096D RID: 2413 RVA: 0x000373C8 File Offset: 0x000355C8
        public ChannelPickupedEventArgs(int channel, int count)
        {
            this.channel = channel;
            this.count = count;
        }

        // Token: 0x04000542 RID: 1346
        int channel;

        int count;
    }
}
