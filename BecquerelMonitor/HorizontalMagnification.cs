using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200012B RID: 299
    public enum HorizontalMagnification
    {
        // Token: 0x04000914 RID: 2324
        [XmlEnum(Name = "Equal")]
        Equal,
        // Token: 0x04000915 RID: 2325
        [XmlEnum(Name = "Fit")]
        Fit
    }
}
