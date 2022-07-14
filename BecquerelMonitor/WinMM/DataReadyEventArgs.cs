using System;

namespace WinMM
{
    // Token: 0x020001A3 RID: 419
    public class DataReadyEventArgs : EventArgs
    {
        // Token: 0x06001504 RID: 5380 RVA: 0x0006B75C File Offset: 0x0006995C
        public DataReadyEventArgs(byte[] data)
        {
            this.data = data;
        }

        // Token: 0x17000642 RID: 1602
        // (get) Token: 0x06001505 RID: 5381 RVA: 0x0006B76C File Offset: 0x0006996C
        public byte[] Data
        {
            get
            {
                return this.data;
            }
        }

        // Token: 0x04000C38 RID: 3128
        byte[] data;
    }
}
