using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace BecquerelMonitor
{
    public enum AmplitudaSerialRequestKind
    {
        Probe,      // one status exchange, no confirmation: "is anybody at this address?" -
                    // queued with the reads, not urgent, so a scan cannot delay a control command
        Status,     // status confirmed by two consecutive reads that agree on bit 0 - urgent
        Control,    // start / stop / clear: no answer - urgent
        Read        // status + header + all pages, every data block verified by checksum
    }

    public sealed class AmplitudaSerialReading
    {
        public bool Ok;
        public string Error;
        public byte Status;
        public bool Running;
        public int LiveSeconds;
        public int[] Channels;
        public long CompletedMs;        // port clock when the request finished
    }

    public sealed class AmplitudaSerialRequest
    {
        public AmplitudaSerialRequestKind Kind;
        public byte Address;
        public byte Command;            // Control only
        public int Channels;            // Read only
        // Read only: goes before the other documents' reads on the line (the final reading of a Stop).
        public bool Urgent;
        // Runs on the LINE thread: it may only hand the result over (a locked queue, a flag).
        // It must never call Acquire / Release or wait for anything.
        public Action<AmplitudaSerialReading> Completed;
        // Set by AmplitudaSerialLine.Cancel, under the line's queue lock.
        internal bool Cancelled;
    }

    // Wire timing of the line, for every wait that has to outlast it (A318, 14.10.2026). The
    // unit is one verified data block: its frame (silence, address, silence, command - two
    // gaps), 1024 bytes, then the checksum frame and byte (two gaps more). At 19200 baud 8N2
    // the 1025 bytes take 587 ms; the rest of BlockTransferMs covers the idle timeouts that end
    // a read and the scheduling of the line thread. Everything grows with the gap, which the
    // settings allow from 10 to 200 ms: a wait fixed in milliseconds is right at one gap only.
    public static class AmplitudaSerialTiming
    {
        const int BlockTransferMs = 700;
        const int UiMarginMs = 2000;

        // One data block (header or page) with its checksum, at the first try.
        public static int ExchangeMs(int gapMs)
        {
            return 4 * gapMs + BlockTransferMs;
        }

        // The longest a read job holds the line in one step: a data block with every try used.
        // A control command queued meanwhile waits this long at most before it goes out.
        public static int WorstStepMs(int gapMs)
        {
            return AmplitudaSerialLine.DataAttempts * ExchangeMs(gapMs) +
                (AmplitudaSerialLine.DataAttempts - 1) * AmplitudaSerialLine.RetryPauseMs;
        }

        // start / stop / clear: one frame and the settle pause after it.
        public static int ControlMs(int gapMs)
        {
            return 2 * gapMs + AmplitudaSerialLine.ControlSettleMs;
        }

        // A confirmed status: up to StatusReads single-byte exchanges.
        public static int StatusMs(int gapMs)
        {
            return AmplitudaSerialLine.StatusReads * (2 * gapMs + 10);
        }

        // The Stop's own halt: the stop command, then status, header and every page.
        public static int HaltMs(int channels, int gapMs)
        {
            return ControlMs(gapMs) + StatusMs(gapMs) + (AmplitudaSerialProtocol.PageCount(channels) + 1) * ExchangeMs(gapMs);
        }

        // How long the device controller waits for a Stop to bring its final reading
        // (AmplitudaSerialDeviceController.StopMeasurement). It covers:
        //   - TWO steps of another document's read job with every try used: the one in flight
        //     when our stop command is queued (a command never cuts a step), and the one between
        //     that command and our urgent read (the read is queued only once the UI thread has
        //     seen the command complete, and the line goes on with the job it is in meanwhile);
        //   - our halt TWICE: a Stop survives one lost page, and then Halt starts over - the stop
        //     command and the whole read again;
        //   - 2 s for the UI thread and the scheduling.
        // 1024 channels: 12.4 s at the default 20 ms gap, 24.7 s at 200 ms; 8192 channels: 34.3 s
        // and 66.7 s. The old budget, 2000 + 1500 per page whatever the gap (8.0 s and 27.5 s),
        // counted one foreign step and cut a healthy Stop at a long gap (A318, finding 2). The UI
        // thread waits this long only while the line keeps moving: a silent block ends the Stop
        // by the acquisition's own stop trouble window, in about 5-10 s.
        public static int StopBudgetMs(int channels, int gapMs)
        {
            return UiMarginMs + 2 * WorstStepMs(gapMs) + 2 * HaltMs(channels, gapMs);
        }

        // How long Release() waits for the line thread before it disposes the port: the step in
        // flight with every try used, a few pending control commands (the stop of each document
        // on the line) and a second for the scheduling. 4.1 s at the default 20 ms gap, 7.7 s at
        // 200 ms. A fixed 3 s was shorter than one step at 200 ms (about 4.4 s), and the stop
        // command queued behind it never went out before the port closed (A318).
        public static int JoinTimeoutMs(int gapMs)
        {
            return WorstStepMs(gapMs) + 4 * ControlMs(gapMs) + 1000;
        }
    }

    // Owner of one serial line. Several blocks hang on one port, and several documents may
    // measure at once, so the port belongs to neither controller: they share a line found by
    // port name. One worker thread runs the exchanges strictly one at a time - the protocol
    // tells an address byte from a command by silence, two writers would break both blocks.
    // Queue unit = one exchange; start/stop/clear and a confirmed status go before pending read
    // steps. A bare probe (address scan) travels with the reads, not urgent, so "Find blocks"
    // cannot delay a pending control command or a running measurement's read.
    public sealed class AmplitudaSerialLine
    {
        public const string ErrorSettingsDiffer = "settings";

        const int FirstByteTimeoutMs = 300;
        const int IdleTimeoutMs = 60;
        // Internal: AmplitudaSerialTiming counts its budgets from them.
        internal const int DataAttempts = 3;
        internal const int StatusReads = 4;
        internal const int RetryPauseMs = 100;
        internal const int ControlSettleMs = 100;
        // The worker only has to finish the exchange it is in and the pending control commands:
        // Release() waits AmplitudaSerialTiming.JoinTimeoutMs(gap) for that.

        static readonly object registryLock = new object();
        static readonly Dictionary<string, AmplitudaSerialLine> registry =
            new Dictionary<string, AmplitudaSerialLine>(StringComparer.OrdinalIgnoreCase);

        // Replaced by tests.
        public static Func<AmplitudaSerialSettings, IAmplitudaSerialPort> PortFactory =
            settings => new AmplitudaSerialComPort(settings);

        readonly AmplitudaSerialSettings settings;
        readonly IAmplitudaSerialPort port;
        readonly Thread worker;
        readonly object queueLock = new object();
        readonly Queue<AmplitudaSerialRequest> urgent = new Queue<AmplitudaSerialRequest>();
        readonly Queue<AmplitudaSerialRequest> reads = new Queue<AmplitudaSerialRequest>();
        // Reads marked Urgent (the final reading of a Stop): served before `reads`, and a normal
        // read job in flight is parked after its current step to let them through.
        readonly Queue<AmplitudaSerialRequest> urgentReads = new Queue<AmplitudaSerialRequest>();
        int users = 1;                  // guarded by registryLock
        bool closing;                   // guarded by queueLock
        bool portOpen = true;           // worker thread only
        long lastActivityMs = long.MinValue / 2;

        AmplitudaSerialLine(AmplitudaSerialSettings settings, IAmplitudaSerialPort port)
        {
            this.settings = settings;
            this.port = port;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "AmplitudaSerial " + settings.PortName;
        }

        // Null + error on failure: ErrorSettingsDiffer, or the text of the open exception.
        public static AmplitudaSerialLine Acquire(AmplitudaSerialSettings settings, out string error)
        {
            error = null;
            lock (registryLock)
            {
                AmplitudaSerialLine line;
                if (registry.TryGetValue(settings.PortName, out line))
                {
                    if (!line.settings.SameParameters(settings))
                    {
                        error = ErrorSettingsDiffer;
                        return null;
                    }
                    line.users++;
                    return line;
                }
                IAmplitudaSerialPort port = PortFactory(settings);
                try
                {
                    port.Open();
                }
                catch (Exception ex)
                {
                    port.Dispose();
                    error = ex.Message;
                    return null;
                }
                line = new AmplitudaSerialLine(settings, port);
                registry.Add(settings.PortName, line);
                line.worker.Start();
                return line;
            }
        }

        // The pause of the line open on this port, 0 if none is open: for the message after
        // Acquire refused with ErrorSettingsDiffer (the registry is the only one who knows).
        public static int OpenGapMs(string portName)
        {
            lock (registryLock)
            {
                AmplitudaSerialLine line;
                return portName != null && registry.TryGetValue(portName, out line) ? line.settings.GapMs : 0;
            }
        }

        // The last user closes the port. Pending start/stop/clear are still sent, pending reads
        // (and probes) are dropped. A read job already in flight is also abandoned: neither the
        // dropped pending reads nor an in-flight job's Completed callback ever fires, so callers
        // must not wait for their results after calling Release().
        public void Release()
        {
            lock (registryLock)
            {
                if (users <= 0)
                {
                    return;
                }
                users--;
                if (users > 0)
                {
                    return;
                }
                registry.Remove(settings.PortName);
                lock (queueLock)
                {
                    closing = true;
                    Monitor.PulseAll(queueLock);
                }
                // Inside the registry lock: a new Acquire of this port must not race the close.
                worker.Join(AmplitudaSerialTiming.JoinTimeoutMs(settings.GapMs));
                port.Dispose();
            }
        }

        public long NowMs
        {
            get { return port.NowMs; }
        }

        public void Enqueue(AmplitudaSerialRequest request)
        {
            lock (queueLock)
            {
                if (closing)
                {
                    return;
                }
                // Only start/stop/clear and a confirmed status jump the line; a bare probe waits
                // with the reads (M2/I2) so a 16-address scan cannot delay a pending control
                // command or block a Release() for as long as the whole scan takes.
                if (request.Kind == AmplitudaSerialRequestKind.Status || request.Kind == AmplitudaSerialRequestKind.Control)
                {
                    urgent.Enqueue(request);
                }
                else if (request.Kind == AmplitudaSerialRequestKind.Read && request.Urgent)
                {
                    urgentReads.Enqueue(request);
                }
                else
                {
                    reads.Enqueue(request);
                }
                Monitor.PulseAll(queueLock);
            }
        }

        // Takes a request off the line (A318, finding 5): its owner no longer wants the result -
        // an acquisition that started a new sequence, gave up (Abort) or failed. A queued request
        // is dropped; a read job in flight ends after the step it is in (a step is never cut: that
        // would leave the block mid-exchange). Its Completed never fires, like the requests
        // Release() drops. Without this an aborted Stop's urgent read went on page by page and,
        // being urgent, could not even be parked: the next Stop on the line waited for all of it.
        public void Cancel(AmplitudaSerialRequest request)
        {
            lock (queueLock)
            {
                request.Cancelled = true;
                Remove(urgent, request);
                Remove(urgentReads, request);
                Remove(reads, request);
            }
        }

        static void Remove(Queue<AmplitudaSerialRequest> queue, AmplitudaSerialRequest request)
        {
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                AmplitudaSerialRequest next = queue.Dequeue();
                if (!ReferenceEquals(next, request))
                {
                    queue.Enqueue(next);
                }
            }
        }

        void Run()
        {
            try
            {
                ReadJob job = null;
                ReadJob parked = null;      // a normal job set aside while an urgent read runs
                for (;;)
                {
                    AmplitudaSerialRequest request = null;
                    lock (queueLock)
                    {
                        // A cancelled job ends here, between its steps (see Cancel).
                        if (job != null && job.Cancelled)
                        {
                            job = null;
                        }
                        if (parked != null && parked.Cancelled)
                        {
                            parked = null;
                        }
                        while (urgent.Count == 0 && urgentReads.Count == 0 && job == null && parked == null &&
                               reads.Count == 0 && !closing)
                        {
                            Monitor.Wait(queueLock);
                        }
                        if (urgent.Count > 0)
                        {
                            request = urgent.Dequeue();
                        }
                        else if (closing)
                        {
                            return;
                        }
                        else if (urgentReads.Count > 0 && (job == null || !job.Urgent))
                        {
                            // The job in flight (if any) has just finished a step: park it, run the
                            // urgent one to completion, then resume (a step is one exchange of
                            // up to DataAttempts tries, ~2.2 s at worst for 1024 bytes, so the
                            // urgent read waits for at most one such step).
                            if (job != null)
                            {
                                parked = job;
                            }
                            job = new ReadJob(urgentReads.Dequeue());
                        }
                        else if (job == null && parked != null)
                        {
                            job = parked;
                            parked = null;
                        }
                        else if (job == null)
                        {
                            AmplitudaSerialRequest queued = reads.Dequeue();
                            if (queued.Kind == AmplitudaSerialRequestKind.Probe)
                            {
                                // A probe is a single exchange (M2): run it the same way as a
                                // status request instead of standing up a multi-step ReadJob for it.
                                request = queued;
                            }
                            else
                            {
                                job = new ReadJob(queued);
                            }
                        }
                    }
                    if (request != null)
                    {
                        RunUrgent(request);
                    }
                    else if (job.Step(this))
                    {
                        job = null;
                    }
                }
            }
            finally
            {
                // Every exit path - including a timed-out Join from Release() - must leave the
                // port shut, or the worker (or a future one) could reopen it after the owner
                // considers the line released (I2).
                ClosePort();
            }
        }

        void RunUrgent(AmplitudaSerialRequest request)
        {
            AmplitudaSerialReading reading = new AmplitudaSerialReading();
            try
            {
                EnsureOpen();
                if (request.Kind == AmplitudaSerialRequestKind.Control)
                {
                    SendFrame(request.Address, request.Command, true);
                    port.Sleep(ControlSettleMs);
                    reading.Ok = true;
                }
                else
                {
                    byte status;
                    bool confirmed = request.Kind == AmplitudaSerialRequestKind.Status;
                    if (ReadStatus(request.Address, confirmed, out status))
                    {
                        reading.Ok = true;
                        reading.Status = status;
                        reading.Running = AmplitudaSerialProtocol.IsRunning(status);
                    }
                    else
                    {
                        reading.Error = "no answer";
                    }
                }
            }
            catch (Exception ex)
            {
                LineError();
                reading.Ok = false;
                reading.Error = ex.Message;
            }
            Complete(request, reading);
        }

        void Complete(AmplitudaSerialRequest request, AmplitudaSerialReading reading)
        {
            try
            {
                reading.CompletedMs = port.NowMs;
                if (request.Completed != null)
                {
                    request.Completed(reading);
                }
            }
            catch (Exception)
            {
            }
        }

        void EnsureOpen()
        {
            if (portOpen)
            {
                return;
            }
            bool isClosing;
            lock (queueLock)
            {
                isClosing = closing;
            }
            if (isClosing)
            {
                // Release() may have already timed out its Join and disposed the port; reopening
                // it here would leak a physical port nobody will ever close again (I2).
                throw new InvalidOperationException("line is closing");
            }
            port.Open();
            portOpen = true;
        }

        // An exception from Write / Read / DiscardInput. A block may be powered from the DTR/RTS
        // lines (AmplitudaSerialPort), so closing the port power-cycles EVERY block on the line:
        // a soft error (a write timeout of a busy adapter) must not do that. Probe the port
        // first: if it still accepts DiscardInput it is alive - keep it open and only fail the
        // request (the caller retries). Only a port that fails the probe too is closed, and the
        // next request reopens it.
        void LineError()
        {
            if (portOpen)
            {
                try
                {
                    port.DiscardInput();
                    return;
                }
                catch (Exception)
                {
                }
            }
            ClosePort();
        }

        void ClosePort()
        {
            portOpen = false;
            try { port.Close(); } catch (Exception) { }
        }

        void WaitGap()
        {
            long left = settings.GapMs - (port.NowMs - lastActivityMs);
            if (left > 0)
            {
                port.Sleep((int)left);
            }
        }

        void SendFrame(byte address, byte command, bool discardInput)
        {
            WaitGap();
            port.Write(address);
            lastActivityMs = port.NowMs;
            WaitGap();
            if (discardInput)
            {
                port.DiscardInput();
            }
            port.Write(command);
            lastActivityMs = port.NowMs;
        }

        int ReadFully(byte[] buffer, int want)
        {
            int got = port.Read(buffer, 0, want, FirstByteTimeoutMs);
            while (got > 0 && got < want)
            {
                int n = port.Read(buffer, got, want - got, IdleTimeoutMs);
                if (n == 0)
                {
                    break;
                }
                got += n;
            }
            lastActivityMs = port.NowMs;
            return got;
        }

        // The status answer is not covered by the checksum command, and a floating ground was
        // seen to turn 0x08 into 0xCC: a confirmed status needs two consecutive reads that agree on bit 0.
        bool ReadStatus(byte address, bool confirmed, out byte status)
        {
            status = 0;
            byte[] one = new byte[1];
            int previous = -1;
            int tries = confirmed ? StatusReads : 1;
            for (int i = 0; i < tries; i++)
            {
                SendFrame(address, AmplitudaSerialProtocol.CommandStatus, true);
                if (ReadFully(one, 1) != 1)
                {
                    previous = -1;
                    continue;
                }
                if (!confirmed || (previous >= 0 && ((previous ^ one[0]) & 0x01) == 0))
                {
                    status = one[0];
                    return true;
                }
                previous = one[0];
            }
            return false;
        }

        // One data block, verified: null after DataAttempts failures.
        byte[] ReadBlock(byte address, byte command)
        {
            byte[] data = new byte[AmplitudaSerialProtocol.BlockBytes];
            byte[] sum = new byte[1];
            for (int attempt = 0; attempt < DataAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    port.Sleep(RetryPauseMs);
                }
                SendFrame(address, command, true);
                if (ReadFully(data, data.Length) != data.Length)
                {
                    continue;
                }
                // Same exchange: nobody else may talk between a block and its checksum.
                SendFrame(address, AmplitudaSerialProtocol.CommandChecksum, false);
                if (ReadFully(sum, 1) == 1 && sum[0] == AmplitudaSerialProtocol.Xor(data, data.Length))
                {
                    return data;
                }
            }
            return null;
        }

        // A Read request runs step by step so that start/stop/clear can go in between.
        sealed class ReadJob
        {
            readonly AmplitudaSerialRequest request;
            readonly AmplitudaSerialReading reading = new AmplitudaSerialReading();
            readonly int pages;
            int step;

            public bool Urgent
            {
                get { return request.Urgent; }
            }

            // Read under the line's queue lock, like Cancel writes it.
            public bool Cancelled
            {
                get { return request.Cancelled; }
            }

            public ReadJob(AmplitudaSerialRequest request)
            {
                this.request = request;
                pages = AmplitudaSerialProtocol.PageCount(request.Channels);
            }

            // True = the job is finished (either way).
            public bool Step(AmplitudaSerialLine line)
            {
                try
                {
                    line.EnsureOpen();
                    if (step == 0)
                    {
                        byte status;
                        if (!line.ReadStatus(request.Address, true, out status))
                        {
                            return Finish(line, "no answer");
                        }
                        reading.Status = status;
                        reading.Running = AmplitudaSerialProtocol.IsRunning(status);
                    }
                    else if (step == 1)
                    {
                        byte[] header = line.ReadBlock(request.Address, AmplitudaSerialProtocol.CommandHeader);
                        if (header == null)
                        {
                            return Finish(line, "header");
                        }
                        reading.LiveSeconds = AmplitudaSerialProtocol.LiveSeconds(header);
                        reading.Channels = new int[request.Channels];
                    }
                    else
                    {
                        int page = step - 2;
                        byte[] data = line.ReadBlock(request.Address, AmplitudaSerialProtocol.PageCommand(page));
                        if (data == null)
                        {
                            return Finish(line, "page " + page.ToString(CultureInfo.InvariantCulture));
                        }
                        AmplitudaSerialProtocol.DecodePage(data, reading.Channels, page);
                        if (page == pages - 1)
                        {
                            return Finish(line, null);
                        }
                    }
                    step++;
                    return false;
                }
                catch (Exception ex)
                {
                    line.LineError();
                    return Finish(line, ex.Message);
                }
            }

            bool Finish(AmplitudaSerialLine line, string error)
            {
                reading.Ok = error == null;
                reading.Error = error;
                if (!reading.Ok)
                {
                    reading.Channels = null;
                }
                line.Complete(request, reading);
                return true;
            }
        }
    }
}
