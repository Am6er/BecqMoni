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
    /// Низ шкалы слабее: когерентное рассеяние в кристалле не выделено (вылет
    /// характеристического K-рентгена кристалла моделируется —
    /// `EfficiencySimulator.XrayEscape`, включён умолчанием). Поэтому сетка
    /// начинается с 40 кэВ, но доверия к первым точкам меньше, чем к середине.
    /// </summary>
    /// <summary>Как разложены узлы сетки энергий.</summary>
    public enum EfficiencyGridMode
    {
        /// <summary>
        /// Штатная сетка <see cref="EfficiencyCalculation.DefaultEnergies"/>,
        /// обрезанная выбранным диапазоном.
        /// </summary>
        Standard,

        /// <summary>Равномерная по логарифму энергии, заданное число узлов.</summary>
        Logarithmic
    }

    /// <summary>
    /// Параметры расчёта кривой из геометрии — цена счёта и сетка, на которой
    /// он ведётся. Физики здесь нет намеренно: ключи переноса
    /// (<see cref="EfficiencySimulator"/>) калиброваны сверкой с Geant4 и новой
    /// TCCFCALC, и выведенные наружу свободными числами они превратили бы
    /// абсолютный уровень кривой в подгоночный.
    /// </summary>
    public sealed class EfficiencyCalculationOptions
    {
        /// <summary>
        /// Историй на узел кривой. Погрешность идёт как 1/√N: на 200 тысячах
        /// это около процента в середине шкалы и несколько процентов на её
        /// верху, где эффективность мала. Больше смысла имеет мало —
        /// систематика модели крупнее.
        /// </summary>
        public int Histories = 200000;

        public double MinEnergyKev = 40.0;

        public double MaxEnergyKev = 3000.0;

        public EfficiencyGridMode GridMode = EfficiencyGridMode.Standard;

        /// <summary>Узлов при логарифмической сетке; штатная считает их сама.</summary>
        public int NodeCount = 34;

        /// <summary>Потоков; 0 — по числу ядер минус один.</summary>
        public int Threads;

        /// <summary>Потоков на самом деле.</summary>
        public int EffectiveThreads
        {
            get
            {
                return this.Threads > 0
                    ? this.Threads
                    : Math.Max(1, Environment.ProcessorCount - 1);
            }
        }

        /// <summary>
        /// Узлы сетки, кэВ. Диапазон в полях — это диапазон СЧЁТА, и штатная
        /// сетка обязана его покрыть: узлы <see cref="EfficiencyCalculation.DefaultEnergies"/>
        /// внутри диапазона берутся как есть — они стоят там, где стоят, по
        /// причине (изгиб кривой внизу шкалы, рабочие линии), и раздвигать их
        /// значило бы получить другую сетку под тем же именем, — а за их краями
        /// сетка ПРОДОЛЖАЕТСЯ (см. <see cref="Reach"/>).
        ///
        /// Прежде диапазон штатную сетку только ОБРЕЗАЛ: выставленные 20 кэВ
        /// считались от 40, выставленные 5000 — до 3000, и увидеть это можно
        /// было только в журнале прогона, задним числом (E16). Число в поле,
        /// которым не считают, читается как обещание.
        ///
        /// Если внутри диапазона штатных узлов не осталось двух, сетка молча
        /// становится логарифмической: пустой ответ здесь хуже.
        /// </summary>
        public double[] BuildGrid()
        {
            double lo = Math.Max(1.0, this.MinEnergyKev);
            double hi = Math.Max(lo * 1.01, this.MaxEnergyKev);

            if (this.GridMode == EfficiencyGridMode.Standard)
            {
                List<double> picked = new List<double>();
                foreach (double energy in EfficiencyCalculation.DefaultEnergies)
                {
                    if (energy >= lo && energy <= hi)
                    {
                        picked.Add(energy);
                    }
                }

                if (picked.Count >= 2)
                {
                    Reach(picked, lo, hi);
                    return picked.ToArray();
                }
            }

            int n = Math.Max(2, this.NodeCount);
            double[] grid = new double[n];
            double logLo = Math.Log(lo), logHi = Math.Log(hi);
            for (int i = 0; i < n; i++)
            {
                grid[i] = Math.Exp(logLo + (logHi - logLo) * i / (n - 1));
            }

            return grid;
        }

        public EfficiencyCalculationOptions Clone()
        {
            return (EfficiencyCalculationOptions)this.MemberwiseClone();
        }
    }

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
        /// Прежний вход с одним числом историй — остальное по умолчанию.
        /// Держится ради проб (`tools/effmaker/probes`), у которых своего UI
        /// нет и настраивать им нечего.
        /// </summary>
        public static EfficiencyFitResult Run(GeometryModel geometry, int histories,
                                              Action<string> log, Func<bool> cancelled)
        {
            return Run(geometry, new EfficiencyCalculationOptions { Histories = histories },
                       log, cancelled);
        }

        /// <summary>
        /// Считает кривую по геометрии. <paramref name="log"/> получает строку
        /// на каждую точку, <paramref name="cancelled"/> опрашивается перед
        /// каждой точкой: одна точка — это десятки тысяч историй, и прервать
        /// счёт внутри неё нельзя. Точки считаются одновременно, в журнал они
        /// всё равно идут по возрастанию энергии.
        /// </summary>
        public static EfficiencyFitResult Run(GeometryModel geometry,
                                              EfficiencyCalculationOptions options,
                                              Action<string> log, Func<bool> cancelled)
        {
            if (options == null)
            {
                options = new EfficiencyCalculationOptions();
            }

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

            double[] energies = options.BuildGrid();
            EfficiencySimulator simulator = new EfficiencySimulator(geometry)
            {
                Histories = Math.Max(1000, options.Histories),
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

            // Сетка и потоки — в журнал вместе со всем прочим, чем посчитано:
            // кривая уходит в конфигурацию прибора одними числами, и по ней
            // самой уже не сказать, на скольких узлах она получена.
            log(string.Format(CultureInfo.CurrentCulture, Resources.EfficiencyMakerGridSummary,
                              energies.Length, energies[0], energies[energies.Length - 1],
                              options.GridMode == EfficiencyGridMode.Standard
                                  ? Resources.EfficiencyMakerGridStandard
                                  : Resources.EfficiencyMakerGridLogarithmic,
                              options.EffectiveThreads));
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
            // Одно ядро по умолчанию оставлено интерфейсу: точек вчетверо
            // больше, чем ядер, на общем времени это не сказывается, а окно не
            // застывает. Число потоков можно задать в форме — результат от него
            // не зависит: зерно берётся от НОМЕРА точки, а не от порядка
            // выполнения (см. ResetStream ниже).
            double[] values = new double[energies.Length];
            double[] errors = new double[energies.Length];
            bool[] ready = new bool[energies.Length];
            object gate = new object();
            int printed = 0;

            // Культура выставлена на потоке счёта, а точки пойдут на потоках
            // пула — без переноса строки прогона взялись бы из нейтрального
            // ресурса вместо выбранного языка.
            CultureInfo ui = CultureInfo.CurrentUICulture;
            CultureInfo formatting = CultureInfo.CurrentCulture;
            ParallelOptions parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.EffectiveThreads,
            };

            // Точки раздаются ПО ОДНОЙ. Parallel.For сам режет диапазон кусками,
            // а точек всего 34 при цене в секунды: кусок из трёх точек означает,
            // что один поток работает, пока остальные ждут. С раздачей по одной
            // 28.0 с против 24.1 (замер на i7-11800H, 8 ядер).
            Parallel.ForEach(Partitioner.Create(0, energies.Length, 1), parallel,
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
                    worker.PeakHalfWidthKev = geometry.PeakHalfWidthKev(energies[index]);
                    double error;
                    double efficiency = worker.Efficiency(energies[index], out error);

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
                                // Энергия с десятой долей: у логарифмической
                                // сетки узлы не круглые, и округление до целых
                                // печатало бы не ту энергию, на которой считано.
                                log(string.Format(CultureInfo.InvariantCulture,
                                    "    {0,9:F1} keV   eps = {1:E4}   +/- {2:F2} %",
                                    energies[printed], values[printed], errors[printed]));
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

            for (int i = 0; i < energies.Length; i++)
            {
                if (!ready[i] || !(values[i] > 0.0) || double.IsNaN(values[i]))
                {
                    continue;
                }

                result.Curve.Add(new ROIEfficiencyData
                {
                    Energy = energies[i],
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

            // Клеймо «чем посчитана» (E12): без него кривая в конфигурации
            // прибора неотличима от посчитанной другой физикой. Версия физики
            // переноса — та же константа, что у матрицы отклика: перенос один.
            // Формат инвариантный: клеймо хранится и сравнивается, а
            // локализованная строка расползалась бы по языкам.
            result.ComputeStamp = string.Format(CultureInfo.InvariantCulture,
                "phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}",
                ResponseMatrix.PhysicsVersion, simulator.Histories,
                result.MinEnergy, result.MaxEnergy, result.Curve.Count,
                options.GridMode == EfficiencyGridMode.Standard ? "std" : "log");
            return result;
        }
    }
}
