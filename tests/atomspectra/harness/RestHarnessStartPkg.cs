// "Replace document spectrum" at Start, and the TCP Start of the same document probes the board
using System;
using System.Linq;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        const long BehindSeconds = 1000000, BehindCounts = 9000000;   // a document far ahead of any mock board

        static void RunTcpSameReplace(AtomSpectraTcpSession s, AtomSpectraTcpBeginOutcome outcome, Func<int> data, Func<int> resets)
        {
            Check(outcome == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning && s.ReplacedDocument, "Replace: started, joined, the document's spectrum replaced (outcome " + outcome + ")");
            int d0 = data();   // the probe's samples went through the unarmed guard already
            Check(WaitFor(() => data() - d0 >= 2 || resets() > 0, 4000), "snapshots arrive after Replace");
            Check(resets() == 0 && data() - d0 >= 2, "the board's smaller counters are no reset from outside (snapshots " + (data() - d0) + ", resets " + resets() + ")");
        }

        static bool RunStartPkg(string scenario)
        {
            switch (scenario)
            {
                case "startpkg-replace":   // (a) REST: the board behind the document, Replace: nothing is sent, the board is joined
                {
                    var c = new Counter();
                    var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Replace));
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    Check(c.Asked == 1 && c.LastQuestion != null && !c.LastQuestion.ContinueAllowed && c.LastQuestion.ReplaceAllowed, "the board behind the document: Continue refused, Replace offered");
                    Check(o == AtomSpectraTcpBeginOutcome.Started, "Replace: Begin returns Started (outcome " + o + ")");
                    Check(s.LastPlan == AtomSpectraRestStartPlan.Join && s.JoinedRunning && s.ReplacedDocument, "Replace on a counting board: joined, the document's spectrum replaced (plan " + s.LastPlan + ")");
                    Thread.Sleep(1500);
                    Check(Posts() == 0, "Replace: no POST to the board (no -rst, -sta, -sto): " + string.Join(",", Accepted()));
                    s.Stop(1000);
                    Thread.Sleep(300);
                    Check(Posts() == 0, "Stop after Replace (a joined acquisition): no -sto");
                    s.Dispose();
                    return true;
                }
                case "startpkg-replace-guard":   // (c) REST: after Replace the board's smaller spectrum is no "reset from outside"
                {
                    var c = new Counter();
                    var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Replace));
                    Hook(s, c);
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.Started, "Replace: Begin returns Started (outcome " + o + ")");
                    Check(WaitFor(() => c.DataReady >= 3 || c.Reset != null, 8000), "snapshots arrive after Replace");
                    Check(c.Reset == null && s.Guard.TripReason == null, "the first polls after Replace do not trip the guard (" + (c.Reset ?? "no trip") + ")");
                    Check(c.DataReady >= 3 && c.FirstTotal >= 0 && c.FirstTotal < BehindCounts, "the board's smaller spectrum reaches the document (first total " + c.FirstTotal + ")");
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "console-tcp-same-behind":    // (b) TCP: Start of the SAME document, the board behind it: the question, -sta not sent
                case "console-tcp-same-replace":   // TCP: the same with Replace: joined, nothing sent, the guard starts from the board
                {
                    bool replace = scenario.EndsWith("replace");
                    var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
                    AtomSpectraRestStartQuestion asked = null;
                    s.Ask = q => { asked = q; return replace ? AtomSpectraRestStartChoice.Replace : AtomSpectraRestStartChoice.Cancel; };
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    int data = 0, resets = 0;
                    s.DataReady += (o, e) => Interlocked.Increment(ref data);
                    s.BoardReset += (o, e) => Interlocked.Increment(ref resets);
                    // behind: false = the same document goes on (#BM-9); replace: true, so that its mutation is of Replace alone
                    var outcome = s.Begin(replace, 2000);
                    Check(asked != null && !asked.ContinueAllowed && asked.ReplaceAllowed, "the same document, the board behind it: asked, Continue refused, Replace offered");
                    if (replace) RunTcpSameReplace(s, outcome, () => data, () => resets);
                    else Check(outcome == AtomSpectraTcpBeginOutcome.Cancelled, "Cancel: Begin returns Cancelled (outcome " + outcome + ")");
                    var cmds = Requests().Select(r => Str(r, "cmd")).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    Check(!cmds.Contains("-sta") && !cmds.Contains("-sto") && !cmds.Contains("-rst"), "nothing sent to the board: " + string.Join(" ", cmds));
                    s.Dispose();
                    return true;
                }
                // startpkg: cases
                default: return false;
            }
        }
    }
}
