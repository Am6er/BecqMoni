using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x0200012D RID: 301
	public enum VolumeUnit
	{
		// Token: 0x0400091B RID: 2331
		[XmlEnum(Name = "Liter")]
		Liter,
		// Token: 0x0400091C RID: 2332
		[XmlEnum(Name = "Milliliter")]
		Milliliter
	}
}
