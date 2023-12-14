namespace BecquerelMonitor
{
    public class RadiaCodeDeviceConfig : InputDeviceConfig
    {
        string device_serial;
        string address_ble;

        public string DeviceSerial
        {
            get { return device_serial; }
            set { this.device_serial = value; }
        }

        public string AddressBLE
        {
            get { return address_ble; }
            set { this.address_ble = value; }
        }

        public RadiaCodeDeviceConfig()
        {

        }

        public RadiaCodeDeviceConfig(RadiaCodeDeviceConfig instance)
        {
            this.device_serial = instance.device_serial;
            this.address_ble= instance.address_ble;
        }

        public override InputDeviceConfig Clone()
        {
            return new RadiaCodeDeviceConfig(this);
        }
    }
}
