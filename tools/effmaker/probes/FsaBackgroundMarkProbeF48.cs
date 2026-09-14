// ═══════════════════════════════════════════════════════════════════════════
//  Полоса F48, 06.09.2026. `S44`: ФОН ПОДАН И НЕ ВЗЯТ — ВИДНО ЛИ ЭТО ЧЕЛОВЕКУ?
// ═══════════════════════════════════════════════════════════════════════════
//
//  Решение Amber 01.09.2026 (вопросник): отказ «фон не взят» ПОКАЗЫВАТЬ В
//  ОКНЕ. До 06.09.2026 он не был исполнен: `FsaResult.BackgroundRejected`
//  ставился один раз в `FsaAnalyzer` и читателя в приложении не имел вовсе —
//  только колонка `bg_rejected` корпусной пробы. Человеку приложение молчало
//  ровно так же, как молчало пробам до 15.08.2026.
//
//  ЧТО МЕРЯЕТСЯ:
//
//    1. РЕСУРСЫ. Пометка и подписи есть в ОБЕИХ культурах, различны между
//       культурами и не равны имени ключа (то есть `.resx` действительно
//       прочитан, а не подставлен ключом).
//    2. МОДЕЛЬ. `FsaPresentationBuilder.QualityText` несёт пометку тогда и
//       только тогда, когда `BackgroundRejected` заполнен. ⛔ ОБЕ СТОРОНЫ:
//       проверка, глядящая лишь на появление, прошла бы и на пометке,
//       стоящей ВСЕГДА.
//    3. ОКНО. В блоке «Качество разбора» (`A247`) появляется РОВНО ОДНА
//       лишняя строка, её подпись — из `FSAReportView.resx`, значение —
//       причина словами, а состав строк блока по `Tag.Kind` остаётся тем же
//       родом. У здорового результата строки нет, и блок той же длины, что
//       был.
//    4. ЦЕПЬ ЦЕЛИКОМ. Настоящий `FsaAnalyzer` на спектре, которому подали фон
//       ДРУГОЙ длины, обязан назвать причину строкой ИЗ РЕСУРСОВ, и она
//       обязана доехать до строки окна. На согласованном фоне причины нет.
//
//    FsaBackgroundMarkProbeF48.exe
//
//  Ожидание: «ВСЕ СОШЛИСЬ», код 0.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using XPTable.Models;

static class FsaBackgroundMarkProbeF48
{
    static int bad;
    static int checks;

    /// <summary>Причина отказа, какой её кладёт разбор. Здесь она — ДАННЫЕ.</summary>
    const string Reason = "the background has 1012 channels, the spectrum 1024";

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // ⛔ Сторож модальных окон — ПЕРВЫМ ДЕЛОМ (`A245`).
        ModalWatchStart();

        // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика,
        // отражение её не видит, и снятая позже она уже могла быть уведена.
        FsaTuningReport.Snapshot();

        // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
        ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
        ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

        Console.WriteLine("сборка приложения: " + typeof(FsaResult).Assembly.Location);
        try
        {
            Console.WriteLine("собрана:           "
                + File.GetLastWriteTime(typeof(FsaResult).Assembly.Location)
                      .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Console.WriteLine("собрана:           не прочитана: " + ex.Message); }

        ResourcesSection();
        ModelSection();
        WindowSection();
        ChainSection();

        Language("en-US");
        ModalWatchStop();
        Console.WriteLine();
        Console.WriteLine("проверок: " + checks.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ"
                                   : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. РЕСУРСЫ
    // ------------------------------------------------------------------

    static void ResourcesSection()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. строки ресурса: обе культуры, обе пары `.resx` ===");

        Pair("FSABackgroundRejectedMark", Mark);
        Pair("FSABackgroundNoCounts", Mark);
        Pair("FSABackgroundChannelMismatch", Mark);
        Pair("FSABackgroundNoLiveTime", Mark);
        Pair("FSAReport_BackgroundRejectedRow", Own);

        // Причина с числами печатается ИНВАРИАНТНОЙ культурой (`A244`): 1012 и
        // 1024 не смеют получить разделитель разрядов ни на одной культуре.
        foreach (string lang in new[] { "ru-RU", "en-US" })
        {
            Language(lang);
            string text = string.Format(CultureInfo.InvariantCulture,
                                        Resources.FSABackgroundChannelMismatch, 1012, 1024);
            Console.WriteLine("  {0}: причина о каналах = «{1}»", lang, text);
            Same(lang + ": в причине число 1012 без разделителя разрядов", true, text.Contains("1012"));
            Same(lang + ": в причине число 1024 без разделителя разрядов", true, text.Contains("1024"));
            Same(lang + ": ни запятой, ни пробела в группах разрядов", false,
                 text.Contains("1,012") || text.Contains("1 012"));
        }

        Language("en-US");
    }

    static void Pair(string key, Func<string, string> read)
    {
        Language("ru-RU");
        string ru = read(key);
        Language("en-US");
        string en = read(key);
        Console.WriteLine("  {0}: en=«{1}», ru=«{2}»", key, en, ru);
        Same(key + ": английская строка есть и не имя ключа", true,
             !string.IsNullOrEmpty(en) && en != key);
        Same(key + ": русская строка есть и не имя ключа", true,
             !string.IsNullOrEmpty(ru) && ru != key);
        Denies(key + ": культуры дают РАЗНЫЙ текст", en == ru);
    }

    // ------------------------------------------------------------------
    // 2. МОДЕЛЬ
    // ------------------------------------------------------------------

    static void ModelSection()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. модель: пометка в хвосте строки качества, обе стороны ===");

        foreach (string lang in new[] { "ru-RU", "en-US" })
        {
            Language(lang);
            string mark = Resources.FSABackgroundRejectedMark;

            FsaResult healthy = Scene(null);
            FsaResult rejected = Scene(Reason);

            string textHealthy = FsaPresentationBuilder.QualityText(healthy, false);
            string textRejected = FsaPresentationBuilder.QualityText(rejected, false);
            Console.WriteLine("  {0}: здоровый  «{1}»", lang, textHealthy);
            Console.WriteLine("  {0}: отвергнут «{1}»", lang, textRejected);

            // ⛔ ОБЕ СТОРОНЫ.
            Same(lang + ": у отвергнутого фона пометка ЕСТЬ", true, textRejected.Contains(mark));
            Same(lang + ": у здорового пометки НЕТ", false, textHealthy.Contains(mark));
            Denies(lang + ": тексты двух сцен РАЗЛИЧАЮТСЯ", textHealthy == textRejected);

            // Место пометки — между «без кривой» и краем сетки (порядок задан
            // строкой реестра `S44` и есть СТАРШИНСТВО, `S104`).
            FsaResult full = Scene(Reason);
            full.EfficiencyUsed = false;
            full.GainOnGridEdge = true;
            string textFull = FsaPresentationBuilder.QualityText(full, false);
            int eff = textFull.IndexOf(Resources.FSANoEfficiencyMark, StringComparison.Ordinal);
            int here = textFull.IndexOf(mark, StringComparison.Ordinal);
            int drift = textFull.LastIndexOf(Resources.FSADriftEdgeMark, StringComparison.Ordinal);
            Console.WriteLine("  {0}: полный хвост «{1}»", lang, textFull);
            Same(lang + ": пометка ПОСЛЕ «без кривой»", true, eff >= 0 && here > eff);
            Same(lang + ": пометка ДО края сетки", true, drift >= 0 && here < drift);
        }

        Language("en-US");
    }

    // ------------------------------------------------------------------
    // 3. ОКНО
    // ------------------------------------------------------------------

    static void WindowSection()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. окно отчёта: строка блока «Качество разбора», обе стороны ===");

        foreach (string lang in new[] { "ru-RU", "en-US" })
        {
            Language(lang);
            string caption = Own("FSAReport_BackgroundRejectedRow");

            int rowsHealthy = Block(Scene(null), lang + " · здоровый", caption, null);
            int rowsRejected = Block(Scene(Reason), lang + " · фон отвергнут", caption, Reason);

            // Строк ровно ДВЕ, и они о разном: «ФОН НЕ ВЫЧТЕН» (род
            // `NoBackground`, стоит с 15.08.2026) говорит ЧТО, новая пометка
            // блока качества — ПОЧЕМУ. Ждать одну значило бы принять окно, в
            // котором причина есть, а сама беда не названа.
            Same(lang + ": отвергнутый фон добавил РОВНО две строки — «что» и «почему»",
                 2, rowsRejected - rowsHealthy);
        }

        Language("en-US");
    }

    /// <summary>
    /// Собрать окно отчёта на планированном сеансе и найти строку пометки.
    /// Возвращает число строк таблицы — им меряется, что блок не разъехался.
    /// </summary>
    static int Block(FsaResult result, string scene, string caption, string wantValue)
    {
        var session = new FsaAnalysisSession();
        Plant(session, result, "f48");
        int count;
        using (var report = new FSAReportView(null))
        {
            // ⛔ Без `ResultData` окно показывает одну строку «Спектр не
            //    выбран» и НИЧЕГО не меряет: проба без него отчиталась бы
            //    «строки пометки нет» на обеих сценах и прошла бы насквозь.
            report.SetProbeSource(session, Sample());
            TableModel model = report.ReportTable.TableModel;
            count = model.Rows.Count;

            int found = 0;
            string value = null;
            var kinds = new List<string>();
            for (int i = 0; i < model.Rows.Count; i++)
            {
                var tag = model.Rows[i].Tag as FsaReportRow;
                kinds.Add(tag == null ? "(нет Tag)" : tag.Kind.ToString());
                if (model.Rows[i].Cells[1].Text == caption)
                {
                    found++;
                    value = model.Rows[i].Cells[2].Text;
                    // Род строки — тот же, что у соседних пометок блока
                    // (`A247`): приёмка `FsaReportViewProbe` судит состав по
                    // `Tag.Kind`, и новая строка обязана лечь в тот же ряд.
                    Same(scene + ": род строки пометки — Quality", "Quality",
                         tag == null ? "(нет Tag)" : tag.Kind.ToString());
                }
            }

            Console.WriteLine("  {0}: строк {1}, роды: {2}", scene, count, string.Join(" ", kinds));
            if (wantValue == null)
            {
                Same(scene + ": строки «фон не взят» НЕТ", 0, found);
            }
            else
            {
                Same(scene + ": строка «фон не взят» одна", 1, found);
                Same(scene + ": значение — причина словами", wantValue, value);
                Same(scene + ": подпись не имя ключа", false,
                     caption.StartsWith("FSAReport_", StringComparison.Ordinal));
            }
        }

        return count;
    }

    // ------------------------------------------------------------------
    // 4. ЦЕПЬ ЦЕЛИКОМ
    // ------------------------------------------------------------------

    /// <summary>
    /// Настоящий разбор: спектру подан фон ДРУГОЙ длины. Причина обязана
    /// прийти из ресурсов (сравнивается с собранной здесь же по тому же
    /// ключу — но ключ читается ОТДЕЛЬНО, а не берётся у результата), доехать
    /// до модели и до строки окна.
    /// </summary>
    static void ChainSection()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. цепь целиком: FsaAnalyzer -> FsaResult -> окно ===");
        Language("en-US");

        FsaResult mismatched = Analyze(1024, 1012);
        FsaResult healthy = Analyze(1024, 1024);

        Same("разбор на несовпадающем фоне состоялся", true, mismatched != null);
        Same("разбор на согласованном фоне состоялся", true, healthy != null);
        if (mismatched == null || healthy == null)
        {
            return;
        }

        string want = string.Format(CultureInfo.InvariantCulture,
                                    Resources.FSABackgroundChannelMismatch, 1012, 1024);
        Console.WriteLine("  причина у результата: «{0}»", mismatched.BackgroundRejected);
        Same("причина названа и равна строке ресурса", want, mismatched.BackgroundRejected);
        Same("фон при этом НЕ вычтен", false, mismatched.BackgroundUsed);

        // ⛔ ОБРАТНАЯ СТОРОНА: согласованный фон причины не имеет.
        Console.WriteLine("  согласованный фон: причина = {0}, вычтен = {1}",
                          healthy.BackgroundRejected ?? "(нет)", healthy.BackgroundUsed);
        Same("у согласованного фона причины НЕТ", null, healthy.BackgroundRejected);

        // И она доезжает до окна.
        Block(mismatched, "цепь · фон отвергнут", Own("FSAReport_BackgroundRejectedRow"), want);
    }

    /// <summary>
    /// Наименьший настоящий разбор: линейная шкала, одна линия в библиотеке,
    /// плоский спектр. Числа здесь неважны — важно, что путь тот самый.
    /// </summary>
    static FsaResult Analyze(int channels, int backgroundChannels)
    {
        EnergySpectrum spectrum = MakeSpectrum(channels, 100);
        EnergySpectrum background = MakeSpectrum(backgroundChannels, 10);

        // FWHM(ch) = √(b + k·ch): около 3 % на середине шкалы — разрешение
        // сцинтиллятора, лишь бы образ был не уже канала.
        var fwhm = new SimpleSqrtFwhmCalibration();
        fwhm.Coefficients = new double[] { 4.0, 0.5 };

        var component = new FsaComponent("Cs-137", FsaComponentKind.Single);
        component.Lines.Add(new FsaLine("Cs-137", 661.7, 85.1));
        component.TotalYieldPercent = 85.1;

        var analyzer = new FsaAnalyzer();
        FsaTuningReport.Print(analyzer);
        try
        {
            return analyzer.Analyze(spectrum, background, fwhm,
                                    new List<FsaComponent> { component }, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  разбор бросил {0}: {1}", ex.GetType().Name, ex.Message);
            return null;
        }
    }

    static EnergySpectrum MakeSpectrum(int channels, int perChannel)
    {
        var spectrum = new EnergySpectrum();
        spectrum.NumberOfChannels = channels;
        int[] counts = new int[channels];
        for (int i = 0; i < channels; i++)
        {
            counts[i] = perChannel;
        }

        spectrum.Spectrum = counts;
        spectrum.MeasurementTime = 1000;
        spectrum.LiveTime = 1000;
        var calibration = new PolynomialEnergyCalibration();
        calibration.PolynomialOrder = 1;
        calibration.Coefficients = new double[] { 0.0, 1.5 };
        spectrum.EnergyCalibration = calibration;
        return spectrum;
    }

    // ------------------------------------------------------------------
    // Служебное
    // ------------------------------------------------------------------

    /// <summary>Наименьший спектр, при котором окно вообще строит отчёт.</summary>
    static ResultData Sample()
    {
        var rd = new ResultData();
        rd.EnergySpectrum = MakeSpectrum(1024, 100);
        return rd;
    }

    static FsaResult Scene(string rejected)
    {
        return new FsaResult
        {
            Chi2Ndf = 2.94,
            BackgroundUsed = rejected == null,
            BackgroundRejected = rejected,
            ResponseMatrixUsed = true,
            EfficiencyUsed = true,
            CascadeSummingUsed = false
        };
    }

    static void Plant(FsaAnalysisSession session, FsaResult result, string stamp)
    {
        Field(session, "result").SetValue(session, result);
        Field(session, "stamp").SetValue(session, stamp);
        Field(session, "running").SetValue(session, false);
        Field(session, "status").SetValue(session, null);
    }

    static FieldInfo Field(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null)
        {
            throw new InvalidOperationException("поля «" + name + "» у сеанса нет — проба устарела");
        }

        return f;
    }

    static readonly ComponentResourceManager ViewResources =
        new ComponentResourceManager(typeof(FSAReportView));

    static string Own(string key)
    {
        return ViewResources.GetString(key) ?? key;
    }

    static string Mark(string key)
    {
        return Resources.ResourceManager.GetString(key) ?? key;
    }

    static void Language(string name)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(name);
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    static void Same(string what, object expected, object got)
    {
        checks++;
        bool ok = Equals(expected, got);
        if (!ok) bad++;
        Console.WriteLine("  {0} {1}: ждали «{2}», вышло «{3}»", ok ? "ок  " : "⛔ НЕТ", what,
                          Convert.ToString(expected, CultureInfo.InvariantCulture),
                          Convert.ToString(got, CultureInfo.InvariantCulture));
    }

    static void Denies(string what, bool wrong)
    {
        checks++;
        if (wrong) bad++;
        Console.WriteLine("  {0} {1}", wrong ? "⛔ НЕТ" : "ок  ", what);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (приём взят у `CultureProbeO14`, `A245`).
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

    static void ModalWatchStart()
    {
        modalWatchThread = new Thread(delegate()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var known = new Dictionary<long, bool>();
            while (!modalWatchStop)
            {
                var found = new List<IntPtr>();
                try
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != self) return true;
                        var cls = new StringBuilder(64);
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
                    bad++;
                    Console.WriteLine("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                        + "» — сторож закрывает его сам, разряд `A245`");
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
        var acc = new StringBuilder();
        try
        {
            EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
            {
                var cls = new StringBuilder(64);
                GetClassNameW(ch, cls, cls.Capacity);
                if (cls.ToString() == "Static")
                {
                    var txt = new StringBuilder(512);
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
}
