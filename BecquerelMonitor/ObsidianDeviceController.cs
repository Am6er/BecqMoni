using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    class ObsidianDeviceController : DeviceController, IDisposable
    {
        private PulseDetector pulseDetector = new PulseDetector();
        private ResultDataStatus resultDataStatus = null;
        private bool new_document_created = false;
        private string deviceGuid;
        private ResultData resultData;
        private string status = "Unknown";
        private ObsidianIn subscribedInstance;

        public ObsidianDeviceController()
        {
            new_document_created = true;
            DeviceConfigManager.GetInstance().DeviceConfigListChanged += ObsidianDeviceController_DeviceConfigListChanged;
        }

        private void ObsidianDeviceController_DeviceConfigListChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            if (resultData == null || subscribedInstance == null)
            {
                return;
            }

            if (resultData.DeviceConfig != null &&
                resultData.DeviceConfig.InputDeviceConfig is ObsidianDeviceConfig deviceConfig)
            {
                subscribedInstance.setDeviceSerial(deviceConfig.DeviceSerial, deviceConfig.AddressBLE);
            }
        }

        public double getCPS()
        {
            if (deviceGuid != null)
            {
                return ObsidianIn.getInstance(deviceGuid).CPS;
            }
            return 0;
        }

        public string getTemp()
        {
            return "--";
        }

        public override void ClearMeasurementResult(ResultData resultData)
        {
            if (deviceGuid != null)
            {
                if (status == "Connected" || status == "Recording")
                {
                    ObsidianIn.getInstance(deviceGuid).sendCommand("Reset");
                    resultData.StartTime = DateTime.Now;
                }
                else
                {
                    MessageBox.Show($"Device is {status}, connect device first!");
                }
            }
        }

        public override bool StartMeasurement(ResultData resultData)
        {
            ResultDataStatus currentResultDataStatus = resultData.ResultDataStatus;
            this.resultData = resultData;
            deviceGuid = resultData.DeviceConfig.Guid;
            this.resultDataStatus = currentResultDataStatus;
            if (resultData.DeviceConfig.InputDeviceConfig.GetType() == typeof(ObsidianDeviceConfig))
            {
                pulseDetector.Pulses = resultData.PulseCollection;
                pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
                ObsidianDeviceConfig deviceConfig = (ObsidianDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                ObsidianIn instance = ObsidianIn.getInstance(deviceGuid);
                DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(resultData.DeviceConfig.Guid, out DeviceConfigInfo dci);
                if (dci != null && dci.InputDeviceConfig is ObsidianDeviceConfig)
                {
                    ObsidianDeviceConfig dc = (ObsidianDeviceConfig)dci.InputDeviceConfig;
                    instance.setDeviceSerial(dc.DeviceSerial, dc.AddressBLE);
                }
                else
                {
                    instance.setDeviceSerial(deviceConfig.DeviceSerial, deviceConfig.AddressBLE);
                }

                SubscribeToInstance(instance);
                currentResultDataStatus.Recording = true;
                if (new_document_created)
                {
                    resultData.StartTime = DateTime.Now;
                    new_document_created = false;
                }
                resultData.DetectorFeature = "Starting";
                instance.sendCommand("Start");
                return true;
            }
            return false;
        }

        private void ObsidianDeviceController_Status(object sender, ObsidianStatusArgs e)
        {
            status = e.Status;
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => setStatus(e.Status), null);
            }
        }

        private void setStatus(string currentStatus)
        {
            if (resultData != null && resultData.ResultDataStatus != null)
            {
                resultData.DetectorFeature = currentStatus;
                switch (currentStatus)
                {
                    case "Recording":
                        resultData.ResultDataStatus.Recording = true;
                        break;
                    case "Faulted":
                    case "Stopped":
                    case "Disconnected":
                        resultData.ResultDataStatus.Recording = false;
                        break;
                }
            }
        }

        public override bool AttachToDevice(ResultData resultData)
        {
            throw new NotImplementedException();
        }

        void ObsidianDeviceController_PortFailure(object sender, EventArgs e)
        {
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => port_failure_stop(), null);
            }
        }

        private void port_failure_stop()
        {
            if (resultData != null && resultData.ResultDataStatus.Recording)
            {
                resultData.MeasurementController.StopRecording();
                resultData.ResultDataStatus.Recording = false;
                MessageBox.Show(Properties.Resources.ERRDetectedErrorWhileCommunication, Properties.Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void DataIn_DataReady(object sender, ObsidianInDataReadyArgs e)
        {
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => update_hystogram(e), null);
            }
        }

        private void update_hystogram(ObsidianInDataReadyArgs e)
        {
            if (resultDataStatus == null)
            {
                return;
            }

            if (resultDataStatus.Recording)
            {
                e.Hystogram.CopyTo(pulseDetector.EnergySpectrum.Spectrum, 0);
                pulseDetector.EnergySpectrum.TotalPulseCount = e.SUM;
                pulseDetector.EnergySpectrum.ValidPulseCount = e.SUM;
                pulseDetector.EnergySpectrum.ChannelPitch = 1;
                if (resultData != null)
                {
                    resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(e.ElapsedTime);
                    resultData.ResultDataStatus.ElapsedTime = resultData.ResultDataStatus.TotalTime;
                }
            }
        }

        public override void StopMeasurement(ResultData resultData)
        {
            ResultDataStatus currentResultDataStatus = resultData.ResultDataStatus;
            if (deviceGuid != null)
            {
                ObsidianIn.getInstance(deviceGuid).sendCommand("Stop");
                resultData.EndTime = DateTime.Now;
                currentResultDataStatus.Recording = false;
            }
        }

        public override void DetachFromDevice(ResultData resultData)
        {
            throw new NotImplementedException();
        }

        public void applicationCLose()
        {
            ObsidianIn.finishAll();
        }

        public PulseDetector PulseDetector
        {
            get { return pulseDetector; }
            set { pulseDetector = value; }
        }

        public void Dispose()
        {
            DeviceConfigManager.GetInstance().DeviceConfigListChanged -= ObsidianDeviceController_DeviceConfigListChanged;
            UnsubscribeFromInstance();
        }

        private void SubscribeToInstance(ObsidianIn instance)
        {
            if (ReferenceEquals(subscribedInstance, instance))
            {
                return;
            }

            UnsubscribeFromInstance();
            if (instance == null)
            {
                return;
            }

            instance.DataReady += DataIn_DataReady;
            instance.PortFailure += ObsidianDeviceController_PortFailure;
            instance.Status += ObsidianDeviceController_Status;
            subscribedInstance = instance;
        }

        private void UnsubscribeFromInstance()
        {
            if (subscribedInstance == null)
            {
                return;
            }

            try
            {
                subscribedInstance.DataReady -= DataIn_DataReady;
                subscribedInstance.PortFailure -= ObsidianDeviceController_PortFailure;
                subscribedInstance.Status -= ObsidianDeviceController_Status;
            }
            catch (Exception)
            {
            }
            subscribedInstance = null;
        }
    }
}
