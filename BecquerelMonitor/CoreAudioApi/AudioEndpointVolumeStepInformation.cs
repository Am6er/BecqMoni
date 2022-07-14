using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    // Token: 0x020001DC RID: 476
    public class AudioEndpointVolumeStepInformation
    {
        // Token: 0x06001657 RID: 5719 RVA: 0x0006DE7C File Offset: 0x0006C07C
        internal AudioEndpointVolumeStepInformation(IAudioEndpointVolume parent)
        {
            Marshal.ThrowExceptionForHR(parent.GetVolumeStepInfo(out this._Step, out this._StepCount));
        }

        // Token: 0x170006BA RID: 1722
        // (get) Token: 0x06001658 RID: 5720 RVA: 0x0006DE9C File Offset: 0x0006C09C
        public uint Step
        {
            get
            {
                return this._Step;
            }
        }

        // Token: 0x170006BB RID: 1723
        // (get) Token: 0x06001659 RID: 5721 RVA: 0x0006DEA4 File Offset: 0x0006C0A4
        public uint StepCount
        {
            get
            {
                return this._StepCount;
            }
        }

        // Token: 0x04000D01 RID: 3329
        uint _Step;

        // Token: 0x04000D02 RID: 3330
        uint _StepCount;
    }
}
