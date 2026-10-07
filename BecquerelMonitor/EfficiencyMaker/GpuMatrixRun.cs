using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.EfficiencyMaker
{
    // ⚡ GPU-путь матрицы отклика — исполнитель (`AMBER160`, полоса П221; в приложении —
    // `AMBER219`, П245 07.10.2026). Упаковка — `GpuPack.cs`, натив — `tools/effmaker/gpu`
    // (поставочная копия `rmgpu_f.dll` лежит рядом с exe, решение Amber 07.10.2026 «В git,
    // как SpecUtilsNet.dll»).
    //
    // Что здесь и почему так:
    //   * `RmGpu` — нативная `rmgpu.dll` (double, ступень 1) или `rmgpu_f.dll` (float,
    //     рабочая), загружается ПО ЯВНОМУ ПУТИ (`LoadLibrary`); приложение берёт копию
    //     рядом с собой (`DefaultPath`), пробы — из `tools\effmaker\gpu\bin`;
    //   * `RmGpu.Probe` — годность GPU БЕЗ счёта (форма матрицы зовёт её при открытии):
    //     есть ли библиотека, грузится ли, есть ли устройство и драйвер, не старее ли
    //     карта той, под которую собрано ядро, та ли физика у сборки. Причина отказа —
    //     словами, галка «Use Nvidia GPU» показывает её подсказкой;
    //   * `GpuNode.ResponseByChannel` — замена `EfficiencySimulator.ResponseByChannel` для
    //     одного узла: GPU считает ОБА цикла `Run` (сырые суммы), а всё после циклов —
    //     зеркало хвоста `Run` (EfficiencySimulator.cs:8665–8774) и вызов
    //     `SmearedContinuumError`/`FinishRun` симулятора;
    //   * истории узла идут на устройство ПОРЦИЯМИ (`RmGpu.RunChunked`): Windows снимает
    //     ядро, работающее дольше двух секунд (TDR), а аналоговая ветвь верхних узлов на
    //     3 млн историй подходит к этому порогу даже на RTX 3070 — на карте слабее
    //     одиночный запуск убил бы контекст. Порция подбирается по времени предыдущей
    //     (цель ~0.4 с); Philox считает историю по счётчику «узел, история», так что
    //     суммы от разбиения не зависят (порядок атомарных сложений — ~1e-15);
    //   * `GpuBuild.Build` — зеркало ПЛОСКОГО пути `ResponseMatrixBuilder.Build`
    //     (`--target=0`, ResponseMatrixBuilder.cs:151–531): симулятор узла даёт
    //     `MakeSimulator` (те же настройки, что у CPU), после узлов — `RebuildTotals`,
    //     `FillResolutionPeak` и `BuildJoint` (κ пар на ЦП, одновременно с узлами); ход и
    //     отмена — те же `IProgress<ResponseMatrixProgress>` и `CancellationToken`, что у
    //     CPU-пути, узел — единица хода;
    //   * `GpuCheck` (проба `GpuMatrixCheck.cs`) — ступень 1 приёмки: CPU гонит историю,
    //     GPU повторяет её с тем же состоянием xorshift64*, истории сравниваются поштучно.
    //
    // ⛔ Адаптивный останов по шуму (`ContinuumErrorTarget > 0`) GPU-путь НЕ повторяет —
    // отказ, а не тихий плоский счёт: склад считается `--target=0`, и форма при галке GPU
    // ставит цель в ноль (решение Amber 07.10.2026, дословно: «Плоско, как склад»).
    //
    // ⚠ Состояние натива — одно на процесс (настройки, упаковка, буферы стадий): живым
    // держится ОДИН `RmGpu` за раз; `Dispose` зовёт `rm_shutdown` (сброс устройства).

    [StructLayout(LayoutKind.Sequential)]
    public struct GpuHistoryOut
    {
        public double Score;
        public double Weight;
        public double DepositA;
        public double Light;
        public double Cos;
        public int Channel;
        public int Bin;
        public int Flags;
        public ulong RngAfter;
    }

    public sealed class RmGpu : IDisposable
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr LastErrorFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ProbeFn(out int count, out int ccMajor, out int ccMinor, out int driverVersion, out int runtimeVersion,
                             byte[] name, int nameLength);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn(int device, long stackBytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CfgSetFn([MarshalAs(UnmanagedType.LPStr)] string name, double value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int VoidIntFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int LoadFn(byte[] blob, int length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SlotsFn(int branch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int RunFn(int branch, double energyKev, double binKev, int bins, long n, long first, int rngMode,
                           ulong[] states, uint key0, uint key1,
                           double[] hist, double[] hist2, double[] chan, double[] light, double[] scal, int scalCount,
                           [In, Out] GpuHistoryOut[] perHistory, int blocks, int threads);

        readonly VoidFn shutdown;
        readonly LastErrorFn lastError;
        readonly InitFn init;
        readonly CfgSetFn cfgSet;
        readonly VoidIntFn cfgCommit;
        readonly LoadFn load;
        readonly RunFn run;
        readonly SlotsFn slots;
        readonly VoidIntFn sizeofHistory;
        readonly VoidIntFn realBytes;

        public readonly string Path;
        public readonly int RealBytes;
        public int Blocks = 0;           // 0 — одна история на нить; < 0 — постоянные нити по занятости (api.cu, rm_run)
        public int Threads = 128;

        /// <summary>Имя рабочей (float) сборки; лежит рядом с приложением как поставочный файл.</summary>
        public const string FileName = "rmgpu_f.dll";

        /// <summary>Стек нити устройства, байт — умолчание пробы (`--gpu-stack`), хватает рекурсии переноса.</summary>
        public const long DefaultStackBytes = 65536;

        /// <summary>
        /// Наименьшая вычислительная способность карты, под которую собрано ядро
        /// (`build_gpu.cmd`: `-arch=sm_86`, RTX 30xx; решение Amber 07.10.2026 «RTX 30xx и
        /// новее»). Карта новее исполняет PTX через JIT драйвера, старее — ядро не запустит.
        /// </summary>
        public const int MinComputeCapability = 86;

        /// <summary>Поставочная копия библиотеки — рядом с исполняемым файлом приложения.</summary>
        public static string DefaultPath
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        public RmGpu(string path, long stackBytes)
        {
            this.Path = System.IO.Path.GetFullPath(path);
            if (!File.Exists(this.Path))
            {
                throw new FileNotFoundException("GPU-путь: нет нативной библиотеки (собрать tools\\effmaker\\gpu\\build_gpu.cmd)", this.Path);
            }

            IntPtr h = LoadLibrary(this.Path);
            if (h == IntPtr.Zero)
            {
                throw new InvalidOperationException("GPU-путь: LoadLibrary(" + this.Path + ") — код " + Marshal.GetLastWin32Error());
            }

            this.shutdown = Get<VoidFn>(h, "rm_shutdown");
            this.lastError = Get<LastErrorFn>(h, "rm_last_error");
            this.init = Get<InitFn>(h, "rm_init");
            this.cfgSet = Get<CfgSetFn>(h, "rm_cfg_set");
            this.cfgCommit = Get<VoidIntFn>(h, "rm_cfg_commit");
            this.load = Get<LoadFn>(h, "rm_load");
            this.run = Get<RunFn>(h, "rm_run");
            this.slots = Get<SlotsFn>(h, "rm_slots");
            this.sizeofHistory = Get<VoidIntFn>(h, "rm_sizeof_history_out");
            this.realBytes = Get<VoidIntFn>(h, "rm_real_bytes");
            this.RealBytes = this.realBytes();
            if (this.sizeofHistory() != Marshal.SizeOf(typeof(GpuHistoryOut)))
            {
                throw new InvalidOperationException("GPU-путь: HistoryOut — " + this.sizeofHistory()
                    + " байт в нативе против " + Marshal.SizeOf(typeof(GpuHistoryOut)) + " в C# (tally.cuh ↔ GpuHistoryOut)");
            }

            this.Ok(this.init(0, stackBytes), "rm_init");

            // (`AMBER161`, П227) Запись вызовов для нативного повторителя `gpu\rm_replay.cpp`:
            // `BQ_GPU_DUMP=<каталог>` — настройки, упаковка сцены, параметры каждого `rm_run`
            // и его суммы. Повторитель зовёт ту же DLL без .NET: под ним работает Nsight
            // Compute (проба AnyCPU с заголовком PE32 под `ncu` падает 0xC000007B), и он же —
            // быстрый стенд сверки ядра против записанных сумм.
            string dump = Environment.GetEnvironmentVariable("BQ_GPU_DUMP");
            if (!string.IsNullOrEmpty(dump))
            {
                Directory.CreateDirectory(dump);
                this.dumpDir = dump;
                this.dumpLog = new StreamWriter(System.IO.Path.Combine(dump, "calls.txt"), false, new UTF8Encoding(false));
                this.dumpLog.WriteLine("init " + stackBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // Раскрой запуска для замеров: `BQ_GPU_LAUNCH=<блоков>,<нитей>`. `1,1` — одна нить
            // идёт по историям ПОДРЯД, как CPU: так отделяется состояние, переходящее из
            // истории в историю (кэш луча, кэши μ), от развилок на последнем разряде.
            string launch = Environment.GetEnvironmentVariable("BQ_GPU_LAUNCH");
            if (!string.IsNullOrEmpty(launch))
            {
                string[] parts = launch.Split(',');
                this.Blocks = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                this.Threads = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        static T Get<T>(IntPtr module, string name) where T : class
        {
            IntPtr p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException("rmgpu: нет " + name);
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        /// <summary>
        /// Годность GPU-пути БЕЗ счёта и без контекста устройства (`AMBER219`): форма
        /// матрицы зовёт это при открытии и по ответу включает или гасит галку «Use Nvidia
        /// GPU», показывая причину подсказкой. Проверки по порядку, первая отказавшая —
        /// причина: файл библиотеки, её загрузка, сборка с `rm_build_info` (старая сборка
        /// без него — отказ), физика сборки против <see cref="ResponseMatrix.PhysicsVersion"/>,
        /// устройство и драйвер (`cudaGetDeviceCount`: нет устройства / драйвер старее
        /// рантайма), вычислительная способность карты не ниже <see cref="MinComputeCapability"/>.
        ///
        /// Ничего из этого не бросает: отказ — словами в <see cref="GpuProbe.Reason"/>.
        /// Положительный контроль отказа «устройства нет» снаружи — переменная среды
        /// `CUDA_VISIBLE_DEVICES=` (пустая) у процесса приложения: рантайм отвечает
        /// `cudaErrorNoDevice`, как на машине без карты NVIDIA.
        /// </summary>
        public static GpuProbe Probe(string path)
        {
            var r = new GpuProbe { Path = path };
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    r.Reason = "library " + FileName + " is not next to the application";
                    r.ReasonCode = GpuProbeReason.NoLibrary;
                    return r;
                }

                IntPtr h = LoadLibrary(System.IO.Path.GetFullPath(path));
                if (h == IntPtr.Zero)
                {
                    r.Reason = "library " + FileName + " failed to load (Win32 error "
                               + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ")";
                    r.ReasonCode = GpuProbeReason.LoadFailed;
                    return r;
                }

                IntPtr infoPtr = GetProcAddress(h, "rm_build_info");
                IntPtr probePtr = GetProcAddress(h, "rm_probe");
                if (infoPtr == IntPtr.Zero || probePtr == IntPtr.Zero)
                {
                    r.Reason = "library " + FileName + " is an old build without rm_build_info/rm_probe — rebuild it (tools\\effmaker\\gpu\\build_gpu.cmd)";
                    r.ReasonCode = GpuProbeReason.OldLibrary;
                    return r;
                }

                var info = (LastErrorFn)Marshal.GetDelegateForFunctionPointer(infoPtr, typeof(LastErrorFn));
                r.BuildInfo = Utf8(info());
                int physics = BuildInfoInt(r.BuildInfo, "phys");
                r.LibraryPhysics = physics;
                if (physics != ResponseMatrix.PhysicsVersion)
                {
                    r.Reason = "library " + FileName + " was built for transport physics "
                               + physics.ToString(CultureInfo.InvariantCulture) + ", this build is physics "
                               + ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture) + " — rebuild it";
                    r.ReasonCode = GpuProbeReason.PhysicsMismatch;
                    return r;
                }

                var probe = (ProbeFn)Marshal.GetDelegateForFunctionPointer(probePtr, typeof(ProbeFn));
                var nameBytes = new byte[256];
                int count, major, minor, driver, runtime;
                int code = probe(out count, out major, out minor, out driver, out runtime, nameBytes, nameBytes.Length);
                r.DriverVersion = driver;
                r.RuntimeVersion = runtime;
                if (code != 0)
                {
                    var lastError = (LastErrorFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(h, "rm_last_error"), typeof(LastErrorFn));
                    string text = Utf8(lastError());
                    // Коды cudaError_t: 100 — cudaErrorNoDevice, 35 — cudaErrorInsufficientDriver.
                    if (code == 100)
                    {
                        r.Reason = "no CUDA-capable NVIDIA GPU found (" + text + ")";
                        r.ReasonCode = GpuProbeReason.NoDevice;
                    }
                    else if (code == 35)
                    {
                        r.Reason = "the NVIDIA driver is older than the CUDA runtime " + Version(runtime)
                                   + " the library was built with — update the driver (" + text + ")";
                        r.ReasonCode = GpuProbeReason.DriverTooOld;
                    }
                    else
                    {
                        r.Reason = "CUDA runtime refused: " + text;
                        r.ReasonCode = GpuProbeReason.RuntimeError;
                    }

                    return r;
                }

                r.DeviceCount = count;
                r.DeviceName = Encoding.UTF8.GetString(nameBytes, 0, Math.Max(0, Array.IndexOf(nameBytes, (byte)0)));
                r.ComputeCapability = major * 10 + minor;
                if (count < 1)
                {
                    r.Reason = "no CUDA-capable NVIDIA GPU found";
                    r.ReasonCode = GpuProbeReason.NoDevice;
                    return r;
                }

                if (r.ComputeCapability < MinComputeCapability)
                {
                    r.Reason = r.DeviceName + " is compute capability " + Cc(r.ComputeCapability)
                               + ", the library needs " + Cc(MinComputeCapability) + " (RTX 30xx) or newer";
                    r.ReasonCode = GpuProbeReason.DeviceTooOld;
                    return r;
                }

                r.Available = true;
                r.Reason = "";
                return r;
            }
            catch (Exception e)
            {
                // Любая неожиданность — отказ словами, а не падение формы при открытии.
                r.Available = false;
                r.Reason = e.GetType().Name + ": " + e.Message;
                r.ReasonCode = GpuProbeReason.RuntimeError;
                return r;
            }
        }

        static string Cc(int cc)
        {
            return (cc / 10).ToString(CultureInfo.InvariantCulture) + "." + (cc % 10).ToString(CultureInfo.InvariantCulture);
        }

        static string Version(int v)
        {
            return (v / 1000).ToString(CultureInfo.InvariantCulture) + "." + ((v % 1000) / 10).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Поле `имя=значение;` из строки `rm_build_info`; нет поля или не число — −1.</summary>
        public static int BuildInfoInt(string info, string key)
        {
            if (string.IsNullOrEmpty(info)) return -1;
            foreach (string part in info.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == key)
                {
                    int v;
                    return int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
                }
            }

            return -1;
        }

        static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            var bytes = new List<byte>();
            for (int i = 0; ; i++)
            {
                byte b = Marshal.ReadByte(p, i);
                if (b == 0) break;
                bytes.Add(b);
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Снять контекст устройства (`rm_shutdown`): приложение считает матрицы много раз
        /// за жизнь процесса, и держать устройство между счётами незачем. Повторный вызов
        /// безвреден.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;
            if (this.dumpLog != null)
            {
                this.dumpLog.Flush();
                this.dumpLog.Dispose();
                this.dumpLog = null;
            }

            this.shutdown();
        }

        bool disposed;

        void Alive()
        {
            if (this.disposed) throw new ObjectDisposedException("RmGpu");
        }

        string LastError()
        {
            return Utf8(this.lastError());
        }

        void Ok(int code, string what)
        {
            if (code != 0) throw new InvalidOperationException("GPU " + what + ": " + this.LastError());
        }

        public int Slots(int branch) { return this.slots(branch); }

        /// <summary>
        /// Настройки и данные симулятора узла — на устройство. Настройки — каждый узел (у
        /// узлов разные допуски пика); данные — только когда `reuse` не задан или
        /// упаковки ещё нет: сцена у узлов одна, и пересобирать 1.4 МБ отражением на
        /// каждый из 144 узлов значило бы тратить на упаковку больше, чем на счёт.
        /// </summary>
        public void Prepare(EfficiencySimulator sim, bool reuse = false)
        {
            this.Alive();
            var watch = Stopwatch.StartNew();
            foreach (KeyValuePair<string, double> kv in GpuPack.Settings(sim))
            {
                this.Ok(this.cfgSet(kv.Key, kv.Value), "rm_cfg_set(" + kv.Key + ")");
                if (this.dumpLog != null)
                {
                    this.dumpLog.WriteLine("cfg " + kv.Key + " " + BitConverter.DoubleToInt64Bits(kv.Value)
                                           .ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            this.Ok(this.cfgCommit(), "rm_cfg_commit");
            if (this.dumpLog != null) this.dumpLog.WriteLine("commit");
            if (!reuse || this.lastBlob == null)
            {
                byte[] blob = GpuPack.Pack(sim);
                this.Ok(this.load(blob, blob.Length), "rm_load");
                this.lastBlob = blob;
                this.LastBlobBytes = blob.Length;
                if (this.dumpLog != null)
                {
                    string name = "blob" + (this.dumpBlobs++).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin";
                    File.WriteAllBytes(System.IO.Path.Combine(this.dumpDir, name), blob);
                    this.dumpLog.WriteLine("load " + name);
                }
            }
            else
            {
                GpuReflect.Call(sim, "EnsureBuilt");
            }

            this.PackSeconds += watch.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Сверка ПРИНЯТОГО допущения «данные у узлов сцены одни»: упаковка этого
        /// симулятора обязана совпасть с загруженной байт в байт. Отказ — исключение.
        /// </summary>
        public void AssertSameBlob(EfficiencySimulator sim, string where)
        {
            byte[] blob = GpuPack.Pack(sim);
            bool same = this.lastBlob != null && blob.Length == this.lastBlob.Length;
            for (int i = 0; same && i < blob.Length; i++) same = blob[i] == this.lastBlob[i];
            if (!same)
            {
                throw new InvalidOperationException("GPU-путь: упаковка узла " + where
                    + " разошлась с упаковкой первого узла сцены — данные зависят от узла, повторное использование неверно");
            }
        }

        public void ForgetBlob() { this.lastBlob = null; }

        byte[] lastBlob;
        public int LastBlobBytes;
        public double PackSeconds, KernelSeconds;

        /// <summary>
        /// Историй в первой порции `RunChunked`; дальше порция подбирается по времени
        /// предыдущего запуска. `BQ_GPU_CHUNK=0` — без разбиения (сверка сумм), `=N` —
        /// порции ровно по N историй.
        /// </summary>
        public long ChunkHistories = 1L << 18;

        /// <summary>Цель длительности одного запуска, с; порог TDR Windows — 2 с на ядро.</summary>
        public const double ChunkTargetSeconds = 0.4;

        static readonly long ChunkFloor = 1L << 16, ChunkCeiling = 1L << 22;

        /// <summary>
        /// Ветвь узла порциями историй: тот же результат, что один `Run` на все `n` (Philox
        /// считает историю по `first + i`), но ни один запуск ядра не живёт дольше цели.
        /// Между порциями — отмена.
        /// </summary>
        public void RunChunked(int branch, double energyKev, double binKev, int bins, long n, long first,
                               uint key0, uint key1,
                               double[] hist, double[] hist2, double[] chan, double[] light, double[] scal,
                               CancellationToken cancellation)
        {
            string env = Environment.GetEnvironmentVariable("BQ_GPU_CHUNK");
            long fixedChunk = -1;
            if (!string.IsNullOrEmpty(env))
            {
                fixedChunk = long.Parse(env, CultureInfo.InvariantCulture);
            }

            if (fixedChunk == 0)
            {
                cancellation.ThrowIfCancellationRequested();
                this.Run(branch, energyKev, binKev, bins, n, first, 1, null, key0, key1, hist, hist2, chan, light, scal, null);
                return;
            }

            long chunk = fixedChunk > 0 ? fixedChunk : Math.Max(ChunkFloor, Math.Min(ChunkCeiling, this.ChunkHistories));
            long done = 0;
            while (done < n)
            {
                cancellation.ThrowIfCancellationRequested();
                long m = Math.Min(chunk, n - done);
                var watch = Stopwatch.StartNew();
                this.Run(branch, energyKev, binKev, bins, m, first + done, 1, null, key0, key1, hist, hist2, chan, light, scal, null);
                done += m;
                double seconds = watch.Elapsed.TotalSeconds;
                if (fixedChunk < 0 && seconds > 0.0)
                {
                    long next = (long)(m * ChunkTargetSeconds / seconds);
                    chunk = Math.Max(ChunkFloor, Math.Min(ChunkCeiling, next));
                    this.ChunkHistories = chunk;
                }
            }
        }

        public void Run(int branch, double energyKev, double binKev, int bins, long n, long first, int rngMode,
                        ulong[] states, uint key0, uint key1,
                        double[] hist, double[] hist2, double[] chan, double[] light, double[] scal,
                        GpuHistoryOut[] perHistory)
        {
            this.Alive();
            // `rm_run` ПРИБАВЛЯЕТ к массивам хоста; в запись идёт приращение этого вызова.
            double[] pre = this.dumpLog != null
                ? new[] { SumOf(hist), SumOf(hist2), SumOf(chan), SumOf(light) } : null;
            double[] preScal = this.dumpLog != null ? (double[])scal.Clone() : null;
            var watch = Stopwatch.StartNew();
            this.Ok(this.run(branch, energyKev, binKev, bins, n, first, rngMode, states, key0, key1,
                             hist, hist2, chan, light, scal, scal.Length, perHistory, this.Blocks, this.Threads),
                    "rm_run(ветвь " + branch + ")");
            this.KernelSeconds += watch.Elapsed.TotalSeconds;
            if (this.dumpLog != null && rngMode != 0 && perHistory == null)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                // run <ветвь> <E бит> <бин бит> <бинов> <n> <first> <режим> <key0> <key1>
                //     <длины hist hist2 chan light scal> <секунд ядра>; затем строка сумм.
                this.dumpLog.WriteLine(string.Join(" ", "run", branch.ToString(ci),
                    BitConverter.DoubleToInt64Bits(energyKev).ToString(ci), BitConverter.DoubleToInt64Bits(binKev).ToString(ci),
                    bins.ToString(ci), n.ToString(ci), first.ToString(ci), rngMode.ToString(ci), key0.ToString(ci), key1.ToString(ci),
                    Len(hist), Len(hist2), Len(chan), Len(light), Len(scal),
                    watch.Elapsed.TotalSeconds.ToString("R", ci)));
                var d = new List<string>
                {
                    "sums",
                    (SumOf(hist) - pre[0]).ToString("R", ci), (SumOf(hist2) - pre[1]).ToString("R", ci),
                    (SumOf(chan) - pre[2]).ToString("R", ci), (SumOf(light) - pre[3]).ToString("R", ci)
                };
                for (int i = 0; i < scal.Length; i++) d.Add((scal[i] - preScal[i]).ToString("R", ci));
                this.dumpLog.WriteLine(string.Join(" ", d));
                this.dumpLog.Flush();
            }
        }

        static string Len(double[] a) { return a == null ? "-1" : a.Length.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        static double SumOf(double[] a)
        {
            double s = 0.0;
            if (a != null) foreach (double v in a) s += v;
            return s;
        }

        string dumpDir;
        StreamWriter dumpLog;
        int dumpBlobs;
    }

    /// <summary>Почему GPU-путь недоступен (<see cref="RmGpu.Probe"/>); форма переводит код в подпись.</summary>
    public enum GpuProbeReason
    {
        None = 0,
        NoLibrary,
        LoadFailed,
        OldLibrary,
        PhysicsMismatch,
        NoDevice,
        DriverTooOld,
        DeviceTooOld,
        RuntimeError
    }

    /// <summary>Ответ <see cref="RmGpu.Probe"/>: годен ли GPU-путь и, если нет, почему.</summary>
    public sealed class GpuProbe
    {
        public bool Available;
        public GpuProbeReason ReasonCode;
        /// <summary>Причина отказа по-английски, для журнала; пусто у годного.</summary>
        public string Reason = "";
        public string Path = "";
        public string BuildInfo = "";
        public int LibraryPhysics = -1;
        public int DeviceCount;
        public string DeviceName = "";
        /// <summary>Вычислительная способность картой числом: 8.6 → 86.</summary>
        public int ComputeCapability;
        public int DriverVersion, RuntimeVersion;
    }

    /// <summary>Слоты накопителей — те же номера, что `tally.cuh`.</summary>
    public static class GpuSlots
    {
        public const int W_SUM = 0, W_SUM2 = 1, W_S0 = 2, W_S2 = 3, W_S4 = 4, W_S00 = 5, W_S22 = 6, W_S44 = 7,
            W_S02 = 8, W_S04 = 9, W_T0 = 10, W_T2 = 11, W_T4 = 12, W_T00 = 13, W_T22 = 14, W_T44 = 15, W_T02 = 16,
            W_T04 = 17, W_RESOLUTION = 18, W_LIGHT_BIN_SPLIT = 19, W_COUNT_LIGHT_BIN_SPLIT = 20,
            W_WEIGHT_LIGHT_BIN_SPLIT = 21, W_COUNT_PATH_LIMIT_CUT = 22, W_COUNT_CASCADE_OVERFLOW = 23,
            W_COUNT_ESCAPE_DROPPED = 24, W_SLOTS = 25;

        public const int A_OUTSIDE = 0, A_OUTSIDE2 = 1, A_OUTSIDE_LIGHT = 2, A_RESOLUTION = 3, A_SCORED = 4,
            A_COUNT_PEAK_BIN_DROPPED = 5, A_WEIGHT_PEAK_BIN_DROPPED = 6, A_COUNT_PEAK_BIN_DROPPED_SCATTERED = 7,
            A_COUNT_PEAK_OUT_OF_CONE = 8, A_COUNT_PATH_LIMIT_CUT = 9, A_COUNT_CASCADE_OVERFLOW = 10,
            A_COUNT_ESCAPE_DROPPED = 11, A_COUNT_PENDING_DROPPED = 12, A_SLOTS = 13;
    }

    /// <summary>Один узел матрицы на GPU — замена `ResponseByChannel`.</summary>
    public static class GpuNode
    {
        const int Channels = 7;

        /// <summary>
        /// Ключ Philox узла — из того же зерна, что у CPU (`MakeSimulator`: зерно + (узел + 1)·φ),
        /// смешанного SplitMix64: независимые узлы, воспроизводимо при любом раскрое запуска.
        /// </summary>
        public static void NodeKey(ResponseMatrixOptions options, EfficiencySimulator sim, int index,
                                   out uint key0, out uint key1)
        {
            int seed = options.Seed != 0 ? options.Seed : sim.Seed;
            ulong z = (ulong)seed + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            key0 = (uint)z;
            key1 = (uint)(z >> 32);
        }

        public static double[][] ResponseByChannel(RmGpu gpu, EfficiencySimulator sim, double energyKev, double binKev,
                                                   uint key0, uint key1, out double relativeError, bool reuseBlob = false)
        {
            return ResponseByChannel(gpu, sim, energyKev, binKev, key0, key1, out relativeError, reuseBlob, CancellationToken.None);
        }

        public static double[][] ResponseByChannel(RmGpu gpu, EfficiencySimulator sim, double energyKev, double binKev,
                                                   uint key0, uint key1, out double relativeError, bool reuseBlob,
                                                   CancellationToken cancellation)
        {
            if (!(energyKev > 0.0) || !(binKev > 0.0)) throw new ArgumentOutOfRangeException("binKev");
            int bins = EfficiencySimulator.PeakBin(energyKev, binKev) + 1;
            var channels = new double[Channels][];
            for (int c = 0; c < Channels; c++) channels[c] = new double[bins];
            double[] histogram = new double[bins];
            GpuReflect.Set(sim, "channelHistograms", channels);
            try
            {
                relativeError = Run(gpu, sim, energyKev, histogram, binKev, key0, key1, reuseBlob, cancellation);
            }
            finally
            {
                GpuReflect.Set(sim, "channelHistograms", null);
            }

            return channels;
        }

        /// <summary>Зеркало `EfficiencySimulator.Run` (:8605–8775) при `histogram != null`.</summary>
        static double Run(RmGpu gpu, EfficiencySimulator sim, double energyKev, double[] histogram, double binKev,
                          uint key0, uint key1, bool reuseBlob, CancellationToken cancellation)
        {
            gpu.Prepare(sim, reuseBlob);                        // зовёт EnsureBuilt
            object lightYield = GpuReflect.Field(sim, "lightYield");
            double[] lightSum = lightYield != null ? new double[histogram.Length] : null;
            GpuReflect.Set(sim, "lightSum", lightSum);
            GpuReflect.Set(sim, "lightBinSplit", 0.0);
            sim.CountLightBinSplit = 0;
            sim.WeightLightBinSplit = 0.0;
            if (lightSum != null)
            {
                SetProperty(sim, "LastPhotonLightScale", 0.0);
                SetProperty(sim, "LastPhotonLightScaleSplit", 0.0);
            }

            int n = Math.Max(1000, sim.Histories);
            GpuReflect.Set(sim, "resolutionWeighted", 0.0);
            GpuReflect.Set(sim, "resolutionAnalog", 0.0);
            SetProperty(sim, "LastResolutionPeakExtra", 0.0);
            var channels = (double[][])GpuReflect.Field(sim, "channelHistograms");
            int bins = histogram.Length;

            // --- цикл 1: взвешенная ветвь -------------------------------------------
            double[] wHist = new double[bins], wChan = new double[bins * Channels], wLight = new double[bins];
            double[] wScal = new double[GpuSlots.W_SLOTS];
            gpu.RunChunked(0, energyKev, binKev, bins, n, 0, key0, key1, wHist, null, wChan, wLight, wScal, cancellation);
            for (int b = 0; b < bins; b++)
            {
                histogram[b] += wHist[b];
                if (lightSum != null) lightSum[b] += wLight[b];
                for (int c = 0; c < Channels; c++) channels[c][b] += wChan[c * bins + b];
            }

            double sum = wScal[GpuSlots.W_SUM], sum2 = wScal[GpuSlots.W_SUM2];
            GpuReflect.Set(sim, "resolutionWeighted", wScal[GpuSlots.W_RESOLUTION]);
            GpuReflect.Set(sim, "lightBinSplit", wScal[GpuSlots.W_LIGHT_BIN_SPLIT]);
            sim.CountLightBinSplit += (long)wScal[GpuSlots.W_COUNT_LIGHT_BIN_SPLIT];
            sim.WeightLightBinSplit += wScal[GpuSlots.W_WEIGHT_LIGHT_BIN_SPLIT];
            sim.CountPathLimitCut += (long)wScal[GpuSlots.W_COUNT_PATH_LIMIT_CUT];
            sim.CountCascadeOverflow += (long)wScal[GpuSlots.W_COUNT_CASCADE_OVERFLOW];
            sim.CountEscapeDropped += (long)wScal[GpuSlots.W_COUNT_ESCAPE_DROPPED];
            SetProperty(sim, "LastAngularMoments", Angular(n, wScal));

            double mean = sum / n;
            double variance = Math.Max(0.0, sum2 / n - mean * mean);

            // --- цикл 2: аналоговая ветвь (AnalogContinuumRun, :10150–10312) ----------
            if (histogram.Length > 1 && sim.AnalogContinuum)
            {
                double[] hist = new double[bins], hist2 = new double[bins], aChan = new double[bins * Channels];
                double[] light = new double[bins];
                double[] aScal = new double[GpuSlots.A_SLOTS];
                // Счётчик Philox аналоговой ветви — со сдвигом 2^40: истории двух ветвей узла
                // берут НЕПЕРЕСЕКАЮЩИЕСЯ потоки (у CPU ветви тоже идут разными числами).
                gpu.RunChunked(1, energyKev, binKev, bins, n, 1L << 40, key0, key1, hist, hist2, aChan, light, aScal, cancellation);
                int peak = bins - 1;
                long scored = (long)aScal[GpuSlots.A_SCORED];
                sim.CountPeakBinDropped += (long)aScal[GpuSlots.A_COUNT_PEAK_BIN_DROPPED];
                sim.WeightPeakBinDropped += aScal[GpuSlots.A_WEIGHT_PEAK_BIN_DROPPED];
                sim.CountPeakBinDroppedScattered += (long)aScal[GpuSlots.A_COUNT_PEAK_BIN_DROPPED_SCATTERED];
                sim.CountPeakOutOfCone += (long)aScal[GpuSlots.A_COUNT_PEAK_OUT_OF_CONE];
                sim.WeightPeakOutOfCone += aScal[GpuSlots.A_OUTSIDE];
                sim.WeightPeakOutOfCone2 += aScal[GpuSlots.A_OUTSIDE2];
                sim.CountAnalogScored += scored;
                sim.CountPathLimitCut += (long)aScal[GpuSlots.A_COUNT_PATH_LIMIT_CUT];
                sim.CountCascadeOverflow += (long)aScal[GpuSlots.A_COUNT_CASCADE_OVERFLOW];
                sim.CountEscapeDropped += (long)aScal[GpuSlots.A_COUNT_ESCAPE_DROPPED];
                sim.CountPendingDropped += (long)aScal[GpuSlots.A_COUNT_PENDING_DROPPED];
                GpuReflect.Set(sim, "resolutionAnalog", aScal[GpuSlots.A_RESOLUTION]);

                sim.LastContinuumIntegralError = scored > 0 ? 100.0 / Math.Sqrt(scored) : 100.0;
                sim.LastContinuumRelativeError = (double)GpuReflect.Call(sim, "SmearedContinuumError",
                                                                          hist, hist2, peak, binKev, energyKev);
                for (int b = 0; b < peak; b++)
                {
                    histogram[b] = hist[b];
                    if (lightSum != null) lightSum[b] = light[b];
                    for (int c = 0; c < Channels; c++) channels[c][b] = aChan[c * bins + b];
                }

                // Потерянный класс — в пик (Run, :8715–8741).
                double outside = aScal[GpuSlots.A_OUTSIDE];
                if (outside > 0.0)
                {
                    double outMean = outside / n;
                    histogram[histogram.Length - 1] += outside;
                    double[] peakChannel = channels[(int)EfficiencySimulator.ResponseChannel.Peak];
                    peakChannel[peakChannel.Length - 1] += outside;
                    if (lightSum != null) lightSum[lightSum.Length - 1] += aScal[GpuSlots.A_OUTSIDE_LIGHT];
                    mean += outMean;
                    variance += Math.Max(0.0, aScal[GpuSlots.A_OUTSIDE2] / n - outMean * outMean);
                }
            }

            double relativeError = mean > 0.0 ? Math.Sqrt(variance / n) / mean * 100.0 : 0.0;
            bool analogBins = histogram.Length > 1 && sim.AnalogContinuum;
            double resW = (double)GpuReflect.Field(sim, "resolutionWeighted");
            double resA = (double)GpuReflect.Field(sim, "resolutionAnalog");
            SetProperty(sim, "LastResolutionPeakExtra", (analogBins ? resA : resW) / n);
            GpuReflect.Call(sim, "FinishRun", energyKev, histogram, binKev, n, mean);
            return relativeError;
        }

        static AngularMomentSums Angular(long n, double[] s)
        {
            var a = new AngularMomentSums();
            SetProperty(a, "N", n);
            SetProperty(a, "S0", s[GpuSlots.W_S0]); SetProperty(a, "S2", s[GpuSlots.W_S2]); SetProperty(a, "S4", s[GpuSlots.W_S4]);
            SetProperty(a, "S00", s[GpuSlots.W_S00]); SetProperty(a, "S22", s[GpuSlots.W_S22]); SetProperty(a, "S44", s[GpuSlots.W_S44]);
            SetProperty(a, "S02", s[GpuSlots.W_S02]); SetProperty(a, "S04", s[GpuSlots.W_S04]);
            SetProperty(a, "T0", s[GpuSlots.W_T0]); SetProperty(a, "T2", s[GpuSlots.W_T2]); SetProperty(a, "T4", s[GpuSlots.W_T4]);
            SetProperty(a, "T00", s[GpuSlots.W_T00]); SetProperty(a, "T22", s[GpuSlots.W_T22]); SetProperty(a, "T44", s[GpuSlots.W_T44]);
            SetProperty(a, "T02", s[GpuSlots.W_T02]); SetProperty(a, "T04", s[GpuSlots.W_T04]);
            return a;
        }

        /// <summary>Свойство с закрытым сеттером — `{ get; private set; }`.</summary>
        public static void SetProperty(object o, string name, object value)
        {
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) throw new MissingMemberException(o.GetType().FullName, name);
            MethodInfo set = p.GetSetMethod(true);
            if (set == null) throw new MissingMethodException(o.GetType().FullName, "set_" + name);
            set.Invoke(o, new[] { value });
        }
    }

    /// <summary>
    /// Зеркало ПЛОСКОГО пути `ResponseMatrixBuilder.Build` (`--target=0`). Узлы — по одному
    /// на GPU, сверху вниз по энергии (как CPU); κ пар — закрытый `BuildJoint` приложения на ЦП.
    /// </summary>
    public static class GpuBuild
    {
        static readonly Type Builder = typeof(ResponseMatrixBuilder);

        public static double LastGpuSeconds, LastJointSeconds;

        public static ResponseMatrix Build(RmGpu gpu, GeometryModel geometry, ResponseMatrixOptions options, TextWriter log)
        {
            return Build(gpu, geometry, options, null, CancellationToken.None, log);
        }

        /// <summary>
        /// Матрица на GPU с ходом и отменой — вход приложения (`AMBER219`). Ход считается
        /// узлами (<see cref="ResponseMatrixProgress.SettledNodes"/>): у плоского счёта
        /// проход у узла один, и полоса идёт только вперёд. Отмена — между порциями историй
        /// (<see cref="RmGpu.RunChunked"/>) и у κ пар на ЦП; наружу уходит
        /// <see cref="OperationCanceledException"/>, как у CPU-пути.
        /// </summary>
        public static ResponseMatrix Build(RmGpu gpu, GeometryModel geometry, ResponseMatrixOptions options,
                                           IProgress<ResponseMatrixProgress> progress, CancellationToken cancellation,
                                           TextWriter log)
        {
            if (gpu == null) throw new ArgumentNullException("gpu");
            if (geometry == null) throw new ArgumentNullException("geometry");
            if (options == null) options = new ResponseMatrixOptions();
            if (options.ContinuumErrorTarget > 0.0)
            {
                throw new NotSupportedException("GPU-путь считает только плоско (--target=0): адаптивный останов по шуму "
                    + "(ContinuumErrorTarget = " + options.ContinuumErrorTarget.ToString(CultureInfo.InvariantCulture)
                    + ") не перенесён");
            }

            // Те же отказы словами до счёта, что у `ResponseMatrixBuilder.Build` (`AMBER201`):
            // боковая постановка цилиндра и элемент вне таблиц ослабления.
            string facingError = geometry.FacingError;
            if (!string.IsNullOrEmpty(facingError)) throw new InvalidOperationException(facingError);
            string unknownElement = geometry.UnknownElementProblem();
            if (unknownElement != null) throw new InvalidOperationException(unknownElement);

            double[] grid = options.BuildGrid(geometry);
            double[] continuumError = new double[grid.Length];
            float[][][] channelRows = new float[EfficiencySimulator.ResponseChannelCount][][];
            for (int c = 0; c < channelRows.Length; c++) channelRows[c] = new float[grid.Length][];
            var watch = Stopwatch.StartNew();
            long[] nodeHistories = new long[grid.Length];
            double[] nodeSeconds = new double[grid.Length];
            AngularMomentSums[] nodeAngular = new AngularMomentSums[grid.Length];
            double[] nodeResolutionExtra = new double[grid.Length];
            int nominal = Math.Max(1, options.Histories);

            // κ пар (`BuildJoint`) от строк узлов не зависит — только от геометрии, настроек и
            // сетки, а пишет одни поля `Joint*`. Поэтому он идёт на ЦП ОДНОВРЕМЕННО с узлами
            // на GPU, во временную матрицу; поля переносятся после узлов. Потоков ЦП — на
            // один меньше обычного: один держит запуски GPU.
            int threads = options.Threads > 0 ? options.Threads : Math.Max(1, Environment.ProcessorCount - 2);
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = cancellation };
            var jointMatrix = new ResponseMatrix();
            var jointWatch = Stopwatch.StartNew();
            double jointSeconds = 0.0;
            Task jointTask = Task.Run(() =>
            {
                GpuReflect.Call(Builder, "BuildJoint", geometry, options, jointMatrix, grid, parallel);
                jointSeconds = jointWatch.Elapsed.TotalSeconds;
            }, cancellation);

            Action<int, double> report = (settled, energy) =>
            {
                if (progress == null) return;
                progress.Report(new ResponseMatrixProgress
                {
                    Done = settled,
                    Total = grid.Length,
                    DoneHistories = (long)settled * nominal,
                    TotalHistories = (long)grid.Length * nominal,
                    LastEnergyKev = energy,
                    StartedNodes = Math.Min(grid.Length, settled + 1),
                    SettledNodes = settled,
                    TotalNodes = grid.Length
                });
            };

            try
            {
            for (int slot = 0; slot < grid.Length; slot++)
            {
                cancellation.ThrowIfCancellationRequested();
                int index = grid.Length - 1 - slot;
                long t0 = Stopwatch.GetTimestamp();
                double energyKev = grid[index];
                report(slot, energyKev);
                var sim = (EfficiencySimulator)GpuReflect.Call(Builder, "MakeSimulator", geometry, options, index, energyKev);
                sim.Histories = Math.Max(1, nominal);
                double resolutionHalfWidth = (double)GpuReflect.Call(Builder, "ResolutionHalfWidth", options, geometry, energyKev);
                sim.ResolutionPeakHalfWidthKev = resolutionHalfWidth;
                uint key0, key1;
                GpuNode.NodeKey(options, sim, index, out key0, out key1);
                double relativeError;
                if (slot == 0) gpu.ForgetBlob();
                double[][] histograms = GpuNode.ResponseByChannel(gpu, sim, energyKev, options.BinKev, key0, key1,
                                                                  out relativeError, true, cancellation);
                // Допущение «данные у узлов сцены одни» сверяется на последнем узле (самая
                // далёкая от первого энергия): разойдись упаковка — отказ, а не тихий счёт.
                if (slot == grid.Length - 1) gpu.AssertSameBlob(sim, energyKev.ToString("F3", CultureInfo.InvariantCulture) + " кэВ");
                continuumError[index] = sim.LastContinuumRelativeError;
                nodeResolutionExtra[index] = resolutionHalfWidth > 0.0 ? sim.LastResolutionPeakExtra : 0.0;
                nodeAngular[index] = sim.LastAngularMoments;
                nodeHistories[index] += sim.Histories;
                nodeSeconds[index] += (double)(Stopwatch.GetTimestamp() - t0) / Stopwatch.Frequency;
                Store(channelRows, index, histograms);
                if (log != null && (slot % 20 == 0 || slot == grid.Length - 1))
                {
                    log.WriteLine("   GPU узел {0}/{1}: {2:F1} кэВ, {3:F2} с, упаковка {4} КБ",
                                  slot + 1, grid.Length, energyKev, nodeSeconds[index], gpu.LastBlobBytes / 1024);
                }
            }
            }
            catch (OperationCanceledException)
            {
                // Отменили узлы — κ пар на ЦП тоже снимается тем же жетоном; дождаться, чтобы
                // наружу не ушла работающая задача.
                try { jointTask.Wait(); } catch (AggregateException) { }
                throw;
            }

            report(grid.Length, grid[0]);
            LastGpuSeconds = watch.Elapsed.TotalSeconds;
            if (log != null)
            {
                log.WriteLine("   GPU      : из них ядра {0:F1} с, упаковка и настройки {1:F1} с", gpu.KernelSeconds, gpu.PackSeconds);
            }

            gpu.KernelSeconds = 0.0;
            gpu.PackSeconds = 0.0;

            double worstContinuum = 0.0, sumInverse = 0.0, sumWeight = 0.0;
            foreach (double e in continuumError)
            {
                if (e > worstContinuum) worstContinuum = e;
                if (e > 0.0) { sumInverse += 1.0 / e; sumWeight += 1.0 / (e * e); }
            }

            long spentTotal = 0, capNode = 0;
            foreach (long h in nodeHistories) { spentTotal += h; if (h > capNode) capNode = h; }

            var matrix = new ResponseMatrix
            {
                ContinuumRelativeError = worstContinuum,
                ContinuumWeightedError = sumWeight > 0.0 ? sumInverse / sumWeight : 0.0,
                HistoriesSpent = spentTotal,
                HistoriesWorstNode = capNode,
                NodeHistories = nodeHistories,
                NodeErrors = continuumError,
                NodeSeconds = nodeSeconds,
                Energies = grid,
                BinKev = options.BinKev,
                ChannelRows = channelRows,
                Histories = options.Histories,
                Options = options.Clone(),
                Stamp = (string)GpuReflect.Call(typeof(ResponseMatrix), "ComputeStamp", geometry, options),
                Normalization = (ResponseMatrixNormalization)GpuReflect.Call(typeof(ResponseMatrix), "NormalizationOf", geometry),
                CreatedUtc = DateTime.UtcNow,
                AngularQk = (AngularAttenuation)GpuReflect.Call(typeof(AngularAttenuation), "FromMoments", grid, nodeAngular)
            };

            matrix.RebuildTotals();
            GpuReflect.Call(Builder, "FillResolutionPeak", geometry, matrix, nodeResolutionExtra);
            try
            {
                jointTask.Wait();
            }
            catch (AggregateException e)
            {
                // `OperationCanceledException` κ пар — наружу той же природы, что у узлов.
                throw e.InnerException ?? e;
            }

            // Все поля `Joint*` временной матрицы — в итоговую (их и только их пишет BuildJoint).
            foreach (PropertyInfo p in typeof(ResponseMatrix).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.Name.StartsWith("Joint", StringComparison.Ordinal) && p.CanRead && p.GetSetMethod(true) != null
                    && p.GetIndexParameters().Length == 0)
                {
                    p.GetSetMethod(true).Invoke(matrix, new[] { p.GetValue(jointMatrix, null) });
                }
            }

            LastJointSeconds = jointSeconds;
            watch.Stop();
            matrix.BuildSeconds = watch.Elapsed.TotalSeconds;
            return matrix;
        }

        /// <summary>= `store` из `ResponseMatrixBuilder.Build` (:225–247).</summary>
        static void Store(float[][][] channelRows, int index, double[][] histograms)
        {
            for (int c = 0; c < histograms.Length; c++)
            {
                double[] histogram = histograms[c];
                bool any = false;
                for (int b = 0; b < histogram.Length && !any; b++) any = histogram[b] > 0.0;
                float[] row = new float[any ? histogram.Length : 0];
                for (int b = 0; b < row.Length; b++) row[b] = (float)histogram[b];
                channelRows[c][index] = row;
            }
        }
    }
}
