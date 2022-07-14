using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
    // Token: 0x020001DD RID: 477
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        // Token: 0x0600165A RID: 5722
        int GetCount(out int SessionCount);

        // Token: 0x0600165B RID: 5723
        int GetSession(int SessionCount, out IAudioSessionControl2 Session);
    }
}
