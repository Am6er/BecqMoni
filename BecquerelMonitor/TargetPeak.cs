namespace BecquerelMonitor
{
    // Token: 0x02000079 RID: 121
    public class TargetPeak
    {
        // Token: 0x170001D3 RID: 467
        // (get) Token: 0x06000613 RID: 1555 RVA: 0x00026684 File Offset: 0x00024884
        // (set) Token: 0x06000614 RID: 1556 RVA: 0x0002668C File Offset: 0x0002488C
        public string Nuclide
        {
            get
            {
                return this.nuclide;
            }
            set
            {
                this.nuclide = value;
            }
        }

        // Token: 0x170001D4 RID: 468
        // (get) Token: 0x06000615 RID: 1557 RVA: 0x00026698 File Offset: 0x00024898
        // (set) Token: 0x06000616 RID: 1558 RVA: 0x000266A0 File Offset: 0x000248A0
        public decimal Energy
        {
            get
            {
                return this.energy;
            }
            set
            {
                this.energy = value;
            }
        }

        // Token: 0x170001D5 RID: 469
        // (get) Token: 0x06000617 RID: 1559 RVA: 0x000266AC File Offset: 0x000248AC
        // (set) Token: 0x06000618 RID: 1560 RVA: 0x000266B4 File Offset: 0x000248B4
        public decimal Error
        {
            get
            {
                return this.error;
            }
            set
            {
                this.error = value;
            }
        }

        // Token: 0x04000336 RID: 822
        string nuclide;

        // Token: 0x04000337 RID: 823
        decimal energy;

        // Token: 0x04000338 RID: 824
        decimal error;
    }
}
