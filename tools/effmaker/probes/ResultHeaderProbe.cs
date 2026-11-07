using BecquerelMonitor;
using System;
using System.Collections;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`fixFsa2`, `AMBER299`, 07.11.2026) ШАПКА ОКНА РЕЗУЛЬТАТА ВМЕЩАЕТ ДВУХСТРОЧНЫЙ ЗАГОЛОВОК.
    /// Проверка экраном 07.11.2026 (снимки 17z–19z полосы `screen`): «FSA: Бк по матрице отклика
    /// (пересчёт…)» на узкой панели переносился на вторую строку, а шапка XPTable стояла 20 точек —
    /// обе строки срезаны сверху и снизу.
    ///
    /// Проба ставит настоящему <see cref="DCResultView"/> (в форме-носителе за краем экрана)
    /// заголовок окна теми же словами ресурсов и судит: высота шапки не меньше высоты текста
    /// заголовка, посчитанной НЕЗАВИСИМО — тем же рисованием, что у `XPHeaderRenderer`
    /// (`Graphics.MeasureString` в ширину колонки минус 4 точки, перенос по словам) плюс 4 точки
    /// рамки. Второе: без пометки «(пересчёт…)» высота та же, что с ней (шапка не прыгает на
    /// каждом пересчёте). Третье: значок «не обнаружен» у значения — подпись культуры интерфейса,
    /// и он ЦЕЛИКОМ в ячейке (замечание Amber 07.11.2026: «…не вся влезала в строчку, она была
    /// обрезанная»): рамка раскладки окна (`ResultValueCellRenderer.MarkLayout`) не выше и не шире
    /// места ячейки, текст выбранным шрифтом — внутри рамки. Положительный контроль — прежняя
    /// раскладка (8 pt, рамка на 2 точки ниже места): обязана быть признана срезанной.
    ///
    /// Ключи: --ui=ru|en; --width=&lt;ширина носителя, точек&gt; (умолчание 470 — панель на снимках);
    /// --font=&lt;кегль шапки&gt; (10 — как 8 pt на 120 DPI); --dpi=96|120 — значок «не обнаружен»: шрифт ячейки
    /// в точках экрана этого DPI при той же высоте строки XPTable (15 точек на любом DPI);
    /// --shots=&lt;каталог&gt; — снимок носителя
    /// (на диске, не в git). Коды: 0 — сошлось; 1 — расхождение; 2 — ключи.
    /// </summary>
    static class ResultHeaderProbe
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int width = 470;
            float font = 0f;
            int dpi = 96;
            string shots = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--ui=", StringComparison.Ordinal)) Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(arg.Substring(5));
                else if (arg.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(arg.Substring(8), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--font=", StringComparison.Ordinal)) font = float.Parse(arg.Substring(7), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
                else if (arg.StartsWith("--dpi=", StringComparison.Ordinal)) dpi = int.Parse(arg.Substring(6), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + arg); return 2; }
            }

            GlobalConfigManager.GetInstance();
            Application.EnableVisualStyles();
            var mainForm = (MainForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainForm));
            var view = new DCResultView(mainForm);
            object table = Field(typeof(DCResultView), "table1").GetValue(view);
            if (font > 0f)
            {
                table.GetType().GetProperty("HeaderFont").SetValue(table, new Font("Microsoft Sans Serif", font), null);
            }
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, 300)
            };
            view.TopLevel = false;
            view.FormBorderStyle = FormBorderStyle.None;
            view.Dock = DockStyle.Fill;
            host.Controls.Add(view);
            view.Show();
            host.Show();
            Pump(200);

            var res = new System.ComponentModel.ComponentResourceManager(typeof(DCResultView));
            string scale = res.GetString("DCResult_FsaScaleMatrix") ?? "FSA: Bq by response matrix";
            string stale = res.GetString("DCResult_FsaStale") ?? "(recomputing…)";
            // заголовок как у окна: шкала и пометка свежести (`FsaMeasurementResult.Summary.Describe`)
            string head = scale.Contains("{") ? string.Format(CultureInfo.InvariantCulture, scale, "") : scale;
            head = head.Trim();
            string staleHead = head + " " + stale;
            Console.WriteLine("культура {0}, носитель {1} точек, кегль шапки {2}", Thread.CurrentThread.CurrentUICulture.Name, width,
                              font > 0f ? font.ToString(CultureInfo.InvariantCulture) : "как в окне");

            int bad = 0;
            bad += Judge(view, table, staleHead, staleHead, "заголовок с пометкой «пересчёт»", shots, "stale");
            int withMark = HeaderHeight(view);
            bad += Judge(view, table, head, staleHead, "заголовок без пометки", shots, "fresh");
            int without = HeaderHeight(view);
            bool steady = withMark == without;
            Console.WriteLine("  высота шапки с пометкой {0}, без неё {1}  {2}", withMark, without, steady ? "ok — не прыгает" : "НЕТ — прыгает на каждом пересчёте");
            if (!steady) bad++;

            Judge(view, table, staleHead, staleHead, "перед сужением руками — заголовок с пометкой", null, "hand");
            bad += HandSection(view, table, staleHead);
            bad += MarkSection(view, table, dpi);

            host.Hide();
            view.Dispose();
            host.Dispose();
            Console.WriteLine(bad == 0 ? "ИТОГ: ВСЁ СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad);
            return bad == 0 ? 0 : 1;
        }

        static int Judge(DCResultView view, object table, string text, string reserve, string what, string shots, string tag)
        {
            MethodInfo three = typeof(DCResultView).GetMethod("SetFsaHeader", Any, null, new[] { typeof(string), typeof(string), typeof(string) }, null);
            if (three != null)
            {
                three.Invoke(view, new object[] { text, null, reserve });
            }
            else
            {
                // прежний код: одного заголовка
                typeof(DCResultView).GetMethod("SetFsaHeader", Any, null, new[] { typeof(string), typeof(string) }, null)
                                    .Invoke(view, new object[] { text, null });
            }
            Pump(200);
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            int columnWidth = (int)columns[0].GetType().GetProperty("Width").GetValue(columns[0], null);
            Font font = (Font)table.GetType().GetProperty("HeaderFont").GetValue(table, null);
            int need;
            using (Graphics g = ((Control)table).CreateGraphics())
            using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
            {
                SizeF size = g.MeasureString(text, font, columnWidth - 4, format);
                need = (int)Math.Ceiling(size.Height) + 4;
            }
            int height = HeaderHeight(view);
            int lines = (int)Math.Round((need - 4) / (double)font.GetHeight(((Control)table).CreateGraphics()));
            bool ok = height >= need;
            Console.WriteLine("  [{0}] «{1}»: колонка {2} точек, строк текста {3}, тексту нужно {4}, шапка {5}  {6}",
                              what, text, columnWidth, lines, need, height, ok ? "ok" : "НЕТ — срезано");
            if (shots != null)
            {
                Directory.CreateDirectory(shots);
                Form host = view.ParentForm ?? (Form)view.Parent;
                using (var frame = new Bitmap(host.ClientSize.Width, 140))
                {
                    host.DrawToBitmap(frame, new Rectangle(0, 0, frame.Width, frame.Height));
                    string file = Path.Combine(shots, "header_" + Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName + "_" + tag + "_" + host.ClientSize.Width + ".png");
                    frame.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("  снимок: {0}", file);
                }
            }
            return ok ? 0 : 1;
        }

        /// <summary>
        /// (`fixFsa2`, второй заход) Первую колонку сузил ЧЕЛОВЕК (ширина колонки сменилась не правилом):
        /// высота шапки обязана пойти за переносом заголовка. Замер критика `critFsa2` на первой
        /// редакции: ширина 90 и 70 — шапка 32 при нужных 42.
        /// </summary>
        static int HandSection(DCResultView view, object table, string staleHead)
        {
            int bad = 0;
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            PropertyInfo widthProperty = columns[0].GetType().GetProperty("Width");
            Font font = (Font)table.GetType().GetProperty("HeaderFont").GetValue(table, null);
            foreach (int width in new[] { 120, 90, 70 })
            {
                widthProperty.SetValue(columns[0], width, null);
                Pump(150);
                int columnWidth = (int)widthProperty.GetValue(columns[0], null);
                int need;
                using (Graphics g = ((Control)table).CreateGraphics())
                using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                {
                    string shown = (string)columns[0].GetType().GetProperty("Text").GetValue(columns[0], null);
                    need = (int)Math.Ceiling(g.MeasureString(shown, font, columnWidth - 4, format).Height) + 4;
                }
                int height = HeaderHeight(view);
                bool ok = height >= need;
                Console.WriteLine("  [ширина руками {0}] колонка {1} точек, заголовку нужно {2}, шапка {3}  {4}",
                                  width, columnWidth, need, height, ok ? "ok" : "НЕТ — срезано");
                if (!ok) bad++;
            }
            return bad;
        }

        /// <summary>Значок «не обнаружен»: подпись культуры и раскладка внутри ячейки; плюс контроль прежней раскладки.</summary>
        static int MarkSection(DCResultView view, object table, int dpi)
        {
            int bad = 0;
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            object renderer = columns[1].GetType().GetProperty("Renderer").GetValue(columns[1], null);
            PropertyInfo markProperty = renderer.GetType().GetProperty("NotDetectedText");
            string mark = markProperty != null ? (string)markProperty.GetValue(renderer, null) : null;
            string want = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "н/о" : "ND";
            bool markOk = mark == want;
            Console.WriteLine("  подпись «не обнаружен»: «{0}» (ждём «{1}»)  {2}", mark ?? "нет свойства — картинка ND", want, markOk ? "ok" : "НЕТ");
            if (!markOk) bad++;

            // место ячейки так, как его отдаёт XPTable: ячейка = ширина колонки × высота строки,
            // минус рамка 1 точка, минус 1 точка слева (`CellRenderer.ClientRectangle`), минус 2 точки отступа значка
            object tableModel = Field(typeof(DCResultView), "tableModel1").GetValue(view);
            int rowHeight = (int)tableModel.GetType().GetProperty("RowHeight").GetValue(tableModel, null);
            int columnWidth = (int)columns[1].GetType().GetProperty("Width").GetValue(columns[1], null);
            var room = new Rectangle(0, 0, columnWidth - 1 - 1 - 2, rowHeight - 1);
            Font tableFont = ((Control)table).Font;
            // DPI экрана: Graphics пробы — 96 DPI, поэтому шрифт ячейки берётся в кегле, который на 96 DPI
            // даёт столько же точек, сколько исходный на заданном DPI
            var cellFont = new Font(tableFont.FontFamily, tableFont.SizeInPoints * dpi / 96f, tableFont.Style, GraphicsUnit.Point);
            string text = mark ?? want;
            using (Graphics g = ((Control)table).CreateGraphics())
            {
                MethodInfo layout = renderer.GetType().GetMethod("MarkLayout", BindingFlags.Public | BindingFlags.Static);
                MethodInfo inkOf = renderer.GetType().GetMethod("MarkInk", BindingFlags.Public | BindingFlags.Static);
                if (layout == null || inkOf == null || layout.GetParameters().Length != 6)
                {
                    Console.WriteLine("  раскладки значка по чернилам нет (`MarkLayout` с шестью доводами, `MarkInk`) — прежний код  НЕТ");
                    bad++;
                }
                else
                {
                    var call = new object[] { g, cellFont, text, room, null, null };
                    var box = (Rectangle)layout.Invoke(null, call);
                    var markFont = (Font)call[4];
                    var offset = (Point)call[5];
                    var ink = (Rectangle)inkOf.Invoke(null, new object[] { text, markFont });
                    // чернила в координатах рамки
                    int inkTop = offset.Y + ink.Y, inkLeft = offset.X + ink.X;
                    int above = inkTop - 1, below = box.Height - 1 - (inkTop + ink.Height);
                    int left = inkLeft - 1, right = box.Width - 1 - (inkLeft + ink.Width);
                    bool fits = box.Width <= room.Width && box.Height <= room.Height && above >= 0 && below >= 0 && left >= 0 && right >= 0;
                    bool centred = Math.Abs(above - below) <= 1;
                    // строчная «о» (у «ND» — «N») тем же шрифтом: высота чернил одной буквы
                    string letter = text == "ND" ? "N" : "о";
                    var letterInk = (Rectangle)inkOf.Invoke(null, new object[] { letter, markFont });
                    Console.WriteLine("  значок «{0}» на {1} DPI: место {2}×{3}, шрифт ячейки {4:0.##} pt → значка {5:0.##} pt; чернила {6}×{7} (буква «{8}» {9} точек); рамка {10}×{11}; поля внутри рамки: сверху {12}, снизу {13}, слева {14}, справа {15}  {16}",
                                      text, dpi, room.Width, room.Height, cellFont.SizeInPoints, markFont.SizeInPoints,
                                      ink.Width, ink.Height, letter, letterInk.Height, box.Width, box.Height, above, below, left, right,
                                      fits && centred ? "ok — целиком, по центру" : (fits ? "НЕТ — не по центру" : "НЕТ — срезан"));
                    if (!fits || !centred) bad++;
                    markFont.Dispose();
                }

                // положительный контроль: прежняя раскладка (первая редакция 07.11.2026, её видела Amber):
                // шрифт ячейки 8 pt, рамка высотой «место − 2»
                using (var oldFont = new Font(tableFont.FontFamily, 8f * dpi / 96f))
                {
                    Size oldText = TextRenderer.MeasureText(g, text, oldFont, Size.Empty, TextFormatFlags.NoPadding);
                    int oldBox = room.Height - 2;
                    bool oldClipped = oldText.Height > oldBox;
                    Console.WriteLine("  КОНТРОЛЬ прежней раскладки: текст 8 pt высотой {0} в рамке {1} — {2}",
                                      oldText.Height, oldBox, oldClipped ? "срезан (сторож видит)" : "НЕ срезан — контроль не сработал");
                    if (!oldClipped) bad++;
                }
            }
            cellFont.Dispose();
            return bad;
        }

        static int HeaderHeight(DCResultView view)
        {
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            return (int)model.GetType().GetProperty("HeaderHeight").GetValue(model, null);
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
    }
}
