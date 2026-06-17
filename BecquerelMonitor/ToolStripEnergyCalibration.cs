using System;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public class ToolStripEnergyCalibration : ToolStripDropDown
    {
        System.Windows.Forms.ToolStripControlHostDesignerSafe host;
        ToolStripEnergyCalibrationControl content;
        Font font = new Font("MS UI Gothic", 9f);

        public ToolStripEnergyCalibration(ToolStripEnergyCalibrationControl content)
        {
            Font = font;
            if (content == null)
            {
                throw new ArgumentNullException("content");
            }

            this.content = content;
            AutoSize = false;
            DoubleBuffered = true;
            ResizeRedraw = true;
            host = new System.Windows.Forms.ToolStripControlHostDesignerSafe(content);
            Padding = Margin = host.Padding = host.Margin = Padding.Empty;
            MinimumSize = content.MinimumSize;
            content.MinimumSize = content.Size;
            MaximumSize = new Size(content.Size.Width + 1, content.Size.Height + 1);
            content.MaximumSize = new Size(content.Size.Width + 1, content.Size.Height + 1);
            Size = new Size(content.Size.Width + 1, content.Size.Height + 1);
            content.Location = Point.Empty;
            Items.Add(host);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (font != null)
                {
                    font.Dispose();
                    font = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
