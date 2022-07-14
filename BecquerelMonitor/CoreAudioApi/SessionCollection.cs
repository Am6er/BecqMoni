using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001B3 RID: 435
    public class SessionCollection
    {
        // Token: 0x060015B6 RID: 5558 RVA: 0x0006D1E0 File Offset: 0x0006B3E0
        internal SessionCollection(IAudioSessionEnumerator realEnumerator)
        {
            this._AudioSessionEnumerator = realEnumerator;
        }

        // Token: 0x1700068E RID: 1678
        public AudioSessionControl this[int index]
        {
            get
            {
                IAudioSessionControl2 realAudioSessionControl;
                Marshal.ThrowExceptionForHR(this._AudioSessionEnumerator.GetSession(index, out realAudioSessionControl));
                return new AudioSessionControl(realAudioSessionControl);
            }
        }

        // Token: 0x1700068F RID: 1679
        // (get) Token: 0x060015B8 RID: 5560 RVA: 0x0006D21C File Offset: 0x0006B41C
        public int Count
        {
            get
            {
                int result;
                Marshal.ThrowExceptionForHR(this._AudioSessionEnumerator.GetCount(out result));
                return result;
            }
        }

        // Token: 0x04000C8B RID: 3211
        IAudioSessionEnumerator _AudioSessionEnumerator;
    }
}
