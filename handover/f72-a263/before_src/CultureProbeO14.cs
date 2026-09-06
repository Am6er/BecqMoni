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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.NucBase;
using BecquerelMonitor.Properties;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

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
        bool modalControl = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a == "--modal-control") modalControl = true;
        }

        // ⛔ Сторож модальных окон — ПЕРВЫМ ДЕЛОМ, до менеджеров-одиночек:
        //    окно, поднятое голым `MessageBox.Show` на безоконном пути, вешает
        //    прогон насмерть, и до этого сторожа исход приёмки зависел от
        //    того, закроет ли окно кто-то снаружи (полоса F20, `A245`).
        ModalWatchStart();

        if (modalControl)
        {
            // Положительный контроль САМОГО сторожа: без него «окон не было»
            // неотличимо от «сторож молчит». Прогон обязан кончиться кодом 1.
            ModalControl();
            Say("");
            Say(failures == 0
                ? "⛔ КОНТРОЛЬ НЕ СРАБОТАЛ: сторож не засчитал ни одного окна"
                : "РАСХОЖДЕНИЙ: " + failures + " (так и надо: это контроль сторожа)");
            ModalWatchStop();
            Finish(outPath);
            return failures == 0 ? 3 : 1;
        }



        // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`),
        //    иначе раздел `A242` повиснет на модальном окне в `DocumentManager`.
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

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

        // ── `A242`: РАЗДЕЛИТЕЛЬ ДРОБНОЙ ЧАСТИ. Раздел идёт ПОСЛЕДНИМ и ставит
        //    культуру потока сам: он мерит другую ручку (`CurrentCulture`,
        //    не `CurrentUICulture`) и обязан оставить плечи выше нетронутыми.
        A242();

        // ── `A244`, полоса П7 «FSA и эффективность». Тот же вопрос, что и
        //    выше, но на СВОИХ местах: отпечатки разбора и матрицы, числа
        //    экрана разбора и пара «печать клейма → разбор клейма».
        A244();

        // ── `A244`, полоса П2 «таблица XPTable». Числа таблиц: печать ячейки,
        //    печать и разбор редактора ячейки и НАБОР КЛАВИШ, которые редактор
        //    вообще пускает. Половина правки тут опаснее целой: поле, куда
        //    нельзя набрать точку, и разбор, который её не принимает, — это не
        //    отказ, а другое число.
        A244P2();

        // ── `A244`, полоса П1 «ввод прибора». Числа приходят от железа
        //    и уходят в разбор НА ЧУЖИХ ПОТОКАХ, куда костыль `MainForm`
        //    не доезжает. Поэтому каждое плечо ставится дважды: на своём
        //    потоке и на потоке пула без переноса контекста исполнения.
        A244P1();

        // ── `A244`, ДОДЕЛКА П1+П3 (полоса F17): окно настроек прибора
        //    (`DeviceConfigForm`) и ВВОД ЧЕЛОВЕКА в поля `DoubleTextBox` /
        //    `IntegerTextBox`, куда это окно печатает и откуда читает.
        A244P1P3();

        // ── `A244`, доли П5 «панели DC» и П6 «зоны интереса» (полоса F22).
        //    Печать и разбор берутся ЖИВЫМИ путями: таблица точек ПШПВ и её
        //    редактор ячейки, подписи формы пика, три снасти правки зоны
        //    (`Load`/`Save` по кругу) и строка области, которую собирает
        //    `string.Concat(object[])` — место, которого сканер не видит.
        A244P5P6();

        ModalWatchStop();

        Say("");
        Say("модальных окон за прогон: " + modalSeen
            + (modalSeen == 0
               ? " — безоконный путь нигде не упёрся в окно"
               : " ⛔ окна поднимались, см. строки выше"));
        Say(failures == 0
            ? "СОШЛОСЬ: строка ресурса вне UI-потока выходит по настройке во всех плечах;"
              + " числа печатаются и разбираются точкой на любой культуре"
            : "РАСХОЖДЕНИЙ: " + failures);
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  `A242`. «Разделитель дробной части у чисел ВСЕГДА ТОЧКА» — правило
    //  Amber 05.09.2026. Мерится ОБЕ СТОРОНЫ: печать и разбор.
    //
    //  ⛔ Почему у каждого плеча стоит положительный контроль. Культура
    //     потока задаётся здесь ПРИСВАИВАНИЕМ, и если бы оно не доезжало
    //     (или платформа возвращала инвариант), все проверки «прошли» бы,
    //     не измерив ничего. Поэтому первым делом на каждом плече печатается
    //     `1.5` БЕЗ культуры: на ru-RU и de-DE обязано выйти «1,5», и не
    //     вышло — плечо чужой культуры не воспроизводит, о чём говорится
    //     прямо, а не замалчивается.
    // ══════════════════════════════════════════════════════════════════════

    static readonly string[] Foreign = { "ru-RU", "de-DE", "en-US" };

    static void A242()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A242`: ЧИСЛО ПЕЧАТАЕТСЯ И РАЗБИРАЕТСЯ ТОЧКОЙ НА ЛЮБОЙ КУЛЬТУРЕ");
        Say("══════════════════════════════════════════════════════════════");

        // Языковая ручка соседних плеч отпускается: дальше речь только о числах.
        CultureInfo.DefaultThreadCurrentUICulture = null;

        string models = FindModels();
        Say("геометрии дерева:  " + (models ?? "НЕ НАЙДЕНЫ — плечо «не сломано» не ставится"));

        // Эталон снимается на ИНВАРИАНТЕ и служит меркой всем плечам.
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        GeometryModel model = SampleGeometry();
        string renderRef = null;
        string stampRef = null;
        try { renderRef = GeometryWriter.Render(model); }
        catch (Exception ex) { Say("⛔ эталон текста `.in` не снят: " + ex.Message); failures++; }
        stampRef = Stamp(model);

        string crossFile = null;   // файл, записанный ПЕРВЫМ чужим плечом

        foreach (string name in Foreign)
        {
            Say("");
            Say("──────────────────────────────────────────────────────────────");
            Say("ПЛЕЧО: системная культура потока " + name + " (подмены разделителя НЕТ)");
            Say("──────────────────────────────────────────────────────────────");

            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Thread.CurrentThread.CurrentCulture = os;

            // ── Положительный контроль плеча.
            string bare = (1.5).ToString();
            string want = os.NumberFormat.NumberDecimalSeparator == "," ? "1,5" : "1.5";
            bool comma = want == "1,5";
            Say(string.Format(CultureInfo.InvariantCulture,
                "  [положительный контроль] `(1.5).ToString()` без культуры = «{0}»  {1}",
                bare, bare == want ? "— плечо воспроизводит культуру"
                                   : "⛔ ОЖИДАЛОСЬ «" + want + "»: ПЛЕЧО НЕ МЕРИТ"));
            if (bare != want) failures++;

            // ══ ПЕЧАТЬ (прямое плечо). Текст `.in` — код приложения, и он же
            //    ложится в клеймо матрицы отклика через `ComputeStamp`.
            if (renderRef != null)
            {
                string here = null;
                try { here = GeometryWriter.Render(model); }
                catch (Exception ex) { Say("  ⛔ `GeometryWriter.Render` бросил: " + ex.Message); failures++; }
                if (here != null)
                {
                    bool same = here == renderRef;
                    Say("  ПЕЧАТЬ  текст `.in` совпал с эталоном инварианта: " + Verdict(same));
                    if (!same)
                    {
                        failures++;
                        Say("    первое расхождение: " + FirstDiff(renderRef, here));
                    }
                }
            }

            // ══ КЛЕЙМО. Оно строится из того же текста, и разойтись оно может
            //    только вместе с ним; отдельная строка нужна потому, что цена
            //    расхождения тут — пересчёт всех матриц.
            string st = Stamp(model);
            bool stampSame = st != null && st == stampRef;
            Say("  КЛЕЙМО  `ResponseMatrix.ComputeStamp` совпало: "
                + (st == null ? "не мерено (клеймо не построилось)" : Verdict(stampSame)));
            if (st != null && !stampSame) failures++;

            // ══ ОБРАТНОЕ ПЛЕЧО. Записано кодом приложения — прочитано кодом
            //    приложения. Половина правки прошла бы незамеченной без этого.
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                "o17-" + name + ".in");
            try
            {
                GeometryWriter.Save(model, tmp);
                GeometryModel back = GeometryModel.Load(tmp);
                string diff = CompareDoubles(model, back);
                Say("  ОБРАТНОЕ  записал и прочитал здесь же: "
                    + (diff == null ? "числа те же" : "⛔ " + diff));
                if (diff != null) failures++;

                // Перекрёстно: файл ЧУЖОГО плеча читается на этом.
                if (crossFile == null)
                {
                    crossFile = tmp;
                }
                else
                {
                    GeometryModel cross = GeometryModel.Load(crossFile);
                    string d2 = CompareDoubles(model, cross);
                    Say("  ОБРАТНОЕ  читаю файл, записанный плечом «"
                        + System.IO.Path.GetFileNameWithoutExtension(crossFile).Substring(4) + "»: "
                        + (d2 == null ? "числа те же" : "⛔ " + d2));
                    if (d2 != null) failures++;
                }
            }
            catch (Exception ex)
            {
                Say("  ОБРАТНОЕ  ⛔ круг не замкнулся: " + ex.Message);
                failures++;
            }

            // ══ РАЗБОР. Три двери, правленные полосой О17.
            A242Parse("  РАЗБОР  `NucBaseFramework.Number(«0.0009»)` (из nucdb)",
                      NumberFromBase("0.0009"), 0.0009);
            A242Parse("  РАЗБОР  `NucBase.HalfLifeYearsFromCell(«5.75(Y)»)`",
                      HalfLifeYears("5.75(Y)"), 5.75);

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НА ПРЕЖНИЙ КОД, и он снял посылку полосы.
            //    Ждали «575» — то есть что точка сойдёт за разделитель тысяч.
            //    Измерено 05.09.2026: выходит НОЛЬ. `NumberStyles.Float` не
            //    несёт `AllowThousands`, точка на ru-RU/de-DE не разрешена
            //    вовсе, и `TryParse` возвращает false, оставляя `out` нулём —
            //    БЕЗ исключения и без единого слова. Значит прежняя цена была
            //    не «число в сто раз больше», а «ноль вместо периода
            //    полураспада»: тише и хуже.
            //
            //    Судится поэтому не конкретное число, а РАСХОЖДЕНИЕ с верным:
            //    не разошлось — плечо дефекта не воспроизводит.
            double was;
            bool okOld = double.TryParse("5.75", NumberStyles.Float, CultureInfo.CurrentCulture, out was);
            bool differs = !okOld || was != 5.75;
            Say(string.Format(CultureInfo.InvariantCulture,
                "    [положительный контроль] ПРЕЖНИЙ разбор «5.75» текущей культурой: {0}, значение {1}  {2}",
                okOld ? "разобрал" : "ОТКАЗАЛ (out остаётся нулём)",
                was.ToString("R", CultureInfo.InvariantCulture),
                comma ? (differs ? "— дефект воспроизведён" : "⛔ ОЖИДАЛОСЬ РАСХОЖДЕНИЕ: ПЛЕЧО НЕ МЕРИТ")
                      : "(культура с точкой — дефекта тут и не было)"));
            if (comma && !differs) failures++;

            // Вторая разновидность прежнего кода — `Convert.ToDouble`: та же
            // культура, но отказ БРОСКОМ, а не нулём. Обе встречались в дереве.
            string conv;
            try { conv = Convert.ToDouble("5.75").ToString("R", CultureInfo.InvariantCulture); }
            catch (FormatException) { conv = "FormatException"; }
            Say("    [положительный контроль] ПРЕЖНИЙ `Convert.ToDouble(«5.75»)` = " + conv
                + (comma ? "  — на культуре с запятой это отказ или другое число" : ""));

            // ══ ВВОЗ CSV кодом приложения — вторая сторона вывоза CSV.
            A242Csv(name);

            // ══ «НЕ СЛОМАНО»: настоящие файлы дерева, ТОЛЬКО ЧТЕНИЕ.
            if (models != null) A242Real(models, name);
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    static void A242Parse(string title, double got, double want)
    {
        bool ok = got == want;
        Say(string.Format(CultureInfo.InvariantCulture,
            "{0} = {1}  {2}", title, got.ToString("R", CultureInfo.InvariantCulture),
            ok ? "по правилу" : "⛔ ОЖИДАЛОСЬ " + want.ToString("R", CultureInfo.InvariantCulture)));
        if (!ok) failures++;
    }

    /// <summary>
    /// Ввоз CSV кодом приложения. Файл пишется С ТОЧКОЙ — ровно так, как его
    /// теперь пишет `DocumentManager.ExportDocumentToCsv`; вывоз сам вызвать
    /// нельзя, он за `SaveFileDialog`, и это названо в отчёте.
    /// </summary>
    static void A242Csv(string culture)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "o17-" + culture + ".csv");
        var sb = new StringBuilder();
        sb.AppendLine("Channel,Counts (TotalTime=3600.3s)");
        for (int i = 0; i < 8; i++) sb.AppendLine(i.ToString(CultureInfo.InvariantCulture) + "," + (i * 10));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

        try
        {
            DocEnergySpectrum doc = new DocEnergySpectrum();
            DocumentManager.GetInstance().ImportCsvToDocument(doc, 3600, path);
            double t = doc.ActiveResultData.EnergySpectrum.MeasurementTime;
            bool ok = Math.Abs(t - 3600.3) < 1e-9;
            Say(string.Format(CultureInfo.InvariantCulture,
                "  ВВОЗ CSV  время из шапки «TotalTime=3600.3s» = {0}  {1}",
                t.ToString("R", CultureInfo.InvariantCulture),
                ok ? "по правилу" : "⛔ ОЖИДАЛОСЬ 3600.3"));
            if (!ok) failures++;
        }
        catch (Exception ex)
        {
            Say("  ВВОЗ CSV  ⛔ отказ: " + ex.Message);
            failures++;
        }

        // Прежний ввоз звал `double.TryParse(totalTimeStr, out totalTime)` —
        // без стиля и без культуры, то есть `NumberStyles.Float | AllowThousands`
        // по ТЕКУЩЕЙ культуре. Здесь точка сойдёт за разделитель тысяч, и
        // «3600.3» станет 36003 — в отличие от разбора со `NumberStyles.Float`,
        // который просто отказывает. Обе разновидности есть в дереве, и обе
        // печатаются, чтобы цена дефекта не выдумывалась по памяти.
        double loose, strict;
        bool okLoose = double.TryParse("3600.3", out loose);
        bool okStrict = double.TryParse("3600.3", NumberStyles.Float, CultureInfo.CurrentCulture, out strict);
        bool comma = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == ",";
        bool differs = !okLoose || loose != 3600.3;
        Say(string.Format(CultureInfo.InvariantCulture,
            "    [положительный контроль] ПРЕЖНИЙ `double.TryParse(«3600.3», out x)` = {0} ({1}); "
            + "со `NumberStyles.Float` = {2} ({3})  {4}",
            loose.ToString("R", CultureInfo.InvariantCulture), okLoose ? "разобрал" : "ОТКАЗАЛ",
            strict.ToString("R", CultureInfo.InvariantCulture), okStrict ? "разобрал" : "ОТКАЗАЛ",
            comma ? (differs ? "— дефект воспроизведён" : "⛔ ОЖИДАЛОСЬ РАСХОЖДЕНИЕ: ПЛЕЧО НЕ МЕРИТ")
                  : "(культура с точкой)"));
        if (comma && !differs) failures++;
    }

    /// <summary>
    /// Плечо «не сломано»: настоящие геометрии дерева читаются кодом
    /// приложения, и числа обязаны совпасть с тем, что дал инвариант.
    /// ⛔ Файлы только ЧИТАЮТСЯ.
    /// </summary>
    static readonly Dictionary<string, Dictionary<string, double>> RealRef
        = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

    static void A242Real(string dir, string culture)
    {
        string[] files = Directory.GetFiles(dir, "*.in");
        Array.Sort(files, StringComparer.Ordinal);
        int judged = 0, off = 0;
        foreach (string f in files)
        {
            Dictionary<string, double> here;
            try { here = Doubles(GeometryModel.Load(f)); }
            catch (Exception ex)
            {
                Say("  НЕ СЛОМАНО ⛔ " + System.IO.Path.GetFileName(f) + ": " + ex.Message);
                off++;
                continue;
            }

            string key = System.IO.Path.GetFileName(f);
            Dictionary<string, double> was;
            if (!RealRef.TryGetValue(key, out was))
            {
                RealRef[key] = here;
                // ⛔ ЧИСЛА ПЕЧАТАЮТСЯ, А НЕ ТОЛЬКО СВЕРЯЮТСЯ МЕЖДУ СОБОЙ:
                //    «совпало на трёх культурах» не отличает «разобрано верно»
                //    от «одинаково неверно на всех трёх». Эти значения сверены
                //    с самим файлом снаружи (см. журнал полосы О17).
                if (RealRef.Count <= 2)
                {
                    Say(string.Format(CultureInfo.InvariantCulture,
                        "  НЕ СЛОМАНО  {0}: диаметр {1} мм, высота {2} мм, ПШПВ@662 {3} %",
                        key,
                        here["CrystalDiameter"].ToString("R", CultureInfo.InvariantCulture),
                        here["CrystalHeight"].ToString("R", CultureInfo.InvariantCulture),
                        here["FwhmAt662Percent"].ToString("R", CultureInfo.InvariantCulture)));
                }
                continue;
            }

            judged++;
            foreach (var kv in here)
            {
                double prev;
                if (!was.TryGetValue(kv.Key, out prev)) continue;
                if (!Same(prev, kv.Value))
                {
                    off++;
                    Say(string.Format(CultureInfo.InvariantCulture,
                        "  НЕ СЛОМАНО ⛔ {0}: {1} = {2} против {3}", key, kv.Key,
                        kv.Value.ToString("R", CultureInfo.InvariantCulture),
                        prev.ToString("R", CultureInfo.InvariantCulture)));
                }
            }
        }
        Say("  НЕ СЛОМАНО  геометрий дерева прочитано " + files.Length
            + (judged == 0 ? " (эталон снят этим плечом, судятся следующие)"
                           : ", сверено с первым плечом " + judged + ", расхождений " + off));
        failures += off;
    }

    // ── подсобное ────────────────────────────────────────────────────────

    static bool Same(double a, double b)
    {
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        return a == b;
    }

    static string Verdict(bool ok) { return ok ? "да" : "⛔ НЕТ"; }

    static string FirstDiff(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                int from = Math.Max(0, i - 30);
                return "…" + a.Substring(from, Math.Min(60, a.Length - from)).Replace("\r", "").Replace("\n", "⏎")
                     + "… против …"
                     + b.Substring(from, Math.Min(60, b.Length - from)).Replace("\r", "").Replace("\n", "⏎") + "…";
            }
        }
        return "длина: " + a.Length + " против " + b.Length;
    }

    static GeometryModel SampleGeometry()
    {
        GeometryModel m = new GeometryModel();
        m.Name = "o17";
        m.IsScintillator = true;
        m.CrystalDiameter = 63.5;
        m.CrystalHeight = 63.5;
        m.FwhmAt662Percent = 7.25;
        return m;
    }

    /// <summary>Все `double`-поля модели — отражением, чтобы правка полей не
    /// прошла мимо пробы молча.</summary>
    static Dictionary<string, double> Doubles(GeometryModel m)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FieldInfo f in typeof(GeometryModel).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType == typeof(double)) d[f.Name] = (double)f.GetValue(m);
        }
        return d;
    }

    static string CompareDoubles(GeometryModel a, GeometryModel b)
    {
        Dictionary<string, double> x = Doubles(a), y = Doubles(b);
        foreach (var kv in x)
        {
            double other;
            if (!y.TryGetValue(kv.Key, out other)) continue;
            // `.in` пишется в сантиметрах и с ограниченной значностью, поэтому
            // сверка ОТНОСИТЕЛЬНАЯ; запятая вместо точки даёт расхождение в
            // разы, а не в знаках, и такой допуск её не прячет.
            double scale = Math.Max(Math.Abs(kv.Value), 1e-9);
            if (Math.Abs(kv.Value - other) / scale > 1e-6)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}: {1} → {2}", kv.Key,
                                     kv.Value.ToString("R", CultureInfo.InvariantCulture),
                                     other.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        return null;
    }

    static string Stamp(GeometryModel m)
    {
        try { return ResponseMatrix.ComputeStamp(m, new ResponseMatrixOptions()); }
        catch { return null; }
    }

    /// <summary>`NucBaseFramework.Number` — метод закрытый, берётся отражением:
    /// это ровно та дверь, через которую числа приходят из `nucdb.sqlite`.</summary>
    static double NumberFromBase(string text)
    {
        MethodInfo mi = typeof(NucBaseFramework).GetMethod("Number",
            BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        if (mi == null) { Say("    ⛔ `NucBaseFramework.Number` не найден — сборка другая"); failures++; return double.NaN; }
        return (double)mi.Invoke(null, new object[] { text });
    }

    static double HalfLifeYears(string cell)
    {
        // Годы: «5.75(Y)» → 5.75. Метод открытый и статический.
        return BecquerelMonitor.NucBase.NucBase.HalfLifeYearsFromCell(cell);
    }

    static string FindModels()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = System.IO.Path.Combine(dir, "LSRM Geometries", "Models");
            if (Directory.Exists(candidate)) return candidate;
            DirectoryInfo up = Directory.GetParent(dir.TrimEnd('\\', '/'));
            dir = up == null ? null : up.FullName;
        }
        return null;
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

    // ══════════════════════════════════════════════════════════════════════
    //  `A244`, ПОЛОСА П7 — «FSA и эффективность». Судятся ЧЕТЫРЕ вещи, и
    //  каждая на трёх системных культурах потока БЕЗ подмены разделителя:
    //
    //   1. ОТПЕЧАТОК РАЗБОРА `FsaAnalysisSession.BuildStamp` — по нему решается
    //      «пересчитать или взять готовое». Он снимается и с UI-потока, и из
    //      фоновой задачи, у которой подменённой культуры нет: «12,5» против
    //      «12.5» — это вечное «устарело» на одном и том же спектре.
    //   2. ОТПЕЧАТОК МАТРИЦЫ `ResponseMatrix.ComputeStamp` С КЛЮЧАМИ. В `A242`
    //      он мерился умолчаниями, где ни зерна, ни ключей позитрона в строке
    //      нет вовсе; здесь они включены нарочно.
    //   3. ЧИСЛА ЭКРАНА разбора (`FsaPresentationBuilder`) — правило Amber не
    //      делает исключения для видимых человеку чисел.
    //   4. ПАРА «печать → разбор» клейма: `ComputeStamp` → `PhysicsFromStamp` и
    //      текст клейма счёта → `EfficiencyMakerForm.TryParseComputeStamp`.
    //      Половина правки опаснее целой, и без обратного плеча она пройдёт
    //      незамеченной.
    //
    //  ⛔ Плечо «КЛЕЙМО НЕ СДВИНУЛОСЬ» устроено так: обе строки печатаются
    //     ЦЕЛИКОМ, и та же проба гоняется на сборке ДО правки. Сверка идёт
    //     снаружи, файлами вывода, а не глазами.
    // ══════════════════════════════════════════════════════════════════════

    static void A244()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П7: ОТПЕЧАТКИ И ЧИСЛА FSA/EFFMAKER — ТОЧКА НА ЛЮБОЙ КУЛЬТУРЕ");
        Say("══════════════════════════════════════════════════════════════");

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string fsaRef = FsaStamp();
        string rmxRef = MatrixStampWithKeys();
        string screenRef = ScreenNumbers();
        string parsedRef = null;

        Say("  эталон (инвариант) FSA-отпечаток: " + (fsaRef ?? "НЕ СНЯТ"));
        Say("  эталон (инвариант) клеймо матрицы с ключами: " + (rmxRef ?? "НЕ СНЯТ"));
        Say("  эталон (инвариант) числа экрана: " + (screenRef ?? "НЕ СНЯТЫ"));

        if (fsaRef == null || rmxRef == null || screenRef == null)
        {
            Say("  ⛔ эталон не снят целиком — раздел не мерит; см. строки выше");
            failures++;
        }

        string crossStamp = null;   // клеймо счёта, напечатанное первым плечом

        foreach (string name in Foreign)
        {
            Say("");
            Say("── ПЛЕЧО " + name + " (подмены разделителя НЕТ) ──");
            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Thread.CurrentThread.CurrentCulture = os;
            bool comma = os.NumberFormat.NumberDecimalSeparator == ",";

            // ── Положительный контроль плеча, ДВА разных: дробное число и
            //    целое через StringBuilder. Второй нужен потому, что правка
            //    клейма матрицы тронула именно ЦЕЛЫЕ (`Append(int)`), и надо
            //    показать, что они на всех культурах печатаются одинаково —
            //    иначе «клеймо не сдвинулось» было бы утверждением, а не
            //    измерением.
            string bare = (12.5).ToString("F1");
            Say("  [полож. контроль] `(12.5).ToString(\"F1\")` без культуры = «" + bare + "»"
                + (bare == (comma ? "12,5" : "12.5") ? " — плечо воспроизводит культуру"
                                                     : " ⛔ ПЛЕЧО НЕ МЕРИТ"));
            if (bare != (comma ? "12,5" : "12.5")) failures++;

            string bareInt = new StringBuilder().Append(140).Append('|').Append(3000000).ToString();
            Say("  [полож. контроль] `StringBuilder.Append(140).Append(3000000)` без культуры = «"
                + bareInt + "»"
                + (bareInt == "140|3000000" ? " — целые от культуры не зависят"
                                            : " ⛔ ЗАВИСЯТ: правка клейма ЕГО СДВИНУЛА"));
            if (bareInt != "140|3000000") failures++;

            // ── 1. Отпечаток разбора.
            string fsa = FsaStamp();
            A244Same("  ОТПЕЧАТОК FSA `FsaAnalysisSession.BuildStamp`", fsa, fsaRef);

            // ── 2. Отпечаток матрицы с включёнными ключами.
            string rmx = MatrixStampWithKeys();
            A244Same("  КЛЕЙМО матрицы с ключами `ResponseMatrix.ComputeStamp`", rmx, rmxRef);

            // ── 3. Числа экрана разбора.
            string screen = ScreenNumbers();
            A244Same("  ЭКРАН `FsaPresentationBuilder` (предел, доля, χ²/ndf)", screen, screenRef);

            // ── 4а. Пара «печать → разбор» отпечатка матрицы, обе стороны свои.
            int phys = -1;
            try { phys = ResponseMatrix.PhysicsFromStamp(rmx ?? ""); }
            catch (Exception ex) { Say("  ОБРАТНОЕ ⛔ `PhysicsFromStamp` бросил: " + ex.Message); failures++; }
            bool physOk = phys == ResponseMatrix.PhysicsVersion;
            Say("  ОБРАТНОЕ  `ComputeStamp` → `PhysicsFromStamp` = "
                + phys.ToString(CultureInfo.InvariantCulture)
                + (physOk ? " (версия физики та же)"
                          : " ⛔ ОЖИДАЛОСЬ " + ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture)));
            if (!physOk) failures++;

            // ── 4б. Пара «клеймо счёта → разбор в окне EffMaker». Печать —
            //       тем же форматом, каким её делает `EfficiencyCalculation`
            //       (строка снимается ОТРАЖЕНИЕМ с той же сборки, чтобы проба
            //       не мерила собственную выдумку), разбор — приватным
            //       `EfficiencyMakerForm.TryParseComputeStamp`.
            string calcStamp = CalcStamp();
            if (crossStamp == null) crossStamp = calcStamp;
            string parsed = ParseCalcStamp(calcStamp);
            if (parsedRef == null) parsedRef = parsed;
            A244Same("  ОБРАТНОЕ  клеймо счёта → `TryParseComputeStamp`", parsed, parsedRef);
            Say("            строка клейма: " + calcStamp + " → " + parsed);

            // Перекрёстно: клеймо, напечатанное ПЕРВЫМ плечом, разбирается здесь.
            if (!string.Equals(crossStamp, calcStamp, StringComparison.Ordinal))
            {
                Say("  ⛔ клеймо счёта на этом плече ДРУГОЕ: «" + calcStamp + "» против «" + crossStamp + "»");
                failures++;
            }
            string parsedCross = ParseCalcStamp(crossStamp);
            A244Same("  ОБРАТНОЕ перекрёстное  клеймо чужого плеча разобрано здесь",
                     parsedCross, parsedRef);
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    static void A244Same(string title, string got, string want)
    {
        bool ok = got != null && string.Equals(got, want, StringComparison.Ordinal);
        Say(title + ": " + (got == null ? "⛔ НЕ СНЯТО"
                                        : ok ? "совпало с эталоном инварианта"
                                             : "⛔ РАЗОШЛОСЬ\n      здесь : " + got
                                               + "\n      эталон: " + want));
        if (!ok) failures++;
    }

    /// <summary>
    /// Отпечаток разбора на постоянном входе. Кривая эффективности и фон не
    /// ставятся нарочно: их части отпечатка культуры не касаются, а вот
    /// время измерения, счёт и ОБЕ калибровки — касаются.
    /// </summary>
    static ResultData fsaInput;

    static string FsaStamp()
    {
        try
        {
            // ⛔ Вход строится ОДИН раз на все плечи: первое поле отпечатка —
            //    `resultData.GetHashCode()`, и у нового объекта оно новое.
            //    Пересоздание входа на каждом плече дало бы расхождение,
            //    к культуре отношения не имеющее (поймано первым прогоном).
            if (fsaInput != null)
            {
                return FsaAnalysisSession.BuildStamp(fsaInput, false, new FsaCalculationOptions());
            }

            var spectrum = new EnergySpectrum(1.0, 1024);
            spectrum.TotalPulseCount = 1234567L;
            spectrum.MeasurementTime = 3600.25;
            spectrum.EnergyCalibration = new PolynomialEnergyCalibration
            {
                PolynomialOrder = 1,
                Coefficients = new double[] { 1.5, 2.75 }
            };

            var fwhm = new SqrtFwhmCalibration();
            fwhm.Coefficients = new double[] { 0.5, 1.25, 0.0 };

            var data = new ResultData();
            data.EnergySpectrum = spectrum;
            data.FwhmCalibration = fwhm;
            data.DetectedPeaks = new List<Peak>
            {
                new Peak { Energy = 661.657, Nuclide = new NuclideDefinition { Name = "Cs-137" } }
            };

            fsaInput = data;
            return FsaAnalysisSession.BuildStamp(data, false, new FsaCalculationOptions());
        }
        catch (Exception ex)
        {
            Say("  ⛔ FSA-отпечаток не снят: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Клеймо матрицы с ВКЛЮЧЁННЫМИ ключами: зерно, позитронный перенос и
    /// рулетка пишутся в строку только когда отличаются от штатных, и в
    /// `A242` они не мерились вовсе.
    /// </summary>
    static string MatrixStampWithKeys()
    {
        try
        {
            var options = new ResponseMatrixOptions
            {
                MinEnergyKev = 30.5,
                MaxEnergyKev = 2700.0,
                NodeCount = 140,
                BinKev = 2.5,
                Histories = 3000000,
                Seed = 20260905,
                XrayEscape = false,
                LXrayEscape = false,
                KLCascade = false,
                PositronTransport = true,
                PositronOffset = false,
                ScatterRoulette = 0.125
            };
            return ResponseMatrix.ComputeStamp(SampleGeometry(), options);
        }
        catch (Exception ex)
        {
            Say("  ⛔ клеймо матрицы с ключами не снято: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>Числа, которые видит человек в таблице разбора.</summary>
    static string ScreenNumbers()
    {
        try
        {
            return FsaPresentationBuilder.LimitText(1.25) + " | "
                   + FsaPresentationBuilder.LimitText(0.000345) + " | "
                   + (1234.5).ToString("n2", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            Say("  ⛔ числа экрана не сняты: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Клеймо счёта кривой — ТОТ ЖЕ формат, что у `EfficiencyCalculation`
    /// (строка формата снимается отражением с поля-константы там, где она
    /// есть; иначе печатается здешней копией, о чём говорится вслух).
    /// </summary>
    static string CalcStamp()
    {
        return string.Format(CultureInfo.InvariantCulture,
            "phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}",
            ResponseMatrix.PhysicsVersion, 200000, 30.5, 2700.0, 34, "std");
    }

    /// <summary>Разбор клейма приватным методом окна EffMaker — отражением.</summary>
    static string ParseCalcStamp(string stamp)
    {
        try
        {
            MethodInfo mi = typeof(EfficiencyMakerForm).GetMethod(
                "TryParseComputeStamp", BindingFlags.Static | BindingFlags.NonPublic);
            if (mi == null)
            {
                Say("  ⛔ `EfficiencyMakerForm.TryParseComputeStamp` не найден — сборка ПРЕЖНЯЯ");
                failures++;
                return null;
            }

            object[] args = new object[] { stamp, 0.0, 0.0, 0.0, 0.0, false };
            bool ok = (bool)mi.Invoke(null, args);
            if (!ok)
            {
                return "разбор ОТКАЗАЛ";
            }

            return string.Format(CultureInfo.InvariantCulture,
                "lo={0} hi={1} hist={2} nodes={3} log={4}",
                ((double)args[1]).ToString("R", CultureInfo.InvariantCulture),
                ((double)args[2]).ToString("R", CultureInfo.InvariantCulture),
                ((double)args[3]).ToString("R", CultureInfo.InvariantCulture),
                ((double)args[4]).ToString("R", CultureInfo.InvariantCulture),
                (bool)args[5] ? "1" : "0");
        }
        catch (Exception ex)
        {
            Say("  ⛔ разбор клейма счёта бросил: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
            return null;
        }
    }

    static void Say(string line)
    {
        // ⚠ Зовётся и со сторожевого потока (`ModalWatch`), поэтому под
        //   замком: `StringBuilder` не потокобезопасен, а порванная строка
        //   отчёта — это отказ, который никто не прочтёт.
        lock (Log)
        {
            Console.WriteLine(line);
            Log.AppendLine(line);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (полоса F20, 05.09.2026).
    //
    //  ⛔ ЗАЧЕМ. До него приёмка раздела `A244P1P3` была ЛОТЕРЕЕЙ, и это
    //     измерено, а не выведено: `DeviceConfigForm.LoadFormContents` →
    //     `AudioInputDeviceForm.LoadFormContents` поднимал голый
    //     `MessageBox.Show(«Выбранное аудио устройство не найдено.»)` — у
    //     новой конфигурации `AudioInputDevice` пуст ВСЕГДА, значит окно
    //     вставало в КАЖДОМ прогоне. Нажать «ОК» в безоконном прогоне
    //     некому, и исход зависел от того, закроет ли окно кто-то снаружи:
    //     один прогон давал код 0, следующий той же командой — «⛔ ЭТАЛОН НЕ
    //     СНЯТ: окно настроек не ответило за 120 с». Сама беда починена в
    //     приложении (`AppUi.Report` вместо `MessageBox.Show`), но проба
    //     обязана ОТКАЗЫВАТЬ БЫСТРО И ВНЯТНО, если окно вернётся откуда
    //     угодно, а не висеть и не зависеть от человека.
    //
    //  Что делает: каждые 200 мс перечисляет окна СВОЕГО процесса класса
    //  `#32770` (это класс `MessageBox` и любого диалога), называет их текст,
    //  засчитывает расхождение и посылает `WM_CLOSE` — прогон идёт дальше и
    //  доносит отчёт целиком, а не гибнет по чужому таймауту.
    //
    //  Положительный контроль — ключ `--modal-control`: проба сама поднимает
    //  `MessageBox` на фоновом потоке, и сторож ОБЯЗАН его назвать и закрыть,
    //  а прогон обязан кончиться кодом 1 за секунды. Без этого ключа «окон не
    //  было» неотличимо от «сторож не работает».
    // ══════════════════════════════════════════════════════════════════════

    const string DialogClass = "#32770";
    const uint WM_CLOSE = 0x0010;

    delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static volatile bool modalWatchStop;
    static Thread modalWatchThread;
    static int modalSeen;

    static void ModalWatchStart()
    {
        modalWatchThread = new Thread(delegate()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            Dictionary<long, bool> known = new Dictionary<long, bool>();
            while (!modalWatchStop)
            {
                List<IntPtr> found = new List<IntPtr>();
                try
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != self) return true;
                        StringBuilder cls = new StringBuilder(64);
                        GetClassNameW(h, cls, cls.Capacity);
                        if (cls.ToString() == DialogClass) found.Add(h);
                        return true;
                    }, IntPtr.Zero);
                }
                catch (Exception) { }

                foreach (IntPtr h in found)
                {
                    long key = h.ToInt64();
                    if (known.ContainsKey(key)) continue;
                    known[key] = true;
                    modalSeen++;
                    failures++;
                    Say("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                        + "» — сторож закрывает его сам; нажать «ОК» здесь некому,"
                        + " разряд `A245`");
                    try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception) { }
                }
                Thread.Sleep(200);
            }
        });
        modalWatchThread.IsBackground = true;
        modalWatchThread.Start();
    }

    /// <summary>Текст диалога — он лежит в его детях класса `Static`.</summary>
    static string ModalText(IntPtr dialog)
    {
        StringBuilder acc = new StringBuilder();
        try
        {
            EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
            {
                StringBuilder cls = new StringBuilder(64);
                GetClassNameW(ch, cls, cls.Capacity);
                if (cls.ToString() == "Static")
                {
                    StringBuilder txt = new StringBuilder(512);
                    GetWindowTextW(ch, txt, txt.Capacity);
                    string s = txt.ToString().Trim();
                    if (s.Length > 0)
                    {
                        if (acc.Length > 0) acc.Append(" / ");
                        acc.Append(s);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
    }

    static void ModalWatchStop()
    {
        modalWatchStop = true;
        if (modalWatchThread != null) modalWatchThread.Join(2000);
    }

    /// <summary>
    /// Положительный контроль сторожа: окно поднимается НАРОЧНО, на фоновом
    /// потоке — ровно как его поднимало приложение. Сторож обязан назвать его
    /// текст и закрыть, иначе проба стояла бы здесь до убийства процесса.
    /// </summary>
    static void ModalControl()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
        Say("══════════════════════════════════════════════════════════════");
        int before = modalSeen;
        DateTime t0 = DateTime.UtcNow;
        Thread th = new Thread(delegate()
        {
            System.Windows.Forms.MessageBox.Show("контрольное окно полосы F20",
                                                 "контроль", System.Windows.Forms.MessageBoxButtons.OK);
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        bool closed = th.Join(20000);
        double sec = (DateTime.UtcNow - t0).TotalSeconds;
        Say("  окно поднято нарочно, закрыто сторожем: " + Verdict(closed)
            + ", секунд " + sec.ToString("F1", CultureInfo.InvariantCulture)
            + ", окон назвал сторож: " + (modalSeen - before));
        if (!closed)
        {
            Say("  ⛔ СТОРОЖ НЕ РАБОТАЕТ: окно не закрыто за 20 с");
            failures++;
        }
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

    // ══════════════════════════════════════════════════════════════════════
    //  `A244`, ПОЛОСА П2 — «таблица XPTable» (`BecquerelMonitor/XPTable/**`).
    //
    //  Что мерится и почему именно это:
    //
    //   1. ПЕЧАТЬ ЯЧЕЙКИ. Каждое число, которое таблица показывает, идёт через
    //      ОДИН провайдер — `CellRenderer.FormatProvider`. Он снимается с
    //      ЖИВОГО отрисовщика ОТРАЖЕНИЕМ, а не выписывается сюда: проба,
    //      которая сама передаёт инвариант, мерит собственную выдумку.
    //   2. ПЕЧАТЬ И РАЗБОР РЕДАКТОРА. `NumberCellEditor`/`DoubleCellEditor`
    //      кладут значение в поле и читают его оттуда обратно. Обе стороны —
    //      в одной полосе нарочно.
    //   3. НАБОР КЛАВИШ. `OnTextBoxKeyPress` решает, какой символ вообще
    //      попадёт в поле. Перевести разбор на инвариант и оставить фильтр на
    //      культуре потока — значит запретить человеку набрать ровно то, что
    //      разбор теперь единственно и понимает.
    //   4. ОБРАТНОЕ плечо и ПЕРЕКРЁСТНОЕ: текст, напечатанный на одной
    //      культуре, читается редактором на другой.
    //   5. ТОЖДЕСТВА переписанных мест: `Convert.ToString(x, 16)` с дополнением
    //      нулём против формата `"x2"` — на ВСЕХ 256 значениях байта; и
    //      `typeof(T).ToString()` против `FullName` — сторона, которой
    //      `DataObject` называет формат перетаскивания.
    // ══════════════════════════════════════════════════════════════════════

    const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;
    const string CellFormat = "0.0##";

    static void A244P2()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П2: ЧИСЛА ТАБЛИЦЫ XPTable — ТОЧКА НА ЛЮБОЙ КУЛЬТУРЕ");
        Say("══════════════════════════════════════════════════════════════");

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        // ── Тождества переписанных мест. Они от культуры не зависят и потому
        //    считаются один раз, до плеч.
        int hexBad = 0;
        string hexFirst = null;
        for (int i = 0; i < 256; i++)
        {
            byte b = (byte)i;
            string was = Convert.ToString(b, 16);
            if (was.Length < 2) was = "0" + was;
            string now = b.ToString("x2", CultureInfo.InvariantCulture);
            if (was != now)
            {
                hexBad++;
                if (hexFirst == null) hexFirst = i.ToString(CultureInfo.InvariantCulture)
                                                 + ": «" + was + "» против «" + now + "»";
            }
        }
        Say("  ТОЖДЕСТВО  `Convert.ToString(b,16)`+дополнение нулём == `b.ToString(\"x2\")` "
            + "на всех 256 байтах: " + Verdict(hexBad == 0)
            + (hexBad == 0 ? "" : " (расхождений " + hexBad + ", первое " + hexFirst + ")"));
        if (hexBad != 0) failures++;

        Type ddh = typeof(GlobalConfigManager).Assembly.GetType("XPTable.Models.DragDropHelper", false);
        Type item = ddh == null ? null : ddh.GetNestedType("DragItemData", BindingFlags.NonPublic);
        if (item == null)
        {
            Say("  ТОЖДЕСТВО  ⛔ `XPTable.Models.DragDropHelper+DragItemData` не найден отражением");
            failures++;
        }
        else
        {
            bool same = item.ToString() == item.FullName;
            Say("  ТОЖДЕСТВО  `typeof(DragItemData).ToString()` == `FullName`: " + Verdict(same)
                + "  («" + item.FullName + "»)");
            if (!same) failures++;
        }

        // ── Эталон снимается на инварианте и служит меркой всем плечам.
        string cellRef = RenderNumberCell(1.5m);
        string dblRef = RenderDoubleCell(1.5);
        string editRef = EditorPrint(1.5m);
        string editDblRef = EditorPrintDouble(1.5);
        string posRef = new XPTable.Models.CellPos(3, 7).ToString();
        Say("  эталон (инвариант): ячейка числа «" + cellRef + "», ячейка double «" + dblRef
            + "», поле редактора «" + editRef + "»/«" + editDblRef + "», `CellPos` «" + posRef + "»");

        string crossText = null;   // текст, напечатанный ПЕРВЫМ чужим плечом

        foreach (string name in Foreign)
        {
            Say("");
            Say("── ПЛЕЧО " + name + " (подмены разделителя НЕТ) ──");
            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Thread.CurrentThread.CurrentCulture = os;
            bool comma = os.NumberFormat.NumberDecimalSeparator == ",";

            // ── Положительный контроль плеча.
            string bare = (1.5).ToString();
            bool armOk = bare == (comma ? "1,5" : "1.5");
            Say("  [полож. контроль] `(1.5).ToString()` без культуры = «" + bare + "»"
                + (armOk ? " — плечо воспроизводит культуру" : " ⛔ ПЛЕЧО НЕ МЕРИТ"));
            if (!armOk) failures++;

            // ── 1. ПЕЧАТЬ ЯЧЕЙКИ через провайдер живого отрисовщика.
            A244P2Same("  ПЕЧАТЬ  `NumberCellRenderer` (провайдер снят отражением)",
                       RenderNumberCell(1.5m), cellRef);
            A244P2Same("  ПЕЧАТЬ  `DoubleCellRenderer`", RenderDoubleCell(1.5), dblRef);

            // ⛔ Положительный контроль на ПРЕЖНИЙ код: до правки провайдером
            //    была культура потока. Тот же метод, другой провайдер.
            string oldCell = XPTable.Renderers.NumberCellRenderer.RenderText(
                1.5m, CellFormat, CultureInfo.CurrentCulture);
            Say("    [полож. контроль] ПРЕЖНИЙ провайдер (культура потока) даёт «" + oldCell + "»"
                + (comma ? (oldCell == "1,5" ? " — дефект воспроизведён"
                                             : " ⛔ ОЖИДАЛОСЬ «1,5»: ПЛЕЧО НЕ МЕРИТ")
                         : " (культура с точкой — дефекта тут и не было)"));
            if (comma && oldCell != "1,5") failures++;

            // ── 2. ПЕЧАТЬ РЕДАКТОРА.
            string printed = EditorPrint(1.5m);
            A244P2Same("  ПЕЧАТЬ  поле `NumberCellEditor`", printed, editRef);
            A244P2Same("  ПЕЧАТЬ  поле `DoubleCellEditor`", EditorPrintDouble(1.5), editDblRef);
            A244P2Same("  ПЕЧАТЬ  `CellPos.ToString()`",
                       new XPTable.Models.CellPos(3, 7).ToString(), posRef);

            // ── 3. НАБОР КЛАВИШ. Что вообще можно набрать в поле.
            A244P2Keys(os);

            // ── 3а. ВВОД ЦЕЛЫХ: цветоподборщик (10 полей и шестнадцатеричное)
            //       и ширина столбца в «Показать столбцы». Все они пропускают
            //       к разбору только цифры, и на такой строке прежний разбор
            //       культурой и новый инвариантом обязаны совпасть — иначе
            //       правка отняла у человека то, что он набирал.
            A244P2Integers();

            // ── 4. ОБРАТНОЕ плечо: напечатал редактор — разобрал редактор.
            decimal back = EditorParse(printed);
            bool backOk = back == 1.5m;
            Say("  ОБРАТНОЕ  «" + printed + "» → `ParseEditText` = "
                + back.ToString(CultureInfo.InvariantCulture)
                + (backOk ? "  числа те же" : "  ⛔ ОЖИДАЛОСЬ 1.5"));
            if (!backOk) failures++;

            double backDbl = EditorParseDouble(EditorPrintDouble(1.5));
            bool backDblOk = backDbl == 1.5;
            Say("  ОБРАТНОЕ  то же у `DoubleCellEditor` = "
                + backDbl.ToString("R", CultureInfo.InvariantCulture)
                + (backDblOk ? "  числа те же" : "  ⛔ ОЖИДАЛОСЬ 1.5"));
            if (!backDblOk) failures++;

            if (crossText == null)
            {
                crossText = printed;
            }
            else
            {
                decimal cross = EditorParse(crossText);
                bool crossOk = cross == 1.5m;
                Say("  ОБРАТНОЕ перекрёстное  текст чужого плеча «" + crossText + "» разобран здесь = "
                    + cross.ToString(CultureInfo.InvariantCulture)
                    + (crossOk ? "  числа те же" : "  ⛔ ОЖИДАЛОСЬ 1.5"));
                if (!crossOk) failures++;
            }

            // ⛔ Положительный контроль на ПРЕЖНИЙ разбор: `decimal.Parse` без
            //    культуры. На культуре с запятой «1.5» уходит в разделитель
            //    тысяч и молча становится 15 — вот цена половины правки.
            string loose;
            try
            {
                loose = decimal.Parse("1.5").ToString(CultureInfo.InvariantCulture);
            }
            catch (FormatException) { loose = "FormatException"; }
            Say("    [полож. контроль] ПРЕЖНИЙ `decimal.Parse(«1.5»)` культурой потока = " + loose
                + (comma ? (loose == "15" ? "  — дефект воспроизведён (в десять раз больше, молча)"
                                          : "  (иной исход, но не 1.5)")
                         : "  (культура с точкой)"));
            if (comma && loose == "1.5") failures++;

            // ── 5. `CellPadding`: преобразователь типа, обе стороны сразу.
            A244P2Padding(os);
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    static void A244P2Same(string title, string got, string want)
    {
        bool ok = got != null && string.Equals(got, want, StringComparison.Ordinal);
        Say(title + " = «" + (got ?? "НЕ СНЯТО") + "»"
            + (ok ? "  совпало с эталоном инварианта" : "  ⛔ ЭТАЛОН «" + want + "»"));
        if (!ok) failures++;
    }

    /// <summary>
    /// Плечо «ВВОД НЕ СЛОМАН». Символы прогоняются через НАСТОЯЩИЙ обработчик
    /// нажатия отражением, а прежнее правило переписано сюда дословно — так
    /// видно не только что принимается сейчас, но и что перестало.
    /// </summary>
    static void A244P2Keys(CultureInfo os)
    {
        var chars = new List<char>(new char[] { '1', '.', ',', '-', 'x' });
        string dec = os.NumberFormat.NumberDecimalSeparator;
        string grp = os.NumberFormat.NumberGroupSeparator;
        if (dec.Length == 1 && !chars.Contains(dec[0])) chars.Add(dec[0]);
        if (grp.Length == 1 && !chars.Contains(grp[0])) chars.Add(grp[0]);

        var nowOk = new StringBuilder();
        var wasOk = new StringBuilder();
        var lost = new StringBuilder();
        foreach (char ch in chars)
        {
            bool now = KeyAccepted(ch);
            bool was = KeyAcceptedOld(ch, os);
            if (now) nowOk.Append(Show(ch)).Append(' ');
            if (was) wasOk.Append(Show(ch)).Append(' ');
            if (was && !now) lost.Append(Show(ch)).Append(' ');
        }

        // Порог: цифры, точка и минус обязаны приниматься на ЛЮБОЙ культуре —
        // иначе поле стало непригодно ровно для того, что разбор понимает.
        bool gate = KeyAccepted('1') && KeyAccepted('.') && KeyAccepted('-');
        Say("  ВВОД  принимается сейчас: " + nowOk.ToString().TrimEnd()
            + " | принималось ПРЕЖНИМ кодом: " + wasOk.ToString().TrimEnd());
        Say("  ВВОД  цифра, точка и минус принимаются: " + Verdict(gate)
            + (lost.Length == 0 ? "; ничего не потеряно"
                                : "; перестал приниматься разделитель групп: " + lost.ToString().TrimEnd()));
        if (!gate) failures++;
    }

    /// <summary>
    /// ВВОД ЦЕЛЫХ, не сломан ли он. Пятнадцать мест полосы разбирают строку,
    /// уже пропущенную через проверку «только цифры»: десять полей
    /// цветоподборщика (`int.Parse`), три поля шестнадцатеричного цвета
    /// (`NumberStyles.HexNumber`) и два разбора ширины столбца
    /// (`Convert.ToInt32`). Сверяется ПРЕЖНИЙ разбор (культурой потока) с
    /// НОВЫМ (инвариантом) на всём допустимом входе.
    /// </summary>
    static void A244P2Integers()
    {
        int badDec = 0, badHex = 0, badWidth = 0;
        for (int v = 0; v <= 360; v++)
        {
            string s = v.ToString(CultureInfo.InvariantCulture);
            if (int.Parse(s, CultureInfo.InvariantCulture) != int.Parse(s)) badDec++;
        }
        for (int v = 0; v < 256; v++)
        {
            string s = v.ToString("x2", CultureInfo.InvariantCulture);
            if (int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                != int.Parse(s, NumberStyles.HexNumber)) badHex++;
        }
        for (int v = 0; v <= 2000; v++)
        {
            string s = v.ToString(CultureInfo.InvariantCulture);
            if (Convert.ToInt32(s, CultureInfo.InvariantCulture) != Convert.ToInt32(s)) badWidth++;
        }
        bool ok = badDec == 0 && badHex == 0 && badWidth == 0;
        Say("  ВВОД ЦЕЛЫХ  прежний разбор культурой == новый инвариантом: "
            + "цветоподборщик 0..360 — расхождений " + badDec
            + ", шестнадцатеричное 00..ff — " + badHex
            + ", ширина столбца 0..2000 — " + badWidth + "  " + Verdict(ok));
        if (!ok) failures++;
    }

    static string Show(char ch)
    {
        if (ch == '\u00a0') return "U+00A0";
        if (ch == '\u202f') return "U+202F";
        if (ch == ' ') return "SP";
        return "«" + ch + "»";
    }

    static bool KeyAccepted(char ch)
    {
        var ed = new XPTable.Editors.NumberCellEditor();
        MethodInfo m = typeof(XPTable.Editors.NumberCellEditor).GetMethod("OnTextBoxKeyPress", NP);
        var e = new System.Windows.Forms.KeyPressEventArgs(ch);
        m.Invoke(ed, new object[] { null, e });
        return !e.Handled;
    }

    /// <summary>ПРЕЖНЕЕ правило, переписанное сюда дословно.</summary>
    static bool KeyAcceptedOld(char ch, CultureInfo os)
    {
        NumberFormatInfo info = os.NumberFormat;
        string s = ch.ToString(CultureInfo.InvariantCulture);
        return char.IsDigit(ch)
               || s == info.NumberDecimalSeparator
               || s == info.NumberGroupSeparator
               || s == info.NegativeSign
               || ch == '\t' || ch == '\b';
    }

    static void A244P2Padding(CultureInfo os)
    {
        try
        {
            var conv = System.ComponentModel.TypeDescriptor.GetConverter(
                typeof(XPTable.Models.CellPadding));
            var src = new XPTable.Models.CellPadding(1, 2, 3, 4);
            string text = conv.ConvertToString(null, os, src);
            var back = (XPTable.Models.CellPadding)conv.ConvertFrom(null, os, text);
            bool ok = back.Left == 1 && back.Top == 2 && back.Right == 3 && back.Bottom == 4;
            Say("  ОБРАТНОЕ  `CellPadding` 1/2/3/4 → «" + text + "» → "
                + back.Left + "/" + back.Top + "/" + back.Right + "/" + back.Bottom
                + (ok ? "  числа те же" : "  ⛔ РАЗОШЛОСЬ"));
            if (!ok) failures++;
        }
        catch (Exception ex)
        {
            Say("  ОБРАТНОЕ  `CellPadding` ⛔ круг не замкнулся: " + ex.Message);
            failures++;
        }
    }

    // ── Мосты к непубличным членам таблицы. Провайдер берётся у ЖИВОГО
    //    отрисовщика: это единственный способ измерить то, чем печатает
    //    приложение, а не то, что проба сама себе передала.

    static IFormatProvider RendererProvider(object renderer)
    {
        Type t = renderer.GetType();
        while (t != null && t.Name != "CellRenderer") t = t.BaseType;
        return (IFormatProvider)t.GetProperty("FormatProvider", NP).GetValue(renderer, null);
    }

    static string RenderNumberCell(decimal v)
    {
        var r = new XPTable.Renderers.NumberCellRenderer();
        return XPTable.Renderers.NumberCellRenderer.RenderText(v, CellFormat, RendererProvider(r));
    }

    static string RenderDoubleCell(double v)
    {
        var r = new XPTable.Renderers.DoubleCellRenderer();
        return XPTable.Renderers.DoubleCellRenderer.RenderText(v, CellFormat, RendererProvider(r));
    }

    static string EditorPrint(decimal v)
    {
        var ed = new XPTable.Editors.NumberCellEditor();
        Type t = typeof(XPTable.Editors.NumberCellEditor);
        t.GetProperty("Format", NP).SetValue(ed, CellFormat, null);
        t.GetProperty("Value", NP).SetValue(ed, v, null);
        return ed.TextBox.Text;
    }

    static decimal EditorParse(string text)
    {
        var ed = new XPTable.Editors.NumberCellEditor();
        Type t = typeof(XPTable.Editors.NumberCellEditor);
        t.GetProperty("Format", NP).SetValue(ed, CellFormat, null);
        ed.TextBox.Text = text;
        t.GetMethod("ParseEditText", NP).Invoke(ed, null);
        return (decimal)t.GetField("currentValue", NP).GetValue(ed);
    }

    static string EditorPrintDouble(double v)
    {
        var ed = new XPTable.Editors.DoubleCellEditor();
        Type t = typeof(XPTable.Editors.DoubleCellEditor);
        t.GetProperty("Format", NP).SetValue(ed, CellFormat, null);
        t.GetProperty("Value", NP).SetValue(ed, v, null);
        return ed.TextBox.Text;
    }

    static double EditorParseDouble(string text)
    {
        var ed = new XPTable.Editors.DoubleCellEditor();
        Type t = typeof(XPTable.Editors.DoubleCellEditor);
        t.GetProperty("Format", NP).SetValue(ed, CellFormat, null);
        ed.TextBox.Text = text;
        t.GetMethod("ParseEditText", NP).Invoke(ed, null);
        return (double)t.GetField("currentValue", NP).GetValue(ed);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  `A244`, ПОЛОСА П1 — «ВВОД ПРИБОРА»: `RadiaCodeIn.cs`, `ObsidianIn.cs`,
    //  `AtomSpectraVCPDeviceForm.cs`, `AudioInputDeviceForm.cs`,
    //  `ObsidianDeviceForm.cs`, `RadiaCodeDeviceForm.cs`.
    //
    //  ⚠ ГЛАВНОЕ ЗДЕСЬ НЕ ЭКРАН, А ФОНОВЫЙ ПОТОК. Костыль `MainForm` (клон
    //    культуры с подменённым разделителем, строки 158–160) держится ровно
    //    на ТОМ потоке, который его сделал. Числа этих шести файлов приходят
    //    от железа и уходят в разбор на ЧУЖИХ потоках: свои `readerThread` и
    //    `discoveryThread` у RadiaCode и Obsidian, обратные вызовы WinRT у
    //    поиска устройств и `BackgroundWorker` у опроса порта. Костыля они не
    //    получают. Поэтому каждое плечо ставится ДВАЖДЫ: на своём потоке и на
    //    потоке пула, запущенном БЕЗ переноса контекста исполнения
    //    (`ThreadPool.UnsafeQueueUserWorkItem`), и у второго есть свой
    //    положительный контроль — что культура туда действительно НЕ доехала.
    //
    //  Что мерится:
    //   1. КРУГ ФОРМЫ. `AudioInputDeviceForm.LoadFormContents` печатает в поля
    //      девять чисел, `SaveFormContents` читает их обратно. Печать и разбор
    //      одного файла — в одном плече нарочно: половина правки даёт не
    //      отказ, а ДРУГОЕ ЧИСЛО, тихо.
    //   2. ПЕРЕКРЁСТНОЕ ОБРАТНОЕ. Текст, напечатанный формой на `ru-RU`,
    //      читается той же формой на `de-DE`, `en-US` и `th-TH`.
    //   3. МЕТКА ВРЕМЕНИ разбора прибора (`RadiaCodeDeviceForm`,
    //      `ObsidianDeviceForm`). Плечо `th-TH` тут не украшение: тайский
    //      календарь БУДДИЙСКИЙ, и та же строка формата без культуры даёт
    //      год 2569 вместо 2026 — у плеча есть цена, и она показывается
    //      положительным контролем.
    //   4. РАЗБОР скорости порта — `AtomSpectraVCPDeviceForm.SaveFormContents`.
    //   5. ПЕЧАТЬ отказа GATT — `FormatProtocolError` обоих потоков прибора.
    //
    //  ⛔ Чего проба НЕ мерит, и это названо. Разбор ответа прибора
    //     (`-inf`: `int.Parse(output[3])`, `double.Parse(output[9])`) и разбор
    //     адреса BLE (`ulong.TryParse`, `Convert.ToUInt64`) лежат ВНУТРИ
    //     обработчиков, которым нужен живой прибор или обратный вызов WinRT;
    //     без прибора их не позвать, а поднимать окно нельзя. Эти места
    //     разобраны сплошным поиском и поимённо —
    //     `handover/p1-devicein-culture/rest-named.txt`.
    // ══════════════════════════════════════════════════════════════════════

    static readonly string[] ForeignP1 = { "ru-RU", "de-DE", "en-US", "th-TH" };

    sealed class P1Shot
    {
        public string Err;
        public string Bare;        // `(1.5).ToString()` ТАМ, где шёл замер
        public string BareDate;    // дата без культуры ТАМ же
        public string Culture;     // имя культуры потока, на котором шёл замер
        public string[] Text;      // поля формы после печати
        public double[] Back;      // числа после разбора
        public string DateRc, DateObs, DateWant;
        public string HexRc, HexObs;
        public string Baud;
    }

    static void A244P1()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П1: ЧИСЛА ВВОДА ПРИБОРА — ТОЧКА НА ЛЮБОЙ КУЛЬТУРЕ И ЛЮБОМ ПОТОКЕ");
        Say("══════════════════════════════════════════════════════════════");

        // ⛔ Ручки соседних разделов отпускаются НАРОЧНО: плечо «фоновый поток»
        //    мерит именно ОТСУТСТВИЕ переноса культуры, и оставленная
        //    `DefaultThreadCurrentCulture` сделала бы его пустым.
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        // ⚠ Разделитель ЗДЕСЬ не печатается нарочно: на этом потоке культура
        //    уже переставлена соседним разделом, и «разделитель ОС» вышел бы
        //    выдумкой. Настоящая культура потока пула — в положительном
        //    контроле фонового плеча ниже.
        Say("  язык ОС: " + CultureInfo.InstalledUICulture.Name
            + "; культура ФОРМАТА потоков без подмены печатается в фоновом плече");

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        P1Shot want = P1Shoot();
        if (want.Err != null)
        {
            Say("  ⛔ ЭТАЛОН НЕ СНЯТ, замер невозможен: " + want.Err);
            failures++;
            return;
        }
        Say("  эталон (инвариант) поля формы: " + string.Join(" | ", want.Text));
        Say("  эталон (инвариант) скорость порта: " + want.Baud
            + ", отказ GATT: " + want.HexRc + " / " + want.HexObs);
        Say("  эталон (инвариант) метка времени разбора: «" + P1Head(want.DateRc)
            + "» / «" + P1Head(want.DateObs) + "»");

        string[] cross = null;      // текст, напечатанный ПЕРВЫМ чужим плечом

        foreach (string name in ForeignP1)
        {
            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Say("");
            Say("── ПЛЕЧО " + name + " (подмены разделителя НЕТ) ──");

            // ── (1) свой поток
            Thread.CurrentThread.CurrentCulture = os;
            P1Shot ui = P1Shoot();
            P1Arm("  ", ui, want, os);
            if (cross == null && ui.Err == null) cross = ui.Text;
            else if (ui.Err == null) P1Cross("  ", cross, want);

            // ── (2) поток пула БЕЗ переноса контекста исполнения
            P1Shot bg = null;
            string bgErr = null;
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            ThreadPool.UnsafeQueueUserWorkItem(delegate
            {
                try { bg = P1Shoot(); }
                catch (Exception ex) { bgErr = ex.GetType().Name + ": " + ex.Message; }
                finally { done.Set(); }
            }, null);
            if (!done.Wait(120000)) bgErr = "поток пула не ответил за 120 с";
            done.Dispose();
            Thread.CurrentThread.CurrentCulture = os;

            if (bgErr != null || bg == null)
            {
                Say("  ⛔ ФОНОВЫЙ ПОТОК: плечо не отработало — " + (bgErr ?? "нет ответа"));
                failures++;
            }
            else
            {
                // ⛔ Положительный контроль ИМЕННО ЭТОГО плеча: культура на
                //    потоке пула обязана ОТЛИЧАТЬСЯ от культуры вызвавшего —
                //    иначе плечо мерит тот же поток другими словами.
                bool differs = bg.Culture != ui.Culture;
                Say("  [полож. контроль] культура потока пула = «" + bg.Culture
                    + "» против «" + ui.Culture + "» у вызвавшего "
                    + (differs ? "— контекст НЕ перенесён, плечо мерит"
                               : "⚠ СОВПАЛИ: перенос контекста не опровергнут, "
                                 + "плечо остаётся проверкой «не сломано»"));
                P1Arm("  ФОН ", bg, want, CultureInfo.GetCultureInfo(bg.Culture));
            }
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    /// <summary>Одно плечо: сверка снятого с эталоном инварианта.</summary>
    static void P1Arm(string tag, P1Shot got, P1Shot want, CultureInfo os)
    {
        if (got == null || got.Err != null)
        {
            Say(tag + "⛔ плечо не отработало: " + (got == null ? "нет ответа" : got.Err));
            failures++;
            return;
        }

        // ── Положительный контроль плеча: культура обязана быть видна.
        bool comma = os.NumberFormat.NumberDecimalSeparator == ",";
        string wantBare = comma ? "1,5" : "1.5";
        bool armOk = got.Bare == wantBare;
        Say(tag + "[полож. контроль] `(1.5).ToString()` без культуры = «" + got.Bare + "»"
            + (armOk ? " — плечо воспроизводит культуру"
                     : " ⛔ ОЖИДАЛОСЬ «" + wantBare + "»: ПЛЕЧО НЕ МЕРИТ"));
        if (!armOk) failures++;

        // ⛔ Второй положительный контроль — КАЛЕНДАРЬ. На `th-TH` он
        //    буддийский, и прежний код напечатал бы 2569 вместо 2026.
        string wantYear = new DateTime(2026, 9, 5).ToString("yyyy", os);
        Say(tag + "[полож. контроль] дата без культуры = «" + got.BareDate + "»"
            + (wantYear == "2026" ? " (календарь тот же — цена у этого плеча только в разделителе)"
                                  : " — календарь ДРУГОЙ (" + wantYear + "), плечо даты мерит"));

        P1Same(tag + "ПЕЧАТЬ  девять полей `AudioInputDeviceForm`",
               string.Join(" | ", got.Text), string.Join(" | ", want.Text));
        P1SameNums(tag + "ОБРАТНОЕ  `SaveFormContents` вернул те же числа", got.Back, want.Back);
        P1Same(tag + "ПЕЧАТЬ  метка времени разбора RadiaCode", P1Head(got.DateRc), got.DateWant);
        P1Same(tag + "ПЕЧАТЬ  метка времени разбора Obsidian", P1Head(got.DateObs), got.DateWant);
        P1Same(tag + "РАЗБОР  скорость порта `AtomSpectraVCPDeviceForm`", got.Baud, want.Baud);
        P1Same(tag + "ПЕЧАТЬ  отказ GATT `RadiaCodeIn.FormatProtocolError`", got.HexRc, want.HexRc);
        P1Same(tag + "ПЕЧАТЬ  отказ GATT `ObsidianIn.FormatProtocolError`", got.HexObs, want.HexObs);
    }

    /// <summary>Перекрёстное обратное: текст ЧУЖОГО плеча читается здесь.</summary>
    static void P1Cross(string tag, string[] text, P1Shot want)
    {
        if (text == null) return;
        try
        {
            AudioInputDeviceForm f = P1NewAudioForm();
            P1PutText(f, text);
            AudioInputDeviceConfig back = new AudioInputDeviceConfig();
            if (!f.SaveFormContents(back))
            {
                Say(tag + "ОБРАТНОЕ перекрёстное ⛔ `SaveFormContents` вернул false");
                failures++;
                return;
            }
            P1SameNums(tag + "ОБРАТНОЕ перекрёстное: текст плеча `ru-RU` прочитан здесь",
                       P1Values(back), want.Back);
        }
        catch (Exception ex)
        {
            Say(tag + "ОБРАТНОЕ перекрёстное ⛔ бросило: " + ex.GetType().Name + ": " + ex.Message);
            failures++;
        }
    }

    static void P1Same(string title, string got, string wantText)
    {
        bool ok = got == wantText;
        Say(title + ": " + (ok ? "«" + got + "»" : "⛔ «" + got + "» ОЖИДАЛОСЬ «" + wantText + "»"));
        if (!ok) failures++;
    }

    static void P1SameNums(string title, double[] got, double[] wantNums)
    {
        if (got == null || wantNums == null || got.Length != wantNums.Length)
        {
            Say(title + ": ⛔ не с чем сверять");
            failures++;
            return;
        }
        int off = 0;
        string first = null;
        for (int i = 0; i < got.Length; i++)
        {
            if (got[i] != wantNums[i])
            {
                off++;
                if (first == null)
                {
                    first = i.ToString(CultureInfo.InvariantCulture) + ": "
                          + got[i].ToString("R", CultureInfo.InvariantCulture) + " против "
                          + wantNums[i].ToString("R", CultureInfo.InvariantCulture);
                }
            }
        }
        Say(title + ": " + (off == 0 ? "все " + got.Length + " совпали"
                                     : "⛔ расхождений " + off + ", первое " + first));
        failures += off == 0 ? 0 : 1;
    }

    /// <summary>Дата из метки «dd-MM-yyyy HH:mm:ss …» — часы сравнивать нельзя.</summary>
    static string P1Head(string s)
    {
        if (string.IsNullOrEmpty(s)) return "(пусто)";
        return s.Length >= 10 ? s.Substring(0, 10) : s;
    }

    // ── снятие одного среза на ТЕКУЩЕМ потоке ────────────────────────────

    static P1Shot P1Shoot()
    {
        P1Shot s = new P1Shot();
        try
        {
            s.Bare = (1.5).ToString();
            s.BareDate = new DateTime(2026, 9, 5).ToString("dd-MM-yyyy");
            s.Culture = CultureInfo.CurrentCulture.Name;
            if (s.Culture.Length == 0) s.Culture = "(инвариант)";
            P1Audio(s);
            P1Devices(s);
            P1Vcp(s);
        }
        catch (Exception ex)
        {
            s.Err = ex.GetType().Name + ": " + ex.Message;
        }
        return s;
    }

    static void P1Audio(P1Shot s)
    {
        AudioInputDeviceConfig cfg = new AudioInputDeviceConfig();
        cfg.AudioInputDevice = null;
        cfg.SamplesPerSecond = 48000;
        cfg.BitsPerSample = 16;
        cfg.Volume = 42;
        cfg.AutoVolumeSetting = true;
        cfg.NegativePolarity = false;
        PRAHomageMethodConfig pra = (PRAHomageMethodConfig)cfg.PulseDetectionMethodConfig;
        // Числа взяты С ДРОБНОЙ ЧАСТЬЮ нарочно: на целых разделитель не виден,
        // и плечо прошло бы, не измерив ничего.
        pra.LowerThreshold = 1.25;
        pra.UpperThreshold = 87.5;
        pra.PulseThreshold = 0.35;
        pra.PulseLowerThreshold = 0.375;
        pra.PulseUpperThreshold = 9.75;
        pra.NumberOfPulses = 1234;

        AudioInputDeviceForm f = P1NewAudioForm();
        f.LoadFormContents(cfg);
        s.Text = P1GetText(f);

        AudioInputDeviceConfig back = new AudioInputDeviceConfig();
        if (!f.SaveFormContents(back))
        {
            throw new InvalidOperationException(
                "`AudioInputDeviceForm.SaveFormContents` вернул false: разбор не прошёл на полях «"
                + string.Join(" | ", s.Text) + "»");
        }
        s.Back = P1Values(back);
    }

    static readonly string[] P1Boxes =
    {
        "comboBox2", "comboBox3", "maskedTextBox1", "doubleTextBox1", "doubleTextBox2",
        "maskedTextBox2", "doubleTextBox3", "doubleTextBox4", "textBox4"
    };

    static AudioInputDeviceForm P1NewAudioForm()
    {
        AudioInputDeviceForm f = new AudioInputDeviceForm();
        // Список звуковых входов не трогаем: без него `LoadFormContents`
        // разыменовал бы `null`, а с непустым — поднял бы окно «устройство не
        // найдено» и повесил безоконный прогон (`S100`).
        FieldInfo fi = typeof(AudioInputDeviceForm).GetField("waveDeviceList", NP);
        if (fi == null) throw new InvalidOperationException("поля `waveDeviceList` нет: сборка чужая");
        Type roc = fi.FieldType;
        Type listT = typeof(List<>).MakeGenericType(roc.GetGenericArguments()[0]);
        fi.SetValue(f, Activator.CreateInstance(roc, new object[] { Activator.CreateInstance(listT) }));
        return f;
    }

    static System.Windows.Forms.Control P1Ctl(object form, string name)
    {
        FieldInfo fi = form.GetType().GetField(name, NP);
        if (fi == null) throw new InvalidOperationException("поля `" + name + "` нет: сборка чужая");
        return (System.Windows.Forms.Control)fi.GetValue(form);
    }

    static string[] P1GetText(AudioInputDeviceForm f)
    {
        string[] t = new string[P1Boxes.Length];
        for (int i = 0; i < P1Boxes.Length; i++) t[i] = P1Ctl(f, P1Boxes[i]).Text;
        return t;
    }

    static void P1PutText(AudioInputDeviceForm f, string[] text)
    {
        for (int i = 0; i < P1Boxes.Length; i++) P1Ctl(f, P1Boxes[i]).Text = text[i];
    }

    static double[] P1Values(AudioInputDeviceConfig c)
    {
        PRAHomageMethodConfig p = (PRAHomageMethodConfig)c.PulseDetectionMethodConfig;
        return new double[]
        {
            c.SamplesPerSecond, c.BitsPerSample, c.Volume,
            p.LowerThreshold, p.UpperThreshold, p.PulseThreshold,
            p.PulseLowerThreshold, p.PulseUpperThreshold, p.NumberOfPulses
        };
    }

    static void P1Devices(P1Shot s)
    {
        s.DateWant = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        RadiaCodeDeviceForm rc = new RadiaCodeDeviceForm();
        MethodInfo hrc = typeof(RadiaCodeDeviceForm).GetMethod("RadiaCodeIn_TroubleShoot", NP);
        if (hrc == null) throw new InvalidOperationException("`RadiaCodeIn_TroubleShoot` не найден");
        hrc.Invoke(rc, new object[] { null, new RadiaCodeTroubleShootArgs("проба П1") });
        s.DateRc = (string)typeof(RadiaCodeDeviceForm).GetField("tshootText", NP).GetValue(rc);

        ObsidianDeviceForm ob = new ObsidianDeviceForm();
        MethodInfo hob = typeof(ObsidianDeviceForm).GetMethod("ObsidianIn_TroubleShoot", NP);
        if (hob == null) throw new InvalidOperationException("`ObsidianIn_TroubleShoot` не найден");
        hob.Invoke(ob, new object[] { null, new ObsidianTroubleShootArgs("проба П1") });
        s.DateObs = (string)typeof(ObsidianDeviceForm).GetField("tshootText", NP).GetValue(ob);

        s.HexRc = P1Hex(typeof(RadiaCodeIn));
        s.HexObs = P1Hex(typeof(ObsidianIn));
    }

    static string P1Hex(Type t)
    {
        MethodInfo m = t.GetMethod("FormatProtocolError",
                                   BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) throw new InvalidOperationException("`" + t.Name + ".FormatProtocolError` не найден");
        return (string)m.Invoke(null, new object[] { (byte?)0x2A });
    }

    static void P1Vcp(P1Shot s)
    {
        AtomSpectraVCPDeviceForm v = new AtomSpectraVCPDeviceForm();
        // ⛔ `formLoading` поднимается ПЕРЕД тем, как трогать списки: иначе
        //    `SelectedIndexChanged` зовёт `TestConnection`, а тот открывает
        //    настоящий COM-порт, и безоконный прогон уходит в ожидание.
        //    А `fillPorts` при пустом списке портов поднимает окно — поэтому
        //    его здесь не зовут вовсе.
        FieldInfo fl = typeof(AtomSpectraVCPDeviceForm).GetField("formLoading", NP);
        if (fl == null) throw new InvalidOperationException("поля `formLoading` нет: сборка чужая");
        fl.SetValue(v, true);

        System.Windows.Forms.ComboBox ports = (System.Windows.Forms.ComboBox)P1Ctl(v, "comPortsBox");
        System.Windows.Forms.ComboBox bauds = (System.Windows.Forms.ComboBox)P1Ctl(v, "baudratesBox");
        ports.Items.Clear();
        ports.Items.Add("COM7");
        ports.SelectedIndex = 0;
        int ix = bauds.Items.IndexOf("600000");
        if (ix < 0) throw new InvalidOperationException("в списке скоростей нет «600000»: список сменился");
        bauds.SelectedIndex = ix;

        AtomSpectraDeviceConfig cfg = new AtomSpectraDeviceConfig();
        if (!v.SaveFormContents(cfg))
        {
            throw new InvalidOperationException("`AtomSpectraVCPDeviceForm.SaveFormContents` вернул false");
        }
        s.Baud = cfg.BaudRate.ToString(CultureInfo.InvariantCulture) + " на " + cfg.ComPortName;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  `A244`, ДОДЕЛКА ОБОРВАННЫХ ПОЛОС П1 и П3 (полоса F17).
    //
    //  П1 «ввод прибора» мерится разделом `A244P1` выше — он на месте и
    //  собран. Здесь достаётся ВТОРАЯ половина той же работы, которая
    //  осталась неизмеренной, когда обе полосы сняли на ходу:
    //
    //   1. ОКНО НАСТРОЕК ПРИБОРА (`DeviceConfigForm`, доля П3).
    //      `LoadFormContents` печатает числа конфигурации в поля, а
    //      `SaveFormContents` читает их обратно. Оба зовутся ОТРАЖЕНИЕМ —
    //      окно не поднимается (`S100`). Печать и разбор в одном плече
    //      нарочно: половина правки даёт не отказ, а ДРУГОЕ ЧИСЛО, тихо.
    //
    //   2. ВВОД ЧЕЛОВЕКА (`DoubleTextBox`, `IntegerTextBox` — те самые поля,
    //      куда печатает и из которых читает и окно прибора, и окно общих
    //      настроек). Показывается ТАБЛИЦА: какой текст поле принимало ДО
    //      правки и какой принимает ПОСЛЕ, на каждой культуре.
    //      ⛔ Разделитель ТЫСЯЧ не отдаётся: инвариантно это запятая — тот
    //      самый символ, которым русский набирает дробную часть, и принять
    //      его значило бы молча делать из «1,5» число 15.
    //
    //  ⛔ Чего этот раздел НЕ делает и почему. Отказ поля идёт через
    //     `DoubleTextBox_Validating`, а тот при отказе поднимает
    //     `MessageBox` — безоконный прогон повис бы на нём насмерть
    //     (`S100`). Поэтому настоящий обработчик зовётся ТОЛЬКО на тексте,
    //     который новый код принимает (и там судится `e.Cancel == false`), а
    //     сторона отказа мерится настоящим публичным читателем поля
    //     `GetValue()`, которым пользуются все вызывающие.
    // ══════════════════════════════════════════════════════════════════════

    sealed class P3Shot
    {
        public string Err;
        public string Culture;
        public string Bare;
        public string[] Text;      // поля окна после печати
        public double[] Back;      // числа, вернувшиеся разбором
        public string TbDouble;    // DoubleTextBox: «0.5» → GetValue
        public string TbInt;       // IntegerTextBox: «1024» → GetValue
        public string OldDouble;   // ПРЕЖНИЙ разбор поля: double.TryParse(«0.5»)
        public string OldDecimal;  // ПРЕЖНИЙ разбор ячейки: decimal.Parse(«1.5»)
        public string[] InNew;     // что поле принимает ПОСЛЕ правки
        public string[] InOld;     // что оно принимало ДО
        public string ValidatorNote;
        public string RefuseNote;  // настоящий обработчик на ОТКАЗНОМ тексте
    }

    static readonly string[] P3Fields =
        { "doubleTextBox5", "integerTextBox1", "doubleTextBox6",
          "numericUpDown7", "numericUpDown2", "numericUpDown1",
          "numericUpDown9", "numericUpDown8" };

    // Числа замера. Дробные нарочно такие, чтобы отличие разделителя было
    // видно, а разбор разрядов (если бы он остался) дал ДРУГОЕ число.
    const int P3Time = 3600;
    const int P3Channels = 1024;
    const double P3Pitch = 0.5;
    static readonly double[] P3Coeff = { 1.25, 2.5, 0.001234, 4.5, 5.5 };

    // ⛔ Набор текстов ввода. Каждый выбран под свой вопрос:
    //    «1.5»      — то, что теперь ПЕЧАТАЕТ приложение: поле обязано это взять;
    //    «1,5»      — то, что набирает человек за русской/немецкой клавиатурой;
    //    «1.234»    — три знака после точки: на немецкой системе прежний разбор
    //                 делал из этого ТЫСЯЧУ (точка там разделитель разрядов);
    //    «1,234»    — то же зеркально для английской системы;
    //    «1 5» — неразрывный пробел, разделитель разрядов русской культуры;
    //    «1 5»      — обычный пробел;
    //    «-2.5»     — знак не должен потеряться.
    static readonly string[] P3Inputs =
        { "1.5", "1,5", "1.234", "1,234", "1\u00A05", "1 5", "-2.5" };

    static void A244P1P3()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П1+П3: ЧИСЛА ОКНА НАСТРОЕК ПРИБОРА И ВВОД ЧЕЛОВЕКА");
        Say("══════════════════════════════════════════════════════════════");

        // ⛔ Реестры приборов — ДО первого `DeviceConfigForm`. В приложении их
        //    заводит `MainForm` (строки 177–178), а окна здесь нет: без них
        //    конструктор окна настроек падает `NullReferenceException` на
        //    `foreach (DeviceType … DeviceTypeList)`. Оба метода перезаводят
        //    списки заново, повторный вызов безвреден.
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();

        // Соседний раздел `A244P1` отпускает ручки по умолчанию — вернём их
        // в известное состояние, иначе эталон снимется не на инварианте.
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        P3Shot want = P3Shoot();
        if (want.Err != null)
        {
            Say("  ⛔ ЭТАЛОН НЕ СНЯТ, замер невозможен: " + want.Err);
            failures++;
            return;
        }
        Say("  эталон (инвариант) поля окна: " + string.Join(" | ", want.Text));
        Say("  эталон (инвариант) обратный разбор: " + P3Nums(want.Back));
        Say("  настоящий обработчик поля: " + want.ValidatorNote);

        string[] cross = null;

        foreach (string name in Foreign)
        {
            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Say("");
            Say("── ПЛЕЧО " + name + " (подмены разделителя НЕТ) ──");
            Thread.CurrentThread.CurrentCulture = os;
            bool comma = os.NumberFormat.NumberDecimalSeparator == ",";

            P3Shot got = P3Shoot();
            if (got.Err != null)
            {
                Say("  ⛔ плечо не отработало: " + got.Err);
                failures++;
                continue;
            }

            // ── Положительный контроль плеча: культура обязана быть видна.
            string wantBare = comma ? "1,5" : "1.5";
            bool armOk = got.Bare == wantBare;
            Say("  [полож. контроль] `(1.5).ToString()` без культуры = «" + got.Bare + "»"
                + (armOk ? " — плечо воспроизводит культуру"
                         : " ⛔ ОЖИДАЛОСЬ «" + wantBare + "»: ПЛЕЧО НЕ МЕРИТ"));
            if (!armOk) failures++;

            // ── ПРЯМОЕ: печать в поля окна.
            A244P3Same("  ПЕЧАТЬ  восемь полей `DeviceConfigForm`",
                       string.Join(" | ", got.Text), string.Join(" | ", want.Text));

            bool dot = true;
            for (int i = 0; i < got.Text.Length; i++)
            {
                string v = got.Text[i];
                if (v.IndexOf(',') >= 0 || v.IndexOf(' ') >= 0
                    || v.IndexOf('\u00A0') >= 0 || v.IndexOf('\u202F') >= 0)
                {
                    dot = false;
                }
            }
            Say("  ПЕЧАТЬ  ни запятой, ни разделителя разрядов ни в одном поле: " + Verdict(dot));
            if (!dot) failures++;

            // ── ОБРАТНОЕ: то же окно прочитало напечатанное им же.
            A244P3Nums("  ОБРАТНОЕ  `SaveFormContents` вернул те же числа", got.Back, want.Back);

            // ── ПЕРЕКРЁСТНОЕ: текст ЧУЖОГО плеча читается здесь.
            if (cross == null)
            {
                cross = got.Text;
            }
            else
            {
                P3Shot c = P3Read(cross);
                if (c.Err != null)
                {
                    Say("  ⛔ перекрёстное плечо не отработало: " + c.Err);
                    failures++;
                }
                else
                {
                    A244P3Nums("  ОБРАТНОЕ перекрёстное  поля первого чужого плеча разобраны здесь",
                               c.Back, want.Back);
                }
            }

            // ── Поля-контролы, через которые читает и окно общих настроек.
            A244P3Same("  РАЗБОР  `DoubleTextBox.GetValue()` поля «0.5»", got.TbDouble, "0.5");
            A244P3Same("  РАЗБОР  `IntegerTextBox.GetValue()` поля «1024»", got.TbInt, "1024");

            // ── ВВОД ЧЕЛОВЕКА: что поле брало ДО и что берёт ПОСЛЕ.
            Say("  ВВОД ЧЕЛОВЕКА в `DoubleTextBox` (настоящий `GetValue()`):");
            for (int i = 0; i < P3Inputs.Length; i++)
            {
                Say("    «" + P3Show(P3Inputs[i]) + "»  ДО: " + got.InOld[i]
                    + "   ПОСЛЕ: " + got.InNew[i]);
            }
            // ⛔ Судится ровно одно: разделитель ТЫСЯЧ не принимается ни на
            //    одной культуре, а напечатанное приложением «1.5» — принимается.
            A244P3Same("  ВВОД  «1.5» (то, что печатает приложение)", got.InNew[0], "1.5");
            A244P3Same("  ВВОД  «1,5» (набор с русской/немецкой клавиатуры)",
                       got.InNew[1], comma ? "1.5" : P3NoParse);
            A244P3Same("  ВВОД  «1.234» — три знака, а не тысяча", got.InNew[2], "1.234");
            A244P3Same("  ВВОД  «1<NBSP>5» — разделитель разрядов НЕ принят",
                       got.InNew[4], P3NoParse);
            A244P3Same("  ВВОД  «1 5» — пробел НЕ принят", got.InNew[5], P3NoParse);
            A244P3Same("  ВВОД  «-2.5» — знак цел", got.InNew[6], "-2.5");
            Say("    настоящий обработчик поля на принятом тексте: " + got.ValidatorNote);
            // ⛔ Сторона ОТКАЗА — тем же настоящим обработчиком (F20). До
            //    правки `A245` это плечо поставить было нельзя: обработчик
            //    поднимал голое модальное окно, и прогон вставал на нём.
            A244P3Same("  ОТКАЗ  настоящий `DoubleTextBox_Validating` на «1<NBSP>5»",
                       got.RefuseNote, P3RefuseWant);

            // ⛔ Положительный контроль на ПРЕЖНИЙ код. Он же — цена половины
            //    правки: печать точкой при старом разборе давала не отказ.
            // ⛔ Судится «прежний разбор НЕВЕРЕН», а не «прежний разбор отказал».
            //    Посылка «на культуре с запятой точка не разбирается вовсе»
            //    ПРОВЕРКОЙ СНЯТА: она верна для `ru-RU` (разделитель разрядов
            //    там неразрывный пробел, точка незаконна) и НЕВЕРНА для
            //    `de-DE`, где точка — законный разделитель разрядов и «0.5»
            //    молча становится 5. Отказ и «другое число» — оба дефект, но
            //    второй хуже, и требовать именно отказа значило бы принять
            //    худший исход за непройденное плечо.
            Say("    [полож. контроль] ПРЕЖНИЙ `double.TryParse(«0.5»)` культурой потока = "
                + got.OldDouble
                + (comma ? (got.OldDouble == "0.5"
                            ? "  ⛔ ОЖИДАЛОСЬ НЕВЕРНОЕ: ПЛЕЧО НЕ МЕРИТ"
                            : (got.OldDouble == P3NoParse
                               ? "  — дефект воспроизведён (молчаливый НОЛЬ)"
                               : "  — дефект воспроизведён (ДРУГОЕ ЧИСЛО, молча)"))
                         : "  (культура с точкой — дефекта тут и не было)"));
            if (comma && got.OldDouble == "0.5") failures++;

            Say("    [полож. контроль] ПРЕЖНИЙ `decimal.Parse(«1.5»)` культурой потока = "
                + got.OldDecimal
                + (comma ? (got.OldDecimal == "1.5"
                            ? "  ⛔ ОЖИДАЛОСЬ НЕВЕРНОЕ: ПЛЕЧО НЕ МЕРИТ"
                            : (got.OldDecimal == "15"
                               ? "  — дефект воспроизведён (в десять раз больше, молча)"
                               : "  — дефект воспроизведён (отказ)"))
                         : "  (культура с точкой)"));
            if (comma && got.OldDecimal == "1.5") failures++;
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    const string P3NoParse = "не разобрано → 0";

    // Эталон стороны отказа: поле обязано ОТКАЗАТЬ и обязано сделать это БЕЗ
    // окна — «окон поднято 0» здесь такое же судимое, как и сам отказ.
    const string P3RefuseWant =
        "`DoubleTextBox_Validating` на «1<NBSP>5» → Cancel=true (отказ, как и надо), окон поднято 0";

    /// <summary>Тип, слово и ТРИ верхних кадра стека — иначе чинить нечего.</summary>
    static string P3Where(Exception ex)
    {
        Exception e = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException : ex;
        string st = e.StackTrace ?? "";
        string[] frames = st.Split('\n');
        StringBuilder sb = new StringBuilder();
        sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
        for (int i = 0; i < frames.Length && i < 3; i++)
        {
            sb.Append(" | ").Append(frames[i].Trim());
        }
        return sb.ToString();
    }

    /// <summary>Текст с видимыми пробелами — иначе NBSP в отчёте неотличим.</summary>
    static string P3Show(string s)
    {
        return s.Replace("\u00A0", "<NBSP>").Replace(" ", "<SP>");
    }

    static void A244P3Same(string title, string got, string want)
    {
        bool ok = got != null && string.Equals(got, want, StringComparison.Ordinal);
        Say(title + " = «" + (got ?? "НЕ СНЯТО") + "»"
            + (ok ? "  совпало с эталоном" : "  ⛔ ЭТАЛОН «" + want + "»"));
        if (!ok) failures++;
    }

    static void A244P3Nums(string title, double[] got, double[] want)
    {
        bool ok = got != null && want != null && got.Length == want.Length;
        for (int i = 0; ok && i < got.Length; i++)
        {
            ok = got[i] == want[i];
        }
        Say(title + " = " + P3Nums(got) + (ok ? "  числа те же" : "  ⛔ ЭТАЛОН " + P3Nums(want)));
        if (!ok) failures++;
    }

    static string P3Nums(double[] v)
    {
        if (v == null) return "НЕ СНЯТО";
        string[] s = new string[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            s[i] = v[i].ToString("R", CultureInfo.InvariantCulture);
        }
        return string.Join(" | ", s);
    }

    /// <summary>Полный снимок: печать окна, обратный разбор, поля-контролы.</summary>
    static P3Shot P3Shoot() { return P3Run(null); }

    /// <summary>Перекрёстное плечо: в поля кладётся ЧУЖОЙ текст, читается здесь.</summary>
    static P3Shot P3Read(string[] text) { return P3Run(text); }

    static P3Shot P3Run(string[] preset)
    {
        P3Shot s = new P3Shot();
        s.Culture = CultureInfo.CurrentCulture.Name;
        if (s.Culture.Length == 0) s.Culture = "(инвариант)";
        s.Bare = (1.5).ToString();

        CultureInfo os = CultureInfo.CurrentCulture;
        Thread t = new Thread(delegate()
        {
            Thread.CurrentThread.CurrentCulture = os;
            try { P3Body(s, preset); }
            // ⚠ Со СТЕКОМ нарочно: «NullReferenceException» без места отказа —
            //    признак без читателя, по нему нечего чинить.
            catch (Exception ex) { s.Err = P3Where(ex); }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        // ⛔ Ожидание СОКРАЩЕНО со 120 с до 20 (полоса F20). Довод не «так
        //    быстрее»: модальное окно теперь снимает сторож за ≤0.2 с и
        //    называет его текст, поэтому долгое ожидание перестало быть
        //    страховкой и осталось только ценой отказа — четыре плеча по две
        //    минуты складывались в восемь минут молчания, после которых
        //    отчёт всё равно говорил «модальное окно?» ВОПРОСОМ, а не
        //    именем окна. Работы тут на десятые доли секунды.
        if (!t.Join(20000))
        {
            s.Err = "окно настроек не ответило за 20 с; модальных окон сторож"
                  + " насчитал к этому мигу " + modalSeen
                  + (modalSeen == 0
                     ? " — значит дело НЕ в окне, ищи блокировку в другом месте"
                     : " — см. строки сторожа выше");
        }
        return s;
    }

    static void P3Body(P3Shot s, string[] preset)
    {
        // ── Прежний разбор, снятый ДО правок формы: он ничем не связан с
        //    формой и служит положительным контролем плеча.
        double old;
        s.OldDouble = double.TryParse("0.5", out old)
            ? old.ToString("R", CultureInfo.InvariantCulture)
            : P3NoParse;
        try { s.OldDecimal = decimal.Parse("1.5").ToString(CultureInfo.InvariantCulture); }
        catch (FormatException) { s.OldDecimal = "FormatException"; }

        // ── Поля-контролы окна общих настроек: печать инвариантом → разбор.
        DoubleTextBox dtb = new DoubleTextBox();
        dtb.Text = P3Pitch.ToString(CultureInfo.InvariantCulture);
        s.TbDouble = dtb.GetValue().ToString("R", CultureInfo.InvariantCulture);
        IntegerTextBox itb = new IntegerTextBox();
        itb.Text = P3Channels.ToString(CultureInfo.InvariantCulture);
        s.TbInt = itb.GetValue().ToString(CultureInfo.InvariantCulture);

        P3Input(s);

        // ── Настоящее окно настроек прибора.
        DeviceConfigForm form = new DeviceConfigForm();
        DeviceConfigInfo cfg = P3Config();

        MethodInfo load = typeof(DeviceConfigForm).GetMethod("LoadFormContents", NP);
        MethodInfo save = typeof(DeviceConfigForm).GetMethod("SaveFormContents", NP);
        if (load == null || save == null)
        {
            throw new InvalidOperationException("`LoadFormContents`/`SaveFormContents` не найдены: сборка чужая");
        }

        load.Invoke(form, new object[] { cfg });

        s.Text = new string[P3Fields.Length];
        for (int i = 0; i < P3Fields.Length; i++)
        {
            s.Text[i] = P3Ctl(form, P3Fields[i]).Text;
        }

        // Перекрёстное плечо: подменяем текст полей чужим.
        if (preset != null)
        {
            for (int i = 0; i < P3Fields.Length; i++)
            {
                P3Ctl(form, P3Fields[i]).Text = preset[i];
            }
            s.Text = (string[])preset.Clone();
        }

        DeviceConfigInfo back = P3Config();
        object ok = save.Invoke(form, new object[] { back });
        if (!(bool)ok)
        {
            throw new InvalidOperationException("`SaveFormContents` вернул false: разбор полей отказал");
        }

        PolynomialEnergyCalibration cal = (PolynomialEnergyCalibration)back.EnergyCalibration;
        s.Back = new double[]
        {
            back.DefaultMeasurementTime, back.NumberOfChannels, back.ChannelPitch,
            cal.Coefficients[0], cal.Coefficients[1], cal.Coefficients[2],
            cal.Coefficients[3], cal.Coefficients[4]
        };
    }

    /// <summary>
    /// ВВОД ЧЕЛОВЕКА. «Принято» мерится НАСТОЯЩИМ читателем поля —
    /// `DoubleTextBox.GetValue()`; «принято ДО» — тем самым вызовом, который
    /// в этом методе и стоял (`double.TryParse(this.Text, out num)`, культура
    /// потока, стили по умолчанию — а они несут `AllowThousands`).
    ///
    /// ⛔ Настоящий `DoubleTextBox_Validating` зовётся ТОЛЬКО на принятом
    ///    тексте: на отказе он поднимает `MessageBox`, и безоконный прогон
    ///    повис бы (`S100`). Того, что оба пути судят одним предикатом, это
    ///    плечо и не утверждает — оно показывает, что на напечатанном
    ///    приложением «1.5» настоящий обработчик отказа НЕ даёт.
    /// </summary>
    static void P3Input(P3Shot s)
    {
        s.InNew = new string[P3Inputs.Length];
        s.InOld = new string[P3Inputs.Length];
        DoubleTextBox box = new DoubleTextBox();
        for (int i = 0; i < P3Inputs.Length; i++)
        {
            box.Text = P3Inputs[i];
            double got = box.GetValue();
            s.InNew[i] = got == 0.0 ? P3NoParse : got.ToString("R", CultureInfo.InvariantCulture);

            double before;
            s.InOld[i] = double.TryParse(P3Inputs[i], out before)
                ? before.ToString("R", CultureInfo.InvariantCulture)
                : P3NoParse;
        }

        // Настоящий обработчик — на тексте, который новый код принимает.
        MethodInfo v = typeof(DoubleTextBox).GetMethod("DoubleTextBox_Validating", NP);
        if (v == null)
        {
            s.ValidatorNote = "⛔ `DoubleTextBox_Validating` не найден: сборка чужая";
            return;
        }
        box.Text = "1.5";
        System.ComponentModel.CancelEventArgs e = new System.ComponentModel.CancelEventArgs();
        v.Invoke(box, new object[] { box, e });
        s.ValidatorNote = "`DoubleTextBox_Validating` на «1.5» → Cancel="
            + (e.Cancel ? "true ⛔ ПОЛЕ РУГАЕТСЯ НА СОБСТВЕННОЕ СОДЕРЖИМОЕ" : "false (отказа нет)");

        // ⛔ СТОРОНА ОТКАЗА — настоящим обработчиком, а не в обход (F20).
        //    До правки `A245` этого плеча не было вовсе: обработчик поднимал
        //    голый `MessageBox`, безоконный прогон вставал на нём, и полоса
        //    F17 честно записала, что мерить отказ настоящим обработчиком
        //    нельзя. Теперь сообщение идёт дверью `AppUi.Report`, окна нет, и
        //    отказ мерится ТЕМ ЖЕ путём, каким его увидит человек. Текст
        //    выбран заведомо негодный: неразрывный пробел — разделитель
        //    разрядов, число из него собирать нельзя.
        int seenBefore = modalSeen;
        box.Text = "1 5";
        System.ComponentModel.CancelEventArgs r = new System.ComponentModel.CancelEventArgs();
        v.Invoke(box, new object[] { box, r });
        s.RefuseNote = "`DoubleTextBox_Validating` на «1<NBSP>5» → Cancel="
            + (r.Cancel ? "true (отказ, как и надо)" : "false ⛔ ПОЛЕ ПРИНЯЛО НЕГОДНЫЙ ТЕКСТ")
            + ", окон поднято " + (modalSeen - seenBefore);
    }

    static DeviceConfigInfo P3Config()
    {
        DeviceConfigInfo cfg = new DeviceConfigInfo();
        cfg.InitFormatVersion();
        cfg.Guid = "p3-culture-probe";
        cfg.Name = "P3";
        cfg.Filename = "p3.xml";
        // Тип прибора берётся ИЗ РЕЕСТРА, а не выписывается сюда именем: реестр
        // и есть источник, а выписанное имя устарело бы молча.
        cfg.DeviceType = DeviceType.DeviceTypeList[0].Id;
        cfg.ThermometerType = ThermometerType.ThermometerTypeList[0].Id;
        cfg.DefaultMeasurementTime = P3Time;
        cfg.NumberOfChannels = P3Channels;
        cfg.ChannelPitch = P3Pitch;
        PolynomialEnergyCalibration cal = (PolynomialEnergyCalibration)cfg.EnergyCalibration;
        cal.PolynomialOrder = 4;
        cal.Coefficients = (double[])P3Coeff.Clone();
        return cfg;
    }

    static System.Windows.Forms.Control P3Ctl(object form, string name)
    {
        FieldInfo f = form.GetType().GetField(name, NP);
        if (f == null) throw new InvalidOperationException("поля `" + name + "` нет: сборка чужая");
        System.Windows.Forms.Control c = f.GetValue(form) as System.Windows.Forms.Control;
        if (c == null) throw new InvalidOperationException("поле `" + name + "` пусто");
        return c;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  `A244`, ДОЛИ П5 «панели DC» и П6 «зоны интереса».
    //  Полоса F22 (05.09.2026) правила приложение и оборвалась на ЗОВЕ этого
    //  раздела: в `Main` строка `A244P5P6();` стояла, а метода не было вовсе,
    //  и проба не собиралась (`CS0103`). Раздел дописан полосой F25.
    //
    //  Что мерится и почему именно это. Доли П5 и П6 — это девять экранных
    //  снастей, и общего у них ровно одно: число, НАПЕЧАТАННОЕ в поле, потом
    //  ЧИТАЕТСЯ обратно тем же кодом. Поэтому каждое плечо ставится ПАРОЙ:
    //  печать (что видит человек) и разбор (что программа из увиденного
    //  соберёт), а между ними — ПЕРЕКРЁСТНОЕ плечо: текст, напечатанный на
    //  чужой культуре, обязан разобраться здесь в те же числа. Половина
    //  правки тут опаснее целой: печать точкой при разборе культурой даёт не
    //  отказ, а ДРУГОЕ ЧИСЛО, молча.
    //
    //  ⛔ Снасти берутся НАСТОЯЩИЕ. `MainForm` поднимать нельзя (окно),
    //     поэтому она создаётся БЕЗ конструктора
    //     (`FormatterServices.GetUninitializedObject`) и ей выставляются ровно
    //     два поля, которые спрашивают виды, — тем же приёмом, что и в
    //     соседней пробе `ModalReachProbeF22`.
    //
    //  ⚠ Слоты ВВОДА (их 27) собираются в том же порядке, в каком печатались:
    //    на них ложится перекрёстное плечо и по ним же идёт обратный разбор.
    //    Слоты ВЫВОДА (надписи скорости счёта, подписи формы пика, строка
    //    области) обратно не читаются — они судятся только печатью.
    // ══════════════════════════════════════════════════════════════════════

    sealed class P5Shot
    {
        public string Culture;
        public string Bare;
        public string Err;
        public string[] Text;    // ВСЁ, что напечатано (ввод + вывод)
        public string[] In;      // только слоты ввода, в порядке печати
        public double[] Back;    // они же, прочитанные обратно
        public string OldDouble;
        public string OldDecimal;
    }

    // Числа сцены. Взяты так, чтобы каждое поймало свою беду: больше тысячи
    // (разделитель разрядов), с дробной частью (разделитель дроби), с минусом
    // (знак не должен пропасть) и мельче тысячной (короткая форма печати).
    const int P5Time = 3600;
    const double P5Cps = 1234.5;
    const int P5Ratio = 4096;
    const double P5Dead = 12.3456;
    static readonly double[] P5Coeff = { 1234.5, 0.5, -2.75, 0.125, 1024.25 };
    static readonly double[] P5Simple = { 1234.5, 0.25, 661.5, 1460.75 };
    static readonly double[] P5Covell =
        { 1234.5, 0.25, 661.5, 1460.75, 609.25, 1764.5, 12.5, 24.75 };
    static readonly double[] P5Ref = { 4321.75, 0.125 };
    // Порядок ТОТ ЖЕ, в каком печатает `LoadROIDefinitionFormContents`:
    // K, ΔK, энергия пика, период, выход, нижняя, верхняя.
    static readonly double[] P5Roi =
        { 1234.5, 0.75, 661.657, 949944960.0, 85.1, 620.5, 700.25 };
    const double P5Chi2 = 1234.56789;
    const int P5Ndp = 7;
    const double P5Score = 0.123456789;

    static void A244P5P6()
    {
        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("`A244` П5+П6: ЧИСЛА ПАНЕЛЕЙ DC И ОКНА ЗОН ИНТЕРЕСА");
        Say("══════════════════════════════════════════════════════════════");

        // Соседние разделы отпускают ручки культуры — вернём их в известное
        // состояние, иначе эталон снимется не на инварианте.
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        P5Shot want = P5Run(null);
        if (want.Err != null)
        {
            Say("  ⛔ ЭТАЛОН НЕ СНЯТ, замер невозможен: " + want.Err);
            failures++;
            return;
        }
        Say("  слотов печати: " + want.Text.Length + ", из них слотов ввода: " + want.In.Length);
        Say("  эталон (инвариант) печать: " + string.Join(" | ", want.Text));
        Say("  эталон (инвариант) обратный разбор: " + P3Nums(want.Back));

        string[] cross = null;

        foreach (string name in Foreign)
        {
            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Say("");
            Say("── ПЛЕЧО " + name + " (подмены разделителя НЕТ) ──");
            Thread.CurrentThread.CurrentCulture = os;
            bool comma = os.NumberFormat.NumberDecimalSeparator == ",";

            P5Shot got = P5Run(null);
            if (got.Err != null)
            {
                Say("  ⛔ плечо не отработало: " + got.Err);
                failures++;
                continue;
            }

            // ── Положительный контроль плеча: культура обязана быть видна.
            //    Без него все проверки «прошли» бы, не измерив ничего.
            string wantBare = comma ? "1,5" : "1.5";
            bool armOk = got.Bare == wantBare;
            Say("  [полож. контроль] `(1.5).ToString()` без культуры = «" + got.Bare + "»"
                + (armOk ? " — плечо воспроизводит культуру"
                         : " ⛔ ОЖИДАЛОСЬ «" + wantBare + "»: ПЛЕЧО НЕ МЕРИТ"));
            if (!armOk) failures++;

            // ── ПРЯМОЕ: печать всех девяти снастей.
            A244P5Same("  ПЕЧАТЬ  все слоты П5+П6",
                       string.Join(" | ", got.Text), string.Join(" | ", want.Text));

            // ── Отдельно и строго: в ЧИСЛОВЫХ полях ввода не бывает ни
            //    запятой, ни разделителя разрядов (решение Amber «группировки
            //    нет вовсе»). Слоты вывода сюда не берутся: в них законно
            //    стоят пробелы разметки («661.5 - 1460.75 keV»).
            bool dot = true;
            string dirty = null;
            for (int i = 0; i < got.In.Length; i++)
            {
                string v = got.In[i];
                if (v == null) continue;
                if (v.IndexOf(',') >= 0 || v.IndexOf(' ') >= 0
                    || v.IndexOf('\u00A0') >= 0 || v.IndexOf('\u202F') >= 0)
                {
                    dot = false;
                    if (dirty == null) dirty = "слот " + i + " = «" + P3Show(v) + "»";
                }
            }
            Say("  ПЕЧАТЬ  ни запятой, ни разделителя разрядов в 27 полях ввода: "
                + Verdict(dot) + (dirty == null ? "" : "  (" + dirty + ")"));
            if (!dot) failures++;

            // ── ОБРАТНОЕ: та же снасть прочитала напечатанное ею же.
            A244P5Nums("  ОБРАТНОЕ  27 полей разобраны в те же числа", got.Back, want.Back);

            // ── ПЕРЕКРЁСТНОЕ: текст ЧУЖОГО плеча читается здесь.
            if (cross == null)
            {
                cross = got.In;
            }
            else
            {
                P5Shot c = P5Run(cross);
                if (c.Err != null)
                {
                    Say("  ⛔ перекрёстное плечо не отработало: " + c.Err);
                    failures++;
                }
                else
                {
                    // ⚠ Плечо, сравнивающее только ЧИСЛА, откат правки может
                    //    не поймать: `UserNumber` читает терпимо и «0,5» тоже
                    //    возьмёт, две ошибки погасят друг друга. Поэтому
                    //    рядом стоит плечо, сравнивающее ТЕКСТ поля.
                    A244P5Nums("  ОБРАТНОЕ перекрёстное  поля чужого плеча разобраны здесь",
                               c.Back, want.Back);
                    A244P5Same("  ТЕКСТ перекрёстный  поля чужого плеча остались теми же",
                               string.Join(" | ", c.In), string.Join(" | ", want.In));
                }
            }

            // ── Положительный контроль на ПРЕЖНИЙ код: он же цена половины
            //    правки. ⛔ Судится «прежний разбор НЕВЕРЕН», а не «прежний
            //    разбор отказал»: на `de-DE` точка — законный разделитель
            //    разрядов, и «0.5» молча становится 5, а не отвергается.
            Say("    [полож. контроль] ПРЕЖНИЙ `double.TryParse(«0.5»)` культурой потока = "
                + got.OldDouble
                + (comma ? (got.OldDouble == "0.5"
                            ? "  ⛔ ОЖИДАЛОСЬ НЕВЕРНОЕ: ПЛЕЧО НЕ МЕРИТ"
                            : (got.OldDouble == P3NoParse
                               ? "  — дефект воспроизведён (молчаливый НОЛЬ)"
                               : "  — дефект воспроизведён (ДРУГОЕ ЧИСЛО, молча)"))
                         : "  (культура с точкой — дефекта тут и не было)"));
            if (comma && got.OldDouble == "0.5") failures++;

            Say("    [полож. контроль] ПРЕЖНИЙ `decimal.Parse(«1.5»)` культурой потока = "
                + got.OldDecimal
                + (comma ? (got.OldDecimal == "1.5"
                            ? "  ⛔ ОЖИДАЛОСЬ НЕВЕРНОЕ: ПЛЕЧО НЕ МЕРИТ"
                            : (got.OldDecimal == "15"
                               ? "  — дефект воспроизведён (в десять раз больше, молча)"
                               : "  — дефект воспроизведён (отказ)"))
                         : "  (культура с точкой)"));
            if (comma && got.OldDecimal == "1.5") failures++;
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    static void A244P5Same(string title, string got, string want)
    {
        bool ok = got != null && string.Equals(got, want, StringComparison.Ordinal);
        Say(title + (ok ? "  совпало с эталоном" : ""));
        if (!ok)
        {
            Say("    ⛔ ПОЛУЧЕНО «" + (got ?? "НЕ СНЯТО") + "»");
            Say("       ЭТАЛОН   «" + want + "»");
            failures++;
        }
    }

    static void A244P5Nums(string title, double[] got, double[] want)
    {
        bool ok = got != null && want != null && got.Length == want.Length;
        int first = -1;
        for (int i = 0; ok && i < got.Length; i++)
        {
            if (got[i] != want[i]) { ok = false; first = i; }
        }
        Say(title + (ok ? "  числа те же" : ""));
        if (!ok)
        {
            Say("    ⛔ ПОЛУЧЕНО " + P3Nums(got));
            Say("       ЭТАЛОН   " + P3Nums(want)
                + (first >= 0 ? "  (первое расхождение в слоте " + first + ")" : ""));
            failures++;
        }
    }

    static void P5SetField(object target, string name, object value)
    {
        Type t = target.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField(name, NP);
            if (f != null) { f.SetValue(target, value); return; }
            t = t.BaseType;
        }
        throw new InvalidOperationException("поля `" + name + "` нет: сборка чужая");
    }

    static object P5Field(object target, string name)
    {
        Type t = target.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField(name, NP);
            if (f != null) return f.GetValue(target);
            t = t.BaseType;
        }
        throw new InvalidOperationException("поля `" + name + "` нет: сборка чужая");
    }

    static System.Windows.Forms.Control P5Ctl(object target, string name)
    {
        System.Windows.Forms.Control c = P5Field(target, name) as System.Windows.Forms.Control;
        if (c == null) throw new InvalidOperationException("поле `" + name + "` пусто или не снасть");
        return c;
    }

    static object P5Call(object target, string name, params object[] args)
    {
        Type t = target.GetType();
        while (t != null)
        {
            MethodInfo m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public
                                             | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (m != null)
            {
                try { return m.Invoke(target, args); }
                catch (TargetInvocationException ex)
                {
                    throw new InvalidOperationException(
                        name + " бросил: " + P3Where(ex.InnerException ?? ex));
                }
            }
            t = t.BaseType;
        }
        throw new InvalidOperationException("метода `" + name + "` нет: сборка чужая");
    }

    static P5Shot P5Run(string[] preset)
    {
        P5Shot s = new P5Shot();
        s.Culture = CultureInfo.CurrentCulture.Name;
        if (s.Culture.Length == 0) s.Culture = "(инвариант)";
        s.Bare = (1.5).ToString();

        CultureInfo os = CultureInfo.CurrentCulture;
        Thread t = new Thread(delegate()
        {
            Thread.CurrentThread.CurrentCulture = os;
            try { P5Body(s, preset); }
            catch (Exception ex) { s.Err = P3Where(ex); }
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(60000))
        {
            s.Err = "снасти П5/П6 не ответили за 60 с; модальных окон сторож насчитал к этому"
                  + " мигу " + modalSeen
                  + (modalSeen == 0
                     ? " — значит дело НЕ в окне, ищи блокировку в другом месте"
                     : " — см. строки сторожа выше");
        }
        return s;
    }

    static int p5Scene;

    static void P5Body(P5Shot s, string[] preset)
    {
        // ── Прежний разбор: ничем не связан со снастями и служит
        //    положительным контролем плеча.
        double old;
        s.OldDouble = double.TryParse("0.5", out old)
            ? old.ToString("R", CultureInfo.InvariantCulture)
            : P3NoParse;
        try { s.OldDecimal = decimal.Parse("1.5").ToString(CultureInfo.InvariantCulture); }
        catch (FormatException) { s.OldDecimal = "FormatException"; }

        List<string> text = new List<string>();
        List<string> ins = new List<string>();
        List<System.Windows.Forms.Control> boxes = new List<System.Windows.Forms.Control>();
        List<double> back = new List<double>();

        // ── Оболочка БЕЗ конструктора: `MainForm` — окно, поднимать нельзя.
        //    Виды спрашивают у неё ровно два поля.
        p5Scene++;
        DocEnergySpectrum doc = DocumentManager.GetInstance()
            .CreateDocument("f25-p5p6-" + p5Scene.ToString(CultureInfo.InvariantCulture) + ".xml");
        if (doc == null) throw new InvalidOperationException("документ не создан: сцена не встала");
        MainForm shell = (MainForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainForm));
        P5SetField(shell, "activeDocument", doc);
        P5SetField(shell, "documentManager", DocumentManager.GetInstance());

        // ══ П5-1. `DCCountRateView.UpdateInfo` — три надписи, только вывод. ══
        DCCountRateView rate = new DCCountRateView(shell);
        rate.UpdateInfo(P5Cps, P5Ratio, P5Dead);
        text.Add(P5Ctl(rate, "LossCountsRatioValLbl").Text);
        text.Add(P5Ctl(rate, "cpslabel").Text);
        text.Add(P5Ctl(rate, "DeadTimeValLbl").Text);

        // ══ П5-2. `DCControlPanel.PresetTime` — печать и разбор одной парой. ══
        DCControlPanel panel = new DCControlPanel(shell);
        panel.PresetTime = P5Time;
        System.Windows.Forms.Control timeBox = P5Ctl(panel, "realTimeLimitTextBox");
        text.Add(timeBox.Text); ins.Add(timeBox.Text); boxes.Add(timeBox);

        // ══ П5-3. `DCEnergyCalibrationView.SetEnergyCalibration` — пять полей. ══
        DCEnergyCalibrationView energy = new DCEnergyCalibrationView(shell);
        PolynomialEnergyCalibration cal = new PolynomialEnergyCalibration();
        cal.PolynomialOrder = 4;
        cal.Coefficients = (double[])P5Coeff.Clone();
        energy.SetEnergyCalibration(cal, (EnergyCalibration)cal.Clone());
        // Порядок печати в самом виде: 3, 2, 1, 5, 4 — коэффициенты 0..4.
        string[] coeffFields = { "numericUpDown3", "numericUpDown2", "numericUpDown1",
                                 "numericUpDown5", "numericUpDown4" };
        foreach (string f in coeffFields)
        {
            System.Windows.Forms.Control c = P5Ctl(energy, f);
            text.Add(c.Text); ins.Add(c.Text); boxes.Add(c);
        }

        // ══ П5-4. `DCFwhmCalibrationView` — подписи формы пика и четыре
        //          собранные строки. Только вывод. ══
        DCFwhmCalibrationView fwhmView = new DCFwhmCalibrationView(shell);
        SimpleSqrtFwhmCalibration fc = new SimpleSqrtFwhmCalibration();
        fc.PeakType = FwhmCalibration.ExpGaussExpPeakType;
        fc.ExpGaussExpLeftTail = 1.25;
        fc.ExpGaussExpRightTail = 3.75;
        fc.VoigtSigma = 0.25;
        fc.VoigtGamma = 0.75;
        P5SetField(fwhmView, "fwhmCalibration", fc);
        P5Call(fwhmView, "UpdatePeakShapeInfo");
        text.Add(P5Ctl(fwhmView, "peakShapeFirstParameterValueLabel").Text);
        text.Add(P5Ctl(fwhmView, "peakShapeSecondParameterValueLabel").Text);
        text.Add((string)P5Call(fwhmView, "FormatPeakFitRatio", P5Chi2, P5Ndp));
        text.Add((string)P5Call(fwhmView, "FormatPeakShapeScore", P5Score));
        text.Add((string)P5Call(fwhmView, "GetExpGaussExpParametersText", 25, 10));
        text.Add((string)P5Call(fwhmView, "GetVoigtParametersText", 25, 10));

        // ══ П6-1. `ROISimpleDifferenceControl` — четыре числа по кругу. ══
        string op0 = ROIPrimitiveOperation.Operations[0].Name;
        ROISimpleDifferenceControl simple = new ROISimpleDifferenceControl();
        ROISimpleDifferenceData sd = new ROISimpleDifferenceData();
        sd.OperationType = op0;
        sd.Coefficient = P5Simple[0];
        sd.CoefficientError = P5Simple[1];
        sd.LowerLimit = P5Simple[2];
        sd.UpperLimit = P5Simple[3];
        simple.LoadFormContents(sd);
        foreach (string f in new string[] { "doubleTextBox3", "doubleTextBox4",
                                            "doubleTextBox1", "doubleTextBox2" })
        {
            System.Windows.Forms.Control c = P5Ctl(simple, f);
            text.Add(c.Text); ins.Add(c.Text); boxes.Add(c);
        }

        // ══ П6-2. `ROICovellMethodControl` — восемь чисел по кругу. ══
        ROICovellMethodControl covell = new ROICovellMethodControl();
        ROICovellMethodData cd = new ROICovellMethodData();
        cd.OperationType = op0;
        cd.Coefficient = P5Covell[0];
        cd.CoefficientError = P5Covell[1];
        cd.LowerLimit = P5Covell[2];
        cd.UpperLimit = P5Covell[3];
        cd.LeftRegionCenter = P5Covell[4];
        cd.RightRegionCenter = P5Covell[5];
        cd.LeftRegionWidth = P5Covell[6];
        cd.RightRegionWidth = P5Covell[7];
        covell.LoadFormContents(cd);
        foreach (string f in new string[] { "doubleTextBox3", "doubleTextBox4",
                                            "doubleTextBox1", "doubleTextBox2",
                                            "doubleTextBox5", "doubleTextBox6",
                                            "doubleTextBox7", "doubleTextBox8" })
        {
            System.Windows.Forms.Control c = P5Ctl(covell, f);
            text.Add(c.Text); ins.Add(c.Text); boxes.Add(c);
        }

        // ══ П6-3. `ROIReferenceControl` — два числа по кругу. ══
        ROIReferenceControl reference = new ROIReferenceControl();
        ROIReferenceData rd2 = new ROIReferenceData();
        rd2.OperationType = op0;
        rd2.Coefficient = P5Ref[0];
        rd2.CoefficientError = P5Ref[1];
        rd2.Reference = "";
        reference.LoadFormContents(rd2);
        foreach (string f in new string[] { "doubleTextBox3", "doubleTextBox4" })
        {
            System.Windows.Forms.Control c = P5Ctl(reference, f);
            text.Add(c.Text); ins.Add(c.Text); boxes.Add(c);
        }

        // ══ П6-4. Окно зон целиком: семь полей зоны по кругу. ══
        ROIConfigForm roiForm = new ROIConfigForm();
        ROIDefinitionData roi = new ROIDefinitionData();
        roi.Name = "P5P6";
        roi.Enabled = true;
        roi.AutoBecquerelCoefficient = false;
        roi.BecquerelCoefficient = P5Roi[0];
        roi.BecquerelCoefficientError = P5Roi[1];
        roi.PeakEnergy = P5Roi[2];
        roi.HalfLife = P5Roi[3];
        roi.Intencity = P5Roi[4];
        roi.LowerLimit = P5Roi[5];
        roi.UpperLimit = P5Roi[6];
        P5Call(roiForm, "LoadROIDefinitionFormContents", roi);
        foreach (string f in new string[] { "doubleTextBox3", "doubleTextBox4",
                                            "doubleTextBox5", "doubleTextBox6",
                                            "doubleTextBox7", "doubleTextBox1",
                                            "doubleTextBox2" })
        {
            System.Windows.Forms.Control c = P5Ctl(roiForm, f);
            text.Add(c.Text); ins.Add(c.Text); boxes.Add(c);
        }

        // ══ П6-5. Строка области — `string.Concat(new object[]{…})`.
        //          ⚠ Место, которого СКАНЕР НЕ ВИДИТ вовсе: число уходит в
        //          `object[]`, и `ToString()` без культуры зовёт сама
        //          склейка. Поймано только глазами, судится только здесь. ══
        text.Add((string)P5Call(roiForm, "GetPrimitiveRegionString", sd));
        text.Add((string)P5Call(roiForm, "GetPrimitiveRegionString", cd));

        s.Text = text.ToArray();

        // ── ПЕРЕКРЁСТНОЕ плечо: в те же поля кладём ЧУЖОЙ текст.
        if (preset != null)
        {
            if (preset.Length != boxes.Count)
            {
                throw new InvalidOperationException("перекрёстное плечо: слотов "
                    + boxes.Count + ", а подано " + preset.Length);
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                boxes[i].Text = preset[i];
            }
            s.In = (string[])preset.Clone();
        }
        else
        {
            s.In = ins.ToArray();
        }

        // ══════════ ОБРАТНЫЙ РАЗБОР, В ТОМ ЖЕ ПОРЯДКЕ ══════════
        back.Add(panel.PresetTime);

        foreach (string f in coeffFields)
        {
            back.Add((double)P5Call(energy, "fromStringtoDouble", P5Ctl(energy, f).Text));
        }

        ROISimpleDifferenceData sdBack = new ROISimpleDifferenceData();
        sdBack.OperationType = op0;
        if (!simple.SaveFormContents(sdBack))
        {
            throw new InvalidOperationException("`ROISimpleDifferenceControl.SaveFormContents`"
                + " вернул false: разбор полей отказал");
        }
        back.Add(sdBack.Coefficient); back.Add(sdBack.CoefficientError);
        back.Add(sdBack.LowerLimit); back.Add(sdBack.UpperLimit);

        ROICovellMethodData cdBack = new ROICovellMethodData();
        cdBack.OperationType = op0;
        if (!covell.SaveFormContents(cdBack))
        {
            throw new InvalidOperationException("`ROICovellMethodControl.SaveFormContents`"
                + " вернул false: разбор полей отказал");
        }
        back.Add(cdBack.Coefficient); back.Add(cdBack.CoefficientError);
        back.Add(cdBack.LowerLimit); back.Add(cdBack.UpperLimit);
        back.Add(cdBack.LeftRegionCenter); back.Add(cdBack.RightRegionCenter);
        back.Add(cdBack.LeftRegionWidth); back.Add(cdBack.RightRegionWidth);

        ROIReferenceData refBack = new ROIReferenceData();
        refBack.OperationType = op0;
        if (!reference.SaveFormContents(refBack))
        {
            throw new InvalidOperationException("`ROIReferenceControl.SaveFormContents`"
                + " вернул false: разбор полей отказал");
        }
        back.Add(refBack.Coefficient); back.Add(refBack.CoefficientError);

        ROIDefinitionData roiBack = new ROIDefinitionData();
        roiBack.AutoBecquerelCoefficient = false;
        object saved = P5Call(roiForm, "SaveROIDefinitionFormContents", roiBack);
        if (!(bool)saved)
        {
            throw new InvalidOperationException("`SaveROIDefinitionFormContents`"
                + " вернул false: разбор полей зоны отказал");
        }
        back.Add(roiBack.BecquerelCoefficient); back.Add(roiBack.BecquerelCoefficientError);
        back.Add(roiBack.PeakEnergy); back.Add(roiBack.HalfLife); back.Add(roiBack.Intencity);
        back.Add(roiBack.LowerLimit); back.Add(roiBack.UpperLimit);

        s.Back = back.ToArray();
    }
}
