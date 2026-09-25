using System;
using System.Collections.Generic;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    public static class LineTests
    {
        static int portCounter;

        static AmplitudaSerialSettings Settings()
        {
            // A fresh port name per test: the registry is static.
            return new AmplitudaSerialSettings { PortName = "FAKE" + (++portCounter) };
        }

        static FakeBlockPort Fake(params int[] addresses)
        {
            FakeBlockPort fake = new FakeBlockPort();
            foreach (int address in addresses) fake.Blocks[address] = new FakeBlock();
            AmplitudaSerialLine.PortFactory = settings => fake;
            return fake;
        }

        static AmplitudaSerialReading Run(AmplitudaSerialLine line, AmplitudaSerialRequestKind kind, byte address, byte command)
        {
            AmplitudaSerialReading result = null;
            line.Enqueue(new AmplitudaSerialRequest
            {
                Kind = kind, Address = address, Command = command, Channels = 1024,
                Completed = reading => result = reading
            });
            T.True(T.Wait(() => result != null, 5000), "request completed");
            return result ?? new AmplitudaSerialReading();
        }

        public static void TestStatusIsReadTwiceAndKeepsTheGap()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            AmplitudaSerialReading reading = Run(line, AmplitudaSerialRequestKind.Status, 0, 0);
            line.Release();
            T.True(reading.Ok, "status ok");
            T.True(!reading.Running, "block is stopped");
            T.Eq(0, fake.GapViolations, "gap violations");
            T.True(string.Join(" ", fake.WireSnapshot()) == "A00 C13 A00 C13", "wire: " + string.Join(" ", fake.WireSnapshot()));
        }

        public static void TestProbeIsASingleExchange()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            AmplitudaSerialReading present = Run(line, AmplitudaSerialRequestKind.Probe, 0, 0);
            AmplitudaSerialReading absent = Run(line, AmplitudaSerialRequestKind.Probe, 7, 0);
            line.Release();
            T.True(present.Ok, "address 0 answers");
            T.True(!absent.Ok, "address 7 is silent");
            T.True(string.Join(" ", fake.WireSnapshot()) == "A00 C13 A07 C13", "wire: " + string.Join(" ", fake.WireSnapshot()));
        }

        public static void TestReadReturnsSpectrumAndLiveTime()
        {
            FakeBlockPort fake = Fake(0);
            fake.Blocks[0].Channels[252] = 1000;
            fake.Blocks[0].Channels[1023] = 65535;
            fake.Blocks[0].LiveMs = 64000;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            AmplitudaSerialReading reading = Run(line, AmplitudaSerialRequestKind.Read, 0, 0);
            line.Release();
            T.True(reading.Ok, "read ok: " + reading.Error);
            T.Eq(1024, reading.Channels.Length, "channels");
            T.Eq(1000, reading.Channels[252], "channel 252");
            T.Eq(65535, reading.Channels[1023], "channel 1023");
            T.Eq(64, reading.LiveSeconds, "live seconds");
            T.Eq(0, fake.GapViolations, "gap violations");
        }

        public static void TestCorruptedBlockIsRetried()
        {
            FakeBlockPort fake = Fake(0);
            fake.Blocks[0].Channels[10] = 7;
            fake.Blocks[0].CorruptNext = 1;         // the first header arrives damaged
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            AmplitudaSerialReading reading = Run(line, AmplitudaSerialRequestKind.Read, 0, 0);
            line.Release();
            T.True(reading.Ok, "read ok after retries: " + reading.Error);
            T.Eq(7, reading.Channels[10], "channel 10");
            int headers = 0;
            foreach (string item in fake.WireSnapshot()) if (item == "C81") headers++;
            T.Eq(2, headers, "header requested twice");
        }

        public static void TestPersistentCorruptionFails()
        {
            FakeBlockPort fake = Fake(0);
            fake.Blocks[0].CorruptNext = 100;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            AmplitudaSerialReading reading = Run(line, AmplitudaSerialRequestKind.Read, 0, 0);
            line.Release();
            T.True(!reading.Ok, "read fails");
            T.True(reading.Channels == null, "no channels from a failed read");
        }

        public static void TestSilentBlockFails()
        {
            Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            AmplitudaSerialReading reading = Run(line, AmplitudaSerialRequestKind.Read, 5, 0);
            line.Release();
            T.True(!reading.Ok, "no block at address 5");
        }

        public static void TestControlJumpsAheadOfReadSteps()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.Gate.Reset();                      // the worker blocks inside its first Write
            AmplitudaSerialReading read = null, control = null;
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Completed = r => read = r });
            System.Threading.Thread.Sleep(50);      // let the worker reach the gate inside the status step
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = 0, Command = AmplitudaSerialProtocol.CommandStop, Completed = r => control = r });
            fake.Gate.Set();
            T.True(T.Wait(() => read != null && control != null, 5000), "both completed");
            line.Release();
            List<string> wire = new List<string>(fake.WireSnapshot());
            T.True(wire.IndexOf("C15") >= 0, "stop was sent");
            T.True(wire.IndexOf("C15") < wire.IndexOf("C83"), "stop went before the last page of the read");
            T.True(read != null && read.Ok, "the read still succeeded");
        }

        // I2/M2: a probe travels with the reads, not urgent, so a pending "Find blocks" scan
        // must not delay Release() by anywhere near the time the whole scan would take.
        // Deterministic via the fake's gate: closed before anything is enqueued, so the worker
        // is parked inside the very first exchange's address Write and cannot race ahead of the
        // test thread's Enqueue/Release calls, instead of relying on which thread the OS happens
        // to schedule first.
        public static void TestProbesDoNotDelayClosing()
        {
            FakeBlockPort fake = Fake(0);
            fake.Blocks[0].Running = true;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.Gate.Reset();          // the worker blocks inside the first exchange's first Write
            for (byte address = 0; address < 16; address++)
            {
                line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Probe, Address = address, Channels = 1024 });
            }
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = 0, Command = AmplitudaSerialProtocol.CommandStop });
            System.Threading.Thread opener = new System.Threading.Thread(delegate ()
            {
                System.Threading.Thread.Sleep(100);
                fake.Gate.Set();
            });
            opener.Start();
            line.Release();             // sets closing while the worker is still parked at the gate
            opener.Join();
            T.True(!fake.Blocks[0].Running, "block was stopped before the port closed");
            T.True(fake.Disposed, "port closed");
            List<string> wire = new List<string>(fake.WireSnapshot());
            T.True(wire.IndexOf("C15") >= 0, "stop was sent: " + string.Join(" ", wire));
            int probes = 0;
            foreach (string item in wire)
            {
                if (item == "C13") probes++;
            }
            T.True(probes <= 2, "only the exchange already in flight (if a probe) should have gone out, got " +
                probes + ": " + string.Join(" ", wire));
        }

        public static void TestTwoClientsShareOnePort()
        {
            FakeBlockPort fake = Fake(0, 1);
            fake.Blocks[0].Channels[5] = 11;
            fake.Blocks[1].Channels[5] = 22;
            AmplitudaSerialSettings settings = Settings();
            string error;
            AmplitudaSerialLine first = AmplitudaSerialLine.Acquire(settings, out error);
            AmplitudaSerialLine second = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = settings.PortName.ToLowerInvariant() }, out error);
            T.True(ReferenceEquals(first, second), "same line for the same port, case-insensitive");
            T.Eq(1, fake.OpenCount, "port opened once");
            AmplitudaSerialReading a = null, b = null;
            first.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Completed = r => a = r });
            second.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 1, Channels = 1024, Completed = r => b = r });
            T.True(T.Wait(() => a != null && b != null, 5000), "both reads completed");
            T.Eq(11, a != null && a.Ok ? a.Channels[5] : -1, "block 0 data");
            T.Eq(22, b != null && b.Ok ? b.Channels[5] : -1, "block 1 data");
            T.Eq(0, fake.GapViolations, "gap violations");
            first.Release();
            T.True(!fake.Disposed, "port stays open for the second client");
            second.Release();
            T.True(fake.Disposed, "port closed after the last client");
        }

        public static void TestDifferentParametersAreRefused()
        {
            Fake(0);
            AmplitudaSerialSettings settings = Settings();
            string error;
            AmplitudaSerialLine first = AmplitudaSerialLine.Acquire(settings, out error);
            AmplitudaSerialLine second = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = settings.PortName, GapMs = 50 }, out error);
            T.True(second == null, "second client refused");
            T.True(error == AmplitudaSerialLine.ErrorSettingsDiffer, "error code: " + error);
            first.Release();
        }

        public static void TestOpenFailureIsReported()
        {
            FakeBlockPort fake = Fake(0);
            fake.OpenFails = true;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            T.True(line == null, "no line");
            T.True(error != null && error.Contains("access denied"), "error text: " + error);
            T.True(fake.Disposed, "failed port disposed");
        }

        public static void TestReleaseSendsPendingControls()
        {
            FakeBlockPort fake = Fake(0);
            fake.Blocks[0].Running = true;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = 0, Command = AmplitudaSerialProtocol.CommandStop });
            line.Release();
            T.True(!fake.Blocks[0].Running, "block was stopped before the port closed");
            T.True(fake.Disposed, "port closed");
        }

        public static void TestPortFailureIsReportedAndPortReopened()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.WriteFails = true;
            AmplitudaSerialReading failed = Run(line, AmplitudaSerialRequestKind.Status, 0, 0);
            fake.WriteFails = false;
            AmplitudaSerialReading ok = Run(line, AmplitudaSerialRequestKind.Status, 0, 0);
            line.Release();
            T.True(!failed.Ok && failed.Error != null, "failure reported");
            T.True(ok.Ok, "works again");
            T.Eq(2, fake.OpenCount, "port reopened");
        }
    }
}
