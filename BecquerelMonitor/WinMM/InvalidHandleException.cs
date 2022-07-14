using System;
using System.Runtime.Serialization;

namespace WinMM
{
    // Token: 0x020001A5 RID: 421
    [Serializable]
    public class InvalidHandleException : Exception
    {
        // Token: 0x06001526 RID: 5414 RVA: 0x0006B87C File Offset: 0x00069A7C
        public InvalidHandleException()
        {
        }

        // Token: 0x06001527 RID: 5415 RVA: 0x0006B884 File Offset: 0x00069A84
        public InvalidHandleException(string message) : base(message)
        {
        }

        // Token: 0x06001528 RID: 5416 RVA: 0x0006B890 File Offset: 0x00069A90
        public InvalidHandleException(string message, Exception inner) : base(message, inner)
        {
        }

        // Token: 0x06001529 RID: 5417 RVA: 0x0006B89C File Offset: 0x00069A9C
        protected InvalidHandleException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
