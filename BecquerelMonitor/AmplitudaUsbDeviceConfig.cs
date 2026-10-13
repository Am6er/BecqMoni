namespace BecquerelMonitor
{
    // Settings of the Amplituda USB HID list-mode spectrometer ("Progress spectrometer").
    // The class name is the XML element name in device config files: never rename it.
    public class AmplitudaUsbDeviceConfig : InputDeviceConfig
    {
        public const string DeviceTypeId = "AmplitudaUSB";

        int vendorId = 0x534B;
        int productId = 0x0874;
        string serialNumber = "";
        int lowerThreshold = 0;
        int upperThreshold = AmplitudaUsbProtocol.AdcMaxCode;
        double deadTimeMicroseconds = 14.0;

        public int VendorId
        {
            get { return vendorId; }
            set { vendorId = value; }
        }

        public int ProductId
        {
            get { return productId; }
            set { productId = value; }
        }

        // Empty = the first device found.
        public string SerialNumber
        {
            get { return serialNumber; }
            set { serialNumber = value ?? ""; }
        }

        // Thresholds are ADC codes 0..4095, inclusive; events outside are counted as invalid.
        public int LowerThreshold
        {
            get { return lowerThreshold; }
            set { lowerThreshold = value; }
        }

        public int UpperThreshold
        {
            get { return upperThreshold; }
            set { upperThreshold = value; }
        }

        // The device subtracts this much live time per registered pulse.
        public double DeadTimeMicroseconds
        {
            get { return deadTimeMicroseconds; }
            set { deadTimeMicroseconds = value; }
        }

        public AmplitudaUsbDeviceConfig()
        {
        }

        public AmplitudaUsbDeviceConfig(AmplitudaUsbDeviceConfig instance)
        {
            vendorId = instance.vendorId;
            productId = instance.productId;
            serialNumber = instance.serialNumber;
            lowerThreshold = instance.lowerThreshold;
            upperThreshold = instance.upperThreshold;
            deadTimeMicroseconds = instance.deadTimeMicroseconds;
        }

        public override InputDeviceConfig Clone()
        {
            return new AmplitudaUsbDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return deadTimeMicroseconds * 1.0E-06;
        }
    }
}
