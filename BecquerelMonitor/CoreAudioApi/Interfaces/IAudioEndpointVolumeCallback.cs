using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001B7 RID: 439
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        // Token: 0x060015BD RID: 5565
        [PreserveSig]
        int OnNotify(IntPtr pNotifyData);
    }
}
