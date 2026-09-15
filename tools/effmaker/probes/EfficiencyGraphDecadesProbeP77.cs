// `AMBER37` / `AMBER38`, П77 15.09.2026: ОСЬ Y ГРАФИКА «CALCULATE FROM GEOMETRY»
// И СНЯТАЯ ПОДСКАЗКА НАД ПАНЕЛЬЮ ПАРАМЕТРОВ.
//
//     efficiencygraphdecadesprobep77 [--out=<файл>]
//
// Что мерится.
//
// 1. `AMBER37`. `EfficiencyCurveGraph` (вкладка «Calculate from geometry» формы
//    `EfficiencyMakerForm`) РИСУЕТ подписи декад оси Y через
//    `Graphics.DrawString`, поэтому контрол рисует в МЕТАФАЙЛ EMF+ — тем же
//    приёмом, что `GraphCultureProbeF27`, — а проба вынимает из записей
//    `DrawString` текст и координату y, из `DrawLines` — линии сетки и точки
//    кривой, из `DrawRects` — рамку поля. Ни `DrawToBitmap`, ни цвета пикселя
//    (ловушка `A244`/П4: `DrawToBitmap` шлёт `WM_PRINT` и перерисовывает сам).
//
//    Сцена 1 — синтетическая кривая с хвостом, как у AS80x80 в корпусе:
//    7 кэВ 1e-25 … 100 кэВ 0.3 … 3000 кэВ 0.014. Ожидание по решению Amber
//    15.09.2026 «6 декад под максимумом»: подписей декад РОВНО семь,
//    `1e0`…`1e-6`, ни одной ниже, ни одного повтора, их y равноотстоящие с
//    шагом высота_поля/6 ± 1 px; линий сетки столько же и на тех же высотах,
//    от нижней рамки до верхней; хвост кривой ниже 1e-6 прижат к НИЖНЕЙ РАМКЕ,
//    а не к внутренней линии. Старая сборка (низ по минимуму всех точек, зажим
//    `mapY` на константе 1e-12) обязана дать 26 подписей, из них 14 на одной
//    высоте, и хвост «полкой» внутри поля — это положительный контроль пробы.
//
//    Сцена 2 — кривая 1e-3…0.3: минимум выше шести декад под максимумом, низ
//    по данным, как и раньше: подписи `1e0`…`1e-3`. Сцена 2б — границы РОВНО
//    на степенях десяти (0.01…0.1): «по исходным значениям» (решение Amber) —
//    `1e-1`, `1e-2` и только они; округление до `float` сдвигало такую
//    границу на декаду в ту или другую сторону.
//
//    Сцена 3 — обе кривые, пунктирная из конфигурации и посчитанная, в одном
//    поле: те же семь подписей, что в сцене 1.
//
// 2. `AMBER38`. Подсказки над панелью параметров нет: ни поля `calcHintLabel`
//    в типе формы, ни ключа `EfficiencyMakerCalcHint` в ресурсах обеих культур
//    (читается `ResourceManager`, то есть мерится СОБРАННЫЙ ресурс, а не текст
//    .resx), ни `Label` прямо на вкладке расчёта; панель параметров стоит у
//    верха вкладки (`Top == 12`), кнопка расчёта под ней без наложения, высота
//    вкладки — по кнопке, график с журналом — сразу под вкладками. Форма
//    создаётся обычным конструктором и НЕ показывается (как в
//    `MakerSaveProbe`); что достаётся графику и журналу от освободившейся
//    высоты — печатается числами: это факт для журнала, не критерий.
//
// ⛔ ОКНО НЕ ПОДНИМАЕТСЯ. Сторож модальных окон (`A245`, образец
//    `CultureProbeO14`/`GraphCultureProbeF27`) стоит первым делом: голое
//    `MessageBox.Show` на безоконном пути вешает прогон насмерть.
//
// ⚠ У снятия с метафайла свой положительный контроль: «подписей не найдено»
//   неотличимо от «снятие не работает», а разбор записей `DrawLines`/`DrawRects`
//   — свой, не F27, и проверяется на известных фигурах до сцен.
//
// Числа печатаются инвариантной культурой (правило Amber 05.09.2026).
//
// Код возврата: 0 — сошлось; 1 — расхождения (напечатаны); 2 — неизвестный ключ.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

static class EfficiencyGraphDecadesProbeP77
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
                             | BindingFlags.Public | BindingFlags.NonPublic;

    // Размер контрола — как у Panel1 сплиттера по дизайнеру формы (942 × 262):
    // на нём и мерится шаг подписей. Само поле (рамка) берётся из отрисовки.
    const int GraphWidth = 942;
    const int GraphHeight = 262;

    static readonly Regex DecadeLabel = new Regex("^1e-?[0-9]+$", RegexOptions.CultureInvariant);

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // ⛔ Культура ЦЕЛИКОМ инвариантная (`T245`, приказ Amber 05.09.2026).
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string outPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание.
            else
            {
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }
        }

        ModalWatchStart();

        Say("== `AMBER37`/`AMBER38` П77: ось Y графика «Calculate from geometry» и снятая подсказка ==");
        Say("");
        Assembly app = typeof(EfficiencyCurveGraph).Assembly;
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                          .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }

        try
        {
            CaptureSelfTest();
            Amber37();
            Amber38();
        }
        catch (Exception ex)
        {
            Say("");
            Say("⛔ ЗАМЕР ОБОРВАЛСЯ: " + ex.GetType().Name + ": " + ex.Message);
            Say(ex.StackTrace);
            failures++;
        }

        Say("");
        Say("модальных окон за прогон: " + modalSeen);
        Say(failures == 0 ? "СОШЛОСЬ, расхождений нет" : "НЕ СОШЛОСЬ: " + failures);
        ModalWatchStop();
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СНЯТИЕ С ОТРИСОВКИ
    //
    //  `Graphics.FromImage(Metafile)` записывает вызовы, `EnumerateMetafile`
    //  проигрывает их по одной записи. Раскладка данных (MS-EMFPLUS):
    //    DrawString: BrushId(4) FormatID(4) Length(4) LayoutRect(16: X Y W H,
    //                float) String(Length×2, UTF-16) — текст со смещения 28,
    //                Y — со смещения 16;
    //    DrawLines:  Count(4) PointData — float-пары, либо int16-пары при
    //                флаге C (0x4000); флаг P (0x0800, относительные точки) —
    //                не разбирается, считается отказом снятия;
    //    DrawRects:  Count(4) RectData — float-четвёрки, либо int16 при 0x4000.
    //  Перо/кисть/шрифт лежат в флагах (ObjectID) и пробе не нужны.
    // ══════════════════════════════════════════════════════════════════════

    sealed class Str
    {
        public string S;
        public float X, Y;
    }

    sealed class Drawing
    {
        public readonly List<Str> Texts = new List<Str>();
        public readonly List<List<PointF>> Lines = new List<List<PointF>>();
        public readonly List<RectangleF> Rects = new List<RectangleF>();
        public int Unparsed;
    }

    static Drawing current;

    static Drawing Capture(Action<Graphics> draw)
    {
        Drawing d = new Drawing();
        using (Bitmap host = new Bitmap(8, 8))
        using (Graphics rg = Graphics.FromImage(host))
        {
            IntPtr hdc = rg.GetHdc();
            Metafile mf;
            try
            {
                mf = new Metafile(hdc, new RectangleF(0, 0, 1200, 900),
                                  MetafileFrameUnit.Pixel, EmfType.EmfPlusOnly);
            }
            finally { rg.ReleaseHdc(hdc); }

            try
            {
                using (Graphics g = Graphics.FromImage(mf))
                {
                    draw(g);
                }

                current = d;
                using (Bitmap play = new Bitmap(4, 4))
                using (Graphics pg = Graphics.FromImage(play))
                {
                    pg.EnumerateMetafile(mf, new Point(0, 0), OnRecord);
                }
            }
            finally
            {
                current = null;
                mf.Dispose();
            }
        }

        return d;
    }

    static bool OnRecord(EmfPlusRecordType recordType, int flags, int dataSize,
                         IntPtr data, PlayRecordCallback cb)
    {
        Drawing d = current;
        if (d == null || data == IntPtr.Zero || dataSize <= 0) return true;
        byte[] buf = new byte[dataSize];
        Marshal.Copy(data, buf, 0, dataSize);
        bool compressed = (flags & 0x4000) != 0;
        bool relative = (flags & 0x0800) != 0;

        switch (recordType)
        {
            case EmfPlusRecordType.DrawString:
            {
                int len = dataSize >= 28 ? BitConverter.ToInt32(buf, 8) : -1;
                if (len >= 0 && 28 + len * 2 <= dataSize)
                {
                    d.Texts.Add(new Str
                    {
                        S = Encoding.Unicode.GetString(buf, 28, len * 2),
                        X = BitConverter.ToSingle(buf, 12),
                        Y = BitConverter.ToSingle(buf, 16),
                    });
                }
                else d.Unparsed++;
                break;
            }

            case EmfPlusRecordType.DrawLines:
            {
                int n = dataSize >= 4 ? BitConverter.ToInt32(buf, 0) : -1;
                int size = compressed ? 4 : 8;
                if (relative || n < 0 || 4 + n * size > dataSize)
                {
                    d.Unparsed++;
                    break;
                }

                List<PointF> pts = new List<PointF>(n);
                for (int i = 0; i < n; i++)
                {
                    int o = 4 + i * size;
                    pts.Add(compressed
                        ? new PointF(BitConverter.ToInt16(buf, o), BitConverter.ToInt16(buf, o + 2))
                        : new PointF(BitConverter.ToSingle(buf, o), BitConverter.ToSingle(buf, o + 4)));
                }

                d.Lines.Add(pts);
                break;
            }

            case EmfPlusRecordType.DrawRects:
            {
                int n = dataSize >= 4 ? BitConverter.ToInt32(buf, 0) : -1;
                int size = compressed ? 8 : 16;
                if (n < 0 || 4 + n * size > dataSize)
                {
                    d.Unparsed++;
                    break;
                }

                for (int i = 0; i < n; i++)
                {
                    int o = 4 + i * size;
                    d.Rects.Add(compressed
                        ? new RectangleF(BitConverter.ToInt16(buf, o), BitConverter.ToInt16(buf, o + 2),
                                         BitConverter.ToInt16(buf, o + 4), BitConverter.ToInt16(buf, o + 6))
                        : new RectangleF(BitConverter.ToSingle(buf, o), BitConverter.ToSingle(buf, o + 4),
                                         BitConverter.ToSingle(buf, o + 8), BitConverter.ToSingle(buf, o + 12)));
                }

                break;
            }
        }

        return true;
    }

    static void CaptureSelfTest()
    {
        Say("");
        Say("[полож. контроль СНЯТИЯ] метафайл отдаёт обратно то, что нарисовано:");
        Drawing got = Capture(delegate(Graphics g)
        {
            using (Font f = new Font("Segoe UI", 9f))
            using (Pen p = new Pen(Color.Black))
            {
                g.DrawString("1e-6", f, Brushes.Black, 2f, 100.5f);
                g.DrawLine(p, 58f, 100f, 922f, 100f);
                g.DrawLines(p, new PointF[] { new PointF(58f, 228f), new PointF(100.25f, 150.5f), new PointF(300f, 12f) });
                g.DrawRectangle(p, new Rectangle(58, 12, 864, 216));
            }
        });

        bool text = got.Texts.Count == 1 && got.Texts[0].S == "1e-6"
                    && Math.Abs(got.Texts[0].X - 2f) < 0.01f && Math.Abs(got.Texts[0].Y - 100.5f) < 0.01f;
        bool lines = got.Lines.Count == 2
                     && got.Lines[0].Count == 2 && Near(got.Lines[0][0], 58f, 100f) && Near(got.Lines[0][1], 922f, 100f)
                     && got.Lines[1].Count == 3 && Near(got.Lines[1][0], 58f, 228f)
                     && Near(got.Lines[1][1], 100.25f, 150.5f) && Near(got.Lines[1][2], 300f, 12f);
        bool rect = got.Rects.Count == 1 && got.Rects[0] == new RectangleF(58, 12, 864, 216);
        Say("  текст: " + Show(got.Texts) + (text ? " — верно" : "  ⛔ НЕ ТО"));
        Say("  линии: " + got.Lines.Count + " (точек " + string.Join("/", got.Lines.Select(l => l.Count.ToString(CultureInfo.InvariantCulture)).ToArray())
            + ")" + (lines ? " — верно" : "  ⛔ НЕ ТО: " + string.Join(" | ", got.Lines.Select(ShowPts).ToArray())));
        Say("  рамка: " + (got.Rects.Count == 1 ? ShowRect(got.Rects[0]) : got.Rects.Count + " шт.")
            + (rect ? " — верно" : "  ⛔ НЕ ТО"));
        if (got.Unparsed != 0) Say("  ⛔ записей не разобрано: " + got.Unparsed);
        if (!(text && lines && rect) || got.Unparsed != 0)
        {
            Say("  ⛔ СНЯТИЕ НЕ РАБОТАЕТ, весь замер недействителен");
            failures++;
        }
    }

    static bool Near(PointF p, float x, float y)
    {
        return Math.Abs(p.X - x) < 0.01f && Math.Abs(p.Y - y) < 0.01f;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AMBER37 — сцены
    // ══════════════════════════════════════════════════════════════════════

    static ROIEfficiencyData P(double keV, double eff)
    {
        return new ROIEfficiencyData { Energy = keV, Efficiency = eff, ErrorPercent = 3.0 };
    }

    /// <summary>Хвост, как у AS80x80 в корпусе: поглощение на 7–15 кэВ.</summary>
    static List<ROIEfficiencyData> TailCurve()
    {
        return new List<ROIEfficiencyData>
        {
            P(7, 1e-25), P(8, 1e-20), P(10, 1e-12), P(15, 1e-8), P(20, 1e-5), P(30, 1e-2),
            P(50, 0.15), P(100, 0.3), P(200, 0.2), P(500, 0.08), P(1000, 0.04), P(3000, 0.014),
        };
    }

    /// <summary>Минимум выше шести декад под максимумом — низ по данным.</summary>
    static List<ROIEfficiencyData> ShallowCurve()
    {
        return new List<ROIEfficiencyData>
        {
            P(30, 0.02), P(60, 0.2), P(100, 0.3), P(300, 0.1), P(1000, 0.03), P(3000, 0.0015),
        };
    }

    /// <summary>Границы ровно на степенях десяти: 0.01 и 0.1.</summary>
    static List<ROIEfficiencyData> ExactCurve()
    {
        return new List<ROIEfficiencyData>
        {
            P(30, 0.05), P(100, 0.1), P(300, 0.06), P(1000, 0.02), P(3000, 0.01),
        };
    }

    /// <summary>Кривая конфигурации прибора — рисуется пунктиром.</summary>
    static List<ROIEfficiencyData> ReferenceCurve()
    {
        return new List<ROIEfficiencyData>
        {
            P(50, 0.1), P(100, 0.25), P(500, 0.07), P(1000, 0.035), P(3000, 0.012),
        };
    }

    static EfficiencyFitResult Fit(List<ROIEfficiencyData> curve)
    {
        return new EfficiencyFitResult
        {
            LevelSource = EfficiencyLevelSource.Simulation,
            Curve = curve,
            MinEnergy = curve.Min(p => p.Energy),
            MaxEnergy = curve.Max(p => p.Energy),
        };
    }

    static Drawing Draw(List<ROIEfficiencyData> reference, EfficiencyFitResult fit)
    {
        MethodInfo onPaint = typeof(EfficiencyCurveGraph).GetMethod(
            "OnPaint", Any, null, new Type[] { typeof(PaintEventArgs) }, null);
        if (onPaint == null) throw new InvalidOperationException("в сборке нет EfficiencyCurveGraph.OnPaint");

        using (EfficiencyCurveGraph graph = new EfficiencyCurveGraph())
        {
            graph.Size = new Size(GraphWidth, GraphHeight);
            graph.SetData(reference, fit);
            return Capture(delegate(Graphics g)
            {
                onPaint.Invoke(graph, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, GraphWidth, GraphHeight)) });
            });
        }
    }

    static void Amber37()
    {
        Say("");
        Say("== `AMBER37`: подписи декад оси Y (контрол " + GraphWidth + "×" + GraphHeight + ") ==");
        Say("  Math.Log10(0.01) = " + Math.Log10(0.01).ToString("R", CultureInfo.InvariantCulture)
            + ", Math.Log10(0.1) = " + Math.Log10(0.1).ToString("R", CultureInfo.InvariantCulture)
            + ", (float)0.01 = " + ((double)(float)0.01).ToString("R", CultureInfo.InvariantCulture)
            + ", (float)0.1 = " + ((double)(float)0.1).ToString("R", CultureInfo.InvariantCulture));

        // Сцена 1: хвост 1e-25, максимум 0.3 → шесть декад под 1e0.
        Scene("сцена 1: хвост 7 кэВ 1e-25 … 100 кэВ 0.3 … 3000 кэВ 0.014, одна кривая",
              null, Fit(TailCurve()), Expected(0, -6), 4);

        // Сцена 2: минимум 1.5e-3 — низ по данным, как раньше.
        Scene("сцена 2: кривая 1.5e-3 … 0.3 — минимум выше шести декад, низ по данным",
              null, Fit(ShallowCurve()), Expected(0, -3), 0);

        // Сцена 2б: границы ровно на степенях десяти — по исходным значениям.
        // Точка 0.01 — это сама нижняя декада, и по построению она лежит НА
        // нижней рамке (одна точка на рамке — не хвост, а граница поля).
        Scene("сцена 2б: границы РОВНО 0.01 … 0.1 — декады по исходным double, не по float",
              null, Fit(ExactCurve()), Expected(-1, -2), 1);

        // Сцена 3: обе кривые в одном поле.
        Scene("сцена 3: пунктирная кривая конфигурации (0.012…0.25) + посчитанная с хвостом",
              ReferenceCurve(), Fit(TailCurve()), Expected(0, -6), 4);
    }

    static string[] Expected(int top, int bottom)
    {
        List<string> list = new List<string>();
        for (int d = top; d >= bottom; d--) list.Add("1e" + d.ToString(CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    /// <param name="onFrame">сколько точек посчитанной кривой обязаны лежать на нижней рамке</param>
    static void Scene(string title, List<ROIEfficiencyData> reference, EfficiencyFitResult fit,
                      string[] expected, int onFrame)
    {
        Say("");
        Say("── " + title);
        Drawing d = Draw(reference, fit);
        if (d.Unparsed != 0)
        {
            Say("  ⛔ записей не разобрано: " + d.Unparsed);
            failures++;
        }

        // Рамка поля — из отрисовки, не из копии констант контрола.
        if (d.Rects.Count != 1)
        {
            Say("  ⛔ рамок поля нарисовано " + d.Rects.Count + ", ждали одну — дальше сцена не мерится");
            failures++;
            return;
        }

        RectangleF plot = d.Rects[0];
        float top = plot.Top, bottom = plot.Bottom, height = plot.Height;
        Say("  поле: " + ShowRect(plot));

        // ── подписи декад: по y сверху вниз (то есть от старшей декады к младшей)
        List<Str> labels = d.Texts.Where(t => DecadeLabel.IsMatch(t.S)).OrderBy(t => t.Y).ToList();
        List<Str> others = d.Texts.Where(t => !DecadeLabel.IsMatch(t.S)).ToList();
        Say("  подписей декад: " + labels.Count + " — " + string.Join(" ", labels.Select(t => t.S).ToArray()));
        Say("  их y: " + string.Join(" ", labels.Select(t => F1(t.Y)).ToArray()));
        Say("  прочих подписей (шкала X и её имя): " + others.Count + " — " + string.Join(" ", others.Select(t => t.S).ToArray()));

        string want = string.Join(" ", expected);
        string have = string.Join(" ", labels.Select(t => t.S).ToArray());
        Check("подписи ровно " + want, have == want, have);

        // повторы — по тексту и по высоте
        var dupText = labels.GroupBy(t => t.S).Where(g => g.Count() > 1).Select(g => g.Key + "×" + g.Count()).ToArray();
        Check("повторов текста нет", dupText.Length == 0, dupText.Length == 0 ? "нет" : string.Join(" ", dupText));
        int sameY = 0;
        for (int i = 1; i < labels.Count; i++)
        {
            if (Math.Abs(labels[i].Y - labels[i - 1].Y) < 1f) sameY++;
        }

        Check("подписей на одной высоте (|Δy| < 1 px) нет", sameY == 0,
              sameY == 0 ? "нет" : "пар на одной высоте: " + sameY
              + " (y=" + F1(labels.Select(t => t.Y).GroupBy(y => (int)Math.Round(y)).OrderByDescending(g => g.Count()).First().First()) + ")");

        // равноотстоящие: шаг = высота поля / (число декад − 1), с точностью до пикселя
        if (labels.Count >= 2)
        {
            float step = height / (labels.Count - 1);
            float worst = 0;
            List<string> gaps = new List<string>();
            for (int i = 1; i < labels.Count; i++)
            {
                float gap = labels[i].Y - labels[i - 1].Y;
                gaps.Add(F1(gap));
                worst = Math.Max(worst, Math.Abs(gap - step));
            }

            Check("шаг подписей = высота поля / " + (labels.Count - 1) + " = " + F1(step) + " px ± 1",
                  worst <= 1f, "шаги " + string.Join(" ", gaps.ToArray()) + ", худшее отклонение " + F1(worst));
        }
        else
        {
            Check("подписей хватает на шаг", false, labels.Count.ToString(CultureInfo.InvariantCulture));
        }

        // ── линии сетки декад: горизонтальные, во всё поле; столько же, от нижней рамки до верхней
        List<float> gridY = d.Lines
            .Where(l => l.Count == 2 && Math.Abs(l[0].Y - l[1].Y) < 0.01f
                        && Math.Abs(Math.Min(l[0].X, l[1].X) - plot.Left) < 0.01f
                        && Math.Abs(Math.Max(l[0].X, l[1].X) - plot.Right) < 0.01f)
            .Select(l => l[0].Y).OrderBy(y => y).ToList();
        Say("  линий декад: " + gridY.Count + " — y " + string.Join(" ", gridY.Select(F1).ToArray()));
        Check("линий декад столько же, сколько подписей", gridY.Count == labels.Count,
              gridY.Count.ToString(CultureInfo.InvariantCulture));
        if (gridY.Count >= 2)
        {
            Check("нижняя линия декады — на нижней рамке (" + F1(bottom) + ")", Math.Abs(gridY.Last() - bottom) <= 0.5f, F1(gridY.Last()));
            Check("верхняя линия декады — на верхней рамке (" + F1(top) + ")", Math.Abs(gridY.First() - top) <= 0.5f, F1(gridY.First()));
            int gridSame = 0;
            for (int i = 1; i < gridY.Count; i++) if (Math.Abs(gridY[i] - gridY[i - 1]) < 1f) gridSame++;
            Check("линий декад на одной высоте нет", gridSame == 0, gridSame == 0 ? "нет" : "пар: " + gridSame);
        }

        // ── кривая: последняя ломаная из многих точек — посчитанная (рисуется после пунктирной)
        List<List<PointF>> curves = d.Lines.Where(l => l.Count >= 3).ToList();
        int wantCurves = (reference != null ? 1 : 0) + 1;
        Check("ломаных кривых нарисовано " + wantCurves, curves.Count == wantCurves,
              curves.Count.ToString(CultureInfo.InvariantCulture));
        if (curves.Count >= 1)
        {
            List<PointF> curve = curves.Last().OrderBy(p => p.X).ToList();
            int frame = curve.Count(p => Math.Abs(p.Y - bottom) <= 0.5f);
            Check("точек кривой на нижней рамке: " + onFrame, frame == onFrame,
                  frame + "; y первых точек " + string.Join(" ", curve.Take(Math.Max(onFrame, 3)).Select(p => F1(p.Y)).ToArray()));
            int inside = curve.Count(p => p.Y >= top - 0.5f && p.Y <= bottom + 0.5f);
            Check("все точки кривой внутри поля", inside == curve.Count,
                  inside + " из " + curve.Count);
            Say("  точек кривой " + curve.Count + ", y от " + F1(curve.Min(p => p.Y)) + " до " + F1(curve.Max(p => p.Y)));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  AMBER38 — подсказка снята, панель у верха вкладки
    // ══════════════════════════════════════════════════════════════════════

    static void Amber38()
    {
        Say("");
        Say("== `AMBER38`: подсказка над панелью параметров снята ==");

        FieldInfo hint = typeof(EfficiencyMakerForm).GetField("calcHintLabel", Any);
        Check("поля calcHintLabel в типе формы нет", hint == null, hint == null ? "нет" : "ЕСТЬ");

        PropertyInfo prop = typeof(Resources).GetProperty("EfficiencyMakerCalcHint",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Check("свойства Resources.EfficiencyMakerCalcHint нет", prop == null, prop == null ? "нет" : "ЕСТЬ");

        // Собранные ресурсы обеих культур — а не текст .resx.
        foreach (CultureInfo culture in new CultureInfo[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("ru") })
        {
            string name = culture.Equals(CultureInfo.InvariantCulture) ? "neutral" : culture.Name;
            string hintText = Resources.ResourceManager.GetString("EfficiencyMakerCalcHint", culture);
            Check("ключа EfficiencyMakerCalcHint нет в ресурсах [" + name + "]", hintText == null,
                  hintText == null ? "нет" : "«" + hintText + "»");
            // Контроль чтения: соседний ключ той же вкладки обязан читаться.
            string tab = Resources.ResourceManager.GetString("EfficiencyMakerTabCalculate", culture);
            Check("(контроль чтения ресурсов) EfficiencyMakerTabCalculate [" + name + "] читается",
                  !string.IsNullOrEmpty(tab), tab ?? "null");
        }

        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            TabControl tabs = (TabControl)Field(form, "tabControl");
            TabPage calc = (TabPage)Field(form, "tabPageCalculate");
            Button button = (Button)Field(form, "calculateButton");
            SplitContainer split = (SplitContainer)Field(form, "splitContainer");
            Control graph = (Control)Field(form, "graph");
            Control log = (Control)Field(form, "logTextBox");

            // Раскладка вкладки расчёта считается при её выборе; форма не
            // показывается, поэтому раскладку зовём и явно — она идемпотентна.
            tabs.SelectedTab = calc;
            MethodInfo layout = typeof(EfficiencyMakerForm).GetMethod("UpdateGeometryLayout", Any);
            if (layout == null) throw new InvalidOperationException("в сборке нет EfficiencyMakerForm.UpdateGeometryLayout");
            layout.Invoke(form, null);

            Label[] labels = calc.Controls.OfType<Label>().ToArray();
            Check("Label прямо на вкладке расчёта нет", labels.Length == 0,
                  labels.Length == 0 ? "нет" : string.Join(" | ", labels.Select(l => "«" + l.Text + "» Top=" + l.Top + " Bottom=" + l.Bottom).ToArray()));

            GroupBox group = calc.Controls.OfType<GroupBox>().FirstOrDefault(g => g.Text == Resources.ResponseMatrixParameters);
            Check("панель параметров (GroupBox «" + Resources.ResponseMatrixParameters + "») на вкладке есть", group != null,
                  group == null ? "нет" : "Top=" + group.Top + " Bottom=" + group.Bottom);
            if (group != null)
            {
                Check("панель у верха вкладки: Top == 12", group.Top == 12, group.Top.ToString(CultureInfo.InvariantCulture));
                Check("кнопка расчёта под панелью без наложения: Top == панель.Bottom + 12",
                      button.Top == group.Bottom + 12, "кнопка Top=" + button.Top + ", панель Bottom=" + group.Bottom);
            }

            int chrome = tabs.Height - tabs.DisplayRectangle.Height;
            Check("высота вкладок по кнопке: tabControl.Height == кнопка.Bottom + 12 + хром(" + chrome + ")",
                  tabs.Height == button.Bottom + 12 + chrome,
                  "tabControl.Height=" + tabs.Height + ", кнопка Bottom=" + button.Bottom);
            Check("график с журналом сразу под вкладками: splitContainer.Top == tabControl.Bottom + 12",
                  split.Top == tabs.Bottom + 12, "split.Top=" + split.Top + ", tabs.Bottom=" + tabs.Bottom);
            // ⚠ `Control.Visible` у непоказанной формы всегда false (оно
            //   действующее, по цепочке родителей); собственный флаг контрола
            //   читается через непубличный `GetState(STATE_VISIBLE = 2)`.
            MethodInfo getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);
            bool ownVisible = getState != null && (bool)getState.Invoke(split, new object[] { 2 });
            Check("график с журналом не скрыты на вкладке расчёта (собственный флаг Visible)", ownVisible,
                  getState == null ? "Control.GetState не найден" : (ownVisible ? "да" : "нет"));

            Say("  факт для журнала (форма " + form.ClientSize.Width + "×" + form.ClientSize.Height + "): "
                + "вкладки Height=" + tabs.Height + "; сплиттер Top=" + split.Top + " Height=" + split.Height
                + " FixedPanel=" + split.FixedPanel + " SplitterDistance=" + split.SplitterDistance
                + "; график Height=" + graph.Height + "; журнал Height=" + log.Height);
        }
    }

    static object Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Any);
        if (field == null) throw new InvalidOperationException("в типе " + target.GetType().Name + " нет поля " + name);
        return field.GetValue(target);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Печать
    // ══════════════════════════════════════════════════════════════════════

    static void Check(string what, bool ok, string got)
    {
        Say("  " + (ok ? "✓ " : "⛔ ") + what + ": " + got);
        if (!ok) failures++;
    }

    static string F1(float v)
    {
        return v.ToString("F1", CultureInfo.InvariantCulture);
    }

    static string Show(List<Str> texts)
    {
        return string.Join(" | ", texts.Select(t => "«" + t.S + "» (" + F1(t.X) + ", " + F1(t.Y) + ")").ToArray());
    }

    static string ShowPts(List<PointF> pts)
    {
        return string.Join(" ", pts.Select(p => "(" + F1(p.X) + "," + F1(p.Y) + ")").ToArray());
    }

    static string ShowRect(RectangleF r)
    {
        return "(" + F1(r.X) + ", " + F1(r.Y) + ", " + F1(r.Width) + " × " + F1(r.Height) + "), низ " + F1(r.Bottom);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (образец — `CultureProbeO14`, полоса F20, `A245`)
    // ══════════════════════════════════════════════════════════════════════

    const string DialogClass = "#32770";
    const uint WM_CLOSE = 0x0010;

    delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static volatile bool modalWatchStop;
    static Thread modalWatchThread;
    static int modalSeen;

    static void ModalWatchStart()
    {
        modalWatchThread = new Thread(delegate()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            Dictionary<long, bool> known = new Dictionary<long, bool>();
            while (!modalWatchStop)
            {
                List<IntPtr> found = new List<IntPtr>();
                try
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != self) return true;
                        StringBuilder cls = new StringBuilder(64);
                        GetClassNameW(h, cls, cls.Capacity);
                        if (cls.ToString() == DialogClass) found.Add(h);
                        return true;
                    }, IntPtr.Zero);
                }
                catch (Exception) { }

                foreach (IntPtr h in found)
                {
                    long key = h.ToInt64();
                    if (known.ContainsKey(key)) continue;
                    known[key] = true;
                    modalSeen++;
                    failures++;
                    Say("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                        + "» — сторож закрывает его сам, разряд `A245`");
                    try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception) { }
                }

                Thread.Sleep(200);
            }
        });
        modalWatchThread.IsBackground = true;
        modalWatchThread.Start();
    }

    static string ModalText(IntPtr dialog)
    {
        StringBuilder acc = new StringBuilder();
        try
        {
            EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassNameW(ch, cls, cls.Capacity);
                if (cls.ToString() == "Static")
                {
                    StringBuilder txt = new StringBuilder(512);
                    GetWindowTextW(ch, txt, txt.Capacity);
                    string s = txt.ToString().Trim();
                    if (s.Length > 0)
                    {
                        if (acc.Length > 0) acc.Append(" / ");
                        acc.Append(s);
                    }
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }

        return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
    }

    static void ModalWatchStop()
    {
        modalWatchStop = true;
        if (modalWatchThread != null) modalWatchThread.Join(2000);
    }

    static void Say(string line)
    {
        Console.WriteLine(line);
        lock (Log) Log.AppendLine(line);
    }

    static void Finish(string outPath)
    {
        if (string.IsNullOrEmpty(outPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("вывод: " + Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine("вывод НЕ записан: " + ex.Message);
        }
    }
}
