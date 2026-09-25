using System;

namespace BecquerelMonitor
{
    public enum AmplitudaSerialVerdict
    {
        Ok,
        PourNeeded,     // taken; fold the block into the base before 16 bits run out
        BlockReset,     // NOT taken: counts or time went down - the block lost its memory
        BlockStopped,   // taken; the block is not acquiring although nobody stopped it
        BlockCeiling,   // taken; a channel hit 65535 and the block stopped itself
        Ignored         // not armed: the block is not verified empty, or is already folded
    }

    // "The document is the master": document = base + block. The block keeps its histogram only
    // while powered and only up to 65535 per channel / 65535 s, so the document never takes the
    // block's word for the whole measurement - only for what was counted since the last clear.
    // Pure arithmetic: no port, no UI, no clock.
    public sealed class AmplitudaSerialSession
    {
        public int PourChannelThreshold = 32768;
        public int PourSecondsThreshold = 32768;

        readonly int[] baseChannels;
        double baseLive;
        long baseSum;
        int[] last;
        long lastSum;
        int lastLive;
        int zeroLive;
        bool armed;

        public AmplitudaSerialSession(int[] documentSpectrum, double documentLiveSeconds)
        {
            baseChannels = (int[])documentSpectrum.Clone();
            baseLive = documentLiveSeconds;
            baseSum = Sum(baseChannels);
        }

        public bool Armed
        {
            get { return armed; }
        }

        // The block was just cleared and read back empty; liveSeconds is its time at that moment.
        public void SetZero(int liveSeconds)
        {
            last = null;
            lastSum = 0;
            lastLive = liveSeconds;
            zeroLive = liveSeconds;
            armed = true;
        }

        public AmplitudaSerialVerdict Apply(int[] block, int liveSeconds, bool running)
        {
            if (!armed)
            {
                return AmplitudaSerialVerdict.Ignored;
            }
            long sum = Sum(block);
            if (sum < lastSum || liveSeconds < lastLive)
            {
                return AmplitudaSerialVerdict.BlockReset;
            }
            // A block whose power blinked comes back with EMPTY memory (hardware fact): the
            // caller (AmplitudaSerialAcquisition) only ever starts polling once Launch() has
            // itself verified that a freshly cleared block is actually running. So a polled
            // reading that comes back BOTH stopped AND still exactly at the zero point it
            // started from cannot be "stopped having legitimately counted nothing yet" - Launch
            // already ruled that out - it can only mean the block lost its memory in between.
            // NOT taken, exactly like the sum/time regression check above.
            if (!running && sum == 0 && liveSeconds <= zeroLive)
            {
                return AmplitudaSerialVerdict.BlockReset;
            }
            last = block;
            lastSum = sum;
            lastLive = liveSeconds;

            int max = 0;
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] > max)
                {
                    max = block[i];
                }
            }
            if (!running)
            {
                return max >= AmplitudaSerialProtocol.ChannelCeiling ? AmplitudaSerialVerdict.BlockCeiling : AmplitudaSerialVerdict.BlockStopped;
            }
            if (max >= PourChannelThreshold || liveSeconds - zeroLive >= PourSecondsThreshold)
            {
                return AmplitudaSerialVerdict.PourNeeded;
            }
            return AmplitudaSerialVerdict.Ok;
        }

        // Folds what the block has counted into the base. The block must be cleared and
        // verified (SetZero) before readings are taken again.
        public void Pour()
        {
            if (last != null)
            {
                for (int i = 0; i < baseChannels.Length; i++)
                {
                    baseChannels[i] += last[i];
                }
                baseSum += lastSum;
                baseLive += lastLive - zeroLive;
            }
            last = null;
            lastSum = 0;
            lastLive = 0;
            zeroLive = 0;
            armed = false;
        }

        public void ClearAll()
        {
            Array.Clear(baseChannels, 0, baseChannels.Length);
            baseSum = 0;
            baseLive = 0;
            last = null;
            lastSum = 0;
            lastLive = 0;
            zeroLive = 0;
            armed = false;
        }

        public void Compose(int[] target)
        {
            for (int i = 0; i < baseChannels.Length; i++)
            {
                target[i] = baseChannels[i] + (last != null ? last[i] : 0);
            }
        }

        public double LiveSeconds
        {
            get { return baseLive + (lastLive - zeroLive); }
        }

        public long TotalCounts
        {
            get { return baseSum + lastSum; }
        }

        // Real time of a piece that ended while nobody watched (the block stopped itself between
        // two polls): its live time scaled by the real/live ratio of this session so far.
        public static double EstimateReal(double realAtLastGood, double liveAtLastGood, double liveNow,
            double realAtSessionStart, double liveAtSessionStart)
        {
            double live = liveAtLastGood - liveAtSessionStart;
            double real = realAtLastGood - realAtSessionStart;
            double ratio = live > 0 && real > 0 ? real / live : 1.0;
            return realAtLastGood + Math.Max(0.0, liveNow - liveAtLastGood) * ratio;
        }

        static long Sum(int[] channels)
        {
            long sum = 0;
            for (int i = 0; i < channels.Length; i++)
            {
                sum += channels[i];
            }
            return sum;
        }
    }
}
