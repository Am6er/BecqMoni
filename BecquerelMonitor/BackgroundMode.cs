using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000122 RID: 290
    public enum BackgroundMode
    {
        // Token: 0x040008F7 RID: 2295
        [XmlEnum(Name = "Visible")]
        Visible,
        // Token: 0x040008F8 RID: 2296
        [XmlEnum(Name = "Invisible")]
        Invisible,

        [XmlEnum(Name = "Substract")]
        Substract
    }
}
