using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class SqrtFwhmCalibration : FwhmCalibration
    {
        // Fwhm [ch]
        // Fwhm(ch) = Sqrt(c + b * ch + a * ch^2)
        string formula = "FWHM = √({2} * ch² + {1} * ch + {0})";

        List<CalibrationPeak> peaks = new List<CalibrationPeak>();
        double[] coefficients = new double[3];

        [XmlArrayItem("Peak")]
        public override List<CalibrationPeak> CalibrationPeaks { get => peaks; set => peaks = value; }

        [XmlArrayItem("Coefficient")]
        public override double[] Coefficients { get => coefficients; set => coefficients = value; }

        public override double ChannelToFwhm(double channel)
        {
            double result = coefficients[0] + coefficients[1] * channel + coefficients[2] * channel * channel;
            if (result < 0) return 0.0;
            return Math.Sqrt(result);
        }

        public override double FwhmToChannel(double fwhm)
        {
            return (-coefficients[1] + Math.Sqrt(coefficients[1] * coefficients[1] - 4 * coefficients[2] * (coefficients[0] - fwhm * fwhm))) / (2 * coefficients[2]);
        }

        public override string GetFormula()
        {
            return String.Format(formula, "c", "b", "a");
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

        public override int MinPeaksRequirement()
        {
            return 3;
        }

        public override string ToString()
        {
            return String.Format(formula, coefficients[0], coefficients[1], coefficients[2]);
        }

        public override bool NotCalibrated()
        {
            return (coefficients[0] == 0 && coefficients[1] == 0 && coefficients[2] == 0);
        }
    }
}
