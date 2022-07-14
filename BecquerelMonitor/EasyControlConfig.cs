using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200012F RID: 303
    public class EasyControlConfig
    {
        // Token: 0x17000421 RID: 1057
        // (get) Token: 0x06000FAC RID: 4012 RVA: 0x00057638 File Offset: 0x00055838
        // (set) Token: 0x06000FAD RID: 4013 RVA: 0x00057640 File Offset: 0x00055840
        [XmlIgnore]
        public DeviceConfigInfo DeviceConfig
        {
            get
            {
                return this.deviceConfig;
            }
            set
            {
                this.deviceConfig = value;
            }
        }

        // Token: 0x17000422 RID: 1058
        // (get) Token: 0x06000FAE RID: 4014 RVA: 0x0005764C File Offset: 0x0005584C
        // (set) Token: 0x06000FAF RID: 4015 RVA: 0x00057654 File Offset: 0x00055854
        public DeviceConfigReference DeviceConfigReference
        {
            get
            {
                return this.deviceConfigReference;
            }
            set
            {
                this.deviceConfigReference = value;
            }
        }

        // Token: 0x17000423 RID: 1059
        // (get) Token: 0x06000FB0 RID: 4016 RVA: 0x00057660 File Offset: 0x00055860
        // (set) Token: 0x06000FB1 RID: 4017 RVA: 0x00057668 File Offset: 0x00055868
        [XmlIgnore]
        public ROIConfigData ROIConfig
        {
            get
            {
                return this.roiConfig;
            }
            set
            {
                this.roiConfig = value;
            }
        }

        // Token: 0x17000424 RID: 1060
        // (get) Token: 0x06000FB2 RID: 4018 RVA: 0x00057674 File Offset: 0x00055874
        // (set) Token: 0x06000FB3 RID: 4019 RVA: 0x0005767C File Offset: 0x0005587C
        public ROIConfigReference ROIConfigReference
        {
            get
            {
                return this.roiConfigReference;
            }
            set
            {
                this.roiConfigReference = value;
            }
        }

        // Token: 0x17000425 RID: 1061
        // (get) Token: 0x06000FB4 RID: 4020 RVA: 0x00057688 File Offset: 0x00055888
        // (set) Token: 0x06000FB5 RID: 4021 RVA: 0x00057690 File Offset: 0x00055890
        public string SpectraFolder
        {
            get
            {
                return this.spectraFolder;
            }
            set
            {
                this.spectraFolder = value;
            }
        }

        // Token: 0x17000426 RID: 1062
        // (get) Token: 0x06000FB6 RID: 4022 RVA: 0x0005769C File Offset: 0x0005589C
        // (set) Token: 0x06000FB7 RID: 4023 RVA: 0x000576A4 File Offset: 0x000558A4
        public string EnergyCalibrationFilePrefix
        {
            get
            {
                return this.energyCalibrationFilePrefix;
            }
            set
            {
                this.energyCalibrationFilePrefix = value;
            }
        }

        // Token: 0x17000427 RID: 1063
        // (get) Token: 0x06000FB8 RID: 4024 RVA: 0x000576B0 File Offset: 0x000558B0
        // (set) Token: 0x06000FB9 RID: 4025 RVA: 0x000576B8 File Offset: 0x000558B8
        public string BackgroundFilePrefix
        {
            get
            {
                return this.backgroundFilePrefix;
            }
            set
            {
                this.backgroundFilePrefix = value;
            }
        }

        // Token: 0x17000428 RID: 1064
        // (get) Token: 0x06000FBA RID: 4026 RVA: 0x000576C4 File Offset: 0x000558C4
        // (set) Token: 0x06000FBB RID: 4027 RVA: 0x000576CC File Offset: 0x000558CC
        public string SampleFilePrefix
        {
            get
            {
                return this.sampleFilePrefix;
            }
            set
            {
                this.sampleFilePrefix = value;
            }
        }

        // Token: 0x17000429 RID: 1065
        // (get) Token: 0x06000FBC RID: 4028 RVA: 0x000576D8 File Offset: 0x000558D8
        // (set) Token: 0x06000FBD RID: 4029 RVA: 0x000576E0 File Offset: 0x000558E0
        public int WarmupTime
        {
            get
            {
                return this.warmupTime;
            }
            set
            {
                this.warmupTime = value;
            }
        }

        // Token: 0x1700042A RID: 1066
        // (get) Token: 0x06000FBE RID: 4030 RVA: 0x000576EC File Offset: 0x000558EC
        // (set) Token: 0x06000FBF RID: 4031 RVA: 0x000576F4 File Offset: 0x000558F4
        public int CalibrationTime
        {
            get
            {
                return this.calibrationTime;
            }
            set
            {
                this.calibrationTime = value;
            }
        }

        // Token: 0x1700042B RID: 1067
        // (get) Token: 0x06000FC0 RID: 4032 RVA: 0x00057700 File Offset: 0x00055900
        // (set) Token: 0x06000FC1 RID: 4033 RVA: 0x00057708 File Offset: 0x00055908
        public int ShutdownTime
        {
            get
            {
                return this.shutdownTime;
            }
            set
            {
                this.shutdownTime = value;
            }
        }

        // Token: 0x1700042C RID: 1068
        // (get) Token: 0x06000FC2 RID: 4034 RVA: 0x00057714 File Offset: 0x00055914
        // (set) Token: 0x06000FC3 RID: 4035 RVA: 0x0005771C File Offset: 0x0005591C
        public int DefaultBGPresetTime
        {
            get
            {
                return this.defaultBGPresetTime;
            }
            set
            {
                this.defaultBGPresetTime = value;
            }
        }

        // Token: 0x1700042D RID: 1069
        // (get) Token: 0x06000FC4 RID: 4036 RVA: 0x00057728 File Offset: 0x00055928
        // (set) Token: 0x06000FC5 RID: 4037 RVA: 0x00057730 File Offset: 0x00055930
        public int BGPresetTime1
        {
            get
            {
                return this.bgPresetTime1;
            }
            set
            {
                this.bgPresetTime1 = value;
            }
        }

        // Token: 0x1700042E RID: 1070
        // (get) Token: 0x06000FC6 RID: 4038 RVA: 0x0005773C File Offset: 0x0005593C
        // (set) Token: 0x06000FC7 RID: 4039 RVA: 0x00057744 File Offset: 0x00055944
        public int BGPresetTime2
        {
            get
            {
                return this.bgPresetTime2;
            }
            set
            {
                this.bgPresetTime2 = value;
            }
        }

        // Token: 0x1700042F RID: 1071
        // (get) Token: 0x06000FC8 RID: 4040 RVA: 0x00057750 File Offset: 0x00055950
        // (set) Token: 0x06000FC9 RID: 4041 RVA: 0x00057758 File Offset: 0x00055958
        public int BGPresetTime3
        {
            get
            {
                return this.bgPresetTime3;
            }
            set
            {
                this.bgPresetTime3 = value;
            }
        }

        // Token: 0x17000430 RID: 1072
        // (get) Token: 0x06000FCA RID: 4042 RVA: 0x00057764 File Offset: 0x00055964
        // (set) Token: 0x06000FCB RID: 4043 RVA: 0x0005776C File Offset: 0x0005596C
        public int BGPresetTime4
        {
            get
            {
                return this.bgPresetTime4;
            }
            set
            {
                this.bgPresetTime4 = value;
            }
        }

        // Token: 0x17000431 RID: 1073
        // (get) Token: 0x06000FCC RID: 4044 RVA: 0x00057778 File Offset: 0x00055978
        // (set) Token: 0x06000FCD RID: 4045 RVA: 0x00057780 File Offset: 0x00055980
        public int DefaultPresetTime
        {
            get
            {
                return this.defaultPresetTime;
            }
            set
            {
                this.defaultPresetTime = value;
            }
        }

        // Token: 0x17000432 RID: 1074
        // (get) Token: 0x06000FCE RID: 4046 RVA: 0x0005778C File Offset: 0x0005598C
        // (set) Token: 0x06000FCF RID: 4047 RVA: 0x00057794 File Offset: 0x00055994
        public int PresetTime1
        {
            get
            {
                return this.presetTime1;
            }
            set
            {
                this.presetTime1 = value;
            }
        }

        // Token: 0x17000433 RID: 1075
        // (get) Token: 0x06000FD0 RID: 4048 RVA: 0x000577A0 File Offset: 0x000559A0
        // (set) Token: 0x06000FD1 RID: 4049 RVA: 0x000577A8 File Offset: 0x000559A8
        public int PresetTime2
        {
            get
            {
                return this.presetTime2;
            }
            set
            {
                this.presetTime2 = value;
            }
        }

        // Token: 0x17000434 RID: 1076
        // (get) Token: 0x06000FD2 RID: 4050 RVA: 0x000577B4 File Offset: 0x000559B4
        // (set) Token: 0x06000FD3 RID: 4051 RVA: 0x000577BC File Offset: 0x000559BC
        public int PresetTime3
        {
            get
            {
                return this.presetTime3;
            }
            set
            {
                this.presetTime3 = value;
            }
        }

        // Token: 0x17000435 RID: 1077
        // (get) Token: 0x06000FD4 RID: 4052 RVA: 0x000577C8 File Offset: 0x000559C8
        // (set) Token: 0x06000FD5 RID: 4053 RVA: 0x000577D0 File Offset: 0x000559D0
        public int PresetTime4
        {
            get
            {
                return this.presetTime4;
            }
            set
            {
                this.presetTime4 = value;
            }
        }

        // Token: 0x04000920 RID: 2336
        DeviceConfigInfo deviceConfig;

        // Token: 0x04000921 RID: 2337
        DeviceConfigReference deviceConfigReference;

        // Token: 0x04000922 RID: 2338
        ROIConfigData roiConfig;

        // Token: 0x04000923 RID: 2339
        ROIConfigReference roiConfigReference;

        // Token: 0x04000924 RID: 2340
        string spectraFolder;

        // Token: 0x04000925 RID: 2341
        string energyCalibrationFilePrefix = "EnergyCalibration";

        // Token: 0x04000926 RID: 2342
        string backgroundFilePrefix = "Background";

        // Token: 0x04000927 RID: 2343
        string sampleFilePrefix = "Sample";

        // Token: 0x04000928 RID: 2344
        int warmupTime = 600;

        // Token: 0x04000929 RID: 2345
        int calibrationTime = 600;

        // Token: 0x0400092A RID: 2346
        int shutdownTime = 5;

        // Token: 0x0400092B RID: 2347
        int defaultBGPresetTime = 3600;

        // Token: 0x0400092C RID: 2348
        int bgPresetTime1 = 600;

        // Token: 0x0400092D RID: 2349
        int bgPresetTime2 = 1800;

        // Token: 0x0400092E RID: 2350
        int bgPresetTime3 = 3600;

        // Token: 0x0400092F RID: 2351
        int bgPresetTime4 = 7200;

        // Token: 0x04000930 RID: 2352
        int defaultPresetTime = 600;

        // Token: 0x04000931 RID: 2353
        int presetTime1 = 300;

        // Token: 0x04000932 RID: 2354
        int presetTime2 = 600;

        // Token: 0x04000933 RID: 2355
        int presetTime3 = 1800;

        // Token: 0x04000934 RID: 2356
        int presetTime4 = 3600;
    }
}
