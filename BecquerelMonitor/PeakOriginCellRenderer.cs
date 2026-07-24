using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Windows.Forms;
using XPTable.Events;
using XPTable.Renderers;

namespace BecquerelMonitor
{
    class PeakOriginCellRenderer : TextCellRenderer
    {
        readonly Bitmap rjmcmcBitmap;
        readonly Bitmap anchorBitmap;
        readonly Bitmap libraryBitmap;

        public PeakOriginCellRenderer()
        {
            this.rjmcmcBitmap = new Bitmap(Resources.CONT);
            this.rjmcmcBitmap.MakeTransparent(Color.White);
            this.anchorBitmap = CreateAnchorBitmap();
            this.libraryBitmap = CreateLibraryBitmap();
        }

        // Красный якорь (U+2693): пик, совпавший с якорной линией нуклидного
        // сета — он включает библиотечный фит всей цепочки.
        static Bitmap CreateAnchorBitmap()
        {
            return CreateGlyphBitmap("⚓", Color.Red);
        }

        // Синяя книжка (U+1F4D6): пик, добавленный библиотечным фитом
        // (origin Library).
        static Bitmap CreateLibraryBitmap()
        {
            return CreateGlyphBitmap("\U0001F4D6", Color.RoyalBlue);
        }

        // Юникод-глиф в битмап 16x16. GDI+ не поддерживает цветные шрифты и
        // рисует эмодзи-глифы Segoe UI Emoji монохромным контуром — поэтому
        // цвет задаётся кистью.
        static Bitmap CreateGlyphBitmap(string glyph, Color color)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Font font = new Font("Segoe UI Emoji", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, brush, new RectangleF(0f, 0f, 16f, 16f), format);
            }

            return bitmap;
        }

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaintBackground(e);
            if (e.Cell == null)
            {
                return;
            }

            Bitmap bitmap = null;
            Peak peak = e.Cell.Tag as Peak;
            if (peak != null)
            {
                if (peak.PeakSearchOrigin == PeakSearchOrigin.Library)
                {
                    bitmap = this.libraryBitmap;
                }
                else if (peak.IsLibraryAnchor)
                {
                    // Только реально сработавший якорь: Nuclide.IsAnchor сам по
                    // себе означал бы «эта линия помечена якорной», и глиф
                    // рисовался бы даже без выбранного сета, когда
                    // LibraryPeakFitter не запускался вовсе.
                    bitmap = this.anchorBitmap;
                }
                else if (peak.PeakSearchOrigin == PeakSearchOrigin.RJMCMC)
                {
                    bitmap = this.rjmcmcBitmap;
                }
            }
            else if (e.Cell.Tag is PeakSearchOrigin && (PeakSearchOrigin)e.Cell.Tag == PeakSearchOrigin.RJMCMC)
            {
                // Обратная совместимость: где-то в Tag может лежать голый enum.
                bitmap = this.rjmcmcBitmap;
            }

            int textOffset = 0;
            if (bitmap != null)
            {
                Rectangle imageRectangle = this.ClientRectangle;
                imageRectangle.X += 2;
                imageRectangle.Width = 18;
                imageRectangle.Height -= 2;
                e.Graphics.DrawImage(bitmap, imageRectangle.X, imageRectangle.Y);
                textOffset = imageRectangle.Width + 3;
            }

            string text = e.Cell.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                Rectangle textRectangle = this.ClientRectangle;
                textRectangle.X += textOffset;
                textRectangle.Width -= textOffset;
                Brush textBrush = e.Enabled ? base.ForeBrush : base.GrayTextBrush;
                this.DrawString(e.Graphics, text, base.Font, textBrush, textRectangle, e.Cell.WordWrap);
            }

            if (e.Cell.WidthNotSet)
            {
                SizeF size = e.Graphics.MeasureString(text, base.Font);
                e.Cell.ContentWidth = textOffset + (int)Math.Ceiling(size.Width);
            }

            if (e.Focused && e.Enabled && e.Table.ShowSelectionRectangle)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, this.ClientRectangle);
            }
        }
    }
}
