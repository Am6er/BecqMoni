using System;
using System.Runtime.InteropServices;
using CoreAudioApi.Interfaces;

namespace CoreAudioApi
{
	// Token: 0x020001DE RID: 478
	public class SimpleAudioVolume
	{
		// Token: 0x0600165C RID: 5724 RVA: 0x0006DEAC File Offset: 0x0006C0AC
		internal SimpleAudioVolume(ISimpleAudioVolume realSimpleVolume)
		{
			this._SimpleAudioVolume = realSimpleVolume;
		}

		// Token: 0x170006BC RID: 1724
		// (get) Token: 0x0600165D RID: 5725 RVA: 0x0006DEBC File Offset: 0x0006C0BC
		// (set) Token: 0x0600165E RID: 5726 RVA: 0x0006DEE0 File Offset: 0x0006C0E0
		public float MasterVolume
		{
			get
			{
				float result;
				Marshal.ThrowExceptionForHR(this._SimpleAudioVolume.GetMasterVolume(out result));
				return result;
			}
			set
			{
				Guid empty = Guid.Empty;
				Marshal.ThrowExceptionForHR(this._SimpleAudioVolume.SetMasterVolume(value, ref empty));
			}
		}

		// Token: 0x170006BD RID: 1725
		// (get) Token: 0x0600165F RID: 5727 RVA: 0x0006DF0C File Offset: 0x0006C10C
		// (set) Token: 0x06001660 RID: 5728 RVA: 0x0006DF30 File Offset: 0x0006C130
		public bool Mute
		{
			get
			{
				bool result;
				Marshal.ThrowExceptionForHR(this._SimpleAudioVolume.GetMute(out result));
				return result;
			}
			set
			{
				Guid empty = Guid.Empty;
				Marshal.ThrowExceptionForHR(this._SimpleAudioVolume.SetMute(value, ref empty));
			}
		}

		// Token: 0x04000D03 RID: 3331
		ISimpleAudioVolume _SimpleAudioVolume;
	}
}
