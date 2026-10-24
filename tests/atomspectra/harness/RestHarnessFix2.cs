using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static void RunFix2Tcp(bool newDocument, out int asked, out AtomSpectraTcpBeginOutcome outcome, out List<string> cmds)
        {
            var s = new AtomSpectraTcpSession("127.0.0.1", port, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", port, 2000));
            int n = 0;
            s.Ask = q => { n++; return AtomSpectraRestStartChoice.Cancel; };
            s.DocumentSeconds = 100;
            s.DocumentCounts = 1000;
            outcome = s.Begin(newDocument, 2000);
            Thread.Sleep(500);
            cmds = Requests().Select(r => Str(r, "cmd")).Where(x => !string.IsNullOrEmpty(x)).ToList();
            asked = n;
            s.Dispose();
        }

        static bool RunFix2(string scenario)
        {
            switch (scenario)
            {
                // fix2-stop-auto
                case "fix2-stop-auto":
                {
                    Type ctlT = CtlT;
                    if (ctlT == null)
                    {
                        Console.WriteLine("n/a: controller type missing");
                        return true;
                    }

                    Type rdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
                    if (rdT == null)
                    {
                        Check(false, "ResultData type is missing");
                        return true;
                    }
                    object rd = Activator.CreateInstance(rdT);

                    var s = new AtomSpectraTcpSession("127.0.0.1", 9, 1000, 0, () => new AtomSpectraTcpTransport("127.0.0.1", 9, 2000));
                    object ctl = Activator.CreateInstance(ctlT);
                    SetMember(ctlT, ctl, "session", s);

                    FieldInfo wf = ctlT.GetField("WindowsForStopQuestion", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    Check(wf != null, "the controller has the windows seam of the Stop question");
                    if (wf == null)
                    {
                        s.Dispose();
                        return true;
                    }

                    object oldW = wf.GetValue(null);
                    try
                    {
                        wf.SetValue(null, new Func<bool>(() => true));

                        MethodInfo two = ctlT.GetMethod("StopMeasurement", new Type[] { rdT, typeof(bool) });
                        MethodInfo one = ctlT.GetMethod("StopMeasurement", new Type[] { rdT });
                        Check(two != null, "StopMeasurement(ResultData, bool manual) exists");
                        if (two == null)
                        {
                            return true;
                        }

                        PropertyInfo ask = typeof(AtomSpectraTcpSession).GetProperty("AskStopJoined");
                        Check(ask != null, "AtomSpectraTcpSession.AskStopJoined property exists");
                        if (ask == null)
                        {
                            return true;
                        }

                        two.Invoke(ctl, new object[] { rd, true });
                        Check(ask.GetValue(s, null) != null, "a manual Stop with a window: the question is armed");

                        ask.SetValue(s, null, null);
                        two.Invoke(ctl, new object[] { rd, false });
                        Check(ask.GetValue(s, null) == null, "the preset/timer Stop (manual=false): no question");

                        ask.SetValue(s, new Func<bool>(() => true), null);
                        one.Invoke(ctl, new object[] { rd });
                        Check(ask.GetValue(s, null) == null, "the one-argument StopMeasurement (every non-manual caller): no question");
                    }
                    finally
                    {
                        wf.SetValue(null, oldW);
                        s.Dispose();
                    }
                    return true;
                }

                // fix2-after-wait-held
                case "fix2-after-wait-held":
                {
                    Type ui = Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor");
                    if (ui == null)
                    {
                        Console.WriteLine("n/a: AtomSpectraRestUi type missing");
                        return true;
                    }

                    FieldInfo cw = ui.GetField("currentWait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    FieldInfo after = ui.GetField("afterWait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo afterWaitM = ui.GetMethod("AfterWait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(Action) }, null);
                    MethodInfo run = ui.GetMethod("RunAfterWait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    Check(cw != null, "AtomSpectraRestUi.currentWait field exists");
                    Check(after != null, "AtomSpectraRestUi.afterWait field exists");
                    Check(afterWaitM != null, "AtomSpectraRestUi.AfterWait method exists");
                    Check(run != null, "AtomSpectraRestUi.RunAfterWait method exists");

                    if (cw == null || after == null || afterWaitM == null || run == null)
                    {
                        return true;
                    }

                    using (Form f1 = new Form())
                    using (Form f2 = new Form())
                    {
                        try
                        {
                            cw.SetValue(null, f1);
                            int ran = 0;
                            afterWaitM.Invoke(null, new object[] { new Action(() => { ran++; }) });
                            int count1 = ((ICollection)after.GetValue(null)).Count;
                            Check(ran == 0 && count1 == 1, "an action asked while a wait window is open is held");

                            cw.SetValue(null, f2);
                            run.Invoke(null, null);
                            int count2 = ((ICollection)after.GetValue(null)).Count;
                            Check(ran == 0 && count2 == 1, "the held action is NOT run over a new wait window: held again");

                            cw.SetValue(null, null);
                            run.Invoke(null, null);
                            int count3 = ((ICollection)after.GetValue(null)).Count;
                            Check(ran == 1 && count3 == 0, "with no wait window the held action runs once");
                        }
                        finally
                        {
                            cw.SetValue(null, null);
                        }
                    }
                    return true;
                }

                // bm10-refresh-typed
                case "bm10-refresh-typed":
                {
                    Assembly asm = typeof(AtomSpectraDeviceConsole).Assembly;
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing");
                        return true;
                    }

                    object e1 = NewEntry(te, "127.0.0.2", "atomspectra.local", "00000051", port);
                    object e2 = NewEntry(te, "127.0.0.3", "atomspectra-2.local", "00000119", port);
                    var boards = new List<object> { e1, e2 };

                    // Case 1: typed "myboard.local"
                    var ch1 = new FakeChooser { WantSerial = "00000119" };
                    var res1 = new List<string>();
                    var r1 = RefreshWith(asm, boards, ch1, res1, "myboard.local");
                    if (r1 == null) return true;
                    Check(ch1.Calls == 0, "a typed name: no choice window");
                    Check(res1.Contains("myboard.local"), "a typed name: that very name is resolved");
                    Check((bool)GetF(r1, "Found") && S(r1, "Host") == "127.0.0.2", "a typed name: the board it resolves to is read, not a browsed one");

                    // Case 2: typed IP "127.0.0.3"
                    var ch2 = new FakeChooser { WantSerial = "00000051" };
                    var res2 = new List<string>();
                    var r2 = RefreshWith(asm, boards, ch2, res2, "127.0.0.3");
                    if (r2 == null) return true;
                    Check(ch2.Calls == 0, "a typed IP: no choice window");
                    Check(res2.Contains("127.0.0.3"), "a typed IP: that very IP is resolved");
                    Check((bool)GetF(r2, "Found") && S(r2, "Host") == "127.0.0.3", "a typed IP: the board it resolves to is read");

                    // Case 3: typed "atomspectra" (default name without .local)
                    var ch3 = new FakeChooser { WantSerial = "00000119" };
                    var res3 = new List<string>();
                    var r3 = RefreshWith(asm, boards, ch3, res3, "atomspectra");
                    if (r3 == null) return true;
                    Check(ch3.Calls == 1 && ch3.LastCount == 2, "the default name browses: two boards, the choice window is offered");
                    Check((bool)GetF(r3, "Found") && S(r3, "Host") == "127.0.0.3", "the default name: the chosen board is read");

                    // Case 4: typed "" (empty string)
                    var ch4 = new FakeChooser { WantSerial = "00000119" };
                    var res4 = new List<string>();
                    var r4 = RefreshWith(asm, boards, ch4, res4, "");
                    if (r4 == null) return true;
                    Check(ch4.Calls == 1 && ch4.LastCount == 2, "the empty name browses: two boards, the choice window is offered");
                    Check((bool)GetF(r4, "Found") && S(r4, "Host") == "127.0.0.3", "the empty name: the chosen board is read");

                    return true;
                }

                // bm10-dot-instance
                case "bm10-dot-instance":
                {
                    Type t = typeof(AtomSpectraDeviceConsole).Assembly.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    if (t == null)
                    {
                        Check(false, "AtomSpectraDnsSd type is missing");
                        return true;
                    }

                    MethodInfo bq = t.GetMethod("BuildQuery", BindingFlags.Public | BindingFlags.Static);
                    if (bq == null)
                    {
                        Check(false, "AtomSpectraDnsSd.BuildQuery method is missing");
                        return true;
                    }

                    Func<string, int, byte[]> build = (name, qtype) => (byte[])bq.Invoke(null, new object[] { (ushort)1, new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(name, qtype) } });

                    Func<byte[], List<string>> labels = (bytes) =>
                    {
                        var list = new List<string>();
                        int offset = 12;
                        while (offset < bytes.Length)
                        {
                            byte len = bytes[offset];
                            if (len == 0) break;
                            offset++;
                            if (offset + len > bytes.Length) break;
                            string label = System.Text.Encoding.UTF8.GetString(bytes, offset, len);
                            list.Add(label);
                            offset += len;
                        }
                        return list;
                    };

                    // SRV question for "My.Board._http._tcp.local"
                    byte[] pkt1 = build("My.Board._http._tcp.local", 33);
                    var lbl1 = labels(pkt1);
                    string joined1 = string.Join("|", lbl1);
                    Check(joined1 == "My.Board|_http|_tcp|local", "SRV question for dotted instance: labels are " + joined1);

                    // TXT question for "Gateway.v2._atomspectra._tcp.local"
                    byte[] pkt2 = build("Gateway.v2._atomspectra._tcp.local", 16);
                    var lbl2 = labels(pkt2);
                    string joined2 = string.Join("|", lbl2);
                    Check(joined2 == "Gateway.v2|_atomspectra|_tcp|local", "TXT question for dotted instance: labels are " + joined2);

                    // Control, PTR question (type 12) for "_http._tcp.local"
                    byte[] pkt3 = build("_http._tcp.local", 12);
                    var lbl3 = labels(pkt3);
                    string joined3 = string.Join("|", lbl3);
                    Check(joined3 == "_http|_tcp|local", "PTR question control: labels are " + joined3);

                    // Control, SRV question for an instance without a dot "AtomSpectra Gateway._http._tcp.local"
                    byte[] pkt4 = build("AtomSpectra Gateway._http._tcp.local", 33);
                    var lbl4 = labels(pkt4);
                    string joined4 = string.Join("|", lbl4);
                    Check(joined4 == "AtomSpectra Gateway|_http|_tcp|local", "SRV question for non-dotted instance: labels are " + joined4);

                    return true;
                }

                // bm10-bad-label
                case "bm10-bad-label":
                {
                    Type t = typeof(AtomSpectraDeviceConsole).Assembly.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    if (t == null)
                    {
                        Check(false, "AtomSpectraDnsSd type is missing");
                        return true;
                    }

                    MethodInfo pr = t.GetMethod("ParseRecords", BindingFlags.Public | BindingFlags.Static);
                    if (pr == null)
                    {
                        Check(false, "AtomSpectraDnsSd.ParseRecords method is missing");
                        return true;
                    }

                    // Helper to build a packet with two A records
                    // Header: id 0x0000, flags 0x84 0x00, qdcount 0, ancount 2, nscount 0, arcount 0
                    // Record 1: name 'a' 'local', type A, class IN, ttl 0, rdlength 4, addr 192.0.2.1
                    // Record 2: name label of length L (bytes 'x'), type A, class IN, ttl 0, rdlength 4, addr 192.0.2.2
                    
                    Action<int, List<byte>> addPacket = (L, bytes) =>
                    {
                        // Header
                        bytes.Add(0x00); bytes.Add(0x00); // id
                        bytes.Add(0x84); bytes.Add(0x00); // flags
                        bytes.Add(0x00); bytes.Add(0x00); // qdcount
                        bytes.Add(0x00); bytes.Add(0x02); // ancount
                        bytes.Add(0x00); bytes.Add(0x00); // nscount
                        bytes.Add(0x00); bytes.Add(0x00); // arcount

                        // Record 1
                        bytes.Add(0x01); bytes.Add((byte)'a'); // label 'a'
                        bytes.Add(0x05); bytes.Add((byte)'l'); bytes.Add((byte)'o'); bytes.Add((byte)'c'); bytes.Add((byte)'a'); bytes.Add((byte)'l'); // label 'local'
                        bytes.Add(0x00); // end of name
                        bytes.Add(0x00); bytes.Add(0x01); // type A
                        bytes.Add(0x00); bytes.Add(0x01); // class IN
                        bytes.Add(0x00); bytes.Add(0x00); bytes.Add(0x00); bytes.Add(0x00); // ttl
                        bytes.Add(0x00); bytes.Add(0x04); // rdlength
                        bytes.Add(192); bytes.Add(0); bytes.Add(2); bytes.Add(1); // addr 192.0.2.1

                        // Record 2
                        bytes.Add((byte)L);
                        for (int i = 0; i < L; i++)
                        {
                            bytes.Add((byte)'x');
                        }
                        bytes.Add(0x00); // end of name
                        bytes.Add(0x00); bytes.Add(0x01); // type A
                        bytes.Add(0x00); bytes.Add(0x01); // class IN
                        bytes.Add(0x00); bytes.Add(0x00); bytes.Add(0x00); bytes.Add(0x00); // ttl
                        bytes.Add(0x00); bytes.Add(0x04); // rdlength
                        bytes.Add(192); bytes.Add(0); bytes.Add(2); bytes.Add(2); // addr 192.0.2.2
                    };

                    // L = 65 (bad)
                    var bytes65 = new List<byte>();
                    addPacket(65, bytes65);
                    try
                    {
                        var res65 = (IList)pr.Invoke(null, new object[] { bytes65.ToArray(), bytes65.Count });
                        Check(res65.Count == 1, "a label of length 65: the packet is not parsed past it (records " + res65.Count + ")");
                    }
                    catch (TargetInvocationException)
                    {
                        Check(false, "an exception came out of ParseRecords for L=65");
                        return true;
                    }

                    // L = 63 (valid, control)
                    var bytes63 = new List<byte>();
                    addPacket(63, bytes63);
                    try
                    {
                        var res63 = (IList)pr.Invoke(null, new object[] { bytes63.ToArray(), bytes63.Count });
                        Check(res63.Count == 2, "a label of length 63 is a valid label: both records taken (records " + res63.Count + ")");
                    }
                    catch (TargetInvocationException)
                    {
                        Check(false, "an exception came out of ParseRecords for L=63");
                        return true;
                    }

                    // L = 191 (bad)
                    var bytes191 = new List<byte>();
                    addPacket(191, bytes191);
                    try
                    {
                        var res191 = (IList)pr.Invoke(null, new object[] { bytes191.ToArray(), bytes191.Count });
                        Check(res191.Count == 1, "a label of length 191: the packet is not parsed past it (records " + res191.Count + ")");
                    }
                    catch (TargetInvocationException)
                    {
                        Check(false, "an exception came out of ParseRecords for L=191");
                        return true;
                    }

                    return true;
                }

                // bm10-flood-cap
                case "bm10-flood-cap":
                {
                    Assembly asm = typeof(AtomSpectraDeviceConsole).Assembly;
                    Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    if (t == null)
                    {
                        Check(false, "AtomSpectraDnsSd type is missing");
                        return true;
                    }

                    FieldInfo rsF = t.GetField("ReadSerial", BindingFlags.Public | BindingFlags.Static);
                    if (rsF == null)
                    {
                        Check(false, "AtomSpectraDnsSd.ReadSerial field is missing");
                        return true;
                    }

                    object oldRs = rsF.GetValue(null);
                    try
                    {
                        rsF.SetValue(null, new Func<string, int, string>((a, p) => "SN"));
                        var l = BrowseFake(asm);
                        if (l == null) return true;
                        Check(l.Count == 16, "40 instances announced: 16 are taken (found " + l.Count + ")");
                    }
                    finally
                    {
                        rsF.SetValue(null, oldRs);
                    }
                    return true;
                }

                // bm10-flood-budget
                case "bm10-flood-budget":
                {
                    Assembly asm = typeof(AtomSpectraDeviceConsole).Assembly;
                    Type t = asm.GetType("BecquerelMonitor.AtomSpectraDnsSd");
                    if (t == null)
                    {
                        Check(false, "AtomSpectraDnsSd type is missing");
                        return true;
                    }

                    FieldInfo stF = t.GetField("SerialTotalMs", BindingFlags.Public | BindingFlags.Static);
                    if (stF == null)
                    {
                        Check(false, "AtomSpectraDnsSd.SerialTotalMs field is missing");
                        return true;
                    }

                    FieldInfo rsF = t.GetField("ReadSerial", BindingFlags.Public | BindingFlags.Static);
                    if (rsF == null)
                    {
                        Check(false, "AtomSpectraDnsSd.ReadSerial field is missing");
                        return true;
                    }

                    object oldSt = stF.GetValue(null);
                    object oldRs = rsF.GetValue(null);
                    int[] calls = new int[1];
                    try
                    {
                        stF.SetValue(null, 400);
                        rsF.SetValue(null, new Func<string, int, string>((a, p) =>
                        {
                            Interlocked.Increment(ref calls[0]);
                            Thread.Sleep(200);
                            return "SN";
                        }));

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var l = BrowseFake(asm);
                        sw.Stop();

                        if (l == null) return true;
                        Check(l.Count >= 1, "boards found (" + l.Count + ")");
                        Check(calls[0] >= 1 && calls[0] <= 3, "the serial reads stop at the total budget: " + calls[0] + " reads of 200 ms with a budget of 400 ms");
                    }
                    finally
                    {
                        stF.SetValue(null, oldSt);
                        rsF.SetValue(null, oldRs);
                    }
                    return true;
                }

                // console-tcp-fix2-silent
                case "console-tcp-fix2-silent":
                {
                    int asked;
                    AtomSpectraTcpBeginOutcome outcome;
                    List<string> cmds;
                    RunFix2Tcp(false, out asked, out outcome, out cmds);
                    Console.WriteLine("Outcome: " + outcome + ", Commands: " + string.Join(" ", cmds));
                    Check(asked == 0, "the same document after its own Stop, the bridge silent: no question");
                    Check(cmds.Contains("-sta") && !cmds.Contains("-rst") && !cmds.Contains("-sto"), "the same document, the bridge silent: -sta at once, as before (commands " + string.Join(" ", cmds) + ")");
                    Check(outcome != AtomSpectraTcpBeginOutcome.Cancelled && outcome != AtomSpectraTcpBeginOutcome.Refused, "Begin is not cancelled or refused (outcome " + outcome + ")");
                    return true;
                }

                // console-tcp-fix2-silent-new
                case "console-tcp-fix2-silent-new":
                {
                    int asked;
                    AtomSpectraTcpBeginOutcome outcome;
                    List<string> cmds;
                    RunFix2Tcp(true, out asked, out outcome, out cmds);
                    Console.WriteLine("Outcome: " + outcome + ", Commands: " + string.Join(" ", cmds));
                    Check(asked == 1, "a NEW document and a silent bridge: the question is asked (control)");
                    Check(!cmds.Contains("-sta"), "a NEW document, the question answered Cancel: -sta not sent");
                    return true;
                }

                // console-tcp-fix2-uncertain
                case "console-tcp-fix2-uncertain":
                {
                    int asked;
                    AtomSpectraTcpBeginOutcome outcome;
                    List<string> cmds;
                    RunFix2Tcp(false, out asked, out outcome, out cmds);
                    Console.WriteLine("Outcome: " + outcome + ", Commands: " + string.Join(" ", cmds));
                    Check(asked == 1, "the same document, an uncertain bridge: the question is asked (" + asked + ")");
                    Check(!cmds.Contains("-sta") && outcome == AtomSpectraTcpBeginOutcome.Cancelled, "Cancel: nothing is sent (outcome " + outcome + ", commands " + string.Join(" ", cmds) + ")");
                    return true;
                }

                default:
                    return false;
            }
        }
    }
}
