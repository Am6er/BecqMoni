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
    /// (`AMBER290`) ПРИЁМКА СТРОКИ ПРИВЯЗКИ: причина тормоза усиления (`AMBER269`, (А1))
    /// не теряется, когда привязка откатывается (`AMBER269`, (А2)).
    ///
    /// Прежде `AnalyzeRolledBack` возвращал разбор без привязки и ПЕРЕЗАПИСЫВАЛ
    /// `AnchorNote` строкой отката целиком: причина тормоза пропадала, и «опор 0»
    /// в строке читалось как «тормоз не срабатывал» — на этом критик `crit7`
    /// диагностировал `AMBER288` неверно. Теперь строка отката несёт ОБА факта:
    /// сперва «тормоз усиления … (почему)», затем «откат привязки … (по какому χ²)».
    ///
    /// Ключи:
    ///   --spectrum=&lt;файл&gt; (обязателен);
    ///   --brake-zero-max=&lt;кэВ&gt; — порог «у нуля» тормоза отражением (умолчание сборки — 30);
    ///   --mode=nucbase|peaks — состав библиотеки: NucBase+равновесие (рецепт `AMBER269`)
    ///     или по подписям поиска пиков (рецепт приложения, `DbLookupsForFsa=false`);
    ///   --no-width — откат НЕ оставляет ширину опор (`AnchorRollbackKeepsWidth=false`);
    ///   --matrix-any — поставить матрицу файла без проверки клейма (как `FsaAnchorVariantsA269`):
    ///     живой случай ТОРМОЗ + ОТКАТ при пороге умолчания, Th-232 на приборе вне корпуса, опора
    ///     16.28 кэВ; без этого ключа у того же спектра тормоз без отката, и снятие вызова
    ///     `WithBrakeNote` в `AnalyzeRolledBack` проба не заметила бы. Случай на корпусе (порог
    ///     поднят, спектр в git) — сцена (2б) `FsaInputCheckProbeP195`, её зовёт `check_all`;
    ///   --expect=brake+rollback|none — что обязано быть в строке; без ключа — только печать;
    ///   --out=&lt;файл&gt; — весь вывод ещё и в файл.
    ///
    /// Строка печатается как «ПРИВЯЗКА: …» — по ней же сверяется «строка не меняется»
    /// у положительного контроля (прогон той же пробы на сборке ДО правки).
    ///
    /// Коды: 0 — проверки сошлись; 1 — не сошлись; 2 — ключи/файл; 3 — разбор не завершился.
    /// </summary>
    static class FsaAnchorBrakeRollbackNoteA290
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, outPath = null, mode = "nucbase", expect = null;
            double brakeZeroMax = double.NaN;
            bool width = true, compose = false, matrixAny = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a.StartsWith("--expect=", StringComparison.Ordinal)) expect = a.Substring(9);
                else if (a.StartsWith("--brake-zero-max=", StringComparison.Ordinal))
                {
                    string raw = a.Substring(17);
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out brakeZeroMax))
                    {
                        Console.Error.WriteLine("порог тормоза не число: " + raw);
                        return 2;
                    }
                }
                else if (a == "--no-width") width = false;
                else if (a == "--matrix-any") matrixAny = true;
                else if (a == "--check-compose") compose = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrum == null || !File.Exists(spectrum))
            {
                Console.Error.WriteLine("нужен --spectrum=<файл.xml>");
                return 2;
            }

            if (mode != "nucbase" && mode != "peaks" && mode != "set")
            {
                Console.Error.WriteLine("режим не знаю: " + mode + " (nucbase|peaks|set)");
                return 2;
            }

            if (expect != null && expect != "brake+rollback" && expect != "brake" && expect != "none")
            {
                Console.Error.WriteLine("ожидание не знаю: " + expect + " (brake+rollback|brake|none)");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            var log = new StringBuilder();
            void Say(string line)
            {
                Console.WriteLine(line);
                log.AppendLine(line);
            }

            ResultData rd = Load(spectrum, nuclides, Say);
            if (rd == null)
            {
                return 2;
            }

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (mode == "nucbase")
            {
                options.DbLookups = true;
                options.FromSet = false;
                options.ChainEquilibrium = true;
            }
            else if (mode == "set")
            {
                options.DbLookups = false;
                options.FromSet = true;
                options.ChainEquilibrium = false;
            }
            else
            {
                options.DbLookups = false;
                options.FromSet = false;
                options.ChainEquilibrium = true;
            }

            Say("спектр: " + spectrum);
            Say("режим: " + mode + (mode == "nucbase" ? " + равновесие ряда"
                : (mode == "set" ? " (набор нуклидов)" : " (по подписям пиков)")));
            Say("порог тормоза: " + (double.IsNaN(brakeZeroMax) ? "умолчание сборки" : F(brakeZeroMax) + " кэВ"));
            Say("ширина отката: " + (width ? "оставлена" : "снята"));

            EfficiencyMaker.ResponseMatrix forced = null;
            if (matrixAny)
            {
                forced = rd.Efficiency != null ? LoadEmbedded(rd.Efficiency.EmbeddedMatrix) : null;
                Say("матрица файла: " + (forced != null ? "есть, поставлена без проверки клейма" : "НЕТ"));
                if (forced == null)
                {
                    Console.Error.WriteLine("--matrix-any: в файле нет встроенной матрицы");
                    return 2;
                }
            }

            string status;
            FsaResult result = Run(rd, options, brakeZeroMax, width, forced, out status);
            if (result == null)
            {
                Console.Error.WriteLine("разбор не завершился: " + (status ?? "нет результата"));
                return 3;
            }

            string note = result.AnchorNote ?? "";
            Say(string.Format(CultureInfo.InvariantCulture,
                "ИТОГ: χ²/ndf Пуассона {0:F2}; усиление {1:F6}; опор {2}; откат {3}",
                result.Chi2NdfPoisson, result.Gain, result.ScaleAnchorsUsed, result.AnchorRolledBack ? "ДА" : "нет"));
            if (result.AnchorRolledBack)
            {
                Say(string.Format(CultureInfo.InvariantCulture,
                    "  отменённый разбор: опор {0}, усиление {1:F6}, χ²/ndf {2:F2}",
                    result.AnchorRollbackAnchors, result.AnchorRollbackGain, result.AnchorRollbackChi2NdfPoisson));
            }

            Say("ПРИВЯЗКА: " + (note.Length > 0 ? note : "—"));

            bool brake = note.Contains("тормоз усиления");
            bool rollback = note.Contains("откат привязки");
            int atBrake = note.IndexOf("тормоз усиления", StringComparison.Ordinal);
            int atRollback = note.IndexOf("откат привязки", StringComparison.Ordinal);

            if (expect == "brake+rollback")
            {
                Same("строка называет тормоз", true, brake);
                Same("строка называет откат", true, rollback);
                Same("признак отката у результата", true, result.AnchorRolledBack);
                Same("тормоз назван ПЕРВЫМ (он причина)", true, atBrake >= 0 && atRollback > atBrake);
                Same("причина тормоза названа словами", true, note.Contains("плеча нет"));
                Same("χ² отката в строке", true, note.Contains("хуже, чем без неё"));
                // (`AMBER294`) причина доезжает до окна: опоры отменённого разбора не взяты
                Same("тормоз снял опоры отменённого разбора", true, result.AnchorBrakedPeaks > 0);
                Same("у отменённого разбора ни одна опора не взята", false,
                     result.AnchorRollbackScaleAnchors == null || result.AnchorRollbackScaleAnchors.Exists(x => x.Used));
                Same("причина опоры — тормоз", true, result.AnchorRollbackScaleAnchors != null
                     && result.AnchorRollbackScaleAnchors.Exists(x => x.Refusal == FsaScaleAnchor.RefusalGainBrake));
                string caption = FSAReportView.AnchorBrakeCaption(result);
                Say("ОКНО (тормоз): " + (caption ?? "—"));
                Say("ОКНО (откат): " + (FSAReportView.AnchorRollbackCaption(result) ?? "—"));
                Same("строка окна называет причину", true, caption != null);
            }
            else if (expect == "brake")
            {
                Same("строка называет тормоз", true, brake);
                Same("причина тормоза названа словами", true, note.Contains("плеча нет"));
                Same("тормоз назван до отката (если откат есть)", true, atRollback < 0 || atRollback > atBrake);
                // (`AMBER294`) итог на экране: опор нет, опора не взята, причина названа
                Same("тормоз снял опоры", true, result.AnchorBrakedPeaks > 0);
                Same("опор в итоге 0", 0, result.ScaleAnchorsUsed);
                Same("усиление в итоге 1", "1.000000", result.Gain.ToString("F6", CultureInfo.InvariantCulture));
                Same("ни одна опора не взята", false, result.ScaleAnchors != null && result.ScaleAnchors.Exists(x => x.Used));
                Say("ОКНО (тормоз): " + (FSAReportView.AnchorBrakeCaption(result) ?? "—"));
                Same("строка окна называет причину", true, FSAReportView.AnchorBrakeCaption(result) != null);
            }
            else if (expect == "none")
            {
                Same("тормоз НЕ назван (его не было)", false, brake);
                Same("строка отката не появилась", false, rollback);
                Same("тормоз опор не снимал", 0, result.AnchorBrakedPeaks);
                Same("строки окна про тормоз нет", (string)null, FSAReportView.AnchorBrakeCaption(result));
            }

            if (compose)
            {
                // (`AMBER290`) Сам перенос причины — на ЖИВОЙ строке этого прогона:
                // та же приватная функция, что зовёт `AnalyzeRolledBack`, отражением.
                MethodInfo brakeOf = typeof(FsaAnalyzer).GetMethod("BrakeNoteOf",
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo withBrake = typeof(FsaAnalyzer).GetMethod("WithBrakeNote",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Same("в сборке есть FsaAnalyzer.BrakeNoteOf", true, brakeOf != null);
                Same("в сборке есть FsaAnalyzer.WithBrakeNote", true, withBrake != null);
                if (brakeOf != null && withBrake != null)
                {
                    string extracted = (string)brakeOf.Invoke(null, new object[] { note });
                    if (brake)
                    {
                        Same("причина тормоза вынута из живой строки целиком", true,
                             extracted != null && extracted.StartsWith("тормоз усиления", StringComparison.Ordinal)
                             && extracted.Contains("плеча нет") && !extracted.Contains("откат привязки"));
                        string composed = (string)withBrake.Invoke(null, new object[] { extracted, "откат привязки (AMBER290): проверка" });
                        Same("строка отката несёт оба факта", true,
                             composed.Contains("тормоз усиления") && composed.Contains("откат привязки"));
                        Same("тормоз в строке отката первым", true,
                             composed.IndexOf("тормоз усиления", StringComparison.Ordinal)
                             < composed.IndexOf("откат привязки", StringComparison.Ordinal));
                        Console.WriteLine("  СЦЕПКА: " + composed);
                    }
                    else
                    {
                        Same("тормоза нет — причина пуста", (string)null, extracted);
                        Same("тормоза нет — строка отката прежняя до бита", "откат привязки (AMBER290): проверка",
                             (string)withBrake.Invoke(null, new object[] { extracted, "откат привязки (AMBER290): проверка" }));
                    }
                }
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, log.ToString(), new UTF8Encoding(false));
                Console.WriteLine("вывод: " + outPath);
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static FsaResult Run(ResultData rd, FsaCalculationOptions options, double brakeZeroMax, bool width,
                             EfficiencyMaker.ResponseMatrix forced, out string status)
        {
            status = null;
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            string hookError = null;
            FsaAnalysisSession.ProbeAnalyzerHook = an =>
            {
                try
                {
                    if (forced != null && rd.Efficiency != null && rd.Efficiency.Geometry != null)
                    {
                        FsaMatrixBinding.Bind(an, rd.Efficiency.Geometry, forced);
                    }

                    SetKey(an, "AnchorRollback", true);
                    SetKey(an, "AnchorRollbackKeepsWidth", width);
                    SetKey(an, "AnchorGainBrake", true);
                    // сеанс приложения снимает повтор (`FsaAnalysisSession`): проба повторяет
                    // его рецепт, иначе повтор подменяет отданный разбор другим
                    SetKey(an, "AnchorZeroGuardReplay", false);
                    if (!double.IsNaN(brakeZeroMax))
                    {
                        SetKey(an, "AnchorGainBrakeZeroMaxKev", brakeZeroMax);
                    }
                }
                catch (Exception ex)
                {
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
                    status = "ключи не поставились: " + hookError;
                    return null;
                }

                status = session.Status;
                return session.Result;
            }
            finally
            {
                FsaAnalysisSession.ProbeAnalyzerHook = null;
            }
        }

        static void SetKey(FsaAnalyzer analyzer, string name, object value)
        {
            PropertyInfo p = typeof(FsaAnalyzer).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p == null)
            {
                throw new MissingMemberException("в этой сборке нет ключа FsaAnalyzer." + name);
            }

            p.SetValue(analyzer, value, null);
        }

        static void Same<T>(string what, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "OK " : "⛔ ", what, expected, actual);
        }

        static string F(double v)
        {
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        /// <summary>Встроенная матрица файла без проверки клейма — как у `FsaAnchorVariantsA269` (`--matrix-any`).</summary>
        static EfficiencyMaker.ResponseMatrix LoadEmbedded(EfficiencyMaker.EmbeddedResponseMatrix embedded)
        {
            if (embedded == null || string.IsNullOrEmpty(embedded.Data))
            {
                return null;
            }

            byte[] packed = Convert.FromBase64String(embedded.Data);
            using (var input = new MemoryStream(packed))
            using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                using (var stream = new MemoryStream(output.ToArray(), false))
                {
                    EfficiencyMaker.MatrixRefusal read;
                    int format;
                    return EfficiencyMaker.ResponseMatrix.Load(stream, out read, out format);
                }
            }
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides, Action<string> say)
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
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            say("прибор: " + ProbeDeviceConfig.Attach(rd));
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

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            say("каналов: " + (s != null ? s.NumberOfChannels.ToString(CultureInfo.InvariantCulture) : "—")
                + "; пиков поиска: " + rd.DetectedPeaks.Count.ToString(CultureInfo.InvariantCulture));
            return rd;
        }
    }
}
