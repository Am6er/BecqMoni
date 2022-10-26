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

        List<DoseRateCalibrationPoint> doseRateCalibrationPoints = new List<DoseRateCalibrationPoint>();
    }
}
