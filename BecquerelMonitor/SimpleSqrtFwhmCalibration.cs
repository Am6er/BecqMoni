using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class SimpleSqrtFwhmCalibration : FwhmCalibration
    {
        // Fwhm [ch]
        // Fwhm(ch) = Sqrt(b + k * ch)

        List<CalibrationPeak> peaks = new List<CalibrationPeak>();
        double[] coefficients = new double[2];

        [XmlArrayItem("Peak")]
        public override List<CalibrationPeak> CalibrationPeaks { get => peaks; set => peaks = value; }

        [XmlArrayItem("Coefficient")]
        public override double[] Coefficients { get => coefficients; set => coefficients = value; }

        public override double ChannelToFwhm(double channel)
        {
            return Math.Sqrt(coefficients[0] + coefficients[1] * channel);
        }

        public override bool PerformCalibration()
        {
            if (peaks.Count <= 1) return false;
            coefficients = Utils.CalibrationSolver.Solve(peaks, 1);
            return true;
        }
    }
}
