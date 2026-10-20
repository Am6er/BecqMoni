using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace MatrixRecomputeProbe
{
    /// <summary>
    /// ПЕРЕСЧЁТ МАТРИЦ ОТКЛИКА И КРИВЫХ ЭФФЕКТИВНОСТИ ПО КОНФИГУ ПРИБОРОВ — БЕЗ
    /// ОКНА, ТЕМ ЖЕ КОДОМ, ЧТО ОКНО (`AMBER46`/`AMBER47`, полоса П113 19.09.2026).
    ///
    /// ЗАЧЕМ. Склад Amber (`config\device\response\&lt;guid&gt;.rmx`) под физикой 21
    /// не читается ни одной матрицей (П110): 15 — прежний формат, 4 — физика 19.
    /// Пересчитать их окном «Response matrix…» — одиннадцать раз по 5…14 минут
    /// рукой на кнопке; эта проба делает то же по списку и пишет то же, что
    /// написало бы окно: `.rmx` под Guid кривой и точки кривой с клеймом в XML
    /// прибора.
    ///
    /// ⛔ СВОЕГО СЧЁТА ЗДЕСЬ НЕТ. Матрица — `ResponseMatrixBuilder.Build` с
    /// `ResponseMatrixOptions`, где заданы ровно те шесть полей, что задаёт
    /// форма (`ResponseMatrixForm.CurrentOptions`: emin, emax, узлы, бин,
    /// истории, потоки), остальное — умолчания класса, как у формы; запись —
    /// `ResponseMatrix.Save` по пути `&lt;config&gt;\device\response\&lt;guid&gt;.rmx`
    /// (тот же, что складывает `ResponseMatrixStore.PathOf`, только от каталога
    /// ключа `--config=`, а не от каталога сборки). Кривая —
    /// `EfficiencyCalculation.Run` с `EfficiencyCalculationOptions` конструктора
    /// кривой (`EfficiencyMakerForm.CurrentCalcOptions`); в конфигурацию она
    /// кладётся так, как это делает `SaveIntoConfig`: точки клонами, `Origin =
    /// Simulation`, `ComputeStamp` результата, `LastUpdated`. XML прибора
    /// читается и пишется тем же `XmlSerializer(typeof(DeviceConfigInfo))`
    /// через `Utils.AtomicFileWriter`, что у `DeviceConfigManager.SaveConfig`.
    ///
    /// ⛔ ПРОБА ПИШЕТ В `--config=`. Работать ТОЛЬКО с копией конфига: живые
    /// каталоги Amber (`OneDrive\Desktop\Debug\config`, `%AppData%\BecqMoni`) —
    /// только чтение, ни байта туда.
    ///
    ///   matrixrecomputeprobe --config=&lt;каталог config копии&gt;
    ///       (--all | --device=&lt;файл прибора без .xml&gt; --curve=&lt;имя кривой&gt; [--curve=…])
    ///       [--emin=5] [--emax=3000] [--nodes=100] [--bin=2] [--hist=1000000]
    ///       [--curve-emin=5] [--curve-emax=3000] [--curve-points=100] [--curve-hist=1000000]
    ///       [--curve-grid=log|std] [--threads=8] [--no-matrix] [--no-curve]
    ///       [--gpu | --gpu=&lt;rmgpu_f.dll&gt;] [--like-window [--hist-as-spent]] [--curve-like-window]
    ///       [--csv=&lt;итог.csv&gt;]
    ///
    /// (П345, 20.10.2026) `--gpu` — матрица на GPU тем же путём, что окно при галке GPU
    /// (`GpuBuild.Build`, плоско); сцена, которую порт не умеет, — на ЦП, как в окне.
    /// `--like-window` — настройки матрицы как кнопка «Пересчитать» (из прежнего `.rmx`,
    /// истории не ниже штатных 3 млн); `--hist-as-spent` — истории не ниже набранных
    /// прежней матрицей на узел (решение Amber 20.10.2026 для пересчёта её склада на GPU:
    /// «Как набрали прежде (Рекомендую)»). `--curve-like-window` — заказ кривой как окно
    /// кривой при открытии (из клейма прежней кривой, `ApplyCalcOptions`).
    ///   matrixrecomputeprobe --config=&lt;каталог&gt; --verify [--csv=…]
    ///   matrixrecomputeprobe --config=&lt;каталог&gt; --stamps [--emin=… --emax=… --nodes=… --bin=… --hist=…]
    ///
    /// `--all` — все кривые с геометрией во всех `config\device\*.xml`.
    /// Умолчания счёта — НЕ умолчания приложения (140 узлов, 3 млн историй,
    /// `A39`/`T49`), а настройки, которыми считала Amber свои матрицы 15–18.09.2026
    /// (100 узлов 5–3000 кэВ, бин 2, 1 млн историй на узел; кривые — 100 точек
    /// логарифмической сетки 5–3000 по 1 млн историй): решение распорядителя П113
    /// «номинал 3 млн НЕ брать, цена ×3». Годность матрицы от этого не зависит —
    /// клеймо (`ResponseMatrix.ComputeStamp`) считается по геометрии и СВОИМ
    /// настройкам матрицы, и `IsValidFor` сходится при любом их выборе.
    ///
    /// `--verify` — приёмка без счёта: каждая кривая с геометрией → её `.rmx`
    /// читается `ResponseMatrix.Load` (читателем ПРИЛОЖЕНИЯ), печатаются формат,
    /// физика, узлы, истории, `IsValidFor(геометрия кривой)`; код 1, если хоть
    /// одна кривая с геометрией осталась без годной матрицы или клеймо кривой
    /// не физики сборки.
    ///
    /// `--stamps` — только клейма геометрий (`ResponseMatrix.ComputeStamp` при
    /// заданных настройках) по каждой кривой: сравнить два каталога до и после
    /// правки XML и увидеть, у кого клеймо сдвинулось.
    ///
    /// Ход счёта печатается в stdout строками с временем; прогресс матрицы —
    /// раз в 10 % досчитанных узлов (`ResponseMatrixProgress.Percent`).
    /// </summary>
    static class Program
    {
        sealed class Job
        {
            public string DevicePath;
            public DeviceConfigInfo Device;
            public EfficiencyConfigData Curve;
        }

        /// <summary>
        /// Прогресс — В КОНСОЛЬ, без контекста синхронизации: `Progress&lt;T&gt;`
        /// без окна постит в пул, порядок строк не гарантирован, а нам нужна
        /// одна строка на каждые 10 % досчитанных узлов, по порядку.
        /// </summary>
        sealed class ConsoleProgress : IProgress<ResponseMatrixProgress>
        {
            readonly string label;
            readonly Stopwatch watch;
            int nextPercent = 10;
            readonly object gate = new object();

            public ConsoleProgress(string label, Stopwatch watch)
            {
                this.label = label;
                this.watch = watch;
            }

            public void Report(ResponseMatrixProgress p)
            {
                lock (this.gate)
                {
                    if (p.Percent + 1e-9 < this.nextPercent)
                    {
                        return;
                    }

                    while (this.nextPercent <= p.Percent + 1e-9 && this.nextPercent <= 100)
                    {
                        this.nextPercent += 10;
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0} {1}: {2,5:F1} % узлов досчитано ({3}/{4} узлов в работе, последний {5:F1} кэВ), {6:F0} с",
                        Stamp(), this.label, p.Percent, p.StartedNodes, p.TotalNodes,
                        p.LastEnergyKev, this.watch.Elapsed.TotalSeconds));
                }
            }
        }

        static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Поля окна кривой при открытии (`EfficiencyMakerForm.ApplyCalcOptions`): из клейма
        /// `grid=LO-HI keV/N log|std` и `hist=H` прежней кривой — границы, истории, вид сетки;
        /// у логарифмической — прежний заказ точек (`OrderedNodes`: наибольшее n ≤ N, при
        /// котором та же сетка на той же геометрии даёт не больше N узлов). Разбор — тем же
        /// выражением, что `TryParseComputeStamp` окна. Клейма нет — края кривой.
        /// </summary>
        static void ApplyCurveLikeWindow(EfficiencyCalculationOptions calc, EfficiencyConfigData curve)
        {
            string stamp = curve.ComputeStamp ?? "";
            System.Text.RegularExpressions.Match grid = System.Text.RegularExpressions.Regex.Match(stamp,
                @"grid=\s*([0-9.]+)\s*-\s*([0-9.]+)\s*keV\s*/\s*([0-9]+)\s*(std|log)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            double lo, hi, nodes;
            if (grid.Success
                && double.TryParse(grid.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lo)
                && double.TryParse(grid.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out hi)
                && hi > lo)
            {
                calc.MinEnergyKev = lo;
                calc.MaxEnergyKev = hi;
                bool log = string.Equals(grid.Groups[4].Value, "log", StringComparison.OrdinalIgnoreCase);
                calc.GridMode = log ? EfficiencyGridMode.Logarithmic : EfficiencyGridMode.Standard;
                System.Text.RegularExpressions.Match hist = System.Text.RegularExpressions.Regex.Match(
                    stamp, @"hist=\s*([0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                double h;
                if (hist.Success && double.TryParse(hist.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out h)
                    && h > 0.0)
                {
                    calc.Histories = (int)h;
                }

                if (log && double.TryParse(grid.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out nodes)
                    && nodes > 0.0)
                {
                    int computed = (int)Math.Min(nodes, 100000.0);
                    calc.NodeCount = computed;
                    if (computed > 2)
                    {
                        for (int n = computed; n >= 2; n--)
                        {
                            var probe = new EfficiencyCalculationOptions
                            {
                                MinEnergyKev = lo, MaxEnergyKev = hi,
                                GridMode = EfficiencyGridMode.Logarithmic, NodeCount = n,
                            };
                            if (probe.BuildGrid(curve.Geometry, null).Length <= computed)
                            {
                                calc.NodeCount = n;
                                break;
                            }
                        }
                    }
                }

                Console.WriteLine("    кривая как окно: {0}-{1} кэВ, {2}, заказ точек {3}, историй {4} (из клейма «{5}»)",
                                  lo.ToString("R", CultureInfo.InvariantCulture), hi.ToString("R", CultureInfo.InvariantCulture),
                                  log ? "log" : "std", calc.NodeCount, calc.Histories, grid.Value);
                return;
            }

            if (curve.Curve != null && curve.Curve.Count > 0)
            {
                double a = double.MaxValue, b = double.MinValue;
                foreach (ROIEfficiencyData p in curve.Curve)
                {
                    if (p == null || !(p.Energy > 0.0)) continue;
                    a = Math.Min(a, p.Energy);
                    b = Math.Max(b, p.Energy);
                }

                if (b > a)
                {
                    calc.MinEnergyKev = a;
                    calc.MaxEnergyKev = b;
                }
            }

            Console.WriteLine("    кривая как окно: клейма нет — края кривой {0}-{1} кэВ",
                              calc.MinEnergyKev.ToString("R", CultureInfo.InvariantCulture),
                              calc.MaxEnergyKev.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Штатное число историй поля окна матрицы (разметка `ResponseMatrixForm.Layout`, `A39`).</summary>
        const int WindowNominalHistories = 3000000;

        /// <summary>Верх поля историй окна матрицы.</summary>
        const int WindowMaxHistories = 30000000;

        /// <summary>
        /// Поля окна матрицы при «Пересчитать» (`ResponseMatrixForm.LoadExisting`): из
        /// прежнего `.rmx` — границы, узлы, бин; истории — не ниже штатных (`A122`).
        /// Прежнего файла нет — границы кривой (`ApplyCurveRange`), прочее — умолчания.
        /// <paramref name="asSpent"/> — истории не ниже набранных прежней матрицей на
        /// узел, вверх до миллиона и не выше поля окна.
        /// </summary>
        static void ApplyLikeWindow(ResponseMatrixOptions options, EfficiencyConfigData curve, string rmxPath,
                                    bool asSpent)
        {
            var defaults = new ResponseMatrixOptions();
            options.MinEnergyKev = defaults.MinEnergyKev;
            options.MaxEnergyKev = defaults.MaxEnergyKev;
            options.NodeCount = defaults.NodeCount;
            options.BinKev = defaults.BinKev;
            options.Histories = WindowNominalHistories;

            ResponseMatrix old = null;
            if (File.Exists(rmxPath))
            {
                MatrixRefusal refusal;
                int fileFormat;
                old = ResponseMatrix.Load(rmxPath, out refusal, out fileFormat);
            }

            if (old != null && old.Options != null)
            {
                options.MinEnergyKev = old.Options.MinEnergyKev;
                options.MaxEnergyKev = old.Options.MaxEnergyKev;
                options.NodeCount = old.Options.NodeCount;
                options.BinKev = old.Options.BinKev;
                options.Histories = Math.Min(WindowMaxHistories, Math.Max(old.Options.Histories, WindowNominalHistories));
                if (asSpent && old.NodeCount > 0)
                {
                    double perNode = (double)old.HistoriesSpent / old.NodeCount;
                    int rounded = (int)Math.Min(WindowMaxHistories, Math.Ceiling(perNode / 1e6) * 1e6);
                    options.Histories = Math.Max(options.Histories, rounded);
                }
            }
            else if (curve.Curve != null && curve.Curve.Count > 0)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (ROIEfficiencyData p in curve.Curve)
                {
                    if (p == null || !(p.Energy > 0.0)) continue;
                    lo = Math.Min(lo, p.Energy);
                    hi = Math.Max(hi, p.Energy);
                }

                options.MinEnergyKev = lo;
                options.MaxEnergyKev = hi;
            }

            Console.WriteLine("    как окно : {0}-{1} кэВ, узлов {2}, бин {3} кэВ, историй на узел {4}{5}",
                              options.MinEnergyKev.ToString("R", CultureInfo.InvariantCulture),
                              options.MaxEnergyKev.ToString("R", CultureInfo.InvariantCulture),
                              options.NodeCount, options.BinKev.ToString("R", CultureInfo.InvariantCulture),
                              options.Histories,
                              old == null ? " (прежней матрицы нет)"
                                  : string.Format(CultureInfo.InvariantCulture, " (прежняя: {0} на узел заказано, {1:F0} набрано)",
                                                  old.Options != null ? old.Options.Histories : 0,
                                                  old.NodeCount > 0 ? (double)old.HistoriesSpent / old.NodeCount : 0.0));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            string config = null, device = null, csv = null;
            var curveNames = new List<string>();
            bool all = false, verify = false, stamps = false, noMatrix = false, noCurve = false;
            double emin = 5.0, emax = 3000.0, bin = 2.0;
            int nodes = 100, hist = 1000000, threads = 0;
            string gpuPath = null;
            bool likeWindow = false, histAsSpent = false, curveLikeWindow = false;
            double cEmin = 5.0, cEmax = 3000.0;
            int cPoints = 100, cHist = 1000000;
            EfficiencyGridMode cGrid = EfficiencyGridMode.Logarithmic;

            foreach (string a in args)
            {
                if (a == "--all") { all = true; continue; }
                if (a == "--verify") { verify = true; continue; }
                if (a == "--stamps") { stamps = true; continue; }
                if (a == "--no-matrix") { noMatrix = true; continue; }
                if (a == "--no-curve") { noCurve = true; continue; }
                // (П345, 20.10.2026; Amber, консоль, дословно: «Считать на GPU!») Матрица —
                // тем же путём, что окно при поставленной галке GPU: `GpuBuild.Build` плоско.
                // Без пути — библиотека рядом с пробой (`RmGpu.DefaultPath`, как у приложения).
                if (a == "--gpu") { gpuPath = RmGpu.DefaultPath; continue; }
                if (a.StartsWith("--gpu=", StringComparison.Ordinal)) { gpuPath = a.Substring(6); continue; }
                // Настройки матрицы — как кнопка «Пересчитать» окна (`ResponseMatrixForm.LoadExisting`):
                // границы, узлы и бин из прежнего `.rmx`, истории — не ниже штатных 3 млн;
                // прежнего файла нет — границы кривой и умолчания. Ключи --emin/--nodes/… тогда не берутся.
                if (a == "--like-window") { likeWindow = true; continue; }
                // Истории не ниже НАБРАННЫХ прежней матрицей на узел (её ЦП-останов по шуму
                // доходил до 20 млн): GPU считает только плоско, и штатные 3 млн вернули бы шум.
                if (a == "--hist-as-spent") { histAsSpent = true; continue; }
                // Заказ кривой — как окно кривой при открытии (`EfficiencyMakerForm.ApplyCalcOptions`):
                // границы, истории и сетка из клейма прежней кривой, число точек — прежний ЗАКАЗ
                // (`OrderedNodes`), а не длина сетки. Ключи --curve-emin/… тогда не берутся.
                if (a == "--curve-like-window") { curveLikeWindow = true; continue; }
                if (a.StartsWith("--config=", StringComparison.Ordinal)) config = a.Substring(9);
                else if (a.StartsWith("--device=", StringComparison.Ordinal)) device = a.Substring(9);
                else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curveNames.Add(a.Substring(8));
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) emin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) emax = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) bin = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--hist=", StringComparison.Ordinal)) hist = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve-emin=", StringComparison.Ordinal)) cEmin = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve-emax=", StringComparison.Ordinal)) cEmax = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve-points=", StringComparison.Ordinal)) cPoints = int.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve-hist=", StringComparison.Ordinal)) cHist = int.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve-grid=", StringComparison.Ordinal))
                {
                    string g = a.Substring(13);
                    if (g == "log") cGrid = EfficiencyGridMode.Logarithmic;
                    else if (g == "std") cGrid = EfficiencyGridMode.Standard;
                    else { Console.Error.WriteLine("--curve-grid= ждёт log|std"); return 2; }
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (string.IsNullOrEmpty(config) || !Directory.Exists(Path.Combine(config, "device")))
            {
                Console.Error.WriteLine("нужен --config=<каталог config с подкаталогом device>");
                return 2;
            }

            if (!verify && !stamps && !all && (device == null || curveNames.Count == 0))
            {
                Console.Error.WriteLine("нужен --all либо --device=<файл прибора> --curve=<имя кривой>");
                return 2;
            }

            if (threads <= 0)
            {
                threads = Math.Max(1, Environment.ProcessorCount - 1);
            }

            string deviceDir = Path.Combine(config, "device");
            string responseDir = Path.Combine(deviceDir, "response");

            Console.WriteLine("{0} MatrixRecomputeProbe: сборка физики {1}, формат файла {2}",
                              Stamp(), ResponseMatrix.PhysicsVersion, ResponseMatrix.FormatVersion);
            Console.WriteLine("  config : {0}", Path.GetFullPath(config));

            // ---- список работ -------------------------------------------------
            var jobs = new List<Job>();
            var serializer = new XmlSerializer(typeof(DeviceConfigInfo));
            string[] files = Directory.GetFiles(deviceDir, "*.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                string fileKey = Path.GetFileNameWithoutExtension(path);
                if (!all && !verify && !stamps
                    && !string.Equals(fileKey, device, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(path), device, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DeviceConfigInfo info;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        info = (DeviceConfigInfo)serializer.Deserialize(stream);
                    }
                }
                catch (Exception ex)
                {
                    // Прибор старой раскладки (`DeviceConfigInfo_097b`) или чужой файл:
                    // кривых с геометрией у него нет по построению, пропускаем словами.
                    Console.WriteLine("  {0}: не читается как DeviceConfigInfo ({1}) — пропущен",
                                      Path.GetFileName(path), ex.GetType().Name);
                    continue;
                }

                if (info.EfficiencyConfigs == null)
                {
                    continue;
                }

                foreach (EfficiencyConfigData curve in info.EfficiencyConfigs)
                {
                    if (curve == null || !curve.HasGeometry)
                    {
                        continue;
                    }

                    if (!all && !verify && !stamps)
                    {
                        bool wanted = false;
                        foreach (string n in curveNames)
                        {
                            if (string.Equals(n, curve.Name, StringComparison.OrdinalIgnoreCase)) wanted = true;
                        }

                        if (!wanted) continue;
                    }

                    jobs.Add(new Job { DevicePath = path, Device = info, Curve = curve });
                }
            }

            if (jobs.Count == 0)
            {
                Console.Error.WriteLine("ни одной кривой с геометрией под заданные ключи");
                return 2;
            }

            Console.WriteLine("  кривых с геометрией: {0}", jobs.Count);

            if (stamps)
            {
                return PrintStamps(jobs, new ResponseMatrixOptions
                {
                    MinEnergyKev = emin, MaxEnergyKev = emax, NodeCount = nodes, BinKev = bin, Histories = hist
                });
            }

            if (verify)
            {
                return Verify(jobs, responseDir, csv);
            }

            // ---- счёт ---------------------------------------------------------
            // GPU — годность тем же `RmGpu.Probe`, что у окна при открытии; негодная
            // библиотека или карта — отказ до счёта, а не тихий уход на ЦП.
            if (gpuPath != null && !noMatrix)
            {
                GpuProbe gpuProbe = RmGpu.Probe(gpuPath);
                Console.WriteLine("  GPU    : {0} — {1}", gpuPath,
                                  gpuProbe.Available
                                      ? "годен: " + gpuProbe.DeviceName + ", " + gpuProbe.BuildInfo
                                      : "НЕГОДЕН: " + gpuProbe.Reason);
                if (!gpuProbe.Available)
                {
                    return 2;
                }
            }

            if (likeWindow)
            {
                Console.WriteLine("  матрица: как окно — у каждой из прежней матрицы{0}; потоков {1}{2}",
                                  histAsSpent ? ", истории не ниже набранных" : "", threads,
                                  noMatrix ? "  — НЕ СЧИТАЕТСЯ (--no-matrix)" : "");
            }
            else
            {
                Console.WriteLine("  матрица: {0}-{1} кэВ, узлов {2}, бин {3} кэВ, историй на узел {4}, потоков {5}{6}",
                                  emin.ToString("R", CultureInfo.InvariantCulture),
                                  emax.ToString("R", CultureInfo.InvariantCulture), nodes,
                                  bin.ToString("R", CultureInfo.InvariantCulture), hist, threads,
                                  noMatrix ? "  — НЕ СЧИТАЕТСЯ (--no-matrix)" : "");
            }

            if (curveLikeWindow)
            {
                Console.WriteLine("  кривая : как окно — у каждой из клейма прежней кривой{0}",
                                  noCurve ? "  — НЕ СЧИТАЕТСЯ (--no-curve)" : "");
            }
            else
            {
                Console.WriteLine("  кривая : {0}-{1} кэВ, точек {2} ({3}), историй на точку {4}{5}",
                                  cEmin.ToString("R", CultureInfo.InvariantCulture),
                                  cEmax.ToString("R", CultureInfo.InvariantCulture), cPoints,
                                  cGrid == EfficiencyGridMode.Logarithmic ? "log" : "std", cHist,
                                  noCurve ? "  — НЕ СЧИТАЕТСЯ (--no-curve)" : "");
            }
            Console.WriteLine();

            StreamWriter csvOut = null;
            if (csv != null)
            {
                bool fresh = !File.Exists(csv);
                csvOut = new StreamWriter(csv, true, new UTF8Encoding(false));
                if (fresh)
                {
                    csvOut.WriteLine("device;curve;guid;matrix_seconds;matrix_cpu_seconds;matrix_nodes;matrix_hist_spent;matrix_noise_weighted_pct;matrix_stamp;matrix_file_bytes;curve_seconds;curve_points;curve_emin;curve_emax;curve_stamp;rmx_path");
                }

                csvOut.AutoFlush = true;
            }

            int failures = 0;
            int index = 0;
            var total = Stopwatch.StartNew();
            foreach (Job job in jobs)
            {
                index++;
                string label = string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] {2} / {3}",
                                             index, jobs.Count, job.Device.Name, job.Curve.Name);
                Console.WriteLine("{0} == {1} ==  guid {2}", Stamp(), label, job.Curve.Guid);
                Console.WriteLine("    геометрия: {0}", job.Curve.Geometry.Describe().Replace("\r", "").Replace("\n", " | "));
                Console.WriteLine("    клеймо кривой было: {0}", job.Curve.ComputeStamp ?? "");

                double mSeconds = 0.0, mCpu = 0.0, mNoise = 0.0;
                int mNodes = 0;
                long mSpent = 0, mBytes = 0;
                string mStamp = "", rmxPath = Path.Combine(responseDir, job.Curve.Guid + ".rmx");
                if (!noMatrix)
                {
                    try
                    {
                        // Ровно то, что делает `ResponseMatrixForm.ComputeClick`:
                        // клон геометрии, шесть полей настроек, `Build`, потом
                        // `SaveClick` → `ResponseMatrixStore.Save` → `matrix.Save(PathOf(guid))`.
                        GeometryModel geometry = job.Curve.Geometry.Clone();
                        var options = new ResponseMatrixOptions
                        {
                            MinEnergyKev = emin,
                            MaxEnergyKev = emax,
                            NodeCount = nodes,
                            BinKev = bin,
                            Histories = hist,
                            Threads = threads
                        };
                        if (likeWindow)
                        {
                            ApplyLikeWindow(options, job.Curve, rmxPath, histAsSpent);
                        }

                        // GPU — как `ResponseMatrixForm.CurrentOptions` при галке: только
                        // плоско (`ContinuumErrorTarget = 0`); сцену, которую порт не умеет
                        // (`GpuPack.SceneProblem`), окно считает на ЦП — и проба тоже, словами.
                        bool onGpu = gpuPath != null;
                        if (onGpu)
                        {
                            options.ContinuumErrorTarget = 0.0;
                            string sceneProblem = GpuPack.SceneProblem(geometry, options);
                            if (sceneProblem != null)
                            {
                                onGpu = false;
                                options.ContinuumErrorTarget = new ResponseMatrixOptions().ContinuumErrorTarget;
                                Console.WriteLine("    GPU не умеет сцену ({0}) — матрица на ЦП, как в окне", sceneProblem);
                            }
                        }

                        TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
                        var watch = Stopwatch.StartNew();
                        ResponseMatrix matrix;
                        if (onGpu)
                        {
                            using (var gpu = new RmGpu(gpuPath, RmGpu.DefaultStackBytes))
                            {
                                matrix = GpuBuild.Build(gpu, geometry, options, new ConsoleProgress("матрица GPU", watch),
                                                        CancellationToken.None, null);
                            }
                        }
                        else
                        {
                            matrix = ResponseMatrixBuilder.Build(
                                geometry, options, new ConsoleProgress("матрица", watch), CancellationToken.None);
                        }

                        watch.Stop();
                        mCpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
                        mSeconds = watch.Elapsed.TotalSeconds;

                        matrix.Save(rmxPath);
                        mBytes = new FileInfo(rmxPath).Length;
                        mNodes = matrix.NodeCount;
                        mSpent = matrix.HistoriesSpent;
                        mNoise = matrix.ContinuumWeightedError;
                        mStamp = matrix.Stamp;

                        // Контроль на месте: файл читается читателем приложения и
                        // годен для геометрии кривой — иначе счёт был зря.
                        MatrixRefusal refusal;
                        int fileFormat;
                        ResponseMatrix back = ResponseMatrix.Load(rmxPath, out refusal, out fileFormat);
                        bool valid = back != null && back.IsValidFor(job.Curve.Geometry);
                        Console.WriteLine("    матрица  : {0:F1} с на часах, {1:F1} с ЦП (ядер {2:F1}), узлов {3}, историй потрачено {4}, шум конт. взвеш. {5:F2} %",
                                          mSeconds, mCpu, mSeconds > 0 ? mCpu / mSeconds : 0.0, mNodes, mSpent, mNoise);
                        Console.WriteLine("    записана : {0} ({1} байт), формат {2}, обратно: {3}",
                                          rmxPath, mBytes, fileFormat,
                                          valid ? "читается, IsValidFor = годна" : "НЕГОДНА (" + refusal + ")");
                        Console.WriteLine("    клеймо   : {0}", mStamp);
                        if (!valid)
                        {
                            failures++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine("    МАТРИЦА НЕ ПОСЧИТАНА: {0}: {1}", ex.GetType().Name, ex.Message);
                    }
                }

                double cSeconds = 0.0;
                int cCount = 0;
                double cLo = 0.0, cHi = 0.0;
                string cStamp = "";
                if (!noCurve)
                {
                    try
                    {
                        // Ровно то, что делает конструктор кривой: `CurrentCalcOptions`
                        // → `EfficiencyCalculation.Run(model, options, log, cancelled)`,
                        // потом `SaveIntoConfig`.
                        var calc = new EfficiencyCalculationOptions
                        {
                            MinEnergyKev = cEmin,
                            MaxEnergyKev = cEmax,
                            GridMode = cGrid,
                            NodeCount = cPoints,
                            Histories = cHist,
                            Threads = threads
                        };
                        if (curveLikeWindow)
                        {
                            ApplyCurveLikeWindow(calc, job.Curve);
                        }

                        var watch = Stopwatch.StartNew();
                        var lines = new List<string>();
                        EfficiencyFitResult result = EfficiencyCalculation.Run(
                            job.Curve.Geometry.Clone(), calc, lines.Add, () => false);
                        watch.Stop();
                        cSeconds = watch.Elapsed.TotalSeconds;
                        if (!result.Ok)
                        {
                            failures++;
                            Console.WriteLine("    КРИВАЯ НЕ ПОСЧИТАНА: {0}", result.Error);
                            foreach (string l in lines) Console.WriteLine("      | " + l);
                        }
                        else
                        {
                            var points = new List<ROIEfficiencyData>();
                            foreach (ROIEfficiencyData point in result.Curve)
                            {
                                points.Add(point.Clone());
                            }

                            job.Curve.Curve = points;
                            job.Curve.Origin = EfficiencyOrigin.Simulation;
                            job.Curve.ComputeStamp = result.ComputeStamp ?? "";
                            job.Curve.LastUpdated = DateTime.Now;
                            cCount = result.Curve.Count;
                            cLo = result.MinEnergy;
                            cHi = result.MaxEnergy;
                            cStamp = job.Curve.ComputeStamp;

                            // Строки журнала расчёта, кроме поточечных: сцена,
                            // сетка, разброс на узел — то, что видит человек в
                            // окне конструктора.
                            foreach (string l in lines)
                            {
                                if (l.Length == 0 || l.Contains("eps =")) continue;
                                Console.WriteLine("      | " + l.Replace("\r", "").Replace("\n", " | "));
                            }

                            Console.WriteLine("    кривая   : {0:F1} с, точек {1} ({2:0.#}-{3:0.#} кэВ)", cSeconds, cCount, cLo, cHi);
                            Console.WriteLine("    клеймо   : {0}", cStamp);

                            // Запись — как `DeviceConfigManager.SaveConfig`: `LastUpdated`
                            // прибора и тот же сериализатор через `AtomicFileWriter`.
                            job.Device.LastUpdated = DateTime.Now;
                            BecquerelMonitor.Utils.AtomicFileWriter.Write(job.DevicePath, stream =>
                            {
                                new XmlSerializer(typeof(DeviceConfigInfo)).Serialize(stream, job.Device);
                            });
                            Console.WriteLine("    XML      : {0} записан ({1} байт)", job.DevicePath, new FileInfo(job.DevicePath).Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine("    КРИВАЯ НЕ ПОСЧИТАНА: {0}: {1}", ex.GetType().Name, ex.Message);
                    }
                }

                if (csvOut != null)
                {
                    csvOut.WriteLine(string.Join(";", new[]
                    {
                        Csv(job.Device.Name), Csv(job.Curve.Name), job.Curve.Guid,
                        mSeconds.ToString("F1", CultureInfo.InvariantCulture),
                        mCpu.ToString("F1", CultureInfo.InvariantCulture),
                        mNodes.ToString(CultureInfo.InvariantCulture),
                        mSpent.ToString(CultureInfo.InvariantCulture),
                        mNoise.ToString("F3", CultureInfo.InvariantCulture),
                        Csv(mStamp), mBytes.ToString(CultureInfo.InvariantCulture),
                        cSeconds.ToString("F1", CultureInfo.InvariantCulture),
                        cCount.ToString(CultureInfo.InvariantCulture),
                        cLo.ToString("R", CultureInfo.InvariantCulture),
                        cHi.ToString("R", CultureInfo.InvariantCulture),
                        Csv(cStamp), Csv(rmxPath)
                    }));
                }

                Console.WriteLine("{0}    итого по кривой {1:F0} с; всего с начала {2:F0} с", Stamp(),
                                  mSeconds + cSeconds, total.Elapsed.TotalSeconds);
                Console.WriteLine();
            }

            if (csvOut != null)
            {
                csvOut.Dispose();
            }

            Console.WriteLine("{0} ГОТОВО: кривых {1}, отказов {2}, всего {3:F0} с", Stamp(), jobs.Count, failures,
                              total.Elapsed.TotalSeconds);
            return failures == 0 ? 0 : 1;
        }

        static int PrintStamps(List<Job> jobs, ResponseMatrixOptions options)
        {
            Console.WriteLine("  настройки клейма: emin={0} emax={1} nodes={2} bin={3} hist={4}",
                              options.MinEnergyKev.ToString("R", CultureInfo.InvariantCulture),
                              options.MaxEnergyKev.ToString("R", CultureInfo.InvariantCulture),
                              options.NodeCount, options.BinKev.ToString("R", CultureInfo.InvariantCulture),
                              options.Histories);
            foreach (Job job in jobs)
            {
                string stamp = ResponseMatrix.ComputeStamp(job.Curve.Geometry, options);
                Console.WriteLine("STAMP\t{0}\t{1}\t{2}\tgap={3} мм/{4} мм {5}\t{6}",
                                  job.Device.Name, job.Curve.Name, job.Curve.Guid,
                                  job.Curve.Geometry.FrontGapThickness.ToString("R", CultureInfo.InvariantCulture),
                                  job.Curve.Geometry.SideGapThickness.ToString("R", CultureInfo.InvariantCulture),
                                  job.Curve.Geometry.Gap != null ? job.Curve.Geometry.Gap.Name : "-",
                                  stamp);
            }

            return 0;
        }

        static int Verify(List<Job> jobs, string responseDir, string csv)
        {
            int bad = 0;
            StreamWriter w = csv != null ? new StreamWriter(csv, false, new UTF8Encoding(false)) : null;
            if (w != null)
            {
                w.WriteLine("device;curve;guid;rmx_present;format;phys;nodes;emin;emax;histories;hist_spent;noise_weighted_pct;valid_for_curve;curve_stamp;curve_phys;matrix_stamp;created_utc;build_seconds;bytes");
            }

            Console.WriteLine();
            Console.WriteLine("{0,-28} {1,-16} {2,-10} {3,4} {4,4} {5,5} {6,9} {7,-8} {8}",
                              "прибор", "кривая", "guid", "форм", "phys", "узлов", "историй", "годна", "клеймо кривой");
            Console.WriteLine(new string('-', 120));
            foreach (Job job in jobs)
            {
                string path = Path.Combine(responseDir, job.Curve.Guid + ".rmx");
                bool present = File.Exists(path);
                ResponseMatrix m = null;
                MatrixRefusal refusal = MatrixRefusal.NoFile;
                int fileFormat = 0;
                if (present)
                {
                    try { m = ResponseMatrix.Load(path, out refusal, out fileFormat); }
                    catch (Exception ex) { Console.WriteLine("  {0}: НЕ ЧИТАЕТСЯ — {1}", path, ex.Message); }
                }

                bool valid = m != null && m.IsValidFor(job.Curve.Geometry);
                int phys = m != null ? ResponseMatrix.PhysicsFromStamp(m.Stamp) : 0;
                int curvePhys = ResponseMatrix.PhysicsFromStamp(job.Curve.ComputeStamp ?? "");
                bool curveOk = curvePhys == ResponseMatrix.PhysicsVersion;
                if (!valid || !curveOk)
                {
                    bad++;
                }

                string verdict = !present ? "НЕТ ФАЙЛА"
                    : m == null ? "ОТКАЗ " + refusal + " (формат " + fileFormat + ")"
                    : valid ? "годна" : "НЕГОДНА";
                Console.WriteLine("{0,-28} {1,-16} {2,-10} {3,4} {4,4} {5,5} {6,9} {7,-8} {8}{9}",
                                  Trim(job.Device.Name, 28), Trim(job.Curve.Name, 16),
                                  job.Curve.Guid.Length > 8 ? job.Curve.Guid.Substring(0, 8) + "…" : job.Curve.Guid,
                                  m != null ? ResponseMatrix.FormatVersion : fileFormat, phys,
                                  m != null ? m.NodeCount : 0, m != null ? m.Histories : 0, verdict,
                                  curvePhys == 0 ? "(без клейма)" : "phys=" + curvePhys,
                                  curveOk ? "" : "  ← КРИВАЯ НЕ ФИЗИКИ " + ResponseMatrix.PhysicsVersion);
                if (w != null)
                {
                    w.WriteLine(string.Join(";", new[]
                    {
                        Csv(job.Device.Name), Csv(job.Curve.Name), job.Curve.Guid, present ? "1" : "0",
                        (m != null ? ResponseMatrix.FormatVersion : fileFormat).ToString(CultureInfo.InvariantCulture),
                        phys.ToString(CultureInfo.InvariantCulture),
                        (m != null ? m.NodeCount : 0).ToString(CultureInfo.InvariantCulture),
                        m != null && m.Options != null ? m.Options.MinEnergyKev.ToString("R", CultureInfo.InvariantCulture) : "",
                        m != null && m.Options != null ? m.Options.MaxEnergyKev.ToString("R", CultureInfo.InvariantCulture) : "",
                        (m != null ? m.Histories : 0).ToString(CultureInfo.InvariantCulture),
                        (m != null ? m.HistoriesSpent : 0).ToString(CultureInfo.InvariantCulture),
                        m != null ? m.ContinuumWeightedError.ToString("F3", CultureInfo.InvariantCulture) : "",
                        valid ? "1" : "0", Csv(job.Curve.ComputeStamp ?? ""),
                        curvePhys.ToString(CultureInfo.InvariantCulture),
                        m != null ? Csv(m.Stamp) : "",
                        m != null ? m.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "",
                        m != null ? m.BuildSeconds.ToString("F1", CultureInfo.InvariantCulture) : "",
                        present ? new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture) : "0"
                    }));
                }
            }

            if (w != null)
            {
                w.Dispose();
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "ВСЕ ГОДНЫ: у каждой кривой с геометрией матрица читается и IsValidFor сходится, клейма кривых физики " + ResponseMatrix.PhysicsVersion
                : "НЕГОДНЫХ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Поле csv с разделителем `;`: клейма НЕСУТ `;` (`phys=21;<sha256>`,
        /// `phys=21; hist=…; grid=…`), и без кавычек столбцы разъезжаются —
        /// наступлено на первом же прогоне (П113): `curve_seconds` читался из
        /// середины клейма. Кавычки по RFC 4180, внутренняя кавычка удваивается.
        /// </summary>
        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0
                ? s
                : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }
    }
}
