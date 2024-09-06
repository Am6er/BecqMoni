namespace BecquerelMonitor
{
    public class RadiaCodeDeviceConfig : InputDeviceConfig
    {
        string device_serial;
        string address_ble;
        PolynomialEnergyCalibration rc_energy_calibration;

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

        public PolynomialEnergyCalibration RC_EnergyCalibration
        {
            get { return rc_energy_calibration; }
            set { this.rc_energy_calibration = value; }
        }

        public RadiaCodeDeviceConfig()
        {

        }

        public RadiaCodeDeviceConfig(RadiaCodeDeviceConfig instance)
        {
            this.device_serial = instance.device_serial;
            this.address_ble = instance.address_ble;
            this.rc_energy_calibration = instance.rc_energy_calibration;
        }

        public override InputDeviceConfig Clone()
        {
            return new RadiaCodeDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return 5.0E-06;
        }
    }
}
