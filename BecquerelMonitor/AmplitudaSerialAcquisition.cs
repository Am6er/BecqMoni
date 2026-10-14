using System;
using System.Collections.Generic;
using System.Threading;

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
    // One moment for counts, live and real time while measuring (P280, decision of Amber
    // 14.10.2026 "Поправка в коде, проверка автору"). A Read of a running block takes seconds -
    // status, header, then page after page - and comes once per poll period, while the PC clock
    // runs on. What the document shows while measuring is therefore the state AT THE LAST GOOD
    // READING: real time at the moment its counts were read (the pages, weighted by their count
    // rates - CountsMomentMs), live time brought from its header to that same moment
    // (AlignOpenLive) and credited with the block clock's half second like a closed segment.
    // Before, real time ran to "now" and the live time stayed at the header: the dead time on
    // screen was too high by up to (1 - d)(Δ + 2 t_read + 0.5)/T, the rates too high by the
    // pages' lag over the header. RealSecondsNow is the old clock, for the preset stop only
    // (ElapsedTicks). Once the acquisition has finished both are the same committed real time.
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
        }

        readonly AmplitudaSerialLine line;
        readonly byte address;
        readonly int channels;
        readonly int pollMs;
        readonly AmplitudaSerialSession session;
        readonly object inboxLock = new object();
        readonly Queue<InboxItem> inbox = new Queue<InboxItem>();
        readonly List<AmplitudaSerialWarning> warnings = new List<AmplitudaSerialWarning>();

        int token;
        bool started;
        bool stopping;
        int stopFailures;       // failed exchanges of the stop sequence since the last success
        bool running;
        bool finished;
        bool dirty;
        bool readOutstanding;
        // The Read request in flight or queued on the line. Set on the UI thread in Send, cleared
        // by its Completed callback on the line thread, hence the interlocked clear.
        AmplitudaSerialRequest pendingRead;
        // A Status or Read reply has ever come back: the block is known to be alive and the
        // measurement has begun (the same whitelist that resets the trouble clock).
        bool answered;
        AmplitudaSerialFailure failure = AmplitudaSerialFailure.None;
        int suggestedChannels;
        bool blockResetDetected;
        long startDeadlineMs;
        long troubleSinceMs = -1;
        long lastReadDoneMs;

        double realCommitted;
        bool segmentActive;
        long segmentStartMs;
        // Port clock at the start command byte of the running segment: the block's clock starts
        // with it (AmplitudaSerialSession). The anchor of the real time shown while measuring;
        // segmentStartMs (its completion, a settle pause later) stays the anchor of the committed
        // time, so a closed segment is what it always was.
        long segmentAnchorMs;
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

        // Real time of the data in hand (P280): while measuring, the moment the counts of the last
        // good reading were read (realAtLastGood - every path that changes the data moves it,
        // and every roll-back returns to it); once finished, the committed real time, exactly as
        // before. Same moment as TotalCounts and LiveSeconds.
        public double RealSeconds
        {
            get
            {
                return finished ? realCommitted : realAtLastGood;
            }
        }

        // The PC clock: committed seconds plus the running segment up to now. What RealSeconds
        // was before P280 - kept for the preset stop (ElapsedTicks) and the hardware smoke run.
        public double RealSecondsNow
        {
            get
            {
                return segmentActive ? realCommitted + (line.NowMs - segmentStartMs) / 1000.0 : realCommitted;
            }
        }

        // What the device controller writes as the measurement's elapsed time
        // (ResultDataStatus.ElapsedTime, in 100-ns ticks; MeasurementController.OnTimer copies it
        // to the spectrum's real time and stops the measurement once it reaches PresetTime).
        // The real time of the data in hand - except once the CLOCK has reached the preset:
        // then the clock, so the preset stop fires on the same tick it always did (the data's
        // moment lags the clock by up to a poll period and a read; shown alone, it would stretch
        // every measurement by that much). The stop then writes the final values at once.
        public long ElapsedTicks(int presetSeconds)
        {
            long clock = (long)(RealSecondsNow * 1.0E+07);
            if (TimeSpan.FromTicks(clock).TotalSeconds >= presetSeconds)
            {
                return clock;
            }
            return (long)(RealSeconds * 1.0E+07);
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
                    answered = true;
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
            if (!answered || !session.Armed)
            {
                // Started but the block has not answered once (switched on late, or not at all):
                // no poll has ever happened, there is nothing to read and nothing to lose. Stop like
                // an Abort, but without a failure - Halt's trouble rule would only end it after
                // ~7.5 s as ConnectionLost and tell the user the Stop was incomplete.
                // The same once it has answered but the session is not armed (P288, kept from A318
                // when merging PR #36): the block has not been verified empty since Start, a pour
                // or a clear, so nothing in it belongs to the document - what was counted before
                // is already in the base, and a final reading would be ignored anyway
                // (Session.Apply when not armed). Without this a Stop in the middle of such a
                // restart read the block for nothing and, if the block fell silent there, ended
                // as an "incomplete" Stop where nothing had been taken or lost. (!answered implies
                // !Armed - the session is armed only after a good read; both are spelled out.)
                // The roll-back changes nothing on the second path: every path that disarms the
                // session leaves realCommitted == realAtLastGood.
                RollBackToLastGood();
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

        // Whatever the old sequence still has queued or in flight on the line is orphaned: a read
        // is withdrawn so that it neither delays the new sequence's own commands and its urgent
        // read nor leaves a dead read in front of the next Stop on the same port. Control and
        // Status requests are never cancelled.
        void NewSequence()
        {
            token++;
            running = false;
            readOutstanding = false;
            AmplitudaSerialRequest orphan = pendingRead;
            if (orphan != null)
            {
                pendingRead = null;
                line.Cancel(orphan);
            }
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
                if (kind == AmplitudaSerialRequestKind.Read)
                {
                    Interlocked.CompareExchange(ref pendingRead, null, request);
                }
                lock (inboxLock)
                {
                    inbox.Enqueue(new InboxItem { Token = sentToken, Kind = kind, Handler = handler, Reading = reading });
                }
            };
            if (kind == AmplitudaSerialRequestKind.Read)
            {
                pendingRead = request;
            }
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
                long sentMs = startedReading.SentMs >= 0 ? startedReading.SentMs : startedMs;
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
                    segmentAnchorMs = sentMs;
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

        // Port-clock moment the reading's total count belongs to (P280). The pages are read one
        // after another, 0.7 s each at 19200 baud, and the channels of page k describe the block
        // when it was asked for them (PageMs[k]); the moments are weighted by each page's count
        // rate - its counts over the time the block had been counting then - which for steady
        // rates is exactly the moment when the true total equals the sum read. A reading without
        // counts: its header moment (only the live time is there to place).
        double CountsMomentMs(AmplitudaSerialReading reading)
        {
            if (reading.HeaderMs < 0 || reading.PageMs == null || reading.Channels == null)
            {
                return reading.CompletedMs;     // not a full Read (never for a good poll)
            }
            double weight = 0.0;
            double moment = 0.0;
            for (int page = 0; page < reading.PageMs.Length; page++)
            {
                long sum = PageSum(reading.Channels, page);
                if (sum <= 0)
                {
                    continue;
                }
                double rate = sum / Math.Max(1.0, reading.PageMs[page] - segmentAnchorMs);
                weight += rate;
                moment += rate * reading.PageMs[page];
            }
            return weight > 0.0 ? moment / weight : reading.HeaderMs;
        }

        // The block reports its live time in the header, read BEFORE the pages; the counts belong
        // to a later moment (CountsMomentMs). The stretch in between gets the live fraction the
        // block itself shows for the running segment up to its header - the open segment's live
        // time (with the half second) over its real time on the PC clock. The fraction is that of
        // the whole segment, so the block's whole seconds blur it by at most 0.5 s / T, and it
        // assumes the dead fraction did not change in the last seconds. The protocol gives no
        // live time nearer the counts (the header is read first, AmplitudaSerialLine.ReadJob).
        void AlignOpenLive(AmplitudaSerialReading reading, double countsAtMs)
        {
            if (!segmentActive || reading.HeaderMs < 0)
            {
                return;
            }
            double headerReal = (reading.HeaderMs - segmentAnchorMs) / 1000.0;
            double stretch = (countsAtMs - reading.HeaderMs) / 1000.0;
            if (headerReal <= 0.0 || stretch <= 0.0)
            {
                return;
            }
            double fraction = Math.Max(0.0, Math.Min(1.0, session.OpenLiveSeconds / headerReal));
            session.AdjustOpenLive(fraction * stretch);
        }

        void Poll()
        {
            readOutstanding = true;
            Send(AmplitudaSerialRequestKind.Read, 0, delegate (AmplitudaSerialReading reading)
            {
                readOutstanding = false;
                lastReadDoneMs = line.NowMs;
                if (!reading.Ok)
                {
                    Troubled();
                    return;
                }
                // A Read of a still-running block takes seconds (status, header, every page), and
                // its numbers are not of one moment: the live time is the block's when the header
                // was asked for, the channels when their pages were. Real time is taken at the
                // moment of the counts (P280; before, the midpoint of the whole read stood in for
                // it, and RealSeconds then ran on to "now" anyway).
                double countsAtMs = CountsMomentMs(reading);
                double realAtReading = segmentActive ? realCommitted + (countsAtMs - segmentAnchorMs) / 1000.0 : realCommitted;
                AmplitudaSerialVerdict verdict = session.Apply(reading.Channels, reading.LiveSeconds, reading.Running);
                switch (verdict)
                {
                    case AmplitudaSerialVerdict.Ok:
                        dirty = true;
                        AlignOpenLive(reading, countsAtMs);
                        realAtLastGood = realAtReading;
                        liveAtLastGood = session.LiveSeconds;
                        break;

                    case AmplitudaSerialVerdict.PourNeeded:
                        dirty = true;
                        AlignOpenLive(reading, countsAtMs);
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
}
