﻿using BecquerelMonitor.Properties;
using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    class AtomSpectraDeviceController : DeviceController, IDisposable
    {
        private PulseDetector pulseDetector = new PulseDetector();
        private ResultDataStatus resultDataStatus = null;
        private bool new_document_created = false;
        private string deviceGuid;
        private ResultData resultData;
        private bool already_subscribed = false;
        private string previous_guid;

        public AtomSpectraDeviceController()
        {
            new_document_created = true;
            DeviceConfigManager.GetInstance().DeviceConfigListChanged += AtomSpectraDeviceController_DeviceConfigListChanged;
        }

        private void AtomSpectraDeviceController_DeviceConfigListChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            /*
            if (deviceGuid == null) return;
            if (!e.Guid.Equals(deviceGuid)) return;
            DeviceConfigInfo dci = DeviceConfigManager.GetInstance().DeviceConfigMap[e.Guid];
            if (dci == null) return;
            if (dci.InputDeviceConfig is AtomSpectraDeviceConfig)
            {
                AtomSpectraDeviceConfig dc = (AtomSpectraDeviceConfig)dci.InputDeviceConfig;
                AtomSpectraVCPIn.getInstance(deviceGuid).setPort(dc.ComPortName);
            }*/
            already_subscribed = false;
        }

        public double getCPS()
        {
            if (deviceGuid != null)
            {
                return AtomSpectraVCPIn.getInstance(deviceGuid).CPS;
            }
            return 0;
        }

        public string getTemp()
        {
            if (deviceGuid != null)
            {
                try
                {
                    AtomSpectraVCPIn.getInstance(deviceGuid).sendCommand("-inf");
                    String resultStr = AtomSpectraVCPIn.getInstance(deviceGuid).getCommandOutput(4000);
                    String result = resultStr.Split(new string[] { "T1 " }, StringSplitOptions.None)[1].Split(' ')[0];
                    return result;
                } catch (Exception)
                {
                    return "--";
                }
            }
            return "--";
        }

        public override void ClearMeasurementResult(ResultData resultData)
        {
            if (deviceGuid != null)
            {
                AtomSpectraVCPIn.getInstance(deviceGuid).sendCommand("-rst");
                AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("-ok", 1000);
                resultData.StartTime = DateTime.Now;
            }
        }

        public override bool StartMeasurement(ResultData resultData)
        {
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            this.resultData = resultData;
            deviceGuid = resultData.DeviceConfig.Guid;
            this.resultDataStatus = resultDataStatus;
            if (resultData.DeviceConfig.InputDeviceConfig.GetType() == typeof(AtomSpectraDeviceConfig))
            {
                this.pulseDetector.Pulses = resultData.PulseCollection;
                this.pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
                AtomSpectraDeviceConfig deviceConfig = (AtomSpectraDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                DeviceConfigInfo dci = DeviceConfigManager.GetInstance().DeviceConfigMap[resultData.DeviceConfig.Guid];
                if (dci != null && (dci.InputDeviceConfig is AtomSpectraDeviceConfig))
                {
                    AtomSpectraDeviceConfig dc = (AtomSpectraDeviceConfig)dci.InputDeviceConfig;
                    AtomSpectraVCPIn.getInstance(deviceGuid).setPort(dc.ComPortName, dc.BaudRate);
                }
                else
                {
                    AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).setPort(deviceConfig.ComPortName, deviceConfig.BaudRate);
                }

                if (previous_guid != null)
                {
                    if (!previous_guid.Equals(resultData.DeviceConfig.Guid))
                    {
                        already_subscribed = false;
                        try
                        {
                            AtomSpectraVCPIn.getInstance(previous_guid).DataReady -= DataIn_DataReady;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                if (!already_subscribed)
                {
                    AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).DataReady += DataIn_DataReady;
                    already_subscribed = true;
                }

                previous_guid = resultData.DeviceConfig.Guid;
                //AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).PortFailure += AtomSpectraDeviceController_PortFailure;
                bool commands_accepted = true;
                if (new_document_created)
                {
                    AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("-sto");
                    commands_accepted &= AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("-ok", 1000);
                    AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("-rst");
                    commands_accepted &= AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("-ok", 1000); 
                }
                AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("-sta");
                // Slow baudrates support
                // Expected: "Warning: silent mode forced due to low interface speed-ok"
                AtomSpectraDeviceConfig devconfig = (AtomSpectraDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                if (devconfig.BaudRate == 38400 || devconfig.BaudRate == 115200)
                {
                    commands_accepted &= AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("Warning: silent mode forced due to low interface speed-ok", 2000);
                } else
                {
                    commands_accepted &= AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("-ok", 1000);
                }
                if (new_document_created)
                {
                    resultData.StartTime = DateTime.Now;
                    if (commands_accepted) new_document_created = false;
                }
                else
                {
                    //resultData.StartTime.Add = stopTimestamp;
                }
                resultDataStatus.Recording = commands_accepted;
                if (commands_accepted) return true;
            }
            MessageBox.Show(Resources.ERRReadDataFromPort_Empty, Resources.ErrorString, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        public override bool AttachToDevice(ResultData resultData)
        {
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            this.resultData = resultData;
            deviceGuid = resultData.DeviceConfig.Guid;
            this.resultDataStatus = resultDataStatus;
            if (resultData.DeviceConfig.InputDeviceConfig.GetType() == typeof(AtomSpectraDeviceConfig))
            {
                this.pulseDetector.Pulses = resultData.PulseCollection;
                this.pulseDetector.EnergySpectrum = resultData.EnergySpectrum;
                AtomSpectraDeviceConfig deviceConfig = (AtomSpectraDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                DeviceConfigInfo dci = DeviceConfigManager.GetInstance().DeviceConfigMap[resultData.DeviceConfig.Guid];
                if (dci != null && (dci.InputDeviceConfig is AtomSpectraDeviceConfig))
                {
                    AtomSpectraDeviceConfig dc = (AtomSpectraDeviceConfig)dci.InputDeviceConfig;
                    AtomSpectraVCPIn.getInstance(deviceGuid).setPort(dc.ComPortName, dc.BaudRate);
                }
                else
                {
                    AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).setPort(deviceConfig.ComPortName, deviceConfig.BaudRate);
                }

                if (previous_guid != null)
                {
                    if (!previous_guid.Equals(resultData.DeviceConfig.Guid))
                    {
                        already_subscribed = false;
                        try
                        {
                            AtomSpectraVCPIn.getInstance(previous_guid).DataReady -= DataIn_DataReady;
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                if (!already_subscribed)
                {
                    AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).DataReady += DataIn_DataReady;
                    already_subscribed = true;
                }

                previous_guid = resultData.DeviceConfig.Guid;
                //AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).PortFailure += AtomSpectraDeviceController_PortFailure;
                bool commands_accepted = true;
                AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).sendCommand("-sta");
                // Slow baudrates support
                // Expected: "Warning: silent mode forced due to low interface speed-ok"
                AtomSpectraDeviceConfig devconfig = (AtomSpectraDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                if (devconfig.BaudRate == 38400 || devconfig.BaudRate == 115200)
                {
                    commands_accepted &= AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("Warning: silent mode forced due to low interface speed-ok", 2000);
                }
                else
                {
                    commands_accepted &= AtomSpectraVCPIn.getInstance(resultData.DeviceConfig.Guid).waitForAnswer("-ok", 1000);
                }
                if (new_document_created)
                {
                    resultData.StartTime = DateTime.Now;
                    if (commands_accepted) new_document_created = false;
                }
                else
                {
                    //resultData.StartTime.Add = stopTimestamp;
                }
                resultDataStatus.Recording = commands_accepted;
                if (commands_accepted) return true;
            }
            MessageBox.Show(Resources.ERRReadDataFromPort_Empty, Resources.ErrorString, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        void AtomSpectraDeviceController_PortFailure(object sender, EventArgs e)
        {
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => port_failure_stop(), null);
            }
        }

        private void port_failure_stop()
        {
            if (resultData != null)
            {
                resultData.MeasurementController.StopRecording();
                resultData.ResultDataStatus.Recording = false;
            }
        }

        private void DataIn_DataReady(object sender, AtomSpectraVCPInDataReadyArgs e)
        {
            if (MainForm.originalContext != null)
            {
                MainForm.originalContext.Post(d => update_hystogram(e), null);
            }
        }

        private void update_hystogram(AtomSpectraVCPInDataReadyArgs e)
        {
            if (this.resultDataStatus.Recording)
            {
                if (e.Hystogram.Length != this.pulseDetector.EnergySpectrum.Spectrum.Length)
                {
                    MessageBox.Show(Resources.ERRIncompatibleChannelParameters);
                    resultData.MeasurementController.StopRecording();
                    resultData.ResultDataStatus.Recording = false;
                    return;
                }
                e.Hystogram.CopyTo(this.pulseDetector.EnergySpectrum.Spectrum, 0);
                int sum = e.Hystogram.Sum();
                this.pulseDetector.EnergySpectrum.TotalPulseCount = sum + e.InvalidPulses;
                this.pulseDetector.EnergySpectrum.ValidPulseCount = sum;
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
                // Preset time elapsed
                if (resultDataStatus.ElapsedTime.TotalSeconds >= (double)resultDataStatus.PresetTime)
                {
                    // Wait 3 seconds on case user "attach" to Atomspectra Pro device with empty spectrum document
                    for (int i = 0; i <= 30; i++)
                    {
                        Thread.Sleep(100);
                    }
                }
                AtomSpectraVCPIn.getInstance(deviceGuid).sendCommand("-sto");
                resultData.EndTime = DateTime.Now;
                resultDataStatus.Recording = !AtomSpectraVCPIn.getInstance(deviceGuid).waitForAnswer("-ok", 1000);
            }
        }

        public override void DetachFromDevice(ResultData resultData)
        {
            ResultDataStatus resultDataStatus = resultData.ResultDataStatus;
            if (deviceGuid != null)
            {
                resultData.EndTime = DateTime.Now;
                resultDataStatus.Recording = false;
            }
        }

        public void applicationCLose()
        {
            AtomSpectraVCPIn.finishAll();
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
