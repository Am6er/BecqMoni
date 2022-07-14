using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200012A RID: 298
    public enum PeakMode
    {
        // Token: 0x04000911 RID: 2321
        [XmlEnum(Name = "Visible")]
        Visible,
        // Token: 0x04000912 RID: 2322
        [XmlEnum(Name = "Invisible")]
        Invisible
    }
}
