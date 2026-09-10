using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace FsaReportViewProbe
{
    /// <summary>
    /// ПРИЁМКА ОКНА ОТЧЁТА РАЗЛОЖЕНИЯ `FSAReportView` (`A145`, этап 3) — без
    /// окна приложения: вид создаётся, документы собираются из файлов
    /// корпуса, снимки берутся `DrawToBitmap` с формы-носителя.
    ///
    ///     fsareportviewprobe --spectrum=&lt;спектр с рядом Th-232&gt; --control=&lt;спектр без ряда&gt;
    ///                        [--out=&lt;каталог снимков&gt;]
    ///
    /// Разделы — по критериям документа `handover/a145-fsa-display-groups.md`:
    ///
    ///   1. ГИГИЕНА (критерий 1): у `EnergySpectrumView` нет ни одного метода
    ///      прежней ручной таблицы, класса `FsaOverlay` в сборке нет, ресурса
    ///      `FSARowsDidNotFit` нет; строка состояния `DrawFsaStatus` есть.
    ///   2. ОКНО (критерий 2): создаётся, persist-string ходит кругом через
    ///      `MainForm.GetContentFromPersistString`, переживает hide/show,
    ///      отслеживает активный документ (строки — от сеанса того документа).
    ///   3. ВХОД В `ShowFSA` (критерий 3): явная команда документа и цикл
    ///      кнопки поднимают событие с верным признаком; закрытие отчёта не
    ///      меняет режим графика и не сбрасывает результат.
    ///   4. ОДИН РЕЗУЛЬТАТ (критерий 4): график и таблица держат ОДИН объект
    ///      `FsaResult`; два потребителя дают один запуск (`RunCount`).
    ///   0. ВИД ЧИСЕЛ (`A244`): у чисел блока качества НЕТ разделителя
    ///      разрядов ни на одной культуре. Судится ВИД строки, а не равенство
    ///      плеч: запятая в группах одинакова на всех культурах, и приёмка по
    ///      равенству её уже пропустила однажды.
    ///   5. СЕМЬ РОДОВ СТРОК (критерий 5) и таблица без потерь по высоте.
    ///      ⛔ Судится СОСТАВ строк по `Tag.Kind`, а не их ЧИСЛО (`A249`):
    ///      после `A247` строки модели ложатся в таблицу не одна в одну.
    ///   6. БЛОК «КАЧЕСТВО РАЗБОРА» (критерий 6, переписан под `A247`): черта,
    ///      заголовок, χ²/ndf своей строкой, СРАЗУ ПОД НИМ множитель `σ×`
    ///      (`A281`, решение Amber 10.09.2026) и по строке на каждую пометку —
    ///      в `ru-RU` и `en-US`, с полными подписями и без многоточия.
    ///   7. ГРУППИРОВКА И ФЛАГИ (критерий 7): родители/дочерние не меняют
    ///      отпечаток и не запускают счёт; расчётный флаг — ровно один запуск
    ///      и одно событие, отпечаток другой, конфигурация спектра и
    ///      умолчание прибора записаны.
    ///   8. РОДИТЕЛИ (критерий 8): недоступны без NucBase+равновесия с
    ///      подсказкой; при допустимом режиме сумма родительских лент и долей
    ///      равна сумме дочерних с машинным допуском.
    ///  11. СОСЕДНИЙ ДОКУМЕНТ (критерий 11): флаг, переключённый в одном
    ///      документе, не меняет копию конфигурации другого открытого.
    ///  12. СНИМКИ (критерий 12): `en` и `ru` в `--out`; по модели — ни одна
    ///      подпись не обрезана и не наложена, шрифт формы у всех, заголовок
    ///      таблицы тем же шрифтом.
    ///
    /// Состояния таблицы (документ, «Состояния таблицы») — отдельным разделом.
    /// ⛔ У каждого раздела, где это возможно, ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;
        static string outDir = ".";

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, controlPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--control=", StringComparison.Ordinal)) controlPath = a.Substring(10);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null || controlPath == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл с рядом Th-232> и --control=<файл без ряда>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            // (`A244`) Разделу нужны только статические построители: он
            // идёт ДО окна и до документов, чтобы вид чисел был измерен
            // даже там, где спектр не открылся.
            NumberLookSection();

            MainForm mainForm = new MainForm();
            DocEnergySpectrum thorium = Open(spectrumPath, nuclides);
            DocEnergySpectrum control = Open(controlPath, nuclides);
            if (thorium == null || control == null)
            {
                return 2;
            }

            // ⛔ ГЕЙТ ГЕОМЕТРИИ (`A277`, 10.09.2026) — «МЕРИТЬ НЕЧЕМ», А НЕ
            // ПАДЕНИЕ. У спектра без геометрии разбор не идёт вовсе, результат
            // сеанса пуст, и раздел 4 звал `GetFsaPresentation(null)`, то есть
            // проба УМИРАЛА `NullReferenceException` посреди прогона, не
            // напечатав приговора. Внешне это неотличимо от поломки окна.
            //
            // Тот же порядок, каким `T256` (полоса П8) развела коды у
            // `FsaGateRescueProbe`: 1 — «нарушено», 2 — «мерить нечем».
            // ⚠ Гейт судится по КАЖДОМУ из двух входов: разделы 5, 6 и 8 берут
            // и `--control`.
            foreach (var pair in new[] { new object[] { "--spectrum", thorium, spectrumPath },
                                         new object[] { "--control", control, controlPath } })
            {
                var doc = (DocEnergySpectrum)pair[1];
                doc.FsaSession.Reset();
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
                using (var probeReport = new FSAReportView(mainForm))
                {
                    probeReport.ProbeConsumer = true;
                    probeReport.SetDocument(doc);
                    WaitIdle(doc.FsaSession);
                    probeReport.SetDocument(null);
                }

                if (doc.FsaSession.Result == null)
                {
                    Console.Error.WriteLine("⛔ МЕРИТЬ НЕЧЕМ: у {0}={1} разбора нет ({2}). Гейт `A277`"
                                            + " отказывает спектру без геометрии ДО единого расчёта,"
                                            + " и приёмке окна отчёта нужен вход, у которого"
                                            + " геометрия ЕСТЬ.",
                                            pair[0], pair[2], doc.FsaSession.Status ?? "причина не названа");

                    // ⛔ ФОРМЫ УБИРАЮТСЯ И НА ЭТОМ ВЫХОДЕ. Без этого процесс
                    // печатает «МЕРИТЬ НЕЧЕМ» и падает следом кодом 0xC000041D
                    // на разборе окон — то есть отказ, названный словами,
                    // снаружи снова неотличим от поломки. Та же грабля, о
                    // которой предупреждает хвост `Main`.
                    thorium.Dispose();
                    control.Dispose();
                    mainForm.Dispose();
                    return 2;
                }
            }

            Hygiene();
            WindowSection(mainForm, thorium, control);
            EntrySection(mainForm, control);      // у контроля есть кривая: дверь FSA открыта без вопросов
            OneResultSection(mainForm, thorium);
            ReportWithoutFsaModeSection(mainForm, thorium);
            RowKindsSection(mainForm, thorium, control);
            StatesSection(mainForm, thorium);
            QualitySection(mainForm, thorium);
            GroupingAndFlagsSection(mainForm, thorium);
            ParentsSection(mainForm, thorium, control);
            NeighbourSection(mainForm, thorium, control);
            RepeatabilitySection(mainForm, thorium, control);
            SnapshotSection(mainForm, thorium);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);

            // ⛔ Формы убираются явно (см. FsaFlagsProbe: иначе процесс падает
            // ПОСЛЕ «ВСЕ СОШЛИСЬ» на разборе окон).
            thorium.Dispose();
            control.Dispose();
            mainForm.Dispose();
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. ГИГИЕНА
        // ------------------------------------------------------------------

        static void Hygiene()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. гигиена: ручной таблицы на графике нет, FsaOverlay нет ===");
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "DrawFsaOwnTable", "DrawFsaRows", "DrawFsaSwatch", "DrawFsaQualityRow",
                                            "FsaTableBudget", "FsaTableRowCount", "FsaCollapsibleBudget",
                                            "FsaQualityMarksWidth", "FsaQualityMarksFormat" })
            {
                Same("у EnergySpectrumView нет " + name, true, typeof(EnergySpectrumView).GetMethod(name, any) == null);
            }

            Same("контроль: строка состояния DrawFsaStatus ЕСТЬ", true,
                 typeof(EnergySpectrumView).GetMethod("DrawFsaStatus", any) != null);
            Same("контроль: ShowFsaTable (имя, которое зовёт чужой файл) ЕСТЬ", true,
                 typeof(EnergySpectrumView).GetMethod("ShowFsaTable", any) != null);
            Same("класса FsaOverlay в сборке нет", true,
                 typeof(FsaAnalysisSession).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaOverlay") == null);
            Same("контроль: класс FsaAnalysisSession есть", true,
                 typeof(FsaAnalysisSession).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaAnalysisSession") != null);
            Same("ресурса FSARowsDidNotFit нет", true,
                 BecquerelMonitor.Properties.Resources.ResourceManager.GetString("FSARowsDidNotFit") == null);
            Same("контроль: ресурс FSACalculating есть", true,
                 BecquerelMonitor.Properties.Resources.ResourceManager.GetString("FSACalculating") != null);
            Same("у Properties.Resources нет свойства FSARowsDidNotFit", true,
                 typeof(BecquerelMonitor.Properties.Resources).GetProperty("FSARowsDidNotFit", any) == null);
        }

        // ------------------------------------------------------------------
        // 2. ОКНО
        // ------------------------------------------------------------------

        static void WindowSection(MainForm mainForm, DocEnergySpectrum a, DocEnergySpectrum b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. окно: создание, persist-string, hide/show, активный документ ===");
            using (var report = new FSAReportView(mainForm))
            {
                string persist = (string)typeof(FSAReportView).GetMethod("GetPersistString",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(report, null);
                Console.WriteLine("  persist-string: {0}", persist);
                Same("persist-string — имя типа", typeof(FSAReportView).ToString(), persist);

                MethodInfo restore = typeof(MainForm).GetMethod("GetContentFromPersistString",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object restored = restore.Invoke(mainForm, new object[] { persist });
                Same("MainForm восстанавливает окно из persist-string", true, restored is FSAReportView);
                object restoredAgain = restore.Invoke(mainForm, new object[] { persist });
                Same("и второй раз отдаёт ТО ЖЕ окно, а не новое", true, ReferenceEquals(restored, restoredAgain));
                Same("контроль: чужая строка окна не даёт", true,
                     !(restore.Invoke(mainForm, new object[] { "Nothing.Of.The.Kind" }) is FSAReportView));

                // Активный документ: строки — от сеанса ЭТОГО документа.
                report.ProbeConsumer = true;
                report.SetDocument(a);
                WaitIdle(a.FsaSession);
                report.RefreshReport();
                int rowsA = report.ReportTable.TableModel.Rows.Count;
                FsaResult resultA = report.Presentation != null ? report.Presentation.Source : null;
                Same("документ A: результат у окна — результат сеанса A", true,
                     resultA != null && ReferenceEquals(resultA, a.FsaSession.Result));

                report.SetDocument(b);
                WaitIdle(b.FsaSession);
                report.RefreshReport();
                int rowsB = report.ReportTable.TableModel.Rows.Count;
                FsaResult resultB = report.Presentation != null ? report.Presentation.Source : null;
                Console.WriteLine("  строк: A {0}, B {1}", rowsA, rowsB);
                Same("документ B: результат у окна — результат сеанса B", true,
                     resultB != null && ReferenceEquals(resultB, b.FsaSession.Result));
                Same("окно показывает B, а не A", false, ReferenceEquals(resultB, resultA));

                report.SetDocument(null);
                Same("без документа — одна строка", 1, report.ReportTable.TableModel.Rows.Count);
                Same("и это «спектр не выбран»", BecquerelMonitor.Properties.Resources.FSAReportNoSpectrum,
                     report.ReportTable.TableModel.Rows[0].Cells[1].Text);
                Same("без документа элементы управления выключены", false,
                     Control<RadioButton>(report, "sourceNucBaseRadio").Enabled
                     || Control<CheckBox>(report, "pileUpCheckBox").Enabled);

                // hide/show внутри формы-носителя: окно живёт, подписка жива.
                // (Скрытие через DockPanel — `Close()` при `HideOnClose` — меряется
                // в разделе 3 на настоящем `MainForm.dockPanel1`.)
                using (Form host = Host(report, 320, 640))
                {
                    report.SetDocument(a);
                    report.Visible = false;
                    Same("скрыто", false, report.Visible);
                    report.Visible = true;
                    Same("показано снова", true, report.Visible);
                    report.RefreshReport();
                    Same("после hide/show строки на месте", rowsA, report.ReportTable.TableModel.Rows.Count);
                    host.Hide();
                }
            }
        }

        // ------------------------------------------------------------------
        // 3. ВХОД В ShowFSA
        // ------------------------------------------------------------------

        static void EntrySection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. вход в ShowFSA: явный и циклический; закрытие отчёта режим не меняет ===");
            var seen = new List<bool>();
            EventHandler<FsaModeEnteredEventArgs> handler = (s, e) => seen.Add(e.Explicit);
            doc.FsaModeEntered += handler;
            try
            {
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.Invisible;
                Invoke(doc, "ShowFsaToolStripMenuItem_Click", null, EventArgs.Empty);
                Same("явная команда: режим графика ShowFSA", BackgroundMode.ShowFSA, doc.EnergySpectrumView.BackgroundMode);
                Same("явная команда: событие одно", 1, seen.Count);
                Same("явная команда: признак Explicit = true", true, seen.Count == 1 && seen[0]);

                // Цикл: из ShowContinuum кнопка ведёт в ShowFSA (кривая и ПШПВ есть).
                seen.Clear();
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowContinuum;
                Invoke(doc, "toolStripSplitButton7_ButtonClick", null, EventArgs.Empty);
                Same("цикл: режим графика ShowFSA", BackgroundMode.ShowFSA, doc.EnergySpectrumView.BackgroundMode);
                Same("цикл: событие одно", 1, seen.Count);
                Same("цикл: признак Explicit = false", false, seen.Count == 1 && seen[0]);

                // Контроль: цикл дальше (из ShowFSA) события не поднимает.
                seen.Clear();
                Invoke(doc, "toolStripSplitButton7_ButtonClick", null, EventArgs.Empty);
                Same("контроль: выход из ShowFSA событие не поднимает", 0, seen.Count);
                Same("контроль: и режим уже не ShowFSA", true, doc.EnergySpectrumView.BackgroundMode != BackgroundMode.ShowFSA);
            }
            finally
            {
                doc.FsaModeEntered -= handler;
            }

            // MainForm подписан на событие документа и показывает окно отчёта.
            Invoke(mainForm, "SubscribeDocumentEvent", doc);
            try
            {
                mainForm.ActiveDocument = doc;
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.Invisible;
                Invoke(doc, "ShowFsaToolStripMenuItem_Click", null, EventArgs.Empty);
                FSAReportView shown = mainForm.FsaReportView;
                Same("MainForm: после явной команды окно отчёта создано", true, shown != null && !shown.IsDisposed);
                Same("MainForm: окно отчёта видно (в DockPanel)", true,
                     shown != null && shown.DockPanel != null && !shown.IsHidden);

                // Закрытие отчёта — скрытие: режим графика и результат не трогаются.
                WaitIdle(doc.FsaSession);
                FsaResult before = doc.FsaSession.Result;
                // Крестик вкладки зовёт `DockPane.CloseActiveContent()` — там и
                // живёт `HideOnClose` (скрыть, а не закрыть); `Form.Close()` и
                // `DockHandler.Close()` панель не спрашивают и окно уничтожают.
                Same("окно отчёта — активное содержимое своей панели", true,
                     shown.Pane != null && ReferenceEquals(shown.Pane.ActiveContent, shown));
                shown.Pane.CloseActiveContent();
                Console.WriteLine("  после крестика вкладки: IsDisposed={0} IsHidden={1} Visible={2} DockState={3}",
                                  shown.IsDisposed, shown.IsHidden, shown.Visible, shown.DockState);
                Same("закрытие отчёта: окно скрыто, а не уничтожено", true, !shown.IsDisposed && shown.IsHidden);
                Same("закрытие отчёта: режим графика остался ShowFSA", BackgroundMode.ShowFSA, doc.EnergySpectrumView.BackgroundMode);
                Same("закрытие отчёта: результат сеанса не сброшен", true,
                     before != null && ReferenceEquals(before, doc.FsaSession.Result));

                // Выход графика из ShowFSA не закрывает открытый отчёт.
                mainForm.ShowFsaReportView(true);
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.Invisible;
                Same("выход графика из ShowFSA не прячет отчёт", false, shown.IsHidden);
                if (shown.Pane != null && ReferenceEquals(shown.Pane.ActiveContent, shown))
                {
                    shown.Pane.CloseActiveContent();
                }
            }
            finally
            {
                Invoke(mainForm, "UnsubscribeDocumentEvent", doc);
                mainForm.ActiveDocument = null;
            }
        }

        // ------------------------------------------------------------------
        // 4. ОДИН РЕЗУЛЬТАТ
        // ------------------------------------------------------------------

        static void OneResultSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. один результат на график и таблицу, второго анализа нет ===");
            FsaAnalysisSession session = doc.FsaSession;
            session.Reset();
            int runsBefore = session.RunCount;
            using (var report = new FSAReportView(mainForm))
            {
                report.ProbeConsumer = true;
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
                report.SetDocument(doc);            // потребитель 1 — окно
                doc.RefreshView();                  // потребитель 2 — график (PrepareViewData → EnsureUpToDate)
                WaitIdle(session);
                report.RefreshReport();
                doc.RefreshView();
                Console.WriteLine("  запусков сеанса: {0}", session.RunCount - runsBefore);
                Same("два потребителя — один запуск", 1, session.RunCount - runsBefore);

                FsaResult result = session.Result;
                Same("результат есть", true, result != null);
                Same("таблица построена из результата сеанса", true,
                     report.Presentation != null && ReferenceEquals(report.Presentation.Source, result));
                object viewPresentation = Invoke(doc.EnergySpectrumView, "GetFsaPresentation", result);
                Same("график построен из ТОГО ЖЕ объекта результата", true,
                     viewPresentation is FsaPresentation && ReferenceEquals(((FsaPresentation)viewPresentation).Source, result));
                Same("группировка графика = группировка окна", report.EffectiveGrouping,
                     ((FsaPresentation)viewPresentation).Grouping == FsaGrouping.Parents ? FsaGrouping.Parents : ((FsaPresentation)viewPresentation).RequestedGrouping);
                Same("отпечаток сеанса — от активного спектра", session.Stamp,
                     FsaAnalysisSession.BuildStamp(doc.ActiveResultData, doc.ActiveResultData.BackgroundEnergySpectrum != null));

                // Контроль: счётчик живой — обесцененный кэш даёт второй запуск.
                session.Invalidate();
                report.RefreshReport();
                Invoke(report, "Consume");
                WaitIdle(session);
                Same("контроль: после Invalidate — ещё один запуск", 2, session.RunCount - runsBefore);
                report.SetDocument(null);
            }
        }

        // ------------------------------------------------------------------
        // 4б. ОТЧЁТ СЧИТАЕТ БЕЗ `ShowFSA` (`A295`)
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ ЭТОГО ПЛЕЧА НЕ БЫЛО, И ИМЕННО ПОЭТОМУ ДЕФЕКТ ДОЖИЛ ДО ЖАЛОБЫ.
        /// Все прежние разделы включали `BackgroundMode.ShowFSA` ПЕРЕД тем,
        /// как проверять окно отчёта, — то есть мерили случай, где разбор
        /// заказывает график. Сценарий «отчёт открыт, график в обычном режиме,
        /// идёт запись спектра» не мерился вовсе.
        ///
        /// Здесь он и мерится: режим графика НЕ `ShowFSA`, данные меняются
        /// (правится сам спектр, как при наборе), и окно обязано заказать
        /// новый счёт САМО — тактом обновления вида.
        /// </summary>
        static void ReportWithoutFsaModeSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4б. отчёт считает БЕЗ ShowFSA (A295) ===");
            FsaAnalysisSession session = doc.FsaSession;
            using (var report = new FSAReportView(mainForm))
            {
                report.ProbeConsumer = true;
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.Invisible;
                report.SetDocument(doc);
                WaitIdle(session);

                Same("контроль: режим графика НЕ ShowFSA", true,
                     doc.EnergySpectrumView.BackgroundMode != BackgroundMode.ShowFSA);

                int before = session.RunCount;

                // Данные сменились — ровно то, что делает набор спектра.
                EnergySpectrum spectrum = doc.ActiveResultData.EnergySpectrum;
                spectrum.MeasurementTime += 1.0;

                // Такт обновления вида: им приходят новые отсчёты.
                doc.RefreshView();
                WaitIdle(session);

                Same("новые данные без ShowFSA: счёт ЗАКАЗАН", true,
                     session.RunCount > before);
                Same("и отпечаток сеанса — от нынешнего спектра", session.Stamp,
                     FsaAnalysisSession.BuildStamp(doc.ActiveResultData,
                         doc.ActiveResultData.BackgroundEnergySpectrum != null));

                // Отрицательный контроль: данные НЕ менялись — лишнего счёта нет.
                int after = session.RunCount;
                doc.RefreshView();
                WaitIdle(session);
                Same("контроль: без смены данных лишнего счёта нет", after, session.RunCount);

                report.SetDocument(null);
            }
        }

        // ------------------------------------------------------------------
        // 5. СЕМЬ РОДОВ СТРОК, БЕЗ ПОТЕРЬ ПО ВЫСОТЕ
        // ------------------------------------------------------------------

        static void RowKindsSection(MainForm mainForm, DocEnergySpectrum a, DocEnergySpectrum b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 5. семь родов строк; таблица не отбрасывает по высоте ===");
            var kinds = new HashSet<FsaReportRowKind>();
            using (var report = new FSAReportView(mainForm))
            {
                report.ProbeConsumer = true;
                foreach (DocEnergySpectrum doc in new[] { a, b })
                {
                    foreach (bool nucBase in new[] { false, true })
                    {
                        SetSource(doc, nucBase);
                        report.SetDocument(doc);
                        WaitIdle(doc.FsaSession);
                        report.RefreshReport();
                        var here = new List<string>();
                        foreach (Row row in report.ReportTable.TableModel.Rows)
                        {
                            var model = (FsaReportRow)row.Tag;
                            kinds.Add(model.Kind);
                            here.Add(model.Kind.ToString());
                        }

                        Console.WriteLine("  {0} ({1}): {2} строк — {3}", doc.Filename, nucBase ? "NucBase" : "пики",
                                          here.Count, string.Join(" ", here));

                        // (`A244`) ЧИСЛА СОСТАВА ПОСИМВОЛЬНО: слева — само число
                        // формата `R` (его правка формата тронуть не может),
                        // справа — то, что напечатано человеку. Строки `NUM`
                        // сравниваются между сборками «до» и «после»: левая
                        // половина обязана совпасть, правая — измениться только
                        // там, где был разделитель разрядов.
                        if (report.Presentation != null && report.Presentation.Layers != null)
                        {
                            foreach (FsaStackLayer layer in report.Presentation.Layers)
                            {
                                Console.WriteLine("NUM\t{0}\t{1}\t{2}\t{3}\t{4}", doc.Filename, nucBase ? "nucbase" : "peaks",
                                                  layer.Name, layer.SharePercent.ToString("R", CultureInfo.InvariantCulture),
                                                  FsaPresentationBuilder.ShareText(layer));
                            }

                            FsaResult src = report.Presentation.Source;
                            Console.WriteLine("NUM\t{0}\t{1}\t{2}\t{3}\t{4}", doc.Filename, nucBase ? "nucbase" : "peaks",
                                              "chi2/residual", src.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture)
                                              + ";" + src.ResidualExcessShare.ToString("R", CultureInfo.InvariantCulture)
                                              + ";" + src.ResidualMissingShare.ToString("R", CultureInfo.InvariantCulture),
                                              QualityValues(report));
                        }
                    }
                }

                // Сцена со ВСЕМИ родами разом — собранная (реальные спектры дают их
                // по частям): без фона, свёрнутые кандидаты, сумм-пики.
                FsaResult scene = SceneAllKinds(a.FsaSession.Result);
                var synthetic = new FsaAnalysisSession();
                Plant(synthetic, scene, "scene");
                report.SetProbeSource(synthetic, a.ActiveResultData);
                var sceneKinds = new HashSet<FsaReportRowKind>();
                foreach (Row row in report.ReportTable.TableModel.Rows)
                {
                    sceneKinds.Add(((FsaReportRow)row.Tag).Kind);
                }

                foreach (FsaReportRowKind kind in Enum.GetValues(typeof(FsaReportRowKind)))
                {
                    if (kind == FsaReportRowKind.Status) continue;
                    Same("род строки в собранной сцене: " + kind, true, sceneKinds.Contains(kind));
                }

                Console.WriteLine("  роды на настоящих спектрах: {0}", string.Join(" ", kinds));

                // Таблица не отбрасывает по высоте. ⛔ СУДИТСЯ СОСТАВ, А НЕ ЧИСЛО
                // (`A249`): после `A247` строки модели ложатся в таблицу не одна в
                // одну — блок качества добавляет черту, заголовок и по строке на
                // пометку, и «строк поровну» стало неверным правилом, а не
                // нарушенным. Состав читается по `Tag.Kind`, содержимое строк вне
                // блока — по подписи и значению.
                using (Form host = Host(report, 320, 160))
                {
                    report.RefreshReport();
                    Application.DoEvents();
                    int marks;
                    string want = Kinds(ExpectedKinds(report, out marks));
                    string have = Kinds(TableKinds(report));
                    int visible = report.ReportTable.GetVisibleRowCount();
                    int rowsInTable = report.ReportTable.TableModel.Rows.Count;
                    Console.WriteLine("  окно 320×160: строк модели {0}, в таблице {1} (черта, заголовок и {2} пометки блока), видимых без прокрутки {3}",
                                      report.BuildRows().Count, rowsInTable, marks, visible);
                    Same("малое окно: СОСТАВ строк таблицы по Tag.Kind = составу модели с развёрнутым блоком",
                         want, have);
                    Same("малое окно: строки ВНЕ блока качества — один в один с моделью, и то же в ячейках",
                         string.Empty, BodyMismatch(report));
                    Same("контроль: видимых без прокрутки МЕНЬШЕ — значит, прокрутка есть, а не потеря",
                         true, visible < rowsInTable);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (`A249`). Мягкая проверка вместо
                    // жёсткой хуже жёсткой: сверка состава обязана ОТКАЗАТЬ на
                    // подменённом роде строки и на потерянной строке. Портится
                    // ровно то, что читает проверка, — `Tag` строки таблицы, — и
                    // тут же возвращается.
                    Row victim = report.ReportTable.TableModel.Rows[rowsInTable - 1];
                    var saved = (FsaReportRow)victim.Tag;
                    victim.Tag = new FsaReportRow { Kind = FsaReportRowKind.Layer, Name = saved.Name, Value = saved.Value };
                    Denies("контроль: подменённый Tag.Kind последней строки сверка не принимает",
                           want == Kinds(TableKinds(report)));
                    victim.Tag = saved;
                    Same("после возврата Tag состав снова сходится", want, Kinds(TableKinds(report)));

                    report.ReportTable.TableModel.Rows.Remove(victim);
                    Denies("контроль: потерянную строку блока сверка не принимает",
                           want == Kinds(TableKinds(report)));
                    report.RefreshReport();
                    Same("после перестройки таблицы состав снова сходится", want, Kinds(TableKinds(report)));
                    host.Hide();
                }

                report.SetDocument(null);
            }
        }

        /// <summary>
        /// Результат, у которого есть строки всех семи родов: слои с сумм-пиками,
        /// именованный и свёрнутый необнаруженные, фон не применён, невязка и
        /// качество. Строится из НАСТОЯЩЕГО результата (слои и модель — его),
        /// подставляются только признаки, которых у живого спектра нет.
        /// </summary>
        static FsaResult SceneAllKinds(FsaResult real)
        {
            var result = new FsaResult
            {
                Chi2Ndf = real.Chi2Ndf,
                Model = real.Model,
                Continuum = real.Continuum,
                BackgroundUsed = false,
                EfficiencyUsed = real.EfficiencyUsed,
                ResponseMatrixUsed = real.ResponseMatrixUsed,
                CascadeSummingUsed = true,
                ResidualExcessShare = real.ResidualExcessShare,
                ResidualMissingShare = real.ResidualMissingShare
            };
            foreach (FsaComponentResult c in real.Components)
            {
                if (c.SumPeakCurve == null && c.Curve != null)
                {
                    var sums = new double[c.Curve.Length];
                    for (int i = 0; i < sums.Length; i++) sums[i] = c.Curve[i] * 0.1;
                    c.SumPeakCurve = sums;
                }

                result.Components.Add(c);
            }

            result.CharacteristicLimits.Add(new FsaCharacteristicLimit
            {
                Name = "Ra-226", Kind = FsaComponentKind.Single, DetectionLimitRate = 1.0,
                DetectionLimitPeakCounts = 100.0, TotalYieldPercent = 5.28
            });
            result.CharacteristicLimits.Add(new FsaCharacteristicLimit
            {
                Name = "Rn-220", Kind = FsaComponentKind.Single, DetectionLimitRate = 1.0,
                DetectionLimitPeakCounts = 100.0, TotalYieldPercent = 0.114
            });
            return result;
        }

        // ------------------------------------------------------------------
        // СОСТОЯНИЯ ТАБЛИЦЫ
        // ------------------------------------------------------------------

        static void StatesSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== состояния: СТРОКА НАД таблицей и содержимое таблицы ===");
            using (var report = new FSAReportView(mainForm))
            {
                // ⛔ ЧТО ИМЕННО ПОВЕРЯЕТСЯ ПОСЛЕ 07.09.2026. Признак «идёт
                // расчёт» БОЛЬШЕ НЕ СТРОКА ТАБЛИЦЫ: при записи спектра разбор
                // пересчитывается непрерывно, и строка мигала, сдвигая таблицу
                // (решение Amber). Он переехал в постоянную цветную строку НАД
                // таблицей. Поэтому здесь два утверждения на каждое состояние:
                // что говорит СТРОКА и чего НЕТ в таблице.
                Label status = (Label)Field(report, "statusLabel").GetValue(report);
                var session = new FsaAnalysisSession();
                ResultData rd = doc.ActiveResultData;

                Field(session, "running").SetValue(session, true);
                Field(session, "status").SetValue(session, BecquerelMonitor.Properties.Resources.FSACalculating);
                report.SetProbeSource(session, rd);
                Same("первый расчёт идёт: строка состояния «идёт расчёт»",
                     BecquerelMonitor.Properties.Resources.FSAStatusRunning, status.Text);
                Same("и таблица ПУСТА — «считается» в ней больше нет",
                     0, report.ReportTable.TableModel.Rows.Count);

                Plant(session, doc.FsaSession.Result, "old");
                Field(session, "running").SetValue(session, true);
                report.RefreshReport();
                int rows = report.ReportTable.TableModel.Rows.Count;
                Same("пересчёт при старом результате: строка состояния «идёт расчёт»",
                     BecquerelMonitor.Properties.Resources.FSAStatusRunning, status.Text);
                Same("и первая строка таблицы — СОСТАВ, а не «пересчёт»",
                     FsaReportRowKind.Layer, ((FsaReportRow)report.ReportTable.TableModel.Rows[0].Tag).Kind);
                Same("старые строки остались (их больше одной)", true, rows > 1);

                Field(session, "running").SetValue(session, false);
                report.RefreshReport();
                Same("расчёт завершён: строка состояния «завершён»",
                     BecquerelMonitor.Properties.Resources.FSAStatusCompleted, status.Text);
                Same("число строк таблицы НЕ изменилось — мигать больше нечему",
                     rows, report.ReportTable.TableModel.Rows.Count);
                Same("первая строка — состав", FsaReportRowKind.Layer, ((FsaReportRow)report.ReportTable.TableModel.Rows[0].Tag).Kind);

                Field(session, "result").SetValue(session, null);
                Field(session, "status").SetValue(session, "ОШИБКА: причина такая-то");
                report.RefreshReport();
                Same("ошибка: строка состояния несёт ПРИЧИНУ, а не одно слово",
                     BecquerelMonitor.Properties.Resources.FSAStatusError + ": ОШИБКА: причина такая-то",
                     status.Text);
                Same("ошибка без результата: одна строка", 1, report.ReportTable.TableModel.Rows.Count);
                Same("и это текст причины", "ОШИБКА: причина такая-то", Text(report, 0, 1));
                report.SetDocument(null);
            }
        }

        // ------------------------------------------------------------------
        // 6. СТРОКА КАЧЕСТВА
        // ------------------------------------------------------------------

        /// <summary>
        /// (`A249`) БЛОК «КАЧЕСТВО РАЗБОРА» — черта, заголовок, χ²/ndf своей
        /// строкой и по строке на каждую пометку.
        ///
        /// ⛔ Прежнее правило («весь хвост пометок одной ячейкой, равной
        /// <c>FsaPresentationBuilder.QualityText</c>») СНЯТО решением Amber
        /// 05.09.2026 вместе с `A247`: та самая склеенная подпись и не
        /// помещалась в колонку, ради чего блок и заведён. Проверять её здесь
        /// значило бы держать приёмку на отменённом правиле.
        ///
        /// ⚠ χ²/ndf берётся числом ≥ 1000 нарочно (`A244`): ниже тысячи `n2` и
        /// `f2` неотличимы, и разделитель разрядов прошёл бы приёмку насквозь.
        /// </summary>
        static void QualitySection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 6. блок «Качество разбора»: заголовок, χ²/ndf, по строке на пометку, обе культуры ===");
            const double chi2 = 1234.5678;
            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                FsaResult scene = new FsaResult
                {
                    Chi2Ndf = chi2, BackgroundUsed = true, ResponseMatrixUsed = false,
                    EfficiencyUsed = false, CascadeSummingUsed = true, GainOnGridEdge = true
                };
                FieldInfo f = typeof(FsaResult).GetField("<SuppressorName>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                f.SetValue(scene, "Backscatter");
                Same(lang + ": сцена «подавлен» собралась", true, scene.CompositionSuppressed);

                var session = new FsaAnalysisSession();
                Plant(session, scene, "quality");
                Field(session, "matrixOldFormat").SetValue(session, true);
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(session, doc.ActiveResultData);

                    // Подписи и слова состояния — из СОБСТВЕННОЙ `.resx` окна,
                    // тем же `ComponentResourceManager`, каким читает окно.
                    // Ключ вместо перевода — отказ: значит `.resx` не прочитан.
                    //
                    // (`AMBER11`) Третьим столбцом — ЦВЕТ слова состояния:
                    // `+` зелёный (учтено), `−` красный (не учтено). Ожидание
                    // записано ЧИСЛОМ здесь, а не взято отражением у окна:
                    // сторож, читающий эталон у подсудимого, проверяет лишь
                    // самосогласованность.
                    var marks = new List<string[]>
                    {
                        new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixOldFormat"), Bad },
                        new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyNotUsed"), Bad },
                        new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good },
                        new[] { Own("FSAReport_DriftRow"), Own("FSAReport_DriftEdge"), Bad },
                        // Имя пересилившего образа — данные результата, не надпись.
                        new[] { Own("FSAReport_SuppressedRow"), "Backscatter", Bad }
                    };
                    int keys = 0;
                    foreach (string[] mark in marks)
                    {
                        if (mark[0].StartsWith("FSAReport_", StringComparison.Ordinal)) keys++;
                    }

                    Same(lang + ": все подписи блока прочитаны из resx (ни одного имени ключа)", 0, keys);
                    ShowBlock(report, lang);
                    Same(lang + ": блок «Качество разбора» собран верно", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", marks)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПЕРВЫЙ: убрана строка χ²/ndf.
                    // Проверка, не отказывающая на заведомо неверном составе,
                    // хуже прежней жёсткой — она не проверяет ничего.
                    int chiRow = Chi2Row(report);
                    Row saved = report.ReportTable.TableModel.Rows[chiRow];
                    report.ReportTable.TableModel.Rows.Remove(saved);
                    Denies(lang + ": контроль — блок без строки χ²/ndf проверку не проходит",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    report.ReportTable.TableModel.Rows.Insert(chiRow, saved);
                    Same(lang + ": строка χ²/ndf возвращена, блок снова сходится", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", marks)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВТОРОЙ: подменено слово состояния
                    // у пометки — блок остаётся той же длины, а содержимое лжёт.
                    Row markRow = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2];
                    string was = markRow.Cells[2].Text;
                    markRow.Cells[2].Text = Own("FSAReport_MatrixUsed");
                    Denies(lang + ": контроль — подменённое слово состояния пометки проверку не проходит",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    markRow.Cells[2].Text = was;

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ТРЕТИЙ (`A244`): число с
                    // разделителем разрядов проверку не проходит — иначе она
                    // судила бы равенство плеч, а запятая в группах на всех
                    // культурах одна и та же и плечи не разводит.
                    Denies(lang + ": контроль — «1,234.57» в колонке значения проверку не проходит",
                           BlockProblems(report, "1,234.57", marks).Count == 0);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ЧЕТВЁРТЫЙ (`AMBER11`): цвет
                    // пометки подменён на противоположный — текст тот же,
                    // длина блока та же, лжёт только цвет.
                    Row painted = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2];
                    Color wasColor = painted.Cells[2].ForeColor;
                    painted.Cells[2].ForeColor = GoodColor;
                    Denies(lang + ": контроль — красная пометка, перекрашенная в зелёный, проверку не проходит",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    painted.Cells[2].ForeColor = wasColor;

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПЯТЫЙ (`A281`): убрана строка
                    // множителя σ×. Сцена собрана с зажатым множителем (1.000),
                    // и без этого контроля «строка есть» было бы неотличимо от
                    // «строки нет»: пустая ячейка и молчание выглядят одинаково.
                    int sigmaRow = Chi2Row(report) + 1;
                    Row sigmaSaved = report.ReportTable.TableModel.Rows[sigmaRow];
                    report.ReportTable.TableModel.Rows.Remove(sigmaSaved);
                    Denies(lang + ": контроль — блок без строки σ× проверку не проходит",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    report.ReportTable.TableModel.Rows.Insert(sigmaRow, sigmaSaved);
                    Same(lang + ": строка σ× возвращена, блок снова сходится", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", marks)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ШЕСТОЙ (`A281`): чужое число в
                    // ячейке σ×. Без него проверка судила бы лишь наличие
                    // строки, а печатать всегда «1.000» — ровно тот дефект,
                    // ради которого множитель и выведен на экран.
                    Denies(lang + ": контроль — «8.163» вместо зажатого «1.000» проверку не проходит",
                           BlockProblems(report, "1234.57", "8.163", marks).Count == 0);
                }

                // ⛔ (`A281`) ВТОРАЯ СЦЕНА МНОЖИТЕЛЯ — РАЗДУТЫЙ. Число взято не
                // из головы: 8.163 — множитель `G1S24_Eu152_P5`, худший на
                // малой базе (замер 10.09.2026, `out_p17_off`). Сцена с одним
                // зажатым множителем доказывала бы только, что окно умеет
                // печатать единицу.
                var inflated = new FsaResult
                {
                    Chi2Ndf = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                    EfficiencyUsed = true, CascadeSummingUsed = true,
                    SigmaInflation = 8.163
                };
                var inflatedSession = new FsaAnalysisSession();
                Plant(inflatedSession, inflated, "quality-inflate");
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(inflatedSession, doc.ActiveResultData);
                    var green = new List<string[]>
                    {
                        new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                        new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                        new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good }
                    };

                    ShowBlock(report, lang);
                    Same(lang + ": раздутый множитель показан как 8.163", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", "8.163", green)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: на этой сцене ожидание «1.000»
                    // проверку проходить не должно — иначе обе сцены мерили бы
                    // одно и то же.
                    Denies(lang + ": контроль — «1.000» на раздутой сцене проверку не проходит",
                           BlockProblems(report, "1234.57", "1.000", green).Count == 0);
                }

                // (`AMBER11`, задача Amber 09.09.2026) ВТОРАЯ СЦЕНА — ВСЁ
                // УЧТЕНО. Без неё зелёный меряется на одной пометке из трёх, и
                // «матрица использована» могла бы остаться серой незамеченной:
                // сцена выше даёт зелёным только суммирование.
                var whole = new FsaResult
                {
                    Chi2Ndf = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                    EfficiencyUsed = true, CascadeSummingUsed = true, GainOnGridEdge = false
                };
                var wholeSession = new FsaAnalysisSession();
                Plant(wholeSession, whole, "quality-good");
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(wholeSession, doc.ActiveResultData);
                    var green = new List<string[]>
                    {
                        new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                        new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                        new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good }
                    };

                    ShowBlock(report, lang);
                    Same(lang + ": всё учтено — три пометки зелёные", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", green)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: зелёная пометка, перекрашенная
                    // в красный, проверку проходить не должна.
                    Row painted = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2];
                    Color wasColor = painted.Cells[2].ForeColor;
                    painted.Cells[2].ForeColor = BadColor;
                    Denies(lang + ": контроль — зелёная пометка, перекрашенная в красный, проверку не проходит",
                           BlockProblems(report, "1234.57", green).Count == 0);
                    painted.Cells[2].ForeColor = wasColor;
                }
            }

            Language("en-US");
        }

        /// <summary>(`AMBER11`) Цвет словами — чтобы отказ читался, а не считался в ARGB.</summary>
        static string Paint(Color color)
        {
            if (color.ToArgb() == GoodColor.ToArgb()) return "зелёный";
            if (color.ToArgb() == BadColor.ToArgb()) return "красный";
            if (color.ToArgb() == Color.Black.ToArgb()) return "чёрный";
            if (color.ToArgb() == Color.Gray.ToArgb()) return "серый";
            return "R" + color.R + " G" + color.G + " B" + color.B;
        }

        /// <summary>(`AMBER11`) Метка ожидаемого цвета: положительный статус.</summary>
        const string Good = "+";

        /// <summary>(`AMBER11`) Метка ожидаемого цвета: отрицательный статус.</summary>
        const string Bad = "−";

        /// <summary>
        /// (`AMBER11`) Зелёный положительного статуса — ТОТ ЖЕ, каким строка
        /// состояния говорит «FSA completed». Записан числом нарочно: эталон
        /// сторожа не берётся у подсудимого.
        /// </summary>
        static readonly Color GoodColor = Color.FromArgb(0, 128, 0);

        /// <summary>(`AMBER11`) Красный отрицательного статуса.</summary>
        static readonly Color BadColor = Color.Firebrick;

        /// <summary>Печать блока целиком — им читается вид, а не только отказы.</summary>
        static void ShowBlock(FSAReportView report, string lang)
        {
            TableModel model = report.ReportTable.TableModel;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                if (((FsaReportRow)model.Rows[i].Tag).Kind != FsaReportRowKind.Quality
                    && ((FsaReportRow)model.Rows[i].Tag).Kind != FsaReportRowKind.Residual)
                {
                    continue;
                }

                // (`AMBER11`) Цвет значения печатается рядом: блок читается
                // глазом по цвету, и вывод сторожа обязан говорить то же.
                Console.WriteLine("  {0}: [{1}] «{2}» | {3} ({4})", lang, i,
                                  model.Rows[i].Cells[1].Text, model.Rows[i].Cells[2].Text,
                                  Paint(model.Rows[i].Cells[2].ForeColor));
            }
        }

        /// <summary>
        /// Номер строки χ²/ndf: первая строка рода Quality ПОСЛЕ черты и
        /// заголовка. Считать смещением от черты нельзя — между заголовком и
        /// χ²/ndf стоят строки блока, которых у другой сцены может не быть
        /// («без фона» появляется только при неучтённом фоне).
        /// </summary>
        static int Chi2Row(FSAReportView report)
        {
            TableModel model = report.ReportTable.TableModel;
            int rule = -1;
            for (int i = 0; i < model.Rows.Count && rule < 0; i++)
            {
                if (((FsaReportRow)model.Rows[i].Tag).Kind == FsaReportRowKind.Quality) rule = i;
            }

            for (int i = rule + 2; rule >= 0 && i < model.Rows.Count; i++)
            {
                if (((FsaReportRow)model.Rows[i].Tag).Kind == FsaReportRowKind.Quality) return i;
            }

            return -1;
        }

        /// <summary>
        /// (`A249`) Жалобы на блок «Качество разбора»: пусто — блок собран так,
        /// как решено 05.09.2026. Возвращается СПИСОК, а не признак: у
        /// положительного контроля должно быть видно, на чём именно отказ.
        /// </summary>
        static List<string> BlockProblems(FSAReportView report, string chi2Value, List<string[]> marks)
        {
            return BlockProblems(report, chi2Value, "1.000", marks);
        }

        /// <summary>
        /// То же, но с ожидаемым множителем погрешностей `σ×` (`A281`).
        /// Отдельная перегрузка, а не значение по умолчанию: сцена с зажатым
        /// множителем (1.000) и сцена с раздутым обязаны отличаться В ВЫЗОВЕ, а
        /// не молча брать одно и то же ожидание.
        /// </summary>
        static List<string> BlockProblems(FSAReportView report, string chi2Value, string inflateValue,
                                          List<string[]> marks)
        {
            var bad = new List<string>();
            TableModel model = report.ReportTable.TableModel;
            int rule = -1;
            for (int i = 0; i < model.Rows.Count && rule < 0; i++)
            {
                if (((FsaReportRow)model.Rows[i].Tag).Kind == FsaReportRowKind.Quality) rule = i;
            }

            if (rule < 0)
            {
                bad.Add("блока нет вовсе: ни одной строки рода Quality");
                return bad;
            }

            if (model.Rows[rule].Cells[1].Text.Length > 0 || model.Rows[rule].Cells[2].Text.Length > 0)
            {
                bad.Add("черта не пуста: «" + model.Rows[rule].Cells[1].Text + "»");
            }

            if (rule + 1 >= model.Rows.Count)
            {
                bad.Add("после черты нет заголовка");
                return bad;
            }

            Row header = model.Rows[rule + 1];
            if (header.Cells[1].Text != Own("FSAReport_QualityHeader"))
            {
                bad.Add("заголовок «" + header.Cells[1].Text + "» вместо «" + Own("FSAReport_QualityHeader") + "»");
            }

            if (header.Cells[1].Font == null || !header.Cells[1].Font.Bold)
            {
                bad.Add("заголовок не полужирный");
            }

            // Невязка стоит В БЛОКЕ и своей строкой: она мера разбора, а не
            // компонент состава (`A247`).
            if (rule + 2 >= model.Rows.Count
                || ((FsaReportRow)model.Rows[rule + 2].Tag).Kind != FsaReportRowKind.Residual)
            {
                bad.Add("под заголовком нет строки невязки");
            }
            else if (model.Rows[rule + 2].Cells[1].Text != Own("FSAReport_ResidualRow"))
            {
                bad.Add("подпись невязки «" + model.Rows[rule + 2].Cells[1].Text + "»");
            }

            int chi = -1;
            for (int i = rule + 2; i < model.Rows.Count && chi < 0; i++)
            {
                if (((FsaReportRow)model.Rows[i].Tag).Kind == FsaReportRowKind.Quality) chi = i;
            }

            if (chi < 0)
            {
                bad.Add("строки χ²/ndf в блоке нет");
                return bad;
            }

            if (model.Rows[chi].Cells[1].Text != Own("FSAReport_Chi2Row"))
            {
                bad.Add("подпись χ²/ndf «" + model.Rows[chi].Cells[1].Text + "»");
            }

            if (model.Rows[chi].Cells[2].Text != chi2Value)
            {
                bad.Add("значение χ²/ndf «" + model.Rows[chi].Cells[2].Text + "» вместо «" + chi2Value + "»");
            }

            // ⛔ (`A281`, решение Amber 10.09.2026 «В блок „Качество разбора“
            // окна отчёта») МНОЖИТЕЛЬ ПОГРЕШНОСТЕЙ — СРАЗУ ПОД χ²/ndf.
            //
            // Судится и подпись, и САМО ЧИСЛО: строка, показывающая «1.000»
            // всегда, выглядела бы точно так же, а именно она и была бы
            // дефектом — множитель по малой базе идёт от 1.000 до 8.163.
            // ⚠ Значение проверяется ПОСИМВОЛЬНО, как и χ²/ndf: разделитель
            // разрядов и запятая вместо точки — тот же грех `A242`/`A244`.
            if (chi + 1 >= model.Rows.Count)
            {
                bad.Add("под строкой χ²/ndf нет строки множителя σ×");
                return bad;
            }

            if (model.Rows[chi + 1].Cells[1].Text != Own("FSAReport_InflationRow"))
            {
                bad.Add("подпись σ× «" + model.Rows[chi + 1].Cells[1].Text + "» вместо «"
                        + Own("FSAReport_InflationRow") + "»");
            }

            if (model.Rows[chi + 1].Cells[2].Text != inflateValue)
            {
                bad.Add("значение σ× «" + model.Rows[chi + 1].Cells[2].Text + "» вместо «"
                        + inflateValue + "»");
            }

            if (string.IsNullOrEmpty(model.Rows[chi + 1].Cells[1].ToolTipText)
                || model.Rows[chi + 1].Cells[1].ToolTipText == model.Rows[chi + 1].Cells[1].Text)
            {
                bad.Add("у строки σ× нет своей подсказки");
            }

            int have = model.Rows.Count - chi - 2;
            if (have != marks.Count)
            {
                bad.Add("пометок " + have + " вместо " + marks.Count);
            }

            for (int k = 0; k < marks.Count && chi + 2 + k < model.Rows.Count; k++)
            {
                Row row = model.Rows[chi + 2 + k];
                if (row.Cells[1].Text != marks[k][0] || row.Cells[2].Text != marks[k][1])
                {
                    bad.Add("пометка " + (k + 1) + " «" + row.Cells[1].Text + " | " + row.Cells[2].Text
                            + "» вместо «" + marks[k][0] + " | " + marks[k][1] + "»");
                }

                // (`AMBER11`) ЦВЕТ СЛОВА СОСТОЯНИЯ. Судится ячейка ЗНАЧЕНИЯ:
                // подпись остаётся чёрной по решению Amber 09.09.2026, и её
                // почернение проверяется отдельной жалобой ниже.
                if (marks[k].Length > 2)
                {
                    Color want = marks[k][2] == Good ? GoodColor : BadColor;
                    if (row.Cells[2].ForeColor.ToArgb() != want.ToArgb())
                    {
                        bad.Add("цвет пометки " + (k + 1) + " «" + row.Cells[2].Text + "» — "
                                + Paint(row.Cells[2].ForeColor) + " вместо " + Paint(want));
                    }

                    if (row.Cells[1].ForeColor.ToArgb() != Color.Black.ToArgb())
                    {
                        bad.Add("подпись пометки " + (k + 1) + " окрашена в " + Paint(row.Cells[1].ForeColor)
                                + ", а должна быть чёрной");
                    }
                }

                if (Trimmed(row.Cells[1].Text))
                {
                    bad.Add("пометка " + (k + 1) + " усечена многоточием");
                }

                if (!row.Cells[1].WordWrap)
                {
                    bad.Add("пометка " + (k + 1) + " без переноса по словам");
                }
            }

            return bad;
        }

        // ------------------------------------------------------------------
        // 7. ГРУППИРОВКА И РАСЧЁТНЫЕ ФЛАГИ
        // ------------------------------------------------------------------

        static void GroupingAndFlagsSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 7. родители/дочерние без счёта; расчётный флаг — один пересчёт ===");
            SetSource(doc, true);
            FsaAnalysisSession session = doc.FsaSession;
            int completed = 0;
            EventHandler onDone = (s, e) => Interlocked.Increment(ref completed);
            session.Completed += onDone;
            try
            {
                using (var report = new FSAReportView(mainForm))
                {
                    report.ProbeConsumer = true;
                    report.SetDocument(doc);
                    WaitIdle(session);
                    report.RefreshReport();
                    string stamp = session.Stamp;
                    int runs = session.RunCount;
                    int done = completed;
                    Same("исходно: результат есть", true, session.Result != null);
                    Same("исходно: родители допустимы (Th-232, NucBase+равновесие)", true,
                         report.Presentation != null && report.Presentation.ParentGroupingAllowed);

                    RadioButton parents = Control<RadioButton>(report, "parentsRadio");
                    RadioButton daughters = Control<RadioButton>(report, "daughtersRadio");
                    Same("радиокнопка родителей доступна", true, parents.Enabled);
                    int daughterRows = report.ReportTable.TableModel.Rows.Count;
                    parents.Checked = true;
                    Same("родители: отпечаток сеанса тот же", stamp, session.Stamp);
                    Same("родители: запусков не прибавилось", runs, session.RunCount);
                    Same("родители: событий не прибавилось", done, completed);
                    Same("родители: группировка документа = родители", FsaGrouping.Parents, doc.FsaGrouping);
                    Same("родители: строк стало меньше", true, report.ReportTable.TableModel.Rows.Count < daughterRows);
                    daughters.Checked = true;
                    Same("дочерние: строк снова столько же", daughterRows, report.ReportTable.TableModel.Rows.Count);
                    Same("дочерние: группировка документа = дочерние", FsaGrouping.Daughters, doc.FsaGrouping);
                    Same("дочерние: запусков не прибавилось", runs, session.RunCount);

                    // Расчётный флаг — через НАСТОЯЩИЙ флажок окна.
                    CheckBox xray = Control<CheckBox>(report, "atomicXrayCheckBox");
                    var cfg = (FWHMPeakDetectionMethodConfig)doc.ActiveResultData.PeakDetectionMethodConfig;
                    bool was = cfg.AtomicXrayForFsa;
                    xray.Checked = !was;
                    WaitIdle(session);
                    Same("флаг: конфигурация спектра записана", !was, cfg.AtomicXrayForFsa);
                    Same("флаг: отпечаток сменился", false, session.Stamp == stamp);
                    Same("флаг: запуск ровно один", runs + 1, session.RunCount);
                    Same("флаг: событие завершения ровно одно", done + 1, completed);
                    Same("флаг: результат посчитан новым отпечатком", session.Stamp,
                         FsaAnalysisSession.BuildStamp(doc.ActiveResultData, doc.ActiveResultData.BackgroundEnergySpectrum != null));
                    DeviceConfigInfo device;
                    DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(doc.ActiveResultData.DeviceConfigReference.Guid, out device);
                    Same("флаг: умолчание прибора записано", !was,
                         device != null && ((FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig).AtomicXrayForFsa);

                    // Контроль: тот же флаг обратно — ещё один запуск (счётчик не залип).
                    xray.Checked = was;
                    WaitIdle(session);
                    Same("контроль: обратное переключение — ещё один запуск", runs + 2, session.RunCount);
                    Same("контроль: отпечаток вернулся к исходному", stamp, session.Stamp);

                    // Быстрая серия: два флага подряд — одна публикация, отпечаток последнего.
                    int before = completed;
                    CheckBox pileUp = Control<CheckBox>(report, "pileUpCheckBox");
                    CheckBox bs = Control<CheckBox>(report, "backscatterCheckBox");
                    pileUp.Checked = false;
                    bs.Checked = false;
                    WaitIdle(session);
                    Same("серия из двух флагов: отпечаток последнего снимка", session.Stamp,
                         FsaAnalysisSession.BuildStamp(doc.ActiveResultData, doc.ActiveResultData.BackgroundEnergySpectrum != null));
                    Same("серия из двух флагов: публикация одна", before + 1, completed);
                    pileUp.Checked = true;
                    bs.Checked = true;
                    WaitIdle(session);
                    report.SetDocument(null);
                }
            }
            finally
            {
                session.Completed -= onDone;
            }
        }

        // ------------------------------------------------------------------
        // 8. РОДИТЕЛИ: доступность и тождество сумм
        // ------------------------------------------------------------------

        static void ParentsSection(MainForm mainForm, DocEnergySpectrum thorium, DocEnergySpectrum control)
        {
            Console.WriteLine();
            Console.WriteLine("=== 8. родители недоступны без NucBase+равновесия; при допустимом — суммы сходятся ===");
            using (var report = new FSAReportView(mainForm))
            {
                report.ProbeConsumer = true;
                RadioButton parents = Control<RadioButton>(report, "parentsRadio");
                RadioButton daughters = Control<RadioButton>(report, "daughtersRadio");
                CheckBox equilibrium = Control<CheckBox>(report, "equilibriumCheckBox");
                RadioButton nucBase = Control<RadioButton>(report, "sourceNucBaseRadio");
                RadioButton peaks = Control<RadioButton>(report, "sourcePeaksRadio");

                SetSource(thorium, false);
                report.SetDocument(thorium);
                WaitIdle(thorium.FsaSession);
                report.RefreshReport();
                Same("по пикам: родители недоступны", false, parents.Enabled);
                Same("по пикам: равновесие недоступно", false, equilibrium.Enabled);
                Same("по пикам: подсказка родителей — «нужен NucBase+равновесие»",
                     BecquerelMonitor.Properties.Resources.FSAReportTipParentsNeedNucBase, report.ToolTipOf(parents));
                Same("по пикам: подсказка равновесия — «нужен NucBase»",
                     BecquerelMonitor.Properties.Resources.FSAReportTipEquilibriumNeedsNucBase, report.ToolTipOf(equilibrium));
                Same("по пикам: выбраны дочерние", true, daughters.Checked && !parents.Checked);

                // Просьба «родители» помнится, пока недоступна.
                report.RequestedGrouping = FsaGrouping.Parents;
                Same("просьба родителей при недоступности: показаны дочерние", true, daughters.Checked);
                Same("просьба помнится", FsaGrouping.Parents, report.RequestedGrouping);
                Same("а к графику ушли дочерние", FsaGrouping.Daughters, thorium.FsaGrouping);

                nucBase.Checked = true;
                WaitIdle(thorium.FsaSession);
                report.RefreshReport();
                Same("NucBase + равновесие: равновесие доступно", true, equilibrium.Enabled);
                Same("NucBase + равновесие: родители доступны", true, parents.Enabled);
                Same("NucBase + равновесие: просьба восстановлена — выбраны родители", true, parents.Checked);
                Same("NucBase + равновесие: к графику ушли родители", FsaGrouping.Parents, thorium.FsaGrouping);
                Same("подсказка родителей — «только группировка»",
                     BecquerelMonitor.Properties.Resources.FSAReportTipGrouping, report.ToolTipOf(parents));

                // Тождество сумм: ленты и доли родителей = сумма дочерних (критерий 8).
                FsaResult result = thorium.FsaSession.Result;
                FsaPresentation d = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
                FsaPresentation p = report.Presentation;
                Same("представление окна — родительское", FsaGrouping.Parents, p.Grouping);
                double worst = 0.0, worstShare = 0.0;
                int roots = 0;
                foreach (FsaStackLayer parent in p.Layers)
                {
                    if (parent.Kind != FsaComponentKind.Chain) continue;
                    roots++;
                    var sum = new double[parent.Curve.Length];
                    double share = 0.0;
                    foreach (FsaStackLayer child in d.Layers)
                    {
                        if (child.DecayChainRoot == parent.DecayChainRoot && child.Kind != FsaComponentKind.Nuisance)
                        {
                            Add(sum, child.Curve);
                            share += child.SharePercent;
                        }
                    }

                    worst = Math.Max(worst, RelDiff(sum, parent.Curve));
                    worstShare = Math.Max(worstShare, Math.Abs(share - parent.SharePercent));
                }

                Console.WriteLine("  корней {0}; |Δкривой|/max {1:E1}; |Δдоли| {2:E1}", roots, worst, worstShare);
                Same("корень ряда в родительском представлении есть", true, roots >= 1);
                Same("сумма кривых дочерних = кривая родителя (машинный ноль)", true, worst < 1e-12);
                Same("сумма долей дочерних = доля родителя", true, worstShare < 1e-9);

                // Равновесие выключено — родители снова недоступны, просьба помнится.
                equilibrium.Checked = false;
                WaitIdle(thorium.FsaSession);
                report.RefreshReport();
                Same("равновесие выкл: родители недоступны", false, parents.Enabled);
                Same("равновесие выкл: показаны дочерние", true, daughters.Checked);
                Same("равновесие выкл: просьба помнится", FsaGrouping.Parents, report.RequestedGrouping);
                equilibrium.Checked = true;
                WaitIdle(thorium.FsaSession);
                report.RefreshReport();
                Same("равновесие вкл: родители восстановлены", true, parents.Enabled && parents.Checked);

                // Спектр без ряда: настройки допускают, результат — нет, причина в подсказке.
                SetSource(control, true);
                report.SetDocument(control);
                WaitIdle(control.FsaSession);
                report.RefreshReport();
                Same("без ряда: родители недоступны", false, parents.Enabled);
                Same("без ряда: подсказка называет причину результата", true,
                     report.ToolTipOf(parents).StartsWith(
                         BecquerelMonitor.Properties.Resources.FSAReportTipParentsRefused.Substring(0, 10), StringComparison.Ordinal));
                Console.WriteLine("  подсказка без ряда: «{0}»", report.ToolTipOf(parents));

                peaks.Checked = true;
                WaitIdle(control.FsaSession);
                report.RequestedGrouping = FsaGrouping.Daughters;
                report.SetDocument(null);
            }
        }

        // ------------------------------------------------------------------
        // 11. СОСЕДНИЙ ДОКУМЕНТ
        // ------------------------------------------------------------------

        static void NeighbourSection(MainForm mainForm, DocEnergySpectrum a, DocEnergySpectrum b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 11. флаг в одном документе не меняет копию конфигурации другого ===");
            var cfgA = (FWHMPeakDetectionMethodConfig)a.ActiveResultData.PeakDetectionMethodConfig;
            var cfgB = (FWHMPeakDetectionMethodConfig)b.ActiveResultData.PeakDetectionMethodConfig;
            string stampB = FsaCalculationOptions.FromConfig(cfgB).Stamp;
            using (var report = new FSAReportView(mainForm))
            {
                report.ProbeConsumer = true;
                report.SetDocument(a);
                WaitIdle(a.FsaSession);
                CheckBox cascade = Control<CheckBox>(report, "cascadeSummingCheckBox");
                bool was = cfgA.CascadeSummingForFsa;
                cascade.Checked = !was;
                WaitIdle(a.FsaSession);
                Same("A: флаг записан", !was, cfgA.CascadeSummingForFsa);
                Same("B: копия конфигурации не тронута", stampB, FsaCalculationOptions.FromConfig(cfgB).Stamp);
                Same("B: и не та же ссылка, что у A", false, ReferenceEquals(cfgA, cfgB));
                cascade.Checked = was;
                WaitIdle(a.FsaSession);
                Same("A: флаг возвращён", was, cfgA.CascadeSummingForFsa);
                report.SetDocument(null);
            }
        }

        // ------------------------------------------------------------------
        // 12. СНИМКИ
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 13. ПОВТОРЯЕМОСТЬ СОСТАВА (`A250`)
        // ------------------------------------------------------------------

        /// <summary>
        /// (`A250`) ОДИН ВХОД — ОДИН И ТОТ ЖЕ ОТЧЁТ. Разбор пересчитывается
        /// N раз подряд БЕЗ единого изменения входных данных
        /// (<see cref="FsaAnalysisSession.Invalidate"/> обесценивает кэш, не
        /// трогая ни спектр, ни настройки), и таблица после каждого пересчёта
        /// снимается ПОБИТОВО: род строки плюс обе видимые ячейки.
        ///
        /// ⛔ Почему это не «проверка ради проверки»: состав отчёта у одного
        /// спектра плыл от прогона к прогону — 16 / 20 / 22 / 27 / 29 / 31 / 32
        /// строки на `G1S16_Co60_P5` при одной сборке и одном файле. Сам разбор
        /// при этом детерминирован до бита (`FsaDeterminismProbeF29`), плыла
        /// ПУБЛИКАЦИЯ: событие <see cref="FsaAnalysisSession.Completed"/>
        /// приходит из ФОНОВОГО потока, и окно-потребитель без ручки читало его
        /// прямо там — одновременно с <c>RefreshReport</c> на главном потоке.
        /// Два потока чистили и наполняли одну <c>TableModel.Rows</c>.
        ///
        /// Положительных контроля два: подменённый ПОРЯДОК строк и потерянная
        /// строка — мерка обязана назвать расхождение, а не промолчать.
        /// </summary>
        static void RepeatabilitySection(MainForm mainForm, DocEnergySpectrum a, DocEnergySpectrum b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 13. повторяемость: один вход — побитово тот же отчёт (A250) ===");
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            Console.WriteLine("  поток окон: {0}", mainThread);

            foreach (DocEnergySpectrum doc in new[] { a, b })
            {
                SetSource(doc, true);
                FsaAnalysisSession session = doc.FsaSession;
                var eventThreads = new List<int>();
                EventHandler watch = delegate
                {
                    lock (eventThreads) { eventThreads.Add(Thread.CurrentThread.ManagedThreadId); }
                };
                session.Completed += watch;
                try
                {
                    using (var report = new FSAReportView(mainForm))
                    {
                        report.ProbeConsumer = true;
                        report.SetDocument(doc);
                        WaitIdle(session);
                        report.RefreshReport();

                        var seen = new List<string>();
                        const int Repeats = 12;
                        for (int i = 0; i < Repeats; i++)
                        {
                            // Кэш обесценен, ВХОД не тронут: следующий заказ
                            // потребителя считает заново тот же самый спектр.
                            session.Invalidate();
                            report.SetDocument(doc);
                            WaitIdle(session);
                            report.RefreshReport();
                            seen.Add(TableShot(report));
                        }

                        var distinct = new List<string>();
                        foreach (string shot in seen)
                        {
                            if (!distinct.Contains(shot)) distinct.Add(shot);
                        }

                        Console.WriteLine("  {0}: пересчётов {1}, строк {2}, различных снимков таблицы {3}",
                                          Path.GetFileName(doc.Filename), Repeats,
                                          report.ReportTable.TableModel.Rows.Count, distinct.Count);
                        if (distinct.Count > 1)
                        {
                            for (int k = 0; k < distinct.Count && k < 3; k++)
                            {
                                Console.WriteLine("    --- снимок {0}: строк {1} ---", k + 1,
                                                  distinct[k].Split('\n').Length);
                                Console.WriteLine("    " + distinct[k].Replace("\n", "\n    "));
                            }
                        }

                        Same("состав отчёта повторяется побитово на " + Repeats + " пересчётах подряд",
                             1, distinct.Count);

                        // Событие приходит из ФОНОВОГО потока — это и есть
                        // место, где публикация обязана перейти на поток окон.
                        var others = new List<int>();
                        lock (eventThreads)
                        {
                            foreach (int t in eventThreads)
                            {
                                if (t != mainThread && !others.Contains(t)) others.Add(t);
                            }
                        }

                        Console.WriteLine("  событий Completed {0}, из них с ЧУЖИХ потоков {1} (потоки: {2})",
                                          eventThreads.Count, others.Count,
                                          others.Count == 0 ? "-" : string.Join(",", others));

                        // ⛔ ДВА ПОЛОЖИТЕЛЬНЫХ КОНТРОЛЯ МЕРКИ: подменённый
                        // ПОРЯДОК строк и потерянная строка. Портится ровно
                        // то, что читает мерка, и тут же возвращается.
                        string reference = TableShot(report);
                        TableModel model = report.ReportTable.TableModel;
                        if (model.Rows.Count >= 3)
                        {
                            Row last = model.Rows[model.Rows.Count - 1];
                            Row prev = model.Rows[model.Rows.Count - 2];
                            model.Rows.Remove(last);
                            model.Rows.Remove(prev);
                            model.Rows.Add(last);
                            model.Rows.Add(prev);
                            Denies("контроль: подменённый ПОРЯДОК двух строк мерка не принимает",
                                   reference == TableShot(report));
                            report.RefreshReport();
                            Same("после перестройки таблицы снимок вернулся", reference, TableShot(report));

                            model.Rows.Remove(model.Rows[model.Rows.Count - 1]);
                            Denies("контроль: потерянную строку мерка не принимает",
                                   reference == TableShot(report));
                            report.RefreshReport();
                            Same("и снова вернулся", reference, TableShot(report));
                        }

                        report.SetDocument(null);
                    }
                }
                finally
                {
                    session.Completed -= watch;
                }
            }
        }

        /// <summary>Снимок таблицы: род строки и обе видимые ячейки, по строке на строку.</summary>
        static string TableShot(FSAReportView report)
        {
            var sb = new StringBuilder();
            foreach (Row row in report.ReportTable.TableModel.Rows)
            {
                var model = row.Tag as FsaReportRow;
                sb.Append(model != null ? model.Kind.ToString() : "(нет Tag)")
                  .Append('|').Append(row.Cells.Count > 1 ? row.Cells[1].Text : string.Empty)
                  .Append('|').Append(row.Cells.Count > 2 ? row.Cells[2].Text : string.Empty)
                  .Append('\n');
            }

            return sb.ToString();
        }

        static void SnapshotSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 12. снимки en/ru; подписи не обрезаны и не наложены, шрифт формы ===");
            SetSource(doc, true);
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                Language(lang);
                using (var report = new FSAReportView(mainForm))
                using (Form host = Host(report, 320, 640))
                {
                    report.ProbeConsumer = true;
                    report.SetDocument(doc);
                    WaitIdle(doc.FsaSession);
                    report.RefreshReport();
                    Application.DoEvents();

                    string path = Path.Combine(outDir, "b15-report-" + lang.Substring(0, 2) + ".png");
                    using (var bmp = new Bitmap(host.ClientSize.Width, host.ClientSize.Height))
                    {
                        report.DrawToBitmap(bmp, new Rectangle(Point.Empty, host.ClientSize));
                        bmp.Save(path, ImageFormat.Png);
                    }

                    Console.WriteLine("  {0}: {1}", lang, path);
                    int clipped = 0, overlapped = 0, foreignFont = 0;
                    Layout(report, report, ref clipped, ref overlapped, ref foreignFont);
                    Same(lang + ": обрезанных подписей нет", 0, clipped);
                    Same(lang + ": наложений нет", 0, overlapped);
                    Same(lang + ": подмены шрифта нет", 0, foreignFont);
                    Same(lang + ": шрифт заголовка таблицы — шрифт формы", report.Font.Name, report.ReportTable.HeaderFont.Name);
                    Same(lang + ": таблице досталось место (высота > 150)", true, report.ReportTable.Height > 150);
                    Console.WriteLine("  {0}: таблица {1}×{2}, строк {3}", lang, report.ReportTable.Width,
                                      report.ReportTable.Height, report.ReportTable.TableModel.Rows.Count);

                    // Положительный контроль: подброшенное усечение сторож видит.
                    CheckBox victim = Control<CheckBox>(report, "escapeCheckBox");
                    victim.AutoSize = false;
                    victim.Width = 40;
                    int c2 = 0, o2 = 0, f2 = 0;
                    Layout(report, report, ref c2, ref o2, ref f2);
                    Denies(lang + ": подброшенное усечение сторож видит", c2 == 0);
                    victim.AutoSize = true;

                    Label alien = new Label { Font = new Font("Courier New", 8f), Text = "x", AutoSize = true };
                    report.Controls.Add(alien);
                    int c3 = 0, o3 = 0, f3 = 0;
                    Layout(report, report, ref c3, ref o3, ref f3);
                    Denies(lang + ": подброшенный чужой шрифт сторож видит", f3 == 0);
                    report.Controls.Remove(alien);
                    alien.Dispose();

                    report.SetDocument(null);
                    host.Hide();
                }
            }

            Language("en-US");
        }

        /// <summary>
        /// Обход дерева: подпись обрезана, если контрол уже своей предпочтительной
        /// ширины или вылезает за клиентскую область родителя; наложение —
        /// пересечение прямоугольников братьев; чужой шрифт — не шрифт формы.
        /// </summary>
        static void Layout(Control form, Control parent, ref int clipped, ref int overlapped, ref int foreignFont)
        {
            var kids = new List<Control>();
            foreach (Control c in parent.Controls) kids.Add(c);
            for (int i = 0; i < kids.Count; i++)
            {
                Control c = kids[i];
                if (c is ButtonBase || c is Label)
                {
                    Size want = c.GetPreferredSize(Size.Empty);
                    if (c.Width < want.Width || c.Right > parent.ClientSize.Width || c.Bottom > parent.ClientSize.Height)
                    {
                        clipped++;
                        Console.WriteLine("   ⛔ обрезано: {0} «{1}» {2} при нужных {3} в {4}", c.Name, c.Text, c.Size, want, parent.ClientSize);
                    }
                }

                if (c is GroupBox)
                {
                    foreach (Control inner in c.Controls)
                    {
                        if (inner.Bottom > c.ClientSize.Height - c.Padding.Bottom + 1 || inner.Right > c.ClientSize.Width)
                        {
                            clipped++;
                            Console.WriteLine("   ⛔ содержимое группы {0} не помещается: {1} в {2}", c.Name, inner.Bounds, c.ClientSize);
                        }
                    }
                }

                for (int j = i + 1; j < kids.Count; j++)
                {
                    if (c.Bounds.IntersectsWith(kids[j].Bounds) && c.Visible && kids[j].Visible)
                    {
                        overlapped++;
                        Console.WriteLine("   ⛔ наложение: {0} и {1}", c.Name, kids[j].Name);
                    }
                }

                if (c.Font.Name != form.Font.Name)
                {
                    foreignFont++;
                    Console.WriteLine("   ⛔ чужой шрифт: {0} — {1}", c.Name, c.Font.Name);
                }

                Layout(form, c, ref clipped, ref overlapped, ref foreignFont);
            }
        }

        // ------------------------------------------------------------------
        // Оснастка
        // ------------------------------------------------------------------

        static void SetSource(DocEnergySpectrum doc, bool nucBase)
        {
            var cfg = (FWHMPeakDetectionMethodConfig)doc.ActiveResultData.PeakDetectionMethodConfig;
            cfg.DbLookupsForFsa = nucBase;
            cfg.ChainEquilibrium = true;
        }

        /// <summary>Подложить готовый результат в сеанс с отпечатком, чтобы потребитель не заказал счёт.</summary>
        static void Plant(FsaAnalysisSession session, FsaResult result, string stamp)
        {
            Field(session, "result").SetValue(session, result);
            Field(session, "stamp").SetValue(session, stamp);
            Field(session, "running").SetValue(session, false);
            Field(session, "status").SetValue(session, null);
        }

        static Form Host(FSAReportView report, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            report.TopLevel = false;
            report.Dock = DockStyle.Fill;
            host.Controls.Add(report);
            report.Show();
            host.Show();
            Application.DoEvents();
            return host;
        }

        static T Control<T>(FSAReportView report, string name) where T : System.Windows.Forms.Control
        {
            FieldInfo f = typeof(FSAReportView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                Console.WriteLine("  ⛔ поля «{0}» у окна НЕТ — проба смотрит не туда", name);
                bad++;
                return null;
            }

            return (T)f.GetValue(report);
        }

        static string Text(FSAReportView report, int row, int cell)
        {
            return report.ReportTable.TableModel.Rows[row].Cells[cell].Text;
        }

        static FieldInfo Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f;
            }

            throw new InvalidOperationException("нет поля " + name + " у " + target.GetType().Name);
        }

        static object Invoke(object target, string name, params object[] args)
        {
            MethodInfo m = null;
            for (Type t = target.GetType(); t != null && m == null; t = t.BaseType)
            {
                m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }

            if (m == null)
            {
                throw new InvalidOperationException("нет метода " + name + " у " + target.GetType().Name);
            }

            return m.Invoke(target, args);
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

            Console.WriteLine("  ⛔ сеанс не дошёл до покоя за отведённое время");
            bad++;
            return false;
        }

        static void Add(double[] target, double[] source)
        {
            if (source == null) return;
            for (int i = 0; i < target.Length && i < source.Length; i++) target[i] += source[i];
        }

        static double RelDiff(double[] a, double[] b)
        {
            if (a == null || b == null) return a == b ? 0.0 : 1.0;
            double diff = 0.0, scale = 0.0;
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                double x = i < a.Length ? a[i] : 0.0, y = i < b.Length ? b[i] : 0.0;
                diff = Math.Max(diff, Math.Abs(x - y));
                scale = Math.Max(scale, Math.Max(Math.Abs(x), Math.Abs(y)));
            }

            return scale > 0.0 ? diff / scale : 0.0;
        }

        static bool Trimmed(string text)
        {
            return text.Contains("…") || text.EndsWith("...", StringComparison.Ordinal);
        }

        static void Language(string name)
        {
            var culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            BecquerelMonitor.Properties.Resources.Culture = culture;
        }

        /// <summary>
        /// Документ из файла корпуса, как в окне: форма документа без показа,
        /// спектр из файла, прибор по правилу проб (`S82`), ПШПВ умолчанием,
        /// пики найдены — ими подписывается состав.
        /// </summary>
        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides)
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

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            if (rd.MeasurementController == null)
            {
                rd.MeasurementController = new MeasurementController(null, rd);
            }

            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}, кривая {3}", Path.GetFileName(path),
                              rd.DetectedPeaks.Count, rd.EnergySpectrum.NumberOfChannels,
                              rd.Efficiency != null ? rd.Efficiency.Name : "(нет)");
            return doc;
        }

        /// <summary>Строка из СОБСТВЕННОЙ `.resx` окна — тем же путём, каким её берёт окно.</summary>
        static readonly ComponentResourceManager ViewResources = new ComponentResourceManager(typeof(FSAReportView));

        static string Own(string key)
        {
            return ViewResources.GetString(key) ?? key;
        }

        /// <summary>
        /// (`A249`) ОЖИДАЕМЫЙ СОСТАВ строк таблицы по роду, выведенный из строк
        /// модели. Строки ложатся в таблицу НЕ ОДНА В ОДНУ (`A247`): перед
        /// первой строкой блока качества встают ЧЕРТА и ЗАГОЛОВОК, а строка
        /// качества разворачивается в χ²/ndf и по строке на каждую пометку.
        /// Пометок всегда три (матрица, кривая, суммирование) плюс строка
        /// множителя `σ×` (`A281`, стоит сразу под χ²/ndf) плюс отвергнутый
        /// фон, край сетки дрейфа и подавленный состав — по признакам
        /// результата.
        /// </summary>
        static List<FsaReportRowKind> ExpectedKinds(FSAReportView report, out int marks)
        {
            List<FsaReportRow> model = report.BuildRows();
            FsaResult result = report.Presentation != null ? report.Presentation.Source : null;
            marks = 0;
            if (result != null)
            {
                // ⛔ (`A281`) ЧЕТЫРЕ, А НЕ ТРИ: под χ²/ndf с 10.09.2026 стоит
                // строка множителя погрешностей `σ×` — она не пометка, но
                // строка блока, и в СОСТАВЕ по `Tag.Kind` неотличима от
                // пометки. Число здесь и есть тот сторож, который поймает её
                // пропажу: подпись и значение судит `BlockProblems`.
                marks = 4;
                // (`S44`, 06.09.2026) Фон подан и не взят — причина словами
                // своей строкой, как край сетки и подавленный состав.
                if (result.BackgroundRejected != null) marks++;
                if (result.DriftOnGridEdge) marks++;
                if (result.CompositionSuppressed) marks++;
            }

            var kinds = new List<FsaReportRowKind>();
            bool opened = false;
            foreach (FsaReportRow row in model)
            {
                bool block = row.Kind == FsaReportRowKind.NoBackground
                             || row.Kind == FsaReportRowKind.Residual
                             || row.Kind == FsaReportRowKind.Quality;
                if (!opened && block)
                {
                    opened = true;
                    kinds.Add(FsaReportRowKind.Quality);   // черта
                    kinds.Add(FsaReportRowKind.Quality);   // заголовок
                }

                kinds.Add(row.Kind);
                if (row.Kind == FsaReportRowKind.Quality)
                {
                    for (int i = 0; i < marks; i++) kinds.Add(FsaReportRowKind.Quality);
                }
            }

            return kinds;
        }

        /// <summary>Роды строк, как их читает окно и пробы, — из `Tag` строки таблицы.</summary>
        static List<FsaReportRowKind> TableKinds(FSAReportView report)
        {
            var kinds = new List<FsaReportRowKind>();
            foreach (Row row in report.ReportTable.TableModel.Rows)
            {
                kinds.Add(((FsaReportRow)row.Tag).Kind);
            }

            return kinds;
        }

        static string Kinds(List<FsaReportRowKind> kinds)
        {
            return string.Join(" ", kinds);
        }

        /// <summary>
        /// (`A249`) Строки ВНЕ блока качества обязаны быть у таблицы теми же,
        /// что у модели: род, подпись и значение один в один, и то же самое —
        /// в ячейках. Это и есть прежняя проверка «ничего не потеряно по
        /// высоте», только сверяется состав, а не число строк. Подпись
        /// подменяется ровно у невязки (`A247`), значение — никогда.
        /// </summary>
        static string BodyMismatch(FSAReportView report)
        {
            var want = new List<string>();
            foreach (FsaReportRow row in report.BuildRows())
            {
                if (row.Kind != FsaReportRowKind.Quality)
                {
                    want.Add(row.Kind + "|" + (row.Name ?? string.Empty) + "|" + (row.Value ?? string.Empty));
                }
            }

            var have = new List<string>();
            var cells = new List<string>();
            foreach (Row row in report.ReportTable.TableModel.Rows)
            {
                var model = (FsaReportRow)row.Tag;
                if (model.Kind == FsaReportRowKind.Quality)
                {
                    continue;
                }

                have.Add(model.Kind + "|" + (model.Name ?? string.Empty) + "|" + (model.Value ?? string.Empty));
                string caption = model.Kind == FsaReportRowKind.Residual ? Own("FSAReport_ResidualRow") : model.Name ?? string.Empty;
                if (row.Cells[1].Text != caption || row.Cells[2].Text != (model.Value ?? string.Empty))
                {
                    cells.Add("ячейки «" + row.Cells[1].Text + " | " + row.Cells[2].Text
                              + "» вместо «" + caption + " | " + (model.Value ?? string.Empty) + "»");
                }
            }

            if (want.Count != have.Count)
            {
                return "строк вне блока " + have.Count + " вместо " + want.Count;
            }

            for (int i = 0; i < want.Count; i++)
            {
                if (want[i] != have[i])
                {
                    return "строка " + i + ": «" + have[i] + "» вместо «" + want[i] + "»";
                }
            }

            return cells.Count == 0 ? string.Empty : string.Join("; ", cells);
        }

        /// <summary>Значения строк блока качества одной строкой — для сверки чисел между сборками.</summary>
        static string QualityValues(FSAReportView report)
        {
            var parts = new List<string>();
            foreach (Row row in report.ReportTable.TableModel.Rows)
            {
                var model = (FsaReportRow)row.Tag;
                if (model.Kind == FsaReportRowKind.Quality || model.Kind == FsaReportRowKind.Residual)
                {
                    if (row.Cells[2].Text.Length > 0) parts.Add(row.Cells[2].Text);
                }
            }

            return string.Join(";", parts);
        }

        /// <summary>
        /// (`A244`) ВИД ЧИСЕЛ БЛОКА КАЧЕСТВА: группировки разрядов нет вовсе
        /// (решение Amber 05.09.2026). Четыре места печати судятся ПОИМЁННО, и
        /// судится ВИД строки, а не равенство плеч разных культур: разделитель
        /// групп у формата `n` одинаков на всех культурах — приёмка по
        /// равенству плеч его уже пропустила однажды.
        /// </summary>
        static void NumberLookSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== 0. вид чисел блока качества: разделителя разрядов НЕТ ни на одной культуре (A244) ===");

            // ⛔ Раздел ходит по культурам и ОБЯЗАН вернуть их как было
            // (мерено на себе 05.09.2026): `Resources.Culture`, выставленный
            // явно, СИЛЬНЕЕ `CurrentUICulture`, и оставленный `en-US`
            // пережил `Program.ApplyLanguage` в конструкторе `MainForm` — язык
            // всего прогона сменился, а с ним и состав разбора (у документа A
            // стало 27 строк вместо 23). Утечка глобального состояния из
            // раздела в раздел — это чужие числа у соседа.
            CultureInfo hadCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo hadUi = Thread.CurrentThread.CurrentUICulture;
            CultureInfo hadResources = BecquerelMonitor.Properties.Resources.Culture;

            // Числа ≥ 1000 нарочно: ниже тысячи `n` и `f` неотличимы.
            var scene = new FsaResult
            {
                Chi2Ndf = 12345.6789,
                ResidualExcessShare = 12.345,
                ResidualMissingShare = 98.7654,
                BackgroundUsed = true
            };
            var layer = new FsaStackLayer { Name = "Cs-137", Kind = FsaComponentKind.Single, SharePercent = 1234.5 };

            foreach (string name in new[] { "ru-RU", "de-DE", "en-US" })
            {
                Language(name);
                FsaPresentation p = FsaPresentationBuilder.Build(scene, FsaGrouping.Daughters, false);
                string residual = null, chi = null;
                foreach (FsaReportRow row in p.Rows)
                {
                    if (row.Kind == FsaReportRowKind.Residual) residual = row.Value;
                    else if (row.Kind == FsaReportRowKind.Quality) chi = row.Value;
                }

                string share = FsaPresentationBuilder.ShareText(layer);
                Console.WriteLine("  {0}: невязка «{1}» | χ²/ndf «{2}» | доля слоя «{3}»", name, residual, chi, share);

                Same(name + ": невязка, лишнее (FsaPresentationBuilder.cs:457) — 1234.5 без группировки",
                     true, residual != null && residual.Contains("1234.5"));
                Same(name + ": невязка, нехватка (FsaPresentationBuilder.cs:458) — 9876.5 без группировки",
                     true, residual != null && residual.Contains("9876.5"));
                Same(name + ": χ²/ndf (FsaPresentationBuilder.cs:468)", "12345.68", chi);
                Same(name + ": доля слоя, ShareText (FsaPresentationBuilder.cs:566)", "1234.50%", share);
                Same(name + ": в невязке разделителя разрядов нет", string.Empty, Grouped(residual));
                Same(name + ": в χ²/ndf разделителя разрядов нет", string.Empty, Grouped(chi));
                Same(name + ": в доле слоя разделителя разрядов нет", string.Empty, Grouped(share));
            }

            // Контроль самой мерки: `n2` на инвариантной культуре — то, что было
            // до правки, — она обязана назвать разделителем разрядов.
            string old = (1234.5).ToString("n2", CultureInfo.InvariantCulture);
            Console.WriteLine("  контроль мерки: прежний формат n2 на инвариантной культуре даёт «{0}»", old);
            Denies("контроль: прежний вид «1,234.50» мерка не принимает", Grouped(old).Length == 0);

            Thread.CurrentThread.CurrentCulture = hadCulture;
            Thread.CurrentThread.CurrentUICulture = hadUi;
            BecquerelMonitor.Properties.Resources.Culture = hadResources;
        }

        /// <summary>
        /// Разделитель разрядов в строке: цифра, знак-разделитель и ровно три
        /// цифры за ним. Пусто — группировки нет. Ищутся ВСЕ знаки, какими
        /// культуры разделяют разряды: запятая, пробел, неразрывный и узкий
        /// неразрывный, апостроф, точка.
        /// </summary>
        static string Grouped(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            const string seps = ",. \u00a0\u202f\u2009'\u2019";
            for (int i = 1; i + 3 < text.Length; i++)
            {
                if (!char.IsDigit(text[i - 1]) || seps.IndexOf(text[i]) < 0) continue;
                if (!char.IsDigit(text[i + 1]) || !char.IsDigit(text[i + 2]) || !char.IsDigit(text[i + 3])) continue;
                if (i + 4 < text.Length && char.IsDigit(text[i + 4])) continue;
                return "«" + text.Substring(i - 1, 5) + "» в «" + text + "»";
            }

            return string.Empty;
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-76} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }

        static void Denies(string what, bool passedButShouldNot)
        {
            Console.WriteLine("  {0} {1,-76} {2}", passedButShouldNot ? "⛔ " : "ok  ", what,
                              passedButShouldNot ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (passedButShouldNot) bad++;
        }
    }
}
