using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000F4 RID: 244
    public class RepeatButton : Button
    {
        // Token: 0x06000BE2 RID: 3042 RVA: 0x00047EC0 File Offset: 0x000460C0
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                RepeatButton.repeatButtonTimer.Tick += this.repeatButtonTimer_Tick;
                RepeatButton.repeatButtonTimer.Interval = 500;
                RepeatButton.repeatButtonTimer.Start();
            }
        }

        // Token: 0x06000BE3 RID: 3043 RVA: 0x00047F18 File Offset: 0x00046118
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                RepeatButton.repeatButtonTimer.Stop();
                RepeatButton.repeatButtonTimer.Tick -= this.repeatButtonTimer_Tick;
            }
        }

        // Token: 0x06000BE4 RID: 3044 RVA: 0x00047F54 File Offset: 0x00046154
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
        }

        // Token: 0x06000BE5 RID: 3045 RVA: 0x00047F60 File Offset: 0x00046160
        void repeatButtonTimer_Tick(object sender, EventArgs e)
        {
            this.OnClick(EventArgs.Empty);
            RepeatButton.repeatButtonTimer.Interval = 100;
        }

        // Token: 0x06000BE6 RID: 3046 RVA: 0x00047F7C File Offset: 0x0004617C
        static RepeatButton()
        {
            RepeatButton.repeatButtonTimer.Interval = 250;
        }

        // Token: 0x0400078A RID: 1930
        static Timer repeatButtonTimer = new Timer();
    }
}
