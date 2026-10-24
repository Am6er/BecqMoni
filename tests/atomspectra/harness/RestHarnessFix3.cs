using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunFix3(string scenario)
        {
            switch (scenario)
            {
                case "fix3-name-to-search":
                {
                    Type finder = typeof(AtomSpectraDeviceConsole).Assembly.GetType("BecquerelMonitor.AtomSpectraBoardFinder");
                    if (finder == null)
                    {
                        Check(false, "AtomSpectraBoardFinder is missing");
                        return true;
                    }

                    MethodInfo nameToSearch = finder.GetMethod("NameToSearch", new Type[] { typeof(string), typeof(int) });
                    MethodInfo isDefaultName = finder.GetMethod("IsDefaultName", new Type[] { typeof(string) });

                    if (nameToSearch == null)
                    {
                        Check(false, "AtomSpectraBoardFinder.NameToSearch exists");
                        return true;
                    }
                    Check(true, "AtomSpectraBoardFinder.NameToSearch exists");

                    if (isDefaultName == null)
                    {
                        Check(false, "AtomSpectraBoardFinder.IsDefaultName exists");
                        return true;
                    }
                    Check(true, "AtomSpectraBoardFinder.IsDefaultName exists");

                    bool Browses(string text)
                    {
                        object name = nameToSearch.Invoke(null, new object[] { text, 8080 });
                        object result = isDefaultName.Invoke(null, new object[] { name });
                        return (bool)result;
                    }

                    Check(Browses(""), "Browses(\"\") is true");
                    Check(Browses("   "), "Browses(\"   \") is true");
                    Check(Browses("atomspectra"), "Browses(\"atomspectra\") is true");
                    Check(Browses("atomspectra.local"), "Browses(\"atomspectra.local\") is true");
                    Check(Browses("ATOMSPECTRA.Local"), "Browses(\"ATOMSPECTRA.Local\") is true");
                    Check(Browses("  atomspectra.local  "), "Browses(\"  atomspectra.local  \") is true");
                    Check(Browses("atomspectra.local."), "Browses(\"atomspectra.local.\") is true");
                    Check(Browses("atomspectra."), "Browses(\"atomspectra.\") is true");
                    Check(Browses(" AtomSpectra.LOCAL. "), "Browses(\" AtomSpectra.LOCAL. \") is true");
                    Check(Browses("atomspectra.local:8080"), "Browses(\"atomspectra.local:8080\") is true");

                    Check(!Browses("http://myboard.local"), "Browses(\"http://myboard.local\") is false");
                    Check(!Browses("http://atomspectra.local"), "Browses(\"http://atomspectra.local\") is false");
                    Check(!Browses("my_board"), "Browses(\"my_board\") is false");
                    Check(!Browses("my_board.local"), "Browses(\"my_board.local\") is false");
                    Check(!Browses("myboard.local"), "Browses(\"myboard.local\") is false");
                    Check(!Browses("192.0.2.5"), "Browses(\"192.0.2.5\") is false");
                    Check(!Browses("127.0.0.1:8080"), "Browses(\"127.0.0.1:8080\") is false");
                    Check(!Browses("[fe80::1]"), "Browses(\"[fe80::1]\") is false");
                    Check(!Browses("localhost"), "Browses(\"localhost\") is false");
                    Check(!Browses("http://atomspectra.local/"), "Browses(\"http://atomspectra.local/\") is false");

                    object nts1 = nameToSearch.Invoke(null, new object[] { "http://myboard.local", 8080 });
                    Check((string)nts1 == "http://myboard.local", "NameToSearch(\"http://myboard.local\", 8080) == \"http://myboard.local\"");
                    object nts2 = nameToSearch.Invoke(null, new object[] { "  my_board ", 8080 });
                    Check((string)nts2 == "my_board", "NameToSearch(\"  my_board \", 8080) == \"my_board\"");
                    object nts3 = nameToSearch.Invoke(null, new object[] { "", 8080 });
                    Check((string)nts3 == "atomspectra.local", "NameToSearch(\"\", 8080) == \"atomspectra.local\"");

                    Assembly asm = finder.Assembly;
                    Type te = asm.GetType("BecquerelMonitor.AtomSpectraBoardEntry");
                    if (te == null)
                    {
                        Check(false, "AtomSpectraBoardEntry is missing");
                        return true;
                    }

                    object e1 = NewEntry(te, "127.0.0.2", "atomspectra.local", "00000051", port);
                    object e2 = NewEntry(te, "127.0.0.3", "atomspectra-2.local", "00000119", port);
                    List<object> entries = new List<object> { e1, e2 };

                    FakeChooser ch1 = new FakeChooser { WantSerial = "00000119" };
                    List<string> res1 = new List<string>();
                    object r1 = RefreshWith(asm, entries, ch1, res1, (string)nameToSearch.Invoke(null, new object[] { "http://myboard.local", 8080 }));
                    if (r1 == null) return true;
                    Check(res1.Count > 0 && res1[0] == "http://myboard.local", "text that is not an address: resolved as typed first (the browse only after it does not answer)");
                    Check(res1.Contains("http://myboard.local"), "text that is not an address: resolved as typed by the old path");

                    FakeChooser ch2 = new FakeChooser { WantSerial = "00000119" };
                    List<string> res2 = new List<string>();
                    object r2 = RefreshWith(asm, entries, ch2, res2, (string)nameToSearch.Invoke(null, new object[] { "my_board", 8080 }));
                    if (r2 == null) return true;
                    Check(res2.Count > 0 && res2[0] == "my_board", "text that is not an address: resolved as typed first (the browse only after it does not answer)");
                    Check(res2.Contains("my_board"), "text that is not an address: resolved as typed by the old path");

                    FakeChooser ch3 = new FakeChooser { WantSerial = "00000119" };
                    List<string> res3 = new List<string>();
                    object r3 = RefreshWith(asm, entries, ch3, res3, (string)nameToSearch.Invoke(null, new object[] { "atomspectra.local.", 8080 }));
                    if (r3 == null) return true;
                    Check(ch3.Calls == 1, "default name with a dot: browse happens");
                    Check(ch3.LastCount == 2, "default name with a dot: browse sees 2 entries");

                    return true;
                }

                case "fix3-held-throws":
                {
                    Type ui = Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor");
                    if (ui == null)
                    {
                        Console.WriteLine("n/a: AtomSpectraRestUi type missing");
                        return true;
                    }

                    BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    FieldInfo currentWait = ui.GetField("currentWait", flags);
                    FieldInfo afterWait = ui.GetField("afterWait", flags);
                    MethodInfo afterWaitMethod = ui.GetMethod("AfterWait", new Type[] { typeof(Action) });
                    MethodInfo runAfterWait = ui.GetMethod("RunAfterWait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (currentWait == null || afterWait == null || afterWaitMethod == null || runAfterWait == null)
                    {
                        Check(currentWait != null, "currentWait exists");
                        Check(afterWait != null, "afterWait exists");
                        Check(afterWaitMethod != null, "AfterWait exists");
                        Check(runAfterWait != null, "RunAfterWait exists");
                        return true;
                    }

                    using (Form f1 = new Form())
                    {
                        try
                        {
                            currentWait.SetValue(null, f1);
                            int ran2 = 0;
                            int ran3 = 0;

                            afterWaitMethod.Invoke(null, new object[] { new Action(() => { throw new InvalidOperationException("held action failed"); }) });
                            afterWaitMethod.Invoke(null, new object[] { new Action(() => { ran2++; }) });
                            afterWaitMethod.Invoke(null, new object[] { new Action(() => { ran3++; }) });

                            ICollection held = (ICollection)afterWait.GetValue(null);
                            Check(held.Count == 3, "held count is 3");

                            currentWait.SetValue(null, null);
                            try
                            {
                                runAfterWait.Invoke(null, null);
                                Check(true, "a held action that throws does not escape RunAfterWait");
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "a held action that throws does not escape RunAfterWait: " + tie.InnerException.Message);
                            }

                            Check(ran2 == 1 && ran3 == 1, "the actions after the failing one still run (ran " + ran2 + ", " + ran3 + ")");
                            held = (ICollection)afterWait.GetValue(null);
                            Check(held.Count == 0, "no held action is left");

                            // Second round: failing action LAST
                            currentWait.SetValue(null, f1);
                            int ran4 = 0;
                            int ran5 = 0;

                            afterWaitMethod.Invoke(null, new object[] { new Action(() => { ran4++; }) });
                            afterWaitMethod.Invoke(null, new object[] { new Action(() => { ran5++; }) });
                            afterWaitMethod.Invoke(null, new object[] { new Action(() => { throw new InvalidOperationException("held action failed"); }) });

                            currentWait.SetValue(null, null);
                            try
                            {
                                runAfterWait.Invoke(null, null);
                                Check(true, "a held action that throws does not escape RunAfterWait (round 2)");
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "a held action that throws does not escape RunAfterWait (round 2): " + tie.InnerException.Message);
                            }

                            Check(ran4 == 1 && ran5 == 1, "the actions before the failing one still run (ran " + ran4 + ", " + ran5 + ")");
                            held = (ICollection)afterWait.GetValue(null);
                            Check(held.Count == 0, "no held action is left (round 2)");
                        }
                        finally
                        {
                            currentWait.SetValue(null, null);
                        }
                    }

                    return true;
                }

                case "fix3-offer-stop":
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
                    Check(true, "ResultData type exists");

                    BindingFlags instFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    MethodInfo schedule = ctlT.GetMethod("ScheduleBoardOffer", instFlags, null, new Type[] { typeof(string), typeof(string), rdT, typeof(long), typeof(long), typeof(bool) }, null);
                    MethodInfo stop2 = ctlT.GetMethod("StopMeasurement", new Type[] { rdT, typeof(bool) });
                    FieldInfo seam = ctlT.GetField("RecordingSeen", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                    Type ui = Type.GetType("BecquerelMonitor.AtomSpectraRestUi, BecquerelMonitor");
                    if (ui == null)
                    {
                        Check(false, "AtomSpectraRestUi type is missing");
                        return true;
                    }

                    BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    FieldInfo currentWait = ui.GetField("currentWait", staticFlags);
                    FieldInfo afterWait = ui.GetField("afterWait", staticFlags);
                    MethodInfo runAfterWait = ui.GetMethod("RunAfterWait", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (schedule == null || stop2 == null || seam == null || currentWait == null || afterWait == null || runAfterWait == null)
                    {
                        Check(schedule != null, "ScheduleBoardOffer exists");
                        Check(stop2 != null, "StopMeasurement exists");
                        Check(seam != null, "RecordingSeen exists");
                        Check(currentWait != null, "currentWait exists");
                        Check(afterWait != null, "afterWait exists");
                        Check(runAfterWait != null, "RunAfterWait exists");
                        return true;
                    }

                    object oldSeam = seam.GetValue(null);
                    try
                    {
                        // Scenario A
                        object rd = Activator.CreateInstance(rdT);
                        object ctl = Activator.CreateInstance(ctlT);
                        List<bool> seen = new List<bool>();
                        seam.SetValue(null, new Action<bool>(b => seen.Add(b)));

                        using (Form f1 = new Form())
                        {
                            currentWait.SetValue(null, f1);
                            try
                            {
                                schedule.Invoke(ctl, new object[] { "text", "log", rd, 10L, 100L, true });
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "ScheduleBoardOffer threw: " + tie.InnerException.Message);
                            }

                            ICollection held = (ICollection)afterWait.GetValue(null);
                            Check(seen.Count == 0 && held.Count == 1, "the offer is held while the wait window is open");

                            try
                            {
                                stop2.Invoke(ctl, new object[] { rd, true });
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "StopMeasurement threw: " + tie.InnerException.Message);
                            }

                            currentWait.SetValue(null, null);
                            try
                            {
                                runAfterWait.Invoke(null, null);
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "RunAfterWait threw: " + tie.InnerException.Message);
                            }

                            Check(seen.Count == 1 && seen[0] == false, "a recording the user stopped during the wait is not offered a replacement (offer made with recording=" + (seen.Count > 0 ? seen[0].ToString() : "none") + ")");
                        }

                        // Scenario B
                        object rd2 = Activator.CreateInstance(rdT);
                        object ctl2 = Activator.CreateInstance(ctlT);
                        List<bool> seen2 = new List<bool>();
                        seam.SetValue(null, new Action<bool>(b => seen2.Add(b)));

                        using (Form f2 = new Form())
                        {
                            currentWait.SetValue(null, f2);
                            try
                            {
                                schedule.Invoke(ctl2, new object[] { "text", "log", rd2, 10L, 100L, true });
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "ScheduleBoardOffer threw (B): " + tie.InnerException.Message);
                            }

                            currentWait.SetValue(null, null);
                            try
                            {
                                runAfterWait.Invoke(null, null);
                            }
                            catch (TargetInvocationException tie)
                            {
                                Check(false, "RunAfterWait threw (B): " + tie.InnerException.Message);
                            }

                            Check(seen2.Count == 1 && seen2[0] == true, "a recording nobody stopped is offered as before");
                        }

                        // Scenario C
                        object rd3 = Activator.CreateInstance(rdT);
                        object ctl3 = Activator.CreateInstance(ctlT);
                        List<bool> seen3 = new List<bool>();
                        seam.SetValue(null, new Action<bool>(b => seen3.Add(b)));

                        currentWait.SetValue(null, null);
                        try
                        {
                            schedule.Invoke(ctl3, new object[] { "text", "log", rd3, 10L, 100L, false });
                        }
                        catch (TargetInvocationException tie)
                        {
                            Check(false, "ScheduleBoardOffer threw (C): " + tie.InnerException.Message);
                        }

                        Check(seen3.Count == 1 && seen3[0] == false, "a recording already stopped at the post is not offered");

                        // Scenario D
                        object rd4 = Activator.CreateInstance(rdT);
                        object rdOther = Activator.CreateInstance(rdT);
                        object ctl4 = Activator.CreateInstance(ctlT);
                        List<bool> seen4 = new List<bool>();
                        seam.SetValue(null, new Action<bool>(b => seen4.Add(b)));

                        try
                        {
                            stop2.Invoke(ctl4, new object[] { rdOther, true });
                        }
                        catch (TargetInvocationException tie)
                        {
                            Check(false, "StopMeasurement threw (D): " + tie.InnerException.Message);
                        }

                        currentWait.SetValue(null, null);
                        try
                        {
                            schedule.Invoke(ctl4, new object[] { "text", "log", rd4, 10L, 100L, true });
                        }
                        catch (TargetInvocationException tie)
                        {
                            Check(false, "ScheduleBoardOffer threw (D): " + tie.InnerException.Message);
                        }

                        Check(seen4.Count == 1 && seen4[0] == true, "a Stop of another document does not cancel the offer");
                    }
                    finally
                    {
                        seam.SetValue(null, oldSeam);
                        currentWait.SetValue(null, null);
                    }

                    return true;
                }

                default:
                    Console.WriteLine("n/a: unknown scenario " + scenario);
                    return true;
            }
        }
    }
}
