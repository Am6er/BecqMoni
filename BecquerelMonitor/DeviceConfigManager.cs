using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x02000075 RID: 117
    public class DeviceConfigManager
    {
        // Token: 0x14000015 RID: 21
        // (add) Token: 0x060005F3 RID: 1523 RVA: 0x000256C0 File Offset: 0x000238C0
        // (remove) Token: 0x060005F4 RID: 1524 RVA: 0x000256FC File Offset: 0x000238FC
        public event DeviceConfigManager.DeviceConfigChangedEventHandler DeviceConfigListChanged;

        // Token: 0x170001D0 RID: 464
        // (get) Token: 0x060005F5 RID: 1525 RVA: 0x00025738 File Offset: 0x00023938
        // (set) Token: 0x060005F6 RID: 1526 RVA: 0x00025740 File Offset: 0x00023940
        public List<DeviceConfigInfo> DeviceConfigList
        {
            get
            {
                return this.deviceConfigList;
            }
            set
            {
                this.deviceConfigList = value;
            }
        }

        // Token: 0x170001D1 RID: 465
        // (get) Token: 0x060005F7 RID: 1527 RVA: 0x0002574C File Offset: 0x0002394C
        // (set) Token: 0x060005F8 RID: 1528 RVA: 0x00025754 File Offset: 0x00023954
        public Dictionary<string, DeviceConfigInfo> DeviceConfigMap
        {
            get
            {
                return this.deviceConfigMap;
            }
            set
            {
                this.deviceConfigMap = value;
            }
        }

        // Token: 0x060005F9 RID: 1529 RVA: 0x00025760 File Offset: 0x00023960
        public static DeviceConfigManager GetInstance()
        {
            DeviceConfigManager.instance.LoadAllConfigFiles();
            return DeviceConfigManager.instance;
        }

        // Token: 0x060005FB RID: 1531 RVA: 0x00025794 File Offset: 0x00023994
        /// <summary>
        /// ⛔ Этот менеджер ВЕШАЛ безоконный прогон, и стоял он МЕЖДУ двумя уже
        /// починенными (<c>S100</c>): <c>CorpusFsaProbe</c> зовёт подряд
        /// <c>GlobalConfigManager</c> → <c>DeviceConfigManager</c> →
        /// <c>NuclideDefinitionManager</c>, и прикрыт соседями он не был.
        /// Измерено 27.08.2026 на собранном коде, две сцены: каталога
        /// <c>config\device</c> нет (1 прогон) и «дубль GUID» — два файла с
        /// одним <c>Guid</c> (4 прогона). Во ВСЕХ пяти процесс убит по сроку
        /// 15–20 с, класс окна <c>#32770</c>, заголовок «Ошибка».
        ///
        /// ⛔ Поправка к <c>S100</c>: наблюдение «один прогон из четырёх в сцене
        /// „дубль GUID“ завершился с кодом 0» ЗДЕСЬ НЕ ВОСПРОИЗВЕЛОСЬ — висли
        /// все четыре. После правки те же четыре дают код 0 все четыре.
        ///
        /// Теперь все сообщения этого файла идут единственной дверью
        /// <see cref="AppUi"/>: в окнах — прежнее модальное окно, без окон —
        /// строка в поток ошибок. А отказ, после которого продолжать НЕЛЬЗЯ —
        /// каталога конфигураций нет вовсе, — без окон бросает: список устройств
        /// остался бы пустым, спектр не нашёл бы своей конфигурации, и числа
        /// были бы не «хуже», а чужими.
        ///
        /// ⚠ Пустой, но существующий каталог сюда НЕ попадает:
        /// <c>Directory.GetFiles</c> отдаёт пустой список без исключения. То
        /// есть отказ поднимается ровно там, где прежде висело окно.
        /// </summary>
        public void LoadAllConfigFiles()
        {
            if (this.listLoaded)
            {
                return;
            }
            this.deviceConfigList.Clear();
            this.deviceConfigMap.Clear();
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo_097b));
            XmlSerializer xmlSerializer2 = new XmlSerializer(typeof(DeviceConfigInfo));
            try
            {
                string[] files = Directory.GetFiles(userDirectoryConfigDevice, "*.xml");
                foreach (string path in files)
                {
                    // Per-file try: one broken XML used to abort loading of ALL remaining
                    // device configs (the try wrapped the whole loop).
                    try
                    {
                        DeviceConfigInfo deviceConfigInfo;
                        using (FileStream fileStream = new FileStream(path, FileMode.Open))
                        {
                            deviceConfigInfo = (DeviceConfigInfo)xmlSerializer2.Deserialize(fileStream);
                        }
                        if (!(deviceConfigInfo.FormatVersion == "120920"))
                        {
                            using (FileStream fileStream2 = new FileStream(path, FileMode.Open))
                            {
                                DeviceConfigInfo_097b old = (DeviceConfigInfo_097b)xmlSerializer.Deserialize(fileStream2);
                                deviceConfigInfo = new DeviceConfigInfo(old);
                            }
                        }
                        deviceConfigInfo.OriginalFilename = Path.GetFileName(path);
                        deviceConfigInfo.Filename = Path.GetFileName(path);
                        if (this.deviceConfigMap.ContainsKey(deviceConfigInfo.Guid))
                        {
                            AppUi.Report(string.Format(Resources.ERRDuplicateDeviceConfigGUID, deviceConfigInfo.Filename), Resources.ErrorDialogTitle, MessageBoxIcon.Exclamation);
                        }
                        else
                        {
                            this.deviceConfigList.Add(deviceConfigInfo);
                            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
                        }
                    }
                    catch (Exception)
                    {
                        AppUi.Report(Resources.ERRLoadingDeviceConfigFailed + "\n" + path, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: the device configuration directory could not be read and there is no UI to report it to: "
                        + AppUi.Where(userDirectoryConfigDeviceDir)
                        + ". Continuing would leave the device list EMPTY, and a spectrum that cannot find its device "
                        + "configuration is analysed with someone else's calibration and resolution. "
                        + "Run from a directory that has config\\device.",
                        ex);
                }
                // Каталог заводится ТОЛЬКО в окнах: пустая заготовка, оставленная
                // пробой в чужом каталоге, — ровно тот случай, из-за которого
                // завели `S100`.
                Directory.CreateDirectory(userDirectoryConfigDeviceDir);
                AppUi.Report(Resources.ERRLoadingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
            this.deviceConfigList.Sort();
            this.listLoaded = true;
            // `S102`: назвать ОТКРЫТЫЙ каталог и то, сколько из него взято.
            AppUi.Note("device configs: " + AppUi.Where(userDirectoryConfigDeviceDir) + ": "
                + this.deviceConfigList.Count + " loaded");
        }

        // Token: 0x060005FC RID: 1532 RVA: 0x00025990 File Offset: 0x00023B90
        public DeviceConfigInfo CreateConfig(string filename)
        {
            DeviceConfigInfo deviceConfigInfo = new DeviceConfigInfo();
            deviceConfigInfo.InitFormatVersion();
            deviceConfigInfo.Guid = Guid.NewGuid().ToString();
            deviceConfigInfo.OriginalFilename = filename;
            deviceConfigInfo.Filename = filename;
            deviceConfigInfo.Name = Path.GetFileNameWithoutExtension(filename);
            string path = userDirectoryConfigDevice + deviceConfigInfo.Filename;
            AudioInputDeviceConfig audioInputDeviceConfig = (AudioInputDeviceConfig)deviceConfigInfo.InputDeviceConfig;
            WaveInDeviceCaps audioInputDevice = null;
            if (WaveIn.Devices.Count > 0)
            {
                audioInputDevice = WaveIn.Devices[0];
            }
            audioInputDeviceConfig.AudioInputDevice = audioInputDevice;
            try
            {
                Utils.AtomicFileWriter.Write(path, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, deviceConfigInfo);
                });
            }
            catch (Exception)
            {
                AppUi.Report(Resources.ERRSavingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return null;
            }
            this.deviceConfigList.Add(deviceConfigInfo);
            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
            this.deviceConfigList.Sort();
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            return deviceConfigInfo;
        }

        // Token: 0x060005FD RID: 1533 RVA: 0x00025AE8 File Offset: 0x00023CE8
        public DeviceConfigInfo DuplicateConfig(DeviceConfigInfo config, string filename)
        {
            DeviceConfigInfo deviceConfigInfo = config.Clone();
            deviceConfigInfo.InitFormatVersion();
            deviceConfigInfo.Guid = Guid.NewGuid().ToString();
            deviceConfigInfo.OriginalFilename = filename;
            deviceConfigInfo.Filename = filename;
            deviceConfigInfo.Name = config.Name + Resources.CopyPostfix;
            try
            {
                string path = userDirectoryConfigDevice + deviceConfigInfo.Filename;
                Utils.AtomicFileWriter.Write(path, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, deviceConfigInfo);
                });
            }
            catch (Exception)
            {
                AppUi.Report(Resources.ERRSavingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return null;
            }
            this.deviceConfigList.Add(deviceConfigInfo);
            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
            this.deviceConfigList.Sort();
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            return deviceConfigInfo;
        }

        // Token: 0x060005FE RID: 1534 RVA: 0x00025C10 File Offset: 0x00023E10
        public bool SaveConfig(DeviceConfigInfo devConfig)
        {
            PolynomialEnergyCalibration pe = (PolynomialEnergyCalibration)devConfig.EnergyCalibration;
            if (!pe.CheckCalibration(channels: devConfig.NumberOfChannels))
            {
                AppUi.Report(Resources.CalibrationFunctionError, Resources.ErrorDialogTitle, MessageBoxIcon.Exclamation);
                // Was "return true" - the caller believed the config was saved, closed
                // the form and the user's edits silently disappeared.
                return false;
            }
            DeviceConfigInfo deviceConfigInfo = null;
            if (!string.IsNullOrEmpty(devConfig.Guid))
            {
                this.deviceConfigMap.TryGetValue(devConfig.Guid, out deviceConfigInfo);
            }
            if (deviceConfigInfo == null)
            {
                deviceConfigInfo = this.deviceConfigList.Find(config => config.Guid == devConfig.Guid);
            }
            // ⛔ `A8`: снятая отсюда запись — ЕДИНСТВЕННОЕ, чем конфигурация
            // представлена во всех выпадающих списках программы. Каждый выход
            // ниже обязан вернуть её на место через <see cref="RestoreConfig"/>.
            DeviceConfigInfo removed = deviceConfigInfo;
            if (removed != null)
            {
                this.deviceConfigMap.Remove(removed.Guid);
                this.deviceConfigList.Remove(removed);
            }
            if (devConfig.OriginalFilename != devConfig.Filename)
            {
                // Новое имя файла занято ДРУГОЙ конфигурацией. Записать по нему —
                // значит затереть её файл, а сама она останется в списке и до
                // перезапуска будет показывать чужие настройки. Измерено
                // 31.08.2026 на сборке до правки: переименование A → «DupB.xml»
                // вернуло true, и DupB.xml стал файлом A — конфигурация B погибла
                // молча.
                //
                // ⚠ И это ЕДИНСТВЕННАЯ причина отказа, которой соответствует
                // `Resources.ERRDuplicateConfigName` — сообщение, которое
                // вызывающие (`DeviceConfigForm`) показывают на ЛЮБОЙ false.
                // Прежде ей не соответствовала ни одна: проверки занятого имени
                // тут не было вовсе.
                //
                // ⚠ Проверка стоит ВНУТРИ переименования нарочно. Из окон иначе
                // столкнуться именами нельзя: имя файла выводится из имени
                // конфигурации, а новым его даёт `AssignNewFilename`, который
                // занятые перебирает. Снаружи же остаётся сохранение БЕЗ
                // переименования — там совпадение имён означает лишь мусор,
                // оставшийся в каталоге от прошлого прогона оснастки, и отказывать
                // из-за него значило бы ломать то, что сегодня работает.
                foreach (DeviceConfigInfo other in this.deviceConfigList)
                {
                    if (string.Equals(other.Filename, devConfig.Filename, StringComparison.OrdinalIgnoreCase))
                    {
                        this.RestoreConfig(removed);
                        return false;
                    }
                }
                try
                {
                    File.Delete(userDirectoryConfigDevice + devConfig.OriginalFilename);
                }
                catch (Exception ex)
                {
                    // Переименование не состоялось — и дальше НЕ идём. Записать
                    // новый файл, не сумев убрать старый, значит оставить на диске
                    // ДВА файла с одним `Guid`, а `LoadAllConfigFiles` берёт из
                    // такой пары произвольный: правки человека молча заменились бы
                    // старым файлом при следующем запуске. Отказ здесь ничего на
                    // диске не меняет, правки остаются в форме несохранёнными, и
                    // повторить сохранение можно, освободив файл.
                    Trace.WriteLine("device config rename failed: " + ex);
                    this.RestoreConfig(removed);
                    AppUi.Report(string.Format(Resources.ERRConfigFileRenameFailed, devConfig.OriginalFilename),
                        Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                    return false;
                }
            }
            devConfig.OriginalFilename = devConfig.Filename;
            devConfig.LastUpdated = DateTime.Now;
            deviceConfigInfo = devConfig.Clone();
            try
            {
                string path = userDirectoryConfigDevice + deviceConfigInfo.Filename;
                Utils.AtomicFileWriter.Write(path, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, deviceConfigInfo);
                });
            }
            catch (Exception ex)
            {
                // Сообщение здесь было и раньше, а вот запись со списков снималась
                // и не возвращалась: человеку говорили «не удалось сохранить», и
                // одновременно конфигурация исчезала отовсюду (`A8`).
                //
                // ⚠ Если переименование выше УСПЕЛО убрать старый файл, а запись
                // нового отказала, на диске конфигурации нет вовсе, и возвращённая
                // строка диску не соответствует. Так всё же лучше: правки живы в
                // форме и сохранение можно повторить, а снятая строка не оставляет
                // человеку ничего. Окно при этом сказано честно.
                Trace.WriteLine("device config save failed: " + ex);
                this.RestoreConfig(removed);
                AppUi.Report(Resources.ERRSavingDeviceConfigFailed, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            this.deviceConfigList.Add(deviceConfigInfo);
            this.deviceConfigMap.Add(deviceConfigInfo.Guid, deviceConfigInfo);
            this.deviceConfigList.Sort();
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            devConfig.Dirty = false;
            return true;
        }

        /// <summary>
        /// Вернуть в списки запись, снятую в начале <see cref="SaveConfig"/>.
        ///
        /// ⛔ Без этого возврата отказ сохранения УНОСИЛ конфигурацию из всех
        /// выпадающих списков программы до перезапуска, хотя файл на диске цел
        /// (`A8`). Измерено 31.08.2026 на сборке ДО правки, сцена «переименование
        /// при захваченном старом файле»: вернул False, «в списке НЕТ, в карте
        /// НЕТ, старый файл на диске да», сообщений — ни одного.
        /// </summary>
        void RestoreConfig(DeviceConfigInfo removed)
        {
            if (removed == null || this.deviceConfigMap.ContainsKey(removed.Guid))
            {
                return;
            }
            this.deviceConfigList.Add(removed);
            this.deviceConfigMap.Add(removed.Guid, removed);
            this.deviceConfigList.Sort();
        }

        /// <summary>
        /// Записать конфигурацию прибора на диск ТИХО — не перестраивая список и
        /// НЕ РАССЫЛАЯ <see cref="DeviceConfigListChanged"/> (`S70`).
        ///
        /// ⛔ Молчание здесь — суть, а не оптимизация. Обычный
        /// <see cref="SaveConfig"/> извещает всех, а слушатель
        /// (<c>MainForm.ApplyDeviceConfigToDocuments</c>) переносит сохранённые
        /// настройки поиска пиков во ВСЕ открытые спектры этого прибора через
        /// <see cref="FWHMPeakDetectionMethodConfig.AdoptFrom"/>, оставляя
        /// спектру только его ПШПВ и кнопку показа. Для галок панели поиска
        /// пиков это ровно то, чего делать НЕЛЬЗЯ: решение Amber 18.08.2026 —
        /// «умолчание прибора меняем, а уже сохранённую копию спектра не
        /// трогаем», и нажатие в одном документе не должно менять разложение в
        /// соседних.
        ///
        /// ⚠ Поверки калибровки здесь тоже нет, и это осознанно: обычный путь
        /// её делает и при отказе показывает модальное окно, а поднимать окно на
        /// щелчок по галке нельзя. Приведение <c>EnergyCalibration</c> к
        /// полиномиальной в том пути к тому же не защищено — на приборе с
        /// нелинейной калибровкой оно бросило бы прямо из обработчика.
        ///
        /// Писать полагается ТУ ЖЕ запись, что лежит в
        /// <see cref="DeviceConfigMap"/>: у неё имя файла совпадает с исходным,
        /// а незаписанного переименования не висит.
        /// </summary>
        public bool SaveConfigQuiet(DeviceConfigInfo devConfig)
        {
            if (devConfig == null || string.IsNullOrEmpty(devConfig.Filename))
            {
                return false;
            }

            // Незаписанное переименование: писать под новым именем — значит
            // оставить на диске два файла. Такое сохраняет только полный путь.
            if (!string.IsNullOrEmpty(devConfig.OriginalFilename)
                && devConfig.OriginalFilename != devConfig.Filename)
            {
                return false;
            }

            devConfig.LastUpdated = DateTime.Now;
            try
            {
                string path = userDirectoryConfigDevice + devConfig.Filename;
                Utils.AtomicFileWriter.Write(path, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                    xmlSerializer.Serialize(fileStream, devConfig);
                });
            }
            catch (Exception ex)
            {
                // Молча — но не бесследно: окно на щелчок по галке не поднимаем,
                // а отказ обязан быть виден тому, кто разбирается.
                Trace.WriteLine("device config quiet save failed: " + ex);
                return false;
            }

            devConfig.Dirty = false;
            return true;
        }

        // Token: 0x060005FF RID: 1535 RVA: 0x00025D88 File Offset: 0x00023F88
        /// <summary>
        /// ⛔ `A9`: строка снимается ТОЛЬКО после того, как файл действительно
        /// исчез. Прежде <c>File.Delete</c> стоял в ПУСТОМ <c>catch</c>, а снятие
        /// со списков шло ВСЕГДА: человек подтверждал удаление, строка пропадала —
        /// и после перезапуска конфигурация оказывалась на месте. Измерено
        /// 31.08.2026 на сборке до правки при захваченном файле: «в списке НЕТ,
        /// в карте НЕТ, файл на диске да», сообщений ноль.
        ///
        /// У отказа есть читатель, и это не признак, а сам список: вызывающий
        /// (<c>DeviceConfigForm.button4_Click</c>) сразу перечитывает список
        /// <c>ListupConfigFiles</c>, и неудалённая конфигурация возвращается на
        /// экран — рядом с окном о том, почему.
        ///
        /// ⚠ Отсутствующий файл отказом НЕ считается: <c>File.Delete</c> на нём
        /// не бросает, так что убранная руками конфигурация уходит из списка.
        /// </summary>
        /// <summary>
        /// Удалить конфигурацию прибора. Возвращает <c>false</c>, если файл
        /// удалить не удалось: тогда строка ОСТАЁТСЯ в списке, и вызвавший
        /// обязан не гасить форму (`A19`).
        /// </summary>
        public bool DeleteConfig(DeviceConfigInfo devConfig)
        {
            DeviceConfigInfo deviceConfigInfo = this.deviceConfigMap[devConfig.Guid];
            try
            {
                File.Delete(userDirectoryConfigDevice + deviceConfigInfo.OriginalFilename);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("device config delete failed: " + ex);
                AppUi.Report(string.Format(Resources.ERRConfigFileDeleteFailed, deviceConfigInfo.OriginalFilename),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            this.deviceConfigList.Remove(deviceConfigInfo);
            this.deviceConfigMap.Remove(deviceConfigInfo.Guid);
            if (this.DeviceConfigListChanged != null)
            {
                this.DeviceConfigListChanged(this, new DeviceConfigChangedEventArgs(deviceConfigInfo.Guid));
            }
            return true;
        }

        string userDirectory = BecquerelMonitor.Package.GetInstance().UserDirectory;

        string userDirectoryConfigDevice = BecquerelMonitor.Package.GetInstance().Device;

        string userDirectoryConfigDeviceDir = BecquerelMonitor.Package.GetInstance().DeviceDir;

        // Token: 0x04000328 RID: 808
        List<DeviceConfigInfo> deviceConfigList = new List<DeviceConfigInfo>();

        // Token: 0x04000329 RID: 809
        Dictionary<string, DeviceConfigInfo> deviceConfigMap = new Dictionary<string, DeviceConfigInfo>();

        // Token: 0x0400032A RID: 810
        bool listLoaded;

        // Token: 0x0400032C RID: 812
        static DeviceConfigManager instance = new DeviceConfigManager();

        // Token: 0x02000222 RID: 546
        // (Invoke) Token: 0x06001933 RID: 6451
        public delegate void DeviceConfigChangedEventHandler(object sender, DeviceConfigChangedEventArgs e);
    }
}
