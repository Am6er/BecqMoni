using BecquerelMonitor.Properties;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x02000118 RID: 280
    public class StandardPulseRecorder
    {
        // Token: 0x170003CA RID: 970
        // (get) Token: 0x06000EE0 RID: 3808 RVA: 0x00056020 File Offset: 0x00054220
        // (set) Token: 0x06000EE1 RID: 3809 RVA: 0x00056028 File Offset: 0x00054228
        public bool Recording
        {
            get
            {
                return this.recording;
            }
            set
            {
                this.recording = value;
            }
        }

        // Token: 0x170003CB RID: 971
        // (get) Token: 0x06000EE2 RID: 3810 RVA: 0x00056034 File Offset: 0x00054234
        // (set) Token: 0x06000EE3 RID: 3811 RVA: 0x0005603C File Offset: 0x0005423C
        public StandardPulseView StandardPulseView
        {
            get
            {
                return this.standardPulseView;
            }
            set
            {
                this.standardPulseView = value;
            }
        }

        // Token: 0x170003CC RID: 972
        // (get) Token: 0x06000EE4 RID: 3812 RVA: 0x00056048 File Offset: 0x00054248
        // (set) Token: 0x06000EE5 RID: 3813 RVA: 0x00056050 File Offset: 0x00054250
        public int NumberOfPulses
        {
            get
            {
                return this.numberOfPulses;
            }
            set
            {
                this.numberOfPulses = value;
            }
        }

        // Token: 0x06000EE7 RID: 3815 RVA: 0x00056094 File Offset: 0x00054294
        public void Initialize(AudioInputDeviceConfig inputDeviceConfig)
        {
            this.inputDeviceConfig = inputDeviceConfig;
            PRAHomageMethodConfig prahomageMethodConfig = (PRAHomageMethodConfig)inputDeviceConfig.PulseDetectionMethodConfig;
            this.pulseShapeSize = prahomageMethodConfig.PulseShapeSize;
            this.peakIndex = prahomageMethodConfig.PeakIndex;
            this.lowerThreshold = prahomageMethodConfig.PulseLowerThreshold;
            this.upperThreshold = prahomageMethodConfig.PulseUpperThreshold;
            this.state = StandardPulseRecorder.DetectionState.Ready;
            this.pulseShape = new double[this.pulseShapeSize];
            this.peakPulseShape = new double[this.pulseShapeSize];
            this.normalizedPulseShape = new double[this.pulseShapeSize];
            this.accumulatedPulseShape = new double[this.pulseShapeSize];
            this.standardPulseShape = new double[this.pulseShapeSize];
            this.numberOfPulses = 0;
            if (this.standardPulseView != null)
            {
                this.standardPulseView.PulseShape = this.standardPulseShape;
                this.standardPulseView.PulseShapeSize = this.pulseShapeSize;
                this.standardPulseView.PeakIndex = this.peakIndex;
                prahomageMethodConfig.NumberOfPulses = this.numberOfPulses;
            }
        }

        // Token: 0x06000EE8 RID: 3816 RVA: 0x00056198 File Offset: 0x00054398
        public bool StartRecording(WaveInDeviceCaps device, WaveFormat waveFormat, bool negativePolarity)
        {
            this.waveFormat = waveFormat;
            this.negativePolarity = negativePolarity;
            try
            {
                this.waveIn = new WaveIn(device.DeviceId);
                this.waveIn.BufferSize = waveFormat.SamplesPerSecond / 50 * 2;
                this.waveIn.BufferQueueSize = 50;
                if (!this.waveIn.SupportsFormat(waveFormat))
                {
                    MessageBox.Show(Resources.ERRNotSupportedWavFormat);
                    return false;
                }
            }
            catch (MMSystemException ex)
            {
                MessageBox.Show(ex.Message);
                this.waveIn.Dispose();
                return false;
            }
            Thread.Sleep(100);
            try
            {
                this.waveIn.Open(waveFormat);
            }
            catch (MMSystemException ex2)
            {
                MessageBox.Show(ex2.Message);
                this.waveIn.Dispose();
                return false;
            }
            this.startTime = DateTime.Now;
            this.recording = true;
            this.waveIn.DataReady += this.waveIn_DataReady;
            this.waveIn.Start();
            return true;
        }

        // Token: 0x06000EE9 RID: 3817 RVA: 0x000562B8 File Offset: 0x000544B8
        public void StopRecording()
        {
            this.measurementTime = DateTime.Now - this.startTime;
            this.recording = false;
            this.waveIn.Stop();
            this.waveIn.Close();
            this.waveIn.DataReady -= this.waveIn_DataReady;
            Thread.Sleep(200);
            this.waveIn.Dispose();
        }

        // Token: 0x06000EEA RID: 3818 RVA: 0x00056328 File Offset: 0x00054528
        void waveIn_DataReady(object sender, DataReadyEventArgs e)
        {
            this.ProcessWaveData(e.Data);
        }



        // Token: 0x06000EEB RID: 3819 RVA: 0x00056338 File Offset: 0x00054538
        void ProcessWaveData(byte[] data)
        {
            double[] array = null;
            int bitsPerSample = (int)this.waveFormat.BitsPerSample;
            if (bitsPerSample == 32)
            {
                array = new double[data.Length / 4];
                for (int i = 0; i < data.Length / 4; i++)
                {
                    double num6 = (double)BitConverter.ToInt32(data, i * 4) * 100.0 / 2147483648.0;
                    array[i] = (this.negativePolarity ? (-num6) : num6);
                }
            }
            if (bitsPerSample == 24)
            {
                array = new double[data.Length / 3];
                for (int i = 0; i < data.Length / 3; i++)
                {
                    int int24 = ((data[i * 3 + 2] << 24) | (data[i * 3 + 1] << 16) | (data[i * 3] << 8)) >> 8;
                    double num6 = (double)int24 * 100.0 / 8388608.0;
                    array[i] = (this.negativePolarity ? (-num6) : num6);
                }
            }
            if (bitsPerSample == 16)
            {
                array = new double[data.Length / 2];
                for (int j = 0; j < data.Length / 2; j++)
                {
                    double num7 = (double)BitConverter.ToInt16(data, j * 2) * 100.0 / 32768.0;
                    array[j] = (this.negativePolarity ? (-num7) : num7);
                }
            }
            this.DetectPulse(array);
        }

        // Token: 0x06000EEC RID: 3820 RVA: 0x00056474 File Offset: 0x00054674
        public void DetectPulse(double[] waveData)
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
            }
            this.previousWaveData = (double[])waveData.Clone();
        }

        // Token: 0x06000EED RID: 3821 RVA: 0x00056560 File Offset: 0x00054760
        void ProcessPulseTrigger()
        {
            this.height = this.pulseShape[this.peakIndex];
            switch (this.state)
            {
                case StandardPulseRecorder.DetectionState.Ready:
                    if (this.height >= this.lowerThreshold)
                    {
                        this.peakHeight = this.height;
                        this.width = 1;
                        this.pulseShape.CopyTo(this.peakPulseShape, 0);
                        this.state = StandardPulseRecorder.DetectionState.Triggered;
                        return;
                    }
                    break;
                case StandardPulseRecorder.DetectionState.Triggered:
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
                    if (this.peakHeight < this.upperThreshold && this.width < 20)
                    {
                        this.ProcessPulse();
                    }
                    this.state = StandardPulseRecorder.DetectionState.Ready;
                    break;
                default:
                    return;
            }
        }

        // Token: 0x06000EEE RID: 3822 RVA: 0x00056654 File Offset: 0x00054854
        void ProcessPulse()
        {
            this.numberOfPulses++;
            double[] array = this.NormalizePulseShape(this.peakPulseShape);
            for (int i = 0; i < this.pulseShapeSize; i++)
            {
                this.accumulatedPulseShape[i] += array[i];
                this.standardPulseShape[i] = this.accumulatedPulseShape[i] / (double)this.numberOfPulses;
            }
            PRAHomageMethodConfig prahomageMethodConfig = (PRAHomageMethodConfig)this.inputDeviceConfig.PulseDetectionMethodConfig;
            prahomageMethodConfig.NumberOfPulses = this.numberOfPulses;
            if (this.standardPulseView != null)
            {
                this.standardPulseView.PulseShape = this.standardPulseShape;
                this.standardPulseView.PulseShapeSize = this.pulseShapeSize;
                this.standardPulseView.PeakIndex = this.peakIndex;
                prahomageMethodConfig.NumberOfPulses = this.numberOfPulses;
            }
        }

        // Token: 0x06000EEF RID: 3823 RVA: 0x00056728 File Offset: 0x00054928
        double[] NormalizePulseShape(double[] shape)
        {
            double num = 0.0;
            for (int i = 0; i < this.pulseShapeSize; i++)
            {
                num += shape[i];
            }
            num /= (double)this.pulseShapeSize;
            for (int j = 0; j < this.pulseShapeSize; j++)
            {
                this.normalizedPulseShape[j] = shape[j] - num;
            }
            double num2 = -100.0;
            double num3 = 100.0;
            for (int k = 0; k < this.pulseShapeSize; k++)
            {
                if (this.normalizedPulseShape[k] > num2)
                {
                    num2 = this.normalizedPulseShape[k];
                }
                if (this.normalizedPulseShape[k] < num3)
                {
                    num3 = this.normalizedPulseShape[k];
                }
            }
            double num4 = 0.0;
            for (int l = 0; l < this.pulseShapeSize; l++)
            {
                num4 += this.normalizedPulseShape[l] * this.normalizedPulseShape[l];
            }
            double num5 = 0.0;
            if (num4 != 0.0)
            {
                num5 = (num2 - num3) / num4;
            }
            for (int m = 0; m < this.pulseShapeSize; m++)
            {
                this.normalizedPulseShape[m] *= num5;
            }
            return this.normalizedPulseShape;
        }

        // Token: 0x04000886 RID: 2182
        AudioInputDeviceConfig inputDeviceConfig;

        // Token: 0x04000887 RID: 2183
        WaveIn waveIn;

        // Token: 0x04000888 RID: 2184
        WaveFormat waveFormat;

        // Token: 0x04000889 RID: 2185
        bool negativePolarity;

        // Token: 0x0400088A RID: 2186
        bool recording;

        // Token: 0x0400088B RID: 2187
        DateTime startTime;

        // Token: 0x0400088C RID: 2188
        TimeSpan measurementTime;

        // Token: 0x0400088D RID: 2189
        int pulseShapeSize = 16;

        // Token: 0x0400088E RID: 2190
        int peakIndex = 8;

        // Token: 0x0400088F RID: 2191
        double lowerThreshold = 1.0;

        // Token: 0x04000890 RID: 2192
        double upperThreshold = 100.0;

        // Token: 0x04000891 RID: 2193
        StandardPulseRecorder.DetectionState state;

        // Token: 0x04000892 RID: 2194
        double[] waveData;

        // Token: 0x04000893 RID: 2195
        double[] previousWaveData;

        // Token: 0x04000894 RID: 2196
        double[] pulseShape;

        // Token: 0x04000895 RID: 2197
        double[] peakPulseShape;

        // Token: 0x04000896 RID: 2198
        double[] normalizedPulseShape;

        // Token: 0x04000897 RID: 2199
        double[] accumulatedPulseShape;

        // Token: 0x04000898 RID: 2200
        double[] standardPulseShape;

        // Token: 0x04000899 RID: 2201
        double height;

        // Token: 0x0400089A RID: 2202
        double peakHeight;

        // Token: 0x0400089B RID: 2203
        int width;

        // Token: 0x0400089C RID: 2204
        int numberOfPulses;

        // Token: 0x0400089D RID: 2205
        StandardPulseView standardPulseView;

        // Token: 0x02000227 RID: 551
        enum DetectionState
        {
            // Token: 0x04000DA3 RID: 3491
            Ready,
            // Token: 0x04000DA4 RID: 3492
            Triggered,
            // Token: 0x04000DA5 RID: 3493
            Rising,
            // Token: 0x04000DA6 RID: 3494
            Falling,
            // Token: 0x04000DA7 RID: 3495
            NotReady
        }
    }
}
