using System;
using System.Drawing;
using System.Windows.Forms;
using XPTable.Events;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor.RoiWizard
{
    // Список нуклидов на странице — не таблица, а набор строк со своей вёрсткой:
    // имя, цветные бейджи семейств и приглушённый хвост «T½ γN XN». Штатная ячейка
    // XPTable знает один цвет и один шрифт на ячейку, поэтому три колонки списка
    // рисуются своими рендерерами. Шрифт держится полем: OnPaint зовётся на каждую
    // видимую ячейку при каждой перерисовке.

    // Бейджи семейств — правило .fbadge темы: прямоугольник без скругления,
    // 9.5 px полужирным, свой цвет фона и текста на каждый код.
    public class FamilyBadgeCellRenderer : CellRenderer
    {
        // padding 0 4px, margin-right 3px, line-height 14px — числа из темы
        const int PadX = 4;
        const int Gap = 3;
        const int BadgeHeight = 14;

        Font font = WizardTheme.BadgeFont;

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            if (e.Cell == null || string.IsNullOrEmpty(e.Cell.Text))
            {
                return;
            }

            Rectangle rect = this.ClientRectangle;
            int x = rect.X;
            int y = rect.Y + (rect.Height - BadgeHeight) / 2;
            foreach (string code in e.Cell.Text.Split(' '))
            {
                if (code.Length == 0)
                {
                    continue;
                }
                string caption = code.ToUpperInvariant();
                int width = TextRenderer.MeasureText(e.Graphics, caption, this.font,
                    new Size(rect.Width, BadgeHeight), TextFormatFlags.NoPadding).Width + PadX * 2;
                if (x + width > rect.Right)
                {
                    break;                     // не влезло — так же обрывается строка на странице
                }
                Color back;
                Color fore;
                WizardTheme.FamilyColors(code, out back, out fore);
                using (SolidBrush brush = new SolidBrush(back))
                {
                    e.Graphics.FillRectangle(brush, x, y, width, BadgeHeight);
                }
                TextRenderer.DrawText(e.Graphics, caption, this.font,
                    new Rectangle(x + PadX, y, width - PadX * 2, BadgeHeight), fore,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                x += width + Gap;
            }
        }

        public override void Dispose()
        {
            if (this.font != null)
            {
                this.font.Dispose();
                this.font = null;
            }
            base.Dispose();
        }
    }

    // Счётчики линий: «γ12 X4» — γ акцентным цветом, X сиреневым, как в списке
    // на странице. Числа приходят текстом ячейки вида «12 4»; X при нуле не рисуется.
    public class LineCountCellRenderer : CellRenderer
    {
        Font font = WizardTheme.HintFont;

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            if (e.Cell == null || string.IsNullOrEmpty(e.Cell.Text))
            {
                return;
            }
            string[] parts = e.Cell.Text.Split(' ');
            if (parts.Length < 2)
            {
                return;
            }

            Rectangle rect = this.ClientRectangle;
            int x = rect.X;
            x += Draw(e.Graphics, "γ" + parts[0], this.font, WizardTheme.Accent, rect, x);
            if (!string.Equals(parts[1], "0", StringComparison.Ordinal))
            {
                Draw(e.Graphics, " X" + parts[1], this.font, WizardTheme.Xray, rect, x);
            }
        }

        static int Draw(Graphics graphics, string text, Font font, Color color, Rectangle rect, int x)
        {
            Size size = TextRenderer.MeasureText(graphics, text, font,
                new Size(rect.Width, rect.Height), TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, text, font,
                new Rectangle(x, rect.Y, size.Width, rect.Height), color,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            return size.Width;
        }

        public override void Dispose()
        {
            if (this.font != null)
            {
                this.font.Dispose();
                this.font = null;
            }
            base.Dispose();
        }
    }

    // Заголовки числовых колонок — по центру. Штатный рендерер выравнивает все
    // подписи влево (Table.HeaderAlignWithColumn по умолчанию выключен), и подпись
    // висела над левым краем, тогда как числа прижаты к правому.
    public class CenteredHeaderRenderer : XPHeaderRenderer
    {
        public override void OnPaintHeader(PaintHeaderEventArgs e)
        {
            if (e.Column != null)
            {
                this.Alignment = e.Column.Alignment == ColumnAlignment.Right
                    ? ColumnAlignment.Center
                    : e.Column.Alignment;
            }
            base.OnPaintHeader(e);
        }
    }

    // Числовая колонка с отступом от правого края: штатная ячейка прижимает число
    // вплотную к границе, а на странице у td задан padding 0 7px.
    public class NumberCellRenderer : CellRenderer
    {
        internal const int PadRight = 7;

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            if (e.Cell == null || string.IsNullOrEmpty(e.Cell.Text))
            {
                return;
            }
            Rectangle rect = this.ClientRectangle;
            rect.Width -= PadRight;
            TextRenderer.DrawText(e.Graphics, e.Cell.Text, this.Font, rect, this.ForeColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Микро-бар интенсивности: полупрозрачная заливка на долю относительной
    // интенсивности, число поверх неё справа. Видно, где сильные линии, не читая
    // чисел. Заливка именно полупрозрачная (--bar), а не сплошная: на выбранной
    // строке фон и так --sel, сплошной бар с ним сливался бы.
    // Доля приходит в Cell.Tag — Data занят интенсивностью, по ней идёт сортировка.
    public class IntensityBarCellRenderer : CellRenderer
    {
        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            if (e.Cell == null)
            {
                return;
            }
            Rectangle rect = this.ClientRectangle;
            if (e.Cell.Tag is double)
            {
                double share = (double)e.Cell.Tag;
                int width = (int)Math.Round(rect.Width * Math.Max(0.0, Math.Min(100.0, share)) / 100.0);
                if (width > 0)
                {
                    using (SolidBrush brush = new SolidBrush(WizardTheme.Bar))
                    {
                        e.Graphics.FillRectangle(brush, rect.X, rect.Y + 1, width, rect.Height - 2);
                    }
                }
            }
            if (!string.IsNullOrEmpty(e.Cell.Text))
            {
                rect.Width -= NumberCellRenderer.PadRight;   // тот же отступ, что у прочих чисел
                TextRenderer.DrawText(e.Graphics, e.Cell.Text, this.Font, rect, this.ForeColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // Тип линии бейджем, как .badge в таблице на странице: γ, X, ХРИ, втор —
    // каждый своей парой цветов.
    public class LineTypeCellRenderer : CellRenderer
    {
        const int PadX = 5;
        const int BadgeHeight = 14;

        Font font = WizardTheme.BadgeFont;

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            if (e.Cell == null || string.IsNullOrEmpty(e.Cell.Text))
            {
                return;
            }
            string caption = e.Cell.Text;
            Rectangle rect = this.ClientRectangle;
            int width = TextRenderer.MeasureText(e.Graphics, caption, this.font,
                new Size(rect.Width, BadgeHeight), TextFormatFlags.NoPadding).Width + PadX * 2;
            int y = rect.Y + (rect.Height - BadgeHeight) / 2;
            Color back;
            Color fore;
            WizardTheme.LineTypeColors(e.Cell.Tag as string, out back, out fore);
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, rect.X, y, width, BadgeHeight);
            }
            TextRenderer.DrawText(e.Graphics, caption, this.font,
                new Rectangle(rect.X + PadX, y, width - PadX * 2, BadgeHeight), fore,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        }

        public override void Dispose()
        {
            if (this.font != null)
            {
                this.font.Dispose();
                this.font = null;
            }
            base.Dispose();
        }
    }

    // Кнопка «+ добавить» в строке результата поиска близких линий. Штатный
    // ButtonCellRenderer растягивает кнопку на всю ячейку, а на странице это
    // компактная кнопка у левого края; для нуклида, уже лежащего в наборе, кнопки
    // на странице нет вовсе — вместо неё подпись «в наборе».
    //
    // Обе правки делаются одним местом: и рисование, и попадание мыши в XPTable
    // идут через CalcButtonBounds. Пустой прямоугольник для «в наборе» гасит и
    // отрисовку кнопки (ThemeManager.DrawButton молча выходит на нулевом размере),
    // и клик — событие поднимается только из своих границ.
    public class NearAddCellRenderer : ButtonCellRenderer
    {
        const int PadX = 9;              // button{padding:2px 9px} темы
        const int Inset = 2;             // кнопка ниже строки таблицы, как на странице

        Cell current;

        protected override Rectangle CalcButtonBounds()
        {
            Rectangle rect = this.ClientRectangle;
            if (this.current == null || this.current.Tag == null)
            {
                return Rectangle.Empty;          // уже в наборе — кнопки нет
            }
            int width = TextRenderer.MeasureText(this.current.Text ?? "", this.Font).Width + PadX * 2;
            return new Rectangle(rect.X, rect.Y + Inset,
                                 Math.Min(rect.Width, width), Math.Max(0, rect.Height - Inset * 2));
        }

        public override void OnPaintCell(PaintCellEventArgs e)
        {
            this.current = e.Cell;
            base.OnPaintCell(e);
            if (e.Cell != null && e.Cell.Tag == null && !string.IsNullOrEmpty(e.Cell.Text))
            {
                // кнопки нет — на её месте приглушённая подпись
                TextRenderer.DrawText(e.Graphics, e.Cell.Text, this.Font, this.ClientRectangle,
                    WizardTheme.Muted,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            }
        }

        // Мышь приходит без прохода через OnPaintCell, а границы кнопки зависят от
        // ячейки — иначе клик по «в наборе» считался бы попаданием в чужую кнопку.
        public override void OnMouseDown(CellMouseEventArgs e)
        {
            this.current = e.Cell;
            base.OnMouseDown(e);
        }

        public override void OnMouseUp(CellMouseEventArgs e)
        {
            this.current = e.Cell;
            base.OnMouseUp(e);
        }

        public override void OnMouseMove(CellMouseEventArgs e)
        {
            this.current = e.Cell;
            base.OnMouseMove(e);
        }

        public override void OnMouseEnter(CellMouseEventArgs e)
        {
            this.current = e.Cell;
            base.OnMouseEnter(e);
        }
    }

    // Приглушённый хвост строки (.nuc .hl): 11 px цветом --muted. Цвет берётся
    // из ячейки, если он задан — так серым гаснет нуклид без линий.
    public class HintCellRenderer : CellRenderer
    {
        Font font = WizardTheme.HintFont;

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            if (e.Cell == null || string.IsNullOrEmpty(e.Cell.Text))
            {
                return;
            }
            Color color = this.ForeColor.IsEmpty || this.ForeColor == Color.Transparent
                ? WizardTheme.Muted
                : this.ForeColor;
            TextRenderer.DrawText(e.Graphics, e.Cell.Text, this.font, this.ClientRectangle, color,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public override void Dispose()
        {
            if (this.font != null)
            {
                this.font.Dispose();
                this.font = null;
            }
            base.Dispose();
        }
    }
}
