// `A184` + `A187`: СЛУЖЕБНЫЕ СТРОКИ, КОТОРЫЕ ВИДИТ ЧЕЛОВЕК, — ОТКУДА ОНИ БЕРУТСЯ.
//
//     stringsprobef55 [--out=<файл>]
//
// Две строки реестра, один предмет: текст на экране обязан приходить из
// `Resources.resx` / `Resources.ru.resx` и меняться вместе с языком окна.
//
//   `A187` — подписи окна «Матрица отклика»: `ResponseMatrixForm`
//            .DescribeInheritedHistories («посчитана реже штатного, поле
//            поднято») и .DescribeFingerprint («Отпечаток тела: …», три его
//            состояния) выбирали язык САМИ, парой литералов под
//            `CurrentUICulture`.
//   `A184` — причина недопустимости родительской группировки: модель отдавала
//            её РУССКОЙ СЛУЖЕБНОЙ СТРОКОЙ, и `FSAReportView` мог лишь
//            подставить её в подсказку — на английском экране русский текст.
//
// ⛔ ОКНО НЕ ЗАПУСКАЕТСЯ. Обе подписи и читатель кода причины берутся из
// собранной сборки ОТРАЖЕНИЕМ (методы закрытые и статические), культура
// выставляется потоку пробы. Ни одна форма не создаётся.
//
// ⚠ ОЖИДАНИЕ ВЫПИСАНО ДОСЛОВНО и берётся НЕ из того ресурса, что меряется:
// проба, читающая ожидание оттуда же, откуда ответ, не мерит ничего.
// Плечо «происхождение» (текст собран из ресурса) стоит ОТДЕЛЬНО от плеча
// «дословно» (в ресурсе написано именно это).
//
// ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ (без них проба доказывает лишь то, что она не падает):
//   К1  сито кириллицы ловит подброшенный русский текст в английской подписи;
//   К2  проверка «ключ есть в обеих культурах» ОТКАЗЫВАЕТ на заведомо
//       отсутствующем ключе;
//   К3  культура БЕЗ сателлита (de-DE) даёт РОВНО английское значение — то
//       есть пропажа ключа из `.ru.resx` выглядела бы как «ru совпал с en», и
//       плечо «ru отличается от en» её ловит;
//   К4  читатель кода причины на `None` возвращает пустоту, а не текст: код
//       без причины не должен превращаться в подпись.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;

// Целевая платформа процесса объявлена общим довеском `_TargetFramework.cs`
// (`T237`); своего атрибута здесь нет нарочно — был бы CS0579.

static class StringsProbeF55
{
    // ------------------------------------------------------------------
    // Ожидания — ДОСЛОВНО. Меняете значение ресурса — меняйте и здесь, иначе
    // проба перестанет мерить содержание и начнёт мерить саму себя.
    // ------------------------------------------------------------------

    const string EnHistories =
        "Computed with 300000 histories per node, nominal is 3000000; field raised to nominal";
    const string RuHistories =
        "Посчитана 300000 историями на узел при штатных 3000000; поле поднято до штатного";

    const string Head = "0123456789abcdef…";

    const string EnFpNone = "Body fingerprint: none (taken from the file)";
    const string RuFpNone = "Отпечаток тела: нет (снимается с файла)";

    const string EnFpNotStored = "Body fingerprint: " + Head + " (not stored in the file)";
    const string RuFpNotStored = "Отпечаток тела: " + Head + " (в файле не записан)";

    const string EnFpOk = "Body fingerprint: " + Head;
    const string RuFpOk = "Отпечаток тела: " + Head;

    const string EnFpBad = "Body fingerprint: " + Head + " ⚠ DOES NOT MATCH the one stored in the file";
    const string RuFpBad = "Отпечаток тела: " + Head + " ⚠ НЕ СХОДИТСЯ с записанным в файле";

    const string EnFreeChain = "chain members have free amplitudes, the parent detection limit is undefined";
    const string RuFreeChain = "члены ряда со свободными амплитудами, предел родителя не определён";
    const string EnNoChain = "there is no decay chain in the composition";
    const string RuNoChain = "в составе нет ряда распада";

    // Служебная строка МОДЕЛИ (`FsaResult.ParentGroupingRefusal`): она остаётся
    // русской нарочно — журналу и пробам, а не экрану.
    const string ServiceFreeChain = "члены ряда со свободными амплитудами: предел родителя не определён";
    const string ServiceNoChain = "в составе нет ряда распада";

    static readonly string[] Keys =
    {
        "ResponseMatrixInheritedHistories",
        "ResponseMatrixFingerprint",
        "ResponseMatrixFingerprintNone",
        "ResponseMatrixFingerprintNotStored",
        "ResponseMatrixFingerprintMismatch",
        "FSAReportRefusalFreeChainMembers",
        "FSAReportRefusalNoDecayChain",
    };

    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal))
            {
                outPath = a.Substring(6);
            }
        }

        Say("=== A184 + A187: служебные строки экрана — из ресурсов или из кода ===");
        Say(ProbeTargetFramework.Describe());
        if (!ProbeTargetFramework.Matches)
        {
            Say("⛔ проба собрана без довеска платформы — числа недоверенные (T237)");
            failures++;
        }

        Assembly app = typeof(GlobalConfigManager).Assembly;
        Say("сборка приложения: " + app.Location);
        Say("");

        try
        {
            KeysInBothCultures();
            MatrixCaptions(app);
            RefusalCode();
            RefusalReader(app);
            Controls(app);
        }
        catch (Exception ex)
        {
            Say("⛔ ОБРЫВ: " + ex.GetType().Name + ": " + ex.Message);
            Say(ex.StackTrace ?? "");
            failures++;
        }

        Say("");
        Say(failures == 0
            ? "ВСЕ СОШЛИСЬ: расхождений нет."
            : "ОТКАЗ: расхождений " + failures.ToString(CultureInfo.InvariantCulture) + ".");

        if (outPath != null)
        {
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("записано: " + outPath);
        }

        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. Ключи есть в ОБЕИХ культурах, и русское значение НЕ РАВНО английскому
    // ------------------------------------------------------------------

    static void KeysInBothCultures()
    {
        Say("--- 1. Семь ключей в обеих культурах ---");
        foreach (string key in Keys)
        {
            KeyPair(key, true);
        }
        Say("");
    }

    /// <summary>
    /// Ключ прочитан обеими культурами и переведён. <paramref name="expectGood"/>
    /// = false — контроль К2: ждём ОТКАЗА и считаем его успехом.
    /// </summary>
    static bool KeyPair(string key, bool expectGood)
    {
        string en = Resources.ResourceManager.GetString(key, En);
        string ru = Resources.ResourceManager.GetString(key, Ru);
        bool good = en != null && ru != null && !string.Equals(en, ru, StringComparison.Ordinal);
        string verdict = en == null ? "НЕТ В en" : ru == null ? "НЕТ В ru"
                       : string.Equals(en, ru, StringComparison.Ordinal) ? "ru СОВПАЛ с en (перевода нет)"
                       : "есть в обеих, переведён";
        Mark(good == expectGood, "  " + key + ": " + verdict);
        return good;
    }

    // ------------------------------------------------------------------
    // 2. A187 — подписи окна матрицы
    // ------------------------------------------------------------------

    static void MatrixCaptions(Assembly app)
    {
        Say("--- 2. `A187`: подписи `ResponseMatrixForm` ---");
        Type form = app.GetType("BecquerelMonitor.ResponseMatrixForm", true);
        MethodInfo histories = Method(form, "DescribeInheritedHistories");
        MethodInfo fingerprint = Method(form, "DescribeFingerprint");

        // (а) «поле поднято до штатного»: матрица на 300 тыс. при штатных 3 млн.
        ResponseMatrix lean = new ResponseMatrix();
        lean.Options = new ResponseMatrixOptions();
        lean.Options.Histories = 300000;

        Pair("DescribeInheritedHistories", () => (string)histories.Invoke(null, new object[] { lean, 3000000m }),
             EnHistories, RuHistories);

        // Пусто, когда матрица не беднее штатной, — на обеих культурах.
        ResponseMatrix rich = new ResponseMatrix();
        rich.Options = new ResponseMatrixOptions();
        rich.Options.Histories = 3000000;
        foreach (CultureInfo c in new[] { En, Ru })
        {
            string empty = InCulture(c, () => (string)histories.Invoke(null, new object[] { rich, 3000000m }));
            Mark(empty == "", "  штатная матрица, " + c.Name + ": подписи нет — «" + empty + "»");
        }

        // (б) отпечаток тела, три состояния.
        Pair("DescribeFingerprint / нет вовсе", () => (string)fingerprint.Invoke(null, new object[] { Matrix(null, null) }),
             EnFpNone, RuFpNone);
        Pair("DescribeFingerprint / не записан", () => (string)fingerprint.Invoke(null, new object[] { Matrix(Body(), null) }),
             EnFpNotStored, RuFpNotStored);
        Pair("DescribeFingerprint / сходится", () => (string)fingerprint.Invoke(null, new object[] { Matrix(Body(), Body()) }),
             EnFpOk, RuFpOk);
        Pair("DescribeFingerprint / НЕ сходится", () => (string)fingerprint.Invoke(null, new object[] { Matrix(Body(), Other()) }),
             EnFpBad, RuFpBad);
        Say("");
    }

    static string Body()
    {
        return "0123456789abcdef" + new string('0', 48);
    }

    static string Other()
    {
        return "fedcba9876543210" + new string('0', 48);
    }

    static ResponseMatrix Matrix(string body, string stored)
    {
        ResponseMatrix m = new ResponseMatrix();
        m.Options = new ResponseMatrixOptions();
        m.BodyFingerprint = body;
        m.StoredBodyFingerprint = stored;
        return m;
    }

    // ------------------------------------------------------------------
    // 3. A184 — КОД причины у модели
    // ------------------------------------------------------------------

    static void RefusalCode()
    {
        Say("--- 3. `A184`: модель отдаёт КОД причины ---");

        Case("связанный ряд (равновесие вкл)", Result(Chain("Bi-214", "Ra-226", "Ra-226")),
             FsaParentGroupingRefusal.None, true, null);
        Case("свободные члены ряда (равновесие выкл)", Result(Chain("Bi-214", null, "Ra-226")),
             FsaParentGroupingRefusal.FreeChainMembers, false, ServiceFreeChain);
        Case("ряда нет вовсе", Result(Chain("Cs-137", null, null)),
             FsaParentGroupingRefusal.NoDecayChain, false, ServiceNoChain);

        // Снимок представления несёт КОД, а не текст: строкового свойства у
        // `FsaPresentation` больше нет — это и есть суть `A184`.
        Type pres = typeof(FsaPresentation);
        Mark(pres.GetProperty("ParentGroupingRefusal") == null,
             "  у `FsaPresentation` НЕТ строкового `ParentGroupingRefusal`");
        PropertyInfo code = pres.GetProperty("ParentGroupingRefusalReason");
        Mark(code != null && code.PropertyType == typeof(FsaParentGroupingRefusal),
             "  у `FsaPresentation` есть `ParentGroupingRefusalReason` типа `FsaParentGroupingRefusal`");

        FsaResult free = Result(Chain("Bi-214", null, "Ra-226"));
        FsaPresentation p = FsaPresentationBuilder.Build(free, FsaGrouping.Parents, false);
        Mark(p.ParentGroupingRefusalReason == FsaParentGroupingRefusal.FreeChainMembers,
             "  построитель донёс код до снимка: " + p.ParentGroupingRefusalReason);
        Mark(p.Grouping == FsaGrouping.Daughters,
             "  недопустимый запрос показан дочерними: " + p.Grouping);
        Say("");
    }

    static void Case(string what, FsaResult result, FsaParentGroupingRefusal code,
                     bool allowed, string service)
    {
        Mark(result.ParentGroupingRefusalReason == code,
             "  " + what + ": код " + result.ParentGroupingRefusalReason + " (ждали " + code + ")");
        Mark(result.ParentGroupingAllowed == allowed,
             "    родители " + (result.ParentGroupingAllowed ? "допустимы" : "НЕДОПУСТИМЫ"));
        Mark(string.Equals(result.ParentGroupingRefusal, service, StringComparison.Ordinal),
             "    служебная строка модели: «" + (result.ParentGroupingRefusal ?? "(null)") + "»");
    }

    static FsaResult Result(FsaComponentResult component)
    {
        FsaResult r = new FsaResult();
        r.Components.Add(component);
        return r;
    }

    static FsaComponentResult Chain(string name, string chainRoot, string decayChainRoot)
    {
        FsaComponentResult c = new FsaComponentResult();
        c.Name = name;
        c.ChainRoot = chainRoot;
        c.DecayChainRoot = decayChainRoot;
        return c;
    }

    // ------------------------------------------------------------------
    // 4. A184 — ЧИТАТЕЛЬ кода: кто превращает его в подпись и в какой культуре
    // ------------------------------------------------------------------

    static void RefusalReader(Assembly app)
    {
        Say("--- 4. `A184`: читатель кода — `FSAReportView.RefusalText` ---");
        Type view = app.GetType("BecquerelMonitor.FSAReportView", true);
        MethodInfo read = Method(view, "RefusalText");

        Pair("RefusalText(FreeChainMembers)",
             () => (string)read.Invoke(null, new object[] { FsaParentGroupingRefusal.FreeChainMembers }),
             EnFreeChain, RuFreeChain);
        Pair("RefusalText(NoDecayChain)",
             () => (string)read.Invoke(null, new object[] { FsaParentGroupingRefusal.NoDecayChain }),
             EnNoChain, RuNoChain);

        // Та же склейка, какой вид ставит подсказку переключателя «родители».
        foreach (CultureInfo c in new[] { En, Ru })
        {
            string tip = InCulture(c, () => string.Format(CultureInfo.CurrentCulture,
                Resources.FSAReportTipParentsRefused,
                (string)read.Invoke(null, new object[] { FsaParentGroupingRefusal.FreeChainMembers })));
            Mark(Cyrillic(tip) == (c == Ru),
                 "  подсказка переключателя, " + c.Name + ": " + tip);
        }
        Say("");
    }

    // ------------------------------------------------------------------
    // 5. Положительные контроли
    // ------------------------------------------------------------------

    static void Controls(Assembly app)
    {
        Say("--- 5. Положительные контроли ---");

        // К1. Сито кириллицы ловит подброшенный русский текст.
        Mark(Cyrillic(RuHistories), "  К1: сито кириллицы ЛОВИТ русскую подпись, поданную как английскую");
        Mark(!Cyrillic(EnHistories), "  К1: и молчит на английской");

        // К2. Проверка ключа ОТКАЗЫВАЕТ на заведомо отсутствующем.
        bool caught = !KeyPair("F55НетТакогоКлюча", false);
        Mark(caught, "  К2: проверка ключа отказала на несуществующем ключе");

        // К3. Культура без сателлита падает на английское значение — то есть
        // пропажа ключа из `.ru.resx` выглядит как «ru совпал с en».
        CultureInfo de = CultureInfo.GetCultureInfo("de-DE");
        string enFp = Resources.ResourceManager.GetString("ResponseMatrixFingerprint", En);
        string deFp = Resources.ResourceManager.GetString("ResponseMatrixFingerprint", de);
        Mark(string.Equals(enFp, deFp, StringComparison.Ordinal),
             "  К3: культура без сателлита (de-DE) даёт английское значение — «" + deFp + "»");

        // К4. Читатель на `None` не выдумывает текста.
        Type view = app.GetType("BecquerelMonitor.FSAReportView", true);
        MethodInfo read = Method(view, "RefusalText");
        string none = InCulture(Ru, () => (string)read.Invoke(null, new object[] { FsaParentGroupingRefusal.None }));
        Mark(none == "", "  К4: `RefusalText(None)` пуст — код без причины не становится подписью");
    }

    // ------------------------------------------------------------------
    // Оснастка
    // ------------------------------------------------------------------

    /// <summary>
    /// Одно плечо: снять подпись на обеих культурах и судить ТРЁМЯ мерками —
    /// дословно, письмом (кириллица только в русской) и различием.
    /// </summary>
    static void Pair(string what, Func<string> make, string en, string ru)
    {
        string gotEn = InCulture(En, make);
        string gotRu = InCulture(Ru, make);
        string trimEn = gotEn.TrimStart('\r', '\n');
        string trimRu = gotRu.TrimStart('\r', '\n');

        Mark(string.Equals(trimEn, en, StringComparison.Ordinal), "  " + what + " / en-US: «" + trimEn + "»");
        Mark(string.Equals(trimRu, ru, StringComparison.Ordinal), "  " + what + " / ru-RU: «" + trimRu + "»");
        Mark(!Cyrillic(trimEn), "    английская подпись без кириллицы");
        Mark(Cyrillic(trimRu), "    русская подпись с кириллицей");
        Mark(!string.Equals(trimEn, trimRu, StringComparison.Ordinal), "    подписи РАЗНЫЕ");
    }

    static string InCulture(CultureInfo culture, Func<string> body)
    {
        CultureInfo old = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = culture;
            return body() ?? "";
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = old;
        }
    }

    static bool Cyrillic(string text)
    {
        if (text == null)
        {
            return false;
        }

        foreach (char c in text)
        {
            // U+0400…U+04FF — кириллица; писано escape-ами нарочно, чтобы
            // сито не зависело от того, какой кодировкой прочитан ЭТОТ файл.
            if (c >= '\u0400' && c <= '\u04FF')
            {
                return true;
            }
        }

        return false;
    }

    static MethodInfo Method(Type type, string name)
    {
        MethodInfo m = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (m == null)
        {
            throw new MissingMethodException(type.FullName + "." + name
                + " — сборка ПРЕЖНЯЯ или метод переименован");
        }

        return m;
    }

    static void Mark(bool ok, string text)
    {
        if (!ok)
        {
            failures++;
        }

        Say((ok ? "ok   " : "ОТКАЗ") + " " + text);
    }

    static void Say(string text)
    {
        Console.WriteLine(text);
        Log.AppendLine(text);
    }
}
