// client rules (channels=N, epoch, skipped poll / adaptive period, 404, calibration under N)
// on recorded board responses (fixtures/responses, copied next to the exe by rest_suite.py).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Globalization;
using BecquerelMonitor;

namespace RestHarness
{
    static partial class Program
    {
        static string F5Path(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "responses", name);
        }

        static string F5Text(string name)
        {
            return File.ReadAllText(F5Path(name));
        }

        static double Poly(double[] c, double x)
        {
            double sum = 0.0;
            double pow = 1.0;
            for (int i = 0; i < c.Length; i++)
            {
                if (!double.IsNaN(c[i]))
                {
                    sum += c[i] * pow;
                }
                pow *= x;
            }
            return sum;
        }

        static double[] F5Calib(string name)
        {
            object root = AtomSpectraJson.Parse(F5Text(name));
            var dict = (Dictionary<string, object>)root;
            var arr = AtomSpectraJson.GetArray(dict, "calib");
            var res = new double[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                res[i] = (double)arr[i];
            }
            return res;
        }

        static bool F5Query(Dictionary<string, object> r, string want)
        {
            if (r == null || !r.ContainsKey("query")) return false;
            var qObj = r["query"];
            if (!(qObj is Dictionary<string, object>)) return false;
            var q = (Dictionary<string, object>)qObj;
            if (!q.ContainsKey("channels")) return false;
            var chObj = q["channels"];
            if (!(chObj is List<object>)) return false;
            var chList = (List<object>)chObj;
            if (chList.Count == 0) return false;
            return chList[0].ToString() == want;
        }

        static bool F5Logged(string text)
        {
            var lines = AtomSpectraTcpLog.Snapshot();
            foreach (var line in lines)
            {
                if (line.ToString().Contains(text))
                {
                    return true;
                }
            }
            return false;
        }

        static bool RunF5(string scenario)
        {
            if (scenario == null || !scenario.StartsWith("f5-"))
            {
                return false;
            }

            try
            {
                if (scenario == "f5-channels")
                {
                    var a = new AtomSpectraRestAcquisition(Client(), 1, 4096);
                    Check(a.SpectrumPath == "/api/spectrum.json?channels=4096", "request carries channels=4096");
                    Check(AtomSpectraRestSpectrum.RequestChannels(2048) == 2048 && AtomSpectraRestSpectrum.RequestChannels(512) == 0 && AtomSpectraRestSpectrum.RequestChannels(0) == 0, "only 8192/4096/2048/1024 are requested");
                    Check(new AtomSpectraRestAcquisition(Client(), 1, 512).SpectrumPath == "/api/spectrum.json", "a profile of 512 sends no parameter");

                    bool p1 = a.PollOnce();
                    Check(p1 && a.Last != null && a.Last.Histogram.Length == 4096 && a.Last.Channels == 4096 && a.Last.Histogram.Sum(x => (long)x) == 13442130, "c08: channels=4096 with 4096 bins taken without folding");

                    bool p2 = a.PollOnce();
                    Check(p2 && a.Last.Histogram.Length == 8192 && a.Last.Channels == -1, "c00: old firmware, 8192 bins without channels taken (the client folds)");

                    bool p3 = a.PollOnce();
                    Check(!p3 && a.LastError != null && a.LastError.Contains("requested"), "c01: channels=8192 for a request of 4096 is a parse error (skipped poll)");
                    Console.WriteLine(a.LastError);

                    var reqs = Requests().Where(r => Str(r, "path") == "/api/spectrum.json").ToList();
                    Check(reqs.Count == 3 && reqs.All(r => F5Query(r, "4096")), "every poll asked channels=4096");

                    a.Dispose();
                }
                else if (scenario == "f5-epoch-boot" || scenario == "f5-epoch-dev" || scenario == "f5-epoch-spec")
                {
                    string field = scenario == "f5-epoch-boot" ? "boot_id" : (scenario == "f5-epoch-dev" ? "dev_resets" : "spec_resets");
                    var c = new Counter();
                    var s = new AtomSpectraRestSession(Client(), 1, true, 0);
                    s.RequestChannels = 8192;
                    Hook(s, c);

                    Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                    // a changed epoch STOPS polling without overwriting the document;
                    // this test now owns only the DETECTION of the field (what the stop itself does is f5-reset-board)
                    Check(WaitFor(() => s.Guard.EpochChanges == 1, 12000), "the epoch change is seen");
                    Check(c.FirstTotal == 13442130, "the first snapshot was c01");
                    Check(s.Guard.EpochChanges == 1 && s.Guard.LastEpochChange != null && s.Guard.LastEpochChange.StartsWith(field), "the change is named by its field");
                    Console.WriteLine(s.Guard.LastEpochChange);

                    s.Detach();
                    s.Dispose();
                }
                else if (scenario == "f5-epoch-trip")
                {
                    var c = new Counter();
                    var s = new AtomSpectraRestSession(Client(), 1, true, 0);
                    s.RequestChannels = 8192;
                    Hook(s, c);

                    Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                    Check(WaitFor(() => c.Reset != null, 10000), "c05: counters down at the same epoch = Trip");
                    Check(s.Guard.EpochChanges == 0, "no epoch change was seen");
                    Check(c.MinTotal == 13442130, "the smaller snapshot never reached the document");

                    s.Detach();
                    s.Dispose();
                }
                else if (scenario == "f5-skip")
                {
                    AtomSpectraRestAcquisition.MaxAttempts = 1;   // this test feeds ONE case per poll; the repeat inside the period is f5-retry-*
                    var a = new AtomSpectraRestAcquisition(Client(), 2, 8192);
                    var g = new AtomSpectraRestGuard();
                    g.Begin(0, 0, true);
                    int lost = 0;
                    a.Sampled += (o, e) => g.Check(e.Sample);
                    a.LinkLost += (o, e) => lost++;

                    bool p1 = a.PollOnce();
                    Check(p1 && a.EffectivePeriodMs == 2000, "c01 taken, period 2 s");

                    bool p2 = a.PollOnce();
                    Check(!p2 && a.EffectivePeriodMs == 4000 && a.DeadlineMs == 2000, "c06 truncated: skipped poll, period x2 = 4 s");

                    bool p3 = a.PollOnce();
                    Check(!p3 && a.EffectivePeriodMs == 8000 && a.DeadlineMs == 4000, "c07 broken: skipped poll, period x2 = 8 s, deadline grows with it");

                    bool p4 = a.PollOnce();
                    Check(p4 && a.EffectivePeriodMs == 8000, "one full poll does not halve yet");

                    Check(!g.Tripped && lost == 0 && !a.IsLinkLost, "skipped polls: no Trip, no link loss, the session stays");
                    Check(F5Logged("poll period 2 s -> 4 s") && F5Logged("poll period 4 s -> 8 s"), "every period change is logged");

                    a.Dispose();
                }
                else if (scenario == "f5-recover")
                {
                    var names = new[] { "c09_seq_normal/seq_1.json", "c09_seq_normal/seq_2.json", "c09_seq_normal/seq_3.json", "c09_seq_normal/seq_4.json", "c09_seq_normal/seq_5.json" };
                    var p = new AtomSpectraRestPeriod(2000);
                    p.Skip();
                    p.Skip();
                    Check(p.EffectiveMs == 8000, "two skipped polls: 8 s");

                    int afterFour = 0;
                    int FeedSeq()
                    {
                        for (int i = 0; i < names.Length; i++)
                        {
                            string err;
                            var args = AtomSpectraRestSpectrum.Parse(F5Text(names[i]), 0, out err);
                            Check(args != null, "c09 " + names[i] + " parses");
                            p.Full();
                            if (i == 3)
                            {
                                afterFour = p.EffectiveMs;
                            }
                        }
                        return p.EffectiveMs;
                    }

                    int r1 = FeedSeq();
                    Check(afterFour == 8000 && r1 == 4000, "c09: 4 full polls keep 8 s, the 5th halves to 4 s");

                    int r2 = FeedSeq();
                    Check(r2 == 2000, "5 more: 2 s (the configured period)");

                    int r3 = FeedSeq();
                    Check(r3 == 2000, "never below the configured period");

                    var q = new AtomSpectraRestPeriod(1000);
                    q.Skip();
                    for (int i = 0; i < 5; i++) q.Full();
                    Check(q.EffectiveMs == 2000, "configured 1 s: after a skip not below 2 s");

                    var w = new AtomSpectraRestPeriod(2000);
                    for (int i = 0; i < 10; i++) w.Skip();
                    Check(w.EffectiveMs == 30000, "ceiling 30 s");
                }
                else if (scenario == "f5-nodata")
                {
                    var c = new Counter();
                    var s = new AtomSpectraRestSession(Client(), 1, true, 0);
                    s.RequestChannels = 8192;
                    Hook(s, c);

                    Check(s.Begin(true, 1000) == AtomSpectraTcpBeginOutcome.Started, "Begin returns Started");
                    var a = s.Acquisition;
                    Check(WaitFor(() => a.NoDataAnswers >= 3, 10000), "three 404 answers");

                    int before = c.DataReady;
                    Check(WaitFor(() => c.DataReady > before, 5000), "snapshots continue after the 404");

                    Check(c.Reset == null && s.Acquisition != null, "404 at a known epoch: no Trip, the session stays");
                    Check(a.Failures == 0, "404 is not a link failure");
                    Check(a.EffectivePeriodMs == 1000, "404 does not lengthen the period");

                    s.Detach();
                    s.Dispose();
                }
                else if (scenario == "f5-calib")
                {
                    double[] slots = F5Calib("c08_channels_4096.json");
                    object root = AtomSpectraJson.Parse(F5Text("c08_channels_4096.json"));
                    var dict = (Dictionary<string, object>)root;
                    int calibCh = (int)AtomSpectraJson.GetNumber(dict, "calib_channels", 0);
                    int n = AtomSpectraJson.GetArray(dict, "bins").Count;
                    Check(calibCh == 8192 && n == 4096 && slots.Length == 5, "c08: calib for 8192, 4096 bins");

                    Check(AtomSpectraRestCalibration.GroupCentre(1) == 0.0 && AtomSpectraRestCalibration.GroupCentre(2) == 0.5 && AtomSpectraRestCalibration.GroupCentre(8) == 3.5, "group centre s = (m-1)/2");

                    int[] ms = { 2, 8 };
                    foreach (int m in ms)
                    {
                        double[] b = AtomSpectraRestCalibration.ForChannels(slots, calibCh, 8192 / m);
                        Check(b != null && b.Length == 5, "m=" + m + ": ForChannels returns 5 coefficients");
                        int[] ks = { 0, 1, 100, 1000, 8192 / m - 1 };
                        foreach (int k in ks)
                        {
                            double want = Poly(slots, m * k + (m - 1) / 2.0);
                            double got = Poly(b, k);
                            Check(Math.Abs(got - want) <= 1e-9 * Math.Max(1.0, Math.Abs(want)), "m=" + m + " k'=" + k + ": E'(k') == E(m*k' + s) (" + got.ToString("R", CultureInfo.InvariantCulture) + " vs " + want.ToString("R", CultureInfo.InvariantCulture) + ")");
                        }
                    }

                    double[] same = AtomSpectraRestCalibration.ForChannels(slots, 8192, 8192);
                    Check(same != null && !ReferenceEquals(same, slots) && same.SequenceEqual(slots), "m = 1: coefficients unchanged");

                    Check(AtomSpectraRestCalibration.ForChannels(slots, 0, 4096).SequenceEqual(AtomSpectraRestCalibration.ForChannels(slots, 8192, 4096)), "no calib_channels = 8192");

                    double[] g = AtomSpectraRestCalibration.ForChannels(new[] { 3.5, 0.41, double.NaN, double.NaN, double.NaN }, 8192, 4096);
                    Check(Math.Abs(g[0] - (3.5 + 0.41 * 0.5)) < 1e-12 && Math.Abs(g[1] - 0.82) < 1e-12 && double.IsNaN(g[2]) && double.IsNaN(g[4]), "empty slots stay empty");
                }
                else if (scenario == "f5-centroid")
                {
                    object root = AtomSpectraJson.Parse(F5Text("c09_seq_normal/seq_1.json"));
                    var dict = (Dictionary<string, object>)root;
                    var binsArr = AtomSpectraJson.GetArray(dict, "bins");
                    int[] full = new int[binsArr.Count];
                    for (int i = 0; i < binsArr.Count; i++)
                    {
                        full[i] = (int)(double)binsArr[i];
                    }
                    double[] slots = F5Calib("c09_seq_normal/seq_1.json");

                    int peak = 576;
                    int maxVal = full[576];
                    for (int i = 577; i <= 703; i++)
                    {
                        if (full[i] > maxVal)
                        {
                            maxVal = full[i];
                            peak = i;
                        }
                    }

                    int lo = ((peak - 24) / 8) * 8;
                    int hi = lo + 48;

                    double sumX = 0.0;
                    double sumF = 0.0;
                    for (int x = lo; x < hi; x++)
                    {
                        sumX += x * full[x];
                        sumF += full[x];
                    }
                    double xf = sumX / sumF;

                    int[] Ns = { 4096, 1024 };
                    foreach (int N in Ns)
                    {
                        int m = 8192 / N;
                        int[] folded = AtomSpectraTcpAcquisition.Compress(full, N);
                        int loK = lo / m;
                        int hiK = hi / m;
                        double sumK = 0.0;
                        double sumFolded = 0.0;
                        for (int k = loK; k < hiK; k++)
                        {
                            sumK += k * folded[k];
                            sumFolded += folded[k];
                        }
                        double kc = sumK / sumFolded;
                        double xc = m * kc + AtomSpectraRestCalibration.GroupCentre(m);
                        double dx = Math.Abs(xf - xc);

                        double ef = Poly(slots, xf);
                        double ec = Poly(AtomSpectraRestCalibration.ForChannels(slots, 8192, N), kc);
                        double halfE = Math.Abs(Poly(slots, xf + m / 2.0) - Poly(slots, xf - m / 2.0)) / 2;

                        Console.WriteLine("centroid N=" + N + " m=" + m + " peak=" + peak + " window=[" + lo + "," + hi + ") x8192=" + xf.ToString("F4", CultureInfo.InvariantCulture) + " x(N)=" + kc.ToString("F4", CultureInfo.InvariantCulture) + " -> " + xc.ToString("F4", CultureInfo.InvariantCulture) + " dx=" + dx.ToString("F4", CultureInfo.InvariantCulture) + " ch (limit " + (m / 2.0).ToString(CultureInfo.InvariantCulture) + ") E8192=" + ef.ToString("F4", CultureInfo.InvariantCulture) + " keV E(N)=" + ec.ToString("F4", CultureInfo.InvariantCulture) + " keV dE=" + Math.Abs(ef - ec).ToString("F4", CultureInfo.InvariantCulture) + " keV (half channel " + halfE.ToString("F4", CultureInfo.InvariantCulture) + " keV)");

                        Check(dx <= m / 2.0, "N=" + N + ": centroid shift within half a folded channel");
                    }
                }
                else
                {
                    Check(false, "unknown f5 scenario");
                }
            }
            catch (Exception ex)
            {
                Check(false, "exception " + ex);
            }

            return true;
        }
    }
}
