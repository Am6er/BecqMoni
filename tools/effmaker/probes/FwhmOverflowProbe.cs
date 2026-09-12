// Полоса П25, строка `AMBER23` (задача Amber 12.09.2026, решение вопросником
// «Только падение»): `System.OverflowException` из `Graphics.DrawLine(Pen, int×4)`
// в `EnergySpectrumView.DrawFWHM` ← `DrawChart` ← `OnPaint`.
//
//     FwhmOverflowProbe [--expect-overflow] [--out=<каталог артефактов>]
//                       [--ref=<каталог артефактов ДО-плеча: scene2_before.png, scene3_before.png>]
//                       [--peak=<отсчётов/канал на вершине, умолчание 4000000>]
//                       [--width=800] [--height=600]
//                       [--sweep=<список M через запятую>]
//
// ЧТО МЕРИТСЯ И ПОЧЕМУ ИМЕННО ТАК.
//
// Падает ОТРИСОВКА: GDI+ отказывает (`ValueOverflow` → `OverflowException`),
// когда координата `DrawLine(int…)` по модулю выходит за ≈2³⁰. Такую
// координату даёт жёлтая ломаная подложки полуширины в `DrawFWHM`: она
// проецировала ВСЕ каналы выделения без отсечения по видимости, а в ЛИНЕЙНОЙ
// шкале при автоподгонке по видимому окну (`VerticalFittingMode.MinMax`) на
// пустом окне (нули хвоста при увеличении) `verticalScale` ≈ M (максимум
// спектра, отсчётов/канал), и y уходит на ~height·M пикселей.
//
// Проба зовёт НАСТОЯЩИЙ `OnPaint` вида (отражением, как `SketchShot` и
// `GapProbeAmber1`) на `Bitmap width×height` — то есть трасса падения та же,
// что у Amber: `DrawFWHM` ← `DrawChart` ← `OnPaint`. Данные — синтетический
// спектр: гауссов пик M отсчётов/канал на низком континууме и нули справа;
// выделение — по пику; вид — линейная шкала, подгонка `MinMax`, увеличение по
// горизонтали и прокрутка в нули. Порядок вызовов повторяет окно: свойства →
// `PrepareViewData` → `HorizontalScale` → `hScrollBar1.Value` (тот же
// обработчик `ValueChanged`, что у человека за прокруткой) → `OnPaint`.
//
// ⛔ ОКНО `BecqMoni` НЕ ПОДНИМАЕТСЯ. `EnergySpectrumView` — `UserControl`, он
//    создаётся обычным конструктором и никуда не показывается; хэндла окна у
//    него нет, `Invalidate` — пустой вызов.
//
// ⚠ ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ, без которых проба не меряет ничего:
//    1. §1 — сам механизм: `DrawLine` с координатой 2³⁰+2¹⁶ на том же холсте
//       ОБЯЗАН бросить `OverflowException` (иначе среда не та, что у Amber, и
//       «не упало» ничего не значит); а координата на пределе клипа
//       приложения (`EnergySpectrumView.GdiCoordinateLimit`, читается из
//       сборки отражением, а не переписан сюда) обязана НЕ бросать — иначе
//       ремень не ремень;
//    2. §2 с ключом `--expect-overflow` — плечо ДО: сцена ОБЯЗАНА дать
//       `OverflowException`, и в трассе обязаны стоять `DrawFWHM`, `DrawChart`
//       и `OnPaint`; без ключа (плечо ПОСЛЕ) — обязана НЕ дать;
//    3. §4 — сцена 2 (весь спектр на экране) и сцена 3 (увеличение ×8, окно
//       на пике, выделение видно ЧАСТИЧНО — отсечение по видимости не смеет
//       съесть видимую часть ломаной): жёлтых точек в кадре > 0 (ломаная
//       ДЕЙСТВИТЕЛЬНО нарисована, а «не упало» — не потому, что рисовать было
//       нечего); с `--ref=` оба кадра сравниваются с кадрами ДО-плеча
//       попиксельно — правка не меняет того, что видно.
//
// ⚠ Библиотека нуклидов. Конструктор вида сам зовёт
//   `NuclideDefinitionManager.GetInstance()` (в `try`, при отказе — пустая
//   библиотека); из каталога проб рядом лежит поставочная, и она поднялась бы.
//   Чтобы кадр не зависел от её содержимого (`AMBER19`: менеджер не
//   поднимать), проба ПОСЛЕ конструктора подменяет поле `nuclideManager` пустой
//   библиотекой; сама она `GetInstance()` не зовёт — подъём в конструкторе
//   есть дело приложения, а не пробы, и на кадр он не влияет.
//
// Ожидание: «РАСХОЖДЕНИЙ НЕТ», код 0. Код 3 — проба не смогла построить сцену
// (не то, что мерится); код 1 — расхождение с ожиданием.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`).

static class FwhmOverflowProbe
{
    const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
                             | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly Type tView = typeof(EnergySpectrumView);
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    // Сцена: 4096 каналов, E = канал (кэВ), пик в канале 1000, σ = 12 каналов,
    // континуум 200 отсчётов в каналах 900…2000, нули ниже 900 (как под порогом
    // регистрации) и выше 2000. Окно «на нулях» — левый край при увеличении ×8
    // (каналы 0…92): выделение лежит ПРАВЕЕ окна, x > 0, и ломаная рисуется
    // (при выделении левее окна `DrawFWHM` её не рисует вовсе — `num3 > 0`).
    const int Channels = 4096;
    const int PeakChannel = 1000;
    const double PeakSigma = 12.0;
    const int SelectFrom = 950;
    const int SelectTo = 1050;
    const double MeasurementTime = 1000.0;
    // Окно сцены 3 (Partial): прокрутка ×8 так, чтобы левый край окна был в
    // канале ≈957, правый ≈1049 — выделение 950…1050 видно не целиком.
    const int PartialScrollPx = 957 * 8;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        bool expectOverflow = false;
        string outDir = null, refDir = null;
        double peak = 4000000.0;
        int width = 800, height = 600;
        double[] sweep = new double[] { 250000.0, 500000.0, 1000000.0, 2000000.0, 4000000.0, 8000000.0 };

        foreach (string a in args)
        {
            if (a == "--expect-overflow") expectOverflow = true;
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
            else if (a.StartsWith("--ref=", StringComparison.Ordinal)) refDir = a.Substring(6);
            else if (a.StartsWith("--peak=", StringComparison.Ordinal))
                peak = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--width=", StringComparison.Ordinal))
                width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--height=", StringComparison.Ordinal))
                height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--sweep=", StringComparison.Ordinal))
            {
                string[] parts = a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                sweep = new double[parts.Length];
                for (int i = 0; i < parts.Length; i++) sweep[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            }
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        if (outDir != null) Directory.CreateDirectory(outDir);

        Say("== полоса П25, `AMBER23`: OverflowException в DrawFWHM ==");
        Say("плечо: " + (expectOverflow ? "ДО правки (ожидаю OverflowException)" : "ПОСЛЕ правки (ожидаю тишину)"));
        Say("");

        try
        {
            Header(width, height);
            Section1_GdiLimit(width, height);
            Section2_Scene1(peak, width, height, expectOverflow);
            Section3_Sweep(sweep, width, height);
            Section4_Frames(peak, width, height, outDir, refDir, expectOverflow);
        }
        catch (ProbeSetupException ex)
        {
            Say("⛔ СЦЕНА НЕ ПОСТРОЕНА (это не то, что мерится): " + ex.Message);
            Finish(outDir, expectOverflow);
            return 3;
        }

        Say("");
        Say(failures == 0 ? "РАСХОЖДЕНИЙ НЕТ" : "РАСХОЖДЕНИЙ: " + failures);
        Finish(outDir, expectOverflow);
        return failures == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  0. Чем мерено
    // ══════════════════════════════════════════════════════════════════════

    static void Header(int width, int height)
    {
        Assembly app = tView.Assembly;
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                            .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            Say("sha256 сборки:     " + Sha256(app.Location));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }
        Say("рабочий каталог:   " + Directory.GetCurrentDirectory());
        Say(ProbeTargetFramework.Describe());
        Say("холст:             " + width.ToString(CultureInfo.InvariantCulture) + "×"
            + height.ToString(CultureInfo.InvariantCulture));

        FieldInfo fLimit = tView.GetField("GdiCoordinateLimit", Any);
        if (fLimit == null)
        {
            Say("клип координат:    в сборке НЕТ (`EnergySpectrumView.GdiCoordinateLimit` не найдено — сборка ДО правки)");
        }
        else
        {
            object v = fLimit.IsLiteral ? fLimit.GetRawConstantValue() : fLimit.GetValue(null);
            Say("клип координат:    GdiCoordinateLimit = " + Convert.ToInt64(v, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture));
        }
        Say("");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  1. Механизм: предел GDI+ на том же холсте
    // ══════════════════════════════════════════════════════════════════════

    static void Section1_GdiLimit(int width, int height)
    {
        Say("§1 механизм — DrawLine(int…) на Bitmap " + width.ToString(CultureInfo.InvariantCulture)
            + "×" + height.ToString(CultureInfo.InvariantCulture));
        int beyond = (1 << 30) + (1 << 16);
        string got = TryLine(width, height, 10, 10, 20, -beyond);
        Say("  y = −(2³⁰+2¹⁶): " + (got ?? "тишина"));
        Check("положительный контроль механизма: за пределом GDI+ бросает OverflowException",
              got != null && got.StartsWith("OverflowException", StringComparison.Ordinal));

        FieldInfo fLimit = tView.GetField("GdiCoordinateLimit", Any);
        if (fLimit != null)
        {
            int limit = Convert.ToInt32(fLimit.IsLiteral ? fLimit.GetRawConstantValue() : fLimit.GetValue(null),
                                        CultureInfo.InvariantCulture);
            string a = TryLine(width, height, -limit, -limit, limit, limit);
            string b = TryLine(width, height, limit, -limit, -limit, limit);
            string c = TryLine(width, height, 10, 10, 20, -limit);
            Say("  отрезки на пределе клипа ±" + limit.ToString(CultureInfo.InvariantCulture)
                + ": диагональ " + (a ?? "тишина") + "; антидиагональ " + (b ?? "тишина")
                + "; одна координата " + (c ?? "тишина"));
            Check("предел клипа приложения GDI+ принимает (обе диагонали и одна координата)",
                  a == null && b == null && c == null);

            // Клип обязан отдавать число в пределе и при NaN/бесконечности:
            // `(int)NaN` даёт int.MinValue, и это ровно та координата, которую
            // GDI+ не примет.
            MethodInfo mClip = tView.GetMethod("GdiCoordinate", BindingFlags.Static | BindingFlags.NonPublic);
            if (mClip == null)
            {
                Check("в сборке есть EnergySpectrumView.GdiCoordinate(double)", false);
            }
            else
            {
                double[] probes = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300, -1e300, 12.7, -12.7, 0.0 };
                var sb = new StringBuilder();
                bool ok = true;
                foreach (double p in probes)
                {
                    int r = (int)mClip.Invoke(null, new object[] { p });
                    sb.Append(p.ToString("R", CultureInfo.InvariantCulture)).Append("→")
                      .Append(r.ToString(CultureInfo.InvariantCulture)).Append(' ');
                    if (Math.Abs((long)r) > limit) ok = false;
                    if (!double.IsNaN(p) && !double.IsInfinity(p) && Math.Abs(p) < limit && r != (int)p) ok = false;
                }
                Say("  GdiCoordinate: " + sb.ToString().TrimEnd());
                Check("GdiCoordinate держит NaN/±∞/±1e300 в пределе и не трогает обычные числа", ok);
            }
        }
        Say("");
    }

    static string TryLine(int width, int height, int x1, int y1, int x2, int y2)
    {
        using (var bmp = new Bitmap(width, height))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SetClip(new Rectangle(41, 0, width - 41, height - 33));
            try
            {
                g.DrawLine(Pens.Yellow, x1, y1, x2, y2);
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  2. Сцена 1 — пик M, выделение по нему, окно на нулях, полный OnPaint
    // ══════════════════════════════════════════════════════════════════════

    sealed class PaintOutcome
    {
        public Exception Error;
        public bool TraceHasDrawFwhm, TraceHasDrawChart, TraceHasOnPaint;
        public double VerticalScale, ScrollBaseY, MaxValue, MinValue;
        public int ScrollY, ScrollX, Height, Width, MinChannel, MaxChannel;
        public bool VScrollEnabled;
        public int VScrollMax, VScrollValue;
        public int YellowPixels;
        public double FwhmChannels;
        public Bitmap Frame;
    }

    enum Window
    {
        ZerosFit,   // окно на нулях, вертикальная прокрутка как поставила подгонка
        ZerosTop,   // окно на нулях, вертикальная прокрутка — в самый верх (человек прокрутил)
        Whole,      // весь спектр на экране (FitHorizontalScale)
        Partial     // увеличение ×8, окно на пике: выделение видно ЧАСТИЧНО (каналы ≈957…1049 из 950…1050)
    }

    static PaintOutcome PaintScene(double peak, int width, int height, Window window, bool keepFrame)
    {
        var outcome = new PaintOutcome();
        ResultData rd = MakeResult(peak);
        var list = new List<ResultData> { rd };

        using (var view = new EnergySpectrumView())
        {
            // Библиотека — пустая, кадр от поставочной не зависит (см. шапку).
            Set(view, "nuclideManager", new NuclideDefinitionManager { NuclideDefinitionFile = new NuclideDefinitionFile() });

            // Размер — ДО данных: SizeChanged при пустом списке ничего не делает.
            view.Size = new Size(width, height);
            view.BackgroundMode = BackgroundMode.Invisible;
            view.HorizontalUnit = HorizontalUnit.Energy;
            view.VerticalUnit = VerticalUnit.Counts;
            view.VerticalScaleType = VerticalScaleType.LinearScale;
            view.VerticalFittingMode = VerticalFittingMode.MinMax;
            view.ChartType = ChartType.LineChart;
            view.PeakMode = PeakMode.Visible;
            view.SmoothingMethod = SmoothingMethod.None;

            view.ResultDataList = list;
            view.ActiveResultDataIndex = 0;
            view.PrepareViewData();
            // Пиксели на кэВ и сдвиг шкалы — как их ставит окно (`MainForm`
            // зовёт после загрузки документа; без этого поле остаётся с
            // умолчанием 0.4 и окно подгонки считается не по тем каналам).
            view.RecalcChartParameters();

            if (window != Window.Whole)
            {
                // Увеличение ×8: на экране каналы 0…92 — одни нули. Прокрутка —
                // тем же путём, что у человека: значение полосы → ValueChanged →
                // RefreshViewportData (подгонка ставит и вертикальную прокрутку).
                view.HorizontalScale = 8.0;
                var hbar = (HScrollBar)tView.GetField("hScrollBar1", Priv).GetValue(view);
                hbar.Value = 1;
                hbar.Value = window == Window.Partial ? PartialScrollPx : 0;
                if (window == Window.ZerosTop)
                {
                    var vbar0 = (VScrollBar)tView.GetField("vScrollBar1", Priv).GetValue(view);
                    if (vbar0.Enabled) vbar0.Value = vbar0.Minimum;
                }
            }
            else
            {
                view.FitHorizontalScale();
            }

            view.SelectionStart = SelectFrom;
            view.SelectionEnd = SelectTo;

            MethodInfo paint = tView.GetMethod("OnPaint", Priv);
            if (paint == null) throw new ProbeSetupException("нет EnergySpectrumView.OnPaint");

            var bmp = new Bitmap(width, height);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    try
                    {
                        paint.Invoke(view, new object[] { new PaintEventArgs(g, view.ClientRectangle) });
                    }
                    catch (TargetInvocationException tie)
                    {
                        outcome.Error = tie.InnerException ?? tie;
                        string trace = outcome.Error.StackTrace ?? "";
                        outcome.TraceHasDrawFwhm = trace.Contains("DrawFWHM");
                        outcome.TraceHasDrawChart = trace.Contains("DrawChart");
                        outcome.TraceHasOnPaint = trace.Contains("OnPaint");
                    }
                }

                outcome.VerticalScale = (double)Get(view, "verticalScale");
                outcome.ScrollBaseY = (double)Get(view, "scrollBaseY");
                outcome.ScrollY = (int)Get(view, "scrollY");
                outcome.ScrollX = (int)Get(view, "scrollX");
                outcome.Height = (int)Get(view, "height");
                outcome.Width = (int)Get(view, "width");
                outcome.MaxValue = (double)Get(view, "maxValue");
                outcome.MinValue = (double)Get(view, "minValue");
                outcome.MinChannel = (int)Get(view, "minChannel");
                outcome.MaxChannel = (int)Get(view, "maxChannel");
                var vbar = (VScrollBar)tView.GetField("vScrollBar1", Priv).GetValue(view);
                outcome.VScrollEnabled = vbar.Enabled;
                outcome.VScrollMax = vbar.Maximum;
                outcome.VScrollValue = vbar.Value;
                outcome.FwhmChannels = ReadFwhm(view);
                outcome.YellowPixels = CountYellow(bmp);
                if (keepFrame) outcome.Frame = bmp;
            }
            finally
            {
                if (!keepFrame) bmp.Dispose();
            }
        }
        return outcome;
    }

    static void Section2_Scene1(double peak, int width, int height, bool expectOverflow)
    {
        Say("§2 сцена 1 — пик " + peak.ToString("F0", CultureInfo.InvariantCulture)
            + " отсчётов/канал в канале " + PeakChannel.ToString(CultureInfo.InvariantCulture)
            + ", выделение " + SelectFrom.ToString(CultureInfo.InvariantCulture) + "…"
            + SelectTo.ToString(CultureInfo.InvariantCulture)
            + ", линейная шкала, MinMax, окно на нулях (×8, каналы 0…92, выделение правее окна), полный OnPaint");
        PaintOutcome o = PaintScene(peak, width, height, Window.ZerosFit, false);
        DescribeView(o);
        if (o.Error == null)
        {
            Say("  исключения нет; жёлтых точек в кадре: " + o.YellowPixels.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Say("  исключение: " + o.Error.GetType().FullName + ": " + o.Error.Message);
            Say("  трасса содержит DrawFWHM=" + (o.TraceHasDrawFwhm ? "да" : "НЕТ")
                + " DrawChart=" + (o.TraceHasDrawChart ? "да" : "НЕТ")
                + " OnPaint=" + (o.TraceHasOnPaint ? "да" : "НЕТ"));
            Say("  --- трасса ---");
            foreach (string line in (o.Error.StackTrace ?? "").Split('\n')) Say("  " + line.TrimEnd('\r'));
            Say("  --------------");
        }

        if (expectOverflow)
        {
            Check("плечо ДО: сцена 1 даёт OverflowException", o.Error is OverflowException);
            Check("плечо ДО: трасса идёт DrawFWHM ← DrawChart ← OnPaint",
                  o.TraceHasDrawFwhm && o.TraceHasDrawChart && o.TraceHasOnPaint);
        }
        else
        {
            Check("плечо ПОСЛЕ: сцена 1 без исключения", o.Error == null);
        }
        Say("");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  3. Развёртка по M — где порог и что с вертикальной прокруткой
    // ══════════════════════════════════════════════════════════════════════

    static void Section3_Sweep(double[] sweep, int width, int height)
    {
        Say("§3 развёртка по M (та же сцена 1) — порог падения при двух положениях вертикальной прокрутки и состояние `RecalcScrollBar`");
        Say("  M            | прокрутка | исход                | verticalScale  | scrollY      | vScroll: вкл  Maximum      Value");
        foreach (double m in sweep)
        {
            foreach (Window w in new[] { Window.ZerosFit, Window.ZerosTop })
            {
                PaintOutcome o = PaintScene(m, width, height, w, false);
                string result = o.Error == null ? "тишина" : o.Error.GetType().Name;
                Say("  " + m.ToString("F0", CultureInfo.InvariantCulture).PadRight(12)
                    + " | " + (w == Window.ZerosFit ? "подгонка " : "верх     ")
                    + " | " + result.PadRight(20)
                    + " | " + o.VerticalScale.ToString("F1", CultureInfo.InvariantCulture).PadRight(14)
                    + " | " + o.ScrollY.ToString(CultureInfo.InvariantCulture).PadRight(12)
                    + " | " + (o.VScrollEnabled ? "да " : "НЕТ") + "  "
                    + o.VScrollMax.ToString(CultureInfo.InvariantCulture).PadRight(12) + " "
                    + o.VScrollValue.ToString(CultureInfo.InvariantCulture));
            }
        }
        Say("  (факт для строки, не приёмка: при height·verticalScale > int.MaxValue `RecalcScrollBar` считает в int с переносом —");
        Say("   Maximum и Value полосы становятся int.MinValue; «верх» = человек увёл вертикальную прокрутку в начало)");
        Say("");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  4. Сцена 2 — обычная: выделение в окне, verticalScale ≈ 1, кадр
    // ══════════════════════════════════════════════════════════════════════

    static void Section4_Frames(double peak, int width, int height, string outDir, string refDir, bool expectOverflow)
    {
        Frame("§4 сцена 2 — тот же спектр, весь на экране (FitHorizontalScale), выделение в окне, полный OnPaint",
              "scene2", peak, width, height, Window.Whole, outDir, refDir, expectOverflow, true);
        Frame("§4 сцена 3 — увеличение ×8, окно на пике (каналы ≈957…1049), выделение 950…1050 видно частично, полный OnPaint",
              "scene3", peak, width, height, Window.Partial, outDir, refDir, expectOverflow, false);
    }

    static void Frame(string title, string name, double peak, int width, int height, Window window,
                      string outDir, string refDir, bool expectOverflow, bool checkUnitScale)
    {
        Say(title);
        PaintOutcome o = PaintScene(peak, width, height, window, true);
        try
        {
            DescribeView(o);
            if (o.Error != null)
            {
                Say("  исключение: " + o.Error.GetType().FullName + ": " + o.Error.Message);
                foreach (string line in (o.Error.StackTrace ?? "").Split('\n')) Say("  " + line.TrimEnd('\r'));
            }
            Check(name + ": без исключения (оба плеча)", o.Error == null);
            Say("  жёлтых точек в кадре: " + o.YellowPixels.ToString(CultureInfo.InvariantCulture)
                + "; полуширина по выделению: " + o.FwhmChannels.ToString("F2", CultureInfo.InvariantCulture) + " кан.");
            Check(name + ": ломаная полуширины нарисована (жёлтых точек > 0)", o.YellowPixels > 0);
            if (checkUnitScale)
            {
                Check(name + ": verticalScale ≈ 1 (подгонка по видимому окну со всем спектром)",
                      o.VerticalScale >= 1.0 && o.VerticalScale < 1.2);
            }

            string arm = expectOverflow ? "before" : "after";
            if (outDir != null && o.Frame != null)
            {
                string path = Path.Combine(outDir, name + "_" + arm + ".png");
                o.Frame.Save(path, ImageFormat.Png);
                Say("  кадр: " + path + "  sha256 " + Sha256(path));
            }

            if (refDir != null)
            {
                string refPng = Path.Combine(refDir, name + "_before.png");
                if (!File.Exists(refPng))
                {
                    Check(name + ": эталонный кадр ДО-плеча существует: " + refPng, false);
                }
                else
                {
                    using (var reference = new Bitmap(refPng))
                    {
                        int diff = CountDifferent(reference, o.Frame);
                        Say("  сравнение с " + refPng + ": размер "
                            + (reference.Width == o.Frame.Width && reference.Height == o.Frame.Height ? "тот же" : "ДРУГОЙ")
                            + ", отличающихся точек " + diff.ToString(CultureInfo.InvariantCulture));
                        Check(name + ": кадр попиксельно совпадает с кадром ДО-плеча", diff == 0);
                    }
                }
            }
        }
        finally
        {
            if (o.Frame != null) o.Frame.Dispose();
        }
        Say("");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Сцена и вид
    // ══════════════════════════════════════════════════════════════════════

    static ResultData MakeResult(double peak)
    {
        var s = new EnergySpectrum(1.0, Channels);
        s.EnergyCalibration = new PolynomialEnergyCalibration(); // порядок 1, E = канал
        s.MeasurementTime = MeasurementTime;
        for (int i = 0; i < Channels; i++)
        {
            double v = (i >= 900 && i < 2000) ? 200.0 : 0.0;
            double d = (i - PeakChannel) / PeakSigma;
            v += peak * Math.Exp(-0.5 * d * d);
            if (v > int.MaxValue) throw new ProbeSetupException("отсчёт не влезает в int: " + v.ToString("R", CultureInfo.InvariantCulture));
            s.Spectrum[i] = (int)Math.Round(v);
        }
        var rd = new ResultData();
        rd.EnergySpectrum = s;
        return rd;
    }

    static void DescribeView(PaintOutcome o)
    {
        Say("  вид: height=" + o.Height.ToString(CultureInfo.InvariantCulture)
            + " width=" + o.Width.ToString(CultureInfo.InvariantCulture)
            + " verticalScale=" + o.VerticalScale.ToString("F3", CultureInfo.InvariantCulture)
            + " scrollBaseY=" + o.ScrollBaseY.ToString("F0", CultureInfo.InvariantCulture)
            + " scrollY=" + o.ScrollY.ToString(CultureInfo.InvariantCulture)
            + " scrollX=" + o.ScrollX.ToString(CultureInfo.InvariantCulture)
            + " окно подгонки: каналы " + o.MinChannel.ToString(CultureInfo.InvariantCulture) + "…"
            + o.MaxChannel.ToString(CultureInfo.InvariantCulture)
            + " max=" + o.MaxValue.ToString("F2", CultureInfo.InvariantCulture)
            + " min=" + o.MinValue.ToString("F2", CultureInfo.InvariantCulture)
            + " vScroll(" + (o.VScrollEnabled ? "вкл" : "ВЫКЛ") + " max=" + o.VScrollMax.ToString(CultureInfo.InvariantCulture)
            + " value=" + o.VScrollValue.ToString(CultureInfo.InvariantCulture) + ")");
    }

    static double ReadFwhm(EnergySpectrumView view)
    {
        object analytics = Get(view, "selectionAnalytics");
        if (analytics == null) return double.NaN;
        PropertyInfo p = analytics.GetType().GetProperty("FwhmResult");
        object r = p == null ? null : p.GetValue(analytics, null);
        if (r == null) return double.NaN;
        PropertyInfo w = r.GetType().GetProperty("RightChannel");
        PropertyInfo l = r.GetType().GetProperty("LeftChannel");
        if (w == null || l == null) return double.NaN;
        return Convert.ToDouble(w.GetValue(r, null), CultureInfo.InvariantCulture)
             - Convert.ToDouble(l.GetValue(r, null), CultureInfo.InvariantCulture);
    }

    static int CountYellow(Bitmap bmp)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.R == 255 && c.G == 255 && c.B == 0) n++;
            }
        return n;
    }

    static int CountDifferent(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return int.MaxValue;
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) n++;
        return n;
    }

    static void Set(object target, string field, object value)
    {
        FieldInfo f = tView.GetField(field, Priv);
        if (f == null) throw new ProbeSetupException("нет поля EnergySpectrumView." + field);
        f.SetValue(target, value);
    }

    static object Get(object target, string field)
    {
        FieldInfo f = tView.GetField(field, Priv);
        if (f == null) throw new ProbeSetupException("нет поля EnergySpectrumView." + field);
        return f.GetValue(target);
    }

    sealed class ProbeSetupException : Exception
    {
        public ProbeSetupException(string message) : base(message) { }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Служебное
    // ══════════════════════════════════════════════════════════════════════

    static void Check(string what, bool ok)
    {
        Say((ok ? "  ✔ " : "  ✘ ") + what);
        if (!ok) failures++;
    }

    static void Say(string s)
    {
        Console.WriteLine(s);
        Log.AppendLine(s);
    }

    static void Finish(string outDir, bool expectOverflow)
    {
        if (outDir == null) return;
        try
        {
            string name = expectOverflow ? "fwhm_overflow_before.log" : "fwhm_overflow_after.log";
            File.WriteAllText(Path.Combine(outDir, name), Log.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("журнал не записан: " + ex.Message);
        }
    }

    static string Sha256(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(path))
        {
            byte[] h = sha.ComputeHash(fs);
            var sb = new StringBuilder(64);
            foreach (byte b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
