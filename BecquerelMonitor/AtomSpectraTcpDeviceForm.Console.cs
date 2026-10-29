// Device features of the AtomSpectra network type (after the COM type AtomSpectraVCPDeviceForm, which
// is not changed): address host[:port] instead of COM port and baud rate, Refresh (name -> GET /api/device), status with the
// serial number, and a command line with its output window. Logic is in AtomSpectraDeviceConsole (testable without WinForms).

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
        private Label labelAddress;
        private TextBox textAddress;
        private Button buttonRefresh;
        private Label labelStatus;
        private Label labelPollHint;
        private Label labelConsole;
        private TextBox textCommand;
        private TextBox textOutput;
        private ToolTip toolTip;   // in components: disposed with the page
        private int restAddressPort = AtomSpectraTcpDeviceConfig.DefaultRestPort;

        // The question before a command that changes the board ("No" is the default button); the test harness answers it.
        public static Func<string, bool> ConfirmCommand = text => AppUi.AskYesNo(text, Resources.AtomSpectraConsoleConfirmTitle, MessageBoxIcon.Warning, true);

        // The board choice window (the test harness replaces it to see the call without a modal window).
        public static Func<IWin32Window, IList<AtomSpectraBoardEntry>, AtomSpectraBoardEntry> ChooseWindow = AtomSpectraBoardChooser.Choose;

        private void BuildConsoleControls()
        {
            if (components == null) components = new System.ComponentModel.Container();
            toolTip = new ToolTip(components);

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
                AutoEllipsis = true,   // a long reason ends with "..." inside its two lines; the whole text is in the tooltip
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
            toolTip.SetToolTip(labelPollHint, AppUi.WrapTip(Resources.AtomSpectraConsolePollTcpHint));   // the full text on hover

            labelConsole = new Label
            {
                AutoSize = true,
                Location = new Point(17, 730),   // +30: the result label above is 90 high (room for the firmware warning)
                Text = Resources.AtomSpectraConsoleWarning
            };
            Controls.Add(labelConsole);

            textCommand = new TextBox
            {
                Location = new Point(17, 750),
                Width = 438,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.CustomSource,
                AutoCompleteCustomSource = new AutoCompleteStringCollection()
            };
            textCommand.KeyDown += textCommand_KeyDown;
            Controls.Add(textCommand);

            textOutput = new TextBox
            {
                Location = new Point(17, 776),
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
            SetStatus(SystemColors.ControlText, "");
            textOutput.Text = "";
            labelPollHint.Visible = !radioRest.Checked;
            buttonRefresh.Enabled = true;
        }

        // the status line holds two lines; a longer reason is cut with "...", the tooltip has all of it
        private void SetStatus(Color color, string text)
        {
            labelStatus.ForeColor = color;
            labelStatus.Text = text;
            toolTip.SetToolTip(labelStatus, string.IsNullOrEmpty(text) ? null : AppUi.WrapTip(text));
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
            string name = AtomSpectraBoardFinder.NameToSearch(textAddress.Text, AddressDefaultPort());   // typed text is resolved as typed, only an empty field or the default name browses

            int generation = ++refreshGeneration;
            int page = pageGeneration;
            bool proxy = !checkNoProxy.Checked;
            bool rest = radioRest.Checked;
            int httpPort = rest ? restAddressPort : AtomSpectraTcpDeviceConfig.DefaultRestPort;

            // a disabled button that has the focus hands it to another control (the command line at the bottom) and the
            // AutoScroll page scrolls after it: the focus goes to the address field (in view) first and the page position is kept.
            Point scrollPos = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            textAddress.Focus();
            buttonRefresh.Enabled = false;
            AutoScrollPosition = scrollPos;
            // the default name is not looked up by itself first: the network is browsed (the status said "searching atomspectra.local" - seen on screen)
            SetStatus(Color.Gray, AtomSpectraBoardFinder.IsDefaultName(name) ? Resources.AtomSpectraConsoleStatusBrowsing
                : string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleStatusSearching, name));

            Thread worker = new Thread(() =>
            {
                // a typed address that answers is read as it is; else the DNS-SD browse of every board; several -> the choice window;
                // none -> the old one-name path. restPort: the REST port the board was read on (TXT rest of its announcement)
                int restPort;
                var r = AtomSpectraBoardFinder.Find(name, httpPort, proxy, 3000, boards => ChooseBoard(boards, generation, page), out restPort);
                OnUi(delegate
                {
                    if (generation == refreshGeneration) buttonRefresh.Enabled = true;   // the last search is over
                    if (generation != refreshGeneration || page != pageGeneration) return;   // a newer search, or another configuration on the page
                    if (r.Cancelled)
                    {
                        SetStatus(Color.Gray, Resources.AtomSpectraBoardChoiceCancelled);
                    }
                    else if (r.Found)
                    {
                        // REST: the address with the port the board answered on (AddressChanged takes it into the REST port)
                        textAddress.Text = AtomSpectraDeviceConsole.FormatAddress(r.Host, rest && radioRest.Checked ? restPort : AddressPort(), AddressDefaultPort());
                        SetStatus(Color.Green, string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleStatusConnected, r.Serial, (r.FoundAs ?? name) + " -> " + r.Host));
                        ApplyBoardCalibration(AtomSpectraRestCalibration.ParseDevice(r.DeviceJson));   // board calibration -> Energy Calibration page
                        ShowBoardGzip(r.BootConfigJson);   // the board's reply compression level
                    }
                    else
                    {
                        SetStatus(Color.Red, string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleStatusNotFound, r.Message));
                    }
                });
            });
            worker.IsBackground = true;
            worker.Name = "AtomSpectraRefresh";
            worker.Start();
        }

        // called on the "Refresh" worker thread when the person has to choose; the window runs on the UI thread, and only for the
        // search still current on the page (ChoiceRefused): not chosen = the search ends as cancelled
        private AtomSpectraBoardEntry ChooseBoard(IList<AtomSpectraBoardEntry> boards, int generation, int page)
        {
            AtomSpectraBoardEntry pick = null;
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    string refused = ChoiceRefused(generation, page);
                    if (refused != null)
                    {
                        AtomSpectraTcpLog.Info("Refresh", "the board choice is not shown: " + refused);
                        return;
                    }
                    pick = ChooseWindow(this, boards);
                }));
            }
            catch (InvalidOperationException) { }   // the settings window was closed meanwhile: nothing chosen
            return pick;
        }

        // Why the choice window is not shown (null: it is shown). The search is ~8 s long: a modal window then must not come up for a
        // search the page has left - a newer one, another configuration (LoadFormContents), a closed or hidden page - nor over a window
        // of the settings form itself (the "Save changes?" question while it closes disables the form).
        private string ChoiceRefused(int generation, int page)
        {
            if (IsDisposed || !Visible) return "the page is closed or hidden";
            if (generation != refreshGeneration) return "a newer search was started";
            if (page != pageGeneration) return "another configuration is on the page";
            Form top = FindForm();
            if (top == null || !top.CanFocus) return "another window of the settings form is open";
            return null;
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

            // "Observe only" holds in both modes: nothing is sent to the board from here
            if (checkObserve.Checked)
            {
                PrependOutput(">> " + cmd + Environment.NewLine + (radioRest.Checked ? Resources.AtomSpectraConsoleObserveOnly : Resources.AtomSpectraConsoleObserveOnlyTcp) + Environment.NewLine);
                return;
            }

            int page = pageGeneration;
            if (radioRest.Checked)
            {
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
                    PrependOutput(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleBadAddress, err) + Environment.NewLine);
                    return;
                }

                if (!CommandConfirmed(cmd, AtomSpectraTcpLog.Mask(client.BaseUrl), false)) return;

                Thread worker = new Thread(() =>
                {
                    string text = AtomSpectraDeviceConsole.SendRest(client, cmd, 3000);
                    OnUi(page, delegate { PrependOutput(text); });
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
                    PrependOutput(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleBadAddress, textAddress.Text) + Environment.NewLine);
                    return;
                }

                // a running measurement of this program on this bridge takes the command into its own session
                bool live = AtomSpectraTcpSession.IsHostInUse(host, port) || AtomSpectraTcpSession.FindLive(host, port) != null;
                if (!CommandConfirmed(cmd, AtomSpectraDeviceConsole.FormatAddress(host, port, 0), live)) return;

                Thread worker = new Thread(() =>
                {
                    string text = AtomSpectraDeviceConsole.SendTcp(host, port, cmd, 2000);
                    OnUi(page, delegate { PrependOutput(text); });
                });
                worker.IsBackground = true;
                worker.Name = "AtomSpectraConsole";
                worker.Start();
            }
        }

        // A command that may change the board (anything but a known query) is sent only after "Yes"; "No" is the default.
        private bool CommandConfirmed(string cmd, string board, bool liveHere)
        {
            AtomSpectraCommandKind kind = AtomSpectraDeviceConsole.Classify(cmd);
            if (kind == AtomSpectraCommandKind.Query) return true;

            string text = string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraConsoleConfirm, cmd, board)
                + Environment.NewLine + Environment.NewLine + AtomSpectraDeviceConsole.EffectText(kind)
                + Environment.NewLine + (liveHere ? Resources.AtomSpectraConsoleLiveTcp : Resources.AtomSpectraConsoleLiveMaybe);
            bool yes;
            try
            {
                yes = ConfirmCommand(text);
            }
            catch (InvalidOperationException)
            {
                yes = false;   // no window to ask in: not sent
            }
            AtomSpectraTcpLog.Info("Console", "command " + cmd + " to " + board + " (" + kind + "): " + (yes ? "confirmed" : "declined, not sent"));
            if (!yes) PrependOutput(">> " + cmd + Environment.NewLine + Resources.AtomSpectraConsoleDeclined + Environment.NewLine);
            return yes;
        }

        // the console writes on top; the window keeps the newest text within the limits
        private void PrependOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            textOutput.Text = AtomSpectraDeviceConsole.ClipHead(text + textOutput.Text, AtomSpectraDeviceConsole.OutputMaxChars, AtomSpectraDeviceConsole.OutputMaxLines);
        }

        // On the UI thread, if the page still shows the configuration the work was started for (page = pageGeneration then).
        private void OnUi(int page, MethodInvoker action)
        {
            OnUi(delegate { if (page == pageGeneration) action(); });
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
