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

// ⚡ GPU-путь матрицы отклика — исполнитель (`AMBER160`, полоса П221). Довесок ко всем
// пробам (файл без `Main`). Упаковка — `GpuMatrix.cs`, натив — `tools/effmaker/gpu`.
//
// Что здесь и почему так:
//   * `RmGpu` — нативная `rmgpu.dll` (double, ступень 1) или `rmgpu_f.dll` (float,
//     рабочая), загружается ПО ЯВНОМУ ПУТИ (`LoadLibrary`): в каталог проб она не
//     кладётся, и план каталога (`appwd_plan.ps1`) о ней знать не обязан;
//   * `GpuNode.ResponseByChannel` — замена `EfficiencySimulator.ResponseByChannel` для
//     одного узла: GPU считает ОБА цикла `Run` (сырые суммы), а всё после циклов —
//     зеркало хвоста `Run` (EfficiencySimulator.cs:8665–8774) и вызов закрытых
//     `SmearedContinuumError`/`FinishRun` приложения отражением;
//   * `GpuBuild.Build` — зеркало ПЛОСКОГО пути `ResponseMatrixBuilder.Build`
//     (`--target=0`, ResponseMatrixBuilder.cs:151–531): симулятор узла даёт закрытый
//     `MakeSimulator` (те же настройки, что у CPU), после узлов — `RebuildTotals`,
//     закрытые `FillResolutionPeak` и `BuildJoint` (κ пар пока на ЦП);
//   * `GpuCheck` — ступень 1 приёмки: CPU гонит историю отражением, GPU повторяет её
//     с тем же состоянием xorshift64*, сравниваются истории поштучно.
//
// ⛔ Адаптивный останов по шуму (`ContinuumErrorTarget > 0`) GPU-путь НЕ повторяет —
// отказ, а не тихий плоский счёт: склад считается `--target=0`.

[StructLayout(LayoutKind.Sequential)]
struct GpuHistoryOut
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

sealed class RmGpu
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr LastErrorFn();
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
    public int Blocks = 0;           // 0 — постоянные нити по занятости (api.cu, rm_run)
    public int Threads = 128;

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
            throw new InvalidOperationException("GPU-путь: LoadLibrary(" + this.Path + ") — код " + Marshal.GetLastWin32Error()
                + " (нет драйвера CUDA или cudart рядом?)");
        }

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

    string LastError()
    {
        IntPtr p = this.lastError();
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
        var watch = Stopwatch.StartNew();
        foreach (KeyValuePair<string, double> kv in GpuPack.Settings(sim))
        {
            this.Ok(this.cfgSet(kv.Key, kv.Value), "rm_cfg_set(" + kv.Key + ")");
        }

        this.Ok(this.cfgCommit(), "rm_cfg_commit");
        if (!reuse || this.lastBlob == null)
        {
            byte[] blob = GpuPack.Pack(sim);
            this.Ok(this.load(blob, blob.Length), "rm_load");
            this.lastBlob = blob;
            this.LastBlobBytes = blob.Length;
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

    public void Run(int branch, double energyKev, double binKev, int bins, long n, long first, int rngMode,
                    ulong[] states, uint key0, uint key1,
                    double[] hist, double[] hist2, double[] chan, double[] light, double[] scal,
                    GpuHistoryOut[] perHistory)
    {
        var watch = Stopwatch.StartNew();
        this.Ok(this.run(branch, energyKev, binKev, bins, n, first, rngMode, states, key0, key1,
                         hist, hist2, chan, light, scal, scal.Length, perHistory, this.Blocks, this.Threads),
                "rm_run(ветвь " + branch + ")");
        this.KernelSeconds += watch.Elapsed.TotalSeconds;
    }
}

/// <summary>Слоты накопителей — те же номера, что `tally.cuh`.</summary>
static class GpuSlots
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
static class GpuNode
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
        if (!(energyKev > 0.0) || !(binKev > 0.0)) throw new ArgumentOutOfRangeException("binKev");
        int bins = EfficiencySimulator.PeakBin(energyKev, binKev) + 1;
        var channels = new double[Channels][];
        for (int c = 0; c < Channels; c++) channels[c] = new double[bins];
        double[] histogram = new double[bins];
        GpuReflect.Set(sim, "channelHistograms", channels);
        try
        {
            relativeError = Run(gpu, sim, energyKev, histogram, binKev, key0, key1, reuseBlob);
        }
        finally
        {
            GpuReflect.Set(sim, "channelHistograms", null);
        }

        return channels;
    }

    /// <summary>Зеркало `EfficiencySimulator.Run` (:8605–8775) при `histogram != null`.</summary>
    static double Run(RmGpu gpu, EfficiencySimulator sim, double energyKev, double[] histogram, double binKev,
                      uint key0, uint key1, bool reuseBlob)
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
        gpu.Run(0, energyKev, binKev, bins, n, 0, 1, null, key0, key1, wHist, null, wChan, wLight, wScal, null);
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
            gpu.Run(1, energyKev, binKev, bins, n, 1L << 40, 1, null, key0, key1, hist, hist2, aChan, light, aScal, null);
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
static class GpuBuild
{
    static readonly Type Builder = typeof(ResponseMatrixBuilder);

    public static double LastGpuSeconds, LastJointSeconds;

    public static ResponseMatrix Build(RmGpu gpu, GeometryModel geometry, ResponseMatrixOptions options, TextWriter log)
    {
        if (options.ContinuumErrorTarget > 0.0)
        {
            throw new NotSupportedException("GPU-путь считает только плоско (--target=0): адаптивный останов по шуму "
                + "(ContinuumErrorTarget = " + options.ContinuumErrorTarget.ToString(CultureInfo.InvariantCulture)
                + ") не перенесён");
        }

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
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = CancellationToken.None };
        var jointMatrix = new ResponseMatrix();
        var jointWatch = Stopwatch.StartNew();
        double jointSeconds = 0.0;
        Task jointTask = Task.Run(() =>
        {
            GpuReflect.Call(Builder, "BuildJoint", geometry, options, jointMatrix, grid, parallel);
            jointSeconds = jointWatch.Elapsed.TotalSeconds;
        });

        for (int slot = 0; slot < grid.Length; slot++)
        {
            int index = grid.Length - 1 - slot;
            long t0 = Stopwatch.GetTimestamp();
            double energyKev = grid[index];
            var sim = (EfficiencySimulator)GpuReflect.Call(Builder, "MakeSimulator", geometry, options, index, energyKev);
            sim.Histories = Math.Max(1, nominal);
            double resolutionHalfWidth = (double)GpuReflect.Call(Builder, "ResolutionHalfWidth", options, geometry, energyKev);
            sim.ResolutionPeakHalfWidthKev = resolutionHalfWidth;
            uint key0, key1;
            GpuNode.NodeKey(options, sim, index, out key0, out key1);
            double relativeError;
            if (slot == 0) gpu.ForgetBlob();
            double[][] histograms = GpuNode.ResponseByChannel(gpu, sim, energyKev, options.BinKev, key0, key1,
                                                              out relativeError, true);
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
