// #BM-21: warning about a board firmware older than 1.2.39 (AtomSpectraFirmware); a warning, never a block.
using System;
using System.Linq;
using System.Reflection;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunBm21(string scenario)
        {
            switch (scenario)
            {
                case "bm21-parse":
                {
                    // Table checks for IsBelowRecommended and related static helpers.
                    Check(AtomSpectraFirmware.RecommendedVersion == "1.2.39", "RecommendedVersion is 1.2.39");

                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2.38") == true, "IsBelowRecommended(\"1.2.38\") is true");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2.39") == false, "IsBelowRecommended(\"1.2.39\") is false");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2.39-t1b") == false, "IsBelowRecommended(\"1.2.39-t1b\") is false");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2.40") == false, "IsBelowRecommended(\"1.2.40\") is false");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.10.0") == false, "IsBelowRecommended(\"1.10.0\") is false");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2.9") == true, "IsBelowRecommended(\"1.2.9\") is true");
                    Check(AtomSpectraFirmware.IsBelowRecommended("0.9.99") == true, "IsBelowRecommended(\"0.9.99\") is true");
                    Check(AtomSpectraFirmware.IsBelowRecommended("2.0.0") == false, "IsBelowRecommended(\"2.0.0\") is false");
                    Check(AtomSpectraFirmware.IsBelowRecommended(" 1.2.38 ") == true, "IsBelowRecommended(\" 1.2.38 \") is true");
                    Check(AtomSpectraFirmware.IsBelowRecommended("?") == null, "IsBelowRecommended(\"?\") is null");
                    Check(AtomSpectraFirmware.IsBelowRecommended("") == null, "IsBelowRecommended(\"\") is null");
                    Check(AtomSpectraFirmware.IsBelowRecommended(null) == null, "IsBelowRecommended(null) is null");
                    Check(AtomSpectraFirmware.IsBelowRecommended("abc") == null, "IsBelowRecommended(\"abc\") is null");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2") == null, "IsBelowRecommended(\"1.2\") is null");
                    Check(AtomSpectraFirmware.IsBelowRecommended("mock-1.2.37") == null, "IsBelowRecommended(\"mock-1.2.37\") is null");
                    Check(AtomSpectraFirmware.IsBelowRecommended("1.2.39.4") == null, "IsBelowRecommended(\"1.2.39.4\") is null");

                    int maj, min, pat;
                    Check(AtomSpectraFirmware.TryParse("1.10.0", out maj, out min, out pat) && maj == 1 && min == 10 && pat == 0, "TryParse(\"1.10.0\") gives (1,10,0)");

                    string wt38 = AtomSpectraFirmware.WarningText("1.2.38");
                    Check(wt38 != null && wt38.Contains("1.2.38") && wt38.Contains("1.2.39"), "WarningText(\"1.2.38\") contains versions");
                    Check(AtomSpectraFirmware.WarningText("1.2.39") == null, "WarningText(\"1.2.39\") is null");
                    Check(AtomSpectraFirmware.WarningText("?") == null, "WarningText(\"?\") is null");
                    Check(AtomSpectraFirmware.WarningText("") == null, "WarningText(\"\") is null");
                    Check(AtomSpectraFirmware.WarningText(null) == null, "WarningText(null) is null");

                    Check(AtomSpectraFirmware.AppendWarning("status", "1.2.39") == "status", "AppendWarning(\"status\", \"1.2.39\") is unchanged");
                    string aw38 = AtomSpectraFirmware.AppendWarning("status", "1.2.38");
                    Check(aw38 != null && aw38.StartsWith("status") && aw38.Contains(Environment.NewLine) && aw38.Contains(wt38), "AppendWarning(\"status\", \"1.2.38\") appends warning");
                    Check(AtomSpectraFirmware.AppendWarning("status", null) == "status", "AppendWarning(\"status\", null) is unchanged");
                    Check(AtomSpectraFirmware.AppendWarning(null, "1.2.38") == wt38, "AppendWarning(null, \"1.2.38\") is warning text");

                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.ConnectFailed) == true, "IsNoAnswerOutcome(ConnectFailed) is true");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.NoAnswer) == true, "IsNoAnswerOutcome(NoAnswer) is true");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.LinkClosed) == true, "IsNoAnswerOutcome(LinkClosed) is true");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.Started) == false, "IsNoAnswerOutcome(Started) is false");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.NoHost) == false, "IsNoAnswerOutcome(NoHost) is false");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.HostBusy) == false, "IsNoAnswerOutcome(HostBusy) is false");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.Cancelled) == false, "IsNoAnswerOutcome(Cancelled) is false");
                    Check(AtomSpectraFirmware.IsNoAnswerOutcome(AtomSpectraTcpBeginOutcome.Refused) == false, "IsNoAnswerOutcome(Refused) is false");
                    return true;
                }
                case "bm21-start-low":
                {
                    // Start fails, firmware is low, warning is included in unreachable text.
                    var s = NewSession(false, 0, null);
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.ConnectFailed, "Begin returns ConnectFailed");
                    Check(s.FirmwareVersion == "1.2.38", "FirmwareVersion is 1.2.38");
                    string t = s.UnreachableText();
                    string hint = AtomSpectraFirmware.WarningText("1.2.38");
                    Check(t != null && t.Contains(hint), "UnreachableText contains warning hint");
                    Check(t != null && t.Contains("1.2.38") && t.Contains("1.2.39"), "UnreachableText contains versions");
                    Check(Requests().Count(r => Str(r, "path") == "/api/status") == 1, "Exactly one /api/status request");
                    s.Dispose();
                    return true;
                }
                case "bm21-start-ok-low":
                {
                    // Start succeeds, firmware is low but not read, no warning in unreachable text.
                    var s = NewSession(false, 0, null);
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                    Check(s.FirmwareVersion == null, "FirmwareVersion is null");
                    Check(Requests().Count(r => Str(r, "path") == "/api/status") == 0, "No /api/status requests");
                    string t = s.UnreachableText();
                    Check(t == null || !t.Contains("1.2.38"), "UnreachableText does not contain 1.2.38");
                    s.Detach();
                    s.Dispose();
                    return true;
                }
                case "bm21-start-fail-new":
                {
                    // Start fails, firmware is recommended, no warning appended.
                    var s = NewSession(false, 0, null);
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.ConnectFailed, "Begin returns ConnectFailed");
                    Check(s.FirmwareVersion == "1.2.39", "FirmwareVersion is 1.2.39");
                    string t = s.UnreachableText();
                    Check(t != null && !t.Contains(Environment.NewLine), "UnreachableText has no newline");
                    Check(t != null && !t.Contains(AtomSpectraFirmware.WarningText("1.2.38")), "UnreachableText does not contain warning");
                    Check(t == AtomSpectraFirmware.AppendWarning(t, null), "UnreachableText equals AppendWarning with null");
                    s.Dispose();
                    return true;
                }
                case "bm21-start-fail-unknown":
                {
                    // Start fails, firmware is unknown, no warning appended.
                    var s = NewSession(false, 0, null);
                    var o = s.Begin(true, 1000);
                    Check(o == AtomSpectraTcpBeginOutcome.ConnectFailed, "Begin returns ConnectFailed");
                    Check(s.FirmwareVersion == "?", "FirmwareVersion is ?");
                    string t = s.UnreachableText();
                    Check(t != null && !t.Contains(Environment.NewLine), "UnreachableText has no newline");
                    Check(t != null && !t.Contains("1.2.39"), "UnreachableText does not contain 1.2.39");
                    Check(AtomSpectraFirmware.IsBelowRecommended(s.FirmwareVersion) == null, "IsBelowRecommended is null");
                    s.Dispose();
                    return true;
                }
                case "bm21-test-low":
                case "bm21-test-new":
                {
                    // Invoke RunRestTest via reflection to check output text.
                    Type formType = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceForm, BecquerelMonitor");
                    if (formType == null)
                    {
                        Console.WriteLine("n/a: AtomSpectraTcpDeviceForm.RunRestTest is not in this build");
                        return true;
                    }
                    MethodInfo mi = formType.GetMethod("RunRestTest", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (mi == null)
                    {
                        Console.WriteLine("n/a: AtomSpectraTcpDeviceForm.RunRestTest is not in this build");
                        return true;
                    }
                    object[] args = new object[] { Client(), null, null };
                    object result = mi.Invoke(null, args);
                    string text = (string)result;

                    if (scenario == "bm21-test-low")
                    {
                        Check(text != null && text.Contains("1.2.38"), "Text contains 1.2.38");
                        string hint = AtomSpectraFirmware.WarningText("1.2.38");
                        Check(text != null && text.Contains(hint), "Text contains warning hint");
                        Check(text != null && text.Contains(Environment.NewLine + hint), "Warning is on its own line");
                        Check(Requests().Count(r => Str(r, "path") == "/api/status") == 1, "Exactly one /api/status request");
                    }
                    else
                    {
                        Check(text != null && text.Contains("1.2.39"), "Text contains 1.2.39");
                        Check(text != null && !text.Contains(AtomSpectraFirmware.WarningText("1.2.38")), "Text does not contain warning");
                        Check(text != null && !text.Contains(Environment.NewLine), "Text has no newline");
                    }
                    return true;
                }
                case "bm21-long":
                {
                    // a version of 140 chars (an anomaly of the board) must not push the hint out of the result label
                    string lv = "1.2.38-" + new string('x', 133);
                    Check(lv.Length == 140 && AtomSpectraFirmware.IsBelowRecommended(lv) == true, "a 140-char version below the threshold");
                    string w = AtomSpectraFirmware.WarningText(lv);
                    int w0 = AtomSpectraFirmware.WarningText("1.2.38").Length;
                    Check(w != null && w.Length <= w0 + 40, "the hint grows by at most 40 chars (" + (w == null ? -1 : w.Length) + " vs " + w0 + ")");
                    Check(w != null && w.Contains("1.2.38-") && w.Contains("1.2.39"), "the hint keeps the start of the version and the recommended one");
                    Check(w != null && !w.Contains(new string('x', 40)), "the tail of the version is cut");
                    return true;
                }
                default:
                    return RunBm21Ctl(scenario);
            }
        }
    }
}
