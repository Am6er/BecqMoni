using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001BC RID: 444
	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IMMDeviceEnumerator
	{
		// Token: 0x060015C6 RID: 5574
		[PreserveSig]
		int EnumAudioEndpoints(EDataFlow dataFlow, EDeviceState StateMask, out IMMDeviceCollection device);

		// Token: 0x060015C7 RID: 5575
		[PreserveSig]
		int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

		// Token: 0x060015C8 RID: 5576
		[PreserveSig]
		int GetDevice(string pwstrId, out IMMDevice ppDevice);

		// Token: 0x060015C9 RID: 5577
		[PreserveSig]
		int RegisterEndpointNotificationCallback(IntPtr pClient);

		// Token: 0x060015CA RID: 5578
		[PreserveSig]
		int UnregisterEndpointNotificationCallback(IntPtr pClient);
	}
}
