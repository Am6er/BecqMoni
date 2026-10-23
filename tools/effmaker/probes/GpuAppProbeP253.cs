using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// П253 (13–14.10.2026): ревизия GPU-пути матрицы отклика В ПРИЛОЖЕНИИ — то, чего
// сборка склада (`CorpusMatrixProbe --gpu`) не видит: отмена, отказ посреди счёта,
// сцена, которую GPU-порт не умеет, и что после всего этого остаётся работать.
//
// Режимы (`--geometry=<.in>` обязателен; `--n=` историй на узел, `--nodes=` узлов):
//
//   --mode=cancel --path=gpu|cpu --cancel-at=<с>
//       Счёт тем же входом, что у окна (`GpuBuild.Build` / `ResponseMatrixBuilder.Build`),
//       отмена через <с> секунд. Печатается: сколько ждали от `Cancel()` до выхода
//       построителя, чем он вышел, и что было ПОСЛЕ выхода за `--tail=` секунд —
//       отчёты хода и процессорное время процесса (оставшаяся работа видна им).
//
//   --mode=fail --fail-after=<k>
//       GPU-счёт; на k-м досчитанном узле отчёт хода БРОСАЕТ на потоке узлов — это тот
//       же путь, по которому уходит отказ устройства (`rm_run` → исключение). Дальше —
//       то же, что у отмены: сколько ждали, чем вышел, что осталось после.
//
//   --mode=iso
//       GPU-счёт сцены «изотропное поле» (`GeometryScenes.Iso` от данной геометрии):
//       порт её не умеет и обязан отказать; что осталось после отказа.
//
//   --mode=kappa | --mode=kappacancel --cancel-at=<с>
//       Таблица κ пар отдельно (`MeasureJointTable`): отпечаток таблицы (правка опроса
//       отмены обязана оставить его побитово) / ожидание отмены посреди блоков κ.
//
//   --mode=probe[=<путь к dll>]   годность GPU в СВОЁМ процессе (`RmGpu.Probe`), с замером времени;
//   --mode=isolated[=<путь к dll>] годность GPU дочерним процессом, как её спрашивает окно
//                                 (`RmGpu.ProbeIsolated`, `AMBER261`): падающая библиотека
//                                 роняет ребёнка, а проба печатает код его выхода;
//   --mode=form                   окно матрицы над геометрией и её ISO-двойником: галка GPU;
//   --mode=mkscene=ground|borehole|iso   записать сцену редактора рядом с геометрией.
//
// Код возврата: 0 — прошло (после выхода построителя нет ни отчётов хода, ни счёта на
// ЦП, и выход — той природы, что ждали); 1 — нет; 2 — ключи.
static class GpuAppProbeP253
{
    sealed class Sink : IProgress<ResponseMatrixProgress>
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public long Count, JointCount;
        public int BuildThread = -1;
        public int FailAfter = -1;
        public bool Failed;
        public double LastJointDoneAt = -1;

        public void Report(ResponseMatrixProgress p)
        {
            Interlocked.Increment(ref this.Count);
            if (Thread.CurrentThread.ManagedThreadId != this.BuildThread)
            {
                Interlocked.Increment(ref this.JointCount);
            }

            if (this.FailAfter >= 0 && !this.Failed && p.SettledNodes >= this.FailAfter
                && Thread.CurrentThread.ManagedThreadId == this.BuildThread)
            {
                this.Failed = true;
                throw new InvalidOperationException("П253: отказ устройства (инъекция) на узле " + p.SettledNodes.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>
    /// `--mode=form`: окно матрицы над данной геометрией и над её ISO-двойником — какой
    /// стала галка «Use Nvidia GPU» (годна / недоступна) и что в её подсказке. Окно не
    /// показывается: читаются поля формы отражением, как у `ResponseMatrixFormProbe`.
    /// </summary>
    static int FormCheck(string geometryPath)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        int bad = 0;
        foreach (bool iso in new[] { false, true })
        {
            GeometryModel g = GeometryModel.Load(geometryPath);
            if (iso) GeometryScenes.Iso(g);
            var config = new EfficiencyConfigData("проба П253") { Guid = "probe-p253-" + Guid.NewGuid().ToString("N"), Geometry = g };
            using (var form = new ResponseMatrixForm(config))
            {
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                object check = typeof(ResponseMatrixForm).GetField("gpuCheck", F).GetValue(form);
                var tip = (System.Windows.Forms.ToolTip)typeof(ResponseMatrixForm).GetField("gpuTip", F).GetValue(form);
                var probe = (GpuProbe)typeof(ResponseMatrixForm).GetField("gpuProbe", F).GetValue(form);
                bool unavailable = (bool)check.GetType().GetProperty("Unavailable").GetValue(check, null);
                string text = tip.GetToolTip((System.Windows.Forms.Control)check);
                bool want = iso || !probe.Available;
                // Причина устройства старше причины сцены: без годного устройства ISO не называется.
                bool ok = unavailable == want && (!iso || !probe.Available || text.IndexOf("ISO", StringComparison.Ordinal) >= 0);
                bad += ok ? 0 : 1;
                Console.WriteLine("{0} {1}: устройство годно {2}, галка {3}; подсказка «{4}»",
                                  ok ? "✓" : "✗", iso ? "ISO-двойник" : "геометрия", probe.Available,
                                  unavailable ? "НЕДОСТУПНА" : "годна", text);
            }
        }

        Console.WriteLine(bad == 0 ? "ПРОШЛО" : "НЕ ПРОШЛО");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>sha256 строк всех каналов (биты float подряд) — сверка «то же тело» без записи файла.</summary>
    static string RowsHash(ResponseMatrix m)
    {
        var bytes = new List<byte>();
        foreach (float[][] channel in m.ChannelRows)
            foreach (float[] row in channel)
            {
                bytes.AddRange(BitConverter.GetBytes(row == null ? -1 : row.Length));
                if (row != null) foreach (float v in row) bytes.AddRange(BitConverter.GetBytes(v));
            }

        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "");
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = null, path = "gpu", geometryPath = null;
        double cancelAt = 5.0, tail = 8.0;
        int failAfter = 3, n = 300000, nodes = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
            else if (a.StartsWith("--path=", StringComparison.Ordinal)) path = a.Substring(7);
            else if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--cancel-at=", StringComparison.Ordinal)) cancelAt = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tail=", StringComparison.Ordinal)) tail = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--fail-after=", StringComparison.Ordinal)) failAfter = int.Parse(a.Substring(13), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (mode != null && mode.StartsWith("probe", StringComparison.Ordinal))
        {
            // Годность GPU так, как её спрашивает окно матрицы при открытии (`RmGpu.Probe`),
            // с замером времени — холодный процесс, первый вызов CUDA. `--mode=probe=<путь>`
            // — библиотека по другому пути (отказ «нет библиотеки» / чужая сборка).
            string dll = mode.Length > 6 ? mode.Substring(6) : RmGpu.DefaultPath;
            var clock = Stopwatch.StartNew();
            GpuProbe p = RmGpu.Probe(dll);
            double first = clock.Elapsed.TotalSeconds;
            clock.Restart();
            GpuProbe again = RmGpu.Probe(dll);
            double second = clock.Elapsed.TotalSeconds;
            Console.WriteLine("Probe({0}): годен {1}, код {2}, причина «{3}», устройство «{4}» cc {5}, драйвер {6}, рантайм {7}, паспорт «{8}»",
                              dll, p.Available, p.ReasonCode, p.Reason, p.DeviceName, p.ComputeCapability,
                              p.DriverVersion, p.RuntimeVersion, p.BuildInfo);
            Console.WriteLine("  время: первый вызов {0:F3} с, повторный {1:F3} с; повторный годен {2}", first, second, again.Available);
            return 0;
        }

        if (mode != null && mode.StartsWith("isolated", StringComparison.Ordinal))
        {
            string dll = mode.Length > 9 ? mode.Substring(9) : RmGpu.DefaultPath;
            var clock = Stopwatch.StartNew();
            GpuProbe p = RmGpu.ProbeIsolated(dll);
            Console.WriteLine("ProbeIsolated({0}): годен {1}, код {2}, выход ребёнка {3}, причина «{4}», устройство «{5}» cc {6}, драйвер {7}, рантайм {8}, паспорт «{9}»; {10:F2} с",
                              dll, p.Available, p.ReasonCode, RmGpu.ExitCodeText(p.ChildExitCode), p.Reason, p.DeviceName,
                              p.ComputeCapability, p.DriverVersion, p.RuntimeVersion, p.BuildInfo, clock.Elapsed.TotalSeconds);
            return 0;
        }

        if (mode == null || geometryPath == null)
        {
            Console.Error.WriteLine("нужны --mode= и --geometry=");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        if (mode == "form")
        {
            return FormCheck(geometryPath);
        }

        GeometryModel geometry = GeometryModel.Load(geometryPath);
        if (mode.StartsWith("mkscene=", StringComparison.Ordinal))
        {
            // Сцена, которой нет в корпусе (поле, лунка, ISO), — из данной геометрии
            // правилом редактора (`GeometryScenes.Apply`, верх 3000 кэВ), в файл `.in`
            // рядом с исходной: для сверки CPU и GPU (`CorpusMatrixProbe --gpu-check`).
            string kind = mode.Substring(8);
            string substituted = kind == "ground" ? GeometryScenes.Ground(geometry, 3000.0)
                               : kind == "borehole" ? GeometryScenes.Borehole(geometry, 3000.0)
                               : kind == "iso" ? GeometryScenes.Iso(geometry)
                               : null;
            if (substituted == null) { Console.Error.WriteLine("сцена: ground|borehole|iso"); return 2; }
            string outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(geometryPath)),
                                          Path.GetFileNameWithoutExtension(geometryPath) + "_" + kind + ".in");
            GeometryWriter.Save(geometry, outPath);
            Console.WriteLine("сцена {0} → {1} (подставлено вещество: «{2}»)", kind, outPath, substituted);
            return 0;
        }

        if (mode == "kappa" || mode == "kappacancel")
        {
            // Таблица κ пар отдельно от узлов: `MeasureJointTable` тем же входом, что у
            // построителя (`BuildJoint`), сетка — `BuildGrid` умолчаний. `kappa` печатает
            // отпечаток таблицы (биты κ и шума, число точек) — правка опроса отмены обязана
            // оставить его ПОБИТОВО; `kappacancel` — отмена через `--cancel-at=` и ожидание.
            var kOptions = new ResponseMatrixOptions();
            if (nodes > 0) kOptions.NodeCount = nodes;
            double[] kGrid = kOptions.BuildGrid(geometry);
            var kCts = new CancellationTokenSource();
            var kParallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = kCts.Token
            };
            var clock = Stopwatch.StartNew();
            ResponseMatrixBuilder.JointTable table = null;
            Exception kOutcome = null;
            var kTask = Task.Run(() =>
            {
                try { table = ResponseMatrixBuilder.MeasureJointTable(geometry, kOptions, kGrid, kParallel); }
                catch (Exception e) { kOutcome = e; }
            });
            double kCancelled = -1;
            if (mode == "kappacancel" && !kTask.Wait(TimeSpan.FromSeconds(cancelAt)))
            {
                kCancelled = clock.Elapsed.TotalSeconds;
                kCts.Cancel();
            }

            kTask.Wait();
            double kEnd = clock.Elapsed.TotalSeconds;
            if (mode == "kappa")
            {
                if (table == null)
                {
                    Console.WriteLine("κ: таблицы нет ({0})", kOutcome == null ? "null" : kOutcome.GetType().Name + ": " + kOutcome.Message);
                    return 1;
                }

                var bytes = new List<byte>();
                foreach (double[] row in table.Kappa) foreach (double v in row) bytes.AddRange(BitConverter.GetBytes(v));
                foreach (double[] row in table.Error) foreach (double v in row) bytes.AddRange(BitConverter.GetBytes(v));
                bytes.AddRange(BitConverter.GetBytes(table.Points));
                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    hash = BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
                }

                Console.WriteLine("κ: {0} с, точек {1}, режим {2}, медиана шума {3:F3} %, отпечаток {4}",
                                  kEnd.ToString("F1", CultureInfo.InvariantCulture), table.Points, table.Mode,
                                  table.RawMedianNoise, hash);
                return 0;
            }

            Console.WriteLine("κ отмена: отменили на {0:F2} с, вышли на {1:F2} с — ожидание {2:F2} с; выход {3}",
                              kCancelled, kEnd, kCancelled >= 0 ? kEnd - kCancelled : -1.0,
                              kOutcome == null ? "таблица (отмена не успела)" : kOutcome.GetType().Name);
            return kOutcome is OperationCanceledException ? 0 : 1;
        }

        if (mode.StartsWith("repeat=", StringComparison.Ordinal))
        {
            // Утечки за жизнь процесса: окно берёт `RmGpu` на каждый счёт и отдаёт его
            // (`rm_shutdown` → `cudaDeviceReset`). k счётов подряд в одном процессе —
            // дескрипторы, частная память процесса и занятая память устройства после каждого.
            int count = int.Parse(mode.Substring(7), CultureInfo.InvariantCulture);
            var rOptions = new ResponseMatrixOptions { Histories = n, ContinuumErrorTarget = 0.0, JointNodes = 0 };
            if (nodes > 0) rOptions.NodeCount = nodes;
            string firstHash = null;
            for (int k = 0; k < count; k++)
            {
                var clock = Stopwatch.StartNew();
                ResponseMatrix m;
                using (var g = new RmGpu(RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
                {
                    m = GpuBuild.Build(g, geometry, rOptions, null, CancellationToken.None, null);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                Process self = Process.GetCurrentProcess();
                self.Refresh();
                string hash = RowsHash(m);
                if (firstHash == null) firstHash = hash;
                Console.WriteLine("счёт {0}: {1:F1} с, дескрипторов {2}, частная память {3:F1} МБ, отпечаток тела {4}{5}",
                                  k + 1, clock.Elapsed.TotalSeconds, self.HandleCount, self.PrivateMemorySize64 / 1048576.0,
                                  hash.Substring(0, 16), hash == firstHash ? "" : " — ⛔ РАЗОШЁЛСЯ с первым");
            }

            return 0;
        }

        if (mode == "iso")
        {
            GeometryScenes.Iso(geometry);
            path = "gpu";
        }

        if (mode == "fail") path = "gpu";
        var options = new ResponseMatrixOptions { Histories = n };
        if (nodes > 0) options.NodeCount = nodes;
        if (path == "gpu") options.ContinuumErrorTarget = 0.0;   // как окно при галке GPU

        Console.WriteLine("П253 {0}: путь {1}, геометрия {2}{3}, {4} историй, узлов {5}, κ блоков в плане {6}",
                          mode, path, Path.GetFileName(geometryPath), mode == "iso" ? " → ISO" : "", n,
                          options.BuildGrid(geometry).Length,
                          ResponseMatrixBuilder.JointPlannedBlocks(geometry, options, options.BuildGrid(geometry).Length));

        var sink = new Sink();
        if (mode == "fail") sink.FailAfter = failAfter;
        var cts = new CancellationTokenSource();
        Exception outcome = null;
        ResponseMatrix matrix = null;
        RmGpu gpu = null;
        var task = Task.Run(() =>
        {
            sink.BuildThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                if (path == "gpu")
                {
                    using (gpu = new RmGpu(RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
                    {
                        matrix = GpuBuild.Build(gpu, geometry, options, sink, cts.Token, null);
                    }
                }
                else
                {
                    matrix = ResponseMatrixBuilder.Build(geometry, options, sink, cts.Token);
                }
            }
            catch (Exception e)
            {
                outcome = e;
            }
        });

        double cancelledAt = -1;
        if (mode == "cancel")
        {
            if (!task.Wait(TimeSpan.FromSeconds(cancelAt)))
            {
                cancelledAt = sink.Clock.Elapsed.TotalSeconds;
                Console.WriteLine("  отмена на {0:F2} с (отчётов хода до неё {1}, из них κ {2})",
                                  cancelledAt, Interlocked.Read(ref sink.Count), Interlocked.Read(ref sink.JointCount));
                cts.Cancel();
            }
            else
            {
                Console.WriteLine("  ⚠ счёт кончился раньше отмены — замер отмены не состоялся");
            }
        }

        task.Wait();
        double endedAt = sink.Clock.Elapsed.TotalSeconds;
        long reportsAtEnd = Interlocked.Read(ref sink.Count);
        Process me = Process.GetCurrentProcess();
        me.Refresh();
        TimeSpan cpu0 = me.TotalProcessorTime;
        Thread.Sleep(TimeSpan.FromSeconds(tail));
        me.Refresh();
        double cpuTail = (me.TotalProcessorTime - cpu0).TotalSeconds;
        long reportsAfter = Interlocked.Read(ref sink.Count) - reportsAtEnd;

        Console.WriteLine("  построитель вышел на {0:F2} с: {1}", endedAt,
                          outcome == null ? "матрица, узлов " + matrix.NodeCount.ToString(CultureInfo.InvariantCulture)
                                          : outcome.GetType().Name + ": " + outcome.Message);
        if (cancelledAt >= 0) Console.WriteLine("  ожидание отмены: {0:F2} с", endedAt - cancelledAt);
        Console.WriteLine("  после выхода за {0:F0} с: отчётов хода {1}, процессорного времени процесса {2:F2} с",
                          tail, reportsAfter, cpuTail);

        bool natureOk = mode == "cancel" ? outcome is OperationCanceledException
                      : mode == "fail" ? outcome is InvalidOperationException
                      : outcome is NotSupportedException;
        bool quiet = reportsAfter == 0 && cpuTail < 0.5 * tail;
        Console.WriteLine("  природа выхода {0}; тишина после выхода {1}", natureOk ? "та" : "НЕ ТА", quiet ? "да" : "НЕТ — работа осталась");
        Console.WriteLine(natureOk && quiet ? "ПРОШЛО" : "НЕ ПРОШЛО");
        return natureOk && quiet ? 0 : 1;
    }
}
