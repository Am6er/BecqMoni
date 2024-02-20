using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000127 RID: 295
    public enum VerticalScaleType
    {

        [XmlEnum(Name = "LinearScale")]
        LinearScale,

        [XmlEnum(Name = "PowerScale")]
        PowerScale,

        [XmlEnum(Name = "LogarithmicScale")]
        LogarithmicScale
    }
}
