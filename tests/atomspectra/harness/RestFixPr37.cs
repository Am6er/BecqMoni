// PR #37 fixes of the REST lane (rest_suite.py: the "pr37-rest-*" scenarios):
// the board's own limit ends a run as a preset end, not a stall; "Continue" is refused over another acquisition (a channel below the
// document); observe only refuses a board behind the document at Start; profile channels the board cannot fill; the JSON parser (depth,
// no copies, NaN in time/lost); Disconnect does not wait for a hung poll; a joined Stop tells the user the board counts on; the user
// texts come from the resources while the log stays English; numbers are invariant; a POST does not ask for "100 Continue".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using Res = BecquerelMonitor.Properties.Resources;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunRestFixPr37(string scenario)
        {
            Action body;
            switch (scenario)
            {
                case "pr37-rest-limit-end": body = Pr37LimitEnd; break;
                case "pr37-rest-limit-stall": body = Pr37LimitStall; break;
                case "pr37-rest-continue-other": body = Pr37ContinueOther; break;
                case "pr37-rest-observe-behind": body = Pr37ObserveBehind; break;
                case "pr37-rest-channels": body = Pr37Channels; break;
                case "pr37-rest-json": body = Pr37Json; break;
                case "pr37-rest-detach-hang": body = Pr37DetachHang; break;
                case "pr37-rest-stop-joined": body = Pr37StopJoined; break;
                case "pr37-rest-texts": body = Pr37Texts; break;
                case "pr37-rest-expect": body = Pr37Expect; break;
                default: return false;
            }
            try { body(); }
            catch (Exception ex) { Check(false, "exception " + ex); }
            return true;
        }

        static Type Pr37Type(string name)
        {
            return Type.GetType("BecquerelMonitor." + name + ", BecquerelMonitor");   // null in the source build (no controller, no forms)
        }

        static string Fmt(string format, params object[] args) { return string.Format(CultureInfo.InvariantCulture, format, args); }

        // a resource only the real build's controller or forms use: read by name (the source build's stub has only those of its sources)
        static string Pr37Res(string key)
        {
            Type t = typeof(Res);
            PropertyInfo p = t.GetProperty(key, BindingFlags.Public | BindingFlags.Static);
            if (p != null) return (string)p.GetValue(null, null);
            FieldInfo f = t.GetField(key, BindingFlags.Public | BindingFlags.Static);
            return f == null ? null : (string)f.GetValue(null);
        }

        // R1. A run we started with the board's own limit (-sta N; the mock stops counting after N s): the document's preset is board + N,
        // the snapshots bring the document to it (the controller ends the run there: save, sound), and no stall is declared after the stop.
        static void Pr37LimitEnd()
        {
            long oldStall = AtomSpectraRestSession.StallOverrideMs;
            AtomSpectraRestSession.StallOverrideMs = 3000;
            try
            {
                var c = new Counter();
                var s = NewSession(false, 5, null);   // headless, an empty board: a new run (-sta 5)
                Hook(s, c);
                long maxTime = -1;
                s.DataReady += (o, e) => { lock (c) maxTime = Math.Max(maxTime, e.ElapsedTime); };
                var outcome = s.Begin(true, 1000);
                Check(outcome == AtomSpectraTcpBeginOutcome.Started && s.LastPlan == AtomSpectraRestStartPlan.StartNew, "a new run is started (" + outcome + ", " + s.LastPlan + ")");
                Check(string.Join(",", Accepted()) == "-sta 5", "-sta 5 is sent (" + string.Join(",", Accepted()) + ")");
                int preset = s.PresetAfterStart(3600);
                Console.WriteLine("preset after Start: " + preset.ToString(CultureInfo.InvariantCulture) + " s");
                Check(preset == 5, "the document's preset is the board's own end: board time 0 + limit 5 s, no margin (" + preset + ")");
                Check(WaitFor(() => { lock (c) return maxTime >= preset; }, 15000), "the document's time reaches the preset (the controller ends the run there as a preset end)");
                Thread.Sleep(7000);   // more than twice the stall time after the board stopped by itself
                long seen; lock (c) seen = maxTime;
                Check(seen == 5, "the board stopped at its own limit (time " + seen + ")");
                Check(c.Stalled == 0, "no stall is declared at the board's own limit (stalls " + c.Stalled + ")");
                Check(s.Acquisition != null, "the polling goes on until the preset end stops the run");
                Check(AtomSpectraTcpLog.Snapshot().Any(l => l.Text.Contains("reached the limit of the run")), "the log says the board reached the limit of the run");
                s.Stop(1000);
                s.Dispose();
            }
            finally { AtomSpectraRestSession.StallOverrideMs = oldStall; }
        }

        // R1, the control: a board that stops BEFORE the limit of our run is still a stall.
        static void Pr37LimitStall()
        {
            long oldStall = AtomSpectraRestSession.StallOverrideMs;
            AtomSpectraRestSession.StallOverrideMs = 3000;
            try
            {
                var c = new Counter();
                var s = NewSession(false, 30, null);
                Hook(s, c);
                var outcome = s.Begin(true, 1000);
                Check(outcome == AtomSpectraTcpBeginOutcome.Started && string.Join(",", Accepted()) == "-sta 30", "a new run with the limit 30 s (" + outcome + ")");
                Check(s.PresetAfterStart(3600) == 30, "the preset is 30 s (" + s.PresetAfterStart(3600) + ")");
                Check(WaitFor(() => c.Stalled >= 1, 20000), "the board stopped at 8 s, before its limit: the stall is declared as before");
                s.Dispose();
            }
            finally { AtomSpectraRestSession.StallOverrideMs = oldStall; }
        }

        static int[] Pr37Doc(int channels, int channel, int count)
        {
            int[] d = new int[channels];
            if (channel >= 0) d[channel] = count;
            return d;
        }

        // R2. The document holds an acquisition the board does not (a channel of the document above the board): "Continue" is not offered,
        // the reason names the channel, nothing is sent; headless refuses. The guard seeded with the document's channels trips on such a
        // snapshot (the board's 8192 folded to the document's channels). A document below the board everywhere still joins.
        static void Pr37ContinueOther()
        {
            AtomSpectraRestSession.ProbeGapMs = 3200;
            // (a) with a window: asked, Continue disabled
            var c = new Counter();
            var s = NewSession(false, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
            s.DocumentSeconds = 100;
            s.DocumentCounts = 1000;
            s.DocumentBins = Pr37Doc(1024, 100, 1000000);   // the board (counting, time 1000, ~37000 counts) is ahead in time and counts
            var o = s.Begin(true, 1000);
            var q = c.LastQuestion;
            Console.WriteLine("with a window: " + o + ", asked " + c.Asked + ", continue allowed " + (q != null && q.ContinueAllowed) + ", channel below " + (q == null ? -2 : q.ChannelBelow));
            Check(c.Asked == 1 && q != null && !q.ContinueAllowed, "the board ahead in time and counts but below the document in a channel: Continue is not offered");
            Check(q != null && q.ChannelBelow == 100, "the question names channel 100");
            Check(o == AtomSpectraTcpBeginOutcome.Cancelled && s.Acquisition == null, "Continue pressed anyway: nothing is started (" + o + ")");
            Check(Posts() == 0, "nothing is sent to the board");
            Type ui = Pr37Type("AtomSpectraRestUi");
            if (ui != null && q != null)
            {
                string text = (string)ui.GetMethod("StartQuestionText", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { q });
                Console.WriteLine("question: " + text.Replace(Environment.NewLine, " / "));
                Check(text.Contains(Fmt(Pr37Res(q.ReplaceAllowed ? "AtomSpectraRestStartOtherSpectrumReplace" : "AtomSpectraRestStartOtherSpectrum"), 100)), "the question says the board holds another acquisition, channel 100");
                Check(!text.Contains(Pr37Res("AtomSpectraRestStartBehind")), "not the 'board was reset' text (the board is ahead in time and counts)");
            }
            s.Dispose();

            // (b) without a window: refused, nothing sent
            var s2 = NewSession(false, 0, null);
            s2.DocumentSeconds = 100;
            s2.DocumentCounts = 1000;
            s2.DocumentBins = Pr37Doc(1024, 100, 1000000);
            var o2 = s2.Begin(true, 1000);
            Check(o2 == AtomSpectraTcpBeginOutcome.Refused && s2.Failure == AtomSpectraRestFailure.HeadlessBehind, "headless: refused as a board behind the document (" + o2 + ", " + s2.Failure + ")");
            Check(Posts() == 0, "headless: nothing is sent");
            s2.Dispose();

            // (c) the control: the document below the board in every channel: Continue joins and the data comes
            var c3 = new Counter();
            var s3 = NewSession(false, 0, Answer(c3, AtomSpectraRestStartChoice.Continue));
            Hook(s3, c3);
            s3.DocumentSeconds = 100;
            s3.DocumentCounts = 1000;
            s3.DocumentBins = Pr37Doc(1024, -1, 0);
            var o3 = s3.Begin(true, 1000);
            Check(o3 == AtomSpectraTcpBeginOutcome.Started && c3.LastQuestion != null && c3.LastQuestion.ContinueAllowed && s3.LastPlan == AtomSpectraRestStartPlan.Join, "the control: Continue offered, joined (" + o3 + ")");
            Check(WaitFor(() => c3.DataReady >= 2, 8000) && c3.Reset == null, "the control: the data comes, no reset");
            s3.Dispose();

            // (d) the guard seeded with the document's channels
            int[] docBins = Pr37Doc(1024, 7, 50);
            int[] bins = new int[8192];
            bins[56] = 10;   // folded channel 7 = 8192 channels 56..63: 10 < 50
            var below = new AtomSpectraRestBoardSample { HasData = true, Time = 200, Total = 5000, HistOk = 5, AtMs = 1000, Bins = bins };
            var g = new AtomSpectraRestGuard();
            g.Begin(100, 1000, true, docBins);
            var u = g.Check(below);
            Console.WriteLine("seeded guard: " + u + ", " + g.TripReason + " | " + g.TripText);
            Check(u == AtomSpectraRestUpdate.Decreased && g.TripReason == "spectrum channel 7 went down (50 -> 10): another acquisition replaced ours", "a snapshot below the document's channel 7 (folded 8192 -> 1024) is another acquisition");
            Check(g.TripText == Fmt(Res.AtomSpectraRestTripChannel, 7, 50, 10), "the user's reason comes from the resources");
            var g2 = new AtomSpectraRestGuard();
            g2.Begin(100, 1000, true);
            Check(g2.Check(below) == AtomSpectraRestUpdate.Apply, "the control: without the document's channels the same snapshot is taken");
            bins[57] = 45;   // channel 7 = 55 >= 50
            var g3 = new AtomSpectraRestGuard();
            g3.Begin(100, 1000, true, docBins);
            Check(g3.Check(below) == AtomSpectraRestUpdate.Apply, "a board at or above the document in every channel is taken");

            // ChannelBelow itself
            Check(AtomSpectraRestPolicy.ChannelBelow(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }) == -1, "equal spectra: no channel below");
            Check(AtomSpectraRestPolicy.ChannelBelow(new[] { 1, 5, 3 }, new[] { 1, 2, 3 }) == 1, "channel 1 below");
            Check(AtomSpectraRestPolicy.ChannelBelow(new[] { 3, 9 }, new[] { 1, 1, 4, 5 }) == 0, "a longer board spectrum is folded: (2, 9) vs (3, 9)");
            Check(AtomSpectraRestPolicy.ChannelBelow(new[] { 1, 1, 1, 1 }, new[] { 0, 0 }) == -1 && AtomSpectraRestPolicy.ChannelBelow(new[] { 5, 5, 5 }, new[] { 0, 0, 0, 0 }) == -1, "spectra that do not fold into each other are not compared");
            Check(AtomSpectraRestPolicy.PlanStart(false, false, true, new AtomSpectraRestBoardSample { HasData = true, Time = 1000, Total = 37000 }, 100, 1000, true) == AtomSpectraRestStartPlan.RefuseHeadless, "PlanStart: a channel below the document is 'behind' for a run without windows");
        }

        // R9. Observe only and a board behind the document (time, counts or a channel): refused at Start with its own text, nothing sent;
        // the control (the document below the board) observes as before.
        static void Pr37ObserveBehind()
        {
            AtomSpectraRestSession.ProbeGapMs = 3200;
            var c = new Counter();
            var s = NewSession(true, 0, Answer(c, AtomSpectraRestStartChoice.Continue));
            Hook(s, c);
            s.DocumentSeconds = 5000;   // the board has 1000 s
            s.DocumentCounts = 1000;
            var o = s.Begin(true, 1000);
            // with a window the board behind the document is asked about (Replace is offered; nothing is ever sent): Continue pressed anyway cancels
            Check(o == AtomSpectraTcpBeginOutcome.Cancelled && c.Asked == 1 && s.Acquisition == null && c.Reset == null, "observe only with a window, the board behind the document in time: asked, Continue pressed anyway cancels (" + o + ")");
            s.Dispose();

            // without a window nobody can choose Replace: refused at Start
            var c1 = new Counter();
            s = NewSession(true, 0, null);
            Hook(s, c1);
            s.DocumentSeconds = 5000;
            s.DocumentCounts = 1000;
            o = s.Begin(true, 1000);
            Check(o == AtomSpectraTcpBeginOutcome.Refused && s.Failure == AtomSpectraRestFailure.ObserveBehind && s.LastPlan == AtomSpectraRestStartPlan.RefuseObserve, "observe only without a window, the board behind the document in time: refused at Start (" + o + ", " + s.Failure + ")");
            Check(c1.Asked == 0 && s.Acquisition == null && c1.Reset == null, "no question, no polling, no 'board was reset' message later");

            var s2 = NewSession(true, 0, null);
            s2.DocumentSeconds = 100;
            s2.DocumentCounts = 1000;
            s2.DocumentBins = Pr37Doc(1024, 100, 1000000);
            var o2 = s2.Begin(true, 1000);
            Check(o2 == AtomSpectraTcpBeginOutcome.Refused && s2.Failure == AtomSpectraRestFailure.ObserveBehind, "observe only, a channel of the board below the document: refused (" + o2 + ")");
            s2.Dispose();
            Check(Posts() == 0, "observe only: nothing is ever sent");

            var c3 = new Counter();
            var s3 = NewSession(true, 0, null);
            Hook(s3, c3);
            s3.DocumentSeconds = 100;
            s3.DocumentCounts = 1000;
            s3.DocumentBins = Pr37Doc(1024, -1, 0);
            Check(s3.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started && s3.LastPlan == AtomSpectraRestStartPlan.Join, "the control: a document below the board is observed");
            Check(WaitFor(() => c3.DataReady >= 2, 8000) && c3.Reset == null, "the control: the data comes, no reset");
            s3.Dispose();

            Type ctl = Pr37Type("AtomSpectraTcpDeviceController");
            if (ctl != null)
            {
                MethodInfo m = ctl.GetMethod("RestFailureText", BindingFlags.NonPublic | BindingFlags.Static);
                string text = (string)m.Invoke(null, new object[] { s, AtomSpectraTcpBeginOutcome.Refused });
                Console.WriteLine("text: " + text);
                Check(text == Pr37Res("ERRAtomSpectraRestObserveBehind"), "the refusal has its own text (resources)");
            }
            s.Dispose();
        }

        // R3. Profile channels: only a whole fold of the board's 8192 is filled; the controller refuses any other N before any request.
        static void Pr37Channels()
        {
            foreach (int n in new[] { 8192, 4096, 2048, 1024, 512, 256 })
                Check(AtomSpectraRestSpectrum.ChannelsSupported(n), n + " channels are supported");
            foreach (int n in new[] { 16384, 6000, 3000, 8193, 0, -1 })
                Check(!AtomSpectraRestSpectrum.ChannelsSupported(n), n + " channels are not supported");
            Type ctl = Pr37Type("AtomSpectraTcpDeviceController");
            if (ctl == null) { Console.WriteLine("the controller is not in this build: its refusal is checked on the real build"); return; }
            MethodInfo m = ctl.GetMethod("RestChannelsRefusal", BindingFlags.NonPublic | BindingFlags.Static);
            Check(m != null, "the controller has RestChannelsRefusal");
            if (m == null) return;
            Func<int, string> refusal = n => (string)m.Invoke(null, new object[] { n });
            Check(refusal(16384) == Fmt(Pr37Res("ERRAtomSpectraRestChannels"), 16384), "16384: refused with the resource text");
            Check(refusal(6000) != null && refusal(3000) != null, "6000 and 3000: refused");
            Check(refusal(4096) == null && refusal(512) == null && refusal(0) == null, "4096, 512 and an unknown profile (0): no refusal");
        }

        static string Pr37Spectrum(string extra)
        {
            return "{\"bins\":[" + string.Join(",", Enumerable.Repeat("0", 8192)) + "]" + extra + "}";
        }

        // R4 and R9. The JSON parser: nesting deeper than 64 is a FormatException (not a StackOverflowException that ends the process); a long
        // answer parses in linear time (no copy of the rest of the text per value); bare tokens still work; NaN in "time"/"lost" is refused.
        static void Pr37Json()
        {
            Func<string, bool> fails = text => { try { AtomSpectraJson.Parse(text); return false; } catch (FormatException) { return true; } };
            Check(fails(new string('[', 20000) + new string(']', 20000)), "20000 nested arrays: a FormatException, the process lives");
            Check(fails(string.Concat(Enumerable.Repeat("{\"a\":", 5000)) + "1" + new string('}', 5000)), "5000 nested objects: a FormatException");
            Check(!fails(new string('[', AtomSpectraJson.MaxDepth) + new string(']', AtomSpectraJson.MaxDepth)), "64 nested arrays parse");
            Check(fails(new string('[', AtomSpectraJson.MaxDepth + 1) + new string(']', AtomSpectraJson.MaxDepth + 1)), "65 nested arrays are refused");
            Check(!fails("[[1],[2],[[3]],{\"a\":[4]}]"), "siblings do not add up as depth");

            string big = "[" + string.Join(",", Enumerable.Repeat("0", 200000)) + "]";
            var sw = Stopwatch.StartNew();
            var list = AtomSpectraJson.Parse(big) as List<object>;
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine("200000 values parsed in " + ms.ToString(CultureInfo.InvariantCulture) + " ms");
            Check(list != null && list.Count == 200000, "a long array parses");
            Check(ms < 3000, "in linear time (" + ms + " ms; a copy of the rest per value took tens of seconds)");

            var bare = AtomSpectraJson.Parse("[nan,NaN,inf,-inf,Infinity,-Infinity,null,true,false,-1.5e3]") as List<object>;
            Check(bare != null && double.IsNaN((double)bare[0]) && double.IsNaN((double)bare[1]) && double.IsPositiveInfinity((double)bare[2]) && double.IsNegativeInfinity((double)bare[3])
                && double.IsPositiveInfinity((double)bare[4]) && double.IsNegativeInfinity((double)bare[5]) && bare[6] == null && (bool)bare[7] && !(bool)bare[8] && (double)bare[9] == -1500,
                "bare tokens and the plain values parse as before");
            Check(fails("[nanx]") && fails("[nul]"), "a token that only starts like one is refused");

            string err, user;
            var ok = AtomSpectraRestSpectrum.Parse(Pr37Spectrum(",\"time\":10,\"lost\":3,\"cps\":nan"), 0, out err, out user);
            Check(ok != null && ok.ElapsedTime == 10 && ok.InvalidPulses == 3 && ok.Cps == 0, "a NaN in cps (shown only) does not lose the snapshot (" + err + ")");
            var t = AtomSpectraRestSpectrum.Parse(Pr37Spectrum(",\"time\":nan,\"lost\":3"), 0, out err, out user);
            Console.WriteLine("time nan: " + err + " | " + user);
            Check(t == null && err != null && err.Contains("time") && user == AtomSpectraRestSpectrum.AnswerText("time"), "time = nan is a broken snapshot ((int)NaN would be int.MinValue)");
            var l = AtomSpectraRestSpectrum.Parse(Pr37Spectrum(",\"time\":10,\"lost\":nan"), 0, out err, out user);
            Check(l == null && err != null && err.Contains("lost") && user == AtomSpectraRestSpectrum.AnswerText("lost"), "lost = nan is a broken snapshot");
            Check(AtomSpectraRestSpectrum.Parse(Pr37Spectrum(",\"time\":-5"), 0, out err, out user) == null, "a negative time is refused");
            var nolost = AtomSpectraRestSpectrum.Parse(Pr37Spectrum(",\"time\":10"), 0, out err, out user);
            Check(nolost != null && nolost.InvalidPulses == 0, "no lost field: 0 as before");
        }

        // R6. Disconnect (Detach) while a poll hangs on the board: it returns at once (the UI thread calls it), nothing of the hung poll
        // reaches the document, and the poll thread ends by itself after its deadline.
        static void Pr37DetachHang()
        {
            AtomSpectraRestSession.ProbeGapMs = 3200;
            var c = new Counter();
            var s = NewSession(false, 0, null);   // headless, a counting board: joined
            Hook(s, c);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "joined the counting board");
            var a = s.Acquisition;
            Thread.Sleep(1200);   // the mock hangs every spectrum poll from its 4th second
            Check(WaitFor(() => a.Attempts > a.Successes + a.Failures + a.NoDataAnswers, 5000), "a poll hangs on the board");
            Thread.Sleep(300);
            int before = c.DataReady;
            var sw = Stopwatch.StartNew();
            s.Detach();
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine("Detach took " + ms.ToString(CultureInfo.InvariantCulture) + " ms (poll deadline " + a.DeadlineMs.ToString(CultureInfo.InvariantCulture) + " ms)");
            Check(ms < 500, "Disconnect returns at once although a poll hangs (" + ms + " ms)");
            Check(s.Acquisition == null && !s.IsOpen, "the session is detached");
            Check(WaitFor(() => !a.IsRunning, 8000), "the hung poll's thread ends by itself after its deadline");
            Thread.Sleep(1500);
            Check(c.DataReady == before && c.LinkLost == 0, "nothing of the detached poll reaches the session's listeners");
            s.Dispose();
        }

        // R9. Stop of a joined acquisition: nothing is sent, and the session tells its owner that the board counts on (the controller
        // shows it, not while the program closes); observe only does not.
        static void Pr37StopJoined()
        {
            AtomSpectraRestSession.ProbeGapMs = 3200;
            int kept = 0;
            var s = NewSession(false, 0, null);
            s.BoardKeepsCounting += (o, e) => kept++;
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "joined the counting board");
            var so = s.Stop(1000);
            Check(so == AtomSpectraTcpStopOutcome.Detached && kept == 1, "Stop of a joined acquisition: detached, the 'board counts on' event once (" + so + ", " + kept + ")");
            Check(Posts() == 0, "nothing is sent");
            s.Dispose();

            int keptObserve = 0;
            var s2 = NewSession(true, 0, null);
            s2.BoardKeepsCounting += (o, e) => keptObserve++;
            Check(s2.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "observe only: started");
            Check(s2.Stop(1000) == AtomSpectraTcpStopOutcome.Detached && keptObserve == 0, "observe only: no such event (the user chose not to command the board)");
            s2.Dispose();
        }

        // R7 and R8. The user's texts come from the resources and the log keeps its English; numbers are invariant whatever the culture.
        static void Pr37Texts()
        {
            AtomSpectraRestSession.ProbeGapMs = 300;
            // a command the board refuses (200 {"ok":false})
            var s = NewSession(false, 0, null);
            var o = s.Begin(true, 1000);
            Console.WriteLine("refused -sta: " + o + ", log: " + s.LastError + " | user: " + s.LastErrorText);
            Check(o == AtomSpectraTcpBeginOutcome.NoAnswer && s.Failure == AtomSpectraRestFailure.StartRefused, "-sta refused ({\"ok\":false})");
            Check(s.LastErrorText == Res.AtomSpectraRestErrCommandRefused && s.LastError != null && s.LastError.StartsWith("answered 200"), "the user's text is the resource, the log's stays English");
            s.Dispose();

            // no answer at all
            var r = new AtomSpectraRestClient("http://127.0.0.1:1/").Get("/api/spectrum.json", 1000);
            Console.WriteLine("refused port: log " + r.Error + " | user " + r.UserError);
            Check(r.UserError == Res.AtomSpectraRestErrRefused || r.UserError == Res.AtomSpectraRestErrTimeout, "a refused connection: the resource text");
            Check(r.Error != null && r.Error != r.UserError, "the log keeps the framework's own text");
            Check(AtomSpectraRestClient.HttpText(401) == Fmt(Res.AtomSpectraRestErrHttp, 401), "an HTTP status: the resource text");
            var u = new AtomSpectraRestSession(new AtomSpectraRestClient("http://127.0.0.1:1/"), 5, false, 0);
            u.Begin(true, 1000);
            string ut = u.UnreachableText();
            Check(ut.Contains(u.LastErrorText ?? "?") && !ut.Contains("ConnectFailure") && !ut.Contains("RequestCanceled"), "the unreachable text carries the resource reason, not the framework's status name");
            u.Dispose();
            Check(AtomSpectraRestSession.HeadlessAskText == Res.ERRAtomSpectraRestHeadlessAsk, "the headless text is a resource");

            // the guard: a user's reason next to the log's one
            var g = new AtomSpectraRestGuard();
            g.Begin(100, 1000, true);
            g.Check(new AtomSpectraRestBoardSample { HasData = true, Time = 50, Total = 10, AtMs = 1 });
            Check(g.TripReason == "board counters went down: time 100 -> 50 s, total 1000 -> 10, hist_ok 0 -> 0", "the log's reason stays English (" + g.TripReason + ")");
            Check(g.TripText == Fmt(Res.AtomSpectraRestTripCounters, 100, 50, 1000, 10), "the user's reason: the resource");
            var ge = new AtomSpectraRestGuard();
            ge.Begin(0, 0, true);
            ge.Check(new AtomSpectraRestBoardSample { HasData = true, Time = 5, Total = 10, AtMs = 1, Epoch = new AtomSpectraRestEpoch { BootId = 7 } });
            ge.Check(new AtomSpectraRestBoardSample { HasData = true, Time = 6, Total = 11, AtMs = 2, Epoch = new AtomSpectraRestEpoch { BootId = 8 } });
            Check(ge.TripReason == "board reset: boot_id: 7 -> 8" && ge.TripText == Fmt(Res.AtomSpectraRestTripEpoch, "boot_id", 7, 8), "a changed epoch: the log's and the user's reason");
            var gn = new AtomSpectraRestGuard();
            gn.Begin(100, 1000, true);
            gn.Check(AtomSpectraRestBoardSample.NoData(1));
            Check(gn.TripText == Fmt(Res.AtomSpectraRestTripNoData, 100, 1000), "the board lost its spectrum: the resource");

            // the calibration: a reason code, not the text of the Error
            Check(AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.ParseDevice("{\"calibration\":[1,2],\"calib_set\":false}")) == AtomSpectraRestCalibReason.NotSet, "calib_set=false: NotSet");
            Check(AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.FromSlots(null)) == AtomSpectraRestCalibReason.NotSet, "no slots: NotSet");
            var gap = AtomSpectraRestCalibration.ParseDevice("{\"calibration\":[1,null,3]}");
            Check(AtomSpectraRestCalibration.ReasonOf(gap) == AtomSpectraRestCalibReason.Gap && AtomSpectraRestCalibration.SlotOf(gap) == 2 && gap.Error == "Number after empty slot at index 2", "a gap: Gap at slot 2, the log's Error as before");
            Check(AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.ParseDevice("{\"calibration\":[1,\"x\"]}")) == AtomSpectraRestCalibReason.NotNumber, "a slot that is not a number");
            Check(AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.ParseDevice("{\"calibration\":[1]}")) == AtomSpectraRestCalibReason.TooFew, "one coefficient: TooFew");
            Check(AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.ParseDevice("[]")) == AtomSpectraRestCalibReason.NotObject
                && AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.ParseDevice("{}")) == AtomSpectraRestCalibReason.NoArray
                && AtomSpectraRestCalibration.ReasonOf(AtomSpectraRestCalibration.ParseDevice("{")) == AtomSpectraRestCalibReason.Malformed, "not an object, no array, broken JSON");
            Type form = Pr37Type("AtomSpectraTcpDeviceForm");
            if (form != null)
            {
                MethodInfo m = form.GetMethod("NotReadReason", BindingFlags.NonPublic | BindingFlags.Static);
                string why = (string)m.Invoke(null, new object[] { gap, AtomSpectraRestCalibReason.Gap });
                Check(why == Fmt(Pr37Res("AtomSpectraCalibErrGap"), 2), "the page's reason of a gap: the resource with the slot (" + why + ")");
            }

            // the gzip level for the user
            Check(AtomSpectraRestBootConfig.Describe(AtomSpectraRestBootConfig.Unsupported) == Res.AtomSpectraGzipLevelUnsupported && AtomSpectraRestBootConfig.Describe(6) == "6", "the gzip level: 'not supported' from the resources");

            // numbers are invariant: a culture whose minus sign is not "-" changes nothing
            CultureInfo old = Thread.CurrentThread.CurrentCulture;
            try
            {
                var odd = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
                odd.NumberFormat.NegativeSign = "~";
                Thread.CurrentThread.CurrentCulture = odd;
                string change = new AtomSpectraRestEpoch { BootId = -5 }.ChangeFrom(new AtomSpectraRestEpoch { BootId = -3 });
                Console.WriteLine("epoch change under a '~' minus: " + change);
                Check(change == "boot_id: -3 -> -5", "the epoch change is printed with the invariant minus (" + change + ")");
                Check(AtomSpectraRestSpectrum.LengthError(4096, -1, 0) == "bins count 4096 without \"channels\" (only 8192)", "the length error is invariant");
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }

        // R9. A POST does not ask for "100 Continue": the board's httpd never sends it, and the client would spend the short command
        // deadline waiting for it before the body (a -sto ending as Unconfirmed).
        static void Pr37Expect()
        {
            AtomSpectraRestSession.ProbeGapMs = 300;
            var s = NewSession(false, 0, null);   // an empty board: -sta goes out
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "started");
            var posts = Requests().Where(q => Str(q, "method") == "POST").ToList();
            Console.WriteLine("POSTs: " + string.Join("; ", posts.Select(q => Str(q, "path") + " " + Str(q, "body").Trim() + " expect='" + Str(q, "expect") + "'")));
            Check(posts.Count >= 1, "the command went out");
            Check(posts.All(q => Str(q, "expect") == ""), "no POST carries Expect: 100-continue");
            s.Stop(1000);
            s.Dispose();
        }
    }
}
