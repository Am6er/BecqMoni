// Tests of the response compression handling and of connection reuse.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5g(string scenario)
        {
            Action body = null;
            switch (scenario)
            {
                case "f5-guard-e5": body = F5gE5; break;
                case "f5-guard-e6": body = F5gE6; break;
                case "f5-linklost-answered": body = () => F5gAnswered("503"); break;
                case "f5-linklost-answered-data": body = () => F5gAnswered("data"); break;
                case "f5-retry-close-first": body = F5gCloseFirst; break;
                case "f5-retry-close-groups": body = F5gCloseGroups; break;
                case "f5-gz-request": body = F5gGzRequest; break;
                case "f5-gz-same": body = F5gGzSame; break;
                case "f5-gz-cut-trailer": body = () => F5gGzCut("trailer"); break;
                case "f5-gz-cut-mid": body = () => F5gGzCut("mid"); break;
                case "f5-gz-plain": body = F5gGzPlain; break;
                default: return RunF5h(scenario);   // RestHarnessF5h.cs
            }
            F5dWith(body);
            return true;
        }
        // a snapshot of a board that reports no epoch (no ?channels / old firmware)
        static AtomSpectraRestBoardSample GN(long time, long total, long hist, long atMs)
        {
            return new AtomSpectraRestBoardSample { HasData = true, Time = time, Total = total, HistOk = hist, AtMs = atMs };
        }

        static long F5gField(object g, string name)
        {
            FieldInfo f = typeof(AtomSpectraRestGuard).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Check(false, "the guard has no field " + name); return long.MinValue; }
            return Convert.ToInt64(f.GetValue(g));
        }

        // Case 5: a board without the epoch. Clear 1: the answer is lost (no confirmation, no withdrawal: the outcome is unknown, the window
        // lives). Clear 2: its request certainly never went out (NotSent). The withdrawal of Clear 2 must give back the window of Clear 1
        // WITH its flag of an unknown outcome: the smaller counters that come next are the answer to Clear 1, not a drop of the counters.
        static void F5gE5()
        {
            var g = new AtomSpectraRestGuard();
            g.Begin(0, 0, true);
            Check(g.Check(GN(100, 5000, 10, 500)) == AtomSpectraRestUpdate.Apply, "the board is joined");
            g.ExpectOwnReset(1000);   // Clear 1: the answer is lost
            g.ExpectOwnReset(2000);   // Clear 2 ...
            g.CancelOwnReset();       // ... NotSent
            AtomSpectraRestUpdate u = g.Check(GN(5, 40, 11, 3000));
            Console.WriteLine("smaller counters after Clear 1: " + u + " " + g.TripReason);
            Check(u == AtomSpectraRestUpdate.Apply && !g.Tripped, "the withdrawal of Clear 2 keeps the unknown outcome of Clear 1: no Trip (" + g.TripReason + ")");
        }

        // Case 6: a confirmed repeated Clear does not overwrite windowT0 / the shift / the count of changes of the window it found (the read of
        // "done" and the write are under one lock; the state is read here from the fields). Its answer is still awaited after the older
        // window would have ended (max(6 s, 2 x period) after the shift): the window is kept for the term of the new opening.
        static void F5gE6()
        {
            var g = new AtomSpectraRestGuard();
            g.Begin(0, 0, true);
            Check(g.Check(GS(100, 5000, 10, 500, 7, 3, 0)) == AtomSpectraRestUpdate.Apply, "the board is joined");
            g.ExpectOwnReset(1000);
            g.ConfirmOwnReset();   // Clear 1
            Check(g.Check(GS(5, 40, 11, 2000, 7, 3, 1)) == AtomSpectraRestUpdate.Apply && !g.Tripped, "the answer to Clear 1 (spec_resets+1) is taken");
            Check(F5gField(g, "windowT0") == 1000 && F5gField(g, "windowShiftAt") == 2000 && F5gField(g, "windowChanges") == 1, "the window of Clear 1: t0 1000, shift 2000, one change");
            g.ExpectOwnReset(9000);
            g.ConfirmOwnReset();   // Clear 2, confirmed, no answer yet
            Console.WriteLine("after Clear 2: t0 " + F5gField(g, "windowT0") + ", shift " + F5gField(g, "windowShiftAt") + ", changes " + F5gField(g, "windowChanges"));
            Check(F5gField(g, "windowT0") == 1000, "windowT0 of the older window is not overwritten (" + F5gField(g, "windowT0") + ")");
            Check(F5gField(g, "windowShiftAt") == 2000, "the shift is not overwritten (" + F5gField(g, "windowShiftAt") + ")");
            Check(F5gField(g, "windowChanges") == 1, "the count of changes is not overwritten (" + F5gField(g, "windowChanges") + ")");
            AtomSpectraRestUpdate u = g.Check(GS(3, 30, 12, 12000, 7, 3, 2));   // the answer to Clear 2, after the term of the older window (8000)
            Check(u == AtomSpectraRestUpdate.Apply && !g.Tripped, "the answer to Clear 2 is taken as the answer, not as a reset from outside (" + g.TripReason + ")");
            u = g.Check(GS(8, 70, 13, 19000, 7, 4, 2));   // 7 s after the answer: the window of 6 s has ended
            Check(u == AtomSpectraRestUpdate.Decreased && g.Tripped, "a reset from outside after the window stops polling");
        }
        // Any answer of the board (HTTP 503, or a 200 whose body does not parse) is not SILENCE: two such skipped polls 15.3 s apart
        // do not lose the link . But a board that keeps answering with failures is not a link in order either:
        // LinkLostAfterFailures (3) answered failures in a row = the link is lost, ONCE (the event), the polling goes on.
        static void F5gAnswered(string kind)
        {
            int lost = 0, failuresAtLost = -1;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => { lost++; failuresAtLost = a.Failures; };
            // two polls, 15.3 s apart: the period in force doubles after a skipped poll, so a long series of quick polls would move the
            // threshold (3 x the period in force) away from the silence and the test could not tell an answer from silence
            a.PollOnce();
            while (a.NowMs < 15300) Thread.Sleep(100);
            a.PollOnce();
            Console.WriteLine(kind + ": failures " + a.Failures + ", time " + a.NowMs + " ms, lost " + lost);
            Check(a.Failures == 2, "both polls were skipped ones (" + a.Failures + ")");
            Check(lost == 0 && !a.IsLinkLost, "two answered failures 15.3 s apart (" + kind + "): no link loss yet, the board answers");
            a.PollOnce();
            Console.WriteLine(kind + ": failures " + a.Failures + ", lost " + lost + ", at the event " + failuresAtLost);
            Check(lost == 1 && a.IsLinkLost && failuresAtLost == 3, "the third answered failure in a row (" + kind + "): the link is lost, once (events " + lost + ", failures at the event " + failuresAtLost + ")");
            a.PollOnce();
            Check(lost == 1 && a.IsLinkLost && a.Failures == 4, "a fourth failure: no second event, the polling goes on (events " + lost + ", failures " + a.Failures + ")");
            a.Dispose();
        }
        static int F5gOpenConnections()
        {
            // the probe has a connection of its own, which counts itself: 1 = nothing else is open
            var r = Client().Get("/__open", 2000, "probe-" + Guid.NewGuid().ToString("N"));
            Match m = Regex.Match(r.Body ?? "", "\"open\"\\s*:\\s*(\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        // Case 2: the first attempt answered in full but the body does not parse; it is repeated on a connection of its own. The connection of the
        // failed first attempt must be closed before the repeat: afterwards the mock holds no connection but the probe's.
        static void F5gCloseFirst()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            Check(p && a.Retries == 1, "a truncated body is repeated and the poll succeeds (retries " + a.Retries + ")");
            int open = -2;
            bool ok = WaitFor(() => { open = F5gOpenConnections(); return open == 1; }, 4000);
            Console.WriteLine("connections held by the mock (probe included): " + open);
            Check(ok, "the connection of the failed attempt is closed (" + open + " open, the probe's one is 1)");
            a.Dispose();
        }
        // Case 3: the groups retry-<Guid> do not pile up in the service point: three repeated attempts leave none of them.
        static void F5gCloseGroups()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            for (int i = 0; i < 3; i++) Check(a.PollOnce(), "poll " + i + " succeeds after one repeat");
            var sp = ServicePointManager.FindServicePoint(new Uri("http://127.0.0.1:" + port + "/"), WebRequest.DefaultWebProxy);
            FieldInfo f = typeof(ServicePoint).GetField("m_ConnectionGroupList", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(f != null, "the service point keeps its groups in m_ConnectionGroupList (reflection)");
            if (f != null)
            {
                var list = f.GetValue(sp) as Hashtable;
                int groups = 0;
                if (list != null) lock (list.SyncRoot) { foreach (object k in list.Keys) if (k.ToString().Contains("retry-")) groups++; }
                Console.WriteLine("retry groups left in the service point: " + groups + ", retries " + a.Retries);
                Check(a.Retries == 3 && groups == 0, "no retry-<Guid> group is left (" + groups + ")");
            }
            a.Dispose();
        }
        static List<Dictionary<string, object>> F5gSpec()
        {
            return Requests().Where(r => Str(r, "path") == "/api/spectrum.json").ToList();
        }

        static bool F5gGz(Dictionary<string, object> r)
        {
            return Str(r, "gz").ToLowerInvariant() == "true";
        }

        // gzip: the request carries Accept-Encoding: gzip
        static void F5gGzRequest()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            Check(a.PollOnce(), "the poll succeeds");
            var rq = F5gSpec();
            Check(rq.Count == 1 && Str(rq[0], "accept_encoding").Contains("gzip"), "the spectrum request asks for gzip (\"" + (rq.Count > 0 ? Str(rq[0], "accept_encoding") : "-") + "\")");
            a.Dispose();
        }
        static bool F5gSameDoc(AtomSpectraRestDataReadyArgs x, AtomSpectraRestDataReadyArgs y)
        {
            if (x == null || y == null) return false;
            bool cal = (x.Calibration == null && y.Calibration == null) || (x.Calibration != null && y.Calibration != null && x.Calibration.SequenceEqual(y.Calibration));
            bool ep = x.Epoch != null && y.Epoch != null && x.Epoch.BootId == y.Epoch.BootId && x.Epoch.DevResets == y.Epoch.DevResets && x.Epoch.SpecResets == y.Epoch.SpecResets;
            return x.Histogram.SequenceEqual(y.Histogram) && x.Total == y.Total && x.LiveSeconds == y.LiveSeconds && x.Channels == y.Channels && x.CalibChannels == y.CalibChannels && cal && ep;
        }

        // gzip: a gzipped answer (Content-Encoding: gzip, the board's X-Spectrum-Gz, whatever the request asked for) gives the same document
        // as the plain one: bins, boot_id / dev_resets / spec_resets, calib_channels, channels
        static void F5gGzSame()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            Check(a.PollOnce(), "the plain answer");
            var d1 = a.Last;
            Check(a.PollOnce(), "the gzipped answer");
            var d2 = a.Last;
            var rq = F5gSpec();
            Check(rq.Count == 2 && !F5gGz(rq[0]) && F5gGz(rq[1]), "the second answer was gzipped, the first plain");
            Check(a.Failures == 0 && a.Retries == 0, "no failed attempt (" + a.Failures + " failures, " + a.Retries + " retries)");
            Check(d1 != null && d2 != null && !object.ReferenceEquals(d1, d2), "two documents");
            Check(F5gSameDoc(d1, d2), "the gzipped answer gives the same document (bins, epoch, calib_channels, channels)");
            a.Dispose();
        }

        // gzip: a body cut short is a failed attempt, repeated inside the period. "trailer": the 8 bytes of CRC/ISIZE are cut off (the
        // framework may decode what is left without a word, the JSON is whole: only the trailer tells it is incomplete); "mid": cut in the middle.
        // The repeat gets a plain answer, so the test needs the repeat only, not the decoding.
        static void F5gGzCut(string kind)
        {
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            bool p = a.PollOnce();
            var rq = F5gSpec();
            Console.WriteLine(kind + ": poll " + p + ", requests " + rq.Count + ", retries " + a.Retries + ", failures " + a.Failures);
            Check(rq.Count >= 1 && F5gGz(rq[0]), "the first answer was a cut gzip");
            Check(a.Retries == 1 && rq.Count == 2, "a cut gzip is a failed attempt and is repeated (retries " + a.Retries + ", requests " + rq.Count + ")");
            Check(p && a.Failures == 0, "the repeat gives the document: no skipped poll");
            a.Dispose();
        }

        // gzip: the plain path is as before; a client that sends no Accept-Encoding gets the plain document, the same one
        static void F5gGzPlain()
        {
            var cl = Client();
            cl.SendAcceptEncoding = false;
            var a = new AtomSpectraRestAcquisition(cl, 10, 8192);
            Check(a.PollOnce(), "a plain answer to a request without Accept-Encoding");
            var d1 = a.Last;
            cl.SendAcceptEncoding = true;
            Check(a.PollOnce(), "a plain answer to a request with Accept-Encoding");
            var d2 = a.Last;
            var rq = F5gSpec();
            Check(rq.Count == 2 && Str(rq[0], "accept_encoding") == "" && !F5gGz(rq[0]) && !F5gGz(rq[1]), "no Accept-Encoding on the first request, both answers plain");   // what the second request carries is f5-gz-request's business
            Check(F5gSameDoc(d1, d2), "the same document either way");
            a.Dispose();
        }

    }
}
