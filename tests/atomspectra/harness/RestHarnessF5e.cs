// tests of the window rule for an own reset (cases 1, 2 and 9 below).
// Sequences follow the window rule above, not ad hoc examples.
using System;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5e(string scenario)
        {
            Action body = null;
            switch (scenario)
            {
                case "f5-window-repeat-notsent": body = F5eRepeatNotSent; break;
                case "f5-startover-external": body = F5eStartOverExternal; break;
                case "f5-window-lost-v1": body = F5eLostV1; break;
                default: return RunF5f(scenario);   // RestHarnessF5f.cs
            }
            F5dWith(body);
            return true;
        }

        // Case 1: Clear 1 is answered (spec_resets+1 at 2000, the window ends 6 s later, at 8000); Clear 2 opens a window at 3000 but its
        // request certainly never went out (NotSent): the window of Clear 1 must be back, so a real reset at 8500 stops polling
        static void F5eRepeatNotSent()
        {
            var g = new AtomSpectraRestGuard();
            g.Begin(0, 0, true);
            Check(g.Check(GS(100, 5000, 10, 500, 7, 3, 0)) == AtomSpectraRestUpdate.Apply, "the board is joined");
            g.ExpectOwnReset(1000);
            g.ConfirmOwnReset();   // Clear 1
            Check(g.Check(GS(5, 40, 11, 2000, 7, 3, 1)) == AtomSpectraRestUpdate.Apply && !g.Tripped, "the answer to Clear 1 (spec_resets+1) is taken");
            g.ExpectOwnReset(3000);   // Clear 2 ...
            g.CancelOwnReset();       // ... whose request never went out (NotSent)
            AtomSpectraRestUpdate u = g.Check(GS(8, 60, 12, 8500, 7, 4, 1));
            Console.WriteLine("a reset from outside at 8500 (the window of Clear 1 ended at 8000): " + u + " " + g.TripReason);
            Check(u == AtomSpectraRestUpdate.Decreased && g.Tripped, "NotSent of Clear 2 gives the window of Clear 1 back: the real reset stops polling");
            Check(g.TripReason != null && g.TripReason.StartsWith("board reset: dev_resets:"), "the stop names the field (" + g.TripReason + ")");
        }

        // Case 2: "Start over" (probes 5 s / 100 without the epoch of the new set). The first snapshot (spec_resets+1, 12 s / 400) is the
        // answer to our reset; a real reset from outside 10 s later (boot_id changed) is not part of it and stops polling.
        static void F5eStartOverExternal()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            s.Ask = Answer(c, AtomSpectraRestStartChoice.Restart);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started && s.RestartedBoard, "Begin returns Started (Start over)");
            Check(WaitFor(() => last[0] == 400, 8000), "the new acquisition reaches the document");
            Check(WaitFor(() => c.Reset != null || last[0] == 900, 20000), "the reset from outside 10 s later comes");
            Console.WriteLine("last total " + last[0] + ", reset text " + c.Reset + ", swallowed lines " + F5dCount("external reset swallowed"));
            Check(c.Reset != null && c.Reset.StartsWith("board reset: boot_id:"), "polling stops with the field named (" + c.Reset + ")");
            Check(last[0] == 400, "the document did not take the external snapshot (last total " + last[0] + ")");
            s.Dispose();
        }

        // Case 9 (the reachable variant: the answer to POST /api/reset is lost, client.Reset itself never throws): the board did reset and its
        // snapshots carry no epoch (no ?channels / old firmware). The unknown outcome keeps the window, so the smaller snapshot is the answer
        // to our reset, not a drop of the counters.
        static void F5eLostV1()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.DataReady >= 3, 8000), "three snapshots before the reset");
            bool ok = s.ClearNow(1500);   // the mock holds the answer 3 s
            Check(!ok, "the answer was lost: no confirmation");
            Check(WaitFor(() => last[0] == 1500 || c.Reset != null, 10000), "the smaller snapshot (15 s / 1500, no epoch fields) comes");
            Check(c.Reset == null && last[0] == 1500, "an unknown outcome: no Trip \"counters went down\", the new set is in the document (" + c.Reset + ")");
            s.Dispose();
        }

    }
}
