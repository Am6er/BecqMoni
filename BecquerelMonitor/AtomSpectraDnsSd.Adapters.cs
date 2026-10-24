using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BecquerelMonitor
{
    public static partial class AtomSpectraDnsSd
    {
        // Test seam: when set, returns the list of local IPv4 addresses to send from.
        public static Func<IList<IPAddress>> LocalAddresses;

        // Log to Trace (becqmoni.log) and AtomSpectraTcpLog.
        internal static void LogFile(string text)
        {
            Trace.WriteLine("AtomSpectra DNS-SD: " + text);
            AtomSpectraTcpLog.Info("Browse", text);
        }

        // Choose the list of local IPv4 addresses to bind sockets to.
        internal static IList<IPAddress> ChooseAddresses()
        {
            // Test seam
            if (LocalAddresses != null)
            {
                var seam = LocalAddresses();
                if (seam != null && seam.Count > 0)
                {
                    LogFile("adapters: " + string.Join(", ", seam.Select(a => a.ToString())));
                    return seam;
                }
            }

            // Loopback test endpoint
            if (IPAddress.IsLoopback(QueryEndpoint.Address))
            {
                var list = new List<IPAddress> { IPAddress.Loopback };
                LogFile("adapters: " + string.Join(", ", list.Select(a => a.ToString())));
                return list;
            }

            // Enumerate network interfaces
            var result = new List<IPAddress>();
            var seen = new HashSet<IPAddress>();
            var logParts = new List<string>();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    bool supportsMulticast;
                    try { supportsMulticast = ni.SupportsMulticast; }
                    catch { continue; }
                    if (!supportsMulticast) continue;

                    IPAddress firstV4 = null;
                    try
                    {
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                firstV4 = ua.Address;
                                break;
                            }
                        }
                    }
                    catch { continue; }

                    if (firstV4 == null) continue;
                    if (!seen.Add(firstV4)) continue;

                    result.Add(firstV4);
                    logParts.Add(ni.Name + "=" + firstV4);
                }
            }
            catch (Exception ex)
            {
                LogFile("adapter enumeration failed: " + ex.Message);
            }

            if (result.Count == 0)
            {
                result.Add(IPAddress.Any);
                logParts.Clear();
                logParts.Add(IPAddress.Any.ToString());
            }

            LogFile("adapters: " + string.Join(", ", logParts));
            return result;
        }

        // Holds a set of sockets and their metadata.
        internal sealed class SocketSet : IDisposable
        {
            public readonly List<Socket> Sockets = new List<Socket>();
            public readonly List<string> Names = new List<string>();
            public int Packets;          // answers received while the PTR query was out
            public int FollowUpPackets;  // answers to the SRV/TXT/A follow-up queries
            public int SendErrors;       // SendTo calls that threw
            public readonly HashSet<string> SentFrom = new HashSet<string>();   // adapters from which at least one query really went out
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                foreach (var s in Sockets)
                {
                    try { s.Close(); } catch { }
                }

                LogFile("sent the PTR query from " +
                    (SentFrom.Count == 0 ? "no adapter" : string.Join(", ", Names.Where(n => SentFrom.Contains(n)))) +
                    "; send errors: " + SendErrors.ToString(CultureInfo.InvariantCulture) +
                    "; response packets received: " + Packets.ToString(CultureInfo.InvariantCulture) +
                    "; follow-up packets: " + FollowUpPackets.ToString(CultureInfo.InvariantCulture));
            }
        }

        // Open one UDP socket per address, bound and configured for multicast.
        internal static SocketSet OpenSockets(IList<IPAddress> addresses)
        {
            var set = new SocketSet();
            byte[] destBytes = QueryEndpoint.Address.GetAddressBytes();
            bool isMulticast = destBytes.Length > 0 && destBytes[0] >= 224 && destBytes[0] <= 239;

            foreach (var addr in addresses)
            {
                Socket socket = null;
                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(addr, 0));

                    if (isMulticast)
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                        if (!addr.Equals(IPAddress.Any))
                        {
                            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, addr.GetAddressBytes());
                        }
                    }

                    // SIO_UDP_CONNRESET off: no "connection reset" on ICMP port-unreachable (best-effort)
                    try
                    {
                        socket.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null);
                    }
                    catch { }

                    set.Sockets.Add(socket);
                    set.Names.Add(addr.ToString());
                }
                catch (Exception ex)
                {
                    if (socket != null)
                    {
                        try { socket.Close(); } catch { }
                    }
                    LogFile("adapter " + addr + " skipped: " + ex.Message);
                }
            }

            // Every binding failed (the address went away between the enumeration and the bind, DHCP renewal): no query would leave at all.
            // The old code bound to the any-address socket; go back to it.
            if (set.Sockets.Count == 0 && !addresses.Contains(IPAddress.Any))
            {
                LogFile("no adapter could be bound: falling back to " + IPAddress.Any);
                return OpenSockets(new List<IPAddress> { IPAddress.Any });
            }

            return set;
        }

        // Send the follow-up query from all sockets, again on QueryScheduleMs, and collect the answers until every question is
        // answered or the window ends: the boards answer after a delay of their own (seen up to ~5 s), and a board whose answer
        // came after a short window used to drop out of the list without a word.
        private static void Exchange(SocketSet set, IList<KeyValuePair<string, int>> questions, int timeoutMs, Cache cache)
        {
            if (questions == null || questions.Count == 0 || set.Sockets.Count == 0) return;

            ushort id = (ushort)new Random().Next(1, 65535);
            byte[] data = BuildQuery(id, questions);
            if (data == null) return;   // no question could be written (BuildQuery logged which)

            var sends = SendMoments(timeoutMs);
            int nextSend = 0;
            var sw = Stopwatch.StartNew();
            byte[] buffer = new byte[9000];

            while (true)
            {
                long now = sw.ElapsedMilliseconds;
                if (now >= timeoutMs || Answered(cache, questions)) break;

                // Send from every socket at the scheduled moments
                while (nextSend < sends.Count && sends[nextSend] <= now)
                {
                    foreach (var s in set.Sockets)
                    {
                        try { s.SendTo(data, QueryEndpoint); }
                        catch (SocketException) { set.SendErrors++; }
                    }
                    nextSend++;
                }

                long wake = nextSend < sends.Count ? Math.Min(timeoutMs, sends[nextSend]) : timeoutMs;
                var read = new List<Socket>(set.Sockets);
                try
                {
                    Socket.Select(read, null, null, (int)Math.Max(1, wake - now) * 1000);
                }
                catch (SocketException)
                {
                    break;
                }

                foreach (var s in read)
                {
                    try
                    {
                        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        int received = s.ReceiveFrom(buffer, ref remote);
                        if (received > 0)
                        {
                            set.FollowUpPackets++;   // Packets is the PTR phase (ExchangeScheduled)
                            cache.Add(ParseRecords(buffer, received), SenderOf(remote));
                        }
                    }
                    catch (SocketException) { }
                }
            }
        }

        // The moments (ms from the start of a window) at which a query is sent: QueryScheduleMs inside the window, 0 at least.
        private static List<int> SendMoments(int windowMs)
        {
            var sends = (QueryScheduleMs ?? new int[] { 0 })
                .Where(t => t >= 0 && t < windowMs)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            if (sends.Count == 0) sends.Add(0);
            return sends;
        }

        // Every follow-up question has its answer: SRV and TXT of the instance, and for an A question an address for each instance
        // that names the host (two boards with one host name: the first A to come is not the second board's).
        private static bool Answered(Cache cache, IList<KeyValuePair<string, int>> questions)
        {
            foreach (var q in questions)
            {
                if (q.Value == TypeSrv && !cache.Srv.ContainsKey(q.Key)) return false;
                if (q.Value == TypeTxt && !cache.Txt.ContainsKey(q.Key)) return false;
                if (q.Value == TypeA)
                {
                    List<string> a;
                    if (!cache.A.TryGetValue(q.Key, out a) || a.Count == 0) return false;
                    foreach (var kv in cache.Srv)
                    {
                        if (string.Equals(kv.Value.Target, q.Key, StringComparison.OrdinalIgnoreCase) && AddressOf(cache, kv.Key, kv.Value) == null) return false;
                    }
                }
                if (q.Value != TypeSrv && q.Value != TypeTxt && q.Value != TypeA) return false;   // PTR: the number of instances is not known
            }
            return true;
        }
    }
}
