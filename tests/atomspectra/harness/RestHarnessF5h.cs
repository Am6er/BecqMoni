// tests of the gzip handling of the client.
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5h(string scenario)
        {
            Action body = null;
            switch (scenario)
            {
                case "f5-gz-status": body = F5hStatus; break;
                case "f5-gz-bomb": body = F5hBomb; break;
                case "f5-gz-accept-only": body = F5hAcceptOnly; break;
                case "f5-gz-bad-crc": body = F5hBadCrc; break;
                case "f5-fail-closes": body = F5hFailCloses; break;
                case "f5-answer-any-attempt": body = F5hAnyAttempt; break;
                case "f5-retry-count-hang": body = F5hCountHang; break;
                default: return RunF5i(scenario);   // RestHarnessF5i.cs
            }
            F5dWith(body);
            return true;
        }
        // Case 3: a 200 whose gzip is cut short is an ANSWER of the board with a bad body: the status is kept (200), the error is named, and a
        // board that always answers so is not silent (no link loss 15.3 s on)
        static void F5hStatus()
        {
            var r = Client().Get("/api/spectrum.json?channels=8192", 3000);
            Console.WriteLine("status " + r.Status + ", error " + r.Error + ", not sent " + r.NotSent);
            Check(r.Status == 200 && r.Error != null && !r.NotSent, "the status of the answer is kept with a cut gzip (" + r.Status + ")");
            int lost = 0;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => lost++;
            a.PollOnce();
            while (a.NowMs < 15300) Thread.Sleep(100);
            a.PollOnce();
            Check(a.Failures == 2, "both polls failed (" + a.Failures + ")");
            Check(lost == 0 && !a.IsLinkLost, "an answer with a cut gzip is not silence: no link loss after 15.3 s");
            a.Dispose();
        }
        // Case 2: the decoded body has a limit (1 MiB; the body of N=8192 is about 26 KB). A gzip bomb (3 MiB of zeros in a few KB) is a refusal,
        // named, not repeated (it is no cut-off body), and the body is not handed on.
        static void F5hBomb()
        {
            var r = Client().Get("/api/spectrum.json?channels=8192", 5000);
            Console.WriteLine("status " + r.Status + ", wire " + r.WireBytes + " B, body " + (r.Body == null ? -1 : r.Body.Length) + " chars, error " + r.Error);
            Check(r.Error != null && r.Error.Contains("limit"), "the body over the limit is refused with the reason named (" + r.Error + ")");
            Check(r.Body == null || r.Body.Length <= 1048576, "the decoded body is not handed on past the limit");
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            Check(!a.PollOnce() && a.Retries == 0, "a bomb is a failed poll and is not repeated (retries " + a.Retries + ")");
            a.Dispose();
        }

        // Case 9: only gzip is asked for (the board does not send deflate, and its integrity is not checked)
        static void F5hAcceptOnly()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            Check(a.PollOnce(), "the poll succeeds");
            var rq = F5gSpec();
            Check(rq.Count == 1 && Str(rq[0], "accept_encoding") == "gzip", "Accept-Encoding is exactly gzip (\"" + (rq.Count > 0 ? Str(rq[0], "accept_encoding") : "-") + "\")");
            a.Dispose();
        }
        // Case 1: a gzip whose CRC32 is wrong (length right) is a body that was not carried whole: a failed attempt, repeated; the repeat is plain
        static void F5hBadCrc()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            var rq = F5gSpec();
            Console.WriteLine("bad crc: poll " + p + ", requests " + rq.Count + ", retries " + a.Retries + ", failures " + a.Failures);
            Check(rq.Count >= 1 && F5gGz(rq[0]), "the first answer was a gzip with a wrong CRC");
            Check(a.Retries == 1 && rq.Count == 2, "a wrong CRC is a failed attempt and is repeated (retries " + a.Retries + ", requests " + rq.Count + ")");
            Check(p && a.Failures == 0, "the repeat gives the document: no skipped poll");
            a.Dispose();
        }
        // Case 4: the connection of ANY failed attempt is closed, also the last one and an answer that is not repeated (a 503 here): the mock
        // holds no connection but the probe's afterwards
        static void F5hFailCloses()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            Check(!a.PollOnce(), "the board answers 503: a failed poll, not repeated");
            int open = -2;
            bool ok = WaitFor(() => { open = F5gOpenConnections(); return open == 1; }, 4000);
            Console.WriteLine("connections held by the mock (probe included): " + open);
            Check(ok, "the connection of the failed attempt is closed (" + open + " open, the probe's one is 1)");
            a.Dispose();
        }

        // a period of three timed-out attempts is ONE failure in a row. The truncated-answer scenario (f5-retry-skip-once) no longer
        // sees a per-attempt count: an answered attempt now resets the counter (MarkAnswered in the loop), so this one uses attempts without an answer.
        static void F5hCountHang()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            Console.WriteLine("hanging board, period 10 s: " + p + ", retries " + a.Retries + ", failures " + a.Failures);
            Check(!p && a.Retries == 2 && a.Failures == 1, "three attempts, one skipped poll");
            Check(F5dCount("poll failed (1 in a row)") == 1, "one failure in a row for the period");
            Check(F5dCount("poll failed (2 in a row)") == 0 && F5dCount("poll failed (3 in a row)") == 0, "not one per attempt");
            a.Dispose();
        }

        // Case 5: an answer of ANY attempt is an answer: attempt 1 hangs, attempt 2 answers with a body that does not parse, attempt 3 hangs.
        // The time of the last answer (read from the field) is that of attempt 2, not the start of the clock.
        static void F5hAnyAttempt()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            FieldInfo f = typeof(AtomSpectraRestAcquisition).GetField("_lastAnsweredMs", BindingFlags.NonPublic | BindingFlags.Instance);
            long at = f == null ? -1 : Convert.ToInt64(f.GetValue(a));
            Console.WriteLine("poll " + p + ", retries " + a.Retries + ", last answer at " + at + " ms, now " + a.NowMs + " ms");
            Check(f != null && !p && a.Retries == 2, "three attempts, none gave data (retries " + a.Retries + ")");
            Check(at >= 2500, "the answer of attempt 2 is an answer: it was at " + at + " ms, not at the start of the clock");
            a.Dispose();
        }

    }
}
