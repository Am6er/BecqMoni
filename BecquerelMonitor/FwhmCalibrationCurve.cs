using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public enum FwhmCalibrationCurve
    {
        [XmlEnum(Name = "Simple Square root")]
        SimpleSquareRoot,

        [XmlEnum(Name = "Square root polynomial")]
        SquareRootPolynomial
    }
}