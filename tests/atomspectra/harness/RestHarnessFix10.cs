using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix10(string scenario)
        {
            switch (scenario)
            {
                case "console-tcp-fix10-stop-reenter":
                    try
                    {
                        var s = new BecquerelMonitor.AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new BecquerelMonitor.AtomSpectraTcpTransport("127.0.0.1", port, 2000));
                        s.Ask = q => BecquerelMonitor.AtomSpectraRestStartChoice.Continue;
                        var o = s.Begin(true, 2000);
                        Check(o == BecquerelMonitor.AtomSpectraTcpBeginOutcome.Started && s.JoinedRunning, "a counting board, Continue: joined (outcome " + o + ")");
                        Thread.Sleep(500);
                        var nested = BecquerelMonitor.AtomSpectraTcpStopOutcome.Stopped;
                        bool nestedRan = false;
                        s.AskStopJoined = () => { s.AskStopJoined = null; nested = s.Stop(1000); nestedRan = true; return true; };
                        var so = s.Stop(1000);
                        Thread.Sleep(300);
                        var cmds = Requests().Select(r => Str(r, "cmd")).Where(x => !string.IsNullOrEmpty(x)).ToList();
                        Check(nestedRan && nested == BecquerelMonitor.AtomSpectraTcpStopOutcome.NotRunning, "the Stop inside the open question is refused, it does not close the session (nested outcome " + nested + ")");
                        Check(cmds.Contains("-sto") && (so == BecquerelMonitor.AtomSpectraTcpStopOutcome.Stopped || so == BecquerelMonitor.AtomSpectraTcpStopOutcome.StoppedAfterReconnect), "the answer 'stop on the board' still sends -sto (outcome " + so + ", commands " + string.Join(" ", cmds) + ")");
                        s.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
                    }
                    return true;

                case "fix10-dns-chain":
                    try
                    {
                        var p = new List<byte>(Fix10Header(2));
                        // answer 1: the name "a", an unknown type 99 whose data (the chain) is skipped by the parser; answer 2: a name that points into the chain
                        p.AddRange(new byte[] { 1, 0x61, 0, 0, 99, 0, 1, 0, 0, 0, 120, (byte)(8800 >> 8), (byte)(8800 & 0xFF) });
                        for (int j = 0; j < 4400; j++)
                        {
                            int target = j == 0 ? 12 : 25 + 2 * (j - 1);   // entry j sits at 25 + 2j and points to the entry before it, entry 0 to the name "a" at 12
                            p.Add((byte)(0xC0 | (target >> 8)));
                            p.Add((byte)(target & 0xFF));
                        }
                        int last = 25 + 2 * 4399;
                        Fix10AddA(p, new byte[] { (byte)(0xC0 | (last >> 8)), (byte)(last & 0xFF) }, 10, 0, 0, 1);
                        Check(p.Count <= 9000, "the packet fits the 9000 byte receive buffer (" + p.Count + " bytes)");
                        var sw = Stopwatch.StartNew();
                        List<BecquerelMonitor.AtomSpectraDnsRecord> recs = null;
                        var th = new Thread(() => { recs = Fix10Parse(p); }, 256 * 1024);   // a worker thread with a small stack: a recursion over the pointers overflows it and ends the process
                        th.Start();
                        th.Join();
                        sw.Stop();
                        Check(recs != null && recs.Count == 1, "a name made of a chain of pointers is refused, only the first answer comes out (records " + (recs == null ? -1 : recs.Count) + ")");
                        Check(sw.ElapsedMilliseconds < 250, "the parse of such a packet is bounded in time (" + sw.ElapsedMilliseconds + " ms)");
                    }
                    catch (Exception ex)
                    {
                        Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
                    }
                    return true;

                case "fix10-dns-limit":
                    try
                    {
                        byte[] Labels(int count, int len)
                        {
                            var b = new List<byte>();
                            for (int i = 0; i < count; i++)
                            {
                                b.Add((byte)len);
                                for (int j = 0; j < len; j++) b.Add((byte)'a');
                            }
                            b.Add(0);
                            return b.ToArray();
                        }

                        var p1 = new List<byte>(Fix10Header(1));
                        Fix10AddA(p1, Labels(5, 63), 10, 0, 0, 1);
                        var r1 = Fix10Parse(p1);
                        Check(r1.Count == 0, "a name longer than 255 bytes is refused (records " + r1.Count + ")");

                        var p2 = new List<byte>(Fix10Header(1));
                        Fix10AddA(p2, Labels(3, 63), 10, 0, 0, 1);
                        var r2 = Fix10Parse(p2);
                        Check(r2.Count == 1 && r2[0].Name.Length == 191, "a name of 191 bytes is accepted (records " + r2.Count + ")");
                    }
                    catch (Exception ex)
                    {
                        Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
                    }
                    return true;

                case "fix10-dns-good":
                    try
                    {
                        var p = new List<byte>(Fix10Header(2));
                        var name1 = new List<byte>();
                        name1.Add(11);
                        foreach (char c in "atomspectra") name1.Add((byte)c);
                        name1.Add(5);
                        foreach (char c in "local") name1.Add((byte)c);
                        name1.Add(0);
                        Fix10AddA(p, name1.ToArray(), 192, 0, 2, 7);
                        Fix10AddA(p, new byte[] { 0xC0, 0x0C }, 192, 0, 2, 8);
                        var recs = Fix10Parse(p);
                        Check(recs.Count == 2, "two records are parsed (records " + recs.Count + ")");
                        Check(recs[0].Name == "atomspectra.local" && recs[0].Address == "192.0.2.7", "the first record is correct (name " + recs[0].Name + ", address " + recs[0].Address + ")");
                        Check(recs[1].Name == "atomspectra.local" && recs[1].Address == "192.0.2.8", "a name given by a compression pointer is read");
                    }
                    catch (Exception ex)
                    {
                        Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
                    }
                    return true;

                case "fix10-reset-to-rest":
                    try
                    {
                        Type ctlT = CtlT;
                        if (ctlT == null)
                        {
                            Console.WriteLine("n/a: the controller is not in this build");
                            return true;
                        }
                        BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
                        MethodInfo open = ctlT.GetMethod("OpenSession", NP);
                        FieldInfo fSession = ctlT.GetField("session", NP);
                        Check(open != null && fSession != null, "the controller has OpenSession and the session field");
                        if (open == null || fSession == null) return true;

                        object Tcp()
                        {
                            var cfg = new BecquerelMonitor.AtomSpectraTcpDeviceConfig();
                            cfg.Host = "board-a.local";
                            cfg.TcpPort = 8234;
                            return cfg;
                        }

                        object Rest()
                        {
                            var cfg = new BecquerelMonitor.AtomSpectraTcpDeviceConfig();
                            cfg.Host = "board-a.local";
                            cfg.Transport = BecquerelMonitor.AtomSpectraTransport.Rest;
                            cfg.RestUrl = "http://127.0.0.1:9/";
                            return cfg;
                        }

                        object ctl = Activator.CreateInstance(ctlT);
                        open.Invoke(ctl, new object[] { Tcp() });
                        var s1 = (BecquerelMonitor.AtomSpectraTcpSession)fSession.GetValue(ctl);
                        Check(s1 != null, "the TCP session is created");
                        if (s1 == null) return true;

                        s1.Clear(1000);
                        Check(s1.ResetOnNextStart, "Clear without an open acquisition leaves the reset for the next Start");

                        open.Invoke(ctl, new object[] { Rest() });
                        open.Invoke(ctl, new object[] { Tcp() });
                        var s2 = (BecquerelMonitor.AtomSpectraTcpSession)fSession.GetValue(ctl);
                        Check(s2 != null && !ReferenceEquals(s1, s2), "a new TCP session is made after the switch to REST and back");
                        Check(s2 != null && !s2.ResetOnNextStart, "the deferred reset is dropped at the switch to REST: the new TCP session does not reset the board");
                        if (s2 != null) s2.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Check(false, "exception: " + ex.GetType().Name + " " + ex.Message);
                    }
                    return true;

                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }

        static byte[] Fix10Header(int ancount)
        {
            var b = new byte[12];
            b[0] = 0x12;
            b[1] = 0x34;
            b[2] = 0x84;
            b[3] = 0x00;
            b[4] = 0x00;
            b[5] = 0x00;
            b[6] = (byte)(ancount >> 8);
            b[7] = (byte)(ancount & 0xFF);
            b[8] = 0x00;
            b[9] = 0x00;
            b[10] = 0x00;
            b[11] = 0x00;
            return b;
        }

        static void Fix10AddA(List<byte> p, byte[] nameBytes, byte a, byte b, byte c, byte d)
        {
            p.AddRange(nameBytes);
            p.Add(0x00);
            p.Add(0x01);
            p.Add(0x00);
            p.Add(0x01);
            p.Add(0x00);
            p.Add(0x00);
            p.Add(0x00);
            p.Add(120);
            p.Add(0x00);
            p.Add(0x04);
            p.Add(a);
            p.Add(b);
            p.Add(c);
            p.Add(d);
        }

        static List<BecquerelMonitor.AtomSpectraDnsRecord> Fix10Parse(List<byte> packet)
        {
            return BecquerelMonitor.AtomSpectraDnsSd.ParseRecords(packet.ToArray(), packet.Count);
        }
    }
}
