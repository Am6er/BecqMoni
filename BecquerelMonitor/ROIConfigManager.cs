using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000140 RID: 320
    public class ROIConfigManager
    {
        // Token: 0x1400005B RID: 91
        // (add) Token: 0x06001029 RID: 4137 RVA: 0x00058148 File Offset: 0x00056348
        // (remove) Token: 0x0600102A RID: 4138 RVA: 0x00058184 File Offset: 0x00056384
        public event EventHandler ROIConfigListChanged;

        // Token: 0x1700044E RID: 1102
        // (get) Token: 0x0600102B RID: 4139 RVA: 0x000581C0 File Offset: 0x000563C0
        // (set) Token: 0x0600102C RID: 4140 RVA: 0x000581C8 File Offset: 0x000563C8
        public List<ROIConfigData> ROIConfigList
        {
            get
            {
                return this.roiConfigList;
            }
            set
            {
                this.roiConfigList = value;
            }
        }

        // Token: 0x1700044F RID: 1103
        // (get) Token: 0x0600102D RID: 4141 RVA: 0x000581D4 File Offset: 0x000563D4
        // (set) Token: 0x0600102E RID: 4142 RVA: 0x000581DC File Offset: 0x000563DC
        public Dictionary<string, ROIConfigData> ROIConfigMap
        {
            get
            {
                return this.roiConfigMap;
            }
            set
            {
                this.roiConfigMap = value;
            }
        }

        // Token: 0x0600102F RID: 4143 RVA: 0x000581E8 File Offset: 0x000563E8
        public static ROIConfigManager GetInstance()
        {
            ROIConfigManager.instance.LoadAllConfigFiles();
            return ROIConfigManager.instance;
        }

        // Token: 0x06001031 RID: 4145 RVA: 0x0005821C File Offset: 0x0005641C
        /// <summary>
        /// Сообщения этого файла переведены на единственную дверь
        /// <see cref="AppUi"/> (<c>S100</c>): в окнах — прежнее модальное окно,
        /// без окон — строка в поток ошибок вместо зависания.
        ///
        /// ⛔ ЗАВОДИТЬ каталог без окон тоже НЕЛЬЗЯ (`A90`): заготовка, оставленная
        /// пробой в чужом каталоге, отменяет собственный опыт — см. верхний
        /// <c>catch</c>. Приём для «в окнах» взят у соседа целиком.
        ///
        /// ⛔ А вот ОТКАЗЫВАТЬ без окон, как это делает <c>DeviceConfigManager</c>
        /// при отсутствии своего каталога, здесь НЕЛЬЗЯ, и это не забывчивость:
        /// в рабочем каталоге корпусных прогонов (<c>tools\CORPUS\scripts\wd_app\config</c>)
        /// каталога <c>ROI</c> НЕТ ВОВСЕ — там лежат только
        /// <c>BecquerelMonitor.xml</c>, <c>NuclideDefinition.xml</c> и
        /// <c>device</c> (смотрено 27.08.2026). Отказ убил бы прогоны, которые
        /// сегодня работают.
        ///
        /// ⚠ Что при этом список ROI пуст, а кривая эффективности берётся
        /// именно из конфигурации ROI, — отдельная беда, и она НЕ закрыта здесь.
        ///
        /// ⛔ ПРИЧИНА ОТКАЗА ТЕПЕРЬ ЕДЕТ ВМЕСТЕ С СООБЩЕНИЕМ (`A22`). Прежде
        /// <c>catch</c> ловил исключение БЕЗ ПЕРЕМЕННОЙ, и человек получал
        /// «не удалось загрузить конфигурационный файл ROI» плюс путь — то есть
        /// ровно то, что он и так видит; отличить «файл занят другой программой»
        /// от «в файле неизвестный примитив» по такому сообщению нельзя.
        /// Причина собирается <see cref="AppUi.Reason"/> и уходит той же дверью
        /// <see cref="AppUi"/>: в окнах — в текст модального окна, без окон —
        /// в ту же строку потока ошибок. Помощник лежит у двери, а не здесь,
        /// затем, что тот же вопрос стоит и у редактора нуклидов (`A25`): двух
        /// соглашений о том, как называется причина, быть не должно.
        /// </summary>
        public void LoadAllConfigFiles()
        {
            if (this.isLoaded)
            {
                return;
            }
            EnsurePrimitiveMaps();
            this.roiConfigList.Clear();
            this.roiConfigMap.Clear();
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
            string loadingPath = null;
            xmlSerializer.UnknownElement += (s, e) => TraceDroppedElement(loadingPath, e);
            try
            {
                string[] files = Directory.GetFiles(configROI, "*.xml");
                foreach (string path in files)
                {
                    // Per-file try: one broken XML used to abort loading of ALL remaining
                    // ROI configs (the try wrapped the whole loop).
                    try
                    {
                        ROIConfigData roiconfigData;
                        loadingPath = path;
                        using (FileStream fileStream = new FileStream(path, FileMode.Open))
                        {
                            roiconfigData = (ROIConfigData)xmlSerializer.Deserialize(fileStream);
                        }
                        if (!(roiconfigData.FormatVersion == "120920"))
                        {
                            roiconfigData.InitFormatVersion();
                        }
                        foreach (ROIDefinitionData roidefinitionData in roiconfigData.ROIDefinitions)
                        {
                            foreach (ROIPrimitiveData roiprimitiveData in roidefinitionData.ROIPrimitives)
                            {
                                roiprimitiveData.Primitive = ROIPrimitiveDefinition.DefinitionsMap[roiprimitiveData.PrimitiveType];
                                roiprimitiveData.Operation = ROIPrimitiveOperation.OperationsMap[roiprimitiveData.OperationType];
                            }
                        }
                        roiconfigData.OriginalFilename = Path.GetFileName(path);
                        roiconfigData.Filename = Path.GetFileName(path);
                        if (this.roiConfigMap.ContainsKey(roiconfigData.Guid))
                        {
                            AppUi.Report(string.Format(Resources.ERRDuplicateROIConfigGUID, roiconfigData.Filename), Resources.ErrorDialogTitle, MessageBoxIcon.Exclamation);
                        }
                        else
                        {
                            this.roiConfigList.Add(roiconfigData);
                            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine("ROI config load failed: " + path + ": " + ex);
                        AppUi.Report(Resources.ERRLoadingROIConfigFailed + "\n" + AppUi.Where(path)
                            + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                            Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("ROI config directory unreadable: " + configROIDir + ": " + ex);
                if (AppUi.HasWindows)
                {
                    // Каталог заводится ТОЛЬКО в окнах — тем же приёмом
                    // (<see cref="AppUi.HasWindows"/>), каким это делает сосед
                    // <c>DeviceConfigManager</c> (`A90`). Второго соглашения о
                    // том, что такое «в окнах», в дереве быть не должно.
                    //
                    // ⛔ Пустая заготовка, оставленная ПРОБОЙ в чужом каталоге, —
                    // ровно та грабля, из-за которой завели `S100`, и стоит она
                    // не опрятности: опыт перестаёт воспроизводиться. Измерено
                    // 03.09.2026 на сцене `F_a22_nodir` и повторено 04.09.2026:
                    // ПЕРВЫЙ прогон `RoiLoadProbe` в каталоге без `config\ROI`
                    // возвращал 2 («каталога нет»), ВТОРОЙ — 0, потому что
                    // каталог остался от первого. Ни одна из двух цифр при этом
                    // не выглядит отказом.
                    Directory.CreateDirectory(configROIDir);
                }

                AppUi.Report(Resources.ERRLoadingROIConfigFailed + "\n" + AppUi.Where(configROIDir)
                    + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
            this.roiConfigList.Sort();
            this.isLoaded = true;
        }

        // Token: 0x06001032 RID: 4146 RVA: 0x0005848C File Offset: 0x0005668C
        public ROIConfigData CreateConfig(string filename)
        {
            ROIConfigData roiconfigData = new ROIConfigData();
            roiconfigData.InitFormatVersion();
            roiconfigData.Guid = Guid.NewGuid().ToString();
            roiconfigData.OriginalFilename = filename;
            roiconfigData.Filename = filename;
            roiconfigData.Name = Path.GetFileNameWithoutExtension(filename);
            string path = configROI + roiconfigData.Filename;
            try
            {
                Utils.AtomicFileWriter.Write(path, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                    xmlSerializer.Serialize(fileStream, roiconfigData);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("ROI config create failed: " + path + ": " + ex);
                AppUi.Report(Resources.ERRSavingROIConfigFailed + "\n" + AppUi.Where(path)
                    + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return null;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            return roiconfigData;
        }

        // Token: 0x06001033 RID: 4147 RVA: 0x00058598 File Offset: 0x00056798
        public ROIConfigData DuplicateConfig(ROIConfigData config, string filename)
        {
            ROIConfigData roiconfigData = config.Clone();
            roiconfigData.InitFormatVersion();
            roiconfigData.Guid = Guid.NewGuid().ToString();
            roiconfigData.OriginalFilename = filename;
            roiconfigData.Filename = filename;
            roiconfigData.Name = config.Name + Resources.CopyPostfix;
            string duplicatePath = configROI + roiconfigData.Filename;
            try
            {
                Utils.AtomicFileWriter.Write(duplicatePath, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                    xmlSerializer.Serialize(fileStream, roiconfigData);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("ROI config duplicate failed: " + duplicatePath + ": " + ex);
                AppUi.Report(Resources.ERRSavingROIConfigFailed + "\n" + AppUi.Where(duplicatePath)
                    + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return null;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            this.roiConfigList.Sort();
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            return roiconfigData;
        }

        // Token: 0x06001034 RID: 4148 RVA: 0x000586BC File Offset: 0x000568BC
        public bool LoadConfig(ROIConfigData roiConfig)
        {
            ROIConfigData roiconfigData = this.roiConfigMap[roiConfig.Guid];
            this.roiConfigMap.Remove(roiconfigData.Guid);
            this.roiConfigList.Remove(roiconfigData);
            string path = configROI + roiconfigData.OriginalFilename;
            EnsurePrimitiveMaps();
            try
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                xmlSerializer.UnknownElement += (s, e) => TraceDroppedElement(path, e);
                using (FileStream fileStream = new FileStream(path, FileMode.Open))
                {
                    roiconfigData = (ROIConfigData)xmlSerializer.Deserialize(fileStream);
                }
                if (!(roiconfigData.FormatVersion == "120920"))
                {
                    roiconfigData.InitFormatVersion();
                }
                foreach (ROIDefinitionData roidefinitionData in roiconfigData.ROIDefinitions)
                {
                    foreach (ROIPrimitiveData roiprimitiveData in roidefinitionData.ROIPrimitives)
                    {
                        roiprimitiveData.Primitive = ROIPrimitiveDefinition.DefinitionsMap[roiprimitiveData.PrimitiveType];
                        roiprimitiveData.Operation = ROIPrimitiveOperation.OperationsMap[roiprimitiveData.OperationType];
                    }
                }
                roiconfigData.OriginalFilename = Path.GetFileName(path);
                roiconfigData.Filename = Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("ROI config reload failed: " + path + ": " + ex);
                AppUi.Report(Resources.ERRLoadingROIConfigFailed + "\n" + AppUi.Where(path)
                    + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            this.roiConfigList.Sort();
            return true;
        }

        // Token: 0x06001035 RID: 4149 RVA: 0x000588C8 File Offset: 0x00056AC8
        public bool SaveConfig(ROIConfigData roiConfig)
        {
            // ⛔ `A8`, слово в слово как у `DeviceConfigManager.SaveConfig`:
            // снятая отсюда запись — единственное, чем конфигурация ROI
            // представлена в списках программы, и каждый выход ниже обязан
            // вернуть её на место.
            ROIConfigData removed = this.roiConfigMap[roiConfig.Guid];
            this.roiConfigMap.Remove(removed.Guid);
            this.roiConfigList.Remove(removed);
            if (roiConfig.OriginalFilename != roiConfig.Filename)
            {
                // Новое имя файла занято ДРУГОЙ конфигурацией — единственная
                // причина, о которой правду говорит `ERRDuplicateConfigName`
                // вызывающего. Внутри переименования по той же причине, что и у
                // `DeviceConfigManager`: без переименования столкнуться именами
                // из окон нельзя.
                foreach (ROIConfigData other in this.roiConfigList)
                {
                    if (string.Equals(other.Filename, roiConfig.Filename, StringComparison.OrdinalIgnoreCase))
                    {
                        this.RestoreConfig(removed);
                        return false;
                    }
                }
                try
                {
                    File.Delete(configROI + roiConfig.OriginalFilename);
                }
                catch (Exception ex)
                {
                    // Дальше НЕ идём: два файла с одним `Guid` на диске — это
                    // произвольный выбор при следующей загрузке, то есть молчаливая
                    // подмена правок старым файлом. Диск не тронут, правки живы в
                    // форме, сохранение можно повторить.
                    System.Diagnostics.Trace.WriteLine("ROI config rename failed: " + ex);
                    this.RestoreConfig(removed);
                    AppUi.Report(string.Format(Resources.ERRConfigFileRenameFailed, roiConfig.OriginalFilename)
                        + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                        Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                    return false;
                }
            }
            roiConfig.OriginalFilename = roiConfig.Filename;
            roiConfig.LastUpdated = DateTime.Now;
            ROIConfigData roiconfigData = roiConfig.Clone();
            string savePath = configROI + roiconfigData.Filename;
            try
            {
                Utils.AtomicFileWriter.Write(savePath, fileStream =>
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ROIConfigData));
                    xmlSerializer.Serialize(fileStream, roiconfigData);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("ROI config save failed: " + ex);
                this.RestoreConfig(removed);
                AppUi.Report(Resources.ERRSavingROIConfigFailed + "\n" + AppUi.Where(savePath)
                    + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            this.roiConfigList.Add(roiconfigData);
            this.roiConfigMap.Add(roiconfigData.Guid, roiconfigData);
            this.roiConfigList.Sort();
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            // ⛔ Снятие пометки «есть несохранённое» стояло ВЫШЕ записи, до
            // единственного места, где она может отказать. Отказ показывал окно
            // «не удалось сохранить» и тут же объявлял конфигурацию сохранённой:
            // кнопка сохранения гасла, а закрытие формы больше не спрашивало.
            // У соседа (`DeviceConfigManager.SaveConfig`) пометка снимается здесь
            // же, после успеха, — приведено к нему.
            roiConfig.Dirty = false;
            return true;
        }

        /// <summary>
        /// Вернуть в списки запись, снятую в начале <see cref="SaveConfig"/>.
        /// Мера та же, что у <c>DeviceConfigManager</c>, и по той же причине
        /// (`A8`): измерено 31.08.2026 на сборке до правки — переименование
        /// конфигурации ROI при захваченном файле давало «вернул False, в списке
        /// НЕТ, в карте НЕТ, старый файл на диске да» и ни одного сообщения.
        /// </summary>
        void RestoreConfig(ROIConfigData removed)
        {
            if (removed == null || this.roiConfigMap.ContainsKey(removed.Guid))
            {
                return;
            }
            this.roiConfigList.Add(removed);
            this.roiConfigMap.Add(removed.Guid, removed);
            this.roiConfigList.Sort();
        }

        // Token: 0x06001036 RID: 4150 RVA: 0x00058A38 File Offset: 0x00056C38
        /// <summary>
        /// ⛔ `A9`, слово в слово как у <c>DeviceConfigManager.DeleteConfig</c>:
        /// строка снимается ТОЛЬКО после того, как файл действительно исчез.
        /// Прежде отказ удаления уходил в пустой <c>catch</c>, а списки чистились
        /// всегда — конфигурация «удалялась» и возвращалась при следующем запуске.
        /// Читатель отказа — сам список: <c>ROIConfigForm.button8_Click</c> сразу
        /// зовёт <c>ListupConfigFiles</c>, и строка возвращается на экран.
        /// </summary>
        /// <summary>
        /// Удалить конфигурацию ROI. Возвращает <c>false</c>, если файл
        /// удалить не удалось: строка ОСТАЁТСЯ в списке, и вызвавший обязан
        /// не гасить форму (`A19`).
        /// </summary>
        public bool DeleteConfig(ROIConfigData roiConfig)
        {
            ROIConfigData roiconfigData = this.roiConfigMap[roiConfig.Guid];
            try
            {
                File.Delete(configROI + roiconfigData.OriginalFilename);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("ROI config delete failed: " + ex);
                AppUi.Report(string.Format(Resources.ERRConfigFileDeleteFailed, roiconfigData.OriginalFilename)
                    + "\n" + string.Format(Resources.ERRFailureReason, AppUi.Reason(ex)),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            this.roiConfigList.Remove(roiconfigData);
            this.roiConfigMap.Remove(roiconfigData.Guid);
            if (this.ROIConfigListChanged != null)
            {
                this.ROIConfigListChanged(this, new EventArgs());
            }
            return true;
        }



        /// <summary>
        /// ⛔ ЧТЕНИЕ КОНФИГУРАЦИЙ ROI ЗАВИСЕЛО ОТ ЧУЖОГО ПОРЯДКА ЗАПУСКА (`A167`).
        ///
        /// Подстановка ниже — <c>ROIPrimitiveDefinition.DefinitionsMap[…]</c> и
        /// <c>ROIPrimitiveOperation.OperationsMap[…]</c> — берёт СТАТИЧЕСКИЕ
        /// карты, которые этот класс не заводит: их заполняет <c>MainForm</c>
        /// (<c>InitializeROIPrimitiveDefinitions</c> / <c>…Operations</c>) и
        /// только он. Пока карты пусты, обе они <c>null</c>, и обращение по
        /// ключу даёт <c>NullReferenceException</c> — на КАЖДОМ файле, где есть
        /// хоть один примитив зоны.
        ///
        /// ⚠ ИЗМЕРЕНО 05.09.2026 (`RoiSupplyProbe --noinit`, сцена из
        /// поставочного <c>config\</c>): из двенадцати поставочных
        /// конфигураций ОТКАЗЫВАЮТ ДЕВЯТЬ, а три (`Ra-226 Intensities`,
        /// `Th-232 Intensities`, `Th-232`) проходят — ровно те, у которых
        /// примитивов ноль. То есть беда не в файлах: те же двенадцать при
        /// заполненных картах читаются все двенадцать, и по sha256 они
        /// побайтно те же самые.
        ///
        /// ⛔ Порядок «сначала <c>MainForm</c>» — не гарантия, а обычай, и
        /// сорваться ему просто: <c>ROIConfigForm</c> зовёт менеджер ИНИЦИАЛИЗАТОРОМ
        /// ПОЛЯ (<c>manager = ROIConfigManager.GetInstance()</c>), то есть до
        /// собственного конструктора, и так же устроен <c>DCControlPanel</c>.
        /// Всякий, кто построит такую форму раньше главного окна — проба,
        /// харнесс, будущий вызов, — получает пустой список зон и по окну
        /// об ошибке на каждый файл. Поэтому недостающее заводится ЗДЕСЬ, у
        /// места употребления, а не поручается вызывающему.
        ///
        /// ⚠ Заводится ТОЛЬКО отсутствующее: повторный
        /// <c>Initialize…</c> создал бы НОВЫЕ объекты примитивов, и уже
        /// подставленные <c>ROIPrimitiveData.Primitive</c> ссылались бы на
        /// старые — то есть сравнение по ссылке начало бы врать.
        ///
        /// ⚠ Подписи примитивов (<c>Translation</c>) берутся из ресурсов, то
        /// есть зависят от культуры потока. В приложении это ничего не двигает:
        /// там карты давно заполнены <c>MainForm</c> — уже после установки
        /// языка, — и сюда управление не заходит вовсе. А запуск, где карт нет,
        /// сегодня не получает ни подписей, ни зон вообще.
        /// </summary>
        static void EnsurePrimitiveMaps()
        {
            if (ROIPrimitiveDefinition.DefinitionsMap == null)
            {
                ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            }
            if (ROIPrimitiveOperation.OperationsMap == null)
            {
                ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            }
        }

        // Совместимости форматов в проекте не делаем, но молча терять данные тоже
        // нельзя: XmlSerializer выбрасывает всё, чему нет свойства в ROIConfigData,
        // и при первом же сохранении элемент исчезает из файла. Так уходили старые
        // кривые <ROIEfficiency> — кривая переехала в конфигурацию прибора, а
        // потерю никто не видел (W15). Решение Amber 08.08.2026: только строка в
        // лог, без диалога. Ловится любой неизвестный элемент, не только кривая.
        static void TraceDroppedElement(string path, XmlElementEventArgs e)
        {
            System.Diagnostics.Trace.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "ROI config \"{0}\": элемент <{1}> (строка {2}) программе неизвестен, " +
                "он не прочитан и будет потерян при первом сохранении конфигурации.",
                path, e.Element != null ? e.Element.Name : "?", e.LineNumber));
        }

        string configROI = Package.GetInstance().ROI;
        string configROIDir = Package.GetInstance().ROIDir;

        // Token: 0x04000964 RID: 2404
        List<ROIConfigData> roiConfigList = new List<ROIConfigData>();

        // Token: 0x04000965 RID: 2405
        Dictionary<string, ROIConfigData> roiConfigMap = new Dictionary<string, ROIConfigData>();

        // Token: 0x04000966 RID: 2406
        bool isLoaded;

        // Token: 0x04000968 RID: 2408
        static ROIConfigManager instance = new ROIConfigManager();
    }
}
