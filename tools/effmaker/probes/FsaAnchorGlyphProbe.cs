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
using System.Windows.Forms;

namespace FsaAnchorGlyphProbe
{
    /// <summary>
    /// ПРИЁМКА `AMBER20` ЧИСЛОМ — полоса П14, 12.09.2026: метка принятой опоры
    /// привязки шкалы на графике FSA — ЯКОРЬ U+2693 из шрифта, а не треугольник.
    ///
    ///     fsaanchorglyphprobe [--shots=&lt;каталог кадров&gt;] [--sabotage=nofont]
    ///                         [--family=&lt;шрифт&gt;] [--height=14]
    ///
    /// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ: вид `EnergySpectrumView` собирается без
    /// формы, поля вьюпорта ставятся отражением ровно так, как это делает
    /// `FsaStackShot`, и метки рисует НАСТОЯЩИЙ `DrawFsaAnchors` на чёрный кадр,
    /// где кроме меток нет ничего, — всё, что не чёрное, и есть метка. Своего
    /// рисования у пробы нет нарочно.
    ///
    /// Что меряется, числом:
    ///
    ///   1. КОНТУР: `EnergySpectrumView.BuildFsaAnchorGlyph(family, 14)` —
    ///      `PointCount` &gt; 0 (положительный контроль: у цветного `Segoe UI Emoji`
    ///      контур пуст, у несуществующего семейства — null), высота чернил по
    ///      `GetBounds` распрямлённой копии = 14 px, низ на y = 0, центр по x на
    ///      x = 0 (нормировка, которой пользуется рисующий).
    ///
    ///   2. КАДР: три принятые опоры (300, 700, 900 кэВ) и одна отвергнутая
    ///      (500 кэВ, `Used = false`). У каждой принятой — ограничивающий
    ///      прямоугольник не-чёрных точек в окне ±12 px вокруг ожидаемого x:
    ///      центр по x = ожидаемому ±1 (тот же расчёт x, что у вида), низ =
    ///      `GetSpectrumValueY(верх стека) − 4` ±1 (белый ореол в 1 px под
    ///      сглаживанием даёт ±1), высота 14…17 px (14 чернил + ореол). Опора на
    ///      900 кэВ стоит у верхнего края — проверяется зажим: низ = 15, глиф
    ///      целиком в поле. Вне окон принятых опор не-чёрных точек НОЛЬ —
    ///      отвергнутая не нарисована, лишнего нет. Синих точек (цвет
    ///      `FsaPalette.AnchorMarkColor` ±60) — не меньше ПЯТОЙ ЧАСТИ не-чёрных:
    ///      первая редакция правки клала однопиксельную белую линию ПОВЕРХ
    ///      заливки, и у тонкого глифа синих оставалось 2 из 119 — метка читалась
    ///      белой; это и есть мерка, которая такое ловит.
    ///
    ///   3. ПОРЧА `--sabotage=nofont`: в поле `EnergySpectrumView.FsaAnchorGlyphFontFamily`
    ///      отражением кладётся несуществующее имя → контур null → ЗАПАСНОЙ ПУТЬ,
    ///      треугольник 10×9 (+кромка): проба ОБЯЗАНА опознать его по высоте
    ///      9…11 и ширине 10…12 и низу `y − 4` (зажим 10), иначе отказ. Без порчи
    ///      треугольник, наоборот, — отказ: значит, шрифта нет и правка не видна.
    ///
    /// Коды возврата: 0 — всё сошлось; 1 — расхождение (какое — построчно);
    /// 2 — ключи.
    /// </summary>
    static class Program
    {
        const int Width = 1000;
        const int Height = 400;
        const int Left = 1;
        const double FromKev = 0.0;
        const double ToKev = 1000.0;
        const int Channels = 1024;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string shots = null, sabotage = null, family = null;
            float heightPx = float.NaN;
            foreach (string a in args)
            {
                if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else if (a.StartsWith("--family=", StringComparison.Ordinal)) family = a.Substring(9);
                else if (a.StartsWith("--height=", StringComparison.Ordinal))
                    heightPx = float.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (sabotage != null && sabotage != "nofont")
            {
                Console.Error.WriteLine("--sabotage= принимает только nofont");
                return 2;
            }

            Type viewType = typeof(EnergySpectrumView);
            FieldInfo familyField = viewType.GetField("FsaAnchorGlyphFontFamily",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            FieldInfo heightField = viewType.GetField("FsaAnchorGlyphHeightPx",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo build = viewType.GetMethod("BuildFsaAnchorGlyph",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (familyField == null || heightField == null || build == null)
            {
                // Сборка БЕЗ правки AMBER20: сказать об этом словами, а не падать
                // NullReference, — эта проба и есть читатель признака.
                Console.WriteLine("СБОРКА БЕЗ ПРАВКИ AMBER20: нет EnergySpectrumView.BuildFsaAnchorGlyph / FsaAnchorGlyphFontFamily / FsaAnchorGlyphHeightPx");
                return 1;
            }

            string defaultFamily = (string)familyField.GetValue(null);
            float defaultHeight = (float)heightField.GetValue(null);
            if (family == null) family = defaultFamily;
            if (float.IsNaN(heightPx)) heightPx = defaultHeight;
            Console.WriteLine("SETUP\tсемейство={0}\tвысота={1}\tпорча={2}",
                              family, heightPx.ToString("F1", CultureInfo.InvariantCulture), sabotage ?? "нет");

            int bad = 0;

            // ---------- 1. КОНТУР ----------
            using (GraphicsPath glyph = (GraphicsPath)build.Invoke(null, new object[] { family, heightPx }))
            {
                if (glyph == null)
                {
                    Console.WriteLine("КОНТУР\t{0}\tnull — шрифта нет или контур пуст", family);
                    bad++;
                }
                else
                {
                    RectangleF b = FlatBounds(glyph);
                    Console.WriteLine("КОНТУР\t{0}\tPointCount={1}\tширина={2}\tвысота={3}\tниз={4}\tцентр_x={5}",
                                      family, glyph.PointCount,
                                      b.Width.ToString("F2", CultureInfo.InvariantCulture),
                                      b.Height.ToString("F2", CultureInfo.InvariantCulture),
                                      b.Bottom.ToString("F2", CultureInfo.InvariantCulture),
                                      (b.Left + b.Width / 2f).ToString("F2", CultureInfo.InvariantCulture));
                    if (glyph.PointCount <= 0) { Console.WriteLine("  ⛔ контур пуст"); bad++; }
                    if (Math.Abs(b.Height - heightPx) > 0.25f) { Console.WriteLine("  ⛔ высота чернил не равна заданной (допуск — шаг распрямления 0.25 px)"); bad++; }
                    if (Math.Abs(b.Bottom) > 0.05f) { Console.WriteLine("  ⛔ низ контура не на y=0"); bad++; }
                    if (Math.Abs(b.Left + b.Width / 2f) > 0.05f) { Console.WriteLine("  ⛔ центр контура не на x=0"); bad++; }
                }
            }

            // Положительные контроли самой функции: цветной шрифт и несуществующий.
            foreach (string control in new[] { "Segoe UI Emoji", "NoSuchFont-P14-Sabotage" })
            {
                using (GraphicsPath g2 = (GraphicsPath)build.Invoke(null, new object[] { control, heightPx }))
                {
                    Console.WriteLine("КОНТРОЛЬ\t{0}\t{1}", control,
                                      g2 == null ? "null (запасной путь)" : "PointCount=" + g2.PointCount.ToString(CultureInfo.InvariantCulture));
                    if (control.StartsWith("NoSuchFont", StringComparison.Ordinal) && g2 != null)
                    {
                        Console.WriteLine("  ⛔ несуществующее семейство дало контур — запасной путь недостижим");
                        bad++;
                    }
                }
            }

            // ---------- 2/3. КАДР ----------
            if (sabotage == "nofont")
            {
                familyField.SetValue(null, "NoSuchFont-P14-Sabotage");
            }
            else if (family != defaultFamily)
            {
                familyField.SetValue(null, family);
            }

            bool expectTriangle = sabotage == "nofont";
            var anchors = new List<Anchor>
            {
                new Anchor(300.0, 500.0, true),
                new Anchor(500.0, 300.0, false),   // отвергнутая — рисоваться НЕ должна
                new Anchor(700.0, 100.0, true),
                new Anchor(900.0, 990.0, true)     // у верхнего края — зажим
            };

            var calibration = new PolynomialEnergyCalibration(); // E = канал
            var spectrum = new EnergySpectrum(1.0, Channels);
            var result = new FsaResult();
            int used = 0;
            foreach (Anchor a in anchors)
            {
                result.ScaleAnchors.Add(new FsaScaleAnchor
                {
                    Component = "P14",
                    LineKev = a.Kev,
                    ModelKev = a.Kev,
                    MeasuredKev = a.Kev,
                    Used = a.Used
                });
                if (a.Used) used++;
            }
            result.ScaleAnchorsUsed = used;

            double[] modelTop = new double[Channels];
            foreach (Anchor a in anchors)
            {
                int ch = (int)Math.Round(calibration.EnergyToChannel(a.Kev, Channels));
                modelTop[ch] = a.Top;
            }

            const double valueRange = 1000.0;
            double pixelPerEnergy = (Width - Left) / (ToKev - FromKev);

            using (var view = new EnergySpectrumView())
            using (var image = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                Set(view, "energySpectrum", spectrum);
                Set(view, "energyCalibration", calibration);
                Set(view, "numberOfChannels", Channels);
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                Set(view, "verticalUnit", VerticalUnit.Counts);
                Set(view, "verticalScaleType", VerticalScaleType.LinearScale);
                Set(view, "height", Height);
                Set(view, "width", Width - Left);
                Set(view, "left", Left);
                Set(view, "scrollX", 0);
                Set(view, "scrollY", 0);
                Set(view, "scrollBaseY", 0.0);
                Set(view, "verticalScale", 1.0);
                Set(view, "horizontalScale", 1.0);
                Set(view, "totalMinValue", 0.0);
                Set(view, "valueRange", valueRange);
                Set(view, "energyViewOffset", FromKev);
                Set(view, "pixelPerEnergy", pixelPerEnergy);

                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    // Те же режимы, что ставит ShowFsaOverlay перед DrawFsaAnchors.
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    Invoke(view, "DrawFsaAnchors", g, result, modelTop);
                }

                if (shots != null)
                {
                    Directory.CreateDirectory(shots);
                    string file = Path.Combine(shots, expectTriangle ? "anchors_sabotage_nofont.png" : "anchors_glyph.png");
                    image.Save(file, ImageFormat.Png);
                    Console.WriteLine("кадр: " + file);
                }

                // Разбор кадра.
                var windows = new List<Rectangle>();
                foreach (Anchor a in anchors)
                {
                    if (!a.Used) continue;
                    int expectedX = (int)((a.Kev - FromKev) * pixelPerEnergy * 1.0) + 0 + Left;
                    int stackY = (int)Invoke(view, "GetSpectrumValueY", a.Top);
                    int expectedBottom = expectTriangle
                        ? Math.Max(10, stackY - 4)
                        : Math.Max((int)Math.Ceiling(heightPx) + 1, stackY - 4);
                    var win = new Rectangle(expectedX - 12, 0, 25, Height);
                    windows.Add(win);

                    int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, blue = 0, ink = 0;
                    for (int y = 0; y < Height; y++)
                    {
                        for (int x = Math.Max(0, win.Left); x < Math.Min(Width, win.Right); x++)
                        {
                            Color c = image.GetPixel(x, y);
                            if (c.R == 0 && c.G == 0 && c.B == 0) continue;
                            ink++;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            if (Near(c, FsaPalette.AnchorMarkColor, 60)) blue++;
                        }
                    }

                    if (ink == 0)
                    {
                        Console.WriteLine("ОПОРА\t{0} кэВ\tНЕ НАРИСОВАНА (в окне x={1}±12 нет ни одной точки)",
                                          a.Kev.ToString("F1", CultureInfo.InvariantCulture), expectedX);
                        bad++;
                        continue;
                    }

                    double centreX = (minX + maxX) / 2.0;
                    int h = maxY - minY + 1, w = maxX - minX + 1;
                    bool looksTriangle = h >= 9 && h <= 11 && w >= 10 && w <= 12;
                    bool looksGlyph = h >= (int)heightPx && h <= (int)heightPx + 3 && !looksTriangle;
                    string shape = looksTriangle ? "треугольник" : looksGlyph ? "якорь" : "непонятно";
                    Console.WriteLine("ОПОРА\t{0} кэВ\tx_ожид={1}\tx_центр={2}\tниз_ожид={3}\tниз={4}\tверх={5}\tвысота={6}\tширина={7}\tсиних={8}\tточек={9}\tформа={10}",
                                      a.Kev.ToString("F1", CultureInfo.InvariantCulture), expectedX,
                                      centreX.ToString("F1", CultureInfo.InvariantCulture),
                                      expectedBottom, maxY, minY, h, w, blue, ink, shape);
                    if (Math.Abs(centreX - expectedX) > 1.0) { Console.WriteLine("  ⛔ центр по x не на энергии опоры"); bad++; }
                    if (Math.Abs(maxY - expectedBottom) > 1) { Console.WriteLine("  ⛔ низ метки не в 4 px над верхом стека"); bad++; }
                    if (minY < 0) { Console.WriteLine("  ⛔ метка вылезла за верх поля"); bad++; }
                    if (blue * 5 < ink) { Console.WriteLine("  ⛔ синих точек меньше пятой части — кромка съела заливку, метка не синяя"); bad++; }
                    if (expectTriangle && !looksTriangle) { Console.WriteLine("  ⛔ порча nofont НЕ дала запасной треугольник"); bad++; }
                    if (!expectTriangle && !looksGlyph) { Console.WriteLine("  ⛔ без порчи метка — не якорь высотой ≈" + heightPx.ToString("F0", CultureInfo.InvariantCulture)); bad++; }
                }

                // Вне окон принятых опор не должно быть ни одной точки: отвергнутая
                // (500 кэВ) не рисуется, лишнего нет.
                int stray = 0;
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        bool inside = false;
                        foreach (Rectangle win in windows)
                        {
                            if (x >= win.Left && x < win.Right) { inside = true; break; }
                        }
                        if (inside) continue;
                        Color c = image.GetPixel(x, y);
                        if (c.R != 0 || c.G != 0 || c.B != 0) stray++;
                    }
                }
                Console.WriteLine("ВНЕ ОКОН\tточек={0}\t(отвергнутая опора 500 кэВ и всё прочее)", stray);
                if (stray != 0) { Console.WriteLine("  ⛔ нарисовано лишнее"); bad++; }
            }

            Console.WriteLine(bad == 0
                ? (expectTriangle ? "СОШЛОСЬ: порча nofont опознана — запасной треугольник на месте" : "СОШЛОСЬ: якорь U+2693 из шрифта, положение прежнее")
                : "РАСХОЖДЕНИЙ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        sealed class Anchor
        {
            public readonly double Kev;
            public readonly double Top;
            public readonly bool Used;
            public Anchor(double kev, double top, bool used) { this.Kev = kev; this.Top = top; this.Used = used; }
        }

        static bool Near(Color c, Color to, int tolerance)
        {
            return Math.Abs(c.R - to.R) <= tolerance && Math.Abs(c.G - to.G) <= tolerance && Math.Abs(c.B - to.B) <= tolerance;
        }

        static RectangleF FlatBounds(GraphicsPath path)
        {
            using (GraphicsPath flat = (GraphicsPath)path.Clone())
            {
                flat.Flatten();
                return flat.GetBounds();
            }
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null) return field;
            }
            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        static object Invoke(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null) throw new InvalidOperationException("нет метода " + name + " у " + target.GetType().Name);
            return method.Invoke(target, args);
        }
    }
}
