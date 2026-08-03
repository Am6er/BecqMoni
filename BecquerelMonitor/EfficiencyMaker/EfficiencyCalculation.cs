using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Второй путь к кривой эффективности: не восстановить её из измерений, а
    /// посчитать из геометрии монте-карловским переносом
    /// (<see cref="EfficiencySimulator"/>).
    ///
    /// Отличие от восстановления по вековому равновесию принципиальное:
    /// **уровень здесь абсолютный**. Восстановление даёт только форму, уровень
    /// приходится брать с прежней кривой или с опорной точки; расчёт даёт
    /// эффективность как она есть, из телесного угла, ослабления и сечений.
    ///
    /// Чего от него ждать. Поверка по источникам с известной активностью
    /// (журнал `tools/effmaker/README.md`): на чистой одиночной линии —
    /// точечный источник Cs-137 на 10 см, паспортная активность — расчёт
    /// сходится с измерением до 1.3 %, у RC103 в маринелли против заводского
    /// коэффициента 0.994. На линиях в наложениях расхождение доходит до
    /// десятка процентов, но это разброс ИЗМЕРЕНИЯ: два независимых способа
    /// вынуть площадь расходятся там на 6–22 %.
    ///
    /// Низ шкалы слабее: когерентное рассеяние в кристалле не выделено, вылет
    /// характеристического рентгена иода не моделируется. Поэтому сетка
    /// начинается с 40 кэВ, но доверия к первым точкам меньше, чем к середине.
    /// </summary>
    public static class EfficiencyCalculation
    {
        /// <summary>
        /// Сетка энергий кривой. Сгущена там, где кривая гнётся сильнее всего
        /// (низ шкалы), и вокруг рабочих линий 662, 1461, 2615 кэВ.
        /// </summary>
        public static readonly double[] DefaultEnergies =
        {
            40, 50, 60, 70, 80, 90, 100, 120, 150, 186, 240, 300, 352, 400, 460,
            510, 583, 609, 662, 720, 800, 900, 1000, 1120, 1250, 1461, 1600,
            1765, 2000, 2200, 2450, 2615, 2800, 3000
        };

        /// <summary>
        /// Считает кривую по геометрии. <paramref name="log"/> получает строку
        /// на каждую точку, <paramref name="cancelled"/> опрашивается между
        /// точками: одна точка — это десятки тысяч историй, и прервать счёт
        /// внутри неё нельзя.
        /// </summary>
        public static EfficiencyFitResult Run(GeometryModel geometry, int histories,
                                              Action<string> log, Func<bool> cancelled)
        {
            if (log == null)
            {
                log = delegate { };
            }

            if (cancelled == null)
            {
                cancelled = () => false;
            }

            EfficiencyFitResult result = new EfficiencyFitResult();
            if (geometry == null)
            {
                result.Error = Resources.EfficiencyMakerNoGeometry;
                return result;
            }

            if (!geometry.IsScintillator)
            {
                result.Error = Resources.EfficiencyMakerGeometryNotScintillator;
                return result;
            }

            int missingZ;
            if (!geometry.Crystal.IsKnown(out missingZ))
            {
                result.Error = string.Format(Resources.EfficiencyMakerGeometryUnknownElement, missingZ);
                return result;
            }

            EfficiencySimulator simulator = new EfficiencySimulator(geometry)
            {
                Histories = Math.Max(1000, histories),
            };

            log(geometry.Describe());
            log(simulator.DescribeScene());
            log(string.Format(CultureInfo.InvariantCulture,
                "{0}: {1}; {2}: {3}; {4}",
                Resources.EfficiencyMakerCrossSections,
                simulator.UsesPartialCrossSections
                    ? Resources.EfficiencyMakerCrossSectionsPartial
                    : Resources.EfficiencyMakerCrossSectionsApprox,
                Resources.EfficiencyMakerBremsstrahlung,
                simulator.ElectronMaterialName.Length > 0
                    ? simulator.ElectronMaterialName
                    : Resources.EfficiencyMakerBremsstrahlungNoData,
                string.Format(CultureInfo.InvariantCulture,
                    Resources.EfficiencyMakerHistories, simulator.Histories)));
            log("");

            foreach (double energy in DefaultEnergies)
            {
                if (cancelled())
                {
                    result.Error = Resources.EfficiencyMakerCancelled;
                    return result;
                }

                double error;
                double efficiency = simulator.Efficiency(energy, out error);
                if (!(efficiency > 0.0) || double.IsNaN(efficiency))
                {
                    continue;
                }

                result.Curve.Add(new ROIEfficiencyData
                {
                    Energy = energy,
                    Efficiency = efficiency,
                    ErrorPercent = error,
                });

                log(string.Format(CultureInfo.InvariantCulture,
                    "    {0,7:F0} keV   eps = {1:E4}   +/- {2:F2} %", energy, efficiency, error));
            }

            if (result.Curve.Count < 2)
            {
                result.Error = Resources.EfficiencyMakerGeometryNoCurve;
                return result;
            }

            result.MinEnergy = result.Curve[0].Energy;
            result.MaxEnergy = result.Curve[result.Curve.Count - 1].Energy;
            result.LevelSource = EfficiencyLevelSource.Simulation;
            return result;
        }
    }
}
