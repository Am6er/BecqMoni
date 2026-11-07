using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x020000BE RID: 190
    public class NuclideDefinitionManager
    {
        // Token: 0x1700028C RID: 652
        // (get) Token: 0x0600092E RID: 2350 RVA: 0x0003571C File Offset: 0x0003391C
        // (set) Token: 0x0600092F RID: 2351 RVA: 0x00035724 File Offset: 0x00033924
        public NuclideDefinitionFile NuclideDefinitionFile
        {
            get
            {
                return this.nuclideDefinitionFile;
            }
            set
            {
                this.nuclideDefinitionFile = value;
            }
        }

        // Token: 0x1700028D RID: 653
        // (get) Token: 0x06000930 RID: 2352 RVA: 0x00035730 File Offset: 0x00033930
        // (set) Token: 0x06000931 RID: 2353 RVA: 0x00035740 File Offset: 0x00033940
        public List<NuclideDefinition> NuclideDefinitions
        {
            get
            {
                return this.nuclideDefinitionFile.NuclideDefinitions;
            }
            set
            {
                this.nuclideDefinitionFile.NuclideDefinitions = value;
            }
        }

        public List<NuclideSet> NuclideSets
        {
            get
            {
                return this.nuclideDefinitionFile.NuclideSets;
            }
            set
            {
                this.nuclideDefinitionFile.NuclideSets = value;
            }
        }

        /// <summary>
        /// Набор, выбранный в панели поиска пиков («Use set») для АКТИВНОГО
        /// документа, или null — «все нуклиды».
        ///
        /// Хранит выбор сам документ
        /// (<see cref="DocEnergySpectrum.SelectedNuclideSet"/>): спектров
        /// открыто несколько, и набор у каждого свой (R9). Здесь стоит выбор
        /// того из них, который сейчас на экране, — и меняет его при смене
        /// документа <see cref="DCPeakDetectionView.ShowPeakDetectionResult"/>.
        ///
        /// Живёт здесь, а не в самой панели, потому что читателей у него двое и
        /// друг до друга они не дотягиваются: поиск пиков (панель) и график
        /// (<see cref="EnergySpectrumView"/>, у него нет ссылки ни на панель, ни
        /// на MainForm). Отражать выбор во второе поле нельзя — оно разошлось бы
        /// с первым; поэтому поле ОДНО и лежит там, куда смотрят оба.
        ///
        /// Не сохраняется: это выбор на сеанс, а не настройка.
        /// </summary>
        public NuclideSet ActiveSet
        {
            get { return this.activeSet; }
            set { this.activeSet = value; }
        }

        NuclideSet activeSet;

        /// <summary>
        /// Линии наборов — по списку на набор: высота считается от самой
        /// сильной линии СВОЕГО набора, и мешать их в одну кучу нельзя.
        ///
        /// Набор рисует линии, когда выполнено ОБА условия:
        ///
        ///  * у него стоит галка «Линии интенс.» — это выключатель;
        ///  * он выбран в панели поиска пиков, ЛИБО там выбраны «все нуклиды».
        ///
        /// Одной галки мало: галки стоят у нескольких наборов сразу, и выбрав
        /// конкретный ряд человек ждёт увидеть его линии, а не все разом —
        /// в спектре тория линии лютеция и радия только мешают. Одного выбора
        /// тоже мало: «все нуклиды» — обычное положение списка, и линии тогда не
        /// появлялись бы вовсе, хотя галка стоит.
        ///
        /// Пустой список — рисовать нечего, и это не ошибка.
        /// </summary>
        public List<List<NuclideDefinition>> IntensityLineSets()
        {
            List<List<NuclideDefinition>> groups = new List<List<NuclideDefinition>>();
            if (this.nuclideDefinitionFile == null || this.NuclideSets == null)
            {
                return groups;
            }

            foreach (NuclideSet set in this.NuclideSets)
            {
                if (set == null || !set.ShowIntensityLines)
                {
                    continue;
                }

                if (this.activeSet != null && set.Id != this.activeSet.Id)
                {
                    continue;
                }

                List<NuclideDefinition> lines = new List<NuclideDefinition>();
                foreach (NuclideDefinition definition in this.NuclideDefinitions)
                {
                    if (definition != null && definition.Visible
                        && definition.Energy > 0.0 && definition.Intencity > 0.0
                        && definition.Sets != null && definition.Sets.Contains(set.Id))
                    {
                        lines.Add(definition);
                    }
                }

                if (lines.Count > 0)
                {
                    groups.Add(lines);
                }
            }

            return groups;
        }

        // Token: 0x06000932 RID: 2354 RVA: 0x00035750 File Offset: 0x00033950
        /// <summary>
        /// ⛔ Заготовка из четырёх записей допустима ТОЛЬКО в оконном режиме, и
        /// это решение, а не недосмотр (<c>S100</c>, 27.08.2026).
        ///
        /// Живой человек, у которого библиотеки и правда нет (первый запуск,
        /// снесённый <c>%AppData%\BecqMoni</c>), не должен упереться в тупик:
        /// без библиотеки панель поиска пиков нерабочая, а завести её руками
        /// негде — редактор нуклидов открывается ИЗ приложения. Поэтому в окнах
        /// поведение прежнее: заготовка пишется на диск и о ней СООБЩАЮТ, а
        /// дальше человек дополняет её из базы (<c>NucBase</c>) или подкладывает
        /// поставочный файл.
        ///
        /// Безоконный запуск (проба, харнесс) ОТКАЗЫВАЕТ. Причина в цене:
        /// состав библиотеки задаёт и поиск пиков, и разбор FSA, поэтому проба,
        /// молча получившая четыре линии вместо поставочных полутора сотен,
        /// печатает правдоподобные, но бессмысленные числа — а человека, который
        /// заметил бы окно, там нет. Отказ читает код возврата пробы; молчаливый
        /// признак вместо исключения был бы признаком без читателя.
        ///
        /// ⛔ И заготовка в безоконном запуске НЕ ПИШЕТСЯ НА ДИСК: именно так
        /// в <c>tools/effmaker/probes/build/config/</c> завёлся четырёхзаписный
        /// файл, неотличимый на вид от настоящего конфига (<c>T73</c>).
        /// </summary>
        public static NuclideDefinitionManager GetInstance()
        {
            // ⛔ Счёт обращений — ПЕРВЫМ действием, до попытки чтения (`AMBER19`):
            // гейт корпусного прогона спрашивает «поднимали ли менеджер», а не
            // «поднялся ли». Безоконный вызов без файла бросает выше по тексту,
            // и `isLoaded` остаётся ложью; исключение же проба глотает в
            // `row.Error` — по одному `isLoaded` подъём был бы невидим.
            NuclideDefinitionManager.raiseCount++;
            if (!NuclideDefinitionManager.instance.LoadDefinitionFile())
            {
                string filename = NuclideDefinitionManager.instance.nuclideDefinitionFilename;
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException(
                        "BecqMoni: nuclide library could not be loaded and there is no UI to report it to: "
                        + AppUi.Where(filename)
                        + ". The 4-nuclide starter library is a windowed-first-run fallback only: it drives both "
                        + "peak search and FSA, so a headless run on it would print plausible nonsense. "
                        + "Run from a directory that has config\\NuclideDefinition.xml.");
                }
                NuclideDefinitionManager.instance.NuclideDefinitionFile = new NuclideDefinitionFile();
                NuclideDefinitionManager.instance.InitializeNuclideDefinitionFile();
                // Only CREATE a default file when none exists. A transient read error
                // (file locked by antivirus/cloud sync) used to trigger an immediate
                // overwrite of the user's NuclideDefinition.xml with the 4-nuclide default.
                if (!File.Exists(filename))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filename));
                    if (NuclideDefinitionManager.instance.SaveDefinitionFile())
                    {
                        AppUi.Report(Resources.MSGNewNuclideDefinitionFileCreated, Resources.NotificationDialogTitle, MessageBoxIcon.Asterisk);
                    }
                }
            }
            return NuclideDefinitionManager.instance;
        }

        // Token: 0x06000934 RID: 2356 RVA: 0x000357C4 File Offset: 0x000339C4
        public bool LoadDefinitionFile(bool forceReload = false)
        {
            if (this.isLoaded && !forceReload)
            {
                return true;
            }
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(NuclideDefinitionFile));
            try
            {
                // `T266`: только на чтение и с разделением записи — довод в
                // `GlobalConfigManager.LoadConfigFile`. Прежний голый
                // `FileMode.Open` просил ЗАПИСЬ, и библиотека «только чтение»
                // или занятая вторым экземпляром не открывалась вовсе.
                using (FileStream fileStream = new FileStream(nuclideDefinitionFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    this.nuclideDefinitionFile = (NuclideDefinitionFile)xmlSerializer.Deserialize(fileStream);
                }
            }
            catch (Exception ex)
            {
                // `T266`: файл ЕСТЬ, а не прочитан — заготовку из четырёх записей
                // поверх него не писать (см. SaveDefinitionFile). Файла нет —
                // первый запуск, заготовку пишет GetInstance.
                this.loadFailedOverExistingFile =
                    !(ex is FileNotFoundException || ex is DirectoryNotFoundException)
                    && File.Exists(nuclideDefinitionFilename);
                AppUi.Report(Resources.ERRLoadingNuclideDefinitionFile, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                this.nuclideDefinitionFile = null;
                // (`AMBER310`) Отказ ПЕРЕчитывания (`forceReload`) оставлял признак «загружено» при
                // пустом файле в памяти, и `GetInstance` отдавал менеджер, падающий на первом списке.
                this.isLoaded = false;
                return false;
            }
            this.loadFailedOverExistingFile = false;
            this.isLoaded = true;
            FillChainsFromNames(this.nuclideDefinitionFile);
            // ⛔ `S102`: заверение о библиотеке обязано называть ОТКРЫТЫЙ файл.
            // Прежде его писала оснастка — ДО запуска и про файл, который она
            // положила («библиотека нуклидов: 152 записей»), а проба с другим
            // рабочим каталогом открывала другой, на 143 записи, и расхождение
            // не всплывало нигде. Строку теперь пишет тот, кто файл открыл, и
            // пишет полный путь: сверить обещание с фактом стало возможно.
            // Печатается только без окон и за безоконный прогон ровно один раз:
            // повторный вызов уходит по `isLoaded` выше, а единственная
            // перезагрузка по требованию (`forceReload`) зовётся из формы
            // редактора — то есть в окнах, где строка молчит.
            AppUi.Note("nuclide library: " + AppUi.Where(nuclideDefinitionFilename) + ": "
                + (this.nuclideDefinitionFile.NuclideDefinitions == null
                    ? 0 : this.nuclideDefinitionFile.NuclideDefinitions.Count)
                + " entries");
            return true;
        }

        /// <summary>
        /// Ряд у линий, заведённых до поля <c>Chain</c>: до сих пор родитель
        /// жил только в хвосте имени, и разбирали этот хвост в двух местах
        /// порознь. Разбирается один раз, здесь.
        ///
        /// Файл при этом НЕ переписывается: поле уедет на диск при первом
        /// сохранении из формы. Молча переписать чужой конфиг на открытии —
        /// цена, которой эта миграция не стоит, а пустое поле у линии без
        /// скобок в имени и есть правильное значение.
        /// </summary>
        static void FillChainsFromNames(NuclideDefinitionFile file)
        {
            if (file == null || file.NuclideDefinitions == null)
            {
                return;
            }

            foreach (NuclideDefinition definition in file.NuclideDefinitions)
            {
                if (definition == null || !string.IsNullOrEmpty(definition.Chain))
                {
                    continue;
                }

                definition.Chain = NuclideDefinition.ChainOf(definition.Name);
            }
        }

        // Token: 0x06000935 RID: 2357 RVA: 0x0003586C File Offset: 0x00033A6C
        public bool SaveDefinitionFile()
        {
            // ⛔ `T266`: библиотека, которая ЕСТЬ на диске, но не прочиталась,
            //    не переписывается тем, что лежит в памяти, — а лежит там
            //    заготовка из четырёх записей (GetInstance). Прежде запись
            //    на запуске была закрыта (`File.Exists` в GetInstance), но
            //    любое сохранение из редактора нуклидов или наборов
            //    (`NuclideDefinitionForm`, `NuclideSetForm`, `NucBase`) молча
            //    заменяло библиотеку человека этими четырьмя записями.
            if (this.loadFailedOverExistingFile)
            {
                AppUi.Report(Resources.ERRSavingNuclideDefinitionFile
                    + "\n" + AppUi.Where(nuclideDefinitionFilename)
                    + "\n\n" + Resources.MSGUnreadableConfigNotOverwritten,
                    Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(NuclideDefinitionFile));
            try
            {
                Utils.AtomicFileWriter.Write(nuclideDefinitionFilename, fileStream =>
                {
                    xmlSerializer.Serialize(fileStream, this.nuclideDefinitionFile);
                });
            }
            catch (Exception)
            {
                AppUi.Report(Resources.ERRSavingNuclideDefinitionFile, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return false;
            }
            return true;
        }

        /// <summary>
        /// (`AMBER310`) Имя ресурса: поставочная библиотека, вшитая в exe при сборке из
        /// <c>config\NuclideDefinition.xml</c> (`LogicalName` в <c>.csproj</c>). Файла-копии нет:
        /// вшивается тот же файл, что идёт рядом с exe содержимым. Равенство с корневым
        /// <c>config\NuclideDefinition.xml</c> держит цель сборки <c>CheckEmbeddedNuclideLibrary</c>.
        /// </summary>
        public const string ShippedLibraryResource = "BecquerelMonitor.config.NuclideDefinition.xml";

        /// <summary>
        /// (`AMBER310`) Куда ляжет прежний файл при сбросе, рядом с рабочим: одна последняя
        /// копия. Путь для текста вопроса.
        /// </summary>
        public string ResetBackupFilename
        {
            get { return this.nuclideDefinitionFilename + ".bak"; }
        }

        /// <summary>
        /// (`AMBER310`) Прочитать вшитую поставочную библиотеку и разобрать её. Ничего не
        /// меняет. Нет ресурса или он не разбирается — false и причина словами: сбрасывать
        /// не на что, и спрашивать человека о том, чего сделать нельзя, незачем.
        /// </summary>
        public static bool ReadShippedLibrary(out byte[] bytes, out NuclideDefinitionFile parsed, out string refusal)
        {
            bytes = null;
            parsed = null;
            refusal = null;
            using (Stream stream = typeof(NuclideDefinitionManager).Assembly.GetManifestResourceStream(ShippedLibraryResource))
            {
                if (stream == null)
                {
                    refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRShippedLibraryMissing, ShippedLibraryResource);
                    return false;
                }

                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    bytes = copy.ToArray();
                }
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    parsed = (NuclideDefinitionFile)new XmlSerializer(typeof(NuclideDefinitionFile)).Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRShippedLibraryUnreadable, AppUi.Reason(ex));
                return false;
            }

            return true;
        }

        /// <summary>
        /// ⛔ (`AMBER310`, 05.11.2026) СБРОС БИБЛИОТЕКИ НА ПОСТАВОЧНУЮ. Amber, консоль, дословно:
        /// «В библиотеку нуклидов добавь кнопку Reset Defaults, чтобы сбрасывать всю библиотеку
        /// на текущий поставочный набор нуклидов и сбрасывать все сеты на текущий поставочный
        /// набор сетов.»; вопросником: «Вшить в exe (Рекомендую)», «Вопрос и резервная копия
        /// (Рекомендую)».
        ///
        /// Файл получает ВШИТЫЕ БАЙТЫ как есть, а не вывод сериализатора: сброс обязан дать тот
        /// же файл, что поставка, побайтно. Прежний файл уходит в <c>.bak</c> одним действием с
        /// заменой (<see cref="File.Replace(string, string, string)"/>), поэтому при отказе записи
        /// не меняется ни файл, ни прежняя копия, ни память. Раскладка (портативная,
        /// установленная) ветвления здесь не требует: путь уже разрешён
        /// <see cref="Package.NuclideDefinition"/>.
        ///
        /// Выбор набора держат по ссылке панель (<see cref="ActiveSet"/>) и документы
        /// (<see cref="DocEnergySpectrum.SelectedNuclideSet"/>), а объекты менеджера здесь новые:
        /// выбор переносится на набор с тем же <c>Id</c>, а если такого в поставке нет, снимается
        /// («все нуклиды»), иначе документ держал бы то, чего в библиотеке больше нет.
        /// </summary>
        public bool ResetToShippedLibrary(byte[] bytes, NuclideDefinitionFile parsed, out string refusal)
        {
            refusal = null;
            string path = this.nuclideDefinitionFilename;
            string temp = path + ".reset.tmp";
            string backup = this.ResetBackupFilename;
            try
            {
                // ⛔ (критик `critSets`, Н1) Файл уже поставочный — замена значила бы копию поставки
                // поверх единственной копии библиотеки человека: второе нажатие её уничтожало.
                if (!this.FileMatchesShipped(bytes))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }

                    if (File.Exists(path))
                    {
                        File.Replace(temp, path, backup);
                    }
                    else
                    {
                        File.Move(temp, path);
                    }
                }
            }
            catch (Exception ex)
            {
                RestoreAfterFailedReset(path, temp, backup);
                refusal = string.Format(CultureInfo.InvariantCulture, Resources.ERRShippedLibraryWrite,
                                        AppUi.Where(path), AppUi.Reason(ex));
                return false;
            }

            this.nuclideDefinitionFile = parsed;
            this.isLoaded = true;
            this.loadFailedOverExistingFile = false;
            FillChainsFromNames(this.nuclideDefinitionFile);
            this.activeSet = SameSetIn(this.activeSet, parsed.NuclideSets);
            foreach (DocEnergySpectrum document in DocumentManager.GetInstance().DocumentList)
            {
                document.SelectedNuclideSet = SameSetIn(document.SelectedNuclideSet, parsed.NuclideSets);
            }

            return true;
        }

        /// <summary>
        /// (`AMBER310`) Рабочий файл по содержимому равен вшитому. Номера сверки с базой (`DbRow`, `DbXray`)
        /// и порядок строк в счёт не идут: окно заполняет номера само, и «Сохранить» без правки не должно
        /// считаться правкой, иначе следующий сброс заменил бы копию библиотеки человека (критик
        /// `critWave2`, S2). Файла нет или он не разбирается — false: сравнить не с чем, решает запись.
        /// </summary>
        public bool FileMatchesShipped(byte[] bytes)
        {
            try
            {
                byte[] current;
                using (FileStream stream = new FileStream(this.nuclideDefinitionFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    current = copy.ToArray();
                }

                return ContentOf(current).SequenceEqual(ContentOf(bytes));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Библиотека без номеров сверки и в одном порядке строк, тем же сериализатором.</summary>
        static byte[] ContentOf(byte[] library)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(NuclideDefinitionFile));
            NuclideDefinitionFile file;
            using (MemoryStream stream = new MemoryStream(library))
            {
                file = (NuclideDefinitionFile)serializer.Deserialize(stream);
            }

            FillChainsFromNames(file);
            foreach (NuclideDefinition definition in file.NuclideDefinitions)
            {
                definition.DbRow = 0;
                definition.DbXray = "";
            }

            file.NuclideDefinitions = file.NuclideDefinitions
                .OrderBy(d => d.Energy).ThenBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Intencity).ToList();
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, file);
                return stream.ToArray();
            }
        }

        /// <summary>(`AMBER310`) Копия от прошлого сброса уже лежит: новый сброс её заменит.</summary>
        public bool ResetBackupExists
        {
            get { return File.Exists(this.ResetBackupFilename); }
        }

        /// <summary>
        /// Уборка после отказа записи. Редкий исход замены «прежний файл уже переименован в
        /// копию, новый на место не встал» (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2) возвращает
        /// прежний файл на его имя: библиотека не должна исчезнуть из-за отказа.
        /// </summary>
        static void RestoreAfterFailedReset(string path, string temp, string backup)
        {
            try
            {
                if (!File.Exists(path) && File.Exists(backup))
                {
                    File.Move(backup, path);
                }

                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception)
            {
                // Главное здесь исходный отказ, он и уходит человеку словами.
            }
        }

        /// <summary>Набор с тем же <c>Id</c> в новом списке или null.</summary>
        static NuclideSet SameSetIn(NuclideSet set, List<NuclideSet> sets)
        {
            if (set == null)
            {
                return null;
            }

            foreach (NuclideSet candidate in sets)
            {
                if (candidate != null && candidate.Id == set.Id)
                {
                    return candidate;
                }
            }

            return null;
        }

        // Token: 0x06000936 RID: 2358 RVA: 0x000358F4 File Offset: 0x00033AF4
        void InitializeNuclideDefinitionFile()
        {
            // (П266, 14.10.2026) Энергии и периоды заготовки — из `nucdb` (NuDat 3), а не
            // круглые числа исходной программы: линия Cs-134 стояла на 798.0 кэВ, где линии
            // нет вовсе (сильные — 795.864 и 801.95, у германия поиск пиков с таким
            // определением 796-й не подписывал), K-40 — 1.277·10⁹ лет против 1.248·10⁹.
            // Годы — те же, что пишет импорт NucBase (`HalfLifeYearsFromCell`): у всех трёх
            // нуклидов база держит период в годах, и число переносится как есть.
            this.nuclideDefinitionFile.NuclideDefinitions.Clear();
            NuclideDefinition nuclideDefinition = new NuclideDefinition();
            nuclideDefinition.Name = "Cs134";
            nuclideDefinition.Energy = 604.721;
            nuclideDefinition.HalfLife = 2.0652;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition);
            NuclideDefinition nuclideDefinition2 = new NuclideDefinition();
            nuclideDefinition2.Name = "Cs137";
            nuclideDefinition2.Energy = 661.657;
            nuclideDefinition2.HalfLife = 30.08;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition2);
            NuclideDefinition nuclideDefinition3 = new NuclideDefinition();
            nuclideDefinition3.Name = "Cs134";
            nuclideDefinition3.Energy = 795.864;
            nuclideDefinition3.HalfLife = 2.0652;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition3);
            NuclideDefinition nuclideDefinition4 = new NuclideDefinition();
            nuclideDefinition4.Name = "K40";
            nuclideDefinition4.Energy = 1460.82;
            nuclideDefinition4.HalfLife = 1.248E+9;
            this.nuclideDefinitionFile.NuclideDefinitions.Add(nuclideDefinition4);
        }

        string nuclideDefinitionFilename = Package.GetInstance().NuclideDefinition;

        /// <summary>
        /// Сколько раз звали <see cref="GetInstance"/> за время жизни процесса
        /// — в том числе вызовы, кончившиеся броском (безоконный запуск без
        /// файла, <c>S100</c>). Признак «поднимали ли поставочную библиотеку»
        /// для гейта корпусного прогона (`AMBER19`): корпус считается только по
        /// нуклидам из базы, и ни одно обращение к менеджеру там не законно.
        /// Читатель — <c>CorpusFsaProbe</c>, в конце прогона; сбросить нельзя
        /// нарочно.
        /// </summary>
        public static int RaiseCount
        {
            get { return NuclideDefinitionManager.raiseCount; }
        }

        static int raiseCount;

        // Token: 0x04000513 RID: 1299
        static NuclideDefinitionManager instance = new NuclideDefinitionManager();

        // Token: 0x04000514 RID: 1300
        NuclideDefinitionFile nuclideDefinitionFile;

        // Token: 0x04000515 RID: 1301
        bool isLoaded;

        /// <summary>
        /// `T266`: последняя попытка чтения отказала, а файл библиотеки на
        /// диске ЕСТЬ (занят, нет прав, битый XML) — <see cref="SaveDefinitionFile"/>
        /// его не переписывает. Удачное чтение снимает признак.
        /// </summary>
        bool loadFailedOverExistingFile;
    }
}
