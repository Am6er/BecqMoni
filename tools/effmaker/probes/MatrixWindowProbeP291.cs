using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// П291 (14.10.2026): окно «Матрица отклика» после отменённого или неудавшегося
// пересчёта (Н6) и отказ GPU посреди счёта (Н7) — находки П253, решения Amber
// 14.10.2026 вопросником:
//
//   Н6 «Вернуть прежний результат» — «Прежний несохранённый результат возвращается
//      вместе со своими подробностями, кнопка «Сохранить» остаётся.»
//   Н7 «Текст на языке окна + совет» — «Понятный текст на языке окна и совет «снимите
//      галку Use Nvidia GPU — посчитается на процессоре». Один ключ en/ru.»
//
//   matrixwindowprobep291 --geometry=<.in> [--ui=en|ru] [--out=<файл>]
//
// Окно НЕ показывается: форма строится, «Посчитать» / «Отмена» / «Сохранить» зовутся
// отражением, ход счёта прокачивается `Application.DoEvents` (продолжение `await`
// в `ComputeClick` идёт через контекст синхронизации WinForms потока пробы), поля
// формы читаются отражением. Сцена — точечная (`RC103_point0`): у неё нет таблицы
// κ пар, и счёт идёт секундами.
//
// Отказы наводятся ПОДМЕНОЙ, без порчи библиотеки и физики:
//   * отказ пересчёта на ЦП — геометрия формы переставляется в «цилиндр сбоку»
//     (`GeometryModel.FacingError`): построитель отказывает словами до счёта;
//   * отказ GPU посреди счёта — `BQ_GPU_LAUNCH=0,2048` (ключ раскроя запуска, его
//     читает `RmGpu`): 2048 нитей на блок больше предела устройства (1024), запуск
//     ядра отказывает `invalid configuration argument` в `rm_run`, ядро не идёт, TDR
//     нет. Это тот же путь, по которому приходит TDR и нехватка памяти устройства:
//     код `rm_run` ≠ 0 → `RmGpu.Ok` → исключение с внутренним текстом.
//
// Код возврата: 0 — все сошлись; 1 — есть расхождения; 2 — ключи или среда.
static class MatrixWindowProbeP291
{
    static readonly StringBuilder Log = new StringBuilder();
    static readonly StringWriter TraceText = new StringWriter();
    static int failures;
    static string ui = "en";

    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Быстрый счёт: точечная сцена, 8 узлов, 20 000 историй.</summary>
    const decimal QuickHistories = 20000;

    /// <summary>Долгий пересчёт, который успевает быть отменённым.</summary>
    const decimal LongHistories = 5000000;

    sealed class Win : IDisposable
    {
        public readonly ResponseMatrixForm Form;
        public readonly EfficiencyConfigData Config;

        public Win(EfficiencyConfigData config)
        {
            this.Config = config;
            this.Form = new ResponseMatrixForm(config);
            this.Box("minEnergyBox").Value = 30;
            this.Box("maxEnergyBox").Value = 3000;
            this.Box("nodesBox").Value = 8;
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
        public ResponseMatrix SavedComputed { get { return (ResponseMatrix)this.Field("savedComputed"); } }
        public string Details { get { return (string)this.Field("detailsText"); } }
        public string Progress { get { return ((Label)this.Field("progressLabel")).Text; } }
        public string State { get { return ((Label)this.Field("stateLabel")).Text; } }
        public bool SaveEnabled { get { return ((Button)this.Field("saveButton")).Enabled; } }

        /// <summary>Строка хода влезает в свою метку по высоте (перенос по словам, как рисует `Label`).</summary>
        public bool ProgressFits(out int need, out int have)
        {
            var label = (Label)this.Field("progressLabel");
            need = TextRenderer.MeasureText(label.Text, label.Font, new System.Drawing.Size(label.Width, int.MaxValue),
                                            TextFormatFlags.WordBreak).Height;
            have = label.Height;
            return need <= have;
        }
        public bool Busy { get { return (bool)this.Field("busy"); } }

        /// <summary>Условие вопроса «не сохранена, закрыть?» из `OnFormClosing`.</summary>
        public bool CloseWouldAsk
        {
            get { return this.Computed != null && !object.ReferenceEquals(this.Computed, this.SavedComputed); }
        }

        public bool GpuUsable
        {
            get { return (bool)typeof(ResponseMatrixForm).GetProperty("GpuUsable", F).GetValue(this.Form, null); }
        }

        public void Gpu(bool on) { ((CheckBox)this.Field("gpuCheck")).Checked = on; }

        void Call(string method)
        {
            typeof(ResponseMatrixForm).GetMethod(method, F).Invoke(this.Form, new object[] { null, EventArgs.Empty });
        }

        public void Save() { this.Call("SaveClick"); }

        /// <summary>
        /// «Посчитать» и дождаться конца. <paramref name="cancelAt"/> ≥ 0 — «Отмена» через
        /// столько секунд; ложь — счёт кончился раньше отмены (замер не состоялся).
        /// </summary>
        public bool Compute(double cancelAt, out double seconds)
        {
            var sw = Stopwatch.StartNew();
            EnsureWinFormsContext();
            this.Call("ComputeClick");
            if (!this.Busy)
            {
                seconds = 0;
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
            seconds = sw.Elapsed.TotalSeconds;
            if (!done) Say("   ⚠ счёт не кончился за 900 с");
            return done && cancelledWhileBusy;
        }

        public void Dispose() { this.Form.Dispose(); }
    }

    /// <summary>
    /// ⛔ (`AMBER268`, 28.10.2026) Контекст окна — перед КАЖДЫМ «Посчитать». Проба
    /// качает очередь `Application.DoEvents` без `Application.Run`, и к второму
    /// счёту `WindowsFormsSynchronizationContext` с потока уже снят (замерено;
    /// что снимает именно выход из `DoEvents`, по исходникам WinForms не
    /// проверено). Второй счёт окна создавал
    /// `Progress&lt;T&gt;` и продолжение `await` уже без него: отчёты и итог шли с
    /// потоков пула вперемешку, и «Узел 12 из 12» затирал «Готово…» (S7 падал и на
    /// коде до `AMBER267`). Замер: без установки 276 отчётов второго счёта вне
    /// потока окна и итог «Узел 144 из 144»; с установкой 0 и «Готово за 3:40».
    /// В приложении цикл `Application.Run` держит контекст всё время — это
    /// свойство пробы, не окна.
    /// </summary>
    static void EnsureWinFormsContext()
    {
        if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }
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

    static bool HasCyrillic(string s)
    {
        if (s == null) return false;
        foreach (char c in s) if (c >= 'Ѐ' && c <= 'ӿ') return true;
        return false;
    }

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
        return new EfficiencyConfigData("проба П291") { Guid = guid, Geometry = g, Curve = Amber267Curve() };
    }

    /// <summary>Подмена: «цилиндр сбоку» — построитель (ЦП и GPU) отказывает словами до счёта.</summary>
    static string BreakFacing(EfficiencyConfigData config)
    {
        config.Geometry.Shape = CrystalShape.Cylinder;
        config.Geometry.Facing = GeometryDetectorFacing.Side;
        return config.Geometry.FacingError;
    }

    static string FailedText(string reason)
    {
        return string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixFailed, reason);
    }

    /// <summary>Новый ключ через `ResourceManager`: проба собирается и против exe без него (контроль «до»).</summary>
    static string GpuFailedText()
    {
        string format = Resources.ResourceManager.GetString("ResponseMatrixGpuFailed", Resources.Culture);
        return format == null ? null : string.Format(CultureInfo.InvariantCulture, format, Resources.ResponseMatrixUseGpu);
    }

    static string Prefix(string format)
    {
        int i = format.IndexOf('{');
        return i < 0 ? format : format.Substring(0, i);
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
        Trace.Listeners.Add(new TextWriterTraceListener(TraceText));
        Trace.AutoFlush = true;
        Environment.SetEnvironmentVariable("BQ_GPU_LAUNCH", null);

        Say("П291 — окно матрицы: отмена/отказ пересчёта (Н6), отказ GPU (Н7)");
        Say("exe:       " + typeof(ResponseMatrixForm).Assembly.Location);
        Say("культура:  " + culture.Name + "; ключ ResponseMatrixGpuFailed "
            + (Resources.ResourceManager.GetString("ResponseMatrixGpuFailed", Resources.Culture) == null ? "ОТСУТСТВУЕТ" : "есть"));
        Say("геометрия: " + geometryPath);
        Application.EnableVisualStyles();
        GlobalConfigManager.GetInstance();
        string run = "p291-" + ui + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        try
        {
            S1CancelRestoresUnsaved(run + "-s1");
            S2CpuFailureRestoresUnsaved(run + "-s2");
            S3GpuFailure(run + "-s3");
            S4SharedRefusalOnGpuPath(run + "-s4");
            S5NoUnsavedShowsStore(run + "-s5");
            S6SavedThenCancel(run + "-s6");
            S7SuccessReplaces(run + "-s7");
        }
        catch (Exception ex)
        {
            failures++;
            Say("⛔ проба оборвалась: " + ex);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BQ_GPU_LAUNCH", null);
        }

        Say("");
        Say(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + failures.ToString(CultureInfo.InvariantCulture));
        if (outPath != null) File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
        return failures == 0 ? 0 : 1;
    }

    static ResponseMatrix QuickCompute(Win w, string what)
    {
        double s;
        w.Box("historiesBox").Value = QuickHistories;
        bool ok = w.Compute(-1, out s);
        Say(string.Format(CultureInfo.InvariantCulture, "   {0}: {1:F1} с, строка хода {2}", what, s, Q(w.Progress)));
        if (!ok || w.Computed == null)
        {
            throw new InvalidOperationException("быстрый счёт не дал матрицы — замер невозможен");
        }

        return w.Computed;
    }

    /// <summary>Н6: посчитали, не сохранили → «Пересчитать» → «Отмена».</summary>
    static void S1CancelRestoresUnsaved(string guid)
    {
        Say("");
        Say("S1 · Н6: несохранённый результат → «Пересчитать» → «Отмена»");
        using (var w = new Win(NewConfig(guid)))
        {
            ResponseMatrix m1 = QuickCompute(w, "первый счёт");
            string d1 = w.Details;
            Check(w.SaveEnabled, "после первого счёта «Сохранить» доступна");

            w.Box("historiesBox").Value = LongHistories;
            double s;
            bool measured = w.Compute(1.5, out s);
            Say(string.Format(CultureInfo.InvariantCulture, "   пересчёт отменён через 1.5 с, вышел за {0:F2} с; строка хода {1}", s, Q(w.Progress)));
            Check(measured, "отмена пришла ПОСРЕДИ пересчёта (замер состоялся)");
            Check(w.Progress != null && w.Progress.StartsWith(Resources.ResponseMatrixCancelled, StringComparison.Ordinal),
                  "строка хода — «" + Resources.ResponseMatrixCancelled + "»");
            Check(object.ReferenceEquals(w.Computed, m1), "посчитанная матрица — ПРЕЖНЯЯ (не null): " + (w.Computed == null ? "null" : "та же"));
            Check(w.SaveEnabled, "«Сохранить» доступна");
            Check(w.Details == d1, "подробности — прежнего результата, без изменений");
            Check(w.CloseWouldAsk, "закрытие спросит «не сохранена, закрыть?» (результат жив)");
            Say("   подробности: " + Q(w.Details));
        }
    }

    /// <summary>Н6 (отказ) и Н7 (отказ ЦП-пути — прежний текст).</summary>
    static void S2CpuFailureRestoresUnsaved(string guid)
    {
        Say("");
        Say("S2 · Н6/Н7: несохранённый результат → пересчёт на ЦП ОТКАЗАЛ (подмена: цилиндр сбоку)");
        using (var w = new Win(NewConfig(guid)))
        {
            ResponseMatrix m1 = QuickCompute(w, "первый счёт");
            string d1 = w.Details;
            string facing = BreakFacing(w.Config);
            double s;
            w.Compute(-1, out s);
            string want = FailedText("InvalidOperationException: " + facing);
            Say("   строка хода: " + Q(w.Progress));
            Check(w.Progress == want, "отказ ЦП — ПРЕЖНИЙ текст ResponseMatrixFailed с причиной");
            Check(object.ReferenceEquals(w.Computed, m1), "посчитанная матрица — прежняя: " + (w.Computed == null ? "null" : "та же"));
            Check(w.SaveEnabled, "«Сохранить» доступна");
            Check(w.Details == d1, "подробности — прежнего результата");
        }
    }

    /// <summary>Н7: отказ GPU посреди счёта (`rm_run`), плюс Н6 для отказа.</summary>
    static void S3GpuFailure(string guid)
    {
        Say("");
        Say("S3 · Н7/Н6: несохранённый результат → пересчёт на GPU, `rm_run` ОТКАЗАЛ (BQ_GPU_LAUNCH=0,2048)");
        using (var w = new Win(NewConfig(guid)))
        {
            if (!w.GpuUsable)
            {
                failures++;
                Say("   ✗ GPU-путь окна негоден на этой машине — НЕ ИЗМЕРЕНО");
                return;
            }

            ResponseMatrix m1 = QuickCompute(w, "первый счёт (ЦП)");
            string d1 = w.Details;
            int traceFrom = TraceText.GetStringBuilder().Length;
            w.Gpu(true);
            Environment.SetEnvironmentVariable("BQ_GPU_LAUNCH", "0,2048");
            double s;
            try
            {
                w.Compute(-1, out s);
            }
            finally
            {
                Environment.SetEnvironmentVariable("BQ_GPU_LAUNCH", null);
                w.Gpu(false);
            }

            string trace = TraceText.GetStringBuilder().ToString().Substring(traceFrom);
            string want = GpuFailedText();
            Say(string.Format(CultureInfo.InvariantCulture, "   вышел за {0:F2} с; строка хода: {1}", s, Q(w.Progress)));
            Say("   ожидалось:   " + Q(want));
            Check(want != null && w.Progress == want, "отказ GPU — НОВЫЙ ключ ResponseMatrixGpuFailed с именем галки «" + Resources.ResponseMatrixUseGpu + "»");
            Check(w.Progress.IndexOf(Resources.ResponseMatrixUseGpu, StringComparison.Ordinal) >= 0, "в строке — имя галки на языке окна");
            Check(ui == "ru" ? HasCyrillic(w.Progress) : !HasCyrillic(w.Progress),
                  ui == "ru" ? "строка по-русски" : "в английском окне НЕТ русских букв");
            Check(w.Progress.IndexOf("rm_run", StringComparison.Ordinal) < 0, "внутреннего текста (`rm_run`) в окне нет");
            int need, have;
            bool fits = w.ProgressFits(out need, out have);
            Check(fits, string.Format(CultureInfo.InvariantCulture, "текст влезает в строку хода: нужно {0} px из {1}", need, have));
            bool logged = trace.IndexOf("rm_run(", StringComparison.Ordinal) >= 0;
            Check(logged, "внутренний текст отказа — в журнале приложения (Trace)");
            if (logged)
            {
                string first = trace.Trim().Split('\n')[0].Trim();
                Say("   журнал: " + Q(first.Length > 220 ? first.Substring(0, 220) + "…" : first));
            }

            Check(object.ReferenceEquals(w.Computed, m1), "посчитанная матрица — прежняя: " + (w.Computed == null ? "null" : "та же"));
            Check(w.SaveEnabled, "«Сохранить» доступна");
            Check(w.Details == d1, "подробности — прежнего результата");
        }
    }

    /// <summary>
    /// Н7, отрицательный контроль разбора: отказ GPU-пути, ОБЩИЙ с ЦП (геометрия), —
    /// прежний текст: совет «снимите галку» солгал бы, ЦП откажет так же.
    /// </summary>
    static void S4SharedRefusalOnGpuPath(string guid)
    {
        Say("");
        Say("S4 · Н7: галка GPU стоит, отказ ОБЩИЙ с ЦП (цилиндр сбоку) — совета снять галку быть не должно");
        using (var w = new Win(NewConfig(guid)))
        {
            if (!w.GpuUsable)
            {
                failures++;
                Say("   ✗ GPU-путь окна негоден на этой машине — НЕ ИЗМЕРЕНО");
                return;
            }

            string d0 = w.Details;
            string facing = BreakFacing(w.Config);
            w.Gpu(true);
            double s;
            try
            {
                w.Compute(-1, out s);
            }
            finally
            {
                w.Gpu(false);
            }

            Say("   строка хода: " + Q(w.Progress));
            Check(w.Progress == FailedText("InvalidOperationException: " + facing), "прежний текст ResponseMatrixFailed с причиной геометрии");
            Check(w.Computed == null && !w.SaveEnabled, "матрицы нет, «Сохранить» погашена");
            Check(w.Details == d0, "подробности — как при открытии");
        }
    }

    /// <summary>Н6, второй случай: прежнего несохранённого нет — подробности склада, как при открытии.</summary>
    static void S5NoUnsavedShowsStore(string guid)
    {
        Say("");
        Say("S5 · Н6: на складе матрица, в окне несохранённого нет → «Пересчитать» → «Отмена» / отказ");
        using (var w = new Win(NewConfig(guid)))
        {
            QuickCompute(w, "счёт для склада");
            w.Save();
        }

        string pending = ResponseMatrixStore.PathOf(guid, ResponseMatrixSource.Pending);
        string store = ResponseMatrixStore.PathOf(guid);
        if (!File.Exists(pending))
        {
            throw new InvalidOperationException("«Сохранить» не записала временный файл: " + pending);
        }

        File.Move(pending, store);
        using (var w = new Win(NewConfig(guid)))
        {
            string d0 = w.Details, s0 = w.State;
            Say("   при открытии: " + Q(s0) + " / " + Q(d0));
            Check(d0.Length > 0 && w.Computed == null && !w.SaveEnabled, "окно описывает склад, «Сохранить» погашена");

            w.Box("historiesBox").Value = LongHistories;
            double s;
            bool measured = w.Compute(1.5, out s);
            Check(measured, "отмена пришла ПОСРЕДИ пересчёта (замер состоялся)");
            Check(w.Details == d0 && w.State == s0, "после отмены: подробности и состояние — склада, как при открытии");
            Check(w.Computed == null && !w.SaveEnabled, "после отмены: матрицы в окне нет, «Сохранить» погашена");
            Check(!w.CloseWouldAsk, "закрытие не спросит");

            BreakFacing(w.Config);
            w.Compute(-1, out s);
            Say("   отказ: " + Q(w.Progress));
            Check(w.Details == d0 && w.State == s0, "после отказа: подробности и состояние — склада");
            Check(w.Computed == null && !w.SaveEnabled, "после отказа: «Сохранить» погашена");
        }

        // Наблюдение (не приёмка, расхождением не считается): удачный пересчёт при
        // матрице на складе — строка «Data … file …» у НЕСОХРАНЁННОЙ матрицы.
        using (var w = new Win(NewConfig(guid)))
        {
            QuickCompute(w, "наблюдение: удачный пересчёт при матрице на складе");
            string line = Array.Find(w.Details.Split('\n'), x => x.IndexOf("KB", StringComparison.Ordinal) >= 0
                                                                 || x.IndexOf("КБ", StringComparison.Ordinal) >= 0);
            Say(string.Format(CultureInfo.InvariantCulture, "   наблюдение: несохранённая матрица, строка {0}; файл склада {1} байт",
                              Q(line == null ? null : line.Trim()), new FileInfo(store).Length));
        }
    }

    /// <summary>Н6: результат УЖЕ сохранён → пересчёт → отмена: «Сохранить» не оживает.</summary>
    static void S6SavedThenCancel(string guid)
    {
        Say("");
        Say("S6 · Н6: результат сохранён → «Пересчитать» → «Отмена»");
        using (var w = new Win(NewConfig(guid)))
        {
            ResponseMatrix m1 = QuickCompute(w, "счёт");
            w.Save();
            string d1 = w.Details;
            Check(!w.SaveEnabled && object.ReferenceEquals(w.SavedComputed, m1), "после «Сохранить» кнопка погашена");
            w.Box("historiesBox").Value = LongHistories;
            double s;
            bool measured = w.Compute(1.5, out s);
            Check(measured, "отмена пришла ПОСРЕДИ пересчёта (замер состоялся)");
            Check(!w.SaveEnabled, "«Сохранить» по-прежнему погашена (сохранённое второй раз не предлагается)");
            Check(w.Details == d1, "подробности — сохранённого результата");
            Check(!w.CloseWouldAsk, "закрытие не спросит");
        }
    }

    /// <summary>Удачный пересчёт — как прежде: новая матрица заменяет прежнюю (ЦП и GPU).</summary>
    static void S7SuccessReplaces(string guid)
    {
        Say("");
        Say("S7 · удачный пересчёт заменяет прежний результат (ЦП, затем GPU в том же процессе)");
        using (var w = new Win(NewConfig(guid)))
        {
            ResponseMatrix m1 = QuickCompute(w, "первый счёт");
            ResponseMatrix m2 = QuickCompute(w, "пересчёт на ЦП");
            Check(!object.ReferenceEquals(m1, m2) && w.SaveEnabled
                  && w.Progress.StartsWith(Prefix(Resources.ResponseMatrixDone), StringComparison.Ordinal),
                  "ЦП: новая матрица, «Готово…», «Сохранить» доступна");
            if (!w.GpuUsable)
            {
                failures++;
                Say("   ✗ GPU-путь окна негоден — НЕ ИЗМЕРЕНО");
                return;
            }

            w.Gpu(true);
            try
            {
                ResponseMatrix m3 = QuickCompute(w, "пересчёт на GPU");
                Check(!object.ReferenceEquals(m2, m3) && w.SaveEnabled
                      && w.Progress.StartsWith(Prefix(Resources.ResponseMatrixDone), StringComparison.Ordinal),
                      "GPU: новая матрица, «Готово…», «Сохранить» доступна");
            }
            finally
            {
                w.Gpu(false);
            }

            // Н6: строка шума континуума — свойство возвращённого результата. GPU считает
            // плоско, и 20 000 историй дают шумную матрицу; отмена следующего пересчёта
            // обязана её повторить вслед за «Отменено».
            ResponseMatrix noisy = w.Computed;
            if (!(noisy.ContinuumWeightedError > 5.0))
            {
                Say("   (шума континуума у результата нет — ветвь строки шума при отмене не проверена)");
                return;
            }

            w.Box("historiesBox").Value = LongHistories;
            double s;
            bool measured = w.Compute(1.5, out s);
            string want = Resources.ResponseMatrixCancelled
                          + string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixContinuumNoise,
                                          noisy.ContinuumWeightedError.ToString("f1", CultureInfo.InvariantCulture));
            Say("   отмена после шумного результата: " + Q(w.Progress));
            Check(measured && object.ReferenceEquals(w.Computed, noisy) && w.SaveEnabled,
                  "отмена посреди пересчёта: шумный результат возвращён, «Сохранить» доступна");
            Check(w.Progress == want, "строка хода — «Отменено» и шум возвращённого результата");
            int need, have;
            Check(w.ProgressFits(out need, out have),
                  string.Format(CultureInfo.InvariantCulture, "текст влезает в строку хода: нужно {0} px из {1}", need, have));
        }
    }
}
