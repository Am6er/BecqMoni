using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace BecquerelMonitor
{
    public static partial class AtomSpectraDnsSd
    {
        // Moments (ms from the start of the window) at which the PTR query is sent again from every adapter
        public static int[] QueryScheduleMs = { 0, 1000, 3000, 5000 };

        // Early exit: no new board for this long. The boards answer after a random delay of up to ~5 s: the quiet time
        // must be longer than that spread, else a third board that answers late is lost after two quick ones.
        public static int QuietExitMs = 5500;

        // Early exit only when at least this many boards are known
        public static int EarlyExitBoards = 2;

        // Count the boards known from the cache by their address: instance -> SRV -> host -> A.
        // One board that shows two labels (_atomspectra and _http) is one address; an instance whose chain is not complete yet is not counted.
        private static int CountBoards(Cache cache)
        {
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (string service in new[] { ServiceAtomSpectra, ServiceHttp })
            {
                if (!cache.Ptr.TryGetValue(service, out var instances)) continue;
                foreach (string inst in instances)
                {
                    bool suffix = inst.EndsWith("." + service, StringComparison.OrdinalIgnoreCase);
                    string label = suffix ? inst.Substring(0, inst.Length - service.Length - 1) : inst;
                    if (service == ServiceHttp && !label.StartsWith(GatewayInstancePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!cache.Srv.TryGetValue(inst, out var srv) || string.IsNullOrEmpty(srv.Target)) continue;
                    if (!cache.A.TryGetValue(srv.Target, out var a) || a.Count == 0) continue;
                    addresses.Add(a[0]);
                }
            }
            return addresses.Count;
        }

        // Perform scheduled DNS-SD exchanges within a time window
        private static void ExchangeScheduled(SocketSet set, IList<KeyValuePair<string, int>> questions, int windowMs, Cache cache)
        {
            if (questions == null || questions.Count == 0 || set.Sockets.Count == 0)
                return;

            var sends = (QueryScheduleMs ?? new int[] { 0 })
                .Where(t => t >= 0 && t < windowMs)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            if (sends.Count == 0) sends.Add(0);

            int nextSend = 0;
            var sw = Stopwatch.StartNew();
            long lastNewMs = 0;
            byte[] buffer = new byte[9000];

            while (true)
            {
                long now = sw.ElapsedMilliseconds;
                if (now >= windowMs) break;

                // a. Send queries at scheduled times
                while (nextSend < sends.Count && sends[nextSend] <= now)
                {
                    byte[] data = BuildQuery((ushort)new Random().Next(1, 65535), questions);
                    for (int i = 0; i < set.Sockets.Count; i++)
                    {
                        try { set.Sockets[i].SendTo(data, QueryEndpoint); set.SentFrom.Add(set.Names[i]); }   // the log names only the adapters that sent
                        catch (SocketException) { set.SendErrors++; }
                    }
                    nextSend++;
                }

                // b. Early exit check
                if (CountBoards(cache) >= EarlyExitBoards && now - lastNewMs >= QuietExitMs)
                    break;

                // c. Compute wake time
                long wake = windowMs;
                if (nextSend < sends.Count)
                    wake = Math.Min(wake, sends[nextSend]);
                if (CountBoards(cache) >= EarlyExitBoards)
                    wake = Math.Min(wake, lastNewMs + QuietExitMs);
                long waitMs = Math.Max(1, wake - now);

                // d. Select for readable sockets
                var read = new List<Socket>(set.Sockets);
                try
                {
                    Socket.Select(read, null, null, (int)Math.Min(waitMs, 1000000) * 1000);
                }
                catch (SocketException)
                {
                    break;
                }

                if (read.Count == 0) continue;

                // e. Read from ready sockets
                foreach (var s in read)
                {
                    try
                    {
                        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        int received = s.ReceiveFrom(buffer, ref remote);
                        if (received > 0)
                        {
                            set.Packets++;
                            int before = CountBoards(cache);
                            cache.Add(ParseRecords(buffer, received));
                            if (CountBoards(cache) > before)
                                lastNewMs = sw.ElapsedMilliseconds;
                        }
                    }
                    catch (SocketException) { }
                }
            }
        }
    }
}
