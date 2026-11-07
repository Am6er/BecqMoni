// Проба AMBER310 (05.11.2026): кнопка «Reset Defaults» окна библиотеки нуклидов. Журнал —
// handover/handover-2026-11-05-fixsets.md.
//
// Amber, консоль, дословно: «В библиотеку нуклидов добавь кнопку Reset Defaults, чтобы сбрасывать
// всю библиотеку на текущий поставочный набор нуклидов и сбрасывать все сеты на текущий поставочный
// набор сетов.» Вопросником: «Вшить в exe (Рекомендую)», «Вопрос и резервная копия (Рекомендую)».
//
//   R  ресурс вшит и побайтно равен config\NuclideDefinition.xml рядом с пробой (и в дереве, --repo=);
//   A  ответ «Нет»: файл, копия, объекты менеджера и пометка окна не меняются;
//   B  ответ «Да» при несохранённой правке окна: файл = вшитому побайтно, .bak = прежнему, объекты
//      новые, выбор документов перенесён по Id либо снят, окно чисто, список панели новый;
//   C  повторный сброс: копия одна, последняя; файл уже поставочный — копия не трогается (иначе
//      второе нажатие уничтожало единственную копию библиотеки человека, критик `critSets`, Н1);
//   D  файл занят: отказ словами, ничего не изменилось (контроль: файл действительно занят);
//   E  файл испорчен: сброс чинит, испорченный уходит в .bak;
//   F  файла нет: сброс кладёт файл, копии нет;
//   G  раскладки: портативная (путь Package, --portable) и установленная (строка пути, каталог
//      ещё не существует);
//   M  ресурс не найден: сборка-мутант с другим LogicalName, ключ --no-resource: отказ словами,
//      вопроса нет.
//
// ЗАПУСК — из КОПИИ каталога проб на скрытом столе, с каталогом приборов рядом:
//
//   ResetDefaultsProbeA310.exe --tmp=<каталог> [--repo=<корень дерева>] [--shots=<каталог>] [--lang=ru|en]
//                              [--portable] [--no-resource]
//
// ⛔ Пишет проба только в <каталог> (путь файла менеджера переставляется отражением); с --portable —
// в config\NuclideDefinition.xml рядом с собой, поэтому запускать из копии каталога. Настоящий
// %AppData%\BecqMoni\config сверяется sha256 до и после. Вопросы и сообщения — через шов AppUi.Probe,
// окон MessageBox проба не поднимает. Коды: 0 — дефектов нет; 4 — есть дефекты; 5 — обвязка.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;

static class ResetDefaultsProbeA310
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int defects;
    static NuclideDefinitionManager mgr;
    static string work, bak;
    static byte[] shipped;
    static NuclideDefinitionFile shippedFile;
    static readonly List<string> asks = new List<string>();
    static readonly List<string> reports = new List<string>();
    static bool answer;

    [System.Runtime.InteropServices.DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
    [System.Runtime.InteropServices.DllImport("kernel32")] static extern bool TerminateProcess(IntPtr h, uint code);

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = Inv;
        string tmp = Key(args, "--tmp=");
        string repo = Key(args, "--repo=");
        string shots = Key(args, "--shots=");
        string lang = Key(args, "--lang=") ?? "en";
        bool portable = args.Contains("--portable");
        bool noResource = args.Contains("--no-resource");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang == "ru" ? "ru-RU" : "en-US");
        if (tmp == null)
        {
            Console.WriteLine("нет --tmp=");
            return 5;
        }

        try
        {
            Directory.CreateDirectory(tmp);
            string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BecqMoni", "config", "NuclideDefinition.xml");
            string realBefore = File.Exists(real) ? Sha(File.ReadAllBytes(real)) : "нет";
            AppUi.Probe = (kind, text, caption) =>
            {
                (kind == "ask" ? asks : reports).Add(text);
                return answer;
            };
            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            mgr = NuclideDefinitionManager.GetInstance();

            if (noResource)
            {
                return RunNoResource(tmp, real, realBefore);
            }

            Section("R: ресурс");
            string refusal;
            bool read = NuclideDefinitionManager.ReadShippedLibrary(out shipped, out shippedFile, out refusal);
            Verdict("R1", "ресурс читается и разбирается", read ? shipped.Length + " байт" : refusal, read);
            if (!read)
            {
                return 4;
            }
            Console.WriteLine("R\tвшито: линий {0}, наборов {1}, sha256 {2}", shippedFile.NuclideDefinitions.Count, shippedFile.NuclideSets.Count, Sha(shipped));
            string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "NuclideDefinition.xml");
            if (!portable)
            {
                Verdict("R2", "вшитое = config\\NuclideDefinition.xml рядом с пробой", Sha(File.ReadAllBytes(beside)), File.ReadAllBytes(beside).SequenceEqual(shipped));
            }
            if (repo != null)
            {
                foreach (string rel in new[] { @"config\NuclideDefinition.xml", @"BecquerelMonitor\config\NuclideDefinition.xml" })
                {
                    byte[] tree = File.ReadAllBytes(Path.Combine(repo, rel));
                    Verdict("R3", "вшитое = " + rel + " дерева", Sha(tree), tree.SequenceEqual(shipped));
                }
            }

            work = portable ? Package.GetInstance().NuclideDefinition : Path.Combine(tmp, "config", "NuclideDefinition.xml");
            bak = work + ".bak";
            Directory.CreateDirectory(Path.GetDirectoryName(work));
            SetField(mgr, "nuclideDefinitionFilename", work);
            Console.WriteLine("SETUP\tрабочий файл: {0}", work);

            CultureInfo ui = Thread.CurrentThread.CurrentUICulture;
            MainForm mainForm = new MainForm(new string[0]);
            Thread.CurrentThread.CurrentUICulture = ui;  // подъём приложения ставит язык из конфига
            // Панели MainForm заводит при показе (`InitializeToolViews`); здесь панель поднята рукой на её поле.
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            SetField(mainForm, "dcPeakDetectionView", panel);
            ComboBox combo = (ComboBox)Field(panel, "comboBoxNuclSet");
            Console.WriteLine("SETUP\tMainForm поднята, панель поиска пиков есть: {0}", panel != null);

            ScenarioCancel();
            ScenarioYes(mainForm, panel, combo);
            ScenarioRepeat();
            ScenarioBusy();
            ScenarioCorrupt();
            ScenarioMissing();
            ScenarioLayouts(tmp, portable);

            if (shots != null)
            {
                Directory.CreateDirectory(shots);
                PutEdited(0);
                mgr.LoadDefinitionFile(true);
                NuclideDefinitionForm f = NewForm();
                Pump(100);
                string name = Path.Combine(shots, "A310_window_" + lang + ".png");
                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(name, ImageFormat.Png);
                }
                Button reset = (Button)Field(f, "buttonResetDefaults");
                Console.WriteLine("SHOT\t{0}: кнопка «{1}», {2}×{3} в {4},{5}; подсказка: {6}", name, reset.Text, reset.Width, reset.Height, reset.Left, reset.Top,
                                  ((ToolTip)Field(f, "dbToolTip")).GetToolTip(reset).Replace(Environment.NewLine, " / "));
                Verdict("S1", "текст кнопки влезает", TextRenderer.MeasureText(reset.Text, reset.Font).Width + " из " + reset.Width,
                        TextRenderer.MeasureText(reset.Text, reset.Font).Width + 6 <= reset.Width);
                Verdict("S2", "кнопка не задевает таблицу и «Сохранить»", reset.Bounds.ToString(),
                        !reset.Bounds.IntersectsWith(((Control)Field(f, "table1")).Bounds) && !reset.Bounds.IntersectsWith(((Control)Field(f, "button6")).Bounds));
                f.Dispose();
            }

            Section("Итог");
            string realAfter = File.Exists(real) ? Sha(File.ReadAllBytes(real)) : "нет";
            Verdict("Z1", "настоящий конфиг не тронут", realAfter, realBefore == realAfter);
            Console.WriteLine("дефектов: {0}", defects);
            Exit(defects == 0 ? 0 : 4);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ обвязка: " + ex);
            Exit(5);
            return 5;
        }
    }

    static int RunNoResource(string tmp, string real, string realBefore)
    {
        Section("M: сборка без ресурса");
        work = Path.Combine(tmp, "config", "NuclideDefinition.xml");
        bak = work + ".bak";
        Directory.CreateDirectory(Path.GetDirectoryName(work));
        SetField(mgr, "nuclideDefinitionFilename", work);
        byte[] before = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "NuclideDefinition.xml"));
        File.WriteAllBytes(work, before);
        mgr.LoadDefinitionFile(true);
        NuclideDefinitionForm f = NewForm();
        Clear();
        answer = true;
        bool done = f.ResetLibraryToShippedForProbe();
        Verdict("M1", "вернул отказ", done.ToString(), !done);
        Verdict("M2", "вопроса не было", asks.Count.ToString(Inv), asks.Count == 0);
        Verdict("M3", "отказ словами, с именем ресурса", reports.Count == 0 ? "(нет)" : reports[0],
                reports.Count == 1 && reports[0].Contains(NuclideDefinitionManager.ShippedLibraryResource));
        Verdict("M4", "файл не тронут, копии нет", Sha(File.ReadAllBytes(work)), File.ReadAllBytes(work).SequenceEqual(before) && !File.Exists(bak));
        f.Dispose();
        Console.WriteLine("дефектов: {0}", defects);
        Exit(defects == 0 ? 0 : 4);
        return 0;
    }

    static void ScenarioCancel()
    {
        Section("A: ответ «Нет»");
        PutEdited(0);
        mgr.LoadDefinitionFile(true);
        byte[] before = File.ReadAllBytes(work);
        DeleteIf(bak);
        NuclideDefinitionFile objectsBefore = mgr.NuclideDefinitionFile;
        NuclideDefinitionForm f = NewForm();
        MakeWindowDirty(f);
        Clear();
        answer = false;
        bool done = f.ResetLibraryToShippedForProbe();
        Verdict("A1", "вернул «не сделано»", done.ToString(), !done);
        Verdict("A2", "вопрос ровно один", asks.Count.ToString(Inv), asks.Count == 1);
        string q = asks.Count > 0 ? asks[0] : "";
        Console.WriteLine("A\tвопрос: {0}", q.Replace("\n", " / ").Replace("\r", ""));
        int lines = objectsBefore.NuclideDefinitions.Count, sets = objectsBefore.NuclideSets.Count;
        Verdict("A3", "в вопросе числа: линии и наборы сейчас и в поставке, путь копии", lines + "/" + sets + " → " + shippedFile.NuclideDefinitions.Count + "/" + shippedFile.NuclideSets.Count,
                q.Contains(lines.ToString(Inv)) && q.Contains(sets.ToString(Inv)) && q.Contains(shippedFile.NuclideDefinitions.Count.ToString(Inv))
                && q.Contains(shippedFile.NuclideSets.Count.ToString(Inv)) && q.Contains(bak));
        Verdict("A4", "о несохранённых правках сказано", q.Contains(BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryUnsaved).ToString(),
                q.Contains(BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryUnsaved));
        Verdict("A5", "файл не тронут, копии нет", Sha(File.ReadAllBytes(work)), File.ReadAllBytes(work).SequenceEqual(before) && !File.Exists(bak));
        Verdict("A8", "копии нет: фразы о её замене нет", q.Contains(BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryBackupExists).ToString(),
                !q.Contains(BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryBackupExists));
        Verdict("A6", "объекты менеджера те же", ReferenceEquals(objectsBefore, mgr.NuclideDefinitionFile).ToString(), ReferenceEquals(objectsBefore, mgr.NuclideDefinitionFile));
        Verdict("A7", "правка окна осталась за человеком", f.HasUnsavedEditsForProbe().ToString(), f.HasUnsavedEditsForProbe());
        f.Dispose();
    }

    static void ScenarioYes(MainForm mainForm, DCPeakDetectionView panel, ComboBox combo)
    {
        Section("B: ответ «Да» при несохранённой правке; документы и панель");
        PutEdited(0);
        mgr.LoadDefinitionFile(true);
        byte[] before = File.ReadAllBytes(work);
        DeleteIf(bak);
        // Набор из поставки, живущий и в правленом файле, и набор только правленого файла.
        NuclideSet keep = mgr.NuclideSets.First(s => shippedFile.NuclideSets.Any(x => x.Id == s.Id));
        NuclideSet hand = mgr.NuclideSets.First(s => shippedFile.NuclideSets.All(x => x.Id != s.Id));
        DocEnergySpectrum a = new DocEnergySpectrum();
        DocEnergySpectrum b = new DocEnergySpectrum();
        a.SelectedNuclideSet = keep;
        b.SelectedNuclideSet = hand;
        List<DocEnergySpectrum> docs = DocumentManager.GetInstance().DocumentList;
        docs.Add(a);
        docs.Add(b);
        mgr.ActiveSet = hand;
        panel.RefreshNuclideSets();
        Verdict("B0", "контроль: до сброса выбор держит чужой объект", combo.Items.Count + " строк, активный «" + mgr.ActiveSet.Name + "»", combo.Items.Contains(hand.Name));

        NuclideDefinitionForm f = NewForm();
        f.Owner = mainForm;
        MakeWindowDirty(f);
        Clear();
        answer = true;
        object oldActive = Field(f, "activeNuclide");
        bool done = f.ResetLibraryToShippedForProbe();
        Pump(150);
        Verdict("B1", "вернул «сделано»", done.ToString(), done);
        Verdict("B2", "вопрос один, отказов нет", asks.Count + "/" + reports.Count, asks.Count == 1 && reports.Count == 0);
        Verdict("B3", "файл побайтно равен вшитому", Sha(File.ReadAllBytes(work)), File.ReadAllBytes(work).SequenceEqual(shipped));
        Verdict("B4", ".bak побайтно равен прежнему файлу", File.Exists(bak) ? Sha(File.ReadAllBytes(bak)) : "нет", File.Exists(bak) && File.ReadAllBytes(bak).SequenceEqual(before));
        Verdict("B5", "временного файла не осталось", File.Exists(work + ".reset.tmp").ToString(), !File.Exists(work + ".reset.tmp"));
        Verdict("B6", "менеджер: линий и наборов как в поставке", mgr.NuclideDefinitions.Count + "/" + mgr.NuclideSets.Count,
                mgr.NuclideDefinitions.Count == shippedFile.NuclideDefinitions.Count && mgr.NuclideSets.Count == shippedFile.NuclideSets.Count);
        Verdict("B7", "документ с поставочным набором: тот же Id, объект из менеджера",
                a.SelectedNuclideSet == null ? "null" : a.SelectedNuclideSet.Name,
                a.SelectedNuclideSet != null && a.SelectedNuclideSet.Id == keep.Id && mgr.NuclideSets.Contains(a.SelectedNuclideSet) && !ReferenceEquals(a.SelectedNuclideSet, keep));
        Verdict("B8", "документ с удалённым набором: «все нуклиды»", b.SelectedNuclideSet == null ? "null" : b.SelectedNuclideSet.Name, b.SelectedNuclideSet == null);
        Verdict("B9", "активный набор панели снят (его нет в поставке)", mgr.ActiveSet == null ? "null" : mgr.ActiveSet.Name, mgr.ActiveSet == null);
        Verdict("B10", "окно: правок нет, активной линии нет, «Сохранить» погашена",
                f.HasUnsavedEditsForProbe() + "/" + (Field(f, "activeNuclide") == null) + "/" + ((Button)Field(f, "button6")).Enabled,
                !f.HasUnsavedEditsForProbe() && Field(f, "activeNuclide") == null && !((Button)Field(f, "button6")).Enabled);
        XPTable.Models.TableModel model = (XPTable.Models.TableModel)Field(f, "tableModel1");
        Verdict("B11", "таблица окна показывает поставку", model.Rows.Count.ToString(Inv), model.Rows.Count == mgr.NuclideDefinitions.Count);
        Verdict("B12", "строки таблицы — объекты менеджера, не прежние",
                model.Rows.Count > 0 ? (mgr.NuclideDefinitions.Contains((NuclideDefinition)model.Rows[0].Tag)).ToString() : "пусто",
                model.Rows.Count > 0 && mgr.NuclideDefinitions.Contains((NuclideDefinition)model.Rows[0].Tag) && oldActive != null && !ReferenceEquals(model.Rows[0].Tag, oldActive));
        Verdict("B13", "список панели: новые имена", combo.Items.Count.ToString(Inv),
                combo.Items.Count == mgr.NuclideSets.Count + 1 && !combo.Items.Contains(hand.Name)
                && mgr.NuclideSets.All(s => combo.Items.Contains(s.Name)));
        // Выбор документа `a` возвращается при переходе на него, по ссылке.
        mainForm.ActiveDocument = a;
        panel.ShowPeakDetectionResult();
        Pump(100);
        Verdict("B14", "панель находит набор документа", combo.SelectedItem + "", Equals(combo.SelectedItem, keep.Name) && ReferenceEquals(mgr.ActiveSet, a.SelectedNuclideSet));
        mainForm.ActiveDocument = null;
        mgr.ActiveSet = null;
        int asksBefore = asks.Count;
        f.Close();
        Verdict("B15", "закрытие окна без вопроса", (asks.Count - asksBefore).ToString(Inv), asks.Count == asksBefore && f.IsDisposed);
        docs.Remove(a);
        docs.Remove(b);
    }

    static void ScenarioRepeat()
    {
        Section("C: повторный сброс — копия одна, последняя");
        PutEdited(1);
        mgr.LoadDefinitionFile(true);
        byte[] before = File.ReadAllBytes(work);
        NuclideDefinitionForm f = NewForm();
        Clear();
        answer = true;
        bool done = f.ResetLibraryToShippedForProbe();
        Verdict("C1", "сделано", done.ToString(), done);
        Verdict("C1b", "копия уже есть: в вопросе фраза о её замене", asks.Count > 0 ? asks[0].Contains(BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryBackupExists).ToString() : "нет вопроса",
                asks.Count == 1 && asks[0].Contains(BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryBackupExists));
        Verdict("C2", ".bak = файл перед этим сбросом", Sha(File.ReadAllBytes(bak)), File.ReadAllBytes(bak).SequenceEqual(before));
        string[] copies = Directory.GetFiles(Path.GetDirectoryName(work), "NuclideDefinition.xml*");
        Verdict("C3", "рядом только файл и одна копия", string.Join(", ", copies.Select(Path.GetFileName)), copies.Length == 2);
        Clear();
        done = f.ResetLibraryToShippedForProbe();
        Verdict("C4", "второе нажатие: вопроса нет, сообщение одно, вернул «не сделано»", asks.Count + "/" + reports.Count + "/" + done, !done && asks.Count == 0 && reports.Count == 1);
        Verdict("C5", "второе нажатие: .bak — по-прежнему библиотека человека", Sha(File.ReadAllBytes(bak)),
                File.ReadAllBytes(bak).SequenceEqual(before) && File.ReadAllBytes(work).SequenceEqual(shipped));
        Console.WriteLine("C\tсообщение: {0}", reports.Count > 0 ? reports[0] : "");

        // Файл поставочный, в окне правка: вопрос про правку, файл и копия на месте.
        MakeWindowDirty(f);
        Clear();
        answer = false;
        NuclideDefinitionFile objectsBefore = mgr.NuclideDefinitionFile;
        done = f.ResetLibraryToShippedForProbe();
        Verdict("C6", "правка окна, ответ «Нет»: правка и объекты на месте", done + ", вопросов " + asks.Count,
                !done && asks.Count == 1 && f.HasUnsavedEditsForProbe() && ReferenceEquals(objectsBefore, mgr.NuclideDefinitionFile));
        string q = asks.Count > 0 ? asks[0] : "";
        Console.WriteLine("C\tвопрос: {0}", q.Replace("\n", " / ").Replace("\r", ""));
        Verdict("C7", "текст вопроса про правки, а не про замену файла", q.Contains(bak).ToString(),
                q == BecquerelMonitor.Properties.Resources.MSGResetNuclideLibraryDropEdits);
        Clear();
        answer = true;
        done = f.ResetLibraryToShippedForProbe();
        Verdict("C8", "ответ «Да»: правки сняты, объекты новые", done + ", правок " + f.HasUnsavedEditsForProbe(),
                done && !f.HasUnsavedEditsForProbe() && !ReferenceEquals(objectsBefore, mgr.NuclideDefinitionFile));
        Verdict("C9", "файл и .bak не тронуты", Sha(File.ReadAllBytes(bak)),
                File.ReadAllBytes(bak).SequenceEqual(before) && File.ReadAllBytes(work).SequenceEqual(shipped));

        // S2 критика `critWave2`: «Сохранить» без правки пишет номера сверки с базой, правкой это не считается.
        bool saved = mgr.SaveDefinitionFile();
        byte[] resaved = File.ReadAllBytes(work);
        Verdict("C10", "контроль: запись без правки изменила байты файла", saved + ", байт " + resaved.Length + " против " + shipped.Length, saved && !resaved.SequenceEqual(shipped));
        Clear();
        answer = true;
        done = f.ResetLibraryToShippedForProbe();
        Verdict("C11", "сброс после записи без правки: вопроса нет, копия и файл не тронуты", asks.Count + "/" + reports.Count + "/" + done,
                !done && asks.Count == 0 && reports.Count == 1 && File.ReadAllBytes(bak).SequenceEqual(before) && File.ReadAllBytes(work).SequenceEqual(resaved));
        f.Dispose();
    }

    static void ScenarioBusy()
    {
        Section("D: файл занят — отказ словами");
        PutEdited(0);
        mgr.LoadDefinitionFile(true);
        byte[] before = File.ReadAllBytes(work);
        byte[] bakBefore = File.ReadAllBytes(bak);
        NuclideDefinitionFile objectsBefore = mgr.NuclideDefinitionFile;
        NuclideDefinitionForm f = NewForm();
        MakeWindowDirty(f);
        Clear();
        answer = true;
        bool done;
        using (new FileStream(work, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool held = false;
            try { using (new FileStream(work, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { } }
            catch (IOException) { held = true; }
            Verdict("D0", "контроль: файл действительно занят", held.ToString(), held);
            done = f.ResetLibraryToShippedForProbe();
        }
        Verdict("D1", "вернул отказ", done.ToString(), !done);
        Verdict("D2", "отказ словами, с путём", reports.Count == 0 ? "(нет)" : reports[0].Replace("\n", " / ").Replace("\r", ""),
                reports.Count == 1 && reports[0].Contains(Path.GetFileName(work)));
        Verdict("D3", "файл и копия прежние, временного нет", Sha(File.ReadAllBytes(work)),
                File.ReadAllBytes(work).SequenceEqual(before) && File.ReadAllBytes(bak).SequenceEqual(bakBefore) && !File.Exists(work + ".reset.tmp"));
        Verdict("D4", "объекты менеджера те же", ReferenceEquals(objectsBefore, mgr.NuclideDefinitionFile).ToString(), ReferenceEquals(objectsBefore, mgr.NuclideDefinitionFile));
        Verdict("D5", "правка окна осталась", f.HasUnsavedEditsForProbe().ToString(), f.HasUnsavedEditsForProbe());
        f.Dispose();
    }

    static void ScenarioCorrupt()
    {
        Section("E: файл испорчен");
        byte[] broken = Encoding.UTF8.GetBytes("<NuclideDefinitionFile><оборвано");
        File.WriteAllBytes(work, broken);
        Clear();
        bool loaded = mgr.LoadDefinitionFile(true);
        Verdict("E0", "контроль: файл не читается", loaded + ", отказов " + reports.Count, !loaded);
        NuclideDefinitionManager same = NuclideDefinitionManager.GetInstance();
        Verdict("E1", "заготовка в памяти поверх файла не записана", same.NuclideDefinitions.Count + " линий", File.ReadAllBytes(work).SequenceEqual(broken));
        NuclideDefinitionForm f = NewForm();
        Clear();
        answer = true;
        bool done = f.ResetLibraryToShippedForProbe();
        Verdict("E2", "сброс прошёл, файл = вшитому", done.ToString(), done && File.ReadAllBytes(work).SequenceEqual(shipped));
        Verdict("E3", "испорченный файл сохранён в .bak", Sha(File.ReadAllBytes(bak)), File.ReadAllBytes(bak).SequenceEqual(broken));
        Verdict("E4", "запись снова разрешена", ((bool)Field(mgr, "loadFailedOverExistingFile")).ToString(), !(bool)Field(mgr, "loadFailedOverExistingFile"));
        Verdict("E5", "повторное чтение файла даёт поставку", mgr.LoadDefinitionFile(true) + ", " + mgr.NuclideDefinitions.Count, mgr.NuclideDefinitions.Count == shippedFile.NuclideDefinitions.Count);
        f.Dispose();
    }

    static void ScenarioMissing()
    {
        Section("F: файла нет");
        mgr.LoadDefinitionFile(true);
        NuclideDefinitionForm f = NewForm();
        File.Delete(work);
        DeleteIf(bak);
        Clear();
        answer = true;
        bool done = f.ResetLibraryToShippedForProbe();
        Verdict("F1", "файл положен, побайтно вшитый", done.ToString(), done && File.ReadAllBytes(work).SequenceEqual(shipped));
        Verdict("F2", "копии нет: копировать было нечего", File.Exists(bak).ToString(), !File.Exists(bak));
        f.Dispose();
    }

    static void ScenarioLayouts(string tmp, bool portable)
    {
        Section("G: раскладки");
        Package installed = new Package();
        SetField(installed, "isStandAlone", false);
        string installedPath = installed.NuclideDefinition;
        Verdict("G1", "установленная: файл в %AppData%", installedPath, installedPath.EndsWith(@"\BecqMoni\config\NuclideDefinition.xml", StringComparison.OrdinalIgnoreCase));
        Package local = new Package();
        SetField(local, "isStandAlone", true);
        Verdict("G2", "портативная: файл рядом с exe", local.NuclideDefinition,
                string.Equals(local.NuclideDefinition, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"config\NuclideDefinition.xml"), StringComparison.OrdinalIgnoreCase));
        if (portable)
        {
            Verdict("G3", "сброс шёл по настоящему пути портативной раскладки", work,
                    string.Equals(work, local.NuclideDefinition, StringComparison.OrdinalIgnoreCase) && File.ReadAllBytes(work).SequenceEqual(shipped));
        }

        // Установленная: тот же код на подмене пути; настоящий %AppData% проба не трогает.
        string saved = (string)Field(mgr, "nuclideDefinitionFilename");
        string fake = Path.Combine(tmp, "AppData", "Roaming", "BecqMoni", "config", "NuclideDefinition.xml");
        DeleteTree(Path.Combine(tmp, "AppData"));
        SetField(mgr, "nuclideDefinitionFilename", fake);
        NuclideDefinitionForm f = NewForm();
        Clear();
        answer = true;
        bool done = f.ResetLibraryToShippedForProbe();
        Verdict("G4", "каталога ещё нет: сброс заводит его и кладёт файл", done.ToString(), done && File.ReadAllBytes(fake).SequenceEqual(shipped));
        f.Dispose();
        SetField(mgr, "nuclideDefinitionFilename", saved);
    }

    // ---------------------------------------------------------------------

    /// <summary>Правленый файл: без первого поставочного набора, с линией и набором руками.</summary>
    static void PutEdited(int variant)
    {
        NuclideDefinitionFile file;
        using (var s = new MemoryStream(shipped))
        {
            file = (NuclideDefinitionFile)new XmlSerializer(typeof(NuclideDefinitionFile)).Deserialize(s);
        }
        NuclideSet doomed = file.NuclideSets[0];
        file.NuclideSets.RemoveAt(0);
        file.NuclideDefinitions.RemoveAll(d => d.Sets != null && d.Sets.Contains(doomed.Id));
        NuclideSet own = new NuclideSet { Id = Guid.NewGuid(), Name = "Hand set A310 " + variant.ToString(Inv) };
        file.NuclideSets.Add(own);
        NuclideDefinition line = new NuclideDefinition { Name = "Hand-" + variant.ToString(Inv), Energy = 777.0 + variant, Intencity = 50.0 };
        line.Sets.Add(own.Id);
        file.NuclideDefinitions.Add(line);
        using (var o = new FileStream(work, FileMode.Create, FileAccess.Write))
        {
            new XmlSerializer(typeof(NuclideDefinitionFile)).Serialize(o, file);
        }
    }

    static NuclideDefinitionForm NewForm()
    {
        NuclideDefinitionForm f = new NuclideDefinitionForm();
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-4000, -4000);
        f.ShowInTaskbar = false;
        f.Show();
        Pump(50);
        return f;
    }

    /// <summary>Правка окна — как рукой: выбрать строку, снять галку «Видимый».</summary>
    static void MakeWindowDirty(NuclideDefinitionForm f)
    {
        XPTable.Models.TableModel model = (XPTable.Models.TableModel)Field(f, "tableModel1");
        model.Selections.SelectCell(0, 0);
        Pump(50);
        CheckBox visible = (CheckBox)Field(f, "checkBox1");
        visible.Checked = !visible.Checked;
        Pump(30);
    }

    static void Clear()
    {
        asks.Clear();
        reports.Clear();
    }

    static void DeleteIf(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    static void DeleteTree(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    static bool ResetLibraryToShippedForProbe(this NuclideDefinitionForm f)
    {
        return (bool)typeof(NuclideDefinitionForm).GetMethod("ResetLibraryToShipped", Any).Invoke(f, null);
    }

    static bool HasUnsavedEditsForProbe(this NuclideDefinitionForm f)
    {
        return (bool)typeof(NuclideDefinitionForm).GetProperty("HasUnsavedEdits", Any).GetValue(f, null);
    }

    static string Sha(byte[] b)
    {
        using (SHA256 s = SHA256.Create())
        {
            return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }

    static string Key(string[] args, string key)
    {
        string a = args.FirstOrDefault(x => x.StartsWith(key, StringComparison.Ordinal));
        return a == null ? null : a.Substring(key.Length);
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
            if (fi != null)
            {
                fi.SetValue(o, value);
                return;
            }
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static void Exit(int code)
    {
        // MainForm держит таймеры и потоки, а окна её не показаны: Environment.Exit падает на
        // обратном вызове окна (0xC000041D) и теряет код возврата. Снимаем себя сами.
        Console.Out.Flush();
        TerminateProcess(GetCurrentProcess(), (uint)code);
    }
}
