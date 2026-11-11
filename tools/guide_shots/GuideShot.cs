// Снимки главного окна для пользовательского гида `docs/` (`AMBER319`, 08.11.2026).
//
// Как устроено. Проба кладётся В КАТАЛОГ КОПИИ ПРИЛОЖЕНИЯ рядом с BecquerelMonitor.exe
// (портативная раскладка: конфиг берётся из <каталог>\config, а не из %AppData%) и зовёт
// точку входа самого приложения — `Program.Main` — отражением. Главное окно создаёт
// приложение своим путём (`Program.cs`, `new MainForm(args)`), сама проба окон не строит:
// голый `new MainForm()` без показа таймера не заводит и ведёт себя не как приложение
// (журнал `handover/handover-2026-11-05-fixui.md`), а `DrawToBitmap` видимого окна
// рисует не то, что видит человек. Кадр — `PrintWindow` (PW_RENDERFULLCONTENT) окна,
// обрезанный по DWMWA_EXTENDED_FRAME_BOUNDS, иначе по краю чёрная полоса невидимой рамки.
//
// ⛔ Запускать ТОЛЬКО на скрытом столе (`HiddenRun.exe`): окно иначе выйдет на экран.
// Сценарий съёмки, копия конфига и установка снимков — `shoot.ps1` рядом (шапка там).
//
//   GuideShot.exe --scene=<main|main-fsa|main-fsa-nomatrix> --out=<каталог> [--wait=<с>]
//
// Сцену задаёт копия конфига (спектр в раскладке, склад матриц) — её готовит `shoot.ps1`;
// здесь только то, что делает человек мышью: выбрать сет, включить FSA, перейти на
// вкладку, навести курсор. Каждое такое действие — вызов ОБРАБОТЧИКА приложения, тот же,
// что зовёт щелчок.
//
// Выход: <out>\<имя>.png (кадр окна и вырезки панелей) и <out>\<scene>.txt — тексты
// всех элементов окна (подписи, поля, списки, строки таблиц): по ним `check.py` ищет
// личное без распознавания картинки. Коды возврата: 0 — снято; 2 — доводы; 3 — окно
// не дождались; 4 — разбор не завершился; 5 — поднялось модальное окно (текст в журнал);
// 6 — исключение.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

static class GuideShot
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lp);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lp);
    delegate bool EnumProc(IntPtr hwnd, IntPtr lp);

    static string scene, outDir;
    static int waitSeconds = 300;
    static StreamWriter log;
    static int exitCode = 6;
    static bool staleControl;
    static string gdmlPath;

    /// <summary>Кривая конструктора в конфиге гида (`make_copy.py`, CYLINDER).</summary>
    const string MakerCurve = "Цилиндр 40×15";

    /// <summary>Сцены: три главного окна и диалоговые (`DriveDialogs`).</summary>
    static readonly string[] Scenes =
    {
        "main", "main-fsa", "main-fsa-nomatrix",
        "devcfg", "devcfg-ecal", "devcfg-eff", "devtcp", "maker", "gdml", "nucdef", "dialogs",
    };

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].StartsWith("--diff="))
        {
            string[] pair = args[0].Substring(7).Split('|');
            return Diff(pair[0], pair[1]);
        }

        return Run(args);
    }

    // Отдельным методом: сверке снимков сборка приложения рядом не нужна, а JIT метода,
    // который её называет, потребовал бы BecquerelMonitor.exe ещё до разбора доводов.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
        foreach (string a in args)
        {
            if (a.StartsWith("--scene=")) scene = a.Substring(8);
            else if (a.StartsWith("--out=")) outDir = a.Substring(6);
            else if (a.StartsWith("--wait=")) waitSeconds = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--stale-control") staleControl = true;
            else if (a.StartsWith("--gdml=")) gdmlPath = a.Substring(7);
        }

        if (Array.IndexOf(Scenes, scene) < 0 || string.IsNullOrEmpty(outDir))
        {
            Console.Error.WriteLine("GuideShot.exe --scene=<" + string.Join("|", Scenes) + "> --out=<каталог> [--wait=<с>] [--gdml=<файл>]");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        log = new StreamWriter(Path.Combine(outDir, scene + ".log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        Say("сцена " + scene + ", каталог " + AppDomain.CurrentDomain.BaseDirectory);

        var driver = new Thread(Drive) { IsBackground = true, Name = "GuideShot" };
        driver.Start();

        // Точка входа приложения целиком: проверка второго экземпляра (имя — по пути ЭТОГО
        // exe), журнал, язык, `Application.Run(new MainForm(args))`. Документ открывает
        // раскладка копии конфига, поэтому доводов нет.
        MethodInfo main = typeof(MainForm).Assembly.GetType("BecquerelMonitor.Program")
            .GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        try
        {
            main.Invoke(null, new object[] { new string[0] });
        }
        catch (Exception ex)
        {
            Say("Program.Main: " + ex);
            return 6;
        }

        return exitCode;
    }

    static void Say(string s)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + s;
        Console.WriteLine(line);
        lock (log) log.WriteLine(line);
    }

    static void Finish(int code)
    {
        exitCode = code;
        Say("выход " + code);
        log.Flush();
        Environment.Exit(code);
    }

    static object Field(object o, string name)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    static object Call(object o, string name, params object[] args)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            foreach (MethodInfo m in t.GetMethods(Any | BindingFlags.DeclaredOnly))
            {
                if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(o, args);
            }
        }
        throw new MissingMethodException(o.GetType().Name, name);
    }

    static T Ui<T>(MainForm mf, Func<T> f)
    {
        return (T)mf.Invoke(f);
    }

    static void Ui(MainForm mf, Action f)
    {
        mf.Invoke(f);
    }

    static void Drive()
    {
        try
        {
            DriveScene();
        }
        catch (Exception ex)
        {
            Say("исключение: " + ex);
            Finish(6);
        }
    }

    static MainForm WaitMainForm()
    {
        DateTime limit = DateTime.Now.AddSeconds(120);
        while (DateTime.Now < limit)
        {
            Thread.Sleep(300);
            CheckDialogs();
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    var mf = f as MainForm;
                    if (mf != null && mf.IsHandleCreated && mf.Visible) return mf;
                }
            }
            catch (InvalidOperationException)
            {
                // список форм меняется на потоке окон — взять в следующий раз
            }
        }

        return null;
    }

    /// <summary>
    /// Модальное окно на скрытом столе нажать некому: проба висела бы до срока. Любое
    /// стандартное окно диалога (#32770) или посторонняя форма процесса — отказ с текстом.
    /// </summary>
    static void CheckDialogs()
    {
        uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var found = new List<string>();
        EnumWindows((h, lp) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != me || !IsWindowVisible(h)) return true;
            var cls = new StringBuilder(256);
            GetClassName(h, cls, 256);
            if (cls.ToString() != "#32770") return true;
            var text = new StringBuilder();
            text.Append(WindowText(h));
            EnumChildWindows(h, (c, l2) => { string t = WindowText(c); if (t.Length > 0) text.Append(" | ").Append(t); return true; }, IntPtr.Zero);
            found.Add(text.ToString());
            return true;
        }, IntPtr.Zero);
        if (found.Count > 0)
        {
            foreach (string s in found) Say("МОДАЛЬНОЕ ОКНО: " + s);
            Finish(5);
        }
    }

    static string WindowText(IntPtr h)
    {
        var sb = new StringBuilder(1024);
        GetWindowText(h, sb, 1024);
        return sb.ToString();
    }

    static void DriveScene()
    {
        MainForm mf = WaitMainForm();
        if (mf == null)
        {
            Say("главное окно не появилось за 120 с");
            Finish(3);
        }

        Say("окно: " + Ui(mf, () => mf.Text + " " + mf.Bounds));
        DocEnergySpectrum doc = null;
        for (int i = 0; i < 200 && doc == null; i++)
        {
            Thread.Sleep(300);
            CheckDialogs();
            doc = Ui(mf, () => mf.ActiveDocument);
        }

        if (doc == null)
        {
            Say("документ раскладки не открылся");
            Finish(3);
        }

        Say("документ: " + Ui(mf, () => doc.Text));
        if (!scene.StartsWith("main"))
        {
            WaitAnalysis(mf, doc);
            DriveDialogs(mf);
            Finish(0);
        }

        bool fsa = scene.StartsWith("main-fsa");
        string set = fsa ? "Th-232 + K-40" : "Cs-137 + K-40";

        // 1. Сет нуклидов — список панели «Обнаружение пиков», как щелчок по пункту.
        Ui(mf, () =>
        {
            var view = (DCPeakDetectionView)Field(mf, "dcPeakDetectionView");
            var combo = (ComboBox)Field(view, "comboBoxNuclSet");
            int index = -1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i].ToString() == set) index = i;
            }

            if (index < 0) throw new InvalidOperationException("в списке сетов нет «" + set + "»");
            combo.SelectedIndex = index;
        });
        Say("сет: " + set);

        // 2. Режим FSA графика — обработчик пункта «Показать FSA» выпадающего списка фона.
        if (fsa)
        {
            Ui(mf, () => Call(doc, "ShowFsaToolStripMenuItem_Click", null, EventArgs.Empty));
            Say("режим графика: FSA");
        }

        // 3. Дождаться поиска пиков и разбора: сеанс не считает, результат есть, и так
        //    подряд несколько опросов (очередь снимков сеанса гасит повторный счёт не сразу).
        WaitAnalysis(mf, doc);

        // 4. Вкладки нижнего правого дока: «Обнаружение пиков» у главного снимка,
        //    «Результат измерений» у снимков FSA; активное окно — как на прежних снимках.
        Ui(mf, () =>
        {
            if (fsa)
            {
                var results = (IList)Field(mf, "dcResultViewList");
                if (results.Count == 0) throw new InvalidOperationException("окна «Результат измерений» нет в раскладке");
                ((WeifenLuo.WinFormsUI.Docking.DockContent)results[0]).Activate();
            }
            else
            {
                var peaks = (WeifenLuo.WinFormsUI.Docking.DockContent)Field(mf, "dcPeakDetectionView");
                peaks.Pane.ActiveContent = peaks;
                doc.Activate();
            }
        });

        // 5. Панель курсора главного снимка — обработчик движения мыши графика.
        if (!fsa)
        {
            Ui(mf, () => PlaceCursor(doc.EnergySpectrumView, 3172.0));
        }

        Thread.Sleep(4500); // график обновляется по таймеру (ChartRefreshCycle 2000 мс)
        CheckDialogs();
        Ui(mf, () => { mf.Refresh(); });
        Thread.Sleep(500);

        Ui(mf, () => Shoot(mf, doc));

        // Вырезки панелей из того же состояния окна (прежние снимки гида, раскладка та же).
        if (scene == "main")
        {
            // «Информация об образце» — вкладка верхнего правого дока, активная.
            var sample = (WeifenLuo.WinFormsUI.Docking.DockContent)Ui(mf, () => Field(mf, "dcSampleInfoView"));
            Ui(mf, () => sample.Activate());
            Thread.Sleep(1500);
            Ui(mf, () => { Point o; using (Bitmap b = Capture(mf, out o)) SavePane(b, o, sample, "panel-sample"); });
            Ui(mf, () => ((WeifenLuo.WinFormsUI.Docking.DockContent)Field(mf, "dcControlPanel")).Activate());
            Thread.Sleep(800);
        }
        else if (scene == "main-fsa")
        {
            // Отчёт FSA, таблица прокручена к «Качеству разбора».
            var report = (WeifenLuo.WinFormsUI.Docking.DockContent)Ui(mf, () => Field(mf, "dcFsaReportView"));
            Ui(mf, () =>
            {
                object table = Field(report, "reportTable");
                object model = table.GetType().GetProperty("TableModel").GetValue(table, null);
                var rows = (IList)model.GetType().GetProperty("Rows").GetValue(model, null);
                table.GetType().GetMethod("EnsureVisible", new[] { typeof(int), typeof(int) }).Invoke(table, new object[] { rows.Count - 1, 0 });
            });
            Thread.Sleep(1500);
            Ui(mf, () => { Point o; using (Bitmap b = Capture(mf, out o)) SavePane(b, o, report, "panel-fsa"); });
        }

        // Положительный контроль метки «!» (`--stale-control`, в гид не идёт): в списке
        // «Геометрия» выбрать первую кривую, которую правило `EfficiencyStaleMark` помечает,
        // и снять панель ещё раз — метка обязана встать в закрытом поле.
        if (staleControl && scene == "main")
        {
            int marked = Ui(mf, () =>
            {
                object panel = Field(mf, "dcControlPanel");
                var combo = (ComboBox)Field(panel, "efficiencyComboBox");
                var marks = (bool[])Field(panel, "efficiencyStaleMarks");
                int i = Array.IndexOf(marks, true);
                if (i >= 0) combo.SelectedIndex = i;
                return i;
            });
            Say("контроль метки: выбран пункт " + marked);
            if (marked >= 0)
            {
                Thread.Sleep(3000);
                CheckDialogs();
                Ui(mf, () => ShootPanel(mf, "panel-control-stale"));
            }
        }

        Finish(0);
    }

    static void WaitAnalysis(MainForm mf, DocEnergySpectrum doc)
    {
        DateTime limit = DateTime.Now.AddSeconds(waitSeconds);
        int calm = 0;
        string last = "";
        while (DateTime.Now < limit)
        {
            Thread.Sleep(500);
            CheckDialogs();
            string state = Ui(mf, () =>
            {
                var s = doc.FsaSession;
                return (s.IsRunning ? "считает" : "стоит") + "; результат " + (s.Result != null ? "есть" : "нет")
                       + "; " + (s.Status ?? "");
            });
            if (state != last)
            {
                Say("разбор: " + state);
                last = state;
            }

            calm = state.StartsWith("стоит; результат есть") ? calm + 1 : 0;
            if (calm >= 6) return;
        }

        Say("разбор не завершился за " + waitSeconds + " с");
        Finish(4);
    }

    static void PlaceCursor(EnergySpectrumView view, double kev)
    {
        // Перебрать столбцы графика и встать на ближайший к энергии — без своей формулы шкалы.
        int best = -1;
        double bestGap = double.MaxValue;
        for (int x = 0; x < view.Width; x++)
        {
            Call(view, "EnergySpectrumView_MouseMove", view, new MouseEventArgs(MouseButtons.None, 0, x, view.Height / 3, 0));
            double e = (double)Field(view, "cursorEnergy");
            double gap = Math.Abs(e - kev);
            if (gap < bestGap) { bestGap = gap; best = x; }
        }

        Call(view, "EnergySpectrumView_MouseMove", view, new MouseEventArgs(MouseButtons.None, 0, best, view.Height / 3, 0));
        view.Invalidate();
        Say(string.Format(CultureInfo.InvariantCulture, "курсор: x={0}, канал {1}, {2:F2} кэВ", best,
                          Field(view, "cursorChannel"), (double)Field(view, "cursorEnergy")));
    }

    /// <summary>Кадр окна `PrintWindow` по рамке DWM; <paramref name="origin"/> — её левый верх на экране.</summary>
    static Bitmap Capture(Form mf, out Point origin)
    {
        IntPtr h = mf.Handle;
        RECT wr, fr;
        GetWindowRect(h, out wr);
        if (DwmGetWindowAttribute(h, 9, out fr, Marshal.SizeOf(typeof(RECT))) != 0) fr = wr;
        Rectangle frame = Rectangle.FromLTRB(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - wr.Left, fr.Bottom - wr.Top);
        Say(string.Format(CultureInfo.InvariantCulture, "окно {0}x{1}, рамка DWM {2}", wr.Right - wr.Left, wr.Bottom - wr.Top, frame));
        origin = new Point(fr.Left, fr.Top);
        using (var whole = new Bitmap(wr.Right - wr.Left, wr.Bottom - wr.Top, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(whole))
            {
                g.Clear(Color.White);
                IntPtr hdc = g.GetHdc();
                // Мигающий курсор ввода в кадр не идёт (на прежних снимках его нет).
                HideCaret(IntPtr.Zero);
                bool ok = PrintWindow(h, hdc, 2);
                ShowCaret(IntPtr.Zero);
                g.ReleaseHdc(hdc);
                if (!ok) throw new InvalidOperationException("PrintWindow отказал");
            }

            Bitmap shot = whole.Clone(frame, PixelFormat.Format24bppRgb);
            // Только у окна с изменяемой рамкой: у постоянных (настройки, «О приложении») точки рамки
            // в углах на снимках гида чёрные — сверено попиксельно.
            if (mf.FormBorderStyle == FormBorderStyle.Sizable || mf.FormBorderStyle == FormBorderStyle.SizableToolWindow)
                WhitenCorners(shot);
            return shot;
        }
    }

    /// <summary>
    /// Углы скругления окна Windows 11 (журнал 07.11.2026): `PrintWindow` оставляет в них
    /// чёрные точки рамки. Белятся чисто чёрные точки в квадратах 9×9 у четырёх углов —
    /// так сняты прежние снимки гида (сверено попиксельно: внешний столбец и строка, по 9 точек).
    /// </summary>
    static void WhitenCorners(Bitmap shot)
    {
        const int r = 9;
        int black = Color.Black.ToArgb();
        foreach (int cx in new[] { 0, shot.Width - r })
        {
            foreach (int cy in new[] { 0, shot.Height - r })
            {
                for (int x = cx; x < cx + r; x++)
                {
                    for (int y = cy; y < cy + r; y++)
                    {
                        if (shot.GetPixel(x, y).ToArgb() == black) shot.SetPixel(x, y, Color.White);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Вырезка «Управление измерением» — вся панель дока с заголовком и ярлыками. Рамка вырезки
    /// гида (AMBER239, 07.11.2026): полоска фона дока по точке слева, справа и снизу, сверху —
    /// без первой строки панели. Сверено с прежним снимком попиксельно по заголовку и ярлыкам:
    /// 462×530 при окне 1880×1040.
    /// </summary>
    static void SavePanel(MainForm mf, Bitmap shot, Point origin, string name)
    {
        var panel = (WeifenLuo.WinFormsUI.Docking.DockContent)Field(mf, "dcControlPanel");
        Control pane = panel.Pane;
        Rectangle r = pane.RectangleToScreen(pane.ClientRectangle);
        r.Offset(-origin.X, -origin.Y);
        r = Rectangle.FromLTRB(r.Left - 1, r.Top + 1, r.Right + 1, r.Bottom + 2);
        using (Bitmap crop = shot.Clone(r, PixelFormat.Format24bppRgb)) Save(crop, name);
        Say("вырезка " + name + ": " + r);
        LogStaleMarks(panel);
    }

    /// <summary>
    /// Вырезка дока по прямоугольнику его панели — без полосок фона дока, как у `panel-result`,
    /// `panel-sample`, `panel-fsa` гида (460×407, 460×529, 307×942 при окне 1880×1040).
    /// </summary>
    static void SavePane(Bitmap shot, Point origin, WeifenLuo.WinFormsUI.Docking.DockContent content, string name)
    {
        Control pane = content.Pane;
        Rectangle r = pane.RectangleToScreen(pane.ClientRectangle);
        r.Offset(-origin.X, -origin.Y);
        r.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
        using (Bitmap crop = shot.Clone(r, PixelFormat.Format24bppRgb)) Save(crop, name);
        Say("вырезка " + name + ": " + r);
    }

    static void ShootPanel(MainForm mf, string name)
    {
        Point origin;
        using (Bitmap shot = Capture(mf, out origin)) SavePanel(mf, shot, origin, name);
    }

    static void Shoot(MainForm mf, DocEnergySpectrum doc)
    {
        Point origin;
        using (Bitmap shot = Capture(mf, out origin))
        {
            Save(shot, scene);
            if (scene == "main") SavePanel(mf, shot, origin, "panel-control");
            else LogStaleMarks(Field(mf, "dcControlPanel"));
            if (scene == "main-fsa")
            {
                var results = (IList)Field(mf, "dcResultViewList");
                SavePane(shot, origin, (WeifenLuo.WinFormsUI.Docking.DockContent)results[0], "panel-result");
            }
        }

        var dump = new StringBuilder();
        dump.Append("[окно] ").Append(mf.Text).Append("\r\n");
        DumpTexts(mf, dump, 0);
        File.WriteAllText(Path.Combine(outDir, scene + ".txt"), dump.ToString(), new UTF8Encoding(false));
    }

    // ------------------------------------------------------------------------------------
    // Диалоговые сцены. Окно открывает обработчик приложения — тот же, что пункт меню или
    // кнопка; модальные окна — через BeginInvoke (обработчик стоит в ShowDialog), немодальные
    // — Invoke. Снимается только своё окно: PrintWindow рисует его и под чужими окнами.
    // ------------------------------------------------------------------------------------

    static void Post(MainForm mf, Action f)
    {
        mf.BeginInvoke(f);
    }

    /// <summary>Дождаться открытой формы нужного типа (последней по списку).</summary>
    static T WaitForm<T>(MainForm mf, int seconds = 60) where T : Form
    {
        DateTime limit = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < limit)
        {
            Thread.Sleep(300);
            CheckDialogs();
            T found = Ui(mf, () =>
            {
                T last = null;
                foreach (Form f in Application.OpenForms)
                {
                    var t = f as T;
                    if (t != null && t.Visible && t.IsHandleCreated) last = t;
                }

                return last;
            });
            if (found != null)
            {
                Thread.Sleep(700);
                return found;
            }
        }

        Say("окно " + typeof(T).Name + " не открылось");
        Finish(3);
        return null;
    }

    /// <summary>Рамка DWM окна — прежнего снимка гида: окно больше рамки на 14×7 точек.</summary>
    static void SizeFrame(MainForm mf, Form f, int frameWidth, int frameHeight)
    {
        // Окно больше рамки DWM на невидимую кайму — разница меряется на самом окне. ⚠ Кайма
        // зависит от подсистемы PE процесса: проба собирается `/subsystemversion:6.00`, как
        // приложение (`mk.ps1`); с 4.0 Windows давала другие метрики рамки и другой масштаб окон.
        Ui(mf, () =>
        {
            f.WindowState = FormWindowState.Normal;
            RECT wr, fr;
            GetWindowRect(f.Handle, out wr);
            if (DwmGetWindowAttribute(f.Handle, 9, out fr, Marshal.SizeOf(typeof(RECT))) != 0) fr = wr;
            int dw = (wr.Right - wr.Left) - (fr.Right - fr.Left), dh = (wr.Bottom - wr.Top) - (fr.Bottom - fr.Top);
            f.Size = new Size(frameWidth + dw, frameHeight + dh);
        });
        Thread.Sleep(400);
    }

    static void Settle(MainForm mf, int ms = 1200)
    {
        Thread.Sleep(ms);
        CheckDialogs();
        Ui(mf, () => Application.DoEvents());
    }

    /// <summary>Снимок формы и тексты её элементов (для сверки на личное).</summary>
    static void SaveForm(MainForm mf, Form f, string name)
    {
        Settle(mf);
        Ui(mf, () =>
        {
            // Заголовок окна: на скрытом столе переднего плана нет, и окно рисуется
            // неактивным (серый текст). Снимки гида сняты кто активным, кто нет — состояние
            // задаётся явно (WM_NCACTIVATE), а не ловится по времени.
            SendMessage(f.Handle, 0x0086, (IntPtr)(ActiveCaption(name) ? 1 : 0), IntPtr.Zero);
            // Рамки фокуса (пунктир на ярлыке вкладки, на кнопке «Посчитать из геометрии»): Windows
            // показывает их по состоянию «клавиатурных подсказок» окна, а оно у конструктора от
            // прогона к прогону разное — снимок плыл на 98–424 точки (замер критика: два прогона
            // подряд). Не время, а состояние: оно задаётся явно — WM_CHANGEUISTATE, UIS_SET с
            // UISF_HIDEFOCUS | UISF_HIDEACCEL, как на снимках гида (пунктира нет).
            if (name.StartsWith("effmaker-"))
            {
                SendMessage(f.Handle, 0x0127, (IntPtr)(1 | (0x3 << 16)), IntPtr.Zero);
            }
            // Курсор ввода мигает: на прежних снимках его нет — прячется на время кадра.
            HideCaret(IntPtr.Zero);
            f.Refresh();
        });

        // Готовность — по самому окну, а не паузой: кадр снимается, пока два подряд (через
        // 0.5 с) не совпадут побитно. Сводка GDML (RichTextBox) дорисовывалась после первого
        // кадра: у одного прогона из трёх строка «г/см³; массовые доли …» была обрезана.
        Bitmap last = null;
        int frames = 0;
        for (; frames < 20; frames++)
        {
            Bitmap cur = Ui(mf, () =>
            {
                // Состояние заголовка и подсказок — заново перед каждым кадром: окно может
                // получить WM_NCACTIVATE от системы между кадрами (у `prefs-sound` один прогон
                // из двух кончался неактивным заголовком).
                SendMessage(f.Handle, 0x0086, (IntPtr)(ActiveCaption(name) ? 1 : 0), IntPtr.Zero);
                if (name.StartsWith("effmaker-")) SendMessage(f.Handle, 0x0127, (IntPtr)(1 | (0x3 << 16)), IntPtr.Zero);
                HideCaret(IntPtr.Zero);
                Point o;
                Bitmap b = Capture(f, out o);
                ShowCaret(IntPtr.Zero);
                return b;
            });
            bool same = last != null && SameBits(last, cur);
            if (last != null) last.Dispose();
            last = cur;
            if (same) break;
            Thread.Sleep(500);
            Ui(mf, () => Application.DoEvents());
        }

        Say(string.Format(CultureInfo.InvariantCulture, "кадр {0}: устоялся за {1} снимков", name, frames + 1));
        if (frames >= 20) Say("⚠ кадр " + name + " не устоялся за 20 снимков — снят последний");
        using (last) Save(last, name);
        Ui(mf, () =>
        {
            var dump = new StringBuilder();
            dump.Append("[окно] ").Append(f.Text).Append("\r\n");
            DumpTexts(f, dump, 0);
            File.WriteAllText(Path.Combine(outDir, name + ".txt"), dump.ToString(), new UTF8Encoding(false));
            ShowCaret(IntPtr.Zero);
        });
    }

    static bool SameBits(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        int[] pa = Pixels(a), pb = Pixels(b);
        for (int i = 0; i < pa.Length; i++)
        {
            if (pa[i] != pb[i]) return false;
        }

        return true;
    }

    [DllImport("user32.dll")] static extern bool HideCaret(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowCaret(IntPtr hwnd);

    /// <summary>
    /// Активный заголовок — как на снимках гида 07.11.2026 (сверено по цвету текста заголовка):
    /// окно прибора (кроме TCP/REST и английского звукового входа), «База изотопов», наборы,
    /// «О программе», настройки. Остальные — неактивные.
    /// </summary>
    static bool ActiveCaption(string name)
    {
        bool en = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName != "ru";
        if (name == "devcfg-device-astcp" || name == "devcfg-device-asrest") return false;
        if (name == "devcfg-device-audio") return !en;
        return name.StartsWith("devcfg-") || name == "nucbase" || name == "nucsets" || name == "about"
               || name.StartsWith("prefs-");
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

    static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            var t = c as T;
            if (t != null) yield return t;
            foreach (T d in Descendants<T>(c)) yield return d;
        }
    }

    /// <summary>Выбрать строку XPTable по тексту первой ячейки (как щелчок по строке).</summary>
    static bool SelectRow(object table, string text)
    {
        object model = table.GetType().GetProperty("TableModel").GetValue(table, null);
        var rows = (IList)model.GetType().GetProperty("Rows").GetValue(model, null);
        for (int i = 0; i < rows.Count; i++)
        {
            var cells = (IList)rows[i].GetType().GetProperty("Cells").GetValue(rows[i], null);
            string cellText = (string)cells[0].GetType().GetProperty("Text").GetValue(cells[0], null);
            if (cellText == text)
            {
                object sel = model.GetType().GetProperty("Selections").GetValue(model, null);
                sel.GetType().GetMethod("SelectCell", new[] { typeof(int), typeof(int) }).Invoke(sel, new object[] { i, 0 });
                MethodInfo ensure = table.GetType().GetMethod("EnsureVisible", new[] { typeof(int), typeof(int) });
                if (ensure != null) ensure.Invoke(table, new object[] { i, 0 });
                return true;
            }
        }

        return false;
    }

    static DeviceConfigInfo Device(string name)
    {
        foreach (DeviceConfigInfo c in DeviceConfigManager.GetInstance().DeviceConfigList)
        {
            if (c.Name == name) return c;
        }

        throw new InvalidOperationException("в копии конфига нет прибора «" + name + "»");
    }

    /// <summary>
    /// Строка списка и вкладка окна конфигурации прибора. ⛔ Перед сменой строки снимается
    /// признак изменённости: иначе окно спросит «Сохранить?», и на скрытом столе ответить
    /// некому (журнал 07.11.2026).
    /// </summary>
    static void DevcfgSelect(MainForm mf, DeviceConfigForm f, string device, int tab, string focus = "none")
    {
        Ui(mf, () =>
        {
            if (f.ActiveDeviceConfig == null || f.ActiveDeviceConfig.Name != device)
            {
                Call(f, "ResetActiveDeviceConfigDirty");
                if (!SelectRow(Field(f, "table1"), device)) throw new InvalidOperationException("строки «" + device + "» нет");
            }

            ((TabControl)Field(f, "tabControl1")).SelectedIndex = tab;
            // Фокус — как на снимке гида: "list" — список конфигураций (строка синяя),
            // "keep" — где его оставило окно (первое поле страницы), "none" — нигде.
            if (focus == "list") ((Control)Field(f, "table1")).Focus();
            else if (focus == "none") f.ActiveControl = null;
            else if (focus.StartsWith("text:"))
            {
                // поле с этим текстом в фокусе и выделено — как после щелчка по нему
                string value = focus.Substring(5);
                Application.DoEvents();
                TextBoxBase box = Descendants<TextBoxBase>(f).Where(t => t.Visible && t.Text == value)
                                                             .OrderBy(t => t.PointToScreen(Point.Empty).Y).FirstOrDefault();
                if (box == null)
                {
                    string seen = string.Join(" | ", Descendants<TextBoxBase>(f).Where(t => t.Visible).Select(t => t.Text));
                    throw new InvalidOperationException("нет поля «" + value + "» для фокуса; поля: " + seen);
                }

                box.Focus();
                box.SelectAll();
            }
        });
        Settle(mf);
        Ui(mf, () => Call(f, "ResetActiveDeviceConfigDirty"));
        Say("прибор «" + device + "», вкладка " + tab);
    }

    static void DevcfgCurve(MainForm mf, DeviceConfigForm f, string curve)
    {
        Ui(mf, () =>
        {
            var combo = (ComboBox)Field(f, "efficiencyCombo");
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i].ToString() == curve) { combo.SelectedIndex = i; return; }
            }

            throw new InvalidOperationException("у прибора нет кривой «" + curve + "»");
        });
        Settle(mf);
        // Выбор кривой в списке — правка конфигурации: «Сохранить» становится доступной,
        // как на снимке `devcfg-eff`. Признак снимает следующий выбор строки (`DevcfgSelect`).
        Ui(mf, () => f.ActiveControl = null);
    }

    /// <summary>
    /// Форма AtomSpectra на время проверки связи гасит списки порта и скорости и включает их
    /// снова; тема Windows рисует включение анимацией, и кадр посреди неё ловил стрелку списка
    /// то 110, то 111 (замер критика). Ждать, пока списки включены, и ещё 3 с.
    /// </summary>
    static void WaitPortsIdle(MainForm mf, DeviceConfigForm f)
    {
        for (int i = 0; i < 100; i++)
        {
            bool idle = Ui(mf, () => Descendants<ComboBox>(f).Where(c => c.Visible).All(c => c.Enabled));
            if (idle) break;
            Thread.Sleep(200);
        }

        Thread.Sleep(3000);
        Ui(mf, () => f.Refresh());
        Thread.Sleep(500);
    }

    static DeviceConfigForm OpenDevcfg(MainForm mf, string device)
    {
        DeviceConfigForm f = Ui(mf, () => mf.ShowDeviceConfigForm(Device(device)));
        Settle(mf, 2000);
        Ui(mf, () => Call(f, "ResetActiveDeviceConfigDirty"));
        return f;
    }

    static void DriveDialogs(MainForm mf)
    {
        switch (scene)
        {
            case "devcfg":
            {
                DeviceConfigForm f = OpenDevcfg(mf, "RC-103");
                var shots = new[]
                {
                    new object[] { "devcfg-general", "RC-103", 0, "list" },
                    new object[] { "devcfg-device-rc", "RC-103", 1, "none" },
                    new object[] { "devcfg-analysis", "RC-103", 4, "none" },
                    new object[] { "devcfg-refs", "RC-103", 5, "keep" },
                    new object[] { "devcfg-device-obs", "Obsidian", 1, "none" },
                    new object[] { "devcfg-device-audio", "Atom Spectra Nano 8 3000 ch", 1, "text:1" },
                    new object[] { "devcfg-device-ausb", "AmplitudaUSB", 1, "none" },
                    new object[] { "devcfg-device-aser", "AmplitudaSerial", 1, "none" },
                    // ⛔ Н1: форма AtomSpectra при загрузке шлёт `-cal` в порт; порт копии — COM99.
                    new object[] { "devcfg-device-as", "Atom Spectra 80x80", 1, "none" },
                };
                foreach (object[] s in shots)
                {
                    DevcfgSelect(mf, f, (string)s[1], (int)s[2], (string)s[3]);
                    if ((string)s[0] == "devcfg-device-as") WaitPortsIdle(mf, f);
                    SaveForm(mf, f, (string)s[0]);
                }

                break;
            }

            case "devcfg-ecal":
            {
                // Снимок калибровки энергии снят на копии с четырьмя приборами (`make_copy.py`).
                DeviceConfigForm f = OpenDevcfg(mf, "RC-103");
                DevcfgSelect(mf, f, "RC-103", 2, "none");
                SaveForm(mf, f, "devcfg-ecal");
                break;
            }

            case "devcfg-eff":
            {
                DeviceConfigForm f = OpenDevcfg(mf, "Atom Spectra 80x80");
                DevcfgSelect(mf, f, "Atom Spectra 80x80", 3);
                DevcfgCurve(mf, f, "AS80_th_disk");
                SaveForm(mf, f, "devcfg-eff");
                Post(mf, () => Call(f, "efficiencyMatrixButton_Click", null, EventArgs.Empty));
                ResponseMatrixForm m = WaitForm<ResponseMatrixForm>(mf);
                SetThreads(mf, m, "threadsBox");
                SaveForm(mf, m, "respmatrix");
                Ui(mf, () => m.Close());
                break;
            }

            case "devtcp":
            {
                DeviceConfigForm f = OpenDevcfg(mf, "AtomSpectra Wi-Fi");
                DevcfgSelect(mf, f, "AtomSpectra Wi-Fi", 1);
                SaveForm(mf, f, "devcfg-device-astcp");
                Ui(mf, () =>
                {
                    foreach (RadioButton r in Descendants<RadioButton>(f))
                    {
                        if (r.Visible && r.Text.IndexOf("REST", StringComparison.Ordinal) >= 0) r.Checked = true;
                    }
                });
                Settle(mf);
                Ui(mf, () => Call(f, "ResetActiveDeviceConfigDirty"));
                SaveForm(mf, f, "devcfg-device-asrest");
                break;
            }

            case "maker":
            {
                DeviceConfigForm f = OpenDevcfg(mf, "Atom Spectra 80x80");
                DevcfgSelect(mf, f, "Atom Spectra 80x80", 3);
                DevcfgCurve(mf, f, MakerCurve);
                EfficiencyMakerForm maker = OpenMaker(mf, f);
                TabControl inner = Ui(mf, () => Descendants<TabControl>((Control)Field(maker, "geometryPanel")).First());
                string[] names = { "effmaker-detector", "effmaker-source", "effmaker-shield" };
                for (int i = 0; i < names.Length; i++)
                {
                    int tab = i;
                    Ui(mf, () => inner.SelectedIndex = tab);
                    SaveForm(mf, maker, names[i]);
                }

                DevcfgCurve(mf, f, "AS80_th_disk");
                EfficiencyMakerForm calc = OpenMaker(mf, f);
                Ui(mf, () =>
                {
                    var outer = (TabControl)Field(calc, "tabControl");
                    outer.SelectedTab = (TabPage)Field(calc, "tabPageCalculate");
                });
                SetThreads(mf, calc, "calcThreadsBox");
                SaveForm(mf, calc, "effmaker-calc");
                break;
            }

            case "gdml":
            {
                EfficiencyMakerForm maker = Ui(mf, () =>
                {
                    var m = new EfficiencyMakerForm();
                    m.StartPosition = FormStartPosition.Manual;
                    m.Location = new Point(40, 40);
                    m.Show(mf);
                    return m;
                });
                Settle(mf, 1500);
                SizeFrame(mf, maker, 1166, 853);
                string problem = Ui(mf, () =>
                {
                    return (bool)typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFile", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(maker, new object[] { gdmlPath, null }) ? null : "отказ импорта";
                });
                if (problem != null) Say("GDML: " + problem);
                Say("GDML: " + Path.GetFileName(gdmlPath));
                // (`AMBER322`) После импорта открыта подвкладка «Файл GDML»: блок «Сопоставление», сверка, чертёж.
                SaveForm(mf, maker, "effmaker-gdml");

                // (`AMBER322`) Окно импорта «что содержится в файле»: списки деталей и маленький чертёж. Человек видит
                // его ДО раскладки; здесь оно строится тем же классом на том же файле и показывается немодально —
                // модальное окно съёмку остановило бы.
                Form importDialog = Ui(mf, () =>
                {
                    var file = BecquerelMonitor.EfficiencyMaker.GdmlGeometry.Read(gdmlPath);
                    var overview = BecquerelMonitor.EfficiencyMaker.GdmlOverview.Of(file);
                    var d = new GdmlImportDialog(file, overview, BecquerelMonitor.EfficiencyMaker.GdmlFieldMap.Build(file, overview));
                    d.StartPosition = FormStartPosition.Manual;
                    d.Location = new Point(80, 80);
                    d.Show(maker);
                    return (Form)d;
                });
                Settle(mf, 1500);
                SaveForm(mf, importDialog, "effmaker-gdml-import");
                Ui(mf, () => importDialog.Close());
                break;
            }

            case "nucdef":
            {
                NuclideDefinitionForm nd = Ui(mf, () => mf.ShowNuclideDefinitionForm());
                Settle(mf, 1500);
                SizeFrame(mf, nd, 675, 475);
                Ui(mf, () => SelectRow(Field(nd, "table1"), "Am-241"));
                SaveForm(mf, nd, "nucdef");
                Ui(mf, () => nd.Close());

                Post(mf, () => mf.ShowNuclideSetForm());
                NuclideSetForm sets = WaitForm<NuclideSetForm>(mf);
                Ui(mf, () => SelectRow(Field(sets, "tableSets"), "Cs-137 + K-40"));
                SaveForm(mf, sets, "nucsets");
                Ui(mf, () => sets.Close());

                Ui(mf, () => Call(mf, "ShowNucBaseView"));
                Form nb = (Form)Ui(mf, () => Field(mf, "nucBaseView"));
                Settle(mf, 1500);
                SizeFrame(mf, nb, 990, 553);
                Ui(mf, () =>
                {
                    ((TextBox)Field(nb, "IsotopeTextBox")).Text = "Cs-137";
                    ((TextBox)Field(nb, "IntencityTextBox")).Text = "6";
                    ((TextBox)Field(nb, "LowEnrgTextBox")).Text = "0";
                    ((TextBox)Field(nb, "HighEnrgTextBox")).Text = "3000";
                    ((Button)Field(nb, "SearchButton")).PerformClick();
                });
                Settle(mf, 3000);
                SaveForm(mf, nb, "nucbase");
                break;
            }

            case "dialogs":
            {
                Post(mf, () => Call(mf, "versionInfoAToolStripMenuItem_Click", null, EventArgs.Empty));
                Form about = WaitForm<AboutForm>(mf);
                SaveForm(mf, about, "about");
                Ui(mf, () => about.Close());

                Ui(mf, () => Call(mf, "basicSettingsBToolStripMenuItem_Click", null, EventArgs.Empty));
                GlobalConfigForm g = WaitForm<GlobalConfigForm>(mf);
                var tabs = (TabControl)Ui(mf, () => Field(g, "tabControl1"));
                string[] names = { "prefs-chart", "prefs-color", "prefs-sound", "prefs-system" };
                for (int i = 0; i < names.Length; i++)
                {
                    int tab = i;
                    // выбор вкладки уводит фокус в первое поле; на снимках гида фокуса нет, кроме
                    // первой вкладки («График»: фокус в окне, кнопка OK обведена как кнопка по умолчанию)
                    Ui(mf, () => { tabs.SelectedIndex = tab; if (tab != 0) g.ActiveControl = null; });
                    SaveForm(mf, g, names[i]);
                }

                break;
            }
        }
    }

    /// <summary>
    /// Поле «Потоков» по умолчанию — число процессоров машины минус один: снимок зависел от
    /// железа (правило 20.10.2026). Перед кадром в поле ставится постоянное число — тем же
    /// путём, что человек (значение поля); приложение не правится.
    /// </summary>
    const int GuideThreads = 4;

    static void SetThreads(MainForm mf, Form f, string field)
    {
        Ui(mf, () => ((NumericUpDown)Field(f, field)).Value = GuideThreads);
        Settle(mf);
    }

    static EfficiencyMakerForm OpenMaker(MainForm mf, DeviceConfigForm f)
    {
        int before = Ui(mf, () => Application.OpenForms.OfType<EfficiencyMakerForm>().Count());
        Ui(mf, () => Call(f, "OpenEfficiencyMaker", Call(f, "SelectedEfficiency")));
        EfficiencyMakerForm maker = null;
        for (int i = 0; i < 100 && maker == null; i++)
        {
            Thread.Sleep(300);
            CheckDialogs();
            maker = Ui(mf, () => Application.OpenForms.OfType<EfficiencyMakerForm>().Skip(before).FirstOrDefault());
        }

        if (maker == null)
        {
            Say("конструктор кривой не открылся");
            Finish(3);
        }

        Settle(mf, 1500);
        SizeFrame(mf, maker, 954, 825);
        Say("конструктор: " + Ui(mf, () => maker.Text));
        return maker;
    }

    static void LogStaleMarks(object panel)
    {
        var combo = (ComboBox)Field(panel, "efficiencyComboBox");
        var marks = (bool[])Field(panel, "efficiencyStaleMarks");
        Say("список «Геометрия»: выбран " + combo.SelectedIndex + " «" + combo.Text + "»");
        for (int i = 0; i < combo.Items.Count; i++)
        {
            Say(string.Format(CultureInfo.InvariantCulture, "  {0} {1} «{2}»", i,
                              i < marks.Length && marks[i] ? "!" : " ", combo.Items[i]));
        }
    }

    static void Save(Bitmap bmp, string name)
    {
        string path = Path.Combine(outDir, name + ".png");
        bmp.Save(path, ImageFormat.Png);
        Say(string.Format(CultureInfo.InvariantCulture, "снимок {0}: {1}x{2}", name, bmp.Width, bmp.Height));
    }

    /// <summary>
    /// Попиксельная сверка двух снимков (`--diff=<было>|<стало>`, окон не строит): размер,
    /// число разных точек и прямоугольники областей, где они лежат (клетки 8×8, слитые по
    /// соседству). Код 0 — совпали, 1 — различаются, 2 — разный размер.
    /// </summary>
    static int Diff(string before, string after)
    {
        using (var a = new Bitmap(before))
        using (var b = new Bitmap(after))
        {
            Console.WriteLine("было  {0}x{1}: {2}", a.Width, a.Height, before);
            Console.WriteLine("стало {0}x{1}: {2}", b.Width, b.Height, after);
            if (a.Width != b.Width || a.Height != b.Height)
            {
                Console.WriteLine("РАЗМЕР РАЗНЫЙ");
                return 2;
            }

            int[] pa = Pixels(a), pb = Pixels(b);
            const int cell = 8;
            int cw = (a.Width + cell - 1) / cell, ch = (a.Height + cell - 1) / cell;
            var mark = new bool[cw, ch];
            long changed = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i] == pb[i]) continue;
                changed++;
                mark[(i % a.Width) / cell, (i / a.Width) / cell] = true;
            }

            Console.WriteLine("разных точек: {0} из {1}", changed, pa.Length);
            var seen = new bool[cw, ch];
            var stack = new Stack<Point>();
            for (int x = 0; x < cw; x++)
            {
                for (int y = 0; y < ch; y++)
                {
                    if (!mark[x, y] || seen[x, y]) continue;
                    int x0 = x, y0 = y, x1 = x, y1 = y;
                    stack.Push(new Point(x, y));
                    seen[x, y] = true;
                    while (stack.Count > 0)
                    {
                        Point p = stack.Pop();
                        x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = p.X + dx, ny = p.Y + dy;
                                if (nx < 0 || ny < 0 || nx >= cw || ny >= ch || seen[nx, ny] || !mark[nx, ny]) continue;
                                seen[nx, ny] = true;
                                stack.Push(new Point(nx, ny));
                            }
                        }
                    }

                    Console.WriteLine("  область x={0}..{1}, y={2}..{3}", x0 * cell, Math.Min(a.Width, (x1 + 1) * cell) - 1,
                                      y0 * cell, Math.Min(a.Height, (y1 + 1) * cell) - 1);
                }
            }

            return changed == 0 ? 0 : 1;
        }
    }

    static int[] Pixels(Bitmap bmp)
    {
        using (Bitmap copy = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppArgb))
        {
            BitmapData data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[copy.Width * copy.Height];
            for (int y = 0; y < copy.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, y * copy.Width, copy.Width);
            }

            copy.UnlockBits(data);
            return px;
        }
    }

    /// <summary>Тексты всех видимых элементов: подписи, поля, пункты списков, строки таблиц.</summary>
    static void DumpTexts(Control c, StringBuilder sb, int depth)
    {
        if (!c.Visible) return;
        string pad = new string(' ', depth * 2);
        if (!string.IsNullOrEmpty(c.Text)) sb.Append(pad).Append(c.GetType().Name).Append(": ").Append(c.Text.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
        var combo = c as ComboBox;
        if (combo != null)
        {
            foreach (object item in combo.Items) sb.Append(pad).Append("  пункт: ").Append(item).Append("\r\n");
        }

        var list = c as ListView;
        if (list != null)
        {
            foreach (ListViewItem item in list.Items)
            {
                sb.Append(pad).Append("  строка:");
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems) sb.Append(" | ").Append(sub.Text);
                sb.Append("\r\n");
            }
        }

        var strip = c as ToolStrip;
        if (strip != null)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                if (!string.IsNullOrEmpty(item.Text)) sb.Append(pad).Append("  кнопка: ").Append(item.Text).Append("\r\n");
            }
        }

        PropertyInfo model = c.GetType().GetProperty("TableModel");
        if (model != null)
        {
            object tm = model.GetValue(c, null);
            object rows = tm != null ? tm.GetType().GetProperty("Rows").GetValue(tm, null) : null;
            if (rows != null)
            {
                foreach (object row in (IEnumerable)rows)
                {
                    sb.Append(pad).Append("  строка:");
                    object cells = row.GetType().GetProperty("Cells").GetValue(row, null);
                    foreach (object cell in (IEnumerable)cells)
                    {
                        sb.Append(" | ").Append(cell.GetType().GetProperty("Text").GetValue(cell, null));
                    }

                    sb.Append("\r\n");
                }
            }
        }

        var grid = c as DataGridView;
        if (grid != null)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                sb.Append(pad).Append("  строка:");
                foreach (DataGridViewCell cell in row.Cells) sb.Append(" | ").Append(cell.FormattedValue);
                sb.Append("\r\n");
            }
        }

        foreach (Control child in c.Controls) DumpTexts(child, sb, depth + 1);
    }
}
