using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П261, 14.10.2026) СОСТОЯНИЯ СЕАНСА FSA В ОКНАХ — приёмка двух находок проверки
    /// интерфейса экраном (П259, журнал `handover/handover-2026-10-14-p259-ui-sweep.md`, пп. 6 и 7).
    ///
    ///  1. ОТЧЁТ FSA ПРИ ПЕРЕКЛЮЧЕНИИ ВКЛАДКИ (`activation`). Порядок — тот же, что у
    ///     <c>MainForm.dockPanel1_ActiveDocumentChanged</c>: окно результата
    ///     (<c>ShowMeasurementResult(true)</c>), НАСТОЯЩИЙ поиск пиков
    ///     (<see cref="DCPeakDetectionView.ShowPeakDetectionResult"/>, асинхронный, как в
    ///     приложении), окно отчёта (<see cref="FSAReportView.SetDocument"/>). Документ открыт,
    ///     как его открывает приложение: найденные пики в файл не пишутся (`[XmlIgnore]`), и до
    ///     первого поиска их у спектра НЕТ. Проба снимает состояние окон каждые 20 мс и судит:
    ///     ни в одном снимке отчёт не показывает строку отказа, если разбор кончается
    ///     нормальным результатом; окно результата не показывает отказа вовсе.
    ///  2. ОКНО РЕЗУЛЬТАТА ПРИ ОТКАЗЕ (`refusal`, `failure`). Разбор отказал — окно обязано
    ///     показать причину словами сеанса (как отчёт), а не вечное «FSA: computing…». Пути:
    ///     отказ анализатора (нет геометрии), ноль пиков у спектра (поиск честно не нашёл —
    ///     отказ НАСТОЯЩИЙ и обязан дойти до обоих окон, не застряв в «идёт расчёт»),
    ///     исключение в фоновом счёте, исключение в подготовке снимка (и при прежнем
    ///     результате — его строки не выдаются за свежие), сброс посреди счёта (отмена),
    ///     смена документа посреди счёта на отказывающий.
    ///
    /// Положительный контроль: сцены с НАСТОЯЩИМ отказом обязаны показать отказ и в отчёте,
    /// и в окне — без этого «отказа не видно» значило бы «проба ничего не видит».
    ///
    /// Сцена `busy` воспроизводит находку 6 ДЕТЕРМИНИРОВАННО: поиск пиков прежней вкладки ещё не
    /// вернулся на поток окон (поток занят), поиск новой встаёт в очередь (`refreshPending`), и
    /// первый проход сеанса без пиков кончается раньше поиска. На коде до П261 — отказ
    /// `FSALibraryEmptied` в таблице отчёта ~200 снимков подряд под «FSA in progress»; сцены
    /// `activation` ловят ту же гонку только под нагрузкой (зависит от скорости поиска).
    /// `refusal` — ещё «отказ вылечен, идёт новый счёт»: под «идёт расчёт» нет причины прежнего отказа.
    ///
    /// Сцена `requeue` (П271, 14.10.2026; находка ревизии П270) — порядок `busy`, но повторный поиск
    /// новой вкладки кончается СРАЗУ, без await (`DCPeakDetectionView`, нет калибровки ПШПВ). Последней
    /// отметкой конца поиска тогда оказывалась отметка очереди с `searched = false`, сеанс выбрасывал
    /// отложенный счёт молча, `Completed` не приходил — окно результата стояло на «FSA: computing…».
    /// Калибровка снимается ПОСЛЕ первого прохода разбора (он уже отложен до конца поиска): спектр
    /// без калибровки с самого начала сеанс отказывает за миллисекунды, обычно раньше, чем спектр
    /// встанет в очередь поиска, — отказ публикуется сразу, и порядок не воспроизводится (замер П271).
    /// Суд: сеанс успокоился (разбор ИЛИ причина), и окно результата показывает его, а не «идёт расчёт».
    ///
    /// Ключи: --spectrum=&lt;файл&gt; (несколько; сцены активации — по каждому, первый — для
    /// путей отказа, второй — прежняя вкладка сцены `busy`); --ui=en|ru; --timeout=300;
    /// --only=activation,busy,requeue,refusal,failure.
    /// Коды: 0 — всё сошлось; 1 — расхождение; 2 — ключи/файлы.
    /// </summary>
    static class FsaSessionStatesProbeP261
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int bad;
        static int timeoutS = 300;
        static string computingText;
        static HashSet<string> only;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--ui=", StringComparison.Ordinal)) Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(a.Substring(5));
                else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = new HashSet<string>(a.Substring(7).Split(','));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectra.Count == 0)
            {
                Console.Error.WriteLine("нужен хотя бы один --spectrum=<файл>");
                return 2;
            }
            foreach (string s in spectra)
            {
                if (!File.Exists(s)) { Console.Error.WriteLine("нет файла: " + s); return 2; }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            // настройки разбора — ОБЩИЕ, как у приложения (`Program.Main`, `AMBER212`)
            FsaCalculationOptions.ApplicationSource = () => GlobalConfigManager.GetInstance().GlobalConfig.FsaCalculation.ToOptions();
            Console.WriteLine("настройки разбора приложения: {0}", FsaCalculationOptions.Of(null).Stamp);
            computingText = new System.ComponentModel.ComponentResourceManager(typeof(DCResultView)).GetString("DCResult_FsaComputing");

            Application.EnableVisualStyles();
            MainForm mainForm = new MainForm();
            var peaks = new DCPeakDetectionView(mainForm);
            Field(typeof(MainForm), "dcPeakDetectionView").SetValue(mainForm, peaks);
            var report = new FSAReportView(mainForm);
            var results = new DCResultView(mainForm);
            Form hostPeaks = Host(peaks, 500, 300, -4000);
            Form hostReport = Host(report, 520, 600, -3400);
            Form hostResults = Host(results, 640, 300, -2800);

            if (Wanted("activation"))
            {
                foreach (string path in spectra)
                {
                    ActivationScene(path, "нормальная сцена", mainForm, peaks, report, results, null, true, null);
                }
            }
            if (Wanted("busy") && spectra.Count > 1)
            {
                // п. 6 детерминированно: поиск пиков ПРЕЖНЕЙ вкладки ещё не отработал (поток окон
                // занят), и поиск новой встаёт в очередь (`refreshPending`) — первый проход сеанса
                // идёт без пиков и кончается раньше поиска
                ActivationScene(spectra[0], "поиск пиков прежней вкладки ещё идёт", mainForm, peaks, report, results,
                                null, true, spectra[1]);
            }
            if (Wanted("requeue") && spectra.Count > 1)
            {
                // (П271) то же, что `busy`, но повтор поиска кончается без await — калибровки ПШПВ нет
                // к его началу (снята после первого прохода разбора, уже отложенного до конца поиска)
                ActivationScene(spectra[0], "поиск прежней вкладки идёт, повтор поиска новой кончается без await (нет калибровки ПШПВ)",
                                mainForm, peaks, report, results, null, null, spectra[1], rd => { rd.FwhmCalibration = null; });
            }
            if (Wanted("refusal"))
            {
                // положительный контроль и п. 7: настоящие отказы обязаны дойти до обоих окон
                ActivationScene(spectra[0], "ноль пиков (SNR 9999): отказ НАСТОЯЩИЙ", mainForm, peaks, report, results,
                                rd => ((FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig).Min_SNR = 9999, false, null);
                ActivationScene(spectra[0], "без кривой и прибора (как «Normalize»): отказ НАСТОЯЩИЙ", mainForm, peaks, report, results,
                                rd => { rd.Efficiency = null; }, false, null);
                RecoveryScene(spectra[0], mainForm, report, results);
            }
            if (Wanted("failure"))
            {
                FailurePaths(spectra[0], mainForm, report, results);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: ВСЁ СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad);
            Console.Out.Flush();
            int code = bad == 0 ? 0 : 1;
            // ⚠ Окна панелей (DockContent) ставят хуки Windows; не снятые до выхода, они звали
            // уже остановленную среду и роняли процесс кодом 0xC000041D (c0020001) ПОСЛЕ итога —
            // код возврата пробы обязан быть её итогом, поэтому окна снимаются явно (как у П254).
            hostPeaks.Hide(); hostReport.Hide(); hostResults.Hide();
            report.Dispose(); results.Dispose(); peaks.Dispose();
            hostPeaks.Dispose(); hostReport.Dispose(); hostResults.Dispose();
            mainForm.Dispose();
            Application.DoEvents();
            return code;
        }

        static bool Wanted(string section)
        {
            return only == null || only.Contains(section);
        }

        // ==================================================================
        // 1. Переключение вкладки: порядок MainForm, настоящий поиск пиков
        // ==================================================================

        static void ActivationScene(string path, string what, MainForm mainForm, DCPeakDetectionView peaks,
                                    FSAReportView report, DCResultView results, Action<ResultData> tweak, bool? expectResult,
                                    string busyPath, Action<ResultData> afterFirstPass = null)
        {
            Console.WriteLine();
            Console.WriteLine("=== АКТИВАЦИЯ: {0} — {1} ===", Path.GetFileName(path), what);
            DocEnergySpectrum previous = null;
            if (busyPath != null)
            {
                // прежняя вкладка: её поиск пиков запущен и ещё не вернулся на поток окон
                previous = Open(busyPath);
                mainForm.ActiveDocument = previous;
                peaks.ShowPeakDetectionResult();
            }
            DocEnergySpectrum doc = Open(path);
            if (doc == null) { bad++; return; }
            ResultData rd = doc.ActiveResultData;
            if (tweak != null) tweak(rd);
            Console.WriteLine("  пиков у спектра до поиска: {0} (файл их не хранит — как у приложения)", rd.DetectedPeaks.Count);

            // порядок `dockPanel1_ActiveDocumentChanged`
            mainForm.ActiveDocument = doc;
            results.ShowResult(true);
            peaks.ShowPeakDetectionResult();
            report.SetDocument(doc);
            if (busyPath != null)
            {
                // поток окон ЗАНЯТ (раскладка, отрисовка), пока первый проход сеанса не кончится:
                // очередь сообщений стоит, фоновый счёт идёт
                bool queued = (bool)Field(typeof(DCPeakDetectionView), "refreshPending").GetValue(peaks);
                FieldInfo running = typeof(FsaAnalysisSession).GetField("running", Any);
                DateTime blockStart = DateTime.UtcNow;
                while ((bool)running.GetValue(doc.FsaSession) && (DateTime.UtcNow - blockStart).TotalSeconds < 120)
                {
                    Thread.Sleep(20);
                }
                Thread.Sleep(300);
                Console.WriteLine("  поиск новой вкладки встал в очередь: {0}; поток окон занят {1:F1} с, первый проход — без пиков",
                                  queued, (DateTime.UtcNow - blockStart).TotalSeconds);
                if (afterFirstPass != null)
                {
                    // (П271) первый проход кончился и отложен до конца поиска; поиск новой вкладки
                    // ещё стоит в очереди — правка входа ДО него
                    bool withheld = typeof(FsaAnalysisSession).GetField("withheldJob", Any).GetValue(doc.FsaSession) != null;
                    afterFirstPass(rd);
                    Console.WriteLine("  первый проход отложен до конца поиска: {0}; вход поиска изменён до повтора", withheld);
                }
            }

            var reportRefusals = new List<string>();
            var resultTexts = new List<string>();
            int samples = 0, refusalSamples = 0, runningRefusalSamples = 0;
            DateTime start = DateTime.UtcNow, stableSince = DateTime.UtcNow;
            string last = null;
            while (true)
            {
                Pump(20);
                samples++;
                FsaAnalysisSession session = doc.FsaSession;
                List<string> statusRows = ReportStatusRows(report);
                string label = ReportLabel(report);
                List<string[]> rows = ResultRows(results);
                foreach (string s in statusRows)
                {
                    if (!reportRefusals.Contains(s)) reportRefusals.Add(s);
                }
                if (statusRows.Count > 0)
                {
                    refusalSamples++;
                    if (session.IsRunning) runningRefusalSamples++;
                }
                foreach (string[] r in rows)
                {
                    if (r.Length > 1 && !IsNumberCell(r[1]) && !resultTexts.Contains(r[1])) resultTexts.Add(r[1]);
                }
                bool searching = (bool)Field(typeof(DCPeakDetectionView), "isProcessing").GetValue(peaks)
                                 || (bool)Field(typeof(DCPeakDetectionView), "refreshPending").GetValue(peaks);
                string state = (searching ? "S" : "-") + (session.IsRunning ? "R" : "-") + (session.Result != null ? "r" : "-")
                               + "|" + label + "|" + string.Join(";", statusRows) + "|" + string.Join(";", rows.Select(r => string.Join(",", r)));
                if (state != last) { last = state; stableSince = DateTime.UtcNow; }
                bool settled = !searching && !session.IsRunning && (session.Result != null || !string.IsNullOrEmpty(session.Status));
                if (settled && (DateTime.UtcNow - stableSince).TotalMilliseconds > 1500) break;
                if ((DateTime.UtcNow - start).TotalSeconds > timeoutS)
                {
                    Console.WriteLine("  ⛔ не успокоилось за {0} с: {1}", timeoutS, state);
                    bad++;
                    break;
                }
            }

            FsaAnalysisSession final = doc.FsaSession;
            double seconds = (DateTime.UtcNow - start).TotalSeconds;
            List<string[]> finalRows = ResultRows(results);
            List<string> finalStatus = ReportStatusRows(report);
            Console.WriteLine("  {0:F1} с, снимков {1}, запусков счёта {2}, пиков после поиска {3}", seconds, samples,
                              final.RunCount, rd.DetectedPeaks.Count);
            Console.WriteLine("  итог сеанса: {0}", final.Result != null ? "разбор есть" : "отказ — " + final.Status);
            Console.WriteLine("  отчёт, строка состояния: «{0}»", ReportLabel(report));
            Console.WriteLine("  отчёт, строки отказа за всё время: {0} (в снимках {1}, из них при идущем счёте {2})",
                              reportRefusals.Count == 0 ? "нет" : string.Join(" | ", reportRefusals.Select(Short)),
                              refusalSamples, runningRefusalSamples);
            Console.WriteLine("  окно результата, нечисловые ячейки за всё время: {0}",
                              resultTexts.Count == 0 ? "нет" : string.Join(" | ", resultTexts.Select(Short)));
            Console.WriteLine("  окно результата в конце: заголовок «{0}», строк {1}: {2}", ResultHeader(results), finalRows.Count,
                              string.Join(" | ", finalRows.Take(4).Select(r => Short(string.Join(" ", r)))));

            if (expectResult == null)
            {
                // (П271) исход любой — разбор или причина, — но окно обязано его ПОКАЗАТЬ
                string reason = final.Status ?? "";
                bool okSession = !final.IsRunning && (final.Result != null || reason.Length > 0);
                bool okWindow = finalRows.Count > 0 && finalRows.All(r => r.Length > 1 && r[1] != computingText);
                Console.WriteLine("  сеанс успокоился (разбор или причина): {0}; окно результата показывает исход, а не «{1}»: {2}",
                                  Ok(okSession), computingText, Ok(okWindow));
                if (!okSession || !okWindow) bad++;
            }
            else if (expectResult == true)
            {
                bool okSession = final.Result != null;
                // строки рода «состояние», которых нет в итоговой таблице, — чужие (прежнего прохода)
                List<string> foreign = reportRefusals.Where(t => !finalStatus.Contains(t)).ToList();
                bool okReport = foreign.Count == 0;
                if (finalStatus.Count > 0)
                {
                    Console.WriteLine("  (строки рода «состояние» в итоговой таблице: {0})", string.Join(" | ", finalStatus.Select(Short)));
                }
                bool okWindow = resultTexts.All(t => t == computingText);
                Console.WriteLine("  разбор нормальный: {0}; отчёт без чужого отказа: {1}; окно без отказа: {2}",
                                  Ok(okSession), Ok(okReport), Ok(okWindow));
                if (!okSession || !okReport || !okWindow) bad++;
            }
            else
            {
                string reason = final.Status ?? "";
                bool okSession = final.Result == null && reason.Length > 0 && !final.IsRunning;
                bool okReport = finalStatus.Count == 1 && finalStatus[0] == reason;
                bool okWindow = finalRows.Count == 1 && finalRows[0].Length > 1 && finalRows[0][1] == reason;
                Console.WriteLine("  отказ дошёл: сеанс {0}; отчёт показывает причину {1}; окно результата показывает причину (не «{2}») {3}",
                                  Ok(okSession), Ok(okReport), computingText, Ok(okWindow));
                if (!okSession || !okReport || !okWindow) bad++;
            }

            report.SetDocument(null);
            mainForm.ActiveDocument = null;
            results.ShowResult(true);
            Pump(50);
            doc.Dispose();
            if (previous != null) previous.Dispose();
        }

        /// <summary>
        /// Отказ настоящий (нет кривой), человек его лечит (кривая вернулась) — идёт новый счёт.
        /// Под жёлтым «идёт расчёт» таблица отчёта не должна держать причину ПРЕЖНЕГО отказа:
        /// это не числа прежнего разбора, а отказ, которого у идущего счёта нет.
        /// </summary>
        static void RecoveryScene(string path, MainForm mainForm, FSAReportView report, DCResultView results)
        {
            Console.WriteLine();
            Console.WriteLine("=== ОТКАЗ ВЫЛЕЧЕН, ИДЁТ НОВЫЙ СЧЁТ: {0} ===", Path.GetFileName(path));
            DocEnergySpectrum doc = Open(path);
            Detect(doc);
            ResultData rd = doc.ActiveResultData;
            EfficiencyConfigData curve = rd.Efficiency;
            rd.Efficiency = null;
            mainForm.ActiveDocument = doc;
            results.ShowResult(true);
            report.SetDocument(doc);
            WaitIdle(doc.FsaSession);
            Pump(300);
            List<string> before = ReportStatusRows(report);
            string refusal = doc.FsaSession.Status ?? "";
            Console.WriteLine("  отказ в отчёте (контроль): {0}  {1}", before.Count == 1 ? Short(before[0]) : "нет",
                              Ok(before.Count == 1 && before[0] == refusal));
            if (!(before.Count == 1 && before[0] == refusal)) bad++;

            rd.Efficiency = curve;
            doc.RefreshView();                       // так приходит любая правка: вид документа обновлён
            int running = 0, stale = 0;
            DateTime start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < timeoutS)
            {
                Pump(20);
                if (!doc.FsaSession.IsRunning) break;
                running++;
                if (ReportStatusRows(report).Contains(refusal)) stale++;
            }
            WaitIdle(doc.FsaSession);
            Pump(300);
            bool okResult = doc.FsaSession.Result != null;
            Console.WriteLine("  новый счёт: снимков при идущем счёте {0}, из них с причиной прежнего отказа {1}; итог — {2}  {3}",
                              running, stale, okResult ? "разбор есть" : "отказ " + doc.FsaSession.Status,
                              Ok(running > 0 && stale == 0 && okResult));
            if (!(running > 0 && stale == 0 && okResult)) bad++;

            report.SetDocument(null);
            mainForm.ActiveDocument = null;
            results.ShowResult(true);
            Pump(50);
            doc.Dispose();
        }

        // ==================================================================
        // 2. Окно результата на путях отказа сеанса
        // ==================================================================

        static void FailurePaths(string path, MainForm mainForm, FSAReportView report, DCResultView results)
        {
            Console.WriteLine();
            Console.WriteLine("=== ОКНО РЕЗУЛЬТАТА НА ПУТЯХ ОТКАЗА: {0} ===", Path.GetFileName(path));
            report.SetDocument(null);
            FieldInfo gateField = typeof(FsaAnalysisSession).GetField("probeGate", Any);

            // 2а. исключение в ФОНОВОМ счёте: задвижка — уже закрытая ручка, WaitOne бросает
            DocEnergySpectrum a = Open(path);
            Detect(a);
            mainForm.ActiveDocument = a;
            var dead = new ManualResetEvent(false);
            dead.Dispose();
            gateField.SetValue(null, dead);
            results.ShowResult(true);
            WaitIdle(a.FsaSession);
            gateField.SetValue(null, null);
            Pump(300);
            JudgeShown("исключение в фоновом счёте", a.FsaSession, results);

            // 2б. исключение в ПОДГОТОВКЕ снимка (свежий сеанс, результата не было)
            a.FsaSession.Reset();
            FsaAnalysisSession.ProbeAnalyzerHook = x => { throw new InvalidOperationException("проба П261: отказ подготовки снимка"); };
            results.ShowResult(true);
            Pump(300);
            FsaAnalysisSession.ProbeAnalyzerHook = null;
            JudgeShown("исключение в подготовке снимка, результата не было", a.FsaSession, results);

            // 2в. то же при ПРЕЖНЕМ результате: строки прежнего спектра не выдаются за свежие
            a.FsaSession.Reset();
            results.ShowResult(true);
            WaitIdle(a.FsaSession);
            Pump(300);
            results.ShowResult(true);
            bool hadResult = a.FsaSession.Result != null;
            ResultData rdA = a.ActiveResultData;
            rdA.EnergySpectrum.Spectrum[100] += 1;           // отпечаток сменился
            rdA.EnergySpectrum.TotalPulseCount += 1;
            FsaAnalysisSession.ProbeAnalyzerHook = x => { throw new InvalidOperationException("проба П261: отказ подготовки снимка"); };
            results.ShowResult(true);
            Pump(300);
            FsaAnalysisSession.ProbeAnalyzerHook = null;
            Console.WriteLine("  (до отказа разбор был: {0})", hadResult);
            JudgeShown("исключение в подготовке снимка при прежнем результате", a.FsaSession, results);
            rdA.EnergySpectrum.Spectrum[100] -= 1;
            rdA.EnergySpectrum.TotalPulseCount -= 1;

            // 2г. сброс посреди счёта (отмена): окно не застревает в «идёт расчёт»
            var gate = new ManualResetEvent(false);
            gateField.SetValue(null, gate);
            a.FsaSession.Reset();
            results.ShowResult(true);
            Pump(200);
            bool wasRunning = a.FsaSession.IsRunning;
            a.FsaSession.Reset();
            gateField.SetValue(null, null);
            gate.Set();
            WaitIdle(a.FsaSession);
            Pump(500);
            WaitIdle(a.FsaSession);
            Pump(500);
            List<string[]> rows = ResultRows(results);
            bool notStuck = a.FsaSession.Result != null && rows.Count > 0 && rows.All(r => r.Length > 1 && r[1] != computingText);
            Console.WriteLine("  сброс посреди счёта (шёл: {0}): итог — {1}, строк окна {2}, первая «{3}»  {4}", wasRunning,
                              a.FsaSession.Result != null ? "разбор есть" : "отказ " + a.FsaSession.Status, rows.Count,
                              rows.Count > 0 ? Short(string.Join(" ", rows[0])) : "—", Ok(notStuck));
            if (!notStuck) bad++;

            // 2д. смена документа посреди счёта на ОТКАЗЫВАЮЩИЙ: окно — отказ нового, не строки прежнего
            DocEnergySpectrum b = Open(path);
            Detect(b);
            b.ActiveResultData.Efficiency = null;
            gate = new ManualResetEvent(false);
            gateField.SetValue(null, gate);
            a.FsaSession.Reset();
            mainForm.ActiveDocument = a;
            results.ShowResult(true);
            Pump(200);
            bool aRunning = a.FsaSession.IsRunning;
            mainForm.ActiveDocument = b;
            results.ShowResult(true);
            Pump(200);
            gateField.SetValue(null, null);
            gate.Set();
            WaitIdle(a.FsaSession);
            WaitIdle(b.FsaSession);
            Pump(500);
            Console.WriteLine("  (A считал при смене: {0})", aRunning);
            JudgeShown("смена документа посреди счёта на отказывающий", b.FsaSession, results);

            // 2е. окно СКРЫТО при отказе и открыто потом
            results.Visible = false;
            Pump(100);
            b.FsaSession.Reset();
            b.FsaSession.EnsureUpToDate(b.ActiveResultData, b.ActiveResultData.BackgroundEnergySpectrum != null);
            WaitIdle(b.FsaSession);
            results.Visible = true;
            Pump(300);
            JudgeShown("отказ при скрытом окне, окно открыто потом", b.FsaSession, results);

            mainForm.ActiveDocument = null;
            results.ShowResult(true);
            Pump(50);
            a.Dispose();
            b.Dispose();
        }

        static void JudgeShown(string what, FsaAnalysisSession session, DCResultView results)
        {
            List<string[]> rows = ResultRows(results);
            string reason = session.Status ?? "";
            bool refused = session.Result == null && reason.Length > 0 && !session.IsRunning;
            bool shown = rows.Count == 1 && rows[0].Length > 1 && rows[0][1] == reason;
            Console.WriteLine("  {0}: сеанс — {1}; окно: заголовок «{2}», ячейка «{3}»  {4}", what,
                              refused ? "отказ «" + Short(reason) + "»" : (session.Result != null ? "разбор есть (ждали отказ)" : "без причины"),
                              ResultHeader(results), rows.Count > 0 && rows[0].Length > 1 ? Short(rows[0][1]) : "—",
                              Ok(refused && shown));
            if (!(refused && shown)) bad++;
        }

        // ---- чтение окон ----

        static List<string> ReportStatusRows(FSAReportView report)
        {
            var texts = new List<string>();
            object table = Field(typeof(FSAReportView), "tableModel").GetValue(report);
            foreach (object row in (IList)table.GetType().GetProperty("Rows").GetValue(table, null))
            {
                FsaReportRow model = row.GetType().GetProperty("Tag").GetValue(row, null) as FsaReportRow;
                if (model != null && model.Kind == FsaReportRowKind.Status)
                {
                    texts.Add(model.Name);
                }
            }
            return texts;
        }

        static string ReportLabel(FSAReportView report)
        {
            Label label = (Label)Field(typeof(FSAReportView), "statusLabel").GetValue(report);
            return label != null ? label.Text : "";
        }

        static List<string[]> ResultRows(DCResultView view)
        {
            object table = Field(typeof(DCResultView), "tableModel1").GetValue(view);
            var rows = new List<string[]>();
            foreach (object row in (IList)table.GetType().GetProperty("Rows").GetValue(table, null))
            {
                IList cells = (IList)row.GetType().GetProperty("Cells").GetValue(row, null);
                var texts = new string[cells.Count];
                for (int i = 0; i < cells.Count; i++)
                {
                    texts[i] = (string)cells[i].GetType().GetProperty("Text").GetValue(cells[i], null) ?? "";
                }
                rows.Add(texts);
            }
            return rows;
        }

        static string ResultHeader(DCResultView view)
        {
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            return (string)columns[0].GetType().GetProperty("Text").GetValue(columns[0], null);
        }

        static bool IsNumberCell(string text)
        {
            double v;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        static string Short(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            return text.Length > 110 ? text.Substring(0, 107) + "…" : text;
        }

        static string Ok(bool ok)
        {
            return ok ? "ok" : "НЕТ";
        }

        // ---- обвязка (образцы FsaResultWindowProbeP254) ----

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
            Console.WriteLine("  ⛔ сеанс не дошёл до покоя за {0} с", timeoutS);
            bad++;
        }

        static void Pump(int ms)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(5);
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

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Any);
                if (field != null) return field;
            }
            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static void Detect(DocEnergySpectrum doc)
        {
            ResultData rd = doc.ActiveResultData;
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Visible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
        }

        /// <summary>
        /// Документ — как его открывает приложение: прибор по ссылке, его настройки поиска,
        /// кривая ПШПВ; найденных пиков НЕТ (`[XmlIgnore]`, ищутся при активации).
        /// </summary>
        static DocEnergySpectrum Open(string path)
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
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            doc.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;   // график не заказчик
            return doc;
        }
    }
}
