using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Windows.Forms;
using XPTable.Events;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor
{
    // Token: 0x02000016 RID: 22
    public class ResultValueCellRenderer : TextCellRenderer
    {
        // Token: 0x060000C0 RID: 192 RVA: 0x00003CCC File Offset: 0x00001ECC
        public ResultValueCellRenderer()
        {
            this.ndBitmap = new Bitmap(Resources.NDBitmap);
            this.ndBitmap.MakeTransparent(Color.White);
        }

        // Token: 0x060000C1 RID: 193 RVA: 0x00003CF4 File Offset: 0x00001EF4
        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaint(e);
            Cell cell = e.Cell;
            if (cell == null)
            {
                return;
            }
            Graphics graphics = e.Graphics;
            if (this.font == null)
            {
                this.font = new Font(base.Font.FontFamily, 8f);
            }
            if (cell.Tag != null && !(bool)cell.Tag)
            {
                // (`fixFsa2`, 07.11.2026) Значок «не обнаружен» — ПОДПИСЬ КУЛЬТУРЫ ИНТЕРФЕЙСА, а не
                // картинка: прежняя картинка несла английское «ND» и в русском интерфейсе
                // (проверка экраном 07.11.2026, снимки 17–19 полосы `screen`). Подпись — из ресурсов
                // окна (`DCResult_NotDetectedMark`: en «ND», ru «н/о»). ⛔ Замечание Amber 07.11.2026,
                // дословно: «…картинка на русском языке, она не вся влезала в строчку, она была
                // обрезанная». Размер значка — ОТ ИЗМЕРЕННОГО ТЕКСТА и высоты строки
                // (<see cref="MarkLayout"/>): не влезает — мельчает шрифт значка, а не режется рамка.
                string mark = this.NotDetectedText;
                if (string.IsNullOrEmpty(mark))
                {
                    Rectangle clientRectangle = this.ClientRectangle;
                    graphics.DrawImage(this.ndBitmap, clientRectangle.X + 2, clientRectangle.Y);
                    return;
                }

                Rectangle room = this.ClientRectangle;
                room.X += 2;
                room.Width = Math.Max(0, room.Width - 2);
                if (this.markFont == null || this.markRoomHeight != room.Height || this.markRoomWidth != room.Width
                    || !string.Equals(this.markLaidFor, mark, StringComparison.Ordinal))
                {
                    if (this.markFont != null)
                    {
                        this.markFont.Dispose();
                    }
                    this.markBox = MarkLayout(graphics, base.Font, mark, room, out this.markFont, out this.markTextOffset);
                    this.markRoomHeight = room.Height;
                    this.markRoomWidth = room.Width;
                    this.markLaidFor = mark;
                }
                Rectangle box = this.markBox;
                box.X = room.X;
                box.Y = room.Y + (room.Height - box.Height) / 2;
                using (Pen pen = new Pen(MarkColor))
                {
                    graphics.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
                }
                // текст ставится по ЧЕРНИЛАМ, а не по строке шрифта: у строчных «н/о» строка шрифта
                // несёт пустой верх (высота прописных) и низ (выносные), и по ней буквы садились низко
                TextRenderer.DrawText(graphics, mark, this.markFont,
                                      new Point(box.X + this.markTextOffset.X, box.Y + this.markTextOffset.Y), MarkColor,
                                      MarkTextFlags);
            }
        }

        static readonly Color MarkColor = Color.FromArgb(0, 0, 192);

        const TextFormatFlags MarkTextFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        /// <summary>
        /// (`fixFsa2`, второй заход 07.11.2026) Раскладка значка «не обнаружен» — ПО ЧЕРНИЛАМ ТЕКСТА.
        /// Рамка = чернила подписи + 1 точка рамки и 2 точки поля по ширине с каждой стороны, 1 точка
        /// рамки и 1 точка поля по высоте. Первая редакция мерила строку шрифта (`MeasureText`): у
        /// «н/о» в ней пустые полосы над строчными и под ними, кегль упирался в высоту строки на
        /// 6.5 pt, а буквы садились низко (замер критика `critFsa2`: строчные 5 точек против 7 у «ND»,
        /// поле 3 сверху и 2 снизу). Шрифт начинается с шрифта ячейки и мельчает шагом 0.5 pt, пока
        /// рамка не влезет в место ячейки (<paramref name="room"/>); ниже 5 pt не идёт. Возвращает
        /// рамку от (0, 0) и сдвиг точки вывода текста от угла рамки — чернила по центру рамки.
        /// Шрифт — новый, его освобождает вызывающий. Открыта пробе (`ResultHeaderProbe`).
        /// </summary>
        public static Rectangle MarkLayout(Graphics graphics, Font cellFont, string mark, Rectangle room,
                                           out Font markFont, out Point textOffset)
        {
            float points = cellFont.SizeInPoints;
            while (true)
            {
                markFont = new Font(cellFont.FontFamily, points, cellFont.Style, GraphicsUnit.Point);
                Rectangle ink = MarkInk(mark, markFont);
                int width = ink.Width + 6;
                int height = ink.Height + 4;
                if ((width <= room.Width && height <= room.Height) || points <= 5f)
                {
                    int w = Math.Min(width, Math.Max(1, room.Width));
                    int h = Math.Min(height, Math.Max(1, room.Height));
                    textOffset = new Point((w - ink.Width) / 2 - ink.X, (h - ink.Height + 1) / 2 - ink.Y);
                    return new Rectangle(0, 0, w, h);
                }
                markFont.Dispose();
                points -= 0.5f;
            }
        }

        /// <summary>
        /// (`fixFsa2`) Чернила подписи: прямоугольник непустых точек текста, выведенного тем же
        /// `TextRenderer.DrawText` от точки (0, 0). Пусто — прямоугольник строки шрифта.
        /// </summary>
        public static Rectangle MarkInk(string mark, Font font)
        {
            Size line = TextRenderer.MeasureText(mark, font, Size.Empty, MarkTextFlags);
            int bw = Math.Max(1, line.Width + 4), bh = Math.Max(1, line.Height + 4);
            using (var bitmap = new Bitmap(bw, bh))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White);
                    TextRenderer.DrawText(g, mark, font, Point.Empty, Color.Black, Color.White, MarkTextFlags);
                }
                int left = bw, top = bh, right = -1, bottom = -1;
                for (int y = 0; y < bh; y++)
                {
                    for (int x = 0; x < bw; x++)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        if (Math.Min(c.R, Math.Min(c.G, c.B)) < 160)
                        {
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
                    }
                }
                return right < 0 ? new Rectangle(0, 0, line.Width, line.Height)
                                 : new Rectangle(left, top, right - left + 1, bottom - top + 1);
            }
        }

        /// <summary>(`fixFsa2`) Подпись «не обнаружен» культуры интерфейса; пусто — прежняя картинка «ND».</summary>
        public string NotDetectedText { get; set; }

        /// <summary>(`fixFsa2`) Раскладка значка под место ячейки — пересчитывается, когда место или подпись сменились.</summary>
        Font markFont;
        Rectangle markBox;
        Point markTextOffset;
        int markRoomHeight = -1;
        int markRoomWidth = -1;
        string markLaidFor;

        // Token: 0x04000036 RID: 54
        Font font;

        // Token: 0x04000037 RID: 55
        Bitmap ndBitmap;
    }
}
