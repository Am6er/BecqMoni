namespace BecquerelMonitor
{
    public class ObsidianDeviceConfig : InputDeviceConfig
    {
        private string device_serial;
        private string address_ble;

        public string DeviceSerial
        {
            get { return device_serial; }
            set { device_serial = value; }
        }

        public string AddressBLE
        {
            get { return address_ble; }
            set { address_ble = value; }
        }

        public ObsidianDeviceConfig()
        {
        }

        public ObsidianDeviceConfig(ObsidianDeviceConfig instance)
        {
            device_serial = instance.device_serial;
            address_ble = instance.address_ble;
        }

        public override InputDeviceConfig Clone()
        {
            return new ObsidianDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return 5.0E-06;
        }
    }
}
