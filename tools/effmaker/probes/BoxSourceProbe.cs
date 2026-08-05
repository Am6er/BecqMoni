using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;

namespace BoxSourceProbe
{
    /// <summary>
    /// Базовые проверки прямоугольной кюветы — источника, добавленного рядом с
    /// точкой, цилиндром и маринелли.
    ///
    /// Проверяется то, чего не видит компилятор:
    ///
    /// 1. **Круговорот через файл.** `GeometryWriter` пишет ключи `SB_*`, а
    ///    `GeometryModel.Load` их читает. Это наше расширение формата, ЛСРМ их
    ///    не знает, и ошибка в имени ключа не всплывёт нигде: поле просто
    ///    останется нулём, а кювета молча выродится в плоскую.
    /// 2. **Предел точечного источника.** Кювета, стянутая почти в точку и
    ///    отнесённая далеко, обязана дать ту же эффективность, что точечный
    ///    источник на том же расстоянии. Если розыгрыш точки внутри кюветы или
    ///    сборка сцены перепутали половину стороны с полной, расхождение видно
    ///    сразу.
    /// 3. **Равенство площадей.** Кювета и цилиндр с одинаковой площадью дна,
    ///    высотой и расстоянием обязаны дать почти одно и то же: телесный угол
    ///    у них разный лишь на углах. Это проверка сборки областей, а не
    ///    физики.
    ///
    /// Полная проверка на массовых прогонах — отдельная задача, здесь только
    /// базовая.
    ///
    ///     boxsourceprobe [--n=200000]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            int n = 200000;
            foreach (string a in args)
            {
                if (a.StartsWith("--n="))
                {
                    n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                }
            }

            int bad = 0;
            bad += RoundTrip();
            bad += PointLimit(n);
            bad += EqualArea(n);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Геометрия-образец: кристалл маленький, вся суть в источнике.</summary>
        static GeometryModel Base()
        {
            GeometryModel g = new GeometryModel
            {
                Name = "boxprobe",
                CrystalDiameter = 20.0,
                CrystalHeight = 20.0,
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.0,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                MountingThickness = 1.0,
            };

            g.Crystal = Material("CsI");
            g.Reflector = Material("PTFE");
            g.Cladding = Material("Al");
            g.BeakerWall = Material("PP");
            g.Source = Material("H2O");
            return g;
        }

        static GeometryMaterial Material(string abbr)
        {
            foreach (GeometryMaterialLibrary.MaterialKind kind
                     in Enum.GetValues(typeof(GeometryMaterialLibrary.MaterialKind)))
            {
                foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialLibrary.Of(kind))
                {
                    if (string.Equals(entry.Abbr, abbr, StringComparison.OrdinalIgnoreCase))
                    {
                        return GeometryMaterialLibrary.Make(entry, entry.Density);
                    }
                }
            }

            throw new ArgumentException("нет вещества " + abbr);
        }

        static int RoundTrip()
        {
            Console.WriteLine("1. Круговорот через файл");
            GeometryModel g = Base();
            g.SourceType = GeometrySourceType.Box;
            g.BoxSourceX = 71.0;
            g.BoxSourceY = 43.0;
            g.BoxSourceHeight = 17.0;
            g.BoxToDetectorDistance = 23.0;
            g.BoxSideWallThickness = 1.5;
            g.BoxEndWallThickness = 2.5;

            string path = Path.Combine(Path.GetTempPath(), "boxprobe.in");
            File.WriteAllText(path, GeometryWriter.Render(g));
            GeometryModel back = GeometryModel.Load(path);

            int bad = 0;
            bad += Same("SourceType", (double)g.SourceType, (double)back.SourceType);
            bad += Same("BoxSourceX", g.BoxSourceX, back.BoxSourceX);
            bad += Same("BoxSourceY", g.BoxSourceY, back.BoxSourceY);
            bad += Same("BoxSourceHeight", g.BoxSourceHeight, back.BoxSourceHeight);
            bad += Same("BoxToDetectorDistance", g.BoxToDetectorDistance, back.BoxToDetectorDistance);
            bad += Same("BoxSideWallThickness", g.BoxSideWallThickness, back.BoxSideWallThickness);
            bad += Same("BoxEndWallThickness", g.BoxEndWallThickness, back.BoxEndWallThickness);
            return bad;
        }

        static int Same(string what, double a, double b)
        {
            bool ok = Math.Abs(a - b) <= 1e-6 * Math.Max(1.0, Math.Abs(a));
            Console.WriteLine("   {0,-24} {1,10:G6} -> {2,10:G6}  {3}",
                              what, a, b, ok ? "ок" : "РАЗЪЕХАЛОСЬ");
            return ok ? 0 : 1;
        }

        static int PointLimit(int n)
        {
            Console.WriteLine("2. Предел точечного источника (кювета 0.5 мм на 200 мм)");
            GeometryModel point = Base();
            point.SourceType = GeometrySourceType.Point;
            point.PointDistance = 200.0;

            GeometryModel box = Base();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = 0.5;
            box.BoxSourceY = 0.5;
            box.BoxSourceHeight = 0.5;
            box.BoxToDetectorDistance = 200.0;

            return Compare(point, box, n, 0.03);
        }

        static int EqualArea(int n)
        {
            Console.WriteLine("3. Кювета и цилиндр равной площади дна");
            GeometryModel cyl = Base();
            cyl.SourceType = GeometrySourceType.Cylinder;
            cyl.BeakerDiameter = 40.0;
            cyl.SourceHeight = 6.0;
            cyl.BeakerToDetectorDistance = 50.0;

            double side = 20.0 * Math.Sqrt(Math.PI);      // та же площадь, что у круга R = 20
            GeometryModel box = Base();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = side;
            box.BoxSourceY = side;
            box.BoxSourceHeight = 6.0;
            box.BoxToDetectorDistance = 50.0;

            return Compare(cyl, box, n, 0.05);
        }

        /// <summary>
        /// Сравнить две геометрии на нескольких энергиях. Допуск сравнивается со
        /// СТАТИСТИКОЙ обоих прогонов: требовать больше, чем даёт розыгрыш,
        /// значит поймать шум и назвать его дефектом.
        /// </summary>
        static int Compare(GeometryModel a, GeometryModel b, int n, double tolerance)
        {
            double[] energies = { 60.0, 200.0, 662.0, 1461.0 };
            EfficiencySimulator sa = new EfficiencySimulator(a) { Histories = n };
            EfficiencySimulator sb = new EfficiencySimulator(b) { Histories = n };
            int bad = 0;
            foreach (double e in energies)
            {
                double ea, eb, da, db;
                ea = sa.Efficiency(e, out da);
                eb = sb.Efficiency(e, out db);
                double ratio = ea > 0.0 ? eb / ea : 0.0;
                double noise = 0.01 * Math.Sqrt(da * da + db * db);
                bool ok = Math.Abs(ratio - 1.0) <= Math.Max(tolerance, 2.0 * noise);
                Console.WriteLine("   {0,6:F0} кэВ  {1:E3} / {2:E3} = {3:F3}  (шум {4:P1})  {5}",
                                  e, eb, ea, ratio, noise, ok ? "ок" : "РАСХОДИТСЯ");
                if (!ok)
                {
                    bad++;
                }
            }

            return bad;
        }
    }
}
