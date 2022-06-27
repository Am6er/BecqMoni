using System;

namespace BecquerelMonitor
{
	// Token: 0x0200005F RID: 95
	public class EnergyCalibrationChangedEventArgs : EventArgs
	{
		// Token: 0x1700017E RID: 382
		// (get) Token: 0x06000482 RID: 1154 RVA: 0x00015C00 File Offset: 0x00013E00
		// (set) Token: 0x06000483 RID: 1155 RVA: 0x00015C08 File Offset: 0x00013E08
		public EnergyCalibration EnergyCalibration
		{
			get
			{
				return this.energyCalibration;
			}
			set
			{
				this.energyCalibration = value;
			}
		}

		// Token: 0x06000484 RID: 1156 RVA: 0x00015C14 File Offset: 0x00013E14
		public EnergyCalibrationChangedEventArgs(EnergyCalibration energyCalibration)
		{
			this.energyCalibration = energyCalibration;
		}

		// Token: 0x040001E8 RID: 488
		EnergyCalibration energyCalibration;
	}
}
