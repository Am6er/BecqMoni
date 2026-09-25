using System;
using System.Collections.Generic;
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
        // Runs on the LINE thread: it may only hand the result over (a locked queue, a flag).
        // It must never call Acquire / Release or wait for anything.
        public Action<AmplitudaSerialReading> Completed;
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
        const int DataAttempts = 3;
        const int StatusReads = 4;
        const int RetryPauseMs = 100;
        const int ControlSettleMs = 100;
        const int JoinTimeoutMs = 5000;

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
                worker.Join(JoinTimeoutMs);
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
                else
                {
                    reads.Enqueue(request);
                }
                Monitor.PulseAll(queueLock);
            }
        }

        void Run()
        {
            try
            {
                ReadJob job = null;
                for (;;)
                {
                    AmplitudaSerialRequest request = null;
                    lock (queueLock)
                    {
                        while (urgent.Count == 0 && job == null && reads.Count == 0 && !closing)
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
                PortFailed();
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
                PortFailed();
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

        // A hung adapter (write timeout) or a vanished port: close, the next request reopens.
        void PortFailed()
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
                            return Finish(line, "page " + page);
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
                    line.PortFailed();
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
