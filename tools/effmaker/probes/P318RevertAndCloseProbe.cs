// Проба полосы П318 (15.10.2026): две находки П316, снятые чтением, — ДО и ПОСЛЕ правки.
// Журнал — handover/handover-2026-10-15-p318-revert-and-close.md; находки —
// handover/handover-2026-10-15-p316-screen-pass-fixes.md («Находки вне своих файлов»).
//
//   N. Редактор нуклидов (`NuclideDefinitionForm`), ответ «Нет» на «Сохранить?»: откат правок
//      прежде шёл ПЕРЕЧИТЫВАНИЕМ файла библиотеки, то есть подменой ВСЕХ объектов менеджера —
//      а выбор набора у открытых спектров (`DocEnergySpectrum.SelectedNuclideSet`) держится по
//      ссылке. Мерится цепочка приложения целиком: два документа с выбранными наборами, панель
//      поиска пиков (`DCPeakDetectionView`), окно редактора, правка, «Нет», обновление панели.
//   D. Конфигурация прибора (`DeviceConfigForm`): «Закрыть» и крестик при нечисле в поле;
//      «Сохранить» и «Закрыть → Да» нечисла не пропускают — с П320 (решения Amber 15.10.2026
//      «Вернуть прежнее (Рекомендую)» и «Оставить (Рекомендую)») поле молча возвращает
//      прежнее число, и запись идёт им, без сообщения о поле (П325 перемерила ожидания: прежде
//      здесь судилось «запись отказывает, поле названо»). Запись идёт во ВРЕМЕННУЮ копию
//      config\device (путь `DeviceConfigManager` подменён на весь раздел D), и по ней же
//      сверяется записанное число.
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут пути от
// каталога входной сборки). Проба зовёт приложение тем, что есть И ДО, И ПОСЛЕ правки (поля
// форм отражением, щелчок — фокус на кнопку и PerformClick, выбор строки — модель таблицы), поэтому один exe
// работает на обеих сборках: сборка ДО правки — положительный контроль (обязана показать
// дефекты).
//
//   P318RevertAndCloseProbe.exe [--only=nuclides|device]
//
// Окна строятся вне экрана (−4000, −4000); настоящие окна — только вопросы приложения
// (MessageBox), на них отвечает поток-автоответчик. Каталог config\ пробы и живой конфиг
// %AppData%\BecqMoni\config сверяются sha256 до и после: проба обязана ничего не записать
// (записи раздела D уходят во временную копию, см. выше).
//
// Строка вывода — «<шаг>\tчто мерено\tзначение\tЧИСТО|ДЕФЕКТ». Строки «НАХОДКА» — замеры
// вне поручения, в код возврата не входят.
// Коды: 0 — дефектов нет; 4 — есть дефекты (на сборке ДО — ожидаемо); 5 — обвязка; 2 — ключи.
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using BecquerelMonitor;
using XPTable.Models;

static class P318RevertAndCloseProbe
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly CultureInfo Ru = new CultureInfo("ru-RU");
    static int defects, rig, findings;
    static StringWriter errors = new StringWriter();

    // Цепочка «документ → панель» для раздела N.
    static MainForm mainForm;
    static DCPeakDetectionView panel;
    static ComboBox combo;
    static DocEnergySpectrum docA, docB;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string only = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
            else { Console.WriteLine("неизвестный ключ: " + a); return 2; }
        }
        // Исключение в обработчике окна — вызывающему, а не в окно «Необработанное
        // исключение» (его автоответчик не знает, и проба повисла бы).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Console.WriteLine(ProbeTargetFramework.Describe());
        string exe = typeof(MainForm).Assembly.Location;
        Console.WriteLine("приложение: " + exe + "  ("
            + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Thread.CurrentThread.CurrentCulture = Inv;
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
            NuclideDefinitionManager.GetInstance();
            if (only == null || only == "nuclides")
            {
                Section("N. редактор нуклидов: «Нет» на «Сохранить?»");
                Nuclides();
            }
            if (only == null || only == "device")
            {
                Section("D. конфигурация прибора: «Закрыть» при нечисле в поле");
                // Реестры приборов в приложении заводит MainForm; без них конструктор окна
                // падает на `DeviceType.DeviceTypeList` (как в DeviceTabProbe, P316).
                DeviceType.InitializeDeviceTypes();
                ThermometerType.InitializeThermometerTypes();
                Device();
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
        Dictionary<string, string> cfgAfter = HashTree(cfgDir);
        List<string> changed = Diff(cfgBefore, cfgAfter);
        Verdict("W", "config пробы не тронут (sha256 до = после, " + cfgBefore.Count.ToString(Inv) + " файлов)",
                changed.Count == 0 ? "совпал" : "изменены: " + string.Join(", ", changed), changed.Count == 0);
        Dictionary<string, string> amberAfter = HashTree(amberDir);
        List<string> amberChanged = Diff(amberBefore, amberAfter);
        if (amberChanged.Count > 0)
        {
            Console.WriteLine("⛔ обвязка: ЖИВОЙ конфиг изменился: " + string.Join(", ", amberChanged));
            rig++;
        }
        else
        {
            Console.WriteLine("W\tживой конфиг %AppData%\\BecqMoni\\config не тронут\t{0} файлов совпали\tЧИСТО", amberAfter.Count);
        }

        string err = errors.ToString();
        if (err.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("--- поток ошибок приложения (AppUi без окон) ---");
            Console.WriteLine(err.TrimEnd());
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ ПРОБЫ: дефектов {0}, находок вне поручения {1}, отказов обвязки {2}", defects, findings, rig);
        if (rig > 0) return 5;
        return defects == 0 ? 0 : 4;
    }

    // ================================================================== N
    static void Nuclides()
    {
        Thread.CurrentThread.CurrentUICulture = Ru;
        NuclideDefinitionManager mgr = NuclideDefinitionManager.GetInstance();
        string file = Package.GetInstance().NuclideDefinition;
        string sha0 = Sha(file);
        if (mgr.NuclideSets.Count < 2)
        {
            Console.WriteLine("⛔ обвязка: в библиотеке каталога проб меньше двух наборов ({0})", mgr.NuclideSets.Count);
            rig++;
            return;
        }

        mainForm = new MainForm();
        panel = new DCPeakDetectionView(mainForm);
        combo = (ComboBox)Field(panel, "comboBoxNuclSet");
        docA = new DocEnergySpectrum();
        docB = new DocEnergySpectrum();
        if (!ResetDocs("SETUP")) return;

        // ---- N1: правка ячейки (имя) и «Нет» при переходе на другую строку
        Step("N1", "правка имени в таблице → щелчок по другой строке → «Сохранить?» «Нет»");
        {
            List<NuclideSet> setsBefore = mgr.NuclideSets.ToList();
            List<NuclideDefinition> defsBefore = mgr.NuclideDefinitions.ToList();
            NuclideDefinitionForm f = new NuclideDefinitionForm();
            ShowOff(f);
            NuclideDefinition line = EditName(f, 0, "-П318");
            if (line == null) { f.Dispose(); return; }
            string name0 = line.Name.Substring(0, line.Name.Length - "-П318".Length);
            double e0 = line.Energy;
            int other = OtherRow(f, line);
            NuclideDefinition target = (NuclideDefinition)Model(f).Rows[other].Tag;
            string targetName = target.Name;
            double targetEnergy = target.Energy;
            List<string> seen = Answered(new[] { IDNO, IDCANCEL, IDOK }, () => Model(f).Selections.SelectCell(other, 0));
            Console.WriteLine("SETUP\tвопросы: {0}", seen.Count == 0 ? "нет" : string.Join(" || ", seen));
            if (seen.Count == 0)
            {
                Console.WriteLine("⛔ обвязка: вопроса «Сохранить?» не было — «Нет» не мерено");
                rig++;
            }
            JudgeRevert("N1", mgr, name0, e0, name0 + "-П318", setsBefore, defsBefore);
            NuclideDefinition active = (NuclideDefinition)Field(f, "activeNuclide");
            Verdict("N1", "активная строка окна — объект менеджера, та, по которой щёлкнули",
                    active == null ? "null" : active.Name + " " + active.Energy.ToString(Inv)
                        + (mgr.NuclideDefinitions.Any(d => ReferenceEquals(d, active)) ? " (в менеджере)" : " (ВНЕ менеджера)"),
                    active != null && mgr.NuclideDefinitions.Any(d => ReferenceEquals(d, active))
                        && active.Name == targetName && active.Energy == targetEnergy);
            JudgeDocs("N1");
            Verdict("N1", "«Нет» не пишет библиотеку (sha256)", Short(Sha(file)), Sha(file) == sha0);
            CloseQuietly(f);
        }

        // ---- N2: галка «Видимый» и «Нет» при закрытии окна
        Step("N2", "галка «Видимый» → закрыть окно → «Сохранить?» «Нет»");
        {
            ResetDocs("N2");
            List<NuclideSet> setsBefore = mgr.NuclideSets.ToList();
            List<NuclideDefinition> defsBefore = mgr.NuclideDefinitions.ToList();
            NuclideDefinitionForm f = new NuclideDefinitionForm();
            ShowOff(f);
            int row = FirstRow(f, d => !string.IsNullOrEmpty(d.Name), 3);
            NuclideDefinition line = (NuclideDefinition)Model(f).Rows[row].Tag;
            Model(f).Selections.SelectCell(row, 0);
            Pump(30);
            if (!ReferenceEquals(Field(f, "activeNuclide"), line))
            {
                Console.WriteLine("⛔ обвязка: строка не стала активной");
                rig++;
                CloseQuietly(f);
                return;
            }
            bool visible0 = line.Visible;
            CheckBox box = (CheckBox)Field(f, "checkBox1");
            box.Checked = !box.Checked;
            Pump(20);
            if (line.Visible == visible0)
            {
                Console.WriteLine("⛔ обвязка: галка «Видимый» в определение не легла");
                rig++;
            }
            List<string> seen = Answered(new[] { IDNO, IDCANCEL, IDOK }, () => f.Close());
            Console.WriteLine("SETUP\t{0} {1}: Visible {2} → {3}; вопросы: {4}", line.Name, line.Energy.ToString(Inv), visible0, !visible0,
                              seen.Count == 0 ? "нет" : string.Join(" || ", seen));
            if (seen.Count == 0)
            {
                Console.WriteLine("⛔ обвязка: вопроса «Сохранить?» не было");
                rig++;
            }
            NuclideDefinition now = Find(mgr, line.Name, line.Energy);
            Verdict("N2", "«Нет» вернул галку «Видимый»", now == null ? "линии нет" : "Visible=" + now.Visible, now != null && now.Visible == visible0);
            Verdict("N2", "окно закрылось", "IsDisposed=" + f.IsDisposed, f.IsDisposed);
            Verdict("N2", "наборы — те же объекты", SameObjects(mgr.NuclideSets, setsBefore), mgr.NuclideSets.SequenceEqual(setsBefore));
            JudgeDocs("N2");
            if (now != null) now.Visible = visible0;
            if (!f.IsDisposed) f.Dispose();
        }

        // ---- N3: открытый рядом редактор наборов с несохранённой правкой
        Step("N3", "редактор наборов открыт, правка не сохранена → в редакторе нуклидов правка + «Нет»"
             + " (в приложении редактор наборов МОДАЛЕН — состояние недостижимо руками, мерено для полноты)");
        {
            ResetDocs("N3");
            NuclideSet s0 = mgr.NuclideSets[0];
            int setsCount = mgr.NuclideSets.Count;
            NuclideDefinition outsider = mgr.NuclideDefinitions.First(d => !d.Sets.Contains(s0.Id) && !string.IsNullOrEmpty(d.Name));
            string oName = outsider.Name;
            double oEnergy = outsider.Energy;
            NuclideSetForm sf = new NuclideSetForm((MainForm)null);
            ShowOff(sf);
            SetField(sf, "selectedSet", s0);
            Call(sf, "UpdateNuclideDefinition", outsider, true);
            NuclideDefinitionForm f = new NuclideDefinitionForm();
            ShowOff(f);
            NuclideDefinition line = EditName(f, 5, "-П318", d => !ReferenceEquals(d, outsider));
            if (line != null)
            {
                int other = OtherRow(f, line, d => !ReferenceEquals(d, outsider));
                List<string> seen = Answered(new[] { IDNO, IDCANCEL, IDOK }, () => Model(f).Selections.SelectCell(other, 0));
                if (seen.Count == 0)
                {
                    Console.WriteLine("⛔ обвязка: вопроса «Сохранить?» не было");
                    rig++;
                }
                NuclideDefinition o = Find(mgr, oName, oEnergy);
                Verdict("N3", "правка редактора наборов (линия " + oName + " в «" + s0.Name + "») пережила «Нет» соседа",
                        o == null ? "линии нет" : (o.Sets.Contains(s0.Id) ? "в наборе" : "НЕ в наборе")
                            + (ReferenceEquals(o, outsider) ? ", тот же объект" : ", ДРУГОЙ объект"),
                        o != null && o.Sets.Contains(s0.Id) && ReferenceEquals(o, outsider));
            }
            CloseQuietly(f);
            // Закрыть редактор наборов «Нет» — его снимок снимает его правку.
            Answered(new[] { IDNO, IDCANCEL, IDOK }, () => sf.Close());
            NuclideDefinition after = Find(mgr, oName, oEnergy);
            Verdict("N3", "после «Нет» редактора наборов: линия вне набора, наборов столько же",
                    (after == null ? "линии нет" : (after.Sets.Contains(s0.Id) ? "в наборе" : "вне набора"))
                        + ", наборов " + mgr.NuclideSets.Count.ToString(Inv) + " из " + setsCount.ToString(Inv),
                    after != null && !after.Sets.Contains(s0.Id) && mgr.NuclideSets.Count == setsCount);
            if (!sf.IsDisposed) sf.Dispose();
            Verdict("N3", "«Нет» не пишет библиотеку (sha256)", Short(Sha(file)), Sha(file) == sha0);
        }

        // ---- N4: «Нет» при занятом файле библиотеки (последним: прежний код ронял менеджер)
        Step("N4", "правка имени → файл библиотеки занят другим процессом → «Нет»");
        {
            ResetDocs("N4");
            int defsCount = mgr.NuclideDefinitions.Count;
            NuclideDefinitionForm f = new NuclideDefinitionForm();
            ShowOff(f);
            NuclideDefinition line = EditName(f, 7, "-П318");
            if (line != null)
            {
                string name0 = line.Name.Substring(0, line.Name.Length - "-П318".Length);
                double e0 = line.Energy;
                int other = OtherRow(f, line);
                string thrown = null;
                int errBefore = errors.ToString().Length;
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Answered(new[] { IDNO, IDCANCEL, IDOK }, () =>
                    {
                        try { Model(f).Selections.SelectCell(other, 0); }
                        catch (Exception ex) { thrown = Reason(ex); }
                    });
                }
                string said = errors.ToString().Substring(errBefore).Trim().Replace("\r", " ").Replace("\n", " ");
                Verdict("N4", "«Нет» при занятом файле — без исключения", thrown == null ? "без исключения" : "ИСКЛЮЧЕНИЕ: " + thrown, thrown == null);
                bool alive = mgr.NuclideDefinitionFile != null;
                Verdict("N4", "библиотека в памяти цела", alive ? "линий " + mgr.NuclideDefinitions.Count.ToString(Inv) + " из " + defsCount.ToString(Inv)
                                                                 : "NuclideDefinitionFile = null", alive && mgr.NuclideDefinitions.Count == defsCount);
                if (alive)
                {
                    Verdict("N4", "«Нет» вернул имя", Find(mgr, name0, e0) != null ? "вернул" : "НЕ вернул",
                            Find(mgr, name0, e0) != null && Find(mgr, name0 + "-П318", e0) == null);
                }
                Verdict("N4", "сообщений об отказе чтения нет (читать нечего)", said.Length == 0 ? "—" : said, said.Length == 0);
                if (!alive)
                {
                    mgr.LoadDefinitionFile(forceReload: true);
                }
            }
            CloseQuietly(f);
            Verdict("N4", "«Нет» не пишет библиотеку (sha256)", Short(Sha(file)), Sha(file) == sha0);
        }

        foreach (Form form in new Form[] { docB, docA, panel, mainForm })
        {
            form.Dispose();
        }
    }

    /// <summary>
    /// Документ A — первый набор библиотеки, документ B — второй; активен A. Выбор — щелчком
    /// по списку панели, как человек.
    /// </summary>
    static bool ResetDocs(string when)
    {
        NuclideDefinitionManager mgr = NuclideDefinitionManager.GetInstance();
        NuclideSet s0 = mgr.NuclideSets[0], s1 = mgr.NuclideSets[1];
        Choose(docA, s0);
        Choose(docB, s1);
        Activate(docA);
        bool ok = ReferenceEquals(docA.SelectedNuclideSet, s0) && ReferenceEquals(docB.SelectedNuclideSet, s1)
                  && ReferenceEquals(mgr.ActiveSet, s0);
        Console.WriteLine("{0}\tдокумент A — «{1}», документ B — «{2}», панель — «{3}»{4}", when,
                          SetName(docA.SelectedNuclideSet), SetName(docB.SelectedNuclideSet), SetName(mgr.ActiveSet), ok ? "" : "  ⛔ не сошлось");
        if (!ok) rig++;
        return ok;
    }

    static void Choose(DocEnergySpectrum doc, NuclideSet set)
    {
        Activate(doc);
        combo.SelectedIndex = NuclideDefinitionManager.GetInstance().NuclideSets.IndexOf(set) + 1;
        Pump(10);
    }

    /// <summary>То, что приложение делает при смене документа и по таймеру панели.</summary>
    static void Activate(DocEnergySpectrum doc)
    {
        mainForm.ActiveDocument = doc;
        panel.ShowPeakDetectionResult();
        Pump(10);
    }

    /// <summary>
    /// Выбор наборов у документов после обновления панели — как после «Нет» его увидит человек:
    /// активный документ, затем переход на соседний и обратно.
    /// </summary>
    static void JudgeDocs(string step)
    {
        NuclideDefinitionManager mgr = NuclideDefinitionManager.GetInstance();
        NuclideSet s0 = mgr.NuclideSets[0], s1 = mgr.NuclideSets[1];
        Activate(docA);
        Verdict(step, "документ A после обновления панели: набор «" + s0.Name + "»",
                SetName(docA.SelectedNuclideSet) + ", список «" + (string)combo.SelectedItem + "»",
                docA.SelectedNuclideSet != null && docA.SelectedNuclideSet.Id == s0.Id && combo.SelectedIndex == mgr.NuclideSets.IndexOf(docA.SelectedNuclideSet) + 1);
        Activate(docB);
        Verdict(step, "документ B (был в фоне): набор «" + s1.Name + "»", SetName(docB.SelectedNuclideSet),
                docB.SelectedNuclideSet != null && docB.SelectedNuclideSet.Id == s1.Id);
        Activate(docA);
    }

    static void JudgeRevert(string step, NuclideDefinitionManager mgr, string name0, double e0, string edited,
                            List<NuclideSet> setsBefore, List<NuclideDefinition> defsBefore)
    {
        bool back = Find(mgr, name0, e0) != null && Find(mgr, edited, e0) == null;
        Verdict(step, "«Нет» вернул имя линии", back ? "«" + name0 + "»" : "НЕ вернул", back);
        Verdict(step, "наборы — те же объекты", SameObjects(mgr.NuclideSets, setsBefore), mgr.NuclideSets.SequenceEqual(setsBefore));
        int same = mgr.NuclideDefinitions.Count(d => defsBefore.Any(b => ReferenceEquals(b, d)));
        Verdict(step, "линии — те же объекты", same.ToString(Inv) + " из " + mgr.NuclideDefinitions.Count.ToString(Inv),
                same == mgr.NuclideDefinitions.Count && same == defsBefore.Count);
        Console.WriteLine("{0}\tвыбор панели (ActiveSet) до её обновления: «{1}», объект {2}", step, SetName(mgr.ActiveSet),
                          mgr.NuclideSets.Any(s => ReferenceEquals(s, mgr.ActiveSet)) ? "в менеджере" : "ВНЕ менеджера");
    }

    /// <summary>
    /// Выбрать строку (n-я подходящая) и править имя в таблице тем путём, каким закрывается
    /// правка ячейки. Возвращает линию или null (обвязка).
    /// </summary>
    static NuclideDefinition EditName(NuclideDefinitionForm f, int skip, string suffix, Func<NuclideDefinition, bool> also = null)
    {
        int row = FirstRow(f, d => !string.IsNullOrEmpty(d.Name) && (also == null || also(d)), skip);
        TableModel m = Model(f);
        NuclideDefinition line = (NuclideDefinition)m.Rows[row].Tag;
        m.Selections.SelectCell(row, 0);
        Pump(30);
        if (!ReferenceEquals(Field(f, "activeNuclide"), line))
        {
            Console.WriteLine("⛔ обвязка: строка не стала активной");
            rig++;
            return null;
        }
        string name0 = line.Name;
        Table t = (Table)Field(f, "table1");
        // Правка ячейки — настоящим редактором XPTable (как `NucBaseCardProbeP275`):
        // открыть, вписать, закрыть; событие закрытия правки форма получает сама.
        t.EditCell(row, 0);
        if (!t.IsEditing)
        {
            Console.WriteLine("⛔ обвязка: ячейка имени не открылась на правку");
            rig++;
            return null;
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
            Console.WriteLine("⛔ обвязка: правка имени в определение не легла ({0}, Dirty={1})", line.Name, line.Dirty);
            rig++;
            return null;
        }
        Console.WriteLine("SETUP\tправка: «{0}» {1} → «{2}»", name0, line.Energy.ToString(Inv), line.Name);
        return line;
    }

    static int FirstRow(NuclideDefinitionForm f, Func<NuclideDefinition, bool> ok, int skip)
    {
        TableModel m = Model(f);
        int seen = 0;
        for (int i = 0; i < m.Rows.Count; i++)
        {
            NuclideDefinition d = m.Rows[i].Tag as NuclideDefinition;
            if (d != null && ok(d) && seen++ >= skip) return i;
        }
        throw new InvalidOperationException("в таблице нет подходящей строки");
    }

    /// <summary>Строка далеко от правленой и с другим именем — «другая строка» для вопроса.</summary>
    static int OtherRow(NuclideDefinitionForm f, NuclideDefinition line, Func<NuclideDefinition, bool> also = null)
    {
        TableModel m = Model(f);
        for (int i = m.Rows.Count - 1; i >= 0; i--)
        {
            NuclideDefinition d = m.Rows[i].Tag as NuclideDefinition;
            if (d != null && !ReferenceEquals(d, line) && !string.IsNullOrEmpty(d.Name) && d.Name != line.Name
                && (also == null || also(d)))
            {
                return i;
            }
        }
        throw new InvalidOperationException("нет другой строки");
    }

    static TableModel Model(NuclideDefinitionForm f)
    {
        return (TableModel)Field(f, "tableModel1");
    }

    static NuclideDefinition Find(NuclideDefinitionManager mgr, string name, double energy)
    {
        return mgr.NuclideDefinitions.FirstOrDefault(d => d.Name == name && d.Energy == energy);
    }

    static string SameObjects(List<NuclideSet> now, List<NuclideSet> before)
    {
        int same = now.Count(s => before.Any(b => ReferenceEquals(b, s)));
        return same.ToString(Inv) + " из " + now.Count.ToString(Inv);
    }

    static void CloseQuietly(Form f)
    {
        if (f.IsDisposed) return;
        Answered(new[] { IDNO, IDCANCEL, IDOK }, () => f.Close());
        if (!f.IsDisposed) f.Dispose();
    }

    // ================================================================== D
    /// <summary>
    /// (П325) Временная копия config\device: запись конфигурации прибора («Сохранить», «Да» на
    /// «Сохранить?») с П320 доходит до файла — путь менеджера подменяется сюда на весь раздел
    /// D, заверенный config\ каталога проб не трогается (строка W).
    /// </summary>
    static string deviceTmp;

    static bool RedirectDeviceWrites()
    {
        DeviceConfigManager dm = DeviceConfigManager.GetInstance();
        FieldInfo pf = typeof(DeviceConfigManager).GetField("userDirectoryConfigDevice", Any);
        string real = pf == null ? null : pf.GetValue(dm) as string;
        if (real == null || !Directory.Exists(real))
        {
            Console.WriteLine("⛔ обвязка: путь записи DeviceConfigManager не найден — запись ушла бы в каталог проб, раздел D не мерится");
            rig++;
            return false;
        }
        deviceTmp = Path.Combine(Path.GetTempPath(), "p318probe_" + Process.GetCurrentProcess().Id.ToString(Inv), "device");
        if (Directory.Exists(deviceTmp)) Directory.Delete(deviceTmp, true);
        Directory.CreateDirectory(deviceTmp);
        foreach (string p in Directory.GetFiles(real)) File.Copy(p, Path.Combine(deviceTmp, Path.GetFileName(p)));
        pf.SetValue(dm, deviceTmp + Path.DirectorySeparatorChar);
        Console.WriteLine("SETUP\tзапись конфигураций прибора перенаправлена: {0} → {1}", real, deviceTmp);
        return true;
    }

    /// <summary>Число из записанного файла конфигурации (первый элемент с таким именем), или null.</summary>
    static double? WrittenNumber(string file, string element)
    {
        if (!File.Exists(file)) return null;
        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
        doc.Load(file);
        System.Xml.XmlNodeList found = doc.GetElementsByTagName(element);
        double v;
        return found.Count > 0 && double.TryParse(found[0].InnerText, NumberStyles.Float, Inv, out v) ? v : (double?)null;
    }

    static void Device()
    {
        Thread.CurrentThread.CurrentUICulture = Ru;
        if (!RedirectDeviceWrites()) return;
        // Числовое поле шапки (шаг канала) — DoubleTextBox первой вкладки.
        foreach (string how in new[] { "Закрыть", "крестик", "Сохранить", "Закрыть → Да", "Закрыть → Да → Сохранить" })
        {
            DeviceConfigForm f = OpenDevice();
            if (f == null) return;
            DeviceConfigInfo cfg = (DeviceConfigInfo)Field(f, "activeDeviceConfig");
            TextBox box = (TextBox)Field(f, "doubleTextBox6");
            string orig = box.Text;
            double pitch0 = cfg.ChannelPitch;
            string file = Path.Combine(deviceTmp, cfg.Filename);
            bool writing = how != "Закрыть" && how != "крестик";
            if (writing && File.Exists(file)) File.Delete(file); // записано ли — по появлению файла
            Focus(f, box);
            box.Text = "abc";
            Pump(20);
            int before = FieldReports();
            List<string> seen;
            Button close = (Button)Field(f, "button5");
            Button save = (Button)Field(f, "button6");
            if (how == "Закрыть") seen = Answered(new[] { IDNO, IDCANCEL, IDOK }, () => Press(f, close));
            else if (how == "крестик") seen = Answered(new[] { IDNO, IDCANCEL, IDOK }, () => f.Close());
            else if (how == "Сохранить") seen = Answered(new[] { IDCANCEL, IDOK }, () => Press(f, save));
            else
            {
                seen = Answered(new[] { IDYES, IDCANCEL, IDOK }, () => Press(f, close));
                if (how.EndsWith("Сохранить", StringComparison.Ordinal) && !f.IsDisposed)
                {
                    // Фокус ушёл на «Закрыть» без проверки поля — «Сохранить» обязан
                    // проверить поле сам.
                    seen.AddRange(Answered(new[] { IDCANCEL, IDOK }, () => Press(f, save)));
                }
            }
            int reports = FieldReports() - before;
            bool closed = f.IsDisposed;
            string got = "закрыто=" + closed + ", сообщений о поле " + reports.ToString(Inv)
                         + ", вопросы: " + (seen.Count == 0 ? "—" : string.Join(" || ", seen));
            if (how == "Закрыть" || how == "крестик")
            {
                Verdict("D", "поле «шаг канала» = abc, «" + how + "» → «Нет»: окно закрыто без проверки поля", got, closed && reports == 0);
            }
            else
            {
                // (П325; решения Amber 15.10.2026 «Вернуть прежнее (Рекомендую)» и «Оставить
                // (Рекомендую)») Нечисло в поле запись больше не отказывает: поле молча
                // возвращает прежнее число, и запись идёт им — без сообщения о поле. «Сохранить»
                // окно оставляет открытым (в поле — прежнее), «Да» на «Сохранить?» закрывает.
                double? written = WrittenNumber(file, "ChannelPitch");
                bool fieldBack = closed || box.Text == orig;
                bool asWanted = how == "Сохранить" ? !closed : closed;
                bool ok = reports == 0 && fieldBack && asWanted && cfg.ChannelPitch == pitch0
                          && written.HasValue && written.Value == pitch0;
                Verdict("D", "поле «шаг канала» = abc, «" + how + "»: молча записан прежний шаг " + pitch0.ToString(Inv), got
                        + (closed ? "" : ", в поле «" + box.Text + "»")
                        + ", шаг в конфигурации " + cfg.ChannelPitch.ToString(Inv)
                        + ", в записанном файле " + (written.HasValue ? written.Value.ToString(Inv) : "ФАЙЛА НЕТ (запись не дошла)"), ok);
            }
            if (!f.IsDisposed)
            {
                box.Text = orig;
                CloseDevice(f);
            }
        }

        // Положительный контроль: число в поле — «Закрыть» закрывает (на обеих сборках).
        {
            DeviceConfigForm f = OpenDevice();
            if (f == null) return;
            TextBox box = (TextBox)Field(f, "doubleTextBox6");
            Focus(f, box);
            box.Text = "1";
            List<string> seen = Answered(new[] { IDNO, IDCANCEL, IDOK }, () => Press(f, (Button)Field(f, "button5")));
            Verdict("D", "контроль: поле «шаг канала» = 1, «Закрыть» → «Нет»: окно закрыто", "закрыто=" + f.IsDisposed, f.IsDisposed);
            if (!f.IsDisposed) CloseDevice(f);
        }

        // Звуковой вход: поле порога во вложенной форме прибора.
        AudioField();
        try { Directory.Delete(Path.GetDirectoryName(deviceTmp), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static void AudioField()
    {
        DeviceConfigForm f = OpenDevice();
        if (f == null) return;
        DeviceConfigInfo cfg = (DeviceConfigInfo)Field(f, "activeDeviceConfig");
        // Вкладка прибора — ДО смены типа: смена вкладки при изменённой конфигурации
        // спрашивает «Сохранить?» (`tabControl1_Selecting`), а «Нет» откатило бы тип.
        TabPage devicePage = (TabPage)Field(f, "tabPage3");
        ((TabControl)devicePage.Parent).SelectedTab = devicePage;
        Pump(50);
        ComboBox types = (ComboBox)Field(f, "comboBox4");
        int audio = -1;
        for (int i = 0; i < types.Items.Count; i++)
        {
            DeviceType t = types.Items[i] as DeviceType;
            if (t != null && t.Id == "AudioInputDevice") audio = i;
        }
        if (audio < 0)
        {
            Console.WriteLine("⛔ обвязка: в списке типов нет звукового входа");
            rig++;
            CloseDevice(f);
            return;
        }
        Answered(new[] { IDYES, IDCANCEL, IDOK }, () => types.SelectedIndex = audio);
        AudioInputDeviceForm af = Field(f, "inputDeviceForm") as AudioInputDeviceForm;
        if (af == null)
        {
            Console.WriteLine("⛔ обвязка: форма звукового входа не поднялась");
            rig++;
            CloseDevice(f);
            return;
        }
        TextBox lower = (TextBox)Field(af, "doubleTextBox1");
        PRAHomageMethodConfig pra = (PRAHomageMethodConfig)((AudioInputDeviceConfig)cfg.InputDeviceConfig).PulseDetectionMethodConfig;
        string orig = lower.Text;
        double lower0 = pra.LowerThreshold;
        Console.WriteLine("SETUP\tзвуковой вход: нижний порог «{0}» = {1}", orig, lower0.ToString(Inv));

        string file = Path.Combine(deviceTmp, cfg.Filename);
        if (File.Exists(file)) File.Delete(file); // записано ли — по появлению файла
        Focus(f, lower);
        lower.Text = "abc";
        Pump(20);
        int before = FieldReports();
        Button close = (Button)Field(f, "button5");
        Button save = (Button)Field(f, "button6");
        List<string> seen = Answered(new[] { IDYES, IDCANCEL, IDOK }, () => Press(f, close));
        if (!f.IsDisposed) seen.AddRange(Answered(new[] { IDCANCEL, IDOK }, () => Press(f, save)));
        int reports = FieldReports() - before;
        // (П325; решения Amber 15.10.2026 «Вернуть прежнее (Рекомендую)» и «Оставить
        // (Рекомендую)») «Да» на «Сохранить?» с нечислом в поле вложенной формы записывает
        // прежний порог молча, и окно закрывается.
        pra = (PRAHomageMethodConfig)((AudioInputDeviceConfig)cfg.InputDeviceConfig).PulseDetectionMethodConfig;
        double? written = WrittenNumber(file, "LowerThreshold");
        Verdict("D", "звуковой вход, порог = abc, «Закрыть → Да» (затем «Сохранить», если окно осталось): молча записан прежний порог " + lower0.ToString(Inv),
                "закрыто=" + f.IsDisposed + ", сообщений о поле " + reports.ToString(Inv) + ", порог в конфигурации " + pra.LowerThreshold.ToString(Inv)
                + ", в записанном файле " + (written.HasValue ? written.Value.ToString(Inv) : "ФАЙЛА НЕТ (запись не дошла)")
                + ", вопросы: " + (seen.Count == 0 ? "—" : string.Join(" || ", seen)),
                f.IsDisposed && reports == 0 && pra.LowerThreshold == lower0 && written.HasValue && written.Value == lower0);

        // НАХОДКИ вне поручения: разбор полей звукового входа при записи.
        if (f.IsDisposed)
        {
            Console.WriteLine("НАХОДКА\tзвуковой вход: SaveFormContents с «abc» и «0,5»\tне мерено: окно закрыто записью (П320 «Вернуть прежнее»; «0,5» → 5 закрыта П320)\t—");
        }
        if (!f.IsDisposed)
        {
            lower.Text = "abc";
            bool accepted = (bool)Call(f, "SaveFormContents", cfg);
            pra = (PRAHomageMethodConfig)((AudioInputDeviceConfig)cfg.InputDeviceConfig).PulseDetectionMethodConfig;
            Finding("звуковой вход: SaveFormContents с порогом «abc» (отказ вложенной формы не читается)",
                    "принято=" + accepted + ", порог " + pra.LowerThreshold.ToString(Inv), !accepted);
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = Ru;
            lower.Text = "0,5";
            bool fieldOk = UserNumber.TryParseDouble(lower.Text, out double parsed);
            accepted = (bool)Call(f, "SaveFormContents", cfg);
            Thread.CurrentThread.CurrentCulture = saved;
            pra = (PRAHomageMethodConfig)((AudioInputDeviceConfig)cfg.InputDeviceConfig).PulseDetectionMethodConfig;
            Finding("звуковой вход: порог «0,5» на русской системе — поле принимает (" + parsed.ToString(Inv) + "), запись кладёт",
                    "принято=" + accepted + ", порог " + pra.LowerThreshold.ToString(Inv), fieldOk && accepted && pra.LowerThreshold == 0.5);
            lower.Text = orig;
            CloseDevice(f);
        }
    }

    static DeviceConfigForm OpenDevice()
    {
        DeviceConfigForm f = new DeviceConfigForm();
        ShowOff(f);
        TableModel list = (TableModel)Field(f, "tableModel1");
        if (list.Rows.Count == 0)
        {
            Console.WriteLine("⛔ обвязка: в каталоге проб нет конфигураций прибора");
            rig++;
            f.Dispose();
            return null;
        }
        list.Selections.SelectCell(0, 0);
        Pump(100);
        if (Field(f, "activeDeviceConfig") == null)
        {
            Console.WriteLine("⛔ обвязка: конфигурация не стала активной");
            rig++;
            f.Dispose();
            return null;
        }
        return f;
    }

    /// <summary>Закрыть, отвечая «Нет» на «Сохранить?»; на всякий случай — без проверки полей.</summary>
    static void CloseDevice(DeviceConfigForm f)
    {
        if (f.IsDisposed) return;
        f.AutoValidate = AutoValidate.Disable;
        Answered(new[] { IDNO, IDCANCEL, IDOK }, () => f.Close());
        if (!f.IsDisposed) f.Dispose();
    }

    static void Focus(Form f, Control box)
    {
        TabPage page = Parent<TabPage>(box);
        if (page != null) ((TabControl)page.Parent).SelectedTab = page;
        Pump(20);
        f.ActiveControl = box;
        Pump(20);
        if (!box.Focused && !ReferenceEquals(f.ActiveControl, box) && !ContainsActive(f, box))
        {
            Console.WriteLine("⛔ обвязка: поле {0} не стало активным", box.Name);
            rig++;
        }
    }

    /// <summary>
    /// Щелчок мышью по кнопке: фокус переходит на кнопку (с проверкой покидаемого поля —
    /// если у кнопки <c>CausesValidation</c>), затем сам щелчок. Голый PerformClick фокуса
    /// не двигает, и поле остаётся активным — у мыши так не бывает.
    /// </summary>
    static void Press(Form f, Button b)
    {
        f.ActiveControl = b;
        Pump(20);
        if (!f.IsDisposed) b.PerformClick();
    }

    static bool ContainsActive(Form f, Control box)
    {
        ContainerControl c = f;
        while (c != null)
        {
            if (ReferenceEquals(c.ActiveControl, box)) return true;
            c = c.ActiveControl as ContainerControl;
        }
        return false;
    }

    /// <summary>Сообщения полей о нечисле (AppUi без окон пишет их в поток ошибок).</summary>
    static int FieldReports()
    {
        string all = errors.ToString();
        return Count(all, BecquerelMonitor.Properties.Resources.ERRInputNumber)
               + Count(all, BecquerelMonitor.Properties.Resources.ERRInputInteger);
    }

    // ================================================================== помощники
    static List<string> Answered(int[] answers, Action act)
    {
        DialogAnswerer.Start(answers);
        try
        {
            act();
            Pump(150);
        }
        finally
        {
            DialogAnswerer.Stop();
        }
        return DialogAnswerer.Seen();
    }

    static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + name);
    }

    static void Step(string id, string text)
    {
        Console.WriteLine();
        Console.WriteLine("--- {0}: {1}", id, text);
    }

    static void Verdict(string id, string what, string got, bool clean)
    {
        if (!clean) defects++;
        Console.WriteLine("{0}\t{1}\t{2}\t{3}", id, what, got ?? "(null)", clean ? "ЧИСТО" : "ДЕФЕКТ");
    }

    static void Finding(string what, string got, bool clean)
    {
        if (!clean) findings++;
        Console.WriteLine("НАХОДКА\t{0}\t{1}\t{2}", what, got, clean ? "чисто" : "ДЕФЕКТ (вне поручения)");
    }

    static string SetName(NuclideSet s)
    {
        return s == null ? "все нуклиды" : s.Name;
    }

    static int Count(string hay, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
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

    static T Parent<T>(Control c) where T : Control
    {
        for (Control p = c.Parent; p != null; p = p.Parent) if (p is T) return (T)p;
        return null;
    }

    static string Sha(string path)
    {
        using (SHA256 h = SHA256.Create())
        using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
    }

    static string Short(string sha)
    {
        return sha.Substring(0, 12);
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

    static string Reason(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.GetType().Name + ": " + ex.Message;
    }

    /// <summary>
    /// Отвечает на вопросы приложения (окна класса #32770 своего процесса): по очереди
    /// ответы из списка (WM_COMMAND), пока окно не уйдёт. Записывает тексты.
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
        static int[] answers = { IDNO, IDCANCEL, IDOK };
        static readonly Dictionary<IntPtr, int> tries = new Dictionary<IntPtr, int>();
        static readonly List<string> seen = new List<string>();

        public static void Start(int[] with)
        {
            lock (seen) { seen.Clear(); tries.Clear(); answers = with; }
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
                    lock (seen)
                    {
                        int n;
                        tries.TryGetValue(h, out n);
                        if (n == 0) seen.Add(Text(h));
                        if (n < answers.Length) PostMessage(h, 0x0111, (IntPtr)answers[n], IntPtr.Zero);
                        tries[h] = n + 1;
                    }
                    return true;
                }, IntPtr.Zero);
                Thread.Sleep(150);
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
                    StringBuilder s = new StringBuilder(1024);
                    GetWindowText(c, s, 1024);
                    if (s.Length > 0) all.Append(s.ToString().Replace("\r", " ").Replace("\n", " "));
                }
                return true;
            }, IntPtr.Zero);
            return all.ToString().Trim();
        }
    }
}
