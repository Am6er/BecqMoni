namespace BecquerelMonitor
{
    public class AtomSpectraDeviceConfig : InputDeviceConfig
    {
        string com_port_name;

        public string ComPortName
        {
            get { return com_port_name; }
            set { this.com_port_name = value; }
        }

        public AtomSpectraDeviceConfig()
        {

        }

        public AtomSpectraDeviceConfig(AtomSpectraDeviceConfig instance)
        {
            this.com_port_name = instance.com_port_name;
        }

        public override InputDeviceConfig Clone()
        {
            return new AtomSpectraDeviceConfig(this);
        }
    }
}
