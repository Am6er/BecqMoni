using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000124 RID: 292
	public enum VerticalUnit
	{
		// Token: 0x040008FD RID: 2301
		[XmlEnum(Name = "Counts")]
		Counts,
		// Token: 0x040008FE RID: 2302
		[XmlEnum(Name = "CountsPerSecond")]
		CountsPerSecond
	}
}
