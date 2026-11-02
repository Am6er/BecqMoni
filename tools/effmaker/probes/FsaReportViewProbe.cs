using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
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
    ///                        [--out=&lt;каталог снимков&gt;] [--store=&lt;склад матриц по guid кривой&gt;]
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
    ///      ⛔ (П108) Собранная сцена подкладывается под НЫНЕШНИЙ отпечаток
    ///      спектра, чтобы окно-потребитель не заказало живой счёт, который
    ///      молча подменял сцену (гонка П107 §9.1); покой сеанса
    ///      утверждается ПЕРЕД снятием подписей. Подпись невязки ждётся с
    ///      пометками `A300`/`S174`, вычисленными ИЗ РЕЗУЛЬТАТА тем же
    ///      правилом, что у `RefreshReport`, — и на сцене (пометок нет), и на
    ///      живом результате (есть); контроли: подброшенная пометка, голая
    ///      подпись при живом результате, чужое число каналов — отказ.
    ///   6. БЛОК «КАЧЕСТВО РАЗБОРА» (критерий 6, переписан под `A247`): черта,
    ///      заголовок, χ²/ndf своей строкой, СРАЗУ ПОД НИМ множитель `σ×`
    ///      (`A281`, решение Amber 10.09.2026) и по строке на каждую пометку —
    ///      в `ru-RU` и `en-US`, с полными подписями и без многоточия. Строки
    ///      привязки шкалы (`AMBER17`) ждутся ПО РЕЗУЛЬТАТУ сцены: при живой
    ///      матрице — «опор нет» либо число опор + усиление + ноль (три сцены:
    ///      без матрицы, с матрицей без опор, с двумя опорами).
    ///   7. ГРУППИРОВКА И ФЛАГИ (критерий 7): родители/дочерние не меняют
    ///      отпечаток и не запускают счёт; расчётный флаг — ровно один запуск
    ///      и одно событие, отпечаток другой, конфигурация спектра записана,
    ///      а умолчание прибора НЕ тронуто (`AMBER212`, 07.10.2026: окно
    ///      отчёта прибор не пишет — в приложении настройка общая).
    ///   8. РОДИТЕЛИ (критерий 8): недоступны без NucBase+равновесия с
    ///      подсказкой; при допустимом режиме сумма родительских лент и долей
    ///      равна сумме дочерних с машинным допуском.
    ///  11. СОСЕДНИЙ ДОКУМЕНТ (критерий 11): флаг, переключённый в одном
    ///      документе, не меняет копию конфигурации другого открытого.
    ///  12. СНИМКИ (критерий 12): `en` и `ru` в `--out`; по модели — ни одна
    ///      подпись не обрезана и не наложена, шрифт формы у всех, заголовок
    ///      таблицы тем же шрифтом.
    ///  14. ОТКАЗ НАЗЫВАЕТ ПРИЧИНУ (`A312`, П118 21.09.2026): сцена Amber
    ///      `Am-241` на контроле — состав из NucBase без нуклидов, один образ
    ///      рентгена кристалла, малая матрица снимает его, библиотека пуста;
    ///      состояние сеанса и строка таблицы — `FSALibraryEmptied` в `en` и
    ///      `ru`, не общее `FSANotPossible`. Контроли: без матрицы разбор
    ///      идёт; причина на анализаторе — `LibraryEmptied` с подробностью;
    ///      остальные причины (вход, геометрия, каналы, полоса) — каждая
    ///      своим членом и своими словами.
    ///  15. СПЕКТР БЕЗ ВРЕМЕНИ — ОТКАЗ С ПРИЧИНОЙ (`AMBER51`, П120 22.09.2026):
    ///      контроль с обнулёнными `LiveTime` и `MeasurementTime` — анализатор
    ///      отказывает `NoLiveTime` с подробностью, сеанс документа и строка
    ///      таблицы несут `FSANoLiveTime` в `en` и `ru` (не общее
    ///      `FSANotPossible`); до правки знаменатель молча подменялся 1 с.
    ///      Контроли: тот же спектр с одним лишь полным временем разбирается
    ///      (живое — ноль, знаменатель — полное, `AMBER35`); с возвращённым
    ///      временем скорости и доли равны разбору нетронутого клона до бита.
    ///  16. СУММ-ПИКИ ПОИМЁННО (`AMBER286`, 02.11.2026): под строкой «— сумм-пик» —
    ///      строки пиков с отступом, по энергии; пики ближе FWHM — одной строкой со
    ///      средневзвешенной энергией и всеми парами в подсказке; только заметные
    ///      (площадь ≥ 3σ счёта окна ±FWHM). Собранная сцена тория (Ac-228, Tl-208,
    ///      пара, слитая пара, тройка, слабый пик) в обоих языках и в родительской
    ///      группировке; положительный контроль — слабый пик строки не получает, тот же
    ///      пик сильнее порога — получает; без дисперсии фита строк нет. Живой разбор
    ///      `--spectrum` с матрицей из `--store=` (в память, `AMBER202`): строки пиков у
    ///      каждого слоя с сумм-пиками — ровно заметные группы, отсчёты строк сходятся с
    ///      нарисованным подслоем сумм.
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

        /// <summary>
        /// (`AMBER286`) Склад матриц корпуса по guid кривой (`--store=`): живой разбор тория с
        /// матрицей в разделе сумм-пиков; null — живая часть раздела не мерится (сказано
        /// словами), собранная сцена мерится всегда.
        /// </summary>
        static string storeDir;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: раздел 14 собирает
            // анализатор руками, и проба обязана сказать, чем он отличается от
            // поставочного (`check_probe_tuning`); правило живёт довеском
            // `FsaTuningReport.cs`, здесь только два вызова.
            FsaTuningReport.Snapshot();

            string spectrumPath = null, controlPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--control=", StringComparison.Ordinal)) controlPath = a.Substring(10);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) storeDir = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null || controlPath == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл с рядом Th-232> и --control=<файл без ряда>");
                return 2;
            }

            // (П105, 18.09.2026) КАТАЛОГ СНИМКОВ — ДО ВСЕГО. `Bitmap.Save` в
            // несуществующий каталог падает не «нет каталога», а безликим
            // `ExternalException: A generic error occurred in GDI+` в разделе
            // 12 — после полминуты счёта и без приговора «НЕ СОШЛОСЬ» (код
            // 0xE0434352, мерено П104). Каталог создаётся здесь; не создался —
            // отказ словами и кодом 2 («мерить нечем»), как у гейта геометрии.
            try
            {
                Directory.CreateDirectory(outDir);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("⛔ МЕРИТЬ НЕЧЕМ: каталог снимков --out={0} не создаётся: {1}", outDir, e.Message);
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
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
            CurveCauseSection(mainForm, thorium);
            UncoveredSection(mainForm, thorium);
            BandWarningSection(mainForm, thorium);
            FitBoundSection(mainForm, thorium);
            GroupingAndFlagsSection(mainForm, thorium);
            ParentsSection(mainForm, thorium, control);
            NeighbourSection(mainForm, thorium, control);
            RepeatabilitySection(mainForm, thorium, control);
            SnapshotSection(mainForm, thorium);
            RefusalSection(mainForm, controlPath, nuclides);
            NoLiveTimeSection(mainForm, controlPath, nuclides);
            SumPeakRowsSection(mainForm, thorium, spectrumPath, nuclides);

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
                     report.ReportTable.TableModel.Rows[0].Cells[0].Text);
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
                //
                // ⛔ СЦЕНА ПОДКЛАДЫВАЕТСЯ ПОД НЫНЕШНИЙ ОТПЕЧАТОК СПЕКТРА (П108,
                // 19.09.2026). Окно здесь — потребитель (`ProbeConsumer`), и
                // `SetProbeSource` → `Consume` → `EnsureUpToDate` заказывает
                // счёт всякий раз, когда отпечаток сеанса не равен отпечатку
                // спектра. С выдуманным отпечатком («scene») сеанс сцены
                // НАЧИНАЛ НАСТОЯЩИЙ СЧЁТ `--spectrum` (измерено: `IsRunning`,
                // `RunCount=1` через 8 мс после `SetProbeSource`), и через
                // секунду-другую живой результат молча ПОДМЕНЯЛ сцену: 18 строк
                // вместо 34, ни сумм-пиков, ни свёрнутых, а у невязки —
                // пометки `A300`/`S174` «показ подрезан на 121 каналах, от
                // 30 кэВ». Кто успевал первым — счёт или проверки малого окна
                // ниже, — решала загрузка машины: под `check_all` П107 проба
                // была красна 3 из 3, сама по себе зелена 3 из 3 (П107 §9.1).
                // Отпечаток сцены равен отпечатку спектра — и `EnsureUpToDate`
                // считает её свежей; что счёта нет и результат — сама сцена,
                // утверждается ниже ЯВНО, перед снятием подписей.
                ResultData sceneData = a.ActiveResultData;
                int[] sceneRaw = sceneData.EnergySpectrum.Spectrum;
                FsaResult scene = SceneAllKinds(a.FsaSession.Result);
                var synthetic = new FsaAnalysisSession();
                Plant(synthetic, scene, FsaAnalysisSession.BuildStamp(sceneData, sceneData.BackgroundEnergySpectrum != null));
                report.SetProbeSource(synthetic, sceneData);
                Same("сцена под нынешним отпечатком: окно-потребитель счёт НЕ заказало (RunCount)", 0, synthetic.RunCount);
                Same("сцена: сеанс в покое", false, synthetic.IsRunning);
                Same("сцена: результат сеанса — сама сцена, не живой пересчёт", true, ReferenceEquals(synthetic.Result, scene));
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

                    // (П108) Состояние сеанса ПЕРЕД снятием подписей — явно: ни
                    // счёта в пути, ни чужого результата. Иначе ячейки читаются
                    // у того, кто успел первым, и зелёный зависит от того, кто
                    // запустил пробу.
                    Same("малое окно: сеанс сцены в покое перед снятием подписей", false, synthetic.IsRunning);
                    Same("малое окно: в окне по-прежнему сцена (счёт её не подменил)", true, ReferenceEquals(synthetic.Result, scene));
                    int marks;
                    string want = Kinds(ExpectedKinds(report, out marks));
                    string have = Kinds(TableKinds(report));
                    int visible = report.ReportTable.GetVisibleRowCount();
                    int rowsInTable = report.ReportTable.TableModel.Rows.Count;
                    Console.WriteLine("  окно 320×160: строк модели {0}, в таблице {1} (черта, заголовок и {2} пометки блока), видимых без прокрутки {3}",
                                      report.BuildRows().Count, rowsInTable, marks, visible);
                    Same("малое окно: СОСТАВ строк таблицы по Tag.Kind = составу модели с развёрнутым блоком",
                         want, have);
                    Console.WriteLine("  подпись невязки, ожидаемая по сцене: «{0}»", ResidualCaption(scene, sceneRaw));
                    Same("малое окно: строки ВНЕ блока качества — один в один с моделью, и то же в ячейках",
                         string.Empty, BodyMismatch(report, scene, sceneRaw));
                    Same("контроль: видимых без прокрутки МЕНЬШЕ — значит, прокрутка есть, а не потеря",
                         true, visible < rowsInTable);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПОДПИСИ НЕВЯЗКИ (П108). У сцены
                    // фона нет — подрезать показ нечего, пометок быть не должно.
                    // Ячейке подбрасывается пометка подрезки: сверка обязана
                    // отказать. Портится ровно то, что читает проверка, — текст
                    // ячейки, — и тут же возвращается.
                    Row residualRow = ResidualTableRow(report);
                    Same("контроль: строка невязки в таблице малого окна есть", true, residualRow != null);
                    if (residualRow != null)
                    {
                        string keptCaption = residualRow.Cells[0].Text;
                        residualRow.Cells[0].Text = ResidualCaption(scene, 1);
                        Denies("контроль: подброшенная пометка подрезки у невязки сцены сверку не проходит",
                               BodyMismatch(report, scene, sceneRaw).Length == 0);
                        residualRow.Cells[0].Text = keptCaption;
                        Same("после возврата подписи ячейки снова сходятся", string.Empty, BodyMismatch(report, scene, sceneRaw));
                    }

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

                    // ⛔ ЖИВОЙ РЕЗУЛЬТАТ В ТОМ ЖЕ ОКНЕ (П108). У сцены пометок у
                    // невязки нет, и ветка «голая подпись + пометки `A300`/`S174`»
                    // на ней не мерится — а именно её П105 сверяла с голой
                    // строкой ресурса и была права лишь пока в окне не было
                    // живого результата. Живой разбор `--spectrum` (фон вычтен —
                    // есть подрезка показа; `Min_Range` прибора выше первого
                    // канала — есть пол) даёт подпись с пометками; ожидание
                    // строится ИЗ РЕЗУЛЬТАТА тем же правилом, что у
                    // `RefreshReport` (`ResidualCaption`), а не текстом руками.
                    // Сеанс дожидается покоя ДО снятия подписей.
                    FsaAnalysisSession liveSession = a.FsaSession;
                    report.SetProbeSource(liveSession, sceneData);
                    WaitIdle(liveSession);
                    report.RefreshReport();
                    Application.DoEvents();
                    FsaResult live = liveSession.Result;
                    Same("живой результат: есть, сеанс в покое перед снятием подписей", true,
                         live != null && !liveSession.IsRunning);
                    string liveCaption = ResidualCaption(live, sceneRaw);
                    Console.WriteLine("  подпись невязки, ожидаемая по живому результату: «{0}» (подрезано каналов {1}, пол {2} кэВ с канала {3}, первый канал фита {4})",
                                      liveCaption, live != null ? live.ClampedChannels(sceneRaw) : -1,
                                      live != null ? live.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture) : "?",
                                      live != null ? live.ResidualFloorChannel : -1, live != null ? live.FirstChannel : -1);
                    Same("живой результат несёт пометки у невязки (подрезка показа и/или пол Min_Range) — иначе ветка пометок не мерится",
                         true, liveCaption != Own("FSAReport_ResidualRow"));
                    Same("малое окно, живой результат: строки ВНЕ блока — один в один с моделью, подпись невязки с пометками — как строит RefreshReport",
                         string.Empty, BodyMismatch(report, live, sceneRaw));

                    // Контроли обеих сторон: (а) голая подпись без пометок — ровно
                    // то, что П105 принимала за верное, — обязана отказать; (б)
                    // пометка с чужим числом каналов — тоже.
                    Row liveRow = ResidualTableRow(report);
                    Same("контроль: строка невязки живого результата в таблице есть", true, liveRow != null);
                    if (liveRow != null && live != null)
                    {
                        string keptCaption = liveRow.Cells[0].Text;
                        liveRow.Cells[0].Text = Own("FSAReport_ResidualRow");
                        Denies("контроль: голая подпись невязки без пометок при живом результате сверку не проходит",
                               BodyMismatch(report, live, sceneRaw).Length == 0);
                        liveRow.Cells[0].Text = ResidualCaption(live, live.ClampedChannels(sceneRaw) + 1);
                        Denies("контроль: пометка с чужим числом подрезанных каналов сверку не проходит",
                               BodyMismatch(report, live, sceneRaw).Length == 0);
                        liveRow.Cells[0].Text = keptCaption;
                        Same("после возврата подписи ячейки живого результата снова сходятся", string.Empty,
                             BodyMismatch(report, live, sceneRaw));
                    }

                    host.Hide();
                }

                report.SetDocument(null);
            }
        }

        /// <summary>
        /// Результат, у которого есть строки всех семи родов: слои с сумм-пиками,
        /// именованный и свёрнутый необнаруженные, фон не применён, невязка и
        /// качество. Строится из НАСТОЯЩЕГО результата (слои и модель — его),
        /// подставляются только признаки, которых у живого спектра нет:
        /// суммирование и — с П105 — живая матрица с двумя опорами привязки
        /// (`AMBER17`), чтобы состав блока мерился и с тремя строками опор
        /// (у корпусных спектров пробы матрицы нет, и ветка иначе не ходит).
        /// </summary>
        static FsaResult SceneAllKinds(FsaResult real)
        {
            var result = new FsaResult
            {
                Chi2Ndf = real.Chi2Ndf, Chi2NdfPoisson = real.Chi2NdfPoisson,
                Model = real.Model,
                Continuum = real.Continuum,
                BackgroundUsed = false,
                EfficiencyUsed = real.EfficiencyUsed,
                ResponseMatrixUsed = true,
                ScaleAnchorsUsed = 2,
                Gain = 1.0123,
                AnchorOffsetKev = -0.5,
                CascadeSummingUsed = true,
                ResidualExcessShare = real.ResidualExcessShare,
                ResidualMissingShare = real.ResidualMissingShare,
                // (`AMBER286`) полоса и дисперсия — чтобы у сцены был и род SumPeakLine
                FirstChannel = real.FirstChannel,
                LastChannel = real.LastChannel
            };
            bool lined = false;
            foreach (FsaComponentResult c in real.Components)
            {
                if (c.SumPeakCurve == null && c.Curve != null)
                {
                    var sums = new double[c.Curve.Length];
                    for (int i = 0; i < sums.Length; i++) sums[i] = c.Curve[i] * 0.1;
                    c.SumPeakCurve = sums;
                }

                // (`AMBER286`) Один заметный сумм-пик поимённо — у КОПИИ первого нуклидного
                // компонента: живой результат, из которого сцена сделана, читают и следующие
                // разделы, и строка пика у него появилась бы и там.
                FsaComponentResult add = c;
                if (!lined && c.Kind != FsaComponentKind.Nuisance && c.Curve != null && real.Model != null)
                {
                    add = ShallowCopy(c);
                    add.SumPeakLines = new List<FsaSumPeakLine>
                    {
                        new FsaSumPeakLine
                        {
                            Nuclide = c.Name, FromKev = 795.0, WithKev = 270.2, EnergyKev = 1065.2, Counts = 1000.0,
                            Channel = 0.5 * (real.FirstChannel + real.LastChannel), FwhmChannels = 10.0
                        }
                    };
                    lined = true;
                }

                result.Components.Add(add);
            }

            if (real.Model != null)
            {
                result.FitVariance = new double[real.Model.Length];
                for (int i = 0; i < result.FitVariance.Length; i++) result.FitVariance[i] = 1.0;
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
        // (`AMBER210`, П238) ПИК ВНЕ СОСТАВА — СТРОКА ОТЧЁТА
        // ------------------------------------------------------------------
        /// <summary>
        /// (`AMBER210`; решение Amber 07.10.2026 вопросником, дословно: «Только отчёт FSA, строка
        /// на пик») Синтетический результат с непокрытыми пиками даёт в блоке пометок по строке на
        /// пик: энергия, избыток в процентах, z и линии библиотеки рядом; слово состояния —
        /// красное «нет в сете». Положительные контроли: тот же результат без пиков строки не даёт
        /// вовсе; пик без кандидатов пишет «нет»; ключ вместо перевода — отказ.
        /// </summary>
        static void UncoveredSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== пик вне состава (AMBER210): строка отчёта на каждый пик, без пиков — строки нет ===");
            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                foreach (int peaks in new[] { 0, 1, 2 })
                {
                    // Сцена — как у раздела 6 (блок качества на ней строится): без опор
                    // привязки — у синтетического результата нет их списка, и строки
                    // опор рассыпали бы блок.
                    FsaResult scene = new FsaResult
                    {
                        Chi2Ndf = 2.5, Chi2NdfPoisson = 2.5, BackgroundUsed = true, ResponseMatrixUsed = false,
                        EfficiencyUsed = false, CascadeSummingUsed = true
                    };
                    if (peaks >= 1)
                    {
                        var peak = new FsaUncoveredPeak { EnergyKev = 609.3, WidthKev = 42.0, ExcessCounts = 12345.0, ExcessShare = 0.28, Z = 15.2 };
                        peak.Candidates.Add(new FsaUncoveredCandidate { Nuclide = "Bi-214", EnergyKev = 609.31 });
                        scene.UncoveredPeaks.Add(peak);
                    }
                    if (peaks >= 2)
                    {
                        scene.UncoveredPeaks.Add(new FsaUncoveredPeak { EnergyKev = 351.9, WidthKev = 30.0, ExcessCounts = 5000.0, ExcessShare = 0.18, Z = 9.1 });
                    }
                    var session = new FsaAnalysisSession();
                    Plant(session, scene, "uncovered" + peaks);
                    using (var report = new FSAReportView(mainForm))
                    {
                        // ⛔ НЕ потребитель: потребитель заказал бы у сеанса настоящий счёт
                        // по спектру документа, и подсаженная сцена сменилась бы живым
                        // результатом без пиков вне состава (так и было на первом прогоне
                        // П238: строк 0 вместо 1 и 2). Блок читается с подсаженной сцены,
                        // как в разделе 6.
                        report.SetProbeSource(session, doc.ActiveResultData);
                        var rows = new List<Row>();
                        foreach (Row row in report.ReportTable.TableModel.Rows)
                        {
                            if (row.Cells.Count > 1 && row.Cells[1].Text == Own("FSAReport_UncoveredValue")) rows.Add(row);
                        }
                        Same(lang + ": пиков " + peaks + " — строк «" + Own("FSAReport_UncoveredValue") + "»", peaks, rows.Count);
                        if (rows.Count != peaks)
                        {
                            ShowBlock(report, lang);
                        }
                        if (peaks >= 1 && rows.Count >= 1)
                        {
                            string caption = rows[0].Cells[0].Text;
                            Same(lang + ": строка первого пика называет энергию, избыток, z и линию библиотеки", true,
                                 caption.Contains("609.3") && caption.Contains("28") && caption.Contains("15.2") && caption.Contains("Bi-214 609.3"));
                            Same(lang + ": слово состояния первого пика красное", Paint(BadColor), Paint(rows[0].Cells[1].ForeColor));
                            Denies(lang + ": контроль — ключ вместо перевода в строке пика", caption.Contains("FSAReport_"));
                        }
                        if (peaks >= 2 && rows.Count >= 2)
                        {
                            string second = rows[1].Cells[0].Text;
                            Same(lang + ": пик без кандидатов пишет «" + Own("FSAReport_UncoveredNone") + "»", true,
                                 second.Contains("351.9") && second.EndsWith(Own("FSAReport_UncoveredNone")));
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// (`AMBER269`; решение Amber 02.11.2026 вопросником, дословно: «Да, по признакам
        /// (Рекомендую)») Синтетический результат с признаками «фон перевычтен» и «модель не
        /// описывает низ спектра» даёт в блоке пометок по красной строке на признак: полоса в
        /// кэВ и z, у второго — отношение образов к спектру; подпись — та же, что у открытого
        /// построителя окна. Положительные контроли: без признаков строк нет вовсе; один признак —
        /// одна строка, другой не появляется; ключ вместо перевода — отказ.
        /// </summary>
        static void BandWarningSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== предупреждения по полосе (AMBER269): «фон перевычтен», «модель не описывает низ» ===");
            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                foreach (int mask in new[] { 0, 1, 2, 3 })
                {
                    // Сцена — как у раздела пиков вне состава: без опор привязки.
                    FsaResult scene = new FsaResult
                    {
                        Chi2Ndf = 2.5, Chi2NdfPoisson = 2.5, BackgroundUsed = true, ResponseMatrixUsed = false,
                        EfficiencyUsed = false, CascadeSummingUsed = true
                    };
                    if ((mask & 1) != 0)
                    {
                        scene.BackgroundOversubtracted = new FsaBandWarning { LoKev = 44.2, HiKev = 63.8, Z = 7.31, Ratio = 1.12, Bands = 1 };
                    }
                    if ((mask & 2) != 0)
                    {
                        scene.ImagesAboveSpectrum = new FsaBandWarning { LoKev = 8.4, HiKev = 30.2, Z = 25.04, Ratio = 1.524, Bands = 2 };
                    }
                    var session = new FsaAnalysisSession();
                    Plant(session, scene, "bandwarn" + mask);
                    using (var report = new FSAReportView(mainForm))
                    {
                        // не потребитель — блок читается с подсаженной сцены (довод — у раздела пиков вне состава)
                        report.SetProbeSource(session, doc.ActiveResultData);
                        Row neg = null, img = null;
                        int negRows = 0, imgRows = 0;
                        foreach (Row row in report.ReportTable.TableModel.Rows)
                        {
                            if (row.Cells.Count < 2) continue;
                            if (row.Cells[1].Text == Own("FSAReport_BackgroundAboveValue")) { neg = row; negRows++; }
                            if (row.Cells[1].Text == Own("FSAReport_ImagesAboveValue")) { img = row; imgRows++; }
                        }
                        Same(lang + ": признаки " + mask + " — строк «" + Own("FSAReport_BackgroundAboveValue") + "»",
                             (mask & 1) != 0 ? 1 : 0, negRows);
                        Same(lang + ": признаки " + mask + " — строк «" + Own("FSAReport_ImagesAboveValue") + "»",
                             (mask & 2) != 0 ? 1 : 0, imgRows);
                        if (negRows != ((mask & 1) != 0 ? 1 : 0) || imgRows != ((mask & 2) != 0 ? 1 : 0))
                        {
                            ShowBlock(report, lang);
                        }
                        if (neg != null)
                        {
                            string caption = neg.Cells[0].Text;
                            Console.WriteLine("    {0}", caption);
                            Same(lang + ": «фон больше спектра» называет полосу 44–64 кэВ, z в подписи нет", true,
                                 caption.Contains("44") && caption.Contains("64") && !caption.Contains("7.3"));
                            Same(lang + ": подпись — та же, что у построителя окна", FSAReportView.BackgroundOversubtractedText(scene), caption);
                            Same(lang + ": z 7.3 и отношение 1.120 — в подсказке строки", true,
                                 neg.Cells[0].ToolTipText != null && neg.Cells[0].ToolTipText.Contains("7.3")
                                 && neg.Cells[0].ToolTipText.Contains("1.120"));
                            Same(lang + ": слово состояния «фон больше спектра» красное", Paint(BadColor), Paint(neg.Cells[1].ForeColor));
                            Denies(lang + ": контроль — ключ вместо перевода в строке «фон больше спектра»",
                                   caption.Contains("FSAReport_") || (neg.Cells[0].ToolTipText ?? "").Contains("FSAReport_"));
                        }
                        if (img != null)
                        {
                            string caption = img.Cells[0].Text;
                            Console.WriteLine("    {0}", caption);
                            // (`AMBER284`, решение Amber 02.11.2026 «Модель выше спектра в … (в … раза)»,
                            // z — в подсказку) полоса и отношение — в подписи, z 25.0 — только в подсказке
                            Same(lang + ": «модель выше спектра» называет полосу 8–30 кэВ и 1.52 раза, z в подписи нет", true,
                                 caption.Contains("8") && caption.Contains("30") && caption.Contains("1.52") && !caption.Contains("25.0"));
                            Same(lang + ": подпись — та же, что у построителя окна", FSAReportView.ImagesAboveSpectrumText(scene), caption);
                            Same(lang + ": z 25.0 — в подсказке строки, та же, что у построителя окна", true,
                                 img.Cells[0].ToolTipText != null && img.Cells[0].ToolTipText.Contains("25.0")
                                 && img.Cells[0].ToolTipText == FSAReportView.ImagesAboveSpectrumTip(scene));
                            Same(lang + ": слово состояния «модель выше спектра» красное", Paint(BadColor), Paint(img.Cells[1].ForeColor));
                            Denies(lang + ": контроль — ключ вместо перевода в строке «модель выше спектра»",
                                   caption.Contains("FSAReport_") || (img.Cells[0].ToolTipText ?? "").Contains("FSAReport_"));
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// (`AMBER269`; решение Amber 02.11.2026 вопросником, дословно: «Да, настройкой FSA с
        /// умолчанием 40 (Рекомендую)»; понижено до 28 — решение Amber 02.11.2026; снято до нуля — решение Amber 03.11.2026
        /// вопросником, дословно: «Правило порога + откат с шириной (Рекомендую)», `AMBER274`) НИЖНЯЯ ГРАНИЦА РАЗБОРА: поле окна есть,
        /// умолчание — нуль (правило порога),
        /// подпись поля на обоих языках; строка блока качества называет границу числом, подсказка —
        /// источник (поле окна или правило порога при нуле).
        /// </summary>
        static void FitBoundSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== нижняя граница разбора (AMBER269, AMBER274): поле окна, умолчание 0 (правило порога), строка блока качества ===");
            Same("умолчание поставки — 0, правило порога (решение Amber 03.11.2026 «Правило порога + откат с шириной (Рекомендую)»)", 0.0, FsaBand.ShippedFitLowerBoundKev);
            Same("умолчание анализатора — то же число", FsaBand.ShippedFitLowerBoundKev, new FsaAnalyzer().FitLowerBoundKev);
            Same("умолчание снимка настроек — то же число", FsaBand.ShippedFitLowerBoundKev, new FsaCalculationOptions().FitLowerBoundKev);
            Same("умолчание настройки приложения — то же число", FsaBand.ShippedFitLowerBoundKev,
                 new BecquerelMonitor.FsaCalculationSettings().FitLowerBoundKev);
            var a = new FsaCalculationOptions { FitLowerBoundKev = 40.0 };
            var b = new FsaCalculationOptions { FitLowerBoundKev = 0.0 };
            Same("граница входит в отпечаток настроек", false, a.Stamp == b.Stamp);
            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(doc.FsaSession, doc.ActiveResultData);
                    var updown = Control<NumericUpDown>(report, "fitLowerBoundUpDown");
                    var label = Control<Label>(report, "fitLowerBoundLabel");
                    if (updown != null && label != null)
                    {
                        Console.WriteLine("    {0}: поле «{1}» = {2}", lang, label.Text, updown.Value);
                        Same(lang + ": поле границы — общее поле с инвариантной культурой", true, updown is InvariantNumericUpDown);
                        Same(lang + ": поле показывает умолчание 0 (правило порога)", 0m, updown.Value);
                        Same(lang + ": подпись поля переведена", lang == "ru-RU" ? "Нижняя граница разбора, кэВ" : "Fit lower bound, keV", label.Text);
                        Same(lang + ": поле лежит в расчётной группе (своя строка в extrasFlow)", "extrasFlow",
                             updown.Parent != null && updown.Parent.Parent != null ? updown.Parent.Parent.Name : null);
                        Same(lang + ": подпись и поле — одной строкой", true, updown.Parent == label.Parent);
                    }
                }

                foreach (double bound in new[] { 40.0, 0.0 })
                {
                    FsaResult scene = new FsaResult
                    {
                        Chi2Ndf = 2.5, Chi2NdfPoisson = 2.5, BackgroundUsed = true, ResponseMatrixUsed = false,
                        EfficiencyUsed = false, CascadeSummingUsed = true,
                        FitLowerBoundKev = bound, FitStartKev = bound > 0.0 ? 40.37 : 6.39
                    };
                    var session = new FsaAnalysisSession();
                    Plant(session, scene, "fitbound" + bound.ToString(CultureInfo.InvariantCulture));
                    using (var report = new FSAReportView(mainForm))
                    {
                        report.SetProbeSource(session, doc.ActiveResultData);
                        Row found = null;
                        foreach (Row row in report.ReportTable.TableModel.Rows)
                        {
                            if (row.Cells.Count > 0 && row.Cells[0].Text == Own("FSAReport_FitBoundRow")) found = row;
                        }
                        Same(lang + ": граница " + bound.ToString(CultureInfo.InvariantCulture) + " — строка «" + Own("FSAReport_FitBoundRow") + "» есть", true, found != null);
                        if (found == null)
                        {
                            ShowBlock(report, lang);
                            continue;
                        }

                        string want = bound > 0.0 ? "40.0" : "6.4";
                        Same(lang + ": значение — граница, с которой шёл разбор", want, found.Cells[1].Text);
                        string tip = found.Cells[1].ToolTipText;
                        if (string.IsNullOrEmpty(tip)) tip = found.Cells[0].ToolTipText ?? "";
                        Console.WriteLine("    {0}: подсказка «{1}»", lang, tip);
                        Same(lang + ": подсказка несёт текст построителя окна", true, tip.Contains(FSAReportView.FitBoundTip(scene)));
                        Denies(lang + ": контроль — ключ вместо перевода в подсказке", tip.Contains("FSAReport_"));
                    }
                }
            }
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
                Same("и это текст причины", "ОШИБКА: причина такая-то", Text(report, 0, 0));
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
                    // строка качества печатает χ² по Пуассону (`AMBER209`, решение Amber 06.10.2026 «Заменить на Пуассона»)
                    Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = false,
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
                        // Имя пересилившего образа — подписью слоя, как в строке состава
                        // (П274 вслед за П275): по-русски «Обратное рассеяние», не служебное «Backscatter».
                        new[] { Own("FSAReport_SuppressedRow"), BecquerelMonitor.Properties.Resources.FSABackscatterName, Bad }
                    };
                    // (`AMBER17`) Матрицы у сцены НЕТ — строки привязки шкалы
                    // нет тоже (синего канала, по которому ищут опоры, без
                    // матрицы не бывает); появись она — число пометок скажет.
                    AnchorMarks(marks, scene, null, null, null);
                    int keys = 0;
                    foreach (string[] mark in marks)
                    {
                        if (mark[0].StartsWith("FSAReport_", StringComparison.Ordinal)) keys++;
                    }

                    Same(lang + ": все подписи блока прочитаны из resx (ни одного имени ключа)", 0, keys);
                    ShowBlock(report, lang);
                    Same(lang + ": блок «Качество разбора» собран верно", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", marks)));

                    // ⛔ (`AMBER284`, решение Amber 02.11.2026 — 28 фраз окна FSA в человекочитаемый
                    // вид) ПРЕЖНИХ ФРАЗ В ОКНЕ НЕТ — и ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежняя подпись
                    // χ²/ndf, подброшенная в ячейку, обязана быть поймана и сканером прежних фраз, и
                    // сверкой блока.
                    NoOldPhrases(report, lang, "сцена «подавлен»");
                    Row oldChi = report.ReportTable.TableModel.Rows[Chi2Row(report)];
                    string chiCaption = oldChi.Cells[0].Text;
                    oldChi.Cells[0].Text = lang == "ru-RU" ? "χ²/ndf, качество подгонки (по статистике Пуассона)"
                                                           : "χ²/ndf, goodness of fit (Poisson statistics)";
                    Denies(lang + ": контроль AMBER284 — прежняя подпись χ²/ndf не поймана сканером прежних фраз",
                           OldPhraseHits(report).Count == 0);
                    Denies(lang + ": контроль AMBER284 — прежняя подпись χ²/ndf проходит сверку блока",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    oldChi.Cells[0].Text = chiCaption;

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
                    string was = markRow.Cells[1].Text;
                    markRow.Cells[1].Text = Own("FSAReport_MatrixUsed");
                    Denies(lang + ": контроль — подменённое слово состояния пометки проверку не проходит",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    markRow.Cells[1].Text = was;

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
                    Color wasColor = painted.Cells[1].ForeColor;
                    painted.Cells[1].ForeColor = GoodColor;
                    Denies(lang + ": контроль — красная пометка, перекрашенная в зелёный, проверку не проходит",
                           BlockProblems(report, "1234.57", marks).Count == 0);
                    painted.Cells[1].ForeColor = wasColor;

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
                    Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
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
                    // (`AMBER17`, П105) Матрица живая, опор нет — строка
                    // «привязка шкалы, опор | none» обязана стоять четвёртой.
                    AnchorMarks(green, inflated, null, null, null);

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
                    Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
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
                    AnchorMarks(green, whole, null, null, null);

                    ShowBlock(report, lang);
                    Same(lang + ": всё учтено — три пометки зелёные, опор нет", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", green)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: зелёная пометка, перекрашенная
                    // в красный, проверку проходить не должна.
                    Row painted = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2];
                    Color wasColor = painted.Cells[1].ForeColor;
                    painted.Cells[1].ForeColor = BadColor;
                    Denies(lang + ": контроль — зелёная пометка, перекрашенная в красный, проверку не проходит",
                           BlockProblems(report, "1234.57", green).Count == 0);
                    painted.Cells[1].ForeColor = wasColor;

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (`AMBER17`, П105): строка опор
                    // ПОТЕРЯНА — ровно то, чего ждала проба до 18.09.2026, и
                    // на чём она была красна «пометок 4 вместо 3» с 11.09.2026:
                    // ожидание «три пометки» приняло бы окно, забывшее сказать,
                    // привязана ли шкала. Теперь такой блок — отказ.
                    int anchorRow = Chi2Row(report) + 2 + 3;
                    Row anchorSaved = report.ReportTable.TableModel.Rows[anchorRow];
                    report.ReportTable.TableModel.Rows.Remove(anchorSaved);
                    Denies(lang + ": контроль — блок с матрицей БЕЗ строки опор проверку не проходит",
                           BlockProblems(report, "1234.57", green).Count == 0);
                    report.ReportTable.TableModel.Rows.Insert(anchorRow, anchorSaved);
                    Same(lang + ": строка опор возвращена, блок снова сходится", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", green)));
                }

                // (`AMBER17`, решение Amber 11.09.2026 «ВКЛ умолчанием —
                // привязка работает сама, слой показывает опоры») ТРЕТЬЯ СЦЕНА
                // — ШКАЛА ПРИВЯЗАНА. Две опоры из трёх кандидатов, усиление
                // +1.23 %, ноль −0.50 кэВ: строк привязки ТРИ, а не одна, и у
                // всех одна подсказка с перечнем кандидатов. Без этой сцены
                // «опор нет» и «опор не искали» мерились бы одинаково, а числа
                // усиления и нуля не мерились бы вовсе.
                var anchored = new FsaResult
                {
                    Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                    EfficiencyUsed = true, CascadeSummingUsed = true,
                    ScaleAnchorsUsed = 2, Gain = 1.0123, AnchorOffsetKev = -0.5
                };
                anchored.ScaleAnchors.Add(new FsaScaleAnchor { Component = "L1", ModelKev = 661.7, ShiftKev = 0.12, PeakShare = 0.81, Used = true });
                anchored.ScaleAnchors.Add(new FsaScaleAnchor { Component = "L2", ModelKev = 1460.8, ShiftKev = -0.09, PeakShare = 0.77, Used = true });
                anchored.ScaleAnchors.Add(new FsaScaleAnchor { Component = "L3", ModelKev = 88.0, ShiftKev = 2.4, PeakShare = 0.12, Used = false, Refusal = "share" });
                var anchoredSession = new FsaAnalysisSession();
                Plant(anchoredSession, anchored, "quality-anchored");
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(anchoredSession, doc.ActiveResultData);
                    var tied = new List<string[]>
                    {
                        new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                        new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                        new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good }
                    };
                    // Числа — рукой, по сцене: 2 опоры, (1.0123 − 1)·100, −0.5.
                    AnchorMarks(tied, anchored, "2", "+1.23", "-0.50");

                    ShowBlock(report, lang);
                    Same(lang + ": шкала привязана — опор 2, усиление +1.23, ноль -0.50", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", tied)));

                    // Подсказка строки опор — перечень кандидатов: заголовок и
                    // по строке на каждого из трёх, принятых и отвергнутого.
                    Row anchorRow = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2 + 3];
                    string tip = anchorRow.Cells[0].ToolTipText ?? string.Empty;
                    Same(lang + ": подсказка строки опор — заголовок и три кандидата",
                         4, tip.Split('\n').Length);
                    Same(lang + ": в подсказке два принятых (+) и один отвергнутый (-)", true,
                         tip.Contains("\n+ ") && tip.Contains("\n- ") && tip.IndexOf("\n- ") == tip.LastIndexOf("\n- "));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПЕРВЫЙ: ожидание «опор нет» на
                    // привязанной сцене — отказ, иначе обе сцены матрицы
                    // мерили бы одно и то же.
                    var untied = new List<string[]>(tied.GetRange(0, 3));
                    untied.Add(new[] { Own("FSAReport_AnchorRow"), Own("FSAReport_AnchorNone"), Plain });
                    Denies(lang + ": контроль — «опор нет» на привязанной сцене проверку не проходит",
                           BlockProblems(report, "1234.57", untied).Count == 0);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВТОРОЙ: потеряна строка усиления.
                    int gainRow = Chi2Row(report) + 2 + 4;
                    Row gainSaved = report.ReportTable.TableModel.Rows[gainRow];
                    report.ReportTable.TableModel.Rows.Remove(gainSaved);
                    Denies(lang + ": контроль — без строки усиления проверку не проходит",
                           BlockProblems(report, "1234.57", tied).Count == 0);
                    report.ReportTable.TableModel.Rows.Insert(gainRow, gainSaved);
                    Same(lang + ": строка усиления возвращена, блок снова сходится", string.Empty,
                         string.Join("; ", BlockProblems(report, "1234.57", tied)));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ТРЕТИЙ: чужое число опор.
                    var miscounted = new List<string[]>(tied.GetRange(0, 3));
                    AnchorMarks(miscounted, anchored, "1", "+1.23", "-0.50");
                    Denies(lang + ": контроль — «1» опора вместо двух проверку не проходит",
                           BlockProblems(report, "1234.57", miscounted).Count == 0);
                }

                // (`AMBER269`, дефект 4; `AMBER274`, решения Amber 02.11.2026 «Редкие узлы + два
                // захода», «MDA под порог z ≥ 3») ЧЕТВЁРТАЯ СЦЕНА — ОТКАТ ПРИВЯЗКИ (А2), ПРАВИЛО MDA
                // И ДВА ЗАХОДА СТРОКИ БЕТЫ. Прежде откат жил только в служебной `AnchorNote`, и окно
                // писало «опор: нет»; правило MDA и шаг узлов под бетой окно не называло вовсе.
                // Ожидание — рукой, по числам сцены: строка правила MDA сразу под σ×; строка шага
                // узлов зелёная (VIF в пределе, два захода); строка опор — слово «откат», под ней —
                // красная строка отката с числами отменённого разбора.
                var rolled = new FsaResult
                {
                    Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                    EfficiencyUsed = true, CascadeSummingUsed = true,
                    AnchorRolledBack = true, AnchorRollbackAnchors = 3, AnchorRollbackGain = 1.0244,
                    AnchorRollbackChi2NdfPoisson = 2924.96,
                    LimitDecisionQuantile = 3.0, LimitDetectionQuantile = 1.6449,
                    BetaKnotFwhm = 16.0, BetaKnotVif = 5.57, BetaKnotVifMax = 10.0, BetaKnotVifMet = true,
                    BetaTwoPass = true, GammaKnotFwhm = 4.0
                };
                rolled.AnchorRollbackScaleAnchors = new List<FsaScaleAnchor>
                {
                    new FsaScaleAnchor { Component = "L1", ModelKev = 2614.5, ShiftKev = 1.5, PeakShare = 0.9, Used = true }
                };
                var rolledSession = new FsaAnalysisSession();
                Plant(rolledSession, rolled, "quality-rolled");
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(rolledSession, doc.ActiveResultData);
                    string rollback = string.Format(CultureInfo.InvariantCulture, Own("FSAReport_AnchorRollbackRow"),
                                                    "3", "+2.44", "2924.96", "1234.57");
                    // ⛔ (`AMBER284`, решение Amber 02.11.2026 «Убрать строку в подсказку строки
                    // бета-нуклидов (Рекомендую)»: на виду — только предупреждение, если VIF выше
                    // предела) VIF здесь в пределе — строки шага узлов в блоке НЕТ; до AMBER284 она
                    // стояла здесь зелёной «два захода».
                    var rolledMarks = new List<string[]>
                    {
                        new[] { Own("FSAReport_LimitRuleRow"),
                                string.Format(CultureInfo.InvariantCulture, Own("FSAReport_LimitRuleValue"), "3"), Plain },
                        new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                        new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                        new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good },
                        new[] { Own("FSAReport_AnchorRow"), Own("FSAReport_AnchorRolledBack"), Plain },
                        new[] { rollback, Own("FSAReport_AnchorRollbackValue"), Bad }
                    };

                    ShowBlock(report, lang);
                    Same(lang + ": откат привязки и правило MDA названы в блоке, строки шага узлов нет (VIF в пределе)",
                         string.Empty, string.Join("; ", BlockProblems(report, "1234.57", rolledMarks)));
                    NoOldPhrases(report, lang, "откат привязки");

                    // подсказка строки опор — опоры ОТМЕНЁННОГО разбора: без них «откат» неотличим от «опор нет»
                    Row anchorRow = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2 + 4];
                    string tip = anchorRow.Cells[0].ToolTipText ?? string.Empty;
                    Same(lang + ": в подсказке строки опор — опора отменённого разбора", true,
                         tip.Contains(Own("FSAReport_AnchorRollbackTip")) && tip.Contains("+ L1 2614.5"));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (`AMBER284`): прежнее ожидание — зелёная строка шага
                    // узлов «два захода» после суммирования — на этой сцене отказ
                    string knots = string.Format(CultureInfo.InvariantCulture, Own("FSAReport_BetaKnotRow"), "16", "5.6", "10")
                                   + string.Format(CultureInfo.InvariantCulture, Own("FSAReport_BetaKnotSecondPass"), "4");
                    var oldKnots = new List<string[]>(rolledMarks);
                    oldKnots.Insert(4, new[] { knots, lang == "ru-RU" ? "два захода" : "two passes", Good });
                    Denies(lang + ": контроль — прежняя строка шага узлов «два захода» в блоке проверку не проходит",
                           BlockProblems(report, "1234.57", oldKnots).Count == 0);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежнее ожидание «опор нет» без строки отката — отказ
                    var silent = new List<string[]>(rolledMarks.GetRange(0, 4));
                    silent.Add(new[] { Own("FSAReport_AnchorRow"), Own("FSAReport_AnchorNone"), Plain });
                    Denies(lang + ": контроль — «опор нет» без строки отката на сцене с откатом проверку не проходит",
                           BlockProblems(report, "1234.57", silent).Count == 0);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: без строки правила MDA — отказ
                    var noRule = new List<string[]>(rolledMarks.GetRange(1, rolledMarks.Count - 1));
                    Denies(lang + ": контроль — блок без строки правила MDA проверку не проходит",
                           BlockProblems(report, "1234.57", noRule).Count == 0);
                }

                BetaKnotVifSection(mainForm, doc, lang, chi2);
                NoMatrixTipSection(mainForm, doc, lang, chi2);
                SecondPassRefusedSection(mainForm, doc, lang, chi2);
                RollbackDeclensionSection(mainForm, doc, lang, chi2);
                NotResolvedSection(mainForm, doc, lang, chi2);
                ScrollReachSection(mainForm, doc, lang, chi2);
            }

            Language("en-US");
        }

        /// <summary>
        /// (`AMBER284`, решения Amber 02.11.2026 вопросником, дословно: «Убрать строку в подсказку
        /// строки бета-нуклидов (Рекомендую)» — на виду только предупреждение, если VIF выше
        /// предела; «"Бета-излучение пробы: 1234 отсч." Остальное в подсказку»; капсула — «Убрать
        /// из строки и подсказки, оставить в отказе (Рекомендую)») ПЯТАЯ СЦЕНА: бета пробы легла,
        /// VIF выше предела и на самом редком шаге. Ожидание — рукой: строка «бета-излучение
        /// пробы» — одни отсчёты, узлы — в подсказке, капсулы нет нигде; красная строка
        /// предупреждения VIF с текстом шага узлов в подсказке. Подсказка строки «только по
        /// бета» — её подпись и тот же текст шага узлов (`FSAReportView.BetaOnlyRowTip`).
        /// Положительные контроли: прежняя строка беты с узлами и капсулой — отказ; блок без
        /// строки VIF — отказ.
        /// </summary>
        static void BetaKnotVifSection(MainForm mainForm, DocEnergySpectrum doc, string lang, double chi2)
        {
            const string capsule = "CAPSULE-A284";
            var beta = new FsaResult
            {
                Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                EfficiencyUsed = true, CascadeSummingUsed = true,
                SampleBetaKeyOn = true, SampleBetaRefusal = BecquerelMonitor.EfficiencyMaker.ElectronSectionRefusal.None,
                SampleBetaCounts = 1234.4, SampleBetaNodes = 40, SampleBetaCapsule = capsule,
                BetaKnotFwhm = 32.0, BetaKnotVif = 14.2, BetaKnotVifMax = 10.0, BetaKnotVifMet = false,
                BetaTwoPass = true, GammaKnotFwhm = 4.0
            };
            var session = new FsaAnalysisSession();
            Plant(session, beta, "quality-beta-vif");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(session, doc.ActiveResultData);
                var marks = new List<string[]>
                {
                    new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                    new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                    new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good },
                    new[] { Own("FSAReport_SampleBetaRow"),
                            string.Format(CultureInfo.InvariantCulture, Own("FSAReport_SampleBetaUsed"), "1234"), Good },
                    new[] { Own("FSAReport_BetaKnotVifOverRow"), Own("FSAReport_BetaKnotVifOverValue"), Bad }
                };
                AnchorMarks(marks, beta, null, null, null);

                ShowBlock(report, lang);
                Same(lang + ": бета пробы — одни отсчёты, предупреждение VIF — красной строкой", string.Empty,
                     string.Join("; ", BlockProblems(report, "1234.57", marks)));
                NoOldPhrases(report, lang, "бета пробы и VIF");

                int chi = Chi2Row(report);
                Row betaRow = report.ReportTable.TableModel.Rows[chi + 2 + 3];
                string betaTip = betaRow.Cells[0].ToolTipText ?? string.Empty;
                Console.WriteLine("    подсказка беты пробы: {0}", betaTip);
                Same(lang + ": подсказка беты пробы — та же, что у построителя окна, с отсчётами и 40 узлами", true,
                     betaTip == FSAReportView.SampleBetaTip(beta) && betaTip.Contains("1234") && betaTip.Contains("40"));
                Same(lang + ": капсулы нет ни в строке беты, ни в её подсказке", false,
                     betaRow.Cells[0].Text.Contains(capsule) || betaRow.Cells[1].Text.Contains(capsule)
                     || betaTip.Contains(capsule) || (betaRow.Cells[1].ToolTipText ?? "").Contains(capsule));

                Row vifRow = report.ReportTable.TableModel.Rows[chi + 2 + 4];
                string vifTip = vifRow.Cells[0].ToolTipText ?? string.Empty;
                Console.WriteLine("    подсказка VIF: {0}", vifTip);
                Same(lang + ": подсказка строки VIF — текст шага узлов построителя окна (VIF 14.2, предел 10)", true,
                     vifTip == FSAReportView.BetaKnotCaption(beta) && vifTip.Contains("14.2") && vifTip.Contains("10"));

                // строка «только по бета» — подсказка: подпись и тот же текст шага узлов
                var sr90 = new FsaReportRow { Kind = FsaReportRowKind.Layer, Name = "Sr-90", BetaOnly = true };
                string rowTip = FSAReportView.BetaOnlyRowTip(sr90, beta);
                Console.WriteLine("    подсказка строки «только по бета»: {0}", rowTip);
                Same(lang + ": подсказка строки «только по бета» — подпись с пометкой и шаг узлов", true,
                     rowTip == FSAReportView.RowCaption(sr90) + Environment.NewLine + FSAReportView.BetaKnotCaption(beta)
                     && FSAReportView.RowCaption(sr90).EndsWith(Own("FSAReport_BetaOnlyMark")));
                Same(lang + ": у строки не по бета своей подсказки шага узлов нет", true,
                     FSAReportView.BetaOnlyRowTip(new FsaReportRow { Kind = FsaReportRowKind.Layer, Name = "Cs-137" }, beta) == null);

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежняя строка беты (узлы и капсула в значении) — отказ
                var oldBeta = new List<string[]>(marks);
                oldBeta[3] = new[] { Own("FSAReport_SampleBetaRow"),
                                     lang == "ru-RU" ? "в образах 1234 отсч.; раздел 40 узлов; капсула источника " + capsule
                                                     : "in images 1234 counts; section of 40 nodes; source capsule " + capsule, Good };
                Denies(lang + ": контроль — прежняя строка беты с узлами и капсулой проверку не проходит",
                       BlockProblems(report, "1234.57", oldBeta).Count == 0);

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: блок без строки VIF при VIF выше предела — отказ
                var noVif = new List<string[]>(marks);
                noVif.RemoveAt(4);
                Denies(lang + ": контроль — блок без строки VIF при VIF выше предела проверку не проходит",
                       BlockProblems(report, "1234.57", noVif).Count == 0);
            }
        }

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026 вопросником, дословно: «Объединить со строкой
        /// выше, где уже написано - нет Матрицы отклика. Остальное убрать в подсказку.») ШЕСТАЯ
        /// СЦЕНА: разбор с кривой и без матрицы. Ожидание — рукой: строка «Матрица отклика |
        /// не учтена» красная и с признаком происшествия, её подсказка — цена разбора без матрицы
        /// словами окна (`FSAReport_MatrixNotUsedTip`); отдельной строки-предупреждения нет —
        /// пометок ровно четыре (матрица, кривая, суммирование, опоры). Положительный контроль:
        /// прежний блок с отдельной строкой «до −11 %» — отказ.
        /// </summary>
        static void NoMatrixTipSection(MainForm mainForm, DocEnergySpectrum doc, string lang, double chi2)
        {
            var scene = new FsaResult
            {
                Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = false,
                EfficiencyUsed = true, CascadeSummingUsed = false
            };
            var session = new FsaAnalysisSession();
            Plant(session, scene, "quality-nomatrix");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(session, doc.ActiveResultData);
                // привязка без матрицы идёт по пикам кривой (`AMBER156` (а)) — строка опор есть
                var marks = new List<string[]>
                {
                    new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixNotUsed"), Bad },
                    new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                    new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingNoMatrix"), Bad },
                    new[] { Own("FSAReport_AnchorRow"), Own("FSAReport_AnchorNone"), Plain }
                };

                ShowBlock(report, lang);
                Same(lang + ": без матрицы — отдельной строки-предупреждения нет, пометок четыре", string.Empty,
                     string.Join("; ", BlockProblems(report, "1234.57", marks)));
                NoOldPhrases(report, lang, "без матрицы");

                Row matrixRow = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2];
                string tip = matrixRow.Cells[0].ToolTipText ?? string.Empty;
                Console.WriteLine("    подсказка строки «Матрица отклика»: {0}", tip);
                Same(lang + ": подсказка строки «Матрица отклика» — цена разбора без матрицы", Own("FSAReport_MatrixNotUsedTip"), tip);
                Same(lang + ": та же подсказка у значения", tip, matrixRow.Cells[1].ToolTipText ?? string.Empty);
                Same(lang + ": подсказка — та же, что у построителя окна", FSAReportView.MatrixNotUsedTip(scene), tip);
                Same(lang + ": в подсказке цена 11 % и совет построить матрицу", true, tip.Contains("11"));
                Same(lang + ": строка «Матрица отклика» — происшествие (признак Warning)", true,
                     ((FsaReportRow)matrixRow.Tag).Warning);

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежний блок с отдельной строкой «… | до −11 %» — отказ
                var old = new List<string[]>(marks);
                old.Insert(3, new[] { lang == "ru-RU" ? "Нет матрицы отклика: каскадное суммирование не учтено"
                                                      : "No response matrix: cascade summing is not applied",
                                      lang == "ru-RU" ? "до −11 %" : "up to −11 %", Bad });
                Denies(lang + ": контроль — прежняя отдельная строка «без матрицы» проверку не проходит",
                       BlockProblems(report, "1234.57", old).Count == 0);
            }

            // контроль условия: у матричного разбора подсказки нет
            Same(lang + ": у матричного разбора подсказки «без матрицы» нет", true,
                 FSAReportView.MatrixNotUsedTip(new FsaResult { ResponseMatrixUsed = true, EfficiencyUsed = true }) == null);
        }

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026 вопросником, дословно: «Вернуть красной строкой
        /// (Рекомендую)») СЕДЬМАЯ СЦЕНА: строка беты дошла до фита, VIF В ПРЕДЕЛЕ, второй заход
        /// гамма-строк ОТКАЗАЛ («NoFit»). Ожидание — рукой: на виду красная строка «нуклиды с
        /// гамма-линиями посчитаны первым, более грубым заходом» | «грубее»; её подсказка — текст
        /// шага узлов построителя окна, в нём причина словами окна (текст отказа «подгонка не
        /// сошлась» сеанса), а не имя члена перечисления «NoFit». Положительные контроли: прежнее
        /// (полосы `a284`) ожидание без этой строки — отказ; та же сцена без отказа — строки нет;
        /// при VIF выше предела и отказе — обе красные строки, порознь.
        /// </summary>
        static void SecondPassRefusedSection(MainForm mainForm, DocEnergySpectrum doc, string lang, double chi2)
        {
            Func<string, bool, FsaResult> scene = (refusal, vifMet) => new FsaResult
            {
                Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                EfficiencyUsed = true, CascadeSummingUsed = true,
                BetaKnotFwhm = 16.0, BetaKnotVif = vifMet ? 5.57 : 14.2, BetaKnotVifMax = 10.0, BetaKnotVifMet = vifMet,
                BetaTwoPass = false, BetaGammaPassRefusal = refusal
            };
            // подписи и значения — рукой, по решению Amber и тексту полосы a284b
            string row = lang == "ru-RU"
                ? "Нуклиды с гамма-линиями посчитаны первым, более грубым заходом: второй заход не состоялся"
                : "Nuclides with gamma lines are computed by the first, coarser pass: the second pass failed";
            string value = lang == "ru-RU" ? "грубее" : "coarser";
            string reason = lang == "ru-RU"
                ? "Полноспектральное разложение: подгонка не сошлась — проверьте энергетическую калибровку и состав"
                : "Full-spectrum decomposition: the fit did not converge — check the energy calibration and the composition";
            Same(lang + ": подпись отказа второго захода в resx — та, что записана рукой", row,
                 Own("FSAReport_BetaKnotSecondRefusedRow"));
            Same(lang + ": значение отказа второго захода в resx — то, что записано рукой", value,
                 Own("FSAReport_BetaKnotSecondRefusedValue"));
            Same(lang + ": причина «NoFit» словами окна — текст отказа сеанса", reason,
                 FSAReportView.SecondPassRefusalText(scene("NoFit", true)));
            Same(lang + ": причина «LibraryEmptied» словами окна — текст отказа сеанса",
                 BecquerelMonitor.Properties.Resources.FSALibraryEmptied,
                 FSAReportView.SecondPassRefusalText(scene("LibraryEmptied", true)));
            Same(lang + ": имя, которого перечисление не знает, — как есть", "Strange42",
                 FSAReportView.SecondPassRefusalText(scene("Strange42", true)));
            Same(lang + ": отказа нет — причины нет", true, FSAReportView.SecondPassRefusalText(scene(null, true)) == null);

            FsaResult refused = scene("NoFit", true);
            var session = new FsaAnalysisSession();
            Plant(session, refused, "quality-second-refused");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(session, doc.ActiveResultData);
                var marks = new List<string[]>
                {
                    new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                    new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                    new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good },
                    new[] { row, value, Bad }
                };
                AnchorMarks(marks, refused, null, null, null);

                ShowBlock(report, lang);
                Same(lang + ": отказ второго захода при VIF в пределе — красной строкой", string.Empty,
                     string.Join("; ", BlockProblems(report, "1234.57", marks)));
                NoOldPhrases(report, lang, "отказ второго захода");

                Row refusedRow = report.ReportTable.TableModel.Rows[Chi2Row(report) + 2 + 3];
                string tip = refusedRow.Cells[0].ToolTipText ?? string.Empty;
                Console.WriteLine("    подсказка строки отказа: {0}", tip.Replace(Environment.NewLine, " ⏎ "));
                Same(lang + ": подсказка строки отказа — текст шага узлов построителя окна",
                     FSAReportView.BetaKnotCaption(refused), tip);
                Same(lang + ": та же подсказка у значения", tip, refusedRow.Cells[1].ToolTipText ?? string.Empty);
                Same(lang + ": в подсказке — причина словами окна своей строкой", true,
                     tip.EndsWith(Environment.NewLine + reason, StringComparison.Ordinal)
                     && tip.Contains(Own("FSAReport_BetaKnotSecondRefused")));
                Same(lang + ": имени члена перечисления «NoFit» в подсказке нет", false, tip.Contains("NoFit"));
                Same(lang + ": строка отказа — происшествие (признак Warning)", true, ((FsaReportRow)refusedRow.Tag).Warning);

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежнее ожидание полосы a284 — отказ виден только в
                // подсказке, строки на виду нет — обязано отказать
                var hidden = new List<string[]>(marks);
                hidden.RemoveAt(3);
                Denies(lang + ": контроль — блок без строки отказа второго захода проверку не проходит",
                       BlockProblems(report, "1234.57", hidden).Count == 0);
            }

            // ⛔ КОНТРОЛЬ УСЛОВИЯ: та же сцена без отказа — строки нет (VIF в пределе, строк беты нет)
            FsaResult clean = scene(null, true);
            var cleanSession = new FsaAnalysisSession();
            Plant(cleanSession, clean, "quality-second-clean");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(cleanSession, doc.ActiveResultData);
                var marks = new List<string[]>
                {
                    new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                    new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                    new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good }
                };
                AnchorMarks(marks, clean, null, null, null);
                Same(lang + ": без отказа второго захода строки отказа нет", string.Empty,
                     string.Join("; ", BlockProblems(report, "1234.57", marks)));
                var phantom = new List<string[]>(marks);
                phantom.Insert(3, new[] { row, value, Bad });
                Denies(lang + ": контроль — строка отказа на сцене без отказа проверку не проходит",
                       BlockProblems(report, "1234.57", phantom).Count == 0);
            }

            // VIF выше предела И отказ: оба факта на виду, порознь — сначала VIF, затем отказ
            FsaResult both = scene("NoFit", false);
            var bothSession = new FsaAnalysisSession();
            Plant(bothSession, both, "quality-second-both");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(bothSession, doc.ActiveResultData);
                var marks = new List<string[]>
                {
                    new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                    new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                    new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good },
                    new[] { Own("FSAReport_BetaKnotVifOverRow"), Own("FSAReport_BetaKnotVifOverValue"), Bad },
                    new[] { row, value, Bad }
                };
                AnchorMarks(marks, both, null, null, null);
                Same(lang + ": VIF выше предела и отказ второго захода — две красные строки", string.Empty,
                     string.Join("; ", BlockProblems(report, "1234.57", marks)));
            }
        }

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026, дословно по заданию: «при 1 выходит „по 1 пикам“.
        /// Склонять по числу: 1, 21, 31… — „пику“; прочие — „пикам“. Слова Amber не менять. EN —
        /// „peak“/„peaks“.») ВОСЬМАЯ СЦЕНА — подпись строки «Оставлена шкала файла» по числу
        /// пиков. Ожидание — рукой, кусками текста: по-русски 1, 21, 101 — «пику», 2, 3, 11, 111 —
        /// «пикам»; по-английски 1 — «peak», прочие — «peaks». Плюс окно со сценой отката по одному
        /// пику: в ячейке — «по 1 пику». Положительный контроль: прежняя подпись «по 1 пикам» в
        /// ячейке — отказ сверки.
        /// </summary>
        static void RollbackDeclensionSection(MainForm mainForm, DocEnergySpectrum doc, string lang, double chi2)
        {
            Func<int, FsaResult> scene = n => new FsaResult
            {
                Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = true,
                EfficiencyUsed = true, CascadeSummingUsed = true,
                AnchorRolledBack = true, AnchorRollbackAnchors = n, AnchorRollbackGain = 1.0158,
                AnchorRollbackChi2NdfPoisson = 2380.71,
                LimitDecisionQuantile = 3.0, LimitDetectionQuantile = 1.6449
            };
            bool ru = lang == "ru-RU";
            var cases = new[] { 1, 2, 3, 5, 11, 12, 21, 22, 101, 111 };
            foreach (int n in cases)
            {
                bool one = ru ? (n == 1 || n == 21 || n == 101) : n == 1;
                string want = ru
                    ? "Оставлена шкала файла: подстройка по " + n + (one ? " пику" : " пикам") + " (+1.58 %) ухудшила подгонку: χ²/ndf 2380.71 против 1234.57"
                    : "File scale kept: adjusting it on " + n + (one ? " peak" : " peaks") + " (+1.58 %) made the fit worse: χ²/ndf 2380.71 against 1234.57";
                Same(lang + ": откат по " + n + " — «" + (ru ? (one ? "пику" : "пикам") : (one ? "peak" : "peaks")) + "»",
                     want, FSAReportView.AnchorRollbackCaption(scene(n)));
            }

            FsaResult single = scene(1);
            var session = new FsaAnalysisSession();
            Plant(session, single, "quality-rollback-one");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(session, doc.ActiveResultData);
                string caption = ru
                    ? "Оставлена шкала файла: подстройка по 1 пику (+1.58 %) ухудшила подгонку: χ²/ndf 2380.71 против 1234.57"
                    : "File scale kept: adjusting it on 1 peak (+1.58 %) made the fit worse: χ²/ndf 2380.71 against 1234.57";
                var marks = new List<string[]>
                {
                    new[] { Own("FSAReport_LimitRuleRow"),
                            string.Format(CultureInfo.InvariantCulture, Own("FSAReport_LimitRuleValue"), "3"), Plain },
                    new[] { Own("FSAReport_MatrixRow"), Own("FSAReport_MatrixUsed"), Good },
                    new[] { Own("FSAReport_EfficiencyRow"), Own("FSAReport_EfficiencyUsed"), Good },
                    new[] { Own("FSAReport_SummingRow"), Own("FSAReport_SummingUsed"), Good },
                    new[] { Own("FSAReport_AnchorRow"), Own("FSAReport_AnchorRolledBack"), Plain },
                    new[] { caption, Own("FSAReport_AnchorRollbackValue"), Bad }
                };
                ShowBlock(report, lang);
                Same(lang + ": откат по одному пику — в окне «" + (ru ? "по 1 пику" : "on 1 peak") + "»", string.Empty,
                     string.Join("; ", BlockProblems(report, "1234.57", marks)));

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежняя подпись «по 1 пикам» / «on 1 peaks» — отказ
                var old = new List<string[]>(marks);
                old[5] = new[] { ru ? caption.Replace("по 1 пику", "по 1 пикам") : caption.Replace("on 1 peak", "on 1 peaks"),
                                 Own("FSAReport_AnchorRollbackValue"), Bad };
                Denies(lang + ": контроль — прежняя подпись «" + (ru ? "по 1 пикам" : "on 1 peaks") + "» проверку не проходит",
                       BlockProblems(report, "1234.57", old).Count == 0);
            }
        }

        /// <summary>
        /// (`AMBER284`, полоса a284b, находка экранной приёмки 02.11.2026) ДЕСЯТАЯ СЦЕНА — ПОСЛЕДНЯЯ
        /// СТРОКА ОКНА ДОСТУПНА ПРОКРУТКЕ. Замер экраном: диск Th-232 без матрицы, окно гида —
        /// колесо на 10 щелчков сдвигало таблицу на одну строку, и низ строки «… рядом в библиотеке:
        /// Pb-214 …» оставался обрезан: предел прокрутки XPTable считался по числу строк, влезающих
        /// от ВЕРХНЕЙ строки, а внизу у окна высокие строки блока качества. Сцена — разбор без
        /// матрицы с откатом шкалы, отказом второго захода и пиком вне состава (внизу три высокие
        /// строки) в окне 320×700 (поле данных таблицы — около двухсот точек). Ожидание: на упоре прокрутки строки от верхней видимой до
        /// последней целиком умещаются в поле данных. ⛔ Положительный контроль: прежний предел (по
        /// строкам от верха) на этой же сцене последнюю строку не умещает — иначе сцена не ловит
        /// дефект и проверка пуста.
        /// </summary>
        static void ScrollReachSection(MainForm mainForm, DocEnergySpectrum doc, string lang, double chi2)
        {
            var scene = new FsaResult
            {
                Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = false,
                EfficiencyUsed = true, CascadeSummingUsed = false,
                AnchorRolledBack = true, AnchorRollbackAnchors = 4, AnchorRollbackGain = 0.9996,
                AnchorRollbackChi2NdfPoisson = 2.81,
                BetaKnotFwhm = 16.0, BetaKnotVif = 5.57, BetaKnotVifMax = 10.0, BetaKnotVifMet = true,
                BetaTwoPass = false, BetaGammaPassRefusal = "NoFit"
            };
            var uncovered = new FsaUncoveredPeak { EnergyKev = 353.5, WidthKev = 30.0, ExcessCounts = 12345.0, ExcessShare = 0.18, Z = 42.2 };
            uncovered.Candidates.Add(new FsaUncoveredCandidate { Nuclide = "Pb-214", EnergyKev = 351.93 });
            scene.UncoveredPeaks.Add(uncovered);
            var session = new FsaAnalysisSession();
            Plant(session, scene, "scroll-reach");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(session, doc.ActiveResultData);
                using (Form host = Host(report, 320, 700))
                {
                    report.RefreshReport();
                    Application.DoEvents();
                    XPTable.Models.Table table = report.ReportTable;
                    var rows = table.TableModel.Rows;
                    int height = table.CellDataRect.Height;
                    int total = 0;
                    for (int i = 0; i < rows.Count; i++) total += rows[i].Height;
                    Console.WriteLine("    окно 320×700: поле данных таблицы {0} px, строк {1} общей высотой {2} px", height, rows.Count, total);
                    Same(lang + ": окно 320×700 — поле данных есть, строки в него не влезают, полоса есть", true,
                         height > 0 && total > height && table.VScroll);

                    VScrollBar bar = table.VerticalScrollBar;
                    int maxValue = bar.Maximum - bar.LargeChange + 1;
                    bar.Value = Math.Max(bar.Minimum, maxValue);
                    Application.DoEvents();
                    int top = table.TopIndex;
                    int tail = 0;
                    for (int i = top; i < rows.Count; i++) tail += rows[i].Height;
                    Console.WriteLine("    поле данных {0} px; строк {1}; на упоре верхняя {2}, высота строк от неё до конца {3} px; последняя строка {4} px",
                                      height, rows.Count, top, tail, rows[rows.Count - 1].Height);
                    Same(lang + ": на упоре прокрутки последняя строка видна целиком", true, tail <= height);

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежний предел — по строкам, влезающим от ВЕРХА таблицы
                    int fromTop = 0, sum = 0;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        sum += rows[i].Height;
                        if (sum < height) fromTop++; else break;
                    }

                    int oldTop = rows.Count - fromTop;
                    int oldTail = 0;
                    for (int i = Math.Max(0, oldTop); i < rows.Count; i++) oldTail += rows[i].Height;
                    Console.WriteLine("    прежний предел: верхняя {0}, высота от неё {1} px", oldTop, oldTail);
                    Denies(lang + ": контроль — прежний предел (строки от верха) умещает последнюю строку",
                           oldTail <= height);
                }
            }
        }

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026 вопросником, дословно: «"не найдено" / "искал эту
        /// составляющую" (Рекомендую)») ДЕВЯТАЯ СЦЕНА — строка образа «Обратное рассеяние»,
        /// предъявленного фиту и не выделившегося: значение «не найдено», подсказка «Разбор искал
        /// эту составляющую…». Строка с этим значением бывает только у образа, а не у нуклида,
        /// поэтому прежнее «не найден» / «искал этот нуклид» на экране читалось неверно. Ожидание
        /// — рукой, полным текстом обоих языков; строка ищется в ТАБЛИЦЕ окна. Положительный
        /// контроль: прежние тексты полосы `a284` с ячейками не совпадают.
        /// </summary>
        static void NotResolvedSection(MainForm mainForm, DocEnergySpectrum doc, string lang, double chi2)
        {
            bool ru = lang == "ru-RU";
            string value = ru ? "не найдено" : "not found";
            string hint = ru
                ? "Разбор искал эту составляющую и не нашёл: её область целиком объясняет подложка. Чтобы оценить предел обнаружения, нужна геометрия детектора."
                : "The analysis looked for this component and did not find it: the baseline explains its whole region. A detector geometry is needed to estimate the detection limit.";
            var dropped = new FsaResult
            {
                Chi2Ndf = chi2, Chi2NdfPoisson = chi2, BackgroundUsed = true, ResponseMatrixUsed = false, EfficiencyUsed = true
            };
            dropped.SuppressedImages.Add(new FsaSuppressedImage
            {
                Name = FsaResult.BackscatterLayerName,
                Kind = FsaComponentKind.Nuisance,
                Z = 0.0
            });

            var session = new FsaAnalysisSession();
            Plant(session, dropped, "not-resolved");
            using (var report = new FSAReportView(mainForm))
            {
                report.SetProbeSource(session, doc.ActiveResultData);
                string shown = FsaPalette.DisplayName(FsaResult.BackscatterLayerName);
                Row found = null;
                int count = 0;
                foreach (Row r in report.ReportTable.TableModel.Rows)
                {
                    if (r.Cells[0].Text == shown)
                    {
                        found = r;
                        count++;
                    }
                }

                Same(lang + ": строка «" + shown + "» в таблице окна одна", 1, count);
                if (found == null)
                {
                    return;
                }

                Console.WriteLine("    «{0}» | {1} — {2}", found.Cells[0].Text, found.Cells[1].Text, found.Cells[0].ToolTipText);
                Same(lang + ": значение — «" + value + "»", value, found.Cells[1].Text);
                Same(lang + ": подсказка — «искал эту составляющую»", hint, found.Cells[0].ToolTipText);
                NoOldPhrases(report, lang, "не выделившееся рассеяние");

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежние тексты полосы a284 обязаны НЕ совпасть
                string oldValue = ru ? "не найден" : "not found (nuclide)";
                string oldHint = ru
                    ? "Разбор искал этот нуклид и не нашёл: его область целиком объясняет подложка. Чтобы оценить предел обнаружения, нужна геометрия детектора."
                    : "The analysis looked for this nuclide and did not find it: the baseline explains its whole region. A detector geometry is needed to estimate the detection limit.";
                Denies(lang + ": контроль — прежняя подсказка «искал этот нуклид» с ячейкой совпала",
                       found.Cells[0].ToolTipText == oldHint);
                if (ru)
                {
                    Denies(lang + ": контроль — прежнее значение «не найден» с ячейкой совпало", found.Cells[1].Text == oldValue);
                }

                string saved = found.Cells[0].ToolTipText;
                found.Cells[0].ToolTipText = oldHint;
                Denies(lang + ": контроль — прежняя подсказка, подброшенная в ячейку, не поймана сканером прежних фраз",
                       OldPhraseHits(report).Count == 0);
                found.Cells[0].ToolTipText = saved;
            }
        }

        /// <summary>
        /// (`AMBER284`) ПРЕЖНИЕ ФРАЗЫ ОКНА — отличительные куски 28 фраз, заменённых решением Amber
        /// 02.11.2026 (журнал `handover/handover-2026-11-02-amber284-texts.md`), обоих языков. Окно,
        /// где хоть одна встретилась в подписи, значении или подсказке, — отказ: это ровно тот
        /// случай, когда ключ переименован, а старый текст остался в коде или в одной из `resx`.
        /// </summary>
        static readonly string[] OldPhrases =
        {
            "Невязка: + лишнее", "χ²/ndf, качество подгонки", "σ×, во сколько раз", "Правило MDA",
            "Привязка шкалы, опор", "Усиление по опорам", "Ноль по опорам", "Откат привязки шкалы",
            "Подгонка дрейфа шкалы", "край сетки", "пик вне состава", "есть в сете, нет в составе", "не разложен",
            "Состав пересилен", " · подавлен", "Фон не взят", "фон не взят", "Нет матрицы отклика:",
            "исключены из образов", "Модель не описывает", "Колонка наложений", "Поставки данных распада",
            "decay_chain", "decay_radiations", "Бета пробы (раздел", "; капсула источника", "по сплошному спектру",
            "Подложка под строками", "два захода", "не выделено", "шумовой пол", "показ подрезан",
            "не определяются", "Ветви β в базе", "образ беты завышен",
            "Residual: + excess", "goodness of fit (Poisson", "σ×, uncertainty", "MDA rule", "Scale anchoring",
            "Gain from anchors", "Zero from anchors", "Scale drift fit", "at grid edge",
            "peak outside the composition", "in the nuclide set, not in the composition",
            "Composition suppressed", " · suppressed", "Background rejected", "bg rejected", "No response matrix:",
            "excluded from the images", "Model does not describe", "Pile-up column", "Decay data sources",
            "electron section of the matrix)", "; source capsule", "by the continuum", "Continuum-only", "Continuum under the beta", "two passes",
            "not resolved", "noise floor", "display clipped", "not determined", "branches summing above",
            "beta image overstated",
            // (`AMBER284`, полоса a284b, решение Amber 02.11.2026 «"не найдено" / "искал эту
            // составляющую"») подсказка строки образа, не нуклида
            "искал этот нуклид", "looked for this nuclide"
        };

        /// <summary>(`AMBER284`) Куски прежних фраз, найденные в окне: «строка: кусок».</summary>
        static List<string> OldPhraseHits(FSAReportView report)
        {
            var hits = new List<string>();
            TableModel model = report.ReportTable.TableModel;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                foreach (Cell cell in model.Rows[i].Cells)
                {
                    string all = (cell.Text ?? string.Empty) + "\n" + (cell.ToolTipText ?? string.Empty);
                    foreach (string old in OldPhrases)
                    {
                        if (all.Contains(old))
                        {
                            hits.Add(i + ": «" + old + "»");
                        }
                    }
                }
            }

            return hits;
        }

        /// <summary>(`AMBER284`) Проверка «прежних фраз в окне нет» с печатью находок.</summary>
        static void NoOldPhrases(FSAReportView report, string lang, string what)
        {
            Same(lang + ": прежних фраз AMBER284 в окне нет (" + what + ")", string.Empty,
                 string.Join("; ", OldPhraseHits(report)));
        }

        // ------------------------------------------------------------------
        // 6в. ПРИЧИНА ПОМЕТКИ КРИВОЙ (`AMBER269`, дефект 5)
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER269` дефект 5, 02.11.2026) Пометка «кривая посчитана для другой
        /// геометрии» в блоке качества называет ПРИЧИНУ так же, как вкладка прибора
        /// (`DeviceConfigForm.GeometryNotes`): кривая точечной сцены, посчитанная до сдвига
        /// точки в капсуле, — «до сдвига точки … мм», а не «геометрию правили после
        /// расчёта». Три сцены на копии спектра (документ не трогается): отпечаток без
        /// строки сдвига — причина сдвиг; та же кривая при правке расстояния — прежняя
        /// подпись; свежий отпечаток — пометки нет (контроль). Ожидание — рукой: формат
        /// из ресурса формы прибора и сдвиг точки, а не вызовом окна.
        /// </summary>
        static void CurveCauseSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 6в. пометка кривой «для другой геометрии»: причина — как на вкладке прибора (AMBER269) ===");
            GeometryModel point = new GeometryModel();
            GeometryPresets.Items[0].Apply(point);
            point.SourceType = GeometrySourceType.Point;
            string now = ResponseMatrix.GeometryFingerprint(point);
            string before = ResponseMatrix.GeometryFingerprintBeforeSourceOffset(point);
            GeometryModel edited = point.Clone();
            edited.PointDistance = edited.PointDistance + 10.0;
            // ⛔ Откат Ф6 (`critpt30`, 02.11.2026): сдвига точки нет — отпечатка «до сдвига» нет,
            // пометка «до сдвига точки» не рождается; правка расстояния — прежняя подпись.
            Same("сцена: у точечной заготовки сдвига точки нет (откат Ф6)", "0.0",
                 point.SourceCapsuleOffset.ToString("0.0##", CultureInfo.InvariantCulture));
            Same("сцена: отпечатка «до сдвига» нет (откат Ф6)", string.Empty, before);

            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                string format = new ComponentResourceManager(typeof(DeviceConfigForm)).GetString("EfficiencyCurveBeforeSourceOffset") ?? "";
                string offsetNote = string.Format(CultureInfo.InvariantCulture, format, "1.5");
                string otherGeometry = BecquerelMonitor.Properties.Resources.EfficiencyCurveOtherGeometry;
                string value = BecquerelMonitor.Properties.Resources.FSAReportCurveOtherGeometryValue;
                Same(lang + ": подпись «до сдвига» из ресурса формы прибора прочитана и отлична от прежней", true,
                     format.Length > 0 && offsetNote != otherGeometry);
                // (`AMBER269`, полоса a269fix5, критик кода физики 29, Н8) причина — прежняя
                // версия программы, а не дата: признак судит по отпечатку, не по дню счёта.
                Same(lang + ": подпись «до сдвига» называет прежнюю версию программы, а не дату", true,
                     format.IndexOf("30.10", StringComparison.Ordinal) < 0
                     && format.IndexOf(lang == "ru-RU" ? "прежней версией" : "earlier version", StringComparison.Ordinal) >= 0);

                var scenes = new[]
                {
                    new { What = "правка расстояния после счёта", Geometry = edited, Print = now, Want = otherGeometry },
                    new { What = "свежий отпечаток (контроль)", Geometry = point, Print = now, Want = (string)null },
                };
                foreach (var scene in scenes)
                {
                    ResultData rd = doc.ActiveResultData.Clone();
                    var eff = new EfficiencyConfigData();
                    eff.Geometry = scene.Geometry;
                    eff.Curve = new List<ROIEfficiencyData>
                    {
                        new ROIEfficiencyData { Energy = 100.0, Efficiency = 0.3 },
                        new ROIEfficiencyData { Energy = 662.0, Efficiency = 0.1 },
                    };
                    eff.GeometryFingerprint = scene.Print;
                    rd.Efficiency = eff;

                    var session = new FsaAnalysisSession();
                    Plant(session, new FsaResult { Chi2Ndf = 1.0, Chi2NdfPoisson = 1.0, BackgroundUsed = true, EfficiencyUsed = true },
                          "curve-cause");
                    using (var report = new FSAReportView(mainForm))
                    {
                        report.SetProbeSource(session, rd);
                        TableModel model = report.ReportTable.TableModel;
                        var marked = new List<string>();
                        bool valueRight = true;
                        for (int i = 0; i < model.Rows.Count; i++)
                        {
                            string caption = model.Rows[i].Cells[0].Text;
                            if (caption == offsetNote || caption == otherGeometry)
                            {
                                marked.Add(caption);
                                valueRight &= model.Rows[i].Cells[1].Text == value;
                            }
                        }
                        Console.WriteLine("  {0}: {1} — пометки: {2}", lang, scene.What,
                                          marked.Count == 0 ? "нет" : "«" + string.Join("» «", marked) + "»");
                        Same(lang + ": " + scene.What + " — пометка кривой", scene.Want ?? "нет",
                             marked.Count == 0 ? "нет" : string.Join(" | ", marked));
                        if (scene.Want != null)
                        {
                            Same(lang + ": " + scene.What + " — слово справа прежнее", true, valueRight);
                        }
                    }
                }
            }

            Language("en-US");
        }

        /// <summary>
        /// (`AMBER17`, решение Amber 11.09.2026 «ВКЛ умолчанием — привязка
        /// работает сама, слой показывает опоры»; П105 18.09.2026) ОЖИДАЕМЫЕ
        /// СТРОКИ ПРИВЯЗКИ ШКАЛЫ — ПО РЕЗУЛЬТАТУ СЦЕНЫ, а не числом «пометок
        /// три». Окно печатает строку опор ВСЕГДА при живой матрице
        /// (`FSAReportView.AddAnchorRow`): «none» при нуле опор — шкала как в
        /// калибровке; при опорах — их число и ещё две строки, усиление в % и
        /// ноль в кэВ. Без матрицы строк нет: синего канала, по которому ищут
        /// опоры, без неё не бывает. С 11.09 по 18.09.2026 проба этого не
        /// знала и была красна «пометок 4 вместо 3» на всякой сцене с матрицей.
        ///
        /// Числа — литералами вызывающего (ожидание пишется рукой, по сцене,
        /// а не берётся у окна); цвет — чёрный: это строки ЧИСЛА, не слова
        /// состояния, и красить их нечем (`A281`, `MakeNumberRow`).
        /// </summary>
        static void AnchorMarks(List<string[]> marks, FsaResult scene, string count, string gain, string zero)
        {
            if (!scene.ResponseMatrixUsed) return;
            if (scene.ScaleAnchorsUsed == 0)
            {
                marks.Add(new[] { Own("FSAReport_AnchorRow"), Own("FSAReport_AnchorNone"), Plain });
                return;
            }

            marks.Add(new[] { Own("FSAReport_AnchorRow"), count, Plain });
            marks.Add(new[] { Own("FSAReport_AnchorGainRow"), gain, Plain });
            marks.Add(new[] { Own("FSAReport_AnchorZeroRow"), zero, Plain });
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
        /// (`AMBER17`, П105) Метка ожидаемого цвета: строка ЧИСЛА — чёрная,
        /// как σ× (`A281`): у числа опор, усиления и нуля нет факта
        /// «учтено/не учтено», судить который мог бы цвет.
        /// </summary>
        const string Plain = "0";

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
                                  model.Rows[i].Cells[0].Text, model.Rows[i].Cells[1].Text,
                                  Paint(model.Rows[i].Cells[1].ForeColor));
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

            if (model.Rows[rule].Cells[0].Text.Length > 0 || model.Rows[rule].Cells[1].Text.Length > 0)
            {
                bad.Add("черта не пуста: «" + model.Rows[rule].Cells[0].Text + "»");
            }

            if (rule + 1 >= model.Rows.Count)
            {
                bad.Add("после черты нет заголовка");
                return bad;
            }

            Row header = model.Rows[rule + 1];
            if (header.Cells[0].Text != Own("FSAReport_QualityHeader"))
            {
                bad.Add("заголовок «" + header.Cells[0].Text + "» вместо «" + Own("FSAReport_QualityHeader") + "»");
            }

            if (header.Cells[0].Font == null || !header.Cells[0].Font.Bold)
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
            else if (model.Rows[rule + 2].Cells[0].Text != Own("FSAReport_ResidualRow"))
            {
                bad.Add("подпись невязки «" + model.Rows[rule + 2].Cells[0].Text + "»");
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

            if (model.Rows[chi].Cells[0].Text != Own("FSAReport_Chi2Row"))
            {
                bad.Add("подпись χ²/ndf «" + model.Rows[chi].Cells[0].Text + "»");
            }

            if (model.Rows[chi].Cells[1].Text != chi2Value)
            {
                bad.Add("значение χ²/ndf «" + model.Rows[chi].Cells[1].Text + "» вместо «" + chi2Value + "»");
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

            if (model.Rows[chi + 1].Cells[0].Text != Own("FSAReport_InflationRow"))
            {
                bad.Add("подпись σ× «" + model.Rows[chi + 1].Cells[0].Text + "» вместо «"
                        + Own("FSAReport_InflationRow") + "»");
            }

            if (model.Rows[chi + 1].Cells[1].Text != inflateValue)
            {
                bad.Add("значение σ× «" + model.Rows[chi + 1].Cells[1].Text + "» вместо «"
                        + inflateValue + "»");
            }

            if (string.IsNullOrEmpty(model.Rows[chi + 1].Cells[0].ToolTipText)
                || model.Rows[chi + 1].Cells[0].ToolTipText == model.Rows[chi + 1].Cells[0].Text)
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
                if (row.Cells[0].Text != marks[k][0] || row.Cells[1].Text != marks[k][1])
                {
                    bad.Add("пометка " + (k + 1) + " «" + row.Cells[0].Text + " | " + row.Cells[1].Text
                            + "» вместо «" + marks[k][0] + " | " + marks[k][1] + "»");
                }

                // (`AMBER11`) ЦВЕТ СЛОВА СОСТОЯНИЯ. Судится ячейка ЗНАЧЕНИЯ:
                // подпись остаётся чёрной по решению Amber 09.09.2026, и её
                // почернение проверяется отдельной жалобой ниже.
                if (marks[k].Length > 2)
                {
                    Color want = marks[k][2] == Good ? GoodColor : marks[k][2] == Plain ? Color.Black : BadColor;
                    if (row.Cells[1].ForeColor.ToArgb() != want.ToArgb())
                    {
                        bad.Add("цвет пометки " + (k + 1) + " «" + row.Cells[1].Text + "» — "
                                + Paint(row.Cells[1].ForeColor) + " вместо " + Paint(want));
                    }

                    if (row.Cells[0].ForeColor.ToArgb() != Color.Black.ToArgb())
                    {
                        bad.Add("подпись пометки " + (k + 1) + " окрашена в " + Paint(row.Cells[0].ForeColor)
                                + ", а должна быть чёрной");
                    }
                }

                if (Trimmed(row.Cells[0].Text))
                {
                    bad.Add("пометка " + (k + 1) + " усечена многоточием");
                }

                if (!row.Cells[0].WordWrap)
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
                    // (`AMBER269`, 02.11.2026) Прибор спектра — правилом открытия файла
                    // (`DocumentManager.AttachDeviceConfig`: Guid, затем номер, затем имя), а
                    // не одним Guid. Зовётся на ПОДСТАВНОМ спектре с той же ссылкой и тем же
                    // спектром (номер прибора — в нём): вызов ставит найденному спектру
                    // свежую копию настроек прибора, а у живого документа настройки свои
                    // (флаги, переключённые разделами выше) — подменять их нельзя.
                    var lookup = new ResultData
                    {
                        DeviceConfigReference = doc.ActiveResultData.DeviceConfigReference,
                        EnergySpectrum = doc.ActiveResultData.EnergySpectrum
                    };
                    DocumentManager.DeviceMatch how;
                    List<DeviceConfigInfo> candidates;
                    DeviceConfigInfo device = DocumentManager.AttachDeviceConfig(
                        lookup, DeviceConfigManager.GetInstance().DeviceConfigList, out how, out candidates);
                    bool? deviceWas = device != null ? ((FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig).AtomicXrayForFsa : (bool?)null;
                    xray.Checked = !was;
                    WaitIdle(session);
                    Same("флаг: конфигурация спектра записана", !was, cfg.AtomicXrayForFsa);
                    Same("флаг: отпечаток сменился", false, session.Stamp == stamp);
                    Same("флаг: запуск ровно один", runs + 1, session.RunCount);
                    Same("флаг: событие завершения ровно одно", done + 1, completed);
                    Same("флаг: результат посчитан новым отпечатком", session.Stamp,
                         FsaAnalysisSession.BuildStamp(doc.ActiveResultData, doc.ActiveResultData.BackgroundEnergySpectrum != null));
                    // (`AMBER212`) окно отчёта прибор больше не пишет: семь флажков —
                    // общая настройка приложения, а у проб её нет и пишется копия спектра.
                    Same("флаг: умолчание прибора НЕ тронуто", deviceWas,
                         device != null ? ((FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig).AtomicXrayForFsa : (bool?)null);

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
                // (`AMBER264`) подсказки окна — блоком по 80 знаков: эталон через тот же перенос
                Same("по пикам: подсказка родителей — «нужен NucBase+равновесие»",
                     BecquerelMonitor.AppUi.WrapTip(BecquerelMonitor.Properties.Resources.FSAReportTipParentsNeedNucBase),
                     report.ToolTipOf(parents));
                Same("по пикам: подсказка равновесия — «нужен NucBase»",
                     BecquerelMonitor.AppUi.WrapTip(BecquerelMonitor.Properties.Resources.FSAReportTipEquilibriumNeedsNucBase),
                     report.ToolTipOf(equilibrium));
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
                     BecquerelMonitor.AppUi.WrapTip(BecquerelMonitor.Properties.Resources.FSAReportTipGrouping),
                     report.ToolTipOf(parents));

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
                  .Append('|').Append(row.Cells.Count > 0 ? row.Cells[0].Text : string.Empty)
                  .Append('|').Append(row.Cells.Count > 1 ? row.Cells[1].Text : string.Empty)
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

        // ------------------------------------------------------------------
        // 14. ОТКАЗ РАЗБОРА НАЗЫВАЕТ ПРИЧИНУ (`A312`)
        // ------------------------------------------------------------------

        /// <summary>
        /// (`A312`, П118 21.09.2026) Сцена Amber `Am-241` (калибровка 08.09.2026)
        /// воспроизводится на контроле: состав из NucBase БЕЗ нуклидов (пики
        /// сняты), в библиотеке остаётся один образ собственного рентгена
        /// кристалла, живая матрица отклика снимает его (`AMBER4`) — библиотека
        /// пуста, `Analyze` отдаёт null. Окно отчёта обязано назвать причину
        /// словами `FSALibraryEmptied` (en и ru), а не общим `FSANotPossible`,
        /// каким оно называло её до 21.09.2026.
        ///
        /// Матрица — МАЛАЯ, считается здесь же (10 узлов × 4000 историй, как у
        /// `BoxCylStampProbeF64`; секунды) под СВОИМ guid кривой `a312-probe-…`:
        /// файл в складе проб живёт только на время раздела и снимается в
        /// finally; ни один настоящий спектр на такой guid не ссылается, и
        /// брошенный при падении файл никому не годен. ⚠ Писать матрицу под
        /// guid КОНТРОЛЯ нельзя: она осталась бы годной ему и молча сменила бы
        /// разбор всех разделов выше при следующем прогоне.
        ///
        /// Положительные контроли: (а) тот же вход БЕЗ матрицы — образ
        /// рентгена остаётся, разбор идёт; (б) причина на анализаторе —
        /// `Refusal == LibraryEmptied`, подробность называет снятый рентген
        /// кристалла, `GeometryRefused` ложен; (в) остальные причины
        /// различимы — вырожденный вход, гейт геометрии, мало каналов, узкая
        /// полоса — каждая своим членом; (г) у каждого члена перечисления свои
        /// слова в обеих культурах, попарно разные, и только неизвестное
        /// значение падает в `FSANotPossible`.
        /// </summary>
        static void RefusalSection(MainForm mainForm, string controlPath, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== 14. отказ разбора называет причину (A312): библиотека, опустевшая с матрицей ===");
            DocEnergySpectrum doc = Open(controlPath, nuclides);
            if (doc == null)
            {
                Same("документ сцены A312 открыт", true, false);
                return;
            }

            ResultData rd = doc.ActiveResultData;
            string guid = "a312-probe-" + Guid.NewGuid().ToString("N");
            FsaBandMode bandWas = FsaBand.DefaultMode;
            try
            {
                if (rd.Efficiency == null || !rd.Efficiency.HasGeometry || rd.EnergySpectrum == null
                    || rd.FwhmCalibration == null)
                {
                    Same("у контроля есть кривая с геометрией и калибровки (иначе сцену не построить)", true, false);
                    return;
                }

                // Состав из NucBase без единого нуклида: пиков нет — вывод
                // состава пуст, остаются приборные образы. Кривая — под guid
                // пробы, матрица включена, атомный рентген включён.
                SetSource(doc, true);
                var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                cfg.AtomicXrayForFsa = true;
                rd.DetectedPeaks = new List<Peak>();
                rd.Efficiency.Guid = guid;
                rd.Efficiency.UseResponseMatrix = true;

                // Библиотека сцены — как её соберёт сеанс: образы есть, и все от кристалла.
                FsaCompositionInference.Report inferred;
                FsaSampleSpec spec = FsaCompositionInference.Infer(rd.DetectedPeaks, rd, out inferred);
                spec.AtomicXray = true;
                List<FsaComponent> library = FsaSampleLibrary.Build(spec);
                int fromCrystal = 0;
                var names = new List<string>();
                foreach (FsaComponent c in library)
                {
                    names.Add(c.Name);
                    if (c.FromCrystal) fromCrystal++;
                }

                Console.WriteLine("  состав сцены: " + inferred);
                Console.WriteLine("  библиотека сцены: " + (names.Count == 0 ? "(пусто)" : string.Join(", ", names)));
                Same("сцена: библиотека без нуклидов не пуста", true, library.Count > 0);
                Same("сцена: все образы библиотеки — рентген кристалла", library.Count, fromCrystal);

                // Малая матрица под guid пробы — в склад рядом с пробой, как её ищет сеанс.
                var options = new ResponseMatrixOptions { NodeCount = 10, Histories = 4000, BinKev = 4.0 };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ResponseMatrix matrix = ResponseMatrixBuilder.Build(rd.Efficiency.Geometry, options, null,
                                                                    CancellationToken.None);
                ResponseMatrixStore.Save(guid, matrix);
                Console.WriteLine("  малая матрица: {0} узлов × {1} историй, {2} с, {3}",
                                  options.NodeCount, options.Histories,
                                  sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
                                  ResponseMatrixStore.PathOf(guid));
                ResponseMatrix stored = ResponseMatrixStore.Load(guid);
                Same("матрица прочитана из склада и годна геометрии сцены", true,
                     stored != null && stored.IsValidFor(rd.Efficiency.Geometry));

                // (б) Причина на анализаторе — тем же входом, что у сеанса.
                var analyzer = new FsaAnalyzer();
                FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, stored);
                FsaTuningReport.Print(analyzer);
                FsaResult direct = analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library,
                                                    FsaEfficiency.FromConfig(rd.Efficiency));
                Same("анализатор: с матрицей разбора нет", true, direct == null);
                Same("анализатор: причина — LibraryEmptied", FsaRefusal.LibraryEmptied, analyzer.Refusal);
                Same("анализатор: GeometryRefused ложен", false, analyzer.GeometryRefused);
                Same("анализатор: подробность называет снятый рентген кристалла", true,
                     analyzer.RefusalNote != null
                     && analyzer.RefusalNote.Contains("crystal x-ray " + fromCrystal.ToString(CultureInfo.InvariantCulture)));
                Console.WriteLine("  подробность: " + analyzer.RefusalNote);

                // (а) Контроль: без матрицы тот же вход разбирается.
                var bare = new FsaAnalyzer();
                FsaMatrixBinding.Bind(bare, rd.Efficiency.Geometry, null);
                FsaResult bareResult = bare.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library,
                                                    FsaEfficiency.FromConfig(rd.Efficiency));
                Same("контроль: без матрицы разбор идёт", true, bareResult != null);
                Same("контроль: без матрицы причина — None", FsaRefusal.None, bare.Refusal);
                Same("контроль: без матрицы подробности нет", null, bare.RefusalNote);

                // Окно отчёта — слова в обеих культурах, через живой сеанс документа.
                foreach (string lang in new[] { "en-US", "ru-RU" })
                {
                    Language(lang);
                    doc.FsaSession.Reset();
                    doc.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
                    using (var report = new FSAReportView(mainForm))
                    {
                        report.ProbeConsumer = true;
                        report.SetDocument(doc);
                        WaitIdle(doc.FsaSession);
                        report.RefreshReport();
                        string status = doc.FsaSession.Status;
                        string expected = BecquerelMonitor.Properties.Resources.FSALibraryEmptied;
                        Same(lang + ": результата сеанса нет", true, doc.FsaSession.Result == null);
                        Same(lang + ": состояние сеанса — FSALibraryEmptied", expected, status);
                        Same(lang + ": состояние — не общее FSANotPossible", false,
                             string.Equals(status, BecquerelMonitor.Properties.Resources.FSANotPossible, StringComparison.Ordinal));
                        int statusRows = 0;
                        string rowText = null;
                        foreach (Row row in report.ReportTable.TableModel.Rows)
                        {
                            var tag = (FsaReportRow)row.Tag;
                            if (tag.Kind == FsaReportRowKind.Status)
                            {
                                statusRows++;
                                rowText = tag.Name;
                            }
                        }

                        Same(lang + ": в таблице одна строка состояния", 1, statusRows);
                        Same(lang + ": строка таблицы несёт причину", expected, rowText);
                        Console.WriteLine("  {0}: {1}", lang, status);
                        report.SetDocument(null);
                    }
                }

                Language("en-US");

                // (в) Остальные причины различимы — на анализаторе, без матрицы.
                var other = new FsaAnalyzer();
                Same("вход: null-спектр — Input", FsaRefusal.Input,
                     Refusal(other, other.Analyze(null, null, rd.FwhmCalibration, library, FsaEfficiency.FromConfig(rd.Efficiency))));
                Same("вход: без калибровки ПШПВ — Input", FsaRefusal.Input,
                     Refusal(other, other.Analyze(rd.EnergySpectrum, null, null, library, FsaEfficiency.FromConfig(rd.Efficiency))));
                Same("вход: пустая библиотека на входе — Input", FsaRefusal.Input,
                     Refusal(other, other.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, new List<FsaComponent>(), FsaEfficiency.FromConfig(rd.Efficiency))));
                other.RequireGeometry = true;
                Same("геометрия: кривой нет — Geometry (A277)", FsaRefusal.Geometry,
                     Refusal(other, other.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library, null)));
                Same("геометрия: GeometryRefused читает тот же признак", true, other.GeometryRefused);
                EnergySpectrum few = rd.EnergySpectrum.Clone();
                few.NumberOfChannels = FsaAnalyzer.MinChannels - 1;
                few.Spectrum = new int[FsaAnalyzer.MinChannels - 1];
                Same("каналы: меньше MinChannels — FewChannels", FsaRefusal.FewChannels,
                     Refusal(other, other.Analyze(few, null, rd.FwhmCalibration, library, FsaEfficiency.FromConfig(rd.Efficiency))));
                Same("каналы: подробность называет число каналов", true,
                     other.RefusalNote != null && other.RefusalNote.Contains("channels=" + (FsaAnalyzer.MinChannels - 1).ToString(CultureInfo.InvariantCulture)));
                FsaBand.DefaultMode = FsaBandMode.FitToLibrary;
                other.MinEnergy = 661.0;
                other.MaxEnergy = 661.0;
                Same("полоса: FitToLibrary с пустым диапазоном — NarrowBand", FsaRefusal.NarrowBand,
                     Refusal(other, other.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library, FsaEfficiency.FromConfig(rd.Efficiency))));
                Console.WriteLine("  полоса: " + other.RefusalNote);
                FsaBand.DefaultMode = bandWas;

                // (г) Слова: у каждого члена свои, попарно разные, не FSANotPossible; неизвестное — FSANotPossible.
                foreach (string lang in new[] { "en-US", "ru-RU" })
                {
                    Language(lang);
                    string generic = BecquerelMonitor.Properties.Resources.FSANotPossible;
                    var texts = new HashSet<string>(StringComparer.Ordinal);
                    foreach (FsaRefusal why in Enum.GetValues(typeof(FsaRefusal)))
                    {
                        if (why == FsaRefusal.None) continue;
                        string text = FsaAnalysisSession.RefusalText(why, null);
                        Same(lang + ": слова у " + why + " есть и не общие", true,
                             !string.IsNullOrEmpty(text) && !string.Equals(text, generic, StringComparison.Ordinal));
                        Same(lang + ": слова у " + why + " свои (не повтор другой причины)", true, texts.Add(text));
                    }

                    Same(lang + ": геометрия с отвергнутой кривой — слова кривой (AMBER34)", true,
                         FsaAnalysisSession.RefusalText(FsaRefusal.Geometry, "probe").Contains("probe"));
                    Same(lang + ": неизвестная причина — общее FSANotPossible", generic,
                         FsaAnalysisSession.RefusalText((FsaRefusal)999, null));
                }

                Language("en-US");
            }
            finally
            {
                FsaBand.DefaultMode = bandWas;
                Language("en-US");
                ResponseMatrixStore.Delete(guid);
                Same("уборка: файл малой матрицы снят из склада", false, File.Exists(ResponseMatrixStore.PathOf(guid)));
                doc.Dispose();
            }
        }

        /// <summary>
        /// 15. (`AMBER51`, П120 22.09.2026) СПЕКТР БЕЗ ВРЕМЕНИ — отказ с
        /// причиной, а не знаменатель 1 с молча. На копии контрольного спектра
        /// обнуляются `LiveTime` и `MeasurementTime` (`EffectiveLiveTime` = 0):
        /// анализатор обязан отказать `NoLiveTime` с подробностью
        /// `liveTime=0 measurementTime=0`; сеанс документа — печатать
        /// `FSANoLiveTime` состоянием и строкой таблицы в обеих культурах.
        /// Положительные контроли: (а) живое время ноль, полное есть —
        /// разбор идёт (знаменатель — полное, `AMBER35`); (б) времена
        /// возвращены — скорости счёта и доли компонентов равны разбору
        /// нетронутого клона до бита; (в) сеанс с возвращённым временем даёт
        /// результат, а не состояние отказа.
        /// </summary>
        static void NoLiveTimeSection(MainForm mainForm, string controlPath, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== 15. спектр без времени — отказ с причиной (AMBER51), не знаменатель 1 с ===");
            DocEnergySpectrum doc = Open(controlPath, nuclides);
            if (doc == null)
            {
                Same("документ контроля открыт", true, false);
                return;
            }

            try
            {
                ResultData rd = doc.ActiveResultData;
                if (rd.EnergySpectrum == null || rd.FwhmCalibration == null || rd.Efficiency == null
                    || !rd.Efficiency.HasGeometry)
                {
                    Same("у контроля есть спектр, калибровка ПШПВ и кривая с геометрией", true, false);
                    return;
                }

                EnergySpectrum intact = rd.EnergySpectrum.Clone();
                double liveWas = rd.EnergySpectrum.LiveTime, realWas = rd.EnergySpectrum.MeasurementTime;
                Console.WriteLine("  контроль: живое {0} с, полное {1} с, EffectiveLiveTime {2} с",
                                  liveWas.ToString("F3", CultureInfo.InvariantCulture),
                                  realWas.ToString("F3", CultureInfo.InvariantCulture),
                                  rd.EnergySpectrum.EffectiveLiveTime.ToString("F3", CultureInfo.InvariantCulture));
                Same("у контроля есть время (иначе сцену не построить)", true, rd.EnergySpectrum.EffectiveLiveTime > 0.0);

                // Библиотека — как её собирает сеанс по пикам контроля.
                List<FsaComponent> library = FsaLibrary.BuildFromPeaks(rd.DetectedPeaks, nuclides.NuclideDefinitions);
                Same("библиотека контроля не пуста", true, library.Count > 0);
                FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);

                // Опора: нетронутый клон.
                var reference = new FsaAnalyzer();
                FsaResult referenceResult = reference.Analyze(intact, null, rd.FwhmCalibration, library, efficiency);
                Same("опора: нетронутый клон разбирается", true, referenceResult != null);

                // (1) Без времени — отказ с причиной.
                EnergySpectrum timeless = intact.Clone();
                timeless.LiveTime = 0.0;
                timeless.MeasurementTime = 0.0;
                Same("без времени: EffectiveLiveTime равен нулю", 0.0, timeless.EffectiveLiveTime);
                var analyzer = new FsaAnalyzer();
                FsaResult none = analyzer.Analyze(timeless, null, rd.FwhmCalibration, library, efficiency);
                Same("без времени: разбора нет", true, none == null);
                Same("без времени: причина — NoLiveTime", FsaRefusal.NoLiveTime, analyzer.Refusal);
                Same("без времени: подробность несёт оба времени", "liveTime=0 measurementTime=0", analyzer.RefusalNote);
                Same("без времени: GeometryRefused ложен", false, analyzer.GeometryRefused);

                // (а) Живое время ноль, полное есть — знаменатель полное, разбор идёт (AMBER35).
                EnergySpectrum realOnly = intact.Clone();
                realOnly.LiveTime = 0.0;
                realOnly.MeasurementTime = realWas > 0.0 ? realWas : 100.0;
                var onlyReal = new FsaAnalyzer();
                FsaResult realResult = onlyReal.Analyze(realOnly, null, rd.FwhmCalibration, library, efficiency);
                Same("контроль (а): только полное время — разбор идёт", true, realResult != null);
                Same("контроль (а): причина — None", FsaRefusal.None, onlyReal.Refusal);
                if (realResult != null)
                {
                    Same("контроль (а): знаменатель результата — полное время", realOnly.MeasurementTime, realResult.LiveTime);
                }

                // (б) Времена возвращены — до бита как у нетронутого клона.
                EnergySpectrum restored = timeless.Clone();
                restored.LiveTime = liveWas;
                restored.MeasurementTime = realWas;
                var again = new FsaAnalyzer();
                FsaResult restoredResult = again.Analyze(restored, null, rd.FwhmCalibration, library, efficiency);
                Same("контроль (б): с возвращённым временем разбор идёт", true, restoredResult != null);
                if (referenceResult != null && restoredResult != null)
                {
                    Same("контроль (б): число компонентов равно опоре", referenceResult.Components.Count, restoredResult.Components.Count);
                    int mismatched = 0;
                    for (int i = 0; i < referenceResult.Components.Count && i < restoredResult.Components.Count; i++)
                    {
                        FsaComponentResult a = referenceResult.Components[i], b = restoredResult.Components[i];
                        if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                            || a.CountRate != b.CountRate || a.SharePercent != b.SharePercent)
                        {
                            mismatched++;
                        }
                    }

                    Same("контроль (б): скорости и доли всех компонентов равны опоре до бита", 0, mismatched);
                    Same("контроль (б): χ²/ndf равен опоре до бита", referenceResult.Chi2Ndf, restoredResult.Chi2Ndf);
                    Same("контроль (б): знаменатель результата — живое время спектра", intact.EffectiveLiveTime, restoredResult.LiveTime);
                }

                // (2) Окно отчёта — слова в обеих культурах через живой сеанс документа.
                rd.EnergySpectrum.LiveTime = 0.0;
                rd.EnergySpectrum.MeasurementTime = 0.0;
                foreach (string lang in new[] { "en-US", "ru-RU" })
                {
                    Language(lang);
                    doc.FsaSession.Reset();
                    doc.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
                    using (var report = new FSAReportView(mainForm))
                    {
                        report.ProbeConsumer = true;
                        report.SetDocument(doc);
                        WaitIdle(doc.FsaSession);
                        report.RefreshReport();
                        string status = doc.FsaSession.Status;
                        string expected = BecquerelMonitor.Properties.Resources.FSANoLiveTime;
                        Same(lang + ": результата сеанса нет", true, doc.FsaSession.Result == null);
                        Same(lang + ": состояние сеанса — FSANoLiveTime", expected, status);
                        Same(lang + ": состояние — не общее FSANotPossible", false,
                             string.Equals(status, BecquerelMonitor.Properties.Resources.FSANotPossible, StringComparison.Ordinal));
                        int statusRows = 0;
                        string rowText = null;
                        foreach (Row row in report.ReportTable.TableModel.Rows)
                        {
                            var tag = (FsaReportRow)row.Tag;
                            if (tag.Kind == FsaReportRowKind.Status)
                            {
                                statusRows++;
                                rowText = tag.Name;
                            }
                        }

                        Same(lang + ": в таблице одна строка состояния", 1, statusRows);
                        Same(lang + ": строка таблицы несёт причину", expected, rowText);
                        Console.WriteLine("  {0}: {1}", lang, status);
                        report.SetDocument(null);
                    }
                }

                Language("en-US");

                // (в) Времена возвращены документу — сеанс даёт результат, не отказ.
                rd.EnergySpectrum.LiveTime = liveWas;
                rd.EnergySpectrum.MeasurementTime = realWas;
                doc.FsaSession.Reset();
                using (var report = new FSAReportView(mainForm))
                {
                    report.ProbeConsumer = true;
                    report.SetDocument(doc);
                    WaitIdle(doc.FsaSession);
                    Same("контроль (в): сеанс с возвращённым временем дал результат", true, doc.FsaSession.Result != null);
                    Same("контроль (в): состояние сеанса — не FSANoLiveTime", false,
                         string.Equals(doc.FsaSession.Status, BecquerelMonitor.Properties.Resources.FSANoLiveTime, StringComparison.Ordinal));
                    report.SetDocument(null);
                }
            }
            finally
            {
                Language("en-US");
                doc.Dispose();
            }
        }

        /// <summary>Причина последнего разбора — при условии, что он действительно отказал.</summary>
        static FsaRefusal Refusal(FsaAnalyzer analyzer, FsaResult result)
        {
            if (result != null)
            {
                Console.WriteLine("   (разбор не отказал — причины нет)");
            }

            return analyzer.Refusal;
        }

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
            // Предел 120 с (было 30 с): 02.10.2026 разбор AS80_Th232Medal в сборке Debug
            // на загруженной машине (~70 %, соседний счёт) не укладывался в 30 с, и сторож
            // check_fsa_report_view краснел без дефекта — П220 и слияние П225 (CPU разбора
            // 8 с в Release, Debug вдвое медленнее). Зависший сеанс по-прежнему отказ.
            for (int i = 0; i < 2400; i++)
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
        /// фон, край сетки дрейфа, строки привязки шкалы (`AMBER17`: одна
        /// «опор нет» либо три при опорах — только при живой матрице) и
        /// подавленный состав — по признакам результата.
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
                // (`AMBER17`, П105) Привязка шкалы — ПО РЕЗУЛЬТАТУ: при живой
                // матрице строка опор есть всегда, при опорах к ней встают
                // усиление и ноль. Без матрицы — ни одной.
                if (result.ResponseMatrixUsed) marks += result.ScaleAnchorsUsed > 0 ? 3 : 1;
                if (result.CompositionSuppressed) marks++;
                // (`AMBER269`, 02.11.2026) предупреждения по полосе — по строке на признак
                if (result.BackgroundOversubtracted != null) marks++;
                if (result.ImagesAboveSpectrum != null) marks++;
                // (`AMBER269`) нижняя граница разбора — строкой блока, когда она известна
                if (FSAReportView.FitBoundText(result) != null) marks++;
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
        ///
        /// ⛔ (П108) Подпись невязки ждётся С ПОМЕТКАМИ `A300`/`S174`, как её
        /// строит `RefreshReport` по <paramref name="result"/> и спектру
        /// <paramref name="raw"/> (<see cref="ResidualCaption(FsaResult, int[])"/>).
        /// До П108 ждалась голая строка ресурса — и проверка проходила лишь
        /// пока в окне не было живого результата (П107 §9.1).
        /// </summary>
        static string BodyMismatch(FSAReportView report, FsaResult result, int[] raw)
        {
            string residualCaption = ResidualCaption(result, raw);
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
                string caption = model.Kind == FsaReportRowKind.Residual ? residualCaption : model.Name ?? string.Empty;
                if (row.Cells[0].Text != caption || row.Cells[1].Text != (model.Value ?? string.Empty))
                {
                    cells.Add("ячейки «" + row.Cells[0].Text + " | " + row.Cells[1].Text
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

        /// <summary>
        /// (П108) ПОДПИСЬ СТРОКИ НЕВЯЗКИ, КАК ЕЁ СТРОИТ `FSAReportView.RefreshReport`:
        /// голая строка ресурса плюс пометки `A300` (на скольких каналах показ
        /// подрезан — <see cref="FsaResult.ClampedChannels"/> по спектру) и
        /// `S174` (от какой энергии считано число — <see cref="FsaResult.ResidualFloorKev"/>,
        /// только когда пол режет полосу фита: <c>ResidualFloorChannel &gt; FirstChannel</c>).
        /// Ожидание считается ИЗ РЕЗУЛЬТАТА и ресурсов окна (`Own`), не текстом
        /// руками; сцепка « — » и «, » — правило окна, повторённое здесь
        /// нарочно: разойдётся — покраснеет ячейка, и это будет правдой.
        /// </summary>
        static string ResidualCaption(FsaResult result, int[] raw)
        {
            return ResidualCaption(result, result != null && raw != null ? result.ClampedChannels(raw) : 0);
        }

        /// <summary>То же с ЗАДАННЫМ числом подрезанных каналов — для подброса чужого числа в контроле.</summary>
        static string ResidualCaption(FsaResult result, int clamped)
        {
            var marks = new List<string>();
            // (`AMBER126`, П168) Шумовой пол — первой пометкой, от 0.05 %
            // (`FSAReportView.ResidualNoiseShownFloor`), формат `F1`.
            if (result != null && result.ResidualSharesDefined && result.ResidualNoiseShare >= 0.0005)
            {
                marks.Add(string.Format(CultureInfo.InvariantCulture, Own("FSAReport_ResidualNoise"),
                                        (100.0 * result.ResidualNoiseShare).ToString("F1", CultureInfo.InvariantCulture)));
            }

            if (clamped > 0)
            {
                marks.Add(string.Format(CultureInfo.InvariantCulture, Own("FSAReport_ResidualClamped"), clamped));
            }

            if (result != null && result.ResidualFloorKev > 0.0 && result.ResidualFloorChannel > result.FirstChannel)
            {
                marks.Add(string.Format(CultureInfo.InvariantCulture, Own("FSAReport_ResidualFloor"),
                                        result.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture)));
            }

            string caption = Own("FSAReport_ResidualRow");
            return marks.Count == 0 ? caption : caption + " — " + string.Join(", ", marks.ToArray());
        }

        /// <summary>Строка невязки в таблице окна — по `Tag.Kind`, не по тексту; null — её нет.</summary>
        static Row ResidualTableRow(FSAReportView report)
        {
            foreach (Row row in report.ReportTable.TableModel.Rows)
            {
                if (((FsaReportRow)row.Tag).Kind == FsaReportRowKind.Residual)
                {
                    return row;
                }
            }

            return null;
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
                    if (row.Cells[1].Text.Length > 0) parts.Add(row.Cells[1].Text);
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
                Chi2NdfPoisson = 12345.6789,
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

        // ------------------------------------------------------------------
        // 16. (`AMBER286`) СУММ-ПИКИ ПОИМЁННО ПОД СТРОКОЙ НУКЛИДА
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER286`) Вопрос Amber 02.11.2026 к снимку тория на AS80, дословно: «Почему
        /// указанных пиков нет в легенде?»; решения вопросником того же дня: «Должны быть видны
        /// в легенде Отчет FSA, где остальные сумм пики живут», «Под строкой нуклида — строки
        /// пиков (Рекомендую)», «Только заметные: видны на графике (Рекомендую)».
        /// </summary>
        static void SumPeakRowsSection(MainForm mainForm, DocEnergySpectrum doc, string spectrumPath,
                                       NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== 16. сумм-пики поимённо (AMBER286): строки под строкой нуклида, слияние в FWHM, только заметные ===");
            SumPeakSceneSection(mainForm, doc);
            SumPeakLiveSection(mainForm, spectrumPath, nuclides);
        }

        const int SumSceneChannels = 4096;
        const double SumSceneFwhm = 20.0;       // каналов; шкала сцены — 1 кэВ на канал
        const double SumSceneVariance = 100.0;  // на канал: σ окна ±FWHM (41–42 канала) ≈ 64, 3σ ≈ 192–194

        static FsaSumPeakLine SumSceneLine(string nuclide, double from, double with, double third, double counts)
        {
            double sum = from + with + third;
            return new FsaSumPeakLine
            {
                Nuclide = nuclide, FromKev = from, WithKev = with, ThirdKev = third, EnergyKev = sum,
                Counts = counts, Channel = sum, FwhmChannels = SumSceneFwhm
            };
        }

        static void AddGauss(double[] target, double centre, double area, double fwhm)
        {
            double sigma = fwhm / 2.3548;
            for (int i = 0; i < target.Length; i++)
            {
                double x = (i - centre) / sigma;
                if (Math.Abs(x) < 8.0)
                {
                    target[i] += area * Math.Exp(-0.5 * x * x) / (sigma * Math.Sqrt(2.0 * Math.PI));
                }
            }
        }

        static FsaComponentResult SumSceneMember(string name, double lineChannel, List<FsaSumPeakLine> lines)
        {
            var curve = new double[SumSceneChannels];
            var sums = new double[SumSceneChannels];
            AddGauss(curve, lineChannel, 200000.0, SumSceneFwhm);
            foreach (FsaSumPeakLine line in lines)
            {
                AddGauss(sums, line.Channel, line.Counts, line.FwhmChannels);
            }

            for (int i = 0; i < curve.Length; i++) curve[i] += sums[i];
            return new FsaComponentResult
            {
                Name = name, Kind = FsaComponentKind.Single, ChainRoot = "Th-232", DecayChainRoot = "Th-232",
                Curve = curve, SumPeakCurve = sums, SumPeakLines = lines, CountRate = 10.0, Z = 50.0
            };
        }

        /// <summary>
        /// Сцена тория с каскадами: у Ac-228 — пара 1065 (заметна), две пары ближе FWHM (1110.6 и
        /// 1123.0 — одна строка), слабая пара 1500 (<paramref name="weakCounts"/>) и тройка 1957.4;
        /// у Tl-208 — пара 1094.0. <paramref name="variance"/> = false — без дисперсии фита.
        /// </summary>
        static FsaResult SumScene(double weakCounts, bool variance)
        {
            var result = new FsaResult
            {
                FirstChannel = 20, LastChannel = 3000, Chi2Ndf = 1.5, Chi2NdfPoisson = 1.5, BackgroundUsed = true,
                ResponseMatrixUsed = true, EfficiencyUsed = true, CascadeSummingUsed = true,
                Model = new double[SumSceneChannels], Continuum = new double[SumSceneChannels]
            };
            result.Components.Add(SumSceneMember("Ac-228", 911.2, new List<FsaSumPeakLine>
            {
                SumSceneLine("Ac-228", 795.0, 270.2, 0.0, 1000.0),
                SumSceneLine("Ac-228", 772.3, 338.3, 0.0, 500.0),
                SumSceneLine("Ac-228", 795.0, 328.0, 0.0, 400.0),
                SumSceneLine("Ac-228", 1000.0, 500.0, 0.0, weakCounts),
                SumSceneLine("Ac-228", 911.2, 463.0, 583.2, 300.0)
            }));
            result.Components.Add(SumSceneMember("Tl-208", 583.2, new List<FsaSumPeakLine>
            {
                SumSceneLine("Tl-208", 583.2, 510.8, 0.0, 300.0)
            }));
            foreach (FsaComponentResult c in result.Components)
            {
                for (int i = 0; i < SumSceneChannels; i++) result.Model[i] += c.Curve[i];
            }

            if (variance)
            {
                result.FitVariance = new double[SumSceneChannels];
                for (int i = 0; i < SumSceneChannels; i++) result.FitVariance[i] = SumSceneVariance;
            }

            return result;
        }

        /// <summary>Строки рода SumPeakLine сразу под строкой «— сумм-пик» слоя <paramref name="layer"/>.</summary>
        static List<FsaReportRow> LinesUnder(List<FsaReportRow> rows, string layer)
        {
            var found = new List<FsaReportRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Kind != FsaReportRowKind.SumPeaks || rows[i].Layer == null || rows[i].Layer.Name != layer)
                {
                    continue;
                }

                for (int j = i + 1; j < rows.Count && rows[j].Kind == FsaReportRowKind.SumPeakLine; j++)
                {
                    found.Add(rows[j]);
                }
            }

            return found;
        }

        static string Names(List<FsaReportRow> rows)
        {
            var names = new List<string>();
            foreach (FsaReportRow row in rows) names.Add(row.Name + " | " + row.Value);
            return string.Join(" ; ", names.ToArray());
        }

        static void SumPeakSceneSection(MainForm mainForm, DocEnergySpectrum doc)
        {
            // σ окна сцены: ±FWHM вокруг центра, √(n·100). У 1500 (целый центр) n = 41: σ = 64.03, 3σ = 192.1.
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                Language(lang);
                string kev = lang == "ru-RU" ? "кэВ" : "keV";
                string counts = lang == "ru-RU" ? "отсч." : "counts";

                // (1) слабый пик 1500 — 100 отсчётов, ниже 3σ = 192: строки нет
                FsaResult weak = SumScene(100.0, true);
                FsaPresentation daughters = FsaPresentationBuilder.Build(weak, FsaGrouping.Daughters, false);
                List<FsaReportRow> ac = LinesUnder(daughters.Rows, "Ac-228");
                List<FsaReportRow> tl = LinesUnder(daughters.Rows, "Tl-208");
                Console.WriteLine("  {0} Ac-228: {1}", lang, Names(ac));
                Console.WriteLine("  {0} Tl-208: {1}", lang, Names(tl));
                Same(lang + ": под Ac-228 три строки (1065, слитые 1111+1123, тройка 1957; слабой 1500 нет)", 3, ac.Count);
                Same(lang + ": под Tl-208 одна строка (1094)", 1, tl.Count);
                if (ac.Count == 3)
                {
                    Same(lang + ": первая — «Σ 1065 " + kev + " = 795.0 + 270.2»", "Σ 1065 " + kev + " = 795.0 + 270.2", ac[0].Name);
                    Same(lang + ": её значение — «1000 " + counts + "»", "1000 " + counts, ac[0].Value);
                    Same(lang + ": слитая — средневзвешенная 1116, сильнейшая пара и «ещё 1»",
                         lang == "ru-RU" ? "Σ 1116 кэВ = 772.3 + 338.3 и ещё 1" : "Σ 1116 keV = 772.3 + 338.3 and 1 more",
                         ac[1].Name);
                    Same(lang + ": слитая — площадь 900", "900 " + counts, ac[1].Value);
                    Same(lang + ": в подсказке слитой — обе пары с суммами", true,
                         ac[1].Hint != null && ac[1].Hint.Contains("772.3 + 338.3 = 1110.6")
                         && ac[1].Hint.Contains("795.0 + 328.0 = 1123.0"));
                    Same(lang + ": в подсказке — порог 3σ числом (194 у окна 1046–1086…)", true,
                         ac[1].Hint != null && ac[1].Hint.Contains("3σ"));
                    Same(lang + ": тройная — тремя слагаемыми", "Σ 1957 " + kev + " = 911.2 + 463.0 + 583.2", ac[2].Name);
                    Same(lang + ": строки пиков — по энергии", true,
                         ac[0].Name.Contains("1065") && ac[1].Name.Contains("1116") && ac[2].Name.Contains("1957"));
                    Same(lang + ": строка пика — с отступом и своим слоем", true,
                         ac[0].Indent == 1 && ac[0].Layer != null && ac[0].Layer.Name == "Ac-228"
                         && ac[0].Swatch == FsaSwatchKind.None);
                    Denies(lang + ": контроль — слабый пик 1500 (100 < 3σ) строку получил", Names(ac).Contains("1500"));
                }

                if (tl.Count == 1)
                {
                    Same(lang + ": Tl-208 — «Σ 1094 " + kev + " = 583.2 + 510.8»", "Σ 1094 " + kev + " = 583.2 + 510.8", tl[0].Name);
                }

                // Группы — тем же построителем, что таблица: σ и признак заметности.
                List<FsaSumPeakGroup> groups = FsaPresentationBuilder.SumPeakGroups(weak, weak.Components[0].SumPeakLines);
                FsaSumPeakGroup g1500 = null;
                foreach (FsaSumPeakGroup g in groups) if (Math.Abs(g.EnergyKev - 1500.0) < 0.01) g1500 = g;
                Same(lang + ": групп у Ac-228 четыре (1065, 1111+1123, 1500, 1957)", 4, groups.Count);
                Same(lang + ": у слабой группы σ окна = √(41·100)", true,
                     g1500 != null && Math.Abs(g1500.Sigma - Math.Sqrt(41.0 * SumSceneVariance)) < 1e-9);
                Same(lang + ": слабая группа незаметна", false, g1500 != null && g1500.Shown);

                // (2) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: тот же пик 250 отсчётов (≥ 192) — строка есть
                FsaResult strong = SumScene(250.0, true);
                List<FsaReportRow> acStrong = LinesUnder(FsaPresentationBuilder.Build(strong, FsaGrouping.Daughters, false).Rows, "Ac-228");
                Same(lang + ": контроль — тот же пик 1500 выше 3σ (250) строку получает", true,
                     acStrong.Count == 4 && acStrong[2].Name == "Σ 1500 " + kev + " = 1000.0 + 500.0"
                     && acStrong[2].Value == "250 " + counts);

                // (3) порог — ровно 3σ: на пороге строка есть, чуть ниже — нет
                double threshold = FsaPresentationBuilder.SumPeakMinSigmas * Math.Sqrt(41.0 * SumSceneVariance);
                Same(lang + ": на пороге 3σ строка есть", 4,
                     LinesUnder(FsaPresentationBuilder.Build(SumScene(threshold, true), FsaGrouping.Daughters, false).Rows, "Ac-228").Count);
                Same(lang + ": на 0.999 порога строки нет", 3,
                     LinesUnder(FsaPresentationBuilder.Build(SumScene(0.999 * threshold, true), FsaGrouping.Daughters, false).Rows, "Ac-228").Count);

                // (4) без дисперсии фита заметность судить нечем — строк пиков нет, строка нуклида есть
                FsaPresentation blind = FsaPresentationBuilder.Build(SumScene(250.0, false), FsaGrouping.Daughters, false);
                int sumRows = 0, lineRows = 0;
                foreach (FsaReportRow row in blind.Rows)
                {
                    if (row.Kind == FsaReportRowKind.SumPeaks) sumRows++;
                    if (row.Kind == FsaReportRowKind.SumPeakLine) lineRows++;
                }

                Same(lang + ": без дисперсии фита — строк пиков нет", 0, lineRows);
                Same(lang + ": без дисперсии фита — строки «— сумм-пик» на месте", 2, sumRows);

                // (5) РОДИТЕЛИ: пики членов — под строкой родителя, слиты по FWHM уже вместе
                FsaPresentation parents = FsaPresentationBuilder.Build(weak, FsaGrouping.Parents, false);
                Same(lang + ": родители допустимы и применены", FsaGrouping.Parents, parents.Grouping);
                List<FsaReportRow> th = LinesUnder(parents.Rows, "Th-232");
                Console.WriteLine("  {0} Th-232 (родители): {1}", lang, Names(th));
                Same(lang + ": под Th-232 три строки (1065; 1094+1111+1123 слиты; 1957)", 3, th.Count);
                if (th.Count == 3)
                {
                    Same(lang + ": слитая у родителя — средневзвешенная 1111 и «ещё 2»",
                         lang == "ru-RU" ? "Σ 1111 кэВ = 772.3 + 338.3 и ещё 2" : "Σ 1111 keV = 772.3 + 338.3 and 2 more",
                         th[1].Name);
                    Same(lang + ": слитая у родителя — площадь 1200", "1200 " + counts, th[1].Value);
                    Same(lang + ": в подсказке родителя — пара Tl-208 с именем нуклида", true,
                         th[1].Hint != null && th[1].Hint.Contains("Tl-208: 583.2 + 510.8 = 1094.0"));
                    Same(lang + ": строка пика родителя — слой родителя", "Th-232", th[1].Layer != null ? th[1].Layer.Name : null);
                }

                int memberLines = LinesUnder(parents.Rows, "Ac-228").Count + LinesUnder(parents.Rows, "Tl-208").Count;
                Same(lang + ": у родителей под членами строк пиков нет (членов в стеке нет)", 0, memberLines);

                // (6) ОКНО: строки в таблице — те же, с отступом и подсказкой
                var session = new FsaAnalysisSession();
                Plant(session, weak, "amber286-" + lang);
                using (var report = new FSAReportView(mainForm))
                {
                    report.SetProbeSource(session, doc.ActiveResultData);
                    var lines = new List<Row>();
                    foreach (Row row in report.ReportTable.TableModel.Rows)
                    {
                        var model = row.Tag as FsaReportRow;
                        if (model != null && model.Kind == FsaReportRowKind.SumPeakLine) lines.Add(row);
                    }

                    Same(lang + ": окно — строк пиков 4 (Ac-228 три, Tl-208 одна)", 4, lines.Count);
                    bool cells = lines.Count > 0;
                    int indent = -1;
                    foreach (Row row in lines)
                    {
                        var model = (FsaReportRow)row.Tag;
                        cells &= row.Cells[0].Text == model.Name && row.Cells[1].Text == model.Value
                                 && row.Cells[0].ToolTipText == model.Hint;
                        indent = row.Cells[0].Padding.Left;
                    }

                    Same(lang + ": окно — подпись, значение и подсказка ячеек = модели", true, cells);
                    Same(lang + ": окно — отступ подписи строки пика 36 пикселей (образец + шаг)", 36, indent);
                    Denies(lang + ": контроль — ключ ресурса вместо перевода в строке пика",
                           lines.Count > 0 && (lines[0].Cells[0].Text.Contains("FSASumPeak") || lines[0].Cells[1].Text.Contains("FSASumPeak")));
                }
            }

            Language("en-US");
        }

        /// <summary>
        /// Живой разбор `--spectrum` с матрицей кривой из `--store=` в памяти: строки пиков у
        /// каждого слоя с сумм-пиками — ровно заметные группы того же построителя, в обоих
        /// режимах группировки; отсчёты пиков сходятся с нарисованным подслоем сумм.
        /// </summary>
        static void SumPeakLiveSection(MainForm mainForm, string spectrumPath, NuclideDefinitionManager nuclides)
        {
            if (string.IsNullOrEmpty(storeDir))
            {
                Console.WriteLine("  (живой торий с матрицей НЕ мерится: склад не дан ключом --store=; сцена выше мерится всегда)");
                return;
            }

            Language("en-US");
            DocEnergySpectrum doc = Open(spectrumPath, nuclides);
            if (doc == null)
            {
                Same("живой торий: спектр открылся", true, false);
                return;
            }

            try
            {
                ResultData rd = doc.ActiveResultData;
                string source = AttachMatrix(rd.Efficiency);
                Console.WriteLine("  матрица живого тория: {0}", source ?? "НЕТ");
                Same("живой торий: матрица кривой нашлась в --store", true, source != null);
                if (source == null)
                {
                    return;
                }

                rd.Efficiency.UseResponseMatrix = true;
                SetSource(doc, true);
                doc.FsaSession.Reset();
                doc.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
                using (var report = new FSAReportView(mainForm))
                {
                    report.ProbeConsumer = true;
                    report.SetDocument(doc);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 12000 && doc.FsaSession.IsRunning; i++)
                    {
                        Thread.Sleep(50);
                        Application.DoEvents();
                    }

                    Application.DoEvents();
                    report.RefreshReport();
                    FsaResult result = doc.FsaSession.Result;
                    Console.WriteLine("  разбор живого тория: {0:F1} с, состояние «{1}»", watch.Elapsed.TotalSeconds,
                                      doc.FsaSession.Status ?? "");
                    Same("живой торий: разбор есть, сеанс в покое", true, result != null && !doc.FsaSession.IsRunning);
                    if (result == null)
                    {
                        return;
                    }

                    Same("живой торий: матрица в разборе", true, result.ResponseMatrixUsed);
                    Same("живой торий: каскадное суммирование сработало", true, result.CascadeSummingUsed);
                    Same("живой торий: дисперсия фита в результате, длиной в модель", true,
                         result.FitVariance != null && result.Model != null && result.FitVariance.Length == result.Model.Length);

                    // Компоненты: пики поимённо есть ровно у тех, у кого есть подслой сумм; их
                    // отсчёты — та же величина, что нарисована подслоем (сумма по полосе).
                    int withLines = 0;
                    bool pairing = true;
                    foreach (FsaComponentResult c in result.Components)
                    {
                        pairing &= (c.SumPeakCurve != null) == (c.SumPeakLines != null && c.SumPeakLines.Count > 0);
                        if (c.SumPeakLines == null) continue;
                        withLines++;

                        // Подслой нарисован в полосе фита; пик у её края лежит в ней долей. Доля —
                        // гауссом с FWHM пика (форма матрицы близка к нему). Мерено 03.11.2026 на
                        // медальоне: у Tl-208 пять пиков из 22 стоят центром выше верхнего края
                        // полосы (суммы с 2614.5), и без поправки отношение 0.958, с ней 1.0022;
                        // у Ac-228, Pb-212, Bi-212 таких нет — 1.0000, 1.0000, 0.9998.
                        double lines = 0.0, inBand = 0.0, drawn = 0.0;
                        int outside = 0;
                        foreach (FsaSumPeakLine line in c.SumPeakLines)
                        {
                            double sigma = line.FwhmChannels / 2.3548;
                            double share = Phi((result.LastChannel + 0.5 - line.Channel) / sigma)
                                           - Phi((result.FirstChannel - 0.5 - line.Channel) / sigma);
                            lines += line.Counts;
                            inBand += line.Counts * share;
                            if (line.Channel < result.FirstChannel || line.Channel > result.LastChannel) outside++;
                        }

                        for (int i = result.FirstChannel; i <= result.LastChannel && i < c.SumPeakCurve.Length; i++) drawn += c.SumPeakCurve[i];
                        Console.WriteLine("SUMLINES\t{0}\tпиков {1} (центром вне полосы {2})\tΣ по пикам {3:F1}\tв полосе {4:F1}\tподслой {5:F1}\tотношение {6:F4}\tчлен ряда {7}",
                                          c.Name, c.SumPeakLines.Count, outside, lines, inBand, drawn,
                                          drawn > 0.0 ? inBand / drawn : double.NaN, c.ChainRoot ?? "—");
                        Same("живой торий: " + c.Name + " — отсчёты пиков в полосе сходятся с подслоем сумм (±1 %)", true,
                             drawn > 0.0 && Math.Abs(inBand / drawn - 1.0) <= 0.01);
                        Denies("живой торий: " + c.Name + " — контроль: отсчёты пиков ×1.02 сверку проходят",
                               drawn > 0.0 && Math.Abs(1.02 * inBand / drawn - 1.0) <= 0.01);
                    }

                    Same("живой торий: пики поимённо есть ровно у компонентов с подслоем сумм", true, pairing);
                    Same("живой торий: компонентов с сумм-пиками поимённо хотя бы один", true, withLines > 0);

                    // Строки: у каждого слоя — ровно заметные группы того же построителя, по энергии.
                    int shownTotal = 0;
                    foreach (FsaGrouping grouping in new[] { FsaGrouping.Daughters, FsaGrouping.Parents })
                    {
                        FsaPresentation presentation = FsaPresentationBuilder.Build(result, grouping, false);
                        foreach (FsaStackLayer layer in presentation.Layers)
                        {
                            if (layer.SumPeakCurve == null) continue;
                            int shown = 0;
                            var hidden = new List<string>();
                            foreach (FsaSumPeakGroup g in FsaPresentationBuilder.SumPeakGroups(result, layer.SumPeakLines))
                            {
                                if (g.Shown) shown++;
                                else hidden.Add(string.Format(CultureInfo.InvariantCulture, "{0:F0}({1:F0}<{2:F0})",
                                                              g.EnergyKev, g.Counts, FsaPresentationBuilder.SumPeakMinSigmas * g.Sigma));
                            }

                            List<FsaReportRow> under = LinesUnder(presentation.Rows, layer.Name);
                            Console.WriteLine("  {0} {1}: строк пиков {2}: {3}", grouping, layer.Name, under.Count, Names(under));
                            Console.WriteLine("  {0} {1}: незаметные группы ({2}): {3}", grouping, layer.Name, hidden.Count,
                                              string.Join(" ", hidden.ToArray()));
                            foreach (FsaReportRow row in under)
                            {
                                Console.WriteLine("    подсказка «{0}»: {1}", row.Name,
                                                  (row.Hint ?? string.Empty).Replace(Environment.NewLine, " / "));
                            }
                            Same("живой торий, " + grouping + ": " + layer.Name + " — строк пиков столько, сколько заметных групп", shown, under.Count);
                            bool ascending = true;
                            for (int i = 1; i < under.Count; i++)
                            {
                                ascending &= SumRowKev(under[i - 1]) <= SumRowKev(under[i]);
                            }

                            Same("живой торий, " + grouping + ": " + layer.Name + " — строки по энергии", true, ascending);
                            if (grouping == FsaGrouping.Daughters) shownTotal += under.Count;
                        }
                    }

                    Same("живой торий: строк пиков в легенде хотя бы одна (торий с каскадами)", true, shownTotal > 0);

                    // В окне — то же, что в модели.
                    int tableLines = 0, modelLines = 0;
                    foreach (Row row in report.ReportTable.TableModel.Rows)
                    {
                        var model = row.Tag as FsaReportRow;
                        if (model != null && model.Kind == FsaReportRowKind.SumPeakLine) tableLines++;
                    }

                    foreach (FsaReportRow row in report.BuildRows())
                    {
                        if (row.Kind == FsaReportRowKind.SumPeakLine) modelLines++;
                    }

                    Same("живой торий: строк пиков в окне столько же, сколько в модели", modelLines, tableLines);
                    report.SetDocument(null);
                }
            }
            finally
            {
                doc.Dispose();
            }
        }

        /// <summary>Функция нормального распределения (erf по Абрамовицу — Стигану 7.1.26, ошибка 1.5e-7).</summary>
        static double Phi(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * z);
            double erf = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
                               * t * Math.Exp(-z * z);
            return x >= 0.0 ? 0.5 * (1.0 + erf) : 0.5 * (1.0 - erf);
        }

        /// <summary>Энергия строки пика из её подписи («Σ 1065 keV = …» → 1065).</summary>
        static double SumRowKev(FsaReportRow row)
        {
            string name = row.Name ?? string.Empty;
            int start = name.IndexOf(' ') + 1;
            int end = start > 0 ? name.IndexOf(' ', start) : -1;
            double kev;
            return end > start && double.TryParse(name.Substring(start, end - start), NumberStyles.Float,
                                                  CultureInfo.InvariantCulture, out kev) ? kev : double.NaN;
        }

        /// <summary>Мелкая копия строки состава (все свойства с чтением и записью) — чтобы не трогать живой результат.</summary>
        static FsaComponentResult ShallowCopy(FsaComponentResult source)
        {
            var copy = new FsaComponentResult();
            foreach (PropertyInfo p in typeof(FsaComponentResult).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                {
                    p.SetValue(copy, p.GetValue(source, null), null);
                }
            }

            return copy;
        }

        /// <summary>
        /// Матрица кривой из `--store=` — в память блоком «матрица из файла спектра» (`AMBER202`),
        /// тем же путём, что у `FsaInputCheckProbeP195`. null — годной нет (причина напечатана).
        /// </summary>
        static string AttachMatrix(EfficiencyConfigData efficiency)
        {
            if (efficiency == null || string.IsNullOrEmpty(efficiency.Guid))
            {
                Console.WriteLine("  у спектра нет кривой — матрицу класть некуда");
                return null;
            }

            string file = Path.Combine(storeDir, efficiency.Guid + ".rmx");
            if (!File.Exists(file))
            {
                Console.WriteLine("  в --store нет матрицы кривой: {0}", file);
                return null;
            }

            ResponseMatrix matrix = ResponseMatrix.Load(file);
            if (matrix == null || !matrix.IsValidFor(efficiency.Geometry))
            {
                Console.WriteLine("  матрица {0} {1}", file, matrix == null ? "не читается этой сборкой" : "негодна для геометрии кривой");
                return null;
            }

            efficiency.EmbeddedMatrix = EmbeddedResponseMatrix.FromFile(efficiency.Guid, file, matrix);
            MatrixRefusal refusal;
            int format;
            ResponseMatrixSource source;
            string spectrumRefusal;
            ResponseMatrix carried = ResponseMatrixStore.Resolve(efficiency, out refusal, out format, out source,
                                                                 out spectrumRefusal);
            if (carried == null || source != ResponseMatrixSource.Spectrum)
            {
                Console.WriteLine("  матрица из --store положена в кривую, но разбор её не берёт ({0}; {1})", source, spectrumRefusal);
                return null;
            }

            return file + " → в память (AMBER202), клеймо " + matrix.Stamp;
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
