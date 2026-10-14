using System;
using System.Globalization;
using System.Diagnostics;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    class ObsidianDeviceController : DeviceController, IDisposable, IDeviceStartsOnFirstPacket
    {
        private PulseDetector pulseDetector = new PulseDetector();
        private ResultDataStatus resultDataStatus = null;
        private bool new_document_created = false;
        private string deviceGuid;
        private ResultData resultData;
        private string status = "Unknown";
        private ObsidianIn subscribedInstance;
        // ⛔ `AMBER164`, 05.10.2026. Набор запущен ЭТИМ контроллером и не
        // остановлен ни человеком, ни прибором; только поток окон. Разбор — у
        // близнеца, `RadiaCodeDeviceController.measuring`.
        private bool measuring = false;

        // ⛔ П305 (14.10.2026), решение Amber «По первому пакету данных (Рекомендую)»:
        // начало набора ставит первый пакет данных этого набора, а не нажатие. Разбор —
        // у близнеца, `RadiaCodeDeviceController`; момент и правило П300 —
        // `MeasurementController.StartSetByFirstPacket`. Только поток окон.
        private bool startAwaitsPacket = false;
        private DateTime startAwaitedSince;
        private DateTime setStartedAt;
        private bool setHasData = false;
        private DateTime endBeforeSet;

        bool IDeviceStartsOnFirstPacket.StartAwaitsPacket
        {
            get { return startAwaitsPacket; }
        }

        // П305: начало поставит первый пакет, пришедший ПОСЛЕ этого мига (разбор у близнеца).
        private void AwaitFirstPacket()
        {
            startAwaitsPacket = true;
            startAwaitedSince = DateTime.Now;
        }

        // П305: пакет лёг в документ; первый после «Пуска» нового документа или «Очистить» ставит начало.
        private void PacketTaken(DateTime received, int deviceSeconds)
        {
            if (received > setStartedAt)
            {
                setHasData = true;
            }
            if (!startAwaitsPacket || !(received > startAwaitedSince))
            {
                return;
            }
            startAwaitsPacket = false;
            new_document_created = false;
            MeasurementController.StartSetByFirstPacket(resultData, received, deviceSeconds);
        }

        public ObsidianDeviceController()
        {
            new_document_created = true;
            DeviceConfigManager.GetInstance().DeviceConfigListChanged += ObsidianDeviceController_DeviceConfigListChanged;
        }

        private void ObsidianDeviceController_DeviceConfigListChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            if (resultData == null || subscribedInstance == null || resultData.DeviceConfig == null || e == null)
            {
                return;
            }

            if (!string.Equals(resultData.DeviceConfig.Guid, e.Guid, StringComparison.Ordinal))
            {
                return;
            }

            // ⛔ `AMBER164` (б): остановленному набору адрес не толкать — иначе
            //    `device_serial_changed` переводил остановленный прибор в
            //    `Starting`; следующий Start возьмёт адрес сам.
            if (!measuring)
            {
                return;
            }

            if (DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(e.Guid, out DeviceConfigInfo updatedDeviceConfig) &&
                updatedDeviceConfig.InputDeviceConfig is ObsidianDeviceConfig deviceConfig)
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
                // ⛔ П267 (14.10.2026): прибор пишет ДРУГОЙ спектр — его накопитель
                //    не наш; разбор у близнеца, `RadiaCodeDeviceController`.
                if (MeasurementController.IsDeviceLeasedByOther(deviceGuid, resultData.MeasurementController))
                {
                    Trace.WriteLine("Obsidian: device " + deviceGuid + " is recording another spectrum - clear is local, no Reset");
                    AwaitFirstPacket();   // П305: начало — по первому пакету следующего набора
                    return;
                }
                if (status == "Connected" || status == "Recording")
                {
                    ObsidianIn.getInstance(deviceGuid).sendCommand("Reset");
                    AwaitFirstPacket();   // П305: начало — по первому пакету после сброса
                }
                else
                {
                    // Сброс накопителя ПРИБОРА не состоялся. Без окон — отказ:
                    // не обнулённые отсчёты прибора приедут целиком со следующим
                    // же обновлением (`update_hystogram` копирует НАКОПЛЕННУЮ
                    // гистограмму) и будут посчитаны как свежие.
                    if (!AppUi.HasWindows)
                    {
                        throw new InvalidOperationException(
                            "BecqMoni: Obsidian is " + status + ", so its accumulated result was NOT cleared. "
                            + "A headless run must not continue: the old counts come back with the next update "
                            + "and look like a fresh measurement.");
                    }
                    AppUi.Report(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ERRDeviceNotClearedNotConnected, status), "", MessageBoxIcon.None);
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
                measuring = true;
                currentResultDataStatus.Recording = true;
                // П305: начало нового документа ставит первый пакет; набор без пакетов
                // документ не меняет (разбор у близнеца, `RadiaCodeDeviceController`).
                setStartedAt = DateTime.Now;
                setHasData = false;
                endBeforeSet = resultData.EndTime;
                if (new_document_created)
                {
                    AwaitFirstPacket();
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
                        // `AMBER164`: только набору, который идёт.
                        if (measuring)
                        {
                            resultData.ResultDataStatus.Recording = true;
                        }
                        break;
                    case "Faulted":
                    case "Stopped":
                    case "Disconnected":
                        // П305: набор без единого пакета — конец прежний (начало не тронуто).
                        if (measuring && !setHasData)
                        {
                            resultData.EndTime = endBeforeSet;
                        }
                        measuring = false;
                        // Hard stop on device-side termination. If a measurement was actually
                        // running (e.g. Troubleshoot stole the BLE link and disposed the recording
                        // instance -> Recording -> Stopped), drive the full stop pipeline so the
                        // Measurement Control panel receives MeasurementTerminated and flips out of
                        // the recording state (Start re-enabled). Previously only the Recording flag
                        // was cleared, so the stop event never reached the form and Start stayed
                        // wrongly active. NotifyMeasurementStoppedByDevice also releases the lease.
                        bool wasRecording = resultData.ResultDataStatus.Recording;
                        resultData.ResultDataStatus.Recording = false;
                        if (wasRecording && resultData.MeasurementController != null)
                        {
                            resultData.MeasurementController.NotifyMeasurementStoppedByDevice();
                        }
                        else
                        {
                            // Not recording: just release the lease (idempotent) so the GUID does
                            // not stay leased until the old spectrum is closed/deleted.
                            resultData.MeasurementController?.ReleaseLeaseIfHeld();
                        }
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
                // ⛔ ПОСРЕДИ НАБОРА, и потому НЕ бросок (остаток `S100`). Связь с
                //    прибором оборвалась, но набранное УЖЕ сохранено строкой выше
                //    (`StopRecording` доводит время и счёт до `ResultData`), и
                //    терять его нельзя. К тому же метод исполняется на потоке
                //    окон — через `MainForm.originalContext.Post` из обработчика
                //    `PortFailure`, — где обработчика исключений над ним нет.
                AppUi.Report(Properties.Resources.ERRDetectedErrorWhileCommunication,
                             Properties.Resources.ErrorDialogTitle, MessageBoxIcon.Exclamation);
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
                // Guard the lengths like AtomSpectraDeviceController does: a document with
                // a different channel count used to crash with ArgumentException here.
                // Use != (not just >): a shorter histogram passes a > check but CopyTo then
                // leaves a stale tail with a TotalPulseCount that no longer matches the array.
                if (e.Hystogram.Length != pulseDetector.EnergySpectrum.Spectrum.Length)
                {
                    Trace.WriteLine(string.Format(CultureInfo.InvariantCulture, "Obsidian: histogram size {0} > document channels {1}, dropping update", e.Hystogram.Length, pulseDetector.EnergySpectrum.Spectrum.Length));
                    return;
                }
                e.Hystogram.CopyTo(pulseDetector.EnergySpectrum.Spectrum, 0);
                pulseDetector.EnergySpectrum.TotalPulseCount = e.SUM;
                pulseDetector.EnergySpectrum.ValidPulseCount = e.SUM;
                pulseDetector.EnergySpectrum.ChannelPitch = 1;
                if (resultData != null)
                {
                    resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(e.ElapsedTime);
                    resultData.ResultDataStatus.ElapsedTime = resultData.ResultDataStatus.TotalTime;
                    // П267: время спектра — вместе с отсчётами ТОГО ЖЕ пакета, а не
                    // следующим тактом окна (разбор у `MeasurementController.SetSpectrumTimes`).
                    MeasurementController.SetSpectrumTimes(pulseDetector.EnergySpectrum,
                        resultData.ResultDataStatus.ElapsedTime.TotalSeconds, resultData.DeviceConfig, false);
                    // П305: первый пакет набора ставит его начало.
                    PacketTaken(e.Received, e.ElapsedTime);
                }
            }
        }

        public override void StopMeasurement(ResultData resultData)
        {
            ResultDataStatus currentResultDataStatus = resultData.ResultDataStatus;
            bool setRunning = measuring;
            measuring = false;
            if (deviceGuid != null)
            {
                ObsidianIn.getInstance(deviceGuid).sendCommand("Stop");
                // П305: «Стоп» до первого пакета набора — документ тот, что был, конец прежний.
                resultData.EndTime = setRunning && !setHasData ? endBeforeSet : DateTime.Now;
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
