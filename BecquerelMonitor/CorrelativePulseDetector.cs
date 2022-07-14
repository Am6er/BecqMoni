using System;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x02000096 RID: 150
    public class CorrelativePulseDetector : PulseDetector
    {
        // Token: 0x0600074A RID: 1866 RVA: 0x0002A1A8 File Offset: 0x000283A8
        public override void Initialize(AudioInputDeviceConfig config, WaveFormat waveFormat, long timeInSamples)
        {
            PRAHomageMethodConfig prahomageMethodConfig = (PRAHomageMethodConfig)config.PulseDetectionMethodConfig;
            this.lowerThreshold = prahomageMethodConfig.LowerThreshold;
            this.upperThreshold = prahomageMethodConfig.UpperThreshold;
            this.pulseShapeSize = prahomageMethodConfig.PulseShapeSize;
            this.peakIndex = prahomageMethodConfig.PeakIndex;
            this.correlationThreshold = prahomageMethodConfig.PulseThreshold;
            this.idealPulseShape = (double[])prahomageMethodConfig.PulseShape.Clone();
            this.pulseShape = new double[this.pulseShapeSize];
            this.peakPulseShape = new double[this.pulseShapeSize];
            this.goodPulseShape = new double[this.pulseShapeSize];
            this.badPulseShape = new double[this.pulseShapeSize];
            this.state = CorrelativePulseDetector.DetectionState.Ready;
            this.previousWaveData = null;
            base.Initialize(config, waveFormat, timeInSamples);
        }

        // Token: 0x0600074B RID: 1867 RVA: 0x0002A274 File Offset: 0x00028474
        public override void DetectPulse(double[] waveData)
        {
            this.waveData = waveData;
            for (int i = -this.pulseShapeSize + 1; i <= waveData.Length - this.pulseShapeSize; i++)
            {
                if (i < 0)
                {
                    if (this.previousWaveData != null && this.previousWaveData.Length + i > 0)
                    {
                        for (int j = 0; j < this.pulseShapeSize; j++)
                        {
                            if (i + j < 0)
                            {
                                this.pulseShape[j] = this.previousWaveData[this.previousWaveData.Length + i + j];
                            }
                            else
                            {
                                this.pulseShape[j] = waveData[i + j];
                            }
                        }
                        this.ProcessPulseTrigger();
                    }
                }
                else
                {
                    for (int k = 0; k < this.pulseShapeSize; k++)
                    {
                        this.pulseShape[k] = waveData[i + k];
                    }
                    this.ProcessPulseTrigger();
                }
                this.timeInSamples += 1L;
            }
            this.previousWaveData = (double[])waveData.Clone();
        }

        // Token: 0x0600074C RID: 1868 RVA: 0x0002A370 File Offset: 0x00028570
        void ProcessPulseTrigger()
        {
            this.height = this.PulseHeight(this.pulseShape);
            switch (this.state)
            {
                case CorrelativePulseDetector.DetectionState.Ready:
                    if (this.height >= this.lowerThreshold)
                    {
                        this.peakHeight = this.height;
                        this.width = 1;
                        this.pulseShape.CopyTo(this.peakPulseShape, 0);
                        this.state = CorrelativePulseDetector.DetectionState.Triggered;
                        return;
                    }
                    break;
                case CorrelativePulseDetector.DetectionState.Triggered:
                    if (this.height >= this.lowerThreshold)
                    {
                        if (this.height > this.peakHeight)
                        {
                            this.peakHeight = this.height;
                            this.pulseShape.CopyTo(this.peakPulseShape, 0);
                        }
                        this.width++;
                        return;
                    }
                    if (this.peakHeight < this.upperThreshold)
                    {
                        this.ProcessPulse();
                    }
                    this.state = CorrelativePulseDetector.DetectionState.Ready;
                    break;
                default:
                    return;
            }
        }

        // Token: 0x0600074D RID: 1869 RVA: 0x0002A454 File Offset: 0x00028654
        void ProcessPulse()
        {
            bool flag = this.Discriminate(this.peakPulseShape, this.pulseShapeSize);
            if (flag)
            {
                this.pulses.Add(new Pulse(this.timeInSamples, this.peakHeight, this.width));
                this.energySpectrum.Increment(this.peakHeight);
            }
            this.energySpectrum.TotalPulseCount++;
            if (this.doUpdatePulseView)
            {
                if (flag)
                {
                    this.peakPulseShape.CopyTo(this.goodPulseShape, 0);
                    if (this.pulseView != null)
                    {
                        this.pulseView.PulseShape = this.goodPulseShape;
                        this.pulseView.PulseShapeSize = this.pulseShapeSize;
                        this.pulseView.PeakIndex = this.peakIndex;
                        this.pulseView.PulseHeight = this.peakHeight;
                        this.pulseView.IsValidPulse = true;
                        return;
                    }
                }
                else
                {
                    this.peakPulseShape.CopyTo(this.badPulseShape, 0);
                    if (this.ngPulseView != null)
                    {
                        this.ngPulseView.PulseShape = this.badPulseShape;
                        this.ngPulseView.PulseShapeSize = this.pulseShapeSize;
                        this.ngPulseView.PeakIndex = this.peakIndex;
                        this.ngPulseView.PulseHeight = this.peakHeight;
                        this.ngPulseView.IsValidPulse = false;
                    }
                }
            }
        }

        // Token: 0x0600074E RID: 1870 RVA: 0x0002A5B4 File Offset: 0x000287B4
        double PulseHeight(double[] shape)
        {
            double num = 0.0;
            for (int i = 0; i < this.pulseShapeSize; i++)
            {
                num += this.pulseShape[i] * this.idealPulseShape[i];
            }
            return num;
        }

        // Token: 0x0600074F RID: 1871 RVA: 0x0002A5F8 File Offset: 0x000287F8
        double CalcExamination(double[] pulseShape, int offset, int size)
        {
            double num = 0.0;
            for (int i = 0; i < this.pulseShapeSize; i++)
            {
                double num2 = (i + offset < size) ? pulseShape[i + offset] : 0.0;
                double num3 = this.idealPulseShape[i];
                double num4 = (num2 - num3) * (num2 - num3);
                num += num4;
            }
            return num;
        }

        // Token: 0x06000750 RID: 1872 RVA: 0x0002A65C File Offset: 0x0002885C
        bool Discriminate(double[] pulseShape, int size)
        {
            double num = 0.0;
            double num2 = 0.0;
            for (int i = 0; i < this.pulseShapeSize; i++)
            {
                double num3 = (i < size) ? pulseShape[i] : 0.0;
                double num4 = this.idealPulseShape[i];
                num += num3;
                num2 += num4;
            }
            num /= (double)this.pulseShapeSize;
            num2 /= (double)this.pulseShapeSize;
            double num5 = 0.0;
            double num6 = 0.0;
            double num7 = 0.0;
            for (int j = 0; j < this.pulseShapeSize; j++)
            {
                double num8 = pulseShape[j];
                double num9 = this.idealPulseShape[j];
                num5 += (num8 - num) * (num9 - num2);
                num6 += (num8 - num) * (num8 - num);
                num7 += (num9 - num2) * (num9 - num2);
            }
            double num10 = 0.0;
            if (num6 != 0.0 && num7 != 0.0)
            {
                num10 = num5 / Math.Sqrt(num6) / Math.Sqrt(num7);
            }
            return num10 > this.correlationThreshold;
        }

        // Token: 0x040003AF RID: 943
        CorrelativePulseDetector.DetectionState state;

        // Token: 0x040003B0 RID: 944
        double lowerThreshold = 1.0;

        // Token: 0x040003B1 RID: 945
        double upperThreshold = 100.0;

        // Token: 0x040003B2 RID: 946
        double correlationThreshold = 0.60000002384185791;

        // Token: 0x040003B3 RID: 947
        int pulseShapeSize = 16;

        // Token: 0x040003B4 RID: 948
        int peakIndex = 8;

        // Token: 0x040003B5 RID: 949
        double[] idealPulseShape;

        // Token: 0x040003B6 RID: 950
        double[] waveData;

        // Token: 0x040003B7 RID: 951
        double[] previousWaveData;

        // Token: 0x040003B8 RID: 952
        double[] pulseShape;

        // Token: 0x040003B9 RID: 953
        double[] peakPulseShape;

        // Token: 0x040003BA RID: 954
        double[] goodPulseShape;

        // Token: 0x040003BB RID: 955
        double[] badPulseShape;

        // Token: 0x040003BC RID: 956
        double height;

        // Token: 0x040003BD RID: 957
        double peakHeight;

        // Token: 0x040003BE RID: 958
        int width;

        // Token: 0x02000223 RID: 547
        enum DetectionState
        {
            // Token: 0x04000D89 RID: 3465
            Ready,
            // Token: 0x04000D8A RID: 3466
            Triggered
        }
    }
}
