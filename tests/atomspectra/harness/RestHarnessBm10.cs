using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Diagnostics;
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

        static object lastBrowse;   // the list object Browse returned last (BrowseFake), for its own fields (Incomplete)

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
            lastBrowse = listObj;
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

        // incomplete >= 0: the browse returns an AtomSpectraBoardList with that many instances without an address;
        // restPort != null: AtomSpectraBoardFinder.Find is called and the port it read the board on is put in restPort[0]
        static object RefreshWith(Assembly asm, List<object> entries, FakeChooser chooser, List<string> resolved, string typed = "atomspectra.local",
            int incomplete = -1, int httpPort = -1, int[] restPort = null)
        {
            Type tf = asm.GetType("BecquerelMonitor.AtomSpectraBoardFinder");
            Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
            if (tf == null || te == null)
            {
                Check(false, "AtomSpectraBoardFinder/AtomSpectraBoardEntry is missing: no board choice in this build");
                return null;
            }

            Type listType = typeof(List<>).MakeGenericType(te);
            IList list;
            if (incomplete >= 0)
            {
                Type tl = asm.GetType("BecquerelMonitor.AtomSpectraBoardList");
                if (tl == null)
                {
                    Check(false, "AtomSpectraBoardList is missing: no count of the boards without an address in this build");
                    return null;
                }
                list = (IList)Activator.CreateInstance(tl);
                tl.GetField("Incomplete").SetValue(list, incomplete);
            }
            else
            {
                list = (IList)Activator.CreateInstance(listType);
            }
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

            MethodInfo mRefresh = tf.GetMethod(restPort != null ? "Find" : "Refresh", BindingFlags.Public | BindingFlags.Static);
            if (mRefresh == null)
            {
                Check(false, "AtomSpectraBoardFinder." + (restPort != null ? "Find" : "Refresh") + " is missing");
                return null;
            }

            object[] args = restPort != null ? new object[] { typed, httpPort >= 0 ? httpPort : port, false, 2000, choose, 0 } : new object[] { typed, httpPort >= 0 ? httpPort : port, false, 2000, choose };
            object r = mRefresh.Invoke(null, args);
            if (restPort != null) restPort[0] = (int)args[5];
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
                case "bm10-refresh-typed-dead":   // the address in the field does not answer (DHCP gave the board another one): the browse looks for the boards
                {
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing: no board choice in this build");
                        return true;
                    }
                    const string Dead = "127.0.0.9";   // nothing listens there

                    var ch = new FakeChooser { WantSerial = "00000119" };
                    var res = new List<string>();
                    var r = RefreshWith(asm, new List<object> { NewEntry(te, A146, "atomspectra.local", "00000051", port), NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch, res, Dead);
                    if (r == null) return true;
                    Check(res.Count > 0 && res[0] == Dead, "the address in the field is tried first, as it is (" + string.Join(",", res) + ")");
                    Check(ch.Calls == 1 && ch.LastCount == 2, "it does not answer: the browse finds two boards, the choice is offered (calls " + ch.Calls + ", boards " + ch.LastCount + ")");
                    Check((bool)GetF(r, "Found") && S(r, "Host") == A119 && S(r, "Serial") == "00000119", "the chosen board is read at its address from the browse");

                    var ch1 = new FakeChooser { WantSerial = "x" };
                    var res1 = new List<string>();
                    var r1 = RefreshWith(asm, new List<object> { NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch1, res1, Dead);
                    if (r1 == null) return true;
                    Check(ch1.Calls == 0 && (bool)GetF(r1, "Found") && S(r1, "Host") == A119, "one board in the browse: read at its new address without a choice (calls " + ch1.Calls + ")");

                    var ch2 = new FakeChooser { WantSerial = "x" };
                    var res2 = new List<string>();
                    var r2 = RefreshWith(asm, new List<object>(), ch2, res2, Dead);
                    if (r2 == null) return true;
                    Check(res2.Contains("atomspectra.local") && (bool)GetF(r2, "Found") && S(r2, "Host") == A146, "no board in the browse either: the default name through the system resolver (" + string.Join(",", res2) + ")");

                    var ch3 = new FakeChooser { WantSerial = "00000051" };
                    var res3 = new List<string>();
                    var r3 = RefreshWith(asm, new List<object> { NewEntry(te, A146, "atomspectra.local", "00000051", port), NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch3, res3, A119);
                    if (r3 == null) return true;
                    Check(ch3.Calls == 0 && (bool)GetF(r3, "Found") && S(r3, "Host") == A119, "control: an address in the field that answers is read as it is, no browse, no choice");
                    return true;
                }
                case "bm10-same-host":   // two boards answer with ONE host name: each instance gets the A of its own packet, a board is listed once
                case "bm10-same-host-split":   // the same without A records next to SRV: the source address of the SRV packet tells the boards apart
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    object g = l.FirstOrDefault(e => (S(e, "Instance") ?? "").StartsWith("AtomSpectra Gateway.", StringComparison.OrdinalIgnoreCase));
                    object g2 = l.FirstOrDefault(e => (S(e, "Instance") ?? "").StartsWith("AtomSpectra Gateway-2.", StringComparison.OrdinalIgnoreCase));
                    Check(l.Count == 2, "two boards with one host name: two entries, the second instance of the board " + A146 + " is not a third board (found " + l.Count + ")");
                    Check(g != null && S(g, "Address") == A146 && S(g, "Serial") == "00000051", "Gateway (SN 00000051) -> " + A146 + ": " + (g == null ? "missing" : Describe(g)));
                    Check(g2 != null && S(g2, "Address") == A119 && S(g2, "Serial") == "00000119", "Gateway-2 (SN 00000119) -> " + A119 + ", not the first A of the host: " + (g2 == null ? "missing" : Describe(g2)));
                    Check(l.Select(e => S(e, "Address")).Distinct().Count() == l.Count, "each address once");
                    return true;
                }
                case "bm10-incomplete":   // an instance answered without its address: one board found is not taken silently
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    FieldInfo fi = lastBrowse == null ? null : lastBrowse.GetType().GetField("Incomplete");
                    Check(l.Count == 2 && fi != null && (int)fi.GetValue(lastBrowse) == 1, "two boards found, the third answered its PTR only: counted as without an address (found " + l.Count + ", incomplete " + (fi == null ? "n/a" : fi.GetValue(lastBrowse).ToString()) + ")");

                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    var ch = new FakeChooser { WantSerial = "00000119" };
                    var r = RefreshWith(asm, new List<object> { NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch, new List<string>(), "atomspectra.local", 1);
                    if (r == null) return true;
                    Check(ch.Calls == 1 && ch.LastCount == 1, "one board and one without an address: the choice is offered (calls " + ch.Calls + ")");
                    Check((bool)GetF(r, "Found") && S(r, "Host") == A119, "the board chosen is read");

                    var chc = new FakeChooser { WantSerial = null };
                    var rc = RefreshWith(asm, new List<object> { NewEntry(te, A146, "atomspectra.local", "00000051", port) }, chc, new List<string>(), "atomspectra.local", 1);
                    if (rc == null) return true;
                    Check(!(bool)GetF(rc, "Found") && (bool)GetF(rc, "Cancelled") && DeviceGets(A146) == 0, "the choice closed: cancelled, the one board found is not read");

                    var ch0 = new FakeChooser { WantSerial = "x" };
                    var r0 = RefreshWith(asm, new List<object> { NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, ch0, new List<string>(), "atomspectra.local", 0);
                    if (r0 == null) return true;
                    Check(ch0.Calls == 0 && (bool)GetF(r0, "Found"), "control: one board, none without an address - read without a choice");
                    return true;
                }
                case "bm10-port":   // the REST port: TXT rest when the board gives it, else the port of the form; Find returns the port it read on
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    object noRest = l.FirstOrDefault(e => S(e, "Address") == A146);
                    object withRest = l.FirstOrDefault(e => S(e, "Address") == A119);
                    Check(noRest != null && I(noRest, "HttpPort") == 0, "no TXT rest: no REST port of the board's own (" + (noRest == null ? "missing" : Describe(noRest)) + ")");
                    Check(withRest != null && I(withRest, "HttpPort") == port, "TXT rest=" + port + ": that port (" + (withRest == null ? "missing" : Describe(withRest)) + ")");

                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    int[] rp = new int[1];
                    var r = RefreshWith(asm, new List<object> { NewEntry(te, A146, "atomspectra.local", "00000051", 0) }, new FakeChooser(), new List<string>(), "atomspectra.local", -1, port, rp);
                    if (r == null) return true;
                    Check((bool)GetF(r, "Found") && rp[0] == port, "no port of the board: read on the port of the form " + port + " (found " + GetF(r, "Found") + ", port " + rp[0] + ")");

                    var r2 = RefreshWith(asm, new List<object> { NewEntry(te, A119, "atomspectra-2.local", "00000119", port) }, new FakeChooser(), new List<string>(), "atomspectra.local", -1, 1, rp);
                    if (r2 == null) return true;
                    Check((bool)GetF(r2, "Found") && rp[0] == port, "the board's port " + port + " over the form's 1: read on it, and it is returned for the field (port " + rp[0] + ")");
                    return true;
                }
                case "bm10-goodbye":   // a board withdraws its records (TTL 0) during the browse: it is not listed
                {
                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count == 1 && S(l[0], "Address") == A146, "the board that said goodbye is not in the list (found " + l.Count + ": " + string.Join("; ", l.Select(Describe)) + ")");
                    return true;
                }
                case "bm10-long-label":   // an instance read from several labels is asked for by those labels; a question that cannot be written is left out alone
                {
                    Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    MethodInfo bq = t == null ? null : t.GetMethod("BuildQuery", BindingFlags.Public | BindingFlags.Static);
                    if (bq == null)
                    {
                        Check(false, "AtomSpectraDnsSd.BuildQuery is missing");
                        return true;
                    }
                    string x40 = new string('x', 40), y40 = new string('y', 40), z70 = new string('z', 70);
                    Func<List<KeyValuePair<string, int>>, byte[]> build = qs =>
                    {
                        try { return (byte[])bq.Invoke(null, new object[] { (ushort)1, qs }); }
                        catch (TargetInvocationException ex) { Console.WriteLine("BuildQuery threw " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message); return null; }
                    };
                    Func<byte[], int, string> labelsAt = (b, offset) =>
                    {
                        var list = new List<string>();
                        while (offset < b.Length && b[offset] != 0 && offset + 1 + b[offset] <= b.Length)
                        {
                            list.Add(System.Text.Encoding.UTF8.GetString(b, offset + 1, b[offset]));
                            offset += 1 + b[offset];
                        }
                        return string.Join("|", list);
                    };

                    byte[] p1 = build(new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(x40 + "." + y40 + "._atomspectra._tcp.local", 33) });
                    Check(p1 != null && labelsAt(p1, 12) == x40 + "|" + y40 + "|_atomspectra|_tcp|local", "an instance of 81 bytes cannot be one label: it is asked for by its two labels (" + (p1 == null ? "no packet" : labelsAt(p1, 12)) + ")");

                    byte[] p2 = build(new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(z70 + "._atomspectra._tcp.local", 33), new KeyValuePair<string, int>("AtomSpectra Gateway._atomspectra._tcp.local", 16) });
                    Check(p2 != null && ((p2[4] << 8) | p2[5]) == 1 && labelsAt(p2, 12) == "AtomSpectra Gateway|_atomspectra|_tcp|local", "a label of 70 bytes: that question is left out, the other one is asked (" + (p2 == null ? "no packet" : "qdcount " + ((p2[4] << 8) | p2[5])) + ")");

                    var l = BrowseFake(asm);
                    if (l == null) return true;
                    Check(l.Count == 2 && l.Any(e => S(e, "Address") == A119), "the browse is not broken by the long instance: both boards found, the long one at " + A119 + " (found " + l.Count + ")");
                    return true;
                }
                case "bm10-no-network":   // not one query could be sent: the browse ends at once, the window is not waited out
                {
                    try
                    {
                        Fix9Addresses(() => new List<IPAddress> { IPAddress.Loopback });   // the adapter list is fixed: no real adapter is touched
                        var sw = Stopwatch.StartNew();
                        var found = Fix9Browse(3000, 0);
                        sw.Stop();
                        if (found == null) return true;
                        Check(found.Count == 0, "nothing could be sent, no board found (found " + found.Count + ")");
                        Check(sw.ElapsedMilliseconds < 1000, "the browse ends at once, not after its 3000 ms window (" + sw.ElapsedMilliseconds + " ms)");
                    }
                    finally
                    {
                        Fix9Addresses(null);
                    }
                    return true;
                }
                default:
                    return RunFix2(scenario);   // RestHarnessFix2.cs
            }
        }
    }
}
