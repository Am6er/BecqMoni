using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000109 RID: 265
    public class EnergySpectrum_097b
    {
        // Token: 0x170003A3 RID: 931
        // (get) Token: 0x06000E56 RID: 3670 RVA: 0x000543D4 File Offset: 0x000525D4
        // (set) Token: 0x06000E57 RID: 3671 RVA: 0x000543DC File Offset: 0x000525DC
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

        // Token: 0x170003A4 RID: 932
        // (get) Token: 0x06000E58 RID: 3672 RVA: 0x000543E8 File Offset: 0x000525E8
        // (set) Token: 0x06000E59 RID: 3673 RVA: 0x000543F0 File Offset: 0x000525F0
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

        // Token: 0x170003A5 RID: 933
        // (get) Token: 0x06000E5A RID: 3674 RVA: 0x000543FC File Offset: 0x000525FC
        // (set) Token: 0x06000E5B RID: 3675 RVA: 0x00054404 File Offset: 0x00052604
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

        // Token: 0x170003A6 RID: 934
        // (get) Token: 0x06000E5C RID: 3676 RVA: 0x00054410 File Offset: 0x00052610
        // (set) Token: 0x06000E5D RID: 3677 RVA: 0x00054418 File Offset: 0x00052618
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

        // Token: 0x170003A7 RID: 935
        // (get) Token: 0x06000E5E RID: 3678 RVA: 0x00054424 File Offset: 0x00052624
        // (set) Token: 0x06000E5F RID: 3679 RVA: 0x0005442C File Offset: 0x0005262C
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

        // Token: 0x170003A8 RID: 936
        // (get) Token: 0x06000E60 RID: 3680 RVA: 0x00054438 File Offset: 0x00052638
        // (set) Token: 0x06000E61 RID: 3681 RVA: 0x00054440 File Offset: 0x00052640
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

        // Token: 0x170003A9 RID: 937
        // (get) Token: 0x06000E62 RID: 3682 RVA: 0x0005444C File Offset: 0x0005264C
        // (set) Token: 0x06000E63 RID: 3683 RVA: 0x00054454 File Offset: 0x00052654
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

        // Token: 0x170003AA RID: 938
        // (get) Token: 0x06000E64 RID: 3684 RVA: 0x00054460 File Offset: 0x00052660
        // (set) Token: 0x06000E65 RID: 3685 RVA: 0x00054468 File Offset: 0x00052668
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

        // Token: 0x170003AB RID: 939
        // (get) Token: 0x06000E66 RID: 3686 RVA: 0x00054474 File Offset: 0x00052674
        // (set) Token: 0x06000E67 RID: 3687 RVA: 0x0005447C File Offset: 0x0005267C
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

        // Token: 0x170003AC RID: 940
        // (get) Token: 0x06000E68 RID: 3688 RVA: 0x00054488 File Offset: 0x00052688
        // (set) Token: 0x06000E69 RID: 3689 RVA: 0x00054490 File Offset: 0x00052690
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

        // Token: 0x06000E6A RID: 3690 RVA: 0x0005449C File Offset: 0x0005269C
        public EnergySpectrum_097b()
        {
        }

        // Token: 0x06000E6B RID: 3691 RVA: 0x000544C4 File Offset: 0x000526C4
        public EnergySpectrum_097b(double channelPitch, int numberOfChannels)
        {
            this.channelPitch = channelPitch;
            this.numberOfChannels = numberOfChannels;
            this.spectrum = new int[numberOfChannels];
        }

        // Token: 0x06000E6C RID: 3692 RVA: 0x00054504 File Offset: 0x00052704
        public void Increment(double value)
        {
            int num = (int)(value / this.channelPitch);
            if (num >= 0 && num <= this.numberOfChannels - 1)
            {
                this.spectrum[num]++;
                this.validPulseCount++;
            }
        }

        // Token: 0x06000E6D RID: 3693 RVA: 0x00054554 File Offset: 0x00052754
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
        }

        // Token: 0x04000837 RID: 2103
        int numberOfChannels;

        // Token: 0x04000838 RID: 2104
        double channelPitch = 0.04;

        // Token: 0x04000839 RID: 2105
        double energyCoefficient = 1.0;

        // Token: 0x0400083A RID: 2106
        double energyOffset;

        // Token: 0x0400083B RID: 2107
        int totalPulseCount;

        // Token: 0x0400083C RID: 2108
        int validPulseCount;

        // Token: 0x0400083D RID: 2109
        double measurementTime;

        // Token: 0x0400083E RID: 2110
        long numberOfSamples;

        // Token: 0x0400083F RID: 2111
        int[] spectrum;

        // Token: 0x04000840 RID: 2112
        double[] drawingSpectrum;
    }
}
