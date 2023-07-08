namespace BecquerelMonitor
{
    // Token: 0x02000092 RID: 146
    public abstract class EnergyCalibration
    {
        // Token: 0x0600071F RID: 1823
        public abstract double ChannelToEnergy(double n);

        // Token: 0x06000720 RID: 1824
        public abstract double EnergyToChannel(double e, int maxChannels = 10000);

        // Token: 0x06000721 RID: 1825
        public abstract EnergyCalibration Clone();

        public abstract EnergyCalibration Downgrade(int p);

        // Token: 0x06000722 RID: 1826
        public abstract bool Equals(EnergyCalibration calib);
    }
}
