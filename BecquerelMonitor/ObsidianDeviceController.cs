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
        private bool already_subscribed = false;
        private string previous_guid;
        private string status = "Unknown";

        public ObsidianDeviceController()
        {
            new_document_created = true;
            DeviceConfigManager.GetInstance().DeviceConfigListChanged += ObsidianDeviceController_DeviceConfigListChanged;
        }

        private void ObsidianDeviceController_DeviceConfigListChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            already_subscribed = false;
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
                if (status == "Connected")
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
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            this.resultData = resultData;
            deviceGuid = resultData.DeviceConfig.Guid;
            this.resultDataStatus = resultDataStatus;
            if (resultData.DeviceConfig.InputDeviceConfig.GetType() == typeof(ObsidianDeviceConfig))
            {
                pulseDetector.Pulses = resultData.PulseCollection;
                pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
                ObsidianDeviceConfig deviceConfig = (ObsidianDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                DeviceConfigInfo dci = DeviceConfigManager.GetInstance().DeviceConfigMap[resultData.DeviceConfig.Guid];
                if (dci != null && dci.InputDeviceConfig is ObsidianDeviceConfig)
                {
                    ObsidianDeviceConfig dc = (ObsidianDeviceConfig)dci.InputDeviceConfig;
                    ObsidianIn.getInstance(deviceGuid).setDeviceSerial(dc.DeviceSerial, dc.AddressBLE);
                }
                else
                {
                    ObsidianIn.getInstance(resultData.DeviceConfig.Guid).setDeviceSerial(deviceConfig.DeviceSerial, deviceConfig.AddressBLE);
                }

                if (previous_guid != null && !previous_guid.Equals(resultData.DeviceConfig.Guid))
                {
                    already_subscribed = false;
                    try
                    {
                        ObsidianIn.getInstance(previous_guid).DataReady -= DataIn_DataReady;
                        ObsidianIn.getInstance(previous_guid).PortFailure -= ObsidianDeviceController_PortFailure;
                        ObsidianIn.getInstance(previous_guid).Status -= ObsidianDeviceController_Status;
                    }
                    catch (Exception)
                    {
                    }
                }

                if (!already_subscribed)
                {
                    ObsidianIn.getInstance(resultData.DeviceConfig.Guid).DataReady += DataIn_DataReady;
                    ObsidianIn.getInstance(resultData.DeviceConfig.Guid).PortFailure += ObsidianDeviceController_PortFailure;
                    ObsidianIn.getInstance(resultData.DeviceConfig.Guid).Status += ObsidianDeviceController_Status;
                    already_subscribed = true;
                }

                previous_guid = resultData.DeviceConfig.Guid;
                bool commands_accepted = true;
                ObsidianIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("Start");
                if (new_document_created)
                {
                    resultData.StartTime = DateTime.Now;
                    if (commands_accepted)
                    {
                        new_document_created = false;
                    }
                }
                resultDataStatus.Recording = commands_accepted;
                if (commands_accepted)
                {
                    return true;
                }
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
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            if (deviceGuid != null)
            {
                ObsidianIn.getInstance(deviceGuid).sendCommand("Stop");
                resultData.EndTime = DateTime.Now;
                resultDataStatus.Recording = false;
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
            throw new NotImplementedException();
        }
    }
}
