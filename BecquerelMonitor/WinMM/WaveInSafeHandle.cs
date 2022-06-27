using System;
using Microsoft.Win32.SafeHandles;

namespace WinMM
{
	// Token: 0x020001A1 RID: 417
	sealed class WaveInSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		// Token: 0x060014FD RID: 5373 RVA: 0x0006B6E8 File Offset: 0x000698E8
		public WaveInSafeHandle() : base(true)
		{
		}

		// Token: 0x060014FE RID: 5374 RVA: 0x0006B6F4 File Offset: 0x000698F4
		public WaveInSafeHandle(IntPtr tempHandle) : base(true)
		{
			this.handle = tempHandle;
		}

		// Token: 0x060014FF RID: 5375 RVA: 0x0006B704 File Offset: 0x00069904
		protected override bool ReleaseHandle()
		{
			if (!base.IsClosed)
			{
				NativeMethods.MMSYSERROR mmsyserror = NativeMethods.waveInClose(this);
				return mmsyserror == NativeMethods.MMSYSERROR.MMSYSERR_NOERROR;
			}
			return true;
		}
	}
}
