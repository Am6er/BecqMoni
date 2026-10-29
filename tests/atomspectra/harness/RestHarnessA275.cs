// PR #39, maintainer's review (amber275): no window held over a wait window may come up inside a window of the device "Clear"
// (the question, or the report of a board that could not be read) - "The board was reset" of another document would pass for
// the answer to this reset (PR #38 review, R8). Real build only: the controller and AtomSpectraRestUi are found by reflection.
//   console-tcp-a275-held-joined  TCP, Clear of a joined acquisition: the one question
//   a275-held-rest-readfail       REST, the board cannot be read: the report, then the plain question
//   a275-held-rest-both           REST, the board is read: the one question with its numbers (held already by #38: the control)
// Each window of the Clear queues an action through AtomSpectraRestUi.AfterWait (what a board offer of another document does);
// the action must not run while that window is open, and must run once the Clear is answered (not lost). The answer is "No".
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool a275Open;
        static int a275Windows, a275RanInside, a275RanAfter;

        static bool RunA275(string scenario)
        {
            Type CtlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
            Type McT = Type.GetType("BecquerelMonitor.MeasurementController, BecquerelMonitor");
            Type UiT = Type.GetType("BecquerelMonitor.AppUi, BecquerelMonitor");
            Type UiRestT = Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor");
            Type RdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
            Type CfgT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceConfig, BecquerelMonitor");
            Type TrT = Type.GetType("BecquerelMonitor.AtomSpectraTransport, BecquerelMonitor");
            if (CtlT == null || McT == null || UiT == null || UiRestT == null || RdT == null || CfgT == null || TrT == null)
            {
                Console.WriteLine("n/a: the controller is not in this build");
                return true;
            }
            MethodInfo afterWait = UiRestT.GetMethod("AfterWait", BindingFlags.Public | BindingFlags.Static);
            FieldInfo probe = UiT.GetField("Probe", BindingFlags.Public | BindingFlags.Static);
            if (afterWait == null || probe == null)
            {
                Check(false, "AtomSpectraRestUi.AfterWait or AppUi.Probe not found");
                return true;
            }

            AtomSpectraTcpSession tcpSession = null;
            try
            {
                object cfg = Activator.CreateInstance(CfgT);
                bool tcp = scenario == "console-tcp-a275-held-joined";
                if (tcp)
                {
                    CfgT.GetProperty("Transport").SetValue(cfg, Enum.Parse(TrT, "TcpBridge"));
                    CfgT.GetProperty("Host").SetValue(cfg, "127.0.0.1");
                    CfgT.GetProperty("TcpPort").SetValue(cfg, port);
                }
                else if (scenario == "a275-held-rest-readfail" || scenario == "a275-held-rest-both")
                {
                    CfgT.GetProperty("Transport").SetValue(cfg, Enum.Parse(TrT, "Rest"));
                    CfgT.GetProperty("RestUrl").SetValue(cfg, scenario == "a275-held-rest-both" ? "http://127.0.0.1:" + port + "/" : "http://127.0.0.1:" + Bm21ClosedPort() + "/");
                }
                else
                {
                    Check(false, "unknown scenario");
                    return true;
                }

                object ctl, mc, rd;
                Bm22Make(cfg, out ctl, out mc, out rd);
                if (mc == null) { Check(false, "setup failed"); return true; }

                if (tcp)
                {
                    // joined the counting bridge as RunPr2Tcp does (a session with its own Start answer "Continue", handed to the controller)
                    var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
                    s.Ask = q => AtomSpectraRestStartChoice.Continue;
                    var outcome = s.Begin(true, 2000);
                    tcpSession = s;
                    CtlT.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctl, s);
                    CtlT.GetField("resultData", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctl, rd);
                    Check(outcome == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "joined (" + outcome + ")");
                    Thread.Sleep(1500);
                }

                a275Windows = a275RanInside = a275RanAfter = 0;
                probe.SetValue(null, new Func<string, string, string, bool>((kind, text, caption) =>
                {
                    a275Windows++;
                    a275Open = true;
                    try
                    {
                        // what a board offer of another document does: it waits for the wait window and for any window held against it
                        afterWait.Invoke(null, new object[] { new Action(() => { if (a275Open) a275RanInside++; else a275RanAfter++; }) });
                    }
                    finally
                    {
                        a275Open = false;
                    }
                    Console.WriteLine("window " + kind + ": " + text);
                    return kind == "ask" ? false : true;   // "No": nothing is cleared
                }));

                bool cleared;
                try
                {
                    cleared = (bool)McT.GetMethod("ConfirmAndClear").Invoke(mc, null);
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }

                Console.WriteLine("A275-TABLE windows=" + a275Windows + " ranInside=" + a275RanInside + " ranAfter=" + a275RanAfter + " cleared=" + cleared);
                Check(a275Windows == (scenario == "a275-held-rest-readfail" ? 2 : 1), "the windows of this Clear: " + a275Windows);
                Check(a275RanInside == 0, "no held window came up inside a window of this Clear (ran inside: " + a275RanInside + ")");
                Check(a275RanAfter == a275Windows, "every held window comes once the Clear is answered (ran after: " + a275RanAfter + " of " + a275Windows + ")");
                Check(!cleared, "No: nothing is cleared");
                if (tcp) Check(!Requests().Any(r => Str(r, "cmd") == "-rst"), "No: no -rst");
                else Check(!Requests().Any(r => Str(r, "method") == "POST" && Str(r, "path") == "/api/reset"), "No: the board is not reset");
            }
            catch (Exception ex)
            {
                Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                probe.SetValue(null, null);
                if (tcpSession != null) { try { tcpSession.Dispose(); } catch { } }
            }
            return true;
        }
    }
}
