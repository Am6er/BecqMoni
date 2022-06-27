using System;
using System.Runtime.InteropServices;
using CoreAudioApi.Interfaces;

namespace CoreAudioApi
{
	// Token: 0x020001D9 RID: 473
	public class AudioMeterInformation
	{
		// Token: 0x06001653 RID: 5715 RVA: 0x0006DE00 File Offset: 0x0006C000
		internal AudioMeterInformation(IAudioMeterInformation realInterface)
		{
			this._AudioMeterInformation = realInterface;
			int hardwareSupport;
			Marshal.ThrowExceptionForHR(this._AudioMeterInformation.QueryHardwareSupport(out hardwareSupport));
			this._HardwareSupport = (EEndpointHardwareSupport)hardwareSupport;
			this._Channels = new AudioMeterInformationChannels(this._AudioMeterInformation);
		}

		// Token: 0x170006B7 RID: 1719
		// (get) Token: 0x06001654 RID: 5716 RVA: 0x0006DE48 File Offset: 0x0006C048
		public AudioMeterInformationChannels PeakValues
		{
			get
			{
				return this._Channels;
			}
		}

		// Token: 0x170006B8 RID: 1720
		// (get) Token: 0x06001655 RID: 5717 RVA: 0x0006DE50 File Offset: 0x0006C050
		public EEndpointHardwareSupport HardwareSupport
		{
			get
			{
				return this._HardwareSupport;
			}
		}

		// Token: 0x170006B9 RID: 1721
		// (get) Token: 0x06001656 RID: 5718 RVA: 0x0006DE58 File Offset: 0x0006C058
		public float MasterPeakValue
		{
			get
			{
				float result;
				Marshal.ThrowExceptionForHR(this._AudioMeterInformation.GetPeakValue(out result));
				return result;
			}
		}

		// Token: 0x04000CE4 RID: 3300
		IAudioMeterInformation _AudioMeterInformation;

		// Token: 0x04000CE5 RID: 3301
		EEndpointHardwareSupport _HardwareSupport;

		// Token: 0x04000CE6 RID: 3302
		AudioMeterInformationChannels _Channels;
	}
}
