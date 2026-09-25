using System;
using System.Collections.Generic;
using System.Threading;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    // One detector block: counts into a single hot channel while running, on virtual time.
    public sealed class FakeBlock
    {
        public int[] Channels = new int[1024];
        public double LiveMs;
        public bool Running;
        public bool Silent;                 // powered off / cable out
        public int CorruptNext;             // corrupt this many next data blocks (after their XOR is latched)
        public byte LoseCommand;            // swallow this control command...
        public int LoseCount;               // ...this many times
        public double Rate = 1000.0;        // counts per second of real time
        public double LiveFraction = 0.7;
        public int HotChannel = 252;
        // Real hardware fact (BDEG-3-2, 1024-channel spectrum): page requests beyond the
        // block's own spectrum still answer 1024 bytes with a VALID checksum, but the payload is
        // foreign memory (other internal buffers), not spectrum - nonzero and untouched by clear.
        public bool ForeignMemoryBeyondSpectrum;
        double carry;
        bool flicker;

        public void Advance(long ms)
        {
            if (!Running || Silent) return;
            carry += Rate * ms / 1000.0;
            int add = (int)carry;
            carry -= add;
            long value = (long)Channels[HotChannel] + add;
            if (value >= 65535)
            {
                // The real block's acquisition (live) clock stops together with the count the
                // instant it hits the ceiling: only the fraction of this step that ran before
                // that instant may add to LiveMs, not the whole (possibly huge) step.
                int room = 65535 - Channels[HotChannel];
                double fraction = add > 0 ? Math.Min(1.0, (double)room / add) : 0.0;
                LiveMs += ms * fraction * LiveFraction;
                Channels[HotChannel] = 65535;
                Running = false;            // the real block stops itself at the ceiling
                carry = 0;                  // nothing more to carry over once stopped
            }
            else
            {
                LiveMs += ms * LiveFraction;
                Channels[HotChannel] = (int)value;
            }
        }

        public void PowerCycle()
        {
            Array.Clear(Channels, 0, Channels.Length);
            LiveMs = 0;
            Running = false;
            carry = 0;
        }

        public byte Status()
        {
            flicker = !flicker;
            return (byte)((Running ? 0x01 : 0x00) | (flicker ? 0x08 : 0x00));
        }
    }

    // A serial port with blocks behind it. Thread-safe: the line thread talks, the test thread
    // advances virtual time and flips block state.
    public sealed class FakeBlockPort : IAmplitudaSerialPort
    {
        readonly object sync = new object();
        readonly Queue<byte> output = new Queue<byte>();
        long nowMs;
        long lastByteMs = -100000;
        bool expectAddress = true;
        int pendingAddress;
        byte lastXor;

        public readonly Dictionary<int, FakeBlock> Blocks = new Dictionary<int, FakeBlock>();
        public readonly List<string> Wire = new List<string>();       // "A00", "C13", ...
        public readonly ManualResetEvent Gate = new ManualResetEvent(true);  // closed = Write blocks
        public int GapMs = 20;
        public int GapViolations;
        public bool OpenFails;
        public bool WriteFails;
        public int OpenCount;
        public bool Disposed;

        public void Open()
        {
            if (OpenFails) throw new UnauthorizedAccessException("fake: access denied");
            lock (sync) { OpenCount++; }
        }

        public void Close()
        {
        }

        public void Write(byte value)
        {
            Gate.WaitOne();
            if (WriteFails) throw new TimeoutException("fake: write timeout");
            lock (sync)
            {
                if (expectAddress)
                {
                    if (nowMs - lastByteMs < GapMs) GapViolations++;
                    pendingAddress = value;
                    expectAddress = false;
                    Wire.Add("A" + value.ToString("X2"));
                }
                else
                {
                    if (nowMs - lastByteMs < GapMs) GapViolations++;
                    expectAddress = true;
                    Wire.Add("C" + value.ToString("X2"));
                    Execute(pendingAddress, value);
                }
                lastByteMs = nowMs;
            }
        }

        void Execute(int address, byte command)
        {
            FakeBlock block;
            if (!Blocks.TryGetValue(address, out block) || block.Silent) return;
            if (command == AmplitudaSerialProtocol.CommandStart || command == AmplitudaSerialProtocol.CommandStop ||
                command == AmplitudaSerialProtocol.CommandClear)
            {
                if (block.LoseCommand == command && block.LoseCount > 0) { block.LoseCount--; return; }
                if (command == AmplitudaSerialProtocol.CommandStart) block.Running = true;
                else if (command == AmplitudaSerialProtocol.CommandStop) block.Running = false;
                else { Array.Clear(block.Channels, 0, block.Channels.Length); block.LiveMs = 0; }
                return;
            }
            if (command == AmplitudaSerialProtocol.CommandStatus) { output.Enqueue(block.Status()); return; }
            if (command == AmplitudaSerialProtocol.CommandChecksum) { output.Enqueue(lastXor); return; }

            byte[] data = new byte[AmplitudaSerialProtocol.BlockBytes];
            if (command == AmplitudaSerialProtocol.CommandHeader)
            {
                for (int i = 0; i < data.Length; i++) data[i] = (byte)(1 - 2 * i);   // the "unfilled" ramp
                int seconds = (int)(block.LiveMs / 1000.0);
                data[32] = 0; data[34] = (byte)(seconds >> 8); data[35] = (byte)seconds;
            }
            else if (command >= AmplitudaSerialProtocol.CommandFirstPage &&
                     command < AmplitudaSerialProtocol.CommandFirstPage + 2)
            {
                int first = (command - AmplitudaSerialProtocol.CommandFirstPage) * 512;
                for (int i = 0; i < 512; i++)
                {
                    data[2 * i] = (byte)block.Channels[first + i];
                    data[2 * i + 1] = (byte)(block.Channels[first + i] >> 8);
                }
            }
            else if (block.ForeignMemoryBeyondSpectrum &&
                     command >= AmplitudaSerialProtocol.CommandFirstPage + 2 &&
                     command < AmplitudaSerialProtocol.CommandFirstPage + AmplitudaSerialProtocol.MaxPages)
            {
                // Foreign memory beyond the block's own 1024-channel spectrum: a fixed nonzero
                // pattern, independent of `block.Channels` - clear never touches it.
                int page = command - AmplitudaSerialProtocol.CommandFirstPage;
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)(i * 7 + page);
                }
            }
            else return;
            lastXor = AmplitudaSerialProtocol.Xor(data, data.Length);
            if (block.CorruptNext > 0) { block.CorruptNext--; data[100] ^= 0x40; }
            foreach (byte b in data) output.Enqueue(b);
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            lock (sync)
            {
                if (output.Count == 0) { AdvanceLocked(timeoutMs); return 0; }
                int n = Math.Min(count, output.Count);
                for (int i = 0; i < n; i++) buffer[offset + i] = output.Dequeue();
                AdvanceLocked((long)Math.Ceiling(n * 0.57));    // 11 bits per byte at 19200 baud
                lastByteMs = nowMs;
                return n;
            }
        }

        public void DiscardInput()
        {
            lock (sync) { output.Clear(); }
        }

        public void Sleep(int milliseconds)
        {
            lock (sync) { AdvanceLocked(milliseconds); }
        }

        // Test thread: let virtual time pass while the line is idle.
        public void Advance(long milliseconds)
        {
            lock (sync) { AdvanceLocked(milliseconds); }
        }

        void AdvanceLocked(long milliseconds)
        {
            if (milliseconds <= 0) return;
            nowMs += milliseconds;
            foreach (FakeBlock block in Blocks.Values) block.Advance(milliseconds);
        }

        public long NowMs
        {
            get { lock (sync) { return nowMs; } }
        }

        public string[] WireSnapshot()
        {
            lock (sync) { return Wire.ToArray(); }
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
