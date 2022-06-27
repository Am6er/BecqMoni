using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000129 RID: 297
	public enum SmoothingMethod
	{
		// Token: 0x0400090D RID: 2317
		[XmlEnum(Name = "None")]
		None,
		// Token: 0x0400090E RID: 2318
		[XmlEnum(Name = "SimpleMovingAverage")]
		SimpleMovingAverage,
		// Token: 0x0400090F RID: 2319
		[XmlEnum(Name = "WeightedMovingAverage")]
		WeightedMovingAverage
	}
}
