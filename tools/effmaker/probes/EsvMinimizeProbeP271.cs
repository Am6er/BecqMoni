using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

/// <summary>
/// (П271, 14.10.2026) СВОРАЧИВАНИЕ ГЛАВНОГО ОКНА В РЕЖИМЕ «FIT» — приёмка находки ревизии П270
/// (`EnergySpectrumView`, правка П259 «вписать держится при смене размера окна»).
///
/// При сворачивании главного окна DockPanelSuite (`DockingMdi`) отдаёт документу размер 0×0
/// (замер П270, `MinimizeProbe`). Подгонка во всю ширину в поле отрицательной ширины давала
/// отрицательный масштаб, `hScrollBar1.Maximum` < 0, и полоса с `RecalcScrollBar` перебрасывали
/// `Value` друг другу без конца — `StackOverflowException` (0xC00000FD), процесс падал без окна.
///
/// Падение убивает процесс, поэтому каждая сцена идёт ОТДЕЛЬНЫМ процессом: проба без ключей —
/// распорядитель, запускает себя с `--scene=&lt;имя&gt;` и сводит коды возврата в таблицу.
///
/// Сцены (спектр — 1024 канала по 3 кэВ, вертикаль MinMax, как у приложения):
///   fit=W        «Fit» вписан в 1000×600 → ширина W (0 — 0×0, как свёрнутое окно; 62 — поле ровно
///                нулевой ширины, 60 — минус два пикселя) → 800×600 →
///                1000×600; после каждого возврата масштаб обязан совпасть с подгонкой
///                ОТДЕЛЬНОГО вида того же размера (подгонка восстановилась, а не застряла).
///   equal=W      контроль П270: режим «Equal» при ширине W — не падал и до правки.
///   deferred     документ открыт при свёрнутом окне: вид 0×0, подгонка заказана
///                (`FitHorizontalScale`) — затем 1000×600: масштаб = подгонке 1000.
///   default      то же через `SetDefaultHorizontalScale` (умолчание «Fit»), как зовёт MainForm.
///   zoomsel=W    увеличение выделения (клавиша M) при ширине W — та же беда отрицательного масштаба.
///   normal       обычные размеры: масштабы и состояние полосы — печатаются для сверки ДО/ПОСЛЕ
///                (правка не должна менять ничего при обычных размерах).
///   dock         настоящий `DocEnergySpectrum` в `DockPanel` (`DockingMdi`, тема VS2015, как у
///                MainForm): окно свернуть → развернуть, дважды; процесс жив, масштаб вида и поле
///                масштаба документа — прежние.
///
/// Коды: 0 — все сцены живы и сошлись; 1 — падение или расхождение; 2 — ключи.
/// </summary>
static class EsvMinimizeProbeP271
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const int StackOverflow = unchecked((int)0xC00000FD);

    static readonly string[] Scenes =
    {
        "fit=0", "fit=1", "fit=2", "fit=3", "fit=60", "fit=62",
        "equal=0", "equal=60", "equal=120",
        "deferred", "default", "zoomsel=30", "normal", "dock"
    };

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string scene = null;
        var only = new List<string>();
        foreach (string a in args)
        {
            if (a.StartsWith("--scene=", StringComparison.Ordinal)) scene = a.Substring(8);
            else if (a.StartsWith("--only=", StringComparison.Ordinal)) only.AddRange(a.Substring(7).Split(','));
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (scene != null)
        {
            // исключение на потоке окон — наружу кодом, а не окном «Unhandled exception»
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            return RunScene(scene);
        }

        return Drive(only.Count > 0 ? only.ToArray() : Scenes);
    }

    // ==================================================================
    // распорядитель: каждая сцена — свой процесс
    // ==================================================================

    static int Drive(string[] scenes)
    {
        string self = Assembly.GetExecutingAssembly().Location;
        int bad = 0;
        var summary = new List<string>();
        foreach (string s in scenes)
        {
            Console.WriteLine();
            Console.WriteLine("=== СЦЕНА " + s + " ===");
            var psi = new ProcessStartInfo(self, "--scene=" + s)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(self)
            };
            string verdict;
            using (Process p = Process.Start(psi))
            {
                var err = new StringBuilder();
                p.ErrorDataReceived += (o, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                string output = p.StandardOutput.ReadToEnd();
                bool exited = p.WaitForExit(120000);
                if (!exited)
                {
                    try { p.Kill(); } catch (Exception) { }
                }
                p.WaitForExit();
                foreach (string line in output.Split('\n'))
                {
                    string t = line.TrimEnd('\r');
                    if (t.Length > 0 && !t.StartsWith("BecqMoni:", StringComparison.Ordinal)) Console.WriteLine("  " + t);
                }
                string errText;
                lock (err) errText = err.ToString();
                foreach (string line in errText.Split('\n'))
                {
                    string t = line.TrimEnd('\r');
                    if (t.Length > 0 && !t.StartsWith("BecqMoni:", StringComparison.Ordinal)) Console.WriteLine("  [stderr] " + t);
                }
                int code = exited ? p.ExitCode : -1;
                bool ok = exited && code == 0 && output.Contains("СЦЕНА: ok");
                if (!exited) verdict = "ВИСИТ (снята через 120 с)";
                else if (code == StackOverflow) verdict = "ПАДЕНИЕ 0xC00000FD (StackOverflowException)";
                else if (code != 0) verdict = "ПАДЕНИЕ, код 0x" + code.ToString("X8");
                else verdict = ok ? "жив, сошлось" : "жив, РАСХОЖДЕНИЕ";
                Console.WriteLine("  → " + verdict);
                if (!ok) bad++;
                summary.Add(string.Format("{0,-11} {1}", s, verdict));
            }
        }

        Console.WriteLine();
        Console.WriteLine("СВОДКА:");
        foreach (string line in summary) Console.WriteLine("  " + line);
        Console.WriteLine(bad == 0 ? "ИТОГ: ВСЁ СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad);
        return bad == 0 ? 0 : 1;
    }

    // ==================================================================
    // одна сцена
    // ==================================================================

    static int RunScene(string scene)
    {
        GlobalConfigManager.GetInstance();
        bool ok;
        string name = scene;
        int arg = -1;
        int eq = scene.IndexOf('=');
        if (eq > 0)
        {
            name = scene.Substring(0, eq);
            arg = int.Parse(scene.Substring(eq + 1), CultureInfo.InvariantCulture);
        }

        switch (name)
        {
            case "fit": ok = FitScene(arg); break;
            case "equal": ok = EqualScene(arg); break;
            case "deferred": ok = DeferredScene(false); break;
            case "default": ok = DeferredScene(true); break;
            case "zoomsel": ok = ZoomSelectionScene(arg); break;
            case "normal": ok = NormalScene(); break;
            case "dock": ok = DockScene(); break;
            default: Console.Error.WriteLine("нет сцены " + scene); return 2;
        }

        Console.WriteLine(ok ? "СЦЕНА: ok" : "СЦЕНА: НЕТ");
        Console.Out.Flush();
        return 0;
    }

    static ResultData Spectrum()
    {
        const int n = 1024;
        var cal = new PolynomialEnergyCalibration();
        cal.PolynomialOrder = 1;
        cal.Coefficients = new double[] { 0.0, 3.0 };
        var s = new EnergySpectrum(1, n);
        s.EnergyCalibration = cal;
        for (int i = 0; i < n; i++) s.Spectrum[i] = 100 + (i % 7) + (i > 200 && i < 220 ? 500 : 0);
        s.MeasurementTime = 100.0;
        s.LiveTime = 100.0;
        long total = 0;
        foreach (int c in s.Spectrum) total += c;
        s.TotalPulseCount = total;
        s.ValidPulseCount = total;
        return new ResultData { EnergySpectrum = s };
    }

    static EnergySpectrumView View(int width, int height, HorizontalMagnification magnification)
    {
        var v = new EnergySpectrumView();
        v.Size = new Size(width, height);
        v.ResultDataList = new List<ResultData> { Spectrum() };
        v.ActiveResultDataIndex = 0;
        v.HorizontalMagnification = magnification;
        // как `MainForm.RefreshDocumentChart` после смены данных: пикселей на кэВ — из калибровки
        // спектра (без этого подгонка вида, ещё не менявшего размер, считает умолчанием 0.4);
        // калибровку вид берёт в `PrepareViewData`, поэтому она — первой
        v.PrepareViewData();
        v.RecalcChartParameters();
        return v;
    }

    /// <summary>Масштаб подгонки ОТДЕЛЬНОГО вида данного размера — эталон.</summary>
    static double ReferenceFit(int width, int height)
    {
        EnergySpectrumView r = View(width, height, HorizontalMagnification.Fit);
        r.FitHorizontalScale();
        double scale = Scale(r);
        r.Dispose();
        return scale;
    }

    static void Resize(EnergySpectrumView v, int width, int height, string what)
    {
        Console.WriteLine("  {0}: ставлю {1}×{2}", what, width, height);
        Console.Out.Flush();
        v.Size = new Size(width, height);
        Console.WriteLine("    {0}", State(v));
        Console.Out.Flush();
    }

    static bool Same(string what, double actual, double expected)
    {
        bool same = actual == expected;
        Console.WriteLine("    {0}: {1} (эталон {2})  {3}", what, R(actual), R(expected), same ? "ok" : "НЕТ");
        return same;
    }

    static bool Sane(EnergySpectrumView v)
    {
        HScrollBar sb = Bar(v);
        bool sane = Scale(v) > 0.0 && sb.Minimum == 0 && sb.Value >= sb.Minimum && sb.Value <= sb.Maximum;
        Console.WriteLine("    масштаб положителен и полоса в порядке (Min 0 ≤ Value ≤ Max): {0}", sane ? "ok" : "НЕТ");
        return sane;
    }

    static bool FitScene(int width)
    {
        EnergySpectrumView v = View(1000, 600, HorizontalMagnification.Fit);
        v.FitHorizontalScale();
        double fit1000 = ReferenceFit(1000, 600), fit800 = ReferenceFit(800, 600);
        Console.WriteLine("  Fit в 1000×600: {0}", State(v));
        Resize(v, width, width == 0 ? 0 : 600, "сжать (0 — как свёрнутое окно)");
        bool ok = Sane(v);
        Resize(v, 800, 600, "развернуть в другой размер");
        ok &= Same("масштаб после 800×600", Scale(v), fit800);
        Resize(v, width, width == 0 ? 0 : 600, "сжать снова");
        Resize(v, 1000, 600, "развернуть в прежний размер");
        ok &= Same("масштаб после 1000×600", Scale(v), fit1000);
        ok &= Sane(v);
        return ok;
    }

    static bool EqualScene(int width)
    {
        // контроль П270: подогнали, перешли в «Equal» — смена размера масштаб не трогает
        EnergySpectrumView v = View(1000, 600, HorizontalMagnification.Fit);
        v.FitHorizontalScale();
        v.HorizontalMagnification = HorizontalMagnification.Equal;
        double before = Scale(v);
        Console.WriteLine("  Fit в 1000×600, затем Equal: {0}", State(v));
        Resize(v, width, width == 0 ? 0 : 600, "сжать");
        Resize(v, 1000, 600, "развернуть");
        return Same("масштаб Equal не тронут", Scale(v), before);
    }

    static bool DeferredScene(bool viaDefault)
    {
        // документ открыт при свёрнутом окне: поля нет с самого начала
        EnergySpectrumView v = View(0, 0, HorizontalMagnification.Fit);
        double fit1000 = ReferenceFit(1000, 600);
        Console.WriteLine("  вид 0×0, подгонка заказана ({0})", viaDefault ? "SetDefaultHorizontalScale, умолчание Fit" : "FitHorizontalScale");
        Console.Out.Flush();
        if (viaDefault)
        {
            GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.DefaultHorizontalMagnification = HorizontalMagnification.Fit;
            v.SetDefaultHorizontalScale();
        }
        else
        {
            v.FitHorizontalScale();
        }
        Console.WriteLine("    {0}", State(v));
        bool ok = Sane(v);
        Resize(v, 1000, 600, "развернуть");
        ok &= Same("масштаб после 1000×600", Scale(v), fit1000);
        return ok;
    }

    static bool ZoomSelectionScene(int width)
    {
        EnergySpectrumView v = View(1000, 600, HorizontalMagnification.Fit);
        v.FitHorizontalScale();
        v.SelectionStart = 100;
        v.SelectionEnd = 200;
        Resize(v, width, 600, "сжать");
        double before = Scale(v);
        Console.WriteLine("  увеличить выделение 100…200 (клавиша M) при ширине {0}", width);
        Console.Out.Flush();
        v.ZoominSelectedRegion();
        Console.WriteLine("    {0}", State(v));
        bool ok = Sane(v);
        ok &= Same("масштаб не тронут (поля нет)", Scale(v), before);
        return ok;
    }

    static bool NormalScene()
    {
        // обычные размеры: что печатается, сверяется ДО/ПОСЛЕ правки построчно
        EnergySpectrumView v = View(1000, 600, HorizontalMagnification.Fit);
        v.FitHorizontalScale();
        Console.WriteLine("  Fit 1000×600: {0}", State(v));
        foreach (Size s in new[] { new Size(700, 600), new Size(1200, 500), new Size(1000, 600), new Size(140, 600), new Size(63, 600), new Size(1000, 600) })
        {
            Resize(v, s.Width, s.Height, "размер");
        }
        v.zoom(3.0M);
        Console.WriteLine("  zoom 3.0: {0}", State(v));
        Resize(v, 800, 600, "размер при увеличении человека");
        v.SetScale11();
        Console.WriteLine("  1:1: {0}", State(v));
        v.SelectionStart = 300;
        v.SelectionEnd = 400;
        v.ZoominSelectedRegion();
        Console.WriteLine("  выделение 300…400: {0}", State(v));
        v.HorizontalMagnification = HorizontalMagnification.Equal;
        GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.DefaultHorizontalMagnification = HorizontalMagnification.Fit;
        v.SetDefaultHorizontalScale();
        Console.WriteLine("  умолчание Fit: {0}", State(v));
        v.RecalcScrollBar();
        Console.WriteLine("  RecalcScrollBar: {0}", State(v));
        return true;
    }

    // ==================================================================
    // настоящий документ в DockPanel: свернуть → развернуть
    // ==================================================================

    static bool DockScene()
    {
        Application.EnableVisualStyles();
        var main = new Form
        {
            Size = new Size(1100, 760),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(40, 40),
            IsMdiContainer = true,
            ShowInTaskbar = false,
            Text = "П271 dock"
        };
        var dock = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingMdi };
        ThemeBase theme = Vs2015Theme();
        if (theme != null) dock.Theme = theme;
        Console.WriteLine("  тема дока: {0}", theme != null ? theme.GetType().Name : "по умолчанию");
        main.Controls.Add(dock);

        var doc = new DocEnergySpectrum();
        var file = new ResultDataFile();
        file.InitFormatVersion();
        file.ResultDataList = new List<ResultData> { Spectrum() };
        doc.ResultDataFile = file;
        doc.ActiveResultDataIndex = 0;
        doc.EnergySpectrumView.HorizontalMagnification = HorizontalMagnification.Fit;
        doc.EnergySpectrumView.VerticalFittingMode = VerticalFittingMode.MinMax;
        doc.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;

        bool ok = true;
        double shownScale = double.NaN;
        decimal shownField = -1;
        int step = 0;
        var log = new List<string>();
        EnergySpectrumView view = doc.EnergySpectrumView;
        view.SizeChanged += (o, e) =>
        {
            log.Add(string.Format(CultureInfo.InvariantCulture, "    вид {0}×{1}, окно {2}: {3}", view.Width, view.Height, main.WindowState, State(view)));
        };

        var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (o, e) =>
        {
            step++;
            foreach (string line in log) Console.WriteLine(line);
            log.Clear();
            switch (step)
            {
                case 1:
                    shownScale = Scale(view);
                    shownField = ScaleField(doc);
                    Console.WriteLine("  показан: вид {0}×{1}, масштаб {2}, поле масштаба {3}", view.Width, view.Height, R(shownScale), shownField.ToString(CultureInfo.InvariantCulture));
                    Console.WriteLine("  свернуть");
                    Console.Out.Flush();
                    main.WindowState = FormWindowState.Minimized;
                    break;
                case 3:
                    Console.WriteLine("  развернуть");
                    Console.Out.Flush();
                    main.WindowState = FormWindowState.Normal;
                    break;
                case 5:
                    ok &= Same("масштаб вида после развёртывания", Scale(view), shownScale);
                    bool field = ScaleField(doc) == shownField;
                    Console.WriteLine("    поле масштаба документа: {0} (было {1})  {2}", ScaleField(doc).ToString(CultureInfo.InvariantCulture), shownField.ToString(CultureInfo.InvariantCulture), field ? "ok" : "НЕТ");
                    ok &= field;
                    Console.WriteLine("  свернуть ещё раз, затем развернуть в другой размер (900×700)");
                    Console.Out.Flush();
                    main.WindowState = FormWindowState.Minimized;
                    break;
                case 7:
                    main.WindowState = FormWindowState.Normal;
                    main.Size = new Size(900, 700);
                    break;
                case 9:
                    double reference = ReferenceFit(view.Width, view.Height);
                    ok &= Same("масштаб после 900×700 = подгонке отдельного вида того же размера", Scale(view), reference);
                    ok &= Sane(view);
                    timer.Stop();
                    main.Close();
                    break;
            }
        };
        main.Shown += (o, e) =>
        {
            doc.Show(dock, DockState.Document);
            doc.UpdateEnergySpectrum();
            view.RecalcChartParameters();   // как `MainForm.RefreshDocumentChart` (`UpdateAllView`)
            doc.SetDefaultHorizontalScale();
            view.FitHorizontalScale();
            timer.Start();
        };
        Application.Run(main);
        bool finished = step >= 9;
        Console.WriteLine("  прогон окна дошёл до конца: {0}", finished ? "ok" : "НЕТ");
        return ok && finished;
    }

    static ThemeBase Vs2015Theme()
    {
        try
        {
            string dll = Path.Combine(Path.GetDirectoryName(typeof(DockPanel).Assembly.Location), "WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll");
            if (!File.Exists(dll)) return null;
            Type t = Assembly.LoadFrom(dll).GetTypes().FirstOrDefault(x => x.Name == "VS2015BlueTheme");
            return t != null ? (ThemeBase)Activator.CreateInstance(t) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  тема VS2015 не загрузилась: " + ex.GetType().Name);
            return null;
        }
    }

    static decimal ScaleField(DocEnergySpectrum doc)
    {
        object box = typeof(DocEnergySpectrum).GetField("toolStripNumUpDownScale", Any).GetValue(doc);
        object control = box.GetType().GetProperty("NumericUpDownControl").GetValue(box, null);
        return ((NumericUpDown)control).Value;
    }

    // ---- чтение вида ----

    static double Scale(EnergySpectrumView v)
    {
        return (double)typeof(EnergySpectrumView).GetField("horizontalScale", Any).GetValue(v);
    }

    static HScrollBar Bar(EnergySpectrumView v)
    {
        return (HScrollBar)typeof(EnergySpectrumView).GetField("hScrollBar1", Any).GetValue(v);
    }

    static string State(EnergySpectrumView v)
    {
        HScrollBar sb = Bar(v);
        object last = typeof(EnergySpectrumView).GetField("lastFitHorizontalScale", Any).GetValue(v);
        return string.Format(CultureInfo.InvariantCulture, "масштаб {0}, последняя подгонка {1}; полоса Min {2} Max {3} Value {4} Large {5} {6}",
                             R(Scale(v)), R((double)last), sb.Minimum, sb.Maximum, sb.Value, sb.LargeChange, sb.Enabled ? "вкл" : "выкл");
    }

    static string R(double x)
    {
        return x.ToString("R", CultureInfo.InvariantCulture);
    }
}
