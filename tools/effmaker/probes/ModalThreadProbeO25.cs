using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

/// <summary>
/// МЕРКА ТОГО, ЧЕЙ ПОТОК ПОДНИМАЕТ ОКНО (`A241`).
///
/// ЗАЧЕМ. <c>AppUi.Report</c> — единственная дверь, через которую приложение
/// сообщает о беде, и до 05.09.2026 она звала <c>MessageBox.Show</c> НА
/// ВЫЗЫВАЮЩЕМ потоке. На фоновом потоке (поток буферов звука
/// <c>WaveIn.MaintainBuffers</c>, счёт FSA, <c>BackgroundWorker</c>,
/// менеджеры-одиночки при первом обращении) это давало окно без хозяина,
/// не модальное приложению, уходящее ЗА главное окно, и вставший до нажатия
/// «ОК» фоновый поток. Отказа при этом НЕ происходит — у <c>MessageBox</c>
/// свой насос сообщений, — то есть признака беды нет никакого.
///
/// ⛔ ОКНО `BecqMoni` НЕ ЗАПУСКАЕТСЯ. Проба поднимает СВОЙ поток окон (форма
/// + <c>Application.Run</c>) и зовёт метод приложения ОТРАЖЕНИЕМ из собранной
/// сборки. Мерится ровно тот код, который уедет людям.
///
/// ПЛЕЧИ, и первое из них — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ:
/// <list type="bullet">
/// <item><c>прежний код</c> — дословно то, что стояло в <c>Report</c> до
/// правки (<c>MessageBox.Show(text, caption, OK, icon)</c>), вызванное с
/// ФОНОВОГО потока. Окно обязано найтись У ФОНОВОГО потока, а сам поток
/// обязан стоять в нём. Не найдётся — мерить нечего, и все остальные числа
/// пусты: без этого плеча «новый код окна не поднял» значило бы только
/// «окно не поднялось вообще ни у кого».</item>
/// <item><c>дверь</c> — <c>AppUi.Report</c> с того же фонового потока. Окно
/// обязано найтись У ПОТОКА ОКОН и НЕ найтись у фонового, а фоновый поток
/// обязан вернуться НЕ ДОЖИДАЯСЬ нажатия «ОК».</item>
/// <item><c>не потеряно</c> — часть предыдущего и отдельный сторож:
/// сообщение обязано ПОЯВИТЬСЯ. Правка, которая просто перестала бы
/// показывать окно с фонового потока, прошла бы первые два условия и была бы
/// хуже дефекта.</item>
/// <item><c>поток окон</c> — вызов той же двери С САМОГО потока окон: окно
/// обязано подняться ТУТ ЖЕ, синхронно, как было. Это контроль на то, что
/// маршалинг не тронул путь, которым идут 90 из 99 вызывающих.</item>
/// </list>
///
/// ⚠ Фоновые потоки заводятся БЕЗ выставления апартамента — ровно как
/// <c>new Thread</c> в приложении (<c>WaveIn</c>, <c>ObsidianIn</c>,
/// <c>RadiaCodeIn</c>, <c>AtomSpectraVCPIn</c>). Проба, поставившая STA «чтобы
/// работало наверняка», мерила бы не тот процесс.
///
/// ⚠ <c>AppUi.HasWindows</c> у пробы ЛОЖЕН по построению (признак — входная
/// сборка), и без подмены дверь ушла бы в поток ошибок, не показав ничего.
/// Поле выставляется отражением, и проба ОБЪЯВЛЯЕТ об этом в шапке: подмена
/// признака — часть замера, а не тихая настройка.
/// </summary>
static class ModalThreadProbeO25
{
    const string Text = "ModalThreadProbeO25: sample text";
    const string Caption = "ModalThreadProbeO25";

    // Класс диалогового окна Windows. У MessageBox он именно такой; у формы
    // WinForms — "WindowsForms10.Window...", так что ложных попаданий нет.
    const string DialogClass = "#32770";

    const uint WM_CLOSE = 0x0010;

    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindow(IntPtr hWnd);

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (Exception) { }

        string outPath = null;
        bool negative = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            // `A263`: второе условие было ОТДЕЛЬНЫМ `if`; сведено в цепочку, чтобы
            // у неё был хвост. `--negative` — ключ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ, и
            // опечатка в нём молча превращала контроль в обычный прогон.
            else if (a == "--negative") negative = true;
            else
            {
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }
        }

        Say("== A241: чей поток поднимает модальное окно ==");
        if (negative)
        {
            Say("   ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ (`--negative`): вместо двери подставлен прежний");
            Say("   код. Ожидаемый исход прогона — ОТКАЗ кодом 1.");
        }
        Say("");

        Assembly app = typeof(AppUi).Assembly;
        Say("сборка приложения:  " + app.Location);
        try
        {
            Say("собрана:            " + File.GetLastWriteTime(app.Location)
                                             .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Say("собрана:            не прочитана: " + ex.Message); }
        Say("платформа пробы:    целевая платформа входной сборки="
            + (AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "НЕ ОБЪЯВЛЕНА")
            + ", среда=" + Environment.Version.ToString());
        Say("признак окон до подмены: AppUi.HasWindows=" + Inv(AppUi.HasWindows));

        // --- подмена признака окон -------------------------------------------------
        FieldInfo flag = typeof(AppUi).GetField("hasWindows",
                                                BindingFlags.NonPublic | BindingFlags.Static);
        if (flag == null)
        {
            Say("");
            Say("⛔ ЗАМЕР НЕВОЗМОЖЕН: поля `hasWindows` в `AppUi` нет — признак переименован.");
            return Finish(outPath, 2);
        }
        // ⚠ `hasWindows` объявлено `static readonly`, и `FieldInfo.SetValue`
        //    на нём МОЛЧА не срабатывает (измерено 05.09.2026: отказа нет,
        //    значение прежнее). Молчаливый промах — ровно тот случай, ради
        //    которого значение ЧИТАЕТСЯ ОБРАТНО, а не считается выставленным.
        //    Запасной путь — своя мера с `stsfld` при `skipVisibility`.
        string how = "FieldInfo.SetValue";
        try { flag.SetValue(null, true); }
        catch (Exception ex) { how = "FieldInfo.SetValue отказала (" + ex.GetType().Name + ")"; }
        if (!ReadFlag(flag))
        {
            how = "stsfld через DynamicMethod";
            try
            {
                DynamicMethod dm = new DynamicMethod("O25_SetHasWindows", null,
                                                     new Type[] { typeof(bool) },
                                                     typeof(AppUi).Module, true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, flag);
                il.Emit(OpCodes.Ret);
                ((Action<bool>)dm.CreateDelegate(typeof(Action<bool>)))(true);
            }
            catch (Exception ex)
            {
                Say("");
                Say("⛔ ЗАМЕР НЕВОЗМОЖЕН: признак окон не выставляется: "
                    + ex.GetType().Name + ": " + ex.Message);
                return Finish(outPath, 2);
            }
        }
        if (!ReadFlag(flag))
        {
            Say("");
            Say("⛔ ЗАМЕР НЕВОЗМОЖЕН: признак окон не принял значение.");
            return Finish(outPath, 2);
        }
        Say("признак окон после подмены: поле hasWindows=" + Inv(ReadFlag(flag))
            + " (" + how + "), свойство AppUi.HasWindows=" + Inv(AppUi.HasWindows));

        // --- свой поток окон -------------------------------------------------------
        UiThread ui = UiThread.Start();
        if (ui == null)
        {
            Say("");
            Say("⛔ ЗАМЕР НЕВОЗМОЖЕН: свой поток окон не поднялся.");
            return Finish(outPath, 2);
        }
        Say("");
        Say("поток окон пробы:   native id=" + Inv(ui.NativeId)
            + ", форма в Application.OpenForms=" + Inv(ui.InOpenForms));
        if (!ui.InOpenForms)
        {
            Say("");
            Say("⛔ ЗАМЕР НЕВОЗМОЖЕН: форма не попала в `Application.OpenForms`, "
                + "а дверь ищет поток окон именно там.");
            ui.Stop();
            return Finish(outPath, 2);
        }

        int code;
        try
        {
            Arm1_OldCode(ui);
            Arm2_Door(ui, negative);
            if (!negative) Arm3_OnUiThread(ui);
        }
        finally
        {
            ui.Stop();
        }

        Say("");
        if (negative)
        {
            // Отрицательный контроль читается НАОБОРОТ: прежний код обязан
            // условия провалить. Прошедший здесь прогон значит, что условия
            // ничего не различают, и это отказ пробы, а не успех.
            if (failures > 0)
            {
                Say("ИТОГ: ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ ОТРАБОТАЛ: прежний код не сошёлся с "
                    + "условиями, несошедшихся условий: " + Inv(failures) + ".");
                code = 1;
            }
            else
            {
                Say("⛔ ИТОГ: ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ ПРОВАЛЕН: прежний код прошёл условия "
                    + "плеча `дверь`. Условия не различают починенный код и дефектный, "
                    + "и «сошлось» обычного прогона не значит ничего.");
                code = 3;
            }
            return Finish(outPath, code);
        }
        if (failures == 0)
        {
            Say("ИТОГ: СОШЛОСЬ. Прежний код поднимал окно фоновым потоком; дверь поднимает "
                + "его потоком окон, не задерживая фоновый, и сообщение не теряется.");
            code = 0;
        }
        else
        {
            Say("ИТОГ: РАЗОШЛОСЬ, несошедшихся условий: " + Inv(failures));
            code = 1;
        }
        return Finish(outPath, code);
    }

    // ---------------------------------------------------------------------------
    // Плечо 1 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: прежний код на фоновом потоке.
    // ---------------------------------------------------------------------------
    static void Arm1_OldCode(UiThread ui)
    {
        Say("");
        Say("--- плечо `прежний код` (положительный контроль) ---");

        BackgroundCall call = BackgroundCall.Run(delegate
        {
            // ДОСЛОВНО то, что стояло в `AppUi.Report` до правки `A241`.
            MessageBox.Show(Text, Caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        });

        List<IntPtr> onBg = WaitForDialogs(call.NativeId, 5000);
        List<IntPtr> onUi = Dialogs(ui.NativeId);

        Say("окон на фоновом потоке:  " + Describe(onBg));
        Say("окон на потоке окон:     " + Describe(onUi));
        Say("фоновый поток вернулся:  " + Inv(call.Returned) + " (окно ещё не закрыто)");

        Check("окно поднято ФОНОВЫМ потоком", onBg.Count == 1);
        Check("на потоке окон окна НЕТ", onUi.Count == 0);
        Check("фоновый поток СТОИТ в окне", !call.Returned);

        CloseAll(onBg);
        bool done = call.WaitFinish(5000);
        Say("после закрытия окна фоновый поток завершился: " + Inv(done)
            + ", вызов занял мс=" + Inv(call.ElapsedMs));
        Check("после закрытия окна фоновый поток пошёл дальше", done);
    }

    // ---------------------------------------------------------------------------
    // Плечо 2 — дверь с фонового потока.
    //
    // ⛔ Ключ `--negative` подставляет сюда ПРЕЖНИЙ код вместо двери: проба
    //    обязана тогда ОТКАЗАТЬ кодом 1. Без этого «сошлось» значило бы лишь
    //    «условия написаны так, что сходятся всегда».
    // ---------------------------------------------------------------------------
    static void Arm2_Door(UiThread ui, bool negative)
    {
        Say("");
        Say(negative
            ? "--- плечо `дверь`, ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: вместо двери прежний код ---"
            : "--- плечо `дверь` ---");

        MethodInfo report = typeof(AppUi).GetMethod("Report",
            BindingFlags.Public | BindingFlags.Static, null,
            new Type[] { typeof(string), typeof(string), typeof(MessageBoxIcon) }, null);
        if (report == null)
        {
            Say("⛔ метода `AppUi.Report(string, string, MessageBoxIcon)` в сборке нет.");
            failures++;
            return;
        }

        BackgroundCall call = BackgroundCall.Run(delegate
        {
            if (negative)
            {
                MessageBox.Show(Text, Caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            report.Invoke(null, new object[] { Text, Caption, MessageBoxIcon.Exclamation });
        });

        // Фоновый поток обязан вернуться сам, не дожидаясь окна.
        bool returned = call.WaitFinish(5000);
        Say("фоновый поток вернулся:  " + Inv(returned) + ", вызов занял мс=" + Inv(call.ElapsedMs));
        Check("фоновый поток НЕ ждёт нажатия «ОК»", returned);
        if (call.Error != null)
        {
            Say("⛔ дверь отказала: " + call.Error);
            failures++;
        }

        List<IntPtr> onUi = WaitForDialogs(ui.NativeId, 5000);
        List<IntPtr> onBg = Dialogs(call.NativeId);

        Say("окон на потоке окон:     " + Describe(onUi));
        Say("окон на фоновом потоке:  " + Describe(onBg));

        Check("окно поднято ПОТОКОМ ОКОН", onUi.Count == 1);
        Check("на фоновом потоке окна НЕТ", onBg.Count == 0);
        Check("сообщение НЕ ПОТЕРЯНО (окно есть)", onUi.Count + onBg.Count > 0);

        CloseAll(onUi);
        CloseAll(onBg);
        WaitNoDialogs(ui.NativeId, 5000);
    }

    // ---------------------------------------------------------------------------
    // Плечо 3 — дверь с САМОГО потока окон: путь 90 из 99 вызывающих.
    // ---------------------------------------------------------------------------
    static void Arm3_OnUiThread(UiThread ui)
    {
        Say("");
        Say("--- плечо `поток окон` ---");

        bool[] returned = { false };
        ui.Post(delegate
        {
            // Зовём дверь ПРЯМО на потоке окон. Окно модально, поэтому эта
            // строка не вернётся до закрытия — что и требуется проверить.
            AppUi.Report(Text, Caption, MessageBoxIcon.Exclamation);
            returned[0] = true;
        });

        List<IntPtr> onUi = WaitForDialogs(ui.NativeId, 5000);
        Say("окон на потоке окон:     " + Describe(onUi));
        Say("вызов вернулся до закрытия окна: " + Inv(returned[0]));

        Check("окно поднято тем же потоком окон", onUi.Count == 1);
        Check("вызов СИНХРОНЕН, как было", !returned[0]);

        CloseAll(onUi);
        WaitNoDialogs(ui.NativeId, 5000);
        // Дать потоку окон дойти до конца обработчика.
        for (int i = 0; i < 50 && !returned[0]; i++) Thread.Sleep(20);
        Say("после закрытия окна вызов вернулся: " + Inv(returned[0]));
        Check("после закрытия окна путь потока окон продолжился", returned[0]);
    }

    // ---------------------------------------------------------------------------
    // Опоры
    // ---------------------------------------------------------------------------
    sealed class BackgroundCall
    {
        public uint NativeId;
        public volatile bool Returned;
        public string Error;
        public long ElapsedMs;
        Thread thread;
        readonly ManualResetEventSlim started = new ManualResetEventSlim(false);

        public static BackgroundCall Run(Action body)
        {
            BackgroundCall self = new BackgroundCall();
            // ⚠ Апартамент НЕ выставляется нарочно: `new Thread` в приложении
            //    (WaveIn, ObsidianIn, RadiaCodeIn, AtomSpectraVCPIn) даёт MTA,
            //    и мерить надо его, а не удобный STA.
            self.thread = new Thread(delegate ()
            {
                self.NativeId = GetCurrentThreadId();
                Stopwatch sw = Stopwatch.StartNew();
                self.started.Set();
                try { body(); }
                catch (Exception ex)
                {
                    Exception inner = ex is TargetInvocationException && ex.InnerException != null
                                      ? ex.InnerException : ex;
                    self.Error = inner.GetType().Name + ": " + inner.Message;
                }
                sw.Stop();
                self.ElapsedMs = sw.ElapsedMilliseconds;
                self.Returned = true;
            });
            self.thread.IsBackground = true;
            self.thread.Start();
            self.started.Wait(5000);
            // Дать вызову дойти до окна.
            Thread.Sleep(300);
            return self;
        }

        public bool WaitFinish(int ms)
        {
            return this.thread.Join(ms);
        }
    }

    sealed class UiThread
    {
        public uint NativeId;
        public bool InOpenForms;
        Form form;
        Thread thread;

        public static UiThread Start()
        {
            UiThread self = new UiThread();
            ManualResetEventSlim ready = new ManualResetEventSlim(false);
            self.thread = new Thread(delegate ()
            {
                self.NativeId = GetCurrentThreadId();
                self.form = new Form
                {
                    Text = "ModalThreadProbeO25 UI",
                    StartPosition = FormStartPosition.Manual,
                    // Далеко за краем экрана: окно нужно как поток и хозяин,
                    // а не как зрелище.
                    Location = new System.Drawing.Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                self.form.Load += delegate { ready.Set(); };
                try { Application.Run(self.form); }
                catch (Exception) { ready.Set(); }
            });
            self.thread.SetApartmentState(ApartmentState.STA);
            self.thread.IsBackground = true;
            self.thread.Start();
            if (!ready.Wait(10000)) return null;
            // Дать форме попасть в Application.OpenForms.
            for (int i = 0; i < 100; i++)
            {
                if (self.CheckOpenForms()) break;
                Thread.Sleep(20);
            }
            self.InOpenForms = self.CheckOpenForms();
            return self;
        }

        bool CheckOpenForms()
        {
            try
            {
                FormCollection forms = Application.OpenForms;
                for (int i = 0; i < forms.Count; i++)
                {
                    if (ReferenceEquals(forms[i], this.form)) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        public void Post(Action body)
        {
            try { this.form.BeginInvoke((MethodInvoker)delegate { body(); }); }
            catch (Exception) { }
        }

        public void Stop()
        {
            try { this.form.BeginInvoke((MethodInvoker)delegate { this.form.Close(); }); }
            catch (Exception) { }
            try { this.thread.Join(5000); }
            catch (Exception) { }
        }
    }

    static List<IntPtr> Dialogs(uint threadId)
    {
        List<IntPtr> found = new List<IntPtr>();
        if (threadId == 0) return found;
        try
        {
            EnumThreadWindows(threadId, delegate (IntPtr h, IntPtr l)
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() == DialogClass) found.Add(h);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return found;
    }

    static List<IntPtr> WaitForDialogs(uint threadId, int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<IntPtr> found = Dialogs(threadId);
        while (found.Count == 0 && sw.ElapsedMilliseconds < ms)
        {
            Thread.Sleep(50);
            found = Dialogs(threadId);
        }
        return found;
    }

    static void WaitNoDialogs(uint threadId, int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (Dialogs(threadId).Count > 0 && sw.ElapsedMilliseconds < ms) Thread.Sleep(50);
    }

    static string Describe(List<IntPtr> windows)
    {
        if (windows.Count == 0) return "0";
        StringBuilder sb = new StringBuilder();
        sb.Append(Inv(windows.Count));
        for (int i = 0; i < windows.Count; i++)
        {
            StringBuilder title = new StringBuilder(256);
            GetWindowText(windows[i], title, title.Capacity);
            sb.Append(i == 0 ? " (" : ", ");
            sb.Append("класс " + DialogClass + ", заголовок «" + title.ToString() + "»");
        }
        sb.Append(")");
        return sb.ToString();
    }

    static void CloseAll(List<IntPtr> windows)
    {
        foreach (IntPtr h in windows)
        {
            try { if (IsWindow(h)) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception) { }
        }
    }

    static bool ReadFlag(FieldInfo flag)
    {
        try { return (bool)flag.GetValue(null); }
        catch (Exception) { return false; }
    }

    static void Check(string what, bool ok)
    {
        Say((ok ? "  ✅ " : "  ⛔ ") + what + ": " + (ok ? "да" : "НЕТ"));
        if (!ok) failures++;
    }

    // ⛔ Числа печатаются ИНВАРИАНТНОЙ культурой — правило Amber 05.09.2026.
    static string Inv(object value)
    {
        IFormattable f = value as IFormattable;
        if (f != null) return f.ToString(null, CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    static void Say(string line)
    {
        Console.WriteLine(line);
        Log.AppendLine(line);
    }

    static int Finish(string outPath, int code)
    {
        Say("");
        Say("код возврата: " + Inv(code));
        if (!string.IsNullOrEmpty(outPath))
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("вывод не записан: " + ex.Message);
            }
        }
        return code;
    }
}
