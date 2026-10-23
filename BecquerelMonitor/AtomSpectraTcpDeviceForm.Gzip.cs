// "Board reply compression" on the Device Specific page (REST).
// Shown after "Test connection (REST)" / "Refresh" (GET /api/boot-config gzip_level); written only by "Apply to board" after a confirmation.
using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm
    {
        private const int GzipRoom = 56;   // two rows: the level row and its state line
        private Label labelGzip;
        private ComboBox comboGzip;
        private Button buttonGzipApply;
        private Label labelGzipState;
        private int boardGzipLevel = AtomSpectraRestBootConfig.Unknown;

        // The confirmation before a write ("No" is the default button); the test harness answers it.
        public static Func<string, bool> ConfirmGzip = text => MessageBox.Show(text, Resources.AtomSpectraGzipTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        // A block above "Test connection (REST)": everything from that button down moves by GzipRoom.
        private void AddGzipControls()
        {
            int top = buttonRestTest.Top;
            foreach (Control c in Controls.Cast<Control>().Where(x => x.Top >= top).ToList()) c.Top += GzipRoom;
            labelGzip = new Label { AutoSize = true, Location = new Point(17, top + 3), Text = Resources.AtomSpectraGzipLabel };
            comboGzip = new ComboBox { Location = new Point(215, top), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            comboGzip.Items.AddRange(new object[] { Resources.AtomSpectraGzipOff, "1", "6" });
            buttonGzipApply = new Button { Location = new Point(325, top - 1), Size = new Size(130, 23), Text = Resources.AtomSpectraGzipApply };
            buttonGzipApply.Click += buttonGzipApply_Click;
            labelGzipState = new Label { AutoSize = true, Location = new Point(215, top + 27), ForeColor = Color.Gray, Text = Resources.AtomSpectraGzipUnknown };
            Controls.AddRange(new Control[] { labelGzip, comboGzip, buttonGzipApply, labelGzipState });
            radioRest.CheckedChanged += (s, e) => UpdateGzipEnabled();
            checkObserve.CheckedChanged += (s, e) => UpdateGzipEnabled();
            UpdateGzipEnabled();
        }

        private void UpdateGzipEnabled()
        {
            bool known = Array.IndexOf(AtomSpectraRestBootConfig.Levels, boardGzipLevel) >= 0;
            bool can = radioRest.Checked && !checkObserve.Checked && known;
            comboGzip.Enabled = can;
            buttonGzipApply.Enabled = can;
            labelGzip.Enabled = radioRest.Checked;
            if (boardGzipLevel == AtomSpectraRestBootConfig.Unsupported) labelGzipState.Text = Resources.AtomSpectraGzipUnsupported;
            else if (!known) labelGzipState.Text = Resources.AtomSpectraGzipUnknown;
            else if (checkObserve.Checked) labelGzipState.Text = Resources.AtomSpectraGzipObserve;
            else labelGzipState.Text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraGzipOnBoard, AtomSpectraRestBootConfig.Describe(boardGzipLevel));
        }

        // On the UI thread after "Test connection (REST)" / "Refresh": the GET /api/boot-config body (null = not read).
        private void ShowBoardGzip(string bootConfigJson)
        {
            boardGzipLevel = bootConfigJson == null ? AtomSpectraRestBootConfig.Unknown : AtomSpectraRestBootConfig.ParseGzipLevel(bootConfigJson);
            int i = Array.IndexOf(AtomSpectraRestBootConfig.Levels, boardGzipLevel);
            if (i >= 0) comboGzip.SelectedIndex = i;
            UpdateGzipEnabled();
        }

        private void buttonGzipApply_Click(object sender, EventArgs e)
        {
            if (!radioRest.Checked || checkObserve.Checked || Array.IndexOf(AtomSpectraRestBootConfig.Levels, boardGzipLevel) < 0 || comboGzip.SelectedIndex < 0)
            {
                UpdateGzipEnabled();
                return;
            }
            int level = AtomSpectraRestBootConfig.Levels[comboGzip.SelectedIndex];

            AtomSpectraTcpDeviceConfig probe = new AtomSpectraTcpDeviceConfig();
            probe.Host = textHost.Text.Trim();
            try
            {
                SaveRest(probe);
            }
            catch (FormatException)
            {
                return;
            }
            string error;
            AtomSpectraRestClient client = probe.CreateRestClient(out error);
            if (client == null)
            {
                PrependOutput(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraGzipFailed, error) + Environment.NewLine);
                return;
            }
            // the question names the board the setting goes to
            if (!ConfirmGzip(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraGzipConfirm, level, AtomSpectraTcpLog.Mask(client.BaseUrl)))) return;

            int page = pageGeneration;
            buttonGzipApply.Enabled = false;
            comboGzip.Enabled = false;
            Thread worker = new Thread(() =>
            {
                string message;
                bool ok = AtomSpectraRestBootConfig.WriteGzipLevel(client, level, 3000, out message);
                int now = AtomSpectraRestBootConfig.ReadGzipLevel(client, 3000);
                string text = ok
                    ? string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraGzipDone, level, AtomSpectraRestBootConfig.Describe(now))
                    : string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraGzipFailed, message);
                AtomSpectraTcpLog.Info("Gzip", text);   // in the log even when the page shows another configuration by now
                // another configuration on the page meanwhile: its board was not the one written, the page is left as it is
                OnUi(page, delegate
                {
                    boardGzipLevel = now;
                    int i = Array.IndexOf(AtomSpectraRestBootConfig.Levels, now);
                    if (i >= 0) comboGzip.SelectedIndex = i;
                    PrependOutput(text + Environment.NewLine);
                    UpdateGzipEnabled();
                });
            });
            worker.IsBackground = true;
            worker.Name = "AtomSpectraGzipApply";
            worker.Start();
        }
    }
}
