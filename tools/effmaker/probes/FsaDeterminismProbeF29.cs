using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace FsaDeterminismProbeF29
{
    /// <summary>
    /// (`A250`) ЛОКАТОР НЕДЕТЕРМИНИЗМА РАЗБОРА FSA — без окон и без таблицы.
    ///
    ///     FsaDeterminismProbeF29 --spectrum=&lt;файл&gt; [--runs=N] [--phase=fsa|peaks|both]
    ///                            [--source=nucbase|peaks]
    ///
    /// Разводит две половины пути, каждую своим плечом:
    ///
    ///   ФАЗА «fsa»   — пики найдены ОДИН раз, разбор гоняется N раз подряд.
    ///                  Расхождение здесь = недетерминизм самого разбора.
    ///   ФАЗА «peaks» — на каждом заходе заново ищутся пики, потом разбор.
    ///                  Расхождение только здесь = недетерминизм ПОИСКА ПИКОВ
    ///                  (состав FSA выводится из подписей пиков, `S57`).
    ///
    /// Печатается отпечаток (sha256) четырёх слоёв, от входа к выходу:
    ///   PEAKS — число пиков, их энергии и подписи;
    ///   LIB   — строка вывода состава (`FSA composition:` из Trace);
    ///           ⚠ пишется ТОЛЬКО веткой NucBase: на `--source=peaks`
    ///           этот слой меряет пустоту и всегда равен sha(«(нет)»);
    ///   COMP  — состав результата: имя, род, доля (формат R);
    ///   ROWS  — числа разбора: χ²/ndf, невязка, число пределов.
    /// Первый слой, у которого отпечатки разошлись, и есть место дефекта.
    /// </summary>
    static class Program
    {
        sealed class TraceCatcher : TraceListener
        {
            public readonly List<string> Lines = new List<string>();
            public override void Write(string message) { }
            public override void WriteLine(string message)
            {
                if (message != null && message.StartsWith("FSA composition:", StringComparison.Ordinal))
                {
                    this.Lines.Add(message);
                }
            }
        }

        static TraceCatcher catcher = new TraceCatcher();

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null;
            int runs = 10;
            string phase = "both";
            // Умолчание — то же, чем пользуется приёмка отчёта
            // (`FsaReportViewProbe.SetSource(doc, true)`): состав из NucBase с
            // равновесием. Ветка «по подписям пиков» строку вывода состава в
            // `Trace` не пишет вовсе, и слой LIB на ней меряет пустоту.
            bool nucBase = true;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--runs=", StringComparison.Ordinal)) runs = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--phase=", StringComparison.Ordinal)) phase = a.Substring(8);
                else if (a.StartsWith("--source=", StringComparison.Ordinal)) nucBase = a.Substring(9) == "nucbase";
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            Trace.Listeners.Add(catcher);

            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            DocEnergySpectrum doc = Open(spectrumPath, nuclides, true);
            if (doc == null) return 2;

            var source = doc.ActiveResultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (source != null)
            {
                source.DbLookupsForFsa = nucBase;
                source.ChainEquilibrium = true;
            }

            Console.WriteLine("SETUP	источник состава: {0}", nucBase ? "NucBase + равновесие" : "подписи пиков");

            int bad = 0;
            if (phase == "fsa" || phase == "both")
            {
                bad += Phase("ФАЗА fsa: пики ОДНИ, разбор гоняется заново", doc, nuclides, runs, false);
            }

            if (phase == "peaks" || phase == "both")
            {
                bad += Phase("ФАЗА peaks: поиск пиков + разбор заново", doc, nuclides, runs, true);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            doc.Dispose();
            return bad == 0 ? 0 : 1;
        }

        static int Phase(string title, DocEnergySpectrum doc, NuclideDefinitionManager nuclides,
                         int runs, bool redetect)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + title + " ===");
            var seen = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < runs; i++)
            {
                ResultData rd = doc.ActiveResultData;
                if (redetect)
                {
                    rd.DetectedPeaks = new PeakDetector().DetectPeak(
                        rd, BackgroundMode.Invisible, SmoothingMethod.None,
                        nuclides.ActiveSet, nuclides.NuclideDefinitions);
                }

                catcher.Lines.Clear();
                FsaAnalysisSession session = doc.FsaSession;
                session.Reset();
                session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null);
                if (!WaitIdle(session))
                {
                    Console.WriteLine("  ⛔ сеанс не дошёл до покоя");
                    return 1;
                }

                FsaResult r = session.Result;
                string peaks = PeakDigest(rd);
                string lib = catcher.Lines.Count > 0 ? catcher.Lines[catcher.Lines.Count - 1] : "(нет)";
                string comp = CompDigest(r);
                string rows = RowDigest(r);

                string key = Sha(peaks) + "/" + Sha(lib) + "/" + Sha(comp) + "/" + Sha(rows);
                Console.WriteLine("  RUN {0,2}  PEAKS {1}  LIB {2}  COMP {3}  ROWS {4}   слоёв {5}, пределов {6}",
                                  i + 1, Sha(peaks), Sha(lib), Sha(comp), Sha(rows),
                                  r != null && r.Components != null ? r.Components.Count : -1,
                                  r != null && r.CharacteristicLimits != null ? r.CharacteristicLimits.Count : -1);
                List<string> bucket;
                if (!seen.TryGetValue(key, out bucket))
                {
                    bucket = new List<string> { peaks, lib, comp, rows };
                    seen[key] = bucket;
                }
            }

            Console.WriteLine("  РАЗЛИЧНЫХ отпечатков: {0} из {1} прогонов", seen.Count, runs);
            if (seen.Count > 1)
            {
                // Назвать ПЕРВЫЙ разошедшийся слой и показать разницу.
                var keys = new List<string>(seen.Keys);
                string[] names = { "PEAKS", "LIB", "COMP", "ROWS" };
                for (int layer = 0; layer < 4; layer++)
                {
                    var vals = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string k in keys) vals.Add(seen[k][layer]);
                    Console.WriteLine("    слой {0}: различных {1}", names[layer], vals.Count);
                    if (vals.Count > 1)
                    {
                        int n = 0;
                        foreach (string v in vals)
                        {
                            Console.WriteLine("      --- вариант {0} ---", ++n);
                            foreach (string line in v.Split('\n'))
                            {
                                Console.WriteLine("      " + line);
                            }

                            if (n >= 3) break;
                        }

                        break;
                    }
                }

                return 1;
            }

            return 0;
        }

        static string PeakDigest(ResultData rd)
        {
            var sb = new StringBuilder();
            List<Peak> peaks = rd.DetectedPeaks;
            sb.Append("пиков ").Append(peaks == null ? -1 : peaks.Count).Append('\n');
            if (peaks != null)
            {
                foreach (Peak p in peaks)
                {
                    sb.Append(p.Energy.ToString("R", CultureInfo.InvariantCulture))
                      .Append(' ').Append(p.Channel.ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append(p.Nuclide != null ? p.Nuclide.Name : "-")
                      .Append('\n');
                }
            }

            return sb.ToString();
        }

        static string CompDigest(FsaResult r)
        {
            if (r == null || r.Components == null) return "(нет результата)";
            var sb = new StringBuilder();
            sb.Append("компонентов ").Append(r.Components.Count).Append('\n');
            foreach (FsaComponentResult c in r.Components)
            {
                sb.Append(c.Name).Append('|').Append(c.Kind)
                  .Append('|').Append(c.SharePercent.ToString("R", CultureInfo.InvariantCulture))
                  .Append('|').Append(c.CountRate.ToString("R", CultureInfo.InvariantCulture))
                  .Append('|').Append(c.Z.ToString("R", CultureInfo.InvariantCulture))
                  .Append('\n');
            }

            if (r.CharacteristicLimits != null)
            {
                sb.Append("пределов ").Append(r.CharacteristicLimits.Count).Append('\n');
                foreach (FsaCharacteristicLimit l in r.CharacteristicLimits)
                {
                    sb.Append(l.Name).Append('|').Append(l.Kind).Append('|').Append(l.Detected ? "+" : "-")
                      .Append('|').Append(l.CountRate.ToString("R", CultureInfo.InvariantCulture))
                      .Append('\n');
                }
            }

            return sb.ToString();
        }

        static string RowDigest(FsaResult r)
        {
            if (r == null) return "(нет результата)";
            var sb = new StringBuilder();
            sb.Append("chi2ndf ").Append(r.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("chi2poisson ").Append(r.Chi2NdfPoisson.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("resid+ ").Append(r.ResidualExcessShare.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("resid- ").Append(r.ResidualMissingShare.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("suppressor ").Append(r.SuppressorName ?? "-").Append('\n');
            return sb.ToString();
        }

        static string Sha(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static bool WaitIdle(FsaAnalysisSession session)
        {
            for (int i = 0; i < 600; i++)
            {
                if (!session.IsRunning)
                {
                    Application.DoEvents();
                    return true;
                }

                Thread.Sleep(50);
                Application.DoEvents();
            }

            return false;
        }

        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides, bool detect)
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

            if (detect)
            {
                rd.DetectedPeaks = new PeakDetector().DetectPeak(
                    rd, BackgroundMode.Invisible, SmoothingMethod.None,
                    nuclides.ActiveSet, nuclides.NuclideDefinitions);
            }

            if (rd.MeasurementController == null)
            {
                rd.MeasurementController = new MeasurementController(null, rd);
            }

            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}, кривая {3}", Path.GetFileName(path),
                              rd.DetectedPeaks != null ? rd.DetectedPeaks.Count : -1,
                              rd.EnergySpectrum.NumberOfChannels,
                              rd.Efficiency != null ? rd.Efficiency.Name : "(нет)");
            return doc;
        }
    }
}
