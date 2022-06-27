using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001D1 RID: 465
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
	interface ISimpleAudioVolume
	{
		// Token: 0x0600163D RID: 5693
		[PreserveSig]
		int SetMasterVolume(float fLevel, ref Guid EventContext);

		// Token: 0x0600163E RID: 5694
		[PreserveSig]
		int GetMasterVolume(out float pfLevel);

		// Token: 0x0600163F RID: 5695
		[PreserveSig]
		int SetMute(bool bMute, ref Guid EventContext);

		// Token: 0x06001640 RID: 5696
		[PreserveSig]
		int GetMute(out bool bMute);
	}
}
