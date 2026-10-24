// "Refresh" finds every AtomSpectra board on the network with DNS-SD (mDNS), not one name.
// Browse _atomspectra._tcp (fw >= 1.2.39: port 8234, TXT sn, fw, rest, proto) and, for older boards, _http._tcp
// instances "AtomSpectra Gateway*" + GET /api/device "serial". The address of a board comes ONLY from the chain
// PTR -> SRV (host, port) -> A of that host: with two boards the "-2" suffix of the instance and of the host
// may belong to DIFFERENT boards, so no address is ever derived from a name. Two boards may even answer with ONE host
// name (the name conflict not resolved): the address of an instance is then the A record of the packet that brought its
// SRV, or the source of that packet (AddressOf), never just the first A of the host.

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

    // The boards of one browse; Incomplete: instances that answered but whose address did not come (or is not known for
    // sure), so they are not in the list. The choice is then offered even for one board: nothing is taken silently.
    public sealed class AtomSpectraBoardList : List<AtomSpectraBoardEntry>
    {
        public int Incomplete;
    }

    public sealed class AtomSpectraDnsRecord
    {
        public string Name;
        public int Type;
        public uint Ttl;   // 0: the record is withdrawn (an mDNS goodbye, RFC 6762 10.1)
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
        // the window of one follow-up (SRV/TXT, then A); it ends as soon as every question is answered, so it is long enough
        // for the same delay of the boards as the PTR browse (QuietExitMs) and costs nothing when the answers come in time
        public static int FollowUpTimeoutMs = 5500;
        public static int SerialTimeoutMs = 1500;
        public static int SerialTotalMs = 3000;   // one budget for all GET /api/device of one browse (not SerialTimeoutMs for each)
        public const int MaxInstances = 16;   // instances taken per service: a flood of answers does not widen the work
        public static bool UseSystemProxy = true;
        // the proxy choice of the search running on this thread (AtomSpectraBoardFinder.Find); two searches at once (another
        // configuration loaded while the first one runs) do not take each other's. null: UseSystemProxy.
        [ThreadStatic] internal static bool? UseSystemProxyOfThread;
        public static Func<string, int, string> ReadSerial = DefaultReadSerial;

        // A question whose name cannot be written (a label over 63 bytes) is left out, not the whole query; null when none is left.
        public static byte[] BuildQuery(ushort id, IList<KeyValuePair<string, int>> questions)
        {
            var body = new MemoryStream();
            int written = 0;
            foreach (var q in questions)
            {
                var one = new MemoryStream();
                try
                {
                    WriteName(one, q.Key, q.Value == TypeSrv || q.Value == TypeTxt);
                }
                catch (ArgumentException ex)
                {
                    LogFile("question " + q.Key + " (type " + q.Value.ToString(CultureInfo.InvariantCulture) + ") left out: " + ex.Message);
                    continue;
                }
                WriteU16(one, q.Value); // qtype
                WriteU16(one, 0x8001); // qclass (IN + QU bit)
                one.WriteTo(body);
                written++;
            }
            if (written == 0)
            {
                return null;
            }

            var ms = new MemoryStream();
            // Header (DNS is big-endian; BinaryWriter would write little-endian)
            WriteU16(ms, id);
            WriteU16(ms, 0); // flags
            WriteU16(ms, written); // qdcount
            WriteU16(ms, 0); // ancount
            WriteU16(ms, 0); // nscount
            WriteU16(ms, 0); // arcount
            body.WriteTo(ms);
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
                // the instance of a service ("Name.with.dots._http._tcp.local") is its first label as it is, not cut at its dots;
                // longer than one label can be, it was read from several (ReadName joins them with dots) and is cut at them again
                if (instance && name.Length > service.Length + 1 && name.EndsWith("." + service, StringComparison.OrdinalIgnoreCase))
                {
                    string instanceLabel = name.Substring(0, name.Length - service.Length - 1);
                    if (Encoding.UTF8.GetByteCount(instanceLabel) > 63)
                    {
                        labels.AddRange(instanceLabel.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                    else
                    {
                        labels.Add(instanceLabel);
                    }
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
                    uint ttl = ((uint)packet[offset] << 24) | ((uint)packet[offset + 1] << 16) | ((uint)packet[offset + 2] << 8) | packet[offset + 3];
                    offset += 4;

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
                        Type = type,
                        Ttl = ttl
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
            // instance -> the A record of its host that came in the same packet as its SRV (a board sends PTR, SRV, TXT and A together)
            public Dictionary<string, string> SrvAddress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // instance -> the source address of the packet that brought its SRV (the board itself, unless a proxy answered)
            public Dictionary<string, string> SrvSender = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // the records of ONE packet; sender: its source address (null when not known)
            public void Add(List<AtomSpectraDnsRecord> records, string sender)
            {
                foreach (var r in records)
                {
                    if (r.Ttl == 0)
                    {
                        Withdraw(r);   // a goodbye: the board took the record back
                        continue;
                    }

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

                // the SRV kept for an instance and an A of its host in this packet: that address is the instance's own
                foreach (var r in records)
                {
                    AtomSpectraDnsRecord kept;
                    if (r.Type != TypeSrv || r.Ttl == 0 || string.IsNullOrEmpty(r.Target) || !Srv.TryGetValue(r.Name, out kept)
                        || !string.Equals(kept.Target, r.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!SrvAddress.ContainsKey(r.Name))
                    {
                        var a = records.FirstOrDefault(x => x.Type == TypeA && x.Ttl > 0 && !string.IsNullOrEmpty(x.Address)
                            && string.Equals(x.Name, r.Target, StringComparison.OrdinalIgnoreCase));
                        if (a != null)
                        {
                            SrvAddress[r.Name] = a.Address;
                        }
                    }
                    if (sender != null && !SrvSender.ContainsKey(r.Name))
                    {
                        SrvSender[r.Name] = sender;
                    }
                }
            }

            private void Withdraw(AtomSpectraDnsRecord r)
            {
                List<string> list;
                if (r.Type == TypePtr && Ptr.TryGetValue(r.Name, out list))
                {
                    list.RemoveAll(x => string.Equals(x, r.Target, StringComparison.OrdinalIgnoreCase));
                }
                else if (r.Type == TypeSrv)
                {
                    Srv.Remove(r.Name);
                    SrvAddress.Remove(r.Name);
                    SrvSender.Remove(r.Name);
                }
                else if (r.Type == TypeTxt)
                {
                    Txt.Remove(r.Name);
                }
                else if (r.Type == TypeA && !string.IsNullOrEmpty(r.Address) && A.TryGetValue(r.Name, out list))
                {
                    list.Remove(r.Address);
                    foreach (string inst in SrvAddress.Where(kv => kv.Value == r.Address && Srv.ContainsKey(kv.Key)
                        && string.Equals(Srv[kv.Key].Target, r.Name, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
                    {
                        SrvAddress.Remove(inst);
                    }
                }
            }
        }

        // The address of an instance: the A record of the packet that brought its SRV, or the source of that packet when it is an
        // address of the host; else the host's A record, when no other instance names that host. Two boards with one host name
        // (the conflict not resolved): the one address no other instance is known by, or null - not known WHICH address is this
        // board's, so it is not offered (the person would choose board B and get board A).
        private static string AddressOf(Cache cache, string instance, AtomSpectraDnsRecord srv)
        {
            if (srv == null || string.IsNullOrEmpty(srv.Target))
            {
                return null;
            }

            string known = KnownAddress(cache, instance, srv.Target);
            if (known != null)
            {
                return known;
            }

            List<string> a;
            if (!cache.A.TryGetValue(srv.Target, out a) || a.Count == 0)
            {
                return null;
            }

            var others = cache.Srv.Where(kv => !string.Equals(kv.Key, instance, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(kv.Value.Target, srv.Target, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList();
            if (others.Count == 0)
            {
                return a[0];
            }

            var free = a.Where(x => !others.Any(o => KnownAddress(cache, o, srv.Target) == x)).ToList();
            return free.Count == 1 ? free[0] : null;
        }

        private static string KnownAddress(Cache cache, string instance, string host)
        {
            string paired;
            if (cache.SrvAddress.TryGetValue(instance, out paired))
            {
                return paired;
            }

            string sender;
            List<string> a;
            if (cache.SrvSender.TryGetValue(instance, out sender) && cache.A.TryGetValue(host, out a) && a.Contains(sender))
            {
                return sender;
            }
            return null;
        }

        private static string SenderOf(EndPoint remote)
        {
            var ip = remote as IPEndPoint;
            return ip == null ? null : ip.Address.ToString();
        }

        // Exchange(SocketSet, ...) and the sockets are in AtomSpectraDnsSd.Adapters.cs (one socket per active IPv4 adapter)

        public static List<AtomSpectraBoardEntry> Browse()
        {
            var entries = new AtomSpectraBoardList();
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

                // Step 5: Follow-up 2 (A records): every host whose instance has no address yet - also a host already in the cache when
                // another board with the same host name answered first (its A is not this board's)
                var hostsToResolve = new List<string>();
                foreach (var inst in atomInstances)
                {
                    string key = inst.ToLowerInvariant();
                    if (cache.Srv.ContainsKey(key))
                    {
                        string host = cache.Srv[key].Target;
                        if (!string.IsNullOrEmpty(host) && AddressOf(cache, key, cache.Srv[key]) == null)
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
                        if (!string.IsNullOrEmpty(host) && AddressOf(cache, key, cache.Srv[key]) == null)
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

                // Step 6: Build entries; an instance without an address is counted (Incomplete), one board is listed once
                var existingAddresses = new HashSet<string>(StringComparer.Ordinal);
                var atomLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Atom instances first
                foreach (var inst in atomInstances)
                {
                    string key = inst.ToLowerInvariant();
                    AtomSpectraDnsRecord srv;
                    string address = cache.Srv.TryGetValue(key, out srv) ? AddressOf(cache, key, srv) : null;
                    if (address == null)
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        entries.Incomplete++;
                        continue;
                    }

                    if (!existingAddresses.Add(address))
                    {
                        AtomSpectraTcpLog.Info("Browse", inst + ": " + address + " is listed already (the same board)");
                        continue;
                    }

                    string host = srv.Target;
                    string serial = null;
                    int httpPort = 0;   // no TXT rest: the REST port of the form, not a guess

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
                    atomLabels.Add(InstanceLabel(inst, ServiceAtomSpectra));
                }

                // Http instances
                var serialClock = Stopwatch.StartNew();
                serialClockOfBrowse = serialClock;
                foreach (var inst in httpInstances)
                {
                    string key = inst.ToLowerInvariant();
                    AtomSpectraDnsRecord srv;
                    string address = cache.Srv.TryGetValue(key, out srv) ? AddressOf(cache, key, srv) : null;
                    if (address == null)
                    {
                        AtomSpectraTcpLog.Info("Browse", "incomplete: " + inst);
                        // a board announces one name under both services: its _http instance is not another board
                        if (!atomLabels.Contains(InstanceLabel(inst, ServiceHttp))) entries.Incomplete++;
                        continue;
                    }

                    string host = srv.Target;

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
            var logMsg = "found " + entries.Count.ToString(CultureInfo.InvariantCulture) + " board(s): ";
            if (entries.Count > 0)
            {
                logMsg += string.Join(", ", entries.Select(e => e.ToString()));
            }
            if (entries.Incomplete > 0)
            {
                logMsg += "; answered without an address: " + entries.Incomplete.ToString(CultureInfo.InvariantCulture);
            }
            LogFile(logMsg);   // Trace (becqmoni.log) and the in-program log

            return entries;
        }

        // "AtomSpectra Gateway-2._http._tcp.local" -> "AtomSpectra Gateway-2"
        private static string InstanceLabel(string instance, string service)
        {
            string suffix = "." + service;
            return instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? instance.Substring(0, instance.Length - suffix.Length) : instance;
        }

        [ThreadStatic] private static Stopwatch serialClockOfBrowse;

        private static string DefaultReadSerial(string address, int port)
        {
            var client = new AtomSpectraRestClient("http://" + address + ":" + port.ToString(CultureInfo.InvariantCulture) + "/")
            {
                UseSystemProxy = UseSystemProxyOfThread ?? UseSystemProxy
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
        // any other text (a URL, a name that does not parse) goes on as typed, to the old path first (Find)
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
            int restPort;
            return Find(name, httpPort, useProxy, timeoutMs, choose, out restPort);
        }

        // Refresh, and restPort: the REST port the board was read on (TXT rest of its announcement, the SRV port of _http, or httpPort)
        public static AtomSpectraDeviceConsole.RefreshResult Find(string name, int httpPort, bool useProxy, int timeoutMs,
            Func<IList<AtomSpectraBoardEntry>, AtomSpectraBoardEntry> choose, out int restPort)
        {
            restPort = httpPort;
            AtomSpectraDnsSd.UseSystemProxyOfThread = useProxy;   // this search's own (not the static of another search running now)
            try
            {
                // a name or an IP in the field is read as it is, by the old path. Only when it does not answer (DHCP gave the board
                // another address, the name is gone) the browse looks for the boards, with the choice window when there are several
                AtomSpectraDeviceConsole.RefreshResult typed = null;
                if (!IsDefaultName(name))
                {
                    AtomSpectraTcpLog.Info("Refresh", "a typed address, resolving " + name + " before the DNS-SD browse");
                    typed = AtomSpectraDeviceConsole.Refresh(name, httpPort, useProxy, timeoutMs);
                    if (typed.Found)
                    {
                        return typed;
                    }
                    AtomSpectraTcpLog.Info("Refresh", name + " did not answer, looking for the boards with the DNS-SD browse");
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
                    if (typed != null)
                    {
                        // no board answered the browse either: the default name through the system resolver, as Refresh did before
                        // the browse; not found there too - the status names the address the person has in the field
                        AtomSpectraTcpLog.Info("Refresh", "no board answered the DNS-SD browse, resolving " + AtomSpectraDeviceConsole.DefaultBoardName);
                        var byDefault = AtomSpectraDeviceConsole.Refresh(AtomSpectraDeviceConsole.DefaultBoardName, httpPort, useProxy, timeoutMs);
                        return byDefault.Found ? byDefault : typed;
                    }

                    AtomSpectraTcpLog.Info("Refresh", "no board answered the DNS-SD browse, resolving " + name);
                    return AtomSpectraDeviceConsole.Refresh(name, httpPort, useProxy, timeoutMs);
                }

                // instances that answered without an address: the choice is offered even for one board, nothing is taken silently
                var list = boards as AtomSpectraBoardList;
                int incomplete = list != null ? list.Incomplete : 0;

                AtomSpectraBoardEntry pick = boards[0];
                if (boards.Count > 1 || incomplete > 0)
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

                restPort = pick.HttpPort > 0 ? pick.HttpPort : httpPort;
                var r = AtomSpectraDeviceConsole.Refresh(pick.Address, restPort, useProxy, timeoutMs);
                r.FoundAs = pick.HostName;

                if (r.Found && !string.IsNullOrEmpty(r.Serial) && r.Serial == "?" && pick.Serial != null)
                {
                    r.Serial = pick.Serial;
                }

                return r;
            }
            finally
            {
                AtomSpectraDnsSd.UseSystemProxyOfThread = null;
            }
        }
    }
}
