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

        // Review of 13.10.2026, item 12: the message for a refused Acquire names the pause the
        // port is really open with (the line of a running measurement - possibly of the SAME
        // configuration, whose form holds an unsaved pause change), not "another configuration".
        // Called by reflection so that the runner still builds on the code before the fix.
        public static void TestOpenPauseIsKnownForTheRefusal()
        {
            Fake(0);
            AmplitudaSerialSettings settings = Settings();
            settings.GapMs = 50;
            string error;
            AmplitudaSerialLine first = AmplitudaSerialLine.Acquire(settings, out error);
            AmplitudaSerialLine second = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = settings.PortName, GapMs = 20 }, out error);
            T.True(second == null && error == AmplitudaSerialLine.ErrorSettingsDiffer, "refused: " + error);
            System.Reflection.MethodInfo openGap = typeof(AmplitudaSerialLine).GetMethod("OpenGapMs");
            T.True(openGap != null, "the line can tell the pause it is open with");
            if (openGap != null)
            {
                T.Eq(50, (int)openGap.Invoke(null, new object[] { settings.PortName.ToLowerInvariant() }), "pause of the open line");
                first.Release();
                T.Eq(0, (int)openGap.Invoke(null, new object[] { settings.PortName }), "no line open after Release");
            }
            else
            {
                first.Release();
            }
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
            // A soft line error must not close the port: a block powered from DTR/RTS would lose power.
            T.Eq(1, fake.OpenCount, "port stayed open after a soft error");
        }

        public static void TestDeadPortIsReopened()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.WriteFails = true;
            fake.PortDead = true;
            AmplitudaSerialReading failed = Run(line, AmplitudaSerialRequestKind.Status, 0, 0);
            fake.WriteFails = false;
            fake.PortDead = false;
            AmplitudaSerialReading ok = Run(line, AmplitudaSerialRequestKind.Status, 0, 0);
            line.Release();
            T.True(!failed.Ok && failed.Error != null, "failure reported");
            T.True(ok.Ok, "works again");
            T.Eq(2, fake.OpenCount, "port reopened");
        }

        // A318, minor: Release() waits for the exchange in flight and the pending control
        // commands before it disposes the port. At the longest gap the settings allow (200 ms) a
        // page step with all three tries takes about 4.4 s on the wire; the old fixed 3 s Join
        // gave up first, and the stop command queued behind that step never went out - the
        // block went on counting. The step is held here in REAL time (the wait is a real Join).
        public static void TestReleaseWaitsForASlowStepAtALongGap()
        {
            FakeBlockPort fake = Fake(0);
            fake.GapMs = 200;
            fake.Blocks[0].Running = true;
            AmplitudaSerialSettings settings = Settings();
            settings.GapMs = 200;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(settings, out error);
            fake.ArmHold(0, AmplitudaSerialProtocol.PageCommand(0));
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024 });
            T.True(fake.Held.WaitOne(5000), "a read step is in flight");
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = 0, Command = AmplitudaSerialProtocol.CommandStop });
            System.Threading.Thread slowStep = new System.Threading.Thread(delegate ()
            {
                System.Threading.Thread.Sleep(4400);
                fake.ReleaseHold();
            });
            slowStep.Start();
            line.Release();
            slowStep.Join();
            List<string> wire = new List<string>(fake.WireSnapshot());
            int stopAt = wire.IndexOf("C15");
            T.True(stopAt >= 0 && stopAt < fake.WireCountAtDispose, "the stop command went out before the port was disposed: stop at "
                + stopAt + ", disposed at " + fake.WireCountAtDispose + ": " + string.Join(" ", wire));
            T.True(!fake.Blocks[0].Running, "block was stopped");
        }

        // Finding 2: the final reading of a Stop must not wait behind other documents' polls.
        public static void TestUrgentReadOvertakesAForeignJob()
        {
            FakeBlockPort fake = Fake(0, 1);
            fake.Blocks[0].Channels[5] = 11;
            fake.Blocks[1].Channels[5] = 22;
            fake.Blocks[1].Channels[700] = 33;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.Gate.Reset();                      // the worker parks inside its first exchange
            AmplitudaSerialReading foreign = null, urgent = null;
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 1, Channels = 1024, Completed = r => foreign = r });
            System.Threading.Thread.Sleep(50);
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Urgent = true, Completed = r => urgent = r });
            fake.Gate.Set();
            T.True(T.Wait(() => foreign != null && urgent != null, 5000), "both completed");
            line.Release();
            T.True(urgent != null && urgent.Ok && urgent.Channels[5] == 11, "urgent read data");
            T.True(foreign != null && foreign.Ok && foreign.Channels[5] == 22 && foreign.Channels[700] == 33,
                "the parked foreign job kept its data");
            List<string> wire = new List<string>(fake.WireSnapshot());
            string text = string.Join(" ", wire);
            int urgentLast = -1, foreignLast = -1;
            for (int i = 0; i + 1 < wire.Count; i++)
            {
                if (wire[i] == "A00" && wire[i + 1] == "C83") urgentLast = i;
                if (wire[i] == "A01" && wire[i + 1] == "C83") foreignLast = i;
            }
            T.True(urgentLast >= 0 && foreignLast >= 0, "both last pages on the wire: " + text);
            T.True(urgentLast < foreignLast, "urgent read finished before the foreign job's last page: " + text);
        }

        // ---- Task 10 A: cancelling a read request ----

        static bool IsDataFrame(string code)
        {
            return code == "C81" || code == "C82" || code == "C83";
        }

        public static void TestCancelledPendingReadNeverReachesTheWire()
        {
            FakeBlockPort fake = Fake(0, 1);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.Gate.Reset();                      // the worker parks inside the first read's first Write
            AmplitudaSerialReading first = null, second = null;
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 1, Channels = 1024, Completed = r => first = r });
            System.Threading.Thread.Sleep(50);
            AmplitudaSerialRequest pending = new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Completed = r => second = r };
            line.Enqueue(pending);
            line.Cancel(pending);
            T.True(second != null && !second.Ok && second.Error == "cancelled", "completed at once as cancelled: " + (second == null ? "null" : second.Error));
            fake.Gate.Set();
            T.True(T.Wait(() => first != null, 5000), "the other read completed");
            line.Release();
            T.True(first != null && first.Ok, "the other read is unharmed");
            List<string> wire = new List<string>(fake.WireSnapshot());
            T.True(!wire.Contains("A00"), "the cancelled read never reached the wire: " + string.Join(" ", wire));
        }

        public static void TestCancelledUrgentPendingReadNeverReachesTheWire()
        {
            FakeBlockPort fake = Fake(0, 1);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.Gate.Reset();
            AmplitudaSerialReading first = null, second = null;
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 1, Channels = 1024, Completed = r => first = r });
            System.Threading.Thread.Sleep(50);
            AmplitudaSerialRequest pending = new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Urgent = true, Completed = r => second = r };
            line.Enqueue(pending);
            line.Cancel(pending);
            T.True(second != null && !second.Ok && second.Error == "cancelled", "completed at once as cancelled");
            fake.Gate.Set();
            T.True(T.Wait(() => first != null, 5000), "the other read completed");
            line.Release();
            T.True(!new List<string>(fake.WireSnapshot()).Contains("A00"), "the cancelled urgent read never reached the wire");
        }

        public static void TestCancelledInFlightReadEndsAtTheStepBoundary()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            fake.Gate.Reset();                      // the worker is inside the status step
            AmplitudaSerialReading result = null;
            AmplitudaSerialRequest request = new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Completed = r => result = r };
            line.Enqueue(request);
            System.Threading.Thread.Sleep(50);
            line.Cancel(request);
            T.True(result == null, "an in-flight read is not completed by Cancel itself");
            fake.Gate.Set();
            T.True(T.Wait(() => result != null, 5000), "completed");
            line.Release();
            T.True(result != null && !result.Ok && result.Error == "cancelled", "cancelled: " + (result == null ? "null" : result.Error));
            foreach (string code in fake.WireSnapshot())
            {
                T.True(!IsDataFrame(code), "no header or page frame after the cancel: " + code);
            }
        }

        public static void TestCancellingACompletedRequestIsHarmless()
        {
            FakeBlockPort fake = Fake(0);
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(Settings(), out error);
            int completions = 0;
            AmplitudaSerialReading result = null;
            AmplitudaSerialRequest request = new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024, Completed = r => { completions++; result = r; } };
            line.Enqueue(request);
            T.True(T.Wait(() => result != null, 5000), "completed");
            line.Cancel(request);
            line.Cancel(request);
            line.Release();
            T.True(result.Ok, "the finished result stays ok");
            T.Eq(1, completions, "completed exactly once");
        }

        // ---- Task 10 B: the stop budget grows with the gap ----

        static AmplitudaSerialLine LineWithGap(int gapMs)
        {
            Fake(0);
            string error;
            return AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = "FAKE" + (++portCounter), GapMs = gapMs }, out error);
        }

        public static void TestStopBudgetGrowsWithTheGap()
        {
            AmplitudaSerialLine line = LineWithGap(20);
            T.Eq(8000, line.StopBudgetMs(1024), "1024 channels at the default gap");
            T.Eq(27500, line.StopBudgetMs(8192), "8192 channels at the default gap");
            line.Release();
            line = LineWithGap(200);
            T.Eq(14840, line.StopBudgetMs(1024), "1024 channels at gap 200");
            line.Release();
        }

        // ---- Task 10 C: the join covers a step at a large gap ----

        public static void TestJoinCoversAStepAtTheConfiguredGap()
        {
            AmplitudaSerialLine line = LineWithGap(20);
            T.Near(3881, line.JoinMs, 50, "join at gap 20");
            line.Release();
            line = LineWithGap(200);
            T.Near(6761, line.JoinMs, 50, "join at gap 200");
            line.Release();
        }
    }
}
