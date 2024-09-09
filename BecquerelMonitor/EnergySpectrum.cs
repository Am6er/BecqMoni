using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000010 RID: 16
    public class EnergySpectrum
    {
        // Token: 0x1700001E RID: 30
        // (get) Token: 0x06000054 RID: 84 RVA: 0x0000284C File Offset: 0x00000A4C
        // (set) Token: 0x06000055 RID: 85 RVA: 0x00002854 File Offset: 0x00000A54
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

        // Token: 0x1700001F RID: 31
        // (get) Token: 0x06000056 RID: 86 RVA: 0x00002860 File Offset: 0x00000A60
        // (set) Token: 0x06000057 RID: 87 RVA: 0x00002868 File Offset: 0x00000A68
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

        // Token: 0x17000020 RID: 32
        // (get) Token: 0x06000058 RID: 88 RVA: 0x00002874 File Offset: 0x00000A74
        // (set) Token: 0x06000059 RID: 89 RVA: 0x0000287C File Offset: 0x00000A7C
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

        // Token: 0x17000021 RID: 33
        // (get) Token: 0x0600005A RID: 90 RVA: 0x00002888 File Offset: 0x00000A88
        // (set) Token: 0x0600005B RID: 91 RVA: 0x00002890 File Offset: 0x00000A90
        public int ValidPulseCount
        {
            get
            {
                return this.validPulseCount;
            }
            set
            {
                this.validPulseCount = value;
            }
        }

        // Token: 0x17000022 RID: 34
        // (get) Token: 0x0600005C RID: 92 RVA: 0x0000289C File Offset: 0x00000A9C
        // (set) Token: 0x0600005D RID: 93 RVA: 0x000028A4 File Offset: 0x00000AA4
        public int TotalPulseCount
        {
            get
            {
                return this.totalPulseCount;
            }
            set
            {
                this.totalPulseCount = value;
            }
        }

        // Token: 0x17000023 RID: 35
        // (get) Token: 0x0600005E RID: 94 RVA: 0x000028B0 File Offset: 0x00000AB0
        // (set) Token: 0x0600005F RID: 95 RVA: 0x000028B8 File Offset: 0x00000AB8
        public double MeasurementTime
        {
            get
            {
                return this.measurementTime;
            }
            set
            {
                this.measurementTime = value;
            }
        }

        public double LiveTime
        {
            get
            {
                return this.liveTime;
            }
            set
            {
                this.liveTime = value;
            }
        }

        // Token: 0x17000024 RID: 36
        // (get) Token: 0x06000060 RID: 96 RVA: 0x000028C4 File Offset: 0x00000AC4
        // (set) Token: 0x06000061 RID: 97 RVA: 0x000028CC File Offset: 0x00000ACC
        public long NumberOfSamples
        {
            get
            {
                return this.numberOfSamples;
            }
            set
            {
                this.numberOfSamples = value;
            }
        }

        // Token: 0x17000025 RID: 37
        // (get) Token: 0x06000062 RID: 98 RVA: 0x000028D8 File Offset: 0x00000AD8
        // (set) Token: 0x06000063 RID: 99 RVA: 0x000028E0 File Offset: 0x00000AE0
        [XmlArrayItem("DataPoint")]
        public int[] Spectrum
        {
            get
            {
                return this.spectrum;
            }
            set
            {
                this.spectrum = value;
            }
        }

        // Token: 0x17000026 RID: 38
        // (get) Token: 0x06000064 RID: 100 RVA: 0x000028EC File Offset: 0x00000AEC
        // (set) Token: 0x06000065 RID: 101 RVA: 0x000028F4 File Offset: 0x00000AF4
        [XmlIgnore]
        public double[] DrawingSpectrum
        {
            get
            {
                return this.drawingSpectrum;
            }
            set
            {
                this.drawingSpectrum = value;
            }
        }

        // Token: 0x06000066 RID: 102 RVA: 0x00002900 File Offset: 0x00000B00
        public EnergySpectrum()
        {
        }

        // Token: 0x06000067 RID: 103 RVA: 0x00002918 File Offset: 0x00000B18
        public EnergySpectrum(double channelPitch, int numberOfChannels)
        {
            this.channelPitch = channelPitch;
            this.numberOfChannels = numberOfChannels;
            this.spectrum = new int[numberOfChannels];
        }

        // Token: 0x06000068 RID: 104 RVA: 0x0000294C File Offset: 0x00000B4C
        public EnergySpectrum(EnergySpectrum_097b old)
        {
            this.numberOfChannels = old.NumberOfChannels;
            this.channelPitch = old.ChannelPitch;
            PolynomialEnergyCalibration polynomialEnergyCalibration = new PolynomialEnergyCalibration();
            PolynomialEnergyCalibration polynomialEnergyCalibration2 = polynomialEnergyCalibration;
            double[] array = new double[3];
            array[0] = old.EnergyOffset;
            array[1] = old.EnergyCoefficient;
            polynomialEnergyCalibration2.Coefficients = array;
            this.energyCalibration = polynomialEnergyCalibration;
            this.totalPulseCount = old.TotalPulseCount;
            this.validPulseCount = old.ValidPulseCount;
            this.measurementTime = old.MeasurementTime;
            this.numberOfSamples = old.NumberOfSamples;
            this.spectrum = old.Spectrum;
        }

        // Token: 0x06000069 RID: 105 RVA: 0x000029F4 File Offset: 0x00000BF4
        public void Increment(double value)
        {
            int channel = (int)(value / this.channelPitch);
            if (channel >= 0 && channel <= this.numberOfChannels - 1)
            {
                this.spectrum[channel]++;
                this.validPulseCount++;
            }
        }

        // Token: 0x0600006A RID: 106 RVA: 0x00002A44 File Offset: 0x00000C44
        public void Initialize()
        {
            if (this.spectrum == null || this.spectrum.Length != this.numberOfChannels)
            {
                this.spectrum = new int[this.numberOfChannels];
            }
            else
            {
                for (int i = 0; i < this.numberOfChannels; i++)
                {
                    this.spectrum[i] = 0;
                }
            }
            this.totalPulseCount = 0;
            this.validPulseCount = 0;
            this.measurementTime = 0.0;
            this.numberOfSamples = 0L;
            this.liveTime = 0.0;
        }

        public EnergySpectrum Clone()
        {
            EnergySpectrum returnValue = new EnergySpectrum(this.channelPitch, this.numberOfChannels);
            returnValue.spectrum = (int[]) this.spectrum.Clone();
            if (this.drawingSpectrum != null)
            {
                returnValue.drawingSpectrum = (double[])this.drawingSpectrum.Clone();
            } else
            {
                returnValue.drawingSpectrum = new double[this.numberOfChannels];
            }
            
            returnValue.channelPitch = this.channelPitch;
            returnValue.numberOfChannels = this.numberOfChannels;
            returnValue.measurementTime = this.measurementTime;
            returnValue.liveTime = this.liveTime;
            returnValue.energyCalibration = this.energyCalibration.Clone();
            returnValue.numberOfSamples = this.numberOfSamples;
            returnValue.totalPulseCount = this.totalPulseCount;
            returnValue.validPulseCount = this.validPulseCount;

            return returnValue;
        }

        // Token: 0x0400001D RID: 29
        int numberOfChannels;

        // Token: 0x0400001E RID: 30
        double channelPitch = 0.04;

        // Token: 0x0400001F RID: 31
        EnergyCalibration energyCalibration;

        // Token: 0x04000020 RID: 32
        int totalPulseCount;

        // Token: 0x04000021 RID: 33
        int validPulseCount;

        // Token: 0x04000022 RID: 34
        double measurementTime;

        double liveTime;

        // Token: 0x04000023 RID: 35
        long numberOfSamples;

        // Token: 0x04000024 RID: 36
        int[] spectrum;

        // Token: 0x04000025 RID: 37
        double[] drawingSpectrum;
    }
}
