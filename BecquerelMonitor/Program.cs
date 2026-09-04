using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{

    static class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            // Single-instance check via a named mutex. The old implementation walked the
            // process list and read process.MainModule.FileName without try/catch, which
            // throws Win32Exception for same-named processes with a different integrity
            // level or bitness and crashed before the UI appeared (and was not atomic).
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string mutexName = "Local\\BecqMoni_" + exePath.Replace('\\', '_').Replace(':', '_').ToLowerInvariant();
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(Properties.Resources.ERRAppAlreadyRunning,
                        Properties.Resources.ErrorExclamation,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Environment.CurrentDirectory = Path.GetDirectoryName(exePath);

                // ⛔ `A15`. Журнал заводится ЗДЕСЬ — до главного окна, чтобы
                // отказы при запуске (загрузка конфигурации, баз, приборов)
                // тоже в него попали. До этой правки все 110 следов
                // `Trace.WriteLine` уходили в никуда. Отказ самого журнала
                // молчалив по замыслу — см. `AppLog.Start`.
                AppLog.Start();

                // ⛔ `A113`. Подписка СРАЗУ ЗА журналом: до неё читателя у
                // необработанного исключения не было вовсе — см. `CatchUnhandled`.
                CatchUnhandled();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>
        /// ДАТЬ ЧИТАТЕЛЯ НЕОБРАБОТАННОМУ ИСКЛЮЧЕНИЮ (`A113`).
        /// </summary>
        /// <remarks>
        /// ⛔ До 04.09.2026 ни <see cref="Application.ThreadException"/>, ни
        /// <see cref="AppDomain.UnhandledException"/> не были подписаны ни разу
        /// — поиск по дереву давал 0 находок. Журнал (`A15`) при этом уже был:
        /// исключение из обработчика события окна показывалось системным окном
        /// .NET и в журнал не попадало ВООБЩЕ, то есть человек, приславший
        /// жалобу, не мог приложить к ней ничего. Тот же разряд, что `A89` и
        /// `A95`, — отказ без читателя.
        ///
        /// ⛔ ЖИЗНЬ ПРИЛОЖЕНИЯ НЕ МЕНЯЕТСЯ, И ЭТО РЕШЕНИЕ, А НЕ УПУЩЕНИЕ.
        /// Обе половины ведут себя ровно так же, как вели до подписки:
        ///
        ///   * ОКНО (<see cref="Application.ThreadException"/>) — приложение
        ///     ПРОДОЛЖАЕТ работу. Сегодня .NET сам показывает здесь
        ///     <see cref="ThreadExceptionDialog"/> с кнопками «Продолжить» и
        ///     «Выход», и «Продолжить» оставляет программу жить. Подписка
        ///     ОТМЕНЯЕТ это окно, поэтому оно поднимается здесь — ТО ЖЕ САМОЕ,
        ///     теми же кнопками и с тем же следствием у каждой. Цена другого
        ///     решения измерима: набор спектра идёт часами, и падение
        ///     обработчика перерисовки или пункта меню не должно выбрасывать
        ///     несохранённое измерение.
        ///   * ПОТОК (<see cref="AppDomain.UnhandledException"/>) — процесс
        ///     ПАДАЕТ, как и падал. Отменить это обработчик не может: с .NET 2.0
        ///     необработанное исключение фонового потока завершает процесс, а
        ///     обработчик здесь только уведомляют (<c>e.IsTerminating</c>).
        ///     Разворачивать это <c>legacyUnhandledExceptionPolicy</c> нельзя:
        ///     она меняет поведение ВСЕГО приложения и оставляет его жить в
        ///     неизвестном состоянии — молча и всюду.
        ///
        /// ⚠ <see cref="Application.SetUnhandledExceptionMode"/> НЕ ЗОВЁТСЯ
        /// нарочно. Умолчание <c>Automatic</c> отдаёт исключение окна сюда, а
        /// при <c>jitDebugging</c> в конфиге — в <see cref="AppDomain"/>; обе
        /// дороги теперь ведут в журнал, а явная установка режима сама была бы
        /// той самой тихой сменой поведения.
        ///
        /// ⛔ У <see cref="Application.ThreadException"/> ОДИН ОБРАБОТЧИК, и
        /// это измерено, а не выведено (`CrashLogProbe`, раздел 3): его
        /// <c>add</c> ЗАМЕЩАЕТ прежний обработчик, а не добавляет второй —
        /// событие живёт полем <c>ThreadContext</c> текущего потока. Значит
        /// второй, кто на него подпишется, МОЛЧА снимет запись падений в
        /// журнал. Сегодня подписчик один во всём дереве, и подписываться
        /// вторым нельзя: сюда надо ДОБАВЛЯТЬ, а не подписываться рядом.
        ///
        /// ⚠ И событие ЭТОГО ПОТОКА: цикл сообщений, заведённый на другом
        /// потоке, своего обработчика не получит. Сегодня цикл в приложении
        /// один — тот, что заводит <see cref="Main"/>.
        ///
        /// ⚠ Причина называется дверью <see cref="AppUi.Reason"/> — второго
        /// соглашения о том, как называется причина, в дереве быть не должно.
        /// Полный <c>ToString()</c> пишется следом: он несёт стек, ради
        /// которого журнал и заводился.
        ///
        /// Зовётся из <see cref="Main"/> и из сторожа `CrashLogProbe`
        /// (отражением, чтобы проба собиралась и со старой сборкой). Повторный
        /// вызов ничего не делает.
        /// </remarks>
        internal static void CatchUnhandled()
        {
            if (Program.subscribed)
            {
                return;
            }
            Program.subscribed = true;
            Application.ThreadException += Program.OnWindowException;
            AppDomain.CurrentDomain.UnhandledException += Program.OnThreadException;
        }

        static bool subscribed;

        /// <summary>
        /// Исключение, вылетевшее из обработчика события окна. Пишем в журнал и
        /// показываем ТО ЖЕ окно, что показывал .NET, — см. замечание к
        /// <see cref="CatchUnhandled"/>.
        /// </summary>
        static void OnWindowException(object sender, ThreadExceptionEventArgs e)
        {
            Program.WriteCrash("окно", e.Exception, e.Exception);

            if (!AppUi.HasWindows)
            {
                // Проба, харнесс, служба: окно повесит запуск намертво, а
                // запись в журнале уже есть. Строку в поток ошибок печатает
                // сама дверь — только без окон.
                AppUi.Note("необработанное исключение: " + AppUi.Reason(e.Exception)
                           + "; подробности в журнале " + AppUi.Where(AppLog.Path));
                return;
            }

            DialogResult result;
            using (ThreadExceptionDialog dialog = new ThreadExceptionDialog(e.Exception))
            {
                result = dialog.ShowDialog();
            }

            if (result == DialogResult.Abort)
            {
                // Ровно то, что делает .NET на «Выход»: закрыть циклы сообщений
                // и выйти. Порядок и код возврата тоже его.
                Application.Exit();
                Environment.Exit(0);
                return;
            }

            WarningException warning = e.Exception as WarningException;
            if (result == DialogResult.Yes && warning != null && warning.HelpUrl != null)
            {
                // Кнопка справки у предупреждения — тоже поведение .NET.
                Help.ShowHelp(null, warning.HelpUrl, warning.HelpTopic);
            }
        }

        /// <summary>
        /// Исключение, вылетевшее из потока без окон. Процесс после этого
        /// завершается, и отменить это здесь нечем, — но запись остаётся.
        /// </summary>
        static void OnThreadException(object sender, UnhandledExceptionEventArgs e)
        {
            Program.WriteCrash(e.IsTerminating ? "поток, процесс завершается" : "поток",
                               e.ExceptionObject as Exception, e.ExceptionObject);
        }

        /// <summary>
        /// Запись о падении. Своё исключение НЕ ВЫПУСКАЕТСЯ: обработчик,
        /// упавший сам, отнял бы у человека и то немногое, что успел записать.
        ///
        /// Сброс буфера в конце — на случай, когда журнал НЕ ЗАВЁДЕН: тогда
        /// <see cref="AppLog.Start"/> не звался, <c>Trace.AutoFlush</c> остался
        /// выключенным, а процесс после падения потока завершается сразу.
        /// ⚠ При заведённом журнале это ничего не меняет: <c>AutoFlush</c> там
        /// включён (<c>AppLog.cs:88</c>).
        ///
        /// ⛔ И оговорка, чтобы её не искали заново: буферизация НЕ была
        /// причиной того, что проба `CrashLogProbe` не находила записи о
        /// падении потока. Замер 04.09.2026: `Flush` дела не изменил, а
        /// ребёнок, запущенный ОТДЕЛЬНО, писал исправно (журнал 1493 → 2366,
        /// метка на месте). Причина в том, что журнал держит родительский
        /// процесс: <see cref="AppLog"/> открывает файл с <c>FileShare.Read</c>
        /// (<c>AppLog.cs:227</c>), и второму процессу писать в него нечем.
        /// Приложению это не вредит — второй запуск оно и так отвергает
        /// (<c>ERRAppAlreadyRunning</c>), — но пробу с дочерним процессом надо
        /// разводить по разным журналам.
        /// </summary>
        static void WriteCrash(string where, Exception exception, object raw)
        {
            try
            {
                Trace.WriteLine("=== НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ (" + where + ") "
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + ", поток " + Thread.CurrentThread.ManagedThreadId + " ===");
                if (exception != null)
                {
                    Trace.WriteLine(AppUi.Reason(exception));
                    Trace.WriteLine(exception.ToString());
                }
                else
                {
                    // Бросить можно и не-исключение (из другого языка). Тогда
                    // ни типа, ни стека нет — говорим об этом прямо, а не
                    // молчим.
                    Trace.WriteLine("брошено не исключение: "
                        + (raw == null ? "<null>" : raw.GetType().FullName + ": " + raw));
                }

                Trace.Flush();
            }
            catch (Exception)
            {
            }
        }
    }
}
