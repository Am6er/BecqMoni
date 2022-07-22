using BecquerelMonitor.Properties;
using System;
using System.Windows.Forms;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x020000C4 RID: 196
    public class AudioInputDeviceController : DeviceController
    {
        // Token: 0x17000295 RID: 661
        // (get) Token: 0x0600096E RID: 2414 RVA: 0x000373D8 File Offset: 0x000355D8
        // (set) Token: 0x0600096F RID: 2415 RVA: 0x000373E0 File Offset: 0x000355E0
        public PulseDetector PulseDetector
        {
            get
            {
                return this.pulseDetector;
            }
            set
            {
                this.pulseDetector = value;
            }
        }

        // Token: 0x06000970 RID: 2416 RVA: 0x000373EC File Offset: 0x000355EC
        public override bool StartMeasurement(ResultData resultData)
        {
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            DeviceConfigInfo deviceConfig = resultData.DeviceConfig;

            if (!(deviceConfig.InputDeviceConfig is AudioInputDeviceConfig))
            {
                MessageBox.Show(Resources.ERRDeviceMismatchConfiguration, Resources.ErrorExclamation);
                return false;
            }

            AudioInputDeviceConfig audioInputDeviceConfig = (AudioInputDeviceConfig)deviceConfig.InputDeviceConfig;

            WaveFormat waveFormat = this.ConstructWaveFormat(audioInputDeviceConfig);
            int deviceId = audioInputDeviceConfig.AudioInputDevice.DeviceId;
            WaveIn waveIn = new WaveIn(deviceId);
            resultDataStatus.WaveIn = waveIn;
            waveIn.BufferSize = waveFormat.SamplesPerSecond / 50 * 2;
            waveIn.BufferQueueSize = 50;
            try
            {
                if (!waveIn.SupportsFormat(waveFormat))
                {
                    MessageBox.Show(Resources.ERRNotSupportedWavFormat);
                    waveIn.Dispose();
                    return false;
                }
            }
            catch (MMSystemException ex)
            {
                MessageBox.Show(ex.Message);
                waveIn.Dispose();
                return false;
            }
            GC.Collect();
            this.pulseDetector.Pulses = resultData.PulseCollection;
            this.pulseDetector.Initialize(audioInputDeviceConfig, waveFormat, resultDataStatus.TimeInSamples);
            this.pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
            resultDataStatus.AudioVolumeChanged = false;
            if (audioInputDeviceConfig.AutoVolumeSetting)
            {
                try
                {
                    resultDataStatus.PreviousVolume = this.audioVolumeController.GetVolume(deviceId);
                    this.audioVolumeController.SetVolume(deviceId, (float)audioInputDeviceConfig.Volume / 100f);
                    resultDataStatus.AudioVolumeChanged = true;
                }
                catch (Exception)
                {
                }
            }
            try
            {
                waveIn.Open(waveFormat);
            }
            catch (MMSystemException ex2)
            {
                MessageBox.Show(ex2.Message);
                waveIn.Dispose();
                return false;
            }
            resultData.StartTime = DateTime.Now;
            if (resultData.EnergySpectrum.TotalPulseCount == 0)
            {
                resultData.StartTime = DateTime.Now;
            }
            resultDataStatus.TimeInSamples = resultData.EnergySpectrum.NumberOfSamples;
            resultDataStatus.Recording = true;
            waveIn.DataReady += this.waveIn_DataReady;
            waveIn.Start();
            return true;
        }

        // Token: 0x06000971 RID: 2417 RVA: 0x000375D8 File Offset: 0x000357D8
        public WaveFormat ConstructWaveFormat(AudioInputDeviceConfig audioConfig)
        {
            return new WaveFormat
            {
                FormatTag = WaveFormatTag.Pcm,
                SamplesPerSecond = audioConfig.SamplesPerSecond,
                BitsPerSample = (short)audioConfig.BitsPerSample,
                Channels = 1
            };
        }

        // Token: 0x06000972 RID: 2418 RVA: 0x00037618 File Offset: 0x00035818
        void waveIn_DataReady(object sender, DataReadyEventArgs e)
        {
            this.pulseDetector.ProcessWaveData(e.Data);
        }

        // Token: 0x06000973 RID: 2419 RVA: 0x0003762C File Offset: 0x0003582C
        public override void StopMeasurement(ResultData resultData)
        {
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            DeviceConfigInfo deviceConfig = resultData.DeviceConfig;
            AudioInputDeviceConfig audioInputDeviceConfig = (AudioInputDeviceConfig)deviceConfig.InputDeviceConfig;
            WaveIn waveIn = resultDataStatus.WaveIn;
            waveIn.Stop();
            waveIn.Close();
            waveIn.DataReady -= this.waveIn_DataReady;
            resultDataStatus.TotalTime += DateTime.Now - resultData.StartTime;
            resultDataStatus.ElapsedTime = resultDataStatus.TotalTime;
            resultDataStatus.TimeInSamples = this.pulseDetector.TimeInSamples;
            resultData.EndTime = DateTime.Now;
            resultData.EnergySpectrum.MeasurementTime = resultDataStatus.TotalTime.TotalSeconds;
            resultData.EnergySpectrum.NumberOfSamples = resultDataStatus.TimeInSamples;
            resultDataStatus.Recording = false;
            waveIn.Dispose();
            GC.Collect();
            if (resultDataStatus.AudioVolumeChanged)
            {
                this.audioVolumeController.SetVolume(audioInputDeviceConfig.AudioInputDevice.DeviceId, resultDataStatus.PreviousVolume);
            }
        }

        // Token: 0x06000974 RID: 2420 RVA: 0x0003772C File Offset: 0x0003592C
        public override void ClearMeasurementResult(ResultData resultData)
        {
        }

        // Token: 0x04000543 RID: 1347
        AudioVolumeController audioVolumeController = new AudioVolumeController();

        // Token: 0x04000544 RID: 1348
        PulseDetector pulseDetector = new CorrelativePulseDetector();
    }
}
