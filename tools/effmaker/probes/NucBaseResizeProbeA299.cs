using BecquerelMonitor;
using BecquerelMonitor.NucBase;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER299`, 11.11.2026) Окно «База изотопов» растягивается (решение Amber 07.11.2026
    /// вопросником, дословно: «Растягиваемое (Рекомендую)»), а английская единица минут —
    /// «min», а не «m» (читалось как метры). Обе культуры интерфейса, `en-US` и `ru-RU`.
    ///
    /// Без ключа — БЕЗ ПОКАЗА ОКОН (форма строится, размер ставится свойством, раскладка идёт
    /// и без описателя окна):
    ///   А. Свойства окна: разворачивается, верхнего предела нет, рамка тянется, самоподгонки
    ///      нет, наименьший размер — прежний 1004×560, и окно открывается им.
    ///   Б. Рост до 1400×900: таблица поиска, карточка, родители и дочерние растут; зазоры
    ///      между соседями и поля до краёв панели — прежние; никто ни на кого не налезает.
    ///   В. Сжатие обратно до наименьшего — все места побитово исходные; меньше наименьшего —
    ///      окно не сжимается.
    ///   А–В идут ДВАЖДЫ: конструктором без аргументов и конструктором <c>NucBase(Form)</c>,
    ///   которым окно строит приложение (<c>MainForm.ShowNucBaseView</c>); владелец — пустая
    ///   форма, не показанная (замечание критика `crit299`, 11.11.2026).
    ///   Г. Колонки таблицы поиска: не уже слова заголовка и своих ячеек, растут с таблицей;
    ///      ширина, заданная руками, переживает рост и сжатие.
    ///   Д. Единица минут: карточка «60.55 min» / «60.55 мин», колонка «60.55(min)» /
    ///      «60.55(мин)»; ячейка хранит код базы «60.55(m)», её разбор прежний; пункт списка
    ///      единиц отбора по месту 4 = подпись единицы, код по месту — «m», 2 мин = 120 с.
    ///   Е. Положительные контроли: окно с <c>MaximizeBox = false</c> и <c>MaximumSize</c>
    ///      = наименьшему ловится проверками А и Б; старая английская подпись «60.55 m»
    ///      ловится проверкой Д; самоподгонка (<c>AutoSize</c>, <c>GrowOnly</c>) не даёт окну
    ///      сжаться обратно — замер довода, по которому она снята.
    ///
    /// <c>--shown [--shots=каталог]</c> — ТОЛЬКО НА СКРЫТОМ СТОЛЕ
    /// (<c>HiddenRun.exe 300 NucBaseResizeProbeA299.exe --shown --shots=…</c>; на столе «Default»
    /// отказ кодом 3, окна не открываются): окно показывается, ищется 717–737 кэВ, выбирается
    /// строка Bi-212; кадр <c>PrintWindow</c> при наименьшем размере и при 1400×900 в обеих
    /// культурах, затем разворот, возврат и сжатие показанного окна до наименьшего.
    ///
    /// Новые члены приложения (<c>StretchLayout</c>) берутся отражением, чтобы та же проба
    /// собиралась и против сборки до правки — там она обязана краснеть. Коды: 0 — сошлось; 1 — нет.
    /// </summary>
    static class NucBaseResizeProbeA299
    {
        static int bad;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        static readonly Size Min = new Size(1004, 560);
        static readonly Size Big = new Size(1400, 900);

        static readonly string[] PanelParts =
        {
            "ResultDataGridView", "buttonImportDef", "checkBoxOverwriteDef", "checkBoxAppendRootName",
            "labelNameFormat", "comboBoxNameFormat", "groupBox2", "SearchStatusLabel", "label6",
            "ParentsDataGridView", "label7", "DaughtersDataGridView",
        };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool shown = Array.IndexOf(args, "--shown") >= 0;
            string shots = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring("--shots=".Length);
            }

            if (shown)
            {
                return ShownArm(shots);
            }

            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                SetCulture(lang);
                Console.WriteLine();
                Console.WriteLine("======== {0} ========", lang);
                Window(lang, "NucBase()", () => new NucBase.NucBase());
                // (критик `crit299`, 11.11.2026) приложение строит окно ДРУГИМ конструктором —
                // `MainForm.ShowNucBaseView` зовёт `NucBase(this)`; владелец здесь — пустая форма,
                // не показанная, без `MainForm`. Без этого плеча пропажа `InitStretch` из
                // `NucBase(Form)` прошла бы незамеченной.
                Window(lang, "NucBase(Form)", () => new NucBase.NucBase(Owner));
                Columns(lang);
                Units(lang);
            }

            SetCulture("en-US");
            Console.WriteLine();
            Console.WriteLine("======== Е. положительные контроли ========");
            Controls();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(Inv));
            return bad == 0 ? 0 : 1;
        }

        static void SetCulture(string lang)
        {
            var culture = new CultureInfo(lang);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        static void Check(string what, bool ok, string detail)
        {
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: {2}", ok ? "ок" : "!!", what, detail);
        }

        static T F<T>(object form, string name)
        {
            return (T)typeof(NucBase.NucBase).GetField(name, Any).GetValue(form);
        }

        static string R(Rectangle r)
        {
            return string.Format(Inv, "({0},{1} {2}×{3})", r.X, r.Y, r.Width, r.Height);
        }

        static string S(Size s)
        {
            return s.Width.ToString(Inv) + "×" + s.Height.ToString(Inv);
        }

        // ---------------------------------------------------------------- А–В
        /// <summary>Свойства окна А; true — всё сошлось (для контроля Е считается отдельно).</summary>
        static bool Props(Form form, bool count)
        {
            var fails = new List<string>();
            if (!form.MaximizeBox) fails.Add("MaximizeBox = false");
            if (form.MaximumSize != Size.Empty) fails.Add("MaximumSize = " + S(form.MaximumSize));
            if (form.FormBorderStyle != FormBorderStyle.Sizable) fails.Add("рамка " + form.FormBorderStyle);
            if (form.AutoSize) fails.Add("AutoSize = true");
            if (form.MinimumSize != Min) fails.Add("MinimumSize = " + S(form.MinimumSize));
            if (form.Size != Min) fails.Add("открывается " + S(form.Size));
            if (count)
            {
                Check("А. окно тянется и разворачивается, наименьший 1004×560", fails.Count == 0,
                      fails.Count == 0 ? "MaximizeBox, без MaximumSize, Sizable, без AutoSize, Min = Size = " + S(form.Size) : string.Join("; ", fails));
            }

            return fails.Count == 0;
        }

        static Dictionary<string, Rectangle> Snap(Form form)
        {
            var d = new Dictionary<string, Rectangle>();
            foreach (string n in PanelParts) d[n] = F<Control>(form, n).Bounds;
            d["panel1"] = F<Control>(form, "panel1").Bounds;
            d["groupBox1"] = F<Control>(form, "groupBox1").Bounds;
            return d;
        }

        /// <summary>Рост Б; возвращает число нарушений (контроль Е зовёт без счёта).</summary>
        static int Grow(Form form, Dictionary<string, Rectangle> b, bool count)
        {
            form.Size = Big;
            Dictionary<string, Rectangle> g = Snap(form);
            Size panel = F<Control>(form, "panel1").ClientSize;
            int dw = form.Size.Width - Min.Width, dh = form.Size.Height - Min.Height;
            var fails = new List<string>();
            Action<bool, string> need = (ok, text) => { if (!ok) fails.Add(text); };

            need(form.Size == Big, "окно " + S(form.Size) + " вместо " + S(Big));
            need(g["panel1"].Width == b["panel1"].Width + dw && g["panel1"].Height == b["panel1"].Height + dh, "панель " + R(g["panel1"]));
            need(g["groupBox1"].Width == b["groupBox1"].Width + dw, "рамка отбора " + R(g["groupBox1"]));
            foreach (string n in new[] { "ResultDataGridView", "ParentsDataGridView", "DaughtersDataGridView" })
            {
                need(g[n].Width > b[n].Width && g[n].Height == b[n].Height + dh, n + " " + R(b[n]) + " → " + R(g[n]));
            }

            need(g["groupBox2"].Width > b["groupBox2"].Width, "карточка " + R(b["groupBox2"]) + " → " + R(g["groupBox2"]));
            // зазоры между соседями — прежние
            Func<Dictionary<string, Rectangle>, string, string, int> gapX = (d, l, r) => d[r].Left - d[l].Right;
            Func<Dictionary<string, Rectangle>, string, string, int> gapY = (d, t, u) => d[u].Top - d[t].Bottom;
            foreach (var pair in new[]
            {
                new[] { "ResultDataGridView", "groupBox2" }, new[] { "ResultDataGridView", "ParentsDataGridView" },
                new[] { "ParentsDataGridView", "DaughtersDataGridView" },
            })
            {
                need(gapX(g, pair[0], pair[1]) == gapX(b, pair[0], pair[1]), "зазор " + pair[0] + "→" + pair[1] + " " + gapX(b, pair[0], pair[1]) + " → " + gapX(g, pair[0], pair[1]));
            }

            foreach (var pair in new[]
            {
                new[] { "ResultDataGridView", "buttonImportDef" }, new[] { "groupBox2", "label6" },
                new[] { "ParentsDataGridView", "SearchStatusLabel" },
            })
            {
                need(gapY(g, pair[0], pair[1]) == gapY(b, pair[0], pair[1]), "зазор " + pair[0] + "↓" + pair[1] + " " + gapY(b, pair[0], pair[1]) + " → " + gapY(g, pair[0], pair[1]));
            }

            // поля до правого и нижнего краёв панели — прежние
            Size p0 = new Size(b["panel1"].Width, b["panel1"].Height);
            foreach (string n in new[] { "groupBox2", "DaughtersDataGridView", "SearchStatusLabel" })
            {
                need(panel.Width - g[n].Right == p0.Width - b[n].Right, "поле справа у " + n + " " + (p0.Width - b[n].Right) + " → " + (panel.Width - g[n].Right));
            }

            foreach (string n in new[] { "checkBoxAppendRootName", "comboBoxNameFormat", "SearchStatusLabel" })
            {
                need(panel.Height - g[n].Bottom == p0.Height - b[n].Bottom, "поле снизу у " + n + " " + (p0.Height - b[n].Bottom) + " → " + (panel.Height - g[n].Bottom));
            }

            // правые края формата имени и таблицы — вместе, как в дизайнере
            need(g["ResultDataGridView"].Right - g["comboBoxNameFormat"].Right == b["ResultDataGridView"].Right - b["comboBoxNameFormat"].Right,
                 "формат имени отстал от края таблицы");
            // никто ни на кого не налезает (сверх того, что было в дизайнере)
            for (int i = 0; i < PanelParts.Length; i++)
            {
                for (int j = i + 1; j < PanelParts.Length; j++)
                {
                    string a = PanelParts[i], c = PanelParts[j];
                    if (g[a].IntersectsWith(g[c]) && !b[a].IntersectsWith(b[c]))
                    {
                        fails.Add("налезают " + a + " " + R(g[a]) + " и " + c + " " + R(g[c]));
                    }
                }
            }

            if (count)
            {
                Check("Б. рост до " + S(Big) + ": таблица, карточка, родители и дочерние растут, зазоры и поля прежние", fails.Count == 0,
                      fails.Count == 0
                          ? "таблица " + R(g["ResultDataGridView"]) + ", карточка " + R(g["groupBox2"]) + ", родители " + R(g["ParentsDataGridView"])
                            + ", дочерние " + R(g["DaughtersDataGridView"]) + ", строка состояния " + R(g["SearchStatusLabel"])
                          : string.Join("; ", fails));
            }

            return fails.Count;
        }

        /// <summary>Владелец для конструктора <c>NucBase(Form)</c>: пустая форма, не показывается.</summary>
        static readonly Form Owner = new Form();

        static void Window(string lang, string ctor, Func<NucBase.NucBase> make)
        {
            Console.WriteLine("--- А–В. окно, конструктор " + ctor);
            using (var form = make())
            {
                Props(form, true);
                Dictionary<string, Rectangle> b = Snap(form);
                Console.WriteLine("    исходно: таблица {0}, карточка {1}, родители {2}, дочерние {3}, панель {4}",
                                  R(b["ResultDataGridView"]), R(b["groupBox2"]), R(b["ParentsDataGridView"]), R(b["DaughtersDataGridView"]), R(b["panel1"]));
                Check("исходные места — дизайнера (таблица 8,0 434×378; карточка 448,3 516×76; панель 976×458)",
                      b["ResultDataGridView"] == new Rectangle(8, 0, 434, 378) && b["groupBox2"] == new Rectangle(448, 3, 516, 76)
                      && b["panel1"].Size == new Size(976, 458), R(b["ResultDataGridView"]) + " " + R(b["groupBox2"]) + " " + R(b["panel1"]));

                Grow(form, b, true);

                form.Size = Min;
                Dictionary<string, Rectangle> back = Snap(form);
                var moved = new List<string>();
                foreach (var kv in b)
                {
                    if (back[kv.Key] != kv.Value) moved.Add(kv.Key + " " + R(kv.Value) + " → " + R(back[kv.Key]));
                }

                Check("В. сжатие до наименьшего — все места исходные", moved.Count == 0, moved.Count == 0 ? "14 мест совпали" : string.Join("; ", moved));
                form.Size = new Size(800, 400);
                Check("В. меньше наименьшего окно не сжимается", form.Size == Min, S(form.Size));
            }
        }

        // ---------------------------------------------------------------- Г
        static DecayRad Line(string name, string line, double energy, double intensity, double hl, string unit)
        {
            return new DecayRad
            {
                Name = name, DecayLine = line, Energy = energy, Intensity = intensity,
                HalfLife = hl, HalfLifeUnit = unit, XrayType = "",
            };
        }

        static void Fill(Form form)
        {
            MethodInfo addRow = typeof(NucBase.NucBase).GetMethod("AddRow", Any);
            addRow.Invoke(form, new object[] { Line("212BI", "G", 727.33, 6.67, 60.55, "m") });
            addRow.Invoke(form, new object[] { Line("232NP", "X", 111.238, 14, 14.7, "m") });
            addRow.Invoke(form, new object[] { Line("212BI", "X", 12.148, 6.99877963, 60.55, "m") });
            addRow.Invoke(form, new object[] { Line("130TE", "G", 1234.5678, 5.8086e-6, 7.9e20, "Y") });
        }

        static int Area(DataGridView grid)
        {
            int area = grid.ClientSize.Width - 2;
            foreach (Control part in grid.Controls)
            {
                if (part is VScrollBar && part.Visible) area -= part.Width;
            }

            return area;
        }

        static string Widths(DataGridView grid)
        {
            var w = new List<string>();
            foreach (DataGridViewColumn c in grid.Columns) w.Add(c.Width.ToString(Inv));
            return string.Join(" ", w);
        }

        static void Columns(string lang)
        {
            Console.WriteLine("--- Г. колонки таблицы поиска");
            using (var form = new NucBase.NucBase())
            {
                var grid = F<DataGridView>(form, "ResultDataGridView");
                Fill(form);
                Func<string, List<string>> narrow = stage =>
                {
                    var list = new List<string>();
                    foreach (DataGridViewColumn c in grid.Columns)
                    {
                        if (c.Resizable == DataGridViewTriState.False) continue;
                        int header = (int)typeof(NucBase.NucBase).GetMethod("HeaderWordWidth", Any).Invoke(null, new object[] { c, grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font });
                        int cells = c.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCellsExceptHeader, true);
                        if (c.Width < header || c.Width < cells) list.Add(stage + ": «" + c.HeaderText + "» " + c.Width + " < " + Math.Max(header, cells));
                    }

                    return list;
                };
                Func<int> sum = () => { int s = 0; foreach (DataGridViewColumn c in grid.Columns) s += c.Width; return s; };

                var bad0 = narrow("наименьший");
                int sum0 = sum(), area0 = Area(grid);
                string w0 = Widths(grid);
                form.Size = Big;
                var bad1 = narrow("1400×900");
                int sum1 = sum(), area1 = Area(grid);
                string w1 = Widths(grid);
                bad0.AddRange(bad1);
                Check("колонки не уже слова заголовка и своих ячеек (оба размера)", bad0.Count == 0, bad0.Count == 0 ? "наим. " + w0 + "; 1400×900 " + w1 : string.Join("; ", bad0));
                Check("колонки растут с таблицей и занимают её", sum1 > sum0 && sum1 <= area1 && sum1 >= area1 - 2 && grid.ScrollBars == ScrollBars.Both,
                      string.Format(Inv, "сумма {0}/{1} → {2}/{3}, прокрутка {4}", sum0, area0, sum1, area1, grid.ScrollBars));

                // ширина руками: «Энергия» — 150, затем сжатие и снова рост
                DataGridViewColumn energy = grid.Columns[3];
                energy.Width = 150;
                form.Size = Min;
                int atMin = energy.Width;
                string wMin = Widths(grid);
                form.Size = Big;
                int atBig = energy.Width;
                Check("ширина, заданная руками, переживает сжатие и рост", atMin == 150 && atBig == 150,
                      string.Format(Inv, "наим. {0} ({1}), 1400×900 {2} ({3})", atMin, wMin, atBig, Widths(grid)));
            }
        }

        // ---------------------------------------------------------------- Д
        static bool CardOk(string lang, string text)
        {
            return text == (lang == "ru-RU" ? "60.55 мин" : "60.55 min");
        }

        static void Units(string lang)
        {
            Console.WriteLine("--- Д. единица минут");
            string min = NucBaseFramework.HalfLifeUnitCaption("m");
            Check("подпись кода «m»", min == (lang == "ru-RU" ? "мин" : "min"), min);
            using (var form = new NucBase.NucBase())
            {
                Type t = typeof(NucBase.NucBase);
                t.GetMethod("ShowIsotopeCard", Any).Invoke(form, new object[]
                {
                    "212BI", new Nuclide { Z = 83, N = 129, HalfLife = "60.55", HalfLifeUOM = "m", HalfLife_Sec = 3633.0 },
                });
                string card = F<Label>(form, "IsotopeHLLabel").Text;
                Check("карточка Bi-212", CardOk(lang, card), card);

                Fill(form);
                var grid = F<DataGridView>(form, "ResultDataGridView");
                string cell = (string)grid.Rows[0].Cells[7].Value;
                string shownCell = Convert.ToString(grid.Rows[0].Cells[7].FormattedValue, Inv);
                Check("колонка периода", shownCell == (lang == "ru-RU" ? "60.55(мин)" : "60.55(min)"), shownCell);
                Check("ячейка хранит код базы", cell == "60.55(m)", cell);
                double years = NucBase.NucBase.HalfLifeYearsFromCell(cell);
                Check("разбор ячейки прежний", Math.Abs(years - 60.55 * 60 / 31536000) < 1e-15, years.ToString("R", Inv));

                var combo = F<ComboBox>(form, "HalfLifeUOMComboBox");
                var codes = (string[])t.GetField("FilterUnits", Any).GetValue(null);
                var shownItems = new List<string>();
                bool same = combo.Items.Count == codes.Length;
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    shownItems.Add((string)combo.Items[i]);
                    same &= i < codes.Length && (string)combo.Items[i] == NucBaseFramework.HalfLifeUnitCaption(codes[i]);
                }

                Check("пункты единиц отбора = подписи кодов FilterUnits по месту", same, string.Join(" ", shownItems));
                MethodInfo toSec = t.GetMethod("ConvertHalfLifeToSeconds", Any);
                double sec = (double)toSec.Invoke(null, new object[] { 2.0, codes[4] });
                Check("пункт 4 — код «m», 2 мин = 120 с", codes[4] == "m" && sec == 120.0 && (string)combo.Items[4] == min,
                      string.Format(Inv, "код «{0}», пункт «{1}», {2} с", codes[4], combo.Items[4], sec));
            }
        }

        // ---------------------------------------------------------------- Е
        static void Controls()
        {
            using (var form = new NucBase.NucBase())
            {
                Dictionary<string, Rectangle> b = Snap(form);
                form.MaximizeBox = false;
                form.MaximumSize = Min;
                bool props = Props(form, false);
                int grow = Grow(form, b, false);
                Check("контроль: неизменное окно (MaximizeBox = false, MaximumSize = наименьшему) ловится А и Б", !props && grow > 0,
                      string.Format(Inv, "А {0}, Б нарушений {1}", props ? "прошла — ПЛОХО" : "краснеет", grow));
            }

            Check("контроль: старая английская подпись «60.55 m» ловится Д", !CardOk("en-US", "60.55 m"), "проверка отвергает «60.55 m»");

            using (var form = new NucBase.NucBase())
            {
                form.AutoSize = true;
                form.AutoSizeMode = AutoSizeMode.GrowOnly;
                form.Size = Big;
                form.Size = Min;
                Size stuck = form.Size;
                Check("контроль: с самоподгонкой GrowOnly окно не сжимается обратно (довод снятия AutoSize)", stuck != Min,
                      "после 1400×900 → наименьший: " + S(stuck));
            }
        }

        // ---------------------------------------------------------------- показ
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        static void Pump(int ms)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }

        static void Shot(Form form, string dir, string name)
        {
            if (dir == null) return;
            IntPtr h = form.Handle;
            RECT wr, fr;
            GetWindowRect(h, out wr);
            if (DwmGetWindowAttribute(h, 9, out fr, Marshal.SizeOf(typeof(RECT))) != 0) fr = wr;
            Rectangle frame = Rectangle.FromLTRB(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - wr.Left, fr.Bottom - wr.Top);
            using (var whole = new Bitmap(wr.Right - wr.Left, wr.Bottom - wr.Top, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(whole))
                {
                    g.Clear(Color.White);
                    IntPtr hdc = g.GetHdc();
                    bool ok = PrintWindow(h, hdc, 2);
                    g.ReleaseHdc(hdc);
                    if (!ok) throw new InvalidOperationException("PrintWindow отказал");
                }

                using (Bitmap shot = whole.Clone(frame, PixelFormat.Format24bppRgb))
                {
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, name + ".png");
                    shot.Save(path, ImageFormat.Png);
                    Console.WriteLine("    кадр {0}: {1}×{2}", path, shot.Width, shot.Height);
                }
            }
        }

        static int ShownArm(string shots)
        {
            var name = new StringBuilder(256);
            int needed;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out needed);
            Console.WriteLine("=== показ; стол «{0}» ===", name);
            if (string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("ОТКАЗ: плечо открывает окна и идёт только на скрытом столе (HiddenRun.exe)");
                return 3;
            }

            Application.EnableVisualStyles();
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                SetCulture(lang);
                Console.WriteLine("======== {0} ========", lang);
                using (var form = new NucBase.NucBase { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
                {
                    form.Show();
                    Pump(500);
                    form.CallSearch(727m);
                    Pump(800);
                    var grid = F<DataGridView>(form, "ResultDataGridView");
                    int bi = -1;
                    for (int i = 0; i < grid.Rows.Count; i++)
                    {
                        string n = Convert.ToString(grid.Rows[i].Cells[1].Value, Inv);
                        string e = Convert.ToString(grid.Rows[i].Cells[3].Value, Inv);
                        if (n.IndexOf("212", StringComparison.Ordinal) >= 0 && n.IndexOf("BI", StringComparison.OrdinalIgnoreCase) >= 0 && e.StartsWith("727", StringComparison.Ordinal))
                        {
                            bi = i;
                            break;
                        }
                    }

                    Check("поиск 717–737 кэВ дал строки, среди них Bi-212 727 кэВ", grid.Rows.Count > 0 && bi >= 0, "строк " + grid.Rows.Count + ", Bi-212 — строка " + bi);
                    if (bi >= 0)
                    {
                        grid.CurrentCell = grid.Rows[bi].Cells[1];
                        Pump(500);
                    }

                    string card = F<Label>(form, "IsotopeHLLabel").Text;
                    Check("карточка Bi-212 на показанном окне", CardOk(lang, card), card);
                    Check("показанное окно открылось наименьшим", form.Size == Min, S(form.Size));
                    Shot(form, shots, "nucbase_" + lang + "_min");

                    form.Size = Big;
                    Pump(800);
                    Check("показанное окно тянется до 1400×900", form.Size == Big, S(form.Size) + ", таблица " + R(grid.Bounds) + ", колонки " + Widths(grid));
                    bool hScroll = false;
                    foreach (Control part in grid.Controls) if (part is HScrollBar && part.Visible) hScroll = true;
                    Console.WriteLine("    прокрутка вбок при 1400×900: {0}", hScroll ? "есть" : "нет");
                    Shot(form, shots, "nucbase_" + lang + "_1400x900");

                    form.WindowState = FormWindowState.Maximized;
                    Pump(800);
                    Size max = form.Size;
                    Check("разворачивается", form.WindowState == FormWindowState.Maximized && max.Width > Big.Width - 1 && max.Height > Min.Height,
                          form.WindowState + " " + S(max) + ", таблица " + R(grid.Bounds));
                    form.WindowState = FormWindowState.Normal;
                    Pump(500);
                    form.Size = Min;
                    Pump(500);
                    Check("показанное окно сжимается обратно до наименьшего", form.Size == Min && grid.Bounds == new Rectangle(8, 0, 434, 378),
                          S(form.Size) + ", таблица " + R(grid.Bounds));
                    form.Close();
                }
            }

            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(Inv));
            return bad == 0 ? 0 : 1;
        }
    }
}
