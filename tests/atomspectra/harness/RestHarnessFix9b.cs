using System;
using System.Reflection;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix9b(string scenario)
        {
            switch (scenario)
            {
                case "fix9-reset-carry":
                case "fix9-reset-other-board":
                    bool other = scenario == "fix9-reset-other-board";
                    Type ctlT = CtlT;
                    if (ctlT == null)
                    {
                        Console.WriteLine("n/a: the controller is not in this build");
                        return true;
                    }

                    BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
                    MethodInfo open = ctlT.GetMethod("OpenSession", NP);
                    MethodInfo close = ctlT.GetMethod("CloseSession", NP);
                    FieldInfo fSession = ctlT.GetField("session", NP);

                    Check(open != null && close != null && fSession != null, "the controller has OpenSession, CloseSession and the session field");
                    if (open == null || close == null || fSession == null)
                    {
                        return true;
                    }

                    Func<string, int, object> cfg = (host, port) =>
                    {
                        var c = new BecquerelMonitor.AtomSpectraTcpDeviceConfig();
                        c.Host = host;
                        c.TcpPort = port;
                        return c;
                    };

                    try
                    {
                        object ctl = Activator.CreateInstance(ctlT);
                        open.Invoke(ctl, new object[] { cfg("Board-A.local", 8234) });
                        var s1 = (BecquerelMonitor.AtomSpectraTcpSession)fSession.GetValue(ctl);
                        Check(s1 != null, "the first session is created");
                        if (s1 == null)
                        {
                            return true;
                        }

                        s1.Clear(1000);
                        Check(s1.ResetOnNextStart, "Clear without an open acquisition leaves the reset for the next Start");

                        close.Invoke(ctl, null);
                        open.Invoke(ctl, new object[] { other ? cfg("board-b.local", 8234) : cfg("board-a.LOCAL", 8234) });
                        var s2 = (BecquerelMonitor.AtomSpectraTcpSession)fSession.GetValue(ctl);

                        Check(s2 != null && !ReferenceEquals(s1, s2), "the session was closed and a new one made");

                        if (other)
                        {
                            Check(s2 != null && !s2.ResetOnNextStart, "another host: the deferred reset is dropped, the new session does not reset that board");
                        }
                        else
                        {
                            Check(s2 != null && s2.ResetOnNextStart, "the same board (the host in another case): the pending reset is not lost");
                        }

                        if (s2 != null)
                        {
                            s2.Dispose();
                        }

                        if (other)
                        {
                            object ctl2 = Activator.CreateInstance(ctlT);
                            open.Invoke(ctl2, new object[] { cfg("board-a.local", 8234) });
                            var sp = (BecquerelMonitor.AtomSpectraTcpSession)fSession.GetValue(ctl2);
                            if (sp != null)
                            {
                                sp.Clear(1000);
                            }

                            open.Invoke(ctl2, new object[] { cfg("board-a.local", 8235) });
                            var s3 = (BecquerelMonitor.AtomSpectraTcpSession)fSession.GetValue(ctl2);
                            Check(s3 != null && !s3.ResetOnNextStart, "another port: the deferred reset is dropped");

                            if (s3 != null)
                            {
                                s3.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Check(false, "exception: " + ex.GetType().Name + " " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                    }

                    return true;
                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}
