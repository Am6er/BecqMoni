using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
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
    ///   5. СЕМЬ РОДОВ СТРОК (критерий 5) и таблица без потерь по высоте.
    ///   6. СТРОКА КАЧЕСТВА (критерий 6): самая тяжёлая сцена в `ru-RU` и
    ///      `en-US` — текст ячейки равен тексту модели, все пять пометок, без
    ///      многоточия; число в своей колонке.
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

            MainForm mainForm = new MainForm();
            DocEnergySpectrum thorium = Open(spectrumPath, nuclides);
            DocEnergySpectrum control = Open(controlPath, nuclides);
            if (thorium == null || control == null)
            {
                return 2;
            }

            Hygiene();
            WindowSection(mainForm, thorium, control);
            EntrySection(mainForm, control);      // у контроля есть кривая: дверь FSA открыта без вопросов
            OneResultSection(mainForm, thorium);
            RowKindsSection(mainForm, thorium, control);
            StatesSection(mainForm, thorium);
            QualitySection(mainForm, thorium);
            GroupingAndFlagsSection(mainForm, thorium);
            ParentsSection(mainForm, thorium, control);
            NeighbourSection(mainForm, thorium, control);
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

                // Таблица не отбрасывает по высоте: строк в XPTable = строк модели при
                // малом окне; положительный контроль — счётчик видимых строк меньше.
                int modelRows = report.BuildRows().Count;
                using (Form host = Host(report, 320, 160))
                {
                    report.RefreshReport();
                    Application.DoEvents();
                    int visible = report.ReportTable.GetVisibleRowCount();
                    Console.WriteLine("  окно 320×160: строк модели {0}, в таблице {1}, видимых без прокрутки {2}",
                                      modelRows, report.ReportTable.TableModel.Rows.Count, visible);
                    Same("малое окно: строк в таблице = строк модели", modelRows, report.ReportTable.TableModel.Rows.Count);
                    Same("контроль: видимых без прокрутки МЕНЬШЕ — значит, прокрутка есть, а не потеря",
                         true, visible < modelRows);
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
            Console.WriteLine("=== состояния таблицы: считается / пересчёт / ошибка ===");
            using (var report = new FSAReportView(mainForm))
            {
                var session = new FsaAnalysisSession();
                ResultData rd = doc.ActiveResultData;

                Field(session, "running").SetValue(session, true);
                Field(session, "status").SetValue(session, BecquerelMonitor.Properties.Resources.FSACalculating);
                report.SetProbeSource(session, rd);
                Same("первый расчёт идёт: одна строка", 1, report.ReportTable.TableModel.Rows.Count);
                Same("и это «считается»", BecquerelMonitor.Properties.Resources.FSACalculating, Text(report, 0, 1));

                Plant(session, doc.FsaSession.Result, "old");
                Field(session, "running").SetValue(session, true);
                report.RefreshReport();
                int rows = report.ReportTable.TableModel.Rows.Count;
                Same("пересчёт при старом результате: первая строка — «пересчёт»",
                     BecquerelMonitor.Properties.Resources.FSAReportRecalculating, Text(report, 0, 1));
                Same("и старые строки остались (их больше одной)", true, rows > 1);
                Same("строка пересчёта оранжевая", Color.DarkOrange, report.ReportTable.TableModel.Rows[0].Cells[1].ForeColor);

                Field(session, "running").SetValue(session, false);
                report.RefreshReport();
                Same("расчёт завершён: строка пересчёта убрана", rows - 1, report.ReportTable.TableModel.Rows.Count);
                Same("первая строка — состав", FsaReportRowKind.Layer, ((FsaReportRow)report.ReportTable.TableModel.Rows[0].Tag).Kind);

                Field(session, "result").SetValue(session, null);
                Field(session, "status").SetValue(session, "ОШИБКА: причина такая-то");
                report.RefreshReport();
                Same("ошибка без результата: одна строка", 1, report.ReportTable.TableModel.Rows.Count);
                Same("и это текст причины", "ОШИБКА: причина такая-то", Text(report, 0, 1));
                report.SetDocument(null);
            }
        }

        // ------------------------------------------------------------------
        // 6. СТРОКА КАЧЕСТВА
        // ------------------------------------------------------------------

        static void QualitySection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 6. строка качества: все пометки, обе культуры, без многоточия, в ячейке XPTable ===");
            const double chi2 = 2.94;
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
                    string full = FsaPresentationBuilder.QualityText(scene, true);
                    Row quality = null;
                    foreach (Row row in report.ReportTable.TableModel.Rows)
                    {
                        if (((FsaReportRow)row.Tag).Kind == FsaReportRowKind.Quality) quality = row;
                    }

                    Same(lang + ": строка качества в таблице есть", true, quality != null);
                    if (quality == null) continue;
                    string text = quality.Cells[1].Text;
                    Console.WriteLine("  {0}: «{1}» | {2}", lang, text, quality.Cells[2].Text);
                    Same(lang + ": текст ячейки = текст модели", full, text);
                    Same(lang + ": без многоточия", false, Trimmed(text));
                    Same(lang + ": ячейка переносится по словам (не усекается)", true, quality.Cells[1].WordWrap);
                    foreach (string mark in new[] { "FSASuppressedMark", "FSAOldMatrixMark", "FSACascadeMark",
                                                    "FSANoEfficiencyMark", "FSADriftEdgeMark" })
                    {
                        string m = BecquerelMonitor.Properties.Resources.ResourceManager.GetString(mark, CultureInfo.CurrentUICulture);
                        Same(lang + ": пометка " + mark + " на месте", true, text.Contains(m.Trim()));
                    }

                    Same(lang + ": число χ²/ndf в своей колонке", chi2.ToString("n2", CultureInfo.CurrentCulture), quality.Cells[2].Text);
                    Same(lang + ": числа в тексте пометок нет", false, text.Contains(quality.Cells[2].Text));

                    // Положительный контроль: урезанный многоточием текст сторож НЕ принимает.
                    string cut = full.Substring(0, full.Length / 2) + "…";
                    Denies(lang + ": урезанный многоточием текст сторож не принимает", !Trimmed(cut));
                }
            }

            Language("en-US");
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
