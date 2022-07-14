using System;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x0200005E RID: 94
    public class ToolStripEnergyCalibration : ToolStripDropDown
    {
        // Token: 0x06000480 RID: 1152 RVA: 0x00015A90 File Offset: 0x00013C90
        public ToolStripEnergyCalibration(ToolStripEnergyCalibrationControl content)
        {
            this.Font = this.font;
            if (content == null)
            {
                throw new ArgumentNullException("content");
            }
            this.content = content;
            this.AutoSize = false;
            this.DoubleBuffered = true;
            base.ResizeRedraw = true;
            this.host = new ToolStripControlHost(content);
            base.Padding = (base.Margin = (this.host.Padding = (this.host.Margin = Padding.Empty)));
            this.MinimumSize = content.MinimumSize;
            content.MinimumSize = content.Size;
            this.MaximumSize = new Size(content.Size.Width + 1, content.Size.Height + 1);
            content.MaximumSize = new Size(content.Size.Width + 1, content.Size.Height + 1);
            base.Size = new Size(content.Size.Width + 1, content.Size.Height + 1);
            content.Location = Point.Empty;
            this.Items.Add(this.host);
        }

        // Token: 0x06000481 RID: 1153 RVA: 0x00015BEC File Offset: 0x00013DEC
        protected override void Dispose(bool disposing)
        {
            this.font.Dispose();
            base.Dispose(disposing);
        }

        // Token: 0x040001E5 RID: 485
        ToolStripControlHost host;

        // Token: 0x040001E6 RID: 486
        ToolStripEnergyCalibrationControl content;

        // Token: 0x040001E7 RID: 487
        Font font = new Font("MS UI Gothic", 9f);
    }
}
