// the page fills its tab, wide fields follow the width, long row labels wrap; the plain-HTTP credentials warning.
using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm
    {
        private const int FieldColumn = 215;
        private const int RightMargin = 12;
        private const int WarningRoom = 34;

        private Label labelAuthWarning;
        private bool fitting;
        private int fittedRight = -1;

        private void ApplyLayout()
        {
            // the form takes the whole device page of DeviceConfigForm
            Dock = DockStyle.Fill;

            AddProxyAuthWarning();
            AddGzipControls();   // "Board reply compression" (AtomSpectraTcpDeviceForm.Gzip.cs)

            foreach (Label l in Controls.OfType<Label>().Where(x => x.AutoSize && x.Left < FieldColumn).ToList())
            {
                if (Controls.Cast<Control>().Any(c => !(c is Label) && c != textHost && c != numericPort && Math.Abs(c.Left - FieldColumn) <= 5 && Math.Abs(c.Top - l.Top) <= 8))
                {
                    // a long row label wraps instead of running into its field
                    l.MaximumSize = new Size(FieldColumn - l.Left - 6, 0);

                    // the rows below move down when the wrapped label is taller than its row
                    var below = Controls.Cast<Control>().Where(x => x.Top > l.Top + 8).ToList();
                    int extra = below.Count == 0 ? 0 : l.Bottom + 2 - below.Min(x => x.Top);
                    if (extra > 0) foreach (Control x in below) x.Top += extra;
                }
            }

            FitWidths();

            // widths follow the client width every time it changes, including when the vertical scroll bar appears; anchors are not used because in a scrolling form they keep the distance to the scroll area, not to the visible edge
            Layout += (s, e) => FitWidths();
            ClientSizeChanged += (s, e) => FitWidths();
        }

        private void FitWidths()
        {
            if (fitting) return;

            int right = ClientSize.Width - RightMargin;

            if (right == fittedRight || right < 100) return;

            fitting = true;
            try
            {
                fittedRight = right;

                foreach (Control w in new Control[] { textAddress, textRestUrl, textCommand, textOutput, labelHint, labelTestResult, labelRestResult, labelStatus })
                {
                    w.Width = Math.Max(80, right - w.Left - (w == textAddress ? buttonRefresh.Width + 6 : 0));
                }

                buttonRefresh.Left = right - buttonRefresh.Width;

                labelConsole.MaximumSize = new Size(Math.Max(80, right - labelConsole.Left), 0);
                labelAuthWarning.MaximumSize = new Size(Math.Max(80, right - labelAuthWarning.Left), 0);
            }
            finally
            {
                fitting = false;
            }
        }

        // Basic and Bearer credentials go over plain HTTP in the Authorization header (to the board, not to a proxy);
        // the warning, for both schemes, sits under the password field
        private void AddProxyAuthWarning()
        {
            int top = textAuthSecret.Bottom + 6;

            // everything below the password field moves down to make room
            foreach (Control c in Controls.Cast<Control>().Where(x => x.Top >= top).ToList())
            {
                c.Top += WarningRoom;
            }

            labelAuthWarning = new Label
            {
                AutoSize = true,
                Location = new Point(17, top),
                ForeColor = Color.DarkRed,
                Text = Resources.AtomSpectraRestProxyAuthWarning
            };

            Controls.Add(labelAuthWarning);

            EventHandler update = (s, e) => labelAuthWarning.Visible = radioRest.Checked && comboAuth.SelectedIndex > 0;
            comboAuth.SelectedIndexChanged += update;
            radioRest.CheckedChanged += update;
            update(this, EventArgs.Empty);
        }
    }
}
