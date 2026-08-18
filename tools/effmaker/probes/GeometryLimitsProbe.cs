using System;
using System.Collections.Generic;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// Связки размеров редактора геометрии — проверка счётом (`E33`).
    ///
    /// Зачем проба, а не «посмотреть в окне». Правило ловит невозможные сцены,
    /// которые расчёт прежде принимал МОЛЧА и доводил до конца, выдавая
    /// правдоподобную чужую кривую. Такую проверку глазами не подтвердить: надо
    /// показать, что на годной геометрии она молчит, а на каждой из четырёх
    /// поломок отзывается — и отзывается на ТО ЖЕ поле, за которое держится
    /// подсветка в окне.
    ///
    ///   geometrylimitsprobe
    /// </summary>
    static class GeometryLimitsProbe
    {
        static int failed;

        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("связки размеров геометрии (E33)");
            Console.WriteLine();

            GoodOnesAreSilent();
            HoleNarrowerThanProbe();
            RingIsGone();
            HoleDeeperThanSample();
            WallEatsClearance();
            BoxWallCountsTwice();
            BoreholeSceneIsConsistent();

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ"
                                          : "ПРОВАЛОВ: " + failed);
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------

        static void Check(string what, bool ok, string detail)
        {
            Console.WriteLine("   {0,-52} {1}{2}", what, ok ? "ок" : "ПРОВАЛ",
                              detail != null ? "  " + detail : "");
            if (!ok)
            {
                failed++;
            }
        }

        static List<GeometryScenes.Issue> Issues(GeometryModel g)
        {
            return GeometryScenes.Inconsistencies(g);
        }

        static bool Has(List<GeometryScenes.Issue> issues, string field, string resource)
        {
            foreach (GeometryScenes.Issue issue in issues)
            {
                if (issue.Field == field && issue.Resource == resource)
                {
                    return true;
                }
            }

            return false;
        }

        static string Names(List<GeometryScenes.Issue> issues)
        {
            if (issues.Count == 0)
            {
                return "(пусто)";
            }

            string s = "";
            foreach (GeometryScenes.Issue issue in issues)
            {
                s += (s.Length > 0 ? ", " : "") + issue.Field;
            }

            return s;
        }

        /// <summary>Маринелли, в котором всё сходится: Ø114 стакан, Ø70 колодец.</summary>
        static GeometryModel Marinelli()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            g.SourceType = GeometrySourceType.Marinelli;
            g.MarinelliBeakerDiameter = 114.0;
            g.MarinelliBeakerHeight = 120.0;
            g.MarinelliHoleDiameter = 70.0;
            g.MarinelliHoleHeight = 70.0;
            g.MarinelliSourceHeight = 100.0;
            g.MarinelliSideThickness = 2.0;
            g.MarinelliHoleSideThickness = 2.0;
            return g;
        }

        static GeometryModel Cylinder()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            g.SourceType = GeometrySourceType.Cylinder;
            g.BeakerDiameter = 40.0;
            g.BeakerHeight = 30.0;
            g.SourceHeight = 20.0;
            g.BeakerSideWallThickness = 1.0;
            return g;
        }

        // ------------------------------------------------------------------

        static void GoodOnesAreSilent()
        {
            Console.WriteLine("годная геометрия молчит:");
            Check("маринелли Ø114 / колодец Ø70", Issues(Marinelli()).Count == 0,
                  Names(Issues(Marinelli())));
            Check("цилиндр Ø40, стенка 1 мм", Issues(Cylinder()).Count == 0,
                  Names(Issues(Cylinder())));
            Console.WriteLine();
        }

        static void HoleNarrowerThanProbe()
        {
            Console.WriteLine("(а) колодец у́же прибора:");
            GeometryModel g = Marinelli();
            double outer = GeometryScenes.DetectorOuterDiameterMm(g);
            g.MarinelliHoleDiameter = outer - 5.0;
            Check("цилиндрический кристалл не лезет",
                  Has(Issues(g), "MarinelliHoleDiameter", "GeometryEditorErrorHoleNarrow"),
                  Names(Issues(g)));

            // Брусок входит в круглую лунку ДИАГОНАЛЬЮ: колодец, равный стороне,
            // выглядит достаточным и им не является.
            GeometryModel b = Marinelli();
            b.Shape = CrystalShape.Box;
            b.CrystalBoxX = 50.0;
            b.CrystalBoxY = 50.0;
            b.CrystalBoxZ = 50.0;
            b.SideReflectorThickness = 0.0;
            b.SideCladdingThickness = 0.0;
            b.MarinelliHoleDiameter = 55.0;
            Check("брусок 50x50 не лезет в Ø55 (диагональ 70.7)",
                  Has(Issues(b), "MarinelliHoleDiameter", "GeometryEditorErrorHoleNarrow"),
                  Names(Issues(b)));

            b.MarinelliHoleDiameter = 75.0;
            Check("он же лезет в Ø75",
                  !Has(Issues(b), "MarinelliHoleDiameter", "GeometryEditorErrorHoleNarrow"),
                  Names(Issues(b)));
            Console.WriteLine();
        }

        static void RingIsGone()
        {
            Console.WriteLine("(б) кольца пробы не остаётся:");
            GeometryModel g = Marinelli();
            g.MarinelliBeakerDiameter = g.MarinelliHoleDiameter + 2.0;
            Check("стакан почти равен колодцу",
                  Has(Issues(g), "MarinelliBeakerDiameter", "GeometryEditorErrorRingGone"),
                  Names(Issues(g)));

            GeometryModel w = Marinelli();
            w.MarinelliSideThickness = 22.0;
            Check("стенка стакана съела кольцо",
                  Has(Issues(w), "MarinelliBeakerDiameter", "GeometryEditorErrorRingGone"),
                  Names(Issues(w)));
            Console.WriteLine();
        }

        static void HoleDeeperThanSample()
        {
            Console.WriteLine("(в) колодец глубже пробы, проба выше стакана:");
            GeometryModel g = Marinelli();
            g.MarinelliHoleHeight = g.MarinelliSourceHeight + 10.0;
            Check("дно колодца ниже дна пробы",
                  Has(Issues(g), "MarinelliHoleHeight", "GeometryEditorErrorHoleDeeper"),
                  Names(Issues(g)));

            GeometryModel t = Marinelli();
            t.MarinelliSourceHeight = t.MarinelliBeakerHeight + 10.0;
            Check("проба выше стакана",
                  Has(Issues(t), "MarinelliSourceHeight", "GeometryEditorErrorSampleTaller"),
                  Names(Issues(t)));

            GeometryModel c = Cylinder();
            c.SourceHeight = c.BeakerHeight + 5.0;
            Check("то же у цилиндра",
                  Has(Issues(c), "SourceHeight", "GeometryEditorErrorSampleTaller"),
                  Names(Issues(c)));
            Console.WriteLine();
        }

        static void WallEatsClearance()
        {
            Console.WriteLine("(г) стенка съедает просвет:");
            GeometryModel g = Cylinder();
            g.BeakerSideWallThickness = 0.5 * g.BeakerDiameter;
            Check("стенка ровно в радиус — пробы нет",
                  Has(Issues(g), "BeakerSideWallThickness", "GeometryEditorErrorWallEatsSample"),
                  Names(Issues(g)));

            g.BeakerSideWallThickness = 0.5 * g.BeakerDiameter - 0.1;
            Check("на десятую меньше — проба есть",
                  Issues(g).Count == 0, Names(Issues(g)));
            Console.WriteLine();
        }

        static void BoxWallCountsTwice()
        {
            Console.WriteLine("(б) у короба стенка снимается с ДВУХ сторон:");
            GeometryModel g = GeometryEditorPanel.Blank();
            g.SourceType = GeometrySourceType.Box;
            g.BoxSourceX = 40.0;
            g.BoxSourceY = 40.0;
            g.BoxSourceHeight = 20.0;
            g.BoxSideWallThickness = 15.0;
            Check("40 мм при стенке 15 — просвета 10, но 2x15 > 40? нет",
                  Issues(g).Count == 0, Names(Issues(g)));

            g.BoxSideWallThickness = 20.0;
            Check("стенка 20 при стороне 40 — просвета нет",
                  Has(Issues(g), "BoxSideWallThickness", "GeometryEditorErrorWallEatsSample"),
                  Names(Issues(g)));
            Console.WriteLine();
        }

        /// <summary>
        /// Сцена «в лунке» строится формулой из того же поперечника, что
        /// проверяет правило, — значит она обязана проходить проверку сама.
        /// Иначе редактор отказывался бы сохранять то, что сам и посчитал.
        /// </summary>
        static void BoreholeSceneIsConsistent()
        {
            Console.WriteLine("сцена, посчитанная формулой, проверку проходит:");
            foreach (double density in new[] { 1.2, 1.5, 1.8 })
            {
                GeometryModel g = GeometryEditorPanel.Blank();
                g.SourceType = GeometrySourceType.Marinelli;
                g.Scene = GeometrySceneKind.Borehole;
                g.Source = GeometryMaterialLibrary.Make(GeometryMaterialLibrary.ByName("Soil"), density);
                GeometryScenes.Apply(g, 2614.0);
                Check(string.Format("лунка, грунт {0:F1} г/см3", density),
                      Issues(g).Count == 0, Names(Issues(g)));

                GeometryModel ground = GeometryEditorPanel.Blank();
                ground.SourceType = GeometrySourceType.Cylinder;
                ground.Scene = GeometrySceneKind.Ground;
                ground.Source = GeometryMaterialLibrary.Make(GeometryMaterialLibrary.ByName("Soil"), density);
                GeometryScenes.Apply(ground, 2614.0);
                Check(string.Format("на земле, грунт {0:F1} г/см3", density),
                      Issues(ground).Count == 0, Names(Issues(ground)));
            }
        }
    }
}
