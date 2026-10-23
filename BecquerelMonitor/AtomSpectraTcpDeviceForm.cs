using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Settings panel of the AtomSpectra TCP device type. Texts come from Properties.Resources
// (neutral English + Russian), so the form needs no .resx of its own (as AmplitudaSerialDeviceForm).
//
// "Test connection" only LISTENS: it connects, counts valid shproto packets for a few seconds
// and disconnects. It sends nothing to the board or the analyzer. It runs on a worker thread,
// the UI thread never waits on the network.

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm : InputDeviceForm
    {
        const int TestListenMs = 3000;

        // The settings form's own warnings (the test harness replaces it to see them without a modal window).
        public static Func<string, DialogResult> ShowWarning = text => MessageBox.Show(text, Resources.InvalidValueDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private bool formLoading;
        // pageGeneration: the configuration shown (LoadFormContents, closing); a late answer of another one is dropped.
        // Each background operation has its own counter, so one finishing does not leave another one's button grey.
        private int pageGeneration;
        private int testGeneration;       // "Test connection" (TCP bridge)
        private int restTestGeneration;   // "Test connection (REST)"
        private int refreshGeneration;    // "Refresh"
        private AtomSpectraTcpDeviceConfig loadedConfig;   // the configuration the fields were loaded from
        private double deadTime;

        public AtomSpectraTcpDeviceForm()
        {
            InitializeComponent();
            BuildRestControls();
            BuildConsoleControls();   // address, Refresh, command line (AtomSpectraTcpDeviceForm.Console.cs)
            ApplyTexts();
            ApplyLayout();   // fills the page, follows its width (AtomSpectraTcpDeviceForm.Layout.cs)
        }

        public AtomSpectraTcpDeviceForm(DeviceConfigForm deviceConfigForm) : base(deviceConfigForm)
        {
            formLoading = true;
            InitializeComponent();
            BuildRestControls();
            BuildConsoleControls();   // address, Refresh, command line (AtomSpectraTcpDeviceForm.Console.cs)
            ApplyTexts();
            ApplyLayout();   // fills the page, follows its width (AtomSpectraTcpDeviceForm.Layout.cs)
            base.DeviceTypeString = Resources.DeviceTypeAtomSpectraTCP;
            formLoading = false;
        }

        private void ApplyTexts()
        {
            labelHost.Text = Resources.AtomSpectraTcpHost;
            labelPort.Text = Resources.AtomSpectraTcpPort;
            labelHint.Text = Resources.AtomSpectraTcpHint;
            buttonTest.Text = Resources.AtomSpectraTcpTest;
        }

        public override void LoadFormContents(InputDeviceConfig inputConfig)
        {
            pageGeneration++;   // answers still on their way belong to the configuration shown before
            formLoading = true;

            var config = inputConfig as AtomSpectraTcpDeviceConfig;
            loadedConfig = config;
            boardGzipLevel = AtomSpectraRestBootConfig.Unknown;   // the compression level read from the previous configuration's board
            if (config != null)
            {
                textHost.Text = config.Host;

                decimal min = numericPort.Minimum;
                decimal max = numericPort.Maximum;
                decimal val = config.TcpPort;
                if (val < min) val = min;
                if (val > max) val = max;
                numericPort.Value = val;

                deadTime = config.DeadTimeValue;
                labelDeadTime.Text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpDeadTimeText, deadTime * 1.0E+06);   // 3 decimals
                LoadRest(config);
                LoadConsole(config);
            }

            labelTestResult.Text = "";
            buttonTest.Enabled = true;
            ApplyModeTexts();   // REST: own hint, bridge test disabled (AtomSpectraTcpDeviceForm.Console.cs)
            UpdateGzipEnabled();
            formLoading = false;
        }

        public override bool SaveFormContents(InputDeviceConfig inputConfig)
        {
            var config = inputConfig as AtomSpectraTcpDeviceConfig;
            if (config == null) return false;

            // DeviceConfigForm.SaveFormContents ignores this method's return value, so an invalid host is refused by
            // an exception. DeviceSaveRefusedException says that the page has already shown why: the form then adds
            // no ERRInvalidInputForm of its own, and the configuration is not written to disk.
            string host = textHost.Text.Trim();
            int port = (int)numericPort.Value;
            SaveRest(config);   // throws DeviceSaveRefusedException on an invalid board URL, after its own message
            // an EMPTY address is not an error while editing (a new configuration starts empty);
            // it is refused at Start and by "Test connection" (ERRAtomSpectraTcpNoHost). A non-empty wrong address is refused here.
            bool addressEmpty = textAddress.Text.Trim().Length == 0;
            if (!RestUrlGiven() && !addressEmpty && (!AtomSpectraTcpDeviceConfig.IsValidHost(host) || port < 1 || port > 65535))
            {
                ShowWarning(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpBadHost, textAddress.Text.Trim(), AddressDefaultPort()));
                throw new DeviceSaveRefusedException("AtomSpectra TCP: invalid host or port");
            }

            try
            {
                config.Host = textHost.Text.Trim();
                config.TcpPort = (int)numericPort.Value;
                config.DeadTimeValue = deadTime;
                SaveConsole(config);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public override void FormClosing()
        {
            pageGeneration++;
        }

        // "Read from device" of DeviceConfigForm: the connection as the fields say NOW (the address reaches the
        // configuration only at Save), on a copy of the configuration being edited. null: the fields were loaded from
        // another configuration (the caller uses the saved one), or the board URL is invalid (already said by a warning).
        public AtomSpectraTcpDeviceConfig EditedConnection(AtomSpectraTcpDeviceConfig saved, out bool refused)
        {
            refused = false;
            if (saved == null || !ReferenceEquals(saved, loadedConfig)) return null;
            var copy = (AtomSpectraTcpDeviceConfig)saved.Clone();
            copy.Host = textHost.Text.Trim();
            copy.TcpPort = (int)numericPort.Value;
            try
            {
                SaveRest(copy);
                SaveConsole(copy);
            }
            catch (FormatException)
            {
                refused = true;
                return null;
            }
            return copy;
        }

        private void Setting_Changed(object sender, EventArgs e)
        {
            if (!formLoading && deviceConfigForm != null)
            {
                deviceConfigForm.SetActiveDeviceConfigDirty();
            }
        }

        private void buttonTest_Click(object sender, EventArgs e)
        {
            string host = textHost.Text.Trim();
            int port = (int)numericPort.Value;

            if (string.IsNullOrEmpty(host))
            {
                labelTestResult.Text = Resources.ERRAtomSpectraTcpNoHost;
                return;
            }

            int generation = ++testGeneration;
            int page = pageGeneration;
            buttonTest.Enabled = false;
            labelTestResult.Text = Resources.AtomSpectraTcpTesting;

            Thread worker = new Thread(() =>
            {
                string text = RunTest(host, port);
                AtomSpectraTcpLog.Info("Test", "Test connection (TCP) " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ": " + text);   // the result goes to the log too
                ShowTestResult(generation, page, text);
            });
            worker.IsBackground = true;
            worker.Name = "AtomSpectraTcpTest";
            worker.Start();
        }

        private static string RunTest(string host, int port)
        {
            using (AtomSpectraTcpTransport transport = new AtomSpectraTcpTransport(host, port, AtomSpectraTcpTransport.DefaultConnectTimeoutMs))
            {
                try
                {
                    transport.Open();
                }
                catch (Exception ex)
                {
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpConnect, host, port, ex.Message);
                }

                AtomSpectraShproto codec = new AtomSpectraShproto();
                byte[] buffer = new byte[4096];
                long start = transport.NowMs;
                int valid = 0;
                int bad = 0;

                try
                {
                    while (transport.NowMs - start < TestListenMs)
                    {
                        int n = transport.Read(buffer, 0, buffer.Length, 100);
                        for (int i = 0; i < n; i++)
                        {
                            if (codec.Feed(buffer[i]))
                            {
                                valid++;
                            }
                            else if (codec.LastFinishBadCrc)
                            {
                                bad++;
                            }
                        }
                    }
                }
                catch (IOException ex)
                {
                    return string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraTcpConnect, host, port, ex.Message);
                }

                double seconds = TestListenMs / 1000.0;

                if (valid == 0 && bad == 0)
                {
                    return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpTestNoData, host, port, seconds);
                }
                else
                {
                    return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpTestResult, host, port, valid, seconds, bad);
                }
            }
        }

        private void ShowTestResult(int generation, int page, string text)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (IsDisposed) return;
                    if (generation == testGeneration) buttonTest.Enabled = !radioRest.Checked;   // the last test is over; the bridge test stays off in REST mode
                    if (generation != testGeneration || page != pageGeneration) return;   // a newer test, or another configuration on the page
                    labelTestResult.Text = text;
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
