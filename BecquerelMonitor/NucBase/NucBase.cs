using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace BecquerelMonitor.NucBase
{
    public partial class NucBase : Form
    {
        private const int CheckedColumnIdx = 0;
        private const int NameColumnIdx = 1;
        private const int LineColumnIdx = 2;
        private const int EnergyColumnIdx = 3;
        private const int IntencityColumnIdx = 4;
        private const int SeriesColumnIdx = 5;
        private const int DecayTypeColumnIdx = 6;
        private const int HalfLifeColumnIdx = 7;

        /// <summary>
        /// Хвост подписи у характеристического рентгена элемента: «W x-ray».
        /// Не переводится: подпись стоит в файле определений и читается тем же
        /// разбором на обоих языках.
        /// </summary>
        private const string XrayNameSuffix = "x-ray";

        /// <summary>
        /// Ключ строки состояния «условия отбора не заданы» (`D44`) в
        /// СОБСТВЕННОМ <c>resx</c> этой формы.
        ///
        /// ⚠ Строка живёт в паре <c>NucBase.resx</c> / <c>NucBase.ru.resx</c>,
        /// а не в общем <c>Properties/Resources</c>: она принадлежит только
        /// этому окну и читается тем же <see cref="ComponentResourceManager"/>,
        /// которым конструктор берёт подписи его же контролов. Пара
        /// проверяется <c>tools/check_resx.py</c> наравне с подписями.
        ///
        /// ⛔ Ключ НЕ ИМЕЕТ ВИДА «контрол.свойство» и конструктору формы
        /// неизвестен: при перезаписи <c>resx</c> конструктором WinForms его
        /// легко потерять. Потеря видна сразу — строка состояния показывает
        /// само имя ключа (см. <see cref="OwnText"/>), а не пустоту.
        /// </summary>
        private const string NoCriteriaKey = "NucBase_NoCriteria";

        static readonly ComponentResourceManager OwnResources = new ComponentResourceManager(typeof(NucBase));

        /// <summary>
        /// Строка из собственного <c>resx</c> формы. Ключ вместо пропажи:
        /// пустая строка состояния неотличима от «сказать нечего», а признак
        /// без читателя — не работа.
        /// </summary>
        static string OwnText(string key)
        {
            return OwnResources.GetString(key) ?? key;
        }

        private string SearchedIsotope;

        public NucBase()
        {
            InitializeComponent();
            // (`AMBER264`, `AMBER299`) Подсказки из дизайнера — блоком.
            AppUi.WrapToolTips(this.toolTip1, this);
            InitGrids();
        }

        /// <summary>
        /// (критик `critUi2`, 07.11.2026) Свёрнутое окно «Базы изотопов», которое приложение
        /// поднимает наверх, разворачивается. Меню «База изотопов…» и «Поиск в Базе Изотопов»
        /// панели пиков зовут <c>MainForm.ShowNucBaseView</c>, а та у открытого окна делает
        /// только <c>BringToFront()</c> — это перестановка порядка окон (<c>SetWindowPos</c> с
        /// <c>HWND_TOP</c>, без сдвига и размера), свёрнутое от неё остаётся свёрнутым, и на
        /// экране ничего не появлялось. Окно ловит именно такую перестановку, будучи свёрнутым, и
        /// разворачивается само (<c>SW_RESTORE</c>: развёрнутое возвращается развёрнутым).
        /// <c>MainForm.cs</c> не тронут.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WmWindowPosChanging = 0x0046;
            const int SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
            if (m.Msg == WmWindowPosChanging && m.LParam != IntPtr.Zero && this.IsHandleCreated
                && this.WindowState == FormWindowState.Minimized)
            {
                var pos = (WindowPos)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(WindowPos));
                // Замер (скрытый стол, 07.11.2026): BringToFront свёрнутого окна приходит как
                // flags = 0x3 (NOMOVE|NOSIZE), а HWND_TOP Windows уже заменила описателем окна, под
                // которое ставить, — поэтому судится не «после кого», а род вызова: только порядок,
                // с активацией, не вниз и не в «поверх всех». Сворачивание приходит с перемещением
                // (flags 0x8170) и сюда не попадает.
                long after = pos.hwndInsertAfter.ToInt64();
                if ((pos.flags & SwpNoMove) != 0 && (pos.flags & SwpNoSize) != 0
                    && (pos.flags & (SwpNoZOrder | SwpNoActivate)) == 0
                    && after != 1 && after != -1 && after != -2)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        if (!this.IsDisposed && this.WindowState == FormWindowState.Minimized)
                        {
                            ShowWindow(new System.Runtime.InteropServices.HandleRef(this, this.Handle), 9);   // SW_RESTORE
                            this.Activate();
                        }
                    });
                }
            }

            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WindowPos
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public int flags;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(System.Runtime.InteropServices.HandleRef hWnd, int nCmdShow);

        public NucBase(Form mainForm)
        {
            InitializeComponent();
            // (`AMBER264`, `AMBER299`) Подсказки из дизайнера — блоком.
            AppUi.WrapToolTips(this.toolTip1, this);
            InitGrids();
            this.mainForm = mainForm;
            this.Icon = Resources.becqmoni;
            this.IncludeDecayChainCheckBox.Enabled = false;
            this.comboBoxNameFormat.SelectedIndex = 1;
        }

        /// <summary>
        /// (П275, 14.10.2026) ЧИСЛА ТАБЛИЦ — С ТОЧКОЙ В ЛЮБОЙ КУЛЬТУРЕ.
        ///
        /// Энергия и выход лежат в ячейках числом <c>double</c> — так их читает
        /// импорт в набор нуклидов и так сортирует колонка, — а печатает число
        /// сама <see cref="DataGridView"/>, поставщиком формата стиля ячейки; не
        /// заданный, он берёт культуру потока. На `ru-RU` таблица показывала
        /// «111,238», «727,33», «6,99877963…» (П273, снимки `12_…`, `22_…`) —
        /// и в английском интерфейсе тоже, культура потока там та же.
        ///
        /// Поставщик ставится на стиль ТАБЛИЦЫ: стили строк и колонок своего не
        /// задают и наследуют его (у лишней строки Kβ задан только цвет).
        /// Разбора обратно нет и не нужно: числовые колонки только для чтения,
        /// импорт берёт <c>double</c> из <c>Value</c>, а не текст.
        ///
        /// Подпись периода в колонке переводится при показе, а не в самой ячейке:
        /// текст ячейки «60.55(m)» — договор с разбором
        /// <see cref="HalfLifeYearsFromCell"/>, и единица в нём остаётся кодом базы.
        /// </summary>
        void InitGrids()
        {
            foreach (DataGridView grid in new[] { this.ResultDataGridView, this.ParentsDataGridView, this.DaughtersDataGridView })
            {
                grid.DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
            }

            this.ResultDataGridView.CellFormatting += this.ResultDataGridView_CellFormatting;
            FitColumns(this.ResultDataGridView);
        }

        /// <summary>
        /// (проверка экраном 07.11.2026) Колонки таблицы результата раскладываются под ширину
        /// области данных (<see cref="ColumnLayout"/>): каждой — не меньше слова её заголовка
        /// (<see cref="HeaderWordWidth"/>) и самой длинной ячейки, остаток — по ширинам дизайнера.
        /// Прежде ширины стояли числами дизайнера, и в русском интерфейсе (у «Интенс., %» там 75
        /// вместо 60) их сумма 439 px была больше области данных 431 px: «Период полураспада»
        /// резался краем таблицы ещё без строк, а с полосой прокрутки (17 px) — сильнее. Колонки
        /// неизменной ширины (галка) не трогаются; ширину, заданную человеком, раскладка не
        /// сбрасывает. Не помещается — таблица прокручивается вбок (`ScrollBars.Both`; дизайнер
        /// держал `Vertical`, и лишнее резалось краем). Режим <c>Fill</c> не годился: при упоре
        /// колонок в нижнюю границу он округлял сумму на 1–2 px больше области (замер пробой
        /// `ScreenFixesProbe1107`, плечо Г).
        /// </summary>
        internal static void FitColumns(DataGridView grid)
        {
            // (критик `critUi2`, третий заход) не помещается — прокрутка вбок, а не обрезка
            grid.ScrollBars = ScrollBars.Both;
            var layout = new ColumnLayout(grid);
            // Всё, что меняет место или строки, сводится в ОДНУ раскладку после пачки событий:
            // прежде размер, полоса прокрутки и строки раскладывали каждый своё, и на один поиск
            // ширина колонки менялась 9–24 раза (83 → 82 → 80 → 79, замер критика).
            EventHandler queue = (sender, e) => layout.Queue();
            grid.SizeChanged += queue;
            grid.VisibleChanged += queue;
            grid.HandleCreated += (sender, e) => layout.Apply();
            foreach (Control part in grid.Controls)
            {
                if (part is ScrollBar)
                {
                    part.VisibleChanged += queue;
                }
            }

            grid.RowsAdded += (sender, e) => layout.Queue();
            grid.RowsRemoved += (sender, e) => layout.Queue();
            // ширина, которую человек задал руками, дальше его: раскладка её не трогает
            grid.ColumnWidthChanged += (sender, e) => layout.NoteWidthChanged(e.Column);
            layout.Apply();
        }

        /// <summary>
        /// Ширина, в которую заголовок колонки ложится без разрыва слова: самое длинное слово
        /// заголовка плюс поля ячейки — по 2 px текста с каждой стороны (поля заголовка
        /// <c>DataGridView</c>), граница и отступы стиля. Значок сортировки сюда не входит: он
        /// рисуется только у колонки, по которой отсортировано, как и прежде. Открыто пробе.
        /// </summary>
        internal static int HeaderWordWidth(DataGridViewColumn column, System.Drawing.Font font)
        {
            int word = 0;
            foreach (string part in (column.HeaderText ?? string.Empty).Split(' ', '\n'))
            {
                word = Math.Max(word, TextRenderer.MeasureText(part, font).Width);
            }

            Padding padding = column.HeaderCell.InheritedStyle != null ? column.HeaderCell.InheritedStyle.Padding : Padding.Empty;
            return word + 2 * 2 + 1 + padding.Horizontal + 2;
        }

        /// <summary>
        /// Раскладка колонок таблицы «Базы изотопов»:
        /// (1) каждой — не меньше слова её заголовка (<c>MinimumWidth</c>) и самой длинной ячейки
        ///     нынешних строк;
        /// (2) осталось место — всем по ширинам дизайнера сверх данного;
        /// (3) места нет — колонки по (1), таблица прокручивается вбок; ни слово заголовка, ни
        ///     ячейка не режутся.
        /// Колонки, которым человек задал ширину руками, стоят как стоят и в дележе не участвуют.
        /// Ширины ставятся при выключенной перерисовке таблицы и только изменившиеся.
        /// </summary>
        internal sealed class ColumnLayout
        {
            readonly DataGridView grid;
            readonly Dictionary<DataGridViewColumn, int> designed = new Dictionary<DataGridViewColumn, int>();
            readonly Dictionary<DataGridViewColumn, int> headers = new Dictionary<DataGridViewColumn, int>();
            readonly HashSet<DataGridViewColumn> manual = new HashSet<DataGridViewColumn>();
            bool applying;
            bool queued;

            public ColumnLayout(DataGridView grid)
            {
                this.grid = grid;
                System.Drawing.Font font = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    this.designed[column] = column.Width;
                    this.headers[column] = Math.Max(column.MinimumWidth, HeaderWordWidth(column, font));
                }
            }

            /// <summary>Колонки, которым человек задал ширину руками (пробе).</summary>
            public ICollection<DataGridViewColumn> Manual
            {
                get { return this.manual; }
            }

            public void NoteWidthChanged(DataGridViewColumn column)
            {
                if (!this.applying && column != null && column.Resizable != DataGridViewTriState.False)
                {
                    this.manual.Add(column);
                }
            }

            public void Queue()
            {
                if (this.queued)
                {
                    return;
                }

                if (!this.grid.IsHandleCreated)
                {
                    this.Apply();
                    return;
                }

                this.queued = true;
                this.grid.BeginInvoke((MethodInvoker)delegate
                {
                    this.queued = false;
                    this.Apply();
                });
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            public void Apply()
            {
                if (this.applying)
                {
                    return;
                }

                this.applying = true;
                bool redraw = this.grid.IsHandleCreated;
                try
                {
                    if (redraw)
                    {
                        SendMessage(this.grid.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);   // WM_SETREDRAW off
                    }

                    this.Layout();
                }
                finally
                {
                    if (redraw)
                    {
                        SendMessage(this.grid.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);   // WM_SETREDRAW on
                        this.grid.Invalidate(true);
                    }

                    this.applying = false;
                }
            }

            int DataArea()
            {
                int area = this.grid.ClientSize.Width - (this.grid.BorderStyle == BorderStyle.None ? 0 : 2)
                           - (this.grid.RowHeadersVisible ? this.grid.RowHeadersWidth : 0);
                foreach (Control part in this.grid.Controls)
                {
                    if (part is VScrollBar && part.Visible)
                    {
                        area -= part.Width;
                    }
                }

                return area;
            }

            void Layout()
            {
                int area = this.DataArea();
                var flexible = new List<DataGridViewColumn>();
                foreach (DataGridViewColumn column in this.grid.Columns)
                {
                    if (!column.Visible)
                    {
                        continue;
                    }

                    if (column.Resizable == DataGridViewTriState.False || this.manual.Contains(column))
                    {
                        area -= column.Width;
                    }
                    else
                    {
                        flexible.Add(column);
                    }
                }

                if (flexible.Count == 0)
                {
                    return;
                }

                // (1) слово заголовка и самая длинная ячейка
                var width = new Dictionary<DataGridViewColumn, int>();
                var needs = new Dictionary<DataGridViewColumn, int>();
                int left = area;
                foreach (DataGridViewColumn column in flexible)
                {
                    int need = Math.Max(2, this.headers[column]);
                    if (this.grid.Rows.Count > 0)
                    {
                        need = Math.Max(need, column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCellsExceptHeader, true));
                    }

                    width[column] = need;
                    needs[column] = need;
                    left -= need;
                }

                // (2) остаток — по долям дизайнера сверх данного: кто уже получил свою долю, больше не получает
                bool fits = left >= 0;
                if (left > 0)
                {
                    int designedSum = 0;
                    foreach (DataGridViewColumn column in flexible)
                    {
                        designedSum += this.designed[column];
                    }

                    var room = new Dictionary<DataGridViewColumn, int>();
                    int roomTotal = 0;
                    foreach (DataGridViewColumn column in flexible)
                    {
                        int share = (int)Math.Floor(this.designed[column] * (double)area / Math.Max(1, designedSum));
                        room[column] = Math.Max(0, share - width[column]);
                        roomTotal += room[column];
                    }

                    int spread = left;
                    foreach (DataGridViewColumn column in flexible)
                    {
                        int give = roomTotal > 0
                            ? (int)Math.Floor(spread * (double)room[column] / roomTotal)
                            : (int)Math.Floor(spread * (double)this.designed[column] / Math.Max(1, designedSum));
                        give = Math.Min(give, left);
                        width[column] += give;
                        left -= give;
                    }

                    width[flexible[flexible.Count - 1]] += left;
                }

                if (fits)
                {
                    // DataGridView режет край последней колонки на 1 px при рамке FixedSingle (замер
                    // плечом Г) — этот пиксель снимается сразу с колонки с наибольшим запасом
                    DataGridViewColumn roomiest = null;
                    int slack = 0;
                    foreach (DataGridViewColumn column in flexible)
                    {
                        int own = width[column] - needs[column];
                        if (roomiest == null || own > slack)
                        {
                            roomiest = column;
                            slack = own;
                        }
                    }

                    if (roomiest != null && slack > 0)
                    {
                        width[roomiest] -= 1;
                    }
                }

                // (3) ставятся только изменившиеся ширины; нижняя граница — слово заголовка
                foreach (DataGridViewColumn column in flexible)
                {
                    column.MinimumWidth = Math.Max(2, this.headers[column]);
                    if (column.Width != width[column])
                    {
                        column.Width = width[column];
                    }
                }
            }
        }

        void ResultDataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // (`AMBER274`, `f8diag`) Выход-предел (`LT`/`LE` поставки) — «< число»: в ячейке
            // остаётся само число (сортировка, импорт), знак — только при показе.
            if (e.ColumnIndex == IntencityColumnIdx && e.RowIndex >= 0 && e.Value is double)
            {
                DecayRad source = this.ResultDataGridView.Rows[e.RowIndex].Tag as DecayRad;
                // (решение Amber 07.11.2026 вопросником, дословно: «Округлять в таблице
                // (Рекомендую)») выход — четырьмя значащими цифрами («22.67» вместо
                // «22.666946633968»); полное число — в подсказке ячейки (`AddRow`), в ячейке
                // лежит прежнее double — сортировка и импорт его и берут
                e.Value = source != null && source.IntensityIsLimit
                    ? IntensityLimitCaption((double)e.Value)
                    : IntensityCaption((double)e.Value);
                e.FormattingApplied = true;
                return;
            }

            string cell = e.ColumnIndex == HalfLifeColumnIdx ? e.Value as string : null;
            if (cell == null)
            {
                return;
            }

            e.Value = HalfLifeCellCaption(cell);
            e.FormattingApplied = true;
        }

        /// <summary>
        /// (`AMBER274`, полоса `f8diag` 02.11.2026) Показ выхода-предела: «&lt; 0.075». Знак —
        /// тот же <see cref="NucBaseFramework.UpperLimitMark"/>, что у периода-предела карточки;
        /// число — инвариантной культурой, как у прочих ячеек выхода. Открыт ради пробы.
        /// </summary>
        public static string IntensityLimitCaption(double value)
        {
            return NucBaseFramework.UpperLimitMark + IntensityCaption(value);
        }

        /// <summary>
        /// (решения Amber 07.11.2026 «Округлять в таблице» и «Единый вид «7.04e-8»») Выход в
        /// таблице — четыре значащие цифры единым видом (<see cref="NucBaseFramework.NumberCaption"/>):
        /// 22.666946633968 → «22.67», 6.99877963 → «6.999», 5.8086e-6 → «5.809e-6». Прежний «G4»
        /// печатал «5.809E-06» и «1.235E+04» (замер критика `critUi2`). Открыт пробе.
        /// </summary>
        public static string IntensityCaption(double value)
        {
            return NucBaseFramework.NumberCaption(value, 4);
        }

        /// <summary>Полный выход для подсказки ячейки: всеми знаками, точкой, с «&lt;» у предела.</summary>
        public static string IntensityFullCaption(double value, bool limit)
        {
            // тем же видом, но всеми знаками: «1.43777164951569e-5», а не «…E-05»
            return (limit ? NucBaseFramework.UpperLimitMark : "") + NucBaseFramework.NumberCaption(value, 0);
        }

        /// <summary>
        /// (П275) Показ ячейки периода «60.55(m)» на языке интерфейса: «60.55(мин)»
        /// по-русски, «60.55(m)» — по-английски, как и было. Ячейка без единицы в
        /// скобках показывается как есть.
        /// </summary>
        public static string HalfLifeCellCaption(string cell)
        {
            int open = (cell ?? "").IndexOf('(');
            if (open < 0 || !cell.EndsWith(")", StringComparison.Ordinal))
            {
                return cell ?? "";
            }

            string unit = cell.Substring(open + 1, cell.Length - open - 2);
            // (решение Amber 07.11.2026 «Единый вид «7.04e-8»») число — единым видом всеми знаками;
            // сама ячейка («7.04E+08(Y)») — договор с разбором `HalfLifeYearsFromCell` и не меняется
            return NucBaseFramework.NumberTextCaption(cell.Substring(0, open)) + "(" + NucBaseFramework.HalfLifeUnitCaption(unit) + ")";
        }

        /// <summary>
        /// (П275) Коды единиц периода у пунктов списка рядом с полем «T½ &gt;» —
        /// ПО МЕСТУ пункта. Пункты списка переведены (`NucBase.ru.resx`:
        /// «нс … лет»), а пересчёт в секунды
        /// (<see cref="ConvertHalfLifeToSeconds"/>) понимает код базы, поэтому
        /// единица берётся по номеру выбранного пункта, а не по его тексту. Прежде
        /// брался текст: перевод пунктов сломал бы отбор молча — неизвестная
        /// единица считается секундами. Порядок совпадает с пунктами `NucBase.resx`
        /// («ns … Y»); их число и порядок сверяет проба `NucBaseCardProbeP275`.
        /// </summary>
        internal static readonly string[] FilterUnits = { "ns", "us", "ms", "s", "m", "h", "d", "Y" };

        private void SearchButton_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void DoSearch()
        {
            string isotopeTextBox = this.IsotopeTextBox.Text.Trim().Replace("-", "");
            // ⛔ (`AMBER197`) Разбор — в `NucBaseFramework.QueryNucid`. Здесь
            // стояло выражение «m и цифра в конце — метка изомера», и «241Am»
            // искался как «241Am» (у Am, Cm, Sm, Pm, Tm, Fm «m» — часть символа):
            // в базе 0 строк, человек читал «ничего не найдено».
            string isotope = NucBaseFramework.QueryNucid(isotopeTextBox);
            this.SearchedIsotope = isotope;
            bool incDecayChain = this.IncludeDecayChainCheckBox.Checked;
            // TryParse instead of Convert.ToDouble: non-numeric input used to throw
            // FormatException out of the search button handler.
            double lowEnergy = 0.0;
            if (this.LowEnrgTextBox.Text.Length != 0)
            {
                UserNumber.TryParseDouble(this.LowEnrgTextBox.Text, out lowEnergy);
            }
            double highEnergy = 0.0;
            if (this.HighEnrgTextBox.Text.Length != 0)
            {
                UserNumber.TryParseDouble(this.HighEnrgTextBox.Text, out highEnergy);
            }
            double intensity = 0.0;
            if (this.IntencityTextBox.Text.Length != 0)
            {
                UserNumber.TryParseDouble(this.IntencityTextBox.Text, out intensity);
            }
            double half_life = -1;
            // (П275) Единица — кодом по месту пункта (`FilterUnits`), а не текстом
            // пункта: по-русски пункты переведены.
            int unitIndex = this.HalfLifeUOMComboBox.SelectedIndex;
            if (unitIndex >= 0 && unitIndex < FilterUnits.Length && this.HalfLifeTextBox.Text.Length > 0)
            {
                double halfLifeValue;
                if (UserNumber.TryParseDouble(this.HalfLifeTextBox.Text, out halfLifeValue))
                {
                    half_life = ConvertHalfLifeToSeconds(halfLifeValue, FilterUnits[unitIndex]);
                }
            }

            NucBaseFramework fw = new NucBaseFramework();

            // ⛔ ИТОГ ЗАПРОСА ГОВОРИТСЯ СЛОВАМИ, И СОБИРАЕТСЯ ОН ЗАНОВО (`T92`).
            // Пока строки состояния не было, отказ базы выглядел ровно как
            // «линий нет»: список молча оставался таким, каким был. Части
            // складываются сюда и уезжают в строку одним куском в конце — так
            // от прошлого запроса не остаётся ни одного слова.
            //
            // ⚠ Части и ПРИЧИНЫ к ним лежат врозь (<see cref="StatusNote"/>,
            // `A92`): причина у всех частей одного отказа одна, а складывалась
            // она в метку столько раз, сколько было частей.
            StatusNote status = new StatusNote();

            // Символ элемента без массового числа («W», «Pb») — запрос не про
            // распад, а про характеристический рентген: чем светит вольфрам
            // электрода или свинец домика, когда в нём выбило K-электрон. Ряда
            // и родителей у такого запроса нет, поэтому ветка своя и короткая.
            // ⛔ Отказ БАЗЫ ВЕЩЕСТВ здесь — не «это не элемент», и уходить с ним
            // в поиск нуклида нельзя (`A25`): по «W» там не найдётся ничего, и
            // человек прочитал бы «Ничего не найдено» — то есть неправду. Ответа
            // на вопрос у нас в этом случае нет вовсе, и так и говорится.
            string elementError;
            string element = ElementSymbol(isotopeTextBox, out elementError);
            if (elementError != null)
            {
                this.ResultDataGridView.Rows.Clear();
                ShowIsotopeCard("", null);
                ShowCardNote(null);
                status.AddFailure(Resources.NucBase_ElementDataFetchError, elementError);
                SetSearchStatus(status);
                UpdateNuclideDefinitionControlsState();
                return;
            }

            if (element != null)
            {
                this.SearchedIsotope = element;
                List<DecayRad> fluorescence = fw.GetFluorescence(
                    element, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy);
                this.ResultDataGridView.Rows.Clear();
                foreach (DecayRad line in fluorescence)
                {
                    AddRow(line);
                }

                RestoreSorting();
                // Карточка нуклида у элемента пустая: нуклида нет вовсе, есть
                // только номер элемента — его `ShowIsotopeCard` и оставит.
                ShowIsotopeCard(element, null);
                // Заметка о карточке от ПРОШЛОГО запроса к этому элементу не
                // относится, и остаться на экране не должна (`T96`).
                ShowCardNote(null);
                if (fw.LastError != null)
                {
                    // ⛔ Отказ базы ВЕЩЕСТВ на полпути (`A25`): таблица пуста
                    // потому, что посмотреть не удалось, — а не потому, что у
                    // элемента нет линий. Слова у этих двух случаев разные, как
                    // и у отказа поиска нуклида ниже.
                    status.AddFailure(Resources.NucBase_ElementDataFetchError, fw.LastError);
                }
                else if (fluorescence.Count == 0)
                {
                    // ⛔ ЗДЕСЬ СТОЯЛО МОДАЛЬНОЕ ОКНО, И ОНО ВЕШАЛО БЕЗОКОННЫЙ
                    // ПРОГОН НАСМЕРТЬ (`T98`). Измерено 27–28.08.2026:
                    // `DoSearch("Na")` не возвращался вовсе — процесс убивали по
                    // сроку; сторожем окон поймано и названо поимённо: класс
                    // `#32770`, заголовок «Характеристический рентген».
                    // Положительный контроль `DoSearch("W")` и до, и после даёт
                    // «Найдено линий: 3» и ни одного окна.
                    //
                    // ⚠ «Провести через `AppUi`» было бы половиной дела: окно
                    // осталось бы, а СЛОВА в строке состояния остались бы
                    // короткими и неверными по существу — «ни одна линия не
                    // подошла под условия отбора» неправда, отбор ни при чём,
                    // в таблице нет самого элемента. Поэтому длинное объяснение
                    // переехало В СТРОКУ СОСТОЯНИЯ, а окна не стало вовсе: метка
                    // видна всегда, кнопки не требует и читается прогоном так же,
                    // как глазами (`T92`).
                    status.Add(string.Format(Resources.NucBase_NoFluorescence, element));
                }
                else
                {
                    status.Add(string.Format(CultureInfo.InvariantCulture, Resources.NucBase_SearchFound, fluorescence.Count));
                }

                SetSearchStatus(status);
                UpdateNuclideDefinitionControlsState();
                return;
            }

            // ⛔ Таблица чистится ДО запроса, а не после удачного (`T92`). При
            // отказе очистка не выполнялась вовсе, и на экране оставались
            // строки ПРОШЛОГО нуклида — читались они как строки нового.
            this.ResultDataGridView.Rows.Clear();

            if (!incDecayChain)
            {
                List<DecayRad> decayRads = fw.getDecayRad(isotope, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                if (decayRads == null)
                {
                    status.AddFailure(Resources.NucBase_LinesFetchError, fw.LastError, isotope);
                }
                else
                {
                    foreach (DecayRad decrad in decayRads)
                    {
                        AddRow(decrad);
                    }
                    RestoreSorting();
                    // ⛔ «Ничего не найдено» и «не о чем спрашивать» — РАЗНЫЕ
                    // слова (`D44`). Пустой запрос — без имени родителя, без
                    // диапазона и без порогов — прежде выгружал в эту таблицу
                    // всю базу излучений (50054 строки, меряно 31.08.2026), а
                    // теперь не идёт вовсе; без отдельных слов такая пустота
                    // читалась бы как «в базе такого нет».
                    status.Add(fw.LastNoCriteria
                        ? OwnText(NoCriteriaKey)
                        : decayRads.Count == 0
                            ? Resources.NucBase_SearchEmpty
                            : string.Format(CultureInfo.InvariantCulture, Resources.NucBase_SearchFound, decayRads.Count));
                }
            }
            else
            {
                // Выбран ряд — значит и выходы показываются НА РАСПАД КОРНЯ
                // ряда, а не на распад своего нуклида: у Tl-208 в ториевом ряду
                // это 35.85 % вместо 99.75 %. Ровно эти числа и импортируются, и
                // ровно их ждёт всё, что стоит на вековом равновесии, —
                // конструктор кривой и разложение спектра.
                Dictionary<string, double> branches = fw.GetChainBranches(isotope);
                if (fw.LastError != null)
                {
                    // Обход ряда оборвался на полпути: часть членов до списка
                    // не доехала, и без этих слов их отсутствие выглядит как
                    // «их в ряду нет».
                    status.AddFailure(Resources.NucBase_DaughtersFetchError, fw.LastError, isotope);
                }

                int shown = 0;
                int refusedMembers = 0;
                string firstReason = null;
                // Ряд без корня — тот же пустой запрос, что и без ряда (`D44`):
                // корня нет, членов ряда нет, и спрашивать не о чем.
                bool noCriteria = false;
                foreach (KeyValuePair<string, double> member in branches.OrderByDescending(m => m.Value))
                {
                    // Порог выхода прикладывается к ПОКАЗАННОМУ числу, а не
                    // к базовому: иначе «не ниже 1 %» отсеивало бы по
                    // величине, которой на экране нет.
                    List<DecayRad> decayRads = fw.getDecayRad(member.Key, intensity: 0.0, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                    if (decayRads == null)
                    {
                        // Отказ по одному члену ряда не отменяет остальных, но
                        // и потеряться молча не должен: он считается и будет
                        // назван вместе с причиной.
                        refusedMembers++;
                        if (firstReason == null)
                        {
                            firstReason = fw.LastError;
                        }

                        continue;
                    }

                    noCriteria |= fw.LastNoCriteria;
                    foreach (DecayRad decrad in decayRads)
                    {
                        decrad.Intensity *= member.Value;
                        if (decrad.Intensity < intensity)
                        {
                            continue;
                        }

                        AddRow(decrad);
                        shown++;
                    }
                }

                RestoreSorting();
                if (refusedMembers > 0)
                {
                    status.AddFailure(Resources.NucBase_ChainLinesRefused, firstReason, refusedMembers);
                }

                if (shown > 0)
                {
                    status.Add(string.Format(CultureInfo.InvariantCulture, Resources.NucBase_SearchFound, shown));
                }
                else if (refusedMembers == 0)
                {
                    // ⛔ «Ничего не найдено» говорится только когда запросы и
                    // правда прошли. Рядом с отказом эти слова противоречат
                    // ему же — а разводить отказ и пустоту разными словами и
                    // есть смысл строки (`T92`). Пустой запрос (`D44`) —
                    // третий случай, и слова у него тоже свои.
                    status.Add(noCriteria ? OwnText(NoCriteriaKey) : Resources.NucBase_SearchEmpty);
                }
            }

            if (isotope.Length == 0)
            {
                // Имени нет — карточке взяться неоткуда, и прошлая на экране
                // остаться не должна. Вместе с ней уходит и заметка о ней.
                ShowIsotopeCard("", null);
                ShowCardNote(null);
            }
            else
            {
                // Заметка о карточке — ВТОРОЙ слот строки состояния, а не часть
                // итога поиска (`T96`). Прежде она подклеивалась сюда, в `status`,
                // и та же заметка от щелчка по строке затирала весь итог целиком.
                ShowCardNote(ShowCardFor(isotope));
            }

            SetSearchStatus(status);
            UpdateNuclideDefinitionControlsState();
        }

        /// <summary>
        /// Символ элемента, если в запросе нет массового числа: «w» -&gt; «W»,
        /// «PB» -&gt; «Pb». Иначе null — искать надо нуклид, как и раньше.
        ///
        /// Регистр приводится здесь, а не в поиске нуклида: тот всё поднимает в
        /// верхний («137CS»), а символ элемента пишется «Pb», и по «PB» в
        /// таблице ничего не найдётся.
        /// </summary>
        public static string ElementSymbol(string query)
        {
            string unusedError;
            return ElementSymbol(query, out unusedError);
        }

        /// <summary>
        /// То же, но с причиной отказа БАЗЫ ВЕЩЕСТВ (`A25`).
        ///
        /// ⛔ Различить «это не элемент» и «спросить было не у кого» обязан
        /// вызывающий, и без выходного параметра он этого сделать не может:
        /// оба случая дают <c>null</c>. Молча свалиться в ветку поиска нуклида
        /// при отказе базы нельзя — по «W» там не найдётся ничего, и человек
        /// прочитает «Ничего не найдено» вместо правды.
        ///
        /// Перегрузка без параметра оставлена ради читателей, которым причина не
        /// нужна (<c>XrayLinesProbe</c> сверяет только разбор).
        /// </summary>
        public static string ElementSymbol(string query, out string error)
        {
            error = null;
            string letters = Regex.Match(query ?? "", @"^[a-zA-Z]{1,2}$").Value;
            if (letters.Length == 0)
            {
                return null;
            }

            string symbol = letters.Substring(0, 1).ToUpperInvariant()
                            + letters.Substring(1).ToLowerInvariant();
            return NucBaseFramework.ElementNumber(symbol, out error) > 0 ? symbol : null;
        }

        /// <summary>
        /// Карточка нуклида: заполнить прочитанным или ОЧИСТИТЬ (nuc = null).
        ///
        /// ⛔ Метод один на все три места, где карточку показывают (`T92`).
        /// Раньше карточку ЗАПОЛНЯЛИ в трёх местах, и ни одно из этих трёх её
        /// не очищало: когда нуклид не читался, каждое просто выходило — и на
        /// экране оставался ПРЕДЫДУЩИЙ нуклид, чьи Z, N, период, родители и
        /// дочки выглядели принадлежащими выбранному. Пустой список хотя бы
        /// виден; чужая карточка выглядит настоящей.
        ///
        /// ⚠ Очистка в дереве БЫЛА — отдельным методом `ClearIsotopeCard`, но
        /// звали его только из ветки запроса про элемент, а не с путей отказа.
        /// Он снят, и его довод переехал сюда: карточка — про распад, а у
        /// элемента распада нет; оставленная от прошлого поиска, она подписала
        /// бы рентген вольфрама периодом полураспада того, кого искали до него.
        /// </summary>
        private void ShowIsotopeCard(string isotope, Nuclide nuc)
        {
            this.lastElementError = null;
            this.IsotopeNameLabel.Text = isotope ?? "";
            this.ParentsDataGridView.Rows.Clear();
            this.DaughtersDataGridView.Rows.Clear();
            if (nuc == null)
            {
                // Номер элемента остаётся и у пустой карточки — ради запроса о
                // характеристическом рентгене («W»): нуклида там нет вовсе, а
                // Z есть. У ненайденного нуклида (`232TH`) `ZOf` даёт 0, и
                // тогда в поле пусто: ноль в графе «Z» — неправда, а не пробел.
                //
                // ⛔ И ЗДЕСЬ ЖЕ УМИРАЛ ВЕСЬ ЗАПРОС (`A25`). Строкой ниже стоял
                // прямой `MaterialDatabase.ZOf`, а он тянет чтение базы веществ:
                // измерено 03.09.2026 на каталоге без `<проба>.exe.config` —
                // `TypeInitializationException` из поставщика SQLite уходил
                // отсюда наружу через `ShowCardFor` и `DoSearch`, процесс умирал
                // кодом −532462766. Причина теперь не бросок, а значение
                // (`NucBaseFramework.ElementNumber`), и её договаривает
                // <see cref="ShowCardFor"/> — в ту же строку состояния.
                int z = NucBaseFramework.ElementNumber(isotope ?? "", out this.lastElementError);
                this.IsotopeZLabel.Text = z > 0 ? z.ToString(CultureInfo.InvariantCulture) : "";
                this.IsotopeNLabel.Text = "";
                this.IsotopeHLLabel.Text = "";
                this.IsotopeSpecActivity.Text = "";
                this.IsotopeAbundance.Text = "";
                return;
            }

            this.IsotopeZLabel.Text = nuc.Z.ToString(CultureInfo.InvariantCulture);
            this.IsotopeNLabel.Text = nuc.N.ToString(CultureInfo.InvariantCulture);
            // ⛔ ПОДПИСЬ СКЛАДЫВАЕТ `NucBaseFramework`, А НЕ ЭТА СТРОКА
            // (`A304`): у 81 нуклида из 4429 период в поставке — не измерение,
            // а граница сверху, и перед ним обязан стоять знак «>» (решение
            // Amber 10.09.2026). Правило показа вынесено туда, чтобы его
            // проверял безоконный читатель: окно `BecqMoni` в проверке не
            // поднимают.
            this.IsotopeHLLabel.Text = NucBaseFramework.HalfLifeCaption(nuc);
            // ⛔ И ЗДЕСЬ ПОДПИСЬ СКЛАДЫВАЕТ `NucBaseFramework`, А НЕ ЭТА СТРОКА
            // (`A304`, решение Amber 10.09.2026: «Ставить „<“ тем же
            // признаком»). Знак ПРОТИВОПОЛОЖЕН знаку строкой выше: период
            // стоит в знаменателе активности, поэтому граница снизу у периода
            // — это граница СВЕРХУ у активности.
            this.IsotopeSpecActivity.Text = NucBaseFramework.SpecificActivityCaption(nuc);
            this.IsotopeAbundance.Text = nuc.Abundance.ToString(CultureInfo.InvariantCulture) + " %";

            // ⛔ (П296, 14.10.2026) СТРОКИ С ДОЛЕЙ РОВНО 0 НЕ ПОКАЗЫВАЮТСЯ — ни у дочерних, ни у
            // родителей (то же ребро глазами дочери). Решение Amber 14.10.2026 вопросником, дословно:
            // «Скрывать строки с долей 0 в карточке (Рекомендую)». Только показ: список
            // `Nuclide.Daughters`/`Parents` и правило ряда не тронуты, база не менялась — у `130SN`,
            // `52FE`, `85Y` нулевая строка держит ряд от двоения (журнал П294), у 25 строк поставки
            // нуль — «доля неизвестна». Читателей списка в приложении один — этот метод.
            foreach (Decay parent in nuc.Parents)
            {
                if (NucBaseFramework.IsZeroShare(parent.DecayPercent))
                {
                    continue;
                }

                this.ParentsDataGridView.Rows.Add(parent.NucName, parent.DecayTypeString, parent.DecayPercent);
            }

            foreach (Decay daughter in nuc.Daughters)
            {
                if (NucBaseFramework.IsZeroShare(daughter.DecayPercent))
                {
                    continue;
                }

                this.DaughtersDataGridView.Rows.Add(daughter.NucName, daughter.DecayTypeString, daughter.DecayPercent);
            }
        }

        /// <summary>
        /// Прочитать нуклид и показать его карточку — или очистить её, если
        /// показывать нечего.
        ///
        /// Возвращает то, что надо СКАЗАТЬ человеку, когда карточки не будет, и
        /// ПУСТУЮ заметку, когда она показана. Слова у двух причин РАЗНЫЕ
        /// (`T92`): «в таблице такой строки нет либо период не измерен» — это
        /// законный ответ базы, а «прочитать не удалось» — отказ, и признак
        /// <see cref="StatusNote.Failed"/> взводится только у второго.
        /// </summary>
        private StatusNote ShowCardFor(string isotope)
        {
            NucBaseFramework fw = new NucBaseFramework();
            Nuclide nuc = fw.getNuclude(isotope);
            // ⚠ `ShowIsotopeCard` взводит `lastElementError`, поэтому читать его
            // надо ПОСЛЕ вызова и до следующего — так же, как `LastError`
            // у самого `NucBaseFramework`.
            ShowIsotopeCard(isotope, nuc);
            string elementError = this.lastElementError;

            StatusNote note = new StatusNote();
            if (nuc == null)
            {
                if (fw.LastError != null)
                {
                    note.AddFailure(Resources.NucBase_IsotopeFetchError, fw.LastError, isotope);
                }
                else
                {
                    note.Add(string.Format(Resources.NucBase_CardEmpty, isotope));
                }
            }

            if (elementError != null)
            {
                // ⛔ Отказ базы ВЕЩЕСТВ — своя причина и свои слова (`A25`): графа
                // «Z» осталась пустой не потому, что такого элемента нет, а потому,
                // что справиться было негде. Прежде этот отказ вылетал наружу
                // броском и убивал весь запрос.
                note.AddFailure(Resources.NucBase_ElementDataFetchError, elementError);
            }

            return note;
        }

        /// <summary>
        /// Строка состояния под таблицами — ЕДИНСТВЕННОЕ место, где редактор
        /// говорит человеку, чем кончился запрос (`T92`).
        ///
        /// ⛔ Диалогом отказ здесь не показывают: `DoSearch` гоняет безоконная
        /// проба (`ChainProbe.CheckSearch`), и модальное окно вставало бы в ней
        /// насмерть — этим уже заплачено (`D42`, `T98`). Метка видна всегда,
        /// ждать кнопки не заставляет и читается прогоном так же, как глазами.
        ///
        /// Цвет — вторая подсказка, не первая: отказ отличается СЛОВАМИ, а
        /// краснота только помогает его заметить.
        ///
        /// ⛔ У метки ДВА СЛОТА, И ЭТО НЕ УКРАШЕНИЕ (`T96`). Сообщения у неё два
        /// и приходят они с разных путей: итог поиска — из <see cref="DoSearch"/>,
        /// заметка о карточке — ещё и из <see cref="ResultDataGridView_CellClick"/>
        /// с <see cref="ResultDataGridView_CellEnter"/>, то есть с КАЖДОЙ ячейки,
        /// по которой прошли стрелками. Пока слот был один, второе сообщение
        /// затирало первое целиком — измерено 27.08.2026: после «Найдено линий: 5.»
        /// щелчок по строке `148EUm1` оставлял в метке только «Карточки для
        /// 148EUm1 нет: …», и итог не возвращался до следующего поиска. Терялся
        /// он ровно тогда, когда сказать было что ОБОИМ.
        /// </summary>
        private void SetSearchStatus(StatusNote note)
        {
            this.searchNote = note ?? new StatusNote();
            RenderStatus();
        }

        /// <summary>
        /// Второй слот той же метки: заметка о карточке нуклида. Пустая строка
        /// или <c>null</c> слот ОЧИЩАЮТ — заметка о прошлой строке таблицы к
        /// новой не относится.
        /// </summary>
        private void SetCardStatus(StatusNote note)
        {
            this.cardNote = note ?? new StatusNote();
            RenderStatus();
        }

        /// <summary>
        /// Собрать метку из двух слотов. Пустой слот места не занимает — иначе
        /// у сообщения появлялась бы пустая строка сверху или снизу.
        ///
        /// ⛔ ПРИЧИНА НАЗЫВАЕТСЯ РОВНО ОДИН РАЗ (`A92`). Отказ базы виден сразу
        /// нескольким наблюдателям — обходу ряда, сбору линий его членов и
        /// карточке нуклида, — и каждый говорил о нём СВОИМИ словами, доклеивая
        /// к ним ПОЛНЫЙ текст одной и той же причины. Измерено 04.09.2026 на
        /// сцене `F_a25_noconf` (каталог без <c>&lt;проба&gt;.exe.config</c>,
        /// поставщик SQLite не поднимается): в метке стояло 4699 знаков, и
        /// 1160 из них — два лишних повтора причины про <c>nucdb.sqlite</c>.
        /// Читать такую метку нельзя: перечень отказавшего тонет в трёх копиях
        /// одного абзаца.
        ///
        /// Поэтому слоты держат ЧАСТИ и ПРИЧИНЫ врозь (<see cref="StatusNote"/>):
        /// сперва идёт перечень того, что отказало, затем — каждая РАЗЛИЧНАЯ
        /// причина по одному разу, подписью <c>ERRFailureReason</c>. Ни одно
        /// слово из состава сообщения при этом не теряется.
        /// </summary>
        private void RenderStatus()
        {
            List<string> lines = new List<string>();
            lines.AddRange(this.searchNote.Parts);
            lines.AddRange(this.cardNote.Parts);

            List<string> reasons = new List<string>();
            foreach (string reason in this.searchNote.Reasons.Concat(this.cardNote.Reasons))
            {
                // Слоты заполняются с РАЗНЫХ путей (`T96`), и одинаковая причина
                // приходит в них по отдельности: сверять надо после слияния, а
                // не внутри каждого.
                if (!reasons.Contains(reason))
                {
                    reasons.Add(reason);
                }
            }

            foreach (string reason in reasons)
            {
                lines.Add(string.Format(Resources.ERRFailureReason, reason));
            }

            this.SearchStatusLabel.Text = string.Join(Environment.NewLine, lines);
            this.SearchStatusLabel.ForeColor = this.searchNote.Failed || this.cardNote.Failed
                ? System.Drawing.Color.Firebrick
                : System.Drawing.SystemColors.ControlText;
        }

        /// <summary>Итог последнего поиска и был ли он отказом (`T96`).</summary>
        private StatusNote searchNote = new StatusNote();

        /// <summary>Заметка о последней показанной карточке (`T96`).</summary>
        private StatusNote cardNote = new StatusNote();

        /// <summary>
        /// Сообщение строки состояния: ЧТО отказало (<see cref="Parts"/>) и
        /// ПОЧЕМУ (<see cref="Reasons"/>) — ВРОЗЬ (`A92`).
        ///
        /// ⛔ Порознь они лежат затем, что причина у нескольких частей одного
        /// отказа ОДНА, а складывалась она в метку столько раз, сколько было
        /// частей. Резать метку по длине было бы лечением следствия: длинна она
        /// не потому, что причина длинная, а потому, что причина повторена.
        ///
        /// ⚠ Признак <see cref="Failed"/> взводится САМОЙ укладкой отказа
        /// (<see cref="AddFailure"/>), а не отдельным присваиванием: прежде это
        /// были две строки в шести местах, и забыть вторую ничего не стоило.
        /// Прежнее поле `lastCardFailed` снято — оно держало ровно этот признак
        /// у карточки, и теперь он едет вместе с самим сообщением.
        /// </summary>
        private sealed class StatusNote
        {
            /// <summary>Что отказало (или чем кончился удачный запрос) — без причин.</summary>
            public readonly List<string> Parts = new List<string>();

            /// <summary>Причины, каждая по одному разу и в порядке появления.</summary>
            public readonly List<string> Reasons = new List<string>();

            /// <summary>Был ли среди частей отказ: от него краснота метки.</summary>
            public bool Failed;

            public void Add(string text)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    this.Parts.Add(text);
                }
            }

            /// <summary>
            /// Сложить сообщение об отказе: слова — в <see cref="Parts"/>,
            /// причину — в <see cref="Reasons"/>, и только если её там ещё нет.
            ///
            /// Причина у шаблона всегда ПОСЛЕДНИЙ довод, поэтому она и стоит
            /// вторым параметром: остальные доводы (<paramref name="args"/>)
            /// уходят в текст, как уходили.
            /// </summary>
            public void AddFailure(string template, string reason, params object[] args)
            {
                this.Failed = true;
                Add(WithoutReason(template, args));
                if (!string.IsNullOrEmpty(reason) && !this.Reasons.Contains(reason))
                {
                    this.Reasons.Add(reason);
                }
            }
        }

        /// <summary>
        /// Текст сообщения БЕЗ причины: шаблон обрезается по месту, где в нём
        /// стоит причина (`A92`).
        ///
        /// ⛔ Обрезается ШАБЛОН, а не готовая строка, и это не изящество:
        /// причина приходит от платформы и содержит что угодно — точки,
        /// двоеточия, пути, чужие сообщения, — а искать её в готовом тексте
        /// значило бы искать иголку, которую сам же туда и положил. Место
        /// причины известно точно: она ПОСЛЕДНИЙ довод шаблона, то есть
        /// «{N}», где N — число остальных доводов.
        ///
        /// ⚠ Вводное предложение, у которого отобрали то, что оно вводит,
        /// снимается целиком: «Дочерние нуклиды {0} прочитаны не все.
        /// Сообщение:» без сообщения — не текст. Признак вводного — ХВОСТОВОЕ
        /// двоеточие; режется по ближайшей точке слева, и правило это работает
        /// в обеих культурах («… Message:» у того же ключа по-английски).
        /// </summary>
        static string WithoutReason(string template, params object[] args)
        {
            int given = args == null ? 0 : args.Length;
            string text = template ?? "";
            int cut = text.IndexOf("{" + given.ToString(CultureInfo.InvariantCulture) + "}",
                                   StringComparison.Ordinal);
            if (cut >= 0)
            {
                text = text.Substring(0, cut);
            }

            text = text.TrimEnd();
            if (text.EndsWith(":", StringComparison.Ordinal))
            {
                int stop = text.LastIndexOfAny(SentenceEnd);
                text = stop >= 0
                    ? text.Substring(0, stop + 1)
                    : text.Substring(0, text.Length - 1).TrimEnd();
            }

            return given == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
        }

        static readonly char[] SentenceEnd = { '.', '!', '?' };

        private void UpdateNuclideDefinitionControlsState()
        {
            bool hasRows = this.ResultDataGridView.Rows.Count > 0;

            buttonImportDef.Enabled = hasRows;
            checkBoxOverwriteDef.Enabled = hasRows;
            checkBoxAppendRootName.Enabled = IncludeDecayChainCheckBox.Checked;
            checkBoxAppendRootName.Checked = IncludeDecayChainCheckBox.Checked;
            comboBoxNameFormat.Enabled = hasRows;
            labelNameFormat.Enabled = hasRows;
        }

        /// <summary>
        /// Подпись определения для линии характеристического рентгена: «W» -&gt;
        /// «W x-ray». Массового числа в ней нет и быть не может — по этому и
        /// отличают рентген от нуклида те, кто читает файл определений
        /// (см. <see cref="NuclideDefinition.IsElementXrayName"/>).
        /// </summary>
        public static string XrayDefinitionName(string symbol)
        {
            return (symbol ?? "").Trim() + " " + XrayNameSuffix;
        }

        /// <summary>
        /// Период полураспада в годах из ячейки таблицы вида «5.75(Y)».
        ///
        /// Вынесено из обработчика импорта вместе с <see cref="XrayDefinitionName"/>:
        /// форму можно собрать и без главного окна, но импорт кончается модальным
        /// сообщением, и проба на нём повисла бы. У рентгена периода нет вовсе —
        /// в ячейке ноль, и разбор обязан его пережить, а не уронить весь импорт.
        /// </summary>
        public static double HalfLifeYearsFromCell(string cell)
        {
            string[] parts = (cell ?? "").Split('(');
            double value;
            // ⛔ ИНВАРИАНТ (`A242`): ячейку пишет `AddDecayRadRow` выше, тоже
            // инвариантом. Пара «печать → разбор» обязана меняться разом, иначе
            // период полураспада меняется молча — измерено 05.09.2026: на
            // `ru-RU` разбор ОТКАЗЫВАЕТ и оставляет ноль, на `de-DE` точка
            // сходит за разделитель тысяч и «5.75» становится 575.
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            // Take the full unit, not the first character: Substring(0,1) turned
            // "ms" into "m" (minutes, a x60000 error) and "us"/"ns" into unknown units.
            string unit = parts.Length > 1 ? parts[1].TrimEnd(')') : "s";
            return ConvertHalfLifeToSeconds(value, unit) / 31536000;
        }

        private static double ConvertHalfLifeToSeconds(double value, string unit)
        {
            double coeff;

            switch (unit)
            {
                case "s":
                    coeff = 1;
                    break;
                case "m":
                    coeff = 60;
                    break;
                case "h":
                    coeff = 3600;
                    break;
                case "d":
                    coeff = 86400;
                    break;
                case "Y":
                    coeff = 31536000;
                    break;
                case "ms":
                    coeff = 1.0 / 1000.0;
                    break;
                case "us":
                    coeff = 1.0 / 1000000.0;
                    break;
                case "ns":
                    coeff = 1.0 / 1000000000.0;
                    break;
                default:
                    coeff = 1.0;
                    break;
            }
            
            return coeff * value;
        }

        private void AddRow(DecayRad decrad)
        {
            // TODO: use data binding?
            // Галочка стоит у того, за чем пришли: у гамма-линий распада и у
            // всех линий рентгена, когда искали именно рентген элемента.
            bool isGamma = decrad.DecayLine == "G"
                           || decrad.DecayLine == NucBaseFramework.FluorescenceLine;
            // ⛔ (`AMBER274`, полоса `f8diag` 02.11.2026; решение Amber «Откатить и отсеивать по
            // признаку») ВЫХОД-ПРЕДЕЛ (`LT`/`LE`) — НЕ ВЫХОД: строка показывается («< 0.075»,
            // серым), но к импорту НЕ помечается. Импортированная, она легла бы в библиотеку
            // выходом, и активность по этой линии вышла бы занижена во столько раз, во сколько
            // настоящий выход ниже предела, — без единого слова. Взять её человек вправе, как и
            // лишнюю половину Kβ (`D33`), но только нарочно; номер строки (`DbRow`, `AMBER281`) у
            // неё пишется так же, как у прочих, и сверка с базой идёт тем же путём.
            bool checkedByDefault = isGamma && !decrad.IntensityIsLimit;
            // ⛔ Kβ лежит в базе ДВАЖДЫ — итогом `KB` и разложением `KpB1`+`KpB2`
            // (`D33`). У лишней при сложении половины в колонке серии стоит знак
            // суммы: складывать её с соседями значит считать Kβ дважды, на
            // Lu-176 это 40.53 % вместо 33.49 %.
            //
            // ⚠ Галочку здесь снимать не надо и НЕ НАДО ПИСАТЬ КОД, который её
            // снимает: у линий распада типа `X` она и так не ставится — стоит
            // она у гамм и у рентгена ЭЛЕМЕНТА, когда искали именно его. Ветка
            // «если лишняя, снять» была бы кодом, который никогда не работает.
            string series = decrad.XrayType + (decrad.Redundant ? DecayRad.RedundantMark : "");
            // ⛔ ИНВАРИАНТНАЯ КУЛЬТУРА, И ВТОРАЯ ПОЛОВИНА ЭТОЙ ПРАВКИ —
            // `HalfLifeYearsFromCell` (`A242`). Ячейка пишется ЗДЕСЬ, а
            // разбирается ТАМ, при импорте в набор нуклидов, и до 05.09.2026 обе
            // стороны брали культуру потока: печать и разбор врали согласованно
            // и потому были незаметны. Починить одну — значит записать «5.75» и
            // прочитать ноль (`ru-RU`) или 575 (`de-DE`). Обе стороны сведены к
            // точке разом.
            string hl = decrad.HalfLife.ToString(CultureInfo.InvariantCulture)
                        + "(" + decrad.HalfLifeUnit + ")";
            int index = this.ResultDataGridView.Rows.Add(checkedByDefault, decrad.Name, decrad.DecayLine, decrad.Energy, decrad.Intensity, series, decrad.DecayTypeString, hl);
            // (`AMBER281`) Строка базы, из которой взята строка таблицы: импорт пишет её
            // номер (`dr_pk`, у рентгена элемента — элемент и серию) в линию библиотеки.
            this.ResultDataGridView.Rows[index].Tag = decrad;
            // (решение Amber 07.11.2026 «Округлять в таблице») в ячейке — четыре значащие цифры,
            // полное число — подсказкой ячейки
            this.ResultDataGridView.Rows[index].Cells[IntencityColumnIdx].ToolTipText =
                IntensityFullCaption(decrad.Intensity, decrad.IntensityIsLimit);
            if (decrad.Redundant)
            {
                DataGridViewRow added = this.ResultDataGridView.Rows[index];
                // (`AMBER151`) У L-серии своя подсказка: лишней там стоит сводная
                // `L` (или итог подоболочки) рядом с подлиниями, а не половина Kβ.
                added.Cells[SeriesColumnIdx].ToolTipText = AppUi.WrapTip(
                    FullSpectrumAnalysis.FsaSampleLibrary.LSeriesKind(decrad.XrayType)
                        != FullSpectrumAnalysis.FsaSampleLibrary.LSeriesNone
                    ? Resources.NucBase_LSeriesRedundantHint
                    : Resources.NucBase_KSeriesRedundantHint);
                added.DefaultCellStyle.ForeColor = System.Drawing.SystemColors.GrayText;
            }
            else if (decrad.IntensityIsLimit)
            {
                // (`f8diag`) Предел — серым, как лишняя строка серии: не выход.
                this.ResultDataGridView.Rows[index].DefaultCellStyle.ForeColor = System.Drawing.SystemColors.GrayText;
            }
        }

        public void CallSearch(decimal energy)
        {
            double delta = 10;
            double lowenergy = (double)energy - delta;
            double highenergy = (double)energy + delta;
            if (lowenergy < 0)
            {
                lowenergy = 0;
            }

            this.LowEnrgTextBox.Text = lowenergy.ToString(CultureInfo.InvariantCulture);
            this.HighEnrgTextBox.Text = highenergy.ToString(CultureInfo.InvariantCulture);

            DoSearch();
        }

        private void ResultDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                if (e.ColumnIndex == CheckedColumnIdx)
                {
                    ToggleSelection();
                }

                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[NameColumnIdx].Value.ToString();
            // ⛔ Карточка чистится в любом случае — и прежде всего когда
            // нуклида не нашлось (`T92`). Здесь стоял выход с примечанием
            // «Stable isotope (no half-life row) — nothing to display», и оно
            // было неверно дважды: у стабильных период есть строкой `STABLE`,
            // отбор их пропускает и карточка у них показывается (`D42`), а
            // «нечего показывать» на деле означало «остаётся карточка
            // предыдущего нуклида».
            ShowCardNote(ShowCardFor(isotope));
        }

        /// <summary>
        /// Сказать о карточке то, что вернул <see cref="ShowCardFor"/>, — во
        /// ВТОРОЙ слот строки состояния (`T96`).
        ///
        /// ⛔ <c>null</c> здесь — не «промолчать», а «снять прежнюю заметку»:
        /// карточка показалась, и старые слова про то, что её нет, ложь. Прежде
        /// метод на <c>null</c> не делал ничего, и заметка о нечитаемой строке
        /// оставалась висеть, пока человек листал СЛЕДУЮЩИЕ, читаемые. Молчание
        /// же было единственным, что берегло итог поиска, — а берёг он его лишь
        /// пока сказать было нечего.
        /// </summary>
        private void ShowCardNote(StatusNote note)
        {
            SetCardStatus(note);
        }

        /// <summary>
        /// ПРИЧИНА ОТКАЗА БАЗЫ ВЕЩЕСТВ у последней показанной карточки, или
        /// <c>null</c> (`A25`). Пишет <see cref="ShowIsotopeCard"/> — она одна
        /// эту базу и трогает, — читает <see cref="ShowCardFor"/> сразу после
        /// вызова, до следующего; так же читаются
        /// <c>NucBaseFramework.LastError</c> и <c>LastNoCriteria</c>.
        ///
        /// Признак нужен затем, что <see cref="ShowIsotopeCard"/> ничего не
        /// возвращает, а сказать человеку надо: пустая графа «Z» при отказе
        /// базы неотличима от пустой графы у нуклида, которого в таблице
        /// элементов нет.
        /// </summary>
        private string lastElementError;

        private void ToggleSelection()
        {
            this.ResultDataGridView.SuspendLayout();
            DataGridViewColumn checkCol = this.ResultDataGridView.Columns[CheckedColumnIdx];
            checkCol.HeaderText = checkCol.HeaderText == "X"
                ? ""
                : "X";

            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                row.Cells[CheckedColumnIdx].Value = checkCol.HeaderText == "X";
            }
            this.ResultDataGridView.RefreshEdit();
            this.ResultDataGridView.ResumeLayout();
        }

        private void IsotopeTextBox_TextChanged(object sender, EventArgs e)
        {
            if (this.IsotopeTextBox.Text.Length == 0)
            {
                this.IncludeDecayChainCheckBox.Enabled = false;
                this.IncludeDecayChainCheckBox.Checked = false;
            } else
            {
                this.IncludeDecayChainCheckBox.Enabled = true;
            }
        }

        private void RestoreSorting()
        {
            ListSortDirection direction;
            if (this.ResultDataGridView.SortOrder == SortOrder.Ascending) direction = ListSortDirection.Ascending;
            else direction = ListSortDirection.Descending;
            if (this.ResultDataGridView.SortedColumn != null)
            {
                this.ResultDataGridView.Sort(this.ResultDataGridView.SortedColumn, direction);
            }
        }

        Form mainForm;

        private void IsotopeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void IntencityTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void HalfLifeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void LowEnrgTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void HighEnrgTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void ResultDataGridView_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[NameColumnIdx].Value.ToString();
            // Та же карточка и то же правило, что при щелчке по строке: показать
            // или очистить, а об отказе сказать (`T92`).
            ShowCardNote(ShowCardFor(isotope));
        }

        private void IsotopeTextBox_Enter(object sender, EventArgs e)
        {
            int DisplayTime = 10000;
            string tip = IsotopeTip();
            this.toolTip1.Show(tip, this.IsotopeTextBox, 0, IsotopeTipDrop(this.IsotopeTextBox), DisplayTime);
        }

        /// <summary>
        /// (`AMBER299`, проверка экраном 07.11.2026) Подсказка поля «Изотоп» — блоком по
        /// <see cref="AppUi.TipWidth"/> знаков (правило Amber 23.10.2026). <c>Show</c> обходит
        /// <see cref="AppUi.WrapToolTips"/> конструктора, и подсказка шла одной строкой ~140 знаков
        /// во всю ширину окна. Открыта пробе: приёмка читает ТОТ ЖЕ текст, что окно.
        /// </summary>
        internal static string IsotopeTip()
        {
            return AppUi.WrapTip(Resources.NucBase_IsotopeTextBoxTooltip1);
        }

        /// <summary>
        /// (критик `critUi2`, 07.11.2026) Подсказка ставится ПОД полем: поле стоит у верхнего края
        /// окна, и блок в две строки над ним (прежние −23 и поднятие на высоту текста) закрывал
        /// заголовок окна и подпись группы «Поиск изотопа» (снимок полосы, `13`). Под полем —
        /// шапка таблицы результата; поле и заголовок окна открыты.
        /// </summary>
        internal static int IsotopeTipDrop(Control box)
        {
            return box.Height + 2;
        }

        /// <summary>
        /// Импорт помеченных строк таблицы в библиотеку нуклидов.
        ///
        /// ⛔ (П328, 15.10.2026, `A336`) СПРОСИТЬ ПЕРЕД ЗАПИСЬЮ, если открыт
        /// редактор нуклидов с несохранёнными правками, — тем же приёмом, что
        /// «Сохранить» редактора наборов (П326). Решение Amber вопросником
        /// 15.10.2026, дословно: «Да, тем же приёмом (Рекомендую)».
        ///
        /// Библиотека лежит в одном файле, и
        /// <see cref="NuclideDefinitionManager.SaveDefinitionFile"/> пишет всё, что
        /// в памяти; редактор нуклидов немодален, его правки ячеек и галки
        /// «Видимый» живут в линиях до его «Сохранить». Прежде импорт уносил их в
        /// файл молча, а окно нуклидов продолжало считать их несохранёнными (его
        /// «Нет» потом откатывал память, файл оставался с правкой). Ответы:
        ///  * «Да» — правки окна нуклидов записываются, как его собственной
        ///    кнопкой «Сохранить» (окно становится «сохранено»), затем импорт;
        ///  * «Нет» — импорт записывается так, будто правок окна нуклидов нет:
        ///    составлен он по линиям ДО правок окна и записан с ними же
        ///    (<see cref="NuclideDefinitionForm.WriteWithoutUnsavedEdits"/>);
        ///    правки остаются в окне несохранёнными, его «Нет» вернёт память к
        ///    файлу, импортированное не трогая;
        ///  * «Отмена» — импорта нет: ни память, ни файл не тронуты.
        /// Поэтому импорт сперва СОСТАВЛЯЕТСЯ (<see cref="PlanImport"/>), не трогая
        /// память, и применяется только после ответа.
        ///
        /// (П328, попутно, `A9`) Отказ записи откатывает импорт в памяти — как у
        /// заведения и удаления линий в редакторе нуклидов. Прежде линии
        /// оставались в памяти без файла (`AMBER201`: «линии лишь в памяти»):
        /// разметка пиков и FSA видели их до перезапуска, а ближайшая запись
        /// библиотеки любым окном уносила их в файл молча.
        /// Проба — <c>P328NucBaseImportAsksProbe</c>, журнал П328.
        /// </summary>
        private void buttonImportDef_Click(object sender, EventArgs e)
        {
            try
            {
                NuclideDefinitionManager defManager = NuclideDefinitionManager.GetInstance();
                ImportPlan plan = this.PlanImport(defManager);
                if (plan.IsEmpty)
                {
                    return;
                }

                List<NuclideDefinitionForm> editors = NuclideDefinitionForm.OpenWithUnsavedEdits();
                bool withoutEditorsEdits = false;
                if (editors.Count > 0)
                {
                    DialogResult answer = MessageBox.Show(this, Resources.MSGNucBaseImportUnsavedNuclides,
                                                          Resources.ConfirmationDialogTitle,
                                                          MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);
                    if (answer == DialogResult.Yes)
                    {
                        foreach (NuclideDefinitionForm editor in editors)
                        {
                            // Отказ (нечисло в поле — окно «Введите число», файл
                            // занят — окно менеджера): импорта нет, как при «Отмене».
                            if (!editor.SaveTogether())
                            {
                                return;
                            }
                        }

                        // Перенос полей окна мог сменить имя или энергию линии —
                        // импорт составляется заново, по записанному.
                        plan = this.PlanImport(defManager);
                    }
                    else if (answer == DialogResult.No)
                    {
                        // Импорт «будто правок окна нет»: и составлен по линиям до
                        // правок (переименованная окном линия иначе не узнаётся и
                        // заводится в файл второй раз), и записан с ними.
                        ImportPlan clean = null;
                        RunWithoutEditorsEdits(editors, 0, () =>
                        {
                            clean = this.PlanImport(defManager);
                            return true;
                        });
                        plan = clean;
                        withoutEditorsEdits = true;
                    }
                    else
                    {
                        return;
                    }
                }

                if (!plan.IsEmpty)
                {
                    plan.Apply(defManager);
                    // (`AMBER201`, 5.9) Ответ записи читается: при отказе окно
                    // об ошибке уже показал менеджер, и следом за ним «импорт
                    // удался» было бы неправдой. (П328) Отказ откатывает импорт.
                    bool written = withoutEditorsEdits
                        ? RunWithoutEditorsEdits(editors, 0, defManager.SaveDefinitionFile)
                        : defManager.SaveDefinitionFile();
                    if (!written)
                    {
                        plan.Revert(defManager);
                        return;
                    }

                    // ⛔ (П341, `A338`) Открытое окно «Редактировать изотоп» строит
                    // таблицу только само и импортированных линий не показывало до
                    // своего «Сохранить», «Новый» или «Удалить». Показ — после
                    // записи и до сообщения: человек читает «Добавлено - N» при
                    // окне, где эти N уже видны. Отказ записи и «Отмена» сюда не
                    // доходят — библиотека та же, что в окне.
                    NuclideDefinitionForm.ShowLibraryChangeInOpen();

                    // ⛔ (`AMBER260`, 22.10.2026) Подписи пиков открытого спектра —
                    // заново, по пополненной библиотеке, тем же вызовом, что у
                    // редактора нуклидов. Прежде импорт поиск пиков не трогал, а
                    // отпечаток FSA (имя, энергия, выход линий) сменялся — разбор
                    // «по меткам» пересчитывался сразу, но по СТАРЫМ подписям, и
                    // импортированный нуклид появлялся лишь после перезапуска
                    // (урановое стекло Max: Th-234 в составе не было до перезапуска).
                    this.UpdatePeakDetectionResult();
                }

                string text = string.Format(CultureInfo.InvariantCulture, Resources.NuclideDefImportSuccess, plan.CreatedCount, plan.UpdatedCount);
                if (plan.RedundantSkipped > 0)
                {
                    text += Environment.NewLine + Environment.NewLine
                            + string.Format(CultureInfo.InvariantCulture, Resources.NucBase_KSeriesRedundantSkipped, plan.RedundantSkipped);
                }

                if (plan.RedundantSkippedL > 0)
                {
                    text += Environment.NewLine + Environment.NewLine
                            + string.Format(CultureInfo.InvariantCulture, Resources.NucBase_LSeriesRedundantSkipped, plan.RedundantSkippedL);
                }

                MessageBox.Show(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.NuclideDefImportError, ex.Message + ex.StackTrace));
            }
        }

        /// <summary>
        /// (`AMBER260`) Повторить поиск пиков активного спектра — как
        /// <c>NuclideDefinitionForm.UpdatePeakDetectionResult</c>. Окно без
        /// главного окна (поднятое пробой) пересчитывать не берётся.
        /// </summary>
        void UpdatePeakDetectionResult()
        {
            MainForm main = this.mainForm as MainForm;
            if (main == null)
            {
                return;
            }

            main.UpdateDetectedPeakView();
            if (main.ActiveDocument != null)
            {
                main.ActiveDocument.UpdateEnergySpectrum();
            }
        }

        /// <summary>
        /// (П328) Выполнить <paramref name="act"/>, пока каждое из окон нуклидов
        /// снимает свои несохранённые правки с линий
        /// (<see cref="NuclideDefinitionForm.WriteWithoutUnsavedEdits"/>); после —
        /// правки на месте. Тем же порядком пишет «только наборы» редактор наборов (П326).
        /// </summary>
        static bool RunWithoutEditorsEdits(List<NuclideDefinitionForm> editors, int index, Func<bool> act)
        {
            if (index == editors.Count)
            {
                return act();
            }

            return editors[index].WriteWithoutUnsavedEdits(() => RunWithoutEditorsEdits(editors, index + 1, act));
        }

        /// <summary>
        /// (П328) Что сделает импорт помеченных строк — НЕ трогая библиотеку:
        /// какие линии завести и какие переписать. Правило отбора прежнее
        /// (`D33`, `AMBER151`, `AMBER197`); заводимые линии видны следующим
        /// строкам того же импорта, как и прежде, когда они сразу ложились в
        /// список менеджера.
        /// </summary>
        ImportPlan PlanImport(NuclideDefinitionManager defManager)
        {
            ImportPlan plan = new ImportPlan();
            // Ряд у всех импортируемых линий один — тот, по которому шёл поиск.
            // Пишется НЕЗАВИСИМО от «дописать имя родителя»: та галочка
            // решает, как линия подписана на графике, а поле — на чей
            // распад дан выход. Раньше это было одно и то же, и выключенная
            // галочка молча теряла принадлежность к ряду.
            string chain = this.IncludeDecayChainCheckBox.Checked
                           && !string.IsNullOrEmpty(this.SearchedIsotope)
                ? FormatIsotopeName(this.SearchedIsotope)
                : "";
            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                if ((bool)row.Cells[CheckedColumnIdx].Value == true)
                {
                    // ⛔ Обе половины Kβ вместе НЕ ВЫГРУЖАЮТСЯ (`D33`): это
                    // не запрет взять помеченную строку, а запрет взять её
                    // ВМЕСТЕ с теми, чью сумму она и есть. Молча пропустить
                    // нельзя — считается и говорится вслух.
                    if (IsRedundantSeries(row) && HasCheckedCounterpart(row))
                    {
                        // (`AMBER151`) L-серия считается отдельно: у неё своё
                        // сообщение — лишней там стоит сводная рядом с подлиниями.
                        if (LKind(row) != FullSpectrumAnalysis.FsaSampleLibrary.LSeriesNone)
                        {
                            plan.RedundantSkippedL++;
                            continue;
                        }

                        plan.RedundantSkipped++;
                        continue;
                    }

                    string name = (string)row.Cells[NameColumnIdx].Value;
                    bool fluorescence = NucBaseFramework.FluorescenceLine.Equals(
                        row.Cells[LineColumnIdx].Value as string, StringComparison.Ordinal);
                    // Формат имени — про нуклиды («137CS» -> «Cs-137»), у
                    // символа элемента ему не за что зацепиться. Подпись
                    // складывается своя: «W x-ray». Слово в ней не украшение
                    // — по отсутствию массового числа в имени рентген и
                    // отличается потом от нуклида (NuclideDefinition).
                    string formattedName = fluorescence
                        ? XrayDefinitionName(name)
                        : FormatIsotopeName(name);
                    double energy = (double)row.Cells[EnergyColumnIdx].Value;
                    double intencity = (double)row.Cells[IntencityColumnIdx].Value;
                    double halfLifeYears = HalfLifeYearsFromCell(
                        (string)row.Cells[HalfLifeColumnIdx].Value);

                    if (!fluorescence && IncludeDecayChainCheckBox.Checked
                        && checkBoxAppendRootName.Checked && this.SearchedIsotope != name)
                    {
                        formattedName += " (" + FormatIsotopeName(this.SearchedIsotope) + ")";
                    }

                    // Ряда у рентгена нет: выход дан не на распад родителя, а
                    // долей внутри K-серии, и вписанный сюда родитель означал
                    // бы, что линию можно ставить на вековое равновесие.
                    string rowChain = fluorescence ? "" : chain;

                    // ⛔ (`AMBER197`) Ключ — НУКЛИД И энергия. Прежде одна
                    // энергия: линия другого нуклида на той же энергии (у
                    // двух и более родителей таких 3212 при выходе ≥ 1 %)
                    // без «перезаписать» молча не заводилась, а с ним
                    // чужая запись получала новые имя, выход, период и ряд.
                    string rowIdentity = LineIdentity(name, fluorescence);
                    NuclideDefinition existingDef = defManager.NuclideDefinitions.Concat(plan.Created).FirstOrDefault(
                        def => def.Energy == energy
                               && LineIdentity(def.NuclideName, def.IsElementXray) == rowIdentity);
                    // (`AMBER281`, 02.11.2026) Номер строки базы — в линию: решение Amber
                    // «в изотоп добавляй строку из базы (индекс) чтобы быть точно уверенным в
                    // будущем». У рентгена элемента строки распада нет — ссылка «элемент:серия».
                    DecayRad source = row.Tag as DecayRad;
                    long dbRow = !fluorescence && source != null ? source.DrPk : 0L;
                    string dbXray = fluorescence && source != null
                        ? NuclideDbLink.XrayReference(name, source.XrayType)
                        : "";
                    if (existingDef != null && checkBoxOverwriteDef.Checked)
                    {
                        plan.Overwrite(existingDef, formattedName, intencity, halfLifeYears, rowChain, dbRow, dbXray);
                    }

                    if (existingDef == null)
                    {
                        plan.Created.Add(new NuclideDefinition()
                        {
                            Name = formattedName,
                            Chain = rowChain,
                            Energy = energy,
                            Intencity = intencity,
                            HalfLife = halfLifeYears,
                            Visible = true,
                            NuclideColor = new SerializableColor(System.Drawing.Color.Green),
                            DbRow = dbRow,
                            DbXray = dbXray
                        });
                    }
                }
            }

            return plan;
        }

        /// <summary>
        /// (П328) Составленный импорт: линии, которые заводятся, и правки
        /// существующих («перезаписать»). <see cref="Apply"/> кладёт его в
        /// библиотеку, <see cref="Revert"/> снимает (отказ записи).
        /// </summary>
        sealed class ImportPlan
        {
            public readonly List<NuclideDefinition> Created = new List<NuclideDefinition>();
            readonly List<ImportOverwrite> overwrites = new List<ImportOverwrite>();
            public int UpdatedCount;
            public int RedundantSkipped;
            public int RedundantSkippedL;

            public int CreatedCount
            {
                get { return this.Created.Count; }
            }

            public bool IsEmpty
            {
                get { return this.Created.Count == 0 && this.UpdatedCount == 0; }
            }

            /// <summary>
            /// Переписать линию. Линия этого же импорта (ещё не в библиотеке)
            /// правится сразу — в библиотеку она ляжет уже такой; линия
            /// библиотеки — при <see cref="Apply"/>.
            /// </summary>
            public void Overwrite(NuclideDefinition target, string name, double intencity, double halfLife, string chain,
                                  long dbRow, string dbXray)
            {
                this.UpdatedCount++;
                ImportOverwrite change = new ImportOverwrite
                {
                    Target = target, Name = name, Intencity = intencity, HalfLife = halfLife, Chain = chain,
                    DbRow = dbRow, DbXray = dbXray
                };
                if (this.Created.Any(created => ReferenceEquals(created, target)))
                {
                    change.Put();
                    return;
                }

                this.overwrites.Add(change);
            }

            public void Apply(NuclideDefinitionManager manager)
            {
                foreach (ImportOverwrite change in this.overwrites)
                {
                    change.Remember();
                    change.Put();
                }

                manager.NuclideDefinitions.AddRange(this.Created);
            }

            public void Revert(NuclideDefinitionManager manager)
            {
                manager.NuclideDefinitions.RemoveAll(def => this.Created.Any(created => ReferenceEquals(created, def)));
                for (int i = this.overwrites.Count - 1; i >= 0; i--)
                {
                    this.overwrites[i].Undo();
                }
            }
        }

        /// <summary>(П328) Правка линии импортом: новые имя, выход, период, ряд — и прежние, для отката.</summary>
        sealed class ImportOverwrite
        {
            public NuclideDefinition Target;
            public string Name;
            public double Intencity;
            public double HalfLife;
            public string Chain;
            /// <summary>(`AMBER281`) Номер строки базы и ссылка рентгена элемента — тоже переписываются.</summary>
            public long DbRow;
            public string DbXray;
            string oldName;
            double oldIntencity;
            double oldHalfLife;
            string oldChain;
            long oldDbRow;
            string oldDbXray;

            public void Remember()
            {
                this.oldName = this.Target.Name;
                this.oldIntencity = this.Target.Intencity;
                this.oldHalfLife = this.Target.HalfLife;
                this.oldChain = this.Target.Chain;
                this.oldDbRow = this.Target.DbRow;
                this.oldDbXray = this.Target.DbXray;
            }

            public void Put()
            {
                this.Target.Name = this.Name;
                this.Target.Intencity = this.Intencity;
                this.Target.HalfLife = this.HalfLife;
                this.Target.Chain = this.Chain;
                this.Target.DbRow = this.DbRow;
                this.Target.DbXray = this.DbXray;
            }

            public void Undo()
            {
                this.Target.Name = this.oldName;
                this.Target.Intencity = this.oldIntencity;
                this.Target.HalfLife = this.oldHalfLife;
                this.Target.Chain = this.oldChain;
                this.Target.DbRow = this.oldDbRow;
                this.Target.DbXray = this.oldDbXray;
            }
        }

        /// <summary>
        /// (`AMBER197`) Чья линия: «N:» + <c>nucid</c> нуклида (в любом из трёх
        /// форматов имени — «214BI», «Bi214», «Bi-214 (Ra-226)» — одно и то же)
        /// либо «X:» + символ у характеристического рентгена элемента. Ключ
        /// импорта — эта метка вместе с энергией.
        /// </summary>
        public static string LineIdentity(string nuclideName, bool elementXray)
        {
            string token = NuclideDefinition.NuclideNameOf(nuclideName ?? "");
            if (elementXray)
            {
                return "X:" + token;
            }

            string nucid = FullSpectrumAnalysis.FsaSampleLibrary.NucidOf(token);
            return "N:" + (nucid.Length > 0 ? nucid : token.ToUpperInvariant());
        }

        /// <summary>
        /// (`AMBER151`) Вид строки L-серии по колонке серии (пометка ∑ снимается):
        /// правило <see cref="FullSpectrumAnalysis.FsaSampleLibrary.LSeriesKind"/>.
        /// </summary>
        static int LKind(DataGridViewRow row)
        {
            string series = (row.Cells[SeriesColumnIdx].Value as string) ?? "";
            if (series.EndsWith(DecayRad.RedundantMark, StringComparison.Ordinal))
            {
                series = series.Substring(0, series.Length - DecayRad.RedundantMark.Length);
            }

            return FullSpectrumAnalysis.FsaSampleLibrary.LSeriesKind(series);
        }

        /// <summary>Строка помечена как лишняя при сложении (`D33`).</summary>
        static bool IsRedundantSeries(DataGridViewRow row)
        {
            string series = row.Cells[SeriesColumnIdx].Value as string;
            return series != null && series.EndsWith(DecayRad.RedundantMark, StringComparison.Ordinal);
        }

        /// <summary>
        /// Отмечена ли у того же родителя и того же типа распада ХОТЬ ОДНА
        /// строка той же Kβ с другой стороны — то есть та, чью сумму помеченная
        /// строка и представляет. Без этой проверки запрет был бы шире, чем
        /// нужно: взять помеченную строку ОДНУ никто не мешает.
        /// </summary>
        bool HasCheckedCounterpart(DataGridViewRow marked)
        {
            string name = marked.Cells[NameColumnIdx].Value as string;
            string decay = marked.Cells[DecayTypeColumnIdx].Value as string;
            string series = (marked.Cells[SeriesColumnIdx].Value as string) ?? "";
            // (`AMBER151`) У помеченной строки L-серии противоположная сторона —
            // любая отмеченная строка того же родителя и типа распада, которая
            // ПОДРОБНЕЕ её (подлиния при сводной или итоге, итог при сводной).
            int markedL = LKind(marked);
            bool markedIsTotal = series.StartsWith(FullSpectrumAnalysis.KSeriesRule.BetaTotal,
                                                   StringComparison.Ordinal);
            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                if (ReferenceEquals(row, marked) || !(row.Cells[CheckedColumnIdx].Value is bool)
                    || !(bool)row.Cells[CheckedColumnIdx].Value)
                {
                    continue;
                }

                if (!string.Equals(row.Cells[NameColumnIdx].Value as string, name, StringComparison.Ordinal)
                    || !string.Equals(row.Cells[DecayTypeColumnIdx].Value as string, decay, StringComparison.Ordinal))
                {
                    continue;
                }

                if (markedL != FullSpectrumAnalysis.FsaSampleLibrary.LSeriesNone)
                {
                    if (LKind(row) > markedL)
                    {
                        return true;
                    }

                    continue;
                }

                string other = (row.Cells[SeriesColumnIdx].Value as string) ?? "";
                bool otherIsTotal = other.StartsWith(FullSpectrumAnalysis.KSeriesRule.BetaTotal,
                                                    StringComparison.Ordinal);
                bool otherIsSplit = other.StartsWith("Kp", StringComparison.Ordinal);
                if (markedIsTotal ? otherIsSplit : otherIsTotal)
                {
                    return true;
                }
            }

            return false;
        }

        private string FormatIsotopeName(string nameFromDb)
        {
            // (`AMBER197`, попутно) Метка изомера — «m» с номером ИЛИ без:
            // в базе обе («99TCm» и «234PAm1»). Прежде «m» без цифры не
            // совпадало, и «99TCm» уходил в набор как есть, мимо выбранного
            // формата имени.
            Regex nameFormat = new Regex("^([0-9]+){1}([A-Z]+){1}(m[0-9]*)?$");
            Match match = nameFormat.Match(nameFromDb);
            if (!match.Success)
            {
                return nameFromDb;
            }

            string mass = match.Groups[1].Value;
            string isotope = match.Groups[2].Value;
            string isotopeLower = $"{isotope.Substring(0, 1)}{isotope.Substring(1).ToLower()}";
            string isomer = match.Groups.Count > 3
                ? match.Groups[3].Value
                : string.Empty;

            switch (comboBoxNameFormat.SelectedIndex)
            {
                case 0: // 137CS, 234PAm1
                    return $"{mass}{isotope}{isomer}";
                case 1: // Cs137, Pa234m1
                    return $"{isotopeLower}{mass}{isomer}";
                case 2: // Cs-137, Pa-234m1
                    return $"{isotopeLower}-{mass}{isomer}";
                default: // Cs137, Pa234m1
                    return $"{isotopeLower}{mass}{isomer}";
            }
        }

        private void IncludeDecayChainCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateNuclideDefinitionControlsState();
        }
    }
}
