using System.Xml.Serialization;

namespace BecquerelMonitor.N42
{
    // Alpha Hound
    [XmlRoot("RadiologicalInstrumentData", Namespace = Ns)]
    public class RadiologicalInstrumentData
    {
        public const string Ns = "http://physics.nist.gov/N42/2006/N42";

        [XmlElement("MeasurementGroup")]
        public AH_MeasurementGroup MeasurementGroup { get; set; }
    }

    public class AH_MeasurementGroup
    {
        [XmlElement("Measurement")]
        public AH_Measurement Measurement { get; set; }
    }

    public class AH_Measurement
    {
        [XmlElement("Spectrum")]
        public AH_Spectrum Spectrum { get; set; }
    }

    public class AH_Spectrum
    {
        [XmlElement("InstrumentInformation")]
        public AH_InstrumentInformation InstrumentInformation { get; set; }

        [XmlElement("EnergyCalibration")]
        public AH_EnergyCalibration EnergyCalibration { get; set; }

        [XmlElement("ChannelData")]
        public AH_ChannelData ChannelData { get; set; }

        [XmlElement("LiveTime")]
        public double LiveTime { get; set; }

        [XmlElement("SpectrumType")]
        public string SpectrumType { get; set; }
    }

    public class AH_InstrumentInformation
    {
        [XmlElement("Manufacturer")]
        public string Manufacturer { get; set; }

        [XmlElement("Model")]
        public string Model { get; set; }

        [XmlElement("SerialNumber")]
        public string SerialNumber { get; set; }
    }

    public class AH_EnergyCalibration
    {
        [XmlElement("CalibrationEquation")]
        public string CalibrationEquation { get; set; }

        [XmlElement("ChannelEnergies")]
        public string ChannelEnergies { get; set; }
    }

    public class AH_ChannelData
    {
        [XmlAttribute("NumberOfChannels")]
        public int NumberOfChannels { get; set; }

        [XmlText]
        public string Data { get; set; }
    }
}
