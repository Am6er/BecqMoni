using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200002D RID: 45
    [XmlRoot("ResultData")]
    public class ResultData_093b
    {
        // Token: 0x1700010F RID: 271
        // (get) Token: 0x06000247 RID: 583 RVA: 0x000092C4 File Offset: 0x000074C4
        // (set) Token: 0x06000248 RID: 584 RVA: 0x000092CC File Offset: 0x000074CC
        public SampleInfoData SampleInfo
        {
            get
            {
                return this.sampleInfo;
            }
            set
            {
                this.sampleInfo = value;
            }
        }

        // Token: 0x17000110 RID: 272
        // (get) Token: 0x06000249 RID: 585 RVA: 0x000092D8 File Offset: 0x000074D8
        // (set) Token: 0x0600024A RID: 586 RVA: 0x000092E0 File Offset: 0x000074E0
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

        // Token: 0x17000111 RID: 273
        // (get) Token: 0x0600024B RID: 587 RVA: 0x000092EC File Offset: 0x000074EC
        // (set) Token: 0x0600024C RID: 588 RVA: 0x000092F4 File Offset: 0x000074F4
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

        // Token: 0x17000112 RID: 274
        // (get) Token: 0x0600024D RID: 589 RVA: 0x00009300 File Offset: 0x00007500
        // (set) Token: 0x0600024E RID: 590 RVA: 0x00009308 File Offset: 0x00007508
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

        // Token: 0x17000113 RID: 275
        // (get) Token: 0x0600024F RID: 591 RVA: 0x00009314 File Offset: 0x00007514
        // (set) Token: 0x06000250 RID: 592 RVA: 0x0000931C File Offset: 0x0000751C
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

        // Token: 0x17000114 RID: 276
        // (get) Token: 0x06000251 RID: 593 RVA: 0x00009328 File Offset: 0x00007528
        // (set) Token: 0x06000252 RID: 594 RVA: 0x00009330 File Offset: 0x00007530
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

        // Token: 0x17000115 RID: 277
        // (get) Token: 0x06000253 RID: 595 RVA: 0x0000933C File Offset: 0x0000753C
        // (set) Token: 0x06000254 RID: 596 RVA: 0x00009344 File Offset: 0x00007544
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

        // Token: 0x17000116 RID: 278
        // (get) Token: 0x06000255 RID: 597 RVA: 0x00009350 File Offset: 0x00007550
        // (set) Token: 0x06000256 RID: 598 RVA: 0x00009358 File Offset: 0x00007558
        public string BackgroundSpectrumFile
        {
            get
            {
                return this.backgroundSpectrumFile;
            }
            set
            {
                this.backgroundSpectrumFile = value;
            }
        }

        // Token: 0x17000117 RID: 279
        // (get) Token: 0x06000257 RID: 599 RVA: 0x00009364 File Offset: 0x00007564
        // (set) Token: 0x06000258 RID: 600 RVA: 0x0000936C File Offset: 0x0000756C
        [XmlIgnore]
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

        // Token: 0x17000118 RID: 280
        // (get) Token: 0x06000259 RID: 601 RVA: 0x00009378 File Offset: 0x00007578
        // (set) Token: 0x0600025A RID: 602 RVA: 0x00009380 File Offset: 0x00007580
        public DateTime StartTime
        {
            get
            {
                return this.startTime;
            }
            set
            {
                this.startTime = value;
            }
        }

        // Token: 0x17000119 RID: 281
        // (get) Token: 0x0600025B RID: 603 RVA: 0x0000938C File Offset: 0x0000758C
        // (set) Token: 0x0600025C RID: 604 RVA: 0x00009394 File Offset: 0x00007594
        public DateTime EndTime
        {
            get
            {
                return this.endTime;
            }
            set
            {
                this.endTime = value;
            }
        }

        // Token: 0x1700011A RID: 282
        // (get) Token: 0x0600025D RID: 605 RVA: 0x000093A0 File Offset: 0x000075A0
        // (set) Token: 0x0600025E RID: 606 RVA: 0x000093A8 File Offset: 0x000075A8
        public EnergySpectrum EnergySpectrum
        {
            get
            {
                return this.energySpectrum;
            }
            set
            {
                this.energySpectrum = value;
            }
        }

        // Token: 0x1700011B RID: 283
        // (get) Token: 0x0600025F RID: 607 RVA: 0x000093B4 File Offset: 0x000075B4
        // (set) Token: 0x06000260 RID: 608 RVA: 0x000093BC File Offset: 0x000075BC
        public EnergySpectrum BackgroundEnergySpectrum
        {
            get
            {
                return this.backgroundEnergySpectrum;
            }
            set
            {
                this.backgroundEnergySpectrum = value;
            }
        }

        // Token: 0x1700011C RID: 284
        // (get) Token: 0x06000261 RID: 609 RVA: 0x000093C8 File Offset: 0x000075C8
        // (set) Token: 0x06000262 RID: 610 RVA: 0x000093D0 File Offset: 0x000075D0
        public PulseCollection PulseCollection
        {
            get
            {
                return this.pulseCollection;
            }
            set
            {
                this.pulseCollection = value;
            }
        }

        // Token: 0x040000AB RID: 171
        SampleInfoData sampleInfo = new SampleInfoData();

        // Token: 0x040000AC RID: 172
        DeviceConfigInfo deviceConfig = new DeviceConfigInfo();

        // Token: 0x040000AD RID: 173
        DeviceConfigReference deviceConfigReference = new DeviceConfigReference();

        // Token: 0x040000AE RID: 174
        ROIConfigData roiConfig = new ROIConfigData();

        // Token: 0x040000AF RID: 175
        ROIConfigReference roiConfigReference = new ROIConfigReference();

        // Token: 0x040000B0 RID: 176
        double energyCoefficient = 1.0;

        // Token: 0x040000B1 RID: 177
        double energyOffset;

        // Token: 0x040000B2 RID: 178
        DateTime startTime = DateTime.Now;

        // Token: 0x040000B3 RID: 179
        DateTime endTime = DateTime.Now;

        // Token: 0x040000B4 RID: 180
        string backgroundSpectrumFile = "";

        // Token: 0x040000B5 RID: 181
        string backgroundSpectrumPathname = "";

        // Token: 0x040000B6 RID: 182
        EnergySpectrum energySpectrum = new EnergySpectrum();

        // Token: 0x040000B7 RID: 183
        EnergySpectrum backgroundEnergySpectrum = new EnergySpectrum();

        // Token: 0x040000B8 RID: 184
        PulseCollection pulseCollection = new PulseCollection();
    }
}
