using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001BD RID: 445
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	interface IMMDevice
	{
		// Token: 0x060015CB RID: 5579
		[PreserveSig]
		int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

		// Token: 0x060015CC RID: 5580
		[PreserveSig]
		int OpenPropertyStore(EStgmAccess stgmAccess, out IPropertyStore propertyStore);

		// Token: 0x060015CD RID: 5581
		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

		// Token: 0x060015CE RID: 5582
		[PreserveSig]
		int GetState(out EDeviceState pdwState);
	}
}
