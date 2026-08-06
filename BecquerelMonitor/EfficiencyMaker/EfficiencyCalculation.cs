using BecquerelMonitor.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

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
        /// на каждую точку, <paramref name="cancelled"/> опрашивается перед
        /// каждой точкой: одна точка — это десятки тысяч историй, и прервать
        /// счёт внутри неё нельзя. Точки считаются одновременно, в журнал они
        /// всё равно идут по возрастанию энергии.
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

            // Предупреждения разбора идут в журнал ПЕРЕД сценой и числами:
            // всё, что ниже, посчитано с учётом того, о чём здесь сказано, и
            // прочитать это задним числом уже бесполезно.
            foreach (string warning in geometry.Warnings)
            {
                log(warning);
            }

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

            // Точки кривой считаются ОДНОВРЕМЕННО. Они независимы полностью:
            // сцена и таблицы сечений у каждого счётчика свои и после сборки
            // только читаются, а поток случайных чисел задаётся номером точки
            // (см. ResetStream).
            //
            // Замер (ASN16 в маринелли, 34 точки по 200 000 историй, i7-11800H,
            // 8 ядер / 16 потоков): 171 с в один поток, 24.1 с в несколько —
            // ускорение 7.1x. Два прогона подряд совпадают до последнего знака.
            //
            // Одно ядро оставлено интерфейсу: точек вчетверо больше, чем ядер,
            // на общем времени это не сказывается, а окно не застывает.
            double[] values = new double[DefaultEnergies.Length];
            double[] errors = new double[DefaultEnergies.Length];
            bool[] ready = new bool[DefaultEnergies.Length];
            object gate = new object();
            int printed = 0;

            // Культура выставлена на потоке счёта, а точки пойдут на потоках
            // пула — без переноса строки прогона взялись бы из нейтрального
            // ресурса вместо выбранного языка.
            CultureInfo ui = CultureInfo.CurrentUICulture;
            CultureInfo formatting = CultureInfo.CurrentCulture;
            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            };

            // Точки раздаются ПО ОДНОЙ. Parallel.For сам режет диапазон кусками,
            // а точек всего 34 при цене в секунды: кусок из трёх точек означает,
            // что один поток работает, пока остальные ждут. С раздачей по одной
            // 28.0 с против 24.1 (замер на i7-11800H, 8 ядер).
            Parallel.ForEach(Partitioner.Create(0, DefaultEnergies.Length, 1), options,
                () =>
                {
                    Thread.CurrentThread.CurrentUICulture = ui;
                    Thread.CurrentThread.CurrentCulture = formatting;
                    return new EfficiencySimulator(geometry)
                    {
                        Histories = simulator.Histories,
                        Seed = simulator.Seed,
                    };
                },
                (range, loop, worker) =>
                {
                    if (cancelled())
                    {
                        loop.Stop();
                        return worker;
                    }

                    int index = range.Item1;
                    worker.ResetStream((ulong)worker.Seed
                                       ^ ((ulong)(index + 1) * 0x9E3779B97F4A7C15UL));

                    // Допуск пика — от разрешения прибора, если оно задано в
                    // геометрии: без него поправка на однократное рассеяние
                    // (SingleScatter) не даёт ничего, см. GeometryModel.FwhmAt662Percent.
                    worker.PeakHalfWidthKev = geometry.PeakHalfWidthKev(DefaultEnergies[index]);
                    double error;
                    double efficiency = worker.Efficiency(DefaultEnergies[index], out error);

                    lock (gate)
                    {
                        values[index] = efficiency;
                        errors[index] = error;
                        ready[index] = true;

                        // В журнал точки выливаются ПО ПОРЯДКУ, по мере того как
                        // готов очередной непрерывный кусок: считаются они
                        // вразнобой, а читать кривую вперемешку невозможно.
                        while (printed < ready.Length && ready[printed])
                        {
                            if (values[printed] > 0.0 && !double.IsNaN(values[printed]))
                            {
                                log(string.Format(CultureInfo.InvariantCulture,
                                    "    {0,7:F0} keV   eps = {1:E4}   +/- {2:F2} %",
                                    DefaultEnergies[printed], values[printed], errors[printed]));
                            }

                            printed++;
                        }
                    }

                    return worker;
                },
                worker => { });

            if (cancelled())
            {
                result.Error = Resources.EfficiencyMakerCancelled;
                return result;
            }

            for (int i = 0; i < DefaultEnergies.Length; i++)
            {
                if (!ready[i] || !(values[i] > 0.0) || double.IsNaN(values[i]))
                {
                    continue;
                }

                result.Curve.Add(new ROIEfficiencyData
                {
                    Energy = DefaultEnergies[i],
                    Efficiency = values[i],
                    ErrorPercent = errors[i],
                });
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
