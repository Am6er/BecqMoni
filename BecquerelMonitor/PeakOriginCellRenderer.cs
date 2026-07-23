using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        // Красный круг: пик, совпавший с якорной линией нуклидного сета —
        // он включает библиотечный фит всей цепочки.
        static Bitmap CreateAnchorBitmap()
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush fill = new SolidBrush(Color.Red))
            using (Pen edge = new Pen(Color.DarkRed, 1f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(fill, 3f, 3f, 10f, 10f);
                g.DrawEllipse(edge, 3f, 3f, 10f, 10f);
            }

            return bitmap;
        }

        // Синяя книжка: пик, добавленный библиотечным фитом (origin Library).
        static Bitmap CreateLibraryBitmap()
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush cover = new SolidBrush(Color.RoyalBlue))
                using (Pen edge = new Pen(Color.MidnightBlue, 1f))
                using (Pen page = new Pen(Color.White, 1f))
                {
                    // Обложка
                    g.FillRectangle(cover, 3.5f, 2.5f, 9f, 11f);
                    g.DrawRectangle(edge, 3.5f, 2.5f, 9f, 11f);
                    // Корешок
                    g.DrawLine(edge, 5.5f, 2.5f, 5.5f, 13.5f);
                    // Строки «текста»
                    g.DrawLine(page, 7f, 5.5f, 11f, 5.5f);
                    g.DrawLine(page, 7f, 8f, 11f, 8f);
                    g.DrawLine(page, 7f, 10.5f, 11f, 10.5f);
                }
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
                else if (peak.IsLibraryAnchor || (peak.Nuclide != null && peak.Nuclide.IsAnchor))
                {
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
