using System;
using System.IO;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaUsbTests
{
    public static class AccumulatorTests
    {
        public static void TestShiftForChannelCounts()
        {
            int shift;
            T.True(AmplitudaUsbAccumulator.TryGetShift(4096, out shift) && shift == 0, "4096 channels -> shift 0");
            T.True(AmplitudaUsbAccumulator.TryGetShift(2048, out shift) && shift == 1, "2048 channels -> shift 1");
            T.True(AmplitudaUsbAccumulator.TryGetShift(1024, out shift) && shift == 2, "1024 channels -> shift 2");
            T.True(AmplitudaUsbAccumulator.TryGetShift(512, out shift) && shift == 3, "512 channels -> shift 3");
            T.True(AmplitudaUsbAccumulator.TryGetShift(256, out shift) && shift == 4, "256 channels -> shift 4");
            T.True(!AmplitudaUsbAccumulator.TryGetShift(2500, out shift), "2500 channels are not supported");
            T.True(!AmplitudaUsbAccumulator.TryGetShift(8192, out shift), "8192 channels are not supported");
            T.True(!AmplitudaUsbAccumulator.TryGetShift(0, out shift), "0 channels are not supported");
        }

        public static void TestApplyBinsCodes()
        {
            ushort[] codes = { 0, 4095, 1023, 1024 };
            int[] s4096 = new int[4096];
            T.Eq(4, AmplitudaUsbAccumulator.Apply(s4096, codes, 4, 0, 0, 4095), "all four accepted");
            T.Eq(1, s4096[0], "code 0 -> channel 0");
            T.Eq(1, s4096[4095], "code 4095 -> channel 4095");

            int[] s1024 = new int[1024];
            AmplitudaUsbAccumulator.Apply(s1024, codes, 4, 2, 0, 4095);
            T.Eq(1, s1024[1023], "code 4095 -> channel 1023 at shift 2");
            T.Eq(1, s1024[255], "code 1023 -> channel 255 at shift 2");
            T.Eq(1, s1024[256], "code 1024 -> channel 256 at shift 2");

            int[] s2048 = new int[2048];
            AmplitudaUsbAccumulator.Apply(s2048, codes, 4, 1, 0, 4095);
            T.Eq(1, s2048[2047], "code 4095 -> channel 2047 at shift 1");
        }

        public static void TestThresholdsAreInclusive()
        {
            ushort[] codes = { 99, 100, 101, 3999, 4000, 4001 };
            int[] spectrum = new int[4096];
            T.Eq(4, AmplitudaUsbAccumulator.Apply(spectrum, codes, 6, 0, 100, 4000), "codes 100..4000 inclusive are accepted");
            T.Eq(0, spectrum[99], "below the lower threshold is cut");
            T.Eq(1, spectrum[100], "lower threshold itself is kept");
            T.Eq(1, spectrum[4000], "upper threshold itself is kept");
            T.Eq(0, spectrum[4001], "above the upper threshold is cut");
            long sum = 0;
            foreach (int v in spectrum) sum += v;
            T.Eq(4, sum, "histogram sum equals the accepted count");
        }

        public static void TestApplyHonoursCountAndArrayBounds()
        {
            ushort[] codes = { 10, 20, 30 };
            int[] spectrum = new int[4096];
            T.Eq(2, AmplitudaUsbAccumulator.Apply(spectrum, codes, 2, 0, 0, 4095), "only the first two codes are used");
            T.Eq(0, spectrum[30], "third code is beyond count");

            int[] tiny = new int[16];                       // wrong geometry must not throw
            T.Eq(0, AmplitudaUsbAccumulator.Apply(tiny, new ushort[] { 4095 }, 1, 0, 0, 4095), "out-of-range channel is skipped");
        }

        public static void TestBatchAccounting()
        {
            AmplitudaUsbAccumulator acc = new AmplitudaUsbAccumulator();
            acc.AddFrame(new ushort[30], 0, 10994, 14.0);
            acc.AddFrame(new ushort[] { 308, 671 }, 2, 10966, 14.0);
            acc.AddMalformedFrame();

            AmplitudaUsbBatch batch = acc.TakeBatch();
            T.Eq(2, batch.Frames, "two good frames");
            T.Eq(1, batch.MalformedFrames, "one malformed frame");
            T.Eq(2, batch.Count, "two events");
            T.Eq(308, batch.Codes[0], "first code kept in order");
            T.Eq(671, batch.Codes[1], "second code kept in order");
            T.Eq(10994 + 10966, batch.LiveMicros, "live time is summed");
            T.Eq(10994 + 10966 + 28, batch.RealMicros, "real time = live + 14 us per event");

            AmplitudaUsbBatch empty = acc.TakeBatch();
            T.Eq(0, empty.Frames, "TakeBatch resets frames");
            T.Eq(0, empty.Count, "TakeBatch resets events");
            T.Eq(0, empty.RealMicros, "TakeBatch resets time");
        }

        public static void TestBatchGrowsBeyondInitialCapacity()
        {
            AmplitudaUsbAccumulator acc = new AmplitudaUsbAccumulator();
            ushort[] full = new ushort[30];
            for (int i = 0; i < 30; i++) full[i] = (ushort)(i + 1);
            for (int f = 0; f < 100; f++) acc.AddFrame(full, 30, 10574, 14.0);
            AmplitudaUsbBatch batch = acc.TakeBatch();
            T.Eq(3000, batch.Count, "3000 events survive buffer growth");
            T.Eq(30, batch.Codes[2999], "last code is intact");
        }

        public static void TestConcurrentProducerAndConsumer()
        {
            const int frames = 200000;
            AmplitudaUsbAccumulator acc = new AmplitudaUsbAccumulator();
            Thread producer = new Thread(() =>
            {
                ushort[] one = { 1234 };
                for (int i = 0; i < frames; i++) acc.AddFrame(one, 1, 10980, 14.0);
            });
            long events = 0, real = 0, gotFrames = 0;
            producer.Start();
            while (producer.IsAlive)
            {
                AmplitudaUsbBatch b = acc.TakeBatch();
                events += b.Count; real += b.RealMicros; gotFrames += b.Frames;
            }
            producer.Join();
            AmplitudaUsbBatch last = acc.TakeBatch();
            events += last.Count; real += last.RealMicros; gotFrames += last.Frames;
            T.Eq(frames, events, "no event lost or duplicated across threads");
            T.Eq(frames, gotFrames, "no frame lost or duplicated across threads");
            T.Eq((long)frames * (10980 + 14), real, "no time lost or duplicated across threads");
        }

        // Replays a capture from real hardware: 64-byte records, 300 s, unshielded background.
        public static void TestRecordedDump()
        {
            if (T.DumpPath.Length == 0 || !File.Exists(T.DumpPath))
            {
                Console.WriteLine("  SKIPPED: pass -Dump <path> to replay the recorded capture");
                return;
            }
            byte[] data = File.ReadAllBytes(T.DumpPath);
            T.Eq(0, data.Length % 64, "dump consists of whole 64-byte records");

            AmplitudaUsbAccumulator acc = new AmplitudaUsbAccumulator();
            byte[] report = new byte[64];
            ushort[] codes = new ushort[AmplitudaUsbProtocol.MaxEvents];
            for (int offset = 0; offset + 64 <= data.Length; offset += 64)
            {
                Buffer.BlockCopy(data, offset, report, 0, 64);
                int count, live;
                if (AmplitudaUsbProtocol.TryDecode(report, 64, codes, out count, out live)) acc.AddFrame(codes, count, live, 14.0);
                else acc.AddMalformedFrame();
            }
            AmplitudaUsbBatch batch = acc.TakeBatch();
            T.Eq(27272, batch.Frames, "frames in the capture");
            T.Eq(0, batch.MalformedFrames, "no malformed frames in the capture");
            T.Eq(3381, batch.Count, "events in the capture");
            T.Near(299.77, batch.LiveMicros / 1e6, 0.01, "live time of the capture, s");
            T.Eq(batch.LiveMicros + 14L * 3381, batch.RealMicros, "real time = live + 14 us per event");

            int min = int.MaxValue, max = 0;
            for (int i = 0; i < batch.Count; i++) { min = Math.Min(min, batch.Codes[i]); max = Math.Max(max, batch.Codes[i]); }
            T.Eq(69, min, "lowest code in the capture");
            T.Eq(4093, max, "highest code in the capture");

            int[] spectrum = new int[4096];
            T.Eq(3381, AmplitudaUsbAccumulator.Apply(spectrum, batch.Codes, batch.Count, 0, 0, 4095), "every event is binned");
        }

        public static void TestTakenBatchIsNeverReused()
        {
            AmplitudaUsbAccumulator acc = new AmplitudaUsbAccumulator();
            acc.AddFrame(new ushort[] { 111 }, 1, 10980, 14.0);
            AmplitudaUsbBatch first = acc.TakeBatch();
            acc.AddFrame(new ushort[] { 222 }, 1, 10980, 14.0);
            AmplitudaUsbBatch second = acc.TakeBatch();
            AmplitudaUsbBatch third = acc.TakeBatch();

            T.True(!ReferenceEquals(first, second) && !ReferenceEquals(second, third), "every TakeBatch returns a new object");
            T.Eq(1, first.Count, "the first batch is intact after later TakeBatch calls");
            T.Eq(111, first.Codes[0], "the first batch keeps its code");
            T.Eq(10994, first.RealMicros, "the first batch keeps its time");
            T.Eq(222, second.Codes[0], "the second batch has its own code");
            T.Eq(0, third.Count, "an idle interval gives an empty batch");
        }

        public static void TestInvertedThresholdsAcceptNothing()
        {
            int[] spectrum = new int[4096];
            T.Eq(0, AmplitudaUsbAccumulator.Apply(spectrum, new ushort[] { 0, 2000, 4095 }, 3, 0, 3000, 100), "lower above upper accepts nothing");
            long sum = 0;
            foreach (int v in spectrum) sum += v;
            T.Eq(0, sum, "the spectrum stays empty");
        }
    }
}
