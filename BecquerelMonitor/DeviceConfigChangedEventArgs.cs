using System;

namespace BecquerelMonitor
{
	// Token: 0x02000074 RID: 116
	public class DeviceConfigChangedEventArgs : EventArgs
	{
		// Token: 0x170001CF RID: 463
		// (get) Token: 0x060005F0 RID: 1520 RVA: 0x0002569C File Offset: 0x0002389C
		// (set) Token: 0x060005F1 RID: 1521 RVA: 0x000256A4 File Offset: 0x000238A4
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

		// Token: 0x060005F2 RID: 1522 RVA: 0x000256B0 File Offset: 0x000238B0
		public DeviceConfigChangedEventArgs(string guid)
		{
			this.guid = guid;
		}

		// Token: 0x04000326 RID: 806
		string guid;
	}
}
