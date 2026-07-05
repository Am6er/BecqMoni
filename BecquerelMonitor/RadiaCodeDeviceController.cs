using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    class RadiaCodeDeviceController : DeviceController, IDisposable
    {
        private PulseDetector pulseDetector = new PulseDetector();
        private ResultDataStatus resultDataStatus = null;
        private bool new_document_created = false;
        private string deviceGuid;
        private ResultData resultData;
        private string status = "Unknown";
        private RadiaCodeIn subscribedInstance;

        public RadiaCodeDeviceController()
        {
            new_document_created = true;
            DeviceConfigManager.GetInstance().DeviceConfigListChanged += RadiaCodeDeviceController_DeviceConfigListChanged;
        }

        private void RadiaCodeDeviceController_DeviceConfigListChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            if (resultData == null || subscribedInstance == null || resultData.DeviceConfig == null || e == null)
            {
                return;
            }

            if (!string.Equals(resultData.DeviceConfig.Guid, e.Guid, StringComparison.Ordinal))
            {
                return;
            }

            if (DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(e.Guid, out DeviceConfigInfo updatedDeviceConfig) &&
                updatedDeviceConfig.InputDeviceConfig is RadiaCodeDeviceConfig deviceConfig)
            {
                subscribedInstance.setDeviceSerial(deviceConfig.DeviceSerial, deviceConfig.AddressBLE);
            }
        }

        public double getCPS()
        {
            if (deviceGuid != null)
            {
                return RadiaCodeIn.getInstance(deviceGuid).CPS;
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
                    RadiaCodeIn.getInstance(deviceGuid).sendCommand("Reset");
                    resultData.StartTime = DateTime.Now;
                } else
                {
                    MessageBox.Show($"Device is {this.status}, connect device first!");
                }
            }
        }

        public override bool StartMeasurement(ResultData resultData)
        {
            ResultDataStatus currentResultDataStatus = resultData.ResultDataStatus;
            this.resultData = resultData;
            deviceGuid = resultData.DeviceConfig.Guid;
            this.resultDataStatus = currentResultDataStatus;
            if (resultData.DeviceConfig.InputDeviceConfig.GetType() == typeof(RadiaCodeDeviceConfig))
            {
                this.pulseDetector.Pulses = resultData.PulseCollection;
                this.pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
                RadiaCodeDeviceConfig deviceConfig = (RadiaCodeDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                RadiaCodeIn instance = RadiaCodeIn.getInstance(deviceGuid);
                DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(resultData.DeviceConfig.Guid, out DeviceConfigInfo dci);
                if (dci != null && (dci.InputDeviceConfig is RadiaCodeDeviceConfig))
                {
                    RadiaCodeDeviceConfig dc = (RadiaCodeDeviceConfig)dci.InputDeviceConfig;
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

        private void RadiaCodeDeviceController_Status(object sender, RadiaCodeStatusArgs e)
        {
            this.status = e.Status;
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => setStatus(e.Status), null);
            }
        }

        private void setStatus(string status)
        {
            if (resultData != null && resultData.ResultDataStatus != null)
            {
                resultData.DetectorFeature = status;
                switch (status)
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

        void RadiaCodeDeviceController_PortFailure(object sender, EventArgs e)
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

        private void DataIn_DataReady(object sender, RadiaCodeInDataReadyArgs e)
        {
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => update_hystogram(e), null);
            }
        }

        private void update_hystogram(RadiaCodeInDataReadyArgs e)
        {
            if (this.resultDataStatus == null)
            {
                return;
            }

            if (this.resultDataStatus.Recording)
            {
                e.Hystogram.CopyTo(this.pulseDetector.EnergySpectrum.Spectrum, 0);
                this.pulseDetector.EnergySpectrum.TotalPulseCount = e.SUM;
                this.pulseDetector.EnergySpectrum.ValidPulseCount = e.SUM;
                this.pulseDetector.EnergySpectrum.ChannelPitch = 1;
                if (this.resultData != null)
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
                RadiaCodeIn.getInstance(deviceGuid).sendCommand("Stop");
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
            RadiaCodeIn.finishAll();
        }

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

        #region IDisposable Members

        public void Dispose()
        {
            DeviceConfigManager.GetInstance().DeviceConfigListChanged -= RadiaCodeDeviceController_DeviceConfigListChanged;
            UnsubscribeFromInstance();
        }

        #endregion

        private void SubscribeToInstance(RadiaCodeIn instance)
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
            instance.PortFailure += RadiaCodeDeviceController_PortFailure;
            instance.Status += RadiaCodeDeviceController_Status;
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
                subscribedInstance.PortFailure -= RadiaCodeDeviceController_PortFailure;
                subscribedInstance.Status -= RadiaCodeDeviceController_Status;
            }
            catch (Exception)
            {
            }

            subscribedInstance = null;
        }
    }
}
