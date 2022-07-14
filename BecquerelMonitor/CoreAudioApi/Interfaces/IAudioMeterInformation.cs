using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001C9 RID: 457
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        // Token: 0x06001619 RID: 5657
        [PreserveSig]
        int GetPeakValue(out float pfPeak);

        // Token: 0x0600161A RID: 5658
        [PreserveSig]
        int GetMeteringChannelCount(out int pnChannelCount);

        // Token: 0x0600161B RID: 5659
        [PreserveSig]
        int GetChannelsPeakValues(int u32ChannelCount, [In] IntPtr afPeakValues);

        // Token: 0x0600161C RID: 5660
        [PreserveSig]
        int QueryHardwareSupport(out int pdwHardwareSupportMask);
    }
}
