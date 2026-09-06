using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace PeakHighlightProbeG9
{
    /// <summary>
    /// ПРИЁМКА `A255` (выбранный в таблице поиска пиков пик виден на спектре:
    /// метка сверху и полоса шириной в ПШПВ — решение Amber 06.09.2026) —
    /// полоса G9, 06.09.2026.
    ///
    ///     peakhighlightprobeg9 --spectrum=&lt;файл&gt; --other=&lt;файл&gt; [--out=&lt;каталог снимков&gt;]
    ///                          [--row=&lt;строка таблицы&gt;] [--width=] [--height=]
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ. `MainForm` заводится, но не показывается
    /// (образец — `NuclideSetMemoryProbe`); документ — НАСТОЯЩИЙ
    /// `DocEnergySpectrum` из файла корпуса в форме-носителе за краем экрана;
    /// таблицу заполняет НАСТОЯЩАЯ панель `DCPeakDetectionView`; кадр снимает
    /// НАСТОЯЩИЙ `EnergySpectrumView` документа (`DrawToBitmap`, то есть его
    /// собственный `OnPaint`). Своей отрисовки у пробы нет нарочно.
    ///
    /// Разделы:
    ///
    ///   1. ПРОВОДКА: выбор строки таблицы доходит до графика активного
    ///      документа; снятие выбора, строка без пика, смена документа и
    ///      повторный поиск пиков выделение снимают.
    ///   2. ПИКСЕЛИ, обе темы: кадр без выбора против кадра с выбором — сколько
    ///      точек изменилось и ГДЕ (все — в окрестности выбранного пика: полоса
    ///      ПШПВ плюс метка); невыбранные пики не тронуты (0 изменений в их
    ///      полосах); контраст полосы и метки числом. Снятие выбора и строка без
    ///      пика возвращают кадр к исходному ПОБАЙТНО. Несколько строк —
    ///      несколько окрестностей.
    ///
    /// ⛔ ПЛЕЧО «ДО». Против сборки БЕЗ правки (в ней нет
    ///    `DCPeakDetectionView.SelectPeakRows`) проба выбирает строку тем же
    ///    `Selections.SelectCell` через отражение и ТРЕБУЕТ 0 изменившихся точек
    ///    — это и есть дефект `A255`, воспроизведённый числом. Против сборки с
    ///    правкой требуется обратное. Какое плечо мерится — печатается в шапке.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;
        static string outDir = ".";
        static bool fixedBuild;

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            ProbeTargetFramework.Assert();

            string spectrumPath = null, otherPath = null;
            int width = 1200, height = 620, row = -1;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--other=", StringComparison.Ordinal)) otherPath = a.Substring(8);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--row=", StringComparison.Ordinal)) row = int.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectrumPath == null || otherPath == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл> и --other=<файл>");
                return 2;
            }
            Directory.CreateDirectory(outDir);

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`):
            // иначе безоконный прогон встаёт на модальном окне.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager globalConfig = GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            fixedBuild = typeof(DCPeakDetectionView).GetMethod("SelectPeakRows", BindingFlags.Instance | BindingFlags.Public) != null;
            Console.WriteLine("SETUP\tсборка: {0}", fixedBuild
                ? "С ПРАВКОЙ A255 (есть DCPeakDetectionView.SelectPeakRows) — ждём выделение"
                : "БЕЗ ПРАВКИ (плечо «до») — ждём 0 изменившихся точек");
            Console.WriteLine("SETUP\tприложение: {0}", typeof(EnergySpectrumView).Assembly.Location);

            MainForm mainForm = new MainForm();
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            Form panelHost = Host(panel, 520, height);

            DocEnergySpectrum doc = OpenDocument(spectrumPath, nuclides);
            DocEnergySpectrum other = OpenDocument(otherPath, nuclides);
            Form docHost = Host(doc, width, height);
            Form otherHost = Host(other, width, height);

            EnergySpectrumView view = doc.EnergySpectrumView;
            view.PeakMode = PeakMode.Visible;
            other.EnergySpectrumView.PeakMode = PeakMode.Visible;
            // Весь спектр — в кадр (кнопка «по ширине» приложения): на шкале по
            // умолчанию 662 кэВ лежит за правым краем кадра 1200 px, и полоса
            // с меткой рисовались бы за кадром — 0 изменившихся точек при
            // работающем выделении (поймано первым прогоном).
            view.FitHorizontalScale();
            other.EnergySpectrumView.FitHorizontalScale();

            // Таблица заполняется ТЕМ ЖЕ путём, что в приложении: смена активного
            // документа → `ShowPeakDetectionResult` → поиск пиков в фоне →
            // `RefreshTable`. Фоновый поиск докручивается насосом сообщений.
            mainForm.ActiveDocument = doc;
            panel.ShowPeakDetectionResult();
            Pump(panel);
            TableModel model = (TableModel)Field(typeof(DCPeakDetectionView), "tableModel1").GetValue(panel);
            List<Peak> peaks = new List<Peak>(doc.ActiveResultData.DetectedPeaks);
            Console.WriteLine("SETUP\tпиков найдено {0}, строк в таблице {1}", peaks.Count, model.Rows.Count);
            Same("строк в таблице столько же, сколько пиков", peaks.Count, model.Rows.Count);
            if (model.Rows.Count == 0)
            {
                Console.WriteLine("пиков нет — мерить нечего");
                return 1;
            }
            if (row < 0)
            {
                row = LoneStrongPeak(peaks, view);
            }
            int row2 = FarPeak(peaks, view, row);
            Peak picked = peaks[row];
            Console.WriteLine("SETUP\tвыбранная строка {0}: {1} кэВ, канал {2}, ПШПВ {3} каналов, SNR {4}",
                              row, F(picked.Energy, 2), picked.Channel, F(picked.FWHM, 1), F(picked.SNR, 0));
            Console.WriteLine("SETUP\tвторая строка для множественного выбора: {0}", row2);

            ColorConfig colors = globalConfig.GlobalConfig.ColorConfig;
            Console.WriteLine("SETUP\tцвета конфигурации: поле {0}, спектр {1}, линия пика {2}",
                              Hex(colors.BackgroundColor.Color), Hex(colors.ActiveSpectrumColor.Color), Hex(colors.PeakLineColor.Color));

            WiringSection(panel, mainForm, doc, other, model, row, row2);
            foreach (bool dark in new[] { false, true })
            {
                Theme(colors, dark);
                PixelSection(panel, doc, model, peaks, row, row2, width, height, dark);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " НЕ СОШЛОСЬ");

            // Окна закрываются РУКАМИ (образец `NuclideSetMemoryProbe`): документ
            // заводит контроллер измерения, брошенный на финализатор он валит
            // процесс при выходе.
            foreach (Form form in new Form[] { docHost, otherHost, panelHost, doc, other, panel, mainForm })
            {
                form.Dispose();
            }
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // 1. ПРОВОДКА
        // ==================================================================

        static void WiringSection(DCPeakDetectionView panel, MainForm mainForm, DocEnergySpectrum doc,
                                  DocEnergySpectrum other, TableModel model, int row, int row2)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. проводка: таблица → график (A255) ===");
            EnergySpectrumView view = doc.EnergySpectrumView;

            Same("до выбора у графика выделения нет", 0, HighlightCount(view));
            Select(panel, model, row);
            if (!fixedBuild)
            {
                Same("плечо «до»: выбор строки до графика НЕ доходит", 0, HighlightCount(view));
                Select(panel, model);
                return;
            }

            Same("выбор строки дошёл до графика: выделен 1 пик", 1, HighlightCount(view));
            Same("выделен именно пик выбранной строки", true, ReferenceEquals(Highlighted(view)[0], model.Rows[row].Tag));
            Same("панель помнит документ, которому отдала выделение", true, ReferenceEquals(HighlightedDocumentOf(panel), doc));

            Select(panel, model, row, row2);
            Same("две строки — два выделенных пика", 2, HighlightCount(view));

            Select(panel, model);
            Same("снятие выбора снимает выделение", 0, HighlightCount(view));
            Same("панель забыла документ", null, HighlightedDocumentOf(panel));

            // Строка БЕЗ пика: у неё нет `Tag`. В приложении таких строк нет —
            // это положительный контроль на путь «выбрано, а выделять нечего».
            object keptTag = model.Rows[row].Tag;
            model.Rows[row].Tag = null;
            Select(panel, model, row);
            Same("строка без пика: выбор есть, выделения нет", 0, HighlightCount(view));
            model.Rows[row].Tag = keptTag;
            Select(panel, model);

            // Смена документа снимает выделение у ПРЕЖНЕГО.
            Select(panel, model, row);
            Same("перед сменой документа выделение стоит", 1, HighlightCount(view));
            mainForm.ActiveDocument = other;
            panel.ShowPeakDetectionResult();
            Pump(panel);
            Same("смена документа сняла выделение у прежнего", 0, HighlightCount(view));
            Same("у нового документа выделения нет", 0, HighlightCount(other.EnergySpectrumView));
            Same("панель никому не должна", null, HighlightedDocumentOf(panel));

            // Обратно; повторный поиск пиков снимает выделение.
            mainForm.ActiveDocument = doc;
            panel.ShowPeakDetectionResult();
            Pump(panel);
            Same("после возврата таблица снова полна", true, model.Rows.Count > row);
            Select(panel, model, row);
            Same("перед повторным поиском выделение стоит", 1, HighlightCount(view));
            panel.UpdatePeakDetectionResult();
            Pump(panel);
            Same("повторный поиск пиков снял выделение", 0, HighlightCount(view));
            Same("таблица после повторного поиска полна", true, model.Rows.Count > row);

            // Положительный контроль проводки: номера, которого в таблице нет,
            // панель не принимает и графику ничего не шлёт.
            Same("строки № 100000 нет — панель отказывает", false, SelectRowsApi(panel, 100000));
            Same("…и графику ничего не отдала", 0, HighlightCount(view));
        }

        // ==================================================================
        // 2. ПИКСЕЛИ
        // ==================================================================

        static void PixelSection(DCPeakDetectionView panel, DocEnergySpectrum doc, TableModel model,
                                 List<Peak> peaks, int row, int row2, int width, int height, bool dark)
        {
            string theme = dark ? "тёмная" : "светлая";
            string tag = dark ? "dark" : "light";
            Console.WriteLine();
            Console.WriteLine("=== 2. пиксели, тема {0} (A255) ===", theme);
            EnergySpectrumView view = doc.EnergySpectrumView;
            Select(panel, model);

            using (Bitmap none = Frame(view))
            {
                none.Save(Path.Combine(outDir, "g9-" + tag + "-none.png"), ImageFormat.Png);
                Select(panel, model, row);
                using (Bitmap pickedFrame = Frame(view))
                {
                    pickedFrame.Save(Path.Combine(outDir, "g9-" + tag + "-picked.png"), ImageFormat.Png);
                    long total = (long)none.Width * none.Height;
                    List<Point> changed = Changed(none, pickedFrame);
                    Console.WriteLine("  кадр {0}×{1} = {2} точек; изменилось {3} ({4} %)",
                                      none.Width, none.Height, total, changed.Count, Pct(changed.Count, total));
                    if (!fixedBuild)
                    {
                        Same("плечо «до»: выбор строки кадр НЕ меняет", 0, changed.Count);
                        Select(panel, model);
                        return;
                    }

                    Same("выбор строки меняет кадр", true, changed.Count > 0);
                    Rectangle band = BandRect(view, peaks[row], height);
                    Rectangle hood = Hood(view, peaks[row], height);
                    int inHood = 0, inBand = 0, outside = 0;
                    double bandLuma = 0.0;
                    foreach (Point p in changed)
                    {
                        if (hood.Contains(p)) inHood++; else outside++;
                        if (band.Contains(p))
                        {
                            inBand++;
                            bandLuma += Math.Abs(Luma(pickedFrame.GetPixel(p.X, p.Y)) - Luma(none.GetPixel(p.X, p.Y)));
                        }
                    }
                    Console.WriteLine("  полоса ПШПВ: x {0}…{1} ({2} px), окрестность с меткой: x {3}…{4}",
                                      band.Left, band.Right, band.Width, hood.Left, hood.Right);
                    Console.WriteLine("  изменившихся в окрестности {0}, вне её {1}; в полосе {2}, средняя |ΔY| в полосе {3}",
                                      inHood, outside, inBand, inBand == 0 ? "-" : F(bandLuma / inBand, 1));
                    Same("все изменения — в окрестности выбранного пика (вне её 0)", 0, outside);
                    Same("полоса ПШПВ видна: изменилось не меньше половины её точек", true, inBand >= band.Width * (long)band.Height / 2);

                    // Метка над вершиной: её цвет — из конфигурации, контраст к полю.
                    ColorConfig colors = GlobalConfigManager.GetInstance().GlobalConfig.ColorConfig;
                    Color ground = colors.BackgroundColor.Color;
                    Color marker = colors.PeakLineColor.Color;
                    int px = PeakX(view, peaks[row].Channel);
                    int markerPixels = 0;
                    foreach (Point p in changed)
                    {
                        if (Math.Abs(p.X - px) <= 6 && Same3(pickedFrame.GetPixel(p.X, p.Y), marker)) markerPixels++;
                    }
                    Console.WriteLine("  метка: точек цвета линии пика {0} у x={1}; яркость метки {2} против поля {3} (|ΔY| {4}), контраст {5}:1",
                                      markerPixels, px, F(Luma(marker), 0), F(Luma(ground), 0),
                                      F(Math.Abs(Luma(marker) - Luma(ground)), 0), F(ContrastRatio(marker, ground), 2));
                    Same("метка нарисована (точек её цвета над пиком > 20)", true, markerPixels > 20);
                    Same("средняя |ΔY| полосы не меньше 30 (видна глазом)", true, inBand > 0 && bandLuma / inBand >= 30.0);

                    // Невыбранные пики не тронуты: в их полосах 0 изменений.
                    int untouched = 0, checkedPeaks = 0;
                    for (int i = 0; i < peaks.Count; i++)
                    {
                        if (i == row) continue;
                        Rectangle otherBand = Hood(view, peaks[i], height);
                        if (otherBand.IntersectsWith(hood)) continue;
                        checkedPeaks++;
                        int hits = 0;
                        foreach (Point p in changed) if (otherBand.Contains(p)) hits++;
                        if (hits == 0) untouched++;
                    }
                    Console.WriteLine("  невыбранных пиков вне окрестности {0}, из них нетронутых {1}", checkedPeaks, untouched);
                    Same("невыбранные пики не тронуты", checkedPeaks, untouched);
                }

                // Снятие выбора — кадр ПОБАЙТНО исходный.
                Select(panel, model);
                using (Bitmap cleared = Frame(view))
                {
                    Same("снятие выбора возвращает кадр побайтно", 0, Changed(none, cleared).Count);
                }

                // Строка без пика — кадр побайтно исходный.
                object keptTag = model.Rows[row].Tag;
                model.Rows[row].Tag = null;
                Select(panel, model, row);
                using (Bitmap stray = Frame(view))
                {
                    Same("строка без пика: кадр побайтно исходный", 0, Changed(none, stray).Count);
                }
                model.Rows[row].Tag = keptTag;
                Select(panel, model);

                // Две строки — две окрестности, и ничего вне них.
                Select(panel, model, row, row2);
                using (Bitmap multi = Frame(view))
                {
                    multi.Save(Path.Combine(outDir, "g9-" + tag + "-multi.png"), ImageFormat.Png);
                    List<Point> changed = Changed(none, multi);
                    Rectangle a = Hood(view, peaks[row], height), b = Hood(view, peaks[row2], height);
                    int inA = 0, inB = 0, outside = 0;
                    foreach (Point p in changed)
                    {
                        if (a.Contains(p)) inA++;
                        else if (b.Contains(p)) inB++;
                        else outside++;
                    }
                    Console.WriteLine("  две строки: изменилось {0}; у первого пика {1}, у второго {2}, вне обоих {3}",
                                      changed.Count, inA, inB, outside);
                    Same("две строки: у каждого пика есть изменения", true, inA > 0 && inB > 0);
                    Same("две строки: вне двух окрестностей 0", 0, outside);
                }
                Select(panel, model);

                // Положительный контроль мерки: кадр против самого себя со
                // сдвигом выбора обязан ОТКАЗАТЬ — мерка не пустая.
                Select(panel, model, row2);
                using (Bitmap second = Frame(view))
                {
                    Denies("контроль мерки: кадр с другим выбором за исходный не сходит", Changed(none, second).Count == 0);
                }
                Select(panel, model);
            }
        }

        // ==================================================================
        // помощники
        // ==================================================================

        /// <summary>Выбор строк ТЕМ ЖЕ путём, что у мыши: через `Selections` таблицы.</summary>
        static void Select(DCPeakDetectionView panel, TableModel model, params int[] rows)
        {
            if (fixedBuild)
            {
                if (!SelectRowsApi(panel, rows))
                {
                    throw new InvalidOperationException("SelectPeakRows отказал на строках " + string.Join(",", rows));
                }
                return;
            }
            model.Selections.Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i == 0) model.Selections.SelectCell(rows[i], 0); else model.Selections.AddCell(rows[i], 0);
            }
        }

        /// <summary>
        /// `DCPeakDetectionView.SelectPeakRows` — ОТРАЖЕНИЕМ: члена нет в сборке
        /// без правки, а проба обязана собираться и против неё (плечо «до»).
        /// </summary>
        static bool SelectRowsApi(DCPeakDetectionView panel, params int[] rows)
        {
            MethodInfo m = typeof(DCPeakDetectionView).GetMethod("SelectPeakRows", BindingFlags.Instance | BindingFlags.Public);
            if (m == null) throw new InvalidOperationException("нет DCPeakDetectionView.SelectPeakRows — сборка старая");
            return (bool)m.Invoke(panel, new object[] { rows });
        }

        static DocEnergySpectrum HighlightedDocumentOf(DCPeakDetectionView panel)
        {
            PropertyInfo p = typeof(DCPeakDetectionView).GetProperty("HighlightedDocument", BindingFlags.Instance | BindingFlags.Public);
            return p == null ? null : (DocEnergySpectrum)p.GetValue(panel, null);
        }

        static IList<Peak> Highlighted(EnergySpectrumView view)
        {
            PropertyInfo p = typeof(EnergySpectrumView).GetProperty("HighlightedPeaks", Any);
            return p == null ? null : (IList<Peak>)p.GetValue(view, null);
        }

        static int HighlightCount(EnergySpectrumView view)
        {
            IList<Peak> list = Highlighted(view);
            return list == null ? 0 : list.Count;
        }

        /// <summary>Кадр настоящим `OnPaint` вида (`DrawToBitmap` → WM_PRINT).</summary>
        static Bitmap Frame(EnergySpectrumView view)
        {
            var image = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(image, new Rectangle(0, 0, view.Width, view.Height));
            return image;
        }

        static List<Point> Changed(Bitmap a, Bitmap b)
        {
            var list = new List<Point>();
            Rectangle r = new Rectangle(0, 0, a.Width, a.Height);
            BitmapData da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = a.Width * 4;
                byte[] la = new byte[n], lb = new byte[n];
                for (int y = 0; y < a.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(da.Scan0 + y * da.Stride, la, 0, n);
                    System.Runtime.InteropServices.Marshal.Copy(db.Scan0 + y * db.Stride, lb, 0, n);
                    for (int x = 0; x < a.Width; x++)
                    {
                        int k = x * 4;
                        if (la[k] != lb[k] || la[k + 1] != lb[k + 1] || la[k + 2] != lb[k + 2] || la[k + 3] != lb[k + 3])
                        {
                            list.Add(new Point(x, y));
                        }
                    }
                }
            }
            finally
            {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
            return list;
        }

        /// <summary>X канала — ТЕМ ЖЕ методом вида, каким он ставит пики (`PeakX`).</summary>
        static int PeakX(EnergySpectrumView view, double channel)
        {
            MethodInfo m = typeof(EnergySpectrumView).GetMethod("PeakX", Any);
            if (m == null) throw new InvalidOperationException("нет EnergySpectrumView.PeakX — сборка старая");
            return (int)m.Invoke(view, new object[] { channel });
        }

        static Rectangle BandRect(EnergySpectrumView view, Peak peak, int height)
        {
            int x1 = PeakX(view, peak.Channel - peak.FWHM / 2.0);
            int x2 = PeakX(view, peak.Channel + peak.FWHM / 2.0);
            if (x2 < x1) { int t = x1; x1 = x2; x2 = t; }
            if (x2 == x1) x2 = x1 + 1;
            int h = (int)Field(typeof(EnergySpectrumView), "height").GetValue(view);
            return new Rectangle(x1, 0, x2 - x1, h);
        }

        /// <summary>Окрестность пика: полоса ПШПВ, расширенная на метку (±7 px) с запасом 1 px.</summary>
        static Rectangle Hood(EnergySpectrumView view, Peak peak, int height)
        {
            Rectangle band = BandRect(view, peak, height);
            int px = PeakX(view, peak.Channel);
            int left = Math.Min(band.Left, px - 8), right = Math.Max(band.Right, px + 8);
            return new Rectangle(left - 1, 0, right - left + 2, view.Height);
        }

        /// <summary>Самый сильный пик, чья окрестность не пересекается с соседними.</summary>
        static int LoneStrongPeak(List<Peak> peaks, EnergySpectrumView view)
        {
            int best = -1;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (!(peaks[i].FWHM > 0.0)) continue;
                if (best < 0 || peaks[i].SNR > peaks[best].SNR) best = i;
            }
            return best < 0 ? 0 : best;
        }

        static int FarPeak(List<Peak> peaks, EnergySpectrumView view, int row)
        {
            int best = -1;
            double bestDistance = 0.0;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (i == row || !(peaks[i].FWHM > 0.0)) continue;
                double d = Math.Abs(peaks[i].Channel - peaks[row].Channel);
                if (d > bestDistance) { bestDistance = d; best = i; }
            }
            return best < 0 ? row : best;
        }

        static void Theme(ColorConfig colors, bool dark)
        {
            colors.BackgroundColor = dark ? Color.Black : Color.White;
            colors.ActiveSpectrumColor = dark ? Color.White : Color.Black;
            colors.PeakLineColor = Color.Red;
            colors.PeakFigureColor = dark ? Color.White : Color.Black;
            colors.PeakBackgroundColor = dark ? Color.FromArgb(48, 48, 48) : Color.LightYellow;
            colors.GridColor1 = dark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(220, 220, 220);
            colors.GridColor2 = dark ? Color.FromArgb(24, 24, 24) : Color.FromArgb(240, 240, 240);
            Console.WriteLine("SETUP\tтема {0}: поле {1}, спектр {2}, линия пика {3}", dark ? "тёмная" : "светлая",
                              Hex(colors.BackgroundColor.Color), Hex(colors.ActiveSpectrumColor.Color), Hex(colors.PeakLineColor.Color));
        }

        /// <summary>Докрутить фоновый поиск пиков панели насосом сообщений.</summary>
        static void Pump(DCPeakDetectionView panel)
        {
            FieldInfo busy = Field(typeof(DCPeakDetectionView), "isProcessing");
            FieldInfo pending = Field(typeof(DCPeakDetectionView), "refreshPending");
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            do
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("поиск пиков не завершился за 60 с");
                }
            }
            while ((bool)busy.GetValue(panel) || (bool)pending.GetValue(panel));
            Application.DoEvents();
        }

        static DocEnergySpectrum OpenDocument(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("нет файла спектра", path);
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

            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
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

        static double Luma(Color c)
        {
            return 0.30 * c.R + 0.59 * c.G + 0.11 * c.B;
        }

        static bool Same3(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        /// <summary>Контраст по WCAG: (L1 + 0.05) / (L2 + 0.05) на линейной яркости.</summary>
        static double ContrastRatio(Color a, Color b)
        {
            double la = Linear(a), lb = Linear(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static double Linear(Color c)
        {
            return 0.2126 * Chan(c.R) + 0.7152 * Chan(c.G) + 0.0722 * Chan(c.B);
        }

        static double Chan(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        static string F(double v, int digits)
        {
            return v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static string Pct(long part, long whole)
        {
            return whole == 0 ? "-" : (100.0 * part / whole).ToString("F3", CultureInfo.InvariantCulture);
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
