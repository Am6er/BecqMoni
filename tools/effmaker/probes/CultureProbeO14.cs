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
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.NucBase;
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

        Say("");
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
