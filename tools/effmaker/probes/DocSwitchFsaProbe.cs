using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`fixFsa2`, 07.11.2026) СМЕНА ДОКУМЕНТА ПРИ ОТКРЫТОМ ОКНЕ РЕЗУЛЬТАТА НЕ ПЕРЕСЧИТЫВАЕТ
    /// ГОТОВЫЙ РАЗБОР FSA. Проверка экраном 07.11.2026 (снимки 20–22 полосы `screen`): при
    /// открытой вкладке «Результат измерений» переход на документ с готовым разбором шёл
    /// пересчётом 1–2 мин; при открытой панели «Обнаружение пиков» — нет.
    ///
    /// Механизм: общий набор нуклидов (`NuclideDefinitionManager.ActiveSet`) входит в
    /// отпечаток разбора и ставится по документу панелью пиков (`ShowPeakDetectionResult`),
    /// а `MainForm.dockPanel1_ActiveDocumentChanged` звал окно результата раньше неё.
    ///
    /// Проба повторяет ПОРЯДОК ВЫЗОВОВ НАСТОЯЩЕГО обработчика: читает IL
    /// `MainForm.dockPanel1_ActiveDocumentChanged` собранного exe, берёт из него
    /// очерёдность `MainForm.ShowMeasurementResult` и `DCPeakDetectionView.ShowPeakDetectionResult`
    /// и зовёт эти НАСТОЯЩИЕ методы в ней (MainForm без конструктора — только поля
    /// `activeDocument`, `dcResultViewList`, `dcPeakDetectionView`; окон на экран нет).
    /// Затем «график и окно отчёта» спрашивают сеанс тем же `EnsureUpToDate`.
    ///
    /// Судится число запусков счёта (`FsaAnalysisSession.RunCount`):
    ///   S1 — документ с готовым разбором, вход тот же: ждём 0 при ЛЮБОМ порядке (`--order=old` —
    ///        порядок до правки `MainForm`: со вторым заходом 07.11.2026 сеанс берёт набор СВОЕГО
    ///        документа, `FsaAnalysisSession.NuclideSetSource`, и порядок вызовов больше ничего не решает);
    ///   S2 — положительный контроль: вход документа сменился (спектр): ждём ≥ 1;
    ///   S4 — `MainForm.RefreshAllView` (окно общих настроек) зовёт `PrepareViewData` у ВСЕХ
    ///        документов: фоновый B в режиме «Show FSA», активен A с другим набором — ждём 0 запусков
    ///        у B, и отпечаток сеанса B несёт ЕГО набор;
    ///   S5 — положительный контроль S4: спектр B изменился — ждём ≥ 1 запуск, и снова с набором B.
    /// На сборке до второго захода S1 при `--order=old` и S4 дают лишний счёт — это и есть контроль.
    /// Ключи: --a=&lt;спектр&gt; --b=&lt;спектр&gt; [--order=il|old|new] [--timeout=300].
    /// Коды: 0 — сошлось; 1 — расхождение; 2 — ключи/файлы; 3 — разбор не дошёл.
    /// </summary>
    static class DocSwitchFsaProbe
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int bad;
        static int timeoutS = 300;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string pathA = null, pathB = null, order = "il";
            foreach (string arg in args)
            {
                if (arg.StartsWith("--a=", StringComparison.Ordinal)) pathA = arg.Substring(4);
                else if (arg.StartsWith("--b=", StringComparison.Ordinal)) pathB = arg.Substring(4);
                else if (arg.StartsWith("--order=", StringComparison.Ordinal)) order = arg.Substring(8);
                else if (arg.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(arg.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + arg); return 2; }
            }
            if (pathA == null || pathB == null || !(order == "il" || order == "old" || order == "new"))
            {
                Console.Error.WriteLine("нужны --a=<спектр> --b=<спектр> [--order=il|old|new]");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            // порядок вызовов обработчика смены документа — из IL собранного exe
            List<string> calls = HandlerCalls();
            int iResult = calls.IndexOf("MainForm.ShowMeasurementResult");
            int iPeaks = calls.IndexOf("DCPeakDetectionView.ShowPeakDetectionResult");
            Console.WriteLine("IL dockPanel1_ActiveDocumentChanged: ShowMeasurementResult #{0}, ShowPeakDetectionResult #{1} (из {2} вызовов)",
                              iResult, iPeaks, calls.Count);
            if (iResult < 0 || iPeaks < 0) { Console.WriteLine("  ⛔ вызовы в IL не найдены"); return 2; }
            bool resultFirst = order == "old" || (order == "il" && iResult < iPeaks);
            Console.WriteLine("ПОРЯДОК ПРОБЫ ({0}): {1}", order, resultFirst ? "окно результата → панель пиков (как до правки)" : "панель пиков → окно результата");

            // MainForm без конструктора: его окна, порты и конфиг пробе не нужны
            var mainForm = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            var results = new DCResultView(mainForm);
            Field(typeof(MainForm), "dcResultViewList").SetValue(mainForm, new List<DCResultView> { results });
            var peaks = new DCPeakDetectionView(mainForm);
            Field(typeof(MainForm), "dcPeakDetectionView").SetValue(mainForm, peaks);
            // продолжения `await` панели пиков — на поток окон, как под Application.Run приложения;
            // без контекста они шли бы пулом и гонялись с таблицей (замер 07.11.2026: NRE в XPTable)
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            Console.WriteLine("контекст потока окон: {0}", SynchronizationContext.Current != null ? SynchronizationContext.Current.GetType().Name : "нет");
            Form host = Host(results, 640, 300);
            Form peakHost = Host(peaks, 640, 300);

            DocEnergySpectrum a = Open(pathA, nuclides);
            DocEnergySpectrum b = Open(pathB, nuclides);
            if (a == null || b == null) return 2;
            // график не заказчик: в пробе заказывают окно результата и «отчёт» (EnsureUpToDate ниже)
            a.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;
            b.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;

            // у документов РАЗНЫЕ наборы: A — вся библиотека, B — самый узкий сет с нуклидом B
            NuclideSet setB = NarrowestSetWith(nuclides, NuclideOf(b));
            a.SelectedNuclideSet = null;
            b.SelectedNuclideSet = setB;
            Console.WriteLine("наборы: A «все», B «{0}»", setB != null ? setB.Name : "НЕТ");
            if (setB == null) { Console.WriteLine("  ⛔ сет для B не найден — механизм не воспроизвести"); return 2; }

            // разбор обоих документов — каждый со своим набором, до покоя
            Activate(mainForm, b, resultFirst); Settle(b);
            Activate(mainForm, a, resultFirst); Settle(a);
            Activate(mainForm, b, resultFirst); Settle(b);
            Activate(mainForm, a, resultFirst); Settle(a);
            if (a.FsaSession.Result == null || b.FsaSession.Result == null)
            {
                Console.WriteLine("  ⛔ разбора нет: A «{0}», B «{1}»", a.FsaSession.Status, b.FsaSession.Status);
                return 3;
            }

            // S1: B уже разобран своим набором, вход тот же
            int before = b.FsaSession.RunCount, beforeA = a.FsaSession.RunCount;
            Activate(mainForm, b, resultFirst);
            Settle(b);
            int runs = b.FsaSession.RunCount - before;
            bool s1 = runs == 0;
            Console.WriteLine("S1 смена A→B, у B готовый разбор, вход тот же: запусков счёта B {0}, A {1} (ждём 0)  {2}",
                              runs, a.FsaSession.RunCount - beforeA, s1 ? "ok" : "НЕТ");
            if (!s1) bad++;

            // S2: вход B сменился — счёт обязан пойти
            Activate(mainForm, a, resultFirst); Settle(a);
            EnergySpectrum sb = b.ActiveResultData.EnergySpectrum;
            sb.Spectrum[sb.Spectrum.Length / 3] += 50;
            sb.TotalPulseCount += 50;
            sb.ValidPulseCount += 50;
            before = b.FsaSession.RunCount;
            Activate(mainForm, b, resultFirst);
            Settle(b);
            runs = b.FsaSession.RunCount - before;
            bool s2 = runs >= 1;
            Console.WriteLine("S2 смена A→B, спектр B изменился: запусков счёта B {0} (ждём ≥ 1)  {1}", runs, s2 ? "ok" : "НЕТ");
            if (!s2) bad++;

            // S4: фоновый документ в режиме «Show FSA», подготовка вида у ВСЕХ документов (RefreshAllView)
            // режим «Show FSA» ставится, пока B активен (его набор — и общий), затем активным
            // становится A; ничто, кроме подготовки вида ниже, сеанс B больше не спрашивает
            b.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
            Pump(300);
            WaitIdle(b.FsaSession);
            Activate(mainForm, a, resultFirst); Settle(a);
            before = b.FsaSession.RunCount;
            b.EnergySpectrumView.PrepareViewData();
            Pump(500);
            WaitIdle(b.FsaSession);
            runs = b.FsaSession.RunCount - before;
            bool ownSet = (b.FsaSession.Stamp ?? "").Contains("|" + setB.Name + ":");
            bool s4 = runs == 0 && ownSet;
            Console.WriteLine("S4 RefreshAllView: активен A («все»), фоновый B в «Show FSA»: запусков счёта B {0} (ждём 0), набор в отпечатке B — {1}  {2}",
                              runs, ownSet ? "свой («" + setB.Name + "»)" : "ЧУЖОЙ", s4 ? "ok" : "НЕТ");
            if (!s4) bad++;

            // S5: вход B сменился — фоновый документ считает, и своим набором
            sb.Spectrum[sb.Spectrum.Length / 3] += 50;
            sb.TotalPulseCount += 50;
            sb.ValidPulseCount += 50;
            before = b.FsaSession.RunCount;
            b.EnergySpectrumView.PrepareViewData();
            Pump(500);
            WaitIdle(b.FsaSession);
            runs = b.FsaSession.RunCount - before;
            ownSet = (b.FsaSession.Stamp ?? "").Contains("|" + setB.Name + ":");
            bool s5 = runs >= 1 && ownSet;
            Console.WriteLine("S5 RefreshAllView, спектр B изменился: запусков счёта B {0} (ждём ≥ 1), набор в отпечатке B — {1}  {2}",
                              runs, ownSet ? "свой" : "ЧУЖОЙ", s5 ? "ok" : "НЕТ");
            if (!s5) bad++;

            peakHost.Hide(); host.Hide();
            results.Dispose(); peaks.Dispose(); host.Dispose(); peakHost.Dispose();
            a.Dispose(); b.Dispose();
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: ВСЁ СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Часть обработчика смены документа, от которой зависит заказ FSA, — в порядке IL.</summary>
        static void Activate(MainForm mainForm, DocEnergySpectrum doc, bool resultFirst)
        {
            mainForm.ActiveDocument = doc;
            MethodInfo showResult = typeof(MainForm).GetMethod("ShowMeasurementResult", Any);
            DCPeakDetectionView peaks = (DCPeakDetectionView)Field(typeof(MainForm), "dcPeakDetectionView").GetValue(mainForm);
            if (resultFirst)
            {
                showResult.Invoke(mainForm, new object[] { true });
                peaks.ShowPeakDetectionResult();
            }
            else
            {
                peaks.ShowPeakDetectionResult();
                showResult.Invoke(mainForm, new object[] { true });
            }
            // дальше в обработчике — график и окно отчёта: оба зовут EnsureUpToDate своего документа
            ResultData rd = doc.ActiveResultData;
            doc.FsaSession.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null);
        }

        /// <summary>До покоя: поиск пиков панели, счёт сеанса, перечитывание окна.</summary>
        static void Settle(DocEnergySpectrum doc)
        {
            for (int round = 0; round < 3; round++)
            {
                Pump(400);
                WaitIdle(doc.FsaSession);
                Pump(300);
                ResultData rd = doc.ActiveResultData;
                // поиск пиков панели закончился — график/отчёт спрашивают снова (как по Completed)
                doc.FsaSession.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null);
                WaitIdle(doc.FsaSession);
            }
        }

        static List<string> HandlerCalls()
        {
            MethodInfo handler = typeof(MainForm).GetMethod("dockPanel1_ActiveDocumentChanged", Any);
            byte[] il = handler.GetMethodBody().GetILAsByteArray();
            var names = new List<string>();
            Module module = handler.Module;
            // однобайтовые и двухбайтовые коды операций с длиной операнда
            var oneByte = new Dictionary<short, OpCode>();
            var twoByte = new Dictionary<short, OpCode>();
            foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (OpCode)f.GetValue(null);
                if (op.Size == 1) oneByte[op.Value] = op; else twoByte[(short)(op.Value & 0xff)] = op;
            }
            int i = 0;
            while (i < il.Length)
            {
                OpCode op;
                if (il[i] == 0xFE) { op = twoByte[il[i + 1]]; i += 2; }
                else { op = oneByte[il[i]]; i += 1; }
                int operand;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: operand = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: operand = 1; break;
                    case OperandType.InlineVar: operand = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: operand = 8; break;
                    case OperandType.InlineSwitch: operand = 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: operand = 4; break;
                }
                if (op == OpCodes.Call || op == OpCodes.Callvirt)
                {
                    MethodBase m = module.ResolveMethod(BitConverter.ToInt32(il, i));
                    names.Add(m.DeclaringType.Name + "." + m.Name);
                }
                i += operand;
            }
            return names;
        }

        static string NuclideOf(DocEnergySpectrum doc)
        {
            List<Peak> peaks = doc.ActiveResultData.DetectedPeaks;
            Peak best = peaks != null ? peaks.Where(p => p != null && p.Nuclide != null).OrderByDescending(p => p.SNR).FirstOrDefault() : null;
            return best != null ? best.Nuclide.Name : null;
        }

        static NuclideSet NarrowestSetWith(NuclideDefinitionManager nuclides, string name)
        {
            if (name == null) return null;
            NuclideSet bestSet = null;
            int bestCount = int.MaxValue;
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                List<NuclideDefinition> members = nuclides.NuclideDefinitions.Where(d => d.Sets != null && d.Sets.Contains(set.Id)).ToList();
                if (members.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) && members.Count < bestCount)
                {
                    bestSet = set;
                    bestCount = members.Count;
                }
            }
            return bestSet;
        }

        // ---- обвязка (образец FsaResultWindowProbeP254) ----

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
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        static Form Host(Form content, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
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

        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path)) { Console.Error.WriteLine("нет файла: " + path); return null; }
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
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }
    }
}
