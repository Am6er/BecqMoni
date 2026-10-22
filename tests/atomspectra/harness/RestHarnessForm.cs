using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunForm(string scenario, string[] args)
        {
            if (RunF5b(scenario)) return true;   // f5-timeout, f5-linklost-time, f5-ctl-calib (RestHarnessF5b.cs)
            if (scenario.StartsWith("f5-")) return RunF5(scenario);   // client rules on the recorded responses (RestHarnessF5.cs)
            if (scenario.StartsWith("gzip-") || scenario.StartsWith("form-gzip")) return RunGzip(scenario);   // (RestHarnessGzip.cs)
            if (scenario == "console-tcp-inf-debug") return RunInfDebug();   // (RestHarnessInfDebug.cs)
            if (scenario.StartsWith("console-tcp-start-")) return RunTcpStart(scenario.Substring("console-tcp-start-".Length));   // (RestHarnessTcpStart.cs)
            if (scenario == "form-calib" || scenario == "form-calib-unset" || scenario == "start-calib") return RunCalibration(scenario);   // (RestHarnessCalibration.cs)
            if (scenario == "form-mode-texts") return RunFormModeTexts();
            if (scenario == "form-layout") return RunFormLayout();   // layout at the page width (RestHarnessFormLayout.cs)
            if (scenario == "form-layout-ru")   // the same layout with the Russian captions (satellite ru\ copied by rest_suite.py)
            {
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("ru-RU");
                return RunFormLayout();
            }
            if (scenario != "form-new-empty" && scenario != "form-bad-address")
                return false;

            Type t = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
            if (t == null)
            {
                Console.WriteLine("n/a: the settings form is not in this build (source build without WinForms)");
                return true;
            }

            var thread = new Thread(() =>
            {
                try
                {
                    var warnings = new List<string>();

                    FieldInfo hook = t.GetField("ShowWarning", BindingFlags.Public | BindingFlags.Static);
                    Check(hook != null, "the form has the ShowWarning hook");
                    if (hook == null) return;

                    object saved = hook.GetValue(null);
                    hook.SetValue(null, new Func<string, DialogResult>(s =>
                    {
                        lock (warnings) warnings.Add(s);
                        Console.WriteLine("warning: " + s);
                        return DialogResult.OK;
                    }));

                    try
                    {
                        object form = Activator.CreateInstance(t);
                        try
                        {
                            var cfg = new AtomSpectraTcpDeviceConfig();
                            t.GetMethod("LoadFormContents").Invoke(form, new object[] { cfg });

                            Check(warnings.Count == 0, "opening a new configuration raises no warning");

                            if (scenario == "form-bad-address")
                            {
                                var tb = (TextBox)t.GetField("textAddress", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                                tb.Text = "http://192.0.2.19/";
                            }

                            bool saved_ok;
                            string threw = null;
                            try
                            {
                                saved_ok = (bool)t.GetMethod("SaveFormContents").Invoke(form, new object[] { cfg });
                            }
                            catch (TargetInvocationException ex)
                            {
                                saved_ok = false;
                                threw = ex.InnerException != null ? ex.InnerException.GetType().Name : ex.GetType().Name;
                            }
                            Console.WriteLine("save: " + saved_ok + (threw != null ? ", threw " + threw : ""));

                            if (scenario == "form-new-empty")
                            {
                                Check(warnings.Count == 0, "saving a new configuration with an empty address raises no warning (the address is checked at Start and Test)");
                                Check(saved_ok, "the configuration with an empty address can be saved");
                            }
                            else
                            {
                                Check(threw == "FormatException" && !saved_ok, "a URL in the address field is refused");
                                Check(warnings.Count == 1, "exactly one warning");
                                string w = warnings.Count > 0 ? warnings[0] : "";
                                Check(w.Contains("host[:port]"), "the warning describes the accepted form host[:port]");
                                Check(!w.Contains("without \":port\"") && !w.Contains("без «:порт»"), "no outdated 'without :port'");
                            }
                        }
                        finally
                        {
                            ((IDisposable)form).Dispose();
                        }
                    }
                    finally
                    {
                        hook.SetValue(null, saved);
                    }
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
