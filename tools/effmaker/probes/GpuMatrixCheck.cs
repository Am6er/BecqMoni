using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

// ⚡ GPU-путь матрицы отклика — СТУПЕНЬ 1 ПРИЁМКИ (`AMBER160`, полоса П221): сверка
// переноса ПО ИСТОРИЯМ. Довесок ко всем пробам (файл без `Main`).
//
// CPU гонит историю отражением (`source.NextWeighted` + `OneHistory`, либо
// `AnalogHistory`) и запоминает состояние xorshift64* на её начало; GPU (`rmgpu.dll`,
// `real = double`, `-fmad=false`) повторяет ту же историю с тем же состоянием.
// Сравниваются: возврат истории, вес, занос, свет, косинус, бин, канал — и СОСТОЯНИЕ
// ГСЧ ПОСЛЕ истории, то есть число выдач: разойдись порядок розыгрышей хоть раз, оно
// разойдётся навсегда, и это видно даже там, где числа случайно совпали.
//
// Допустимы только РЕДКИЕ развилки: `log`/`exp`/`pow` CUDA и .NET расходятся в последнем
// разряде, и сравнение на границе (`u < p`) изредка уходит в другую ветку. Доля
// расходящихся историй печатается; систематика (одна и та же функция, все истории
// одного вида) — дефект переноса.
static class GpuCheck
{
    sealed class Acc
    {
        readonly FieldInfo f;
        public Acc(Type t, string name)
        {
            for (Type k = t; k != null && this.f == null; k = k.BaseType)
            {
                this.f = k.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (this.f == null) throw new MissingFieldException(t.FullName, name);
        }

        public object Get(object o) { return this.f.GetValue(o); }
        public void Set(object o, object v) { this.f.SetValue(o, v); }
    }

    static MethodInfo Method(Type t, string name, int args)
    {
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name == name && m.GetParameters().Length == args) return m;
        }

        throw new MissingMethodException(t.FullName, name);
    }

    static bool Same(double a, double b)
    {
        if (a == b) return true;
        double scale = Math.Max(Math.Abs(a), Math.Abs(b));
        return Math.Abs(a - b) <= 1e-9 * Math.Max(scale, 1e-300);
    }

    /// <summary>Сверка ветви `branch` (0 — взвешенная, 1 — аналоговая) на узле `index`.</summary>
    public static int Run(RmGpu gpu, GeometryModel geometry, ResponseMatrixOptions options, int index,
                          int histories, int branch, TextWriter log)
    {
        // Ступень 1 — double-сборка. Float-сборку пускает только замер выбросов
        // (`BQ_GPU_CHECK_FLOAT=1`): развилки там везде, ищутся ВЕЛИЧИНЫ (наибольший |возврат|).
        bool floatProbe = Environment.GetEnvironmentVariable("BQ_GPU_CHECK_FLOAT") == "1";
        if (gpu.RealBytes != 8 && !floatProbe) throw new InvalidOperationException("ступень 1 — только double-сборка rmgpu.dll");
        Type builder = typeof(ResponseMatrixBuilder);
        double[] grid = options.BuildGrid(geometry);
        double energyKev = grid[index];
        double binKev = options.BinKev;
        var sim = (EfficiencySimulator)GpuReflect.Call(builder, "MakeSimulator", geometry, options, index, energyKev);
        sim.Histories = histories;
        sim.ResolutionPeakHalfWidthKev = (double)GpuReflect.Call(builder, "ResolutionHalfWidth", options, geometry, energyKev);
        gpu.Prepare(sim);

        Type st = typeof(EfficiencySimulator);
        int bins = EfficiencySimulator.PeakBin(energyKev, binKev) + 1;
        int peak = bins - 1;
        object lightYield = GpuReflect.Field(sim, "lightYield");
        double[] lightSum = lightYield != null ? new double[bins] : null;
        GpuReflect.Set(sim, "lightSum", lightSum);
        var channels = new double[7][];
        for (int c = 0; c < 7; c++) channels[c] = new double[bins];
        GpuReflect.Set(sim, "channelHistograms", channels);
        GpuReflect.Set(sim, "resolutionWeighted", 0.0);
        GpuReflect.Set(sim, "resolutionAnalog", 0.0);

        object source = GpuReflect.Field(sim, "source");
        GpuReflect.Call(source, "Retune", sim, energyKev);
        MethodInfo next = Method(source.GetType(), "NextWeighted", 4);
        MethodInfo one = Method(st, "OneHistory", 7);
        MethodInfo analog = Method(st, "AnalogHistory", 9);
        MethodInfo binOf = Method(st, "BinOf", 4);
        MethodInfo channelOf3 = null;
        foreach (MethodInfo m in st.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (m.Name == "ChannelOf" && m.GetParameters().Length == 3) channelOf3 = m;
        }

        var state = new Acc(st, "state");
        var historyDeposit = new Acc(st, "historyDeposit");
        var lightDeposit = new Acc(st, "lightDeposit");
        var lastCos = new Acc(st, "lastHistoryCos");

        var states = new ulong[histories];
        var cpu = new GpuHistoryOut[histories];
        double[] cpuHist = new double[bins];
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < histories; i++)
        {
            states[i] = (ulong)state.Get(sim);
            object[] a = { sim, 0.0, 0.0, 0.0 };
            double pointWeight = (double)next.Invoke(source, a);
            double x = (double)a[1], y = (double)a[2], z = (double)a[3];
            var o = new GpuHistoryOut { Channel = -1, Bin = -1 };
            if (branch == 0)
            {
                o.Score = (double)one.Invoke(sim, new object[] { energyKev, x, y, z, cpuHist, binKev, pointWeight });
                o.Weight = pointWeight;
                o.DepositA = (double)historyDeposit.Get(sim);
                o.Light = (double)lightDeposit.Get(sim);
                o.Cos = (double)lastCos.Get(sim);
            }
            else
            {
                object[] b = { energyKev, x, y, z, pointWeight, false, false, 0.0, false };
                double deposited = (double)analog.Invoke(sim, b);
                o.Score = deposited;
                o.Weight = (double)b[4];
                o.DepositA = (double)b[7];
                o.Light = (double)lightDeposit.Get(sim);
                o.Flags = ((bool)b[6] ? 1 : 0) | ((bool)b[8] ? 2 : 0);
                if (deposited > 0.0)
                {
                    o.Bin = (int)binOf.Invoke(sim, new object[] { peak, binKev, energyKev, deposited });
                    if (channelOf3 != null)
                    {
                        o.Channel = Convert.ToInt32(channelOf3.Invoke(sim, new object[] { energyKev - deposited, deposited, o.DepositA }));
                    }
                }
            }

            o.RngAfter = (ulong)state.Get(sim);
            cpu[i] = o;
        }

        double cpuSeconds = watch.Elapsed.TotalSeconds;

        // ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ сверки: `BQ_GPU_CHECK_POISON=1` портит младший бит
        // состояния каждой ЧЁТНОЙ истории перед отдачей GPU — сверка обязана назвать
        // примерно половину историй расходящимися. Сверка, которая молчит и тут, не
        // сверяет ничего.
        if (Environment.GetEnvironmentVariable("BQ_GPU_CHECK_POISON") == "1")
        {
            for (int i = 0; i < histories; i += 2) states[i] ^= 2UL;
            log.WriteLine("   ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: состояния чётных историй испорчены");
        }

        var gpuOut = new GpuHistoryOut[histories];
        double[] gHist = new double[bins], gHist2 = new double[bins], gChan = new double[bins * 7], gLight = new double[bins];
        double[] gScal = new double[gpu.Slots(branch)];
        watch.Restart();
        gpu.Run(branch, energyKev, binKev, bins, histories, 0, 0, states, 0, 0,
                gHist, branch == 1 ? gHist2 : null, gChan, gLight, gScal, gpuOut);
        double gpuSeconds = watch.Elapsed.TotalSeconds;

        int rngDiff = 0, scoreDiff = 0, weightDiff = 0, depDiff = 0, lightDiff = 0, cosDiff = 0, binDiff = 0, chanDiff = 0, flagDiff = 0;
        var shown = new List<string>();
        for (int i = 0; i < histories; i++)
        {
            GpuHistoryOut c = cpu[i], g = gpuOut[i];
            bool bad = false;
            if (c.RngAfter != g.RngAfter) { rngDiff++; bad = true; }
            if (!Same(c.Score, g.Score)) { scoreDiff++; bad = true; }
            if (!Same(c.Weight, g.Weight)) { weightDiff++; bad = true; }
            if (!Same(c.DepositA, g.DepositA)) { depDiff++; bad = true; }
            if (!Same(c.Light, g.Light)) { lightDiff++; bad = true; }
            if (branch == 0 && !Same(c.Cos, g.Cos)) { cosDiff++; bad = true; }
            if (branch == 1 && c.Bin != g.Bin) { binDiff++; bad = true; }
            if (branch == 1 && c.Bin >= 0 && c.Bin != peak && g.Channel >= 0 && c.Channel != g.Channel
                && !(c.Channel == 0 && g.Channel == 1)) { chanDiff++; bad = true; }
            if (branch == 1 && c.Flags != g.Flags) { flagDiff++; bad = true; }
            if (bad && shown.Count < 12)
            {
                shown.Add(string.Format(CultureInfo.InvariantCulture,
                    "   #{0}: CPU score {1:R} w {2:R} dep {3:R} light {4:R} bin {5} ch {6} rng {7:X16} | GPU score {8:R} w {9:R} dep {10:R} light {11:R} bin {12} ch {13} rng {14:X16}",
                    i, c.Score, c.Weight, c.DepositA, c.Light, c.Bin, c.Channel, c.RngAfter,
                    g.Score, g.Weight, g.DepositA, g.Light, g.Bin, g.Channel, g.RngAfter));
            }
        }

        int anyBad = 0;
        for (int i = 0; i < histories; i++)
        {
            GpuHistoryOut c = cpu[i], g = gpuOut[i];
            if (c.RngAfter != g.RngAfter || !Same(c.Score, g.Score) || !Same(c.Light, g.Light)) anyBad++;
        }

        log.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "СВЕРКА {0} ветви, узел {1} ({2:F3} кэВ), историй {3}: CPU {4:F1} с, GPU {5:F2} с",
            branch == 0 ? "взвешенной" : "аналоговой", index, energyKev, histories, cpuSeconds, gpuSeconds));
        log.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "   расходятся: ГСЧ после {0}, возврат {1}, вес {2}, занос {3}, свет {4}, cos {5}, бин {6}, канал {7}, признаки {8}; историй с любой развилкой {9} ({10:F4} %)",
            rngDiff, scoreDiff, weightDiff, depDiff, lightDiff, cosDiff, binDiff, chanDiff, flagDiff, anyBad,
            100.0 * anyBad / Math.Max(1, histories)));
        foreach (string s in shown) log.WriteLine(s);

        // АРБИТР развилки (`A315`): расходящуюся историю переигрывает СВЕЖИЙ CPU-симулятор
        // (новый `MakeSimulator`, то же состояние ГСЧ, без предыдущей истории). Совпал
        // свежий CPU с GPU — значит, CPU-поток получил своё число от состояния, оставленного
        // прошлой историей, а GPU прав. Только взвешенная ветвь и не больше 12 историй.
        if (branch == 0)
        {
            int replayed = 0;
            for (int i = 0; i < histories && replayed < 12; i++)
            {
                if (cpu[i].RngAfter == gpuOut[i].RngAfter && Same(cpu[i].Score, gpuOut[i].Score)) continue;
                replayed++;
                var fresh = (EfficiencySimulator)GpuReflect.Call(builder, "MakeSimulator", geometry, options, index, energyKev);
                fresh.Histories = histories;
                fresh.ResolutionPeakHalfWidthKev = sim.ResolutionPeakHalfWidthKev;
                GpuReflect.Call(fresh, "EnsureBuilt");
                GpuReflect.Set(fresh, "lightSum", lightYield != null ? new double[bins] : null);
                var freshChannels = new double[7][];
                for (int c = 0; c < 7; c++) freshChannels[c] = new double[bins];
                GpuReflect.Set(fresh, "channelHistograms", freshChannels);
                object freshSource = GpuReflect.Field(fresh, "source");
                GpuReflect.Call(freshSource, "Retune", fresh, energyKev);
                state.Set(fresh, states[i]);
                object[] fa = { fresh, 0.0, 0.0, 0.0 };
                double fw = (double)next.Invoke(freshSource, fa);
                double fs = (double)one.Invoke(fresh, new object[] { energyKev, (double)fa[1], (double)fa[2], (double)fa[3], new double[bins], binKev, fw });
                log.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   арбитр #{0}: CPU в потоке {1:R}, CPU свежий {2:R}, GPU {3:R} — {4}",
                    i, cpu[i].Score, fs, gpuOut[i].Score,
                    Same(fs, gpuOut[i].Score) ? "свежий CPU = GPU (поток CPU несёт состояние прошлой истории)"
                    : Same(fs, cpu[i].Score) ? "свежий CPU = CPU в потоке (расходится GPU)" : "все три разные"));
            }
        }
        {
            int worst = 0;
            for (int i = 1; i < histories; i++)
            {
                if (Math.Abs(gpuOut[i].Score) + Math.Abs(gpuOut[i].Light) > Math.Abs(gpuOut[worst].Score) + Math.Abs(gpuOut[worst].Light)) worst = i;
            }

            log.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   наибольший выход GPU: #{0} score {1:R} w {2:R} dep {3:R} light {4:R} | CPU score {5:R} light {6:R}",
                worst, gpuOut[worst].Score, gpuOut[worst].Weight, gpuOut[worst].DepositA, gpuOut[worst].Light,
                cpu[worst].Score, cpu[worst].Light));
        }

        // Суммы: CPU-гистограмма (взвешенная клала её сама) против GPU.
        if (branch == 0)
        {
            double maxRel = 0.0;
            for (int b = 0; b < bins; b++)
            {
                double s = Math.Max(Math.Abs(cpuHist[b]), Math.Abs(gHist[b]));
                if (s > 0.0) maxRel = Math.Max(maxRel, Math.Abs(cpuHist[b] - gHist[b]) / s);
            }

            log.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   гистограмма: max |Δ|/x по бинам {0:E3}; бин пика CPU {1:R} GPU {2:R}", maxRel, cpuHist[peak], gHist[peak]));
        }

        return anyBad;
    }
}
