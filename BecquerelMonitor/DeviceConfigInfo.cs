using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x020000FA RID: 250
    public class DeviceConfigInfo : IComparable
    {
        // Token: 0x17000327 RID: 807
        // (get) Token: 0x06000BF9 RID: 3065 RVA: 0x00048094 File Offset: 0x00046294
        // (set) Token: 0x06000BFA RID: 3066 RVA: 0x0004809C File Offset: 0x0004629C
        public string FormatVersion
        {
            get
            {
                return this.formatVersion;
            }
            set
            {
                this.formatVersion = value;
            }
        }

        // Token: 0x17000328 RID: 808
        // (get) Token: 0x06000BFB RID: 3067 RVA: 0x000480A8 File Offset: 0x000462A8
        // (set) Token: 0x06000BFC RID: 3068 RVA: 0x000480B0 File Offset: 0x000462B0
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

        // Token: 0x17000329 RID: 809
        // (get) Token: 0x06000BFD RID: 3069 RVA: 0x000480BC File Offset: 0x000462BC
        // (set) Token: 0x06000BFE RID: 3070 RVA: 0x000480C4 File Offset: 0x000462C4
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

        // Token: 0x1700032A RID: 810
        // (get) Token: 0x06000BFF RID: 3071 RVA: 0x000480D0 File Offset: 0x000462D0
        // (set) Token: 0x06000C00 RID: 3072 RVA: 0x000480D8 File Offset: 0x000462D8
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

        // Token: 0x1700032B RID: 811
        // (get) Token: 0x06000C01 RID: 3073 RVA: 0x000480E4 File Offset: 0x000462E4
        // (set) Token: 0x06000C02 RID: 3074 RVA: 0x000480EC File Offset: 0x000462EC
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

        // Token: 0x1700032C RID: 812
        // (get) Token: 0x06000C03 RID: 3075 RVA: 0x000480F8 File Offset: 0x000462F8
        // (set) Token: 0x06000C04 RID: 3076 RVA: 0x00048100 File Offset: 0x00046300
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

        // Token: 0x1700032D RID: 813
        // (get) Token: 0x06000C05 RID: 3077 RVA: 0x0004810C File Offset: 0x0004630C
        // (set) Token: 0x06000C06 RID: 3078 RVA: 0x00048114 File Offset: 0x00046314
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

        // Token: 0x1700032E RID: 814
        // (get) Token: 0x06000C07 RID: 3079 RVA: 0x00048120 File Offset: 0x00046320
        // (set) Token: 0x06000C08 RID: 3080 RVA: 0x00048128 File Offset: 0x00046328
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

        // Token: 0x1700032F RID: 815
        // (get) Token: 0x06000C09 RID: 3081 RVA: 0x00048134 File Offset: 0x00046334
        // (set) Token: 0x06000C0A RID: 3082 RVA: 0x0004813C File Offset: 0x0004633C
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

        // Token: 0x17000330 RID: 816
        // (get) Token: 0x06000C0B RID: 3083 RVA: 0x00048148 File Offset: 0x00046348
        // (set) Token: 0x06000C0C RID: 3084 RVA: 0x00048150 File Offset: 0x00046350
        public string DeviceType
        {
            get
            {
                return this.deviceType;
            }
            set
            {
                this.deviceType = value;
            }
        }

        // Token: 0x17000331 RID: 817
        // (get) Token: 0x06000C0D RID: 3085 RVA: 0x0004815C File Offset: 0x0004635C
        // (set) Token: 0x06000C0E RID: 3086 RVA: 0x00048164 File Offset: 0x00046364
        public string ThermometerType
        {
            get
            {
                return this.thermometerType;
            }
            set
            {
                this.thermometerType = value;
            }
        }

        // Token: 0x17000332 RID: 818
        // (get) Token: 0x06000C0F RID: 3087 RVA: 0x00048170 File Offset: 0x00046370
        // (set) Token: 0x06000C10 RID: 3088 RVA: 0x00048178 File Offset: 0x00046378
        public string EnergyCalibrationType
        {
            get
            {
                return this.energyCalibrationType;
            }
            set
            {
                this.energyCalibrationType = value;
            }
        }

        // Token: 0x17000333 RID: 819
        // (get) Token: 0x06000C11 RID: 3089 RVA: 0x00048184 File Offset: 0x00046384
        // (set) Token: 0x06000C12 RID: 3090 RVA: 0x0004818C File Offset: 0x0004638C
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

        // Token: 0x17000334 RID: 820
        // (get) Token: 0x06000C13 RID: 3091 RVA: 0x00048198 File Offset: 0x00046398
        // (set) Token: 0x06000C14 RID: 3092 RVA: 0x000481A0 File Offset: 0x000463A0
        [XmlElement(typeof(SerialInputDeviceConfig))]
        [XmlElement(typeof(AudioInputDeviceConfig))]
        [XmlElement(typeof(AtomSpectraDeviceConfig))]
        public InputDeviceConfig InputDeviceConfig
        {
            get
            {
                return this.inputDeviceConfig;
            }
            set
            {
                this.inputDeviceConfig = value;
            }
        }

        // Token: 0x17000335 RID: 821
        // (get) Token: 0x06000C15 RID: 3093 RVA: 0x000481AC File Offset: 0x000463AC
        // (set) Token: 0x06000C16 RID: 3094 RVA: 0x000481B4 File Offset: 0x000463B4
        [XmlElement(typeof(BuiltinThermometerConfig))]
        [XmlElement(typeof(USBThermometerConfig))]
        public ThermometerConfig ThermometerConfig
        {
            get
            {
                return this.thermometerConfig;
            }
            set
            {
                this.thermometerConfig = value;
            }
        }

        // Token: 0x17000336 RID: 822
        // (get) Token: 0x06000C17 RID: 3095 RVA: 0x000481C0 File Offset: 0x000463C0
        // (set) Token: 0x06000C18 RID: 3096 RVA: 0x000481C8 File Offset: 0x000463C8
        [XmlElement(typeof(NonlinearEnergyCalibration))]
        [XmlElement(typeof(PolynomialEnergyCalibration))]
        public EnergyCalibration EnergyCalibration
        {
            get
            {
                return this.energyCalibration;
            }
            set
            {
                this.energyCalibration = value;
            }
        }

        // Token: 0x17000337 RID: 823
        // (get) Token: 0x06000C19 RID: 3097 RVA: 0x000481D4 File Offset: 0x000463D4
        // (set) Token: 0x06000C1A RID: 3098 RVA: 0x000481DC File Offset: 0x000463DC
        public StabilizerConfig StabilizerConfig
        {
            get
            {
                return this.stabilizerConfig;
            }
            set
            {
                this.stabilizerConfig = value;
            }
        }

        // Token: 0x17000338 RID: 824
        // (get) Token: 0x06000C1B RID: 3099 RVA: 0x000481E8 File Offset: 0x000463E8
        // (set) Token: 0x06000C1C RID: 3100 RVA: 0x000481F0 File Offset: 0x000463F0
        [XmlElement(typeof(DoseRateConfig))]
        public DoseRateConfig DoseRateConfig
        {
            get
            {
                return this.doseRateConfig;
            }
            set
            {
                this.doseRateConfig = value;
            }
        }

        // Token: 0x17000339 RID: 825
        // (get) Token: 0x06000C1D RID: 3101 RVA: 0x000481FC File Offset: 0x000463FC
        // (set) Token: 0x06000C1E RID: 3102 RVA: 0x00048204 File Offset: 0x00046404
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

        // Token: 0x1700033A RID: 826
        // (get) Token: 0x06000C1F RID: 3103 RVA: 0x00048210 File Offset: 0x00046410
        // (set) Token: 0x06000C20 RID: 3104 RVA: 0x00048218 File Offset: 0x00046418
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

        // Token: 0x1700033B RID: 827
        // (get) Token: 0x06000C21 RID: 3105 RVA: 0x00048224 File Offset: 0x00046424
        // (set) Token: 0x06000C22 RID: 3106 RVA: 0x0004822C File Offset: 0x0004642C
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

        // Token: 0x06000C23 RID: 3107 RVA: 0x00048238 File Offset: 0x00046438
        public DeviceConfigInfo()
        {
            this.inputDeviceConfig = new AudioInputDeviceConfig();
            this.energyCalibration = new PolynomialEnergyCalibration();
            this.doseRateConfig = new DoseRateConfig();
            this.stabilizerConfig = new StabilizerConfig();
            this.peakDetectionMethodConfig = new SimplePeakDetectionMethodConfig();
        }

        // Token: 0x06000C24 RID: 3108 RVA: 0x00048320 File Offset: 0x00046520
        public void InitFormatVersion()
        {
            this.formatVersion = "120920";
        }

        // Token: 0x06000C25 RID: 3109 RVA: 0x00048330 File Offset: 0x00046530
        public DeviceConfigInfo(DeviceConfigInfo info)
        {
            this.originalFilename = string.Copy(info.originalFilename);
            this.formatVersion = string.Copy(info.formatVersion);
            this.guid = string.Copy(info.guid);
            this.filename = string.Copy(info.filename);
            this.name = string.Copy(info.name);
            this.lastUpdated = info.lastUpdated;
            this.defaultMeasurementTime = info.defaultMeasurementTime;
            this.numberOfChannels = info.numberOfChannels;
            this.channelPitch = info.ChannelPitch;
            this.deviceType = string.Copy(info.deviceType);
            this.thermometerType = string.Copy(info.thermometerType);
            this.energyCalibrationType = string.Copy(info.energyCalibrationType);
            this.note = string.Copy(info.note);
            this.inputDeviceConfig = info.inputDeviceConfig.Clone();
            if (info.thermometerConfig != null)
            {
                this.thermometerConfig = info.thermometerConfig.Clone();
            }
            this.energyCalibration = info.energyCalibration.Clone();
            if (info.doseRateConfig != null)
            {
                this.doseRateConfig = info.doseRateConfig.Clone();
            }
            if (info.stabilizerConfig != null)
            {
                this.stabilizerConfig = info.stabilizerConfig.Clone();
            }
            this.peakDetectionMethodConfig = info.peakDetectionMethodConfig.Clone();
            this.backgroundSpectrumPathname = string.Copy(info.backgroundSpectrumPathname);
        }

        // Token: 0x06000C26 RID: 3110 RVA: 0x0004854C File Offset: 0x0004674C
        public DeviceConfigInfo(DeviceConfigInfo_097b old)
        {
            this.formatVersion = "120920";
            this.originalFilename = old.OriginalFilename;
            this.guid = old.Guid;
            this.filename = old.Filename;
            this.name = old.Name;
            this.lastUpdated = old.LastUpdated;
            this.defaultMeasurementTime = old.DefaultMeasurementTime;
            this.numberOfChannels = old.NumberOfChannels;
            this.channelPitch = old.ChannelPitch;
            this.deviceType = "AudioInputDevice";
            this.thermometerType = "None";
            this.energyCalibrationType = "Polynomial";
            this.note = old.Note;
            AudioInputDeviceConfig audioInputDeviceConfig = new AudioInputDeviceConfig();
            this.inputDeviceConfig = audioInputDeviceConfig;
            audioInputDeviceConfig.AudioInputDevice = old.RecordingDevice;
            audioInputDeviceConfig.SamplesPerSecond = old.SamplesPerSecond;
            audioInputDeviceConfig.BitsPerSample = old.BitsPerSample;
            audioInputDeviceConfig.Volume = old.Volume;
            audioInputDeviceConfig.AutoVolumeSetting = old.AutoVolumeSetting;
            audioInputDeviceConfig.NegativePolarity = old.NegativePolarity;
            audioInputDeviceConfig.PulseDetectionMethodConfig = old.PulseDetectionMethodConfig;
            this.thermometerConfig = null;
            PolynomialEnergyCalibration polynomialEnergyCalibration = new PolynomialEnergyCalibration();
            polynomialEnergyCalibration.PolynomialOrder = 2;
            PolynomialEnergyCalibration polynomialEnergyCalibration2 = polynomialEnergyCalibration;
            double[] array = new double[3];
            array[0] = old.EnergyOffset;
            array[1] = old.EnergyCoefficient;
            polynomialEnergyCalibration2.Coefficients = array;
            this.energyCalibration = polynomialEnergyCalibration;
            this.peakDetectionMethodConfig = old.PeakDetectionMethodConfig;
            this.backgroundSpectrumPathname = old.BackgroundSpectrumPathname;
        }

        // Token: 0x06000C27 RID: 3111 RVA: 0x00048748 File Offset: 0x00046948
        public DeviceConfigReference CreateReference()
        {
            return new DeviceConfigReference
            {
                Name = this.name,
                Guid = this.guid
            };
        }

        // Token: 0x06000C28 RID: 3112 RVA: 0x00048778 File Offset: 0x00046978
        int IComparable.CompareTo(object obj)
        {
            DeviceConfigInfo deviceConfigInfo = (DeviceConfigInfo)obj;
            return DateTime.Compare(deviceConfigInfo.LastUpdated, this.LastUpdated);
        }

        // Token: 0x06000C29 RID: 3113 RVA: 0x000487A4 File Offset: 0x000469A4
        public DeviceConfigInfo Clone()
        {
            return new DeviceConfigInfo(this);
        }

        // Token: 0x0400078D RID: 1933
        const string formatVersionString = "120920";

        // Token: 0x0400078E RID: 1934
        string formatVersion = "";

        // Token: 0x0400078F RID: 1935
        string originalFilename = "";

        // Token: 0x04000790 RID: 1936
        bool dirty;

        // Token: 0x04000791 RID: 1937
        string guid;

        // Token: 0x04000792 RID: 1938
        string filename = "";

        // Token: 0x04000793 RID: 1939
        string name = "";

        // Token: 0x04000794 RID: 1940
        DateTime lastUpdated = DateTime.Now;

        // Token: 0x04000795 RID: 1941
        int defaultMeasurementTime = 3600;

        // Token: 0x04000796 RID: 1942
        double channelPitch = 0.04;

        // Token: 0x04000797 RID: 1943
        int numberOfChannels = 2500;

        // Token: 0x04000798 RID: 1944
        string deviceType = "AudioInputDevice";

        // Token: 0x04000799 RID: 1945
        string thermometerType = "None";

        // Token: 0x0400079A RID: 1946
        string energyCalibrationType = "Polynomial";

        // Token: 0x0400079B RID: 1947
        CDATA note = "";

        // Token: 0x0400079C RID: 1948
        InputDeviceConfig inputDeviceConfig;

        // Token: 0x0400079D RID: 1949
        ThermometerConfig thermometerConfig;

        // Token: 0x0400079E RID: 1950
        EnergyCalibration energyCalibration;

        // Token: 0x0400079F RID: 1951
        StabilizerConfig stabilizerConfig;

        // Token: 0x040007A0 RID: 1952
        DoseRateConfig doseRateConfig;

        // Token: 0x040007A1 RID: 1953
        PeakDetectionMethodConfig peakDetectionMethodConfig;

        // Token: 0x040007A2 RID: 1954
        string backgroundSpectrumPathname = "";
    }
}
