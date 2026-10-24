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
            if (RunUiFix(scenario)) return true;   // PR37 fix, UI lane (UiFix.cs)
            if (scenario.StartsWith("tcplane-") || scenario.StartsWith("console-tcp-lane-")) return RunTcpLane(scenario);   // PR #38, TCP lane (RestHarnessTcpLane.cs)
            if (scenario == "console-tcp-fix10-stop-reenter" || scenario.StartsWith("fix10-")) return RunFix10(scenario);   // audit 6 (RestHarnessFix10.cs)
            if (scenario == "fix9-reset-carry" || scenario == "fix9-reset-other-board") return RunFix9b(scenario);   // VERIFY-4 #7 (RestHarnessFix9b.cs)
            if (scenario == "bm10-quiet-three" || scenario == "bm10-count-address" || scenario == "bm10-bind-fallback"
                || scenario == "bm10-log-sent" || scenario == "bm10-log-packets") return RunFix9(scenario);   // VERIFY-4 (RestHarnessFix9.cs)
            if (scenario == "bm18-chooser") return RunFix7(scenario);   // #BM-18 (RestHarnessFix7.cs)
            if (scenario == "bm10-schedule") return RunFix6(scenario);   // #BM-17 (RestHarnessFix6.cs)
            if (scenario == "bm10-adapters") return RunFix5(scenario);   // #BM-17 (RestHarnessFix5.cs)
            if (scenario.StartsWith("fix4-")) return RunFix4(scenario);   // #BM-14/#BM-15 (RestHarnessFix4.cs)
            if (scenario.StartsWith("fix3-")) return RunFix3(scenario);   // (RestHarnessFix3.cs)
            if (scenario.StartsWith("fix2-") || scenario.StartsWith("console-tcp-fix2-")) return RunFix2(scenario);   // (RestHarnessFix2.cs)
            if (scenario.StartsWith("night-") || scenario.StartsWith("console-tcp-night-")) return RunNight(scenario);   // (RestHarnessNight.cs)
            if (scenario.StartsWith("bm10-")) return RunBm10(scenario);   // #BM-10 (RestHarnessBm10.cs)
            if (scenario.StartsWith("startpkg-") || scenario.StartsWith("console-tcp-same-")) return RunStartPkg(scenario);   // (RestHarnessStartPkg.cs)
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
                                // PR37 fix, UI lane: the refusal says that the page has shown its warning (DeviceConfigForm adds no second window)
                                Check(threw == "DeviceSaveRefusedException" && !saved_ok, "a URL in the address field is refused (DeviceSaveRefusedException: already reported)");
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
