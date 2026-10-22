// Device features of the AtomSpectra network type (after the COM type AtomSpectraVCPDeviceForm, which
// is not changed): address host[:port] instead of COM port and baud rate, Refresh (name -> GET /api/device), status with the
// serial number, and a command line with its output window. Logic is in AtomSpectraDeviceConsole (testable without WinForms).

using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AtomSpectraTcpDeviceForm
    {
        private Label labelAddress;
        private TextBox textAddress;
        private Button buttonRefresh;
        private Label labelStatus;
        private Label labelPollHint;
        private Label labelConsole;
        private TextBox textCommand;
        private TextBox textOutput;
        private int restAddressPort = AtomSpectraTcpDeviceConfig.DefaultRestPort;

        private void BuildConsoleControls()
        {
            labelHost.Visible = false;
            textHost.Visible = false;
            labelPort.Visible = false;
            numericPort.Visible = false;

            labelAddress = new Label
            {
                AutoSize = true,
                Location = new Point(17, 76),
                Text = Resources.AtomSpectraConsoleAddress
            };
            Controls.Add(labelAddress);

            textAddress = new TextBox
            {
                Location = new Point(215, 73),
                Width = 150
            };
            textAddress.TextChanged += AddressChanged;
            Controls.Add(textAddress);

            buttonRefresh = new Button
            {
                Location = new Point(370, 71),
                Size = new Size(85, 23),
                Text = Resources.AtomSpectraConsoleRefresh
            };
            buttonRefresh.Click += buttonRefresh_Click;
            Controls.Add(buttonRefresh);

            labelStatus = new Label
            {
                Location = new Point(17, 104),
                Size = new Size(438, 28),
                Text = ""
            };
            Controls.Add(labelStatus);

            labelPollHint = new Label
            {
                AutoSize = true,
                Location = new Point(300, 404),
                ForeColor = Color.Gray,
                Text = Resources.AtomSpectraConsolePollTcpShort   // the long hint was cut at the right edge
            };
            Controls.Add(labelPollHint);
            new ToolTip().SetToolTip(labelPollHint, Resources.AtomSpectraConsolePollTcpHint);   // the full text on hover

            labelConsole = new Label
            {
                AutoSize = true,
                Location = new Point(17, 700),
                Text = Resources.AtomSpectraConsoleWarning
            };
            Controls.Add(labelConsole);

            textCommand = new TextBox
            {
                Location = new Point(17, 720),
                Width = 438,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.CustomSource,
                AutoCompleteCustomSource = new AutoCompleteStringCollection()
            };
            textCommand.KeyDown += textCommand_KeyDown;
            Controls.Add(textCommand);

            textOutput = new TextBox
            {
                Location = new Point(17, 746),
                Size = new Size(438, 150),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 8.25f)
            };
            Controls.Add(textOutput);

            radioTcp.CheckedChanged += (s, e) => UpdateConsoleMode();
            radioRest.CheckedChanged += (s, e) => UpdateConsoleMode();
        }

        private int AddressDefaultPort()
        {
            return radioRest.Checked ? AtomSpectraTcpDeviceConfig.DefaultRestPort : AtomSpectraTcpDeviceConfig.DefaultTcpPort;
        }

        private int AddressPort()
        {
            return radioRest.Checked ? restAddressPort : (int)numericPort.Value;
        }

        private void UpdateConsoleMode()
        {
            if (labelPollHint == null) return;
            labelPollHint.Visible = !radioRest.Checked;
            ApplyModeTexts();
            if (!formLoading && textHost.Text.Trim().Length > 0)
            {
                textAddress.Text = AtomSpectraDeviceConsole.FormatAddress(textHost.Text.Trim(), AddressPort(), AddressDefaultPort());
            }
        }

        private void AddressChanged(object sender, EventArgs e)
        {
            string h;
            int p;
            if (AtomSpectraDeviceConsole.TryParseAddress(textAddress.Text, AddressDefaultPort(), out h, out p))
            {
                textHost.Text = h;
                if (radioRest.Checked)
                {
                    if (restAddressPort != p)
                    {
                        restAddressPort = p;
                        if (!formLoading) Setting_Changed(this, EventArgs.Empty);
                    }
                }
                else
                {
                    numericPort.Value = Math.Max(numericPort.Minimum, Math.Min(numericPort.Maximum, p));
                }
            }
            else
            {
                textHost.Text = textAddress.Text.Trim();
            }
        }

        private void LoadConsole(AtomSpectraTcpDeviceConfig config)
        {
            restAddressPort = config.RestPort;
            textAddress.Text = radioRest.Checked
                ? AtomSpectraDeviceConsole.FormatAddress(config.Host, config.RestPort, AtomSpectraTcpDeviceConfig.DefaultRestPort)
                : AtomSpectraDeviceConsole.FormatAddress(config.Host, config.TcpPort, AtomSpectraTcpDeviceConfig.DefaultTcpPort);
            labelStatus.Text = "";
            textOutput.Text = "";
            labelPollHint.Visible = !radioRest.Checked;
            buttonRefresh.Enabled = true;
        }

        // REST shows its own hint; the bridge port test (TCP) is disabled there and says why. TCP keeps the old ones.
        private void ApplyModeTexts()
        {
            bool rest = radioRest.Checked;
            labelHint.Text = rest ? Resources.AtomSpectraRestHint : Resources.AtomSpectraTcpHint;
            buttonTest.Enabled = !rest;
            if (rest) labelTestResult.Text = Resources.AtomSpectraRestTcpTestDisabled;
            else if (labelTestResult.Text == Resources.AtomSpectraRestTcpTestDisabled) labelTestResult.Text = "";
        }

        private void SaveConsole(AtomSpectraTcpDeviceConfig config)
        {
            if (radioRest.Checked) config.RestPort = restAddressPort;
        }

        private void buttonRefresh_Click(object sender, EventArgs e)
        {
            string h;
            int p;
            string name = AtomSpectraDeviceConsole.DefaultBoardName;
            if (AtomSpectraDeviceConsole.TryParseAddress(textAddress.Text, AddressDefaultPort(), out h, out p) && Uri.CheckHostName(h) == UriHostNameType.Dns)
            {
                name = h;
            }

            int generation = ++testGeneration;
            bool proxy = !checkNoProxy.Checked;
            int httpPort = radioRest.Checked ? restAddressPort : AtomSpectraTcpDeviceConfig.DefaultRestPort;

            buttonRefresh.Enabled = false;
            labelStatus.ForeColor = Color.Gray;
            labelStatus.Text = string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraConsoleStatusSearching, name);

            Thread worker = new Thread(() =>
            {
                var r = AtomSpectraDeviceConsole.Refresh(name, httpPort, proxy, 3000);
                OnUi(delegate
                {
                    if (generation != testGeneration) return;
                    buttonRefresh.Enabled = true;
                    if (r.Found)
                    {
                        textAddress.Text = AtomSpectraDeviceConsole.FormatAddress(r.Host, AddressPort(), AddressDefaultPort());
                        labelStatus.ForeColor = Color.Green;
                        labelStatus.Text = string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraConsoleStatusConnected, r.Serial, name + " -> " + r.Host);
                        ApplyBoardCalibration(AtomSpectraRestCalibration.ParseDevice(r.DeviceJson));   // board calibration -> Energy Calibration page
                        ShowBoardGzip(r.BootConfigJson);   // the board's reply compression level
                    }
                    else
                    {
                        labelStatus.ForeColor = Color.Red;
                        labelStatus.Text = string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraConsoleStatusNotFound, r.Message);
                    }
                });
            });
            worker.IsBackground = true;
            worker.Name = "AtomSpectraRefresh";
            worker.Start();
        }

        private void textCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Return) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            string cmd = textCommand.Text.Trim();
            if (cmd.Length == 0) return;
            textCommand.AutoCompleteCustomSource.Add(cmd);
            textCommand.Clear();

            if (radioRest.Checked)
            {
                if (checkObserve.Checked)
                {
                    PrependOutput(">> " + cmd + Environment.NewLine + Resources.AtomSpectraConsoleObserveOnly + Environment.NewLine);
                    return;
                }

                var probe = new AtomSpectraTcpDeviceConfig();
                probe.Host = textHost.Text.Trim();
                probe.RestPort = restAddressPort;

                try
                {
                    SaveRest(probe);
                }
                catch (FormatException)
                {
                    return;
                }

                string err;
                AtomSpectraRestClient client = probe.CreateRestClient(out err);
                if (client == null)
                {
                    PrependOutput(string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraConsoleBadAddress, err) + Environment.NewLine);
                    return;
                }

                Thread worker = new Thread(() =>
                {
                    string text = AtomSpectraDeviceConsole.SendRest(client, cmd, 3000);
                    OnUi(delegate { PrependOutput(text); });
                });
                worker.IsBackground = true;
                worker.Name = "AtomSpectraConsole";
                worker.Start();
            }
            else
            {
                string host = textHost.Text.Trim();
                int port = (int)numericPort.Value;
                if (!AtomSpectraTcpDeviceConfig.IsValidHost(host))
                {
                    PrependOutput(string.Format(CultureInfo.CurrentCulture, Resources.AtomSpectraConsoleBadAddress, textAddress.Text) + Environment.NewLine);
                    return;
                }

                Thread worker = new Thread(() =>
                {
                    string text = AtomSpectraDeviceConsole.SendTcp(host, port, cmd, 2000);
                    OnUi(delegate { PrependOutput(text); });
                });
                worker.IsBackground = true;
                worker.Name = "AtomSpectraConsole";
                worker.Start();
            }
        }

        private void PrependOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            textOutput.Text = text + textOutput.Text;
        }

        private void OnUi(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (IsDisposed) return;
                    action();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
