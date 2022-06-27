using System;
using System.Runtime.Serialization;

namespace WinMM
{
	// Token: 0x0200019D RID: 413
	[Serializable]
	public class MMSystemException : Exception
	{
		// Token: 0x060014D5 RID: 5333 RVA: 0x0006AA58 File Offset: 0x00068C58
		public MMSystemException()
		{
		}

		// Token: 0x060014D6 RID: 5334 RVA: 0x0006AA60 File Offset: 0x00068C60
		public MMSystemException(string message) : base(message)
		{
		}

		// Token: 0x060014D7 RID: 5335 RVA: 0x0006AA6C File Offset: 0x00068C6C
		public MMSystemException(string message, Exception inner) : base(message, inner)
		{
		}

		// Token: 0x060014D8 RID: 5336 RVA: 0x0006AA78 File Offset: 0x00068C78
		protected MMSystemException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}
}
