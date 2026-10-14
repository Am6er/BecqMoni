using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    // The two time views of an acquisition, through reflection, so that the tests compile
    // against the code before P280 too (that build is the positive control of MomentTests).
    public static class TimeView
    {
        // The PC clock: AmplitudaSerialAcquisition.RealSecondsNow since P280; before it, RealSeconds was the clock.
        public static double Clock(AmplitudaSerialAcquisition acq)
        {
            PropertyInfo clock = typeof(AmplitudaSerialAcquisition).GetProperty("RealSecondsNow");
            return clock != null ? (double)clock.GetValue(acq, null) : acq.RealSeconds;
        }

        // What the device controller writes as the elapsed time (ticks): ElapsedTicks(preset)
        // since P280; before it, the ticks of RealSeconds.
        public static long ElapsedTicks(AmplitudaSerialAcquisition acq, int presetSeconds)
        {
            MethodInfo elapsed = typeof(AmplitudaSerialAcquisition).GetMethod("ElapsedTicks");
            return elapsed != null ? (long)elapsed.Invoke(acq, new object[] { presetSeconds }) : (long)(acq.RealSeconds * 1.0E+07);
        }

        public static string F(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    // P280 (14.10.2026), decision of Amber "Поправка в коде, проверка автору (Рекомендую)": while
    // measuring, the counts, the live time and the real time the application sees belong to ONE
    // moment.
    //
    // The check needs no moment at all. The fake block counts `Rate` per second of real time and
    // runs its live clock at LiveFraction of it (dead fraction d = 1 - LiveFraction). So next to
    // N counts the truth is the real time R_N = N / Rate and the live time L_N = LiveFraction * R_N,
    // whenever the counts were read. The application shows (N, L, R); R - R_N and L - L_N are the
    // errors, in seconds. Divided by T they are the errors of the dead fraction (1 - L/R against
    // d) and of the rates (N/R against Rate, N/L against Rate/LiveFraction) - the numbers of both
    // panels and of the activity while measuring.
    //
    // Bounds after P280: real time exact (to the fake's stamps; 0.15 s for the test thread's
    // 100-ms steps that may fall between a byte and its stamp); live time within the block's
    // whole second, i.e. 0.5 s * R / T_h, T_h the real time at the header - the protocol has no
    // live time nearer the counts (the header is read before the pages). T_h >= R - t_read.
    // Before P280 (the same test built against HEAD 4cb5ce9e) both bounds are broken by the
    // amounts of the P250 journal formula; the run prints them.
    public static class MomentTests
    {
        static int portCounter;
        const double LiveFraction = 0.7;

        sealed class Outcome
        {
            public int Samples;
            public double ReadSeconds;
            public double RealErrMin = double.MaxValue, RealErrMax = double.MinValue;
            public double LiveErrMin = double.MaxValue, LiveErrMax = double.MinValue;
            public double LiveExcess = double.MinValue;     // max of |L - L_N| - allowance
            public double DeadPointsMin = double.MaxValue, DeadPointsMax = double.MinValue;     // at T in [50, 70] s
            public double RateLiveMin = double.MaxValue, RateLiveMax = double.MinValue;         // N/L over the truth - 1, %
            public double RateRealMin = double.MaxValue, RateRealMax = double.MinValue;         // N/R over the truth - 1, %
            public double DeadSecondsMin = double.MaxValue, DeadSecondsMax = double.MinValue;   // (D - d) * R over all samples
        }

        // Virtual seconds of the last complete Read job of the block on the wire: from its first
        // status command to the checksum command of its last page.
        static double ReadJobSeconds(FakeBlockPort fake, int pages)
        {
            string[] wire = fake.WireSnapshot();
            long[] ms = fake.WireMsSnapshot();
            string lastPage = "C" + (AmplitudaSerialProtocol.CommandFirstPage + pages - 1).ToString("X2", CultureInfo.InvariantCulture);
            for (int i = Math.Min(wire.Length, ms.Length) - 1; i >= 0; i--)
            {
                if (wire[i] != lastPage || i + 2 >= ms.Length || wire[i + 2] != "C10")
                {
                    continue;
                }
                int header = Array.LastIndexOf(wire, "C81", i);
                if (header < 0)
                {
                    return 0;
                }
                int first = header;
                // The read's own confirmed status: two exchanges (the fake never makes it retry) -
                // not further back, where the statuses of the start sequence may sit right before it.
                for (int k = 0; k < 2 && first - 2 >= 0 && wire[first - 2] == "C13"; k++)
                {
                    first -= 2;
                }
                return (ms[i + 2] - ms[first]) / 1000.0;
            }
            return 0;
        }

        static Outcome Run(string name, int channels, int pollMs, int gapMs, bool spread, int hotChannel, double rate,
            out bool stoppedClean)
        {
            FakeBlockPort fake = new FakeBlockPort();
            fake.GapMs = gapMs;
            FakeBlock block = new FakeBlock();
            block.Channels = new int[channels];
            block.LiveFraction = LiveFraction;
            block.Rate = rate;
            block.Spread = spread;
            block.HotChannel = hotChannel;
            fake.Blocks[0] = block;
            AmplitudaSerialLine.PortFactory = settings => fake;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = "MOM" + (++portCounter).ToString(CultureInfo.InvariantCulture), GapMs = gapMs }, out error);
            AmplitudaSerialAcquisition acq = new AmplitudaSerialAcquisition(line, 0, channels, pollMs, new int[channels], 0, 0);
            int pages = AmplitudaSerialProtocol.PageCount(channels);
            Outcome outcome = new Outcome();
            acq.Start();
            DateTime limit = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < limit && TimeView.Clock(acq) < 125.0)
            {
                acq.Pump();
                double r = acq.RealSeconds;
                double l = acq.LiveSeconds;
                long n = acq.TotalCounts;
                if (outcome.ReadSeconds == 0)
                {
                    outcome.ReadSeconds = ReadJobSeconds(fake, pages);
                }
                if (acq.Running && r >= 20.0 && n > 0 && outcome.ReadSeconds > 0)
                {
                    double realN = n / rate;
                    double liveN = LiveFraction * realN;
                    double realErr = r - realN;
                    double liveErr = l - liveN;
                    double allowance = 0.5 * r / (r - outcome.ReadSeconds) + 0.1;
                    outcome.Samples++;
                    outcome.RealErrMin = Math.Min(outcome.RealErrMin, realErr);
                    outcome.RealErrMax = Math.Max(outcome.RealErrMax, realErr);
                    outcome.LiveErrMin = Math.Min(outcome.LiveErrMin, liveErr);
                    outcome.LiveErrMax = Math.Max(outcome.LiveErrMax, liveErr);
                    outcome.LiveExcess = Math.Max(outcome.LiveExcess, Math.Abs(liveErr) - allowance);
                    double deadSeconds = ((1.0 - l / r) - (1.0 - LiveFraction)) * r;
                    outcome.DeadSecondsMin = Math.Min(outcome.DeadSecondsMin, deadSeconds);
                    outcome.DeadSecondsMax = Math.Max(outcome.DeadSecondsMax, deadSeconds);
                    if (r >= 50.0 && r <= 70.0)
                    {
                        double deadPoints = 100.0 * ((1.0 - l / r) - (1.0 - LiveFraction));
                        double rateLive = 100.0 * ((n / l) / (rate / LiveFraction) - 1.0);
                        double rateReal = 100.0 * ((n / r) / rate - 1.0);
                        outcome.DeadPointsMin = Math.Min(outcome.DeadPointsMin, deadPoints);
                        outcome.DeadPointsMax = Math.Max(outcome.DeadPointsMax, deadPoints);
                        outcome.RateLiveMin = Math.Min(outcome.RateLiveMin, rateLive);
                        outcome.RateLiveMax = Math.Max(outcome.RateLiveMax, rateLive);
                        outcome.RateRealMin = Math.Min(outcome.RateRealMin, rateReal);
                        outcome.RateRealMax = Math.Max(outcome.RateRealMax, rateReal);
                    }
                }
                fake.Advance(100);
                Thread.Sleep(1);
            }

            // After the Stop: the numbers of the arithmetic that has always made them (P280 does not
            // touch it) - bit for bit. One segment, no pour: live = 0.0 + (block seconds + 0.5),
            // capped at real; real = the committed time, the same as the clock now; counts = the block.
            acq.RequestStop();
            DateTime stopLimit = DateTime.UtcNow.AddSeconds(30);
            while (!acq.Finished && DateTime.UtcNow < stopLimit)
            {
                acq.Pump();
                fake.Advance(100);
                Thread.Sleep(1);
            }
            line.Release();
            int blockSeconds = (int)(block.LiveMs / 1000.0);
            double expectedLive = 0.0;
            expectedLive += blockSeconds + (blockSeconds >= 1 ? AmplitudaSerialSession.SegmentClockCorrectionSeconds : 0.0);
            long blockSum = 0;
            foreach (int value in block.Channels) blockSum += value;
            stoppedClean = acq.Finished && acq.Failure == AmplitudaSerialFailure.None && !block.Running;
            T.True(stoppedClean, name + ": stopped cleanly (" + acq.Failure + ")");
            T.True(acq.RealSeconds == TimeView.Clock(acq), name + ": after Stop real time = the committed clock, bit for bit");
            T.True(acq.LiveSeconds == Math.Min(expectedLive, acq.RealSeconds), name + ": after Stop live = block seconds + 0.5, bit for bit ("
                + TimeView.F(acq.LiveSeconds, "R") + " vs " + TimeView.F(Math.Min(expectedLive, acq.RealSeconds), "R") + ")");
            T.Eq(blockSum, acq.TotalCounts, name + ": after Stop counts = the block");
            double finalDead = 1.0 - acq.LiveSeconds / acq.RealSeconds;
            Console.WriteLine("  " + name + " after Stop: real " + TimeView.F(acq.RealSeconds, "F3") + " s, live " + TimeView.F(acq.LiveSeconds, "F3")
                + " s, dead " + TimeView.F(100.0 * finalDead, "F3") + " %, rate " + TimeView.F(acq.TotalCounts / acq.RealSeconds, "F2") + " cps");
            return outcome;
        }

        static void Check(string name, int channels, int pollMs, int gapMs, bool spread, int hotChannel, double rate)
        {
            bool stoppedClean;
            Outcome o = Run(name, channels, pollMs, gapMs, spread, hotChannel, rate, out stoppedClean);
            double f = LiveFraction;
            double d = 1.0 - f;
            double delta = pollMs / 1000.0;
            // The P250 journal: the dead fraction reads high by (1 - d)(t_read - 0.5)/T ... (1 - d)(Δ + 2 t_read + 0.5)/T.
            double formulaLow = f * (o.ReadSeconds - 0.5);
            double formulaHigh = f * (delta + 2.0 * o.ReadSeconds + 0.5);
            Console.WriteLine("  " + name + ": pages " + AmplitudaSerialProtocol.PageCount(channels).ToString(CultureInfo.InvariantCulture)
                + ", d " + TimeView.F(d, "F2") + ", Δ " + TimeView.F(delta, "F1") + " s, t_read " + TimeView.F(o.ReadSeconds, "F2") + " s, samples "
                + o.Samples.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("    real - R_N  " + TimeView.F(o.RealErrMin, "F3") + " … " + TimeView.F(o.RealErrMax, "F3")
                + " s;  live - L_N  " + TimeView.F(o.LiveErrMin, "F3") + " … " + TimeView.F(o.LiveErrMax, "F3") + " s");
            Console.WriteLine("    dead time too high by (D - d)·T  " + TimeView.F(o.DeadSecondsMin, "F2") + " … " + TimeView.F(o.DeadSecondsMax, "F2")
                + " s;  P250 formula " + TimeView.F(formulaLow, "F2") + " … " + TimeView.F(formulaHigh, "F2") + " s");
            Console.WriteLine("    at T 50…70 s: dead " + TimeView.F(o.DeadPointsMin, "F2") + " … " + TimeView.F(o.DeadPointsMax, "F2")
                + " points;  N/L " + TimeView.F(o.RateLiveMin, "F2") + " … " + TimeView.F(o.RateLiveMax, "F2")
                + " %;  N/R " + TimeView.F(o.RateRealMin, "F2") + " … " + TimeView.F(o.RateRealMax, "F2") + " %");
            T.True(o.Samples > 50, name + ": enough samples (" + o.Samples.ToString(CultureInfo.InvariantCulture) + ")");
            T.True(Math.Max(Math.Abs(o.RealErrMin), Math.Abs(o.RealErrMax)) <= 0.15,
                name + ": real time shown is the real time of the counts shown, within 0.15 s ("
                + TimeView.F(o.RealErrMin, "F3") + " … " + TimeView.F(o.RealErrMax, "F3") + " s)");
            T.True(o.LiveExcess <= 0.0,
                name + ": live time shown is the live time of the counts shown, within 0.5 s·T/T_h + 0.1 s ("
                + TimeView.F(o.LiveErrMin, "F3") + " … " + TimeView.F(o.LiveErrMax, "F3") + " s, worst excess " + TimeView.F(o.LiveExcess, "F3") + " s)");
        }

        // 1024 channels: two pages, a read of about 2 s; counts on every page.
        public static void TestOneMomentTwoPages()
        {
            Check("2 pages", 1024, 3000, 20, true, 0, 1000.0);
        }

        // 8192 channels: sixteen pages, a read of about 11 s - the counts lag the header most.
        public static void TestOneMomentSixteenPages()
        {
            Check("16 pages", 8192, 3000, 20, true, 0, 1000.0);
        }

        // All counts on the LAST of sixteen pages: the moment of the counts is that page's, not
        // the middle of the read (the weighting by pages). 200 cps keeps the channel below the pour.
        public static void TestOneMomentCountsOnTheLastPage()
        {
            Check("16 pages, last page", 8192, 3000, 20, false, 8100, 200.0);
        }

        // A slower line and a longer poll period: Δ 10 s, gap 100 ms (t_read about 3.3 s for 1024).
        public static void TestOneMomentSlowLine()
        {
            Check("2 pages, Δ 10 s, gap 100 ms", 1024, 10000, 100, true, 0, 1000.0);
        }

        // The preset stop must fire on the same tick as before: MeasurementController.OnTimer stops
        // once the elapsed time the controller writes reaches PresetTime. Emulated here: the
        // controller's write (ElapsedTicks) and the core's check, every 100 ms of virtual time.
        // Before P280 the elapsed time WAS the clock; since P280 it is the data's real time, except
        // once the clock has reached the preset. So at every check before the stop the clock is
        // below the preset, and at the stop it is at or above it - the rule of before.
        public static void TestPresetStopFollowsTheClock()
        {
            const int preset = 40;
            FakeBlockPort fake = new FakeBlockPort();
            FakeBlock block = new FakeBlock();
            block.Channels = new int[8192];
            block.Spread = true;
            fake.Blocks[0] = block;
            AmplitudaSerialLine.PortFactory = settings => fake;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = "PRE" + (++portCounter).ToString(CultureInfo.InvariantCulture) }, out error);
            AmplitudaSerialAcquisition acq = new AmplitudaSerialAcquisition(line, 0, 8192, 3000, new int[8192], 0, 0);
            acq.Start();
            double clockBefore = -1;
            double clockAtStop = -1;
            double dataAtStop = -1;
            bool elapsedWasClockAtStop = false;
            bool earlyClockOverPreset = false;
            double maxLagBefore = 0;
            DateTime limit = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < limit)
            {
                acq.Pump();
                long ticks = TimeView.ElapsedTicks(acq, preset);
                double clock = TimeView.Clock(acq);
                if (TimeSpan.FromTicks(ticks).TotalSeconds >= preset)
                {
                    clockAtStop = clock;
                    dataAtStop = acq.RealSeconds;
                    elapsedWasClockAtStop = ticks == (long)(clock * 1.0E+07);
                    break;
                }
                if (clock >= preset)
                {
                    earlyClockOverPreset = true;    // the clock is past the preset and no stop: late
                }
                maxLagBefore = Math.Max(maxLagBefore, clock - acq.RealSeconds);
                clockBefore = clock;
                fake.Advance(100);
                Thread.Sleep(1);
            }
            acq.RequestStop();
            T.True(T.Wait(() => { acq.Pump(); fake.Advance(100); return acq.Finished; }, 30000), "finished");
            line.Release();
            Console.WriteLine("  preset " + preset.ToString(CultureInfo.InvariantCulture) + " s: clock at the last check before "
                + TimeView.F(clockBefore, "F3") + " s, at the stop " + TimeView.F(clockAtStop, "F3") + " s; data real time then "
                + TimeView.F(dataAtStop, "F3") + " s (lag up to " + TimeView.F(maxLagBefore, "F2") + " s before); final real "
                + TimeView.F(acq.RealSeconds, "F3") + " s");
            T.True(clockAtStop >= preset, "the stop came at the clock past the preset: " + TimeView.F(clockAtStop, "F3"));
            T.True(!earlyClockOverPreset && clockBefore < preset, "no check with the clock past the preset went by without a stop");
            T.True(elapsedWasClockAtStop, "at the stop the elapsed time written is the clock");
            T.True(acq.RealSeconds >= preset, "final real time at or past the preset: " + TimeView.F(acq.RealSeconds, "F3"));
            T.True(acq.Failure == AmplitudaSerialFailure.None, "failure: " + acq.Failure);
        }
    }
}
