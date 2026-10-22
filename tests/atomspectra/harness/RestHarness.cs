using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static int port;
        static string logPath;
        static string fail;

        static int checksRun;   // the number of checks executed; a real-build-only scenario that ran none (n/a) is not a pass (rest_suite.py reads "checks executed: N")

        static void Check(bool ok, string what)
        {
            checksRun++;
            Console.WriteLine((ok ? "ok   " : "BAD  ") + what);
            if (!ok && fail == null) fail = what;
        }

        static AtomSpectraRestClient Client()
        {
            return new AtomSpectraRestClient("http://127.0.0.1:" + port + "/");
        }

        static List<Dictionary<string, object>> Requests()
        {
            var list = new List<Dictionary<string, object>>();
            if (logPath == null || logPath == "-") return list;
            try
            {
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var obj = AtomSpectraJson.Parse(line) as Dictionary<string, object>;
                            if (obj != null) list.Add(obj);
                        }
                        catch (FormatException)
                        {
                            // skip malformed lines
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ignore IO errors
            }
            return list;
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            if (d == null) return "";
            object v;
            if (d.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
        }

        static int Num(Dictionary<string, object> d, string k)
        {
            if (d == null) return 0;
            object v;
            if (d.TryGetValue(k, out v) && v != null)
            {
                try { return (int)(double)v; }
                catch (Exception) { return 0; }
            }
            return 0;
        }

        static List<string> Accepted()
        {
            var res = new List<string>();
            foreach (var r in Requests())
            {
                if (Str(r, "method") == "POST" && Num(r, "status") == 200)
                {
                    string path = Str(r, "path");
                    if (path == "/api/reset") res.Add("RESET");
                    else if (path == "/api/command") res.Add(Str(r, "body").Trim());
                }
            }
            return res;
        }

        static int Posts()
        {
            // an empty or unread mock log must not pass as "0 POST"
            var rq = Requests();
            return rq.Count == 0 ? -1 : rq.Count(r => Str(r, "method") == "POST");
        }

        static bool WaitFor(Func<bool> cond, int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(100);
            }
            return cond();
        }

        static AtomSpectraRestSession NewSession(bool observe, int limit, Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> ask)
        {
            var s = new AtomSpectraRestSession(Client(), 1, observe, limit);
            s.Ask = ask;
            return s;
        }

        class Counter
        {
            public int DataReady;
            public long MinTotal = long.MaxValue;
            public long FirstTotal = -1;
            public long MinTime = long.MaxValue;   // the document time, not only the counts
            public long FirstTime = -1;
            public int Asked;
            public AtomSpectraRestStartQuestion LastQuestion;
            public int LinkLost;
            public string Reset;
            public int Stalled;
        }

        static void Hook(AtomSpectraRestSession s, Counter c)
        {
            s.DataReady += (sender, e) =>
            {
                lock (c)
                {
                    c.DataReady++;
                    var r = e as AtomSpectraRestDataReadyArgs;
                    if (r != null)
                    {
                        if (c.FirstTotal < 0) c.FirstTotal = r.Total;
                        c.MinTotal = Math.Min(c.MinTotal, r.Total);
                        if (c.FirstTime < 0) c.FirstTime = r.ElapsedTime;
                        c.MinTime = Math.Min(c.MinTime, r.ElapsedTime);
                    }
                }
            };
            s.LinkLost += (sender, e) => { c.LinkLost++; };
            s.BoardReset += (sender, e) => { c.Reset = e ?? "?"; };
            s.BoardStalled += (sender, e) => { c.Stalled++; };
        }

        static Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> Answer(Counter c, AtomSpectraRestStartChoice choice)
        {
            return q =>
            {
                c.Asked++;
                c.LastQuestion = q;
                return choice;
            };
        }

        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("FAIL usage: RestHarness.exe <scenario> <port> <mockLogPath>");
                return 2;
            }

            string scenario = args[0];
            try { port = int.Parse(args[1]); } catch { port = 0; }
            logPath = args[2];
            fail = null;

            // Determinism: the mock's timeline starts here (GET /__sync, not logged), after the process start-up
            // and with the HTTP stack warm, so mock events and harness sleeps share one origin under any load.
            if (port > 0 && !scenario.StartsWith("console-tcp"))   // the TCP bridge mock has no HTTP clock
            {
                try
                {
                    var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:" + port + "/__sync");
                    rq.Proxy = null;
                    rq.Timeout = 10000;
                    using (rq.GetResponse()) { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL " + scenario + ": clock sync with the mock failed: " + ex.Message);
                    return 1;
                }
            }

            const int T = 1000;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                switch (scenario)
                {
                    case "policy":
                    {
                        var a = new AtomSpectraRestBoardSample { HasData = true, Time = 100, HistOk = 10, AtMs = 0 };
                        Check(AtomSpectraRestPolicy.IsCounting(a, new AtomSpectraRestBoardSample { HasData = true, Time = 100, HistOk = 10, AtMs = 1000 }), "gap < 3 s means counting (doubt)");
                        Check(!AtomSpectraRestPolicy.IsCounting(a, new AtomSpectraRestBoardSample { HasData = true, Time = 100, HistOk = 10, AtMs = 3200 }), "no growth over 3.2 s = stopped");
                        Check(AtomSpectraRestPolicy.IsCounting(a, new AtomSpectraRestBoardSample { HasData = true, Time = 100, HistOk = 13, AtMs = 3200 }), "hist_ok grew");
                        Check(AtomSpectraRestPolicy.IsCounting(a, new AtomSpectraRestBoardSample { HasData = true, Time = 103, HistOk = 10, AtMs = 3200 }), "time grew, hist_ok not");
                        Check(AtomSpectraRestPolicy.IsCounting(a, new AtomSpectraRestBoardSample { HasData = false, AtMs = 3200 }), "data vs 404 = doubt");
                        Check(AtomSpectraRestPolicy.StartCommand(0) == "-sta", "StartCommand(0) == -sta");
                        Check(AtomSpectraRestPolicy.StartCommand(120) == "-sta 120", "StartCommand(120) == -sta 120");
                        break;
                    }
                    case "preset":
                    {
                        Check(AtomSpectraRestPolicy.PresetForContinue(3600, 70000, 0) >= 73600, "default preset 3600 continues past board time 70000");
                        Check(AtomSpectraRestPolicy.PresetForContinue(3600, 70000, 600) == 70600, "PresetForContinue(3600, 70000, 600) == 70600");
                        Check(AtomSpectraRestPolicy.PresetForContinue(3600, 100, 0) == 3700, "PresetForContinue(3600, 100, 0) == 3700");
                        Check(AtomSpectraRestPolicy.PresetForContinue(100000, 70000, 0) == 170000, "PresetForContinue(100000, 70000, 0) == 170000");
                        break;
                    }
                    case "join-continue":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(c.Asked == 1, "Asked once");
                        Check(c.LastQuestion != null && c.LastQuestion.Counting, "LastQuestion.Counting is true");
                        Check(s.LastPlan == AtomSpectraRestStartPlan.Join, "LastPlan is Join");
                        Check(s.JoinedRunning, "JoinedRunning is true");
                        Check(Posts() == 0, "joining sends no POST");
                        Check(WaitFor(() => c.DataReady >= 1, 5000), "snapshots arrive");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "restart":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Restart));
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(s.RestartedBoard, "RestartedBoard is true");
                        Check(string.Join(",", Accepted()) == "-sto,RESET,-sta", "Accepted commands are -sto,RESET,-sta");
                        // the new acquisition after the reset reaches the document
                        Check(WaitFor(() => c.DataReady >= 1 && c.MinTotal < 400000, 6000), "data of the new acquisition arrives after Start over");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "cancel":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Cancel));
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Cancelled, "Begin returns Cancelled");
                        Check(Posts() == 0, "No POSTs");
                        Check(s.Acquisition == null, "Acquisition is null");
                        s.Dispose();
                        break;
                    }
                    case "observe":
                    {
                        var c = new Counter();
                        var s = NewSession(true, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(c.Asked == 0, "observe never asks");
                        Thread.Sleep(1500);
                        var so = s.Stop(T);
                        Check(so == AtomSpectraTcpStopOutcome.Detached, "Stop returns Detached");
                        bool cl = s.ClearNow(T);
                        Check(!cl && s.Failure == AtomSpectraRestFailure.ObserveOnly, "ClearNow fails with ObserveOnly");
                        Check(Posts() == 0, "observe only: 0 POST");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "stopped-empty":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(c.Asked == 0, "Asked 0 times");
                        Check(s.LastPlan == AtomSpectraRestStartPlan.StartNew, "LastPlan is StartNew");
                        Check(string.Join(",", Accepted()) == "-sta", "Accepted is -sta");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "stopped-data":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(c.Asked == 1, "Asked once");
                        Check(c.LastQuestion != null && !c.LastQuestion.Counting, "LastQuestion.Counting is false");
                        Check(s.LastPlan == AtomSpectraRestStartPlan.Resume, "LastPlan is Resume");
                        Check(string.Join(",", Accepted()) == "-sta", "Accepted is -sta (no RESET)");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "limit":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 120, Answer(c, AtomSpectraRestStartChoice.Continue));
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(string.Join(",", Accepted()) == "-sta 120", "Accepted is -sta 120");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "decrease":
                    case "time-back":
                    case "hist-reset":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(WaitFor(() => c.Reset != null, 15000), "board reset detected");
                        Check(c.DataReady >= 1, "DataReady >= 1");
                        Check(c.MinTotal >= c.FirstTotal, "no smaller snapshot reached the document");
                        Check(c.MinTime >= c.FirstTime, "no earlier board time reached the document");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "ext-reset":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(WaitFor(() => c.Reset != null, 15000), "board reset detected");
                        Check(c.DataReady >= 1, "snapshots before the reset (the check below is not trivial)");
                        Check(c.MinTotal >= c.FirstTotal, "no smaller snapshot reached the document");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "nodata-404":
                    case "dead-404":
                    {
                        var c = new Counter();
                        var s = NewSession(true, 0, null);
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Thread.Sleep(5000);
                        var a = s.Acquisition;
                        Check(a != null, "Acquisition is not null");
                        Check(a.NoDataAnswers >= 3, "NoDataAnswers >= 3");
                        if (scenario == "nodata-404")   // dead-404 checks only the dead mark (one property per scenario)
                        {
                            Check(a.Failures == 0, "404 is not a link failure");
                            Check(!a.IsLinkLost && c.LinkLost == 0, "No link lost");
                        }
                        // the board sends X-Spectrometer-Dead "0" or "1"; only "1" means a silent analyzer
                        bool deadLogged = AtomSpectraTcpLog.Snapshot().Any(l => l.ToString().Contains("(X-Spectrometer-Dead)"));
                        Check(deadLogged == (scenario == "dead-404"), "dead mark only for X-Spectrometer-Dead: 1 (logged " + deadLogged + ")");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "link-loss":      // the board refuses connections (mock --down-mode refuse)
                    case "link-silent":    // the board accepts and stays silent past the deadline
                    {
                        AtomSpectraRestAcquisition.LinkLostFloorMs = 3000;   // the 15 s floor is the product default (f5-linklost-silence); this scenario is about loss and recovery
                        AtomSpectraRestAcquisition.MaxAttempts = 1;   // and not about the repeat of a poll inside the period (f5-retry-*)
                        var c = new Counter();
                        var s = NewSession(true, 0, null);
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(WaitFor(() => c.LinkLost >= 1, 20000), "link lost after failed polls");
                        // with the adaptive period (x2 after a skipped poll) the 8 s silence of link-silent holds 2 polls, not 3;
                        // the loss itself comes by the time without an answer (3 configured periods, guarded by f5-linklost-time)
                        int minFailures = scenario == "link-silent" ? 2 : 3;
                        Check(WaitFor(() => s.Acquisition != null && !s.Acquisition.IsLinkLost && s.Acquisition.Failures >= minFailures, 20000), "link restored");
                        Check(c.LinkLost == 1, "LinkLost count is 1");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "clear-fail":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        bool cl = s.ClearNow(T);
                        Check(!cl && s.Failure == AtomSpectraRestFailure.ResetRefused, "ClearNow fails with ResetRefused");
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Thread.Sleep(3000);
                        int resets = Requests().Count(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/reset");
                        Check(resets == 1, "a failed Clear is not repeated by Start");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "ok-false":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.NoAnswer, "Begin returns NoAnswer");
                        Check(s.Failure == AtomSpectraRestFailure.StartRefused, "Failure is StartRefused");
                        Check(s.Acquisition == null, "Acquisition is null");
                        s.Dispose();
                        break;
                    }
                    case "close-joined":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, null);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.Join, "Started and Join");
                        Thread.Sleep(1500);
                        var so = s.Stop(T);
                        Check(so == AtomSpectraTcpStopOutcome.Detached, "Stop returns Detached");
                        Check(Posts() == 0, "closing a joined acquisition sends no -sto");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "deferred-stop":
                    case "deferred-stop-ok":
                    {
                        AtomSpectraRestSession.RetryStopEveryMs = 1000;
                        var c = new Counter();
                        var s = NewSession(false, 0, null);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        long elapsed = sw.ElapsedMilliseconds;
                        Thread.Sleep(Math.Max(0, 6000 - (int)elapsed));
                        var so = s.Stop(T);
                        Check(so == AtomSpectraTcpStopOutcome.Deferred, "Stop returns Deferred");
                        Thread.Sleep(12000);   // margin for the delivery at ~13 s
                        string got = string.Join(",", Accepted());
                        if (scenario == "deferred-stop")
                        {
                            Check(got == "-sta", "deferred -sto cancelled after a board reboot");
                            Check(!AtomSpectraRestSession.StopRetryPending("127.0.0.1", port), "StopRetryPending is false");
                        }
                        else
                        {
                            Check(got == "-sta,-sto", "deferred -sto delivered");
                        }
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "gzip":
                    {
                        var c = new Counter();
                        var s = NewSession(true, 0, null);
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(WaitFor(() => c.DataReady >= 1, 6000), "gzip snapshot decoded");
                        var rq = Requests();
                        Check(rq.Any(r => Str(r, "path") == "/api/spectrum.json" && Str(r, "accept_encoding").Contains("gzip")), "Accept-Encoding gzip sent");
                        Check(rq.Any(r => r.ContainsKey("gz") && r["gz"] is bool && (bool)r["gz"]), "the answer was compressed");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "queue":
                    {
                        var c = new Counter();
                        var s = NewSession(true, 0, null);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Thread.Sleep(6000);
                        var a = s.Acquisition;
                        Check(a.MaxInFlight == 1, "one request at a time");
                        Check(a.Attempts >= 3 && a.Attempts <= 6, "ticks skipped, no queue");
                        Check(a.DeadlineMs == 2000, "DeadlineMs is 2000");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "prefix-auth":
                    {
                        var cfg = new AtomSpectraTcpDeviceConfig();
                        cfg.Transport = AtomSpectraTransport.Rest;
                        cfg.RestUrl = "http://127.0.0.1:" + port + "/board1/";
                        cfg.RestAuthKind = AtomSpectraRestAuth.Bearer;
                        cfg.RestAuthSecret = "s3cr3t-TOKEN-xyz";
                        string err;
                        var client = cfg.CreateRestClient(out err);
                        Check(client != null, "Client created");
                        var s = new AtomSpectraRestSession(client, 1, true, 0);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Thread.Sleep(2000);
                        var rq = Requests();
                        // the board has data (--valid 1), so ANY 404 (lost prefix) or 401 is a failure
                        Check(rq.Count > 0 && rq.All(r => Num(r, "status") == 200), "prefix and Bearer accepted (all 200)");
                        string logText = string.Join("\n", AtomSpectraTcpLog.Snapshot().Select(l => l.ToString()));
                        Check(!logText.Contains("s3cr3t-TOKEN-xyz"), "secret not in the log");
                        Check(!AtomSpectraTcpLog.Mask("Authorization: Bearer s3cr3t-TOKEN-xyz").Contains("s3cr3t"), "Mask hides Authorization");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "config":
                    {
                        var cfg = new AtomSpectraTcpDeviceConfig();
                        cfg.RestAuthSecret = "pw-Пароль-1";
                        Check(cfg.RestAuthSecretProtected.Length > 0 && !cfg.RestAuthSecretProtected.Contains("pw-"), "Secret protected");
                        Check(cfg.RestAuthSecret == "pw-Пароль-1", "DPAPI round trip");
                        XmlSerializer ser = new XmlSerializer(typeof(AtomSpectraTcpDeviceConfig));
                        StringWriter xw = new StringWriter();
                        ser.Serialize(xw, cfg);
                        string xml = xw.ToString();
                        Check(!xml.Contains("pw-Пароль-1"), "no plain secret in XML");
                        var back = (AtomSpectraTcpDeviceConfig)ser.Deserialize(new StringReader(xml));
                        Check(back.RestAuthSecret == "pw-Пароль-1", "Deserialized secret matches");
                        var old = (AtomSpectraTcpDeviceConfig)ser.Deserialize(new StringReader("<?xml version=\"1.0\"?><AtomSpectraTcpDeviceConfig><Host>192.0.2.50</Host><TcpPort>8234</TcpPort></AtomSpectraTcpDeviceConfig>"));
                        Check(old.Transport == AtomSpectraTransport.TcpBridge && old.RestPollSeconds == 10 && old.RestUrl == "" && !old.RestObserveOnly, "old XML loads as TCP, 10 s");
                        cfg.RestPollSeconds = 0;
                        Check(cfg.RestPollSeconds == 1, "RestPollSeconds min 1");
                        cfg.RestPollSeconds = 100;
                        Check(cfg.RestPollSeconds == 60, "RestPollSeconds max 60");
                        Check(!AtomSpectraTcpDeviceConfig.IsValidRestUrl("ftp://x/"), "ftp invalid");
                        Check(AtomSpectraTcpDeviceConfig.IsValidRestUrl("https://h.example/p/"), "https valid");
                        var h = new AtomSpectraTcpDeviceConfig();
                        h.Host = "atomspectra.local";
                        Check(h.EffectiveRestUrl() == "http://atomspectra.local/", "EffectiveRestUrl correct");
                        break;
                    }
                    case "parse":
                    {
                        string json = "{\"bins\":[" + string.Join(",", Enumerable.Repeat("0", 8192)) + "],\"total\":0,\"time\":12,\"live\":11.9,\"hist_ok\":77,\"dead\":1,\"calib\":[3.5,0.41,0,0,0],\"calib_set\":true}";
                        string e;
                        var d = AtomSpectraRestSpectrum.Parse(json, out e);
                        Check(d != null, "Parse returns non-null");
                        Check(d.SpectrometerSilent, "dead is a flag");
                        Check(d.HistOk == 77, "HistOk is 77");
                        Check(d.Calibration != null && d.Calibration.Length == 5, "Calibration length 5");
                        var cal = AtomSpectraRestCalibration.ParseDevice(json);
                        Check(cal.Coefficients != null && cal.Coefficients.Length == 2 && cal.Coefficients[0] == 3.5, "calib[5] read from spectrum.json");
                        var cal2 = AtomSpectraRestCalibration.ParseDevice("{\"calibration\":[1.5,0.4,0.001,0,0],\"calib_set\":true}");
                        Check(cal2.Coefficients != null && cal2.Coefficients.Length == 3, "calibration length 3");
                        // the recorded answer of the real board (firmware 1.2.37; null fields t2/t3)
                        string live = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spectrum_sample.json"));
                        var b = AtomSpectraRestSpectrum.Parse(live, out e);
                        Check(b != null, "real board answer parsed (" + e + ")");
                        Check(b.Histogram.Sum(x => (long)x) == 288566 && b.Total == 288566, "sum(bins) == total == 288566");
                        Check(b.ElapsedTime == 57722, "time 57722 s");
                        Check(Math.Abs(b.LiveSeconds - 57721.2) < 1e-9, "live 57721.2 s");
                        Check(!b.SpectrometerSilent && b.HistOk == 2019 && b.Calibration != null && b.Calibration.Length == 5, "dead 0, hist_ok 2019, calib[5]");
                        break;
                    }
                    case "stall":
                    {
                        AtomSpectraRestSession.StallOverrideMs = 4000;
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        int postsAtBegin = Posts();
                        Check(WaitFor(() => c.Stalled >= 1, 15000), "board stop detected");
                        Check(Posts() == postsAtBegin, "nothing sent when the stop is detected");
                        s.Detach();
                        s.Dispose();
                        break;
                    }
                    case "headless":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, null);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.Join && Posts() == 0, "Started, Join, 0 POSTs");
                        s.Detach();
                        var s2 = NewSession(false, 0, null);
                        s2.DocumentSeconds = 1000000;
                        s2.DocumentCounts = 9000000;
                        var o2 = s2.Begin(true, T);
                        Check(o2 == AtomSpectraTcpBeginOutcome.Refused && s2.Failure == AtomSpectraRestFailure.HeadlessBehind, "Refused, HeadlessBehind");
                        Check(Posts() == 0, "No POSTs");
                        s2.Detach();
                        s2.Dispose();
                        break;
                    }
                    case "reopened":
                    {
                        var c = new Counter();
                        var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                        s.DocumentSeconds = 1000000;
                        s.DocumentCounts = 9000000;
                        var o = s.Begin(true, T);
                        Check(c.Asked == 1 && c.LastQuestion != null && !c.LastQuestion.ContinueAllowed, "Continue not offered when the board is behind");
                        Check(o == AtomSpectraTcpBeginOutcome.Cancelled, "Begin returns Cancelled");
                        Check(Posts() == 0, "No POSTs");
                        s.Dispose();
                        break;
                    }
                    case "live":   // real board, GET only: observe-only session (args[1] = base URL)
                    {
                        var c = new Counter();
                        var s = new AtomSpectraRestSession(new AtomSpectraRestClient(args[1]), 2, true, 0);
                        Hook(s, c);
                        var o = s.Begin(true, T);
                        Check(o == AtomSpectraTcpBeginOutcome.Started, "observe Begin on the live board");
                        Console.WriteLine("counting=" + s.Counting + " probe time=" + (s.LastProbe != null ? s.LastProbe.Time : -1));
                        Check(WaitFor(() => c.DataReady >= 3, 15000), "3 snapshots from the live board");
                        var a = s.Acquisition;
                        Console.WriteLine("attempts=" + a.Attempts + " failures=" + a.Failures + " 404=" + a.NoDataAnswers + " first total=" + c.FirstTotal + " min=" + c.MinTotal + " last=" + a.Last.Total + " sum(bins)=" + a.Last.Histogram.Sum(x => (long)x) + " hist_ok=" + a.Last.HistOk + " dead=" + a.Last.SpectrometerSilent);
                        Check(a.Last.Histogram.Sum(x => (long)x) == a.Last.Total, "sum(bins) == total");
                        var dev = AtomSpectraRestCalibration.ParseDevice(new AtomSpectraRestClient(args[1]).Get("/api/device", 3000).Body);
                        Check(dev.Coefficients != null && a.Last.Calibration != null && Math.Abs(dev.Coefficients[1] - a.Last.Calibration[1]) < 1e-12, "calib == /api/device calibration");
                        s.Stop(T);
                        break;
                    }
                    default:
                        if (RunFix(scenario, args)) break;   // fix scenarios (RestHarnessFixes.cs)
                        Console.WriteLine("FAIL " + scenario + ": unknown scenario");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + scenario + ": exception " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }

            Console.WriteLine("checks executed: " + checksRun);
            if (fail == null)
            {
                Console.WriteLine("PASS " + scenario);
                return 0;
            }
            else
            {
                Console.WriteLine("FAIL " + scenario + ": " + fail);
                foreach (var l in AtomSpectraTcpLog.Snapshot()) Console.WriteLine("    log: " + l);   // the client's own log, for the reader of a red run
                return 1;
            }
        }
    }
}
