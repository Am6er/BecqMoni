using System.Drawing;

using XPTable.Models;

namespace XPTable.Renderers
{
    /// <summary>
    /// Draws a rectangle round the destination drag drop row.
    /// </summary>
    public class DragDropRenderer : IDragDropRenderer
    {
        /// <summary>
        /// Creates a renderer that draw a red rectangle round the hovered row.
        /// </summary>
        public DragDropRenderer()
        {
            _forecolor = Color.Red;
        }

        /// <summary>
        /// Creates a rendered that draw a rectangle round the hovered row with the specified color.
        /// </summary>
        /// <param name="forecolor"></param>
        public DragDropRenderer(Color forecolor)
        {
            _forecolor = forecolor;
        }

        /// <summary>
        /// Called when the given row is hovered during drag drop.
        /// </summary>
        /// <param name="g"></param>
        /// <param name="row"></param>
        /// <param name="rowRect"></param>
        public void PaintDragDrop(Graphics g, Row row, Rectangle rowRect)
        {
            // (П315) Перо разбирается здесь же, а не финализатором: кадр перетаскивания частый.
            using (Pen pen = new Pen(_forecolor))
            {
                g.DrawRectangle(pen, rowRect);
            }
        }

        private Color _forecolor;

        /// <summary>
        /// Gets or sets the color used to draw the hover indicator rectangle.
        /// </summary>
        public Color ForeColor
        {
            get { return _forecolor; }
            set { _forecolor = value; }
        }
    }
}
