using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001D8 RID: 472
	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IMMDeviceCollection
	{
		// Token: 0x06001651 RID: 5713
		[PreserveSig]
		int GetCount(out uint pcDevices);

		// Token: 0x06001652 RID: 5714
		[PreserveSig]
		int Item(uint nDevice, out IMMDevice Device);
	}
}
