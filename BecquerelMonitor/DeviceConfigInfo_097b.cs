using System;
using System.Xml.Serialization;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x0200010A RID: 266
    [XmlRoot("DeviceConfigInfo")]
    public class DeviceConfigInfo_097b : IComparable, ICloneable
    {
        // Token: 0x170003AD RID: 941
        // (get) Token: 0x06000E6E RID: 3694 RVA: 0x000545DC File Offset: 0x000527DC
        // (set) Token: 0x06000E6F RID: 3695 RVA: 0x000545E4 File Offset: 0x000527E4
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

        // Token: 0x170003AE RID: 942
        // (get) Token: 0x06000E70 RID: 3696 RVA: 0x000545F0 File Offset: 0x000527F0
        // (set) Token: 0x06000E71 RID: 3697 RVA: 0x000545F8 File Offset: 0x000527F8
        [XmlIgnore]
        public string Filename
        {
            get
            {
                return this.filename;
            }
            set
            {
                this.filename = value;
            }
        }

        // Token: 0x170003AF RID: 943
        // (get) Token: 0x06000E72 RID: 3698 RVA: 0x00054604 File Offset: 0x00052804
        // (set) Token: 0x06000E73 RID: 3699 RVA: 0x0005460C File Offset: 0x0005280C
        [XmlIgnore]
        public string OriginalFilename
        {
            get
            {
                return this.originalFilename;
            }
            set
            {
                this.originalFilename = value;
            }
        }

        // Token: 0x170003B0 RID: 944
        // (get) Token: 0x06000E74 RID: 3700 RVA: 0x00054618 File Offset: 0x00052818
        // (set) Token: 0x06000E75 RID: 3701 RVA: 0x00054620 File Offset: 0x00052820
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

        // Token: 0x170003B1 RID: 945
        // (get) Token: 0x06000E76 RID: 3702 RVA: 0x0005462C File Offset: 0x0005282C
        // (set) Token: 0x06000E77 RID: 3703 RVA: 0x00054634 File Offset: 0x00052834
        public DateTime LastUpdated
        {
            get
            {
                return this.lastUpdated;
            }
            set
            {
                this.lastUpdated = value;
            }
        }

        // Token: 0x170003B2 RID: 946
        // (get) Token: 0x06000E78 RID: 3704 RVA: 0x00054640 File Offset: 0x00052840
        // (set) Token: 0x06000E79 RID: 3705 RVA: 0x00054648 File Offset: 0x00052848
        public WaveInDeviceCaps RecordingDevice
        {
            get
            {
                return this.recordingDevice;
            }
            set
            {
                this.recordingDevice = value;
            }
        }

        // Token: 0x170003B3 RID: 947
        // (get) Token: 0x06000E7A RID: 3706 RVA: 0x00054654 File Offset: 0x00052854
        // (set) Token: 0x06000E7B RID: 3707 RVA: 0x0005465C File Offset: 0x0005285C
        public int SamplesPerSecond
        {
            get
            {
                return this.samplesPerSecond;
            }
            set
            {
                this.samplesPerSecond = value;
            }
        }

        // Token: 0x170003B4 RID: 948
        // (get) Token: 0x06000E7C RID: 3708 RVA: 0x00054668 File Offset: 0x00052868
        // (set) Token: 0x06000E7D RID: 3709 RVA: 0x00054670 File Offset: 0x00052870
        public int BitsPerSample
        {
            get
            {
                return this.bitsPerSample;
            }
            set
            {
                this.bitsPerSample = value;
            }
        }

        // Token: 0x170003B5 RID: 949
        // (get) Token: 0x06000E7E RID: 3710 RVA: 0x0005467C File Offset: 0x0005287C
        // (set) Token: 0x06000E7F RID: 3711 RVA: 0x00054684 File Offset: 0x00052884
        public int Volume
        {
            get
            {
                return this.volume;
            }
            set
            {
                this.volume = value;
            }
        }

        // Token: 0x170003B6 RID: 950
        // (get) Token: 0x06000E80 RID: 3712 RVA: 0x00054690 File Offset: 0x00052890
        // (set) Token: 0x06000E81 RID: 3713 RVA: 0x00054698 File Offset: 0x00052898
        public bool AutoVolumeSetting
        {
            get
            {
                return this.autoVolumeSetting;
            }
            set
            {
                this.autoVolumeSetting = value;
            }
        }

        // Token: 0x170003B7 RID: 951
        // (get) Token: 0x06000E82 RID: 3714 RVA: 0x000546A4 File Offset: 0x000528A4
        // (set) Token: 0x06000E83 RID: 3715 RVA: 0x000546AC File Offset: 0x000528AC
        public bool NegativePolarity
        {
            get
            {
                return this.negativePolarity;
            }
            set
            {
                this.negativePolarity = value;
            }
        }

        // Token: 0x170003B8 RID: 952
        // (get) Token: 0x06000E84 RID: 3716 RVA: 0x000546B8 File Offset: 0x000528B8
        // (set) Token: 0x06000E85 RID: 3717 RVA: 0x000546C0 File Offset: 0x000528C0
        public int DefaultMeasurementTime
        {
            get
            {
                return this.defaultMeasurementTime;
            }
            set
            {
                this.defaultMeasurementTime = value;
            }
        }

        // Token: 0x170003B9 RID: 953
        // (get) Token: 0x06000E86 RID: 3718 RVA: 0x000546CC File Offset: 0x000528CC
        // (set) Token: 0x06000E87 RID: 3719 RVA: 0x000546D4 File Offset: 0x000528D4
        public double ChannelPitch
        {
            get
            {
                return this.channelPitch;
            }
            set
            {
                this.channelPitch = value;
            }
        }

        // Token: 0x170003BA RID: 954
        // (get) Token: 0x06000E88 RID: 3720 RVA: 0x000546E0 File Offset: 0x000528E0
        // (set) Token: 0x06000E89 RID: 3721 RVA: 0x000546E8 File Offset: 0x000528E8
        public int NumberOfChannels
        {
            get
            {
                return this.numberOfChannels;
            }
            set
            {
                this.numberOfChannels = value;
            }
        }

        // Token: 0x170003BB RID: 955
        // (get) Token: 0x06000E8A RID: 3722 RVA: 0x000546F4 File Offset: 0x000528F4
        // (set) Token: 0x06000E8B RID: 3723 RVA: 0x000546FC File Offset: 0x000528FC
        public string PulseDetectionMethod
        {
            get
            {
                return this.pulseDetectionMethod;
            }
            set
            {
                this.pulseDetectionMethod = value;
            }
        }

        // Token: 0x170003BC RID: 956
        // (get) Token: 0x06000E8C RID: 3724 RVA: 0x00054708 File Offset: 0x00052908
        // (set) Token: 0x06000E8D RID: 3725 RVA: 0x00054710 File Offset: 0x00052910
        public CDATA Note
        {
            get
            {
                return this.note;
            }
            set
            {
                this.note = value;
            }
        }

        // Token: 0x170003BD RID: 957
        // (get) Token: 0x06000E8E RID: 3726 RVA: 0x0005471C File Offset: 0x0005291C
        // (set) Token: 0x06000E8F RID: 3727 RVA: 0x00054724 File Offset: 0x00052924
        [XmlElement(typeof(PRAHomageMethodConfig))]
        public PulseDetectionMethodConfig PulseDetectionMethodConfig
        {
            get
            {
                return this.pulseDetectionMethodConfig;
            }
            set
            {
                this.pulseDetectionMethodConfig = value;
            }
        }

        // Token: 0x170003BE RID: 958
        // (get) Token: 0x06000E90 RID: 3728 RVA: 0x00054730 File Offset: 0x00052930
        // (set) Token: 0x06000E91 RID: 3729 RVA: 0x00054738 File Offset: 0x00052938
        public double EnergyCoefficient
        {
            get
            {
                return this.energyCoefficient;
            }
            set
            {
                this.energyCoefficient = value;
            }
        }

        // Token: 0x170003BF RID: 959
        // (get) Token: 0x06000E92 RID: 3730 RVA: 0x00054744 File Offset: 0x00052944
        // (set) Token: 0x06000E93 RID: 3731 RVA: 0x0005474C File Offset: 0x0005294C
        public double EnergyOffset
        {
            get
            {
                return this.energyOffset;
            }
            set
            {
                this.energyOffset = value;
            }
        }

        // Token: 0x170003C0 RID: 960
        // (get) Token: 0x06000E94 RID: 3732 RVA: 0x00054758 File Offset: 0x00052958
        // (set) Token: 0x06000E95 RID: 3733 RVA: 0x00054760 File Offset: 0x00052960
        [XmlElement(typeof(SimplePeakDetectionMethodConfig))]
        public PeakDetectionMethodConfig PeakDetectionMethodConfig
        {
            get
            {
                return this.peakDetectionMethodConfig;
            }
            set
            {
                this.peakDetectionMethodConfig = value;
            }
        }

        // Token: 0x170003C1 RID: 961
        // (get) Token: 0x06000E96 RID: 3734 RVA: 0x0005476C File Offset: 0x0005296C
        // (set) Token: 0x06000E97 RID: 3735 RVA: 0x00054774 File Offset: 0x00052974
        public string BackgroundSpectrumPathname
        {
            get
            {
                return this.backgroundSpectrumPathname;
            }
            set
            {
                this.backgroundSpectrumPathname = value;
            }
        }

        // Token: 0x170003C2 RID: 962
        // (get) Token: 0x06000E98 RID: 3736 RVA: 0x00054780 File Offset: 0x00052980
        // (set) Token: 0x06000E99 RID: 3737 RVA: 0x00054788 File Offset: 0x00052988
        [XmlIgnore]
        public bool Dirty
        {
            get
            {
                return this.dirty;
            }
            set
            {
                this.dirty = value;
            }
        }

        // Token: 0x06000E9A RID: 3738 RVA: 0x00054794 File Offset: 0x00052994
        public DeviceConfigInfo_097b()
        {
            this.pulseDetectionMethodConfig = new PRAHomageMethodConfig();
            this.peakDetectionMethodConfig = new SimplePeakDetectionMethodConfig();
        }

        // Token: 0x06000E9B RID: 3739 RVA: 0x00054864 File Offset: 0x00052A64
        public DeviceConfigReference CreateReference()
        {
            return new DeviceConfigReference
            {
                Name = this.name,
                Guid = this.guid
            };
        }

        // Token: 0x06000E9C RID: 3740 RVA: 0x00054894 File Offset: 0x00052A94
        int IComparable.CompareTo(object obj)
        {
            DeviceConfigInfo deviceConfigInfo = (DeviceConfigInfo)obj;
            return DateTime.Compare(deviceConfigInfo.LastUpdated, this.LastUpdated);
        }

        // Token: 0x06000E9D RID: 3741 RVA: 0x000548C0 File Offset: 0x00052AC0
        public object Clone()
        {
            DeviceConfigInfo deviceConfigInfo = new DeviceConfigInfo();
            return (DeviceConfigInfo)base.MemberwiseClone();
        }

        // Token: 0x04000841 RID: 2113
        string originalFilename = "";

        // Token: 0x04000842 RID: 2114
        bool dirty;

        // Token: 0x04000843 RID: 2115
        string guid;

        // Token: 0x04000844 RID: 2116
        string filename = "";

        // Token: 0x04000845 RID: 2117
        string name = "";

        // Token: 0x04000846 RID: 2118
        DateTime lastUpdated = DateTime.Now;

        // Token: 0x04000847 RID: 2119
        WaveInDeviceCaps recordingDevice;

        // Token: 0x04000848 RID: 2120
        int samplesPerSecond = 44100;

        // Token: 0x04000849 RID: 2121
        int bitsPerSample = 16;

        // Token: 0x0400084A RID: 2122
        int volume = 100;

        // Token: 0x0400084B RID: 2123
        bool autoVolumeSetting;

        // Token: 0x0400084C RID: 2124
        bool negativePolarity;

        // Token: 0x0400084D RID: 2125
        int defaultMeasurementTime = 3600;

        // Token: 0x0400084E RID: 2126
        double channelPitch = 0.04;

        // Token: 0x0400084F RID: 2127
        int numberOfChannels = 2500;

        // Token: 0x04000850 RID: 2128
        string pulseDetectionMethod = "PRA Homage Method";

        // Token: 0x04000851 RID: 2129
        CDATA note = "";

        // Token: 0x04000852 RID: 2130
        PulseDetectionMethodConfig pulseDetectionMethodConfig;

        // Token: 0x04000853 RID: 2131
        double energyCoefficient = 1.0;

        // Token: 0x04000854 RID: 2132
        double energyOffset;

        // Token: 0x04000855 RID: 2133
        PeakDetectionMethodConfig peakDetectionMethodConfig;

        // Token: 0x04000856 RID: 2134
        string backgroundSpectrumPathname = "";
    }
}
