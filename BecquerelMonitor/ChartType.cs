using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000126 RID: 294
	public enum ChartType
	{
		// Token: 0x04000903 RID: 2307
		[XmlEnum(Name = "BarChart")]
		BarChart,
		// Token: 0x04000904 RID: 2308
		[XmlEnum(Name = "LineChart")]
		LineChart
	}
}
