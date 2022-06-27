using System;

namespace CoreAudioApi.Interfaces
{
	// Token: 0x020001C1 RID: 449
	struct AUDIO_VOLUME_NOTIFICATION_DATA
	{
		// Token: 0x060015EE RID: 5614 RVA: 0x0006D3A8 File Offset: 0x0006B5A8
		void FixCS0649()
		{
			this.guidEventContext = Guid.Empty;
			this.bMuted = false;
			this.fMasterVolume = 0f;
			this.nChannels = 0u;
			this.ChannelVolume = 0f;
		}

		// Token: 0x04000C9A RID: 3226
		public Guid guidEventContext;

		// Token: 0x04000C9B RID: 3227
		public bool bMuted;

		// Token: 0x04000C9C RID: 3228
		public float fMasterVolume;

		// Token: 0x04000C9D RID: 3229
		public uint nChannels;

		// Token: 0x04000C9E RID: 3230
		public float ChannelVolume;
	}
}
