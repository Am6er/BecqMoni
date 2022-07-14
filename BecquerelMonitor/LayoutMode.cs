using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200011C RID: 284
    public enum LayoutMode
    {
        // Token: 0x040008AD RID: 2221
        [XmlEnum(Name = "UserMode")]
        UserMode,
        // Token: 0x040008AE RID: 2222
        [XmlEnum(Name = "ExpertMode")]
        ExpertMode
    }
}
