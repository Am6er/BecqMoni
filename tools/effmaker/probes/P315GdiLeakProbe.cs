using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П315, 15.10.2026) УТЕЧКИ РЕСУРСОВ ПРИ ДОЛГОЙ РАБОТЕ — ЗАМЕРОМ, А НЕ ЧТЕНИЕМ.
    ///
    /// Человек оставляет набор на часы и сутки; окно перерисовывается по такту. Утечка
    /// объектов GDI или USER (предел процесса — 10 000 каждого) либо памяти кончается
    /// падением с потерей набора. Проба гоняет НАСТОЯЩИЕ пути приложения и печатает
    /// счётчики процесса до и после:
    ///   GDI  — GetGuiResources(GR_GDIOBJECTS),  USER — GetGuiResources(GR_USEROBJECTS),
    ///   HND  — Process.HandleCount (ядро), MAN — управляемая память после полной сборки,
    ///   PRV  — частная память процесса после полной сборки и финализаторов.
    ///
    /// Режимы (--mode=):
    ///   paint — документ (настоящий <c>DocEnergySpectrum</c>) в скрытом окне; кадр — путь такта
    ///           графика <c>MainForm.RefreshDocumentChart</c> (RecalcChartParameters, PrepareViewData,
    ///           RecalcScrollBar, Invalidate) и отрисовка вида: WM_PRINT (<c>DrawToBitmap</c>) — окно за
    ///           краем экрана WM_PAINT не получает (мерено: 0 из 180), `--paint=wm` — WM_PAINT. Сцены по кругу:
    ///           линейная/степенная/логарифмическая шкала, линии/столбики, каналы/энергия, отсчёты/cps,
    ///           фон, вычитание, континуум, сглаживание SMA/WMA, курсор с панелью и выделение,
    ///           подсветка пиков, слои FSA. Между сценами (кроме FSA) спектр «набирается».
    ///   tick  — настоящий <c>MainForm</c> со всеми окнами-панелями (скрытно, без захвата фокуса),
    ///           документ открыт его же путём, набор изображён (Recording, время, отсчёты); кадр —
    ///           настоящий такт <c>MainForm.OnTimer</c> (100 мс) и разбор очереди сообщений окна.
    ///   docs  — тот же <c>MainForm</c>: открыть документ, такты, закрыть — N раз; живые после
    ///           сборки документы и виды (<c>WeakReference</c>), путь удержания и рост GDI/USER на цикл.
    ///   layout — смена раскладки (пункты меню) N раз: живы ли прежние окна-панели, кто держит.
    ///   session / bginvoke / plain — развилки: только сеанс FSA без окон; только BeginInvoke из
    ///           пула; простая форма без кода приложения (контроль среды).
    ///
    /// Опыты (--experiment=, через запятую): noacc — заслон WM_GETOBJECT (внешний клиент
    /// доступности держит опрошенные окна — п. 6 журнала); nofix — снять правки П315 отражением
    /// (положительный контроль правок); capcache — предел кэша Клейна — Нишины (изображение правки
    /// `DoseRate.cs`); nodrop / allowdrop — развилки по перетаскиванию. Ключи развилок paint:
    /// --scenes=nofsa|fsa|N, --fsa-each-round, --unsub=doc|view|both, --session-invoke; docs:
    /// --settle=с; tick: --census-out=, --census-base= (перепись достижимого по типам).
    /// Журнал: handover/handover-2026-10-15-p315-long-run-leaks.md.
    ///
    /// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (--sabotage=): подброшенная заведомая утечка на каждом кадре,
    ///   gdi     — CreatePen без DeleteObject (+1 GDI на кадр),
    ///   user    — CreateMenu без DestroyMenu (+1 USER на кадр),
    ///   managed — byte[4096] в статический список (+4 КБ на кадр),
    ///   pen     — new Pen без Dispose: объект GDI+, НЕ GDI; ждём «роста нет» — финализатор
    ///             его освобождает (контроль того, что проба отличает лень от утечки),
    ///   docref  — (docs) документ в статический список: живых должно стать N.
    /// Проба с подброшенной утечкой обязана её назвать, иначе она не мерит.
    ///
    ///   P315GdiLeakProbe --mode=paint|tick|docs|layout|session|bginvoke|plain --spectrum=&lt;xml&gt; [--frames=2000] [--cycles=12]
    ///                    [--sabotage=none|gdi|user|managed|pen|docref] [--work=&lt;каталог копии&gt;]
    ///
    /// ⚠ Спектр копируется в --work (по умолчанию %TEMP%): такт с автосохранением пишет файл
    /// документа, и корпус портить нельзя.
    /// Коды: 0 — роста нет (или подброшенная утечка поймана — для контроля); 4 — рост найден
    /// (без подброса — утечка приложения); 3 — подброшенная утечка НЕ поймана (проба не мерит);
    /// 5 — обвязка; 2 — ключи.
    /// </summary>
    static class P315GdiLeakProbe
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
        [DllImport("gdi32.dll")] static extern IntPtr CreatePen(int style, int width, int color);
        [DllImport("user32.dll")] static extern IntPtr CreateMenu();

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(int threadId, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        /// <summary>
        /// Окна процесса по классам: верхние окна всех потоков, их потомки и окна-сообщения
        /// (HWND_MESSAGE). USER-объект, который растёт, чаще всего — окно; класс его называет.
        /// </summary>
        static Dictionary<string, int> WindowClasses()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new HashSet<IntPtr>();
            EnumWindowsProc add = null;
            add = (h, l) =>
            {
                if (!seen.Add(h)) return true;
                var sb = new StringBuilder(256);
                GetClassName(h, sb, sb.Capacity);
                string name = sb.ToString();
                // классы WinForms несут номер домена и хэш: «WindowsForms10.Window.8.app.0.2bf8098_r6_ad1»
                int app = name.IndexOf(".app.", StringComparison.Ordinal);
                if (app > 0) name = name.Substring(0, app);
                Control owner = Control.FromHandle(h);
                if (owner != null) name += " = " + owner.GetType().Name + (string.IsNullOrEmpty(owner.Name) ? "" : "(" + owner.Name + ")");
                int n;
                map.TryGetValue(name, out n);
                map[name] = n + 1;
                EnumChildWindows(h, add, IntPtr.Zero);
                return true;
            };
            foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
            {
                EnumThreadWindows(t.Id, add, IntPtr.Zero);
            }
            int me = Process.GetCurrentProcess().Id;
            IntPtr HWND_MESSAGE = new IntPtr(-3);
            IntPtr child = IntPtr.Zero;
            while ((child = FindWindowEx(HWND_MESSAGE, child, null, null)) != IntPtr.Zero)
            {
                int pid;
                GetWindowThreadProcessId(child, out pid);
                if (pid == me) add(child, IntPtr.Zero);
            }
            return map;
        }

        static void PrintWindowDiff(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            var keys = new SortedSet<string>(a.Keys, StringComparer.Ordinal);
            keys.UnionWith(b.Keys);
            int total = 0;
            foreach (string k in keys)
            {
                int x, y;
                a.TryGetValue(k, out x);
                b.TryGetValue(k, out y);
                total += y;
                if (x != y) Console.WriteLine("ОКНА\t{0}: {1} → {2} ({3})", k, x, y, (y - x).ToString("+0;-0;0", CultureInfo.InvariantCulture));
            }
            Console.WriteLine("ОКНА\tвсего окон процесса: {0}", total);
        }

        static readonly List<object> SabotageKeep = new List<object>();
        static string sabotage = "none";
        static int rigFail;

        sealed class QuietHost : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }

            /// <summary>Сколько раз окно спрашивали WM_GETOBJECT — клиент доступности (MSAA/UIA).</summary>
            public static int GetObjectCount;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x003D) Interlocked.Increment(ref GetObjectCount);
                base.WndProc(ref m);
            }
        }

        sealed class QuietMainForm : MainForm
        {
            public QuietMainForm() : base(new string[0]) { }
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        struct Sample
        {
            public long Gdi, User, Handles, Managed, Private, Threads, GuiThreads;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string mode = "paint", path = null, work = null;
            int frames = 2000, cycles = 12;
            foreach (string a in args)
            {
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--frames=", StringComparison.Ordinal)) frames = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--cycles=", StringComparison.Ordinal)) cycles = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else if (a.StartsWith("--work=", StringComparison.Ordinal)) work = a.Substring(7);
                else if (a.StartsWith("--paint=", StringComparison.Ordinal)) paintPath = a.Substring(8);
                else if (a.StartsWith("--scenes=", StringComparison.Ordinal)) sceneFilter = a.Substring(9);
                else if (a.StartsWith("--fsa-each-round", StringComparison.Ordinal)) fsaEachRound = true;
                else if (a.StartsWith("--unsub=", StringComparison.Ordinal)) unsub = a.Substring(8);
                else if (a == "--session-invoke") sessionInvoke = true;
                else if (a.StartsWith("--experiment=", StringComparison.Ordinal)) experiment = a.Substring(13);
                else if (a.StartsWith("--settle=", StringComparison.Ordinal)) settleS = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a == "--census") withCensus = true;
                else if (a.StartsWith("--census-out=", StringComparison.Ordinal)) { withCensus = true; censusOut = a.Substring(13); }
                else if (a.StartsWith("--census-base=", StringComparison.Ordinal)) { withCensus = true; censusBase = a.Substring(14); }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null || !File.Exists(path))
            {
                Console.Error.WriteLine("нужен --spectrum=<существующий xml>");
                return 2;
            }
            work = work ?? Path.Combine(Path.GetTempPath(), "p315_leak");
            Directory.CreateDirectory(work);
            string copy = Path.Combine(work, Path.GetFileName(path));
            File.Copy(path, copy, true);

            Application.EnableVisualStyles();
            // ⛔ Контекст синхронизации окон — СВОЙ и навсегда. Без `Application.Run` каждый
            // `Application.DoEvents` в конце снимает WindowsFormsSynchronizationContext и оставляет
            // голый SynchronizationContext — продолжения `await` (поиск пиков
            // `DCPeakDetectionView.UpdatePeakDetectionResult`) уходили в пул и правили таблицу
            // чужим потоком: мерено 4006 строк «Peak detection», добавленных не с потока окон, и
            // NullReferenceException в XPTable на такте 5653. В приложении цикл сообщений держит
            // контекст сам — это свойство пробы, не дефект приложения.
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            Console.WriteLine("SETUP\tрежим {0}, спектр {1} (копия в {2}), подброс «{3}»", mode, Path.GetFileName(path), work, sabotage);
            int code;
            try
            {
                if (mode == "paint") code = RunPaint(copy, frames);
                else if (mode == "tick") code = RunTick(copy, frames);
                else if (mode == "docs") code = RunDocs(copy, cycles);
                else if (mode == "session") code = RunSession(copy, cycles);
                else if (mode == "bginvoke") code = RunBgInvoke(cycles);
                else if (mode == "layout") code = RunLayout(copy, cycles);
                else if (mode == "plain") code = RunPlain(cycles);
                else { Console.Error.WriteLine("неизвестный режим: " + mode); return 2; }
            }
            catch (Exception ex)
            {
                Console.WriteLine("⛔ обвязка: " + ex);
                return 5;
            }
            return code;
        }

        // ================================================================ paint

        static EnergySpectrumView view;
        static DocEnergySpectrum doc;
        static Random rng = new Random(315);

        static int RunPaint(string path, int frames)
        {
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            doc = Open(path, nuclides);
            ResultData rd = doc.ActiveResultData;
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            FsaCalculationOptions.ApplicationSource = () =>
            {
                FsaCalculationOptions o = FsaCalculationOptions.FromConfig(cfg);
                o.DbLookups = true;
                return o;
            };

            var host = new QuietHost
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-5000, -3000),
                ShowInTaskbar = false,
                ClientSize = new Size(1280, 760)
            };
            doc.TopLevel = false;
            doc.Dock = DockStyle.Fill;
            host.Controls.Add(doc);
            doc.Show();
            host.Show();
            doc.UpdateEnergySpectrum();
            doc.SetDefaultHorizontalScale();
            view = doc.EnergySpectrumView;
            Pump(200);
            Unsubscribe(doc.FsaSession);

            List<Scene> scenes = Scenes(rd);
            if (sceneFilter != null)
            {
                var keep = new List<Scene>();
                for (int i = 0; i < scenes.Count; i++)
                {
                    bool isFsa = !scenes[i].Acquire;
                    if ((sceneFilter == "nofsa" && !isFsa) || (sceneFilter == "fsa" && isFsa)
                        || sceneFilter == i.ToString(CultureInfo.InvariantCulture)) keep.Add(scenes[i]);
                }
                scenes = keep;
            }
            foreach (Scene sc in scenes) Console.WriteLine("SETUP\tсцена: {0}", sc.Name);
            int perScene = 20;
            int round = perScene * scenes.Count;
            int rounds = Math.Max(2, frames / round);
            Console.WriteLine("SETUP\tсцен {0}, кадров на сцену {1}, кругов {2} (+1 прогрев) — кадров в замере {3}",
                              scenes.Count, perScene, rounds, rounds * round);

            int painted = 0, drawn = 0;
            Action<int> frame = i =>
            {
                drawn++;
                if (PaintOnce()) painted++;
                Sabotage();
            };

            // прогрев: каждая сцена — один полный проход (кэши глифов, образцов, шрифтов)
            RunRound(scenes, perScene, frame);
            Console.WriteLine("SETUP\tпрогрев: кадров {0}, дошло до OnPaint {1}", drawn, painted);
            if (painted < drawn * 9 / 10)
            {
                Console.WriteLine("⛔ обвязка: WM_PAINT не доходит до вида — проба мерила бы пустоту");
                return 5;
            }

            var samples = new List<Sample>();
            samples.Add(Measure());
            Dictionary<string, int> windowsBefore = WindowClasses();
            Print("база", samples[0], samples[0], 0);
            Stopwatch sw = Stopwatch.StartNew();
            int counted = 0;
            for (int r = 0; r < rounds; r++)
            {
                RunRound(scenes, perScene, i => { frame(i); counted++; });
                Sample s = Measure();
                samples.Add(s);
                Print("круг " + (r + 1).ToString(CultureInfo.InvariantCulture), samples[0], s, counted);
            }
            Console.WriteLine("SETUP\tкадров {0} за {1} с; дошло до OnPaint {2} из {3}", counted,
                              (sw.ElapsedMilliseconds / 1000.0).ToString("F1", CultureInfo.InvariantCulture), painted, drawn);

            PrintWindowDiff(windowsBefore, WindowClasses());
            int verdict = Judge(samples, counted, "кадр");
            host.Hide();
            doc.Dispose();
            host.Dispose();
            return verdict;
        }

        sealed class Scene
        {
            public string Name;
            public Action Apply;
            public bool Acquire = true;
        }

        static List<Scene> Scenes(ResultData rd)
        {
            var list = new List<Scene>();
            bool hasBg = rd.BackgroundEnergySpectrum != null;
            list.Add(new Scene { Name = "линейная, столбики, каналы, отсчёты, фон виден", Apply = () =>
            {
                Base();
                view.VerticalScaleType = VerticalScaleType.LinearScale;
                view.ChartType = ChartType.BarChart;
                view.HorizontalUnit = HorizontalUnit.Channel;
                view.BackgroundMode = BackgroundMode.Visible;
            } });
            list.Add(new Scene { Name = "логарифмическая, линии, энергия, cps", Apply = () =>
            {
                Base();
                view.VerticalScaleType = VerticalScaleType.LogarithmicScale;
                view.ChartType = ChartType.LineChart;
                view.VerticalUnit = VerticalUnit.CountsPerSecond;
            } });
            list.Add(new Scene { Name = "степенная, подгонка MinMax, SMA", Apply = () =>
            {
                Base();
                view.VerticalScaleType = VerticalScaleType.PowerScale;
                view.VerticalFittingMode = VerticalFittingMode.MinMax;
                view.SmoothingMethod = SmoothingMethod.SimpleMovingAverage;
            } });
            list.Add(new Scene { Name = "линейная, WMA, рисование Normal", Apply = () =>
            {
                Base();
                view.SmoothingMethod = SmoothingMethod.WeightedMovingAverage;
                view.DrawingMode = DrawingMode.Normal;
            } });
            if (hasBg)
            {
                list.Add(new Scene { Name = "вычитание фона", Apply = () =>
                {
                    Base();
                    view.BackgroundMode = BackgroundMode.Substract;
                } });
            }
            list.Add(new Scene { Name = "континуум", Apply = () =>
            {
                Base();
                view.BackgroundMode = BackgroundMode.ShowContinuum;
            } });
            list.Add(new Scene { Name = "курсор с панелью, выделение области", Apply = () =>
            {
                Base();
                int ch = rd.EnergySpectrum.NumberOfChannels;
                Set(view, "selectionStart", ch / 8);
                Set(view, "selectionEnd", ch / 8 + ch / 64);
                Set(view, "validCursor", true);
                Set(view, "cursorChannel", ch / 6);
                Set(view, "cursorX", 300);
            } });
            list.Add(new Scene { Name = "подсветка пиков", Apply = () =>
            {
                Base();
                var peaks = new List<Peak>();
                if (rd.DetectedPeaks != null)
                {
                    for (int i = 0; i < rd.DetectedPeaks.Count && i < 3; i++) peaks.Add(rd.DetectedPeaks[i]);
                }
                typeof(EnergySpectrumView).GetProperty("HighlightedPeaks", Any).SetValue(view, peaks, null);
            } });
            list.Add(new Scene { Name = "FSA: слои, невязка, подписи", Acquire = false, Apply = () =>
            {
                Base();
                view.VerticalScaleType = VerticalScaleType.PowerScale;
                view.HorizontalUnit = HorizontalUnit.Energy;
                view.BackgroundMode = BackgroundMode.ShowFSA;
                PrepareOnce();
                FsaAnalysisSession session = doc.FsaSession;
                ResultData r = doc.ActiveResultData;
                if (fsaEachRound) session.Invalidate();
                session.EnsureUpToDate(r, r.BackgroundEnergySpectrum != null, FsaCalculationOptions.Of(r));
                WaitIdle(session, 300);
                if (session.Result == null && !fsaWarned)
                {
                    fsaWarned = true;
                    Console.WriteLine("  ⚠ разбора нет ({0}) — сцена рисует отказ, а не слои", session.Status ?? "?");
                }
            } });
            return list;
        }

        static bool fsaWarned;

        static void Base()
        {
            Set(view, "validCursor", false);
            Set(view, "selectionStart", -1);
            Set(view, "selectionEnd", -1);
            Set(view, "cursorX", -1);
            typeof(EnergySpectrumView).GetProperty("HighlightedPeaks", Any).SetValue(view, null, null);
            view.VerticalScaleType = VerticalScaleType.LinearScale;
            view.ChartType = ChartType.LineChart;
            view.HorizontalUnit = HorizontalUnit.Energy;
            view.VerticalUnit = VerticalUnit.Counts;
            view.VerticalFittingMode = VerticalFittingMode.None;
            view.SmoothingMethod = SmoothingMethod.None;
            view.DrawingMode = DrawingMode.HighDefinition;
            view.BackgroundMode = BackgroundMode.Visible;
            view.PeakMode = PeakMode.Visible;
        }

        static void RunRound(List<Scene> scenes, int perScene, Action<int> frame)
        {
            int n = 0;
            foreach (Scene scene in scenes)
            {
                scene.Apply();
                for (int i = 0; i < perScene; i++)
                {
                    if (scene.Acquire) Acquire(doc.ActiveResultData);
                    frame(n++);
                }
            }
        }

        /// <summary>Набор: несколько сотен отсчётов в случайные каналы — спектр меняется каждый кадр.</summary>
        static void Acquire(ResultData rd)
        {
            EnergySpectrum s = rd.EnergySpectrum;
            int n = s.NumberOfChannels;
            for (int k = 0; k < 300; k++)
            {
                int ch = (int)(n * Math.Pow(rng.NextDouble(), 2.0));
                s.Spectrum[ch]++;
            }
            s.TotalPulseCount += 300;
            s.ValidPulseCount += 300;
        }

        static void PrepareOnce()
        {
            view.RecalcChartParameters();
            view.PrepareViewData();
            view.RecalcScrollBar();
        }

        /// <summary>
        /// Один кадр — как такт графика `MainForm.RefreshDocumentChart`, и настоящий WM_PAINT.
        /// Признак «OnPaint дошёл»: `DrawChart` первым делом пишет `scrollBaseY` — до кадра там NaN.
        /// </summary>
        static bool PaintOnce()
        {
            PrepareOnce();
            Set(view, "scrollBaseY", double.NaN);
            view.Invalidate();
            if (paintPath == "wm")
            {
                view.Update();
            }
            else
            {
                // WM_PRINT: окно за краем экрана WM_PAINT не получает (мерено: 0 из 180 кадров),
                // а отрисовка та же — OnPaintBackground + OnPaint вида на Graphics кадра
                if (frameBitmap == null || frameBitmap.Width != view.Width || frameBitmap.Height != view.Height)
                {
                    if (frameBitmap != null) frameBitmap.Dispose();
                    frameBitmap = new Bitmap(Math.Max(1, view.Width), Math.Max(1, view.Height));
                }
                view.DrawToBitmap(frameBitmap, new Rectangle(0, 0, frameBitmap.Width, frameBitmap.Height));
            }
            Application.DoEvents();
            object sb = Field(typeof(EnergySpectrumView), "scrollBaseY").GetValue(view);
            return !double.IsNaN((double)sb);
        }

        // ---------------------------------------------------------------- кто держит

        sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        sealed class Node
        {
            public object Obj;
            public Node Parent;
            public string Label;
        }

        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        static FieldInfo[] RefFields(Type t)
        {
            FieldInfo[] list;
            if (fieldCache.TryGetValue(t, out list)) return list;
            var all = new List<FieldInfo>();
            for (Type x = t; x != null && x != typeof(object); x = x.BaseType)
            {
                foreach (FieldInfo f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.FieldType.IsPointer
                        || f.FieldType == typeof(IntPtr) || f.FieldType == typeof(UIntPtr)) continue;
                    all.Add(f);
                }
            }
            list = all.ToArray();
            fieldCache[t] = list;
            return list;
        }

        static bool Skip(object o)
        {
            if (o == null || o is string || o is Type || o is MemberInfo || o is Assembly || o is Module || o is Pointer) return true;
            Type t = o.GetType();
            return t.IsPrimitive || t.IsEnum || t.IsPointer;
        }

        /// <summary>
        /// Путь удержания: обход графа объектов в ширину от корней (статические поля всех типов
        /// приложения, WinForms и DockPanel; переданные объекты) по полям, элементам массивов и
        /// целям делегатов. Печатает цепочку полей до <paramref name="target"/>; null — не найден.
        /// </summary>
        static string PathTo(object target, params KeyValuePair<string, object>[] extraRoots)
        {
            var visited = new HashSet<object>(new RefEq());
            var queue = new Queue<Node>();
            Action<object, Node, string> push = (o, parent, label) =>
            {
                if (Skip(o) || !visited.Add(o)) return;
                queue.Enqueue(new Node { Obj = o, Parent = parent, Label = label });
            };
            foreach (KeyValuePair<string, object> r in extraRoots) push(r.Value, null, r.Key);
            // System.dll — ради Microsoft.Win32.SystemEvents (её статический словарь подписчиков
            // держит окна, подписавшиеся на смену настроек системы); System.Drawing — кэши
            foreach (Assembly asm in new[] { typeof(MainForm).Assembly, typeof(Form).Assembly, typeof(WeifenLuo.WinFormsUI.Docking.DockPanel).Assembly,
                                             typeof(Microsoft.Win32.SystemEvents).Assembly, typeof(Pen).Assembly, typeof(object).Assembly })
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (Type t in types)
                {
                    if (t == null || t.ContainsGenericParameters) continue;
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.IsLiteral) continue;
                        object v;
                        try { v = f.GetValue(null); } catch { continue; }
                        push(v, null, t.FullName + "::" + f.Name);
                    }
                }
            }
            int steps = 0;
            while (queue.Count > 0 && steps < 3000000)
            {
                Node n = queue.Dequeue();
                steps++;
                if (ReferenceEquals(n.Obj, target))
                {
                    var parts = new List<string>();
                    for (Node x = n; x != null; x = x.Parent) parts.Add(x.Label);
                    parts.Reverse();
                    return string.Join(" → ", parts.ToArray());
                }
                object o = n.Obj;
                if (o is Array arr)
                {
                    Type et = arr.GetType().GetElementType();
                    if (et.IsPrimitive || et.IsEnum || et == typeof(string) || arr.Rank != 1) continue;
                    int len = arr.Length;
                    for (int i = 0; i < len; i++)
                    {
                        object e = arr.GetValue(i);
                        if (e == null) continue;
                        if (et.IsValueType)
                        {
                            foreach (FieldInfo f in RefFields(et))
                            {
                                object v = f.GetValue(e);
                                push(v, n, "[" + i.ToString(CultureInfo.InvariantCulture) + "]." + f.Name);
                            }
                        }
                        else
                        {
                            push(e, n, "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
                        }
                    }
                    continue;
                }
                foreach (FieldInfo f in RefFields(o.GetType()))
                {
                    object v;
                    try { v = f.GetValue(o); } catch { continue; }
                    if (v == null) continue;
                    if (f.FieldType.IsValueType && !(v is Delegate))
                    {
                        // поле-структура: её ссылочные поля — на шаг глубже, той же меткой
                        foreach (FieldInfo g in RefFields(v.GetType()))
                        {
                            object w;
                            try { w = g.GetValue(v); } catch { continue; }
                            push(w, n, o.GetType().Name + "." + f.Name + "." + g.Name);
                        }
                        continue;
                    }
                    push(v, n, o.GetType().Name + "." + f.Name);
                }
            }
            Console.WriteLine("  (обход: {0} объектов{1})", steps.ToString(CultureInfo.InvariantCulture),
                              queue.Count > 0 ? ", ОБОРВАН пределом — путь мог не найтись" : ", граф пройден целиком");
            return null;
        }

        /// <summary>
        /// Перепись достижимого: те же корни, что у <see cref="PathTo"/>, — число объектов и
        /// примерный размер массивов по типу. Разность двух переписей называет то, что растёт.
        /// </summary>
        static Dictionary<string, long[]> Census(params KeyValuePair<string, object>[] extraRoots)
        {
            var visited = new HashSet<object>(new RefEq());
            var stack = new Stack<object>();
            Action<object> push = o => { if (!Skip(o) && visited.Add(o)) stack.Push(o); };
            foreach (KeyValuePair<string, object> r in extraRoots) push(r.Value);
            foreach (Assembly asm in new[] { typeof(MainForm).Assembly, typeof(Form).Assembly, typeof(WeifenLuo.WinFormsUI.Docking.DockPanel).Assembly,
                                             typeof(Microsoft.Win32.SystemEvents).Assembly, typeof(Pen).Assembly, typeof(object).Assembly })
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (Type t in types)
                {
                    if (t == null || t.ContainsGenericParameters) continue;
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.IsLiteral) continue;
                        object v;
                        try { v = f.GetValue(null); } catch { continue; }
                        push(v);
                    }
                }
            }
            var map = new Dictionary<string, long[]>(StringComparer.Ordinal);
            while (stack.Count > 0)
            {
                object o = stack.Pop();
                Type ot = o.GetType();
                string key = ot.FullName ?? ot.Name;
                long[] cell;
                if (!map.TryGetValue(key, out cell)) { cell = new long[2]; map[key] = cell; }
                cell[0]++;
                if (o is Array arr)
                {
                    Type et = ot.GetElementType();
                    int size = et.IsValueType ? (et == typeof(double) || et == typeof(long) ? 8 : et == typeof(byte) || et == typeof(bool) ? 1 : et == typeof(char) || et == typeof(short) ? 2 : 4) : 8;
                    cell[1] += (long)arr.Length * size;
                    if (et.IsPrimitive || et.IsEnum || et == typeof(string) || arr.Rank != 1) continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        object e = arr.GetValue(i);
                        if (e == null) continue;
                        if (et.IsValueType)
                        {
                            foreach (FieldInfo f in RefFields(et)) push(f.GetValue(e));
                        }
                        else push(e);
                    }
                    continue;
                }
                foreach (FieldInfo f in RefFields(ot))
                {
                    object v;
                    try { v = f.GetValue(o); } catch { continue; }
                    if (v == null) continue;
                    if (f.FieldType.IsValueType && !(v is Delegate))
                    {
                        foreach (FieldInfo g in RefFields(v.GetType()))
                        {
                            object w;
                            try { w = g.GetValue(v); } catch { continue; }
                            push(w);
                        }
                        continue;
                    }
                    push(v);
                }
            }
            return map;
        }

        /// <summary>Перепись в конце прогона: записать (`--census-out=`) и сравнить с прошлой (`--census-base=`).</summary>
        static void CensusReport(object root)
        {
            Measure();
            Dictionary<string, long[]> now = Census(new KeyValuePair<string, object>("root", root));
            if (censusOut != null)
            {
                var lines = new List<string>();
                foreach (KeyValuePair<string, long[]> kv in now)
                {
                    lines.Add(kv.Key + "\t" + kv.Value[0].ToString(CultureInfo.InvariantCulture) + "\t" + kv.Value[1].ToString(CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(censusOut, lines.ToArray(), Encoding.UTF8);
                Console.WriteLine("SETUP\tперепись: {0} типов → {1}", now.Count, censusOut);
            }
            if (censusBase != null && File.Exists(censusBase))
            {
                var old = new Dictionary<string, long[]>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(censusBase, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length == 3) old[p[0]] = new[] { long.Parse(p[1], CultureInfo.InvariantCulture), long.Parse(p[2], CultureInfo.InvariantCulture) };
                }
                PrintCensusDiff(old, now, 30);
            }
        }

        static void PrintCensusDiff(Dictionary<string, long[]> a, Dictionary<string, long[]> b, int top)
        {
            var rows = new List<KeyValuePair<string, long[]>>();
            foreach (KeyValuePair<string, long[]> kv in b)
            {
                long[] old;
                a.TryGetValue(kv.Key, out old);
                long dn = kv.Value[0] - (old != null ? old[0] : 0), db = kv.Value[1] - (old != null ? old[1] : 0);
                if (dn != 0 || db != 0) rows.Add(new KeyValuePair<string, long[]>(kv.Key, new[] { dn, db, kv.Value[0] }));
            }
            rows.Sort((x, y) => Math.Abs(y.Value[0] * 32 + y.Value[1]).CompareTo(Math.Abs(x.Value[0] * 32 + x.Value[1])));
            for (int i = 0; i < rows.Count && i < top; i++)
            {
                Console.WriteLine("ПЕРЕПИСЬ\t{0}: объектов {1} (всего {2}), массивов +{3} Б", rows[i].Key,
                                  rows[i].Value[0].ToString("+0;-0;0", CultureInfo.InvariantCulture),
                                  rows[i].Value[2].ToString(CultureInfo.InvariantCulture),
                                  rows[i].Value[1].ToString(CultureInfo.InvariantCulture));
            }
        }

        static string paintPath = "print";
        static string sceneFilter;
        static string unsub;
        static readonly HashSet<int> callers = new HashSet<int>();
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();

        static string CallerThreads()
        {
            var alive = new HashSet<int>();
            foreach (ProcessThread t in Process.GetCurrentProcess().Threads) alive.Add(t.Id);
            int live = 0;
            lock (callers) { foreach (int id in callers) if (alive.Contains(id)) live++; return callers.Count.ToString(CultureInfo.InvariantCulture) + " потоков звали, живы " + live.ToString(CultureInfo.InvariantCulture); }
        }
        static bool sessionInvoke;
        static string experiment;
        static int settleS;
        static bool withCensus;
        static string censusOut, censusBase;

        /// <summary>
        /// Кэш квадратуры Клейна — Нишины мощности дозы (<c>DoseRateCoefficients.comptonCache</c>,
        /// ключ — энергия кэВ): число записей. Перепись тактов назвала его источником роста.
        /// </summary>
        static int ComptonCacheCount()
        {
            FieldInfo f = typeof(DoseRateCoefficients).GetField("comptonCache", BindingFlags.Static | BindingFlags.NonPublic);
            var d = f != null ? f.GetValue(null) as System.Collections.IDictionary : null;
            return d != null ? d.Count : -1;
        }

        /// <summary>
        /// (`--experiment=capcache`) Предлагаемая правка `DoseRate.cs` — изображением, без правки
        /// чужого файла: кэш с пределом 4096 записей (переполнился — очистить). Если рост памяти
        /// такта уходит — источник один.
        /// </summary>
        static void CapComptonCache()
        {
            if (!Exp("capcache")) return;
            FieldInfo f = typeof(DoseRateCoefficients).GetField("comptonCache", BindingFlags.Static | BindingFlags.NonPublic);
            var d = f != null ? f.GetValue(null) as System.Collections.IDictionary : null;
            if (d == null) return;
            lock (d)
            {
                if (d.Count >= 4096) d.Clear();
            }
        }

        /// <summary>Ключ опыта в списке через запятую: <c>--experiment=noacc,nofix</c>.</summary>
        static bool Exp(string token)
        {
            return experiment != null && Array.IndexOf(experiment.Split(','), token) >= 0;
        }

        /// <summary>
        /// (`--experiment=nofix`) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРАВОК П315 без второй сборки: снять в
        /// живом приложении обе правки отражением — чистку меню у темы
        /// (<c>MainForm.DockPanel_ContentRemoved</c>) и отписку панели от одиночки
        /// (<c>DCControlPanel.DCControlPanel_Disposed</c>). Утечка обязана вернуться.
        /// </summary>
        static void UndoFixes(MainForm mf)
        {
            if (!Exp("nofix")) return;
            var panel = FieldValue(mf, "dockPanel1") as WeifenLuo.WinFormsUI.Docking.DockPanel;
            MethodInfo handler = typeof(MainForm).GetMethod("DockPanel_ContentRemoved", Any);
            if (panel != null && handler != null)
            {
                Delegate d = Delegate.CreateDelegate(typeof(EventHandler<WeifenLuo.WinFormsUI.Docking.DockContentEventArgs>), mf, handler);
                panel.ContentRemoved -= (EventHandler<WeifenLuo.WinFormsUI.Docking.DockContentEventArgs>)d;
                Console.WriteLine("SETUP\tnofix: снята чистка меню у темы (MainForm.DockPanel_ContentRemoved)");
            }
            else
            {
                Console.WriteLine("SETUP\tnofix: чистки меню у темы в сборке НЕТ — сборка до правки");
            }
            undoPanelFix = typeof(DCControlPanel).GetMethod("DCControlPanel_Disposed", Any) != null;
        }

        static bool undoPanelFix;

        /// <summary>Снять с панели отписку от одиночки (nofix) — для каждой новой панели.</summary>
        static void UndoPanelFix(MainForm mf)
        {
            if (!undoPanelFix) return;
            var p = FieldValue(mf, "dcControlPanel") as DCControlPanel;
            if (p == null) return;
            MethodInfo handler = typeof(DCControlPanel).GetMethod("DCControlPanel_Disposed", Any);
            p.Disposed -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), p, handler);
        }

        // ---------------------------------------------------------------- клиент доступности

        /// <summary>
        /// (`--experiment=noacc`) Заслон WM_GETOBJECT: внешний клиент доступности (MSAA/UIA — на
        /// этой машине он спрашивает каждое новое окно, мерено: 67 запросов к 6 скрытым формам)
        /// получает COM-ссылку на <c>AccessibleObject</c> окна и держит её у себя — это корень,
        /// которого обход полей не видит, и закрытая форма живёт, пока клиент не отпустит. Заслон —
        /// внешний подкласс поверх WinForms: на WM_GETOBJECT отвечает 0 (клиент берёт стандартный
        /// прокси системы, без управляемых объектов). Ставится на окна процесса ДО разбора очереди:
        /// межпроцессный запрос доходит до окна только при разборе.
        /// </summary>
        sealed class AccBlocker : NativeWindow
        {
            public static int Blocked;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x003D)
                {
                    Interlocked.Increment(ref Blocked);
                    m.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref m);
            }
        }

        static readonly HashSet<IntPtr> blockedWindows = new HashSet<IntPtr>();

        static void BlockAccessibility()
        {
            if (!Exp("noacc")) return;
            var all = new List<IntPtr>();
            EnumWindowsProc add = null;
            add = (h, l) =>
            {
                all.Add(h);
                EnumChildWindows(h, add, IntPtr.Zero);
                return true;
            };
            foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
            {
                if (t.Id == GetCurrentThreadId()) EnumThreadWindows(t.Id, add, IntPtr.Zero);
            }
            foreach (IntPtr h in all)
            {
                if (!blockedWindows.Add(h)) continue;
                new AccBlocker().AssignHandle(h);
            }
        }

        /// <summary>Развилка: снять подписчиков сеанса (doc / view / both) — кто из них растит USER.</summary>
        static void Unsubscribe(FsaAnalysisSession session)
        {
            if (unsub == null) return;
            foreach (string ev in new[] { "Completed", "Started" })
            {
                FieldInfo f = typeof(FsaAnalysisSession).GetField(ev, BindingFlags.Instance | BindingFlags.NonPublic);
                var d = f != null ? f.GetValue(session) as Delegate : null;
                if (d == null) continue;
                foreach (Delegate h in d.GetInvocationList())
                {
                    bool isDoc = h.Target is DocEnergySpectrum, isView = h.Target is EnergySpectrumView;
                    if ((unsub == "both" && (isDoc || isView)) || (unsub == "doc" && isDoc) || (unsub == "view" && isView))
                    {
                        typeof(FsaAnalysisSession).GetEvent(ev).RemoveEventHandler(session, h);
                        Console.WriteLine("SETUP\tснят подписчик {0}.{1} → {2}", ev, h.Method.Name, h.Target.GetType().Name);
                    }
                    else
                    {
                        Console.WriteLine("SETUP\tоставлен подписчик {0}.{1} → {2}", ev, h.Method.Name, h.Target != null ? h.Target.GetType().Name : "static");
                    }
                }
            }
        }
        static bool fsaEachRound;
        static Bitmap frameBitmap;

        // ================================================================ session

        /// <summary>
        /// Развилка «рост на пересчёт FSA»: только сеанс разбора, без документа, вида и окон.
        /// Цикл — Invalidate + EnsureUpToDate + ожидание покоя, ровно как пересчёт при наборе.
        /// </summary>
        static int RunSession(string path, int cycles)
        {
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            DocEnergySpectrum d = Open(path, nuclides);
            ResultData rd = d.ActiveResultData;
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            FsaCalculationOptions.ApplicationSource = () =>
            {
                FsaCalculationOptions o = FsaCalculationOptions.FromConfig(cfg);
                o.DbLookups = true;
                return o;
            };
            var session = new FsaAnalysisSession();
            if (sessionInvoke)
            {
                // подписчик-двойник вида и документа: только BeginInvoke в окно с дескриптором
                var host = new QuietHost { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -3000), ShowInTaskbar = false };
                host.Show();
                Pump(100);
                MethodInvoker nothing = delegate { };
                session.Completed += (o, e) =>
                {
                    lock (callers) callers.Add(GetCurrentThreadId());
                    if (sessionInvoke) host.BeginInvoke(nothing);
                };
                Console.WriteLine("SETUP\tподписчик Completed: BeginInvoke(пусто) в скрытое окно");
            }
            Action run = () =>
            {
                session.Invalidate();
                session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, FsaCalculationOptions.Of(rd));
                WaitIdle(session, 300);
                Sabotage();
            };
            for (int i = 0; i < 3; i++) run();
            Console.WriteLine("SETUP\tразбор: {0}", session.Result != null ? "есть, χ²/ndf " + session.Result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture) : "нет — " + (session.Status ?? "?"));
            var samples = new List<Sample>();
            samples.Add(Measure());
            Print("база", samples[0], samples[0], 0);
            int step = Math.Max(1, cycles / 6);
            int counted = 0;
            while (counted < cycles)
            {
                for (int i = 0; i < step && counted < cycles; i++) { run(); counted++; }
                Sample s = Measure();
                samples.Add(s);
                Print("пересчётов " + counted.ToString(CultureInfo.InvariantCulture), samples[0], s, counted);
                Console.WriteLine("ПОТОКИ\t{0}", CallerThreads());
            }
            return Judge(samples, counted, "пересчёт");
        }

        // ================================================================ plain

        /// <summary>
        /// Контроль среды для «docs»: простая форма БЕЗ кода приложения (кнопка, поле, панель
        /// инструментов, контекстное меню) — показать и закрыть N раз. Если и она не собирается,
        /// держит среда (клиент доступности и т. п.), а не приложение.
        /// </summary>
        static int RunPlain(int cycles)
        {
            var forms = new List<WeakReference>();
            for (int c = 0; c < cycles; c++)
            {
                var f = new QuietHost { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -3000), ShowInTaskbar = false, ClientSize = new Size(400, 300) };
                var strip = new ToolStrip();
                strip.Items.Add(new ToolStripButton("a"));
                f.Controls.Add(strip);
                f.Controls.Add(new TextBox { Location = new Point(10, 40) });
                f.Controls.Add(new Button { Location = new Point(10, 80), Text = "b" });
                var menu = new ContextMenuStrip();
                menu.Items.Add("x");
                menu.Opening += (s, e) => { GC.KeepAlive(f); };
                f.ContextMenuStrip = menu;
                if (Exp("allowdrop")) f.AllowDrop = true;
                f.Show();
                BlockAccessibility();
                Pump(100);
                forms.Add(new WeakReference(f));
                f.Close();
                f.Dispose();
                menu.Dispose();
                Pump(50);
            }
            Pump(500);
            Measure();
            int alive = 0;
            foreach (WeakReference w in forms) if (w.IsAlive) alive++;
            Console.WriteLine("ИТОГ\tпростых форм показано-закрыто {0}, живы после сборки {1}; WM_GETOBJECT к формам: {2}, отклонено заслоном {3}",
                              forms.Count, alive, QuietHost.GetObjectCount, AccBlocker.Blocked);
            if (alive > 0)
            {
                object first = null;
                foreach (WeakReference w in forms) { first = w.Target; if (first != null) break; }
                Console.WriteLine("ИТОГ\tдержит: {0}", PathTo(first) ?? "путь от статических корней не найден");
            }
            return alive > 1 ? 4 : 0;
        }

        // ================================================================ layout

        /// <summary>
        /// Смена раскладки (меню «User layout» / «Expert layout») пересоздаёт окна-панели
        /// (<c>MainForm.InitializeToolViews</c>), прежние закрываются. Живы ли прежние панели
        /// после сборки и что с ними делает событие одиночки <c>DeviceConfigManager</c>
        /// (на него подписана <c>DCControlPanel</c>).
        /// </summary>
        static int RunLayout(string path, int cycles)
        {
            // ⛔ смена раскладки ПИШЕТ файл раскладки рядом с пробой (`config\layout`); каталога
            // до прогона не было — снять его после, иначе следующий запуск откроет документ из него
            string layoutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "layout");
            bool layoutDirExisted = Directory.Exists(layoutDir);
            try
            {
                return RunLayoutCore(path, cycles);
            }
            finally
            {
                if (!layoutDirExisted && Directory.Exists(layoutDir))
                {
                    Directory.Delete(layoutDir, true);
                    Console.WriteLine("SETUP\tснят файл раскладки, записанный сменами: {0}", layoutDir);
                }
            }
        }

        static int RunLayoutCore(string path, int cycles)
        {
            QuietMainForm mf = ShowMainForm();
            if (mf == null) return 5;
            UndoFixes(mf);
            UndoPanelFix(mf);
            DocEnergySpectrum d = OpenInMainForm(mf, path);
            if (d == null) return 5;
            MethodInfo user = typeof(MainForm).GetMethod("userLayoutToolStripMenuItem_Click", Any);
            MethodInfo expert = typeof(MainForm).GetMethod("expertLayoutToolStripMenuItem_Click", Any);
            var panels = new List<WeakReference>();
            // (`AMBER276`, 29.10.2026) Поля `dcCFwhmCalibrationView` больше нет: калибровка
            // энергии и FWHM — одна вкладка в поле `dcEnergyCalibrationView`.
            string[] viewFields = { "dcControlPanel", "dcPulseView", "dcSampleInfoView", "dcSpectrumListView", "dcPeakDetectionView",
                                    "dcEnergyCalibrationView", "dcCountRateView", "dcFsaReportView" };
            foreach (string f in viewFields)
            {
                // Снятое поле молча дало бы WeakReference(null) — «вид отпущен» без вида.
                if (typeof(MainForm).GetField(f, Any) == null) throw new MissingFieldException("MainForm", f);
            }
            var oldViews = new List<KeyValuePair<string, WeakReference>>();
            var samples = new List<Sample>();
            Action switchOnce = () =>
            {
                foreach (DocEnergySpectrum open in DocumentManager.GetInstance().DocumentList) open.Dirty = false;
                panels.Add(new WeakReference(FieldValue(mf, "dcControlPanel")));
                foreach (string f in viewFields) oldViews.Add(new KeyValuePair<string, WeakReference>(f, new WeakReference(FieldValue(mf, f))));
                object rvl = FieldValue(mf, "dcResultViewList");
                if (rvl is System.Collections.IEnumerable rvs)
                {
                    foreach (object rv in rvs) oldViews.Add(new KeyValuePair<string, WeakReference>("dcResultViewList[]", new WeakReference(rv)));
                }
                object mode = FieldValue(mf, "layoutMode");
                bool toUser = mode != null && mode.ToString() == "ExpertMode";
                (toUser ? user : expert).Invoke(mf, new object[] { null, EventArgs.Empty });
                UndoPanelFix(mf);
                BlockAccessibility();
                Pump(100);
                Sabotage();
            };
            // прогрев — две смены (туда и обратно)
            switchOnce();
            switchOnce();
            samples.Add(Measure());
            Print("база", samples[0], samples[0], 0);
            for (int c = 1; c <= cycles; c++)
            {
                switchOnce();
                if (c % 2 == 0 || c == cycles)
                {
                    Sample s = Measure();
                    samples.Add(s);
                    Print("смен " + c.ToString(CultureInfo.InvariantCulture), samples[0], s, c);
                }
            }
            Measure();
            object current = FieldValue(mf, "dcControlPanel");
            int alive = 0, aliveDisposed = 0;
            foreach (WeakReference w in panels)
            {
                object p = w.Target;
                if (p == null || ReferenceEquals(p, current)) continue;
                alive++;
                if (((Control)p).IsDisposed) aliveDisposed++;
            }
            Console.WriteLine("ИТОГ\tсмен раскладки {0}: прежних панелей DCControlPanel живо после сборки {1} (из них закрытых {2})",
                              panels.Count, alive, aliveDisposed);
            var currentViews = new HashSet<object>();
            foreach (string f in viewFields) { object v = FieldValue(mf, f); if (v != null) currentViews.Add(v); }
            if (FieldValue(mf, "dcResultViewList") is System.Collections.IEnumerable nowRvs)
            {
                foreach (object rv in nowRvs) currentViews.Add(rv);
            }
            var aliveByField = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var sampleByField = new Dictionary<string, object>(StringComparer.Ordinal);
            var seenViews = new HashSet<object>();
            foreach (KeyValuePair<string, WeakReference> kv in oldViews)
            {
                object v = kv.Value.Target;
                if (v == null || currentViews.Contains(v) || !seenViews.Add(v)) continue;
                int n;
                aliveByField.TryGetValue(kv.Key, out n);
                aliveByField[kv.Key] = n + 1;
                sampleByField[kv.Key] = v;
            }
            foreach (KeyValuePair<string, object> kv in sampleByField)
            {
                string chain = PathTo(kv.Value, new KeyValuePair<string, object>("MainForm", mf));
                Console.WriteLine("ДЕРЖИТ\t{0}: {1}", kv.Key, chain ?? "путь от статических корней и MainForm не найден");
            }
            int aliveOther = 0;
            foreach (KeyValuePair<string, int> kv in aliveByField)
            {
                Console.WriteLine("ИТОГ\tпрежних {0} живо после сборки: {1}", kv.Key, kv.Value);
                if (kv.Key != "dcControlPanel") aliveOther += kv.Value;
            }
            if (aliveByField.Count == 0) Console.WriteLine("ИТОГ\tпрежних окон-панелей живых после сборки нет");

            // событие одиночки после смен — как при сохранении конфигурации прибора
            FieldInfo evf = typeof(DeviceConfigManager).GetField("DeviceConfigListChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            var handlers = evf != null ? evf.GetValue(DeviceConfigManager.GetInstance()) as Delegate : null;
            int panelHandlers = 0, failures = 0;
            if (handlers != null)
            {
                DeviceConfigInfo first = DeviceConfigManager.GetInstance().DeviceConfigList.Count > 0
                    ? DeviceConfigManager.GetInstance().DeviceConfigList[0] : null;
                foreach (Delegate h in handlers.GetInvocationList())
                {
                    if (h.Target is DCControlPanel) panelHandlers++;
                    try
                    {
                        h.DynamicInvoke(DeviceConfigManager.GetInstance(), new DeviceConfigChangedEventArgs(first != null ? first.Guid : null));
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Exception inner = ex.InnerException ?? ex;
                        Console.WriteLine("ИТОГ\tподписчик {0}.{1} (закрыт: {2}) бросил {3}: {4}", h.Target != null ? h.Target.GetType().Name : "static",
                                          h.Method.Name, h.Target is Control ? ((Control)h.Target).IsDisposed.ToString() : "—",
                                          inner.GetType().Name, inner.Message);
                    }
                }
            }
            Console.WriteLine("ИТОГ\tподписчиков DeviceConfigListChanged из DCControlPanel: {0} (живая панель одна); бросков при событии {1}",
                              panelHandlers, failures);
            int verdict = Judge(samples, cycles, "смену");
            CloseMainForm(mf);
            // одна прежняя панель без пути от корней — не рост (последняя смена; клиент
            // доступности, опросивший окно до заслона); рост — когда их больше одной
            if (alive > 0 || aliveOther > 1 || panelHandlers > 1 || failures > 0)
            {
                Console.WriteLine("⛔ РОСТ: прежние окна-панели живут после смен раскладки (панелей управления {0}, прочих {1}, подписчиков {2}, бросков {3})",
                                  alive, aliveOther, panelHandlers, failures);
                return 4;
            }
            return verdict;
        }

        // ================================================================ bginvoke

        /// <summary>
        /// Развилка «рост на пересчёт FSA» доведена до одного вызова: оба подписчика сеанса
        /// (вид и документ) зовут <c>Control.BeginInvoke</c> С ФОНОВОГО потока пула. Здесь —
        /// только это: окно с дескриптором, N раз Task.Run → BeginInvoke(пусто) → покой.
        /// Печатает, растёт ли USER без предела или встаёт на полку числа потоков пула.
        /// </summary>
        static int RunBgInvoke(int cycles)
        {
            var host = new QuietHost { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -3000), ShowInTaskbar = false };
            host.Show();
            Pump(100);
            MethodInvoker nothing = delegate { };
            var samples = new List<Sample>();
            Action run = () =>
            {
                // занять пул, как занимает его счёт: несколько задач разом, вызов — из одной
                Task<int>[] busy = new Task<int>[4];
                for (int i = 0; i < busy.Length; i++)
                {
                    int k = i;
                    busy[i] = Task.Run(() =>
                    {
                        if (k == 0) host.BeginInvoke(nothing);
                        Thread.Sleep(5);
                        return k;
                    });
                }
                Task.WaitAll(busy);
                Application.DoEvents();
                Sabotage();
            };
            for (int i = 0; i < 50; i++) run();
            samples.Add(Measure());
            Print("база", samples[0], samples[0], 0);
            int step = Math.Max(1, cycles / 6);
            int counted = 0;
            while (counted < cycles)
            {
                for (int i = 0; i < step && counted < cycles; i++) { run(); counted++; }
                Sample s = Measure();
                samples.Add(s);
                Print("вызовов " + counted.ToString(CultureInfo.InvariantCulture), samples[0], s, counted);
            }
            int verdict = Judge(samples, counted, "вызов");
            host.Dispose();
            return verdict;
        }

        // ================================================================ tick

        static int RunTick(string path, int frames)
        {
            QuietMainForm mf = ShowMainForm();
            if (mf == null) return 5;
            DocEnergySpectrum d = OpenInMainForm(mf, path);
            if (d == null) return 5;
            ResultData rd = d.ActiveResultData;
            StartFakeRecording(d, rd);
            MethodInfo onTimer = typeof(MainForm).GetMethod("OnTimer", Any, null, new[] { typeof(object), typeof(EventArgs) }, null);
            Console.WriteLine("SETUP\tокон-панелей у MainForm: {0}; активный документ {1}; контекст синхронизации потока окон: {2}",
                              CountDockContents(mf), mf.ActiveDocument != null ? mf.ActiveDocument.Text : "нет",
                              SynchronizationContext.Current != null ? SynchronizationContext.Current.GetType().Name : "НЕТ");
            // сторож потока: таблицу «Peak detection» правят только с потока окон
            int uiThread = Thread.CurrentThread.ManagedThreadId;
            int foreignAdds = 0;
            string foreignStack = null;
            object peakView = FieldValue(mf, "dcPeakDetectionView");
            var peakModel = peakView != null ? FieldValue(peakView, "tableModel1") as XPTable.Models.TableModel : null;
            if (peakModel != null)
            {
                peakModel.RowAdded += (s, e) =>
                {
                    if (Thread.CurrentThread.ManagedThreadId != uiThread)
                    {
                        Interlocked.Increment(ref foreignAdds);
                        if (foreignStack == null) foreignStack = Environment.StackTrace;
                    }
                };
            }

            int ticks = 0;
            Action tick = () =>
            {
                Acquire(rd);
                rd.ResultDataStatus.ElapsedTime += TimeSpan.FromMilliseconds(100);
                rd.ResultDataStatus.TotalTime += TimeSpan.FromMilliseconds(100);
                d.UpdateSpectrum = true;
                try
                {
                    onTimer.Invoke(mf, new object[] { null, EventArgs.Empty });
                }
                catch (Exception)
                {
                    Console.WriteLine("ТАКТ\tупал на такте {0}; строк таблицы «Peak detection», добавленных НЕ с потока окон: {1}",
                                      ticks, foreignAdds);
                    if (foreignStack != null) Console.WriteLine("ТАКТ\tстек чужого потока: " + foreignStack.Replace(Environment.NewLine, " | "));
                    throw;
                }
                CapComptonCache();
                Application.DoEvents();
                Sabotage();
                ticks++;
            };

            // прогрев — 1000 тактов (100 с набора): все счётчики MainForm прошли хотя бы раз
            for (int i = 0; i < 1000; i++) tick();
            var samples = new List<Sample>();
            samples.Add(Measure());
            Print("база", samples[0], samples[0], 0);
            var root = new KeyValuePair<string, object>("MainForm", mf);
            // ⚠ перепись читает статические поля ВСЕХ типов и тем запускает их статические
            // конструкторы — посреди прогона это меняет состояние (мерено: после переписи на
            // базе такт упал в XPTable). Поэтому перепись — только в конце прогона, а разность —
            // между двумя прогонами разной длины (`--census-out=`).
            Dictionary<string, long[]> census0 = null;
            int counted = 0;
            int step = Math.Max(1, frames / 6);
            Stopwatch sw = Stopwatch.StartNew();
            while (counted < frames)
            {
                for (int i = 0; i < step && counted < frames; i++) { tick(); counted++; }
                Sample s = Measure();
                samples.Add(s);
                Print("тактов " + counted.ToString(CultureInfo.InvariantCulture), samples[0], s, counted);
                Console.WriteLine("КЭШ\tзаписей в кэше Клейна — Нишины мощности дозы: {0}", ComptonCacheCount());
            }
            Console.WriteLine("SETUP\tтактов {0} (≈{1} мин набора) за {2} с", counted,
                              (counted / 600.0).ToString("F1", CultureInfo.InvariantCulture),
                              (sw.ElapsedMilliseconds / 1000.0).ToString("F1", CultureInfo.InvariantCulture));
            if (withCensus)
            {
                Measure();
                Dictionary<string, long[]> census1 = Census(root);
                if (censusOut != null)
                {
                    var lines = new List<string>();
                    foreach (KeyValuePair<string, long[]> kv in census1)
                    {
                        lines.Add(kv.Key + "\t" + kv.Value[0].ToString(CultureInfo.InvariantCulture) + "\t" + kv.Value[1].ToString(CultureInfo.InvariantCulture));
                    }
                    File.WriteAllLines(censusOut, lines.ToArray(), Encoding.UTF8);
                    Console.WriteLine("SETUP\tперепись: {0} типов → {1}", census1.Count, censusOut);
                }
                if (censusBase != null && File.Exists(censusBase))
                {
                    census0 = new Dictionary<string, long[]>(StringComparer.Ordinal);
                    foreach (string line in File.ReadAllLines(censusBase, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length == 3) census0[p[0]] = new[] { long.Parse(p[1], CultureInfo.InvariantCulture), long.Parse(p[2], CultureInfo.InvariantCulture) };
                    }
                    PrintCensusDiff(census0, census1, 30);
                }
            }
            Console.WriteLine("ТАКТ\tстрок таблицы «Peak detection», добавленных НЕ с потока окон: {0}", foreignAdds);
            if (foreignStack != null) Console.WriteLine("ТАКТ\tстек чужого потока: " + foreignStack.Replace(Environment.NewLine, " | "));
            int verdict = Judge(samples, counted, "такт");
            rd.ResultDataStatus.Recording = false;
            CloseMainForm(mf);
            return verdict;
        }

        static void StartFakeRecording(DocEnergySpectrum d, ResultData rd)
        {
            // набор без прибора: контроллер прибора пуст, `MeasurementController.OnTimer`
            // ставит время спектру из ElapsedTime; уставка — сутки
            rd.ResultDataStatus.PresetTime = 86400 * 10;
            rd.PresetTime = 86400 * 10;
            rd.ResultDataStatus.Recording = true;
            d.AutoSave = false;
        }

        // ================================================================ docs

        static int RunDocs(string path, int cycles)
        {
            QuietMainForm mf = ShowMainForm();
            if (mf == null) return 5;
            UndoFixes(mf);
            MethodInfo onTimer = typeof(MainForm).GetMethod("OnTimer", Any, null, new[] { typeof(object), typeof(EventArgs) }, null);
            var docs = new List<WeakReference>();
            var views = new List<WeakReference>();
            Action cycle = () =>
            {
                DocEnergySpectrum d = OpenInMainForm(mf, path);
                if (d == null) { rigFail++; return; }
                docs.Add(new WeakReference(d));
                views.Add(new WeakReference(d.EnergySpectrumView));
                if (sabotage == "docref") SabotageKeep.Add(d);
                for (int i = 0; i < 30; i++)
                {
                    d.UpdateSpectrum = true;
                    onTimer.Invoke(mf, new object[] { null, EventArgs.Empty });
                    BlockAccessibility();
                    Application.DoEvents();
                }
                d.Dirty = false;
                if (Exp("nodrop")) d.AllowDrop = false;
                bool creatingBefore = CreatingHandle(d);
                try
                {
                    mf.CloseDocument(d.Filename, true);
                }
                catch (Exception ex)
                {
                    rigFail++;
                    Console.WriteLine("  ⛔ закрытие документа №{0} бросило {1}: {2}; до закрытия CreatingHandle={3}, после={4}, дескриптор {5}, в DocumentList {6}",
                                      docs.Count, ex.GetType().Name, ex.Message, creatingBefore, CreatingHandle(d), d.IsHandleCreated,
                                      DocumentManager.GetInstance().DocumentList.Contains(d));
                    Console.WriteLine("    стек: " + ex.StackTrace.Replace(Environment.NewLine, " | "));
                }
                Pump(50);
            };

            // прогрев — один цикл (первое открытие тянет кэши, базы, шрифты)
            cycle();
            var samples = new List<Sample>();
            samples.Add(Measure());
            Dictionary<string, int> windowsBefore = WindowClasses();
            Print("база", samples[0], samples[0], 0);
            for (int c = 1; c <= cycles; c++)
            {
                cycle();
                if (c % 3 == 0 || c == cycles)
                {
                    Sample s = Measure();
                    samples.Add(s);
                    Print("циклов " + c.ToString(CultureInfo.InvariantCulture), samples[0], s, c);
                }
            }
            if (settleS > 0)
            {
                // фоновые счёты FSA закрытых документов дойти до конца (они держат вид, а вид — документ)
                Console.WriteLine("SETUP\tпокой {0} с перед подсчётом живых", settleS);
                Pump(settleS * 1000);
            }
            Measure();
            PrintWindowDiff(windowsBefore, WindowClasses());
            int aliveDocs = 0, aliveViews = 0;
            var aliveAt = new List<string>();
            for (int i = 0; i < docs.Count; i++)
            {
                if (docs[i].IsAlive) { aliveDocs++; aliveAt.Add(i.ToString(CultureInfo.InvariantCulture)); }
                if (views[i].IsAlive) aliveViews++;
            }
            int listed = DocumentManager.GetInstance().DocumentList.Count;
            Console.WriteLine("ИТОГ\tоткрыто-закрыто {0}; живы после сборки: документов {1} [{2}], видов {3}; в DocumentList {4}; заслон доступности: {5}",
                              docs.Count, aliveDocs, string.Join(",", aliveAt), aliveViews, listed,
                              Exp("noacc") ? "отклонено WM_GETOBJECT " + AccBlocker.Blocked.ToString(CultureInfo.InvariantCulture) : "нет");
            for (int i = 0; i < docs.Count; i++)
            {
                var od = docs[i].Target as DocEnergySpectrum;
                if (od == null) continue;
                var ov = views[i].Target as Control;
                Console.WriteLine("ЖИВ\tдокумент №{0}: закрыт {1}, дескриптор {2}; вид: закрыт {3}, дескриптор {4}", i, od.IsDisposed, od.IsHandleCreated,
                                  ov != null ? ov.IsDisposed.ToString() : "—", ov != null ? ov.IsHandleCreated.ToString() : "—");
            }
            if (aliveDocs > 0)
            {
                Console.WriteLine("ИТОГ\tкто держит самый ранний живой документ: {0}", WhoHolds(mf, docs));
            }
            if (withCensus)
            {
                CensusReport(mf);
            }
            int verdict = Judge(samples, cycles, "цикл");
            bool docLeak = aliveDocs > 1;
            if (docLeak) Console.WriteLine("⛔ РОСТ: закрытые документы не собираются ({0} из {1})", aliveDocs, docs.Count);
            CloseMainForm(mf);
            if (sabotage == "docref")
            {
                if (!docLeak)
                {
                    Console.WriteLine("⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НЕ ПОЙМАН: удержанные документы не видны");
                    return 3;
                }
                Console.WriteLine("✅ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПОЙМАН: удержанные документы видны");
                return 0;
            }
            if (rigFail > 0) return 5;
            return docLeak ? 4 : verdict;
        }

        /// <summary>Флаг <c>Control.STATE_CREATINGHANDLE</c> (0x00040000) — окно посреди создания дескриптора.</summary>
        static bool CreatingHandle(Control c)
        {
            MethodInfo getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            return getState != null && (bool)getState.Invoke(c, new object[] { 0x00040000 });
        }

        static string WhoHolds(MainForm mf, List<WeakReference> docs)
        {
            object last = null;
            for (int i = 0; i < docs.Count && last == null; i++) last = docs[i].Target;
            if (last == null) return "—";
            string chain = PathTo(last, new KeyValuePair<string, object>("MainForm", mf));
            if (chain != null) return chain;
            var hits = new List<string>();
            foreach (object owner in new object[] { mf, FieldValue(mf, "dcFsaReportView"), FieldValue(mf, "dcControlPanel") })
            {
                ScanFields(owner, last, hits);
            }
            object list = FieldValue(mf, "dcResultViewList");
            if (list is System.Collections.IEnumerable items)
            {
                foreach (object rv in items) ScanFields(rv, last, hits);
            }
            return hits.Count == 0 ? "среди полей MainForm/панелей не найден" : string.Join("; ", hits);
        }

        static void ScanFields(object owner, object target, List<string> hits)
        {
            if (owner == null) return;
            for (Type t = owner.GetType(); t != null && t != typeof(Form) && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsValueType) continue;
                    if (ReferenceEquals(f.GetValue(owner), target)) hits.Add(owner.GetType().Name + "." + f.Name);
                }
            }
        }

        static object FieldValue(object owner, string name)
        {
            for (Type t = owner.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any);
                if (f != null) return f.GetValue(owner);
            }
            return null;
        }

        // ================================================================ MainForm

        static QuietMainForm ShowMainForm()
        {
            var mf = new QuietMainForm();
            mf.StartPosition = FormStartPosition.Manual;
            mf.ShowInTaskbar = false;
            mf.Opacity = 0.0;
            mf.Show();
            BlockAccessibility();
            Pump(300);
            mf.WindowState = FormWindowState.Normal;
            mf.Location = new Point(-6000, -4000);
            mf.Size = new Size(1400, 900);
            mf.Opacity = 1.0;
            Pump(300);
            // свой такт MainForm остановлен: такты подаёт проба, иначе счёт шёл бы ещё и по часам
            var timer = FieldValue(mf, "timer") as System.Windows.Forms.Timer;
            if (timer == null)
            {
                Console.WriteLine("⛔ обвязка: у MainForm нет таймера — Load не прошёл");
                return null;
            }
            timer.Stop();
            return mf;
        }

        static DocEnergySpectrum OpenInMainForm(MainForm mf, string path)
        {
            MethodInfo open = typeof(MainForm).GetMethod("OpenExistingDocument", Any, null, new[] { typeof(string) }, null);
            int before = DocumentManager.GetInstance().DocumentList.Count;
            open.Invoke(mf, new object[] { path });
            BlockAccessibility();
            Pump(200);
            List<DocEnergySpectrum> list = DocumentManager.GetInstance().DocumentList;
            if (list.Count != before + 1)
            {
                Console.WriteLine("⛔ обвязка: документ не открылся ({0} → {1})", before, list.Count);
                return null;
            }
            DocEnergySpectrum d = list[list.Count - 1];
            if (mf.ActiveDocument != d)
            {
                // активация вкладки без фокуса окна может не прийти — ставим как dockPanel
                MethodInfo changed = typeof(MainForm).GetMethod("dockPanel1_ActiveDocumentChanged", Any);
                d.Activate();
                Pump(50);
                if (mf.ActiveDocument != d && changed != null)
                {
                    mf.ActiveDocument = d;
                }
            }
            return d;
        }

        static void CloseMainForm(QuietMainForm mf)
        {
            // ⛔ НЕ Close(): FormClosing пишет раскладку и конфиг. Dispose гасит окно без записи.
            foreach (DocEnergySpectrum d in DocumentManager.GetInstance().DocumentList.ToArray())
            {
                d.Dirty = false;
                mf.CloseDocument(d.Filename, true);
            }
            mf.Hide();
            mf.Dispose();
        }

        static int CountDockContents(MainForm mf)
        {
            var panel = FieldValue(mf, "dockPanel1") as WeifenLuo.WinFormsUI.Docking.DockPanel;
            return panel != null ? panel.Contents.Count : -1;
        }

        // ================================================================ мерка

        static void Sabotage()
        {
            switch (sabotage)
            {
                case "gdi":
                    CreatePen(0, 1, 0);
                    break;
                case "user":
                    CreateMenu();
                    break;
                case "managed":
                    SabotageKeep.Add(new byte[4096]);
                    break;
                case "pen":
                    GC.KeepAlive(new Pen(Color.Red, 2f));
                    break;
            }
        }

        static Sample Measure()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            GC.Collect();
            Process p = Process.GetCurrentProcess();
            p.Refresh();
            IntPtr h = p.Handle;
            return new Sample
            {
                Gdi = GetGuiResources(h, 0),
                User = GetGuiResources(h, 1),
                Handles = p.HandleCount,
                Managed = GC.GetTotalMemory(true),
                Private = p.PrivateMemorySize64,
                Threads = p.Threads.Count,
                GuiThreads = CountGuiThreads(p)
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GuiThreadInfo
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public int left, top, right, bottom;
        }

        [DllImport("user32.dll", SetLastError = true)] static extern bool GetGUIThreadInfo(int idThread, ref GuiThreadInfo info);

        /// <summary>
        /// Потоки процесса, ставшие GUI-потоками (у них есть очередь win32k): каждому — объект
        /// USER и пара дескрипторов. П. 2 журнала: поток пула, позвавший `PostMessage`, остаётся
        /// таким до конца жизни; рост USER, равный росту этого числа, — полка числа потоков пула.
        /// </summary>
        static long CountGuiThreads(Process p)
        {
            long n = 0;
            foreach (ProcessThread t in p.Threads)
            {
                var info = new GuiThreadInfo { cbSize = Marshal.SizeOf(typeof(GuiThreadInfo)) };
                if (GetGUIThreadInfo(t.Id, ref info)) n++;
            }
            return n;
        }

        static void Print(string label, Sample b, Sample s, int n)
        {
            Console.WriteLine("ЗАМЕР\t{0,-12} GDI {1,5} ({2,5})  USER {3,5} ({4,5})  HND {5,5} ({6,5})  MAN {7,9} КБ ({8,8})  PRV {9,9} КБ ({10,8})  THR {11,3}  GUI-потоков {12,3}",
                              label,
                              s.Gdi, (s.Gdi - b.Gdi).ToString("+0;-0;0", CultureInfo.InvariantCulture),
                              s.User, (s.User - b.User).ToString("+0;-0;0", CultureInfo.InvariantCulture),
                              s.Handles, (s.Handles - b.Handles).ToString("+0;-0;0", CultureInfo.InvariantCulture),
                              s.Managed / 1024, ((s.Managed - b.Managed) / 1024).ToString("+0;-0;0", CultureInfo.InvariantCulture),
                              s.Private / 1024, ((s.Private - b.Private) / 1024).ToString("+0;-0;0", CultureInfo.InvariantCulture),
                              s.Threads, s.GuiThreads);
        }

        /// <summary>
        /// Рост — по разнице «последний замер − база» на единицу работы и по ТРЕНДУ: утечка растёт
        /// от замера к замеру, шум кэша — встаёт на полку. Пороги: GDI/USER — 3 объекта за прогон
        /// и не меньше 0.002 на единицу (один объект на 500 кадров — за сутки такта 100 мс это
        /// 1700 объектов); управляемая память — 100 Б на единицу; частная — 2 КБ на единицу.
        /// </summary>
        static int Judge(List<Sample> samples, int units, string unit)
        {
            Sample b = samples[0], e = samples[samples.Count - 1];
            int midIndex = samples.Count / 2;
            Sample mid = samples[midIndex];
            double u = Math.Max(1, units);
            // замеры идут через равные доли работы: вторая половина — её доля единиц
            double uHalf = Math.Max(1.0, u * (samples.Count - 1 - midIndex) / Math.Max(1, samples.Count - 1));
            var leaks = new List<string>();
            Func<string, long, long, long, double, bool> check = (name, d, dHalf, minTotal, perUnit) =>
            {
                double rate = d / u, rateHalf = dHalf / uHalf;
                // ⛔ судит ВТОРАЯ ПОЛОВИНА прогона: кэш, наполнившийся в начале, встаёт на полку
                // и там не растёт; утечка растёт и во второй половине тем же темпом
                bool grows = dHalf * 2 >= minTotal && rateHalf >= perUnit;
                Console.WriteLine("ИТОГ\t{0,-4} на {1}: весь прогон {2} ({3}), вторая половина {4} ({5}) — {6}", name, unit,
                                  rate.ToString("F4", CultureInfo.InvariantCulture), d.ToString(CultureInfo.InvariantCulture),
                                  rateHalf.ToString("F4", CultureInfo.InvariantCulture), dHalf.ToString(CultureInfo.InvariantCulture),
                                  grows ? "⛔ РОСТ" : "роста нет");
                if (grows) leaks.Add(name);
                return grows;
            };
            check("GDI", e.Gdi - b.Gdi, e.Gdi - mid.Gdi, 3, 0.002);
            // USER — за вычетом новых GUI-потоков (п. 2 журнала: по объекту на поток пула,
            // позвавший PostMessage, — полка числа потоков, а не утечка)
            long guiAll = e.GuiThreads - b.GuiThreads, guiHalf = e.GuiThreads - mid.GuiThreads;
            if (guiAll != 0 || guiHalf != 0)
            {
                Console.WriteLine("ИТОГ\tновых GUI-потоков: за прогон {0}, во второй половине {1} — USER судится за их вычетом",
                                  guiAll.ToString(CultureInfo.InvariantCulture), guiHalf.ToString(CultureInfo.InvariantCulture));
            }
            check("USER", e.User - b.User - Math.Max(0, guiAll), e.User - mid.User - Math.Max(0, guiHalf), 3, 0.002);
            check("HND", e.Handles - b.Handles - 2 * Math.Max(0, guiAll), e.Handles - mid.Handles - 2 * Math.Max(0, guiHalf), 20, 0.01);
            check("MAN", e.Managed - b.Managed, e.Managed - mid.Managed, 64 * 1024, 100.0);
            check("PRV", e.Private - b.Private, e.Private - mid.Private, 4 * 1024 * 1024, 2048.0);

            string expected = sabotage == "gdi" ? "GDI" : sabotage == "user" ? "USER" : sabotage == "managed" ? "MAN" : null;
            if (expected != null)
            {
                if (!leaks.Contains(expected))
                {
                    Console.WriteLine("⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НЕ ПОЙМАН: подброшенная утечка {0} не видна — проба не мерит", expected);
                    return 3;
                }
                Console.WriteLine("✅ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПОЙМАН: {0}", expected);
                return 0;
            }
            if (sabotage == "pen")
            {
                Console.WriteLine(leaks.Count == 0
                    ? "✅ КОНТРОЛЬ «перо без Dispose»: роста нет — объект GDI+ освобождает финализатор, это не утечка"
                    : "⚠ КОНТРОЛЬ «перо без Dispose»: рост " + string.Join(",", leaks.ToArray()));
            }
            if (rigFail > 0) return 5;
            Console.WriteLine("ИТОГ ПРОБЫ: {0}", leaks.Count == 0 ? "роста нет" : "⛔ рост " + string.Join(", ", leaks.ToArray()));
            return leaks.Count == 0 ? 0 : 4;
        }

        // ================================================================ обвязка

        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\tпиков найдено {0}", rd.DetectedPeaks != null ? rd.DetectedPeaks.Count : 0);
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var d = new DocEnergySpectrum(path);
            d.ResultDataFile = file;
            d.ActiveResultDataIndex = 0;
            return d;
        }

        static void WaitIdle(FsaAnalysisSession session, int timeoutS)
        {
            for (int i = 0; i < timeoutS * 20; i++)
            {
                if (!session.IsRunning)
                {
                    Application.DoEvents();
                    return;
                }
                Thread.Sleep(50);
                Application.DoEvents();
            }
            Console.WriteLine("  ⛔ сеанс не дошёл до покоя за {0} с", timeoutS);
            rigFail++;
        }

        static void Pump(int ms)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Any);
                if (field != null) return field;
            }
            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }
    }
}
