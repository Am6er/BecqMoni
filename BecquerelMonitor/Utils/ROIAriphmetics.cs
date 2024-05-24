using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.Utils
{
    public class ROIAriphmetics
    {
        private IInterpolation EffCurve { get; set; }
        private IInterpolation ErrCurve { get; set; }

        public bool HasValidCurve { get; private set; }
        public double MaxEnergy { get; private set; }
        public double MinEnergy { get; private set; }

        public ROIConfigData ROIConfigData { get; private set; }

        public ROIAriphmetics(ROIConfigData config)
        {
            this.ROIConfigData = config;
            this.InitInterpolation();
        }

        public ROIEfficiencyData CalculateEfficiency(double energy)
        {
            if (!this.HasValidCurve || energy < this.MinEnergy || energy > this.MaxEnergy)
            {
                return null;
            }
            
            double efficiency = this.EffCurve.Interpolate(energy);
            double error = this.ErrCurve.Interpolate(energy);

            return new ROIEfficiencyData()
            {
                Energy = energy,
                Efficiency = efficiency,
                ErrorPercent = error,
            };
        }

        private void InitInterpolation()
        {
            List<double> effEnergies = new List<double>();
            List<double> effValues = new List<double>();
            List<double> effErrors = new List<double>();
            this.MaxEnergy = double.MinValue;
            this.MinEnergy = double.MaxValue;

            this.ROIConfigData.ROIEfficiency.ForEach(def =>
            {
                if (def.Energy > 0 && def.Efficiency > 0)
                {
                    if (def.Energy > this.MaxEnergy) { this.MaxEnergy = def.Energy; }
                    if (def.Energy < this.MinEnergy) { this.MinEnergy = def.Energy; }

                    effEnergies.Add(def.Energy);
                    effValues.Add(def.Efficiency);
                    effErrors.Add(def.ErrorPercent);
                }
            });

            if (effEnergies.Count < 2 || this.MaxEnergy <= this.MinEnergy)
            {
                this.HasValidCurve = false;
                return;
            }

            this.EffCurve = Interpolate.CubicSplineMonotone(effEnergies, effValues);
            this.ErrCurve = Interpolate.CubicSplineMonotone(effEnergies, effErrors);
            this.HasValidCurve = true;
        }
    }
}
