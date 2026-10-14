using System;
using System.Collections.Generic;
using System.Globalization;
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
            return Make(document, live, real, channels, 3000, 20);
        }

        static Rig Make(int[] document, double live, double real, int channels, int pollMs, int gapMs)
        {
            Rig rig = new Rig();
            rig.Fake = new FakeBlockPort();
            rig.Fake.GapMs = gapMs;
            rig.Block = new FakeBlock();
            rig.Fake.Blocks[0] = rig.Block;
            AmplitudaSerialLine.PortFactory = settings => rig.Fake;
            string error;
            rig.Line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = "ACQ" + (++portCounter), GapMs = gapMs }, out error);
            rig.Acq = new AmplitudaSerialAcquisition(rig.Line, 0, channels, pollMs, document ?? new int[channels], live, real);
            return rig;
        }

        // Pumps several acquisitions on one line and lets virtual time pass (real-time limit 60 s).
        static bool Until(FakeBlockPort fake, Func<bool> condition, params AmplitudaSerialAcquisition[] acquisitions)
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < limit)
            {
                foreach (AmplitudaSerialAcquisition acquisition in acquisitions)
                {
                    acquisition.Pump();
                }
                if (condition()) return true;
                fake.Advance(100);
                Thread.Sleep(1);
            }
            return false;
        }

        // Exchanges "A<address> C<command>" on the wire from position `from` on.
        static int Exchanges(string[] wire, int from, string address, string command)
        {
            int count = 0;
            for (int i = Math.Max(0, from); i + 1 < wire.Length; i++)
            {
                if (wire[i] == address && wire[i + 1] == command)
                {
                    count++;
                }
            }
            return count;
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
            T.True(rig.Acq.LiveSeconds < 60, "old time is gone: " + rig.Acq.LiveSeconds.ToString(CultureInfo.InvariantCulture));
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
                "real time rolled back to the last good reading: " + rig.Acq.RealSeconds.ToString(CultureInfo.InvariantCulture));
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
            // ...up to the checksum request of its last page (P249, 14.10.2026): with the gate shut
            // between "C83" and that request the read could never complete, and the drain below
            // would find nothing - a latent flake under load. After it no more writes are needed.
            T.True(T.Wait(() =>
            {
                string[] wire = rig.Fake.WireSnapshot();
                int last = Array.LastIndexOf(wire, "C83");
                return Count(wire, "C83") > pagesBefore && last + 2 < wire.Length && wire[last + 2] == "C10";
            }, 5000), "the discovering read reached the wire");
            rig.Fake.Gate.Reset();                                 // worker is idle: nothing queued but that read's result
            // Drains it: PourNeeded -> Halt() enqueues Stop. Pumped until then, not once: the
            // checksum byte may still be on its way when the gate shuts (it needs no write).
            T.True(T.Wait(() => { rig.Acq.Pump(); return !rig.Acq.Running; }, 5000), "the pour began");
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
            // ~0.12 s, the fake's near-instant Halt exchange) - it did not go red here because
            // realBefore was captured via RealSeconds, which then ran through the still-active
            // segment up to "now", not via the internal realAtLastGood (the last actually
            // confirmed poll) that the fix rolls back to; the two coincided closely in this
            // specific immediate-PowerCycle-then-RequestStop timing. (Since P280 RealSeconds IS
            // realAtLastGood while measuring.) The fix is still made
            // (see AmplitudaSerialAcquisition.RequestStop) because a poll boundary further before
            // the reset - or a slower/longer Halt retry sequence - would make the gap between
            // realBefore and realAtLastGood, and thus the dead stretch this guards against, much
            // larger than the 3.5 s tolerance here.
            T.True(rig.Acq.RealSeconds <= realBefore + 3.5,
                "real time rolled back to the last good reading: " + rig.Acq.RealSeconds.ToString(CultureInfo.InvariantCulture));
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
            T.True(rig.Acq.RealSeconds <= real + 3.5, "real time rolled back to the last good reading: " + rig.Acq.RealSeconds.ToString(CultureInfo.InvariantCulture));
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
            T.True(rig.Fake.NowMs >= 20000, "waited 20 s for a block that is warming up: " + rig.Fake.NowMs.ToString(CultureInfo.InvariantCulture));
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
            // The clock (P280): RealSeconds is the real time of the data in hand, and the first
            // reading of the new segment may come just under 4 s.
            T.True(rig.Until(() => rig.Acq.Running && TimeView.Clock(rig.Acq) >= 4), "running again");
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

        // Finding 3: a dead block at Stop must give up in about StopTroubleMs, not the 30 s
        // of a measurement in progress. Virtual time of the fake port.
        public static void TestStopOnASilentBlockGivesUpFast()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            long counts = rig.Acq.TotalCounts;
            rig.Block.Silent = true;
            long tStop = rig.Fake.NowMs;
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            long spent = rig.Fake.NowMs - tStop;
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ConnectionLost, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts >= counts && counts > 0, "data kept");
            T.True(spent <= 9000, "gave up well under TroubleMs (the window starts at the first failed exchange), took " + spent.ToString(CultureInfo.InvariantCulture) + " ms");
        }

        // Maintainer's re-check of #35 (13.10.2026), finding 1: a Stop whose final reading never
        // comes keeps counts and live time at the last good poll - real time must stay there too,
        // not run on to the stop command (that stretch would read as dead time). Before the fix
        // real time ended at or after realAtStop and the count rate came out low.
        public static void TestStopWithoutTheFinalReadingRollsRealTimeBack()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            rig.Block.Silent = true;
            // The clock at the Stop (P280: RealSeconds already shows the last good reading).
            double realAtStop = TimeView.Clock(rig.Acq);
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "gave up");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.ConnectionLost, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.RealSeconds < realAtStop, "real time rolled back to the last good poll: "
                + rig.Acq.RealSeconds.ToString(CultureInfo.InvariantCulture) + " vs " + realAtStop.ToString(CultureInfo.InvariantCulture) + " at Stop");
            T.Near(1000.0, rig.Acq.TotalCounts / rig.Acq.RealSeconds, 60.0, "count rate unbiased by the lost reading");
            T.True(rig.Acq.LiveSeconds <= rig.Acq.RealSeconds, "live <= real");
        }

        // The same, for the controller's give-up: Abort after the stop command already went out
        // (Halt's CommitSegment has run) must not keep the stretch up to that command either.
        public static void TestAbortAfterTheStopCommandRollsRealTimeBack()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            rig.Block.Silent = true;
            int stopsBefore = Count(rig.Fake.WireSnapshot(), "C15");
            double realAtStop = TimeView.Clock(rig.Acq);     // the clock at the Stop (P280)
            long tStop = rig.Fake.NowMs;
            rig.Acq.RequestStop();
            T.True(rig.Until(() => Count(rig.Fake.WireSnapshot(), "C15") > stopsBefore && rig.Fake.NowMs >= tStop + 1000),
                "the stop command went out and was answered");
            T.True(!rig.Acq.Finished, "still waiting for the final reading");
            rig.Acq.Abort();
            rig.Line.Release();
            T.True(rig.Acq.Finished, "finished");
            T.True(rig.Acq.RealSeconds < realAtStop, "real time rolled back to the last good poll: "
                + rig.Acq.RealSeconds.ToString(CultureInfo.InvariantCulture) + " vs " + realAtStop.ToString(CultureInfo.InvariantCulture) + " at Stop");
            T.Near(1000.0, rig.Acq.TotalCounts / rig.Acq.RealSeconds, 60.0, "count rate unbiased by the lost reading");
        }

        // Re-check finding 4: live time is never published above real time. A document whose
        // stored live time exceeds its real time is the plain case of what the half-second block
        // clock correction can produce on a short, lightly loaded segment.
        public static void TestLiveNeverExceedsReal()
        {
            Rig rig = Make(null, 100.4, 100.0);
            T.Near(100.0, rig.Acq.LiveSeconds, 1e-9, "capped at real time");
            T.Near(100.4, rig.Acq.Session.LiveSeconds, 1e-9, "the session itself keeps its value");
            rig.Line.Release();
        }

        // Review I-2: a Stop must survive one lost page (a whole retry cycle of one block fails),
        // the dead-block give-up needs at least two failed halt attempts.
        public static void TestStopWithOneLostPageStillSucceeds()
        {
            Rig rig = Make(null, 0, 0);
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 8), "ran 8 s");
            long before = rig.Acq.TotalCounts;
            rig.Block.CorruptNext = 3;          // all 3 attempts of one data block fail, then it recovers
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts > before, "the final reading was taken: " + rig.Acq.TotalCounts + " vs " + before);
            T.True(!rig.Block.Running, "block stopped");
        }

        // Review M-1: the final segment of a Stop gets the clock correction too.
        public static void TestStopAddsTheClockCorrectionToTheLastSegment()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.LiveFraction = 1.0;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.RealSeconds >= 20), "ran 20 s (no pour below 32768 counts)");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            double blockLive = Math.Floor(rig.Block.LiveMs / 1000.0);
            T.Near(blockLive + AmplitudaSerialSession.SegmentClockCorrectionSeconds, rig.Acq.Session.LiveSeconds, 0.01, "live = block seconds + correction");
            // With no dead time at all the correction may overshoot the measured real time; the
            // published value is then capped at it (re-check finding 4).
            T.Near(Math.Min(blockLive + AmplitudaSerialSession.SegmentClockCorrectionSeconds, rig.Acq.RealSeconds), rig.Acq.LiveSeconds, 0.01, "published live = min(corrected, real)");
            T.True(rig.Acq.LiveSeconds <= rig.Acq.RealSeconds, "live <= real");
        }

        // A318 (2): the Stop budget of the device controller must cover a Stop that is slow only
        // because the line is: the longest gap the settings allow (200 ms) and another document
        // on the same port whose read job uses all three tries on every step. Such a job holds
        // the line for TWO of its steps during our Stop - the one in flight when the stop command
        // is queued (a command never cuts a step) and the one between that command and our urgent
        // read (the read is queued only once the UI thread has seen the command complete). Before
        // the fix the budget was 2000 + 1500 per page, the same at every gap: 8.0 s for 1024
        // channels against about 14 s spent here, 27.5 s for 8192 against about 34 s - a healthy
        // Stop was cut off as "incomplete" and its final reading lost.
        static void StopBehindASlowForeignJob(int channels)
        {
            const int gapMs = 200;
            Rig rig = Make(null, 0, 0, channels, 600000, gapMs);        // no polls of our own
            rig.Block.Channels = new int[channels];
            FakeBlock foreign = new FakeBlock();
            foreign.FlakyTries = 2;
            rig.Fake.Blocks[1] = foreign;
            rig.Acq.Start();
            T.True(rig.Until(() => rig.Acq.Running), "running");
            rig.Fake.Advance(5000);
            rig.Fake.ArmHold(1, AmplitudaSerialProtocol.PageCommand(0));
            rig.Line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Read, Address = 1, Channels = 1024 });
            T.True(rig.Fake.Held.WaitOne(10000), "the other document's read is at its first page");
            long tStop = rig.Fake.NowMs;
            rig.Acq.RequestStop();
            rig.Fake.ReleaseHold();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            long spent = rig.Fake.NowMs - tStop;
            rig.Line.Release();
            int budget = AmplitudaSerialTiming.StopBudgetMs(channels, gapMs);
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "failure: " + rig.Acq.Failure);
            T.True(rig.Acq.TotalCounts > 4000, "the final reading was taken: " + rig.Acq.TotalCounts.ToString(CultureInfo.InvariantCulture));
            T.True(spent <= budget, channels.ToString(CultureInfo.InvariantCulture) + " channels at a " + gapMs.ToString(CultureInfo.InvariantCulture)
                + " ms gap: the Stop took " + spent.ToString(CultureInfo.InvariantCulture) + " ms of the line, the budget is "
                + budget.ToString(CultureInfo.InvariantCulture) + " ms");
        }

        public static void TestStopBudgetCoversASlowLineAt1024Channels()
        {
            StopBehindASlowForeignJob(1024);
        }

        public static void TestStopBudgetCoversASlowLineAt8192Channels()
        {
            StopBehindASlowForeignJob(8192);
        }

        // A318 (5): a given-up acquisition (Abort - the controller's budget ran out, or the
        // program is closing) takes its own final reading off the line. Left there, the dead
        // read went on page by page, and being urgent it could not even be parked: the next
        // document's Stop waited for all of it. Here the dead read is held on the wire at its
        // header; after Abort not one page of it may follow.
        public static void TestAbortTakesItsReadOffTheLine()
        {
            Rig rig = Make(null, 0, 0, 1024, 600000, 20);
            FakeBlock other = new FakeBlock();
            rig.Fake.Blocks[1] = other;
            AmplitudaSerialAcquisition second = new AmplitudaSerialAcquisition(rig.Line, 1, 1024, 600000, new int[1024], 0, 0);
            rig.Acq.Start();
            second.Start();
            T.True(Until(rig.Fake, () => rig.Acq.Running && second.Running, rig.Acq, second), "both running");
            rig.Fake.Advance(5000);
            rig.Fake.ArmHold(0, AmplitudaSerialProtocol.CommandHeader);
            rig.Acq.RequestStop();
            T.True(T.Wait(() => { rig.Acq.Pump(); return rig.Fake.Held.WaitOne(0); }, 10000), "the Stop's own read is on the wire");
            int wireAtAbort = rig.Fake.WireSnapshot().Length;
            rig.Acq.Abort();
            second.RequestStop();
            rig.Fake.ReleaseHold();
            T.True(Until(rig.Fake, () => second.Finished, second), "the other document's Stop finished");
            rig.Line.Release();
            string[] wire = rig.Fake.WireSnapshot();
            int deadPages = Exchanges(wire, wireAtAbort, "A00", "C82") + Exchanges(wire, wireAtAbort, "A00", "C83");
            T.Eq(0, deadPages, "pages of the aborted read after Abort");
            T.True(Exchanges(wire, wireAtAbort, "A00", "C15") >= 1, "the aborted block was still told to stop");
            T.True(second.Failure == AmplitudaSerialFailure.None && second.TotalCounts > 4000,
                "the other Stop kept its reading: " + second.Failure + ", " + second.TotalCounts.ToString(CultureInfo.InvariantCulture));
        }

        // A318, minor: a Stop before the block has ever answered (or before it was verified
        // empty) has nothing to read - nothing in the block belongs to the document yet. It used
        // to wait out the stop trouble window (about 7.5 s) and then show "Stop incomplete: the
        // final reading was not taken", which reads as lost data where there was none.
        public static void TestStopBeforeTheFirstAnswerEndsAtOnce()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Silent = true;
            rig.Acq.Start();
            // Not pumped meanwhile: the first status request is on the line, nothing else is sent
            // (pumping a silent block on virtual time races the 20 s start deadline).
            T.True(T.Wait(() => Count(rig.Fake.WireSnapshot(), "C13") >= 1, 5000), "the block is asked for its status");
            int stopsBefore = Count(rig.Fake.WireSnapshot(), "C15");
            rig.Acq.RequestStop();
            T.True(rig.Acq.Finished, "finished at once, no final reading to wait for");
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            T.True(T.Wait(() => Count(rig.Fake.WireSnapshot(), "C15") > stopsBefore, 5000), "a stop command still went out");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "nothing was lost, so no failure: " + rig.Acq.Failure);
            T.Eq(0, rig.Acq.TotalCounts, "no counts");
        }

        // The same once the block has answered but is not verified empty yet: Stop right after
        // Start must not read the block (its content is not ours) - it ends at once.
        public static void TestStopBeforeTheBlockIsClearedReadsNothing()
        {
            Rig rig = Make(null, 0, 0);
            rig.Block.Channels[252] = 4000;     // foreign counts that must not leak in
            rig.Acq.Start();
            T.True(rig.Until(() => Array.IndexOf(rig.Fake.WireSnapshot(), "C15") >= 0), "the start sequence began");
            int headersBefore = Count(rig.Fake.WireSnapshot(), "C81");
            rig.Acq.RequestStop();
            T.True(rig.Until(() => rig.Acq.Finished), "finished");
            rig.Line.Release();
            T.True(rig.Acq.Failure == AmplitudaSerialFailure.None, "failure: " + rig.Acq.Failure);
            T.Eq(0, rig.Acq.TotalCounts, "nothing taken from an unverified block");
            T.Eq(headersBefore, Count(rig.Fake.WireSnapshot(), "C81"), "headers read after a Stop before the block was verified empty");
        }
    }
}
