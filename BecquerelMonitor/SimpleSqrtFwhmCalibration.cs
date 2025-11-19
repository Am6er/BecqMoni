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
                CalibrationPeaks = this.CalibrationPeaks,
                Coefficients = this.Coefficients
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

        public override bool PerformCalibration()
        {
            if (peaks.Count <= 1) return false;
            coefficients = Utils.CalibrationSolver.Solve(peaks, 1);
            return true;
        }

        public override string ToString()
        {
            return String.Format(formula, coefficients[1], coefficients[0]);
        }

        public override bool NotCalibrated()
        {
            return (coefficients[0] == 0 && coefficients[1] == 0);
        }
    }
}
