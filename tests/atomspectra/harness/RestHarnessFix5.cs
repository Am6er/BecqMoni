using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace RestHarness
{
    static partial class Program
    {
        sealed class CaptureListener : TraceListener
        {
            public readonly List<string> Lines = new List<string>();

            public override void Write(string message)
            {
                lock (Lines)
                {
                    Lines.Add(message ?? "");
                }
            }

            public override void WriteLine(string message)
            {
                lock (Lines)
                {
                    Lines.Add(message ?? "");
                }
            }

            public string All()
            {
                lock (Lines)
                {
                    return string.Join("\n", Lines);
                }
            }
        }

        static bool RunFix5(string scenario)
        {
            switch (scenario)
            {
                case "bm10-adapters":
                {
                    Assembly asm = typeof(BecquerelMonitor.AtomSpectraDeviceConsole).Assembly;
                    Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    if (t == null)
                    {
                        Check(false, "AtomSpectraDnsSd is missing");
                        return true;
                    }

                    FieldInfo seam = t.GetField("LocalAddresses", BindingFlags.Public | BindingFlags.Static);
                    if (seam == null)
                    {
                        Check(false, "AtomSpectraDnsSd.LocalAddresses (adapter list seam) exists");
                        return true;
                    }
                    Check(true, "AtomSpectraDnsSd.LocalAddresses (adapter list seam) exists");

                    var cap = new CaptureListener();
                    Trace.Listeners.Add(cap);
                    try
                    {
                        seam.SetValue(null, (Func<IList<IPAddress>>)(() => new List<IPAddress> { IPAddress.Parse("127.0.0.2"), IPAddress.Parse("127.0.0.3") }));
                        var found = BrowseFake(asm);
                        if (found == null) return true;
                        Check(found.Count == 2, "two adapters, only the second is answered by the responder: both boards found (found " + found.Count + ")");

                        string log = cap.All();
                        Check(log.Contains("127.0.0.2") && log.Contains("127.0.0.3"), "becqmoni.log lists both adapters the query was sent from");
                        Check(log.Contains("response packets received"), "becqmoni.log has the number of response packets");
                        Check(log.Contains("found 2 board"), "becqmoni.log has the boards found");

                        lock (cap.Lines) cap.Lines.Clear();
                        seam.SetValue(null, (Func<IList<IPAddress>>)(() => new List<IPAddress> { IPAddress.Parse("127.0.0.2") }));
                        var none = BrowseFake(asm);
                        if (none == null) return true;
                        Check(none.Count == 0, "a single adapter that the responder ignores finds nothing: the query leaves from the adapter's own address (found " + none.Count + ")");
                        Check(cap.All().Contains("response packets received: 0"), "becqmoni.log says no response packet came");
                    }
                    finally
                    {
                        Trace.Listeners.Remove(cap);
                        seam.SetValue(null, null);
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
