using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Кривая эффективности регистрации: лог-лог интерполяция по точкам ROI.
    /// За краями таблицы кривая продолжается константой — экстраполировать
    /// наклоном на краю опаснее, чем занизить вес далёкой линии.
    /// </summary>
    public sealed class FsaEfficiency
    {
        readonly double[] logEnergy;
        readonly double[] logEfficiency;

        FsaEfficiency(List<KeyValuePair<double, double>> points)
        {
            points.Sort((a, b) => a.Key.CompareTo(b.Key));
            List<double> energies = new List<double>(points.Count);
            List<double> values = new List<double>(points.Count);
            foreach (KeyValuePair<double, double> point in points)
            {
                // дубль энергии дал бы нулевой шаг интерполяции и NaN в образе
                if (energies.Count > 0 && point.Key <= Math.Exp(energies[energies.Count - 1]))
                {
                    continue;
                }

                energies.Add(Math.Log(point.Key));
                values.Add(Math.Log(Math.Max(point.Value, 1e-12)));
            }

            this.logEnergy = energies.ToArray();
            this.logEfficiency = values.ToArray();
        }

        /// <summary>
        /// Кривая из ROI-конфигурации спектра или null, если её там нет.
        /// Точки с неположительной эффективностью отбрасываются: в поставочных
        /// файлах встречаются и нули, и физически невозможные значения.
        /// </summary>
        public static FsaEfficiency FromRoiConfig(ROIConfigData roiConfig)
        {
            if (roiConfig == null || roiConfig.ROIEfficiency == null)
            {
                return null;
            }

            List<KeyValuePair<double, double>> points = new List<KeyValuePair<double, double>>();
            foreach (ROIEfficiencyData point in roiConfig.ROIEfficiency)
            {
                if (point == null || point.Energy <= 0.0 || point.Efficiency <= 0.0 || point.Efficiency > 1.0)
                {
                    continue;
                }

                points.Add(new KeyValuePair<double, double>(point.Energy, point.Efficiency));
            }

            if (points.Count < 2)
            {
                return null;
            }

            FsaEfficiency curve = new FsaEfficiency(points);
            return curve.logEnergy.Length >= 2 ? curve : null;
        }

        public double Eval(double energy)
        {
            if (this.logEnergy.Length == 0)
            {
                return 1.0;
            }

            double x = Math.Log(Math.Max(energy, 1.0));
            if (x <= this.logEnergy[0])
            {
                return Math.Exp(this.logEfficiency[0]);
            }

            if (x >= this.logEnergy[this.logEnergy.Length - 1])
            {
                return Math.Exp(this.logEfficiency[this.logEfficiency.Length - 1]);
            }

            int hi = 1;
            while (this.logEnergy[hi] < x)
            {
                hi++;
            }

            double f = (x - this.logEnergy[hi - 1]) / (this.logEnergy[hi] - this.logEnergy[hi - 1]);
            return Math.Exp(this.logEfficiency[hi - 1] + f * (this.logEfficiency[hi] - this.logEfficiency[hi - 1]));
        }
    }
}
