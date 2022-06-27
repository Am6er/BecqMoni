using System;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001E0 RID: 480
	struct Blob
	{
		// Token: 0x06001662 RID: 5730 RVA: 0x0006E1CC File Offset: 0x0006C3CC
		void FixCS0649()
		{
			this.Length = 0;
			this.Data = IntPtr.Zero;
		}

		// Token: 0x04000D0F RID: 3343
		public int Length;

		// Token: 0x04000D10 RID: 3344
		public IntPtr Data;
	}
}
