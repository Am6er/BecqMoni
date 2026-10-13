using BecquerelMonitor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace EnergyToChannelMemoP213
{
    /// <summary>
    /// П213 (02.10.2026, `T267`): ПРИЁМКА МЕМОИЗАЦИИ
    /// <c>PolynomialEnergyCalibration.EnergyToChannel</c> после замены проверки
    /// потолка (<c>ConcurrentDictionary.Count</c> → счётчик рядом со словарём).
    /// Собирается и гоняется против ОБЕИХ сборок (до и после): кеш читается
    /// отражением, тип поля распознаётся сам (словарь или словарь со счётчиком).
    ///
    ///   energytochannelmemop213 [--plant] [--bench] [--threads=N] [--calls=N]
    ///
    /// Четыре части, итог — код возврата (0 — всё сошлось, 1 — расхождение,
    /// 2 — отказ среды/исключение):
    ///   (1) ЭКВИВАЛЕНТНОСТЬ: для трёх калибровок (степень 1, 2, 3 — корень
    ///       <c>FindRoots</c>) и трёх длин шкалы каждый вызов через кеш (промах,
    ///       повтор-попадание, после переполнения) побитово равен ответу свежей
    ///       копии калибровки (у копии кеша нет — первый вызов считает напрямую);
    ///   (2) ПЕРЕПОЛНЕНИЕ: однопоточно 3·32768 + 5 разных энергий; печатается,
    ///       на каком промахе словарь очищен и сколько в нём записей после —
    ///       обязано быть «сброс на промахе 32769, 65537, 98305; после — 1;
    ///       наибольшее 32768»; у новой сборки счётчик = <c>Map.Count</c>;
    ///   (3) МНОГОПОТОЧНО: N потоков (умолчание — число ядер) зовут одну
    ///       калибровку одновременно — энергии из пула 50 000 (больше потолка —
    ///       сбросы под гонкой) и из пула 64 (попадания), плюс поток, который
    ///       дёргает <c>InvalidateCache</c>; каждый ответ побитово сверяется с
    ///       эталоном, исключения считаются; печатается размер словаря в конце;
    ///   (4) `--plant` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: в кеш подсаживается значение
    ///       +1 ulp для одной энергии, и сверка (1) обязана его поймать (код 1).
    /// `--bench` — время промахов (поток энергий без повторов) одним потоком и
    /// N потоками, минимум из пяти кругов.
    /// </summary>
    internal static class Program
    {
        const int Limit = 32768;
        static int failures;

        static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("ОТКАЗ: " + e);
                return 2;
            }
        }

        static string Arg(string[] args, string key, string fallback)
        {
            string p = "--" + key + "=";
            foreach (string a in args) { if (a.StartsWith(p, StringComparison.Ordinal)) return a.Substring(p.Length); }
            return fallback;
        }

        static PolynomialEnergyCalibration Make(params double[] c)
        {
            return new PolynomialEnergyCalibration { PolynomialOrder = c.Length - 1, Coefficients = c };
        }

        static string Bits(double v) { return BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture); }

        static double Reference(PolynomialEnergyCalibration calibration, double energy, int channels)
        {
            // Свежая копия без кеша: первый вызов идёт ветвью «словаря ещё нет»
            // и считает EnrgToChannel напрямую.
            return ((PolynomialEnergyCalibration)calibration.Clone()).EnergyToChannel(energy, channels);
        }

        // Кеш отражением: (словарь, счётчик или -1, если счётчика нет — старая сборка).
        static FieldInfo memoField = typeof(PolynomialEnergyCalibration).GetField("energytochanel", BindingFlags.NonPublic | BindingFlags.Instance);

        static ConcurrentDictionary<double, double> MapOf(PolynomialEnergyCalibration calibration, out int counter)
        {
            object memo = memoField.GetValue(calibration);
            counter = -1;
            if (memo == null) return null;
            var direct = memo as ConcurrentDictionary<double, double>;
            if (direct != null) return direct;
            Type t = memo.GetType();
            counter = (int)t.GetField("count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(memo);
            return (ConcurrentDictionary<double, double>)t.GetField("Map", BindingFlags.Public | BindingFlags.Instance).GetValue(memo);
        }

        static int Run(string[] args)
        {
            bool plant = args.Contains("--plant");
            bool bench = args.Contains("--bench");
            int threads = int.Parse(Arg(args, "threads", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            int calls = int.Parse(Arg(args, "calls", "400000"), CultureInfo.InvariantCulture);
            Console.WriteLine("сборка: " + typeof(PolynomialEnergyCalibration).Assembly.Location + "; поле кеша: " + memoField.FieldType.Name);

            var calibrations = new List<Tuple<string, PolynomialEnergyCalibration>>
            {
                Tuple.Create("степень 1", Make(-3.25, 0.7421)),
                Tuple.Create("степень 2", Make(-12.5, 2.953, 1.17e-5)),
                Tuple.Create("степень 3", Make(4.1, 0.362, 2.1e-6, -1.3e-10)),
            };
            int[] lengths = { 1024, 4096, 8192 };

            // (1) эквивалентность
            long checkedCalls = 0;
            foreach (var pair in calibrations)
            {
                foreach (int n in lengths)
                {
                    var c = (PolynomialEnergyCalibration)pair.Item2.Clone();
                    double emax = c.ChannelToEnergy(n);
                    var rng = new Random(213 + n);
                    var energies = new List<double>();
                    for (int i = 0; i < 3000; i++) energies.Add(emax * (rng.NextDouble() * 1.05 - 0.02));
                    energies.Add(0.0); energies.Add(-1.0); energies.Add(emax); energies.Add(emax * 1.5);
                    var refs = energies.Select(e => Reference(c, e, n)).ToArray();
                    bool planted = false;
                    for (int pass = 0; pass < 3; pass++)
                    {
                        if (plant && pass == 1 && !planted)
                        {
                            int unused;
                            var map = MapOf(c, out unused);
                            double e0 = energies[10];
                            double v0;
                            if (map != null && map.TryGetValue(e0, out v0))
                            {
                                map[e0] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(v0) + 1);
                                Console.WriteLine("КОНТРОЛЬ: в кеш подсажено +1 ulp для " + e0.ToString("R", CultureInfo.InvariantCulture) + " кэВ (" + pair.Item1 + ", N=" + n + ")");
                                planted = true;
                            }
                        }
                        for (int i = 0; i < energies.Count; i++)
                        {
                            double v = c.EnergyToChannel(energies[i], n);
                            checkedCalls++;
                            if (BitConverter.DoubleToInt64Bits(v) != BitConverter.DoubleToInt64Bits(refs[i]))
                            {
                                if (failures++ < 10)
                                    Console.WriteLine("РАСХОЖДЕНИЕ (1) " + pair.Item1 + " N=" + n + " проход " + pass + " E=" + energies[i].ToString("R", CultureInfo.InvariantCulture) + ": " + Bits(v) + " против " + Bits(refs[i]));
                            }
                        }
                    }
                }
            }
            Console.WriteLine("(1) эквивалентность: вызовов " + checkedCalls + ", расхождений " + failures);
            if (plant)
            {
                Console.WriteLine(failures > 0 ? "КОНТРОЛЬ ПОЙМАН: подсадка дала расхождение" : "КОНТРОЛЬ НЕ ПОЙМАН");
                return failures > 0 ? 1 : 0;
            }

            // (2) переполнение
            {
                var c = Make(-12.5, 2.953, 1.17e-5);
                int n = 8192;
                double emax = c.ChannelToEnergy(n);
                int total = 3 * Limit + 5;
                var resets = new List<int>();
                int prev = 0, maxSeen = 0, afterReset = -1, counterMismatch = 0;
                long hitsChecked = 0;
                for (int i = 1; i <= total; i++)
                {
                    double e = emax * (i / (double)(total + 1));
                    c.EnergyToChannel(e, n);
                    int counter;
                    var map = MapOf(c, out counter);
                    int count = map.Count;
                    if (count < prev) { resets.Add(i); if (afterReset < 0) afterReset = count; else if (afterReset != count) afterReset = -2; }
                    if (counter >= 0 && counter != count) counterMismatch++;
                    maxSeen = Math.Max(maxSeen, count);
                    prev = count;
                    // попадание сразу после промаха — то же значение
                    if (i % 997 == 0)
                    {
                        double hit = c.EnergyToChannel(e, n);
                        hitsChecked++;
                        if (BitConverter.DoubleToInt64Bits(hit) != BitConverter.DoubleToInt64Bits(Reference(c, e, n)))
                        {
                            failures++;
                            Console.WriteLine("РАСХОЖДЕНИЕ (2) на попадании E=" + e.ToString("R", CultureInfo.InvariantCulture));
                        }
                    }
                }
                int finalCounter;
                int finalCount = MapOf(c, out finalCounter).Count;
                Console.WriteLine("(2) переполнение: разных энергий " + total + "; сброс на промахах " + string.Join(", ", resets)
                    + "; записей после сброса " + afterReset + "; наибольшее " + maxSeen + "; в конце " + finalCount
                    + (finalCounter >= 0 ? " (счётчик " + finalCounter + ", расхождений счётчика со словарём " + counterMismatch + ")" : " (счётчика нет — старая сборка)")
                    + "; попаданий сверено " + hitsChecked);
                bool ok = resets.SequenceEqual(new[] { Limit + 1, 2 * Limit + 1, 3 * Limit + 1 }) && afterReset == 1 && maxSeen == Limit && finalCount == 5 && counterMismatch == 0;
                if (!ok) { failures++; Console.WriteLine("РАСХОЖДЕНИЕ (2): ожидалось сброс на 32769, 65537, 98305; после — 1; наибольшее 32768; в конце 5"); }
            }

            // (3) многопоточно
            {
                var c = Make(-12.5, 2.953, 1.17e-5);
                int n = 8192;
                double emax = c.ChannelToEnergy(n);
                var rng = new Random(2131);
                double[] wide = Enumerable.Range(0, 50000).Select(i => emax * rng.NextDouble()).ToArray();
                double[] narrow = Enumerable.Range(0, 64).Select(i => emax * rng.NextDouble()).ToArray();
                var refWide = wide.Select(e => Reference(c, e, n)).ToArray();
                var refNarrow = narrow.Select(e => Reference(c, e, n)).ToArray();
                long bad = 0, exceptions = 0, done = 0, invalidations = 0;
                int maxMap = 0;
                var start = new ManualResetEventSlim(false);
                bool stop = false;
                var workers = new List<Thread>();
                for (int t = 0; t < threads; t++)
                {
                    int seed = 7000 + t;
                    var th = new Thread(() =>
                    {
                        var r = new Random(seed);
                        start.Wait();
                        for (int k = 0; k < calls; k++)
                        {
                            try
                            {
                                bool useWide = (k & 3) != 0;
                                int idx = useWide ? r.Next(wide.Length) : r.Next(narrow.Length);
                                double e = useWide ? wide[idx] : narrow[idx];
                                double expect = useWide ? refWide[idx] : refNarrow[idx];
                                double v = c.EnergyToChannel(e, n);
                                if (BitConverter.DoubleToInt64Bits(v) != BitConverter.DoubleToInt64Bits(expect)) Interlocked.Increment(ref bad);
                            }
                            catch (Exception) { Interlocked.Increment(ref exceptions); }
                        }
                        Interlocked.Add(ref done, calls);
                    });
                    workers.Add(th);
                    th.Start();
                }
                var inval = new Thread(() =>
                {
                    start.Wait();
                    while (!Volatile.Read(ref stop))
                    {
                        Thread.Sleep(5);
                        c.InvalidateCache();
                        Interlocked.Increment(ref invalidations);
                        int unused;
                        var map = MapOf(c, out unused);
                        if (map != null) { int m = map.Count; if (m > maxMap) maxMap = m; }
                    }
                });
                inval.Start();
                var sw = Stopwatch.StartNew();
                start.Set();
                foreach (var th in workers) th.Join();
                Volatile.Write(ref stop, true);
                inval.Join();
                int fc;
                var fm = MapOf(c, out fc);
                Console.WriteLine("(3) многопоточно: потоков " + threads + ", вызовов " + done + " за " + sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)
                    + " с; расхождений " + bad + ", исключений " + exceptions + ", сбросов шкалы " + invalidations
                    + "; словарь: наибольший замеченный " + maxMap + ", в конце " + fm.Count + (fc >= 0 ? " (счётчик " + fc + ")" : ""));
                if (bad != 0 || exceptions != 0) failures++;
                if (fm.Count > Limit + threads) { failures++; Console.WriteLine("РАСХОЖДЕНИЕ (3): словарь перерос потолок больше чем на число потоков"); }
            }

            if (bench)
            {
                var c0 = Make(-12.5, 2.953, 1.17e-5);
                int n = 8192;
                double emax = c0.ChannelToEnergy(n);
                int m = 2000000;
                for (int mode = 0; mode < 2; mode++)
                {
                    int th = mode == 0 ? 1 : threads;
                    double best = double.MaxValue;
                    for (int round = 0; round < 5; round++)
                    {
                        var c = (PolynomialEnergyCalibration)c0.Clone();
                        c.EnergyToChannel(1.0, n);
                        var sw = Stopwatch.StartNew();
                        var ts = Enumerable.Range(0, th).Select(t => new Thread(() =>
                        {
                            int per = m / th;
                            double step = emax / (m + 1.0);
                            for (int k = 0; k < per; k++) c.EnergyToChannel(step * (t * per + k + 0.5), n);
                        })).ToList();
                        ts.ForEach(x => x.Start());
                        ts.ForEach(x => x.Join());
                        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                    }
                    Console.WriteLine("(время) промахов " + m + ", потоков " + th + ": минимум из 5 — " + best.ToString("F1", CultureInfo.InvariantCulture)
                        + " мс (" + (best * 1e6 / m).ToString("F1", CultureInfo.InvariantCulture) + " нс на вызов)");
                }
            }

            Console.WriteLine("ИТОГ: " + (failures == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЕ (" + failures + ")"));
            return failures == 0 ? 0 : 1;
        }
    }
}
