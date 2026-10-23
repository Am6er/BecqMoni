using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    //   * `RmGpu.Probe` — годность GPU БЕЗ счёта: есть ли библиотека, грузится ли, есть ли
    //     устройство и драйвер, не старее ли карта той, под которую собрано ядро, та ли
    //     физика у сборки. Причина отказа — словами, галка «Use Nvidia GPU» показывает её
    //     подсказкой. Форма матрицы зовёт её ДОЧЕРНИМ процессом (`ProbeIsolated`,
    //     `AMBER261`): сбой в драйвере роняет ребёнка, а не приложение;
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

        [DllImport("kernel32")]
        static extern uint SetErrorMode(uint mode);

        const uint SEM_FAILCRITICALERRORS = 0x0001, SEM_NOGPFAULTERRORBOX = 0x0002, SEM_NOOPENFILEERRORBOX = 0x8000;

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
        /// новее»). Карта того же поколения новее (8.9) исполняет тот же машинный код, другого
        /// поколения — PTX через JIT драйвера (<see cref="DriverTooOldForJit"/>), старее — ядро
        /// не запустит.
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
        public static GpuProbe Probe(string path, Action<string> step = null)
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

                // (`AMBER261`) Шаги — для дочерней пробы: упавший ребёнок называет
                // последний начатый нативный вызов (`ProbeIsolated`).
                if (step != null) step("LoadLibrary");
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
                if (step != null) step("rm_build_info");
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
                if (step != null) step("rm_probe");
                int code = probe(out count, out major, out minor, out driver, out runtime, nameBytes, nameBytes.Length);
                r.DriverVersion = driver;
                r.RuntimeVersion = runtime;
                if (code != 0)
                {
                    if (step != null) step("rm_last_error");
                    var lastError = (LastErrorFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(h, "rm_last_error"), typeof(LastErrorFn));
                    string text = Utf8(lastError());
                    // Коды cudaError_t: 100 — cudaErrorNoDevice, 35 — cudaErrorInsufficientDriver.
                    if (code == 100)
                    {
                        r.Reason = "no CUDA-capable NVIDIA GPU found (" + text + ")";
                        r.ReasonCode = GpuProbeReason.NoDevice;
                    }
                    else if (code == 35 && driver == 0)
                    {
                        // (`AMBER261`) Драйвера NVIDIA нет вовсе: `cudaDriverGetVersion`
                        // отдаёт 0, а `cudaGetDeviceCount` — тот же код 35, что у старого
                        // драйвера; подпись «драйвер 0.0 старее рантайма» на машине без
                        // карты NVIDIA врала бы.
                        r.Reason = "no NVIDIA driver installed (" + text + ")";
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

                if (DriverTooOldForJit(major, minor, r.BuildInfo, driver, runtime))
                {
                    r.Reason = r.DeviceName + " runs the kernel through the driver's PTX JIT, which needs a driver not older"
                               + " than the CUDA runtime " + Version(runtime) + " the library was built with (driver "
                               + Version(driver) + ") — update the driver";
                    r.ReasonCode = GpuProbeReason.DriverTooOld;
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

        /// <summary>Ключ командной строки приложения: «ответить на пробу GPU и выйти» (<see cref="ProbeChildMain"/>).</summary>
        public const string ProbeChildKey = "--gpu-probe";

        /// <summary>Сколько ждать ответа дочерней пробы, мс; дольше — отказ, ребёнок снимается.</summary>
        public const int ProbeChildTimeoutMs = 15000;

        /// <summary>
        /// ⛔ (`AMBER261`, 23.10.2026) <see cref="Probe"/> В ДОЧЕРНЕМ ПРОЦЕССЕ — так её зовёт
        /// окно матрицы. У стороннего пользователя кнопка «Матрица отклика...» роняла
        /// приложение молча: без окна ошибки и без строки в журнале. Так умирает процесс
        /// только от сбоя в нативном коде, а при открытии окна нативный код один — эта
        /// проба (`LoadLibrary` библиотеки, рантайм CUDA, драйвер NVIDIA). Нарушение
        /// доступа и переполнение стека .NET 4 не отдаёт ни в `catch` пробы, ни в
        /// `AppDomain.UnhandledException` журнала, поэтому ловить их в своём процессе
        /// нечем; ребёнок падает вместо приложения, и падение называется кодом.
        ///
        /// Ребёнок — само приложение (`Assembly.Location` этой сборки, а не входной: под
        /// пробой входная — проба) с ключом <see cref="ProbeChildKey"/>;
        /// `Program.Main` отвечает на него раньше всего прочего — без журнала, проверки
        /// второго экземпляра и окон. Ответ — строки «ключ=значение» в stdout
        /// (<see cref="WriteAnswer"/>). Ненулевой код выхода, молчание или таймаут —
        /// <see cref="GpuProbeReason.ProbeCrashed"/> / <see cref="GpuProbeReason.ProbeTimeout"/>.
        ///
        /// ⚠ Счёт на GPU (<see cref="RmGpu(string, long)"/>) по-прежнему идёт в процессе
        /// приложения (решение Amber 23.10.2026 вопросником, дословно: «Отдельный процесс
        /// (Рекомендую)» — о пробе); до него доходит лишь машина, где проба прошла.
        /// </summary>
        public static GpuProbe ProbeIsolated(string path)
        {
            var r = new GpuProbe { Path = path };
            try
            {
                string exe = typeof(RmGpu).Assembly.Location;
                var start = new ProcessStartInfo(exe, ProbeChildKey + " \"" + path + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exe)
                };
                using (Process child = Process.Start(start))
                {
                    Task<string> answer = child.StandardOutput.ReadToEndAsync();
                    if (!child.WaitForExit(ProbeChildTimeoutMs))
                    {
                        try
                        {
                            child.Kill();
                        }
                        catch (Exception)
                        {
                            // Успел выйти сам — всё равно отказ: ответа в срок не было.
                        }

                        r.Reason = "the GPU check process did not answer in "
                                   + (ProbeChildTimeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " s";
                        r.ReasonCode = GpuProbeReason.ProbeTimeout;
                        return r;
                    }

                    child.WaitForExit();
                    string text = answer.Wait(ProbeChildTimeoutMs) ? answer.Result : "";
                    r.ChildExitCode = child.ExitCode;
                    if (child.ExitCode != 0)
                    {
                        string at = LastStep(text);
                        r.Reason = "the GPU check process crashed in the native code"
                                   + (at.Length > 0 ? " at " + at : "") + " (exit code " + ExitCodeText(child.ExitCode) + ")";
                        r.ReasonCode = GpuProbeReason.ProbeCrashed;
                        return r;
                    }

                    if (!ReadAnswer(text, r))
                    {
                        r.Reason = "the GPU check process gave no answer";
                        r.ReasonCode = GpuProbeReason.ProbeCrashed;
                        return r;
                    }

                    return r;
                }
            }
            catch (Exception e)
            {
                // Не запустился сам ребёнок — отказ словами, как у пробы в процессе.
                r.Available = false;
                r.Reason = "the GPU check process did not start: " + e.GetType().Name + ": " + e.Message;
                r.ReasonCode = GpuProbeReason.RuntimeError;
                return r;
            }
        }

        /// <summary>Код выхода так, как его пишет Windows: «0xC0000005» — нарушение доступа, «0xC00000FD» — переполнение стека.</summary>
        public static string ExitCodeText(int code)
        {
            return "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Дочерняя сторона <see cref="ProbeIsolated"/>: проба в своём процессе, ответ в
        /// stdout, код 0. Окно отчёта Windows о сбое гасится (`SEM_NOGPFAULTERRORBOX`):
        /// пока оно висит, упавший ребёнок жив, и приложение ждало бы до таймаута.
        /// </summary>
        public static int ProbeChildMain(string path)
        {
            SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
            using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
            {
                // Шаг уходит родителю ДО нативного вызова и сразу: после сбоя буфер не сбросится.
                GpuProbe r = Probe(path, s =>
                {
                    output.WriteLine("step=" + s);
                    output.Flush();
                });
                WriteAnswer(output, r);
            }

            return 0;
        }

        /// <summary>Последний шаг `step=…` ответа ребёнка; нет — пусто.</summary>
        static string LastStep(string text)
        {
            string at = "";
            foreach (string line in (text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string l = line.TrimEnd('\r');
                if (l.StartsWith("step=", StringComparison.Ordinal)) at = l.Substring(5);
            }

            return at;
        }

        static void WriteAnswer(TextWriter output, GpuProbe r)
        {
            output.WriteLine("available=" + (r.Available ? "1" : "0"));
            output.WriteLine("reasonCode=" + ((int)r.ReasonCode).ToString(CultureInfo.InvariantCulture));
            output.WriteLine("reason=" + Escape(r.Reason));
            output.WriteLine("buildInfo=" + Escape(r.BuildInfo));
            output.WriteLine("libraryPhysics=" + r.LibraryPhysics.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("deviceCount=" + r.DeviceCount.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("deviceName=" + Escape(r.DeviceName));
            output.WriteLine("computeCapability=" + r.ComputeCapability.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("driverVersion=" + r.DriverVersion.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("runtimeVersion=" + r.RuntimeVersion.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("end=1");
        }

        /// <summary>Разбор <see cref="WriteAnswer"/>; без замыкающей строки `end=1` ответ не принимается.</summary>
        static bool ReadAnswer(string text, GpuProbe r)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in (text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string l = line.TrimEnd('\r');
                int eq = l.IndexOf('=');
                if (eq > 0) fields[l.Substring(0, eq)] = l.Substring(eq + 1);
            }

            if (!fields.ContainsKey("end")) return false;
            r.Available = Field(fields, "available") == 1;
            r.ReasonCode = (GpuProbeReason)Field(fields, "reasonCode");
            r.Reason = Unescape(fields, "reason");
            r.BuildInfo = Unescape(fields, "buildInfo");
            r.LibraryPhysics = Field(fields, "libraryPhysics");
            r.DeviceCount = Field(fields, "deviceCount");
            r.DeviceName = Unescape(fields, "deviceName");
            r.ComputeCapability = Field(fields, "computeCapability");
            r.DriverVersion = Field(fields, "driverVersion");
            r.RuntimeVersion = Field(fields, "runtimeVersion");
            return true;
        }

        static int Field(Dictionary<string, string> fields, string key)
        {
            string s;
            int v;
            return fields.TryGetValue(key, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static string Unescape(Dictionary<string, string> fields, string key)
        {
            string s;
            if (!fields.TryGetValue(key, out s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    sb.Append(c == 'n' ? '\n' : c == 'r' ? '\r' : c);
                }
                else
                {
                    sb.Append(s[i]);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// (`AMBER233`, П337, 18.10.2026) Нужен ли карте драйвер не старее рантайма сборки.
        /// Машинный код ядра собран под одну архитектуру (`arch=sm_86` паспорта), и его
        /// исполняют карты того же поколения не младше её (RTX 30xx 8.6, RTX 40xx 8.9). Карта
        /// другого поколения (RTX 50xx, 12.0) исполняет PTX через JIT драйвера, а JIT берёт
        /// PTX только тулкита не новее себя; драйвер старее рантайма отказывает тогда лишь
        /// при запуске ядра — галка выглядела бы годной, а счёт отказал бы на первом узле.
        /// Машинный код совместимость младших версий CUDA держит (драйвер 13.0 исполняет
        /// рантайм 13.4), поэтому для него драйвер с рантаймом не сверяется. Архитектуры в
        /// паспорте нет — считается, что машинный код не подходит (отказ строже, не мягче).
        /// </summary>
        public static bool DriverTooOldForJit(int ccMajor, int ccMinor, string buildInfo, int driverVersion, int runtimeVersion)
        {
            int arch = BuildInfoArch(buildInfo);
            bool nativeCode = arch > 0 && ccMajor == arch / 10 && ccMinor >= arch % 10;
            return !nativeCode && driverVersion < runtimeVersion;
        }

        /// <summary>Архитектура машинного кода из `arch=sm_NN;` строки `rm_build_info`; нет — −1.</summary>
        public static int BuildInfoArch(string info)
        {
            if (string.IsNullOrEmpty(info)) return -1;
            foreach (string part in info.Split(';'))
            {
                if (part.StartsWith("arch=sm_", StringComparison.Ordinal))
                {
                    int v;
                    return int.TryParse(part.Substring(8), NumberStyles.None, CultureInfo.InvariantCulture, out v) ? v : -1;
                }
            }

            return -1;
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
        /// упаковки ещё нет: сцена у узлов одна, и пересобирать 1.4 МБ упаковки на
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
                sim.EnsureBuilt();
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
        RuntimeError,
        /// <summary>(`AMBER261`) Дочерний процесс пробы упал (<see cref="RmGpu.ProbeIsolated"/>): сбой в драйвере или рантайме CUDA.</summary>
        ProbeCrashed,
        /// <summary>(`AMBER261`) Дочерний процесс пробы не ответил за <see cref="RmGpu.ProbeChildTimeoutMs"/>.</summary>
        ProbeTimeout
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
        /// <summary>Код выхода дочерней пробы (<see cref="RmGpu.ProbeIsolated"/>); у пробы в своём процессе 0.</summary>
        public int ChildExitCode;
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
            sim.channelHistograms = channels;
            try
            {
                relativeError = Run(gpu, sim, energyKev, histogram, binKev, key0, key1, reuseBlob, cancellation);
            }
            finally
            {
                sim.channelHistograms = null;
            }

            return channels;
        }

        /// <summary>Зеркало `EfficiencySimulator.Run` (:8605–8775) при `histogram != null`.</summary>
        static double Run(RmGpu gpu, EfficiencySimulator sim, double energyKev, double[] histogram, double binKev,
                          uint key0, uint key1, bool reuseBlob, CancellationToken cancellation)
        {
            gpu.Prepare(sim, reuseBlob);                        // зовёт EnsureBuilt
            MaterialDatabase.LightYieldCurve lightYield = sim.lightYield;
            double[] lightSum = lightYield != null ? new double[histogram.Length] : null;
            sim.lightSum = lightSum;
            sim.lightBinSplit = 0.0;
            sim.CountLightBinSplit = 0;
            sim.WeightLightBinSplit = 0.0;
            if (lightSum != null)
            {
                sim.LastPhotonLightScale = 0.0;
                sim.LastPhotonLightScaleSplit = 0.0;
            }

            int n = Math.Max(1000, sim.Histories);
            sim.resolutionWeighted = 0.0;
            sim.resolutionAnalog = 0.0;
            sim.LastResolutionPeakExtra = 0.0;
            double[][] channels = sim.channelHistograms;
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
            sim.resolutionWeighted = wScal[GpuSlots.W_RESOLUTION];
            sim.lightBinSplit = wScal[GpuSlots.W_LIGHT_BIN_SPLIT];
            sim.CountLightBinSplit += (long)wScal[GpuSlots.W_COUNT_LIGHT_BIN_SPLIT];
            sim.WeightLightBinSplit += wScal[GpuSlots.W_WEIGHT_LIGHT_BIN_SPLIT];
            sim.CountPathLimitCut += (long)wScal[GpuSlots.W_COUNT_PATH_LIMIT_CUT];
            sim.CountCascadeOverflow += (long)wScal[GpuSlots.W_COUNT_CASCADE_OVERFLOW];
            sim.CountEscapeDropped += (long)wScal[GpuSlots.W_COUNT_ESCAPE_DROPPED];
            sim.LastAngularMoments = Angular(n, wScal);

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
                sim.resolutionAnalog = aScal[GpuSlots.A_RESOLUTION];

                sim.LastContinuumIntegralError = scored > 0 ? 100.0 / Math.Sqrt(scored) : 100.0;
                sim.LastContinuumRelativeError = sim.SmearedContinuumError(hist, hist2, peak, binKev, energyKev);
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
            double resW = sim.resolutionWeighted;
            double resA = sim.resolutionAnalog;
            sim.LastResolutionPeakExtra = (analogBins ? resA : resW) / n;
            sim.FinishRun(energyKev, histogram, binKev, n, mean);
            return relativeError;
        }

        static AngularMomentSums Angular(long n, double[] s)
        {
            var a = new AngularMomentSums();
            a.N = n;
            a.S0 = s[GpuSlots.W_S0]; a.S2 = s[GpuSlots.W_S2]; a.S4 = s[GpuSlots.W_S4];
            a.S00 = s[GpuSlots.W_S00]; a.S22 = s[GpuSlots.W_S22]; a.S44 = s[GpuSlots.W_S44];
            a.S02 = s[GpuSlots.W_S02]; a.S04 = s[GpuSlots.W_S04];
            a.T0 = s[GpuSlots.W_T0]; a.T2 = s[GpuSlots.W_T2]; a.T4 = s[GpuSlots.W_T4];
            a.T00 = s[GpuSlots.W_T00]; a.T22 = s[GpuSlots.W_T22]; a.T44 = s[GpuSlots.W_T44];
            a.T02 = s[GpuSlots.W_T02]; a.T04 = s[GpuSlots.W_T04];
            return a;
        }
    }

    /// <summary>
    /// Зеркало ПЛОСКОГО пути `ResponseMatrixBuilder.Build` (`--target=0`). Узлы — по одному
    /// на GPU, сверху вниз по энергии (как CPU); κ пар — `BuildJoint` приложения (internal) на ЦП.
    /// </summary>
    public static class GpuBuild
    {
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

            // (П253) Сцена, которую порт не умеет, — отказ ДО счёта: прежде он приходил из
            // упаковки первого узла, когда κ пар на ЦП уже была запущена.
            string sceneProblem = GpuPack.SceneProblem(geometry, options);
            if (sceneProblem != null) throw new NotSupportedException(sceneProblem);

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
            // ⛔ (П253, 14.10.2026) У κ СВОЙ жетон, связанный с отменой счёта. Прежде κ шла
            // на жетоне отмены, и снимала её ТОЛЬКО отмена: отказ узла (устройство — TDR,
            // нехватка памяти, сцена, которую порт не умеет) уходил наружу, а κ на всех
            // ядрах ЦП продолжала считать в никуда и слать отчёты хода в окно, уже
            // сказавшее «не удалось». Теперь любой выход из счёта узлов снимает κ и ждёт её.
            var jointStop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = jointStop.Token };
            var jointMatrix = new ResponseMatrix();
            var jointWatch = Stopwatch.StartNew();
            double jointSeconds = 0.0;

            // (`AMBER220`, П246; решение Amber 07.10.2026, дословно: «Узлы + блоки κ,
            // план κ — предельный 96») Единица полосы — узел ИЛИ блок κ: узлы на
            // устройстве и κ на ЦП идут одновременно, и каждый досчитанный блок
            // двигает полосу так же, как узел. Счётчики узлов и κ — общие для
            // двух потоков отчёта; знаменатель κ — предел плана, чтобы добор не
            // пятил полосу (`A46`); последний отчёт ставит κ в предел.
            int jointUnits = ResponseMatrixBuilder.JointPlannedBlocks(geometry, options, grid.Length);
            int settledNodes = 0, jointDone = 0, jointNow = jointUnits > 0 ? ResponseMatrixBuilder.JointBlocks : 0;
            double lastEnergy = grid[grid.Length - 1];
            Action<bool> publish = (final) =>
            {
                if (progress == null) return;
                int settled = Volatile.Read(ref settledNodes);
                int jd = final ? jointUnits : Math.Min(jointUnits, Volatile.Read(ref jointDone));
                progress.Report(new ResponseMatrixProgress
                {
                    Done = settled,
                    Total = grid.Length,
                    DoneHistories = (long)settled * nominal,
                    TotalHistories = (long)grid.Length * nominal,
                    LastEnergyKev = Volatile.Read(ref lastEnergy),
                    StartedNodes = Math.Min(grid.Length, settled + 1),
                    SettledNodes = settled,
                    TotalNodes = grid.Length,
                    JointPlanned = jointUnits,
                    JointPlannedNow = Volatile.Read(ref jointNow),
                    JointDone = jd
                });
            };
            Action<int, int> jointReport = null;
            if (progress != null && jointUnits > 0)
            {
                jointReport = (d, now) =>
                {
                    Volatile.Write(ref jointDone, d);
                    Volatile.Write(ref jointNow, now);
                    publish(false);
                };
            }

            Task jointTask = Task.Run(() =>
            {
                ResponseMatrixBuilder.BuildJoint(geometry, options, jointMatrix, grid, parallel, jointReport);
                jointSeconds = jointWatch.Elapsed.TotalSeconds;
            }, jointStop.Token);

            Action<int, double> report = (settled, energy) =>
            {
                Volatile.Write(ref settledNodes, settled);
                Volatile.Write(ref lastEnergy, energy);
                publish(false);
            };

            // ⛔ (П271, 14.10.2026) Под одной защитой — всё до ожидания κ: узлы И то, что
            // идёт после них (отчёт хода, журнал, клеймо, нормировка, угловые моменты,
            // итоги, пик разрешения). Прежде отчёт хода после узлов, `ComputeStamp`,
            // `NormalizationOf` и `AngularAttenuation.FromMoments` стояли между двумя
            // защитами: бросок любого из них уходил наружу, а κ на ЦП продолжала считать (П270).
            ResponseMatrix matrix;
            try
            {
            for (int slot = 0; slot < grid.Length; slot++)
            {
                cancellation.ThrowIfCancellationRequested();
                int index = grid.Length - 1 - slot;
                long t0 = Stopwatch.GetTimestamp();
                double energyKev = grid[index];
                report(slot, energyKev);
                EfficiencySimulator sim = ResponseMatrixBuilder.MakeSimulator(geometry, options, index, energyKev);
                sim.Histories = Math.Max(1, nominal);
                double resolutionHalfWidth = ResponseMatrixBuilder.ResolutionHalfWidth(options, geometry, energyKev);
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
                    log.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                  "   GPU узел {0}/{1}: {2:F1} кэВ, {3:F2} с, упаковка {4} КБ",
                                  slot + 1, grid.Length, energyKev, nodeSeconds[index], gpu.LastBlobBytes / 1024));
                }
            }

            report(grid.Length, grid[0]);
            LastGpuSeconds = watch.Elapsed.TotalSeconds;
            if (log != null)
            {
                log.WriteLine(string.Format(CultureInfo.InvariantCulture,
                              "   GPU      : из них ядра {0:F1} с, упаковка и настройки {1:F1} с", gpu.KernelSeconds, gpu.PackSeconds));
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

            matrix = new ResponseMatrix
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
                Stamp = ResponseMatrix.ComputeStamp(geometry, options),
                Normalization = ResponseMatrix.NormalizationOf(geometry),
                CreatedUtc = DateTime.UtcNow,
                AngularQk = AngularAttenuation.FromMoments(grid, nodeAngular)
            };

            matrix.RebuildTotals();
            ResponseMatrixBuilder.FillResolutionPeak(geometry, matrix, nodeResolutionExtra);
            }
            catch
            {
                // Любой выход до ожидания κ — отмена, отказ устройства, отказ упаковки, отказ
                // сборки матрицы после узлов — снимает κ пар на ЦП и ждёт её: наружу не уходит
                // работающая задача, и после выхода построителя в окно не приходит ни одного
                // отчёта хода (П253, П271).
                StopJoint(jointStop, jointTask);
                throw;
            }

            try
            {
                jointTask.Wait();
            }
            catch (AggregateException e)
            {
                // `OperationCanceledException` κ пар — наружу той же природы, что у узлов.
                throw e.InnerException ?? e;
            }
            finally
            {
                jointStop.Dispose();
            }

            // (`AMBER220`) κ досчитана — полоса на 100 % ровно здесь, когда матрица собрана.
            publish(true);

            // Все поля `Joint*` временной матрицы — в итоговую (их и только их пишет BuildJoint,
            // ResponseMatrixBuilder.cs:575–603), в порядке объявления (ResponseMatrix.cs:850–887).
            // ⚠ Список поимённый: новое поле `Joint*`, которое начнёт писать `BuildJoint`, сюда
            // само не доедет — дописать его здесь в той же правке.
            matrix.JointEnergies = jointMatrix.JointEnergies;
            matrix.JointKappa = jointMatrix.JointKappa;
            matrix.JointKappaError = jointMatrix.JointKappaError;
            matrix.JointPoints = jointMatrix.JointPoints;
            matrix.JointMode = jointMatrix.JointMode;
            matrix.JointRawMedianNoise = jointMatrix.JointRawMedianNoise;
            matrix.JointRawMaxNoise = jointMatrix.JointRawMaxNoise;
            matrix.JointCellsCounted = jointMatrix.JointCellsCounted;
            matrix.JointSubstituted = jointMatrix.JointSubstituted;

            LastJointSeconds = jointSeconds;
            watch.Stop();
            matrix.BuildSeconds = watch.Elapsed.TotalSeconds;
            return matrix;
        }

        /// <summary>
        /// (П253) Снять κ пар и дождаться её — при любом выходе из счёта, кроме удачного.
        /// Свой отказ κ здесь не нужен: наружу уходит причина, по которой вышли узлы.
        /// Ожидание короткое — замер κ опрашивает отмену на каждой точке
        /// (<see cref="EfficiencySimulator.JointPeakSums(double[], int, double[], int)"/>).
        /// </summary>
        static void StopJoint(CancellationTokenSource jointStop, Task jointTask)
        {
            jointStop.Cancel();
            try { jointTask.Wait(); } catch (AggregateException) { }
            jointStop.Dispose();
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
