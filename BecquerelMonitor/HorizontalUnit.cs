using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000125 RID: 293
    public enum HorizontalUnit
    {
        // Token: 0x04000900 RID: 2304
        [XmlEnum(Name = "Channel")]
        Channel,
        // Token: 0x04000901 RID: 2305
        [XmlEnum(Name = "Energy")]
        Energy
    }
}
