using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace BecquerelMonitor
{
    /// <summary>
    /// Файл раскладки панелей (<c>&lt;Layout&gt;\ExpertMode.xml</c>): запись,
    /// которая не оставляет пустого файла, и чтение, которое не валит
    /// приложение. Строка `AMBER43`, задача Amber 15.09.2026 дословно:
    /// «Падение приложения, если файл настроек расположения окон в UI
    /// поломался при сохранении. Основная задача - избежать поломки этого
    /// файла ExpertMode.xml».
    ///
    /// ⛔ ЧТО БЫЛО. <c>DockPanel.SaveAsXml(имя)</c> библиотеки DockPanelSuite
    /// 3.1.1 — это <c>new FileStream(имя, FileMode.Create)</c> и только затем
    /// сериализация (измерено по IL: <c>ldc.i4.2</c> → <c>newobj FileStream</c>
    /// → <c>call SaveAsXml(Stream)</c>). То есть файл сперва ОБРЕЗАЕТСЯ до нуля
    /// и лишь потом пишется. Процесс, остановленный между этими шагами
    /// (завершение сеанса Windows — <c>FormClosing</c> идёт и при
    /// <c>WindowsShutDown</c>, а система убивает приложение по своему сроку;
    /// отказ сериализации; полный диск), оставляет пустой файл. А чтение
    /// стояло в <c>MainForm_Load</c> без заслона: <c>XmlException: Root element
    /// is missing</c> из <c>OnLoad</c> никто не ловил, и приложение падало на
    /// КАЖДОМ запуске, пока файл не удалят руками (журнал падения —
    /// <c>handover/amber43/crashlog-2026-09-15.txt</c>).
    ///
    /// ЗАПИСЬ (<see cref="Save"/> / <see cref="Write"/>) — атомарная, в три шага:
    /// <list type="number">
    /// <item>раскладка сериализуется В ПАМЯТЬ (<c>SaveAsXml(Stream,
    /// Encoding)</c>); отказ сериализации файла не касается вовсе — нечему
    /// касаться;</item>
    /// <item>байты ложатся во временный файл рядом (<c>ExpertMode.xml.tmp</c>)
    /// с <c>Flush(true)</c> — до диска, а не до кэша;</item>
    /// <item><c>File.Replace(tmp, target, target + ".bak")</c> — подмена одним
    /// вызовом ОС (<c>ReplaceFile</c>), прежняя удачная копия остаётся
    /// <c>.bak</c>; целевого файла ещё нет — <c>File.Move</c>.</item>
    /// </list>
    /// Убитый посреди процесс оставляет либо старый файл, либо новый, но не
    /// пустой; в худшем случае рядом лежит осиротевший <c>.tmp</c>, который
    /// следующая запись перезапишет. Отказ любого шага убирает <c>.tmp</c> и
    /// бросает — читатели отказа прежние (~~`A13`~~: окно либо записка).
    ///
    /// ⚠ Кодировка — <c>Encoding.Unicode</c>, как у прежнего
    /// <c>SaveAsXml(имя)</c> (измерено по IL: <c>call Encoding.get_Unicode</c>):
    /// файл, записанный этим путём, байт в байт тот же, что писала библиотека.
    ///
    /// ⚠ Сериализатор — ДЕЛЕГАТ, а не панель, нарочно: проба
    /// (<c>LayoutFileProbe</c>) подсаживает в него отказ и останов и мерит
    /// файл после; у приложения делегат один — <c>panel.SaveAsXml</c>.
    ///
    /// ЧТЕНИЕ (<see cref="Load"/> / <see cref="Read"/>) — под заслоном, по
    /// решению Amber 15.09.2026 дословно: «Не падать: отложить сломанный,
    /// поднять `.bak`, иначе умолчание; сказать окном после показа», и по
    /// решению Amber 17.09.2026 дословно: «Поставочная раскладка как
    /// умолчание». ЛЕСТНИЦА:
    /// <list type="number">
    /// <item>целевой файл; отказ разбора → сломанный переименовывается в
    /// <c>ExpertMode.xml.broken-&lt;yyyyMMdd-HHmmss&gt;</c> (улика остаётся, и
    /// следующий запуск на него уже не наткнётся);</item>
    /// <item><c>.bak</c> — прежняя удачная копия;</item>
    /// <item>ПОСТАВОЧНЫЙ файл <c>config\layout\ExpertMode.xml</c> из каталога
    /// приложения (<see cref="SupplyPathFor"/>) — в установке ClickOnce он там
    /// лежит, это посев первого запуска (<c>MainForm</c>, копирование
    /// <c>config</c> в <c>%AppData%\BecqMoni</c>); берётся ТОЛЬКО если его путь
    /// не совпадает с целевым — в портативной сборке (<c>Package.IsStandAlone</c>,
    /// <c>Package.Layout</c> — каталог сборки) рабочий файл и есть поставочный,
    /// и он уже отложен ступенью 1;</item>
    /// <item>ВСТРОЕННАЯ копия того же файла (<see cref="BuiltinLayout"/> —
    /// EmbeddedResource <c>config\layout\ExpertMode.xml</c> в сборке, тот же
    /// файл, не копия: ссылка в <c>.csproj</c>);</item>
    /// <item>пустое умолчание — панели по одной из меню «Вид».</item>
    /// </list>
    /// ⛔ ЗАЧЕМ СТУПЕНИ 3–4. Найдено экраном 17.09.2026 (П95): без <c>.bak</c>
    /// — а это ровно случай Amber 15.09, первое падение, копии ещё нет —
    /// «умолчание» было ПУСТЫМ окном: ни панелей, ни вкладок спектров
    /// (документы живут в том же файле раскладки), панели открываются по одной
    /// через меню. Файла НЕТ вовсе — та же лестница со ступени 3, и это не
    /// отказ (слова нет): раскладка по умолчанию — поставочная, а не пустая.
    ///
    /// Что случилось, метод НЕ решает сам: он возвращает <see cref="LoadOutcome"/>,
    /// а слово за экраном говорит <c>MainForm</c> (<c>LoadLayoutXml</c>) —
    /// после показа окна на запуске, сразу — из пунктов меню; без окон —
    /// строкой в поток ошибок. Отказ здесь НЕ молчит ни на одном пути (память
    /// «Признак отказа без читателя»).
    ///
    /// ⚠ Почему следующую ступень можно поднимать ПОСЛЕ отказа на той же
    /// панели: <c>Persistor.LoadFromXml</c> сперва разбирает ВЕСЬ XML в
    /// структуры и только затем зовёт <c>SuspendLayout</c> и строит панель
    /// (измерено по IL: <c>LoadContents</c> → <c>LoadPanes</c> →
    /// <c>LoadDockWindows</c> → <c>LoadFloatWindows</c> → <c>XmlReader.Close</c>
    /// → <c>DockPanel.SuspendLayout</c>). Отказ разбора панели не касается, и
    /// сторож «панель уже заполнена» на следующем чтении не срабатывает.
    /// Удачно поднятый <c>.bak</c> копируется на место основного файла —
    /// чтобы запуск, оборванный до закрытия, не потерял раскладку ещё раз.
    /// Поставочная и встроенная копии на место НЕ кладутся: закрытие запишет
    /// то, что человек расставил, а оборванный запуск придёт к той же ступени.
    /// </summary>
    public static class LayoutFile
    {
        /// <summary>Хвост временного файла записи.</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>Хвост прежней удачной копии.</summary>
        public const string BackupSuffix = ".bak";

        /// <summary>Приставка хвоста отложенного сломанного файла; за ней — метка времени.</summary>
        public const string BrokenSuffix = ".broken-";

        /// <summary>Поставочный файл раскладки — от каталога приложения (тот, что сеет первый запуск).</summary>
        public const string SupplyRelativePath = "config\\layout\\ExpertMode.xml";

        /// <summary>
        /// Имя встроенной копии поставочного файла (EmbeddedResource в
        /// <c>.csproj</c>, <c>LogicalName</c>). Файл тот же —
        /// <c>BecquerelMonitor/config/layout/ExpertMode.xml</c>, ссылкой, а не копией.
        /// </summary>
        public const string BuiltinResourceName = "BecquerelMonitor.config.layout.ExpertMode.xml";

        /// <summary>Как кончилось чтение — откуда взята раскладка.</summary>
        public enum LoadKind
        {
            /// <summary>Файла нет, и ни поставочной, ни встроенной копии не вышло — пусто, и это не отказ.</summary>
            Absent,
            /// <summary>Прочитан основной файл.</summary>
            Loaded,
            /// <summary>Основной негоден, поднята прежняя копия <c>.bak</c>.</summary>
            LoadedFromBackup,
            /// <summary>Показана поставочная раскладка из каталога приложения.</summary>
            LoadedFromSupply,
            /// <summary>Показана встроенная копия поставочной раскладки.</summary>
            LoadedFromBuiltin,
            /// <summary>Основной негоден, и ни одна ступень не поднялась — пустое умолчание.</summary>
            Default,
        }

        /// <summary>
        /// Итог чтения: что читалось, что отказало и почему, куда отложен
        /// сломанный файл. Слово за экраном собирает <see cref="Describe"/>.
        /// </summary>
        public sealed class LoadOutcome
        {
            public LoadKind Kind;
            /// <summary>Основной файл.</summary>
            public string Target;
            /// <summary>Был ли основной файл на диске.</summary>
            public bool TargetExisted;
            /// <summary>Причина отказа основного файла (<c>AppUi.Reason</c>); null — не отказывал.</summary>
            public string TargetError;
            /// <summary>Куда отложен сломанный файл; null — отложить не удалось.</summary>
            public string BrokenPath;
            /// <summary>Причина, по которой отложить не удалось; null — удалось или не требовалось.</summary>
            public string BrokenMoveError;
            /// <summary>Путь прежней копии.</summary>
            public string Backup;
            /// <summary>Была ли прежняя копия на диске в момент отказа основного файла.</summary>
            public bool BackupExisted;
            /// <summary>Причина отказа прежней копии; null — не читалась или прочитана.</summary>
            public string BackupError;
            /// <summary>Путь поставочного файла; null — ступени нет (портативная сборка: он и есть целевой).</summary>
            public string Supply;
            /// <summary>Был ли поставочный файл на диске, когда до него дошло.</summary>
            public bool SupplyExisted;
            /// <summary>Причина отказа поставочного файла; null — не читался или прочитан.</summary>
            public string SupplyError;
            /// <summary>Причина отказа встроенной копии; null — не читалась или прочитана.</summary>
            public string BuiltinError;

            /// <summary>Есть о чём сказать человеку: основной файл был и отказал.</summary>
            public bool Failed
            {
                get
                {
                    return this.TargetError != null;
                }
            }
        }

        /// <summary>Путь прежней удачной копии для данного файла раскладки.</summary>
        public static string BackupOf(string target)
        {
            return target + BackupSuffix;
        }

        /// <summary>Путь временного файла записи для данного файла раскладки.</summary>
        public static string TempOf(string target)
        {
            return target + TempSuffix;
        }

        /// <summary>
        /// Поставочный файл раскладки для данного целевого: <c>config\layout\ExpertMode.xml</c>
        /// от каталога приложения (<c>Application.ExecutablePath</c> — тот же
        /// корень, от которого <c>MainForm</c> сеет <c>config</c> в
        /// <c>%AppData%\BecqMoni</c> на первом запуске). Возвращает null, когда
        /// поставочный путь СОВПАДАЕТ с целевым — портативная сборка
        /// (<c>Package.IsStandAlone</c>), где рабочий файл и есть поставочный:
        /// читать «поставочный» после того, как он же отложен как сломанный,
        /// нечего, ступень пропускается ко встроенной копии. Сравнение — по
        /// полным путям без учёта регистра, а не по <c>Package.IsStandAlone</c>:
        /// условие ступени — сам путь, и проба меряет его без ClickOnce.
        /// </summary>
        public static string SupplyPathFor(string target)
        {
            string appDir;
            try
            {
                appDir = Path.GetDirectoryName(Application.ExecutablePath);
            }
            catch (Exception)
            {
                return null;
            }
            if (string.IsNullOrEmpty(appDir))
            {
                return null;
            }
            string supply = Path.Combine(appDir, SupplyRelativePath);
            if (SamePath(supply, target))
            {
                return null;
            }
            return supply;
        }

        /// <summary>
        /// Встроенная копия поставочного файла раскладки — поток ресурса
        /// сборки; null, если ресурса в сборке нет (тогда ступень — отказ с
        /// причиной, а не тишина).
        /// </summary>
        public static Stream BuiltinLayout()
        {
            return typeof(LayoutFile).Assembly.GetManifestResourceStream(BuiltinResourceName);
        }

        /// <summary>
        /// Записать раскладку панели в <paramref name="target"/> атомарно
        /// (довод — в описании класса). Отказ — исключением; файл при отказе
        /// либо прежний, либо новый, но не пустой и не обрезанный.
        /// </summary>
        public static void Save(DockPanel panel, string target)
        {
            if (panel == null)
            {
                throw new ArgumentNullException("panel");
            }
            Write(target, delegate (Stream stream)
            {
                // Двухаргументная перегрузка = upstream: false — с объявлением
                // <?xml …?> и WriteEndDocument, как у SaveAsXml(имя); она
                // закрывает поток, но MemoryStream.ToArray() работает и после
                // закрытия. ⚠ Перегрузка с upstream: true объявления НЕ пишет
                // («часть большого документа») — файл выходил на 41 знак короче
                // библиотечного (измерено пробой LayoutFileProbe, плечо 3.1:
                // 2010 байт против 2092). Кодировка та же, что у SaveAsXml(имя).
                panel.SaveAsXml(stream, Encoding.Unicode);
            });
        }

        /// <summary>
        /// Ядро записи: <paramref name="serialize"/> пишет в поток памяти,
        /// байты ложатся во временный файл рядом и подменяют
        /// <paramref name="target"/> одним вызовом ОС; прежний файл остаётся
        /// <c>.bak</c>. Отказ любого шага убирает <c>.tmp</c> и бросает.
        /// </summary>
        public static void Write(string target, Action<Stream> serialize)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentNullException("target");
            }
            if (serialize == null)
            {
                throw new ArgumentNullException("serialize");
            }

            // 1. В память. Отказ здесь файла не касается вовсе.
            byte[] bytes;
            using (MemoryStream memory = new MemoryStream())
            {
                serialize(memory);
                bytes = memory.ToArray();
            }
            if (bytes.Length == 0)
            {
                // Ровно тот файл, от которого чинимся, — писать его нельзя.
                throw new InvalidOperationException(
                    "layout serializer produced no bytes; the existing file " + AppUi.Where(target) + " is kept");
            }

            // 2. Во временный файл рядом — тот же каталог, тот же том: иначе
            //    File.Replace не подменит.
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temp = TempOf(target);
            try
            {
                using (FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                // 3. Подмена. ReplaceFile — один вызов ОС; прежний файл уходит
                //    в .bak (существующий .bak перезаписывается).
                if (File.Exists(target))
                {
                    File.Replace(temp, target, BackupOf(target), true);
                }
                else
                {
                    File.Move(temp, target);
                }
            }
            catch (Exception)
            {
                // .tmp не должен оставаться: следующий запуск его не читает, но
                // и лежать рядом с раскладкой ему незачем. Отказ уборки не
                // важнее отказа записи — глотается, бросается исходное.
                TryDelete(temp);
                throw;
            }
        }

        /// <summary>
        /// Прочитать раскладку в панель под заслоном (довод — в описании
        /// класса): целевой файл → <c>.bak</c> → поставочный файл из каталога
        /// приложения → встроенная копия → пусто. Не бросает на сломанном
        /// файле; итог — в <see cref="LoadOutcome"/>.
        /// </summary>
        public static LoadOutcome Load(DockPanel panel, string target, DeserializeDockContent deserialize)
        {
            return Load(panel, target, deserialize, SupplyPathFor(target), BuiltinLayout);
        }

        /// <summary>
        /// То же с явными ступенями 3–4: <paramref name="supply"/> — путь
        /// поставочного файла (null — ступени нет), <paramref name="builtin"/>
        /// — поток встроенной копии (null — ступени нет). Проба подсаживает
        /// сюда своё; у приложения — <see cref="SupplyPathFor"/> и
        /// <see cref="BuiltinLayout"/>.
        /// </summary>
        public static LoadOutcome Load(DockPanel panel, string target, DeserializeDockContent deserialize,
                                       string supply, Func<Stream> builtin)
        {
            if (panel == null)
            {
                throw new ArgumentNullException("panel");
            }
            return Read(target, supply, delegate (Stream stream)
            {
                // Поток — наш, а не строковой перегрузки библиотеки: после
                // отказа разбора файл переименовывается, и держать его нельзя;
                // closeStream: false — закрывает тот, кто открыл.
                panel.LoadFromXml(stream, deserialize, false);
            }, builtin);
        }

        /// <summary>
        /// Ядро чтения: <paramref name="loadFrom"/> читает раскладку из потока
        /// и бросает на негодном содержимом. Ступени — целевой файл (отказ →
        /// сломанный откладывается в <c>.broken-&lt;дата&gt;</c>) → <c>.bak</c>
        /// → поставочный файл <paramref name="supply"/> (null — ступени нет) →
        /// встроенная копия <paramref name="builtin"/> (null — ступени нет) →
        /// пусто. Файл, которого нет, — не отказ: лестница идёт со ступени
        /// поставочного файла молча. Сам метод не бросает; отказ чтения — в
        /// итоге, и его обязан назвать вызывающий.
        /// </summary>
        public static LoadOutcome Read(string target, string supply, Action<Stream> loadFrom, Func<Stream> builtin)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentNullException("target");
            }
            if (loadFrom == null)
            {
                throw new ArgumentNullException("loadFrom");
            }

            LoadOutcome outcome = new LoadOutcome();
            outcome.Target = target;
            outcome.Backup = BackupOf(target);
            outcome.Supply = supply;

            // 1. Целевой файл.
            outcome.TargetExisted = File.Exists(target);
            if (outcome.TargetExisted)
            {
                if (TryLoadFile(target, loadFrom, out outcome.TargetError))
                {
                    outcome.Kind = LoadKind.Loaded;
                    return outcome;
                }

                // Отложить сломанный: улика остаётся, следующий запуск на неё не
                // наткнётся. Метка времени — инвариантной культурой (правило Amber
                // 05.09.2026 о культуре: печать — только явной инвариантной).
                string broken = target + BrokenSuffix + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                for (int n = 1; File.Exists(broken) && n < 100; n++)
                {
                    broken = target + BrokenSuffix + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                             + "-" + n.ToString(CultureInfo.InvariantCulture);
                }
                try
                {
                    File.Move(target, broken);
                    outcome.BrokenPath = broken;
                }
                catch (Exception ex)
                {
                    outcome.BrokenMoveError = AppUi.Reason(ex);
                }

                // 2. Прежняя копия.
                if (File.Exists(outcome.Backup))
                {
                    outcome.BackupExisted = true;
                    if (TryLoadFile(outcome.Backup, loadFrom, out outcome.BackupError))
                    {
                        outcome.Kind = LoadKind.LoadedFromBackup;
                        // Копия годна — вернуть её на место основного файла, чтобы
                        // запуск, оборванный до закрытия, не пришёл к умолчанию.
                        // Не вышло (файл ещё занят) — не беда: закрытие запишет.
                        if (outcome.BrokenPath != null)
                        {
                            try
                            {
                                File.Copy(outcome.Backup, target, false);
                            }
                            catch (Exception)
                            {
                            }
                        }
                        return outcome;
                    }
                }
            }

            // 3. Поставочный файл из каталога приложения (решение Amber
            //    17.09.2026 «Поставочная раскладка как умолчание»). Ступени нет
            //    (null) в портативной сборке — там он и есть целевой.
            if (supply != null && File.Exists(supply))
            {
                outcome.SupplyExisted = true;
                if (TryLoadFile(supply, loadFrom, out outcome.SupplyError))
                {
                    outcome.Kind = LoadKind.LoadedFromSupply;
                    return outcome;
                }
            }

            // 4. Встроенная копия того же файла.
            if (builtin != null)
            {
                try
                {
                    using (Stream stream = builtin())
                    {
                        if (stream == null)
                        {
                            throw new FileNotFoundException("embedded layout resource is missing", BuiltinResourceName);
                        }
                        loadFrom(stream);
                    }
                    outcome.Kind = LoadKind.LoadedFromBuiltin;
                    return outcome;
                }
                catch (Exception ex)
                {
                    outcome.BuiltinError = AppUi.Reason(ex);
                }
            }

            // 5. Пусто. Файла не было — не отказ (Absent); был и негоден — Default.
            outcome.Kind = outcome.TargetExisted ? LoadKind.Default : LoadKind.Absent;
            return outcome;
        }

        /// <summary>
        /// Одна ступень лестницы по файлу: поток открывается здесь и живёт
        /// ровно на время чтения (<c>FileShare.Read</c>), чтобы отказавший
        /// файл можно было тут же переименовать. Отказ — false с причиной.
        /// </summary>
        static bool TryLoadFile(string path, Action<Stream> loadFrom, out string error)
        {
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    loadFrom(file);
                }
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = AppUi.Reason(ex);
                return false;
            }
        }

        /// <summary>
        /// Слово за экраном об отказе чтения: файл, причина, куда отложен,
        /// чем кончилось — какая ступень лестницы поднялась (копия,
        /// поставочная, встроенная) или что осталось пусто. Ресурсы в двух
        /// языках (<c>LayoutLoad*</c>). Для итога без отказа — пустая строка.
        /// </summary>
        public static string Describe(LoadOutcome outcome)
        {
            if (outcome == null || !outcome.Failed)
            {
                return "";
            }
            string setAside = (outcome.BrokenPath != null)
                ? string.Format(Resources.LayoutLoadSetAside, AppUi.Where(outcome.BrokenPath))
                : string.Format(Resources.LayoutLoadNotSetAside, outcome.BrokenMoveError ?? "");
            StringBuilder ending = new StringBuilder();
            if (outcome.Kind == LoadKind.LoadedFromBackup)
            {
                ending.Append(string.Format(Resources.LayoutLoadBackupUsed, AppUi.Where(outcome.Backup)));
            }
            else
            {
                ending.Append(outcome.BackupExisted
                    ? string.Format(Resources.LayoutLoadBackupBroken, AppUi.Where(outcome.Backup), outcome.BackupError ?? "")
                    : Resources.LayoutLoadNoBackup);
                if (outcome.Kind == LoadKind.LoadedFromSupply)
                {
                    ending.Append(' ').Append(string.Format(Resources.LayoutLoadSupplyUsed, AppUi.Where(outcome.Supply)));
                }
                else
                {
                    if (outcome.SupplyExisted)
                    {
                        ending.Append(' ').Append(string.Format(Resources.LayoutLoadSupplyBroken,
                                                                AppUi.Where(outcome.Supply), outcome.SupplyError ?? ""));
                    }
                    if (outcome.Kind == LoadKind.LoadedFromBuiltin)
                    {
                        ending.Append(' ').Append(Resources.LayoutLoadBuiltinUsed);
                    }
                    else
                    {
                        if (outcome.BuiltinError != null)
                        {
                            ending.Append(' ').Append(string.Format(Resources.LayoutLoadBuiltinBroken, outcome.BuiltinError));
                        }
                        ending.Append(' ').Append(Resources.LayoutLoadDefault);
                    }
                }
            }
            return string.Format(Resources.LayoutLoadFailed,
                                 AppUi.Where(outcome.Target), outcome.TargetError, setAside, ending.ToString());
        }

        /// <summary>
        /// Строка для потока ошибок о том, что файла раскладки НЕ БЫЛО, а
        /// поставочная или встроенная копия отказала. Это не отказ человеку
        /// (файла и не ждали) и окна не заслуживает — читатель без окон
        /// (<c>AppUi.Note</c>), чтобы битая поставочная копия в каталоге
        /// приложения не молчала. Пустая строка — сказать нечего.
        /// </summary>
        public static string DescribeAbsent(LoadOutcome outcome)
        {
            if (outcome == null || outcome.TargetExisted)
            {
                return "";
            }
            if (outcome.SupplyError == null && outcome.BuiltinError == null)
            {
                return "";
            }
            StringBuilder text = new StringBuilder();
            text.Append("layout file ").Append(AppUi.Where(outcome.Target)).Append(" is absent");
            if (outcome.SupplyError != null)
            {
                text.Append("; supplied layout ").Append(AppUi.Where(outcome.Supply)).Append(" failed: ").Append(outcome.SupplyError);
            }
            if (outcome.BuiltinError != null)
            {
                text.Append("; built-in layout failed: ").Append(outcome.BuiltinError);
            }
            text.Append("; shown: ").Append(outcome.Kind.ToString());
            return text.ToString();
        }

        static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
