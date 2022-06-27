using System;
using System.Runtime.InteropServices;
using CoreAudioApi.Interfaces;

namespace CoreAudioApi
{
	// Token: 0x020001D7 RID: 471
	public class MMDeviceEnumerator
	{
		// Token: 0x0600164D RID: 5709 RVA: 0x0006DD40 File Offset: 0x0006BF40
		public MMDeviceCollection EnumerateAudioEndPoints(EDataFlow dataFlow, EDeviceState dwStateMask)
		{
			IMMDeviceCollection parent;
			Marshal.ThrowExceptionForHR(this._realEnumerator.EnumAudioEndpoints(dataFlow, dwStateMask, out parent));
			return new MMDeviceCollection(parent);
		}

		// Token: 0x0600164E RID: 5710 RVA: 0x0006DD6C File Offset: 0x0006BF6C
		public MMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role)
		{
			IMMDevice realDevice = null;
			Marshal.ThrowExceptionForHR(this._realEnumerator.GetDefaultAudioEndpoint(dataFlow, role, out realDevice));
			return new MMDevice(realDevice);
		}

		// Token: 0x0600164F RID: 5711 RVA: 0x0006DD9C File Offset: 0x0006BF9C
		public MMDevice GetDevice(string ID)
		{
			IMMDevice realDevice = null;
			Marshal.ThrowExceptionForHR(this._realEnumerator.GetDevice(ID, out realDevice));
			return new MMDevice(realDevice);
		}

		// Token: 0x06001650 RID: 5712 RVA: 0x0006DDC8 File Offset: 0x0006BFC8
		public MMDeviceEnumerator()
		{
			if (Environment.OSVersion.Version.Major < 6)
			{
				throw new NotSupportedException("This functionality is only supported on Windows Vista or newer.");
			}
		}

		// Token: 0x04000CE3 RID: 3299
		IMMDeviceEnumerator _realEnumerator = new _MMDeviceEnumerator() as IMMDeviceEnumerator;
	}
}
