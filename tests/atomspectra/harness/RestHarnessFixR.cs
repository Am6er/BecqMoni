// Maintainer's fixes of PR #38, REST lane: R2 - after "Replace document spectrum" the guard starts from the snapshot the Start question
// showed (its time, counts, channels and epoch), not from zero; R6 - in observe only the Replace hint does not promise -sta.
using System;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFixR(string scenario)
        {
            switch (scenario)
            {
                case "fixr2-replace-reset-asked":   // R2: the board is reset from outside while the question is open; Replace must not take the new acquisition
                {
                    var c = new Counter();
                    AtomSpectraRestResponse outside = null;
                    var s = NewSession(false, 0, q =>
                    {
                        c.Asked++;
                        c.LastQuestion = q;
                        // the question may stay open for minutes: meanwhile someone resets the board (its web page) and it counts anew
                        outside = Client().Reset(2000);
                        Thread.Sleep(2500);
                        return AtomSpectraRestStartChoice.Replace;
                    });
                    Hook(s, c);
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    Check(outside != null && outside.Accepted, "the board was reset from outside while the question was open");
                    Check(c.Asked == 1 && c.LastQuestion != null && c.LastQuestion.ReplaceAllowed && c.LastQuestion.Board != null && c.LastQuestion.Board.Time >= 70000,
                        "asked with the board as it was before the reset (time " + (c.LastQuestion != null && c.LastQuestion.Board != null ? c.LastQuestion.Board.Time : -1) + " s)");
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning && s.ReplacedDocument, "Replace on a counting board: joined, the document's spectrum replaced (outcome " + o + ", plan " + s.LastPlan + ")");
                    Check(WaitFor(() => c.Reset != null, 6000), "the first poll trips: the board is not what the question showed (" + (c.Reset ?? "no trip") + ")");
                    Check(c.DataReady == 0, "nothing of the board's new acquisition reaches the document (snapshots " + c.DataReady + ", first total " + c.FirstTotal + ")");
                    var acc = Accepted();
                    Check(acc.Count == 1 && acc[0] == "RESET", "the session sent nothing, only the reset from outside is in the log: " + string.Join(",", acc));
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "fixr2-replace-epoch-asked":   // R2: the epoch changes while the question is open (time and counts overtook): Replace must not take it
                {
                    var c = new Counter();
                    var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Replace));
                    Hook(s, c);
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    Check(c.Asked == 1 && c.LastQuestion != null && c.LastQuestion.ReplaceAllowed && !c.LastQuestion.Counting, "a stopped board behind the document: asked, Replace offered");
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.Resume && s.ReplacedDocument, "Replace on a stopped board: -sta, the document's spectrum replaced (outcome " + o + ", plan " + s.LastPlan + ")");
                    Check(WaitFor(() => c.Reset != null, 6000), "the first poll trips on the epoch of the snapshot the question showed (" + (c.Reset ?? "no trip") + ")");
                    Check(c.Reset != null && c.Reset.Contains("boot_id"), "the reason is the changed boot_id (" + (c.Reset ?? "-") + ")");
                    Check(c.DataReady == 0, "nothing of the other acquisition reaches the document (snapshots " + c.DataReady + ")");
                    var acc = Accepted();
                    Check(acc.Count == 1 && acc[0] == "-sta", "only -sta was sent: " + string.Join(",", acc));
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "fixr2-replace-resume":   // R2, control: Replace on a stopped board, the board unchanged: -sta, no false "reset"
                {
                    var c = new Counter();
                    var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Replace));
                    Hook(s, c);
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    s.RequestChannels = 1024;   // the probe's 8192 channels are folded as the polls bring them
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.Resume && s.ReplacedDocument && s.StartedByUs, "Replace on a stopped board: resumed by -sta (outcome " + o + ", plan " + s.LastPlan + ")");
                    Check(WaitFor(() => c.DataReady >= 3 || c.Reset != null, 8000), "snapshots arrive after Replace");
                    Check(c.Reset == null && s.Guard.TripReason == null, "the resumed board is no reset from outside (" + (c.Reset ?? "no trip") + ")");
                    AtomSpectraRestBoardSample seen = c.LastQuestion != null ? c.LastQuestion.Board : null;
                    Check(c.DataReady >= 3 && seen != null && seen.Time >= 5000 && c.FirstTime >= seen.Time && c.FirstTotal >= seen.Total,
                        "the document takes the board's acquisition from where the question showed it (first time " + c.FirstTime + " s, total " + c.FirstTotal + "; shown " + (seen != null ? seen.Time + " s, " + seen.Total : "-") + ")");
                    var acc = Accepted();
                    Check(acc.Count == 1 && acc[0] == "-sta", "only -sta was sent: " + string.Join(",", acc));
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "fixr2-replace-join-epoch":   // R2, control: Replace on a counting board that reports its epoch, the epoch unchanged: no false "reset"
                {
                    var c = new Counter();
                    var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Replace));
                    Hook(s, c);
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.Join && s.ReplacedDocument, "Replace on a counting board: joined (outcome " + o + ", plan " + s.LastPlan + ")");
                    Check(WaitFor(() => c.DataReady >= 3 || c.Reset != null, 8000), "snapshots arrive after Replace");
                    Check(c.Reset == null && s.Guard.TripReason == null && s.Guard.EpochChanges == 0, "the same epoch is no reset from outside (" + (c.Reset ?? "no trip") + ")");
                    Check(Posts() == 0, "Replace on a counting board: nothing posted: " + string.Join(",", Accepted()));
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "fixr6-observe-hint":   // R6: observe only, Replace offered: the hint says nothing is sent, not "a stopped board gets -sta"
                {
                    var c = new Counter();
                    var s = NewSession(true, 0, Answer(c, AtomSpectraRestStartChoice.Cancel));
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    AtomSpectraRestStartQuestion q = c.LastQuestion;
                    Check(c.Asked == 1 && q != null && q.ReplaceAllowed && q.ObserveOnly, "observe only, the board behind the document: Replace offered, the question is marked observe only");
                    Check(o == AtomSpectraTcpBeginOutcome.Cancelled && Posts() == 0, "Cancel: nothing posted (outcome " + o + ")");
                    s.Dispose();
                    // the text: AtomSpectraRestUi is not in the source build of the suite
                    Type ui = Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor");
                    MethodInfo sqt = ui == null ? null : ui.GetMethod("StartQuestionText");
                    if (sqt == null || q == null) { Console.WriteLine("n/a: AtomSpectraRestUi is not in this build"); return true; }
                    string observeHint = ResText("AtomSpectraRestStartReplaceHintObserve");
                    string hint = ResText("AtomSpectraRestStartReplaceHint");
                    Check(!string.IsNullOrEmpty(observeHint) && !string.IsNullOrEmpty(hint), "the resources of both Replace hints are read");
                    if (string.IsNullOrEmpty(observeHint) || string.IsNullOrEmpty(hint)) return true;
                    string text = (string)sqt.Invoke(null, new object[] { q });
                    Console.WriteLine("text: " + text);
                    Check(text.Contains(observeHint) && !text.Contains(hint), "observe only: the hint of observe only, not the one that promises -sta to a stopped board");
                    var plain = new AtomSpectraRestStartQuestion { Counting = q.Counting, Board = q.Board, DocumentSeconds = q.DocumentSeconds, DocumentCounts = q.DocumentCounts, ContinueAllowed = false, ReplaceAllowed = true, ChannelBelow = q.ChannelBelow };
                    string plainText = (string)sqt.Invoke(null, new object[] { plain });
                    Check(plainText.Contains(hint) && !plainText.Contains(observeHint), "control: without observe only the usual hint");
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
