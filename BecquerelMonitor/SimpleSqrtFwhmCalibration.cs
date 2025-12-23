using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class SimpleSqrtFwhmCalibration : FwhmCalibration
    {
        // Fwhm [ch]
        // Fwhm(ch) = Sqrt(b + k * ch)
        string formula = "FWHM = √({1} * ch + {0})";

        List<CalibrationPeak> peaks = new List<CalibrationPeak>();
        double[] coefficients = new double[2];

        [XmlArrayItem("Peak")]
        public override List<CalibrationPeak> CalibrationPeaks { get => peaks; set => peaks = value; }

        [XmlArrayItem("Coefficient")]
        public override double[] Coefficients { get => coefficients; set => coefficients = value; }

        public override double ChannelToFwhm(double channel)
        {
            double result = coefficients[0] + coefficients[1] * channel;
            if (result < 0) return 0.0;
            return Math.Sqrt(result);
        }

        public override double FwhmToChannel(double fwhm)
        {
            return (fwhm * fwhm - coefficients[0]) / coefficients[1];
        }
        public override FwhmCalibration Clone()
        {
            return new SimpleSqrtFwhmCalibration
            {
                CalibrationPeaks = CalibrationPeak.ClonePeaks(this.CalibrationPeaks),
                Coefficients = this.Coefficients,
                PeakType = this.PeakType,
                ExpGaussExpLeftTail = this.ExpGaussExpLeftTail,
                ExpGaussExpRightTail = this.ExpGaussExpRightTail,
                Chi2pNdp = this.Chi2pNdp,
            };
        }

        public override string GetFormula()
        {
            return String.Format(formula, "k", "b");
        }



        public override int MinPeaksRequirement()
        {
            return 2;
        }

        public override bool PerformCalibration(int maxchannels)
        {
            if (peaks.Count <= 1) return false;
            coefficients = Utils.CalibrationSolver.Solve(peaks, 1);
            return CheckCalibration(maxchannels);
        }

        public override string ToString()
        {
            return String.Format(formula, coefficients[1], coefficients[0]);
        }

        public override bool NotCalibrated()
        {
            return (coefficients[0] == 0 && coefficients[1] == 0);
        }

        private bool CheckCalibration(int maxchannels)
        {
            for (int i = 1; i < maxchannels; i++)
            {
                if (ChannelToFwhm(i - 1) > ChannelToFwhm(i)) return false;
            }
            return true;
        }

        public override int PeakType { get => this.peak_type; set => this.peak_type = value; }

        public override double ExpGaussExpLeftTail { get => this.left_tail; set => this.left_tail = value; }

        public override double ExpGaussExpRightTail { get => this.right_tail; set => this.right_tail = value; }

        public override double Chi2pNdp { get => this.chi2pndp; set => this.chi2pndp = value; }

        int peak_type = 0;

        double left_tail = 1.0;

        double right_tail = 1.0;

        double chi2pndp = -1.0;
    }
}
