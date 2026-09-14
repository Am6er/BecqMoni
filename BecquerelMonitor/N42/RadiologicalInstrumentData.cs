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

        // ⛔ ЖИВОЕ ВРЕМЯ — ТОЖЕ string, А НЕ double (`A214`, 06.09.2026).
        //    Здесь стоял double, и это был отказ разбора ВСЕГО документа на
        //    всяком файле, записавшем живое время ПО СПЕЦИФИКАЦИИ N42-2006,
        //    то есть как xs:duration («PT295S»). Измерено 06.09.2026 входом
        //    case30_rad_live_iso: XmlSerializer бросал
        //    InvalidOperationException «There is an error in XML document
        //    (10, 20)» ← FormatException, и наружу шло «ввоз N42 оборвался» —
        //    ни спектра, ни указания, какое поле виновато.
        //
        //    ⚠ Соседнее поле RealTime уже заведено строкой ровно по этой
        //    причине (05.09.2026), а LiveTime осталось числом: дверь читала
        //    ОДНО из двух времён одного элемента двумя разными соглашениями.
        //    Теперь оба читаются Util.N42SecondsLoose — обе записи принимаются,
        //    нечитаемая НАЗЫВАЕТСЯ словами (`A140`), а не подставляется молча.
        [XmlElement("LiveTime")]
        public string LiveTime { get; set; }

        // ⛔ ПОЛНОЕ ВРЕМЯ НАБОРА — ЭЛЕМЕНТ БЫЛ, ЧИТАТЕЛЯ НЕ БЫЛО (05.09.2026).
        //    В N42-2006 у Spectrum ДВА времени: RealTime (по часам) и LiveTime
        //    (зачтённое). Модель несла только второе, XmlSerializer выбрасывал
        //    первое МОЛЧА — тот же разряд, что `A136` (EnergyBoundaryValues), —
        //    и разбор клал живое время в поле полного, потому что другого у
        //    него не было.
        //
        //    ⚠ ТИП string, А НЕ double, И ЭТО НЕ ЛЕНЬ. Извод Alpha Hound пишет
        //    LiveTime ГОЛЫМИ СЕКУНДАМИ («295»), а спецификация N42-2006
        //    требует xs:duration («PT295S»). Объявив здесь double, мы получили
        //    бы отказ разбора ВСЕГО файла на записи по спецификации; объявив
        //    string, читаем обе записи (см. Util.N42SecondsLoose).
        [XmlElement("RealTime")]
        public string RealTime { get; set; }

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
