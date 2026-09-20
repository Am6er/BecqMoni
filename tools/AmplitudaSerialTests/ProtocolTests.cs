using BecquerelMonitor;

namespace AmplitudaSerialTests
{
    public static class ProtocolTests
    {
        public static void TestChannelCounts()
        {
            T.True(AmplitudaSerialProtocol.IsValidChannelCount(512), "512 is valid");
            T.True(AmplitudaSerialProtocol.IsValidChannelCount(1024), "1024 is valid");
            T.True(AmplitudaSerialProtocol.IsValidChannelCount(8192), "8192 is valid");
            T.True(!AmplitudaSerialProtocol.IsValidChannelCount(0), "0 is invalid");
            T.True(!AmplitudaSerialProtocol.IsValidChannelCount(1000), "1000 is invalid");
            T.True(!AmplitudaSerialProtocol.IsValidChannelCount(8704), "17 pages is invalid");
            T.Eq(2, AmplitudaSerialProtocol.PageCount(1024), "pages of 1024");
            T.Eq(0x82, AmplitudaSerialProtocol.PageCommand(0), "page 0 command");
            T.Eq(0x83, AmplitudaSerialProtocol.PageCommand(1), "page 1 command");
        }

        public static void TestXor()
        {
            byte[] data = { 0x01, 0x02, 0x04, 0xFF };
            T.Eq(0x01 ^ 0x02 ^ 0x04, AmplitudaSerialProtocol.Xor(data, 3), "xor of the first 3 bytes");
            T.Eq(0x01 ^ 0x02 ^ 0x04 ^ 0xFF, AmplitudaSerialProtocol.Xor(data, 4), "xor of all bytes");
        }

        public static void TestStatusUsesBitZeroOnly()
        {
            T.True(!AmplitudaSerialProtocol.IsRunning(0x00), "0x00 stopped");
            T.True(!AmplitudaSerialProtocol.IsRunning(0x08), "0x08 stopped");
            T.True(!AmplitudaSerialProtocol.IsRunning(0x88), "0x88 stopped");
            T.True(AmplitudaSerialProtocol.IsRunning(0x01), "0x01 running");
            T.True(AmplitudaSerialProtocol.IsRunning(0x09), "0x09 running");
        }

        public static void TestHeaderLiveTimeIsBigEndian()
        {
            byte[] header = new byte[AmplitudaSerialProtocol.BlockBytes];
            header[34] = 0x04;      // 0x046F = 1135 s, the value seen on the real block
            header[35] = 0x6F;
            T.Eq(1135, AmplitudaSerialProtocol.LiveSeconds(header), "live time");
        }

        public static void TestPageIsLittleEndian()
        {
            byte[] page = new byte[AmplitudaSerialProtocol.BlockBytes];
            page[0] = 0x34; page[1] = 0x12;                 // channel 0 of the page
            page[1022] = 0xFF; page[1023] = 0xFF;           // channel 511 of the page
            int[] channels = new int[1024];
            AmplitudaSerialProtocol.DecodePage(page, channels, 1);
            T.Eq(0x1234, channels[512], "first channel of page 1");
            T.Eq(65535, channels[1023], "last channel of page 1");
            T.Eq(0, channels[0], "page 0 untouched");
        }
    }
}
