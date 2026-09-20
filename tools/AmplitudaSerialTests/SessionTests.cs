using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    public static class SessionTests
    {
        static int[] Block(int channel, int value)
        {
            int[] block = new int[1024];
            block[channel] = value;
            return block;
        }

        public static void TestDocumentPlusBlock()
        {
            int[] document = new int[1024];
            document[252] = 500;
            AmplitudaSerialSession session = new AmplitudaSerialSession(document, 100.0);
            session.SetZero(0);
            T.True(session.Apply(Block(252, 40), 7, true) == AmplitudaSerialVerdict.Ok, "verdict ok");
            int[] target = new int[1024];
            session.Compose(target);
            T.Eq(540, target[252], "document + block");
            T.Near(107.0, session.LiveSeconds, 1e-9, "live = document + block");
            T.Eq(540, session.TotalCounts, "total counts");
            document[252] = 0;
            session.Compose(target);
            T.Eq(540, target[252], "the base is a copy of the document");
        }

        public static void TestNotArmedIgnoresReadings()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            T.True(session.Apply(Block(1, 9999), 50, true) == AmplitudaSerialVerdict.Ignored, "ignored before SetZero");
            T.Eq(0, session.TotalCounts, "foreign counts not taken");
            session.SetZero(0);
            session.Apply(Block(1, 10), 1, true);
            session.Pour();
            T.True(session.Apply(Block(1, 10), 1, false) == AmplitudaSerialVerdict.Ignored, "ignored after Pour");
            T.Eq(10, session.TotalCounts, "not counted twice");
        }

        public static void TestZeroOfTimeIsTheFirstReadingAfterClear()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            session.SetZero(5);
            session.Apply(Block(1, 10), 12, true);
            T.Near(7.0, session.LiveSeconds, 1e-9, "live counted from the zero reading");
        }

        public static void TestPourThresholds()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            session.SetZero(0);
            T.True(session.Apply(Block(3, 32767), 10, true) == AmplitudaSerialVerdict.Ok, "below the channel threshold");
            T.True(session.Apply(Block(3, 32768), 11, true) == AmplitudaSerialVerdict.PourNeeded, "channel threshold");
            session.Pour();
            session.SetZero(0);
            T.True(session.Apply(Block(3, 5), 32768, true) == AmplitudaSerialVerdict.PourNeeded, "time threshold");
        }

        public static void TestPourFoldsBlockIntoBase()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            session.SetZero(0);
            session.Apply(Block(3, 40000), 600, true);
            session.Pour();
            session.SetZero(0);
            session.Apply(Block(3, 30000), 450, true);
            int[] target = new int[1024];
            session.Compose(target);
            T.Eq(70000, target[3], "a channel beyond 16 bits");
            T.Near(1050.0, session.LiveSeconds, 1e-9, "live across the pour");
        }

        public static void TestBlockResetKeepsTheLastGoodReading()
        {
            // Both readings below are nonzero (channel 3 carries counts), so this exercises only
            // the original sum/time regression check, not rule 4 (stopped-and-completely-empty):
            // rule 4 is covered separately by TestStoppedAndEmptyIsBlockReset.
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            session.SetZero(0);
            session.Apply(Block(3, 900), 20, true);
            T.True(session.Apply(Block(3, 0), 0, false) == AmplitudaSerialVerdict.BlockReset, "sum went down");
            T.True(session.Apply(Block(3, 950), 3, true) == AmplitudaSerialVerdict.BlockReset, "time went down");
            T.Eq(900, session.TotalCounts, "last good reading kept");
            session.Pour();
            T.Eq(900, session.TotalCounts, "and folded into the base");
        }

        public static void TestStoppedAndCeiling()
        {
            // Both readings below carry a nonzero sum, so they stay clear of rule 4 (a stopped,
            // completely empty reading is a reset, not "genuinely stopped with data" - see
            // TestStoppedAndEmptyIsBlockReset for that boundary).
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            session.SetZero(0);
            T.True(session.Apply(Block(3, 100), 5, false) == AmplitudaSerialVerdict.BlockStopped, "stopped without a ceiling");
            T.Eq(100, session.TotalCounts, "the reading of a stopped block is taken");
            T.True(session.Apply(Block(3, 65535), 9, false) == AmplitudaSerialVerdict.BlockCeiling, "stopped at the ceiling");
            T.Eq(65535, session.TotalCounts, "the ceiling reading is taken");
        }

        // Rule 4: Launch() only ever lets Poll() start once it has verified the freshly cleared
        // block is actually running, so a polled reading that comes back BOTH stopped AND still
        // exactly at the zero point it started from can only mean the block lost its memory in
        // between (a power blink) - never "stopped having legitimately counted nothing yet",
        // which Launch's own check already rules out.
        public static void TestStoppedAndEmptyIsBlockReset()
        {
            AmplitudaSerialSession session = new AmplitudaSerialSession(new int[1024], 0.0);
            session.SetZero(0);
            T.True(session.Apply(new int[1024], 0, false) == AmplitudaSerialVerdict.BlockReset,
                "stopped and still exactly at the zero reading: not taken");
            T.Eq(0, session.TotalCounts, "nothing taken");
            // The boundary the rule actually tests: sum == 0 alone is not enough - live time must
            // also not have advanced past the zero point, or a block that is legitimately stopped
            // having counted nothing (idle, not reset) would be misclassified as a reset.
            T.True(session.Apply(new int[1024], 5, false) == AmplitudaSerialVerdict.BlockStopped,
                "empty but live time advanced: genuinely stopped, not a reset");
        }

        public static void TestClearAll()
        {
            int[] document = new int[1024];
            document[1] = 77;
            AmplitudaSerialSession session = new AmplitudaSerialSession(document, 50.0);
            session.SetZero(0);
            session.Apply(Block(1, 5), 2, true);
            session.ClearAll();
            T.Eq(0, session.TotalCounts, "counts cleared");
            T.Near(0.0, session.LiveSeconds, 1e-9, "live cleared");
            T.True(!session.Armed, "not armed until the block is verified empty again");
        }

        public static void TestEstimateReal()
        {
            // The session ran 100 s real / 70 s live; the block then counted 7 more live seconds.
            T.Near(1110.0, AmplitudaSerialSession.EstimateReal(1100.0, 770.0, 777.0, 1000.0, 700.0), 1e-6, "scaled by the session ratio");
            T.Near(5.0, AmplitudaSerialSession.EstimateReal(0.0, 0.0, 5.0, 0.0, 0.0), 1e-9, "no history: live = real");
        }
    }
}
