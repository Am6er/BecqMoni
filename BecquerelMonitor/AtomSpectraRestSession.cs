using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace BecquerelMonitor
{
    public enum AtomSpectraRestFailure
    {
        None,
        Unreachable,
        StopRefused,
        ResetRefused,
        StartRefused,
        ObserveOnly,
        HeadlessBehind,
        DocumentNotEmpty,   // "Start over" is refused in a document that holds data
        HeadlessAsk,        // a stopped board with data and no window to ask in: refused, no -sta
        ObserveBehind       // observe only, and the board holds less than the document: refused at Start, nothing sent
    }

    public sealed class AtomSpectraRestStartQuestion
    {
        public bool Counting;
        public AtomSpectraRestBoardSample Board;
        public long DocumentSeconds;
        public long DocumentCounts;
        public bool ContinueAllowed;
        public bool ReplaceAllowed;   // "Replace document spectrum" is offered (AtomSpectraRestPolicy.ReplaceAllowed)
        public int ChannelBelow = -1;   // the first channel in which the board holds fewer counts than the document (-1: none)
        public string Note;   // an extra line under the question (TCP: what each button sends)
        public bool ObserveOnly;   // observe only: Replace sends nothing at all, not even -sta to a stopped board
    }

    // The AtomSpectra type over the board's REST API; the board counts by itself, BecqMoni polls GET /api/spectrum.json and mirrors it.
    // Rules: a Start on a board with data asks every time (Continue / Start over / Cancel);
    // "observe only" never sends a POST; there is NO deferred reset (a Clear resets now or never);
    // Stop sends -sto only for an acquisition this session started;
    // a board whose counters went down is never written over the document.
    public sealed partial class AtomSpectraRestSession : IAtomSpectraSession
    {
        public static int ProbeGapMs = 3200;
        public static int RetryStopEveryMs = 10000;
        public static long StallOverrideMs = 0;
        public const int RetryStopForMinutes = 30;
        public const int ProbeTimeoutMs = 2000;

        private static readonly object retryLock = new object();
        private static readonly Dictionary<string, Timer> stopRetries = new Dictionary<string, Timer>(StringComparer.OrdinalIgnoreCase);

        private readonly AtomSpectraRestClient client;
        private readonly int periodSeconds;
        private readonly bool observeOnly;
        private readonly int limitSeconds;
        private readonly AtomSpectraRestGuard guard = new AtomSpectraRestGuard();

        private AtomSpectraRestAcquisition acquisition;
        private bool joined;
        private bool replaced;   // Start took the board's spectrum in place of the document's: the guard starts from the snapshot the question showed
        private bool startedByUs;
        private bool counting;
        private double elapsedAtBegin;
        private string lastError;       // for the log (English)
        private string lastUserError;   // the same failure for the user (resources); null when there is none to show
        private bool disposed;
        private bool resetReported;
        private bool stallReported;
        private bool limitReported;
        private long noDataSince = -1;        // AtMs of the first 404 of the series in progress
        private bool noDataReported;
        private AtomSpectraRestBoardSample lastProbe;
        private readonly object acqLock = new object();   // Detach runs on the UI thread (Stop) and on the poll thread (reset/stall)

        public Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> Ask;
        public long DocumentSeconds;
        public long DocumentCounts;
        public int[] DocumentBins;    // the document's channels at Start (null: none); "Continue" needs every one of them on the board
        public int RequestChannels;   // channels of the profile at Start; ?channels=N only for 8192/4096/2048/1024
        private int epochChangesSeen;

        // The text of a Start refused without a window: a stopped board with data, nobody to ask
        public static string HeadlessAskText { get { return BecquerelMonitor.Properties.Resources.ERRAtomSpectraRestHeadlessAsk; } }

        // The message of a lost link for the user: REST keeps polling every period, not the TCP bridge's 60 s reconnect.
        public string LinkLostText()
        {
            AtomSpectraRestAcquisition a = acquisition;
            if (a != null && a.AccessDenied)   // 401/403 is a refusal of access, not a board that does not answer
                return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.MSGAtomSpectraRestAccessDenied,
                    AtomSpectraTcpLog.Mask(client.BaseUrl), a.LastAnsweredStatus, periodSeconds);
            return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.MSGAtomSpectraRestLinkLost,
                AtomSpectraTcpLog.Mask(client.BaseUrl), periodSeconds);
        }

        // The message of a Start that could not reach the board.
        public string UnreachableText()
        {
            return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.ERRAtomSpectraRestUnreachable,
                AtomSpectraTcpLog.Mask(client.BaseUrl), periodSeconds, LastErrorText ?? "");   // not the bridge text (one client, busy slot)
        }

        public AtomSpectraRestSession(AtomSpectraRestClient client, int periodSeconds, bool observeOnly, int limitSeconds)
        {
            if (client == null) throw new ArgumentNullException("client");
            this.client = client;
            this.periodSeconds = periodSeconds;
            this.observeOnly = observeOnly;
            this.limitSeconds = limitSeconds;
            guard.Note = delegate(string text) { AtomSpectraTcpLog.Info("Session", text); };
        }

        public string TransportName { get { return "REST"; } }
        public string Host { get { return client.Host; } }
        public int Port { get { return client.Port; } }
        public string Endpoint { get { return client.Host + ":" + client.Port.ToString(CultureInfo.InvariantCulture); } }
        public bool IsConnected { get { return acquisition != null && !acquisition.IsLinkLost; } }
        public bool IsLinkLost { get { return acquisition != null && acquisition.IsLinkLost; } }
        public bool IsOpen { get { return acquisition != null; } }
        public string LastError { get { return lastError; } }              // the log text
        public string LastErrorText { get { return lastUserError; } }      // the user's text of the same failure (null: none)
        public bool JoinedRunning { get { return joined; } }
        public bool ReplacedDocument { get { return replaced; } }
        // Replace: the board's spectrum the Start question showed (the second look) as a snapshot for the document; its live time and calibration come with the first poll
        public AtomSpectraTcpDataReadyArgs ReplacedSnapshot { get { AtomSpectraRestBoardSample p = lastProbe; return replaced && p != null && p.HasData && p.Bins != null ? new AtomSpectraRestDataReadyArgs((int[])p.Bins.Clone(), 0, (int)Math.Min(int.MaxValue, p.Time), 0, 0, double.NaN, p.Total, false, p.HistOk, null) : null; } }
        public double ElapsedAtBeginSeconds { get { return elapsedAtBegin; } }
        public bool StartedByUs { get { return startedByUs; } }
        public bool ObserveOnly { get { return observeOnly; } }
        public int LimitSeconds { get { return limitSeconds; } }
        public bool Counting { get { return counting; } }
        public bool RestartedBoard { get; private set; }
        public AtomSpectraRestFailure Failure { get; private set; }
        public AtomSpectraRestStartPlan LastPlan { get; private set; }
        public AtomSpectraRestBoardSample LastProbe { get { return lastProbe; } }
        public AtomSpectraRestBoardSample TripSample { get; private set; }   // the snapshot that showed the reset from outside (the board's new acquisition)
        public AtomSpectraRestAcquisition Acquisition { get { return acquisition; } }
        public AtomSpectraRestGuard Guard { get { return guard; } }
        public long StallMs { get { return StallOverrideMs > 0 ? StallOverrideMs : Math.Max(3L * Math.Max(1, periodSeconds) * 1000L, 15000L); } }

        public event EventHandler<AtomSpectraTcpDataReadyArgs> DataReady;
        public event EventHandler LinkLost;
        public event EventHandler<string> BoardReset;
        public event EventHandler BoardStalled;
        public event EventHandler BoardKeepsCounting;   // Stop of a joined acquisition: polling ended, nothing sent, the board counts on

        // Preset of the document after a successful Begin; the controller applies it (MeasurementController.cs:749 compares it).
        public int PresetAfterStart(int documentPreset)
        {
            long board = (long)elapsedAtBegin;
            // our run with a configured limit: the board stops itself at board + limit, and the document reaches the same preset
            // there (its time is the board's), so the run ends as a normal preset end (save, sound), not as a stall
            if (startedByUs && limitSeconds > 0)
                return (int)Math.Min(int.MaxValue / 2, board + limitSeconds);
            return board > 0 ? AtomSpectraRestPolicy.PresetForContinue(documentPreset, board, limitSeconds) : documentPreset;
        }

        // the board has counted the whole limit of the run we started (-sta N): its stop is the end of the run, not a stall
        private bool AtBoardLimit(AtomSpectraRestBoardSample s)
        {
            return startedByUs && limitSeconds > 0 && s != null && s.HasData && s.Time >= (long)elapsedAtBegin + limitSeconds;
        }

        private static string RetryKey(string host, int port)
        {
            return host + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        public static bool StopRetryPending(string host, int port)
        {
            lock (retryLock)
            {
                return stopRetries.ContainsKey(RetryKey(host, port));
            }
        }

        public AtomSpectraRestBoardSample ReadBoard(int timeoutMs, out string error)
        {
            string user;
            return ReadBoard(timeoutMs, out error, out user);
        }

        // error: the log text; userError: the same for the user
        public AtomSpectraRestBoardSample ReadBoard(int timeoutMs, out string error, out string userError)
        {
            error = null;
            userError = null;
            try
            {
                AtomSpectraRestResponse r = client.Get("/api/spectrum.json", timeoutMs);
                if (r.Status == 404)
                {
                    return AtomSpectraRestBoardSample.NoData((long)Environment.TickCount);
                }
                if (!r.Ok)
                {
                    error = r.Error ?? ("HTTP " + r.Status.ToString(CultureInfo.InvariantCulture));
                    userError = r.UserText;
                    return null;
                }
                AtomSpectraRestDataReadyArgs data = AtomSpectraRestSpectrum.Parse(r.Body, 0, out error, out userError);
                if (data == null) return null;
                return AtomSpectraRestSpectrum.ToSample(data, (long)Environment.TickCount);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                userError = string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.AtomSpectraRestErrNetwork, ex.Message);
                return null;
            }
        }

        public AtomSpectraTcpBeginOutcome Begin(bool newDocument, int commandTimeoutMs)
        {
            if (string.IsNullOrEmpty(client.Host)) return AtomSpectraTcpBeginOutcome.NoHost;

            CancelStopRetry();
            Detach();
            Failure = AtomSpectraRestFailure.None;
            lastError = null;
            lastUserError = null;
            RestartedBoard = false;
            joined = false;
            replaced = false;
            startedByUs = false;
            elapsedAtBegin = 0;
            resetReported = false;
            TripSample = null;
            stallReported = false;
            limitReported = false;
            noDataSince = -1;
            noDataReported = false;
            // [deferred reset: none] - a failed Clear leaves nothing behind for this Start

            string err, userErr;
            AtomSpectraRestBoardSample first = ReadBoard(ProbeTimeoutMs, out err, out userErr);
            if (first == null)
            {
                lastError = err;
                lastUserError = userErr;
                Failure = AtomSpectraRestFailure.Unreachable;
                AtomSpectraTcpLog.Warn("Session", "Start: board not reachable: " + err);
                return AtomSpectraTcpBeginOutcome.ConnectFailed;
            }

            Thread.Sleep(ProbeGapMs);
            AtomSpectraRestBoardSample second = ReadBoard(ProbeTimeoutMs, out err, out userErr);
            if (second == null)
            {
                lastError = err;
                lastUserError = userErr;
                Failure = AtomSpectraRestFailure.Unreachable;
                AtomSpectraTcpLog.Warn("Session", "Start: board not reachable (2nd look): " + err);
                return AtomSpectraTcpBeginOutcome.ConnectFailed;
            }

            lastProbe = second;
            counting = AtomSpectraRestPolicy.IsCounting(first, second);
            // a board spectrum below the document's in any channel is another acquisition, even one that overtook the time and the counts
            int below = second.HasData ? AtomSpectraRestPolicy.ChannelBelow(DocumentBins, second.Bins) : -1;

            AtomSpectraRestStartPlan plan = AtomSpectraRestPolicy.PlanStart(observeOnly, Ask != null, counting, second, DocumentSeconds, DocumentCounts, below >= 0);

            if (plan == AtomSpectraRestStartPlan.RefuseObserve) plan = ObservePlan(second, below);   // Replace is still possible (nothing sent)
            if (plan == AtomSpectraRestStartPlan.RefuseObserve)
            {
                // observe only never sends a command; joining would end at the first snapshot as "the board was reset"
                LastPlan = plan;
                Failure = AtomSpectraRestFailure.ObserveBehind;
                lastError = below >= 0
                    ? AtomSpectraTcpLog.Format("observe only: channel {0} of the board holds fewer counts than the document", below)
                    : "observe only: the board holds less than the document";
                AtomSpectraTcpLog.Warn("Session", "Start refused: " + lastError + "; nothing sent");
                return AtomSpectraTcpBeginOutcome.Refused;
            }

            if (plan == AtomSpectraRestStartPlan.Ask && Ask == null)
            {
                // nobody to ask (headless) and the board is stopped with data: no -sta, the Start is refused
                LastPlan = plan;
                Failure = AtomSpectraRestFailure.HeadlessAsk;
                lastError = "the board is stopped and holds a measurement; there is no window to ask what to do with it";
                return AtomSpectraTcpBeginOutcome.Refused;
            }

            if (plan == AtomSpectraRestStartPlan.Ask)
            {
                bool continueAllowed = !AtomSpectraRestPolicy.BoardBehindDocument(second, DocumentSeconds, DocumentCounts) && below < 0;
                AtomSpectraRestStartQuestion q = new AtomSpectraRestStartQuestion
                {
                    Counting = counting,
                    Board = second,
                    DocumentSeconds = DocumentSeconds,
                    DocumentCounts = DocumentCounts,
                    ContinueAllowed = continueAllowed,
                    ReplaceAllowed = AtomSpectraRestPolicy.ReplaceAllowed(second, DocumentSeconds, DocumentCounts, continueAllowed),
                    ChannelBelow = below
                };
                AtomSpectraRestStartChoice choice = Ask(q);
                AtomSpectraTcpLog.Info("Session", "Start dialog: " + choice);
                plan = AtomSpectraRestPolicy.PlanChoice(choice, counting, continueAllowed, AtomSpectraRestPolicy.BoardHasData(second));
                replaced = choice == AtomSpectraRestStartChoice.Replace && plan != AtomSpectraRestStartPlan.Cancel;
                if (replaced) AtomSpectraTcpLog.Info("Session", "Start: the document's spectrum is replaced by the board's; the board is not reset");
            }

            LastPlan = plan;
            AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("Start: board time {0} -> {1} s, hist_ok {2} -> {3}, data {4}, counting {5}, observe {6}, plan {7}, channel below the document {8}",
                first.Time, second.Time, first.HistOk, second.HistOk, second.HasData, counting, observeOnly, plan, below));

            long boardTime = second.HasData ? second.Time : 0;

            switch (plan)
            {
                case AtomSpectraRestStartPlan.Cancel:
                    return AtomSpectraTcpBeginOutcome.Cancelled;

                case AtomSpectraRestStartPlan.RefuseHeadless:
                    Failure = AtomSpectraRestFailure.HeadlessBehind;
                    lastError = "the board holds less than the document";
                    return AtomSpectraTcpBeginOutcome.Refused;

                case AtomSpectraRestStartPlan.Join:
                    joined = true;
                    elapsedAtBegin = boardTime;
                    // Replace: the board's smaller spectrum is what the document takes, not a reset from outside; the guard starts
                    // from the snapshot the question showed (the question may stay open for minutes, the board may change meanwhile)
                    if (replaced) guard.BeginFrom(second, SeenBins(second), counting);
                    else guard.Begin(DocumentSeconds, DocumentCounts, counting, DocumentBins);
                    break;

                case AtomSpectraRestStartPlan.Resume:
                case AtomSpectraRestStartPlan.StartNew:
                    if (!Send(AtomSpectraRestPolicy.StartCommand(limitSeconds), commandTimeoutMs))
                    {
                        Failure = AtomSpectraRestFailure.StartRefused;
                        return AtomSpectraTcpBeginOutcome.NoAnswer;
                    }
                    startedByUs = true;
                    elapsedAtBegin = boardTime;
                    if (replaced) guard.BeginFrom(second, SeenBins(second), true);   // -sta resumes the board's acquisition, no reset
                    else guard.Begin(DocumentSeconds, DocumentCounts, true, DocumentBins);
                    break;

                case AtomSpectraRestStartPlan.Restart:
                    // [no deferred reset]
                    // the reset would make the first new snapshot overwrite this document's own data
                    if (DocumentSeconds > 0 || DocumentCounts > 0)
                    {
                        Failure = AtomSpectraRestFailure.DocumentNotEmpty;
                        lastError = "Start over needs a new document: this one holds data";
                        return AtomSpectraTcpBeginOutcome.Refused;
                    }
                    if (counting && !Send("-sto", commandTimeoutMs))
                    {
                        Failure = AtomSpectraRestFailure.StopRefused;
                        return AtomSpectraTcpBeginOutcome.NoAnswer;
                    }
                    guard.Begin(0, 0, true);
                    guard.ExpectOwnReset();   // the window of OUR reset opens before the request goes (no acquisition yet: t0 = the first snapshot)
                    AtomSpectraRestResponse rr = client.Reset(commandTimeoutMs);
                    if (!rr.Accepted || rr.SentFalse)
                    {
                        if (rr.NotSent || rr.SentFalse) guard.CancelOwnReset();
                        lastError = rr.SentFalse ? "reset answered sent=false: the analyzer did not get -rst" : rr.Error ?? AtomSpectraTcpLog.Format("reset answered {0} {1}", rr.Status, rr.Body);
                        lastUserError = rr.SentFalse ? BecquerelMonitor.Properties.Resources.AtomSpectraRestErrResetUnsent : RefusalText(rr);
                        Failure = AtomSpectraRestFailure.ResetRefused;
                        AtomSpectraTcpLog.Error("Session", "Start over: reset refused (" + lastError + ")" + (counting ? "; the board was already stopped by -sto" : ""));
                        return AtomSpectraTcpBeginOutcome.NoAnswer;
                    }
                    AtomSpectraTcpLog.Info("Session", "Start over: board reset");
                    RestartedBoard = true;
                    guard.ConfirmOwnReset(boardTime, second.HasData ? second.Total : 0);
                    if (!Send(AtomSpectraRestPolicy.StartCommand(limitSeconds), commandTimeoutMs))
                    {
                        Failure = AtomSpectraRestFailure.StartRefused;
                        return AtomSpectraTcpBeginOutcome.NoAnswer;
                    }
                    startedByUs = true;
                    elapsedAtBegin = 0;
                    break;

                default:
                    return AtomSpectraTcpBeginOutcome.Cancelled;
            }

            epochChangesSeen = guard.EpochChanges;
            acquisition = new AtomSpectraRestAcquisition(client, periodSeconds, RequestChannels);
            acquisition.Sampled += OnSampled;
            acquisition.LinkLost += OnLinkLost;
            acquisition.LinkRestored += OnLinkRestored;
            acquisition.Start();
            AtomSpectraTcpLog.Info("Session", "REST session started on " + client.BaseUrl + " (" + plan + ")");
            return AtomSpectraTcpBeginOutcome.Started;
        }

        // the channels of a probe (always 8192: a probe asks without ?channels=N) as the polls of this session bring them
        private int[] SeenBins(AtomSpectraRestBoardSample s)
        {
            int n = AtomSpectraRestSpectrum.RequestChannels(RequestChannels);
            int[] bins = s != null ? s.Bins : null;
            return bins != null && n > 0 && bins.Length > n && bins.Length % n == 0 ? AtomSpectraTcpAcquisition.Compress(bins, n) : bins;
        }

        // the user's text of a POST the board did not take: no answer / HTTP status / 200 {"ok":false}
        private static string RefusalText(AtomSpectraRestResponse r)
        {
            if (r.UserError != null) return r.UserError;
            return r.Ok ? BecquerelMonitor.Properties.Resources.AtomSpectraRestErrCommandRefused : r.UserText;
        }

        private AtomSpectraRestResponse lastCommand;   // the answer to the last Send (was the command delivered at all?)

        private bool Send(string command, int timeoutMs)
        {
            AtomSpectraRestResponse r = client.Command(command, timeoutMs);
            lastCommand = r;
            if (r.Accepted)
            {
                AtomSpectraTcpLog.Info("Session", command + " accepted");
                return true;
            }
            lastError = r.Error ?? AtomSpectraTcpLog.Format("answered {0} {1}", r.Status, r.Body);
            lastUserError = RefusalText(r);
            AtomSpectraTcpLog.Warn("Session", command + " refused: " + lastError);
            return false;
        }

        public AtomSpectraTcpStopOutcome Stop(int commandTimeoutMs)
        {
            if (acquisition == null && !startedByUs && !joined) return AtomSpectraTcpStopOutcome.NotRunning;

            AtomSpectraRestBoardSample last = acquisition != null ? acquisition.LastSample : null;
            if (last == null) last = lastProbe;

            DetachNoWait();   // the caller (UI thread) does not wait for a poll hanging on the board

            if (observeOnly)
            {
                AtomSpectraTcpLog.Info("Session", "Stop: observe only, nothing sent to the board");
                joined = false;
                return AtomSpectraTcpStopOutcome.Detached;
            }

            if (!startedByUs && !StopJoinedOnBoard())
            {
                AtomSpectraTcpLog.Info("Session", "Stop: the acquisition was joined, not started here - -sto is not sent, polling stopped");
                joined = false;
                EventHandler kept = BoardKeepsCounting;   // the user is told that the board counts on (unless the Stop question told it already)
                if (kept != null && AskStopJoined == null) kept(this, EventArgs.Empty);
                return AtomSpectraTcpStopOutcome.Detached;
            }

            startedByUs = false;

            if (Send("-sto", commandTimeoutMs)) return AtomSpectraTcpStopOutcome.Stopped;

            // the board cache (2 s) hides a short stop, so a repeat is safe only for a -sto that certainly
            // never reached the board; one that got through (503, timeout after connect) is left to the user
            AtomSpectraRestResponse sr = lastCommand;
            if (sr != null && !sr.NotSent && !(sr.Ok && !sr.Accepted))
            {
                AtomSpectraTcpLog.Warn("Session", "Stop: -sto reached the board but its result is unknown (" + lastError + "); it is not repeated - check the board");
                return AtomSpectraTcpStopOutcome.Unconfirmed;
            }

            StartStopRetry(last);
            AtomSpectraTcpLog.Warn("Session", AtomSpectraTcpLog.Format("Stop: -sto not delivered, retrying every {0} s for {1} min while the board keeps the same acquisition", RetryStopEveryMs / 1000, RetryStopForMinutes));
            return AtomSpectraTcpStopOutcome.Deferred;
        }

        private void StartStopRetry(AtomSpectraRestBoardSample atStop)
        {
            string key = RetryKey(client.Host, client.Port);
            long stopTime = atStop != null && atStop.HasData ? atStop.Time : 0;
            long stopHist = atStop != null && atStop.HasData ? atStop.HistOk : 0;

            lock (retryLock)
            {
                if (stopRetries.ContainsKey(key)) return;

                int[] state = new int[2];
                long[] first = { -1, 0 };   // board time and moment (ms) of the first answer after Stop
                Stopwatch watch = Stopwatch.StartNew();

                Timer timer = new Timer(delegate(object stateObj)
                {
                    int[] s = (int[])stateObj;
                    if (Interlocked.CompareExchange(ref s[1], 1, 0) != 0) return;
                    try
                    {
                        if (watch.ElapsedMilliseconds >= RetryStopForMinutes * 60000L)
                        {
                            AtomSpectraTcpLog.Error("Session", AtomSpectraTcpLog.Format("deferred -sto given up after {0} min", RetryStopForMinutes));
                            CancelStopRetryKey(key);
                            return;
                        }

                        string e;
                        AtomSpectraRestBoardSample now = ReadBoard(ProbeTimeoutMs, out e);
                        if (now == null) return;

                        // someone else restarted or reset the board since our Stop -> the deferred -sto is not ours any more
                        if (!now.HasData || now.Time < stopTime || now.HistOk < stopHist)
                        {
                            AtomSpectraTcpLog.Warn("Session", AtomSpectraTcpLog.Format("deferred -sto cancelled: the board was reset or restarted since our Stop (time {0} -> {1} s)", stopTime, now.HasData ? now.Time : 0));
                            CancelStopRetryKey(key);
                            return;
                        }

                        // -sto only to a board that has been counting all along since our Stop; a board that
                        // stopped (our lost -sto did work) and maybe was resumed by someone else is left alone
                        long wall = watch.ElapsedMilliseconds;
                        if (first[0] < 0) { first[0] = now.Time; first[1] = wall; return; }
                        if (wall - first[1] < AtomSpectraRestPolicy.MinProbeGapMs) return;
                        // tolerance 3 s = board cache 2 s + rounding 1 s; a pause of the board longer than that (stopped and resumed
                        // by someone else) cancels the repeat whenever the first look comes
                        bool counted = now.Time - first[0] + 2 >= (wall - first[1]) / 1000 && now.Time - stopTime + 3 >= wall / 1000;
                        if (!counted)
                        {
                            AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("deferred -sto cancelled: the board stopped since our Stop (time {0} -> {1} s)", first[0], now.Time));
                            CancelStopRetryKey(key);
                            return;
                        }

                        AtomSpectraRestResponse r = client.Command("-sto", ProbeTimeoutMs * 2);
                        if (r.Accepted)
                        {
                            AtomSpectraTcpLog.Info("Session", "deferred -sto delivered");
                            CancelStopRetryKey(key);
                        }
                        else if (!r.NotSent && !r.Ok)
                        {
                            // this one got through without a clear answer - never send a second one
                            AtomSpectraTcpLog.Warn("Session", "deferred -sto reached the board, result unknown (" + (r.Error ?? r.Status.ToString(CultureInfo.InvariantCulture)) + "); not repeated");
                            CancelStopRetryKey(key);
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref s[1], 0);
                    }
                }, state, RetryStopEveryMs, RetryStopEveryMs);

                stopRetries[key] = timer;
            }
        }

        private void CancelStopRetry()
        {
            CancelStopRetryKey(RetryKey(client.Host, client.Port));
        }

        private static void CancelStopRetryKey(string key)
        {
            lock (retryLock)
            {
                Timer t;
                if (stopRetries.TryGetValue(key, out t))
                {
                    t.Dispose();
                    stopRetries.Remove(key);
                }
            }
        }

        public bool ClearNow(int commandTimeoutMs)
        {
            Failure = AtomSpectraRestFailure.None;
            if (observeOnly)
            {
                Failure = AtomSpectraRestFailure.ObserveOnly;
                AtomSpectraTcpLog.Info("Session", "Clear: observe only, the board is not reset");
                return false;
            }

            // Polling goes on while the request is in flight; a new epoch in that window is OUR reset's answer.
            // An unknown outcome (exception, lost answer) keeps the window to its term; only a certain "not done" withdraws it.
            AtomSpectraRestAcquisition running = acquisition;
            guard.ExpectOwnReset(running != null ? running.NowMs : -1);
            AtomSpectraRestResponse r = client.Reset(commandTimeoutMs);
            if (r.Accepted && r.SentFalse)
            {
                guard.CancelOwnReset();
                // the board answered but the analyzer did not get -rst - the spectrum is not cleared
                lastError = "the board answered sent=false: the analyzer did not get the reset";
                lastUserError = BecquerelMonitor.Properties.Resources.AtomSpectraRestErrResetUnsent;
                Failure = AtomSpectraRestFailure.ResetRefused;
                AtomSpectraTcpLog.Warn("Session", "Clear: reset not done, sent=false (the analyzer did not get -rst)");
                return false;
            }
            if (r.Accepted)
            {
                guard.ConfirmOwnReset();
                resetReported = false;
                stallReported = false;
                AtomSpectraTcpLog.Info("Session", "Clear: board reset");
                return true;
            }

            if (r.NotSent) guard.CancelOwnReset();   // the request certainly never reached the board
            lastError = r.Error ?? AtomSpectraTcpLog.Format("answered {0} {1}", r.Status, r.Body);
            lastUserError = RefusalText(r);
            Failure = AtomSpectraRestFailure.ResetRefused;
            // [clear failed]
            AtomSpectraTcpLog.Warn("Session", "Clear: reset not delivered (" + lastError + "); nothing is queued, the board keeps its spectrum");
            return false;
        }

        public void Clear(int commandTimeoutMs)
        {
            ClearNow(commandTimeoutMs);
        }

        // Stops the polling without waiting for a request in flight: "Disconnect" calls it on the UI thread, and a board that went
        // silent would hold it for the poll's deadline. Nothing the old poll still brings reaches the document (OnSampled checks it).
        public void Detach()
        {
            DetachNoWait();   // never waits: Disconnect runs on the UI thread
        }

        // Stop polling without waiting for a request in flight: the poll thread ends by itself after it.
        private void DetachNoWait()
        {
            AtomSpectraRestAcquisition a;
            lock (acqLock)
            {
                a = acquisition;
                acquisition = null;
            }
            if (a != null)
            {
                a.Sampled -= OnSampled;
                a.LinkLost -= OnLinkLost;
                a.LinkRestored -= OnLinkRestored;
                a.Signal();
            }
        }

        public string Query(string command, int timeoutMs)
        {
            if (command != "-inf") return null;
            try
            {
                AtomSpectraRestResponse r = client.Get("/api/device", timeoutMs);
                if (!r.Ok) return null;
                object parsed = AtomSpectraJson.Parse(r.Body);
                if (parsed == null) return null;
                Dictionary<string, object> o = parsed as Dictionary<string, object>;
                if (o == null) return null;
                double rise = AtomSpectraJson.GetNumber(o, "rise", double.NaN);
                double fall = AtomSpectraJson.GetNumber(o, "fall", double.NaN);
                double freq = AtomSpectraJson.GetNumber(o, "freq", double.NaN);
                if (double.IsNaN(rise) || double.IsNaN(fall) || double.IsNaN(freq) || freq <= 0) return null;
                return "RISE " + rise.ToString(CultureInfo.InvariantCulture) + " FALL " + fall.ToString(CultureInfo.InvariantCulture) + " F " + freq.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                Detach();
            }
        }

        // A series of 404 longer than 3 periods in force: ONE log line and the status "board has no data yet" (no error, no Trip, no stop);
        // the next snapshot with data ends the series (the controller's status text takes over at once).
        private void NoDataSignal(AtomSpectraRestUpdate u, AtomSpectraRestBoardSample sample, AtomSpectraRestAcquisition running)
        {
            if (sample == null) return;
            if (sample.HasData)
            {
                if (u == AtomSpectraRestUpdate.Apply) { noDataSince = -1; noDataReported = false; }   // a snapshot that is not taken (Stale) is not data
                return;
            }
            if (u != AtomSpectraRestUpdate.NoData) return;
            if (noDataSince < 0) noDataSince = sample.AtMs;
            long period = running != null ? running.EffectivePeriodMs : 1000;
            if (noDataReported || sample.AtMs - noDataSince <= 3 * period) return;
            noDataReported = true;
            AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("board has no data yet: HTTP 404 for {0} s (a reset board answers 404 until its first sweep); polling goes on", (sample.AtMs - noDataSince) / 1000));
            AtomSpectraTcpLog.SetStatus(TransportName, Endpoint, BecquerelMonitor.Properties.Resources.AtomSpectraRestStatusNoData);
        }

        private void OnSampled(object sender, AtomSpectraRestSampleArgs e)
        {
            AtomSpectraRestAcquisition running = acquisition;
            if (sender != running) return;   // a poll that ended after Detach (or of an older Start): nothing of it reaches the document
            guard.PeriodMs = running.EffectivePeriodMs;
            AtomSpectraRestUpdate u = guard.Check(e.Sample);
            NoDataSignal(u, e.Sample, running);
            if (guard.EpochChanges != epochChangesSeen)
            {
                epochChangesSeen = guard.EpochChanges;   // logged once per change
                AtomSpectraTcpLog.Info("Session", "board epoch changed (" + guard.LastEpochChange + "): the board was reset from outside");
            }
            if (u == AtomSpectraRestUpdate.Apply)
            {
                if (e.Data != null)
                {
                    EventHandler<AtomSpectraTcpDataReadyArgs> h = DataReady;
                    if (h != null) h(this, e.Data);
                }
            }
            else if (u == AtomSpectraRestUpdate.Decreased)
            {
                if (!resetReported)
                {
                    resetReported = true;
                    TripSample = e.Sample;
                    string why = guard.TripReason;
                    AtomSpectraTcpLog.Error("Session", "board reset or restarted from outside: " + why + "; the document is not overwritten, polling stops, nothing is sent");
                    EventHandler<string> h = BoardReset;
                    if (h != null) h(this, why);
                }
                Detach();
                return;
            }

            bool atLimit = AtBoardLimit(e.Sample);
            if (atLimit && !limitReported)
            {
                limitReported = true;
                AtomSpectraTcpLog.Info("Session", AtomSpectraTcpLog.Format("the board reached the limit of the run ({0} s from {1} s): its stop ends the run at the document's preset, it is no stall", limitSeconds, (long)elapsedAtBegin));
            }

            AtomSpectraRestAcquisition acq = acquisition;
            // a 404 tells nothing about growth, and after skipped polls (doubled period) the last growth is long ago
            if (u != AtomSpectraRestUpdate.NoData && !stallReported && acq != null && guard.Stalled(acq.NowMs, StallMs) && !atLimit)
            {
                stallReported = true;
                AtomSpectraTcpLog.Warn("Session", AtomSpectraTcpLog.Format("the board stopped counting (no growth for {0} s); polling stops, nothing is sent", StallMs / 1000));
                EventHandler h2 = BoardStalled;
                if (h2 != null) h2(this, EventArgs.Empty);
                Detach();
            }
        }

        private void OnLinkLost(object sender, EventArgs e)
        {
            if (sender != acquisition) return;   // the loss of a poll that was detached already
            EventHandler h = LinkLost;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void OnLinkRestored(object sender, EventArgs e)
        {
            AtomSpectraTcpLog.Info("Session", "link restored");
        }
    }
}
