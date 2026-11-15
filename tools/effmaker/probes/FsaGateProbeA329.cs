using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER329`, полоса a329 15.11.2026) КНОПКА FSA ВЫКЛЮЧАЕТ РАЗБОР ПОЛНОСТЬЮ — ворота сеанса
    /// документа против трёх потребителей разбора: график (`EnergySpectrumView.UpdateFsaOverlay`),
    /// окно «Отчёт FSA» (`FSAReportView.Consume`) и окно «Результат измерений»
    /// (`DCResultView.FsaCollection`). Amber 15.11.2026, дословно: «Если кнопка FSA выключена, то
    /// вообще никакого разбора FSA быть не должно.» Вопросником того же дня: окна пишут «FSA
    /// выключен» с причиной; при кнопке ВКЛ и пиках ВЫКЛ — тоже нет разбора (правило показа
    /// `DocEnergySpectrum.IsFsaShownBy`).
    ///
    /// Настоящие окна в форме-носителе за краем экрана, документы из файлов корпуса, кнопки — их
    /// обработчиками (отражением); ворота включены признаком приложения
    /// (<see cref="DocEnergySpectrum.FsaGateEnforced"/>). Критерии:
    ///   В0 правило ворот: 4 сочетания «кнопка × пики» и признак ВЫКЛ (пробы) — ворота открыты;
    ///   В1 кнопка ВЫКЛ: обновление вида, оба окна, график в ShowFSA насильно — запусков счёта 0,
    ///      событий «счёт начат» 0, отпечатка нет, результата нет; отчёт — строка «FSA выключен
    ///      (кнопка)», строк таблицы 0; окно результата — заголовок тот же, строк 0;
    ///   В2 кнопка ВКЛ (её обработчиком, больше ничем): разбор пошёл и окна ожили сами — результат
    ///      есть, отчёт «расчёт завершён» и строки, окно результата со строками (положительный
    ///      контроль и «ВЫКЛ → ВКЛ оживляет окна»);
    ///   В3 пики ВЫКЛ (кнопкой пиков) при кнопке FSA ВКЛ: результат забыт, причина «пики» в обоих
    ///      окнах, строк 0; обновление вида и окна — запусков 0;
    ///   В4 пики ВКЛ: разбор вернулся сам, окна со строками;
    ///   В5 кнопка ВЫКЛ ПОСРЕДИ счёта (счёт стоит на задвижке проб): по окончании счёта результата
    ///      нет (отброшен поколением), окна — «FSA выключен», строк 0;
    ///   В6 тексты: обе причины есть в en и ru и различаются;
    ///   В7 ворота не трогают пробы: признак ВЫКЛ, кнопка ВЫКЛ — сеанс считает, как прежде.
    ///
    ///     fsagateprobea329 --spectrum=&lt;xml&gt; [--spoil=gate] [--timeout=300]
    ///
    /// `--spoil=gate` — положительный контроль судьи: у сеанса документа снят источник ворот (как в
    /// сборке до правки) — В1/В3/В5 обязаны отказать, код 1.
    /// Вывод — «  ok   …» / «  ⛔  …», последняя строка «ВСЕ СОШЛИСЬ» / «НЕ СОШЛОСЬ: N».
    /// Коды: 0 — все критерии; 1 — есть отказы; 2 — мерить нечем (ключи, файлы).
    /// </summary>
    static class FsaGateProbeA329
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int bad;
        static int good;
        static int timeoutS = 300;
        static bool spoil;
        static int started;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = arg.Substring(11);
                else if (arg == "--spoil=gate") spoil = true;
                else if (arg.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(arg.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + arg); return 2; }
            }
            if (spectrum == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<xml> [--spoil=gate] [--timeout=300]");
                return 2;
            }
            if (!File.Exists(spectrum))
            {
                Console.WriteLine("  ⛔ мерить нечем: нет файла спектра {0}", spectrum);
                return 2;
            }

            GlobalConfigManager manager = GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            string configPath = Path.Combine(Path.GetDirectoryName(typeof(DocEnergySpectrum).Assembly.Location),
                                             "config", "BecquerelMonitor.xml");
            DateTime configStamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            if (spoil)
            {
                Console.WriteLine("⚠ --spoil=gate: у сеанса документа снят источник ворот (сборка до правки) — контроль судьи");
            }

            // ApplicationSource не ставится: конфиг на диск не пишется (SetFsaModeEverywhere)
            ChartViewConfig chart = manager.GlobalConfig.ChartViewConfig;
            chart.FsaModeEnabled = false;
            chart.DefaultPeakMode = PeakMode.Visible;
            chart.DefaultBackgroundMode = BackgroundMode.Visible;
            DocEnergySpectrum.FsaGateEnforced = true;

            RuleSection();
            TextsSection();

            MainForm mainForm = new MainForm();
            DocEnergySpectrum a = Open(spectrum, nuclides);
            if (a == null) return 2;
            DocumentManager.GetInstance().DocumentList.Add(a);
            mainForm.ActiveDocument = a;
            if (spoil)
            {
                a.FsaSession.OffReasonSource = null;
            }
            a.FsaSession.Started += (s, e) => Interlocked.Increment(ref started);

            var report = new FSAReportView(mainForm);
            report.ProbeConsumer = true;
            Form reportHost = Host(report, 700, 420, -4000);
            report.SetDocument(a);
            var results = new DCResultView(mainForm);
            Form resultsHost = Host(results, 640, 400, -3000);
            results.ShowResult(true);
            Pump(300);

            string offButton = FsaAnalysisSession.OffText(FsaOffReason.Button);
            string offPeaks = FsaAnalysisSession.OffText(FsaOffReason.Peaks);

            // В1 — кнопка ВЫКЛ
            Console.WriteLine();
            Console.WriteLine("=== В1. кнопка FSA ВЫКЛ: разбора нет ни у одного потребителя ===");
            Same("причина ворот документа", FsaOffReason.Button, Reason(a));
            Same("график не в FSA", true, a.EnergySpectrumView.BackgroundMode != BackgroundMode.ShowFSA);
            int runs0 = a.FsaSession.RunCount;
            started = 0;
            a.RefreshView();                                     // такт вида — все подписчики
            results.ShowResult(true);
            report.RefreshReport();
            // график насильно в ShowFSA: его заказ тоже упирается в ворота сеанса
            BackgroundMode keep = a.EnergySpectrumView.BackgroundMode;
            a.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
            a.EnergySpectrumView.PrepareViewData();
            a.EnergySpectrumView.BackgroundMode = keep;
            Pump(1500);
            WaitIdle(a.FsaSession);
            Silent("кнопка ВЫКЛ", a, runs0, report, results, offButton);

            // В2 — кнопка ВКЛ своим обработчиком; окна оживают сами
            Console.WriteLine();
            Console.WriteLine("=== В2. кнопка FSA ВКЛ: разбор пошёл, окна ожили без переоткрытия ===");
            int runs1 = a.FsaSession.RunCount;
            Invoke(a, "toolStripSplitButtonFsa_ButtonClick", null, EventArgs.Empty);
            Same("галочка FSA ВКЛ", true, chart.FsaModeEnabled);
            Same("причина ворот", FsaOffReason.None, Reason(a));
            Alive("кнопка ВКЛ", a, runs1, report, results, offButton, offPeaks);

            // В3 — пики ВЫКЛ
            Console.WriteLine();
            Console.WriteLine("=== В3. пики ВЫКЛ при кнопке FSA ВКЛ: разбора нет, причина «пики» ===");
            Invoke(a, "toolStripSplitButton9_ButtonClick", null, EventArgs.Empty);
            Same("пики ВЫКЛ", PeakMode.Invisible, a.EnergySpectrumView.PeakMode);
            Same("галочка FSA цела", true, chart.FsaModeEnabled);
            Same("причина ворот", FsaOffReason.Peaks, Reason(a));
            Pump(500);
            WaitIdle(a.FsaSession);
            Same("результат забыт (память)", true, a.FsaSession.Result == null);
            int runs2 = a.FsaSession.RunCount;
            started = 0;
            a.RefreshView();
            results.ShowResult(true);
            report.RefreshReport();
            Pump(1500);
            WaitIdle(a.FsaSession);
            Silent("пики ВЫКЛ", a, runs2, report, results, offPeaks);

            // В4 — пики ВКЛ
            Console.WriteLine();
            Console.WriteLine("=== В4. пики ВКЛ: разбор вернулся сам ===");
            int runs3 = a.FsaSession.RunCount;
            Invoke(a, "toolStripSplitButton9_ButtonClick", null, EventArgs.Empty);
            Same("пики ВКЛ", PeakMode.Visible, a.EnergySpectrumView.PeakMode);
            Alive("пики ВКЛ", a, runs3, report, results, offButton, offPeaks);

            // В5 — кнопка ВЫКЛ посреди счёта
            Console.WriteLine();
            Console.WriteLine("=== В5. кнопка ВЫКЛ посреди счёта: результат отброшен ===");
            FieldInfo gateField = typeof(FsaAnalysisSession).GetField("probeGate", Any);
            if (gateField == null)
            {
                Console.WriteLine("  ⛔  обвязка: нет FsaAnalysisSession.probeGate");
                bad++;
            }
            else
            {
                var gate = new ManualResetEvent(false);
                gateField.SetValue(null, gate);
                a.FsaSession.Reset();
                a.RefreshView();                                  // окна заказывают новый счёт — он стоит
                Pump(300);
                bool running = a.FsaSession.IsRunning;
                Same("счёт идёт (стоит на задвижке)", true, running);
                Invoke(a, "toolStripSplitButtonFsa_ButtonClick", null, EventArgs.Empty);
                Same("галочка FSA ВЫКЛ", false, chart.FsaModeEnabled);
                gateField.SetValue(null, null);
                gate.Set();
                WaitIdle(a.FsaSession);
                Pump(800);
                Same("досчитанный результат не опубликован", true, a.FsaSession.Result == null);
                int runs4 = a.FsaSession.RunCount;
                started = 0;
                a.RefreshView();
                results.ShowResult(true);
                Pump(1000);
                WaitIdle(a.FsaSession);
                Silent("ВЫКЛ посреди счёта", a, runs4, report, results, offButton);
            }

            // В7 — пробы без ворот
            Console.WriteLine();
            Console.WriteLine("=== В7. признак ворот ВЫКЛ (пробы, корпус): кнопка ВЫКЛ разбору не мешает ===");
            DocEnergySpectrum.FsaGateEnforced = false;
            Same("причина ворот при признаке ВЫКЛ", FsaOffReason.None, Reason(a));
            int runs5 = a.FsaSession.RunCount;
            a.RefreshView();
            Pump(300);
            WaitIdle(a.FsaSession);
            Pump(300);
            Same("сеанс посчитал (запусков ≥ 1)", true, a.FsaSession.RunCount > runs5);
            Same("результат есть", true, a.FsaSession.Result != null);

            WaitIdle(a.FsaSession);
            reportHost.Hide();
            resultsHost.Hide();
            report.SetDocument(null);
            report.Dispose();
            results.Dispose();
            reportHost.Dispose();
            resultsHost.Dispose();
            DocumentManager.GetInstance().DocumentList.Remove(a);
            a.Dispose();
            mainForm.Dispose();
            Console.WriteLine();
            Same("проба конфиг на диске не писала (mtime рядом с exe)", configStamp,
                 File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue);
            Console.WriteLine();
            Console.WriteLine("сошлось {0}, отказов {1}", good, bad);
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
            Environment.Exit(bad == 0 ? 0 : 1);
            return bad == 0 ? 0 : 1;
        }

        // ---- В0, В6 ----

        static void RuleSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== В0. правило ворот: FSA выключен ⇔ не показан (кнопка × пики) ===");
            ChartViewConfig chart = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig;
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            var doc = new DocEnergySpectrum();
            foreach (bool flag in new[] { false, true })
            {
                foreach (PeakMode peaks in new[] { PeakMode.Invisible, PeakMode.Visible })
                {
                    chart.FsaModeEnabled = flag;
                    doc.EnergySpectrumView.PeakMode = peaks;
                    FsaOffReason expected = flag && peaks == PeakMode.Visible ? FsaOffReason.None
                        : flag ? FsaOffReason.Peaks : FsaOffReason.Button;
                    Same(string.Format("кнопка {0}, пики {1}", flag ? "ВКЛ" : "ВЫКЛ", peaks == PeakMode.Visible ? "ВКЛ" : "ВЫКЛ"),
                         expected, Reason(doc));
                }
            }
            DocEnergySpectrum.FsaGateEnforced = false;
            chart.FsaModeEnabled = false;
            Same("признак ворот ВЫКЛ (пробы): кнопка ВЫКЛ — ворота открыты", FsaOffReason.None, Reason(doc));
            DocEnergySpectrum.FsaGateEnforced = true;
            Same("сеанс без документа: ворота открыты", FsaOffReason.None, new FsaAnalysisSession().OffReason);
            chart.FsaModeEnabled = false;
            doc.Dispose();
        }

        static void TextsSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== В6. тексты причины в en и ru ===");
            ResourceManager rm = BecquerelMonitor.Properties.Resources.ResourceManager;
            CultureInfo en = CultureInfo.InvariantCulture, ru = CultureInfo.GetCultureInfo("ru");
            foreach (string key in new[] { "FSAOffByButton", "FSAOffByPeaks" })
            {
                string e = rm.GetString(key, en), r = rm.GetString(key, ru);
                Same(key + " en есть", true, !string.IsNullOrEmpty(e));
                Same(key + " ru есть и не en", true, !string.IsNullOrEmpty(r) && r != e);
                Console.WriteLine("       en «{0}» / ru «{1}»", e, r);
                Same(key + " — термин FSA в обоих", true, e != null && r != null && e.Contains("FSA") && r.Contains("FSA"));
            }
            Same("причины различаются", true, rm.GetString("FSAOffByButton", ru) != rm.GetString("FSAOffByPeaks", ru));
        }

        // ---- суды ----

        static void Silent(string what, DocEnergySpectrum a, int runsBefore, FSAReportView report, DCResultView results, string offText)
        {
            Same(what + ": запусков счёта", 0, a.FsaSession.RunCount - runsBefore);
            Same(what + ": событий «счёт начат»", 0, started);
            Same(what + ": результата нет", true, a.FsaSession.Result == null);
            Same(what + ": сеанс не занят", false, a.FsaSession.IsRunning);
            Same(what + ": отчёт — строка состояния", offText, ReportStatus(report));
            Same(what + ": отчёт — строк таблицы", 0, ReportRows(report));
            Same(what + ": окно результата — заголовок", offText, ResultHeader(results));
            Same(what + ": окно результата — строк", 0, ResultRows(results));
        }

        static void Alive(string what, DocEnergySpectrum a, int runsBefore, FSAReportView report, DCResultView results,
                          string offButton, string offPeaks)
        {
            // только ожидание: окна обязаны ожить сами, по событию ворот документа
            for (int i = 0; i < timeoutS * 10 && (a.FsaSession.IsRunning || a.FsaSession.Result == null || ResultRows(results) == 0
                                                   || ReportStatus(report) != BecquerelMonitor.Properties.Resources.FSAStatusCompleted); i++)
            {
                Pump(100);
            }
            Pump(300);
            Same(what + ": счёт запущен (запусков ≥ 1)", true, a.FsaSession.RunCount > runsBefore);
            Same(what + ": результат есть", true, a.FsaSession.Result != null);
            Same(what + ": отчёт — «расчёт завершён»", BecquerelMonitor.Properties.Resources.FSAStatusCompleted, ReportStatus(report));
            Same(what + ": отчёт — строки есть", true, ReportRows(report) > 0);
            string head = ResultHeader(results);
            Same(what + ": окно результата — заголовок не «выключен»", true, head != offButton && head != offPeaks);
            Same(what + ": окно результата — строки есть", true, ResultRows(results) > 0);
            Console.WriteLine("       заголовок окна результата «{0}», строк отчёта {1}, строк результата {2}",
                              head, ReportRows(report), ResultRows(results));
        }

        static FsaOffReason Reason(DocEnergySpectrum doc)
        {
            return doc.FsaSession.OffReason;
        }

        static string ReportStatus(FSAReportView report)
        {
            return ((Label)Field(report, "statusLabel")).Text;
        }

        static int ReportRows(FSAReportView report)
        {
            object model = Field(report, "tableModel");
            return ((IList)model.GetType().GetProperty("Rows").GetValue(model, null)).Count;
        }

        static string ResultHeader(DCResultView view)
        {
            object model = Field(view, "columnModel1");
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            return (string)columns[0].GetType().GetProperty("Text").GetValue(columns[0], null);
        }

        static int ResultRows(DCResultView view)
        {
            object model = Field(view, "tableModel1");
            return ((IList)model.GetType().GetProperty("Rows").GetValue(model, null)).Count;
        }

        // ---- обвязка (образцы FsaResultWindowProbeP254 / FsaToggleProbeA328) ----

        static void Same<T>(string what, T expected, T actual)
        {
            if (Equals(expected, actual))
            {
                good++;
                Console.WriteLine("  ok   {0}: {1}", what, actual);
            }
            else
            {
                bad++;
                Console.WriteLine("  ⛔  {0}: ждали {1}, вышло {2}", what, expected, actual);
            }
        }

        static void WaitIdle(FsaAnalysisSession session)
        {
            for (int i = 0; i < timeoutS * 20; i++)
            {
                if (!session.IsRunning)
                {
                    Application.DoEvents();
                    return;
                }
                Thread.Sleep(50);
                Application.DoEvents();
            }
            Console.WriteLine("  ⛔  сеанс не дошёл до покоя за {0} с", timeoutS);
            bad++;
        }

        static void Pump(int ms)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        static Form Host(Form content, int width, int height, int x)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(x, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            content.TopLevel = false;
            content.Dock = DockStyle.Fill;
            host.Controls.Add(content);
            content.Show();
            host.Show();
            return host;
        }

        static object Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any);
                if (f != null) return f.GetValue(target);
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        static object Invoke(object target, string name, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(name, Any);
            if (m == null) throw new MissingMethodException(target.GetType().Name, name);
            try
            {
                return m.Invoke(target, args);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(name + ": " + (ex.InnerException ?? ex), ex.InnerException ?? ex);
            }
        }

        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides)
        {
            ResultDataFile file;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
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
            // как `FsaResultWindowProbeP254.Prepare`: FWHM и пики — панели поиска пиков у пробы нет
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
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }
    }
}
