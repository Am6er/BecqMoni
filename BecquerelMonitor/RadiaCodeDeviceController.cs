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
        private bool already_subscribed = false;
        private string previous_guid;
        private string status = "Unknown";

        public RadiaCodeDeviceController()
        {
            new_document_created = true;
            DeviceConfigManager.GetInstance().DeviceConfigListChanged += RadiaCodeDeviceController_DeviceConfigListChanged;
        }

        private void RadiaCodeDeviceController_DeviceConfigListChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            already_subscribed = false;
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
                if (status == "Connected")
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
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            this.resultData = resultData;
            deviceGuid = resultData.DeviceConfig.Guid;
            this.resultDataStatus = resultDataStatus;
            if (resultData.DeviceConfig.InputDeviceConfig.GetType() == typeof(RadiaCodeDeviceConfig))
            {
                this.pulseDetector.Pulses = resultData.PulseCollection;
                this.pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
                RadiaCodeDeviceConfig deviceConfig = (RadiaCodeDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                DeviceConfigInfo dci = DeviceConfigManager.GetInstance().DeviceConfigMap[resultData.DeviceConfig.Guid];
                if (dci != null && (dci.InputDeviceConfig is RadiaCodeDeviceConfig))
                {
                    RadiaCodeDeviceConfig dc = (RadiaCodeDeviceConfig)dci.InputDeviceConfig;
                    RadiaCodeIn.getInstance(deviceGuid).setDeviceSerial(dc.DeviceSerial, dc.AddressBLE);
                }
                else
                {
                    RadiaCodeIn.getInstance(resultData.DeviceConfig.Guid).setDeviceSerial(deviceConfig.DeviceSerial, deviceConfig.AddressBLE);
                }

                if (previous_guid != null)
                {
                    if (!previous_guid.Equals(resultData.DeviceConfig.Guid))
                    {
                        already_subscribed = false;
                        try
                        {
                            RadiaCodeIn.getInstance(previous_guid).DataReady -= DataIn_DataReady;
                            RadiaCodeIn.getInstance(previous_guid).PortFailure -= RadiaCodeDeviceController_PortFailure;
                            RadiaCodeIn.getInstance(previous_guid).Status -= RadiaCodeDeviceController_Status;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                if (!already_subscribed)
                {
                    RadiaCodeIn.getInstance(resultData.DeviceConfig.Guid).DataReady += DataIn_DataReady;
                    RadiaCodeIn.getInstance(resultData.DeviceConfig.Guid).PortFailure += RadiaCodeDeviceController_PortFailure;
                    RadiaCodeIn.getInstance(resultData.DeviceConfig.Guid).Status += RadiaCodeDeviceController_Status;
                    already_subscribed = true;
                }

                previous_guid = resultData.DeviceConfig.Guid;
                bool commands_accepted = true;
                if (new_document_created)
                {
                    RadiaCodeIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("Start");
                }
                RadiaCodeIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("Start");
                RadiaCodeDeviceConfig devconfig = (RadiaCodeDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                //TODO add check
                commands_accepted &= true;
                if (new_document_created)
                {
                    resultData.StartTime = DateTime.Now;
                    if (commands_accepted) new_document_created = false;
                }
                resultDataStatus.Recording = commands_accepted;
                if (commands_accepted) return true;
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
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            if (deviceGuid != null)
            {
                RadiaCodeIn.getInstance(deviceGuid).sendCommand("Stop");
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
            throw new NotImplementedException();
        }

        #endregion
    }
}
