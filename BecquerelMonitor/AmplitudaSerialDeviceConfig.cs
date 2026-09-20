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
        double deadTimeMicroseconds = 95.0;
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

        // Only for the places where BecqMoni recomputes live time by formula (pulse rebuild,
        // device re-assignment). During a measurement live time comes from the block.
        public double DeadTimeMicroseconds
        {
            get { return deadTimeMicroseconds; }
            set { deadTimeMicroseconds = value; }
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
            deadTimeMicroseconds = instance.deadTimeMicroseconds;
            instructionShown = instance.instructionShown;
            hvSettleSeconds = instance.hvSettleSeconds;
        }

        public override InputDeviceConfig Clone()
        {
            return new AmplitudaSerialDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return deadTimeMicroseconds * 1.0E-06;
        }

        public AmplitudaSerialSettings ToSettings()
        {
            return new AmplitudaSerialSettings { PortName = portName, BaudRate = 19200, GapMs = gapMilliseconds };
        }
    }
}
