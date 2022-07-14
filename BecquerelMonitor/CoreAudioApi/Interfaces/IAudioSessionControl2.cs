using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001C5 RID: 453
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // Token: 0x060015FC RID: 5628
        [PreserveSig]
        int GetState(out AudioSessionState state);

        // Token: 0x060015FD RID: 5629
        [PreserveSig]
        int GetDisplayName(out IntPtr name);

        // Token: 0x060015FE RID: 5630
        [PreserveSig]
        int SetDisplayName(string value, Guid EventContext);

        // Token: 0x060015FF RID: 5631
        [PreserveSig]
        int GetIconPath(out IntPtr Path);

        // Token: 0x06001600 RID: 5632
        [PreserveSig]
        int SetIconPath(string Value, Guid EventContext);

        // Token: 0x06001601 RID: 5633
        [PreserveSig]
        int GetGroupingParam(out Guid GroupingParam);

        // Token: 0x06001602 RID: 5634
        [PreserveSig]
        int SetGroupingParam(Guid Override, Guid Eventcontext);

        // Token: 0x06001603 RID: 5635
        [PreserveSig]
        int RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

        // Token: 0x06001604 RID: 5636
        [PreserveSig]
        int UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

        // Token: 0x06001605 RID: 5637
        [PreserveSig]
        int GetSessionIdentifier(out IntPtr retVal);

        // Token: 0x06001606 RID: 5638
        [PreserveSig]
        int GetSessionInstanceIdentifier(out IntPtr retVal);

        // Token: 0x06001607 RID: 5639
        [PreserveSig]
        int GetProcessId(out uint retvVal);

        // Token: 0x06001608 RID: 5640
        [PreserveSig]
        int IsSystemSoundsSession();

        // Token: 0x06001609 RID: 5641
        [PreserveSig]
        int SetDuckingPreference(bool optOut);
    }
}
