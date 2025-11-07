using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class SqrtFwhmCalibration : FwhmCalibration
    {
        // Fwhm [ch]
        // Fwhm(ch) = Sqrt(c + b * ch + a * ch^2)
        
        List<CalibrationPeak> peaks = new List<CalibrationPeak>();
        double[] coefficients = new double[3];

        [XmlArrayItem("Peak")]
        public override List<CalibrationPeak> CalibrationPeaks { get => peaks; set => peaks = value; }

        [XmlArrayItem("Coefficient")]
        public override double[] Coefficients { get => coefficients; set => coefficients = value; }

        public override double ChannelToFwhm(double channel)
        {
            return Math.Sqrt(coefficients[0] + coefficients[1] * channel + coefficients[2] * channel * channel);
        }

        public override FwhmCalibration Clone()
        {
            return new SqrtFwhmCalibration {
                CalibrationPeaks = this.CalibrationPeaks,
                Coefficients = this.Coefficients
            };
        }

        public override bool PerformCalibration()
        {
            if (peaks.Count <= 2) return false;
            coefficients = Utils.CalibrationSolver.Solve(peaks, 2);
            return true;
        }
    }
}
