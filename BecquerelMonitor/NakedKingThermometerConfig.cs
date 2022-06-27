using System;

namespace BecquerelMonitor
{
	// Token: 0x02000133 RID: 307
	public class NakedKingThermometerConfig : ThermometerConfig
	{
		// Token: 0x17000439 RID: 1081
		// (get) Token: 0x06000FE6 RID: 4070 RVA: 0x00057A4C File Offset: 0x00055C4C
		// (set) Token: 0x06000FE7 RID: 4071 RVA: 0x00057A54 File Offset: 0x00055C54
		public string SerialPortName
		{
			get
			{
				return this.serialPortName;
			}
			set
			{
				this.serialPortName = value;
			}
		}

		// Token: 0x1700043A RID: 1082
		// (get) Token: 0x06000FE8 RID: 4072 RVA: 0x00057A60 File Offset: 0x00055C60
		// (set) Token: 0x06000FE9 RID: 4073 RVA: 0x00057A68 File Offset: 0x00055C68
		public int BaudRate
		{
			get
			{
				return this.baudRate;
			}
			set
			{
				this.baudRate = value;
			}
		}

		// Token: 0x1700043B RID: 1083
		// (get) Token: 0x06000FEA RID: 4074 RVA: 0x00057A74 File Offset: 0x00055C74
		// (set) Token: 0x06000FEB RID: 4075 RVA: 0x00057A7C File Offset: 0x00055C7C
		public double BaseTemperature1
		{
			get
			{
				return this.baseTemperature1;
			}
			set
			{
				this.baseTemperature1 = value;
			}
		}

		// Token: 0x1700043C RID: 1084
		// (get) Token: 0x06000FEC RID: 4076 RVA: 0x00057A88 File Offset: 0x00055C88
		// (set) Token: 0x06000FED RID: 4077 RVA: 0x00057A90 File Offset: 0x00055C90
		public double Coefficient1
		{
			get
			{
				return this.coefficient1;
			}
			set
			{
				this.coefficient1 = value;
			}
		}

		// Token: 0x1700043D RID: 1085
		// (get) Token: 0x06000FEE RID: 4078 RVA: 0x00057A9C File Offset: 0x00055C9C
		// (set) Token: 0x06000FEF RID: 4079 RVA: 0x00057AA4 File Offset: 0x00055CA4
		public double BaseTemperature2
		{
			get
			{
				return this.baseTemperature2;
			}
			set
			{
				this.baseTemperature2 = value;
			}
		}

		// Token: 0x1700043E RID: 1086
		// (get) Token: 0x06000FF0 RID: 4080 RVA: 0x00057AB0 File Offset: 0x00055CB0
		// (set) Token: 0x06000FF1 RID: 4081 RVA: 0x00057AB8 File Offset: 0x00055CB8
		public double Coefficient2
		{
			get
			{
				return this.coefficient2;
			}
			set
			{
				this.coefficient2 = value;
			}
		}

		// Token: 0x06000FF2 RID: 4082 RVA: 0x00057AC4 File Offset: 0x00055CC4
		public NakedKingThermometerConfig()
		{
		}

		// Token: 0x06000FF3 RID: 4083 RVA: 0x00057ACC File Offset: 0x00055CCC
		public NakedKingThermometerConfig(NakedKingThermometerConfig config)
		{
			this.serialPortName = config.serialPortName;
			this.baudRate = config.baudRate;
			this.baseTemperature1 = config.baseTemperature1;
			this.coefficient1 = config.coefficient1;
			this.baseTemperature2 = config.baseTemperature2;
			this.coefficient2 = config.coefficient2;
		}

		// Token: 0x06000FF4 RID: 4084 RVA: 0x00057B2C File Offset: 0x00055D2C
		public override ThermometerConfig Clone()
		{
			return new NakedKingThermometerConfig(this);
		}

		// Token: 0x0400093B RID: 2363
		string serialPortName;

		// Token: 0x0400093C RID: 2364
		int baudRate;

		// Token: 0x0400093D RID: 2365
		double baseTemperature1;

		// Token: 0x0400093E RID: 2366
		double baseTemperature2;

		// Token: 0x0400093F RID: 2367
		double coefficient1;

		// Token: 0x04000940 RID: 2368
		double coefficient2;
	}
}
