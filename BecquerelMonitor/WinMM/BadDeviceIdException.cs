using System;
using System.Runtime.Serialization;

namespace WinMM
{
	// Token: 0x020001A7 RID: 423
	[Serializable]
	public class BadDeviceIdException : MMSystemException
	{
		// Token: 0x06001534 RID: 5428 RVA: 0x0006B984 File Offset: 0x00069B84
		public BadDeviceIdException()
		{
		}

		// Token: 0x06001535 RID: 5429 RVA: 0x0006B98C File Offset: 0x00069B8C
		public BadDeviceIdException(string message) : base(message)
		{
		}

		// Token: 0x06001536 RID: 5430 RVA: 0x0006B998 File Offset: 0x00069B98
		public BadDeviceIdException(string message, Exception inner) : base(message, inner)
		{
		}

		// Token: 0x06001537 RID: 5431 RVA: 0x0006B9A4 File Offset: 0x00069BA4
		protected BadDeviceIdException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}
}
