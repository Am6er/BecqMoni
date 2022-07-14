using CoreAudioApi.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001CB RID: 459
    public class AudioEndpointVolumeChannel
    {
        // Token: 0x06001621 RID: 5665 RVA: 0x0006D894 File Offset: 0x0006BA94
        internal AudioEndpointVolumeChannel(IAudioEndpointVolume parent, int channel)
        {
            this._Channel = (uint)channel;
            this._AudioEndpointVolume = parent;
        }

        // Token: 0x170006AB RID: 1707
        // (get) Token: 0x06001622 RID: 5666 RVA: 0x0006D8AC File Offset: 0x0006BAAC
        // (set) Token: 0x06001623 RID: 5667 RVA: 0x0006D8D8 File Offset: 0x0006BAD8
        public float VolumeLevel
        {
            get
            {
                float result;
                Marshal.ThrowExceptionForHR(this._AudioEndpointVolume.GetChannelVolumeLevel(this._Channel, out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(this._AudioEndpointVolume.SetChannelVolumeLevel(this._Channel, value, Guid.Empty));
            }
        }

        // Token: 0x170006AC RID: 1708
        // (get) Token: 0x06001624 RID: 5668 RVA: 0x0006D8F8 File Offset: 0x0006BAF8
        // (set) Token: 0x06001625 RID: 5669 RVA: 0x0006D924 File Offset: 0x0006BB24
        public float VolumeLevelScalar
        {
            get
            {
                float result;
                Marshal.ThrowExceptionForHR(this._AudioEndpointVolume.GetChannelVolumeLevelScalar(this._Channel, out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(this._AudioEndpointVolume.SetChannelVolumeLevelScalar(this._Channel, value, Guid.Empty));
            }
        }

        // Token: 0x04000CAF RID: 3247
        uint _Channel;

        // Token: 0x04000CB0 RID: 3248
        IAudioEndpointVolume _AudioEndpointVolume;
    }
}
