using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace TransferCacheP211
{
    /// <summary>
    /// П211 (02.10.2026, `T265` вариант Б): ПРИЁМКА ГОТОВЫХ СТРОК ПЕРЕНОСА
    /// (<c>ResponseMatrix.TransferCache</c>) на уровне матрицы — побитово, по
    /// всему складу, с положительным контролем; и цена переноса «до/после».
    ///
    ///   transfercachep211 --store=каталог_с_rmx [--calls=N] [--energies=K] [--rounds=R]
    ///                     [--ulp] [--only=имя]
    ///
    /// На каждую матрицу склада (<c>TransferByChannel</c> = true, как ставит
    /// разбор) — нагрузка по образу сумм-континуума каскада: K энергий «третьего»
    /// (случайные, плюс РОВНО узлы сетки, ниже первого и выше последнего узла),
    /// N вызовов (энергия из K, канал 0…n−1 и −1, множитель света 1 / ≈1 / 0 / −1,
    /// сдвиг от −300 до +2500 кэВ, вес — обычный, 1e-320 (потеря в ноль при
    /// умножении), 0 и отрицательный), каждый — тремя входами (Channel / Shifted /
    /// Light) прежнего пути <c>ResponseMatrix</c> (плечо A) и кэша (плечо Б) в
    /// раздельные приёмники. Приёмники сверяются побитово.
    ///
    /// `--ulp` — положительный контроль сверки: в один ненулевой бин плеча Б
    /// подсаживается +1 ulp; сверка обязана найти ровно его.
    ///
    /// Время — минимум из R кругов на плечо (плечи по очереди), только нагрузка
    /// (без построения приёмников); у кэша — круг С ПУСТЫМ кэшем, то есть с
    /// ценой подготовки строк.
    ///
    ///   transfercachep211 --spectrum=X.xml --sample=..[,..] [--chain=..]
    ///
    /// Второй режим — РАЗБОР целиком (как <c>DepositCarryP207</c>, матрица —
    /// <c>FsaMatrixBinding.Bind</c>) и счётчики готовых строк сумм-континуума
    /// после него (<c>FsaAnalyzer.SumTransferStats</c>: попадания, построенные
    /// строки, объём, сбросы) — сколько строк переиспользуется на настоящем
    /// разборе и сколько памяти это стоит.
    ///
    /// Коды: 0 — побитово / разбор прошёл; 1 — расхождение; 2 — ключи/файлы.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();
            var inv = CultureInfo.InvariantCulture;
            string store = null, only = null, spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            int calls = 20000, energies = 300, rounds = 3;
            bool ulp = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a.StartsWith("--calls=", StringComparison.Ordinal)) calls = int.Parse(a.Substring(8), inv);
                else if (a.StartsWith("--energies=", StringComparison.Ordinal)) energies = int.Parse(a.Substring(11), inv);
                else if (a.StartsWith("--rounds=", StringComparison.Ordinal)) rounds = int.Parse(a.Substring(9), inv);
                else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
                else if (a == "--ulp") ulp = true;
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                    chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath != null)
            {
                return Analysis(spectrumPath, chains, nuclides);
            }

            if (store == null || !Directory.Exists(store))
            {
                Console.Error.WriteLine("нужен --store=каталог с *.rmx");
                return 2;
            }

            var files = new List<string>(Directory.GetFiles(store, "*.rmx"));
            files.Sort(StringComparer.Ordinal);
            long totalBins = 0, totalDiffer = 0, totalCalls = 0;
            double sumA = 0.0, sumB = 0.0;
            int matrices = 0;
            bool planted = false;
            foreach (string path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (only != null && name != only)
                {
                    continue;
                }

                ResponseMatrix m = ResponseMatrix.Load(path);
                if (m == null || !m.HasChannels || m.Energies == null || m.Energies.Length == 0)
                {
                    Console.WriteLine("{0}: пропуск (нет матрицы или каналов)", name);
                    continue;
                }

                m.TransferByChannel = true;
                List<Call> work = Work(m, calls, energies, name.GetHashCode());
                int len = ResponseMatrix.ImageBins(6000.0, m.BinKev) + 1;
                int nc = m.ChannelRows.Length;

                double bestA = double.MaxValue, bestB = double.MaxValue;
                double[][] ta = null, tb = null;
                ResponseMatrix.TransferCache cache = null;
                for (int r = 0; r < rounds; r++)
                {
                    ta = Targets(nc, len);
                    var sw = Stopwatch.StartNew();
                    foreach (Call c in work)
                    {
                        double[] t = ta[c.Channel + 1];
                        switch (c.Entry)
                        {
                            case 0: m.AccumulateChannel(t, c.Energy, c.Weight, c.Channel); break;
                            case 1: m.AccumulateShifted(t, c.Energy, c.Weight, c.Channel, c.ShiftKev); break;
                            default: m.AccumulateLight(t, c.Energy, c.Weight, c.Channel, c.ShiftKev, c.Scale); break;
                        }
                    }

                    bestA = Math.Min(bestA, sw.Elapsed.TotalMilliseconds);

                    tb = Targets(nc, len);
                    cache = new ResponseMatrix.TransferCache(m);
                    sw.Restart();
                    foreach (Call c in work)
                    {
                        double[] t = tb[c.Channel + 1];
                        switch (c.Entry)
                        {
                            case 0: cache.AccumulateChannel(t, c.Energy, c.Weight, c.Channel); break;
                            case 1: cache.AccumulateShifted(t, c.Energy, c.Weight, c.Channel, c.ShiftKev); break;
                            default: cache.AccumulateLight(t, c.Energy, c.Weight, c.Channel, c.ShiftKev, c.Scale); break;
                        }
                    }

                    bestB = Math.Min(bestB, sw.Elapsed.TotalMilliseconds);
                }

                if (ulp && !planted)
                {
                    for (int c = 0; c < tb.Length && !planted; c++)
                    {
                        for (int i = 0; i < tb[c].Length; i++)
                        {
                            if (tb[c][i] > 0.0)
                            {
                                tb[c][i] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(tb[c][i]) + 1);
                                Console.WriteLine("КОНТРОЛЬ: +1 ulp в {0}, приёмник {1}, бин {2}", name, c - 1, i);
                                planted = true;
                                break;
                            }
                        }
                    }
                }

                long bins = 0, differ = 0;
                for (int c = 0; c < ta.Length; c++)
                {
                    for (int i = 0; i < len; i++)
                    {
                        bins++;
                        if (BitConverter.DoubleToInt64Bits(ta[c][i]) != BitConverter.DoubleToInt64Bits(tb[c][i]))
                        {
                            differ++;
                            if (differ <= 5)
                            {
                                Console.WriteLine("  РАСХОЖДЕНИЕ {0} приёмник {1} бин {2}: {3:R} против {4:R}",
                                                  name, c - 1, i, ta[c][i], tb[c][i]);
                            }
                        }
                    }
                }

                matrices++;
                totalBins += bins;
                totalDiffer += differ;
                totalCalls += work.Count;
                sumA += bestA;
                sumB += bestB;
                Console.WriteLine("{0,-28} вызовов {1}, бинов {2}, расходится {3}; A {4:F1} мс, Б {5:F1} мс (×{6:F2});"
                                  + " строк {7}, попаданий {8}, {9:F1} МБ",
                                  name, work.Count, bins, differ, bestA, bestB, bestA / bestB,
                                  cache.Misses, cache.Hits, cache.Bytes / 1048576.0);
            }

            Console.WriteLine("ИТОГ: матриц {0}, вызовов {1}, бинов {2}, расходится {3}; перенос A {4:F0} мс, Б {5:F0} мс (×{6:F2})",
                              matrices, totalCalls, totalBins, totalDiffer, sumA, sumB, sumB > 0.0 ? sumA / sumB : 0.0);
            if (matrices == 0)
            {
                return 2;
            }

            return totalDiffer == 0 ? 0 : 1;
        }

        /// <summary>Разбор целиком и счётчики готовых строк после него.</summary>
        static int Analysis(string spectrumPath, List<string> chains, List<string> nuclides)
        {
            if (chains.Count == 0 && nuclides.Count == 0)
            {
                Console.Error.WriteLine("нужен состав --sample=/--chain=");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            ResultData rd = Load(spectrumPath);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            string guid = rd.Efficiency != null ? rd.Efficiency.Guid : null;
            ResponseMatrix matrix = ResponseMatrixStore.Load(guid);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                Console.Error.WriteLine("матрицы сцены нет или отпечаток не сошёлся");
                return 2;
            }

            var analyzer = new FsaAnalyzer();
            FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
            analyzer.ScintillatorMaterial = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            FsaTuningReport.Print(analyzer, "готовые строки переноса");
            var watch = Stopwatch.StartNew();
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            double seconds = watch.Elapsed.TotalSeconds;
            if (result == null)
            {
                Console.Error.WriteLine("разбор не состоялся: " + analyzer.Refusal);
                return 2;
            }

            PropertyInfo stats = typeof(FsaAnalyzer).GetProperty(
                "SumTransferStats", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Console.WriteLine("разбор {0:F2} с; χ²/ndf {1:R}; готовые строки: {2}",
                              seconds, result.Chi2Ndf, stats != null ? stats.GetValue(analyzer) : "свойства нет (старая сборка)");
            return 0;
        }

        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0
                    ? label.ToUpperInvariant()
                    : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
            }

            return nucids;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            Console.WriteLine("прибор: {0}", ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }

        sealed class Call
        {
            public double Energy, Weight, ShiftKev, Scale;
            public int Channel, Entry;
        }

        static double[][] Targets(int channels, int len)
        {
            var t = new double[channels + 1][];
            for (int c = 0; c < t.Length; c++)
            {
                t[c] = new double[len];
            }

            return t;
        }

        static List<Call> Work(ResponseMatrix m, int calls, int energies, int seed)
        {
            var rnd = new Random(seed);
            double[] grid = m.Energies;
            var e = new List<double>();
            e.Add(grid[0] * 0.5);                          // ниже первого узла
            e.Add(grid[grid.Length - 1] * 1.02);           // выше последнего
            for (int i = 0; i < grid.Length; i += 7)
            {
                e.Add(grid[i]);                            // ровно узел (тождество)
            }

            while (e.Count < energies)
            {
                e.Add(30.0 + rnd.NextDouble() * Math.Min(2970.0, grid[grid.Length - 1] - 30.0));
            }

            int nc = m.ChannelRows.Length;
            var work = new List<Call>(calls);
            for (int i = 0; i < calls; i++)
            {
                var c = new Call
                {
                    Energy = e[rnd.Next(e.Count)],
                    Channel = rnd.Next(nc + 1) - 1,
                    Entry = rnd.Next(3),
                    ShiftKev = -300.0 + rnd.NextDouble() * 2800.0
                };

                int s = rnd.Next(20);
                // множитель света — чаще единица (β = 0), реже ≈1, ноль и минус (откат на 1)
                c.Scale = s < 14 ? 1.0 : s == 14 ? 0.0 : s == 15 ? -1.0 : 0.97 + rnd.Next(7) * 0.01;
                int w = rnd.Next(50);
                c.Weight = w == 0 ? 0.0 : w == 1 ? -1e-4 : w == 2 ? 1e-320 : 1e-6 + rnd.NextDouble() * 1e-3;
                work.Add(c);
            }

            return work;
        }
    }
}
