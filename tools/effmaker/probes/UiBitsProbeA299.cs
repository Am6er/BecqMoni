using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace UiBitsProbeA299
{
    /// <summary>
    /// ПРИЁМКА `AMBER299` (05.11.2026) — мелочи интерфейса, пункты (1)…(4).
    ///
    ///     uibitsprobea299 --spectrum=&lt;файл корпуса&gt; [--shots=&lt;каталог&gt;]
    ///
    /// ⛔ ОКОН НЕТ: `MainForm` и `DCResultView` заводятся, но не показываются, описателей окон у
    /// таблицы нет. Полоса прокрутки XPTable судит о себе по `Control.Visible`, а он у непоказанной
    /// формы ложен, поэтому признак видимости формы результата ставится отражением
    /// (`Control.SetState`): это то, что сделал бы показ, минус сам показ.
    ///
    /// (1) Падежи `FSANotPossible` и `FSAFailed` — против литералов; английский текст не менялся.
    ///
    /// (2) Первая колонка окна результата. Строки — настоящий `ShowResult` на разборе из 40 строк
    /// (результат подложен в сеанс документа вместе с его отпечатком, счёта нет). Судится вылет:
    /// сумма колонок минус `CellDataRect.Width`.
    ///   а. 40 строк при разной ширине: полоса прокрутки есть, вылета нет, кроме случая, когда
    ///      первая колонка уже на нижней границе 90 px (тогда вылет законен и назван).
    ///   б. Сужение и расширение: вылета нет на узкой, на широкой колонка вернулась к прежней ширине.
    ///   в. Ручная ширина: сужение панели её не отнимает, расширение и новые строки — тоже.
    ///   г. Полоса появилась БЕЗ `ShowResult` и без `Resize` (строки добавлены в модель напрямую) —
    ///      это ветка строк, которым высоту считает отрисовка (`EnableWordWrap`).
    ///   д. Длинный заголовок (П274): колонка выросла и по уходу текста вернулась.
    /// Положительный контроль — та же проба на сборке HEAD без правки: строки а, б, в, г красные.
    ///
    /// (3) Панель канала, настоящий `ShowCursorValues` в `Bitmap`: высота и пустая полоса внизу при
    /// ненулевом фоне, нулевом фоне в канале, нулевом времени фона и без фона. Решение Amber 05.11.2026
    /// вопросником, дословно: «Постоянная высота, «—» (Рекомендую)»: при наличии фона высота 110 всегда;
    /// при нуле строки «Фоновых имп» 0.00, «Проба / фон, %» с прочерком и подсказка на месте, знака
    /// процента нет. На сборке с условной высотой нулевой фон даёт 78 вместо 110.
    ///
    /// (4) Подсказки `NucBase`: строка `IncludeDecayChainCheckBox` равна `AppUi.WrapTip` от текста
    /// ресурса; ни у одного контрола нет строки длиннее ширины блока — в обеих культурах и у обоих
    /// конструкторов.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; есть расхождение — код 1; ключи и файлы — код 2.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int VisibleFlag = 0x2;      // Control.STATE_VISIBLE
        const int FloorWidth = 90;        // DCResultView.FirstColumnRoom: нижняя граница первой колонки

        static int bad;
        static string shots;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectrum == null || !File.Exists(spectrum))
            {
                Console.Error.WriteLine("нужен --spectrum=<файл корпуса>");
                return 2;
            }

            Console.WriteLine("сборка приложения: {0}", typeof(EnergySpectrumView).Assembly.Location);
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            FsaTexts();
            ResultColumns(spectrum);
            ChannelPanel();
            NucBaseTips();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  (1) падежи
        // ------------------------------------------------------------------

        static void FsaTexts()
        {
            Console.WriteLine("=== (1) падежи FSANotPossible, FSAFailed ===");
            Same("ru FSANotPossible", "Полноспектральный разбор для этого спектра невозможен", Resource("FSANotPossible", "ru"));
            Same("ru FSAFailed", "Полноспектральный разбор не удался.", Resource("FSAFailed", "ru"));
            Same("en FSANotPossible", "Full-spectrum decomposition is not possible for this spectrum", Resource("FSANotPossible", "en"));
            Same("en FSAFailed", "Full-spectrum decomposition failed.", Resource("FSAFailed", "en"));
        }

        static string Resource(string key, string culture)
        {
            return BecquerelMonitor.Properties.Resources.ResourceManager.GetString(key, new CultureInfo(culture));
        }

        // ------------------------------------------------------------------
        //  (2) первая колонка окна результата
        // ------------------------------------------------------------------

        sealed class Scene : IDisposable
        {
            public DCResultView View;
            public Table Table;
            public ColumnModel Columns;
            public TableModel Model;

            public Scene(MainForm mainForm, DocEnergySpectrum doc, int width, int height)
            {
                // Окно заводится без документа, как при запуске приложения: конструктор сам зовёт
                // `ShowResult`, и сцена со строками, заполненными до размера, мерила бы не то.
                mainForm.ActiveDocument = null;
                View = new DCResultView(mainForm);
                mainForm.ActiveDocument = doc;
                Table = (Table)Field(typeof(DCResultView), "table1").GetValue(View);
                Columns = (ColumnModel)Field(typeof(DCResultView), "columnModel1").GetValue(View);
                Model = (TableModel)Field(typeof(DCResultView), "tableModel1").GetValue(View);
                typeof(Control).GetMethod("SetState", Any, null, new[] { typeof(int), typeof(bool) }, null)
                    .Invoke(View, new object[] { VisibleFlag, true });
                Size(width, height);
            }

            public void Size(int width, int height)
            {
                Table.Size = new Size(width, height);
                Table.PerformLayout();
            }

            public int First { get { return Columns.Columns[0].Width; } }

            public int Sum
            {
                get
                {
                    int sum = 0;
                    foreach (Column c in Columns.Columns) sum += c.Width;
                    return sum;
                }
            }

            public int Data { get { return Table.CellDataRect.Width; } }

            public int Overflow { get { return Sum - Data; } }

            public void Dispose()
            {
                View.Dispose();
            }

            public string Say()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "таблица {0} px, полоса {1}, строк {2}, колонки {3}+{4}, место {5}, вылет {6} px",
                    Table.Width, Table.VScroll ? "есть" : "нет", Model.Rows.Count, First, Sum - First, Data, Overflow);
            }
        }

        static void ResultColumns(string spectrum)
        {
            MainForm mainForm = new MainForm();
            DocEnergySpectrum doc = OpenDocument(spectrum);
            PlantResult(doc, 40);
            foreach (string culture in new[] { "ru", "en" })
            {
                Console.WriteLine();
                Console.WriteLine("=== (2) первая колонка окна результата, культура {0} ===", culture);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                ResultColumnsIn(mainForm, doc);
            }
            doc.Dispose();
            mainForm.Dispose();
        }

        static void ResultColumnsIn(MainForm mainForm, DocEnergySpectrum doc)
        {
            // а. сорок строк при разной ширине
            foreach (int width in new[] { 370, 400, 420, 438, 440, 453, 480, 640 })
            {
                using (var scene = new Scene(mainForm, doc, width, 200))
                {
                    scene.View.ShowResult(true);
                    Console.WriteLine("  а. {0}", scene.Say());
                    Same("а. строк 40", 40, scene.Model.Rows.Count);
                    Same("а. полоса прокрутки при 40 строках", true, scene.Table.VScroll);
                    if (scene.First > FloorWidth)
                    {
                        Same("а. при " + width.ToString(CultureInfo.InvariantCulture) + " px вылета нет", true, scene.Overflow <= 0);
                    }
                    else
                    {
                        Console.WriteLine("     первая колонка на нижней границе {0} px: вылет законен, есть горизонтальная прокрутка", FloorWidth);
                    }
                }
            }

            // б. сужение и расширение
            using (var scene = new Scene(mainForm, doc, 640, 200))
            {
                scene.View.ShowResult(true);
                int wide = scene.First;
                Console.WriteLine("  б. широкая: {0}", scene.Say());
                scene.Size(440, 200);
                Console.WriteLine("  б. узкая:   {0}", scene.Say());
                Same("б. на узкой вылета нет", true, scene.Overflow <= 0);
                scene.Size(640, 200);
                Console.WriteLine("  б. снова широкая: {0}", scene.Say());
                Same("б. расширение вернуло ширину", wide, scene.First);
            }

            // в. ручная ширина
            using (var scene = new Scene(mainForm, doc, 640, 200))
            {
                scene.View.ShowResult(true);
                const int hand = 300;
                scene.Columns.Columns[0].Width = hand;      // так её ставит XPTable по концу перетаскивания
                Console.WriteLine("  в. ручная {0} px: {1}", hand, scene.Say());
                scene.Size(420, 200);
                Console.WriteLine("  в. узкая:   {0}", scene.Say());
                Same("в. сужение ручную ширину не отнимает", hand, scene.First);
                scene.Size(640, 200);
                Console.WriteLine("  в. широкая: {0}", scene.Say());
                Same("в. расширение ручную ширину не меняет", hand, scene.First);
                scene.View.ShowResult(true);
                Console.WriteLine("  в. новые строки: {0}", scene.Say());
                Same("в. новые строки ручную ширину не меняют", hand, scene.First);
            }

            // г. полоса появилась без ShowResult и без Resize
            using (var scene = new Scene(mainForm, doc, 440, 200))
            {
                MethodInfo header = typeof(DCResultView).GetMethod("SetFsaHeader", Any, null, new[] { typeof(string), typeof(string) }, null); // (`fixFsa2`) перегрузка с запасным текстом шапки рядом
                header.Invoke(scene.View, new object[] { "FSA: Bq by response matrix", null });
                Console.WriteLine("  г. до строк: {0}", scene.Say());
                for (int i = 0; i < 40; i++)
                {
                    var row = new Row();
                    row.Cells.Add(new Cell("Nu-" + i.ToString(CultureInfo.InvariantCulture)));
                    row.Cells.Add(new Cell("1"));
                    row.Cells.Add(new Cell("2"));
                    row.Cells.Add(new Cell("3"));
                    scene.Model.Rows.Add(row);
                }
                scene.Table.PerformLayout();
                Console.WriteLine("  г. строки в модель: {0}", scene.Say());
                Same("г. полоса появилась", true, scene.Table.VScroll);
                Same("г. вылета нет", true, scene.Overflow <= 0);
            }

            // д. длинный заголовок и возврат (П274)
            using (var scene = new Scene(mainForm, doc, 900, 200))
            {
                MethodInfo header = typeof(DCResultView).GetMethod("SetFsaHeader", Any, null, new[] { typeof(string), typeof(string) }, null); // (`fixFsa2`) перегрузка с запасным текстом шапки рядом
                header.Invoke(scene.View, new object[] { "FSA", null });
                int plain = scene.First;
                string longText = "FSA: Bq by response matrix (recomputing…) " + new string('x', 40);
                header.Invoke(scene.View, new object[] { longText, null });
                int grown = scene.First;
                header.Invoke(scene.View, new object[] { "FSA", null });
                int back = scene.First;
                Console.WriteLine("  д. короткий {0} px, длинный {1} px, снова короткий {2} px", plain, grown, back);
                Same("д. под длинный заголовок колонка выросла", true, grown > plain);
                Same("д. по уходу заголовка вернулась", plain, back);
            }
        }

        /// <summary>
        /// Разбор из <paramref name="rows"/> строк — в сеанс документа вместе с его отпечатком:
        /// `EnsureUpToDate` видит «этот снимок уже посчитан», и счёта нет.
        /// </summary>
        static void PlantResult(DocEnergySpectrum doc, int rows)
        {
            var result = new FsaResult
            {
                ResponseMatrixUsed = true,
                EfficiencyUsed = true,
                LimitDecisionQuantile = 3.0,
                LimitDetectionQuantile = 1.6449,
            };
            for (int i = 0; i < rows; i++)
            {
                result.Components.Add(new FsaComponentResult
                {
                    Name = "Nu-" + (100 + i).ToString(CultureInfo.InvariantCulture),
                    Kind = FsaComponentKind.Single,
                    CountRate = 10.0 + i,
                    Z = 20.0,
                    DecisionThresholdRate = 1.0,
                    DetectionLimitRate = 2.0,
                });
            }
            ResultData rd = doc.ActiveResultData;
            FsaAnalysisSession session = doc.FsaSession;
            Field(typeof(FsaAnalysisSession), "result").SetValue(session, result);
            Field(typeof(FsaAnalysisSession), "stamp").SetValue(session,
                FsaAnalysisSession.BuildStamp(rd, rd.BackgroundEnergySpectrum != null, FsaCalculationOptions.Of(rd)));
        }

        static DocEnergySpectrum OpenDocument(string path)
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
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }

        // ------------------------------------------------------------------
        //  (3) панель канала: высота тем же условием, что отрисовка (`A195`)
        // ------------------------------------------------------------------

        const int PanelChannel = 100;

        static void ChannelPanel()
        {
            Console.WriteLine();
            Console.WriteLine("=== (3) панель канала: высота и пустая полоса ===");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru");
            //                 фон в канале, время фона, есть фон, высота
            PanelCase("фон ненулевой", 1000, 1000.0, true, 110);
            PanelCase("фон в канале нулевой", 0, 1000.0, true, 110);
            PanelCase("время фона нулевое", 1000, 0.0, true, 110);
            PanelCase("фона нет", 0, 0.0, false, 62);
        }

        static void PanelCase(string name, int bgCounts, double bgTime, bool hasBackground, int expected)
        {
            using (var view = new EnergySpectrumView())
            using (var frame = new Bitmap(500, 200, PixelFormat.Format32bppArgb))
            {
                int[] fgData = new int[1024];
                int[] bgData = new int[1024];
                fgData[PanelChannel] = 5000;
                bgData[PanelChannel] = bgCounts;
                var cal = new PolynomialEnergyCalibration();
                cal.PolynomialOrder = 1;
                cal.Coefficients = new double[] { 0.0, 1.0 };
                var fg = new EnergySpectrum
                {
                    NumberOfChannels = fgData.Length, Spectrum = fgData, EnergyCalibration = cal,
                    MeasurementTime = 1000.0, TotalPulseCount = 5000, ValidPulseCount = 5000,
                };
                var bg = new EnergySpectrum
                {
                    NumberOfChannels = bgData.Length, Spectrum = bgData, EnergyCalibration = cal,
                    MeasurementTime = bgTime, TotalPulseCount = bgCounts, ValidPulseCount = bgCounts,
                };
                var result = new ResultData { EnergySpectrum = fg, BackgroundEnergySpectrum = hasBackground ? bg : null, Visible = true };
                Action<string, object> set = (field, value) => Field(typeof(EnergySpectrumView), field).SetValue(view, value);
                view.Size = new Size(frame.Width, frame.Height);
                set("energySpectrum", fg);
                set("backgroundEnergySpectrum", hasBackground ? bg : null);
                set("activeResultData", result);
                set("energyCalibration", cal);
                set("baseEnergyCalibration", cal);
                set("backgroundEnergyCalibration", cal);
                set("backgroundNumberOfChannels", bgData.Length);
                set("left", 0);
                set("width", frame.Width);
                set("height", frame.Height);
                set("cursorX", -1);
                set("validCursor", true);
                set("cursorChannel", PanelChannel);
                set("selectionStart", -1);
                set("selectionEnd", -1);
                set("nuclideCursorPeakDirty", false);
                set("nuclideCursorPeak", null);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.Clear(Color.LightBlue);
                    typeof(EnergySpectrumView).GetMethod("ShowCursorValues", Any).Invoke(view, new object[] { g });
                }
                if (shots != null)
                {
                    Directory.CreateDirectory(shots);
                    frame.Save(Path.Combine(shots, "a299-channel-" + name.Replace(' ', '-') + ".png"), ImageFormat.Png);
                }

                Rectangle box = DarkGrayBox(frame);
                int gap = BottomGap(frame, box);
                Console.WriteLine("  {0}: панель {1}×{2}, пустая полоса внизу {3} px", name, box.Width, box.Height, gap);
                Same(name + ": высота панели", expected, box.Height);
                // Шрифт кончается на 3–8 px выше рамки: больше — значит, под строкой резерв без строки.
                Same(name + ": пустой полосы внизу нет", true, gap <= 8);

                if (hasBackground && (bgCounts == 0 || bgTime <= 0.0))
                {
                    int row = RowDifference(view, frame, box.X, 58, BecquerelMonitor.Properties.Resources.ChartHeaderBGCounts, "0.00");
                    Console.WriteLine("    строка «Фоновых имп» 0.00: отличий точек {0}", row);
                    Same(name + ": строка фона на месте", 0, row);
                    int ratio = RowDifference(view, frame, box.X, 74, BecquerelMonitor.Properties.Resources.ChartHeaderCountBGRatio, "\u2014");
                    Console.WriteLine("    строка «Проба / фон, %» с прочерком: отличий точек {0}", ratio);
                    Same(name + ": строка отношения с прочерком", 0, ratio);
                }
            }
        }

        /// <summary>Заливка панели цвета `DarkGray` — её ограничивающий прямоугольник и есть высота (образец G10).</summary>
        static Rectangle DarkGrayBox(Bitmap b)
        {
            int gray = Color.DarkGray.ToArgb();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < b.Height; y++)
            {
                for (int x = 0; x < b.Width; x++)
                {
                    if (b.GetPixel(x, y).ToArgb() != gray) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>Сколько белых рядов точек под последней строкой текста, до нижней рамки.</summary>
        static int BottomGap(Bitmap b, Rectangle box)
        {
            int white = Color.White.ToArgb();
            int bottom = box.Bottom - 5;     // белое кончается на 3 px выше тени, ряд рамки — ещё один
            int gap = 0;
            for (int y = bottom; y > box.Top; y--)
            {
                for (int x = box.Left + 4; x < box.Right - 8; x++)
                {
                    if (b.GetPixel(x, y).ToArgb() != white) return gap;
                }
                gap++;
            }
            return gap;
        }

        static int RowDifference(EnergySpectrumView view, Bitmap frame, int panelX, int row, string label, string value)
        {
            Rectangle text = new Rectangle(panelX + 5, 10 + row, EnergySpectrumView.CursorPanelWidth - 12, 32);
            using (Bitmap ideal = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(ideal))
            using (StringFormat far = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.Clear(Color.White);
                g.DrawString(label, view.Font, Brushes.Black, text);
                g.DrawString(value, view.Font, Brushes.Black, text, far);
                int differ = 0;
                for (int y = text.Top; y < text.Top + 16; y++)
                {
                    for (int x = text.Left; x < text.Right; x++)
                    {
                        if (ideal.GetPixel(x, y).ToArgb() != frame.GetPixel(x, y).ToArgb()) differ++;
                    }
                }
                return differ;
            }
        }

        // ------------------------------------------------------------------
        //  (4) подсказки NucBase
        // ------------------------------------------------------------------

        static void NucBaseTips()
        {
            Console.WriteLine();
            Console.WriteLine("=== (4) подсказки NucBase ===");
            int width = (int)typeof(AppUi).GetField("TipWidth", Any).GetValue(null);
            Same("ширина блока подсказок", 80, width);
            foreach (string culture in new[] { "ru", "en" })
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                var resources = new ComponentResourceManager(typeof(BecquerelMonitor.NucBase.NucBase));
                string raw = resources.GetString("IncludeDecayChainCheckBox.ToolTip", new CultureInfo(culture));
                Same(culture + ": исходный текст длиннее блока (есть что переносить)", true, raw != null && raw.Length > width);
                foreach (bool withOwner in new[] { false, true })
                {
                    using (BecquerelMonitor.NucBase.NucBase form = withOwner
                        ? new BecquerelMonitor.NucBase.NucBase((Form)null)
                        : new BecquerelMonitor.NucBase.NucBase())
                    {
                        var tip = (ToolTip)Field(typeof(BecquerelMonitor.NucBase.NucBase), "toolTip1").GetValue(form);
                        var box = (Control)Field(typeof(BecquerelMonitor.NucBase.NucBase), "IncludeDecayChainCheckBox").GetValue(form);
                        string text = tip.GetToolTip(box);
                        int tips = 0, longest = 0;
                        Sweep(tip, form, ref tips, ref longest);
                        string[] lines = text.Replace("\r\n", "\n").Split('\n');
                        Console.WriteLine("  {0}, конструктор {1}: IncludeDecayChainCheckBox {2} строк, длиннейшая {3}; подсказок {4}, длиннейшая строка {5}",
                                          culture, withOwner ? "(Form)" : "()", lines.Length,
                                          lines.Max(l => l.Length), tips, longest);
                        Same(culture + ": подсказка = AppUi.WrapTip(ресурс)", AppUi.WrapTip(raw), text);
                        Same(culture + ": ни одной строки длиннее блока", true, longest <= width);
                    }
                }
            }
        }

        static void Sweep(ToolTip tip, Control root, ref int tips, ref int longest)
        {
            string text = tip.GetToolTip(root);
            if (!string.IsNullOrEmpty(text))
            {
                tips++;
                foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Length > longest) longest = line.Length;
                }
            }
            foreach (Control child in root.Controls)
            {
                Sweep(tip, child, ref tips, ref longest);
            }
        }

        // ------------------------------------------------------------------
        //  мерка
        // ------------------------------------------------------------------

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Any);
                if (field != null) return field;
            }
            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static void Same(string what, object expected, object got)
        {
            if (Equals(expected, got))
            {
                Console.WriteLine("  ✓ {0}", what);
                return;
            }
            Console.WriteLine("  ⛔ {0}: ждали {1}, получили {2}", what, expected ?? "null", got ?? "null");
            bad++;
        }
    }
}
