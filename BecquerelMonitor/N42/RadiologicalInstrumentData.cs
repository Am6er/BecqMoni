using System.Xml.Serialization;

namespace BecquerelMonitor.N42
{
    [XmlRoot("RadiologicalInstrumentData", Namespace = Ns)]
    public class RadiologicalInstrumentData
    {
        public const string Ns = "http://physics.nist.gov/N42/2006/N42";

        [XmlElement("MeasurementGroup")]
        public MeasurementGroup MeasurementGroup { get; set; }
    }

    public class MeasurementGroup
    {
        [XmlElement("Measurement")]
        public Measurement Measurement { get; set; }
    }

    public class Measurement
    {
        [XmlElement("Spectrum")]
        public N42Spectrum Spectrum { get; set; }
    }

    public partial class N42Spectrum
    {
        [XmlElement("InstrumentInformation")]
        public InstrumentInformation InstrumentInformation { get; set; }

        [XmlElement("EnergyCalibration")]
        public N42EnergyCalibration EnergyCalibration { get; set; }

        [XmlElement("ChannelData")]
        public N42ChannelData ChannelData { get; set; }

        [XmlElement("LiveTime")]
        public double LiveTime { get; set; }

        [XmlElement("SpectrumType")]
        public string SpectrumType { get; set; }
    }

    public class InstrumentInformation
    {
        [XmlElement("Manufacturer")]
        public string Manufacturer { get; set; }

        [XmlElement("Model")]
        public string Model { get; set; }

        [XmlElement("SerialNumber")]
        public string SerialNumber { get; set; }
    }

    public class N42EnergyCalibration
    {
        [XmlElement("CalibrationEquation")]
        public string CalibrationEquation { get; set; }

        [XmlElement("ChannelEnergies")]
        public string ChannelEnergies { get; set; }
    }

    public class N42ChannelData
    {
        [XmlAttribute("NumberOfChannels")]
        public int NumberOfChannels { get; set; }

        [XmlText]
        public string Data { get; set; }
    }
}
