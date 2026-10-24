// PR #38, the maintainer's TCP lane: R1 Replace takes the board's spectrum of the question at once (and is offered only where it folds into
// the document), R3 an answer given in advance holds only for a counting board, T5 the same board by HostKey keeps its session, T4/O1 a Stop
// entered while the first one still runs does not stop the session again. The controller is reached by reflection (real build only).
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static object LaneResultData(int channels, int fill, double seconds)
        {
            Type rdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
            Type specT = Type.GetType("BecquerelMonitor.EnergySpectrum, BecquerelMonitor");
            object rd = Activator.CreateInstance(rdT);
            object spec = Activator.CreateInstance(specT, new object[] { 1.0, channels });
            int[] bins = (int[])specT.GetProperty("Spectrum").GetValue(spec, null);
            for (int i = 0; i < bins.Length; i++) bins[i] = fill;
            specT.GetProperty("ValidPulseCount").SetValue(spec, (long)fill * channels, null);
            specT.GetProperty("TotalPulseCount").SetValue(spec, (long)fill * channels, null);
            rdT.GetProperty("EnergySpectrum").SetValue(rd, spec, null);
            object st = rdT.GetProperty("ResultDataStatus").GetValue(rd, null);
            st.GetType().GetProperty("ElapsedTime").SetValue(st, TimeSpan.FromSeconds(seconds), null);
            st.GetType().GetProperty("TotalTime").SetValue(st, TimeSpan.FromSeconds(seconds), null);
            return rd;
        }

        static object LaneSpectrum(object rd)
        {
            return rd.GetType().GetProperty("EnergySpectrum").GetValue(rd, null);
        }

        static double LaneSeconds(object rd)
        {
            object st = rd.GetType().GetProperty("ResultDataStatus").GetValue(rd, null);
            return ((TimeSpan)st.GetType().GetProperty("ElapsedTime").GetValue(st, null)).TotalSeconds;
        }

        static bool RunTcpLane(string scenario)
        {
            BindingFlags np = BindingFlags.NonPublic | BindingFlags.Instance;
            BindingFlags ns = BindingFlags.NonPublic | BindingFlags.Static;
            switch (scenario)
            {
                case "console-tcp-lane-replace":   // R1 on the bridge mock (counting): the board behind the document, Replace
                {
                    var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
                    AtomSpectraRestStartQuestion asked = null;
                    s.Ask = q => { asked = q; return AtomSpectraRestStartChoice.Replace; };
                    s.DocumentSeconds = BehindSeconds;
                    s.DocumentCounts = BehindCounts;
                    var outcome = s.Begin(true, 2000);
                    AtomSpectraTcpDataReadyArgs seen = s.ReplacedSnapshot;
                    Check(outcome == AtomSpectraTcpBeginOutcome.Started && s.ReplacedDocument, "Replace: started, the document's spectrum replaced (outcome " + outcome + ")");
                    Check(asked != null && asked.Board != null && asked.Board.Bins != null && asked.Board.Bins.Length == 8192, "the question carries the board's channels (8192)");
                    Check(seen != null && asked != null && asked.Board != null && seen.ElapsedTime == asked.Board.Time
                        && seen.Histogram.Sum(x => (long)x) == asked.Board.Total && (long)s.ElapsedAtBeginSeconds == asked.Board.Time,
                        "the session keeps the snapshot the question showed (its time and counts)");
                    Type ctlT = CtlT;
                    if (ctlT == null)
                    {
                        Console.WriteLine("n/a: the controller is not in this build");
                        s.Dispose();
                        return true;
                    }
                    object ctl = Activator.CreateInstance(ctlT);
                    MethodInfo take = ctlT.GetMethod("TakeBoardSpectrum", np);
                    Check(take != null, "the controller has TakeBoardSpectrum");
                    if (take != null && seen != null)
                    {
                        object rd = LaneResultData(8192, 1000, BehindSeconds);   // the document: its own spectrum, far ahead of the board
                        take.Invoke(ctl, new object[] { rd, s, 8192 });
                        object spec = LaneSpectrum(rd);
                        int[] doc = (int[])spec.GetType().GetProperty("Spectrum").GetValue(spec, null);
                        long valid = (long)spec.GetType().GetProperty("ValidPulseCount").GetValue(spec, null);
                        Console.WriteLine("document after Start: " + LaneSeconds(rd) + " s, " + valid + " counts; the question: " + seen.ElapsedTime + " s, " + seen.Histogram.Sum(x => (long)x) + " counts");
                        Check(doc.SequenceEqual(seen.Histogram), "R1: the document holds the board's spectrum of the question at once, not its own");
                        Check(valid == seen.Histogram.Sum(x => (long)x) && (long)LaneSeconds(rd) == seen.ElapsedTime, "R1: with the board's time and counts (no mixture)");
                        object rdHalf = LaneResultData(4096, 1000, BehindSeconds);
                        take.Invoke(ctl, new object[] { rdHalf, s, 4096 });
                        int[] half = (int[])LaneSpectrum(rdHalf).GetType().GetProperty("Spectrum").GetValue(LaneSpectrum(rdHalf), null);
                        Check(half.Sum(x => (long)x) == seen.Histogram.Sum(x => (long)x) && half[0] == seen.Histogram[0] + seen.Histogram[1], "a document of 4096 channels: the spectrum folded as every snapshot");
                    }
                    MethodInfo fits = ctlT.GetMethod("SnapshotFits", ns);
                    Check(fits != null, "the controller has SnapshotFits");
                    if (fits != null)
                    {
                        Func<int, int, int, bool> f = (b, p, d) => (bool)fits.Invoke(null, new object[] { new int[b], p, d });
                        Check(f(8192, 8192, 8192) && f(8192, 4096, 4096) && f(4096, 4096, 4096) && f(8192, 0, 8192), "fits: equal, or folded by a whole factor to the document");
                        Check(!f(8192, 8192, 4096) && !f(8192, 3000, 3000) && !f(8192, 16384, 16384) && !f(4096, 8192, 8192), "does not fit: the profile is not the document, a fold by a broken factor, a board shorter than the document");
                    }
                    MethodInfo only = ctlT.GetMethod("ReplaceOnlyIfFolds", ns);
                    Check(only != null, "the controller has ReplaceOnlyIfFolds");
                    if (only != null)
                    {
                        AtomSpectraRestStartQuestion inner = null;
                        Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> ask = q => { inner = q; return AtomSpectraRestStartChoice.Cancel; };
                        var q4096 = new AtomSpectraRestStartQuestion { Board = new AtomSpectraRestBoardSample { HasData = true, Time = 10, Total = 100, Bins = new int[8192] }, DocumentSeconds = 100, DocumentCounts = 1000, ReplaceAllowed = true };
                        var w = (Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>)only.Invoke(null, new object[] { LaneResultData(4096, 1, 100), 8192, ask });
                        w(q4096);
                        Check(inner == q4096 && !q4096.ReplaceAllowed, "R1: the board's channels do not fold into the document's: Replace is not offered");
                        var q8192 = new AtomSpectraRestStartQuestion { Board = new AtomSpectraRestBoardSample { HasData = true, Time = 10, Total = 100, Bins = new int[8192] }, DocumentSeconds = 100, DocumentCounts = 1000, ReplaceAllowed = true };
                        var w2 = (Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>)only.Invoke(null, new object[] { LaneResultData(8192, 1, 100), 8192, ask });
                        w2(q8192);
                        Check(inner == q8192 && q8192.ReplaceAllowed, "the channels fold: Replace stays offered (control)");
                    }
                    s.Dispose();
                    return true;
                }
                case "tcplane-answer-counting":   // R3: the answer given in advance by the window after a board reset
                {
                    Type ctlT = CtlT;
                    if (ctlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return true; }
                    object rd = Activator.CreateInstance(Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor"));
                    MethodInfo given = ctlT.GetMethod("AnswerGiven", ns);
                    Check(given != null, "the controller has AnswerGiven");
                    if (given == null) return true;
                    int fallback = 0;
                    Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice> window = q => { fallback++; return AtomSpectraRestStartChoice.Cancel; };
                    Func<AtomSpectraRestStartChoice, Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>> prepared = choice =>
                    {
                        SetMember(ctlT, null, "answerFor", rd);
                        SetMember(ctlT, null, "answer", choice);
                        SetMember(ctlT, null, "answerSeconds", 100L);
                        SetMember(ctlT, null, "answerCounts", 1000L);
                        return (Func<AtomSpectraRestStartQuestion, AtomSpectraRestStartChoice>)given.Invoke(null, new object[] { rd, window });
                    };
                    var r1 = prepared(AtomSpectraRestStartChoice.Replace)(new AtomSpectraRestStartQuestion { Counting = false, Board = Sample(130, 1500), DocumentSeconds = 1000, DocumentCounts = 9000, ContinueAllowed = false, ReplaceAllowed = true });
                    Check(r1 == AtomSpectraRestStartChoice.Cancel && fallback == 1, "R3: Replace given in advance for a STOPPED board: the Start window is shown (it says what is sent), no silent -sta (" + r1 + ", asked " + fallback + ")");
                    var r2 = prepared(AtomSpectraRestStartChoice.Continue)(new AtomSpectraRestStartQuestion { Counting = false, Board = Sample(130, 1500), DocumentSeconds = 0, DocumentCounts = 0, ContinueAllowed = true, ReplaceAllowed = false });
                    Check(r2 == AtomSpectraRestStartChoice.Cancel && fallback == 2, "R3: 'open in a new document' for a stopped board: the Start window is shown (" + r2 + ", asked " + fallback + ")");
                    var r3 = prepared(AtomSpectraRestStartChoice.Replace)(new AtomSpectraRestStartQuestion { Counting = true, Board = Sample(130, 1500), DocumentSeconds = 1000, DocumentCounts = 9000, ContinueAllowed = false, ReplaceAllowed = true });
                    Check(r3 == AtomSpectraRestStartChoice.Replace && fallback == 2, "a counting board as seen: Replace holds without a question (control, " + r3 + ")");
                    var r4 = prepared(AtomSpectraRestStartChoice.Continue)(new AtomSpectraRestStartQuestion { Counting = true, Board = Sample(130, 1500), DocumentSeconds = 0, DocumentCounts = 0, ContinueAllowed = true, ReplaceAllowed = false });
                    Check(r4 == AtomSpectraRestStartChoice.Continue && fallback == 2, "a counting board: Continue in the new document holds (control, " + r4 + ")");
                    return true;
                }
                case "tcplane-host-key":   // T5: the same board in another spelling keeps its session (and what the session knows)
                {
                    Type ctlT = CtlT;
                    if (ctlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return true; }
                    MethodInfo open = ctlT.GetMethod("OpenSession", np);
                    FieldInfo fSession = ctlT.GetField("session", np);
                    Check(open != null && fSession != null, "the controller has OpenSession and the session field");
                    if (open == null || fSession == null) return true;
                    Func<string, int, object> cfg = (host, p) => new AtomSpectraTcpDeviceConfig { Host = host, TcpPort = p };
                    object ctl = Activator.CreateInstance(ctlT);
                    open.Invoke(ctl, new object[] { cfg("Board-A.local", 8234) });
                    object s1 = fSession.GetValue(ctl);
                    open.Invoke(ctl, new object[] { cfg("board-a.LOCAL", 8234) });
                    object s2 = fSession.GetValue(ctl);
                    Console.WriteLine("sessions: " + (s1 == null ? "null" : ((AtomSpectraTcpSession)s1).Host) + " / " + (s2 == null ? "null" : ((AtomSpectraTcpSession)s2).Host));
                    Check(s1 != null && ReferenceEquals(s1, s2), "T5: the same host in another case: the session is kept, not made again at every Start");
                    open.Invoke(ctl, new object[] { cfg("board-b.local", 8234) });
                    object s3 = fSession.GetValue(ctl);
                    Check(s3 != null && !ReferenceEquals(s1, s3), "another host: a new session (control)");
                    open.Invoke(ctl, new object[] { cfg("board-b.local", 8235) });
                    object s4 = fSession.GetValue(ctl);
                    Check(s4 != null && !ReferenceEquals(s3, s4), "another port: a new session (control)");
                    if (s4 != null) ((IDisposable)s4).Dispose();
                    return true;
                }
                case "tcplane-stop-nested":   // T4/O1: a Stop entered while the first one still runs (its question open)
                {
                    Type ctlT = CtlT;
                    if (ctlT == null) { Console.WriteLine("n/a: the controller is not in this build"); return true; }
                    Type rdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
                    object rd = Activator.CreateInstance(rdT);
                    var s = new AtomSpectraTcpSession("127.0.0.1", 9, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", 9, 2000));
                    object ctl = Activator.CreateInstance(ctlT);
                    SetMember(ctlT, ctl, "session", s);
                    SetMember(ctlT, ctl, "deviceGuid", "pr38-lane-stop-nested");
                    FieldInfo stopping = ctlT.GetField("stopping", np);
                    PropertyInfo inProgress = ctlT.GetProperty("StopInProgress");
                    MethodInfo stop2 = ctlT.GetMethod("StopMeasurement", new Type[] { rdT, typeof(bool) });
                    Check(stopping != null && inProgress != null && stop2 != null, "the controller has the stopping flag, StopInProgress and StopMeasurement(ResultData, bool)");
                    if (stopping == null || inProgress == null || stop2 == null) { s.Dispose(); return true; }
                    DateTime end0 = new DateTime(2026, 1, 2, 3, 4, 5);
                    rdT.GetProperty("EndTime").SetValue(rd, end0, null);
                    Func<bool> first = () => false;   // the question of the first Stop
                    s.AskStopJoined = first;
                    stopping.SetValue(ctl, true);
                    Check((bool)inProgress.GetValue(ctl, null), "while the first Stop runs the controller says so (StopInProgress)");
                    stop2.Invoke(ctl, new object[] { rd, false });   // the preset / the timer meanwhile
                    Check(ReferenceEquals(s.AskStopJoined, first), "T4: the second Stop leaves the question of the first one as it is (AskStopJoined)");
                    Check((DateTime)rdT.GetProperty("EndTime").GetValue(rd, null) == end0, "O1: the second Stop does not move the end of the run");
                    stopping.SetValue(ctl, false);
                    stop2.Invoke(ctl, new object[] { rd, false });
                    Check(s.AskStopJoined == null && !(bool)inProgress.GetValue(ctl, null), "a Stop of its own (control): the non-manual Stop asks nothing");
                    Check((DateTime)rdT.GetProperty("EndTime").GetValue(rd, null) != end0, "a Stop of its own sets the end of the run (control)");
                    s.Dispose();
                    return true;
                }
                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}
