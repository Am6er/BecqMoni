// a poll that missed its deadline, link loss by time without an answer, and the
// calibration rescale called from the controller (real build only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static bool RunF5b(string scenario)
        {
            try
            {
                if (scenario == "f5-timeout")
                {
                    var a = new AtomSpectraRestAcquisition(Client(), 2, 8192);
                    var g = new AtomSpectraRestGuard();
                    g.Begin(0, 0, true);
                    int lost = 0;
                    a.Sampled += (o, e) => g.Check(e.Sample);
                    a.LinkLost += (o, e) => lost++;

                    bool p1 = a.PollOnce();
                    Check(p1 && a.EffectivePeriodMs == 2000, "first poll taken, period 2 s");

                    Thread.Sleep(1300);
                    var w = Stopwatch.StartNew();
                    bool p2 = a.PollOnce();
                    long took = w.ElapsedMilliseconds;
                    Console.WriteLine("timed out poll took " + took + " ms: " + a.LastError);

                    Check(!p2 && took >= 1900 && took < 4000, "the hanging poll is cut at its deadline (2 s)");
                    Check(a.EffectivePeriodMs == 4000 && a.Failures == 1, "a poll past its deadline is a skipped poll: period x2 = 4 s");
                    Check(!g.Tripped && lost == 0 && !a.IsLinkLost, "a skipped poll by timeout: no Trip, no link loss");

                    Thread.Sleep(2500);
                    bool p3 = a.PollOnce();
                    Check(p3 && !g.Tripped && a.EffectivePeriodMs == 4000, "after the hang the next poll is taken, the session goes on");

                    a.Dispose();
                }
                else if (scenario == "f5-linklost-time")
                {
                    AtomSpectraRestAcquisition.LinkLostFloorMs = 3000;   // keeps this test on the 3-period part of max(3 periods, floor); the 15 s floor is f5-linklost-silence
                    var a = new AtomSpectraRestAcquisition(Client(), 1, 8192);
                    int lost = 0;
                    a.LinkLost += (o, e) => lost++;

                    bool p1 = a.PollOnce();
                    Check(p1, "first poll taken");

                    Thread.Sleep(1300);
                    a.PollOnce();
                    a.PollOnce();

                    Console.WriteLine("failures " + a.Failures + ", period " + a.EffectivePeriodMs + " ms, lost " + lost);

                    Check(a.Failures == 2, "two skipped polls (the period, printed above, is checked by f5-skip/f5-timeout)");
                    Check(lost == 1 && a.IsLinkLost, "link lost after 3 configured periods without an answer, before the 3rd failure");

                    a.Dispose();
                }
                else if (scenario == "f5-ctl-calib")
                {
                    Type ctlT = Type.GetType("BecquerelMonitor.AtomSpectraTcpDeviceController, BecquerelMonitor");
                    Type rdT = Type.GetType("BecquerelMonitor.ResultData, BecquerelMonitor");
                    Type specT = Type.GetType("BecquerelMonitor.EnergySpectrum, BecquerelMonitor");

                    if (ctlT == null || rdT == null || specT == null)
                    {
                        Console.WriteLine("n/a: the controller is not in this build");
                        return true;
                    }

                    object ctl = Activator.CreateInstance(ctlT);
                    object rd = Activator.CreateInstance(rdT);

                    object spec;
                    try
                    {
                        spec = Activator.CreateInstance(specT, new object[] { 1.0, 4096 });
                    }
                    catch (MissingMethodException)
                    {
                        spec = Activator.CreateInstance(specT, new object[] { 1, 4096 });
                    }

                    rdT.GetProperty("EnergySpectrum").SetValue(rd, spec);
                    rdT.GetProperty("DeviceConfig").SetValue(rd, null);   // no configuration: only the document gets the calibration, nothing is saved
                    object status = rdT.GetProperty("ResultDataStatus").GetValue(rd);
                    status.GetType().GetProperty("Recording").SetValue(status, true);

                    var nf = BindingFlags.NonPublic | BindingFlags.Instance;
                    ctlT.GetField("resultData", nf).SetValue(ctl, rd);
                    ctlT.GetField("boardCalibrationPending", nf).SetValue(ctl, true);

                    double[] slots = F5Calib("c08_channels_4096.json");
                    string text = F5Text("c08_channels_4096.json");
                    var dict = (Dictionary<string, object>)AtomSpectraJson.Parse(text);
                    var bins = AtomSpectraJson.GetArray(dict, "bins");
                    int[] hist = new int[bins.Count];
                    for (int i = 0; i < bins.Count; i++)
                        hist[i] = (int)(double)bins[i];
                    long sum = hist.Sum(x => (long)x);

                    var d = new AtomSpectraRestDataReadyArgs(hist, 0, 15, 0, 0, double.NaN, sum, false, 0, (double[])slots.Clone());
                    d.CalibChannels = 8192;

                    MethodInfo upd = ctlT.GetMethod("UpdateHistogram", nf);
                    Check(upd != null, "the controller has UpdateHistogram");
                    if (upd == null) return true;
                    upd.Invoke(ctl, new object[] { d });

                    object cal = specT.GetProperty("EnergyCalibration").GetValue(spec);
                    double[] got = cal == null ? null : (double[])cal.GetType().GetProperty("Coefficients").GetValue(cal);
                    double[] want = AtomSpectraRestCalibration.ForChannels(slots, 8192, 4096);

                    Console.WriteLine("document calibration: " + (got == null ? "null" : string.Join("; ", got.Select(x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));

                    Check(got != null && want != null && got.SequenceEqual(want), "the controller gives the document the calibration rescaled to 4096 channels");
                    Check(got != null && !got.SequenceEqual(slots), "not the 8192-channel coefficients of the board");
                }
                else
                {
                    return RunF5c(scenario);   // RestHarnessF5c.cs
                }

                return true;
            }
            catch (Exception ex)
            {
                Check(false, "exception " + ex);
                return true;
            }
        }
    }
}
