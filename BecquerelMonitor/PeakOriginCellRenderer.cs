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

        public PeakOriginCellRenderer()
        {
            this.rjmcmcBitmap = new Bitmap(Resources.CONT);
            this.rjmcmcBitmap.MakeTransparent(Color.White);
        }

        protected override void OnPaint(PaintCellEventArgs e)
        {
            base.OnPaintBackground(e);
            if (e.Cell == null)
            {
                return;
            }

            Bitmap bitmap = null;
            if (e.Cell.Tag is PeakSearchOrigin && (PeakSearchOrigin)e.Cell.Tag == PeakSearchOrigin.RJMCMC)
            {
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
