using System;
using System.Globalization;
using System.Diagnostics;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    class RadiaCodeDeviceController : DeviceController, IDisposable, IDeviceStartsOnFirstPacket
    {
        private PulseDetector pulseDetector = new PulseDetector();
        private ResultDataStatus resultDataStatus = null;
        private bool new_document_created = false;
        private string deviceGuid;
        private ResultData resultData;
        private string status = "Unknown";
        private RadiaCodeIn subscribedInstance;
        // ⛔ `AMBER164`, 05.10.2026. Набор запущен ЭТИМ контроллером и не
        // остановлен — ни человеком (StopMeasurement), ни прибором (статус
        // Faulted/Stopped/Disconnected). Только поток окон. Контроллер остаётся
        // подписан на экземпляр и после Stop, и без этого признака статус
        // «Recording», пришедший ПОСЛЕ остановки (событие, отправленное до
        // Stop и доехавшее через `Post` позже, или прибор, который поток чтения
        // сам вернул в опрос), включал документу `Recording = true` без Start.
        private bool measuring = false;

        // ⛔ П305 (14.10.2026), решение Amber вопросником, дословно: «По первому пакету
        // данных (Рекомендую)». НАЧАЛО НАБОРА СТАВИТ ПЕРВЫЙ ПАКЕТ ДАННЫХ ЭТОГО НАБОРА, а
        // не нажатие: «Пуск» у BLE принимается сразу, ответ прибора приходит потом
        // статусом, и отказ до первого пакета («Faulted», «Стоп» по бесконечному
        // переподключению) оставлял загруженному файлу его отсчёты при начале «Пуска» —
        // Cs-137 при известной пробе ×1.0638 вместо ×1.00001 (находка П303). Момент и
        // правило П300 — `MeasurementController.StartSetByFirstPacket`.
        // ⚠ Поля читаются и пишутся только потоком окон.
        private bool startAwaitsPacket = false;   // «Пуск» нового документа или «Очистить» ждёт пакет
        private DateTime startAwaitedSince;       // пакет, пришедший не позже, — прежний (`AwaitFirstPacket`)
        private DateTime setStartedAt;            // «Пуск» этого набора
        private bool setHasData = false;          // пакет этого набора лёг в документ
        private DateTime endBeforeSet;            // конец документа до «Пуска»

        bool IDeviceStartsOnFirstPacket.StartAwaitsPacket
        {
            get { return startAwaitsPacket; }
        }

        /// <summary>
        /// П305: начало поставит первый пакет, пришедший ПОСЛЕ этого мига. Зовётся
        /// после команды прибору: пакет, прочитанный потоком чтения до «Reset», несёт
        /// метку прихода раньше (`RadiaCodeIn.run` ставит её до чтения состояния) и
        /// начала не ставит — его отсчёты сняты до сброса. Пакет, уже стоящий в
        /// очереди окна, — тоже.
        /// </summary>
        private void AwaitFirstPacket()
        {
            startAwaitsPacket = true;
            startAwaitedSince = DateTime.Now;
        }

        /// <summary>
        /// П305: пакет лёг в документ (<c>update_hystogram</c>). Первый после
        /// «Пуска» нового документа или «Очистить» ставит начало.
        /// </summary>
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

            // ⛔ `AMBER164` (б). Остановленному набору новый адрес не нужен:
            //    следующий Start сам возьмёт его из `DeviceConfigMap`
            //    (`StartMeasurement`). А `setDeviceSerial` поднимает в экземпляре
            //    `device_serial_changed`, и поток чтения переводил ОСТАНОВЛЕННЫЙ
            //    прибор в `Starting` — сохранение настройки прибора запускало набор.
            if (!measuring)
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
                // ⛔ П267 (14.10.2026): прибор пишет ДРУГОЙ спектр — его накопитель
                //    не наш. Контроллер остановленного спектра остаётся подписан на
                //    общий экземпляр и слышит статус «Recording» ЧУЖОГО набора, и
                //    «Очистить» здесь слал прибору «Reset» — сосед терял всё
                //    набранное. Очищается только документ (`DCControlPanel`).
                if (MeasurementController.IsDeviceLeasedByOther(deviceGuid, resultData.MeasurementController))
                {
                    Trace.WriteLine("RadiaCode: device " + deviceGuid + " is recording another spectrum - clear is local, no Reset");
                    // П305: начало — по первому пакету следующего набора этого спектра;
                    // прибор не сброшен, и пакет принесёт его накопитель со своим временем.
                    AwaitFirstPacket();
                    return;
                }
                if (status == "Connected" || status == "Recording")
                {
                    RadiaCodeIn.getInstance(deviceGuid).sendCommand("Reset");
                    // П305: начало — по первому пакету после сброса (миг сброса), а не
                    // нажатие; сброс, потерянный при обрыве, начало тоже не сдвинет.
                    AwaitFirstPacket();
                } else
                {
                    // Тот же разбор, что у Obsidian: сброс накопителя ПРИБОРА не
                    // состоялся, а не обнулённые отсчёты вернутся со следующим
                    // обновлением и сойдут за свежие. Без окон — отказ.
                    if (!AppUi.HasWindows)
                    {
                        throw new InvalidOperationException(
                            "BecqMoni: RadiaCode is " + this.status + ", so its accumulated result was NOT cleared. "
                            + "A headless run must not continue: the old counts come back with the next update "
                            + "and look like a fresh measurement.");
                    }
                    AppUi.Report(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ERRDeviceNotClearedNotConnected, this.status), "", MessageBoxIcon.None);
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
                measuring = true;
                currentResultDataStatus.Recording = true;
                // П305: начало нового документа ставит первый пакет (`PacketTaken`);
                // набор, не принёсший ни одного, документ не меняет — и конец, который
                // такт окна (`MainForm.OnTimer`) двигает «сейчас» в ожидании данных,
                // возвращается прежним (`setStatus`, `StopMeasurement`).
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
                        // `AMBER164`: только набору, который идёт (разбор у `measuring`).
                        if (measuring)
                        {
                            resultData.ResultDataStatus.Recording = true;
                        }
                        break;
                    case "Faulted":
                    case "Stopped":
                    case "Disconnected":
                        // П305: набор кончился, не дав ни одного пакета, — документ тот,
                        // что был: начало не тронуто (его ставит пакет), конец — прежний.
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
                // ⛔ ПОСРЕДИ НАБОРА — не бросок; разбор тот же, что у Obsidian:
                //    набранное уже сведено строкой выше, а метод исполняется на
                //    потоке окон (`MainForm.originalContext.Post`), где исключение
                //    некому поймать.
                AppUi.Report(Properties.Resources.ERRDetectedErrorWhileCommunication,
                             Properties.Resources.ErrorDialogTitle, MessageBoxIcon.Exclamation);
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
                // Guard the lengths like AtomSpectraDeviceController does: a document with
                // a different channel count used to crash with ArgumentException here.
                // Use != (not just >): a shorter histogram passes a > check but CopyTo then
                // leaves a stale tail with a TotalPulseCount that no longer matches the array.
                if (e.Hystogram.Length != this.pulseDetector.EnergySpectrum.Spectrum.Length)
                {
                    Trace.WriteLine(string.Format(CultureInfo.InvariantCulture, "RadiaCode: histogram size {0} > document channels {1}, dropping update", e.Hystogram.Length, this.pulseDetector.EnergySpectrum.Spectrum.Length));
                    return;
                }
                e.Hystogram.CopyTo(this.pulseDetector.EnergySpectrum.Spectrum, 0);
                this.pulseDetector.EnergySpectrum.TotalPulseCount = e.SUM;
                this.pulseDetector.EnergySpectrum.ValidPulseCount = e.SUM;
                this.pulseDetector.EnergySpectrum.ChannelPitch = 1;
                if (this.resultData != null)
                {
                    resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(e.ElapsedTime);
                    resultData.ResultDataStatus.ElapsedTime = resultData.ResultDataStatus.TotalTime;
                    // П267: время спектра — вместе с отсчётами ТОГО ЖЕ пакета, а не
                    // следующим тактом окна (разбор у `MeasurementController.SetSpectrumTimes`).
                    MeasurementController.SetSpectrumTimes(this.pulseDetector.EnergySpectrum,
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
                RadiaCodeIn.getInstance(deviceGuid).sendCommand("Stop");
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
