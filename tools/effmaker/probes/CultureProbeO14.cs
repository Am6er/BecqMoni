// `A238`: НА КАКОМ ЯЗЫКЕ ВЫХОДИТ СТРОКА РЕСУРСА, ПРОЧИТАННАЯ ВНЕ UI-ПОТОКА.
//
//     cultureprobeo14 [--out=<файл>]
//
// Строка `A238` утверждает: `CultureInfo.DefaultThreadCurrentUICulture` не
// выставляется нигде, `MainForm` выставляет `Thread.CurrentThread.CurrentUICulture`
// только СВОЕМУ потоку, а значит всё, что читает ресурсы из `Task.Run`, берёт
// язык ОС, а не выбранный в меню. Проба это ПРОВЕРЯЕТ, а не предполагает:
// посылка строки может оказаться уже реальности (у `Task.Run` культура течёт с
// контекстом исполнения, начиная с .NET 4.6), и тогда законный исход — переписать
// строку, а не пробу.
//
// ⛔ Окно приложения НЕ ЗАПУСКАЕТСЯ: общий порядок выставления языка берётся из
// собранной сборки ОТРАЖЕНИЕМ (`BecquerelMonitor.Program.ApplyLanguage`). Нет
// метода — значит сборка ПРЕЖНЯЯ, и проба повторяет ту единственную строку,
// которую делает `MainForm` (`Thread.CurrentThread.CurrentUICulture = …`).
//
// Устройство замера. Культура ОС на этой машине — ru-RU, поэтому ГЛАВНОЕ плечо
// идёт БЕЗ всякой подмены: настройка «» (ровно то, что кладёт в конфигурацию
// пункт меню «English»), ОС русская. Зеркальное плечо (настройка ru-RU при
// английской ОС) на русской машине без подмены не ставится — подставная ОС
// задаётся `DefaultThreadCurrentUICulture` ДО применения настройки, и это
// названо в отчёте, потому что новый порядок ту же ручку и крутит.
//
// Стартеры (кто читает ресурс) перечислены нарочно разные: у части из них
// контекст исполнения течёт с UI-потока, у части — нет, и разница между ними и
// есть предмет замера.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BecquerelMonitor;
using BecquerelMonitor.Properties;

// ⛔ БЕЗ ЭТОЙ СТРОКИ ПРОБА МЕРИТ ДРУГОЙ ПРОЦЕСС, А НЕ ПРИЛОЖЕНИЕ. С .NET 4.6
//    культура течёт по задачам вместе с контекстом исполнения, и включает это
//    поведение СОВМЕСТИМОСТНЫЙ переключатель, который платформа выбирает по
//    целевой платформе ВХОДНОЙ сборки. У приложения она объявлена
//    (.NETFramework 4.8), у пробы, собранной голым `csc`, — нет вовсе, и
//    процесс пробы жил бы по старым правилам. Тогда «дефект воспроизведён»
//    означало бы только «проба собрана иначе». Значение печатается в шапке.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8",
                                                     FrameworkDisplayName = ".NET Framework 4.8")]

static class CultureProbeO14
{
    // ⚠ Ключи взяты СТАРЫЕ и устойчивые (лежат в `Resources.resx` с чужих
    //   времён), а образцы выписаны здесь ДОСЛОВНО: проба, которая берёт
    //   ожидание из того же ресурса, что и мерит, не мерит ничего.
    const string Key1 = "ChartHeaderChannel";
    const string Key2 = "ConfirmationDialogTitle";

    const string En1 = "Channel:";
    const string Ru1 = "Канал:";
    const string En2 = "Confirmation";
    const string Ru2 = "Подтверждение";

    static readonly StringBuilder Log = new StringBuilder();
    static int failures;


    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
        }



        Say("== A238: язык строки ресурса вне UI-потока ==");
        Say("");

        Assembly app = typeof(GlobalConfigManager).Assembly;
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                          .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }

        Say("культура ОС:       InstalledUICulture=" + CultureInfo.InstalledUICulture.Name
            + ", CurrentUICulture главного потока при старте=" + Name(CultureInfo.CurrentUICulture));

        // Правила совместимости процесса пробы — те же, что у приложения, или нет.
        bool noFlow;
        bool known = AppContext.TryGetSwitch("Switch.System.Globalization.NoAsyncCurrentCulture", out noFlow);
        Say("процесс пробы:     целевая платформа входной сборки="
            + (AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "НЕ ОБЪЯВЛЕНА")
            + ", NoAsyncCurrentCulture=" + (known ? noFlow.ToString() : "по умолчанию платформы"));

        // Сателлит `ru` рядом с пробой: без него оба языка дали бы английское, и
        // проба сказала бы «сошлось», не измерив ничего.
        string ruProbe = Resources.ResourceManager.GetString(Key1, CultureInfo.GetCultureInfo("ru-RU"));
        string enProbe = Resources.ResourceManager.GetString(Key1, CultureInfo.InvariantCulture);
        Say("ресурс " + Key1 + ": инвариант=«" + enProbe + "», ru-RU=«" + ruProbe + "»");
        if (ruProbe != Ru1 || enProbe != En1)
        {
            Say("");
            Say("⛔ РАСХОЖДЕНИЕ: ресурсы не те, что выписаны в пробе (нет папки `ru` рядом "
                + "или ключ переехал). Замер невозможен.");
            Finish(outPath);
            return 2;
        }

        MethodInfo apply = FindApply(app);
        Say("общий порядок:     " + (apply == null
            ? "МЕТОДА НЕТ — сборка ПРЕЖНЯЯ (плечо «после» не ставится)"
            : apply.DeclaringType.FullName + "." + apply.Name + "(string)"));
        Say("");

        // ── Плечо 1. НАСТОЯЩЕЕ, без подмены: настройка «английский», ОС русская.
        Arm("ПЛЕЧО 1 (настоящее): настройка «» (English), ОС " + CultureInfo.InstalledUICulture.Name,
            null, "", En1, En2, apply, true);

        // ── Плечо 2. Зеркало: настройка русская, подставная ОС английская.
        Arm("ПЛЕЧО 2 (подставная ОС en-US): настройка «ru-RU»",
            "en-US", "ru-RU", Ru1, Ru2, apply, true);

        // ── Плечо 3. Настройка «OS»: метка пункта меню «(Как в системе)», а не
        //    имя культуры, и язык обязан остаться ОСным. Дефект здесь СВОЙ:
        //    `GetCultureInfo("OS")` отдаёт не отказ, а ОСЕТИНСКУЮ культуру
        //    (`os` по ISO 639), ресурсов на ней нет — и «как в системе» на
        //    русской Windows выходило английским. Прежний путь обязан это
        //    показать.
        Arm("ПЛЕЧО 3 (настройка «OS» — метка, не культура): ожидается язык ОС",
            null, "OS", Ru1, Ru2, apply, true);

        // ── Справочный замер, ПОСЛЕДНИМ и на ОТДЕЛЬНОМ потоке. Отвечает на
        //    «почему культура не течёт по `Task.Run`, хотя с .NET 4.6 обещано»:
        //    обещание касается двери `CultureInfo.CurrentUICulture`, а `MainForm`
        //    ходил в другую — `Thread.CurrentThread.CurrentUICulture`.
        //    ⛔ Отдельный поток и последнее место не украшение: присваивание
        //    `CultureInfo.CurrentUICulture` кладёт значение в контекст
        //    ИСПОЛНЕНИЯ, и оно потекло бы в задачи судимых плеч, показав
        //    правку работающей там, где она ни при чём.
        Say("");
        Say("──────────────────────────────────────────────────────────────");
        Say("СПРАВОЧНО (не судится): вторая дверь `CultureInfo.CurrentUICulture`");
        Say("──────────────────────────────────────────────────────────────");
        Thread scratch = new Thread(() =>
            Path("  настройка «» (English), ОС " + CultureInfo.InstalledUICulture.Name,
                 null, "", En1, En2, null, true));
        scratch.IsBackground = true;
        scratch.Start();
        scratch.Join(20000);

        Say("");
        Say(failures == 0
            ? "СОШЛОСЬ: строка ресурса вне UI-потока выходит по настройке во всех плечах"
            : "РАСХОЖДЕНИЙ: " + failures);
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Общий порядок выставления языка в собранной сборке. Отражением — чтобы
    /// одна и та же проба гонялась и по ПРЕЖНЕЙ сборке, где метода ещё нет.
    /// </summary>
    static MethodInfo FindApply(Assembly app)
    {
        Type program = app.GetType("BecquerelMonitor.Program");
        if (program == null) return null;
        return program.GetMethod("ApplyLanguage",
                                 BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                                 null, new[] { typeof(string) }, null);
    }

    /// <summary>
    /// Одно плечо: подставная ОС (или настоящая), настройка, ожидаемые строки.
    /// Внутри — два пути: ПРЕЖНИЙ (одна строка `MainForm`) и ОБЩИЙ (метод сборки).
    /// </summary>
    static void Arm(string title, string osStandIn, string setting,
                    string want1, string want2, MethodInfo apply, bool expectDefect)
    {
        Say("");
        Say("──────────────────────────────────────────────────────────────");
        Say(title);
        Say("──────────────────────────────────────────────────────────────");

        int old = Path("  путь ПРЕЖНИЙ (только `Thread.CurrentThread.CurrentUICulture`, MainForm:154)",
                       osStandIn, setting, want1, want2, null, false);

        // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Прежний путь ОБЯЗАН показать язык ОС хотя бы
        //    у одного стартера: не показал — либо проба не воспроизводит дорогу,
        //    либо посылка строки неверна. И то и другое — находка, а не «сошлось».
        if (expectDefect)
        {
            Say(old > 0
                ? "    [положительный контроль] дефект воспроизведён у стартеров: " + old
                : "    [положительный контроль] ⛔ ДЕФЕКТ НЕ ВОСПРОИЗВЁЛСЯ: прежний путь "
                  + "отдал язык настройки ВСЕМ стартерам");
            if (old == 0) failures++;
        }
        else if (old > 0)
        {
            Say("    ⛔ прежний путь разошёлся с ожиданием у стартеров: " + old);
            failures++;
        }

        if (apply == null)
        {
            Say("  путь ОБЩИЙ: метода нет в сборке — не мерен");
            return;
        }

        int now = Path("  путь ОБЩИЙ (`Program.ApplyLanguage` из сборки)",
                       osStandIn, setting, want1, want2, apply, false);
        if (now > 0)
        {
            Say("    ⛔ общий путь оставил язык ОС у стартеров: " + now);
            failures += now;
        }
        else
        {
            Say("    общий путь: язык настройки у ВСЕХ стартеров");
        }
    }

    /// <summary>
    /// Один путь одного плеча. Порядок важен: подставная ОС и долгоживущие
    /// стартеры заводятся ДО применения настройки — так же, как в приложении
    /// поток чтения прибора живёт с запуска.
    /// </summary>
    static int Path(string title, string osStandIn, string setting,
                    string want1, string want2, MethodInfo apply, bool viaCultureInfo)
    {
        Say("");
        Say(title);

        // 1. Мир до настройки: культура ОС (настоящая или подставная).
        CultureInfo os = osStandIn == null
            ? CultureInfo.GetCultureInfo(CultureInfo.InstalledUICulture.Name)
            : CultureInfo.GetCultureInfo(osStandIn);
        CultureInfo.DefaultThreadCurrentUICulture = osStandIn == null ? null : os;
        Thread.CurrentThread.CurrentUICulture = os;

        // 2. Долгоживущие стартеры — ЗАВОДЯТСЯ СЕЙЧАС, до настройки.
        ManualResetEventSlim gate = new ManualResetEventSlim(false);
        Reading early = null;
        Thread earlyThread = new Thread(() => { gate.Wait(); early = Take("поток, запущенный ДО настройки"); });
        earlyThread.IsBackground = true;
        earlyThread.Start();

        Reading timer = null;
        ManualResetEventSlim timerDone = new ManualResetEventSlim(false);
        Timer t = new Timer(_ => { timer = Take("Timer, заведённый ДО настройки"); timerDone.Set(); },
                            null, Timeout.Infinite, Timeout.Infinite);

        // 3. Настройка.
        string how;
        if (apply == null && viaCultureInfo)
        {
            how = "CultureInfo.CurrentUICulture = GetCultureInfo(«" + setting + "»)";
            try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(setting); }
            catch (CultureNotFoundException) { how += " → CultureNotFoundException, культура потока не тронута"; }
        }
        else if (apply == null)
        {
            how = "Thread.CurrentThread.CurrentUICulture = GetCultureInfo(«" + setting + "»)";
            try { Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(setting); }
            catch (CultureNotFoundException) { how += " → CultureNotFoundException, культура потока не тронута"; }
        }
        else
        {
            how = "Program.ApplyLanguage(«" + setting + "»)";
            try { apply.Invoke(null, new object[] { setting }); }
            catch (TargetInvocationException ex)
            {
                how += " → бросил " + ex.InnerException.GetType().Name;
                failures++;
            }
        }
        Say("    настройка: " + how);
        Say("    DefaultThreadCurrentUICulture после: "
            + (CultureInfo.DefaultThreadCurrentUICulture == null
               ? "не выставлена" : Name(CultureInfo.DefaultThreadCurrentUICulture)));

        // ⚠ ЧИСЛА — ОТДЕЛЬНАЯ РУЧКА, и здесь она воспроизводится ровно так, как
        //   её крутит `MainForm` СРАЗУ ЗА языком: клон культуры потока с
        //   подменённым разделителем дробной части. Ни прежний код, ни правка
        //   `A238` этот клон другим потокам не отдают, и колонка «1.5» ниже
        //   показывает расхождение числом, а не мнением.
        CultureInfo custom = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
        custom.NumberFormat.NumberDecimalSeparator = ".";
        Thread.CurrentThread.CurrentCulture = custom;

        // 4. Снятие показаний.
        List<Reading> rows = new List<Reading>();
        rows.Add(Take("UI-поток (сам)"));
        rows.Add(Task.Run(() => Take("Task.Run с UI-потока")).Result);
        rows.Add(RunOnThread("поток, запущенный ПОСЛЕ настройки"));
        rows.Add(InBackgroundWorker("BackgroundWorker.DoWork"));
        rows.Add(Unsafe("поток пула БЕЗ переноса контекста"));
        rows.Add(InParallel("Parallel.For (итерация на потоке пула)"));

        gate.Set();
        earlyThread.Join(5000);
        if (early != null) { early.Judged = false; rows.Add(early); }

        t.Change(0, Timeout.Infinite);
        timerDone.Wait(5000);
        t.Dispose();
        if (timer != null) { timer.Judged = false; rows.Add(timer); }

        int off = 0;
        foreach (Reading r in rows)
        {
            bool ok = r.S1 == want1 && r.S2 == want2;
            Say(string.Format(CultureInfo.InvariantCulture,
                "    {0,-45} культура={1,-11} {2}=«{3}» {4}=«{5}»  {6}   [1.5 → «{7}»]",
                r.Starter, r.Culture, Key1, r.S1, Key2, r.S2,
                ok ? "по настройке" : (r.Judged ? "НЕ ПО НАСТРОЙКЕ" : "не по настройке, НЕ СУДИТСЯ"),
                r.Num));
            if (!ok && r.Judged) off++;
        }
        return off;
    }

    sealed class Reading
    {
        public string Starter;
        public string Culture;
        public string S1;
        public string S2;
        public string Num;

        // ⚠ Два стартера не судятся, и это не поблажка правке. Поток на .NET
        //   наследует культуру СОЗДАТЕЛЯ в момент запуска; поток, заведённый ДО
        //   выбора языка (и таймер, снявший контекст тогда же), унесли культуру
        //   ОС с собой, и никакое умолчание процесса их уже не догонит —
        //   догнало бы только присваивание НА САМОМ этом потоке. Строки
        //   печатаются, потому что это предел любого общего порядка, и его
        //   надо видеть; в счёт расхождений они не идут.
        public bool Judged = true;
    }

    /// <summary>Чтение ресурса ровно так, как его читает приложение.</summary>
    static Reading Take(string starter)
    {
        return new Reading
        {
            Starter = starter,
            Culture = Name(CultureInfo.CurrentUICulture),
            S1 = Resources.ChartHeaderChannel,
            S2 = Resources.ConfirmationDialogTitle,
            Num = (1.5).ToString(),
        };
    }

    static Reading RunOnThread(string starter)
    {
        Reading r = null;
        Thread th = new Thread(() => r = Take(starter));
        th.IsBackground = true;
        th.Start();
        th.Join(5000);
        return r ?? Empty(starter);
    }

    /// <summary>
    /// `BackgroundWorker` — им заведены записи коэффициентов в прибор
    /// (`DeviceConfigForm`) и конструктор кривой; ресурсы там читаются десятками.
    /// </summary>
    static Reading InBackgroundWorker(string starter)
    {
        Reading r = null;
        ManualResetEventSlim done = new ManualResetEventSlim(false);
        System.ComponentModel.BackgroundWorker w = new System.ComponentModel.BackgroundWorker();
        w.DoWork += (s, e) => { r = Take(starter); };
        w.RunWorkerCompleted += (s, e) => done.Set();
        w.RunWorkerAsync();
        done.Wait(5000);
        return r ?? Empty(starter);
    }

    static Reading Unsafe(string starter)
    {
        Reading r = null;
        ManualResetEventSlim done = new ManualResetEventSlim(false);
        ThreadPool.UnsafeQueueUserWorkItem(_ => { r = Take(starter); done.Set(); }, null);
        done.Wait(5000);
        return r ?? Empty(starter);
    }

    static Reading InParallel(string starter)
    {
        Reading r = null;
        object gate = new object();
        int caller = Thread.CurrentThread.ManagedThreadId;
        Parallel.For(0, 64, i =>
        {
            if (Thread.CurrentThread.ManagedThreadId == caller) { Thread.Sleep(1); return; }
            lock (gate) { if (r == null) r = Take(starter); }
        });
        return r ?? Empty(starter + " (все итерации на вызывающем потоке — НЕ МЕРЕНО)");
    }

    static Reading Empty(string starter)
    {
        return new Reading { Starter = starter, Culture = "—", S1 = "—", S2 = "—", Num = "—" };
    }

    static string Name(CultureInfo ci)
    {
        return ci == null ? "—" : (ci.Name.Length == 0 ? "(инвариант)" : ci.Name);
    }

    static void Say(string line)
    {
        Console.WriteLine(line);
        Log.AppendLine(line);
    }

    static void Finish(string outPath)
    {
        if (string.IsNullOrEmpty(outPath)) return;
        try
        {
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("вывод: " + System.IO.Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine("вывод НЕ записан: " + ex.Message);
        }
    }
}
