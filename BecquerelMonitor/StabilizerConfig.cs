using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
	// Token: 0x0200007A RID: 122
	public class StabilizerConfig
	{
		// Token: 0x170001D6 RID: 470
		// (get) Token: 0x0600061A RID: 1562 RVA: 0x000266C8 File Offset: 0x000248C8
		// (set) Token: 0x0600061B RID: 1563 RVA: 0x000266D0 File Offset: 0x000248D0
		public List<TargetPeak> TargetPeaks
		{
			get
			{
				return this.targetPeaks;
			}
			set
			{
				this.targetPeaks = value;
			}
		}

		// Token: 0x0600061C RID: 1564 RVA: 0x000266DC File Offset: 0x000248DC
		public StabilizerConfig()
		{
		}

		// Token: 0x0600061D RID: 1565 RVA: 0x000266F0 File Offset: 0x000248F0
		public StabilizerConfig(StabilizerConfig config)
		{
			this.targetPeaks.Clear();
			foreach (TargetPeak targetPeak in config.targetPeaks)
			{
				TargetPeak targetPeak2 = new TargetPeak();
				targetPeak2.Nuclide = string.Copy(targetPeak.Nuclide);
				targetPeak2.Energy = targetPeak.Energy;
				targetPeak2.Error = targetPeak.Error;
				this.targetPeaks.Add(targetPeak2);
			}
		}

		// Token: 0x0600061E RID: 1566 RVA: 0x0002679C File Offset: 0x0002499C
		public StabilizerConfig Clone()
		{
			return new StabilizerConfig(this);
		}

		// Token: 0x04000339 RID: 825
		List<TargetPeak> targetPeaks = new List<TargetPeak>();
	}
}
