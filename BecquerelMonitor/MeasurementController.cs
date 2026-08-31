using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Media;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000083 RID: 131
    public class MeasurementController
    {
        // Exclusive device ownership: one physical device (device config GUID) can be
        // recorded by at most one MeasurementController at a time. Without this two
        // ResultData could subscribe to the same singleton input; Stop from one sent a
        // physical stop while the other stayed with Recording == true.
        static readonly Dictionary<string, MeasurementController> deviceLeases = new Dictionary<string, MeasurementController>();
        static readonly object deviceLeaseLock = new object();

        bool AcquireDeviceLease()
        {
            string guid = this.resultData != null && this.resultData.DeviceConfig != null
                ? this.resultData.DeviceConfig.Guid
                : null;
            if (guid == null)
            {
                return true;
            }
            lock (deviceLeaseLock)
            {
                MeasurementController owner;
                if (deviceLeases.TryGetValue(guid, out owner) && owner != this)
                {
                    return false;
                }
                deviceLeases[guid] = this;
                return true;
            }
        }

        void ReleaseDeviceLease()
        {
            string guid = this.resultData != null && this.resultData.DeviceConfig != null
                ? this.resultData.DeviceConfig.Guid
                : null;
            if (guid == null)
            {
                return;
            }
            lock (deviceLeaseLock)
            {
                MeasurementController owner;
                if (deviceLeases.TryGetValue(guid, out owner) && owner == this)
                {
                    deviceLeases.Remove(guid);
                }
            }
        }

        /// <summary>
        /// Занят ли прибор с этим `guid` идущим измерением.
        ///
        /// ⛔ `A17`. Признак не выдуман: аренда <c>deviceLeases</c> заводится в
        /// <see cref="StartRecording"/> / <see cref="AttachToDevice"/> и
        /// снимается на остановке, закрытии документа и удалении спектра. Здесь
        /// у неё появляется ВТОРОЙ читатель — кнопка «Troubleshoot» настройки
        /// прибора, которая прежде убивала занятый прибор, не спрашивая
        /// (<c>RadiaCodeIn.TryClaimForTroubleshoot</c> и близнец у Obsidian).
        ///
        /// ⚠ Чтение, и только чтение: аренду этот метод не берёт и не отдаёт.
        /// </summary>
        public static bool IsDeviceBusy(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }
            lock (deviceLeaseLock)
            {
                return deviceLeases.ContainsKey(guid);
            }
        }

        // Idempotent lease release for teardown paths (document close / spectrum delete).
        // The lease used to be released only from StopRecording()/DetachFromDevice(), both
        // of which the close/delete callers skip once ResultDataStatus.Recording has been
        // flipped to false by a device Stopped/Faulted/Disconnected status. That orphaned
        // the GUID in deviceLeases until process restart.
        public void ReleaseLeaseIfHeld()
        {
            this.ReleaseDeviceLease();
        }

        // Device-initiated termination: the device reported a terminal status
        // (Stopped/Faulted/Disconnected) on its own — e.g. its BLE connection was taken over by
        // the Troubleshoot session while a measurement was running. The device side is already
        // torn down, so we must NOT re-issue a Stop command (StopMeasurement would call
        // ObsidianIn.getInstance and resurrect a disposed instance). We only release the lease,
        // clear the recording flag, and raise MeasurementTerminated so the Measurement Control
        // panel leaves the recording state (Start re-enabled, Stop disabled). Idempotent.
        public void NotifyMeasurementStoppedByDevice()
        {
            this.ReleaseDeviceLease();
            if (this.resultData != null && this.resultData.ResultDataStatus != null)
            {
                this.resultData.ResultDataStatus.Recording = false;
            }
            if (this.MeasurementTerminated != null)
            {
                this.MeasurementTerminated(this, new EventArgs());
            }
        }

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

        /// <summary>
        /// ⛔ ЧТО ЗНАЧИТ «БЕЗ ОКОН» НА ПУТИ ИЗМЕРЕНИЯ (остаток `S100`, 28.08.2026).
        ///
        /// Здесь беды двух разных пород, и одной меркой их мерить нельзя:
        ///
        /// 1. Прибор НЕ ОТКРЫЛСЯ на СТАРТЕ (занят, конфигурации нет, тип чужой,
        ///    порт молчит). Данных ещё нет; продолжать нельзя. Без окон —
        ///    ОТКАЗ БРОСКОМ, а не <c>false</c>.
        ///
        ///    ⚠ Довод не общий, а измеренный: у <c>false</c> ЧИТАТЕЛЯ НЕТ.
        ///    Единственный оконный вызывающий, <c>DCControlPanel.StartMeasurement</c>
        ///    (<c>DCControlPanel.cs:144</c>), возвращаемое значение
        ///    <c>StartRecording()</c> ПРОСТО ИГНОРИРУЕТ — и дальше метит документ
        ///    <c>Dirty</c>, зовёт <c>UpdateSampleInfo</c> и <c>ShowDocumentStatus</c>,
        ///    как будто набор пошёл. Безоконный прогон на «просто false» ведёт себя
        ///    так же: пишет ПУСТОЙ спектр и выдаёт его за измерение. Читатель у
        ///    отказа обязан быть, и здесь это код возврата прогона.
        ///
        /// 2. Прибор отвалился ПОСРЕДИ набора. Данные уже есть, и бросок их теряет:
        ///    такие места живут в контроллерах приборов (обработчики
        ///    <c>port_failure_stop</c>, <c>update_hystogram</c>, поток буферов
        ///    <c>WaveIn.MaintainBuffers</c>) и переведены на <see cref="AppUi.Report"/>,
        ///    а не на бросок. См. пояснения там же.
        ///
        /// В окнах НИЧЕГО не меняется: <see cref="AppUi.Report"/> поднимает то же
        /// модальное окно, с тем же текстом, тем же заголовком и той же значковой
        /// частью, что и прежний <c>MessageBox.Show</c>.
        /// </summary>
        // Token: 0x060006AB RID: 1707 RVA: 0x00028040 File Offset: 0x00026240
        public bool StartRecording()
        {
            ResultDataStatus resultDataStatus = this.resultData.ResultDataStatus;
            if (!this.AcquireDeviceLease())
            {
                string busy = string.Format(Resources.ERRDeviceBusy, this.resultData.DeviceConfig.Name);
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: the measurement cannot start - the device is already being recorded by another "
                        + "spectrum: " + busy + " A headless run must not continue here: nothing would be acquired, "
                        + "and the empty spectrum would be presented as a measurement.");
                }
                AppUi.Report(busy, "", MessageBoxIcon.None);
                return false;
            }
            if (!this.CreateDeviceController())
            {
                this.ReleaseDeviceLease();
                return false;
            }
            try
            {
                bool result = this.deviceController.StartMeasurement(this.resultData);
                if (!result)
                {
                    this.ReleaseDeviceLease();
                    return false;
                }
            } catch (Exception ex)
            {
                // ⛔ Без окон этот `catch` НЕ ГЛОТАЕТ. Он ловит всё подряд и
                //    подменяет причину одним и тем же «Bluetooth не поддержан
                //    операционной системой» — то есть отказ звукового входа или
                //    последовательного порта доехал бы до человека чужим текстом,
                //    а до безоконного прогона не доехал бы вовсе.
                if (!AppUi.HasWindows)
                {
                    this.ReleaseDeviceLease();
                    throw new InvalidOperationException(
                        "BecqMoni: the device failed at the start of the measurement (" + ex.GetType().Name + "): "
                        + ex.Message + " A headless run must not continue: the spectrum would stay empty.", ex);
                }
                AppUi.Report(Resources.ERRBTNotSupportedByOS, "", MessageBoxIcon.None);
                this.ReleaseDeviceLease();
                return false;
            }

            return true;
        }

        public bool AttachToDevice()
        {
            ResultDataStatus resultDataStatus = this.resultData.ResultDataStatus;
            if (!this.AcquireDeviceLease())
            {
                string busy = string.Format(Resources.ERRDeviceBusy, this.resultData.DeviceConfig.Name);
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: cannot attach to the device - it is already being recorded by another spectrum: "
                        + busy + " A headless run must not continue: nothing would be acquired.");
                }
                AppUi.Report(busy, "", MessageBoxIcon.None);
                return false;
            }
            if (!this.CreateDeviceController())
            {
                this.ReleaseDeviceLease();
                return false;
            }
            bool attached = this.deviceController.AttachToDevice(this.resultData);
            if (!attached)
            {
                this.ReleaseDeviceLease();
            }
            return attached;
        }

        // Token: 0x060006AC RID: 1708 RVA: 0x00028070 File Offset: 0x00026270
        bool CreateDeviceController()
        {
            DeviceConfigInfo deviceConfig = this.resultData.DeviceConfig;
            if (deviceConfig == null || deviceConfig.Guid == null || !this.deviceConfigManager.DeviceConfigMap.ContainsKey(deviceConfig.Guid))
            {
                // Старт измерения: см. пояснение у `StartRecording`.
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: the spectrum names a device configuration that is not among the loaded ones ("
                        + (deviceConfig == null ? "<none>" : "GUID " + (deviceConfig.Guid ?? "<none>"))
                        + "); loaded: " + this.deviceConfigManager.DeviceConfigMap.Count
                        + ". The measurement cannot start, and a headless run must not continue with an empty spectrum.");
                }
                AppUi.Report(Resources.ERRDeviceConfigNotSelected, "", MessageBoxIcon.None);
                return false;
            }
            DeviceType deviceType = null;
            DeviceType.DeviceTypeMap.TryGetValue(this.resultData.DeviceConfig.DeviceType, out deviceType);
            if (deviceType == null)
            {
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: the device configuration names a device type that is not registered: \""
                        + (this.resultData.DeviceConfig.DeviceType ?? "<none>") + "\". The measurement cannot start, "
                        + "and a headless run must not continue with an empty spectrum.");
                }
                AppUi.Report(Resources.ERRInvalidDeviceType, "", MessageBoxIcon.None);
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
            else if (deviceType.DeviceControllerType == typeof(ObsidianDeviceController))
            {
                if (this.deviceController == null)
                {
                    this.deviceController = (DeviceController)Activator.CreateInstance(deviceType.DeviceControllerType);
                }
            }
            if (this.deviceController == null)
            {
                // Сюда попадает зарегистрированный тип прибора, у которого
                // `DeviceControllerType` не совпал ни с одним из четырёх выше.
                // Сегодня таких в реестре нет — но это и есть та ветка, которая
                // молча промолчит при добавлении пятого прибора.
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: device type \"" + (this.resultData.DeviceConfig.DeviceType ?? "<none>")
                        + "\" is registered, but no controller was built for it (controller type "
                        + (deviceType.DeviceControllerType == null ? "<none>" : deviceType.DeviceControllerType.Name)
                        + "). The measurement cannot start.");
                }
                AppUi.Report(Resources.ERRInvalidDeviceType, "", MessageBoxIcon.None);
                return false;
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
            this.ReleaseDeviceLease();
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
            this.ReleaseDeviceLease();
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
            } catch (Exception ex)
            {
                // Сброс накопителя ПРИБОРА. Метод `void`, читателя у неудачи нет
                // вовсе, а цена молчания та же, что у пустого спектра: не
                // обнулённые отсчёты прибора приедут в следующем же обновлении
                // (`update_hystogram` копирует НАКОПЛЕННУЮ гистограмму целиком) и
                // будут посчитаны как свежие. Без окон — отказ.
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: the device refused to clear its accumulated result (" + ex.GetType().Name + "): "
                        + ex.Message + " A headless run must not continue: counts that were not zeroed come back "
                        + "with the next update and look like a fresh measurement.", ex);
                }
                AppUi.Report(Resources.ERRBTNotSupportedByOS, "", MessageBoxIcon.None);
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
