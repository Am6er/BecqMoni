using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;

namespace RestHarness
{
    static partial class Program
    {
        static void SetStatic(Type t, string name, object value)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f == null)
            {
                Check(false, name + " is missing");
                throw new InvalidOperationException(name + " is missing");
            }
            f.SetValue(null, value);
        }

        static List<object> Fix9Browse(int windowMs, int endpointPort)
        {
            Assembly asm = typeof(BecquerelMonitor.AtomSpectraDeviceConsole).Assembly;
            Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
            if (t == null)
            {
                Check(false, "AtomSpectraDnsSd is missing");
                return null;
            }

            // endpointPort 0: the destination is 0.0.0.0:9, every SendTo throws WSAEADDRNOTAVAIL (10049) and nothing is put on the wire
            SetStatic(t, "QueryEndpoint", new IPEndPoint(endpointPort == 0 ? IPAddress.Any : IPAddress.Loopback, endpointPort == 0 ? 9 : endpointPort));
            SetStatic(t, "BrowseTimeoutMs", windowMs);
            SetStatic(t, "FollowUpTimeoutMs", 500);
            SetStatic(t, "UseSystemProxy", false);
            SetStatic(t, "ReadSerial", (Func<string, int, string>)((a, p) => null));

            FieldInfo epField = t.GetField("QueryEndpoint", BindingFlags.Public | BindingFlags.Static);
            if (epField != null)
            {
                IPEndPoint ep = epField.GetValue(null) as IPEndPoint;
                if (ep == null || (!ep.Address.Equals(IPAddress.Loopback) && !ep.Address.Equals(IPAddress.Any)))
                {
                    Check(false, "the browse would leave the machine");
                    return null;
                }
            }

            MethodInfo browse = t.GetMethod("Browse", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (browse == null)
            {
                Check(false, "Browse method is missing");
                return null;
            }

            object result;
            try
            {
                result = browse.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                Check(false, "Browse threw: " + ex.InnerException.Message);
                return null;
            }

            IEnumerable enumerable = result as IEnumerable;
            if (enumerable == null)
            {
                return new List<object>();
            }

            List<object> list = new List<object>();
            foreach (object e in enumerable)
            {
                list.Add(e);
                Console.WriteLine("entry: " + Describe(e));
            }
            return list;
        }

        static void Fix9Addresses(Func<IList<IPAddress>> seamValue)
        {
            Assembly asm = typeof(BecquerelMonitor.AtomSpectraDeviceConsole).Assembly;
            Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
            if (t == null) return;
            SetStatic(t, "LocalAddresses", seamValue);
        }

        static bool RunFix9(string scenario)
        {
            switch (scenario)
            {
                case "bm10-quiet-three":
                {
                    var found = Fix9Browse(8000, port);
                    if (found == null) return true;
                    Check(found.Count == 3, "all three boards are found although the third answers 3.5 s late (found " + found.Count + ")");
                    bool hasLate = found.Any(e => S(e, "Address") == "127.0.0.4");
                    Check(hasLate, "the late third board 127.0.0.4 is in the list");
                    return true;
                }
                case "bm10-count-address":
                {
                    var found = Fix9Browse(8000, port);
                    if (found == null) return true;
                    Check(found.Count == 2, "one board with two labels is not counted twice: the late board .119 is found (found " + found.Count + ")");
                    bool hasLate = found.Any(e => S(e, "Address") == "127.0.0.3");
                    Check(hasLate, "the late board 127.0.0.3 is in the list");
                    return true;
                }
                case "bm10-bind-fallback":
                {
                    var cap = new CaptureListener();
                    Trace.Listeners.Add(cap);
                    try
                    {
                        Fix9Addresses(() => new List<IPAddress> { IPAddress.Parse("203.0.113.1") });
                        var found = Fix9Browse(700, port);
                        if (found == null) return true;
                        Check(found.Count == 2, "every adapter bind failed: the query still goes out from the any-address socket, both boards found (found " + found.Count + ")");
                        int queries = MockLog().Count(x => x.Contains("\"kind\": \"dns\""));
                        Check(queries >= 1, "the responder received the query (" + queries + ")");
                        string log = cap.All();
                        Check(log.Contains("0.0.0.0"), "becqmoni.log names the fallback address 0.0.0.0");
                    }
                    finally
                    {
                        Trace.Listeners.Remove(cap);
                        Fix9Addresses(null);
                    }
                    return true;
                }
                case "bm10-log-sent":
                {
                    var cap = new CaptureListener();
                    Trace.Listeners.Add(cap);
                    try
                    {
                        Fix9Addresses(() => new List<IPAddress> { IPAddress.Loopback });   // the adapter list is fixed: no real adapter is touched
                        var found = Fix9Browse(700, 0);
                        if (found == null) return true;
                        string log = cap.All();
                        Console.WriteLine("log: " + log.Replace("\n", " | "));
                        Check(found.Count == 0, "nothing could be sent, no board found (found " + found.Count + ")");
                        Check(!log.Contains("sent the PTR query from 127.0.0.1"), "the log does not claim that the query was sent from 127.0.0.1 when every send failed");
                        Check(log.Contains("sent the PTR query from no adapter"), "the log says the query was sent from no adapter");
                        Check(log.Contains("send errors: 1"), "the log counts the failed send");
                    }
                    finally
                    {
                        Trace.Listeners.Remove(cap);
                        Fix9Addresses(null);
                    }
                    return true;
                }
                case "bm10-log-packets":
                {
                    var cap = new CaptureListener();
                    Trace.Listeners.Add(cap);
                    try
                    {
                        Fix9Addresses(null);
                        var found = Fix9Browse(700, port);
                        if (found == null) return true;
                        string log = cap.All();
                        Console.WriteLine("log: " + log.Replace("\n", " | "));
                        Check(found.Count == 2, "both boards found (found " + found.Count + ")");
                        Check(log.Contains("response packets received: 2;"), "the PTR answers are 2 packets (the follow-up answers are not mixed in)");
                        Check(log.Contains("follow-up packets: 4"), "the follow-up answers (SRV/TXT and A of two boards) are counted separately");
                    }
                    finally
                    {
                        Trace.Listeners.Remove(cap);
                    }
                    return true;
                }
                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}
