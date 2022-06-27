using System;

namespace CoreAudioApi
{
	// Token: 0x020001C7 RID: 455
	[Flags]
	public enum EDeviceState : uint
	{
		// Token: 0x04000CA6 RID: 3238
		DEVICE_STATE_ACTIVE = 1u,
		// Token: 0x04000CA7 RID: 3239
		DEVICE_STATE_UNPLUGGED = 2u,
		// Token: 0x04000CA8 RID: 3240
		DEVICE_STATE_NOTPRESENT = 4u,
		// Token: 0x04000CA9 RID: 3241
		DEVICE_STATEMASK_ALL = 7u
	}
}
