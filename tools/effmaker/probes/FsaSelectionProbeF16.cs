using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace FsaSelectionProbeF16
{
    /// <summary>
    /// ПРИЁМКА `A246` (выделение выбранного компонента на спектре приглушением
    /// остальных) и `A247` (блок «Качество разбора» в таблице отчёта) — полоса
    /// F16, 05.09.2026.
    ///
    ///     fsaselectionprobef16 --spectrum=&lt;файл&gt; [--layer=&lt;имя слоя&gt;]
    ///                          [--out=&lt;каталог снимков&gt;] [--width=] [--height=]
    ///                          [--numbers=&lt;csv&gt;]
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ. Ленты рисует НАСТОЯЩИЙ метод вида
    /// (`EnergySpectrumView.ShowFsaOverlay`, зовётся отражением), таблицу
    /// заполняет НАСТОЯЩЕЕ окно отчёта (`FSAReportView` в форме-носителе за
    /// краем экрана, снимок `DrawToBitmap`). Своей отрисовки у пробы нет
    /// нарочно: проба, рисующая по своим правилам, показала бы не приложение.
    ///
    /// Разделы:
    ///
    ///   0. ЧИСЛА (`--numbers=`) — выгрузка и выход. Режим написан так, чтобы
    ///      СОБИРАТЬСЯ И ПРОТИВ СТАРОЙ СБОРКИ (все новые члены зовутся
    ///      отражением): им снимается «до» и «после» правки, и сравниваются
    ///      посимвольно строки состава, доля каждого слоя (`R`, побитовый
    ///      круг), значение невязки и значение χ²/ndf.
    ///   1. ПРОВОДКА (`A246`): выбор строки в таблице настоящего окна доходит
    ///      до настоящего графика настоящего документа.
    ///   2. ПИКСЕЛИ (`A246`): три кадра одного вида — без выбора, с выбором и
    ///      маска слоёв (та же `DrawFsaBand` в опознавательных цветах). Считается
    ///      доля изменившихся пикселей, средняя разница яркости у приглушённых
    ///      и НЕИЗМЕННОСТЬ области выбранного. Оба поля: чёрное и белое.
    ///   3. ПОДПИСИ (`A247`): черта, заголовок, перечень пометок; ширина каждой
    ///      подписи против ширины колонки в обеих культурах; снимки окна.
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

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, layerName = null, numbersPath = null;
            int width = 1200, height = 620, panel = 460;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--layer=", StringComparison.Ordinal)) layerName = a.Substring(8);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
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
            foreach (FsaStackLayer layer in layers)
            {
                Console.WriteLine("ROW\t{0}\t{1}", layer.Name,
                                  layer.SharePercent.ToString("F3", CultureInfo.InvariantCulture));
            }

            if (layerName == null)
            {
                layerName = BiggestNamed(layers);
            }

            Console.WriteLine("SETUP\tвыделяем слой «{0}»", layerName);

            if (numbersPath != null)
            {
                return Numbers(numbersPath, rd, result, layers, panel);
            }

            Directory.CreateDirectory(outDir);
            WiringSection(spectrumPath, nuclides, result, layerName);
            PixelSection(rd, result, layerName, width, height);
            CaptionSection(rd, result, panel, layerName);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // 0. ЧИСЛА — выгрузка, годная и для старой сборки
        // ==================================================================

        /// <summary>
        /// Всё, что человек читает как ЧИСЛО, — в один csv, и берётся оно из
        /// ТАБЛИЦЫ настоящего окна, а не из модели: спор «поменялись ли числа»
        /// про то, что стоит на экране.
        ///
        /// Ключи устойчивы к перекладке строк (`A247` разносит служебные строки
        /// по нескольким): строки состава ищутся по роду, невязка — по роду
        /// `Residual`, χ²/ndf — по первой строке рода `Quality` с непустым
        /// значением. И до правки, и после эти три правила указывают на одно
        /// и то же число.
        /// </summary>
        static int Numbers(string path, ResultData rd, FsaResult result,
                           List<FsaStackLayer> layers, int panel)
        {
            var session = new FsaAnalysisSession();
            Plant(session, result, "f16");
            using (var report = new FSAReportView(null))
            using (Form host = Host(report, panel, 900))
            {
                report.SetProbeSource(session, rd);
                Application.DoEvents();

                var lines = new List<string>();
                lines.Add("kind,name,value");
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
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                                                model.Kind, Csv(row.Cells[1].Text), Csv(row.Cells[2].Text)));
                    }
                    else if (model.Kind == FsaReportRowKind.Residual && residual == null)
                    {
                        residual = row.Cells[2].Text;
                    }
                    else if (model.Kind == FsaReportRowKind.Quality && chi2 == null
                             && !string.IsNullOrEmpty(row.Cells[2].Text))
                    {
                        chi2 = row.Cells[2].Text;
                    }
                }

                lines.Add(string.Format(CultureInfo.InvariantCulture, "service,residual,{0}", Csv(residual ?? "")));
                lines.Add(string.Format(CultureInfo.InvariantCulture, "service,chi2,{0}", Csv(chi2 ?? "")));
                foreach (FsaStackLayer layer in layers)
                {
                    // `R` — побитовый круг double: доля слоя сравнивается без
                    // округления, иначе сдвиг в третьем знаке прошёл бы молча.
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "share,{0},{1}",
                                            Csv(layer.Name), layer.SharePercent.ToString("R", CultureInfo.InvariantCulture)));
                }

                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
                Console.WriteLine("ЧИСЛА\t{0}: строк {1}, невязка «{2}», χ²/ndf «{3}»",
                                  path, lines.Count - 1, residual, chi2);
                host.Hide();
            }

            return 0;
        }

        static string Csv(string s)
        {
            return (s ?? string.Empty).Replace(',', ';');
        }

        // ==================================================================
        // 1. ПРОВОДКА: таблица -> график настоящего документа
        // ==================================================================

        static void WiringSection(string path, NuclideDefinitionManager nuclides,
                                  FsaResult result, string layerName)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. выбор строки доходит до графика документа (A246) ===");
            DocEnergySpectrum doc = OpenDocument(path, nuclides);
            if (doc == null)
            {
                bad++;
                return;
            }

            try
            {
                Plant(doc.FsaSession, result, "f16");
                using (var report = new FSAReportView(null))
                using (Form host = Host(report, 460, 900))
                {
                    report.SetDocument(doc);
                    Application.DoEvents();

                    Same("исходно приглушения нет", null, Highlight(doc.EnergySpectrumView));
                    Same("исходно выбора нет", null, Selected(report));

                    Same("строка слоя нашлась и выбралась", true, Select(report, layerName));
                    Same("окно знает выбранный слой", layerName, Selected(report));
                    Same("график получил имя слоя", layerName, Highlight(doc.EnergySpectrumView));

                    // Пересчёт таблицы не смеет ронять выбор человека.
                    report.RefreshReport();
                    Same("после перестройки таблицы выбор на месте", layerName, Selected(report));
                    Same("после перестройки таблицы приглушение на месте", layerName,
                         Highlight(doc.EnergySpectrumView));

                    // Строка без ленты (заголовок блока качества, χ²/ndf,
                    // пометка) приглушать нечего — и не приглушает.
                    int service = ServiceRowIndex(report);
                    Same("служебная строка блока качества в таблице есть", true, service >= 0);
                    if (service >= 0)
                    {
                        report.ReportTable.TableModel.Selections.SelectCell(service, 1);
                        Application.DoEvents();
                        Same("выбор строки без ленты снимает приглушение", null, Highlight(doc.EnergySpectrumView));
                    }

                    Select(report, layerName);
                    Same("выбор вернулся", layerName, Highlight(doc.EnergySpectrumView));
                    Same("снятие выбора снимает приглушение", true, Select(report, null));
                    Same("после снятия выбора у графика null", null, Highlight(doc.EnergySpectrumView));

                    // Положительный контроль: имени, которого в таблице нет,
                    // окно НЕ принимает и графику ничего не шлёт.
                    Select(report, layerName);
                    bool taken = Select(report, "нет такого слоя F16");
                    Denies("несуществующий слой окно не принимает", taken);
                    Same("график при этом не тронут", layerName, Highlight(doc.EnergySpectrumView));

                    report.SetDocument(null);
                    Same("смена документа снимает приглушение у прежнего", null,
                         Highlight(doc.EnergySpectrumView));
                    host.Hide();
                }
            }
            finally
            {
                doc.Dispose();
            }
        }

        static int ServiceRowIndex(FSAReportView report)
        {
            TableModel model = report.ReportTable.TableModel;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                var tag = model.Rows[i].Tag as FsaReportRow;
                if (tag != null && tag.Kind == FsaReportRowKind.Quality && tag.Layer == null
                    && !string.IsNullOrEmpty(tag.Name))
                {
                    return i;
                }
            }

            return -1;
        }

        // ==================================================================
        // 2. ПИКСЕЛИ: приглушение видно и измеримо, выбранный не тронут
        // ==================================================================

        static void PixelSection(ResultData rd, FsaResult result, string layerName, int width, int height)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. приглушение остальных: пиксели, обе темы (A246) ===");
            foreach (bool dark in new[] { true, false })
            {
                string theme = dark ? "тёмная" : "светлая";
                Color ground = dark ? Color.Black : Color.White;
                using (var view = new EnergySpectrumView())
                {
                    Setup(view, rd, result, width, height);

                    using (Bitmap none = Frame(view, width, height, ground, null))
                    using (Bitmap picked = Frame(view, width, height, ground, layerName))
                    using (Bitmap stray = Frame(view, width, height, ground, "нет такого слоя F16"))
                    using (Bitmap mask = MaskFrame(view, width, height))
                    {
                        none.Save(Path.Combine(outDir, "f16-stack-" + (dark ? "dark" : "light") + "-none.png"), ImageFormat.Png);
                        picked.Save(Path.Combine(outDir, "f16-stack-" + (dark ? "dark" : "light") + "-picked.png"), ImageFormat.Png);
                        mask.Save(Path.Combine(outDir, "f16-stack-" + (dark ? "dark" : "light") + "-mask.png"), ImageFormat.Png);

                        int index = LayerIndex(view, layerName);
                        Same(theme + ": слой найден в снимке представления", true, index >= 0);

                        Diff all = Compare(none, picked, mask, -1);
                        Diff own = Compare(none, picked, mask, index);
                        Diff others = CompareOthers(none, picked, mask, index);
                        Diff nothing = Compare(none, stray, mask, -1);

                        Console.WriteLine("  {0}: пикселей всего {1}, лент {2}; выбранный слой {3} px, остальные {4} px",
                                          theme, all.Total, MaskedCount(mask), own.Region, others.Region);
                        Console.WriteLine("  {0}: изменилось всего {1} ({2} %); у выбранного {3}; у остальных {4} ({5} %), средняя |ΔY| {6}",
                                          theme, all.Changed,
                                          Pct(all.Changed, all.Total), own.Changed, others.Changed,
                                          Pct(others.Changed, others.Region),
                                          others.MeanDelta.ToString("F1", CultureInfo.InvariantCulture));

                        Same(theme + ": область выбранного слоя НЕ изменилась", 0L, own.Changed);
                        Same(theme + ": у выбранного слоя есть своя область", true, own.Region > 1000);
                        Same(theme + ": приглушены почти все чужие пиксели (>90 %)", true,
                             others.Region > 0 && others.Changed * 100L >= 90L * others.Region);
                        Same(theme + ": приглушение заметно глазом (средняя |ΔY| > 20)", true, others.MeanDelta > 20.0);

                        // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: «выбор есть, а приглушения
                        // нет» — имя, которого среди слоёв нет. Кадр обязан
                        // совпасть с кадром без выбора побитово, и та же
                        // проверка обязана на нём ОТКАЗАТЬ.
                        Console.WriteLine("  {0}: контроль «выбор без приглушения» — изменилось {1} пикселей",
                                          theme, nothing.Changed);
                        Denies(theme + ": на кадре без приглушения проверка не проходит",
                               nothing.Changed > 0);
                    }
                }
            }
        }

        struct Diff
        {
            public long Total;
            public long Region;
            public long Changed;
            public double MeanDelta;
        }

        /// <summary>
        /// Сравнить два кадра в области маски. <paramref name="layer"/> −1 —
        /// весь кадр; иначе только пиксели этого слоя.
        /// </summary>
        static Diff Compare(Bitmap a, Bitmap b, Bitmap mask, int layer)
        {
            var d = new Diff();
            double sum = 0.0;
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    d.Total++;
                    int id = mask.GetPixel(x, y).R;
                    if (layer >= 0 && id != layer + 1)
                    {
                        continue;
                    }

                    if (layer >= 0)
                    {
                        d.Region++;
                    }

                    Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                    if (ca.ToArgb() != cb.ToArgb())
                    {
                        d.Changed++;
                        sum += Math.Abs(Luma(ca) - Luma(cb));
                    }
                }
            }

            if (layer < 0)
            {
                d.Region = d.Total;
            }

            d.MeanDelta = d.Changed > 0 ? sum / d.Changed : 0.0;
            return d;
        }

        static Diff CompareOthers(Bitmap a, Bitmap b, Bitmap mask, int layer)
        {
            var d = new Diff();
            double sum = 0.0;
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    d.Total++;
                    int id = mask.GetPixel(x, y).R;
                    if (id == 0 || id == layer + 1)
                    {
                        continue;
                    }

                    d.Region++;
                    Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
                    if (ca.ToArgb() != cb.ToArgb())
                    {
                        d.Changed++;
                        sum += Math.Abs(Luma(ca) - Luma(cb));
                    }
                }
            }

            d.MeanDelta = d.Changed > 0 ? sum / d.Changed : 0.0;
            return d;
        }

        static long MaskedCount(Bitmap mask)
        {
            long n = 0;
            for (int y = 0; y < mask.Height; y++)
            {
                for (int x = 0; x < mask.Width; x++)
                {
                    if (mask.GetPixel(x, y).R != 0) n++;
                }
            }

            return n;
        }

        static double Luma(Color c)
        {
            return 0.30 * c.R + 0.59 * c.G + 0.11 * c.B;
        }

        static string Pct(long part, long whole)
        {
            return whole == 0 ? "-" : (100.0 * part / whole).ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>Кадр стека настоящим методом вида при заданном выделении.</summary>
        static Bitmap Frame(EnergySpectrumView view, int width, int height, Color ground, string highlight)
        {
            SetHighlight(view, highlight);
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

        /// <summary>
        /// МАСКА ЛЕНТ: та же <c>DrawFsaBand</c> вида, но каждым слоем в своём
        /// опознавательном цвете (R = номер слоя + 1). Своей геометрии проба не
        /// считает — иначе мерила бы не то, что нарисовано.
        /// </summary>
        static Bitmap MaskFrame(EnergySpectrumView view, int width, int height)
        {
            SetHighlight(view, null);
            var image = new Bitmap(width, height);
            object presentation = Field(typeof(EnergySpectrumView), "fsaPresentation").GetValue(view);
            var layers = (List<FsaStackLayer>)presentation.GetType().GetProperty("Layers").GetValue(presentation, null);
            var cumulative = (double[][])Field(typeof(EnergySpectrumView), "fsaCumulative").GetValue(view);
            var zero = (double[])Field(typeof(EnergySpectrumView), "fsaZeroLevel").GetValue(view);
            MethodInfo band = typeof(EnergySpectrumView).GetMethod("DrawFsaBand", Any);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                for (int k = 0; k < layers.Count; k++)
                {
                    double[] lower = k > 0 ? cumulative[k - 1] : zero;
                    using (Brush brush = new SolidBrush(Color.FromArgb(255, k + 1, 0, 0)))
                    {
                        band.Invoke(view, new object[] { g, brush, lower, cumulative[k] });
                    }
                }
            }

            return image;
        }

        static int LayerIndex(EnergySpectrumView view, string name)
        {
            object presentation = Field(typeof(EnergySpectrumView), "fsaPresentation").GetValue(view);
            if (presentation == null)
            {
                return -1;
            }

            var layers = (List<FsaStackLayer>)presentation.GetType().GetProperty("Layers").GetValue(presentation, null);
            for (int k = 0; k < layers.Count; k++)
            {
                if (string.Equals(layers[k].Name, name, StringComparison.Ordinal)) return k;
            }

            return -1;
        }

        // ==================================================================
        // 3. ПОДПИСИ БЛОКА КАЧЕСТВА
        // ==================================================================

        static void CaptionSection(ResultData rd, FsaResult result, int panel, string selectedLayerName)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. блок «Качество разбора»: черта, заголовок, подписи целиком (A247) ===");
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                Language(lang);
                var session = new FsaAnalysisSession();
                Plant(session, result, "f16");
                using (var report = new FSAReportView(null))
                using (Form host = Host(report, panel, 900))
                {
                    report.SetProbeSource(session, rd);
                    Application.DoEvents();

                    TableModel model = report.ReportTable.TableModel;
                    int column = report.ReportTable.ColumnModel.Columns[1].Width;
                    Console.WriteLine("  {0}: панель {1} px, колонка «Компонент» {2} px, строк {3}",
                                      lang, panel, column, model.Rows.Count);

                    int rule = -1, header = -1;
                    for (int i = 0; i < model.Rows.Count; i++)
                    {
                        if (model.Rows[i].Height <= 4 && model.Rows[i].Cells[1].BackColor.A == 255
                            && string.IsNullOrEmpty(model.Rows[i].Cells[1].Text))
                        {
                            rule = i;
                            if (i + 1 < model.Rows.Count) header = i + 1;
                            break;
                        }
                    }

                    Same(lang + ": черта над блоком есть", true, rule >= 0);
                    Same(lang + ": заголовок сразу под чертой", true, header >= 0);
                    if (header < 0)
                    {
                        continue;
                    }

                    string title = model.Rows[header].Cells[1].Text;
                    Console.WriteLine("  {0}: заголовок «{1}», шрифт {2}", lang, title,
                                      model.Rows[header].Cells[1].Font != null
                                          ? model.Rows[header].Cells[1].Font.Style.ToString() : "(шрифт формы)");
                    Same(lang + ": заголовок непустой", true, !string.IsNullOrEmpty(title));
                    Same(lang + ": заголовок не имя ключа", false, title.StartsWith("FSAReport_", StringComparison.Ordinal));
                    Same(lang + ": заголовок полужирный", true,
                         model.Rows[header].Cells[1].Font != null && model.Rows[header].Cells[1].Font.Bold);

                    // Все подписи блока — от черты и до конца таблицы.
                    int wide = 0, keys = 0, trimmed = 0, wrapped = 0;
                    double widest = 0.0;
                    string widestText = string.Empty;
                    using (Graphics g = report.CreateGraphics())
                    {
                        for (int i = rule; i < model.Rows.Count; i++)
                        {
                            string text = model.Rows[i].Cells[1].Text ?? string.Empty;
                            if (text.Length == 0) continue;
                            if (text.StartsWith("FSAReport_", StringComparison.Ordinal)) keys++;
                            if (text.EndsWith("…", StringComparison.Ordinal)) trimmed++;
                            if (model.Rows[i].Cells[1].WordWrap || i == header) wrapped++;
                            Font font = model.Rows[i].Cells[1].Font ?? report.Font;
                            double w = g.MeasureString(text, font).Width;
                            if (w > widest) { widest = w; widestText = text; }
                            if (w > column) wide++;
                            Console.WriteLine("  {0}: «{1}» = {2} px | {3}", lang, text,
                                              w.ToString("F0", CultureInfo.InvariantCulture),
                                              model.Rows[i].Cells[2].Text);
                        }
                    }

                    Console.WriteLine("  {0}: самая широкая подпись «{1}» {2} px против колонки {3} px",
                                      lang, widestText, widest.ToString("F0", CultureInfo.InvariantCulture), column);
                    Same(lang + ": ни одной подписи-ключа (resx прочитан)", 0, keys);
                    Same(lang + ": ни одной подписи с многоточием", 0, trimmed);
                    Same(lang + ": все подписи блока помещаются в колонку", 0, wide);

                    // Положительный контроль той же мерки: заведомо длинная
                    // подпись в ту же колонку НЕ помещается.
                    using (Graphics g = report.CreateGraphics())
                    {
                        string huge = new string('W', 120);
                        Denies(lang + ": заведомо длинная подпись мерку не проходит",
                               g.MeasureString(huge, report.Font).Width <= column);
                    }

                    using (var shot = new Bitmap(panel, host.ClientSize.Height))
                    {
                        report.DrawToBitmap(shot, new Rectangle(0, 0, panel, host.ClientSize.Height));
                        shot.Save(Path.Combine(outDir, "f16-report-" + lang.Substring(0, 2) + ".png"), ImageFormat.Png);
                    }

                    // Тот же снимок с ВЫБРАННОЙ строкой состава: на графике ей
                    // отвечает полноцветная лента, а в таблице — подсветка
                    // выбора; `A246` смотрится по двум снимкам сразу.
                    if (Select(report, selectedLayerName))
                    {
                        Application.DoEvents();
                        using (var shot = new Bitmap(panel, host.ClientSize.Height))
                        {
                            report.DrawToBitmap(shot, new Rectangle(0, 0, panel, host.ClientSize.Height));
                            shot.Save(Path.Combine(outDir, "f16-report-" + lang.Substring(0, 2) + "-selected.png"),
                                      ImageFormat.Png);
                        }

                        Same(lang + ": выбор в таблице держится", selectedLayerName, Selected(report));
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

        static void SetHighlight(EnergySpectrumView view, string value)
        {
            PropertyInfo property = typeof(EnergySpectrumView).GetProperty("FsaHighlight", Any);
            if (property == null)
            {
                throw new InvalidOperationException("нет EnergySpectrumView.FsaHighlight — сборка старая");
            }

            property.SetValue(view, value, null);
        }

        static string Highlight(EnergySpectrumView view)
        {
            PropertyInfo property = typeof(EnergySpectrumView).GetProperty("FsaHighlight", Any);
            return property == null ? null : (string)property.GetValue(view, null);
        }

        static bool Select(FSAReportView report, string layerName)
        {
            MethodInfo method = typeof(FSAReportView).GetMethod("SelectComponent", Any);
            if (method == null)
            {
                throw new InvalidOperationException("нет FSAReportView.SelectComponent — сборка старая");
            }

            bool taken = (bool)method.Invoke(report, new object[] { layerName });
            Application.DoEvents();
            return taken;
        }

        static string Selected(FSAReportView report)
        {
            PropertyInfo property = typeof(FSAReportView).GetProperty("SelectedComponent", Any);
            return property == null ? null : (string)property.GetValue(report, null);
        }

        static string BiggestNamed(List<FsaStackLayer> layers)
        {
            string best = null;
            double share = -1.0;
            foreach (FsaStackLayer layer in layers)
            {
                if (string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (layer.SharePercent > share)
                {
                    share = layer.SharePercent;
                    best = layer.Name;
                }
            }

            return best;
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

            // ⚠ БЕЗ МАТРИЦЫ ОТКЛИКА: у `ASN16_Charoite` геометрии нет вовсе
            // (часть корпуса `unknown`). Для мерки ЭКРАНА это законно — числа
            // отсюда в журнал разбора не идут, — но сказать об этом обязано.
            Console.WriteLine("SETUP\t⚠ разбор БЕЗ матрицы отклика: числа годны только для мерки экрана");
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

            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));
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

            return rd;
        }

        static DocEnergySpectrum OpenDocument(string path, NuclideDefinitionManager nuclides)
        {
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

            ProbeDeviceConfig.Attach(rd);
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
