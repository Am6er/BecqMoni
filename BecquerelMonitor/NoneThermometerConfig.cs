namespace BecquerelMonitor
{
    // Token: 0x020000D7 RID: 215
    public class NoneThermometerConfig : ThermometerConfig
    {
        // Token: 0x06000AEB RID: 2795 RVA: 0x000455FC File Offset: 0x000437FC
        public NoneThermometerConfig()
        {
        }

        // Token: 0x06000AEC RID: 2796 RVA: 0x00045604 File Offset: 0x00043804
        public NoneThermometerConfig(NoneThermometerConfig config)
        {
        }

        // Token: 0x06000AED RID: 2797 RVA: 0x0004560C File Offset: 0x0004380C
        public override ThermometerConfig Clone()
        {
            return new NoneThermometerConfig(this);
        }
    }
}
