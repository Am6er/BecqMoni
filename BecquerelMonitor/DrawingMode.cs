using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000123 RID: 291
	public enum DrawingMode
	{
		// Token: 0x040008FA RID: 2298
		[XmlEnum(Name = "HighDefinition")]
		HighDefinition,
		// Token: 0x040008FB RID: 2299
		[XmlEnum(Name = "Normal")]
		Normal
	}
}
