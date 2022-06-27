using System;

namespace BecquerelMonitor
{
	// Token: 0x020000F9 RID: 249
	public class ShowEnergyCalibrationViewEventArgs : EventArgs
	{
		// Token: 0x17000326 RID: 806
		// (get) Token: 0x06000BF6 RID: 3062 RVA: 0x00048070 File Offset: 0x00046270
		// (set) Token: 0x06000BF7 RID: 3063 RVA: 0x00048078 File Offset: 0x00046278
		public bool Visible
		{
			get
			{
				return this.visible;
			}
			set
			{
				this.visible = value;
			}
		}

		// Token: 0x06000BF8 RID: 3064 RVA: 0x00048084 File Offset: 0x00046284
		public ShowEnergyCalibrationViewEventArgs(bool visible)
		{
			this.visible = visible;
		}

		// Token: 0x0400078C RID: 1932
		bool visible;
	}
}
