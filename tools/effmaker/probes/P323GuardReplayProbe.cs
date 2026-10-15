using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П323 15.10.2026, `A320`) ПОВТОР СТРАЖА НУЛЯ В СЕАНСЕ ПРИЛОЖЕНИЯ: побитово, время,
    /// положительный контроль.
    ///
    /// Решение Amber вопросником 15.10.2026, дословно: «Только для проб (Рекомендую)» — сеанс
    /// приложения (<see cref="FsaAnalysisSession"/>) ставит анализатору ключ «без повтора»
    /// (<c>FsaAnalyzer.AnchorZeroGuardReplay = false</c>), пробы и корпус повтор сохраняют.
    ///
    /// Плечи (все — через сеанс приложения, тот же путь, что окно):
    ///   app    — крючка проб нет: так считает приложение (на новой сборке — без повтора);
    ///   replay — пустой крючок проб: сеанс оставляет повтор (как до П323);
    ///   apphook — крючок проб САМ снимает повтор (<c>AnchorZeroGuardReplay = false</c>, только
    ///            сборка «после»): ключ приложения под крючком — нужен с <c>--ratio=</c>;
    ///   off    — крючок ставит <c>AnchorZeroGuard = "off"</c>: разбор без стража (время «до
    ///            стража» и положительный контроль чувствительности отпечатка).
    /// По каждому спектру и плечу: контрольный прогон (счётчик проходов разбора
    /// <c>FsaAnalyzer.CorePassCount</c> — отражением, у сборки «до» его нет; строки «ход
    /// привязки:» трассы нуля <c>FsaAnalyzer.ZeroTraceSink</c> — есть у обеих сборок), затем
    /// <c>--reps=</c> прогонов на время вперемешку по плечам. Отпечаток — sha256 ПОЛНОГО графа
    /// результата отражением (все поля экземпляра, double — битами), отдельно — строк окна
    /// результата в Бк (<see cref="FsaMeasurementResult.Build"/> → <c>Translate</c>) и вида
    /// отчёта (<see cref="FsaPresentationBuilder.Build"/> для дочерних и родителей: строки и
    /// слои). Отпечаток считается до Бк и вида: они не трогают результат.
    ///
    /// Ключи: --spectrum=&lt;файл&gt; (можно несколько); --arms=app,replay,off (умолчание);
    /// --reps=3; --set=&lt;имя&gt;; --fromset; --nucbase; --equilibrium=on|off (иначе — по файлу);
    /// --timeout=600; --ratio=x (запас стража <c>AnchorZeroGuardRatio</c> в плечах с крючком — чтобы
    /// запасной НЕ взялся и шёл повтор нуля по съёмке; плечо app крючка не имеет и с ним
    /// не сочетается); --dump-dir=&lt;каталог&gt; (текстовый дамп графа на каждое плечо — для diff).
    /// Строки RUN (табуляцией) — для сверки сборок «до» и «после» снаружи.
    /// Коды: 0 — app = replay побитово везде, где есть оба плеча, и каждое плечо повторяет
    /// себя; 1 — расхождение; 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class P323GuardReplayProbe
    {
        sealed class Run
        {
            public string Arm;
            public long Ms;
            public long Passes = -1;
            public int AnchorTrace = -1;
            public string HashResult, HashBq, HashView;
            public FsaResult Result;
            public string BqText;
            public bool Fired, Taken;
        }

        static string SetName;
        static bool FromSet, NucBase;
        static string Equilibrium;
        static int TimeoutS = 600;
        static string DumpDir;
        static double Ratio = double.NaN;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            var arms = new List<string> { "app", "replay", "off" };
            int reps = 3;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--arms=", StringComparison.Ordinal)) arms = a.Substring(7).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                else if (a.StartsWith("--reps=", StringComparison.Ordinal)) reps = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) SetName = a.Substring(6);
                else if (a == "--fromset") FromSet = true;
                else if (a == "--nucbase") NucBase = true;
                else if (a.StartsWith("--equilibrium=", StringComparison.Ordinal)) Equilibrium = a.Substring(14);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) TimeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--dump-dir=", StringComparison.Ordinal)) DumpDir = a.Substring(11);
                else if (a.StartsWith("--ratio=", StringComparison.Ordinal)) Ratio = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectra.Count == 0) { Console.Error.WriteLine("нужен хотя бы один --spectrum="); return 2; }
            foreach (string arm in arms)
            {
                if (arm != "app" && arm != "apphook" && arm != "replay" && arm != "off") { Console.Error.WriteLine("плечо не знаю: " + arm); return 2; }
                if (arm == "app" && !double.IsNaN(Ratio)) { Console.Error.WriteLine("--ratio= ставится крючком, а у плеча app крючка нет — берите apphook"); return 2; }
                if (arm == "apphook" && typeof(FsaAnalyzer).GetProperty("AnchorZeroGuardReplay") == null) { Console.Error.WriteLine("плечо apphook — только сборке «после»"); return 2; }
            }
            if (Equilibrium != null && Equilibrium != "on" && Equilibrium != "off") { Console.Error.WriteLine("--equilibrium= принимает on|off"); return 2; }
            if (DumpDir != null) Directory.CreateDirectory(DumpDir);

            PropertyInfo replayKey = typeof(FsaAnalyzer).GetProperty("AnchorZeroGuardReplay");
            PropertyInfo passCounter = typeof(FsaAnalyzer).GetProperty("CorePassCount", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("SETUP\tсборка: {0}; ключ повтора AnchorZeroGuardReplay {1}; счётчик проходов {2}",
                              typeof(FsaAnalyzer).Assembly.Location,
                              replayKey != null ? "есть (сборка «после»)" : "НЕТ (сборка «до»)",
                              passCounter != null ? "есть" : "нет");
            {
                var probe = new FsaAnalyzer();
                Console.WriteLine("SETUP\tумолчание анализатора: AnchorZeroGuard «{0}», запас {1}, повтор {2}",
                                  probe.AnchorZeroGuard, probe.AnchorZeroGuardRatio.ToString("R", CultureInfo.InvariantCulture),
                                  replayKey != null ? replayKey.GetValue(probe).ToString() : "(ключа нет — повтор всегда)");
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            if (SetName != null)
            {
                NuclideSet set = null;
                foreach (NuclideSet s in nuclides.NuclideSets)
                {
                    if (string.Equals(s.Name, SetName, StringComparison.OrdinalIgnoreCase)) set = s;
                }
                if (set == null) { Console.Error.WriteLine("сета нет: " + SetName); return 2; }
                nuclides.ActiveSet = set;
            }
            Console.WriteLine("SETUP\tсет {0}; матрицы {1}; состав {2}; равновесие {3}",
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет)",
                              BecquerelMonitor.EfficiencyMaker.ResponseMatrixStore.Directory,
                              FromSet ? "из сета" : NucBase ? "из NucBase" : "по файлу",
                              Equilibrium ?? "по файлу");
            if (!double.IsNaN(Ratio))
            {
                Console.WriteLine("SETUP\tзапас стража в плечах с крючком: {0}", Ratio.ToString("R", CultureInfo.InvariantCulture));
            }

            int code = 0;
            foreach (string path in spectra)
            {
                int rc = OneSpectrum(path, arms, reps, nuclides, passCounter);
                if (rc > code) code = rc;
            }
            Console.WriteLine();
            Console.WriteLine("ИТОГ: код {0}", code);
            return code;
        }

        static int OneSpectrum(string path, List<string> arms, int reps, NuclideDefinitionManager nuclides, PropertyInfo passCounter)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            Console.WriteLine();
            Console.WriteLine("=== {0} ===", name);
            if (!File.Exists(path)) { Console.WriteLine("  нет файла: " + path); return 2; }

            var control = new Dictionary<string, Run>();
            var timed = new Dictionary<string, List<Run>>();
            foreach (string arm in arms)
            {
                // контрольный прогон: трасса нуля и счётчик проходов (вне замера времени)
                int anchorLines = 0;
                FsaAnalyzer.ZeroTraceSink = line =>
                {
                    if (line != null && line.StartsWith("ход привязки:", StringComparison.Ordinal)) Interlocked.Increment(ref anchorLines);
                };
                Run c;
                try
                {
                    c = RunOnce(path, arm, nuclides, passCounter, true);
                }
                finally
                {
                    FsaAnalyzer.ZeroTraceSink = null;
                }
                if (c == null) return 3;
                c.AnchorTrace = anchorLines;
                control[arm] = c;
                timed[arm] = new List<Run>();
                Console.WriteLine("RUN\t{0}\t{1}\tcontrol\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}",
                                  name, arm, c.Ms, c.Passes, c.AnchorTrace, c.HashResult, c.HashBq, c.HashView);
            }

            for (int r = 0; r < reps; r++)
            {
                foreach (string arm in arms)
                {
                    Run t = RunOnce(path, arm, nuclides, passCounter, false);
                    if (t == null) return 3;
                    timed[arm].Add(t);
                    Console.WriteLine("RUN\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}",
                                      name, arm, r + 1, t.Ms, t.Passes, -1, t.HashResult, t.HashBq, t.HashView);
                }
            }

            int code = 0;
            Run first = control[arms[0]];
            FsaResult res = first.Result;
            Console.WriteLine("  страж (по служебной строке, плечо {0}): сработал {1}; взят запасной {2}",
                              arms[0], first.Fired ? "ДА" : "нет", first.Taken ? "ДА" : "нет");
            Console.WriteLine("  χ²/ndf {0}; χ²/ndf Пуассона {1}; матрица {2}",
                              res.Chi2Ndf.ToString("F4", CultureInfo.InvariantCulture),
                              res.Chi2NdfPoisson.ToString("F4", CultureInfo.InvariantCulture),
                              res.ResponseMatrixUsed ? "применена" : "нет");
            Console.WriteLine("  привязка: {0}", res.AnchorNote);
            Console.Write(first.BqText);
            foreach (string arm in arms)
            {
                Run c = control[arm];
                List<long> ms = timed[arm].Select(x => x.Ms).OrderBy(x => x).ToList();
                long median = ms.Count > 0 ? ms[ms.Count / 2] : c.Ms;
                Console.WriteLine("  [{0,-6}] проходов разбора {1}; строк «ход привязки» {2}; время медиана {3} мс (контроль {4}; прогоны {5}); сработал {6}, взят {7}",
                                  arm, c.Passes >= 0 ? c.Passes.ToString(CultureInfo.InvariantCulture) : "—", c.AnchorTrace,
                                  median, c.Ms, string.Join(", ", timed[arm].Select(x => x.Ms.ToString(CultureInfo.InvariantCulture))),
                                  c.Fired ? "да" : "нет", c.Taken ? "да" : "нет");
                Console.WriteLine("           отпечаток: результат {0}; Бк {1}; вид {2}", c.HashResult, c.HashBq, c.HashView);
                foreach (Run t in timed[arm])
                {
                    if (t.HashResult != c.HashResult || t.HashBq != c.HashBq || t.HashView != c.HashView)
                    {
                        Console.WriteLine("  ⚠ [{0}] ПЛЕЧО НЕ ПОВТОРЯЕТ СЕБЯ: {1}/{2}/{3}", arm, t.HashResult, t.HashBq, t.HashView);
                        code = 1;
                    }
                }
            }

            foreach (string left in new[] { "app", "apphook" })
            {
                if (control.ContainsKey(left) && control.ContainsKey("replay"))
                {
                    Run a = control[left], b = control["replay"];
                    bool same = a.HashResult == b.HashResult && a.HashBq == b.HashBq && a.HashView == b.HashView;
                    Console.WriteLine("  СВЕРКА {0} = replay (результат, Бк, вид): {1}", left, same ? "ПОБИТОВО ТО ЖЕ" : "РАСХОДИТСЯ");
                    if (!same) code = 1;
                }
            }

            if (control.ContainsKey("off") && control.ContainsKey("replay"))
            {
                Run b = control["replay"], o = control["off"];
                bool same = o.HashResult == b.HashResult;
                Console.WriteLine("  контроль чувствительности: off {0} replay по результату ({1})",
                                  same ? "=" : "≠",
                                  b.Fired ? (same ? "⚠ страж сработал, а отпечаток не различил" : "страж сработал — отличие ожидаемо (служебная строка, у взятого — числа)")
                                          : (same ? "страж молчит — совпадение ожидаемо" : "⚠ страж молчит, а отпечаток различил"));
                if (b.Fired == same) code = 1;
            }

            return code;
        }

        static Run RunOnce(string path, string arm, NuclideDefinitionManager nuclides, PropertyInfo passCounter, bool isControl)
        {
            ResultData rd = Load(path, nuclides);
            if (rd == null) return null;
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (NucBase) options.DbLookups = true;
            if (FromSet) options.FromSet = true;
            if (Equilibrium != null) options.ChainEquilibrium = Equilibrium == "on";

            FsaAnalyzer hooked = null;
            if (arm == "app")
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
            }
            else if (arm == "replay")
            {
                FsaAnalysisSession.ProbeAnalyzerHook = an => { hooked = an; SetRatio(an); };
            }
            else if (arm == "apphook")
            {
                FsaAnalysisSession.ProbeAnalyzerHook = an =>
                {
                    hooked = an;
                    SetRatio(an);
                    typeof(FsaAnalyzer).GetProperty("AnchorZeroGuardReplay").SetValue(an, false);
                };
            }
            else
            {
                FsaAnalysisSession.ProbeAnalyzerHook = an => { hooked = an; SetRatio(an); an.AnchorZeroGuard = "off"; };
            }

            var run = new Run { Arm = arm };
            long before = passCounter != null ? (long)passCounter.GetValue(null) : -1;
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            var sw = Stopwatch.StartNew();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            bool finished = done.WaitOne(TimeSpan.FromSeconds(TimeoutS));
            sw.Stop();
            FsaAnalysisSession.ProbeAnalyzerHook = null;
            run.Ms = sw.ElapsedMilliseconds;
            run.Passes = passCounter != null ? (long)passCounter.GetValue(null) - before : -1;
            if (!finished || session.Result == null)
            {
                Console.WriteLine("  [{0}] разбор не завершился: {1}", arm, session.Status);
                return null;
            }

            FsaResult result = session.Result;
            run.Result = result;
            string note = result.AnchorNote ?? "";
            run.Fired = note.Contains("страж нуля (A320)");
            run.Taken = run.Fired && note.Contains(": взят «");

            string tag = Path.GetFileNameWithoutExtension(path) + "_" + arm;
            run.HashResult = Hash(result, isControl ? tag + "_result" : null);

            // строки окна результата (Бк) — тем же путём, что окно Measurement Result
            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, result, nuclides.NuclideDefinitions,
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            var text = new StringBuilder();
            text.AppendLine("  окно результата: " + summary.Describe(null) + "; " + summary.Details(null));
            object bqGraph = null;
            if (built != null)
            {
                var manager = new MeasurementResultManager();
                MeasurementResultCollection bq = manager.Translate(built, ResultTranslation.Becquerels);
                // строки и шапка набора без ссылки на ResultData: у спектра без конца
                // набора его endTime ставится временем загрузки, и отпечаток плыл бы по часам
                bqGraph = new object[] { bq.ResultList, bq.MeasurementTime, bq.LiveTime, bq.SourceKey, built.ResultList };
                foreach (MeasurementResult row in bq.ResultList)
                {
                    text.AppendFormat(CultureInfo.InvariantCulture, "    {0,-14} {1,14} ± {2,12}  a# {3,12}  {4}\n",
                                      row.Line != null ? row.Line.Name : "?",
                                      row.IsValid ? row.ResultValue.ToString("R", CultureInfo.InvariantCulture) : row.StatusText,
                                      row.IsValid ? row.ResultError.ToString("G6", CultureInfo.InvariantCulture) : "",
                                      row.IsValid ? row.MDA.ToString("G6", CultureInfo.InvariantCulture) : "",
                                      row.IsValid ? "" : "(нет числа)");
                }
            }
            run.BqText = text.ToString();
            run.HashBq = Hash(new object[] { summary, bqGraph, run.BqText }, isControl ? tag + "_bq" : null);

            // вид отчёта: строки и слои, для дочерних и для родителей
            var views = new List<object>();
            foreach (FsaGrouping g in new[] { FsaGrouping.Daughters, FsaGrouping.Parents })
            {
                FsaPresentation p = FsaPresentationBuilder.Build(result, g, false);
                views.Add(new object[] { g.ToString(), p.Grouping.ToString(), p.QualityText, p.Rows, p.Layers });
            }
            run.HashView = Hash(views, isControl ? tag + "_view" : null);
            return run;
        }

        static void SetRatio(FsaAnalyzer an)
        {
            if (!double.IsNaN(Ratio)) an.AnchorZeroGuardRatio = Ratio;
        }

        // ---------------- отпечаток: общий довесок FsaResultDump (`AMBER213`, П241) ----------------

        /// <summary>
        /// sha256 полного снимка графа (<c>FsaResultDump</c>: все поля экземпляра, числа — `R` и
        /// биты), первые 16 знаков; при <c>--dump-dir=</c> снимок контрольного прогона — в файл.
        /// </summary>
        static string Hash(object root, string dumpName)
        {
            string text = FsaResultDump.Of(root);
            if (dumpName != null && DumpDir != null)
            {
                File.WriteAllText(Path.Combine(DumpDir, dumpName + ".txt"), text, new UTF8Encoding(false));
            }
            return FsaResultDump.Sha(text).Substring(0, 16);
        }

        // ---------------- загрузка спектра — как FsaAnchorZeroProbeP258 ----------------

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
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

            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
