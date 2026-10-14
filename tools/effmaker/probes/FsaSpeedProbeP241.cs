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
    ///   (П304) --spectrum=&lt;файл&gt;|&lt;правка&gt;[;&lt;правка&gt;…] — правка калибровки ширины спектра
    ///                          перед разбором (контроль ключа банка ядер): `coefs*K` — все
    ///                          коэффициенты ×K, `coef&lt;i&gt;*K`, `type=N`, `tailL=x`, `tailR=x`,
    ///                          `vsig=x`, `vgam=x`; снимок — &lt;имя&gt;@&lt;правка&gt;.dump.txt
    ///   (П304) --mem           после каждого прогона: сборка мусора и строка MEM (куча, рабочий набор)
    ///   (П304) --bank-limit-mb=N предел банков ядер на процесс (отражением; у сборки «до» его нет)
    ///   (П304) --concurrent=N  до последовательной части — N кругов разборов всех спектров РАЗОМ
    ///                          (сеансы разных документов); отпечатки сверяются с последовательными
    /// Строка BANKS (П304) — реестр банков ядер на процесс, если он у сборки есть.
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
            bool mem = false;
            int concurrent = 0;
            double bankLimitMb = -1.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a == "--mem") mem = true;
                else if (a.StartsWith("--concurrent=", StringComparison.Ordinal)) concurrent = int.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bank-limit-mb=", StringComparison.Ordinal)) bankLimitMb = double.Parse(a.Substring(16), CultureInfo.InvariantCulture);
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

            // (П304) Реестр банков ядер на процесс — отражением: у сборки «до» его нет.
            Type bankType = typeof(FsaAnalyzer).GetNestedType("ShapeKernelBank", BindingFlags.NonPublic);
            FieldInfo limitField = bankType != null ? bankType.GetField("RetainLimitBytes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) : null;
            MethodInfo describe = bankType != null ? bankType.GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) : null;
            if (bankLimitMb >= 0.0)
            {
                if (limitField == null)
                {
                    Console.Error.WriteLine("--bank-limit-mb= задан, а у сборки нет банка на процесс (сборка «до»)");
                    return 2;
                }
                limitField.SetValue(null, (long)(bankLimitMb * 1048576.0));
            }
            Console.WriteLine("SETUP\tбанк ядер: {0}", limitField != null
                ? "на процесс, предел " + ((long)limitField.GetValue(null) / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " МБ"
                : "на разбор (сборка «до»)");

            FsaAnalyzer last = null;
            FsaAnalysisSession.ProbeAnalyzerHook = a => last = a;
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Console.WriteLine("SETUP\tядер {0}; сет {1}; матрицы {2}", Environment.ProcessorCount,
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет)",
                              BecquerelMonitor.EfficiencyMaker.ResponseMatrixStore.Directory);

            int code = 0;
            // (П304) --concurrent=N: ДО последовательной части — N кругов, в каждом
            // разборы ВСЕХ спектров идут разом (как сеансы разных документов окна);
            // первый круг — на пустом реестре банков ядер. Отпечаток каждого
            // сверяется в конце с первым прогоном последовательной части.
            var concurrentSha = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int round = 0; round < concurrent; round++)
            {
                var items = new List<Tuple<string, ResultData, FsaCalculationOptions>>();
                foreach (string spec in spectra)
                {
                    string cname;
                    FsaCalculationOptions copts;
                    ResultData crd = Prepare(spec, nuclides, out cname, out copts);
                    if (crd == null) { code = 2; continue; }
                    items.Add(Tuple.Create(cname, crd, copts));
                }
                var sessions = new List<FsaAnalysisSession>();
                var events = new List<ManualResetEvent>();
                var swc = Stopwatch.StartNew();
                foreach (var it in items)
                {
                    var session = new FsaAnalysisSession();
                    var done = new ManualResetEvent(false);
                    session.Completed += (s, e) => done.Set();
                    sessions.Add(session);
                    events.Add(done);
                }
                for (int i = 0; i < items.Count; i++)
                {
                    sessions[i].EnsureUpToDate(items[i].Item2, items[i].Item2.BackgroundEnergySpectrum != null, items[i].Item3);
                }
                for (int i = 0; i < items.Count; i++)
                {
                    if (!events[i].WaitOne(TimeSpan.FromSeconds(timeoutS)))
                    {
                        Console.WriteLine("CONCURRENT\t{0}\tкруг {1}\tНЕ ЗАВЕРШЁН", items[i].Item1, round + 1);
                        code = 3;
                        continue;
                    }
                    var sbc = new StringBuilder();
                    sbc.Append("status=").Append(sessions[i].Status ?? "null").Append('\n');
                    sbc.Append(FsaResultDump.Of(sessions[i].Result));
                    string shaC = FsaResultDump.Sha(sbc.ToString());
                    List<string> list;
                    if (!concurrentSha.TryGetValue(items[i].Item1, out list)) concurrentSha[items[i].Item1] = list = new List<string>();
                    list.Add(shaC);
                }
                swc.Stop();
                Console.WriteLine("CONCURRENT\tкруг {0}: {1} разборов разом за {2} мс", round + 1, items.Count,
                                  swc.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture));
                if (describe != null)
                {
                    Console.WriteLine("BANKS\t{0}", describe.Invoke(null, null));
                }
            }
            var sequentialSha = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string spec in spectra)
            {
                string name;
                FsaCalculationOptions options;
                ResultData rd = Prepare(spec, nuclides, out name, out options);
                if (rd == null) { code = 2; continue; }
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
                    if (describe != null)
                    {
                        Console.WriteLine("BANKS\t{0}", describe.Invoke(null, null));
                    }
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

                    // (П304) память процесса: без --mem — как есть, с --mem — после сборки
                    // мусора; сеанс, результат и анализатор прогона отпущены: держать
                    // банк ядер между разборами — дело реестра, а не пробы
                    last = null;
                    session = null;
                    result = null;
                    if (mem)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    me.Refresh();
                    Console.WriteLine("MEM\t{0}\t{1}\tкуча {2} МБ\tрабочий набор {3} МБ\tчастная {4} МБ{5}",
                                      name, r + 1,
                                      (GC.GetTotalMemory(false) / 1048576.0).ToString("F1", CultureInfo.InvariantCulture),
                                      (me.WorkingSet64 / 1048576.0).ToString("F1", CultureInfo.InvariantCulture),
                                      (me.PrivateMemorySize64 / 1048576.0).ToString("F1", CultureInfo.InvariantCulture),
                                      mem ? "\tпосле сборки мусора" : "");
                }
                if (wall.Count > 0)
                {
                    List<double> warm = wall.Count > 1 ? wall.Skip(1).ToList() : wall;
                    List<double> warmCpu = cpu.Count > 1 ? cpu.Skip(1).ToList() : cpu;
                    Console.WriteLine("SUMMARY\t{0}\tхолодный {1} мс\tтёплый медиана {2} мс (из {3})\tcpu тёплый медиана {4} мс\tsha {5}",
                                      name, wall[0].ToString("F0", CultureInfo.InvariantCulture),
                                      Median(warm).ToString("F0", CultureInfo.InvariantCulture), warm.Count,
                                      Median(warmCpu).ToString("F0", CultureInfo.InvariantCulture), firstSha);
                    sequentialSha[name] = firstSha;
                }
            }

            // (П304) сверка разборов «разом» с последовательными
            foreach (KeyValuePair<string, List<string>> kv in concurrentSha)
            {
                string seq;
                if (!sequentialSha.TryGetValue(kv.Key, out seq))
                {
                    Console.WriteLine("CONCURRENT\t{0}\tпоследовательного прогона нет", kv.Key);
                    code = 4;
                    continue;
                }
                int same = kv.Value.Count(s => s == seq);
                Console.WriteLine("CONCURRENT\t{0}\tсовпало с последовательным {1} из {2}", kv.Key, same, kv.Value.Count);
                if (same != kv.Value.Count)
                {
                    Console.WriteLine("DIVERGED\t{0}\tразбор «разом» разошёлся с последовательным", kv.Key);
                    code = 4;
                }
            }
            return code;
        }

        /// <summary>
        /// (П304) Спектр к разбору: «файл[|правка;…]» → данные (с правкой калибровки
        /// ширины), имя снимка и режим Amber 07.10.2026.
        /// </summary>
        static ResultData Prepare(string spec, NuclideDefinitionManager nuclides, out string name,
                                  out FsaCalculationOptions options)
        {
            int barAt = spec.IndexOf('|');
            string path = barAt >= 0 ? spec.Substring(0, barAt) : spec;
            string mutation = barAt >= 0 ? spec.Substring(barAt + 1) : null;
            name = Path.GetFileNameWithoutExtension(path);
            options = null;
            if (mutation != null)
            {
                var safe = new StringBuilder();
                foreach (char c in mutation) safe.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
                name += "@" + safe;
            }
            ResultData rd = Load(path, nuclides);
            if (rd == null) return null;
            if (mutation != null && !Mutate(rd, mutation)) return null;
            options = FsaCalculationOptions.Of(rd);
            options.DbLookups = true;
            options.FromSet = false;
            options.ChainEquilibrium = false;
            options.CascadeSumming = true;
            options.PileUp = true;
            options.Backscatter = true;
            options.AtomicXray = true;
            options.EscapeAndAnnihilation = true;
            return rd;
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
                // (П304) ячейка банка на процесс несёт ленивое ядро полем Kernel
                FieldInfo kf = v != null ? v.GetType().GetField("Kernel", BindingFlags.Instance | BindingFlags.Public) : null;
                if (kf != null) v = kf.GetValue(v);
                PropertyInfo pv = v != null ? v.GetType().GetProperty("Value") : null;
                double[] k = pv != null ? pv.GetValue(v, null) as double[] : v as double[];
                if (k != null) doubles += k.Length;
            }
            return string.Format(CultureInfo.InvariantCulture, "ядер {0}, чисел {1} ({2:F1} МБ)", dict.Count, doubles, doubles * 8.0 / 1048576.0);
        }

        /// <summary>
        /// (П304) Правка калибровки ширины спектра перед разбором — контроль ключа банка
        /// ядер: коэффициенты ширины (`coefs*K`, `coef&lt;i&gt;*K`) и форма пика (`type=`,
        /// `tailL=`, `tailR=`, `vsig=`, `vgam=`). Правится КЛОН калибровки, кладётся на место.
        /// </summary>
        static bool Mutate(ResultData rd, string mutation)
        {
            if (rd.FwhmCalibration == null)
            {
                Console.Error.WriteLine("правка «" + mutation + "»: у спектра нет калибровки ширины");
                return false;
            }
            FwhmCalibration cal = rd.FwhmCalibration.Clone();
            foreach (string part in mutation.Split(';'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int star = p.IndexOf('*');
                int eq = p.IndexOf('=');
                if (star > 0 && p.StartsWith("coef", StringComparison.Ordinal))
                {
                    double k = double.Parse(p.Substring(star + 1), CultureInfo.InvariantCulture);
                    double[] c = (double[])cal.Coefficients.Clone();
                    string which = p.Substring(4, star - 4);
                    for (int i = 0; i < c.Length; i++)
                    {
                        if (which == "s" || which == i.ToString(CultureInfo.InvariantCulture)) c[i] *= k;
                    }
                    cal.Coefficients = c;
                }
                else if (eq > 0)
                {
                    string key = p.Substring(0, eq);
                    string val = p.Substring(eq + 1);
                    switch (key)
                    {
                        case "type": cal.PeakType = int.Parse(val, CultureInfo.InvariantCulture); break;
                        case "tailL": cal.ExpGaussExpLeftTail = double.Parse(val, CultureInfo.InvariantCulture); break;
                        case "tailR": cal.ExpGaussExpRightTail = double.Parse(val, CultureInfo.InvariantCulture); break;
                        case "vsig": cal.VoigtSigma = double.Parse(val, CultureInfo.InvariantCulture); break;
                        case "vgam": cal.VoigtGamma = double.Parse(val, CultureInfo.InvariantCulture); break;
                        default: Console.Error.WriteLine("правка: неизвестное поле " + key); return false;
                    }
                }
                else
                {
                    Console.Error.WriteLine("правка: не разобрано «" + p + "»");
                    return false;
                }
            }
            rd.FwhmCalibration = cal;
            Console.WriteLine("SETUP\tправка «{0}»: тип {1}, хвосты {2}/{3}, Фойгт {4}/{5}, коэффициенты {6}",
                              mutation, cal.PeakType,
                              cal.ExpGaussExpLeftTail.ToString("R", CultureInfo.InvariantCulture),
                              cal.ExpGaussExpRightTail.ToString("R", CultureInfo.InvariantCulture),
                              cal.VoigtSigma.ToString("R", CultureInfo.InvariantCulture),
                              cal.VoigtGamma.ToString("R", CultureInfo.InvariantCulture),
                              string.Join(" ", cal.Coefficients.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
            return true;
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
