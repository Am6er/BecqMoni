using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x020000A9 RID: 169
	public class PRAHomageMethodConfig : PulseDetectionMethodConfig
	{
		// Token: 0x17000252 RID: 594
		// (get) Token: 0x0600084D RID: 2125 RVA: 0x000302D4 File Offset: 0x0002E4D4
		// (set) Token: 0x0600084E RID: 2126 RVA: 0x000302DC File Offset: 0x0002E4DC
		public double LowerThreshold
		{
			get
			{
				return this.lowerThreshold;
			}
			set
			{
				this.lowerThreshold = value;
			}
		}

		// Token: 0x17000253 RID: 595
		// (get) Token: 0x0600084F RID: 2127 RVA: 0x000302E8 File Offset: 0x0002E4E8
		// (set) Token: 0x06000850 RID: 2128 RVA: 0x000302F0 File Offset: 0x0002E4F0
		public double UpperThreshold
		{
			get
			{
				return this.upperThreshold;
			}
			set
			{
				this.upperThreshold = value;
			}
		}

		// Token: 0x17000254 RID: 596
		// (get) Token: 0x06000851 RID: 2129 RVA: 0x000302FC File Offset: 0x0002E4FC
		// (set) Token: 0x06000852 RID: 2130 RVA: 0x00030304 File Offset: 0x0002E504
		public double PulseThreshold
		{
			get
			{
				return this.pulseThreshold;
			}
			set
			{
				this.pulseThreshold = value;
			}
		}

		// Token: 0x17000255 RID: 597
		// (get) Token: 0x06000853 RID: 2131 RVA: 0x00030310 File Offset: 0x0002E510
		// (set) Token: 0x06000854 RID: 2132 RVA: 0x00030318 File Offset: 0x0002E518
		public int PulseShapeSize
		{
			get
			{
				return this.pulseShapeSize;
			}
			set
			{
				if (this.pulseShapeSize != value)
				{
					this.pulseShape = new double[value];
					this.pulseShapeSize = value;
				}
			}
		}

		// Token: 0x17000256 RID: 598
		// (get) Token: 0x06000855 RID: 2133 RVA: 0x0003033C File Offset: 0x0002E53C
		// (set) Token: 0x06000856 RID: 2134 RVA: 0x00030344 File Offset: 0x0002E544
		public int PeakIndex
		{
			get
			{
				return this.peakIndex;
			}
			set
			{
				this.peakIndex = value;
			}
		}

		// Token: 0x17000257 RID: 599
		// (get) Token: 0x06000857 RID: 2135 RVA: 0x00030350 File Offset: 0x0002E550
		// (set) Token: 0x06000858 RID: 2136 RVA: 0x00030358 File Offset: 0x0002E558
		public double PulseLowerThreshold
		{
			get
			{
				return this.pulseLowerThreshold;
			}
			set
			{
				this.pulseLowerThreshold = value;
			}
		}

		// Token: 0x17000258 RID: 600
		// (get) Token: 0x06000859 RID: 2137 RVA: 0x00030364 File Offset: 0x0002E564
		// (set) Token: 0x0600085A RID: 2138 RVA: 0x0003036C File Offset: 0x0002E56C
		public double PulseUpperThreshold
		{
			get
			{
				return this.pulseUpperThreshold;
			}
			set
			{
				this.pulseUpperThreshold = value;
			}
		}

		// Token: 0x17000259 RID: 601
		// (get) Token: 0x0600085B RID: 2139 RVA: 0x00030378 File Offset: 0x0002E578
		// (set) Token: 0x0600085C RID: 2140 RVA: 0x00030380 File Offset: 0x0002E580
		public int NumberOfPulses
		{
			get
			{
				return this.numberOfPulses;
			}
			set
			{
				this.numberOfPulses = value;
			}
		}

		// Token: 0x1700025A RID: 602
		// (get) Token: 0x0600085D RID: 2141 RVA: 0x0003038C File Offset: 0x0002E58C
		// (set) Token: 0x0600085E RID: 2142 RVA: 0x00030394 File Offset: 0x0002E594
		[XmlArrayItem("DataPoint")]
		public double[] PulseShape
		{
			get
			{
				return this.pulseShape;
			}
			set
			{
				this.pulseShape = value;
			}
		}

		// Token: 0x0600085F RID: 2143 RVA: 0x000303A0 File Offset: 0x0002E5A0
		public PRAHomageMethodConfig()
		{
			this.pulseShape = new double[this.pulseShapeSize];
		}

		// Token: 0x06000860 RID: 2144 RVA: 0x00030424 File Offset: 0x0002E624
		public override PulseDetectionMethodConfig Clone()
		{
			return (PRAHomageMethodConfig)base.MemberwiseClone();
		}

		// Token: 0x0400044E RID: 1102
		double lowerThreshold = 1.0;

		// Token: 0x0400044F RID: 1103
		double upperThreshold = 100.0;

		// Token: 0x04000450 RID: 1104
		double pulseThreshold = 0.6;

		// Token: 0x04000451 RID: 1105
		int pulseShapeSize = 16;

		// Token: 0x04000452 RID: 1106
		int peakIndex = 8;

		// Token: 0x04000453 RID: 1107
		double pulseLowerThreshold = 4.0;

		// Token: 0x04000454 RID: 1108
		double pulseUpperThreshold = 40.0;

		// Token: 0x04000455 RID: 1109
		int numberOfPulses;

		// Token: 0x04000456 RID: 1110
		double[] pulseShape;
	}
}
