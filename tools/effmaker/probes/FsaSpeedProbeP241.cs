using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER213`, П241) ВРЕМЯ И ПОБИТОВЫЙ ОТПЕЧАТОК РАЗБОРА FSA — тем же путём,
    /// что окно (`FsaAnalysisSession.EnsureUpToDate` → фон → `Completed`).
    ///
    /// Режим по умолчанию — как у Amber 07.10.2026: состав «From NucBase»
    /// (`DbLookups`), равновесие ВЫКЛ, пять компонентов модели ВКЛ (суммирование,
    /// наложения, обратное рассеяние, рентген, вылет/511).
    ///
    /// На каждый спектр — `--runs=N` разборов подряд в одном процессе (первый —
    /// холодный: JIT и статические кэши баз). Печатается время каждого, медиана
    /// тёплых (со второго), процессорное время (видно, сколько ядер занято), и
    /// sha256 ПОЛНОГО снимка результата: все поля <see cref="FsaResult"/> и
    /// вложенных объектов отражением, числа — восемью байтами (`R` и биты), то
    /// есть совпадение отпечатков = совпадение каждого числа до последнего бита.
    /// Отпечаток обязан совпасть у всех N прогонов (иначе код 4) и — между
    /// сборками «до» и «после» (сравнивает вызывающий).
    ///
    /// Ключи:
    ///   --spectrum=&lt;файл&gt;     можно несколько раз
    ///   --runs=N               разборов на спектр (умолчание 3)
    ///   --dump=&lt;каталог&gt;      снимок результата первого прогона: &lt;имя спектра&gt;.dump.txt
    ///   --flip=&lt;путь поля&gt;     ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: в снимке у первого числа, путь
    ///                          которого содержит подстроку, перевернуть младший бит
    ///   --timeout=600          секунд ожидания одного разбора
    ///   --threads=N            степень параллелизма разбора (`FsaParallel.Degree`; 1 — подряд)
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился; 4 — прогоны разошлись.
    /// </summary>
    static class FsaSpeedProbeP241
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            int runs = 3, timeoutS = 600;
            string dumpDir = null, flip = null;
            int threads = 0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--runs=", StringComparison.Ordinal)) runs = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpDir = a.Substring(7);
                else if (a.StartsWith("--flip=", StringComparison.Ordinal)) flip = a.Substring(7);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectra.Count == 0 || runs < 1)
            {
                Console.Error.WriteLine("нужен хотя бы один --spectrum=<файл> и --runs ≥ 1");
                return 2;
            }
            if (dumpDir != null) Directory.CreateDirectory(dumpDir);

            // Степень параллелизма разбора (`FsaParallel.Degree`, П241) — отражением:
            // у сборки «до» класса нет, и проба обязана собираться против обеих.
            Type parallel = typeof(FsaAnalyzer).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaParallel");
            FieldInfo degree = parallel != null ? parallel.GetField("Degree", BindingFlags.Public | BindingFlags.Static) : null;
            if (threads > 0)
            {
                if (degree == null)
                {
                    Console.Error.WriteLine("--threads= задан, а у сборки нет FsaParallel.Degree (сборка «до»)");
                    return 2;
                }
                degree.SetValue(null, threads);
            }
            Console.WriteLine("SETUP	степень параллелизма разбора: {0}",
                              degree != null ? degree.GetValue(null).ToString() : "нет (последовательный разбор)");

            FsaAnalyzer last = null;
            FsaAnalysisSession.ProbeAnalyzerHook = a => last = a;
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Console.WriteLine("SETUP\tядер {0}; сет {1}; матрицы {2}", Environment.ProcessorCount,
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет)",
                              BecquerelMonitor.EfficiencyMaker.ResponseMatrixStore.Directory);

            int code = 0;
            foreach (string path in spectra)
            {
                ResultData rd = Load(path, nuclides);
                if (rd == null) { code = 2; continue; }
                FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
                options.DbLookups = true;
                options.FromSet = false;
                options.ChainEquilibrium = false;
                options.CascadeSumming = true;
                options.PileUp = true;
                options.Backscatter = true;
                options.AtomicXray = true;
                options.EscapeAndAnnihilation = true;

                string name = Path.GetFileNameWithoutExtension(path);
                var wall = new List<double>();
                var cpu = new List<double>();
                string firstSha = null;
                for (int r = 0; r < runs; r++)
                {
                    var session = new FsaAnalysisSession();
                    var done = new ManualResetEvent(false);
                    session.Completed += (s, e) => done.Set();
                    Process me = Process.GetCurrentProcess();
                    TimeSpan cpu0 = me.TotalProcessorTime;
                    var sw = Stopwatch.StartNew();
                    session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
                    bool finished = done.WaitOne(TimeSpan.FromSeconds(timeoutS));
                    sw.Stop();
                    me.Refresh();
                    double cpuMs = (me.TotalProcessorTime - cpu0).TotalMilliseconds;
                    if (!finished)
                    {
                        Console.WriteLine("RUN\t{0}\t{1}\tНЕ ЗАВЕРШЁН за {2} с", name, r + 1, timeoutS);
                        code = 3;
                        break;
                    }
                    FsaResult result = session.Result;
                    Console.WriteLine("BANK	{0}", KernelBankSize(last));
                    var sb = new StringBuilder();
                    sb.Append("status=").Append(session.Status ?? "null").Append('\n');
                    sb.Append(FsaResultDump.Of(result));
                    string text = sb.ToString();
                    if (flip != null)
                    {
                        text = Flip(text, flip);
                    }
                    string sha = FsaResultDump.Sha(text);
                    wall.Add(sw.Elapsed.TotalMilliseconds);
                    cpu.Add(cpuMs);
                    Console.WriteLine("RUN\t{0}\t{1}\t{2} мс\tcpu {3} мс\tsha {4}\tкомпонентов {5}\tχ²/ndf {6}\tматрица {7}",
                                      name, r + 1, sw.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                                      cpuMs.ToString("F0", CultureInfo.InvariantCulture), sha.Substring(0, 16),
                                      result != null && result.Components != null ? result.Components.Count : -1,
                                      result != null ? result.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture) : "—",
                                      result == null ? "—" : (result.ResponseMatrixUsed ? "ПРИМЕНЕНА" : "нет"));
                    if (r == 0)
                    {
                        firstSha = sha;
                        if (dumpDir != null)
                        {
                            File.WriteAllText(Path.Combine(dumpDir, name + ".dump.txt"), text, new UTF8Encoding(false));
                        }
                    }
                    else if (sha != firstSha)
                    {
                        Console.WriteLine("DIVERGED\t{0}\tпрогон {1} разошёлся с первым", name, r + 1);
                        code = 4;
                    }
                }
                if (wall.Count > 0)
                {
                    List<double> warm = wall.Count > 1 ? wall.Skip(1).ToList() : wall;
                    List<double> warmCpu = cpu.Count > 1 ? cpu.Skip(1).ToList() : cpu;
                    Console.WriteLine("SUMMARY\t{0}\tхолодный {1} мс\tтёплый медиана {2} мс (из {3})\tcpu тёплый медиана {4} мс\tsha {5}",
                                      name, wall[0].ToString("F0", CultureInfo.InvariantCulture),
                                      Median(warm).ToString("F0", CultureInfo.InvariantCulture), warm.Count,
                                      Median(warmCpu).ToString("F0", CultureInfo.InvariantCulture), firstSha);
                }
            }
            return code;
        }

        /// <summary>Сколько ядер и чисел держит банк ядер уширения анализатора (отражением; для оценки памяти).</summary>
        static string KernelBankSize(FsaAnalyzer analyzer)
        {
            if (analyzer == null) return "анализатора нет";
            FieldInfo f = typeof(FsaAnalyzer).GetField("kernelBank", BindingFlags.Instance | BindingFlags.NonPublic);
            object bank = f != null ? f.GetValue(analyzer) : null;
            if (bank == null) return "банка нет";
            FieldInfo vf = bank.GetType().GetField("values", BindingFlags.Instance | BindingFlags.NonPublic);
            var dict = vf != null ? vf.GetValue(bank) as System.Collections.IDictionary : null;
            if (dict == null) return "словаря нет";
            long doubles = 0;
            foreach (System.Collections.DictionaryEntry e in dict)
            {
                object v = e.Value;
                PropertyInfo pv = v != null ? v.GetType().GetProperty("Value") : null;
                double[] k = pv != null ? pv.GetValue(v, null) as double[] : v as double[];
                if (k != null) doubles += k.Length;
            }
            return string.Format(CultureInfo.InvariantCulture, "ядер {0}, чисел {1} ({2:F1} МБ)", dict.Count, doubles, doubles * 8.0 / 1048576.0);
        }

        static double Median(List<double> v)
        {
            var s = v.OrderBy(x => x).ToList();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
        }

        /// <summary>
        /// Положительный контроль: первое число (строка вида «путь = R | d:биты»),
        /// путь которого содержит <paramref name="needle"/>, получает младший бит
        /// мантиссы перевёрнутым — ровно разница «в последнем бите».
        /// </summary>
        static string Flip(string text, string needle)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int bar = lines[i].IndexOf(" | d:", StringComparison.Ordinal);
                if (bar < 0 || lines[i].IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                long bits = long.Parse(lines[i].Substring(bar + 5), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                double flipped = BitConverter.Int64BitsToDouble(bits ^ 1L);
                int eq = lines[i].IndexOf(" = ", StringComparison.Ordinal);
                lines[i] = lines[i].Substring(0, eq) + " = " + FsaResultDump.D(flipped);
                Console.WriteLine("FLIP\t{0}", lines[i]);
                return string.Join("\n", lines);
            }
            Console.WriteLine("FLIP\tполе с «{0}» не найдено", needle);
            return text;
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }
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
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
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
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}; кривая {3}", Path.GetFileName(path), rd.DetectedPeaks.Count,
                              s.NumberOfChannels, rd.Efficiency != null ? "«" + rd.Efficiency.Name + "»" : "нет");
            return rd;
        }
    }
}
