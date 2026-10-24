// The command life cycle of one AtomSpectra TCP measurement (start, stop, clear, detach) over one
// AtomSpectraTcpAcquisition, kept free of WinForms so tests drive it against a mock board.
// The board serves ONE client: the hosts held by this process are registered, and a second
// measurement on the same board is refused at once instead of being turned away by the board.
// Spectrum resets: -rst is sent only by a new document's Start and by an explicit Clear; the
// automatic reconnect sends only -sta (or the deferred -sto), never -rst.
// A drop of the board's counters that is not our own -rst (a reset from the web page, the button, a power cycle) is never
// written over the document: the snapshots stop and BoardReset is raised (as REST, AtomSpectraRestGuard).

using BecquerelMonitor.Properties;
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

    // What the probe at Start saw of the board (the stream of the bridge, no command sent).
    public enum AtomSpectraTcpBoardState
    {
        NotProbed,      // no probe: an explicit Clear waits
        Silent,         // no complete cycle in ProbeMs: nothing is known about the board
        Uncertain,      // one sample, or two close ones with the same time: counting or stopped is not known
        Counting,
        Stopped
    }

    // Why a Start without a window was refused (the controller picks the text).
    public enum AtomSpectraTcpRefusal
    {
        None,
        StoppedWithData,
        Unknown,        // the bridge did not show the analyzer's state (Silent / Uncertain)
        BehindDocument
    }

    // The board's counters went down without our -rst: the numbers before and after (Channel: the first channel that went down, -1: none).
    public sealed class AtomSpectraTcpBoardResetArgs : EventArgs
    {
        public long TimeBefore { get; private set; }
        public long TimeNow { get; private set; }
        public long TotalBefore { get; private set; }
        public long TotalNow { get; private set; }
        public int Channel { get; private set; }

        public AtomSpectraTcpBoardResetArgs(long timeBefore, long timeNow, long totalBefore, long totalNow, int channel)
        {
            TimeBefore = timeBefore;
            TimeNow = timeNow;
            TotalBefore = totalBefore;
            TotalNow = totalNow;
            Channel = channel;
        }

        public string LogText()
        {
            return "time " + TimeBefore.ToString(CultureInfo.InvariantCulture) + " -> " + TimeNow.ToString(CultureInfo.InvariantCulture)
                + " s, total " + TotalBefore.ToString(CultureInfo.InvariantCulture) + " -> " + TotalNow.ToString(CultureInfo.InvariantCulture)
                + (Channel >= 0 ? ", channel " + Channel.ToString(CultureInfo.InvariantCulture) + " went down" : "");
        }
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

    public sealed partial class AtomSpectraTcpSession : IAtomSpectraSession
    {
        // IAtomSpectraSession. "Continue" on a counting board joins it (no command); the stream carries
        // the board's ACCUMULATED histogram and time (AtomSpectraTcpDeviceController.UpdateHistogram copies them), so joining is real.
        public string TransportName { get { return "TCP"; } }
        public bool JoinedRunning { get { return joinedRunning; } }
        public double ElapsedAtBeginSeconds { get { return elapsedAtBegin; } }

        // A Start on a board with data, or into a document with data, asks "Continue / Start over / Replace / Cancel" (the REST dialog);
        // null = no window to ask (headless runs, the TCP suite): the REST rules without windows: a counting board is joined
        // (nothing sent), a stopped board with data is refused (HeadlessAsk, nothing sent), an empty board gets -sta only; no reset without an answer.
        public Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> Ask { get; set; }
        public const int ProbeMs = 2500;   // the bridge sends a status packet about once a second
        // Two samples this far apart with the same board time = a stopped board; closer ones (or one) tell nothing.
        public const int ProbeSpanMs = 1500;
        // Four probe windows = about ten cycles of the bridge (one a second): no complete cycle for this long while the socket
        // stays open is a lost link (the acquisition reconnects as on any loss). The same span bounds our own reset: a drop of
        // the counters this long after a -rst we sent is a reset from outside.
        public const int NoDataLinkLostMs = 4 * ProbeMs;
        public const int OwnResetWindowMs = 4 * ProbeMs;
        bool joinedRunning;
        bool replaced;   // "Replace document spectrum": the guard starts from the board as the probe saw it, not from the document
        double elapsedAtBegin;
        public bool ReplacedDocument { get { return replaced; } }

        // What the document holds at Start (set by the controller, as REST): the question shows it, "Continue" is refused
        // when the board holds less, and the document is the first reference of the reset guard.
        public long DocumentSeconds { get; set; }
        public long DocumentCounts { get; set; }
        public AtomSpectraTcpBoardState BoardState { get; private set; }
        public AtomSpectraTcpRefusal Refusal { get; private set; }
        // Stop sent -sto on a live link and got no answer (the controller says so instead of "no connection").
        public bool StopUnanswered { get; private set; }

        // The reset guard: the last snapshot given to the document; armed by a successful Begin, disarmed by Stop/Detach/Close.
        readonly object guardLock = new object();
        bool guardArmed, guardTripped, guardHasLast;
        long guardTime, guardTotal;
        int[] guardBins;
        // The board as the probe saw it when Start joins or resumes it (the first reference of the guard), and the last
        // board numbers known (the probe, then every snapshot given to the document).
        long probeTime, probeTotal, boardTime, boardTotal;

        // The board's counters went down without our -rst: raised once, on the reader thread; no snapshot is given after it.
        public event EventHandler<AtomSpectraTcpBoardResetArgs> BoardReset;

        // The board's last known time and counts (the Clear question shows them).
        public long BoardSeconds { get { lock (guardLock) return boardTime; } }
        public long BoardCounts { get { lock (guardLock) return boardTotal; } }

        // True when Clear sends nothing now (no open acquisition): the reset waits for the next Start (resetOnNextStart, see Clear).
        public bool ClearDeferred { get { return acquisition == null; } }

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

        // The controller hands over a reset that a closed session of the same document still owed: the next Start sends -rst.
        public void RequestResetOnNextStart()
        {
            resetOnNextStart = true;
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
            DisarmGuard();
            Refusal = AtomSpectraTcpRefusal.None;
            StopUnanswered = false;
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
                    AtomSpectraTcpLog.Warn("Session", "board busy: " + HostKey(host, port) + " is already used by another measurement");
                    lastError = "busy";
                    return AtomSpectraTcpBeginOutcome.HostBusy;
                }

                acquisition = new AtomSpectraTcpAcquisition(transportFactory, reconnectDelayMs, keepAliveMs, HostKey(host, port));
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
                    lastError = acquisition.LastError ?? string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraTcpErrTimeout, ConnectTimeoutMs + 500);
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
                // no window to ask in and the board is stopped with data (or its state is not known): refused, nothing is sent to it
                if (Refusal == AtomSpectraTcpRefusal.None) Refusal = AtomSpectraTcpRefusal.StoppedWithData;
                lastError = Refusal == AtomSpectraTcpRefusal.Unknown
                    ? "the bridge did not show the state of the analyzer, and there is no window to ask what to do; nothing was sent to the board"
                    : "the analyzer is stopped and holds a measurement, and there is no window to ask what to do with it; nothing was sent to the board";
                AtomSpectraTcpLog.Warn("Session", "TCP Start refused: " + lastError);
                Close();
                return AtomSpectraTcpBeginOutcome.Refused;
            }
            if (plan == AtomSpectraRestStartPlan.RefuseHeadless)
            {
                // the board holds less than the document and there is no window to ask: refused, nothing is sent to it
                Refusal = AtomSpectraTcpRefusal.BehindDocument;
                lastError = "the analyzer holds less than the document, and there is no window to ask what to do; nothing was sent to the board";
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
            ArmGuard();
            acquisition.NoDataLimitMs = NoDataLinkLostMs;
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
            replaced = false;
            elapsedAtBegin = 0;
            probeTime = probeTotal = 0;
            BoardState = AtomSpectraTcpBoardState.NotProbed;
            if (resetOnNextStart) return AtomSpectraRestStartPlan.Restart;   // an explicit Clear waits for this Start
            // the same document is probed as well (as REST): a board behind the document gets nothing before the answer
            AtomSpectraRestBoardSample[] s = ProbeBoard(ProbeMs);   // with or without a window; no question possible = the REST rules without windows
            bool grew = s.Length >= 2 && s[s.Length - 1].Time > s[0].Time;
            // counting only when the board time grew, stopped only when it stood still over ProbeSpanMs; no sample, one sample or
            // two close ones tell nothing (a slow bridge): the board is then not taken as counting, so "Continue" sends -sta
            // (harmless on a counting analyzer, the reconnect sends it too) and Stop sends -sto
            bool known = grew || (s.Length >= 2 && s[s.Length - 1].AtMs - s[0].AtMs >= ProbeSpanMs);
            BoardState = s.Length == 0 ? AtomSpectraTcpBoardState.Silent : !known ? AtomSpectraTcpBoardState.Uncertain
                : grew ? AtomSpectraTcpBoardState.Counting : AtomSpectraTcpBoardState.Stopped;
            bool counting = grew;
            AtomSpectraRestBoardSample board = s.Length > 0 ? s[s.Length - 1] : AtomSpectraRestBoardSample.NoData(0);
            AtomSpectraRestStartPlan plan = AtomSpectraRestPolicy.PlanStart(false, Ask != null, counting, board, DocumentSeconds, DocumentCounts);
            // a silent bridge says nothing about the board, its spectrum included: never a blind -sta - a question, or a refusal without a window
            if (BoardState == AtomSpectraTcpBoardState.Silent) plan = AtomSpectraRestStartPlan.Ask;
            // ... except the same document after its own Stop ("pause - continue"): a silent bridge gets -sta at once, as before; Uncertain keeps the question
            if (BoardState == AtomSpectraTcpBoardState.Silent && !newDocument) plan = AtomSpectraRestStartPlan.StartNew;
            if (!known && Ask == null && plan == AtomSpectraRestStartPlan.Ask) Refusal = AtomSpectraTcpRefusal.Unknown;
            bool continueAllowed = !AtomSpectraRestPolicy.BoardBehindDocument(board, DocumentSeconds, DocumentCounts);
            // the same document after its own Stop ("pause - continue"): a board seen clearly, not behind the document, gets -sta as before, no question
            if (plan == AtomSpectraRestStartPlan.Ask && !newDocument && known && board.HasData && continueAllowed) plan = AtomSpectraRestStartPlan.StartNew;
            if (plan == AtomSpectraRestStartPlan.Ask && Ask != null)
            {
                AtomSpectraRestStartQuestion q = new AtomSpectraRestStartQuestion
                {
                    Counting = counting,
                    Board = board,
                    DocumentSeconds = DocumentSeconds,
                    DocumentCounts = DocumentCounts,
                    ContinueAllowed = continueAllowed,
                    ReplaceAllowed = AtomSpectraRestPolicy.ReplaceAllowed(board, DocumentSeconds, DocumentCounts, continueAllowed)
                };
                AtomSpectraRestStartChoice choice = Ask(q);
                plan = AtomSpectraRestPolicy.PlanChoice(choice, counting, continueAllowed, AtomSpectraRestPolicy.BoardHasData(board));
                replaced = choice == AtomSpectraRestStartChoice.Replace && plan != AtomSpectraRestStartPlan.Cancel;
            }
            if (plan == AtomSpectraRestStartPlan.Restart && (DocumentSeconds > 0 || DocumentCounts > 0))
            {
                // the reset would make the new acquisition overwrite this document's data (the dialog does not offer it either)
                AtomSpectraTcpLog.Warn("Session", "TCP Start over refused: the document holds data");
                plan = AtomSpectraRestStartPlan.Cancel;
            }
            if (plan == AtomSpectraRestStartPlan.Join) joinedRunning = true;
            if (plan == AtomSpectraRestStartPlan.Join || plan == AtomSpectraRestStartPlan.Resume)
            {
                elapsedAtBegin = board.Time;
                probeTime = board.Time;
                probeTotal = board.Total;
            }
            lock (guardLock)
            {
                boardTime = board.Time;
                boardTotal = board.Total;
            }
            AtomSpectraTcpLog.Info("Session", "TCP Start: board " + BoardState + " (" + s.Length.ToString(CultureInfo.InvariantCulture) + " samples), time "
                + board.Time.ToString(CultureInfo.InvariantCulture) + " s, counts " + board.Total.ToString(CultureInfo.InvariantCulture)
                + ", document " + DocumentSeconds.ToString(CultureInfo.InvariantCulture) + " s, " + DocumentCounts.ToString(CultureInfo.InvariantCulture) + " counts -> " + plan
                + (replaced ? " (the document's spectrum is replaced by the board's)" : ""));
            return plan;
        }

        public AtomSpectraTcpStopOutcome Stop(int commandTimeoutMs)
        {
            if (stopQuestionOpen)
            {
                // a second Stop (the preset, the timer) while the user is still answering the first: the answer decides, this one does nothing
                AtomSpectraTcpLog.Info("Session", "Stop refused: the Stop question of " + HostKey(host, port) + " is still open");
                return AtomSpectraTcpStopOutcome.NotRunning;
            }
            StopUnanswered = false;
            if (acquisition == null)
            {
                return AtomSpectraTcpStopOutcome.NotRunning;
            }

            DisarmGuard();
            if (joinedRunning && !StopJoinedOnBoard())
            {
                // a joined acquisition is not ours to stop (as REST): no -sto, the board keeps counting
                AtomSpectraTcpLog.Info("Session", "Stop of a joined acquisition on " + HostKey(host, port) + ": detached, -sto not sent");
                Close();
                return AtomSpectraTcpStopOutcome.Detached;
            }

            if (acquisition.IsConnected)
            {
                acquisition.CommandOnReconnect = null;
                if (Command("-sto", commandTimeoutMs))
                {
                    Close();
                    return AtomSpectraTcpStopOutcome.Stopped;
                }

                // A live socket and no answer: the stop is not confirmed, so it is not "Stopped". The link is dropped and -sto goes
                // again on a new connection; if that does not happen at once it stays deferred, as with no link, and the user is told.
                StopUnanswered = true;
                AtomSpectraTcpLog.Warn("Session", "no answer to -sto from " + HostKey(host, port) + ": the link is dropped to send it again on a new connection");
                acquisition.ConnectionLost -= OnLost;   // this drop is ours, not a lost link to announce
                acquisition.NoDataLimitMs = 0;
                acquisition.ReconnectAndSend("-sto");
            }
            else
            {
                acquisition.CommandOnReconnect = "-sto";
                acquisition.ExitAfterReconnectCommand = true;
                acquisition.ReconnectNow();
            }

            if (acquisition.WaitExit(StopReconnectWaitMs))
            {
                Close();
                return AtomSpectraTcpStopOutcome.StoppedAfterReconnect;
            }

            // The analyzer may keep counting until the deferred -sto reaches it.
            AtomSpectraTcpLog.Warn("Session", "no link at Stop: -sto deferred until " + HostKey(host, port) + " answers");
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
            AtomSpectraTcpLog.Info("TCP", "sent " + command + ": " + (answer == null ? "no answer" : answer.Length.ToString(CultureInfo.InvariantCulture) + " chars"));
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
            string text = "sent " + command + " to " + HostKey(host, port) + ": " + (ok ? "-ok" : "no answer") + " in " + sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms";
            if (ok) AtomSpectraTcpLog.Info("TCP", text); else AtomSpectraTcpLog.Warn("TCP", text);
            return ok;
        }

        void OnData(object sender, AtomSpectraTcpDataReadyArgs e)
        {
            AtomSpectraTcpBoardResetArgs reset;
            if (!Accept(e, sender as AtomSpectraTcpAcquisition, out reset))
            {
                if (reset != null)
                {
                    AtomSpectraTcpAcquisition a = acquisition;
                    if (a != null) a.CommandOnReconnect = null;   // the board's acquisition is not ours any more: no -sta on a reconnect
                    AtomSpectraTcpLog.Error("Session", "the counters of " + HostKey(host, port) + " went down without our -rst (" + reset.LogText()
                        + "): reset from outside; the snapshots no longer go to the document");
                    EventHandler<AtomSpectraTcpBoardResetArgs> resetHandler = BoardReset;
                    if (resetHandler != null) resetHandler(this, reset);
                }
                return;
            }

            EventHandler<AtomSpectraTcpDataReadyArgs> handler = DataReady;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        // The first reference of the guard is the document at Start (its time and counts) or, when Start joined or resumed
        // the board, what the probe saw if that is more; then every snapshot given to the document.
        void ArmGuard()
        {
            lock (guardLock)
            {
                guardArmed = true;
                guardTripped = false;
                // Replace: the document's numbers are given up, the board's smaller spectrum is no reset from outside
                guardTime = replaced ? probeTime : Math.Max(DocumentSeconds, probeTime);
                guardTotal = replaced ? probeTotal : Math.Max(DocumentCounts, probeTotal);
                guardHasLast = guardTime > 0 || guardTotal > 0;
                guardBins = null;
            }
        }

        void DisarmGuard()
        {
            lock (guardLock)
            {
                guardArmed = false;
            }
        }

        // false: the snapshot does not go to the document (reset != null: just found out that the board was reset from outside).
        // A drop of the time, of the total or of any channel is a reset; it is ours when a -rst went to the board
        // OwnResetWindowMs before at most (Clear, Start over, a queued Clear on the reconnect, the console).
        bool Accept(AtomSpectraTcpDataReadyArgs e, AtomSpectraTcpAcquisition a, out AtomSpectraTcpBoardResetArgs reset)
        {
            reset = null;
            long total = e.Histogram.Sum(x => (long)x);
            bool ownReset = false;
            lock (guardLock)
            {
                if (!guardArmed) return true;
                if (guardTripped) return false;
                if (guardHasLast)
                {
                    int channel = FirstDecrease(guardBins, e.Histogram);
                    if (e.ElapsedTime < guardTime || total < guardTotal || channel >= 0)
                    {
                        long resetAt = a == null ? long.MinValue : a.ResetWrittenAtMs;
                        ownReset = resetAt != long.MinValue && a.ClockMs - resetAt <= OwnResetWindowMs;
                        if (!ownReset)
                        {
                            guardTripped = true;
                            reset = new AtomSpectraTcpBoardResetArgs(guardTime, e.ElapsedTime, guardTotal, total, channel);
                            return false;
                        }
                    }
                }
                guardHasLast = true;
                guardTime = boardTime = e.ElapsedTime;
                guardTotal = boardTotal = total;
                guardBins = e.Histogram;
            }
            if (ownReset) AtomSpectraTcpLog.Info("Session", "counters went down after our -rst: the new acquisition of " + HostKey(host, port) + " goes on");
            return true;
        }

        static int FirstDecrease(int[] before, int[] now)
        {
            if (before == null || now == null || before.Length != now.Length) return -1;
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] < before[i]) return i;
            }
            return -1;
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

            DisarmGuard();
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
            DisarmGuard();
            acquisition.DataReady -= OnData;
            acquisition.ConnectionLost -= OnLost;
            acquisition.NoDataLimitMs = 0;

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
