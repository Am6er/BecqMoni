using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinMM
{
    // Token: 0x020001A9 RID: 425
    public sealed class PlaySound
    {
        // Token: 0x06001564 RID: 5476 RVA: 0x0006C1B4 File Offset: 0x0006A3B4
        PlaySound()
        {
        }

        // Token: 0x06001565 RID: 5477 RVA: 0x0006C1BC File Offset: 0x0006A3BC
        public static void PlaySystemSound(string systemSoundName)
        {
            if (NativeMethods.PlaySound(systemSoundName, (IntPtr)0, NativeMethods.PLAYSOUNDFLAGS.SND_ASYNC | NativeMethods.PLAYSOUNDFLAGS.SND_NODEFAULT | NativeMethods.PLAYSOUNDFLAGS.SND_PURGE | NativeMethods.PLAYSOUNDFLAGS.SND_ALIAS) == 0u)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        // Token: 0x06001566 RID: 5478 RVA: 0x0006C1F0 File Offset: 0x0006A3F0
        public static void StopAllSounds()
        {
            if (NativeMethods.PlaySound(null, (IntPtr)0, NativeMethods.PLAYSOUNDFLAGS.SND_PURGE) == 0u)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        // Token: 0x06001567 RID: 5479 RVA: 0x0006C224 File Offset: 0x0006A424
        public static void PlaySoundFile(string soundFileName)
        {
            if (NativeMethods.PlaySound(soundFileName, (IntPtr)0, NativeMethods.PLAYSOUNDFLAGS.SND_ASYNC | NativeMethods.PLAYSOUNDFLAGS.SND_NODEFAULT | NativeMethods.PLAYSOUNDFLAGS.SND_PURGE | NativeMethods.PLAYSOUNDFLAGS.SND_FILENAME) == 0u)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }
}
