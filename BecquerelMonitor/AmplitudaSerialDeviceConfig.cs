using System;

namespace BecquerelMonitor
{
    // Settings of a detector block on the RS-232 daisy chain of the "Progress" set.
    // The class name is the XML element name in device config files: never rename it.
    public class AmplitudaSerialDeviceConfig : InputDeviceConfig
    {
        public const string DeviceTypeId = "AmplitudaSerial";

        string portName = "";
        int address = 0;
        int pollSeconds = 3;
        int gapMilliseconds = 20;
        bool instructionShown = false;
        int hvSettleSeconds = 60;

        public string PortName
        {
            get { return portName; }
            set { portName = value ?? ""; }
        }

        // Address of the block on the line, 0..255.
        public int Address
        {
            get { return address; }
            set { address = Math.Max(0, Math.Min(255, value)); }
        }

        // One full read of a 1024-channel block takes 2.5 s.
        public int PollSeconds
        {
            get { return pollSeconds; }
            set { pollSeconds = Math.Max(2, Math.Min(60, value)); }
        }

        // Silence that marks the address byte; slow blocks may need more than 20 ms.
        public int GapMilliseconds
        {
            get { return gapMilliseconds; }
            set { gapMilliseconds = Math.Max(10, Math.Min(200, value)); }
        }

        // Whether the one-time behaviour instruction has already been shown for this config.
        // Lives here, not in the global settings, so it stays per device configuration.
        public bool InstructionShown
        {
            get { return instructionShown; }
            set { instructionShown = value; }
        }

        // How long AmplitudaSerialDeviceController refuses Start after a detected block reset
        // (spectrum protection), to keep a measurement from starting straight into the block's
        // ~1-minute high-voltage re-tuning. 0 switches the protection off.
        public int HvSettleSeconds
        {
            get { return hvSettleSeconds; }
            set { hvSettleSeconds = Math.Max(0, Math.Min(600, value)); }
        }

        public AmplitudaSerialDeviceConfig()
        {
        }

        public AmplitudaSerialDeviceConfig(AmplitudaSerialDeviceConfig instance)
        {
            portName = instance.portName;
            address = instance.address;
            pollSeconds = instance.pollSeconds;
            gapMilliseconds = instance.gapMilliseconds;
            instructionShown = instance.instructionShown;
            hvSettleSeconds = instance.hvSettleSeconds;
        }

        public override InputDeviceConfig Clone()
        {
            return new AmplitudaSerialDeviceConfig(this);
        }

        // 0 = "dead time unknown", the project's convention for serial devices
        // (SerialInputDeviceConfig.DeadTime). The readers of that zero are prepared for it: the
        // load counter divides by 1 - tau*n only when tau > 0 (CountsRateManager), and the FSA
        // cascade summing takes its own default coincidence window at zero
        // (FsaAnalysisSession.DeadTimeOf). A guessed value would silently widen that window.
        // This block reports its own live time, so nothing here needs a dead time.
        public override double DeadTime()
        {
            return 0.0;
        }

        public AmplitudaSerialSettings ToSettings()
        {
            return new AmplitudaSerialSettings { PortName = portName, BaudRate = 19200, GapMs = gapMilliseconds };
        }
    }
}
