using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    // PR2 (feat/rest-debts): the sequence of MainForm.DeleteActiveSpectrum over a joined REST acquisition (scenarios pr2-*)
    static partial class Program
    {
        static bool RunPr2(string scenario)
        {
            if (scenario != "pr2-delete-keep" && scenario != "pr2-clear-after-keep")
            {
                Check(false, "unknown scenario");
                return true;
            }

            Func<bool> oldWindows = null;
            FieldInfo windowsField = null;
            try
            {
                Type CtlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
                Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");
                Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
                Type RdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");

                if (CtlT == null || McT == null || UiT == null || RdT == null)
                {
                    Console.WriteLine("n/a: the controller is not in this build");
                    return true;
                }

                lock (Bm22Journal)
                {
                    Bm22Journal.Clear();
                }

                string url = "http://127.0.0.1:" + port + "/";
                object cfg = Bm22RestCfg(url, false);
                object ctl, mc, rd;
                Bm22Make(cfg, out ctl, out mc, out rd);
                if (mc == null)
                {
                    Check(false, "setup failed");
                    return true;
                }

                object docConfig = RdT.GetProperty("DeviceConfig").GetValue(rd, null);   // the board folds 8192 bins by a whole factor: 1024 channels
                docConfig.GetType().GetProperty("NumberOfChannels").SetValue(docConfig, 1024, null);

                MethodInfo attachMethod = CtlT.GetMethod("AttachToDevice", new[] { RdT });
                if (attachMethod == null)
                {
                    Check(false, "AttachToDevice not found");
                    return true;
                }
                object attachResult;
                try { attachResult = attachMethod.Invoke(ctl, new[] { rd }); }
                catch (TargetInvocationException ex) { throw ex.InnerException; }
                bool joined = (bool)attachResult;
                Check(joined, "joined");

                Type statusType = RdT.GetProperty("ResultDataStatus").PropertyType;
                bool recording = (bool)statusType.GetProperty("Recording").GetValue(RdT.GetProperty("ResultDataStatus").GetValue(rd, null), null);
                Check(recording, "recording is true");

                FieldInfo probeField = UiT.GetField("Probe", BindingFlags.Public | BindingFlags.Static);
                if (probeField == null)
                {
                    Check(false, "Probe field not found");
                    return true;
                }

                Func<string, string, string, bool> probe = (kind, text, title) =>
                {
                    lock (Bm22Journal)
                    {
                        Bm22Journal.Add(kind + "|" + text);
                    }
                    return kind == "ask" ? Bm22Answer : true;
                };
                probeField.SetValue(null, probe);
                Bm22Answer = true;

                windowsField = CtlT.GetField("WindowsForStopQuestion", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (windowsField == null)
                {
                    Check(false, "WindowsForStopQuestion field not found");
                    return true;
                }
                oldWindows = (Func<bool>)windowsField.GetValue(null);
                windowsField.SetValue(null, (Func<bool>)(() => true));

                MethodInfo stopMethod = McT.GetMethod("StopRecording", new[] { typeof(bool) });
                if (stopMethod == null)
                {
                    Check(false, "StopRecording not found");
                    return true;
                }
                try
                {
                    stopMethod.Invoke(mc, new object[] { true });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }

                object statusAfter = RdT.GetProperty("ResultDataStatus").GetValue(rd, null);
                Check(!(bool)statusType.GetProperty("Recording").GetValue(statusAfter, null), "the Stop ended the recording of the document");

                if (scenario == "pr2-clear-after-keep")
                {
                    // the "Clear" button after "Stop" -> "Do not stop" of a joined acquisition (checklist 2026.10.24.01, section 4): what REST does now
                    MethodInfo clearMethod = McT.GetMethod("ConfirmAndClear");
                    if (clearMethod == null) { Check(false, "ConfirmAndClear not found"); return true; }
                    int asksBefore = Bm22Count("ask"), resetsBefore = Bm22Resets();   // the Stop phase is judged by pr2-delete-keep
                    bool cleared;
                    try { cleared = (bool)clearMethod.Invoke(mc, null); }
                    catch (TargetInvocationException ex) { throw ex.InnerException; }
                    Console.WriteLine("PR2-TABLE scenario=" + scenario + " asks=" + Bm22Count("ask") + " reports=" + Bm22Count("report") + " resets=" + Bm22Resets() + " cleared=" + cleared);
                    lock (Bm22Journal) { foreach (var entry in Bm22Journal) Console.WriteLine("PR2-JOURNAL " + entry); }
                    Check(cleared, "the document is cleared");
                    Check(Bm22Count("ask") - asksBefore == 1, "one question for the Clear (the board's numbers and the document together)");
                    Check(Bm22Resets() - resetsBefore == 1, "REST: the board is reset at once, after that question (no deferred reset)");
                    return true;
                }

                MethodInfo releaseMethod = McT.GetMethod("ReleaseLeaseIfHeld");
                if (releaseMethod != null)
                {
                    releaseMethod.Invoke(mc, null);
                }

                MethodInfo disposeMethod = CtlT.GetMethod("Dispose");
                if (disposeMethod != null)
                {
                    disposeMethod.Invoke(ctl, null);
                }

                Thread.Sleep(1500);

                int sto = 0;
                List<Dictionary<string, object>> reqs = Requests();
                foreach (var r in reqs)
                {
                    if (Str(r, "path") == "/api/command" && Str(r, "body").Trim() == "-sto")
                    {
                        sto++;
                    }
                }

                Console.WriteLine("PR2-TABLE scenario=" + scenario + " asks=" + Bm22Count("ask") + " reports=" + Bm22Count("report") + " resets=" + Bm22Resets() + " sto=" + sto);
                lock (Bm22Journal)
                {
                    foreach (var entry in Bm22Journal)
                    {
                        Console.WriteLine("PR2-JOURNAL " + entry);
                    }
                }

                Check(Bm22Count("ask") == 0, "no second question through AppUi after the Stop question: " + Bm22Count("ask"));
                Check(Bm22Resets() == 0, "the board is not reset");
                string journalStr = string.Join(" ; ", Bm22Journal);
                Check(Bm22Count("report") == 0, "no message after the Stop question: " + journalStr);
                Check(sto == 0, "no -sto sent");

                return true;
            }
            catch (Exception ex)
            {
                Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
                return true;
            }
            finally
            {
                try
                {
                    Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
                    if (UiT != null)
                    {
                        FieldInfo probeField = UiT.GetField("Probe", BindingFlags.Public | BindingFlags.Static);
                        if (probeField != null)
                        {
                            probeField.SetValue(null, null);
                        }
                    }

                    if (windowsField != null && oldWindows != null) windowsField.SetValue(null, oldWindows);
                }
                catch { }
            }
        }

        static bool RunPr2Switch(string scenario)
        {
            if (scenario != "console-tcp-pr2-switch-no" && scenario != "console-tcp-pr2-switch-deferred-no" && !scenario.StartsWith("pr2-switch-yes"))
            {
                Check(false, "unknown scenario");
                return true;
            }

            try
            {
                Type CtlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
                Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");
                Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
                Type RdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
                Type CfgT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceConfig, BecquerelMonitor");
                Type TrT = Type.GetType("BecquerelMonitor.AtomSpectraTransport, BecquerelMonitor");

                if (CtlT == null || McT == null || UiT == null || RdT == null || CfgT == null || TrT == null)
                {
                    Console.WriteLine("n/a: the controller is not in this build");
                    return true;
                }

                object tcpCfg = Activator.CreateInstance(CfgT);
                CfgT.GetProperty("Transport").SetValue(tcpCfg, Enum.Parse(TrT, "TcpBridge"));
                CfgT.GetProperty("Host").SetValue(tcpCfg, "127.0.0.1");
                CfgT.GetProperty("TcpPort").SetValue(tcpCfg, scenario.StartsWith("pr2-switch-yes") ? cmdPort : port);

                lock (Bm22Journal)
                {
                    Bm22Journal.Clear();
                }

                object ctl, mc, rd;
                Bm22Make(tcpCfg, out ctl, out mc, out rd);
                if (mc == null)
                {
                    Check(false, "setup failed");
                    return true;
                }

                object deviceConfig = RdT.GetProperty("DeviceConfig").GetValue(rd);
                deviceConfig.GetType().GetProperty("NumberOfChannels").SetValue(deviceConfig, 1024);

                var tcpSession = new AtomSpectraTcpSession("127.0.0.1", scenario.StartsWith("pr2-switch-yes") ? cmdPort : port, 1000, 1000, null);
                tcpSession.Ask = q => AtomSpectraRestStartChoice.Continue;
                var outcome = tcpSession.Begin(true, 2000);
                CtlT.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctl, tcpSession);
                CtlT.GetField("resultData", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctl, rd);
                bool joined = outcome == AtomSpectraTcpBeginOutcome.Started && tcpSession.JoinedRunning;
                Check(joined, "joined");

                Thread.Sleep(1500);

                UiT.GetField("Probe").SetValue(null, new Func<string, string, string, bool>((kind, text, extra) =>
                {
                    lock (Bm22Journal)
                    {
                        Bm22Journal.Add(kind + "|" + text);
                    }
                    return kind == "ask" ? Bm22Answer : true;
                }));
                Bm22Answer = scenario.StartsWith("pr2-switch-yes");   // IMPL-11 (review 2, H4): pr2-switch-yes-* answer "Yes" and only record how many questions / reports follow

                string carriedBefore = null;
                if (scenario == "console-tcp-pr2-switch-deferred-no")
                {
                    tcpSession.Stop(1000);
                    tcpSession.RequestResetOnNextStart(0, 0);
                    try
                    {
                        CtlT.GetMethod("CloseSession", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ctl, null);
                    }
                    catch (TargetInvocationException ex)
                    {
                        if (ex.InnerException != null)
                        {
                            throw ex.InnerException;
                        }
                        throw;
                    }
                    carriedBefore = (string)CtlT.GetField("carriedResetKey", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctl);
                    Check(!string.IsNullOrEmpty(carriedBefore), "a deferred reset is carried by the controller");
                }

                CfgT.GetProperty("Transport").SetValue(tcpCfg, Enum.Parse(TrT, "Rest"));
                CfgT.GetProperty("RestUrl").SetValue(tcpCfg, scenario == "pr2-switch-yes-up" ? "http://127.0.0.1:" + port + "/" : "http://127.0.0.1:1/");

                object sessionBefore = CtlT.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctl);

                bool cleared = false;
                try
                {
                    cleared = (bool)McT.GetMethod("ConfirmAndClear").Invoke(mc, null);
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                    {
                        throw ex.InnerException;
                    }
                    throw;
                }

                object sessionAfter = CtlT.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctl);
                string carriedAfter = (string)CtlT.GetField("carriedResetKey", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctl);

                int rst = Requests().Count(r => Str(r, "cmd") == "-rst");
                int sto = Requests().Count(r => Str(r, "cmd") == "-sto");

                Console.WriteLine("PR2-SWITCH-TABLE cleared=" + cleared + " sessionKept=" + (object.ReferenceEquals(sessionBefore, sessionAfter)) + " carriedKept=" + (carriedBefore == carriedAfter));

                if (scenario.StartsWith("pr2-switch-yes"))
                {
                    // PR #39 review: after the switch to REST "Clear" asks ONE question too - the board's numbers with the document when the REST board
                    // is readable (it was the plain question and then the board's: two questions, and "Yes, No" cleared the document only)
                    bool up = scenario == "pr2-switch-yes-up";
                    Console.WriteLine("PR2-SWITCH-YES board=" + (up ? "up" : "down") + " asks=" + Bm22Count("ask") + " reports=" + Bm22Count("report") + " cleared=" + cleared + " resets=" + Bm22Resets() + " tcpSessionClosed=" + !object.ReferenceEquals(sessionBefore, sessionAfter));
                    Check(cleared && Bm22Count("ask") == 1, "Yes: one question " + (up ? "(the document and the board together)" : "(the document: the board cannot be read)") + ", the document is cleared: " + Bm22Count("ask"));
                    if (up)
                    {
                        string bothFmt = (string)Type.GetType("BecquerelMonitor.Properties.Resources, BecquerelMonitor").GetProperty("MSGAtomSpectraRestClearBothConfirm").GetValue(null);
                        string pat = "^" + System.Text.RegularExpressions.Regex.Escape(bothFmt).Replace("\\{0}", "\\d+").Replace("\\{1}", "\\d+").Replace("\\{2}", "\\d+") + "$";
                        string firstAsk;
                        lock (Bm22Journal) { firstAsk = Bm22Journal.FirstOrDefault(e => e.StartsWith("ask|")); }
                        Check(firstAsk != null && System.Text.RegularExpressions.Regex.IsMatch(firstAsk.Substring(4), pat), "the one question is MSGAtomSpectraRestClearBothConfirm with the REST board's numbers: " + firstAsk);
                    }
                    Check(Bm22Resets() == (up ? 1 : 0) && !object.ReferenceEquals(sessionBefore, sessionAfter), "Yes: the TCP session is replaced, the board is reset " + (up ? "once" : "not (it cannot be read)"));
                }
                Check(!cleared || scenario.StartsWith("pr2-switch-yes"), "No: the document is not cleared");

                if (scenario == "console-tcp-pr2-switch-no")
                {
                    Check(object.ReferenceEquals(sessionAfter, sessionBefore) && sessionBefore != null, "No: the TCP session is still the controller's session (not closed by the question)");
                    Check(tcpSession.IsOpen, "No: the TCP session is not disposed");
                }
                else if (scenario == "console-tcp-pr2-switch-deferred-no")
                {
                    Check(carriedAfter == carriedBefore, "No: the deferred reset is still carried (not dropped by the question)");
                }

                Check(rst == 0 && sto == 0, "No: nothing was sent to the board");

                try
                {
                    tcpSession.Dispose();
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
                if (UiT != null)
                {
                    UiT.GetField("Probe").SetValue(null, null);
                }
            }

            return true;
        }
        static bool RunPr2Tcp(string scenario)
        {
            if (scenario != "console-tcp-pr2-clear-joined" && scenario != "console-tcp-pr2-clear-joined-no" && scenario != "console-tcp-pr2-clear-deferred")
            {
                Check(false, "unknown scenario");
                return true;
            }

            try
            {
                Type CtlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
                Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");
                Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
                Type RdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
                Type CfgT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceConfig, BecquerelMonitor");
                Type TrT = Type.GetType("BecquerelMonitor.AtomSpectraTransport, BecquerelMonitor");

                if (CtlT == null || McT == null || UiT == null || RdT == null || CfgT == null || TrT == null)
                {
                    Console.WriteLine("n/a: the controller is not in this build");
                    return true;
                }

                object tcpCfg = Activator.CreateInstance(CfgT);
                CfgT.GetProperty("Transport").SetValue(tcpCfg, Enum.Parse(TrT, "TcpBridge"));
                CfgT.GetProperty("Host").SetValue(tcpCfg, "127.0.0.1");
                CfgT.GetProperty("TcpPort").SetValue(tcpCfg, port);

                lock (Bm22Journal)
                {
                    Bm22Journal.Clear();
                }

                object ctl, mc, rd;
                Bm22Make(tcpCfg, out ctl, out mc, out rd);
                if (mc == null)
                {
                    Check(false, "setup failed");
                    return true;
                }

                object deviceConfig = RdT.GetProperty("DeviceConfig").GetValue(rd);
                deviceConfig.GetType().GetProperty("NumberOfChannels").SetValue(deviceConfig, 1024);

                // joined the counting bridge the way fix-ctl-clear does: a session with its own Start answer "Continue", handed to the controller
                var tcpSession = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
                tcpSession.Ask = q => AtomSpectraRestStartChoice.Continue;
                var outcome = tcpSession.Begin(true, 2000);
                CtlT.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctl, tcpSession);
                CtlT.GetField("resultData", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctl, rd);
                bool joined = outcome == AtomSpectraTcpBeginOutcome.Started && tcpSession.JoinedRunning;
                Check(joined, "joined");

                Thread.Sleep(1500);

                UiT.GetField("Probe").SetValue(null, new Func<string, string, string, bool>((kind, text, extra) =>
                {
                    lock (Bm22Journal)
                    {
                        Bm22Journal.Add(kind + "|" + text);
                    }
                    return kind == "ask" ? Bm22Answer : true;
                }));
                Bm22Answer = !scenario.EndsWith("-no");
                bool deferred = scenario.EndsWith("-deferred");
                if (deferred) tcpSession.Stop(1000);   // Stop of a joined acquisition without a question: detached, the board counts on; Clear is deferred now

                bool cleared = false;
                try
                {
                    cleared = (bool)McT.GetMethod("ConfirmAndClear").Invoke(mc, null);
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                    {
                        throw ex.InnerException;
                    }
                    throw;
                }

                int rst = Requests().Count(r => Str(r, "cmd") == "-rst");
                int asks = Bm22Count("ask");
                int reports = Bm22Count("report");

                Console.WriteLine("PR2-TCP-TABLE asks=" + asks + " reports=" + reports + " rst=" + rst + " cleared=" + cleared);
                lock (Bm22Journal)
                {
                    foreach (string line in Bm22Journal)
                    {
                        Console.WriteLine("PR2-TCP-JOURNAL " + line);
                    }
                }

                string firstAsk = null;
                lock (Bm22Journal) { firstAsk = Bm22Journal.FirstOrDefault(e => e.StartsWith("ask|")); }
                Check(asks == 1, "ONE question for the Clear of a joined TCP acquisition (the document and the analyzer together), asked: " + asks);
                if (scenario.EndsWith("-no"))
                {
                    Check(!cleared && rst == 0, "No: neither the document nor the analyzer is cleared (cleared " + cleared + ", -rst " + rst + ")");
                }
                else if (deferred)
                {
                    Check(cleared && rst == 0, "deferred: the document is cleared, nothing is sent now (cleared " + cleared + ", -rst " + rst + ")");
                    Check(firstAsk != null && firstAsk.Contains("-rst") && (firstAsk.Contains("Start") || firstAsk.Contains("Старт")), "the one question says that -rst goes at the next Start: " + firstAsk);
                }
                else
                {
                    Check(cleared, "the document is cleared after Yes");
                    Check(rst == 1, "-rst was sent once, after that question");
                    Check(firstAsk != null && firstAsk.Contains("-rst") && !firstAsk.Contains("Start") && !firstAsk.Contains("Старт"), "the one question speaks of the document and the analyzer together: " + firstAsk);
                    // PR2 review 2, H1: the WHOLE text of MSGAtomSpectraTcpClearJoinedBothConfirm (document AND analyzer; the numbers are any), not only "-rst"
                    string bothFmt = (string)Type.GetType("BecquerelMonitor.Properties.Resources, BecquerelMonitor").GetProperty("MSGAtomSpectraTcpClearJoinedBothConfirm").GetValue(null);
                    string pat = "^" + System.Text.RegularExpressions.Regex.Escape(bothFmt).Replace("\\{0}", "\\d+").Replace("\\{1}", "\\d+").Replace("\\{2}", "\\d+") + "$";
                    Check(firstAsk != null && System.Text.RegularExpressions.Regex.IsMatch(firstAsk.Substring(firstAsk.IndexOf('|') + 1), pat), "the question is the text of MSGAtomSpectraTcpClearJoinedBothConfirm (the document AND the analyzer): " + firstAsk);
                }
            }
            catch (Exception ex)
            {
                Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
                if (UiT != null)
                {
                    UiT.GetField("Probe").SetValue(null, null);
                }
            }

            return true;
        }
    }
}
