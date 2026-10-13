namespace BecquerelMonitor
{
    // Wire protocol of the detector blocks on the RS-232 daisy chain of the "Progress"
    // spectrometer set (NPP Doza BDEG-3-2 and relatives), recovered from SerialDrv.hmd.
    //
    // Frame: [address] - pause >= 20 ms - [command]. The address byte is told from a command
    // only by the silence before it. 19200 8N2, DTR and RTS raised.
    //   0x13 status  -> 1 byte, bit 0 = acquiring. Other bits are not understood (bit 3 flickers).
    //                   NOT covered by the checksum command.
    //   0x14 start, 0x15 stop, 0x16 clear (channels and time) -> no answer
    //   0x81 header  -> 1024 bytes; acquisition (live) time = uint16 BE at offset 34, seconds
    //   0x82+N page  -> 1024 bytes = 512 channels, uint16 LE
    //   0x10         -> 1 byte = XOR of the last data block (header or page)
    // A channel that reaches 65535 makes the block stop by itself.
    public static class AmplitudaSerialProtocol
    {
        public const byte CommandChecksum = 0x10;
        public const byte CommandStatus = 0x13;
        public const byte CommandStart = 0x14;
        public const byte CommandStop = 0x15;
        public const byte CommandClear = 0x16;
        public const byte CommandHeader = 0x81;
        public const byte CommandFirstPage = 0x82;

        public const int BlockBytes = 1024;
        public const int ChannelsPerPage = 512;
        public const int MaxPages = 16;
        public const int ChannelCeiling = 65535;

        const int LiveTimeOffset = 34;      // record of buffer 0 starts at 32; +2 = time, uint16 BE

        public static bool IsValidChannelCount(int channels)
        {
            return channels > 0 && channels % ChannelsPerPage == 0 && channels / ChannelsPerPage <= MaxPages;
        }

        public static int PageCount(int channels)
        {
            return channels / ChannelsPerPage;
        }

        public static byte PageCommand(int pageIndex)
        {
            return (byte)(CommandFirstPage + pageIndex);
        }

        public static byte Xor(byte[] data, int count)
        {
            byte acc = 0;
            for (int i = 0; i < count; i++)
            {
                acc ^= data[i];
            }
            return acc;
        }

        public static bool IsRunning(byte status)
        {
            return (status & 0x01) != 0;
        }

        public static int LiveSeconds(byte[] header)
        {
            return (header[LiveTimeOffset] << 8) | header[LiveTimeOffset + 1];
        }

        public static void DecodePage(byte[] page, int[] channels, int pageIndex)
        {
            int first = pageIndex * ChannelsPerPage;
            for (int i = 0; i < ChannelsPerPage; i++)
            {
                channels[first + i] = page[2 * i] | (page[2 * i + 1] << 8);
            }
        }
    }
}
