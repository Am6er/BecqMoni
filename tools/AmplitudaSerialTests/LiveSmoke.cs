using System;
using System.Globalization;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    // Real hardware: CLEARS and STARTS the block. Run only when the user said so.
    //   --live <port> <address> <seconds> <pourThreshold or 0> [gapMs, 20 by default] [channels, 1024 by default]
    // Every 5 s one line: the real and live time of the data in hand, their dead fraction and
    // the count rate (P280: one moment for all of them - the dead % must stay level from line to
    // line, within 50/T points, instead of the saw-tooth of a clock that runs ahead of the data),
    // and the PC clock. After the Stop: how long it took against the line's Stop budget.
    public static class LiveSmoke
    {
        public static int Run(string[] args)
        {
            string port = args.Length > 1 ? args[1] : "COM2";
            byte address = (byte)(args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 0);
            int seconds = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 60;
            int pour = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 0;
            int gap = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 20;
            int channels = args.Length > 6 ? int.Parse(args[6], CultureInfo.InvariantCulture) : 1024;

            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = port, GapMs = gap }, out error);
            if (line == null) { Console.WriteLine("cannot open " + port + ": " + error); return 2; }

            AmplitudaSerialAcquisition acq = new AmplitudaSerialAcquisition(line, address, channels, 3000, new int[channels], 0, 0);
            if (pour > 0) acq.Session.PourChannelThreshold = pour;
            acq.Start();
            DateTime next = DateTime.UtcNow;
            bool stopping = false;
            System.Diagnostics.Stopwatch stopClock = null;
            while (!acq.Finished)
            {
                acq.Pump();
                foreach (AmplitudaSerialWarning warning in acq.TakeWarnings()) Console.WriteLine("  WARNING: " + warning);
                if (DateTime.UtcNow >= next)
                {
                    next = DateTime.UtcNow.AddSeconds(5);
                    int[] spectrum = new int[channels];
                    acq.Compose(spectrum);
                    int max = 0, at = 0;
                    for (int i = 0; i < spectrum.Length; i++) if (spectrum[i] > max) { max = spectrum[i]; at = i; }
                    double real = acq.RealSeconds;
                    double live = acq.LiveSeconds;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "real {0,7:F2} s  live {1,7:F2} s  dead {2,6:F2} %  rate {3,8:F1} cps  counts {4,9}  max {5}@{6}  clock {7,7:F2} s  running {8}",
                        real, live, real > 0 ? 100.0 * (real - live) / real : 0.0, real > 0 ? acq.TotalCounts / real : 0.0,
                        acq.TotalCounts, max, at, TimeView.Clock(acq), acq.Running));
                }
                // By the clock, as the preset stop of the application (ElapsedTicks).
                if (!stopping && TimeView.Clock(acq) >= seconds) { stopping = true; stopClock = System.Diagnostics.Stopwatch.StartNew(); acq.RequestStop(); }
                Thread.Sleep(20);
            }
            if (stopClock != null)
            {
                Console.WriteLine("stop took " + stopClock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms (gap " +
                    gap.ToString(CultureInfo.InvariantCulture) + " ms, budget " + line.StopBudgetMs(channels).ToString(CultureInfo.InvariantCulture) + " ms)");
            }
            line.Release();
            Console.WriteLine("finished: failure = " + acq.Failure + ", real " + acq.RealSeconds.ToString("F1", CultureInfo.InvariantCulture) +
                " s, live " + acq.LiveSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s, counts " + acq.TotalCounts.ToString(CultureInfo.InvariantCulture) +
                ", rate " + (acq.RealSeconds > 0 ? acq.TotalCounts / acq.RealSeconds : 0).ToString("F1", CultureInfo.InvariantCulture) + " cps" +
                (acq.SuggestedChannels != 0 ? ", suggested channels " + acq.SuggestedChannels.ToString(CultureInfo.InvariantCulture) : ""));
            return acq.Failure == AmplitudaSerialFailure.None ? 0 : 1;
        }
    }
}
