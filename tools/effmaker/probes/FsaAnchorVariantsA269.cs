using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER269`, замер 04.11.2026) СРАВНЕНИЕ ПЛЕЧ ПРИВЯЗКИ ШКАЛЫ FSA НА ОДНИХ ДАННЫХ.
    ///
    /// Плечи (все — режим «Из NucBase», один и тот же рецепт загрузки и разбора):
    ///   0    — как сейчас: откат привязки (А2) включён, прочие плечи выключены;
    ///   A    — тормоза: (1) усиление не меняется, если не осталось двух опор,
    ///          разнесённых по шкале (<c>AnchorGainBrake = "count"</c>), и (2) откат
    ///          по χ²/ndf Пуассона (<c>AnchorRollback</c>, уже в плече 0);
    ///   B    — опоры только у линий, подтверждённых поиском пиков
    ///          (<c>AnchorConfirmByPeaks</c> + <c>DetectedPeakKev</c> из таблицы
    ///          «Обнаруженные пики», допуск — FWHM по калибровке файла);
    ///   AB   — A и B вместе.
    ///
    /// ⛔ Ключи A1/B сняты из кода 01.11.2026; проба ставит их ОТРАЖЕНИЕМ и требует
    /// сборку, где они есть (временная сборка полосы). На обычной сборке честно
    /// отказывает кодом 4, а не молча мерит одно и то же плечо четыре раза.
    ///
    /// Ключи: --spectrum=&lt;файл&gt; (обязателен); --arm=0|A|B|AB|all (по умолчанию all);
    /// --no-equilibrium (по умолчанию равновесие ряда ВКЛ — рецепт журнала `AMBER269`);
    /// --control — положительный контроль: откат (А2) СНЯТ, чтобы тормоз (А1) был
    /// виден; на спектре с одной опорой у нуля плечо 0 обязано уйти по шкале,
    /// плечо A — остаться; --matrix-any — поставить встроенную матрицу файла
    /// принудительно (клеймо не проверяется: матрицы этих спектров — физика 27,
    /// формат 11, нынешним кодом отвергаются); --peaks — печатать найденные пики;
    /// --out=&lt;файл&gt; — весь вывод ещё и в файл.
    ///
    /// Коды: 0 — посчитано; 2 — ключи/файл; 3 — разбор не завершился ИЛИ в сборке
    /// нет ключей плеч (нужна временная сборка замера) — отказ быстрый, до запуска
    /// сеанса, а не через 600 с ожидания.
    /// </summary>
    static class FsaAnchorVariantsA269
    {
        static readonly string[] AllArms = { "0", "A", "B", "AB" };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, outPath = null, arm = "all";
            bool equilibrium = true, control = false, printPeaks = false, matrixAny = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--arm=", StringComparison.Ordinal)) arm = a.Substring(6);
                else if (a == "--no-equilibrium") equilibrium = false;
                else if (a == "--control") control = true;
                else if (a == "--peaks") printPeaks = true;
                else if (a == "--matrix-any") matrixAny = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrum == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            var arms = new List<string>();
            if (string.Equals(arm, "all", StringComparison.OrdinalIgnoreCase))
            {
                arms.AddRange(AllArms);
            }
            else
            {
                foreach (string p in arm.Split(','))
                {
                    string one = p.Trim().ToUpperInvariant();
                    if (one == "0" || one == "A" || one == "B" || one == "AB")
                    {
                        arms.Add(one);
                    }
                    else
                    {
                        Console.Error.WriteLine("плечо не знаю: " + p + " (0|A|B|AB|all)");
                        return 2;
                    }
                }
            }

            // ⛔ Ключи плеч сняты из кода 01.11.2026. На обычной сборке их нет, и
            // ставить их отражением бессмысленно: отказ ДО сеанса, иначе исключение
            // в потоке сеанса не даёт Completed и проба висит 600 с.
            List<string> missing = MissingKeys();
            if (missing.Count > 0)
            {
                Console.Error.WriteLine("ОТКАЗ: в этой сборке нет ключей FsaAnalyzer."
                    + string.Join(", FsaAnalyzer.", missing.ToArray())
                    + " — плечи (А1)/(Б) сняты 01.11.2026; нужна временная сборка замера AMBER269");
                return 3;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            string deviceNote;
            ResultData rd = Load(spectrum, nuclides, out deviceNote);
            if (rd == null)
            {
                return 2;
            }

            var log = new StringBuilder();
            void Say(string line)
            {
                Console.WriteLine(line);
                log.AppendLine(line);
            }

            EfficiencyMaker.ResponseMatrix forcedMatrix = null;
            string matrixNote = null;
            if (matrixAny)
            {
                EfficiencyMaker.MatrixRefusal refusal;
                int fileFormat;
                EfficiencyMaker.ResponseMatrixSource source;
                string spectrumRefusal;
                forcedMatrix = EfficiencyMaker.ResponseMatrixStore.Resolve(
                    rd.Efficiency, out refusal, out fileFormat, out source, out spectrumRefusal);
                if (forcedMatrix == null && rd.Efficiency != null && rd.Efficiency.EmbeddedMatrix != null)
                {
                    // Клеймо встроенной матрицы не сошлось с нынешней геометрией —
                    // для замера ставим её принудительно (та же матрица файла,
                    // тот же класс, что у --matrix-any критика `critrn`).
                    forcedMatrix = LoadEmbedded(rd.Efficiency.EmbeddedMatrix);
                }

                matrixNote = "матрица файла: " + (forcedMatrix != null ? "есть" : "НЕТ")
                             + "; отказ " + refusal.ToString()
                             + "; формат " + fileFormat.ToString(CultureInfo.InvariantCulture)
                             + "; источник " + source.ToString()
                             + (string.IsNullOrEmpty(spectrumRefusal) ? "" : "; " + spectrumRefusal);
                if (forcedMatrix != null && rd.Efficiency != null && rd.Efficiency.Geometry != null)
                {
                    matrixNote += "; клеймо " + (forcedMatrix.IsValidFor(rd.Efficiency.Geometry)
                        ? "сошлось" : "НЕ сошлось — ставлю принудительно");
                }
            }

            Say("спектр: " + spectrum);
            Say("прибор: " + deviceNote);
            if (matrixNote != null)
            {
                Say(matrixNote);
            }
            Say("каналов: " + rd.EnergySpectrum.NumberOfChannels.ToString(CultureInfo.InvariantCulture)
                + "; отсчётов: " + rd.EnergySpectrum.TotalPulseCount.ToString(CultureInfo.InvariantCulture)
                + "; фон: " + (rd.BackgroundEnergySpectrum != null ? "свой, в файле" : "нет"));
            if (control)
            {
                // Положительный контроль: откат (А2) снят, чтобы тормоз (А1)
                // был виден. Вход — спектр с одной опорой у нуля (v3): плечо 0
                // обязано уйти по шкале, плечо A — остаться. Годный вход — тот
                // же спектр, но с тормозом: числа обязаны разойтись.
                Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: откат (А2) СНЯТ — тормоз (А1) должен быть виден: плечо 0 уйдёт, плечо A останется");
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            var peakKev = new List<double>();
            foreach (Peak p in rd.DetectedPeaks)
            {
                if (p != null && !double.IsNaN(p.Energy))
                {
                    peakKev.Add(p.Energy);
                }
            }

            Say("пиков поиска: " + rd.DetectedPeaks.Count.ToString(CultureInfo.InvariantCulture));
            if (printPeaks)
            {
                foreach (Peak p in rd.DetectedPeaks)
                {
                    Say(string.Format(CultureInfo.InvariantCulture,
                        "  пик {0,9:F2} кэВ кан. {1,5} SNR {2,7:F1} {3}",
                        p.Energy, p.Channel, p.SNR, p.Nuclide != null ? p.Nuclide.Name : "(неизвестный)"));
                }
            }

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            options.DbLookups = true;          // «Из NucBase»
            options.FromSet = false;
            options.ChainEquilibrium = equilibrium;
            Say("режим: Из NucBase" + (equilibrium ? " + равновесие ряда" : ""));

            var results = new Dictionary<string, FsaResult>();
            var gains = new Dictionary<string, double>();
            var chi = new Dictionary<string, double>();
            var used = new Dictionary<string, int>();
            var rolled = new Dictionary<string, bool>();
            var notes = new Dictionary<string, string>();
            bool failed = false;

            foreach (string one in arms)
            {
                Say("");
                Say("=== плечо " + one + " ===");
                FsaAnalyzer analyzer;
                List<string> passes;
                string status;
                FsaResult result;
                try
                {
                    result = Run(rd, options, one, peakKev.ToArray(), forcedMatrix, !control,
                                 out analyzer, out passes, out status);
                }
                catch (MissingMemberException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Say("ОТКАЗ: " + ex.Message);
                    failed = true;
                    continue;
                }

                if (result == null)
                {
                    Say("ОТКАЗ: разбор не завершился: " + (status ?? "нет результата"));
                    failed = true;
                    continue;
                }

                results[one] = result;
                gains[one] = result.Gain;
                chi[one] = result.Chi2NdfPoisson;
                used[one] = result.ScaleAnchorsUsed;
                rolled[one] = result.AnchorRolledBack;
                notes[one] = result.AnchorNote;

                Say(string.Format(CultureInfo.InvariantCulture,
                    "ИТОГ: χ²/ndf Пуассона {0:F2}; усиление {1:F6}; нуль {2:F3} кан.; опор {3}; матрица {4}; откат {5}",
                    result.Chi2NdfPoisson, result.Gain, result.OffsetChannels, result.ScaleAnchorsUsed,
                    result.ResponseMatrixUsed ? "да" : "нет", result.AnchorRolledBack ? "ДА" : "нет"));
                if (result.AnchorRolledBack)
                {
                    Say(string.Format(CultureInfo.InvariantCulture,
                        "  отменённый разбор: опор {0}, усиление {1:F6}, χ²/ndf {2:F2}",
                        result.AnchorRollbackAnchors, result.AnchorRollbackGain, result.AnchorRollbackChi2NdfPoisson));
                }

                Say("  привязка: " + (result.AnchorNote ?? "—"));
                Say("  ход привязки: " + (analyzer != null ? analyzer.ScaleAnchorStats : "—"));

                // опоры прохода 0 — то, что просит постановка: состав и сдвиги
                bool printedPass0 = false;
                foreach (string line in passes)
                {
                    if (line.StartsWith("разбор ", StringComparison.Ordinal))
                    {
                        int at = line.IndexOf("проход 0:", StringComparison.Ordinal);
                        if (at < 0)
                        {
                            break;
                        }

                        printedPass0 = true;
                        Say("  проход 0:");
                        Say(Indent(line));
                        continue;
                    }

                    if (printedPass0)
                    {
                        Say(Indent(line));
                    }
                }

                if (!printedPass0)
                {
                    Say("  прохода 0 в приёмнике нет (привязка не шла)");
                }

                Say("  итоговая таблица опор:");
                if (result.ScaleAnchors == null || result.ScaleAnchors.Count == 0)
                {
                    Say("    (пусто)");
                }
                else
                {
                    foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
                    {
                        Say(string.Format(CultureInfo.InvariantCulture,
                            "    {0,9:F2} кэВ {1,-14} модель {2,9:F2} измерено {3,9:F2} сдвиг {4,8:F2} кэВ z {5,7:F1} {6}{7}",
                            anchor.LineKev, anchor.Component, anchor.ModelKev, anchor.MeasuredKev, anchor.ShiftKev,
                            anchor.Z, anchor.Used ? "ВЗЯТА" : "отказ",
                            string.IsNullOrEmpty(anchor.Refusal) ? "" : " (" + anchor.Refusal + ")"));
                    }
                }

                Say("  состав (имп/с, доля, z):");
                foreach (FsaComponentResult row in result.Components)
                {
                    if (row == null || row.Kind == FsaComponentKind.Nuisance)
                    {
                        continue;
                    }

                    Say(string.Format(CultureInfo.InvariantCulture,
                        "    {0,-14} {1,12:F3} имп/с  {2,6:F1} %  z {3,7:F1}{4}",
                        row.Name, row.CountRate, row.SharePercent, row.Z, row.BetaOnly ? "  (бета)" : ""));
                }
            }

            if (results.Count > 1)
            {
                Say("");
                Say("=== СВОДКА: плечо | χ²/ndf | усиление | опор | откат ===");
                foreach (string one in arms)
                {
                    if (!results.ContainsKey(one))
                    {
                        continue;
                    }

                    Say(string.Format(CultureInfo.InvariantCulture,
                        "{0,-3} | {1,10:F2} | {2,10:F6} | {3,3} | {4}",
                        one, chi[one], gains[one], used[one], rolled[one] ? "да" : "нет"));
                }

                string best = null;
                foreach (string one in arms)
                {
                    if (results.ContainsKey(one) && (best == null || chi[one] < chi[best]))
                    {
                        best = one;
                    }
                }

                if (best != null)
                {
                    Say("лучший по χ²/ndf: плечо " + best);
                }
            }

            if (control)
            {
                bool pass = results.ContainsKey("0") && results.ContainsKey("A")
                            && Math.Abs(gains["0"] - gains["A"]) > 1.0E-9;
                Say("");
                Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: усиление 0 = " + F(gains.ContainsKey("0") ? gains["0"] : double.NaN)
                    + ", A = " + F(gains.ContainsKey("A") ? gains["A"] : double.NaN)
                    + " — " + (pass ? "РАЗОШЛИСЬ (тормоз виден)" : "СОШЛИСЬ (контроль не сработал)"));
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, log.ToString(), new UTF8Encoding(false));
                Console.WriteLine("вывод: " + outPath);
            }

            return failed ? 3 : 0;
        }

        static FsaResult Run(ResultData rd, FsaCalculationOptions options, string arm, double[] peakKev,
                             EfficiencyMaker.ResponseMatrix forcedMatrix, bool rollback,
                             out FsaAnalyzer analyzer, out List<string> passes, out string status)
        {
            analyzer = null;
            passes = new List<string>();
            status = null;
            bool withA = arm == "A" || arm == "AB";
            bool withB = arm == "B" || arm == "AB";

            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            FsaAnalyzer last = null;
            string hookError = null;
            List<string> sinkLines = passes;
            Action<string> sink = line =>
            {
                lock (sinkLines)
                {
                    sinkLines.Add(line);
                }
            };

            FsaAnalyzer.AnchorPassSink = sink;
            FsaAnalysisSession.ProbeAnalyzerHook = an =>
            {
                try
                {
                    last = an;
                    if (forcedMatrix != null && rd.Efficiency != null && rd.Efficiency.Geometry != null)
                    {
                        FsaMatrixBinding.Bind(an, rd.Efficiency.Geometry, forcedMatrix);
                    }

                    SetKey(an, "AnchorRollback", rollback);
                    SetKey(an, "AnchorRollbackKeepsWidth", true);
                    SetKey(an, "AnchorGainBrake", withA ? "count" : "off");
                    SetKey(an, "AnchorConfirmByPeaks", withB);
                    SetKey(an, "DetectedPeakKev", withB ? peakKev : null);
                }
                catch (Exception ex)
                {
                    // Исключение в потоке сеанса не даёт Completed: будим ожидание
                    // и называем причину, а не висим 600 с.
                    hookError = ex.Message;
                    done.Set();
                }
            };
            try
            {
                session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
                if (!done.WaitOne(TimeSpan.FromSeconds(600)))
                {
                    status = "не завершился за 600 с";
                    return null;
                }

                if (hookError != null)
                {
                    status = "ключи плеч не поставились: " + hookError;
                    return null;
                }

                analyzer = last;
                status = session.Status;
                return session.Result;
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
                FsaAnalyzer.AnchorPassSink = null;
            }
        }

        /// <summary>Встроенная матрица файла без проверки клейма — для замера (`--matrix-any`).</summary>
        static EfficiencyMaker.ResponseMatrix LoadEmbedded(EfficiencyMaker.EmbeddedResponseMatrix embedded)
        {
            if (embedded == null || string.IsNullOrEmpty(embedded.Data))
            {
                return null;
            }

            try
            {
                byte[] packed = Convert.FromBase64String(embedded.Data);
                using (var input = new MemoryStream(packed))
                using (var gzip = new System.IO.Compression.GZipStream(
                    input, System.IO.Compression.CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    byte[] raw = output.ToArray();
                    using (var stream = new MemoryStream(raw, false))
                    {
                        EfficiencyMaker.MatrixRefusal read;
                        int format;
                        return EfficiencyMaker.ResponseMatrix.Load(stream, out read, out format);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static readonly string[] RequiredKeys = { "AnchorGainBrake", "AnchorConfirmByPeaks", "DetectedPeakKev" };

        /// <summary>Ключи плеч, которых нет на обычной сборке (сняты 01.11.2026).</summary>
        static List<string> MissingKeys()
        {
            var missing = new List<string>();
            foreach (string name in RequiredKeys)
            {
                if (typeof(FsaAnalyzer).GetProperty(name, BindingFlags.Instance | BindingFlags.Public) == null)
                {
                    missing.Add(name);
                }
            }

            return missing;
        }

        /// <summary>Ключ анализатора — отражением: на обычной сборке его нет, и это ОТКАЗ, а не «плечо 0».</summary>
        static void SetKey(FsaAnalyzer analyzer, string name, object value)
        {
            PropertyInfo p = typeof(FsaAnalyzer).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p == null)
            {
                throw new MissingMemberException(
                    "в этой сборке нет ключа FsaAnalyzer." + name
                    + " — плечи (А1)/(Б) сняты 01.11.2026; нужна временная сборка замера AMBER269");
            }

            p.SetValue(analyzer, value, null);
        }

        static string Indent(string text)
        {
            return "    " + text.Replace("\n", "\n    ");
        }

        static string F(double v)
        {
            return v.ToString("F6", CultureInfo.InvariantCulture);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides, out string deviceNote)
        {
            deviceNote = null;
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
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            deviceNote = ProbeDeviceConfig.Attach(rd);
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
    }
}
