using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;

// E27: показать, ЧТО именно кладут в поля две готовые сцены съёмки в поле —
// «Детектор на земле» и «Детектор в лунке» — и сверить это с расчётом,
// которым коэффициенты получены.
//
// Зачем проба. Формулы стоят в приложении (`GeometryScenes`), а
// коэффициенты в них получены питоном (`tools/effmaker/ground_halfspace.py`,
// `tools/effmaker/borehole.py`). Два набора чисел в двух языках расходятся при
// первой же правке молча, и заметить это будет не на чем: сцена не «падает»,
// она просто перестаёт держать 98 %.
//
// Числа здесь НЕ набираются руками: детектор берётся у `GeometryPresets`,
// заготовка — у `GeometryEditorPanel.Blank()`, сцена — у
// `GeometryScenes`. Печатается то, что увидит человек в полях.
//
//   groundscenesprobe [--energy=3000] [--detector=«имя пресета»]
class GroundScenesProbe
{
    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        double energy = 3000.0;
        string only = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--energy=", StringComparison.Ordinal))
                energy = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--detector=", StringComparison.Ordinal)) only = a.Substring(11);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        // Менеджеры нужны библиотеке веществ: пресеты зовут
        // GeometryMaterialLibrary, а она читает matdb и файл пользователя.
        GlobalConfigManager.GetInstance();

        if (GeometryMaterialLibrary.ByName(GeometryScenes.DefaultSampleMaterial) == null)
        {
            Console.Error.WriteLine("в библиотеке нет вещества «"
                                    + GeometryScenes.DefaultSampleMaterial
                                    + "» — сцену считать не по чему");
            return 1;
        }

        Console.WriteLine("верхняя энергия расчёта: {0:F0} кэВ", energy);
        Console.WriteLine();

        foreach (GeometryPresets.Preset detector in GeometryPresets.Items)
        {
            if (only != null && detector.Name != only)
            {
                continue;
            }

            GeometryModel g = GeometryEditorPanel.Blank();
            detector.Apply(g);
            double outer = GeometryScenes.DetectorOuterDiameterMm(g);
            double height = GeometryScenes.CrystalHeightAboveSampleMm(g);

            Console.WriteLine("=== {0}", detector.Name);
            Console.WriteLine("    поперечник {0:F1} мм, середина кристалла над пробой {1:F1} мм",
                              outer, height);

            GeometryModel ground = g.Clone();
            string set1 = GeometryScenes.Ground(ground, energy);
            double mfp = GeometryScenes.MeanFreePathMm(ground.Source, energy);
            Console.WriteLine("    проба: {0} {1:F2} г/см3, свободный пробег {2:F1} мм{3}",
                              ground.Source.Name, ground.Source.Density, mfp,
                              set1.Length > 0 ? "  <- подставлено" : "");
            Console.WriteLine("    НА ЗЕМЛЕ : Ø{0:F0} мм (радиус {1:F2} пробега), глубина {2:F0} мм"
                              + " ({3:F2} пробега), зазор {4:F0}, стенки {5:F0}/{6:F0}, объём {7:F0} л",
                              ground.BeakerDiameter, 0.5 * ground.BeakerDiameter / mfp,
                              ground.SourceHeight, ground.SourceHeight / mfp,
                              ground.BeakerToDetectorDistance,
                              ground.BeakerSideWallThickness, ground.BeakerEndWallThickness,
                              GeometryScenes.SampleVolumeCm3(ground) / 1000.0);
            Console.WriteLine("               радиус = {0:F1}·{1:F1} пробега + {2:F0}·{3:F1} мм высоты"
                              + " = {4:F0} мм",
                              GeometryScenes.GroundRadiusMfp, mfp,
                              GeometryScenes.GroundRadiusPerHeight, height,
                              0.5 * ground.BeakerDiameter);

            GeometryModel hole = g.Clone();
            GeometryScenes.Borehole(hole, energy);
            Console.WriteLine("    В ЛУНКЕ  : лунка Ø{0:F0} мм × {1:F0} мм ({2:F2} L),"
                              + " сцена Ø{3:F0} мм, проба {4:F0} мм ({5:F2} L),"
                              + " нос над дном {6:F0}, объём {7:F0} л",
                              hole.MarinelliHoleDiameter, hole.MarinelliHoleHeight,
                              hole.MarinelliHoleHeight / mfp, hole.MarinelliBeakerDiameter,
                              hole.MarinelliSourceHeight, hole.MarinelliSourceHeight / mfp,
                              hole.MarinelliToDetectorDistance,
                              GeometryScenes.SampleVolumeCm3(hole) / 1000.0);
            Console.WriteLine("               грунт от стенки лунки {0:F2} пробега,"
                              + " под дном {1:F2} пробега",
                              0.5 * (hole.MarinelliBeakerDiameter - hole.MarinelliHoleDiameter) / mfp,
                              (hole.MarinelliSourceHeight - hole.MarinelliHoleHeight) / mfp);

            // Сверка с тем, ради чего сцена и строилась: коэффициенты должны
            // остаться теми, которыми считался охват. Расходится — значит,
            // формулу правили, а расчёт нет.
            Check("глубина на земле", ground.SourceHeight / mfp, GeometryScenes.GroundDepthMfp);
            Check("глубина лунки", hole.MarinelliHoleHeight / mfp, GeometryScenes.BoreholeDepthMfp);
            Check("грунт от стенки",
                  0.5 * (hole.MarinelliBeakerDiameter - hole.MarinelliHoleDiameter) / mfp,
                  GeometryScenes.BoreholeSideMfp);
            Check("грунт под дном",
                  (hole.MarinelliSourceHeight - hole.MarinelliHoleHeight) / mfp,
                  GeometryScenes.BoreholeBottomMfp);
            Console.WriteLine();
        }

        return failed;
    }

    static int failed;

    static void Check(string what, double got, double want)
    {
        if (Math.Abs(got - want) > 1e-6 * Math.Max(1.0, want))
        {
            Console.WriteLine("    ⛔ {0}: {1:F4} пробега вместо {2:F4}", what, got, want);
            failed = 1;
        }
    }
}
