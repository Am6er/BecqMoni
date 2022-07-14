using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001BE RID: 446
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
    public interface IAudioSessionEvents
    {
        // Token: 0x060015CF RID: 5583
        [PreserveSig]
        int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string NewDisplayName, Guid EventContext);

        // Token: 0x060015D0 RID: 5584
        [PreserveSig]
        int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string NewIconPath, Guid EventContext);

        // Token: 0x060015D1 RID: 5585
        [PreserveSig]
        int OnSimpleVolumeChanged(float NewVolume, bool newMute, Guid EventContext);

        // Token: 0x060015D2 RID: 5586
        [PreserveSig]
        int OnChannelVolumeChanged(uint ChannelCount, IntPtr NewChannelVolumeArray, uint ChangedChannel, Guid EventContext);

        // Token: 0x060015D3 RID: 5587
        [PreserveSig]
        int OnGroupingParamChanged(Guid NewGroupingParam, Guid EventContext);

        // Token: 0x060015D4 RID: 5588
        [PreserveSig]
        int OnStateChanged(AudioSessionState NewState);

        // Token: 0x060015D5 RID: 5589
        [PreserveSig]
        int OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason);
    }
}
