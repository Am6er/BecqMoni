using System;
using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    // P280: the members the alignment of counts, live and real time is built from. Uses members
    // that do not exist before P280 - left out of the positive-control build against HEAD 4cb5ce9e
    // (MomentTests is the one that runs there).
    public static class MomentApiTests
    {
        static int portCounter;

        static int[] Block(int channel, int value)
        {
            int[] block = new int[1024];
            block[channel] = value;
            return block;
        }

        // The adjustment belongs to the open reading: a new reading clears it, Pour folds it with
        // the half second, SetZero and ClearAll drop it.
        public static void TestOpenAdjustBelongsToTheOpenReading()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 100.0);
            session.AdjustOpenLive(5.0);
            T.Near(100.0, session.LiveSeconds, 1e-12, "no open reading: nothing to adjust");
            session.SetZero(0);
            session.Apply(Block(3, 10), 10, true);
            T.Near(110.5, session.LiveSeconds, 1e-12, "open segment: block seconds + 0.5");
            T.Near(10.5, session.OpenLiveSeconds, 1e-12, "open live seconds");
            session.AdjustOpenLive(1.25);
            T.Near(111.75, session.LiveSeconds, 1e-12, "adjusted to the moment of the counts");
            session.Apply(Block(3, 20), 12, true);
            T.Near(112.5, session.LiveSeconds, 1e-12, "a new reading clears the adjustment");
            session.AdjustOpenLive(0.3);
            session.Pour();
            T.Near(112.8, session.LiveSeconds, 1e-12, "the pour folds what was shown");
            T.Near(0.0, session.OpenLiveSeconds, 1e-12, "nothing open after the pour");
            session.SetZero(0);
            session.Apply(Block(3, 5), 0, true);
            T.Near(112.8, session.LiveSeconds, 1e-12, "a segment under one block second gets no half second");
            session.ClearAll();
            T.Near(0.0, session.LiveSeconds, 1e-12, "cleared");
        }

        // The fold every Stop and every planned pour makes - a stopped block's reading, no
        // adjustment - gives the same bits as before P280: base + (block seconds + 0.5).
        public static void TestStoppedFoldIsBitForBitTheOld()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 123.456);
            session.SetZero(0);
            session.Apply(Block(3, 10), 7, true);
            session.AdjustOpenLive(0.987654321);
            session.Apply(Block(3, 30), 41, false);     // the halt's reading of the stopped block
            session.Pour();
            double old = 123.456;
            int segment = 41;
            old += segment + (segment >= 1 ? AmplitudaSerialSession.SegmentClockCorrectionSeconds : 0.0);
            T.True(session.LiveSeconds == old, "bit for bit: " + session.LiveSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " vs " + old.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        // The line stamps each data block at its command byte: header, then the pages in order,
        // all before the completion; a control command at its byte, a settle pause before it completes.
        public static void TestReadAndControlAreStamped()
        {
            FakeBlockPort fake = new FakeBlockPort();
            fake.Blocks[0] = new FakeBlock();
            AmplitudaSerialLine.PortFactory = settings => fake;
            string error;
            AmplitudaSerialLine line = AmplitudaSerialLine.Acquire(new AmplitudaSerialSettings { PortName = "STAMP" + (++portCounter).ToString(System.Globalization.CultureInfo.InvariantCulture) }, out error);
            AmplitudaSerialReading read = null;
            line.Enqueue(new AmplitudaSerialRequest
            {
                Kind = AmplitudaSerialRequestKind.Read, Address = 0, Channels = 1024,
                Completed = reading => read = reading
            });
            T.True(T.Wait(() => read != null, 5000), "read completed");
            AmplitudaSerialReading control = null;
            line.Enqueue(new AmplitudaSerialRequest
            {
                Kind = AmplitudaSerialRequestKind.Control, Address = 0, Command = AmplitudaSerialProtocol.CommandStart,
                Completed = reading => control = reading
            });
            T.True(T.Wait(() => control != null, 5000), "control completed");
            line.Release();
            long[] ms = fake.WireMsSnapshot();
            string[] wire = fake.WireSnapshot();
            T.True(read.Ok && read.PageMs != null && read.PageMs.Length == 2, "two page stamps");
            T.Eq(ms[Array.IndexOf(wire, "C81")], read.HeaderMs, "header stamped at its command byte");
            T.Eq(ms[Array.IndexOf(wire, "C82")], read.PageMs[0], "page 0 stamped at its command byte");
            T.Eq(ms[Array.IndexOf(wire, "C83")], read.PageMs[1], "page 1 stamped at its command byte");
            T.True(read.HeaderMs < read.PageMs[0] && read.PageMs[0] < read.PageMs[1] && read.PageMs[1] < read.CompletedMs, "in order, before the completion");
            T.Eq(ms[Array.IndexOf(wire, "C14")], control.SentMs, "start stamped at its command byte");
            T.True(control.CompletedMs - control.SentMs >= 100, "the settle pause lies between the command and its completion");
        }
    }
}
