// Мерка полосы П1 захода 10.09.2026 по строкам `E39` и `E40`.
//
// ⛔ ЗАЧЕМ СВОЙ ФАЙЛ, А НЕ ПРАВКА `EditorShot`. Каталог
// `tools/effmaker/probes/**` в этом заходе занят другими полосами, править его
// нельзя. `EditorShot --check` при этом меряет ДРУГОЕ: пересечения подписей и
// выход за поле. Ни двойного числа (`E39`), ни прижатого (`E40`) он не видит —
// оба случая для него ЗЕЛЁНЫЕ, потому что подписи формально не пересеклись.
// Ровно поэтому `E39` и `E40` и были найдены глазами, а не приёмкой.
//
//   sketchp1probe            — все плечи, код 0, если все сошлись
//   sketchp1probe --verbose  — ещё и перечень всех подписей каждого плеча
//
// Коды возврата: 0 — все плечи сошлись; 1 — хоть одно разошлось; 2 — отказ
// оснастки (не построилась сцена, не нашлось свойство).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using SketchMode = BecquerelMonitor.EfficiencyMaker.GeometrySketch.SketchMode;

namespace BecquerelMonitor.Probes
{
    static class SketchP1Probe
    {
        /// <summary>
        /// Колонка редактора геометрий на форме, ужатой в свой `MinimumSize`, —
        /// самое тесное место, где чертёж живёт в приложении. Ровно в ней
        /// `E39` и читалось как одно число из одиннадцати знаков.
        /// </summary>
        static readonly Size Narrow = new Size(232, 440);

        /// <summary>Просторное поле — тот же чертёж, когда окно растянуто.</summary>
        static readonly Size Roomy = new Size(420, 460);

        const double Energy = 3000.0;

        static bool verbose;

        static int Main(string[] args)
        {
            foreach (string a in args)
            {
                if (a == "--verbose") verbose = true;
            }

            try
            {
                bool ok = true;
                Console.WriteLine("=== E39: одно число, напечатанное дважды ===");
                ok &= E39();
                Console.WriteLine();
                Console.WriteLine("=== E40: размерное число без свободного места ===");
                ok &= E40();
                Console.WriteLine();
                Console.WriteLine(ok ? "ИТОГ: все плечи сошлись" : "ИТОГ: РАЗОШЛОСЬ");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex.Message);
                return 2;
            }
        }

        // ------------------------------------------------------------------
        // E39
        // ------------------------------------------------------------------

        static bool E39()
        {
            bool ok = true;

            // --- сцены, ради которых строка заведена -----------------------
            //
            // ⚠ Мерка ДВОЙНАЯ нарочно. Мало показать, что на чертеже одно
            // число: если бы сцена просто перестала заполнять поле, чертёж дал
            // бы тот же ноль — а вместе с ним ушли бы проверка «проба выше
            // сосуда» и запись `SC_BeakerHeight` в `.in`. Поэтому рядом с
            // числом подписей стоит число в МОДЕЛИ: оно обязано остаться
            // прежним и равным высоте пробы.
            GeometryModel ground = Scene("ground");
            ok &= Arm("сцена «на земле», узкое поле", ground, SketchMode.Source, Narrow, 0);
            ok &= Arm("сцена «на земле», просторное поле", ground, SketchMode.Source, Roomy, 0);
            ok &= Model("сцена «на земле»", ground.SourceHeight, ground.BeakerHeight);

            GeometryModel hole = Scene("borehole");
            ok &= Arm("сцена «в лунке», узкое поле", hole, SketchMode.Source, Narrow, 0);
            ok &= Arm("сцена «в лунке», просторное поле", hole, SketchMode.Source, Roomy, 0);
            ok &= Model("сцена «в лунке»", hole.MarinelliSourceHeight, hole.MarinelliBeakerHeight);

            // --- ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ -----------------------------------
            //
            // ⛔ Без него плечи выше не меряют НИЧЕГО: проба, которая всегда
            // видит ноль пар, дала бы ноль и на неправленом дереве. Здесь вход
            // заведомо плохой для правила «совпало — не печатаем»: у сосуда
            // ЕСТЬ стенки, а высоту, равную пробе, человек ввёл руками. Обе
            // подписи обязаны стоять, то есть пара обязана НАЙТИСЬ — и если
            // она не нашлась, значит правка задушила случай, который душить
            // было нельзя, и проба это скажет.
            //
            // ⚠ Стенки РАЗНЫЕ (1.5 и 2.0) нарочно. С двумя равными проба
            // насчитала 2 пары вместо одной: «1.5 | 1.5» — своя собственная
            // пара, к `BeakerHeight` отношения не имеющая. Контроль, ожидание
            // которого приходится подгонять под посторонний шум, меряет уже не
            // то, ради чего заведён.
            GeometryModel handmade = Scene("ground");
            handmade.BeakerSideWallThickness = 1.5;
            handmade.BeakerEndWallThickness = 2.0;
            ok &= Arm("КОНТРОЛЬ: сосуд со стенками, высоты равны руками",
                      handmade, SketchMode.Source, Roomy, 1);

            return ok;
        }

        /// <summary>Плечо `E39`: сколько пар одинаковых чисел на чертеже.</summary>
        static bool Arm(string name, GeometryModel m, SketchMode mode, Size size, int expect)
        {
            List<string> texts;
            int pairs = Draw(m, mode, size, out texts);
            bool ok = pairs == expect;
            Console.WriteLine("  {0,-52} пар одинаковых чисел {1}, ждали {2} — {3}",
                              name + " " + size.Width + "x" + size.Height,
                              pairs, expect, ok ? "ok" : "РАЗОШЛОСЬ");
            if (verbose || !ok)
            {
                Console.WriteLine("      подписи: " + string.Join(" | ", texts.ToArray()));
            }

            return ok;
        }

        /// <summary>
        /// Плечо «модель не тронута»: поле сосуда по-прежнему заполнено и
        /// по-прежнему равно высоте пробы. Снята ПЕЧАТЬ, а не значение.
        /// </summary>
        static bool Model(string name, double sample, double vessel)
        {
            bool ok = vessel > 0.0 && Math.Abs(vessel - sample) < 1e-9;
            Console.WriteLine("  {0,-52} в модели проба {1} и сосуд {2} — {3}",
                              name + ": поле цело",
                              sample.ToString("G6", CultureInfo.InvariantCulture),
                              vessel.ToString("G6", CultureInfo.InvariantCulture),
                              ok ? "ok" : "РАЗОШЛОСЬ");
            return ok;
        }

        // ------------------------------------------------------------------
        // E40
        // ------------------------------------------------------------------

        static bool E40()
        {
            bool ok = true;
            GeometryModel ground = Scene("ground");
            GeometryModel hole = Scene("borehole");

            // Штатные поля: прижатых быть не должно — иначе чертёж в
            // приложении уже сейчас нечитаем, и это отдельная беда.
            ok &= Crowd("сцена «на земле», просторное поле", ground, SketchMode.Source, Roomy, 0, 0);
            ok &= Crowd("сцена «в лунке», просторное поле", hole, SketchMode.Source, Roomy, 0, 0);

            // --- ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ -----------------------------------
            //
            // ⛔ Заведомо плохой вход: поле, куда 40+ мест по ГОСТ не влезают.
            // Счётчик ОБЯЗАН стать положительным. Не стал — значит он не
            // считает, и нули выше ничего не стоят: ровно так признак отказа и
            // остаётся без читателя, а это и есть `E40`.
            //
            // ⛔ ШИРИНА 170, А НЕ 70. Первым заходом здесь стояло 70x70 —
            // и контроль показал НОЛЬ прижатых, то есть «прошёл» на неправленом
            // счётчике. Причина: в режиме `Source` поля по бокам занимают
            // `MarginX = 78` каждое, при ширине 70 масштаб выходит
            // отрицательным, `OnPaint` уходит ранним `return` — и чертежа НЕТ
            // ВОВСЕ. Ноль подписей давал ноль прижатых, и пустота читалась как
            // благополучие. Ровно поэтому ниже проверяется ещё и что подписи
            // вообще напечатались.
            //
            // ⚠ Вход ищется ПЕРЕБОРОМ, а не назначается. Ровно один тесный
            // размер уже подвёл (170x110 в режиме `Source`: пять подписей, все
            // встали свободно — сорока мест по ГОСТ хватает надолго). Контроль
            // требует, чтобы счётчик сработал ХОТЬ НА ОДНОМ входе из списка, и
            // печатает весь список: так видно, что мерили, а не только чем
            // кончилось.
            ok &= CrowdAny("КОНТРОЛЬ: где-то счётчик обязан сработать", new[]
            {
                // ⚠ Режим `Overview` в кандидаты НЕ ВЗЯТ: он печатает табличку
                // текста напрямую `Print`, минуя `Place`, — размерных чисел
                // там нет вовсе, и ноль прижатых у него означал бы только
                // «этот режим тут ни при чём». Первым заходом четыре размера
                // `Overview` стояли в списке и давали честные нули, ничего не
                // меря.
                new Tight { M = Dense(), Mode = SketchMode.Source,   Size = new Size(232, 440) },
                new Tight { M = Dense(), Mode = SketchMode.Source,   Size = new Size(232, 260) },
                new Tight { M = Dense(), Mode = SketchMode.Source,   Size = new Size(200, 180) },
                new Tight { M = Dense(), Mode = SketchMode.Source,   Size = new Size(180, 130) },
                new Tight { M = hole,    Mode = SketchMode.Detector, Size = new Size(170, 110) },
                new Tight { M = ground,  Mode = SketchMode.Detector, Size = new Size(170, 110) },
            });

            // Второй контроль, с другой стороны: счётчик обязан ВЕРНУТЬСЯ в
            // ноль на том же чертеже, стоит дать ему место. Счётчик, который
            // только растёт, показывал бы беду там, где её нет.
            ok &= Crowd("КОНТРОЛЬ: она же в поле 420x460 — счётчик сброшен",
                        hole, SketchMode.Source, Roomy, 0, 0);

            return ok;
        }

        struct Tight
        {
            public GeometryModel M;
            public SketchMode Mode;
            public Size Size;
        }

        /// <summary>
        /// Положительный контроль `E40`: счётчик обязан стать положительным
        /// хотя бы на одном входе из списка. Печатается весь список — вход,
        /// на котором чертёж не нарисовался вовсе, помечен отдельно и в зачёт
        /// не идёт.
        /// </summary>
        static bool CrowdAny(string name, Tight[] cases)
        {
            bool any = false;
            Console.WriteLine("  " + name + ":");
            foreach (Tight t in cases)
            {
                List<string> texts;
                Draw(t.M, t.Mode, t.Size, out texts);
                int n = LastCrowded;
                bool drawn = texts.Count > 0;
                if (drawn && n > 0)
                {
                    any = true;
                }

                Console.WriteLine("      {0,-10} {1,3}x{2,-3} — подписей {3,2}, прижатых {4,2}{5}",
                                  t.Mode, t.Size.Width, t.Size.Height, texts.Count, n,
                                  drawn ? string.Empty : "   (чертёж не нарисован — не в зачёт)");
            }

            Console.WriteLine("      итог контроля: счётчик {0} — {1}",
                              any ? "сработал" : "НЕ СРАБОТАЛ НИ РАЗУ",
                              any ? "ok" : "РАЗОШЛОСЬ");
            return any;
        }

        static bool Crowd(string name, GeometryModel m, SketchMode mode, Size size,
                          int lo, int hi)
        {
            List<string> texts;
            Draw(m, mode, size, out texts);
            int n = LastCrowded;
            // ⛔ ЧЕРТЁЖ ОБЯЗАН БЫТЬ НАРИСОВАН. Ноль прижатых на пустом чертеже
            // — не «всё влезло», а «ничего не печатали»: ровно на этом первый
            // заход контроля прошёл вхолостую (см. выше про 70x70).
            bool drawn = texts.Count > 0;
            bool ok = drawn && n >= lo && n <= hi;
            Console.WriteLine("  {0,-52} прижатых чисел {1} из {2} подписей, ждали {3} — {4}",
                              name + " " + size.Width + "x" + size.Height,
                              n, texts.Count,
                              hi == int.MaxValue ? ">= " + lo : (lo == hi ? lo.ToString() : lo + ".." + hi),
                              ok ? "ok" : (drawn ? "РАЗОШЛОСЬ" : "РАЗОШЛОСЬ (чертёж не нарисован)"));
            return ok;
        }

        // ------------------------------------------------------------------
        // Оснастка
        // ------------------------------------------------------------------

        static int LastCrowded;

        /// <summary>
        /// Стакан Маринелли, у которого ненулевы ВСЕ десять размеров, и все
        /// числа длинные. Это не сцена: сцены гасят половину полей нулями, а
        /// `DimH`/`DimV` нулевой размер не подписывают вовсе — оттого у
        /// «в лунке» на чертеже всего пять чисел, и места им хватает всегда.
        ///
        /// ⛔ Ради ЭТОГО случая `E40` и заведена: у геометрии, введённой
        /// человеком в поля редактора, читателя прижатых чисел не было —
        /// `EditorShot --check` меряет 14 заготовленных сцен, а таких среди них
        /// нет.
        /// </summary>
        static GeometryModel Dense()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            g.SourceType = GeometrySourceType.Marinelli;
            g.MarinelliBeakerDiameter = 128.75;
            g.MarinelliHoleDiameter = 82.45;
            g.MarinelliSourceHeight = 104.35;
            g.MarinelliHoleHeight = 76.85;
            g.MarinelliBeakerHeight = 112.65;
            g.MarinelliSideThickness = 2.35;
            g.MarinelliEndWallThickness = 3.45;
            g.MarinelliHoleSideThickness = 1.85;
            g.MarinelliHoleEndWallThickness = 2.75;
            g.MarinelliToDetectorDistance = 12.55;
            return g;
        }

        static GeometryModel Scene(string kind)
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            if (kind == "ground") GeometryScenes.Ground(g, Energy);
            else GeometryScenes.Borehole(g, Energy);
            return g;
        }

        /// <summary>
        /// Построить чертёж без окна — тем же способом, что `EditorShot`:
        /// `OnPaint` защищён, зовём его так же, как это делает WinForms.
        /// Возвращает число ПАР подписей с совпадающим текстом; заодно
        /// запоминает счётчик прижатых.
        /// </summary>
        static int Draw(GeometryModel model, SketchMode mode, Size size, out List<string> texts)
        {
            using (GeometrySketch sketch = new GeometrySketch { Mode = mode })
            using (Bitmap bmp = new Bitmap(size.Width, size.Height))
            {
                sketch.Bounds = new Rectangle(Point.Empty, size);
                sketch.SetModel(model);
                MethodInfo paint = typeof(GeometrySketch).GetMethod(
                    "OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                if (paint == null)
                {
                    throw new InvalidOperationException("не нашёлся GeometrySketch.OnPaint");
                }

                using (Graphics g = Graphics.FromImage(bmp))
                using (PaintEventArgs e = new PaintEventArgs(g, new Rectangle(Point.Empty, size)))
                {
                    paint.Invoke(sketch, new object[] { e });
                }

                LastCrowded = sketch.CrowdedLabels;

                texts = new List<string>();
                foreach (GeometrySketch.SketchLabel l in sketch.Labels)
                {
                    texts.Add(l.Text);
                }

                // Пара — это два размерных числа с ОДИНАКОВЫМ текстом. Именно
                // так `E39` и выглядит для человека: «638.6» и «638.6» в трёх
                // точках друг от друга читаются как одно длинное число.
                int pairs = 0;
                for (int i = 0; i < texts.Count; i++)
                {
                    for (int j = i + 1; j < texts.Count; j++)
                    {
                        if (string.Equals(texts[i], texts[j], StringComparison.Ordinal))
                        {
                            pairs++;
                        }
                    }
                }

                return pairs;
            }
        }
    }
}
