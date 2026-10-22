using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BecquerelMonitor
{
    // shproto codec, ported from AtomSpectraVCPIn.cs:32-40 (constants, state),
    // :439-495 (crc16, search_packet) and :658-693 (send_packet, add2buff_escape); same algorithm, no
    // port I/O.
    public sealed class AtomSpectraShproto
    {
        public const byte Start = (byte)(0xFE | 0x80);
        public const byte Esc = (byte)(0xFD | 0x80);
        public const byte Finish = (byte)(0xA5 | 0x80);
        public const int MaxDataCount = 65535;

        private bool rx_esc = false;
        private ushort rx_crc = 0;
        private int packet_cmd;
        private int packet_length;
        private List<byte> packet = new List<byte>();
        private bool inPacket = false;
        private bool lastFinishBadCrc = false;

        public static ushort Crc16(ushort crc, byte data)
        {
            crc = (ushort)(crc ^ data);
            for (byte i = 0; i < 8; ++i)
            {
                if (((ushort)(crc & 0x0001)) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                else crc = (ushort)(crc >> 1);
            }
            return crc;
        }

        public bool Feed(byte rxByte)
        {
            lastFinishBadCrc = false;

            // A TCP client joins the board's stream mid-packet, so bytes before the first START must not form a packet
            // (the COM reader opens the port in the same position but relies on the crc alone).
            if (!inPacket && rxByte != Start)
            {
                return false;
            }

            switch (rxByte)
            {
                case Start:
                    packet.Clear();
                    rx_esc = false;
                    rx_crc = 0xFFFF;
                    packet_length = 0;
                    inPacket = true;
                    break;
                case Esc:
                    rx_esc = true;
                    break;
                case Finish:
                    inPacket = false;
                    packet_length -= 3;
                    if (rx_crc != 0)
                    {
                        lastFinishBadCrc = true;
                        return false;
                    }
                    return true;
                default:
                    if (rx_esc)
                    {
                        rx_esc = false;
                        rxByte = (byte)(~rxByte);
                    }
                    if (packet.Count < MaxDataCount)
                    {
                        if (packet_length == 0) packet_cmd = rxByte;
                        else packet.Add(rxByte);
                        packet_length++;
                    }
                    rx_crc = Crc16(rx_crc, rxByte);
                    break;
            }
            return false;
        }

        public int Command
        {
            get { return packet_cmd; }
        }

        public int Length
        {
            get { return packet_length; }
        }

        public List<byte> Packet
        {
            get { return packet; }
        }

        public bool LastFinishBadCrc
        {
            get { return lastFinishBadCrc; }
        }

        public static byte[] Encode(byte[] data)
        {
            List<byte> outBuf = new List<byte>();
            ushort packet_crc = 0xFFFF;

            outBuf.Add(0xFF);
            outBuf.Add(Start);

            foreach (byte d in data)
            {
                packet_crc = Crc16(packet_crc, d);
                Add2BuffEscape(outBuf, d);
            }

            Add2BuffEscape(outBuf, (byte)(packet_crc & 0xff));
            Add2BuffEscape(outBuf, (byte)(packet_crc >> 8));

            outBuf.Add(Finish);

            return outBuf.ToArray();
        }

        private static void Add2BuffEscape(List<byte> outBuf, byte txByte)
        {
            if (txByte == Esc || txByte == Start || txByte == Finish)
            {
                outBuf.Add(Esc);
                outBuf.Add((byte)((~txByte) & 0xFF));
            }
            else
            {
                outBuf.Add(txByte);
            }
        }

        public static byte[] EncodeText(string command)
        {
            byte[] ascii = Encoding.ASCII.GetBytes(command);
            byte[] data = new byte[ascii.Length + 1];
            data[0] = 0x03;
            Array.Copy(ascii, 0, data, 1, ascii.Length);
            return Encode(data);
        }
    }

    public class AtomSpectraTcpDataReadyArgs : EventArgs
    {
        public int[] Histogram { get; private set; }
        public int CpuLoad { get; private set; }
        public int ElapsedTime { get; private set; }
        public int InvalidPulses { get; private set; }
        public int Cps { get; private set; }

        public AtomSpectraTcpDataReadyArgs(int[] histogram, int cpuLoad, int elapsedTime, int invalidPulses, int cps)
        {
            Histogram = histogram;
            CpuLoad = cpuLoad;
            ElapsedTime = elapsedTime;
            InvalidPulses = invalidPulses;
            Cps = cps;
        }
    }

    // The protocol loop, separate from the controller (structure as AmplitudaSerialAcquisition):
    // one background thread owns the transport, reconnects after a delay when the link drops, parses
    // shproto and raises DataReady when a complete histogram cycle and a status packet have both arrived, as AtomSpectraVCPIn does once per second.
    public sealed class AtomSpectraTcpAcquisition : IDisposable
    {
        public const int HistogramSize = 8192;
        // the board keeps its single client slot busy for about a minute after a client vanishes (keepalive release), so earlier retries are refused.
        public const int DefaultReconnectDelayMs = 60000;
        public const int ReadTimeoutMs = 100;

        private readonly Func<IAtomSpectraTcpTransport> factory;
        private readonly int reconnectDelayMs;
        private readonly int keepAliveMs;
        private readonly string name;

        private IAtomSpectraTcpTransport transport;
        private Thread thread;
        private volatile bool alive;
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent connectedEvent = new ManualResetEvent(false);
        private readonly ConcurrentQueue<byte[]> outgoing = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
        private AtomSpectraShproto codec;
        private readonly int[] histogram = new int[HistogramSize];
        private int cpuLoad;
        private int cps;
        private int invalidPulses;
        private long lastKeepAliveMs;
        private bool connectedOnce;

        private int packetsReceived;
        private int crcErrors;
        private int malformedPackets;
        private int reconnects;

        private volatile bool isConnected;
        private volatile bool isLinkLost;
        private volatile string lastError;
        private volatile string commandOnReconnect;

        // Wake-up for an immediate reconnect, first-attempt signal (fail fast),
        // one-shot commands for the next connection, exit after the reconnect command (deferred -sto).
        private readonly AutoResetEvent wakeEvent = new AutoResetEvent(false);
        private readonly ManualResetEvent attemptEvent = new ManualResetEvent(false);
        private readonly ConcurrentQueue<string> oneShot = new ConcurrentQueue<string>();
        private volatile bool exitAfterReconnectCommand;
        private bool reconnectCommandSent;

        // A data event pairs a COMPLETE histogram cycle (all channels since the packet with offset 0) with a
        // status packet, in whichever order they arrive; both flags are cleared on every (re)connect, so a
        // histogram from before a dropped link is never handed out as new.
        private readonly bool[] covered = new bool[HistogramSize];
        private int coveredCount;
        private bool histogramFresh;
        private bool statusFresh;
        private int lastElapsed;
        private long histogramAtMs;
        private long statusAtMs;
        // The analyzer sends the histogram and the status of one cycle together, cycles about 1 s apart
        // (PROTOCOL.en.md); a status and a histogram further apart than this belong to different cycles
        // (e.g. a status left over from the cycle before a reconnect) and are not paired.
        public const int PairWindowMs = 400;

        // Log window statistics, written every StatsEveryMs
        public const int StatsEveryMs = 5000;
        private readonly Stopwatch statsClock = Stopwatch.StartNew();
        private long statsAtMs;
        private long bytesReceived, statsBytes;
        private int statsPackets, histogramCycles;

        public event EventHandler<AtomSpectraTcpDataReadyArgs> DataReady;
        public event EventHandler ConnectionLost;
        public event EventHandler Connected;

        public AtomSpectraTcpAcquisition(Func<IAtomSpectraTcpTransport> transportFactory, int reconnectDelayMs, int keepAliveMs, string name)
        {
            this.factory = transportFactory;
            this.reconnectDelayMs = reconnectDelayMs;
            this.keepAliveMs = keepAliveMs;
            this.name = name;
        }

        public void Start()
        {
            if (thread != null) return;
            alive = true;
            thread = new Thread(Run) { IsBackground = true, Name = "AtomSpectraTcp" };
            thread.Start();
        }

        public bool IsConnected
        {
            get { return isConnected; }
        }

        public bool IsLinkLost
        {
            get { return isLinkLost; }
        }

        public string LastError
        {
            get { return lastError; }
        }

        public int PacketsReceived
        {
            get { return packetsReceived; }
        }

        public int CrcErrors
        {
            get { return crcErrors; }
        }

        public int MalformedPackets
        {
            get { return malformedPackets; }
        }

        public int Reconnects
        {
            get { return reconnects; }
        }

        // the board's watchdog does not restart the analyzer for a TCP client,
        // so after a dropped link the measurement is restarted by the client.
        public string CommandOnReconnect
        {
            get { return commandOnReconnect; }
            set { commandOnReconnect = value; }
        }

        public bool WaitConnected(int timeoutMs)
        {
            return connectedEvent.WaitOne(timeoutMs) && IsConnected;
        }

        // waits for the first connect attempt, successful or not; a refused connect answers in milliseconds.
        public bool WaitFirstAttempt(int timeoutMs)
        {
            return attemptEvent.WaitOne(timeoutMs) && IsConnected;
        }

        // skip the rest of the reconnect delay.
        public void ReconnectNow()
        {
            wakeEvent.Set();
        }

        // sent once on the next connection, before CommandOnReconnect.
        public void QueueOnReconnect(string command)
        {
            oneShot.Enqueue(command);
        }

        public bool PendingOneShot(string command)
        {
            foreach (string s in oneShot)
            {
                if (s == command) return true;
            }
            return false;
        }

        // the reader thread writes CommandOnReconnect on the next reconnect and then ends: deferred -sto.
        public bool ExitAfterReconnectCommand
        {
            get { return exitAfterReconnectCommand; }
            set { exitAfterReconnectCommand = value; }
        }

        public bool WaitExit(int timeoutMs)
        {
            Thread current = thread;
            return current == null || current.Join(timeoutMs);
        }

        public void SendCommand(string command)
        {
            Trace.WriteLine("AtomSpectraTcp command: " + command);
            string line;
            while (lines.TryDequeue(out line)) { }
            outgoing.Enqueue(AtomSpectraShproto.EncodeText(command));
        }

        public bool WaitForAnswer(string answer, int timeoutMs)
        {
            string line = GetCommandOutput(timeoutMs);
            if (line == null) return false;
            int cr = line.IndexOf('\r');
            if (cr >= 0) line = line.Substring(0, cr);
            return answer.Equals(line);
        }

        public string GetCommandOutput(int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string line;
                if (lines.TryDequeue(out line))
                {
                    return line;
                }
                Thread.Sleep(1);
            }
            return "";
        }

        public void Dispose()
        {
            alive = false;
            stopEvent.Set();
            wakeEvent.Set();
            if (thread != null)
            {
                if (!thread.Join(5000))
                {
                    Trace.WriteLine("AtomSpectraTcp thread did not stop within 5000 ms");
                }
                thread = null;
            }
            if (transport != null)
            {
                try
                {
                    transport.Close();
                    transport.Dispose();
                }
                catch
                {
                }
                transport = null;
            }
        }

        // Channel compression ported from AtomSpectraVCPIn.cs:605-632 (sum of `mul` adjacent channels).
        public static int[] Compress(int[] full, int channels)
        {
            if (channels > 0 && full.Length > channels)
            {
                int mul = full.Length / channels;
                int[] result = new int[channels];
                for (int i = 0; i < channels; i++)
                {
                    int sum = 0;
                    for (int j = 0; j < mul; j++)
                    {
                        sum += full[i * mul + j];
                    }
                    result[i] = sum;
                }
                return result;
            }
            return (int[])full.Clone();
        }

        private void WaitBeforeRetry()
        {
            WaitHandle.WaitAny(new WaitHandle[] { stopEvent, wakeEvent }, reconnectDelayMs);
        }

        private void Run()
        {
            byte[] buffer = new byte[4096];
            while (alive)
            {
                if (transport == null)
                {
                    IAtomSpectraTcpTransport created = null;
                    try
                    {
                        created = factory();
                        created.Open();
                        transport = created;
                        codec = new AtomSpectraShproto();
                        lastKeepAliveMs = created.NowMs;
                        Array.Clear(covered, 0, covered.Length);
                        coveredCount = 0;
                        histogramFresh = false;
                        statusFresh = false;
                        string queued;
                        string reconnectCommandSentNow = null;
                        while (oneShot.TryDequeue(out queued))
                        {
                            outgoing.Enqueue(AtomSpectraShproto.EncodeText(queued));
                            AtomSpectraTcpLog.Info("TCP", "queued command " + queued + " goes out on this connection");
                        }
                        if (connectedOnce)
                        {
                            reconnects++;
                            string again = CommandOnReconnect;
                            if (again != null)
                            {
                                outgoing.Enqueue(AtomSpectraShproto.EncodeText(again));
                                reconnectCommandSent = true;
                                reconnectCommandSentNow = again;
                            }
                        }
                        connectedOnce = true;
                        isLinkLost = false;
                        lastError = null;
                        isConnected = true;
                        connectedEvent.Set();
                        attemptEvent.Set();
                        Trace.WriteLine("AtomSpectraTcp connected: " + name);
                        AtomSpectraTcpLog.Info("TCP", (reconnects > 0 ? "reconnected (#" + reconnects + ") to " : "connected to ") + name
                            + (reconnectCommandSentNow != null ? ", sent " + reconnectCommandSentNow + " after the reconnect" : ""));
                        EventHandler handler = Connected;
                        if (handler != null)
                        {
                            try
                            {
                                handler(this, EventArgs.Empty);
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine("AtomSpectraTcp Connected handler exception: " + ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (created != null)
                        {
                            try
                            {
                                created.Dispose();
                            }
                            catch
                            {
                            }
                        }
                        lastError = ex.Message;
                        isLinkLost = true;
                        attemptEvent.Set();
                        Trace.WriteLine("AtomSpectraTcp connect to " + name + " failed: " + ex.Message
                            + "; next attempt in " + (reconnectDelayMs / 1000) + " s");
                        AtomSpectraTcpLog.Warn("TCP", "connect to " + name + " failed: " + ex.Message
                            + "; next attempt in " + (reconnectDelayMs / 1000) + " s (at " + DateTime.Now.AddMilliseconds(reconnectDelayMs).ToString("HH:mm:ss") + ")");
                        WaitBeforeRetry();
                        continue;
                    }
                }

                try
                {
                    byte[] packet;
                    while (outgoing.TryDequeue(out packet))
                    {
                        transport.Write(packet, 0, packet.Length);
                    }

                    if (exitAfterReconnectCommand && reconnectCommandSent)
                    {
                        Trace.WriteLine("AtomSpectraTcp " + name + ": reconnect command sent, reader ends");
                        AtomSpectraTcpLog.Info("TCP", name + ": deferred command delivered after the reconnect, connection closed");
                        break;
                    }

                    if (keepAliveMs > 0 && transport.NowMs - lastKeepAliveMs >= keepAliveMs)
                    {
                        transport.Write(new byte[] { 0xFF }, 0, 1);
                        lastKeepAliveMs = transport.NowMs;
                    }

                    int n = transport.Read(buffer, 0, buffer.Length, ReadTimeoutMs);
                    if (n > 0) { bytesReceived += n; statsBytes += n; }
                    LogStatsIfDue();
                    for (int i = 0; i < n; i++)
                    {
                        bool finished = codec.Feed(buffer[i]);
                        if (finished)
                        {
                            HandlePacket();
                        }
                        else if (buffer[i] == AtomSpectraShproto.Finish && codec.LastFinishBadCrc)
                        {
                            crcErrors++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!alive) break;
                    isConnected = false;
                    connectedEvent.Reset();
                    isLinkLost = true;
                    lastError = ex.Message;
                    try
                    {
                        transport.Dispose();
                    }
                    catch
                    {
                    }
                    transport = null;
                    Trace.WriteLine("AtomSpectraTcp link lost: " + ex.Message + " next attempt in " + (reconnectDelayMs / 1000) + " s");
                    AtomSpectraTcpLog.Error("TCP", "link to " + name + " lost: " + ex.Message + "; next attempt in " + (reconnectDelayMs / 1000)
                        + " s (at " + DateTime.Now.AddMilliseconds(reconnectDelayMs).ToString("HH:mm:ss") + "); counts on the analyzer are kept");
                    EventHandler handler = ConnectionLost;
                    if (handler != null)
                    {
                        try
                        {
                            handler(this, EventArgs.Empty);
                        }
                        catch (Exception ex2)
                        {
                            Trace.WriteLine("AtomSpectraTcp ConnectionLost handler exception: " + ex2.Message);
                        }
                    }
                    WaitBeforeRetry();
                }
            }
            if (transport != null)
            {
                try
                {
                    transport.Dispose();
                }
                catch
                {
                }
                transport = null;
            }
            isConnected = false;
        }

        // Packet handling ported from AtomSpectraVCPIn.cs:570-642 (cmd 0x01 histogram, 0x04 status,
        // 0x03 text), plus length checks: a malformed packet is counted, the link is kept.
        private void HandlePacket()
        {
            try
            {
                int cmd = codec.Command;
                int length = codec.Length;
                List<byte> packet = codec.Packet;
                packetsReceived++;
                statsPackets++;

                if (cmd == 0x01)
                {
                    if (length < 2)
                    {
                        malformedPackets++;
                        return;
                    }
                    int offset = packet[0] | (packet[1] << 8);
                    // offset 0 starts a new spectrum, PROTOCOL.en.md "Histogram packet"
                    if (offset == 0)
                    {
                        Array.Clear(covered, 0, covered.Length);
                        coveredCount = 0;
                        histogramFresh = false;
                    }
                    int count = (length - 2) / 4;
                    for (int j = 0; j < count; j++)
                    {
                        int index = offset + j;
                        if (index < histogram.Length)
                        {
                            int value = packet[j * 4 + 2] | (packet[j * 4 + 3] << 8) | (packet[j * 4 + 4] << 16) | (packet[j * 4 + 5] << 24);
                            histogram[index] = value & 0x7FFFFFF;
                            if (!covered[index])
                            {
                                covered[index] = true;
                                coveredCount++;
                            }
                        }
                    }
                    if (coveredCount == HistogramSize)
                    {
                        histogramFresh = true;
                        histogramCycles++;
                        histogramAtMs = transport.NowMs;
                        TryRaiseDataReady();
                    }
                }
                else if (cmd == 0x04)
                {
                    if (length < 10)
                    {
                        malformedPackets++;
                        return;
                    }
                    int elapsed_time = (packet[0] | (packet[1] << 8) | (packet[2] << 16) | (packet[3] << 24));
                    elapsed_time &= 0x7FFFFFF;
                    lastElapsed = elapsed_time;
                    cpuLoad = packet[4] | (packet[5] << 8);
                    cps = packet[6] | (packet[7] << 8) | (packet[8] << 16) | (packet[9] << 24);
                    // Donor `packet.Count > 10` (payload + 2 crc bytes): the field is there from 14 payload bytes;
                    // a shorter status packet keeps the previous value, as in the donor.
                    if (length >= 14)
                    {
                        invalidPulses = (packet[10] | (packet[11] << 8) | (packet[12] << 16) | (packet[13] << 24));
                    }

                    statusFresh = true;
                    statusAtMs = transport.NowMs;
                    TryRaiseDataReady();
                }
                else if (cmd == 0x03)
                {
                    byte[] arr = new byte[length];
                    packet.CopyTo(0, arr, 0, length);
                    string str = Encoding.ASCII.GetString(arr, 0, length);
                    lines.Enqueue(str);
                    Trace.WriteLine("AtomSpectraTcp line: " + str);
                }
                // Other codes (0x02 oscilloscope) are not used by BecqMoni, as in the donor.
            }
            catch (Exception ex)
            {
                malformedPackets++;
                Trace.WriteLine("AtomSpectraTcp malformed packet: " + ex.Message);
            }
        }

        // Every StatsEveryMs: packets/s, bytes/s, complete histogram cycles and the last counters (log window).
        private void LogStatsIfDue()
        {
            long now = statsClock.ElapsedMilliseconds;
            if (now - statsAtMs < StatsEveryMs) return;
            double seconds = (now - statsAtMs) / 1000.0;
            long total = 0;
            for (int i = 0; i < histogram.Length; i++) total += histogram[i];
            AtomSpectraTcpLog.Info("TCP", string.Format(CultureInfo.InvariantCulture,
                "stats: {0:F1} packets/s, {1:F0} bytes/s, histogram cycles {2}, last total {3}, time {4} s, cps {5}, invalid {6}, crc errors {7}",
                statsPackets / seconds, statsBytes / seconds, histogramCycles, total, lastElapsed, cps, invalidPulses, crcErrors));
            statsAtMs = now;
            statsPackets = 0;
            statsBytes = 0;
        }

        // a data event pairs a complete histogram cycle with a status packet in whichever order they arrive;
        // a status packet alone only updates the counters.
        private void TryRaiseDataReady()
        {
            if (!(histogramFresh && statusFresh)) return;
            if (Math.Abs(histogramAtMs - statusAtMs) > PairWindowMs)
            {
                // Keep only the newer half; the older one belongs to another cycle.
                if (histogramAtMs > statusAtMs) statusFresh = false; else histogramFresh = false;
                return;
            }
            histogramFresh = false;
            statusFresh = false;
            EventHandler<AtomSpectraTcpDataReadyArgs> handler = DataReady;
            if (handler != null)
            {
                try
                {
                    handler(this, new AtomSpectraTcpDataReadyArgs((int[])histogram.Clone(), cpuLoad, lastElapsed, invalidPulses, cps));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("AtomSpectraTcp DataReady handler exception: " + ex.Message);
                }
            }
        }
    }
}
