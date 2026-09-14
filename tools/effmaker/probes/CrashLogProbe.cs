// ⛔ БЕЗ ЭТОЙ СТРОКИ ПРОБА ПИШЕТ В ЖУРНАЛ В ПУСТОТУ, И МОЛЧА (`T145`).
// `Trace.WriteLine` и `Trace.Flush` помечены `[Conditional("TRACE")]`, то есть
// ВЫРЕЗАЮТСЯ КОМПИЛЯТОРОМ там, где символ `TRACE` не объявлен. У проб проекта
// нет (см. `README.md`), а `build_all.ps1` зовёт `csc` без `/d:TRACE`, — и
// маяк, которым плечо потока доказывает, что мерить было чем, не появлялся в
// журнале ВОВСЕ. Приложение это не задевает: у него символ ставит `.csproj`,
// и `Program.WriteCrash` пишет исправно. Поймано замером 04.09.2026: запись о
// падении в журнале ЕСТЬ, а маяка рядом с ней НЕТ.
#define TRACE

using BecquerelMonitor;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CrashLogProbe
{
    /// <summary>
    /// СТОРОЖ ЖУРНАЛА НЕОБРАБОТАННЫХ ИСКЛЮЧЕНИЙ (`A113`).
    ///
    /// ЗАЧЕМ. `Program.Main` заводит журнал (`A15`), но до 04.09.2026 ни
    /// <c>Application.ThreadException</c>, ни
    /// <c>AppDomain.UnhandledException</c> не были подписаны ни разу — поиск по
    /// дереву давал 0 находок. Исключение из обработчика события окна
    /// показывалось системным окном .NET и в журнал не попадало ВОВСЕ: человек,
    /// приславший жалобу, приложить к ней не мог ничего. Тот же разряд, что
    /// `A89` и `A95`, — отказ без читателя.
    ///
    /// ЧТО МЕРЯЕТСЯ:
    ///
    ///   1. ОКНО. Исключение бросается из настоящего обработчика события UI
    ///      (<c>Timer.Tick</c>) внутри настоящего цикла сообщений
    ///      <c>Application.Run</c> — то есть тем же путём, каким его получает
    ///      человек. Затем читается журнал: в записи обязаны стоять ТИП
    ///      исключения, СООБЩЕНИЕ и СТЕК.
    ///   2. ФОНОВЫЙ ПОТОК. Исключение бросается из фонового потока, и это
    ///      <c>AppDomain.UnhandledException</c>. Отменить гибель процесса такой
    ///      обработчик не может, поэтому опыт ставится ДОЧЕРНИМ ПРОЦЕССОМ (та
    ///      же проба с ключом <c>--crash-thread</c>), а журнал читается после
    ///      его смерти.
    ///   3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПЛЕЧА ПОТОКА: тот же ребёнок с ключом
    ///      <c>--no-subscribe</c> — подписку приложения НЕ ЗОВУТ, и записи
    ///      обязано НЕ БЫТЬ.
    ///   4. ОДИН ОБРАБОТЧИК. <c>Application.ThreadException</c> ЗАМЕЩАЕТ
    ///      обработчик при подписке, а не добавляет второй. Это не мелочь
    ///      устройства пробы: второй подписчик в приложении молча снял бы
    ///      запись падений в журнал.
    ///
    /// ⛔ ЖУРНАЛЫ РОДИТЕЛЯ И РЕБЁНКА РАЗВЕДЕНЫ, И ЭТО НЕ УДОБСТВО, А УСЛОВИЕ
    /// ИЗМЕРИМОСТИ (`T145`). <see cref="AppLog"/> открывает файл
    /// <c>Append</c>/<c>Write</c>/<c>FileShare.Read</c> (<c>AppLog.cs:227</c>) —
    /// пока его держит родитель, ребёнку писать НЕЧЕМ, и <c>WriteCrash</c>
    /// глотает отказ молча. Плечо тогда отказывает одинаково и когда двери нет,
    /// и когда мерить нечем. Поэтому <see cref="Spawn"/> ЗАКРЫВАЕТ журнал
    /// родителя и тут же ПРОВЕРЯЕТ, что файл отпущен, — открыв его ровно так
    /// же, как это делает приложение. Приложению эта грабля не вредит: второй
    /// запуск оно и так отвергает (<c>ERRAppAlreadyRunning</c>).
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — ЧЕТВЕРНОЙ.
    ///
    ///   * ВНЕШНИЙ: та же проба на СТАРОЙ сборке. Подписка ищется ОТРАЖЕНИЕМ,
    ///     поэтому проба собирается и с приложением, у которого её ещё нет; там
    ///     журнал обязан остаться ПУСТЫМ. Именно это делает «журнал непустой»
    ///     измерением, а не совпадением.
    ///   * ПЛЕЧА ПОТОКА (раздел 3): ТА ЖЕ сборка, ТОТ ЖЕ ребёнок, ТО ЖЕ
    ///     падение — отличие ровно одно, подписку не зовут. Запись обязана не
    ///     появиться.
    ///   * ЧЕМ МЕРЯЕМ (оба плеча потока): ребёнок пишет в журнал МАЯК — тем же
    ///     каналом <c>Trace</c>, каким пишет <c>WriteCrash</c>, — и называет
    ///     вслух свой путь журнала, доехавшую метку и судьбу подписки. Нет
    ///     маяка — журнала у ребёнка не было вовсе, и «записи нет» не значит
    ///     ничего. ⚠ Маяк НЕ несёт метки плеча нарочно: <see cref="Has"/> судит
    ///     по окну ±2000 знаков, и маяк с меткой поймал бы соседнюю запись
    ///     прошлого плеча за свою.
    ///   * ВНУТРЕННИЙ: тем же поиском в журнале ищется метка, которой никто не
    ///     бросал. Нашлась — проверка совпадает с чем попало и не меряет
    ///     ничего.
    ///
    /// ⚠ СВОЙ обработчик проба вешает НАРОЧНО, и он не подменяет собой
    /// измеряемое. Без него старая сборка вставала бы намертво на системном
    /// окне .NET (<c>ThreadExceptionDialog</c>) — известная грабля «молчащая
    /// проба = MessageBox». Обработчик пробы только считает и ничего не пишет;
    /// судится по журналу, а его наполняет исключительно приложение.
    ///
    /// ⚠ Журнал ложится РЯДОМ С ПРОБОЙ, а не в <c>%AppData%\BecqMoni</c>:
    /// <c>Package.IsStandAlone</c> у не-ClickOnce сборки истинно, и
    /// <c>UserDirectory</c> — каталог сборки. Пользовательский журнал проба не
    /// трогает.
    ///
    ///     crashlogprobe [--crash-thread --mark="…" [--no-subscribe]]
    ///
    /// Ключи в квадратных скобках — ДОЧЕРНИЙ прогон, его заводит сама проба.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        /// <summary>Метка исключения из обработчика события окна.</summary>
        const string MarkUi = "CrashLogProbe: упал обработчик события окна";

        /// <summary>Метка исключения из фонового потока.</summary>
        const string MarkBg = "CrashLogProbe: упал фоновый поток";

        /// <summary>
        /// Метка исключения из фонового потока в плече БЕЗ ПОДПИСКИ. Своя, а не
        /// <see cref="MarkBg"/>: плечи гоняются по одному журналу, и общая метка
        /// не дала бы отличить «записи нет» от «нашли запись прошлого плеча».
        /// </summary>
        const string MarkNoSub = "CrashLogProbe: упал фоновый поток без подписки";

        /// <summary>
        /// Метка, которой НИКТО НЕ БРОСАЕТ, — внутренний положительный контроль
        /// поиска по журналу.
        /// </summary>
        const string MarkNever = "CrashLogProbe: этого исключения не было";

        /// <summary>Кадр стека, по которому судится наличие стека в записи.</summary>
        const string Frame = "CrashLogProbe.Program";

        /// <summary>
        /// Начало строки-МАЯКА, которую ребёнок кладёт В ЖУРНАЛ перед падением,
        /// тем же каналом <c>Trace</c>, каким пишет <c>Program.WriteCrash</c>.
        /// Маяк отвечает на вопрос «а мерить-то было чем?»: без него «записи
        /// нет» одинаково выглядит и когда двери нет, и когда журнала у ребёнка
        /// не завелось вовсе.
        ///
        /// ⛔ Маяк НЕСЁТ ИМЯ ПЛЕЧА, А НЕ МЕТКУ ИСКЛЮЧЕНИЯ, и это не описка.
        /// <see cref="Has"/> судит запись по окну ±2000 знаков вокруг метки;
        /// маяк с меткой плеча «без подписки» попал бы в окно соседней записи
        /// плеча «с подпиской» и выдал бы её за свою — контроль отказал бы
        /// ровно там, где обязан подтверждать.
        /// </summary>
        const string Beacon = "CrashLogProbe: журнал ребёнка жив, ";

        /// <summary>Имя плеча с подпиской приложения — хвост маяка.</summary>
        const string ArmSub = "ПЛЕЧО-С-ПОДПИСКОЙ";

        /// <summary>Имя плеча без подписки — хвост маяка.</summary>
        const string ArmNoSub = "ПЛЕЧО-БЕЗ-ПОДПИСКИ";

        /// <summary>Ребёнок называет путь своего журнала — родитель сверяет.</summary>
        const string SaidLog = "ребёнок: журнал = ";

        /// <summary>
        /// Ребёнок называет ДОЕХАВШУЮ метку. ⛔ Метка идёт в командной строке и
        /// содержит пробелы: без кавычек она приезжает обрезанной по первому
        /// пробелу, запись в журнале выходит с другим текстом, и плечо
        /// отказывает по причине, к подписке отношения не имеющей.
        /// </summary>
        const string SaidMark = "ребёнок: метка = ";

        /// <summary>Ребёнок называет судьбу подписки приложения.</summary>
        const string SaidSub = "ребёнок: подписка = ";

        /// <summary>Ответы ребёнка про подписку — сверяются буквально.</summary>
        const string SubCalled = "найдена и позвана";
        const string SubHeld = "найдена, НЕ звали";
        const string SubAbsent = "в сборке НЕТ";

        /// <summary>Код возврата дочернего процесса, убитого своим же сторожем.</summary>
        const int ChildExit = 9;

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки
            // (`T60`): менеджер на пустых картах падает МОДАЛЬНЫМ окном, а
            // безоконный прогон на нём виснет навсегда.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            bool child = Array.IndexOf(args, "--crash-thread") >= 0;
            if (child)
            {
                return Child(args);
            }

            // `A263`: штатный вызов — `crashlogprobe` БЕЗ ключей; `--mark=` и
            // `--no-subscribe` понимает только ребёнок, и родитель составляет их
            // сам. Прежде родитель молча глотал ЛЮБОЙ довод, и «--no-subscrib»
            // с опечаткой выглядел как рабочий вызов, ничего не отключая.
            foreach (string arg in args)
            {
                Console.WriteLine("не знаю ключа: " + arg);
                return 2;
            }

            string log = LogFile();
            Wipe(log);

            Console.WriteLine("журнал: {0}", log);

            // ⛔ СУДИМ СВЕЖИЙ ЖУРНАЛ, И ЭТО ПРОВЕРЯЕТСЯ (`T145`). `Wipe`
            // отказывает молча — файл держат, каталог только для чтения, — и
            // тогда ВСЕ находки пробы могут быть чужими, прошлого прогона.
            // Поймано замером 04.09.2026: прогон при занятом журнале отчитался
            // «в журнале есть запись» и «маяк есть» по остаткам предыдущего.
            Say("прежний журнал убран — судим СВЕЖИЙ", Empty(log),
                "остался прежний журнал (" + Read(log).Length
                + " знаков): любая находка может быть чужой");
            string opened = StartLog();
            Say("журнал заведён", !string.IsNullOrEmpty(opened), "AppLog.Path пуст");

            MethodInfo subscribe = FindSubscription();
            Console.WriteLine("подписка приложения: {0}",
                              subscribe == null
                                  ? "НЕТ — сборка старая (положительный контроль)"
                                  : "есть, " + subscribe.DeclaringType.FullName + "." + subscribe.Name);

            Window(log, subscribe);
            Background(log, subscribe != null);
            Withheld(log, subscribe != null);
            Single();
            Control(log);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. ПАДЕНИЕ ОБРАБОТЧИКА СОБЫТИЯ ОКНА
        // ------------------------------------------------------------------

        /// <summary>
        /// Исключение бросается из <c>Timer.Tick</c> — это настоящий обработчик
        /// события UI: тик приходит оконным сообщением, и ловит его тот же
        /// <c>NativeWindow.Callback</c>, что и нажатие кнопки.
        /// </summary>
        static void Window(string log, MethodInfo subscribe)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. падение обработчика события окна ===");

            int caught = 0;
            ThreadExceptionEventHandler guard = delegate { caught++; };

            // ⛔ ПОРЯДОК ЗДЕСЬ ЗНАЧИМ, И ЭТО ИЗМЕРЕНО (раздел 3).
            // `Application.ThreadException` держит ОДИН обработчик: его `add`
            // ЗАМЕЩАЕТ прежний, а не добавляется к нему. Сторож пробы поэтому
            // вешается ПЕРВЫМ — на старой сборке он единственный и спасает
            // прогон от системного окна .NET, на новой его тут же замещает
            // подписка приложения, и это ровно то, что нужно измерить.
            Application.ThreadException += guard;
            if (subscribe != null)
            {
                subscribe.Invoke(null, null);
            }

            try
            {
                Crash();
            }
            finally
            {
                Application.ThreadException -= guard;
            }

            bool expect = subscribe != null;
            Console.WriteLine("    сторож пробы перехватил: {0}", caught);
            if (!expect)
            {
                Say("исключение дошло до Application.OnThreadException", caught == 1,
                    "перехвачено " + caught + ", ждали 1");
            }

            string text = Read(log);
            Record(text, "InvalidOperationException", MarkUi, expect, "окно");
        }

        /// <summary>
        /// Цикл сообщений, в котором падает обработчик события. Окно СОЗДАЁТСЯ,
        /// но не показывается: нужен лишь цикл, а поднимать окно пробе нельзя.
        /// </summary>
        static void Crash()
        {
            using (Form host = new Form())
            {
                IntPtr unused = host.Handle;
                GC.KeepAlive(unused);

                using (Timer boom = new Timer())
                using (Timer stop = new Timer())
                {
                    boom.Interval = 1;
                    boom.Tick += Boom;
                    stop.Interval = 900;
                    stop.Tick += delegate { Application.ExitThread(); };
                    boom.Start();
                    stop.Start();
                    Application.Run(new ApplicationContext());
                }
            }
        }

        static void Boom(object sender, EventArgs e)
        {
            ((Timer)sender).Stop();
            throw new InvalidOperationException(MarkUi);
        }

        // ------------------------------------------------------------------
        // 2. ПАДЕНИЕ ФОНОВОГО ПОТОКА
        // ------------------------------------------------------------------

        /// <summary>
        /// Фоновый поток убивает процесс, отменить это нельзя — поэтому опыт
        /// ставится дочерним процессом.
        /// </summary>
        static void Background(string log, bool expect)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. падение фонового потока ===");

            string talk;
            int code = Spawn(log, MarkBg, true, out talk);
            Say("дочерний процесс умер на своём сторожа", code == ChildExit,
                "код возврата " + code + ", ждали " + ChildExit);

            string text = Read(log);
            Measurable(log, talk, text, ArmSub, MarkBg);
            Record(text, "ApplicationException", MarkBg, expect, "фоновый поток");
        }

        /// <summary>
        /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПЛЕЧА ПОТОКА, НА ЭТОЙ ЖЕ СБОРКЕ. Тот же
        /// двоичный файл, то же приложение, то же падение, тот же журнал — но
        /// подписку приложения НЕ ЗОВУТ. Запись обязана НЕ появиться.
        ///
        /// ⛔ Одного «записи нет» тут МАЛО, и это цена, уже уплаченная однажды
        /// (`T145`): плечо, у которого журнала не завелось, молчит ровно так же,
        /// как плечо без подписки. Поэтому <see cref="Measurable"/> сперва
        /// доказывает, что мерить БЫЛО ЧЕМ — маяк ребёнка в журнале, путь
        /// журнала тот же, метка доехала целиком, — и только потом судится
        /// отсутствие записи. Отдельной строкой сверяется, что отличие плеч
        /// РОВНО ОДНО: подписка в сборке есть, но её не звали.
        /// </summary>
        static void Withheld(string log, bool subscribed)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПЛЕЧА ПОТОКА: подписку не звали ===");

            int before = Read(log).Length;
            string talk;
            int code = Spawn(log, MarkNoSub, false, out talk);
            Say("дочерний процесс умер на своём сторожа", code == ChildExit,
                "код возврата " + code + ", ждали " + ChildExit);

            string text = Read(log);
            Measurable(log, talk, text, ArmNoSub, MarkNoSub);

            string said = Said(talk, SaidSub);
            string want = subscribed ? SubHeld : SubAbsent;
            Say("отличие плеч ровно одно: подписка " + want, said == want,
                "ребёнок сказал «" + said + "»");

            bool found = Has(text, "ApplicationException", MarkNoSub, Frame);
            Say("без подписки записи НЕТ", !found,
                "запись появилась там, где подписки не звали");
            Console.WriteLine("    журнал был {0} знаков, стал {1} — вырос на маяк, но не на запись",
                              before, text.Length);
        }

        /// <summary>
        /// ⛔ ЧЕМ МЕРИЛИ. Три условия, без которых «записи нет» не значит
        /// ничего: журнал у ребёнка ТОТ ЖЕ файл, метка доехала до него ЦЕЛИКОМ,
        /// и в журнале лежит МАЯК этого плеча — то есть канал <c>Trace</c> у
        /// ребёнка работал.
        /// </summary>
        static void Measurable(string log, string talk, string text, string arm, string mark)
        {
            string his = Said(talk, SaidLog);
            Say("ребёнок завёл ТОТ ЖЕ журнал", Same(his, log),
                "у ребёнка «" + his + "», у родителя «" + log + "»");

            string got = Said(talk, SaidMark);
            Say("метка доехала до ребёнка целиком", got == mark,
                "доехало «" + got + "», посылали «" + mark + "»");

            Say("маяк ребёнка в журнале есть — мерить БЫЛО ЧЕМ",
                text.IndexOf(Beacon + arm, StringComparison.Ordinal) >= 0,
                "маяка нет: ребёнок в журнал не писал вовсе, и «записи нет» тут ничего не значит");
        }

        /// <summary>
        /// Запустить себя же дочерним процессом и дождаться смерти.
        ///
        /// ⛔ ЖУРНАЛ РОДИТЕЛЯ ЗАКРЫВАЕТСЯ ЗДЕСЬ, И ОСВОБОЖДЕНИЕ ПРОВЕРЯЕТСЯ
        /// (`T145`). Слушатель держит файл <c>Append</c>/<c>Write</c>/
        /// <c>FileShare.Read</c>, и ребёнок, просящий запись, получил бы отказ —
        /// журнала у него не было бы вовсе, а <c>WriteCrash</c> глотает такой
        /// отказ молча.
        ///
        /// ⚠ Метка идёт В КАВЫЧКАХ: она содержит пробелы, а разбор командной
        /// строки режет по ним. Без кавычек ребёнок бросал бы исключение с
        /// ОБРЕЗАННЫМ сообщением, и родитель не находил бы записи — по причине,
        /// к подписке отношения не имеющей.
        ///
        /// ⚠ Поток ошибок вычитывается ОТДЕЛЬНЫМ потоком: два последовательных
        /// <c>ReadToEnd</c> запираются друг о друга, стоит одному из каналов
        /// заполнить свой буфер.
        /// </summary>
        static int Spawn(string log, string mark, bool subscribe, out string talk)
        {
            CloseLog();
            Say("журнал родителя отпущен — ребёнку есть куда писать", Freed(log),
                "файл всё ещё держит родитель, и плечо мерило бы пустоту");

            string exe = Assembly.GetEntryAssembly().Location;
            string args = "--crash-thread --mark=\"" + mark + "\""
                          + (subscribe ? "" : " --no-subscribe");
            ProcessStartInfo start = new ProcessStartInfo(exe, args);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.WorkingDirectory = Path.GetDirectoryName(exe);

            using (Process p = Process.Start(start))
            {
                string errText = string.Empty;
                Thread stderr = new Thread(delegate ()
                {
                    errText = p.StandardError.ReadToEnd();
                });
                stderr.IsBackground = true;
                stderr.Start();

                string outText = p.StandardOutput.ReadToEnd();
                stderr.Join(30000);

                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch (Exception) { }
                    Say("дочерний процесс завершился", false, "висит дольше 30 с");
                    talk = outText + "\n" + errText;
                    return -1;
                }

                talk = outText + "\n" + errText;
                foreach (string line in talk.Split('\n'))
                {
                    if (line.Trim().Length > 0)
                    {
                        Console.WriteLine("    | " + line.TrimEnd());
                    }
                }
                return p.ExitCode;
            }
        }

        /// <summary>
        /// Дочерний прогон: завести журнал, подписать ПРИЛОЖЕНИЕ, потом свой
        /// сторож — порядок важен, обработчики <c>AppDomain</c> зовутся в
        /// порядке подписки, и приложение обязано успеть записать до выхода.
        ///
        /// ⛔ Ребёнок НАЗЫВАЕТ ВСЛУХ три вещи — путь своего журнала, доехавшую
        /// метку и судьбу подписки, — и кладёт в журнал МАЯК. Родителю этого
        /// хватает, чтобы отличить «двери нет» от «мерить нечем»: молчащий
        /// ребёнок без журнала выглядел бы точно так же, как ребёнок без
        /// подписки, и плечо мерило бы пустоту (`T145`).
        /// </summary>
        static int Child(string[] args)
        {
            string mark = MarkBg;
            bool subscribe = true;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--mark=", StringComparison.Ordinal))
                {
                    mark = arg.Substring(7);
                }
                // `A263`: второе условие было ОТДЕЛЬНЫМ `if`, а не звеном цепочки;
                // сведено в цепочку, чтобы у неё был хвост. Довод не может быть
                // разом `--mark=…` и `--no-subscribe`, поведение то же.
                else if (arg == "--no-subscribe")
                {
                    subscribe = false;
                }
                // `--crash-thread` — ЗАКОННЫЙ ключ: им `Main` и отличает ребёнка
                // от родителя (`Array.IndexOf` выше), сюда он доезжает вместе с
                // остальными. Без этой ветки хвост отказывал бы СВОЕМУ же
                // ребёнку — поймано плечом 2 приёмки `A263`, а не чтением кода.
                else if (arg == "--crash-thread")
                {
                }
                else
                {
                    Console.WriteLine("не знаю ключа: " + arg);
                    return 2;
                }
            }

            string path = StartLog();
            MethodInfo found = FindSubscription();
            if (subscribe && found != null)
            {
                found.Invoke(null, null);
            }

            Console.WriteLine(SaidLog + path);
            Console.WriteLine(SaidMark + mark);
            Console.WriteLine(SaidSub + (found == null
                                             ? SubAbsent
                                             : subscribe ? SubCalled : SubHeld));
            Console.Out.Flush();

            // МАЯК: тем же каналом, каким пишет `Program.WriteCrash`. Он
            // отвечает родителю на вопрос «мерить было чем?» — см. `Beacon`.
            Trace.WriteLine(Beacon + (subscribe ? ArmSub : ArmNoSub));
            Trace.Flush();

            AppDomain.CurrentDomain.UnhandledException += delegate
            {
                // Свой выход — чтобы не поднялось окно отчёта об ошибке
                // Windows: на нём безоконный прогон висит намертво.
                try
                {
                    Console.Error.WriteLine("сторож пробы: фоновый поток упал, выхожу кодом "
                                            + ChildExit + (subscribe ? "" : " (подписку не звали)"));
                    Console.Error.Flush();
                }
                catch (Exception)
                {
                }
                Environment.Exit(ChildExit);
            };

            Thread t = new Thread(delegate () { throw new ApplicationException(mark); });
            t.IsBackground = false;
            t.Start();
            Thread.Sleep(20000);
            Console.Error.WriteLine("сторож пробы: фоновый поток НЕ уронил процесс");
            return 8;
        }

        // ------------------------------------------------------------------
        // 4. У `Application.ThreadException` ОДИН ОБРАБОТЧИК
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ ЗАМЕР, А НЕ ПАМЯТЬ. <c>Application.ThreadException</c> выглядит
        /// как обычное событие, но его <c>add</c> ЗАМЕЩАЕТ обработчик, а не
        /// добавляет второй (он живёт полем <c>ThreadContext</c> текущего
        /// потока). Поймано 04.09.2026 на первом же прогоне этой пробы:
        /// сторож пробы был подписан ВТОРЫМ, сработал он, а обработчик
        /// приложения не сработал вовсе — и журнал остался пуст на СБОРКЕ,
        /// ГДЕ ПОДПИСКА ЕСТЬ.
        ///
        /// ⚠ Отсюда следствие и для приложения: второй, кто подпишется на это
        /// событие, МОЛЧА снимет запись падений в журнал. Сегодня подписчик
        /// один — во всём дереве.
        ///
        /// ⚠ Раздел идёт ПОСЛЕ первых трёх нарочно: он снимает подписку
        /// приложения (своей же второй подпиской) и обратно не ставит —
        /// <c>CatchUnhandled</c> повторного вызова не делает.
        /// </summary>
        static void Single()
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. у Application.ThreadException ОДИН обработчик ===");

            int first = 0;
            int second = 0;
            ThreadExceptionEventHandler a = delegate { first++; };
            ThreadExceptionEventHandler b = delegate { second++; };
            Application.ThreadException += a;
            Application.ThreadException += b;
            try
            {
                Crash();
            }
            finally
            {
                Application.ThreadException -= b;
                Application.ThreadException -= a;
            }

            Console.WriteLine("    первая подписка: {0}, вторая: {1}", first, second);
            Say("вторая подписка ЗАМЕСТИЛА первую, а не добавилась к ней",
                first == 0 && second == 1,
                "первая " + first + ", вторая " + second + " — событие ведёт себя как обычное многоадресное");
        }

        // ------------------------------------------------------------------
        // 5. ВНУТРЕННИЙ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ
        // ------------------------------------------------------------------

        /// <summary>
        /// Проверка, которая совпадает с чем попало, не меряет ничего: тем же
        /// поиском ищется метка, которой никто не бросал.
        /// </summary>
        static void Control(string log)
        {
            Console.WriteLine();
            Console.WriteLine("=== 5. положительный контроль поиска ===");
            string text = Read(log);
            bool found = Has(text, "InvalidOperationException", MarkNever, Frame);
            Say("метки, которой не бросали, в журнале НЕТ", !found,
                "поиск нашёл несуществующее — он совпадает с чем попало");
        }

        // ------------------------------------------------------------------
        // Общее
        // ------------------------------------------------------------------

        /// <summary>
        /// Запись судится по ТРЁМ признакам сразу: тип, сообщение и кадр стека.
        /// Их близость проверяется окном в 4000 знаков — иначе три случайных
        /// совпадения в разных концах файла сошли бы за одну запись.
        /// </summary>
        static void Record(string text, string type, string mark, bool expect, string what)
        {
            bool found = Has(text, type, mark, Frame);
            if (expect)
            {
                Say("в журнале есть запись (" + what + "): тип, сообщение, стек", found,
                    "записи нет; журнала " + text.Length + " знаков");
            }
            else
            {
                Say("СТАРАЯ СБОРКА: журнал пуст (" + what + ")", !found,
                    "запись нашлась там, где подписки нет");
                Console.WriteLine("    журнала {0} знаков, записи об исключении нет — так и должно быть",
                                  text.Length);
            }
        }

        static bool Has(string text, string type, string mark, string frame)
        {
            int at = text.IndexOf(mark, StringComparison.Ordinal);
            while (at >= 0)
            {
                int from = Math.Max(0, at - 2000);
                int to = Math.Min(text.Length, at + 2000);
                string window = text.Substring(from, to - from);
                if (window.IndexOf(type, StringComparison.Ordinal) >= 0 &&
                    window.IndexOf(frame, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
                at = text.IndexOf(mark, at + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>
        /// Полный путь журнала — тем же способом, каким его считает приложение:
        /// <c>Package.UserDirectory</c> плюс <c>AppLog.FileName</c>.
        /// </summary>
        static string LogFile()
        {
            string dir = Package.GetInstance().UserDirectory;
            string name = (string)AppLogType().GetField("FileName",
                BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return Path.Combine(dir, name);
        }

        static Type AppLogType()
        {
            Type t = typeof(Package).Assembly.GetType("BecquerelMonitor.AppLog");
            if (t == null)
            {
                throw new InvalidOperationException("в сборке нет BecquerelMonitor.AppLog");
            }
            return t;
        }

        static string StartLog()
        {
            Type t = AppLogType();
            t.GetMethod("Start", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
             .Invoke(null, null);
            return (string)t.GetProperty("Path",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        }

        /// <summary>
        /// Слово ребёнка по названному началу строки. Не нашлось — пустая
        /// строка, и сверка родителя на ней ОТКАЗЫВАЕТ: молчание ребёнка это
        /// тоже отказ, а не «всё хорошо».
        /// </summary>
        static string Said(string talk, string prefix)
        {
            foreach (string line in (talk ?? string.Empty).Split('\n'))
            {
                string one = line.Trim();
                if (one.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return one.Substring(prefix.Length).Trim();
                }
            }
            return string.Empty;
        }

        /// <summary>Один ли это файл. Пустая строка — НЕ тот же.</summary>
        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// ⛔ Отпущен ли журнал родителем — спрашивается ТЕМ ЖЕ ОТКРЫТИЕМ,
        /// каким его просит приложение (<c>Append</c>/<c>Write</c>/
        /// <c>FileShare.Read</c>, <c>AppLog.cs:227</c>). Пока файл держит
        /// слушатель родителя, эта просьба отказывает — ровно как у ребёнка,
        /// только у ребёнка отказ съедает <c>WriteCrash</c>, а здесь он виден.
        /// </summary>
        static bool Freed(string log)
        {
            try
            {
                using (new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        static void CloseLog()
        {
            foreach (TraceListener listener in new System.Collections.ArrayList(Trace.Listeners))
            {
                if (listener.Name == "becqmoni")
                {
                    listener.Flush();
                    listener.Close();
                    Trace.Listeners.Remove(listener);
                }
            }
        }

        /// <summary>
        /// Подписка приложения ищется ОТРАЖЕНИЕМ — затем, чтобы проба
        /// собиралась и со СТАРОЙ сборкой, где её ещё нет. Ссылка по имени
        /// сделала бы положительный контроль невозможным: он не компилировался
        /// бы.
        /// </summary>
        static MethodInfo FindSubscription()
        {
            Type t = typeof(Package).Assembly.GetType("BecquerelMonitor.Program");
            if (t == null)
            {
                return null;
            }
            return t.GetMethod("CatchUnhandled",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, Type.EmptyTypes, null);
        }

        static void Wipe(string log)
        {
            foreach (string f in new[] { log, log + ".1" })
            {
                try
                {
                    if (File.Exists(f))
                    {
                        File.Delete(f);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("не удалось убрать прежний журнал {0}: {1}", f, ex.Message);
                }
            }
        }

        /// <summary>Журнала нет или он пуст — то есть находки будут СВОИ.</summary>
        static bool Empty(string log)
        {
            try
            {
                return !File.Exists(log) || new FileInfo(log).Length == 0L;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Read(string log)
        {
            try
            {
                using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read,
                                                      FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
            catch (FileNotFoundException)
            {
                return "";
            }
            catch (DirectoryNotFoundException)
            {
                return "";
            }
        }

        static void Say(string what, bool ok, string why)
        {
            Console.WriteLine("{0} {1}{2}", ok ? "  ok " : "  НЕТ", what,
                              ok ? "" : " — " + why);
            if (!ok)
            {
                bad++;
            }
        }
    }
}
