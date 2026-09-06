// `A244`, доля П8 «остальное». Полоса F28 захода 05.09.2026.
//
//     restcultureprobef28 [--out=<файл>] [--modal-control]
//
// ЧТО МЕРЯЕТСЯ. Правило Amber 05.09.2026: разделитель дробной части у чисел
// ВСЕГДА ТОЧКА, печать — строго инвариантом, чтение поля — терпимо (сначала
// инвариантом, потом культурой системы), группировки разрядов нет вовсе.
// Проба берёт места доли П8 (всё, что не вошло в доли П1–П7 и П8а) и судит
// их на трёх системных культурах: `ru-RU`, `de-DE`, `en-US`.
//
// ⛔ КОСТЫЛЬ `MainForm.cs:158-160` (клон культуры с подменённым разделителем)
//    ЗДЕСЬ НЕ ДЕЙСТВУЕТ, и это нарочно: окно не поднимается, `MainForm` не
//    строится, культура потока выставляется присваиванием. Иначе проба
//    показывала бы точку, которую ставит костыль, а не правку. Проверяется
//    ЯВНО: у каждой культуры печатается её настоящий разделитель.
//
// ⛔ СУДИТСЯ ВИД СТРОКИ, А НЕ РАВЕНСТВО ПЛЕЧ. Доля П7 прошла приёмку с
//    непереведённой группировкой ровно потому, что запятая в группах
//    одинакова на всех культурах и плечи не разводит. Поэтому каждый снимок
//    сверяется с ЭТАЛОНОМ-ИНВАРИАНТОМ посимвольно, а отдельным пунктом
//    ищется разделитель групп в самих строках.
//
// ⛔ ПЛЕЧО ПО ЧИСЛАМ ОТКАТА НЕ ЛОВИТ (измерено полосами F17/F20/F25):
//    `UserNumber` читает терпимо и напечатанное по ошибке «661,5» берёт
//    обратно — две ошибки гасят друг друга. Поэтому обратные плечи здесь
//    сверяют И ЧИСЛО, И ТЕКСТ поля.
//
// ⛔ Окно `BecqMoni` не запускается. Модальное окно на безоконном пути вешает
//    прогон насмерть (`A245`), поэтому первым делом поднимается сторож окон —
//    перечисляет окна процесса, называет текст, засчитывает расхождение и
//    закрывает. Положительный контроль сторожа — ключ `--modal-control`.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.NucBase;
using BecquerelMonitor.Properties;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

static class RestCultureProbeF28
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    static readonly string[] Foreign = { "ru-RU", "de-DE", "en-US" };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outPath = null;
        bool modalControl = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a == "--modal-control") modalControl = true;
            // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание. ⚠ Перепись
            // F70 числила эту пробу ОТКАЗЫВАЮЩЕЙ по слову «Неизвестный» в
            // комментарии про тип распада — слово было, ветки не было.
            else
            {
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }
        }

        ModalWatchStart();

        if (modalControl)
        {
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
        //    иначе первый же путь через `DocumentManager` встанет на окне.
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

        Header();

        try
        {
            Run();
        }
        catch (Exception ex)
        {
            failures++;
            Say("⛔ ПРОГОН ОБОРВАН: " + ex.GetType().Name + ": " + ex.Message);
            Say(ex.StackTrace ?? "");
        }

        ModalWatchStop();
        Say("");
        Say("модальных окон за прогон: " + modalSeen
            + (modalSeen == 0 ? "" : "  ⛔ (каждое засчитано расхождением)"));
        Say(failures == 0 ? "СОШЛОСЬ, расхождений 0" : "⛔ НЕ СОШЛОСЬ: " + failures);
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    static void Header()
    {
        Assembly app = typeof(GlobalConfigManager).Assembly;
        Say("== `A244` доля П8 «остальное»: число печатается и разбирается ТОЧКОЙ ==");
        Say("");
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                          .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }
        Say("проба собрана:     " + File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location)
                                       .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        Say("целевая платформа входной сборки: "
            + (AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "⛔ НЕ ОБЪЯВЛЕНА"));
        Say("культура ОС:       " + CultureInfo.InstalledUICulture.Name
            + "; культура потока при старте: " + CultureInfo.CurrentCulture.Name
            + ", её разделитель дробной части «"
            + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + "»");
        Say("⛔ костыль `MainForm` (клон культуры с подменённым разделителем) НЕ ПРИМЕНЁН:"
            + " окно не поднимается, `MainForm` не строится.");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СНИМОК: все места доли П8, дающие строку с числом.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Один слот печати: имя и полученная строка.</summary>
    sealed class Slot
    {
        public string Name;
        public string Text;
        public Slot(string name, string text) { Name = name; Text = text; }
    }

    static string Safe(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) { return "⛔ЗАПУСК:" + ex.GetType().Name + ":" + ex.Message; }
    }

    /// <summary>Все слоты ПЕЧАТИ доли П8, снимаемые на текущей культуре потока.</summary>
    static List<Slot> PrintSlots()
    {
        var s = new List<Slot>();

        // 1–2. `PolynomialEnergyCalibration.ToString()` — окно настроек прибора
        //      показывает этой строкой формулу энергетической шкалы (F17 назвала
        //      место прицельно: `PolynomialEnergyCalibration.cs:433,444`).
        s.Add(new Slot("poly.ToString дробный", Safe(delegate
        {
            var c = new PolynomialEnergyCalibration();
            c.PolynomialOrder = 2;
            c.Coefficients = new double[] { 1.25, 2.5, 0.00123 };
            return c.ToString();
        })));
        s.Add(new Slot("poly.ToString ≥1000", Safe(delegate
        {
            var c = new PolynomialEnergyCalibration();
            c.PolynomialOrder = 1;
            c.Coefficients = new double[] { 1234.5, 1000.25 };
            return c.ToString();
        })));

        // 3–5. Модели ПШПВ — та же строка формулы в окне настроек.
        s.Add(new Slot("SqrtFwhm.ToString", Safe(delegate
        {
            var c = new SqrtFwhmCalibration();
            c.Coefficients = new double[] { 1234.5, 0.5, 0.00025 };
            return c.ToString();
        })));
        s.Add(new Slot("SimpleSqrtFwhm.ToString", Safe(delegate
        {
            var c = new SimpleSqrtFwhmCalibration();
            c.Coefficients = new double[] { 1234.5, 0.5 };
            return c.ToString();
        })));
        s.Add(new Slot("PowerFwhm.ToString", Safe(delegate
        {
            var c = new PowerFwhmCalibration();
            c.Coefficients = new double[] { 1234.5, 0.5 };
            return c.ToString();
        })));

        // 6–7. Мощность дозы — СТРОКА СОСТОЯНИЯ главного окна.
        s.Add(new Slot("DoseRate.ToString", Safe(delegate
        {
            var d = new DoseRate();
            d.Rate = 0.5;
            d.Error = 0.125;
            d.Coverage = 1.0;
            return d.ToString();
        })));
        s.Add(new Slot("DoseRate приписка покрытия", Safe(delegate
        {
            var d = new DoseRate();
            d.Rate = 1234.5;      // > 1000 → мЗв/ч, показание 1.234
            d.Error = 12.25;
            d.Coverage = 0.5;
            return d.ToString();
        })));

        // 8. Отказ коэффициентов дозы: числа едут в тексте отказа.
        s.Add(new Slot("DoseRate отказ вне таблицы", Safe(delegate
        {
            try
            {
                DoseRateCoefficients.AmbientDoseConversion(1.0e9);
                return "⛔ ОТКАЗА НЕ БЫЛО";
            }
            catch (DoseRateRefusalException ex) { return ex.Message; }
        })));

        // 9. Подпись нуклида в списках и выпадающих меню.
        s.Add(new Slot("NuclideDefinition.ToString", Safe(delegate
        {
            var n = new NuclideDefinition();
            n.Name = "Cs-137";
            n.Energy = 1234.5;
            return n.ToString();
        })));

        // 10. Полоса хода набора — надпись поверх полосы.
        s.Add(new Slot("PercentageProgressBar.Text", Safe(delegate
        {
            var bar = new PercentageProgressBar();
            bar.setDateFormat(false);
            bar.Maximum = 2000;   // иначе `base.Value = 1234` выходит за предел полосы
            bar.PriorText = "3600";
            bar.DoubleValue = 1234.5;
            return bar.Text;
        })));

        // 10а. Та же надпись, но ветка «суток больше нуля»: она печатает
        //      длительность форматом ИЗ РЕСУРСА (`d\д\ hh\:mm\:ss` в русской
        //      паре), и до полосы F34 звалась без культуры вовсе. Слот нужен
        //      затем, что ветка `hh\:mm\:ss` из слота 10 её НЕ ЗАДЕВАЕТ:
        //      93784.5 с = 1 сутки 02:03:04.
        s.Add(new Slot("PercentageProgressBar срок > суток", Safe(delegate
        {
            var bar = new PercentageProgressBar();
            bar.setDateFormat(true);
            bar.Maximum = 2000;
            bar.PriorText = "93784.5";
            bar.DoubleValue = 1234.5;
            return bar.Text;
        })));

        // 11. Длительность в файле N42 (`PT…S`) — приватная статика.
        s.Add(new Slot("N42 длительность PT…S", Safe(delegate
        {
            MethodInfo m = FindStatic("BecquerelMonitor.N42.Util", "N42Duration");
            return (string)m.Invoke(null, new object[] { 3600.3 });
        })));

        // 12. Имя изготовителя звукового устройства — число уходит в XPath.
        s.Add(new Slot("WaveIn изготовитель", Safe(delegate
        {
            MethodInfo m = FindStatic("WinMM.WaveIn", "GetManufacturer");
            return (string)m.Invoke(null, new object[] { (ushort)1234 });
        })));

        // 13–14. Неизвестный тип распада печатается ЧИСЛОМ.
        s.Add(new Slot("Decay.DecayTypeString", Safe(delegate
        {
            var d = new Decay();
            d.DecayType = 1234;
            return d.DecayTypeString;
        })));
        s.Add(new Slot("DecayRad.DecayTypeString", Safe(delegate
        {
            var d = new DecayRad();
            d.DecayType = 1234;
            return d.DecayTypeString;
        })));

        // 15. Отказ снять копию спектра — число каналов в тексте.
        s.Add(new Slot("EnergySpectrum.Clone отказ", Safe(delegate
        {
            var sp = new EnergySpectrum(1.0, 4096);
            sp.EnergyCalibration = null;
            try
            {
                sp.Clone();
                return "⛔ ОТКАЗА НЕ БЫЛО";
            }
            catch (InvalidOperationException ex) { return ex.Message; }
        })));

        // 16. Отказ обратить энергетическую шкалу: `PolynomialEnergyCalibration`
        //     печатает энергию и ВСЕ коэффициенты (`string.Join` + `ToString("R")`).
        s.Add(new Slot("poly отказ обращения", Safe(delegate
        {
            var c = new PolynomialEnergyCalibration();
            c.PolynomialOrder = 2;
            c.Coefficients = new double[] { 1234.5, 0.0, 0.0 };
            try
            {
                c.EnergyToChannel(1234.5, maxCh: 8192);
                return "(отказа нет: путь без окон отдал число)";
            }
            catch (InvalidOperationException ex) { return ex.Message; }
        })));

        // 17. Имя нового документа: `int` уходит в `object[]` и печатается
        //     самой склейкой `string.Concat` — сканеру это место НЕ ВИДНО.
        s.Add(new Slot("имя нового документа", Safe(delegate
        {
            object serial = 1234;
            return string.Concat(new object[] { "New", " (", serial, ").xml" });
        })));

        return s;
    }

    static MethodInfo FindStatic(string typeName, string method)
    {
        Type t = typeof(GlobalConfigManager).Assembly.GetType(typeName, true);
        MethodInfo m = t.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic
                                           | BindingFlags.Public);
        if (m == null) throw new MissingMethodException(typeName + "." + method);
        return m;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ОБРАТНОЕ ПЛЕЧО: поля, которые ЧИТАЮТСЯ обратно.
    //  Сверяется И ЧИСЛО, И ТЕКСТ поля — плечо по одним числам откат не ловит.
    // ══════════════════════════════════════════════════════════════════════

    sealed class ReadBack
    {
        public string Name;
        public string Typed;     // что «набрано» в поле
        public string Text;      // что в поле стоит после набора
        public string Value;     // что вернул разбор
        public string Want;      // ожидаемое значение
    }

    static List<ReadBack> ParseSlots()
    {
        var list = new List<ReadBack>();

        list.Add(Try("SpectrumCutOffDialog энергия", "661.5", "661.5", delegate(string typed, out string text)
        {
            var f = new SpectrumCutOffDialog();
            Set(f, "energyradioButton", "Checked", true);
            Set(f, "energytextBox", "Text", typed);
            text = (string)Get(f, "energytextBox", "Text");
            object r = f.GetType().GetMethod("SendData").Invoke(f, null);
            object val = r.GetType().GetField("Item3").GetValue(r);
            f.Dispose();
            return Convert.ToString(val, CultureInfo.InvariantCulture);
        }));

        list.Add(Try("SpectrumCutOffDialog канал", "4096", "4096", delegate(string typed, out string text)
        {
            var f = new SpectrumCutOffDialog();
            Set(f, "energyradioButton", "Checked", false);
            Set(f, "channeltextBox", "Text", typed);
            text = (string)Get(f, "channeltextBox", "Text");
            object r = f.GetType().GetMethod("SendData").Invoke(f, null);
            object val = r.GetType().GetField("Item4").GetValue(r);
            f.Dispose();
            return Convert.ToString(val, CultureInfo.InvariantCulture);
        }));

        list.Add(Try("ChanNumberChangeDialog каналы", "8192", "8192", delegate(string typed, out string text)
        {
            var f = new ChanNumberChangeDialog();
            Set(f, "textBox1", "Text", typed);
            f.GetType().GetMethod("button1_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                       .Invoke(f, new object[] { null, EventArgs.Empty });
            text = (string)Get(f, "textBox1", "Text");
            object val = f.GetType().GetMethod("SendData").Invoke(f, null);
            f.Dispose();
            return Convert.ToString(val, CultureInfo.InvariantCulture);
        }));

        // Пара «печать → разбор» внутри одного вида: секунды кладёт в поле
        // `DCControlPanel` инвариантом, читает их обратно `ToDateFmt`.
        list.Add(Try("PercentageProgressBar срок", "3600", "01:00:00 (50.0%)", delegate(string typed, out string text)
        {
            var bar = new PercentageProgressBar();
            bar.setDateFormat(true);
            bar.PriorText = typed;
            bar.DoubleValue = 50.0;
            text = bar.PriorText;
            return bar.Text;
        }));

        return list;
    }

    delegate string ReadBackRun(string typed, out string text);

    static ReadBack Try(string name, string typed, string want, ReadBackRun run)
    {
        var rb = new ReadBack { Name = name, Typed = typed, Want = want };
        try
        {
            string text;
            rb.Value = run(typed, out text);
            rb.Text = text;
        }
        catch (Exception ex)
        {
            rb.Value = "⛔ЗАПУСК:" + ex.GetType().Name + ":" + Innermost(ex);
            rb.Text = "";
        }
        return rb;
    }

    static string Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    static void Set(object form, string field, string prop, object value)
    {
        object ctl = Field(form, field);
        ctl.GetType().GetProperty(prop).SetValue(ctl, value, null);
    }

    static object Get(object form, string field, string prop)
    {
        object ctl = Field(form, field);
        return ctl.GetType().GetProperty(prop).GetValue(ctl, null);
    }

    static object Field(object form, string name)
    {
        Type t = form.GetType();
        while (t != null)
        {
            FieldInfo fi = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                                            | BindingFlags.Public);
            if (fi != null) return fi.GetValue(form);
            t = t.BaseType;
        }
        throw new MissingFieldException(form.GetType().Name + "." + name);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ПРОГОН
    // ══════════════════════════════════════════════════════════════════════

    static void Run()
    {
        // Эталон — на ИНВАРИАНТЕ; он и есть мерка вида.
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        List<Slot> reference = PrintSlots();
        List<ReadBack> refParse = ParseSlots();

        Say("");
        Say("══════════════════════════════════════════════════════════════");
        Say("ЭТАЛОН (культура потока = инвариант). Слотов печати: " + reference.Count
            + ", слотов ввода: " + refParse.Count);
        Say("══════════════════════════════════════════════════════════════");
        foreach (Slot sl in reference)
        {
            Say("  " + Pad(sl.Name) + " = «" + sl.Text + "»");
            if (sl.Text.StartsWith("⛔", StringComparison.Ordinal))
            {
                failures++;
                Say("     ⛔ слот не снят — мерить нечем");
            }
        }
        foreach (ReadBack rb in refParse)
        {
            Say("  [ввод] " + Pad(rb.Name) + " набрано «" + rb.Typed
                + "», в поле «" + rb.Text + "», разбор дал «" + rb.Value + "»");
            if (rb.Value.StartsWith("⛔", StringComparison.Ordinal)) { failures++; }
        }

        // ── Отдельный пункт: РАЗДЕЛИТЕЛЯ ГРУПП В ЭТАЛОНЕ НЕТ ВОВСЕ.
        //    Решение Amber: группировки разрядов нет; инвариант САМ даёт
        //    «1,234.50», и равенство плеч этого не ловит — ловит только
        //    осмотр самой строки.
        Say("");
        Say("── группировка разрядов в эталоне (решение Amber: её нет вовсе) ──");
        int grouped = 0;
        foreach (Slot sl in reference)
        {
            if (HasGrouping(sl.Text))
            {
                grouped++;
                failures++;
                Say("  ⛔ РАЗДЕЛИТЕЛЬ ГРУПП: " + sl.Name + " = «" + sl.Text + "»");
            }
        }
        Say("  слотов с разделителем групп: " + grouped + (grouped == 0 ? " — так и надо" : ""));

        string[] crossPrint = null;   // тексты, снятые ПЕРВЫМ чужим плечом

        foreach (string name in Foreign)
        {
            Say("");
            Say("──────────────────────────────────────────────────────────────");
            Say("ПЛЕЧО: системная культура потока " + name + " (подмены разделителя НЕТ)");
            Say("──────────────────────────────────────────────────────────────");

            CultureInfo os = CultureInfo.GetCultureInfo(name);
            Thread.CurrentThread.CurrentCulture = os;

            // ── Положительный контроль плеча: без него «всё сошлось» значило бы
            //    только «культура не выставилась».
            string bare = (1.5).ToString();
            string want = os.NumberFormat.NumberDecimalSeparator == "," ? "1,5" : "1.5";
            Say("  [положительный контроль] `(1.5).ToString()` без культуры = «" + bare + "»  "
                + (bare == want ? "— плечо воспроизводит культуру"
                                : "⛔ ОЖИДАЛОСЬ «" + want + "»: ПЛЕЧО НЕ МЕРИТ"));
            if (bare != want) failures++;
            string bareThousand = (1234.5).ToString("n2");
            Say("  [положительный контроль] `(1234.5).ToString(\"n2\")` без культуры = «"
                + bareThousand + "» — так выглядела бы группировка, если бы её оставили");

            // ══ ПРЯМОЕ ПЛЕЧО: ПЕЧАТЬ. Сверка с эталоном ПОСИМВОЛЬНО.
            List<Slot> here = PrintSlots();
            if (here.Count != reference.Count)
            {
                failures++;
                Say("  ⛔ число слотов разошлось: " + here.Count + " против " + reference.Count);
            }
            int bad = 0;
            for (int i = 0; i < here.Count && i < reference.Count; i++)
            {
                if (here[i].Text == reference[i].Text) continue;
                bad++;
                failures++;
                Say("  ⛔ ПЕЧАТЬ РАЗОШЛАСЬ: " + here[i].Name);
                Say("       эталон: «" + reference[i].Text + "»");
                Say("       здесь:  «" + here[i].Text + "»");
            }
            Say("  ПЕЧАТЬ: слотов " + here.Count + ", разошлось " + bad
                + (bad == 0 ? " — вид строки тот же, что у инварианта" : ""));

            // ══ ОБРАТНОЕ ПЛЕЧО: поле читается обратно. И число, И текст.
            List<ReadBack> parses = ParseSlots();
            int badParse = 0;
            for (int i = 0; i < parses.Count && i < refParse.Count; i++)
            {
                ReadBack a = refParse[i], b = parses[i];
                if (b.Value != a.Value)
                {
                    badParse++; failures++;
                    Say("  ⛔ РАЗБОР РАЗОШЁЛСЯ: " + b.Name
                        + " — эталон «" + a.Value + "», здесь «" + b.Value + "»");
                }
                if (b.Text != a.Text)
                {
                    badParse++; failures++;
                    Say("  ⛔ ТЕКСТ ПОЛЯ РАЗОШЁЛСЯ: " + b.Name
                        + " — эталон «" + a.Text + "», здесь «" + b.Text + "»");
                }
                if (b.Want != null && b.Value != b.Want && !b.Value.StartsWith("⛔", StringComparison.Ordinal))
                {
                    badParse++; failures++;
                    Say("  ⛔ РАЗБОР ДАЛ НЕ ТО: " + b.Name
                        + " — ждали «" + b.Want + "», вышло «" + b.Value + "»");
                }
            }
            Say("  РАЗБОР: слотов " + parses.Count + ", расхождений " + badParse
                + (badParse == 0 ? " — и число, и текст поля те же" : ""));

            // ══ ПЕРЕКРЁСТНОЕ ПЛЕЧО: строки, напечатанные ПЕРВЫМ чужим плечом
            //    (`ru-RU`), читаются здесь.
            if (crossPrint == null)
            {
                crossPrint = new string[here.Count];
                for (int i = 0; i < here.Count; i++) crossPrint[i] = here[i].Text;
                Say("  ПЕРЕКРЁСТНОЕ: снимок этого плеча взят меркой следующим");
            }
            else
            {
                int badCross = 0;
                for (int i = 0; i < here.Count && i < crossPrint.Length; i++)
                {
                    if (here[i].Text == crossPrint[i]) continue;
                    badCross++; failures++;
                    Say("  ⛔ ПЕРЕКРЁСТНОЕ РАЗОШЛОСЬ: " + here[i].Name
                        + " — на " + Foreign[0] + " «" + crossPrint[i]
                        + "», здесь «" + here[i].Text + "»");
                }
                Say("  ПЕРЕКРЁСТНОЕ: расхождений " + badCross
                    + (badCross == 0 ? " — строка " + Foreign[0] + " читается здесь так же" : ""));

                // Число, напечатанное на `ru-RU`, разбирается ЗДЕСЬ инвариантом.
                double v;
                bool ok = double.TryParse("1234.5", NumberStyles.Float,
                                          CultureInfo.InvariantCulture, out v) && v == 1234.5;
                Say("  ПЕРЕКРЁСТНОЕ по числу: «1234.5» инвариантом здесь = "
                    + v.ToString("R", CultureInfo.InvariantCulture) + (ok ? "" : "  ⛔"));
                if (!ok) failures++;
            }

            // ══ группировка разрядов на самом плече.
            int gr = 0;
            foreach (Slot sl in here) if (HasGrouping(sl.Text)) gr++;
            if (gr > 0) { failures++; Say("  ⛔ разделитель групп на плече " + name + ": слотов " + gr); }
        }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Есть ли в строке РАЗДЕЛИТЕЛЬ ГРУПП. Признак: цифра, потом запятая или
    /// неразрывный/обычный пробел, потом РОВНО три цифры и не цифра.
    /// Именно так выглядит «1,234.50» / «1 234,50», и именно этого быть не
    /// должно (решение Amber 05.09.2026).
    /// </summary>
    static bool HasGrouping(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 1; i + 3 < s.Length; i++)
        {
            char c = s[i];
            if (c != ',' && c != ' ' && c != ' ' && c != ' ') continue;
            if (!char.IsDigit(s[i - 1])) continue;
            if (!char.IsDigit(s[i + 1]) || !char.IsDigit(s[i + 2]) || !char.IsDigit(s[i + 3])) continue;
            if (i + 4 < s.Length && char.IsDigit(s[i + 4])) continue;
            return true;
        }
        return false;
    }

    static string Pad(string s)
    {
        return s.Length >= 32 ? s : s + new string(' ', 32 - s.Length);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (образец — `CultureProbeO14`, полоса F20, `A245`)
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
                    string t = txt.ToString().Trim();
                    if (t.Length > 0)
                    {
                        if (acc.Length > 0) acc.Append(" / ");
                        acc.Append(t);
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
            System.Windows.Forms.MessageBox.Show("контрольное окно полосы F28",
                                                 "контроль", System.Windows.Forms.MessageBoxButtons.OK);
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        bool closed = th.Join(20000);
        double sec = (DateTime.UtcNow - t0).TotalSeconds;
        Say("  окно поднято нарочно, закрыто сторожем: " + (closed ? "да" : "⛔ НЕТ")
            + ", секунд " + sec.ToString("F1", CultureInfo.InvariantCulture)
            + ", окон назвал сторож: " + (modalSeen - before));
        if (!closed) failures++;
    }

    // ══════════════════════════════════════════════════════════════════════

    static void Say(string line)
    {
        lock (Log)
        {
            Log.AppendLine(line);
            Console.WriteLine(line);
        }
    }

    static void Finish(string outPath)
    {
        if (string.IsNullOrEmpty(outPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("отчёт: " + Path.GetFullPath(outPath));
        }
        catch (Exception ex) { Console.WriteLine("отчёт не записан: " + ex.Message); }
    }
}
