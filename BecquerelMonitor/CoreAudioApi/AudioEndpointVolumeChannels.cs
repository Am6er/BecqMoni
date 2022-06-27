using System;
using System.Runtime.InteropServices;
using CoreAudioApi.Interfaces;

namespace CoreAudioApi
{
	// Token: 0x020001C8 RID: 456
	public class AudioEndpointVolumeChannels
	{
		// Token: 0x170006A6 RID: 1702
		// (get) Token: 0x06001616 RID: 5654 RVA: 0x0006D7C8 File Offset: 0x0006B9C8
		public int Count
		{
			get
			{
				int result;
				Marshal.ThrowExceptionForHR(this._AudioEndPointVolume.GetChannelCount(out result));
				return result;
			}
		}

		// Token: 0x170006A7 RID: 1703
		public AudioEndpointVolumeChannel this[int index]
		{
			get
			{
				return this._Channels[index];
			}
		}

		// Token: 0x06001618 RID: 5656 RVA: 0x0006D7FC File Offset: 0x0006B9FC
		internal AudioEndpointVolumeChannels(IAudioEndpointVolume parent)
		{
			this._AudioEndPointVolume = parent;
			int count = this.Count;
			this._Channels = new AudioEndpointVolumeChannel[count];
			for (int i = 0; i < count; i++)
			{
				this._Channels[i] = new AudioEndpointVolumeChannel(this._AudioEndPointVolume, i);
			}
		}

		// Token: 0x04000CAA RID: 3242
		IAudioEndpointVolume _AudioEndPointVolume;

		// Token: 0x04000CAB RID: 3243
		AudioEndpointVolumeChannel[] _Channels;
	}
}
