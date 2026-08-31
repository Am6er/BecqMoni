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
                TextWriterTraceListener listener = new TextWriterTraceListener(
                    new StreamWriter(file, true, new UTF8Encoding(false)), "becqmoni");
                Trace.Listeners.Add(listener);
                Trace.AutoFlush = true;
                path = file;
                Trace.WriteLine(string.Empty);
                Trace.WriteLine("=== BecqMoni " + Application_Version() + " запущен " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    " ===");
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
