using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFormModeTexts()
        {
            Type t = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
            if (t == null)
            {
                Console.WriteLine("n/a: the settings form is not in this build");
                return true;
            }

            var thread = new Thread(() =>
            {
                try
                {
                    var f = BindingFlags.NonPublic | BindingFlags.Instance;
                    object form = Activator.CreateInstance(t);

                    var dtCfg = new AtomSpectraTcpDeviceConfig();
                    dtCfg.DeadTimeValue = 1.21428571428571E-06;   // "Dead Time: 1.21428571428571 µs"
                    t.GetMethod("LoadFormContents").Invoke(form, new object[] { dtCfg });

                    Func<string, Control> c = n => (Control)t.GetField(n, f).GetValue(form);
                    Console.WriteLine("dead time: " + c("labelDeadTime").Text);
                    Check(c("labelDeadTime").Text.Contains("1.214") && !c("labelDeadTime").Text.Contains("1.2142"), "the dead time is rounded to 3 decimals");

                    Console.WriteLine("REST hint: " + c("labelHint").Text + " | test enabled: " + c("buttonTest").Enabled + " | " + c("labelTestResult").Text);
                    Check(c("labelHint").Text.Contains("host[:port]") && !c("labelHint").Text.Contains("8234"), "REST: its own hint (host[:port]), not the bridge's port 8234");
                    Check(!c("buttonTest").Enabled && c("buttonTest").Text.Length > 0 && c("labelTestResult").Text.Length > 0, "REST: the bridge test button is disabled, keeps its text, and the reason is shown");

                    ((RadioButton)c("radioTcp")).Checked = true;

                    Console.WriteLine("TCP hint: " + c("labelHint").Text + " | test enabled: " + c("buttonTest").Enabled);
                    Check(c("labelHint").Text.Contains("8234") && c("buttonTest").Enabled, "TCP: the bridge hint and test button as before");

                    ((IDisposable)form).Dispose();
                }
                catch (Exception ex)
                {
                    Check(false, "form test threw " + ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return true;
        }
    }
}
