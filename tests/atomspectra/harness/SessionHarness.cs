using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace SessionHarness
{
    public static partial class Program
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
                    int fixResult;
                    if (TryRunFix(mode, args, out fixResult))   // PR37 fix, TCP (the fix-* modes below)
                    {
                        return fixResult;
                    }
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
            session.Clear(1000);   // a Clear left for the next Start keeps these scenarios on the -sto/-rst/-sta start they test; since O3 it does not
            session.Ask = q => AtomSpectraRestStartChoice.Restart;   // reset a counting board blind, the Start question is answered "Start over"
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
            session.Clear(1000);   // a Clear left for the next Start keeps these scenarios on the -sto/-rst/-sta start they test; since O3 it does not
            session.Ask = q => AtomSpectraRestStartChoice.Restart;   // reset a counting board blind, the Start question is answered "Start over"
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
            session.Clear(1000);   // a Clear left for the next Start keeps these scenarios on the -sto/-rst/-sta start they test; since O3 it does not
            session.Ask = q => AtomSpectraRestStartChoice.Restart;   // reset a counting board blind, the Start question is answered "Start over"
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
            session.Clear(1000);   // a Clear left for the next Start keeps these scenarios on the -sto/-rst/-sta start they test; since O3 it does not
            session.Ask = q => AtomSpectraRestStartChoice.Restart;   // reset a counting board blind, the Start question is answered "Start over"
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
            s1.Clear(1000);   // a Clear left for the next Start keeps this scenario on the -sto/-rst/-sta start; since O3 it does not
            s1.Ask = q => AtomSpectraRestStartChoice.Restart;   // reset a counting board blind, the Start question is answered "Start over"
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

// PR37 fix, TCP lane: session scenarios of the TCP fixes on the bridge mock (mock_cmd.py), built into SessionHarness.exe.
// fix-reset             T1: the board is reset from outside while the link is down; the reconnect sends -sta, the smaller
//                           counters never reach the document, BoardReset is raised once
// fix-ownreset          T1, the positive control: our own Clear (-rst) drops the counters and the new acquisition goes on
// fix-silent[-ask]      T2: a bridge that sends no status: refused without a window, asked with one ("Continue" sends -sta)
// fix-onesample[-headless] T3: one status in the probe: not taken as counting; "Continue" sends -sta, Stop sends -sto
// fix-docbehind[-ask]   T7: the document holds more than the board: refused without a window, "Continue" not offered with one
// fix-stopmute          T4: -sto on a live link gets no answer: not "Stopped", -sto goes again on a new connection
// fix-stall             T4: an open socket without data for NoDataLinkLostMs is a lost link (reconnect, -sta again)
// fix-ctl-clear         T8 (real build, the controller by reflection): Clear of a joined acquisition asks first (throws without
//                           windows, nothing sent); Clear of our own one resets at once and puts the raised preset back
// fix-ctl-reset         T1 + T9 (real build): the controller on a reset from outside - the document keeps its data, the recording
//                           stops, the session detaches without -sto; the snapshots are folded on the "UI" thread
// PR #38, the maintainer's TCP lane:
// fix-stopask-reset     T2 + T3: the board is reset from outside while the Stop question of a joined acquisition is open - the guard
//                           still watches (BoardReset, no smaller spectrum to the document); the acquisition is closed meanwhile (as the
//                           controller does on BoardReset), the answer "stop on the board" then sends nothing and does not throw
// fix-clear-counted[-headless] O3: a Clear before any Start leaves the reset for it; the board counts - the Start question (Cancel: nothing
//                           sent, the reset still waits; Start over: -sto -rst -sta), without a window refused with nothing sent
// fix-clear-own         O3, the positive control: a Clear after our own Stop on a stopped board resets it at the next Start without a
//                           question; a Clear that saw less than the board holds now is asked about
namespace SessionHarness
{
    public static partial class Program
    {
        static int fixFailures;

        static void FixCheck(bool ok, string what)
        {
            Console.WriteLine((ok ? "ok: " : "FAILED: ") + what);
            if (!ok) fixFailures++;
        }

        static int FixVerdict()
        {
            Console.WriteLine(fixFailures == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fixFailures.ToString(CultureInfo.InvariantCulture) + " checks)");
            return fixFailures == 0 ? 0 : 1;
        }

        static bool TryRunFix(string mode, string[] args, out int result)
        {
            result = 1;
            Func<int, string, int> body;
            switch (mode)
            {
                case "fix-reset": body = FixReset; break;
                case "fix-ownreset": body = FixOwnReset; break;
                case "fix-silent": body = (p, l) => FixSilent(p, l, false); break;
                case "fix-silent-ask": body = (p, l) => FixSilent(p, l, true); break;
                case "fix-onesample": body = (p, l) => FixOneSample(p, l, true); break;
                case "fix-onesample-headless": body = (p, l) => FixOneSample(p, l, false); break;
                case "fix-docbehind": body = (p, l) => FixDocBehind(p, l, false); break;
                case "fix-docbehind-ask": body = (p, l) => FixDocBehind(p, l, true); break;
                case "fix-stopmute": body = FixStopMute; break;
                case "fix-stall": body = FixStall; break;
                case "fix-ctl-clear": body = FixCtlClear; break;
                case "fix-ctl-reset": body = FixCtlReset; break;
                case "fix-stopask-reset": body = FixStopAskReset; break;
                case "fix-clear-counted": body = (p, l) => FixClearCounted(p, l, true); break;
                case "fix-clear-counted-headless": body = (p, l) => FixClearCounted(p, l, false); break;
                case "fix-clear-own": body = FixClearOwn; break;
                default: return false;
            }
            if (args.Length < 3)
            {
                Console.WriteLine("RESULT: FAIL (missing args)");
                return true;
            }
            if (args.Length > 3 && Directory.Exists(args[3]))
            {
                // the controller scenarios load the program's own libraries (MathNet for EnergySpectrum) from the directory of the build
                string libs = args[3];
                AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
                {
                    string path = Path.Combine(libs, new AssemblyName(e.Name).Name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
            }
            try
            {
                result = body(int.Parse(args[1], CultureInfo.InvariantCulture), args[2]);
            }
            catch (Exception ex)
            {
                // the real cause, not "the target of an invocation threw" of a reflection call
                Exception cause = ex;
                while ((cause is TargetInvocationException || cause is TypeInitializationException) && cause.InnerException != null) cause = cause.InnerException;
                Console.WriteLine("RESULT: FAIL (exception) " + cause.GetType().Name + ": " + cause.Message);
                result = 3;
            }
            return true;
        }

        static List<string> Sent(string log)
        {
            return ReadCommands(log).Select(kv => kv.Value).ToList();
        }

        static string Joined(IEnumerable<KeyValuePair<int, string>> cmds)
        {
            return string.Join(" ", cmds.Select(kv => kv.Key.ToString(CultureInfo.InvariantCulture) + ":" + kv.Value));
        }

        // T1: 3 cycles, the link drops, the board is reset meanwhile; the reconnect sends -sta and gets smaller counters
        static int FixReset(int port, string log)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            var given = new List<int>();
            int resets = 0;
            AtomSpectraTcpBoardResetArgs seen = null;
            s.DataReady += (o, e) => { lock (given) given.Add(e.ElapsedTime); };
            s.BoardReset += (o, e) => { seen = e; Interlocked.Increment(ref resets); };
            var outcome = s.Begin(false, 1000);
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started, "Start (same document: -sta) is done (" + outcome + ")");
            WaitUntil(() => Volatile.Read(ref resets) > 0, 15000);
            Thread.Sleep(1500);   // more snapshots of the new acquisition arrive; none may reach the document
            var cmds = ReadCommands(log);
            s.Detach();
            s.Dispose();
            List<int> times;
            lock (given) times = given.ToList();
            Console.WriteLine("given times: " + string.Join(",", times.Select(t => t.ToString(CultureInfo.InvariantCulture))) + "; resets " + resets
                + (seen != null ? " (" + seen.LogText() + ")" : "") + "; commands " + Joined(cmds));
            FixCheck(resets == 1, "the drop of the counters after the reconnect is reported once (BoardReset)");
            FixCheck(times.Count >= 2 && times.Zip(times.Skip(1), (a, b) => b >= a).All(x => x), "no snapshot with a lower time reached the document");
            FixCheck(seen != null && seen.TimeNow < seen.TimeBefore && seen.TotalNow < seen.TotalBefore, "the event carries the numbers before and after");
            FixCheck(cmds.Any(kv => kv.Key == 1 && kv.Value == "-sta"), "the reconnect sent -sta (the story of the finding)");
            FixCheck(!cmds.Any(kv => kv.Value == "-rst"), "no -rst was sent");
            return FixVerdict();
        }

        // T1, the positive control: Clear (-rst) on our own run drops the counters, which is our reset - the snapshots go on
        static int FixOwnReset(int port, string log)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            var given = new List<int>();
            int resets = 0;
            s.DataReady += (o, e) => { lock (given) given.Add(e.ElapsedTime); };
            s.BoardReset += (o, e) => Interlocked.Increment(ref resets);
            var outcome = s.Begin(false, 1000);
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started, "Start is done (" + outcome + ")");
            WaitUntil(() => { lock (given) return given.Count >= 3; }, 8000);
            int before;
            lock (given) before = given.Count;
            s.Clear(1000);
            Thread.Sleep(2500);
            List<int> times;
            lock (given) times = given.ToList();
            var cmds = ReadCommands(log);
            var stop = s.Stop(1000);
            s.Dispose();
            Console.WriteLine("given times: " + string.Join(",", times.Select(t => t.ToString(CultureInfo.InvariantCulture))) + " (Clear after " + before.ToString(CultureInfo.InvariantCulture)
                + "); resets " + resets + "; stop " + stop + "; commands " + Joined(cmds));
            FixCheck(cmds.Any(kv => kv.Value == "-rst"), "Clear sent -rst");
            FixCheck(resets == 0, "our own reset is not a reset from outside");
            FixCheck(times.Count > before && times.Skip(before).Any(t => t < times[before - 1]), "the smaller counters of the new acquisition reach the document");
            return FixVerdict();
        }

        // T2: no status at all in the probe
        static int FixSilent(int port, string log, bool withWindow)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            int asked = 0;
            AtomSpectraRestStartQuestion question = null;
            if (withWindow)
                s.Ask = q => { asked++; question = q; return AtomSpectraRestStartChoice.Continue; };
            var outcome = s.Begin(true, 1000);
            Thread.Sleep(400);
            var sent = Sent(log);
            Console.WriteLine("outcome " + outcome + ", state " + s.BoardState + ", refusal " + s.Refusal + ", asked " + asked + ", commands " + string.Join(" ", sent));
            FixCheck(s.BoardState == AtomSpectraTcpBoardState.Silent, "the probe saw no status (Silent)");
            if (!withWindow)
            {
                FixCheck(outcome == AtomSpectraTcpBeginOutcome.Refused && s.Refusal == AtomSpectraTcpRefusal.Unknown, "without a window: refused, the reason is the unknown state");
                FixCheck(!sent.Contains("-sta") && !sent.Contains("-sto") && !sent.Contains("-rst"), "without a window: nothing was sent (no blind -sta)");
                s.Dispose();
                return FixVerdict();
            }
            FixCheck(asked == 1 && question != null && !question.Counting && question.ContinueAllowed, "with a window: the question is asked (Continue offered)");
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started && sent.Contains("-sta") && !sent.Contains("-rst") && !s.JoinedRunning, "Continue: -sta without a reset, not a join");
            var stop = s.Stop(1000);
            Thread.Sleep(300);
            FixCheck(stop == AtomSpectraTcpStopOutcome.Stopped && Sent(log).Contains("-sto"), "Stop sends -sto (" + stop + ")");
            s.Dispose();
            return FixVerdict();
        }

        // T3: one status in the probe (a stopped board with data, the bridge sends one cycle only)
        static int FixOneSample(int port, string log, bool withWindow)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            int asked = 0;
            AtomSpectraRestStartQuestion question = null;
            if (withWindow)
                s.Ask = q => { asked++; question = q; return AtomSpectraRestStartChoice.Continue; };
            var outcome = s.Begin(true, 1000);
            Thread.Sleep(400);
            var sent = Sent(log);
            Console.WriteLine("outcome " + outcome + ", state " + s.BoardState + ", refusal " + s.Refusal + ", asked " + asked + ", joined " + s.JoinedRunning
                + ", board time " + s.ElapsedAtBeginSeconds.ToString("R", CultureInfo.InvariantCulture) + ", commands " + string.Join(" ", sent));
            FixCheck(s.BoardState == AtomSpectraTcpBoardState.Uncertain, "the probe saw one status (Uncertain)");
            if (!withWindow)
            {
                FixCheck(outcome == AtomSpectraTcpBeginOutcome.Refused && s.Refusal == AtomSpectraTcpRefusal.Unknown, "without a window and with data on the board: refused (not a blind join)");
                FixCheck(!sent.Contains("-sta") && !sent.Contains("-sto") && !sent.Contains("-rst"), "without a window: nothing was sent");
                s.Dispose();
                return FixVerdict();
            }
            FixCheck(asked == 1 && question != null && !question.Counting, "with a window: asked, the board is not presented as counting");
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started && sent.Contains("-sta") && !sent.Contains("-rst"), "Continue: -sta (Resume), no reset");
            FixCheck(!s.JoinedRunning && s.ElapsedAtBeginSeconds == 500, "not a join; the document starts from the board's 500 s");
            var stop = s.Stop(1000);
            Thread.Sleep(300);
            FixCheck(stop == AtomSpectraTcpStopOutcome.Stopped && Sent(log).Contains("-sto"), "Stop sends -sto, not a detach (" + stop + ")");
            s.Dispose();
            return FixVerdict();
        }

        // T7: the document holds 100000 s, the counting board a few seconds
        static int FixDocBehind(int port, string log, bool withWindow)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            s.DocumentSeconds = 100000;
            s.DocumentCounts = 1;
            int asked = 0;
            AtomSpectraRestStartQuestion question = null;
            if (withWindow)
                s.Ask = q => { asked++; question = q; return AtomSpectraRestStartChoice.Continue; };
            var outcome = s.Begin(true, 1000);
            Thread.Sleep(400);
            var sent = Sent(log);
            Console.WriteLine("outcome " + outcome + ", state " + s.BoardState + ", refusal " + s.Refusal + ", asked " + asked
                + (question != null ? ", question: document " + question.DocumentSeconds.ToString(CultureInfo.InvariantCulture) + " s, continue allowed " + question.ContinueAllowed : "")
                + ", commands " + string.Join(" ", sent));
            if (!withWindow)
            {
                FixCheck(outcome == AtomSpectraTcpBeginOutcome.Refused && s.Refusal == AtomSpectraTcpRefusal.BehindDocument, "without a window: refused, the board holds less than the document");
            }
            else
            {
                FixCheck(asked == 1 && question != null && question.DocumentSeconds == 100000 && question.DocumentCounts == 1, "the question knows what the document holds");
                FixCheck(question != null && !question.ContinueAllowed, "Continue is not offered: the board holds less than the document");
                FixCheck(outcome == AtomSpectraTcpBeginOutcome.Cancelled, "a Continue that is not allowed cancels (" + outcome + ")");
            }
            FixCheck(!sent.Contains("-sta") && !sent.Contains("-sto") && !sent.Contains("-rst"), "nothing was sent to the board");
            s.Dispose();
            return FixVerdict();
        }

        // T4: the bridge does not answer -sto on the first connection
        static int FixStopMute(int port, string log)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            int lost = 0;
            s.LinkLost += (o, e) => Interlocked.Increment(ref lost);
            var outcome = s.Begin(false, 1000);
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started, "Start is done (" + outcome + ")");
            Thread.Sleep(1200);
            var sw = Stopwatch.StartNew();
            var stop = s.Stop(1000);
            long ms = sw.ElapsedMilliseconds;
            WaitUntil(() => ReadCommands(log).Any(kv => kv.Key == 1 && kv.Value == "-sto"), 6000);
            var cmds = ReadCommands(log);
            Console.WriteLine("stop " + stop + " in " + ms.ToString(CultureInfo.InvariantCulture) + " ms, unanswered " + s.StopUnanswered + ", lost events " + lost + ", commands " + Joined(cmds));
            FixCheck(stop != AtomSpectraTcpStopOutcome.Stopped, "an unanswered -sto is not reported as Stopped");
            FixCheck(stop == AtomSpectraTcpStopOutcome.StoppedAfterReconnect || stop == AtomSpectraTcpStopOutcome.Deferred, "it is resent or deferred");
            FixCheck(s.StopUnanswered, "the session says the stop was not answered (its own message)");
            FixCheck(cmds.Any(kv => kv.Key == 1 && kv.Value == "-sto"), "-sto goes again on a new connection");
            FixCheck(lost == 0, "our own drop is not announced as a lost link");
            WaitUntil(() => AtomSpectraTcpSession.OrphanCount("127.0.0.1", port) == 0, 5000);
            s.Dispose();
            return FixVerdict();
        }

        // T4: the socket stays open, the data stops after 3 cycles
        static int FixStall(int port, string log)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            long lastData = 0, lostAt = 0;
            var clock = Stopwatch.StartNew();
            int lost = 0;
            s.DataReady += (o, e) => Interlocked.Exchange(ref lastData, clock.ElapsedMilliseconds);
            s.LinkLost += (o, e) => { Interlocked.Exchange(ref lostAt, clock.ElapsedMilliseconds); Interlocked.Increment(ref lost); };
            var outcome = s.Begin(false, 1000);
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started, "Start is done (" + outcome + ")");
            WaitUntil(() => Volatile.Read(ref lost) > 0, AtomSpectraTcpSession.NoDataLinkLostMs + 8000);
            long gap = Interlocked.Read(ref lostAt) - Interlocked.Read(ref lastData);
            WaitUntil(() => ReadCommands(log).Any(kv => kv.Key == 1 && kv.Value == "-sta"), 6000);
            var cmds = ReadCommands(log);
            Console.WriteLine("lost " + lost + ", " + gap.ToString(CultureInfo.InvariantCulture) + " ms after the last snapshot (limit "
                + AtomSpectraTcpSession.NoDataLinkLostMs.ToString(CultureInfo.InvariantCulture) + " ms); commands " + Joined(cmds));
            FixCheck(lost == 1, "an open socket without data is a lost link");
            FixCheck(gap >= AtomSpectraTcpSession.NoDataLinkLostMs - 500 && gap <= AtomSpectraTcpSession.NoDataLinkLostMs + 3000, "after the limit, not before");
            FixCheck(cmds.Any(kv => kv.Key == 1 && kv.Value == "-sta"), "the reconnect sends -sta again");
            s.Stop(1000);
            s.Dispose();
            return FixVerdict();
        }

        // T2 + T3: a joined acquisition (the counting mock, Continue); while the Stop question is open the board is reset from outside, the
        // acquisition is closed meanwhile (as the controller does on BoardReset: its post runs in the modal loop of the question), and the
        // answer is "stop on the board"
        static int FixStopAskReset(int port, string log)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            var given = new List<int>();
            int resets = 0, asked = 0;
            s.DataReady += (o, e) => { lock (given) given.Add(e.ElapsedTime); };
            s.BoardReset += (o, e) => Interlocked.Increment(ref resets);
            s.Ask = q => AtomSpectraRestStartChoice.Continue;
            var outcome = s.Begin(true, 1000);
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "a counting board, Continue: joined (" + outcome + ")");
            bool resetInQuestion = false;
            s.AskStopJoined = () =>
            {
                asked++;
                resetInQuestion = WaitUntil(() => Volatile.Read(ref resets) > 0, 10000);   // the user thinks; the board is reset meanwhile
                Thread.Sleep(1500);   // more snapshots of the new acquisition arrive; none may reach the document
                s.Detach();           // BoardReset -> the controller detaches the session (StopAfterBoardReset), still inside the question
                return true;          // "stop on the board" - for an acquisition that is gone
            };
            AtomSpectraTcpStopOutcome stop = AtomSpectraTcpStopOutcome.NotRunning;
            Exception thrown = null;
            try { stop = s.Stop(1000); }
            catch (Exception ex) { thrown = ex; }
            Thread.Sleep(300);
            var cmds = ReadCommands(log);
            List<int> times;
            lock (given) times = given.ToList();
            Console.WriteLine("asked " + asked + ", reset in the question " + resetInQuestion + " (resets " + resets + "), stop " + stop
                + (thrown != null ? ", thrown " + thrown.GetType().Name + ": " + thrown.Message : "") + "; given times "
                + string.Join(",", times.Select(t => t.ToString(CultureInfo.InvariantCulture))) + "; commands " + Joined(cmds));
            FixCheck(asked == 1, "the Stop question of the joined acquisition is asked");
            FixCheck(resetInQuestion && resets == 1, "T2: the reset from outside while the question is open is found (BoardReset once)");
            FixCheck(times.Count >= 2 && times.Zip(times.Skip(1), (a, b) => b >= a).All(x => x), "T2: no smaller spectrum reached the document while the question was open");
            FixCheck(thrown == null, "T3: the answer after the acquisition was closed meanwhile does not throw");
            FixCheck(stop == AtomSpectraTcpStopOutcome.Detached && s.JoinedRunning, "T3: Detached, the acquisition on the board still not ours (" + stop + ")");
            FixCheck(!cmds.Any(kv => kv.Value == "-sto" || kv.Value == "-rst" || kv.Value == "-sta"), "nothing was sent to the board");
            s.Dispose();
            return FixVerdict();
        }

        // O3: a Clear before any Start (as after a restart of the program) leaves the reset for the next Start; the board counts meanwhile
        static int FixClearCounted(int port, string log, bool withWindow)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            s.Clear(1000);
            FixCheck(s.ResetOnNextStart, "Clear without an open acquisition leaves the reset for the next Start");
            int asked = 0;
            AtomSpectraRestStartQuestion question = null;
            if (withWindow)
                s.Ask = q => { asked++; question = q; return AtomSpectraRestStartChoice.Cancel; };
            var o1 = s.Begin(true, 1000);
            Thread.Sleep(300);
            var sent = Sent(log);
            Console.WriteLine("outcome " + o1 + ", state " + s.BoardState + ", refusal " + s.Refusal + ", asked " + asked
                + (question != null && question.Board != null ? ", question: board " + question.Board.Time.ToString(CultureInfo.InvariantCulture) + " s, counting " + question.Counting : "")
                + ", commands " + string.Join(" ", sent));
            bool none = !sent.Contains("-sto") && !sent.Contains("-rst") && !sent.Contains("-sta");
            if (!withWindow)
            {
                FixCheck(o1 == AtomSpectraTcpBeginOutcome.Refused && s.Refusal == AtomSpectraTcpRefusal.ClearPending, "O3 without a window: refused, the reason is the Clear that did not see this acquisition");
                FixCheck(none, "O3 without a window: nothing was sent (no blind -sto -rst -sta)");
                FixCheck(s.ResetOnNextStart, "the reset still waits");
                s.Dispose();
                return FixVerdict();
            }
            FixCheck(asked == 1 && question != null && question.Counting, "O3: a counting board, the Start question instead of a blind reset");
            FixCheck(question != null && question.Board != null && question.Board.Time > 0 && question.DocumentSeconds == 0 && question.DocumentCounts == 0,
                "the question shows the board's numbers, the document empty (Start over is offered)");
            FixCheck(o1 == AtomSpectraTcpBeginOutcome.Cancelled && none, "Cancel: nothing was sent (" + o1 + ")");
            FixCheck(s.ResetOnNextStart, "Cancel: the reset still waits");
            s.Ask = q => { asked++; return AtomSpectraRestStartChoice.Restart; };
            var o2 = s.Begin(true, 1000);
            Thread.Sleep(300);
            var cmds = ReadCommands(log);
            Console.WriteLine("second Start " + o2 + ", asked " + asked + ", commands " + Joined(cmds));
            FixCheck(o2 == AtomSpectraTcpBeginOutcome.Started && asked == 2 && cmds.Count(kv => kv.Value == "-rst") == 1
                && cmds.Any(kv => kv.Key == 1 && kv.Value == "-sto") && cmds.Any(kv => kv.Key == 1 && kv.Value == "-sta"), "the answer Start over: -sto, -rst, -sta");
            FixCheck(!s.ResetOnNextStart, "the reset is done");
            s.Stop(1000);
            s.Dispose();
            return FixVerdict();
        }

        // O3, the positive control (the mock stopped at 500 s): Continue (-sta), our own Stop, Clear - the next Start resets without a
        // question; then a Clear that saw 100 s, the board holds 500 s: asked
        static int FixClearOwn(int port, string log)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            int asked = 0;
            s.Ask = q => { asked++; return AtomSpectraRestStartChoice.Continue; };
            var o1 = s.Begin(true, 1000);
            FixCheck(o1 == AtomSpectraTcpBeginOutcome.Started && asked == 1 && !s.JoinedRunning, "a stopped board with data: asked, Continue resumes it (" + o1 + ")");
            Thread.Sleep(1200);
            var stop = s.Stop(1000);
            s.Clear(1000);
            Console.WriteLine("stop " + stop + "; Clear after it: the reset waits " + s.ResetOnNextStart + ", the board then "
                + s.ClearSeenSeconds.ToString(CultureInfo.InvariantCulture) + " s, " + s.ClearSeenCounts.ToString(CultureInfo.InvariantCulture) + " counts");
            FixCheck(s.ResetOnNextStart && s.ClearSeenSeconds == 500 && s.ClearSeenCounts > 0, "the Clear after our own Stop waits for the next Start and remembers the board (500 s)");
            var o2 = s.Begin(false, 1000);
            Thread.Sleep(300);
            var cmds = ReadCommands(log);
            Console.WriteLine("second Start " + o2 + ", asked " + asked + ", commands " + Joined(cmds));
            FixCheck(o2 == AtomSpectraTcpBeginOutcome.Started && asked == 1, "the board still holds what the Clear saw: no question");
            FixCheck(cmds.Any(kv => kv.Key == 1 && kv.Value == "-sto") && cmds.Any(kv => kv.Key == 1 && kv.Value == "-rst") && cmds.Any(kv => kv.Key == 1 && kv.Value == "-sta"),
                "it is reset as the Clear promised: -sto, -rst, -sta");
            FixCheck(!s.ResetOnNextStart, "the reset is done");
            s.Stop(1000);
            s.RequestResetOnNextStart(100, 10);   // a Clear that saw 100 s: the 500 s of the board were counted after it
            AtomSpectraRestStartQuestion question = null;
            s.Ask = q => { asked++; question = q; return AtomSpectraRestStartChoice.Cancel; };
            var o3 = s.Begin(false, 1000);
            Thread.Sleep(300);
            var third = ReadCommands(log).Where(kv => kv.Key == 2).ToList();
            Console.WriteLine("third Start " + o3 + ", asked " + asked + ", commands of its connection " + Joined(third));
            FixCheck(asked == 2 && question != null && question.Board != null && question.Board.Time == 500, "a board ahead of what the Clear saw: the Start question with its numbers");
            FixCheck(o3 == AtomSpectraTcpBeginOutcome.Cancelled && third.Count == 0, "Cancel: nothing sent");
            s.Dispose();
            return FixVerdict();
        }

        // ---- the controller (real build only): found by reflection; the source build has no controller and says n/a ----

        const BindingFlags NonPub = BindingFlags.NonPublic | BindingFlags.Instance;

        static Type CtlType
        {
            get { return Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor"); }
        }

        static bool CtlMissing()
        {
            if (CtlType != null) return false;
            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BecquerelMonitor.exe")))
            {
                FixCheck(false, "the controller is not found in the real build");
                return true;
            }
            Console.WriteLine("n/a: the controller is not in this build");
            return true;
        }

        static object NewResultData(int channels, int preset)
        {
            Type rdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
            Type specT = Type.GetType("BecquerelMonitor.EnergySpectrum, BecquerelMonitor");
            object rd = Activator.CreateInstance(rdT);
            object spec = Activator.CreateInstance(specT, new object[] { 1.0, channels });
            rdT.GetProperty("EnergySpectrum").SetValue(rd, spec);
            rdT.GetProperty("DeviceConfig").SetValue(rd, null);
            StatusSet(rd, "PresetTime", preset);
            return rd;
        }

        static object StatusOf(object rd)
        {
            return rd.GetType().GetProperty("ResultDataStatus").GetValue(rd);
        }

        static T StatusGet<T>(object rd, string name)
        {
            object st = StatusOf(rd);
            return (T)st.GetType().GetProperty(name).GetValue(st);
        }

        static void StatusSet(object rd, string name, object value)
        {
            object st = StatusOf(rd);
            st.GetType().GetProperty(name).SetValue(st, value);
        }

        static Exception InvokeClear(Type ctlT, object ctl, object rd)
        {
            try
            {
                ctlT.GetMethod("ClearMeasurementResult").Invoke(ctl, new object[] { rd });
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException;
            }
        }

        // T8: a joined acquisition (the counting mock, Continue) and then our own one (-sta) on the same document
        static int FixCtlClear(int port, string log)
        {
            if (CtlMissing()) return FixVerdict();
            Type ctlT = CtlType;
            object ctl = Activator.CreateInstance(ctlT);
            object rd = NewResultData(8192, 40000);
            ctlT.GetField("resultData", NonPub).SetValue(ctl, rd);
            ctlT.GetField("deviceGuid", NonPub).SetValue(ctl, "pr37-fix-ctl-clear");
            ctlT.GetField("presetBeforeBoardTime", NonPub).SetValue(ctl, 3600);   // as a Start that joined a board with time

            var joined = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            joined.Ask = q => AtomSpectraRestStartChoice.Continue;
            var o1 = joined.Begin(true, 2000);
            FixCheck(o1 == AtomSpectraTcpBeginOutcome.Started && joined.JoinedRunning, "joined the counting board (" + o1 + ")");
            ctlT.GetField("session", NonPub).SetValue(ctl, joined);
            Exception thrown = InvokeClear(ctlT, ctl, rd);
            Thread.Sleep(300);
            var sentA = Sent(log);
            Console.WriteLine("joined: " + (thrown == null ? "no exception" : thrown.GetType().Name + ": " + thrown.Message) + "; preset "
                + StatusGet<int>(rd, "PresetTime").ToString(CultureInfo.InvariantCulture) + "; commands " + string.Join(" ", sentA));
            FixCheck(thrown is InvalidOperationException, "Clear of a joined acquisition asks first; without windows the question throws (S100)");
            FixCheck(!sentA.Contains("-rst"), "without an answer nothing is reset on the board");
            FixCheck(StatusGet<int>(rd, "PresetTime") == 40000, "the preset stays while the board is not reset");
            joined.Stop(1000);   // detached, no -sto
            joined.Dispose();

            var own = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            var o2 = own.Begin(false, 2000);
            FixCheck(o2 == AtomSpectraTcpBeginOutcome.Started && !own.JoinedRunning, "our own acquisition (-sta) (" + o2 + ")");
            ctlT.GetField("session", NonPub).SetValue(ctl, own);
            thrown = InvokeClear(ctlT, ctl, rd);
            Thread.Sleep(300);
            var cmds = ReadCommands(log);
            Console.WriteLine("own: " + (thrown == null ? "no exception" : thrown.GetType().Name + ": " + thrown.Message) + "; preset "
                + StatusGet<int>(rd, "PresetTime").ToString(CultureInfo.InvariantCulture) + "; commands " + Joined(cmds));
            FixCheck(thrown == null && cmds.Any(kv => kv.Key == 1 && kv.Value == "-rst"), "Clear of our own acquisition resets the board without a question");
            FixCheck(StatusGet<int>(rd, "PresetTime") == 3600, "the preset raised by the board's time goes back to the document's own (40000 -> 3600)");
            FixCheck((int)ctlT.GetField("presetBeforeBoardTime", NonPub).GetValue(ctl) == -1, "and is forgotten");
            own.Stop(1000);
            own.Dispose();
            return FixVerdict();
        }

        // The UI thread of the program, for MainForm.originalContext: posts run in order on one thread of their own.
        sealed class UiThread : SynchronizationContext, IDisposable
        {
            readonly BlockingCollection<Action> work = new BlockingCollection<Action>();

            public UiThread()
            {
                var t = new Thread(() =>
                {
                    foreach (Action a in work.GetConsumingEnumerable())
                    {
                        try { a(); }
                        catch (Exception ex) { Console.WriteLine("ui thread: " + ex.GetType().Name + ": " + ex.Message); }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                work.Add(() => d(state));
            }

            public void Flush()
            {
                var done = new ManualResetEvent(false);
                work.Add(() => done.Set());
                done.WaitOne(5000);
            }

            public void Dispose()
            {
                work.CompleteAdding();
            }
        }

        // T1 + T9: the same mock as fix-reset, the controller's own handlers on the session
        static int FixCtlReset(int port, string log)
        {
            if (CtlMissing()) return FixVerdict();
            Type ctlT = CtlType;
            FieldInfo context = Type.GetType("BecquerelMonitor.MainForm, BecquerelMonitor").GetField("originalContext", BindingFlags.Public | BindingFlags.Static);
            var ui = new UiThread();
            context.SetValue(null, ui);
            object ctl = Activator.CreateInstance(ctlT);
            object rd = NewResultData(8192, 40000);
            StatusSet(rd, "Recording", true);
            ctlT.GetField("resultData", NonPub).SetValue(ctl, rd);
            ctlT.GetField("deviceGuid", NonPub).SetValue(ctl, "pr37-fix-ctl-reset");
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 1000, null);
            s.DataReady += (EventHandler<AtomSpectraTcpDataReadyArgs>)Delegate.CreateDelegate(typeof(EventHandler<AtomSpectraTcpDataReadyArgs>), ctl, ctlT.GetMethod("Acquisition_DataReady", NonPub));
            s.BoardReset += (EventHandler<AtomSpectraTcpBoardResetArgs>)Delegate.CreateDelegate(typeof(EventHandler<AtomSpectraTcpBoardResetArgs>), ctl, ctlT.GetMethod("Session_BoardReset", NonPub));
            int resets = 0;
            s.BoardReset += (o, e) => Interlocked.Increment(ref resets);
            ctlT.GetField("session", NonPub).SetValue(ctl, s);
            var outcome = s.Begin(false, 1000);
            FixCheck(outcome == AtomSpectraTcpBeginOutcome.Started, "Start is done (" + outcome + ")");
            WaitUntil(() => Volatile.Read(ref resets) > 0, 15000);
            Thread.Sleep(1500);
            ui.Flush();
            double seconds = StatusGet<TimeSpan>(rd, "ElapsedTime").TotalSeconds;
            object spec = rd.GetType().GetProperty("EnergySpectrum").GetValue(rd);
            long valid = (long)spec.GetType().GetProperty("ValidPulseCount").GetValue(spec);
            bool recording = StatusGet<bool>(rd, "Recording");
            var cmds = ReadCommands(log);
            bool logged = AtomSpectraTcpLog.Snapshot().Any(l => l.Text.Contains("board reset from outside"));
            Console.WriteLine("document: " + seconds.ToString("R", CultureInfo.InvariantCulture) + " s, " + valid.ToString(CultureInfo.InvariantCulture)
                + " counts, recording " + recording + "; session open " + s.IsOpen + "; resets " + resets + "; commands " + Joined(cmds));
            FixCheck(resets == 1, "the reset from outside is found");
            FixCheck(!recording, "the recording is stopped");
            FixCheck(seconds >= 2 && valid > 100000000000L, "the document keeps the data before the reset (board time 1 s and lower counts never copied)");
            FixCheck(!s.IsOpen, "the session is detached");
            FixCheck(!cmds.Any(kv => kv.Value == "-sto" || kv.Value == "-rst"), "no -sto, no -rst: the acquisition on the board is not the document's any more");
            FixCheck(logged, "without windows: a line in the log instead of a window");
            context.SetValue(null, null);
            ui.Dispose();
            s.Dispose();
            return FixVerdict();
        }
    }
}
