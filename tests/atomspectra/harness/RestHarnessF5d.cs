// The window of an own reset is judged by TIME of the snapshots
// Rule: the first snapshot does not close it, any change of the epoch triple inside it is part of our
// own reset, it closes max(6 s, 2 x period) after the last change; an unknown outcome of the reset request keeps it open.
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static int F5dCount(string text)
        {
            return AtomSpectraTcpLog.Snapshot().Count(l => l.ToString().Contains(text));
        }


        static bool RunF5d(string scenario)
        {
            Action body = null;
            switch (scenario)
            {
                case "f5-window-steps": body = F5dWindowSteps; break;
                case "f5-window-lost": body = F5dWindowLost; break;
                case "f5-window-notsent": body = F5dWindowNotSent; break;
                case "f5-clear-twice": body = F5dClearTwice; break;
                case "f5-startover-lost": body = F5dStartOverLost; break;
                case "f5-nodata-signal": body = F5dNoDataSignal; break;
                case "f5-stale-after": body = F5dStaleAfter; break;
                case "f5-window-swallow": body = F5dWindowSwallow; break;
                case "f5-form-calib": body = F5dFormCalib; break;
                default: return RunF5e(scenario);   // RestHarnessF5e.cs
            }
            F5dWith(body);
            return true;
        }

        // the stall detector must not interfere with long repeated snapshots; "counting" is decided by two looks 300 ms apart
        static void F5dWith(Action body)
        {
            long oldStall = AtomSpectraRestSession.StallOverrideMs;
            int oldGap = AtomSpectraRestSession.ProbeGapMs;
            AtomSpectraRestSession.StallOverrideMs = 600000;
            AtomSpectraRestSession.ProbeGapMs = 300;
            try { body(); }
            catch (Exception ex) { Check(false, "exception " + ex); }
            finally { AtomSpectraRestSession.StallOverrideMs = oldStall; AtomSpectraRestSession.ProbeGapMs = oldGap; }
        }

        // a session on the mock that notes the last total handed to the document
        static AtomSpectraRestSession F5dSession(Counter c, long[] last)
        {
            var s = new AtomSpectraRestSession(Client(), 1, false, 0);
            s.RequestChannels = 8192;
            Hook(s, c);
            s.DataReady += (o, e) => { var r = e as AtomSpectraRestDataReadyArgs; if (r != null) last[0] = r.Total; };
            return s;
        }

        // (1) the own-reset window: step 1 (spec_resets+1) about 2 s and step 2 (dev_resets+1) about 5 s after the reset are 0 stops;
        // an external reset later than max(6 s, 2 x period) after the last step stops with the field named
        static void F5dWindowSteps()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.DataReady >= 3, 8000), "three snapshots before the reset");
            Check(s.ClearNow(2000), "Clear is accepted");
            Check(WaitFor(() => last[0] == 1500, 8000), "step 1 (spec_resets+1, about 2 s after the reset) is taken: the new acquisition");
            Check(WaitFor(() => last[0] == 2500 || c.Reset != null, 12000), "step 2 (dev_resets+1, about 5 s after the reset) comes");
            Console.WriteLine("after step 2: last total " + last[0] + ", reset text " + c.Reset);
            Check(last[0] == 2500 && c.Reset == null && s.Acquisition != null, "0 stops: both steps belong to our own reset");
            Check(WaitFor(() => c.Reset != null, 25000), "an external reset after the window (>9 s after the last step) stops polling");
            Check(c.Reset != null && c.Reset.StartsWith("board reset: boot_id:"), "the stop names the field: " + c.Reset);
            Check(last[0] == 2500, "the document did not take the external snapshot (last total " + last[0] + ")");
            s.Dispose();
        }

        // (2a) the answer to POST /api/reset is lost (the mock holds it 3 s, the client gives up at 1.5 s) but the board did reset
        static void F5dWindowLost()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.DataReady >= 3, 8000), "three snapshots before the reset");
            bool ok = s.ClearNow(1500);
            Console.WriteLine("ClearNow " + ok + ", failure " + s.Failure + " (outcome unknown)");
            Check(!ok, "the answer of the reset was lost: ClearNow reports no confirmation");
            Check(WaitFor(() => last[0] == 1500 || c.Reset != null, 10000), "the new acquisition (spec_resets+1) comes");
            Check(c.Reset == null && s.Acquisition != null && last[0] == 1500, "an unknown outcome keeps the window: 0 stops, the new set is in the document (" + c.Reset + ")");
            s.Dispose();
        }

        // (2b) the request certainly never reached the board (connection refused = NotSent): the window is withdrawn at once, so the
        // epoch change that comes later IS an external reset. Green by design (a guard against keeping the window too long).
        static void F5dWindowNotSent()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.DataReady >= 3, 4000), "three snapshots before the refusal window (mock down 4.5-22.5 s)");
            while (sw.ElapsedMilliseconds < 5000) Thread.Sleep(50);
            var probe = Client().Reset(8000);   // the same refusal seen by the client directly
            Check(probe.NotSent && !probe.Accepted, "a refused connect is classified NotSent (" + probe.Error + ")");
            bool ok = s.ClearNow(8000);   // a refused connect is retried by Windows for about 2 s, then ConnectFailure
            Console.WriteLine("ClearNow " + ok + " at " + sw.ElapsedMilliseconds + " ms, failure " + s.Failure);
            Check(!ok && s.Failure == AtomSpectraRestFailure.ResetRefused, "the reset was not delivered");
            Check(WaitFor(() => c.Reset != null || last[0] == 1500, 60000), "the board comes back with spec_resets+1");
            Check(c.Reset != null && c.Reset.StartsWith("board reset: spec_resets:"), "NotSent withdrew the window: the change is an external reset (" + c.Reset + ")");
            s.Dispose();
        }

        // (3) a second Clear before the first snapshot of the new acquisition: the reference numbers of the stale check are those of
        // the board BEFORE the first reset (163682 s / 13442130), not those of the empty new set
        static void F5dClearTwice()
        {
            var g = new AtomSpectraRestGuard();
            g.Begin(0, 0, true);
            Check(g.Check(GS(163682, 13442130, 119812, 1000, 7, 3, 0)) == AtomSpectraRestUpdate.Apply, "the board is joined");
            g.ExpectOwnReset(); g.ConfirmOwnReset();   // Clear 1
            Check(g.Check(AtomSpectraRestBoardSample.NoData(1500)) == AtomSpectraRestUpdate.NoData, "a 404 after the first reset");
            g.ExpectOwnReset(); g.ConfirmOwnReset();   // Clear 2, still no snapshot of the new set
            AtomSpectraRestUpdate old = g.Check(GS(163682, 13442130, 119812, 2500, 7, 3, 0));
            Console.WriteLine("the cached old snapshot after Clear 2: " + old);
            Check(old == AtomSpectraRestUpdate.Stale, "the cached snapshot from before the first reset is not taken into the document");
            Check(g.Check(GS(5, 40, 119813, 3500, 7, 3, 1)) == AtomSpectraRestUpdate.Apply, "the new acquisition is taken");
            Check(g.Check(GS(8, 70, 119814, 4500, 7, 3, 1)) == AtomSpectraRestUpdate.Apply && !g.Tripped, "and goes on");
        }

        // (4) "Start over" with the first poll lost: the probes (no ?channels) carry the numbers of the old set, 5 s / 100; by the time the
        // second poll answers (after the skip) the new set is above them (12 s / 400). The first snapshot is the reference, not Stale.
        static void F5dStartOverLost()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            s.Ask = Answer(c, AtomSpectraRestStartChoice.Restart);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started (Start over)");
            Check(s.RestartedBoard && s.LastPlan == AtomSpectraRestStartPlan.Restart, "the board was reset by us");
            Check(WaitFor(() => last[0] == 400 || c.Reset != null, 15000), "the new acquisition (12 s / 400) reaches the document");
            Console.WriteLine("last total " + last[0] + ", data events " + c.DataReady + ", polls failed " + (s.Acquisition == null ? -1 : s.Acquisition.Failures));
            Check(last[0] == 400 && c.Reset == null, "after a lost first poll the document is updated, no stop");
            Check(s.Acquisition != null && s.Acquisition.Failures == 1, "exactly the first poll was lost");
            s.Dispose();
        }

        // (6) the "no data" signal: a series of 404 longer than 3 x the period in force = ONE log line, polling alive, no Trip;
        // a single 404 = no line; a snapshot with data ends the series (the next series gives its own line)
        static void F5dNoDataSignal()
        {
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            var a = s.Acquisition;
            Check(WaitFor(() => a.NoDataAnswers >= 1, 8000), "the first 404");
            Check(F5dCount("board has no data yet") == 0, "one 404: no line");
            Check(WaitFor(() => a.NoDataAnswers >= 6, 12000), "six 404 in a row (5 s, longer than 3 periods of 1 s)");
            Check(F5dCount("board has no data yet") == 1, "a long series: exactly one line (" + F5dCount("board has no data yet") + ")");
            Check(c.Reset == null && s.Acquisition != null && c.Stalled == 0, "no Trip, no stop, polling is alive");
            int d = c.DataReady;
            Check(WaitFor(() => c.DataReady >= d + 2, 8000), "a snapshot with data comes");
            Check(WaitFor(() => a.NoDataAnswers >= 12, 15000), "a second long series");
            Check(F5dCount("board has no data yet") == 2, "the data ended the first series: one line per series (" + F5dCount("board has no data yet") + ")");
            s.Dispose();
        }

        // (7) the freshness mark of the log window follows the period in force: 2 x period + 5 s (the controller sets 2 x configured + 5 once)
        static void F5dStaleAfter()
        {
            int old = AtomSpectraTcpLog.StaleAfterSeconds;
            try
            {
                AtomSpectraTcpLog.StaleAfterSeconds = 7;   // what the controller sets for a configured 1 s
                var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
                Check(a.PollOnce() && AtomSpectraTcpLog.StaleAfterSeconds == 7, "a full poll at the configured period: 7 s stays");
                Check(!a.PollOnce() && a.EffectivePeriodMs == 2000, "c06: skipped poll, period 2 s");
                Check(AtomSpectraTcpLog.StaleAfterSeconds == 9, "period 2 s: stale after 9 s (got " + AtomSpectraTcpLog.StaleAfterSeconds + ")");
                Check(!a.PollOnce() && a.EffectivePeriodMs == 4000, "c07: skipped poll, period 4 s");
                Check(AtomSpectraTcpLog.StaleAfterSeconds == 13, "period 4 s: stale after 13 s (got " + AtomSpectraTcpLog.StaleAfterSeconds + ")");
                a.Dispose();
            }
            finally { AtomSpectraTcpLog.StaleAfterSeconds = old; }
        }

        // (8)the first change of the triple after our reset is its answer; a further change inside the window is swallowed and
        // written ONCE to the log: "external reset swallowed by own-reset window: <field> ..."
        static void F5dWindowSwallow()
        {
            const string line = "external reset swallowed by own-reset window";
            var c = new Counter();
            long[] last = { 0 };
            var s = F5dSession(c, last);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.DataReady >= 3, 8000), "three snapshots before the reset");
            Check(s.ClearNow(2000), "Clear is accepted");
            Check(WaitFor(() => last[0] == 2500 || c.Reset != null, 12000), "the second change (boot_id, a real reboot inside the window) comes");
            Check(c.Reset == null && last[0] == 2500, "swallowed: 0 stops (" + c.Reset + ")");
            Check(F5dCount(line) == 1, "exactly one line about it (" + F5dCount(line) + ")");
            Check(AtomSpectraTcpLog.Snapshot().Any(l => l.ToString().Contains(line) && l.ToString().Contains("boot_id")), "the line names the field");
            s.Dispose();
        }

        // "Test connection (REST)" gives the Energy Calibration page the board polynomial for the profile's channels, not for 8192
        // (real build only). The form knows the profile only through the hook profileChannels (null in the older tests = unchanged).
        static void F5dFormCalib()
        {
            Type t = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
            if (t == null) { Console.WriteLine("n/a: the settings form is not in this build"); return; }
            var nf = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
            FieldInfo hook = t.GetField("profileChannels", nf), target = t.GetField("calibrationTarget", nf);
            Check(hook != null && target != null, "the form has the hooks profileChannels (N of the profile) and calibrationTarget");
            if (hook == null || target == null) return;
            var th = new Thread(() =>
            {
                try { F5dFormCalibRun(t, hook, target); }
                catch (Exception ex) { Check(false, "form test threw " + ex); }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            th.Join();
        }

        static void F5dFormCalibRun(Type t, FieldInfo hook, FieldInfo target)
        {
            var host = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new System.Drawing.Size(490, 599) };
            var form = (System.Windows.Forms.Control)Activator.CreateInstance(t);
            host.Controls.Add(form);
            var cfg = new AtomSpectraTcpDeviceConfig { Transport = AtomSpectraTransport.Rest, RestUrl = "http://127.0.0.1:" + port + "/" };
            t.GetMethod("LoadFormContents").Invoke(form, new object[] { cfg });
            host.Show();
            double[] got = null;
            target.SetValue(form, new Action<double[]>(c => got = c));
            hook.SetValue(form, new Func<int>(() => 4096));
            t.GetMethod("buttonRestTest_Click", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, EventArgs.Empty });
            var btn = (System.Windows.Forms.Control)t.GetField("buttonRestTest", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            Pump(() => btn.Enabled, 8000);
            System.Windows.Forms.Application.DoEvents();
            double[] want = AtomSpectraRestCalibration.ForChannels(MockCalib, 8192, 4096);
            Console.WriteLine("page got: " + (got == null ? "null" : string.Join("; ", got)));
            Check(got != null && want != null && got.SequenceEqual(want), "profile of 4096 channels: the page gets the polynomial of the folded scale, as Start gives the document");
            Check(got != null && !got.SequenceEqual(MockCalib), "not the 8192-channel coefficients of the board");
            host.Close();
            host.Dispose();
        }
    }
}
