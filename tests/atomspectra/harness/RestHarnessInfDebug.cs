// the text of the -inf answer goes to the client log at the Debug level, hidden by default (the device serial may be in it).
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        // console-tcp-inf-debug: the bridge mock (mock_cmd.py --inf-text "...SERIAL 00000001...") answers -inf with a text
        static bool RunInfDebug()
        {
            object debug;
            try
            {
                debug = Enum.Parse(typeof(AtomSpectraTcpLogLevel), "Debug");
            }
            catch (ArgumentException)
            {
                Check(false, "the client log has no Debug level");
                return true;
            }
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
            s.Begin(false, 2000);
            string answer = s.Query("-inf", 2000);
            s.Dispose();
            var lines = AtomSpectraTcpLog.Snapshot();
            Console.WriteLine("answer: " + (answer ?? "<none>").Replace("\r", " ").Replace("\n", " / "));
            Check(answer != null && answer.Contains("SERIAL 00000001"), "the mock's -inf answer arrived");
            Check(lines.Any(l => l.Level.Equals(debug) && l.Text.Contains("SERIAL 00000001")), "the -inf text is in the log at the Debug level");
            Check(!lines.Any(l => !l.Level.Equals(debug) && l.Text.Contains("SERIAL 00000001")), "the -inf text is on no other level");
            Check(lines.Any(l => l.Level == AtomSpectraTcpLogLevel.Info && l.Text.Contains("sent -inf")), "the Info line 'sent -inf: N chars' stays");

            Type lf = Type.GetType("BecquerelMonitor.AtomSpectraTcpLogForm, BecquerelMonitor");
            if (lf == null)
            {
                Console.WriteLine("n/a: the log window is not in this build");
                return true;
            }
            var thread = new Thread(() =>
            {
                try
                {
                    var f = BindingFlags.NonPublic | BindingFlags.Instance;
                    var form = (Form)Activator.CreateInstance(lf, true);
                    var box = (ComboBox)lf.GetField("levelBox", f).GetValue(form);
                    MethodInfo passes = lf.GetMethod("Passes", f);
                    var line = new AtomSpectraTcpLogLine(1, DateTime.Now, (AtomSpectraTcpLogLevel)debug, "TCP", "answer to -inf: SERIAL 00000001");
                    var info = new AtomSpectraTcpLogLine(2, DateTime.Now, AtomSpectraTcpLogLevel.Info, "TCP", "sent -inf: 30 chars");
                    Console.WriteLine("filter items: " + box.Items.Count + ", default " + box.SelectedIndex + " '" + box.Text + "'");
                    Check(!(bool)passes.Invoke(form, new object[] { line }) && (bool)passes.Invoke(form, new object[] { info }), "default filter: Debug lines hidden, Info lines shown");
                    box.SelectedIndex = box.Items.Count - 1;
                    Check(box.Items.Count == 4 && (bool)passes.Invoke(form, new object[] { line }), "the last filter item shows the Debug lines");
                    form.Dispose();
                }
                catch (Exception ex)
                {
                    Check(false, "log window test threw " + ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return true;
        }
    }
}
