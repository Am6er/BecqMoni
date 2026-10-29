using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// П292 (14.10.2026): строка подробностей окна «Матрица отклика» — «Data: N KB, file: M KB» —
// у посчитанной, но ещё НЕ сохранённой матрицы называла размер файла ПРЕЖНЕЙ матрицы на
// складе (находка П291, замер: склад 65 252 байта, новая матрица — 74 КБ данных).
// Решение Amber 14.10.2026 вопросником, дословно: «“file: not saved” / “не сохранена”
// (Рекомендую)». Описание выбранного: «Пока матрица не сохранена — слова вместо числа; после
// «Сохранить» — размер нового файла. Один ключ en/ru.»
//
//   matrixwindowdetailsprobep292 --geometry=<.in> [--ui=en|ru] [--out=<файл>]
//
// Окно НЕ показывается: форма строится, «Посчитать» / «Отмена» / «Сохранить» зовутся
// отражением, продолжение `await` прокачивается `Application.DoEvents` (приём
// `MatrixWindowProbeP291`). Склад и временный файл — в каталоге пробы (портативная
// раскладка: `Package.DeviceDir` = `<каталог exe>\config\device`), конфиг Amber не тронут.
//
// Ожидаемое строится ОТДЕЛЬНО от кода окна — из ресурсов и самих матриц тем же порядком
// аргументов, что у прежнего `Describe` (приложение: `DescribeNodes`, `DescribeFingerprint`
// не менялись). Новый ключ читается через `ResourceManager`: проба собирается и против exe
// БЕЗ него — это контроль «до».
//
// Код возврата: 0 — все сошлись; 1 — есть расхождения; 2 — ключи или среда.
static class MatrixWindowDetailsProbeP292
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;
    static string ui = "en";

    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;

    /// <summary>Имя нового ключа — формат подробностей несохранённой матрицы.</summary>
    const string NotSavedKey = "ResponseMatrixDetailsNotSaved";

    const decimal QuickHistories = 20000;
    const decimal LongHistories = 5000000;

    sealed class Win : IDisposable
    {
        public readonly ResponseMatrixForm Form;
        public readonly EfficiencyConfigData Config;

        public Win(EfficiencyConfigData config, int nodes)
        {
            this.Config = config;
            this.Form = new ResponseMatrixForm(config);
            this.Box("minEnergyBox").Value = 30;
            this.Box("maxEnergyBox").Value = 3000;
            this.Box("nodesBox").Value = nodes;
            this.Box("binBox").Value = 2;
            this.Box("historiesBox").Value = QuickHistories;
            this.Box("threadsBox").Value = 4;
        }

        object Field(string name)
        {
            FieldInfo f = typeof(ResponseMatrixForm).GetField(name, F);
            if (f == null) throw new MissingFieldException("ResponseMatrixForm", name);
            return f.GetValue(this.Form);
        }

        public NumericUpDown Box(string name) { return (NumericUpDown)this.Field(name); }
        public ResponseMatrix Computed { get { return (ResponseMatrix)this.Field("computed"); } }
        public string Details { get { return (string)this.Field("detailsText"); } }
        public bool SaveEnabled { get { return ((Button)this.Field("saveButton")).Enabled; } }
        public bool Busy { get { return (bool)this.Field("busy"); } }
        public Panel DetailsPanel { get { return (Panel)this.Field("detailsPanel"); } }

        void Call(string method)
        {
            typeof(ResponseMatrixForm).GetMethod(method, F).Invoke(this.Form, new object[] { null, EventArgs.Empty });
        }

        public void Save() { this.Call("SaveClick"); }

        public string Nodes(ResponseMatrix m)
        {
            return (string)typeof(ResponseMatrixForm).GetMethod("DescribeNodes", F).Invoke(this.Form, new object[] { m });
        }

        /// <summary>«Посчитать» и дождаться конца; <paramref name="cancelAt"/> ≥ 0 — «Отмена» через столько секунд.</summary>
        public bool Compute(double cancelAt)
        {
            // (`AMBER268`) Контекст окна перед каждым счётом: первый `DoEvents` без
            // `Application.Run` снимает его с потока, и второй счёт шёл бы отчётами
            // с потоков пула (подробно — `MatrixWindowProbeP291.EnsureWinFormsContext`).
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            this.Call("ComputeClick");
            if (!this.Busy)
            {
                Say("   ⚠ «Посчитать» не начало счёт");
                return false;
            }

            bool cancelledWhileBusy = true;
            if (cancelAt >= 0)
            {
                Pump(() => !this.Busy, cancelAt);
                cancelledWhileBusy = this.Busy;
                if (cancelledWhileBusy) this.Call("CancelClick");
            }

            bool done = Pump(() => !this.Busy, 900);
            if (!done) Say("   ⚠ счёт не кончился за 900 с");
            return done && cancelledWhileBusy;
        }

        public void Dispose() { this.Form.Dispose(); }
    }

    static bool Pump(Func<bool> until, double seconds)
    {
        var sw = Stopwatch.StartNew();
        while (!until())
        {
            if (sw.Elapsed.TotalSeconds > seconds) return false;
            Application.DoEvents();
            Thread.Sleep(5);
        }

        Application.DoEvents();
        return true;
    }

    static void Say(string s)
    {
        Console.WriteLine(s);
        Log.AppendLine(s);
    }

    static void Check(bool ok, string what)
    {
        if (!ok) failures++;
        Say((ok ? "   ✓ " : "   ✗ ") + what);
    }

    static string Q(string s) { return s == null ? "<null>" : "«" + s.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ") + "»"; }

    static System.Collections.Generic.List<ROIEfficiencyData> Amber267Curve()
    {
        return new System.Collections.Generic.List<ROIEfficiencyData>
        {
            new ROIEfficiencyData { Energy = 5, Efficiency = 0.5, ErrorPercent = 1 },
            new ROIEfficiencyData { Energy = 662, Efficiency = 0.1, ErrorPercent = 1 },
            new ROIEfficiencyData { Energy = 3000, Efficiency = 0.02, ErrorPercent = 1 },
        };
    }

    static string geometryPath;

    static EfficiencyConfigData NewConfig(string guid)
    {
        GeometryModel g = GeometryModel.Load(geometryPath);
        // (`AMBER267`) Без посчитанной кривой окно «Посчитать» не даёт; края
        // кривой — умолчания полей окна (5–3000 кэВ), чтобы диапазон счёта не сдвинулся.
        return new EfficiencyConfigData("проба П292") { Guid = guid, Geometry = g, Curve = Amber267Curve() };
    }

    static string Res(string key)
    {
        return Resources.ResourceManager.GetString(key, Resources.Culture);
    }

    static string[] Lines(string text)
    {
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    }

    /// <summary>Первые пять строк `Describe`: четыре строки формата и строка отпечатка тела.</summary>
    static string Head(string details)
    {
        string[] l = Lines(details);
        var sb = new StringBuilder();
        for (int i = 0; i < 5 && i < l.Length; i++) { if (i > 0) sb.Append('\n'); sb.Append(l[i]); }
        return sb.ToString();
    }

    /// <summary>
    /// Ожидаемые пять строк `Describe` — из ресурсов и матрицы, не кодом окна.
    /// <paramref name="fileBytes"/> &lt; 0 — несохранённая: берётся формат нового ключа.
    /// </summary>
    static string Expected(Win w, ResponseMatrix m, long fileBytes)
    {
        string format = fileBytes < 0 ? Res(NotSavedKey) : Res("ResponseMatrixDetails");
        if (format == null) return null;
        string fp = (string)typeof(ResponseMatrixForm).GetMethod("DescribeFingerprint", S).Invoke(null, new object[] { m });
        string text = string.Format(CultureInfo.InvariantCulture, format,
                                    w.Nodes(m), m.Energies[0], m.Energies[m.NodeCount - 1], m.BinKev, m.Histories,
                                    m.DataBytes / 1024.0, Math.Max(fileBytes, 0) / 1024.0,
                                    m.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                                    m.BuildSeconds) + fp;
        return Head(text);
    }

    static string Kb(long bytes)
    {
        return (bytes / 1024.0).ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>Третья строка подробностей — «Data: N KB, file: …».</summary>
    static string Line3(string details)
    {
        string[] l = Lines(details);
        return l.Length > 2 ? l[2] : null;
    }

    static string StorePath(string guid) { return ResponseMatrixStore.PathOf(guid); }
    static string PendingPath(string guid) { return ResponseMatrixStore.PathOf(guid, ResponseMatrixSource.Pending); }

    /// <summary>Положить матрицу на склад КОПИИ (как после сохранения конфигурации прибора).</summary>
    static long MakeStore(string guid, int nodes)
    {
        using (var w = new Win(NewConfig(guid), nodes))
        {
            w.Compute(-1);
            if (w.Computed == null) throw new InvalidOperationException("счёт для склада не дал матрицы");
            w.Save();
        }

        if (!File.Exists(PendingPath(guid))) throw new InvalidOperationException("«Сохранить» не записала временный файл");
        File.Move(PendingPath(guid), StorePath(guid));
        return new FileInfo(StorePath(guid)).Length;
    }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--ui=", StringComparison.Ordinal)) ui = a.Substring(5);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else
            {
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }
        }

        if (geometryPath == null || !File.Exists(geometryPath) || (ui != "en" && ui != "ru"))
        {
            Console.WriteLine("нужно --geometry=<файл .in> и --ui=en|ru");
            return 2;
        }

        var culture = new CultureInfo(ui == "ru" ? "ru-RU" : "en-US");
        Thread.CurrentThread.CurrentUICulture = culture;
        Resources.Culture = culture;

        Say("П292 — окно матрицы: размер файла у несохранённой матрицы");
        Say("exe:       " + typeof(ResponseMatrixForm).Assembly.Location);
        Say("культура:  " + culture.Name + "; ключ " + NotSavedKey + " " + (Res(NotSavedKey) == null ? "ОТСУТСТВУЕТ" : "есть"));
        Say("геометрия: " + geometryPath);
        Say("склад:     " + ResponseMatrixStore.Directory);
        Application.EnableVisualStyles();
        GlobalConfigManager.GetInstance();
        string run = "p292-" + ui + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        try
        {
            S0Resources();
            S1StoreWithoutRecompute(run + "-s1");
            S2UnsavedOverStore(run + "-s2");
            S3UnsavedNoStore(run + "-s3");
            S4CancelKeepsState(run + "-s4");
        }
        catch (Exception ex)
        {
            failures++;
            Say("⛔ проба оборвалась: " + ex);
        }

        Say("");
        Say(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
        if (outPath != null) File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Ресурс: новый ключ есть, согласован с прежним форматом, слова на языке окна.</summary>
    static void S0Resources()
    {
        Say("");
        Say("S0 · ресурсы: ключ " + NotSavedKey);
        string old = Res("ResponseMatrixDetails");
        string neu = Res(NotSavedKey);
        Check(neu != null, "ключ есть в культуре " + Resources.Culture.Name);
        if (neu == null) return;
        Say("   формат нового ключа: " + Q(neu));
        string[] o = Lines(old), n = Lines(neu);
        Check(o.Length == 4 && n.Length == 4, "обе строки формата — по четыре строки");
        if (o.Length != 4 || n.Length != 4) return;
        Check(o[0] == n[0] && o[1] == n[1] && o[3] == n[3],
              "строки 1, 2 и 4 нового формата — слово в слово прежние (не разъедутся)");
        int cutOld = o[2].LastIndexOf(',');
        int cutNew = n[2].LastIndexOf(',');
        Check(cutOld > 0 && cutNew == cutOld && o[2].Substring(0, cutOld) == n[2].Substring(0, cutNew),
              "третья строка до «, файл» — прежняя (данные: " + Q(o[2].Substring(0, Math.Max(cutOld, 0))) + ")");
        Check(neu.IndexOf("{6", StringComparison.Ordinal) < 0, "числа файла ({6}) в новом формате нет — слова вместо числа");
        string tail = cutNew > 0 ? n[2].Substring(cutNew + 1).Trim() : "";
        string want = ui == "ru" ? "файл: не сохранён" : "file: not saved";
        Check(tail == want, "хвост третьей строки — «" + want + "» (стоит «" + tail + "»)");
        Say("   прежний формат без изменений: " + Q(old));
    }

    /// <summary>Склад без пересчёта — прежний текст побайтно (не сломано).</summary>
    static void S1StoreWithoutRecompute(string guid)
    {
        Say("");
        Say("S1 · на складе матрица, окно открыто и НЕ пересчитывалось — прежний текст, размер файла склада");
        long storeLen = MakeStore(guid, 8);
        using (var w = new Win(NewConfig(guid), 8))
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix loaded = ResponseMatrixStore.Load(guid, ResponseMatrixSource.Store, out refusal, out fileFormat);
            string want = Expected(w, loaded, storeLen);
            Say("   файл склада " + storeLen + " байт; строка 3: " + Q(Line3(w.Details)));
            Check(Head(w.Details) == want, "первые пять строк подробностей == прежний формат с размером склада (" + Kb(storeLen) + " KB)");
            Check(Line3(w.Details).IndexOf(Kb(storeLen), StringComparison.Ordinal) >= 0 && w.Computed == null, "размер склада назван; несохранённой матрицы в окне нет");
        }
    }

    /// <summary>Главный случай находки П291: несохранённая матрица при ДРУГОЙ матрице на складе.</summary>
    static void S2UnsavedOverStore(string guid)
    {
        Say("");
        Say("S2 · на складе матрица (8 узлов), в окне посчитана НОВАЯ (12 узлов), ещё не сохранена");
        long storeLen = MakeStore(guid, 8);
        using (var w = new Win(NewConfig(guid), 12))
        {
            w.Compute(-1);
            ResponseMatrix m = w.Computed;
            if (m == null) throw new InvalidOperationException("счёт не дал матрицы");
            string line3 = Line3(w.Details);
            Say("   склад " + storeLen + " байт (" + Kb(storeLen) + " KB); данные новой матрицы " + Kb(m.DataBytes) + " KB");
            Say("   строка 3: " + Q(line3));
            bool oldDefect = line3 != null && line3.IndexOf(Kb(storeLen) + " ", StringComparison.Ordinal) >= 0
                             && (ui == "ru" ? line3.EndsWith("файл: " + Kb(storeLen) + " КБ", StringComparison.Ordinal)
                                            : line3.EndsWith("file: " + Kb(storeLen) + " KB", StringComparison.Ordinal));
            Say("   КОНТРОЛЬ (до правки — ДА, после — НЕТ): у несохранённой названа цифра файла СКЛАДА, другой матрицы: "
                + (oldDefect ? "ДА" : "нет"));
            string literal = ui == "ru"
                ? "Данных: " + Kb(m.DataBytes) + " КБ, файл: не сохранён"
                : "Data: " + Kb(m.DataBytes) + " KB, file: not saved";
            Check(line3 == literal, "строка 3 — «" + literal + "»");
            Check(!oldDefect, "цифры файла склада в строке нет");
            string want = Expected(w, m, -1);
            Check(want != null && Head(w.Details) == want, "первые пять строк == формат нового ключа с матрицей окна");
            Check(w.SaveEnabled, "«Сохранить» доступна");
            CheckOneRow(w);

            // «Сохранить» — размер НОВОГО файла (временного), а не склада.
            w.Save();
            long newLen = new FileInfo(PendingPath(guid)).Length;
            string after = Line3(w.Details);
            Say("   после «Сохранить»: новый файл " + newLen + " байт (" + Kb(newLen) + " KB); склад " + storeLen + " байт (" + Kb(storeLen) + " KB)");
            Say("   строка 3: " + Q(after));
            string literalSaved = ui == "ru"
                ? "Данных: " + Kb(m.DataBytes) + " КБ, файл: " + Kb(newLen) + " КБ"
                : "Data: " + Kb(m.DataBytes) + " KB, file: " + Kb(newLen) + " KB";
            Check(Kb(newLen) != Kb(storeLen), "размеры нового файла и склада различаются в КБ (контроль различимости: "
                                              + Kb(newLen) + " ≠ " + Kb(storeLen) + ")");
            Check(after == literalSaved, "строка 3 после «Сохранить» — «" + literalSaved + "» (размер НОВОГО файла)");
            Check(Head(w.Details) == Expected(w, m, newLen), "первые пять строк == прежний формат с размером нового файла");
            Check(!w.SaveEnabled, "«Сохранить» погашена");
        }
    }

    /// <summary>Склада нет совсем: «0 KB» у несохранённой — тоже ложь.</summary>
    static void S3UnsavedNoStore(string guid)
    {
        Say("");
        Say("S3 · склада нет, в окне посчитана матрица, не сохранена (раньше — «file: 0 KB»)");
        using (var w = new Win(NewConfig(guid), 8))
        {
            Check(!File.Exists(StorePath(guid)) && !File.Exists(PendingPath(guid)), "файлов матрицы на диске нет");
            w.Compute(-1);
            ResponseMatrix m = w.Computed;
            string line3 = Line3(w.Details);
            Say("   строка 3: " + Q(line3));
            string literal = ui == "ru"
                ? "Данных: " + Kb(m.DataBytes) + " КБ, файл: не сохранён"
                : "Data: " + Kb(m.DataBytes) + " KB, file: not saved";
            Check(line3 == literal, "строка 3 — «" + literal + "»");
            w.Save();
            long newLen = new FileInfo(PendingPath(guid)).Length;
            string literalSaved = ui == "ru"
                ? "Данных: " + Kb(m.DataBytes) + " КБ, файл: " + Kb(newLen) + " КБ"
                : "Data: " + Kb(m.DataBytes) + " KB, file: " + Kb(newLen) + " KB";
            Say("   после «Сохранить»: " + Q(Line3(w.Details)) + " (файл " + newLen + " байт)");
            Check(Line3(w.Details) == literalSaved, "после «Сохранить» — размер нового файла: «" + literalSaved + "»");
        }
    }

    /// <summary>Отмена пересчёта возвращает прежний результат — подробности по тому, сохранён ли он.</summary>
    static void S4CancelKeepsState(string guid)
    {
        Say("");
        Say("S4 · отмена пересчёта: подробности — по тому, сохранён ли возвращённый результат");
        MakeStore(guid, 8);

        // (а) возвращённый НЕ сохранён → «not saved» остаётся
        using (var w = new Win(NewConfig(guid), 10))
        {
            w.Compute(-1);
            ResponseMatrix a = w.Computed;
            string d1 = w.Details;
            w.Box("historiesBox").Value = LongHistories;
            bool measured = w.Compute(1.5);
            Check(measured, "(а) отмена пришла ПОСРЕДИ пересчёта (замер состоялся)");
            Check(object.ReferenceEquals(w.Computed, a), "(а) возвращена прежняя матрица");
            Check(w.Details == d1 && Line3(w.Details).IndexOf(ui == "ru" ? "не сохранён" : "not saved", StringComparison.Ordinal) >= 0,
                  "(а) подробности прежние, у несохранённой — «" + (ui == "ru" ? "не сохранён" : "not saved") + "»: " + Q(Line3(w.Details)));
            Check(w.SaveEnabled, "(а) «Сохранить» доступна");
        }

        // (б) возвращённый СОХРАНЁН → размер файла остаётся числом
        using (var w = new Win(NewConfig(guid), 10))
        {
            w.Compute(-1);
            ResponseMatrix a = w.Computed;
            w.Save();
            long newLen = new FileInfo(PendingPath(guid)).Length;
            string d1 = w.Details;
            w.Box("historiesBox").Value = LongHistories;
            bool measured = w.Compute(1.5);
            Check(measured, "(б) отмена пришла ПОСРЕДИ пересчёта (замер состоялся)");
            string literalSaved = ui == "ru" ? "файл: " + Kb(newLen) + " КБ" : "file: " + Kb(newLen) + " KB";
            Check(object.ReferenceEquals(w.Computed, a) && w.Details == d1 && Line3(w.Details).EndsWith(literalSaved, StringComparison.Ordinal),
                  "(б) сохранённая возвращена, подробности прежние, размер файла — число: " + Q(Line3(w.Details)));
            Check(!w.SaveEnabled, "(б) «Сохранить» погашена");

            // (в) сохранённая A → удачный пересчёт B (не сохранена) → отмена следующего:
            //     возвращается B, файл на диске — A, а строка — «not saved»; «Сохранить» → размер B.
            w.Box("historiesBox").Value = QuickHistories;
            w.Box("nodesBox").Value = 14;
            w.Compute(-1);
            ResponseMatrix b = w.Computed;
            Check(!object.ReferenceEquals(a, b), "(в) удачный пересчёт дал НОВУЮ матрицу B");
            string literalNot = ui == "ru" ? "файл: не сохранён" : "file: not saved";
            Check(Line3(w.Details).EndsWith(literalNot, StringComparison.Ordinal),
                  "(в) B не сохранена, на диске файл A (" + Kb(newLen) + " KB) — строка 3 «" + literalNot + "»: " + Q(Line3(w.Details)));
            string d2 = w.Details;
            w.Box("historiesBox").Value = LongHistories;
            measured = w.Compute(1.5);
            Check(measured && object.ReferenceEquals(w.Computed, b) && w.Details == d2
                  && Line3(w.Details).EndsWith(literalNot, StringComparison.Ordinal),
                  "(в) отмена: возвращена B, подробности прежние, «" + literalNot + "»: " + Q(Line3(w.Details)));
            w.Save();
            long lenB = new FileInfo(PendingPath(guid)).Length;
            string literalB = ui == "ru" ? "файл: " + Kb(lenB) + " КБ" : "file: " + Kb(lenB) + " KB";
            Check(Line3(w.Details).EndsWith(literalB, StringComparison.Ordinal) && Kb(lenB) != Kb(newLen),
                  "(в) после «Сохранить» — размер файла B (" + Kb(lenB) + " KB, у A было " + Kb(newLen) + "): " + Q(Line3(w.Details)));
        }
    }

    /// <summary>Третья строка занимает одну строку панели (не переносится и не уходит за край).</summary>
    static void CheckOneRow(Win w)
    {
        var rows = w.DetailsPanel.Controls;
        if (rows.Count < 3) { Check(false, "в панели подробностей меньше трёх строк"); return; }
        Label l3 = null;
        foreach (Control c in rows) if (c.Top > 0 && c.Text == Line3(w.Details)) l3 = (Label)c;
        if (l3 == null) { Check(false, "метка третьей строки не найдена"); return; }
        Size one = TextRenderer.MeasureText(l3.Text, l3.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.WordBreak);
        Check(one.Width <= l3.Width, string.Format(CultureInfo.InvariantCulture,
              "третья строка влезает по ширине без переноса: нужно {0} px из {1}", one.Width, l3.Width));
    }
}
