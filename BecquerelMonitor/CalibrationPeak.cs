using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class CalibrationPeak : IComparable
    {
        int channel;
        double energy;
        double fwhm;
        int peak_type = 0;
        double left_tail = 1.0;
        double right_tail = 1.0;
        double chi2pndp = -1.0;

        public int Channel { get => channel; set => channel = value; }
        public double Energy { get => energy; set => energy = value; }
        public double FWHM { get => fwhm; set => fwhm = value; }
        
        [XmlIgnore]
        public int PeakType { get => peak_type; set => peak_type = value; }

        [XmlIgnore]
        public double ExpGaussExpLeftTail { get => this.left_tail; set => this.left_tail = value; }

        [XmlIgnore]
        public double ExpGaussExpRightTail { get => this.right_tail; set => this.right_tail = value; }

        [XmlIgnore]
        public double Chi2pNdp { get => this.chi2pndp; set => this.chi2pndp = value; }

        public int CompareTo(object obj)
        {
            return channel.CompareTo(((CalibrationPeak)obj).Channel);
        }

        public bool Equals(CalibrationPeak other)
        {
            if (other.fwhm == fwhm && other.channel == channel) return true;
            return false;
        }

        public static List<CalibrationPeak> ClonePeaks(List<CalibrationPeak> pts)
        {
            List<CalibrationPeak> result = new List<CalibrationPeak>();
            foreach (CalibrationPeak point in pts)
            {
                CalibrationPeak p = new CalibrationPeak
                {
                    Channel = point.Channel,
                    Energy = point.Energy,
                    FWHM = point.FWHM,
                    PeakType = point.PeakType,
                    ExpGaussExpLeftTail = point.ExpGaussExpLeftTail,
                    ExpGaussExpRightTail = point.ExpGaussExpRightTail,
                    Chi2pNdp = point.Chi2pNdp
                };
                result.Add(p);
            }
            return result;
        }
    }
}
