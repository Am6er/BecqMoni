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

        // Шрифты — общие на процесс, а не свойства, создающие Font на каждое
        // обращение: Walk раздаёт BaseFont сотне контролов, а рендеры держат
        // BadgeFont/HintFont полями, и ни один из потребителей владельцем шрифта
        // не становится — WinForms не диспозит Font, который ему присвоили.
        // Дескрипторы GDI здесь считают (ср. using (Pen ...) в EnergySpectrumView).

        // 12px/1.4 "Segoe UI" из темы — это 9 pt
        public static readonly Font BaseFont =
            new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        public static readonly Font LegendFont =
            new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);

        // 11px мелкого текста списков (.nuc .hl) — 8.25 pt
        public static readonly Font HintFont =
            new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);

        // 9.5px полужирного бейджа семейства (.fbadge) — 7.125 pt
        public static readonly Font BadgeFont =
            new Font("Segoe UI", 7.125F, FontStyle.Bold, GraphicsUnit.Point);

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
            // Рамку окна Apply не трогает намеренно. Оба окна модуля — DockContent,
            // и полоску заголовка с кнопками им рисует тема DockPanelSuite самого
            // приложения. Своя отрисовка заголовка здесь была и удалена: подобранные
            // руками цвета совпадали с одной темой и расходились с любой другой.
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
                    // Отсортированный столбец XPTable по умолчанию заливает своим
                    // цветом (WhiteSmoke) поверх фона строки, и после щелчка по
                    // заголовку отмеченные строки теряли подсветку именно в этом
                    // столбце. Прозрачный цвет отключает заливку (рендерер рисует
                    // её только при A != 0); направление сортировки и так показано
                    // стрелкой в заголовке — как на странице-эталоне.
                    table.SortedColumnBackColor = Color.Transparent;
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
}
