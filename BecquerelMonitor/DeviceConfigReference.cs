namespace BecquerelMonitor
{
    // Token: 0x02000131 RID: 305
    public class DeviceConfigReference
    {
        // Token: 0x17000437 RID: 1079
        // (get) Token: 0x06000FDA RID: 4058 RVA: 0x000578C8 File Offset: 0x00055AC8
        // (set) Token: 0x06000FDB RID: 4059 RVA: 0x000578D0 File Offset: 0x00055AD0
        public string Name
        {
            get
            {
                return this.name;
            }
            set
            {
                this.name = value;
            }
        }

        // Token: 0x17000438 RID: 1080
        // (get) Token: 0x06000FDC RID: 4060 RVA: 0x000578DC File Offset: 0x00055ADC
        // (set) Token: 0x06000FDD RID: 4061 RVA: 0x000578E4 File Offset: 0x00055AE4
        public string Guid
        {
            get
            {
                return this.guid;
            }
            set
            {
                this.guid = value;
            }
        }

        // Token: 0x04000936 RID: 2358
        string name;

        // Token: 0x04000937 RID: 2359
        string guid;
    }
}
