using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER326`, полоса `a326`) ВРЕМЯ РАЗБОРА FSA ТЕМ ЖЕ ПУТЁМ, ЧТО ОКНО, И ПО СТАДИЯМ.
    ///
    /// Отличия от <c>FsaSpeedProbeP241</c>, ради которых проба заведена:
    /// * настройки — как на снимке окна Amber 11.11.2026: состав «From NucBase», равновесие ряда
    ///   ВКЛ, пять компонентов модели ВКЛ, нижняя граница 0 (ключи <c>--eq=</c>, <c>--db=</c>);
    /// * ⛔ повтор стража нуля и отката привязки (<c>AnchorZeroGuardReplay</c>) — КАК У ПРИЛОЖЕНИЯ,
    ///   то есть выключен: P241 ставит крючок анализатора, а крючок сам по себе включает повтор
    ///   (<c>FsaAnalysisSession.Capture</c>), и проба мерила бы больше счёта, чем окно
    ///   (<c>--replay</c> — включить, как у P241);
    /// * режим <c>--mode=stages</c>: подготовка сеанса (<c>Capture</c>, отражением) и фоновая
    ///   половина (<c>Compute</c>) по шагам с секундомером — вывод состава, сборка образов, доли
    ///   членов ряда, разбор, расхождения поставок, изотопный состав, родители сета вне состава,
    ///   и сборка картинки окна (<c>FsaPresentationBuilder.Build</c>, группировка «дочерние»);
    /// * загрузка машины за время прогона: процессорное время ВСЕХ прочих процессов (системные
    ///   времена ядра минус простой минус свой процесс), в «ядрах» — доля от стены.
    ///
    /// Отпечаток результата — <c>FsaResultDump</c> (все поля, числа битами): сверка «до/после».
    /// Ключи: --spectrum=&lt;файл&gt; --runs=N (3) --mode=session|stages|both (both) --dump=&lt;каталог&gt;
    ///        --eq=on|off (on) --db=on|off (on) --replay --threads=N --timeout=600
    ///        --trace — след заходов ядра и решений сторожа автомаски (<c>FsaAnalyzer.ProbeCoreTrace</c>);
    ///        --nomask — автомаска фона ВЫКЛ (<c>BackgroundExcessMask</c>): ЗАМЕР, а не режим окна, —
    ///        цена повторного захода «весь разбор заново без маски» (`AMBER295`) как разность времён;
    ///        дамп такого прогона с дампом окна не сверяется (служебные строки маски другие).
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился; 4 — прогоны разошлись.
    /// </summary>
    static class FsaPerfProbeA326
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class Load
        {
            long idle0, kernel0, user0;
            TimeSpan own0;
            long t0;

            public Load()
            {
                GetSystemTimes(out this.idle0, out this.kernel0, out this.user0);
                Process me = Process.GetCurrentProcess();
                this.own0 = me.TotalProcessorTime;
                this.t0 = Stopwatch.GetTimestamp();
            }

            /// <summary>«своё cpu мс; прочие cpu мс; прочие, ядер» за отрезок.</summary>
            public string Text(out double ownMs)
            {
                long idle, kernel, user;
                GetSystemTimes(out idle, out kernel, out user);
                Process me = Process.GetCurrentProcess();
                me.Refresh();
                ownMs = (me.TotalProcessorTime - this.own0).TotalMilliseconds;
                double busyMs = ((kernel - this.kernel0) + (user - this.user0) - (idle - this.idle0)) / 10000.0;
                double wallMs = (Stopwatch.GetTimestamp() - this.t0) * 1000.0 / Stopwatch.Frequency;
                double othersMs = Math.Max(0.0, busyMs - ownMs);
                return string.Format(Inv, "cpu своё {0:F0} мс ({1:F2} ядра)\tпрочие {2:F0} мс ({3:F2} ядра)",
                                     ownMs, wallMs > 0 ? ownMs / wallMs : 0.0, othersMs, wallMs > 0 ? othersMs / wallMs : 0.0);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            int runs = 3, timeoutS = 600, threads = 0;
            string dumpDir = null, mode = "both";
            bool eq = true, db = true, replay = false, noMask = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--runs=", StringComparison.Ordinal)) runs = int.Parse(a.Substring(7), Inv);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), Inv);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), Inv);
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpDir = a.Substring(7);
                else if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a == "--eq=on" || a == "--eq=off") eq = a.EndsWith("on", StringComparison.Ordinal);
                else if (a == "--db=on" || a == "--db=off") db = a.EndsWith("on", StringComparison.Ordinal);
                else if (a == "--replay") replay = true;
                // (a326p) заходы ядра на копиях анализатора: 0 — подряд, 1 — заход с привязкой на копии, 2 — и запасные впрок
                else if (a.StartsWith("--core=", StringComparison.Ordinal)) FsaAnalyzer.CoreParallelMode = int.Parse(a.Substring(7), Inv);
                else if (a.StartsWith("--specdeg=", StringComparison.Ordinal)) FsaAnalyzer.CoreSpeculationMinDegree = int.Parse(a.Substring(10), Inv);
                // (a326p) прогрев как у приложения при запуске, с ожиданием конца (человек успел открыть спектр)
                else if (a == "--warmup")
                {
                    FsaWarmup.Start();
                    FsaWarmup.Wait(120000);
                    Console.WriteLine("SETUP\tпрогрев: методов {0}, {1:F0} мс", FsaWarmup.MethodsPrepared, FsaWarmup.Milliseconds);
                }
                // (a326p) замер памяти: держать сеансы живыми (как открытые документы с разбором) и выдержка перед замером
                else if (a == "--keep") keep = new List<object>();
                else if (a.StartsWith("--pause=", StringComparison.Ordinal)) pauseMs = int.Parse(a.Substring(8), Inv);
                else if (a == "--audit") { Console.Write(FsaAnalyzer.CoreFieldAudit()); return 0; }
                else if (a == "--nomask") noMask = true;
                // как приложение при запуске (`Program.cs`): отпечаток данных переноса matdb — до разбора
                else if (a == "--prefetch") BecquerelMonitor.EfficiencyMaker.MaterialDatabase.SimulatorDataFingerprint();
                else if (a == "--trace")
                {
                    // метка времени — миллисекунды от запуска пробы: по разностям видно время стены захода
                    var traceClock = Stopwatch.StartNew();
                    FsaAnalyzer.ProbeCoreTrace = s => Console.WriteLine("TRACE\t" + traceClock.Elapsed.TotalMilliseconds.ToString("F0", Inv) + " мс\t" + s);
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectra.Count == 0 || runs < 1 || (mode != "session" && mode != "stages" && mode != "both"))
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>, --runs ≥ 1, --mode=session|stages|both");
                return 2;
            }

            if (dumpDir != null) Directory.CreateDirectory(dumpDir);
            if (threads > 0) FsaParallel.Degree = threads;
            Console.WriteLine("SETUP\tстепень параллелизма {0}; повтор стража {1}; режим {2}; eq {3}; db {4}; автомаска фона {5}",
                              FsaParallel.Degree, replay ? "ВКЛ (как P241)" : "выкл (как приложение)", mode,
                              eq ? "on" : "off", db ? "on" : "off", noMask ? "ВЫКЛ (--nomask, замер)" : "как приложение");
            Console.WriteLine("SETUP\tконфигурация {0}", Package.GetInstance().Config);

            Console.WriteLine("SETUP	заходы ядра на копиях: режим {0}, впрок при степени от {1}", FsaAnalyzer.CoreParallelMode, FsaAnalyzer.CoreSpeculationMinDegree);
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Console.WriteLine("SETUP\tсет {0}; матрицы {1}",
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет)",
                              BecquerelMonitor.EfficiencyMaker.ResponseMatrixStore.Directory);

            int code = 0;
            foreach (string path in spectra)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                ResultData rd = LoadSpectrum(path, nuclides);
                if (rd == null) { code = 2; continue; }
                FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
                options.DbLookups = db;
                options.FromSet = false;
                options.ChainEquilibrium = eq;
                options.AtomicXray = true;
                options.CascadeSumming = true;
                options.Backscatter = true;
                options.EscapeAndAnnihilation = true;
                options.PileUp = true;
                options.FitLowerBoundKev = 0.0;
                Console.WriteLine("SETUP\t{0}: настройки {1}", name, options.Stamp);

                if (mode != "stages")
                {
                    int c = RunSession(name, rd, options, runs, timeoutS, replay, noMask, dumpDir);
                    if (c != 0) code = c;
                }

                if (mode != "session")
                {
                    int c = RunStages(name, rd, options, runs, replay, noMask, dumpDir);
                    if (c != 0) code = c;
                }
            }

            return code;
        }

        static List<object> keep;
        static int pauseMs;

        /// <summary>(a326p) Память процесса: рабочий набор, частная, пик рабочего набора, куча сборщика; сборщик серверный или нет.</summary>
        static string MemText()
        {
            if (pauseMs > 0) Thread.Sleep(pauseMs);
            Process me = Process.GetCurrentProcess();
            me.Refresh();
            return string.Format(Inv, "рабочий набор {0:F0} МБ\tчастная {1:F0} МБ\tпик рабочего набора {2:F0} МБ\tпик частной {3:F0} МБ\tкуча {4:F0} МБ\tсборщик {5}\tпотоков {6}",
                me.WorkingSet64 / 1048576.0, me.PrivateMemorySize64 / 1048576.0, me.PeakWorkingSet64 / 1048576.0,
                me.PeakPagedMemorySize64 / 1048576.0, GC.GetTotalMemory(false) / 1048576.0,
                System.Runtime.GCSettings.IsServerGC ? "серверный" : "рабочей станции", me.Threads.Count);
        }

        static int RunSession(string name, ResultData rd, FsaCalculationOptions options, int runs, int timeoutS,
                              bool replay, bool noMask, string dumpDir)
        {
            FsaAnalysisSession.ProbeAnalyzerHook = a =>
            {
                a.AnchorZeroGuardReplay = replay;
                if (noMask) a.BackgroundExcessMask = false;
            };
            int code = 0;
            var wall = new List<double>();
            string firstSha = null;
            try
            {
                for (int r = 0; r < runs; r++)
                {
                    var session = new FsaAnalysisSession();
                    var done = new ManualResetEvent(false);
                    session.Completed += (s, e) => done.Set();
                    var load = new Load();
                    long cores0 = FsaAnalyzer.CorePassCount;
                    long once0 = FsaAnalyzer.OncePassCount;
                    var sw = Stopwatch.StartNew();
                    session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
                    bool finished = done.WaitOne(TimeSpan.FromSeconds(timeoutS));
                    sw.Stop();
                    double ownMs;
                    string loadText = load.Text(out ownMs);
                    if (!finished)
                    {
                        Console.WriteLine("SESSION\t{0}\t{1}\tНЕ ЗАВЕРШЁН за {2} с", name, r + 1, timeoutS);
                        return 3;
                    }

                    FsaResult result = session.Result;
                    var sb = new StringBuilder();
                    sb.Append("status=").Append(session.Status ?? "null").Append('\n');
                    sb.Append(FsaResultDump.Of(result));
                    string text = sb.ToString();
                    string sha = FsaResultDump.Sha(text);
                    wall.Add(sw.Elapsed.TotalMilliseconds);
                    Console.WriteLine("SESSION\t{0}\t{1}\t{2} мс\t{3}\tsha {4}\tкомпонентов {5}\tχ²/ndf {6}\tматрица {7}\tстатус {8}",
                                      name, r + 1, sw.Elapsed.TotalMilliseconds.ToString("F0", Inv), loadText,
                                      sha.Substring(0, 16),
                                      result != null && result.Components != null ? result.Components.Count : -1,
                                      result != null ? result.Chi2Ndf.ToString("R", Inv) : "—",
                                      result == null ? "—" : (result.ResponseMatrixUsed ? "ПРИМЕНЕНА" : "нет"),
                                      session.Status ?? "null");
                    Console.WriteLine("PASSES\t{0}\t{1}\tAnalyzeCore {2}\tAnalyzeOnce {4}\tбанк {3}", name, r + 1,
                                      FsaAnalyzer.CorePassCount - cores0, BankText(), FsaAnalyzer.OncePassCount - once0);
                    if (keep != null) { keep.Add(session); keep.Add(result); }
                    Console.WriteLine("MEM\t{0}\t{1}\t{2}", name, r + 1, MemText());
                    if (r == 0)
                    {
                        firstSha = sha;
                        if (dumpDir != null)
                        {
                            File.WriteAllText(Path.Combine(dumpDir, name + ".session.dump.txt"), text, new UTF8Encoding(false));
                        }
                    }
                    else if (sha != firstSha)
                    {
                        Console.WriteLine("DIVERGED\t{0}\tпрогон {1} разошёлся с первым", name, r + 1);
                        code = 4;
                    }
                }
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
            }

            List<double> warm = wall.Count > 1 ? wall.Skip(1).ToList() : wall;
            Console.WriteLine("SUMMARY\t{0}\tсеанс: холодный {1} мс\tтёплые {2}\tмедиана тёплых {3} мс\tsha {4}",
                              name, wall[0].ToString("F0", Inv),
                              string.Join(" ", warm.Select(w => w.ToString("F0", Inv))),
                              Median(warm).ToString("F0", Inv), firstSha);
            return code;
        }

        static int RunStages(string name, ResultData rd, FsaCalculationOptions options, int runs, bool replay,
                             bool noMask, string dumpDir)
        {
            Type sessionType = typeof(FsaAnalysisSession);
            MethodInfo capture = sessionType.GetMethod("Capture", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo ratiosOf = sessionType.GetMethod("MemberActivityRatiosOf", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo remember = sessionType.GetMethod("RememberMemberActivityRatios", BindingFlags.Static | BindingFlags.NonPublic);
            if (capture == null || ratiosOf == null || remember == null)
            {
                Console.Error.WriteLine("у сборки нет Capture / MemberActivityRatiosOf / RememberMemberActivityRatios");
                return 2;
            }

            int code = 0;
            string firstSha = null;
            var names = new[] { "capture", "infer", "library", "ratios", "analyze", "supply", "isotopes", "omissions", "present" };
            var table = new List<double[]>();
            for (int r = 0; r < runs; r++)
            {
                var t = new double[names.Length];
                var load = new Load();
                long cores0 = FsaAnalyzer.CorePassCount;
                var session = new FsaAnalysisSession();
                var sw = Stopwatch.StartNew();
                object job = capture.Invoke(session, new object[] { rd, rd.BackgroundEnergySpectrum != null, options, "a326", 0 });
                t[0] = Lap(sw);
                Type jt = job.GetType();
                Func<string, object> f = n => jt.GetField(n).GetValue(job);
                var analyzer = (FsaAnalyzer)f("Analyzer");
                analyzer.AnchorZeroGuardReplay = replay;
                if (noMask) analyzer.BackgroundExcessMask = false;
                var jobOptions = (FsaCalculationOptions)f("Options");
                var peaks = (List<Peak>)f("Peaks");
                var input = (ResultData)f("CompositionInput");
                var definitions = (List<NuclideDefinition>)f("Definitions");
                var set = (NuclideSet)f("Set");

                FsaCompositionInference.Report inferred = null;
                FsaSampleSpec spec;
                if (jobOptions.DbLookups)
                {
                    spec = FsaCompositionInference.Infer(peaks, input, out inferred);
                }
                else
                {
                    Console.Error.WriteLine("--mode=stages умеет только состав «From NucBase» (--db=on)");
                    return 2;
                }

                jobOptions.ApplyTo(spec);
                t[1] = Lap(sw);
                List<FsaComponent> library = FsaSampleLibrary.Build(spec);
                t[2] = Lap(sw);
                var ratios = (Dictionary<string, double>)ratiosOf.Invoke(null, new object[] { library, definitions });
                t[3] = Lap(sw);
                FsaResult computed = analyzer.Analyze((EnergySpectrum)f("Spectrum"), (EnergySpectrum)f("Background"),
                                                      (FwhmCalibration)f("FwhmCalibration"), library,
                                                      (FsaEfficiency)f("Efficiency"));
                t[4] = Lap(sw);
                if (computed != null)
                {
                    remember.Invoke(null, new object[] { computed, ratios });
                    computed.SupplyDiscrepancies = FsaCascadeSummer.SupplyDiscrepanciesOf(computed);
                    t[5] = Lap(sw);
                    computed.IsotopeRatios = FsaSampleLibrary.IsotopeRatiosOf(computed, analyzer.LimitQuantileK);
                    t[6] = Lap(sw);
                    computed.SetOmissions.AddRange(FsaCompositionInference.SetOmissions(set, definitions, inferred, peaks, library));
                    t[7] = Lap(sw);
                    FsaPresentationBuilder.Build(computed, FsaGrouping.Daughters, false);
                    t[8] = Lap(sw);
                }

                double ownMs;
                string loadText = load.Text(out ownMs);
                table.Add(t);
                string sha = computed != null ? FsaResultDump.Sha(FsaResultDump.Of(computed)) : "null";
                var line = new StringBuilder();
                line.AppendFormat(Inv, "STAGES\t{0}\t{1}\tвсего {2:F0} мс", name, r + 1, t.Sum());
                for (int i = 0; i < names.Length; i++) line.AppendFormat(Inv, "\t{0} {1:F0}", names[i], t[i]);
                line.Append('\t').Append(loadText);
                line.Append("\tобразов ").Append(library.Count.ToString(Inv));
                line.Append("\tAnalyzeCore ").Append((FsaAnalyzer.CorePassCount - cores0).ToString(Inv));
                line.Append("\tsha ").Append(sha.Length >= 16 ? sha.Substring(0, 16) : sha);
                line.Append("\tбанк ").Append(BankText());
                Console.WriteLine(line.ToString());
                if (r == 0)
                {
                    firstSha = sha;
                    if (dumpDir != null && computed != null)
                    {
                        File.WriteAllText(Path.Combine(dumpDir, name + ".stages.dump.txt"), FsaResultDump.Of(computed), new UTF8Encoding(false));
                    }
                }
                else if (sha != firstSha)
                {
                    Console.WriteLine("DIVERGED\t{0}\tстадии: прогон {1} разошёлся с первым", name, r + 1);
                    code = 4;
                }
            }

            var med = new StringBuilder();
            med.AppendFormat(Inv, "SUMMARY\t{0}\tстадии, медиана тёплых", name);
            List<double[]> warmRows = table.Count > 1 ? table.Skip(1).ToList() : table;
            for (int i = 0; i < names.Length; i++)
            {
                med.AppendFormat(Inv, "\t{0} {1:F0}", names[i], Median(warmRows.Select(row => row[i]).ToList()));
            }

            med.Append("\tsha ").Append(firstSha);
            Console.WriteLine(med.ToString());
            return code;
        }

        /// <summary>Реестр банков ядер на процесс (отражением, `ShapeKernelBank.Describe`).</summary>
        static string BankText()
        {
            Type bankType = typeof(FsaAnalyzer).GetNestedType("ShapeKernelBank", BindingFlags.NonPublic);
            MethodInfo describe = bankType != null
                ? bankType.GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                : null;
            return describe != null ? Convert.ToString(describe.Invoke(null, null), Inv) : "нет";
        }

        static double Lap(Stopwatch sw)
        {
            double ms = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            return ms;
        }

        static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            var s = values.OrderBy(v => v).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }

        static ResultData LoadSpectrum(string path, NuclideDefinitionManager nuclides)
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

            var sw = Stopwatch.StartNew();
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\t{0}: пиков {1} (поиск {2:F0} мс), каналов {3}; кривая {4}; фон {5}",
                              Path.GetFileName(path), rd.DetectedPeaks.Count, sw.Elapsed.TotalMilliseconds,
                              s.NumberOfChannels, rd.Efficiency != null ? "«" + rd.Efficiency.Name + "»" : "нет",
                              rd.BackgroundEnergySpectrum != null ? "есть" : "нет");
            return rd;
        }
    }
}
