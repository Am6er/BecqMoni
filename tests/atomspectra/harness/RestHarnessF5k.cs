// link loss by answered failures (mixed series, 401/403),
// the channels of the edited field, a page that is not given an unfolded polynomial, the text of HeadlessAsk.
// The expected polynomials are NUMBERS computed beforehand (exact rationals), not a call of the code under test (see Fold4096/Fold2048).
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        // a(x), x = m*k + (m-1)/2, expanded in powers of k: the 8192-channel polynomial of the mock (3.65, 0.41, 4.5e-05, -1.3e-08, 1.2e-12) for 4096 / 2048
        // channels; computed with python fractions (exact), rounded to double once.
        static readonly double[] Fold4096 = { 3.855011248375075, 0.8200899805012, 0.0001799220072, -1.039808e-07, 1.92e-11 };
        static readonly double[] Fold2048 = { 4.2651012061310745, 1.6405396490647999, 0.0007190642592000001, -8.315392e-07, 3.072e-10 };
        static bool Near(double[] got, double[] want)
        {
            if (got == null || want == null || got.Length != want.Length) return false;
            for (int i = 0; i < want.Length; i++) if (Math.Abs(got[i] - want[i]) > 1e-12 * Math.Max(Math.Abs(want[i]), 1e-30)) return false;
            return true;
        }
        static string Dbl(double[] c) { return c == null ? "-" : string.Join("; ", c.Select(x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture))); }
        static bool RunF5k(string scenario)
        {
            Action body;
            switch (scenario)
            {
                case "f5-linklost-mixed": body = F5kMixed; break;
                case "f5-linklost-mixed-poll": body = F5kMixedPoll; break;
                case "f5-linklost-gzip3": body = F5kGzip3; break;
                case "f5-linklost-401": body = () => F5kAccess(401); break;
                case "f5-linklost-403": body = () => F5kAccess(403); break;
                case "f5-linklost-503": body = () => F5kAccess(503); break;
                case "f5-linklost-access-text": body = F5kAccessText; break;
                case "f5-form-cfg-n": body = F5kFormCfgN; break;
                case "f5-form-test-n": body = F5kFormTestN; break;
                case "f5-ctl-headless-text": body = F5kHeadlessText; break;
                default: return false;
            }
            F5dWith(body);
            return true;
        }
        // a board that answers 503 and goes silent in turn. The silences used to end the run of answered failures (and every 503 kept the silence
        // from building up), so the link was never lost. Answered failures since the last good poll: the third one (the 5th poll) is the loss, once.
        static void F5kMixed()
        {
            AtomSpectraRestAcquisition.MaxAttempts = 1;   // one case per poll
            int lost = 0;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => lost++;
            var after = new int[5];
            for (int i = 0; i < 5; i++) { a.PollOnce(); after[i] = lost; }
            Console.WriteLine("events after each poll (503, silence, 503, silence, 503): " + string.Join(",", after) + ", failures " + a.Failures);
            Check(a.Failures == 5, "all five polls failed (" + a.Failures + ")");
            Check(after[3] == 0, "two answered failures (503) with a silence between: no loss yet");
            Check(lost == 1 && a.IsLinkLost, "the third answered failure is the loss although silences came between: once (events " + lost + ")");
            a.Dispose();
        }
        // attempt 1 gets a body cut short at HTTP 200 (the board answered), the repeat in the same period gets nothing. The poll
        // returns the status of its LAST attempt (0), but the board did answer in it: three such polls are the loss.
        static void F5kMixedPoll()
        {
            AtomSpectraRestAcquisition.MaxAttempts = 2;
            int lost = 0;
            var a = new AtomSpectraRestAcquisition(Client(), 10, 8192);
            a.LinkLost += (o, e) => lost++;
            var after = new int[3];
            for (int i = 0; i < 3; i++) { a.PollOnce(); after[i] = lost; }
            Console.WriteLine("events after each poll (cut body then silence, x3): " + string.Join(",", after) + ", failures " + a.Failures + ", retries " + a.Retries);
            Check(a.Failures == 3 && a.Retries == 3, "each poll: the attempt answered, the repeat did not (failures " + a.Failures + ", retries " + a.Retries + ")");
            Check(after[1] == 0, "two such polls: no loss yet");
            Check(lost == 1 && a.IsLinkLost, "the third poll in which the board answered with a failure is the loss: once (events " + lost + ")");
            a.Dispose();
        }
        // Three broken gzip answers in a row (the trailer cut off, cut in the middle, a wrong CRC) are three ANSWERED
        // failures (HTTP 200, the board is there): the third is the loss, once.
        static void F5kGzip3()
        {
            AtomSpectraRestAcquisition.MaxAttempts = 1;
            int lost = 0;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => lost++;
            a.PollOnce(); a.PollOnce();
            Check(a.Failures == 2 && lost == 0, "two broken gzip answers: failures " + a.Failures + ", no loss yet (events " + lost + ")");
            a.PollOnce();
            Console.WriteLine("after three broken gzip answers: failures " + a.Failures + ", events " + lost + ", last error " + a.LastError);
            Check(a.Failures == 3 && lost == 1 && a.IsLinkLost, "the third broken gzip answer is the loss, once (events " + lost + ")");
            a.Dispose();
        }
        // a board that answers 401/403 (a wrong token) is not a board that does not answer: after the third answer the event comes (the polling
        // goes on) but the acquisition says it is a refusal of access (AccessDenied); an HTTP 503 is not. 503 is the control of the same scenario.
        static void F5kAccess(int status)
        {
            AtomSpectraRestAcquisition.MaxAttempts = 1;
            int lost = 0;
            var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
            a.LinkLost += (o, e) => lost++;
            a.PollOnce(); a.PollOnce();
            Check(lost == 0, "two answers HTTP " + status + ": no loss yet");
            a.PollOnce();
            PropertyInfo p = typeof(AtomSpectraRestAcquisition).GetProperty("AccessDenied");
            Check(p != null, "the acquisition has AccessDenied (a refusal of access is told from a lost link)");
            bool denied = p != null && (bool)p.GetValue(a);
            Console.WriteLine("HTTP " + status + " x3: events " + lost + ", AccessDenied " + denied + ", last error " + a.LastError);
            Check(lost == 1 && a.IsLinkLost, "three answers HTTP " + status + ": the event, once (events " + lost + ")");
            Check(denied == (status == 401 || status == 403), "AccessDenied is true for 401 and 403 only (HTTP " + status + ": " + denied + ")");
            a.Dispose();
        }
        // The text the user is shown: the board starts refusing access (HTTP 401) while a measurement is joined; the text of the lost link says so
        // and does not claim that the board does not answer. A session on the mock (--fail-from 3 --fail-status 401).
        static void F5kAccessText()
        {
            AtomSpectraRestAcquisition.MaxAttempts = 1;
            var c = new Counter();
            var s = NewSession(true, 0, null);
            Hook(s, c);
            Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
            Check(WaitFor(() => c.LinkLost >= 1, 40000), "the board refuses access from the 3rd second: the event comes");
            string text = s.LinkLostText();
            Console.WriteLine("text: " + text);
            Check(text.Contains("HTTP 401"), "the text names the refusal (HTTP 401)");
            Check(!text.Contains("does not answer"), "the text does not say that the board does not answer");
            s.Detach();
            s.Dispose();
        }
        // the text a headless refusal (a stopped board with data, no window to ask in) gives the user, from the controller
        static void F5kHeadlessText()
        {
            MethodInfo m = CtlT == null ? null : CtlT.GetMethod("RestFailureText", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) { Console.WriteLine("n/a: the controller is not in this build"); return; }
            AtomSpectraRestSession.ProbeGapMs = 3200;   // F5dWith shortens the gap to 300 ms, which makes every board "counting" (a gap under 3 s is a doubt): the board must be seen STOPPED
            var s = new AtomSpectraRestSession(Client(), 1, false, 0);   // Ask is null: no window
            var o = s.Begin(true, 1000);
            Check(o == AtomSpectraTcpBeginOutcome.Refused && s.Failure == AtomSpectraRestFailure.HeadlessAsk, "headless Start on a stopped board with data: refused, HeadlessAsk (" + o + ", " + s.Failure + ")");
            string text = (string)m.Invoke(null, new object[] { s, o });
            Console.WriteLine("text: " + text);
            // the text is a resource now (English or Russian by the culture of the run)
            Check(text == BecquerelMonitor.Properties.Resources.ERRAtomSpectraRestHeadlessAsk, "the text is the HeadlessAsk resource");
            Check(text.Contains("cannot ask") || text.Contains("не может спросить"), "the text says that a run without windows cannot ask what to do with the board's measurement");
            Check(text.Contains("Nothing was sent") || text.Contains("ничего не послано"), "the text says that nothing was sent to the board");
            s.Dispose();
        }
        // the button "read the calibration" of DeviceConfigForm takes the channels from the FIELD being edited (not from the
        // saved profile), and gives the field its page folded to them. A form of the real class, the REST board is the mock; the numbers are given beforehand.
        static void F5kFormCfgN()
        {
            Type ft = Type.GetType("BecquerelMonitor.DeviceConfigForm, BecquerelMonitor");
            if (ft == null || CfgT == null) { Console.WriteLine("n/a: the device configuration form is not in this build"); return; }
            var th = new Thread(() => { try { F5kFormCfgRun(ft); } catch (Exception ex) { Check(false, "form test threw " + ex); } });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            if (!th.Join(60000)) Check(false, "the form test did not finish in 60 s (a dialog waiting for a person?): the process ends, the dialog goes with it");
        }
        static System.Windows.Forms.Control F5kCtl(object owner, string name)
        {
            return (System.Windows.Forms.Control)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        }
        static void F5kFormCfgRun(Type ft)
        {   // what MainForm does before the forms are made (MainForm.cs:180-181)
            Type.GetType("BecquerelMonitor.DeviceType, BecquerelMonitor").GetMethod("InitializeDeviceTypes").Invoke(null, null);
            Type.GetType("BecquerelMonitor.ThermometerType, BecquerelMonitor").GetMethod("InitializeThermometerTypes").Invoke(null, null);
            var form = (System.Windows.Forms.Form)Activator.CreateInstance(ft);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-3000, -3000);
            form.ShowInTaskbar = false;
            form.Show();
            F5kFormCfgCase(form, ft, 8192, "4096", Fold4096, "saved profile 8192, the field says 4096");
            F5kFormCfgCase(form, ft, 8192, "2048", Fold2048, "saved profile 8192, the field says 2048");
            F5kFormCfgCase(form, ft, 4096, "4096", Fold4096, "saved profile 4096, the field says 4096");
            F5kFormCfgCase(form, ft, 4096, "8192", MockCalib, "saved profile 4096, the field says 8192");
            // NOT form.Close(): FormClosing asks "the configuration file was changed, save?" (ConfirmSaveDeviceConfig) and waits for a person. Dispose raises no
            // FormClosing, so no dialog and nothing is saved (the harness never calls SaveConfig; the form was found to show this dialog in the first run, ).
            form.Dispose();
        }
        static void F5kFormCfgCase(System.Windows.Forms.Form form, Type ft, int saved, string field, double[] want, string what)
        {
            object info = Activator.CreateInstance(CfgT);
            CfgT.GetProperty("Guid").SetValue(info, Guid.NewGuid().ToString());
            CfgT.GetProperty("DeviceType").SetValue(info, AtomSpectraTcpDeviceConfig.DeviceTypeId);
            CfgT.GetProperty("NumberOfChannels").SetValue(info, saved);
            CfgT.GetProperty("EnergyCalibration").SetValue(info, new PolynomialEnergyCalibration());
            CfgT.GetProperty("InputDeviceConfig").SetValue(info, new AtomSpectraTcpDeviceConfig { Transport = AtomSpectraTransport.Rest, RestUrl = "http://127.0.0.1:" + port + "/" });
            ft.GetProperty("ActiveDeviceConfig").SetValue(form, info);
            F5kCtl(form, "integerTextBox1").Text = field;   // (a TextChanged marks the configuration dirty: cleared, then only the end of the read sets it again)
            CfgT.GetProperty("Dirty").SetValue(info, false);
            var btn = F5kCtl(form, "button13");
            ft.GetMethod("button13_Click", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { null, EventArgs.Empty });
            // button13.Enabled is false while the parent panel of a form with no configuration chosen is disabled: the end of the read is the dirty flag
            bool done = Pump(() => (bool)CfgT.GetProperty("Dirty").GetValue(info), 10000);
            double[] got = new double[5];
            string[] names = { "numericUpDown7", "numericUpDown2", "numericUpDown1", "numericUpDown9", "numericUpDown8" };
            for (int i = 0; i < 5; i++) double.TryParse(F5kCtl(form, names[i]).Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out got[i]);
            Console.WriteLine(what + ": fields " + Dbl(got));
            Check(done && Near(got, want), what + ": the page gets " + Dbl(want));
        }
        // "Test connection (REST)" gives the Energy Calibration page the board's polynomial folded to the profile's channels; where it
        // cannot be folded (6000 does not divide 8192, 16384 is more than the board has) the page is given nothing - not the 8192-channel polynomial.
        static void F5kFormTestN()
        {
            Type t = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
            if (t == null) { Console.WriteLine("n/a: the settings form is not in this build"); return; }
            var th = new Thread(() => { try { F5kFormTestRun(t); } catch (Exception ex) { Check(false, "form test threw " + ex); } });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            if (!th.Join(60000)) Check(false, "the form test did not finish in 60 s (a dialog waiting for a person?): the process ends, the dialog goes with it");
        }
        static void F5kFormTestRun(Type t)
        {
            F5kFormTestCase(t, 4096, Fold4096);
            F5kFormTestCase(t, 2048, Fold2048);
            F5kFormTestCase(t, 8192, MockCalib);
            F5kFormTestCase(t, 6000, null);
            F5kFormTestCase(t, 16384, null);
        }
        static void F5kFormTestCase(Type t, int n, double[] want)
        {
            var nf = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
            var host = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000), ShowInTaskbar = false, ClientSize = new System.Drawing.Size(490, 599) };
            var form = (System.Windows.Forms.Control)Activator.CreateInstance(t);
            host.Controls.Add(form);
            t.GetMethod("LoadFormContents").Invoke(form, new object[] { new AtomSpectraTcpDeviceConfig { Transport = AtomSpectraTransport.Rest, RestUrl = "http://127.0.0.1:" + port + "/" } });
            host.Show();
            double[] got = null;
            int given = 0;
            t.GetField("calibrationTarget", nf).SetValue(form, new Action<double[]>(c => { got = c; given++; }));
            t.GetField("profileChannels", nf).SetValue(form, new Func<int>(() => n));
            t.GetMethod("buttonRestTest_Click", nf).Invoke(form, new object[] { null, EventArgs.Empty });
            var btn = F5kCtl(form, "buttonRestTest");
            Pump(() => btn.Enabled, 10000);
            System.Windows.Forms.Application.DoEvents();
            string output = F5kCtl(form, "textOutput").Text;
            Console.WriteLine("profile " + n + " channels: page given " + given + " time(s), " + Dbl(got));
            if (want == null) Check(given == 0 && got == null && output.Contains(n.ToString()), "profile " + n + " channels: nothing is given to the page, the output says why (given " + given + ")");
            else Check(given == 1 && Near(got, want), "profile " + n + " channels: the page gets " + Dbl(want));
            host.Close();
            host.Dispose();
        }
        // ==END==
    }
}
