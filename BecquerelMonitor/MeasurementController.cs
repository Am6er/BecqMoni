using BecquerelMonitor.Properties;
using System;
using System.Media;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000083 RID: 131
    public class MeasurementController
    {
        // Token: 0x14000019 RID: 25
        // (add) Token: 0x060006A0 RID: 1696 RVA: 0x00027F40 File Offset: 0x00026140
        // (remove) Token: 0x060006A1 RID: 1697 RVA: 0x00027F7C File Offset: 0x0002617C
        public event EventHandler MeasurementTerminated;

        // Token: 0x170001FA RID: 506
        // (get) Token: 0x060006A2 RID: 1698 RVA: 0x00027FB8 File Offset: 0x000261B8
        // (set) Token: 0x060006A3 RID: 1699 RVA: 0x00027FC0 File Offset: 0x000261C0
        public DocEnergySpectrum Document
        {
            get
            {
                return this.document;
            }
            set
            {
                this.document = value;
            }
        }

        // Token: 0x170001FB RID: 507
        // (get) Token: 0x060006A4 RID: 1700 RVA: 0x00027FCC File Offset: 0x000261CC
        // (set) Token: 0x060006A5 RID: 1701 RVA: 0x00027FD4 File Offset: 0x000261D4
        public ResultData ResultData
        {
            get
            {
                return this.resultData;
            }
            set
            {
                this.resultData = value;
            }
        }

        // Token: 0x170001FC RID: 508
        // (get) Token: 0x060006A6 RID: 1702 RVA: 0x00027FE0 File Offset: 0x000261E0
        // (set) Token: 0x060006A7 RID: 1703 RVA: 0x00027FE8 File Offset: 0x000261E8
        public bool SaveOnMeasurementEnd
        {
            get
            {
                return this.saveOnMeasurementEnd;
            }
            set
            {
                this.saveOnMeasurementEnd = value;
            }
        }

        // Token: 0x170001FD RID: 509
        // (get) Token: 0x060006A8 RID: 1704 RVA: 0x00027FF4 File Offset: 0x000261F4
        // (set) Token: 0x060006A9 RID: 1705 RVA: 0x00027FFC File Offset: 0x000261FC
        public DeviceController DeviceController
        {
            get
            {
                return this.deviceController;
            }
            set
            {
                this.deviceController = value;
            }
        }

        // Token: 0x060006AA RID: 1706 RVA: 0x00028008 File Offset: 0x00026208
        public MeasurementController(DocEnergySpectrum document, ResultData resultData)
        {
            this.document = document;
            this.resultData = resultData;
        }

        // Token: 0x060006AB RID: 1707 RVA: 0x00028040 File Offset: 0x00026240
        public bool StartRecording()
        {
            ResultDataStatus resultDataStatus = this.resultData.ResultDataStatus;
            if (!this.CreateDeviceController())
            {
                return false;
            }
            try
            {
                bool result = this.deviceController.StartMeasurement(this.resultData);
                if (!result) return false;
            } catch (Exception)
            {
                MessageBox.Show(Resources.ERRBTNotSupportedByOS);
                return false;
            }
            
            return true;
        }

        public bool AttachToDevice()
        {
            ResultDataStatus resultDataStatus = this.resultData.ResultDataStatus;
            if (!this.CreateDeviceController())
            {
                return false;
            }
            return this.deviceController.AttachToDevice(this.resultData);
        }

        // Token: 0x060006AC RID: 1708 RVA: 0x00028070 File Offset: 0x00026270
        bool CreateDeviceController()
        {
            DeviceConfigInfo deviceConfig = this.resultData.DeviceConfig;
            if (deviceConfig == null || deviceConfig.Guid == null || !this.deviceConfigManager.DeviceConfigMap.ContainsKey(deviceConfig.Guid))
            {
                MessageBox.Show(Resources.ERRDeviceConfigNotSelected);
                return false;
            }
            DeviceType deviceType = null;
            DeviceType.DeviceTypeMap.TryGetValue(this.resultData.DeviceConfig.DeviceType, out deviceType);
            if (deviceType == null)
            {
                MessageBox.Show(Resources.ERRInvalidDeviceType);
                return false;
            }
            if (deviceType.DeviceControllerType == typeof(AudioInputDeviceController))
            {
                this.deviceController = (DeviceController)Activator.CreateInstance(deviceType.DeviceControllerType);
            }
            else if (deviceType.DeviceControllerType == typeof(AtomSpectraDeviceController))
            {
                //if (this.deviceController != null)
                //{
                //    ((AtomSpectraDeviceController)this.deviceController).SuspendThread();
                //    deviceController = null;
                //}
                if (this.deviceController == null)
                {
                    this.deviceController = (DeviceController)Activator.CreateInstance(deviceType.DeviceControllerType);
                }
            }
            else if (deviceType.DeviceControllerType == typeof(RadiaCodeDeviceController))
            {
                if (this.deviceController == null)
                {
                    this.deviceController = (DeviceController)Activator.CreateInstance(deviceType.DeviceControllerType);
                }
            }
            return true;
        }

        // Token: 0x060006AD RID: 1709 RVA: 0x00028110 File Offset: 0x00026310
        public void StopRecording()
        {
            if (this.deviceController == null)
            {
                return;
            }
            this.deviceController.StopMeasurement(this.resultData);
            if (this.MeasurementTerminated != null)
            {
                this.MeasurementTerminated(this, new EventArgs());
            }
        }

        public void DetachFromDevice()
        {
            if (this.deviceController == null)
            {
                return;
            }
            this.deviceController.DetachFromDevice(this.resultData);
            if (this.MeasurementTerminated != null)
            {
                this.MeasurementTerminated(this, new EventArgs());
            }
        }

        // Token: 0x060006AE RID: 1710 RVA: 0x0002814C File Offset: 0x0002634C
        public void ClearMeasurementResult()
        {
            if (this.deviceController == null)
            {
                return;
            }
            try
            {
                this.deviceController.ClearMeasurementResult(this.resultData);
            } catch (Exception)
            {
                MessageBox.Show(Resources.ERRBTNotSupportedByOS);
            }
        }

        // Token: 0x060006AF RID: 1711 RVA: 0x0002816C File Offset: 0x0002636C
        public void OnTimer(object sender, EventArgs e)
        {
            ResultDataStatus resultDataStatus = this.resultData.ResultDataStatus;
            if (resultDataStatus.Recording)
            {
                if (this.resultData.MeasurementController.DeviceController is AtomSpectraDeviceController)
                {
                    //resultDataStatus.ElapsedTime = resultDataStatus.TotalTime;
                    AtomSpectraDeviceConfig devconfig = (AtomSpectraDeviceConfig)resultData.DeviceConfig.InputDeviceConfig;
                    if (devconfig.BaudRate == 38400 || devconfig.BaudRate == 115200)
                    {
                        AtomSpectraVCPIn.getInstance(this.resultData.DeviceConfig.Guid).sendCommand("-sho");
                        AtomSpectraVCPIn.getInstance(this.resultData.DeviceConfig.Guid).waitForAnswer("-ok collecting", 1000);
                    }
                }
                else if (this.resultData.MeasurementController.DeviceController is RadiaCodeDeviceController)
                {
                    //resultDataStatus.ElapsedTime = resultDataStatus.TotalTime;
                }
                else if (this.resultData.MeasurementController.DeviceController is AudioInputDeviceController)
                {
                    resultDataStatus.ElapsedTime = DateTime.Now - this.resultData.StartTime + resultDataStatus.TotalTime;
                }
                this.resultData.EnergySpectrum.MeasurementTime = resultDataStatus.ElapsedTime.TotalSeconds;
                this.resultData.EnergySpectrum.LiveTime = Utils.LiveTime.Calculate(this.resultData.EnergySpectrum.MeasurementTime,
                    this.resultData.EnergySpectrum.TotalPulseCount,
                    this.resultData.DeviceConfig.InputDeviceConfig.DeadTime());
            }
            else
            {
                bool testing = resultDataStatus.Testing;
            }
            // Preset time elapsed
            if (resultDataStatus.Recording && resultDataStatus.ElapsedTime.TotalSeconds >= (double)resultDataStatus.PresetTime)
            {
                this.StopRecording();
                this.document.UpdateSpectrum = false;
                if (this.saveOnMeasurementEnd)
                {
                    this.documentManager.SaveDocument(this.document);
                }
                GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
                string measurementCompletion = globalConfig.SoundConfig.MeasurementCompletion;
                if (measurementCompletion != null && measurementCompletion != "")
                {
                    SoundPlayer soundPlayer = new SoundPlayer(measurementCompletion);
                    soundPlayer.Play();
                }
            }
        }

        // Token: 0x04000377 RID: 887
        DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();

        // Token: 0x04000378 RID: 888
        ROIConfigManager roiConfigManager = ROIConfigManager.GetInstance();

        // Token: 0x04000379 RID: 889
        DocumentManager documentManager = DocumentManager.GetInstance();

        // Token: 0x0400037A RID: 890
        ResultData resultData;

        // Token: 0x0400037B RID: 891
        DocEnergySpectrum document;

        // Token: 0x0400037C RID: 892
        DeviceController deviceController;

        // Token: 0x0400037D RID: 893
        bool saveOnMeasurementEnd;
    }
}
