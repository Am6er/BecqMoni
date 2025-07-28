using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class ResultData
    {
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

        public string BackgroundSpectrumFile
        {
            get
            {
                return this.backgroundSpectrumFile;
            }
            set
            {
                if (value != null)
                {
                    this.backgroundSpectrumFile = string.Join("", value.Split(Path.GetInvalidFileNameChars()));
                }
                else
                {
                    this.backgroundSpectrumFile = value;
                }
                
            }
        }

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

        [XmlIgnore]
        public string DetectorFeature
        {
            get
            {
                return this.detectorFeature;
            }
            set
            {
                this.detectorFeature = value;
            }
        }

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

        public EnergySpectrum EnergySpectrum
        {
            get
            {
                return this.energySpectrum;
            }
            set
            {
                this.energySpectrum = value;
                this.continuum_refresh = true;
                this.subtract_refresh = true;
            }
        }

        public EnergySpectrum BackgroundEnergySpectrum
        {
            get
            {
                return this.backgroundEnergySpectrum;
            }
            set
            {
                this.backgroundEnergySpectrum = value;
                this.subtract_refresh = true;
            }
        }

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

        [XmlIgnore]
        public List<CalibrationPoint> CalibrationPoints
        {
            get
            {
                return this.calibrationPoints;
            }
            set
            {
                this.calibrationPoints = value;
            }
        }

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

        [XmlIgnore]
        public List<CountRate> CountRates
        {
            get
            {
                return this.countRates;
            }
            set
            {
                this.countRates = value;
            }
        }

        public ResultData()
        {
        }

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

        public ResultData Clone()
        {
            ResultData resultData = new ResultData();
            resultData.SampleInfo = this.SampleInfo.Clone();
            resultData.DeviceConfig = this.DeviceConfig;
            resultData.DeviceConfigReference = this.DeviceConfigReference;
            resultData.ROIConfigReference = this.ROIConfigReference;
            resultData.ROIConfig = this.ROIConfig;
            resultData.StartTime = this.StartTime;
            resultData.EndTime = this.EndTime;
            resultData.PresetTime = this.PresetTime;
            resultData.BackgroundEnergySpectrum = this.BackgroundEnergySpectrum.Clone();
            resultData.BackgroundSpectrumFile = this.BackgroundSpectrumFile;
            resultData.BackgroundSpectrumPathname = this.BackgroundSpectrumPathname;
            resultData.EnergySpectrum = this.EnergySpectrum.Clone();
            resultData.PulseCollection = this.PulseCollection.Clone();

            return resultData;
        }

        ResultDataStatus resultDataStatus = new ResultDataStatus();

        MeasurementController measurementController;

        MeasurementResultCollection measurementResultCollection;

        SampleInfoData sampleInfo = new SampleInfoData();

        DeviceConfigInfo deviceConfig = new DeviceConfigInfo();

        DeviceConfigReference deviceConfigReference = new DeviceConfigReference();

        ROIConfigData roiConfig = new ROIConfigData();

        ROIConfigReference roiConfigReference = new ROIConfigReference();

        DateTime startTime = DateTime.Now;

        DateTime endTime = DateTime.Now;

        int presetTime;

        string backgroundSpectrumFile = "";

        string backgroundSpectrumPathname = "";

        EnergySpectrum energySpectrum = new EnergySpectrum();

        EnergySpectrum backgroundEnergySpectrum;

        PulseCollection pulseCollection = new PulseCollection();

        bool dirty;

        bool visible = true;

        bool selected;

        bool continuum_refresh = false;

        bool subtract_refresh = false;

        string detectorFeature;

        List<Peak> detectedPeaks = new List<Peak>();

        PeakDetectionMethodConfig peakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig();

        List<Peak> calibrationPeaks = new List<Peak>();

        List<CalibrationPoint> calibrationPoints = new List<CalibrationPoint>();

        List<CountRate> countRates = new List<CountRate>();
    }
}
