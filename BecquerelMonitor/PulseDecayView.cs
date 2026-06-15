using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000E3 RID: 227
    public partial class PulseDecayView : UserControl
    {
        // Token: 0x170002F4 RID: 756
        // (get) Token: 0x06000B2B RID: 2859 RVA: 0x00045F9C File Offset: 0x0004419C
        // (set) Token: 0x06000B2C RID: 2860 RVA: 0x00045FA4 File Offset: 0x000441A4
        public PulseCollection Pulses
        {
            get
            {
                return this.pulses;
            }
            set
            {
                this.pulses = value;
            }
        }

        // Token: 0x06000B2D RID: 2861 RVA: 0x00045FB0 File Offset: 0x000441B0
        public PulseDecayView()
        {
            this.InitializeComponent();
        }

        // Token: 0x06000B2E RID: 2862 RVA: 0x00045FC0 File Offset: 0x000441C0
        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics graphics = pe.Graphics;
            if (this.pulses == null)
            {
                return;
            }
            using (Bitmap bitmap = new Bitmap(this.Width, this.Height))
            {
                for (int i = 0; i < this.pulses.Pulses.Count; i++)
                {
                    int num = (int)(this.pulses[i].Height * 1024.0 / 40.0);
                    int num2 = this.Height - (int)((double)this.pulses[i].Width * 5.0);
                    if (num2 >= 0 && num2 < this.Height && num > 0 && num < this.Width)
                    {
                        bitmap.SetPixel(num, num2, Color.Red);
                    }
                }
                graphics.DrawImage(bitmap, new Point(0, 0));
            }
        }

        // Token: 0x04000753 RID: 1875
        PulseCollection pulses;
    }
}
