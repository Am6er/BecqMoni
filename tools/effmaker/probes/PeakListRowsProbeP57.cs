using BecquerelMonitor;
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

namespace PeakListRowsProbeP57
{
    /// <summary>
    /// ПРИЁМКА `AMBER26` (задача Amber 14.09.2026: в списке пиков строка с N
    /// именами-кандидатами высотой базовая × N, имена с новой строки, а не
    /// через « / ») — полоса П57, 14.09.2026.
    ///
    ///     peaklistrowsprobep57 --spectrum=&lt;файл&gt; [--device=&lt;прибор корпуса .xml&gt;]
    ///                          [--shots=&lt;каталог снимков&gt;] [--snr=5] [--tol=20]
    ///                          [--width=432] [--height=300] [--expect-overflow]
    ///
    /// ⚠ СРЕДА — КАТАЛОГ ПРОБ (`build_pN`): там поставочная библиотека нуклидов,
    ///    как у человека за экраном, но НЕТ приборов корпуса; прибор спектра
    ///    даётся файлом `--device=` (`tools/CORPUS/corpus/devices/*.xml`) и
    ///    ставится спектру правилом `ProbeDeviceConfig.Attach`, в менеджер не
    ///    кладётся. Оснастка корпуса (`wd_*`) не годится: по `AMBER19` в ней
    ///    нет библиотеки, и `NuclideDefinitionManager` отказывает без окна.
    ///
    /// ⛔ Снимки `p57-*.png` идут в каталог ключа `--shots=`, без ключа — в
    ///    `%TEMP%\peaklistrows-shots`; каталог печатается ПЕРВОЙ строкой (образец
    ///    `PeakHighlightProbeG9`).
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ. `MainForm` заводится, но не показывается;
    /// документ — НАСТОЯЩИЙ `DocEnergySpectrum` из файла корпуса в форме-носителе
    /// за краем экрана; таблицу заполняет НАСТОЯЩАЯ панель `DCPeakDetectionView`
    /// тем же путём, что в приложении (`ActiveDocument` → `ShowPeakDetectionResult`
    /// → поиск пиков → `RefreshTable`); кадр снимает НАСТОЯЩИЙ `XPTable.Table`
    /// (`DrawToBitmap` → WM_PRINT → его `OnPaint`). Своей отрисовки у пробы нет.
    ///
    /// Разделы:
    ///
    ///   1. XPTable УМЕЕТ — отражением: `Row.Height` с открытым сеттером,
    ///      `Table.EnableWordWrap`, `Cell.WordWrap`; у панели ключ поднят.
    ///   2. МОДЕЛЬ: у каждой строки высота = базовая × N (N — число кандидатов
    ///      её пика), у строк с одним именем — базовая (положительный контроль);
    ///      текст графы «Nuclide» — N имён через перевод строки, без « / », в
    ///      порядке кандидатов, победитель первым. Дефект — ЧИСЛОМ: ширина
    ///      прежней надписи « / » против ширины графы.
    ///   3. ГЕОМЕТРИЯ: `RowRect` высокой строки = базовая × N; следующая строка
    ///      начинается ровно под ней; `RowIndexAt` по верхней и нижней точке
    ///      высокой строки отдаёт её, точкой ниже — следующую; сумма высот =
    ///      `TotalRowAndHeaderHeight` − заголовок.
    ///   4. ПИКСЕЛИ (обе культуры): в ячейке высокой строки ровно N полос чернил
    ///      (N строк текста), последняя не упирается в низ ячейки, ни одна — в
    ///      правый край; у строки с одним именем — одна полоса.
    ///   5. МЫШЬ: `OnMouseDown`/`OnMouseUp` таблицы (отражением) по верхней и
    ///      нижней точке высокой строки выбирают ЕЁ, точкой ниже — следующую;
    ///      выбор доходит до графика (`A255`).
    ///   6. СОРТИРОВКА по энергии в обе стороны: высоты едут вместе со строками
    ///      (по `Tag`), геометрия сходится и после.
    ///   7. ПРОКРУТКА: полоса есть (носитель ужимается при нужде);
    ///      `EnsureVisible` последней строки ставит её в кадр; шаг полосы на 1
    ///      двигает верхнюю строку и `RowIndexAt` по первой видимой точке.
    ///
    /// ⛔ ПЛЕЧО «ДО» (сборка без `PeakDetector.PeakLabel(Peak, string)`): та же
    ///    проба ждёт прежнего — все строки базовой высоты, « / » в тексте,
    ///    одна полоса чернил, — и печатает ширину надписи против ширины графы:
    ///    это дефект `AMBER26`, воспроизведённый числом. Плечо печатается в шапке.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; 1 — не сошлись или мерить нечего (ни
    /// одной строки с двумя именами); 2 — ключи/файлы.
    /// </summary>
    static class Program
    {
        static int bad;
        static string outDir;
        static bool fixedBuild;
        const string FlagSeparatorMark = " / ";

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, devicePath = null;
            int width = 432, height = 300;
            bool expectOverflow = false;
            double snr = 5.0, tol = 20.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--device=", StringComparison.Ordinal)) devicePath = a.Substring(9);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) outDir = a.Substring(8);
                else if (a == "--expect-overflow") expectOverflow = true;
                else if (a.StartsWith("--snr=", StringComparison.Ordinal)) snr = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }
            bool shotsGiven = !string.IsNullOrEmpty(outDir);
            if (!shotsGiven)
            {
                outDir = Path.Combine(Path.GetTempPath(), "peaklistrows-shots");
            }
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            Console.WriteLine(shotsGiven
                ? "снимки: " + outDir + " (ключ --shots=)"
                : "снимки: " + outDir + " (ключ --shots= не задан, каталог по умолчанию)");
            ProbeTargetFramework.Assert();
            // Тот же режим отрисовки текста, что у приложения (`Program.Main`) —
            // ДО первого элемента управления, иначе вызов отказывает.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            fixedBuild = typeof(PeakDetector).GetMethod("PeakLabel", BindingFlags.Static | BindingFlags.Public,
                                                        null, new[] { typeof(Peak), typeof(string) }, null) != null;
            Console.WriteLine("SETUP\tсборка: {0}", fixedBuild
                ? "С ПРАВКОЙ AMBER26 (есть PeakDetector.PeakLabel(Peak, string)) — ждём строки ×N"
                : "БЕЗ ПРАВКИ (плечо «до») — ждём базовую высоту и « / » у всех");
            Console.WriteLine("SETUP\tприложение: {0}", typeof(DCPeakDetectionView).Assembly.Location);
            Console.WriteLine("SETUP\tпорог SNR {0}, допуск {1} (ключи --snr=/--tol=)", F(snr, 1), F(tol, 1));

            CultureInfo hadUi = Thread.CurrentThread.CurrentUICulture;
            CultureInfo hadResources = BecquerelMonitor.Properties.Resources.Culture;
            MainForm mainForm = new MainForm();
            Language("en");

            DocEnergySpectrum doc = OpenDocument(spectrumPath, devicePath, nuclides, snr, tol);
            Form docHost = Host(doc, 1000, 500);

            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            Form panelHost = Host(panel, width, height);
            mainForm.ActiveDocument = doc;
            panel.ShowPeakDetectionResult();
            Pump(panel);

            Table table = (Table)Field(typeof(DCPeakDetectionView), "table1").GetValue(panel);
            TableModel model = table.TableModel;
            List<Peak> peaks = new List<Peak>(doc.ActiveResultData.DetectedPeaks);
            Console.WriteLine("SETUP\tпиков найдено {0}, строк в таблице {1}, базовая высота строки {2} px, графа «{3}» {4} px",
                              peaks.Count, model.Rows.Count, model.RowHeight, table.ColumnModel.Columns[0].Text, table.ColumnModel.Columns[0].Width);
            Console.WriteLine("SETUP\tтаблица {0}×{1} px, область данных y {2}..{3} ({4} px = {5} базовых строк), носитель {6}×{7}",
                              table.Width, table.Height, table.CellDataRect.Top, table.CellDataRect.Bottom, table.CellDataRect.Height,
                              table.CellDataRect.Height / Math.Max(1, model.RowHeight), width, height);
            Same("строк в таблице столько же, сколько пиков", peaks.Count, model.Rows.Count);

            int[] names = new int[model.Rows.Count];
            int multi = 0, maxNames = 1, firstMulti = -1, firstSingle = -1;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                Peak peak = model.Rows[i].Tag as Peak;
                names[i] = peak == null || peak.Nuclide == null ? 1 : peak.NuclideCandidates.Count;
                if (names[i] > 1) { multi++; if (firstMulti < 0) firstMulti = i; if (names[i] > maxNames) maxNames = names[i]; }
                else if (firstSingle < 0) firstSingle = i;
            }
            Console.WriteLine("SETUP\tстрок с двумя и более именами {0} (наибольшее число имён {1}), первая такая — {2}, первая с одним именем — {3}",
                              multi, maxNames, firstMulti, firstSingle);
            if (multi == 0 || firstSingle < 0)
            {
                Console.WriteLine("нужна хотя бы одна строка с двумя именами И одна с одним — мерить нечего (SNR/допуск/спектр)");
                return 1;
            }

            AbilitySection(table);
            ModelSection(table, model, names, expectOverflow);
            GeometrySection(table, model, names, "исходный порядок");
            PixelSection(table, model, names, "en", panel);
            MouseSection(panel, table, model, names, doc);
            SortSection(table, model, names);
            ScrollSection(panelHost, panel, table, model, names, width);

            // Русская культура — своя панель: заголовки граф (`Изотоп`) кладёт
            // designer-код при создании, а не при показе.
            Language("ru");
            DCPeakDetectionView panelRu = new DCPeakDetectionView(mainForm);
            Form panelRuHost = Host(panelRu, width, height);
            panelRu.ShowPeakDetectionResult();
            Pump(panelRu);
            Table tableRu = (Table)Field(typeof(DCPeakDetectionView), "table1").GetValue(panelRu);
            Console.WriteLine();
            Console.WriteLine("=== ru: заголовок графы «{0}» ===", tableRu.ColumnModel.Columns[0].Text);
            Same("ru: строк столько же", model.Rows.Count, tableRu.TableModel.Rows.Count);
            int[] namesRu = new int[tableRu.TableModel.Rows.Count];
            for (int i = 0; i < namesRu.Length; i++)
            {
                Peak peak = tableRu.TableModel.Rows[i].Tag as Peak;
                namesRu[i] = peak == null || peak.Nuclide == null ? 1 : peak.NuclideCandidates.Count;
            }
            PixelSection(tableRu, tableRu.TableModel, namesRu, "ru", panelRu);
            Language("en");
            Thread.CurrentThread.CurrentUICulture = hadUi;
            BecquerelMonitor.Properties.Resources.Culture = hadResources;

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " НЕ СОШЛОСЬ");

            foreach (Form form in new Form[] { docHost, panelHost, panelRuHost, doc, panel, panelRu, mainForm })
            {
                form.Dispose();
            }
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // 1. XPTable умеет — отражением
        // ==================================================================

        static void AbilitySection(Table table)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. XPTable умеет: высота отдельной строки и многострочная ячейка ===");
            PropertyInfo rowHeight = typeof(Row).GetProperty("Height", BindingFlags.Instance | BindingFlags.Public);
            Same("Row.Height есть и открыт на запись", true, rowHeight != null && rowHeight.CanWrite && rowHeight.GetSetMethod() != null);
            PropertyInfo enable = typeof(Table).GetProperty("EnableWordWrap", BindingFlags.Instance | BindingFlags.Public);
            Same("Table.EnableWordWrap есть (переключатель переменной высоты)", true, enable != null && enable.CanWrite);
            PropertyInfo cellWrap = typeof(Cell).GetProperty("WordWrap", BindingFlags.Instance | BindingFlags.Public);
            Same("Cell.WordWrap есть (перенос в ячейке)", true, cellWrap != null && cellWrap.CanWrite);
            Same("у панели переменная высота включена", fixedBuild, table.EnableWordWrap);

            // Перевод строки в GDI+ `DrawString` — перенос даже при `NoWrap`
            // (флаг ставит `CellRenderer.DrawString` ячейке без `WordWrap`).
            using (Bitmap bmp = new Bitmap(8, 8))
            using (Graphics g = Graphics.FromImage(bmp))
            using (StringFormat nowrap = new StringFormat { FormatFlags = StringFormatFlags.NoWrap })
            {
                int chars, lines;
                g.MeasureString("Ac-228\nPb-212\nXray-Pb", table.Font, new SizeF(1000, 1000), nowrap, out chars, out lines);
                Same("GDI+ при NoWrap считает «a\\nb\\nc» тремя строками", 3, lines);
            }
        }

        // ==================================================================
        // 2. Модель
        // ==================================================================

        static void ModelSection(Table table, TableModel model, int[] names, bool expectOverflow)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. модель: высота строки = базовая × N, имена с новой строки ===");
            int baseHeight = model.RowHeight;
            int cellWidth = table.ColumnModel.Columns[0].Width - 2;
            int overflow = 0;
            using (Bitmap bmp = new Bitmap(8, 8))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                for (int i = 0; i < model.Rows.Count; i++)
                {
                    Row row = model.Rows[i];
                    Peak peak = (Peak)row.Tag;
                    int n = names[i];
                    int expected = fixedBuild && n > 1 ? baseHeight * n : baseHeight;
                    string text = row.Cells[0].Text;
                    int breaks = text.Split('\n').Length - 1;
                    bool slash = text.IndexOf(FlagSeparatorMark, StringComparison.Ordinal) >= 0;
                    string flag = PeakDetector.PeakLabel(peak);
                    float flagWidth = g.MeasureString(flag, table.Font).Width;
                    if (n > 1 && flagWidth > cellWidth) overflow++;

                    string what = string.Format(CultureInfo.InvariantCulture, "строка {0} ({1} кэВ, имён {2})", i, F(peak.Energy, 1), n);
                    Same(what + ": высота", expected, row.Height);
                    Same(what + ": переводов строки в тексте", fixedBuild ? n - 1 : 0, breaks);
                    Same(what + ": « / » в тексте", !fixedBuild && n > 1, slash);
                    if (fixedBuild && n > 1)
                    {
                        string[] lines = text.Split('\n');
                        bool order = true;
                        for (int k = 0; k < n; k++) order &= lines[k] == peak.NuclideCandidates[k].Name;
                        Same(what + ": имена в порядке кандидатов, победитель первым", true, order);
                        Same(what + ": выравнивание строки по верху", RowAlignment.Top, row.Alignment);
                        Console.WriteLine("       {0}  |  флажок «{1}» шириной {2} px против графы {3} px",
                                          text.Replace("\n", " ⏎ "), flag, F(flagWidth, 0), cellWidth);
                    }
                    else if (n == 1)
                    {
                        Same(what + ": выравнивание по центру (как прежде)", RowAlignment.Center, row.Alignment);
                    }
                }
            }
            // Дефект числом: хотя бы у одной строки с соперниками надпись « / »
            // шире графы — то, что Amber видела как «третий кандидат не отобразим».
            // Это свойство СПЕКТРА (длина имён), а не сборки, поэтому отказ — только
            // по ключу `--expect-overflow` (приёмочный спектр); без ключа — число.
            Console.WriteLine("  строк, у которых надпись « / » шире графы «Nuclide»: {0}", overflow);
            if (expectOverflow)
            {
                Denies("положительный контроль (--expect-overflow): надпись « / » хотя бы одной строки НЕ ВЛЕЗАЕТ в графу", overflow == 0);
            }
        }

        // ==================================================================
        // 3. Геометрия
        // ==================================================================

        static void GeometrySection(Table table, TableModel model, int[] names, string tag)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. геометрия ({0}): RowRect, RowIndexAt, сумма высот; верхняя строка {1} ===", tag, table.TopIndex);
            BringMultiIntoView(table, model, names);
            int baseHeight = model.RowHeight;
            int x = table.ColumnModel.Columns[0].Width / 2;
            int sum = 0;
            int checkedMulti = 0;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                Rectangle rect = table.RowRect(i);
                int n = names[i];
                int expected = fixedBuild && n > 1 ? baseHeight * n : baseHeight;
                sum += model.Rows[i].Height;
                // За кадром (выше верха данных или ниже низа) мышью не достать —
                // `RowIndexAt` там по праву отдаёт −1; после сортировки XPTable
                // сам подкручивает таблицу к выбранной строке, и верх уезжает.
                if (rect.Top < table.CellDataRect.Top || rect.Bottom > table.CellDataRect.Bottom) continue;
                if (n > 1 || i == 0)
                {
                    string what = string.Format(CultureInfo.InvariantCulture, "строка {0} (имён {1})", i, n);
                    Same(what + ": RowRect.Height", expected, rect.Height);
                    Same(what + ": RowIndexAt по верхней точке", i, table.RowIndexAt(x, rect.Top + 1));
                    Same(what + ": RowIndexAt по нижней точке", i, table.RowIndexAt(x, rect.Bottom - 1));
                    if (i + 1 < model.Rows.Count)
                    {
                        Same(what + ": следующая начинается ровно под ней", rect.Bottom, table.RowRect(i + 1).Y);
                        Same(what + ": точкой ниже — следующая строка", i + 1, table.RowIndexAt(x, rect.Bottom + 1));
                    }
                    if (n > 1) checkedMulti++;
                }
            }
            Same("сумма высот строк = TotalRowAndHeaderHeight − заголовок", sum, table.TotalRowAndHeaderHeight - table.HeaderHeight);
            Same("хотя бы одна высокая строка в кадре проверена (не пустой раздел)", true, checkedMulti > 0);
        }

        // ==================================================================
        // 4. Пиксели
        // ==================================================================

        static void PixelSection(Table table, TableModel model, int[] names, string lang, DCPeakDetectionView panel)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. пиксели ({0}): полосы чернил в ячейке «Nuclide» ===", lang);
            model.Selections.Clear();
            BringMultiIntoView(table, model, names);
            Application.DoEvents();
            string path = Path.Combine(outDir, "p57-table-" + lang + ".png");
            using (Bitmap frame = new Bitmap(panel.Width, panel.Height))
            {
                panel.DrawToBitmap(frame, new Rectangle(0, 0, panel.Width, panel.Height));
                frame.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("  снимок панели: {0}", path);

            // Каждая высокая строка подводится в кадр и меряется на СВОЁМ кадре
            // таблицы; одиночные — два контроля с первого кадра.
            int shownMulti = 0, shownSingle = 0;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                int n = names[i];
                if (n == 1 && shownSingle >= 2) continue;   // два контроля хватит
                Rectangle rect = table.RowRect(i);
                if (rect.Top < table.CellDataRect.Top || rect.Bottom > table.CellDataRect.Bottom)
                {
                    if (n == 1) continue;
                    table.EnsureVisible(i, 0);
                    Application.DoEvents();
                    Console.WriteLine("  строка {0} подведена в кадр: верхняя строка теперь {1}", i, table.TopIndex);
                }
                Rectangle cell = table.CellRect(i, 0);
                if (cell.Bottom > table.CellDataRect.Bottom || cell.Top < table.CellDataRect.Top)
                {
                    Same(string.Format(CultureInfo.InvariantCulture, "{0}: строка {1} (имён {2}) помещается в кадр", lang, i, n), true, false);
                    continue;
                }
                using (Bitmap frame = new Bitmap(table.Width, table.Height))
                {
                    table.DrawToBitmap(frame, new Rectangle(0, 0, table.Width, table.Height));
                    List<Rectangle> bands = InkBands(frame, Rectangle.Inflate(cell, -1, -1));
                    string what = string.Format(CultureInfo.InvariantCulture, "{0}: строка {1} (имён {2}, ячейка {3}×{4})", lang, i, n, cell.Width, cell.Height);
                    int expectedBands = fixedBuild ? n : 1;
                    Same(what + ": полос чернил", expectedBands, bands.Count);
                    if (bands.Count > 0)
                    {
                        Rectangle last = bands[bands.Count - 1];
                        Same(what + ": последняя полоса не упирается в низ ячейки", true, last.Bottom < cell.Bottom - 1);
                        int rightMost = 0;
                        foreach (Rectangle b in bands) rightMost = Math.Max(rightMost, b.Right);
                        Same(what + ": чернила не доходят до правого края ячейки", true, rightMost < cell.Right - 2);
                        StringBuilder sb = new StringBuilder();
                        foreach (Rectangle b in bands) sb.AppendFormat(CultureInfo.InvariantCulture, " [y {0}..{1}, x {2}..{3}]", b.Top - cell.Top, b.Bottom - cell.Top, b.Left - cell.Left, b.Right - cell.Left);
                        Console.WriteLine("       полосы:{0}", sb);
                    }
                    if (n > 1)
                    {
                        shownMulti++;
                        if (n >= 3)
                        {
                            string tall = Path.Combine(outDir, "p57-table-" + lang + "-x" + n.ToString(CultureInfo.InvariantCulture) + ".png");
                            using (Bitmap whole = new Bitmap(panel.Width, panel.Height))
                            {
                                panel.DrawToBitmap(whole, new Rectangle(0, 0, panel.Width, panel.Height));
                                whole.Save(tall, ImageFormat.Png);
                            }
                            Console.WriteLine("  снимок со строкой ×{0}: {1}", n, tall);
                        }
                    }
                    else shownSingle++;
                }
            }
            Console.WriteLine("  проверено высоких строк {0} (все), одиночных {1}", shownMulti, shownSingle);
            Same(lang + ": высокие строки проверены все", true, shownMulti > 0 && shownMulti == CountMulti(names));
            table.EnsureVisible(0, 0);
            Application.DoEvents();
        }

        static int CountMulti(int[] names)
        {
            int k = 0;
            foreach (int n in names) if (n > 1) k++;
            return k;
        }

        /// <summary>
        /// Первая высокая строка — в кадр (`EnsureVisible`), если она за ним:
        /// у длинного списка она может лежать ниже видимой области, а после
        /// сортировки XPTable сам подкручивает таблицу к выбранной строке.
        /// </summary>
        static void BringMultiIntoView(Table table, TableModel model, int[] names)
        {
            for (int i = 0; i < model.Rows.Count; i++)
            {
                if (names[i] <= 1) continue;
                Rectangle rect = table.RowRect(i);
                if (rect.Top < table.CellDataRect.Top || rect.Bottom > table.CellDataRect.Bottom)
                {
                    table.EnsureVisible(i, 0);
                    Application.DoEvents();
                    Console.WriteLine("  строка {0} подведена в кадр: верхняя строка теперь {1}", i, table.TopIndex);
                }
                return;
            }
        }

        /// <summary>Горизонтальные полосы чернил (строки текста) в прямоугольнике кадра.</summary>
        static List<Rectangle> InkBands(Bitmap frame, Rectangle area)
        {
            var bands = new List<Rectangle>();
            int start = -1, left = int.MaxValue, right = -1;
            for (int y = area.Top; y <= area.Bottom; y++)
            {
                bool ink = false;
                int rowLeft = int.MaxValue, rowRight = -1;
                if (y < area.Bottom)
                {
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        Color c = frame.GetPixel(x, y);
                        if (0.30 * c.R + 0.59 * c.G + 0.11 * c.B < 128.0)
                        {
                            ink = true;
                            if (x < rowLeft) rowLeft = x;
                            if (x > rowRight) rowRight = x;
                        }
                    }
                }
                if (ink)
                {
                    if (start < 0) { start = y; left = int.MaxValue; right = -1; }
                    left = Math.Min(left, rowLeft);
                    right = Math.Max(right, rowRight);
                }
                else if (start >= 0)
                {
                    bands.Add(Rectangle.FromLTRB(left, start, right + 1, y));
                    start = -1;
                }
            }
            return bands;
        }

        // ==================================================================
        // 5. Мышь
        // ==================================================================

        static void MouseSection(DCPeakDetectionView panel, Table table, TableModel model, int[] names, DocEnergySpectrum doc)
        {
            Console.WriteLine();
            Console.WriteLine("=== 5. мышь: OnMouseDown/OnMouseUp таблицы по высокой строке ===");
            BringMultiIntoView(table, model, names);
            int target = -1;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                if (names[i] > 1 && table.RowRect(i).Bottom + 2 < table.CellDataRect.Bottom && i + 1 < model.Rows.Count) { target = i; break; }
            }
            if (target < 0)
            {
                Console.WriteLine("  высокой строки в кадре нет — раздел пропущен");
                bad++;
                return;
            }
            Rectangle rect = table.RowRect(target);
            int x = table.ColumnModel.Columns[1].Left + 4;
            Peak expected = (Peak)model.Rows[target].Tag;

            Click(table, x, rect.Top + 1);
            Same("клик по верхней точке высокой строки выбирает её", target, SelectedRow(table));
            Same("выбор дошёл до графика (A255): тот самый пик", true, HighlightedIs(doc, expected));

            Click(table, x, rect.Bottom - 1);
            Same("клик по нижней точке той же строки — она же", target, SelectedRow(table));
            Same("выбранных строк одна", 1, table.SelectedIndicies.Length);

            string picked = Path.Combine(outDir, "p57-table-en-picked.png");
            using (Bitmap frame = new Bitmap(panel.Width, panel.Height))
            {
                panel.DrawToBitmap(frame, new Rectangle(0, 0, panel.Width, panel.Height));
                frame.Save(picked, ImageFormat.Png);
            }
            Console.WriteLine("  снимок с выбором: {0}", picked);

            Click(table, x, rect.Bottom + 1);
            Same("клик точкой ниже — следующая строка", target + 1, SelectedRow(table));

            model.Selections.Clear();
            Application.DoEvents();
            Same("снятие выбора: выбранных нет", 0, table.SelectedIndicies.Length);
        }

        static void Click(Table table, int x, int y)
        {
            MethodInfo down = Method(typeof(Table), "OnMouseDown");
            MethodInfo up = Method(typeof(Table), "OnMouseUp");
            var e = new MouseEventArgs(MouseButtons.Left, 1, x, y, 0);
            down.Invoke(table, new object[] { e });
            up.Invoke(table, new object[] { e });
            Application.DoEvents();
        }

        static int SelectedRow(Table table)
        {
            int[] rows = table.SelectedIndicies;
            return rows.Length == 0 ? -1 : rows[0];
        }

        static bool HighlightedIs(DocEnergySpectrum doc, Peak peak)
        {
            PropertyInfo p = typeof(EnergySpectrumView).GetProperty("HighlightedPeaks", Any);
            if (p == null) return false;
            IList<Peak> list = (IList<Peak>)p.GetValue(doc.EnergySpectrumView, null);
            return list != null && list.Count == 1 && ReferenceEquals(list[0], peak);
        }

        // ==================================================================
        // 6. Сортировка
        // ==================================================================

        static void SortSection(Table table, TableModel model, int[] names)
        {
            Console.WriteLine();
            Console.WriteLine("=== 6. сортировка по энергии: высоты едут со строками ===");
            int baseHeight = model.RowHeight;
            foreach (SortOrder order in new[] { SortOrder.Descending, SortOrder.Ascending })
            {
                table.Sort(1, order);
                Application.DoEvents();
                bool monotone = true, heights = true;
                double previous = order == SortOrder.Descending ? double.MaxValue : double.MinValue;
                int[] namesNow = new int[model.Rows.Count];
                for (int i = 0; i < model.Rows.Count; i++)
                {
                    Peak peak = (Peak)model.Rows[i].Tag;
                    int n = peak.Nuclide == null ? 1 : peak.NuclideCandidates.Count;
                    namesNow[i] = n;
                    int expected = fixedBuild && n > 1 ? baseHeight * n : baseHeight;
                    heights &= model.Rows[i].Height == expected;
                    monotone &= order == SortOrder.Descending ? peak.Energy <= previous : peak.Energy >= previous;
                    previous = peak.Energy;
                }
                Same(order + ": порядок строк по энергии", true, monotone);
                Same(order + ": высота каждой строки = базовая × N её пика", true, heights);
                GeometrySection(table, model, namesNow, order.ToString());
            }
            // Исходный порядок пиков — по каналу; после Ascending по энергии он тот же.
            for (int i = 0; i < model.Rows.Count; i++)
            {
                Peak peak = (Peak)model.Rows[i].Tag;
                names[i] = peak.Nuclide == null ? 1 : peak.NuclideCandidates.Count;
            }
        }

        // ==================================================================
        // 7. Прокрутка
        // ==================================================================

        static void ScrollSection(Form host, DCPeakDetectionView panel, Table table, TableModel model, int[] names, int width)
        {
            Console.WriteLine();
            Console.WriteLine("=== 7. прокрутка ===");
            if (!table.VScroll)
            {
                // Ужать носитель так, чтобы в кадр влезало три базовых строки.
                int want = table.Top + table.HeaderHeight + model.RowHeight * 3 + 8;
                host.ClientSize = new Size(width, want);
                Application.DoEvents();
                Console.WriteLine("  носитель ужат до {0} px — полоса прокрутки: {1}", want, table.VScroll);
            }
            Same("полоса прокрутки есть", true, table.VScroll);
            if (!table.VScroll) return;

            int last = model.Rows.Count - 1;
            int x = table.ColumnModel.Columns[0].Width / 2;
            table.EnsureVisible(last, 0);
            Application.DoEvents();
            Rectangle rect = table.RowRect(last);
            Same("EnsureVisible(последняя): её низ в кадре", true, rect.Bottom <= table.CellDataRect.Bottom);
            Same("EnsureVisible(последняя): её верх в кадре", true, rect.Top >= table.CellDataRect.Top);
            Same("EnsureVisible(последняя): RowIndexAt по её верхней точке", last, table.RowIndexAt(x, rect.Top + 1));
            Same("EnsureVisible(последняя): RowIndexAt по её нижней точке", last, table.RowIndexAt(x, rect.Bottom - 1));
            Console.WriteLine("  верхняя строка после EnsureVisible: {0}, строк всего {1}", table.TopIndex, model.Rows.Count);

            // Каждая высокая строка — `EnsureVisible` с верха и целиком в кадре:
            // так XPTable подводит строку при ходьбе стрелками (`FocusedCell`) и
            // после сортировки. С переменной высотой прежний расчёт (число целых
            // строк от ТЕКУЩЕГО верха плюс одна) оставлял высокую строку внизу
            // обрезанной — поправка в `Table.EnsureVisible` (П57).
            for (int i = 0; i < model.Rows.Count; i++)
            {
                if (names[i] <= 1) continue;
                table.EnsureVisible(0, 0);
                Application.DoEvents();
                table.EnsureVisible(i, 0);
                Application.DoEvents();
                Rectangle r = table.RowRect(i);
                string what = string.Format(CultureInfo.InvariantCulture, "EnsureVisible(строка {0}, имён {1}) с верха: верхняя строка {2}", i, names[i], table.TopIndex);
                Same(what + ", верх в кадре", true, r.Top >= table.CellDataRect.Top);
                Same(what + ", низ в кадре", true, r.Bottom <= table.CellDataRect.Bottom);
            }

            table.EnsureVisible(0, 0);
            Application.DoEvents();
            Same("EnsureVisible(0): верхняя строка 0", 0, table.TopIndex);
            table.VerticalScrollBar.Value = table.VerticalScrollBar.Value + 1;
            Application.DoEvents();
            Same("шаг полосы на 1: верхняя строка 1", 1, table.TopIndex);
            Rectangle first = table.RowRect(1);
            Same("шаг полосы на 1: строка 1 начинается у верха данных", table.CellDataRect.Top, first.Top);
            Same("шаг полосы на 1: RowIndexAt по первой видимой точке", 1, table.RowIndexAt(x, table.CellDataRect.Top + 1));
            int expected1 = fixedBuild && names[1] > 1 ? model.RowHeight * names[1] : model.RowHeight;
            Same("шаг полосы на 1: высота строки 1 прежняя", expected1, first.Height);
            Same("шаг полосы на 1: строка 2 ровно под строкой 1", first.Bottom, table.RowRect(2).Y);
            table.EnsureVisible(0, 0);
            Application.DoEvents();
        }

        // ==================================================================
        // помощники
        // ==================================================================

        static void Language(string name)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            Thread.CurrentThread.CurrentUICulture = culture;
            BecquerelMonitor.Properties.Resources.Culture = culture;
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
                Thread.Sleep(10);
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("поиск пиков не завершился за 60 с");
                }
            }
            while ((bool)busy.GetValue(panel) || (bool)pending.GetValue(panel));
            Application.DoEvents();
        }

        static DocEnergySpectrum OpenDocument(string path, string devicePath, NuclideDefinitionManager nuclides, double snr, double tol)
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

            string attached = ProbeDeviceConfig.Attach(rd);
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), attached);
            // ⚠ `ResultData.PeakDetectionMethodConfig` после чтения файла НЕ null —
            // там умолчания библиотеки; признак «прибора нет» — сам менеджер.
            bool deviceKnown = rd.DeviceConfigReference != null && rd.DeviceConfigReference.Guid != null
                               && DeviceConfigManager.GetInstance().DeviceConfigMap.ContainsKey(rd.DeviceConfigReference.Guid);
            if (!deviceKnown && devicePath != null)
            {
                // Каталог проб несёт поставочную библиотеку, но НЕ приборы
                // корпуса (их кладёт `mk_appwd.ps1`, а в оснастке корпуса по
                // `AMBER19` нет библиотеки). Прибор корпуса читается из файла
                // ключа `--device=` и ставится спектру тем же правилом, что у
                // `ProbeDeviceConfig.Attach`; в менеджер приборов НЕ кладётся.
                var deviceSerializer = new XmlSerializer(typeof(DeviceConfigInfo));
                DeviceConfigInfo device;
                using (var stream = new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    device = (DeviceConfigInfo)deviceSerializer.Deserialize(stream);
                }
                if (rd.DeviceConfigReference != null && !string.Equals(device.Guid, rd.DeviceConfigReference.Guid, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("GUID прибора из --device= (" + device.Guid + ") не совпадает со ссылкой спектра (" + rd.DeviceConfigReference.Guid + ")");
                }
                rd.DeviceConfig = device;
                rd.PeakDetectionMethodConfig = FWHMPeakDetectionMethodConfig.AdoptFrom(
                    device.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig,
                    rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig);
                Console.WriteLine("SETUP\tприбор взят из файла --device=: «{0}» ({1})", device.Name, Path.GetFileName(devicePath));
            }
            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                // Порог и допуск — как на экране Amber (ключи --snr=/--tol=).
                cfg.Min_SNR = snr;
                cfg.Tolerance = tol;
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (rd.FwhmCalibration == null && cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }
            else
            {
                throw new InvalidOperationException("у спектра нет FWHMPeakDetectionMethodConfig — прибор корпуса не подхватился");
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

        static MethodInfo Method(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod(name, Any | BindingFlags.DeclaredOnly);
                if (m != null) return m;
            }
            throw new InvalidOperationException("нет метода " + name + " у " + type.Name);
        }

        static string F(double v, int digits)
        {
            return v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-78} {2}{3}", ok ? "ok  " : "НЕТ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }

        static void Denies(string what, bool passedButShouldNot)
        {
            Console.WriteLine("  {0} {1,-78} {2}", passedButShouldNot ? "НЕТ " : "ok  ", what,
                              passedButShouldNot ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (passedButShouldNot) bad++;
        }
    }
}
