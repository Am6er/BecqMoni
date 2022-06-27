using System;

namespace BecquerelMonitor
{
	// Token: 0x02000031 RID: 49
	public class DoseRate
	{
		// Token: 0x17000121 RID: 289
		// (get) Token: 0x060002AB RID: 683 RVA: 0x0000D1E4 File Offset: 0x0000B3E4
		// (set) Token: 0x060002AC RID: 684 RVA: 0x0000D1EC File Offset: 0x0000B3EC
		public double Rate
		{
			get
			{
				return this.rate;
			}
			set
			{
				this.rate = value;
			}
		}

		// Token: 0x17000122 RID: 290
		// (get) Token: 0x060002AD RID: 685 RVA: 0x0000D1F8 File Offset: 0x0000B3F8
		// (set) Token: 0x060002AE RID: 686 RVA: 0x0000D200 File Offset: 0x0000B400
		public double Error
		{
			get
			{
				return this.error;
			}
			set
			{
				this.error = value;
			}
		}

		// Token: 0x040000F4 RID: 244
		double rate;

		// Token: 0x040000F5 RID: 245
		double error;
	}
}
