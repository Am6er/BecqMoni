namespace BecquerelMonitor
{
    // Wire format of the Amplituda "Progress spectrometer" USB HID input report (list mode):
    //   [0]       report id, always 0
    //   [1]       payload length in bytes = 2 * events in this frame (even, <= 60)
    //   [2..3]    live time of the frame, microseconds, uint16 LE
    //   [4..4+N)  pulse amplitudes, uint16 LE, 12-bit ADC codes 0..4095
    // The device has no output reports: it streams one frame every ~11 ms whenever powered.
    public static class AmplitudaUsbProtocol
    {
        public const int ReportLength = 64;
        public const int HeaderLength = 4;
        public const int MaxEvents = (ReportLength - HeaderLength) / 2;
        public const int AdcMaxCode = 4095;

        // A sanity bound, deliberately loose: real frames carry about 10994 us.
        public const int MaxLiveMicros = 20000;

        // Returns false for a malformed frame; such a frame must be dropped as a whole
        // (neither its events nor its time are counted).
        public static bool TryDecode(byte[] report, int length, ushort[] codes, out int count, out int liveMicros)
        {
            count = 0;
            liveMicros = 0;
            if (report == null || codes == null || length != ReportLength ||
                report.Length < ReportLength || codes.Length < MaxEvents)
            {
                return false;
            }
            if (report[0] != 0)
            {
                return false;
            }
            int payload = report[1];
            if ((payload & 1) != 0 || payload > ReportLength - HeaderLength)
            {
                return false;
            }
            int live = report[2] | (report[3] << 8);
            if (live == 0 || live > MaxLiveMicros)
            {
                return false;
            }
            int events = payload / 2;
            for (int i = 0; i < events; i++)
            {
                int code = report[HeaderLength + 2 * i] | (report[HeaderLength + 2 * i + 1] << 8);
                if (code > AdcMaxCode)
                {
                    return false;
                }
                codes[i] = (ushort)code;
            }
            count = events;
            liveMicros = live;
            return true;
        }
    }
}
