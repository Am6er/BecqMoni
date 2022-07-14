using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001B5 RID: 437
    public class AudioSessionManager
    {
        // Token: 0x060015BB RID: 5563 RVA: 0x0006D240 File Offset: 0x0006B440
        internal AudioSessionManager(IAudioSessionManager2 realAudioSessionManager)
        {
            this._AudioSessionManager = realAudioSessionManager;
            IAudioSessionEnumerator realEnumerator;
            Marshal.ThrowExceptionForHR(this._AudioSessionManager.GetSessionEnumerator(out realEnumerator));
            this._Sessions = new SessionCollection(realEnumerator);
        }

        // Token: 0x17000690 RID: 1680
        // (get) Token: 0x060015BC RID: 5564 RVA: 0x0006D27C File Offset: 0x0006B47C
        public SessionCollection Sessions
        {
            get
            {
                return this._Sessions;
            }
        }

        // Token: 0x04000C8C RID: 3212
        IAudioSessionManager2 _AudioSessionManager;

        // Token: 0x04000C8D RID: 3213
        SessionCollection _Sessions;
    }
}
