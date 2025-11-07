using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public abstract class FwhmCalibration
    {
        public static SimpleSqrtFwhmCalibration DefaultCalibration(FWHMPeakDetectionMethodConfig fwhmCalibration, EnergyCalibration energyCalibration)
        {
            SimpleSqrtFwhmCalibration simpleSqrtFwhmCalibration = new SimpleSqrtFwhmCalibration();
            CalibrationPeak peak = new CalibrationPeak
            {
                Channel = 0,
                Energy = energyCalibration.ChannelToEnergy(0),
                FWHM = fwhmCalibration.FWHM_AT_0
            };
            simpleSqrtFwhmCalibration.CalibrationPeaks.Add(peak);

            peak = new CalibrationPeak
            {
                Channel = (int)fwhmCalibration.Ch_Fwhm,
                Energy = energyCalibration.ChannelToEnergy(fwhmCalibration.Ch_Fwhm),
                FWHM = fwhmCalibration.Width_Fwhm
            };
            simpleSqrtFwhmCalibration.CalibrationPeaks.Add(peak);

            if (simpleSqrtFwhmCalibration.PerformCalibration())
            {
                return simpleSqrtFwhmCalibration;
            }
            else
            {
                return null;
            }
        }

        public abstract double ChannelToFwhm(double channel);

        [XmlArrayItem("Peak")]
        public abstract List<CalibrationPeak> CalibrationPeaks { get; set; }

        [XmlArrayItem("Coefficient")]
        public abstract double[] Coefficients { get; set; }

        [XmlIgnore]
        public int MaxCoefficients { get => Coefficients.Length; }

        public abstract bool PerformCalibration();

        public abstract FwhmCalibration Clone();
    }
}
