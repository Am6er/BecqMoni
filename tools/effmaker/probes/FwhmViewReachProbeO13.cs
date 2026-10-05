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
    ///   ИМПОРТ-N42     — настоящий импорт корпусного `.n42` (`--n42=`): дверь
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
            SceneGraphInitA243();

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
            // ⛔ С 05.10.2026 (`AMBER201`, мелочь 4.10) «Удалить» БЕЗ выделенной
            //    строки не снимает ничего — прежде снималась первая опора молча.
            //    Поэтому дверь дёргается дважды: без выделения (обязано остаться
            //    столько же) и с выделенной ВТОРОЙ строкой (обязана уйти именно она).
            int before = s.Peaks.Count;
            Outcome o = s.Invoke("RemovePeakButton_Click", new object[] { null, EventArgs.Empty });
            int after = s.Peaks == null ? -1 : s.Peaks.Count;
            Door("310  RemovePeakButton_Click (без выделения)", o,
                 "точек было " + before + ", стало " + after);
            Control("без выделения НИЧЕГО не снято (4.10)", o.Ok && after == before,
                    "точек " + before + " -> " + after + " | " + o.Text);

            s.Update();
            before = s.Peaks.Count;
            int victim = before > 1 ? s.Peaks[1].Channel : -1;
            s.Model.Selections.SelectCell(1, 0);
            o = s.Invoke("RemovePeakButton_Click", new object[] { null, EventArgs.Empty });
            after = s.Peaks == null ? -1 : s.Peaks.Count;
            bool victimGone = s.Peaks != null && !s.Peaks.Any(p => p.Channel == victim);
            Door("310  RemovePeakButton_Click (выделена строка 2)", o,
                 "точек было " + before + ", стало " + after + ", канал " + victim + " снят: " + victimGone);
            Control("строка 310 ИСПОЛНИЛАСЬ (снята ВЫДЕЛЕННАЯ точка)",
                    after == before - 1 && victimGone,
                    "точек " + before + " -> " + after + ", канал " + victim + " снят: " + victimGone);

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
        // ЗАМЕР. Сцена «ИМПОРТ-N42» — переживает ли пустота обновление вида.
        // ==================================================================
        static void SceneN42()
        {
            Console.WriteLine("=== СЦЕНА «ИМПОРТ-N42» — ПОСЫЛКА СТРОКИ `A236` ===");
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

            // ⛔ ПЛЕЧО ОБЯЗАНО БЫТЬ ПУСТЫМ. Прежняя редакция импортировала поверх ЖИВОЙ
            //    кривой (`live: true`) и печатала «кривая есть» у 12 из 12 — а это
            //    лишь «дверь не стирает существующую», про пустую не говорит ничего.
            //    Дверь `ImportDocumentN42` кривую разрешения не трогает (`A234`),
            //    поэтому мерить надо ровно там, где до импорта её НЕ БЫЛО.
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
                    Console.WriteLine("  " + Path.GetFileName(f) + ": импорт ОТКАЗАЛ — " + Where(ex));
                    continue;
                }
                ok++;
                bool nullNow = s.Doc.ActiveResultData.FwhmCalibration == null;
                if (nullNow) nullAtImport++;
                s.Update();
                bool nullAfter = s.Doc.ActiveResultData.FwhmCalibration == null;
                if (nullAfter) nullAfterUpdate++;
                Console.WriteLine("  " + Path.GetFileName(f).PadRight(28)
                                  + " кривая после ИМПОРТА: " + (nullNow ? "ПУСТА" : "есть")
                                  + " | после ОБНОВЛЕНИЯ ВИДА: " + (nullAfter ? "ПУСТА" : "есть")
                                  + " | строк в таблице: " + s.Rows);
            }
            Console.WriteLine();
            Console.WriteLine("  импортировано " + ok + ", пустых после импорта " + nullAtImport
                              + ", пустых ПОСЛЕ ОБНОВЛЕНИЯ ВИДА " + nullAfterUpdate);
            Console.WriteLine();
        }

        // ==================================================================
        // ЗАМЕР `A243`. Сторож `null` в `FWHMCalibrationGraph.Init`.
        //
        // ⛔ ЭТО ЗАМЕР ДОСТИЖИМОСТИ, А НЕ ПОЧИНКА. Строка `A243` говорит, что
        // `Init` разыменовывает кривую голым. Разыменований там ДВА РОДА:
        //   род I  — АРГУМЕНТ `fwhmCalibration.Clone()`
        //            (IL: `ldarg.1; callvirt FwhmCalibration.Clone`);
        //   род II — цепочка `mainForm.ActiveDocument.ActiveResultData
        //            .EnergySpectrum.EnergyCalibration.Clone()`
        //            (IL: `callvirt EnergySpectrum.get_EnergyCalibration;
        //            callvirt EnergyCalibration.Clone`).
        // ⛔ S183: номеров строк здесь нет нарочно — мерка на `:56`/`:53`
        //   протухла от сдвига файла на строку и краснела на любом входе.
        // Зовущий один — `DCFwhmCalibrationView.ViewCalibrationButton_Click`,
        // и над вызовом стоит сторож `A236` (976-984).
        //
        // ⛔ ОТРИЦАТЕЛЬНЫЙ ВЫВОД БЕЗ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ НИЧЕГО НЕ ЗНАЧИТ:
        //   ПЛЕЧО 1 — проба ДОСТАЁТ до `Init`: на живой кривой метод обязан
        //     оставить след ВНУТРИ объекта графика (поля `fwhmCalibration`,
        //     `points`, `originalpoints`, `maxChannels`, `maxFWHM`), а не
        //     «объект создался».
        //   ПЛЕЧО 2 — проба ЛОВИТ настоящее падение: `Init` зовётся напрямую
        //     с `null`, и проба обязана назвать ИМЕННО инструкцию рода I
        //     (`IsRodI`). Второе падение — рода II, с `EnergyCalibration =
        //     null` (`IsRodII`). Место — по IL тела метода (`SiteOf`).
        // ==================================================================
        static void SceneGraphInitA243()
        {
            Console.WriteLine("=== СЦЕНА «ГРАФИК» — ЗАМЕР `A243` (FWHMCalibrationGraph.Init) ===");
            Console.WriteLine();

            // --- ПЛЕЧО 1: проба достаёт до Init ------------------------------
            Stand live = Stand.Make("o22-graph-live", live: true);
            if (live == null) { Fail("стенд ЖИВАЯ (A243) не построился"); return; }
            live.Update();

            GraphOutcome g = live.InitRaw(live.Doc.ActiveResultData.FwhmCalibration);
            Console.WriteLine("  ПЛЕЧО 1. Init(живая кривая, " + live.Channels + ")");
            Console.WriteLine("      исход: " + (g.O.Ok ? "БЕЗ ОТКАЗА" : "ОТКАЗ  " + g.O.Text));
            Console.WriteLine("      след ВНУТРИ графика: " + g.Trace);
            Control("ПЛЕЧО 1: `Init` ИСПОЛНИЛСЯ — поля графика заполнены",
                    g.O.Ok && g.Points == 3 && g.OriginalPoints == 3
                    && g.MaxChannels == live.Channels && g.MaxFwhm > 0.0,
                    "следа нет — проба до `Init` НЕ ДОЗВОНИЛАСЬ: " + g.Trace);

            // ⛔ S183: место падения судится по ИНСТРУКЦИИ тела `Init`, а не по
            //    номеру строки (см. `SiteOf`). Метод берётся по сигнатуре: если
            //    его переименуют или сменят параметры — отказ, а не молчание.
            MethodBase init = typeof(FWHMCalibrationGraph).GetMethod("Init",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(FwhmCalibration), typeof(int) }, null);
            if (init == null) { Fail("метода FWHMCalibrationGraph.Init(FwhmCalibration, int) НЕТ в сборке"); return; }

            // --- ПЛЕЧО 2а: настоящее падение рода I (аргумент null) ----------
            GraphOutcome n = live.InitRaw(null);
            IlSite sn = SiteOf(n.O.Ex, init);
            Console.WriteLine();
            Console.WriteLine("  ПЛЕЧО 2а. Init(null, " + live.Channels + ") — род I, аргумент");
            Console.WriteLine("      исход: " + (n.O.Ok ? "БЕЗ ОТКАЗА" : "ОТКАЗ  " + n.O.Text));
            Console.WriteLine("      место: " + sn.Text);
            Control("ПЛЕЧО 2а: проба ЛОВИТ падение на `callvirt FwhmCalibration.Clone` над АРГУМЕНТОМ (ldarg.1)",
                    IsRodI(n.O, sn),
                    "проба СЛЕПА: голое разыменование аргумента не дало названного падения — "
                    + n.O.Text + " | " + sn.Text);

            // --- ПЛЕЧО 2б: настоящее падение рода II (EnergyCalibration null) -
            Stand noEcal = Stand.Make("o22-graph-noecal", live: true);
            if (noEcal == null) { Fail("стенд БЕЗ ЭНЕРГОКАЛИБРОВКИ не построился"); return; }
            noEcal.Update();
            noEcal.KillEnergyCalibration();
            GraphOutcome e = noEcal.InitRaw(noEcal.Doc.ActiveResultData.FwhmCalibration);
            IlSite se = SiteOf(e.O.Ex, init);
            Console.WriteLine();
            Console.WriteLine("  ПЛЕЧО 2б. Init(живая кривая) при EnergyCalibration == null — род II");
            Console.WriteLine("      исход: " + (e.O.Ok ? "БЕЗ ОТКАЗА" : "ОТКАЗ  " + e.O.Text));
            Console.WriteLine("      место: " + se.Text);
            Control("ПЛЕЧО 2б: проба ЛОВИТ падение на `callvirt EnergyCalibration.Clone` над get_EnergyCalibration",
                    IsRodII(e.O, se),
                    "род II не дал названного падения — " + e.O.Text + " | " + se.Text);

            // --- ЗАМЕР: доходит ли НАСТОЯЩАЯ дверь до `Init` ------------------
            // Пустая кривая: сторож `A236` (978-984) обязан вернуть управление.
            // «Повисла» здесь значила бы, что дверь дошла до `ShowDialog`, то
            // есть `Init` уже отработал.
            Console.WriteLine();
            Console.WriteLine("  ЗАМЕР. Настоящая дверь `ViewCalibrationButton_Click`:");
            Stand empty = Stand.Make("o22-graph-null", live: false);
            if (empty == null) { Fail("стенд ПУСТАЯ (A243) не построился"); return; }
            empty.Update();
            Console.WriteLine("      сцена ПУСТАЯ: кривая документа "
                              + (empty.Doc.ActiveResultData.FwhmCalibration == null ? "ПУСТА" : "есть")
                              + ", поле вида "
                              + (GetField(empty.View, "fwhmCalibration") == null ? "ПУСТО" : "есть")
                              + ", кнопка viewCalibrationButton "
                              + (((Control)GetField(empty.View, "viewCalibrationButton")).Enabled ? "Enabled" : "выкл"));
            Outcome d = empty.ViewCalibrationDoor(4000);
            Console.WriteLine("      исход: " + (d.Ok ? "БЕЗ ОТКАЗА (сторож вернул)" : "ОТКАЗ  " + d.Text));
            Control("РОД I НЕДОСТИЖИМ: пустая кривая остановлена сторожем 978-984, до `Init` не дошло",
                    d.Ok, "дверь не остановлена: " + d.Text);

            // Кривая ЕСТЬ, а энергокалибровки НЕТ: сторож 978-984 это звено НЕ
            // проверяет, и дверь обязана дойти до `Init` и упасть на 53.
            Stand door2 = Stand.Make("o22-graph-door-noecal", live: true);
            if (door2 == null) { Fail("стенд ДВЕРЬ БЕЗ ЭНЕРГОКАЛИБРОВКИ не построился"); return; }
            door2.Update();
            door2.KillEnergyCalibration();
            Outcome d2 = door2.ViewCalibrationDoor(4000);
            Console.WriteLine();
            Console.WriteLine("      сцена «кривая есть, EnergyCalibration == null»");
            Console.WriteLine("      исход: " + (d2.Ok ? "БЕЗ ОТКАЗА" : "ОТКАЗ  " + d2.Text));
            Console.WriteLine("      [ЗАМЕР] род II через настоящую дверь: "
                              + (IsRodII(d2, SiteOf(d2.Ex, init))
                                 ? "ДОСТИЖИМ" : "не воспроизвёлся"));
            // ⛔ Читатель признака (`A243`): до правки эта сцена давала
            //    `NullReferenceException @ FWHMCalibrationGraph.cs:53`
            //    (`handover/o22-fwhmgraph/o22-1-probe-before.txt`). Сторож
            //    978-985 добавил четвёртое звено — падение обязано уйти.
            //    ⚠ Что уход НЕ от слепоты пробы, держит ПЛЕЧО 2б выше: прямой
            //    вызов `Init` на той же сцене падает по-прежнему.
            Control("РОД II ЗАКРЫТ: пустая энергокалибровка остановлена сторожем 978-985",
                    d2.Ok, "дверь всё ещё валит чужой класс: " + d2.Text);

            Console.WriteLine();
        }

        /// <summary>Род I (`A243`): пустой АРГУМЕНТ — `ldarg.1; callvirt FwhmCalibration.Clone`.</summary>
        static bool IsRodI(Outcome o, IlSite s)
        {
            return !o.Ok && Innermost(o.Ex) is NullReferenceException
                   && s.InMethod && s.OnBoundary && s.Op == "callvirt"
                   && s.Target == "FwhmCalibration.Clone" && s.Prev == "ldarg.1";
        }

        /// <summary>Род II (`A243`): пустая энергокалибровка спектра —
        /// `callvirt EnergySpectrum.get_EnergyCalibration; callvirt EnergyCalibration.Clone`.</summary>
        static bool IsRodII(Outcome o, IlSite s)
        {
            return !o.Ok && Innermost(o.Ex) is NullReferenceException
                   && s.InMethod && s.OnBoundary && s.Op == "callvirt"
                   && s.Target == "EnergyCalibration.Clone"
                   && s.Prev == "callvirt EnergySpectrum.get_EnergyCalibration";
        }

        static Exception Innermost(Exception ex)
        {
            Exception e = ex;
            while ((e is TargetInvocationException || e is TypeInitializationException)
                   && e.InnerException != null)
            {
                e = e.InnerException;
            }
            return e;
        }

        sealed class GraphOutcome
        {
            public Outcome O;
            public string Trace;
            public int Points = -1, OriginalPoints = -1, MaxChannels = -1;
            public double MaxFwhm = double.NaN;
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

            public int Channels { get { return Doc.ActiveResultData.EnergySpectrum.NumberOfChannels; } }

            /// <summary>
            /// ⛔ Замер `A243`. `Init` зовётся НАПРЯМУЮ, с любым аргументом, и
            /// после него читаются ПОЛЯ САМОГО ГРАФИКА: без следа внутри
            /// объекта «отказа не было» неотличимо от «проба не дозвонилась».
            /// </summary>
            public GraphOutcome InitRaw(FwhmCalibration cal)
            {
                GraphOutcome r = new GraphOutcome();
                FWHMCalibrationGraph graph;
                try
                {
                    graph = new FWHMCalibrationGraph(Form);
                }
                catch (Exception ex)
                {
                    r.O = new Outcome(false, "конструктор графика: " + Where(ex));
                    r.Trace = "график не построился";
                    return r;
                }
                try
                {
                    graph.Init(cal, Channels);
                    r.O = new Outcome(true, "прошла");
                }
                catch (Exception ex)
                {
                    r.O = new Outcome(ex);
                }
                List<CalibrationPeak> pts = GetField(graph, "points") as List<CalibrationPeak>;
                List<CalibrationPeak> orig = GetField(graph, "originalpoints") as List<CalibrationPeak>;
                object mc = GetField(graph, "maxChannels");
                object mf = GetField(graph, "maxFWHM");
                object fc = GetField(graph, "fwhmCalibration");
                r.Points = pts == null ? -1 : pts.Count;
                r.OriginalPoints = orig == null ? -1 : orig.Count;
                r.MaxChannels = mc == null ? -1 : (int)mc;
                r.MaxFwhm = mf == null ? double.NaN : (double)mf;
                r.Trace = "fwhmCalibration " + (fc == null ? "ПУСТО" : "есть")
                          + ", points " + r.Points
                          + ", originalpoints " + r.OriginalPoints
                          + ", maxChannels " + r.MaxChannels
                          + ", maxFWHM " + F(r.MaxFwhm);
                return r;
            }

            /// <summary>
            /// Снимает энергокалибровку спектра — звено, которого сторож
            /// `A236` (978-984) НЕ проверяет, а `Init` разыменовывает (:53).
            /// </summary>
            public void KillEnergyCalibration()
            {
                Doc.ActiveResultData.EnergySpectrum.EnergyCalibration = null;
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
                    return new Outcome(ex);
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
                    return new Outcome(ex);
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
            public readonly Exception Ex;   // S183: признак места берётся из исключения, не из текста
            public Outcome(bool ok, string text) { Ok = ok; Text = text; }
            public Outcome(Exception ex) { Ok = false; Text = Where(ex); Ex = ex; }
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

        // ==================================================================
        // ⛔ S183 (24.09.2026, П154). Место падения — НЕ номер строки.
        //
        // Прежде мерка требовала `FWHMCalibrationGraph.cs:56` и `:53`, файл
        // сдвинулся на строку, и проба две недели краснела ОДИНАКОВО на любом
        // входе — отказ ни о чём не говорил. Номер строки едет от любой правки
        // выше по файлу, а судить надо о ВЕЩИ: какое разыменование упало.
        //
        // Признак здесь — кадр стека именно ЭТОГО метода (сравнение по
        // метаданным, не по имени-строке), смещение IL в нём и инструкция
        // тела метода на этом смещении: опкод, его цель и инструкция перед
        // ней (кто положил на стек пустой получатель). Строк файла признак
        // не касается вовсе; номер строки печатается только для глаз.
        // ==================================================================
        sealed class IlSite
        {
            public bool InMethod;          // кадр метода найден в стеке
            public int Offset = -1;        // смещение IL из кадра (граница карты)
            public bool OnBoundary;        // смещение попало на начало инструкции
            public string Boundary = "";   // инструкция на границе
            public int At = -1;            // смещение падающей инструкции
            public string Op = "", Target = "", Prev = "";
            public string Text { get { return !InMethod ? "кадра метода в стеке НЕТ"
                : !OnBoundary ? "IL_" + H(Offset) + " НЕ на границе инструкции"
                : "граница IL_" + H(Offset) + " " + Boundary + " -> падает IL_" + H(At)
                  + " " + Op + (Target.Length > 0 ? " " + Target : "") + "  <- " + Prev; } }
            static string H(int v) { return v.ToString("X4", CultureInfo.InvariantCulture); }
        }

        /// <summary>Опкоды, которые разыменовывают получатель и падают на пустом.</summary>
        static bool Derefs(string op)
        {
            return op == "callvirt" || op == "ldfld" || op == "ldflda" || op == "stfld"
                   || op == "ldvirtftn" || op == "ldlen" || op.StartsWith("ldelem") || op.StartsWith("stelem")
                   || op.StartsWith("ldind") || op.StartsWith("stind");
        }

        sealed class IlInstr
        {
            public int Offset; public string Op; public string Target;
            public override string ToString() { return Op + (Target.Length > 0 ? " " + Target : ""); }
        }

        static Dictionary<short, System.Reflection.Emit.OpCode> opTable;

        /// <summary>
        /// Разбор тела метода в список инструкций (опкод и цель вызова или
        /// поля). Тот же обход, что `N42RoundTripProbe --mode=il`.
        /// </summary>
        static List<IlInstr> Decode(MethodBase m)
        {
            if (opTable == null)
            {
                opTable = new Dictionary<short, System.Reflection.Emit.OpCode>();
                foreach (FieldInfo f in typeof(System.Reflection.Emit.OpCodes)
                         .GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (f.FieldType != typeof(System.Reflection.Emit.OpCode)) continue;
                    System.Reflection.Emit.OpCode op = (System.Reflection.Emit.OpCode)f.GetValue(null);
                    opTable[op.Value] = op;
                }
            }
            byte[] il = m.GetMethodBody().GetILAsByteArray();
            List<IlInstr> list = new List<IlInstr>();
            int i = 0;
            while (i < il.Length)
            {
                int start = i;
                short code = il[i];
                if (il[i] == 0xFE) { code = (short)(0xFE00 | il[i + 1]); i += 2; } else { i += 1; }
                System.Reflection.Emit.OpCode op;
                if (!opTable.TryGetValue(code, out op))
                    throw new InvalidOperationException("неизвестный опкод на IL_" + start.ToString("X4", CultureInfo.InvariantCulture));
                string target = "";
                switch (op.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone: break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                    case System.Reflection.Emit.OperandType.ShortInlineI:
                    case System.Reflection.Emit.OperandType.ShortInlineVar: i += 1; break;
                    case System.Reflection.Emit.OperandType.InlineVar: i += 2; break;
                    case System.Reflection.Emit.OperandType.InlineBrTarget:
                    case System.Reflection.Emit.OperandType.InlineI:
                    case System.Reflection.Emit.OperandType.ShortInlineR: i += 4; break;
                    case System.Reflection.Emit.OperandType.InlineI8:
                    case System.Reflection.Emit.OperandType.InlineR: i += 8; break;
                    case System.Reflection.Emit.OperandType.InlineSwitch:
                        { int k = BitConverter.ToInt32(il, i); i += 4 + 4 * k; break; }
                    case System.Reflection.Emit.OperandType.InlineMethod:
                    case System.Reflection.Emit.OperandType.InlineField:
                        {
                            int tok = BitConverter.ToInt32(il, i); i += 4;
                            MemberInfo mi = m.Module.ResolveMember(tok);
                            target = (mi.DeclaringType == null ? "" : mi.DeclaringType.Name + ".") + mi.Name;
                            break;
                        }
                    case System.Reflection.Emit.OperandType.InlineString:
                    case System.Reflection.Emit.OperandType.InlineType:
                    case System.Reflection.Emit.OperandType.InlineTok:
                    case System.Reflection.Emit.OperandType.InlineSig: i += 4; break;
                    default:
                        throw new InvalidOperationException("неучтённый вид операнда " + op.OperandType);
                }
                list.Add(new IlInstr { Offset = start, Op = op.Name, Target = target });
            }
            return list;
        }

        /// <summary>
        /// Где ВНУТРИ метода <paramref name="where"/> упало исключение: кадр
        /// ищется по метаданным метода, смещение IL — из кадра, инструкция —
        /// из тела метода.
        /// </summary>
        static IlSite SiteOf(Exception ex, MethodBase where)
        {
            IlSite s = new IlSite();
            if (ex == null) return s;
            Exception e = ex;
            while ((e is TargetInvocationException || e is TypeInitializationException)
                   && e.InnerException != null)
            {
                e = e.InnerException;
            }
            StackTrace st = new StackTrace(e, false);
            for (int i = 0; i < st.FrameCount; i++)
            {
                MethodBase fm = st.GetFrame(i).GetMethod();
                if (fm != null && fm.Module == where.Module && fm.MetadataToken == where.MetadataToken)
                {
                    s.InMethod = true;
                    s.Offset = st.GetFrame(i).GetILOffset();
                    break;
                }
            }
            if (!s.InMethod) return s;
            // ⚠ Смещение кадра — НЕ сама падающая инструкция, а ближайшая
            //   граница карты IL → машинный код не позже места падения
            //   (измерено П154 24.09.2026 на Debug: род I дал `IL_0032 ldarg.0`
            //   — начало оператора, род II — `IL_001C` ровно на `callvirt`,
            //   потому что граница стоит и сразу за предыдущим вызовом). Отсюда
            //   правило: от границы вперёд до ПЕРВОЙ разыменовывающей
            //   инструкции — пустой получатель падает только на ней.
            List<IlInstr> body = Decode(where);
            int k0 = body.FindIndex(x => x.Offset == s.Offset);
            if (k0 < 0) return s;
            s.OnBoundary = true;
            s.Boundary = body[k0].ToString();
            for (int k = k0; k < body.Count; k++)
            {
                if (!Derefs(body[k].Op)) continue;
                s.Op = body[k].Op;
                s.Target = body[k].Target;
                s.At = body[k].Offset;
                s.Prev = k > 0 ? body[k - 1].ToString() : "<начало метода>";
                break;
            }
            return s;
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
