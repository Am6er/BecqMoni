using System;
using System.Runtime.InteropServices;
using CoreAudioApi.Interfaces;

namespace CoreAudioApi
{
	// Token: 0x020001C2 RID: 450
	public class AudioMeterInformationChannels
	{
		// Token: 0x17000696 RID: 1686
		// (get) Token: 0x060015EF RID: 5615 RVA: 0x0006D3DC File Offset: 0x0006B5DC
		public int Count
		{
			get
			{
				int result;
				Marshal.ThrowExceptionForHR(this._AudioMeterInformation.GetMeteringChannelCount(out result));
				return result;
			}
		}

		// Token: 0x17000697 RID: 1687
		public float this[int index]
		{
			get
			{
				float[] array = new float[this.Count];
				GCHandle gchandle = GCHandle.Alloc(array, GCHandleType.Pinned);
				Marshal.ThrowExceptionForHR(this._AudioMeterInformation.GetChannelsPeakValues(array.Length, gchandle.AddrOfPinnedObject()));
				gchandle.Free();
				return array[index];
			}
		}

		// Token: 0x060015F1 RID: 5617 RVA: 0x0006D44C File Offset: 0x0006B64C
		internal AudioMeterInformationChannels(IAudioMeterInformation parent)
		{
			this._AudioMeterInformation = parent;
		}

		// Token: 0x04000C9F RID: 3231
		IAudioMeterInformation _AudioMeterInformation;
	}
}
