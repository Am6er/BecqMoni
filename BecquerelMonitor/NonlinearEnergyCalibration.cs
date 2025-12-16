using System;

namespace BecquerelMonitor
{
    // Token: 0x02000093 RID: 147
    public class NonlinearEnergyCalibration : EnergyCalibration
    {
        // Token: 0x06000725 RID: 1829 RVA: 0x00029CB8 File Offset: 0x00027EB8
        public NonlinearEnergyCalibration()
        {
        }

        // Token: 0x06000727 RID: 1831 RVA: 0x00029CC8 File Offset: 0x00027EC8
        public override double ChannelToEnergy(double n)
        {
            throw new NotImplementedException();
        }

        // Token: 0x06000728 RID: 1832 RVA: 0x00029CD0 File Offset: 0x00027ED0
        public override double EnergyToChannel(double e, int maxChannels = 10000)
        {
            throw new NotImplementedException();
        }

        // Token: 0x06000729 RID: 1833 RVA: 0x00029CD8 File Offset: 0x00027ED8
        public NonlinearEnergyCalibration(NonlinearEnergyCalibration calib)
        {
        }

        // Token: 0x0600072A RID: 1834 RVA: 0x00029CE0 File Offset: 0x00027EE0
        public override bool Equals(EnergyCalibration calib)
        {
            throw new NotImplementedException();
        }

        // Token: 0x0600072B RID: 1835 RVA: 0x00029CE8 File Offset: 0x00027EE8
        public override EnergyCalibration Clone()
        {
            return new NonlinearEnergyCalibration(this);
        }

        public override EnergyCalibration Downgrade(int p)
        {
            throw new NotImplementedException();
        }

        public override int MaxChannels()
        {
            throw new NotImplementedException();
        }
    }
}
