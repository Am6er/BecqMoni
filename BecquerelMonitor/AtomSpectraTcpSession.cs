// The command life cycle of one AtomSpectra TCP measurement (start, stop, clear, detach) over one
// AtomSpectraTcpAcquisition, kept free of WinForms so tests drive it against a mock board.
// The board serves ONE client: the hosts held by this process are registered, and a second
// measurement on the same board is refused at once instead of being turned away by the board.
// Spectrum resets: -rst is sent only by a new document's Start and by an explicit Clear; the
// automatic reconnect sends only -sta (or the deferred -sto), never -rst.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor
{
    public enum AtomSpectraTcpBeginOutcome
    {
        Started,
        NoHost,
        HostBusy,
        ConnectFailed,
        NoAnswer,
        LinkClosed,
        Cancelled,      // REST: "Cancel" in the Start dialog
        Refused         // REST: a headless run cannot ask, and the board holds less than the document
    }

    public enum AtomSpectraTcpStopOutcome
    {
        NotRunning,
        Stopped,
        StoppedAfterReconnect,
        Deferred,
        Detached,       // REST: joined or observe-only acquisition, -sto not sent, polling stopped
        Unconfirmed     // REST: -sto reached the board but no clear answer came back; not repeated
    }

    public sealed class AtomSpectraTcpSession : IAtomSpectraSession
    {
        // IAtomSpectraSession. "Continue" on a counting board joins it (no command); the stream carries
        // the board's ACCUMULATED histogram and time (AtomSpectraTcpDeviceController.UpdateHistogram copies them), so joining is real.
        public string TransportName { get { return "TCP"; } }
        public bool JoinedRunning { get { return joinedRunning; } }
        public double ElapsedAtBeginSeconds { get { return elapsedAtBegin; } }

        // A Start into a new document on a board with data asks "Continue / Start over / Cancel" (the REST dialog);
        // null = no window to ask (headless runs, the TCP suite): the REST rules without windows: a counting board is joined
        // (nothing sent), a stopped board with data is refused (HeadlessAsk, nothing sent), an empty board gets -sta only; no reset without an answer.
        public Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> Ask { get; set; }
        public const int ProbeMs = 2500;   // the bridge sends a status packet about once a second
        bool joinedRunning;
        double elapsedAtBegin;

        public const int ConnectTimeoutMs = 1500;
        public const int StopReconnectWaitMs = 1900;
        public const int DefaultReconnectDelayMs = 60000;

        static readonly object registryLock = new object();
        static readonly Dictionary<string, int> hostsInUse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly List<KeyValuePair<string, AtomSpectraTcpAcquisition>> orphans = new List<KeyValuePair<string, AtomSpectraTcpAcquisition>>();

        readonly string host;
        readonly int port;
        readonly int reconnectDelayMs;
        readonly int keepAliveMs;
        readonly Func<IAtomSpectraTcpTransport> transportFactory;

        AtomSpectraTcpAcquisition acquisition;
        bool registered;
        bool resetOnNextStart;
        bool disposed;
        string lastError;

        public AtomSpectraTcpSession(string host, int port, int reconnectDelayMs, int keepAliveMs, Func<IAtomSpectraTcpTransport> transportFactory)
        {
            this.host = (host ?? string.Empty).Trim();
            this.port = port;
            this.reconnectDelayMs = reconnectDelayMs;
            this.keepAliveMs = keepAliveMs;

            if (transportFactory == null)
            {
                string h = this.host;
                int p = this.port;
                transportFactory = () => new AtomSpectraTcpTransport(h, p, ConnectTimeoutMs);
            }
            this.transportFactory = transportFactory;
        }

        public string Host
        {
            get { return host; }
        }

        public int Port
        {
            get { return port; }
        }

        public bool IsConnected
        {
            get { return acquisition != null && acquisition.IsConnected; }
        }

        public bool IsLinkLost
        {
            get { return acquisition != null && acquisition.IsLinkLost; }
        }

        public bool IsOpen
        {
            get { return acquisition != null; }
        }

        public string LastError
        {
            get { return lastError; }
        }

        public bool ResetOnNextStart
        {
            get { return resetOnNextStart; }
        }

        public event EventHandler<AtomSpectraTcpDataReadyArgs> DataReady;
        public event EventHandler LinkLost;

        public static string HostKey(string host, int port)
        {
            return (host ?? string.Empty).Trim() + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        static readonly List<AtomSpectraTcpSession> liveSessions = new List<AtomSpectraTcpSession>();

        // The open session of this bridge, if any (the settings console sends commands through it instead of a second connection).
        public static AtomSpectraTcpSession FindLive(string host, int port)
        {
            string key = HostKey(host, port);
            lock (registryLock)
            {
                return liveSessions.FirstOrDefault(s => HostKey(s.host, s.port) == key && s.IsConnected);
            }
        }

        public static bool IsHostInUse(string host, int port)
        {
            string key = HostKey(host, port);
            lock (registryLock)
            {
                return hostsInUse.ContainsKey(key);
            }
        }

        public static int OrphanCount(string host, int port)
        {
            string key = HostKey(host, port);
            List<AtomSpectraTcpAcquisition> toDispose = new List<AtomSpectraTcpAcquisition>();
            int count = 0;

            lock (registryLock)
            {
                for (int i = orphans.Count - 1; i >= 0; i--)
                {
                    if (orphans[i].Key != key)
                    {
                        continue;
                    }

                    AtomSpectraTcpAcquisition a = orphans[i].Value;
                    if (a.WaitExit(0))
                    {
                        toDispose.Add(a);
                        orphans.RemoveAt(i);
                    }
                    else
                    {
                        count++;
                    }
                }
            }

            foreach (AtomSpectraTcpAcquisition a in toDispose)
            {
                try
                {
                    a.Dispose();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Orphan dispose failed: " + ex.Message);
                }
            }

            return count;
        }

        public AtomSpectraTcpBeginOutcome Begin(bool newDocument, int commandTimeoutMs)
        {
            if (host.Length == 0)
            {
                return AtomSpectraTcpBeginOutcome.NoHost;
            }

            if (TakeOrphans())
            {
                resetOnNextStart = true;
            }

            if (acquisition == null || !acquisition.IsConnected)
            {
                Close();

                if (IsHostInUse(host, port))
                {
                    AtomSpectraTcpLog.Warn("Session", "board busy: " + host + ":" + port + " is already used by another measurement");
                    lastError = "busy";
                    return AtomSpectraTcpBeginOutcome.HostBusy;
                }

                acquisition = new AtomSpectraTcpAcquisition(transportFactory, reconnectDelayMs, keepAliveMs, host + ":" + port);
                acquisition.DataReady += OnData;
                acquisition.ConnectionLost += OnLost;
                acquisition.Start();

                string key = HostKey(host, port);
                lock (registryLock)
                {
                    int current;
                    if (hostsInUse.TryGetValue(key, out current))
                    {
                        hostsInUse[key] = current + 1;
                    }
                    else
                    {
                        hostsInUse[key] = 1;
                    }
                    liveSessions.Add(this);   // the settings console sends through an open session (FindLive)
                }
                registered = true;

                if (!acquisition.WaitFirstAttempt(ConnectTimeoutMs + 500))
                {
                    lastError = acquisition.LastError ?? "timeout";
                    Close();
                    return AtomSpectraTcpBeginOutcome.ConnectFailed;
                }
            }

            bool accepted = true;
            AtomSpectraRestStartPlan plan = PlanTcpStart(newDocument);   // was "-sto, -rst for every new document"
            if (plan == AtomSpectraRestStartPlan.Cancel)
            {
                Close();
                return AtomSpectraTcpBeginOutcome.Cancelled;
            }
            if (plan == AtomSpectraRestStartPlan.Ask)
            {
                // no window to ask in and the board is stopped with data: refused, nothing is sent to it
                lastError = AtomSpectraRestSession.HeadlessAskText;
                AtomSpectraTcpLog.Warn("Session", "TCP Start refused: " + lastError);
                Close();
                return AtomSpectraTcpBeginOutcome.Refused;
            }
            if (plan == AtomSpectraRestStartPlan.Restart)
            {
                accepted = Command("-sto", commandTimeoutMs) & Command("-rst", commandTimeoutMs);
                if (accepted)
                {
                    resetOnNextStart = false;
                }
            }

            if (plan != AtomSpectraRestStartPlan.Join) accepted &= Command("-sta", commandTimeoutMs);   // Join: the board is not touched

            if (!accepted)
            {
                AtomSpectraTcpBeginOutcome outcome = acquisition.IsLinkLost ? AtomSpectraTcpBeginOutcome.LinkClosed : AtomSpectraTcpBeginOutcome.NoAnswer;
                lastError = acquisition.LastError;
                Close();
                return outcome;
            }

            // never -rst on an automatic reconnect
            acquisition.CommandOnReconnect = "-sta";
            if (plan == AtomSpectraRestStartPlan.Join) acquisition.CommandOnReconnect = null;   // a joined acquisition is not touched on reconnect either
            return AtomSpectraTcpBeginOutcome.Started;
        }

        // Status samples of the stream (board time, counts) until two are >= 1.5 s apart or waitMs passes; no command is sent.
        private AtomSpectraRestBoardSample[] ProbeBoard(int waitMs)
        {
            var samples = new List<AtomSpectraRestBoardSample>();
            Stopwatch sw = Stopwatch.StartNew();
            EventHandler<AtomSpectraTcpDataReadyArgs> h = (o, e) =>
            {
                lock (samples) samples.Add(new AtomSpectraRestBoardSample { HasData = true, Time = e.ElapsedTime, Total = e.Histogram.Sum(x => (long)x), AtMs = sw.ElapsedMilliseconds });
            };
            acquisition.DataReady += h;
            try
            {
                while (sw.ElapsedMilliseconds < waitMs)
                {
                    lock (samples) if (samples.Count >= 2 && samples[samples.Count - 1].AtMs - samples[0].AtMs >= 1500) break;
                    System.Threading.Thread.Sleep(50);
                }
            }
            finally { acquisition.DataReady -= h; }
            lock (samples) return samples.ToArray();
        }

        // the REST rules (AtomSpectraRestPolicy) on what the board's own stream says before any command.
        private AtomSpectraRestStartPlan PlanTcpStart(bool newDocument)
        {
            joinedRunning = false;
            elapsedAtBegin = 0;
            if (resetOnNextStart) return AtomSpectraRestStartPlan.Restart;   // an explicit Clear waits for this Start
            if (!newDocument) return AtomSpectraRestStartPlan.StartNew;       // the same document goes on: -sta, as before
            AtomSpectraRestBoardSample[] s = ProbeBoard(ProbeMs);   // with or without a window; no question possible = the REST rules without windows
            bool counting = s.Length >= 2 ? s[s.Length - 1].Time > s[0].Time : s.Length == 1;   // one sample: unknown, ask
            AtomSpectraRestBoardSample board = s.Length > 0 ? s[s.Length - 1] : AtomSpectraRestBoardSample.NoData(0);
            AtomSpectraRestStartPlan plan = AtomSpectraRestPolicy.PlanStart(false, Ask != null, counting, board, 0, 0);
            if (plan == AtomSpectraRestStartPlan.Ask && Ask != null)
                plan = AtomSpectraRestPolicy.PlanChoice(Ask(new AtomSpectraRestStartQuestion { Counting = counting, Board = board, ContinueAllowed = true }), counting, true);
            if (plan == AtomSpectraRestStartPlan.Join) joinedRunning = true;
            if (plan == AtomSpectraRestStartPlan.Join || plan == AtomSpectraRestStartPlan.Resume) elapsedAtBegin = board.Time;
            AtomSpectraTcpLog.Info("Session", "TCP Start: board " + (s.Length == 0 ? "silent" : counting ? "counting" : "stopped") + ", time " + board.Time + " s, counts " + board.Total + " -> " + plan);
            return plan;
        }

        public AtomSpectraTcpStopOutcome Stop(int commandTimeoutMs)
        {
            if (acquisition == null)
            {
                return AtomSpectraTcpStopOutcome.NotRunning;
            }

            if (joinedRunning)
            {
                // a joined acquisition is not ours to stop (as REST): no -sto, the board keeps counting
                AtomSpectraTcpLog.Info("Session", "Stop of a joined acquisition on " + host + ":" + port + ": detached, -sto not sent");
                Close();
                return AtomSpectraTcpStopOutcome.Detached;
            }

            if (acquisition.IsConnected)
            {
                acquisition.CommandOnReconnect = null;
                if (!Command("-sto", commandTimeoutMs))
                {
                    Trace.WriteLine("Stop command failed: " + acquisition.LastError);
                }
                Close();
                return AtomSpectraTcpStopOutcome.Stopped;
            }

            acquisition.CommandOnReconnect = "-sto";
            acquisition.ExitAfterReconnectCommand = true;
            acquisition.ReconnectNow();

            if (acquisition.WaitExit(StopReconnectWaitMs))
            {
                Close();
                return AtomSpectraTcpStopOutcome.StoppedAfterReconnect;
            }

            // The analyzer may keep counting until the deferred -sto reaches it.
            AtomSpectraTcpLog.Warn("Session", "no link at Stop: -sto deferred until " + host + ":" + port + " answers");
            Orphan();
            return AtomSpectraTcpStopOutcome.Deferred;
        }

        public void Clear(int commandTimeoutMs)
        {
            // The ONLY place that asks for a spectrum reset besides a new document's Start
            // (the user's explicit "Clear").
            if (acquisition == null)
            {
                resetOnNextStart = true;
                return;
            }

            if (acquisition.IsConnected)
            {
                Command("-rst", commandTimeoutMs);
            }
            else
            {
                // Sent once on the next connection; resetOnNextStart stays false, so a later
                // continuation Start does not reset again.
                acquisition.QueueOnReconnect("-rst");
                AtomSpectraTcpLog.Warn("Session", "Clear without a link: -rst queued for the next connection");
            }
        }

        public void Detach()
        {
            // No -sto: the analyzer keeps collecting.
            Close();
        }

        public string Query(string command, int timeoutMs)
        {
            if (acquisition == null || !acquisition.IsConnected)
            {
                return null;
            }

            acquisition.SendCommand(command);
            string answer = acquisition.GetCommandOutput(timeoutMs);
            AtomSpectraTcpLog.Info("TCP", "sent " + command + ": " + (answer == null ? "no answer" : answer.Length + " chars"));
            if (answer != null)   // the text itself only at Debug (hidden by default): -inf may carry the serial
                AtomSpectraTcpLog.Debug("TCP", "answer to " + command + ": " + answer.Trim().Replace("\r\n", " | ").Replace("\n", " | ").Replace("\r", " | "));
            return answer;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Close();
        }

        bool Command(string command, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            acquisition.SendCommand(command);
            bool ok = acquisition.WaitForAnswer("-ok", timeoutMs);
            string text = "sent " + command + " to " + host + ":" + port + ": " + (ok ? "-ok" : "no answer") + " in " + sw.ElapsedMilliseconds + " ms";
            if (ok) AtomSpectraTcpLog.Info("TCP", text); else AtomSpectraTcpLog.Warn("TCP", text);
            return ok;
        }

        void OnData(object sender, AtomSpectraTcpDataReadyArgs e)
        {
            EventHandler<AtomSpectraTcpDataReadyArgs> handler = DataReady;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        void OnLost(object sender, EventArgs e)
        {
            EventHandler handler = LinkLost;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        void Close()
        {
            if (acquisition == null)
            {
                return;
            }

            if (acquisition.PendingOneShot("-rst"))
            {
                // The user's Clear did not reach the device.
                resetOnNextStart = true;
            }

            acquisition.DataReady -= OnData;
            acquisition.ConnectionLost -= OnLost;
            acquisition.CommandOnReconnect = null;

            try
            {
                acquisition.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Acquisition dispose failed: " + ex.Message);
            }

            Unregister();
            acquisition = null;
        }

        void Unregister()
        {
            if (!registered)
            {
                return;
            }

            string key = HostKey(host, port);
            lock (registryLock)
            {
                int current;
                if (hostsInUse.TryGetValue(key, out current))
                {
                    if (current <= 1)
                    {
                        hostsInUse.Remove(key);
                    }
                    else
                    {
                        hostsInUse[key] = current - 1;
                    }
                }
                liveSessions.Remove(this);
            }
            registered = false;
        }

        void Orphan()
        {
            acquisition.DataReady -= OnData;
            acquisition.ConnectionLost -= OnLost;

            string key = HostKey(host, port);
            lock (registryLock)
            {
                orphans.Add(new KeyValuePair<string, AtomSpectraTcpAcquisition>(key, acquisition));
            }

            Unregister();
            acquisition = null;
        }

        // Removes and disposes the orphans of THIS board; true if one of them still held an unsent Clear (-rst).
        bool TakeOrphans()
        {
            List<AtomSpectraTcpAcquisition> toDispose = new List<AtomSpectraTcpAcquisition>();
            bool anyPendingRst = false;

            lock (registryLock)
            {
                string key = HostKey(host, port);
                for (int i = orphans.Count - 1; i >= 0; i--)
                {
                    if (orphans[i].Key != key)
                    {
                        continue;
                    }
                    AtomSpectraTcpAcquisition a = orphans[i].Value;
                    if (a.PendingOneShot("-rst"))
                    {
                        anyPendingRst = true;
                    }
                    toDispose.Add(a);
                    orphans.RemoveAt(i);
                }
            }

            foreach (AtomSpectraTcpAcquisition a in toDispose)
            {
                try
                {
                    a.Dispose();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Orphan dispose failed: " + ex.Message);
                }
            }

            return anyPendingRst;
        }
    }
}
