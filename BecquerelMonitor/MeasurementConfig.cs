using System;

namespace BecquerelMonitor
{
	// Token: 0x0200011E RID: 286
	public class MeasurementConfig
	{
		// Token: 0x170003F2 RID: 1010
		// (get) Token: 0x06000F44 RID: 3908 RVA: 0x00056E80 File Offset: 0x00055080
		// (set) Token: 0x06000F45 RID: 3909 RVA: 0x00056E88 File Offset: 0x00055088
		public VolumeUnit VolumeUnit
		{
			get
			{
				return this.volumeUnit;
			}
			set
			{
				this.volumeUnit = value;
			}
		}

		// Token: 0x170003F3 RID: 1011
		// (get) Token: 0x06000F46 RID: 3910 RVA: 0x00056E94 File Offset: 0x00055094
		// (set) Token: 0x06000F47 RID: 3911 RVA: 0x00056E9C File Offset: 0x0005509C
		public WeightUnit WeightUnit
		{
			get
			{
				return this.weightUnit;
			}
			set
			{
				this.weightUnit = value;
			}
		}

		// Token: 0x170003F4 RID: 1012
		// (get) Token: 0x06000F48 RID: 3912 RVA: 0x00056EA8 File Offset: 0x000550A8
		// (set) Token: 0x06000F49 RID: 3913 RVA: 0x00056EB0 File Offset: 0x000550B0
		public decimal ErrorLevel
		{
			get
			{
				return this.errorLevel;
			}
			set
			{
				this.errorLevel = value;
			}
		}

		// Token: 0x170003F5 RID: 1013
		// (get) Token: 0x06000F4A RID: 3914 RVA: 0x00056EBC File Offset: 0x000550BC
		// (set) Token: 0x06000F4B RID: 3915 RVA: 0x00056EC4 File Offset: 0x000550C4
		public decimal DetectionLevel
		{
			get
			{
				return this.detectionLevel;
			}
			set
			{
				this.detectionLevel = value;
			}
		}

		// Token: 0x170003F6 RID: 1014
		// (get) Token: 0x06000F4C RID: 3916 RVA: 0x00056ED0 File Offset: 0x000550D0
		// (set) Token: 0x06000F4D RID: 3917 RVA: 0x00056ED8 File Offset: 0x000550D8
		public int DetectionCondition
		{
			get
			{
				return this.detectionCondition;
			}
			set
			{
				this.detectionCondition = value;
			}
		}

		// Token: 0x170003F7 RID: 1015
		// (get) Token: 0x06000F4E RID: 3918 RVA: 0x00056EE4 File Offset: 0x000550E4
		// (set) Token: 0x06000F4F RID: 3919 RVA: 0x00056EEC File Offset: 0x000550EC
		public bool ShowValuesForNDResult
		{
			get
			{
				return this.showValuesForNDResult;
			}
			set
			{
				this.showValuesForNDResult = value;
			}
		}

		// Token: 0x040008C6 RID: 2246
		VolumeUnit volumeUnit = VolumeUnit.Milliliter;

		// Token: 0x040008C7 RID: 2247
		WeightUnit weightUnit = WeightUnit.Gram;

		// Token: 0x040008C8 RID: 2248
		decimal errorLevel = 1.0m;

		// Token: 0x040008C9 RID: 2249
		decimal detectionLevel = 3.0m;

		// Token: 0x040008CA RID: 2250
		int detectionCondition;

		// Token: 0x040008CB RID: 2251
		bool showValuesForNDResult;
	}
}
