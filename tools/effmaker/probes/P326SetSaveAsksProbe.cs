// Проба полосы П326 (15.10.2026): «Сохранить» редактора наборов при открытом редакторе нуклидов
// с несохранёнными правками — ДО и ПОСЛЕ правки. Журнал — handover/handover-2026-10-15-p326-set-save-asks.md;
// находка — П318 (В3), handover/handover-2026-10-15-p318-revert-and-close.md.
//
// Решение Amber вопросником 15.10.2026, дословно: «Спросить перед записью (Рекомендую)».
//
// Библиотека и наборы лежат в ОДНОМ файле (`NuclideDefinition.xml`), и запись наборов пишет
// всё, что в памяти, — в том числе правки ячеек и галки «Видимый» немодального редактора
// нуклидов, ещё не сохранённые им самим. Мерится:
//   A  «Отмена» на вопросе — не пишет ничего, обе пометки «изменено» остаются;
//   B  «Да» — пишет наборы и правки нуклидов, редактор нуклидов становится «сохранён»;
//   C  «Нет» — пишет только наборы (правка имени линии — НЕ в файле, покраска набора — в
//      файле), правка остаётся в окне нуклидов; его «Нет» потом сводит память с файлом;
//   D  правка поля справа (не перенесённая в линию) + «Да» — поле записано;
//   E  закрытие редактора наборов: «Сохранить?» → «Да» → вопрос о правках нуклидов → «Отмена» —
//      окно остаётся открытым, файл не тронут;
//   K  контроль: редактор нуклидов закрыт / открыт без правок — вопроса нет, запись как прежде;
//      «Сохранить?» при закрытии виден автоответчику (положительный контроль обвязки);
//   G  (вне поручения, починено в полосе) «Сохранить» → правка → закрыть → «Нет»: записанное
//      остаётся в памяти;
//   H  (вне поручения, починено в полосе) «Сохранить» → закрыть: панель поиска пиков
//      пересчитана (`A31`); контроль — окно без правок панель не трогает, «Нет» — трогает.
//
// ЗАПУСК — из каталога проб (BecquerelMonitor.exe и config\ рядом):
//
//   P326SetSaveAsksProbe.exe --tmp=<каталог>
//
// ⛔ Пишет проба ТОЛЬКО копию библиотеки в <каталог>: путь файла менеджера переставляется
// отражением до первой записи. Каталог config\ пробы и живой %AppData%\BecqMoni\config
// сверяются sha256 до и после. Окна форм — вне экрана (−4000, −4000); настоящие окна — только
// вопросы приложения (MessageBox), на них отвечает поток-автоответчик (k-му окну — k-й ответ).
// Проба зовёт приложение тем, что есть И ДО, И ПОСЛЕ правки (поля отражением, щелчок —
// PerformClick), поэтому один exe работает на обеих сборках: сборка ДО — положительный контроль.
//
// Строка вывода — «<шаг>\tчто мерено\tзначение\tЧИСТО|ДЕФЕКТ».
// Коды: 0 — дефектов нет; 4 — есть дефекты (на сборке ДО — ожидаемо); 5 — обвязка; 2 — ключи.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;
using XPTable.Models;

static class P326SetSaveAsksProbe
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;
    const string Suffix = "-P326";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly CultureInfo Ru = new CultureInfo("ru-RU");
    static int defects, rig;
    static StringWriter errors = new StringWriter();
    static string work, pristine;
    static NuclideDefinitionManager mgr;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string tmp = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
            else { Console.WriteLine("неизвестный ключ: " + a); return 2; }
        }
        if (string.IsNullOrEmpty(tmp))
        {
            Console.WriteLine("нужен ключ --tmp=<каталог>: туда кладётся копия библиотеки, её и пишет проба");
            return 2;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Console.WriteLine(ProbeTargetFramework.Describe());
        string exe = typeof(MainForm).Assembly.Location;
        Console.WriteLine("приложение: " + exe + "  ("
            + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Thread.CurrentThread.CurrentCulture = Inv;
        Thread.CurrentThread.CurrentUICulture = Ru;
        Application.EnableVisualStyles();

        string cfgDir = Path.Combine(Path.GetDirectoryName(exe), "config");
        string amberDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BecqMoni", "config");
        Dictionary<string, string> cfgBefore = HashTree(cfgDir);
        Dictionary<string, string> amberBefore = HashTree(amberDir);
        Console.WriteLine("SETUP\tconfig пробы: {0} файлов; живой конфиг {1}: {2} файлов", cfgBefore.Count, amberDir, amberBefore.Count);

        TextWriter realErr = Console.Error;
        Console.SetError(errors);
        try
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            mgr = NuclideDefinitionManager.GetInstance();
            Directory.CreateDirectory(tmp);
            pristine = Path.Combine(tmp, "pristine_NuclideDefinition.xml");
            work = Path.Combine(tmp, "NuclideDefinition.xml");
            File.Copy(Package.GetInstance().NuclideDefinition, pristine, true);
            SetField(mgr, "nuclideDefinitionFilename", work);
            Console.WriteLine("SETUP\tбиблиотека менеджера переставлена: {0}", work);
            if (Fresh("SETUP"))
            {
                Run();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ обвязка: " + ex);
            rig++;
        }
        finally
        {
            Console.SetError(realErr);
            DialogAnswerer.Stop();
        }

        Section("сверка записей");
        List<string> changed = Diff(cfgBefore, HashTree(cfgDir));
        if (changed.Count > 0)
        {
            Console.WriteLine("⛔ обвязка: config пробы изменён: " + string.Join(", ", changed));
            rig++;
        }
        else
        {
            Console.WriteLine("W\tconfig пробы не тронут\t{0} файлов совпали\tЧИСТО", cfgBefore.Count);
        }
        List<string> amberChanged = Diff(amberBefore, HashTree(amberDir));
        if (amberChanged.Count > 0)
        {
            Console.WriteLine("⛔ обвязка: ЖИВОЙ конфиг изменился: " + string.Join(", ", amberChanged));
            rig++;
        }
        else
        {
            Console.WriteLine("W\tживой конфиг %AppData%\\BecqMoni\\config не тронут\t{0} файлов совпали\tЧИСТО", amberBefore.Count);
        }

        string err = errors.ToString();
        if (err.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("--- поток ошибок приложения (AppUi без окон) ---");
            Console.WriteLine(err.TrimEnd());
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ ПРОБЫ: дефектов {0}, отказов обвязки {1}", defects, rig);
        if (rig > 0) return 5;
        return defects == 0 ? 0 : 4;
    }

    // ================================================================ сцена
    static NuclideSet s0;
    static NuclideDefinition lineL, lineO, lineO2;

    /// <summary>
    /// Чистая копия библиотеки на диск и в память (объекты новые). Набор s0 — первый с линией
    /// L (уникальные имя+энергия); O, O2 — линии вне s0.
    /// </summary>
    static bool Fresh(string when)
    {
        File.Copy(pristine, work, true);
        if (!mgr.LoadDefinitionFile(true))
        {
            Console.WriteLine("⛔ обвязка: копия библиотеки не прочиталась");
            rig++;
            return false;
        }
        Func<NuclideDefinition, bool> unique = d => !string.IsNullOrEmpty(d.Name) && d.NuclideColor != null
            && mgr.NuclideDefinitions.Count(x => x.Name == d.Name && x.Energy == d.Energy) == 1;
        s0 = mgr.NuclideSets.FirstOrDefault(s => mgr.NuclideDefinitions.Any(d => d.Sets.Contains(s.Id) && unique(d)));
        if (s0 == null)
        {
            Console.WriteLine("⛔ обвязка: нет набора с линией");
            rig++;
            return false;
        }
        lineL = mgr.NuclideDefinitions.Where(d => d.Sets.Contains(s0.Id) && unique(d)).Skip(1).FirstOrDefault()
                ?? mgr.NuclideDefinitions.First(d => d.Sets.Contains(s0.Id) && unique(d));
        List<NuclideDefinition> outs = mgr.NuclideDefinitions.Where(d => !d.Sets.Contains(s0.Id) && unique(d) && d.Name != lineL.Name).ToList();
        if (outs.Count < 2)
        {
            Console.WriteLine("⛔ обвязка: вне набора меньше двух линий");
            rig++;
            return false;
        }
        lineO = outs[0];
        lineO2 = outs[outs.Count - 1];
        if (when == "SETUP")
        {
            Console.WriteLine("SETUP\tбиблиотека: линий {0}, наборов {1}; s0 «{2}», L = {3}, O = {4}, O2 = {5}",
                mgr.NuclideDefinitions.Count, mgr.NuclideSets.Count, s0.Name, Line(lineL), Line(lineO), Line(lineO2));
        }
        return true;
    }

    static void Run()
    {
        ScenarioCancel();
        ScenarioYes();
        ScenarioNo();
        ScenarioFieldYes();
        ScenarioClosing();
        Controls();
        ScenarioSaveThenNo();
        ScenarioRefreshAfterSave();
    }

    // ---- A: «Отмена»
    static void ScenarioCancel()
    {
        Section("A. правка имени в редакторе нуклидов + правка набора → «Сохранить» наборов → «Отмена»");
        if (!Fresh("A")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf, null); return; }
        NuclideSetForm sf = OpenSets(null);
        AddToSet(sf, s0, lineO);
        string sha0 = Sha(work);
        List<string> seen = Answered(new[] { IDCANCEL }, () => Press(sf, "buttonSave"));
        Questions("A", seen);
        Verdict("A", "вопрос о правках редактора нуклидов задан", Count(seen), seen.Count == 1);
        Verdict("A", "«Отмена» не пишет файл (sha256)", Sha(work) == sha0 ? "не тронут" : "ЗАПИСАН", Sha(work) == sha0);
        Verdict("A", "редактор наборов по-прежнему «изменено»", SetDirty(sf), SetIsDirty(sf));
        Verdict("A", "редактор нуклидов по-прежнему «изменено»", NucDirty(nf), NucIsDirty(nf));
        CloseAll(nf, sf);
    }

    // ---- B: «Да»
    static void ScenarioYes()
    {
        Section("B. то же → «Да» (записать вместе)");
        if (!Fresh("B")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        double e = lineL.Energy;
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf, null); return; }
        NuclideSetForm sf = OpenSets(null);
        AddToSet(sf, s0, lineO);
        List<string> seen = Answered(new[] { IDYES }, () => Press(sf, "buttonSave"));
        Questions("B", seen);
        Verdict("B", "вопрос о правках редактора нуклидов задан", Count(seen), seen.Count == 1);
        NuclideDefinitionFile disk = ReadDisk();
        Verdict("B", "в файле: линия O в наборе s0 (наборы записаны)", InSet(disk, lineO.Name, lineO.Energy), InSetBool(disk, lineO.Name, lineO.Energy));
        Verdict("B", "в файле: линия L с новым именем (правка нуклидов записана)", FileName(disk, e, n0), FileHas(disk, n0 + Suffix, e));
        Verdict("B", "редактор нуклидов «сохранён»", NucDirty(nf), !NucIsDirty(nf));
        Verdict("B", "редактор наборов «сохранён»", SetDirty(sf), !SetIsDirty(sf));
        List<string> onClose = Answered(new[] { IDNO }, () => nf.Close());
        Questions("B", onClose);
        Verdict("B", "закрытие редактора нуклидов после «Да» — вопроса нет", Count(onClose), onClose.Count == 0);
        disk = ReadDisk();
        Verdict("B", "память = файл: имя L", "память «" + lineL.Name + "», файл " + FileName(disk, e, n0), FileHas(disk, lineL.Name, e));
        CloseAll(nf, sf);
    }

    // ---- C: «Нет» + покраска набора
    static void ScenarioNo()
    {
        Section("C. правка имени L + покраска набора s0 (L в нём) + O в s0 → «Нет» (только наборы) → «Нет» редактора нуклидов");
        if (!Fresh("C")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        double e = lineL.Energy;
        int c0 = lineL.NuclideColor.Color.ToArgb();
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf, null); return; }
        NuclideSetForm sf = OpenSets(null);
        AddToSet(sf, s0, lineO);
        Color paint = c0 == Color.Magenta.ToArgb() ? Color.Orange : Color.Magenta;
        ((global::ColorComboBox.ColorComboBox)Field(sf, "assignColorComboBox")).SelectedColor = paint;
        SetField(sf, "selectedSet", s0);
        Call(sf, "buttonAssignColor_Click", null, EventArgs.Empty);
        int painted = lineL.NuclideColor.Color.ToArgb();
        Console.WriteLine("SETUP\tL: «{0}» → «{1}», цвет {2:X8} → {3:X8}", n0, lineL.Name, c0, painted);
        if (painted == c0)
        {
            Console.WriteLine("⛔ обвязка: покраска набора не легла на L");
            rig++;
        }
        List<string> seen = Answered(new[] { IDNO }, () => Press(sf, "buttonSave"));
        Questions("C", seen);
        Verdict("C", "вопрос о правках редактора нуклидов задан", Count(seen), seen.Count == 1);
        NuclideDefinitionFile disk = ReadDisk();
        Verdict("C", "в файле: O в s0 (наборы записаны)", InSet(disk, lineO.Name, lineO.Energy), InSetBool(disk, lineO.Name, lineO.Energy));
        Verdict("C", "в файле: имя L прежнее (правка нуклидов НЕ записана)", FileName(disk, e, n0), FileHas(disk, n0, e) && !FileHas(disk, n0 + Suffix, e));
        int fileColor = FileColor(disk, e, n0);
        Verdict("C", "в файле: цвет L (" + FileName(disk, e, n0) + ") — покраска набора", fileColor.ToString("X8", Inv), fileColor == painted);
        Verdict("C", "память: правка имени L цела", "«" + lineL.Name + "»", lineL.Name == n0 + Suffix);
        Verdict("C", "редактор нуклидов по-прежнему «изменено»", NucDirty(nf), NucIsDirty(nf));
        Verdict("C", "редактор наборов «сохранён»", SetDirty(sf), !SetIsDirty(sf));
        CloseQuiet(sf);
        List<string> onClose = Answered(new[] { IDNO }, () => nf.Close());
        Questions("C", onClose);
        disk = ReadDisk();
        bool nameSame = FileHas(disk, lineL.Name, e);
        int colorNow = lineL.NuclideColor.Color.ToArgb();
        Verdict("C", "после «Нет» редактора нуклидов: имя в памяти = в файле",
                "память «" + lineL.Name + "», файл " + FileName(disk, e, n0), nameSame);
        Verdict("C", "после «Нет» редактора нуклидов: цвет в памяти = в файле (покраска цела)",
                "память " + colorNow.ToString("X8", Inv) + ", файл " + FileColor(disk, e, n0).ToString("X8", Inv),
                colorNow == FileColor(disk, e, n0));
        CloseAll(nf, sf);
    }

    // ---- D: правка поля справа + «Да»
    static void ScenarioFieldYes()
    {
        Section("D. правка поля «Интенсивность» (не перенесена в линию) + O в s0 → «Сохранить» наборов → «Да»");
        if (!Fresh("D")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        if (!Select(nf, lineL)) { CloseAll(nf, null); return; }
        double i0 = lineL.Intencity;
        double i1 = Math.Round(i0 + 1.25, 4);
        Control box = (Control)Field(nf, "intensityTextBox");
        box.Text = i1.ToString(Inv);
        Pump(20);
        Console.WriteLine("SETUP\tL {0}: поле интенсивности {1} → {2}; в линии {3}; Dirty={4}",
            Line(lineL), i0.ToString(Inv), box.Text, lineL.Intencity.ToString(Inv), lineL.Dirty);
        NuclideSetForm sf = OpenSets(null);
        AddToSet(sf, s0, lineO);
        List<string> seen = Answered(new[] { IDYES }, () => Press(sf, "buttonSave"));
        Questions("D", seen);
        Verdict("D", "вопрос о правках редактора нуклидов задан", Count(seen), seen.Count == 1);
        NuclideDefinitionFile disk = ReadDisk();
        NuclideDefinition f = FileLine(disk, lineL.Name, lineL.Energy);
        Verdict("D", "в файле: интенсивность L из поля", f == null ? "линии нет" : f.Intencity.ToString(Inv), f != null && f.Intencity == i1);
        Verdict("D", "в файле: O в s0", InSet(disk, lineO.Name, lineO.Energy), InSetBool(disk, lineO.Name, lineO.Energy));
        Verdict("D", "редактор нуклидов «сохранён»", NucDirty(nf), !NucIsDirty(nf));
        CloseAll(nf, sf);
    }

    // ---- E: закрытие редактора наборов → «Да» → «Отмена»
    static void ScenarioClosing()
    {
        Section("E. правка имени L + O в s0 → закрыть редактор наборов → «Сохранить?» «Да» → вопрос → «Отмена»");
        if (!Fresh("E")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf, null); return; }
        NuclideSetForm sf = OpenSets(null);
        AddToSet(sf, s0, lineO);
        string sha0 = Sha(work);
        List<string> seen = Answered(new[] { IDYES, IDCANCEL }, () => sf.Close());
        Questions("E", seen);
        Verdict("E", "вопросов два: «Сохранить?» и о правках редактора нуклидов", Count(seen), seen.Count == 2);
        Verdict("E", "«Отмена» не пишет файл (sha256)", Sha(work) == sha0 ? "не тронут" : "ЗАПИСАН", Sha(work) == sha0);
        Verdict("E", "окно наборов осталось открытым с правкой", sf.IsDisposed ? "закрыто" : "открыто, " + SetDirty(sf),
                !sf.IsDisposed && SetIsDirty(sf));
        CloseAll(nf, sf);
    }

    // ---- K: контроль
    static void Controls()
    {
        Section("K. контроль: редактор нуклидов закрыт / открыт без правок — вопроса нет, запись как прежде");
        if (!Fresh("K1")) return;
        {
            NuclideSetForm sf = OpenSets(null);
            AddToSet(sf, s0, lineO);
            string sha0 = Sha(work);
            List<string> seen = Answered(new[] { IDNO }, () => Press(sf, "buttonSave"));
            Questions("K1", seen);
            Verdict("K1", "редактора нуклидов нет: вопросов", Count(seen), seen.Count == 0);
            NuclideDefinitionFile disk = ReadDisk();
            Verdict("K1", "файл записан, O в s0", (Sha(work) != sha0 ? "записан, " : "НЕ записан, ") + InSet(disk, lineO.Name, lineO.Energy),
                    Sha(work) != sha0 && InSetBool(disk, lineO.Name, lineO.Energy));
            Verdict("K1", "редактор наборов «сохранён»", SetDirty(sf), !SetIsDirty(sf));
            // Положительный контроль автоответчика: «Сохранить?» при закрытии с правкой виден.
            AddToSet(sf, s0, lineO2);
            List<string> onClose = Answered(new[] { IDNO }, () => sf.Close());
            Questions("K1", onClose);
            Verdict("K1", "контроль обвязки: «Сохранить?» при закрытии с правкой виден автоответчику", Count(onClose), onClose.Count == 1);
            CloseAll(null, sf);
        }
        if (!Fresh("K2")) return;
        {
            NuclideDefinitionForm nf = OpenNuclides();
            Select(nf, lineL);
            NuclideSetForm sf = OpenSets(null);
            AddToSet(sf, s0, lineO);
            string sha0 = Sha(work);
            List<string> seen = Answered(new[] { IDNO }, () => Press(sf, "buttonSave"));
            Questions("K2", seen);
            Verdict("K2", "редактор нуклидов открыт без правок: вопросов", Count(seen), seen.Count == 0);
            NuclideDefinitionFile disk = ReadDisk();
            Verdict("K2", "файл записан, O в s0", (Sha(work) != sha0 ? "записан, " : "НЕ записан, ") + InSet(disk, lineO.Name, lineO.Energy),
                    Sha(work) != sha0 && InSetBool(disk, lineO.Name, lineO.Energy));
            CloseAll(nf, sf);
        }
    }

    // ---- G: «Сохранить» → правка → закрыть «Нет»
    static void ScenarioSaveThenNo()
    {
        Section("G. (вне поручения) O в s0 → «Сохранить» → O2 в s0 → закрыть → «Сохранить?» «Нет»");
        if (!Fresh("G")) return;
        NuclideSetForm sf = OpenSets(null);
        AddToSet(sf, s0, lineO);
        Answered(new[] { IDNO }, () => Press(sf, "buttonSave"));
        AddToSet(sf, s0, lineO2);
        List<string> seen = Answered(new[] { IDNO }, () => sf.Close());
        Questions("G", seen);
        NuclideDefinitionFile disk = ReadDisk();
        bool oMem = lineO.Sets.Contains(s0.Id), o2Mem = lineO2.Sets.Contains(s0.Id);
        Verdict("G", "записанное пережило «Нет»: O в s0 в памяти (в файле — " + InSet(disk, lineO.Name, lineO.Energy) + ")",
                oMem ? "в наборе" : "НЕ в наборе", oMem && InSetBool(disk, lineO.Name, lineO.Energy));
        Verdict("G", "незаписанное снято: O2 вне s0 в памяти и в файле", (o2Mem ? "в наборе" : "вне набора") + ", файл " + InSet(disk, lineO2.Name, lineO2.Energy),
                !o2Mem && !InSetBool(disk, lineO2.Name, lineO2.Energy));
        CloseAll(null, sf);
    }

    // ---- H: «Сохранить» → закрыть: пересчёт панели поиска пиков
    static void ScenarioRefreshAfterSave()
    {
        Section("H. (вне поручения) пересчёт панели поиска пиков при закрытии редактора наборов (`A31`)");
        if (!Fresh("H")) return;
        MainForm mf = new MainForm();
        try
        {
            RefreshSteps(mf);
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ обвязка (H): " + ex);
            rig++;
        }
        finally
        {
            mf.Dispose();
        }
    }

    static void RefreshSteps(MainForm mf)
    {
        {
            // Панель заводит не конструктор MainForm, а его загрузка; проба ставит свою в то же поле.
            DCPeakDetectionView panel = new DCPeakDetectionView(mf);
            SetField(mf, "dcPeakDetectionView", panel);
            TableModel rows = (TableModel)Field(panel, "tableModel1");
            mf.ActiveDocument = new DocEnergySpectrum();

            // H0: открыть и закрыть без правок — панель не трогается.
            rows.Rows.Add(new Row());
            NuclideSetForm sf = OpenSets(mf);
            Answered(new[] { IDNO }, () => sf.Close());
            Verdict("H0", "контроль: окно без правок панель не пересчитывает (метка цела)", "строк " + rows.Rows.Count.ToString(Inv), rows.Rows.Count == 1);
            CloseQuiet(sf);

            // H1: правка → закрыть → «Нет» — пересчёт (положительный контроль метки).
            rows.Rows.Clear();
            rows.Rows.Add(new Row());
            sf = OpenSets(mf);
            AddToSet(sf, s0, lineO);
            Answered(new[] { IDNO }, () => sf.Close());
            Verdict("H1", "контроль: правка → «Нет» — панель пересчитана (метка снята)", "строк " + rows.Rows.Count.ToString(Inv), rows.Rows.Count == 0);
            CloseQuiet(sf);

            // H2: правка → «Сохранить» → закрыть — пересчёт обязан быть.
            if (!Fresh("H2")) return;
            rows.Rows.Clear();
            rows.Rows.Add(new Row());
            sf = OpenSets(mf);
            AddToSet(sf, s0, lineO);
            Answered(new[] { IDNO }, () => Press(sf, "buttonSave"));
            List<string> seen = Answered(new[] { IDNO }, () => sf.Close());
            Questions("H2", seen);
            Verdict("H2", "«Сохранить» → закрыть: панель пересчитана (метка снята)", "строк " + rows.Rows.Count.ToString(Inv), rows.Rows.Count == 0);
            CloseQuiet(sf);
        }
    }

    // ================================================================ помощники сцены
    static NuclideDefinitionForm OpenNuclides()
    {
        NuclideDefinitionForm f = new NuclideDefinitionForm();
        ShowOff(f);
        return f;
    }

    static NuclideSetForm OpenSets(MainForm owner)
    {
        NuclideSetForm f = new NuclideSetForm(owner);
        ShowOff(f);
        return f;
    }

    static void AddToSet(NuclideSetForm f, NuclideSet set, NuclideDefinition line)
    {
        SetField(f, "selectedSet", set);
        Call(f, "UpdateNuclideDefinition", line, true);
    }

    static bool Select(NuclideDefinitionForm f, NuclideDefinition line)
    {
        TableModel m = (TableModel)Field(f, "tableModel1");
        int row = -1;
        for (int i = 0; i < m.Rows.Count; i++)
        {
            if (ReferenceEquals(m.Rows[i].Tag, line)) { row = i; break; }
        }
        if (row < 0)
        {
            Console.WriteLine("⛔ обвязка: строки линии {0} в таблице нет", Line(line));
            rig++;
            return false;
        }
        m.Selections.SelectCell(row, 0);
        Pump(30);
        if (!ReferenceEquals(Field(f, "activeNuclide"), line))
        {
            Console.WriteLine("⛔ обвязка: строка не стала активной");
            rig++;
            return false;
        }
        return true;
    }

    /// <summary>Правка имени в таблице настоящим редактором XPTable (как P318, `NucBaseCardProbeP275`).</summary>
    static bool EditName(NuclideDefinitionForm f, NuclideDefinition line, string suffix)
    {
        if (!Select(f, line)) return false;
        XPTable.Models.Table t = (XPTable.Models.Table)Field(f, "table1");
        TableModel m = t.TableModel;
        int row = -1;
        for (int i = 0; i < m.Rows.Count; i++)
        {
            if (ReferenceEquals(m.Rows[i].Tag, line)) { row = i; break; }
        }
        string name0 = line.Name;
        t.EditCell(row, 0);
        if (!t.IsEditing)
        {
            Console.WriteLine("⛔ обвязка: ячейка имени не открылась на правку");
            rig++;
            return false;
        }
        object editor = t.EditingCellEditor;
        TextBox box = (TextBox)editor.GetType().GetProperty("TextBox").GetValue(editor);
        box.Text = name0 + suffix;
        Type et = editor.GetType();
        while (et != null && et.GetProperty("UserEdit", BindingFlags.NonPublic | BindingFlags.Instance) == null) et = et.BaseType;
        if (et != null) et.GetProperty("UserEdit", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(editor, true);
        t.StopEditing();
        Pump(20);
        if (line.Name != name0 + suffix || !line.Dirty)
        {
            Console.WriteLine("⛔ обвязка: правка имени в линию не легла ({0}, Dirty={1})", line.Name, line.Dirty);
            rig++;
            return false;
        }
        Console.WriteLine("SETUP\tправка в редакторе нуклидов: «{0}» {1} → «{2}» (в памяти, не записана)", name0, line.Energy.ToString(Inv), line.Name);
        return true;
    }

    static void Press(Form f, string buttonField)
    {
        Button b = (Button)Field(f, buttonField);
        if (!b.Enabled)
        {
            Console.WriteLine("⛔ обвязка: кнопка {0} выключена", buttonField);
            rig++;
            return;
        }
        f.ActiveControl = b;
        Pump(20);
        if (!f.IsDisposed) b.PerformClick();
    }

    static string SetDirty(NuclideSetForm f)
    {
        return "dirty=" + Field(f, "dirty") + ", «Сохранить» " + (((Button)Field(f, "buttonSave")).Enabled ? "активна" : "погашена");
    }

    static bool SetIsDirty(NuclideSetForm f)
    {
        return Equals(Field(f, "dirty"), true) && ((Button)Field(f, "buttonSave")).Enabled;
    }

    static string NucDirty(NuclideDefinitionForm f)
    {
        NuclideDefinition a = (NuclideDefinition)Field(f, "activeNuclide");
        return "правки линий в памяти=" + Field(f, "definitionsDirty") + ", активная Dirty=" + (a != null && a.Dirty)
               + ", «Сохранить» " + (((Button)Field(f, "button6")).Enabled ? "активна" : "погашена");
    }

    static bool NucIsDirty(NuclideDefinitionForm f)
    {
        NuclideDefinition a = (NuclideDefinition)Field(f, "activeNuclide");
        return Equals(Field(f, "definitionsDirty"), true) || (a != null && a.Dirty) || ((Button)Field(f, "button6")).Enabled;
    }

    /// <summary>Закрыть окна сцены, на любые вопросы — «Нет».</summary>
    static void CloseAll(NuclideDefinitionForm nf, NuclideSetForm sf)
    {
        if (sf != null) CloseQuiet(sf);
        if (nf != null) CloseQuiet(nf);
    }

    static void CloseQuiet(Form f)
    {
        if (f == null || f.IsDisposed) return;
        Answered(new[] { IDNO, IDNO, IDNO }, () => f.Close());
        if (!f.IsDisposed)
        {
            Console.WriteLine("⛔ обвязка: окно {0} не закрылось", f.GetType().Name);
            rig++;
            f.Dispose();
        }
    }

    // ================================================================ файл
    static NuclideDefinitionFile ReadDisk()
    {
        XmlSerializer xs = new XmlSerializer(typeof(NuclideDefinitionFile));
        using (FileStream s = new FileStream(work, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            return (NuclideDefinitionFile)xs.Deserialize(s);
        }
    }

    static NuclideDefinition FileLine(NuclideDefinitionFile f, string name, double energy)
    {
        return f.NuclideDefinitions.FirstOrDefault(d => d.Name == name && d.Energy == energy);
    }

    static bool FileHas(NuclideDefinitionFile f, string name, double energy)
    {
        return FileLine(f, name, energy) != null;
    }

    static string FileName(NuclideDefinitionFile f, double energy, string n0)
    {
        List<string> names = f.NuclideDefinitions.Where(d => d.Energy == energy && d.Name != null
                                                             && d.Name.StartsWith(n0, StringComparison.Ordinal))
                              .Select(d => "«" + d.Name + "»").ToList();
        return names.Count == 0 ? "линии нет" : string.Join(", ", names);
    }

    /// <summary>Цвет линии L в файле — по энергии и началу имени (имя в файле может быть правленым).</summary>
    static int FileColor(NuclideDefinitionFile f, double energy, string n0)
    {
        NuclideDefinition d = f.NuclideDefinitions.FirstOrDefault(x => x.Energy == energy && x.Name != null
                                                                       && x.Name.StartsWith(n0, StringComparison.Ordinal));
        return d == null || d.NuclideColor == null ? 0 : d.NuclideColor.Color.ToArgb();
    }

    static bool InSetBool(NuclideDefinitionFile f, string name, double energy)
    {
        NuclideDefinition d = FileLine(f, name, energy);
        return d != null && d.Sets.Contains(s0.Id);
    }

    static string InSet(NuclideDefinitionFile f, string name, double energy)
    {
        NuclideDefinition d = FileLine(f, name, energy);
        return d == null ? "линии нет" : d.Sets.Contains(s0.Id) ? "в наборе" : "вне набора";
    }

    static string Line(NuclideDefinition d)
    {
        return d.Name + " " + d.Energy.ToString(Inv);
    }

    // ================================================================ общее
    static List<string> Answered(int[] answers, Action act)
    {
        DialogAnswerer.Start(answers);
        try
        {
            act();
            Pump(200);
        }
        finally
        {
            DialogAnswerer.Stop();
        }
        return DialogAnswerer.Seen();
    }

    static void Questions(string step, List<string> seen)
    {
        Console.WriteLine("{0}\tвопросы: {1}", step, seen.Count == 0 ? "нет" : string.Join(" || ", seen));
    }

    static string Count(List<string> seen)
    {
        return "вопросов " + seen.Count.ToString(Inv);
    }

    static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + name);
    }

    static void Verdict(string id, string what, string got, bool clean)
    {
        if (!clean) defects++;
        Console.WriteLine("{0}\t{1}\t{2}\t{3}", id, what, got ?? "(null)", clean ? "ЧИСТО" : "ДЕФЕКТ");
    }

    static void ShowOff(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.ShowInTaskbar = false;
        f.Show();
        Pump(50);
    }

    static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms);
    }

    static object Field(object o, string name)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo fi = t.GetField(name, Any);
            if (fi != null) return fi.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static void SetField(object o, string name, object value)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo fi = t.GetField(name, Any);
            if (fi != null) { fi.SetValue(o, value); return; }
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static object Call(object o, string name, params object[] args)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            foreach (MethodInfo m in t.GetMethods(Any))
            {
                if (m.Name == name && m.GetParameters().Length == args.Length)
                {
                    try { return m.Invoke(o, args); }
                    catch (TargetInvocationException ex) { throw ex.InnerException; }
                }
            }
        }
        throw new MissingMethodException(o.GetType().Name, name);
    }

    static string Sha(string path)
    {
        using (SHA256 h = SHA256.Create())
        using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
    }

    static Dictionary<string, string> HashTree(string dir)
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return map;
        foreach (string p in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { map[p.Substring(dir.Length)] = Sha(p); }
            catch (IOException) { map[p.Substring(dir.Length)] = "занят"; }
        }
        return map;
    }

    static List<string> Diff(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        List<string> d = new List<string>();
        foreach (var kv in a) { string v; if (!b.TryGetValue(kv.Key, out v) || v != kv.Value) d.Add(kv.Key); }
        foreach (var kv in b) if (!a.ContainsKey(kv.Key)) d.Add(kv.Key + " (новый)");
        return d;
    }

    /// <summary>
    /// Отвечает на вопросы приложения (окна класса #32770 своего процесса): k-му новому окну —
    /// k-й ответ списка (WM_COMMAND), повторяя его, пока окно не уйдёт; после четырёх
    /// безуспешных — «Нет», «ОК», «Отмена» по кругу (чтобы проба не повисла). Записывает тексты.
    /// Окно считается новым по паре «дескриптор + текст»: дескриптор ушедшего окна Windows
    /// может отдать следующему.
    /// </summary>
    static class DialogAnswerer
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        static volatile bool run;
        static Thread thread;
        static int[] answers = { IDNO };
        static int next;
        static readonly Dictionary<string, int> assigned = new Dictionary<string, int>();
        static readonly Dictionary<string, int> tries = new Dictionary<string, int>();
        static readonly List<string> seen = new List<string>();
        static readonly int[] fallback = { IDNO, IDOK, IDCANCEL };

        public static void Start(int[] with)
        {
            lock (seen) { seen.Clear(); tries.Clear(); assigned.Clear(); answers = with; next = 0; }
            run = true;
            thread = new Thread(Loop) { IsBackground = true };
            thread.Start();
        }

        public static void Stop()
        {
            run = false;
            if (thread != null) thread.Join(1000);
            thread = null;
        }

        public static List<string> Seen()
        {
            lock (seen) return new List<string>(seen);
        }

        static void Loop()
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            while (run)
            {
                EnumWindows((h, l) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != me || !IsWindowVisible(h)) return true;
                    StringBuilder cls = new StringBuilder(64);
                    GetClassName(h, cls, 64);
                    if (cls.ToString() != "#32770") return true;
                    string text = Text(h);
                    string key = h.ToInt64().ToString(CultureInfo.InvariantCulture) + "|" + text;
                    lock (seen)
                    {
                        int answer;
                        if (!assigned.TryGetValue(key, out answer))
                        {
                            answer = next < answers.Length ? answers[next] : IDNO;
                            next++;
                            assigned[key] = answer;
                            seen.Add(text);
                        }
                        int n;
                        tries.TryGetValue(key, out n);
                        int send = n < 4 ? answer : fallback[(n - 4) % fallback.Length];
                        PostMessage(h, 0x0111, (IntPtr)send, IntPtr.Zero);
                        tries[key] = n + 1;
                    }
                    return true;
                }, IntPtr.Zero);
                Thread.Sleep(120);
            }
        }

        static string Text(IntPtr h)
        {
            StringBuilder all = new StringBuilder();
            StringBuilder t = new StringBuilder(512);
            GetWindowText(h, t, 512);
            all.Append("[" + t + "] ");
            EnumChildWindows(h, (c, l) =>
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassName(c, cls, 64);
                if (cls.ToString() == "Static")
                {
                    StringBuilder s = new StringBuilder(2048);
                    GetWindowText(c, s, 2048);
                    if (s.Length > 0) all.Append(s.ToString().Replace("\r", " ").Replace("\n", " "));
                }
                return true;
            }, IntPtr.Zero);
            return all.ToString().Trim();
        }
    }
}
