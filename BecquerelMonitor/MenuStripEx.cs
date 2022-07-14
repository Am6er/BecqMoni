using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000106 RID: 262
    public class MenuStripEx : MenuStrip
    {
        // Token: 0x1700039D RID: 925
        // (get) Token: 0x06000E45 RID: 3653 RVA: 0x0005422C File Offset: 0x0005242C
        // (set) Token: 0x06000E46 RID: 3654 RVA: 0x00054234 File Offset: 0x00052434
        public bool ClickThrough
        {
            get
            {
                return this.clickThrough;
            }
            set
            {
                this.clickThrough = value;
            }
        }

        // Token: 0x1700039E RID: 926
        // (get) Token: 0x06000E47 RID: 3655 RVA: 0x00054240 File Offset: 0x00052440
        // (set) Token: 0x06000E48 RID: 3656 RVA: 0x00054248 File Offset: 0x00052448
        public bool SuppressHighlighting
        {
            get
            {
                return this.suppressHighlighting;
            }
            set
            {
                this.suppressHighlighting = value;
            }
        }

        // Token: 0x06000E49 RID: 3657 RVA: 0x00054254 File Offset: 0x00052454
        protected override void WndProc(ref Message m)
        {
            if ((long)m.Msg == 512L && this.suppressHighlighting && !base.TopLevelControl.ContainsFocus)
            {
                return;
            }
            base.WndProc(ref m);
            if ((long)m.Msg == 33L && this.clickThrough && m.Result == (IntPtr)2L)
            {
                m.Result = (IntPtr)1L;
            }
        }

        // Token: 0x04000831 RID: 2097
        bool clickThrough;

        // Token: 0x04000832 RID: 2098
        bool suppressHighlighting = true;
    }
}
