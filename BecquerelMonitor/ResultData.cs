using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000082 RID: 130
    public class ResultData
    {
        // Token: 0x170001E4 RID: 484
        // (get) Token: 0x06000671 RID: 1649 RVA: 0x00027994 File Offset: 0x00025B94
        // (set) Token: 0x06000672 RID: 1650 RVA: 0x0002799C File Offset: 0x00025B9C
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

        // Token: 0x170001E5 RID: 485
        // (get) Token: 0x06000673 RID: 1651 RVA: 0x000279A8 File Offset: 0x00025BA8
        // (set) Token: 0x06000674 RID: 1652 RVA: 0x000279B0 File Offset: 0x00025BB0
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

        // Token: 0x170001E6 RID: 486
        // (get) Token: 0x06000675 RID: 1653 RVA: 0x000279BC File Offset: 0x00025BBC
        // (set) Token: 0x06000676 RID: 1654 RVA: 0x000279C4 File Offset: 0x00025BC4
        [XmlIgnore]
        public MeasurementResultCollection MeasurementResultCollection
        {
            get
            {
                return this.measurementResultCollection;
            }
            set
            {
                this.measurementResultCollection = value;
            }
        }

        // Token: 0x170001E7 RID: 487
        // (get) Token: 0x06000677 RID: 1655 RVA: 0x000279D0 File Offset: 0x00025BD0
        // (set) Token: 0x06000678 RID: 1656 RVA: 0x000279D8 File Offset: 0x00025BD8
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

        // Token: 0x170001E8 RID: 488
        // (get) Token: 0x06000679 RID: 1657 RVA: 0x000279E4 File Offset: 0x00025BE4
        // (set) Token: 0x0600067A RID: 1658 RVA: 0x000279EC File Offset: 0x00025BEC
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

        // Token: 0x170001E9 RID: 489
        // (get) Token: 0x0600067B RID: 1659 RVA: 0x000279F8 File Offset: 0x00025BF8
        // (set) Token: 0x0600067C RID: 1660 RVA: 0x00027A00 File Offset: 0x00025C00
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

        // Token: 0x170001EA RID: 490
        // (get) Token: 0x0600067D RID: 1661 RVA: 0x00027A0C File Offset: 0x00025C0C
        // (set) Token: 0x0600067E RID: 1662 RVA: 0x00027A14 File Offset: 0x00025C14
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

        // Token: 0x170001EB RID: 491
        // (get) Token: 0x0600067F RID: 1663 RVA: 0x00027A20 File Offset: 0x00025C20
        // (set) Token: 0x06000680 RID: 1664 RVA: 0x00027A28 File Offset: 0x00025C28
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

        // Token: 0x170001EC RID: 492
        // (get) Token: 0x06000681 RID: 1665 RVA: 0x00027A34 File Offset: 0x00025C34
        // (set) Token: 0x06000682 RID: 1666 RVA: 0x00027A3C File Offset: 0x00025C3C
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

        // Token: 0x170001ED RID: 493
        // (get) Token: 0x06000683 RID: 1667 RVA: 0x00027A48 File Offset: 0x00025C48
        // (set) Token: 0x06000684 RID: 1668 RVA: 0x00027A50 File Offset: 0x00025C50
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

        // Token: 0x170001EE RID: 494
        // (get) Token: 0x06000685 RID: 1669 RVA: 0x00027A5C File Offset: 0x00025C5C
        // (set) Token: 0x06000686 RID: 1670 RVA: 0x00027A64 File Offset: 0x00025C64
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

        // Token: 0x170001EF RID: 495
        // (get) Token: 0x06000687 RID: 1671 RVA: 0x00027A70 File Offset: 0x00025C70
        // (set) Token: 0x06000688 RID: 1672 RVA: 0x00027A78 File Offset: 0x00025C78
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

        // Token: 0x170001F0 RID: 496
        // (get) Token: 0x06000689 RID: 1673 RVA: 0x00027A84 File Offset: 0x00025C84
        // (set) Token: 0x0600068A RID: 1674 RVA: 0x00027A8C File Offset: 0x00025C8C
        public int PresetTime
        {
            get
            {
                return this.presetTime;
            }
            set
            {
                this.presetTime = value;
            }
        }

        // Token: 0x170001F1 RID: 497
        // (get) Token: 0x0600068B RID: 1675 RVA: 0x00027A98 File Offset: 0x00025C98
        // (set) Token: 0x0600068C RID: 1676 RVA: 0x00027AA0 File Offset: 0x00025CA0
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

        // Token: 0x170001F2 RID: 498
        // (get) Token: 0x0600068D RID: 1677 RVA: 0x00027AAC File Offset: 0x00025CAC
        // (set) Token: 0x0600068E RID: 1678 RVA: 0x00027AB4 File Offset: 0x00025CB4
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

        // Token: 0x170001F3 RID: 499
        // (get) Token: 0x0600068F RID: 1679 RVA: 0x00027AC0 File Offset: 0x00025CC0
        // (set) Token: 0x06000690 RID: 1680 RVA: 0x00027AC8 File Offset: 0x00025CC8
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

        // Token: 0x170001F4 RID: 500
        // (get) Token: 0x06000691 RID: 1681 RVA: 0x00027AD4 File Offset: 0x00025CD4
        // (set) Token: 0x06000692 RID: 1682 RVA: 0x00027ADC File Offset: 0x00025CDC
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

        // Token: 0x170001F5 RID: 501
        // (get) Token: 0x06000693 RID: 1683 RVA: 0x00027AE8 File Offset: 0x00025CE8
        // (set) Token: 0x06000694 RID: 1684 RVA: 0x00027AF0 File Offset: 0x00025CF0
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

        // Token: 0x170001F6 RID: 502
        // (get) Token: 0x06000695 RID: 1685 RVA: 0x00027AFC File Offset: 0x00025CFC
        // (set) Token: 0x06000696 RID: 1686 RVA: 0x00027B04 File Offset: 0x00025D04
        [XmlIgnore]
        public bool Selected
        {
            get
            {
                return this.selected;
            }
            set
            {
                this.selected = value;
            }
        }

        // Token: 0x170001F7 RID: 503
        // (get) Token: 0x06000697 RID: 1687 RVA: 0x00027B10 File Offset: 0x00025D10
        // (set) Token: 0x06000698 RID: 1688 RVA: 0x00027B18 File Offset: 0x00025D18
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

        // Token: 0x170001F8 RID: 504
        // (get) Token: 0x06000699 RID: 1689 RVA: 0x00027B24 File Offset: 0x00025D24
        // (set) Token: 0x0600069A RID: 1690 RVA: 0x00027B2C File Offset: 0x00025D2C
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

        // Token: 0x170001F9 RID: 505
        // (get) Token: 0x0600069B RID: 1691 RVA: 0x00027B38 File Offset: 0x00025D38
        // (set) Token: 0x0600069C RID: 1692 RVA: 0x00027B40 File Offset: 0x00025D40
        [XmlIgnore]
        public List<Peak> CalibrationPeaks
        {
            get
            {
                return this.calibrationPeaks;
            }
            set
            {
                this.calibrationPeaks = value;
            }
        }

        // Token: 0x0600069D RID: 1693 RVA: 0x00027B4C File Offset: 0x00025D4C
        public ResultData()
        {
        }

        // Token: 0x0600069E RID: 1694 RVA: 0x00027C10 File Offset: 0x00025E10
        public ResultData(ResultData_093b old)
        {
            this.sampleInfo = old.SampleInfo;
            this.deviceConfig = old.DeviceConfig;
            this.deviceConfigReference = old.DeviceConfigReference;
            this.roiConfig = old.ROIConfig;
            this.roiConfigReference = old.ROIConfigReference;
            this.startTime = old.StartTime;
            this.endTime = old.EndTime;
            this.backgroundSpectrumFile = old.BackgroundSpectrumFile;
            this.backgroundSpectrumPathname = old.BackgroundSpectrumPathname;
            this.energySpectrum = old.EnergySpectrum;
            this.backgroundEnergySpectrum = old.BackgroundEnergySpectrum;
            this.pulseCollection = old.PulseCollection;
            PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)this.energySpectrum.EnergyCalibration;
            polynomialEnergyCalibration.Coefficients[2] = 0.0;
            polynomialEnergyCalibration.Coefficients[1] = old.EnergyCoefficient;
            polynomialEnergyCalibration.Coefficients[0] = old.EnergyOffset;
            PolynomialEnergyCalibration polynomialEnergyCalibration2 = (PolynomialEnergyCalibration)this.backgroundEnergySpectrum.EnergyCalibration;
            polynomialEnergyCalibration2.Coefficients[2] = 0.0;
            polynomialEnergyCalibration2.Coefficients[1] = old.EnergyCoefficient;
            polynomialEnergyCalibration2.Coefficients[0] = old.EnergyOffset;
        }

        // Token: 0x0600069F RID: 1695 RVA: 0x00027DE0 File Offset: 0x00025FE0
        public ResultData(ResultData_097b old)
        {
            this.sampleInfo = old.SampleInfo;
            this.deviceConfig = old.DeviceConfig;
            this.deviceConfigReference = old.DeviceConfigReference;
            this.roiConfig = old.ROIConfig;
            this.roiConfigReference = old.ROIConfigReference;
            this.startTime = old.StartTime;
            this.endTime = old.EndTime;
            this.backgroundSpectrumFile = old.BackgroundSpectrumFile;
            this.backgroundSpectrumPathname = old.BackgroundSpectrumPathname;
            this.energySpectrum = new EnergySpectrum(old.EnergySpectrum);
            this.backgroundEnergySpectrum = new EnergySpectrum(old.BackgroundEnergySpectrum);
            this.pulseCollection = old.PulseCollection;
        }

        // Token: 0x04000361 RID: 865
        ResultDataStatus resultDataStatus = new ResultDataStatus();

        // Token: 0x04000362 RID: 866
        MeasurementController measurementController;

        // Token: 0x04000363 RID: 867
        MeasurementResultCollection measurementResultCollection;

        // Token: 0x04000364 RID: 868
        SampleInfoData sampleInfo = new SampleInfoData();

        // Token: 0x04000365 RID: 869
        DeviceConfigInfo deviceConfig = new DeviceConfigInfo();

        // Token: 0x04000366 RID: 870
        DeviceConfigReference deviceConfigReference = new DeviceConfigReference();

        // Token: 0x04000367 RID: 871
        ROIConfigData roiConfig = new ROIConfigData();

        // Token: 0x04000368 RID: 872
        ROIConfigReference roiConfigReference = new ROIConfigReference();

        // Token: 0x04000369 RID: 873
        DateTime startTime = DateTime.Now;

        // Token: 0x0400036A RID: 874
        DateTime endTime = DateTime.Now;

        // Token: 0x0400036B RID: 875
        int presetTime;

        // Token: 0x0400036C RID: 876
        string backgroundSpectrumFile = "";

        // Token: 0x0400036D RID: 877
        string backgroundSpectrumPathname = "";

        // Token: 0x0400036E RID: 878
        EnergySpectrum energySpectrum = new EnergySpectrum();

        // Token: 0x0400036F RID: 879
        EnergySpectrum backgroundEnergySpectrum;

        // Token: 0x04000370 RID: 880
        PulseCollection pulseCollection = new PulseCollection();

        // Token: 0x04000371 RID: 881
        bool dirty;

        // Token: 0x04000372 RID: 882
        bool visible = true;

        // Token: 0x04000373 RID: 883
        bool selected;

        // Token: 0x04000374 RID: 884
        List<Peak> detectedPeaks = new List<Peak>();

        // Token: 0x04000375 RID: 885
        PeakDetectionMethodConfig peakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig();

        // Token: 0x04000376 RID: 886
        List<Peak> calibrationPeaks = new List<Peak>();
    }
}
