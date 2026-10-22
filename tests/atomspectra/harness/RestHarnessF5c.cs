// regression tests for the polling rules of the REST client
// and the BuildArgs extraction. Sequences are built from the code (guard rules, acquisition clock), not copied from examples.
// Guard-level scenarios feed the guard the samples the session would feed it; session-level ones use the recorded responses.
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static AtomSpectraRestBoardSample GS(long time, long total, long hist, long atMs, long boot, long dev, long spec)
        {
            return new AtomSpectraRestBoardSample { HasData = true, Time = time, Total = total, HistOk = hist, AtMs = atMs, Epoch = new AtomSpectraRestEpoch { BootId = boot, DevResets = dev, SpecResets = spec } };
        }

        static bool RunF5c(string scenario)
        {
            try
            {
                switch (scenario)
                {
                    case "f5-reset-board": F5cResetBoardGuard(); F5cResetBoardSession(); F5cResetBoardSeries(); return true;
                    case "f5-reset-own": F5cResetOwn(); return true;
                    case "f5-stale-epoch": F5cStaleEpoch(); return true;
                    case "f5-linklost-effective": F5cLinkLostEffective(); return true;
                    case "f5-nodata-nostall": F5cNoDataNoStall(); return true;
                    case "f5-period-floor": F5cPeriodFloor(); return true;
                    case "f5-clear-race": F5cClearRace(); return true;
                    case "f5-ctl-calib-channels": F5cBuildArgs(); return true;
                    default: return RunF5d(scenario);   // RestHarnessF5d.cs
                }
            }
            catch (Exception ex)
            {
                Check(false, "exception " + ex);
                return true;
            }
        }

        // a document of 3600 s, then a snapshot with spec_resets+1 and time 5 s
        static void F5cResetBoardGuard()
        {
            var g = new AtomSpectraRestGuard();
            g.Begin(3600, 50000, true);
            Check(g.Check(GS(3700, 60000, 500, 1000, 1, 1, 0)) == AtomSpectraRestUpdate.Apply, "the board is joined (3700 s >= the document 3600 s)");
            var u2 = g.Check(GS(5, 80, 501, 1500, 1, 1, 1));
            Console.WriteLine("a board reset: " + u2 + ", reason: " + g.TripReason);
            Check(u2 == AtomSpectraRestUpdate.Decreased && g.Tripped, "spec_resets+1, time 5 s: polling stops, the sample is never applied (the document is not overwritten)");
            Check(g.TripReason == "board reset: spec_resets: 0 -> 1", "the reason names the field: board reset: <field>: <was> -> <now>");
            Check(g.EpochChanges == 1 && g.LastEpochChange == "spec_resets: 0 -> 1", "EpochChanges/LastEpochChange stay for the log");
            Check(g.Check(GS(3800, 70000, 502, 2000, 1, 1, 1)) == AtomSpectraRestUpdate.Decreased, "a later bigger snapshot does not restart it");
        }

        // (b) the same through the session on the recorded cases: x01 changes all three fields, so cutting the comparison of ONE
        // field cannot turn this test red (the field tests f5-epoch-* own that)
        static void F5cResetBoardSession()
        {
            var c = new Counter();
            var s = new AtomSpectraRestSession(Client(), 1, true, 0);
            s.RequestChannels = 8192;
            s.DocumentSeconds = 3600;
            s.DocumentCounts = 1000;
            Hook(s, c);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.Reset != null, 12000), "x01 (all three epoch fields changed, time 15 s): BoardReset is raised");
            Console.WriteLine("session reset text: " + c.Reset);
            Check(c.Reset == "board reset: boot_id: 439041101 -> 439041102", "the session passes the reason on");
            int dr = c.DataReady;
            Thread.Sleep(1500);
            Check(s.Acquisition == null, "polling is stopped");
            Check(c.DataReady == dr && c.MinTotal == 13442130, "nothing after the change reached the document (smallest total applied: " + c.MinTotal + ")");
            s.Dispose();
        }

        // (c) 20 resets from the board side, 500 ms apart, the field rotating; every one stops with its field named
        static void F5cResetBoardSeries()
        {
            int stops = 0, applied = 0, named = 0;
            var g = new AtomSpectraRestGuard();
            string[] fields = { "boot_id", "dev_resets", "spec_resets" };
            for (int i = 0; i < 20; i++)
            {
                long at = i * 2000L;
                int k = i % 3;
                g.Begin(100, 1000, true);
                g.Check(GS(200 + i, 5000 + i, 10 + i, at, 7, 3, i));
                AtomSpectraRestUpdate r = g.Check(GS(2, 20, 11 + i, at + 500, k == 0 ? 8 : 7, k == 1 ? 4 : 3, k == 2 ? i + 1 : i));
                if (r == AtomSpectraRestUpdate.Decreased) stops++;
                if (r == AtomSpectraRestUpdate.Apply) applied++;
                if (g.TripReason != null && g.TripReason.StartsWith("board reset: " + fields[k] + ": ")) named++;
            }
            Console.WriteLine("board-side series: stops " + stops + ", applied " + applied + ", field named " + named);
            Check(stops == 20 && applied == 0 && named == 20, "20 board-side resets: 20 stops, 0 overwrites, the right field named every time");
        }

        // after OUR reset the new acquisition has already caught up with the numbers of the old one before the first poll
        static void F5cStaleEpoch()
        {
            for (int path = 0; path < 2; path++)
            {
                string name = path == 0 ? "Clear" : "Start over";
                var g = new AtomSpectraRestGuard();
                g.Begin(0, 0, true);
                Check(g.Check(GS(2, 30, 5, 1000, 1, 1, 0)) == AtomSpectraRestUpdate.Apply, name + ": the board is joined at 2 s / 30");
                if (path == 0) g.AcceptReset(); else g.AcceptReset(2, 30);
                AtomSpectraRestUpdate u = g.Check(GS(3, 45, 6, 2500, 1, 1, 1));
                Console.WriteLine(name + ": first snapshot after the reset (3 s / 45, spec_resets+1): " + u);
                Check(u == AtomSpectraRestUpdate.Apply, name + ": a new epoch is a new acquisition, not the cached old snapshot (Stale)");
                Check(g.Check(GS(4, 60, 7, 3000, 1, 1, 1)) == AtomSpectraRestUpdate.Apply && !g.Tripped, name + ": the next snapshot is taken too");
            }
            var h = new AtomSpectraRestGuard();
            h.Begin(0, 0, true);
            h.Check(GS(2, 30, 5, 1000, 1, 1, 0));
            h.AcceptReset();
            Check(h.Check(GS(3, 45, 6, 1500, 1, 1, 0)) == AtomSpectraRestUpdate.Stale, "the same epoch and not below the old numbers is still the cached snapshot (Stale)");
        }

        static int f5Apply, f5Stale, f5Stops;
        static long f5Spec, f5At, f5Hist, f5LastT, f5LastN;

        static void F5cStep(AtomSpectraRestGuard g, AtomSpectraRestBoardSample s)
        {
            AtomSpectraRestUpdate u = g.Check(s);
            if (u == AtomSpectraRestUpdate.Apply) f5Apply++; else if (u == AtomSpectraRestUpdate.Stale) f5Stale++; else if (u == AtomSpectraRestUpdate.Decreased) f5Stops++;
        }

        // one own reset: k 0 = Clear, 1 = Start over (the board bumps spec_resets), 2 = Clear without a bump, 3 = a board without epoch fields
        static void F5cOwnRound(AtomSpectraRestGuard g, int k)
        {
            if (k == 1) g.AcceptReset(f5LastT, f5LastN); else g.AcceptReset();
            F5cStep(g, GS(f5LastT, f5LastN, f5Hist, f5At += 500, 7, 3, f5Spec));   // the cache of the board still serves the old snapshot
            if (k < 2) f5Spec++;
            AtomSpectraRestBoardSample f1 = GS(1, 10, f5Hist + 1, f5At += 500, 7, 3, f5Spec), f2 = GS(2, 20, f5Hist + 2, f5At += 500, 7, 3, f5Spec);
            if (k == 3) { f1.Epoch = null; f2.Epoch = null; }
            F5cStep(g, f1);
            F5cStep(g, f2);
            f5LastT = 2; f5LastN = 20; f5Hist += 2;
        }

        // the client's own reset (Clear: AcceptReset(); Start over: AcceptReset(time, total)) never stops polling
        static void F5cResetOwn()
        {
            var g = new AtomSpectraRestGuard();
            g.Begin(0, 0, true);
            f5Apply = f5Stale = f5Stops = 0;
            f5Spec = 0; f5At = 0; f5Hist = 100; f5LastT = 100; f5LastN = 5000;
            Check(g.Check(GS(f5LastT, f5LastN, f5Hist++, f5At += 500, 7, 3, f5Spec)) == AtomSpectraRestUpdate.Apply, "the board is joined");
            for (int i = 0; i < 20; i++) F5cOwnRound(g, i % 4);
            Console.WriteLine("own-reset series: stale " + f5Stale + ", applied " + f5Apply + ", stops " + f5Stops + ", tripped " + g.Tripped + ", epoch changes " + g.EpochChanges);
            Check(f5Stops == 0 && !g.Tripped, "20 resets of the client itself, 500 ms apart: 0 stops, no Trip");
            Check(f5Stale == 20 && f5Apply == 40, "every cached old snapshot is Stale, every new one is taken");
            Check(g.EpochChanges == 0, "an epoch change caused by our own reset is not counted as a board reset");
        }

        // configured 1 s, the board answers nothing from the start (every poll is cut by its deadline)
        static void F5cLinkLostEffective()
        {
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            int lost = 0;
            a.LinkLost += (o, e) => lost++;
            bool p1 = a.PollOnce();
            bool p2 = a.PollOnce();
            Console.WriteLine("after 2 skipped polls: failures " + a.Failures + ", period " + a.EffectivePeriodMs + " ms, lost " + lost + ", clock " + a.NowMs + " ms");
            Check(!p1 && !p2 && a.Failures == 2 && a.EffectivePeriodMs == 4000, "two polls cut by the deadline, the period is doubled twice");
            Check(a.NowMs >= 3000, "more than 3 configured periods have passed without an answer");
            Check(lost == 0 && !a.IsLinkLost, "2 skipped polls of a doubled period: no link loss (the threshold follows the period in force)");
            // the third failure in a row WITH NO ANSWER is not a lost link (silence only, f5-linklost-silence); since the third failing ANSWER is (f5-linklost-answered, -mixed)
            a.Dispose();
        }

        // (scaled: configured 1 s, StallMs 5 s instead of 3 periods = 15 s): after two skipped polls the period is 4 s
        static void F5cNoDataNoStall()
        {
            long oldStall = AtomSpectraRestSession.StallOverrideMs;
            int oldGap = AtomSpectraRestSession.ProbeGapMs;
            AtomSpectraRestSession.StallOverrideMs = 5000;
            AtomSpectraRestSession.ProbeGapMs = 300;   // two looks inside 3 s = "counting", so the guard expects growth
            try { F5cNoDataBody(); }
            finally { AtomSpectraRestSession.StallOverrideMs = oldStall; AtomSpectraRestSession.ProbeGapMs = oldGap; }
        }

        static void F5cSecond404(AtomSpectraRestSession s, Counter c, AtomSpectraRestAcquisition a)
        {
            Check(WaitFor(() => a.NoDataAnswers >= 2, 8000), "the next 404 comes");
            Check(c.Stalled == 0 && c.Reset == null && s.Acquisition != null, "still no stall and no reset after the second 404");
        }

        static void F5cNoDataBody()
        {
            var c = new Counter();
            var s = new AtomSpectraRestSession(Client(), 1, true, 0);
            s.RequestChannels = 8192;
            Hook(s, c);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            var a = s.Acquisition;
            Check(WaitFor(() => a.NoDataAnswers >= 1, 20000), "the first 404 after two skipped polls");
            Thread.Sleep(600);
            Console.WriteLine("clock at the 404: " + a.NowMs + " ms, period " + a.EffectivePeriodMs + " ms, stalled " + c.Stalled + ", failures " + a.Failures);
            Check(a.Failures == 2 && a.NowMs >= 5000, "two skipped polls, more than StallMs since the last growth");
            Check(c.Stalled == 0 && s.Acquisition != null, "a 404 is not a stall: polling goes on");
            if (c.Stalled == 0) F5cSecond404(s, c, a);
            s.Detach();
            s.Dispose();
        }

        // the halving goes down to the floor of 2 s, not only down to the configured period
        static void F5cPeriodFloor()
        {
            var p = new AtomSpectraRestPeriod(10000);
            p.Skip();
            Check(p.EffectiveMs == 20000, "10 s, a skipped poll: 20 s");
            // rule "not more often than configured": the halving ends at the configured period, never below 2 s
            int[] want = { 10000, 10000, 10000 };
            for (int step = 0; step < want.Length; step++)
            {
                for (int i = 0; i < 5; i++) p.Full();
                Check(p.EffectiveMs == want[step], "configured 10 s, after " + (step + 1) * 5 + " full polls: " + want[step] + " ms (got " + p.EffectiveMs + ")");
            }
            var m = new AtomSpectraRestPeriod(4000);
            m.Skip();
            for (int i = 0; i < 10; i++) m.Full();
            Check(m.EffectiveMs == 4000, "configured 4 s: 8 s after a skip, 4 s after 5 full polls, and 4 s (not 2 s) after 10 (got " + m.EffectiveMs + ")");
            var n = new AtomSpectraRestPeriod(1000);
            n.Skip();
            for (int i = 0; i < 10; i++) n.Full();
            Check(n.EffectiveMs == 2000, "configured 1 s: after a skip not below 2 s (got " + n.EffectiveMs + ")");
            var q = new AtomSpectraRestPeriod(40000);
            for (int i = 0; i < 4; i++) q.Skip();
            Check(q.EffectiveMs == 40000, "the ceiling is max(30 s, configured): a configured 40 s stays 40 s");
            var r = new AtomSpectraRestPeriod(3000);
            for (int i = 0; i < 10; i++) r.Skip();
            Check(r.EffectiveMs == 30000, "the ceiling 30 s for a configured 3 s");
        }

        // BuildArgs of the controller (real build only): the REST snapshot keeps calib_channels through the fold
        static void F5cBuildArgs()
        {
            Type ctlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
            if (ctlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return; }
            MethodInfo bm = ctlT.GetMethod("BuildArgs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Check(bm != null, "the controller has a static BuildArgs");
            if (bm == null) return;
            string err;
            AtomSpectraRestDataReadyArgs src = AtomSpectraRestSpectrum.Parse(F5Text("c01_epoch_added.json"), 0, out err);
            Check(src != null && src.Histogram.Length == 8192, "c01: 8192 bins");
            if (src == null) return;
            src.CalibChannels = 2048;   // not the 8192 default, so a dropped copy shows
            F5cBuildArgsRest(bm, src);
            var plain = new AtomSpectraTcpDataReadyArgs(src.Histogram, 1, 2, 3, 4);
            object o3 = bm.Invoke(null, new object[] { plain, 4096, 4096 });
            var r3 = o3 as AtomSpectraTcpDataReadyArgs;
            Check(r3 != null && !(o3 is AtomSpectraRestDataReadyArgs) && r3.Histogram.Length == 4096, "a TCP snapshot stays a TCP snapshot, folded to the profile");
        }

        // Clear sends the reset, polling goes on, and the snapshot of the new acquisition (spec_resets+1)
        // arrives BEFORE the reset request returns. It is the answer to OUR reset, no board reset from outside.
        static void F5cClearRace()
        {
            int oldGap = AtomSpectraRestSession.ProbeGapMs;
            AtomSpectraRestSession.ProbeGapMs = 300;   // "counting": Join, no -sta
            try { F5cClearRaceBody(); }
            finally { AtomSpectraRestSession.ProbeGapMs = oldGap; }
        }

        static void F5cClearRaceBody()
        {
            var c = new Counter();
            var s = new AtomSpectraRestSession(Client(), 1, false, 0);
            s.RequestChannels = 8192;
            Hook(s, c);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.DataReady >= 1, 5000), "c01 is taken");
            bool ok = s.ClearNow(8000);   // the mock holds POST /api/reset for 3.5 s; the polls at 1 s and 2 s go on meanwhile
            Console.WriteLine("ClearNow " + ok + ", reset text " + c.Reset + ", epoch changes " + s.Guard.EpochChanges);
            Check(ok, "the reset was accepted");
            Check(c.Reset == null && s.Acquisition != null && s.Guard.EpochChanges == 0, "the new epoch that came during our reset is not a board reset: no stop");
            Check(WaitFor(() => c.MinTotal == 1500, 5000), "the new acquisition (total 1500) goes into the document");
            s.Detach();
            s.Dispose();
        }

        static void F5cBuildArgsRest(MethodInfo bm, AtomSpectraRestDataReadyArgs src)
        {
            // the profile has 1024 channels now, but the run was fixed at 4096 at Start: restChannels wins
            var r1 = bm.Invoke(null, new object[] { src, 1024, 4096 }) as AtomSpectraRestDataReadyArgs;
            Check(r1 != null, "a REST snapshot gives a REST snapshot");
            if (r1 != null)
            {
                Check(r1.Histogram.Length == 4096 && r1.Histogram.Sum(x => (long)x) == src.Histogram.Sum(x => (long)x), "folded to restChannels (4096), the sum is kept");
                Check(r1.CalibChannels == 2048, "calib_channels is carried over (2048, got " + r1.CalibChannels + ")");
                Check(r1.Total == src.Total && r1.HistOk == src.HistOk && r1.LiveSeconds.Equals(src.LiveSeconds) && ReferenceEquals(r1.Calibration, src.Calibration), "total, hist_ok, live time and calib are carried over");
            }
            var r2 = bm.Invoke(null, new object[] { src, 1024, 0 }) as AtomSpectraRestDataReadyArgs;
            Check(r2 != null && r2.Histogram.Length == 1024 && r2.CalibChannels == 2048, "restChannels 0: the channels of the profile (1024)");
        }
    }
}
