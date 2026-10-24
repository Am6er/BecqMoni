// "Refresh" finds every AtomSpectra board on the network with DNS-SD (mDNS), not one name.
// Browse _atomspectra._tcp (fw >= 1.2.39: port 8234, TXT sn, fw, rest, proto) and, for older boards, _http._tcp
// instances "AtomSpectra Gateway*" + GET /api/device "serial". The address of a board comes ONLY from the chain
// PTR -> SRV (host, port) -> A of that host: with two boards the "-2" suffix of the instance and of the host
// may belong to DIFFERENT boards, so no address is ever derived from a name.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BecquerelMonitor
{
    public sealed class AtomSpectraBoardEntry
    {
        public string Address;
        public string HostName;
        public string Instance;
        public string Serial;
        public int HttpPort;
        public int BridgePort;
        public string Source;

        public override string ToString()
        {
            return Address + " SN " + (Serial ?? "?") + " (" + HostName + ")";
        }
    }

    public sealed class AtomSpectraDnsRecord
    {
        public string Name;
        public int Type;
        public string Target;
        public int Port;
        public string Address;
        public Dictionary<string, string> Txt;
    }

    public static partial class AtomSpectraDnsSd
    {
        public const string ServiceAtomSpectra = "_atomspectra._tcp.local";
        public const string ServiceHttp = "_http._tcp.local";
        public const string GatewayInstancePrefix = "AtomSpectra Gateway";
        public const int TypeA = 1;
        public const int TypePtr = 12;
        public const int TypeTxt = 16;
        public const int TypeSrv = 33;

        public static IPEndPoint QueryEndpoint = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        // the whole window of the PTR browse; the boards answer after a random delay (seen up to ~5 s), the query is repeated
        // on QueryScheduleMs and the browse leaves early only when EarlyExitBoards boards are known and nothing new came for QuietExitMs
        public static int BrowseTimeoutMs = 8000;
        public static int FollowUpTimeoutMs = 800;
        public static int SerialTimeoutMs = 1500;
        public static int SerialTotalMs = 3000;   // one budget for all GET /api/device of one browse (not SerialTimeoutMs for each)
        public const int MaxInstances = 16;   // instances taken per service: a flood of answers does not widen the work
        public static bool UseSystemProxy = true;
        public static Func<string, int, string> ReadSerial = DefaultReadSerial;

        public static byte[] BuildQuery(ushort id, IList<KeyValuePair<string, int>> questions)
        {
            var ms = new MemoryStream();
            // Header (DNS is big-endian; BinaryWriter would write little-endian)
            WriteU16(ms, id);
            WriteU16(ms, 0); // flags
            WriteU16(ms, questions.Count); // qdcount
            WriteU16(ms, 0); // ancount
            WriteU16(ms, 0); // nscount
            WriteU16(ms, 0); // arcount

            // Questions
            foreach (var q in questions)
            {
                WriteName(ms, q.Key, q.Value == TypeSrv || q.Value == TypeTxt);
                WriteU16(ms, q.Value); // qtype
                WriteU16(ms, 0x8001); // qclass (IN + QU bit)
            }
            return ms.ToArray();
        }

        private static void WriteU16(Stream s, int v)
        {
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static void WriteName(Stream s, string name, bool instance)
        {
            if (string.IsNullOrEmpty(name))
            {
                s.WriteByte(0);
                return;
            }

            var labels = new List<string>();
            foreach (string service in new[] { ServiceAtomSpectra, ServiceHttp })
            {
                // the instance of a service ("Name.with.dots._http._tcp.local") is its first label as it is, not cut at its dots
                if (instance && name.Length > service.Length + 1 && name.EndsWith("." + service, StringComparison.OrdinalIgnoreCase))
                {
                    labels.Add(name.Substring(0, name.Length - service.Length - 1));
                    name = service;
                    break;
                }
            }
            labels.AddRange(name.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var label in labels)
            {
                var bytes = Encoding.UTF8.GetBytes(label);
                if (bytes.Length > 63)
                {
                    throw new ArgumentException("Label too long");
                }
                s.WriteByte((byte)bytes.Length);
                s.Write(bytes, 0, bytes.Length);
            }
            s.WriteByte(0);
        }

        public static List<AtomSpectraDnsRecord> ParseRecords(byte[] packet, int length)
        {
            var records = new List<AtomSpectraDnsRecord>();
            if (packet == null || length < 12)
            {
                return records;
            }

            try
            {
                // header: id, flags, qdcount (4-5), ancount (6-7), nscount (8-9), arcount (10-11)
                int qdcount = (packet[4] << 8) | packet[5];
                int ancount = (packet[6] << 8) | packet[7];
                int nscount = (packet[8] << 8) | packet[9];
                int arcount = (packet[10] << 8) | packet[11];
                int offset = 12;
                for (int i = 0; i < qdcount; i++)   // skip the questions
                {
                    ReadName(packet, length, ref offset);
                    offset += 4; // type + class
                }

                int totalRecords = ancount + nscount + arcount;
                for (int i = 0; i < totalRecords; i++)
                {
                    if (offset >= length)
                    {
                        break;
                    }

                    string name = ReadName(packet, length, ref offset);
                    if (offset + 10 > length)
                    {
                        break;
                    }

                    int type = (packet[offset] << 8) | packet[offset + 1];
                    offset += 2;
                    offset += 2; // class
                    offset += 4; // ttl

                    if (offset + 2 > length)
                    {
                        break;
                    }

                    int rdlength = (packet[offset] << 8) | packet[offset + 1];
                    offset += 2;

                    if (offset + rdlength > length)
                    {
                        break;
                    }

                    var rec = new AtomSpectraDnsRecord
                    {
                        Name = name.ToLowerInvariant(),
                        Type = type
                    };

                    if (type == TypeA && rdlength == 4)
                    {
                        rec.Address = packet[offset].ToString(CultureInfo.InvariantCulture) + "." +
                                      packet[offset + 1].ToString(CultureInfo.InvariantCulture) + "." +
                                      packet[offset + 2].ToString(CultureInfo.InvariantCulture) + "." +
                                      packet[offset + 3].ToString(CultureInfo.InvariantCulture);
                    }
                    else if (type == TypePtr)
                    {
                        int targetOffset = offset;
                        rec.Target = ReadName(packet, length, ref targetOffset);
                        // PTR Target keeps case
                    }
                    else if (type == TypeSrv)
                    {
                        if (rdlength >= 6)
                        {
                            // priority (2), weight (2), port (2), target; `offset` stays at the rdata start (+= rdlength below)
                            rec.Port = (packet[offset + 4] << 8) | packet[offset + 5];
                            int targetOffset = offset + 6;
                            rec.Target = ReadName(packet, length, ref targetOffset).ToLowerInvariant();
                        }
                    }
                    else if (type == TypeTxt)
                    {
                        rec.Txt = new Dictionary<string, string>(StringComparer.Ordinal);
                        int txtOffset = offset;
                        while (txtOffset < offset + rdlength)
                        {
                            int len = packet[txtOffset];
                            txtOffset++;
                            if (txtOffset + len > offset + rdlength)
                            {
                                break;
                            }
                            string val = Encoding.UTF8.GetString(packet, txtOffset, len);
                            txtOffset += len;

                            string key;
                            string value;
                            int eqIdx = val.IndexOf('=');
                            if (eqIdx > 0)
                            {
                                key = val.Substring(0, eqIdx).ToLowerInvariant();
                                value = val.Substring(eqIdx + 1);
                            }
                            else
                            {
                                key = val.ToLowerInvariant();
                                value = "";
                            }

                            if (!rec.Txt.ContainsKey(key))
                            {
                                rec.Txt[key] = value;
                            }
                        }
                    }

                    records.Add(rec);
                    offset += rdlength;
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Stop parsing
            }
            catch (ArgumentException)
            {
                // Stop parsing
            }

            return records;
        }

        // Longest name of RFC 1035, dots included.
        private const int MaxNameLength = 255;

        // Compression pointers followed in one name, in total.
        private const int MaxNameJumps = 64;

        // The work for one name is bounded by the jump count and the length limit, so a hostile packet cannot make it deep or slow.
        private static string ReadName(byte[] p, int length, ref int offset)
        {
            var sb = new StringBuilder();
            int pos = offset;
            int callerOffset = -1;
            int jumps = 0;

            while (true)
            {
                if (pos >= length)
                {
                    throw new IndexOutOfRangeException();
                }

                int len = p[pos];

                if ((len & 0xC0) == 0xC0)
                {
                    if (pos + 1 >= length)
                    {
                        throw new IndexOutOfRangeException();
                    }

                    jumps++;
                    if (jumps > MaxNameJumps)
                    {
                        throw new ArgumentException("Too many compression jumps");
                    }

                    int pointer = ((len & 0x3F) << 8) | p[pos + 1];

                    if (callerOffset < 0)
                    {
                        callerOffset = pos + 2;
                    }

                    if (pointer >= pos)
                    {
                        throw new IndexOutOfRangeException();
                    }

                    pos = pointer;
                    continue;
                }
                else if (len == 0)
                {
                    offset = callerOffset >= 0 ? callerOffset : pos + 1;
                    return sb.ToString();
                }
                else if (len > 63)
                {
                    throw new ArgumentException("Bad label length");
                }
                else
                {
                    if (pos + 1 + len > length)
                    {
                        throw new IndexOutOfRangeException();
                    }

                    int total = sb.Length + (sb.Length > 0 ? 1 : 0) + len;
                    if (total > MaxNameLength)
                    {
                        throw new ArgumentException("Name too long");
                    }

                    if (sb.Length > 0)
                    {
                        sb.Append('.');
                    }

                    sb.Append(Encoding.UTF8.GetString(p, pos + 1, len));
                    pos += 1 + len;
                }
            }
        }

        private class Cache
        {
            public Dictionary<string, List<string>> Ptr = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, AtomSpectraDnsRecord> Srv = new Dictionary<string, AtomSpectraDnsRecord>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, string>> Txt = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<string>> A = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public void Add(IEnumerable<AtomSpectraDnsRecord> records)
            {
                foreach (var r in records)
                {
                    if (r.Type == TypePtr)
                    {
                        // r.Name is the service name (lowercase)
                        // r.Target is the instance name (original case)
                        if (!Ptr.ContainsKey(r.Name))
                        {
                            Ptr[r.Name] = new List<string>();
                        }
                        if (Ptr[r.Name].Count < MaxInstances && !Ptr[r.Name].Any(x => string.Equals(x, r.Target, StringComparison.OrdinalIgnoreCase)))
                        {
                            Ptr[r.Name].Add(r.Target);
                        }
                    }
                    else if (r.Type == TypeSrv)
                    {
                        // r.Name is the instance name (lowercase)
                        if (!Srv.ContainsKey(r.Name))
                        {
                            Srv[r.Name] = r;
                        }
                    }
                    else if (r.Type == TypeTxt)
                    {
                        // r.Name is the instance name (lowercase)
                        if (!Txt.ContainsKey(r.Name))
                        {
                            Txt[r.Name] = r.Txt;
                        }
                    }
                    else if (r.Type == TypeA)
                    {
                        // r.Name is the host name (lowercase)
                        if (!A.ContainsKey(r.Name))
                        {
                            A[r.Name] = new List<string>();
                        }
                        if (!string.IsNullOrEmpty(r.Address) && !A[r.Name].Contains(r.Address))
                        {
                            A[r.Name].Add(r.Address);
                        }
                    }
                }
            }
        }

        // Exchange(SocketSet, ...) and the sockets are in AtomSpectraDnsSd.Adapters.cs (one socket per active IPv4 adapter)

        public static List<AtomSpectraBoardEntry> Browse()
        {
            var entries = new List<AtomSpectraBoardEntry>();
            var cache = new Cache();

            using (var socket = OpenSockets(ChooseAddresses()))
            {
                // Step 2: Browse PTR
                var ptrQuestions = new List<KeyValuePair<string, int>>
                {
                    new KeyValuePair<string, int>(ServiceAtomSpectra, TypePtr),
                    new KeyValuePair<string, int>(ServiceHttp, TypePtr)
                };
                ExchangeScheduled(socket, ptrQuestions, BrowseTimeoutMs, cache);

                // Step 3: Identify instances
                var atomInstances = new List<string>();
                if (cache.Ptr.ContainsKey(ServiceAtomSpectra))
                {
                    atomInstances.AddRange(cache.Ptr[ServiceAtomSpectra]);
                }

                var httpInstances = new List<string>();
                if (cache.Ptr.ContainsKey(ServiceHttp))
                {
                    foreach (var inst in cache.Ptr[ServiceHttp])
                    {
                        // Check if instance label starts with GatewayInstancePrefix
                        // Instance name format: "Label._http._tcp.local"
                        // We need to extract the label part
                        string suffix = "." + ServiceHttp;
                        if (inst.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            string label = inst.Substring(0, inst.Length - suffix.Length);
                            if (label.StartsWith(GatewayInstancePrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                httpInstances.Add(inst);
                            }
                        }
                    }
                }

                // Step 4: Follow-up 1 (SRV/TXT)
                var followUp1 = new List<KeyValuePair<string, int>>();
                foreach (var inst in atomInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (!cache.Srv.ContainsKey(key))
                    {
                        followUp1.Add(new KeyValuePair<string, int>(inst, TypeSrv));
                    }
                    if (!cache.Txt.ContainsKey(key))
                    {
                        followUp1.Add(new KeyValuePair<string, int>(inst, TypeTxt));
                    }
                }
                foreach (var inst in httpInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (!cache.Srv.ContainsKey(key))
                    {
                        followUp1.Add(new KeyValuePair<string, int>(inst, TypeSrv));
                    }
                }

                if (followUp1.Count > 0)
                {
                    Exchange(socket, followUp1, FollowUpTimeoutMs, cache);
                }

                // Step 5: Follow-up 2 (A records)
                var hostsToResolve = new List<string>();
                foreach (var inst in atomInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (cache.Srv.ContainsKey(key))
                    {
                        string host = cache.Srv[key].Target;
                        if (!string.IsNullOrEmpty(host) && !cache.A.ContainsKey(host.ToLowerInvariant()))
                        {
                            if (!hostsToResolve.Contains(host))
                            {
                                hostsToResolve.Add(host);
                            }
                        }
                    }
                }
                foreach (var inst in httpInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (cache.Srv.ContainsKey(key))
                    {
                        string host = cache.Srv[key].Target;
                        if (!string.IsNullOrEmpty(host) && !cache.A.ContainsKey(host.ToLowerInvariant()))
                        {
                            if (!hostsToResolve.Contains(host))
                            {
                                hostsToResolve.Add(host);
                            }
                        }
                    }
                }

                if (hostsToResolve.Count > 0)
                {
                    var aQuestions = new List<KeyValuePair<string, int>>();
                    foreach (var host in hostsToResolve)
                    {
                        aQuestions.Add(new KeyValuePair<string, int>(host, TypeA));
                    }
                    Exchange(socket, aQuestions, FollowUpTimeoutMs, cache);
                }

                // Step 6: Build entries
                var existingAddresses = new HashSet<string>(StringComparer.Ordinal);

                // Atom instances first
                foreach (var inst in atomInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (!cache.Srv.ContainsKey(key))
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        continue;
                    }

                    var srv = cache.Srv[key];
                    string host = srv.Target;
                    if (string.IsNullOrEmpty(host) || !cache.A.ContainsKey(host.ToLowerInvariant()))
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        continue;
                    }

                    var aList = cache.A[host.ToLowerInvariant()];
                    if (aList.Count == 0)
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        continue;
                    }

                    string address = aList[0];
                    string serial = null;
                    int httpPort = 80;

                    if (cache.Txt.ContainsKey(key))
                    {
                        var txt = cache.Txt[key];
                        if (txt.ContainsKey("sn"))
                        {
                            string sn = txt["sn"];
                            if (!string.IsNullOrEmpty(sn))
                            {
                                serial = sn;
                            }
                        }
                        if (txt.ContainsKey("rest"))
                        {
                            string rest = txt["rest"];
                            int port;
                            if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port >= 1 && port <= 65535)
                            {
                                httpPort = port;
                            }
                        }
                    }

                    var entry = new AtomSpectraBoardEntry
                    {
                        Address = address,
                        HostName = host,
                        Instance = inst,
                        Serial = serial,
                        HttpPort = httpPort,
                        BridgePort = srv.Port,
                        Source = "atomspectra"
                    };
                    entries.Add(entry);
                    existingAddresses.Add(address);
                }

                // Http instances
                var serialClock = Stopwatch.StartNew();
                serialClockOfBrowse = serialClock;
                foreach (var inst in httpInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (!cache.Srv.ContainsKey(key))
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        continue;
                    }

                    var srv = cache.Srv[key];
                    string host = srv.Target;
                    if (string.IsNullOrEmpty(host) || !cache.A.ContainsKey(host.ToLowerInvariant()))
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        continue;
                    }

                    var aList = cache.A[host.ToLowerInvariant()];
                    if (aList.Count == 0)
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        continue;
                    }

                    string address = aList[0];

                    // Skip if address already exists (newer board announced both)
                    if (existingAddresses.Contains(address))
                    {
                        continue;
                    }

                    string serial = null;
                    try
                    {
                        if (serialClock.ElapsedMilliseconds < SerialTotalMs) serial = ReadSerial(address, srv.Port);
                    }
                    catch (Exception)
                    {
                        serial = null;
                    }

                    var entry = new AtomSpectraBoardEntry
                    {
                        Address = address,
                        HostName = host,
                        Instance = inst,
                        Serial = serial,
                        HttpPort = srv.Port,
                        BridgePort = 0,
                        Source = "http"
                    };
                    entries.Add(entry);
                    existingAddresses.Add(address);
                }
            }

            // Step 7: Log
            var logMsg = "found " + entries.Count + " board(s): ";
            if (entries.Count > 0)
            {
                logMsg += string.Join(", ", entries.Select(e => e.ToString()));
            }
            LogFile(logMsg);   // Trace (becqmoni.log) and the in-program log

            return entries;
        }

        [ThreadStatic] private static Stopwatch serialClockOfBrowse;

        private static string DefaultReadSerial(string address, int port)
        {
            var client = new AtomSpectraRestClient("http://" + address + ":" + port.ToString(CultureInfo.InvariantCulture) + "/")
            {
                UseSystemProxy = UseSystemProxy
            };

            Stopwatch clock = serialClockOfBrowse;   // what is left of the browse's budget, not a full timeout for each board
            int timeout = clock == null ? SerialTimeoutMs : Math.Min(SerialTimeoutMs, SerialTotalMs - (int)clock.ElapsedMilliseconds);
            if (timeout < 1) return null;
            var resp = client.Get("/api/device", timeout);
            if (!resp.Ok)
            {
                return null;
            }

            try
            {
                object obj = AtomSpectraJson.Parse(resp.Body);
                if (obj is Dictionary<string, object>)
                {
                    var d = (Dictionary<string, object>)obj;
                    return AtomSpectraJson.GetString(d, "serial");
                }
            }
            catch (FormatException)
            {
                return null;
            }

            return null;
        }
    }

    public static class AtomSpectraBoardFinder
    {
        public static Func<List<AtomSpectraBoardEntry>> Browse = AtomSpectraDnsSd.Browse;

        // empty or the default name of a board (atomspectra, atomspectra.local): the browse; any other host is the person's own choice
        public static bool IsDefaultName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            string n = name.Trim().TrimEnd('.');   // "atomspectra.local." is the same name
            return string.Equals(n, "atomspectra", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, AtomSpectraDeviceConsole.DefaultBoardName, StringComparison.OrdinalIgnoreCase);
        }

        // the name the Refresh button searches for: an empty field is the default name (the browse); an address that parses is its host;
        // any other text (a URL, a name that does not parse) goes on as typed, to the old path, never to the browse
        public static string NameToSearch(string text, int defaultPort)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return AtomSpectraDeviceConsole.DefaultBoardName;
            }

            string h;
            int p;
            if (AtomSpectraDeviceConsole.TryParseAddress(text, defaultPort, out h, out p) && Uri.CheckHostName(h) != UriHostNameType.Unknown)
            {
                return h;
            }

            return text.Trim();
        }

        public static AtomSpectraDeviceConsole.RefreshResult Refresh(string name, int httpPort, bool useProxy, int timeoutMs,
            Func<IList<AtomSpectraBoardEntry>, AtomSpectraBoardEntry> choose)
        {
            AtomSpectraDnsSd.UseSystemProxy = useProxy;

            // a name or an IP typed by the person is resolved as it is, by the old path: no browse, no choice window
            if (!IsDefaultName(name))
            {
                AtomSpectraTcpLog.Info("Refresh", "a typed address, resolving " + name + " without the DNS-SD browse");
                return AtomSpectraDeviceConsole.Refresh(name, httpPort, useProxy, timeoutMs);
            }

            List<AtomSpectraBoardEntry> boards;
            try
            {
                boards = Browse();
            }
            catch (Exception ex)
            {
                AtomSpectraTcpLog.Info("Refresh", "browse failed: " + ex.Message);
                boards = new List<AtomSpectraBoardEntry>();
            }

            if (boards == null)
            {
                boards = new List<AtomSpectraBoardEntry>();
            }

            if (boards.Count == 0)
            {
                AtomSpectraTcpLog.Info("Refresh", "no board answered the DNS-SD browse, resolving " + name);
                return AtomSpectraDeviceConsole.Refresh(name, httpPort, useProxy, timeoutMs);
            }

            AtomSpectraBoardEntry pick = boards[0];
            if (boards.Count > 1)
            {
                pick = choose != null ? choose(boards) : null;
                if (pick == null)
                {
                    AtomSpectraTcpLog.Info("Refresh", "board choice cancelled");
                    return new AtomSpectraDeviceConsole.RefreshResult
                    {
                        Found = false,
                        Cancelled = true,
                        Message = ""
                    };
                }
            }

            var r = AtomSpectraDeviceConsole.Refresh(pick.Address, pick.HttpPort > 0 ? pick.HttpPort : httpPort, useProxy, timeoutMs);
            r.FoundAs = pick.HostName;

            if (r.Found && !string.IsNullOrEmpty(r.Serial) && r.Serial == "?" && pick.Serial != null)
            {
                r.Serial = pick.Serial;
            }

            return r;
        }
    }
}
