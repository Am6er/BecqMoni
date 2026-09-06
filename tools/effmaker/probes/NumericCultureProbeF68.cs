// `A261`: ПОЛЕ СО СТРЕЛКАМИ ПЕЧАТАЕТ И РАЗБИРАЕТ ЧИСЛО КУЛЬТУРОЙ ПОТОКА.
//
//     numericcultureprobef68 [--out=<файл>] [--modal-control]
//
// Штатный `System.Windows.Forms.NumericUpDown` берёт `CultureInfo.CurrentCulture`
// внутри себя, в четырёх местах сразу, и обойти их снаружи нельзя:
//
//   * печать  — `UpdateEditText` → `GetNumberText` → `value.ToString("F2", CurrentCulture)`;
//   * разбор  — `ParseEditText` → `Decimal.Parse(Text, CurrentCulture)`, метод ЗАКРЫТ;
//   * стрелки — `UpButton`/`DownButton` зовут тот же закрытый разбор;
//   * набор   — `OnTextBoxKeyPress` пропускает только знаки ТЕКУЩЕЙ культуры,
//               то есть на русской системе СЪЕДАЕТ точку с писком.
//
// Приказ Amber 05.09.2026 — разделитель дробной части ВСЕГДА ТОЧКА, и на этих
// полях он не выполнялся: под `ru-RU` человек видел «0,50».
//
// ⛔ ОКНО `BecqMoni` НЕ ЗАПУСКАЕТСЯ. Формы заводятся ОТРАЖЕНИЕМ из собранной
// сборки, культура потока подменяется на время замера, показывается только
// `Text` контрола и результат разбора поданного текста.
//
// Что меряется, по каждому найденному полю и по каждой из двух культур:
//
//   1. ПЕЧАТЬ — `Text` контрола после того, как форма построена. Запятая в нём
//      под `ru-RU` — это ровно то, что видит человек.
//   2. РАЗБОР — в `Text` кладётся число, напечатанное ТОЧКОЙ, и читается
//      `Value`. Расхождение — это «человек ввёл 0.5, а поле прочло другое».
//   3. НАБОР — `OnTextBoxKeyPress` зовётся отражением со знаком «.»: съеденная
//      точка значит, что до разбора дело вообще не дойдёт.
//
// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ идёт ВСЕГДА, а не по ключу: рядом с полями форм
// меряется ПОДБРОШЕННЫЙ голый `NumericUpDown` с теми же настройками. Он обязан
// быть найден по всем трём признакам; если он чист — проба не мерит ничего, и
// прогон кончается отказом независимо от форм.
//
// ⛔ ВТОРОЙ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — культура `en-US`: её столбец обязан
// совпасть с прежним замером ЗНАК В ЗНАК. Сравнение делает не проба, а
// сличение её же выгрузок «до» и «после» (ключ `--out=`).
//
// Целевая платформа процесса объявлена общим довеском `_TargetFramework.cs`.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

static class NumericCultureProbeF68
{
    const string Invariant = "InvariantNumericUpDown";

    static readonly StringBuilder Log = new StringBuilder();
    static readonly List<string> Rows = new List<string>();
    static int bad;
    static int scenesBuilt, scenesFailed;

    // Итоги по культурам: сколько полей показывает запятую в печати, сколько
    // не разобрало поданную точку, сколько съело точку при наборе.
    static readonly Dictionary<string, int> CommaPrint = new Dictionary<string, int>();
    static readonly Dictionary<string, int> ParseWrong = new Dictionary<string, int>();
    static readonly Dictionary<string, int> DotEaten = new Dictionary<string, int>();
    static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
    static int notInvariantType;

    [STAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (Exception) { }

        string outPath = null;
        bool modalControl = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a == "--modal-control") modalControl = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        // ⛔ Сторож модальных окон — до всего остального: голое окно на
        //    безоконном пути вешает прогон насмерть (`A245`).
        ModalWatchStart();

        Say(ProbeTargetFramework.Describe());
        Say("культура ОС:       " + CultureInfo.InstalledUICulture.Name
            + "; культура потока при старте: " + Thread.CurrentThread.CurrentCulture.Name);
        Type inv = typeof(DeviceConfigForm).Assembly.GetType("BecquerelMonitor." + Invariant);
        Say("общий приём:       " + (inv == null
            ? "⛔ типа BecquerelMonitor." + Invariant + " в сборке НЕТ (замер «до»)"
            : "тип BecquerelMonitor." + Invariant + " в сборке ЕСТЬ"));

        // ⛔ Порядок заведения одиночек — тот же, что у `MainForm` (`MainForm.cs`):
        //    без него три формы падают `NullReferenceException` ещё в
        //    конструкторе, и двенадцать полей `DeviceConfigForm` не мерятся вовсе.
        Bootstrap();

        if (modalControl)
        {
            ModalControl();
            ModalWatchStop();
            Say("");
            Say(bad == 0
                ? "⛔ КОНТРОЛЬ НЕ СРАБОТАЛ: сторож окон не засчитал ни одного окна"
                : "РАСХОЖДЕНИЙ: " + bad + " (так и надо: это контроль сторожа)");
            Dump(outPath);
            return Leave(bad == 0 ? 1 : 0);
        }

        try
        {
            foreach (string culture in new[] { "en-US", "ru-RU" })
            {
                Head("КУЛЬТУРА ПОТОКА " + culture);
                CultureInfo prev = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                try
                {
                    Seen[culture] = 0;
                    CommaPrint[culture] = 0;
                    ParseWrong[culture] = 0;
                    DotEaten[culture] = 0;

                    MeasureField(culture, "(ПОДБРОШЕННЫЙ)", Planted(), true, 0);
                    foreach (KeyValuePair<string, Func<object>> scene in Scenes())
                    {
                        Scene(culture, scene.Key, scene.Value);
                    }
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = prev;
                }
            }
        }
        catch (Exception ex)
        {
            Say("!! проба сорвалась: " + ex);
            ModalWatchStop();
            Dump(outPath);
            return Leave(3);
        }

        Mechanism();
        Verdict();
        ModalWatchStop();
        Say("");
        Say("модальных окон за прогон: " + modalSeen + " (обязано быть 0; контроль сторожа — ключ --modal-control)");
        Dump(outPath);

        return Leave(bad == 0 ? 0 : 1);
    }

    [DllImport("kernel32.dll")]
    static extern void ExitProcess(uint code);

    /// <summary>
    /// ⛔ ВЫХОД ЖЁСТКИЙ, И ЭТО НЕ ЛЕНЬ. Проба заводит двенадцать форм подряд
    /// без `Application.Run`; закрытие процесса ШТАТНЫМ путём (возврат из
    /// `Main` или `Environment.Exit`) роняет его кодом 0xC0000409 УЖЕ ПОСЛЕ
    /// того, как напечатаны все числа и записана выгрузка. Замер при этом
    /// верен, а код возврата читается как отказ — то есть приёмка судила бы
    /// не то. Проверено: путь `--modal-control`, где форм нет, выходит 0
    /// штатно; отказ от `Dispose` сцен падения не убрал.
    ///
    /// ⚠ Печать к этому месту уже вылита: `Console.Out` пишет без задержки,
    /// а выгрузка закрыта `File.WriteAllText`.
    /// </summary>
    static int Leave(int code)
    {
        try { Console.Out.Flush(); Console.Error.Flush(); } catch (Exception) { }
        ExitProcess((uint)code);
        return code;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СЦЕНЫ — кто заводит поля со стрелками
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Девять форм строки `A261` плюс три места, которых её перепись не
    /// назвала: панель калибровки в панели инструментов, обёртка
    /// `ToolStripNumericUpdown` (её поле живёт в панели спектра) и форма
    /// матрицы отклика, которая строит свои поля кодом, а не дизайнером.
    /// ⚠ `MainForm` не заводится нарочно — виды берут её ссылку и не трогают
    /// в построении; окно приложения при этом не поднимается.
    /// </summary>
    static IEnumerable<KeyValuePair<string, Func<object>>> Scenes()
    {
        yield return S("AudioInputDeviceForm", () => new AudioInputDeviceForm());
        yield return S("DCCountRateView", () => new DCCountRateView(Shell()));
        yield return S("DCEnergyCalibrationView", () => new DCEnergyCalibrationView(Shell()));
        yield return S("DCPeakDetectionView", () => new DCPeakDetectionView(Shell()));
        yield return S("DCSampleInfoView", () => new DCSampleInfoView(Shell()));
        yield return S("DeviceConfigForm", () => new DeviceConfigForm());
        yield return S("EfficiencyMakerForm", () => new EfficiencyMakerForm());
        yield return S("GlobalConfigForm", () => new GlobalConfigForm());
        yield return S("ROIConfigForm", () => new ROIConfigForm());
        yield return S("ToolStripEnergyCalibrationControl", () => new ToolStripEnergyCalibrationControl());
        yield return S("ToolStripNumericUpdown", () => new ToolStripNumericUpdown());
        yield return S("ResponseMatrixForm", () => new ResponseMatrixForm(new EfficiencyConfigData()));
    }

    static KeyValuePair<string, Func<object>> S(string name, Func<object> make)
    {
        return new KeyValuePair<string, Func<object>>(name, make);
    }

    /// <summary>
    /// Одиночки в том же порядке, в каком их заводит `MainForm` — окно при
    /// этом не поднимается. ⚠ Обе карты примитивов ROI обязаны быть заполнены
    /// ДО менеджеров-одиночек, иначе `ROIConfigManager` грузит ноль конфигураций.
    /// </summary>
    static MainForm shell;

    /// <summary>
    /// Оболочка для панелей «DC»: их конструкторы читают активный документ, и
    /// без неё три вида падают `NullReferenceException` (замерено). ⛔ Окно не
    /// показывается — заводится только объект; `Application.Run` не зовётся.
    /// </summary>
    static MainForm Shell()
    {
        if (shell == null) shell = new MainForm();
        return shell;
    }

    static void Bootstrap()
    {
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
        GlobalConfigManager.GetInstance().PrepareConfigFile();
        DeviceConfigManager.GetInstance();
        ROIConfigManager.GetInstance();
        NuclideDefinitionManager.GetInstance();
        Say("одиночки заведены: приборы, термометры, ROI, конфигурация, нуклиды");
    }

    /// <summary>Голый контрол, подброшенный рядом с полями форм.</summary>
    static NumericUpDown Planted()
    {
        NumericUpDown n = new NumericUpDown();
        n.Name = "подброшенный";
        n.Minimum = 0m;
        n.Maximum = 100m;
        n.DecimalPlaces = 2;
        n.Value = 0.5m;
        return n;
    }

    static void Scene(string culture, string name, Func<object> make)
    {
        object built;
        try
        {
            built = make();
        }
        catch (Exception ex)
        {
            scenesFailed++;
            bad++;
            Say("");
            Say("⛔ " + name + ": не строится — " + ex.GetType().Name + ": " + Short(ex.Message));
            return;
        }

        scenesBuilt++;
        List<NumericUpDown> found = new List<NumericUpDown>();
        try
        {
            Control host = built as Control;
            if (host != null) Walk(host, found);
            ToolStripControlHost item = built as ToolStripControlHost;
            if (item != null && item.Control != null) Walk(item.Control, found);
        }
        catch (Exception ex)
        {
            bad++;
            Say("⛔ " + name + ": обход не удался — " + ex.GetType().Name);
        }

        Say("");
        Say("  " + name + ": полей со стрелками — " + found.Count);
        for (int i = 0; i < found.Count; i++)
        {
            MeasureField(culture, name, found[i], false, i);
        }

        // ⚠ Форма НЕ разбирается нарочно. `Dispose` полусобранной формы (окна
        //   у неё нет, `Application.Run` не звался) роняет процесс кодом
        //   0xC0000409 уже ПОСЛЕ печати всех чисел — замер верен, а код
        //   возврата читается как отказ. Прогон короткий, память отдаст выход.
        GC.KeepAlive(built);
    }

    static void Walk(Control c, List<NumericUpDown> acc)
    {
        NumericUpDown n = c as NumericUpDown;
        if (n != null) acc.Add(n);

        ToolStrip ts = c as ToolStrip;
        if (ts != null)
        {
            foreach (ToolStripItem it in ts.Items) WalkItem(it, acc);
        }

        foreach (Control child in c.Controls) Walk(child, acc);
    }

    static void WalkItem(ToolStripItem it, List<NumericUpDown> acc)
    {
        ToolStripControlHost h = it as ToolStripControlHost;
        if (h != null && h.Control != null) Walk(h.Control, acc);

        ToolStripDropDownItem d = it as ToolStripDropDownItem;
        if (d != null && d.HasDropDownItems)
        {
            foreach (ToolStripItem sub in d.DropDownItems) WalkItem(sub, acc);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ЗАМЕР ОДНОГО ПОЛЯ — печать, разбор, набор
    // ══════════════════════════════════════════════════════════════════════

    // Плечо `en-US` — эталон: под ним культура печатает и разбирает так же,
    // как инвариант, и любое расхождение русского плеча с ним есть цена
    // культуры потока, а не логики формы.
    static readonly Dictionary<string, string> EnPrint = new Dictionary<string, string>();
    static readonly Dictionary<string, string> EnParse = new Dictionary<string, string>();

    static void MeasureField(string culture, string scene, NumericUpDown n, bool planted, int ordinal)
    {
        string name = string.IsNullOrEmpty(n.Name) ? "(без имени)" : n.Name;
        string type = n.GetType().Name;
        int dp = n.DecimalPlaces;
        string key = scene + "#" + ordinal.ToString(CultureInfo.InvariantCulture);

        Seen[culture] = Seen[culture] + 1;
        // Тип считается ОДИН раз (на русском заходе): формы строятся дважды,
        // и суммарное число вводило бы в заблуждение вдвое.
        if (!planted && type != Invariant && culture == "ru-RU") notInvariantType++;

        // 1. ПЕЧАТЬ — то, что стоит в поле сразу после построения формы.
        string printed = n.Text ?? "";
        bool commaPrint = printed.IndexOf(',') >= 0;
        if (commaPrint) CommaPrint[culture] = CommaPrint[culture] + 1;

        // 2. РАЗБОР — число с ТОЧКОЙ, набранное человеком.
        decimal want = TestValue(n);
        string typed = want.ToString("F" + dp.ToString(CultureInfo.InvariantCulture),
                                     CultureInfo.InvariantCulture);
        string parsed;
        try
        {
            n.Text = typed;          // так же, как это делает набор с клавиатуры
            parsed = n.Value.ToString(CultureInfo.InvariantCulture); // геттер обязан разобрать
        }
        catch (Exception ex)
        {
            parsed = "ОТКАЗ " + ex.GetType().Name;
        }

        // 3. НАБОР — доходит ли точка до поля вообще.
        bool dotEaten = DotIsEaten(n);
        if (dotEaten) DotEaten[culture] = DotEaten[culture] + 1;

        // ⚠ КРИТЕРИЙ РАЗБОРА — НЕ «вышло не то, что подано». Форма вправе
        //   поправить значение своей логикой: `DeviceConfigForm.numericUpDown13`
        //   держит себя больше соседа и на поданное «50» честно отвечает «51»
        //   на ОБЕИХ культурах. Такое расхождение к культуре отношения не имеет,
        //   и первый заход пробы записал его в дефекты зря. Судится разница
        //   МЕЖДУ ПЛЕЧАМИ: что вышло под `ru-RU` против того же поля под `en-US`.
        bool printDiff = false, parseDiff = false;
        if (culture == "en-US")
        {
            EnPrint[key] = printed;
            EnParse[key] = parsed;
        }
        else
        {
            string wasPrint, wasParse;
            printDiff = EnPrint.TryGetValue(key, out wasPrint) && wasPrint != printed;
            parseDiff = EnParse.TryGetValue(key, out wasParse) && wasParse != parsed;
        }
        if (parseDiff) ParseWrong[culture] = ParseWrong[culture] + 1;

        Rows.Add(string.Join("	", new[]
        {
            culture, scene, name, type, dp.ToString(CultureInfo.InvariantCulture),
            n.ThousandsSeparator ? "групп" : "-",
            printed, typed, parsed,
            commaPrint ? "ЗАПЯТАЯ" : "-",
            printDiff ? "ПЕЧАТЬ≠en" : "-",
            parseDiff ? "РАЗБОР≠en" : "-",
            dotEaten ? "ТОЧКА-СЪЕДЕНА" : "-"
        }));

        if (planted || commaPrint || printDiff || parseDiff || dotEaten)
        {
            Say(string.Format(
                "    {0,-34} {1,-28} dp={2} печать «{3}»  подано «{4}» → {5}{6}{7}{8}{9}",
                scene, name, dp, printed, typed, parsed,
                commaPrint ? "  ⛔ЗАПЯТАЯ" : "",
                printDiff ? "  ⛔ПЕЧАТЬ≠en" : "",
                parseDiff ? "  ⛔РАЗБОР≠en" : "",
                dotEaten ? "  ⛔ТОЧКА СЪЕДЕНА" : ""));
        }
    }

    /// <summary>
    /// Число, которое заведомо влезает в диапазон поля и заведомо несёт
    /// разделитель дробной части, если у поля есть разряды после точки.
    /// </summary>
    static decimal TestValue(NumericUpDown n)
    {
        decimal lo = n.Minimum, hi = n.Maximum;
        if (hi < lo) { decimal t = lo; lo = hi; hi = t; }
        // ⚠ Потолок у части полей — `decimal.MaxValue`, и середина диапазона
        //   давала бы число в тридцать знаков: читать такую строку нельзя, а
        //   мерить она мерит то же самое. Окно замера ограничено сверху.
        if (hi > lo + 1000m) hi = lo + 1000m;
        decimal mid = lo + (hi - lo) / 2m;
        int dp = n.DecimalPlaces;
        decimal unit = 1m;
        for (int i = 0; i < dp; i++) unit /= 10m;

        decimal x = Math.Round(mid, dp, MidpointRounding.AwayFromZero);
        if (dp > 0)
        {
            // Дробная часть обязана быть ненулевой — иначе точка в поданном
            // тексте не появится и разбор мерить нечем.
            x = Math.Truncate(x) + unit;
            if (x > hi) x = Math.Truncate(hi) - unit;
            if (x < lo) x = Math.Truncate(lo) + unit;
        }
        if (x > hi) x = hi;
        if (x < lo) x = lo;
        return Math.Round(x, dp, MidpointRounding.AwayFromZero);
    }

    static readonly MethodInfo KeyPress = typeof(NumericUpDown).GetMethod(
        "OnTextBoxKeyPress", BindingFlags.Instance | BindingFlags.NonPublic,
        null, new[] { typeof(object), typeof(KeyPressEventArgs) }, null);

    /// <summary>Съедает ли фильтр знаков набранную ТОЧКУ.</summary>
    static bool DotIsEaten(NumericUpDown n)
    {
        if (KeyPress == null)
        {
            bad++;
            Say("⛔ OnTextBoxKeyPress отражением не найден — сторона НАБОРА не измерена");
            return false;
        }
        KeyPressEventArgs e = new KeyPressEventArgs('.');
        try { KeyPress.Invoke(n, new object[] { n, e }); }
        catch (Exception ex) { Say("⛔ фильтр знаков сорвался: " + ex.GetType().Name); return false; }
        return e.Handled;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ОСТАЛЬНЫЕ СТОРОНЫ ПРИЁМА — стрелки и набранная запятая
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Печать и разбор — не всё. Считаются ещё два места, где база берёт
    /// культуру потока, и оба меряются на ОДНОМ поле, заведённом здесь же:
    ///
    ///   * СТРЕЛКА после набранного «0.5» — база разбирает поле СВОИМ закрытым
    ///     `ParseEditText`, и на русской системе прибавка шла бы не к 0.5;
    ///   * НАБРАННАЯ ЗАПЯТАЯ на русской раскладке — приём обязан её пропускать
    ///     и читать как дробную часть (домашнее правило `UserNumber`,
    ///     ~~`A244`~~: печатаем всегда одинаково, читаем терпимо), а на
    ///     печать всё равно выводить точку.
    ///
    /// ⛔ Каждое плечо идёт ДВАЖДЫ: на общем поле и на подброшенном голом.
    ///    Разница между ними и есть цена приёма; совпадение значило бы, что
    ///    мерить нечего.
    /// </summary>
    static void Mechanism()
    {
        Head("СТРЕЛКИ И НАБРАННАЯ ЗАПЯТАЯ (культура потока ru-RU)");
        Type inv = typeof(DeviceConfigForm).Assembly.GetType("BecquerelMonitor." + Invariant);
        CultureInfo prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            Arm("подброшенный (голый)", Planted());
            if (inv == null)
            {
                Say("  общего типа в сборке нет — второе плечо не ставится (замер «до»)");
            }
            else
            {
                NumericUpDown n = (NumericUpDown)Activator.CreateInstance(inv);
                n.Minimum = 0m; n.Maximum = 100m; n.DecimalPlaces = 2;
                n.Increment = 1m; n.Value = 0.5m;
                Arm("общий " + Invariant, n);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    static void Arm(string title, NumericUpDown n)
    {
        Say("");
        Say("  " + title + ":");

        // 1. Стрелка вверх после набранного «0.5» при шаге 1.
        // ⚠ Прежнее значение нарочно ДАЛЕКО от набранного: при 0.5 оба плеча
        //   давали 1.5 — голое потому, что разбор отказал и прибавка легла на
        //   старое значение, а общее потому, что разбор прошёл. Совпадение
        //   выглядело как «разницы нет».
        n.Value = 9m;
        n.Text = "0.5";
        n.UpButton();
        Say("     набрано «0.5», стрелка вверх (шаг " + n.Increment.ToString(CultureInfo.InvariantCulture)
            + ") → значение " + n.Value.ToString(CultureInfo.InvariantCulture)
            + ", в поле «" + n.Text + "»");

        // 2. Русская запятая: доходит ли до поля и как читается.
        bool commaEaten = true;
        if (KeyPress != null)
        {
            KeyPressEventArgs e = new KeyPressEventArgs(',');
            try { KeyPress.Invoke(n, new object[] { n, e }); commaEaten = e.Handled; }
            catch (Exception) { }
        }
        n.Text = "0,5";
        string readComma = n.Value.ToString(CultureInfo.InvariantCulture);
        Say("     набранная запятая: " + (commaEaten ? "СЪЕДЕНА фильтром" : "пропущена")
            + "; «0,5» прочтено как " + readComma + ", в поле «" + n.Text + "»");

        // 3. Разделитель разрядов — его нет вовсе (`A244`).
        n.Text = "1.5";
        Say("     набрано «1.5» → " + n.Value.ToString(CultureInfo.InvariantCulture)
            + ", в поле «" + n.Text + "»");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ПРИГОВОР
    // ══════════════════════════════════════════════════════════════════════

    static void Verdict()
    {
        Head("ИТОГ");
        Say("сцен построено: " + scenesBuilt + ", не построилось: " + scenesFailed);
        Say("полей формы НЕ общего типа " + Invariant + ": " + notInvariantType
            + " (после правки обязано быть 0)");

        // ⛔ Положительный контроль: подброшенный обязан быть найден по всем
        //    трём признакам на РУССКОЙ культуре. Чистый подброшенный значит,
        //    что проба не мерит ничего, — и это отказ сам по себе.
        int plantedHits = 0;
        foreach (string row in Rows)
        {
            string[] f = row.Split('	');
            if (f[0] != "ru-RU" || f[1] != "(ПОДБРОШЕННЫЙ)") continue;
            if (f[9] != "-") plantedHits++;   // запятая в печати
            if (f[11] != "-") plantedHits++;  // разбор разошёлся с en-US
            if (f[12] != "-") plantedHits++;  // точка съедена при наборе
        }
        Say("");
        if (plantedHits == 3)
        {
            Say("положительный контроль: подброшенный голый NumericUpDown найден "
                + "по всем трём признакам (печать, разбор, набор) — проба мерит");
        }
        else
        {
            bad++;
            Say("⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НЕ СРАБОТАЛ: подброшенный дал "
                + plantedHits + " признака из 3 — замер ничего не значит");
        }

        // Приговор — по полям ФОРМ; подброшенный в него не входит, он обязан
        // быть плохим по построению.
        int enComma = 0, ruComma = 0, printDiff = 0, parseDiff = 0, dot = 0, fields = 0;
        foreach (string row in Rows)
        {
            string[] f = row.Split('	');
            if (f[1] == "(ПОДБРОШЕННЫЙ)") continue;
            if (f[0] == "en-US")
            {
                if (f[9] != "-") enComma++;
                continue;
            }
            fields++;
            if (f[9] != "-") ruComma++;
            if (f[10] != "-") printDiff++;
            if (f[11] != "-") parseDiff++;
            if (f[12] != "-") dot++;
        }

        Say("");
        Say("ПОЛЯ ФОРМ, найдено: " + fields);
        Say("  en-US: с запятой в печати " + enComma
            + " (запятая под английской культурой — это ГРУППИРОВКА разрядов, `A244`)");
        Say("  ru-RU: с запятой в печати " + ruComma
            + ", печать разошлась с en-US " + printDiff
            + ", разбор разошёлся с en-US " + parseDiff
            + ", съело набранную точку " + dot);
        if (enComma + ruComma + printDiff + parseDiff + dot + notInvariantType > 0) bad++;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Печать и выгрузка
    // ══════════════════════════════════════════════════════════════════════

    static void Head(string title)
    {
        Say("");
        Say("══ " + title + " " + new string('═', Math.Max(3, 66 - title.Length)));
    }

    static void Say(string line)
    {
        Console.WriteLine(line);
        Log.AppendLine(line);
    }

    static string Short(string s)
    {
        if (s == null) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }

    static void Dump(string outPath)
    {
        if (outPath == null) return;
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            StringBuilder sb = new StringBuilder();
            sb.Append(Log.ToString());
            sb.AppendLine();
            sb.AppendLine("══ ТАБЛИЦА ══");
            sb.AppendLine("культура\tсцена\tимя\tтип\tdp\tгруппы\tпечать\tподано\tразобрано"
                          + "\tзапятая\tпечать≠en\tразбор≠en\tнабор");
            foreach (string row in Rows) sb.AppendLine(row);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("выгрузка: " + Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ выгрузка не удалась: " + ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (приём полосы F20, `A245`)
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
                    bad++;
                    Say("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                        + "» — сторож закрывает его сам; нажать «ОК» здесь некому");
                    try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception) { }
                }

                Thread.Sleep(200);
            }
        });
        modalWatchThread.IsBackground = true;
        modalWatchThread.Start();
    }

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

    /// <summary>Положительный контроль САМОГО сторожа окон.</summary>
    static void ModalControl()
    {
        Head("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
        Thread th = new Thread(delegate()
        {
            MessageBox.Show("контрольное окно полосы F68", "контроль", MessageBoxButtons.OK);
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        bool closed = th.Join(20000);
        Say(closed
            ? "окно закрыто сторожем"
            : "⛔ окно не закрылось за 20 с — сторож не работает");
        if (!closed) bad++;
    }
}
