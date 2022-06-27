using System;

namespace WinMM
{
	// Token: 0x020001B0 RID: 432
	public class WaveInDeviceCaps
	{
		// Token: 0x17000679 RID: 1657
		// (get) Token: 0x06001580 RID: 5504 RVA: 0x0006C3A4 File Offset: 0x0006A5A4
		// (set) Token: 0x06001581 RID: 5505 RVA: 0x0006C3AC File Offset: 0x0006A5AC
		public int DeviceId
		{
			get
			{
				return this.deviceId;
			}
			set
			{
				this.deviceId = value;
			}
		}

		// Token: 0x1700067A RID: 1658
		// (get) Token: 0x06001582 RID: 5506 RVA: 0x0006C3B8 File Offset: 0x0006A5B8
		// (set) Token: 0x06001583 RID: 5507 RVA: 0x0006C3C0 File Offset: 0x0006A5C0
		public string Manufacturer
		{
			get
			{
				return this.manufacturer;
			}
			set
			{
				this.manufacturer = value;
			}
		}

		// Token: 0x1700067B RID: 1659
		// (get) Token: 0x06001584 RID: 5508 RVA: 0x0006C3CC File Offset: 0x0006A5CC
		// (set) Token: 0x06001585 RID: 5509 RVA: 0x0006C3D4 File Offset: 0x0006A5D4
		public int ProductId
		{
			get
			{
				return this.productId;
			}
			set
			{
				this.productId = value;
			}
		}

		// Token: 0x1700067C RID: 1660
		// (get) Token: 0x06001586 RID: 5510 RVA: 0x0006C3E0 File Offset: 0x0006A5E0
		// (set) Token: 0x06001587 RID: 5511 RVA: 0x0006C3E8 File Offset: 0x0006A5E8
		public int DriverVersion
		{
			get
			{
				return this.driverVersion;
			}
			set
			{
				this.driverVersion = value;
			}
		}

		// Token: 0x1700067D RID: 1661
		// (get) Token: 0x06001588 RID: 5512 RVA: 0x0006C3F4 File Offset: 0x0006A5F4
		// (set) Token: 0x06001589 RID: 5513 RVA: 0x0006C3FC File Offset: 0x0006A5FC
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

		// Token: 0x1700067E RID: 1662
		// (get) Token: 0x0600158A RID: 5514 RVA: 0x0006C408 File Offset: 0x0006A608
		// (set) Token: 0x0600158B RID: 5515 RVA: 0x0006C410 File Offset: 0x0006A610
		public int Channels
		{
			get
			{
				return this.channels;
			}
			set
			{
				this.channels = value;
			}
		}

		// Token: 0x04000C6E RID: 3182
		int deviceId;

		// Token: 0x04000C6F RID: 3183
		string manufacturer;

		// Token: 0x04000C70 RID: 3184
		int productId;

		// Token: 0x04000C71 RID: 3185
		int driverVersion;

		// Token: 0x04000C72 RID: 3186
		string name;

		// Token: 0x04000C73 RID: 3187
		int channels;
	}
}
