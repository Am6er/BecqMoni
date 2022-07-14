namespace WinMM
{
    // Token: 0x020001AB RID: 427
    public class WaveOutDeviceCaps
    {
        // Token: 0x1700066E RID: 1646
        // (get) Token: 0x0600156D RID: 5485 RVA: 0x0006C2D0 File Offset: 0x0006A4D0
        // (set) Token: 0x0600156E RID: 5486 RVA: 0x0006C2D8 File Offset: 0x0006A4D8
        public int DeviceId
        {
            get
            {
                return this.deviceId;
            }
            set
            {
                this.deviceId = value;
            }
        }

        // Token: 0x1700066F RID: 1647
        // (get) Token: 0x0600156F RID: 5487 RVA: 0x0006C2E4 File Offset: 0x0006A4E4
        // (set) Token: 0x06001570 RID: 5488 RVA: 0x0006C2EC File Offset: 0x0006A4EC
        public string Manufacturer
        {
            get
            {
                return this.manufacturer;
            }
            set
            {
                this.manufacturer = value;
            }
        }

        // Token: 0x17000670 RID: 1648
        // (get) Token: 0x06001571 RID: 5489 RVA: 0x0006C2F8 File Offset: 0x0006A4F8
        // (set) Token: 0x06001572 RID: 5490 RVA: 0x0006C300 File Offset: 0x0006A500
        public int ProductId
        {
            get
            {
                return this.productId;
            }
            set
            {
                this.productId = value;
            }
        }

        // Token: 0x17000671 RID: 1649
        // (get) Token: 0x06001573 RID: 5491 RVA: 0x0006C30C File Offset: 0x0006A50C
        // (set) Token: 0x06001574 RID: 5492 RVA: 0x0006C314 File Offset: 0x0006A514
        public int DriverVersion
        {
            get
            {
                return this.driverVersion;
            }
            set
            {
                this.driverVersion = value;
            }
        }

        // Token: 0x17000672 RID: 1650
        // (get) Token: 0x06001575 RID: 5493 RVA: 0x0006C320 File Offset: 0x0006A520
        // (set) Token: 0x06001576 RID: 5494 RVA: 0x0006C328 File Offset: 0x0006A528
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

        // Token: 0x17000673 RID: 1651
        // (get) Token: 0x06001577 RID: 5495 RVA: 0x0006C334 File Offset: 0x0006A534
        // (set) Token: 0x06001578 RID: 5496 RVA: 0x0006C33C File Offset: 0x0006A53C
        public int Channels
        {
            get
            {
                return this.channels;
            }
            set
            {
                this.channels = value;
            }
        }

        // Token: 0x17000674 RID: 1652
        // (get) Token: 0x06001579 RID: 5497 RVA: 0x0006C348 File Offset: 0x0006A548
        public bool SupportsPitch
        {
            get
            {
                return (this.Capabilities & NativeMethods.WAVECAPS.WAVECAPS_PITCH) != (NativeMethods.WAVECAPS)0;
            }
        }

        // Token: 0x17000675 RID: 1653
        // (get) Token: 0x0600157A RID: 5498 RVA: 0x0006C358 File Offset: 0x0006A558
        public bool SupportsPlaybackRate
        {
            get
            {
                return (this.Capabilities & NativeMethods.WAVECAPS.WAVECAPS_PLAYBACKRATE) != (NativeMethods.WAVECAPS)0;
            }
        }

        // Token: 0x17000676 RID: 1654
        // (get) Token: 0x0600157B RID: 5499 RVA: 0x0006C368 File Offset: 0x0006A568
        public bool SupportsVolume
        {
            get
            {
                return (this.Capabilities & NativeMethods.WAVECAPS.WAVECAPS_VOLUME) != (NativeMethods.WAVECAPS)0;
            }
        }

        // Token: 0x17000677 RID: 1655
        // (get) Token: 0x0600157C RID: 5500 RVA: 0x0006C378 File Offset: 0x0006A578
        public bool SupportsStereoVolume
        {
            get
            {
                return (this.Capabilities & NativeMethods.WAVECAPS.WAVECAPS_LRVOLUME) != (NativeMethods.WAVECAPS)0;
            }
        }

        // Token: 0x17000678 RID: 1656
        // (get) Token: 0x0600157D RID: 5501 RVA: 0x0006C388 File Offset: 0x0006A588
        // (set) Token: 0x0600157E RID: 5502 RVA: 0x0006C390 File Offset: 0x0006A590
        internal NativeMethods.WAVECAPS Capabilities
        {
            get
            {
                return this.capabilities;
            }
            set
            {
                this.capabilities = value;
            }
        }

        // Token: 0x04000C41 RID: 3137
        int deviceId;

        // Token: 0x04000C42 RID: 3138
        string manufacturer;

        // Token: 0x04000C43 RID: 3139
        int productId;

        // Token: 0x04000C44 RID: 3140
        int driverVersion;

        // Token: 0x04000C45 RID: 3141
        string name;

        // Token: 0x04000C46 RID: 3142
        int channels;

        // Token: 0x04000C47 RID: 3143
        NativeMethods.WAVECAPS capabilities;
    }
}
