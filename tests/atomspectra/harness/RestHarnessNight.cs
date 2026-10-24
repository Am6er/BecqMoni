// findings 1, 3, 4, 7, 8, 9 of the pass over #BM-1/#BM-7/#BM-9, #BM-4, #BM-5, #BM-13/#BM-6, the Start text
// beside Replace, and the diagnosis of #BM-3 (what TCP Clear sends). New members of the program are reached by reflection, so that
// this file builds on the code before the fixes as well and the scenarios are RED there by their checks, not by the build.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        // Reads a resource string from the BecquerelMonitor assembly by name.
        static string ResText(string name)
        {
            Type t = Type.GetType("BecquerelMonitor.Properties.Resources, BecquerelMonitor");
            if (t == null) return null;
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return p == null ? null : (string)p.GetValue(null, null);
        }

        // Retrieves a field, property, or method from a type by name.
        static object Member(Type t, string name, bool isStatic)
        {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            if (t == null) return null;
            FieldInfo fi = t.GetField(name, f);
            if (fi != null) return fi;
            PropertyInfo pi = t.GetProperty(name, f);
            if (pi != null) return pi;
            MethodInfo mi = t.GetMethod(name, f);
            return mi;
        }

        // Sets a field or property value on a target object via reflection.
        static void SetMember(Type t, object target, string name, object value)
        {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            if (t == null) return;
            FieldInfo fi = t.GetField(name, f);
            if (fi != null)
            {
                fi.SetValue(target, value);
                return;
            }
            PropertyInfo pi = t.GetProperty(name, f);
            if (pi != null)
            {
                pi.SetValue(target, value, null);
            }
        }

        // Creates a board sample with the given time and total counts.
        static AtomSpectraRestBoardSample Sample(long time, long total)
        {
            return new AtomSpectraRestBoardSample { HasData = true, Time = time, Total = total };
        }

        // Runs a specific night scenario by name.
        static bool RunNight(string scenario)
        {
            switch (scenario)
            {
                case "night-replace-empty":
                {
                    Check(!AtomSpectraRestPolicy.ReplaceAllowed(Sample(0, 0), 100, 1000, false), "an empty board (0 s, 0 counts, the TCP sample of an idle analyzer): Replace is not offered");
                    Check(AtomSpectraRestPolicy.ReplaceAllowed(Sample(10, 100), 100, 1000, false), "a board with a spectrum behind the document: Replace is offered (control)");
                    return true;
                }
                case "night-start-text":
                {
                    // by reflection: AtomSpectraRestUi is not in the source build of the suite
                    MethodInfo sqt = Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor") == null ? null : Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor").GetMethod("StartQuestionText");
                    if (sqt == null) { Console.WriteLine("n/a: AtomSpectraRestUi is not in this build"); return true; }
                    Func<AtomSpectraRestStartQuestion, string> startText = q => (string)sqt.Invoke(null, new object[] { q });
                    string behind = ResText("AtomSpectraRestStartBehind");
                    string other = ResText("AtomSpectraRestStartOtherSpectrum");
                    Check(behind != null && other != null && behind.Contains(";") && other.Contains(";"), "the resources of the Start text are read");
                    if (behind != null && other != null && behind.Contains(";") && other.Contains(";"))
                    {
                        string adviceBehind = behind.Substring(behind.LastIndexOf(';') + 1).Trim();
                        string adviceOther = other.Substring(other.LastIndexOf(';') + 1).Trim();
                        var q1 = new AtomSpectraRestStartQuestion { Counting = true, Board = Sample(10, 100), DocumentSeconds = 100, DocumentCounts = 1000, ContinueAllowed = false, ReplaceAllowed = true, ChannelBelow = -1 };
                        Check(!startText(q1).Contains(adviceBehind), "Replace shown, the board behind: no advice to create a new document");
                        q1.ReplaceAllowed = false;
                        Check(startText(q1).Contains(adviceBehind), "Replace not shown: the advice stays (control)");
                        var q2 = new AtomSpectraRestStartQuestion { Counting = true, Board = Sample(5000, 500000), DocumentSeconds = 100, DocumentCounts = 1000, ContinueAllowed = false, ReplaceAllowed = true, ChannelBelow = 5 };
                        Check(!startText(q2).Contains(adviceOther), "Replace shown, another spectrum (channel below): no advice to create a new document");
                        Console.WriteLine("text: " + startText(q2));
                    }
                    return true;
                }
                case "night-observe-replace":
                {
                    var c = new Counter();
                    var s = NewSession(true, 0, Answer(c, AtomSpectraRestStartChoice.Replace));
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var o = s.Begin(true, 1000);
                    Check(c.Asked == 1 && c.LastQuestion != null && !c.LastQuestion.ContinueAllowed && c.LastQuestion.ReplaceAllowed, "observe only, the board behind the document: asked, Continue refused, Replace offered");
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning && s.ReplacedDocument, "observe only + Replace: started, joined, the document's spectrum replaced (outcome " + o + ")");
                    Thread.Sleep(1500);
                    Check(Posts() == 0, "observe only + Replace: nothing posted to the board: " + string.Join(",", Accepted()));
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "night-stop-joined-ask":
                case "night-stop-joined-keep":
                {
                    bool stop = scenario.EndsWith("ask");
                    var c = new Counter();
                    var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "a counting board, Continue: joined (outcome " + o + ")");
                    int kept = 0, asked = 0;
                    s.BoardKeepsCounting += (x, e) => kept++;
                    PropertyInfo p = typeof(AtomSpectraRestSession).GetProperty("AskStopJoined");
                    Check(p != null, "the REST session has the Stop question of a joined acquisition (AskStopJoined)");
                    if (p != null) p.SetValue(s, new Func<bool>(() => { asked++; return stop; }), null);
                    Thread.Sleep(1000);
                    var so = s.Stop(1000);
                    Thread.Sleep(300);
                    if (stop)
                    {
                        Check(asked == 1 && so == AtomSpectraTcpStopOutcome.Stopped && Accepted().Contains("-sto"), "Stop of a joined acquisition, the user stops it on the board: -sto sent (asked " + asked + ", outcome " + so + ", posts " + string.Join(",", Accepted()) + ")");
                    }
                    else
                    {
                        Check(asked == 1 && so == AtomSpectraTcpStopOutcome.Detached && Posts() == 0 && kept == 0, "Stop of a joined acquisition, the user lets it count: nothing sent, no second message (asked " + asked + ", outcome " + so + ", posts " + Posts() + ", kept " + kept + ")");
                    }
                    s.Dispose();
                    return true;
                }
                case "night-status-401":
                {
                    if (CtlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return true; }
                    F5dWith(() =>
                    {
                        AtomSpectraRestAcquisition.MaxAttempts = 1;
                        var c = new Counter();
                        var s = NewSession(true, 0, null);
                        Hook(s, c);
                        Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                        Check(WaitFor(() => c.LinkLost >= 1, 40000), "the board refuses access: the link-lost event comes");
                        Type ctlT = CtlT;
                        Check(ctlT != null, "the controller is in this build");
                        if (ctlT != null)
                        {
                            object ctl = Activator.CreateInstance(ctlT);
                            SetMember(ctlT, ctl, "session", s);
                            MethodInfo m = ctlT.GetMethod("Session_LinkLost", BindingFlags.NonPublic | BindingFlags.Instance);
                            Check(m != null, "the controller has Session_LinkLost");
                            if (m != null) m.Invoke(ctl, new object[] { s, EventArgs.Empty });
                        }
                        string st = AtomSpectraTcpLog.StatusText ?? "";
                        string lost = ResText("AtomSpectraTcpStatusLinkLost") ?? "link lost";
                        Console.WriteLine("status: " + st);
                        Check(st.Contains("401") && !st.Contains(lost), "the status line names the refusal of access (HTTP 401), not a lost link");
                        s.Detach();
                        s.Dispose();
                    });
                    return true;
                }
                case "night-answer-stale":
                {
                    Type ctlT = CtlT;
                    if (ctlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return true; }
                    if (ctlT == null) return true;
                    object rd = Activator.CreateInstance(Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor"));
                    MethodInfo given = ctlT.GetMethod("AnswerGiven", BindingFlags.NonPublic | BindingFlags.Static);
                    Check(given != null, "the controller has AnswerGiven");
                    if (given == null) return true;
                    int fallback = 0;
                    Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> ask = q => { fallback++; return AtomSpectraRestStartChoice.Cancel; };
                    Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> Prepared()
                    {
                        SetMember(ctlT, null, "answerFor", rd);
                        SetMember(ctlT, null, "answer", AtomSpectraRestStartChoice.Replace);
                        SetMember(ctlT, null, "answerSeconds", 100L);
                        SetMember(ctlT, null, "answerCounts", 1000L);
                        object f = given.GetParameters().Length == 2 ? given.Invoke(null, new object[] { rd, ask }) : given.Invoke(null, new object[] { rd });
                        return (Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>)f;
                    }
                    var r1 = Prepared()(new AtomSpectraRestStartQuestion { Board = Sample(500, 5000), DocumentSeconds = 100, DocumentCounts = 1000, ContinueAllowed = true, ReplaceAllowed = false });
                    Check(r1 != AtomSpectraRestStartChoice.Replace && fallback == 1, "the board overtook the document since the answer: Replace given in advance is not used, the question is asked again (" + r1 + ", asked " + fallback + ")");
                    var r2 = Prepared()(new AtomSpectraRestStartQuestion { Board = Sample(50, 400), DocumentSeconds = 1000, DocumentCounts = 9000, ContinueAllowed = false, ReplaceAllowed = true });
                    Check(r2 != AtomSpectraRestStartChoice.Replace && fallback == 2, "the board was reset again since the answer (50 s < 100 s seen): asked again (" + r2 + ", asked " + fallback + ")");
                    var r3 = Prepared()(new AtomSpectraRestStartQuestion { Board = Sample(130, 1500), DocumentSeconds = 1000, DocumentCounts = 9000, ContinueAllowed = false, ReplaceAllowed = true });
                    Check(r3 == AtomSpectraRestStartChoice.Replace && fallback == 2, "the board as seen, grown: Replace holds without a question (control, " + r3 + ")");
                    return true;
                }
                case "night-reset-offer":
                {
                    Type ctlT = CtlT;
                    if (ctlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return true; }
                    if (ctlT == null) return true;
                    var sw = new StringWriter();
                    var tl = new TextWriterTraceListener(sw);
                    Trace.Listeners.Add(tl);
                    MethodInfo off = ctlT.GetMethod("OfferBoardAcquisition", BindingFlags.NonPublic | BindingFlags.Static);
                    Check(off != null, "the controller has OfferBoardAcquisition");
                    if (off != null)
                    {
                        object[] args = off.GetParameters().Length == 6 ? new object[] { "TEXT-OF-RESET", "night", null, 10L, 100L, true } : new object[] { "TEXT-OF-RESET", "night", null, 10L, 100L };
                        off.Invoke(null, args);
                    }
                    Trace.Flush();
                    string shown = sw.ToString();
                    Trace.Listeners.Remove(tl);
                    string advice = ResText("MSGAtomSpectraRestBoardResetNewDocument") ?? "?";
                    Console.WriteLine("shown: " + shown.Trim());
                    Check(shown.Contains("TEXT-OF-RESET") && shown.Contains(advice), "the reset message shown without its window says what to do next (a new document)");
                    MethodInfo pred = ctlT.GetMethod("OfferAfterReset", BindingFlags.NonPublic | BindingFlags.Static);
                    Check(pred != null, "the controller decides the window by OfferAfterReset");
                    if (pred != null)
                    {
                        Check((bool)pred.Invoke(null, new object[] { true, 10L, 100L }), "a running recording, the board with a spectrum: the window is offered");
                        Check(!(bool)pred.Invoke(null, new object[] { true, 0L, 0L }), "#BM-4: an empty board: no window, the message");
                        Check(!(bool)pred.Invoke(null, new object[] { false, 10L, 100L }), "finding 8: the recording was stopped by the user first: no window");
                    }
                    return true;
                }
                case "console-tcp-night-resume":
                {
                    var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
                    int asked = 0;
                    s.Ask = q => { asked++; return AtomSpectraRestStartChoice.Cancel; };
                    s.DocumentSeconds = 100;
                    s.DocumentCounts = 1000;
                    var o = s.Begin(false, 2000);
                    Thread.Sleep(500);
                    var cmds = Requests().Select(r => Str(r, "cmd")).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    Console.WriteLine("outcome " + o + ", commands: " + string.Join(" ", cmds));
                    Check(asked == 0, "the same document after its own Stop, the board not behind it: no question (pause - continue)");
                    Check(o == AtomSpectraTcpBeginOutcome.Started && cmds.Contains("-sta") && !cmds.Contains("-rst") && !cmds.Contains("-sto") && !s.JoinedRunning, "the same document goes on: -sta only, as before (outcome " + o + ")");
                    s.Dispose();
                    return true;
                }
                case "console-tcp-night-stop-ask":
                {
                    var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
                    s.Ask = q => AtomSpectraRestStartChoice.Continue;
                    var o = s.Begin(true, 2000);
                    Check(o == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "a counting board, Continue: joined (outcome " + o + ")");
                    int asked = 0;
                    PropertyInfo p = typeof(AtomSpectraTcpSession).GetProperty("AskStopJoined");
                    Check(p != null, "the TCP session has the Stop question of a joined acquisition (AskStopJoined)");
                    if (p != null) p.SetValue(s, new Func<bool>(() => { asked++; return true; }), null);
                    Thread.Sleep(500);
                    var so = s.Stop(1000);
                    Thread.Sleep(300);
                    var cmds = Requests().Select(r => Str(r, "cmd")).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    Check(asked == 1 && cmds.Contains("-sto") && (so == AtomSpectraTcpStopOutcome.Stopped || so == AtomSpectraTcpStopOutcome.StoppedAfterReconnect), "Stop of a joined acquisition, the user stops it on the board: -sto sent (asked " + asked + ", outcome " + so + ", commands " + string.Join(" ", cmds) + ")");
                    s.Dispose();
                    return true;
                }
                case "console-tcp-night-clear":
                {
                    var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
                    s.Ask = q => AtomSpectraRestStartChoice.Cancel;
                    var o = s.Begin(true, 2000);
                    Thread.Sleep(500);
                    Func<int> rst = () => Requests().Count(r => Str(r, "cmd").Trim() == "-rst");
                    s.Clear(1000);
                    Thread.Sleep(400);
                    int afterRunning = rst();
                    Console.WriteLine("Start " + o + "; Clear of a running session: -rst sent " + afterRunning + " time(s)");
                    Check(afterRunning == 1, "Clear of a running session sends -rst at once");
                    s.Stop(1000);
                    Thread.Sleep(300);
                    s.Clear(1000);
                    Thread.Sleep(400);
                    int afterStop = rst();
                    Console.WriteLine("Clear after Stop: -rst sent now " + (afterStop - afterRunning) + " time(s), reset waits for the next Start: " + s.ResetOnNextStart);
                    Check(afterStop == afterRunning && s.ResetOnNextStart, "Clear after Stop sends nothing now: the reset waits for the next Start");
                    byte[] frame = AtomSpectraShproto.EncodeText("-rst");
                    Console.WriteLine("frame of -rst: " + BitConverter.ToString(frame));
                    s.Dispose();
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
