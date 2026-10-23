// REST mode: the "Board log" tab reads GET /api/devlog?since=N of the board in use (AtomSpectraRestClient.Current) on a worker thread; the status line shows how old the board data is.
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public sealed partial class AtomSpectraTcpLogForm
    {
        private TabControl tabs;
        private TextBox devlogBox;
        private Button devlogRefresh;
        private System.Windows.Forms.Timer devlogTimer;
        private long devlogNext;
        private int devlogBusy;

        private Control BuildTabs(Control logBox)
        {
            tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage pageLog = new TabPage(Resources.AtomSpectraTcpLogTitle);
            logBox.Dock = DockStyle.Fill;
            pageLog.Controls.Add(logBox);

            TabPage pageBoard = new TabPage(Resources.AtomSpectraTcpLogDevlog);
            devlogBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = logBox.Font };
            devlogRefresh = new Button { Text = Resources.AtomSpectraTcpLogRefresh, Dock = DockStyle.Top, Height = 26 };
            devlogRefresh.Click += (s, e) => RefreshDevlog();
            pageBoard.Controls.Add(devlogBox);
            pageBoard.Controls.Add(devlogRefresh);

            tabs.TabPages.Add(pageLog);
            tabs.TabPages.Add(pageBoard);

            devlogTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            devlogTimer.Tick += (s, e) => { if (tabs.SelectedIndex == 1) RefreshDevlog(); };
            devlogTimer.Start();

            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedIndex == 1) RefreshDevlog(); };
            FormClosed += (s, e) => { devlogTimer.Stop(); devlogTimer.Dispose(); };

            return tabs;
        }

        private void RefreshDevlog()
        {
            AtomSpectraRestClient client = AtomSpectraRestClient.Current;
            if (client == null)
            {
                devlogBox.Text = Resources.AtomSpectraTcpLogNoBoard;
                return;
            }

            if (Interlocked.CompareExchange(ref devlogBusy, 1, 0) != 0) return;

            long since = devlogNext;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text = null;
                long next = since;
                string error = null;

                try
                {
                    AtomSpectraRestResponse r = client.Get("/api/devlog?since=" + since.ToString(CultureInfo.InvariantCulture), 3000);
                    if (!r.Ok)
                        error = AtomSpectraDeviceConsole.Problem(r);
                    else
                        text = FormatDevlog(r.Body, since, out next);
                }
                catch (FormatException)
                {
                    error = Resources.AtomSpectraRestBadJson;
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("Devlog", "board log not read: " + ex.Message);
                    error = Resources.AtomSpectraRestReadFailed;
                }
                finally
                {
                    Interlocked.Exchange(ref devlogBusy, 0);
                }

                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (IsDisposed) return;
                        if (error != null)
                        {
                            AppendDevlog(string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpLogDevlogFailed, AtomSpectraTcpLog.Mask(client.BaseUrl), error) + Environment.NewLine);
                            return;
                        }
                        devlogNext = next;
                        if (text.Length > 0) AppendDevlog(text);
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        // the board log tab keeps the newest lines within the limits of the console output window
        private void AppendDevlog(string text)
        {
            devlogBox.AppendText(text);
            if (devlogBox.TextLength > AtomSpectraDeviceConsole.OutputMaxChars || devlogBox.GetLineFromCharIndex(devlogBox.TextLength) >= AtomSpectraDeviceConsole.OutputMaxLines)
            {
                // trimmed to three quarters, so a long session does not rewrite the box on every refresh
                devlogBox.Text = AtomSpectraDeviceConsole.ClipTail(devlogBox.Text, AtomSpectraDeviceConsole.OutputMaxChars * 3 / 4, AtomSpectraDeviceConsole.OutputMaxLines * 3 / 4);
                devlogBox.SelectionStart = devlogBox.TextLength;
                devlogBox.ScrollToCaret();
            }
        }

        public static string FormatDevlog(string json, long since, out long next)
        {
            return AtomSpectraDevlog.Format(json, since, out next);   // moved to AtomSpectraDevlog (reused by the settings console)
        }

        private static string StaleSuffix()
        {
            Func<double> age = AtomSpectraTcpLog.DataAgeSeconds;
            if (age == null) return "";

            double a;
            try { a = age(); }
            catch (Exception) { return ""; }

            if (a < 0) return "";

            string f = a > AtomSpectraTcpLog.StaleAfterSeconds ? Resources.AtomSpectraTcpLogStale : Resources.AtomSpectraTcpLogFresh;
            return "  |  " + string.Format(CultureInfo.InvariantCulture, f, (int)a);
        }
    }
}
