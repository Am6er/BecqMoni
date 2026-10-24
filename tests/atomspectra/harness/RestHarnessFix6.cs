using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix6(string scenario)
        {
            switch (scenario)
            {
                case "bm10-schedule":
                {
                    Assembly asm = typeof(BecquerelMonitor.AtomSpectraDeviceConsole).Assembly;
                    Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    if (t == null)
                    {
                        Check(false, "AtomSpectraDnsSd is missing");
                        return true;
                    }

                    FieldInfo fSched = t.GetField("QueryScheduleMs", BindingFlags.Public | BindingFlags.Static);
                    if (fSched == null)
                    {
                        Check(false, "AtomSpectraDnsSd.QueryScheduleMs (repeat schedule) exists");
                        return true;
                    }
                    Check(true, "AtomSpectraDnsSd.QueryScheduleMs (repeat schedule) exists");

                    FieldInfo fEndpoint = t.GetField("QueryEndpoint", BindingFlags.Public | BindingFlags.Static);
                    if (fEndpoint == null)
                    {
                        Check(false, "QueryEndpoint exists");
                        return true;
                    }
                    fEndpoint.SetValue(null, new IPEndPoint(IPAddress.Loopback, port));

                    FieldInfo fBrowseTimeout = t.GetField("BrowseTimeoutMs", BindingFlags.Public | BindingFlags.Static);
                    if (fBrowseTimeout == null)
                    {
                        Check(false, "BrowseTimeoutMs exists");
                        return true;
                    }
                    fBrowseTimeout.SetValue(null, 8000);

                    FieldInfo fFollowUp = t.GetField("FollowUpTimeoutMs", BindingFlags.Public | BindingFlags.Static);
                    if (fFollowUp == null)
                    {
                        Check(false, "FollowUpTimeoutMs exists");
                        return true;
                    }
                    fFollowUp.SetValue(null, 500);

                    FieldInfo fProxy = t.GetField("UseSystemProxy", BindingFlags.Public | BindingFlags.Static);
                    if (fProxy == null)
                    {
                        Check(false, "UseSystemProxy exists");
                        return true;
                    }
                    fProxy.SetValue(null, false);

                    FieldInfo fLocal = t.GetField("LocalAddresses", BindingFlags.Public | BindingFlags.Static);
                    if (fLocal != null)
                    {
                        fLocal.SetValue(null, null);
                    }

                    IPEndPoint ep = (IPEndPoint)fEndpoint.GetValue(null);
                    if (ep == null || ep.Address != IPAddress.Loopback || ep.Port != port)
                    {
                        Check(false, "the browse would leave the machine");
                        return true;
                    }

                    MethodInfo browse = t.GetMethod("Browse", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (browse == null)
                    {
                        Check(false, "Browse() exists");
                        return true;
                    }

                    var sw = Stopwatch.StartNew();
                    System.Collections.IList list = null;
                    try
                    {
                        list = (System.Collections.IList)browse.Invoke(null, null);
                    }
                    catch (TargetInvocationException ex)
                    {
                        Check(false, "Browse threw: " + ex.InnerException.Message);
                        return true;
                    }
                    sw.Stop();

                    Check(list != null && list.Count == 2, "both boards are found although one answers 5 s late (found " + (list == null ? 0 : list.Count) + ")");

                    int dns = 0;
                    foreach (string line in MockLog())
                    {
                        if (line.Contains("\"kind\": \"dns\""))
                            dns++;
                    }
                    Check(dns >= 3, "the PTR query is repeated on the schedule (queries seen by the responder: " + dns + ")");

                    Check(sw.ElapsedMilliseconds < 12000, "the browse ends by itself after the early exit or the window (" + sw.ElapsedMilliseconds + " ms)");

                    return true;
                }
                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}
