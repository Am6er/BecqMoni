using System;

namespace BecquerelMonitor
{
	// Token: 0x02000108 RID: 264
	public class ROIConfigReference
	{
		// Token: 0x170003A1 RID: 929
		// (get) Token: 0x06000E51 RID: 3665 RVA: 0x000543A4 File Offset: 0x000525A4
		// (set) Token: 0x06000E52 RID: 3666 RVA: 0x000543AC File Offset: 0x000525AC
		public string Name
		{
			get
			{
				return this.name;
			}
			set
			{
				this.name = value;
			}
		}

		// Token: 0x170003A2 RID: 930
		// (get) Token: 0x06000E53 RID: 3667 RVA: 0x000543B8 File Offset: 0x000525B8
		// (set) Token: 0x06000E54 RID: 3668 RVA: 0x000543C0 File Offset: 0x000525C0
		public string Guid
		{
			get
			{
				return this.guid;
			}
			set
			{
				this.guid = value;
			}
		}

		// Token: 0x04000835 RID: 2101
		string name;

		// Token: 0x04000836 RID: 2102
		string guid;
	}
}
