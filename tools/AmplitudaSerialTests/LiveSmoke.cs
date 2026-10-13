using System;
using System.Globalization;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    // Real hardware: CLEARS and STARTS the block. Run only when the user said so.
    //   --live <port> <address> <seconds> <pourThreshold or 0> [gapMs]
    public static class LiveSmoke
    {
        public static int Run(string[] args)
        {
            string port = args.Length > 1 ? args[1] : "COM2";
            byte address = (byte)(args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 0);
            int seconds = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 60;
            int pour = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 0;
            int gap = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 20;

            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = port, GapMs = gap }, out error);
            if (line == null) { Console.WriteLine("cannot open " + port + ": " + error); return 2; }

            AmplitudaSerialAcquisition acq = new AmplitudaSerialAcquisition(line, address, 1024, 3000, new int[1024], 0, 0);
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
                    int[] spectrum = new int[1024];
                    acq.Compose(spectrum);
                    int max = 0, at = 0;
                    for (int i = 0; i < spectrum.Length; i++) if (spectrum[i] > max) { max = spectrum[i]; at = i; }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "real {0,7:F1} s  live {1,7:F1} s  counts {2,9}  max {3}@{4}  running {5}",
                        acq.RealSeconds, acq.LiveSeconds, acq.TotalCounts, max, at, acq.Running));
                }
                if (!stopping && acq.RealSeconds >= seconds) { stopping = true; stopClock = System.Diagnostics.Stopwatch.StartNew(); acq.RequestStop(); }
                Thread.Sleep(20);
            }
            if (stopClock != null)
            {
                Console.WriteLine("stop took " + stopClock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms (gap " +
                    gap.ToString(CultureInfo.InvariantCulture) + " ms, budget " + line.StopBudgetMs(1024).ToString(CultureInfo.InvariantCulture) + " ms)");
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
