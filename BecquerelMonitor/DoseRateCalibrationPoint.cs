using System;

namespace BecquerelMonitor
{
    public class DoseRateCalibrationPoint
    {
        public DoseRateCalibrationPoint()
        {
        }

        public double LowerBound
        {
            get
            {
                return this.lowerbound;
            }
            set
            {
                this.lowerbound = value;
            }
        }

        public double UpperBound
        {
            get
            {
                return this.upperbound;
            }
            set
            {
                this.upperbound = value;
            }
        }

        public double CPS
        {
            get
            {
                return this.cps;
            }
            set
            {
                this.cps = value;
                if (this.etalondoseratevalue > 0 && this.cps > 0)
                {
                    this.sensitivity = this.etalondoseratevalue / this.cps;
                }
                else
                {
                    this.sensitivity = 0;
                }
            }
        }

        public double EtalonDoseRateValue
        {
            get
            {
                return this.etalondoseratevalue;
            }
            set
            {
                this.etalondoseratevalue = value;
                if (this.etalondoseratevalue > 0 && this.cps > 0)
                {
                    this.sensitivity = this.etalondoseratevalue / this.cps;
                }
                else
                {
                    this.sensitivity = 0;
                }
            }
        }

        public double Sensitivity
        {
            get
            {
                return this.sensitivity;
            }
        }

        public bool Equals(DoseRateCalibrationPoint point)
        {
            if (this.upperbound == point.UpperBound && this.lowerbound == point.LowerBound && this.cps == point.CPS && this.etalondoseratevalue == point.EtalonDoseRateValue)
            {
                return true;
            }
            return false;
        }

        double lowerbound;
        double upperbound;
        double cps;
        double etalondoseratevalue;
        double sensitivity;
    }
}
