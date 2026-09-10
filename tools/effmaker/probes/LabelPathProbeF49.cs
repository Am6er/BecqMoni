// Полоса F49, строки `A190` (спор соседей у активности) и `A228` (второй путь
// подписи — под курсором).
//
//     LabelPathProbeF49 --spectra=<…\CORPUS\corpus\spectra> [--csv=f49.csv]
//                       [--out=<файл журнала>] [--no-sweep]
//                       [--expect-false-disputes=N] [--expect-inflated=N]
//                       [--expect-cursor-mismatch=N] [--modal-control]
//
// ЧТО МЕРИТСЯ И ПОЧЕМУ ИМЕННО ТАК.
//
// `A190`. Спор соседей у активности (`EnergySpectrumView.ScanActivityRivals`)
// считает соперником ЛЮБУЮ видимую линию набора с выходом больше нуля — в том
// числе такую, по которой сама же ветка активности считать ОТКАЗЫВАЕТ
// (характеристический рентген элемента, выход ниже `MinimumActivityYieldPercent`).
// Проба зовёт НАСТОЯЩИЙ метод отражением и рядом, в том же проходе, считает
// соперников тремя разрядами — настоящие / рентген / ниже порога, — и потому
// умеет назвать не только «завышено», но и ЦЕНУ решения: сколько предупреждений
// «рядом стоит рентген» исчезает.
//
// `A228`. Подпись под курсором (`EnergySpectrumView.EnsureCursorNuclidePeak`)
// отбирала кандидатов своими правилами, а таблица состава — правилами
// `PeakDetector` (`S134`, `S64`, `A197`, `A227`). Проба ставит курсор в
// найденный пик и сравнивает ДВА НАРИСОВАННЫХ ТЕКСТА: флажок под курсором и
// флажок пика — оба через `DrawPeakFlag`, снятые с отрисовки метафайлом EMF+.
// Своей копии форматирования у пробы нет: сравнивается то, что нарисовало
// приложение. Таблица состава берёт ту же строку (`DCPeakDetectionView:339`
// зовёт `PeakDetector.PeakLabel`, и `DrawPeakFlag` зовёт её же).
//
// ⛔ ОКНО `BecqMoni` НЕ ПОДНИМАЕТСЯ. `EnergySpectrumView` — `UserControl`: он
//    создаётся обычным конструктором и никуда не показывается, поля вьюпорта
//    ставятся отражением (приём `GraphCultureProbeF27`). Сторож модальных окон
//    стоит первым делом (`A245`): голое `MessageBox.Show` на безоконном пути
//    вешает прогон насмерть.
//
// ⛔ ПОРОГИ И ОКНА ЧИТАЮТСЯ ИЗ СБОРКИ (`GetRawConstantValue`), а не переписаны
//    сюда: своя копия константы вкомпилировалась бы намертво и продолжала бы
//    мерить старое число после смены порога.
//
// ⚠ ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ, без которых проба не меряет ничего:
//    1. снятие текста с отрисовки проверяется на заведомо известных строках —
//       «строк не найдено» неотличимо от «снятие не работает»;
//    2. ожидания (`--expect-…`) подменяют ОЖИДАНИЕ, а не то, что считает
//       приложение: прогон с заведомо неверным числом обязан ОТКАЗАТЬ;
//    3. без ключей ожидание встроенное — ноль ложных споров, ноль завышений,
//       ноль расхождений подписи, — и на сборке ДО правок проба обязана
//       ОТКАЗАТЬ и назвать места.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

static class LabelPathProbeF49
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
                             | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    // Пороги приложения — читаются из сборки в §0.
    static double activityYield;      // EnergySpectrumView.MinimumActivityYieldPercent
    static double labelMissInFwhm;    // PeakDetector.MaximumLabelMissInFwhm

    static Type tView, tAnalytics;
    static MethodInfo mScanRivals, mEnsureCursor, mDrawFlag;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string spectraDir = null, csvPath = "f49_labelpath.csv", outPath = null;
        bool sweep = true, modalControl = false;
        int? expFalse = null, expInflated = null, expCursor = null;

        foreach (string a in args)
        {
            if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
            else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a == "--no-sweep") sweep = false;
            else if (a == "--modal-control") modalControl = true;
            else if (a.StartsWith("--expect-false-disputes=", StringComparison.Ordinal))
                expFalse = int.Parse(a.Substring(24), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--expect-inflated=", StringComparison.Ordinal))
                expInflated = int.Parse(a.Substring(18), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--expect-cursor-mismatch=", StringComparison.Ordinal))
                expCursor = int.Parse(a.Substring(25), CultureInfo.InvariantCulture);
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
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
                : "окон назвал сторож: " + modalSeen + " (так и надо: это контроль сторожа)");
            ModalWatchStop();
            Finish(outPath);
            return failures == 0 ? 3 : 0;
        }

        Say("== полоса F49: `A190` спор соседей и `A228` подпись под курсором ==");
        Say("");

        if (!Section0_What(spectraDir)) { ModalWatchStop(); Finish(outPath); return 3; }
        Section0b_CaptureSelfTest();

        NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
        ReportLibrary(nuclides);

        if (string.IsNullOrEmpty(spectraDir) || !Directory.Exists(spectraDir))
        {
            Say("⛔ ключ --spectra не задан или каталога нет — мерить нечем");
            failures++;
            ModalWatchStop();
            Finish(outPath);
            return 3;
        }

        Corpus(nuclides, spectraDir, csvPath, sweep);
        Verdicts(expFalse, expInflated, expCursor);

        Say("");
        Say("модальных окон за прогон: " + modalSeen);
        Say(failures == 0 ? "РАСХОЖДЕНИЙ НЕТ" : "РАСХОЖДЕНИЙ: " + failures);
        ModalWatchStop();
        Finish(outPath);
        return failures == 0 ? 0 : 1;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  0. Чем мерено
    // ══════════════════════════════════════════════════════════════════════

    static bool Section0_What(string spectraDir)
    {
        Assembly app = typeof(EnergySpectrumView).Assembly;
        Say("сборка приложения: " + app.Location);
        try
        {
            Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                            .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            Say("sha256 сборки:     " + Sha16(app.Location));
        }
        catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }
        Say("рабочий каталог:   " + Directory.GetCurrentDirectory());
        Say("корпус:            " + (spectraDir ?? "(не задан)"));

        tView = typeof(EnergySpectrumView);
        tAnalytics = tView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
        mScanRivals = tView.GetMethod("ScanActivityRivals", Priv);
        mEnsureCursor = tView.GetMethod("EnsureCursorNuclidePeak", Priv);
        mDrawFlag = tView.GetMethod("DrawPeakFlag", Priv);
        if (tAnalytics == null || mScanRivals == null || mEnsureCursor == null || mDrawFlag == null)
        {
            Say("⛔ в сборке нет одного из: SelectionAnalytics / ScanActivityRivals /"
                + " EnsureCursorNuclidePeak / DrawPeakFlag — мерить нечем");
            failures++;
            return false;
        }

        FieldInfo fYield = tView.GetField("MinimumActivityYieldPercent",
                                          BindingFlags.Public | BindingFlags.Static);
        FieldInfo fMiss = typeof(PeakDetector).GetField("MaximumLabelMissInFwhm",
                                                        BindingFlags.Public | BindingFlags.Static);
        if (fYield == null || fMiss == null)
        {
            Say("⛔ в сборке нет MinimumActivityYieldPercent или MaximumLabelMissInFwhm");
            failures++;
            return false;
        }
        activityYield = Convert.ToDouble(fYield.GetRawConstantValue(), CultureInfo.InvariantCulture);
        labelMissInFwhm = Convert.ToDouble(fMiss.GetRawConstantValue(), CultureInfo.InvariantCulture);
        Say("порог активности ИЗ СБОРКИ:      " + N(activityYield) + " %");
        Say("окно подписи ИЗ СБОРКИ:          " + N(labelMissInFwhm) + " ПШПВ");
        Say("");
        return true;
    }

    static void ReportLibrary(NuclideDefinitionManager nuclides)
    {
        List<NuclideDefinition> defs = nuclides.NuclideDefinitions;
        int visible = 0, xray = 0, low = 0;
        foreach (NuclideDefinition d in defs)
        {
            if (d == null || !d.Visible || d.Energy == 0.0) continue;
            visible++;
            if (NuclideDefinition.IsElementXrayName(d.Name)) xray++;
            else if (d.Intencity > 0.0 && d.Intencity < activityYield) low++;
        }
        NuclideSet set = nuclides.ActiveSet;
        Say("библиотека: записей " + defs.Count + ", видимых с энергией " + visible
            + " (рентген " + xray + ", выход ниже порога " + low + ")"
            + ", активный набор " + (set == null ? "нет" : set.Name));
        string lib = Path.Combine(Directory.GetCurrentDirectory(), "config\\NuclideDefinition.xml");
        if (File.Exists(lib))
        {
            // ⛔ ОТПЕЧАТОК — ПО НОРМАЛИЗОВАННЫМ КОНЦАМ СТРОК (`T172`): сырой
            // sha256 у ОДНОЙ И ТОЙ ЖЕ библиотеки разный в дереве с LF и в
            // дереве с CRLF, и числа двух заходов переставали быть сравнимы.
            // Печатаются оба — отпечаток, чтобы сравнивать, сырой, чтобы было
            // видно, что дерево переписано.
            byte[] raw = File.ReadAllBytes(lib);
            Say("            " + lib + ", байт " + raw.Length
                + ", концы строк " + EolKind(raw)
                + ", отпечаток " + Sha16(NormalizeEol(raw))
                + ", сырой sha256 " + Sha16(raw));
        }
        else
        {
            Say("            ⛔ config\\NuclideDefinition.xml рядом НЕТ — прогон взял заготовку");
            failures++;
        }
        Say("");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  0b. Положительный контроль СНЯТИЯ ТЕКСТА С ОТРИСОВКИ
    //      «строк не найдено» неотличимо от «снятие не работает».
    // ══════════════════════════════════════════════════════════════════════

    static void Section0b_CaptureSelfTest()
    {
        Say("[полож. контроль СНЯТИЯ] метафайл отдаёт обратно то, что нарисовано:");
        List<string> got = Capture(delegate(Graphics g)
        {
            using (Font f = new Font("Segoe UI", 9f))
            {
                g.DrawString("Cs-137", f, Brushes.Black, new RectangleF(0, 0, 200, 20));
                g.DrawString("Ac-228 / Ra-226", f, Brushes.Red, 3f, 20f);
            }
        });
        bool ok = got.Count == 2 && got[0] == "Cs-137" && got[1] == "Ac-228 / Ra-226";
        Say("  снято " + got.Count + " строк: «" + string.Join("» | «", got.ToArray()) + "»"
            + (ok ? " — снятие работает" : "  ⛔ СНЯТИЕ НЕ РАБОТАЕТ, весь замер `A228` недействителен"));
        if (!ok) failures++;
        Say("");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Корпус: один проход, оба замера
    // ══════════════════════════════════════════════════════════════════════

    // --- `A190` -----------------------------------------------------------
    static int reach, refusedXray, refusedYield, noCoeff, shown;
    static int rivalsShown, falseDisputes, inflated, inflatedReal;
    static int withXrayRival, withLowRival, xrayWarningLost;
    static double inflatedWorst = 1.0;
    static string inflatedWhere = "";
    static readonly List<string> falseDisputeCases = new List<string>();
    static readonly List<string> inflatedCases = new List<string>();
    static readonly List<string> xrayWarningCases = new List<string>();
    static readonly SortedDictionary<string, int> xrayWarningSpectra = new SortedDictionary<string, int>();

    // --- `A228` -----------------------------------------------------------
    static int cursorPeaks, cursorMatch, cursorMismatch, cursorSilent, cursorSamePeak;
    static readonly List<string> cursorCases = new List<string>();
    static long sweepInside, sweepInsideMatch, sweepInsideMismatch, sweepInsideSilent;
    static long sweepOutside, sweepOutsideLabelled;
    static long sweepFlagged, sweepFlaggedNoPeak;
    static readonly List<string> sweepCases = new List<string>();

    static void Corpus(NuclideDefinitionManager nuclides, string spectraDir, string csvPath, bool sweep)
    {
        NuclideSet set = nuclides.ActiveSet;
        List<NuclideDefinition> defs = nuclides.NuclideDefinitions;

        var csv = new StringBuilder();
        csv.AppendLine("spectrum,peak_kev,fwhm_kev,label,line_kev,intensity_pct,"
                       + "rivals_app,factor_app,rivals_real,factor_real,rivals_xray,rivals_low,"
                       + "cursor_text,table_text,cursor_agrees");

        int spectra = 0, failed = 0, peaks = 0;

        using (Font font = new Font("Segoe UI", 9f))
        using (EnergySpectrumView view = new EnergySpectrumView())
        using (Pen pen = new Pen(Color.Black))
        using (Brush brush = new SolidBrush(Color.Black))
        using (Brush bgBrush = new SolidBrush(Color.White))
        {
            view.Font = font;
            SetupView(view, nuclides);
            object rivalView = FormatterServices.GetUninitializedObject(tView);
            Set(rivalView, "nuclideManager", nuclides);

            PropertyInfo pFwhmKev = tAnalytics.GetProperty("SelectionFWHMinkev");
            PropertyInfo pRivals = tAnalytics.GetProperty("ActivityRivals");
            PropertyInfo pFactor = tAnalytics.GetProperty("ActivityRivalFactor");

            foreach (string file in Directory.GetFiles(spectraDir, "*.xml")
                                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                ResultData rd;
                List<Peak> found;
                try
                {
                    rd = LoadResult(file);
                    found = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible,
                                                          SmoothingMethod.None, set, defs);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(name + ": " + e.Message);
                    failed++;
                    continue;
                }

                spectra++;
                if (found == null) found = new List<Peak>();
                peaks += found.Count;
                // ⛔ НАЙДЕННЫЕ ПИКИ КЛАДУТСЯ В САМ РЕЗУЛЬТАТ. `DetectPeak`
                //    только ВОЗВРАЩАЕТ список, а вид читает `DetectedPeaks` —
                //    без этой строки подпись под курсором молчала бы на всём
                //    корпусе, и молчание выглядело бы как дефект приложения.
                //    Поймано первым же прогоном плеча «после» (проба назвала
                //    место), а не глазами.
                rd.DetectedPeaks = found;
                bool hasCurve = rd.Efficiency != null;
                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;

                PointViewAt(view, rd);

                foreach (Peak peak in found)
                {
                    double fwhmKev = peak.FwhmKev(cal);
                    NuclideDefinition nd = peak.Nuclide;

                    // ── `A228`: курсор ставится В ПИК, оба текста снимаются с
                    //    отрисовки одним и тем же методом приложения.
                    string tableText = Draw(view, peak, pen, brush, bgBrush);
                    Peak underCursor = CursorPeakAt(view, cal, peak.Energy, peak.Channel);
                    string cursorText = underCursor == null
                        ? null
                        : Draw(view, underCursor, pen, brush, bgBrush);
                    cursorPeaks++;
                    // ⛔ СОВПАДЕНИЕ ТЕКСТА — ЕЩЁ НЕ ТОТ ЖЕ ПИК. Два соседних
                    //    пика могут нести одну подпись, и «сошлось» тогда
                    //    значило бы «показан не тот пик, но с тем же именем».
                    //    Поэтому рядом считается ТОЖДЕСТВО ОБЪЕКТА.
                    if (object.ReferenceEquals(underCursor, peak)) cursorSamePeak++;
                    bool agrees = cursorText != null && cursorText == tableText;
                    if (agrees) cursorMatch++;
                    else if (cursorText == null) { cursorSilent++; cursorMismatch++; }
                    else cursorMismatch++;
                    if (!agrees && cursorCases.Count < 400)
                    {
                        cursorCases.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0,-24} {1,9} кэВ: под курсором «{2}», в таблице «{3}»",
                            Cut(name, 24), N(peak.Energy), cursorText ?? "(нет)", tableText));
                    }

                    // ── `A190`: спор соседей у активности.
                    if (nd == null || !(nd.Intencity > 0.0) || !hasCurve || !(fwhmKev > 0.0))
                    {
                        continue;
                    }
                    reach++;
                    if (NuclideDefinition.IsElementXrayName(nd.Name)) { refusedXray++; continue; }
                    if (nd.Intencity < activityYield) { refusedYield++; continue; }
                    double k, dk;
                    if (!BecquerelCoefficient.TryForLine(peak.Energy, nd.Intencity, rd.Efficiency, out k, out dk))
                    {
                        noCoeff++;
                        continue;
                    }
                    shown++;

                    object an = Activator.CreateInstance(tAnalytics, true);
                    pFwhmKev.SetValue(an, fwhmKev, null);
                    mScanRivals.Invoke(rivalView, new object[] { peak, an });
                    int rivals = (int)pRivals.GetValue(an, null);
                    double factor = (double)pFactor.GetValue(an, null);

                    // Разряды соперников считаются ЗДЕСЬ, независимо от
                    // приложения: только так «завышено» отличается от «сошлось».
                    int realRivals = 0, xrayRivals = 0, lowRivals = 0;
                    double realFactor = 1.0;
                    NuclideDefinition best = null, bestXray = null;
                    double window = 0.5 * fwhmKev;
                    foreach (NuclideDefinition d in defs)
                    {
                        if (d == null || !d.Visible || d.Energy == 0.0) continue;
                        if (set != null && (d.Sets == null || !d.Sets.Contains(set.Id))) continue;
                        if (!(d.Intencity > 0.0)) continue;
                        if (d.Name == nd.Name && Math.Abs(d.Energy - nd.Energy) < 1e-9) continue;
                        if (Math.Abs(d.Energy - peak.Energy) > window) continue;

                        if (NuclideDefinition.IsElementXrayName(d.Name))
                        {
                            xrayRivals++;
                            if (bestXray == null) bestXray = d;
                            continue;
                        }
                        if (d.Intencity < activityYield) { lowRivals++; continue; }

                        realRivals++;
                        double f = nd.Intencity / d.Intencity;
                        if (f < 1.0) f = 1.0 / f;
                        if (f > realFactor) { realFactor = f; best = d; }
                    }

                    if (rivals > 0) rivalsShown++;
                    if (rivals > 0 && realRivals == 0)
                    {
                        falseDisputes++;
                        if (falseDisputeCases.Count < 400)
                        {
                            falseDisputeCases.Add(string.Format(CultureInfo.InvariantCulture,
                                "{0,-24} {1,9} кэВ «{2}»: соперников {3} (рентген {4}, ниже порога {5}),"
                                + " настоящих НЕТ, панель «до ×{6}»",
                                Cut(name, 24), N(peak.Energy), nd.Name, rivals, xrayRivals, lowRivals,
                                factor.ToString("F2", CultureInfo.InvariantCulture)));
                        }
                    }
                    if (rivals > 0 && factor > realFactor * (1.0 + 1e-9))
                    {
                        inflated++;
                        if (realRivals > 0) inflatedReal++;
                        double ratio = factor / realFactor;
                        if (ratio > inflatedWorst)
                        {
                            inflatedWorst = ratio;
                            inflatedWhere = string.Format(CultureInfo.InvariantCulture,
                                "{0} @ {1} кэВ, подпись «{2}»: панель «до ×{3}», по настоящим ×{4}",
                                name, N(peak.Energy), nd.Name,
                                factor.ToString("F2", CultureInfo.InvariantCulture),
                                realFactor.ToString("F2", CultureInfo.InvariantCulture));
                        }
                        if (inflatedCases.Count < 400)
                        {
                            inflatedCases.Add(string.Format(CultureInfo.InvariantCulture,
                                "{0,-24} {1,9} кэВ «{2}»: панель ×{3}, по настоящим ×{4} ({5}×)",
                                Cut(name, 24), N(peak.Energy), nd.Name,
                                factor.ToString("F2", CultureInfo.InvariantCulture),
                                realFactor.ToString("F2", CultureInfo.InvariantCulture),
                                ratio.ToString("F1", CultureInfo.InvariantCulture)));
                        }
                    }
                    // ЦЕНА РЕШЕНИЯ Amber: предупреждение «рядом стоит рентген».
                    if (xrayRivals > 0)
                    {
                        withXrayRival++;
                        int had;
                        xrayWarningSpectra.TryGetValue(name, out had);
                        xrayWarningSpectra[name] = had + 1;
                        if (realRivals == 0 && lowRivals == 0) xrayWarningLost++;
                        if (xrayWarningCases.Count < 400)
                        {
                            xrayWarningCases.Add(string.Format(CultureInfo.InvariantCulture,
                                "{0,-24} {1,9} кэВ «{2}»: рентген «{3}» в том же пике,"
                                + " прочих соперников {4}",
                                Cut(name, 24), N(peak.Energy), nd.Name,
                                bestXray == null ? "?" : bestXray.Name, realRivals + lowRivals));
                        }
                    }
                    if (lowRivals > 0) withLowRival++;

                    csv.AppendLine(string.Join(",", name.Replace(',', ';'), F(peak.Energy, "F3"),
                        F(fwhmKev, "F3"), nd.Name.Replace(',', ';'), F(nd.Energy, "F3"),
                        F(nd.Intencity, "G6"),
                        rivals.ToString(CultureInfo.InvariantCulture), F(factor, "F3"),
                        realRivals.ToString(CultureInfo.InvariantCulture), F(realFactor, "F3"),
                        xrayRivals.ToString(CultureInfo.InvariantCulture),
                        lowRivals.ToString(CultureInfo.InvariantCulture),
                        (cursorText ?? "(нет)").Replace(',', ';'),
                        (tableText ?? "(нет)").Replace(',', ';'),
                        agrees ? "1" : "0"));
                }

                if (sweep) Sweep(view, name, rd, found, cal);
            }

            File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
            Say("спектров " + spectra + " (отказало " + failed + "), пиков " + peaks);
            Say("выгрузка: " + Path.GetFullPath(csvPath));
            Say("");
        }

        ReportA190();
        ReportA228(sweep);
    }

    /// <summary>
    /// Развёртка по ВСЕЙ шкале: курсор ставится в каждый канал спектра.
    /// Отвечает на два вопроса разом — совпадает ли подпись под курсором с
    /// подписью пика ВНУТРИ пика, и как часто подпись показывается ВНЕ пиков
    /// (это и есть цена решения `A228` для человека за экраном).
    ///
    /// «Внутри пика» — полПШПВ этого пика: та же мерка, какой приложение мерит
    /// «линия попала в тот же пик» у спора соседей.
    /// </summary>
    static void Sweep(EnergySpectrumView view, string name, ResultData rd, List<Peak> found, EnergyCalibration cal)
    {
        EnergySpectrum s = rd.EnergySpectrum;
        int channels = s.NumberOfChannels;
        double[] fwhm = new double[found.Count];
        for (int i = 0; i < found.Count; i++) fwhm[i] = found[i].FwhmKev(cal);

        for (int ch = 0; ch < channels; ch++)
        {
            double e = cal.ChannelToEnergy(ch);
            Peak inside = null;
            double bestMiss = 0.0;
            bool anyNear = false;      // хоть один найденный пик в окне подписи
            for (int i = 0; i < found.Count; i++)
            {
                if (!(fwhm[i] > 0.0)) continue;
                double miss = Math.Abs(e - found[i].Energy);
                if (miss <= labelMissInFwhm * fwhm[i]) anyNear = true;
                if (miss > 0.5 * fwhm[i]) continue;
                if (inside == null || miss < bestMiss) { inside = found[i]; bestMiss = miss; }
            }

            Peak cursor = CursorPeakAt(view, cal, e, ch);
            string cursorLabel = cursor == null ? null : LabelOf(cursor);
            if (cursorLabel != null)
            {
                sweepFlagged++;
                // ⛔ ГЛАВНОЕ ЧИСЛО ЦЕНЫ И ПОЛЬЗЫ: подпись показана там, где
                //    НАЙДЕННОГО ПИКА НЕТ ВОВСЕ (ни одного ближе окна подписи
                //    приложения). Это имя нуклида над голым континуумом —
                //    утверждение о пробе, которого не подтверждает ни одно
                //    правило подписи.
                if (!anyNear) sweepFlaggedNoPeak++;
            }

            if (inside != null)
            {
                sweepInside++;
                string want = LabelOf(inside);
                if (cursorLabel == null) { sweepInsideSilent++; sweepInsideMismatch++; }
                else if (cursorLabel == want) sweepInsideMatch++;
                else
                {
                    sweepInsideMismatch++;
                    if (sweepCases.Count < 60)
                    {
                        sweepCases.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0,-24} {1,9} кэВ: под курсором «{2}», пик под курсором «{3}» @{4} кэВ",
                            Cut(name, 24), N(e), cursorLabel, want, N(inside.Energy)));
                    }
                }
            }
            else
            {
                sweepOutside++;
                if (cursorLabel != null) sweepOutsideLabelled++;
            }
        }
    }

    static void ReportA190()
    {
        Say("=== `A190`: спор соседей у активности ===");
        Say("  до ветки активности доходят " + reach + "; ОТКАЗ рентген " + refusedXray
            + ", ОТКАЗ выход ниже порога " + refusedYield
            + ", коэффициент не получен " + noCoeff + "; ПОКАЗАНО числом " + shown);
        Say("  показано СО СПОРОМ                                   " + rivalsShown);
        Say("  спор ТОЛЬКО по отказным линиям (ложный спор)         " + falseDisputes);
        Say("  множитель на панели ЗАВЫШЕН, всего                   " + inflated);
        Say("    из них там, где настоящие соперники ЕСТЬ           " + inflatedReal);
        if (inflatedWhere.Length > 0)
        {
            Say("  худшее завышение: " + inflatedWhere + " — в "
                + inflatedWorst.ToString("F1", CultureInfo.InvariantCulture) + " раз");
        }
        Say("");
        Say("  ЦЕНА РЕШЕНИЯ Amber (названа и принята): предупреждение «рядом рентген»");
        Say("  пиков, у которых в том же пике стоит линия РЕНТГЕНА  " + withXrayRival
            + "  на спектрах: " + xrayWarningSpectra.Count);
        Say("    из них теряют предупреждение ЦЕЛИКОМ               " + xrayWarningLost);
        Say("  пиков с соперником НИЖЕ ПОРОГА выхода                " + withLowRival);
        foreach (var kv in xrayWarningSpectra)
        {
            Say("      " + kv.Key + " — пиков " + kv.Value);
        }
        Say("");
        Print("  ложные споры (до 12):", falseDisputeCases, 12);
        Print("  завышения множителя (до 12):", inflatedCases, 12);
        Print("  предупреждения «рядом рентген» (до 12):", xrayWarningCases, 12);
        Say("");
    }

    static void ReportA228(bool sweep)
    {
        Say("=== `A228`: подпись под курсором против подписи пика ===");
        Say("  пиков проверено (курсор ставится В ПИК)              " + cursorPeaks);
        Say("    подписи СОВПАЛИ                                   " + cursorMatch);
        Say("    РАЗОШЛИСЬ                                         " + cursorMismatch);
        Say("      из них курсор молчит вовсе                      " + cursorSilent);
        Say("    под курсором ТОТ ЖЕ ОБЪЕКТ-ПИК, а не тёзка        " + cursorSamePeak);
        Print("  расхождения (до 15):", cursorCases, 15);
        if (sweep)
        {
            Say("");
            Say("  развёртка по ВСЕЙ шкале (курсор в каждый канал корпуса):");
            Say("    позиций ВНУТРИ найденного пика                  " + sweepInside);
            Say("      подпись совпала с подписью пика               " + sweepInsideMatch);
            Say("      разошлась                                     " + sweepInsideMismatch);
            Say("        из них курсор молчит                        " + sweepInsideSilent);
            Say("    позиций ВНЕ всех пиков (дальше полПШПВ)         " + sweepOutside);
            Say("      и там показана подпись                        " + sweepOutsideLabelled
                + " = " + (100.0 * sweepOutsideLabelled / Math.Max(1L, sweepOutside))
                             .ToString("F1", CultureInfo.InvariantCulture) + " % шкалы вне пиков");
            Say("    положений с флажком ВСЕГО                       " + sweepFlagged
                + " из " + (sweepInside + sweepOutside));
            Say("      ⛔ из них там, где НАЙДЕННОГО ПИКА НЕТ ВОВСЕ    " + sweepFlaggedNoPeak
                + " (имя нуклида над голым континуумом)");
            Print("    расхождения развёртки (до 10):", sweepCases, 10);
        }
        Say("");
    }

    /// <summary>
    /// Сверка с ожиданием. ⛔ Ожидание ВСТРОЕННОЕ — ноль, ноль и ноль; ключи
    /// `--expect-…` его ПОДМЕНЯЮТ и нужны только положительному контролю:
    /// прогон с заведомо неверным числом обязан отказать, и на сборке ДО правок
    /// проба обязана отказать сама.
    /// </summary>
    static void Verdicts(int? expFalse, int? expInflated, int? expCursor)
    {
        Say("=== приёмка ===");
        Check("ложных споров", falseDisputes, expFalse ?? 0, falseDisputeCases);
        Check("завышений множителя", inflated, expInflated ?? 0, inflatedCases);
        Check("расхождений подписи под курсором", cursorMismatch, expCursor ?? 0, cursorCases);
    }

    static void Check(string what, int got, int want, List<string> cases)
    {
        if (got == want)
        {
            Say("  " + what + ": " + got + " — сошлось с ожиданием");
            return;
        }
        Say("  ⛔ " + what + ": " + got + ", ждали " + want + " — ОТКАЗ");
        foreach (string c in cases.Take(5)) Say("      " + c);
        failures++;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Вид без окна
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Поля вьюпорта: вид собран без формы, поэтому шкалу, размеры и курсор
    /// надо выставить руками (приём `GraphCultureProbeF27.Setup`).
    /// </summary>
    static void SetupView(EnergySpectrumView view, NuclideDefinitionManager nuclides)
    {
        Set(view, "nuclideManager", nuclides);
        Set(view, "left", 1);
        Set(view, "width", 900);
        Set(view, "height", 600);
        Set(view, "bottom", 16);
        Set(view, "scrollX", 0);
        Set(view, "scrollY", 0);
        Set(view, "horizontalScale", 1.0);
        Set(view, "verticalScale", 1.0);
        Set(view, "pixelPerEnergy", 0.4);
        Set(view, "energyViewOffset", 0.0);
        Set(view, "horizontalUnit", HorizontalUnit.Energy);
        Set(view, "backgroundMode", BackgroundMode.Invisible);
        Set(view, "peakMode", PeakMode.Visible);
        Set(view, "validCursor", true);
        Set(view, "cursorX", 400);
    }

    static void PointViewAt(EnergySpectrumView view, ResultData rd)
    {
        EnergySpectrum s = rd.EnergySpectrum;
        Set(view, "activeResultData", rd);
        Set(view, "energySpectrum", s);
        Set(view, "energyCalibration", s.EnergyCalibration);
        Set(view, "baseEnergyCalibration", s.EnergyCalibration);
        Set(view, "numberOfChannels", s.NumberOfChannels);
        Set(view, "totalMaxChannel", s.NumberOfChannels);
    }

    /// <summary>Подпись под курсором, поставленным в точку шкалы.</summary>
    static Peak CursorPeakAt(EnergySpectrumView view, EnergyCalibration cal, double energyKev, int channel)
    {
        Set(view, "cursorEnergy", energyKev);
        Set(view, "cursorChannel", channel);
        Set(view, "nuclideCursorPeakDirty", true);
        mEnsureCursor.Invoke(view, null);
        return (Peak)tView.GetField("nuclideCursorPeak", Priv).GetValue(view);
    }

    /// <summary>
    /// Текст флажка — СНЯТЫЙ С ОТРИСОВКИ. Своей копии форматирования у пробы
    /// нет: рисует `DrawPeakFlag` приложения, проба лишь читает метафайл.
    /// </summary>
    static string Draw(EnergySpectrumView view, Peak peak, Pen pen, Brush brush, Brush bgBrush)
    {
        List<string> texts = Capture(delegate(Graphics g)
        {
            mDrawFlag.Invoke(view, new object[] { g, peak, 40, 2, pen, brush, bgBrush });
        });
        return texts.Count == 0 ? "(нарисовано пусто)" : string.Join(" | ", texts.ToArray());
    }

    /// <summary>
    /// Подпись пика ТЕМ ЖЕ выражением, каким её берёт таблица состава
    /// (`DCPeakDetectionView`): `PeakDetector.PeakLabel`, а без подписи —
    /// `Resources.UnknownNuclide`. Для развёртки по всей шкале рисовать
    /// метафайл на каждый канал слишком дорого, поэтому здесь зовётся тот же
    /// метод приложения напрямую.
    /// </summary>
    static string LabelOf(Peak peak)
    {
        string s = PeakDetector.PeakLabel(peak);
        return s ?? "(?)";
    }

    static void Set(object target, string field, object value)
    {
        FieldInfo f = tView.GetField(field, Priv);
        if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + field);
        f.SetValue(target, value);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Чтение спектра корпуса — тем же порядком, что у `BqActivityProbe`
    // ══════════════════════════════════════════════════════════════════════

    static ResultData LoadResult(string path)
    {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ResultDataFile));
        ResultDataFile file;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            file = (ResultDataFile)serializer.Deserialize(stream);
        }
        ResultData rd = file.ResultDataList.First();
        EnergySpectrum s = rd.EnergySpectrum;
        if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
        {
            long total = 0;
            for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
            s.TotalPulseCount = total;
            s.ValidPulseCount = total;
        }
        var pcal = s != null ? s.EnergyCalibration as PolynomialEnergyCalibration : null;
        if (pcal != null) pcal.CheckCalibration(s.NumberOfChannels);

        string note = ProbeDeviceConfig.Attach(rd);
        if (note.IndexOf("НЕТ", StringComparison.Ordinal) >= 0)
        {
            Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path) + ": " + note);
        }
        if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig))
        {
            throw new InvalidDataException("нет настроек поиска пиков ни в спектре, ни в приборе");
        }
        if (rd.FwhmCalibration == null)
        {
            var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
            rd.FwhmCalibration = cfg.FwhmCalibration
                ?? FwhmCalibration.DefaultCalibration(cfg, s.EnergyCalibration);
        }
        return rd;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Снятие текста с отрисовки (образец — `GraphCultureProbeF27`)
    //
    //  У записи EmfPlusDrawString данные лежат так:
    //  BrushId(4) FormatID(4) Length(4) LayoutRect(16) String(Length×2, UTF-16).
    //  Отсюда смещение 28.
    // ══════════════════════════════════════════════════════════════════════

    static List<string> drawn;

    static List<string> Capture(Action<Graphics> draw)
    {
        List<string> texts = new List<string>();
        using (Bitmap host = new Bitmap(8, 8))
        using (Graphics rg = Graphics.FromImage(host))
        {
            IntPtr hdc = rg.GetHdc();
            Metafile mf;
            try
            {
                mf = new Metafile(hdc, new RectangleF(0, 0, 1200, 900),
                                  MetafileFrameUnit.Pixel, EmfType.EmfPlusOnly);
            }
            finally { rg.ReleaseHdc(hdc); }

            try
            {
                using (Graphics g = Graphics.FromImage(mf))
                {
                    draw(g);
                }

                drawn = texts;
                using (Bitmap play = new Bitmap(4, 4))
                using (Graphics pg = Graphics.FromImage(play))
                {
                    pg.EnumerateMetafile(mf, new Point(0, 0), OnRecord);
                }
            }
            finally
            {
                drawn = null;
                mf.Dispose();
            }
        }

        return texts;
    }

    static bool OnRecord(EmfPlusRecordType recordType, int flags, int dataSize,
                         IntPtr data, PlayRecordCallback cb)
    {
        if (recordType == EmfPlusRecordType.DrawString && data != IntPtr.Zero && dataSize >= 28
            && drawn != null)
        {
            byte[] buf = new byte[dataSize];
            Marshal.Copy(data, buf, 0, dataSize);
            int len = BitConverter.ToInt32(buf, 8);
            if (len >= 0 && 28 + len * 2 <= dataSize)
            {
                drawn.Add(Encoding.Unicode.GetString(buf, 28, len * 2));
            }
            else
            {
                drawn.Add("(ЗАПИСЬ НЕ РАЗОБРАНА len=" + len + " size=" + dataSize + ")");
            }
        }

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Сторож модальных окон (образец — `CultureProbeO14`, `A245`)
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

    static void ModalControl()
    {
        Say("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
        int before = modalSeen;
        DateTime t0 = DateTime.UtcNow;
        Thread th = new Thread(delegate()
        {
            System.Windows.Forms.MessageBox.Show("контрольное окно полосы F49",
                                                 "контроль", System.Windows.Forms.MessageBoxButtons.OK);
        });
        th.IsBackground = true;
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        bool closed = th.Join(20000);
        double sec = (DateTime.UtcNow - t0).TotalSeconds;
        Say("  окно поднято нарочно, закрыто сторожем: " + (closed ? "ДА" : "НЕТ")
            + ", секунд " + sec.ToString("F1", CultureInfo.InvariantCulture)
            + ", окон назвал сторож: " + (modalSeen - before));
        if (!closed)
        {
            Say("  ⛔ СТОРОЖ НЕ РАБОТАЕТ: окно не закрыто за 20 с");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Мелочи. ⛔ Числа печатаются ИНВАРИАНТНОЙ культурой, группировки
    //  разрядов нет вовсе (`A244`).
    // ══════════════════════════════════════════════════════════════════════

    static void Print(string title, List<string> cases, int top)
    {
        if (cases.Count == 0) return;
        Say(title);
        foreach (string c in cases.Take(top)) Say("    " + c);
        if (cases.Count > top) Say("    … всего " + cases.Count);
    }

    static string N(double v)
    {
        return v.ToString("0.######", CultureInfo.InvariantCulture);
    }

    static string F(double v, string fmt)
    {
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }

    static string Cut(string s, int n)
    {
        if (s == null) return "";
        return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }

    static string Sha16(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant().Substring(0, 16);
        }
    }

    static string Sha16(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant().Substring(0, 16);
        }
    }

    /// <summary>
    /// Концы строк, СЧИТАННЫЕ ПО БАЙТАМ (`T172`). Разбирать файл как текст тут
    /// нельзя: три способа счёта уже давали три разных числа.
    /// </summary>
    static string EolKind(byte[] b)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] == 13)
            {
                if (i + 1 < b.Length && b[i + 1] == 10) { crlf++; i++; }
                else cr++;
            }
            else if (b[i] == 10) lf++;
        }

        if (crlf + lf + cr == 0) return "нет переводов строк";
        if (lf == 0 && cr == 0) return "CRLF";
        if (crlf == 0 && cr == 0) return "LF";
        return string.Format(CultureInfo.InvariantCulture,
                             "смешанные (CRLF {0}, LF {1}, CR {2})", crlf, lf, cr);
    }

    /// <summary>
    /// CRLF → LF, одиночный CR → LF. Нормализуются ТОЛЬКО концы строк:
    /// пробелы, отступы и BOM — это содержимое (`T172`).
    /// </summary>
    static byte[] NormalizeEol(byte[] b)
    {
        var outBytes = new List<byte>(b.Length);
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] == 13)
            {
                outBytes.Add(10);
                if (i + 1 < b.Length && b[i + 1] == 10) i++;
            }
            else outBytes.Add(b[i]);
        }

        return outBytes.ToArray();
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
            string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(false));
            Console.WriteLine("вывод: " + Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine("вывод НЕ записан: " + ex.Message);
        }
    }
}
