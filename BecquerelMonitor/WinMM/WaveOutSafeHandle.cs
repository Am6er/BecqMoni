using Microsoft.Win32.SafeHandles;
using System;

namespace WinMM
{
    // Token: 0x0200019F RID: 415
    sealed class WaveOutSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        // Token: 0x060014F8 RID: 5368 RVA: 0x0006B688 File Offset: 0x00069888
        public WaveOutSafeHandle() : base(true)
        {
        }

        // Token: 0x060014F9 RID: 5369 RVA: 0x0006B694 File Offset: 0x00069894
        public WaveOutSafeHandle(IntPtr tempHandle) : base(true)
        {
            this.handle = tempHandle;
        }

        // Token: 0x060014FA RID: 5370 RVA: 0x0006B6A4 File Offset: 0x000698A4
        protected override bool ReleaseHandle()
        {
            if (!base.IsClosed)
            {
                NativeMethods.MMSYSERROR mmsyserror = NativeMethods.waveOutClose(this);
                return mmsyserror == NativeMethods.MMSYSERROR.MMSYSERR_NOERROR;
            }
            return true;
        }
    }
}
