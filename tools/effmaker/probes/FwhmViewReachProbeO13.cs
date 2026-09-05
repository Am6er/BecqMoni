using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using XPTable.Editors;
using XPTable.Events;
using XPTable.Models;

namespace FwhmViewReachProbeO13
{
    /// <summary>
    /// ДОСТИЖИМОСТЬ ШЕСТИ ГОЛЫХ РАЗЫМЕНОВАНИЙ `ActiveResultData.FwhmCalibration`
    /// В `DCFwhmCalibrationView` (`A236`, полоса О13).
    ///
    /// ⛔ ЭТО ЗАМЕР, А НЕ ПОЧИНКА. Строка `A236` перечисляет шесть мест, где
    /// кривая разрешения берётся без сторожа (310, 396, 413, 838, 923, 952),
    /// и пять, где сторож есть (73, 342, 876, 895, 913). Вопрос строки один:
    /// ДОХОДИТ ли выполнение до голых мест, когда кривой нет.
    ///
    /// ⛔ ОТРИЦАТЕЛЬНЫЙ ВЫВОД БЕЗ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ НИЧЕГО НЕ ЗНАЧИТ.
    /// Проба, не дозвонившаяся до вида вовсе, напечатает «падений нет» — и это
    /// неотличимо от «дефекта нет». Поэтому здесь ДВА плеча контроля:
    ///
    ///   ПЛЕЧО 1 (проба достаёт до вида). Сцена «ЖИВАЯ»: кривая на месте, три
    ///     точки. Каждая дверь дёргается и обязана оставить СЛЕД ИМЕННО НА ТОЙ
    ///     СТРОКЕ, о которой спор: 310 — точек стало 2 (снятие), 413 — стало 4
    ///     (добавление), 838 — у точки сменилась ПШПВ, 923 — список очищен и
    ///     пересобран. Не «вид создался», а «эта самая строка исполнилась».
    ///
    ///   ПЛЕЧО 2 (проба ловит настоящее падение). Та же проба гоняется по
    ///     КОПИИ ДЕРЕВА, где у правильного места (сторож на 894-899 в
    ///     `GetAllPeaksButton_Click`) сторож СНЯТ. Проба обязана поймать
    ///     `NullReferenceException` и НАЗВАТЬ строку. Ключ `--control` только
    ///     печатает, какой сборкой гоняются, — разводить плечи руками нельзя.
    ///
    /// Сцены:
    ///   ЖИВАЯ        — документ пунктом меню (`CreateDocument`), кривая есть.
    ///   ПУСТАЯ       — кривой нет И умолчание НЕ СТРОИТСЯ: у конфигурации
    ///                  прибора прямая через (0, FWHM_AT_0) и (Ch_Fwhm,
    ///                  Width_Fwhm) не растёт (`ResultData.cs:470`). Это
    ///                  единственное состояние, где `EnsureFwhmCalibration`
    ///                  бессилен, а значит единственное, где голые места
    ///                  вообще могут ожить.
    ///   ВВОЗ-N42     — настоящий ввоз корпусного `.n42` (`--n42=`): дверь
    ///                  оставляет кривую пустой (`A234`), и надо ИЗМЕРИТЬ,
    ///                  переживает ли эта пустота обновление вида.
    ///
    ///   FwhmViewReachProbeO13.exe [--n42=&lt;каталог с *.n42&gt;] [--control]
    ///
    /// Код возврата: 0 — оба плеча контроля сошлись; 1 — контроль не сошёлся
    /// (замер недействителен); 2 — ключи не разобраны.
    /// </summary>
    static class Program
    {
        static string n42Dir;
        static bool control;
        static int badControl;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            foreach (string a in args)
            {
                if (a.StartsWith("--n42=")) n42Dir = a.Substring(6);
                else if (a == "--control") control = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            string asm = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + asm);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(asm).ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  плечо: " + (control
                ? "КОНТРОЛЬНОЕ (сторож 894-899 снят в копии дерева) — ждём ПАДЕНИЕ на 923"
                : "РАБОЧЕЕ (дерево как есть)"));
            Console.WriteLine();

            SceneLive();
            SceneNull();
            SceneN42();

            Console.WriteLine();
            Console.WriteLine("=== ИТОГ КОНТРОЛЯ ===");
            if (badControl == 0)
            {
                Console.WriteLine("  ОБА ПЛЕЧА СОШЛИСЬ — замер действителен");
            }
            else
            {
                Console.WriteLine("  КОНТРОЛЬ НЕ СОШЁЛСЯ: " + badControl + " — замер НЕДЕЙСТВИТЕЛЕН");
            }
            return badControl == 0 ? 0 : 1;
        }

        // ==================================================================
        // ПЛЕЧО 1. Сцена «ЖИВАЯ» — след на КАЖДОЙ из спорных строк.
        // ==================================================================
        static void SceneLive()
        {
            Console.WriteLine("=== СЦЕНА «ЖИВАЯ» — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, ПЛЕЧО 1 ===");
            Console.WriteLine("  вопрос: доходит ли проба ДО САМИХ спорных строк, когда кривая на месте");
            Console.WriteLine();

            Stand s = Stand.Make("o13-live", live: true);
            if (s == null) { Fail("стенд ЖИВАЯ не построился"); return; }

            s.Update();
            s.PrintState();

            // --- строка 310: RemovePeakButton_Click снимает точку -------------
            int before = s.Peaks.Count;
            Outcome o = s.Invoke("RemovePeakButton_Click", new object[] { null, EventArgs.Empty });
            int after = s.Peaks == null ? -1 : s.Peaks.Count;
            Door("310  RemovePeakButton_Click", o,
                 "точек было " + before + ", стало " + after);
            Control("строка 310 ИСПОЛНИЛАСЬ (точка снята)", after == before - 1,
                    "точек " + before + " -> " + after);

            // --- строки 396/413: EnergySpectrumView_PeakPickuped --------------
            s.Update();
            before = s.Peaks.Count;
            s.Set("peakPickupProcessing", true);
            o = s.Invoke("EnergySpectrumView_PeakPickuped",
                         new object[] { s.Doc.EnergySpectrumView, Pick(510, 9.0) });
            after = s.Peaks == null ? -1 : s.Peaks.Count;
            Door("396/413  EnergySpectrumView_PeakPickuped", o,
                 "точек было " + before + ", стало " + after);
            Control("строки 396 и 413 ИСПОЛНИЛИСЬ (точка добавлена)", after == before + 1,
                    "точек " + before + " -> " + after);

            // --- строка 838: CollectedPeaksTable_EditingStopped ---------------
            s.Update();
            List<CalibrationPeak> sorted = s.Peaks;
            double fwhmBefore = sorted.Count > 0 ? sorted[0].FWHM : double.NaN;
            o = s.Invoke("CollectedPeaksTable_EditingStopped",
                         new object[] { s.Table, s.EditArgs(0, 3, "17") });
            double fwhmAfter = s.Peaks.Count > 0 ? s.Peaks[0].FWHM : double.NaN;
            Door("838  CollectedPeaksTable_EditingStopped", o,
                 "ПШПВ первой точки " + F(fwhmBefore) + " -> " + F(fwhmAfter));
            Control("строка 838 ИСПОЛНИЛАСЬ (ПШПВ переписана)",
                    Math.Abs(fwhmAfter - 17.0) < 1e-9, F(fwhmBefore) + " -> " + F(fwhmAfter));

            // --- строка 923: GetAllPeaksButton_Click --------------------------
            s.Update();
            // Второй документ с одной ЧУЖОЙ точкой: сбор по всем документам
            // обязан её притянуть, и это доказывает, что 923 (Clear) и 934
            // (Add) исполнились, а не «ничего не менялось».
            Stand other = Stand.Make("o13-live-2", live: true);
            if (other != null)
            {
                other.Peaks.Clear();
                other.Peaks.Add(new CalibrationPeak { Channel = 777, Energy = 1234.0, FWHM = 5.0 });
            }
            before = s.Peaks.Count;
            o = s.Invoke("GetAllPeaksButton_Click", new object[] { null, EventArgs.Empty });
            after = s.Peaks.Count;
            bool got777 = s.Peaks.Any(p => p.Channel == 777);
            Door("923  GetAllPeaksButton_Click", o,
                 "точек было " + before + ", стало " + after + ", чужая 777 притянута: " + got777);
            Control("строка 923 ИСПОЛНИЛАСЬ (список очищен и пересобран)", got777,
                    "чужой точки 777 в списке нет — сбор не работал");

            // --- строки 950/952: ViewCalibrationButton_Click ------------------
            // ⛔ Сам обработчик НЕ дёргается: на 951 стоит ShowDialog, и
            //    безоконный прогон повис бы на модальном окне. Мерятся строки
            //    949-950 ДОСЛОВНО — конструктор окна и Init, — а 952 стоит ЗА
            //    ответом окна и достижим лишь тогда, когда 950 прошла.
            o = s.GraphInit();
            Door("950  FWHMCalibrationGraph.Init (952 стоит ЗА ShowDialog)", o, "-");
            Control("строка 950 ИСПОЛНИЛАСЬ (окно кривой построено)", o.Ok, o.Text);

            // На ЖИВОЙ кривой настоящий обработчик обязан дойти до ShowDialog и
            // повиснуть: это и есть доказательство, что дверь зовётся по-настоящему.
            o = s.ViewCalibrationDoor(4000);
            Door("952  ViewCalibrationButton_Click (НАСТОЯЩИЙ обработчик)", o, "-");
            Control("живая кривая ДОШЛА до модального окна — дверь настоящая",
                    !o.Ok && o.Text.StartsWith("ПОВИСЛА"),
                    "ожидалось зависание на ShowDialog, получено: " + o.Text);

            Console.WriteLine();
        }

        // ==================================================================
        // ЗАМЕР. Сцена «ПУСТАЯ» — кривой нет и умолчание не строится.
        // ==================================================================
        static void SceneNull()
        {
            Console.WriteLine("=== СЦЕНА «ПУСТАЯ» — ЗАМЕР ДОСТИЖИМОСТИ ===");
            Console.WriteLine("  кривой нет, и умолчание НЕ СТРОИТСЯ: прямая конфигурации не растёт");
            Console.WriteLine();

            Stand s = Stand.Make("o13-null", live: false);
            if (s == null) { Fail("стенд ПУСТАЯ не построился"); return; }

            s.Update();
            s.PrintState();
            Control("после обновления вида кривая ВСЁ ЕЩЁ пуста",
                    s.Doc.ActiveResultData.FwhmCalibration == null,
                    "EnsureFwhmCalibration её восстановил — сцена не та, что мерилась");

            Outcome o = s.Invoke("RemovePeakButton_Click", new object[] { null, EventArgs.Empty });
            Door("310  RemovePeakButton_Click", o, "-");

            s.Set("peakPickupProcessing", true);
            o = s.Invoke("EnergySpectrumView_PeakPickuped",
                         new object[] { s.Doc.EnergySpectrumView, Pick(510, 9.0) });
            Door("396/413  EnergySpectrumView_PeakPickuped", o, "-");

            o = s.Invoke("CollectedPeaksTable_EditingStopped",
                         new object[] { s.Table, s.EditArgs(-1, 3, "17") });
            Door("838  CollectedPeaksTable_EditingStopped (строка СОЧИНЕНА: в таблице их нет)", o,
                 "строк в таблице: " + s.Rows);

            o = s.Invoke("GetAllPeaksButton_Click", new object[] { null, EventArgs.Empty });
            Door("923  GetAllPeaksButton_Click", o, "-");
            if (control)
            {
                Control("КОНТРОЛЬНОЕ ПЛЕЧО: снятый сторож дал ПАДЕНИЕ на 923",
                        !o.Ok && o.Text.Contains("DCFwhmCalibrationView.cs"),
                        "проба СЛЕПА: сторож снят, а падения нет — " + o.Text);
            }
            else
            {
                Control("РАБОЧЕЕ ПЛЕЧО: сторож 894-899 держит — падения нет", o.Ok,
                        "дверь упала при живом стороже: " + o.Text);
            }

            o = s.GraphInit();
            Door("950  FWHMCalibrationGraph.Init (952 стоит ЗА ShowDialog)", o, "-");

            // ⛔ Главный замер по 952: на ПУСТОЙ кривой настоящий обработчик обязан
            //    вернуться сторожем. «Повисла» здесь значит, что сторожа нет.
            o = s.ViewCalibrationDoor(4000);
            Door("952  ViewCalibrationButton_Click (НАСТОЯЩИЙ обработчик)", o, "-");
            Control("пустая кривая ОСТАНОВЛЕНА сторожем, до окна не дошло", o.Ok,
                    "дверь не остановлена: " + o.Text);

            Console.WriteLine();
        }

        // ==================================================================
        // ЗАМЕР. Сцена «ВВОЗ-N42» — переживает ли пустота обновление вида.
        // ==================================================================
        static void SceneN42()
        {
            Console.WriteLine("=== СЦЕНА «ВВОЗ-N42» — ПОСЫЛКА СТРОКИ `A236` ===");
            if (n42Dir == null)
            {
                Console.WriteLine("  пропущена: не задан --n42=<каталог>");
                Console.WriteLine();
                return;
            }
            string[] files = Directory.GetFiles(n42Dir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);
            Console.WriteLine("  каталог: " + Path.GetFullPath(n42Dir) + ", файлов " + files.Length);
            Console.WriteLine();

            // ⛔ ПЛЕЧО ОБЯЗАНО БЫТЬ ПУСТЫМ. Прежняя редакция ввозила поверх ЖИВОЙ
            //    кривой (`live: true`) и печатала «кривая есть» у 12 из 12 — а это
            //    лишь «дверь не стирает существующую», про пустую не говорит ничего.
            //    Дверь `ImportDocumentN42` кривую разрешения не трогает (`A234`),
            //    поэтому мерить надо ровно там, где до ввоза её НЕ БЫЛО.
            int nullAtImport = 0, nullAfterUpdate = 0, ok = 0;
            foreach (string f in files)
            {
                Stand s = Stand.Make("o13-n42-" + Path.GetFileNameWithoutExtension(f), live: false);
                if (s == null) continue;
                try
                {
                    DocumentManager.GetInstance().ImportDocumentN42(s.Doc, f);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  " + Path.GetFileName(f) + ": ввоз ОТКАЗАЛ — " + Where(ex));
                    continue;
                }
                ok++;
                bool nullNow = s.Doc.ActiveResultData.FwhmCalibration == null;
                if (nullNow) nullAtImport++;
                s.Update();
                bool nullAfter = s.Doc.ActiveResultData.FwhmCalibration == null;
                if (nullAfter) nullAfterUpdate++;
                Console.WriteLine("  " + Path.GetFileName(f).PadRight(28)
                                  + " кривая после ВВОЗА: " + (nullNow ? "ПУСТА" : "есть")
                                  + " | после ОБНОВЛЕНИЯ ВИДА: " + (nullAfter ? "ПУСТА" : "есть")
                                  + " | строк в таблице: " + s.Rows);
            }
            Console.WriteLine();
            Console.WriteLine("  ввезено " + ok + ", пустых после ввоза " + nullAtImport
                              + ", пустых ПОСЛЕ ОБНОВЛЕНИЯ ВИДА " + nullAfterUpdate);
            Console.WriteLine();
        }

        // ==================================================================
        // СТЕНД
        // ==================================================================
        sealed class Stand
        {
            public DocEnergySpectrum Doc;
            public MainForm Form;
            public DCFwhmCalibrationView View;

            public static Stand Make(string name, bool live)
            {
                DocEnergySpectrum doc = DocumentManager.GetInstance().CreateDocument(name + ".xml");
                if (doc == null) return null;

                ResultData rd = doc.ActiveResultData;
                // Спектр с настоящим пиком: `CalcPeakFitValues` на 405 считает
                // по нему, и на пустой гистограмме мерить нечего.
                EnergySpectrum es = rd.EnergySpectrum;
                for (int i = 0; i < es.NumberOfChannels; i++)
                {
                    double d = (i - 500.0) / 8.0;
                    es.Spectrum[i] = 20 + (int)(4000.0 * Math.Exp(-0.5 * d * d));
                }
                es.MeasurementTime = 300.0;
                es.TotalPulseCount = es.Spectrum.Sum(x => (long)x);

                FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                if (live)
                {
                    if (rd.FwhmCalibration == null && cfg != null)
                    {
                        rd.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, es.EnergyCalibration);
                    }
                    if (rd.FwhmCalibration == null) return null;
                    rd.FwhmCalibration.CalibrationPeaks.Clear();
                    rd.FwhmCalibration.CalibrationPeaks.Add(new CalibrationPeak { Channel = 100, Energy = 200.0, FWHM = 7.0 });
                    rd.FwhmCalibration.CalibrationPeaks.Add(new CalibrationPeak { Channel = 300, Energy = 600.0, FWHM = 11.0 });
                    rd.FwhmCalibration.CalibrationPeaks.Add(new CalibrationPeak { Channel = 800, Energy = 1600.0, FWHM = 19.0 });
                }
                else
                {
                    // ⛔ Состояние `ResultData.cs:470`: умолчание НЕ СТРОИТСЯ,
                    //    потому что прямая через (0, FWHM_AT_0) и (Ch_Fwhm,
                    //    Width_Fwhm) не растёт. Правка живёт ТОЛЬКО в памяти
                    //    этого прогона: конфигурация на диск не пишется.
                    if (cfg == null) return null;
                    cfg.FWHM_AT_0 = 40.0;
                    cfg.Width_Fwhm = 1.0;
                    cfg.FwhmCalibration = null;
                    rd.FwhmCalibration = null;
                }

                MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
                SetField(form, "activeDocument", doc);
                SetField(form, "documentManager", DocumentManager.GetInstance());

                DCFwhmCalibrationView view = new DCFwhmCalibrationView(form);

                return new Stand { Doc = doc, Form = form, View = view };
            }

            public List<CalibrationPeak> Peaks
            {
                get
                {
                    FwhmCalibration c = Doc.ActiveResultData.FwhmCalibration;
                    return c == null ? null : c.CalibrationPeaks;
                }
            }

            public Table Table { get { return (Table)GetField(View, "CollectedPeaksTable"); } }
            public TableModel Model { get { return (TableModel)GetField(View, "tableModel1"); } }
            public int Rows { get { return Model.Rows.Count; } }

            public void Update()
            {
                Invoke("UpdateFwhmCalibration", new object[] { false });
            }

            public void Set(string field, object value) { SetField(View, field, value); }

            public CellEditEventArgs EditArgs(int rowIndex, int column, string text)
            {
                Row row;
                if (rowIndex >= 0 && rowIndex < Model.Rows.Count)
                {
                    row = Model.Rows[rowIndex];
                }
                else
                {
                    row = new Row();
                    row.Cells.Add(new Cell("1"));
                    row.Cells.Add(new Cell(100));
                    row.Cells.Add(new Cell(200.0));
                    row.Cells.Add(new Cell(7.0));
                }
                NumberCellEditor editor = new NumberCellEditor();
                editor.TextBox.Text = text;
                return new CellEditEventArgs(row.Cells[column], editor, Table,
                                             row.Index, column, Rectangle.Empty);
            }

            /// <summary>
            /// ДОСЛОВНО строки 949-950 обработчика `ViewCalibrationButton_Click`.
            /// Дальше в приложении стоит `ShowDialog`, и безоконная проба на нём
            /// повисла бы; строка 952 живёт ЗА ответом окна.
            /// </summary>
            public Outcome GraphInit()
            {
                try
                {
                    FwhmCalibration field = (FwhmCalibration)GetField(View, "fwhmCalibration");
                    FWHMCalibrationGraph graph = new FWHMCalibrationGraph(Form);
                    graph.Init(field, Doc.ActiveResultData.EnergySpectrum.NumberOfChannels);
                    return new Outcome(true, "прошла");
                }
                catch (Exception ex)
                {
                    return new Outcome(false, Where(ex));
                }
            }

            /// <summary>
            /// НАСТОЯЩИЙ обработчик `ViewCalibrationButton_Click` — в своём потоке
            /// STA с таймаутом. ⛔ `GraphInit` воспроизводит строки 949-950
            /// ДОСЛОВНО и потому СЛЕПА к стражу, поставленному в самом
            /// обработчике: она мерит копию кода, а не код. Здесь зовётся живой
            /// метод; если сторож сработал, он вернётся, не дойдя до `ShowDialog`.
            /// Если сторожа нет — поток упрётся в модальное окно, и это ровно то,
            /// что надо назвать (потому поток фоновый: он не держит процесс).
            /// </summary>
            public Outcome ViewCalibrationDoor(int timeoutMs)
            {
                Outcome result = null;
                System.Threading.Thread t = new System.Threading.Thread(delegate ()
                {
                    result = Invoke("ViewCalibrationButton_Click", new object[] { null, EventArgs.Empty });
                });
                t.IsBackground = true;
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.Start();
                if (!t.Join(timeoutMs))
                {
                    return new Outcome(false, "ПОВИСЛА на модальном окне (ShowDialog) — сторожа нет");
                }
                return result ?? new Outcome(false, "поток кончился без исхода");
            }

            public Outcome Invoke(string method, object[] args)
            {
                MethodInfo mi = typeof(DCFwhmCalibrationView).GetMethod(method,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi == null) return new Outcome(false, "метода " + method + " НЕТ в сборке");
                try
                {
                    mi.Invoke(View, args);
                    return new Outcome(true, "прошла");
                }
                catch (Exception ex)
                {
                    return new Outcome(false, Where(ex));
                }
            }

            public void PrintState()
            {
                FwhmCalibration doc = Doc.ActiveResultData.FwhmCalibration;
                FwhmCalibration fld = (FwhmCalibration)GetField(View, "fwhmCalibration");
                Console.WriteLine("  ActiveResultData.FwhmCalibration: " + (doc == null ? "ПУСТА" : "есть"));
                Console.WriteLine("  поле вида fwhmCalibration:        " + (fld == null ? "ПУСТО" : "есть"));
                Console.WriteLine("  строк в таблице:                  " + Rows);
                Console.WriteLine("  панель calibrationProcessingPanel Visible: " + Vis("calibrationProcessingPanel"));
                Console.WriteLine("  кнопки (Enabled/Visible):");
                foreach (string b in new[] { "addPeakButton", "removePeakButton", "cancelAddPeakButton",
                                             "getAllPeaksButton", "executeCalibrationButton",
                                             "saveToDeviceCfgButton", "viewCalibrationButton", "CollectedPeaksTable" })
                {
                    Control c = GetField(View, b) as Control;
                    Console.WriteLine("      " + b.PadRight(26) + (c == null ? "нет поля"
                        : (c.Enabled ? "Enabled" : "выкл   ") + " / " + (c.Visible ? "Visible" : "скрыт")));
                }
                Console.WriteLine();
            }

            bool Vis(string field)
            {
                Control c = GetField(View, field) as Control;
                return c != null && c.Visible;
            }
        }

        sealed class Outcome
        {
            public readonly bool Ok;
            public readonly string Text;
            public Outcome(bool ok, string text) { Ok = ok; Text = text; }
        }

        static PeakPickupedEventArgs Pick(int channel, double fwhm)
        {
            return new PeakPickupedEventArgs(channel, channel * 2.0, fwhm, channel - 30, channel + 30);
        }

        // ==================================================================
        // Печать и отражение
        // ==================================================================
        static void Door(string name, Outcome o, string effect)
        {
            Console.WriteLine("  ДВЕРЬ " + name);
            Console.WriteLine("      исход: " + (o.Ok ? "БЕЗ ОТКАЗА" : "ОТКАЗ  " + o.Text));
            if (effect != "-") Console.WriteLine("      след:  " + effect);
        }

        static void Control(string what, bool ok, string detail)
        {
            Console.WriteLine("      [КОНТРОЛЬ] " + (ok ? "СОШЁЛСЯ  " : "НЕ СОШЁЛСЯ") + "  " + what);
            if (!ok)
            {
                Console.WriteLine("                 " + detail);
                badControl++;
            }
        }

        static void Fail(string what)
        {
            Console.WriteLine("  [КОНТРОЛЬ] НЕ СОШЁЛСЯ  " + what);
            badControl++;
        }

        static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Самое ГЛУБОКОЕ исключение и первый кадр стека, у которого есть
        /// имя файла: без этого «упало» не называет строки, а вся строка
        /// `A236` — про строки.
        /// </summary>
        static string Where(Exception ex)
        {
            Exception e = ex;
            while ((e is TargetInvocationException || e is TypeInitializationException)
                   && e.InnerException != null)
            {
                e = e.InnerException;
            }
            string place = "<без строк>";
            StackTrace st = new StackTrace(e, true);
            for (int i = 0; i < st.FrameCount; i++)
            {
                StackFrame f = st.GetFrame(i);
                string file = f.GetFileName();
                if (!string.IsNullOrEmpty(file))
                {
                    place = Path.GetFileName(file) + ":" + f.GetFileLineNumber()
                            + " (" + f.GetMethod().Name + ")";
                    break;
                }
            }
            return e.GetType().Name + " @ " + place;
        }

        static object GetField(object target, string name)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) return fi.GetValue(target);
                t = t.BaseType;
            }
            return null;
        }

        static void SetField(object target, string name, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) { fi.SetValue(target, value); return; }
                t = t.BaseType;
            }
            throw new InvalidOperationException("поля " + name + " нет: " + target.GetType().Name);
        }
    }
}
