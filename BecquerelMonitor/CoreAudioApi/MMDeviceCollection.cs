using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001C4 RID: 452
    public class MMDeviceCollection
    {
        // Token: 0x1700069B RID: 1691
        // (get) Token: 0x060015F9 RID: 5625 RVA: 0x0006D5BC File Offset: 0x0006B7BC
        public int Count
        {
            get
            {
                uint result;
                Marshal.ThrowExceptionForHR(this._MMDeviceCollection.GetCount(out result));
                return (int)result;
            }
        }

        // Token: 0x1700069C RID: 1692
        public MMDevice this[int index]
        {
            get
            {
                IMMDevice realDevice;
                this._MMDeviceCollection.Item((uint)index, out realDevice);
                return new MMDevice(realDevice);
            }
        }

        // Token: 0x060015FB RID: 5627 RVA: 0x0006D608 File Offset: 0x0006B808
        internal MMDeviceCollection(IMMDeviceCollection parent)
        {
            this._MMDeviceCollection = parent;
        }

        // Token: 0x04000CA1 RID: 3233
        IMMDeviceCollection _MMDeviceCollection;
    }
}
