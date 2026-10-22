using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix(string scenario, string[] args)
        {
            const int T = 1000;
            Stopwatch sw = Stopwatch.StartNew();

            if (scenario == "overtake")
            {
                var c = new Counter();
                int foreign = 0;
                var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                Hook(s, c);
                s.DataReady += (o, e) =>
                {
                    // mock v2: the foreign run is the board shape shifted by 300 channels (channels 0..299 empty)
                    if (e.Histogram != null && e.Histogram.Length > 300 && e.Histogram.Take(300).All(x => x == 0) && e.Histogram.Skip(300).Any(x => x > 0))
                        Interlocked.Increment(ref foreign);
                };

                var o1 = s.Begin(true, T);
                Check(o1 == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Check(WaitFor(() => c.Reset != null, 15000), "another acquisition detected (reset + overtake)");
                Check(foreign == 0, "no snapshot of another acquisition reached the document");
                s.Detach();
                s.Dispose();
                return true;
            }

            if (scenario == "restart-doc")
            {
                var c = new Counter();
                var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Restart));
                s.DocumentSeconds = 100;
                s.DocumentCounts = 50;
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Refused && s.Failure.ToString() == "DocumentNotEmpty", "Start over is refused in a document that holds data");
                Check(Posts() == 0, "nothing sent");
                s.Dispose();
                return true;
            }

            if (scenario == "preset-new")
            {
                var c = new Counter();
                var s = NewSession(false, 7200, null);
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.StartNew, "new acquisition started (-sta 7200)");
                int p = s.PresetAfterStart(3600);
                Console.WriteLine("preset after a new run with limit 7200: " + p);
                Check(p >= 7200, "the board's limit 7200 s governs, not the document preset 3600 s");
                s.Detach();
                s.Dispose();

                var s0 = NewSession(false, 0, null);
                int p0 = s0.PresetAfterStart(3600);
                Console.WriteLine("preset with no configured limit (no Begin, board time 0): " + p0);
                Check(p0 == 3600, "no configured limit: the document preset 3600 s stays the limit (fact)");
                s0.Dispose();
                return true;
            }

            if (scenario == "deferred-foreign")
            {
                // the -sto never reached the board (refused); meanwhile someone else stopped it
                // (and later resumes it): the deferred -sto must be cancelled, not sent into that acquisition
                AtomSpectraRestSession.RetryStopEveryMs = 1000;
                var s = NewSession(false, 0, null);
                Check(s.Begin(true, T) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Thread.Sleep(Math.Max(0, 6000 - (int)sw.ElapsedMilliseconds));
                var so = s.Stop(T);
                Check(so == AtomSpectraTcpStopOutcome.Deferred, "board unreachable at Stop: Deferred (got " + so + ")");
                Thread.Sleep(12000);   // stabilization: 3 s more margin for the deferred decision
                int stoSent = Requests().Count(r => Str(r, "path") == "/api/command" && Str(r, "body").Trim() == "-sto");
                Check(stoSent == 0, "no deferred -sto into a board stopped by someone else (sent " + stoSent + ")");
                s.Dispose();
                return true;
            }

            if (scenario == "deferred-resumed")
            {
                AtomSpectraRestSession.RetryStopEveryMs = 1000;
                var c = new Counter();
                var s = NewSession(false, 0, null);
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Thread.Sleep(Math.Max(0, 6000 - (int)sw.ElapsedMilliseconds));
                var so = s.Stop(T);
                // the -sto reached the board (an answer came back, only the result is unknown): no automatic repeat
                Check(so.ToString() == "Unconfirmed", "the -sto reached the board but its answer was lost: Unconfirmed (got " + so + ")");
                Thread.Sleep(12000);   // stabilization: 3 s more margin for the deferred decision
                string got = string.Join(",", Accepted());
                Console.WriteLine("accepted: " + got);
                // the mock answers every -sto with 503 (answer lost), so count the attempts, not the accepted ones
                int stoSent = Requests().Count(r => Str(r, "path") == "/api/command" && Str(r, "body").Trim() == "-sto");
                Console.WriteLine("-sto sent: " + stoSent);
                Check(stoSent == 1, "the deferred -sto is not repeated against a board that already stopped (and was resumed by someone else)");
                s.Dispose();
                return true;
            }

            if (scenario == "clear-stale")
            {
                var c = new Counter();
                var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                Hook(s, c);
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Check(WaitFor(() => c.DataReady >= 2, 6000), "snapshots before Clear");
                long before = c.FirstTotal;
                bool cl = s.ClearNow(T);
                Check(cl, "Clear accepted");
                Thread.Sleep(6000);
                Check(c.Reset == null, "our own reset is not taken for a reset from outside (stale cached snapshot ignored)");
                Check(c.MinTotal < before, "the new acquisition after Clear reached the document");
                s.Detach();
                s.Dispose();
                return true;
            }

            if (scenario == "session-key")
            {
                var cfg = new AtomSpectraTcpDeviceConfig();
                cfg.RestUrl = "http://127.0.0.1:1/";
                cfg.RestAuthKind = AtomSpectraRestAuth.Bearer;
                cfg.RestAuthSecret = "abc-secret";
                string k1 = cfg.RestSessionKey();
                cfg.RestAuthSecret = "abc-secret";
                string k2 = cfg.RestSessionKey();
                Check(k1 == k2, "saving the same secret again keeps the session key");
                cfg.RestAuthSecret = "other";
                Check(cfg.RestSessionKey() != k1, "a different secret changes the key");
                Check(!k1.Contains("abc-secret"), "the key does not carry the plain secret");
                return true;
            }

            if (scenario == "headless-stopped")
            {
                var c = new Counter();
                var s = NewSession(false, 0, null);
                // a stopped board with data is NOT started without a question (was: Resume, -sta);
                // with no window to ask in, the Start is refused and nothing is sent to the board
                var withData = new AtomSpectraRestBoardSample { HasData = true, Time = 100, Total = 5000, HistOk = 10, AtMs = 0 };
                var planned = AtomSpectraRestPolicy.PlanStart(false, false, false, withData, 0, 0);
                Check(planned == AtomSpectraRestStartPlan.Ask, "headless, stopped board with data: the plan is Ask, not Resume (" + planned + ")");
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Refused, "no window to ask in: Begin is refused (" + o + ")");
                Check(string.Join(",", Accepted()) == "", "nothing is sent to the board (\"" + string.Join(",", Accepted()) + "\")");
                s.Detach();
                s.Dispose();
                return true;
            }

            if (scenario == "stop-hang")
            {
                // Determinism (stabilization): period 10 s gives a poll deadline of 5 s; the mock hangs only the poll
                // (from 12 s), so the poll started at ~13 s is still in flight when Stop runs at 14 s, and the -sto
                // answers at once. The property is checked by state, not by a millisecond threshold: after Stop the
                // poll thread is still alive (not waited for); the mutation joins it and returns only after it ended.
                var s = new AtomSpectraRestSession(Client(), 10, false, 0);
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Thread.Sleep(Math.Max(0, 14000 - (int)sw.ElapsedMilliseconds));
                var acq = s.Acquisition;
                Check(acq != null && acq.IsRunning, "polling runs before Stop");
                var w = Stopwatch.StartNew();
                var so = s.Stop(T);
                bool stillPolling = acq != null && acq.IsRunning;
                Console.WriteLine("Stop took " + w.ElapsedMilliseconds + " ms, outcome " + so + ", poll thread alive after Stop: " + stillPolling);
                Check(stillPolling, "Stop does not wait for the poll hanging on the board (poll thread still alive when Stop returns)");
                s.Dispose();
                return true;
            }

            if (scenario == "url-userinfo")
            {
                Check(!AtomSpectraTcpDeviceConfig.IsValidRestUrl("http://user:pass@192.0.2.19/"), "URL with user:pass is refused by the form check");
                var cfg = new AtomSpectraTcpDeviceConfig();
                cfg.RestUrl = "http://user:pass@127.0.0.1/";
                string err;
                var cl = cfg.CreateRestClient(out err);
                Check(cl == null, "no client from a URL with user:pass");
                Check(err == null || !err.Contains("pass@"), "the error text does not repeat the password");
                return true;
            }

            return RunPassC(scenario, args);
        }
    }
}
