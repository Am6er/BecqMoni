// TCP Start sent -sto/-rst/-sta by itself and erased the board's acquisition; now it asks.
using System;
using System.Linq;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        // TCP Start without a window follows the REST rules (AtomSpectraRestPolicy.PlanStart):
        // a counting board is joined (nothing sent), a stopped board with data is REFUSED (nothing sent, the board's measurement stays), an empty board: -sta only
        static void RunTcpStartHeadless(AtomSpectraTcpSession s, string mode, AtomSpectraTcpBeginOutcome outcome, System.Collections.Generic.List<string> cmds)
        {
            bool none = !cmds.Contains("-sto") && !cmds.Contains("-rst") && !cmds.Contains("-sta");
            if (mode == "headless-counting")
                Check(outcome == AtomSpectraTcpBeginOutcome.Started && none && s.JoinedRunning, "headless, a counting board: joined, no -sto, -rst, -sta (outcome " + outcome + ")");
            if (mode == "headless-stopped")
                Check(outcome == AtomSpectraTcpBeginOutcome.Refused && none && !string.IsNullOrEmpty(s.LastError), "headless, a stopped board with data: refused, nothing sent to the board (outcome " + outcome + ", error " + s.LastError + ")");
            if (mode == "headless-idle")
                Check(outcome == AtomSpectraTcpBeginOutcome.Started && !cmds.Contains("-sto") && !cmds.Contains("-rst") && cmds.Contains("-sta"), "headless, an empty board: -sta only, no reset (outcome " + outcome + ")");
        }

        // console-tcp-start-<continue|new|idle|stopped>: the bridge mock (mock_cmd.py) logs every text command
        static bool RunTcpStart(string mode)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
            var ask = typeof(AtomSpectraTcpSession).GetProperty("Ask");
            if (ask == null)
            {
                Check(false, "the TCP session has no Start question (Ask)");
                s.Dispose();
                return true;
            }
            int asked = 0;
            bool counting = false;
            bool headless = mode.StartsWith("headless-");   // no window, Ask stays null; the same rules as the REST Start without a window
            if (!headless) ask.SetValue(s, new Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>(q =>
            {
                asked++;
                counting = q.Counting;
                Console.WriteLine("asked: counting " + q.Counting + ", board " + (q.Board == null ? -1 : q.Board.Time) + " s | " + q.Note);
                return mode == "new" ? AtomSpectraRestStartChoice.Restart : AtomSpectraRestStartChoice.Continue;
            }));
            var outcome = s.Begin(true, 2000);
            Thread.Sleep(500);
            var cmds = Requests().Select(r => Str(r, "cmd")).Where(c => c != null).ToList();
            Console.WriteLine("outcome " + outcome + ", commands: " + string.Join(" ", cmds));
            if (headless) { RunTcpStartHeadless(s, mode, outcome, cmds); s.Dispose(); return true; }
            Check(outcome == AtomSpectraTcpBeginOutcome.Started, "Start is done");
            if (mode == "continue")
            {
                Check(asked == 1 && counting, "a counting board: the question is asked");
                Check(!cmds.Contains("-sto") && !cmds.Contains("-rst") && !cmds.Contains("-sta"), "Continue on a counting board: no -sto, -rst, -sta");
                Check(s.JoinedRunning, "Continue on a counting board: the session says it joined");
                var stop = s.Stop(1000);
                Thread.Sleep(300);
                var after = Requests().Select(r => Str(r, "cmd")).Where(c => c != null).ToList();
                Console.WriteLine("stop " + stop + ", commands: " + string.Join(" ", after));
                Check(stop == AtomSpectraTcpStopOutcome.Detached && !after.Contains("-sto"), "Stop of a joined acquisition: detached, no -sto (the board keeps its run)");
            }
            if (mode == "new")
            {
                Check(asked == 1, "a counting board: the question is asked");
                Check(cmds.Contains("-sto") && cmds.Contains("-rst") && cmds.Contains("-sta"), "New acquisition: -sto, -rst, -sta");
            }
            if (mode == "idle")
            {
                Check(asked == 0, "an idle empty board: no question");
                Check(!cmds.Contains("-sto") && !cmds.Contains("-rst") && cmds.Contains("-sta"), "an idle empty board: -sta only, no reset");
            }
            if (mode == "stopped")
            {
                Check(asked == 1 && !counting, "a stopped board with data: the question is asked");
                Check(!cmds.Contains("-rst") && !cmds.Contains("-sto") && cmds.Contains("-sta"), "Continue on a stopped board: -sta without a reset");
            }
            s.Dispose();
            return true;
        }
    }
}
