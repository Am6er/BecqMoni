// Case 5 (the "no data" flag is ended by data only), the LinkLost-by-silence rule
// (silence >= max(3 x period in force, 15 s), no count of failures) and the retry of a poll inside its period (the retry rule).
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5f(string scenario)
        {
            Action body = null;
            switch (scenario)
            {
                case "f5-nodata-stale": body = F5fNoDataStale; break;
                case "f5-linklost-silence": body = F5fLinkLostSilence; break;
                case "f5-retry-ok": body = F5fRetryOk; break;
                case "f5-retry-skip-once": body = F5fRetrySkipOnce; break;
                case "f5-retry-period2": body = F5fRetryPeriod2; break;
                case "f5-retry-budget": body = F5fRetryBudget; break;
                case "f5-retry-404": body = F5fRetry404; break;
                case "f5-retry-slow": body = F5fRetrySlow; break;
                default: return RunF5g(scenario);   // RestHarnessF5g.cs
            }
            F5dWith(body);
            return true;
        }

        static int F5fSpecRequests()
        {
            return Requests().Count(r => Str(r, "path") == "/api/spectrum.json");
        }

        // Case 5: a snapshot that is Stale (the cache after our Clear) is not data: it does not end the "no data" series, so the
        // 404 that follow it are the same series and give no second line
        static void F5fNoDataStale()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            var a = s.Acquisition;
            Check(WaitFor(() => a.NoDataAnswers >= 6, 12000), "six 404 in a row");
            Check(F5dCount("board has no data yet") == 1, "one line for the series (" + F5dCount("board has no data yet") + ")");
            Check(s.ClearNow(2000), "Clear is accepted; the next snapshot is the cached old one (Stale)");
            Check(WaitFor(() => a.NoDataAnswers >= 12, 15000), "six more 404 after the Stale snapshot");
            Console.WriteLine("lines: " + F5dCount("board has no data yet") + ", data events " + c.DataReady);
            Check(F5dCount("board has no data yet") == 1, "still one line: only a snapshot that is taken ends the series (" + F5dCount("board has no data yet") + ")");
            Check(c.Reset == null && s.Acquisition != null, "polling goes on");
            s.Dispose();
        }

        // LinkLost only by silence >= max(3 x period in force, 15 s); the count of failures in a row does not count. The mock REFUSES the connection
        // (a 503 is an answer of the board, so it would not be silence): 4 in a row at a period of 2 s are 4 skipped polls, not a lost link.
        static void F5fLinkLostSilence()
        {
            int lost2 = 0, lost1 = 0;
            Thread.Sleep(700);   // the mock refuses from 0.3 s after the clock sync (a refusal from 0 would fail the sync itself)
            var a2 = new AtomSpectraRestAcquisition(Client(), 2, 8192);
            a2.LinkLost += (o, e) => lost2++;
            for (int i = 0; i < 4; i++) a2.PollOnce();
            Console.WriteLine("4 quick failures: failures " + a2.Failures + ", period " + a2.EffectivePeriodMs + " ms, lost " + lost2 + ", silence " + a2.NowMs + " ms");
            Check(a2.Failures == 4 && lost2 == 0 && !a2.IsLinkLost, "4 skipped polls in a row at a period of 2 s: no link loss");
            a2.Dispose();
            var a1 = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a1.LinkLost += (o, e) => lost1++;
            Thread.Sleep(6000);
            a1.PollOnce();
            Check(lost1 == 0 && !a1.IsLinkLost, "silence 6 s (more than 3 periods of 1 s, less than 15 s): no link loss");
            while (a1.NowMs < 15200) Thread.Sleep(100);
            a1.PollOnce();
            Check(lost1 == 1 && a1.IsLinkLost, "silence of 15 s: link lost (once)");
            a1.Dispose();
        }

        // Retry inside the period (period 10 s: attempt max(2 s, period/4) = 2.5 s, up to 3 attempts, the budget is the period).
        // k = 1 and k = 2 truncated/broken answers, then a full one: ONE good period, no skipped poll, no change of the period.
        static void F5fRetryOk()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            var sw = Stopwatch.StartNew();
            bool k1 = a.PollOnce();
            long t1 = sw.ElapsedMilliseconds;
            bool k2 = a.PollOnce();
            long t2 = sw.ElapsedMilliseconds - t1;
            Console.WriteLine("k=1 -> " + k1 + " in " + t1 + " ms, k=2 -> " + k2 + " in " + t2 + " ms, requests " + F5fSpecRequests());
            Check(k1 && k2, "a truncated answer, and a truncated plus a broken one, are repeated inside the period: the poll succeeds");
            Check(a.Failures == 0 && a.EffectivePeriodMs == 10000 && !a.IsLinkLost, "no skipped poll, the period stays 10 s");
            Check(F5fSpecRequests() == 5 && t1 <= 10000 && t2 <= 10000, "5 requests in all (2 + 3), each poll within its period");
            a.Dispose();
        }

        // k = 3: the whole period gave nothing = ONE skipped poll: one doubling, ONE failure in a row (not one per attempt)
        static void F5fRetrySkipOnce()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            Console.WriteLine("k=3 -> " + p + ", failures " + a.Failures + ", period " + a.EffectivePeriodMs + " ms");
            Check(!p && a.Failures == 1 && a.EffectivePeriodMs == 20000, "a period without a single good attempt: one skipped poll, the period 20 s");
            Check(F5dCount("poll failed (1 in a row)") == 1, "the failure is counted once for the period");
            Check(F5dCount("poll failed (2 in a row)") == 0 && F5dCount("poll failed (3 in a row)") == 0, "not once per attempt");
            a.Dispose();
        }

        // period 2 s: the attempt is 2 s, one attempt only (a skipped poll doubles the period, which gives more attempts next time)
        static void F5fRetryPeriod2()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 2, 8192);
            bool p = a.PollOnce();
            Check(!p && a.Failures == 1 && a.EffectivePeriodMs == 4000, "period 2 s: the truncated answer is a skipped poll");
            Check(F5fSpecRequests() == 1, "one attempt (" + F5fSpecRequests() + " requests)");
            a.Dispose();
        }

        // Budget: period 6 s, the board hangs. Attempt 2 s, pause 0.2 s: the second attempt ends at 4.2 s, a third would end at 6.4 s, over
        // the period, so it is not made. The poll takes 4.2 s, never more than the period.
        static void F5fRetryBudget()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 6, 8192);
            var sw = Stopwatch.StartNew();
            bool p = a.PollOnce();
            long took = sw.ElapsedMilliseconds;
            Console.WriteLine("hanging board, period 6 s: " + p + " in " + took + " ms");
            Check(!p && a.Failures == 1 && a.EffectivePeriodMs == 12000, "a period of timeouts is one skipped poll");
            Check(took >= 4000, "a timed-out attempt is repeated (took " + took + " ms)");
            Check(took <= 6100, "the attempts together stay within the period (took " + took + " ms)");
            a.Dispose();
        }

        // Period 10 s, the board hangs: the attempt is max(2 s, 0.3 x period) = 3 s and all THREE attempts fit the period with the pauses
        // (3 x 3 s + 2 x 0.2 s = 9.4 s). On period/4 (2.5 s) the poll takes 8.2 s; on period/3 (3.33 s) the third attempt does not start
        // (6.9 s + 0.2 s + 3.33 s > 10 s): only 1 repeat.
        static void F5fRetrySlow()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            var sw = Stopwatch.StartNew();
            bool p = a.PollOnce();
            long took = sw.ElapsedMilliseconds;
            Console.WriteLine("hanging board, period 10 s: " + p + " in " + took + " ms, retries " + a.Retries);
            Check(!p && a.Failures == 1, "a period of timeouts is one skipped poll");
            Check(a.Retries == 2, "three attempts at a period of 10 s = two repeats (" + a.Retries + ")");
            Check(took >= 9000, "each attempt lasts 3 s: 3 x 3 s + the pauses (took " + took + " ms)");
            Check(took <= 10000, "three attempts together stay within the period (took " + took + " ms)");
            a.Dispose();
        }

        // a 404 is an answer: not repeated (the board has no spectrum, the link is fine)
        static void F5fRetry404()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            Check(p && a.NoDataAnswers == 1 && a.Failures == 0, "404 No spectrum data yet is an answer");
            Check(F5fSpecRequests() == 1, "one request, a 404 is not repeated (" + F5fSpecRequests() + ")");
            a.Dispose();
        }

    }
}
