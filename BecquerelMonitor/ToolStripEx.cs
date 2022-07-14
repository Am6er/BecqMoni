using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000107 RID: 263
    public class ToolStripEx : ToolStrip
    {
        // Token: 0x1700039F RID: 927
        // (get) Token: 0x06000E4B RID: 3659 RVA: 0x000542E8 File Offset: 0x000524E8
        // (set) Token: 0x06000E4C RID: 3660 RVA: 0x000542F0 File Offset: 0x000524F0
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

        // Token: 0x170003A0 RID: 928
        // (get) Token: 0x06000E4D RID: 3661 RVA: 0x000542FC File Offset: 0x000524FC
        // (set) Token: 0x06000E4E RID: 3662 RVA: 0x00054304 File Offset: 0x00052504
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

        // Token: 0x06000E4F RID: 3663 RVA: 0x00054310 File Offset: 0x00052510
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

        // Token: 0x04000833 RID: 2099
        bool clickThrough;

        // Token: 0x04000834 RID: 2100
        bool suppressHighlighting = true;
    }
}
