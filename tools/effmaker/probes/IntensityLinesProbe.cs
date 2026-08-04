using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace IntensityLinesProbe
{
    /// <summary>
    /// Вертикальные линии интенсивностей на графике спектра после переезда из
    /// наборов зон в наборы нуклидов.
    ///
    /// Рисуются они по ВЫБРАННОМУ НАБОРУ: энергия даёт положение, выход —
    /// высоту, цвет нуклида — цвет. Проверять это глазами дорого и ненадёжно:
    /// линия, уехавшая на десяток пикселей, на глаз неотличима от правильной, а
    /// пропавшая — от «в наборе её нет». Поэтому чертёж рисуется в картинку и
    /// разбирается по пикселям.
    ///
    ///     intensityprobe [куда положить png]
    ///
    /// Проверяется:
    ///
    /// 1. ПОЛОЖЕНИЕ И ВЫСОТА каждой линии против независимо посчитанных чисел;
    /// 2. ЦВЕТ — свой у каждого нуклида, а не общий на набор;
    /// 3. ОТБОР: погашенный нуклид, нуклид без выхода и нуклид чужого набора не
    ///    рисуются;
    /// 4. НАБОР НЕ ВЫБРАН либо у набора СНЯТА ГАЛКА «линии интенсивностей» —
    ///    не рисуется ничего. Набор выбирают ради поиска пиков, и частокол в
    ///    тридцать линий не обязан появляться заодно;
    /// 5. ЛИНИЯ ЗА КРАЕМ КАРТИНКИ не мешает следующим за ней.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        const int Width = 700;
        const int Height = 200;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            int bad = 0;
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();

            // Набор заводится СВОЙ, а не берётся из конфига: проба обязана
            // знать ожидаемые числа наперёд, а чужой набор меняется без неё.
            // В файл ничего не пишется — SaveDefinitionFile не зовётся.
            NuclideSet set = new NuclideSet
            {
                Id = Guid.NewGuid(), Name = "~проба", ShowIntensityLines = true
            };
            NuclideSet other = new NuclideSet { Id = Guid.NewGuid(), Name = "~проба-чужой" };
            manager.NuclideSets.Add(set);
            manager.NuclideSets.Add(other);

            NuclideDefinition strong = Line(manager, set, "Pr-100", 300.0, 100.0, Color.Red);
            NuclideDefinition half = Line(manager, set, "Pr-101", 100.0, 50.0, Color.Lime);
            NuclideDefinition weak = Line(manager, set, "Pr-102", 600.0, 25.0, Color.Blue);
            NuclideDefinition hidden = Line(manager, set, "Pr-103", 200.0, 80.0, Color.Magenta);
            hidden.Visible = false;
            Line(manager, set, "Pr-104", 400.0, 0.0, Color.Cyan);
            NuclideDefinition stranger = Line(manager, other, "Pr-105", 500.0, 90.0, Color.Yellow);
            // За правым краем картинки: раньше такая линия гасила все следующие.
            Line(manager, set, "Pr-106", 5000.0, 70.0, Color.Orange);
            NuclideDefinition afterFar = Line(manager, set, "Pr-107", 650.0, 10.0, Color.White);

            manager.ActiveNuclideSet = set;
            using (EnergySpectrumView view = Chart())
            using (Bitmap image = new Bitmap(Width, Height))
            {
                Draw(view, image);

                // Высота: 0.8 от поля на самом сильном выходе набора, дальше
                // пропорционально. Числа считаются здесь заново, а не берутся у
                // отрисовки, — иначе сверялась бы формула сама с собой.
                bad += Line(image, "Pr-100 300 кэВ, выход 100 %", 300, strong.NuclideColor.Color, 0.8);
                bad += Line(image, "Pr-101 100 кэВ, выход 50 %", 100, half.NuclideColor.Color, 0.4);
                bad += Line(image, "Pr-102 600 кэВ, выход 25 %", 600, weak.NuclideColor.Color, 0.2);
                bad += Line(image, "Pr-107 650 кэВ, после ушедшей за край", 650, afterFar.NuclideColor.Color, 0.08);

                bad += Missing(image, "погашенный нуклид не рисуется", 200, hidden.NuclideColor.Color);
                bad += Missing(image, "нуклид без выхода не рисуется", 400, Color.Cyan);
                bad += Missing(image, "нуклид чужого набора не рисуется", 500, stranger.NuclideColor.Color);

                if (args.Length > 0)
                {
                    image.Save(args[0], System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("  чертёж: {0}", args[0]);
                }
            }

            manager.ActiveNuclideSet = null;
            using (EnergySpectrumView view = Chart())
            using (Bitmap image = new Bitmap(Width, Height))
            {
                Draw(view, image);
                bad += Same("набор не выбран: линий нет", 0, Painted(image));
            }

            // Тот же набор с той же начинкой, но галка снята: набор выбирают
            // ради поиска пиков, и линии не обязаны появляться заодно.
            set.ShowIntensityLines = false;
            manager.ActiveNuclideSet = set;
            using (EnergySpectrumView view = Chart())
            using (Bitmap image = new Bitmap(Width, Height))
            {
                Draw(view, image);
                bad += Same("галка набора снята: линий нет", 0, Painted(image));
            }

            bad += CheckPersistence();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Галка живёт у набора и обязана пережить файл. Отдельно — файл БЕЗ
        /// неё: такие у всех, кто заводил наборы раньше, и читаться они должны
        /// как «линии не показывать», а не отказом.
        /// </summary>
        static int CheckPersistence()
        {
            Console.WriteLine();
            Console.WriteLine("=== хранение галки ===");
            int bad = 0;

            NuclideDefinitionFile file = new NuclideDefinitionFile();
            file.NuclideSets.Add(new NuclideSet
            {
                Id = Guid.NewGuid(), Name = "с линиями", ShowIntensityLines = true
            });
            file.NuclideSets.Add(new NuclideSet
            {
                Id = Guid.NewGuid(), Name = "без линий", ShowIntensityLines = false
            });

            System.Xml.Serialization.XmlSerializer serializer =
                new System.Xml.Serialization.XmlSerializer(typeof(NuclideDefinitionFile));
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            using (System.IO.StringWriter writer = new System.IO.StringWriter(text))
            {
                serializer.Serialize(writer, file);
            }

            NuclideDefinitionFile back;
            using (System.IO.StringReader reader = new System.IO.StringReader(text.ToString()))
            {
                back = (NuclideDefinitionFile)serializer.Deserialize(reader);
            }

            bad += Same("включённая пережила запись", true, back.NuclideSets[0].ShowIntensityLines);
            bad += Same("снятая пережила запись", false, back.NuclideSets[1].ShowIntensityLines);

            string legacy = text.ToString()
                .Replace("<ShowIntensityLines>true</ShowIntensityLines>", "")
                .Replace("<ShowIntensityLines>false</ShowIntensityLines>", "");
            NuclideDefinitionFile old;
            using (System.IO.StringReader reader = new System.IO.StringReader(legacy))
            {
                old = (NuclideDefinitionFile)serializer.Deserialize(reader);
            }

            bad += Same("старый файл: наборов", 2, old.NuclideSets.Count);
            bad += Same("старый файл: линии не показываются", false, old.NuclideSets[0].ShowIntensityLines);
            return bad;
        }

        static NuclideDefinition Line(NuclideDefinitionManager manager, NuclideSet set,
                                      string name, double energy, double intensity, Color color)
        {
            NuclideDefinition definition = new NuclideDefinition
            {
                Name = name,
                Energy = energy,
                Intencity = intensity,
                Visible = true,
                NuclideColor = new SerializableColor(color)
            };
            definition.Sets.Add(set.Id);
            manager.NuclideDefinitions.Add(definition);
            return definition;
        }

        /// <summary>
        /// График с плоской шкалой: энергия равна пикселю, поля нет, масштаб
        /// линейный. Так ожидаемые числа считаются в уме, и проба меряет
        /// отрисовку, а не арифметику прокрутки.
        /// </summary>
        static EnergySpectrumView Chart()
        {
            EnergySpectrumView view = new EnergySpectrumView();
            Set(view, "height", Height);
            Set(view, "left", 0);
            Set(view, "scrollX", 0);
            Set(view, "horizontalScale", 1.0);
            Set(view, "pixelPerEnergy", 1.0);
            Set(view, "energyViewOffset", 0.0);
            Set(view, "horizontalUnit", HorizontalUnit.Energy);
            Set(view, "verticalScaleType", VerticalScaleType.LinearScale);
            return view;
        }

        static void Draw(EnergySpectrumView view, Bitmap image)
        {
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                MethodInfo method = typeof(EnergySpectrumView).GetMethod("ShowNuclideSetIntensities",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method == null)
                {
                    throw new InvalidOperationException("нет EnergySpectrumView.ShowNuclideSetIntensities");
                }

                method.Invoke(view, new object[] { g });
            }
        }

        /// <summary>Верх линии в столбце: пикселей у пера два, ищем в трёх.</summary>
        static int Top(Bitmap image, int x, Color color)
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int column = x + dx;
                    if (column < 0 || column >= image.Width)
                    {
                        continue;
                    }

                    Color pixel = image.GetPixel(column, y);
                    if (pixel.R == color.R && pixel.G == color.G && pixel.B == color.B)
                    {
                        return y;
                    }
                }
            }

            return -1;
        }

        static int Line(Bitmap image, string what, int x, Color color, double share)
        {
            int top = Top(image, x, color);
            int expected = (int)((1.0 - share) * (Height - 1));
            bool ok = top >= 0 && Math.Abs(top - expected) <= 2;
            Console.WriteLine("  {0,-46} {1} верх {2}{3}", what, ok ? "=" : "!!", top,
                              ok ? "" : string.Format(" вместо {0}", expected));
            return ok ? 0 : 1;
        }

        static int Missing(Bitmap image, string what, int x, Color color)
        {
            int top = Top(image, x, color);
            bool ok = top < 0;
            Console.WriteLine("  {0,-46} {1}{2}", what, ok ? "=" : "!!",
                              ok ? "" : string.Format(" нарисован, верх {0}", top));
            return ok ? 0 : 1;
        }

        static int Painted(Bitmap image)
        {
            int count = 0;
            for (int x = 0; x < image.Width; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Color pixel = image.GetPixel(x, y);
                    if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                throw new InvalidOperationException("нет поля " + name);
            }

            field.SetValue(target, value);
        }

        static int Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-46} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0}", expected));
            return ok ? 0 : 1;
        }
    }
}
