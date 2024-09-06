namespace BecquerelMonitor
{
    public class AtomSpectraDeviceConfig : InputDeviceConfig
    {
        string com_port_name;
        int baud_rate = 600000;
        double deadTime = 0;

        public string ComPortName
        {
            get { return com_port_name; }
            set { this.com_port_name = value; }
        }

        public int BaudRate
        {
            get { return baud_rate; }
            set {baud_rate = value; }
        }

        public double DeadTimeValue
        {
            get { return deadTime; }
            set { this.deadTime = value; }
        }

        public AtomSpectraDeviceConfig()
        {

        }

        public AtomSpectraDeviceConfig(AtomSpectraDeviceConfig instance)
        {
            this.com_port_name = instance.com_port_name;
            this.baud_rate = instance.baud_rate;
            this.deadTime = instance.deadTime;
        }

        public override InputDeviceConfig Clone()
        {
            return new AtomSpectraDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return this.deadTime;
        }
    }
}
