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
        HeadlessAsk         // a stopped board with data and no window to ask in: refused, no -sta
    }

    public sealed class AtomSpectraRestStartQuestion
    {
        public bool Counting;
        public AtomSpectraRestBoardSample Board;
        public long DocumentSeconds;
        public long DocumentCounts;
        public bool ContinueAllowed;
        public string Note;   // an extra line under the question (TCP: what each button sends)
    }

    // The AtomSpectra type over the board's REST API; the board counts by itself, BecqMoni polls GET /api/spectrum.json and mirrors it.
    // Rules: a Start on a board with data asks every time (Continue / Start over / Cancel);
    // "observe only" never sends a POST; there is NO deferred reset (a Clear resets now or never);
    // Stop sends -sto only for an acquisition this session started;
    // a board whose counters went down is never written over the document.
    public sealed class AtomSpectraRestSession : IAtomSpectraSession
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
        private bool startedByUs;
        private bool counting;
        private double elapsedAtBegin;
        private string lastError;
        private bool disposed;
        private bool resetReported;
        private bool stallReported;
        private long noDataSince = -1;        // AtMs of the first 404 of the series in progress
        private bool noDataReported;
        private AtomSpectraRestBoardSample lastProbe;
        private readonly object acqLock = new object();   // Detach runs on the UI thread (Stop) and on the poll thread (reset/stall)

        public Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> Ask;
        public long DocumentSeconds;
        public long DocumentCounts;
        public int RequestChannels;   // channels of the profile at Start; ?channels=N only for 8192/4096/2048/1024
        private int epochChangesSeen;

        // The message of a lost link for the user: REST keeps polling every period, not the TCP bridge's 60 s reconnect.
        // English literals (no ru/en resource yet)
        public const string AccessDeniedFormat = "AtomSpectra REST: the board {0} answers, but refuses access (HTTP {1}). Check the authorization token in the device settings. The measurement is not stopped; BecqMoni keeps polling the board every {2} s.";
        public const string HeadlessAskText = "The board is stopped and holds a measurement, and a run without windows cannot ask what to do with it. Nothing was sent to the board.";

        public string LinkLostText()
        {
            AtomSpectraRestAcquisition a = acquisition;
            if (a != null && a.AccessDenied)   // 401/403 is a refusal of access, not a board that does not answer
                return string.Format(CultureInfo.InvariantCulture, AccessDeniedFormat, AtomSpectraTcpLog.Mask(client.BaseUrl), a.LastAnsweredStatus, periodSeconds);
            return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.MSGAtomSpectraRestLinkLost,
                AtomSpectraTcpLog.Mask(client.BaseUrl), periodSeconds);
        }

        // The message of a Start that could not reach the board.
        public string UnreachableText()
        {
            return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.ERRAtomSpectraRestUnreachable,
                AtomSpectraTcpLog.Mask(client.BaseUrl), periodSeconds, LastError ?? "");   // not the bridge text (one client, busy slot)
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
        public bool IsConnected { get { return acquisition != null && !acquisition.IsLinkLost; } }
        public bool IsLinkLost { get { return acquisition != null && acquisition.IsLinkLost; } }
        public bool IsOpen { get { return acquisition != null; } }
        public string LastError { get { return lastError; } }
        public bool JoinedRunning { get { return joined; } }
        public double ElapsedAtBeginSeconds { get { return elapsedAtBegin; } }
        public bool StartedByUs { get { return startedByUs; } }
        public bool ObserveOnly { get { return observeOnly; } }
        public int LimitSeconds { get { return limitSeconds; } }
        public bool Counting { get { return counting; } }
        public bool RestartedBoard { get; private set; }
        public AtomSpectraRestFailure Failure { get; private set; }
        public AtomSpectraRestStartPlan LastPlan { get; private set; }
        public AtomSpectraRestBoardSample LastProbe { get { return lastProbe; } }
        public AtomSpectraRestAcquisition Acquisition { get { return acquisition; } }
        public AtomSpectraRestGuard Guard { get { return guard; } }
        public long StallMs { get { return StallOverrideMs > 0 ? StallOverrideMs : Math.Max(3L * Math.Max(1, periodSeconds) * 1000L, 15000L); } }

        public event EventHandler<AtomSpectraTcpDataReadyArgs> DataReady;
        public event EventHandler LinkLost;
        public event EventHandler<string> BoardReset;
        public event EventHandler BoardStalled;

        // Preset of the document after a successful Begin; the controller applies it (MeasurementController.cs:749 compares it).
        public int PresetAfterStart(int documentPreset)
        {
            long board = (long)elapsedAtBegin;
            // our run with a configured limit is stopped by the board (-sta N), not by the document preset
            if (startedByUs && limitSeconds > 0)
                return (int)Math.Min(int.MaxValue / 2, board + limitSeconds + 2L * Math.Max(1, periodSeconds) + 5);
            return board > 0 ? AtomSpectraRestPolicy.PresetForContinue(documentPreset, board, limitSeconds) : documentPreset;
        }

        public static bool StopRetryPending(string host, int port)
        {
            lock (retryLock)
            {
                return stopRetries.ContainsKey(host + ":" + port);
            }
        }

        public AtomSpectraRestBoardSample ReadBoard(int timeoutMs, out string error)
        {
            error = null;
            try
            {
                AtomSpectraRestResponse r = client.Get("/api/spectrum.json", timeoutMs);
                if (r.Status == 404)
                {
                    return AtomSpectraRestBoardSample.NoData((long)Environment.TickCount);
                }
                if (!r.Ok)
                {
                    error = r.Error ?? ("HTTP " + r.Status);
                    return null;
                }
                AtomSpectraRestDataReadyArgs data = AtomSpectraRestSpectrum.Parse(r.Body, out error);
                if (data == null) return null;
                return AtomSpectraRestSpectrum.ToSample(data, (long)Environment.TickCount);
            }
            catch (Exception ex)
            {
                error = ex.Message;
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
            RestartedBoard = false;
            joined = false;
            startedByUs = false;
            elapsedAtBegin = 0;
            resetReported = false;
            stallReported = false;
            noDataSince = -1;
            noDataReported = false;
            // [deferred reset: none] - a failed Clear leaves nothing behind for this Start

            string err;
            AtomSpectraRestBoardSample first = ReadBoard(ProbeTimeoutMs, out err);
            if (first == null)
            {
                lastError = err;
                Failure = AtomSpectraRestFailure.Unreachable;
                AtomSpectraTcpLog.Warn("Session", "Start: board not reachable: " + err);
                return AtomSpectraTcpBeginOutcome.ConnectFailed;
            }

            Thread.Sleep(ProbeGapMs);
            AtomSpectraRestBoardSample second = ReadBoard(ProbeTimeoutMs, out err);
            if (second == null)
            {
                lastError = err;
                Failure = AtomSpectraRestFailure.Unreachable;
                AtomSpectraTcpLog.Warn("Session", "Start: board not reachable (2nd look): " + err);
                return AtomSpectraTcpBeginOutcome.ConnectFailed;
            }

            lastProbe = second;
            counting = AtomSpectraRestPolicy.IsCounting(first, second);

            AtomSpectraRestStartPlan plan = AtomSpectraRestPolicy.PlanStart(observeOnly, Ask != null, counting, second, DocumentSeconds, DocumentCounts);

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
                bool continueAllowed = !AtomSpectraRestPolicy.BoardBehindDocument(second, DocumentSeconds, DocumentCounts);
                AtomSpectraRestStartQuestion q = new AtomSpectraRestStartQuestion
                {
                    Counting = counting,
                    Board = second,
                    DocumentSeconds = DocumentSeconds,
                    DocumentCounts = DocumentCounts,
                    ContinueAllowed = continueAllowed
                };
                AtomSpectraRestStartChoice choice = Ask(q);
                AtomSpectraTcpLog.Info("Session", "Start dialog: " + choice);
                plan = AtomSpectraRestPolicy.PlanChoice(choice, counting, continueAllowed);
            }

            LastPlan = plan;
            AtomSpectraTcpLog.Info("Session", "Start: board time " + first.Time + " -> " + second.Time + " s, hist_ok " + first.HistOk + " -> " + second.HistOk + ", data " + second.HasData + ", counting " + counting + ", observe " + observeOnly + ", plan " + plan);

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
                    guard.Begin(DocumentSeconds, DocumentCounts, counting);
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
                    guard.Begin(DocumentSeconds, DocumentCounts, true);
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
                        lastError = rr.SentFalse ? "reset answered sent=false: the analyzer did not get -rst" : rr.Error ?? ("reset answered " + rr.Status + " " + rr.Body);
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
            lastError = r.Error ?? ("answered " + r.Status + " " + r.Body);
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

            if (!startedByUs)
            {
                AtomSpectraTcpLog.Info("Session", "Stop: the acquisition was joined, not started here - -sto is not sent, polling stopped");
                joined = false;
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
            AtomSpectraTcpLog.Warn("Session", "Stop: -sto not delivered, retrying every " + (RetryStopEveryMs / 1000) + " s for " + RetryStopForMinutes + " min while the board keeps the same acquisition");
            return AtomSpectraTcpStopOutcome.Deferred;
        }

        private void StartStopRetry(AtomSpectraRestBoardSample atStop)
        {
            string key = client.Host + ":" + client.Port;
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
                            AtomSpectraTcpLog.Error("Session", "deferred -sto given up after " + RetryStopForMinutes + " min");
                            CancelStopRetryKey(key);
                            return;
                        }

                        string e;
                        AtomSpectraRestBoardSample now = ReadBoard(ProbeTimeoutMs, out e);
                        if (now == null) return;

                        // someone else restarted or reset the board since our Stop -> the deferred -sto is not ours any more
                        if (!now.HasData || now.Time < stopTime || now.HistOk < stopHist)
                        {
                            AtomSpectraTcpLog.Warn("Session", "deferred -sto cancelled: the board was reset or restarted since our Stop (time " + stopTime + " -> " + (now.HasData ? now.Time : 0) + " s)");
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
                            AtomSpectraTcpLog.Info("Session", "deferred -sto cancelled: the board stopped since our Stop (time " + first[0] + " -> " + now.Time + " s)");
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
                            AtomSpectraTcpLog.Warn("Session", "deferred -sto reached the board, result unknown (" + (r.Error ?? r.Status.ToString()) + "); not repeated");
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
            CancelStopRetryKey(client.Host + ":" + client.Port);
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
            lastError = r.Error ?? ("answered " + r.Status + " " + r.Body);
            Failure = AtomSpectraRestFailure.ResetRefused;
            // [clear failed]
            AtomSpectraTcpLog.Warn("Session", "Clear: reset not delivered (" + lastError + "); nothing is queued, the board keeps its spectrum");
            return false;
        }

        public void Clear(int commandTimeoutMs)
        {
            ClearNow(commandTimeoutMs);
        }

        public void Detach()
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
                a.Dispose();
            }
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
            AtomSpectraTcpLog.Info("Session", "board has no data yet: HTTP 404 for " + (sample.AtMs - noDataSince) / 1000 + " s (a reset board answers 404 until its first sweep); polling goes on");
            AtomSpectraTcpLog.SetStatus(TransportName, Host + ":" + Port, "board has no data yet");
        }

        private void OnSampled(object sender, AtomSpectraRestSampleArgs e)
        {
            AtomSpectraRestAcquisition running = acquisition;
            if (running != null) guard.PeriodMs = running.EffectivePeriodMs;
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
                    string why = guard.TripReason;
                    AtomSpectraTcpLog.Error("Session", "board reset or restarted from outside: " + why + "; the document is not overwritten, polling stops, nothing is sent");
                    EventHandler<string> h = BoardReset;
                    if (h != null) h(this, why);
                }
                Detach();
                return;
            }

            AtomSpectraRestAcquisition acq = acquisition;
            // a 404 tells nothing about growth, and after skipped polls (doubled period) the last growth is long ago
            if (u != AtomSpectraRestUpdate.NoData && !stallReported && acq != null && guard.Stalled(acq.NowMs, StallMs))
            {
                stallReported = true;
                AtomSpectraTcpLog.Warn("Session", "the board stopped counting (no growth for " + (StallMs / 1000) + " s); polling stops, nothing is sent");
                EventHandler h2 = BoardStalled;
                if (h2 != null) h2(this, EventArgs.Empty);
                Detach();
            }
        }

        private void OnLinkLost(object sender, EventArgs e)
        {
            EventHandler h = LinkLost;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void OnLinkRestored(object sender, EventArgs e)
        {
            AtomSpectraTcpLog.Info("Session", "link restored");
        }
    }
}
