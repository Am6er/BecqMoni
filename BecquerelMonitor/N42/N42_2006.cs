using System.Xml.Serialization;

namespace BecquerelMonitor.N42
{

    // N42 2006
    [XmlRoot("N42InstrumentData", Namespace = Ns)]
    public class N42InstrumentData
    {
        public const string Ns = "http://physics.nist.gov/Divisions/Div846/Gp4/ANSIN4242/2005/ANSIN4242";

        [XmlElement("Measurement")]
        public N42_2006_Measurement Measurement { get; set; }

        [XmlElement("Calibration")]
        public N42_2006_EnergyCalibration[] Calibration { get; set; }
    }

    public class N42_2006_Measurement
    {
        [XmlElement("InstrumentInformation")]
        public N42_2006_InstrumentInformation InstrumentInformation { get; set; }

        [XmlElement("Spectrum")]
        public N42_2006_Spectrum Spectrum { get; set; }
    }
    public class N42_2006_Spectrum
    {
        [XmlElement("ChannelData")]
        public string ChannelData { get; set; }

        [XmlElement("StartTime")]
        public string StartTime { get; set; }

        [XmlElement("RealTime")]
        public string RealTime { get; set; }

        [XmlElement("LiveTime")]
        public string LiveTime { get; set; }

        [XmlAttribute("Type")]
        public string Type { get; set; }
    }

    public class N42_2006_InstrumentInformation
    {
        [XmlElement("InstrumentType")]
        public string InstrumentType { get; set; }

        [XmlElement("Manufacturer")]
        public string Manufacturer { get; set; }

        [XmlElement("InstrumentModel")]
        public string InstrumentModel { get; set; }

        [XmlElement("InstrumentID")]
        public string InstrumentID { get; set; }

        [XmlElement("ProbeType")]
        public string ProbeType { get; set; }
    }

    public class N42_2006_EnergyCalibration
    {
        [XmlAttribute("Type")]
        public string Type { get; set; }

        [XmlElement("Equation")]
        public N42_2006_Equation Equation { get; set; }
    }

    public class N42_2006_Equation
    {
        [XmlAttribute("Model")]
        public string Model { get; set; }

        [XmlElement("Coefficients")]
        public string Coefficients { get; set; }
    }
}
