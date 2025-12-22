using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public abstract class FwhmCalibration
    {
        // Enum all siblings of abstract class FwhmCalibration
        public enum FwhmCalibrationCurve
        {
            [XmlEnum(Name = "Simple Square root")]
            SimpleSqrtFwhmCalibration,

            [XmlEnum(Name = "Square root polynomial")]
            SqrtFwhmCalibration
        }

        public static SimpleSqrtFwhmCalibration DefaultCalibration(FWHMPeakDetectionMethodConfig fwhmConfig, EnergyCalibration energyCalibration)
        {
            SimpleSqrtFwhmCalibration simpleSqrtFwhmCalibration = new SimpleSqrtFwhmCalibration();
            CalibrationPeak peak = new CalibrationPeak
            {
                Channel = 0,
                Energy = energyCalibration.ChannelToEnergy(0),
                FWHM = fwhmConfig.FWHM_AT_0
            };
            simpleSqrtFwhmCalibration.CalibrationPeaks.Add(peak);

            peak = new CalibrationPeak
            {
                Channel = (int)fwhmConfig.Ch_Fwhm,
                Energy = energyCalibration.ChannelToEnergy(fwhmConfig.Ch_Fwhm),
                FWHM = fwhmConfig.Width_Fwhm
            };
            simpleSqrtFwhmCalibration.CalibrationPeaks.Add(peak);

            simpleSqrtFwhmCalibration.PeakType = fwhmConfig.PeakType;
            simpleSqrtFwhmCalibration.ExpGaussExpLeftTail = fwhmConfig.ExpGaussExpLeftTail;
            simpleSqrtFwhmCalibration.ExpGaussExpRightTail = fwhmConfig.ExpGaussExpRightTail;

            if (simpleSqrtFwhmCalibration.PerformCalibration(energyCalibration.MaxChannels()))
            {
                return simpleSqrtFwhmCalibration;
            }
            else
            {
                // TODO Может чтобы избежать null нужно тут влепить некую дефолтную кривую по аналогии с y = x? На подумать.
                return null;
            }
        }

        public abstract double ChannelToFwhm(double channel);

        public abstract double FwhmToChannel(double fwhm);

        [XmlArrayItem("Peak")]
        public abstract List<CalibrationPeak> CalibrationPeaks { get; set; }

        [XmlArrayItem("Coefficient")]
        public abstract double[] Coefficients { get; set; }

        public abstract bool PerformCalibration(int maxchannels);

        public abstract FwhmCalibration Clone();

        public List<CalibrationPeak> ClonePeaks()
        {
            return new List<CalibrationPeak>(CalibrationPeaks);
        }

        public abstract string GetFormula();

        public override abstract string ToString();

        public abstract bool NotCalibrated();

        public abstract int MinPeaksRequirement();

        public abstract int PeakType { get; set; }

        public abstract double ExpGaussExpLeftTail { get; set; }

        public abstract double ExpGaussExpRightTail { get; set; }

        public abstract double Chi2pNdp {  get; set; }
    }
}
