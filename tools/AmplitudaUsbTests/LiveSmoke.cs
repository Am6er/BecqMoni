using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaUsbTests
{
    // Reads the real device for N seconds. Read-only: the device has no output reports.
    public static class LiveSmoke
    {
        public static int Run(int seconds)
        {
            const int Vid = 0x534B, Pid = 0x0874;
            List<AmplitudaUsbDeviceInfo> found = AmplitudaUsbReader.FindDevices(Vid, Pid);
            Console.WriteLine("devices found: " + found.Count);
            foreach (AmplitudaUsbDeviceInfo d in found)
            {
                Console.WriteLine("  " + d + "  [" + d.Manufacturer + "]");
            }
            T.True(found.Count > 0, "at least one Amplituda USB device is connected");
            if (found.Count == 0) return 1;

            using (AmplitudaUsbReader reader = new AmplitudaUsbReader(Vid, Pid, "", 14.0))
            {
                int error;
                T.True(reader.Start(out error), "reader starts (error " + error + ")");
                if (reader.State != AmplitudaUsbReaderState.Running) return 1;
                Console.WriteLine("opened s/n " + reader.SerialNumber);

                Stopwatch wall = Stopwatch.StartNew();
                long frames = 0, events = 0, realMicros = 0, liveMicros = 0, malformed = 0;
                int min = int.MaxValue, max = 0;
                while (wall.Elapsed.TotalSeconds < seconds)
                {
                    Thread.Sleep(250);
                    AmplitudaUsbBatch b = reader.TakeBatch();
                    frames += b.Frames; events += b.Count; realMicros += b.RealMicros; liveMicros += b.LiveMicros; malformed += b.MalformedFrames;
                    for (int i = 0; i < b.Count; i++) { min = Math.Min(min, b.Codes[i]); max = Math.Max(max, b.Codes[i]); }
                }
                wall.Stop();
                T.True(reader.State == AmplitudaUsbReaderState.Running, "reader is still running at the end");

                Stopwatch stopping = Stopwatch.StartNew();
                reader.Stop();
                stopping.Stop();
                AmplitudaUsbBatch tail = reader.TakeBatch();
                frames += tail.Frames; events += tail.Count; realMicros += tail.RealMicros; liveMicros += tail.LiveMicros;

                double wallSeconds = wall.Elapsed.TotalSeconds;
                double lost = 1.0 - (realMicros / 1e6) / wallSeconds;
                Console.WriteLine(string.Format("wall {0:F2} s, device {1:F2} s, live {2:F2} s", wallSeconds, realMicros / 1e6, liveMicros / 1e6));
                Console.WriteLine(string.Format("{0:F1} frames/s, {1:F2} cps, codes {2}..{3}, malformed {4}, lost {5:P2}, stop took {6} ms",
                    frames / wallSeconds, events / (realMicros / 1e6), events > 0 ? min : 0, max, malformed, lost, stopping.ElapsedMilliseconds));

                // One frame per ~11 ms is the idle rate; under load the device sends frames more often
                // (about 500 per second at 6000 cps), so only the lower bound is fixed.
                T.True(frames / wallSeconds > 85 && frames / wallSeconds < 1100, "at least ~91 frames per second (more under load)");
                T.Eq(0, malformed, "no malformed frames");
                T.True(lost < 0.01, "less than 1 % of the stream lost");
                T.True(stopping.ElapsedMilliseconds < 2000, "Stop returns within 2 s");
                T.True(reader.State == AmplitudaUsbReaderState.Stopped, "state is Stopped after Stop");
                T.True(max <= 4095, "codes stay within 12 bits");
            }
            Console.WriteLine("passed " + T.Passed + ", failed " + T.Failed);
            return T.Failed == 0 ? 0 : 1;
        }
    }
}
