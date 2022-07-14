using System;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x02000095 RID: 149
    public class PulseDetector
    {
        // Token: 0x1700021A RID: 538
        // (get) Token: 0x06000737 RID: 1847 RVA: 0x00029F50 File Offset: 0x00028150
        // (set) Token: 0x06000738 RID: 1848 RVA: 0x00029F58 File Offset: 0x00028158
        public WaveFormat WaveFormat
        {
            get
            {
                return this.waveFormat;
            }
            set
            {
                this.waveFormat = value;
            }
        }

        // Token: 0x1700021B RID: 539
        // (get) Token: 0x06000739 RID: 1849 RVA: 0x00029F64 File Offset: 0x00028164
        // (set) Token: 0x0600073A RID: 1850 RVA: 0x00029F6C File Offset: 0x0002816C
        public PulseView PulseView
        {
            get
            {
                return this.pulseView;
            }
            set
            {
                this.pulseView = value;
            }
        }

        // Token: 0x1700021C RID: 540
        // (get) Token: 0x0600073B RID: 1851 RVA: 0x00029F78 File Offset: 0x00028178
        // (set) Token: 0x0600073C RID: 1852 RVA: 0x00029F80 File Offset: 0x00028180
        public PulseView NGPulseView
        {
            get
            {
                return this.ngPulseView;
            }
            set
            {
                this.ngPulseView = value;
            }
        }

        // Token: 0x1700021D RID: 541
        // (get) Token: 0x0600073D RID: 1853 RVA: 0x00029F8C File Offset: 0x0002818C
        // (set) Token: 0x0600073E RID: 1854 RVA: 0x00029F94 File Offset: 0x00028194
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

        // Token: 0x1700021E RID: 542
        // (get) Token: 0x0600073F RID: 1855 RVA: 0x00029FA0 File Offset: 0x000281A0
        // (set) Token: 0x06000740 RID: 1856 RVA: 0x00029FA8 File Offset: 0x000281A8
        public PulseCollection Pulses
        {
            get
            {
                return this.pulses;
            }
            set
            {
                this.pulses = value;
            }
        }

        // Token: 0x1700021F RID: 543
        // (get) Token: 0x06000741 RID: 1857 RVA: 0x00029FB4 File Offset: 0x000281B4
        // (set) Token: 0x06000742 RID: 1858 RVA: 0x00029FBC File Offset: 0x000281BC
        public bool DoUpdatePulseView
        {
            get
            {
                return this.doUpdatePulseView;
            }
            set
            {
                this.doUpdatePulseView = value;
            }
        }

        // Token: 0x17000220 RID: 544
        // (get) Token: 0x06000743 RID: 1859 RVA: 0x00029FC8 File Offset: 0x000281C8
        // (set) Token: 0x06000744 RID: 1860 RVA: 0x00029FD0 File Offset: 0x000281D0
        public long TimeInSamples
        {
            get
            {
                return this.timeInSamples;
            }
            set
            {
                this.timeInSamples = value;
            }
        }

        // Token: 0x06000746 RID: 1862 RVA: 0x00029FE4 File Offset: 0x000281E4
        public void ProcessWaveData(byte[] data)
        {
            int bitsPerSample = (int)this.waveFormat.BitsPerSample;
            int num = bitsPerSample;
            if (num == 24)
            {

                this.waveData = new double[data.Length / 3];
                for (int i = 0; i < data.Length / 3; i++)
                {
                    uint num2 = (uint)((uint)data[i * 3 + 2] << 16);
                    uint num3 = (uint)((uint)data[i * 3 + 1] << 8);
                    uint num4 = (uint)data[i * 3];
                    uint num5;
                    if ((num2 & 8388608u) == 0u)
                    {
                        num5 = (num2 | num3 | num4);
                    }
                    else
                    {
                        num5 = (4278190080u | num2 | num3 | num4);
                    }
                    double num6 = (double)num5 * 100.0 / 8388608.0;
                    this.waveData[i] = (this.negativePolarity ? (-num6) : num6);
                }

            }
            if (num == 16)
            {
                this.waveData = new double[data.Length / 2];
                for (int j = 0; j < data.Length / 2; j++)
                {
                    double num7 = (double)BitConverter.ToInt16(data, j * 2) * 100.0 / 32768.0;
                    this.waveData[j] = (this.negativePolarity ? (-num7) : num7);
                }
            }
            this.DetectPulse(this.waveData);
        }

        // Token: 0x06000747 RID: 1863 RVA: 0x0002A134 File Offset: 0x00028334
        public virtual void Initialize(AudioInputDeviceConfig audioConfig, WaveFormat waveFormat, long timeInSamples)
        {
            this.waveFormat = waveFormat;
            this.negativePolarity = audioConfig.NegativePolarity;
            this.timeInSamples = timeInSamples;
        }

        // Token: 0x06000748 RID: 1864 RVA: 0x0002A150 File Offset: 0x00028350
        public virtual void DetectPulse(double[] waveData)
        {
        }

        // Token: 0x040003A6 RID: 934
        protected WaveFormat waveFormat;

        // Token: 0x040003A7 RID: 935
        protected PulseView pulseView;

        // Token: 0x040003A8 RID: 936
        protected PulseView ngPulseView;

        // Token: 0x040003A9 RID: 937
        protected EnergySpectrum energySpectrum;

        // Token: 0x040003AA RID: 938
        protected PulseCollection pulses;

        // Token: 0x040003AB RID: 939
        protected bool doUpdatePulseView;

        // Token: 0x040003AC RID: 940
        protected long timeInSamples;

        // Token: 0x040003AD RID: 941
        double[] waveData;

        // Token: 0x040003AE RID: 942
        bool negativePolarity;
    }
}
