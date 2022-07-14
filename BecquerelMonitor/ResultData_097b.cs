using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000073 RID: 115
    [XmlRoot("ResultData")]
    public class ResultData_097b
    {
        // Token: 0x170001BD RID: 445
        // (get) Token: 0x060005CB RID: 1483 RVA: 0x00025470 File Offset: 0x00023670
        // (set) Token: 0x060005CC RID: 1484 RVA: 0x00025478 File Offset: 0x00023678
        [XmlIgnore]
        public ResultDataStatus ResultDataStatus
        {
            get
            {
                return this.resultDataStatus;
            }
            set
            {
                this.resultDataStatus = value;
            }
        }

        // Token: 0x170001BE RID: 446
        // (get) Token: 0x060005CD RID: 1485 RVA: 0x00025484 File Offset: 0x00023684
        // (set) Token: 0x060005CE RID: 1486 RVA: 0x0002548C File Offset: 0x0002368C
        [XmlIgnore]
        public MeasurementController MeasurementController
        {
            get
            {
                return this.measurementController;
            }
            set
            {
                this.measurementController = value;
            }
        }

        // Token: 0x170001BF RID: 447
        // (get) Token: 0x060005CF RID: 1487 RVA: 0x00025498 File Offset: 0x00023698
        // (set) Token: 0x060005D0 RID: 1488 RVA: 0x000254A0 File Offset: 0x000236A0
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

        // Token: 0x170001C0 RID: 448
        // (get) Token: 0x060005D1 RID: 1489 RVA: 0x000254AC File Offset: 0x000236AC
        // (set) Token: 0x060005D2 RID: 1490 RVA: 0x000254B4 File Offset: 0x000236B4
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

        // Token: 0x170001C1 RID: 449
        // (get) Token: 0x060005D3 RID: 1491 RVA: 0x000254C0 File Offset: 0x000236C0
        // (set) Token: 0x060005D4 RID: 1492 RVA: 0x000254C8 File Offset: 0x000236C8
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

        // Token: 0x170001C2 RID: 450
        // (get) Token: 0x060005D5 RID: 1493 RVA: 0x000254D4 File Offset: 0x000236D4
        // (set) Token: 0x060005D6 RID: 1494 RVA: 0x000254DC File Offset: 0x000236DC
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

        // Token: 0x170001C3 RID: 451
        // (get) Token: 0x060005D7 RID: 1495 RVA: 0x000254E8 File Offset: 0x000236E8
        // (set) Token: 0x060005D8 RID: 1496 RVA: 0x000254F0 File Offset: 0x000236F0
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

        // Token: 0x170001C4 RID: 452
        // (get) Token: 0x060005D9 RID: 1497 RVA: 0x000254FC File Offset: 0x000236FC
        // (set) Token: 0x060005DA RID: 1498 RVA: 0x00025504 File Offset: 0x00023704
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

        // Token: 0x170001C5 RID: 453
        // (get) Token: 0x060005DB RID: 1499 RVA: 0x00025510 File Offset: 0x00023710
        // (set) Token: 0x060005DC RID: 1500 RVA: 0x00025518 File Offset: 0x00023718
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

        // Token: 0x170001C6 RID: 454
        // (get) Token: 0x060005DD RID: 1501 RVA: 0x00025524 File Offset: 0x00023724
        // (set) Token: 0x060005DE RID: 1502 RVA: 0x0002552C File Offset: 0x0002372C
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

        // Token: 0x170001C7 RID: 455
        // (get) Token: 0x060005DF RID: 1503 RVA: 0x00025538 File Offset: 0x00023738
        // (set) Token: 0x060005E0 RID: 1504 RVA: 0x00025540 File Offset: 0x00023740
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

        // Token: 0x170001C8 RID: 456
        // (get) Token: 0x060005E1 RID: 1505 RVA: 0x0002554C File Offset: 0x0002374C
        // (set) Token: 0x060005E2 RID: 1506 RVA: 0x00025554 File Offset: 0x00023754
        public EnergySpectrum_097b EnergySpectrum
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

        // Token: 0x170001C9 RID: 457
        // (get) Token: 0x060005E3 RID: 1507 RVA: 0x00025560 File Offset: 0x00023760
        // (set) Token: 0x060005E4 RID: 1508 RVA: 0x00025568 File Offset: 0x00023768
        public EnergySpectrum_097b BackgroundEnergySpectrum
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

        // Token: 0x170001CA RID: 458
        // (get) Token: 0x060005E5 RID: 1509 RVA: 0x00025574 File Offset: 0x00023774
        // (set) Token: 0x060005E6 RID: 1510 RVA: 0x0002557C File Offset: 0x0002377C
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

        // Token: 0x170001CB RID: 459
        // (get) Token: 0x060005E7 RID: 1511 RVA: 0x00025588 File Offset: 0x00023788
        // (set) Token: 0x060005E8 RID: 1512 RVA: 0x00025590 File Offset: 0x00023790
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

        // Token: 0x170001CC RID: 460
        // (get) Token: 0x060005E9 RID: 1513 RVA: 0x0002559C File Offset: 0x0002379C
        // (set) Token: 0x060005EA RID: 1514 RVA: 0x000255A4 File Offset: 0x000237A4
        [XmlIgnore]
        public bool Visible
        {
            get
            {
                return this.visible;
            }
            set
            {
                this.visible = value;
            }
        }

        // Token: 0x170001CD RID: 461
        // (get) Token: 0x060005EB RID: 1515 RVA: 0x000255B0 File Offset: 0x000237B0
        // (set) Token: 0x060005EC RID: 1516 RVA: 0x000255B8 File Offset: 0x000237B8
        [XmlIgnore]
        public List<Peak> DetectedPeaks
        {
            get
            {
                return this.detectedPeaks;
            }
            set
            {
                this.detectedPeaks = value;
            }
        }

        // Token: 0x170001CE RID: 462
        // (get) Token: 0x060005ED RID: 1517 RVA: 0x000255C4 File Offset: 0x000237C4
        // (set) Token: 0x060005EE RID: 1518 RVA: 0x000255CC File Offset: 0x000237CC
        [XmlIgnore]
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

        // Token: 0x04000314 RID: 788
        ResultDataStatus resultDataStatus = new ResultDataStatus();

        // Token: 0x04000315 RID: 789
        MeasurementController measurementController;

        // Token: 0x04000316 RID: 790
        SampleInfoData sampleInfo = new SampleInfoData();

        // Token: 0x04000317 RID: 791
        DeviceConfigInfo deviceConfig = new DeviceConfigInfo();

        // Token: 0x04000318 RID: 792
        DeviceConfigReference deviceConfigReference = new DeviceConfigReference();

        // Token: 0x04000319 RID: 793
        ROIConfigData roiConfig = new ROIConfigData();

        // Token: 0x0400031A RID: 794
        ROIConfigReference roiConfigReference = new ROIConfigReference();

        // Token: 0x0400031B RID: 795
        DateTime startTime = DateTime.Now;

        // Token: 0x0400031C RID: 796
        DateTime endTime = DateTime.Now;

        // Token: 0x0400031D RID: 797
        string backgroundSpectrumFile = "";

        // Token: 0x0400031E RID: 798
        string backgroundSpectrumPathname = "";

        // Token: 0x0400031F RID: 799
        EnergySpectrum_097b energySpectrum = new EnergySpectrum_097b();

        // Token: 0x04000320 RID: 800
        EnergySpectrum_097b backgroundEnergySpectrum = new EnergySpectrum_097b();

        // Token: 0x04000321 RID: 801
        PulseCollection pulseCollection = new PulseCollection();

        // Token: 0x04000322 RID: 802
        bool dirty;

        // Token: 0x04000323 RID: 803
        bool visible = true;

        // Token: 0x04000324 RID: 804
        List<Peak> detectedPeaks = new List<Peak>();

        // Token: 0x04000325 RID: 805
        PeakDetectionMethodConfig peakDetectionMethodConfig = new SimplePeakDetectionMethodConfig();
    }
}
