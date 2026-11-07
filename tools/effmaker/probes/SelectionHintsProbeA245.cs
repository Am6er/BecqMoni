using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace SelectionHintsProbeA245
{
    /// <summary>
    /// ПРИЁМКА `AMBER245` (04.11.2026), пункты (8) и (9) — панель значений курсора.
    ///
    ///     selectionhintsprobea245
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ: `EnergySpectrumView` поднимается без
    /// конструктора (`FormatterServices.GetUninitializedObject`), поля ставятся
    /// отражением, и зовётся приватный `EnsureSelectionAnalytics` — та самая
    /// ветка, что считает панель. Своя копия ветки мерила бы копию.
    ///
    /// ЧТО МЕРЯЕТСЯ, п. (8) — жалоба «через подписанный пик Cs-137 при фоне и
    /// кривой панель не показала строку активности и не назвала причину,
    /// проверить условие „ровно один подписанный пик“»:
    ///
    ///   1. СЛУЧАЙ ВОСПРОИЗВЕДЁН: подпись Cs-137, фон, кривая. `FindActivityPeak`
    ///      находит РОВНО один подписанный пик, отказа нет, Lc &gt; 0, активность
    ///      посчитана и больше нуля. То есть условие «ровно один подписанный пик»
    ///      ПРОЙДЕНО, и молчание панели им не объясняется.
    ///   2. ПРИЧИНА НАЗВАНА СЛОВАМИ: `SelectionActivityHint` возвращает текст
    ///      `Resources.SelectionActivityHint` на обоих языках — «активность
    ///      считает полноспектральный разбор, смотрите окно „Результат
    ///      измерений“». Это следствие решения Amber 06.10.2026 (`AMBER208`,
    ///      П236): строка активности с панели выделения снята нарочно.
    ///   3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ МЕРКИ: те же поля, но (а) второй подписанный
    ///      пик в том же выделении и (б) пик без подписи — `FindActivityPeak`
    ///      обязан вернуть null, активность — ноль, подсказка — null. Проверка,
    ///      у которой нет входа с другим ответом, не меряет ничего.
    ///   4. Обратный контроль подсказки: подпись есть, числа нет (кривой нет) —
    ///      подсказка молчит, потому что причина там другая, а печатать отказы
    ///      на панели запрещено решением `AMBER2`.
    ///
    /// п. (9): подпись `ChartHeaderCountBGRatio` — «Проба / фон, %» по-русски и
    /// «Sample / background, %» по-английски (`AMBER297`: `8cfe1a80` поставил её
    /// перевёрнутой над формулой проба/фон; решение Amber 05.11.2026 — поправить
    /// подпись, формулу не менять), подсказка к ней непустая и объясняет число
    /// словами.
    ///
    /// п. (9а) ЗНАЧЕНИЕ НА ПАНЕЛИ, `AMBER297`: настоящий `ShowCursorValues` рисует
    /// обе панели (выделения и канала) в `Bitmap` при заданных числах проба
    /// 66234876, фон в тех же каналах 65588919 за вдесятеро большее время, то есть
    /// 6558891.9 в приведении ко времени пробы. Строка панели сверяется ПО ТОЧКАМ
    /// с той же строкой, нарисованной тем же шрифтом: подпись ресурса и значение
    /// проба/фон×100 = 1009.85 %. Порядок слов судится отдельно: числитель формулы
    /// (проба) обязан стоять в подписи первым.
    /// Положительный контроль в том же процессе: значение обратной дроби (9.90 %) и
    /// старая подпись в те же точки НЕ ложатся. Ключ `--shots=&lt;каталог&gt;` кладёт
    /// снимок панелей (на диск, не в git).
    /// ⛔ Обращённая формула в ПРИЛОЖЕНИИ и старая подпись в ресурсе роняют пробу
    /// кодом 1: снято отдельными сборками-мутантами (журнал
    /// `handover/handover-2026-11-05-fixui.md`).
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;
        static string shots;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args ?? new string[0])
            {
                if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Console.WriteLine("=== чем мерено ===");
            Console.WriteLine("сборка приложения: {0}", typeof(EnergySpectrumView).Assembly.Location);
            Console.WriteLine("подсказка панели выделения, ru: «{0}»", Text("ru", "SelectionActivityHint"));
            Console.WriteLine();

            Section8();
            Section9();
            RatioOnPanel("ru");
            RatioOnPanel("en");

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  (8) панель «Выбранная область»: подписанный Cs-137 + фон + кривая
        // ------------------------------------------------------------------

        static void Section8()
        {
            Console.WriteLine("=== (8) «Выбранная область»: подписанный Cs-137, фон и кривая ===");
            Scene scene = Scene.At(661.657);
            scene.SetLabel("Cs-137", 661.657, 85.1);
            Scene.Answer answer = scene.Run();

            Console.WriteLine("  подпись: {0}, отказ: {1}, Lc: {2}, A: {3} Бк",
                              answer.Label ?? "нет", answer.Refusal ?? "нет",
                              N(answer.Lc), N(answer.Activity));
            Check("случай воспроизведён: пик подписан Cs-137", "Cs-137", answer.Label);
            Check("случай: отказа подписи нет", null, answer.Refusal);
            if (!(answer.Lc > 0.0)) { Console.WriteLine("  ⛔ Lc не посчитан — случай не тот"); bad++; }
            if (!(answer.Activity > 0.0)) { Console.WriteLine("  ⛔ активность не посчитана — случай не тот"); bad++; }

            // 1. Условие «ровно один подписанный пик» — прямо, а не по косвенным признакам.
            Peak found = scene.FindPeak();
            Console.WriteLine("  FindActivityPeak: {0}",
                              found == null ? "null" : found.Nuclide.Name + " " + F(found.Energy, "F2") + " кэВ");
            Check("условие «ровно один подписанный пик» пройдено", true, found != null);

            // 2. Причина названа словами, и на обоих языках.
            string ru = Hint(scene);
            string en = Text("en", "SelectionActivityHint");
            Console.WriteLine("  подсказка ru: «{0}»", Text("ru", "SelectionActivityHint"));
            Console.WriteLine("  подсказка en: «{0}»", en);
            Check("подсказка при посчитанном числе не пуста (ru)", true, !string.IsNullOrEmpty(ru));
            Check("подсказка при посчитанном числе не пуста (en)", true, !string.IsNullOrEmpty(en));
            Check("подсказка по-русски — не английский текст", false, ru == en);

            // 3. Положительный контроль мерки: другой вход — другой ответ.
            Scene two = Scene.At(661.657);
            two.SetLabel("Cs-137", 661.657, 85.1);
            two.AddSecondPeak();
            Scene.Answer twoAnswer = two.Run();
            Console.WriteLine("  контроль «два подписанных пика»: пик {0}, A {1} Бк, подсказка {2}",
                              two.FindPeak() == null ? "null" : "найден",
                              N(twoAnswer.Activity), Hint(two) == null ? "null" : "есть");
            Check("контроль: два пика — FindActivityPeak молчит", true, two.FindPeak() == null);
            Check("контроль: два пика — активности нет", true, !(twoAnswer.Activity > 0.0));
            Check("контроль: два пика — подсказки нет", null, Hint(two));

            Scene bare = Scene.At(661.657);
            Scene.Answer bareAnswer = bare.Run();
            Console.WriteLine("  контроль «без подписи»: пик {0}, A {1} Бк, подсказка {2}",
                              bare.FindPeak() == null ? "null" : "найден",
                              N(bareAnswer.Activity), Hint(bare) == null ? "null" : "есть");
            Check("контроль: без подписи — FindActivityPeak молчит", true, bare.FindPeak() == null);
            Check("контроль: без подписи — подсказки нет", null, Hint(bare));

            // 4. Обратный контроль: подпись есть, числа нет (кривой нет) — подсказка молчит.
            Scene noCurve = Scene.At(661.657);
            noCurve.SetLabel("Cs-137", 661.657, 85.1);
            noCurve.DropCurve();
            Scene.Answer noCurveAnswer = noCurve.Run();
            Console.WriteLine("  контроль «кривой нет»: отказ «{0}», A {1} Бк, подсказка {2}",
                              noCurveAnswer.Refusal ?? "нет", N(noCurveAnswer.Activity),
                              Hint(noCurve) == null ? "null" : "есть");
            Check("контроль: кривой нет — отказ словами есть", true,
                  !string.IsNullOrEmpty(noCurveAnswer.Refusal));
            Check("контроль: кривой нет — подсказки о снятой строке нет", null, Hint(noCurve));
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        //  (9) подпись «Проба / фон, %» и слова к ней
        // ------------------------------------------------------------------

        static void Section9()
        {
            Console.WriteLine("=== (9) «Проба / фон, %»: подпись и подсказка ===");
            string ru = Text("ru", "ChartHeaderCountBGRatio");
            string en = Text("en", "ChartHeaderCountBGRatio");
            string ruHint = Text("ru", "ChartHeaderCountBGRatioHint");
            string enHint = Text("en", "ChartHeaderCountBGRatioHint");
            Console.WriteLine("  подпись ru: «{0}»", ru);
            Console.WriteLine("  подпись en: «{0}»", en);
            Console.WriteLine("  подсказка ru: «{0}»", ruHint);
            Console.WriteLine("  подсказка en: «{0}»", enHint);
            Check("подпись ru — «Проба / фон, %»", "Проба / фон, %", ru);
            Check("подпись en — «Sample / background, %»", "Sample / background, %", en);
            // ⛔ (`AMBER297`) Порядок слов — отдельной проверкой: литерал выше однажды уже
            //    закрепил ошибку. Значение панели — проба/фон, поэтому числитель стоит первым.
            Check("порядок слов ru: «проба» раньше «фон»", true, WordsInOrder(ru, "проба", "фон"));
            Check("порядок слов en: «sample» раньше «background»", true, WordsInOrder(en, "sample", "background"));
            Check("контроль мерки порядка: старая подпись ru отвергается", false, WordsInOrder("Фон / проба, %", "проба", "фон"));
            Check("контроль мерки порядка: старая подпись en отвергается", false, WordsInOrder("Background / sample, %", "sample", "background"));
            Check("подсказка ru не пуста", true, !string.IsNullOrEmpty(ruHint));
            Check("подсказка en не пуста", true, !string.IsNullOrEmpty(enHint));
            Check("подсказка ru — не английский текст", false, ruHint == enHint);
            Console.WriteLine();
        }

        static bool WordsInOrder(string text, string first, string second)
        {
            int a = text == null ? -1 : text.IndexOf(first, StringComparison.OrdinalIgnoreCase);
            int b = text == null ? -1 : text.IndexOf(second, StringComparison.OrdinalIgnoreCase);
            return a >= 0 && b >= 0 && a < b;
        }

        // ------------------------------------------------------------------
        //  (9а) значение на панели: настоящий ShowCursorValues в Bitmap
        // ------------------------------------------------------------------

        const int PanelChannel = 100;
        const int SampleCounts = 66234876;
        const int RawBackgroundCounts = 65588919;
        const double SampleTime = 1000.0, BackgroundTime = 10000.0;
        const int PanelY = 10, PanelWidth = 230;
        /// <summary>Верх строки процента от верха панели: панель канала 4+16+22+16+16.</summary>
        const int ChannelRatioRow = 74;
        /// <summary>Верх строки процента от верха панели: панель выделения 4+22+16+22+16+16.</summary>
        const int SelectionRatioRow = 96;

        static void RatioOnPanel(string culture)
        {
            CultureInfo ui = new CultureInfo(culture);
            CultureInfo keepUi = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentUICulture = ui;
            try
            {
                double adjusted = RawBackgroundCounts * SampleTime / BackgroundTime;
                double percent = SampleCounts / adjusted * 100.0;
                string expected = percent.ToString("f2", CultureInfo.InvariantCulture) + Text(culture, "PercentCharacter");
                string reversed = (adjusted / SampleCounts * 100.0).ToString("f2", CultureInfo.InvariantCulture) + Text(culture, "PercentCharacter");
                string label = Text(culture, "ChartHeaderCountBGRatio");
                string oldLabel = culture == "ru" ? "Фон / проба, %" : "Background / sample, %";
                Console.WriteLine("=== (9а) панель, культура {0}: проба {1}, фон {2}, ждём «{3}» при подписи «{4}» ===",
                                  culture, SampleCounts.ToString("f0", CultureInfo.InvariantCulture),
                                  adjusted.ToString("f1", CultureInfo.InvariantCulture), expected, label);
                Check("значение ровно 1009.85 %", "1009.85" + Text(culture, "PercentCharacter"), expected);

                using (EnergySpectrumView view = new EnergySpectrumView())
                using (Bitmap frame = new Bitmap(800, 300, PixelFormat.Format32bppArgb))
                {
                    int x = DrawPanels(view, frame);
                    if (shots != null)
                    {
                        Directory.CreateDirectory(shots);
                        string file = Path.Combine(shots, "a297-panels-" + culture + ".png");
                        frame.Save(file, ImageFormat.Png);
                        Console.WriteLine("  снимок: {0}", file);
                    }
                    int selectionX = x;
                    int channelX = x - PanelWidth - 10;
                    foreach (var panel in new[]
                    {
                        new { Name = "панель выделения", X = selectionX, Row = SelectionRatioRow },
                        new { Name = "панель канала", X = channelX, Row = ChannelRatioRow },
                    })
                    {
                        int good = RowDifference(view, frame, panel.X, panel.Row, label, expected);
                        Console.WriteLine("  {0}: отличий строки «{1}» / «{2}» точек {3}", panel.Name, label, expected, good);
                        Check(panel.Name + ": строка совпала по точкам", 0, good);
                        int wrongValue = RowDifference(view, frame, panel.X, panel.Row, label, reversed);
                        int wrongLabel = RowDifference(view, frame, panel.X, panel.Row, oldLabel, expected);
                        Console.WriteLine("  {0}: контроль: обратная дробь «{1}» отличий {2}; старая подпись «{3}» отличий {4}",
                                          panel.Name, reversed, wrongValue, oldLabel, wrongLabel);
                        Check(panel.Name + ": контроль — обратная дробь в те же точки не ложится", true, wrongValue > 0);
                        Check(panel.Name + ": контроль — старая подпись в те же точки не ложится", true, wrongLabel > 0);
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = keepUi;
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Настоящая отрисовка панелей: поля вида ставятся отражением, зовётся приватный
        /// `ShowCursorValues`, окно не создаётся. Возвращает левый край панели выделения.
        /// </summary>
        static int DrawPanels(EnergySpectrumView view, Bitmap frame)
        {
            int[] fgData = new int[1024];
            int[] bgData = new int[1024];
            fgData[PanelChannel] = SampleCounts;
            bgData[PanelChannel] = RawBackgroundCounts;
            var cal = new PolynomialEnergyCalibration();
            cal.PolynomialOrder = 1;
            cal.Coefficients = new double[] { 0.0, 1.0 };
            EnergySpectrum fg = new EnergySpectrum
            {
                NumberOfChannels = fgData.Length, Spectrum = fgData, EnergyCalibration = cal,
                MeasurementTime = SampleTime, TotalPulseCount = SampleCounts, ValidPulseCount = SampleCounts,
            };
            EnergySpectrum bg = new EnergySpectrum
            {
                NumberOfChannels = bgData.Length, Spectrum = bgData, EnergyCalibration = cal,
                MeasurementTime = BackgroundTime, TotalPulseCount = RawBackgroundCounts, ValidPulseCount = RawBackgroundCounts,
            };
            ResultData result = new ResultData { EnergySpectrum = fg, BackgroundEnergySpectrum = bg, Visible = true };
            Type t = typeof(EnergySpectrumView);
            Action<string, object> set = (name, value) =>
            {
                FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + name);
                f.SetValue(view, value);
            };
            view.Size = new Size(frame.Width, frame.Height);
            set("energySpectrum", fg);
            set("backgroundEnergySpectrum", bg);
            set("activeResultData", result);
            set("energyCalibration", cal);
            set("baseEnergyCalibration", cal);
            set("backgroundEnergyCalibration", cal);
            set("backgroundNumberOfChannels", bgData.Length);
            set("left", 0);
            set("width", frame.Width);
            set("height", frame.Height);
            set("cursorX", -1);                 // столбец под курсором не сжат: TryCursorSpan молчит
            set("validCursor", true);
            set("cursorChannel", PanelChannel);
            set("selectionStart", PanelChannel);
            set("selectionEnd", PanelChannel);
            set("selectionAnalyticsDirty", true);
            set("selectionAnalytics", null);
            set("nuclideCursorPeakDirty", false);
            set("nuclideCursorPeak", null);
            set("backgroundMode", BackgroundMode.Invisible);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.LightBlue);
                t.GetMethod("ShowCursorValues", BindingFlags.NonPublic | BindingFlags.Instance)
                 .Invoke(view, new object[] { g });
            }
            return frame.Width - PanelWidth - 10;
        }

        /// <summary>
        /// Сколько точек строки панели отличается от той же строки, нарисованной тем же
        /// шрифтом и выравниванием на белом поле. Высота строки — 16 px: шаг отрисовки.
        /// </summary>
        static int RowDifference(EnergySpectrumView view, Bitmap frame, int panelX, int row, string label, string value)
        {
            Rectangle text = new Rectangle(panelX + 5, PanelY + row, PanelWidth - 12, 32);
            Rectangle area = new Rectangle(text.X, text.Y, text.Width, 16);
            using (Bitmap ideal = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(ideal))
            using (StringFormat far = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.Clear(Color.White);
                g.DrawString(label, view.Font, Brushes.Black, text);
                g.DrawString(value, view.Font, Brushes.Black, text, far);
                int differ = 0;
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        if (ideal.GetPixel(x, y).ToArgb() != frame.GetPixel(x, y).ToArgb()) differ++;
                    }
                }
                return differ;
            }
        }

        // ------------------------------------------------------------------
        //  Мерка
        // ------------------------------------------------------------------

        /// <summary>Подсказка панели выделения — тот же приватный метод, что зовёт отрисовка.</summary>
        static string Hint(Scene scene)
        {
            MethodInfo hint = typeof(EnergySpectrumView).GetMethod(
                "SelectionActivityHint", BindingFlags.NonPublic | BindingFlags.Static);
            if (hint == null)
            {
                Console.WriteLine("  ⛔ в сборке нет EnergySpectrumView.SelectionActivityHint — мерить нечем");
                bad++;
                return null;
            }
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru");
            return (string)hint.Invoke(null, new object[] { scene.Analytics });
        }

        /// <summary>Текст ресурса в названной культуре — тем же менеджером, что у приложения.</summary>
        static string Text(string culture, string key)
        {
            string text = Resources.ResourceManager.GetString(key, new CultureInfo(culture));
            if (text == null)
            {
                Console.WriteLine("  ⛔ нет ресурса «{0}» в культуре «{1}»", key, culture);
                bad++;
            }
            return text;
        }

        static void Check(string what, object expected, object got)
        {
            if (Equals(expected, got))
            {
                Console.WriteLine("  ✓ {0}", what);
                return;
            }
            Console.WriteLine("  ⛔ {0}: ждали {1}, получили {2}", what,
                              expected ?? "null", got ?? "null");
            bad++;
        }

        static string N(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v)
                ? "-" : v.ToString("G6", CultureInfo.InvariantCulture);
        }

        static string F(double v, string format)
        {
            return double.IsNaN(v) || double.IsInfinity(v)
                ? "" : v.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Сцена `BqActivityProbe`: гауссиана Cs-137, фон вдвое дольше, кривая
        /// 50…2000 кэВ, выделение ±40 кэВ. Окно приложения не поднимается.
        /// </summary>
        class Scene
        {
            static readonly Type TView = typeof(EnergySpectrumView);
            static readonly Type TAn = TView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            static readonly MethodInfo MEnsure = TView.GetMethod("EnsureSelectionAnalytics",
                BindingFlags.NonPublic | BindingFlags.Instance);
            static readonly MethodInfo MFind = TView.GetMethod("FindActivityPeak",
                BindingFlags.NonPublic | BindingFlags.Instance);

            public const double CurveMin = 50.0, CurveMax = 2000.0;
            public double FgTime = 1000.0, BgTime = 2000.0;

            EnergySpectrum fg, bg;
            PolynomialEnergyCalibration cal;
            Peak peak;
            public ResultData Result;
            public EfficiencyConfigData Curve;
            public object View;
            public object Analytics;

            public class Answer
            {
                public double Activity, Lc;
                public string Label, Refusal;
            }

            public static Scene At(double energyKev)
            {
                var sc = new Scene();
                sc.cal = new PolynomialEnergyCalibration();
                sc.cal.PolynomialOrder = 1;
                sc.cal.Coefficients = new double[] { 0.0, 1.0 };

                int channels = 4096;
                int centre = (int)Math.Round(energyKev);
                double sigma = 12.0;
                int[] fgArr = new int[channels];
                int[] bgArr = new int[channels];
                for (int i = 0; i < channels; i++)
                {
                    double gauss = 40000.0 * Math.Exp(-0.5 * Math.Pow((i - centre) / sigma, 2.0));
                    fgArr[i] = 200 + (int)Math.Round(gauss);
                    bgArr[i] = 300;
                }

                sc.fg = MakeSpectrum(fgArr, sc.cal, sc.FgTime);
                sc.bg = MakeSpectrum(bgArr, sc.cal, sc.BgTime);

                sc.Curve = new EfficiencyConfigData("проба");
                sc.Curve.Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = CurveMin, Efficiency = 2.0e-2, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 662.0,    Efficiency = 1.0e-3, ErrorPercent = 5.0 },
                    new ROIEfficiencyData { Energy = CurveMax, Efficiency = 1.0e-4, ErrorPercent = 8.0 },
                };

                sc.peak = new Peak
                {
                    Energy = energyKev,
                    Channel = centre,
                    Count = 40000,
                    FWHM = 2.354820045 * sigma,
                    SNR = 100.0,
                };

                sc.Result = new ResultData
                {
                    EnergySpectrum = sc.fg,
                    BackgroundEnergySpectrum = sc.bg,
                    Visible = true,
                    Efficiency = sc.Curve,
                };
                sc.Result.SampleInfo.Weight = 1.0;
                sc.Result.SampleInfo.Volume = 1.0;
                sc.Result.DetectedPeaks.Add(sc.peak);
                return sc;
            }

            static EnergySpectrum MakeSpectrum(int[] data, EnergyCalibration cal, double time)
            {
                var s = new EnergySpectrum();
                s.NumberOfChannels = data.Length;
                s.Spectrum = data;
                s.EnergyCalibration = cal;
                s.MeasurementTime = time;
                long total = 0;
                for (int i = 0; i < data.Length; i++) total += data[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
                return s;
            }

            public void SetLabel(string name, double lineKev, double intensity)
            {
                this.peak.Nuclide = new NuclideDefinition
                {
                    Name = name,
                    Energy = lineKev,
                    Intencity = intensity,
                    Visible = true,
                    Sets = new HashSet<Guid>(),
                };
            }

            public void AddSecondPeak()
            {
                this.Result.DetectedPeaks.Add(new Peak
                {
                    Energy = this.peak.Energy + 10.0,
                    Channel = this.peak.Channel + 10,
                    Count = 1000,
                    FWHM = this.peak.FWHM,
                    Nuclide = new NuclideDefinition
                    {
                        Name = "K-40", Energy = this.peak.Energy + 10.0,
                        Intencity = 10.66, Visible = true, Sets = new HashSet<Guid>(),
                    },
                });
            }

            public void DropCurve()
            {
                this.Result.Efficiency = null;
            }

            public Answer Run()
            {
                this.View = FormatterServices.GetUninitializedObject(TView);
                Set("energySpectrum", this.fg);
                Set("backgroundEnergySpectrum", this.bg);
                Set("substractedEnergySpectrum", null);
                Set("normByEffEnergySpectrum", null);
                Set("energyCalibration", this.cal);
                Set("baseEnergyCalibration", this.cal);
                Set("backgroundEnergyCalibration", this.cal);
                Set("backgroundNumberOfChannels", this.bg == null ? 0 : this.bg.NumberOfChannels);
                Set("selectionStart", 621);
                Set("selectionEnd", 701);
                Set("peakMode", PeakMode.Visible);
                Set("backgroundMode", BackgroundMode.Invisible);
                Set("activeResultData", this.Result);
                Set("globalConfigManager", Config());
                Set("nuclideManager", NuclideDefinitionManager.GetInstance());
                Set("selectionAnalyticsDirty", true);
                Set("selectionAnalytics", null);
                Set("selectionFWHM", 0.0);

                MEnsure.Invoke(this.View, null);
                this.Analytics = TView.GetField("selectionAnalytics",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this.View);

                var a = new Answer();
                if (this.Analytics == null)
                {
                    return a;
                }
                a.Activity = (double)Get("Activity");
                a.Label = (string)Get("ActivityLabel");
                a.Refusal = (string)Get("ActivityRefusal");
                a.Lc = (double)Get("Lc");
                return a;
            }

            /// <summary>Тот же `FindActivityPeak`, что зовёт ветка активности.</summary>
            public Peak FindPeak()
            {
                if (this.View == null || this.Analytics == null)
                {
                    return null;
                }
                return (Peak)MFind.Invoke(this.View, new object[] { this.Analytics });
            }

            static GlobalConfigManager Config()
            {
                var m = new GlobalConfigManager();
                var c = new GlobalConfigInfo();
                if (c.ColorConfig != null &&
                    (c.ColorConfig.SpectrumColorList == null || c.ColorConfig.SpectrumColorList.Count == 0))
                {
                    c.ColorConfig.InitializeSpectrumColor();
                }
                m.GlobalConfig = c;
                return m;
            }

            void Set(string field, object value)
            {
                FieldInfo f = TView.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null)
                {
                    throw new InvalidOperationException("нет поля EnergySpectrumView." + field);
                }
                f.SetValue(this.View, value);
            }

            object Get(string property)
            {
                PropertyInfo p = TAn.GetProperty(property);
                if (p == null)
                {
                    throw new InvalidOperationException("нет свойства SelectionAnalytics." + property);
                }
                return p.GetValue(this.Analytics, null);
            }
        }
    }
}
