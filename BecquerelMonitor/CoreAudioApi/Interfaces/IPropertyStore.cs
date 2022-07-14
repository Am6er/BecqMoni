using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001BA RID: 442
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        // Token: 0x060015C0 RID: 5568
        [PreserveSig]
        int GetCount(out int count);

        // Token: 0x060015C1 RID: 5569
        [PreserveSig]
        int GetAt(int iProp, out PropertyKey pkey);

        // Token: 0x060015C2 RID: 5570
        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant pv);

        // Token: 0x060015C3 RID: 5571
        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant propvar);

        // Token: 0x060015C4 RID: 5572
        [PreserveSig]
        int Commit();
    }
}
