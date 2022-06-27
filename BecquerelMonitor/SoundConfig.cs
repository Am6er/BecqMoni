using System;

namespace BecquerelMonitor
{
	// Token: 0x02000130 RID: 304
	public class SoundConfig
	{
		// Token: 0x17000436 RID: 1078
		// (get) Token: 0x06000FD7 RID: 4055 RVA: 0x000578A0 File Offset: 0x00055AA0
		// (set) Token: 0x06000FD8 RID: 4056 RVA: 0x000578A8 File Offset: 0x00055AA8
		public string MeasurementCompletion
		{
			get
			{
				return this.measurementCompletion;
			}
			set
			{
				this.measurementCompletion = value;
			}
		}

		// Token: 0x04000935 RID: 2357
		string measurementCompletion = "";
	}
}
