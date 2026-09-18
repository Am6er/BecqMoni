using BecquerelMonitor;

namespace AmplitudaUsbTests
{
    public static class ProtocolTests
    {
        // Builds a well-formed 64-byte report: [0]=0, [1]=2*events, [2..3]=live us LE, then codes LE.
        public static byte[] Frame(int liveMicros, params int[] codes)
        {
            byte[] r = new byte[64];
            r[1] = (byte)(codes.Length * 2);
            r[2] = (byte)(liveMicros & 0xFF);
            r[3] = (byte)(liveMicros >> 8);
            for (int i = 0; i < codes.Length; i++)
            {
                r[4 + 2 * i] = (byte)(codes[i] & 0xFF);
                r[5 + 2 * i] = (byte)(codes[i] >> 8);
            }
            return r;
        }

        static bool Decode(byte[] report, int length, out int count, out int live, out ushort[] codes)
        {
            codes = new ushort[AmplitudaUsbProtocol.MaxEvents];
            return AmplitudaUsbProtocol.TryDecode(report, length, codes, out count, out live);
        }

        public static void TestEmptyFrame()
        {
            int count, live; ushort[] codes;
            T.True(Decode(Frame(10994), 64, out count, out live, out codes), "empty frame is valid");
            T.Eq(0, count, "no events");
            T.Eq(10994, live, "live time");
        }

        public static void TestOneAndTwoEvents()
        {
            int count, live; ushort[] codes;
            T.True(Decode(Frame(10980, 308), 64, out count, out live, out codes), "one event is valid");
            T.Eq(1, count, "one event");
            T.Eq(308, codes[0], "code 0x0134 little endian");
            T.Eq(10980, live, "live time with one event");

            T.True(Decode(Frame(10966, 72, 4095), 64, out count, out live, out codes), "two events are valid");
            T.Eq(2, count, "two events");
            T.Eq(72, codes[0], "first code");
            T.Eq(4095, codes[1], "second code is the ADC maximum");
        }

        public static void TestFullFrame()
        {
            int[] many = new int[30];
            for (int i = 0; i < many.Length; i++) many[i] = 100 + i;
            int count, live; ushort[] codes;
            T.True(Decode(Frame(10574, many), 64, out count, out live, out codes), "30 events fill the report");
            T.Eq(30, count, "thirty events");
            T.Eq(129, codes[29], "last code");
        }

        public static void TestTailBeyondPayloadIsIgnored()
        {
            byte[] r = Frame(10980, 500);
            r[6] = 0xFF; r[7] = 0xFF;             // garbage right after the declared payload
            int count, live; ushort[] codes;
            T.True(Decode(r, 64, out count, out live, out codes), "garbage after the payload does not matter");
            T.Eq(1, count, "only the declared event is read");
        }

        public static void TestMalformedFrames()
        {
            int count, live; ushort[] codes;

            byte[] odd = Frame(10994); odd[1] = 3;
            T.True(!Decode(odd, 64, out count, out live, out codes), "odd payload length is rejected");

            byte[] tooLong = Frame(10994); tooLong[1] = 62;
            T.True(!Decode(tooLong, 64, out count, out live, out codes), "payload longer than 60 bytes is rejected");

            T.True(!Decode(Frame(10980, 4096), 64, out count, out live, out codes), "code above 4095 is rejected");

            byte[] reportId = Frame(10994); reportId[0] = 1;
            T.True(!Decode(reportId, 64, out count, out live, out codes), "non-zero report id is rejected");

            T.True(!Decode(Frame(10994), 63, out count, out live, out codes), "short read is rejected");
            T.True(!Decode(Frame(0), 64, out count, out live, out codes), "zero live time is rejected");
            T.True(!Decode(Frame(20001), 64, out count, out live, out codes), "live time above 20000 us is rejected");
            T.True(Decode(Frame(20000), 64, out count, out live, out codes), "live time of exactly 20000 us is accepted");

            T.True(!AmplitudaUsbProtocol.TryDecode(null, 64, new ushort[30], out count, out live), "null report is rejected");
            T.True(!AmplitudaUsbProtocol.TryDecode(Frame(10994), 64, new ushort[29], out count, out live), "too small code buffer is rejected");
            T.Eq(0, count, "count is zero after a rejected frame");
        }
    }
}
