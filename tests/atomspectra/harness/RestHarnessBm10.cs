using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        // "Refresh" finds every board with DNS-SD (fake responder mock_mdns.py on 127.0.0.1, never the network).
        // The new API is reached ONLY by reflection, so this harness compiles against a build without it and fails there by a check.

        internal static object GetF(object o, string field)
        {
            return o.GetType().GetField(field).GetValue(o);
        }

        internal static string S(object o, string f)
        {
            return (string)GetF(o, f);
        }

        internal static int I(object o, string f)
        {
            return (int)GetF(o, f);
        }

        static string Describe(object e)
        {
            return "Address=" + S(e, "Address") + " Host=" + S(e, "HostName") + " Instance=" + S(e, "Instance") + " Serial=" + (S(e, "Serial") ?? "null") + " Http=" + I(e, "HttpPort") + " Bridge=" + I(e, "BridgePort") + " Source=" + S(e, "Source");
        }

        static List<string> MockLog()
        {
            var list = new List<string>();
            try
            {
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        list.Add(line);
                    }
                }
            }
            catch
            {
                // ignore
            }
            return list;
        }

        static int DeviceGets(string host)
        {
            var lines = MockLog();
            int count = 0;
            foreach (var line in lines)
            {
                if (line.Contains("\"kind\": \"http\"") && line.Contains("\"host\": \"" + host + "\"") && line.Contains("/api/device"))
                {
                    count++;
                }
            }
            return count;
        }

        static List<object> BrowseFake(Assembly asm)
        {
            Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
            if (t == null)
            {
                Check(false, "AtomSpectraDnsSd is missing: no DNS-SD browse in this build");
                return null;
            }

            // Set static fields
            FieldInfo fEndpoint = t.GetField("QueryEndpoint", BindingFlags.Public | BindingFlags.Static);
            if (fEndpoint == null)
            {
                Check(false, "AtomSpectraDnsSd setting QueryEndpoint is missing");
                return null;
            }
            fEndpoint.SetValue(null, new IPEndPoint(IPAddress.Loopback, port));

            FieldInfo fBrowseTimeout = t.GetField("BrowseTimeoutMs", BindingFlags.Public | BindingFlags.Static);
            if (fBrowseTimeout == null)
            {
                Check(false, "AtomSpectraDnsSd setting BrowseTimeoutMs is missing");
                return null;
            }
            fBrowseTimeout.SetValue(null, 700);

            FieldInfo fFollowUpTimeout = t.GetField("FollowUpTimeoutMs", BindingFlags.Public | BindingFlags.Static);
            if (fFollowUpTimeout == null)
            {
                Check(false, "AtomSpectraDnsSd setting FollowUpTimeoutMs is missing");
                return null;
            }
            fFollowUpTimeout.SetValue(null, 500);

            FieldInfo fUseProxy = t.GetField("UseSystemProxy", BindingFlags.Public | BindingFlags.Static);
            if (fUseProxy == null)
            {
                Check(false, "AtomSpectraDnsSd setting UseSystemProxy is missing");
                return null;
            }
            fUseProxy.SetValue(null, false);

            // SAFETY: read QueryEndpoint back
            IPEndPoint ep = (IPEndPoint)fEndpoint.GetValue(null);
            if (ep == null || !ep.Address.Equals(IPAddress.Loopback) || ep.Port != port)
            {
                Check(false, "the browse would leave the machine: endpoint " + ep);
                return null;
            }

            MethodInfo mBrowse = t.GetMethod("Browse", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (mBrowse == null)
            {
                Check(false, "AtomSpectraDnsSd.Browse is missing");
                return null;
            }

            var listObj = mBrowse.Invoke(null, null);
            if (listObj == null)
            {
                return new List<object>();
            }

            var list = new List<object>();
            if (listObj is IEnumerable)
            {
                foreach (var item in (IEnumerable)listObj)
                {
                    list.Add(item);
                }
            }

            foreach (var e in list)
            {
                Console.WriteLine("entry: " + Describe(e));
            }

            return list;
        }

        class FakeChooser
        {
            public int Calls;
            public int LastCount = -1;
            public string WantSerial;

            public object Pick(object boards)
            {
                Calls++;
                var l = (IList)boards;
                LastCount = l.Count;
                if (WantSerial == null) return null;
                foreach (object e in l)
                {
                    if (Program.S(e, "Serial") == WantSerial) return e;
                }
                return null;
            }
        }

        static object NewEntry(Type te, string address, string host, string serial, int httpPort)
        {
            object o = Activator.CreateInstance(te);
            o.GetType().GetField("Address").SetValue(o, address);
            o.GetType().GetField("HostName").SetValue(o, host);
            o.GetType().GetField("Instance").SetValue(o, "AtomSpectra Gateway._atomspectra._tcp.local");
            o.GetType().GetField("Serial").SetValue(o, serial);
            o.GetType().GetField("HttpPort").SetValue(o, httpPort);
            o.GetType().GetField("BridgePort").SetValue(o, 8234);
            o.GetType().GetField("Source").SetValue(o, "atomspectra");
            return o;
        }

        static object RefreshWith(Assembly asm, List<object> entries, FakeChooser chooser, List<string> resolved, string typed = "atomspectra.local")
        {
            Type tf = asm.GetType("BecquerelMonitor.AtomSpectraBoardFinder");
            Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
            if (tf == null || te == null)
            {
                Check(false, "AtomSpectraBoardFinder/AtomSpectraBoardEntry is missing: no board choice in this build");
                return null;
            }

            Type listType = typeof(List<>).MakeGenericType(te);
            IList list = (IList)Activator.CreateInstance(listType);
            foreach (var e in entries)
            {
                list.Add(e);
            }

            // Set the static field Browse of tf
            FieldInfo fBrowse = tf.GetField("Browse", BindingFlags.Public | BindingFlags.Static);
            if (fBrowse == null)
            {
                Check(false, "AtomSpectraBoardFinder.Browse is missing");
                return null;
            }

            Type funcType = typeof(Func<>).MakeGenericType(listType);
            var lambda = Expression.Lambda(funcType, Expression.Constant(list, listType));
            fBrowse.SetValue(null, lambda.Compile());

            // The choose delegate
            Type ilist = typeof(IList<>).MakeGenericType(te);
            var p = Expression.Parameter(ilist, "boards");
            var call = Expression.Call(Expression.Constant(chooser), typeof(FakeChooser).GetMethod("Pick"), Expression.Convert(p, typeof(object)));
            Type chooseFuncType = typeof(Func<,>).MakeGenericType(ilist, te);
            Delegate choose = Expression.Lambda(chooseFuncType, Expression.Convert(call, te), p).Compile();

            // Set Resolve
            AtomSpectraDeviceConsole.Resolve = name =>
            {
                lock (resolved) resolved.Add(name);
                IPAddress ip;
                if (IPAddress.TryParse(name, out ip)) return new[] { ip };
                return new[] { IPAddress.Parse("127.0.0.2") };
            };

            MethodInfo mRefresh = tf.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Static);
            if (mRefresh == null)
            {
                Check(false, "AtomSpectraBoardFinder.Refresh is missing");
                return null;
            }

            object r = mRefresh.Invoke(null, new object[] { typed, port, false, 2000, choose });
            if (r != null)
            {
                Console.WriteLine("refresh: Found=" + GetF(r, "Found") + " Host=" + S(r, "Host") + " Serial=" + S(r, "Serial") + " Cancelled=" + GetF(r, "Cancelled"));
            }
            return r;
        }

        // #BM-10 safety for EVERY scenario: since the DNS-SD client, "Refresh" of the settings form (form-calib*) would send a
        // multicast mDNS query to the real network. Before any scenario: the finder sees no board (old one-name path, as the
        // existing tests expect) and the browse endpoint is a dead loopback port. bm10-* scenarios set their own values later.
        static void Bm10NoNetwork()
        {
            Assembly asm = typeof(AtomSpectraDeviceConsole).Assembly;
            Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd"), tf = asm.GetType("BecquerelMonitor.AtomSpectraBoardFinder"), te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
            if (t == null || tf == null || te == null) return;   // a build without DNS-SD sends nothing
            t.GetField("QueryEndpoint", BindingFlags.Public | BindingFlags.Static).SetValue(null, new IPEndPoint(IPAddress.Loopback, 9));
            Type listType = typeof(List<>).MakeGenericType(te);
            tf.GetField("Browse", BindingFlags.Public | BindingFlags.Static).SetValue(null,
                Expression.Lambda(typeof(Func<>).MakeGenericType(listType), Expression.New(listType)).Compile());
        }

        static bool Has(List<object> l, Func<object, bool> f)
        {
            return l.Any(f);
        }

        static bool RunBm10(string scenario)
        {
            const string A146 = "127.0.0.2";
            const string A119 = "127.0.0.3";
            const string APrinter = "127.0.0.4";

            Assembly asm = typeof(AtomSpectraDeviceConsole).Assembly;

            switch (scenario)
            {
                case "bm10-count":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count == 2, "two boards found by the browse (found " + l.Count + ")");
                    return true;
                }
                case "bm10-chain":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count >= 1, "at least one board found");
                    foreach (var e in l)
                    {
                        string instance = S(e, "Instance") ?? "";
                        if (instance.StartsWith("AtomSpectra Gateway-2.", StringComparison.OrdinalIgnoreCase))
                        {
                            Check(S(e, "HostName") == "atomspectra.local" && S(e, "Address") == A146, "instance Gateway-2 -> SRV host atomspectra.local -> A " + A146 + ": " + Describe(e));
                        }
                        else if (instance.StartsWith("AtomSpectra Gateway.", StringComparison.OrdinalIgnoreCase))
                        {
                            Check(S(e, "HostName") == "atomspectra-2.local" && S(e, "Address") == A119, "instance Gateway -> SRV host atomspectra-2.local -> A " + A119 + ": " + Describe(e));
                        }
                        else
                        {
                            Check(false, "unexpected instance " + instance);
                        }
                    }
                    return true;
                }
                case "bm10-txt-sn":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count >= 1, "at least one board found");
                    foreach (var e in l)
                    {
                        string instance = S(e, "Instance") ?? "";
                        if (instance.StartsWith("AtomSpectra Gateway-2.", StringComparison.OrdinalIgnoreCase))
                        {
                            Check(S(e, "Serial") == "00000051", "TXT sn of Gateway-2: " + Describe(e));
                        }
                        else if (instance.StartsWith("AtomSpectra Gateway.", StringComparison.OrdinalIgnoreCase))
                        {
                            Check(S(e, "Serial") == "00000119", "TXT sn of Gateway: " + Describe(e));
                        }
                    }
                    return true;
                }
                case "bm10-txt-rest":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count >= 1, "at least one board found");
                    foreach (var e in l)
                    {
                        Check(I(e, "HttpPort") == port && I(e, "BridgePort") == 8234, "REST port from TXT rest=" + port + ", bridge port from SRV 8234: " + Describe(e));
                    }
                    return true;
                }
                case "bm10-legacy-serial":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(Has(l, e => S(e, "Address") == A146 || S(e, "Address") == A119), "an older board found");
                    foreach (var e in l)
                    {
                        if (S(e, "Address") == A146)
                        {
                            Check(S(e, "Serial") == "00000051", "serial of " + A146 + " from GET /api/device: " + Describe(e));
                        }
                        else if (S(e, "Address") == A119)
                        {
                            Check(S(e, "Serial") == "00000119", "serial of " + A119 + " from GET /api/device: " + Describe(e));
                        }
                    }
                    return true;
                }
                case "bm10-legacy-filter":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count >= 1, "at least one board found");
                    Check(!Has(l, e => S(e, "Address") == APrinter || S(e, "Serial") == "PRINTER"), "the printer's _http._tcp instance is not a board");
                    Check(DeviceGets(APrinter) == 0, "no GET /api/device to the printer (" + DeviceGets(APrinter) + ")");
                    return true;
                }
                case "bm10-mixed":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(Has(l, e => S(e, "Address") == A146), "the 1.2.39 board " + A146 + " found");
                    Check(Has(l, e => S(e, "Address") == A119), "the older board " + A119 + " found (the one 'Refresh' missed)");
                    Check(l.Select(e => S(e, "Address")).Distinct().Count() == l.Count, "each board once (" + l.Count + " entries)");
                    Check(DeviceGets(A146) == 0, "the 1.2.39 board's serial comes from TXT, no GET /api/device to it (" + DeviceGets(A146) + ")");
                    return true;
                }
                case "bm10-one-query":   // a complete answer (PTR + SRV, TXT, A in one packet, as a board sends it) is parsed whole: no follow-up query
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count >= 1, "at least one board found");
                    int queries = MockLog().Count(x => x.Contains("\"kind\": \"dns\""));
                    Check(queries == 1, "one browse query, no follow-up (queries " + queries + ")");
                    return true;
                }
                case "bm10-followup":
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count >= 1, "boards resolved through follow-up SRV/TXT and A queries (found " + l.Count + ")");
                    return true;
                }
                case "bm10-refresh-two":
                {
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing: no board choice in this build");
                        return true;
                    }
                    var ch = new FakeChooser { WantSerial = "00000119" };
                    var res = new List<string>();
                    var r = RefreshWith(asm, new List<object> { NewEntry(te, A146, "atomspectra.local", "00000051", port), NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch, res);
                    if (r == null) return true;
                    Check(ch.Calls == 1 && ch.LastCount == 2, "two boards: the choice is offered once with both (calls " + ch.Calls + ", boards " + ch.LastCount + ")");
                    Check((bool)GetF(r, "Found") && S(r, "Host") == A119 && S(r, "Serial") == "00000119", "the chosen board is the one read");
                    return true;
                }
                case "bm10-refresh-one":
                {
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing: no board choice in this build");
                        return true;
                    }
                    var ch = new FakeChooser { WantSerial = "00000119" };
                    var res = new List<string>();
                    var r = RefreshWith(asm, new List<object> { NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch, res);
                    if (r == null) return true;
                    Check(ch.Calls == 0, "one board: no choice window (calls " + ch.Calls + ")");
                    Check((bool)GetF(r, "Found") && S(r, "Host") == A119, "one board: read as before");
                    return true;
                }
                case "bm10-refresh-none":
                {
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing: no board choice in this build");
                        return true;
                    }
                    var ch = new FakeChooser { WantSerial = "x" };
                    var res = new List<string>();
                    var r = RefreshWith(asm, new List<object>(), ch, res);
                    if (r == null) return true;
                    Check(res.Contains("atomspectra.local"), "no board in the browse: the old name path resolves atomspectra.local (" + string.Join(",", res) + ")");
                    Check((bool)GetF(r, "Found") && S(r, "Host") == A146, "the old path finds the board");
                    Check(ch.Calls == 0, "no choice without boards");
                    return true;
                }
                case "bm10-refresh-cancel":
                {
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing: no board choice in this build");
                        return true;
                    }
                    var ch = new FakeChooser { WantSerial = null };
                    var res = new List<string>();
                    var r = RefreshWith(asm, new List<object> { NewEntry(te, A146, "atomspectra.local", "00000051", port), NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch, res);
                    if (r == null) return true;
                    Check(!(bool)GetF(r, "Found") && (bool)GetF(r, "Cancelled"), "the choice closed: nothing found, cancelled");
                    Check(DeviceGets(A146) + DeviceGets(A119) == 0, "the choice closed: no board is read");
                    return true;
                }
                default:
                    return RunFix2(scenario);   // RestHarnessFix2.cs
            }
        }
    }
}
