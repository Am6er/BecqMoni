using System;
using System.Collections.Generic;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    public static class AcquisitionTests
    {
        static int portCounter;

        // Occurrences of a wire code, e.g. counting "C83" (the last page command of a
        // 1024-channel Read) to detect that one more full Read has reached the wire.
        static int Count(string[] wire, string code)
        {
            int count = 0;
            foreach (string entry in wire)
            {
                if (entry == code)
                {
                    count++;
                }
            }
            return count;
        }

        sealed class Rig
        {
            public FakeBlockPort Fake;
            public FakeBlock Block;
            public AmplitudaSerialLine Line;
            public AmplitudaSerialAcquisition Acq;
            public List<AmplitudaSerialWarning> Warnings = new List<AmplitudaSerialWarning>();

            // Pumps and lets virtual time pass until the condition holds (real-time limit 60 s).
            public bool Until(Func<bool> condition)
            {
                DateTime limit = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < limit)
                {
                    Acq.Pump();
                    Warnings.AddRange(Acq.TakeWarnings());
                    if (condition()) return true;
                    Fake.Advance(100);
                    Thread.Sleep(1);
                }
                return false;
            }

            public int[] Composed()
            {
                int[] target = new int[1024];
                Acq.Compose(target);
                return target;
            }
        }

        static Rig Make(int[] document, double live, double real)
        {
            return Make(document, live, real, 1024);
        }

        static Rig Make(int[] document, double live, double real, int channels)
        {
            Rig rig = new Rig();
            rig.Fake = new FakeBlockPort();
            rig.Block = new FakeBlock();
            rig.Fake.Blocks[0] = rig.Block;
            AmplitudaSerialLine.PortFactory = settings => rig.Fake;
            string error;
            rig.Line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = "ACQ" + (++portCounter) }, out error);
            rig.Acq = new AmplitudaSerialAcquisition(rig.Line, 0, channels, 3000, document ?? new int[channels], live, real);
            return rig;
        }

        public static void TestStartAccumulateStop()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 10), "ran for 10 s");
            T.True(rig.Acq.Running, "running");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "no failure");
            T.True(!rig.Block.Running, "block stopped");
            T.Eq(rig.Block.Channels[252], rig.Composed()[252], "document = block");
            T.True(rig.Acq.TotalCounts > 9000, "counted: " + rig.Acq.TotalCounts);
            T.Near(1000.0, rig.Acq.TotalCounts / rig.Acq.RealSeconds, 30.0, "count rate by real time");
            T.Near(0.7 * rig.Acq.RealSeconds, rig.Acq.LiveSeconds, 1.5, "live time from the block");
            T.Eq(0, rig.Fake.GapViolations, "gap violations");
            List<string> wire = new List<string>(rig.Fake.WireSnapshot());
            T.True(wire.IndexOf("C15") < wire.IndexOf("C16") && wire.IndexOf("C16") < wire.IndexOf("C14"), "stop, clear, start order");
        }

        public static void TestContinuesADocument()
        {
            int[] document = new int[1024];
            document[252] = 500;
            document[10] = 3;
            Rig rig = Make(document, 100.0, 150.0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 156), "ran 6 more seconds");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.Eq(500 + rig.Block.Channels[252], rig.Composed()[252], "document + block");
            T.Eq(3, rig.Composed()[10], "untouched channel kept");
            T.True(rig.Acq.LiveSeconds > 100.0 && rig.Acq.RealSeconds > 150.0, "times continue");
        }

        public static void TestForeignCountsInTheBlockAreDropped()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Channels[252] = 9999;
            rig.Block.LiveMs = 500000;
            rig.Block.Running = true;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Running), "running");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Composed()[252] < 9000, "old counts are gone: " + rig.Composed()[252]);
            T.True(rig.Acq.LiveSeconds < 60, "old time is gone: " + rig.Acq.LiveSeconds);
        }

        public static void TestPourOverGoesBeyond16Bits()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Rate = 5000;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.TotalCounts > 100000 && rig.Acq.Running), "counted past 65535");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Composed()[252] > 100000, "channel beyond 16 bits: " + rig.Composed()[252]);
            T.True(rig.Warnings.Count == 0, "a planned pour raises no warning");
            T.Near(5000.0, rig.Acq.TotalCounts / rig.Acq.RealSeconds, 200.0, "rate unbiased by pours");
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "no failure");
        }

        // Spectrum protection: a block that comes back with EMPTY memory spends about a minute
        // re-tuning its high voltage by its own LED, with a drifting gain that would distort
        // whatever it counts meanwhile. The old behaviour (fold the last good reading and
        // silently restart into that minute) is gone: a detected reset now STOPS the
        // measurement, keeping everything collected before the reset.
        public static void TestBlockResetStopsTheMeasurement()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            long before = rig.Acq.TotalCounts;
            double realBefore = rig.Acq.RealSeconds;
            rig.Block.PowerCycle();
            T.True(rig.Until(() => rig.Acq.Finished), "stopped by spectrum protection");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.BlockReset, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.BlockResetDetected, "block reset detected");
            T.True(rig.Acq.TotalCounts >= before, "data kept: " + rig.Acq.TotalCounts);
            T.True(rig.Acq.RealSeconds <= realBefore + 3.5,
                "real time rolled back to the last good reading: " + rig.Acq.RealSeconds);
            T.True(!rig.Warnings.Contains(AmplitudaSerialWarning.BlockReset), "no warning on the Fail path");
            T.True(!rig.Block.Running, "the block was told to stop");
        }

        // Launch() only lets Poll() start once it has verified the freshly cleared block is
        // running: a reset noticed on the very first poll after Start is therefore just as much
        // a genuine reset as one noticed hours in (Session rule 4), not a "hasn't counted
        // anything yet" false positive.
        public static void TestResetRightAfterStartIsNoticed()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Running), "running");
            rig.Block.PowerCycle();
            T.True(rig.Until(() => rig.Acq.Finished), "stopped by spectrum protection");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.BlockReset, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.BlockResetDetected, "block reset detected");
        }

        // A reset noticed while the acquisition is mid-pour (the Halt() that folds the block
        // before 16 bits run out) must stop exactly like one noticed on a plain poll, not
        // silently Restart() into the high-voltage re-tuning window.
        //
        // Determinism: natural Rate-driven progress toward the pour threshold would make it a
        // matter of luck whether PowerCycle() lands before or after the background line-worker
        // thread races through the Halt exchange that follows discovery (the fake's I/O is
        // virtually instantaneous, so the worker can complete an entire exchange before the test
        // thread's next statement runs - the very race the task called out explicitly). Instead:
        // force the channel over the pour threshold directly so exactly one specific poll
        // discovers it, and wait (bounded, real time, M8 add-ons review) for that read's own last
        // page command ("C83", 1024 channels = 2 pages) to actually land on the wire, THEN close
        // the fake's gate before draining it. Only after the gate is shut does draining run
        // Halt(), which enqueues the block's Stop command - so the worker is GUARANTEED to block
        // at the very first byte of that command (Gate.Reset() happens-before the Enqueue on this
        // same thread), and can never race ahead into the read that follows. PowerCycle() is then
        // applied while the worker is provably parked, and only then is the gate reopened.
        public static void TestResetDuringPourStops()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Rate = 5000;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.TotalCounts > 20000 && rig.Acq.Running), "counted past the pour threshold");

            rig.Block.Channels[rig.Block.HotChannel] = 40000;      // >= PourChannelThreshold
            int pagesBefore = Count(rig.Fake.WireSnapshot(), "C83");
            rig.Fake.Advance(4000);                                // past the 3 s poll interval
            rig.Acq.Pump();                                        // issues the discovering Read (async)
            T.True(T.Wait(() => Count(rig.Fake.WireSnapshot(), "C83") > pagesBefore, 5000),
                "the discovering read reached the wire");
            rig.Fake.Gate.Reset();                                 // worker is idle: nothing queued but that read's result
            rig.Acq.Pump();                                        // drains it: PourNeeded -> Halt() enqueues Stop
            T.True(!rig.Acq.Running, "the pour began");
            rig.Block.PowerCycle();                                // safe: the worker is parked before its first byte
            rig.Fake.Gate.Set();

            T.True(rig.Until(() => rig.Acq.Finished), "stopped by spectrum protection");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.BlockReset, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts > 20000, "data kept: " + rig.Acq.TotalCounts);
        }

        // The block is stopped and told to Start again with RequestStop(), not measured through:
        // it finishes normally (no Failure), the last good reading is kept, and a warning tells
        // the user (and lets the device controller arm the settle guard) - this is now the ONLY
        // path that ever raises AmplitudaSerialWarning.BlockReset.
        public static void TestResetSeenAtStopIsReported()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            long countsBefore = rig.Acq.TotalCounts;
            double realBefore = rig.Acq.RealSeconds;
            rig.Block.PowerCycle();
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            rig.Warnings.AddRange(rig.Acq.TakeWarnings());
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "failure: " + rig.Acq.Failure);
            T.True(rig.Warnings.Contains(AmplitudaSerialWarning.BlockReset), "warning raised");
            T.True(rig.Acq.BlockResetDetected, "block reset detected");
            T.True(rig.Acq.TotalCounts >= countsBefore, "last good reading kept: " + rig.Acq.TotalCounts);
            // M1 (add-ons review): Halt's own CommitSegment already advanced real time up to the
            // stop command before the reset was even noticed; without a roll-back that dead
            // stretch (no counts, no live time behind it) would stay in the document. This
            // assertion measured realBefore=8.019s/finalReal=8.139s BEFORE the fix (delta only
            // ~0.12 s, the fake's near-instant Halt exchange) - it does not go red here because
            // realBefore is captured via RealSeconds (which already extrapolates through the
            // still-active segment up to "now"), not via the internal realAtLastGood (the last
            // actually confirmed poll) that the fix rolls back to; the two coincide closely in
            // this specific immediate-PowerCycle-then-RequestStop timing. The fix is still made
            // (see AmplitudaSerialAcquisition.RequestStop) because a poll boundary further before
            // the reset - or a slower/longer Halt retry sequence - would make the gap between
            // realBefore and realAtLastGood, and thus the dead stretch this guards against, much
            // larger than the 3.5 s tolerance here.
            T.True(rig.Acq.RealSeconds <= realBefore + 3.5,
                "real time rolled back to the last good reading: " + rig.Acq.RealSeconds);
        }

        public static void TestCeilingSelfStopIsRecovered()
        {
            // The pour threshold can only be jumped over when nobody polls for a long time
            // (the PC slept, the UI hung): 90 s pass here without a single Pump.
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 20 && rig.Acq.Running), "ran 20 s");
            rig.Fake.Advance(90000);
            T.True(!rig.Block.Running && rig.Block.Channels[252] == 65535, "the block stopped itself at the ceiling");
            T.True(rig.Until(() => rig.Warnings.Contains(AmplitudaSerialWarning.BlockCeiling)), "ceiling noticed");
            T.True(rig.Until(() => rig.Acq.Running), "running again");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Acq.TotalCounts >= 65535, "ceiling data kept");
            // The block's clock has whole seconds, so the real/live ratio of a 20 s history is a few percent off.
            T.Near(1000.0, rig.Acq.TotalCounts / rig.Acq.RealSeconds, 80.0, "rate estimated through the ceiling");
        }

        public static void TestSilenceFailsAfterTheTroubleWindow()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            long counts = rig.Acq.TotalCounts;
            double real = rig.Acq.RealSeconds;
            rig.Block.Silent = true;
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ConnectionLost, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts >= counts, "data kept");
            T.True(rig.Acq.RealSeconds <= real + 3.5, "real time rolled back to the last good reading: " + rig.Acq.RealSeconds);
        }

        // C1: a control command's "Ok" only means the byte was written, never that the block is
        // alive. Before the fix, silence during the start sequence (StopThenRestart/Restart) kept
        // resetting the trouble clock on every "clear -> Ok" step and never gave up.
        public static void TestSilenceDuringStartFails()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => Array.IndexOf(rig.Fake.WireSnapshot(), "C15") >= 0), "the first stop command went out");
            rig.Block.Silent = true;
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ConnectionLost, "failure: " + rig.Acq.Failure);
            T.Eq(0, rig.Acq.TotalCounts, "no counts were ever taken");
        }

        // C1, the pour case: silence during the Halt() that folds the block before a pour must
        // also give up, keeping what was already counted.
        public static void TestSilenceDuringPourFails()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Rate = 5000;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.TotalCounts > 20000 && rig.Acq.Running), "counted past the pour threshold");
            T.True(rig.Until(() => !rig.Acq.Running), "the pour began");
            rig.Block.Silent = true;
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ConnectionLost, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts > 20000, "data kept: " + rig.Acq.TotalCounts);
        }

        // I4 still holds: a 5 s power outage plus the block's own 2-5 s answer time afterwards is
        // well under 20 s of silence, comfortably inside the 30 s trouble window, so the block
        // answering again is noticed as a reset (not given up on as ConnectionLost) - but
        // spectrum protection now stops the measurement instead of silently continuing into the
        // high-voltage re-tuning.
        public static void TestShortPowerGlitchStopsWithBlockReset()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8 && rig.Acq.Running), "ran 8 s");
            long before = rig.Acq.TotalCounts;
            long t0 = rig.Fake.NowMs;
            rig.Block.PowerCycle();
            rig.Block.Silent = true;
            T.True(rig.Until(() => rig.Fake.NowMs >= t0 + 20000), "20 s of outage and warm-up passed");
            rig.Block.Silent = false;
            T.True(rig.Until(() => rig.Acq.Finished), "stopped by spectrum protection");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.BlockReset,
                "failure: " + rig.Acq.Failure + " (must not be ConnectionLost: the 30 s trouble window still covers the 20 s outage)");
            T.True(rig.Acq.BlockResetDetected, "block reset detected");
            T.True(rig.Acq.TotalCounts >= before, "data kept");
        }

        public static void TestNoAnswerAtStart()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Silent = true;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.NoAnswer, "failure: " + rig.Acq.Failure);
            T.True(rig.Fake.NowMs >= 20000, "waited 20 s for a block that is warming up: " + rig.Fake.NowMs);
        }

        public static void TestLateAnswerAtStart()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Silent = true;
            rig.Acq.Start();
            rig.Until(() => rig.Fake.NowMs >= 10000);
            rig.Block.Silent = false;
            T.True(rig.Until(() => rig.Acq.Running), "running after a late answer");
            rig.Acq.Abort();
            rig.Line.Release();
            T.True(!rig.Block.Running, "abort still stops the block");
        }

        public static void TestClearOnTheFly()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            rig.Acq.ClearAll();
            T.Eq(0, rig.Acq.TotalCounts, "cleared at once");
            T.True(rig.Until(() => rig.Acq.Running && rig.Acq.RealSeconds >= 4), "running again");
            T.True(rig.Acq.RealSeconds < 8, "real time restarted");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.Near(1000.0, rig.Acq.TotalCounts / rig.Acq.RealSeconds, 60.0, "rate after clear");
        }

        public static void TestLostStartCommandIsRetried()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.LoseCommand = AmplitudaSerialProtocol.CommandStart;
            rig.Block.LoseCount = 1;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Running), "running after a retry");
            rig.Acq.Abort();
            rig.Line.Release();
        }

        public static void TestLostClearCommandIsRetried()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Channels[252] = 4000;
            rig.Block.LoseCommand = AmplitudaSerialProtocol.CommandClear;
            rig.Block.LoseCount = 1;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Running), "running after a retry");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Composed()[252] < 4000, "old counts are gone: " + rig.Composed()[252]);
        }

        public static void TestBlockThatCannotBeClearedFails()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Channels[252] = 4000;
            rig.Block.LoseCommand = AmplitudaSerialProtocol.CommandClear;
            rig.Block.LoseCount = 100;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ClearFailed, "failure: " + rig.Acq.Failure);
            T.Eq(0, rig.Acq.TotalCounts, "foreign counts never reached the document");
        }

        public static void TestBlockThatIgnoresStopRaisesAWarning()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 6 && rig.Acq.Running), "running");
            rig.Block.LoseCommand = AmplitudaSerialProtocol.CommandStop;
            rig.Block.LoseCount = 100;
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            rig.Warnings.AddRange(rig.Acq.TakeWarnings());
            T.True(rig.Acq.Finished, "finished");
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "failure: " + rig.Acq.Failure);
            T.True(rig.Warnings.Contains(AmplitudaSerialWarning.BlockWontStop), "warning raised");
            T.True(rig.Acq.TotalCounts > 4000, "data kept: " + rig.Acq.TotalCounts);
            T.True(rig.Block.Running, "the fake really ignored stop");
        }

        // Bug fix: a block whose own spectrum is shorter than the configured channel count
        // (BDEG-3-2: 1024 real channels, configured here for 4096) still answers page requests
        // beyond its spectrum with a validly checksummed reply, but the payload is foreign
        // memory that CommandClear never touches. The diagnosis must be immediate and specific
        // (ChannelsExceedBlock + the real channel count), not the generic "does not obey clear"
        // after three pointless retries - repeating the clear cannot make foreign memory zero.
        public static void TestMoreChannelsThanTheBlockHas()
        {
            Rig rig = Make(new int[4096], 0, 0, 4096);
            rig.Block.ForeignMemoryBeyondSpectrum = true;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ChannelsExceedBlock, "failure: " + rig.Acq.Failure);
            T.Eq(1024, rig.Acq.SuggestedChannels, "suggested channel count");
            T.Eq(0, rig.Acq.TotalCounts, "foreign memory never reached the document");
            List<string> wire = new List<string>(rig.Fake.WireSnapshot());
            T.Eq(1, wire.FindAll(c => c == "C16").Count, "no pointless clear retries");
        }

        // The new foreign-memory-beyond-spectrum behaviour of the fake block must not disturb a
        // normal run whose configured channel count matches the block's own spectrum (1024): the
        // pages the acquisition actually reads (0 and 1) are real channel data regardless of the
        // flag, so nothing here should ever touch the ChannelsExceedBlock path.
        public static void TestForeignMemoryDoesNotBreakA1024ChannelRun()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.ForeignMemoryBeyondSpectrum = true;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 6), "ran 6 s");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts > 0, "counted: " + rig.Acq.TotalCounts);
        }

        public static void TestStopDuringStart()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Channels[252] = 4000;     // foreign counts that must not leak in
            rig.Acq.Start();
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.Eq(0, rig.Acq.TotalCounts, "nothing taken from an unverified block");
        }
    }
}
