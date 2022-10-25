using System.Collections.Generic;
using System.Linq;

namespace BecquerelMonitor
{
    // Token: 0x0200001C RID: 28
    public class DoseRateConfig
    {

        // Token: 0x060000EE RID: 238 RVA: 0x00005450 File Offset: 0x00003650
        public DoseRateConfig()
        {
        }

        // Token: 0x060000EF RID: 239 RVA: 0x00005468 File Offset: 0x00003668
        public DoseRateConfig(DoseRateConfig config)
        {
            this.doseRateCalibrationPoints = config.DoseRateCalibrationPoints;
        }

        // Token: 0x060000F0 RID: 240 RVA: 0x000054A4 File Offset: 0x000036A4
        public DoseRateConfig Clone()
        {
            return new DoseRateConfig(this);
        }

        public List<DoseRateCalibrationPoint> DoseRateCalibrationPoints
        {
            get
            {
                return this.doseRateCalibrationPoints;
            }
            set
            {
                this.doseRateCalibrationPoints = value;
                this.doseRateCalibrationPoints.Sort();
            }
        }

        public DoseRateCalibrationPoint GetPointbyLowerBound(double lowerbound)
        {
            foreach(DoseRateCalibrationPoint point in this.doseRateCalibrationPoints)
            {
                if (point.LowerBound == lowerbound) return point;
            }
            return null;
        }

        public DoseRateCalibrationPoint GetPointbyUpperBound(double upperbound)
        {
            foreach(DoseRateCalibrationPoint point in this.doseRateCalibrationPoints)
            {
                if (point.UpperBound == upperbound) return point;
            }
            return null;
        }

        public bool ValidateCalibration()
        {
            DoseRateCalibrationPoint lower = GetPointbyLowerBound(0.0);
            DoseRateCalibrationPoint upper = GetPointbyUpperBound(3000.0);
            if (lower == null || upper == null)
            {
                return false;
            }
            if (this.doseRateCalibrationPoints.Count == 1 && lower.LowerBound == 0.0 && lower.UpperBound == 3000.0)
            {
                return true;
            }
            for(int i = 1; i <= this.doseRateCalibrationPoints.Count - 1; i++)
            {
                lower = GetPointbyLowerBound(lower.UpperBound);
                if (lower == null)
                {
                    return false;
                }
                if (i == this.doseRateCalibrationPoints.Count - 1 && lower.UpperBound == 3000.0)
                {
                    return true;
                }
            }

            return false;
        }

        List<DoseRateCalibrationPoint> doseRateCalibrationPoints = new List<DoseRateCalibrationPoint>();
    }
}
