using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace SelectionPanelProbeG10
{
    /// <summary>
    /// ПРИЁМКА `A193` и `A195` КАДРОМ — полоса G10, 06.09.2026.
    ///
    ///     selectionpanelprobeg10 --spectrum=&lt;файл корпуса&gt; [--shots=&lt;каталог снимков&gt;]
    ///                            [--ref=&lt;g10-panel.txt другой сборки&gt;] [--width=1200] [--height=620]
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ (образец — `PeakHighlightProbeG9`):
    /// `MainForm` заводится и не показывается; документ — НАСТОЯЩИЙ
    /// `DocEnergySpectrum` из файла корпуса в форме-носителе за краем экрана;
    /// пики находит НАСТОЯЩАЯ панель `DCPeakDetectionView` тем же путём, что в
    /// приложении; кадр снимает НАСТОЯЩИЙ `EnergySpectrumView.OnPaint`
    /// (`DrawToBitmap`). Своей отрисовки у пробы нет нарочно.
    ///
    /// Что меряется, по пяти плечам и на ОБЕИХ культурах (en, ru):
    ///
    ///   honest   кривая есть, фон есть, нетто &gt; 0     — число; кадр и числа те же, что раньше
    ///   nocurve  кривой у спектра нет                  — `A193`: отказ словами `ActivityNoCurveRefused`
    ///   netneg   фон вдвое выше спектра, нетто &lt; 0     — `A193`: отказ словами `ActivityNetNotPositiveRefused`
    ///   lczero   фон есть, но в выделении пуст, Lc = 0 — `A195`: панель НЕ резервирует 54 px под число;
    ///                                                    `A259` (полоса F66, 06.09.2026): отказ словами
    ///                                                    `ActivityLcZeroRefused` — число ПОСЧИТАНО, но
    ///                                                    на панель не выходит, и молчания больше нет
    ///   nobg     фонового спектра нет                  — контроль ~~`A192`~~: отказ тот же, кадр тот же
    ///
    /// Для каждого плеча печатаются: текст отказа из аналитики вида против
    /// ресурса ЭТОЙ сборки в ЭТОЙ культуре, высота серой панели выделения в
    /// точках (ограничивающий прямоугольник цвета `DarkGray`), sha256 кадра.
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ В ТОМ ЖЕ ПРОЦЕССЕ: у плеч с отказом подпись и
    /// отказ в аналитике обнуляются (без пересчёта) и кадр снимается снова —
    /// это ровно то, что рисовал прежний код (он ни подписи, ни отказа не
    /// заводил); число изменившихся точек в области панели обязано быть больше
    /// нуля, а панель — ниже на 16 (подпись) + 16 (строка K) + 16 × строк отказа
    /// (+ 6 отступа при Lc = 0). Строк отказа проба меряет тем же
    /// `Graphics.MeasureString`, которым вид переносит текст: прежний код
    /// резервировал 48 всегда, и вторая строка длинного отказа ложилась поверх
    /// черты и «Peak Counts» (найдено этой пробой, починено той же полосой).
    ///
    /// ⛔ ПЛЕЧО «ДО» — ТА ЖЕ ПРОБА ПРОТИВ СБОРКИ БЕЗ ПРАВКИ: она сама узнаёт
    /// сборку по ДВУМ признакам — есть ли `EnergySpectrumView.RefuseActivity`
    /// (правка `A193`/`A195`, полоса G10) и есть ли строка ресурса
    /// `ActivityLcZeroRefused` (правка `A259`, полоса F66), — и ждёт от старой
    /// МОЛЧАНИЯ (ни подписи, ни отказа) на `nocurve`/`netneg` и на `lczero`.
    /// Сводка каждого прогона пишется в `&lt;shots&gt;\g10-panel.txt` и открывается
    /// строкой `#` с этими двумя признаками; ключ `--ref=` сличает текущий
    /// прогон со сводкой другой сборки, и ПРАВИЛО СЛИЧЕНИЯ ВЫБИРАЕТСЯ ПО ТОМУ,
    /// ЧЕМ СБОРКИ РАЗЛИЧАЮТСЯ:
    ///
    ///   различие `A193`/`A195` (G10): `honest` — sha256 кадров равны и высоты
    ///     равны; `lczero` — высота старой минус новой = 54; `nocurve` — высота
    ///     новой минус старой = 48; `netneg` — 54 и `nobg` — 6 (при Lc = 0 с
    ///     подписью прежний код не резервировал 6 px отступа перед блоком ПШПВ,
    ///     и последняя строка свисала с заливки);
    ///
    ///   различие `A259` (F66): ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — `honest`, `nocurve`,
    ///     `netneg` и `nobg` обязаны совпасть ПОБАЙТНО (sha256) и по высоте, а
    ///     сдвинуться обязано ТОЛЬКО плечо `lczero`: панель с отказом выше
    ///     молчащей на 38 + 16 × строк (16 подписи + 6 отступа при Lc = 0 +
    ///     16 строки «Activity Bq: no K» + 16 × строк переноса отказа).
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;
        static string shotDir;
        static bool fixedBuild;
        /// <summary>Есть ли в сборке правка `A259` (полоса F66) — по строке ресурса.</summary>
        static bool lcZeroFixed;
        static readonly List<string> summary = new List<string>();

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Края синтетической кривой, кэВ — как у `BqActivityProbe`.</summary>
        const double CurveMin = 50.0, CurveMax = 2000.0;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, refPath = null;
            int width = 1200, height = 620;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shotDir = a.Substring(8);
                else if (a.StartsWith("--ref=", StringComparison.Ordinal)) refPath = a.Substring(6);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            // Каталог снимков — ПЕРВОЙ строкой (`T231`): без ключа — в %TEMP%,
            // а не в текущий каталог, чтобы кадры не оседали в корне дерева.
            bool shotsGiven = !string.IsNullOrEmpty(shotDir);
            if (!shotsGiven) shotDir = Path.Combine(Path.GetTempPath(), "selectionpanel-shots");
            shotDir = Path.GetFullPath(shotDir);
            Directory.CreateDirectory(shotDir);
            Console.WriteLine(shotsGiven ? "снимки: " + shotDir + " (ключ --shots=)"
                                         : "снимки: " + shotDir + " (ключ --shots= не задан, каталог по умолчанию)");
            Console.WriteLine(ProbeTargetFramework.Describe());

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            fixedBuild = typeof(EnergySpectrumView).GetMethod("RefuseActivity", BindingFlags.Instance | BindingFlags.NonPublic) != null;
            Console.WriteLine("SETUP\tсборка: {0}", fixedBuild
                ? "С ПРАВКОЙ A193/A195 (есть EnergySpectrumView.RefuseActivity) — ждём отказы словами"
                : "БЕЗ ПРАВКИ (плечо «до») — ждём молчание и +54 px при Lc = 0");
            // `A259` (F66): признак — строка ресурса, а не метод: отказ ставится
            // в `EnsureSelectionAnalytics`, своего метода у него нет.
            lcZeroFixed = AppResources.GetString("ActivityLcZeroRefused", CultureInfo.InvariantCulture) != null;
            Console.WriteLine("SETUP\tсборка: {0}", lcZeroFixed
                ? "С ПРАВКОЙ A259 (есть ресурс ActivityLcZeroRefused) — при Lc = 0 ждём отказ словами"
                : "БЕЗ ПРАВКИ A259 (плечо «до») — при Lc = 0 ждём МОЛЧАНИЕ при посчитанном числе");
            Console.WriteLine("SETUP\tприложение: {0}", typeof(EnergySpectrumView).Assembly.Location);

            MainForm mainForm = new MainForm();
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            Form panelHost = Host(panel, 520, height);
            DocEnergySpectrum doc = OpenDocument(spectrumPath);
            Form docHost = Host(doc, width, height);
            EnergySpectrumView view = doc.EnergySpectrumView;
            view.PeakMode = PeakMode.Visible;
            view.FitHorizontalScale();

            mainForm.ActiveDocument = doc;
            panel.ShowPeakDetectionResult();
            Pump(panel);

            ResultData rd = doc.ActiveResultData;
            Peak picked = PickPeak(rd.DetectedPeaks);
            if (picked == null)
            {
                Console.WriteLine("подписанного пика с выходом не ниже порога нет — мерить нечего");
                return 1;
            }
            int half = Math.Max(2, (int)Math.Round(picked.FWHM));
            view.SelectionStart = Math.Max(0, picked.Channel - half);
            view.SelectionEnd = Math.Min(rd.EnergySpectrum.NumberOfChannels - 1, picked.Channel + half);
            Console.WriteLine("SETUP\tпик {0} кэВ, канал {1}, ПШПВ {2} каналов, подпись «{3}» {4} кэВ I = {5} %; выделение каналы {6}…{7}",
                              F(picked.Energy, 2), picked.Channel, F(picked.FWHM, 1), picked.Nuclide.Name,
                              F(picked.Nuclide.Energy, 2), picked.Nuclide.Intencity.ToString("g4", CultureInfo.InvariantCulture),
                              view.SelectionStart, view.SelectionEnd);

            // Синтетическая кривая — у корпусного прибора кривой нет, а плечу
            // `honest` она нужна; те же три точки, что у `BqActivityProbe`.
            var curve = new EfficiencyConfigData("проба G10");
            curve.Curve = new List<ROIEfficiencyData>
            {
                new ROIEfficiencyData { Energy = CurveMin, Efficiency = 2.0e-2, ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = 662.0,    Efficiency = 1.0e-3, ErrorPercent = 5.0 },
                new ROIEfficiencyData { Energy = CurveMax, Efficiency = 1.0e-4, ErrorPercent = 8.0 },
            };
            EnergySpectrum fg = rd.EnergySpectrum;

            foreach (string culture in new[] { "en", "ru" })
            {
                Console.WriteLine();
                Console.WriteLine("=== культура {0} ===", culture);
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

                Arm("honest", culture, doc, view, () => { rd.Efficiency = curve; rd.BackgroundEnergySpectrum = Scaled(fg, 0.1, fg.MeasurementTime); },
                    expectRefusalKey: null, expectNumber: true, expectLcZero: false);
                Arm("nocurve", culture, doc, view, () => { rd.Efficiency = null; rd.BackgroundEnergySpectrum = Scaled(fg, 0.1, fg.MeasurementTime); },
                    expectRefusalKey: "ActivityNoCurveRefused", expectNumber: false, expectLcZero: false);
                Arm("netneg", culture, doc, view, () => { rd.Efficiency = curve; rd.BackgroundEnergySpectrum = Scaled(fg, 2.0, fg.MeasurementTime); },
                    expectRefusalKey: "ActivityNetNotPositiveRefused", expectNumber: false, expectLcZero: false);
                Arm("lczero", culture, doc, view, () => { rd.Efficiency = curve; rd.BackgroundEnergySpectrum = Scaled(fg, 0.0, fg.MeasurementTime); },
                    expectRefusalKey: "ActivityLcZeroRefused", expectNumber: false, expectLcZero: true);
                Arm("nobg", culture, doc, view, () => { rd.Efficiency = curve; rd.BackgroundEnergySpectrum = null; },
                    expectRefusalKey: "ActivityNoBackgroundRefused", expectNumber: false, expectLcZero: false, refusalIsOld: true);
            }
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en");

            // Признаки сборки — ПЕРВОЙ строкой сводки: по ним `--ref=` выбирает
            // правило сличения (чем именно две сборки различаются), а не гадает.
            summary.Insert(0, "#\tg10=" + (fixedBuild ? "1" : "0") + "\ta259=" + (lcZeroFixed ? "1" : "0"));
            string summaryPath = Path.Combine(shotDir, "g10-panel.txt");
            File.WriteAllLines(summaryPath, summary, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("сводка прогона: {0}", summaryPath);
            if (refPath != null)
            {
                Compare(refPath);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " НЕ СОШЛОСЬ");
            foreach (Form form in new Form[] { docHost, panelHost, doc, panel, mainForm })
            {
                form.Dispose();
            }
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // одно плечо
        // ==================================================================

        static void Arm(string name, string culture, DocEnergySpectrum doc, EnergySpectrumView view, Action state,
                        string expectRefusalKey, bool expectNumber, bool expectLcZero, bool refusalIsOld = false)
        {
            Console.WriteLine();
            Console.WriteLine("--- {0} ---", name);
            state();
            view.PrepareViewData();
            view.RefreshSelectionOverlay();

            string tag = "g10-" + name + "-" + culture;
            Bitmap frame = Frame(view);
            frame.Save(Path.Combine(shotDir, tag + ".png"), ImageFormat.Png);
            string sha = Sha256(frame);
            Rectangle panelBox = DarkGrayBox(frame);

            object an = Field(typeof(EnergySpectrumView), "selectionAnalytics").GetValue(view);
            if (an == null)
            {
                Console.WriteLine("  НЕТ  аналитика выделения не построена");
                bad++;
                frame.Dispose();
                return;
            }
            string refusal = (string)Get(an, "ActivityRefusal");
            string label = (string)Get(an, "ActivityLabel");
            double activity = (double)Get(an, "Activity");
            double lc = (double)Get(an, "Lc");
            double net = (double)Get(an, "NetCounts");
            Console.WriteLine("  нетто {0}, Lc {1}, A {2}, подпись «{3}», отказ «{4}»",
                              F(net, 1), F(lc, 2), F(activity, 2), label ?? "", refusal ?? "");
            // Строк отказа — той же меркой GDI+, которой вид переносит текст в
            // ширину панели (r2 = ширина панели − 12); не копия правила, а та же
            // функция измерения. Одна строка — 0 переносов.
            int lines = refusal == null ? 0 : Lines(frame, view.Font, refusal, panelBox.Width - 12);
            Console.WriteLine("  панель: {0}×{1} в ({2},{3}); кадр sha256 {4}…; строк отказа по мерке GDI+: {5}",
                              panelBox.Width, panelBox.Height, panelBox.X, panelBox.Y, sha.Substring(0, 16), lines);
            summary.Add(string.Join("\t", name, culture, panelBox.Height.ToString(CultureInfo.InvariantCulture), sha, refusal ?? "",
                                    lines.ToString(CultureInfo.InvariantCulture), (lc > 0.0 ? "1" : "0")));

            if (expectLcZero)
            {
                Same("Lc = 0 (фон в выделении пуст)", true, lc == 0.0);
                // ⛔ Число в памяти посчитано и ПОСЛЕ правки `A259` тоже: решение
                //    Amber 06.09.2026 — «отказ словами», а не показ числа; цена
                //    (человек не увидит посчитанного) названа и принята. Поэтому
                //    ниже, на общем пути плеча с отказом, «числа нет» не судится.
                Same("число в памяти посчитано (A > 0)", true, activity > 0.0);
                if (!lcZeroFixed)
                {
                    // Плечо «до»: на панели нет НИЧЕГО про активность — ни числа,
                    // ни отказа. Молчание того же рода, что два закрытых в `A193`.
                    Same("плечо «до»: отказа нет (молчание `A259` числом)", true, refusal == null);
                    Console.WriteLine("  ⚠ число посчитано, а на панели ни числа, ни отказа — это и есть `A259`");
                    frame.Dispose();
                    return;
                }
                // Дальше — общий путь плеча с отказом: текст против ресурса,
                // положительный контроль «как рисовал прежний код», высота.
            }
            else if (expectNumber)
            {
                Same("число посчитано (A > 0)", true, activity > 0.0);
                Same("Lc > 0", true, lc > 0.0);
                Same("отказа нет", true, refusal == null);
                Same("подпись поставлена", true, !string.IsNullOrEmpty(label));
                frame.Dispose();
                return;
            }

            // Плечи с отказом.
            bool expectVoice = fixedBuild || refusalIsOld || expectLcZero;
            if (!expectVoice)
            {
                Same("плечо «до»: подписи нет", true, label == null);
                Same("плечо «до»: отказа нет (молчание, дефект A193 числом)", true, refusal == null);
                frame.Dispose();
                return;
            }

            string expected = AppResources.GetString(expectRefusalKey, CultureInfo.GetCultureInfo(culture));
            if (expected == null)
            {
                Console.WriteLine("  НЕТ  в сборке нет строки ресурса «{0}» — ждать нечего", expectRefusalKey);
                bad++;
                frame.Dispose();
                return;
            }
            Same("отказ словами = ресурс «" + expectRefusalKey + "» [" + culture + "]", expected, refusal);
            Same("подпись поставлена (отказ занимает место числа)", true, !string.IsNullOrEmpty(label));
            if (!expectLcZero)
            {
                Same("числа нет", 0.0, activity);
            }
            if (culture == "ru")
            {
                Same("русский текст отличается от английского (сателлит ru рядом)", true,
                     refusal != AppResources.GetString(expectRefusalKey, CultureInfo.GetCultureInfo("en")));
            }

            // ⛔ Положительный контроль: так рисовал ПРЕЖНИЙ код — без подписи и
            //    без отказа. Аналитика не пересчитывается, только два поля
            //    обнуляются; отрисовка читает лишь их.
            Set(an, "ActivityRefusal", null);
            Set(an, "ActivityLabel", null);
            using (Bitmap silent = Frame(view))
            {
                Rectangle silentBox = DarkGrayBox(silent);
                // Область панели — заливка с полем 4 px: вокруг заливки вид рисует
                // рамку в 3 px, и она растёт вместе с панелью.
                Rectangle area = Rectangle.Union(panelBox, silentBox);
                area.Inflate(Margin, Margin);
                int changed = ChangedIn(frame, silent, area);
                int outside = ChangedIn(frame, silent, new Rectangle(0, 0, frame.Width, frame.Height)) - changed;
                Console.WriteLine("  контроль «как рисовал прежний код»: панель {0} px против {1} px; изменилось точек в области панели {2}, вне её {3}",
                                  silentBox.Height, panelBox.Height, changed, outside);
                Same("отказ виден: точек изменилось в области панели > 0", true, changed > 0);
                // Отступ 6 px перед блоком ПШПВ: при Lc = 0 с подписью прежний код
                // его не резервировал, и последняя строка свисала с заливки —
                // «вне панели» менялись точки. С правкой G10 (`A195`-родня) вне
                // панели 0, а панель выше на 16 + 32 + 6 при Lc = 0 и на 48 при Lc > 0.
                // Ожидаемый рост панели: подпись 16 + строка «Activity Bq: no K» 16 +
                // 16 на каждую строку отказа + 6 px отступа при Lc = 0. Прежний код
                // резервировал 48 всегда: вторая строка отказа ложилась поверх
                // черты и «Peak Counts», а при Lc = 0 последняя строка свисала с
                // заливки (у прежнего кода «вне панели» поэтому печатается, но не судится).
                int grow = fixedBuild ? 32 + 16 * lines + (lc > 0.0 ? 0 : 6) : 48;
                if (fixedBuild)
                {
                    Same("вне панели (с полем 4 px) кадр тот же", 0, outside);
                }
                Same("панель с отказом выше на " + grow.ToString(CultureInfo.InvariantCulture)
                     + (fixedBuild ? " px (16 подписи + 16 строки K + 16 × строк отказа" + (lc > 0.0 ? "" : " + 6 отступа") + ")"
                                   : " px (прежний код: 48 всегда)"),
                     grow, panelBox.Height - silentBox.Height);
                silent.Save(Path.Combine(shotDir, tag + "-silent.png"), ImageFormat.Png);
            }
            frame.Dispose();
        }

        // ==================================================================
        // сличение с прогоном другой сборки
        // ==================================================================

        static void Compare(string refPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== сличение с другой сборкой: {0} ===", refPath);
            if (!File.Exists(refPath))
            {
                Console.WriteLine("  НЕТ  файла сводки нет");
                bad++;
                return;
            }
            var other = new Dictionary<string, string[]>();
            bool refG10 = false, refA259 = false;
            foreach (string line in File.ReadAllLines(refPath))
            {
                string[] c = line.Split('\t');
                if (c.Length > 0 && c[0] == "#")
                {
                    foreach (string mark in c)
                    {
                        if (mark == "g10=1") refG10 = true;
                        if (mark == "a259=1") refA259 = true;
                    }
                    continue;
                }
                if (c.Length >= 4) other[c[0] + "/" + c[1]] = c;
            }
            // ⛔ ЧЕМ РАЗЛИЧАЮТСЯ СБОРКИ — тем и судим. Если различие в `A193`/`A195`
            //    (полоса G10), правила старые. Если в `A259` (полоса F66), сдвинуться
            //    обязано ТОЛЬКО плечо `lczero`, а остальные четыре — побайтно те же:
            //    это и есть положительный контроль правки.
            bool g10Diff = fixedBuild != refG10;
            bool a259Diff = lcZeroFixed != refA259;
            Console.WriteLine("  сборки различаются: A193/A195 — {0}; A259 — {1}",
                              g10Diff ? "ДА" : "нет", a259Diff ? "ДА" : "нет");
            if (!g10Diff && !a259Diff)
            {
                Console.WriteLine("  правило: сборки в одном состоянии — ждём ВСЁ побайтно то же");
            }
            foreach (string line in summary)
            {
                string[] c = line.Split('\t');
                if (c.Length > 0 && c[0] == "#") continue;
                string key = c[0] + "/" + c[1];
                string[] o;
                if (!other.TryGetValue(key, out o))
                {
                    Console.WriteLine("  НЕТ  в сводке другой сборки нет плеча {0}", key);
                    bad++;
                    continue;
                }
                int mine = int.Parse(c[2], CultureInfo.InvariantCulture);
                int theirs = int.Parse(o[2], CultureInfo.InvariantCulture);
                // Кто «старый», кто «новый» — по этой сборке: сличение симметрично.
                int newH = fixedBuild ? mine : theirs;
                int oldH = fixedBuild ? theirs : mine;
                int lines = c.Length > 5 ? int.Parse(c[5], CultureInfo.InvariantCulture) : 0;
                bool lcPositive = c.Length > 6 && c[6] == "1";

                // ⛔ Сборки различаются ТОЛЬКО правкой `A259` (либо не различаются
                //    вовсе) — тогда сдвинуться имеет право одно плечо `lczero`, а
                //    остальные четыре обязаны совпасть. Это положительный контроль:
                //    правка, тронувшая чужое плечо, здесь и ловится.
                if (!g10Diff)
                {
                    string refPng = Path.Combine(Path.GetDirectoryName(refPath), "g10-" + c[0] + "-" + c[1] + ".png");
                    string myPng = Path.Combine(shotDir, "g10-" + c[0] + "-" + c[1] + ".png");
                    if (c[0] == "lczero" && a259Diff)
                    {
                        int withH = lcZeroFixed ? mine : theirs;
                        int withoutH = lcZeroFixed ? theirs : mine;
                        int withLines = lcZeroFixed
                            ? lines
                            : (o.Length > 5 ? int.Parse(o[5], CultureInfo.InvariantCulture) : 0);
                        Same(key + ": панель с отказом выше молчащей на 38 + 16 × строк"
                             + " (16 подписи + 6 отступа при Lc = 0 + 16 строки K + 16 × строк) (A259)",
                             38 + 16 * withLines, withH - withoutH);
                        Same(key + ": кадры различаются", true, o[3] != c[3]);
                    }
                    else
                    {
                        Same(key + ": высота панели та же", theirs, mine);
                        PanelCrop(key, refPng, myPng, o[3] == c[3]);
                        Same(key + ": кадр целиком побайтно тот же", true, o[3] == c[3]);
                    }
                    continue;
                }

                switch (c[0])
                {
                    case "honest":
                        // Кадр ЦЕЛИКОМ у двух сборок может расходиться вне панели по
                        // чужим причинам (поиск пиков правят соседние полосы) — это
                        // печатается с местом расхождения; судится ОБЛАСТЬ ПАНЕЛИ.
                        Same(key + ": высота панели та же", theirs, mine);
                        PanelCrop(key, Path.Combine(Path.GetDirectoryName(refPath), "g10-" + c[0] + "-" + c[1] + ".png"),
                                  Path.Combine(shotDir, "g10-" + c[0] + "-" + c[1] + ".png"), o[3] == c[3]);
                        break;
                    case "nobg":
                        // Отказ тот же (~~`A192`~~); панель выше на 6 px отступа при
                        // Lc = 0 и на 16 за каждую строку переноса сверх первой.
                        Same(key + ": отказ словами тот же", o.Length > 4 ? o[4] : "", c.Length > 4 ? c[4] : "");
                        Same(key + ": новая панель выше старой на 6 (отступ при Lc = 0) + 16 × (строк − 1)",
                             6 + 16 * (lines - 1), newH - oldH);
                        break;
                    case "lczero":
                        Same(key + ": старая панель выше новой ровно на 54 px (A195)", 54, oldH - newH);
                        break;
                    default:
                        // nocurve (Lc > 0), netneg (Lc = 0): прежний код молчал (48 не
                        // резервировал вовсе), новый — 32 + 16 × строк (+ 6 при Lc = 0).
                        Same(key + ": новая панель выше старой на 32 + 16 × строк" + (lcPositive ? "" : " + 6") + " (A193)",
                             32 + 16 * lines + (lcPositive ? 0 : 6), newH - oldH);
                        Same(key + ": кадры различаются", true, o[3] != c[3]);
                        break;
                }
            }
        }

        // ==================================================================
        // помощники
        // ==================================================================

        const int Margin = 4;

        /// <summary>Строк, на которые GDI+ переносит текст в ширину — та же мерка, что у вида.</summary>
        static int Lines(Bitmap b, Font font, string text, int width)
        {
            using (Graphics g = Graphics.FromImage(b))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                float lineHeight = font.GetHeight(g);
                SizeF size = g.MeasureString(text, font, width, format);
                return Math.Max(1, (int)Math.Round(size.Height / lineHeight));
            }
        }

        /// <summary>Область панели (заливка с полем) у двух кадров побайтно; остальное — справкой с местом.</summary>
        static void PanelCrop(string key, string refPng, string myPng, bool wholeSame)
        {
            if (!File.Exists(refPng))
            {
                Console.WriteLine("  НЕТ  нет кадра другой сборки {0}", refPng);
                bad++;
                return;
            }
            using (Bitmap a = new Bitmap(refPng))
            using (Bitmap b = new Bitmap(myPng))
            {
                Rectangle area = Rectangle.Union(DarkGrayBox(a), DarkGrayBox(b));
                area.Inflate(Margin, Margin);
                int inside = ChangedIn(a, b, area);
                int total = ChangedIn(a, b, new Rectangle(0, 0, a.Width, a.Height));
                Console.WriteLine("  {0}: кадр целиком {1}; вне панели точек различается {2}{3}",
                                  key, wholeSame ? "побайтно тот же" : "РАЗЛИЧАЕТСЯ", total - inside,
                                  total - inside > 0 ? " в " + BoxOf(a, b, area) : "");
                Same(key + ": область панели (с полем 4 px) побайтно та же", 0, inside);
            }
        }

        static string BoxOf(Bitmap a, Bitmap b, Rectangle skip)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    if (skip.Contains(x, y)) continue;
                    if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
                }
            }
            return maxX < 0 ? "-" : string.Format(CultureInfo.InvariantCulture, "x {0}…{1}, y {2}…{3}", minX, maxX, minY, maxY);
        }

        static Peak PickPeak(IList<Peak> peaks)
        {
            double threshold = Convert.ToDouble(typeof(EnergySpectrumView)
                .GetField("MinimumActivityYieldPercent", BindingFlags.Public | BindingFlags.Static)
                .GetRawConstantValue(), CultureInfo.InvariantCulture);
            Peak best = null;
            foreach (Peak p in peaks)
            {
                if (p.Nuclide == null || !(p.Nuclide.Intencity >= threshold) || NuclideDefinition.IsElementXrayName(p.Nuclide.Name)) continue;
                if (!(p.Energy > CurveMin && p.Energy < CurveMax)) continue;
                if (best == null || p.SNR > best.SNR) best = p;
            }
            return best;
        }

        /// <summary>Фон из самого спектра: те же каналы и калибровка, отсчёты × factor.</summary>
        static EnergySpectrum Scaled(EnergySpectrum fg, double factor, double time)
        {
            var s = new EnergySpectrum();
            s.NumberOfChannels = fg.NumberOfChannels;
            s.Spectrum = new int[fg.NumberOfChannels];
            long total = 0;
            for (int i = 0; i < fg.NumberOfChannels; i++)
            {
                s.Spectrum[i] = (int)Math.Round(fg.Spectrum[i] * factor);
                total += s.Spectrum[i];
            }
            s.EnergyCalibration = fg.EnergyCalibration;
            s.MeasurementTime = time;
            s.TotalPulseCount = total;
            s.ValidPulseCount = total;
            return s;
        }

        /// <summary>Кадр настоящим `OnPaint` вида (`DrawToBitmap` → WM_PRINT).</summary>
        static Bitmap Frame(EnergySpectrumView view)
        {
            var image = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(image, new Rectangle(0, 0, view.Width, view.Height));
            return image;
        }

        /// <summary>Ограничивающий прямоугольник точек цвета `Brushes.DarkGray` — заливка панели выделения.</summary>
        static Rectangle DarkGrayBox(Bitmap b)
        {
            Color g = Color.DarkGray;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            Rectangle r = new Rectangle(0, 0, b.Width, b.Height);
            BitmapData d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = b.Width * 4;
                byte[] line = new byte[n];
                for (int y = 0; y < b.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, line, 0, n);
                    for (int x = 0; x < b.Width; x++)
                    {
                        int k = x * 4;
                        if (line[k] == g.B && line[k + 1] == g.G && line[k + 2] == g.R)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally
            {
                b.UnlockBits(d);
            }
            return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        static int ChangedIn(Bitmap a, Bitmap b, Rectangle area)
        {
            area.Intersect(new Rectangle(0, 0, a.Width, a.Height));
            if (area.IsEmpty) return 0;
            int changed = 0;
            Rectangle r = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = a.Width * 4;
                byte[] la = new byte[n], lb = new byte[n];
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(da.Scan0 + y * da.Stride, la, 0, n);
                    System.Runtime.InteropServices.Marshal.Copy(db.Scan0 + y * db.Stride, lb, 0, n);
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        int k = x * 4;
                        if (la[k] != lb[k] || la[k + 1] != lb[k + 1] || la[k + 2] != lb[k + 2] || la[k + 3] != lb[k + 3]) changed++;
                    }
                }
            }
            finally
            {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
            return changed;
        }

        static string Sha256(Bitmap b)
        {
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                using (var sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        /// <summary>
        /// Ресурсы ПРИЛОЖЕНИЯ по имени — класс `Properties.Resources` у сборки
        /// внутренний (образец `BqActivityProbe`).
        /// </summary>
        static readonly System.Resources.ResourceManager AppResources =
            new System.Resources.ResourceManager("BecquerelMonitor.Properties.Resources", typeof(EnergySpectrumView).Assembly);

        /// <summary>Докрутить фоновый поиск пиков панели насосом сообщений (образец G9).</summary>
        static void Pump(DCPeakDetectionView panel)
        {
            FieldInfo busy = Field(typeof(DCPeakDetectionView), "isProcessing");
            FieldInfo pending = Field(typeof(DCPeakDetectionView), "refreshPending");
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
                if (DateTime.UtcNow > deadline) throw new TimeoutException("поиск пиков не завершился за 60 с");
            }
            while ((bool)busy.GetValue(panel) || (bool)pending.GetValue(panel));
            Application.DoEvents();
        }

        static DocEnergySpectrum OpenDocument(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("нет файла спектра", path);
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }

        static Form Host(Form content, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            content.TopLevel = false;
            content.Dock = DockStyle.Fill;
            host.Controls.Add(content);
            content.Show();
            host.Show();
            return host;
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Any);
                if (field != null) return field;
            }
            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static object Get(object an, string prop)
        {
            PropertyInfo p = an.GetType().GetProperty(prop, Any);
            if (p == null) throw new InvalidOperationException("нет свойства SelectionAnalytics." + prop);
            return p.GetValue(an, null);
        }

        static void Set(object an, string prop, object value)
        {
            PropertyInfo p = an.GetType().GetProperty(prop, Any);
            if (p == null) throw new InvalidOperationException("нет свойства SelectionAnalytics." + prop);
            p.SetValue(an, value, null);
        }

        static string F(double v, int digits)
        {
            return v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-72} {2}{3}", ok ? "ok  " : "НЕТ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }
    }
}
