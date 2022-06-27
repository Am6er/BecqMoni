using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000127 RID: 295
	public enum VerticalScaleType
	{
		// Token: 0x04000906 RID: 2310
		[XmlEnum(Name = "LinearScale")]
		LinearScale,
		// Token: 0x04000907 RID: 2311
		[XmlEnum(Name = "LogarithmicScale")]
		LogarithmicScale
	}
}
