using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000D8 RID: 216
    public class IconPanel : Panel
    {
        // Token: 0x06000AEE RID: 2798 RVA: 0x00045628 File Offset: 0x00043828
        public IconPanel()
        {
            base.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            base.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            base.SetStyle(ControlStyles.UserPaint, true);
        }

        // Token: 0x06000AEF RID: 2799 RVA: 0x00045660 File Offset: 0x00043860
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.InterpolationMode = InterpolationMode.High;
            int height = base.Height;
            graphics.DrawImage(Resources.icon256, new Rectangle(0, 0, height, height));
            graphics.InterpolationMode = InterpolationMode.Default;
        }

        // Token: 0x06000AF0 RID: 2800 RVA: 0x000456A8 File Offset: 0x000438A8
        protected override void OnSizeChanged(EventArgs e)
        {
            base.Invalidate();
            base.OnSizeChanged(e);
        }
    }
}
