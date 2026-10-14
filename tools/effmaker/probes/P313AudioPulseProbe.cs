// Проба полосы П313 (15.10.2026): ТРАКТ ИМПУЛЬСОВ ЗВУКОВОГО ВХОДА БЕЗ ПРИБОРА.
//
// Синтетический поток отсчётов звуковой карты — импульсы известной амплитуды и формы
// (двойная экспонента: фронт tr, спад td, мкс), гауссов шум, пуассоновы времена прихода,
// наложения, зашкал — кодируется в байты 16/24/32 бит (знак — по полярности) и
// прогоняется через НАСТОЯЩИЙ код приложения:
//   * эталонный импульс снимает `StandardPulseRecorder.DetectPulse` (как кнопка «Запись
//     эталонного импульса» в окне звукового прибора), форма берётся из его поля
//     `standardPulseShape` отражением;
//   * набор ведёт `CorrelativePulseDetector` дверью `PulseDetector.ProcessWaveData(byte[])` —
//     ровно то, что зовёт `AudioInputDeviceController.waveIn_DataReady` на каждый буфер
//     WaveIn (`BufferSize = SamplesPerSecond / 50 * 2` отсчётов);
//   * живое время — `Utils.LiveTime.Calculate(T, TotalPulseCount, config.DeadTime())`, как
//     в `AudioInputDeviceController.StopMeasurement` и такте `MeasurementController.OnTimer`.
//
// Разделы:
//   L  линейность: моноэнергетические потоки 5…80 % ПШ → средняя высота, канал, высота/A;
//   B  разрядность и полярность: один поток 16/24/32 бит × (+, −) → один спектр;
//   F  границы буферов: один поток буферами 1920/441/100/17/16 → один список импульсов;
//      ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ сравнения — буфер 7 < S (окна, нуждающиеся в позапрошлом
//      буфере, теряются; в WaveIn короткий буфер бывает только последним, на «Стопе»);
//   O  смещение нуля АЦП +3 % ПШ → спектр тот же;
//   R  скорость счёта и живое время: 10…3000 имп/с, линия 30 % (+ сплошное 5…60 %) →
//      отсчёты / живое время против заложенной скорости; τ, при котором сошлось бы;
//   C  зашкал: 80…200 % ПШ, 16 бит → доля в спектре, канал, вклад в TotalPulseCount;
//   U  импульсы выше верхнего порога высоты (порог 50, половина событий 70 %): ДО П313 они
//      не шли в TotalPulseCount — их мёртвое время терялось, живое время росло, скорость
//      линии падала; ПРАВИЛО: при той же полной скорости живое время от их доли не зависит;
//   S  частота дискретизации 44.1/48/96/192 кГц (S ∝ fs): эталон и высота линии;
//   W  `WaveIn.BufferQueueSize = 0` (без устройства: `new WaveIn(-1)` устройства не открывает):
//      ДО П313 сеттер проверял прежнее значение поля и ноль принимал молча; ПРАВИЛО — отказ
//      `ArgumentOutOfRangeException`, как у близнеца `BufferSize = 0` (он — контроль).
//
// Один exe пробы работает со сборкой ДО правки П313 (обязан показать дефекты U и W) и ПОСЛЕ.
// Коды: 0 — правила U и W выполнены, контроли сработали (сборка ПОСЛЕ); 1 — дефект U или W виден
// (сборка ДО); 3 — положительный контроль (раздел F, U или W) НЕ сработал — замер слеп;
// 5 — обрыв. Остальные разделы — числа для журнала, кода не меняют.
//
//   P313AudioPulseProbe.exe            (из каталога, где лежит BecquerelMonitor.exe)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BecquerelMonitor;
using WinMM;

static class P313AudioPulseProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    // Форма импульса: двойная экспонента, время в микросекундах.
    const double TrUs = 15.0;
    const double TdUs = 60.0;
    const double NoiseFs = 0.15;          // σ шума, % полной шкалы
    const double ChannelPitch = 0.04;     // как DeviceConfigInfo по умолчанию
    const int Channels = 4096;

    struct Ev
    {
        public double T;   // с
        public double A;   // % ПШ (знак — физический)
    }

    sealed class Run
    {
        public int[] Spectrum;
        public long Total;
        public long Valid;
        public long Samples;
        public List<Pulse> Pulses;
        public double LiveTime;
        public double DeadTime;
    }

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Console.WriteLine("приложение: " + typeof(PulseDetector).Assembly.Location);
            Console.WriteLine(string.Format(Inv, "форма: tr = {0} мкс, td = {1} мкс; шум σ = {2} % ПШ; шаг канала {3}, каналов {4}",
                TrUs, TdUs, NoiseFs, ChannelPitch, Channels));

            int fs = 48000, S = 16, peak = 8;
            double[] ideal = RecordStandard(fs, S, peak, TrUs, TdUs, 20.0, 3000, out int nStd, out int nOffered);
            Console.WriteLine(string.Format(Inv, "эталон 48 кГц: принято {0} из {1} импульсов; форма: {2}",
                nStd, nOffered, string.Join(" ", ideal.Select(v => v.ToString("F3", Inv)))));
            Console.WriteLine(string.Format(Inv, "  Σ эталона = {0:E2} (смещение нуля в высоту не идёт, только если ≈ 0)", ideal.Sum()));

            SectionL(fs, S, peak, ideal);
            SectionB(fs, S, peak, ideal);
            int ctl = SectionF(fs, S, peak, ideal);
            SectionO(fs, S, peak, ideal);
            SectionR(fs, S, peak, ideal);
            SectionC(fs, S, peak, ideal);
            int u = SectionU(fs, S, peak, ideal);
            SectionS();
            int w = SectionW();
            if (w == 3) u = 3;
            else if (w == 1 && u == 0) u = 1;
            if (ctl != 0 || u == 3)
            {
                Console.WriteLine("⛔ ИТОГ: положительный контроль не сработал — замер слеп (код 3)");
                return 3;
            }
            if (u == 1)
            {
                Console.WriteLine("⛔ ИТОГ: ДЕФЕКТ виден (раздел U и/или W) — код 1; так обязана отвечать сборка ДО П313");
                return 1;
            }
            Console.WriteLine("ИТОГ: правила разделов U и W выполнены, контроли сработали (код 0)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ ОБРЫВ: " + ex);
            return 5;
        }
    }

    // ───────────────────────────── поток ─────────────────────────────

    static double PeakNorm(double tr, double td)
    {
        double tpk = tr * td / (td - tr) * Math.Log(td / tr);
        return Math.Exp(-tpk / td) - Math.Exp(-tpk / tr);
    }

    /// <summary>Поток в % ПШ: сумма импульсов + шум + смещение.</summary>
    static double[] Synthesize(int fs, double seconds, List<Ev> evs, double tr, double td, double noise, double offset, int seed)
    {
        long n = (long)Math.Round(seconds * fs);
        double[] x = new double[n];
        double norm = PeakNorm(tr, td);
        int sup = (int)Math.Ceiling(12.0 * td * 1e-6 * fs) + 2;
        foreach (Ev e in evs)
        {
            double t0 = e.T * fs;                 // в отсчётах, дробное
            long first = (long)Math.Ceiling(t0);
            for (long k = first; k < first + sup && k < n; k++)
            {
                double dtUs = (k - t0) / fs * 1e6;
                if (dtUs <= 0) continue;
                x[k] += e.A * (Math.Exp(-dtUs / td) - Math.Exp(-dtUs / tr)) / norm;
            }
        }
        Random rnd = new Random(seed);
        for (long k = 0; k < n; k++)
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            x[k] += offset + noise * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
        return x;
    }

    static List<Ev> Poisson(double rate, double seconds, Func<Random, double> amp, int seed)
    {
        Random rnd = new Random(seed);
        List<Ev> list = new List<Ev>();
        double t = 0.001;
        while (true)
        {
            t += -Math.Log(1.0 - rnd.NextDouble()) / rate;
            if (t >= seconds - 0.002) break;
            list.Add(new Ev { T = t, A = amp(rnd) });
        }
        return list;
    }

    static List<Ev> Regular(double period, double seconds, double a, int seed)
    {
        Random rnd = new Random(seed);
        List<Ev> list = new List<Ev>();
        for (double t = 0.002; t < seconds - 0.002; t += period)
        {
            list.Add(new Ev { T = t + rnd.NextDouble() * 1e-4, A = a });
        }
        return list;
    }

    /// <summary>% ПШ → байты PCM заданной разрядности; зашкал — зажим на код ПШ, как АЦП.</summary>
    static byte[] Encode(double[] x, int bits)
    {
        int bps = bits / 8;
        byte[] b = new byte[x.Length * bps];
        double fsCode = bits == 16 ? 32768.0 : bits == 24 ? 8388608.0 : 2147483648.0;
        for (int i = 0; i < x.Length; i++)
        {
            double c = Math.Round(x[i] / 100.0 * fsCode);
            if (c > fsCode - 1) c = fsCode - 1;
            if (c < -fsCode) c = -fsCode;
            long v = (long)c;
            for (int k = 0; k < bps; k++)
            {
                b[i * bps + k] = (byte)((v >> (8 * k)) & 0xFF);
            }
        }
        return b;
    }

    // ───────────────────────────── код приложения ─────────────────────────────

    static AudioInputDeviceConfig Config(int fs, int bits, bool neg, int S, int peak, double[] ideal,
                                         double lower = 1.0, double upper = 100.0, double corr = 0.6)
    {
        AudioInputDeviceConfig c = new AudioInputDeviceConfig();
        c.SamplesPerSecond = fs;
        c.BitsPerSample = bits;
        c.NegativePolarity = neg;
        PRAHomageMethodConfig p = (PRAHomageMethodConfig)c.PulseDetectionMethodConfig;
        p.PulseShapeSize = S;
        p.PeakIndex = peak;
        p.LowerThreshold = lower;
        p.UpperThreshold = upper;
        p.PulseThreshold = corr;
        if (ideal != null) p.PulseShape = (double[])ideal.Clone();
        return c;
    }

    static double[] RecordStandard(int fs, int S, int peak, double tr, double td, double amp, int n,
                                   out int accepted, out int offered)
    {
        AudioInputDeviceConfig c = Config(fs, 16, false, S, peak, null);
        StandardPulseRecorder rec = new StandardPulseRecorder();
        rec.Initialize(c);
        double seconds = n * 0.01 + 0.01;
        List<Ev> evs = Regular(0.01, seconds, amp, 11);
        offered = evs.Count;
        double[] x = Synthesize(fs, seconds, evs, tr, td, NoiseFs, 0.0, 12);
        // те же 16 бит, что у набора: ProcessWaveData приватен — декодируем как он (код 16 бит)
        int buf = fs / 50 * 2;
        for (int s0 = 0; s0 < x.Length; s0 += buf)
        {
            int len = Math.Min(buf, x.Length - s0);
            double[] chunk = new double[len];
            for (int i = 0; i < len; i++)
            {
                double code = Math.Round(x[s0 + i] / 100.0 * 32768.0);
                if (code > 32767) code = 32767;
                if (code < -32768) code = -32768;
                chunk[i] = (double)(short)code * 100.0 / 32768.0;
            }
            rec.DetectPulse(chunk);
        }
        accepted = rec.NumberOfPulses;
        return (double[])((double[])typeof(StandardPulseRecorder).GetField("standardPulseShape", F).GetValue(rec)).Clone();
    }

    static Run Detect(int fs, int bits, bool neg, int S, int peak, double[] ideal, double[] x, int buf,
                      double lower = 1.0, double upper = 100.0)
    {
        AudioInputDeviceConfig c = Config(fs, bits, neg, S, peak, ideal, lower, upper);
        WaveFormat wf = new WaveFormat { FormatTag = WaveFormatTag.Pcm, SamplesPerSecond = fs, BitsPerSample = (short)bits, Channels = 1 };
        CorrelativePulseDetector det = new CorrelativePulseDetector();
        det.Pulses = new PulseCollection();
        det.Initialize(c, wf, 0L);
        EnergySpectrum es = new EnergySpectrum(ChannelPitch, Channels);
        det.EnergySpectrum = es;
        double[] phys = neg ? x.Select(v => -v).ToArray() : x;
        byte[] all = Encode(phys, bits);
        int bps = bits / 8;
        for (int s0 = 0; s0 < x.Length; s0 += buf)
        {
            int len = Math.Min(buf, x.Length - s0);
            byte[] chunk = new byte[len * bps];
            Buffer.BlockCopy(all, s0 * bps, chunk, 0, len * bps);
            det.ProcessWaveData(chunk);
        }
        Run r = new Run();
        r.Spectrum = (int[])es.Spectrum.Clone();
        r.Total = es.TotalPulseCount;
        r.Valid = es.ValidPulseCount;
        r.Samples = det.TimeInSamples;
        r.Pulses = det.Pulses.Pulses.ToList();
        r.DeadTime = c.DeadTime();
        double T = x.Length / (double)fs;
        r.LiveTime = BecquerelMonitor.Utils.LiveTime.Calculate(T, r.Total, r.DeadTime);
        return r;
    }

    static void Stats(IEnumerable<double> v, out double mean, out double sd, out int n)
    {
        double[] a = v.ToArray();
        n = a.Length;
        mean = n > 0 ? a.Average() : double.NaN;
        double m = mean;
        sd = n > 1 ? Math.Sqrt(a.Sum(q => (q - m) * (q - m)) / (n - 1)) : double.NaN;
    }

    static string G(double v, int d = 3) { return v.ToString("F" + d.ToString(Inv), Inv); }

    // ───────────────────────────── разделы ─────────────────────────────

    static void SectionL(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── L: линейность (16 бит, 20 имп/с, 20 с на амплитуду, буфер 1920)");
        Console.WriteLine("  A,%ПШ | принято/подано | высота ср. | σ | высота/A | канал ср. | Total");
        foreach (double a in new[] { 5.0, 10.0, 20.0, 40.0, 60.0, 80.0 })
        {
            List<Ev> evs = Poisson(20, 20, rg => a, 100 + (int)a);
            double[] x = Synthesize(fs, 20, evs, TrUs, TdUs, NoiseFs, 0, 200 + (int)a);
            Run r = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
            Stats(r.Pulses.Select(p => p.Height), out double m, out double sd, out int n);
            Console.WriteLine(string.Format(Inv, "  {0,5} | {1}/{2} | {3} | {4} | {5} | {6} | {7}",
                a, n, evs.Count, G(m), G(sd), G(m / a, 4), G(m / ChannelPitch, 1), r.Total));
        }
    }

    static void SectionB(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── B: разрядность и полярность (300 имп/с, линия 30 % + сплошное 5…60 %, 30 с)");
        List<Ev> evs = Poisson(300, 30, rg => rg.NextDouble() < 0.5 ? 30.0 : 5.0 + 55.0 * rg.NextDouble(), 31);
        double[] x = Synthesize(fs, 30, evs, TrUs, TdUs, NoiseFs, 0, 32);
        Run baseRun = null;
        foreach (int bits in new[] { 16, 24, 32 })
        {
            foreach (bool neg in new[] { false, true })
            {
                Run r = Detect(fs, bits, neg, S, peak, ideal, x, fs / 50 * 2);
                if (baseRun == null) baseRun = r;
                int diffCh = 0;
                for (int i = 0; i < Channels; i++) diffCh += Math.Abs(r.Spectrum[i] - baseRun.Spectrum[i]);
                double maxDh = 0;
                if (r.Pulses.Count == baseRun.Pulses.Count)
                {
                    for (int i = 0; i < r.Pulses.Count; i++) maxDh = Math.Max(maxDh, Math.Abs(r.Pulses[i].Height - baseRun.Pulses[i].Height));
                }
                else maxDh = double.NaN;
                Console.WriteLine(string.Format(Inv, "  {0} бит {1}: Total {2}, Valid {3}, отсчётов {4}; Σ|Δканал| к 16+ = {5}, max|Δвысоты| = {6}",
                    bits, neg ? "−" : "+", r.Total, r.Valid, r.Samples, diffCh, maxDh.ToString("E2", Inv)));
            }
        }
    }

    static int SectionF(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── F: границы буферов (300 имп/с, смесь, 20 с, 16 бит)");
        List<Ev> evs = Poisson(300, 20, rg => rg.NextDouble() < 0.5 ? 30.0 : 5.0 + 55.0 * rg.NextDouble(), 41);
        double[] x = Synthesize(fs, 20, evs, TrUs, TdUs, NoiseFs, 0, 42);
        Run baseRun = null;
        int ctlDiff = 0;
        foreach (int buf in new[] { 1920, 441, 100, 17, 16, 7 })
        {
            Run r = Detect(fs, 16, false, S, peak, ideal, x, buf);
            if (baseRun == null) baseRun = r;
            int same = 0;
            int n = Math.Min(r.Pulses.Count, baseRun.Pulses.Count);
            for (int i = 0; i < n; i++)
            {
                if (r.Pulses[i].Time == baseRun.Pulses[i].Time && r.Pulses[i].Height == baseRun.Pulses[i].Height) same++;
            }
            bool identical = same == r.Pulses.Count && r.Pulses.Count == baseRun.Pulses.Count && r.Total == baseRun.Total;
            Console.WriteLine(string.Format(Inv, "  буфер {0,5}: импульсов {1}, Total {2}, отсчётов {3} (ожидалось {4}); совпало поимённо {5} — {6}",
                buf, r.Pulses.Count, r.Total, r.Samples, x.Length, same, identical ? "ТОЖДЕСТВЕННО" : "РАЗНИТСЯ"));
            if (buf == 7) ctlDiff = identical ? 1 : 0;
        }
        Console.WriteLine(ctlDiff == 0
            ? "  контроль (буфер 7 < S = 16) — расхождение видно: сравнение зрячее"
            : "  ⛔ контроль (буфер 7 < S) НЕ дал расхождения — сравнение слепо");
        return ctlDiff;
    }

    static void SectionO(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── O: смещение нуля АЦП (линия 30 %, 100 имп/с, 20 с)");
        List<Ev> evs = Poisson(100, 20, rg => 30.0, 51);
        foreach (double off in new[] { 0.0, 3.0, -3.0 })
        {
            double[] x = Synthesize(fs, 20, evs, TrUs, TdUs, NoiseFs, off, 52);
            Run r = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
            Stats(r.Pulses.Select(p => p.Height), out double m, out double sd, out int n);
            Console.WriteLine(string.Format(Inv, "  смещение {0,4} %: импульсов {1}/{2}, высота {3} ± {4}, Total {5}",
                off, n, evs.Count, G(m), G(sd), r.Total));
        }
    }

    static void SectionR(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── R: скорость счёта и живое время (16 бит, буфер 1920; τ модели = S/fs = "
                          + G(S * 1e6 / fs, 1) + " мкс)");
        // окно линии — по редкому потоку
        double lo, hi;
        {
            List<Ev> evs = Poisson(10, 200, rg => 30.0, 61);
            double[] x = Synthesize(fs, 200, evs, TrUs, TdUs, NoiseFs, 0, 62);
            Run r = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
            Stats(r.Pulses.Select(p => p.Height), out double m, out double sd, out int n);
            lo = m - 5 * sd; hi = m + 5 * sd;
            Console.WriteLine(string.Format(Inv, "  окно линии по 10 имп/с: [{0}; {1}] (высота {2} ± {3}, принято {4}/{5})",
                G(lo), G(hi), G(m), G(sd), n, evs.Count));
        }
        Console.WriteLine("  чистая линия 30 %:");
        Console.WriteLine("  r, имп/с | событий | в окне | Total | T, с | T_жив, с | окно/T_жив ÷ r | τ, при котором сошлось бы, мкс");
        foreach (double rate in new[] { 10.0, 100.0, 300.0, 1000.0, 2000.0, 3000.0 })
        {
            double T = rate <= 100 ? 200 : 60;
            List<Ev> evs = Poisson(rate, T, rg => 30.0, 70 + (int)rate);
            double[] x = Synthesize(fs, T, evs, TrUs, TdUs, NoiseFs, 0, 80 + (int)rate);
            Run run = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
            long inWin = run.Pulses.LongCount(p => p.Height >= lo && p.Height <= hi);
            double ratio = inWin / run.LiveTime / (evs.Count / T);
            double m = run.Total / T;
            double tauFit = (1.0 - inWin / (double)evs.Count) / m;
            Console.WriteLine(string.Format(Inv, "  {0,6} | {1} | {2} | {3} | {4} | {5} | {6} | {7}",
                rate, evs.Count, inWin, run.Total, T, G(run.LiveTime, 2), G(ratio, 4), G(tauFit * 1e6, 1)));
        }
        Console.WriteLine("  смесь: линия 30 % (половина) + сплошное 5…60 % — все принятые в спектр:");
        Console.WriteLine("  r, имп/с | событий | Valid | Total | T_жив, с | Valid/T_жив ÷ (r·доля приёма при 10 имп/с)");
        double acc10 = double.NaN;
        foreach (double rate in new[] { 10.0, 100.0, 300.0, 1000.0, 3000.0 })
        {
            double T = rate <= 100 ? 200 : 60;
            List<Ev> evs = Poisson(rate, T, rg => rg.NextDouble() < 0.5 ? 30.0 : 5.0 + 55.0 * rg.NextDouble(), 90 + (int)rate);
            double[] x = Synthesize(fs, T, evs, TrUs, TdUs, NoiseFs, 0, 95 + (int)rate);
            Run run = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
            if (rate == 10.0) acc10 = run.Valid / (double)evs.Count;
            double ratio = run.Valid / run.LiveTime / (evs.Count / T * acc10);
            Console.WriteLine(string.Format(Inv, "  {0,6} | {1} | {2} | {3} | {4} | {5}",
                rate, evs.Count, run.Valid, run.Total, G(run.LiveTime, 2), G(ratio, 4)));
        }
    }

    static void SectionC(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── C: зашкал (16 бит, 20 имп/с, 20 с; верхний порог высоты 100 — умолчание)");
        Console.WriteLine("  A,%ПШ | подано | в спектре | Total | высота ср. ± σ | канал");
        foreach (double a in new[] { 80.0, 95.0, 100.0, 105.0, 120.0, 150.0, 200.0, 400.0 })
        {
            List<Ev> evs = Poisson(20, 20, rg => a, 300 + (int)a);
            double[] x = Synthesize(fs, 20, evs, TrUs, TdUs, NoiseFs, 0, 400 + (int)a);
            Run r = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
            Stats(r.Pulses.Select(p => p.Height), out double m, out double sd, out int n);
            Console.WriteLine(string.Format(Inv, "  {0,5} | {1} | {2} | {3} | {4} ± {5} | {6}",
                a, evs.Count, r.Valid, r.Total, G(m), G(sd), double.IsNaN(m) ? "—" : G(m / ChannelPitch, 1)));
        }
    }

    /// <summary>
    /// Правило: при той же полной скорости событий ЖИВОЕ ВРЕМЯ не зависит от того, какая их
    /// доля легла выше верхнего порога высоты: детектор занят любым сработавшим импульсом.
    /// 0 — правило выполнено (живое время с «большими» и без них — в пределах 2 %);
    /// 1 — дефект (живое время с «большими» больше: их мёртвое время потеряно);
    /// 3 — контроль не сработал (без «больших» линия не сходится с заложенной в 5 %, либо
    /// «большой» импульс попал в спектр — порог не режет, и замер не про то).
    /// Отношение «окно линии / живое время ÷ r» печатается сведением: его остаток с
    /// «большими» — потери наложений (большой сосед сильнее уводит высоту линии из окна),
    /// это физика тракта, а не учёт мёртвого времени.
    /// </summary>
    static int SectionU(int fs, int S, int peak, double[] ideal)
    {
        Console.WriteLine();
        Console.WriteLine("── U: импульсы выше верхнего порога ВЫСОТЫ (порог 50; линия 30 % и «большие» 70 %, 16 бит, 60 с)");
        Console.WriteLine("  r, имп/с | доля больших | событий | Total | в окне линии | выше порога в спектре | T_жив | (окно/T_жив) ÷ r_линии");
        int verdict = 0;
        foreach (double rate in new[] { 300.0, 1000.0 })
        {
            double baseRatio = double.NaN, baseLive = double.NaN;
            foreach (double big in new[] { 0.0, 0.5 })
            {
                double T = 60;
                List<Ev> evs = Poisson(rate, T, rg => rg.NextDouble() < big ? 70.0 : 30.0, 500 + (int)rate + (int)(big * 10));
                double[] x = Synthesize(fs, T, evs, TrUs, TdUs, NoiseFs, 0, 600 + (int)rate);
                Run run = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2, 1.0, 50.0);
                int nLine = evs.Count(e => e.A == 30.0);
                long inWin = run.Pulses.LongCount(p => p.Height >= 25 && p.Height <= 35);
                long above = run.Pulses.LongCount(p => p.Height >= 50.0);
                double ratio = inWin / run.LiveTime / (nLine / T);
                Console.WriteLine(string.Format(Inv, "  {0,6} | {1} | {2} | {3} | {4} | {5} | {6} | {7}",
                    rate, big, evs.Count, run.Total, inWin, above, G(run.LiveTime, 2), G(ratio, 4)));
                if (above > 0)
                {
                    Console.WriteLine("    ⛔ контроль: импульс выше порога 50 попал в спектр — порог не режет");
                    verdict = 3;
                }
                if (big == 0.0)
                {
                    baseRatio = ratio;
                    baseLive = run.LiveTime;
                    if (Math.Abs(ratio - 1.0) > 0.05)
                    {
                        Console.WriteLine("    ⛔ контроль: без «больших» линия не сходится с заложенной в 5 % — замер негоден");
                        verdict = 3;
                    }
                }
                else
                {
                    double dLive = run.LiveTime / baseLive - 1.0;
                    double d = ratio / baseRatio - 1.0;
                    bool bad = dLive > 0.02;
                    Console.WriteLine(string.Format(Inv, "    живое время к той же скорости без «больших»: {0} % — {1}; окно линии: {2} % (наложения, сведение)",
                        (dLive * 100).ToString("+0.00;-0.00", Inv),
                        bad ? "ДЕФЕКТ (мёртвое время «больших» потеряно)" : "сходится",
                        (d * 100).ToString("+0.00;-0.00", Inv)));
                    if (bad && verdict == 0) verdict = 1;
                }
            }
        }
        return verdict;
    }

    static void SectionS()
    {
        Console.WriteLine();
        Console.WriteLine("── S: частота дискретизации (S = 16·fs/48000, индекс пика S/2; эталон 20 %, линия 30 %, 16 бит)");
        Console.WriteLine("  td, мкс | fs | S | эталон: принято/подано | высота линии ср. ± σ | принято линии");
        foreach (double td in new[] { 60.0, 20.0 })
        {
            foreach (int fs in new[] { 44100, 48000, 96000, 192000 })
            {
                int S = (int)Math.Round(16.0 * fs / 48000.0);
                int peak = S / 2;
                double[] ideal = RecordStandard(fs, S, peak, td / 4.0, td, 20.0, 1000, out int nStd, out int nOff);
                List<Ev> evs = Poisson(20, 20, rg => 30.0, 700 + fs / 1000);
                double[] x = Synthesize(fs, 20, evs, td / 4.0, td, NoiseFs, 0, 800 + fs / 1000);
                Run r = Detect(fs, 16, false, S, peak, ideal, x, fs / 50 * 2);
                Stats(r.Pulses.Select(p => p.Height), out double m, out double sd, out int n);
                Console.WriteLine(string.Format(Inv, "  {0,4} | {1,6} | {2,3} | {3}/{4} | {5} ± {6} | {7}/{8}",
                    td, fs, S, nStd, nOff, G(m), G(sd), n, evs.Count));
            }
        }
    }

    /// <summary>0 — отказ на нуле есть; 1 — ноль принят молча (дефект); 3 — контроль
    /// (близнец BufferSize = 0 отказывает, 50 принимается) не выполнен.</summary>
    static int SectionW()
    {
        Console.WriteLine();
        Console.WriteLine("── W: WaveIn.BufferQueueSize = 0 (new WaveIn(-1), устройство не открывается)");
        WaveIn wi = new WaveIn(-1);
        try
        {
            bool ctlSize = Throws(() => wi.BufferSize = 0);
            bool okQueue = !Throws(() => wi.BufferQueueSize = 50) && wi.BufferQueueSize == 50;
            bool zeroQueue = Throws(() => wi.BufferQueueSize = 0);
            int afterZero = wi.BufferQueueSize;
            bool negQueue = Throws(() => wi.BufferQueueSize = -5);
            Console.WriteLine(string.Format(Inv, "  контроль: BufferSize = 0 — {0}; BufferQueueSize = 50 — {1}",
                ctlSize ? "отказ" : "ПРИНЯТ ⛔", okQueue ? "принят" : "НЕ ПРИНЯТ ⛔"));
            Console.WriteLine(string.Format(Inv, "  BufferQueueSize = 0 — {0} (после: {1}); = −5 — {2}",
                zeroQueue ? "отказ" : "ПРИНЯТ МОЛЧА", afterZero, negQueue ? "отказ" : "ПРИНЯТ МОЛЧА"));
            if (!ctlSize || !okQueue) return 3;
            return zeroQueue && negQueue ? 0 : 1;
        }
        finally
        {
            wi.Dispose();
        }
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}
