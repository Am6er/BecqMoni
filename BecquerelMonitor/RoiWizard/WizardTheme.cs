using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using XPTable.Models;

namespace BecquerelMonitor.RoiWizard
{
    // Палитра веб-версии инструмента (styles/becqmoni.css), перенесённая в форму
    // один в один. Веб-страница — эталон интерфейса: на ней обкатывались и раскладка,
    // и цвета, поэтому окно в BecqMoni обязано выглядеть так же, а не «примерно так».
    //
    // Числа взяты из переменных темы, имена сохранены, чтобы правку в CSS было легко
    // перенести сюда: --card, --panel, --head, --ink, --muted, --line, --grid,
    // --accent, --accent-ink, --sel, --tabbg.
    static class WizardTheme
    {
        public static readonly Color Card = Color.FromArgb(0xFF, 0xFF, 0xFF);        // --card
        public static readonly Color Panel = Color.FromArgb(0xEC, 0xEF, 0xF3);       // --panel
        public static readonly Color Head = Color.FromArgb(0x1F, 0x3A, 0x5F);        // --head
        public static readonly Color Ink = Color.FromArgb(0x1A, 0x1A, 0x1A);         // --ink
        public static readonly Color Muted = Color.FromArgb(0x5A, 0x66, 0x72);       // --muted
        public static readonly Color Line = Color.FromArgb(0xAD, 0xAD, 0xAD);        // --line
        public static readonly Color Grid = Color.FromArgb(0xEE, 0xF0, 0xF2);        // --grid
        public static readonly Color Accent = Color.FromArgb(0x12, 0x50, 0x7A);      // --accent
        public static readonly Color AccentInk = Color.FromArgb(0x1F, 0x3A, 0x5F);   // --accent-ink
        public static readonly Color Selection = Color.FromArgb(0xCD, 0xE4, 0xF7);   // --sel
        public static readonly Color TabBack = Color.FromArgb(0xE4, 0xE4, 0xE4);     // --tabbg
        public static readonly Color Chip = Color.FromArgb(0xE5, 0xE5, 0xE5);       // --chip
        public static readonly Color ChipLine = Color.FromArgb(0x7A, 0xA7, 0xCE);   // .chip.on border
        public static readonly Color Xray = Color.FromArgb(0x8A, 0x3D, 0x72);       // X-линии в списке
        public static readonly Color NoLines = Color.FromArgb(0x9A, 0x9A, 0x9A);    // .nuc.nolines
        // --bar: rgba(20,72,116,.22) — микро-бар интенсивности полупрозрачный намеренно,
        // иначе на выбранной строке сливался бы с --sel
        public static readonly Color Bar = Color.FromArgb(56, 0x14, 0x48, 0x74);

        // 12px/1.4 "Segoe UI" из темы — это 9 pt
        public static Font BaseFont
        {
            get { return new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point); }
        }

        public static Font LegendFont
        {
            get { return new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point); }
        }

        // 11px мелкого текста списков (.nuc .hl) — 8.25 pt
        public static Font HintFont
        {
            get { return new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point); }
        }

        // 9.5px полужирного бейджа семейства (.fbadge) — 7.125 pt
        public static Font BadgeFont
        {
            get { return new Font("Segoe UI", 7.125F, FontStyle.Bold, GraphicsUnit.Point); }
        }

        // Цвета бейджей типов линий — правила .b-g / .b-x / .b-xrf / .b-sec темы.
        // Ключ — не подпись (она переводится), а код типа.
        public static void LineTypeColors(string kind, out Color back, out Color fore)
        {
            switch (kind)
            {
                case "g":   back = Color.FromArgb(0xD3, 0xE0, 0xEE); fore = Color.FromArgb(0x12, 0x50, 0x7A); return;
                case "x":   back = Color.FromArgb(0xEC, 0xD9, 0xE8); fore = Color.FromArgb(0x8A, 0x4A, 0x7A); return;
                case "xrf": back = Color.FromArgb(0xF6, 0xE6, 0xC8); fore = Color.FromArgb(0x8A, 0x64, 0x20); return;
                case "sec": back = Color.FromArgb(0xD9, 0xE8, 0xDC); fore = Color.FromArgb(0x2F, 0x6B, 0x42); return;
                default:    back = Chip;                             fore = Ink;                             return;
            }
        }

        // Цвета бейджей семейств — правила .f-popular … .f-waste темы, пара «фон/текст».
        // Коды классификации: NORM, MED, IND, SNM по ANSI N42.34, остальные вне стандарта.
        public static void FamilyColors(string code, out Color back, out Color fore)
        {
            switch ((code ?? "").ToLowerInvariant())
            {
                case "popular": back = Color.FromArgb(0xE2, 0xF0, 0xDC); fore = Color.FromArgb(0x2F, 0x6B, 0x3F); return;
                case "norm":    back = Color.FromArgb(0xDC, 0xE9, 0xF5); fore = Color.FromArgb(0x28, 0x52, 0x7A); return;
                case "med":     back = Color.FromArgb(0xF5, 0xE2, 0xEF); fore = Color.FromArgb(0x8A, 0x3D, 0x72); return;
                case "ind":     back = Color.FromArgb(0xE6, 0xE2, 0xF5); fore = Color.FromArgb(0x4B, 0x3F, 0x8A); return;
                case "snm":     back = Color.FromArgb(0xFD, 0xF0, 0xD0); fore = Color.FromArgb(0x8A, 0x6A, 0x1F); return;
                case "fiss":    back = Color.FromArgb(0xFD, 0xE2, 0xDE); fore = Color.FromArgb(0x93, 0x37, 0x2C); return;
                case "naa":     back = Color.FromArgb(0xDF, 0xF0, 0xF2); fore = Color.FromArgb(0x27, 0x6B, 0x73); return;
                case "waste":   back = Color.FromArgb(0xEC, 0xE6, 0xDE); fore = Color.FromArgb(0x6B, 0x5A, 0x45); return;
                default:        back = Chip;                             fore = Ink;                             return;
            }
        }

        // Применяется после InitializeComponent: обходит дерево контролов и красит то,
        // что в вебе окрашено темой. Системные цвета трогаются только там, где тема
        // задаёт своё — фон окна (#f0f0f0) и так совпадает с системным.
        public static void Apply(Control root)
        {
            root.Font = BaseFont;
            Walk(root);
            // рамку окна Apply не трогает: окно мастера — DockContent, его полоску
            // рисует тема DockPanelSuite самого приложения; самодельная полоска
            // (ApplyCaption) нужна только окнам вне док-системы — справке
        }

        // Полоска заголовка — как у док-панелей самого BecqMoni («Обнаружение пиков»,
        // «Управление измерением»): цвет ToolWindowCaptionInactive.Background из
        // VS2015BlueTheme, которую MainForm ставит в InitializeDockPanelTheme, та же
        // высота, булавка и кнопки прямо в полоске, углы без скругления. Системный
        // заголовок Windows и толще, и не пускает в себя булавку, поэтому он убирается
        // совсем (WM_NCCALCSIZE), а полоска рисуется своя. Стили окна при этом
        // сохраняются — перетаскивание, ресайз за края и снап работают как обычно.
        // Все значения — из ColorPalette той же темы (снятые рефлексией):
        // ToolWindowCaptionInactive.*, ToolWindowCaptionButton*, ToolWindowBorder.
        internal static readonly Color CaptionBack = Color.FromArgb(0x4D, 0x60, 0x82);
        internal static readonly Color CaptionGlyph = Color.FromArgb(0xCE, 0xD4, 0xDD);
        internal static readonly Color CaptionHover = Color.FromArgb(0xFF, 0xFC, 0xF4);
        internal static readonly Color CaptionHoverEdge = Color.FromArgb(0xE5, 0xC3, 0x65);
        internal static readonly Color CaptionDown = Color.FromArgb(0xFF, 0xE8, 0xA6);
        internal static readonly Color WindowEdge = Color.FromArgb(0x8E, 0x9B, 0xBC);

        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWA_TEXT_COLOR = 36;
        const int DWMWCP_DONOTROUND = 1;      // у полосок панелей скругления нет

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
                                                         ref int value, int size);

        public static void ApplyCaption(Form form)
        {
            new PanelChrome(form);
        }

        internal static int ColorRef(Color color)
        {
            // DWM ждёт 0x00BBGGRR (COLORREF), а Color.ToArgb — 0xAARRGGBB
            return color.R | (color.G << 8) | (color.B << 16);
        }

        internal static void SetDwm(Form form)
        {
            IntPtr handle = form.Handle;
            int caption = ColorRef(CaptionBack);    // видно только в миниатюрах Alt-Tab
            int text = ColorRef(Card);
            int border = ColorRef(CaptionBack);
            int corner = DWMWCP_DONOTROUND;
            try
            {
                DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                                      ref corner, sizeof(int));
            }
            catch (DllNotFoundException) { }        // dwmapi.dll есть везде, где есть Aero
            catch (EntryPointNotFoundException) { } // на всякий случай
        }


        static void Walk(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                GroupBox box = control as GroupBox;
                if (box != null)
                {
                    // легенда панели — акцентным цветом и полужирным, как .gbox > .lg
                    box.ForeColor = AccentInk;
                    box.Font = LegendFont;
                    Walk(box);
                    // содержимое панели остаётся обычным шрифтом
                    foreach (Control child in box.Controls)
                    {
                        child.Font = BaseFont;
                    }
                    continue;
                }

                Table table = control as Table;
                if (table != null)
                {
                    table.GridColor = Grid;
                    table.GridLines = GridLines.Both;
                    table.SelectionBackColor = Selection;
                    table.SelectionForeColor = Ink;
                    table.ForeColor = Ink;
                    table.BackColor = Card;
                    continue;
                }

                StatusStrip status = control as StatusStrip;
                if (status != null)
                {
                    status.BackColor = Panel;
                    status.ForeColor = AccentInk;
                    continue;
                }

                ListBox list = control as ListBox;
                if (list != null)
                {
                    list.ForeColor = Accent;      // чипы «Выбрано» — акцентным, как в вебе
                    continue;
                }

                NumericUpDown numeric = control as NumericUpDown;
                if (numeric != null)
                {
                    ApplyNumberPadding(numeric);
                    continue;
                }

                Label label = control as Label;
                if (label != null && label.Text.EndsWith(":", StringComparison.Ordinal))
                {
                    label.ForeColor = Muted;      // подписи-заголовки списков приглушены
                    continue;
                }

                Walk(control);
            }
        }

        // Отступ числа от правого края поля. В теме у input задан padding 1px 4px,
        // а у поля ввода WinForms отступа нет ни свойством, ни стилем: внутренние
        // поля текста задаёт окну сообщение EM_SETMARGINS. Число прижимается вправо
        // (TextAlign задан в разметке), и без отступа оно упиралось бы в стрелки.
        const int EM_SETMARGINS = 0x00D3;
        const int EC_RIGHTMARGIN = 0x0002;
        const int NumberPadding = 4;                 // px, как padding-right в теме

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        static void ApplyNumberPadding(NumericUpDown numeric)
        {
            // поле ввода счётчика — его дочерний контрол; сообщение шлётся окну,
            // поэтому отступ ставится и заново при каждом пересоздании дескриптора
            foreach (Control child in numeric.Controls)
            {
                TextBoxBase edit = child as TextBoxBase;
                if (edit == null)
                {
                    continue;
                }
                SetRightMargin(edit);
                edit.HandleCreated += delegate(object sender, EventArgs e)
                {
                    SetRightMargin((Control)sender);
                };
            }
        }

        static void SetRightMargin(Control edit)
        {
            if (!edit.IsHandleCreated)
            {
                return;
            }
            SendMessage(edit.Handle, EM_SETMARGINS,
                        (IntPtr)EC_RIGHTMARGIN, (IntPtr)(NumberPadding << 16));
        }
    }

    // Полоска заголовка в стиле док-панелей BecqMoni плюс подкласс окна, который
    // прячет системный caption. NativeWindow используется, чтобы не заставлять формы
    // наследоваться от специального базового класса.
    sealed class PanelChrome : NativeWindow
    {
        // Метрики VS2012DockPaneCaption (их переиспользует тема VS2015): высота =
        // кнопка 18px + зазоры 3 сверху и снизу (текстовая ветка формулы даёт меньше
        // и не побеждает); кнопки — квадраты с зазором 1 между собой и 4 от правого
        // края. Высота 24px сверена пиксельно с живой панелью «Обнаружение пиков».
        const int StripHeight = 24;               // высота полоски док-панели, лог. px
        const int ButtonSize = 18;
        const int ButtonGapTop = 3;
        const int ButtonGapBetween = 1;
        const int ButtonGapRight = 4;
        const int Grip = 6;                       // зона ресайза по краям, лог. px

        const int WM_NCCALCSIZE = 0x0083;
        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCLIENT = 1, HTCAPTION = 2;
        const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        const int SM_CXSIZEFRAME = 32, SM_CXPADDEDBORDER = 92;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        readonly Form form;
        readonly Panel strip;
        readonly CheckBox pin;
        readonly Button buttonMax;
        readonly ToolTip tips = new ToolTip();

        public PanelChrome(Form owner)
        {
            this.form = owner;
            this.strip = new Panel();
            this.strip.Dock = DockStyle.Top;
            this.strip.Height = this.Scaled(StripHeight);
            this.strip.BackColor = WizardTheme.CaptionBack;
            this.strip.Paint += this.OnStripPaint;
            this.strip.MouseDown += this.OnStripMouseDown;
            this.strip.MouseDoubleClick += this.OnStripDoubleClick;

            Button close = this.MakeButton("");             // ChromeClose
            close.Click += delegate { this.form.Close(); };
            this.buttonMax = this.MakeButton("");           // ChromeMaximize
            this.buttonMax.Visible = this.form.MaximizeBox;
            this.buttonMax.Click += delegate { this.ToggleMaximize(); };
            Button minimize = this.MakeButton("");          // ChromeMinimize
            minimize.Visible = this.form.MinimizeBox;
            minimize.Click += delegate { this.form.WindowState = FormWindowState.Minimized; };

            this.pin = new CheckBox();
            this.pin.Appearance = Appearance.Button;
            this.StyleFlat(this.pin);
            this.pin.Text = "";                             // Pin
            this.pin.FlatAppearance.CheckedBackColor = WizardTheme.CaptionDown;
            this.pin.CheckedChanged += delegate
            {
                this.form.TopMost = this.pin.Checked;
                this.pin.Text = this.pin.Checked ? "" : "";
            };
            this.tips.SetToolTip(this.pin, RoiWizardStrings.pinTip);

            // порядок добавления — это порядок укладки от правого края
            this.strip.Controls.Add(close);
            this.strip.Controls.Add(this.buttonMax);
            this.strip.Controls.Add(minimize);
            this.strip.Controls.Add(this.pin);
            this.strip.Resize += delegate { this.LayoutButtons(); };
            this.LayoutButtons();

            // окантовка окна — ToolWindowBorder темы: кольцо в 1px из фона формы
            // плюс кромка DWM того же цвета
            this.form.BackColor = WizardTheme.WindowEdge;
            this.form.Padding = new Padding(1);

            // полоска добавляется последней: док-раскладка обходит контролы с конца,
            // поэтому она займёт верх раньше, чем Fill заберёт остаток
            this.form.Controls.Add(this.strip);
            this.form.TextChanged += delegate { this.strip.Invalidate(); };
            this.form.Resize += delegate { this.SyncMaxGlyph(); };

            if (this.form.IsHandleCreated)
            {
                this.Attach();
            }
            this.form.HandleCreated += delegate { this.Attach(); };
            this.form.HandleDestroyed += delegate { this.ReleaseHandle(); };
        }

        void Attach()
        {
            this.AssignHandle(this.form.Handle);
            WizardTheme.SetDwm(this.form);
            // рамки больше нет — окно чуть ужимается, чтобы содержимое осталось
            // тех же пропорций, что и с системным заголовком
            this.form.PerformLayout();
        }

        int Scaled(int logical)
        {
            return (int)Math.Round(logical * this.form.DeviceDpi / 96.0);
        }

        Button MakeButton(string glyph)
        {
            Button button = new Button();
            this.StyleFlat(button);
            button.Text = glyph;
            return button;
        }

        void StyleFlat(ButtonBase button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = WizardTheme.CaptionHoverEdge;
            button.FlatAppearance.MouseOverBackColor = WizardTheme.CaptionHover;
            button.FlatAppearance.MouseDownBackColor = WizardTheme.CaptionDown;
            button.BackColor = WizardTheme.CaptionBack;
            button.ForeColor = WizardTheme.CaptionGlyph;
            button.Font = new Font("Segoe MDL2 Assets", 7F);
            button.Size = new Size(this.Scaled(ButtonSize), this.Scaled(ButtonSize));
            button.TabStop = false;
            button.TextAlign = ContentAlignment.MiddleCenter;
            // кремовая заливка с золотой рамкой и чёрным глифом — только под курсором,
            // как у кнопок панелей темы
            button.MouseEnter += delegate(object sender, EventArgs e)
            {
                ButtonBase self = (ButtonBase)sender;
                self.FlatAppearance.BorderSize = 1;
                self.ForeColor = WizardTheme.Ink;
            };
            button.MouseLeave += delegate(object sender, EventArgs e)
            {
                ButtonBase self = (ButtonBase)sender;
                self.FlatAppearance.BorderSize = 0;
                self.ForeColor = WizardTheme.CaptionGlyph;
            };
        }

        void LayoutButtons()
        {
            int x = this.strip.Width - this.Scaled(ButtonGapRight);
            int y = this.Scaled(ButtonGapTop);
            foreach (Control button in this.strip.Controls)
            {
                if (!(button is ButtonBase))
                {
                    continue;
                }
                x -= button.Width;
                button.Location = new Point(x, y);
                x -= this.Scaled(ButtonGapBetween);
            }
        }

        void OnStripPaint(object sender, PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, this.form.Text, WizardTheme.BaseFont,
                new Rectangle(this.Scaled(6), 0,
                              this.strip.Width - this.Scaled(6), this.strip.Height),
                WizardTheme.Card,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        void OnStripMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            int code = HTCAPTION;
            if (this.form.WindowState == FormWindowState.Normal && e.Y <= this.Scaled(Grip))
            {
                int grip = this.Scaled(Grip);
                code = e.X < grip ? HTTOPLEFT
                     : e.X > this.strip.Width - grip ? HTTOPRIGHT : HTTOP;
            }
            ReleaseCapture();
            SendMessage(this.form.Handle, WM_NCLBUTTONDOWN, (IntPtr)code, IntPtr.Zero);
        }

        void OnStripDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.form.MaximizeBox)
            {
                this.ToggleMaximize();
            }
        }

        void ToggleMaximize()
        {
            this.form.WindowState = this.form.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        void SyncMaxGlyph()
        {
            this.buttonMax.Text = this.form.WindowState == FormWindowState.Maximized
                ? "" : "";            // ChromeRestore : ChromeMaximize
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero)
                    {
                        if (this.form.WindowState == FormWindowState.Maximized)
                        {
                            // развёрнутое окно без этой поправки вылезает рамкой
                            // за края монитора
                            RECT rect = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                            int pad = GetSystemMetrics(SM_CXSIZEFRAME)
                                    + GetSystemMetrics(SM_CXPADDEDBORDER);
                            rect.Left += pad; rect.Top += pad;
                            rect.Right -= pad; rect.Bottom -= pad;
                            Marshal.StructureToPtr(rect, m.LParam, false);
                        }
                        m.Result = IntPtr.Zero;   // клиентская область = всё окно
                        return;
                    }
                    break;

                case WM_NCHITTEST:
                    base.WndProc(ref m);
                    if ((int)m.Result == HTCLIENT
                        && this.form.WindowState == FormWindowState.Normal)
                    {
                        Point at = this.form.PointToClient(new Point(m.LParam.ToInt32()));
                        int grip = this.Scaled(Grip);
                        bool left = at.X < grip;
                        bool right = at.X > this.form.ClientSize.Width - grip;
                        bool top = at.Y < grip;
                        bool bottom = at.Y > this.form.ClientSize.Height - grip;
                        int hit = HTCLIENT;
                        if (top) { hit = left ? HTTOPLEFT : right ? HTTOPRIGHT : HTTOP; }
                        else if (bottom) { hit = left ? HTBOTTOMLEFT : right ? HTBOTTOMRIGHT : HTBOTTOM; }
                        else if (left) { hit = HTLEFT; }
                        else if (right) { hit = HTRIGHT; }
                        m.Result = (IntPtr)hit;
                    }
                    return;
            }
            base.WndProc(ref m);
        }
    }
}
