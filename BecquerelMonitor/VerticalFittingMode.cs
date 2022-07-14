using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000128 RID: 296
    public enum VerticalFittingMode
    {
        // Token: 0x04000909 RID: 2313
        [XmlEnum(Name = "None")]
        None,
        // Token: 0x0400090A RID: 2314
        [XmlEnum(Name = "MinMax")]
        MinMax,
        // Token: 0x0400090B RID: 2315
        [XmlEnum(Name = "BackgroundMixMax")]
        BackgroundMinMax
    }
}
