using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace EditorShot
{
    /// <summary>
    /// Снимок ВКЛАДКИ ИСТОЧНИКА редактора геометрии в PNG — с уже наложенной
    /// готовой сценой (E27) — и ЧИСЛОВАЯ ПРИЁМКА ЧЕРТЕЖА (E28).
    ///
    /// Зачем снимок. `SketchShot` снимает чертёж, а не разметку, а править
    /// пришлось именно разметку: строка готовой сцены сдвинула поля источника на
    /// 64 точки вниз, и вещества пробы у маринелли оказались близко к нижнему
    /// краю. Обрезанный родителем контрол не пропадает и не падает — он просто
    /// не виден, и заметить это можно только глазами. Форма берётся РОВНО в
    /// свой MinimumSize: если помещается здесь, поместится везде.
    ///
    /// Зачем приёмка. Этой же пробой 16.08.2026 найден `E28`: на сцене «в
    /// лунке» подписи размеров печатались друг на друге («51.9» и «547.4» в
    /// одной точке). Смотреть на снимок глазами — проверка, которая уже один раз
    /// пропустила дефект; поэтому ключ `--check` собирает прямоугольники ВСЕХ
    /// подписей (<see cref="GeometrySketch.Labels"/>) и считает две величины:
    /// сколько пар налезло друг на друга и сколько подписей ушло за поле. Обе
    /// обязаны быть НУЛЁМ.
    ///
    /// ⛔ Приёмка, которая проходит всегда, не мерит ничего. Плечо
    /// `--only=extreme` подсовывает заведомо плохой вход — деталь в 10⁻⁴ от
    /// габарита, — а прогон этой же пробы на СТАРОМ коде отрисовки обязан дать
    /// не ноль.
    ///
    /// ⚠ Окна чертёж не требует: он строится прямым вызовом `OnPaint` в
    /// `Bitmap`, как в `SketchShot`. Молчащая проба — это `MessageBox` за
    /// кадром.
    ///
    ///   editorshot &lt;куда.png&gt; [--scene=1|2] [--energy=3000]
    ///     --scene=1 — «Детектор на земле», =2 — «Детектор в лунке», 0 — без сцены
    ///
    ///   editorshot --check [--dir=&lt;куда класть PNG&gt;] [--tag=before|after]
    ///                      [--only=&lt;имя сцены&gt;] [--expect=ok|fail]
    ///
    /// Коды возврата: 0 — сошлось с ожиданием; 1 — не сошлось; 2 — плохие ключи.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 1)
            {
                Console.Error.WriteLine("editorshot <куда.png> [--scene=1|2] [--energy=3000]");
                Console.Error.WriteLine("editorshot --check [--dir=] [--tag=] [--only=] [--expect=ok|fail]");
                return 2;
            }

            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();

            bool check = false;
            string dir = ".", tag = "shot", only = null;
            bool expectFail = false;
            string outPath = null;
            int scene = 2;
            double energy = 3000.0;
            foreach (string a in args)
            {
                if (a == "--check") check = true;
                else if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
                else if (a.StartsWith("--tag=", StringComparison.Ordinal)) tag = a.Substring(6);
                else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
                else if (a.StartsWith("--expect=", StringComparison.Ordinal))
                {
                    // ⛔ `A77`, вторая волна 06.09.2026: РОВНО `ok` или `fail`,
                    // на прочее отказ с кодом. Прежде было `== "fail"`, то есть
                    // любая опечатка (`--expect=fial`, `--expect=Fail`) молча
                    // означала «жду успеха» — и ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ этой
                    // пробы переставал быть контролем, никак того не показывая.
                    // Тихо портится не число, а СМЫСЛ прогона; ровно этим и
                    // дорога `A77`.
                    string v = a.Substring(9);
                    if (v == "fail") expectFail = true;
                    else if (v == "ok") expectFail = false;
                    else
                    {
                        Console.Error.WriteLine("⛔ ключ --expect= понимает только ok и fail, а получил «"
                                                + v + "». Разбор строгий (`A77`).");
                        return 2;
                    }
                }
                else if (a.StartsWith("--scene=", StringComparison.Ordinal))
                    scene = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal))
                    energy = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (!a.StartsWith("--", StringComparison.Ordinal) && outPath == null) outPath = a;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (check)
            {
                return Check(dir, tag, only, expectFail, energy);
            }

            if (outPath == null)
            {
                Console.Error.WriteLine("не сказано, куда писать PNG");
                return 2;
            }

            return Shot(outPath, scene, energy);
        }

        // ==================================================================
        // Снимок разметки (прежняя работа пробы)
        // ==================================================================

        static int Shot(string outPath, int scene, double energy)
        {
            using (Form form = new Form())
            using (GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill })
            {
                // Тот же размер, что MinimumSize конструктора кривой, минус
                // место его собственных панелей — тесный случай, ради которого
                // проба и заведена.
                form.ClientSize = new Size(880, 470);
                form.Controls.Add(panel);
                form.Show();

                GeometryModel g = GeometryEditorPanel.Blank();
                GeometryPresets.Items[0].Apply(g);       // Nano 16 — любой, лишь бы обвязка была
                if (scene == 1)
                {
                    GeometryScenes.Ground(g, energy);
                }
                else if (scene == 2)
                {
                    GeometryScenes.Borehole(g, energy);
                }

                panel.SetSceneEnergy(energy);
                // Сцена въезжает МОДЕЛЬЮ, а не выбором в списке: так проверяется
                // и обратный ход — что панель узнаёт вид съёмки в сохранённой
                // геометрии и встаёт на нужную строку списка сама.
                panel.SetModel(g);

                TabControl tabs = FindTabs(panel);
                if (tabs == null)
                {
                    Console.Error.WriteLine("в панели нет вкладок — разметка изменилась");
                    return 1;
                }

                tabs.SelectedIndex = 1;                  // вкладка источника
                ComboBox types = FindSourceTypes(tabs.TabPages[1]);
                if (types == null)
                {
                    Console.Error.WriteLine("на вкладке источника нет списка типов");
                    return 1;
                }

                int want = scene == 1 ? 4 : scene == 2 ? 5 : 0;
                if (types.SelectedIndex != want)
                {
                    Console.Error.WriteLine("список типов встал на строку {0}, а ожидалась {1}",
                                            types.SelectedIndex, want);
                    return 1;
                }

                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    panel.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.ClientSize));
                    bmp.Save(outPath, ImageFormat.Png);
                }

                form.Hide();
            }

            Console.WriteLine("записано: {0}", outPath);
            return 0;
        }

        // ==================================================================
        // Приёмка чертежа (E28)
        // ==================================================================

        sealed class Case
        {
            public string Id;                            // латиницей — уходит в имя файла
            public string Name;                          // как называть в отчёте
            public GeometrySketch.SketchMode Mode;
            public Func<double, GeometryModel> Make;
            public Size[] Sizes;
        }

        /// <summary>
        /// Размеры чертежа, при которых он живёт в приложении. Первый — колонка
        /// редактора геометрий на форме, ужатой в свой `MinimumSize` (880x470
        /// минус 640 точек колонки полей): самый тесный случай, ради которого
        /// проба и заведена. Второй — просторный, как у `SketchShot`.
        /// </summary>
        static readonly Size[] EditorSizes = { new Size(232, 440), new Size(420, 460) };

        /// <summary>Миниатюра вкладки «Эффективность» в конфиге прибора.</summary>
        static readonly Size[] OverviewSizes = { new Size(300, 200), new Size(220, 120) };

        static GeometryModel Base()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);            // Nano 16 — обвязка на месте
            return g;
        }

        static List<Case> Cases()
        {
            List<Case> list = new List<Case>();

            // --- сцены в поле: ради них строка E28 и заведена ---------------
            list.Add(new Case
            {
                Id = "borehole_src", Name = "в лунке / источник",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e => { GeometryModel g = Base(); GeometryScenes.Borehole(g, e); return g; },
            });
            list.Add(new Case
            {
                Id = "borehole_det", Name = "в лунке / детектор",
                Mode = GeometrySketch.SketchMode.Detector, Sizes = EditorSizes,
                Make = e => { GeometryModel g = Base(); GeometryScenes.Borehole(g, e); return g; },
            });
            list.Add(new Case
            {
                Id = "ground_src", Name = "на земле / источник",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e => { GeometryModel g = Base(); GeometryScenes.Ground(g, e); return g; },
            });
            list.Add(new Case
            {
                Id = "ground_det", Name = "на земле / детектор",
                Mode = GeometrySketch.SketchMode.Detector, Sizes = EditorSizes,
                Make = e => { GeometryModel g = Base(); GeometryScenes.Ground(g, e); return g; },
            });

            // --- обычные сцены: было 0, обязано остаться 0 -------------------
            list.Add(new Case
            {
                Id = "marinelli", Name = "маринелли",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.SourceType = GeometrySourceType.Marinelli;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "point", Name = "точечный источник",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.SourceType = GeometrySourceType.Point;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "cylinder", Name = "банка",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.SourceType = GeometrySourceType.Cylinder;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "box", Name = "кювета",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.SourceType = GeometrySourceType.Box;
                    g.BoxSourceX = 60.0;
                    g.BoxSourceY = 40.0;
                    g.BoxSourceHeight = 30.0;
                    g.BoxToDetectorDistance = 5.0;
                    g.BoxSideWallThickness = 1.0;
                    g.BoxEndWallThickness = 1.0;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "detector", Name = "детектор с обвязкой",
                Mode = GeometrySketch.SketchMode.Detector, Sizes = EditorSizes,
                Make = e => Base(),
            });
            list.Add(new Case
            {
                Id = "bare", Name = "голый кристалл",
                Mode = GeometrySketch.SketchMode.Detector, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.FrontReflectorThickness = 0.0;
                    g.SideReflectorThickness = 0.0;
                    g.FrontCladdingThickness = 0.0;
                    g.SideCladdingThickness = 0.0;
                    g.MountingThickness = 0.0;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "boxcrystal", Name = "брусковый кристалл",
                Mode = GeometrySketch.SketchMode.Detector, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.Shape = CrystalShape.Box;
                    g.CrystalBoxX = 15.0;
                    g.CrystalBoxY = 18.0;
                    g.CrystalBoxZ = 60.0;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "overview", Name = "миниатюра",
                Mode = GeometrySketch.SketchMode.Overview, Sizes = OverviewSizes,
                Make = e => Base(),
            });

            // --- заведомо плохой вход: деталь в 10⁻⁴ от габарита -------------
            // Ни в одном поле такого нет и не ожидается. Смысл плеча один:
            // показать, ЧТО делает приёмка на входе хуже реального.
            list.Add(new Case
            {
                Id = "extreme", Name = "крайняя: деталь 1e-4 габарита",
                Mode = GeometrySketch.SketchMode.Source, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.SourceType = GeometrySourceType.Marinelli;
                    g.MarinelliBeakerDiameter = 100000.0;
                    g.MarinelliBeakerHeight = 100000.0;
                    g.MarinelliSourceHeight = 99000.0;
                    g.MarinelliHoleDiameter = 10.0;      // 1e-4 от габарита
                    g.MarinelliHoleHeight = 50000.0;
                    g.MarinelliSideThickness = 8.0;
                    g.MarinelliEndWallThickness = 6.0;
                    g.MarinelliHoleSideThickness = 4.0;
                    g.MarinelliHoleEndWallThickness = 3.0;
                    g.MarinelliToDetectorDistance = 12.0;
                    return g;
                },
            });
            list.Add(new Case
            {
                Id = "extreme_det", Name = "крайняя: обвязка 1e-4 кристалла",
                Mode = GeometrySketch.SketchMode.Detector, Sizes = EditorSizes,
                Make = e =>
                {
                    GeometryModel g = Base();
                    g.Shape = CrystalShape.Cylinder;
                    g.CrystalDiameter = 100000.0;
                    g.CrystalHeight = 100000.0;
                    g.FrontReflectorThickness = 10.0;    // 1e-4 от габарита
                    g.SideReflectorThickness = 10.0;
                    g.FrontCladdingThickness = 8.0;
                    g.SideCladdingThickness = 8.0;
                    g.MountingThickness = 6.0;
                    return g;
                },
            });

            return list;
        }

        /// <summary>
        /// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОЙ МЕРКИ, до всякого чертежа.
        ///
        /// Три счётчика приёмки на исправленном чертеже дают ноль — и ноль они
        /// дадут и в том случае, если считать разучились. Поэтому перед каждым
        /// прогоном мерке подсовывается заведомо плохой набор: одна пара
        /// наложена, одна стоит впритык, одна подпись за полем. Каждый счётчик
        /// ОБЯЗАН показать ровно единицу; не показал — числа ниже недействительны.
        /// </summary>
        static bool SelfTest()
        {
            Size field = new Size(200, 200);
            GeometrySketch.SketchLabel[] bad =
            {
                Fake("наложение А", "A", 10f, 10f),
                Fake("наложение Б", "Б", 30f, 12f),   // задевает «А»
                Fake("впритык В", "В", 10f, 40f),
                Fake("впритык Г", "Г", 51f, 40f),     // одна точка зазора до «В»
                Fake("за полем Д", "Д", 180f, 190f),  // не помещается в 200x200
            };

            List<string> hits = new List<string>();
            int pairs = Overlaps(bad, hits), tight = Tight(bad, hits), outside = Outside(bad, field, hits);
            bool ok = pairs == 1 && tight == 1 && outside == 1;
            Console.WriteLine("самопроверка мерки: пересечений {0}, впритык {1}, за полем {2} — {3}",
                              pairs, tight, outside, ok ? "мерка живая" : "МЕРКА СЛЕПА");
            return ok;
        }

        static GeometrySketch.SketchLabel Fake(string text, string key, float x, float y)
        {
            return new GeometrySketch.SketchLabel
            {
                Bounds = new RectangleF(x, y, 40f, 14f),
                Text = text,
                Key = key,
            };
        }

        static int Check(string dir, string tag, string only, bool expectFail, double energy)
        {
            if (!SelfTest())
            {
                Console.Error.WriteLine("мерка не сошлась на заведомо плохом входе — прогон бессмыслен");
                return 1;
            }

            Directory.CreateDirectory(dir);
            Console.WriteLine("шрифт чертежа: {0} {1:0.##} pt", Control.DefaultFont.Name,
                              Control.DefaultFont.SizeInPoints);
            Console.WriteLine();
            Console.WriteLine("{0,-32} {1,-9} {2,7} {3,11} {4,8} {5,9}",
                              "сцена", "поле", "подписей", "пересечений", "впритык", "за полем");
            Console.WriteLine(new string('-', 83));

            int totalPairs = 0, totalTight = 0, totalOut = 0, rows = 0;
            List<string> notes = new List<string>();
            foreach (Case c in Cases())
            {
                if (only != null && c.Id != only)
                {
                    continue;
                }

                foreach (Size size in c.Sizes)
                {
                    string png = Path.Combine(dir, string.Format(CultureInfo.InvariantCulture,
                        "{0}_{1}_{2}x{3}.png", tag, c.Id, size.Width, size.Height));
                    GeometrySketch.SketchLabel[] labels;
                    try
                    {
                        labels = Draw(c.Make(energy), c.Mode, size, png);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("{0,-32} {1,-9} ОТКАЗ: {2}", c.Name,
                                          size.Width + "x" + size.Height, ex.Message);
                        return 1;
                    }

                    List<string> hits = new List<string>();
                    int pairs = Overlaps(labels, hits);
                    int tight = Tight(labels, hits);
                    int outside = Outside(labels, size, hits);
                    Console.WriteLine("{0,-32} {1,-9} {2,7} {3,11} {4,8} {5,9}", c.Name,
                                      size.Width + "x" + size.Height, labels.Length,
                                      pairs, tight, outside);
                    foreach (string h in hits)
                    {
                        notes.Add(string.Format("    {0} {1}x{2}: {3}", c.Name,
                                                size.Width, size.Height, h));
                    }

                    totalPairs += pairs;
                    totalTight += tight;
                    totalOut += outside;
                    rows++;
                }
            }

            Console.WriteLine(new string('-', 83));
            Console.WriteLine("{0,-32} {1,-9} {2,7} {3,11} {4,8} {5,9}",
                              "ИТОГО (" + rows + " чертежей)", "", "",
                              totalPairs, totalTight, totalOut);
            if (notes.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("что именно налезло:");
                foreach (string n in notes)
                {
                    Console.WriteLine(n);
                }
            }

            Console.WriteLine();
            Console.WriteLine("снимки: {0}", Path.GetFullPath(dir));

            bool clean = totalPairs == 0 && totalTight == 0 && totalOut == 0;
            if (rows == 0)
            {
                Console.Error.WriteLine("НЕ НАШЛОСЬ НИ ОДНОГО ЧЕРТЕЖА — приёмка ничего не мерила");
                return 1;
            }

            if (expectFail)
            {
                Console.WriteLine(clean
                    ? "ПЛЕЧО НЕ СРАБОТАЛО: ждали беспорядка, получили ноль — приёмка ничего не мерит"
                    : "плечо сработало: беспорядок найден, как и ждали");
                return clean ? 1 : 0;
            }

            Console.WriteLine(clean
                ? "СОШЛОСЬ: ни одного пересечения, ни одной пары впритык, ни одного вылета"
                : "НЕ СОШЛОСЬ");
            return clean ? 0 : 1;
        }

        /// <summary>
        /// Построить чертёж в `Bitmap` без окна — тем же способом, что и
        /// `SketchShot`: `OnPaint` защищён, зовём его так же, как это делает
        /// сам WinForms.
        /// </summary>
        static GeometrySketch.SketchLabel[] Draw(GeometryModel model, GeometrySketch.SketchMode mode,
                                                 Size size, string png)
        {
            using (GeometrySketch sketch = new GeometrySketch { Mode = mode })
            using (Bitmap bmp = new Bitmap(size.Width, size.Height))
            {
                sketch.Bounds = new Rectangle(Point.Empty, size);
                sketch.SetModel(model);
                MethodInfo paint = typeof(GeometrySketch).GetMethod(
                    "OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                using (Graphics g = Graphics.FromImage(bmp))
                using (PaintEventArgs e = new PaintEventArgs(g, new Rectangle(Point.Empty, size)))
                {
                    paint.Invoke(sketch, new object[] { e });
                }

                if (png != null)
                {
                    bmp.Save(png, ImageFormat.Png);
                }

                return sketch.Labels;
            }
        }

        static int Overlaps(GeometrySketch.SketchLabel[] labels, List<string> hits)
        {
            int n = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                for (int j = i + 1; j < labels.Length; j++)
                {
                    if (!labels[i].Bounds.IntersectsWith(labels[j].Bounds))
                    {
                        continue;
                    }

                    n++;
                    hits.Add(string.Format(CultureInfo.InvariantCulture,
                        "«{0}» ({1}) налезло на «{2}» ({3})",
                        labels[i].Text, labels[i].Key ?? "-",
                        labels[j].Text, labels[j].Key ?? "-"));
                }
            }

            return n;
        }

        /// <summary>Зазор, который размерное число обязано иметь вокруг себя, точек.</summary>
        const float Gap = 2f;

        /// <summary>
        /// Пары РАЗМЕРНЫХ чисел, стоящих ближе <see cref="Gap"/> точек, но ещё
        /// не наложившихся.
        ///
        /// Зачем отдельно от пересечений. Вторая половина `E28` — «у сцены на
        /// земле два числа встык» — пересечением НЕ ЛОВИТСЯ: «638.6» и «638.6»
        /// разделены двумя точками, прямоугольники не задевают друг друга, а
        /// читается это как одно число из одиннадцати знаков. Приёмка, которая
        /// видит только наложение, назвала бы такой чертёж чистым.
        ///
        /// Строки таблички миниатюры (ключа у них нет) в счёт не идут нарочно:
        /// это связный абзац, строки которого стоят вплотную по замыслу.
        /// </summary>
        static int Tight(GeometrySketch.SketchLabel[] labels, List<string> hits)
        {
            int n = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                for (int j = i + 1; j < labels.Length; j++)
                {
                    if (labels[i].Key == null || labels[j].Key == null)
                    {
                        continue;
                    }

                    RectangleF grown = RectangleF.Inflate(labels[i].Bounds, Gap, Gap);
                    if (!grown.IntersectsWith(labels[j].Bounds)
                        || labels[i].Bounds.IntersectsWith(labels[j].Bounds))
                    {
                        continue;
                    }

                    n++;
                    hits.Add(string.Format(CultureInfo.InvariantCulture,
                        "«{0}» ({1}) стоит впритык к «{2}» ({3})",
                        labels[i].Text, labels[i].Key, labels[j].Text, labels[j].Key));
                }
            }

            return n;
        }

        static int Outside(GeometrySketch.SketchLabel[] labels, Size size, List<string> hits)
        {
            RectangleF field = new RectangleF(0f, 0f, size.Width, size.Height);
            int n = 0;
            foreach (GeometrySketch.SketchLabel l in labels)
            {
                if (field.Contains(l.Bounds))
                {
                    continue;
                }

                n++;
                hits.Add(string.Format(CultureInfo.InvariantCulture,
                    "«{0}» ({1}) вышло за поле: {2}", l.Text, l.Key ?? "-", l.Bounds));
            }

            return n;
        }

        static TabControl FindTabs(Control root)
        {
            foreach (Control c in root.Controls)
            {
                TabControl tabs = c as TabControl;
                if (tabs != null)
                {
                    return tabs;
                }

                tabs = FindTabs(c);
                if (tabs != null)
                {
                    return tabs;
                }
            }

            return null;
        }

        /// <summary>
        /// Список типов источника узнаётся по числу строк: их шесть — четыре
        /// формы и две съёмки в поле (E27). Списки веществ на той же вкладке
        /// длиной в библиотеку, спутать нельзя. Привязка к порядку контролов
        /// сломалась бы от любой правки разметки — ровно того, что проба и
        /// проверяет.
        /// </summary>
        const int SourceKindCount = 6;

        static ComboBox FindSourceTypes(Control root)
        {
            foreach (Control c in root.Controls)
            {
                ComboBox combo = c as ComboBox;
                if (combo != null && combo.Items.Count == SourceKindCount)
                {
                    return combo;
                }

                ComboBox found = FindSourceTypes(c);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
