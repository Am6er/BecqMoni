using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public abstract class FwhmCalibration
    {
        public const int GaussianPeakType = 0;
        public const int ExpGaussExpPeakType = 1;
        public const int VoigtPeakType = 2;

        public static bool IsSupportedPeakType(int peakType)
        {
            return peakType == GaussianPeakType ||
                peakType == ExpGaussExpPeakType ||
                peakType == VoigtPeakType;
        }

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

            // set default peak shape as gauss
            simpleSqrtFwhmCalibration.PeakType = GaussianPeakType;
            simpleSqrtFwhmCalibration.ExpGaussExpLeftTail = 1.0;
            simpleSqrtFwhmCalibration.ExpGaussExpRightTail = 1.0;
            simpleSqrtFwhmCalibration.VoigtSigma = 1.0;
            simpleSqrtFwhmCalibration.VoigtGamma = 1.0;

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

        public FwhmCalibration RecalcWithNewChannelNum(int oldchannelnum, int newchannelnum)
        {
            FwhmCalibration newFwhmCalibration = Clone();
            double mul = (double)oldchannelnum / (double)newchannelnum;
            foreach (CalibrationPeak peak in newFwhmCalibration.CalibrationPeaks)
            {
                // Round the channel instead of truncating, and keep FWHM as double:
                // the old (int) cast turned e.g. FWHM 12.7/8 into 1 (and anything < mul
                // into 0), so the curve was fitted to ruined points.
                peak.Channel = (int)Math.Round(peak.Channel / mul);
                peak.FWHM = peak.FWHM / mul;
            }
            newFwhmCalibration.PerformCalibration(newchannelnum);
            return newFwhmCalibration;
        }

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

        public abstract double VoigtSigma { get; set; }

        public abstract double VoigtGamma { get; set; }

        public abstract double GaussianChi2Total { get; set; }

        public abstract double ExpGaussExpChi2Total { get; set; }

        public abstract double VoigtChi2Total { get; set; }

        public abstract double Chi2pNdp {  get; set; }
    }
}
