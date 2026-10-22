using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BecquerelMonitor;

namespace SessionHarness
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Console.WriteLine("RESULT: FAIL (missing mode)");
                    return 1;
                }

                string mode = args[0];
                int port = 0;
                string host = "127.0.0.1";

                if (mode == "pairing")
                {
                    if (args.Length < 4)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    port = int.Parse(args[1]);
                    string expectedPath = args[2];
                    int minEvents = int.Parse(args[3]);
                    return RunPairing(port, expectedPath, minEvents);
                }
                else if (mode == "p1")
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    port = int.Parse(args[1]);
                    string logPath = args[2];
                    return RunP1(port, logPath);
                }
                else if (mode == "p1clear")
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    port = int.Parse(args[1]);
                    string logPath = args[2];
                    return RunP1Clear(port, logPath);
                }
                else if (mode == "p2fast")
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    port = int.Parse(args[1]);
                    string logPath = args[2];
                    return RunP2Fast(port, logPath);
                }
                else if (mode == "p2deferred")
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    port = int.Parse(args[1]);
                    string logPath = args[2];
                    return RunP2Deferred(port, logPath);
                }
                else if (mode == "busy")
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    port = int.Parse(args[1]);
                    return RunBusy(port);
                }
                else if (mode == "failfast")
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("RESULT: FAIL (missing args)");
                        return 1;
                    }
                    host = args[1];
                    port = int.Parse(args[2]);
                    return RunFailFast(host, port);
                }
                else
                {
                    Console.WriteLine("RESULT: FAIL (unknown mode)");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL (exception) " + ex.Message);
                return 3;
            }
        }

        private static int RunPairing(int port, string expectedPath, int minEvents)
        {
            int[] expected;
            try
            {
                string[] lines = File.ReadAllLines(expectedPath);
                expected = lines.Select(l => int.Parse(l.Trim())).ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL (read expected: " + ex.Message + ")");
                return 1;
            }

            var events = new List<AtomSpectraTcpDataReadyArgs>();
            object lockObj = new object();

            var acquisition = new AtomSpectraTcpAcquisition(
                () => new AtomSpectraTcpTransport("127.0.0.1", port, 3000),
                1000,
                0,
                "pairing"
            );

            acquisition.DataReady += (sender, e) =>
            {
                lock (lockObj)
                {
                    events.Add(e);
                }
            };

            acquisition.Start();

            bool reached = WaitUntil(() =>
            {
                lock (lockObj)
                {
                    return events.Count >= minEvents;
                }
            }, 15000);

            acquisition.Dispose();

            int consistentCount = 0;
            int totalEvents = 0;

            lock (lockObj)
            {
                totalEvents = events.Count;
                foreach (var ev in events)
                {
                    bool consistent = false;
                    if (ev.Histogram != null && ev.Histogram.Length >= expected.Length)
                    {
                        if (ev.Histogram[5] == 1000 + ev.ElapsedTime)
                        {
                            bool allMatch = true;
                            for (int i = 0; i < expected.Length; i++)
                            {
                                if (i == 5) continue;
                                if (ev.Histogram[i] != expected[i])
                                {
                                    allMatch = false;
                                    break;
                                }
                            }
                            consistent = allMatch;
                        }
                    }

                    if (consistent) consistentCount++;
                    Console.WriteLine("event elapsed=" + ev.ElapsedTime + " ch5=" + (ev.Histogram != null && ev.Histogram.Length > 5 ? ev.Histogram[5] : -1) + " consistent=" + consistent);
                }
            }

            Console.WriteLine("pairs consistent: " + consistentCount + "/" + totalEvents);
            Console.WriteLine("reconnects: " + acquisition.Reconnects);

            if (totalEvents >= minEvents && consistentCount == totalEvents && acquisition.Reconnects >= 1)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (pairing criteria not met)");
                return 1;
            }
        }

        private static int RunP1(int port, string logPath)
        {
            var session = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            session.Clear(1000);   // a Start without a window no longer restarts a counting board by itself; an explicit Clear first keeps these scenarios on the -sto/-rst/-sta start they test
            var beginOutcome = session.Begin(true, 1000);
            if (beginOutcome != AtomSpectraTcpBeginOutcome.Started)
            {
                Console.WriteLine("RESULT: FAIL (begin not started)");
                session.Dispose();
                return 1;
            }

            Thread.Sleep(6000);

            var stopOutcome = session.Stop(1000);
            Console.WriteLine("stop: " + stopOutcome);

            Thread.Sleep(300);

            var commands = ReadCommands(logPath);
            int rstTotal = 0;
            int rstLater = 0;
            int staLater = 0;

            foreach (var kv in commands)
            {
                Console.WriteLine("log: conn=" + kv.Key + " cmd=" + kv.Value);
                if (kv.Value == "-rst")
                {
                    rstTotal++;
                    if (kv.Key >= 1) rstLater++;
                }
                if (kv.Value == "-sta" && kv.Key >= 1)
                {
                    staLater++;
                }
            }

            Console.WriteLine("rstTotal=" + rstTotal + " rstLater=" + rstLater + " staLater=" + staLater);

            session.Dispose();

            if (rstTotal == 1 && rstLater == 0 && staLater >= 1)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (p1 criteria not met)");
                return 1;
            }
        }

        private static int RunP1Clear(int port, string logPath)
        {
            var session = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            session.Clear(1000);   // a Start without a window no longer restarts a counting board by itself; an explicit Clear first keeps these scenarios on the -sto/-rst/-sta start they test
            var beginOutcome = session.Begin(true, 1000);
            if (beginOutcome != AtomSpectraTcpBeginOutcome.Started)
            {
                Console.WriteLine("RESULT: FAIL (begin not started)");
                session.Dispose();
                return 1;
            }

            if (!WaitUntil(() => session.IsLinkLost, 6000))
            {
                Console.WriteLine("RESULT: FAIL (link not lost)");
                session.Dispose();
                return 1;
            }

            session.Clear(1000);

            if (!WaitUntil(() => session.IsConnected, 8000))
            {
                Console.WriteLine("RESULT: FAIL (not reconnected)");
                session.Dispose();
                return 1;
            }

            Thread.Sleep(800);

            var stopOutcome1 = session.Stop(1000);
            Console.WriteLine("stop1: " + stopOutcome1);

            var beginOutcome2 = session.Begin(false, 1000);
            if (beginOutcome2 != AtomSpectraTcpBeginOutcome.Started)
            {
                Console.WriteLine("RESULT: FAIL (second begin not started)");
                session.Dispose();
                return 1;
            }

            Thread.Sleep(500);

            var stopOutcome2 = session.Stop(1000);
            Console.WriteLine("stop2: " + stopOutcome2);

            Thread.Sleep(300);

            var commands = ReadCommands(logPath);
            int rstTotal = 0;
            foreach (var kv in commands)
            {
                Console.WriteLine("log: conn=" + kv.Key + " cmd=" + kv.Value);
                if (kv.Value == "-rst")
                {
                    rstTotal++;
                }
            }

            Console.WriteLine("rstTotal=" + rstTotal);

            session.Dispose();

            if (rstTotal == 2)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (p1clear criteria not met)");
                return 1;
            }
        }

        private static int RunP2Fast(int port, string logPath)
        {
            var session = new AtomSpectraTcpSession("127.0.0.1", port, 60000, 1000, null);
            session.Clear(1000);   // a Start without a window no longer restarts a counting board by itself; an explicit Clear first keeps these scenarios on the -sto/-rst/-sta start they test
            var beginOutcome = session.Begin(true, 1000);
            if (beginOutcome != AtomSpectraTcpBeginOutcome.Started)
            {
                Console.WriteLine("RESULT: FAIL (begin not started)");
                session.Dispose();
                return 1;
            }

            if (!WaitUntil(() => session.IsLinkLost, 6000))
            {
                Console.WriteLine("RESULT: FAIL (link not lost)");
                session.Dispose();
                return 1;
            }

            var sw = Stopwatch.StartNew();
            var stopOutcome = session.Stop(1000);
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;

            Console.WriteLine("stop: " + stopOutcome + " elapsed=" + elapsed);

            Thread.Sleep(300);

            var commands = ReadCommands(logPath);
            var later = commands.Where(kv => kv.Key >= 1).ToList();

            foreach (var kv in later)
            {
                Console.WriteLine("log: conn=" + kv.Key + " cmd=" + kv.Value);
            }

            session.Dispose();

            bool pass = stopOutcome == AtomSpectraTcpStopOutcome.StoppedAfterReconnect &&
                        later.Count == 1 &&
                        later[0].Value == "-sto";

            if (pass)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (p2fast criteria not met)");
                return 1;
            }
        }

        private static int RunP2Deferred(int port, string logPath)
        {
            var session = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            session.Clear(1000);   // a Start without a window no longer restarts a counting board by itself; an explicit Clear first keeps these scenarios on the -sto/-rst/-sta start they test
            var beginOutcome = session.Begin(true, 1000);
            if (beginOutcome != AtomSpectraTcpBeginOutcome.Started)
            {
                Console.WriteLine("RESULT: FAIL (begin not started)");
                session.Dispose();
                return 1;
            }

            if (!WaitUntil(() => session.IsLinkLost, 6000))
            {
                Console.WriteLine("RESULT: FAIL (link not lost)");
                session.Dispose();
                return 1;
            }

            var sw = Stopwatch.StartNew();
            var stopOutcome = session.Stop(1000);
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;

            Console.WriteLine("stop: " + stopOutcome + " elapsed=" + elapsed);

            WaitUntil(() => AtomSpectraTcpSession.OrphanCount("127.0.0.1", port) == 0, 10000);
            int orphanCount = AtomSpectraTcpSession.OrphanCount("127.0.0.1", port);
            Console.WriteLine("orphanCount=" + orphanCount);

            Thread.Sleep(300);

            var commands = ReadCommands(logPath);
            var later = commands.Where(kv => kv.Key >= 1).ToList();

            foreach (var kv in later)
            {
                Console.WriteLine("log: conn=" + kv.Key + " cmd=" + kv.Value);
            }

            session.Dispose();

            bool pass = stopOutcome == AtomSpectraTcpStopOutcome.Deferred &&
                        elapsed <= 2100 &&
                        later.Count == 1 &&
                        later[0].Value == "-sto";

            if (pass)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (p2deferred criteria not met)");
                return 1;
            }
        }

        private static int RunBusy(int port)
        {
            var s1 = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            s1.Clear(1000);   // a Start without a window no longer restarts a counting board by itself; an explicit Clear first keeps these scenarios on the -sto/-rst/-sta start they test
            var o1 = s1.Begin(true, 1000);
            if (o1 != AtomSpectraTcpBeginOutcome.Started)
            {
                Console.WriteLine("RESULT: FAIL (s1 begin not started)");
                s1.Dispose();
                return 1;
            }

            var s2 = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            var sw = Stopwatch.StartNew();
            var o2 = s2.Begin(true, 1000);
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;

            Console.WriteLine("o2=" + o2 + " elapsed=" + elapsed);

            s1.Stop(1000);
            s2.Dispose();
            s1.Dispose();

            if (o2 == AtomSpectraTcpBeginOutcome.HostBusy && elapsed < 300)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (busy criteria not met)");
                return 1;
            }
        }

        private static int RunFailFast(string host, int port)
        {
            var session = new AtomSpectraTcpSession(host, port, 1000, 1000, null);
            var sw = Stopwatch.StartNew();
            var o = session.Begin(true, 1000);
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;

            Console.WriteLine("o=" + o + " elapsed=" + elapsed + " lastError=" + session.LastError);

            session.Dispose();

            if (o == AtomSpectraTcpBeginOutcome.ConnectFailed && elapsed <= 2100)
            {
                Console.WriteLine("RESULT: PASS");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAIL (failfast criteria not met)");
                return 1;
            }
        }

        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Thread.Sleep(20);
            }
            return cond();
        }

        private static List<KeyValuePair<int, string>> ReadCommands(string path)
        {
            var result = new List<KeyValuePair<int, string>>();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    string[] lines = File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        if (line.Contains("\"cmd\": "))
                        {
                            int conn = 0;
                            string cmd = "";

                            int connIdx = line.IndexOf("\"conn\": ");
                            if (connIdx >= 0)
                            {
                                int start = connIdx + 8;
                                int end = line.IndexOfAny(new char[] { ',', '}', ' ' }, start);
                                if (end < 0) end = line.Length;
                                string connStr = line.Substring(start, end - start).Trim();
                                if (int.TryParse(connStr, out conn))
                                {
                                    // parsed
                                }
                            }

                            int cmdIdx = line.IndexOf("\"cmd\": \"");
                            if (cmdIdx >= 0)
                            {
                                int start = cmdIdx + 8;
                                int end = line.IndexOf('"', start);
                                if (end > start)
                                {
                                    cmd = line.Substring(start, end - start);
                                }
                            }

                            result.Add(new KeyValuePair<int, string>(conn, cmd));
                        }
                    }
                    return result;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
            }
            return result;
        }
    }
}
