using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200012C RID: 300
    public enum MagnificationReference
    {
        // Token: 0x04000917 RID: 2327
        [XmlEnum(Name = "Left")]
        Left,
        // Token: 0x04000918 RID: 2328
        [XmlEnum(Name = "Center")]
        Center,
        // Token: 0x04000919 RID: 2329
        [XmlEnum(Name = "Right")]
        Right
    }
}
