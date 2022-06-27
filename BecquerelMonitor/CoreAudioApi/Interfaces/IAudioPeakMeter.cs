using System;
using System.Runtime.InteropServices;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001B4 RID: 436
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("DD79923C-0599-45e0-B8B6-C8DF7DB6E796")]
	interface IAudioPeakMeter
	{
		// Token: 0x060015B9 RID: 5561
		int GetChannelCount(out int pcChannels);

		// Token: 0x060015BA RID: 5562
		int GetLevel(int Channel, out float level);
	}
}
