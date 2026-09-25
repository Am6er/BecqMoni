using System;

namespace BecquerelMonitor
{
    // Events and time collected between two TakeBatch calls.
    public sealed class AmplitudaUsbBatch
    {
        public ushort[] Codes = new ushort[256];
        public int Count;                 // events in Codes
        public long LiveMicros;           // sum of per-frame live time
        public long RealMicros;           // sum of per-frame real time = live + dead time * events
        public int Frames;                // well-formed frames
        public int MalformedFrames;       // dropped frames

        internal void Add(ushort code)
        {
            if (Count == Codes.Length)
            {
                Array.Resize(ref Codes, Codes.Length * 2);
            }
            Codes[Count++] = code;
        }
    }

    // Hands list-mode data from the reader thread to the UI thread.
    // The reader thread calls AddFrame; the UI thread calls TakeBatch and bins the codes itself,
    // so thresholds and the channel shift are always applied to the document's current array.
    public sealed class AmplitudaUsbAccumulator
    {
        readonly object sync = new object();
        AmplitudaUsbBatch filling = new AmplitudaUsbBatch();

        public void AddFrame(ushort[] codes, int count, int liveMicros, double deadMicrosPerEvent)
        {
            lock (sync)
            {
                for (int i = 0; i < count; i++)
                {
                    filling.Add(codes[i]);
                }
                filling.LiveMicros += liveMicros;
                filling.RealMicros += liveMicros + (long)Math.Round(deadMicrosPerEvent * count);
                filling.Frames++;
            }
        }

        public void AddMalformedFrame()
        {
            lock (sync)
            {
                filling.MalformedFrames++;
            }
        }

        // Hands over everything collected so far. The returned batch belongs to the caller:
        // it is never touched again, a fresh one is started for the following frames.
        public AmplitudaUsbBatch TakeBatch()
        {
            lock (sync)
            {
                AmplitudaUsbBatch ready = filling;
                filling = new AmplitudaUsbBatch();
                return ready;
            }
        }

        // Bins ADC codes into the spectrum; returns how many passed the thresholds (inclusive).
        public static int Apply(int[] spectrum, ushort[] codes, int count, int shift, int lowerThreshold, int upperThreshold)
        {
            int accepted = 0;
            for (int i = 0; i < count; i++)
            {
                int code = codes[i];
                if (code < lowerThreshold || code > upperThreshold)
                {
                    continue;
                }
                int channel = code >> shift;
                if ((uint)channel >= (uint)spectrum.Length)
                {
                    continue;
                }
                spectrum[channel]++;
                accepted++;
            }
            return accepted;
        }

        // The 12-bit ADC maps onto 4096, 2048, 1024, 512 or 256 channels by a right shift.
        public static bool TryGetShift(int numberOfChannels, out int shift)
        {
            for (shift = 0; shift <= 4; shift++)
            {
                if ((AmplitudaUsbProtocol.AdcMaxCode + 1) >> shift == numberOfChannels)
                {
                    return true;
                }
            }
            shift = 0;
            return false;
        }
    }
}
