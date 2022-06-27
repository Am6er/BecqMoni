using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001BB RID: 443
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
	interface IMMEndpoint
	{
		// Token: 0x060015C5 RID: 5573
		[PreserveSig]
		int GetDataFlow(out EDataFlow pDataFlow);
	}
}
