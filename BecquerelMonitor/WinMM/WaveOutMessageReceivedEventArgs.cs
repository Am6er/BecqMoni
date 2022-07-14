using System;

namespace WinMM
{
    // Token: 0x020001A0 RID: 416
    public sealed class WaveOutMessageReceivedEventArgs : EventArgs
    {
        // Token: 0x060014FB RID: 5371 RVA: 0x0006B6D0 File Offset: 0x000698D0
        public WaveOutMessageReceivedEventArgs(WaveOutMessage message)
        {
            this.message = message;
        }

        // Token: 0x17000641 RID: 1601
        // (get) Token: 0x060014FC RID: 5372 RVA: 0x0006B6E0 File Offset: 0x000698E0
        public WaveOutMessage Message
        {
            get
            {
                return this.message;
            }
        }

        // Token: 0x04000C37 RID: 3127
        WaveOutMessage message;
    }
}
