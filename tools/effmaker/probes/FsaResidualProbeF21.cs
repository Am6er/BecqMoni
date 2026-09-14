using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace FsaResidualProbeF21
{
    /// <summary>
    /// ПРИЁМКА `A248` (галочка показа/скрытия ленты невязки на графике FSA) —
    /// полоса F21, 05.09.2026.
    ///
    ///     fsaresidualprobef21 --spectrum=&lt;файл&gt; [--out=&lt;каталог снимков&gt;]
    ///                         [--against=&lt;каталог опорных кадров&gt;]
    ///                         [--numbers=&lt;csv&gt;] [--width=] [--height=] [--panel=]
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ. Ленты рисует НАСТОЯЩИЙ метод вида
    /// (`EnergySpectrumView.ShowFsaOverlay`, зовётся отражением), строки
    /// заполняет НАСТОЯЩЕЕ окно отчёта (`FSAReportView` в форме-носителе за
    /// краем экрана).
    ///
    /// ⚠ ОДИН ИСХОДНИК СОБИРАЕТСЯ И ПРОТИВ СТАРОЙ СБОРКИ: всё, чего до `A248`
    /// не было (`EnergySpectrumView.FsaShowResidual`,
    /// `FSAReportView.ShowResidualBand`, сам контрол), зовётся ОТРАЖЕНИЕМ. На
    /// старой сборке проба снимает опорные кадры и числа и выходит — их потом
    /// читает новая по ключу `--against=`.
    ///
    /// Разделы:
    ///
    ///   1. ПРОВОДКА: галочка настоящего окна доходит до настоящего графика
    ///      настоящего документа; положение держится при смене спектра; закрытие
    ///      окна ленту возвращает.
    ///   2. ПИКСЕЛИ. Четыре кадра одного вида на каждом поле:
    ///        A — галочка включена;
    ///        B — галочка выключена;
    ///        E+ — СБОРКА кадра из тех же приватных методов вида, ВКЛЮЧАЯ
    ///             `DrawFsaResidual`; обязана совпасть с A побитово — это
    ///             приёмка самой сборки, без неё E− ничего не стоит;
    ///        E− — та же сборка, но `DrawFsaResidual` НЕ зовётся вовсе; это и
    ///             есть «кадр, нарисованный вовсе без невязки».
    ///      Приёмка: B ≡ E− (ноль изменившихся), B ≠ A (положительный контроль,
    ///      доля названа), A ≡ опорный кадр старой сборки побитово.
    ///   3. ЧИСЛА: строки таблицы отчёта при обеих позициях галочки —
    ///      посимвольно; строка невязки и χ²/ndf на месте при обеих.
    ///   4. ПОДПИСЬ: галочка шестая в блоке «Дополнительные компоненты модели»,
    ///      подпись и подсказка не ключи ресурса, помещаются в панель; обе
    ///      культуры, снимки окна.
    ///
    /// ⛔ У каждого раздела ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — вход, на котором проверка
    /// обязана отказать.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;
        static string outDir = ".";

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Имя приватного поля вида, которым `A248` гасит ленту.</summary>
        const string ViewFlag = "FsaShowResidual";

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика,
            // отражение её не видит, и снятая позже она уже могла быть уведена.
            FsaTuningReport.Snapshot();

            string spectrumPath = null, numbersPath = null, againstDir = null;
            int width = 1200, height = 620, panel = 460;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--against=", StringComparison.Ordinal)) againstDir = a.Substring(10);
                else if (a.StartsWith("--numbers=", StringComparison.Ordinal)) numbersPath = a.Substring(10);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--panel=", StringComparison.Ordinal)) panel = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`):
            // иначе безоконный прогон встаёт на модальном окне.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            ResultData rd = Load(spectrumPath, nuclides);
            if (rd == null)
            {
                return 2;
            }

            FsaResult result = Analyze(rd, nuclides);
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось");
                return 1;
            }

            List<FsaStackLayer> layers = result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers);
            Console.WriteLine("SETUP\tχ²/ndf {0}, слоёв {1}",
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture), layers.Count);

            bool fresh = typeof(EnergySpectrumView).GetProperty(ViewFlag, Any) != null;
            Console.WriteLine("SETUP\tсборка {0} (свойство {1} {2})",
                              fresh ? "СВЕЖАЯ" : "СТАРАЯ", ViewFlag, fresh ? "есть" : "отсутствует");

            Directory.CreateDirectory(outDir);

            if (numbersPath != null)
            {
                return Numbers(numbersPath, rd, result, layers, panel, fresh);
            }

            if (!fresh)
            {
                // Старая сборка: снимаем ОПОРНЫЕ кадры «как сейчас» и уходим.
                // Гасить там нечего, и приёмку ей проходить не за что.
                ReferenceFrames(rd, result, width, height);
                Console.WriteLine();
                Console.WriteLine("ОПОРНЫЕ КАДРЫ СНЯТЫ (сборка старая)");
                return 0;
            }

            WiringSection(spectrumPath, nuclides, result);
            PixelSection(rd, result, width, height, againstDir);
            NumberSection(rd, result, panel);
            CaptionSection(rd, result, panel);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // Опорные кадры старой сборки
        // ==================================================================

        static void ReferenceFrames(ResultData rd, FsaResult result, int width, int height)
        {
            foreach (bool dark in new[] { true, false })
            {
                Color ground = dark ? Color.Black : Color.White;
                using (var view = new EnergySpectrumView())
                {
                    Setup(view, rd, result, width, height);
                    using (Bitmap on = Frame(view, width, height, ground))
                    {
                        string path = Path.Combine(outDir, Name(dark, "on"));
                        on.Save(path, ImageFormat.Png);
                        Console.WriteLine("  снят {0}", path);
                    }
                }
            }
        }

        static string Name(bool dark, string what)
        {
            return "f21-stack-" + (dark ? "dark" : "light") + "-" + what + ".png";
        }

        // ==================================================================
        // 0. ЧИСЛА — выгрузка, годная и для старой сборки
        // ==================================================================

        /// <summary>
        /// Всё, что человек читает как ЧИСЛО, — в один csv, и берётся оно из
        /// ТАБЛИЦЫ настоящего окна, а не из модели: спор «поменялись ли числа»
        /// про то, что стоит на экране. На свежей сборке выгрузка делается
        /// ДВАЖДЫ — при включенной и при выключенной галочке, — и обе половины
        /// пишутся в один файл с пометкой позиции.
        /// </summary>
        static int Numbers(string path, ResultData rd, FsaResult result,
                           List<FsaStackLayer> layers, int panel, bool fresh)
        {
            var lines = new List<string>();
            lines.Add("position,kind,name,value");
            foreach (bool show in fresh ? new[] { true, false } : new[] { true })
            {
                foreach (string line in TableDump(rd, result, panel, show, fresh))
                {
                    lines.Add((show ? "on," : "off,") + line);
                }
            }

            foreach (FsaStackLayer layer in layers)
            {
                // `R` — побитовый круг double: доля слоя сравнивается без
                // округления, иначе сдвиг в третьем знаке прошёл бы молча.
                lines.Add(string.Format(CultureInfo.InvariantCulture, "model,share,{0},{1}",
                                        Csv(layer.Name), layer.SharePercent.ToString("R", CultureInfo.InvariantCulture)));
            }

            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
            Console.WriteLine("ЧИСЛА\t{0}: строк {1}", path, lines.Count - 1);
            return 0;
        }

        /// <summary>
        /// Строки таблицы настоящего окна при заданном положении галочки.
        /// Ключи устойчивы к перекладке строк: состав ищется по роду, невязка —
        /// по роду `Residual`, χ²/ndf — по первой строке рода `Quality` с
        /// непустым значением.
        /// </summary>
        static List<string> TableDump(ResultData rd, FsaResult result, int panel, bool show, bool fresh)
        {
            var dump = new List<string>();
            var session = new FsaAnalysisSession();
            Plant(session, result, "f21");
            using (var report = new FSAReportView(null))
            using (Form host = Host(report, panel, 900))
            {
                if (fresh)
                {
                    SetShowResidual(report, show);
                }

                report.SetProbeSource(session, rd);
                Application.DoEvents();
                if (fresh)
                {
                    SetShowResidual(report, show);
                    Application.DoEvents();
                }

                string residual = null, chi2 = null;
                foreach (Row row in report.ReportTable.TableModel.Rows)
                {
                    var model = row.Tag as FsaReportRow;
                    if (model == null)
                    {
                        continue;
                    }

                    if (model.Kind == FsaReportRowKind.Layer || model.Kind == FsaReportRowKind.SumPeaks
                        || model.Kind == FsaReportRowKind.Undetected
                        || model.Kind == FsaReportRowKind.UndetectedFolded)
                    {
                        dump.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                                               model.Kind, Csv(row.Cells[1].Text), Csv(row.Cells[2].Text)));
                    }
                    else if (model.Kind == FsaReportRowKind.Residual && residual == null)
                    {
                        residual = row.Cells[1].Text + " = " + row.Cells[2].Text;
                    }
                    else if (model.Kind == FsaReportRowKind.Quality && chi2 == null
                             && !string.IsNullOrEmpty(row.Cells[2].Text))
                    {
                        chi2 = row.Cells[1].Text + " = " + row.Cells[2].Text;
                    }
                }

                dump.Add("service,residual," + Csv(residual ?? ""));
                dump.Add("service,chi2," + Csv(chi2 ?? ""));
                host.Hide();
            }

            return dump;
        }

        static string Csv(string s)
        {
            return (s ?? string.Empty).Replace(',', ';');
        }

        // ==================================================================
        // 1. ПРОВОДКА: галочка -> график настоящего документа
        // ==================================================================

        static void WiringSection(string path, NuclideDefinitionManager nuclides, FsaResult result)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. галочка доходит до графика документа (A248) ===");
            DocEnergySpectrum doc = OpenDocument(path, nuclides);
            if (doc == null)
            {
                bad++;
                return;
            }

            try
            {
                Plant(doc.FsaSession, result, "f21");
                using (var report = new FSAReportView(null))
                using (Form host = Host(report, 460, 900))
                {
                    report.SetDocument(doc);
                    Application.DoEvents();

                    Same("исходно лента показывается", true, ViewShows(doc.EnergySpectrumView));
                    Same("исходно галочка стоит", true, BoxChecked(report));

                    SetShowResidual(report, false);
                    Application.DoEvents();
                    Same("снятая галочка гасит ленту у графика", false, ViewShows(doc.EnergySpectrumView));
                    Same("галочка на экране снялась", false, BoxChecked(report));

                    // Отпечаток разбора от показа не зависит: результат сеанса
                    // ТОТ ЖЕ объект, пересчёта не было.
                    Same("результат сеанса не подменён", true,
                         ReferenceEquals(result, doc.FsaSession.Result));

                    // Смена спектра положение галочки не отменяет.
                    report.ActiveResultDataChanged();
                    Application.DoEvents();
                    Same("после перечитывания документа лента всё ещё погашена", false,
                         ViewShows(doc.EnergySpectrumView));

                    SetShowResidual(report, true);
                    Application.DoEvents();
                    Same("возвращённая галочка возвращает ленту", true, ViewShows(doc.EnergySpectrumView));

                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ проводки: пока галочка стоит,
                    // ни одно ДРУГОЕ действие ленту не гасит. Переключаем
                    // группировку — признак показа обязан устоять.
                    report.RequestedGrouping = FsaGrouping.Parents;
                    Application.DoEvents();
                    Denies("группировка ленту не гасит", !ViewShows(doc.EnergySpectrumView));

                    SetShowResidual(report, false);
                    report.SetDocument(null);
                    Application.DoEvents();
                    Same("после ухода документа его график не тронут (лента погашена)", false,
                         ViewShows(doc.EnergySpectrumView));

                    report.SetDocument(doc);
                    Application.DoEvents();
                    Same("возврат к документу переносит на него положение галочки", false,
                         ViewShows(doc.EnergySpectrumView));

                    host.Hide();
                    report.Dispose();
                    Application.DoEvents();
                    Same("закрытие окна возвращает ленту (иначе вернуть её нечем)", true,
                         ViewShows(doc.EnergySpectrumView));
                }
            }
            finally
            {
                doc.Dispose();
            }
        }

        // ==================================================================
        // 2. ПИКСЕЛИ: погашено — значит ни одного пикселя
        // ==================================================================

        static void PixelSection(ResultData rd, FsaResult result, int width, int height, string againstDir)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. лента невязки: пиксели, обе темы (A248) ===");
            foreach (bool dark in new[] { true, false })
            {
                string theme = dark ? "тёмная" : "светлая";
                Color ground = dark ? Color.Black : Color.White;
                using (var view = new EnergySpectrumView())
                {
                    Setup(view, rd, result, width, height);

                    SetViewShows(view, true);
                    using (Bitmap on = Frame(view, width, height, ground))
                    {
                        SetViewShows(view, false);
                        using (Bitmap off = Frame(view, width, height, ground))
                        {
                            // Сборка кадра из тех же приватных методов вида.
                            // ⚠ Порядок важен: `fsaPresentation` и кадровые
                            // массивы заполняет первый же `ShowFsaOverlay`.
                            SetViewShows(view, true);
                            using (Bitmap withRes = Replica(view, width, height, ground, true))
                            using (Bitmap noRes = Replica(view, width, height, ground, false))
                            {
                                on.Save(Path.Combine(outDir, Name(dark, "on")), ImageFormat.Png);
                                off.Save(Path.Combine(outDir, Name(dark, "off")), ImageFormat.Png);
                                noRes.Save(Path.Combine(outDir, Name(dark, "noresidual")), ImageFormat.Png);

                                long total = (long)width * height;
                                long replica = Changed(on, withRes);
                                long zero = Changed(off, noRes);
                                long control = Changed(on, off);
                                long bandPixels = Changed(withRes, noRes);

                                Console.WriteLine("  {0}: пикселей всего {1}; лента невязки занимает {2} ({3} %)",
                                                  theme, total, bandPixels, Pct(bandPixels, total));

                                // ⛔ Приёмка САМОЙ СБОРКИ: без неё «кадр без
                                // невязки» ничего не значил бы.
                                Same(theme + ": сборка кадра верна (E+ ≡ кадр «включено»)", 0L, replica);
                                Same(theme + ": у ленты невязки есть своя область", true, bandPixels > 500);

                                // (1) выключено — отличий от кадра БЕЗ невязки нет вовсе.
                                Console.WriteLine("  {0}: «выключено» против кадра вовсе без невязки — {1} px",
                                                  theme, zero);
                                Same(theme + ": «выключено» ≡ кадр вовсе без невязки", 0L, zero);

                                // (4) положительный контроль.
                                Console.WriteLine("  {0}: контроль «выключено против включено» — {1} px ({2} %)",
                                                  theme, control, Pct(control, total));
                                Denies(theme + ": на паре «вкл/выкл» проверка не проходит", control == 0);
                                Same(theme + ": погашено ровно то, что рисует лента", bandPixels, control);

                                if (againstDir != null)
                                {
                                    string reference = Path.Combine(againstDir, Name(dark, "on"));
                                    if (!File.Exists(reference))
                                    {
                                        Console.WriteLine("  {0}: опорного кадра нет: {1}", theme, reference);
                                        bad++;
                                    }
                                    else
                                    {
                                        using (var old = new Bitmap(reference))
                                        {
                                            long drift = Changed(on, old);
                                            Console.WriteLine("  {0}: против опорного кадра старой сборки — {1} px",
                                                              theme, drift);
                                            Same(theme + ": «включено» ≡ старая сборка побитово", 0L, drift);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// СБОРКА КАДРА из тех же приватных методов вида, в том же порядке, что
        /// и <c>ShowFsaOverlay</c>, с одним отличием: невязку можно НЕ звать.
        ///
        /// ⛔ Своей геометрии проба не считает — ленты кладёт `DrawFsaBand`
        /// вида, кривые `DrawFsaCurve` вида, невязку `DrawFsaResidual` вида.
        /// Верность самой сборки не предполагается, а МЕРЯЕТСЯ: кадр со
        /// звонком в невязку обязан совпасть с настоящим кадром побитово.
        /// </summary>
        static Bitmap Replica(EnergySpectrumView view, int width, int height, Color ground, bool withResidual)
        {
            var image = new Bitmap(width, height);
            object presentation = Field(typeof(EnergySpectrumView), "fsaPresentation").GetValue(view);
            var layers = (List<FsaStackLayer>)presentation.GetType().GetProperty("Layers").GetValue(presentation, null);
            var cumulative = (double[][])Field(typeof(EnergySpectrumView), "fsaCumulative").GetValue(view);
            var sumPeak = (double[][])Field(typeof(EnergySpectrumView), "fsaSumPeakLevel").GetValue(view);
            var zero = (double[])Field(typeof(EnergySpectrumView), "fsaZeroLevel").GetValue(view);
            var net = (double[])Field(typeof(EnergySpectrumView), "fsaNetSpectrum").GetValue(view);
            MethodInfo band = typeof(EnergySpectrumView).GetMethod("DrawFsaBand", Any);
            MethodInfo curve = typeof(EnergySpectrumView).GetMethod("DrawFsaCurve", Any);
            MethodInfo residual = typeof(EnergySpectrumView).GetMethod("DrawFsaResidual", Any);
            Color spectrumColor = GlobalConfigManager.GetInstance().GlobalConfig.ColorConfig
                                      .ActiveSpectrumColor.Color;
            double[] top = cumulative[layers.Count - 1];

            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(ground);
                SmoothingMode savedSmoothing = g.SmoothingMode;
                PixelOffsetMode savedPixelOffset = g.PixelOffsetMode;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Default;
                try
                {
                    for (int k = 0; k < layers.Count; k++)
                    {
                        double[] lower = k > 0 ? cumulative[k - 1] : zero;
                        Color color = ((FsaPresentation)presentation).ColorOf(layers[k].Name);
                        Color painted = Color.FromArgb(230, color);
                        using (Brush brush = new SolidBrush(painted))
                        {
                            band.Invoke(view, new object[] { g, brush, lower, cumulative[k] });
                        }

                        double[] sumLevel = sumPeak != null ? sumPeak[k] : null;
                        if (sumLevel != null)
                        {
                            using (Brush hatch = new HatchBrush(HatchStyle.DarkUpwardDiagonal,
                                                                FsaPalette.SumPeakHatchColor(color), painted))
                            {
                                band.Invoke(view, new object[] { g, hatch, lower, sumLevel });
                            }
                        }
                    }
                }
                finally
                {
                    g.SmoothingMode = savedSmoothing;
                    g.PixelOffsetMode = savedPixelOffset;
                }

                if (withResidual)
                {
                    residual.Invoke(view, new object[] { g, top, net, spectrumColor });
                }

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                try
                {
                    using (Pen modelPen = new Pen(Color.FromArgb(200, Color.White)))
                    {
                        curve.Invoke(view, new object[] { g, modelPen, top, false });
                    }

                    using (Pen spectrumPen = new Pen(spectrumColor))
                    {
                        curve.Invoke(view, new object[] { g, spectrumPen, net, true });
                    }
                }
                finally
                {
                    g.SmoothingMode = savedSmoothing;
                    g.PixelOffsetMode = savedPixelOffset;
                }
            }

            return image;
        }

        static long Changed(Bitmap a, Bitmap b)
        {
            long n = 0;
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) n++;
                }
            }

            return n;
        }

        static string Pct(long part, long whole)
        {
            return whole == 0 ? "-" : (100.0 * part / whole).ToString("F3", CultureInfo.InvariantCulture);
        }

        /// <summary>Кадр стека настоящим методом вида.</summary>
        static Bitmap Frame(EnergySpectrumView view, int width, int height, Color ground)
        {
            var image = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(ground);
                MethodInfo show = typeof(EnergySpectrumView).GetMethod("ShowFsaOverlay", Any);
                if (show == null)
                {
                    throw new InvalidOperationException("нет EnergySpectrumView.ShowFsaOverlay");
                }

                show.Invoke(view, new object[] { g });
            }

            return image;
        }

        // ==================================================================
        // 3. ЧИСЛА: обе позиции галочки дают одно и то же
        // ==================================================================

        static void NumberSection(ResultData rd, FsaResult result, int panel)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. числа при обеих позициях галочки (A248) ===");
            List<string> on = TableDump(rd, result, panel, true, true);
            List<string> off = TableDump(rd, result, panel, false, true);

            Same("строк в таблице поровну", on.Count, off.Count);
            int differ = 0;
            for (int i = 0; i < Math.Min(on.Count, off.Count); i++)
            {
                if (!string.Equals(on[i], off[i], StringComparison.Ordinal))
                {
                    differ++;
                    Console.WriteLine("  РАСХОЖДЕНИЕ {0}: «{1}» против «{2}»", i, on[i], off[i]);
                }
            }

            Console.WriteLine("  сверено строк {0}, расхождений {1}", on.Count, differ);
            Same("все строки посимвольно те же", 0, differ);

            string residualOn = Line(on, "service,residual,");
            string residualOff = Line(off, "service,residual,");
            string chi2On = Line(on, "service,chi2,");
            string chi2Off = Line(off, "service,chi2,");
            Console.WriteLine("  невязка: вкл «{0}», выкл «{1}»", residualOn, residualOff);
            Console.WriteLine("  χ²/ndf:  вкл «{0}», выкл «{1}»", chi2On, chi2Off);

            Same("строка невязки в таблице есть при включенной", true, residualOn.Length > 0);
            Same("строка невязки в таблице есть при ВЫКЛЮЧЕННОЙ", true, residualOff.Length > 0);
            Same("χ²/ndf в таблице есть при включенной", true, chi2On.Length > 0);
            Same("χ²/ndf в таблице есть при ВЫКЛЮЧЕННОЙ", true, chi2Off.Length > 0);

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ мерки: та же посимвольная сверка на
            // заведомо разных списках обязана ОТКАЗАТЬ.
            var spoiled = new List<string>(on);
            spoiled[spoiled.Count - 1] = spoiled[spoiled.Count - 1] + " F21";
            int spoiledDiffer = 0;
            for (int i = 0; i < spoiled.Count; i++)
            {
                if (!string.Equals(on[i], spoiled[i], StringComparison.Ordinal)) spoiledDiffer++;
            }

            Denies("на подпорченном списке сверка не проходит", spoiledDiffer == 0);
        }

        static string Line(List<string> dump, string prefix)
        {
            foreach (string s in dump)
            {
                if (s.StartsWith(prefix, StringComparison.Ordinal)) return s.Substring(prefix.Length);
            }

            return string.Empty;
        }

        // ==================================================================
        // 4. ПОДПИСЬ ГАЛОЧКИ: место, текст, подсказка, обе культуры
        // ==================================================================

        static void CaptionSection(ResultData rd, FsaResult result, int panel)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. шестая галочка блока: место и подпись, обе культуры (A248) ===");
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                Language(lang);
                var session = new FsaAnalysisSession();
                Plant(session, result, "f21");
                using (var report = new FSAReportView(null))
                using (Form host = Host(report, panel, 900))
                {
                    report.SetProbeSource(session, rd);
                    Application.DoEvents();

                    var box = (CheckBox)Field(typeof(FSAReportView), "residualBandCheckBox").GetValue(report);
                    var flow = (Control)Field(typeof(FSAReportView), "extrasFlow").GetValue(report);
                    var group = (Control)Field(typeof(FSAReportView), "extrasGroupBox").GetValue(report);

                    int boxes = 0;
                    foreach (Control c in flow.Controls)
                    {
                        if (c is CheckBox) boxes++;
                    }

                    Console.WriteLine("  {0}: блок «{1}», галочек {2}, наша {3}-я, подпись «{4}»",
                                      lang, group.Text, boxes, flow.Controls.IndexOf(box) + 1, box.Text);

                    Same(lang + ": галочка лежит в блоке «Дополнительные компоненты модели»", flow, box.Parent);
                    Same(lang + ": галочек в блоке ровно шесть", 6, boxes);
                    Same(lang + ": наша — шестая", 5, flow.Controls.IndexOf(box));
                    Same(lang + ": по умолчанию стоит (лента показывается)", true, box.Checked);
                    Same(lang + ": подпись непустая", true, !string.IsNullOrEmpty(box.Text));
                    Same(lang + ": подпись не имя контрола", false,
                         box.Text.StartsWith("residualBandCheckBox", StringComparison.Ordinal));

                    string tip = report.ToolTipOf(box);
                    Console.WriteLine("  {0}: подсказка «{1}»", lang, tip);
                    Same(lang + ": подсказка непустая", true, !string.IsNullOrEmpty(tip));
                    Same(lang + ": подсказка не имя ключа", false,
                         tip.StartsWith("FSAReport_", StringComparison.Ordinal));

                    // ⚠ Подпись СОСЕДЕЙ трогать было нечем — сверяем, что они
                    // на месте и не поехали: блок должен остаться прежним.
                    var pileUp = (CheckBox)Field(typeof(FSAReportView), "pileUpCheckBox").GetValue(report);
                    Same(lang + ": пятая галочка блока прежняя", 4, flow.Controls.IndexOf(pileUp));

                    using (Graphics g = report.CreateGraphics())
                    {
                        double own = g.MeasureString(box.Text, box.Font).Width + 20.0;
                        Console.WriteLine("  {0}: подпись {1} px против панели {2} px",
                                          lang, own.ToString("F0", CultureInfo.InvariantCulture), panel);
                        Same(lang + ": подпись помещается в панель", true, own <= panel);

                        double tipWidth = g.MeasureString(tip, report.Font).Width;
                        Console.WriteLine("  {0}: подсказка {1} px",
                                          lang, tipWidth.ToString("F0", CultureInfo.InvariantCulture));

                        // Положительный контроль той же мерки.
                        Denies(lang + ": заведомо длинная подпись мерку не проходит",
                               g.MeasureString(new string('W', 120), report.Font).Width + 20.0 <= panel);
                    }

                    using (var shot = new Bitmap(panel, host.ClientSize.Height))
                    {
                        report.DrawToBitmap(shot, new Rectangle(0, 0, panel, host.ClientSize.Height));
                        shot.Save(Path.Combine(outDir, "f21-report-" + lang.Substring(0, 2) + "-on.png"),
                                  ImageFormat.Png);
                    }

                    SetShowResidual(report, false);
                    Application.DoEvents();
                    Same(lang + ": снятие галочки строк таблицы не трогает", true,
                         report.ReportTable.TableModel.Rows.Count > 0);
                    using (var shot = new Bitmap(panel, host.ClientSize.Height))
                    {
                        report.DrawToBitmap(shot, new Rectangle(0, 0, panel, host.ClientSize.Height));
                        shot.Save(Path.Combine(outDir, "f21-report-" + lang.Substring(0, 2) + "-off.png"),
                                  ImageFormat.Png);
                    }

                    host.Hide();
                }
            }

            Language("en-US");
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static void Language(string name)
        {
            var culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            BecquerelMonitor.Properties.Resources.Culture = culture;
        }

        /// <summary>Признак показа ленты у ВИДА — отражением: старой сборке его не знать.</summary>
        static bool ViewShows(EnergySpectrumView view)
        {
            PropertyInfo property = typeof(EnergySpectrumView).GetProperty(ViewFlag, Any);
            if (property == null)
            {
                throw new InvalidOperationException("нет EnergySpectrumView." + ViewFlag + " — сборка старая");
            }

            return (bool)property.GetValue(view, null);
        }

        static void SetViewShows(EnergySpectrumView view, bool value)
        {
            typeof(EnergySpectrumView).GetProperty(ViewFlag, Any).SetValue(view, value, null);
        }

        /// <summary>Положение галочки ОКНА — отражением, по той же причине.</summary>
        static void SetShowResidual(FSAReportView report, bool value)
        {
            PropertyInfo property = typeof(FSAReportView).GetProperty("ShowResidualBand", Any);
            if (property == null)
            {
                throw new InvalidOperationException("нет FSAReportView.ShowResidualBand — сборка старая");
            }

            property.SetValue(report, value, null);
        }

        static bool BoxChecked(FSAReportView report)
        {
            var box = (CheckBox)Field(typeof(FSAReportView), "residualBandCheckBox").GetValue(report);
            return box.Checked;
        }

        /// <summary>
        /// Поля вьюпорта — те же, что ставит `FsaStackShot`: вид собирается без
        /// формы, поэтому шкалу и размеры надо выставить руками.
        /// </summary>
        static void Setup(EnergySpectrumView view, ResultData rd, FsaResult result, int width, int height)
        {
            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            double fromKev = 0.0;
            double toKev = calibration.ChannelToEnergy(spectrum.NumberOfChannels - 1);
            double ceiling = 0.0;
            for (int i = 0; i < result.Model.Length; i++)
            {
                if (result.Model[i] > ceiling) ceiling = result.Model[i];
            }

            const int left = 1;
            const double pownum = 4.0;
            Set(view, "energySpectrum", spectrum);
            Set(view, "activeResultData", rd);
            if (rd.BackgroundEnergySpectrum != null)
            {
                Set(view, "backgroundEnergySpectrum", rd.BackgroundEnergySpectrum);
                Set(view, "backgroundEnergyCalibration", rd.BackgroundEnergySpectrum.EnergyCalibration);
                Set(view, "backgroundNumberOfChannels", rd.BackgroundEnergySpectrum.NumberOfChannels);
            }

            Set(view, "energyCalibration", calibration);
            Set(view, "numberOfChannels", spectrum.NumberOfChannels);
            Set(view, "backgroundMode", BackgroundMode.ShowFSA);
            Set(view, "horizontalUnit", HorizontalUnit.Energy);
            Set(view, "verticalUnit", VerticalUnit.Counts);
            Set(view, "verticalScaleType", VerticalScaleType.PowerScale);
            Set(view, "pownum", pownum);
            Set(view, "totalMinValuePow", 0.0);
            Set(view, "valueRangePow", Math.Pow(ceiling, 1.0 / pownum));
            Set(view, "totalMinValueLog", 0.0);
            Set(view, "valueRangeLog", Math.Log10(ceiling));
            Set(view, "height", height);
            Set(view, "width", width - left);
            Set(view, "left", left);
            Set(view, "scrollX", 0);
            Set(view, "scrollY", 0);
            Set(view, "scrollBaseY", 0.0);
            Set(view, "verticalScale", 1.0);
            Set(view, "horizontalScale", 1.0);
            Set(view, "totalMinValue", 0.0);
            Set(view, "valueRange", ceiling);
            Set(view, "energyViewOffset", fromKev);
            Set(view, "pixelPerEnergy", (width - left) / (toKev - fromKev));
            Set(view, "dirty", false);

            object session = typeof(EnergySpectrumView).GetProperty("FsaSession", Any).GetValue(view, null);
            Field(session.GetType(), "result").SetValue(session, result);
        }

        static FsaResult Analyze(ResultData rd, NuclideDefinitionManager nuclides)
        {
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\tпиков {0}, образов {1}", peaks.Count, library.Count);
            if (library.Count == 0)
            {
                return null;
            }

            var analyzer = new FsaAnalyzer();
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            // ⚠ БЕЗ МАТРИЦЫ ОТКЛИКА, если геометрии у спектра нет. Для мерки
            // ЭКРАНА это законно — числа отсюда в журнал разбора не идут, — но
            // сказать об этом обязано.
            Console.WriteLine("SETUP\t⚠ числа годны только для мерки экрана, в журнал разбора не идут");

            // (`T243`) ЧЕМ СЧИТАЛИ — ДО СЧЁТА И ВСЛУХ: часть настроек взята у
            // КОНФИГУРАЦИИ ПРИБОРА (полоса поиска пиков, окно совпадения), и
            // по выводу этого не было видно ни строки.
            FsaTuningReport.Print(analyzer);
            return analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                    library, FsaEfficiency.FromConfig(rd.Efficiency));
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }

            ResultDataFile file = ReadFile(path);
            ResultData rd = Prepare(file);
            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));
            Fwhm(rd);
            return rd;
        }

        static ResultDataFile ReadFile(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (ResultDataFile)serializer.Deserialize(stream);
            }
        }

        static ResultData Prepare(ResultDataFile file)
        {
            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            return rd;
        }

        static void Fwhm(ResultData rd)
        {
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }
        }

        static DocEnergySpectrum OpenDocument(string path, NuclideDefinitionManager nuclides)
        {
            ResultDataFile file = ReadFile(path);
            ResultData rd = Prepare(file);
            ProbeDeviceConfig.Attach(rd);
            Fwhm(rd);

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            if (rd.MeasurementController == null)
            {
                rd.MeasurementController = new MeasurementController(null, rd);
            }

            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }

        static void Plant(FsaAnalysisSession session, FsaResult result, string stamp)
        {
            Field(session.GetType(), "result").SetValue(session, result);
            Field(session.GetType(), "stamp").SetValue(session, stamp);
            Field(session.GetType(), "running").SetValue(session, false);
            Field(session.GetType(), "status").SetValue(session, null);
        }

        static Form Host(FSAReportView report, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            report.TopLevel = false;
            report.Dock = DockStyle.Fill;
            host.Controls.Add(report);
            report.Show();
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

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-72} {2}{3}", ok ? "ok  " : "НЕТ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }

        static void Denies(string what, bool passedButShouldNot)
        {
            Console.WriteLine("  {0} {1,-72} {2}", passedButShouldNot ? "НЕТ " : "ok  ", what,
                              passedButShouldNot ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (passedButShouldNot) bad++;
        }
    }
}
