// Проба полосы П316 (15.10.2026): находки прохода экраном П312 — ДО и ПОСЛЕ правки.
// Журнал — handover/handover-2026-10-15-p316-screen-pass-fixes.md; находки —
// handover/handover-2026-10-15-p312-screen-pass.md (Н1–Н23).
//
// ЗАПУСК — из каталога, где лежат BecquerelMonitor.exe и config\ (менеджеры берут пути
// от каталога входной сборки). Проба зовёт приложение только тем, что есть И ДО, И ПОСЛЕ
// правки (поля и методы форм — отражением; новый помощник конца набора — отражением с
// откатом на прежний голый вызов), поэтому один exe работает на обеих сборках: сборка
// ДО правки — положительный контроль (обязана показать дефекты).
//
//   P316ScreenPassProbe.exe [--tmp=<каталог>]
//
// Каждая строка вывода — «Н<номер>  что мерено  значение  ЧИСТО|ДЕФЕКТ». Окна строятся
// вне экрана (−4000, −4000); единственные настоящие окна — вопросы «Сохранить?»
// редактора наборов и конфигурации прибора: на них поток-автоответчик жмёт «Нет»
// (IDNO), запись файлов проба не делает. Библиотека нуклидов каталога проб сверяется
// sha256 до и после — «Нет» обязан не писать.
//
// Коды: 0 — дефектов нет; 4 — есть дефекты (на сборке ДО — ожидаемо); 5 — обвязка.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using XPTable.Events;
using XPTable.Models;

static class P316ScreenPassProbe
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly CultureInfo Ru = new CultureInfo("ru-RU");
    static readonly CultureInfo En = new CultureInfo("en-US");
    static int defects, rig;
    static string tmp;
    static StringWriter errors = new StringWriter();

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        tmp = Path.Combine(Path.GetTempPath(), "p316probe");
        foreach (string a in args)
        {
            if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
            else { Console.WriteLine("неизвестный ключ: " + a); return 5; }
        }
        Directory.CreateDirectory(tmp);
        Console.WriteLine(ProbeTargetFramework.Describe());
        string exe = typeof(MainForm).Assembly.Location;
        Console.WriteLine("приложение: " + exe + "  ("
            + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
        Thread.CurrentThread.CurrentCulture = Inv;
        Application.EnableVisualStyles();
        // Сообщения AppUi без окон идут в поток ошибок — перехватываем, чтобы назвать.
        TextWriter realErr = Console.Error;
        Console.SetError(errors);
        try
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            Section("ресурсы");
            ResourceChecks();
            Section("свойства окон");
            Dialogs();
            Section("редактор наборов изотопов");
            NuclideSets();
            Section("настройки");
            Preferences();
            Section("конец набора: звук");
            CompletionSound();
            Section("конфигурация прибора");
            // Реестры приборов в приложении заводит MainForm; без них конструктор окна
            // настроек прибора падает на `DeviceType.DeviceTypeList` (как в DeviceTabProbe).
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            DeviceConfig(Ru);
            DeviceConfig(En);
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

    // ------------------------------------------------------------------ ресурсы
    static void ResourceChecks()
    {
        ResourceManager app = BecquerelMonitor.Properties.Resources.ResourceManager;

        string l4 = Res(typeof(InputDeviceForm), "label4.Text", Ru);
        Verdict("Н7", "InputDeviceForm.label4 (ru)", l4, HasCyr(l4));

        string sr = Res(typeof(AudioInputDeviceForm), "label2.Text", Ru);
        bool srOk = sr != null && !sr.Contains("импульс");
        Verdict("Н8", "AudioInputDeviceForm.label2 (ru, en «Sampling Rate»)", sr, srOk);
        // Помещается ли подпись до поля выбора частоты (comboBox2, x = 135): подпись
        // AutoSize, начинается в x = 16, шрифт формы.
        using (Font f = new Font("Microsoft Sans Serif", 8.25f))
        {
            int w = TextRenderer.MeasureText(sr ?? "", f).Width;
            Verdict("Н8", "ширина подписи ru, px (до поля x=135 от x=16 — не больше 117)", w.ToString(Inv), 16 + w <= 135);
        }

        string l50ru = Res(typeof(DeviceConfigForm), "label50.Text", Ru), l51ru = Res(typeof(DeviceConfigForm), "label51.Text", Ru);
        string l50en = Res(typeof(DeviceConfigForm), "label50.Text", Inv), l51en = Res(typeof(DeviceConfigForm), "label51.Text", Inv);
        Console.WriteLine("Н10\tlabel50 (y=115, у numericUpDown14 = Min_FWHM_Tol): en «{0}», ru «{1}»", l50en, l50ru);
        Console.WriteLine("Н10\tlabel51 (y=141, у numericUpDown15 = Max_FWHM_Tol): en «{0}», ru «{1}»", l51en, l51ru);
        Verdict("Н10", "ru label50 — «Мин.», label51 — «Макс.»", l50ru + " | " + l51ru,
                l50ru != null && l51ru != null && l50ru.StartsWith("Мин", StringComparison.Ordinal) && l51ru.StartsWith("Макс", StringComparison.Ordinal)
                && l50en.StartsWith("Min", StringComparison.Ordinal) && l51en.StartsWith("Max", StringComparison.Ordinal));

        string spec = Res(typeof(MainForm), "SpecUtilsToolStripMenuItem.Text", Inv), specRu = Res(typeof(MainForm), "SpecUtilsToolStripMenuItem.Text", Ru);
        Verdict("Н13", "меню «SpecUtils» en", spec, spec.Contains("SpecUtils") && Ellipsis(spec));
        Verdict("Н13", "меню «SpecUtils» ru", specRu, specRu.Contains("SpecUtils") && Ellipsis(specRu));
        string comb = Res(typeof(MainForm), "CombineSpectrasToolStripMenuItem.Text", Inv);
        Verdict("Н13", "меню объединения en", comb, !comb.Contains("Spectras"));
        string csv1 = Res(typeof(MainForm), "cSVFileToolStripMenuItem.Text", Ru), csv2 = Res(typeof(MainForm), "cSVEnFileToolStripMenuItem.Text", Ru);
        Verdict("Н13", "меню CSV ru (каналы)", csv1, !csv1.Contains("Channel") && !csv1.Contains("Counts"));
        Verdict("Н13", "меню CSV ru (энергия)", csv2, !csv2.Contains("Energy") && !csv2.Contains("Counts"));
        string chan = Res(typeof(ChanNumberChangeDialog), "label1.Text", Inv);
        Verdict("Н13", "ChanNumberChangeDialog.label1 en", chan, !chan.Contains("chanel"));

        string inNum = app.GetString("ERRInputNumber", Ru), inInt = app.GetString("ERRInputInteger", Ru);
        Verdict("Н14", "ERRInputNumber ru (en «Input a number.»)", inNum, inNum.StartsWith("Введите", StringComparison.Ordinal));
        Verdict("Н14", "ERRInputInteger ru (en «Input an integer.»)", inInt, inInt.StartsWith("Введите", StringComparison.Ordinal));

        string zin = Res(typeof(EnergySpectrumView), "button1.ToolTip", Ru), zout = Res(typeof(EnergySpectrumView), "button2.ToolTip", Ru);
        Verdict("Н17", "подсказка «+» ru", zin, HasCyr(zin));
        Verdict("Н17", "подсказка «−» ru", zout, HasCyr(zout));
        string zinEn = Res(typeof(EnergySpectrumView), "button1.ToolTip", Inv), zoutEn = Res(typeof(EnergySpectrumView), "button2.ToolTip", Inv);
        Verdict("Н17", "подсказки en", zinEn + " | " + zoutEn, zinEn == "Zoom in" && zoutEn == "Zoom out");

        // Ключ отказа объединения ищется по тексту — имя ключа в обеих сборках одно.
        string combineErr = FindNeutral(app, "Different number of channels in combined");
        Verdict("Н18", "отказ объединения en", combineErr, combineErr != null && !combineErr.Contains("spectrums"));
        string g1 = Res(typeof(GlobalConfigForm), "autoSaveDefaultPolicyCheckBox.Text", Inv);
        string g2 = Res(typeof(GlobalConfigForm), "importSpectrumWithEmptyConfigCheckBox.Text", Inv);
        Verdict("Н18", "настройки en: «spectrums» (попутно)", g1 + " | " + g2, g1 != null && g2 != null && !g1.Contains("spectrums") && !g2.Contains("spectrums"));
    }

    // ------------------------------------------------------------------ свойства окон
    static void Dialogs()
    {
        Thread.CurrentThread.CurrentUICulture = Ru;
        using (AboutForm about = new AboutForm())
        {
            Verdict("Н9", "AboutForm MaximizeBox/MinimizeBox", about.MaximizeBox + "/" + about.MinimizeBox, !about.MaximizeBox && !about.MinimizeBox);
        }

        Form mat = null;
        try
        {
            mat = new GeometryMaterialEditorForm((BecquerelMonitor.EfficiencyMaker.GeometryMaterialLibrary.MaterialKind)0);
            Verdict("Н11", "GeometryMaterialEditorForm.FormBorderStyle", mat.FormBorderStyle.ToString(), mat.FormBorderStyle != FormBorderStyle.Sizable);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Н11\t⛔ обвязка: окно «Вещества» не построено: " + Reason(ex));
            rig++;
        }
        finally
        {
            if (mat != null) mat.Dispose();
        }

        // Н20: Esc / Enter — настоящим ProcessDialogKey окна, показанного вне экрана.
        DialogKeys("ChanNumberChangeDialog", () => new ChanNumberChangeDialog(), "resultStatus");
        DialogKeys("SpectrumCutOffDialog", () => new SpectrumCutOffDialog(), "resultStatus");
        DialogKeys("SelectDeviceDialog", () => new SelectDeviceDialog(), null);
        // Редактор наборов: только Esc (Enter там ничего не значит — «Сохранить»
        // по Enter было бы сюрпризом).
        NuclideSetForm sets = new NuclideSetForm((MainForm)null);
        ShowOff(sets);
        bool handled = (bool)Call(sets, "ProcessDialogKey", Keys.Escape);
        Pump(50);
        Verdict("Н20", "NuclideSetForm: Esc закрывает", "обработано=" + handled + ", закрыто=" + sets.IsDisposed, sets.IsDisposed);
        if (!sets.IsDisposed) { sets.Close(); sets.Dispose(); }
    }

    static void DialogKeys(string name, Func<Form> make, string acceptedField)
    {
        Form f = make();
        Verdict("Н20", name + ": рамка", f.FormBorderStyle.ToString(), f.FormBorderStyle != FormBorderStyle.Sizable);
        ShowOff(f);
        Call(f, "ProcessDialogKey", Keys.Escape);
        Pump(50);
        Verdict("Н20", name + ": Esc закрывает", "закрыто=" + f.IsDisposed, f.IsDisposed);
        if (!f.IsDisposed) { f.Close(); f.Dispose(); }

        if (acceptedField == null)
        {
            // SelectDeviceDialog: «Применить» без конфигураций ничего не выбирает и окно
            // не закрывает — меряется только, что Enter дошёл до кнопки по умолчанию.
            Form g = make();
            Verdict("Н20", name + ": кнопка по умолчанию (Enter)", g.AcceptButton == null ? "нет" : ((Control)g.AcceptButton).Name, g.AcceptButton != null);
            g.Dispose();
            return;
        }
        Form h = make();
        // (П322, решение Amber 15.10.2026 «64…65536 (Рекомендую)») пустое поле числа каналов —
        // теперь отказ словами, окно остаётся; Enter меряется с годным числом.
        if (h is ChanNumberChangeDialog) ((TextBox)Field(h, "textBox1")).Text = "4096";
        ShowOff(h);
        Call(h, "ProcessDialogKey", Keys.Enter);
        Pump(50);
        object accepted = Field(h, acceptedField);
        Verdict("Н20", name + ": Enter нажимает «Принять»", "принято=" + accepted + ", закрыто=" + h.IsDisposed, Equals(accepted, true) && h.IsDisposed);
        if (!h.IsDisposed) { h.Close(); h.Dispose(); }
    }

    // ------------------------------------------------------------------ редактор наборов
    static void NuclideSets()
    {
        Thread.CurrentThread.CurrentUICulture = Ru;
        NuclideDefinitionManager mgr = NuclideDefinitionManager.GetInstance();
        string file = Package.GetInstance().NuclideDefinition;
        string shaBefore = Sha(file);
        if (mgr.NuclideSets.Count < 2)
        {
            Console.WriteLine("⛔ обвязка: в библиотеке каталога проб меньше двух наборов ({0})", mgr.NuclideSets.Count);
            rig++;
            return;
        }
        List<NuclideSet> openedSets = mgr.NuclideSets.ToList();
        List<string> openedNames = openedSets.Select(s => s.Name).ToList();
        NuclideSet s0 = openedSets[0], s1 = openedSets[1];
        // Выбор документа в панели поиска пиков — набор s1 (его и удалим в окне).
        mgr.ActiveSet = s1;
        NuclideDefinition outsider = mgr.NuclideDefinitions.First(d => !d.Sets.Contains(s0.Id));
        Dictionary<NuclideDefinition, Color> colors = mgr.NuclideDefinitions.Where(d => d.NuclideColor != null)
            .ToDictionary(d => d, d => d.NuclideColor.Color);
        Console.WriteLine("SETUP\tбиблиотека {0}: наборов {1}, линий {2}; набор s0 «{3}», s1 «{4}», чужая линия {5} {6}",
            file, openedSets.Count, mgr.NuclideDefinitions.Count, s0.Name, s1.Name, outsider.Name,
            outsider.Energy.ToString(Inv));

        NuclideSetForm f = new NuclideSetForm((MainForm)null);
        ShowOff(f);
        Table tNuc = (Table)Field(f, "tableNuclides");
        Table tSets = (Table)Field(f, "tableSets");
        TableModel mSets = (TableModel)Field(f, "tableModelSets");

        // Н1: набор не выбран.
        Verdict("Н1", "таблица изотопов без выбранного набора (ru)", tNuc.NoItemsText, HasCyr(tNuc.NoItemsText));
        // Н1: набор выбран, фильтр без совпадений.
        SetField(f, "selectedSet", s0);
        ((TextBox)Field(f, "textBoxFilter")).Text = "zzzz-нет-такого";
        Pump(20);
        Verdict("Н1", "набор выбран, фильтр без совпадений (ru)", tNuc.NoItemsText + " (строк " + ((TableModel)Field(f, "tableModelNuclides")).Rows.Count + ")",
                HasCyr(tNuc.NoItemsText));
        ((TextBox)Field(f, "textBoxFilter")).Text = "";
        Verdict("Н1", "таблица наборов пустая (ru)", tSets.NoItemsText, HasCyr(tSets.NoItemsText));

        // Н4: пустое и пробельное имя.
        foreach (string empty in new[] { "", "   " })
        {
            Row r0 = mSets.Rows[0];
            r0.Cells[0].Text = empty;
            Call(f, "tableSets_EditingStopped", tSets, new CellEditEventArgs(r0.Cells[0], null, tSets, 0, 0, Rectangle.Empty));
            Verdict("Н4", "имя набора «" + empty + "» → набор / ячейка", "«" + s0.Name + "» / «" + r0.Cells[0].Text + "»",
                    s0.Name == openedNames[0] && r0.Cells[0].Text == openedNames[0]);
        }
        Verdict("Н4", "пустое имя не взводит «Сохранить»", "dirty=" + Field(f, "dirty"), Equals(Field(f, "dirty"), false));

        // Правки, которые «Нет» обязано откатить.
        SetField(f, "selectedSet", s0);
        Call(f, "UpdateNuclideDefinition", outsider, true);
        Row rr = mSets.Rows[0];
        rr.Cells[0].Text = "П316-переименован";
        Call(f, "tableSets_EditingStopped", tSets, new CellEditEventArgs(rr.Cells[0], null, tSets, 0, 0, Rectangle.Empty));
        s0.HideUnknownPeaks = !s0.HideUnknownPeaks; // галка — тем же путём, что CellCheckChanged
        bool hide0 = !s0.HideUnknownPeaks;
        Call(f, "buttonAssignColor_Click", null, EventArgs.Empty);
        Call(f, "buttonAddSet_Click", null, EventArgs.Empty);
        NuclideSet added = mgr.NuclideSets[mgr.NuclideSets.Count - 1];
        // Н3: имя нового набора.
        Verdict("Н3", "имя нового набора (ru)", added.Name, added.Name.StartsWith("Новый набор ", StringComparison.Ordinal));
        SetField(f, "selectedSet", s1);
        Call(f, "buttonDeleteSet_Click", null, EventArgs.Empty);
        Console.WriteLine("SETUP\tв окне: «{0}» += {1}, s0 переименован в «{2}», s0.Hide → {3}, цвет s0, добавлен «{4}», удалён s1; ActiveSet = {5}",
            s0.Id.ToString().Substring(0, 8), outsider.Name, s0.Name, s0.HideUnknownPeaks, added.Name,
            mgr.ActiveSet == null ? "null" : mgr.ActiveSet.Name);

        // Н2: закрыть, на «Сохранить?» ответить «Нет».
        DialogAnswerer.Start();
        f.Close();
        Pump(300);
        DialogAnswerer.Stop();
        List<string> seen = DialogAnswerer.Seen();
        Console.WriteLine("SETUP\tвопросы при закрытии: {0}", seen.Count == 0 ? "нет" : string.Join(" || ", seen));
        if (seen.Count == 0)
        {
            Console.WriteLine("⛔ обвязка: вопроса «Сохранить?» не было — «Нет» не мерено");
            rig++;
        }
        Verdict("Н2", "после «Нет»: чужая линия в s0", outsider.Sets.Contains(s0.Id) ? "есть" : "нет", !outsider.Sets.Contains(s0.Id));
        Verdict("Н2", "после «Нет»: имя s0", s0.Name, s0.Name == openedNames[0]);
        Verdict("Н2", "после «Нет»: галка «скрыть неизв.» s0", s0.HideUnknownPeaks.ToString(), s0.HideUnknownPeaks == hide0);
        bool sameSets = mgr.NuclideSets.Count == openedSets.Count
            && mgr.NuclideSets.Zip(openedSets, (a, b) => ReferenceEquals(a, b)).All(x => x);
        Verdict("Н2", "после «Нет»: наборы — те же объекты в том же порядке",
            mgr.NuclideSets.Count.ToString(Inv) + " из " + openedSets.Count.ToString(Inv), sameSets);
        Verdict("Н2", "после «Нет»: выбор панели (ActiveSet) — s1", mgr.ActiveSet == null ? "null" : mgr.ActiveSet.Name, ReferenceEquals(mgr.ActiveSet, s1));
        int recolored = colors.Count(kv => kv.Key.NuclideColor != null && kv.Key.NuclideColor.Color != kv.Value);
        Verdict("Н2", "после «Нет»: перекрашенных линий", recolored.ToString(Inv), recolored == 0);
        string shaAfter = Sha(file);
        Verdict("Н2", "«Нет» не пишет библиотеку (sha256 до = после)", shaBefore.Substring(0, 12) + " / " + shaAfter.Substring(0, 12), shaBefore == shaAfter);
        mgr.ActiveSet = null;
    }

    // ------------------------------------------------------------------ настройки
    static void Preferences()
    {
        Thread.CurrentThread.CurrentUICulture = Ru;
        // Н14: нечисло в поле; «Отмена», крестик, «ОК».
        foreach (string how in new[] { "Отмена", "крестик", "ОК" })
        {
            GlobalConfigForm g = new GlobalConfigForm();
            g.LoadFormContents(GlobalConfigManager.GetInstance().GlobalConfig);
            ShowOff(g);
            DoubleTextBox box = Find<DoubleTextBox>(g).FirstOrDefault(b => b.Enabled);
            if (box == null)
            {
                Console.WriteLine("⛔ обвязка: в настройках нет поля DoubleTextBox");
                rig++;
                g.Dispose();
                return;
            }
            TabPage page = Parent<TabPage>(box);
            if (page != null) ((TabControl)page.Parent).SelectedTab = page;
            g.ActiveControl = box;
            box.Text = "abc";
            int before = Count(errors.ToString(), BecquerelMonitor.Properties.Resources.ERRInputNumber);
            if (how == "Отмена") ((Button)Field(g, "button2")).PerformClick();
            else if (how == "ОК") ((Button)Field(g, "button1")).PerformClick();
            else g.Close();
            Pump(50);
            int reports = Count(errors.ToString(), BecquerelMonitor.Properties.Resources.ERRInputNumber) - before;
            string got = "закрыто=" + g.IsDisposed + ", сообщений «" + BecquerelMonitor.Properties.Resources.ERRInputNumber + "»: " + reports.ToString(Inv);
            if (how == "ОК")
                Verdict("Н14", "поле «" + box.Name + "» = abc, «ОК» по-прежнему НЕ принимает", got, !g.IsDisposed && reports == 1);
            else
                Verdict("Н14", "поле «" + box.Name + "» = abc, " + how + " закрывает без проверки", got, g.IsDisposed && reports == 0);
            if (!g.IsDisposed)
            {
                box.Text = "1";
                g.Close();
                g.Dispose();
            }
        }

        // Н21: «Play» — нет файла; файл есть, но не WAV.
        string missing = Path.Combine(tmp, "nope.wav");
        if (File.Exists(missing)) File.Delete(missing);
        string notWav = Path.Combine(tmp, "not-a-wave.wav");
        File.WriteAllText(notWav, "это не RIFF/WAVE", Encoding.UTF8);
        foreach (string path in new[] { missing, notWav })
        {
            // «Play» — щелчком настоящей кнопки (button7 вкладки «Звук»), а не именем
            // обработчика: имя обработчика в литерале пробы сторож `check_headless`
            // относит ко ВСЕМ одноимённым методам дерева (у DCEnergyCalibrationView
            // есть свой button7_Click с окном).
            GlobalConfigForm g = new GlobalConfigForm();
            ShowOff(g);
            ((TextBox)Field(g, "textBox6")).Text = path;
            Button play = (Button)Field(g, "button7");
            TabPage soundPage = Parent<TabPage>(play);
            if (soundPage != null) ((TabControl)soundPage.Parent).SelectedTab = soundPage;
            Pump(20);
            int errBefore = errors.ToString().Length;
            string thrown = null;
            try { play.PerformClick(); }
            catch (Exception ex) { thrown = Reason(ex); }
            string said = errors.ToString().Substring(errBefore).Trim();
            Verdict("Н21", "«Play» с " + Path.GetFileName(path) + (File.Exists(path) ? " (не WAV)" : " (нет файла)"),
                thrown != null ? "ИСКЛЮЧЕНИЕ: " + thrown : "без исключения; сказано: " + (said.Length == 0 ? "—" : said.Replace("\r", " ").Replace("\n", " ")),
                thrown == null && said.Length > 0);
            g.Dispose();
        }
    }

    // ------------------------------------------------------------------ конец набора
    static void CompletionSound()
    {
        string missing = Path.Combine(tmp, "nope.wav");
        string notWav = Path.Combine(tmp, "not-a-wave.wav");
        MethodInfo helper = typeof(MeasurementController).GetMethod("PlayCompletionSound", Any);
        foreach (string path in new[] { missing, notWav })
        {
            string thrown = null;
            try
            {
                if (helper != null) helper.Invoke(null, new object[] { path });
                else new System.Media.SoundPlayer(path).Play(); // прежний голый вызов (сборка ДО)
            }
            catch (Exception ex) { thrown = Reason(ex); }
            Verdict("Н21", "конец набора, звук " + Path.GetFileName(path) + (helper == null ? " [помощника нет — прежний вызов]" : ""),
                thrown == null ? "без исключения" : "ИСКЛЮЧЕНИЕ: " + thrown, thrown == null);
        }
    }

    // ------------------------------------------------------------------ конфигурация прибора
    static void DeviceConfig(CultureInfo ui)
    {
        Thread.CurrentThread.CurrentUICulture = ui;
        string lang = ui.TwoLetterISOLanguageName;
        DeviceConfigForm f = new DeviceConfigForm();
        ShowOff(f);
        TableModel list = (TableModel)Field(f, "tableModel1");
        if (list.Rows.Count == 0)
        {
            Console.WriteLine("⛔ обвязка: в каталоге проб нет конфигураций прибора");
            rig++;
            f.Dispose();
            return;
        }
        list.Selections.SelectCell(0, 0);
        Pump(100);
        DeviceConfigInfo cfg = (DeviceConfigInfo)Field(f, "activeDeviceConfig");
        if (cfg == null)
        {
            Console.WriteLine("⛔ обвязка: конфигурация не стала активной");
            rig++;
            f.Dispose();
            return;
        }

        // Н23: вкладки помещаются в одну строку и ничего не обрезано.
        TabControl tc = (TabControl)Field(f, "tabControl1");
        Rectangle last = tc.GetTabRect(tc.TabCount - 1);
        int clipped = 0;
        var clippedNames = new List<string>();
        foreach (TabPage p in tc.TabPages)
        {
            foreach (Control c in p.Controls)
            {
                if (c.Visible && c.Bottom > p.ClientSize.Height + p.AutoScrollPosition.Y && !p.AutoScroll)
                {
                    clipped++;
                    clippedNames.Add(p.Text + ":" + c.Name);
                }
            }
        }
        string tabs = string.Join(" · ", tc.TabPages.Cast<TabPage>().Select(p => p.Text));
        Verdict("Н23", lang + ": вкладки видны все (правый край последней ≤ ширины)",
            "[" + tabs + "] край " + last.Right.ToString(Inv) + " / ширина " + tc.Width.ToString(Inv) + ", рядов " + tc.RowCount.ToString(Inv),
            last.Right <= tc.Width && last.Width > 0);
        Verdict("Н23", lang + ": содержимое вкладок не обрезано снизу", clipped == 0 ? "0" : clipped.ToString(Inv) + " " + string.Join(", ", clippedNames.Take(6)), clipped == 0);

        if (ui.Name == Ru.Name)
        {
            // Н10 — живая форма: подпись рядом с полем «Мин.».
            Label l50 = (Label)Field(f, "label50");
            Control n14 = (Control)Field(f, "numericUpDown14");
            Verdict("Н10", "ru: подпись у поля Min_FWHM_Tol (y=" + n14.Top.ToString(Inv) + ")", l50.Text + " (y=" + l50.Top.ToString(Inv) + ")",
                    l50.Text.StartsWith("Мин", StringComparison.Ordinal) && Math.Abs(l50.Top - n14.Top) < 8);

            // Н12: каналы и лимит времени.
            TextBox ch = (TextBox)Field(f, "integerTextBox1");
            TextBox tm = (TextBox)Field(f, "doubleTextBox5");
            string ch0 = ch.Text, tm0 = tm.Text;
            int cfgCh0 = cfg.NumberOfChannels, cfgTm0 = cfg.DefaultMeasurementTime;
            // (П322, решение Amber 15.10.2026 «0 — отказ (Рекомендую)») лимит «0» —
            // теперь отказ, как отрицательный; прежде он стоял в контроле «принимается».
            foreach (string[] pair in new[] { new[] { "-5", tm0 }, new[] { "0", tm0 }, new[] { ch0, "-100" }, new[] { ch0, "0" } })
            {
                ch.Text = pair[0];
                tm.Text = pair[1];
                int errBefore = errors.ToString().Length;
                bool ok = (bool)Call(f, "SaveFormContents", cfg);
                string said = errors.ToString().Substring(errBefore).Trim().Replace("\r", " ").Replace("\n", " ");
                Verdict("Н12", "каналы «" + pair[0] + "», лимит «" + pair[1] + "» → запись",
                    "принято=" + ok + ", в конфигурации " + cfg.NumberOfChannels.ToString(Inv) + " кан. / " + cfg.DefaultMeasurementTime.ToString(Inv) + " с; сказано: " + (said.Length == 0 ? "—" : said),
                    !ok && cfg.NumberOfChannels == cfgCh0 && cfg.DefaultMeasurementTime == cfgTm0 && said.Length > 0);
                cfg.NumberOfChannels = cfgCh0;
                cfg.DefaultMeasurementTime = cfgTm0;
            }
            // Положительный контроль: прежние числа — принимаются («0» лимита — с П322 отказ).
            foreach (string t in new[] { tm0 })
            {
                ch.Text = ch0;
                tm.Text = t;
                bool ok = (bool)Call(f, "SaveFormContents", cfg);
                Verdict("Н12", "контроль: каналы «" + ch0 + "», лимит «" + t + "» → запись", "принято=" + ok, ok);
            }
            tm.Text = tm0;

            // Н19: «New» в опорах автокалибровки и правка имени.
            TableModel peaks = (TableModel)Field(f, "tableModel3");
            Table peaksTable = (Table)Field(f, "table3");
            Call(f, "button10_Click", null, EventArgs.Empty); // пустая строка ("", 0, 10)
            Row good = new Row();
            good.Cells.Add(new Cell("Cs-137"));
            good.Cells.Add(new Cell(661.657m));
            good.Cells.Add(new Cell(5m));
            peaks.Rows.Add(good);
            bool saved = (bool)Call(f, "SaveFormContents", cfg);
            List<TargetPeak> tp = cfg.StabilizerConfig.TargetPeaks;
            string tpText = string.Join("; ", tp.Select(p => "«" + p.Nuclide + "» " + p.Energy.ToString(Inv)));
            Verdict("Н19", "опоры после «New» и записи: нет опоры с E ≤ 0", "принято=" + saved + ": " + tpText, saved && tp.All(p => p.Energy > 0m));
            Verdict("Н19", "контроль: опора Cs-137 661.657 записана", tpText, tp.Any(p => p.Energy == 661.657m));
            Call(f, "ResetActiveDeviceConfigDirty");
            Row named = peaks.Rows[peaks.Rows.Count - 1];
            named.Cells[0].Text = "Cs137";
            Call(f, "table3_EditingStopped", peaksTable, new CellEditEventArgs(named.Cells[0], null, peaksTable, named.Index, 0, Rectangle.Empty));
            Verdict("Н19", "правка имени нуклида (столбец 0) взводит «Сохранить»", "Dirty=" + cfg.Dirty, cfg.Dirty);
        }

        DialogAnswerer.Start();
        f.Close();
        Pump(200);
        DialogAnswerer.Stop();
        List<string> seen = DialogAnswerer.Seen();
        if (seen.Count > 0) Console.WriteLine("SETUP\t{0}: вопросы при закрытии конфигурации: {1}", lang, string.Join(" || ", seen));
        if (!f.IsDisposed) f.Dispose();
    }

    // ------------------------------------------------------------------ помощники
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

    static string Res(Type t, string key, CultureInfo c)
    {
        return new ComponentResourceManager(t).GetString(key, c);
    }

    static string FindNeutral(ResourceManager rm, string start)
    {
        ResourceSet set = rm.GetResourceSet(CultureInfo.InvariantCulture, true, true);
        foreach (System.Collections.DictionaryEntry e in set)
        {
            string v = e.Value as string;
            if (v != null && v.StartsWith(start, StringComparison.Ordinal)) return v;
        }
        return null;
    }

    /// <summary>Многоточие пункта меню — три точки, как у остальных пунктов MainForm («Экспорт...»), не две.</summary>
    static bool Ellipsis(string s)
    {
        return s.EndsWith("...", StringComparison.Ordinal) && !s.EndsWith("....", StringComparison.Ordinal);
    }

    static bool HasCyr(string s)
    {
        return s != null && s.Any(ch => ch >= 'Ѐ' && ch <= 'ӿ');
    }

    static int Count(string hay, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    static Type FindType(string suffix)
    {
        return typeof(MainForm).Assembly.GetTypes().First(t => t.FullName.EndsWith(suffix, StringComparison.Ordinal));
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

    static IEnumerable<T> Find<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            if (c is T) yield return (T)c;
            foreach (T x in Find<T>(c)) yield return x;
        }
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

    static string Reason(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.GetType().Name + ": " + ex.Message;
    }

    /// <summary>
    /// Отвечает «Нет» на вопросы приложения (окна класса #32770 своего процесса):
    /// WM_COMMAND IDNO; если окно не ушло — IDCANCEL, затем IDOK. Записывает тексты.
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
        static readonly Dictionary<IntPtr, int> tries = new Dictionary<IntPtr, int>();
        static readonly List<string> seen = new List<string>();

        public static void Start()
        {
            lock (seen) { seen.Clear(); tries.Clear(); }
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
            int[] answers = { 7, 2, 1 }; // IDNO, IDCANCEL, IDOK
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
