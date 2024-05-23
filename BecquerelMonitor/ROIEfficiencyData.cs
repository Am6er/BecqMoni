using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class ROIEfficiencyData
    {
        public double Energy { get; set; }

        public double Efficiency { get; set; }

        public double ErrorPercent { get; set; }

        public ROIEfficiencyData Clone()
        {
            return new ROIEfficiencyData() 
            {
               Energy = this.Energy,
               Efficiency = this.Efficiency,
               ErrorPercent = this.ErrorPercent
            };
        }
    }
}
