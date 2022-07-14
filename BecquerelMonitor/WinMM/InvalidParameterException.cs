using System;
using System.Runtime.Serialization;

namespace WinMM
{
    // Token: 0x020001A2 RID: 418
    [Serializable]
    public class InvalidParameterException : MMSystemException
    {
        // Token: 0x06001500 RID: 5376 RVA: 0x0006B730 File Offset: 0x00069930
        public InvalidParameterException()
        {
        }

        // Token: 0x06001501 RID: 5377 RVA: 0x0006B738 File Offset: 0x00069938
        public InvalidParameterException(string message) : base(message)
        {
        }

        // Token: 0x06001502 RID: 5378 RVA: 0x0006B744 File Offset: 0x00069944
        public InvalidParameterException(string message, Exception inner) : base(message, inner)
        {
        }

        // Token: 0x06001503 RID: 5379 RVA: 0x0006B750 File Offset: 0x00069950
        protected InvalidParameterException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
