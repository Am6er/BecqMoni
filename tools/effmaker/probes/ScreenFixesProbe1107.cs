using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ScreenFixesProbe1107
{
    /// <summary>
    /// ПРИЁМКА ПОЛОСЫ `fixUi2` (07.11.2026) — дефекты окон, найденные проверкой настоящим экраном.
    ///
    ///     screenfixesprobe1107 --src=&lt;каталог BecquerelMonitor исходников&gt; --work=&lt;свой каталог&gt;
    ///
    /// ⛔ Формы ПОКАЗЫВАЮТСЯ (подсказка, колонки, сворачивание): запускать только на скрытом столе
    /// (`HiddenRun.exe`), не на экране человека.
    ///
    /// (А, `AMBER294`) Строки опор подсказки «Опорных пиков для шкалы»: каждый код отвода опоры,
    ///     какой ставит код (`Refusal = "…"` в `FullSpectrumAnalysis/*.cs` и константы
    ///     `FsaScaleAnchor.Refusal*`, читаются из исходников), печатается словами ресурса; опора без
    ///     измеренного центра (остаток NaN) — без «NaN»; принятая опора со сдвигом по свету — без
    ///     пробела перед запятой. Обе культуры.
    /// (Б) Значение строки «пик не объяснён»: без слова «сет»/«set» (состав, а не набор).
    /// (В, `AMBER299`) Подсказка поля «Изотоп» окна «База изотопов» — настоящее окно подсказки:
    ///     строки не длиннее `AppUi.TipWidth`, склейка = ресурс, поле не закрыто.
    /// (Г) «Период полураспада» таблицы «База изотопов» не обрезан: колонка целиком в видимой
    ///     области и не уже своего заголовка — пустая таблица и с результатом поиска; обе культуры.
    /// (Д, `AMBER310`) Вопрос сброса библиотеки к поставочной: путь копии переносится только
    ///     после разделителя каталогов, строки пути не длиннее `ResetPathWidth`; ответ «Нет» не
    ///     меняет файл (sha256) и не заводит копию.
    /// (Е) Свёрнутое окно «Редактировать изотоп»: `Show()` + `Activate()` (так зовёт его
    ///     `MainForm.ShowNuclideDefinitionForm`) разворачивает его; хозяин окна сохраняется.
    /// (Ж) Окно прибора называет подстановку кристалла из геометрии (`CrystalOfferNote`): есть при
    ///     подстановке, нет без неё, нет после смены вещества в списке; кристалл, выбранный
    ///     человеком, назван выбором (`crystalChosenNote`), а не подстановкой окна.
    /// (К) Свёрнутая «База изотопов»: `BringToFront()` (так её поднимает `MainForm.ShowNucBaseView`)
    ///     разворачивает её; без вызова свёрнутое остаётся свёрнутым.
    ///
    /// (Л) Строка состояния отчёта FSA при отказе: обрыв многоточием, полный текст подсказкой.
    /// (М) Единый вид числа «Базы изотопов» (решение Amber 07.11.2026 «Единый вид «7.04e-8»»):
    ///     таблица (4 значащие), предел, подсказка (все знаки), карточка нуклида.
    ///
    /// Третий заход (критик `critUi2`): в Г — ширина колонки меняется на поиск не больше раза;
    /// колонки не уже слова заголовка и самой длинной ячейки (кроме заданных человеком); видная
    /// не целиком — только при прокрутке вбок; 11729 строк на 0…3000 кэВ; размер окна печатается
    /// (оно неизменного размера).
    ///
    /// Второй заход (замечания критика `critUi2`): в А — разбор кодов видит литерал в тернарии
    /// (контроль — подсаженный `a.Refusal = c ? "far" : null;`); в В — подсказка под полем; в Г —
    /// ячейкам хватает, когда места хватает всем, и ручная ширина колонки держится.
    ///
    /// Положительный контроль — та же проба на сборке HEAD 2d8cb9b3 (без правок полосы): А, Б, В,
    /// Г, Д, Е, Ж красные. Ожидание: «ВСЕ СОШЛИСЬ», код 0; расхождение — 1; нечем мерить — 2.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static int bad;
        static int unmeasured;
        static string shotDir;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string src = null, work = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--src=", StringComparison.Ordinal)) src = a.Substring(6);
                else if (a.StartsWith("--work=", StringComparison.Ordinal)) work = a.Substring(7);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (src == null || !Directory.Exists(Path.Combine(src, "FullSpectrumAnalysis")) || work == null)
            {
                Console.Error.WriteLine("нужны --src=<каталог BecquerelMonitor> и --work=<свой каталог>");
                return 2;
            }

            Console.WriteLine("сборка приложения: {0}", typeof(FSAReportView).Assembly.Location);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            Step("А", () => AnchorTip(src));
            Step("Б", Uncovered);
            Step("В", IsotopeTip);
            Step("Г", HalfLifeColumn);
            shotDir = work;
            Step("Д", () => ResetQuestion(work));
            Step("Е", MinimizedEditor);
            Step("Ж", CrystalNote);
            Step("К", MinimizedNucBase);
            Step("Л", StatusLine);
            Step("М", NumberFormat);

            Console.WriteLine();
            if (bad == 0 && unmeasured == 0) Console.WriteLine("ВСЕ СОШЛИСЬ");
            else Console.WriteLine("НЕ СОШЛОСЬ: {0}, НЕ ИЗМЕРЕНО: {1}", bad, unmeasured);
            return bad > 0 ? 1 : unmeasured > 0 ? 2 : 0;
        }

        static void Step(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Exception e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Console.WriteLine("  НЕТ ({0}) исключение: {1}: {2}", name, e.GetType().Name, e.Message);
                bad++;
            }
        }

        static void Ok(bool condition, string what)
        {
            Console.WriteLine("  {0} {1}", condition ? "ok " : "НЕТ", what);
            if (!condition) bad++;
        }

        static void Unmeasured(string what)
        {
            Console.WriteLine("  НЕ ИЗМЕРЕНО: {0}", what);
            unmeasured++;
        }

        static void Ui(string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
        }

        static void Pump()
        {
            for (int i = 0; i < 20; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        // ------------------------------------------------------------------
        //  (А) строки опор
        // ------------------------------------------------------------------

        static readonly Dictionary<string, string> RefusalKeys = new Dictionary<string, string>
        {
            { "edge", "FSAReport_AnchorEdgeRefusal" },
            { "skip", "FSAReport_AnchorSkipRefusal" },
            { "narrow", "FSAReport_AnchorNarrowRefusal" },
            { "z", "FSAReport_AnchorZRefusal" },
            { "share", "FSAReport_AnchorShareRefusal" },
            { "shift", "FSAReport_AnchorShiftRefusal" },
            { "outlier", "FSAReport_AnchorOutlierRefusal" },
            { "brake", "FSAReport_AnchorBrakeRefusal" },
            { "not_in_step", "FSAReport_AnchorNotInStepRefusal" },
        };

        static string Own(string key, string culture)
        {
            return new ComponentResourceManager(typeof(FSAReportView)).GetString(key, new CultureInfo(culture));
        }

        static void AnchorTip(string src)
        {
            Console.WriteLine("=== (А) строки опор подсказки шкалы ===");
            // (критик `critUi2`) Положительный контроль разбора: код в тернарии и в выражении
            // присваивания должен находиться так же, как голый литерал. Прежний разбор брал только
            // `Refusal = "код"`, и подсаженный `a.Refusal = c ? "far" : null;` проходил зелёным.
            var planted = RefusalCodes("a.Refusal = c ? \"far\" : null;\n b.Refusal = x ?? \"near\";\n if (a.Refusal == \"cmp\") {}");
            Console.WriteLine("  контроль разбора: {0}", string.Join(", ", planted));
            Ok(planted.Contains("far") && planted.Contains("near"), "разбор видит код в тернарии и в выражении присваивания");
            Ok(!planted.Contains("cmp"), "разбор не берёт литерал сравнения (==) за код");

            var codes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(Path.Combine(src, "FullSpectrumAnalysis"), "*.cs"))
            {
                codes.UnionWith(RefusalCodes(File.ReadAllText(file)));
            }
            Console.WriteLine("  коды в исходниках: {0}", string.Join(", ", codes));
            Ok(codes.Count >= 9, "коды отбора опор найдены в исходниках (не меньше девяти)");
            foreach (string code in codes)
            {
                Ok(RefusalKeys.ContainsKey(code), "код «" + code + "» знаком пробе (иначе — новый код без слов)");
            }

            MethodInfo append = typeof(FSAReportView).GetMethod("AppendAnchorLines", Any);
            if (append == null)
            {
                Unmeasured("нет FSAReportView.AppendAnchorLines");
                return;
            }

            foreach (string culture in new[] { "en", "ru" })
            {
                Ui(culture);
                string tipRefusal = Own("FSAReport_AnchorTipRefusal", culture);
                foreach (KeyValuePair<string, string> pair in RefusalKeys)
                {
                    var anchor = new FsaScaleAnchor
                    {
                        Component = "Cs-137", LineKev = 661.66, ModelKev = 661.4, ShiftKev = 0.31,
                        PeakShare = 0.42, Used = false, Refusal = pair.Key
                    };
                    string line = Tip(append, anchor);
                    string words = Own(pair.Value, culture);
                    bool tail = words != null && tipRefusal != null
                        && line.EndsWith(string.Format(CultureInfo.InvariantCulture, tipRefusal, words), StringComparison.Ordinal);
                    Console.WriteLine("    {0} {1,-12} «{2}»", culture, pair.Key, line);
                    Ok(tail, culture + " " + pair.Key + ": строка кончается словами ресурса " + pair.Value);
                }

                var nan = new FsaScaleAnchor
                {
                    Component = "Cs-137", LineKev = 44.6, ModelKev = 44.6, ShiftKev = double.NaN,
                    SigmaKev = double.NaN, MeasuredKev = double.NaN, PeakShare = 0.68, Used = false, Refusal = "z"
                };
                string nanLine = Tip(append, nan);
                Console.WriteLine("    {0} без центра «{1}»", culture, nanLine);
                Ok(nanLine.IndexOf("NaN", StringComparison.Ordinal) < 0, culture + ": нет «NaN» у опоры без центра");
                Ok(nanLine.Contains("44.6") && nanLine.Contains("0.68"), culture + ": центр модели и доля на месте");

                var used = new FsaScaleAnchor
                {
                    Component = "K-40", LineKev = 1460.82, ModelKev = 1443.9, ShiftKev = 0.33, PeakShare = 0.95,
                    Used = true, LightShiftKev = -38.24
                };
                string usedLine = Tip(append, used);
                Console.WriteLine("    {0} принятая «{1}»", culture, usedLine);
                Ok(usedLine.IndexOf(" ,", StringComparison.Ordinal) < 0, culture + ": нет пробела перед запятой");
                Ok(usedLine.StartsWith("+ K-40 1443.9 ", StringComparison.Ordinal) && usedLine.Contains("-38.24"),
                   culture + ": знак, компонент, центр и свет на месте");
            }
        }

        /// <summary>
        /// Коды отвода опоры в тексте исходника: ВСЕ строковые литералы правой части каждого
        /// присваивания `Refusal = …;` (не сравнения `==`) и значения констант `const string
        /// Refusal… = "…"`.
        /// </summary>
        static SortedSet<string> RefusalCodes(string text)
        {
            var codes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(text, @"\bRefusal\s*=(?!=)([^;]*);"))
            {
                foreach (Match s in Regex.Matches(m.Groups[1].Value, @"""([A-Za-z_]+)"""))
                {
                    codes.Add(s.Groups[1].Value);
                }
            }
            foreach (Match m in Regex.Matches(text, @"const\s+string\s+Refusal\w+\s*=\s*""([A-Za-z_]+)"""))
            {
                codes.Add(m.Groups[1].Value);
            }
            return codes;
        }

        static string Tip(MethodInfo append, FsaScaleAnchor anchor)
        {
            var tip = new StringBuilder();
            append.Invoke(null, new object[] { tip, new List<FsaScaleAnchor> { anchor } });
            return tip.ToString().Trim('\r', '\n');
        }

        // ------------------------------------------------------------------
        //  (Б) «пик не объяснён»
        // ------------------------------------------------------------------

        static void Uncovered()
        {
            Console.WriteLine("=== (Б) значение строки «пик не объяснён» ===");
            string ru = Own("FSAReport_UncoveredValue", "ru");
            string en = Own("FSAReport_UncoveredValue", "en");
            Console.WriteLine("    ru «{0}», en «{1}»", ru, en);
            Ok(!string.IsNullOrEmpty(ru) && !Regex.IsMatch(ru, @"\bсет"), "ru: без слова «сет»");
            Ok(!string.IsNullOrEmpty(en) && !Regex.IsMatch(en, @"\bset\b"), "en: без слова «set»");
        }

        // ------------------------------------------------------------------
        //  (В) подсказка поля «Изотоп» — настоящее окно подсказки
        // ------------------------------------------------------------------

        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        static void IsotopeTip()
        {
            Console.WriteLine("=== (В) подсказка поля «Изотоп» ===");
            foreach (string culture in new[] { "en", "ru" })
            {
                Ui(culture);
                string raw = BecquerelMonitor.Properties.Resources.ResourceManager.GetString("NucBase.IsotopeTextBoxTooltip1", new CultureInfo(culture));
                using (var form = new BecquerelMonitor.NucBase.NucBase())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(100, 300);
                    form.Show();
                    form.Activate();
                    // на скрытом столе форма не становится активной сама, а ToolTip.Show
                    // показывает подсказку только у активного окна потока
                    SetActiveWindow(form.Handle);
                    Pump();
                    Console.WriteLine("    {0}: активное окно потока — форма: {1}", culture, GetActiveWindow() == form.Handle);
                    var box = (Control)typeof(BecquerelMonitor.NucBase.NucBase).GetField("IsotopeTextBox", Any).GetValue(form);
                    typeof(BecquerelMonitor.NucBase.NucBase).GetMethod("IsotopeTextBox_Enter", Any).Invoke(form, new object[] { box, EventArgs.Empty });
                    Pump();
                    // Текст — тот, что ToolTip.Show положил подсказке поля (TipInfo.Caption в
                    // закрытой таблице `tools`), окно — сама подсказка: видима ли и где стоит.
                    var tip = (ToolTip)typeof(BecquerelMonitor.NucBase.NucBase).GetField("toolTip1", Any).GetValue(form);
                    IntPtr tipHandle = (IntPtr)typeof(ToolTip).GetProperty("Handle", Any).GetValue(tip, null);
                    var tools = (System.Collections.Hashtable)typeof(ToolTip).GetField("tools", Any).GetValue(tip);
                    object info = tools[box];
                    string shown = info == null ? null : (string)info.GetType().GetProperty("Caption", Any).GetValue(info, null);
                    bool visible = tipHandle != IntPtr.Zero && IsWindowVisible(tipHandle);
                    RECT tipRect;
                    GetWindowRect(tipHandle, out tipRect);
                    if (shown == null || !visible)
                    {
                        Unmeasured(culture + ": подсказка не показана (текст " + (shown == null ? "нет" : "есть") + ", окно видимо " + visible + ")");
                        continue;
                    }

                    Rectangle field = box.RectangleToScreen(box.ClientRectangle);
                    string[] lines = shown.Replace("\r\n", "\n").Split('\n');
                    int longest = 0;
                    foreach (string line in lines) longest = Math.Max(longest, line.Length);
                    Console.WriteLine("    {0}: строк {1}, самая длинная {2} знаков, окно {3}×{4} px, верх {5}, низ {6}; поле {7}…{8}",
                                      culture, lines.Length, longest, tipRect.Right - tipRect.Left, tipRect.Bottom - tipRect.Top,
                                      tipRect.Top, tipRect.Bottom, field.Top, field.Bottom);
                    Ok(longest <= AppUi.TipWidth, culture + ": строки не длиннее " + AppUi.TipWidth.ToString(CultureInfo.InvariantCulture));
                    Ok(string.Join(" ", lines) == raw, culture + ": склейка строк = текст ресурса");
                    // (критик `critUi2`) над полем блок закрывал заголовок окна и подпись группы
                    Ok(tipRect.Top >= field.Bottom, culture + ": подсказка под полем — поле, заголовок окна и подпись группы открыты");
                    form.Close();
                }
            }
        }

        // ------------------------------------------------------------------
        //  (Г) колонка «Период полураспада»
        // ------------------------------------------------------------------

        static void HalfLifeColumn()
        {
            Console.WriteLine("=== (Г) колонки «Базы изотопов» ===");
            foreach (string culture in new[] { "en", "ru" })
            {
                Ui(culture);
                using (var form = new BecquerelMonitor.NucBase.NucBase())
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(100, 300);
                    form.Show();
                    Pump();
                    Type t = typeof(BecquerelMonitor.NucBase.NucBase);
                    var grid = (DataGridView)t.GetField("ResultDataGridView", Any).GetValue(form);
                    var column = (DataGridViewColumn)t.GetField("HalfLifeColumn", Any).GetValue(form);
                    var none = new HashSet<DataGridViewColumn>();
                    Column(culture + ", пустая", grid, column, none);
                    form.CallSearch(661.657m);
                    Pump();
                    Console.WriteLine("    {0}: строк после поиска {1}, вертикальная прокрутка {2}", culture, grid.Rows.Count,
                                      grid.Controls.OfType<VScrollBar>().Any(s => s.Visible));
                    if (grid.Rows.Count < 20)
                    {
                        Unmeasured(culture + ": поиск дал меньше 20 строк — прокрутки нет, сцена не та");
                    }
                    Column(culture + ", с результатом", grid, column, none);

                    // (критик `critUi2`, третий заход, (б)) раскладка на поиск — одна: ширина каждой
                    // колонки меняется не больше одного раза (прежде 9–24 события, 83 → 82 → 80 → 79)
                    var changes = new Dictionary<DataGridViewColumn, int>();
                    DataGridViewColumnEventHandler count = (s, e) => { int n; changes.TryGetValue(e.Column, out n); changes[e.Column] = n + 1; };
                    grid.ColumnWidthChanged += count;
                    form.CallSearch(1460.82m);
                    Pump();
                    grid.ColumnWidthChanged -= count;
                    int most = changes.Count == 0 ? 0 : changes.Values.Max();
                    Console.WriteLine("    {0}: событий смены ширины на поиск {1}, у одной колонки не больше {2}", culture, changes.Values.Sum(), most);
                    Ok(most <= 1, culture + ": ширина колонки меняется на поиск не больше одного раза");

                    // (в) таблица не тянется потому, что не тянется окно: «База изотопов» — окно
                    // неизменного размера (MinimumSize = MaximumSize, без кнопки «развернуть»).
                    // Сделать его растягиваемым — решение Amber, не правка привязки; здесь только замер.
                    Console.WriteLine("    {0}: окно {1}×{2}, MinimumSize {3}, MaximumSize {4}, MaximizeBox {5}", culture,
                                      form.Width, form.Height, form.MinimumSize, form.MaximumSize, form.MaximizeBox);
                    // (г) много строк: весь диапазон энергий
                    ((Control)t.GetField("IsotopeTextBox", Any).GetValue(form)).Text = "";
                    ((Control)t.GetField("LowEnrgTextBox", Any).GetValue(form)).Text = "0";
                    ((Control)t.GetField("HighEnrgTextBox", Any).GetValue(form)).Text = "3000";
                    t.GetMethod("DoSearch", Any).Invoke(form, null);
                    Pump();
                    Console.WriteLine("    {0}: строк на 0…3000 кэВ {1}", culture, grid.Rows.Count);
                    Column(culture + ", 0…3000 кэВ", grid, column, none);

                    // (а) ширину, заданную человеком, раскладка не сбрасывает — ни новой пачкой строк,
                    // ни сменой размера окна; не помещается — прокрутка вбок, а не обрезка
                    var name = (DataGridViewColumn)t.GetField("NameColumn2", Any).GetValue(form);
                    form.CallSearch(661.657m);
                    Pump();
                    int manual = name.Width + 23;
                    name.Width = manual;
                    Pump();
                    form.CallSearch(1460.82m);
                    Pump();
                    form.Width += 40;
                    Pump();
                    form.Width -= 40;
                    Pump();
                    Console.WriteLine("    {0}: ручная ширина «{1}» {2}, после поиска и смены размера {3}", culture, name.HeaderText, manual, name.Width);
                    Ok(name.Width == manual, culture + ": ручная ширина колонки держится");
                    Column(culture + ", ручная ширина", grid, column, new HashSet<DataGridViewColumn> { name });
                    form.Close();
                }
            }
        }

        /// <summary>
        /// Каждая колонка не уже самого длинного слова своего заголовка плюс поля текста (2 + 2 px
        /// и граница) и самой длинной ячейки — кроме колонок неизменной ширины и заданных
        /// человеком (<paramref name="manual"/>). Колонка, видная не целиком, допустима только при
        /// видимой полосе прокрутки вбок (прокрутка, а не обрезка; критик `critUi2`, третий заход).
        /// </summary>
        static void Column(string title, DataGridView grid, DataGridViewColumn halfLife, HashSet<DataGridViewColumn> manual)
        {
            Font font = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
            bool hscroll = grid.Controls.OfType<HScrollBar>().Any(s => s.Visible);
            int sum = 0;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (!column.Visible) continue;
                sum += column.Width;
                string text = column.HeaderText ?? "";
                // поля текста заголовка DataGridView: по 2 px с каждой стороны и граница
                const int overhead = 5;
                int word = 0;
                foreach (string part in text.Split(' ', '\n'))
                {
                    word = Math.Max(word, TextRenderer.MeasureText(part, font).Width);
                }
                Rectangle cut = grid.GetColumnDisplayRectangle(column.Index, true);
                bool whole = cut.Width == column.Width;
                string mark = column == halfLife ? " ← период" : "";
                int content = grid.Rows.Count > 0 ? column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCellsExceptHeader, true) : 0;
                int need = Math.Max(word + overhead, content);
                Console.WriteLine("    {0}: «{1}» ширина {2}, видно {3}, слову с полями нужно {4}, самой длинной ячейке {5}{6}",
                                  title, text.Replace("\n", " "), column.Width, cut.Width, word + overhead, content, mark);
                Ok(whole || hscroll, title + ": «" + text + "» видна целиком или есть прокрутка вбок");
                Ok(word + overhead <= column.Width, title + ": самое длинное слово «" + text + "» помещается");
                if (column.Resizable != DataGridViewTriState.False && !manual.Contains(column) && grid.Rows.Count > 0)
                {
                    Ok(column.Width >= need, title + ": «" + text + "» не уже своей самой длинной ячейки ("
                       + column.Width.ToString(CultureInfo.InvariantCulture) + " из " + need.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
            Console.WriteLine("    {0}: сумма колонок {1}, клиент таблицы {2}, прокрутка вбок {3}, сдвиг {4}",
                              title, sum, grid.ClientSize.Width, hscroll, grid.HorizontalScrollingOffset);
        }

        // ------------------------------------------------------------------
        //  (Д) вопрос сброса библиотеки
        // ------------------------------------------------------------------

        static void ResetQuestion(string work)
        {
            Console.WriteLine("=== (Д) вопрос сброса библиотеки к поставочной, ответ «Нет» ===");
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            FieldInfo field = typeof(NuclideDefinitionManager).GetField("nuclideDefinitionFilename", Any);
            string original = (string)field.GetValue(manager);
            string dir = Path.Combine(work, "reset probe dir", "BecquerelMonitor", "bin", "screen", "config");
            Directory.CreateDirectory(dir);
            string library = Path.Combine(dir, "NuclideDefinition.xml");
            // библиотека сверяется с поставкой по содержимому, поэтому из копии снимается одна линия
            var xml = new System.Xml.XmlDocument();
            xml.Load(original);
            System.Xml.XmlNode firstLine = xml.SelectSingleNode("/NuclideDefinitionFile/NuclideDefinitions/Nuclide");
            Console.WriteLine("    из копии снята линия «{0}»", firstLine.SelectSingleNode("Name").InnerText);
            firstLine.ParentNode.RemoveChild(firstLine);
            xml.Save(library);
            if (File.Exists(library + ".bak")) File.Delete(library + ".bak");
            string before = Sha(library);
            field.SetValue(manager, library);
            string asked = null;
            try
            {
                Ok(manager.LoadDefinitionFile(true), "копия библиотеки прочитана");
                AppUi.Probe = (kind, text, caption) =>
                {
                    Console.WriteLine("    окно «{0}»: {1}", kind, text.Replace("\n", " / "));
                    if (kind == "ask") asked = text;
                    return false;
                };
                using (var editor = new NuclideDefinitionForm())
                {
                    object result = typeof(NuclideDefinitionForm).GetMethod("ResetLibraryToShipped", Any).Invoke(editor, null);
                    Ok(Equals(result, false), "«Нет» — сброса нет");
                }
            }
            finally
            {
                AppUi.Probe = null;
                field.SetValue(manager, original);
                manager.LoadDefinitionFile(true);
            }

            if (asked == null)
            {
                Unmeasured("вопроса не было");
                return;
            }

            string backup = library + ".bak";
            string[] lines = asked.Replace("\r\n", "\n").Split('\n');
            int first = Array.FindIndex(lines, l => backup.StartsWith(l, StringComparison.Ordinal) && l.Length > 3);
            var joined = new StringBuilder();
            int pathLines = 0, longest = 0;
            bool breaksAfterSeparator = true;
            for (int i = first; first >= 0 && i < lines.Length && joined.Length < backup.Length; i++)
            {
                if (pathLines > 0 && !(joined[joined.Length - 1] == '\\')) breaksAfterSeparator = false;
                joined.Append(lines[i]);
                pathLines++;
                longest = Math.Max(longest, lines[i].Length);
            }
            Console.WriteLine("    путь {0} знаков, строк пути {1}, самая длинная {2}", backup.Length, pathLines, longest);
            foreach (string l in lines) Console.WriteLine("      | " + l);
            Ok(joined.ToString() == backup, "строки пути склеиваются в путь копии побайтно");
            Ok(pathLines > 1 && breaksAfterSeparator, "путь перенесён, и только после разделителя каталогов");
            Ok(longest <= 40, "строки пути не длиннее 40 знаков");
            string after = Sha(library);
            Console.WriteLine("    sha256 до  {0}", before);
            Console.WriteLine("    sha256 после {0}", after);
            Ok(before == after, "«Нет»: файл библиотеки не изменён");
            Ok(!File.Exists(backup), "«Нет»: копия .bak не заведена");
        }

        static string Sha(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
        }

        // ------------------------------------------------------------------
        //  (Е) свёрнутое окно «Редактировать изотоп»
        // ------------------------------------------------------------------

        static void MinimizedEditor()
        {
            Console.WriteLine("=== (Е) свёрнутое «Редактировать изотоп» и повторный вызов ===");
            Ui("ru");
            using (var owner = new Form { Text = "хозяин пробы", StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), Size = new Size(800, 600) })
            {
                owner.Show();
                Pump();
                using (var editor = new NuclideDefinitionForm())
                {
                    editor.Owner = owner;
                    editor.Show();
                    editor.Activate();
                    Pump();
                    Ok(!IsIconic(editor.Handle), "контроль: открытое окно не свёрнуто");
                    editor.WindowState = FormWindowState.Minimized;
                    Pump();
                    bool minimized = IsIconic(editor.Handle);
                    Console.WriteLine("    свёрнуто: {0}", minimized);
                    if (!minimized)
                    {
                        Unmeasured("окно не свернулось — сцена не та");
                        return;
                    }

                    // ровно то, что делает MainForm.ShowNuclideDefinitionForm с уже открытым окном
                    editor.Show();
                    editor.Activate();
                    Pump();
                    Console.WriteLine("    после Show+Activate: свёрнуто {0}, состояние {1}, хозяин {2}",
                                      IsIconic(editor.Handle), editor.WindowState, editor.Owner == owner ? "тот же" : "другой");
                    Ok(!IsIconic(editor.Handle) && editor.WindowState != FormWindowState.Minimized, "окно развёрнуто повторным вызовом");
                    Ok(editor.Owner == owner, "хозяин окна тот же");
                    editor.Close();
                }
                owner.Close();
            }
        }

        // ------------------------------------------------------------------
        //  (Л) строка состояния окна отчёта FSA при отказе
        // ------------------------------------------------------------------

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr dc, uint flags);

        /// <summary>
        /// Строка состояния отчёта FSA с длинной причиной отказа (как на снимках полосы `screen`
        /// `13_*`, `14_*`): текст метки — полный, обрыв — многоточием (`AutoEllipsis`), полный текст —
        /// подсказкой блоком. Окно строится без `MainForm` (null): сеанс и данные подставлены в
        /// закрытые поля, состояние метки ставит настоящий `RefreshStatusLine`. Снимок окна —
        /// PrintWindow на скрытом столе в `--work`.
        /// </summary>
        static void StatusLine()
        {
            Console.WriteLine("=== (Л) строка состояния окна отчёта FSA при отказе ===");
            Ui("ru");
            string reason = "Полноспектральный разбор: прибор спектра не найден в конфигурации (в файле — «проба»), а без прибора нет и геометрии. Выберите прибор в панели «Управление измерением»";
            using (var report = new FSAReportView(null))
            {
                var session = new FsaAnalysisSession();
                typeof(FsaAnalysisSession).GetField("status", Any).SetValue(session, reason);
                typeof(FSAReportView).GetField("session", Any).SetValue(report, session);
                typeof(FSAReportView).GetField("probeResultData", Any).SetValue(report, new ResultData());
                typeof(FSAReportView).GetMethod("RefreshStatusLine", Any).Invoke(report, null);
                var label = (Label)typeof(FSAReportView).GetField("statusLabel", Any).GetValue(report);
                string tip = report.ToolTipOf(label);
                Console.WriteLine("    метка «{0}», AutoEllipsis {1}", label.Text, label.AutoEllipsis);
                Console.WriteLine("    подсказка «{0}»", (tip ?? "").Replace(Environment.NewLine, " / "));
                Ok(label.Text.EndsWith(reason, StringComparison.Ordinal), "текст метки — полный (пробы читают его)");
                Ok(label.AutoEllipsis, "обрыв метки — многоточием (AutoEllipsis)");
                Ok(tip == AppUi.WrapTip(label.Text), "полный текст — подсказкой блоком");

                // снимок: метка той же ширины, что в окне по умолчанию (320 px), на форме скрытого стола
                using (var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(50, 50), Size = new Size(340, 80), Text = "проба Л" })
                {
                    var copy = new Label { AutoEllipsis = label.AutoEllipsis, Text = label.Text, Font = label.Font, ForeColor = label.ForeColor,
                                           TextAlign = label.TextAlign, Dock = DockStyle.Top, Height = label.Height };
                    host.Controls.Add(copy);
                    host.Show();
                    Pump();
                    using (var bmp = new Bitmap(host.Width, host.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            IntPtr dc = g.GetHdc();
                            PrintWindow(host.Handle, dc, 2);
                            g.ReleaseHdc(dc);
                        }
                        if (shotDir != null)
                        {
                            bmp.Save(Path.Combine(shotDir, "status_line_" + (label.AutoEllipsis ? "ellipsis" : "plain") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    host.Close();
                }
            }
        }

        // ------------------------------------------------------------------
        //  (М) единый вид числа «Базы изотопов»
        // ------------------------------------------------------------------

        /// <summary>
        /// Решение Amber 07.11.2026 вопросником, дословно: «Единый вид «7.04e-8» (Рекомендую)»: от
        /// 1e-4 до 1e6 — дробью, вне — строчная «e» без «+» и лишних нулей; в таблице 4 значащие
        /// цифры, в подсказке — все; так же карточка нуклида. Функции берутся по имени (отражением):
        /// на сборке без них плечо краснеет, а не падает.
        /// </summary>
        static void NumberFormat()
        {
            Console.WriteLine("=== (М) единый вид числа: таблица, подсказка, карточка ===");
            Type nb = typeof(BecquerelMonitor.NucBase.NucBase);
            MethodInfo table = nb.GetMethod("IntensityCaption", BindingFlags.Public | BindingFlags.Static);
            MethodInfo limit = nb.GetMethod("IntensityLimitCaption", BindingFlags.Public | BindingFlags.Static);
            MethodInfo full = nb.GetMethod("IntensityFullCaption", BindingFlags.Public | BindingFlags.Static);
            Func<double, string> T = v => table == null ? "(нет IntensityCaption)" : (string)table.Invoke(null, new object[] { v });
            var cases = new[]
            {
                new KeyValuePair<double, string>(1e-5, "1e-5"),
                new KeyValuePair<double, string>(5.8086e-6, "5.809e-6"),
                new KeyValuePair<double, string>(7.04e-8, "7.04e-8"),
                new KeyValuePair<double, string>(12345.678, "12345.7"),
                new KeyValuePair<double, string>(5.42e17, "5.42e17"),
                new KeyValuePair<double, string>(7.9e20, "7.9e20"),
                new KeyValuePair<double, string>(22.666946633968, "22.67"),
                new KeyValuePair<double, string>(6.99877963, "6.999"),
                new KeyValuePair<double, string>(1.2345e-4, "0.0001235"),
                new KeyValuePair<double, string>(100.0, "100"),
                new KeyValuePair<double, string>(1.235e6, "1.235e6"),
            };
            foreach (var c in cases)
            {
                string got = T(c.Key);
                Ok(got == c.Value, "таблица: " + c.Key.ToString("R", CultureInfo.InvariantCulture) + " → «" + got + "» (ждали «" + c.Value + "»)");
            }

            string lim = limit == null ? "(нет)" : (string)limit.Invoke(null, new object[] { 1e-5 });
            Ok(lim == "< 1e-5", "предел: «" + lim + "» (ждали «< 1e-5»)");
            string tip = full == null ? "(нет)" : (string)full.Invoke(null, new object[] { 1.43777164951569e-5, false });
            Ok(tip == "1.43777164951569e-5", "подсказка всеми знаками: «" + tip + "» (ждали «1.43777164951569e-5»)");
            string tipPlain = full == null ? "(нет)" : (string)full.Invoke(null, new object[] { 22.666946633968, false });
            Ok(tipPlain == "22.666946633968", "подсказка всеми знаками, дробью: «" + tipPlain + "»");

            foreach (string culture in new[] { "en", "ru" })
            {
                Ui(culture);
                var nuc = new BecquerelMonitor.NucBase.Nuclide { Z = 52, N = 78, HalfLife = "0.79E+21", HalfLifeUOM = "Y", HalfLife_Sec = 2.49e28, HalfLifeIsLimit = true };
                string hl = BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeCaption(nuc);
                string unitY = BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeUnitCaption("Y");
                Ok(hl == "> 7.9e20 " + unitY, culture + ": карточка, период-предел: «" + hl + "»");
                var tiny = new BecquerelMonitor.NucBase.Nuclide { Z = 1, N = 2, HalfLife = "3.705e-13", HalfLifeUOM = "ns", HalfLife_Sec = 3.705e-22, HalfLifeIsLimit = true };
                string hl2 = BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeCaption(tiny);
                Ok(hl2 == "> 3.705e-13 " + BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeUnitCaption("ns"), culture + ": карточка, малый период: «" + hl2 + "»");
                var bi = new BecquerelMonitor.NucBase.Nuclide { Z = 83, N = 129, HalfLife = "60.55", HalfLifeUOM = "m", HalfLife_Sec = 3633.0 };
                string hl3 = BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeCaption(bi);
                Ok(hl3 == "60.55 " + BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeUnitCaption("m"), culture + ": карточка, обычный период: «" + hl3 + "»");
                string sa = BecquerelMonitor.NucBase.NucBaseFramework.SpecificActivityCaption(bi);
                Console.WriteLine("    {0}: удельная активность «{1}»", culture, sa);
                Ok(sa.IndexOf('E') < 0 && sa.IndexOf('+') < 0 && Regex.IsMatch(sa, @"^\d(\.\d+)?e\d+ "), culture + ": карточка, удельная активность — строчная e без «+»");
            }
        }

        // ------------------------------------------------------------------
        //  (К) свёрнутая «База изотопов» и повторный вызов
        // ------------------------------------------------------------------

        static void MinimizedNucBase()
        {
            Console.WriteLine("=== (К) свёрнутая «База изотопов» и BringToFront (так её поднимает MainForm.ShowNucBaseView) ===");
            Ui("ru");
            using (var form = new BecquerelMonitor.NucBase.NucBase())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(100, 300);
                form.Show();
                Pump();
                form.WindowState = FormWindowState.Minimized;
                Pump();
                bool minimized = IsIconic(form.Handle);
                Console.WriteLine("    свёрнуто: {0}", minimized);
                if (!minimized)
                {
                    Unmeasured("окно не свернулось — сцена не та");
                    return;
                }

                // ровно то, что делает MainForm.ShowNucBaseView с уже открытым окном
                form.BringToFront();
                Pump();
                Console.WriteLine("    после BringToFront: свёрнуто {0}, состояние {1}", IsIconic(form.Handle), form.WindowState);
                Ok(!IsIconic(form.Handle) && form.WindowState != FormWindowState.Minimized, "окно развёрнуто повторным вызовом");

                // контроль: сворачивание само окно не разворачивает (перестановки HWND_TOP нет)
                form.WindowState = FormWindowState.Minimized;
                Pump();
                Ok(IsIconic(form.Handle), "контроль: свёрнутое без вызова остаётся свёрнутым");
                form.Close();
            }
        }

        // ------------------------------------------------------------------
        //  (Ж) пояснение подстановки кристалла
        // ------------------------------------------------------------------

        static void CrystalNote()
        {
            Console.WriteLine("=== (Ж) вопрос «Сохранить?» называет подставленный кристалл ===");
            MethodInfo note = typeof(DeviceConfigForm).GetMethod("CrystalOfferNote", Any);
            if (note == null)
            {
                Ok(false, "у окна прибора нет пояснения подстановки (CrystalOfferNote)");
                return;
            }

            foreach (string culture in new[] { "en", "ru" })
            {
                Ui(culture);
                using (var form = new DeviceConfigForm())
                {
                    var offered = Device("", "Cesium iodide");
                    Load(form, offered);
                    string text = (string)note.Invoke(form, null);
                    Console.WriteLine("    {0}: подстановка — изменён {1}, «{2}»", culture, offered.Dirty, text);
                    Ok(offered.Dirty && text != null && text.Contains("Cesium iodide"), culture + ": подстановка названа");

                    var combo = (ComboBox)typeof(DeviceConfigForm).GetField("crystalMaterialCombo", Any).GetValue(form);
                    object other = null;
                    foreach (object item in combo.Items)
                    {
                        string s = Convert.ToString(item, CultureInfo.InvariantCulture);
                        if (s.IndexOf("Sodium iodide", StringComparison.OrdinalIgnoreCase) >= 0) other = item;
                    }
                    if (other != null)
                    {
                        combo.SelectedItem = other;
                        Ok(note.Invoke(form, null) == null, culture + ": вещество сменено в списке — пояснения нет");
                    }
                    else
                    {
                        Unmeasured(culture + ": в списке нет Sodium iodide");
                    }

                    var bare = Device("", (string)null);
                    Load(form, bare);
                    Ok(!bare.Dirty && note.Invoke(form, null) == null, culture + ": контроль — без геометрии не изменён и без пояснения");

                    var set = Device("Sodium iodide", "Cesium iodide");
                    Load(form, set);
                    Ok(!set.Dirty && note.Invoke(form, null) == null, culture + ": поле задано — без пояснения");

                    // (критик `critUi2`) кристалл выбрал человек (окно выбора из разных кристаллов,
                    // «Да» на вопрос о новой геометрии) — вопрос называет выбор, а не подстановку окна
                    var two = Device("", "Cesium iodide", "Sodium iodide");
                    Load(form, two);
                    Ok(!two.Dirty && note.Invoke(form, null) == null, culture + ": контроль — разные кристаллы без окон: не изменён, без пояснения");
                    typeof(DeviceConfigForm).GetMethod("SetCrystalFromGeometry", Any).Invoke(form, new object[] { "Sodium iodide", true });
                    string chosen = (string)note.Invoke(form, null);
                    string chosenFormat = new ComponentResourceManager(typeof(DeviceConfigForm)).GetString("crystalChosenNote", new CultureInfo(culture));
                    string offeredFormat = new ComponentResourceManager(typeof(DeviceConfigForm)).GetString("crystalOfferedNote", new CultureInfo(culture));
                    Console.WriteLine("    {0}: выбор человека — изменён {1}, «{2}»", culture, two.Dirty, chosen);
                    Ok(two.Dirty && chosen != null && chosenFormat != null
                       && chosen == string.Format(CultureInfo.InvariantCulture, chosenFormat, Convert.ToString(
                              typeof(DeviceConfigForm).GetMethod("CrystalComboItem", Any).Invoke(form, new object[] { "Sodium iodide", false }), CultureInfo.InvariantCulture)),
                       culture + ": выбор человека назван выбором (crystalChosenNote)");
                    Ok(chosen == null || offeredFormat == null || !chosen.StartsWith(offeredFormat.Substring(0, Math.Min(12, offeredFormat.Length)), StringComparison.Ordinal),
                       culture + ": выбор человека не назван подстановкой окна");
                }
            }
        }

        static DeviceConfigInfo Device(string field, params string[] crystals)
        {
            var device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "проба fixUi2" };
            device.CrystalMaterialName = field;
            int i = 0;
            foreach (string crystal in crystals)
            {
                var curve = new EfficiencyConfigData("кривая " + (++i).ToString(CultureInfo.InvariantCulture));
                if (crystal != null)
                {
                    var g = new BecquerelMonitor.EfficiencyMaker.GeometryModel();
                    g.Crystal = new BecquerelMonitor.EfficiencyMaker.GeometryMaterial { Name = crystal };
                    curve.Geometry = g;
                }
                device.EfficiencyConfigs.Add(curve);
            }
            return device;
        }

        static void Load(DeviceConfigForm form, DeviceConfigInfo device)
        {
            form.ActiveDeviceConfig = device;
            typeof(DeviceConfigForm).GetMethod("LoadFormContents", Any).Invoke(form, new object[] { device });
        }
    }
}
