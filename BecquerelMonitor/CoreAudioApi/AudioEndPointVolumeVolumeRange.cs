using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001CA RID: 458
    public class AudioEndPointVolumeVolumeRange
    {
        // Token: 0x0600161D RID: 5661 RVA: 0x0006D854 File Offset: 0x0006BA54
        internal AudioEndPointVolumeVolumeRange(IAudioEndpointVolume parent)
        {
            Marshal.ThrowExceptionForHR(parent.GetVolumeRange(out this._VolumeMindB, out this._VolumeMaxdB, out this._VolumeIncrementdB));
        }

        // Token: 0x170006A8 RID: 1704
        // (get) Token: 0x0600161E RID: 5662 RVA: 0x0006D87C File Offset: 0x0006BA7C
        public float MindB
        {
            get
            {
                return this._VolumeMindB;
            }
        }

        // Token: 0x170006A9 RID: 1705
        // (get) Token: 0x0600161F RID: 5663 RVA: 0x0006D884 File Offset: 0x0006BA84
        public float MaxdB
        {
            get
            {
                return this._VolumeMaxdB;
            }
        }

        // Token: 0x170006AA RID: 1706
        // (get) Token: 0x06001620 RID: 5664 RVA: 0x0006D88C File Offset: 0x0006BA8C
        public float IncrementdB
        {
            get
            {
                return this._VolumeIncrementdB;
            }
        }

        // Token: 0x04000CAC RID: 3244
        float _VolumeMindB;

        // Token: 0x04000CAD RID: 3245
        float _VolumeMaxdB;

        // Token: 0x04000CAE RID: 3246
        float _VolumeIncrementdB;
    }
}
