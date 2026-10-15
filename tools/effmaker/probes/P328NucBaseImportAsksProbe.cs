// Проба полосы П328 (15.10.2026, строка A336): импорт из NucBase при открытом редакторе нуклидов
// с несохранёнными правками — ДО и ПОСЛЕ правки. Журнал — handover/handover-2026-10-15-p328-nucbase-import-asks.md;
// находка — П326, handover/handover-2026-10-15-p326-set-save-asks.md; образец пробы — P326SetSaveAsksProbe.
//
// Решение Amber вопросником 15.10.2026, дословно: «Да, тем же приёмом (Рекомендую)».
//
// Библиотека лежит в ОДНОМ файле (`NuclideDefinition.xml`), и запись импорта пишет всё, что в памяти, —
// в том числе правки ячеек и галки «Видимый» немодального редактора нуклидов, ещё не сохранённые им
// самим. Мерится:
//   A  «Отмена» на вопросе — импорта нет: файл не тронут, в памяти нет ни новой линии, ни перезаписи;
//   B  «Да» — пишутся и импорт, и правки нуклидов; редактор нуклидов становится «сохранён»;
//   C  «Нет» — пишется только импорт (правка имени L — НЕ в файле), правка остаётся в окне нуклидов;
//      его «Нет» потом сводит память с файлом и НЕ трогает импортированное;
//   D  правка поля справа (не перенесённая в линию) + «Да» — поле записано;
//   E  «Нет», когда импорт перезаписывает линию, правленную окном (галка «Видимый»): в файле — импорт
//      и галка до правки; «Нет» окна нуклидов снимает только галку;
//   R  «Нет», когда окно переименовало линию, а импорт (без «перезаписать») несёт её же: второй копии
//      линии в файле не заводится (импорт составлен по линиям до правок окна);
//   K  контроль: редактора нуклидов нет / открыт без правок — вопроса нет, импорт как прежде; окно
//      «импорт завершён» автоответчику видно (положительный контроль обвязки);
//   F  (вне поручения, починено в полосе) отказ записи — импорт в памяти откатывается (`A9`);
//   Н  находка, дефектом пробы НЕ считается: несохранённые правки РЕДАКТОРА НАБОРОВ импорт уносит в
//      файл молча (решение Amber было про редактор нуклидов).
//
// ЗАПУСК — из каталога проб (BecquerelMonitor.exe и config\ рядом):
//
//   P328NucBaseImportAsksProbe.exe --tmp=<каталог>
//
// ⛔ Пишет проба ТОЛЬКО копию библиотеки в <каталог>: путь файла менеджера переставляется
// отражением до первой записи. Каталог config\ пробы и живой %AppData%\BecqMoni\config
// сверяются sha256 до и после. Окна форм — вне экрана (−4000, −4000); настоящие окна — только
// вопросы и сообщения приложения (MessageBox), на них отвечает поток-автоответчик (k-му окну —
// k-й ответ). Проба зовёт приложение тем, что есть И ДО, И ПОСЛЕ правки (поля отражением,
// щелчок — PerformClick), поэтому один исходник работает на обеих сборках: сборка ДО —
// положительный контроль.
//
// Строка вывода — «<шаг>\tчто мерено\tзначение\tЧИСТО|ДЕФЕКТ» (у находки — «НАХОДКА»).
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
using BecquerelMonitor.NucBase;
using NucBaseForm = BecquerelMonitor.NucBase.NucBase;
using XPTable.Models;

static class P328NucBaseImportAsksProbe
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;
    const string Suffix = "-P328";
    // Новая линия импорта: Na-22 1274.537 кэВ (в поставочной библиотеке Na-22 нет — проверяется).
    const string NewNucid = "22NA";
    const double NewEnergy = 1274.537;
    const double NewIntensity = 99.94;
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
    static string nucidL, nucidO;

    /// <summary>
    /// Чистая копия библиотеки на диск и в память (объекты новые). L — линия, которую правит
    /// окно нуклидов; O — линия, которую перезаписывает импорт; O2 — линия вне набора s0 (находка Н).
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
        Func<NuclideDefinition, string> nucid = d =>
        {
            string id = NucBaseForm.LineIdentity(d.NuclideName, d.IsElementXray);
            return id.StartsWith("N:", StringComparison.Ordinal) && id.Length > 2 && char.IsDigit(id[2]) ? id.Substring(2) : null;
        };
        // Линия годится, если она единственная со своим нуклидом и энергией (по ключу импорта).
        Func<NuclideDefinition, bool> importable = d => unique(d) && nucid(d) != null
            && mgr.NuclideDefinitions.Count(x => x.Energy == d.Energy
                   && NucBaseForm.LineIdentity(x.NuclideName, x.IsElementXray) == NucBaseForm.LineIdentity(d.NuclideName, d.IsElementXray)) == 1;
        lineL = mgr.NuclideDefinitions.FirstOrDefault(d => d.Name == "Cs-137" && importable(d))
                ?? mgr.NuclideDefinitions.FirstOrDefault(importable);
        lineO = mgr.NuclideDefinitions.FirstOrDefault(d => d.Name == "Am-241" && importable(d) && !ReferenceEquals(d, lineL))
                ?? mgr.NuclideDefinitions.FirstOrDefault(d => importable(d) && !ReferenceEquals(d, lineL) && nucid(d) != nucid(lineL));
        if (lineL == null || lineO == null)
        {
            Console.WriteLine("⛔ обвязка: нет двух линий нуклидов с однозначным ключом импорта");
            rig++;
            return false;
        }
        nucidL = nucid(lineL);
        nucidO = nucid(lineO);
        s0 = mgr.NuclideSets.FirstOrDefault();
        lineO2 = s0 == null ? null
            : mgr.NuclideDefinitions.FirstOrDefault(d => unique(d) && !d.Sets.Contains(s0.Id)
                                                         && !ReferenceEquals(d, lineL) && !ReferenceEquals(d, lineO));
        if (mgr.NuclideDefinitions.Any(d => d.Energy == NewEnergy && NucBaseForm.LineIdentity(d.NuclideName, d.IsElementXray) == "N:" + NewNucid))
        {
            Console.WriteLine("⛔ обвязка: линия Na-22 {0} уже есть в библиотеке — «новой» импорт её не сделает", NewEnergy.ToString(Inv));
            rig++;
            return false;
        }
        if (when == "SETUP")
        {
            Console.WriteLine("SETUP\tбиблиотека: линий {0}, наборов {1}; L = {2} ({3}), O = {4} ({5}), новая N = {6} {7}; s0 «{8}», O2 = {9}",
                mgr.NuclideDefinitions.Count, mgr.NuclideSets.Count, Line(lineL), nucidL, Line(lineO), nucidO,
                NewNucid, NewEnergy.ToString(Inv), s0 == null ? "(нет)" : s0.Name, lineO2 == null ? "(нет)" : Line(lineO2));
        }
        return true;
    }

    static void Run()
    {
        ScenarioCancel();
        ScenarioYes();
        ScenarioNo();
        ScenarioFieldYes();
        ScenarioConflictNo();
        ScenarioRenameNo();
        Controls();
        ScenarioWriteFails();
        FindingSetEditor();
    }

    /// <summary>Строка импорта: новая линия N.</summary>
    static DecayRad RowNew()
    {
        return Rad(NewNucid, NewEnergy, NewIntensity, 2.6018, "Y");
    }

    /// <summary>Строка импорта, совпадающая с линией d по ключу (нуклид + энергия); выход — метка.</summary>
    static DecayRad RowFor(NuclideDefinition d, string nucid, double intensity)
    {
        return Rad(nucid, d.Energy, intensity, 30.08, "Y");
    }

    static DecayRad Rad(string nucid, double energy, double intensity, double hl, string unit)
    {
        return new DecayRad
        {
            Name = nucid, DecayLine = "G", Energy = energy, Intensity = intensity,
            HalfLife = hl, HalfLifeUnit = unit, XrayType = ""
        };
    }

    static double Marker(NuclideDefinition d)
    {
        return Math.Round(d.Intencity + 7.25, 4);
    }

    // ---- A: «Отмена»
    static void ScenarioCancel()
    {
        Section("A. правка имени L в редакторе нуклидов → импорт (N новая, O перезаписать) → «Отмена»");
        if (!Fresh("A")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        double o0 = lineO.Intencity;
        int count0 = mgr.NuclideDefinitions.Count;
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf); return; }
        NucBaseForm nb = OpenImport(true, RowNew(), RowFor(lineO, nucidO, Marker(lineO)));
        string sha0 = Sha(work);
        List<string> seen = Answered(new[] { IDCANCEL, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("A", seen);
        Verdict("A", "вопрос о правках редактора нуклидов задан", Asked(seen), AskedCount(seen) == 1);
        Verdict("A", "«Отмена» не пишет файл (sha256)", Sha(work) == sha0 ? "не тронут" : "ЗАПИСАН", Sha(work) == sha0);
        Verdict("A", "импорта нет: новой линии N в памяти нет", MemHasNew() ? "есть" : "нет", !MemHasNew());
        Verdict("A", "импорта нет: выход O в памяти прежний", lineO.Intencity.ToString(Inv) + " (был " + o0.ToString(Inv) + ")", lineO.Intencity == o0);
        Verdict("A", "импорта нет: линий в памяти столько же", mgr.NuclideDefinitions.Count.ToString(Inv) + " (было " + count0.ToString(Inv) + ")",
                mgr.NuclideDefinitions.Count == count0);
        Verdict("A", "сообщения «импорт завершён» нет", Done(seen), DoneCount(seen) == 0);
        Verdict("A", "редактор нуклидов по-прежнему «изменено», правка имени цела", NucDirty(nf) + ", имя «" + lineL.Name + "»",
                NucIsDirty(nf) && lineL.Name == n0 + Suffix);
        CloseQuiet(nb);
        CloseAll(nf);
    }

    // ---- B: «Да»
    static void ScenarioYes()
    {
        Section("B. то же → «Да» (записать вместе)");
        if (!Fresh("B")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        double e = lineL.Energy;
        double mark = Marker(lineO);
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf); return; }
        NucBaseForm nb = OpenImport(true, RowNew(), RowFor(lineO, nucidO, mark));
        List<string> seen = Answered(new[] { IDYES, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("B", seen);
        Verdict("B", "вопрос о правках редактора нуклидов задан (первым)", Asked(seen), AskedCount(seen) == 1 && seen.Count > 0 && IsQuestion(seen[0]));
        Verdict("B", "импорт выполнен («импорт завершён»)", Done(seen), DoneCount(seen) == 1);
        NuclideDefinitionFile disk = ReadDisk();
        Verdict("B", "в файле: новая линия N", FileHasNew(disk) ? "есть" : "нет", FileHasNew(disk));
        Verdict("B", "в файле: выход O перезаписан импортом", FileIntensity(disk, lineO), FileIntensityIs(disk, lineO, mark));
        Verdict("B", "в файле: линия L с новым именем (правка нуклидов записана)", FileName(disk, e, n0), FileHas(disk, n0 + Suffix, e));
        Verdict("B", "редактор нуклидов «сохранён»", NucDirty(nf), !NucIsDirty(nf));
        List<string> onClose = Answered(new[] { IDNO }, () => nf.Close());
        Questions("B", onClose);
        Verdict("B", "закрытие редактора нуклидов после «Да» — вопроса нет", Count(onClose), onClose.Count == 0);
        disk = ReadDisk();
        Verdict("B", "память = файл: имя L", "память «" + lineL.Name + "», файл " + FileName(disk, e, n0), FileHas(disk, lineL.Name, e));
        Verdict("B", "память = файл: N и выход O", (MemHasNew() ? "N есть" : "N нет") + ", O " + lineO.Intencity.ToString(Inv),
                MemHasNew() == FileHasNew(disk) && FileIntensityIs(disk, lineO, lineO.Intencity));
        CloseQuiet(nb);
        CloseAll(nf);
    }

    // ---- C: «Нет»
    static void ScenarioNo()
    {
        Section("C. то же → «Нет» (только импорт) → «Нет» редактора нуклидов");
        if (!Fresh("C")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        double e = lineL.Energy;
        double mark = Marker(lineO);
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf); return; }
        NucBaseForm nb = OpenImport(true, RowNew(), RowFor(lineO, nucidO, mark));
        List<string> seen = Answered(new[] { IDNO, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("C", seen);
        Verdict("C", "вопрос о правках редактора нуклидов задан", Asked(seen), AskedCount(seen) == 1);
        Verdict("C", "импорт выполнен («импорт завершён»)", Done(seen), DoneCount(seen) == 1);
        NuclideDefinitionFile disk = ReadDisk();
        Verdict("C", "в файле: новая линия N", FileHasNew(disk) ? "есть" : "нет", FileHasNew(disk));
        Verdict("C", "в файле: выход O перезаписан импортом", FileIntensity(disk, lineO), FileIntensityIs(disk, lineO, mark));
        Verdict("C", "в файле: имя L прежнее (правка нуклидов НЕ записана)", FileName(disk, e, n0), FileHas(disk, n0, e) && !FileHas(disk, n0 + Suffix, e));
        Verdict("C", "память: правка имени L цела", "«" + lineL.Name + "»", lineL.Name == n0 + Suffix);
        Verdict("C", "редактор нуклидов по-прежнему «изменено»", NucDirty(nf), NucIsDirty(nf));
        CloseQuiet(nb);
        List<string> onClose = Answered(new[] { IDNO }, () => nf.Close());
        Questions("C", onClose);
        disk = ReadDisk();
        Verdict("C", "после «Нет» редактора нуклидов: имя L в памяти = в файле",
                "память «" + lineL.Name + "», файл " + FileName(disk, e, n0), FileHas(disk, lineL.Name, e));
        Verdict("C", "после «Нет» редактора нуклидов: N цела в памяти и в файле",
                (MemHasNew() ? "память есть" : "память НЕТ") + ", " + (FileHasNew(disk) ? "файл есть" : "файл НЕТ"), MemHasNew() && FileHasNew(disk));
        Verdict("C", "после «Нет» редактора нуклидов: выход O — импорт, память = файл",
                "память " + lineO.Intencity.ToString(Inv) + ", файл " + FileIntensity(disk, lineO), lineO.Intencity == mark && FileIntensityIs(disk, lineO, mark));
        CloseAll(nf);
    }

    // ---- D: правка поля справа + «Да»
    static void ScenarioFieldYes()
    {
        Section("D. правка поля «Интенсивность» L (не перенесена в линию) → импорт N → «Да»");
        if (!Fresh("D")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        if (!Select(nf, lineL)) { CloseAll(nf); return; }
        double i0 = lineL.Intencity;
        double i1 = Math.Round(i0 + 1.25, 4);
        Control box = (Control)Field(nf, "intensityTextBox");
        box.Text = i1.ToString(Inv);
        Pump(20);
        Console.WriteLine("SETUP\tL {0}: поле интенсивности {1} → {2}; в линии {3}; Dirty={4}",
            Line(lineL), i0.ToString(Inv), box.Text, lineL.Intencity.ToString(Inv), lineL.Dirty);
        NucBaseForm nb = OpenImport(false, RowNew());
        List<string> seen = Answered(new[] { IDYES, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("D", seen);
        Verdict("D", "вопрос о правках редактора нуклидов задан", Asked(seen), AskedCount(seen) == 1);
        NuclideDefinitionFile disk = ReadDisk();
        NuclideDefinition f = FileLine(disk, lineL.Name, lineL.Energy);
        Verdict("D", "в файле: интенсивность L из поля", f == null ? "линии нет" : f.Intencity.ToString(Inv), f != null && f.Intencity == i1);
        Verdict("D", "в файле: новая линия N", FileHasNew(disk) ? "есть" : "нет", FileHasNew(disk));
        Verdict("D", "редактор нуклидов «сохранён»", NucDirty(nf), !NucIsDirty(nf));
        CloseQuiet(nb);
        CloseAll(nf);
    }

    // ---- E: «Нет», импорт перезаписывает линию, правленную окном (галка «Видимый»)
    static void ScenarioConflictNo()
    {
        Section("E. галка «Видимый» L снята в окне нуклидов → импорт перезаписывает L (выход) + N → «Нет» → «Нет» окна нуклидов");
        if (!Fresh("E")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        if (!Select(nf, lineL)) { CloseAll(nf); return; }
        string n0 = lineL.Name;
        double e = lineL.Energy;
        bool v0 = lineL.Visible;
        double mark = Marker(lineL);
        CheckBox vis = (CheckBox)Field(nf, "checkBox1");
        vis.Checked = !vis.Checked;
        Pump(20);
        Console.WriteLine("SETUP\tL {0}: Visible {1} → {2} (в памяти, не записана); Dirty={3}", Line(lineL), v0, lineL.Visible, lineL.Dirty);
        if (lineL.Visible == v0)
        {
            Console.WriteLine("⛔ обвязка: галка «Видимый» в линию не легла");
            rig++;
            CloseAll(nf);
            return;
        }
        NucBaseForm nb = OpenImport(true, RowNew(), RowFor(lineL, nucidL, mark));
        List<string> seen = Answered(new[] { IDNO, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("E", seen);
        Verdict("E", "вопрос о правках редактора нуклидов задан", Asked(seen), AskedCount(seen) == 1);
        NuclideDefinitionFile disk = ReadDisk();
        NuclideDefinition fl = FileLineByKey(disk, lineL);
        Verdict("E", "в файле: выход L — импорт", fl == null ? "линии нет" : fl.Intencity.ToString(Inv), fl != null && fl.Intencity == mark);
        Verdict("E", "в файле: галка L до правки окна (правка окна НЕ записана)", fl == null ? "линии нет" : "Visible=" + fl.Visible, fl != null && fl.Visible == v0);
        Verdict("E", "редактор нуклидов по-прежнему «изменено»", NucDirty(nf), NucIsDirty(nf));
        CloseQuiet(nb);
        List<string> onClose = Answered(new[] { IDNO }, () => nf.Close());
        Questions("E", onClose);
        disk = ReadDisk();
        fl = FileLineByKey(disk, lineL);
        Verdict("E", "после «Нет» окна нуклидов: галка L память = файл",
                "память Visible=" + lineL.Visible + ", файл " + (fl == null ? "линии нет" : "Visible=" + fl.Visible), fl != null && fl.Visible == lineL.Visible);
        Verdict("E", "после «Нет» окна нуклидов: выход L — импорт, память = файл (импортированное не тронуто)",
                "память " + lineL.Intencity.ToString(Inv) + ", файл " + (fl == null ? "линии нет" : fl.Intencity.ToString(Inv)),
                lineL.Intencity == mark && fl != null && fl.Intencity == mark);
        Verdict("E", "после «Нет» окна нуклидов: имя L память = файл",
                "память «" + lineL.Name + "», файл " + (fl == null ? "линии нет" : "«" + fl.Name + "»"), fl != null && fl.Name == lineL.Name);
        CloseAll(nf);
    }

    // ---- R: «Нет», окно переименовало линию, импорт (без «перезаписать») несёт её же
    static void ScenarioRenameNo()
    {
        Section("R. имя L переименовано в окне нуклидов → импорт строки L (без «перезаписать») + N → «Нет» → «Нет» окна нуклидов");
        if (!Fresh("R")) return;
        NuclideDefinitionForm nf = OpenNuclides();
        string n0 = lineL.Name;
        double e = lineL.Energy;
        if (!EditName(nf, lineL, Suffix)) { CloseAll(nf); return; }
        string idEdited = NucBaseForm.LineIdentity(lineL.NuclideName, lineL.IsElementXray);
        Console.WriteLine("SETUP\tключ импорта L после переименования: {0} (до — N:{1})", idEdited, nucidL);
        if (idEdited == "N:" + nucidL)
        {
            Console.WriteLine("⛔ обвязка: переименование не сменило ключ импорта — сцена R ничего не мерит");
            rig++;
            CloseAll(nf);
            return;
        }
        // Линий на энергии L в библиотеке до импорта (вместе с L) — по чистой копии на диске.
        int atE0 = ReadDisk().NuclideDefinitions.Count(d => d.Energy == e);
        NucBaseForm nb = OpenImport(false, RowNew(), RowFor(lineL, nucidL, lineL.Intencity));
        List<string> seen = Answered(new[] { IDNO, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("R", seen);
        Verdict("R", "вопрос о правках редактора нуклидов задан", Asked(seen), AskedCount(seen) == 1);
        NuclideDefinitionFile disk = ReadDisk();
        int inFile = disk.NuclideDefinitions.Count(d => d.Energy == e);
        Verdict("R", "в файле: линий на энергии L столько же, сколько было (второй копии L не заведено)",
                inFile.ToString(Inv) + " (было " + atE0.ToString(Inv) + "), " + FileName(disk, e, ""), inFile == atE0);
        Verdict("R", "в файле: новая линия N", FileHasNew(disk) ? "есть" : "нет", FileHasNew(disk));
        CloseQuiet(nb);
        List<string> onClose = Answered(new[] { IDNO }, () => nf.Close());
        Questions("R", onClose);
        disk = ReadDisk();
        int mem = mgr.NuclideDefinitions.Count(d => d.Energy == e);
        inFile = disk.NuclideDefinitions.Count(d => d.Energy == e);
        Verdict("R", "после «Нет» окна нуклидов: линий на энергии L память = файл = было",
                "память " + mem.ToString(Inv) + ", файл " + inFile.ToString(Inv) + ", было " + atE0.ToString(Inv), mem == atE0 && inFile == atE0);
        Verdict("R", "после «Нет» окна нуклидов: имя L память = файл", "память «" + lineL.Name + "», файл " + FileName(disk, e, n0),
                FileHas(disk, lineL.Name, e));
        CloseAll(nf);
    }

    // ---- K: контроль
    static void Controls()
    {
        Section("K. контроль: редактора нуклидов нет / открыт без правок — вопроса нет, импорт как прежде");
        if (!Fresh("K1")) return;
        {
            double mark = Marker(lineO);
            NucBaseForm nb = OpenImport(true, RowNew(), RowFor(lineO, nucidO, mark));
            string sha0 = Sha(work);
            List<string> seen = Answered(new[] { IDOK }, () => Press(nb, "buttonImportDef"));
            Questions("K1", seen);
            Verdict("K1", "редактора нуклидов нет: вопроса нет", Asked(seen), AskedCount(seen) == 0);
            Verdict("K1", "контроль обвязки: «импорт завершён» автоответчику виден", Done(seen), DoneCount(seen) == 1);
            NuclideDefinitionFile disk = ReadDisk();
            Verdict("K1", "файл записан: N и выход O", (Sha(work) != sha0 ? "записан, " : "НЕ записан, ")
                    + (FileHasNew(disk) ? "N есть, " : "N нет, ") + "O " + FileIntensity(disk, lineO),
                    Sha(work) != sha0 && FileHasNew(disk) && FileIntensityIs(disk, lineO, mark));
            Verdict("K1", "память = файл: N и выход O", (MemHasNew() ? "N есть" : "N нет") + ", O " + lineO.Intencity.ToString(Inv),
                    MemHasNew() && lineO.Intencity == mark);
            CloseQuiet(nb);
        }
        if (!Fresh("K2")) return;
        {
            NuclideDefinitionForm nf = OpenNuclides();
            Select(nf, lineL);
            NucBaseForm nb = OpenImport(false, RowNew());
            string sha0 = Sha(work);
            List<string> seen = Answered(new[] { IDOK }, () => Press(nb, "buttonImportDef"));
            Questions("K2", seen);
            Verdict("K2", "редактор нуклидов открыт без правок: вопроса нет", Asked(seen), AskedCount(seen) == 0);
            NuclideDefinitionFile disk = ReadDisk();
            Verdict("K2", "файл записан, N в нём", (Sha(work) != sha0 ? "записан, " : "НЕ записан, ") + (FileHasNew(disk) ? "N есть" : "N нет"),
                    Sha(work) != sha0 && FileHasNew(disk));
            CloseQuiet(nb);
            CloseAll(nf);
        }
    }

    // ---- F: отказ записи
    static void ScenarioWriteFails()
    {
        Section("F. (вне поручения) файл библиотеки занят → импорт (N новая, O перезаписать): отказ записи откатывает импорт в памяти");
        if (!Fresh("F")) return;
        double o0 = lineO.Intencity;
        int count0 = mgr.NuclideDefinitions.Count;
        NucBaseForm nb = OpenImport(true, RowNew(), RowFor(lineO, nucidO, Marker(lineO)));
        string sha0 = Sha(work);
        int errBefore = errors.ToString().Length;
        List<string> seen;
        using (new FileStream(work, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            seen = Answered(new[] { IDOK, IDOK }, () => Press(nb, "buttonImportDef"));
        }
        Questions("F", seen);
        string said = errors.ToString().Substring(errBefore).Trim().Replace("\r", " ").Replace("\n", " ");
        Console.WriteLine("F\tотказ записи сказан: {0}", said.Length > 0 ? said : (seen.Count > 0 ? "окном" : "НЕ СКАЗАН"));
        Verdict("F", "контроль: запись действительно не прошла (файл не тронут)", Sha(work) == sha0 ? "не тронут" : "ЗАПИСАН", Sha(work) == sha0);
        Verdict("F", "«импорт завершён» не показан", Done(seen), DoneCount(seen) == 0);
        Verdict("F", "память = файл: новой линии N в памяти нет", MemHasNew() ? "есть" : "нет", !MemHasNew());
        Verdict("F", "память = файл: выход O прежний", lineO.Intencity.ToString(Inv) + " (был " + o0.ToString(Inv) + ")", lineO.Intencity == o0);
        Verdict("F", "линий в памяти столько же", mgr.NuclideDefinitions.Count.ToString(Inv) + " (было " + count0.ToString(Inv) + ")",
                mgr.NuclideDefinitions.Count == count0);
        CloseQuiet(nb);
    }

    // ---- Н: находка — несохранённые правки редактора наборов
    static void FindingSetEditor()
    {
        Section("Н. (находка, дефектом пробы не считается) O2 в набор s0 в редакторе наборов (не сохранено) → импорт N → закрыть редактор наборов «Нет»");
        if (!Fresh("Н")) return;
        if (s0 == null || lineO2 == null)
        {
            Console.WriteLine("Н\tнет набора или линии вне него — сцена пропущена");
            return;
        }
        NuclideSetForm sf = new NuclideSetForm(null);
        ShowOff(sf);
        SetField(sf, "selectedSet", s0);
        Call(sf, "UpdateNuclideDefinition", lineO2, true);
        NucBaseForm nb = OpenImport(false, RowNew());
        List<string> seen = Answered(new[] { IDOK, IDOK }, () => Press(nb, "buttonImportDef"));
        Questions("Н", seen);
        NuclideDefinitionFile disk = ReadDisk();
        Finding("Н", "вопрос о правках редактора наборов при импорте", Asked(seen) + " (о правках нуклидов)");
        Finding("Н", "в файле: O2 в наборе s0 — правка редактора наборов записана импортом", InSet(disk, lineO2));
        Finding("Н", "редактор наборов после импорта", "dirty=" + Field(sf, "dirty"));
        List<string> onClose = Answered(new[] { IDNO }, () => sf.Close());
        Questions("Н", onClose);
        disk = ReadDisk();
        bool mem = lineO2.Sets.Contains(s0.Id);
        Finding("Н", "после «Нет» редактора наборов: O2 в s0 — память / файл",
                (mem ? "в наборе" : "вне набора") + " / " + InSet(disk, lineO2) + (mem == InSetBool(disk, lineO2) ? " (сходятся)" : " (РАСХОДЯТСЯ)"));
        CloseQuiet(nb);
        CloseQuiet(sf);
    }

    // ================================================================ помощники сцены
    static NuclideDefinitionForm OpenNuclides()
    {
        NuclideDefinitionForm f = new NuclideDefinitionForm();
        ShowOff(f);
        return f;
    }

    /// <summary>Окно NucBase с готовыми строками поиска (все помечены), «перезаписать» — по ключу.</summary>
    static NucBaseForm OpenImport(bool overwrite, params DecayRad[] rows)
    {
        NucBaseForm f = new NucBaseForm();
        ShowOff(f);
        foreach (DecayRad r in rows)
        {
            Call(f, "AddRow", r);
        }
        Call(f, "UpdateNuclideDefinitionControlsState");
        ((CheckBox)Field(f, "checkBoxOverwriteDef")).Checked = overwrite;
        DataGridView grid = (DataGridView)Field(f, "ResultDataGridView");
        int marked = grid.Rows.Cast<DataGridViewRow>().Count(r => Equals(r.Cells[0].Value, true));
        if (marked != rows.Length)
        {
            Console.WriteLine("⛔ обвязка: помечено строк импорта {0} из {1}", marked, rows.Length);
            rig++;
        }
        return f;
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

    /// <summary>Правка имени в таблице настоящим редактором XPTable (как P318, P326).</summary>
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

    /// <summary>Закрыть окно нуклидов сцены, на любые вопросы — «Нет».</summary>
    static void CloseAll(NuclideDefinitionForm nf)
    {
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

    // ================================================================ вопросы
    static bool IsQuestion(string text)
    {
        return text.Contains("Редактировать изотоп") || text.Contains("Edit Nuclide Definition");
    }

    static bool IsDone(string text)
    {
        return text.Contains("Импорт завершен") || text.Contains("Import successful");
    }

    static int AskedCount(List<string> seen)
    {
        return seen.Count(IsQuestion);
    }

    static int DoneCount(List<string> seen)
    {
        return seen.Count(IsDone);
    }

    static string Asked(List<string> seen)
    {
        return "вопросов о правках " + AskedCount(seen).ToString(Inv) + " (окон всего " + seen.Count.ToString(Inv) + ")";
    }

    static string Done(List<string> seen)
    {
        return "«импорт завершён» " + DoneCount(seen).ToString(Inv);
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

    static bool IsNew(NuclideDefinition d)
    {
        return d.Energy == NewEnergy && NucBaseForm.LineIdentity(d.NuclideName, d.IsElementXray) == "N:" + NewNucid;
    }

    static bool MemHasNew()
    {
        return mgr.NuclideDefinitions.Any(IsNew);
    }

    static bool FileHasNew(NuclideDefinitionFile f)
    {
        return f.NuclideDefinitions.Any(IsNew);
    }

    static NuclideDefinition FileLine(NuclideDefinitionFile f, string name, double energy)
    {
        return f.NuclideDefinitions.FirstOrDefault(d => d.Name == name && d.Energy == energy);
    }

    /// <summary>Линия файла по ключу импорта (нуклид + энергия) — имя импорт может переписать.</summary>
    static NuclideDefinition FileLineByKey(NuclideDefinitionFile f, NuclideDefinition mem)
    {
        string key = NucBaseForm.LineIdentity(NuclideDefinition.NuclideNameOf(mem.Name), mem.IsElementXray);
        return f.NuclideDefinitions.FirstOrDefault(d => d.Energy == mem.Energy && NucBaseForm.LineIdentity(d.NuclideName, d.IsElementXray) == key);
    }

    static string FileIntensity(NuclideDefinitionFile f, NuclideDefinition mem)
    {
        NuclideDefinition d = FileLineByKey(f, mem);
        return d == null ? "линии нет" : d.Intencity.ToString(Inv);
    }

    static bool FileIntensityIs(NuclideDefinitionFile f, NuclideDefinition mem, double value)
    {
        NuclideDefinition d = FileLineByKey(f, mem);
        return d != null && d.Intencity == value;
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

    static bool InSetBool(NuclideDefinitionFile f, NuclideDefinition mem)
    {
        NuclideDefinition d = FileLine(f, mem.Name, mem.Energy);
        return d != null && d.Sets.Contains(s0.Id);
    }

    static string InSet(NuclideDefinitionFile f, NuclideDefinition mem)
    {
        NuclideDefinition d = FileLine(f, mem.Name, mem.Energy);
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
        Console.WriteLine("{0}\tокна: {1}", step, seen.Count == 0 ? "нет" : string.Join(" || ", seen));
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

    static void Finding(string id, string what, string got)
    {
        Console.WriteLine("{0}\t{1}\t{2}\tНАХОДКА", id, what, got ?? "(null)");
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
    /// Отвечает на окна приложения (класс #32770 своего процесса): k-му новому окну — k-й ответ
    /// списка (WM_COMMAND), повторяя его, пока окно не уйдёт; после четырёх безуспешных —
    /// «Нет», «ОК», «Отмена» по кругу (чтобы проба не повисла). Записывает тексты. Окно считается
    /// новым по паре «дескриптор + текст»: дескриптор ушедшего окна Windows может отдать следующему.
    /// (Тот же, что в P326SetSaveAsksProbe.)
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
