using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    public enum AmplitudaSerialFailure
    {
        None,
        NoAnswer,           // the block did not answer within 20 s of Start
        ConnectionLost,     // no valid answer for more than 30 s (5 s, and two failed attempts, while stopping)
        ClearFailed,        // the block does not come back empty after a clear
        StartFailed,        // the block does not start acquiring
        ChannelsExceedBlock,// the configured channel count is longer than the block's own spectrum
        BlockReset          // spectrum protection: the block came back with empty memory
    }

    public enum AmplitudaSerialWarning
    {
        // Raised ONLY when a reset is seen at Stop (RequestStop's Halt continuation): the
        // measurement still finishes normally there (nothing left to protect), so the user only
        // needs telling, not the harder failure below. Every other place a reset is noticed
        // (a plain poll, or mid-pour) raises AmplitudaSerialFailure.BlockReset instead - no
        // warning - since spectrum protection stops the measurement on those paths.
        BlockReset,
        BlockStopped,
        BlockCeiling,
        BlockWontStop
    }

    // The measurement as a state machine over a shared line. No WinForms, no BecqMoni types:
    // the device controller is a thin adapter around this class, and the console tests drive
    // it against a fake block on virtual time.
    //
    // Threading: every public member is called from ONE thread (the UI thread). Line callbacks
    // only put results into the inbox; Pump() drains it. A result belongs to the sequence that
    // asked for it: starting a new sequence bumps the token and orphans what is in flight.
    //
    // Real time = committed seconds + the running segment, measured on the port clock between
    // the completion of the start command and the completion of the stop command. Pours and
    // restarts are therefore outside both real and live time.
    //
    // Spectrum protection (hardware fact, confirmed on a hardware spike): after its power
    // blinks, the block comes back within a few seconds with EMPTY memory and then spends about
    // a minute re-tuning its PMT high voltage by an internal LED. It keeps counting meanwhile,
    // but with a drifting gain, so whatever lands in the spectrum during that minute has a
    // distorted energy scale. The status byte gives no sign of the tuning (only bit 3 flickers,
    // regardless). We cannot know for sure that power was lost - only that the block lost its
    // memory - so a detected reset STOPS the measurement (AmplitudaSerialFailure.BlockReset)
    // instead of the old behaviour of folding the last good reading and silently restarting
    // straight into that distorted minute. AmplitudaSerialDeviceController additionally refuses
    // a new Start for a configurable settle time after a detected reset.
    public sealed class AmplitudaSerialAcquisition
    {
        const int StartAnswerMs = 20000;        // margin for a block switched on right at Start
        // The block answers 2-5 s after power-up; the 20 s start wait is a margin for a block
        // that happens to be switched on at that exact moment, well above the plain answer time.
        const int TroubleMs = 30000;
        // A short power glitch (a few seconds of outage) plus the block's own 2-5 s answer time
        // afterwards must fit inside this window, so that it ends as a detected block reset
        // (spectrum protection) rather than being given up on as a lost connection.
        const int CommandAttempts = 3;
        // Once the user has pressed Stop the UI thread is waiting for us: a dead block (a pulled
        // cable) must give up in about this long, not after the 30 s of a measurement in progress.
        const int StopTroubleMs = 5000;

        sealed class InboxItem
        {
            public int Token;
            public AmplitudaSerialRequestKind Kind;
            public Action<AmplitudaSerialReading> Handler;
            public AmplitudaSerialReading Reading;
            public AmplitudaSerialRequest Request;
        }

        readonly AmplitudaSerialLine line;
        readonly byte address;
        readonly int channels;
        readonly int pollMs;
        readonly AmplitudaSerialSession session;
        readonly object inboxLock = new object();
        readonly Queue<InboxItem> inbox = new Queue<InboxItem>();
        readonly List<AmplitudaSerialWarning> warnings = new List<AmplitudaSerialWarning>();
        // Requests sent and not yet seen back in Pump(); UI thread only. A new sequence takes
        // them off the line (NewSequence).
        readonly List<AmplitudaSerialRequest> outstanding = new List<AmplitudaSerialRequest>();

        int token;
        bool started;
        bool stopping;
        int stopFailures;       // failed exchanges of the stop sequence since the last success
        bool running;
        bool finished;
        bool dirty;
        bool readOutstanding;
        AmplitudaSerialFailure failure = AmplitudaSerialFailure.None;
        int suggestedChannels;
        bool blockResetDetected;
        long startDeadlineMs;
        long troubleSinceMs = -1;
        long lastReadDoneMs;

        double realCommitted;
        bool segmentActive;
        long segmentStartMs;
        double realAtSessionStart;
        double liveAtSessionStart;
        double realAtLastGood;
        double liveAtLastGood;

        public AmplitudaSerialAcquisition(AmplitudaSerialLine line, byte address, int channels, int pollMs,
            int[] documentSpectrum, double documentLiveSeconds, double documentRealSeconds)
        {
            this.line = line;
            this.address = address;
            this.channels = channels;
            this.pollMs = pollMs;
            session = new AmplitudaSerialSession(documentSpectrum, documentLiveSeconds);
            realCommitted = documentRealSeconds;
            realAtSessionStart = documentRealSeconds;
            liveAtSessionStart = documentLiveSeconds;
            realAtLastGood = documentRealSeconds;
            liveAtLastGood = documentLiveSeconds;
        }

        public AmplitudaSerialSession Session { get { return session; } }
        public bool Finished { get { return finished; } }
        public bool Running { get { return running; } }
        public AmplitudaSerialFailure Failure { get { return failure; } }
        // Set only together with Failure == ChannelsExceedBlock: the channel count the block's
        // own spectrum actually supports (a multiple of ChannelsPerPage). 0 otherwise.
        public int SuggestedChannels { get { return suggestedChannels; } }
        // True once this acquisition has ever seen the block come back with empty memory
        // (spectrum protection tripped, on any of the three paths: a plain poll, mid-pour, or at
        // Stop). The device controller reads this once the session ends to arm the settle guard.
        public bool BlockResetDetected { get { return blockResetDetected; } }
        // Never above real time (maintainer's re-check of #35, 13.10.2026, finding 4): the block
        // clock correction adds the EXPECTED half second per segment, and on a short, lightly
        // loaded segment that can overshoot the real time measured for it. Live > real is not
        // a state a spectrum may be in - BecqMoni then reads it as "no live time at all" (the FSA
        // pile-up factor 2.0 instead of 1.0, a negative dead time on screen). The internal
        // bookkeeping (liveAtLastGood, EstimateReal) keeps the uncapped session value.
        public double LiveSeconds { get { return Math.Min(session.LiveSeconds, RealSeconds); } }
        public long TotalCounts { get { return session.TotalCounts; } }

        public double RealSeconds
        {
            get
            {
                return segmentActive ? realCommitted + (line.NowMs - segmentStartMs) / 1000.0 : realCommitted;
            }
        }

        public void Compose(int[] target)
        {
            session.Compose(target);
        }

        public bool TakeDirty()
        {
            bool was = dirty;
            dirty = false;
            return was;
        }

        public List<AmplitudaSerialWarning> TakeWarnings()
        {
            List<AmplitudaSerialWarning> taken = new List<AmplitudaSerialWarning>(warnings);
            warnings.Clear();
            return taken;
        }

        public void Start()
        {
            if (started)
            {
                return;
            }
            started = true;
            NewSequence();
            startDeadlineMs = line.NowMs + StartAnswerMs;
            AwaitAnswer();
        }

        public void Pump()
        {
            // Only what has arrived by now: a result that comes back while this call runs (the
            // handlers below send the next request at once) waits for the next Pump(). Otherwise
            // a line that answers faster than the UI thread checks the inbox - the fake block of
            // the console tests on virtual time, where an exchange costs no real time at all -
            // keeps one call going round, and the 20 s start deadline ran out inside a single
            // Pump() (TestLateAnswerAtStart, flaky under load, 14.10.2026). On a real port an
            // exchange takes tens of milliseconds, so nothing changes there.
            int arrived;
            lock (inboxLock)
            {
                arrived = inbox.Count;
            }
            for (int taken = 0; taken < arrived; taken++)
            {
                InboxItem item;
                lock (inboxLock)
                {
                    item = inbox.Dequeue();
                }
                outstanding.Remove(item.Request);
                if (item.Token != token || finished)
                {
                    continue;
                }
                // Whitelist, not a blacklist: only a Status or a Read reply that actually came
                // back can clear the trouble clock. A control command has no answer (Ok only
                // means the byte was written, never that the block is alive), and a Probe's
                // status is unconfirmed (a single, possibly garbled byte) - neither is evidence
                // the block is alive, and treating either as evidence would let a recovery loop's
                // own "clear -> Ok" step reset the watchdog every round, so the 30 s give-up would
                // never be reached.
                if (item.Reading.Ok &&
                    (item.Kind == AmplitudaSerialRequestKind.Status || item.Kind == AmplitudaSerialRequestKind.Read))
                {
                    troubleSinceMs = -1;
                    stopFailures = 0;
                }
                item.Handler(item.Reading);
            }
            if (running && !finished && !readOutstanding && line.NowMs - lastReadDoneMs >= pollMs)
            {
                Poll();
            }
        }

        // Stop the block, take the last reading, then Finished. Keep pumping until then.
        public void RequestStop()
        {
            if (finished)
            {
                return;
            }
            if (!started)
            {
                finished = true;
                return;
            }
            if (!session.Armed)
            {
                // Nothing in the block belongs to the document (A318, re-check of #35 13.10.2026):
                // the block has not answered yet, or has not been verified empty since Start, a
                // pour or a clear - whatever was counted before that is already in the base, and
                // a reading taken now would be ignored anyway (Session.Apply when not armed).
                // So there is no final reading to wait for: stop the block (best effort, the
                // command needs no answer) and finish at once. Before, a Stop while waiting for
                // the first answer read a silent block until the stop trouble window ran out
                // (about 7.5 s) and ended in ConnectionLost - shown as "Stop incomplete: the final
                // reading was not taken", where nothing had been taken or lost. Real time needs
                // no roll-back: with the session disarmed it already stands at the last good
                // reading (realCommitted == realAtLastGood on every path that disarms it).
                NewSequence();
                finished = true;
                line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = address, Command = AmplitudaSerialProtocol.CommandStop });
                return;
            }
            stopping = true;
            NewSequence();
            Halt(0, delegate (AmplitudaSerialReading last)
            {
                AmplitudaSerialVerdict verdict = TakeFinal(last);
                if (verdict == AmplitudaSerialVerdict.BlockReset)
                {
                    // The block lost its memory sometime during the stop sequence. Unlike a
                    // reset noticed while still measuring, a user-requested Stop has no
                    // "measurement to protect" left to interrupt: TakeFinal already kept the
                    // last good reading (BlockReset is never taken), so the document ends up
                    // exactly as any other normal Stop would leave it. Only a warning is raised
                    // here - this is the ONLY path that ever adds AmplitudaSerialWarning.BlockReset
                    // - both to tell the user and so the device controller can arm the same
                    // settle guard as the Fail path below.
                    //
                    // Real time still needs the same roll-back the Fail paths do (M1, add-ons
                    // review): Halt's own CommitSegment already advanced realCommitted up to the
                    // stop command's completion, but that stretch carries no counts and no live
                    // time (the block's memory was already wiped) - left uncorrected, it would be
                    // a dead-time gap in the document. realAtLastGood is always meaningful here
                    // (it starts at the document's real time and is only ever advanced by
                    // successful polls), including when the reset is seen before any poll
                    // succeeded, where it correctly falls back to the document's own base.
                    blockResetDetected = true;
                    realCommitted = realAtLastGood;
                    segmentActive = false;
                    warnings.Add(AmplitudaSerialWarning.BlockReset);
                }
                else if (verdict != AmplitudaSerialVerdict.Ignored)
                {
                    // The last segment ends here, not in a pour: fold it so it gets the same
                    // block-clock correction as every poured segment.
                    session.Pour();
                }
                finished = true;
            }, true);
        }

        // Clear while measuring: the document starts from zero, the block is restarted.
        public void ClearAll()
        {
            session.ClearAll();
            realCommitted = 0;
            segmentActive = false;
            realAtSessionStart = 0;
            liveAtSessionStart = 0;
            realAtLastGood = 0;
            liveAtLastGood = 0;
            dirty = false;
            if (!started || finished)
            {
                return;
            }
            NewSequence();
            StopThenRestart(0);
        }

        // Give up now: a best-effort stop command, no waiting, no final reading. NewSequence also
        // takes the Stop's own urgent read off the line (A318, finding 5), so the next Stop on
        // this port does not wait behind a reading nobody will take.
        public void Abort()
        {
            if (finished)
            {
                return;
            }
            // No final reading: counts and live time stay at the last good poll, and so must real time.
            RollBackToLastGood();
            NewSequence();
            finished = true;
            if (started)
            {
                line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = address, Command = AmplitudaSerialProtocol.CommandStop });
            }
        }

        void NewSequence()
        {
            token++;
            running = false;
            readOutstanding = false;
            // What the previous sequence still has on the line would only be dropped by token
            // when it comes back - take it off the line instead (A318, finding 5): an aborted
            // Stop's urgent read, the poll orphaned by a Stop or a clear, a start command not yet
            // sent. Every caller follows with its own stop or clear (or enqueues a stop directly,
            // which is never tracked here), so the block is never left without one.
            foreach (AmplitudaSerialRequest request in outstanding)
            {
                line.Cancel(request);
            }
            outstanding.Clear();
        }

        void Send(AmplitudaSerialRequestKind kind, byte command, Action<AmplitudaSerialReading> handler, bool urgent = false)
        {
            int sentToken = token;
            AmplitudaSerialRequest request = new AmplitudaSerialRequest();
            request.Kind = kind;
            request.Address = address;
            request.Command = command;
            request.Channels = channels;
            request.Urgent = urgent;
            request.Completed = delegate (AmplitudaSerialReading reading)
            {
                lock (inboxLock)
                {
                    inbox.Enqueue(new InboxItem { Token = sentToken, Kind = kind, Handler = handler, Reading = reading, Request = request });
                }
            };
            outstanding.Add(request);
            line.Enqueue(request);
        }

        // False = keep retrying the step; true = the acquisition has failed and is finished.
        bool Troubled()
        {
            long now = line.NowMs;
            if (troubleSinceMs < 0)
            {
                troubleSinceMs = now;
            }
            if (stopping)
            {
                // The window counts from the first failed exchange, like the normal rule, and a
                // Stop is never given up on before at least two attempts have failed: one noisy
                // page (three failed tries, ~2 s) must still get its retry.
                stopFailures++;
                if (now - troubleSinceMs <= StopTroubleMs || stopFailures < 2)
                {
                    return false;
                }
            }
            else if (now - troubleSinceMs <= TroubleMs)
            {
                return false;
            }
            Fail(AmplitudaSerialFailure.ConnectionLost);
            return true;
        }

        void Fail(AmplitudaSerialFailure reason)
        {
            // What came after the last good reading cannot be trusted.
            RollBackToLastGood();
            failure = reason;
            NewSequence();
            finished = true;
            line.Enqueue(new AmplitudaSerialRequest { Kind = AmplitudaSerialRequestKind.Control, Address = address, Command = AmplitudaSerialProtocol.CommandStop });
        }

        // The acquisition ends without the reading that would close the running piece (Abort, or
        // Fail on any path): counts and live time stay at the last good reading, so real time is
        // set back to it as well - otherwise the stretch from that reading to the end would show
        // up in the document as dead time (maintainer's re-check of #35, 13.10.2026, finding 1).
        // This does NOT depend on segmentActive: on a Stop or a pour, Halt's CommitSegment has
        // already advanced realCommitted up to the stop command before the final read, and it is
        // exactly that read which went missing. Every place that closes a piece WITH a reading
        // (a pour's fold, a block that stopped itself, a clear) moves realAtLastGood along with
        // realCommitted, so on those paths this changes nothing.
        void RollBackToLastGood()
        {
            realCommitted = realAtLastGood;
            segmentActive = false;
        }

        void CommitSegment(long atMs)
        {
            if (segmentActive)
            {
                realCommitted += Math.Max(0, atMs - segmentStartMs) / 1000.0;
                segmentActive = false;
            }
        }

        void AwaitAnswer()
        {
            Send(AmplitudaSerialRequestKind.Status, 0, delegate (AmplitudaSerialReading reading)
            {
                if (reading.Ok)
                {
                    StopThenRestart(0);
                }
                else if (line.NowMs >= startDeadlineMs)
                {
                    Fail(AmplitudaSerialFailure.NoAnswer);
                }
                else
                {
                    AwaitAnswer();
                }
            });
        }

        void StopThenRestart(int attempt)
        {
            Send(AmplitudaSerialRequestKind.Control, AmplitudaSerialProtocol.CommandStop, delegate (AmplitudaSerialReading reading)
            {
                if (!reading.Ok)
                {
                    if (!Troubled())
                    {
                        StopThenRestart(attempt);
                    }
                    return;
                }
                CommitSegment(reading.CompletedMs);
                Restart(attempt);
            });
        }

        // Clear, read back, require an empty stopped block, then launch.
        void Restart(int attempt)
        {
            Send(AmplitudaSerialRequestKind.Control, AmplitudaSerialProtocol.CommandClear, delegate (AmplitudaSerialReading cleared)
            {
                if (!cleared.Ok)
                {
                    if (!Troubled())
                    {
                        Restart(attempt);
                    }
                    return;
                }
                Send(AmplitudaSerialRequestKind.Read, 0, delegate (AmplitudaSerialReading reading)
                {
                    if (!reading.Ok)
                    {
                        if (!Troubled())
                        {
                            Restart(attempt);
                        }
                        return;
                    }
                    long sum = 0;
                    for (int i = 0; i < reading.Channels.Length; i++)
                    {
                        sum += reading.Channels[i];
                    }
                    // Hardware fact (BDEG-3-2 and relatives): a block whose own spectrum is
                    // shorter than the configured channel count still answers page requests
                    // beyond its spectrum with a correctly checksummed reply, but the payload is
                    // foreign memory (other internal buffers) that CommandClear never touches.
                    // Tell that apart from a genuine clear failure by looking at the reading page
                    // by page: if a leading run of pages reads back genuinely empty (the block's
                    // own spectrum really was cleared) followed by at least one nonzero page, no
                    // amount of retrying the clear will ever zero that tail - fail immediately
                    // with the channel count the block's spectrum actually supports, instead of
                    // three pointless retries ending in the generic "does not obey clear".
                    if (!reading.Running)
                    {
                        int pageCount = AmplitudaSerialProtocol.PageCount(channels);
                        int leadingZeroPages = 0;
                        while (leadingZeroPages < pageCount && PageSum(reading.Channels, leadingZeroPages) == 0)
                        {
                            leadingZeroPages++;
                        }
                        if (leadingZeroPages >= 1 && leadingZeroPages < pageCount)
                        {
                            bool tailAllZero = true;
                            for (int page = leadingZeroPages; page < pageCount && tailAllZero; page++)
                            {
                                if (PageSum(reading.Channels, page) != 0)
                                {
                                    tailAllZero = false;
                                }
                            }
                            if (!tailAllZero)
                            {
                                suggestedChannels = leadingZeroPages * AmplitudaSerialProtocol.ChannelsPerPage;
                                Fail(AmplitudaSerialFailure.ChannelsExceedBlock);
                                return;
                            }
                        }
                    }
                    if (sum != 0 || reading.Running)
                    {
                        if (attempt + 1 < CommandAttempts)
                        {
                            StopThenRestart(attempt + 1);
                        }
                        else
                        {
                            Fail(AmplitudaSerialFailure.ClearFailed);
                        }
                        return;
                    }
                    session.SetZero(reading.LiveSeconds);
                    Launch(0);
                });
            });
        }

        // Sum of one ChannelsPerPage-wide slice of a full reading.
        static long PageSum(int[] channels, int pageIndex)
        {
            int first = pageIndex * AmplitudaSerialProtocol.ChannelsPerPage;
            long sum = 0;
            for (int i = 0; i < AmplitudaSerialProtocol.ChannelsPerPage; i++)
            {
                sum += channels[first + i];
            }
            return sum;
        }

        void Launch(int attempt)
        {
            Send(AmplitudaSerialRequestKind.Control, AmplitudaSerialProtocol.CommandStart, delegate (AmplitudaSerialReading startedReading)
            {
                if (!startedReading.Ok)
                {
                    if (!Troubled())
                    {
                        Launch(attempt);
                    }
                    return;
                }
                long startedMs = startedReading.CompletedMs;
                Send(AmplitudaSerialRequestKind.Status, 0, delegate (AmplitudaSerialReading status)
                {
                    if (!status.Ok)
                    {
                        if (!Troubled())
                        {
                            Launch(attempt);
                        }
                        return;
                    }
                    if (!status.Running)
                    {
                        if (attempt + 1 < CommandAttempts)
                        {
                            Launch(attempt + 1);
                        }
                        else
                        {
                            Fail(AmplitudaSerialFailure.StartFailed);
                        }
                        return;
                    }
                    segmentStartMs = startedMs;
                    segmentActive = true;
                    lastReadDoneMs = status.CompletedMs;
                    running = true;
                });
            });
        }

        // Stop the block and read it while it stands still.
        // final = the Stop's own halt: only its read is Urgent (a pour's halt rides with the normal polls).
        void Halt(int attempt, Action<AmplitudaSerialReading> halted, bool final)
        {
            Send(AmplitudaSerialRequestKind.Control, AmplitudaSerialProtocol.CommandStop, delegate (AmplitudaSerialReading stopped)
            {
                if (!stopped.Ok)
                {
                    if (!Troubled())
                    {
                        Halt(attempt, halted, final);
                    }
                    return;
                }
                CommitSegment(stopped.CompletedMs);
                Send(AmplitudaSerialRequestKind.Read, 0, delegate (AmplitudaSerialReading reading)
                {
                    if (!reading.Ok)
                    {
                        if (!Troubled())
                        {
                            Halt(attempt, halted, final);
                        }
                        return;
                    }
                    if (reading.Running && attempt + 1 < CommandAttempts)
                    {
                        Halt(attempt + 1, halted, final);
                        return;
                    }
                    if (reading.Running)
                    {
                        // The block ignores the stop command; its reading is still taken, the next Start clears it.
                        warnings.Add(AmplitudaSerialWarning.BlockWontStop);
                    }
                    halted(reading);
                }, final);      // the Stop's final reading must not wait behind other documents' polls
            });
        }

        AmplitudaSerialVerdict TakeFinal(AmplitudaSerialReading reading)
        {
            AmplitudaSerialVerdict verdict = session.Apply(reading.Channels, reading.LiveSeconds, reading.Running);
            if (verdict != AmplitudaSerialVerdict.Ignored && verdict != AmplitudaSerialVerdict.BlockReset)
            {
                dirty = true;
            }
            return verdict;
        }

        void Poll()
        {
            readOutstanding = true;
            long issuedMs = line.NowMs;
            Send(AmplitudaSerialRequestKind.Read, 0, delegate (AmplitudaSerialReading reading)
            {
                readOutstanding = false;
                lastReadDoneMs = line.NowMs;
                if (!reading.Ok)
                {
                    Troubled();
                    return;
                }
                // A Read of a still-running block takes real time to transfer (header + every page):
                // the reported header/page data is a live snapshot taken partway through that transfer,
                // not at its completion. reading.CompletedMs alone would systematically overstate the real
                // time behind the reported counts; the midpoint of issue and completion is a much closer
                // stand-in for "when this reading's numbers were actually true" without needing a per-page
                // timestamp (which the wire protocol/Reading type does not provide).
                double sampledAtMs = (issuedMs + reading.CompletedMs) / 2.0;
                double realAtReading = segmentActive ? realCommitted + (sampledAtMs - segmentStartMs) / 1000.0 : realCommitted;
                AmplitudaSerialVerdict verdict = session.Apply(reading.Channels, reading.LiveSeconds, reading.Running);
                switch (verdict)
                {
                    case AmplitudaSerialVerdict.Ok:
                        dirty = true;
                        realAtLastGood = realAtReading;
                        liveAtLastGood = session.LiveSeconds;
                        break;

                    case AmplitudaSerialVerdict.PourNeeded:
                        dirty = true;
                        realAtLastGood = realAtReading;
                        liveAtLastGood = session.LiveSeconds;
                        NewSequence();
                        Halt(0, delegate (AmplitudaSerialReading final)
                        {
                            AmplitudaSerialVerdict finalVerdict = TakeFinal(final);
                            if (finalVerdict == AmplitudaSerialVerdict.BlockReset)
                            {
                                // The block lost its memory between this poll and the halt read
                                // that was about to fold it (a power blink right as the pour was
                                // starting): stop exactly like a reset noticed on a plain poll -
                                // roll real time back to the last good reading (discarding
                                // whatever Halt's own CommitSegment already added for the
                                // interval up to its stop command, which cannot be trusted once
                                // the block has reset), keep what was already folded, and do NOT
                                // Restart into the high-voltage re-tuning window.
                                blockResetDetected = true;
                                realCommitted = realAtLastGood;
                                segmentActive = false;
                                session.Pour();
                                Fail(AmplitudaSerialFailure.BlockReset);
                                return;
                            }
                            realAtLastGood = realCommitted;
                            liveAtLastGood = session.LiveSeconds;
                            session.Pour();
                            Restart(0);
                        }, false);
                        break;

                    case AmplitudaSerialVerdict.BlockReset:
                        // Spectrum protection (hardware fact): a block whose power blinked comes
                        // back with EMPTY memory, then spends about a minute re-tuning its high
                        // voltage by its own LED with a drifting gain - the status byte gives no
                        // sign of this (only bit 3 flickers, always). Whatever it counts during
                        // that minute would land in the spectrum with a distorted energy scale,
                        // so instead of silently restarting into it (the old behaviour), the
                        // measurement stops here: the last good reading is kept (rolled back
                        // exactly like the old restart path did), and no warning is raised on
                        // this path - the failure itself tells the device controller, which
                        // reports it and arms the settle guard. Start must be pressed again.
                        blockResetDetected = true;
                        realCommitted = realAtLastGood;     // the piece after the last good reading is lost
                        segmentActive = false;
                        session.Pour();
                        Fail(AmplitudaSerialFailure.BlockReset);
                        break;

                    case AmplitudaSerialVerdict.BlockStopped:
                    case AmplitudaSerialVerdict.BlockCeiling:
                        warnings.Add(verdict == AmplitudaSerialVerdict.BlockCeiling ? AmplitudaSerialWarning.BlockCeiling : AmplitudaSerialWarning.BlockStopped);
                        dirty = true;
                        // Nobody saw when the block stopped: real time of the last piece is estimated from its live time.
                        realCommitted = AmplitudaSerialSession.EstimateReal(realAtLastGood, liveAtLastGood, session.LiveSeconds,
                            realAtSessionStart, liveAtSessionStart);
                        segmentActive = false;
                        realAtLastGood = realCommitted;
                        liveAtLastGood = session.LiveSeconds;
                        session.Pour();
                        NewSequence();
                        Restart(0);
                        break;
                }
            });
        }
    }

    // Messages of the AmplitudaSerial devices to the person at the screen: at most ONE box at a
    // time, and never inside the code that raised the message (A318, maintainer's re-check of
    // #35 13.10.2026, finding 3).
    //
    // Why. The pump timer clears its re-entry flag before it reports, so that Pump() keeps
    // running while a modal box is open (a restart after a pour moves only through Pump(); with
    // the flag still set, the measurement stood still until the box was closed). The price was
    // a box inside a box: a warning that repeats - the block stopping itself every cycle, the
    // ceiling on an overloaded line - opened a new nested AppUi.Report every 5-8 s, and a night
    // of it piles hundreds of nested message loops on the UI thread's stack. And the synchronous
    // Stop showed its "Stop incomplete" box before it returned, so a Stop by preset time saved
    // the document only once that box was closed.
    //
    // So a message is never shown where it arises: Add() queues it and posts ONE show to the UI
    // message loop; while that box is open, new messages only queue (a text already waiting is
    // not queued twice) and are shown together, in one box, once it is closed. The pump timer
    // keeps ticking inside the box's modal loop exactly as before - nothing here waits for the
    // box. Posting alone would not do: a modal box's own loop dispatches posted messages too, so
    // a second posted show would still open inside the first; the gate is what prevents that.
    //
    // While the application is closing nothing is shown at all (`closing`, the device controller
    // passes MainForm.ApplicationClosing; review of 13.10.2026, item 4): every message here is for
    // the person's information only - the measurement has already ended or goes on - and a modal
    // box in the middle of the close holds it up, and a Windows shutdown with it, until somebody
    // presses OK; nothing can be done about it at that moment anyway. The same rule as
    // MainForm.SaveLayoutXml and AmplitudaUsbDeviceController.ReportLoss. It is checked both when
    // a message arrives and when the box is about to open: a show posted just before the close
    // began would otherwise still open in its middle.
    //
    // No WinForms here, so the console tests drive it with a fake message loop: the device
    // controller passes the post (MainForm.originalContext) and the show (AppUi.Report). It lives
    // in this file because the test runner compiles exactly these dependency-free sources.
    public sealed class AmplitudaSerialNotices
    {
        const string Separator = "\r\n\r\n";

        readonly object sync = new object();
        readonly List<string> pending = new List<string>();
        readonly Action<Action> post;
        readonly Action<string> show;
        readonly Func<bool> closing;
        bool scheduled;     // a show is posted or on screen: Add() only queues

        public AmplitudaSerialNotices(Action<Action> post, Action<string> show, Func<bool> closing)
        {
            this.post = post;
            this.show = show;
            this.closing = closing;
        }

        public void Add(string text)
        {
            if (string.IsNullOrEmpty(text) || closing())
            {
                return;
            }
            bool schedule;
            lock (sync)
            {
                if (!pending.Contains(text))
                {
                    pending.Add(text);
                }
                schedule = !scheduled;
                scheduled = true;
            }
            if (schedule)
            {
                post(ShowPending);
            }
        }

        void ShowPending()
        {
            for (;;)
            {
                string text;
                lock (sync)
                {
                    if (pending.Count == 0)
                    {
                        scheduled = false;
                        return;
                    }
                    text = string.Join(Separator, pending);
                    pending.Clear();
                }
                try
                {
                    if (!closing())
                    {
                        show(text);     // modal: what arrives meanwhile is queued, shown on the next turn
                    }
                }
                catch (Exception)
                {
                    // A box that failed must not shut the gate for good: the loop goes on.
                }
            }
        }
    }
}
