using System;
using System.Linq;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunPassC(string scenario, string[] args)
        {
            const int T = 1000;

            if (scenario == "dead-flag")
            {
                // Mock: counting board with "dead":1 in spectrum.json
                var c = new Counter();
                var s = NewSession(true, 0, null);
                Hook(s, c);
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Check(WaitFor(() => c.DataReady >= 1, 6000), "snapshot with dead=1 delivered");
                var a = s.Acquisition;
                Check(a != null && a.Last != null && a.Last.SpectrometerSilent, "dead=1 read as the flag 'analyzer silent'");
                s.Detach();
                s.Dispose();
                return true;
            }

            if (scenario == "reset-unsent")
            {
                // Mock: POST /api/reset answers {"ok":true,"sent":false}
                var c = new Counter();
                var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                bool cl = s.ClearNow(T);
                Console.WriteLine("ClearNow=" + cl + " Failure=" + s.Failure);
                Check(!cl, "a reset the analyzer did not receive (sent=false) is not reported as done");
                Check(s.Failure.ToString() == "ResetRefused", "Failure ResetRefused");
                bool warned = AtomSpectraTcpLog.Snapshot().Any(l => l.ToString().Contains("sent=false"));
                Check(warned, "the log says sent=false");
                s.Dispose();
                return true;
            }

            if (scenario == "live-nan")
            {
                // Mock: "live":nan as the board prints it
                var c = new Counter();
                var s = NewSession(true, 0, null);
                Hook(s, c);
                var o = s.Begin(true, T);
                Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                Check(WaitFor(() => c.DataReady >= 1, 6000), "a snapshot with live=nan is still delivered");
                var a = s.Acquisition;
                Check(a != null && a.Last != null && double.IsNaN(a.Last.LiveSeconds) && a.Last.ElapsedTime > 0, "live=nan gives NaN (the controller then computes live time itself), time intact");
                s.Detach();
                s.Dispose();
                return true;
            }

            return RunConsole(scenario, args);   // device console (RestHarnessConsole.cs)
        }
    }
}
