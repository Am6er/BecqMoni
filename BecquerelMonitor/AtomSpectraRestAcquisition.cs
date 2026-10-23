using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace BecquerelMonitor
{
    public class AtomSpectraRestDataReadyArgs : AtomSpectraTcpDataReadyArgs
    {
        public double LiveSeconds { get; private set; }
        public long Total { get; private set; }
        // "dead" of the board is a 0/1 flag "the analyzer is silent" (spectrum_meta_fmt.h:111), NOT a dead time.
        public bool SpectrometerSilent { get; private set; }
        public long HistOk { get; private set; }            // "hist_ok": monotonic until a reboot
        public double[] Calibration { get; private set; }   // "calib" (up to 5 slots) when "calib_set", else null
        // fields of the v2 answer (the request carried ?channels=N)
        public int Channels { get; set; } = -1;              // top-level "channels"; -1: absent (old firmware / v1 answer)
        public int CalibChannels { get; set; } = AtomSpectraRestSpectrum.Channels;   // "calib_channels": the scale of "calib"
        public AtomSpectraRestEpoch Epoch { get; set; }      // boot_id / dev_resets / spec_resets; null: none of them

        public AtomSpectraRestDataReadyArgs(
            int[] histogram,
            int cpuLoad,
            int elapsedTime,
            int invalidPulses,
            int cps,
            double liveSeconds,
            long total,
            bool spectrometerSilent,
            long histOk,
            double[] calibration)
            : base(histogram, cpuLoad, elapsedTime, invalidPulses, cps)
        {
            LiveSeconds = liveSeconds;
            Total = total;
            SpectrometerSilent = spectrometerSilent;
            HistOk = histOk;
            Calibration = calibration;
        }
    }

    public static class AtomSpectraRestSpectrum
    {
        public const int Channels = 8192;   // the board's full scale; the only length of an answer without "channels"

        // the N the board folds to; a profile with another number of channels sends no parameter
        public static readonly int[] RequestableChannels = { 8192, 4096, 2048, 1024 };

        public static int RequestChannels(int profileChannels)
        {
            return Array.IndexOf(RequestableChannels, profileChannels) >= 0 ? profileChannels : 0;
        }

        // A profile the board's 8192 channels fold into by a whole factor; any other N (16384, 6000) cannot be
        // filled from the board: more channels are never sent, and a fold that does not divide would lose the top ones.
        public static bool ChannelsSupported(int profileChannels)
        {
            return profileChannels > 0 && profileChannels <= Channels && Channels % profileChannels == 0;
        }

        // the user's text of a snapshot that does not parse (the field that is wrong)
        public static string AnswerText(string field)
        {
            return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.AtomSpectraRestErrAnswer, field);
        }

        private static string ChannelsText(int got, int wanted)
        {
            return string.Format(CultureInfo.InvariantCulture, BecquerelMonitor.Properties.Resources.AtomSpectraRestErrChannels, got, wanted);
        }

        private static string Inv(long v) { return v.ToString(CultureInfo.InvariantCulture); }

        // double.IsFinite does not exist in .NET Framework 4.8
        public static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        private static long? Whole(Dictionary<string, object> o, string key)
        {
            double v = AtomSpectraJson.GetNumber(o, key, double.NaN);
            return Finite(v) ? (long?)(long)v : null;
        }

        public static AtomSpectraRestBoardSample ToSample(AtomSpectraRestDataReadyArgs d, long atMs)
        {
            return new AtomSpectraRestBoardSample { HasData = true, Time = d.ElapsedTime, Total = d.Total, HistOk = d.HistOk, AtMs = atMs, Bins = d.Histogram, Epoch = d.Epoch };
        }

        // /api/spectrum.json is the primary source: one snapshot carries counts, time, live time and rejected pulses together;
        // the binary /api/spectrum (32 KB) has no time, so the pair "spectrum - time" could not be built from it alone.
        public static AtomSpectraRestDataReadyArgs Parse(string json, out string error)
        {
            return Parse(json, 0, out error);
        }

        // "channels", when present, must equal the bins; then the length must be the N asked for (8192 for a
        // request without the parameter). Without "channels" only the old 8192 answer is taken (the client folds it).
        public static string LengthError(int bins, int declared, int requested)
        {
            string user;
            return LengthError(bins, declared, requested, out user);
        }

        // the log text (null: the length is right) and the same for the user
        public static string LengthError(int bins, int declared, int requested, out string userError)
        {
            userError = null;
            if (declared < 0)
            {
                if (bins == Channels) return null;
                userError = ChannelsText(bins, Channels);
                return "bins count " + Inv(bins) + " without \"channels\" (only " + Inv(Channels) + ")";
            }
            if (declared != bins)
            {
                userError = AnswerText("channels");
                return "\"channels\" " + Inv(declared) + " != bins count " + Inv(bins);
            }
            int wanted = requested > 0 ? requested : Channels;
            if (bins == wanted) return null;
            userError = ChannelsText(bins, wanted);
            return "\"channels\" " + Inv(bins) + " != requested " + Inv(wanted);
        }

        // requested: the N of ?channels=N (0: the request had no parameter)
        public static AtomSpectraRestDataReadyArgs Parse(string json, int requested, out string error)
        {
            string user;
            return Parse(json, requested, out error, out user);
        }

        // a whole number of a field that must be one: NaN, inf or out of the int range is a broken snapshot ((int)NaN is int.MinValue)
        private static bool WholeField(Dictionary<string, object> o, string key, double fallback, out int value)
        {
            double v = AtomSpectraJson.GetNumber(o, key, fallback);
            value = 0;
            if (!Finite(v) || v < 0 || v > int.MaxValue) return false;
            value = (int)v;
            return true;
        }

        // error: the log text; userError: the same for the user (resources)
        public static AtomSpectraRestDataReadyArgs Parse(string json, int requested, out string error, out string userError)
        {
            error = null;
            userError = null;
            try
            {
                object root = AtomSpectraJson.Parse(json);
                if (!(root is Dictionary<string, object>))
                {
                    error = "not an object";
                    userError = AnswerText("JSON");
                    return null;
                }

                Dictionary<string, object> o = (Dictionary<string, object>)root;

                List<object> bins = AtomSpectraJson.GetArray(o, "bins");
                if (bins == null)
                {
                    error = "no bins";
                    userError = AnswerText("bins");
                    return null;
                }

                int declared = o.ContainsKey("channels") ? (int)AtomSpectraJson.GetNumber(o, "channels", -1) : -1;
                string lengthUser = null;
                string lengthError = LengthError(bins.Count, declared, requested, out lengthUser);
                if (lengthError != null)
                {
                    error = lengthError;
                    userError = lengthUser;
                    return null;
                }

                int n = bins.Count;
                int[] histogram = new int[n];
                long sum = 0;
                for (int i = 0; i < n; i++)
                {
                    object item = bins[i];
                    if (!(item is double))
                    {
                        error = "bin " + Inv(i) + " is not a number";
                        userError = AnswerText("bins");
                        return null;
                    }

                    double val = (double)item;
                    if (!AtomSpectraRestSpectrum.Finite(val) || val < 0 || val > int.MaxValue)
                    {
                        error = "bin " + Inv(i) + " out of range";
                        userError = AnswerText("bins");
                        return null;
                    }

                    int count = (int)val;
                    histogram[i] = count;
                    sum += count;
                }

                if (!o.ContainsKey("time"))
                {
                    error = "missing time";
                    userError = AnswerText("time");
                    return null;
                }

                int elapsed;
                if (!WholeField(o, "time", double.NaN, out elapsed))
                {
                    error = "\"time\" is not a number of seconds";
                    userError = AnswerText("time");
                    return null;
                }

                double cpuVal = AtomSpectraJson.GetNumber(o, "cpu", 0);
                int cpu = Finite(cpuVal) && Math.Abs(cpuVal) <= int.MaxValue ? (int)cpuVal : 0;   // shown only

                double cpsVal = AtomSpectraJson.GetNumber(o, "cps", 0);
                int cps = Finite(cpsVal) && Math.Abs(cpsVal) <= int.MaxValue ? (int)cpsVal : 0;   // shown only

                int invalid;
                if (!WholeField(o, "lost", 0, out invalid))
                {
                    error = "\"lost\" is not a count";
                    userError = AnswerText("lost");
                    return null;
                }

                double liveVal = double.NaN;
                if (o.ContainsKey("live"))
                {
                    double lv = AtomSpectraJson.GetNumber(o, "live", double.NaN);
                    if (AtomSpectraRestSpectrum.Finite(lv))
                    {
                        liveVal = lv;
                    }
                }

                long total = sum;
                if (o.ContainsKey("total"))
                {
                    double tv = AtomSpectraJson.GetNumber(o, "total", sum);
                    if (AtomSpectraRestSpectrum.Finite(tv))
                    {
                        total = (long)tv;
                    }
                }

                bool silent = AtomSpectraJson.GetNumber(o, "dead", 0) != 0;
                double hv = AtomSpectraJson.GetNumber(o, "hist_ok", 0);
                long histOk = AtomSpectraRestSpectrum.Finite(hv) && hv > 0 ? (long)hv : 0;
                double[] calib = null;
                List<object> cal = AtomSpectraJson.GetArray(o, "calib");
                if (cal != null && AtomSpectraJson.GetBool(o, "calib_set", true))
                {
                    calib = new double[Math.Min(5, cal.Count)];
                    for (int i = 0; i < calib.Length; i++) calib[i] = cal[i] is double ? (double)cal[i] : double.NaN;
                }

                AtomSpectraRestEpoch epoch = new AtomSpectraRestEpoch { BootId = Whole(o, "boot_id"), DevResets = Whole(o, "dev_resets"), SpecResets = Whole(o, "spec_resets") };
                long? calibChannels = Whole(o, "calib_channels");

                return new AtomSpectraRestDataReadyArgs(
                    histogram,
                    cpu,
                    elapsed,
                    invalid,
                    cps,
                    liveVal,
                    total,
                    silent,
                    histOk,
                    calib)
                {
                    Channels = declared,
                    CalibChannels = calibChannels.HasValue && calibChannels.Value > 0 && calibChannels.Value <= int.MaxValue ? (int)calibChannels.Value : Channels,
                    Epoch = epoch.Any ? epoch : null
                };
            }
            catch (Exception ex)
            {
                error = "parse exception: " + ex.Message;
                userError = AnswerText("JSON");
                return null;
            }
        }
    }

    public sealed class AtomSpectraRestSampleArgs : EventArgs
    {
        public AtomSpectraRestBoardSample Sample;
        public AtomSpectraRestDataReadyArgs Data;    // null for HTTP 404 "No spectrum data yet"
    }

    public sealed class AtomSpectraRestAcquisition : IDisposable
    {
        // this many polls with a failing ANSWER of the board (503, a body that does not parse, 401) since the last good poll = the link
        // is lost, whatever happened in between (the other way to lose it is silence, see LinkLostFloorMs); the polling goes on either way
        public const int LinkLostAfterFailures = 3;
        public static int LinkLostFloorMs = 15000;    // the least silence that is a lost link (a test lowers it to keep a short outage scenario)
        public const int MinDeadlineMs = 2000;
        public const int StatsEveryMs = 5000;

        private readonly AtomSpectraRestClient _client;
        private readonly int _periodMs;
        private readonly int _requestChannels;              // N of ?channels=N, 0 = no parameter
        private readonly AtomSpectraRestPeriod _period;     // the effective period
        private long _lastAnsweredMs;                       // clock time of the last answer (0 = the start of the clock)
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private readonly object _stopLock = new object();
        private bool _stopClosed;                           // _stopEvent is disposed (by the poll thread on its way out, or by Dispose)
        private Thread _thread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private volatile bool _alive;
        private volatile bool _linkLost;
        private int _consecutiveFailures;
        private int _answeredFailures;                      // polls with a failing ANSWER of the board since the last good poll (silent polls neither add nor end the run)
        private int _lastAnsweredStatus;                    // the HTTP status of the last failing answer (0 after a good poll); 401/403 = a refusal of access
        private int _attempts;
        private int _successes;
        private int _failures;
        private int _noData;
        private int _maxInFlight;
        private int _inFlight;
        private int _lastWireBytes;
        private string _lastEncoding;
        private string _lastError;
        private AtomSpectraRestDataReadyArgs _last;
        private volatile AtomSpectraRestBoardSample _lastSample;

        // Every answered poll: a snapshot, or HTTP 404 "No spectrum data yet" (Data == null). The session decides what it means.
        public event EventHandler<AtomSpectraRestSampleArgs> Sampled;
        public event EventHandler LinkLost;
        public event EventHandler LinkRestored;

        public AtomSpectraRestAcquisition(AtomSpectraRestClient client, int periodSeconds)
            : this(client, periodSeconds, 0)
        {
        }

        public AtomSpectraRestAcquisition(AtomSpectraRestClient client, int periodSeconds, int requestChannels)
        {
            if (client == null) throw new ArgumentNullException("client");
            _client = client;
            _requestChannels = AtomSpectraRestSpectrum.RequestChannels(requestChannels);

            int clamped = periodSeconds;
            if (clamped < 1) clamped = 1;
            if (clamped > 60) clamped = 60;
            _periodMs = clamped * 1000;
            _period = new AtomSpectraRestPeriod(_periodMs);
        }

        public int DeadlineMs
        {
            get
            {
                // max(2 s, period / 2) of the effective period (the adaptive period lengthens both); at 1 s the poll may
                // outlive the period, Run then skips ticks (no queue)
                return Math.Max(MinDeadlineMs, _period.EffectiveMs / 2);
            }
        }

        public int EffectivePeriodMs { get { return _period.EffectiveMs; } }
        public string SpectrumPath { get { return "/api/spectrum.json" + (_requestChannels > 0 ? "?channels=" + _requestChannels.ToString(CultureInfo.InvariantCulture) : ""); } }

        public bool IsLinkLost
        {
            get { return _linkLost; }
        }

        // the status of the last failing answer; a board that answers 401/403 is there but refuses access (a token), it is not lost
        public int LastAnsweredStatus { get { return Volatile.Read(ref _lastAnsweredStatus); } }
        public bool AccessDenied
        {
            get { int s = LastAnsweredStatus; return s == 401 || s == 403; }
        }

        public bool IsRunning
        {
            get { return _thread != null && _thread.IsAlive; }
        }

        public int PeriodMs
        {
            get { return _periodMs; }
        }

        // Counters for the log and the tests
        public int Attempts { get { return Volatile.Read(ref _attempts); } }
        public int Successes { get { return Volatile.Read(ref _successes); } }
        public int Failures { get { return Volatile.Read(ref _failures); } }
        public int MaxInFlight { get { return Volatile.Read(ref _maxInFlight); } }
        public string LastError { get { return _lastError; } }
        public int NoDataAnswers { get { return Volatile.Read(ref _noData); } }
        public AtomSpectraRestDataReadyArgs Last { get { return _last; } }
        public AtomSpectraRestBoardSample LastSample { get { return _lastSample; } }
        public long NowMs { get { return _clock.ElapsedMilliseconds; } }
        // Age of the last answered poll, s (-1: none yet): "data is N s old" in the log window.
        public double DataAgeSeconds { get { AtomSpectraRestBoardSample l = _lastSample; return l == null ? -1 : (NowMs - l.AtMs) / 1000.0; } }

        public void Start()
        {
            if (_thread != null) return;
            lock (_stopLock)
            {
                if (_stopClosed) return;   // disposed before it was started
                _stopEvent.Reset();
            }

            _alive = true;

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "AtomSpectraRest"
            };

            AtomSpectraTcpLog.Info("REST", AtomSpectraTcpLog.Format("polling {0}/api/spectrum.json every {1} s, deadline {2} ms", _client.BaseUrl, _periodMs / 1000, DeadlineMs));

            _thread.Start();
        }

        private void SetStop()
        {
            lock (_stopLock)
            {
                if (!_stopClosed) _stopEvent.Set();
            }
        }

        private void CloseStop()
        {
            lock (_stopLock)
            {
                if (_stopClosed) return;
                _stopClosed = true;
                _stopEvent.Dispose();
            }
        }

        // the stop event may be gone already (a test polls by hand after Dispose): then the stop is asked for
        private bool WaitStop(int ms)
        {
            try { return _stopEvent.WaitOne(ms); }
            catch (ObjectDisposedException) { return true; }
        }

        // Ask the poll thread to end after the request in flight, without waiting for it; the thread is a background one and disposes
        // the stop event itself on its way out, and no snapshot is raised after this (the session unhooks before it signals).
        public void Signal()
        {
            _alive = false;
            SetStop();
        }

        // Ends the polling and WAITS for the thread (up to a deadline): not for the UI thread, which uses Signal.
        public void Dispose()
        {
            Thread t = _thread;
            _alive = false;
            SetStop();

            if (t == null)
            {
                CloseStop();   // never started: no poll thread to dispose the event
                return;
            }
            if (t != Thread.CurrentThread)
            {
                t.Join(DeadlineMs + 1000);
                if (t.IsAlive)
                {
                    AtomSpectraTcpLog.Warn("REST", "poll thread did not stop in time");
                }
                _thread = null;
            }
        }

        public bool PollOnce()
        {
            int now = Interlocked.Increment(ref _inFlight);
            // update MaxInFlight with a compare loop
            while (true)
            {
                int current = Volatile.Read(ref _maxInFlight);
                if (now <= current) break;
                if (Interlocked.CompareExchange(ref _maxInFlight, now, current) == current) break;
            }

            try
            {
                Interlocked.Increment(ref _attempts);

                AtomSpectraRestResponse r;
                string error;
                AtomSpectraRestDataReadyArgs data;
                int answeredStatus;
                FetchWithRetry(out r, out data, out error, out answeredStatus);   // a failed attempt is repeated inside the period (see there)
                _lastWireBytes = r.WireBytes;
                _lastEncoding = r.ContentEncoding;

                if (r.Status == 404)
                {
                    Interlocked.Increment(ref _noData);
                    MarkGood();
                    AtomSpectraRestBoardSample none = AtomSpectraRestBoardSample.NoData(NowMs);
                    _lastSample = none;
                    AtomSpectraTcpLog.Info("REST", "404 No spectrum data yet" + (r.SpectrometerDead ? " (X-Spectrometer-Dead)" : "") + ": the board has no spectrum, the link is fine");
                    RaiseSampled(none, null);
                    return true;
                }

                if (error != null)
                {
                    Interlocked.Increment(ref _failures);
                    int cf = Interlocked.Increment(ref _consecutiveFailures);
                    _lastError = error;
                    int periodInForce = _period.EffectiveMs;   // the period this poll was scheduled with, before the doubling below
                    // missed deadline, transport error or an answer that did not parse = a skipped poll: no Trip
                    PeriodAfter(false);

                    AtomSpectraTcpLog.Warn("REST", AtomSpectraTcpLog.Format("poll failed ({0} in a row): {1}", cf, error));

                    // The link is lost by SILENCE: no answer for max(3 periods, 15 s); the period is the larger of
                    // the configured one and the one in force. The plain count of failures is not used: they come fast when a board refuses.
                    long silentMs = NowMs - Interlocked.Read(ref _lastAnsweredMs);
                    bool silent = silentMs >= Math.Max(3L * Math.Max(_periodMs, periodInForce), (long)LinkLostFloorMs);
                    // and by LinkLostAfterFailures polls in which the board ANSWERED with a failure, counted since the last good poll
                    // (a board that answers 503 for ever is no link; a silent poll neither adds nor ends the run, so 503 and silence in turn count too)
                    int answeredInRow = answeredStatus > 0 ? Interlocked.Increment(ref _answeredFailures) : Volatile.Read(ref _answeredFailures);
                    if (answeredStatus > 0) Interlocked.Exchange(ref _lastAnsweredStatus, answeredStatus);
                    if ((silent || answeredInRow >= LinkLostAfterFailures) && !_linkLost)
                    {
                        _linkLost = true;
                        AtomSpectraTcpLog.Error("REST", AtomSpectraTcpLog.Format("link lost after {0} failed polls, {1} ms without an answer, {2} answered failures in a row", cf, silentMs, answeredInRow));
                        RaiseLinkLost();
                    }

                    return false;
                }

                Interlocked.Increment(ref _successes);
                _last = data;
                MarkGood();
                PeriodAfter(true);
                AtomSpectraRestBoardSample sample = AtomSpectraRestSpectrum.ToSample(data, NowMs);
                _lastSample = sample;
                RaiseSampled(sample, data);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        // a failed attempt is repeated INSIDE the period (see FetchWithRetry)
        public const int RetryPauseMs = 200;
        // the attempt of one poll is max(MinDeadlineMs, 0.3 x period) = period * AttemptPermille / 1000; the ONLY place of the 0.3
        public const int AttemptPermille = 300;
        public static int MaxAttempts = 3;   // attempts per period at most (a test sets 1 to feed one case per poll)
        private int _retries;
        public int Retries { get { return Volatile.Read(ref _retries); } }

        // error == null: a snapshot (data) or a 404 (data == null, the caller tells them apart by the status)
        private void Judge(AtomSpectraRestResponse r, out AtomSpectraRestDataReadyArgs data, out string error)
        {
            data = null;
            error = null;
            if (r.Status == 404) return;
            error = r.Ok ? null : (r.Error ?? "HTTP " + r.Status.ToString(CultureInfo.InvariantCulture));
            if (error == null) data = AtomSpectraRestSpectrum.Parse(r.Body, _requestChannels, out error);
        }

        // Repeated: no answer (timeout, cancel, reset) and a cut-off body (the JSON does not parse). Not repeated: data, a 404 (an answer),
        // any other HTTP status, a whole answer whose data is wrong.
        private static bool Repeatable(AtomSpectraRestResponse r, string error)
        {
            if (r.Status == 404) return false;
            if (error == null) return false;
            if (r.BodyBroken) return true;   // a body cut short or corrupt (the status is the board's, it did answer)
            if (r.Status == 0) return !r.NotSent;   // a refused connect (NotSent) is not repeated: nothing listens, a pause of 0.2 s changes nothing
            return r.Ok && error.StartsWith("parse exception");
        }

        // An attempt lasts max(2 s, 0.3 x period); there are min(3, period / attempt) of them, at least one; the next one only if the time
        // spent + the pause + an attempt still fits the period. Each repeated attempt has a connection of its own.
        // answeredStatus: the HTTP status of the last attempt of THIS poll in which the board answered (0 = it answered in none); r is the LAST
        // attempt, which may have got nothing although an earlier one did
        private void FetchWithRetry(out AtomSpectraRestResponse r, out AtomSpectraRestDataReadyArgs data, out string error, out int answeredStatus)
        {
            answeredStatus = 0;
            int period = _period.EffectiveMs;
            int attemptMs = Math.Max(MinDeadlineMs, (int)((long)period * AttemptPermille / 1000));
            int attempts = Math.Max(1, Math.Min(MaxAttempts, period / attemptMs));
            Stopwatch spent = Stopwatch.StartNew();
            for (int n = 1; ; n++)
            {
                r = n == 1 ? _client.Get(SpectrumPath, attempts > 1 ? attemptMs : DeadlineMs) : _client.Get(SpectrumPath, attemptMs, "retry-" + Guid.NewGuid().ToString("N"));
                // ANY HTTP answer of ANY attempt (401, 500, 503, a body that does not parse) is the board answering: the silence does not build up
                if (r.Status > 0) MarkAnswered();
                if (r.Status > 0) answeredStatus = r.Status;
                Judge(r, out data, out error);
                if (!Repeatable(r, error) || n >= attempts || spent.ElapsedMilliseconds + RetryPauseMs + attemptMs > period)
                {
                    if (error != null) { _client.CloseConnections(""); }   // the connection of the LAST failed attempt is closed as well
                    return;
                }
                Interlocked.Increment(ref _retries);
                _client.CloseConnections(null);   // the connection of the failed attempt is closed before the repeat
                AtomSpectraTcpLog.Info("REST", AtomSpectraTcpLog.Format("poll attempt {0} of {1} failed ({2}), repeated inside the period", n, attempts, error));
                if (WaitStop(RetryPauseMs)) return;
            }
        }

        // full = a snapshot taken; a 404 "No spectrum data yet" is neither (the period stays)
        private void PeriodAfter(bool full)
        {
            int before = _period.EffectiveMs;
            if (full ? _period.Full() : _period.Skip())
            {
                AtomSpectraTcpLog.StaleAfterSeconds = 2 * _period.EffectiveMs / 1000 + 5;   // the freshness mark of the log window follows the period in force
                AtomSpectraTcpLog.Info("REST", AtomSpectraTcpLog.Format("poll period {0} s -> {1} s, deadline {2} ms", before / 1000.0, _period.EffectiveMs / 1000.0, DeadlineMs) +
                    (full ? AtomSpectraTcpLog.Format(" ({0} full polls in a row)", AtomSpectraRestPeriod.FullToHalve) : " (skipped poll: " + _lastError + ")"));
            }
        }

        private void MarkAnswered()
        {
            Interlocked.Exchange(ref _lastAnsweredMs, NowMs);   // any answer of the board (503, a bad body too): the silence does not build up
        }

        // only a GOOD poll (a snapshot or a 404 "no data yet") restores the link and ends the run of failures; a failing answer does not
        private void MarkGood()
        {
            MarkAnswered();
            Interlocked.Exchange(ref _answeredFailures, 0);
            Interlocked.Exchange(ref _lastAnsweredStatus, 0);
            if (_linkLost)
            {
                _linkLost = false;
                AtomSpectraTcpLog.Info("REST", "link restored");
                RaiseLinkRestored();
            }
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }

        private void Run()
        {
            long next = _clock.ElapsedMilliseconds;
            long nextStats = next + StatsEveryMs;

            try
            {
                while (_alive)
                {
                    long wait = next - _clock.ElapsedMilliseconds;
                    if (wait > 0 && WaitStop((int)wait)) break;
                    if (!_alive) break;

                    PollOnce();

                    next += _period.EffectiveMs;

                    long nowMs = _clock.ElapsedMilliseconds;
                    if (next <= nowMs) next = nowMs + _period.EffectiveMs;

                    if (nowMs >= nextStats)
                    {
                        LogStats();
                        nextStats = nowMs + StatsEveryMs;
                    }
                }
            }
            finally
            {
                CloseStop();   // the loop ends only when the stop was asked for
            }
        }

        private void LogStats()
        {
            int attempts = Volatile.Read(ref _attempts);
            int successes = Volatile.Read(ref _successes);
            int failures = Volatile.Read(ref _failures);
            int wireBytes = Volatile.Read(ref _lastWireBytes);
            string encoding = _lastEncoding ?? "uncompressed";
            if (encoding == null || encoding.Length == 0) encoding = "uncompressed";

            long total = 0;
            double time = 0;
            double live = 0;
            double dead = 0;
            int cps = 0;

            if (_last != null)
            {
                total = _last.Total;
                time = _last.ElapsedTime;
                live = _last.LiveSeconds;
                dead = _last.SpectrometerSilent ? 1 : 0;
                cps = _last.Cps;
            }

            string msg = AtomSpectraTcpLog.Format("REST stats: period {0} s (configured {1} s), polls {2} (ok {3}, failed {4}), last response {5} B ({6}), last total {7}, time {8} s, live {9} s, {10}, cps {11}, 404 answers {12}",
                         _period.EffectiveMs / 1000.0, _periodMs / 1000, attempts, successes, failures, wireBytes, encoding, total,
                         time, live, dead > 0 ? "analyzer silent (dead=1)" : "dead=0", cps, Volatile.Read(ref _noData));

            AtomSpectraTcpLog.Info("REST", msg);
        }

        private void RaiseSampled(AtomSpectraRestBoardSample sample, AtomSpectraRestDataReadyArgs data)
        {
            EventHandler<AtomSpectraRestSampleArgs> handler = Sampled;
            if (handler != null)
            {
                try
                {
                    handler(this, new AtomSpectraRestSampleArgs { Sample = sample, Data = data });
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("REST", "Sampled handler exception: " + ex.Message);
                }
            }
        }

        private void RaiseLinkLost()
        {
            EventHandler handler = LinkLost;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("REST", "LinkLost handler exception: " + ex.Message);
                }
            }
        }

        private void RaiseLinkRestored()
        {
            EventHandler handler = LinkRestored;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AtomSpectraTcpLog.Warn("REST", "LinkRestored handler exception: " + ex.Message);
                }
            }
        }
    }
}
