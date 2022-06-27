using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001D2 RID: 466
	[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IAudioSessionManager2
	{
		// Token: 0x06001641 RID: 5697
		[PreserveSig]
		int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, IntPtr ISessionControl);

		// Token: 0x06001642 RID: 5698
		[PreserveSig]
		int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, IntPtr SimpleAudioVolume);

		// Token: 0x06001643 RID: 5699
		[PreserveSig]
		int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

		// Token: 0x06001644 RID: 5700
		[PreserveSig]
		int RegisterSessionNotification(IntPtr IAudioSessionNotification);

		// Token: 0x06001645 RID: 5701
		[PreserveSig]
		int UnregisterSessionNotification(IntPtr IAudioSessionNotification);

		// Token: 0x06001646 RID: 5702
		[PreserveSig]
		int RegisterDuckNotification(string sessionID, IntPtr IAudioVolumeDuckNotification);

		// Token: 0x06001647 RID: 5703
		[PreserveSig]
		int UnregisterDuckNotification(IntPtr IAudioVolumeDuckNotification);
	}
}
