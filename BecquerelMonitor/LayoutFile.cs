using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Text;
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
    /// поднять `.bak`, иначе умолчание; сказать окном после показа». Отказ
    /// разбора → сломанный файл переименовывается в
    /// <c>ExpertMode.xml.broken-&lt;yyyyMMdd-HHmmss&gt;</c> (улика остаётся,
    /// и следующий запуск на него уже не наткнётся) → читается <c>.bak</c> →
    /// негоден и он — раскладка по умолчанию. Что случилось, метод НЕ
    /// решает сам: он возвращает <see cref="LoadOutcome"/>, а слово за экраном
    /// говорит <c>MainForm</c> (<c>LoadLayoutXml</c>) — после показа окна на
    /// запуске, сразу — из пунктов меню; без окон — строкой в поток ошибок.
    /// Отказ здесь НЕ молчит ни на одном пути (память «Признак отказа без
    /// читателя»).
    ///
    /// ⚠ Почему <c>.bak</c> можно поднимать ПОСЛЕ отказа на той же панели:
    /// <c>Persistor.LoadFromXml</c> сперва разбирает ВЕСЬ XML в структуры и
    /// только затем зовёт <c>SuspendLayout</c> и строит панель (измерено по
    /// IL: <c>LoadContents</c> → <c>LoadPanes</c> → <c>LoadDockWindows</c> →
    /// <c>LoadFloatWindows</c> → <c>XmlReader.Close</c> →
    /// <c>DockPanel.SuspendLayout</c>). Отказ разбора панели не касается, и
    /// сторож «панель уже заполнена» на втором чтении не срабатывает.
    /// Удачно поднятый <c>.bak</c> копируется на место основного файла —
    /// чтобы запуск, оборванный до закрытия, не потерял раскладку ещё раз.
    /// </summary>
    public static class LayoutFile
    {
        /// <summary>Хвост временного файла записи.</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>Хвост прежней удачной копии.</summary>
        public const string BackupSuffix = ".bak";

        /// <summary>Приставка хвоста отложенного сломанного файла; за ней — метка времени.</summary>
        public const string BrokenSuffix = ".broken-";

        /// <summary>Как кончилось чтение.</summary>
        public enum LoadKind
        {
            /// <summary>Файла нет — раскладка по умолчанию, и это не отказ.</summary>
            Absent,
            /// <summary>Прочитан основной файл.</summary>
            Loaded,
            /// <summary>Основной негоден, поднята прежняя копия <c>.bak</c>.</summary>
            LoadedFromBackup,
            /// <summary>Негодны и основной, и копия (или копии нет) — умолчание.</summary>
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

            /// <summary>Есть о чём сказать человеку: основной файл отказал.</summary>
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
        /// класса). Не бросает на сломанном файле; итог — в
        /// <see cref="LoadOutcome"/>.
        /// </summary>
        public static LoadOutcome Load(DockPanel panel, string target, DeserializeDockContent deserialize)
        {
            if (panel == null)
            {
                throw new ArgumentNullException("panel");
            }
            return Read(target, delegate (string path)
            {
                // Поток открывается здесь, а не строковой перегрузкой
                // библиотеки, чтобы его время жизни было нашим: после отказа
                // разбора файл переименовывается, и держать его нельзя.
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    panel.LoadFromXml(file, deserialize, false);
                }
            });
        }

        /// <summary>
        /// Ядро чтения: <paramref name="loadFrom"/> читает раскладку из данного
        /// пути и бросает на негодном файле. Отказ основного файла → сломанный
        /// откладывается в <c>.broken-&lt;дата&gt;</c> → читается <c>.bak</c> →
        /// иначе умолчание. Сам метод не бросает; отказ чтения — в итоге, и его
        /// обязан назвать вызывающий.
        /// </summary>
        public static LoadOutcome Read(string target, Action<string> loadFrom)
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

            if (!File.Exists(target))
            {
                outcome.Kind = LoadKind.Absent;
                return outcome;
            }

            try
            {
                loadFrom(target);
                outcome.Kind = LoadKind.Loaded;
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.TargetError = AppUi.Reason(ex);
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

            // Поднять прежнюю копию.
            if (File.Exists(outcome.Backup))
            {
                outcome.BackupExisted = true;
                try
                {
                    loadFrom(outcome.Backup);
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
                catch (Exception ex)
                {
                    outcome.BackupError = AppUi.Reason(ex);
                }
            }

            outcome.Kind = LoadKind.Default;
            return outcome;
        }

        /// <summary>
        /// Слово за экраном об отказе чтения: файл, причина, куда отложен,
        /// чем кончилось. Ресурсы в двух языках (<c>LayoutLoad*</c>). Для
        /// итога без отказа — пустая строка.
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
            string ending;
            if (outcome.Kind == LoadKind.LoadedFromBackup)
            {
                ending = string.Format(Resources.LayoutLoadBackupUsed, AppUi.Where(outcome.Backup));
            }
            else if (outcome.BackupExisted)
            {
                ending = string.Format(Resources.LayoutLoadBackupBroken, AppUi.Where(outcome.Backup), outcome.BackupError ?? "");
            }
            else
            {
                ending = Resources.LayoutLoadNoBackup;
            }
            return string.Format(Resources.LayoutLoadFailed,
                                 AppUi.Where(outcome.Target), outcome.TargetError, setAside, ending);
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
