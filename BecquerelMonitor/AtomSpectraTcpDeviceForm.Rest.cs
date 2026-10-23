// REST settings of the AtomSpectra device type (both transports stay "for tests" with a switch).
// Controls are created in code, so the designer file stays as it was.
// The REST test only READS (GET /api/status, GET /api/device).

using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm
    {
        private RadioButton radioTcp;
        private RadioButton radioRest;
        private TextBox textRestUrl;
        private InvariantNumericUpDown numericPoll;
        private InvariantNumericUpDown numericLimit;
        private CheckBox checkObserve;
        private CheckBox checkNoProxy;
        private ComboBox comboAuth;
        private TextBox textAuthUser;
        private TextBox textAuthSecret;
        private Button buttonRestTest;
        private Button buttonShowLog;
        private Label labelRestResult;

        private static Label MakeLabel(string text, int y)
        {
            return new Label { AutoSize = true, Location = new Point(17, y + 3), Text = text };
        }

        private void BuildRestControls()
        {
            this.AutoScroll = true;

            // Transport selection
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestTransport, 345));
            radioTcp = new RadioButton { Location = new Point(215, 345), AutoSize = true, Text = Resources.AtomSpectraRestTransportTcp };
            radioTcp.CheckedChanged += Setting_Changed;
            radioTcp.CheckedChanged += (s, e) => UpdateRestEnabled();
            this.Controls.Add(radioTcp);

            radioRest = new RadioButton { Location = new Point(330, 345), AutoSize = true, Text = Resources.AtomSpectraRestTransportRest };
            radioRest.CheckedChanged += Setting_Changed;
            radioRest.CheckedChanged += (s, e) => UpdateRestEnabled();
            this.Controls.Add(radioRest);

            // URL
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestUrl, 373));
            textRestUrl = new TextBox { Location = new Point(215, 373), Width = 240 };
            textRestUrl.TextChanged += Setting_Changed;
            this.Controls.Add(textRestUrl);

            // Poll interval
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestPoll, 401));
            numericPoll = new InvariantNumericUpDown { Location = new Point(215, 401), Width = 80, Minimum = 1, Maximum = 60, Value = 10 };
            numericPoll.ValueChanged += Setting_Changed;
            this.Controls.Add(numericPoll);

            // Limit seconds
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestLimit, 429));
            numericLimit = new InvariantNumericUpDown { Location = new Point(215, 429), Width = 100, Minimum = 0, Maximum = 31536000, Value = 0 };
            numericLimit.ValueChanged += Setting_Changed;
            this.Controls.Add(numericLimit);

            // Observe only
            checkObserve = new CheckBox { Location = new Point(17, 457), AutoSize = true, Text = Resources.AtomSpectraRestObserve };
            checkObserve.CheckedChanged += Setting_Changed;
            this.Controls.Add(checkObserve);

            // No proxy
            checkNoProxy = new CheckBox { Location = new Point(17, 482), AutoSize = true, Text = Resources.AtomSpectraRestNoProxy };
            checkNoProxy.CheckedChanged += Setting_Changed;
            this.Controls.Add(checkNoProxy);

            // Auth kind
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestAuth, 510));
            comboAuth = new ComboBox { Location = new Point(215, 510), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            comboAuth.Items.Add("None");
            comboAuth.Items.Add("Basic");
            comboAuth.Items.Add("Bearer");
            comboAuth.SelectedIndexChanged += Setting_Changed;
            comboAuth.SelectedIndexChanged += (s, e) => UpdateRestEnabled();
            this.Controls.Add(comboAuth);

            // Auth user
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestAuthUser, 538));
            textAuthUser = new TextBox { Location = new Point(215, 538), Width = 150 };
            textAuthUser.TextChanged += Setting_Changed;
            this.Controls.Add(textAuthUser);

            // Auth secret
            this.Controls.Add(MakeLabel(Resources.AtomSpectraRestAuthSecret, 566));
            textAuthSecret = new TextBox { Location = new Point(215, 566), Width = 150, UseSystemPasswordChar = true };
            textAuthSecret.TextChanged += Setting_Changed;
            this.Controls.Add(textAuthSecret);

            // Buttons
            buttonRestTest = new Button { Location = new Point(17, 598), Size = new Size(170, 23), Text = Resources.AtomSpectraRestTest };
            buttonRestTest.Click += buttonRestTest_Click;
            this.Controls.Add(buttonRestTest);

            buttonShowLog = new Button { Location = new Point(195, 598), Size = new Size(130, 23), Text = Resources.AtomSpectraRestShowLog };
            buttonShowLog.Click += (s, e) => AtomSpectraTcpLogForm.ShowLog(FindForm());
            this.Controls.Add(buttonShowLog);

            // Result label
            labelRestResult = new Label { Location = new Point(17, 628), Size = new Size(438, 60) };
            this.Controls.Add(labelRestResult);

            // Default state
            comboAuth.SelectedIndex = 0;
            radioTcp.Checked = true;
        }

        private void UpdateRestEnabled()
        {
            bool rest = radioRest.Checked;
            textRestUrl.Enabled = rest;
            numericPoll.Enabled = rest;
            numericLimit.Enabled = rest;
            checkObserve.Enabled = rest;
            checkNoProxy.Enabled = rest;
            comboAuth.Enabled = rest;
            buttonRestTest.Enabled = rest;

            bool auth = rest && comboAuth.SelectedIndex > 0;
            textAuthUser.Enabled = auth && comboAuth.SelectedIndex == 1;
            textAuthSecret.Enabled = auth;
        }

        private void LoadRest(AtomSpectraTcpDeviceConfig config)
        {
            // a new (empty) configuration starts with REST; an old file without the new fields loads as TCP bridge
            bool fresh = string.IsNullOrEmpty(config.Host) && string.IsNullOrEmpty(config.RestUrl);
            radioRest.Checked = config.Transport == AtomSpectraTransport.Rest || fresh;
            radioTcp.Checked = !radioRest.Checked;

            textRestUrl.Text = config.RestUrl ?? "";
            numericPoll.Value = Math.Max(1, Math.Min(60, config.RestPollSeconds));
            numericLimit.Value = Math.Max(0, Math.Min((int)numericLimit.Maximum, config.RestLimitSeconds));
            checkObserve.Checked = config.RestObserveOnly;
            checkNoProxy.Checked = config.RestNoProxy;
            comboAuth.SelectedIndex = (int)config.RestAuthKind;
            textAuthUser.Text = config.RestAuthUser ?? "";
            textAuthSecret.Text = config.RestAuthSecret ?? "";
            labelRestResult.Text = "";
            UpdateRestEnabled();
        }

        private bool RestUrlGiven()
        {
            return radioRest.Checked && textRestUrl.Text.Trim().Length > 0;
        }

        private void SaveRest(AtomSpectraTcpDeviceConfig config)
        {
            string url = textRestUrl.Text.Trim();
            if (radioRest.Checked && !AtomSpectraTcpDeviceConfig.IsValidRestUrl(url))
            {
                ShowWarning(string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestBadUrl, url));
                throw new FormatException("AtomSpectra REST: invalid board URL");
            }

            config.Transport = radioRest.Checked ? AtomSpectraTransport.Rest : AtomSpectraTransport.TcpBridge;
            config.RestUrl = url;
            config.RestPollSeconds = (int)numericPoll.Value;
            config.RestLimitSeconds = (int)numericLimit.Value;
            config.RestObserveOnly = checkObserve.Checked;
            config.RestNoProxy = checkNoProxy.Checked;
            config.RestAuthKind = (AtomSpectraRestAuth)Math.Max(0, comboAuth.SelectedIndex);
            config.RestAuthUser = textAuthUser.Text;
            config.RestAuthSecret = textAuthSecret.Text;
        }

        private void buttonRestTest_Click(object sender, EventArgs e)
        {
            AtomSpectraTcpDeviceConfig probe = new AtomSpectraTcpDeviceConfig();
            probe.Host = textHost.Text.Trim();
            if (probe.Host.Length == 0 && textRestUrl.Text.Trim().Length == 0)
            {
                labelRestResult.Text = Resources.ERRAtomSpectraTcpNoHost;   // the empty address is refused here, not at save
                return;
            }

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
                labelRestResult.Text = string.Format(CultureInfo.InvariantCulture, Resources.ERRAtomSpectraRestBadUrl, error);
                return;
            }

            int generation = ++testGeneration;
            buttonRestTest.Enabled = false;
            labelRestResult.Text = Resources.AtomSpectraTcpTesting;

            Thread worker = new Thread(delegate ()
            {
                AtomSpectraTcpCalibrationResult calibration;
                string bootConfig;
                string text = RunRestTest(client, out calibration, out bootConfig);
                AtomSpectraTcpLog.Info("Test", "Test connection (REST) " + client.BaseUrl + ": " + text);   // the result goes to the log too
                ShowRestResult(generation, text, calibration, bootConfig);
            });
            worker.IsBackground = true;
            worker.Name = "AtomSpectraRestTest";
            worker.Start();
        }

        // how a reply came over the network (shows the compression)
        private static string CompressionText(AtomSpectraRestResponse r)
        {
            if (!r.Ok) return "";
            return r.ContentEncoding.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraRestCompression, r.ContentEncoding, r.WireBytes, r.BodyBytes)
                : string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraRestNoCompression, r.BodyBytes);
        }

        private static string RunRestTest(AtomSpectraRestClient client, out AtomSpectraTcpCalibrationResult calibration, out string bootConfig)
        {
            calibration = null;   // the GET /api/device answer also carries the calibration for the Energy Calibration page
            bootConfig = null;    // GET /api/boot-config (gzip_level), read only
            AtomSpectraRestResponse st = client.Get("/api/status", 3000);
            if (!st.Ok)
            {
                return client.BaseUrl + ": " + (st.Error ?? ("HTTP " + st.Status));
            }

            try
            {
                Dictionary<string, object> s = AtomSpectraJson.Parse(st.Body) as Dictionary<string, object>;
                AtomSpectraRestResponse dv = client.Get("/api/device", 3000);
                Dictionary<string, object> d = dv.Ok ? AtomSpectraJson.Parse(dv.Body) as Dictionary<string, object> : null;
                calibration = dv.Ok ? AtomSpectraRestCalibration.ParseDevice(dv.Body)
                    : new AtomSpectraTcpCalibrationResult(AtomSpectraTcpCalibrationFailure.Connect, dv.Error ?? ("HTTP " + dv.Status), null, null);
                AtomSpectraRestResponse boot = client.Get("/api/boot-config", 3000);
                bootConfig = boot.Ok ? boot.Body : null;
                string compression = CompressionText(client.Get("/api/spectrum.json", 3000));   // the board compresses only the spectrum

                string calib = d != null && AtomSpectraJson.GetBool(d, "calib_set", false)
                    ? Resources.AtomSpectraRestCalibSet
                    : Resources.AtomSpectraRestCalibNone;

                return string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraRestTestResult,
                    client.BaseUrl,
                    AtomSpectraJson.GetString(s, "fw_version") ?? "?",
                    AtomSpectraJson.GetNumber(s, "time", -1),
                    AtomSpectraJson.GetNumber(s, "total_counts", -1),
                    calib) + compression;
            }
            catch (FormatException ex)
            {
                return client.BaseUrl + ": " + ex.Message;
            }
        }

        private void ShowRestResult(int generation, string text, AtomSpectraTcpCalibrationResult calibration = null, string bootConfig = null)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (generation != testGeneration || IsDisposed) return;
                    labelRestResult.Text = text;
                    if (calibration != null) ApplyBoardCalibration(calibration);   // board calibration -> Energy Calibration page
                    ShowBoardGzip(bootConfig);   // the board's reply compression level (null = not read)
                    buttonRestTest.Enabled = radioRest.Checked;
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
