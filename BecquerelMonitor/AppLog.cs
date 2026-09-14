using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace BecquerelMonitor
{
    /// <summary>
    /// Журнал приложения: даёт ЧИТАТЕЛЯ следам <see cref="Trace"/>.
    /// </summary>
    /// <remarks>
    /// ⛔ Строка `A15`, решение Amber 28.08.2026. До этого в дереве было
    /// <b>110 вызовов `Trace.WriteLine` в 16 файлах</b>, и читать их было
    /// НЕКОМУ: в `App.config` нет раздела `system.diagnostics`, ни одного
    /// `Trace.Listeners` во всём дереве, а в `Release` при этом стоит
    /// `DefineConstants=TRACE` — то есть вызовы живые и уходят в
    /// `OutputDebugString`, видимый только под отладчиком. При жалобе человека
    /// взять подробности было неоткуда.
    ///
    /// Перепись и разбор: `app-silent-failures.md`. Из 110 внутри `catch`
    /// стоит 31, остальные 79 — ход работы; журнал оживляет и то, и другое.
    ///
    /// ⚠ Почему НЕ через `App.config`. Раздел `system.diagnostics` умеет завести
    /// слушателя, но путь в нём — строка, а каталог у нас зависит от раскладки
    /// (<see cref="Package.UserDirectory"/> отдаёт разное для установленной и
    /// портативной), и подрезать разросшийся файл он тоже не умеет. Поэтому
    /// слушатель заводится кодом.
    /// </remarks>
    static class AppLog
    {
        /// <summary>Имя журнала внутри пользовательского каталога.</summary>
        public const string FileName = "becqmoni.log";

        /// <summary>
        /// Порог подрезки. Дойдя до него, журнал переименовывается в
        /// <c>becqmoni.log.1</c> (прежний <c>.1</c> удаляется), и запись
        /// начинается заново. То есть на диске лежит не больше двух таких
        /// файлов — от 2 до 4 МБ.
        ///
        /// ⚠ Порог проверяется ПРИ ЗАПУСКЕ И ПОСЛЕ КАЖДОЙ ЗАПИСИ
        /// (<see cref="RollingListener"/>). Пока проверка стояла только в
        /// <see cref="Start"/>, это был порог на момент старта, а не потолок
        /// файла — строка `A48`.
        /// </summary>
        const long MaxBytes = 2L * 1024L * 1024L;

        static string path;

        /// <summary>
        /// Полный путь к журналу — то, что показывают человеку. Пустая строка,
        /// если журнал завести не удалось.
        /// </summary>
        public static string Path
        {
            get { return path ?? string.Empty; }
        }

        /// <summary>
        /// Завести слушателя. Зовётся ОДИН раз, из <see cref="Program.Main"/>,
        /// до создания главного окна.
        /// </summary>
        /// <remarks>
        /// ⛔ Отказ здесь НЕ ПОДНИМАЕТ ОКНА и не роняет запуск: журнал — вещь
        /// служебная, и человек, у которого он не завёлся, всё равно должен
        /// получить работающую программу. Отказ виден по пустому
        /// <see cref="Path"/>: пункт меню, показывающий журнал, на нём гаснет.
        /// </remarks>
        public static void Start()
        {
            if (path != null)
            {
                return;
            }
            path = string.Empty;
            try
            {
                string dir = Package.GetInstance().UserDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, FileName);
                Rotate(file);
                RollingListener listener = new RollingListener(file);
                Trace.Listeners.Add(listener);
                Trace.AutoFlush = true;
                path = file;
                Trace.WriteLine(string.Empty);
                Trace.WriteLine(Header("BecqMoni " + Application_Version() + " запущен"));
            }
            catch (Exception)
            {
                // Каталог только для чтения, диск полон, файл занят другим
                // сеансом — журнала не будет, но программа обязана пойти
                // дальше. `path` остаётся пустым, и это ЕСТЬ признак отказа
                // (см. замечание к методу).
                path = string.Empty;
            }
        }

        /// <summary>
        /// Подрезка: разросшийся журнал уезжает в <c>.1</c>, чтобы файл не рос
        /// без предела на машине, где программа не закрывается неделями.
        /// </summary>
        /// <remarks>
        /// Зовётся из ДВУХ мест: <see cref="Start"/> — перед открытием файла, и
        /// <see cref="RollingListener"/> — в ходе работы, когда файл дорос до
        /// <see cref="MaxBytes"/>. Второй зватель обязан закрыть файл ДО вызова:
        /// <c>File.Move</c> на открытом файле отказывает.
        ///
        /// Размер проверяется здесь ещё раз, а не только у звателя: условие у
        /// обоих одно, и разъезжаться ему нельзя.
        /// </remarks>
        static void Rotate(string file)
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists || info.Length < MaxBytes)
                {
                    return;
                }
                string previous = file + ".1";
                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }
                File.Move(file, previous);
            }
            catch (Exception)
            {
                // Подрезать не вышло — пишем в тот же файл дальше. Это лучше,
                // чем остаться без журнала вовсе.
            }
        }

        /// <summary>
        /// Слушатель, который подрезает журнал НЕ ТОЛЬКО ПРИ ЗАПУСКЕ.
        /// </summary>
        /// <remarks>
        /// ⛔ Строка `A48`. Прежде <see cref="Rotate"/> звался единственный раз,
        /// из <see cref="Start"/>, и порог 2 МБ был порогом НА МОМЕНТ СТАРТА, а
        /// не потолком файла: на машине, которая не выключается неделями —
        /// ровно тот случай, ради которого подрезка и заводилась, — журнал рос
        /// без предела.
        ///
        /// Проверка стоит во <see cref="Flush"/>, а не в <see cref="WriteLine"/>:
        /// флашем заканчивается ЛЮБАЯ запись, пока включён <c>Trace.AutoFlush</c>
        /// (его ставит <see cref="Start"/>), в том числе чужая, сложенная из
        /// <c>Write</c> без перевода строки, — а подрезка на границе флаша не
        /// рвёт строку пополам. <see cref="WriteLine"/> проверяет сам только при
        /// выключенном <c>AutoFlush</c>, когда звать <c>Flush</c> больше некому.
        ///
        /// ⚠ Размер спрашивается у потока, а не складывается из длин сообщений:
        /// отступы <c>Trace.IndentLevel</c> и заголовки событий идут мимо такого
        /// счётчика, и он врал бы в меньшую сторону. Лишней ценой это не
        /// является — запись и так синхронная (<c>Trace.AutoFlush</c>).
        ///
        /// ⚠ Своя блокировка нужна, хотя <c>Trace</c> и сериализует вызовы
        /// глобальной: сериализует он их, лишь пока слушатель объявляет
        /// <c>IsThreadSafe = false</c>, а закрытие и переоткрытие файла посреди
        /// чужой записи — это потерянные строки, а не замедление.
        /// </remarks>
        sealed class RollingListener : TextWriterTraceListener
        {
            readonly string file;
            readonly object gate = new object();
            FileStream stream;
            bool suspended;

            /// <summary>
            /// ⛔ Отказ открытия бросается наружу: его ждёт <see cref="Start"/>,
            /// чтобы оставить <see cref="Path"/> пустым и погасить пункт меню.
            /// </summary>
            public RollingListener(string file)
            {
                this.file = file;
                this.Name = "becqmoni";
                this.Open();
            }

            /// <summary>
            /// ⚠ При включённом <c>Trace.AutoFlush</c> здесь НЕ проверяется
            /// ничего: <c>Trace</c> сразу за этим сам зовёт <see cref="Flush"/>,
            /// и проверка там же. Иначе на каждую строку выходил бы лишний
            /// сброс и лишний вопрос о длине файла. Измерено на 5000 строк:
            /// прежний слушатель 40 мс, этот 44–48 (+12 %), а с проверкой в
            /// обоих местах 49–59 (+26 %). Выключенный <c>AutoFlush</c> —
            /// единственный случай, когда проверять надо здесь, иначе её не
            /// сделает никто.
            /// </summary>
            public override void WriteLine(string message)
            {
                lock (this.gate)
                {
                    base.WriteLine(message);
                    if (!Trace.AutoFlush)
                    {
                        base.Flush();
                        this.RollIfBig();
                    }
                }
            }

            public override void Flush()
            {
                lock (this.gate)
                {
                    base.Flush();
                    this.RollIfBig();
                }
            }

            public override void Close()
            {
                lock (this.gate)
                {
                    base.Close();
                    this.stream = null;
                }
            }

            void Open()
            {
                this.stream = new FileStream(this.file, FileMode.Append,
                    FileAccess.Write, FileShare.Read);
                this.Writer = new StreamWriter(this.stream, new UTF8Encoding(false));
            }

            /// <summary>
            /// Дорос до порога — закрыть, подрезать, открыть заново.
            /// </summary>
            /// <remarks>
            /// ⛔ Отказ подрезки НЕ ПОВТОРЯЕТСЯ на каждой строке. Если после
            /// <see cref="Rotate"/> файл всё ещё за порогом (переименование не
            /// прошло: файл держат, каталог только для чтения), проверка гасится
            /// до конца сеанса — иначе каждая запись закрывала бы и открывала
            /// файл заново, и цена этого больше, чем разросшийся журнал. Причина
            /// при этом НАЗЫВАЕТСЯ в самом журнале: молчащий отказ — ровно то,
            /// ради чего журнал и заводился (`A15`).
            /// </remarks>
            void RollIfBig()
            {
                if (this.stream == null || this.suspended)
                {
                    return;
                }
                long length;
                try
                {
                    length = this.stream.Length;
                }
                catch (Exception)
                {
                    return;
                }
                if (length < MaxBytes)
                {
                    return;
                }
                this.CloseFile();
                Rotate(this.file);
                try
                {
                    this.Open();
                }
                catch (Exception)
                {
                    // Открыть заново не вышло (диск полон, каталог исчез).
                    // `Writer` остаётся пустым, `EnsureWriter` при пустом имени
                    // файла возвращает false, записи молча теряются — но
                    // программа работает дальше: журнал служебный.
                    this.stream = null;
                    this.suspended = true;
                    return;
                }
                if (this.stream.Length >= MaxBytes)
                {
                    this.suspended = true;
                    this.Note("подрезать журнал не удалось, он растёт дальше");
                    return;
                }
                this.Note("журнал подрезан, прежняя часть в " + FileName + ".1");
            }

            /// <summary>
            /// Отметка в НОВОМ файле — прямо в поток, мимо <c>Trace</c>: чужие
            /// слушатели к делу не относятся, а <see cref="WriteLine"/> здесь
            /// снова позвал бы проверку размера.
            /// </summary>
            void Note(string what)
            {
                try
                {
                    this.Writer.WriteLine(Header(what));
                    this.Writer.Flush();
                }
                catch (Exception)
                {
                }
            }

            /// <summary>
            /// Закрыть файл. <c>File.Move</c> на открытом файле отказывает, а
            /// <c>base.Close</c> роняет ссылку на поток, только если закрытие
            /// прошло без ошибки, — поэтому поток добивается отдельно.
            /// </summary>
            void CloseFile()
            {
                try
                {
                    base.Close();
                }
                catch (Exception)
                {
                }
                try
                {
                    if (this.stream != null)
                    {
                        this.stream.Dispose();
                    }
                }
                catch (Exception)
                {
                }
                this.Writer = null;
                this.stream = null;
            }
        }

        /// <summary>
        /// Строка-разделитель журнала: <c>=== что, время ===</c>. Ею начинается
        /// сеанс и ею же отмечается подрезка, чтобы человек, открывший журнал,
        /// видел, что начало уехало в <c>.1</c>, а не гадал.
        /// </summary>
        static string Header(string what)
        {
            return "=== " + what + " " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                " ===";
        }

        static string Application_Version()
        {
            try
            {
                return System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName().Version.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }
}
