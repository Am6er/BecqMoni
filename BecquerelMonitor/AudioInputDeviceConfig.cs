using System.Xml.Serialization;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x0200003F RID: 63
    public class AudioInputDeviceConfig : InputDeviceConfig
    {
        // Token: 0x1700014C RID: 332
        // (get) Token: 0x0600037A RID: 890 RVA: 0x000112B0 File Offset: 0x0000F4B0
        // (set) Token: 0x0600037B RID: 891 RVA: 0x000112B8 File Offset: 0x0000F4B8
        public WaveInDeviceCaps AudioInputDevice
        {
            get
            {
                return this.audioInputDevice;
            }
            set
            {
                this.audioInputDevice = value;
            }
        }

        // Token: 0x1700014D RID: 333
        // (get) Token: 0x0600037C RID: 892 RVA: 0x000112C4 File Offset: 0x0000F4C4
        // (set) Token: 0x0600037D RID: 893 RVA: 0x000112CC File Offset: 0x0000F4CC
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

        // Token: 0x1700014E RID: 334
        // (get) Token: 0x0600037E RID: 894 RVA: 0x000112D8 File Offset: 0x0000F4D8
        // (set) Token: 0x0600037F RID: 895 RVA: 0x000112E0 File Offset: 0x0000F4E0
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

        // Token: 0x1700014F RID: 335
        // (get) Token: 0x06000380 RID: 896 RVA: 0x000112EC File Offset: 0x0000F4EC
        // (set) Token: 0x06000381 RID: 897 RVA: 0x000112F4 File Offset: 0x0000F4F4
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

        // Token: 0x17000150 RID: 336
        // (get) Token: 0x06000382 RID: 898 RVA: 0x00011300 File Offset: 0x0000F500
        // (set) Token: 0x06000383 RID: 899 RVA: 0x00011308 File Offset: 0x0000F508
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

        // Token: 0x17000151 RID: 337
        // (get) Token: 0x06000384 RID: 900 RVA: 0x00011314 File Offset: 0x0000F514
        // (set) Token: 0x06000385 RID: 901 RVA: 0x0001131C File Offset: 0x0000F51C
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

        // Token: 0x17000152 RID: 338
        // (get) Token: 0x06000386 RID: 902 RVA: 0x00011328 File Offset: 0x0000F528
        // (set) Token: 0x06000387 RID: 903 RVA: 0x00011330 File Offset: 0x0000F530
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

        // Token: 0x17000153 RID: 339
        // (get) Token: 0x06000388 RID: 904 RVA: 0x0001133C File Offset: 0x0000F53C
        // (set) Token: 0x06000389 RID: 905 RVA: 0x00011344 File Offset: 0x0000F544
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

        // Token: 0x0600038A RID: 906 RVA: 0x00011350 File Offset: 0x0000F550
        public AudioInputDeviceConfig()
        {
        }

        // Token: 0x0600038B RID: 907 RVA: 0x0001138C File Offset: 0x0000F58C
        public AudioInputDeviceConfig(AudioInputDeviceConfig config)
        {
            this.audioInputDevice = config.audioInputDevice;
            this.samplesPerSecond = config.samplesPerSecond;
            this.bitsPerSample = config.bitsPerSample;
            this.volume = config.volume;
            this.autoVolumeSetting = config.autoVolumeSetting;
            this.negativePolarity = config.negativePolarity;
            this.pulseDetectionMethodConfig = config.PulseDetectionMethodConfig.Clone();
        }

        // Token: 0x0600038C RID: 908 RVA: 0x00011430 File Offset: 0x0000F630
        public override InputDeviceConfig Clone()
        {
            return new AudioInputDeviceConfig(this);
        }

        public override double DeadTime()
        {
            PRAHomageMethodConfig pRAHomageMethodConfig = (PRAHomageMethodConfig)this.PulseDetectionMethodConfig;
            return (double)pRAHomageMethodConfig.PulseShapeSize / (double)this.samplesPerSecond;
        }

        // Token: 0x04000163 RID: 355
        WaveInDeviceCaps audioInputDevice;

        // Token: 0x04000164 RID: 356
        int samplesPerSecond = 44100;

        // Token: 0x04000165 RID: 357
        int bitsPerSample = 16;

        // Token: 0x04000166 RID: 358
        int volume = 100;

        // Token: 0x04000167 RID: 359
        bool autoVolumeSetting;

        // Token: 0x04000168 RID: 360
        bool negativePolarity;

        // Token: 0x04000169 RID: 361
        string pulseDetectionMethod = "PRA Homage Method";

        // Token: 0x0400016A RID: 362
        PulseDetectionMethodConfig pulseDetectionMethodConfig = new PRAHomageMethodConfig();
    }
}
