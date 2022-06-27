using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x0200012E RID: 302
	public enum WeightUnit
	{
		// Token: 0x0400091E RID: 2334
		[XmlEnum(Name = "Kilogram")]
		Kilogram,
		// Token: 0x0400091F RID: 2335
		[XmlEnum(Name = "Gram")]
		Gram
	}
}
